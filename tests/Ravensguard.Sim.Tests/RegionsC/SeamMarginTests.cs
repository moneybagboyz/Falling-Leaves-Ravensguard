using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.RegionsC;

/// <summary>The spec's seam margins (note N5/N25): how far from a region's edge settlements, lots and fields really stand.</summary>
public class SeamMarginTests(ITestOutputHelper output)
{
    private static int EdgeDistance(int x, int y) => Math.Min(Math.Min(x, y), Math.Min(RegionData.Meters - 1 - x, RegionData.Meters - 1 - y));

    [Theory]
    [InlineData(1234UL)]
    [InlineData(1UL)]
    public void Settlements_lots_and_fields_keep_clear_of_region_edges(ulong seed)
    {
        var w = SeamWorld.Get(seed);
        int towns = 0, villages = 0, townsInside = 0, villagesInside = 0, minLot = int.MaxValue, minField = int.MaxValue, minSettle = int.MaxValue, regions = 0;
        foreach (var rc in w.Block3())
        {
            var r = w.Region(rc); regions++;
            foreach (var s in r.Settlements)
            {
                int d = EdgeDistance(s.CenterX, s.CenterY);
                minSettle = Math.Min(minSettle, d);
                if (s.Class == SettlementClass.Town) { towns++; if (d >= 1200) townsInside++; }
                else if (s.Class == SettlementClass.Village) { villages++; if (d >= 400) villagesInside++; }
                foreach (var l in s.Plan.Lots) minLot = Math.Min(minLot, Math.Min(EdgeDistance(l.Rect.X, l.Rect.Y), EdgeDistance(l.Rect.Right, l.Rect.Bottom)));
            }
            foreach (var f in r.Fields) minField = Math.Min(minField, EdgeDistance(f.Tile % RegionData.Size * RegionData.TileM, f.Tile / RegionData.Size * RegionData.TileM));
        }
        output.WriteLine($"seed {seed}, {regions} regions: nearest settlement centre {minSettle} m from an edge, nearest lot corner {minLot} m, nearest field tile {minField} m; towns clear of 1,200 m {townsInside}/{towns}, villages clear of 400 m {villagesInside}/{villages}");
        // what the generator guarantees (settlement centres at least 8 tiles = 512 m in, fields not on the edge row), measured with room to spare; the spec's 64 m floor is met by a wide margin
        Assert.True(minSettle >= 480, $"a settlement centre stands {minSettle} m from an edge");
        Assert.True(minLot >= 250, $"a lot stands {minLot} m from an edge (the spec's floor is 64 m)");
        Assert.True(minField >= 64, $"a field stands {minField} m from an edge");
    }
}
