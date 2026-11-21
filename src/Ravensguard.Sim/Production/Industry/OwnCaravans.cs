using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Production.Industry;

/// <summary>
/// What a trip of a works' own caravan would earn, by part (industries 5.10, IN-18): the sales at the destination with the per-unit repricing of that book, the wages (master, drivers, guards) and the draught animals' fodder for the days on the road, the tolls, the
/// realm's customs, the wagons' wear, the market toll and the master's share, and the risk (the chance the road's ambushes take the cargo times the share an ambush takes). <c>MarginMites = Gross - Costs - Risk</c>. A quote that cannot be made says why in <see cref="Why"/>.
/// </summary>
public sealed class CaravanQuote
{
    public bool Ok; public string Why = "";
    public DestKind Kind; public int Dest;
    public long PathMm; public int Days, MovingHours, Wagons, Drivers, Guards;
    public long LoadG, UnsoldG;
    public long GrossMites;
    public long MasterMites, DriverMites, GuardMites, FodderMites, TollMites, CustomsMites, WearMites, MarketTollMites, MasterShareMites;
    public long RiskMites, AmbushPpm; public int Danger;
    /// <summary>How old the price the quote leans on is, in days (0: read from the book itself), and whether the owner's notebook had it.</summary>
    public int PriceAgeDays; public bool PriceKnown;
    public long FodderPerDay;
    public List<(ushort Good, long Qty)> Manifest { get; } = [];
    public long WagesMites => MasterMites + DriverMites + GuardMites;
    public long CostsMites => WagesMites + FodderMites + TollMites + CustomsMites + WearMites + MarketTollMites + MasterShareMites;
    public long MarginMites => GrossMites - CostsMites - RiskMites;
    /// <summary>The parts as the panel prints them: key, mites (sales first, then each cost as a positive figure, the risk last).</summary>
    public IEnumerable<(string Key, long Mites)> Parts()
    {
        yield return ("in_q_sales", GrossMites); yield return ("in_q_wages", WagesMites); yield return ("in_q_fodder", FodderMites); yield return ("in_q_tolls", TollMites); yield return ("in_q_customs", CustomsMites);
        yield return ("in_q_wear", WearMites); yield return ("in_q_market_toll", MarketTollMites); yield return ("in_q_master_share", MasterShareMites); yield return ("in_q_risk", RiskMites);
    }

    public static CaravanQuote No(string why) => new() { Ok = false, Why = why };
}

/// <summary>
/// The works' own caravans (P7-IN4, industries 5.10): the quote from the prices the owner knows and the road's risk, the hiring of a master (Trading 40 or more; 500 a day and 2 percent of the sales) and guards, the wagons bought once and kept between trips, the goods loaded from the
/// stage's store onto the caravan's own cart store, the float of coin the trip carries, and the three moments the caravan hands to its owner (<see cref="IOwnCaravans"/>): the daily pay, the sale at the far end and the settlement at home. The caravan is a real Phase 2 caravan
/// (the same road, speed, daylight, tolls, encounters and fought ambushes); its owner is the stage's business. Every coin is a ledger transfer and every good a store move: a caravan lost on the road is capital lost.
/// </summary>
public sealed class OwnCaravans : IOwnCaravans
{
    private readonly IndustryWorld _w;
    public OwnCaravans(IndustryWorld w) { _w = w; }

    private IndustryBook Book => _w.Book;
    private ProductionSystem Prod => _w.Prod;
    private Ledger Led => Prod.Econ.Ledger;
    private PersonTable People => Prod.Pop.People;
    private ContinentTrade Trade => _w.Trade!;
    private Ravensguard.Content.IndustrySettingsDef Set => Prod.Content.Industries.Settings;

    // ------------------------------------------------------------------ the road

    /// <summary>The road of an order from a settlement: a town of the region over the roads between them, or a border gate that leads to the neighbouring region (its gate's index too). Null, with the reason, when there is none.</summary>
    public CaravanPath? PathOf(int settlement, CaravanOrder o, out string why) => Route(settlement, o, out why)?.Path;

    private (CaravanPath Path, int Gate)? Route(int settlement, CaravanOrder o, out string why)
    {
        why = "";
        var trade = _w.Trade; if (trade == null || !trade.IsBound) { why = "in_caravan_no_continent"; return null; }
        var region = Prod.Pop.Region;
        if (o.Kind == DestKind.Town)
        {
            if ((uint)o.Dest >= (uint)region.Settlements.Count || o.Dest == settlement) { why = "in_caravan_bad_town"; return null; }
            if (Prod.Markets == null) { why = "in_caravan_no_market"; return null; }
            var p = CaravanRoads.BuildBetween(region, settlement, o.Dest, false);
            if (p == null) { why = "in_caravan_no_road"; return null; }
            return (p, -1);
        }
        var st = trade.Link.State; var rl = trade.Link.Mine; var et = rl.ForNeighbour(o.Dest);
        if (et == null || (uint)o.Dest >= (uint)st.Summaries.Length) { why = "in_caravan_no_gate"; return null; }
        var nb = st.Summaries[o.Dest];
        if (nb.Detailed) { why = "in_caravan_detailed"; return null; }
        if (nb.Pop <= 0 || st.Edges[et.Edge].Closed) { why = "in_caravan_closed"; return null; }
        var (path, gate) = trade.PathTo(settlement, o.Dest);
        if (path == null) { why = "in_caravan_no_road"; return null; }
        return (path, gate);
    }

    /// <summary>The seconds a train moving at <paramref name="slowestMmPerGs"/> spends on the road there and back: each segment at the road class's share of the speed.</summary>
    private static long MovingSeconds(CaravanPath p, int slowestMmPerGs)
    {
        long s = 0;
        for (int i = 0; i + 1 < p.CumMm.Length; i++)
        {
            long len = p.CumMm[i + 1] - p.CumMm[i]; int pm = Vehicles.ClassPermille((RoadClass)(i < p.Class.Length ? p.Class[i] : (byte)RoadClass.Road));
            long speed = Math.Max(1, (long)slowestMmPerGs * pm / 1000);
            s += (len + speed - 1) / speed;
        }
        return 2 * s;
    }

    /// <summary>The chance, in parts per million, that at least one ambush falls on a trip of <paramref name="hours"/> moving hours on a road of the given danger (the Phase 2 rule: an encounter every hour with 120 ppm + 30 ppm a point of danger, of which a share of danger x 40 percent are bandits).</summary>
    public static long AmbushPpm(int danger, int hours)
    {
        danger = Math.Clamp(danger, 0, 100);
        long enc = 120L + 30L * danger, hour = enc * (danger * 40 / 100) / 100;
        long surv = 1_000_000;
        for (int h = 0; h < hours && surv > 0; h++) surv = surv * (1_000_000 - hour) / 1_000_000;
        return 1_000_000 - surv;
    }

    // ------------------------------------------------------------------ the quote

    /// <summary>The goods a train of <paramref name="wagons"/> takes from a stage's store: its output goods in order, up to the load by mass and by volume.</summary>
    private List<(ushort Good, long Qty)> LoadOf(Business b, StageKind k, VehicleKind kind, int wagons)
    {
        var cat = Prod.Econ.Goods; long massLeft = (long)Vehicles.CapKg(kind) * 1000 * wagons, volLeft = (long)Vehicles.CapLitres(kind) * 1000 * wagons;
        var list = new List<(ushort, long)>();
        foreach (var g in IndustryWorld.OutputsOf(k))
        {
            long have = b.Store.Qty(g); if (have <= 0) continue;
            long mpu = Math.Max(1, cat.MassG(g, 1)), vpu = Math.Max(1, cat.VolumeMl(g, 1));
            long take = Math.Min(have, Math.Min(massLeft / mpu, volLeft / vpu)); if (take <= 0) continue;
            list.Add((g, take)); massLeft -= take * mpu; volLeft -= take * vpu;
        }
        return list;
    }

    /// <summary>
    /// The quote for a stage's order (5.10): what a trip leaving today would bring and cost, from the prices the owner knows (<paramref name="notes"/>: the hero's notebook; a price older than today is discounted for its age, 1 percent a day; none: read from the book as it stands).
    /// <paramref name="ambushPpm"/> overrides the road's own chance (tests, IN-18).
    /// </summary>
    public CaravanQuote Quote(ulong business, CaravanOrder? order, int day, PriceNotebook? notes = null, long? ambushPpm = null, int? drivers = null, int? guards = null)
    {
        if (_w.Trade == null) return CaravanQuote.No("in_caravan_no_continent");
        var r = Book.StageOf(business); if (r == null) return CaravanQuote.No("in_no_such_stage");
        var w = r.Value.Works; var s = w.Stages[r.Value.Index]; var b = _w.BusinessOf(s)!; var k = _w.KindOf(s);
        order ??= s.Order; if (order == null) return CaravanQuote.No("in_caravan_no_order");
        var rt = Route(b.Settlement, order, out string why); if (rt == null) return CaravanQuote.No(why);
        var (path, gate) = rt.Value; var kind = (VehicleKind)order.Vehicle; var cat = Prod.Econ.Goods; var markets = Prod.Markets!;
        var q = new CaravanQuote { Ok = true, Kind = order.Kind, Dest = order.Dest, PathMm = path.LengthMm, Wagons = order.Wagons };
        q.Drivers = drivers ?? ContinentTrade.DriversFor(order.Wagons); q.Guards = guards ?? order.Guards;
        foreach (var l in LoadOf(b, k, kind, order.Wagons)) { q.Manifest.Add(l); q.LoadG += cat.MassG(l.Good, l.Qty); }
        if (q.Manifest.Count == 0) { q.Ok = false; q.Why = "in_caravan_nothing"; return q; }
        var time = GameTime.FromGameHours((long)day * 24 + 9);
        int slowest = Vehicles.SpeedMmPerGs(kind);
        long moveS = MovingSeconds(path, slowest); q.MovingHours = (int)((moveS + 3599) / 3600);
        var (w0, w1) = ContinentTrade.Window(time.Season);
        q.Days = (int)((moveS + (w1 - w0) * 3600L - 1) / ((w1 - w0) * 3600L)) + 1;
        var st = Trade.Link.State; var home = markets.BookForSettlement(b.Settlement); int spread = Prod.Econ.Money.SpreadHalfPermille;
        var world = Trade.SimWorld; var policy = Ravensguard.Sim.Taxes.Tolls.PolicyOf(world);
        // the days on the road
        q.MasterMites = (long)Set.MasterWageMites * q.Days; q.DriverMites = (long)Set.DriverWageMites * q.Drivers * q.Days; q.GuardMites = (long)Set.GuardWageMites * q.Guards * q.Days;
        long hay = _w.FeedPrice(home, "hay", Prod.Content.Scale.HayMitesPerKg);
        q.FodderPerDay = 8 * hay * Vehicles.Draught(kind) * order.Wagons; q.FodderMites = q.FodderPerDay * q.Days;
        q.WearMites = (long)Vehicles.PriceMites(kind) * order.Wagons * Set.WagonWearPmPerDay * q.Days / 1000;
        foreach (var t in path.Tolls) if (t.Kind == TollKind.Bridge) q.TollMites += 2L * Ravensguard.Sim.Taxes.Tolls.Compute(policy, Ravensguard.Sim.Taxes.TollKind.Bridge, order.Wagons, 0);
        // the sales
        int age = 0; bool known = false;
        if (order.Kind == DestKind.Town)
        {
            var book = markets.BookForSettlement(order.Dest);
            foreach (var (g, qty) in q.Manifest)
            {
                var plan = book.PlanSaleToMerchants(g, qty); long gross = plan.PaidMites, sold = plan.Qty;
                if (notes != null && notes.TryGet(order.Dest, g, out var n) && book.BidMicro(g) > 0)
                {
                    int a = Math.Max(0, day - n.HeardDay); age = Math.Max(age, a); known = true;
                    gross = gross * n.BidMicro / book.BidMicro(g) * Math.Max(0, 1000 - a * Set.PriceAgePmPerDay) / 1000;
                }
                q.GrossMites += gross; q.MarketTollMites += plan.TollMites * (plan.PaidMites > 0 ? gross : 0) / Math.Max(1, plan.PaidMites);
                q.UnsoldG += cat.MassG(g, qty - sold);
            }
            var rg = Prod.Pop.Region;
            if (rg.Settlements[b.Settlement].Realm != rg.Settlements[order.Dest].Realm) q.TollMites += Ravensguard.Sim.Taxes.Tolls.Compute(policy, Ravensguard.Sim.Taxes.TollKind.Gate, order.Wagons, q.GrossMites);
        }
        else
        {
            var nb = st.Summaries[order.Dest]; var et = Trade.Link.Mine.ForNeighbour(order.Dest)!;
            age = Math.Max(0, day - nb.PriceDay); known = true;
            int down = Math.Max(0, 1000 - age * ContinentTrade.PriceAgeDiscountPermillePerDay);
            long budget = Math.Max(0, et.PayBudgetMites - et.CoinNetIn);
            foreach (var (g, qty) in q.Manifest)
            {
                int ti = Array.IndexOf(ContinentRules.TrackedGoods, cat.StringId(g)); int idx = ti >= 0 ? nb.PriceIdx[ti] : 1000;
                long bid = home.Goods[g].RefMicro * idx / 1000 * (1000 - spread) / 1000 * down / 1000;
                if (bid <= 0) { q.UnsoldG += cat.MassG(g, qty); continue; }
                long take = Math.Min(qty, (long)((Int128)budget * 1_000_000 / bid)), pay = (long)(((Int128)bid * take + 500_000) / 1_000_000);
                if (take <= 0 || pay <= 0) { q.UnsoldG += cat.MassG(g, qty); continue; }
                budget -= pay; q.GrossMites += pay; q.UnsoldG += cat.MassG(g, qty - take);
                q.CustomsMites += RuleCustoms.Quote(world, b.Settlement, cat.StringId(g), pay, export: true);
            }
            foreach (var t in path.Tolls) if (t.Kind == TollKind.RealmGate) q.TollMites += (long)ContinentTrade.TollGateMites * order.Wagons;
        }
        q.PriceAgeDays = age; q.PriceKnown = known;
        q.MasterShareMites = q.GrossMites * Set.MasterSharePm / 1000;
        q.Danger = Trade.DangerOf(path, st);
        q.AmbushPpm = ambushPpm ?? AmbushPpm(q.Danger, q.MovingHours);
        q.RiskMites = (long)((Int128)q.GrossMites * q.AmbushPpm * Set.AmbushTakePm / 1_000_000_000);
        _ = gate;
        return q;
    }

    // ------------------------------------------------------------------ sending a trip

    private ulong Fleet(CaravanOrder o) => o.Fleet;

    private Caravan? FleetOf(CaravanOrder o)
    {
        if (o.Fleet == 0 || Trade == null) return null;
        var c = Trade.Link.State.Caravans.Get(new EntityId(o.Fleet));
        return c != null && c.State != CaravanState.Lost ? c : null;
    }

    /// <summary>The persons on the caravans of this region (masters and crews), who cannot be hired for another.</summary>
    private HashSet<int> Busy()
    {
        var set = new HashSet<int>();
        foreach (var c in Trade.Link.State.Caravans.All) if (c.Home == Trade.Link.Region && c.State != CaravanState.Lost) { if (c.Master >= 0) set.Add(c.Master); foreach (int p in c.Crew) set.Add(p); }
        return set;
    }

    /// <summary>
    /// The master of a trip: the person the owner named when he is free and fit, else the best trader of the region with Trading at the minimum or better who is on no other caravan (the works' owner and <paramref name="exclude"/> never); among equal skill the one of the
    /// works' own town, then the lowest row, and among those still equal a draw on stream 0x743 keyed by the trip's hour and stage.
    /// </summary>
    private int PickMaster(Works w, WorkStage s, CaravanOrder o, int day, long hourKey, ICollection<int> exclude)
    {
        var pt = People; var busy = Busy(); int min = Set.MasterMinTrading;
        bool Fit(int p) => p >= 0 && p < pt.Count && pt.Status[p] == PersonStatus.Alive && p != w.Owner && !busy.Contains(p) && !exclude.Contains(p) && pt.SkillLevelOf(p, Skill.Trading) >= min && pt.AgeYears(p, day) >= 18;
        if (o.Master >= 0 && Fit(o.Master)) return o.Master;
        int town = _w.BusinessOf(s)!.Settlement;
        var best = new List<int>(); int bestSkill = -1, bestHome = -1;
        for (int p = 0; p < pt.Count; p++)
        {
            if (!Fit(p)) continue;
            int sk = pt.SkillLevelOf(p, Skill.Trading), home = pt.HomeSettlement[p] == town ? 1 : 0;
            if (sk > bestSkill || (sk == bestSkill && home > bestHome)) { best.Clear(); bestSkill = sk; bestHome = home; }
            if (sk == bestSkill && home == bestHome) best.Add(p);
        }
        if (best.Count == 0) return -1;
        if (best.Count == 1) return best[0];
        return best[DetRng.Range(_w.Seed, hourKey, s.Business, RngStreams.OwnCaravanTies, 0, 0, best.Count)];
    }

    /// <summary>The standing order's check each works day: a caravan goes when the stage holds enough of a load, its caravan is home and the trip's margin is above nothing.</summary>
    public void TrySendStanding(Works w, WorkStage s, int day, GameTime now)
    {
        var o = s.Order; if (o == null || Trade == null || !Trade.IsBound) return;
        if (TripOfStage(s.Business) != null) return;
        var b = _w.BusinessOf(s); if (b == null || b.State != BusinessState.Open) return;
        var kind = (VehicleKind)o.Vehicle; var k = _w.KindOf(s);
        long cap = (long)Vehicles.CapKg(kind) * 1000 * o.Wagons, load = 0;
        foreach (var g in IndustryWorld.OutputsOf(k)) load += Prod.Econ.Goods.MassG(g, b.Store.Qty(g));
        if (load * 1000 < cap * Set.MinLoadPm) return;
        var q = Quote(s.Business, o, day);
        if (!q.Ok || q.MarginMites <= 0) return;
        Send(s.Business, day, now, null, standing: true);
    }

    private OwnTrip? TripOfStage(ulong stage) { foreach (var t in Book.Trips) if (t.Stage == stage) return t; return null; }

    /// <summary>Whether a stage has a caravan of its own on the road.</summary>
    public bool Away(ulong stage) => TripOfStage(stage) != null;

    /// <summary>
    /// Sends a trip now (the "Send a caravan" button, the standing order's daily check): quotes it, buys the wagons the first time (or the missing ones), hires the drivers and guards and a master, loads the stage's goods onto the caravan's store, puts the trip's float in its
    /// purse and sets it loading. The hero's own sends pass his notebook, so the quote and the result say how old the prices are.
    /// </summary>
    public OwnResult Send(ulong business, int day, GameTime now, PriceNotebook? notes, bool standing = false)
    {
        var trade = _w.Trade; if (trade == null || !trade.IsBound) return OwnResult.No("in_caravan_no_continent");
        var r = Book.StageOf(business); if (r == null) return OwnResult.No("in_no_such_stage");
        var w = r.Value.Works; var s = w.Stages[r.Value.Index]; var o = s.Order; if (o == null) return OwnResult.No("in_caravan_no_order");
        if (w.Closed) return OwnResult.No("in_works_closed");
        if (TripOfStage(business) != null) return OwnResult.No("in_caravan_away");
        var biz = _w.BusinessOf(s)!; var k = _w.KindOf(s); var markets = Prod.Markets!; var homeBook = markets.BookForSettlement(biz.Settlement);
        var kind = (VehicleKind)o.Vehicle; long wagonPrice = Vehicles.PriceMites(kind);
        var q0 = Quote(business, o, day, notes); if (!q0.Ok) return OwnResult.No(q0.Why);
        var fleet = FleetOf(o); int have = fleet?.Vehicles.Length ?? 0, buy = Math.Max(0, o.Wagons - have);
        long need = buy * wagonPrice + q0.CostsMites;
        long hourKey = now.TotalHours;
        if (PickMaster(w, s, o, day, hourKey, []) < 0) return OwnResult.No("in_caravan_no_master", Set.MasterMinTrading);
        if (Led.Balance(biz.Purse).Mites < need) return new OwnResult(false, "bz_cannot_afford", [Quotes.Money(need)], need, CannotAfford: true);

        var st = trade.Link.State; int region = trade.Link.Region; var (path, gate) = Route(biz.Settlement, o, out _)!.Value;
        // wagons and drivers: bought once, kept between trips
        int drivers = ContinentTrade.DriversFor(o.Wagons);
        if (fleet == null)
        {
            var crew = drivers > 0 ? trade.HireCrew(biz.Settlement, drivers, 0, now) : [];
            int use = Math.Min(o.Wagons, 2 + 2 * crew.Length);
            if (!trade.PayWainwrights(biz.Party, homeBook, use * wagonPrice)) return OwnResult.No("in_caravan_no_wagons");
            var id = trade.NewCaravanId();
            fleet = new Caravan
            {
                Id = id, Home = region, Region = region, Settlement = biz.Settlement, OwnerBusiness = biz.Index, OwnerHousehold = biz.OwnerHousehold, Account = Led.Open(PurseKind.Caravan, id),
                Vehicles = Enumerable.Repeat(kind, use).ToArray(), Crew = crew, FoundedSec = now.Seconds, State = CaravanState.Idle, AnchorSec = now.Seconds,
            };
            Prod.Econ.OpenStore(StoreKind.Chest, id, 0, 0, enforce: false);
            st.Caravans.Add(fleet); trade.CaravansFounded++;
            o.Fleet = id.Value;
        }
        else if (o.Wagons > fleet.Vehicles.Length)
        {
            int haveDrivers = fleet.Crew.Length - fleet.Guards;
            if (drivers > haveDrivers) { var more = trade.HireCrew(biz.Settlement, drivers - haveDrivers, 0, now); fleet.Crew = [.. fleet.Crew.Take(haveDrivers), .. more, .. fleet.Crew.Skip(haveDrivers)]; haveDrivers += more.Length; }
            int use = Math.Min(o.Wagons, 2 + 2 * haveDrivers), extra = use - fleet.Vehicles.Length;
            if (extra > 0 && trade.PayWainwrights(biz.Party, homeBook, extra * wagonPrice)) fleet.Vehicles = [.. fleet.Vehicles, .. Enumerable.Repeat(kind, extra)];
        }
        int wagons = Math.Min(o.Wagons, fleet.Vehicles.Length);
        // guards for the trip, then the master (never a person already on the crew)
        if (o.Guards > 0) { var g = trade.HireCrew(biz.Settlement, o.Guards, 1, now); fleet.Crew = [.. fleet.Crew, .. g]; fleet.Guards += g.Length; }
        int master = PickMaster(w, s, o, day, hourKey, fleet.Crew);
        if (master < 0) { fleet.GuardsRelease(People); return OwnResult.No("in_caravan_no_master", Set.MasterMinTrading); }
        // the quote again with the train as it now stands
        var cur = new CaravanOrder { Kind = o.Kind, Dest = o.Dest, Vehicle = o.Vehicle, Wagons = wagons, Guards = fleet.Guards, Standing = o.Standing, Fleet = o.Fleet, Master = master };
        var q = Quote(business, cur, day, notes, drivers: fleet.Crew.Length - fleet.Guards);
        if (!q.Ok) { fleet.GuardsRelease(People); return OwnResult.No(q.Why); }
        long fl = Math.Min(Led.Balance(biz.Purse).Mites, q.CostsMites * Set.FloatPm / 1000 + 2000);
        // load: the goods leave the stage's store (packed goods come out of their casks first) for the caravan's own store
        var store = Prod.Econ.OpenStore(StoreKind.Chest, fleet.Id, 0, 0, enforce: false);
        var trip = new OwnTrip { Caravan = fleet.Id.Value, Stage = business, Kind = o.Kind, Dest = o.Dest, Master = master, DepartDay = day, Days = q.Days, Wagons = wagons, Guards = fleet.Guards, Float = fl, QuotedMargin = q.MarginMites, QuotedRisk = q.RiskMites, QuotedValue = q.GrossMites, QuotedGross = q.GrossMites };
        foreach (var (g, qty) in q.Manifest)
        {
            if (!Store.Move(biz.Store, store, g, qty)) throw new InvalidOperationException("stage store lost goods to the caravan");
            _w.Unpack(s, g, qty, biz.Store);
            trip.Manifest.Add((g, qty));
        }
        if (fl > 0 && !Led.Transfer(biz.Purse, fleet.Account, new Coin(fl), TransferReason.Trade)) throw new InvalidOperationException("the trip's float vanished");
        trip.Float = fl;
        trip.FodderPerDay = q.FodderPerDay;
        var path2 = path;
        fleet.Master = master; fleet.Used = wagons; fleet.Path = path2; fleet.Neighbour = o.Kind == DestKind.Town ? region : o.Dest; fleet.GateIndex = gate;
        fleet.DangerX = q.Danger; fleet.ExpectedProfitMites = q.MarginMites; fleet.TripCostMites = q.CostsMites + q.RiskMites; fleet.FloatMites = fl; fleet.PurchaseMites = 0; fleet.Cargo = [];
        fleet.Dist = 0; fleet.PrevDist = 0; fleet.AnchorSec = now.Seconds; fleet.LastTollMm = -1; fleet.TradedAtGate = false; fleet.ArrearsMites = 0;
        int jitter = DetRng.Range(_w.Seed, now.TotalHours, fleet.Id.Value, ContinentTrade.StreamTrip, 0, 0, 4);
        fleet.WaitUntilSec = now.Seconds + (2 + jitter) * 3600L; fleet.DepartSec = fleet.WaitUntilSec; fleet.State = CaravanState.Loading;
        Book.AddTrip(trip); trade.TripsStarted++;
        _ = standing;
        return new OwnResult(true, "in_caravan_sent", [wagons, Quotes.Money(q.MarginMites), q.Days], q.MarginMites, fleet.Id);
    }

    // ------------------------------------------------------------------ IOwnCaravans

    public bool Owns(Caravan c) => Book.TripOf(c.Id.Value) != null;

    private void Pay(Caravan c, int person, long wage, TransferReason why, ref long short_)
    {
        if (person < 0 || People.Status[person] != PersonStatus.Alive) return;
        int hh = People.Household[person]; if (hh < 0) return;
        long pay = Math.Min(wage, Led.Balance(c.Account).Mites);
        if (pay > 0) Led.Transfer(c.Account, Prod.Econ.HouseholdPurse[hh], new Coin(pay), why);
        if (pay < wage) short_ += wage - pay;
    }

    public void PayWages(Caravan c, GameTime now)
    {
        var t = Book.TripOf(c.Id.Value); if (t == null) return;
        long short_ = 0; int drivers = c.Crew.Length - c.Guards;
        Pay(c, c.Master, Set.MasterWageMites, TransferReason.Wage, ref short_);
        for (int i = 0; i < c.Crew.Length; i++) Pay(c, c.Crew[i], i < drivers ? Set.DriverWageMites : Set.GuardWageMites, TransferReason.Wage, ref short_);
        if (t.FodderPerDay > 0) Pay(c, c.Master, t.FodderPerDay, TransferReason.Trade, ref short_);              // the teams' hay and oats, bought along the road (paid to the master's household as the purse for the camps)
        c.ArrearsMites += short_;
        _ = now;
    }

    public void Arrive(TickContext ctx, ContinentTrade trade, Caravan c, GameTime time)
    {
        var t = Book.TripOf(c.Id.Value);
        if (t == null || t.Kind == DestKind.Neighbour)
        {
            long before = trade.ExportedMites;
            trade.GateTradeAt(ctx, c, time);
            if (t != null) t.Sales += trade.ExportedMites - before;
        }
        else
        {
            var store = Prod.Econ.OpenStore(StoreKind.Chest, c.Id, 0, 0, enforce: false);
            var book = Prod.Markets!.BookForSettlement(t.Dest); int day = (int)time.TotalDays;
            var party = new MarketParty(c.Id, c.Account, store, Ravensguard.Sim.Economy.Markets.PartyKind.Business, -1);
            long sales = 0;
            foreach (var (g, q) in store.Rows()) sales += book.SellToMerchants(party, g, q, day).PaidMites;
            var rg = Prod.Pop.Region;
            if (sales > 0 && rg.Settlements[c.Settlement].Realm != rg.Settlements[t.Dest].Realm)
                Ravensguard.Sim.Taxes.Tolls.ChargeAt(trade.SimWorld, c.Account, t.Dest, Ravensguard.Sim.Taxes.TollKind.Gate, c.Fielded, sales);
            t.Sales += sales;
            c.Cargo.Clear(); c.TradedAtGate = true; c.State = CaravanState.Inbound; c.Region = c.Home; c.WaitUntilSec = (ctx.Hour + 1) * 3600 + 2 * 3600L;
        }
        if (t != null) t.Sold = true;
    }

    public void Home(TickContext ctx, ContinentTrade trade, Caravan c, GameTime time)
    {
        var t = Book.TripOf(c.Id.Value); int day = (int)time.TotalDays;
        var r = t == null ? null : Book.StageOf(t.Stage);
        var biz = r == null ? null : _w.BusinessOf(r.Value.Works.Stages[r.Value.Index]);
        if (t == null || r == null || biz == null) { trade.EndTrip(c); return; }
        var w = r.Value.Works; var s = w.Stages[r.Value.Index]; var led = Led;
        var store = Prod.Econ.OpenStore(StoreKind.Chest, c.Id, 0, 0, enforce: false);
        foreach (var (g, q) in store.Rows()) if (!Store.Move(store, biz.Store, g, q)) throw new InvalidOperationException("the caravan's unsold goods vanished");
        // the wagons' wear to the wainwrights, the master's share, the crew's arrears, then what is left goes into the works' cash box
        var home = Prod.Markets!.BookForSettlement(biz.Settlement);
        long wear = (long)Vehicles.PriceMites(c.Vehicles.Length > 0 ? c.Vehicles[0] : VehicleKind.OxWagon) * t.Wagons * Set.WagonWearPmPerDay * Math.Max(1, day - t.DepartDay + 1) / 1000;
        wear = Math.Min(wear, led.Balance(c.Account).Mites);
        if (wear > 0) trade.PayWainwrights(biz.Party, home, wear, c.Account);
        long short_ = 0;
        Pay(c, c.Master, t.Sales * Set.MasterSharePm / 1000, TransferReason.Wage, ref short_);
        long arrears = Math.Min(c.ArrearsMites, led.Balance(c.Account).Mites);
        if (arrears > 0 && c.Crew.Length > 0)
        {
            long share = arrears / c.Crew.Length;
            foreach (int p in c.Crew) if (People.Status[p] == PersonStatus.Alive && People.Household[p] >= 0 && share > 0 && led.Transfer(c.Account, Prod.Econ.HouseholdPurse[People.Household[p]], new Coin(share), TransferReason.Wage)) c.ArrearsMites -= share;
        }
        c.ArrearsMites = 0;
        long back = led.Balance(c.Account).Mites;
        if (back > 0) led.Transfer(c.Account, biz.Purse, new Coin(back), TransferReason.Trade);
        long costs = t.Float + t.Sales - back;
        s.Add(day, WorkStage.Upkeep, Math.Max(0, costs)); w.Injected += back - t.Sales;
        w.CaravanTrips++; w.CaravanSoldMites += t.Sales; w.CaravanNetMites += back - t.Float;
        c.ProfitTotalMites += back - t.Float; c.Master = -1;
        trade.EndTrip(c);
        Book.Trips.Remove(t);
        Write(ctx, w, s, t, c, time, costs);
    }

    public void Lost(TickContext ctx, ContinentTrade trade, Caravan c, GameTime time)
    {
        var t = Book.TripOf(c.Id.Value); if (t == null) return;
        var r = Book.StageOf(t.Stage);
        if (r != null)
        {
            var w = r.Value.Works; var s = w.Stages[r.Value.Index];
            s.Add((int)time.TotalDays, WorkStage.Upkeep, t.Float);
            w.CaravansLost++; w.CaravanNetMites -= t.Float;
            if (s.Order != null && s.Order.Fleet == c.Id.Value) s.Order.Fleet = 0;
        }
        Book.Trips.Remove(t);
    }

    private void Write(TickContext ctx, Works w, WorkStage s, OwnTrip t, Caravan c, GameTime time, long costs)
    {
        var chron = RuleChron.Of(ctx.Outbox);
        var who = People.IdOf(w.Owner); var reg = Prod.Pop.Region; var place = reg.Settlements[w.Settlement].Id;
        var cause = new Cause(CauseKind.Trade, null, NoteKey: "ch_note_own_caravan");
        if (t.Kind == DestKind.Town)
            chron.Chron(time, "caravan_returned", "ch_ind_caravan", [ChronParam.Person("owner", who), ChronParam.Place("place", place), ChronParam.Place("dest", reg.Settlements[t.Dest].Id), ChronParam.Mites("sold", t.Sales), ChronParam.Mites("cost", Math.Max(0, costs))], [who, new EntityId(s.Business)], [cause]);
        else
            chron.Chron(time, "caravan_returned_gate", "ch_ind_caravan_gate", [ChronParam.Person("owner", who), ChronParam.Place("place", place), ChronParam.Mites("sold", t.Sales), ChronParam.Mites("cost", Math.Max(0, costs))], [who, new EntityId(s.Business)], [cause]);
        _ = c;
    }
}
