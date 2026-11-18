using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Economy.Markets;

public sealed partial class ContinentTrade
{
    private readonly Dictionary<(int Settlement, int Neighbour), (CaravanPath? Path, int Gate)> _paths = [];

    /// <summary>The road from a settlement's market to the best gate that leads to <paramref name="neighbour"/> (cached: a pure function of the region).</summary>
    public (CaravanPath? Path, int Gate) PathTo(int settlement, int neighbour)
    {
        if (_paths.TryGetValue((settlement, neighbour), out var hit)) return hit;
        var region = _ew.World.Population.Region; var home = RegionCoord.OfCell(region.CellX, region.CellY);
        CaravanPath? best = null; int bestGate = -1;
        var st = Link.State;
        bool realmDiffers = st.Summaries[neighbour].Realm != 255 && region.Settlements[settlement].Realm >= 0 && st.Summaries[neighbour].Realm != region.Settlements[settlement].Realm;
        for (int g = 0; g < region.Gates.Count; g++)
        {
            if (CaravanRoads.Across(home, region.Gates[g].Side).Id != neighbour) continue;
            var p = CaravanRoads.Build(region, settlement, g, realmDiffers);
            if (p != null && (best == null || p.LengthMm < best.LengthMm)) { best = p; bestGate = g; }
        }
        return _paths[(settlement, neighbour)] = (best, bestGate);
    }

    private Dictionary<int, List<MarketBook>>? _cluster;
    /// <summary>The books a market town's caravan serves: every book of its fief, ascending settlement (the carters of the fief bring their surplus to the town and take its imports home on the road).</summary>
    private List<MarketBook> ClusterOf(MarketBook b)
    {
        if (_cluster == null)
        {
            _cluster = [];
            var r = _ew.World.Population.Region;
            foreach (var bk in _books()) { int f = r.Settlements[bk.SettlementIndex].FiefIndex; int key = f >= 0 ? f : -1 - bk.SettlementIndex; if (!_cluster.TryGetValue(key, out var l)) _cluster[key] = l = []; l.Add(bk); }
        }
        int fief = _ew.World.Population.Region.Settlements[b.SettlementIndex].FiefIndex;
        return _cluster[fief >= 0 ? fief : -1 - b.SettlementIndex];
    }

    /// <summary>The market town of its fief's cluster: the book with merchants and the most people (ties the lowest settlement). Its merchants own the fief's caravans.</summary>
    private bool IsMainBook(MarketBook b)
    {
        MarketBook? best = null;
        foreach (var cb in ClusterOf(b)) if (cb.Merchants.Count > 0 && (best == null || cb.Population > best.Population)) best = cb;
        return ReferenceEquals(best, b);
    }

    /// <summary>What goods brought to a short book fetch as the book's own customers buy them down again: the integral of its ask price over the stock range they fill, less five percent for what is left over.</summary>
    private static long ImportValue(MarketBook cb, ushort g, long units, int spread)
    {
        long stock = cb.Goods[g].StockUnits;
        long mid = cb.Curve(g).BuyMid(stock + units, units);
        return (long)(((Int128)mid * (1000 + spread) + 500) / 1000) * 95 / 100;
    }

    private static long Price(long refMicro, long qty, int permille) => (long)(((Int128)refMicro * qty * permille / 1000 + 500_000) / 1_000_000);

    /// <summary>Hours of daylight travel a game day allows (caravans spec 3.1: sunrise + 0.5 h to sunset - 0.5 h), by season.</summary>
    public static (int From, int To) Window(Season s) => s switch { Season.Summer => (5, 19), Season.Winter => (8, 16), _ => (6, 18) };
    public static bool InWindow(Season s, int hourOfDay) { var (a, b) = Window(s); return hourOfDay >= a && hourOfDay < b; }

    private sealed class Plan
    {
        public List<(MarketBook Book, ushort Good, long Units)> Exports = [];
        public List<(int Dest, ushort Good, long Units)> Imports = [];
        public long Profit, Cost, Revenue, Purchase;
        public int EdgeIndex;
        public CaravanPath Path = null!;
        public int Gate, Neighbour;
        public long ImportBudget;
        public int Used;
    }

    // ------------------------------------------------------------------ the day's planning

    private void PlanDay(GameTime now)
    {
        var st = Link.State; int region = Link.Region; int day = (int)now.TotalDays;
        foreach (var c in st.Caravans.All.ToList())
            if (c.Home == region && c.State == CaravanState.Lost && day - c.LostDay > TimeConst.DaysPerSeason) st.Caravans.Remove(c.Id);
        var rl = st.Link(region);
        if (rl.Edges.Count == 0) return;
        foreach (var b in _books())
        {
            if (b.Merchants.Count == 0 || !IsMainBook(b)) continue;
            foreach (var m in b.Merchants)
            {
                var biz = _ew.Production.ById(m.Id);
                if (biz == null || biz.State != BusinessState.Open || biz.Owner < 0 || _ew.World.People.Status[biz.Owner] != PersonStatus.Alive) continue;
                var car = FindOf(st, region, biz.Index);
                if (car != null && car.State != CaravanState.Idle) continue;
                TryTrip(now, st, rl, b, m, biz, car);
            }
        }
    }

    private static Caravan? FindOf(ContinentState st, int region, int business)
    {
        foreach (var c in st.Caravans.All) if (c.Home == region && c.OwnerBusiness == business && c.State != CaravanState.Lost) return c;
        return null;
    }

    private void TryTrip(GameTime now, ContinentState st, RegionLink rl, MarketBook b, MarketParty m, Business biz, Caravan? car)
    {
        var led = _econ.Ledger; long cash = led.Balance(m.Purse).Mites;
        long wagon = Vehicles.PriceMites(VehicleKind.OxWagon);
        long loanRoom = car == null ? Advanceable(b) : 0;
        int owned = car?.Vehicles.Length ?? 0;
        // the fleet the merchant could field: what he owns, plus wagons he can pay for (founding also on his lord's advance)
        int canBuy = (int)Math.Max(0, (cash + loanRoom - FoundingCashMites) / wagon);
        int maxFleet = Math.Min(MaxVehicles, Math.Max(owned, owned + canBuy));
        if (maxFleet < 1) return;
        if (car == null && st.Caravans.ActiveCount() >= CaravanTable.MaxActive) return;
        int guards = car?.Guards ?? 0;
        Plan? best = null;
        foreach (var et in rl.Edges)
        {
            var nb = st.Summaries[et.Neighbour]; var edge = st.Edges[et.Edge];
            if (edge.Closed || nb.Pop <= 0 || nb.Detailed || et.PayBudgetMites <= 0) continue;
            var (path, gate) = PathTo(b.SettlementIndex, et.Neighbour); if (path == null) continue;
            foreach (int use in UseCounts)
            {
                if (use > maxFleet) break;
                var plan = Evaluate(now, st, et, nb, b, m, path, gate, FleetOf(car, use), guards, cash + loanRoom, car == null || use > owned);
                if (plan != null) { plan.Used = use; if (best == null || plan.Profit > best.Profit) best = plan; }
            }
        }
        if (best == null) return;
        Launch(now, st, b, m, biz, car, best);
    }

    private static VehicleKind[] FleetOf(Caravan? car, int use)
    {
        var k = new VehicleKind[use];
        for (int i = 0; i < use; i++) k[i] = car != null && i < car.Vehicles.Length ? car.Vehicles[i] : VehicleKind.OxWagon;
        return k;
    }

    public const int MaxVehicles = 8;
    private static readonly int[] UseCounts = [1, 2, 3, 4, 6, 8];

    /// <summary>Hired drivers a train of <paramref name="vehicles"/> needs: the master leads the first two wagons, each hired driver two more.</summary>
    public static int DriversFor(int vehicles) => Math.Max(0, (vehicles - 1) / 2);

    // ------------------------------------------------------------------ the margin

    private Plan? Evaluate(GameTime now, ContinentState st, EdgeTrade et, RegionSummary nb, MarketBook b, MarketParty m, CaravanPath path, int gate, VehicleKind[] kinds, int guards,
        long cash, bool founding)
    {
        var cat = _econ.Goods; int n = cat.Count; var money = _econ.Money; int spread = money.SpreadHalfPermille;
        long capMassG = 0, capVolMl = 0; int slowest = int.MaxValue;
        foreach (var k in kinds) { capMassG += Vehicles.CapKg(k) * 1000L; capVolMl += Vehicles.CapLitres(k) * 1000L; slowest = Math.Min(slowest, Vehicles.SpeedMmPerGs(k)); }
        int day = (int)now.TotalDays; int age = Math.Max(0, day - nb.PriceDay);
        int ageDown = Math.Max(0, 1000 - age * PriceAgeDiscountPermillePerDay), ageUp = 1000 + age * PriceAgeDiscountPermillePerDay;

        // days on the road: there and back over the daylight hours, plus a day at each end
        var (w0, w1) = Window(now.Season);
        long travelS = path.RoundMm / Math.Max(1, slowest);
        long tripDays = (travelS + (w1 - w0) * 3600L - 1) / ((w1 - w0) * 3600L) + 1;
        int drivers = DriversFor(kinds.Length);
        long wages = tripDays * (drivers * DriverWageMites + guards * GuardWageMites);
        long fodder = tripDays * 8 * kinds.Sum(Vehicles.Draught) * 20;                  // 8 kg of hay or grain a day an animal, bought at the camps at about 20 mites a kilo
        long tolls = 0; foreach (var t in path.Tolls) tolls += t.Kind == TollKind.Bridge ? 2L * kinds.Length * TollBridgeMites : (long)kinds.Length * TollGateMites;
        long transport = wages + fodder + tolls + (founding ? 0 : 0);

        var exportC = new List<(ushort G, long Units, long Cost, long Rev, long PerKg)>();
        var importC = new List<(ushort G, long Units, long Cost, long Rev, long PerKg)>();
        var cluster = ClusterOf(b); var exportPieces = new Dictionary<ushort, List<(MarketBook, long)>>(); var importDest = new Dictionary<ushort, List<(int, long)>>();
        long payLeft = et.PayBudgetMites - et.CoinNetIn;
        for (ushort g = 0; g < n; g++)
        {
            if (_local[g]) continue;
            var gb = b.Goods[g];
            long massPerUnit = Math.Max(1, cat.MassG(g, 1));
            int k = Array.IndexOf(ContinentRules.TrackedGoods, cat.StringId(g));
            int idx = k >= 0 ? nb.PriceIdx[k] : 1000;
            long refMicro = gb.RefMicro; if (refMicro <= 0) continue;
            long bidMicro = refMicro * idx / 1000 * (1000 - spread) / 1000 * ageDown / 1000, askMicro = refMicro * idx / 1000 * (1000 + spread) / 1000 * ageUp / 1000;
            long fitMass = capMassG / massPerUnit;
            long volPerUnit = Math.Max(1, cat.VolumeMl(g, 1)); long fitVol = capVolMl / volPerUnit; long fit = Math.Min(fitMass, fitVol);
            if (fit <= 0) continue;
            // export: books of the fief hold well beyond their cover; the merchant offers the lots' owners 88 percent of what the gate pays (his margin and the road's cost), the dearest floors first
            if (bidMicro > 0)
            {
                long contract = bidMicro * ExportContractPermille / 1000, maxByPay = (long)((Int128)Math.Max(0, payLeft) * 1_000_000 / bidMicro);
                long u = 0, cost = 0; var pieces = new List<(MarketBook, long)>();
                foreach (var cb in cluster)
                {
                    var cg = cb.Goods[g]; long ccover = cg.Demand * cg.TargetCoverDays; if (ccover <= 1 || cg.StockUnits * 1000 <= ccover * 1600) continue;
                    long take = Math.Min(Math.Min(cg.StockUnits - ccover * 13 / 10, Math.Max(1, (cg.StockUnits - ccover) * 6 / 10)), Math.Min(fit, maxByPay) - u);
                    if (take <= 0) break;
                    long c = Price(contract, take, 1000), curve = cb.QuoteBuy(g, take); if (curve > 0 && curve < c) c = curve;
                    pieces.Add((cb, take)); u += take; cost += c;
                }
                if (u >= 1 && cost > 0)
                {
                    long rev = Price(bidMicro, u, 1000);
                    long perKg = (rev - cost) * 1_000_000 / (u * massPerUnit);       // micro-mites per gram of margin
                    if (rev > cost) { exportC.Add((g, u, cost, rev, perKg)); exportPieces[g] = pieces; }
                }
            }
            // import: books of the fief are short and the gate sells below what they would pay; each book's share goes home with the caravan
            if (!_made[g])
            {
                long want = 0, rev = 0; var dest = new List<(int, long)>();
                foreach (var cb in cluster)
                {
                    var cg = cb.Goods[g]; long ccover = cg.Demand * cg.TargetCoverDays;
                    if (ccover <= 1 || cg.StockUnits * 1000 >= ccover * 500 || !(cg.StructuralDemand > 0 || cg.LastTradedDay >= 0 || cg.DemandEma > 0)) continue;
                    long w1u = Math.Min(Math.Max(1, ccover * 7 / 10 - cg.StockUnits), fit - want);
                    if (k >= 0) w1u = Math.Min(w1u, (et.SpareG[k] - want * massPerUnit) / massPerUnit);
                    if (w1u < 1) continue;
                    rev += ImportValue(cb, g, w1u, spread); want += w1u; dest.Add((cb.SettlementIndex, w1u));
                }
                if (want >= 1)
                {
                    long cost = Price(askMicro, want, 1000);
                    long perKg = cost > 0 ? (rev - cost) * 1_000_000 / (want * massPerUnit) : 0;
                    if (rev > cost && cost > 0) { importC.Add((g, want, cost, rev, perKg)); importDest[g] = dest; }
                }
            }
        }
        if (exportC.Count == 0 && importC.Count == 0) return null;
        exportC.Sort((x, y) => x.PerKg != y.PerKg ? y.PerKg.CompareTo(x.PerKg) : x.G.CompareTo(y.G));
        importC.Sort((x, y) => x.PerKg != y.PerKg ? y.PerKg.CompareTo(x.PerKg) : x.G.CompareTo(y.G));
        var plan = new Plan { EdgeIndex = et.Edge, Path = path, Gate = gate, Neighbour = et.Neighbour };
        long purchase = 0, revenue = 0, massLeft = capMassG, volLeft = capVolMl, budget = Math.Max(0, cash - FoundingCashMites / 2 - (founding ? Vehicles.PriceMites(kinds[0]) * kinds.Length : 0));
        foreach (var (g, u, cost, rev, _) in exportC)
        {
            var tmp = new List<(ushort, long)>(); FillLine(cat, tmp, g, u, cost, rev, ref purchase, ref revenue, ref massLeft, ref volLeft, ref budget, true);
            if (tmp.Count == 0) continue;
            long left = tmp[0].Item2;                                              // units the load takes, spread over the books in order
            foreach (var (pb, pu) in exportPieces[g]) { long t = Math.Min(pu, left); if (t > 0) plan.Exports.Add((pb, g, t)); left -= t; }
        }
        long impPurchase = 0, impRevenue = 0; massLeft = capMassG; volLeft = capVolMl; long impBudget = budget + revenue / 2;
        foreach (var (g, u, cost, rev, _) in importC)
        {
            var tmp = new List<(ushort, long)>(); FillLine(cat, tmp, g, u, cost, rev, ref impPurchase, ref impRevenue, ref massLeft, ref volLeft, ref impBudget, false);
            if (tmp.Count == 0) continue;
            long left = tmp[0].Item2;
            foreach (var (d, du) in importDest[g]) { long t = Math.Min(du, left); if (t > 0) plan.Imports.Add((d, g, t)); left -= t; }
        }
        if (plan.Exports.Count == 0 && plan.Imports.Count == 0) return null;
        long gross = revenue - purchase + impRevenue - impPurchase;
        long totalSpend = purchase + impPurchase;
        long capital = totalSpend * CapitalCostPermillePerDay * tripDays / 1000;
        long risk = (revenue + impRevenue) * (RiskPremiumPct) / 100;
        long profit = gross - transport - capital - risk;
        plan.Profit = profit; plan.Cost = transport + capital + risk; plan.Revenue = revenue + impRevenue; plan.Purchase = totalSpend; plan.ImportBudget = impPurchase;
        if (profit <= 0 || profit * 100 < (totalSpend + transport) * MinMarginPct) return null;
        return plan;
    }

    private static void FillLine(GoodCatalog cat, List<(ushort, long)> into, ushort g, long units, long cost, long rev, ref long purchase, ref long revenue, ref long massLeft, ref long volLeft, ref long budget, bool exportSide)
    {
        long massPerUnit = Math.Max(1, cat.MassG(g, 1)), volPerUnit = Math.Max(1, cat.VolumeMl(g, 1));
        long take = Math.Min(units, Math.Min(massLeft / massPerUnit, volLeft / volPerUnit));
        if (take <= 0) return;
        long c = cost * take / units, r = rev * take / units;
        if (c > budget) { take = take * budget / Math.Max(1, c); if (take <= 0) return; c = cost * take / units; r = rev * take / units; }
        into.Add((g, take)); purchase += c; revenue += r; massLeft -= take * massPerUnit; volLeft -= take * volPerUnit; budget -= c;
        _ = exportSide;
    }

    // ------------------------------------------------------------------ launching a trip

    private void Launch(GameTime now, ContinentState st, MarketBook b, MarketParty m, Business biz, Caravan? car, Plan plan)
    {
        var led = _econ.Ledger; var w = _ew.World; int day = (int)now.TotalDays; var pt = w.People;
        int use = plan.Used; long wagon = Vehicles.PriceMites(VehicleKind.OxWagon);
        if (car == null)
        {
            var crew = HireCrew(b.SettlementIndex, DriversFor(use), 0, now);
            use = Math.Min(use, 2 + 2 * crew.Length);                                  // the master and each hired driver lead two wagons
            long price = use * wagon;
            long debt = 0; ulong debtTo = 0;
            long own = led.Balance(m.Purse).Mites - FoundingCashMites / 2;
            if (own < price)
            {
                // his lord's treasury advances the rest (reason Stipend), to be repaid from the caravan's takings
                var tid = TreasuryOf(b.SettlementIndex);
                if (tid != Demography.ContinentOwner && led.TryFind(PurseKind.Treasury, tid, out var tp))
                {
                    long adv = Math.Min(price - Math.Max(0, own), led.Balance(tp).Mites / 10);
                    if (adv > 0 && led.Transfer(tp, m.Purse, new Coin(adv), TransferReason.Stipend)) { debt = adv; debtTo = tid.Value; }
                }
            }
            if (use < 1 || led.Balance(m.Purse).Mites < price) { if (debt > 0) led.Transfer(m.Purse, led.Open(PurseKind.Treasury, new EntityId(debtTo)), new Coin(debt), TransferReason.Trade); return; }
            if (!PayWainwrights(m, b, price)) return;
            var id = EntityId.Make(IdKind.Other, CaravanIdBase + EntityId.RegionBase(Link.Region) + st.NextCaravanSerial(Link.Region));
            car = new Caravan
            {
                Id = id, Home = Link.Region, Region = Link.Region, Settlement = b.SettlementIndex, OwnerBusiness = biz.Index, Master = biz.Owner, OwnerHousehold = biz.OwnerHousehold,
                Vehicles = Enumerable.Repeat(VehicleKind.OxWagon, use).ToArray(), Crew = crew, Account = led.Open(PurseKind.Caravan, id), FoundedSec = now.Seconds, State = CaravanState.Idle, AnchorSec = now.Seconds,
                DebtMites = debt, DebtTo = debtTo,
            };
            _econ.OpenStore(StoreKind.Chest, id, 0, 0, enforce: false);
            st.Caravans.Add(car);
            CaravansFounded++;
        }
        else if (use > car.Vehicles.Length)
        {
            // the fleet grows when the cargo asks for it and the merchant can pay and find drivers
            int drivers = car.Crew.Length - car.Guards;
            if (DriversFor(use) > drivers) { var more = HireCrew(b.SettlementIndex, DriversFor(use) - drivers, 0, now); car.Crew = [.. car.Crew.Take(drivers), .. more, .. car.Crew.Skip(drivers)]; drivers += more.Length; }
            use = Math.Min(use, 2 + 2 * drivers);
            int extra = use - car.Vehicles.Length;
            if (extra > 0 && led.Balance(m.Purse).Mites - FoundingCashMites / 2 >= extra * wagon && PayWainwrights(m, b, extra * wagon))
                car.Vehicles = [.. car.Vehicles, .. Enumerable.Repeat(VehicleKind.OxWagon, extra)];
            use = Math.Min(use, car.Vehicles.Length);
        }
        plan.Used = use;
        // guards for the danger of the road, hired for the trip
        int danger = DangerOf(plan.Path, st);
        int wanted = Math.Clamp(danger / 25 * ((use + 3) / 4), 0, 8);
        if (car.Guards < wanted) { var g = HireCrew(b.SettlementIndex, wanted - car.Guards, 1, now); car.Crew = [.. car.Crew, .. g]; car.Guards += g.Length; }
        car.DangerX = danger; car.Used = use;
        var store = _econ.OpenStore(StoreKind.Chest, car.Id, 0, 0, enforce: false);
        // load: buy at the home book with the merchant's coin, move into the cargo store
        long spent = 0; int day2 = day;
        foreach (var (eb, g, units) in plan.Exports) spent += LoadExport(eb, m, g, units, plan, st, store);
        long wagesAdvance = 3 * ((car.Crew.Length - car.Guards) * DriverWageMites + car.Guards * GuardWageMites);
        long fl = plan.ImportBudget + wagesAdvance + 2000;
        fl = Math.Min(fl, led.Balance(m.Purse).Mites);
        if (fl > 0) led.Transfer(m.Purse, car.Account, new Coin(fl), TransferReason.Trade);
        car.FloatMites = fl; car.PurchaseMites = spent; car.ExpectedProfitMites = plan.Profit; car.TripCostMites = plan.Cost;
        car.Cargo = plan.Imports.Select(x => new CargoLine { Good = x.Good, Units = x.Units, Dest = x.Dest }).ToList();
        car.Path = plan.Path; car.Neighbour = plan.Neighbour; car.GateIndex = plan.Gate;
        car.Dist = 0; car.PrevDist = 0; car.AnchorSec = now.Seconds; car.LastTollMm = -1; car.TradedAtGate = false;
        int jitter = DetRng.Range(w.Seed, now.TotalHours, car.Id.Value, StreamTrip, 0, 0, 4);
        car.WaitUntilSec = now.Seconds + (2 + jitter) * 3600L;
        car.DepartSec = car.WaitUntilSec;
        car.State = CaravanState.Loading;
        TripsStarted++;
    }

    /// <summary>
    /// Buys <paramref name="units"/> of a good from the book's lots, the dearest floors first (the lots the market would sell last), paying their owners the contract price out of
    /// the merchant's purse (the market toll comes out of the owner's proceeds as at any sale) and moving the goods into the caravan's store. Returns the mites paid.
    /// </summary>
    private long LoadExport(MarketBook b, MarketParty m, ushort g, long units, Plan plan, ContinentState st, Store cargo)
    {
        var led = _econ.Ledger; var gb = b.Goods[g]; var nb = st.Summaries[plan.Neighbour];
        int spread = _econ.Money.SpreadHalfPermille;
        int k = Array.IndexOf(ContinentRules.TrackedGoods, _econ.Goods.StringId(g)); int idx = k >= 0 ? nb.PriceIdx[k] : 1000;
        long contract = gb.RefMicro * idx / 1000 * (1000 - spread) / 1000 * ExportContractPermille / 1000;
        if (contract <= 0) return 0;
        b.Sync(g);
        long left = Math.Min(units, gb.StockUnits), paidTotal = 0;
        long budget = led.Balance(m.Purse).Mites;
        for (int i = gb.Lots.Count - 1; i >= 0 && left > 0; i--)
        {
            var lot = gb.Lots[i]; long q = Math.Min(lot.Qty, left);
            if (q <= 0) continue;
            long unit = Math.Min(contract, b.AskMicro(g));
            long pay = Price(unit, q, 1000);
            if (pay > budget - paidTotal) { q = (long)((Int128)(budget - paidTotal) * 1_000_000 / unit); if (q <= 0) break; pay = Price(unit, q, 1000); }
            if (lot.Owner.Purse != m.Purse && pay > 0)
            {
                if (!led.Transfer(m.Purse, lot.Owner.Purse, new Coin(pay), TransferReason.Trade)) break;
                long toll = (long)(((Int128)pay * _econ.Money.MarketTollPermille + 500) / 1000);
                if (toll > 0) led.Transfer(lot.Owner.Purse, b.TollPurse, new Coin(toll), TransferReason.MarketToll);
                paidTotal += pay;
            }
            if (!Store.Move(b.BookStore, cargo, g, q)) break;
            lot.Qty -= q; gb.StockUnits -= q; left -= q;
            if (lot.Qty == 0) gb.Lots.RemoveAt(i);
        }
        return paidTotal;
    }

    public const int ExportContractPermille = 880;
    /// <summary>What the merchant's lord will lend toward wagons: a tenth of his treasury's coin.</summary>
    private long Advanceable(MarketBook b)
    {
        var tid = TreasuryOf(b.SettlementIndex);
        return tid != Demography.ContinentOwner && _econ.Ledger.TryFind(PurseKind.Treasury, tid, out var tp) ? _econ.Ledger.Balance(tp).Mites / 10 : 0;
    }

    private EntityId TreasuryOf(int settlement)
    {
        var r = _ew.World.Population.Region;
        int fief = settlement < r.Settlements.Count ? r.Settlements[settlement].FiefIndex : -1;
        return fief >= 0 && fief < r.Fiefs.Count ? r.Fiefs[fief].Id : Demography.ContinentOwner;
    }

    /// <summary>The wagons are bought from the region's carpenters (their purses get the coin, ascending business, the market's own town first).</summary>
    private bool PayWainwrights(MarketParty m, MarketBook b, long price)
    {
        var led = _econ.Ledger; long left = price;
        var shops = _ew.Production.Businesses.Where(x => x.State == BusinessState.Open && _ew.Production.KindOf(x).Id == "carpentry").OrderBy(x => x.Settlement == b.SettlementIndex ? 0 : 1).ThenBy(x => x.Index).ToList();
        if (shops.Count == 0) return led.Transfer(m.Purse, b.TollPurse, new Coin(price), TransferReason.Trade);
        for (int i = 0; left > 0; i++)
        {
            long part = Math.Min(left, Math.Max(1, price / Math.Min(shops.Count, 3) + 1));
            if (!led.Transfer(m.Purse, shops[i % shops.Count].Purse, new Coin(part), TransferReason.Trade)) return false;
            left -= part;
        }
        return true;
    }

    public const ulong CaravanIdBase = 0x400_0000;
    public static bool IsCaravanId(EntityId id) => id.Kind == IdKind.Other && (id.Counter & EntityId.SerialMask) >= CaravanIdBase && (id.Counter & EntityId.SerialMask) < CaravanIdBase + 0x100_0000;

    private int DangerOf(CaravanPath path, ContinentState st)
    {
        var region = _ew.World.Population.Region; int hide = 0;
        var home = RegionCoord.OfCell(region.CellX, region.CellY);
        foreach (var s in region.Sites)
        {
            if (s.Kind != SiteKind.Cave || s.SubKind is not ("hideout" or "den")) continue;
            long sx = home.OriginXM + s.X, sy = home.OriginYM + s.Y;
            for (int i = 0; i < path.X.Length; i += Math.Max(1, path.X.Length / 12)) { long dx = path.X[i] - sx, dy = path.Y[i] - sy; if (dx * dx + dy * dy < 4000L * 4000) { hide++; break; } }
        }
        return Math.Clamp(15 + 10 * Math.Min(hide, 5) + st.DangerBonus, 0, 100);
    }

    /// <summary>
    /// Hires <paramref name="n"/> people from the settlement's idle adults (men of 16 to 50 with no trade and no employer, not a head of a household, not a farm child): drivers
    /// (<paramref name="role"/> 0) or guards (1). Fewer when fewer are free. They keep their household and bed; their wages are paid to it.
    /// </summary>
    private int[] HireCrew(int settlement, int n, int role, GameTime now)
    {
        if (n <= 0) return [];
        var w = _ew.World; var pt = w.People; var hht = w.Households; long day = now.TotalDays;
        var busy = new HashSet<int>();
        foreach (var c in Link.State.Caravans.All) if (c.Home == Link.Region) foreach (int p in c.Crew) busy.Add(p);
        var got = new List<int>();
        // drivers and guards come from the young men of the region with no trade of their own: farm sons and the like (sons of landed households first, the settlement's own before the rest)
        for (int pass = 0; pass < 2 && got.Count < n; pass++)
            for (int p = 0; p < pt.Count && got.Count < n; p++)
            {
                if (pt.Status[p] != PersonStatus.Alive || pt.Sex[p] != Sex.Male || pt.Occupation[p] != Occupation.None || busy.Contains(p)) continue;
                int hh = pt.Household[p]; if (hh < 0 || hht.Head[hh] == p || hht.MemberCount[hh] < 3) continue;
                if (pass == 0 && pt.HomeSettlement[p] != settlement) continue;
                int age = w.AgeOn(p, day); if (age < 18 || age > 40) continue;
                if (pt.PregnantUntilDay[p] >= 0 || pt.Spouse[p] >= 0 && role == 1) continue;
                got.Add(p); busy.Add(p);
            }
        return [.. got];
    }

    private readonly HashSet<int> _onRoad = [];
    private bool IsOnRoad(int person) => _onRoad.Contains(person);

    private void ReleaseCrew(int[] crew) { foreach (int p in crew) _onRoad.Remove(p); }
}
