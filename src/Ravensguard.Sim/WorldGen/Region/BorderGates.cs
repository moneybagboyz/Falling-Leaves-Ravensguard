using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.WorldGen.Region;

/// <summary>Which edge of a region a gate stands on.</summary>
public enum BorderSide : byte { North, East, South, West }

/// <summary>
/// One road crossing of a region edge, derived from the continental road network (seamless-regions spec 2.3).
/// <see cref="Tile"/> is the tile row (east/west edges) or column (north/south edges) along the edge, 0..499, the same number on both neighbours.
/// <see cref="Bearing"/> is the unit direction of the road leaving this region through the gate, in 1/1024 units.
/// </summary>
public readonly record struct RegionGate(BorderSide Side, int Tile, RoadClass Class, int RouteId, int BearingX, int BearingY)
{
    /// <summary>The gate as the neighbour across the edge sees it.</summary>
    public RegionGate Mirror => new(Side switch { BorderSide.North => BorderSide.South, BorderSide.South => BorderSide.North, BorderSide.East => BorderSide.West, _ => BorderSide.East }, Tile, Class, RouteId, -BearingX, -BearingY);
}

/// <summary>
/// The continental road network seen from region edges. The network is the history's trade routes (cell paths between settlements, abandoned ones left out);
/// every step of a route that leaves one region for the next is a border crossing at the point where the step crosses the shared edge. The result is a
/// pure function of the history: no region reads its neighbour's terrain, so both sides of an edge agree on every gate (position, class, route) whichever
/// is generated first. Gates on one edge closer than <see cref="MergeM"/> merge into the one with the lowest route id.
/// All integer maths.
/// </summary>
public sealed class BorderGates
{
    public const int MergeM = 1500;
    /// <summary>Length of the straight segment a region builds inward from each gate, along the route's bearing.</summary>
    public const int SegmentM = 200;
    private const int Tiles = RegionSettings.Tiles, TileM = RegionSettings.TileM;

    private readonly List<RegionGate>[][] _gates = new List<RegionGate>[RegionCoord.Count][];

    private BorderGates()
    {
        for (int i = 0; i < _gates.Length; i++) { _gates[i] = new List<RegionGate>[4]; for (int s = 0; s < 4; s++) _gates[i][s] = []; }
    }

    private static readonly ConditionalWeakTable<WorldHistory, BorderGates> Cache = new();

    /// <summary>The gates of a history, computed once and shared (immutable after that).</summary>
    public static BorderGates For(WorldHistory h)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(h, out var g)) return g;
            g = Compute(h);
            Cache.AddOrUpdate(h, g);
            return g;
        }
    }

    /// <summary>All gates of one edge of a region, sorted by tile.</summary>
    public IReadOnlyList<RegionGate> Of(RegionCoord r, BorderSide side) => r.IsValid ? _gates[r.Id][(int)side] : [];

    /// <summary>All gates of a region, sides in N, E, S, W order, each sorted by tile.</summary>
    public IEnumerable<RegionGate> Of(RegionCoord r)
    {
        for (int s = 0; s < 4; s++) foreach (var g in Of(r, (BorderSide)s)) yield return g;
    }

    /// <summary>SHA-256 over every gate of every region in region-id order (sides N, E, S, W, each sorted by tile), for determinism tests.</summary>
    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b, v); h.AppendData(b); }
        for (int id = 0; id < RegionCoord.Count; id++)
            foreach (var g in Of(RegionCoord.FromId(id))) { L(id); L((long)g.Side); L(g.Tile); L((long)g.Class); L(g.RouteId); L(g.BearingX); L(g.BearingY); }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    public int Total => _gates.Sum(a => a.Sum(l => l.Count)) / 2;

    private readonly record struct Raw(int Pos, int RouteId, int Step, RoadClass Class);

    private static BorderGates Compute(WorldHistory h)
    {
        var res = new BorderGates();
        var townOf = new Dictionary<ulong, bool>();
        foreach (var s in h.Settlements) townOf[s.Id.Value] = s.Pop >= 500;
        // edges: vertical edges indexed (rx, ry) = between region (rx, ry) and (rx + 1, ry); horizontal between (rx, ry) and (rx, ry + 1)
        var vert = new List<Raw>[RegionCoord.Count];
        var horiz = new List<Raw>[RegionCoord.Count];
        for (int ri = 0; ri < h.TradeRoutes.Count; ri++)
        {
            var rt = h.TradeRoutes[ri];
            if (rt.Abandoned || rt.Cells.Length < 2) continue;
            bool ta = townOf.GetValueOrDefault(rt.A.Value), tb = townOf.GetValueOrDefault(rt.B.Value);
            var cls = ta && tb ? RoadClass.Highway : ta || tb ? RoadClass.Road : RoadClass.Track;
            for (int k = 1; k < rt.Cells.Length; k++)
            {
                int ca = rt.Cells[k - 1], cb = rt.Cells[k];
                int ax = ca % Continent.Size, ay = ca / Continent.Size, bx = cb % Continent.Size, by = cb / Continent.Size;
                var ra = RegionCoord.OfCell(ax, ay); var rb = RegionCoord.OfCell(bx, by);
                if (ra == rb) continue;
                if (ra.X != rb.X && ra.Y == rb.Y)
                {
                    // the step crosses the vertical edge between the two regions at the y of the (shared) cell row centre
                    var west = ra.X < rb.X ? ra : rb;
                    (vert[west.Id] ??= []).Add(new Raw(ay * RegionSettings.CellM + RegionSettings.CellM / 2, ri, k, cls));
                }
                else if (ra.Y != rb.Y && ra.X == rb.X)
                {
                    var north = ra.Y < rb.Y ? ra : rb;
                    (horiz[north.Id] ??= []).Add(new Raw(ax * RegionSettings.CellM + RegionSettings.CellM / 2, ri, k, cls));
                }
                // routes are 4-connected cell paths, so a step never leaves two regions at once
            }
        }
        for (int id = 0; id < RegionCoord.Count; id++)
        {
            var r = RegionCoord.FromId(id);
            if (vert[id] != null) Emit(res, Merge(vert[id]), r, r.Offset(1, 0), vertical: true);
            if (horiz[id] != null) Emit(res, Merge(horiz[id]), r, r.Offset(0, 1), vertical: false);
        }
        for (int i = 0; i < res._gates.Length; i++) for (int s = 0; s < 4; s++) res._gates[i][s].Sort((p, q) => p.Tile != q.Tile ? p.Tile.CompareTo(q.Tile) : p.RouteId.CompareTo(q.RouteId));
        return res;
    }

    /// <summary>Greedy by route id (then step): a crossing within 1,500 m along the edge of one already kept is dropped. The order is global, never per region.</summary>
    private static List<Raw> Merge(List<Raw> raw)
    {
        raw.Sort((a, b) => a.RouteId != b.RouteId ? a.RouteId.CompareTo(b.RouteId) : a.Step.CompareTo(b.Step));
        var kept = new List<Raw>();
        foreach (var g in raw)
        {
            bool dup = false;
            foreach (var k in kept) if (Math.Abs(k.Pos - g.Pos) < MergeM) { dup = true; break; }
            if (!dup) kept.Add(g);
        }
        return kept;
    }

    private static void Emit(BorderGates res, List<Raw> kept, RegionCoord a, RegionCoord b, bool vertical)
    {
        foreach (var g in kept)
        {
            // the gate stands on the tile row (or column) the crossing point falls in, counted from the edge's region origin; both regions use the same number
            int origin = vertical ? a.OriginYM : a.OriginXM;
            int tile = Math.Clamp((g.Pos - origin) / TileM, 0, Tiles - 1);
            // the road leaving A through the edge heads east (vertical edge) or south (horizontal edge); B sees the mirror
            var outA = vertical ? new RegionGate(BorderSide.East, tile, g.Class, g.RouteId, 1024, 0) : new RegionGate(BorderSide.South, tile, g.Class, g.RouteId, 0, 1024);
            res._gates[a.Id][(int)outA.Side].Add(outA);
            var inB = outA.Mirror;
            res._gates[b.Id][(int)inB.Side].Add(inB);
        }
    }
}
