using Ravensguard.Content;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;
using static Ravensguard.Sim.Combat.Abstract.AbstractScenarios;
using W = Ravensguard.Sim.Combat.Abstract.AbstractWarScenarios;

namespace Ravensguard.Sim.Tests.Combat.Abstract;

/// <summary>Phase 7 AW-A: abstract battle v2's geometry, passes, prisoners and loot (abstract-war-and-calibration 10: AW-1 to AW-3, AW-13).</summary>
[Collection("fight")]
public class AbstractWarTests(FightFixture fx, ITestOutputHelper output)
{
    private WoundTables T => WoundTables.For(fx.Db) ?? throw new InvalidOperationException("no wound tables");
    private AbstractBattleDef V2 => AbstractBattle.V2Def(fx.Db);

    [Fact]
    public void V2Row_IsInTheContent_AndV1IsTheDefault()
    {
        Assert.Equal(2, V2.Version);
        var b = new AbstractBattle(fx.C, T, AbstractEquivalence.InputOf(Scenario.A, 1));
        Assert.False(b.IsV2);
        Assert.Equal(1, b.K.Version);
    }

    [Fact]
    public void AW1_V2WithItsGeometryTermsIdle_HashesAsV1WhereThereIsNoGeometry()
    {
        // equal lines, no horse: the wrap, the reserves and the passes have nothing to do; the two block terms are zeroed (they are v2's own morale)
        var idle = V2 with { BrokenX100 = 0, WallX100 = 0, WrapRatePm = 1000, WrapFlankPm = 1000 };
        foreach (var s in new[] { Scenario.A, Scenario.ANoLeader })
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var input = AbstractEquivalence.InputOf(s, 5000 + seed);
                var v1 = new AbstractBattle(fx.C, T, input); v1.Run();
                var v2 = new AbstractBattle(fx.C, T, input, idle); v2.Run();
                Assert.True(v2.IsV2);
                Assert.Equal(v1.CoreHash(), v2.CoreHash());
                Assert.Equal(v1.Report.Winner, v2.Report.Winner);
                Assert.Equal(v1.Report.DurationS, v2.Report.DurationS);
            }
    }

    [Fact]
    public void AW1_V1IsUntouched_ByTheV2Code()
    {
        // the default def is v1: the same battle twice, and the v2 hooks never ran
        var a = new AbstractBattle(fx.C, T, AbstractEquivalence.InputOf(Scenario.B, 9)); a.Run();
        var b = new AbstractBattle(fx.C, T, AbstractEquivalence.InputOf(Scenario.B, 9)); b.Run();
        Assert.Equal(a.Hash(), b.Hash());
        Assert.Empty(a.Blocks);
        Assert.Empty(a.Report.Prisoners);
        Assert.Empty(a.Report.Loot);
    }

    [Fact]
    public void AW2_TheWiderLine_WrapsTheNarrowerOnesFlank()
    {
        int wraps = 0, flankedSeen = 0, flankHits = 0, otherHits = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var b = new AbstractBattle(fx.C, T, W.InputOf(W.Scenario.E, 7000 + seed), V2);
            // before the first step: the overhang is on the wider line (side 1), none on the narrower
            Assert.Equal(0, b.Side[0].Count(x => x.Wrap));
            wraps += b.Side[1].Count(x => x.Wrap);
            Assert.All(b.Side[1].Where(x => x.Wrap), x => Assert.Equal(BattlePosition.Flank, x.Position));
            var foot0 = b.Blocks.Single(x => x.Side == 0); var foot1 = b.Blocks.Single(x => x.Side == 1);
            Assert.True(foot1.Frontage > foot0.Frontage * 13 / 10);                        // the wider line is about 40% wider
            while (!b.Over && b.Step < 40)
            {
                b.StepOnce();
                if (b.Side[0].Any(x => x.Flanked)) flankedSeen++;
                foreach (var x in b.Side[0]) { if (x.Flanked) flankHits += x.HitsThisStep; else if (x.Row == 0) otherHits += x.HitsThisStep; }
            }
        }
        output.WriteLine($"E: wrappers {wraps} flanked steps {flankedSeen} flank-member hits {flankHits} other front hits {otherHits}");
        Assert.True(wraps >= 12 * 20, "the overhang of 16 files a side is a wrap");
        Assert.True(flankedSeen > 0, "the wrapped end carries the flank term");
        Assert.True(flankHits > 0, "the wrappers' blows land on the flank members");
    }

    [Fact]
    public void AW2_TheFlankTerm_IsLocal_NotTheWholeSide()
    {
        var b = new AbstractBattle(fx.C, T, W.InputOf(W.Scenario.E, 7001), V2);
        b.StepOnce();
        int flanked = b.Side[0].Count(x => x.Flanked), all = b.Side[0].Count;
        Assert.InRange(flanked, 1, all / 3);                                             // the ends, not the whole line (v1 carried the term on every man)
    }

    [Fact]
    public void AW3_TheCharge_IsAPass_WithTrampleAndShock_AgainstUnbracedFoot()
    {
        var tot = new PassTally();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var b = new AbstractBattle(fx.C, T, W.InputOf(W.Scenario.F, 8000 + seed), V2);
            b.Run();
            tot.Passes += b.Passes.Passes; tot.Refusals += b.Passes.Refusals; tot.Tramples += b.Passes.Tramples; tot.TrampleHits += b.Passes.TrampleHits; tot.Shocks += b.Passes.Shocks; tot.Falls += b.Passes.Falls;
        }
        output.WriteLine($"F: passes {tot.Passes} refusals {tot.Refusals} tramples {tot.Tramples} hits {tot.TrampleHits} shocks {tot.Shocks} falls {tot.Falls}");
        Assert.True(tot.Passes > 20 * 40, "most of the 60 charge home");
        Assert.Equal(0, tot.Refusals);                                                   // no braced polearm in the path: no refusal roll
        Assert.True(tot.TrampleHits > 0 && tot.Tramples >= tot.TrampleHits);
        Assert.Equal(20, tot.Shocks);                                                    // ten riders or more home against an unbraced front: shock, once a battle
    }

    [Fact]
    public void AW3_TheBracedSchiltron_RefusesAndUnhorses_AtTheLiveRows_AndTakesNoShock()
    {
        var tot = new PassTally();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var b = new AbstractBattle(fx.C, T, W.InputOf(W.Scenario.G, 9000 + seed), V2);
            b.Run();
            tot.Rolls += b.Passes.Rolls; tot.ExpectedRefusalPm += b.Passes.ExpectedRefusalPm; tot.Refusals += b.Passes.Refusals; tot.BraceDowns += b.Passes.BraceDowns;
            tot.Falls += b.Passes.Falls; tot.Shocks += b.Passes.Shocks; tot.Passes += b.Passes.Passes; tot.Threw += b.Passes.Threw;
        }
        double live = tot.ExpectedRefusalPm / (double)tot.Rolls / 1000.0, got = tot.Refusals / (double)tot.Rolls;       // the live refusal rows (ChargeMath.RefusalPm) against the draws
        output.WriteLine($"G: rolls {tot.Rolls} refusal {got:P1} (live rows {live:P1}) brace-downs {tot.BraceDowns} falls {tot.Falls} threw {tot.Threw} shocks {tot.Shocks} home {tot.Passes}");
        Assert.True(tot.Rolls > 40 * 50);
        Assert.InRange(got, live * 0.9, live * 1.1);
        Assert.True(tot.BraceDowns > 0 && tot.Falls >= tot.BraceDowns + tot.Threw);
        Assert.Equal(0, tot.Shocks);                                                     // the braced block is not shocked
    }

    [Fact]
    public void AW13_TheYielded_GoToCaptivityWithRankClasses_AndTheKitStaysAsLoot()
    {
        int prisoners = 0, loot = 0, ransom = 0, ranked = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var b = new AbstractBattle(fx.C, T, W.InputOf(W.Scenario.E, 6000 + seed), V2);
            var rep = b.Run();
            if (rep.Winner < 0) continue;
            int holder = b.Team[rep.Winner];
            foreach (var p in rep.Prisoners)
            {
                Assert.Equal(1 - rep.Winner, p.Side);                                    // only the loser's men are taken
                Assert.Equal(holder, p.ByTeam);
                Assert.Equal(SoldierStatus.Captured, b.Soldiers.Single(x => x.Person == p.Person).Status);
                if (p.Rank != RankClass.Common) ranked++;
            }
            prisoners += rep.Prisoners.Count;
            Assert.Equal(rep.Sides[0].Dead + rep.Sides[1].Dead + rep.Sides[0].Captured + rep.Sides[1].Captured, rep.Loot.Count);       // every dead and every taken man's kit is a row
            Assert.All(rep.Loot, l => { Assert.Equal(holder, l.ByTeam); Assert.NotEmpty(l.Worn); });
            loot += rep.Loot.Count;
            ransom += b.Commands.Count(c => c.Kind == BattleCommandKind.Ransom);
            Assert.All(b.Commands.Where(c => c.Kind == BattleCommandKind.Ransom), c => Assert.True(c.Rank >= (int)RankClass.Veteran));
            Assert.True(rep.Prisoners.Count == b.Commands.Count(c => c.Kind == BattleCommandKind.Status && c.Status == SoldierStatus.Captured));
        }
        // the knights of G are mailed: those taken are veterans, and a veteran's taking is a ransom command
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var b = new AbstractBattle(fx.C, T, W.InputOf(W.Scenario.G, 6500 + seed), V2);
            var rep = b.Run();
            ranked += rep.Prisoners.Count(p => p.Rank == RankClass.Veteran);
            ransom += b.Commands.Count(c => c.Kind == BattleCommandKind.Ransom);
            Assert.All(rep.Prisoners.Where(p => p.Side == 0), p => Assert.NotEqual(RankClass.Common, p.Rank));
        }
        output.WriteLine($"E+G: prisoners {prisoners} (veterans {ranked}) loot rows {loot} ransoms {ransom}");
        Assert.True(prisoners > 0 && loot > 0 && ransom > 0);      // (the fitted knights are taken in so few fights that the veterans are the captains)
    }


    /// <summary>The comparisons of the gate (abstract-war 4.5) that v2 does not reach yet, by scenario and metric (numbers stripped): a gap that opens again, or a new one, fails by name; one closed is welcome.</summary>
    private static readonly HashSet<string> KnownGaps = new(StringComparer.Ordinal)
    {
        "E: side 0 casualties",
        "E: side 1 casualties",
        "E: win rate",
        "E: duration",
        "E: wound mix [2]",
        "E: wound mix [3]",
        "F: wound mix [1]",
        "F: wound mix [3]",
        "G: duration",
        "G: wound mix [1]",
        "G: wound mix [3]",
    };

    private static string Key(string failure) => System.Text.RegularExpressions.Regex.Replace(failure.Split(" vs ")[0], @"\s[\d.]+( s|%)?$", "");

    [Fact]
    public void AW4_TheGate_V2AgainstTheLiveFights_EachMetricByName()
    {
        var fails = new List<string>();
        foreach (var s in Enum.GetValues<W.Scenario>())
        {
            var tac = AbstractEquivalence.Unpack(TacticalReference.WarTallies[s]);
            var abs = AbstractEquivalence.RunAbstractOf(fx.C, T, x => W.InputOf(s, x), TacticalReference.Seed0, TacticalReference.Seeds, V2);
            output.WriteLine(AbstractEquivalence.Row($"{s} tactical", tac)); output.WriteLine(AbstractEquivalence.Row($"{s} v2", abs));
            fails.AddRange(AbstractEquivalence.Compare(tac, abs, s.ToString()));
        }
        foreach (var f in fails) output.WriteLine("gap: " + f);
        var fresh = fails.Where(f => !KnownGaps.Contains(Key(f))).ToList();
        Assert.True(fresh.Count == 0, "gaps that are not known:\n" + string.Join("\n", fresh));
        // F is inside the gate on casualties, win rate and duration; G on casualties and win rate (its duration and wound mix are gaps)
        Assert.DoesNotContain(fails, f => f.StartsWith("F: side") || f.StartsWith("F: win") || f.StartsWith("F: duration") || f.StartsWith("G: side") || f.StartsWith("G: win rate"));
    }

    [Fact]
    public void AW4_TheGate_V2OnThePhase4Scenarios_IsNoWorseThanV1Was()
    {
        int gaps = 0;
        foreach (var s in AbstractScenarios.Gate)
        {
            var abs = AbstractEquivalence.RunAbstract(fx.C, T, s, TacticalReference.Seed0, TacticalReference.Seeds, V2);
            var f = AbstractEquivalence.Compare(TacticalReference.Of(s), abs, s.ToString());
            foreach (var x in f) output.WriteLine("gap: " + x);
            gaps += f.Count;
        }
        Assert.True(gaps <= Phase4V2Gaps, $"{gaps} gaps (v2 fits {Phase4V2Gaps} of the five Phase 4 scenarios' 45 comparisons)");
    }

    private const int Phase4V2Gaps = 18;

    [Fact, Trait("Category", "Soak")]
    public void AW4_Soak_TheStoredLiveFightsOfEFG_AreTheLiveFightsOfToday()
    {
        foreach (var s in Enum.GetValues<W.Scenario>())
            Assert.Equal(TacticalReference.WarTallies[s], AbstractEquivalence.Pack(AbstractEquivalence.RunTacticalOf(x => W.Tactical(fx.C, s, x), TacticalReference.Seed0, TacticalReference.Seeds)));
    }

    [Fact]
    public void V2_IsDeterministic()
    {
        var a = new AbstractBattle(fx.C, T, W.InputOf(W.Scenario.F, 3), V2); a.Run();
        var b = new AbstractBattle(fx.C, T, W.InputOf(W.Scenario.F, 3), V2); b.Run();
        Assert.Equal(a.Report.Hash, b.Report.Hash);
        var c = new AbstractBattle(fx.C, T, W.InputOf(W.Scenario.F, 4), V2); c.Run();
        Assert.NotEqual(a.Report.Hash, c.Report.Hash);
    }
}
