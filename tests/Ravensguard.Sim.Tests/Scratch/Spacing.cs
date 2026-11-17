using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;
namespace Ravensguard.Sim.Tests.Scratch;
public class Spacing(ITestOutputHelper output)
{
    [Fact]
    public void Print()
    {
        var dir = ContinentSettings.FindBaseContent()!;
        int ok = 0, tot = 0;
        foreach (ulong seed in new ulong[] { 1, 2, 7, 42, 1234 })
        {
            var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
            var hs = HistorySettings.LoadDefaults(dir); hs.Seed = seed;
            var rs = RegionSettings.LoadDefaults(dir); rs.Seed = seed;
            var c = ContinentGenerator.Generate(cs); var h = HistoryGenerator.Generate(c, hs);
            var pick = EmbarkSite.AutoPick(c, h);
            var r = RegionGenerator.Generate(c, h, pick.CellX, pick.CellY, rs);
            var towns = r.Towns.ToList();
            var nn = towns.Select(t => towns.Where(u => u != t).Select(u => Math.Sqrt(Math.Pow(t.CenterX - u.CenterX, 2) + Math.Pow(t.CenterY - u.CenterY, 2)) / 1000.0).DefaultIfEmpty(99).Min()).ToList();
            int good = nn.Count(d => d >= 6 && d <= 10); ok += good; tot += nn.Count;
            output.WriteLine($"seed {seed} pick {pick.CellX},{pick.CellY} towns {towns.Count}: {string.Join(", ", nn.Select(d => d.ToString("0.0")))}  good {good}/{nn.Count}");
        }
        output.WriteLine($"TOTAL {ok}/{tot} = {100.0 * ok / tot:0}%");
    }
}
