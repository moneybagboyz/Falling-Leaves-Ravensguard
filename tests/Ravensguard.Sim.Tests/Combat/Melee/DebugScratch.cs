using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee;
using Xunit.Abstractions;
namespace Ravensguard.Sim.Tests.Combat.Melee;
[Collection("combat")]
public class DebugScratch(CombatFixture f, ITestOutputHelper o)
{
    [Fact]
    public void Dump()
    {
        var m = MeleeScenarios.BenchMatchups[0];
        for (int d = 0; d < 4; d++)
        {
            var w = new MeleeWorld(f.Tables, (ulong)(1000 + d));
            w.Add(m.A(d)); w.Add(m.B(d));
            while (!w.IsOver && w.Tick < 2000) w.StepWithAi();
            o.WriteLine($"--- duel {d}");
            foreach (var e in w.Events) o.WriteLine($"{e.Tick * 0.05:0.00} {e.Text}");
            foreach (var x in w.Fighters) o.WriteLine($"{x.Name}: blood {x.Blood:0} pain {x.Pain:0.0}/{x.KnockoutPain} morale {x.Morale:0} state {x.State}");
        }
        var sev = new Dictionary<string,int>();
        for (int d = 0; d < 200; d++) {
            var w = new MeleeWorld(f.Tables, (ulong)(1000 + d)){EmitText=false};
            w.Add(m.A(d)); w.Add(m.B(d));
            while (!w.IsOver && w.Tick < 2000) w.StepWithAi();
            foreach (var (_, wd) in w.WoundLog) if (wd.IsSevered) sev[wd.PartId] = sev.GetValueOrDefault(wd.PartId)+1;
        }
        o.WriteLine(string.Join(", ", sev.Select(k => k.Key + "=" + k.Value)));
    }
}
