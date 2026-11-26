using System.Runtime.CompilerServices;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Naval;

/// <summary>A sea edge (ships-and-naval-war 3.3): two coastal regions whose coasts face the same water, and the length of the sea path between them in kilometres (an integer BFS over ocean cells of 2 km).</summary>
public sealed record SeaEdge(int A, int B, int LengthKm);

/// <summary>
/// P7-SH-C (3.3, SH-9): the sea edges of the continent, derived from its <c>Ocean</c> mask and never saved. A region's shore is the set of ocean cells that touch one of its land cells; an edge joins two regions
/// when a breadth-first walk over ocean cells (4-connected, a cell is <see cref="CellKm"/> across) from one's shore reaches the other's within <see cref="MaxKm"/> of sea path. Its length is the walk's shortest, in whole cells
/// (two regions that touch the same shore cell are one cell, 2 km, apart). Two coasts that back onto different seas are never joined. Integer only; regions and cells in id order.
/// </summary>
public static class SeaEdges
{
    public const int CellKm = 2, MaxKm = 64;
    private const int MaxCells = MaxKm / CellKm;

    /// <summary>Builds the edges of a <paramref name="size"/> x <paramref name="size"/> cell mask; <paramref name="regionOf"/> names the region of a land cell (cell index <c>y * size + x</c>; negative for land no region owns). Ordered by (A, B), A below B.</summary>
    public static List<SeaEdge> Build(int size, bool[] ocean, Func<int, int> regionOf)
    {
        int n = size * size;
        // the ocean cells that touch land, with the regions whose land they touch (sorted, distinct)
        var touch = new SortedDictionary<int, SortedSet<int>>();
        for (int c = 0; c < n; c++)
        {
            if (ocean[c]) continue;
            int r = regionOf(c), x = c % size, y = c / size;
            if (r < 0) continue;                                           // land that belongs to no region (a wall between seas)
            if (x > 0 && ocean[c - 1]) Note(touch, c - 1, r);
            if (x < size - 1 && ocean[c + 1]) Note(touch, c + 1, r);
            if (y > 0 && ocean[c - size]) Note(touch, c - size, r);
            if (y < size - 1 && ocean[c + size]) Note(touch, c + size, r);
        }
        var seeds = new SortedDictionary<int, List<int>>();               // region -> its shore ocean cells (ascending)
        foreach (var (cell, regions) in touch) foreach (int r in regions) { if (!seeds.TryGetValue(r, out var l)) seeds[r] = l = []; l.Add(cell); }
        var edges = new List<SeaEdge>();
        var stamp = new int[n]; var dist = new int[n]; int gen = 0; var queue = new int[n];
        foreach (var (a, cells) in seeds)
        {
            gen++; int head = 0, tail = 0;
            foreach (int c in cells) { stamp[c] = gen; dist[c] = 0; queue[tail++] = c; }
            var best = new SortedDictionary<int, int>();
            while (head < tail)
            {
                int c = queue[head++]; int d = dist[c];
                if (touch.TryGetValue(c, out var rs)) foreach (int b in rs) if (b > a && (!best.TryGetValue(b, out int old) || d < old)) best[b] = d;
                if (d + 1 >= MaxCells) continue;                           // (d + 1) cells of path is the most a 64 km edge allows
                int x = c % size, y = c / size;
                if (x > 0) Step(c - 1, d + 1);
                if (x < size - 1) Step(c + 1, d + 1);
                if (y > 0) Step(c - size, d + 1);
                if (y < size - 1) Step(c + size, d + 1);
            }
            foreach (var (b, d) in best) edges.Add(new SeaEdge(a, b, (d + 1) * CellKm));

            void Step(int c, int d) { if (!ocean[c] || stamp[c] == gen) return; stamp[c] = gen; dist[c] = d; queue[tail++] = c; }
        }
        return edges;
    }

    private static void Note(SortedDictionary<int, SortedSet<int>> touch, int cell, int region)
    {
        if (!touch.TryGetValue(cell, out var s)) touch[cell] = s = [];
        s.Add(region);
    }

    private static readonly ConditionalWeakTable<bool[], List<SeaEdge>> Cache = new();

    /// <summary>The edges of a generated continent (cached per mask: pure, so the cache never changes a result).</summary>
    public static IReadOnlyList<SeaEdge> Of(Ravensguard.Sim.WorldGen.Continent c)
        => Cache.GetValue(c.Ocean, _ => Build(Ravensguard.Sim.WorldGen.Continent.Size, c.Ocean, cell => RegionCoord.OfCell(cell).Id));

    /// <summary>The edge between two regions, if any (either order).</summary>
    public static SeaEdge? Find(IEnumerable<SeaEdge> edges, int a, int b)
    {
        if (a > b) (a, b) = (b, a);
        foreach (var e in edges) if (e.A == a && e.B == b) return e;
        return null;
    }
}
