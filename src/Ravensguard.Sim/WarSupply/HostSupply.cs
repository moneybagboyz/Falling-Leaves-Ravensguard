using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Disease;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using PartyKind = Ravensguard.Sim.Economy.Markets.PartyKind;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.WarSupply;

/// <summary>
/// The interface the military calls (army-economy 3): register, update and disband a host; <see cref="Needs"/> (pure) and <see cref="Supply"/> with the ladder (baggage, the lord's food rents, depots within a day and convoys from
/// the ones beyond it, the markets at their asks), pay and arrears and the hunger and unpaid days; <see cref="Muster"/>, <see cref="Release"/> and <see cref="Desert"/> for levies (a mustered man's household loses his labour:
/// <see cref="IsOnCampaign"/> is the farming planner's hook); <see cref="SiegeBlockade"/>, <see cref="DaysOfFood"/> (and <c>siege_starving</c> at 5 days), <see cref="HarvestRisk"/> and <see cref="Camp"/> (which feeds the disease rule's
/// army camp); the abstract tier (<see cref="ToAbstract"/>: the same needs, counters and pay, the food bought abroad at the continent's prices). Sutlers, purveyance and forage, the pay chest's moves and capture, kit and loot are
/// P7-AE2 and P7-AE3. The economy never moves soldiers; every coin and gram here moves through the ledger and the stores, so both audits stay exact.
/// One writer: the host slot (<see cref="HostSupplySystem"/>, hour 18) or the harness at a barrier.
/// </summary>
public sealed partial class HostSupply
{
    public WarSupplyBook Book { get; }
    public EconomyWorld Economy { get; }
    private readonly EconomyState _econ;
    private readonly Population _pop;
    private readonly Dictionary<int, Rt> _rt = [];
    private readonly Dictionary<int, DayResult> _last = [];
    private int _lastDay = int.MinValue;
    private readonly ushort[] _stapleGoods, _relishGoods;
    private readonly ushort _hay, _oats, _firewood;
    /// <summary>The hook for a household that evades the levy (the unrest rule, stream 0x682): settlement, household, day. Null until the unrest wiring binds it (P7-AM-B).</summary>
    public Func<int, int, int, bool>? Evades { get; set; }
    /// <summary>Where the host's own chronicle entries go (<c>army_bought</c>, <c>siege_starving</c>, <c>depot_emptied</c>): the host slot sets it to the tick's outbox each day; a harness may set it to the chronicle. Silent by default.</summary>
    public RuleChron Chron { get; set; } = RuleChron.Silent;
    /// <summary>The world's disease book when it has one (the wiring sets it): <see cref="Camp"/> and the daily step feed its <c>ArmyCampSize</c> hook from it, once.</summary>
    public Func<DiseaseBook?>? DiseaseSource { get; set; }
    private bool _diseaseBound;
    /// <summary>The convoys of the hosts (the second caravan owner beside the works').</summary>
    public HostConvoys Convoys { get; }
    private ushort[] _loadGoods = [];
    /// <summary>The goods a convoy loads from a depot: the staple grains and oats.</summary>
    internal ushort[] LoadGoods => _loadGoods;

    /// <summary>The live objects of a host's records, found again by owner after a load.</summary>
    private sealed class Rt
    {
        public PurseId Chest = PurseId.None, Soldiers = PurseId.None, Treasury = PurseId.None;
        public Store Baggage = null!;
        public MarketParty Party = null!;
    }

    public HostSupply(EconomyWorld economy, WarSupplyBook book)
    {
        Economy = economy; Book = book; _econ = economy.World.Economy; _pop = economy.World.Population;
        var c = _econ.Goods;
        _oats = c.TryIdOf("oats", out var o) ? o : ushort.MaxValue; _hay = c.TryIdOf("hay", out var h) ? h : ushort.MaxValue; _firewood = _econ.Food.Firewood;
        _stapleGoods = [.. _econ.Food.Staples.Where(g => g != _oats && !c.IsPiece(g) && c.Def(g).Class is "grain" or "flour")];
        _relishGoods = [.. _econ.Food.Relishes.Where(g => !c.IsPiece(g))];
        _loadGoods = [.. _stapleGoods, .. (_oats == ushort.MaxValue ? [] : new[] { _oats })];
        Convoys = new HostConvoys(this);
        foreach (var s in book.Blockades.Keys) { var bk = BookOf(s); if (bk != null) bk.Blockaded = true; }
    }

    /// <summary>Puts the service in the home region's bag and binds the farming planner's hook; the world's save state is <paramref name="book"/>.</summary>
    public static HostSupply Install(EconomyWorld economy, WarSupplyBook book)
    {
        var hs = new HostSupply(economy, book);
        economy.World.Set(hs);
        economy.War = hs;
        economy.Trade.OwnWar = hs.Convoys;
        return hs;
    }

    private int Day => (int)(Economy.World.State.HourDone / 24);
    private MarketBook? BookOf(int settlement) => settlement >= 0 && settlement < _pop.Region.Settlements.Count ? Economy.Markets.Books[Economy.Markets.BookOf[settlement]] : null;

    // ------------------------------------------------------------------ hosts

    /// <summary>Registers a host (army-economy 3.1) and returns its id; the chest purse and baggage store are the caller's records.</summary>
    public HostId Register(HostSpec spec)
    {
        if (_econ.Ledger.KindOf(spec.PayChest) != PurseKind.Chest) throw new ArgumentException("the pay chest must be a Chest purse", nameof(spec));
        var a = new HostAccount
        {
            Id = Book.NextHost++, Realm = spec.Realm, Lord = spec.Lord.Value,
            ChestKind = (byte)_econ.Ledger.KindOf(spec.PayChest), ChestOwner = _econ.Ledger.OwnerOf(spec.PayChest).Value, BaggageOwner = spec.Baggage.Owner.Value,
            Purveyance = spec.Policy.Purveyance, Plunder = spec.Policy.Plunder, Forage = spec.Policy.Forage, Depots = [.. spec.Policy.Depots ?? []], BuyRadiusM = spec.Policy.BuyRadiusM,
            Campaign = spec.Campaign.Value,
            Followers = spec.Followers, Carts = spec.Carts, Outside = Math.Min(spec.OutsideMen, spec.Men.Total),
            Riding = spec.Animals.Riding, Pack = spec.Animals.Pack, Draught = spec.Animals.Draught, Oxen = spec.Animals.Oxen,
        };
        Array.Copy(spec.Men.Counts, a.Men, Roster.Roles);
        if (!spec.Treasury.IsNone) { a.TreasuryKind = (byte)_econ.Ledger.KindOf(spec.Treasury); a.TreasuryOwner = _econ.Ledger.OwnerOf(spec.Treasury).Value; }
        Book.Hosts[a.Id] = a;
        Resolve(a);
        return new HostId(a.Id);
    }

    /// <summary>The host's place, mode and roster as the military last left them (army-economy 3.1).</summary>
    public void Update(HostId h, HostPlace place, HostMode mode, Roster? men = null)
    {
        var a = Acc(h);
        a.PlaceSettlement = place.Settlement; a.PlaceXM = place.XM; a.PlaceYM = place.YM; a.Pasture = place.Pasture; a.Mode = mode;
        if (men != null) Array.Copy(men.Counts, a.Men, Roster.Roles);
    }

    /// <summary>A place at a settlement's centre.</summary>
    public HostPlace PlaceAt(int settlement, bool pasture = false)
    {
        var s = _pop.Region.Settlements[settlement];
        return new HostPlace(settlement, s.CenterX, s.CenterY, pasture);
    }

    /// <summary>
    /// The host is done: what the chest can pay of the arrears is paid, the men on campaign go home (labour returns), the soldiers' coin goes home (outsiders' share leaves with them), the unspent coin returns to the
    /// treasury the host was funded from, and the account is dropped. Goods stay in the baggage store (the lord's).
    /// </summary>
    public void Disband(HostId h)
    {
        var a = Acc(h); var rt = Resolve(a);
        PayArrears(a, rt, null);
        foreach (int p in Book.OnCampaign.Where(kv => kv.Value == a.Id).Select(kv => kv.Key).ToList()) Book.OnCampaign.Remove(p);
        SettleSoldiers(a, rt);
        var led = _econ.Ledger; long left = led.Balance(rt.Chest).Mites;
        if (left > 0 && !rt.Treasury.IsNone && rt.Treasury != rt.Chest) led.Transfer(rt.Chest, rt.Treasury, new Coin(left), TransferReason.Hoard);
        foreach (int s in Book.Blockades.Where(kv => kv.Value == a.Id).Select(kv => kv.Key).ToList()) SiegeBlockade(s, false, h);
        Book.Hosts.Remove(a.Id); _rt.Remove(a.Id); _last.Remove(a.Id);
    }

    public bool Exists(HostId h) => Book.Hosts.ContainsKey(h.Value);
    public IEnumerable<HostId> Hosts() { foreach (var id in Book.Hosts.Keys) yield return new HostId(id); }
    public HostAccount Account(HostId h) => Acc(h);
    public DayResult? Last(HostId h) => _last.GetValueOrDefault(h.Value);
    public PurseId ChestOf(HostId h) => Resolve(Acc(h)).Chest;
    public PurseId SoldiersPurse(HostId h) => Resolve(Acc(h)).Soldiers;
    public Store BaggageOf(HostId h) => Resolve(Acc(h)).Baggage;

    private HostAccount Acc(HostId h) => Book.Hosts.TryGetValue(h.Value, out var a) ? a : throw new KeyNotFoundException($"no {h}");

    private Rt Resolve(HostAccount a)
    {
        if (_rt.TryGetValue(a.Id, out var rt)) return rt;
        var led = _econ.Ledger;
        rt = new Rt();
        if (!led.TryFind((PurseKind)a.ChestKind, new EntityId(a.ChestOwner), out rt.Chest)) throw new InvalidDataException($"host {a.Id}: pay chest purse is missing");
        if (!_econ.OtherStores.TryGetValue(new EntityId(a.BaggageOwner), out var bag)) throw new InvalidDataException($"host {a.Id}: baggage store is missing");
        rt.Baggage = bag;
        rt.Soldiers = led.Open(PurseKind.Chest, HostIds.Soldiers(new HostId(a.Id)));
        if (a.TreasuryOwner != 0) led.TryFind((PurseKind)a.TreasuryKind, new EntityId(a.TreasuryOwner), out rt.Treasury);
        rt.Party = new MarketParty(new EntityId(a.BaggageOwner), rt.Chest, rt.Baggage, PartyKind.Merchant, -1 - a.Id);
        return _rt[a.Id] = rt;
    }

    // ------------------------------------------------------------------ needs

    /// <summary>A day's needs (army-economy 3.2): pure.</summary>
    public DayNeeds Needs(HostId h, int day) => NeedsOf(Acc(h), day);

    internal static DayNeeds NeedsOf(HostAccount a, int day)
    {
        var season = GameTime.FromGameHours((long)day * 24).Season;
        int men = a.Total;
        var (hay, oats) = SupplyConfig.Fodder(a.Animals);
        if (a.Pasture && season is Season.Summer or Season.Autumn) hay /= 2;
        long wood = (long)(men + a.Followers) * SupplyConfig.FirewoodG * (season == Season.Winter ? 2 : 1);
        long pay = 0;
        for (int r = 0; r < Roster.Roles; r++) pay += (long)a.Men[r] * WageToday(a, (TroopRole)r);
        return new DayNeeds((long)men * SupplyConfig.RationG + (long)a.Followers * SupplyConfig.FollowerRationG, (long)men * SupplyConfig.RelishG, hay, oats, wood, pay);
    }

    private static int WageToday(HostAccount a, TroopRole r) => r == TroopRole.Levy && a.ServiceDays < SupplyConfig.LevyFreeDays ? 0 : SupplyConfig.Wage(r);

    // ------------------------------------------------------------------ the daily call

    /// <summary>The host slot: every host, in id order, once for a day (hour 18 by <see cref="HostSupplySystem"/>).</summary>
    public void RunDay(int day)
    {
        if (day == _lastDay) return;
        _lastDay = day;
        BindDisease();
        CheckLarders(day);
        foreach (var id in Book.Hosts.Keys.ToArray()) _last[id] = Supply(new HostId(id), day);
        foreach (var id in _last.Keys.Where(k => !Book.Hosts.ContainsKey(k)).ToArray()) _last.Remove(id);
    }

    /// <summary>
    /// Runs the ladder for the host's day (army-economy 4.1, first cut): each need from the baggage, then the markets within the buy radius at their asks (per-unit repricing), staple first; pay from the chest in the
    /// overview's order, then arrears with what is left; the hunger and unpaid days; the levies' service days. Coin and goods move only through the ledger and the stores.
    /// </summary>
    public DayResult Supply(HostId h, int day)
    {
        var a = Acc(h); var rt = Resolve(a);
        var need = NeedsOf(a, day);
        var res = new DayResult { Day = day, Host = h, Needed = need };
        var books = a.Abstract ? [] : BooksInReach(a);
        var led = _econ.Ledger;
        long coin0 = led.Balance(rt.Chest).Mites;
        NewForageDay(a); UpdateSutlers(a, day);
        // staple first: the men's lives; then pay; then the animals, the fires and the relish with what the chest has left
        Fill(a, rt, res, SupplyNeed.Staple, need.StapleG, books, day);
        PayDay(a, rt, res, need);
        Fill(a, rt, res, SupplyNeed.Hay, need.FodderHayG, books, day);
        Fill(a, rt, res, SupplyNeed.Oats, need.FodderOatsG, books, day);
        Fill(a, rt, res, SupplyNeed.Firewood, need.FirewoodG, books, day);
        Fill(a, rt, res, SupplyNeed.Relish, need.RelishG, books, day);
        // the days short
        bool hungry = res.ShortG[(int)SupplyNeed.Staple] > 0;
        for (int r = 0; r < Roster.Roles; r++)
        {
            if (a.Men[r] == 0) { a.HungerDays[r] = 0; continue; }
            a.HungerDays[r] = hungry ? a.HungerDays[r] + 1 : 0;
        }
        if (a.Men[(int)TroopRole.Levy] > 0 || a.Total > 0) a.ServiceDays++;
        for (int r = 0; r < Roster.Roles; r++) { res.HungerDays[r] = a.HungerDays[r]; res.UnpaidDays[r] = a.UnpaidDays[r]; res.Arrears[r] = a.Arrears[r]; }
        long staples = 0; foreach (var g in _stapleGoods) staples += EqG(g, rt.Baggage.Qty(g));
        res.DaysOfFoodMilli = need.StapleG > 0 ? staples * 1000 / need.StapleG : 0;
        res.DaysFundedMilli = need.PayMites > 0 ? led.Balance(rt.Chest).Mites * 1000 / need.PayMites : 0;
        res.BoughtMites = Math.Max(0, coin0 - led.Balance(rt.Chest).Mites - res.PaidTotal);
        if (!a.Abstract && a.Depots.Length > 0 && a.Carts > 0 && staples < need.StapleG * SupplyConfig.ConvoyBelowDays) TrySendConvoy(a, rt, need, day);
        NotePrices(a, res, day);
        for (int n = 0; n < DayResult.Needs; n++)
        {
            for (int s = 0; s < DayResult.Sources; s++) a.Totals[n * DayResult.Sources + s] += res.Delivered[n, s];
            a.Totals[DayResult.Needs * DayResult.Sources + n] += res.ShortG[n];
        }
        return res;
    }

    // ------------------------------------------------------------------ the ladder

    private long EqG(ushort good, long qty) => (long)((Int128)qty * _econ.Goods.FoodMeasuresPpm(good) / SupplyConfig.GrainMeasurePpm);
    private bool IsStapleLike(SupplyNeed n) => n == SupplyNeed.Staple;

    private ushort[] GoodsOf(SupplyNeed n) => n switch
    {
        SupplyNeed.Staple => _stapleGoods, SupplyNeed.Relish => _relishGoods,
        SupplyNeed.Hay => _hay == ushort.MaxValue ? [] : [_hay], SupplyNeed.Oats => _oats == ushort.MaxValue ? [] : [_oats], _ => [_firewood],
    };

    /// <summary>The need's measure of a stock quantity of the good: staple goods by their food measures (a grain 1:1), the rest by mass.</summary>
    private long Measure(SupplyNeed n, ushort good, long qty) => IsStapleLike(n) ? EqG(good, qty) : qty;
    /// <summary>The smallest quantity of the good that measures at least <paramref name="eq"/>.</summary>
    private long QtyFor(SupplyNeed n, ushort good, long eq)
    {
        if (!IsStapleLike(n)) return eq;
        long ppm = Math.Max(1, _econ.Goods.FoodMeasuresPpm(good));
        return (long)(((Int128)eq * SupplyConfig.GrainMeasurePpm + ppm - 1) / ppm);
    }

    private void Fill(HostAccount a, Rt rt, DayResult res, SupplyNeed n, long want, List<MarketBook> books, int day)
    {
        if (want <= 0) return;
        var goods = GoodsOf(n);
        var why = n == SupplyNeed.Firewood ? DestroyReason.Burned : DestroyReason.Eaten;
        long left = want;
        // 1. baggage
        left = Pull(rt, res, n, goods, why, left, SupplySource.Baggage, rt.Baggage, 0);
        if (a.Abstract)
        {
            // the abstract tier: no books here; the food is bought abroad at the continent's prices, paid from the chest
            if (left > 0) left = AbroadBuy(rt, res, n, goods, why, left);
            res.ShortG[(int)n] = Math.Max(0, left);
            return;
        }
        // 2. food rents: the lord's own barn within reach gives to his host (the lord's goods, so no coin), keeping his household's reserve of grain
        if (left > 0 && n is SupplyNeed.Staple or SupplyNeed.Hay or SupplyNeed.Oats && LordBarn(a) is var (barn, reserveEq) && barn != null)
            left = Pull(rt, res, n, goods, why, left, SupplySource.FoodRent, barn, n == SupplyNeed.Staple ? reserveEq : 0);
        // 3. depots within a day's cart: delivered the same day (the beyond ones are the convoy's)
        if (left > 0 && n is SupplyNeed.Staple or SupplyNeed.Oats)
            foreach (int ds in DepotsByDistance(a, sameDayOnly: true))
            {
                if (left <= 0) break;
                var depot = DepotStore(ds); if (depot == null) continue;
                left = Pull(rt, res, n, goods, why, left, SupplySource.Depot, depot, 0);
                NoteDepot(a, ds, depot, day);
            }
        // 4. the market: books by distance, the cheapest good per measure first, at the books' asks
        if (left > 0 && goods.Length > 0)
        {
            foreach (var bk in books)
            {
                if (left <= 0) break;
                var order = goods.Where(g => bk.Goods[g].StockUnits > 0).OrderBy(g => bk.AskMicro(g) * SupplyConfig.GrainMeasurePpm / Math.Max(1, IsStapleLike(n) ? _econ.Goods.FoodMeasuresPpm(g) : SupplyConfig.GrainMeasurePpm)).ThenBy(g => g).ToArray();
                foreach (var g in order)
                {
                    if (left <= 0) break;
                    long units = QtyFor(n, g, left);
                    long ask0 = bk.AskMicro(g);
                    var t = bk.Buy(rt.Party, g, units, SupplyConfig.BuyMaxRatioPermille, day);
                    if (t.Qty <= 0) continue;
                    long qty = Math.Min(t.Qty, units);
                    long eq = Math.Min(left, Measure(n, g, qty));
                    rt.Baggage.Destroy(g, qty, why);
                    res.Delivered[(int)n, (int)SupplySource.Market] += eq; left -= eq;
                    if (n == SupplyNeed.Staple)
                    {
                        (long Ask0, long Ask1, long Grams, ushort Good) p = res.Purchases.TryGetValue(bk.SettlementIndex, out var q0) ? q0 : (ask0, ask0, 0L, g);
                        res.Purchases[bk.SettlementIndex] = (p.Ask0, bk.AskMicro(g), p.Grams + qty, g);
                    }
                }
            }
        }
        // 5. sutlers, 6. purveyance (own realm), 7. forage
        if (n == SupplyNeed.Staple)
        {
            if (left > 0) left = Sutle(a, rt, res, goods, left, day);
            if (left > 0) left = Purvey(a, rt, res, goods, left, want, day);
        }
        if (left > 0) left = Forage(a, rt, res, n, goods, left, day);
        res.ShortG[(int)n] = Math.Max(0, left);
    }

    /// <summary>Eats up to <paramref name="left"/> measures of the need's goods out of <paramref name="from"/> (the baggage itself, or a barn or depot whose goods are first moved into the baggage), keeping <paramref name="reserveEq"/> measures of staple in it; returns what is still wanted.</summary>
    private long Pull(Rt rt, DayResult res, SupplyNeed n, ushort[] goods, DestroyReason why, long left, SupplySource src, Store from, long reserveEq)
    {
        bool bag = ReferenceEquals(from, rt.Baggage);
        long avail = long.MaxValue;
        if (reserveEq > 0) { long have = 0; foreach (var g in goods) have += Measure(n, g, from.Qty(g)); avail = Math.Max(0, have - reserveEq); }
        foreach (var g in goods)
        {
            if (left <= 0 || avail <= 0) break;
            long have = from.Qty(g); if (have <= 0) continue;
            long want = Math.Min(left, avail);
            long takeQty = Math.Min(have, QtyFor(n, g, want));
            long eq = Math.Min(want, Measure(n, g, takeQty));
            if (eq <= 0) continue;
            // eat exactly what covers the measure (the last unit may over-cover by a rounding gram)
            if (!bag && !Store.Move(from, rt.Baggage, g, takeQty)) continue;
            if (!rt.Baggage.Destroy(g, takeQty, why)) continue;
            res.Delivered[(int)n, (int)src] += eq; left -= eq; avail -= eq;
        }
        return left;
    }

    /// <summary>The lord's own barn when the host's lord is a person with a household that holds one within the buy radius, and the grain his household keeps in it (30 days of its people's staple).</summary>
    private (Store? Barn, long ReserveEq) LordBarn(HostAccount a)
    {
        if (a.Lord == 0) return (null, 0);
        var pt = _pop.People; int p = pt.IndexOf(new EntityId(a.Lord));
        if (p < 0 || pt.Status[p] != PersonStatus.Alive) return (null, 0);
        int hh = pt.Household[p]; if (hh < 0) return (null, 0);
        if (!_econ.OtherStores.TryGetValue(FarmIds.Barn(hh), out var barn)) return (null, 0);
        int s = _pop.Households.Settlement[hh]; var region = _pop.Region;
        if (s < 0 || s >= region.Settlements.Count) return (null, 0);
        long dx = region.Settlements[s].CenterX - a.PlaceXM, dy = region.Settlements[s].CenterY - a.PlaceYM;
        if (dx * dx + dy * dy > (long)a.BuyRadiusM * a.BuyRadiusM) return (null, 0);
        return (barn, (long)_pop.Households.MemberCount[hh] * 1100 * SupplyConfig.ReserveGrainDays);
    }

    // ------------------------------------------------------------------ depots and convoys

    /// <summary>A settlement's war depot store, when one has been opened (<see cref="OpenDepot"/>).</summary>
    public Store? DepotStore(int settlement) => _econ.OtherStores.TryGetValue(HostIds.Depot(settlement), out var s) ? s : null;

    /// <summary>Opens (once) the war depot at a settlement: a granary of <paramref name="capacityKg"/> kilograms (0: unlimited) that the host's side keeps for its armies. The depot belongs to the host's lord or realm in the sense that the host's policy names it; its stocking is the steward's (grain moved in by <see cref="Store.Move"/>).</summary>
    public Store OpenDepot(int settlement, long capacityKg = 0) => _econ.OpenStore(StoreKind.Granary, HostIds.Depot(settlement), capacityKg * 1000, 0, true);

    /// <summary>The policy's depots, nearest first (ties by settlement), those within a day's cart of the host's place or only those beyond it.</summary>
    private List<int> DepotsByDistance(HostAccount a, bool sameDayOnly)
    {
        var region = _pop.Region; var list = new List<(long D2, int S)>(); long day2 = (long)SupplyConfig.DepotSameDayM * SupplyConfig.DepotSameDayM;
        foreach (int s in a.Depots.Distinct())
        {
            if (s < 0 || s >= region.Settlements.Count) continue;
            long dx = region.Settlements[s].CenterX - a.PlaceXM, dy = region.Settlements[s].CenterY - a.PlaceYM, d2 = dx * dx + dy * dy;
            if ((d2 <= day2) == sameDayOnly) list.Add((d2, s));
        }
        list.Sort((x, y) => x.D2 != y.D2 ? x.D2.CompareTo(y.D2) : x.S.CompareTo(y.S));
        return [.. list.Select(x => x.S)];
    }

    /// <summary>Writes <c>depot_emptied</c> once when a war has taken the last grain out of a depot; a refilled depot may write it again.</summary>
    private void NoteDepot(HostAccount a, int settlement, Store depot, int day)
    {
        long grain = 0; foreach (var g in _loadGoods) grain += depot.Qty(g);
        if (grain > 0) { Book.Emptied.Remove(settlement); return; }
        if (!Book.Emptied.Add(settlement)) return;
        var sid = _pop.Region.Settlements[settlement].Id;
        Chron.Chron(WriteTime(day), "depot_emptied", "ch_war_depot_emptied", [ChronParam.Place("place", sid)], [sid], [HostCause(a, CauseKind.Supply, "ch_note_war_depot_drawn")], ("host", a.Id.ToString()));
    }

    /// <summary>The quartermaster sends a convoy when the baggage is low: to the nearest depot beyond a day's reach that holds at least a day of the host's staple, for up to <see cref="SupplyConfig.ConvoyLoadDays"/> days of it. One at a time.</summary>
    private void TrySendConvoy(HostAccount a, Rt rt, DayNeeds need, int day)
    {
        if (Convoys.Of(a.Id) != null || need.StapleG <= 0) return;
        foreach (int ds in DepotsByDistance(a, sameDayOnly: false))
        {
            var depot = DepotStore(ds); if (depot == null) continue;
            long grain = 0; foreach (var g in _loadGoods) grain += depot.Qty(g);
            if (grain < need.StapleG) continue;
            long want = Math.Min(grain, need.StapleG * SupplyConfig.ConvoyLoadDays);
            if (Convoys.Send(a, rt.Chest, rt.Baggage, ds, want, day, GameTime.FromGameHours((long)day * 24 + 18))) return;
        }
    }

    /// <summary>A cause naming the host's campaign entry when it has one, else a note key.</summary>
    private static Cause HostCause(HostAccount a, CauseKind kind, string noteKey) => a.Campaign != 0 ? new Cause(kind, new EntityId(a.Campaign)) : new Cause(kind, null, NoteKey: noteKey);
    private static GameTime WriteTime(int day) => GameTime.FromGameHours((long)day * 24 + 18);

    /// <summary>The books whose staple ask the host's buying moved by 5 per cent or more today each get an <c>army_bought</c> entry caused by the host's campaign (army-economy 8: the price entry names the army, not market drift).</summary>
    private void NotePrices(HostAccount a, DayResult res, int day)
    {
        foreach (var (s, p) in res.Purchases)
        {
            if (p.Ask0 <= 0 || (p.Ask1 - p.Ask0) * 1000 < p.Ask0 * SupplyConfig.PriceNotePermille) continue;
            var sid = _pop.Region.Settlements[s].Id; long pct = (p.Ask1 - p.Ask0) * 100 / p.Ask0;
            var subj = a.Lord != 0 ? new[] { sid, new EntityId(a.Lord) } : new[] { sid };
            var e = Chron.Chron(WriteTime(day), "army_bought", "ch_war_bought", [ChronParam.Place("place", sid), ChronParam.Count("kg", (int)(p.Grams / 1000)), ChronParam.Count("pct", (int)pct)], subj,
                [HostCause(a, CauseKind.Supply, "ch_note_war_buying")], ("host", a.Id.ToString()), ("good", _econ.Goods.StringId(p.Good)), ("ask_before", p.Ask0.ToString()), ("ask_after", p.Ask1.ToString()));
            if (e.Value != 0) res.EventIds.Add(e);
        }
    }

    /// <summary>The blockaded towns' larders: once a town's days of food fall under <see cref="SupplyConfig.StarvingDays"/> its <c>siege_starving</c> is written (once for the blockade).</summary>
    private void CheckLarders(int day)
    {
        foreach (var (s, host) in Book.Blockades.ToArray())
        {
            if (Book.Starving.Contains(s)) continue;
            int days = DaysOfFood(s); if (days >= SupplyConfig.StarvingDays) continue;
            Book.Starving.Add(s);
            var sid = _pop.Region.Settlements[s].Id;
            var cause = Book.Hosts.TryGetValue(host, out var a) ? HostCause(a, CauseKind.Famine, "ch_note_war_blockade") : new Cause(CauseKind.Famine, null, NoteKey: "ch_note_war_blockade");
            Chron.Chron(WriteTime(day), "siege_starving", "ch_war_starving", [ChronParam.Place("place", sid), ChronParam.Count("days", days)], [sid], [cause], ("host", host.ToString()), ("days", days.ToString()));
        }
    }

    // ------------------------------------------------------------------ the abstract tier

    /// <summary>
    /// The host goes to the abstract tier (outside every detailed region): its needs, counters, arrears and pay are the same; its food is bought abroad at the continent's prices (the grain index of its region) from its chest, the coin leaving the region as
    /// emigrants' coin so the continent's total holds. Its chest, baggage and soldiers' purse stay where they are.
    /// </summary>
    public void ToAbstract(HostId h) { Acc(h).Abstract = true; }

    /// <summary>The host comes back to a detailed region at <paramref name="place"/>: the counters it carried are the same account's.</summary>
    public void ToDetailed(HostId h, HostPlace place, HostMode mode) { var a = Acc(h); a.Abstract = false; Update(h, place, mode); }

    /// <summary>The price of a good abroad in micro-mites a unit: the region's books' mean reference price, times the continent's index for wheat-like goods, at the ask side of the spread.</summary>
    private long AbroadPriceMicro(ushort g)
    {
        long sum = 0; int n = 0;
        foreach (var b in Economy.Markets.AllBooks()) { long r = b.Goods[g].RefMicro; if (r > 0) { sum += r; n++; } }
        if (n == 0) return 0;
        long price = sum / n; int idx = 1000;
        var link = Economy.World.Get<ContinentLink>();
        if (link != null && IsStapleLike(g) && (uint)link.Region < (uint)link.State.Summaries.Length && link.State.Summaries[link.Region].PriceIdx.Length > 0) idx = Math.Max(1, link.State.Summaries[link.Region].PriceIdx[0]);
        return price * idx / 1000 * (1000 + _econ.Money.SpreadHalfPermille) / 1000;
    }
    private bool IsStapleLike(ushort g) => Array.IndexOf(_stapleGoods, g) >= 0;

    private long AbroadBuy(Rt rt, DayResult res, SupplyNeed n, ushort[] goods, DestroyReason why, long left)
    {
        var link = Economy.World.Get<ContinentLink>(); if (link == null || goods.Length == 0) return left;
        var led = _econ.Ledger;
        foreach (var g in goods.OrderBy(g => AbroadPriceMicro(g) * SupplyConfig.GrainMeasurePpm / Math.Max(1, IsStapleLike(n) ? _econ.Goods.FoodMeasuresPpm(g) : SupplyConfig.GrainMeasurePpm)).ThenBy(g => g))
        {
            if (left <= 0) break;
            long ppu = AbroadPriceMicro(g); if (ppu <= 0) continue;
            long units = QtyFor(n, g, left), bal = led.Balance(rt.Chest).Mites;
            long cost = (long)(((Int128)units * ppu + PriceCurve.Micro - 1) / PriceCurve.Micro);
            if (cost > bal) { units = (long)((Int128)bal * PriceCurve.Micro / ppu); cost = (long)(((Int128)units * ppu + PriceCurve.Micro - 1) / PriceCurve.Micro); }
            if (units <= 0 || cost <= 0 || cost > bal) continue;
            if (!led.Sink(rt.Chest, new Coin(cost), SinkReason.Emigrate)) continue;
            link.Mine.EmigrantCoin += cost;
            rt.Baggage.Create(g, units, CreateReason.Import); rt.Baggage.Destroy(g, units, why);
            long eq = Math.Min(left, Measure(n, g, units));
            res.Delivered[(int)n, (int)SupplySource.Market] += eq; left -= eq;
        }
        return left;
    }

    // ------------------------------------------------------------------ the disease rule's army camp

    /// <summary>The people of the army camp this person is in: his host's men and followers when the host is camped or besieging and he is on its muster or is its lord; 0 otherwise (the disease rule's <c>ArmyCampSize</c>).</summary>
    public int ArmyCampSizeOf(ulong personId)
    {
        var pt = _pop.People; int p = pt.IndexOf(new EntityId(personId));
        HostAccount? a = null;
        if (p >= 0 && Book.OnCampaign.TryGetValue(p, out int host)) Book.Hosts.TryGetValue(host, out a);
        if (a == null) foreach (var x in Book.Hosts.Values) if (x.Lord == personId) { a = x; break; }
        return a != null && a.Mode is HostMode.Camp or HostMode.Siege ? a.Total + a.Followers : 0;
    }

    private void BindDisease()
    {
        if (_diseaseBound || DiseaseSource?.Invoke() is not { } b) return;
        var prev = b.ArmyCampSize;
        b.ArmyCampSize = id => Math.Max(ArmyCampSizeOf(id), prev?.Invoke(id) ?? 0);
        _diseaseBound = true;
    }

    private List<MarketBook> BooksInReach(HostAccount a)
    {
        var list = new List<(long D2, int S, MarketBook B)>();
        var region = _pop.Region; long r2 = (long)a.BuyRadiusM * a.BuyRadiusM;
        foreach (var b in Economy.Markets.AllBooks())
        {
            if (b.Blockaded) continue;
            var s = region.Settlements[b.SettlementIndex];
            long dx = s.CenterX - a.PlaceXM, dy = s.CenterY - a.PlaceYM, d2 = dx * dx + dy * dy;
            if (d2 <= r2) list.Add((d2, b.SettlementIndex, b));
        }
        list.Sort((x, y) => x.D2 != y.D2 ? x.D2.CompareTo(y.D2) : x.S.CompareTo(y.S));
        return [.. list.Select(x => x.B)];
    }

    // ------------------------------------------------------------------ pay

    private void PayDay(HostAccount a, Rt rt, DayResult res, DayNeeds need)
    {
        var led = _econ.Ledger;
        var members = LevyHouseholds(a);
        long short_ = 0;
        foreach (var role in SupplyConfig.PayOrder)
        {
            int r = (int)role, n = a.Men[r]; if (n == 0) continue;
            long due = (long)n * WageToday(a, role); if (due <= 0) { if (a.Arrears[r] == 0) a.UnpaidDays[r] = 0; continue; }
            long pay = Math.Min(due, led.Balance(rt.Chest).Mites);
            if (pay > 0) PayRole(a, rt, role, pay, members);
            res.Paid[r] += pay; res.Delivered[(int)SupplyNeed.Pay, (int)SupplySource.Baggage] += pay;
            if (pay < due) { a.Arrears[r] += due - pay; a.UnpaidDays[r]++; short_ += due - pay; }
        }
        PayArrears(a, rt, res);
        res.ShortG[(int)SupplyNeed.Pay] = short_;
        for (int r = 0; r < Roster.Roles; r++) if (a.Arrears[r] == 0) a.UnpaidDays[r] = 0;
    }

    /// <summary>What is left of the chest after the day's wages pays the arrears, in the pay order (<c>res</c> null at Disband).</summary>
    private void PayArrears(HostAccount a, Rt rt, DayResult? res)
    {
        var led = _econ.Ledger; var members = LevyHouseholds(a);
        foreach (var role in SupplyConfig.PayOrder)
        {
            int r = (int)role; if (a.Arrears[r] <= 0) continue;
            long pay = Math.Min(a.Arrears[r], led.Balance(rt.Chest).Mites); if (pay <= 0) continue;
            PayRole(a, rt, role, pay, members);
            a.Arrears[r] -= pay;
            if (res != null) { res.Paid[r] += pay; res.Delivered[(int)SupplyNeed.Pay, (int)SupplySource.Baggage] += pay; }
            if (a.Arrears[r] == 0) a.UnpaidDays[r] = 0;
        }
    }

    /// <summary>The households of the host's levies (person count by household, ascending), for the levies' pay.</summary>
    private SortedDictionary<int, int> LevyHouseholds(HostAccount a)
    {
        var d = new SortedDictionary<int, int>(); var hhOf = _pop.People.Household;
        foreach (var (p, host) in Book.OnCampaign) if (host == a.Id && hhOf[p] >= 0) d[hhOf[p]] = d.GetValueOrDefault(hhOf[p]) + 1;
        return d;
    }

    /// <summary>Pays a role's coin out of the chest: a levy person's share to his household's purse (so the home keeps what he earns), everything else to the host's soldiers' purse (settled at <see cref="Disband"/>).</summary>
    private void PayRole(HostAccount a, Rt rt, TroopRole role, long pay, SortedDictionary<int, int> levyHouseholds)
    {
        var led = _econ.Ledger; long toHouseholds = 0;
        if (role == TroopRole.Levy && levyHouseholds.Count > 0 && a.Men[(int)TroopRole.Levy] > 0)
        {
            int persons = 0; foreach (int c in levyHouseholds.Values) persons += c;
            long share = pay * Math.Min(persons, a.Men[(int)TroopRole.Levy]) / a.Men[(int)TroopRole.Levy], per = persons > 0 ? share / persons : 0;
            if (per > 0)
                foreach (var (hh, c) in levyHouseholds)
                    if (led.Transfer(rt.Chest, _econ.HouseholdPurse[hh], new Coin(per * c), TransferReason.Wage)) toHouseholds += per * c;
        }
        long rest = pay - toHouseholds;
        if (rest > 0) led.Transfer(rt.Chest, rt.Soldiers, new Coin(rest), TransferReason.Wage);
    }

    /// <summary>The soldiers' coin goes home: the outsiders' share leaves the region with them (the continent side takes it), the rest is spent at the settlement the levies came from, shared among its households (ascending).</summary>
    private void SettleSoldiers(HostAccount a, Rt rt)
    {
        var led = _econ.Ledger; long bal = led.Balance(rt.Soldiers).Mites; if (bal <= 0) return;
        int men = Math.Max(1, a.Total), outside = Math.Min(a.Outside, men);
        long away = a.Total > 0 ? bal * outside / men : 0;
        var link = Economy.World.Get<ContinentLink>();
        if (away > 0)
        {
            if (link != null) { if (led.Sink(rt.Soldiers, new Coin(away), SinkReason.Emigrate)) link.Mine.EmigrantCoin += away; }
            else away = 0;
        }
        long home = led.Balance(rt.Soldiers).Mites; if (home <= 0) return;
        var hhs = new List<int>(); int s = a.HomeSettlement >= 0 ? a.HomeSettlement : a.PlaceSettlement;
        var hh = _pop.Households;
        for (int i = 0; i < hh.Count; i++) if (hh.Settlement[i] == s && hh.MemberCount[i] > 0) hhs.Add(i);
        if (hhs.Count == 0) { if (!rt.Treasury.IsNone) led.Transfer(rt.Soldiers, rt.Treasury, new Coin(home), TransferReason.Hoard); return; }
        long per = home / hhs.Count;
        if (per > 0) foreach (int i in hhs) led.Transfer(rt.Soldiers, _econ.HouseholdPurse[i], new Coin(per), TransferReason.Wage);
        long rem = led.Balance(rt.Soldiers).Mites;
        if (rem > 0) led.Transfer(rt.Soldiers, _econ.HouseholdPurse[hhs[0]], new Coin(rem), TransferReason.Wage);
    }

    // ------------------------------------------------------------------ men

    /// <summary>The farming planner's hook: this person is on campaign and his household has lost his labour (army-economy 3.3, 4.9).</summary>
    public bool IsOnCampaign(int person) => Book.OnCampaign.ContainsKey(person);

    /// <summary>
    /// Levies from a fief (army-economy 3.3; P7-EA3c, then P7-EA3d, overview 10d: the levy is drawn by village). The fief's quota is filled village by village, in the lord's order: the villages nearest the fief's seat first (they reach
    /// the lord's banner soonest), the seat itself, the lord's own demesne village, last. A village sends one man a household (a man aged 16 to 45, not on a staff, not already on campaign), field hands first (a farmer, ploughman, labourer or
    /// shepherd, or an unoccupied man of a peasant household), the man who is not the head before the head, in household order, up to a cap of <see cref="Ravensguard.Content.FarmingSettingsDef.LevyVillageCapPct"/> per cent of the village's
    /// households (at least one man) before the next village is asked: so the drawn villages lose most of their reapers while the undrawn ones keep theirs. A household that evades (<see cref="Evades"/>) gives none; a man who is his
    /// household's only working adult is asked after the households that have another (P7-AM-B, overview 8.2).
    /// The men are marked on campaign, counted as levies of the host, and recorded against their village and year (<see cref="WarSupplyBook.Levied"/>) for the harvest's chronicle.
    /// </summary>
    public MusterResult Muster(HostId h, int fief, int men)
    {
        var a = Acc(h); int day = Day, year = day / TimeConst.DaysPerYear;
        var hh = _pop.Households; var pt = _pop.People; var region = _pop.Region; var picked = new List<int>(); int evaded = 0, asked = 0;
        int seat = fief >= 0 && fief < region.Fiefs.Count ? region.Fiefs[fief].SeatSettlement : -1;
        int capPct = Math.Clamp(Economy.Farming.Settings.Def.LevyVillageCapPct, 1, 100);
        var byVillage = new SortedDictionary<int, List<(int Round, bool Sole, bool Hand, int Hh, int Man)>>(); var households = new Dictionary<int, int>();
        for (int i = 0; i < hh.Count; i++)
        {
            int s = hh.Settlement[i]; if (s < 0 || s >= region.Settlements.Count || region.Settlements[s].FiefIndex != fief || hh.MemberCount[i] == 0) continue;
            households[s] = households.GetValueOrDefault(s) + 1;
            var able = new List<(bool Hand, bool Head, int Man)>();
            int workers = 0;                                                                                // P7-AM-B: the household's working adults (16 to 59, alive, men and women): a man who is the only one is asked last
            foreach (int m in hh.Members(i)) { if (pt.Status[m] != PersonStatus.Alive) continue; int a0 = pt.AgeYears(m, day); if (a0 >= 16 && a0 < 60) workers++; }
            foreach (int m in hh.Members(i))
            {
                if (pt.Sex[m] != Sex.Male || pt.Status[m] != PersonStatus.Alive || Book.OnCampaign.ContainsKey(m) || Economy.Labour.IsStaff(m) || Economy.IsWarbandMan?.Invoke(m) == true) continue;       // P7-AM-B: not a retainer of a warband or a band, nor a released levy still on the road home
                int age = pt.AgeYears(m, day); if (age < SupplyConfig.LevyMinAge || age > SupplyConfig.LevyMaxAge) continue;
                var occ = pt.Occupation[m];
                bool hand = occ is Occupation.Farmer or Occupation.Ploughman or Occupation.Labourer or Occupation.Shepherd || (occ == Occupation.None && hh.Class[i] == SocialClass.Peasant);
                able.Add((hand, m == hh.Head[i], m));
            }
            if (able.Count == 0) continue;
            if (!byVillage.TryGetValue(s, out var list)) byVillage[s] = list = [];
            int round = 0;                                                                                  // a household's first man is its field hand who is not the head; a second round takes the next
            foreach (var (hand, _, man) in able.OrderByDescending(x => x.Hand).ThenBy(x => x.Head).ThenBy(x => x.Man)) list.Add((round++, workers <= 1, hand, i, man));
        }
        long Dist(int s)
        {
            if (seat < 0) return 0;
            long dx = region.Settlements[s].CenterX - region.Settlements[seat].CenterX, dy = region.Settlements[s].CenterY - region.Settlements[seat].CenterY;
            return dx * dx + dy * dy;
        }
        var order = byVillage.Keys.OrderBy(s => s == seat ? 2 : households[s] < SupplyConfig.LevyVillageMinHouseholds ? 1 : 0).ThenBy(Dist).ThenBy(s => s).ToList();
        foreach (int s in order)
        {
            if (picked.Count >= men) break;
            int cap = Math.Max(1, households[s] * capPct / 100), taken = 0;
            foreach (var (_, _, _, i, pick) in byVillage[s].OrderBy(c => c.Round).ThenBy(c => c.Sole).ThenByDescending(c => c.Hand).ThenBy(c => c.Hh))      // one man a household first, a household's only worker last, field hands first, then the next men
            {
                if (picked.Count >= men || taken >= cap) break;
                asked++;
                if (Evades?.Invoke(s, i, day) == true) { evaded++; continue; }
                picked.Add(pick); taken++;
                if (a.HomeSettlement < 0) a.HomeSettlement = s;
            }
            if (taken > 0) Book.AddLevied(s, year, taken);
        }
        foreach (int p in picked) Book.OnCampaign[p] = a.Id;
        a.Men[(int)TroopRole.Levy] += picked.Count;
        return new MusterResult(picked, evaded, asked);
    }

    /// <summary>Men drawn from the settlement by musters in the given year (the farming chronicle's question at the year's close).</summary>
    public int LevyMenFrom(int settlement, int year) => Book.Levied.TryGetValue(settlement, out var l) && l.Year == year ? l.Men : 0;

    /// <summary>The persons the host has on campaign, ascending.</summary>
    public IReadOnlyList<int> Persons(HostId h) => [.. Book.OnCampaign.Where(kv => kv.Value == h.Value).Select(kv => kv.Key)];

    /// <summary>Home again: the persons' labour returns to their households and they leave the levy count.</summary>
    public void Release(HostId h, IReadOnlyList<int> persons)
    {
        var a = Acc(h); int n = 0;
        foreach (int p in persons) if (Book.OnCampaign.TryGetValue(p, out int host) && host == a.Id) { Book.OnCampaign.Remove(p); n++; }
        a.Men[(int)TroopRole.Levy] = Math.Max(0, a.Men[(int)TroopRole.Levy] - n);
    }

    /// <summary>The military's decision: the persons leave. The levies keep their kit, the arrears owed to deserters are forgiven (the share of the levy's arrears they stood for) and they walk home.</summary>
    public void Desert(HostId h, IReadOnlyList<int> persons)
    {
        var a = Acc(h); int before = a.Men[(int)TroopRole.Levy];
        Release(h, persons);
        ForgiveArrears(a, TroopRole.Levy, before, before - a.Men[(int)TroopRole.Levy]);
    }

    /// <summary>Deserters who are counts of the roster rather than persons (retinue, mercenaries, men from outside): the same settlement.</summary>
    public int DesertCounts(HostId h, TroopRole role, int count)
    {
        var a = Acc(h); int before = a.Men[(int)role], n = Math.Min(count, before);
        if (n <= 0) return 0;
        a.Men[(int)role] -= n;
        ForgiveArrears(a, role, before, n);
        a.Outside = Math.Min(a.Outside, a.Total);
        return n;
    }

    private static void ForgiveArrears(HostAccount a, TroopRole role, int before, int gone)
    {
        if (before <= 0 || gone <= 0) return;
        a.Arrears[(int)role] -= a.Arrears[(int)role] * gone / before;
        if (a.Men[(int)role] == 0) { a.Arrears[(int)role] = 0; a.UnpaidDays[(int)role] = 0; }
    }

    // ------------------------------------------------------------------ settlements and the land

    /// <summary>A blockade on or off (army-economy 4.8): the settlement's book takes no imports and sends no exports (caravans, the regional carters and the export desk skip it), and the host does not shop there.</summary>
    public void SiegeBlockade(int settlement, bool on, HostId besieger)
    {
        var bk = BookOf(settlement);
        if (on) Book.Blockades[settlement] = besieger.Value; else { Book.Blockades.Remove(settlement); Book.Starving.Remove(settlement); }
        if (bk != null) bk.Blockaded = on;
    }

    public bool IsBlockaded(int settlement) => Book.Blockades.ContainsKey(settlement);

    /// <summary>Thousandths of a day of staple the settlement holds (its households' stores, its book's stock when it has its own, and the stores its id owns) over what its people eat (1,100 g of grain a person a day).</summary>
    public long DaysOfFoodMilli(int settlement)
    {
        var hh = _pop.Households; long eq = 0, people = 0;
        for (int i = 0; i < hh.Count; i++)
        {
            if (hh.Settlement[i] != settlement || hh.MemberCount[i] == 0) continue;
            people += hh.MemberCount[i];
            eq += StapleEq(_econ.HouseholdStore[i]);
        }
        var bk = Economy.Markets.Books[settlement] ?? null;
        if (bk != null) eq += StapleEq(bk.BookStore);
        if (_econ.OtherStores.TryGetValue(_pop.Region.Settlements[settlement].Id, out var own)) eq += StapleEq(own);
        return people == 0 ? 0 : eq * 1000 / (people * 1100);
    }

    /// <summary>Whole days of food in the settlement (army-economy 3.4).</summary>
    public int DaysOfFood(int settlement) => (int)(DaysOfFoodMilli(settlement) / 1000);

    private long StapleEq(Store s) { long t = 0; foreach (var g in _stapleGoods) t += EqG(g, s.Qty(g)); if (_oats != ushort.MaxValue) t += EqG(_oats, s.Qty(_oats)); return t; }

    /// <summary>Kg of harvest lost if <paramref name="menAbsent"/> men miss <paramref name="days"/> days of the harvest window (army-economy 4.9): men x min(days, the window's 15) x 10 hours x the region's harvest per worker-hour (last year's harvest over its farming hands' hours in the window).</summary>
    public long HarvestRisk(int fief, int menAbsent, int days)
    {
        var farming = Economy.Farming; var sums = farming.State.Summaries;
        long harvestG = sums.Count > 0 ? sums[^1].HarvestG : farming.State.HarvestG;
        long hands = 0; var pt = _pop.People; var hh = _pop.Households; int day = Day;
        for (int i = 0; i < hh.Count; i++) { if (hh.MemberCount[i] == 0) continue; foreach (int m in hh.Members(i)) { int age = pt.AgeYears(m, day); if (pt.Status[m] == PersonStatus.Alive && age >= 16 && age < 60) hands++; } }
        long hours = Math.Max(1, hands * SupplyConfig.LevyHoursPerDay * TimeConst.DaysPerSeason);
        long perHourG = harvestG / hours;
        return (long)menAbsent * Math.Min(days, TimeConst.DaysPerSeason) * SupplyConfig.LevyHoursPerDay * perHourG / 1000;
    }

    /// <summary>A host pitches camp at a settlement: <see cref="CampSize"/> reads it (the disease rule's <c>ArmyCampSize</c> hook is bound by the military's wiring).</summary>
    public void Camp(HostId h, int settlement)
    {
        var a = Acc(h); a.PlaceSettlement = settlement; a.Mode = HostMode.Camp;
        if (settlement >= 0 && settlement < _pop.Region.Settlements.Count) { a.PlaceXM = _pop.Region.Settlements[settlement].CenterX; a.PlaceYM = _pop.Region.Settlements[settlement].CenterY; }
        BindDisease();
    }

    /// <summary>Men camped at or besieging a settlement.</summary>
    public int CampSize(int settlement) { int t = 0; foreach (var a in Book.Hosts.Values) if (a.PlaceSettlement == settlement && a.Mode is HostMode.Camp or HostMode.Siege) t += a.Total; return t; }
}

/// <summary>The host slot (overview 8.4: <c>HostSupply</c> 1,150, H1, acts only at hour 18): the day's <see cref="HostSupply.RunDay"/>. A no-op without a service in the world's bag, so a game with no host pays nothing and draws nothing.</summary>
[AbstractSystem]
public sealed class HostSupplySystem : AbstractSystem
{
    public HostSupplySystem() : base("host_supply", TickPhase.H1Caravans, SystemOrder.HostSupply) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 18) return;
        var hs = ctx.World.Get<HostSupply>();
        if (hs == null || hs.Book.Hosts.Count == 0) return;
        hs.Chron = RuleChron.Of(ctx.Outbox);
        hs.RunDay((int)ctx.Day);
    }
}

/// <summary>The army economy's season step (P7-AE2, S5: after the rule's dues and loans at hour 5 of a season's first day): purveyance tallies paid or defaulted, the season's scutage written. A no-op while there is nothing owed or noted.</summary>
[AbstractSystem]
public sealed class WarSupplySeasonSystem : AbstractSystem
{
    public WarSupplySeasonSystem() : base("war_supply_season", TickPhase.H6Seasonal, SystemOrder.S4Taxes + 25) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 5 || !ctx.IsSeasonStartDay) return;
        var hs = ctx.World.Get<HostSupply>();
        if (hs == null || (hs.Book.Tallies.Count == 0 && hs.Book.ScutageNotes.Count == 0)) return;
        hs.Chron = RuleChron.Of(ctx.Outbox);
        hs.SeasonStep((int)ctx.Day);
    }
}
