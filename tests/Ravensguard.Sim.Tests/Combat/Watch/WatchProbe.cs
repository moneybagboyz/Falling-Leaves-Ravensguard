using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Travel;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Watch;

[Collection("fight")]
public class WatchProbe(FightFixture fx, ITestOutputHelper output)
{
    [Fact]
    public void Probe()
    {
        var def = fx.Db.Get<EncounterDef>("bandit_ambush_small");
        var cats = new Dictionary<string, (int far, int all)>();
        for (int i = 0; i < 40; i++)
        {
            var hero = new FighterKit { Name = "Hero", Team = 0, Weapon = FightCalibration.Sword, Skill = 50, Shield = "heater", Worn = [new("tunic", "linen"), new("gambeson", "linen")], Person = 7 };
            var charge = new FighterKit { Name = "Edda", Team = 0, X = -800, Y = 1000, Weapon = FightCalibration.Sword, Skill = 30, Worn = [new("tunic", "linen")], Person = 8 };
            var ev = new EncounterEvent(500_000 + i * 3600, def.Id, def.Kind, 0, 0, 0, 0, 1, [101, 102, 103], def.Bark);
            var w = FightSetup.Encounter(fx.C, def, ev, hero, [charge], (ulong)(40 + i)).Start(fx.C, null);
            w.SetStance(0, WatchStance.Of(fx.C, StancePreset.Protect), 1); w.SetWatch(0, true);
            var h = w.Fighters[0]; var x = w.Fighters[1];
            for (int t = 0; t < 20 * 90 && !w.IsOver; t++)
            {
                w.StepWithAi();
                if (!h.Active || x.Act is FAct.Dead or FAct.Fled || x.Act == FAct.Surrendered) continue;
                string k = $"h:{h.Act}{(h.Routing ? "R" : "")} x:{(x.Routing ? "R" : x.Act == FAct.Down ? "D" : "F")} why:{w.Watch!.LastDecision}";
                var c = cats.GetValueOrDefault(k);
                cats[k] = (c.far + (w.DistMm(h, x) > 3000 ? 1 : 0), c.all + 1);
            }
        }
        foreach (var kv in cats.OrderByDescending(k => k.Value.far)) output.WriteLine($"{kv.Key} far {kv.Value.far} of {kv.Value.all}");
    }
}
