using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.RegionsC;

/// <summary>The spec's seam margins (note N5/N25) as a generator invariant: no settlement, lot or field of any region of the grid comes near its edge, for both pinned seeds.</summary>
public class SeamMarginTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1234UL)]
    [InlineData(1UL)]
    public void The_seam_margins_hold_for_every_region_of_the_grid(ulong seed)
    {
        var w = SeamWorld.Get(seed);
        int regions = 0, towns = 0, villages = 0;
        int minSettle = int.MaxValue, minLot = int.MaxValue, minField = int.MaxValue;
        var bad = new System.Collections.Concurrent.ConcurrentBag<string>();
        var gate = new object();
        Parallel.For(0, RegionCoord.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, id =>
        {
            var rc = RegionCoord.FromId(id);
            var r = w.Fresh(rc);
            var m = RegionSeamMargins.Of(r);
            lock (gate)
            {
                regions++; towns += r.Settlements.Count(s => s.Class == SettlementClass.Town); villages += r.Settlements.Count(s => s.Class == SettlementClass.Village);
                minSettle = Math.Min(minSettle, m.SettlementCentre); minLot = Math.Min(minLot, m.LotCorner); minField = Math.Min(minField, m.FieldTile);
            }
            foreach (var why in m.Violations()) bad.Add($"{rc}: {why}");
        });
        output.WriteLine($"seed {seed}: {regions} regions, {towns} towns, {villages} villages; nearest settlement centre {minSettle} m from an edge, nearest lot corner {minLot} m, nearest field tile {minField} m");
        Assert.Equal(RegionCoord.Count, regions);
        Assert.True(towns + villages > 0, "the grid has settlements to check");
        Assert.Empty(bad);
    }
}
