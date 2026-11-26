using Ravensguard.Sim.Combat;
using System.Diagnostics;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Battle;

/// <summary>CV-B budget: 2,000 soldiers with 300 cavalry, 100 of them horse archers (kicks on), the mounted layer and the whole tick.</summary>
[Collection(TimingCollection.Name)]
public class CavalryBBudgetTests(FightFixture fx, ITestOutputHelper output) : IClassFixture<FightFixture>
{
    private FightWorld Big()
    {
        var w = fx.World(50); w.RecordEvents = false;
        var book = w.EnsureFormations();
        for (int team = 0; team < 2; team++)
        {
            for (int k = 0; k < 10; k++)
            {
                var ids = new List<int>();
                for (int i = 0; i < 100; i++)
                {
                    var kit = (i % 3 == 0 ? fx.Kit("s", team, 0, 0, skill: 40, shield: "heater") : fx.Kit("w", team, 0, 0, skill: 40)) with { Courage = 100, Tactics = i == 0 ? 30 : -1 };
                    ids.Add(w.Add(kit).Id);
                }
                var r = book.Form(w, team, (byte)(k + 1), FormationKind.Infantry, ids, team == 0 ? -10_000 : 10_000, (k - 5) * 34_000, team == 0 ? 1024 : -1024, 0, Formation.Line, 4, place: true);
                r.CaptainAlways = true;
            }
            for (int k = 0; k < 10; k++)
            {
                bool bows = k % 3 == 0;
                var ids = new List<int>();
                for (int i = 0; i < 15; i++)
                {
                    var kit = bows
                        ? new FighterKit { Name = "h", Team = team, Launcher = "longbow", Ammo = "bodkin_arrow", AmmoCount = 24, Worn = [new WornItem("tunic", "linen")], Skill = 40, ArcherySkill = 50, Courage = 100, Tactics = i == 0 ? 50 : -1, FaceX = team == 0 ? 1024 : -1024 }
                        : fx.Kit("R", team, 0, 0, weapon: MountedCalibration.Lance, skill: 50, worn: FightCalibration.Mailed.Worn, faceX: team == 0 ? 1024 : -1024) with { Shield = "heater", Courage = 100, Tactics = i == 0 ? 50 : -1 };
                    ids.Add(w.AddMounted(kit, new MountKit { Riding = 50 + i % 7, GearKg = 20, RiderKg = 80, TrainedPm = 900 }).Id);
                }
                var r = book.Form(w, team, (byte)(40 + k), FormationKind.Cavalry, ids, team == 0 ? -110_000 : 110_000, (k - 5) * 34_000, team == 0 ? 1024 : -1024, 0, bows ? Formation.Loose : Formation.Line, 0, 500, place: true);
                r.CaptainAlways = true;
            }
        }
        w.LodCentre = (-10_000, 0);
        return w;
    }

    [Fact]
    public void CVB_Budget_HorseArchersInTheTwoThousandSoldierFight_TheMountedLayerStaysUnder08ms()
    {
        double Pass()
        {
            var w = Big();
            for (int i = 0; i < 20; i++) w.StepWithAi();
            var L = w.Mounts!; long p0 = L.PerfTicks;
            var sw = new Stopwatch(); double sum = 0, worst = 0; int n = 600;
            for (int i = 0; i < n; i++) { sw.Restart(); w.StepWithAi(); double ms = sw.Elapsed.TotalMilliseconds; sum += ms; worst = Math.Max(worst, ms); }
            double mounted = (L.PerfTicks - p0) * 1000.0 / Stopwatch.Frequency / n;
            output.WriteLine($"2,000 soldiers + 300 cavalry (100 horse archers), ticks 21..620: whole tick {sum / n:0.00} ms average, worst {worst:0.0} ms; the mounted layer {mounted:0.000} ms a tick");
            return mounted;
        }
        double avg = TestBudget.BestPass(0.8, 3, Pass);
        Assert.True(avg <= TestBudget.Ms(0.8), $"the mounted layer averaged {avg:0.000} ms a tick");
    }
}
