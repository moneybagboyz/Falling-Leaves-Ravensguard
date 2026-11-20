using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Heists;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit;

namespace Ravensguard.Sim.Tests.Heists;

/// <summary>HJ-8 (heists-and-jailbreaks 4.4): an indoor fight uses the site's real walls: a crossbow bolt does not pass a wall, a guard holding a doorway holds it, and the blades' wear is reported for the barrier to write back.</summary>
[Collection("fight")]
public class HeistFightTests(FightFixture fx)
{
    private static HeistMap Town() => HeistTests.Rules.Map("gaol_town")!;
    private static byte[] Doors(HeistMap m) { var d = new byte[m.Doors.Count + 1]; Array.Fill(d, (byte)2); return d; }

    [Fact]
    public void HJ8_ACrossbowBoltDoesNotPassAWall_ButFliesTrueInTheOpen()
    {
        var m = Town();                                                         // two cells side by side: the wall between them is the column x = 4 (cells at x 1-3 and 5-7)
        foreach (bool walls in new[] { true, false })
        {
            var w = fx.World(5); if (walls) w.Walls = new HeistWalls(m, Doors(m));
            var xb = w.Add(fx.Kit("Shooter", 0, 1500, 2500, launcher: "crossbow", ammo: "quarrel", ammoCount: 10, skill: 95, faceX: 1024, faceY: 0), true);
            var foe = w.Add(fx.Kit("Foe", 1, 6500, 2500, faceX: -1024, faceY: 0), true);
            Assert.True(xb.Loaded);
            w.LaunchAt(xb, foe.X, foe.Y, 1250, 0);
            for (int i = 0; i < 20; i++) w.Step();
            bool reached = w.Events.Any(e => e.Target == foe.Id || e.Actor == foe.Id);
            if (walls) { Assert.False(reached, "the bolt went through the wall"); Assert.Contains(w.Events, e => e.Kind == FightEventKind.ProjectileMiss && e.Actor == xb.Id); }
            else Assert.True(reached, "in the open the bolt reaches him");
        }
    }

    [Fact]
    public void HJ8_AGuardHoldingADoorwayBlocksTwoAttackers_WhoGetByInTheOpenButNotThroughTheWalls()
    {
        var m = Town();                                                         // the door at (22, 4) in the one-metre corridor: walls above and below it
        int Passed(bool walls)
        {
            var w = fx.World(9); if (walls) w.Walls = new HeistWalls(m, Doors(m));
            var guard = w.Add(fx.Kit("Guard", 0, 22_500, 4_500, faceX: -1024, faceY: 0), true);
            var a1 = w.Add(fx.Kit("A1", 1, 20_000, 3_700, faceX: 1024, faceY: 0), true);
            var a2 = w.Add(fx.Kit("A2", 1, 19_000, 4_500, faceX: 1024, faceY: 0), true);
            for (int i = 0; i < 400; i++)
            {
                guard.Cmd.MoveX = -1024; guard.Cmd.MoveY = 0;                  // he leans into the doorway
                a1.Cmd.MoveX = 1024; a1.Cmd.MoveY = 0; a2.Cmd.MoveX = 1024; a2.Cmd.MoveY = 0;
                w.Step();
            }
            return new[] { a1, a2 }.Count(a => a.X > 23_000);                    // past the door line
        }
        Assert.Equal(0, Passed(true));
        Assert.True(Passed(false) > 0, "in the open arena the walls are only a line on the floor");
    }

    [Fact]
    public void HJ8_TheBladesWearIsReportedForTheBarrier()
    {
        var w = fx.World(3);
        var a = w.Add(fx.Kit("A", 0, 0, weapon: new WeaponInstance("arming_sword", "steel", Ravensguard.Sim.Goods.ItemQuality.Common, false, 41)), false);
        var b = w.Add(fx.Kit("B", 1, 1400, weapon: new WeaponInstance("arming_sword", "steel", Ravensguard.Sim.Goods.ItemQuality.Common, false, 42), shield: "heater", worn: [new WornItem("gambeson", "linen"), new WornItem("mail_hauberk", "iron")]), false);
        for (int i = 0; i < 20 * 40 && !w.IsOver; i++) w.StepWithAi();
        var keeps = w.WeaponKeepPm().ToList();
        Assert.NotEmpty(keeps); Assert.All(keeps, k => Assert.InRange(k.KeepPm, 1, 999));
        Assert.Contains(keeps, k => k.Item is 41 or 42);
        Assert.NotEmpty(w.Events);
        _ = a; _ = b;
    }
}
