using System.Diagnostics;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Continental;

/// <summary>A detailed region the tick reads and writes through its economy (a region of the scheduler, or the one region of a headless world).</summary>
public readonly record struct ResidentRegion(int Id, EconomyWorld Economy);

/// <summary>What one tick did, for the tests and the perf report.</summary>
public sealed record ContinentReport(long Season, double Ms, int Residents, int Edges, int NewsItems, long MigrantsMoved, long GrainFlowKg, long CoinFlowMites);

/// <summary>
/// The seasonal continent tick v1 (continent-tick spec 3, steps C1-C8): detailed regions export a summary, the others evolve by closed forms, trade, migrants and news move
/// along the road network, and whatever touches a detailed region becomes an order of its caravans (<see cref="EdgeTrade"/> limits) or migrants it takes in
/// (<see cref="RegionLink.PendingHouseholds"/>). Regions are processed in ascending id and edges in index order, all in integers, so the result never depends on
/// which regions happen to be resident or on the order they were stepped (G-C9). Runs at a barrier, on a worker thread, one season after the last.
/// </summary>
public static class ContinentTick
{
    /// <summary>The season index (year x 4 + season) a game time falls in.</summary>
    public static long SeasonOf(GameTime t) => (long)t.Year * 4 + (int)t.Season;

    /// <summary>True at the hour the tick is due: hour 0 of the second day of a season (after every slice of the seasonal day, see the spec notes).</summary>
    public static bool IsDue(long hour) { var t = GameTime.FromGameHours(hour); return t.Hour == 0 && t.DayOfSeason == 2; }

    public static ContinentReport Run(ContinentState st, IReadOnlyList<ResidentRegion> residents, GameTime now)
    {
        var sw = Stopwatch.StartNew();
        long season = SeasonOf(now); int day = (int)now.TotalDays;
        if (season <= st.LastSeason) return new ContinentReport(season, 0, residents.Count, st.Edges.Length, 0, 0, 0, 0);
        var res = residents.OrderBy(r => r.Id).ToArray();
        var isRes = new bool[ContinentState.Regions]; foreach (var r in res) isRes[r.Id] = true;

        Settle(st, res, isRes);
        long grainFlow = 0, coinFlow = 0;
        for (int i = 0; i < res.Length; i++) Summarise(st, res[i], day);
        for (int id = 0; id < ContinentState.Regions; id++)
        {
            var s = st.Summaries[id]; s.Detailed = isRes[id];
            if (!isRes[id] && s.Pop > 0) ClosedForm(st, s, now, season);
        }
        TradeFlows(st, isRes, ref grainFlow, ref coinFlow);
        long moved = Migration(st, res, isRes, day);
        int news = News(st, res, isRes, now, season);
        Orders(st, res, isRes);
        st.LastSeason = season; st.TicksRun++;
        st.LastTickMs = sw.Elapsed.TotalMilliseconds;
        return new ContinentReport(season, st.LastTickMs, res.Length, st.Edges.Length, news, moved, grainFlow, coinFlow);
    }

    // ------------------------------------------------------------------ settle the flows the regions made since the last tick

    private static void Settle(ContinentState st, ResidentRegion[] res, bool[] isRes)
    {
        foreach (var (id, l) in st.Links)
        {
            foreach (var e in l.Edges)
            {
                var nb = st.Summaries[e.Neighbour];
                if (!isRes[e.Neighbour])
                {
                    nb.CoinTotal -= e.CoinNetIn;
                    nb.TreasuryCoin += e.TollMites;
                    for (int k = 0; k < ContinentRules.K; k++) nb.StockG[k] = Math.Max(0, nb.StockG[k] + e.StockNetOut[k]);
                    e.CoinNetIn = 0; e.TollMites = 0; Array.Clear(e.StockNetOut);
                }
                // a detailed neighbour: the region's own ledger settles it (ResidentFlows), never the summary, which the next C1 rebuilds from the records
            }
            if (l.EmigrantPersons > 0 || l.EmigrantCoin > 0)
            {
                int to = BestDestination(st, id, isRes);
                var d = st.Summaries[to];
                d.Pop += l.EmigrantPersons; d.Households += l.EmigrantHouseholds; d.CoinTotal += l.EmigrantCoin;
                d.StockG[0] += l.EmigrantMeasures * 1_000_000 / ContinentRules.MeasuresPerKgX1000;
                l.EmigrantPersons = 0; l.EmigrantHouseholds = 0; l.EmigrantCoin = 0; l.EmigrantMeasures = 0;
            }
            if (l.PendingPersons > 0 || l.PendingCoin > 0 || l.TakenCoin > 0)
            {
                // what the region did not take goes back where it came from; what it took is already in its ledger
                int from = l.PendingFromRegion >= 0 ? l.PendingFromRegion : id;
                var d = st.Summaries[from];
                d.Pop += l.PendingPersons - l.TakenPersons; d.Households += l.PendingHouseholds - l.TakenPersons / 5;
                d.CoinTotal += l.PendingCoin - l.TakenCoin;
                d.StockG[0] += (l.PendingMeasures - l.TakenMeasures) * 1_000_000 / ContinentRules.MeasuresPerKgX1000;
                l.PendingPersons = l.PendingHouseholds = l.TakenPersons = 0; l.PendingCoin = l.TakenCoin = l.PendingMeasures = l.TakenMeasures = 0; l.PendingFromRegion = -1;
            }
        }
    }

    /// <summary>The neighbour with the most room, ties the lowest id; any region at all when the road leads nowhere.</summary>
    private static int BestDestination(ContinentState st, int from, bool[] isRes)
    {
        int best = -1; long bestScore = long.MinValue;
        foreach (int ei in st.EdgesOf[from])
        {
            var e = st.Edges[ei]; if (e.Closed) continue;
            int nb = e.Other(from); var s = st.Summaries[nb]; if (s.Pop <= 0) continue;
            long score = (isRes[nb] ? 0 : 10L * Math.Max(0, s.Vacancies)) - 5 * s.Unrest;
            if (score > bestScore) { bestScore = score; best = nb; }
        }
        if (best >= 0) return best;
        for (int i = 0; i < ContinentState.Regions; i++) if (i != from && st.Summaries[i].Pop > 0) return i;
        return from;
    }

    // ------------------------------------------------------------------ C1: summary of a detailed region

    /// <summary>Copies the aggregates of a detailed region's records into its summary (C1).</summary>
    public static void Summarise(ContinentState st, ResidentRegion r, int day)
    {
        var ew = r.Economy; var w = ew.World; var s = st.Summaries[r.Id]; var econ = w.Economy; var hht = w.Households;
        s.Detailed = true; s.LastDetailDay = day;
        s.Pop = (int)Math.Min(int.MaxValue / 4, w.Vital.Alive);
        int hh = 0, dw = 0, vac = 0;
        for (int i = 0; i < hht.Count; i++) { if (hht.MemberCount[i] > 0) hh++; if (hht.Beds[i] > 0) { dw++; if (hht.MemberCount[i] == 0) vac++; } }
        s.Households = hh; s.Dwellings = dw; s.Vacancies = vac;
        var led = econ.Ledger;
        s.CoinTotal = led.TotalOf(PurseKind.Household).Mites + led.TotalOf(PurseKind.Business).Mites + led.TotalOf(PurseKind.Person).Mites + led.TotalOf(PurseKind.Settlement).Mites;
        s.TreasuryCoin = led.TotalOf(PurseKind.Treasury).Mites;
        var cat = econ.Goods;
        long popW = 0; var acc = new long[ContinentRules.K];
        foreach (var b in ew.Markets.AllBooks()) { popW += Math.Max(1, b.Population); }
        popW = Math.Max(1, popW);
        for (int k = 0; k < ContinentRules.K; k++)
        {
            if (!cat.TryIdOf(ContinentRules.TrackedGoods[k], out ushort g)) continue;
            s.StockG[k] = Math.Max(0, cat.MassG(g, econ.Mass.Net(g)));
            foreach (var b in ew.Markets.AllBooks())
            {
                long bas = b.Goods[g].BaseRefMicro; if (bas <= 0) continue;
                acc[k] += b.MidMicro(g) * 1000 / bas * Math.Max(1, b.Population);
            }
            s.PriceIdx[k] = (int)Math.Clamp(acc[k] / popW, ContinentRules.PriceMinPermille, ContinentRules.PriceMaxPermille);
            long demand = Math.Max(1, (long)s.Pop * ContinentRules.DailyGramsPerPerson[k]);
            s.CoverX10[k] = (int)Math.Min(int.MaxValue / 2, s.StockG[k] * 10 / demand);
        }
        s.PriceDay = day;
        int famine = 0; var fs = w.Vital.FamineStreak; for (int i = 0; i < fs.Length; i++) famine = Math.Max(famine, fs[i]);
        s.FamineDays = famine;
    }

    // ------------------------------------------------------------------ C2-C5: closed forms of a region nobody visits

    public static int Isqrt(long n) { if (n <= 0) return 0; long x = (long)Math.Sqrt(n); while (x * x > n) x--; while ((x + 1) * (x + 1) <= n) x++; return (int)x; }

    /// <summary>Price of a good from its cover: <c>sqrt(target / cover)</c> clamped to 0.3..4 (spec 3.3), as permille.</summary>
    public static int PriceFromCover(int k, long stockG, long pop)
    {
        long demand = Math.Max(1, pop * ContinentRules.DailyGramsPerPerson[k]);
        long coverX10 = stockG * 10 / demand;
        if (coverX10 <= 0) return ContinentRules.PriceMaxPermille;
        long q = 1_000_000L * ContinentRules.TargetCoverDays[k] * 10 / coverX10;
        return Math.Clamp(Isqrt(q), ContinentRules.PriceMinPermille, ContinentRules.PriceMaxPermille);
    }

    private static void ClosedForm(ContinentState st, RegionSummary s, GameTime now, long season)
    {
        long pop = Math.Max(1, s.Pop);
        var sn = now.Season;
        // C3 food (grain): harvest in autumn, the season's eating, spoilage
        long grain = s.StockG[0];
        if (sn == Season.Autumn)
        {
            int w = HarvestFactor(st, s.Id, now.Year);
            long harvestMeasures = s.GrainCapacityNet * w / 1000;
            grain += harvestMeasures * 1_000_000 / ContinentRules.MeasuresPerKgX1000;
        }
        long eatMeasures = pop * 15 * ContinentRules.GrainMeasuresPerDayX100 / 100;
        long eatG = eatMeasures * 1_000_000 / ContinentRules.MeasuresPerKgX1000;
        long shortG = Math.Max(0, eatG - grain);
        grain = Math.Max(0, grain - eatG);
        grain = grain * (100 - ContinentRules.SpoilagePctPerSeason) / 100;
        s.FamineDays = shortG > 0 ? s.FamineDays + 15 : 0;
        s.StockG[0] = grain;
        long shortShare = eatG > 0 ? shortG * 1000 / eatG : 0;
        // C4 demography: births and deaths with the remainder carried
        long cover = grain * 10 / Math.Max(1, pop * ContinentRules.DailyGramsPerPerson[0]);
        int foodMult = cover >= 200 ? 1000 : cover >= 80 ? 800 : cover >= 30 ? 500 : 200;
        int mortMult = (int)Math.Min(3000, 1000 + 20 * shortShare);
        long denom = 4L * 10_000 * 1000;
        long births = pop * ContinentRules.BirthRatePctX100PerYear * foodMult + s.PopRemainder;
        long deaths = pop * ContinentRules.DeathRatePctX100PerYear * mortMult;
        long net = births - deaths;
        long whole = net >= 0 ? net / denom : -((-net + denom - 1) / denom);
        s.PopRemainder = (int)(net - whole * denom);
        long cap = (long)s.Dwellings * ContinentRules.HouseCapPersonsPerDwellingX10 / 10;
        long np = Math.Max(1, pop + whole);
        if (cap > 0 && np > cap) np = cap;
        s.Pop = (int)np;
        s.Households = Math.Max(1, s.Pop / 5);
        s.Vacancies = Math.Max(0, s.Dwellings - s.Households);
        // the other tracked goods: stock relaxes toward the region's target, which wanders by year (stream 0x250), so neighbours differ and trade has a reason
        for (int k = 1; k < ContinentRules.K; k++)
        {
            long target = np * ContinentRules.DailyGramsPerPerson[k] * ContinentRules.TargetCoverDays[k];
            int f = 1000 + DetRng.Range(st.Seed, now.Year, (ulong)s.Id * 16 + (ulong)k, ContinentRules.StreamContinent, (uint)sn, -250, 251);
            s.StockG[k] += (target * f / 1000 - s.StockG[k]) * 30 / 100;
            if (s.StockG[k] < 0) s.StockG[k] = 0;
        }
        for (int k = 0; k < ContinentRules.K; k++)
        {
            s.PriceIdx[k] = PriceFromCover(k, s.StockG[k], np);
            s.CoverX10[k] = (int)Math.Min(int.MaxValue / 2, s.StockG[k] * 10 / Math.Max(1, np * ContinentRules.DailyGramsPerPerson[k]));
        }
        s.PriceDay = (int)now.TotalDays;
        s.Unrest = Math.Clamp(s.Unrest + (s.FamineDays > 0 ? 100 : -50), 0, 1000);
        // C5: closed-form regions neither mint nor sink coin; their treasuries keep what tolls brought
    }

    /// <summary>The year's harvest of a region as permille of a normal one (spec 3.2): the continent's weather where it is known, else a noise draw (mean about 1000).</summary>
    public static int HarvestFactor(ContinentState st, int region, int year)
    {
        if (st.HarvestPermille != null) return st.HarvestPermille(region, year);
        int roll = DetRng.Range(st.Seed, year, (ulong)region, ContinentRules.StreamContinent, 1, 0, 1000);
        return roll < 30 ? 475 : roll < 100 ? 800 : 940 + roll % 120;
    }

    // ------------------------------------------------------------------ C6: trade between summary regions

    private static void TradeFlows(ContinentState st, bool[] isRes, ref long grainFlowKg, ref long coinFlow)
    {
        // candidate edges: both ends are closed-form regions, ordered by the price gap of grain (largest first), then A, B
        var order = new List<(int Gap, ContinentEdge E)>();
        foreach (var e in st.Edges)
        {
            if (e.Closed || isRes[e.A] || isRes[e.B]) continue;
            var a = st.Summaries[e.A]; var b = st.Summaries[e.B]; if (a.Pop <= 0 || b.Pop <= 0) continue;
            order.Add((Math.Abs(a.PriceIdx[0] - b.PriceIdx[0]), e));
        }
        order.Sort((x, y) => x.Gap != y.Gap ? y.Gap.CompareTo(x.Gap) : x.E.A != y.E.A ? x.E.A.CompareTo(y.E.A) : x.E.B.CompareTo(y.E.B));
        foreach (var (_, e) in order)
        {
            var a = st.Summaries[e.A]; var b = st.Summaries[e.B];
            long capLeft = e.CapacityKgPerSeason * 1000;                                     // grams the road carries this season, shared by the goods
            for (int k = 0; k < ContinentRules.K && capLeft > 0; k++)
            {
                var (ex, im) = a.PriceIdx[k] <= b.PriceIdx[k] ? (a, b) : (b, a);        // the cheap side exports
                if (im.PriceIdx[k] - ex.PriceIdx[k] < 150) continue;                    // a gap under 15 percent does not pay for a cart
                long targetEx = ex.Pop * (long)ContinentRules.DailyGramsPerPerson[k] * ContinentRules.TargetCoverDays[k];
                long targetIm = im.Pop * (long)ContinentRules.DailyGramsPerPerson[k] * ContinentRules.TargetCoverDays[k];
                long surplus = ex.StockG[k] - targetEx * ContinentRules.ExportSurplusCoverX100 / 100;
                long deficit = im.StockG[k] < targetIm * ContinentRules.ImportDeficitCoverX100 / 100 ? targetIm - im.StockG[k] : 0;
                long flow = Math.Min(Math.Min(surplus, deficit), capLeft);
                if (flow <= 0) continue;
                long p = (long)(ex.PriceIdx[k] + im.PriceIdx[k]) / 2;                    // permille of base
                long unitMilli = BasePriceMilliMitesPerKg(k) * p / 1000;                 // milli-mites per kg
                long pay = flow / 1000 * unitMilli / 1000;
                if (pay > im.CoinTotal) { flow = unitMilli <= 0 ? 0 : im.CoinTotal * 1000 / unitMilli * 1000; pay = im.CoinTotal; }
                if (flow <= 0 || pay <= 0) continue;
                ex.StockG[k] -= flow; im.StockG[k] += flow; im.CoinTotal -= pay; ex.CoinTotal += pay; capLeft -= flow;
                if (k == 0) grainFlowKg += flow / 1000;
                coinFlow += pay;
                ex.PriceIdx[k] = PriceFromCover(k, ex.StockG[k], ex.Pop); im.PriceIdx[k] = PriceFromCover(k, im.StockG[k], im.Pop);
            }
        }
    }

    /// <summary>Base price of a tracked good, milli-mites per kilogram (the content's base prices: grain 45 mites/kg, etc.); set once from the goods catalog by <see cref="BindPrices"/>.</summary>
    private static long[] _basePrice = [45_000, 2_000, 493_437, 1_944_200, 23_446, 33_000, 955_525, 40_290];
    public static long BasePriceMilliMitesPerKg(int k) => _basePrice[k];
    public static void BindPrices(GoodCatalog cat)
    {
        var p = new long[ContinentRules.K];
        for (int k = 0; k < p.Length; k++) p[k] = cat.TryIdOf(ContinentRules.TrackedGoods[k], out ushort g) ? (cat.IsPiece(g) ? cat.BasePriceMilliMites(g) * 1000 / Math.Max(1, cat.MassG(g, 1)) : cat.BasePriceMilliMites(g)) : _basePrice[k];
        _basePrice = p;
    }

    // ------------------------------------------------------------------ C7: migration

    private static long Migration(ContinentState st, ResidentRegion[] res, bool[] isRes, int day)
    {
        long moved = 0;
        // a detailed region takes in refugees from starving neighbours and the odd settler while it is below its starting head count
        for (int id = 0; id < ContinentState.Regions; id++)
        {
            var s = st.Summaries[id];
            if (isRes[id] || s.Pop <= 0) continue;
            bool famine = s.FamineDays >= ContinentRules.FamineThresholdDays * 5;
            long want = (long)s.Pop * (famine ? ContinentRules.EmigrateFaminePermille : s.Unrest > 700 || s.WarFlags != 0 ? ContinentRules.EmigrateNormalPermille + 10 : ContinentRules.EmigrateNormalPermille) / 1000;
            if (want <= 0) continue;
            // destinations by descending score, ties lowest region id
            var dests = new List<(long Score, int Region, long Room)>();
            foreach (int ei in st.EdgesOf[id])
            {
                var e = st.Edges[ei]; if (e.Closed) continue;
                int nb = e.Other(id); var d = st.Summaries[nb]; if (d.Pop <= 0) continue;
                long room;
                if (isRes[nb]) room = 0;                                    // detailed regions are filled below, by their own rule
                else room = Math.Max(0, d.Vacancies) * ContinentRules.HouseCapPersonsPerDwellingX10 / 10;
                long score = 10L * d.Vacancies + 2 * Math.Min(d.CoverX10[0], 1000) / 10 - e.LengthKm - 5L * d.Unrest;
                if (!famine && d.FamineDays > 0) continue;
                dests.Add((score, nb, room));
            }
            dests.Sort((x, y) => x.Score != y.Score ? y.Score.CompareTo(x.Score) : x.Region.CompareTo(y.Region));
            foreach (var (_, nb, room) in dests)
            {
                if (want <= 0) break;
                long n = Math.Min(want, room); if (n <= 0) continue;
                MoveBetweenSummaries(s, st.Summaries[nb], n);
                want -= n; moved += n;
            }
        }
        // refugees and settlers for detailed regions
        foreach (var r in res)
        {
            var s = st.Summaries[r.Id]; var l = st.Link(r.Id);
            if (l.PendingPersons > 0) continue;
            long start = r.Economy.World.Vital.Years.Count > 0 ? r.Economy.World.Vital.Years[0].PopulationAtStart : s.Pop;
            // an offer of settlers while the region is below its starting head count; the region takes what it has dwellings and its own quota for (RegionPopulation, S7)
            long quota = 0;           // the region pulls its own returnees and settlers at S7 (PopulationMigrationSystem), at its own quota; nothing is posted any more
            bool refugeesOnly = false;
            foreach (int ei in st.EdgesOf[r.Id])
            {
                if (quota <= 0) break;
                var e = st.Edges[ei]; if (e.Closed) continue;
                int nb = e.Other(r.Id); var src = st.Summaries[nb]; if (isRes[nb] || src.Pop <= 1000) continue;
                bool famine = src.FamineDays >= ContinentRules.FamineThresholdDays * 5;
                long cap = Math.Min(quota, (long)src.Pop * (famine ? ContinentRules.EmigrateFaminePermille : ContinentRules.EmigrateNormalPermille) / 1000);
                if (cap <= 0) continue;
                long n = TakeFrom(src, cap, out long coin, out long measures);
                if (n <= 0) continue;
                l.TotalPosted += n; l.PendingPersons += (int)n; l.PendingHouseholds += (int)Math.Max(1, n / 5); l.PendingCoin += coin; l.PendingMeasures += measures;
                if (l.PendingFromRegion < 0 || l.PendingPersons == n) l.PendingFromRegion = nb;
                quota -= n; moved += n;
            }
            _ = refugeesOnly;
        }
        return moved;
    }

    /// <summary>Takes <paramref name="n"/> people (and their coin and grain at the region's per-head values) out of a summary.</summary>
    private static long TakeFrom(RegionSummary src, long n, out long coin, out long measures)
    {
        n = Math.Min(n, Math.Max(0, src.Pop - 1));
        long pop = Math.Max(1, src.Pop);
        coin = src.CoinTotal * n / pop;
        measures = src.StockG[0] * ContinentRules.MeasuresPerKgX1000 / 1_000_000 * n / pop / 4;       // a quarter of their share of the grain
        src.Pop -= (int)n; src.Households = Math.Max(1, src.Households - (int)Math.Max(1, n / 5)); src.CoinTotal -= coin;
        src.StockG[0] -= measures * 1_000_000 / ContinentRules.MeasuresPerKgX1000;
        src.Vacancies = Math.Max(0, src.Dwellings - src.Households);
        return n;
    }

    private static void MoveBetweenSummaries(RegionSummary from, RegionSummary to, long n)
    {
        n = TakeFrom(from, n, out long coin, out long measures);
        to.Pop += (int)n; to.Households += (int)Math.Max(1, n / 5); to.CoinTotal += coin; to.StockG[0] += measures * 1_000_000 / ContinentRules.MeasuresPerKgX1000;
        to.Vacancies = Math.Max(0, to.Dwellings - to.Households);
    }

    // ------------------------------------------------------------------ C8: news

    private static int News(ContinentState st, ResidentRegion[] res, bool[] isRes, GameTime now, long season)
    {
        int day = (int)now.TotalDays, made = 0;
        var fresh = new List<(int Origin, NewsItem Item)>();
        for (int id = 0; id < ContinentState.Regions; id++)
        {
            var s = st.Summaries[id]; if (s.Pop <= 0) continue;
            if (s.FamineDays >= 30 && !s.News.Any(n => n.Kind == NewsKind.Famine && n.Origin == id && day - n.Day < 45)) fresh.Add((id, new NewsItem(NewsKind.Famine, (byte)id, s.Pop, day, 0)));
            if (s.PriceIdx[0] >= 3000 && now.Season != Season.Autumn) fresh.Add((id, new NewsItem(NewsKind.PriceSpike, (byte)id, s.PriceIdx[0], day, 0)));
        }
        if (now.Season == Season.Autumn)
            for (int id = 0; id < ContinentState.Regions; id++)
            {
                var s = st.Summaries[id]; if (s.Pop <= 0) continue;
                if (HarvestFactor(st, id, now.Year) < 600) fresh.Add((id, new NewsItem(NewsKind.HarvestFailure, (byte)id, HarvestFactor(st, id, now.Year), day, 0)));
            }
        foreach (var (origin, item) in fresh)
        {
            // breadth-first up to three hops along the roads; each hop ages the word by the road's length at courier pace
            var seen = new HashSet<int> { origin }; var frontier = new List<int> { origin };
            Deliver(st, res, isRes, origin, item, origin, now);
            for (int hop = 1; hop <= ContinentRules.NewsMaxHops; hop++)
            {
                var next = new List<int>();
                foreach (int from in frontier)
                    foreach (int ei in st.EdgesOf[from])
                    {
                        var e = st.Edges[ei]; if (e.Closed) continue;
                        int to = e.Other(from); if (!seen.Add(to) || st.Summaries[to].Pop <= 0) continue;
                        int ageDays = (int)(e.LengthKm * 1000L * 100 / ContinentRules.NewsMetresPerGameSecondX100 / 86_400 * hop);
                        Deliver(st, res, isRes, to, item with { Hops = (byte)hop, Day = item.Day }, origin, now.Seconds >= 0 ? now : now);
                        _ = ageDays; next.Add(to); made++;
                    }
                frontier = next;
            }
            made++;
        }
        st.NewsSeen += made;
        return made;
    }

    private static void Deliver(ContinentState st, ResidentRegion[] res, bool[] isRes, int to, NewsItem item, int origin, GameTime now)
    {
        var s = st.Summaries[to];
        s.News.Add(item); if (s.News.Count > RegionSummary.NewsCap) s.News.RemoveAt(0);
        if (!isRes[to] || to == origin) return;
        foreach (var r in res) if (r.Id == to) { Chronicle(st, r, item, origin, now); break; }
    }

    private static void Chronicle(ContinentState st, ResidentRegion r, NewsItem item, int origin, GameTime now)
    {
        var w = r.Economy.World;
        string what = item.Kind switch
        {
            NewsKind.Famine => "famine", NewsKind.HarvestFailure => "a failed harvest", NewsKind.PriceSpike => "grain at famine prices", NewsKind.WarStart => "war", NewsKind.WarEnd => "peace",
            NewsKind.BanditRaid => "a bandit raid", NewsKind.CaravanLost => "a lost caravan", _ => "plague",
        };
        var cause = item.Kind is NewsKind.Famine or NewsKind.HarvestFailure or NewsKind.PriceSpike ? CauseKind.Famine : item.Kind is NewsKind.WarStart or NewsKind.WarEnd ? CauseKind.War : CauseKind.Other;
        var id = w.Ids.Allocate(IdKind.Event);
        w.Chronicle.Add(new ChronicleEntry(id, now, "word_reached", [], [new Cause(cause, null, what + " in " + st.Names[origin])],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["place"] = st.Names[r.Id], ["origin"] = st.Names[origin], ["about"] = what, ["hops"] = item.Hops.ToString(), ["text"] = $"word reached {st.Names[r.Id]} of {what} in {st.Names[origin]}" }));
    }

    // ------------------------------------------------------------------ the orders a detailed region's caravans may fill (CT4)

    private static void Orders(ContinentState st, ResidentRegion[] res, bool[] isRes)
    {
        // how many detailed regions share each summary neighbour, so its coin and spare goods are not promised twice
        var sharers = new int[ContinentState.Regions];
        foreach (var r in res) foreach (int ei in st.EdgesOf[r.Id]) sharers[st.Edges[ei].Other(r.Id)]++;
        foreach (var r in res)
        {
            var l = st.Link(r.Id);
            foreach (var e in l.Edges)
            {
                var nb = st.Summaries[e.Neighbour]; var edge = st.Edges[e.Edge];
                int share = Math.Max(1, sharers[e.Neighbour]);
                e.PayBudgetMites = edge.Closed || nb.Pop <= 0 ? 0 : nb.CoinTotal * ContinentRules.PayBudgetPermillePerSeason / 1000 / share;
                for (int k = 0; k < ContinentRules.K; k++)
                {
                    long target = nb.Pop * (long)ContinentRules.DailyGramsPerPerson[k] * ContinentRules.TargetCoverDays[k];
                    long spare = nb.StockG[k] - target * 9 / 10;               // what the neighbour has beyond nine tenths of its own cover
                    e.SpareG[k] = edge.Closed ? 0 : Math.Max(0, Math.Min(spare / 2 / share, edge.CapacityKgPerSeason * 1000));
                }
            }
        }
    }
}
