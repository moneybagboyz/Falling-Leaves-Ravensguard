using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Melee;

/// <summary>Headless AI-vs-AI benchmark (spike 2). Prints the table pasted into docs/spikes/02-melee.md.</summary>
[Collection("combat")]
public class MeleeBenchTests(CombatFixture f, ITestOutputHelper output)
{
    [Fact]
    public void Benchmark200SeededDuelsPerMatchup()
    {
        var rows = MeleeScenarios.BenchMatchups.Select(m => MeleeScenarios.Bench(f.Tables, m, 200)).ToList();
        output.WriteLine(MeleeScenarios.ToMarkdown(rows));
        foreach (var r in rows)
        {
            Assert.True(r.MeanS > 3, $"{r.Matchup}: fights should not end instantly ({r.MeanS:0.0}s)");
            Assert.True(r.MeanS < 120, $"{r.Matchup}: fights should not drag ({r.MeanS:0.0}s)");
            Assert.True(r.Outcomes.GetValueOrDefault(Outcome.Stalemate) <= r.N / 10, $"{r.Matchup}: too many timeouts");
        }
    }

    [Fact]
    public void SkirmishSummary()
    {
        int n = 20, wolves = 0, over = 0; double dur = 0; int routs = 0, kos = 0, dead = 0, fled = 0, yield = 0, ko = 0;
        for (ulong seed = 1; seed <= (ulong)n; seed++)
        {
            var w = MeleeScenarios.Skirmish(f.Tables, seed, 10, emitText: false);
            while (!w.IsOver && w.Tick < 20 * 300) w.StepWithAi();
            if (w.IsOver) over++;
            dur += w.TimeS;
            if (w.Fighters.Any(x => x.Active && x.Team == 0)) wolves++;
            routs += w.Events.Count(e => e.Kind == EventKind.Rout);
            dead += w.Fighters.Count(x => x.State == Act.Dead); ko += w.Fighters.Count(x => x.State == Act.Down);
            fled += w.Fighters.Count(x => x.State == Act.Fled); yield += w.Fighters.Count(x => x.State == Act.Surrendered);
        }
        output.WriteLine($"SKIRMISH 10v10 x{n}: finished {over}/{n}, mean {dur / n:0.0}s, wolves win {wolves}/{n}; per battle: dead {dead / (double)n:0.0}, down {ko / (double)n:0.0}, fled {fled / (double)n:0.0}, yielded {yield / (double)n:0.0}, rout events {routs / (double)n:0.0}");
    }
}
