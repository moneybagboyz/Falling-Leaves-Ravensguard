using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Tests.Live;

/// <summary>
/// Phase 3 cleanup: <c>JobTests.Determinism</c> once ran out of memory. The shared <see cref="TownRig"/> ground has one pathfinder, and test classes that run in parallel searched it at
/// the same time: the per-search arrays are shared, a parent chain got a cycle, and the path builder grew its list without end. The pathfinder now serialises its searches.
/// </summary>
public class SharedPathfinderTests
{
    [Fact]
    public void One_pathfinder_searched_from_many_threads_gives_the_one_thread_routes()
    {
        var rig = TownRig.Get; var g = rig.Ground;
        int cx = (int)(rig.CentreXMm / 1000), cy = (int)(rig.CentreYMm / 1000);
        var pairs = new List<(int, int, int, int)>();
        for (int i = 0; i < 64; i++)
        {
            int ax = cx - 40 + (i * 7) % 80, ay = cy - 40 + (i * 13) % 80, bx = cx - 40 + (i * 29) % 80, by = cy - 40 + (i * 31) % 80;
            if (g.IsOpen(ax, ay) && g.IsOpen(bx, by)) pairs.Add((ax, ay, bx, by));
        }
        Assert.True(pairs.Count >= 10, $"{pairs.Count} open pairs");
        string Key((int X, int Y)[]? r) => r == null ? "-" : string.Join(";", r.Select(c => c.X + "," + c.Y));
        var expected = pairs.Select(p => Key(g.Route(p.Item1, p.Item2, p.Item3, p.Item4))).ToArray();
        var got = new string[8][];
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, t =>
        {
            var mine = new string[pairs.Count];
            for (int rep = 0; rep < 4; rep++)
                for (int k = 0; k < pairs.Count; k++) { int i = (k + t * 5) % pairs.Count; var p = pairs[i]; mine[i] = Key(g.Route(p.Item1, p.Item2, p.Item3, p.Item4)); }
            got[t] = mine;
        });
        foreach (var m in got) Assert.Equal(expected, m);
    }
}
