using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee;

namespace Ravensguard.Sim.Tests.Combat.Melee;

/// <summary>Phase 0 review fixes: simultaneous impacts, outcome stickiness, attack choice.</summary>
[Collection("combat")]
public class MeleeReviewTests(CombatFixture f)
{
    private static readonly WornItem[] None = [];

    private static FighterSpec Spec(string name, int team, WeaponInstance w, Vec2 at, WornItem[]? worn = null, int skill = 60)
        => new(name, team, w, worn ?? None, at, skill) { FaceDir = new Vec2(team == 0 ? 1 : -1, 0) };

    private static readonly EventKind[] Resolutions = [EventKind.Wound, EventKind.Whiff, EventKind.Blocked, EventKind.Parried, EventKind.Dodged];

    [Fact]
    public void SimultaneousBlows_BothResolve_EvenWhenFirstOneKnocksTheOtherOut()
    {
        int mutual = 0;
        for (ulong seed = 1; seed <= 80; seed++)
        {
            var w = new MeleeWorld(f.Tables, seed);
            var a = w.Add(Spec("A", 0, MeleeScenarios.Sword, new(0, 0)), true);
            var b = w.Add(Spec("B", 1, MeleeScenarios.Sword, new(1.5, 0)), true);
            a.Cmd.Start = new AttackRequest { PartId = "upper_body", TargetId = 1 };
            b.Cmd.Start = new AttackRequest { PartId = "upper_body", TargetId = 0 };
            while (a.State != Act.Strike) { w.Step(); if (a.State == Act.Windup) { a.Pain = a.KnockoutPain - 0.2; b.Pain = b.KnockoutPain - 0.2; } }
            int t = w.Tick;
            Assert.Equal(Act.Strike, b.State); // identical timings: both blows land on the same tick

            var onTick = w.Events.Where(e => e.Tick == t && Array.IndexOf(Resolutions, e.Kind) >= 0).ToList();
            bool aResolved = onTick.Any(e => (e.Actor == 0 && e.Target == 1 && e.Kind is EventKind.Wound or EventKind.Whiff)
                                          || (e.Actor == 1 && e.Target == 0 && e.Kind is EventKind.Blocked or EventKind.Parried or EventKind.Dodged));
            bool bResolved = onTick.Any(e => (e.Actor == 1 && e.Target == 0 && e.Kind is EventKind.Wound or EventKind.Whiff)
                                          || (e.Actor == 0 && e.Target == 1 && e.Kind is EventKind.Blocked or EventKind.Parried or EventKind.Dodged));
            Assert.True(aResolved && bResolved, $"seed {seed}: both blows of tick {t} must resolve (a={aResolved}, b={bResolved})");
            if (a.State == Act.Down && b.State == Act.Down) mutual++;
        }
        Assert.True(mutual > 0, "at least one seed must show a mutual knockdown");
    }

    [Fact]
    public void SimultaneousBlows_IdOrderGivesNoAdvantage()
    {
        int aDown = 0, bDown = 0;
        for (ulong seed = 1; seed <= 120; seed++)
        {
            var w = new MeleeWorld(f.Tables, seed);
            var a = w.Add(Spec("A", 0, MeleeScenarios.Sword, new(0, 0)), true);
            var b = w.Add(Spec("B", 1, MeleeScenarios.Sword, new(1.5, 0)), true);
            a.Cmd.Start = new AttackRequest { PartId = "upper_body", TargetId = 1 };
            b.Cmd.Start = new AttackRequest { PartId = "upper_body", TargetId = 0 };
            while (a.State != Act.Strike) { w.Step(); if (a.State == Act.Windup) { a.Pain = a.KnockoutPain - 0.2; b.Pain = b.KnockoutPain - 0.2; } }
            if (a.State == Act.Down) aDown++;
            if (b.State == Act.Down) bDown++;
        }
        // before the fix fighter 0 could never be knocked out by a blow that fighter 1 threw in the same tick
        Assert.True(aDown > 0 && bDown > 0);
        Assert.InRange(aDown - bDown, -aDown / 2 - 5, aDown / 2 + 5);
    }

    [Theory]
    [InlineData(Act.Surrendered)]
    [InlineData(Act.Fled)]
    public void BleedingOut_DoesNotOverwriteFledOrSurrendered(Act outcome)
    {
        var w = new MeleeWorld(f.Tables, 1);
        var a = w.Add(Spec("A", 0, MeleeScenarios.Sword, new(0, 0)), true);
        var b = w.Add(Spec("B", 1, MeleeScenarios.Sword, new(30, 0)), true);
        b.State = outcome;
        b.Blood = b.MaxBlood * (1 - (MeleeRules.ShockCollapse + 0.02)); // collapsed range, not yet dead
        for (int i = 0; i < 5; i++) w.Step();
        Assert.Equal(outcome, b.State);
        Assert.Equal(outcome == Act.Fled ? Outcome.Rout : Outcome.Surrender, w.OutcomeOf(b));

        b.Blood = b.MaxBlood * (1 - (MeleeRules.ShockDead + 0.02)); // but they can still die
        w.Step();
        Assert.Equal(Act.Dead, b.State);
    }

    [Fact]
    public void PickAttack_ChoosesBySituation()
    {
        var mail = MeleeScenarios.Mailed;
        var w = new MeleeWorld(f.Tables, 1);
        var sword = w.Add(Spec("S", 0, MeleeScenarios.Sword, new(0, 0), skill: 40), true);
        var armoured = w.Add(Spec("M", 1, MeleeScenarios.Sword, new(1.5, 0), mail), true);
        var bare = w.Add(Spec("U", 1, MeleeScenarios.Sword, new(3, 0), MeleeScenarios.Unarmoured), true);
        var spear = w.Add(Spec("P", 0, MeleeScenarios.Spear, new(0, 5)), true);

        Assert.Equal("pommel", MeleeAi.PickAttack(w, sword, armoured, "upper_body"));   // hard armour: blunt first
        Assert.Equal("slash", MeleeAi.PickAttack(w, sword, bare, "upper_body"));        // unarmoured: edge first
        Assert.Equal("butt", MeleeAi.PickAttack(w, spear, armoured, "head"));
        Assert.Equal("thrust", MeleeAi.PickAttack(w, spear, bare, "upper_body"));       // no edge: point beats blunt
        Assert.Equal(MeleeAi.PickAttack(w, sword, armoured, "upper_body"), MeleeAi.PickAttack(w, sword, armoured, "upper_body"));
    }
}
