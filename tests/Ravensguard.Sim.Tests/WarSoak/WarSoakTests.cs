using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Tests.Combat.Battle;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.EconomyAudit;
using Ravensguard.Sim.WorldGen;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.WarSoakTests;

/// <summary>P7-AW-E gate: AW-14 (conversion at the bubble edge), AW-15 (the war soak, CI part: seed 1234, 2 game years) and AW-16 (the nightly: 10 worlds x 25 years, every gate of abstract-war 4.6).</summary>
[Collection("fight")]
public class WarSoakTests(FightFixture fx, ITestOutputHelper output)
{
    // ---------------------------------------------------------------- AW-14

    [Fact]
    public void AW14_AFightWhoseLastFormationsLeaveTheBubble_IsConvertedAndFinishedAbstractly_NobodyLostOrMadeTwice()
    {
        var chron = new List<(string Type, IReadOnlyList<Cause> Causes)>();
        BattleBubble Make()
        {
            var b = BattleBubble.Open(fx.C, new BubbleScenario(fx) { Persons = true }.Opening(60, 60, 200, unitSize: 30, gap: 40_000, seed: 77));
            b.Chronicle = (when, type, key, ps, subj, causes, data) => { chron.Add((type, causes)); return new EntityId((ulong)(900 + chron.Count)); };
            return b;
        }
        var live = Make();
        for (int t = 0; t < 20 * 14 && !live.Closed; t++) live.Step();                                   // 14 seconds: the lines have met and men have fallen
        Assert.False(live.Closed);
        var men = live.W.Fighters.Where(f => !live.W.IsHorse(f)).ToList();
        int dead0 = men.Count(f => f.Act == FAct.Dead), total = men.Count;
        var ids = men.Select(f => f.Person).ToList();
        // the observer is a long way off: every man is beyond the bubble's 150 m
        Assert.True(live.AllBeyondEdge(2_000_000, 2_000_000));
        Assert.False(live.AllBeyondEdge(0, 0));                                                           // an observer in the middle still sees them
        Assert.True(live.Demote());
        Assert.True(live.Closed); Assert.NotNull(live.Converted); Assert.NotNull(live.Result);
        var c = live.Converted!;
        // continuous: the same men, in the same order, the dead stay dead, nobody made or lost
        Assert.Equal(total, c.Soldiers);
        Assert.Equal(ids, live.W.Fighters.Where(f => !live.W.IsHorse(f)).Select(f => f.Person).ToList());
        Assert.Equal(dead0, c.DeadAtEdge); Assert.True(c.DeadAtEnd >= c.DeadAtEdge);
        Assert.Equal(c.Soldiers, c.StandingAtEnd + c.DownAtEnd + c.DeadAtEnd + c.FledAtEnd + c.YieldedAtEnd);
        var res = live.Result!; Assert.Equal(2, res.Sides.Count);
        Assert.Equal(total, res.Soldiers.Count - live.W.Fighters.Count(f => live.W.IsHorse(f)));
        Assert.Equal(c.DeadAtEnd, res.Sides.Sum(s => s.Dead)); Assert.Equal(c.StandingAtEnd, res.Sides.Sum(s => s.Standing));
        Assert.Single(chron, e => e.Type == "battle_fought");                                              // one entry, with the war's cause, as a fight that ended in the box
        Assert.Contains(chron.Single(e => e.Type == "battle_fought").Causes, k => k.Kind == CauseKind.War);
        output.WriteLine($"AW-14: {total} men; at the edge {c.StandingAtEdge} standing {c.DownAtEdge} down {c.DeadAtEdge} dead; at the end {c.StandingAtEnd} standing {c.DownAtEnd} down {c.DeadAtEnd} dead {c.FledAtEnd} fled {c.YieldedAtEnd} yielded in {c.Steps} steps");
        // the same fight converted at the same tick ends the same
        var again = Make(); for (int t = 0; t < 20 * 14 && !again.Closed; t++) again.Step();
        Assert.True(again.Demote()); Assert.Equal(c.BattleHash, again.Converted!.BattleHash);
    }

    [Fact]
    public void P7R2_MenKilledInTheAbstractHalf_AreDeadInTheLedger()
    {
        var ledger = new Ravensguard.Sim.Combat.Wounds.WoundLedger(77, 8 * 3600);
        var b = BattleBubble.Open(fx.C, new BubbleScenario(fx) { Persons = true }.Opening(60, 60, 200, unitSize: 30, gap: 40_000, seed: 77, ledger: ledger));
        for (int t = 0; t < 20 * 14 && !b.Closed; t++) b.StepShared();
        Assert.True(b.Demote());
        var c = b.Converted!;
        Assert.True(c.DeadAtEnd > c.DeadAtEdge, "the abstract half must kill someone for this test to bite");
        int checkedDead = 0;
        foreach (var f in b.W.Fighters.Where(f => !b.W.IsHorse(f) && f.Act == FAct.Dead))
        {
            Assert.True(ledger.Books.TryGetValue(f.Kit.Person, out var bk) && bk.Dead, $"person {f.Kit.Person} is dead in the fight but not in the ledger");
            checkedDead++;
        }
        Assert.Equal(c.DeadAtEnd, checkedDead);
    }

    // ---------------------------------------------------------------- AW-15

    [Fact]
    public void AW15_WarSoak_CiPart_Seed1234_2Years_CoinMassAndPeopleExact_TheTrailIsTheSameRunToRun()
    {
        string hash;
        {
            using var run = AuditWorld.Create();
            var soak = new WarSoak(run, 2) { WarYearFrom = 1 };
            var r = soak.Run(y => output.WriteLine($"y{y.Year}: alive {y.Alive} births {y.Births} deaths {y.Deaths} wars open {y.WarsOpen} declared {y.WarsDeclared} ended {y.WarsEnded} host-days {y.HostDays} battles {y.Battles} places taken {y.PlacesTaken} desertion waves {y.Desertion} worst hour {y.HourMsMax} ms"));
            var gates = WarSoakGates.Exact(r, TestBudget.Ms(WarSoakGates.WorstHourMs));
            foreach (var g in gates) output.WriteLine((g.Pass ? "PASS " : "FAIL ") + g.Name + ": " + g.Text);
            output.WriteLine($"wars declared {r.WarsDeclared}, pair-seasons at war {r.PairSeasonsAtWar} of {r.PairSeasons}, trail {r.TrailHash}");
            Assert.Equal(2, r.Rows.Count);
            Assert.True(r.WarsDeclared >= 1, "the realms went to war in the two years");
            Assert.All(gates, g => Assert.True(g.Pass, g.Name + ": " + g.Text));
            hash = r.TrailHash;
        }
        using var run2 = AuditWorld.Create();
        Assert.Equal(hash, new WarSoak(run2, 2) { WarYearFrom = 1 }.Run().TrailHash);
    }

    // ---------------------------------------------------------------- AW-16

    /// <summary>The nightly (<c>FL_NIGHTLY=1</c>): 10 worlds x 25 years (<c>FL_WARSOAK_WORLDS</c> and <c>FL_WARSOAK_YEARS</c> shorten it); each world must pass every gate of abstract-war 4.6, a failing gate named.</summary>
    [Fact, Trait("Category", "Soak")]
    public void AW16_WarSoak_Nightly_TenWorlds_25Years_EveryGateOfSection46()
    {
        if (Environment.GetEnvironmentVariable("FL_NIGHTLY") != "1") return;
        int worlds = int.TryParse(Environment.GetEnvironmentVariable("FL_WARSOAK_WORLDS"), out var w) ? w : 10;
        int years = int.TryParse(Environment.GetEnvironmentVariable("FL_WARSOAK_YEARS"), out var y) ? y : 25;
        var failed = new List<string>(); string dir = ContinentSettings.FindBaseContent()!;
        for (int i = 0; i < worlds; i++)
        {
            ulong seed = i == 0 ? 1234UL : (ulong)(1000 + i);
            using var run = i == 0 ? AuditWorld.Create() : WarLoadRun.Create(seed, dir);
            var soak = new WarSoak(run, years, i);
            var r = soak.Run(row => output.WriteLine($"world {i} y{row.Year}: alive {row.Alive} births {row.Births} deaths {row.Deaths} imm {row.Immigrants} em {row.Emigrants} wars open {row.WarsOpen} declared {row.WarsDeclared} ended {row.WarsEnded} host-days {row.HostDays} battles {row.Battles} taken {row.PlacesTaken} desertion {row.Desertion} hour {row.HourMsMax} ms"));
            var gates = WarSoakGates.All(r, TestBudget.Ms(WarSoakGates.WorstHourMs));
            output.WriteLine(WarSoakGates.Format(r, gates));
            foreach (var e in r.Ends) output.WriteLine($"world {i} war {e.Serial}: realms {e.Attacker} v {e.Defender}, days {e.StartDay}-{e.EndDay}, {e.Seasons} seasons, ended by {e.Cause}, traced {e.Traced}");
            foreach (var g in gates.Where(g => !g.Pass)) failed.Add($"world {i} seed {seed}: {g.Name}: {g.Text}");
        }
        Assert.True(failed.Count == 0, string.Join("\n", failed));
    }
}
