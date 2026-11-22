using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>P7-SG-C: height, merlons and murder holes (sieges-and-engines SG-10, SG-11).</summary>
[Collection("fight")]
public class AssaultHeightTests(FightFixture fx, ITestOutputHelper output)
{
    // ------------------------------------------------------------------ SG-10: height and merlons

    /// <summary>Drives an archer's draw and loose at a target until the first shot: the projectile and the shot event's aim sigma.</summary>
    private static (Projectile P, int Sigma) FirstShot(Scene s, Fighter archer, Fighter target)
    {
        for (int i = 0; i < 400 && s.W.Projectiles.Count == 0; i++)
        {
            if (archer.Act == FAct.Idle && !archer.Loaded && archer.Ammo > 0) archer.Cmd.Reload = true;
            else if (archer.Act == FAct.Idle && archer.Loaded) archer.Cmd.Start = new AttackReq { Target = target.Id, Part = -1, Gap = -1, Auto = true };
            s.W.Step();
        }
        Assert.NotEmpty(s.W.Projectiles);
        return (s.W.Projectiles[0], s.W.Events.First(e => e.Kind == FightEventKind.Shot).A);
    }

    [Fact]
    public void SG10_AnArcherOneLevelUp_HasPlus15PercentRangeAndAnAimFivePointsBetter_AndAMissileFromBelowMeetsTheMerlonAt600pm()
    {
        // the same archer, the same target, on the ground and on the walk: range x 1.15, sigma x 0.8
        (Projectile P, int Sigma) Shoot(int level)
        {
            var s = Scene.Make(fx);
            var a = s.Add(0, 0, 35_000, level: level, launcher: "longbow", ammo: "bodkin_arrow", ammoCount: 24, skill: 50);
            var t = s.Add(1, 30_000, 35_000);
            return FirstShot(s, a, t);
        }
        var (p0, sig0) = Shoot(0); var (p1, sig1) = Shoot(1);
        output.WriteLine($"range {p0.RangeMm} mm -> {p1.RangeMm} mm, aim sigma {sig0} -> {sig1} mdeg");
        Assert.Equal(p0.RangeMm * 1150 / 1000, p1.RangeMm);                                  // +15% range
        Assert.Equal(sig0 * 800 / 1000, sig1);
        // that tighter aim is about five points of hit chance for a skill-50 archer at 30 m (the 21% of ML-10)
        int Hits(int sigma) { int h = 0; for (int i = 0; i < 50_000; i++) if (Math.Abs(FightWorld.AimOffsetMm(30_000, sigma, DetRng.Range(77, i, 1, RngStreams.AimError, 0, 0, 256))) <= 250) h++; return h * 1000 / 50_000; }
        int h0 = Hits(sig0), h1 = Hits(sig1);
        output.WriteLine($"hit chance {h0 / 10.0:0.0}% -> {h1 / 10.0:0.0}%");
        Assert.InRange(h1 - h0, 30, 70);
        // a man on the tower's deck, level with the walk, shoots at a man on the walk with no height bonus
        var st = Scene.Make(fx);
        var deck = st.Add(0, 0, 35_000, level: 1, launcher: "longbow", ammo: "bodkin_arrow", ammoCount: 24, skill: 50);
        var onWalk = st.Add(1, 30_000, 35_000, level: 1);
        var (pl, sigl) = FirstShot(st, deck, onWalk);
        Assert.Equal(p0.RangeMm, pl.RangeMm); Assert.Equal(sig0, sigl);

        // the merlon: a missile from below that reaches the parapet cell is stopped at 600 permille (the draw of 0x7C2 keyed by the projectile and the cell); one from the walk or the deck is not
        var m = Scene.Make(fx);
        var below = m.Add(0, 45_500, 38_000); var level = m.Add(0, 47_500, 41_500, level: 1);
        int stopped = 0, n = 4000;
        for (uint serial = 1; serial <= n; serial++)
        {
            Assert.False(m.A.MissileStopped(45_500, 40_500, 3_500, level.Id, serial));            // from the walk: no merlon in the way
            if (m.A.MissileStopped(45_500, 40_500, 3_500, below.Id, serial)) stopped++;
        }
        output.WriteLine($"merlon stopped {stopped} of {n} ({stopped * 1000 / n} pm)");
        Assert.InRange(stopped * 1000 / n, 560, 640);
        Assert.True(m.A.MissileStopped(45_500, 41_000, 1_500, below.Id, 1));                      // a stone wall's face stops everything at the ground level
        Assert.False(m.A.MissileStopped(45_500, 41_500, 3_500, below.Id, 1));                     // over the walk: open air
        // and in a fight: an arrow from the ground at a man behind the parapet meets it in about 600 of 1,000 shots
        int blocked = 0, hit = 0, fights = 120;
        for (ulong seed = 1; seed <= (ulong)fights; seed++)
        {
            var s = Scene.Make(fx, seed);
            var a = s.Add(0, 45_500, 37_500, launcher: "longbow", ammo: "bodkin_arrow", ammoCount: 4, skill: 100, faceY: 1024);
            var d = s.Add(1, 45_500, 41_500, level: 1, faceY: -1024);
            FirstShot(s, a, d);
            for (int i = 0; i < 40; i++) s.W.Step();
            if (s.W.Events.Any(e => e.Kind == FightEventKind.ProjectileMiss && e.Actor == a.Id)) blocked++;
            else if (s.W.Events.Any(e => e.Target == d.Id && (e.Kind == FightEventKind.Hit || e.Kind == FightEventKind.Stopped || e.Kind == FightEventKind.ProjectileHit))) hit++;
        }
        output.WriteLine($"arrows at a man behind the parapet: {blocked} stopped, {hit} reached him of {fights}");
        Assert.InRange(blocked * 1000 / fights, 480, 720);
        Assert.True(hit + blocked >= fights - 6);
    }

    // ------------------------------------------------------------------ SG-11: murder holes

    [Fact]
    public void SG11_AStoneFrom6m_ArrivesAt10_8mps_AsABluntBlowOnTheHeadOrShoulders_AndBoilingWaterBurnsEveryoneInTheCell()
    {
        // the speed comes from an integer table, no Math.Sqrt: sqrt(2 x 9.81 x 6) = 10.85
        Assert.InRange(AssaultMath.FallSpeedMmps(6000), 10_800, 10_900);
        Assert.True(AssaultMath.FallSpeedMmps(3000) < AssaultMath.FallSpeedMmps(6000) && AssaultMath.FallSpeedMmps(12_000) < 16_000);
        output.WriteLine($"a stone falling 6 m: {AssaultMath.FallSpeedMmps(6000)} mm/s, 20 kg is {20L * AssaultMath.FallSpeedMmps(6000)} milli kg m/s");
        // a gatehouse: the hole at the cell (40, 41) two levels up, a man of the garrison at it, a man below
        var s = Scene.Make(fx);
        s.Add(1, 40_500, 41_500, level: 2);
        var victim = s.Add(0, 40_500, 41_500);
        var hole = s.A.AddHole(1, 40, 41, level: 2, stones: 3, cauldron: false);
        for (int i = 0; i < 3; i++) s.W.Step();
        Assert.Equal(1, hole.Dropped); Assert.Equal(2, hole.Stones);
        var hits = s.W.Events.Where(e => e.Kind == FightEventKind.Hit && e.Target == victim.Id).ToList();
        Assert.Single(hits);
        string part = victim.Plan.Parts[hits[0].Part].BaseId;
        Assert.Contains(part, new[] { "head", "upper_body" });                                   // the head or the shoulders
        Assert.True(victim.Book.Records.Count > 0);
        Assert.All(victim.Book.Records, r => Assert.Equal(WoundKind.Crush, r.Kind));             // blunt
        // every 10 s while the defenders have stones: not before, and one more at 200 ticks
        for (int i = 0; i < 190; i++) s.W.Step();
        Assert.Equal(1, hole.Dropped);
        for (int i = 0; i < 20; i++) s.W.Step();
        Assert.Equal(2, hole.Dropped);
        // no defender at the hole, no stone
        var s2 = Scene.Make(fx);
        s2.Add(0, 40_500, 41_500); var h2 = s2.A.AddHole(1, 40, 41, level: 2, stones: 3, cauldron: false);
        for (int i = 0; i < 300; i++) s2.W.Step();
        Assert.Equal(0, h2.Dropped);
        // boiling water: every person in the 1 m cell is burned, friend and foe, and not the man a metre off
        var s3 = Scene.Make(fx);
        s3.Add(1, 40_500, 41_500, level: 2);
        var a1 = s3.Add(0, 40_300, 41_300); var a2 = s3.Add(0, 40_700, 41_700); var friend = s3.Add(1, 40_500, 41_500); var off = s3.Add(0, 42_500, 41_500);
        var h3 = s3.A.AddHole(1, 40, 41, level: 2, stones: 0, cauldron: true);
        for (int i = 0; i < 3; i++) s3.W.Step();
        Assert.Equal(1, h3.Poured);
        foreach (var f in new[] { a1, a2, friend }) Assert.Contains(f.Book.Records, r => r.Kind == WoundKind.Burn);
        Assert.Empty(off.Book.Records);
    }
}
