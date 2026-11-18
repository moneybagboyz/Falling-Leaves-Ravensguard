using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Economy.Markets;

/// <summary>Who a trader is: a purse and a store that belong to one record (a household, a business, a merchant's own business).</summary>
public sealed class MarketParty(EntityId id, PurseId purse, Store store, PartyKind kind, int index)
{
    public EntityId Id { get; } = id;
    public PurseId Purse { get; } = purse;
    public Store Store { get; } = store;
    public PartyKind Kind { get; } = kind;
    /// <summary>Household or business table index (the order orders are processed in).</summary>
    public int Index { get; } = index;
    public override string ToString() => $"{Kind} {Id}";
}

public enum PartyKind : byte { Household, Business, Merchant }

/// <summary>A listed lot (money spec 2.4): goods that sit in the book's store on behalf of their owner until sold or expired.</summary>
public sealed class StockLot
{
    public uint Id;
    public MarketParty Owner = null!;
    public ushort Good;
    public long Qty;
    /// <summary>Lowest mid, in permille of Ref, at which the owner will sell.</summary>
    public int FloorPermille;
    public int ListedDay;
    public bool IsMerchant;
}

/// <summary>Price state of one good in one book (money spec 2.4). Prices are micro-mites per unit (gram, or piece).</summary>
public sealed class GoodBook
{
    public long RefMicro, BaseRefMicro;
    public long DemandEma, StructuralDemand, FloorDemand;
    public long StockUnits;
    public int TargetCoverDays;
    public int LastTradedDay = -1;
    public long DemandToday, TradedToday;
    public List<StockLot> Lots { get; } = [];

    /// <summary>
    /// Demand in units per day: the largest of structural, EMA and floor (money spec 3.1), at least 1. Where the goods have a structural demand (the baskets and the
    /// planned inputs, which are exact) the EMA of requests counts at most that much: unfilled orders are re-posted every day and would otherwise count
    /// 10 to 20 days of need again each day, driving a shortage's price up on its own (integration decision I4).
    /// </summary>
    public long Demand => Math.Max(1, StructuralDemand > 0 ? Math.Max(StructuralDemand, Math.Max(Math.Min(DemandEma, StructuralDemand * EmaCapOverStructural), FloorDemand))
                                                          : Math.Max(DemandEma, FloorDemand));
    public const int EmaCapOverStructural = 2;
}

/// <summary>The outcome of a buy: units bought, coin the buyer paid, toll the sellers paid onward.</summary>
public readonly record struct TradeResult(long Qty, long PaidMites, long TollMites)
{
    public static readonly TradeResult None = new(0, 0, 0);
}

/// <summary>An order to buy (money spec 3.4): filled in a session in a fixed order, partly if need be, and then dropped.</summary>
public sealed record BuyOrder(MarketParty Buyer, ushort Good, long Qty, int MaxPriceRatioPermille)
{
    /// <summary>Lines of one buyer are filled in ascending priority (staples before ale).</summary>
    public int Priority { get; init; }
    /// <summary>Called with the result when the order is filled (businesses book their input spend).</summary>
    public Action<TradeResult>? OnFilled { get; init; }
    /// <summary>What <see cref="OnFilled"/> is, so a pending order can be saved and its callback rebuilt: 0 none, 1 a business's input spend (arg: business index), 2 a household's durable (arg: household x durables + kind), 3 / 4 a household's spending on craft goods / building materials (arg: household; decision I37).</summary>
    public byte HookKind { get; init; }
    public int HookArg { get; init; }
}

/// <summary>
/// The market of one settlement (money spec 2.4, 3.1-3.4): stock lots owned by real parties, price state per good, and trades that move coin through the
/// <see cref="Ledger"/> and goods between <see cref="Store"/>s. Customers pay <c>ask = mid x (1 + h)</c>; a seller to a merchant gets <c>bid = mid x (1 - h)</c>;
/// the seller pays the market toll to the market owner's treasury. Nothing is created: coin and goods only move.
/// </summary>
public sealed class MarketBook
{
    public int SettlementIndex { get; }
    public EntityId SettlementId { get; }
    public string Name { get; }
    public EconomyState Econ { get; }
    public MoneySettingsDef Money { get; }
    public Store BookStore { get; }
    public PurseId TollPurse { get; }
    public int Population { get; set; }
    public ulong Seed { get; }
    public GoodBook[] Goods { get; }
    private uint _nextLot = 1;
    private readonly List<BuyOrder> _orders = [];
    public List<MarketParty> Merchants { get; } = [];
    public List<ImportRequest> ImportRequests { get; } = [];
    /// <summary>Open minutes: sessions run at hours open..close-1.</summary>
    public bool IsOpen(int hour) => hour >= Money.MarketOpenHour && hour < Money.MarketCloseHour;

    public MarketBook(EconomyState econ, int settlementIndex, EntityId settlementId, string name, PurseId tollPurse, int population, ulong seed)
    {
        Econ = econ; Money = econ.Money; SettlementIndex = settlementIndex; SettlementId = settlementId; Name = name; TollPurse = tollPurse; Population = population; Seed = seed;
        BookStore = econ.OpenStore(StoreKind.Granary, EconIds.MarketStore(settlementIndex), 0, 0, enforce: false);   // the market hall is a warehouse (decision I28): grain keeps as in a granary
        var cat = econ.Goods;
        Goods = new GoodBook[cat.Count];
        for (ushort g = 0; g < cat.Count; g++)
        {
            // kg and litre goods: milli-mites per kg is micro-mites per gram; piece goods: milli-mites per piece x 1000 is micro-mites per piece
            long micro = cat.IsPiece(g) ? cat.BasePriceMilliMites(g) * 1000 : cat.BasePriceMilliMites(g);
            var d = cat.Def(g);
            Goods[g] = new GoodBook { RefMicro = micro, BaseRefMicro = micro, TargetCoverDays = d.TargetCoverDays > 0 ? d.TargetCoverDays : 30 };
        }
    }

    // ---------- curve and quotes ----------

    public PriceCurve Curve(ushort good)
    {
        var gb = Goods[good];
        return new PriceCurve(gb.RefMicro, gb.Demand * gb.TargetCoverDays, Money.PriceClampMinPermille, Money.PriceClampMaxPermille);
    }

    /// <summary>Mid price in micro-mites per unit at the current stock.</summary>
    public long MidMicro(ushort good) => Curve(good).MidMicro(Goods[good].StockUnits);
    /// <summary>Mid price in mites per kg (per piece for piece goods) as a double, for display and tests.</summary>
    public double MidMitesPerKg(ushort good) => MidMicro(good) * (Econ.Goods.IsPiece(good) ? 1.0 / PriceCurve.Micro : 1000.0 / PriceCurve.Micro);
    public double RefMitesPerKg(ushort good) => Goods[good].RefMicro * (Econ.Goods.IsPiece(good) ? 1.0 / PriceCurve.Micro : 1000.0 / PriceCurve.Micro);
    /// <summary>Current mid as a multiple of Ref in permille.</summary>
    public int MidRatioPermille(ushort good) => Curve(good).MultPermille(Goods[good].StockUnits);
    public long AskMicro(ushort good) => MidMicro(good) * (1000 + Money.SpreadHalfPermille) / 1000;
    public long BidMicro(ushort good) => MidMicro(good) * (1000 - Money.SpreadHalfPermille) / 1000;
    /// <summary>Days of cover on the book now: stock over demand.</summary>
    public double CoverDays(ushort good) => (double)Goods[good].StockUnits / Goods[good].Demand;

    /// <summary>What buying <paramref name="qty"/> now would cost the customer in mites (ask side), ignoring floors; 0 when the book holds less.</summary>
    public long QuoteBuy(ushort good, long qty)
    {
        var gb = Goods[good]; if (qty <= 0 || qty > gb.StockUnits) return 0;
        return Round(Curve(good).BuyMid(gb.StockUnits, qty), 1000 + Money.SpreadHalfPermille);
    }

    /// <summary>What selling <paramref name="qty"/> to a merchant now would pay in mites (bid side).</summary>
    public long QuoteSell(ushort good, long qty) => qty <= 0 ? 0 : Round(Curve(good).SellMid(Goods[good].StockUnits, qty), 1000 - Money.SpreadHalfPermille);

    private static long Round(long midMites, int permille) => (long)(((Int128)midMites * permille + 500) / 1000);

    // ---------- spoilage sync ----------

    /// <summary>
    /// Goods in the book store can spoil like any stock; the lots lose the same quantity, newest lots first, so the lots always add up to the store.
    /// </summary>
    public void Sync(ushort good)
    {
        var gb = Goods[good]; long have = BookStore.Qty(good);
        if (have >= gb.StockUnits) return;
        long excess = gb.StockUnits - have;
        for (int i = gb.Lots.Count - 1; i >= 0 && excess > 0; i--)
        {
            var l = gb.Lots[i]; long cut = Math.Min(l.Qty, excess);
            l.Qty -= cut; excess -= cut; gb.StockUnits -= cut;
            if (l.Qty == 0) gb.Lots.RemoveAt(i);
        }
    }

    public void SyncAll() { for (ushort g = 0; g < Goods.Length; g++) if (Goods[g].StockUnits > 0) Sync(g); }

    // ---------- listing ----------

    /// <summary>Moves goods from the seller's store onto the book as a lot (floor in permille of Ref). Returns the quantity listed (all of it unless the store holds less).</summary>
    public long List(MarketParty seller, ushort good, long qty, int floorPermille, int day, bool merchantLot = false)
    {
        qty = Math.Min(qty, seller.Store.Qty(good));
        if (qty <= 0) return 0;
        if (!Store.Move(seller.Store, BookStore, good, qty)) return 0;
        AddLot(seller, good, qty, floorPermille, day, merchantLot);
        return qty;
    }

    private void AddLot(MarketParty owner, ushort good, long qty, int floor, int day, bool merchant)
    {
        var gb = Goods[good];
        // one lot per owner and good (decision I40): a merchant's at the same floor; a producer's new goods join the lot it already has on the book, at that lot's
        // floor and listing day (they come down and go home with it). A household that sold every day had a lot for each day, and the books carried tens of
        // thousands of lots that every sale walked through
        foreach (var l in gb.Lots)
            if (l.Owner.Id == owner.Id && l.IsMerchant == merchant && (!merchant || l.FloorPermille == floor)) { l.Qty += qty; gb.StockUnits += qty; return; }
        var lot = new StockLot { Id = _nextLot++, Owner = owner, Good = good, Qty = qty, FloorPermille = floor, ListedDay = day, IsMerchant = merchant };
        int pos = gb.Lots.Count;
        while (pos > 0 && (gb.Lots[pos - 1].FloorPermille > floor)) pos--;
        gb.Lots.Insert(pos, lot);
        gb.StockUnits += qty;
    }

    /// <summary>Goods that lose this much a day in a market hall (bread, fresh fish and meat, milk) are sold by their makers only, never to a merchant's stock.</summary>
    public const int PerishablePpmPerDay = 100_000;

    /// <summary>The lowest floor a merchant's lot comes down to (integration decision I36; was 750): in a glut the merchant sells at a loss rather than sit on the stock with his cash gone.</summary>
    public const int MerchantFloorMinPermille = 400;

    /// <summary>Returns lots listed more than the expiry days ago (non-merchant lots only) to their owners' stores.</summary>
    public long ExpireLots(int day)
    {
        SyncAll();
        // a producer's lot that did not sell comes down: its floor falls by a third a day it stays on the book, so a glut cannot jam the market (a lot priced
        // above the curve would never sell and the stock would never fall, integration decision I21)
        for (int g = 0; g < Goods.Length; g++)
        {
            var gl = Goods[g].Lots; bool changed = false;
            foreach (var l in gl)
            {
                if (!l.IsMerchant && l.FloorPermille > 300 && l.ListedDay < day) { l.FloorPermille = Math.Max(300, l.FloorPermille * 2 / 3); changed = true; }
                else if (l.IsMerchant && l.FloorPermille > MerchantFloorMinPermille && day - l.ListedDay > 5) { l.FloorPermille = Math.Max(MerchantFloorMinPermille, l.FloorPermille * 9 / 10); changed = true; }   // a merchant sells stock that will not move at a smaller margin, at a loss in the end (decision I36)
            }
            if (changed) gl.Sort((a, b) => a.FloorPermille != b.FloorPermille ? a.FloorPermille.CompareTo(b.FloorPermille) : a.Id.CompareTo(b.Id));
        }
        long returned = 0;
        for (int g = 0; g < Goods.Length; g++)
        {
            var gb = Goods[g];
            for (int i = gb.Lots.Count - 1; i >= 0; i--)
            {
                var l = gb.Lots[i];
                if (l.IsMerchant || day - l.ListedDay < Money.LotExpiryDays) continue;
                if (Store.Move(BookStore, l.Owner.Store, l.Good, l.Qty)) { gb.StockUnits -= l.Qty; returned += l.Qty; gb.Lots.RemoveAt(i); }
            }
        }
        return returned;
    }

    // ---------- buying ----------

    /// <summary>Largest quantity the lots would let a buyer take, honouring lot floors and a price ratio cap (units).</summary>
    public long Eligible(ushort good, long wanted, int maxRatioPermille)
    {
        var gb = Goods[good]; var curve = Curve(good);
        long cur = gb.StockUnits, taken = 0;
        long stockFloor = maxRatioPermille >= Money.PriceClampMaxPermille ? 0 : (long)((Int128)curve.TargetUnits * 1_000_000 / ((long)maxRatioPermille * maxRatioPermille));
        foreach (var lot in gb.Lots)
        {
            if (taken >= wanted) break;
            int ratio = curve.MultPermille(cur);
            if (lot.FloorPermille > ratio) break;
            long room = Math.Max(0, cur - stockFloor);
            long take = Math.Min(Math.Min(lot.Qty, wanted - taken), room);
            if (take <= 0) break;
            taken += take; cur -= take;
        }
        return taken;
    }

    /// <summary>
    /// Buys up to <paramref name="wanted"/> units for the buyer at the exact per-unit integral price (money spec 3.2, 3.3). Walks lots in ascending
    /// (floor, id) while their floor is within the current mid; pays each owner their slice and the owner pays the toll. Partial fills are normal.
    /// </summary>
    public TradeResult Buy(MarketParty buyer, ushort good, long wanted, int maxRatioPermille, int day)
    {
        Sync(good);
        var gb = Goods[good];
        if (wanted > 0) gb.DemandToday += wanted;
        if (wanted <= 0 || gb.StockUnits <= 0) return TradeResult.None;
        var curve = Curve(good);
        long budget = Econ.Ledger.Balance(buyer.Purse).Mites;
        long cap = Eligible(good, wanted, maxRatioPermille);
        // lots owned by the buyer cannot be bought from itself
        long q = curve.MaxAffordable(gb.StockUnits, cap, budget, Money.SpreadHalfPermille);
        if (q <= 0) return TradeResult.None;
        long s0 = gb.StockUnits, left = q, taken = 0, paidSoFar = 0, tollTotal = 0;
        var done = new List<StockLot>();
        long prevGross = 0;
        foreach (var lot in gb.Lots.ToArray())
        {
            if (left <= 0) break;
            long take = Math.Min(lot.Qty, left);
            taken += take; left -= take;
            long cumMid = curve.BuyMid(s0, taken);
            long cumGross = Round(cumMid, 1000 + Money.SpreadHalfPermille);
            long gross = cumGross - prevGross; prevGross = cumGross;
            if (!Store.Move(BookStore, buyer.Store, good, take)) throw new InvalidOperationException("book store lost goods");
            lot.Qty -= take; gb.StockUnits -= take;
            if (lot.Qty == 0) gb.Lots.Remove(lot);
            if (gross > 0 && lot.Owner.Purse != buyer.Purse)
            {
                if (!Econ.Ledger.Transfer(buyer.Purse, lot.Owner.Purse, new Coin(gross), TransferReason.Trade)) throw new InvalidOperationException("buyer could not pay a quote it could afford");
                long toll = (long)(((Int128)gross * Money.MarketTollPermille + 500) / 1000);
                if (toll > 0 && Econ.Ledger.Transfer(lot.Owner.Purse, TollPurse, new Coin(toll), TransferReason.MarketToll)) tollTotal += toll;
            }
            else if (gross > 0) { /* own lot: pay nothing, coin stays put */ }
            paidSoFar += gross;
        }
        gb.LastTradedDay = day; gb.TradedToday += q;
        return new TradeResult(q, paidSoFar, tollTotal);
    }

    // ---------- selling to merchants ----------

    /// <summary>
    /// A producer sells to the book's merchants in id order at the bid (money spec 3.4): each takes what its cash and its stock cap (0.8 T cover) allow. The goods
    /// become the merchant's lot at floor 1.0. Returns the units sold and what the seller received.
    /// </summary>
    public TradeResult SellToMerchants(MarketParty seller, ushort good, long qty, int day)
    {
        if (qty <= 0 || Merchants.Count == 0) return TradeResult.None;
        if (Econ.Goods.SpoilPpm(good, StoreKind.Granary) >= PerishablePpmPerDay) return TradeResult.None;   // no merchant buys bread or fresh meat to hold (decision I40)
        Sync(good);
        var gb = Goods[good];
        qty = Math.Min(qty, seller.Store.Qty(good));
        long soldTotal = 0, proceedsTotal = 0, tollTotal = 0;
        foreach (var m in Merchants)
        {
            if (qty - soldTotal <= 0) break;
            if (m.Id == seller.Id) continue;
            var curve = Curve(good);
            long capUnits = gb.Demand * gb.TargetCoverDays * Money.MerchantStockCapPermilleOfCover / 1000;
            long holds = 0; foreach (var l in gb.Lots) if (l.IsMerchant) holds += l.Qty;      // the book's merchants together hold at most 0.8 of the cover (a stock the price curve puts under the lots' floor jams the book, decision I21)
            long room = Math.Max(0, capUnits - holds);
            long cash = Econ.Ledger.Balance(m.Purse).Mites;
            long q = curve.MaxSellable(gb.StockUnits, Math.Min(qty - soldTotal, room), cash, Money.SpreadHalfPermille);
            if (q <= 0) continue;
            long proceeds = Round(curve.SellMid(gb.StockUnits, q), 1000 - Money.SpreadHalfPermille);
            if (proceeds > 0 && !Econ.Ledger.Transfer(m.Purse, seller.Purse, new Coin(proceeds), TransferReason.Trade)) continue;
            if (!Store.Move(seller.Store, BookStore, good, q)) throw new InvalidOperationException("seller lost goods");
            AddLot(m, good, q, 1000, day, true);
            long toll = (long)(((Int128)proceeds * Money.MarketTollPermille + 500) / 1000);
            if (toll > 0 && Econ.Ledger.Transfer(seller.Purse, TollPurse, new Coin(toll), TransferReason.MarketToll)) tollTotal += toll;
            soldTotal += q; proceedsTotal += proceeds;
        }
        if (soldTotal > 0) gb.LastTradedDay = day;
        return new TradeResult(soldTotal, proceedsTotal, tollTotal);
    }

    /// <summary>Sells to merchants first, lists the rest as the seller's own lot at the given floor (production spec 3.2, money spec 3.4). Returns the units that left the seller's store.</summary>
    public long SellOrList(MarketParty seller, ushort good, long qty, int floorPermille, int day)
    {
        qty = Math.Min(qty, seller.Store.Qty(good));
        if (qty <= 0) return 0;
        long sold = SellToMerchants(seller, good, qty, day).Qty;
        long listed = qty - sold > 0 ? List(seller, good, qty - sold, floorPermille, day) : 0;
        return sold + listed;
    }

    // ---------- orders and sessions ----------

    public void Post(BuyOrder order) { if (order.Qty > 0) _orders.Add(order); }
    public int PendingOrders => _orders.Count;
    /// <summary>The orders posted and not yet filled, in posting order (saved with the markets: an order posted in one hour is filled by the next session, which may be hours away).</summary>
    public IReadOnlyList<BuyOrder> Orders => _orders;
    public void ClearOrders() => _orders.Clear();

    /// <summary>
    /// One session (money spec 3.6): fills the posted orders in an order that depends only on the parties (a hash of the hour and the party id, then the id),
    /// never on posting order; partial fills allowed; what is left lapses.
    /// </summary>
    public (int Filled, long Mites) RunSession(long hourKey, int day)
    {
        if (_orders.Count == 0) return (0, 0);
        var sorted = _orders.Select(o => (o, key: DetRng.Next(Seed, hourKey, o.Buyer.Id.Value, 0x220, 0))).OrderBy(x => x.key).ThenBy(x => x.o.Buyer.Id.Value).ThenBy(x => x.o.Priority).ThenBy(x => x.o.Good).ToList();
        _orders.Clear();
        int filled = 0; long mites = 0;
        foreach (var (o, _) in sorted)
        {
            var r = Buy(o.Buyer, o.Good, o.Qty, o.MaxPriceRatioPermille, day);
            if (r.Qty > 0) { filled++; mites += r.PaidMites; }
            o.OnFilled?.Invoke(r);
        }
        return (filled, mites);
    }

    // ---------- daily and seasonal ----------

    /// <summary>H5 daily globals (money spec 4): <c>ema += (observed - ema) / days</c> (floor), counters reset, lots expire.</summary>
    public void Daily(int day)
    {
        ExpireLots(day);
        for (int g = 0; g < Goods.Length; g++)
        {
            var gb = Goods[g];
            gb.DemandEma += FloorDiv(gb.DemandToday - gb.DemandEma, Math.Max(1, Money.DemandEmaDays));
            gb.DemandToday = 0; gb.TradedToday = 0;
        }
    }

    /// <summary>S5 price drift (money spec 3.5): <c>Ref += (base x clamp(cpp / seed_cpp, 0.5, 2) - Ref) / 4</c>, floor. Returns the target multiplier in permille.</summary>
    public int DriftRefs(long coinMitesInSettlement)
    {
        long cpp = Population > 0 ? coinMitesInSettlement / Population : Money.SeedCppMites;
        int mult = (int)Math.Clamp(cpp * 1000 / Money.SeedCppMites, 500, 2000);
        for (int g = 0; g < Goods.Length; g++)
        {
            var gb = Goods[g];
            long target = gb.BaseRefMicro * mult / 1000;
            gb.RefMicro += FloorDiv(target - gb.RefMicro, Math.Max(1, Money.RefDriftDivisor));
        }
        return mult;
    }

    private static long FloorDiv(long n, long d) { long q = n / d; return (n % d != 0 && ((n ^ d) < 0)) ? q - 1 : q; }

    // ---------- inspection ----------

    internal uint NextLotId { get => _nextLot; set => _nextLot = value; }
    public IEnumerable<StockLot> AllLots() { foreach (var gb in Goods) foreach (var l in gb.Lots) yield return l; }
    public long TotalLotUnits(ushort good) => Goods[good].Lots.Sum(l => l.Qty);
}

/// <summary>A merchant's request for goods from other places (money spec 3.4); the caravan planner reads these later.</summary>
public sealed record ImportRequest(int SettlementIndex, ushort Good, long Qty, int Day, EntityId Merchant);

/// <summary>Ids of the economic records the economy adds beside the people tables (all kind Other, with a block per record type).</summary>
public static class EconIds
{
    public const ulong BusinessBase = 0x100_0000, MarketBase = 0x200_0000, SiteBase = 0x300_0000;
    public static EntityId Business(int index) => EntityId.Make(IdKind.Other, BusinessBase + (ulong)index);
    public static EntityId MarketStore(int settlement) => EntityId.Make(IdKind.Other, MarketBase + (ulong)settlement);
    public static bool IsBusiness(EntityId id) => id.Kind == IdKind.Other && id.Counter >= BusinessBase && id.Counter < MarketBase;
    public static int BusinessIndex(EntityId id) => (int)(id.Counter - BusinessBase);
}
