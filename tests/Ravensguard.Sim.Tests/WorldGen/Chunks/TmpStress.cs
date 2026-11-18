using System.Collections.Concurrent;
using Ravensguard.Sim.WorldGen.Chunks;
using Xunit;
using Ravensguard.Sim.Tests.WorldGen.ChunkGen;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.WorldGen.Chunks;

public class TmpStress(ChunkWorldFixture fx, ITestOutputHelper output) : IClassFixture<ChunkWorldFixture>
{
    [Fact]
    public void Stress()
    {
        var t = fx.Region.Settlements.OrderByDescending(x => x.TargetPop).First();
        int cx = t.CenterX / 32, cy = t.CenterY / 32;
        var coords = new List<(int, int)>();
        for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++) coords.Add((cx + dx, cy + dy));
        var baseG = new ChunkGenerator(fx.Region, fx.Population, fx.Content);
        var expect = coords.ToDictionary(c => c, c => baseG.GenerateLevels(c.Item1, c.Item2).ContentHash);
        int bad = 0;
        for (int it = 0; it < 40; it++)
        {
            var g = new ChunkGenerator(fx.Region, fx.Population, fx.Content);
            var rnd = new Random(it);
            var order = coords.OrderBy(_ => rnd.Next()).ToList();
            var res = new ConcurrentDictionary<(int, int), string>();
            Parallel.ForEach(order, new ParallelOptions { MaxDegreeOfParallelism = 8 }, c => res[c] = g.GenerateLevels(c.Item1, c.Item2).ContentHash);
            foreach (var c in coords) if (res[c] != expect[c]) { bad++; output.WriteLine($"iter {it} chunk {c} differs"); }
        }
        Assert.Equal(0, bad);
    }
}
