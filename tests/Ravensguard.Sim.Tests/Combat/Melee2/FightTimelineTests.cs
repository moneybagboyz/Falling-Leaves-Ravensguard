using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;

namespace Ravensguard.Sim.Tests.Combat.Melee2;

/// <summary>melee-and-ranged 8: ML-1 to ML-8, the timeline, defence, stamina and shield walls of <see cref="FightWorld"/>.</summary>
[Collection("fight")]
public class FightTimelineTests(FightFixture fx)
{
    private static int Slash(Fighter f) => f.Weapon.AttackIndex("slash");

    /// <summary>A starts an automatic slash at B on the first step; returns the impact tick.</summary>
    private static int Swing(FightWorld w, Fighter a, Fighter b, int attack = -1)
    {
        a.Cmd.Start = new AttackReq { Attack = attack >= 0 ? attack : Slash(a), Target = b.Id, Part = b.Plan.IndexOf("upper_body"), Gap = -1, Auto = true };
        w.Step();
        Assert.Equal(FAct.Windup, a.Act);
        return a.ImpactAt;
    }

    private static int Count(FightWorld w, FightEventKind k) => w.Events.Count(e => e.Kind == k);

    // ---------------------------------------------------------------- ML-1 timings

    [Fact]
    public void ML1_TimingTableInTicks_AtSkill0_AndSkill50()
    {
        // wind-up / recover at skill 0 (melee 3.1); the mace (0.65 s) is the P4-C calibration (Notes), the spear back at plan 4A.3's 0.40 / 0.50 s (close-out item 1), the rest the spec's table
        var expect = new Dictionary<string, (int W, int R, int S)>
        {
            ["dagger"] = (6, 6, 2), ["arming_sword"] = (9, 8, 2), ["longsword"] = (10, 9, 2), ["falchion"] = (10, 9, 2), ["hand_axe"] = (9, 9, 2), ["axe"] = (12, 12, 3),
            ["great_axe"] = (17, 16, 3), ["mace"] = (13, 13, 3), ["flail"] = (13, 14, 3), ["war_hammer"] = (12, 12, 3), ["maul"] = (18, 18, 3), ["club"] = (11, 10, 2),
            ["quarterstaff"] = (8, 8, 2), ["spear"] = (8, 10, 2), ["pike"] = (14, 16, 3), ["javelin"] = (7, 10, 2),
        };
        var w = fx.World();
        foreach (var (shape, e) in expect)
        {
            var f = w.Add(fx.Kit(shape, 0, 0, weapon: new WeaponInstance(shape, shape is "club" or "quarterstaff" ? "oak" : "steel"), skill: 0));
            Assert.Equal((e.W, e.R, e.S), (f.BaseWindTicks, f.BaseRecoverTicks, f.StrikeTicks));
            Assert.Equal(e.W, w.WindupTicks(f, false));
            Assert.Equal(e.R, w.RecoverTicks(f));
        }
        Assert.Equal(16, expect.Count);
        var bow = w.Add(fx.Kit("bow", 0, 0, launcher: "longbow", ammo: "bodkin_arrow", skill: 50));
        Assert.Equal(16, w.DrawTicks(bow));                                       // 18 x (400 - 50) / 400 = 15.75, rounded up
        var sword = w.Add(fx.Kit("sword", 0, 0, skill: 50));
        Assert.Equal(8, w.WindupTicks(sword, false));                             // 9 x 350 / 400 = 7.9 -> 8
        Assert.Equal(7, w.RecoverTicks(sword));                                   // 8 x 350 / 400 = 7
        Assert.Equal(4, w.WindupTicks(sword, true));                              // a riposte winds up at half
    }

    // ---------------------------------------------------------------- ML-2 cone and arcs

    [Fact]
    public void ML2_AttackConeAndArcs()
    {
        var w = fx.World();
        var a = w.Add(fx.Kit("A", 0, 0));
        var t = w.Add(fx.Kit("T", 1, 0));
        void Place(int deg) { t.X = 1200 * FightMath.Cos64[0] / 1024; double r = deg * Math.PI / 180; t.X = (int)Math.Round(1200 * Math.Cos(r)); t.Y = (int)Math.Round(1200 * Math.Sin(r)); }
        Place(59); Assert.True(w.InStrike(a, t, a.ReachMm));
        Place(61); Assert.False(w.InStrike(a, t, a.ReachMm));
        Place(-59); Assert.True(w.InStrike(a, t, a.ReachMm));
        Assert.Equal(512, fx.C.K.AttackConeQ10); Assert.Equal(658, fx.C.K.FrontArcQ10); Assert.Equal(-724, fx.C.K.RearArcQ10);
        // the defender's arcs: front 100 degrees, rear 90 degrees, flank the rest (t faces west, toward the origin)
        t.X = 1500; t.Y = 0; t.FX = -1024; t.FY = 0;
        Facing At(int deg) { double r = deg * Math.PI / 180; return w.FacingOf(t, t.X + (int)Math.Round(1500 * Math.Cos(Math.PI + r)), (int)Math.Round(1500 * Math.Sin(Math.PI + r))); }
        Assert.Equal(Facing.Front, At(49)); Assert.Equal(Facing.Flank, At(51));
        Assert.Equal(Facing.Flank, At(134)); Assert.Equal(Facing.Rear, At(136));
        // out of reach: whiffed
        var (w2, x, y) = fx.Duel(gap: 2200, aPlayer: true, bPlayer: true);
        int imp = Swing(w2, x, y);
        while (w2.Tick < imp) w2.Step();
        Assert.Equal(1, Count(w2, FightEventKind.Whiff));
    }

    // ---------------------------------------------------------------- ML-3 parry

    [Fact]
    public void ML3_ParryWindowIsFourTicks_AndSucceedsSkillOver120()
    {
        // pressed k ticks before the impact: a parry is possible for k = 0..3 only
        for (int k = 0; k <= 6; k++)
        {
            int parried = 0;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var (w, a, b) = fx.Duel(seed, skillB: 100, aPlayer: true, bPlayer: true);
                int imp = Swing(w, a, b);
                while (w.Tick < imp) { if (w.Tick + 1 == imp - k) b.Cmd.Block = true; w.Step(); }
                parried += Count(w, FightEventKind.Parried);
            }
            if (k <= 3) Assert.True(parried > 40, $"k={k}: {parried}"); else Assert.Equal(0, parried);
        }
        // success = skill / 120: 10,000 seeds at skill 60 -> 50% +- 1.5
        int ok = 0, n = 10_000;
        for (int i = 0; i < n; i++)
        {
            var (w, a, b) = fx.Duel((ulong)(5000 + i), skillB: 60, aPlayer: true, bPlayer: true);
            int imp = Swing(w, a, b);
            while (w.Tick < imp) { if (w.Tick + 1 == imp - 1) b.Cmd.Block = true; w.Step(); }
            ok += Count(w, FightEventKind.Parried);
        }
        Assert.InRange(ok * 1000 / n, 485, 515);
        // a success staggers the attacker (recover + 4) and opens an 8-tick riposte at half wind-up
        var (w3, a3, b3) = fx.Duel(7, skillB: 100, aPlayer: true, bPlayer: true);
        for (ulong s = 7; ; s++)
        {
            (w3, a3, b3) = fx.Duel(s, skillB: 100, aPlayer: true, bPlayer: true);
            int imp = Swing(w3, a3, b3);
            while (w3.Tick < imp) { if (w3.Tick + 1 == imp) b3.Cmd.Block = true; w3.Step(); }
            if (Count(w3, FightEventKind.Parried) > 0) break;
        }
        Assert.Equal(FAct.Stagger, a3.Act);
        Assert.Equal(w3.RecoverTicks(a3) + 4, a3.ActTicks);
        Assert.Equal(w3.Tick + 8, b3.RiposteUntil);
    }

    // ---------------------------------------------------------------- ML-4 dodge

    [Fact]
    public void ML4_Dodge_SevenTicks_CooldownTwenty_Evades80Percent_NotInWindup()
    {
        var (w, a, b) = fx.Duel(aPlayer: true, bPlayer: true, gap: 3000);
        int x0 = b.Y;
        b.Cmd.DodgeY = 1024; w.Step();
        int start = w.Tick;
        int ticks = 0;
        while (b.Act == FAct.Dodge) { ticks++; w.Step(); }
        Assert.Equal(7, ticks);                                                 // in the dodge after each of 7 steps, 7 steps of movement
        Assert.InRange(b.Y - x0, 1450, 1550);                                   // 7 x 4.3 m/s / 20 = 1.5 m
        Assert.Equal(start + 7 + 20, b.DodgeReady);
        while (w.Tick + 1 < b.DodgeReady) { b.Cmd.DodgeY = 1024; w.Step(); Assert.NotEqual(FAct.Dodge, b.Act); }
        b.Cmd.DodgeY = 1024; w.Step(); Assert.Equal(FAct.Dodge, b.Act);         // ready again exactly 20 ticks after the dodge ended
        // evasion 80% +- 1.5 over 10,000 blows
        int ev = 0, n = 10_000;
        for (int i = 0; i < n; i++)
        {
            var (w2, a2, b2) = fx.Duel((ulong)(20_000 + i), aPlayer: true, bPlayer: true);
            int imp = Swing(w2, a2, b2);
            while (w2.Tick < imp) { if (w2.Tick + 1 == imp - 1) b2.Cmd.DodgeY = 1024; w2.Step(); }
            ev += Count(w2, FightEventKind.Dodged);
        }
        Assert.InRange(ev * 1000 / n, 785, 815);
        // no dodge from a wind-up
        var (w3, a3, b3) = fx.Duel(aPlayer: true, bPlayer: true);
        Swing(w3, a3, b3);
        a3.Cmd.DodgeY = 1024; w3.Step();
        Assert.Equal(FAct.Windup, a3.Act);
    }

    // ---------------------------------------------------------------- ML-5 feint

    [Fact]
    public void ML5_FeintInFirstHalfOnly_SecondHalfIsABlock_AndSpendsTheDefendersParry()
    {
        var (w, a, b) = fx.Duel(aPlayer: true, bPlayer: true);
        w.Input(a.Id, new FightInput(FightInputKind.AttackDown, Slash(a), -1, -1));
        w.Step(); w.Step();                                                      // 2 of 8 ticks: the first half
        b.Cmd.Block = true; w.Step();                                            // the defender starts a parry against it
        w.Input(a.Id, new FightInput(FightInputKind.BlockDown));
        w.Step();
        Assert.Equal(1, Count(w, FightEventKind.Feint));
        Assert.Equal(FAct.Recover, a.Act);
        Assert.Equal(w.Tick + 8, b.ParryDeadUntil);
        // the second half of the wind-up: RMB is a block, not a feint
        var (w2, a2, b2) = fx.Duel(aPlayer: true, bPlayer: true);
        w2.Input(a2.Id, new FightInput(FightInputKind.AttackDown, Slash(a2), -1, -1));
        for (int i = 0; i < 5; i++) w2.Step();
        w2.Input(a2.Id, new FightInput(FightInputKind.BlockDown));
        w2.Step();
        Assert.Equal(0, Count(w2, FightEventKind.Feint));
        Assert.True(a2.Cmd.Block);
        Assert.Equal(FAct.Windup, a2.Act);
    }

    // ---------------------------------------------------------------- ML-6 stamina

    [Fact]
    public void ML6_Stamina_SwingBlockRegenAndExhaustion()
    {
        var w = fx.World();
        var a = w.Add(fx.Kit("A", 0, 0, skill: 50));
        Assert.Equal(690, w.SwingCost(a));                                       // 4 + 2 x 1.45 kg = 6.9
        Assert.Equal(414, w.SwingCost(a, SwingStyle.Quick));
        Assert.Equal(1035, w.SwingCost(a, SwingStyle.Heavy));
        a.Player = true; a.Stamina = 5000; w.Step();
        Assert.Equal(5060, a.Stamina);                                            // rest 12/s
        a.Cmd.Block = true; w.Step(); w.Step();
        Assert.Equal(5060 + 60 + 25, a.Stamina);                                  // guarding 5/s (the press tick is still idle at the vitals: 60, then 25)
        var plated = w.Add(fx.Kit("P", 0, 5000, worn: FightCalibration.Plated.Worn), true);
        Assert.True(plated.ArmourG > 20_000, $"{plated.ArmourG} g");
        plated.Stamina = 5000; w.Step();
        Assert.Equal(5025, plated.Stamina);                                       // armour over 20 kg: 5/s
        a.Stamina = 2000;
        Assert.Equal(11, w.WindupTicks(a, false));                                // 8 x 1.3 = 10.4 -> 11 below the 25 mark
        // a blocked blow costs 3 + M/50 (the resolver's block outcome)
        for (ulong seed = 1; seed < 400; seed++)
        {
            var (w2, x, y) = fx.Duel(seed, aPlayer: true, bPlayer: true);
            y.Cmd.Block = true; w2.Step(); w2.Step(); w2.Step(); w2.Step(); w2.Step();
            y.Stamina = 5000;
            x.Cmd.Start = new AttackReq { Attack = Slash(x), Target = y.Id, Part = y.Plan.IndexOf("upper_body"), Gap = -1, Auto = true };
            BlowResult? got = null;
            w2.OnBlow = (_, _, r, _) => { if (r.Blocked) got = r; };
            int before = 0;
            while (x.Act != FAct.Recover) { before = y.Stamina; w2.Step(); if (got != null) break; }
            if (got == null) continue;
            long absorbed = Math.Min(got.InitialMilli, Blocks.CapacityMilli(new BlockInput { Kind = BlockKind.Weapon, BlockerSkill = y.Skill }, fx.C.T.Cal));
            Assert.Equal(300 + absorbed / 500, got.Block.StaminaX100);
            Assert.Equal(before + 25 - got.Block.StaminaX100, y.Stamina);
            return;
        }
        Assert.Fail("no blocked blow in 400 seeds");
    }

    // ---------------------------------------------------------------- ML-7, ML-8 shield walls

    private (FightWorld W, Fighter[] Wall, Fighter Foe) Wall(bool spearBehind = false)
    {
        var w = fx.World();
        var wall = new[] { w.Add(fx.Kit("L", 0, 0, -900, shield: "heater"), true), w.Add(fx.Kit("M", 0, 0, 0, shield: "heater"), true), w.Add(fx.Kit("R", 0, 0, 900, shield: "heater"), true) };
        var foe = w.Add(fx.Kit("F", 1, 1500), true);
        if (spearBehind) w.Add(fx.Kit("S", 0, -800, 0, weapon: new WeaponInstance("spear", "steel")), true);
        foreach (var f in wall) f.Cmd.Block = true;
        for (int i = 0; i < 45; i++) w.Step();
        return (w, wall, foe);
    }

    [Fact]
    public void ML7_ShieldWall_ThreeBracedHeaters_MiddleCovers650_MovingBreaksIt_FlankIgnoresIt()
    {
        var (w, wall, foe) = Wall();
        var atk = foe.Weapon.Attacks[Slash(foe)];
        Assert.All(wall, f => Assert.True(w.Braced(f)));
        Assert.Equal(2, w.BracedNeighbours(wall[1]));
        Assert.Equal(650, w.ShieldCover(wall[1], Facing.Front, atk, false));      // 450 + 2 x 100
        Assert.Equal(550, w.ShieldCover(wall[0], Facing.Front, atk, false));
        Assert.Equal(0, w.ShieldCover(wall[1], Facing.Flank, atk, false));        // a flank or rear attack bypasses the wall
        Assert.Equal(0, w.ShieldCover(wall[1], Facing.Rear, atk, false));
        var flail = fx.C.T.Weapon("flail", "steel").Attacks[0];
        Assert.Equal(325, w.ShieldCover(wall[1], Facing.Front, flail, false));    // a flail ignores half of the cover
        // the middle man steps out 1.1 m: he is no longer braced, and the ends lose their wall
        wall[1].Cmd.Block = false; wall[1].Cmd.MoveX = 1024; for (int i = 0; i < 9; i++) w.Step(); wall[1].Cmd.MoveX = 0; w.Step();
        Assert.False(w.Braced(wall[1]));
        Assert.Equal(0, w.BracedNeighbours(wall[0]));
        Assert.Equal(450, w.ShieldCover(wall[0], Facing.Front, atk, false));
    }

    [Fact]
    public void ML8_SecondRankSpearStrikesOverTheWall_AndCannotBeShieldBlocked()
    {
        var (w, wall, foe) = Wall(spearBehind: true);
        var spear = w.Fighters.Single(f => f.Name == "S");
        Assert.True(w.OverWall(spear));
        Assert.False(w.OverWall(wall[1]));
        // a foe with a heater facing the wall: the spear's thrust gets no shield block from it, only its weapon
        var shielded = w.Add(fx.Kit("H", 1, 1500, 1500, shield: "heater"), true);
        shielded.Cmd.Block = true; for (int i = 0; i < 45; i++) w.Step();
        var thrust = spear.Weapon.Attacks[spear.Weapon.AttackIndex("thrust")];
        Assert.Equal(0, w.ShieldCover(shielded, Facing.Front, thrust, w.OverWall(spear)));
        Assert.True(w.WeaponCover(shielded, Facing.Front) > 0);
        // and the thrust reaches the foe 2.3 m in front of the spearman, past the wall
        spear.Cmd.Start = new AttackReq { Attack = spear.Weapon.AttackIndex("thrust"), Target = foe.Id, Part = -1, Gap = -1, Auto = true };
        int imp = spear.Act == FAct.Idle ? w.Tick + w.WindupTicks(spear, false) : 0;
        for (int i = 0; i < 20; i++) w.Step();
        Assert.Contains(w.Events, e => e.Actor == spear.Id && e.Kind is FightEventKind.Hit or FightEventKind.Missed or FightEventKind.Stopped || e.Target == spear.Id && e.Kind is FightEventKind.Blocked or FightEventKind.Parried or FightEventKind.Dodged);
        Assert.DoesNotContain(w.Events, e => e.Actor == spear.Id && e.Kind == FightEventKind.Whiff);
        _ = imp;
    }
}
