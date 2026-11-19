using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Tests.Combat.Melee2;

/// <summary>melee-and-ranged 8: ML-9 to ML-13, launchers, the aim cone, paths, reloads and quivers, thrown and improvised weapons.</summary>
[Collection("fight")]
public class FightRangedTests(FightFixture fx)
{
    private Fighter Archer(FightWorld w, string launcher = "longbow", string ammo = "bodkin_arrow", int count = 24, int x = 0, int skill = 50) =>
        w.Add(fx.Kit("Archer", 0, x, launcher: launcher, ammo: ammo, ammoCount: count, skill: skill), true);

    [Fact]
    public void ML9_LauncherMomentum()
    {
        var w = fx.World();
        Assert.Equal(120_037, w.LauncherMomentumMilli(45, 55_000, 1000, 0));      // longbow bodkin at the muzzle
        Assert.InRange(w.LauncherMomentumMilli(45, 55_000, 1000, 100_000), 105_600 * 99 / 100, 105_600 * 101 / 100);
        Assert.Equal(132_405, w.LauncherMomentumMilli(70, 65_000, 600, 0));       // crossbow quarrel
    }

    [Fact]
    public void ML10_AimCone_Skill50Hits21Percent_Skill100Hits66Percent_At30m()
    {
        var w = fx.World();
        Assert.Equal(1750, w.AimSigmaMdeg(50, false, 40, false, false));          // 3 x (1 - 50/120)
        Assert.Equal(500, w.AimSigmaMdeg(100, false, 40, false, false));
        Assert.Equal(3500, w.AimSigmaMdeg(50, true, 0, false, false));            // released early x2
        Assert.Equal(5250, w.AimSigmaMdeg(50, false, 2, false, false));           // a quick shot x3
        foreach (var (skill, pct) in new[] { (50, 210), (100, 660) })
        {
            int sigma = w.AimSigmaMdeg(skill, false, 40, false, false), hits = 0, n = 50_000;
            for (int i = 0; i < n; i++)
                if (Math.Abs(FightWorld.AimOffsetMm(30_000, sigma, DetRng.Range(77, i, 1, RngStreams.AimError, 0, 0, 256))) <= 250) hits++;
            Assert.InRange(hits * 1000 / n, pct - 20, pct + 20);
        }
    }

    [Fact]
    public void ML11_AProjectileHitsTheFirstBodyOnItsPath_AndLobsOverFriendsAtLongRange()
    {
        // an ally 5 m out on the line takes the arrow meant for the enemy at 10 m
        var w = fx.World();
        var archer = Archer(w);
        var ally = w.Add(fx.Kit("Ally", 0, 5000, faceX: 1024), true);
        var foe = w.Add(fx.Kit("Foe", 1, 10_000), true);
        w.LaunchAt(archer, foe.X, foe.Y, 1250, 0);
        for (int i = 0; i < 10; i++) w.Step();
        Assert.Contains(w.Events, e => e.Target == ally.Id && e.Kind is FightEventKind.ProjectileHit or FightEventKind.Stopped || e.Actor == ally.Id && e.Kind == FightEventKind.Blocked);
        Assert.DoesNotContain(w.Events, e => e.Target == foe.Id);
        // at 30 m the shot arcs: it passes over the ally and comes down on the enemy
        var w2 = fx.World();
        var a2 = Archer(w2);
        var friend = w2.Add(fx.Kit("Ally", 0, 5000, faceX: 1024), true);
        var far = w2.Add(fx.Kit("Foe", 1, 30_000), true);
        var p = w2.LaunchAt(a2, far.X, far.Y, 1250, 0);
        Assert.True(p.ApexMm > 0);
        for (int i = 0; i < 20; i++) w2.Step();
        Assert.DoesNotContain(w2.Events, e => e.Target == friend.Id || e.Actor == friend.Id);
        Assert.Contains(w2.Events, e => e.Target == far.Id && e.Kind is FightEventKind.ProjectileHit or FightEventKind.Stopped);
    }

    [Fact]
    public void ML12_ReloadTimes_CrossbowCancelledByMovement_QuiverOf24Empties()
    {
        var w = fx.World();
        var bow = Archer(w);
        bow.Cmd.Reload = true; w.Step();
        int t0 = w.Tick;
        while (bow.Act == FAct.Reloading) w.Step();
        Assert.Equal(20, w.Tick - t0 + 1);                                         // nocking takes 1.0 s
        Assert.True(bow.Loaded); Assert.Equal(23, bow.Ammo);
        var xb = Archer(w, "crossbow", "quarrel", 20, x: 3000);
        xb.Cmd.Reload = true; w.Step();
        t0 = w.Tick;
        while (xb.Act == FAct.Reloading) w.Step();
        Assert.Equal(200, w.Tick - t0 + 1);                                        // spanning takes 10.0 s
        var xb2 = Archer(w, "crossbow", "quarrel", 20, x: 6000);
        xb2.Cmd.Reload = true; w.Step();
        for (int i = 0; i < 50; i++) w.Step();
        xb2.Cmd.MoveX = 1024; xb2.Cmd.Sprint = true; w.Step();                     // moving at more than 1.5 m/s cancels the spanning
        Assert.Equal(FAct.Idle, xb2.Act); Assert.False(xb2.Loaded); Assert.Equal(20, xb2.Ammo);
        // a quiver of 24: 24 shots, then nothing
        var w2 = fx.World();
        var b2 = Archer(w2);
        var foe = w2.Add(fx.Kit("Foe", 1, 30_000), true);
        int shots = 0;
        for (int i = 0; i < 24 * 80 && shots < 30; i++)
        {
            if (b2.Act == FAct.Idle && !b2.Loaded && b2.Ammo > 0) b2.Cmd.Reload = true;
            else if (b2.Act == FAct.Idle && b2.Loaded) b2.Cmd.Start = new AttackReq { Target = foe.Id, Part = -1, Gap = -1, Auto = true };
            w2.Step();
            shots = w2.Events.Count(e => e.Kind == FightEventKind.Shot);
        }
        Assert.Equal(24, shots);
        Assert.Equal(0, b2.Ammo); Assert.False(b2.Loaded);
    }

    [Fact]
    public void ML13_ThrowAndImprovised()
    {
        var w = fx.World();
        int v = w.ThrowSpeedMmps(800, 1250);
        Assert.InRange(v, 14_200 * 99 / 100, 14_200 * 101 / 100);                 // javelin 14.2 m/s
        Assert.InRange(w.ThrowMomentumMilli(800, v), 110_200 * 99 / 100, 110_200 * 101 / 100);
        Assert.InRange(w.ThrowRangeMm(v), 25_600 * 99 / 100, 25_600 * 101 / 100);
        Assert.InRange(w.ThrowSpeedMmps(330, 1250), 16_100, 16_300);               // dagger 16.2
        // a thrown javelin flies, lands and lies on the ground
        var thrower = w.Add(fx.Kit("T", 0, 0, throws: [new ThrowStock("javelin", "steel", 2)]), true);
        var foe = w.Add(fx.Kit("F", 1, 8000), true);
        thrower.Cmd.Throw = (foe.X, foe.Y);
        for (int i = 0; i < 60; i++) w.Step();
        Assert.Contains(w.Events, e => e.Kind == FightEventKind.Throw);
        Assert.Single(thrower.Throwables); Assert.Equal(1, thrower.Throwables[0].Count);
        Assert.Contains(w.Ground, g => g.Shape == "javelin");
        // an improvised stool breaks after 3 +- 1 blows
        var blows = new List<int>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var w2 = fx.World(seed);
            var brawler = w2.Add(fx.Kit("B", 0, 0, improvised: "stool", skill: 40));
            w2.Add(fx.Kit("V", 1, 1200, skill: 20));
            Assert.Equal("improvised", brawler.Family);
            int n = 0;
            for (int i = 0; i < 2400 && brawler.Improvised != null; i++) { w2.StepWithAi(); }
            n = brawler.ImprovisedBlows;
            if (brawler.Improvised == null) blows.Add(n);
        }
        Assert.NotEmpty(blows);
        Assert.All(blows, n => Assert.InRange(n, 2, 4));
        // a bottle breaks on its first blow and leaves shards
        var w3 = fx.World(3);
        var b = w3.Add(fx.Kit("B", 0, 0, improvised: "bottle"));
        w3.Add(fx.Kit("V", 1, 1200, skill: 10));
        for (int i = 0; i < 1200 && b.Improvised != null; i++) w3.StepWithAi();
        Assert.Null(b.Improvised);
        Assert.Equal(1, b.ImprovisedBlows);
        Assert.Contains(w3.Ground, g => g.Shape == "shards");
        Assert.Equal("fist", b.Weapon.Shape.Id);
    }
}
