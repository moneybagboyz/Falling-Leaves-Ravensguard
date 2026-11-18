using Ravensguard.Sim.Core;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;
namespace Ravensguard.Sim.Tests.Farming;
public class Scratch2(ITestOutputHelper o)
{
    [Fact] public void Fert()
    {
        var r = EconFixture.Pop.Region; var fs = r.Fields;
        var f = fs.Select(x => (int)x.Fertility).OrderBy(x => x).ToArray();
        o.WriteLine($"fields {f.Length} mean {f.Average():F1} p10 {f[f.Length / 10]} p50 {f[f.Length / 2]} p90 {f[f.Length * 9 / 10]} min {f[0]} max {f[^1]} people {EconFixture.Pop.People.Count} acres/person {f.Length / (double)EconFixture.Pop.People.Count:F2}");
        double net = fs.Where(x => x.Slot != 2).Sum(x => Math.Min((int)x.Fertility, 91) * 52800.0 / 70 - 10560) / 1100;
        o.WriteLine($"potential net per year (no weather, 2/3 sown) = {net:F0} measures vs demand {EconFixture.Pop.People.Count * 60 * 0.92:F0}; slots {fs.Count(x => x.Slot == 0)} {fs.Count(x => x.Slot == 1)} {fs.Count(x => x.Slot == 2)}");
    }
}
