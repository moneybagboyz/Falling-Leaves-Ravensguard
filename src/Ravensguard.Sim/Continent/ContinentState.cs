using System.Security.Cryptography;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Continental;

/// <summary>
/// The abstract continent (plan 1B, continent-tick spec): 256 <see cref="RegionSummary"/>, the road links between adjacent regions, the news in flight, the caravans
/// on the road and, per detailed region, the <see cref="RegionLink"/> through which coin, goods and people cross between it and its neighbours. Mutated only at the
/// seasonal barrier (<see cref="ContinentTick"/>), except for its links and caravans, which one region's worker writes between two ticks.
/// </summary>
public sealed class ContinentState
{
    public const int Regions = RegionCoord.Count;
    public ulong Seed { get; }
    public RegionSummary[] Summaries { get; }
    public ContinentEdge[] Edges { get; private set; } = [];
    /// <summary>Per region: edge indices ordered by the neighbour's id.</summary>
    public int[][] EdgesOf { get; private set; }
    public SortedDictionary<int, RegionLink> Links { get; } = [];
    public CaravanTable Caravans { get; }
    /// <summary>The home region of the world (its summary starts detailed and its caravans are the ones the player may see first).</summary>
    public int Home { get; }
    /// <summary>Index of the last season the tick ran (year x 4 + season), -1 before the first.</summary>
    public long LastSeason { get; set; } = -1;
    /// <summary>True when no scheduler drives the tick: a single-region world (soak, tests) runs it from its own ticker's barrier.</summary>
    public bool Standalone { get; init; }
    /// <summary>Wall time of the last tick, milliseconds (reports only).</summary>
    public double LastTickMs { get; set; }
    public long TicksRun { get; set; }
    /// <summary>Added to every road's danger (tests force ambushes with it; 0 in play).</summary>
    public int DangerBonus { get; set; }
    /// <summary>Coin sunk by the economy into the continent and sourced back, since the world began (reports; the totals must meet).</summary>
    public long NewsSeen { get; set; }
    /// <summary>A name for each region (its largest settlement), for the chronicle's "word reached ..."; derived from the history, not saved.</summary>
    public string[] Names { get; set; } = Enumerable.Range(0, Regions).Select(i => $"region {i}").ToArray();
    /// <summary>The year's harvest of a region as permille of a normal one (the continent's weather); null: a plain noise draw.</summary>
    public Func<int, int, int>? HarvestPermille { get; set; }

    public ContinentState(ulong seed, int home)
    {
        Seed = seed; Home = home;
        Summaries = new RegionSummary[Regions];
        for (int i = 0; i < Regions; i++) Summaries[i] = new RegionSummary { Id = (byte)i };
        EdgesOf = new int[Regions][]; for (int i = 0; i < Regions; i++) EdgesOf[i] = [];
        Caravans = new CaravanTable();
    }

    private readonly object _linkGate = new();

    public RegionLink Link(int region)
    {
        lock (_linkGate)
        {
            if (Links.TryGetValue(region, out var l)) return l;
            l = new RegionLink { Region = region };
            foreach (int ei in EdgesOf[region]) l.Edges.Add(new EdgeTrade { Edge = ei, Neighbour = Edges[ei].Other(region) });
            Links[region] = l;
            return l;
        }
    }

    // ------------------------------------------------------------------ building

    /// <summary>
    /// The continent of a world: summaries from the history's settlements (scaled so the home region matches its real head count), edges from the trade routes' border
    /// gates, coin per head as in the home region.
    /// </summary>
    public static ContinentState Create(ulong seed, WorldHistory history, int home, int homePop, long homeCoinMites, bool standalone = false)
    {
        var st = new ContinentState(seed, home) { Standalone = standalone };
        var pops = new long[Regions]; var best = new int[Regions]; Array.Fill(best, -1); var bestPop = new int[Regions];
        foreach (var s in history.Settlements)
        {
            if (s.IsRuin || s.Pop <= 0) continue;
            int r = RegionCoord.OfCell(s.Cell).Id;
            pops[r] += s.Pop;
            if (s.Pop > bestPop[r]) { bestPop[r] = s.Pop; best[r] = s.Realm; }
        }
        long scaleNum = Math.Max(1, homePop), scaleDen = Math.Max(1, pops[home]);
        long coinPerPerson = homePop > 0 ? homeCoinMites / homePop : 5000;
        for (int r = 0; r < Regions; r++)
        {
            var sm = st.Summaries[r];
            sm.Realm = best[r] >= 0 ? (byte)Math.Min(254, best[r]) : (byte)255;
            long pop = r == home ? homePop : pops[r] * scaleNum / scaleDen;
            st.Seed_(sm, (int)Math.Min(int.MaxValue / 4, pop), coinPerPerson);
        }
        st.Summaries[home].Detailed = true;
        st.BuildEdges(BorderGates.For(history));
        return st;
    }

    /// <summary>A continent with no history (tests that build a bare economy): the home region and four equal neighbours joined to it by one road each.</summary>
    public static ContinentState Standalone_(ulong seed, int home, int homePop, long homeCoinMites)
    {
        var st = new ContinentState(seed, home) { Standalone = true };
        long coinPerPerson = homePop > 0 ? homeCoinMites / homePop : 5000;
        var h = RegionCoord.FromId(home);
        foreach (var rc in new[] { h, h.Offset(0, -1), h.Offset(1, 0), h.Offset(0, 1), h.Offset(-1, 0) })
            if (rc.IsValid) st.Seed_(st.Summaries[rc.Id], homePop, coinPerPerson);
        st.Summaries[home].Detailed = true;
        var edges = new List<ContinentEdge>();
        foreach (var rc in new[] { h.Offset(0, -1), h.Offset(1, 0), h.Offset(0, 1), h.Offset(-1, 0) })
            if (rc.IsValid) edges.Add(new ContinentEdge { A = Math.Min(home, rc.Id), B = Math.Max(home, rc.Id), Gates = 1, Class = RoadClass.Road });
        st.SetEdges(edges);
        return st;
    }

    /// <summary>A continent over the history's roads with nothing in it yet: the numbers come from a save (<see cref="Ravensguard.Sim.Save.ContinentSave.Restore"/>).</summary>
    public static ContinentState ForLoad(ulong seed, WorldHistory history, int home)
    {
        var st = new ContinentState(seed, home);
        st.BuildEdges(BorderGates.For(history));
        return st;
    }

    private readonly SortedDictionary<int, ulong> _serials = [];
    /// <summary>The next caravan serial of a region (saved with the continent, so ids never depend on whether the game was saved and loaded on the way).</summary>
    public ulong NextCaravanSerial(int region) { lock (_linkGate) { ulong n = _serials.GetValueOrDefault(region) + 1; _serials[region] = n; return n; } }
    public ulong[] SerialState() { lock (_linkGate) return _serials.SelectMany(kv => new[] { (ulong)kv.Key, kv.Value }).ToArray(); }
    public void SetSerialState(ulong[] s) { lock (_linkGate) { _serials.Clear(); for (int i = 0; i + 1 < s.Length; i += 2) _serials[(int)s[i]] = s[i + 1]; } }

    /// <summary>Starting numbers of a region of <paramref name="pop"/> people: steady-state stocks, base prices, coin per head.</summary>
    private void Seed_(RegionSummary s, int pop, long coinPerPerson)
    {
        s.Pop = pop; s.Households = pop / 5; s.Dwellings = s.Households + s.Households / 30 + 1; s.Vacancies = s.Dwellings - s.Households;
        s.CoinTotal = pop * coinPerPerson;
        s.GrainCapacityNet = (long)pop * 5520 / 100 * ContinentRules.HarvestOverNeedPermille / 1000;
        for (int k = 0; k < ContinentRules.K; k++)
        {
            long cover = k == 0 ? 6000 : ContinentRules.TargetCoverDays[k] * 1000L;           // grain starts in spring: a little over two months of the year's store, others at target
            s.StockG[k] = (long)pop * ContinentRules.DailyGramsPerPerson[k] * (k == 0 ? 56 : ContinentRules.TargetCoverDays[k]);
            s.PriceIdx[k] = 1000; s.CoverX10[k] = (int)(cover / 100);
        }
        s.Prosperity = 500;
    }

    private void BuildEdges(BorderGates gates)
    {
        var list = new List<ContinentEdge>();
        for (int id = 0; id < Regions; id++)
        {
            var r = RegionCoord.FromId(id);
            foreach (var (side, nb) in new[] { (BorderSide.East, r.Offset(1, 0)), (BorderSide.South, r.Offset(0, 1)) })
            {
                if (!nb.IsValid) continue;
                var g = gates.Of(r, side); if (g.Count == 0) continue;
                var cls = RoadClass.None; foreach (var x in g) if (x.Class > cls) cls = x.Class;
                list.Add(new ContinentEdge { A = id, B = nb.Id, Gates = g.Count, Class = cls });
            }
        }
        SetEdges(list);
    }

    private void SetEdges(List<ContinentEdge> list)
    {
        for (int i = 0; i < list.Count; i++) list[i].Index = i;
        Edges = [.. list];
        var per = new List<int>[Regions]; for (int i = 0; i < Regions; i++) per[i] = [];
        foreach (var e in Edges) { per[e.A].Add(e.Index); per[e.B].Add(e.Index); }
        for (int i = 0; i < Regions; i++) EdgesOf[i] = [.. per[i].OrderBy(ei => Edges[ei].Other(i))];
    }

    // ------------------------------------------------------------------ totals

    public long CoinInSummaries() { long t = 0; foreach (var s in Summaries) t += s.CoinTotal + s.TreasuryCoin; return t; }

    /// <summary>Everything the continent holds or owes, mites: summaries, treasuries, immigrants' stakes on the road, edge debts. Add every ledger's circulating coin to get the world's total (it is constant).</summary>
    public long TotalCoin()
    {
        long t = CoinInSummaries();
        foreach (var l in Links.Values)
        {
            t += l.PendingCoin - l.TakenCoin + l.EmigrantCoin;
            foreach (var e in l.Edges) t -= e.CoinNetIn;
            foreach (var e in l.Edges) t += e.TollMites;
        }
        return t;
    }

    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b, v); h.AppendData(b); }
        L(LastSeason);
        foreach (var s in Summaries)
        {
            L(s.Pop); L(s.Households); L(s.Dwellings); L(s.Vacancies); L(s.CoinTotal); L(s.TreasuryCoin); L(s.FamineDays); L(s.Unrest); L(s.PopRemainder); L(s.PriceDay); L(s.Detailed ? 1 : 0);
            foreach (long x in s.StockG) L(x); foreach (int x in s.PriceIdx) L(x);
            L(s.News.Count); foreach (var n in s.News) { L((long)n.Kind); L(n.Origin); L(n.Magnitude); L(n.Day); L(n.Hops); }
        }
        foreach (var e in Edges) { L(e.A); L(e.B); L(e.Gates); L((long)e.Class); L(e.Closed ? 1 : 0); }
        foreach (var (id, l) in Links)
        {
            L(id); L(l.EmigrantPersons); L(l.EmigrantHouseholds); L(l.EmigrantCoin); L(l.EmigrantMeasures); L(l.PendingPersons); L(l.PendingHouseholds); L(l.PendingCoin); L(l.PendingMeasures); L(l.PendingFromRegion);
            L(l.TakenPersons); L(l.TakenCoin); L(l.TakenMeasures);
            foreach (var e in l.Edges) { L(e.Edge); L(e.PayBudgetMites); L(e.CoinNetIn); L(e.TollMites); foreach (long x in e.SpareG) L(x); foreach (long x in e.StockNetOut) L(x); }
        }
        h.AppendData(System.Text.Encoding.ASCII.GetBytes(Caravans.ComputeHash()));
        return Convert.ToHexString(h.GetHashAndReset());
    }
}

/// <summary>Things a continent takes from the world it belongs to and does not save: place names and the weather's say on the year's harvest.</summary>
public static class ContinentWiring
{
    /// <summary>Names each region after its largest settlement and sets the harvest from the continent's long weather conditions (drought, wet year, cold summer) of the region's culture.</summary>
    public static void Attach(ContinentState st, Ravensguard.Sim.WorldGen.Continent continent, WorldHistory history, Ravensguard.Sim.Weather.RegionWeather weather)
    {
        var best = new int[ContinentState.Regions];
        foreach (var s in history.Settlements)
        {
            if (s.IsRuin) continue;
            int r = RegionCoord.OfCell(s.Cell).Id;
            if (s.Pop > best[r]) { best[r] = s.Pop; st.Names[r] = s.Name; }
        }
        st.HarvestPermille = (region, year) =>
        {
            var rc = RegionCoord.FromId(region);
            int cell = Math.Min(Ravensguard.Sim.WorldGen.Continent.Size - 1, rc.CellY + 8) * Ravensguard.Sim.WorldGen.Continent.Size + Math.Min(Ravensguard.Sim.WorldGen.Continent.Size - 1, rc.CellX + 8);
            var lc = weather.Fronts.Long.For(continent.CultureId[cell], year);
            int noise = DetRng.Range(st.Seed, year, (ulong)region, ContinentRules.StreamContinent, 2, 0, 100);        // hail and blight, mean about 990
            int hail = noise < 10 ? 900 + noise * 6 : 1000;
            int w = lc.Kind switch { Ravensguard.Sim.Weather.LongKind.Drought => 475, Ravensguard.Sim.Weather.LongKind.WetYear => 850, Ravensguard.Sim.Weather.LongKind.ColdSummer => 800, _ => 1000 };
            return w * hail / 1000;
        };
    }
}
