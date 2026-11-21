using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Building;

/// <summary>A wheel's output on a given day (large-buildings 4.1): the watts at the shaft, the watts the water carries before the wheel's efficiency, the flow and head it used, and why it gives nothing ("" when it works).</summary>
public sealed record WheelOutput(int Watts, int WaterW, int FlowLps, int HeadMm, string Key, object[] Args)
{
    public bool Works => Key.Length == 0;
    public static WheelOutput None(int flowLps, int headMm, string key, params object[] args) => new(0, 0, flowLps, headMm, key, args);
}

/// <summary>
/// P7-LB1 (large-buildings 4.1): water power from flow and fall. Integer arithmetic only; every number comes from <see cref="WaterPowerRulesDef"/>.
/// <list type="bullet">
/// <item>Flow at a point: <c>catchment_tenths_km2 x 15 / 10 x season_pm / 1,000</c> litres a second (the catchment at a point is the waterway's downstream catchment scaled by the distance from the source over its length); zero while frozen, doubled on a river-high day.</item>
/// <item>Head: the fall of the waterway 30 m upstream with no leat; with a leat of length L the fall over L (capped 4,000 mm); a weir adds 1,000 mm on a stream no wider than 10 m.</item>
/// <item>Output: <c>min(rated, 981 x Q x head / 100,000 x eta / 1,000)</c> watts, with the wheel's head band (a wheel outside it gives 0 and says why).</item>
/// </list>
/// </summary>
public static class WaterPower
{
    public static int SeasonPm(WaterPowerRulesDef r, Season s)
    {
        int i = (int)s;
        return i < r.SeasonPm.Count ? r.SeasonPm[i] : 1000;
    }

    /// <summary>The flow in litres a second at a point whose catchment is <paramref name="catchmentTenthsKm2"/> (before ice and floods).</summary>
    public static int FlowLps(WaterPowerRulesDef r, long catchmentTenthsKm2, Season season)
        => (int)Math.Min(int.MaxValue, catchmentTenthsKm2 * r.FlowLpsPerKm2 * SeasonPm(r, season) / (10L * 1000));

    /// <summary>The flow of a day: nothing while frozen, doubled on a river-high day.</summary>
    public static int Flow(WaterPowerRulesDef r, long catchmentTenthsKm2, Season season, bool frozen, bool riverHigh)
    {
        if (frozen) return 0;
        long q = FlowLps(r, catchmentTenthsKm2, season);
        if (riverHigh) q = q * r.RiverHighFlowPm / 1000;
        return (int)Math.Min(int.MaxValue, q);
    }

    /// <summary>The head in mm from the fall (mm) with a leat or without, and a weir where the stream is narrow enough (4.1).</summary>
    public static int HeadMm(WaterPowerRulesDef r, int fallMm, bool leat, bool weir, int widthCm)
    {
        int h = Math.Max(0, fallMm);
        if (leat) h = Math.Min(h, r.LeatHeadCapMm);
        if (weir && widthCm <= r.WeirMaxWidthCm) h += r.WeirHeadMm;
        return h;
    }

    /// <summary>The water's watts: <c>981 x Q x head / 100,000</c> (before the wheel's efficiency).</summary>
    public static int WaterWatts(int flowLps, int headMm) => (int)Math.Min(int.MaxValue, 981L * flowLps * headMm / 100_000);

    /// <summary>
    /// A wheel's output (4.1): <c>min(rated, 981 x Q x head x eta / (100,000 x 1,000))</c>. A wheel outside its head band, on water it does not suit or without the leat it needs gives 0 with the
    /// reason as an en.csv key (<c>lb_head_low</c>, <c>lb_head_high</c>, <c>lb_need_river</c>, <c>lb_need_leat</c>); a wheel with no water gives 0 (<c>lb_no_flow</c>).
    /// </summary>
    public static WheelOutput Output(PowerPieceDef wheel, int flowLps, int headMm, bool river, bool leat)
    {
        if (wheel.Water == "river" && !river) return WheelOutput.None(flowLps, headMm, "lb_need_river");
        if (wheel.NeedsLeat && !leat) return WheelOutput.None(flowLps, headMm, "lb_need_leat");
        if (headMm < wheel.HeadMinMm) return WheelOutput.None(flowLps, headMm, "lb_head_low", headMm, wheel.HeadMinMm, wheel.HeadMaxMm);
        if (wheel.HeadMaxMm > 0 && headMm > wheel.HeadMaxMm) return WheelOutput.None(flowLps, headMm, "lb_head_high", headMm, wheel.HeadMinMm, wheel.HeadMaxMm);
        if (flowLps <= 0) return WheelOutput.None(flowLps, headMm, "lb_no_flow");
        long w = 981L * flowLps * headMm * wheel.EtaPm / (100_000L * 1000);
        return new WheelOutput((int)Math.Min(wheel.RatedW, w), WaterWatts(flowLps, headMm), flowLps, headMm, "", []);
    }

    // ------------------------------------------------------------------ ice

    /// <summary>
    /// Whether the water is frozen on <paramref name="day"/> (4.1): it freezes after <c>FrozenDays</c> days in a row below <c>FrozenBelowC</c> and thaws after <c>ThawDays</c> days in a row above
    /// <c>ThawAboveC</c>. Derived, never saved: the most recent day on which either run was complete decides (looking back at most <c>IceLookbackDays</c>; none found: open water).
    /// </summary>
    public static bool Frozen(WaterPowerRulesDef r, long day, Func<long, int> meanC)
    {
        for (long d = day; d > day - r.IceLookbackDays; d--)
        {
            if (Run(d, r.FrozenDays, t => t < r.FrozenBelowC, meanC)) return true;
            if (Run(d, r.ThawDays, t => t > r.ThawAboveC, meanC)) return false;
        }
        return false;
    }

    private static bool Run(long end, int days, Func<int, bool> test, Func<long, int> meanC)
    {
        for (int i = 0; i < days; i++) if (!test(meanC(end - i))) return false;
        return true;
    }
}

/// <summary>Where a point lies on a waterway: which one, how far along it from the source (m), how far from its centre line (m), its width and kind.</summary>
public readonly record struct WaterPoint(int Waterway, int AlongM, int OffsetM, int WidthCm, bool River);

/// <summary>A waterway's profile for the water maths: its cumulative length, the tile height under each vertex (mm) and its catchment (tenths of km2) at a distance (derived, cached).</summary>
public sealed class WaterProfile
{
    public Waterway Way { get; }
    public int[] CumM { get; }
    public int[] HeightMm { get; }
    public int TotalM { get; }

    public WaterProfile(Waterway w, Region region)
    {
        Way = w;
        int n = w.Points.Length;
        CumM = new int[n]; HeightMm = new int[n];
        long cum = 0;
        for (int i = 0; i < n; i++)
        {
            if (i > 0) cum += Dist(w.Points[i - 1], w.Points[i]);
            CumM[i] = (int)cum;
            int tx = Math.Clamp(w.Points[i].X / Region.TileM, 0, Region.Size - 1), ty = Math.Clamp(w.Points[i].Y / Region.TileM, 0, Region.Size - 1);
            HeightMm[i] = region.HeightCm[ty * Region.Size + tx] * 10;
        }
        TotalM = Math.Max(1, (int)cum);
    }

    private static long Dist(PointM a, PointM b)
    {
        long dx = a.X - b.X, dy = a.Y - b.Y;
        return ISqrt(dx * dx + dy * dy);
    }

    public static long ISqrt(long v)
    {
        if (v <= 0) return 0;
        long x = (long)Math.Sqrt(v);
        while (x * x > v) x--;
        while ((x + 1) * (x + 1) <= v) x++;
        return x;
    }

    /// <summary>The ground height under the waterway at a distance from the source (mm): linear between the vertices' tile heights.</summary>
    public int HeightAt(int alongM)
    {
        alongM = Math.Clamp(alongM, 0, TotalM);
        int hi = Array.BinarySearch(CumM, alongM);
        if (hi >= 0) return HeightMm[hi];
        hi = ~hi;
        if (hi <= 0) return HeightMm[0];
        if (hi >= CumM.Length) return HeightMm[^1];
        int span = Math.Max(1, CumM[hi] - CumM[hi - 1]);
        return (int)(HeightMm[hi - 1] + (long)(HeightMm[hi] - HeightMm[hi - 1]) * (alongM - CumM[hi - 1]) / span);
    }

    /// <summary>The fall (mm, never negative) of the waterway over <paramref name="lengthM"/> metres upstream of a point.</summary>
    public int FallMm(int alongM, int lengthM) => Math.Max(0, HeightAt(Math.Max(0, alongM - lengthM)) - HeightAt(alongM));

    /// <summary>The catchment at a distance from the source: the downstream catchment scaled by the distance over the length (tenths of km2).</summary>
    public long CatchmentTenths(int alongM) => (long)Way.CatchmentTenthsKm2 * Math.Clamp(alongM, 0, TotalM) / TotalM;

    public int WidthAt(int alongM)
    {
        int n = Way.WidthCm.Length; if (n == 0) return 0;
        alongM = Math.Clamp(alongM, 0, TotalM);
        int i = Array.BinarySearch(CumM, alongM); if (i < 0) i = Math.Min(n - 1, Math.Max(0, ~i - 1));
        return Way.WidthCm[Math.Min(i, n - 1)];
    }
}

/// <summary>
/// The region's water for the power rules: finds the waterway under a cell, the head and watts reachable at it for each wheel and the region's mill sites (4.1, 4.6). A pure function of the region
/// and the content (profiles cached per waterway). Cells are region metres.
/// </summary>
public sealed class WaterSites
{
    public Region Region { get; }
    public WaterPowerRulesDef Rules { get; }
    private readonly Dictionary<int, WaterProfile> _profiles = [];

    public WaterSites(Region region, WaterPowerRulesDef rules) { Region = region; Rules = rules; }

    public WaterProfile Profile(Waterway w)
    {
        lock (_profiles)
        {
            if (!_profiles.TryGetValue(w.Id, out var p)) _profiles[w.Id] = p = new WaterProfile(w, Region);
            return p;
        }
    }

    /// <summary>The waterway nearest the cell and where on it (null when none lies within <paramref name="maxM"/> metres of the cell).</summary>
    public WaterPoint? Nearest(int x, int y, int maxM)
    {
        WaterPoint? best = null; long bestD2 = (long)maxM * maxM + 1;
        foreach (var w in Region.Waterways)
        {
            var pts = w.Points; if (pts.Length == 0) continue;
            var pr = Profile(w);
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i];
                long ax = a.X, ay = a.Y;
                if (i + 1 < pts.Length)
                {
                    var b = pts[i + 1];
                    long dx = b.X - ax, dy = b.Y - ay, len2 = dx * dx + dy * dy;
                    long t = len2 == 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) * 1000 / len2, 0, 1000);
                    long px = ax + dx * t / 1000, py = ay + dy * t / 1000;
                    long d2 = (x - px) * (x - px) + (y - py) * (y - py);
                    if (d2 < bestD2 || (d2 == bestD2 && best is { } bb && w.Id < bb.Waterway))
                    {
                        bestD2 = d2;
                        int along = (int)(pr.CumM[i] + (pr.CumM[i + 1] - pr.CumM[i]) * t / 1000);
                        best = new WaterPoint(w.Id, along, (int)WaterProfile.ISqrt(d2), pr.WidthAt(along), w.Kind == WaterKind.River);
                    }
                }
                else
                {
                    long d2 = (x - ax) * (x - ax) + (y - ay) * (y - ay);
                    if (d2 < bestD2) { bestD2 = d2; best = new WaterPoint(w.Id, pr.CumM[i], (int)WaterProfile.ISqrt(d2), pr.WidthAt(pr.CumM[i]), w.Kind == WaterKind.River); }
                }
            }
        }
        return best;
    }

    /// <summary>Whether a cell lies on a waterway's bank: within half the water's width plus the bank reach of its centre line.</summary>
    public WaterPoint? Bank(int x, int y)
    {
        if (Nearest(x, y, 400) is not { } p) return null;
        return p.OffsetM <= p.WidthCm / 200 + Rules.BankReachM ? p : null;
    }

    public Waterway Way(int id) => Region.Waterways[id];

    /// <summary>
    /// What a wheel at a bank point gives in a season (no ice, no flood): the flow there, the head with a leat of <paramref name="leatM"/> metres (0: none) and a weir or not, and the wheel's output.
    /// </summary>
    public WheelOutput Wheel(WaterPoint p, PowerPieceDef wheel, Season season, int leatM, bool weir, bool frozen = false, bool riverHigh = false)
    {
        var pr = Profile(Way(p.Waterway));
        int leat = Math.Clamp(leatM, 0, Rules.LeatMaxM);
        long catchTenths = pr.CatchmentTenths(p.AlongM);
        int q = WaterPower.Flow(Rules, catchTenths, season, frozen, riverHigh);
        int fall = pr.FallMm(p.AlongM, leat > 0 ? leat : Rules.NoLeatFallM);
        int head = WaterPower.HeadMm(Rules, fall, leat > 0, weir, p.WidthCm);
        return WaterPower.Output(wheel, q, head, p.River, leat > 0);
    }

    /// <summary>One entry of the region's mill-site list (4.6): a bank point, the best wheel there with its leat and weir, and the watts in autumn and summer.</summary>
    public sealed record MillSite(int Waterway, int AlongM, int X, int Y, string Wheel, int LeatM, bool Weir, int HeadMm, int AutumnW, int SummerW);

    private static readonly int[] LeatTries = [0, 60, 120, 200, 300];

    /// <summary>The best wheel, leat and weir at a point by its autumn output (the wheel ids in order; the first best wins).</summary>
    public MillSite? BestAt(WaterPoint p, int x, int y, IReadOnlyList<PowerPieceDef> wheels)
    {
        MillSite? best = null;
        foreach (var wd in wheels)
            foreach (int leat in LeatTries)
                foreach (bool weir in new[] { false, true })
                {
                    if (weir && p.WidthCm > Rules.WeirMaxWidthCm) continue;
                    var a = Wheel(p, wd, Season.Autumn, leat, weir);
                    if (a.Watts <= 0) continue;
                    if (best == null || a.Watts > best.AutumnW || (a.Watts == best.AutumnW && leat + (weir ? 1 : 0) < best.LeatM + (best.Weir ? 1 : 0)))
                        best = new MillSite(p.Waterway, p.AlongM, x, y, wd.Piece, leat, weir, a.HeadMm, a.Watts, Wheel(p, wd, Season.Summer, leat, weir).Watts);
                }
        return best;
    }

    /// <summary>
    /// The region's mill sites (4.6): bank points sampled along every waterway, each with its best wheel; those with at least <c>SiteMinW</c> watts in autumn, best first (ties: waterway, then
    /// distance from the source), kept at least <c>SiteSpacingM</c> metres apart. Pure: the same region gives the same list.
    /// </summary>
    public List<MillSite> MillSites(IReadOnlyList<PowerPieceDef> wheels)
    {
        var all = new List<MillSite>();
        foreach (var w in Region.Waterways)
        {
            var pr = Profile(w);
            for (int s = Rules.SiteSampleM; s < pr.TotalM; s += Rules.SiteSampleM)
            {
                // the point on the centre line at distance s from the source
                int i = Array.BinarySearch(pr.CumM, s); if (i < 0) i = ~i;
                if (i <= 0 || i >= w.Points.Length) continue;
                var a = w.Points[i - 1]; var b = w.Points[i];
                int span = Math.Max(1, pr.CumM[i] - pr.CumM[i - 1]);
                int x = a.X + (int)((long)(b.X - a.X) * (s - pr.CumM[i - 1]) / span), y = a.Y + (int)((long)(b.Y - a.Y) * (s - pr.CumM[i - 1]) / span);
                var p = new WaterPoint(w.Id, s, 0, pr.WidthAt(s), w.Kind == WaterKind.River);
                if (BestAt(p, x, y, wheels) is { } m && m.AutumnW >= Rules.SiteMinW) all.Add(m);
            }
        }
        all.Sort((a, b) => a.AutumnW != b.AutumnW ? b.AutumnW.CompareTo(a.AutumnW) : a.Waterway != b.Waterway ? a.Waterway.CompareTo(b.Waterway) : a.AlongM.CompareTo(b.AlongM));
        var kept = new List<MillSite>();
        foreach (var m in all)
        {
            bool near = false;
            foreach (var k in kept)
            {
                long dx = k.X - m.X, dy = k.Y - m.Y;
                if (dx * dx + dy * dy < (long)Rules.SiteSpacingM * Rules.SiteSpacingM) { near = true; break; }
            }
            if (!near) kept.Add(m);
        }
        return kept;
    }
}
