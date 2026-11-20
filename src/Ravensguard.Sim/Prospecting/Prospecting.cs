using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.WorldGen;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Prospecting;

/// <summary>An ore body as the world holds it (the hidden truth): the vein, its mineral, the grams left, its grade.</summary>
public readonly record struct OreBody(int Vein, MineralType Mineral, long RemainingG, long InitialG, int GradePm);

/// <summary>
/// The truth prospecting reads (spec 3: "the truth is already in the sim"): tile stocks of the resource layer and the ore bodies of the region's veins, with what has been mined from them.
/// Only reads; nothing here changes a stock (PX-2). The upstream index of the flow network (for panning) is built once per region.
/// </summary>
public sealed class ProspectTruth(RegionData region, ResourceLayer? layer, Func<int, long>? minedOfVein = null)
{
    public RegionData Region { get; } = region;
    public ResourceLayer? Layer { get; } = layer;
    public Func<int, long>? MinedOfVein { get; set; } = minedOfVein;
    private int[]? _upStart, _up;

    /// <summary>Ore minerals (worked by mines): iron, copper, tin, silver, gold, coal and rock salt. Stone and peat are surface kinds of the layer.</summary>
    public static bool IsOre(MineralType m) => m is MineralType.Iron or MineralType.Copper or MineralType.Tin or MineralType.Silver or MineralType.Gold or MineralType.Coal or MineralType.Salt;

    /// <summary>The grade of a vein, permille: a pure function of the generated vein (no draw after world generation).</summary>
    public static int GradeOf(Ravensguard.Sim.WorldGen.Region.RegionVein v) => 250 + (int)((uint)(v.Tonnage * 37 + v.TileX * 13 + v.TileY * 7 + (int)v.Type * 101) % 500);

    /// <summary>The ore body under a tile: the vein of an ore mineral whose footprint (its radius) covers the tile, the largest first, then the lowest index; null when none.</summary>
    public OreBody? OreAt(int tile)
    {
        int tx = tile % RegionData.Size, ty = tile / RegionData.Size; int best = -1;
        for (int i = 0; i < Region.Veins.Count; i++)
        {
            var v = Region.Veins[i]; if (!IsOre(v.Type)) continue;
            int dx = tx - v.TileX, dy = ty - v.TileY;
            if (dx * dx + dy * dy > v.RadiusTiles * v.RadiusTiles) continue;
            if (best < 0 || v.Tonnage > Region.Veins[best].Tonnage) best = i;
        }
        return best < 0 ? null : Body(best);
    }

    public OreBody Body(int vein)
    {
        var v = Region.Veins[vein]; long init = (long)v.Tonnage * 1_000_000;
        long mined = MinedOfVein?.Invoke(vein) ?? 0;
        return new OreBody(vein, v.Type, Math.Max(0, init - mined), init, GradeOf(v));
    }

    /// <summary>The stock of a surface kind on a tile (grams; 0 when the tile has none).</summary>
    public long SurfaceG(int tile, ResourceKind kind)
    {
        if (Layer == null) return 0;
        int row = Layer.Find(tile, kind);
        return row < 0 ? 0 : Layer.StockG(row);
    }

    /// <summary>The surface kinds the eye sees walking past (spec 3: woodland, open stone, clay banks, peat bog, salt flats, fish water).</summary>
    public static readonly ResourceKind[] Visible = [ResourceKind.Wood, ResourceKind.Peat, ResourceKind.Salt, ResourceKind.Fish];
    /// <summary>The beds a test pit finds (spec 4: clay, sand and stone under the tile).</summary>
    public static readonly ResourceKind[] Beds = [ResourceKind.Clay, ResourceKind.Sand, ResourceKind.Stone];

    /// <summary>The tiles upstream of <paramref name="tile"/> along the flow network, breadth first, at most <paramref name="depth"/> steps (the tile itself first).</summary>
    public List<int> Upstream(int tile, int depth)
    {
        if (_up == null)
        {
            int n = RegionData.Tiles; var counts = new int[n + 1];
            for (int t = 0; t < n; t++) { int to = Region.FlowTo[t]; if (to >= 0 && to < n) counts[to + 1]++; }
            for (int t = 0; t < n; t++) counts[t + 1] += counts[t];
            var up = new int[counts[n]]; var fill = (int[])counts.Clone();
            for (int t = 0; t < n; t++) { int to = Region.FlowTo[t]; if (to >= 0 && to < n) up[fill[to]++] = t; }
            _upStart = counts; _up = up;
        }
        var outp = new List<int> { tile }; var seen = new HashSet<int> { tile }; var frontier = new List<int> { tile };
        for (int d = 0; d < depth && frontier.Count > 0; d++)
        {
            var next = new List<int>();
            foreach (int t in frontier)
                for (int k = _upStart![t]; k < _upStart[t + 1]; k++) { int u = _up[k]; if (seen.Add(u)) { outp.Add(u); next.Add(u); } }
            frontier = next;
        }
        return outp;
    }

    public bool IsStream(int tile) => (Region.Water[tile] & (Ravensguard.Sim.WorldGen.Region.WaterFlags.River)) != 0 || Region.Catchment[tile] >= 20;
}

/// <summary>
/// The methods of prospecting (spec 4) as pure functions over the truth: each returns the knowledge rows it gives. Estimates are the truth plus an error drawn from stream 0x5C0, keyed on
/// (person, tile, method, game day), whose spread falls with skill (a trial shaft: +-30% at skill 50, +-10% at 90); hints from reading the land draw stream 0x5C1. The truth is never drawn.
/// </summary>
public static class Prospector
{
    public static int SpreadPm(ProspectMethod m, int skill, ProspectRulesDef r)
    {
        skill = Math.Clamp(skill, 0, 100);
        int s = m == ProspectMethod.TrialShaft
            ? r.ErrorAt50Pm - (skill - 50) * (r.ErrorAt50Pm - r.ErrorAt90Pm) / 40
            : r.ErrorAt0Pm - skill * (r.ErrorAt0Pm - r.ErrorAt90Pm) / 90;
        return Math.Clamp(s, 20, 900);
    }

    public static int ConfidencePm(ProspectMethod m, int skill, ProspectRulesDef r)
    {
        int b = m switch { ProspectMethod.Walk => r.WalkConfidencePm, ProspectMethod.ReadLand => r.ReadConfidencePm, ProspectMethod.TestPit => r.PitConfidencePm, ProspectMethod.TrialShaft => r.ShaftConfidencePm, _ => r.PitConfidencePm };
        return m == ProspectMethod.Walk ? b : Math.Clamp(b * (600 + 4 * Math.Clamp(skill, 0, 100)) / 1000, 1, 1000);
    }

    /// <summary>The estimate of a truth: <c>truth x (1000 + e) / 1000</c>, e uniform in [-spread, spread] (stream 0x5C0, key person, tile, method, day).</summary>
    public static long Estimate(ulong seed, EntityId person, int tile, ProspectMethod m, int day, int code, long truth, int spreadPm)
    {
        if (truth <= 0) return 0;
        int e = DetRng.Range(seed, day, person.Value, RngStreams.ProspectError, (uint)tile * 16u + (uint)m + ((uint)code << 26), -spreadPm, spreadPm + 1);
        return Math.Max(0, truth * (1000 + e) / 1000);
    }

    private static int EstGrade(ulong seed, EntityId person, int tile, ProspectMethod m, int day, int grade, int spreadPm)
    {
        int e = DetRng.Range(seed, day, person.Value, RngStreams.ProspectError, (uint)tile * 16u + (uint)m + 0x8000_0000u, -spreadPm / 2, spreadPm / 2 + 1);
        return Math.Clamp(grade * (1000 + e) / 1000, 1, 1000);
    }

    /// <summary>Walk and look: the visible surface kinds of the tile at a rough estimate and confidence 200.</summary>
    public static List<ProspectRow> Walk(ProspectTruth w, ProspectRulesDef r, ulong seed, EntityId person, int tile, int skill, int day)
    {
        var rows = new List<ProspectRow>();
        foreach (var k in ProspectTruth.Visible)
        {
            long t = w.SurfaceG(tile, k); if (t <= 0) continue;
            int code = (int)k; int sp = SpreadPm(ProspectMethod.Walk, skill, r);
            rows.Add(new ProspectRow(person, tile, code, Estimate(seed, person, tile, ProspectMethod.Walk, day, code, t, sp), 0, ConfidencePm(ProspectMethod.Walk, skill, r), day, ProspectMethod.Walk));
        }
        return rows;
    }

    /// <summary>Read the land (skill 20+): the visible kinds more surely, and a hint of each hidden ore body within the read radius by direction only (noticed at <c>300 + 7 x skill</c> pm, stream 0x5C1).</summary>
    public static (List<ProspectRow> Rows, List<ProspectHint> Hints) ReadLand(ProspectTruth w, ProspectRulesDef r, ulong seed, EntityId person, int tile, int skill, int day)
    {
        var rows = new List<ProspectRow>(); var hints = new List<ProspectHint>();
        if (skill < r.ReadMinSkill) return (rows, hints);
        foreach (var k in ProspectTruth.Visible)
        {
            long t = w.SurfaceG(tile, k); if (t <= 0) continue;
            int code = (int)k; int sp = SpreadPm(ProspectMethod.ReadLand, skill, r);
            rows.Add(new ProspectRow(person, tile, code, Estimate(seed, person, tile, ProspectMethod.ReadLand, day, code, t, sp), 0, ConfidencePm(ProspectMethod.ReadLand, skill, r), day, ProspectMethod.ReadLand));
        }
        int tx = tile % RegionData.Size, ty = tile / RegionData.Size; long reach = r.ReadHintM;
        var reg = w.Region;
        for (int i = 0; i < reg.Veins.Count; i++)
        {
            var v = reg.Veins[i]; if (!ProspectTruth.IsOre(v.Type)) continue;
            long dx = (v.TileX - tx) * (long)RegionData.TileM, dy = (v.TileY - ty) * (long)RegionData.TileM;
            long edge = Math.Max(0, ProductionSystem.ISqrt(dx * dx + dy * dy) - (long)v.RadiusTiles * RegionData.TileM);
            if (edge > reach) continue;
            int pm = Math.Min(950, 300 + 7 * Math.Clamp(skill, 0, 100));
            if (DetRng.Range(seed, day, person.Value, RngStreams.ProspectHint, (uint)(tile * 64 + i), 0, 1000) >= pm) continue;
            hints.Add(new ProspectHint(person, tile, (int)v.Type, Direction(dx, dy), day));
        }
        return (rows, hints);
    }

    /// <summary>The octant of a vector (0 = north, clockwise), integers only.</summary>
    public static int Direction(long dx, long dy)
    {
        if (dx == 0 && dy == 0) return 0;
        // y grows southward on the region grid: north is -y
        long ax = Math.Abs(dx), ay = Math.Abs(dy);
        bool diag = ax * 1000 > ay * 414 && ay * 1000 > ax * 414;      // tan 22.5 deg ~ 0.414
        if (diag) return dx > 0 ? (dy < 0 ? 1 : 3) : (dy < 0 ? 7 : 5);
        if (ax >= ay) return dx > 0 ? 2 : 6;
        return dy < 0 ? 0 : 4;
    }

    /// <summary>Pan a stream: the ore minerals of the catchment upstream (the minerals of the tiles the water came over), ascending; which, not where. Empty when the tile is no stream.</summary>
    public static int[] Pan(ProspectTruth w, ProspectRulesDef r, int tile)
    {
        if (!w.IsStream(tile)) return [];
        var found = new SortedSet<int>();
        foreach (int t in w.Upstream(tile, r.PanUpstreamTiles))
        {
            int m = w.Region.Mineral[t] - 1;
            if (m >= 0 && ProspectTruth.IsOre((MineralType)m) && (MineralType)m is not MineralType.Coal and not MineralType.Salt) found.Add(m);
        }
        return [.. found];
    }

    /// <summary>A test pit: the clay, sand and stone beds under the tile at good confidence.</summary>
    public static List<ProspectRow> TestPit(ProspectTruth w, ProspectRulesDef r, ulong seed, EntityId person, int tile, int skill, int day)
    {
        var rows = new List<ProspectRow>();
        foreach (var k in ProspectTruth.Beds)
        {
            long t = w.SurfaceG(tile, k); if (t <= 0) continue;
            int code = (int)k; int sp = SpreadPm(ProspectMethod.TestPit, skill, r);
            rows.Add(new ProspectRow(person, tile, code, Estimate(seed, person, tile, ProspectMethod.TestPit, day, code, t, sp), 0, ConfidencePm(ProspectMethod.TestPit, skill, r), day, ProspectMethod.TestPit));
        }
        return rows;
    }

    /// <summary>A trial shaft or bore: the ore body under the tile, its type, grade and a tonnage estimate (+-30% at skill 50, +-10% at 90). Empty when there is none.</summary>
    public static List<ProspectRow> TrialShaft(ProspectTruth w, ProspectRulesDef r, ulong seed, EntityId person, int tile, int skill, int day)
    {
        var body = w.OreAt(tile); if (body is not { } b) return [];
        int code = ResourceCode.Ore(b.Mineral); int sp = SpreadPm(ProspectMethod.TrialShaft, skill, r);
        return [new ProspectRow(person, tile, code, Estimate(seed, person, tile, ProspectMethod.TrialShaft, day, code, b.RemainingG, sp), EstGrade(seed, person, tile, ProspectMethod.TrialShaft, day, b.GradePm, sp),
            ConfidencePm(ProspectMethod.TrialShaft, skill, r), day, ProspectMethod.TrialShaft)];
    }

    /// <summary>The hours a method takes (spec 4 table).</summary>
    public static int Hours(ProspectMethod m, ProspectRulesDef r) => m switch
    {
        ProspectMethod.ReadLand => r.ReadHours, ProspectMethod.Pan => r.PanHours, ProspectMethod.TestPit => r.PitHours, ProspectMethod.TrialShaft => r.ShaftHours, _ => 0,
    };

    /// <summary>The tools a method needs, as craft tool ids: any one of each group (spec 4: a pan; a shovel and a pick; an auger or a pick).</summary>
    public static string[][] Tools(ProspectMethod m) => m switch
    {
        ProspectMethod.Pan => [["pan", "pot"]],
        ProspectMethod.TestPit => [["spade"], ["pick", "crowbar"]],
        ProspectMethod.TrialShaft => [["pick", "auger"]],
        _ => [],
    };

    public static bool HasTools(ProspectMethod m, IReadOnlyCollection<string> tools) => Tools(m).All(g => g.Any(tools.Contains));
}
