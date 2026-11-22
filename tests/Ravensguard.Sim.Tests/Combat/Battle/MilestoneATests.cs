using System.Diagnostics;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Battle;

/// <summary>
/// Milestone M-A, "the field" (overview 3, P7-BB-C): a battle of formations, 1,000 a side, commanded through the command path (the hero's recorded inputs, the overlay's draft sent on resume at the paused
/// tick) from the opening to its close in the bubble. The Godot side of the milestone (F1 to F4, R, the pause, the crowd layer) is the smoke, the self-test and <c>-- --fight-demo battle</c>.
/// </summary>
[Collection(TimingCollection.Name)]
public class MilestoneATests(FightFixture fx, ITestOutputHelper output) : IClassFixture<FightFixture>
{
    [Fact]
    public void MA_ABattleOfThousandAndThousand_IsCommandedFromTheHeroToItsEnd_WithinTheTickBudget()
    {
        var sc = new BubbleScenario(fx);
        var sink = new List<(string Type, IReadOnlyList<Cause> Causes)>();
        ulong n = 600;
        var b = BattleBubble.Open(fx.C, sc.Opening(1000, 1000, 2000, unitSize: 100, gap: 140_000, hero: true, seed: 91));
        b.Chronicle = (when, type, key, ps, subj, causes, data) => { sink.Add((type, causes)); return new EntityId(++n); };
        Assert.Equal(2000, b.W.Fighters.Count);
        Assert.All(b.Sides, s => Assert.Equal(1000, s.LiveCap));
        var book = b.W.Formations!;
        var mine = book.Rows.Where(r => r.Side == 0).OrderBy(r => r.Id).ToList();
        Assert.Equal(10, mine.Count);
        var seen = new HashSet<string>();
        int EnemyRow(int k) => book.Rows.First(r => r.Side == 1 && r.Id == k).Index;
        void Send(FightInput i) => b.W.Input(b.Hero, i);
        var draft = new OrderDraft();
        var sw = new Stopwatch(); double worst = 0, sum = 0; int ticks = 0; var slow = new List<(double Ms, int Tick)>();
        for (int t = 0; t < 40_000 && !b.Closed; t++)
        {
            int tick = b.W.Tick;
            // the hero commands: advance all, a shield wall on the left, a charge, a flank placed in the paused overlay and sent on resume, the captain handed a formation
            if (tick == 0) foreach (var r in mine) Send(FormationCommands.Order(r.Id, OrderKind.Advance));
            if (tick == 200) Send(FormationCommands.Order(1, OrderKind.ShieldWall));
            if (tick == 300) Send(FormationCommands.At(2, OrderKind.Charge, EnemyRow(2)));
            if (tick == 100)
            {
                draft.Place(3, FormationCommands.At(3, OrderKind.FlankLeft, EnemyRow(3)));                    // placed in the paused overlay: nothing is sent yet
                draft.Place(6, FormationCommands.Order(6, OrderKind.Hold));
                Assert.Equal(0, mine[2].PendingCount);
                draft.Commit(Send);                                                                         // resume: sent at the paused tick
                Assert.Contains(mine[2].Pending.Take(mine[2].PendingCount), o => o.Kind == OrderKind.FlankLeft && o.IssuedTick == 100);
            }
            if (tick == 400) Send(FormationCommands.Captain(7, true));
            sw.Restart(); b.Step(); double ms = sw.Elapsed.TotalMilliseconds; if (tick >= 20) { sum += ms; worst = Math.Max(worst, ms); ticks++; slow.Add((ms, tick)); }                       // the first ticks are the JIT's (150 ms), not the sim's
            if (tick % 1 == 0)
                foreach (var r in mine) { seen.Add($"{r.Id}:{r.Order.Kind}"); if (r.Shape == Formation.ShieldWall) seen.Add($"{r.Id}:wall"); if (r.Order.IssuedTick == 100) seen.Add($"{r.Id}:placed"); }
        }
        Assert.True(b.Closed, "the battle did not close");
        output.WriteLine("slowest ticks: " + string.Join(", ", slow.OrderByDescending(x => x.Ms).Take(8).Select(x => $"{x.Tick}: {x.Ms:0.0}")));
        var res = b.Result!;
        output.WriteLine($"M-A: closed at tick {res.Ticks} ({res.Ticks / 20 / 60.0:0.0} min), winner team {res.WinnerTeam}, {res.OutcomeKey}; lost {res.Sides[0].Lost} / {res.Sides[1].Lost} (dead {res.Sides[0].Dead}+{res.Sides[1].Dead}, down {res.Sides[0].Down}+{res.Sides[1].Down}, fled {res.Sides[0].Fled}+{res.Sides[1].Fled}, yielded {res.Sides[0].Yielded}+{res.Sides[1].Yielded}); waves {res.Sides[0].Waves}/{res.Sides[1].Waves}; average {sum / ticks:0.00} ms a tick, worst {worst:0.0} ms over {ticks} ticks");
        Assert.True(seen.Contains("1:wall"), "the shield wall orders did not arrive");
        Assert.True(seen.Contains("2:Charge"), "the charge did not arrive");
        Assert.True(seen.Contains("3:placed"), "the drafted flank did not arrive (the order placed in the overlay, sent at the paused tick, took effect)");
        Assert.True(seen.Contains("1:Advance") && seen.Contains("4:Advance"), "the advance did not arrive");
        Assert.Equal(2000, res.Soldiers.Count);
        Assert.Contains(sink, e => e.Type == "battle_fought" && e.Causes.Any(c => c.EventId == new EntityId(777)));
        Assert.Contains(sink, e => e.Type == "battle_hero_led");
        Assert.True(sum / ticks <= TestBudget.Ms(25), $"average {sum / ticks:0.00} ms");
    }
}
