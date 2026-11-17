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
}
