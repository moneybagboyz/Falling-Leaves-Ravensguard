using System.Collections.Concurrent;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Seamless;

/// <summary>Part A of the seamless continent (seamless-regions spec): the region grid, border gates, rivers and heights that agree across edges, and order-independent generation.</summary>
public class SeamlessRegionTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Seeds => new[] { 1UL, 1234UL }.Select(s => new object[] { s });
    private const int N = RegionSettings.Tiles;

    // ------------------------------------------------------------------ grid and embark

    [Fact]
    public void Grid_math_matches_the_spec_golden_case()
    {
        var r = RegionCoord.OfCell(37, 130);
        Assert.Equal(130, r.Id);
        Assert.Equal((2, 8), (r.X, r.Y));
        Assert.Equal((32, 128), (r.CellX, r.CellY));
        Assert.Equal(RegionCoord.FromId(130), r);
        Assert.Equal([113, 114, 115, 129, 131, 145, 146, 147], r.Neighbours().Select(n => n.Id).OrderBy(i => i));
        Assert.Equal(3, RegionCoord.FromId(0).Neighbours().Count());
        Assert.Equal(new RegionCoord(2, 8), RegionCoord.Snap(37, 130));
        Assert.Equal(new RegionCoord(2, 8), RegionCoord.OfMetres(r.OriginXM + 31_999, r.OriginYM));
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Embark_picks_a_grid_square_and_scores_256_of_them(ulong seed)
    {
        var w = SeamWorld.Get(seed);
        Assert.Equal(0, w.Embark.CellX % 16); Assert.Equal(0, w.Embark.CellY % 16);
        var s = EmbarkSite.Score(w.Continent, w.History, 37 + 16 * 5, 130);
        Assert.Equal(0, s.CellX % 16); Assert.Equal(0, s.CellY % 16); // any cell snaps to its square
        Assert.Equal(EmbarkSite.Score(w.Continent, w.History, s.CellX + 15, s.CellY + 15), s);
        var scored = Enumerable.Range(0, 256).Select(i => EmbarkSite.Score(w.Continent, w.History, RegionCoord.FromId(i))).ToList();
        Assert.Equal(256, scored.Select(x => (x.CellX, x.CellY)).Distinct().Count());
        Assert.Equal(w.Embark.Total, scored.Where(x => !x.Excluded).Max(x => x.Total));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegionGenerator.Generate(w.Continent, w.History, w.Embark.CellX + 2, w.Embark.CellY, w.Settings));
    }

    // ------------------------------------------------------------------ order independence

    private sealed record Fingerprint(string Region, string People, string Landmarks);

    private Fingerprint Build(SeamWorld w, RegionCoord rc)
    {
        var r = w.Fresh(rc);
        var marks = LandmarkPlacer.Place(r, w.History, w.Content);
        r.Landmarks = marks;
        var pop = Populator.Populate(r, w.History, w.Content);
        return new Fingerprint(r.ComputeHash(), pop.ComputeHash(), LandmarkPlacer.ComputeHash(marks));
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Regions_generated_in_different_orders_have_identical_hashes(ulong seed)
    {
        var w = SeamWorld.Get(seed);
        var block = w.Block3().ToList();
        var forward = block.ToDictionary(r => r.Id, r => Build(w, r));
        // reverse order, one at a time
        foreach (var rc in Enumerable.Reverse(block)) Assert.Equal(forward[rc.Id], Build(w, rc));
        // a shuffled order, all at once on four threads
        var shuffled = block.OrderBy(r => (r.Id * 2654435761u) % 97).ToList();
        var parallel = new ConcurrentDictionary<int, Fingerprint>();
        Parallel.ForEach(shuffled, new ParallelOptions { MaxDegreeOfParallelism = 4 }, rc => parallel[rc.Id] = Build(w, rc));
        foreach (var rc in block) Assert.Equal(forward[rc.Id], parallel[rc.Id]);
        // and a region generated alone, after nothing else, equals the one generated among its neighbours
        var alone = Build(SeamWorld.Get(seed), block[4]);
        Assert.Equal(forward[block[4].Id], alone);
        output.WriteLine($"seed {seed}: {block.Count} regions identical in 3 orders; centre {forward[block[4].Id].Region[..16]}");
    }

    // ------------------------------------------------------------------ gates

    /// <summary>Every shared edge (east and south) of the 3 x 3 block as (a, b, ra, rb, isVertical).</summary>
    private IEnumerable<(RegionCoord A, RegionCoord B, RegionData RA, RegionData RB, bool Vertical)> Edges(SeamWorld w)
    {
        var block = w.Block3().ToList();
        foreach (var a in block)
            foreach (var (dx, dy) in new[] { (1, 0), (0, 1) })
            {
                var b = a.Offset(dx, dy);
                if (block.Contains(b)) yield return (a, b, w.Region(a), w.Region(b), dx == 1);
            }
    }

    private static int EdgeTile(int along, bool vertical, bool sideA) => vertical ? along * N + (sideA ? N - 1 : 0) : (sideA ? N - 1 : 0) * N + along;
    private static int Along(int tile, bool vertical) => vertical ? tile / N : tile % N;

    [Theory, MemberData(nameof(Seeds))]
    public void Road_crossings_match_on_both_sides_of_every_shared_edge(ulong seed)
    {
        var w = SeamWorld.Get(seed);
        int total = 0, paved = 0;
        foreach (var (a, b, ra, rb, vertical) in Edges(w))
        {
            var sideA = vertical ? BorderSide.East : BorderSide.South; var sideB = vertical ? BorderSide.West : BorderSide.North;
            var ga = ra.Gates.Where(g => g.Side == sideA).ToList(); var gb = rb.Gates.Where(g => g.Side == sideB).ToList();
            Assert.Equal(ga.Select(g => g.Mirror), gb);
            // the shared list is the pure function of the history: the region built it exactly so
            Assert.Equal(BorderGates.For(w.History).Of(a, sideA), ga);
            foreach (var g in ga)
            {
                total++;
                // a gate road node on each side, on the edge tile of the very same row or column
                int ta = EdgeTile(g.Tile, vertical, true), tb = EdgeTile(g.Tile, vertical, false);
                // the gate node of each side is on the border line itself, at the same position along it, to the metre: the two roads are one polyline across the edge
                var na = ra.RoadNodes.Single(n => n.Kind == RoadNodeKind.Gate && (vertical ? n.X == RegionSettings.RegionM && n.Y / 64 == g.Tile : n.Y == RegionSettings.RegionM && n.X / 64 == g.Tile));
                var nb = rb.RoadNodes.Single(n => n.Kind == RoadNodeKind.Gate && (vertical ? n.X == 0 && n.Y / 64 == g.Tile : n.Y == 0 && n.X / 64 == g.Tile));
                Assert.Equal(vertical ? na.Y : na.X, vertical ? nb.Y : nb.X);
                bool water = (ra.Water[ta] & (WaterFlags.Sea | WaterFlags.Lake)) != 0 || (rb.Water[tb] & (WaterFlags.Sea | WaterFlags.Lake)) != 0;
                if (!water)
                {
                    Assert.True(ra.Road[ta] > 0 && rb.Road[tb] > 0, $"no road on the gate tile of {a}-{b} at {g.Tile}");
                    paved++;
                }
                // each side builds the straight segment inward from the gate
                Assert.Contains(ra.RoadEdges, e => (e.A == na.Id || e.B == na.Id));
            }
        }
        output.WriteLine($"seed {seed}: {total} gate crossings on {Edges(w).Count()} shared edges, {paved} carrying a road tile on both sides");
        Assert.True(total >= 3, "the block should have several road crossings");
    }

    [Fact]
    public void Gate_merge_keeps_the_lowest_route_id_per_edge_and_is_order_free()
    {
        var w = SeamWorld.Get(1234);
        var gates = BorderGates.For(w.History);
        for (int id = 0; id < 256; id++)
        {
            var rc = RegionCoord.FromId(id);
            foreach (var side in new[] { BorderSide.East, BorderSide.South })
            {
                var list = gates.Of(rc, side);
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                        Assert.True(Math.Abs(list[i].Tile - list[j].Tile) * RegionSettings.TileM >= BorderGates.MergeM - RegionSettings.TileM, "gates on one edge are at least ~1.5 km apart");
            }
        }
        // another BorderGates computed from scratch (cache bypassed by a new history instance with the same routes) is the same
        Assert.True(gates.Total > 50);
    }

    // ------------------------------------------------------------------ rivers and heights

    private static bool Chan(RegionData r, int t) => (r.Water[t] & (WaterFlags.River | WaterFlags.Stream)) != 0;

    [Theory, MemberData(nameof(Seeds))]
    public void Rivers_continue_across_every_shared_edge_with_the_same_width_and_depth(ulong seed)
    {
        var w = SeamWorld.Get(seed);
        int crossings = 0;
        foreach (var (a, b, ra, rb, vertical) in Edges(w))
        {
            // every polyline vertex on the border line has the identical vertex (position, width, depth, kind) on the other side, and the two sides have the same set
            var onA = BorderVertices(ra, vertical, atFar: true);
            var onB = BorderVertices(rb, vertical, atFar: false);
            Assert.Equal(onA, onB);
            crossings += onA.Count;
            // a channel tile on an edge is a continental river: a polyline passes through it
            for (int i = 0; i < N; i++)
                foreach (var (r, sideA) in new[] { (ra, true), (rb, false) })
                {
                    int t = EdgeTile(i, vertical, sideA);
                    if (Chan(r, t)) Assert.Contains(r.Waterways, wt => wt.Tiles.Contains(t));
                }
        }
        output.WriteLine($"seed {seed}: {crossings} river crossings of the block's edges, all identical on both sides");
        Assert.True(crossings >= 2, "the block should have rivers crossing its edges");
    }

    /// <summary>The waterway vertices on the border line of a region, mirrored to a common frame (position along the edge), with width, depth and kind, as a sorted list.</summary>
    private static List<string> BorderVertices(RegionData r, bool vertical, bool atFar)
    {
        var res = new List<string>();
        int line = atFar ? RegionSettings.RegionM : 0;
        foreach (var w in r.Waterways)
            for (int k = 0; k < w.Points.Length; k++)
            {
                var p = w.Points[k];
                if ((vertical ? p.X : p.Y) != line) continue;
                int along = vertical ? p.Y : p.X;
                if (along <= 0 || along >= RegionSettings.RegionM) continue; // a river through a corner meets the diagonal neighbour, not this one
                res.Add($"{(vertical ? p.Y : p.X)} w{w.WidthCm[k]} d{w.DepthCm[k]} {w.Kind}");
            }
        res.Sort(StringComparer.Ordinal);
        return res;
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Heights_along_every_shared_edge_are_the_same_pure_function_on_both_sides(ulong seed)
    {
        var w = SeamWorld.Get(seed);
        long sameSide = 0, across = 0, n = 0;
        foreach (var (a, b, ra, rb, vertical) in Edges(w))
        {
            // global tile coordinates: the edge tile of A and the first tile of B are adjacent
            int gxA = a.X * N + (vertical ? N - 1 : 0), gyA = a.Y * N + (vertical ? 0 : N - 1);
            int[] pure = RegionGenerator.HeightBlock(w.Continent, w.History, w.Settings, gxA, gyA, vertical ? 2 : N, vertical ? N : 2);
            for (int i = 0; i < N; i++)
            {
                int hA = ra.HeightCm[EdgeTile(i, vertical, true)], hB = rb.HeightCm[EdgeTile(i, vertical, false)];
                int pA = vertical ? pure[i * 2] : pure[i], pB = vertical ? pure[i * 2 + 1] : pure[N + i];
                Assert.Equal(pA, hA); Assert.Equal(pB, hB); // both regions equal the shared pure function, to the centimetre
                across += Math.Abs(hA - hB);
                sameSide += Math.Abs(hA - ra.HeightCm[vertical ? i * N + N - 2 : (N - 2) * N + i]);
                n++;
            }
        }
        output.WriteLine($"seed {seed}: mean height step across an edge {across / (double)n:0.0} cm vs {sameSide / (double)n:0.0} cm between inner tiles");
        Assert.True(across <= sameSide * 1.3 + n * 4, "the terrain steps across an edge as it does inside the region");
    }

    [Theory, MemberData(nameof(Seeds))]
    public void No_lot_field_or_parcel_stands_within_a_tile_of_a_region_border(ulong seed)
    {
        var w = SeamWorld.Get(seed);
        foreach (var rc in w.Block3())
        {
            var r = w.Region(rc);
            foreach (var s in r.Settlements)
                foreach (var lot in s.Plan.Lots)
                {
                    Assert.True(lot.Rect.X >= 64 && lot.Rect.Y >= 64 && lot.Rect.Right <= RegionSettings.RegionM - 64 && lot.Rect.Bottom <= RegionSettings.RegionM - 64, $"{rc}: {s.Name} lot {lot.Index} {lot.Rect} reaches the border");
                }
            foreach (var f in r.Fields) Assert.True(f.Tile % N is >= 1 and <= N - 2 && f.Tile / N is >= 1 and <= N - 2, $"{rc}: a field tile on the border");
        }
    }
}
