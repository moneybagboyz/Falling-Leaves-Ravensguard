using System.Diagnostics;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.RegionsB;

public class RegionWalkTests(ITestOutputHelper output)
{
    private static double Land(Continent c, RegionCoord r)
    {
        int n = 0;
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) if (c.IsLand(Continent.Index(r.CellX + x, r.CellY + y))) n++;
        return n / 256.0;
    }

    /// <summary>The row with the longest run of 12 mostly-land regions.</summary>
    private static List<RegionCoord> FindLine(Continent c, int len)
    {
        List<RegionCoord> best = [];
        for (int y = 1; y < 15; y++)
        {
            var run = new List<RegionCoord>();
            for (int x = 0; x < 16; x++)
            {
                var r = new RegionCoord(x, y);
                if (Land(c, r) >= 0.5) { run.Add(r); if (run.Count >= len) return run[^len..]; } else run.Clear();
                if (run.Count > best.Count) best = [.. run];
            }
        }
        return best;
    }

    [Fact]
    public void Walking_twelve_regions_in_a_line_keeps_memory_bounded_and_pages_the_ones_left_behind()
    {
        var w0 = Seamless.SeamWorld.Get(1234);
        var line = FindLine(w0.Continent, 12);
        output.WriteLine("line " + string.Join(" ", line));
        Assert.True(line.Count >= 8, "a row of at least 8 land regions");
        var rig = new RegionRig(line[0]);
        long baseline = GC.GetTotalMemory(true);
        long peakBytes = 0; int peakResident = 0, peakDetailed = 0; double maxStageMs = 0, maxBuildMs = 0;
        var pager = (MemoryRegionPager)rig.Set.Pager;
        long hour = rig.Set.HomeWorld!.HourDone;
        foreach (var at in line)
        {
            var sw = Stopwatch.StartNew();
            // the manager's own rule would do this as the hero nears each border; here every region of the 5 x 5 block around the hero is asked for
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    var n = at.Offset(dx, dy);
                    if (!n.IsValid || rig.Mgr.IsReady(n) || Land(w0.Continent, n) < 0.3) continue;
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) <= 1 || rig.Set.Pager.Has(n.Id)) { rig.Mgr.GenerateNow(n); maxBuildMs = Math.Max(maxBuildMs, rig.Mgr.TryGet(n)!.BuildMs); }
                }
            hour += 6;
            rig.Sched.Update(GameTime.FromGameHours(hour), at);
            maxStageMs = Math.Max(maxStageMs, sw.Elapsed.TotalMilliseconds);
            long mem = GC.GetTotalMemory(true);
            peakBytes = Math.Max(peakBytes, mem);
            peakResident = Math.Max(peakResident, rig.Set.ResidentCount);
            peakDetailed = Math.Max(peakDetailed, rig.Set.Resident.Count(r => r.Level is RegionLevel.Live or RegionLevel.Detailed));
            Assert.True(rig.Set.Resident.Count(r => r.Level is RegionLevel.Live or RegionLevel.Detailed) <= 9);
            Assert.All(rig.Set.Resident, r => Assert.True(r.Coord.Chebyshev(at) <= 2 || r.Coord == rig.Home));
            output.WriteLine($"at {at}: resident {rig.Set.ResidentCount}, paged {pager.Ids.Count - rig.Set.ResidentCount + 0}, {mem / 1048576} MB, stage {sw.ElapsedMilliseconds} ms");
        }
        output.WriteLine($"PEAK {peakBytes / 1048576} MB over baseline {baseline / 1048576} MB; resident peak {peakResident}, detailed peak {peakDetailed}; slowest stage {maxStageMs:0} ms; slowest region build {maxBuildMs:0} ms; paged sections {pager.Ids.Count} ({pager.Ids.Sum(i => pager.Read(i)!.Length) / 1e6:0.0} MB)");
        Assert.True(peakResident <= 26, "25 resident regions at most (ring 2) plus the home region");
        Assert.True(peakBytes < 1_400L * 1048576, $"peak {peakBytes / 1048576} MB");
        Assert.True(pager.Ids.Count > 0, "regions were paged to the pager on the way");
    }
}
