using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.World;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Spikes;

/// <summary>Spike 5: z-levels, portals, A* with incremental invalidation (plan 2.2a).</summary>
public class LevelsSpike(ITestOutputHelper output)
{
    /// <summary>A 20x20 tower footprint: cellar z-1, ground z0, floors z1 and z2 (tower top), joined by stairs in different corners.</summary>
    private static (LevelGrid g, Portal s_cellar, Portal s01, Portal s12, Portal ladder12) Tower()
    {
        var g = new LevelGrid(64, 64, -2, 2);
        g.FillRect(0, 0, 63, 63, 0, true);
        for (int z = -1; z <= 2; z++) if (z != 0) g.FillRect(10, 10, 29, 29, z, true);
        var sc = g.AddPortal(PortalKind.Stairs, new(11, 11, -1), new(11, 11, 0), 40);
        var s01 = g.AddPortal(PortalKind.Stairs, new(28, 11, 0), new(28, 11, 1), 40);
        var s12 = g.AddPortal(PortalKind.Stairs, new(28, 28, 1), new(28, 28, 2), 40);
        var lad = g.AddPortal(PortalKind.Ladder, new(11, 28, 1), new(11, 28, 2), 30);
        return (g, sc, s01, s12, lad);
    }

    [Fact]
    public void CellarToTowerTop_ThroughStairs()
    {
        var (g, sc, s01, s12, lad) = Tower();
        var pf = new LevelPathfinder(g);
        var r = pf.FindPath(new(15, 15, -1), new(15, 15, 2));
        Assert.NotNull(r);
        Assert.Equal(-1, r!.Cells[0].Z);
        Assert.Equal(2, r.Cells[^1].Z);
        Assert.Equal(3, r.PortalsUsed.Count);
        Assert.Equal(new[] { sc.Id, s01.Id, s12.Id }, r.PortalsUsed.ToArray());
        // every step is a legal move: adjacent in xy on the same level, or a portal hop
        for (int i = 1; i < r.Cells.Count; i++)
        {
            var a = r.Cells[i - 1]; var b = r.Cells[i];
            if (a.Z == b.Z) Assert.True(Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1);
            else Assert.True(g.Portals.Any(p => (p.A == a && p.B == b) || (p.A == b && p.B == a)));
        }
        output.WriteLine($"cellar->tower top: {r.Cells.Count} cells, cost {r.Cost}, expanded {r.Expanded}");
        var ab = pf.FindPathAbstract(new(15, 15, -1), new(15, 15, 2));
        Assert.NotNull(ab);
        Assert.Equal(r.Cost, ab!.Cost);
    }

    [Fact]
    public void LadderPulledUp_ReroutesOrFails()
    {
        var (g, _, _, s12, lad) = Tower();
        var pf = new LevelPathfinder(g);
        // From the ladder's own corner on z1 to the ladder's top on z2: the ladder is the shortest way.
        var start = new Cell3(12, 27, 1); var goal = new Cell3(12, 27, 2);
        var before = pf.FindPath(start, goal)!;
        Assert.Contains(lad.Id, before.PortalsUsed);
        g.SetPortalEnabled(lad.Id, false); // pull the ladder up
        var after = pf.FindPath(start, goal)!;
        Assert.DoesNotContain(lad.Id, after.PortalsUsed);
        Assert.Contains(s12.Id, after.PortalsUsed);
        Assert.True(after.Cost > before.Cost);
        g.SetPortalEnabled(s12.Id, false); // bar the stairs too: top is cut off
        Assert.Null(pf.FindPath(start, goal));
        Assert.Null(pf.FindPathAbstract(start, goal));
        Assert.False(pf.IsReachable(start, goal));
        g.SetPortalEnabled(lad.Id, true); // lower it again
        Assert.NotNull(pf.FindPathAbstract(start, goal));
    }

    [Fact]
    public void FloorCollapse_InvalidatesOnlyAffectedPortals()
    {
        var (g, sc, s01, s12, lad) = Tower();
        var pf = new LevelPathfinder(g);
        pf.RefreshAbstraction();
        int totalChunkLevels = g.ChunkLevelCount;
        Assert.NotNull(pf.FindPathAbstract(new(15, 15, 0), new(15, 15, 2)));

        // Collapse the NE corner of the z+1 floor where the 0->1 stairs land.
        var hit = g.CollapseFloor(24, 10, 29, 15, 1);
        Assert.Equal(new[] { s01.Id }, hit);
        Assert.True(sc.Enabled && s12.Enabled && lad.Enabled);
        Assert.False(s01.Enabled);

        pf.RefreshAbstraction();
        output.WriteLine($"collapse: re-zoned {pf.LastZonesRebuilt} of {totalChunkLevels} chunk-levels");
        Assert.True(pf.LastZonesRebuilt <= 4);
        // z1 and z2 are now unreachable from ground (only route was s01); cellar still reaches ground.
        Assert.Null(pf.FindPath(new(15, 15, 0), new(15, 15, 2)));
        Assert.False(pf.IsReachable(new(15, 15, 0), new(15, 15, 1)));
        Assert.True(pf.IsReachable(new(15, 15, -1), new(40, 40, 0)));

        // Open a second route (a ladder from ground to z1 elsewhere): re-plans through it.
        var lad2 = g.AddPortal(PortalKind.Ladder, new(12, 12, 0), new(12, 12, 1), 30);
        var r = pf.FindPathAbstract(new(15, 15, 0), new(15, 15, 2))!;
        Assert.Contains(lad2.Id, r.PortalsUsed);
        // A cell that is now a hole cannot be a destination.
        Assert.Null(pf.FindPath(new(15, 15, 0), new(26, 12, 1)));
    }

    private static LevelGrid BigMap(ulong seed)
    {
        var g = new LevelGrid(256, 256, -2, 2);
        for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
                g.SetOpen(x, y, 0, DetRng.Range(seed, 0, (ulong)(y * 256 + x), 1, 0, 0, 100) >= 20); // 20% rubble/walls
        for (int cy = 0; cy < 8; cy++)
            for (int cx = 0; cx < 8; cx++)
            {
                int x0 = cx * 32 + 8, y0 = cy * 32 + 8;
                g.FillRect(x0, y0, x0 + 13, y0 + 13, 0, true);
                for (int z = -2; z <= 2; z++) if (z != 0) g.FillRect(x0, y0, x0 + 13, y0 + 13, z, true);
                g.AddPortal(PortalKind.Stairs, new(x0 + 1, y0 + 1, -1), new(x0 + 1, y0 + 1, 0), 40);
                g.AddPortal(PortalKind.Stairs, new(x0 + 12, y0 + 1, -2), new(x0 + 12, y0 + 1, -1), 40);
                g.AddPortal(PortalKind.Stairs, new(x0 + 12, y0 + 12, 0), new(x0 + 12, y0 + 12, 1), 40);
                g.AddPortal(PortalKind.Stairs, new(x0 + 1, y0 + 12, 1), new(x0 + 1, y0 + 12, 2), 40);
            }
        return g;
    }

    private static List<(Cell3, Cell3)> Queries(LevelGrid g, ulong seed, int n)
    {
        var res = new List<(Cell3, Cell3)>();
        uint draw = 0;
        Cell3 Pick()
        {
            while (true)
            {
                int x = DetRng.Range(seed, 1, 0, 2, draw++, 0, 256), y = DetRng.Range(seed, 1, 0, 2, draw++, 0, 256);
                int z = DetRng.Range(seed, 1, 0, 2, draw++, -2, 3);
                if (g.IsOpen(x, y, z)) return new(x, y, z);
            }
        }
        while (res.Count < n) res.Add((Pick(), Pick()));
        return res;
    }

    private static ulong Hash(PathResult? r)
    {
        if (r == null) return 1;
        ulong h = 1469598103934665603UL;
        foreach (var c in r.Cells) h = DetRng.Mix(h ^ (ulong)(c.X | (c.Y << 10) | ((c.Z + 2) << 20)));
        return h;
    }

    [Fact]
    public void Deterministic_SamePathEveryRun()
    {
        ulong Run()
        {
            var g = BigMap(7); var pf = new LevelPathfinder(g);
            ulong h = 0;
            foreach (var (a, b) in Queries(g, 7, 20)) h = DetRng.Mix(h ^ Hash(pf.FindPath(a, b)) ^ Hash(pf.FindPathAbstract(a, b)));
            return h;
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Perf_200RandomPaths_256x256x5()
    {
        var g = BigMap(11);
        var pf = new LevelPathfinder(g);
        var qs = Queries(g, 11, 200);

        var sw = Stopwatch.StartNew();
        pf.RefreshAbstraction();
        double buildMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var exact = qs.Select(q => pf.FindPath(q.Item1, q.Item2)).ToList();
        double exactMs = sw.Elapsed.TotalMilliseconds;
        long exactExpanded = exact.Where(r => r != null).Sum(r => (long)r!.Expanded);

        sw.Restart();
        var abs = qs.Select(q => pf.FindPathAbstract(q.Item1, q.Item2)).ToList();
        double absMs = sw.Elapsed.TotalMilliseconds;
        long absExpanded = abs.Where(r => r != null).Sum(r => (long)r!.Expanded);

        int found = exact.Count(r => r != null), foundAbs = abs.Count(r => r != null);
        int crossLevel = exact.Count(r => r != null && r.PortalsUsed.Count > 0);
        double worstRatio = 1;
        for (int i = 0; i < qs.Count; i++)
            if (exact[i] != null && abs[i] != null) worstRatio = Math.Max(worstRatio, (double)abs[i]!.Cost / Math.Max(1, exact[i]!.Cost));

        output.WriteLine($"map 256x256x5 ({g.CellCount} cells, {g.Portals.Count} portals, {pf.ZoneCount} zones), abstraction build {buildMs:F1} ms");
        output.WriteLine($"exact A*   : 200 queries {exactMs:F0} ms ({exactMs / 200:F2} ms/path), found {found}, cross-level {crossLevel}, expanded {exactExpanded}");
        output.WriteLine($"abstract   : 200 queries {absMs:F0} ms ({absMs / 200:F2} ms/path), found {foundAbs}, expanded {absExpanded}, worst cost ratio {worstRatio:F3}");

        // incremental invalidation on the big map: pull up one ladder-like portal, refresh the abstraction, re-plan
        sw.Restart();
        g.SetPortalEnabled(g.Portals[10].Id, false);
        pf.RefreshAbstraction();
        double invalMs = sw.Elapsed.TotalMilliseconds;
        output.WriteLine($"invalidate 1 portal: re-zoned {pf.LastZonesRebuilt} chunk-levels, refresh {invalMs:F1} ms (full build was {buildMs:F1} ms)");
        Assert.True(pf.LastZonesRebuilt <= 2);

        Assert.Equal(found, foundAbs);
        Assert.True(crossLevel > 20);
        Assert.True(exactMs < 20_000, $"exact A* too slow: {exactMs} ms");
        Assert.True(absMs < 10_000, $"abstract too slow: {absMs} ms");
        Assert.True(worstRatio < 1.5);
    }
}
