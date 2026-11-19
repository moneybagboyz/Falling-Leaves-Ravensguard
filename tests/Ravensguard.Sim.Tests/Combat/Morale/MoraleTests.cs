using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Morale;

/// <summary>Morale, rout and surrender in the live fight (morale-capture-succession 4.1 to 4.3; gates MC-1 to MC-6).</summary>
[Collection("fight")]
public class MoraleTests(FightFixture fx, ITestOutputHelper output)
{
    private FightWorld World(ulong seed = 1) => new(fx.C, seed, 1, 8 * 3600) { RecordEvents = true };

    private FighterKit Kit(string name, int team, int x, int y = 0, int courage = 70, string plan = "human", WeaponInstance? weapon = null) =>
        fx.Kit(name, team, x, y) with { Courage = courage, Plan = plan, Weapon = weapon ?? FightFixture.Sword };

    // ---------------------------------------------------------------- MC-1

    [Fact]
    public void MC1_AFriendFallingWithin10m_Costs6_DecayingToNothingIn15s_AndNothingBeyond10m()
    {
        var w = World();
        var a = w.Add(Kit("A", 0, 0));
        var b = w.Add(Kit("B", 0, 0, 5000));
        var far = w.Add(Kit("Far", 0, 0, 15_000));
        w.Add(Kit("Foe", 1, 100_000));
        for (int i = 0; i < 20; i++) w.Step();
        Assert.Equal(0, a.M.PenaltyX100);
        b.Book.State = Consciousness.Dead;                                  // B falls (dies) 5 m from A, 15 m from Far
        int fell = -1, peak = 0, zeroAt = -1;
        for (int i = 0; i < 20 * 20; i++)
        {
            w.Step();
            if (fell < 0 && a.M.PenaltyX100 > 0) { fell = w.Tick; peak = a.M.PenaltyX100; }
            if (fell >= 0 && zeroAt < 0 && a.M.PenaltyX100 == 0) zeroAt = w.Tick;
        }
        Assert.Equal(FAct.Dead, b.Act);
        Assert.Equal(600, peak);                                            // -6 points
        Assert.Equal(15.0, (zeroAt - fell) / 20.0);                         // 0.4 a second: gone in 15.0 s
        Assert.Equal(0, far.M.PenaltyX100);                                 // beyond 10 m it costs nothing
    }

    // ---------------------------------------------------------------- MC-2

    [Fact]
    public void MC2_TheThresholds_TermByTerm()
    {
        var R = MoraleRules.For(fx.Db);
        // courage 70, flanked (-15), two friends down (-12) = 43: steady
        var t = new MoraleTerms(7000, 1200, 0, 1500, 0, 0, 0, 0, 0, 0, 0);
        Assert.Equal(4300, R.Value(t));
        Assert.Equal(MoraleMood.Steady, R.MoodOf(R.Value(t), recentLoss: true));
        // plus 5 blows (-10) = 33: wavering
        t = t with { PenaltyX100 = 1200 + 5 * R.D.HitTakenX100 };
        Assert.Equal(3300, R.Value(t));
        Assert.Equal(MoraleMood.Wavering, R.MoodOf(R.Value(t), true));
        // plus pain 30 (-10.5) = 22.5: still wavering
        t = t with { PainPct = 30 };
        Assert.Equal(2250, R.Value(t));
        Assert.Equal(MoraleMood.Wavering, R.MoodOf(R.Value(t), true));
        // plus a routing neighbour for 2 s (-4: four 2 Hz updates of 1 point) = 18.5: rout
        t = t with { RoutingX100 = 4 * R.RoutingPerNeighbourPerUpdate };
        Assert.Equal(1850, R.Value(t));
        Assert.Equal(MoraleMood.Routing, R.MoodOf(R.Value(t), true));
        // and live: one routing neighbour for 2 s adds 4 points to a fighter's routing term
        var w = World();
        var a = w.Add(Kit("A", 0, 0)); var n = w.Add(Kit("N", 0, 0, 3000)); w.Add(Kit("Foe", 1, 100_000));
        w.Step();
        n.Routing = true; n.M.Mood = MoraleMood.Routing;
        for (int i = 0; i < 40; i++) w.Step();
        Assert.Equal(400, a.M.RoutAccX100);
    }

    // ---------------------------------------------------------------- MC-3

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MC3_ARoutedFighterRallies_WithACaptainWithin20mFor4s_AndNotWithout(bool captain)
    {
        var w = World();
        var f = w.Add(Kit("F", 0, 0));
        var c = w.Add(Kit("Captain", 0, 0, 5000));
        w.Add(Kit("Foe", 1, 15_000));                                       // in sight (no escape), out of the 12 m outnumbered ring
        if (captain) w.SetCaptain(c.Id);
        w.Step();
        f.Routing = true; f.M.Mood = MoraleMood.Routing;
        int rallied = -1;
        for (int i = 0; i < 20 * 10 && rallied < 0; i++) { w.Step(); if (!f.Routing) rallied = w.Tick; }
        if (captain)
        {
            Assert.True(rallied > 0);
            Assert.InRange(rallied - 1, 4 * 20, 4 * 20 + 10);               // 4 s of a captain within 20 m (at 2 Hz)
            Assert.Contains(w.Events, e => e.Kind == FightEventKind.Rally && e.Actor == f.Id);
            Assert.True(f.M.ImmuneUntil > w.Tick);                          // the routing-neighbour term is ignored for 10 s
        }
        else Assert.True(f.Routing);
    }

    // ---------------------------------------------------------------- MC-4

    [Fact]
    public void MC4_ACorneredFighterSurrendersOnlyBelow10_AFreeFleePathMeansRout()
    {
        // enemies 2 m ahead and 2 m behind: cornered; morale 5: surrender (bandits accept)
        var w = World();
        var f = w.Add(Kit("F", 0, 0, courage: 5));
        w.Add(Kit("Mate", 0, -30_000));
        w.Add(Kit("Front", 1, 2000)); w.Add(Kit("Back", 1, -2000) with { FaceX = FightMath.One });
        w.SetSide(1, SideKind.Bandits);
        for (int i = 0; i < 12; i++) w.Step();
        Assert.True(w.Cornered(f));
        Assert.Equal(FAct.Surrendered, f.Act);
        Assert.Contains(w.Events, e => e.Kind == FightEventKind.Surrender && e.Actor == f.Id && e.A == 0);

        // one enemy ahead, the side's rally point behind: a free flee path, so the same morale routs instead
        var w2 = World();
        var g = w2.Add(Kit("G", 0, 0, courage: 5));
        w2.Add(Kit("Mate", 0, -30_000));
        w2.Add(Kit("Front", 1, 2000));
        w2.SetSide(1, SideKind.Bandits);
        for (int i = 0; i < 12; i++) w2.Step();
        Assert.False(w2.Cornered(g));
        Assert.True(g.Routing);
        Assert.NotEqual(FAct.Surrendered, g.Act);

        // cornered but at 15 (not below 10): a rout, not a surrender
        var w3 = World();
        var h = w3.Add(Kit("H", 0, 0, courage: 40));                       // 40 - 10 outnumbered - 15 flanked = 15
        w3.Add(Kit("Mate", 0, -30_000));
        w3.Add(Kit("Front", 1, 2000)); w3.Add(Kit("Back", 1, -2000) with { FaceX = FightMath.One });
        w3.SetSide(1, SideKind.Bandits);
        for (int i = 0; i < 12; i++) w3.Step();
        Assert.True(h.Routing);
        Assert.NotEqual(FAct.Surrendered, h.Act);

        // beasts never accept: a cornered fighter facing wolves fights on (refused), and asks again no sooner than 10 s later
        var w4 = World();
        var k = w4.Add(Kit("K", 0, 0, courage: 5));
        w4.Add(Kit("Mate", 0, -30_000));
        w4.Add(Kit("Front", 1, 2000)); w4.Add(Kit("Back", 1, -2000) with { FaceX = FightMath.One });
        w4.SetSide(1, SideKind.Beasts);
        for (int i = 0; i < 12; i++) w4.Step();
        Assert.NotEqual(FAct.Surrendered, k.Act);
        Assert.Contains(w4.Events, e => e.Kind == FightEventKind.Surrender && e.Actor == k.Id && e.A == 1);
    }

    // ---------------------------------------------------------------- MC-5

    [Fact]
    public void MC5_AWolfFleesByBloodLoss_ACaveBeastSooner_ADraugrNever()
    {
        var R = MoraleRules.For(fx.Db);
        // the spec's 40% and 30% are the v2 wound model's unconscious and collapse bands (WB: 400, 300), never reached awake; the content flees them at 25% and 20%
        Assert.True(R.BeastFleeBloodPm("quadruped") < MedicineTables.UnconsciousPm && R.BeastFleeBloodPm("quadruped") < MedicineTables.CollapsePm);
        Assert.True(R.BeastFleeBloodPm("cave_beast") < R.BeastFleeBloodPm("quadruped"));
        Assert.Equal(int.MaxValue, R.BeastFleeBloodPm("draugr"));
        Assert.Equal(-1, R.BeastFleeBloodPm("human"));
        Assert.Equal(0, R.AcceptPm(SideKind.Beasts)); Assert.Equal(0, R.AcceptPm(SideKind.Draugr));

        Fighter Bled(string plan, int lossPm, int courage = 0)
        {
            var w = World();
            var b = w.Add(Kit("Beast", 1, 0, plan: plan, courage: courage, weapon: plan == "quadruped" ? new WeaponInstance("natural_wolf", "bone") : null));
            w.Add(Kit("Hunter", 0, -2500)); w.Add(Kit("Hunter2", 0, 2500) with { FaceX = -FightMath.One });
            w.Step();
            b.Book.BloodUl = b.Book.BloodMaxUl - b.Book.BloodMaxUl * lossPm / 1000;
            for (int i = 0; i < 12; i++) w.Step();
            return b;
        }
        int wolf = R.BeastFleeBloodPm("quadruped"), cave = R.BeastFleeBloodPm("cave_beast");
        Assert.True(Bled("quadruped", wolf + 10).Routing);
        Assert.False(Bled("quadruped", wolf - 50).Routing);
        Assert.True(Bled("cave_beast", cave + 10).Routing);
        Assert.False(Bled("cave_beast", cave - 50).Routing);
        var d = Bled("draugr", 450, courage: 1);                          // a draugr at no morale, half its blood gone, cornered: it never routs nor yields
        Assert.False(d.Routing);
        Assert.NotEqual(FAct.Surrendered, d.Act);
    }

    // ---------------------------------------------------------------- MC-6

    [Fact]
    public void MC6_TenVersusTen_AllFinish_DurationDeathsDownsAndRoutsInTheBaselineRanges()
    {
        double secs = 0, deaths = 0, downs = 0, routs = 0;
        const int Seeds = 20;
        for (ulong s = 0; s < Seeds; s++)
        {
            var w = FightCalibration.Skirmish(fx.C, 500 + s, 10);
            var tally = MoraleScenarios.Run(w, 180);
            Assert.True(tally.Finished, $"seed {500 + s} did not finish in 180 s");
            secs += tally.Seconds; downs += tally.Downs; routs += tally.Routs;
            for (int k = 0; k < 120 * 20; k++) w.Step();                     // two minutes on, nobody treating: the downed who bleed out are the fight's dead
            deaths += w.Fighters.Count(f => f.Act == FAct.Dead);
        }
        output.WriteLine($"mean {secs / Seeds:0.0} s, deaths {deaths / Seeds:0.0}, downs {downs / Seeds:0.0}, routs {routs / Seeds:0.0}");
        Assert.InRange(secs / Seeds, 25 - 8, 25 + 8);
        Assert.InRange(deaths / Seeds, 4, 8);
        Assert.InRange(downs / Seeds, 3, 6);
        Assert.InRange(routs / Seeds, 8, 16);
    }

    // ---------------------------------------------------------------- the hero, determinism, saving

    [Fact]
    public void TheHeroNeverRoutsByMorale_ButFleesWhenItRunsClear_AndCanYield()
    {
        var w = World();
        var hero = w.Add(Kit("You", 0, 0, courage: 1), player: true);
        w.Add(Kit("Foe", 1, 2000)); w.Add(Kit("Foe2", 1, -2000) with { FaceX = FightMath.One });
        w.SetSide(1, SideKind.Army);
        for (int i = 0; i < 40; i++) w.Step();
        Assert.False(hero.Routing);
        Assert.True(hero.Active);
        // yield: armies accept 90%; across seeds most are accepted, some refused
        int accepted = 0;
        for (ulong s = 1; s <= 200; s++)
        {
            var y = World(s);
            var h = y.Add(Kit("You", 0, 0), player: true); y.Add(Kit("Foe", 1, 2000)); y.SetSide(1, SideKind.Army);
            y.Step();
            y.Input(h.Id, new FightInput(FightInputKind.Yield));
            if (h.Act == FAct.Surrendered) accepted++;
        }
        Assert.InRange(accepted, 160, 196);
        // running 20 m clear for 5 s is fleeing
        var r = World();
        var you = r.Add(Kit("You", 0, 0), player: true); r.Add(Kit("Foe", 1, 30_000));
        for (int i = 0; i < 20 * 7; i++) r.Step();
        Assert.True(you.Active);                                            // standing off at 30 m is not fleeing
        you.Cmd.MoveX = -FightMath.One; you.Cmd.Sprint = true;              // running away is
        for (int i = 0; i < 20 * 14 && you.Active; i++) r.Step();
        Assert.Equal(FAct.Fled, you.Act);
    }

    [Fact]
    public void MoraleIsInTheFightRecord_SaveLoadSaveIsByteIdentical_AndResumesExactly()
    {
        var w = FightCalibration.Skirmish(fx.C, 77, 6);
        w.AppointCaptains();
        for (int i = 0; i < 240; i++) w.StepWithAi();
        Assert.Contains(w.Fighters, f => f.M.PenaltyX100 > 0 || f.Routing);
        var bytes = FightRecord.Capture(w, "test").ToBytes();
        var back = FightRecord.FromBytes(bytes).Restore(fx.C);
        Assert.Equal(bytes, FightRecord.Capture(back, "test").ToBytes());
        Assert.Equal(w.MoraleHash(), back.MoraleHash());
        for (int i = 0; i < 600; i++) { w.StepWithAi(); back.StepWithAi(); }
        Assert.Equal(w.Hash(), back.Hash());
        Assert.Equal(w.MoraleHash(), back.MoraleHash());
    }

    [Fact]
    public void AVersion6FightRecord_MigratesToVersion7_AndResumesWithFreshMorale()
    {
        var w = FightCalibration.Skirmish(fx.C, 5, 3);
        for (int i = 0; i < 60; i++) w.StepWithAi();
        var rec = FightRecord.Capture(w, "test"); rec.Morale = null;                 // what a v6 save holds
        var file = new SaveFile(new SaveHeader { FormatVersion = 6 }, [new(FightRecord.SectionName, SaveCodec.SerializeCompressed(rec))]);
        var migrated = SaveMigrator.CreateDefault().Migrate(file);
        Assert.Equal(7, migrated.Header.FormatVersion);
        var back = SaveCodec.DeserializeCompressed<FightRecord>(migrated.Sections[FightRecord.SectionName].ToArray());
        Assert.NotNull(back.Morale);
        var fw = back.Restore(fx.C);
        for (int i = 0; i < 40; i++) fw.StepWithAi();
        Assert.All(fw.Fighters, f => Assert.True(f.M.Has(MoraleFlags.Inited)));
    }

    [Fact]
    public void AWalkAway_EndsTheFight_AndTheDecidedWinnerIsTheSideWithMoreFallenFoes()
    {
        var w = World();
        w.Add(Kit("A", 0, 0)); w.Add(Kit("B", 1, 60_000));
        for (int i = 0; i < 20 * 16; i++) w.Step();
        Assert.True(w.Disengaged);
        Assert.True(w.IsOver);
        Assert.Equal(-1, w.DecidedWinner);
    }
}
