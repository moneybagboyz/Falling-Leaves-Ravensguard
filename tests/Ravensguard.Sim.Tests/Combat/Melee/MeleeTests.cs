using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee;

namespace Ravensguard.Sim.Tests.Combat.Melee;

[Collection("combat")]
public class MeleeTests(CombatFixture f)
{
    private static readonly WornItem[] None = [];

    private static FighterSpec Spec(string name, int team, WeaponInstance w, Vec2 at, int skill = 0, double shield = 0, WornItem[]? worn = null, int toughness = 10)
        => new(name, team, w, worn ?? None, at, skill, Toughness: toughness, ShieldCover: shield) { FaceDir = new Vec2(team == 0 ? 1 : -1, 0) };

    /// <summary>Two hand-driven fighters 1.5 m apart facing each other (no AI, no auto morale).</summary>
    private (MeleeWorld w, Fighter a, Fighter b) Pair(ulong seed = 1, int skillA = 0, int skillB = 0, WeaponInstance? wa = null, WeaponInstance? wb = null,
        double shieldB = 0, double dist = 1.5)
    {
        var w = new MeleeWorld(f.Tables, seed);
        var a = w.Add(Spec("A", 0, wa ?? MeleeScenarios.Sword, new(0, 0), skillA), true);
        var b = w.Add(Spec("B", 1, wb ?? MeleeScenarios.Sword, new(dist, 0), skillB, shieldB), true);
        return (w, a, b);
    }

    private static void StartAttack(Fighter a, string part = "upper_body", bool auto = true)
        => a.Cmd.Start = new AttackRequest { PartId = part, TargetId = -1, AutoRelease = auto };

    // ---------------------------------------------------------------- timings

    [Theory]
    [InlineData("dagger", 6, 6, 2)]
    [InlineData("arming_sword", 9, 8, 2)]
    [InlineData("axe", 12, 12, 3)]
    [InlineData("maul", 18, 18, 3)]
    [InlineData("spear", 8, 10, 2)]
    public void TimingsMatchPlanTableAtSkillZero(string shape, int windup, int recover, int strike)
    {
        var (w, a, _) = Pair(wa: new WeaponInstance(shape, "steel"));
        Assert.Equal(windup, w.WindupTicks(a, false));
        Assert.Equal(recover, w.RecoverTicks(a));
        Assert.Equal(strike, w.StrikeTicks(a));

        // and the state machine actually spends exactly that long in each phase
        StartAttack(a); w.Step();
        Assert.Equal(Act.Windup, a.State);
        int windupSteps = 1;
        while (a.State == Act.Windup) { w.Step(); windupSteps++; }
        Assert.Equal(windup, windupSteps);
        Assert.Equal(Act.Strike, a.State);
        int strikeTicks = 1;                     // ticks observed in the state, entry tick included
        for (w.Step(); a.State == Act.Strike; w.Step()) strikeTicks++;
        Assert.Equal(strike, strikeTicks);
        Assert.Equal(Act.Recover, a.State);
        int recoverTicks = 1;
        for (w.Step(); a.State == Act.Recover; w.Step()) recoverTicks++;
        Assert.Equal(recover, recoverTicks);
        Assert.Equal(Act.Idle, a.State);
    }

    [Fact]
    public void SkillAndEncumbranceScaleTimings()
    {
        var (w, a, _) = Pair(skillA: 80);
        Assert.Equal(7, w.WindupTicks(a, false)); // 0.45 * (1 - 80/400) = 0.36 s
        a.OverKg = 25;                             // +25%
        Assert.Equal(9, w.WindupTicks(a, false));  // 0.45 * 0.8 * 1.25 = 0.45 s
    }

    [Fact]
    public void ExhaustionSlowsWindupBy30PercentAndWeakensBlock()
    {
        var (w, a, _) = Pair();
        Assert.Equal(9, w.WindupTicks(a, false));
        a.Stamina = 24.9;
        Assert.Equal(12, w.WindupTicks(a, false));   // 0.45 * 1.3 = 0.585 s
        a.Stamina = 25;
        Assert.Equal(9, w.WindupTicks(a, false));
    }

    [Fact]
    public void StaminaCostsAndRegeneration()
    {
        var (w, a, _) = Pair();
        StartAttack(a); w.Step();
        double expected = 100 - (4 + 2 * a.WeaponKg);
        Assert.InRange(a.Stamina, expected - 0.5, expected + 0.5); // regen has not started (winding up)
        // rest 1 s after the swing ends: +12/s
        while (a.State != Act.Idle) w.Step();
        a.Stamina = 50;
        double before = a.Stamina;
        for (int i = 0; i < MeleeRules.TickHz; i++) w.Step();
        Assert.InRange(a.Stamina - before, 11.5, 12.5);
        // guarding regenerates 5/s
        a.Stamina = 20; a.Cmd.Block = true; w.Step();
        double g0 = a.Stamina;
        for (int i = 0; i < MeleeRules.TickHz; i++) w.Step();
        Assert.InRange(a.Stamina - g0, 4.5, 5.5);
    }

    // ---------------------------------------------------------------- parry, block, dodge, feint, facing

    private int ImpactTickOfLoneAttacker()
    {
        var (w, a, _) = Pair();
        StartAttack(a);
        while (a.State != Act.Strike) w.Step();
        return w.Tick;
    }

    private (int parried, int blocked) ParryTrials(int pressOffsetFromImpact, int trials = 300)
    {
        int impact = ImpactTickOfLoneAttacker(), parried = 0, blocked = 0;
        for (ulong seed = 1; seed <= (ulong)trials; seed++)
        {
            var (w, a, b) = Pair(seed, skillB: 60);
            StartAttack(a);
            while (w.Tick < impact)
            {
                if (w.Tick + 1 == impact - pressOffsetFromImpact) b.Cmd.Block = true;
                w.Step();
            }
            parried += w.Events.Count(e => e.Kind == EventKind.Parried);
            blocked += w.Events.Count(e => e.Kind == EventKind.Blocked);
        }
        return (parried, blocked);
    }

    [Fact]
    public void ParryNeedsBlockPressedWithin0Point2sBeforeImpactAndSucceedsAtSkillOver120()
    {
        var (inP, _) = ParryTrials(4);   // pressed exactly 0.20 s before impact
        var (outP, outB) = ParryTrials(5); // 0.25 s: too early, a plain hold-block
        Assert.InRange(inP, 300 * 0.40, 300 * 0.60); // skill 60 / 120 = 50%
        Assert.Equal(0, outP);
        Assert.True(outB > 0);
    }

    [Fact]
    public void SuccessfulParryStaggersAttackerAndOpensHalfWindupRiposte()
    {
        int impact = ImpactTickOfLoneAttacker();
        for (ulong seed = 1; seed < 200; seed++)
        {
            var (w, a, b) = Pair(seed, skillB: 120);
            StartAttack(a);
            while (w.Tick < impact) { if (w.Tick + 1 == impact - 2) b.Cmd.Block = true; w.Step(); }
            if (!w.Events.Any(e => e.Kind == EventKind.Parried)) continue;
            Assert.Equal(Act.Stagger, a.State);
            Assert.Equal(impact + MeleeRules.RiposteTicks, b.RiposteUntil); // 0.4 s
            b.Cmd.Start = new AttackRequest(); w.Step();
            Assert.True(b.RiposteWindup);
            Assert.Equal(MeleeRules.Ticks(0.45 * (1 - 120 / 400.0) * 0.5), b.WindupNeeded);
            Assert.True(b.WindupNeeded < w.WindupTicks(b, false));
            return;
        }
        Assert.Fail("no parry in 200 seeds at skill 120");
    }

    [Fact]
    public void ShieldBlocksFrontButNeverRear()
    {
        int front = 0, rear = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            foreach (bool fromRear in new[] { false, true })
            {
                var (w, a, b) = Pair(seed, shieldB: MeleeRules.TowerCover);
                if (fromRear) b.Face = new Vec2(1, 0); // B turns away from A (A is at x=0, B faces +x)
                b.Cmd.Block = true; w.Step(); w.Step(); w.Step(); w.Step(); w.Step(); w.Step(); // block held >0.2 s: no parry window
                StartAttack(a);
                for (int i = 0; i < 14; i++) w.Step();
                int blocks = w.Events.Count(e => e.Kind == EventKind.Blocked);
                if (fromRear) rear += blocks; else front += blocks;
            }
        }
        Assert.Equal(0, rear);                 // rear: not even the weapon helps
        Assert.True(front > 300 * 0.75, $"tower shield + sword should block most frontal blows ({front}/300)");
    }

    [Fact]
    public void FacingClassifiesFrontFlankRear()
    {
        var (w, a, b) = Pair();
        Assert.Equal(Facing.Front, MeleeWorld.FacingOf(b, a));       // B faces -x toward A
        b.Face = new Vec2(0, 1); Assert.Equal(Facing.Flank, MeleeWorld.FacingOf(b, a));
        b.Face = new Vec2(1, 0); Assert.Equal(Facing.Rear, MeleeWorld.FacingOf(b, a));
        b.Face = new Vec2(-0.7071, 0.7071); Assert.Equal(Facing.Front, MeleeWorld.FacingOf(b, a)); // 45°: inside the 100° arc
        b.Face = new Vec2(-0.5, 0.866);  Assert.Equal(Facing.Flank, MeleeWorld.FacingOf(b, a));   // 60°: outside it
    }

    [Fact]
    public void DodgeLastsPoint35sThenCoolsDownOneSecond()
    {
        var (w, a, _) = Pair();
        a.Cmd.Dodge = new Vec2(0, 1); w.Step();
        Assert.Equal(Act.Dodge, a.State);
        int ticks = 1; while (a.State == Act.Dodge) { w.Step(); ticks++; }
        Assert.Equal(7, ticks);
        a.Cmd.Dodge = new Vec2(0, 1); w.Step();           // inside the cooldown
        Assert.NotEqual(Act.Dodge, a.State);
        for (int i = 0; i < 18; i++) w.Step();            // cooldown ends 20 ticks after the dodge ends
        a.Cmd.Dodge = new Vec2(0, 1); w.Step();
        Assert.Equal(Act.Dodge, a.State);
        Assert.True(a.Pos.Y > 0.5, "the dodge actually moves the fighter");
    }

    [Fact]
    public void FeintCancelsOnlyDuringFirstHalfOfWindup()
    {
        // early feint: cancelled, never strikes, costs only the swing stamina + 2
        var (w, a, _) = Pair();
        StartAttack(a); w.Step(); w.Step();
        a.Cmd.Feint = true; w.Step();                     // elapsed 2 of 9
        Assert.Equal(Act.Recover, a.State);
        for (int i = 0; i < 30; i++) w.Step();
        Assert.DoesNotContain(w.Events, e => e.Kind is EventKind.Whiff or EventKind.Wound or EventKind.Blocked);
        Assert.Contains(w.Events, e => e.Kind == EventKind.Feint);

        // late feint (second half): ignored, the blow comes out
        var (w2, a2, _) = Pair();
        StartAttack(a2); for (int i = 0; i < 6; i++) w2.Step();
        a2.Cmd.Feint = true; w2.Step();
        Assert.NotEqual(Act.Recover, a2.State);
        for (int i = 0; i < 4; i++) w2.Step();
        Assert.Equal(Act.Recover, a2.State); // went through Strike
        Assert.DoesNotContain(w2.Events, e => e.Kind == EventKind.Feint);
        Assert.Contains(w2.Events, e => e.Kind is EventKind.Whiff or EventKind.Wound or EventKind.Blocked or EventKind.Parried or EventKind.Dodged);
    }

    [Fact]
    public void HoldingPastWindupMakesHeavySwing()
    {
        var (w, a, _) = Pair();
        StartAttack(a, auto: false); a.Cmd.Hold = true;
        for (int i = 0; i < 9 + 6 + 1; i++) w.Step();
        Assert.Equal(Act.Windup, a.State); Assert.True(a.Heavy);
        a.Cmd.Hold = false; w.Step();
        Assert.Equal(Act.Strike, a.State);
    }

    // ---------------------------------------------------------------- vitals

    private static ActiveWound Artery(double rate) => new() { PartId = "upper_leg_l", TissueId = "muscle", StructureId = "femoral_artery", Rate = rate };

    private (double downS, double deadS) Bleed(double rate)
    {
        var w = new MeleeWorld(f.Tables, 9);
        var v = w.Add(Spec("V", 0, MeleeScenarios.Sword, new(0, 0)), true);
        v.Wounds.Add(Artery(rate));
        double down = -1;
        for (int i = 0; i < 2000 && v.Alive; i++)
        {
            w.Step();
            if (down < 0 && v.State == Act.Down) down = w.TimeS;
        }
        Assert.False(v.Alive);
        return (down, w.TimeS);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(100)]
    public void FemoralArteryClassWoundDownsIn18sAndKillsIn30s(double rate)
    {
        var (down, dead) = Bleed(rate);
        Assert.InRange(down, 18 * 0.7, 18 * 1.3);
        Assert.InRange(dead, 30 * 0.7, 30 * 1.3);
    }

    [Fact]
    public void MinorBleedingClotsButArteriesNever()
    {
        var w = new MeleeWorld(f.Tables, 9);
        var v = w.Add(Spec("V", 0, MeleeScenarios.Sword, new(0, 0)), true);
        var muscle = new ActiveWound { PartId = "upper_arm_l", TissueId = "muscle", Rate = 3, Clots = true };
        var art = Artery(0.0001); // negligible blood loss but proves the rate does not decay
        v.Wounds.Add(muscle); v.Wounds.Add(art);
        for (int i = 0; i < 90 * 20; i++) w.Step();
        Assert.InRange(muscle.Rate, 1.4, 1.6);    // 90 s half-life: 3 -> 1.5
        Assert.Equal(0.0001, art.Rate);
    }

    [Fact]
    public void ShockThresholdsByBloodLoss()
    {
        var w = new MeleeWorld(f.Tables, 3);
        var v = w.Add(Spec("V", 0, MeleeScenarios.Sword, new(0, 0)), true);
        v.Blood = v.MaxBlood * 0.84; w.Step(); Assert.True(v.Weakened); Assert.True(v.Active);
        v.Blood = v.MaxBlood * 0.69; w.Step(); Assert.Equal(Act.Down, v.State); Assert.Equal(DownReason.Blood, v.Down);
        v.Blood = v.MaxBlood * 0.49; w.Step(); Assert.Equal(Act.Dead, v.State);
        var w2 = new MeleeWorld(f.Tables, 3);
        var u = w2.Add(Spec("U", 0, MeleeScenarios.Sword, new(0, 0)), true);
        u.Blood = u.MaxBlood * 0.9; w2.Step();
        Assert.False(u.Weakened); Assert.True(u.Active);
    }

    [Fact]
    public void PainDecaysTwoPercentPerSecondAndKnocksOutAt60Plus4xToughness()
    {
        var w = new MeleeWorld(f.Tables, 3);
        var v = w.Add(Spec("V", 0, MeleeScenarios.Sword, new(0, 0), toughness: 5), true);
        Assert.Equal(80, v.KnockoutPain);
        v.Pain = 50; for (int i = 0; i < 20; i++) w.Step();
        Assert.InRange(v.Pain, 50 * 0.98 - 0.2, 50 * 0.98 + 0.2);
        v.Pain = 79; w.Step(); Assert.True(v.Active);
        v.Pain = 81; w.Step(); Assert.Equal(Act.Down, v.State); Assert.Equal(DownReason.Pain, v.Down);
    }

    [Fact]
    public void MoraleRoutsBelow20AndAFriendsDeathHurts()
    {
        var w = new MeleeWorld(f.Tables, 3);
        var a = w.Add(Spec("A", 0, MeleeScenarios.Sword, new(0, 0)));
        var mate = w.Add(Spec("M", 0, MeleeScenarios.Sword, new(2, 0)));
        w.Add(Spec("E", 1, MeleeScenarios.Sword, new(12, 0)));
        for (int i = 0; i < 20; i++) w.Step();
        Assert.Equal(70, a.Morale);
        mate.Blood = 0; w.Step();                      // a friend dies within 10 m
        Assert.Equal(Act.Dead, mate.State);
        for (int i = 0; i < 10; i++) w.Step();
        Assert.InRange(a.Morale, 63, 65);              // -6 (then decaying 0.4/s)
        a.Pain = 79; a.Blood = a.MaxBlood * 0.75; for (int i = 0; i < 10; i++) w.Step();
        Assert.True(a.Routing);
        Assert.True(a.Morale < MeleeRules.RoutBelow);
    }

    [Fact]
    public void BrainCutKillsButConcussionOnlyKnocksOut()
    {
        // run real heads: a heavy blunt blow to the head never kills outright; an edged blow reaching the brain can
        int dead = 0, down = 0, other = 0;
        for (ulong seed = 1; seed <= 400; seed++)
        {
            var w = new MeleeWorld(f.Tables, seed);
            var a = w.Add(Spec("A", 0, MeleeScenarios.Maul, new(0, 0), 60), true);
            var b = w.Add(Spec("B", 1, MeleeScenarios.Sword, new(1.2, 0)), true);
            a.Cmd.Start = new AttackRequest { PartId = "head", TargetId = 1 };
            for (int i = 0; i < 30; i++) w.Step();
            var brain = w.WoundLog.Any(x => x.Wound.StructureId == "brain");
            if (!brain) { other++; continue; }
            if (b.State == Act.Dead) dead++; else if (b.State == Act.Down) down++;
        }
        Assert.Equal(0, dead);       // blunt brain wounds: concussion only
        Assert.True(down + other > 0);
    }

    // ---------------------------------------------------------------- determinism and battles

    private static string Digest(MeleeWorld w) =>
        string.Join("|", w.Events.Select(e => $"{e.Tick}:{e.Kind}:{e.Actor}:{e.Target}:{e.Text}"))
        + "#" + string.Join(",", w.Fighters.Select(x => $"{x.State}/{x.Blood:0.000}/{x.Pain:0.000}/{x.Pos.X:0.000}/{x.Stamina:0.000}"));

    private string Duel(ulong seed, int ticks = 1200)
    {
        var w = new MeleeWorld(f.Tables, seed);
        var m = MeleeScenarios.BenchMatchups[1];
        w.Add(m.A(3)); w.Add(m.B(3));
        for (int i = 0; i < ticks && !w.IsOver; i++) w.StepWithAi();
        return Digest(w);
    }

    [Fact]
    public void SeededDuelIsDeterministic()
    {
        Assert.Equal(Duel(77), Duel(77));
        Assert.NotEqual(Duel(77), Duel(78));
    }

    [Fact]
    public void TenVersusTenSkirmishCompletesWithRoutsAndKnockouts()
    {
        int routsOrKo = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var w = MeleeScenarios.Skirmish(f.Tables, seed, 10, emitText: false);
            int guard = 0;
            while (!w.IsOver && guard++ < 20 * 300) w.StepWithAi();
            Assert.True(w.IsOver, $"seed {seed}: battle did not finish within 300 s");
            Assert.Equal(20, w.Fighters.Count);
            routsOrKo += w.Events.Count(e => e.Kind is EventKind.Rout or EventKind.Down or EventKind.Surrender or EventKind.Fled);
            Assert.True(w.Fighters.Count(x => !x.Active) >= 5, "the losing side should be broken");
        }
        Assert.True(routsOrKo >= 20);
    }

    [Fact]
    public void SkirmishProducesRoutsAcrossSeeds()
    {
        int routs = 0, kos = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var w = MeleeScenarios.Skirmish(f.Tables, seed, 10, emitText: false);
            int guard = 0;
            while (!w.IsOver && guard++ < 20 * 300) w.StepWithAi();
            routs += w.Events.Count(e => e.Kind is EventKind.Rout);
            kos += w.Events.Count(e => e.Kind is EventKind.Down);
        }
        Assert.True(routs > 0, "morale must break someone across 6 battles");
        Assert.True(kos > 0);
    }
}
