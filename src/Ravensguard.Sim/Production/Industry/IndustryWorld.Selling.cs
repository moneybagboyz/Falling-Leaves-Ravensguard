using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Production.Industry;

/// <summary>What selling a stage's output at the gate would bring today (industries 5.10, IN-17): the book it goes to, the quantity, the book's price for it (the per-unit repricing is in the figure), what the carting costs and the market toll the seller pays.</summary>
public readonly record struct GateQuote(int Settlement, long Qty, long GrossMites, long CartMites, long CartTollMites, long MarketTollMites, long DistanceM, string Vehicle, int Vehicles)
{
    /// <summary>The spec's figure: the book's price less the carting (and the tolls on the cart's road).</summary>
    public long NetOfCartingMites => GrossMites - CartMites - CartTollMites;
    /// <summary>What the works keeps: the above less the market toll.</summary>
    public long NetMites => NetOfCartingMites - MarketTollMites;
}

/// <summary>What the books in reach take of one output good (industries 5.10, economies-of-scale 5.4): the gate price, the room a day, what the border desk can still carry off, and how many days the stage's stock needs to clear at that rate.</summary>
public readonly record struct GateView(ushort Good, int Settlement, long BidMicro, long NetPerKgMilli, long AbsorbPerDay, long DeskRoomMites, long StockG, long DaysToClear, bool OverAbsorption);

/// <summary>
/// Selling (P7-IN4, industries 5.10): the gate (the nearest book the carting makes worth it, with the books' absorption), a supply contract's hold, and the works' own caravans (<see cref="OwnCaravans"/>). The pure rules are here; every coin is a ledger transfer between
/// real purses (the carter's household and the treasury of the town the cart enters) and every good is a store move.
/// </summary>
public sealed partial class IndustryWorld
{
    private ContinentTrade? _trade;
    private OwnCaravans? _caravans;
    /// <summary>The home region's caravan trade (set by the wiring; null in a world with no continent: the gate still sells, own caravans are refused).</summary>
    public ContinentTrade? Trade { get => _trade; set { _trade = value; if (value != null) value.Own = Caravans; } }
    /// <summary>The works' own caravans: quotes, hiring, dispatch and the trips' ends.</summary>
    public OwnCaravans Caravans => _caravans ??= new OwnCaravans(this);
    /// <summary>The open supply contracts (the scale book's), for a stage routed to a contract; null: none.</summary>
    public Func<IReadOnlyList<SupplyContract>>? ContractList { get; set; }
    /// <summary>The carter's household of a place (the first adult labourer there); the default reads the people table.</summary>
    public Func<int, int, int>? CarterOf { get; set; }

    internal int CarterHousehold(int settlement, int day)
    {
        if (CarterOf != null) return CarterOf(settlement, day);
        var pt = People;
        for (int i = 0; i < pt.Count; i++)
            if (pt.IsAlive(i) && pt.HomeSettlement[i] == settlement && pt.Occupation[i] == Occupation.Labourer && pt.Household[i] >= 0 && pt.AgeYears(i, day) is >= 16 and < 60) return pt.Household[i];
        return -1;
    }

    private (long X, long Y) OriginOf(WorkStage s, Business b)
    {
        if (s.HasSite) return (((long)s.SiteX0 + s.SiteX1) / 2, ((long)s.SiteY0 + s.SiteY1) / 2);
        var st = Prod.Pop.Region.Settlements[b.Settlement];
        return (st.CenterX, st.CenterY);
    }

    internal long FeedPrice(MarketBook? book, string good, long fallback)
    {
        if (book == null || !Prod.Econ.Goods.TryIdOf(good, out var g)) return fallback;
        long mid = book.MidMicro(g);
        return mid <= 0 ? fallback : Math.Max(1, (mid + 500) / 1000);
    }

    private PurseId TreasuryOf(int settlement)
    {
        var reg = Prod.Pop.Region;
        int fief = (uint)settlement < (uint)reg.Settlements.Count ? reg.Settlements[settlement].FiefIndex : -1;
        return fief >= 0 && fief < reg.Fiefs.Count && Led.TryFind(PurseKind.Treasury, reg.Fiefs[fief].Id, out var t) ? t : default;
    }

    // ------------------------------------------------------------------ the gate

    /// <summary>
    /// The gate price of <paramref name="qty"/> of a stage's output (5.10): the books within the absorption reach of the stage's premises (and its own town's), each priced by its curve for the whole lot (the per-unit repricing), less the carting from the premises to it
    /// (<c>Carting.Plan</c>, ES2) and the market toll; the best net wins (ties the nearer, then the lower settlement). IN-17: 100 kg at a bid of 440 two kilometres off is 44,000 less the carting.
    /// </summary>
    public GateQuote QuoteGate(Works w, WorkStage s, ushort good, long qty)
    {
        var b = BusinessOf(s)!; var markets = Prod.Markets;
        if (markets == null || qty <= 0) return new GateQuote(b.Settlement, 0, 0, 0, 0, 0, 0, "", 0);
        var reg = Prod.Pop.Region; var ss = Prod.Content.Scale; var (ox, oy) = OriginOf(s, b);
        long reach2 = (long)ss.AbsorbReachM * ss.AbsorbReachM, mass = Prod.Econ.Goods.MassG(good, qty);
        var home = markets.BookForSettlement(b.Settlement);
        long hay = FeedPrice(home, "hay", ss.HayMitesPerKg), oats = FeedPrice(home, "oats", ss.OatsMitesPerKg);
        GateQuote best = default; bool have = false;
        for (int i = 0; i < reg.Settlements.Count; i++)
        {
            long dx = reg.Settlements[i].CenterX - ox, dy = reg.Settlements[i].CenterY - oy, d2 = dx * dx + dy * dy;
            if (i != b.Settlement && d2 > reach2) continue;
            var bk = markets.BookForSettlement(i); if (bk.Blockaded) continue;
            long dist = IntMath.Isqrt(d2), gross = bk.QuoteSell(good, qty);
            var trip = Carting.Plan(ss, dist, mass, hay, oats, Prod.Content.Tax.GateTollMitesPerCart, i != b.Settlement);
            long toll = (long)(((Int128)gross * bk.Money.MarketTollPermille + 500) / 1000);
            var q = new GateQuote(i, qty, gross, trip.CrewAndTeamMites, trip.TollMites, toll, dist, trip.Vehicle, trip.Vehicles);
            if (!have || q.NetMites > best.NetMites || (q.NetMites == best.NetMites && (q.DistanceM < best.DistanceM || (q.DistanceM == best.DistanceM && q.Settlement < best.Settlement)))) { best = q; have = true; }
        }
        return have ? best : new GateQuote(b.Settlement, qty, 0, 0, 0, 0, 0, "", 0);
    }

    /// <summary>
    /// The books' absorption of a stage's output good (economies-of-scale 5.4 for the stage's stock): the demand a day of every book in reach (less the other producers of the kind there: none are counted for a works), the border desk's room, the gate price per kilogram
    /// after the carting, and the days the stage's stock needs to clear at that rate. <c>OverAbsorption</c> is true when the stock is more than a week of what the books take.
    /// </summary>
    public GateView ViewGate(ulong stageBusiness, ushort good)
    {
        if (Find(stageBusiness) is not { } f || BusinessOf(f.S) is not { } b || Prod.Markets == null) return default;
        var reg = Prod.Pop.Region; var ss = Prod.Content.Scale; var (ox, oy) = OriginOf(f.S, b);
        long reach2 = (long)ss.AbsorbReachM * ss.AbsorbReachM;
        var parts = new List<(long, int)>(); long desk = 0;
        for (int i = 0; i < reg.Settlements.Count; i++)
        {
            long dx = reg.Settlements[i].CenterX - ox, dy = reg.Settlements[i].CenterY - oy;
            if (i != b.Settlement && dx * dx + dy * dy > reach2) continue;
            var sb = Prod.Markets.BookForSettlement(i); var gb = sb.Goods[good];
            parts.Add((gb.Demand, 0));
            foreach (var ir in sb.ImportRequests) if (ir.Good == good) desk += ir.Qty / Math.Max(1, gb.TargetCoverDays);
        }
        long cap = Absorption.CapPerDay(parts, desk), stock = b.Store.Qty(good);
        long unit = Prod.Econ.Goods.IsPiece(good) ? 1 : 1000;
        var q = QuoteGate(f.W, f.S, good, Math.Max(unit, stock));
        long perUnit = q.Qty > 0 ? q.NetMites * 1000 / q.Qty : 0;                       // milli-mites a unit (a gram, or a piece)
        long perKgMilli = Prod.Econ.Goods.IsPiece(good) ? perUnit : perUnit * 1000;
        long days = cap <= 0 ? (stock > 0 ? long.MaxValue / 2 : 0) : (stock + cap - 1) / cap;
        long deskRoom = Trade?.DeskRoomMites ?? 0;
        return new GateView(good, q.Settlement, Prod.Markets.BookForSettlement(q.Settlement).BidMicro(good), perKgMilli, cap, deskRoom, stock, days, stock > cap * 7);
    }

    /// <summary>
    /// A gate sale (the Forward step of a stage routed to the gate or, for the part beyond its contracts' hold, to a contract): the best book by <see cref="QuoteGate"/>, the goods sold to its merchants and the rest listed at the floor, the carting paid from the works'
    /// purse (the crew and team to a carter's household, the tolls to the town's treasury). A place with no carter, or a purse that cannot pay the bill, sells at the works' own town instead. Returns the units that left the store.
    /// </summary>
    private long SellAtGate(Works w, WorkStage s, Business b, ushort good, long qty, int day)
    {
        if (Prod.Markets == null || qty <= 0) return 0;
        var q = QuoteGate(w, s, good, qty);
        int where = q.Settlement;
        long cart = q.CartMites, toll = q.CartTollMites; int carter = -1;
        if (where != b.Settlement && (q.NetMites <= 0 || (cart + toll) > 0 && (carter = CarterHousehold(b.Settlement, day)) < 0 || (cart + toll) > Led.Balance(b.Purse).Mites ))
        {
            where = b.Settlement; cart = 0; toll = 0; carter = -1;                       // no carter, no coin for the road, or no gain in it: sold at home
        }
        var book = Prod.Markets.BookForSettlement(where);
        long sold = book.SellOrList(b.Party, good, qty, 900, day);
        if (sold <= 0) return 0;
        if (cart + toll > 0 && sold < qty) { cart = cart * sold / qty; toll = toll * sold / qty; }
        if (cart > 0 && carter >= 0 && Led.Transfer(b.Purse, Prod.Econ.HouseholdPurse[carter], new Coin(cart), TransferReason.Wage)) s.Add(day, WorkStage.Upkeep, cart);
        var tp = TreasuryOf(where); if (tp.IsNone) tp = book.TollPurse;                                  // the town's treasury, else its market's toll purse
        if (toll > 0 && Led.Transfer(b.Purse, tp, new Coin(toll), TransferReason.Toll)) s.Add(day, WorkStage.Upkeep, toll);
        return sold;
    }

    // ------------------------------------------------------------------ contracts

    /// <summary>The grams (or pieces) of a good the stage keeps back for the supply contracts it is the seller of: the goods of <c>ContractHoldDeliveries</c> deliveries of each open one.</summary>
    public long ContractHold(ulong stageBusiness, ushort good)
    {
        var list = ContractList?.Invoke(); if (list == null) return 0;
        string id = Prod.Econ.Goods.StringId(good); long hold = 0;
        foreach (var c in list)
            if (c.State == SupplyState.Open && c.Seller == stageBusiness && c.Good == id) hold += c.QtyPerDelivery * Set.ContractHoldDeliveries;
        return hold;
    }

    /// <summary>Whether a stage is the seller of an open supply contract.</summary>
    public bool HasContract(ulong stageBusiness)
    {
        var list = ContractList?.Invoke(); if (list == null) return false;
        foreach (var c in list) if (c.State == SupplyState.Open && c.Seller == stageBusiness) return true;
        return false;
    }

    // ------------------------------------------------------------------ the owner's order for a stage's caravan

    /// <summary>
    /// Sets a stage's caravan order (5.10): where it goes (a town of the region by settlement index, or a neighbouring region by its id, over its border gate), the wagons (a kind and how many), the guards hired for each trip, and whether the order stands. Refuses a
    /// destination the roads do not reach, the stage's own town, a neighbour that is itself run in detail (its coin is real; the planner skips it too) and a world with no continent.
    /// </summary>
    public OwnResult SetCaravanOrder(ulong business, DestKind kind, int dest, byte vehicle, int wagons, int guards, bool standing)
    {
        if (Find(business) is not { } f) return OwnResult.No("in_no_such_stage");
        if (Trade == null || !Trade.IsBound) return OwnResult.No("in_caravan_no_continent");
        if (vehicle is not (1 or 2)) return OwnResult.No("in_caravan_bad_wagons");
        wagons = Math.Clamp(wagons, 1, Set.MaxWagons); guards = Math.Clamp(guards, 0, 8);
        var b = BusinessOf(f.S)!;
        var o = new CaravanOrder { Kind = kind, Dest = dest, Vehicle = vehicle, Wagons = wagons, Guards = guards, Standing = standing, Fleet = f.S.Order?.Fleet ?? 0, Master = f.S.Order?.Master ?? -1 };
        var probe = Caravans.PathOf(b.Settlement, o, out string why);
        if (probe == null) return OwnResult.No(why);
        f.S.Order = o;
        return OwnResult.Yes("in_caravan_order_set", wagons, guards);
    }

    /// <summary>Names the person who leads the stage's caravans (a person row; -1: the best free trader is hired for each trip).</summary>
    public OwnResult SetCaravanMaster(ulong business, int person)
    {
        if (Find(business) is not { } f) return OwnResult.No("in_no_such_stage");
        if (f.S.Order == null) return OwnResult.No("in_caravan_no_order");
        if (person >= 0 && (person >= People.Count || People.Status[person] != PersonStatus.Alive || People.SkillLevelOf(person, Skill.Trading) < Set.MasterMinTrading)) return OwnResult.No("in_caravan_master_unfit", Set.MasterMinTrading);
        f.S.Order.Master = person;
        return OwnResult.Yes("in_caravan_master_set");
    }

    /// <summary>The stages' standing orders, once a works day (called at the end of <see cref="Day"/>): a stage routed to its caravan whose order stands and whose store holds enough of a load sends a caravan if its fleet is home.</summary>
    private void DispatchStanding(Works w, int day, GameTime now)
    {
        if (Trade == null) return;
        foreach (var s in w.Stages)
            if (s.Route == Routing.Caravan && s.Order is { Standing: true }) Caravans.TrySendStanding(w, s, day, now);
    }
}
