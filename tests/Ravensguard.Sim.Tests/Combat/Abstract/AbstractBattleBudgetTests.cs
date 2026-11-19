using System.Diagnostics;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Abstract;

/// <summary>
/// AB-13's budgets at the caravan ambushes' sizes (abstract-battles-v1 8). Phase 4 review finding 2: the soak ran in the parallel "fight" collection with no settled heap, so the full suite
/// (which CI runs, Soak tests included) failed it at 7.1 ms against 3 ms while it takes about 2 ms alone. It now runs in the <see cref="TimingCollection"/> and measures the way every other
/// budget test does (<see cref="TestBudget.BestPass"/>: a settled heap, the best of a few passes). The budgets are unchanged. The 2000 v 2000 budget (45 ms) comes back as a Phase 7 test with
/// the battle bubble (army-scale battles moved to Phase 7, decided with the user).
/// </summary>
[Collection(TimingCollection.Name)]
public class AbstractBattleBudgetTests(FightFixture fx, ITestOutputHelper output) : IClassFixture<FightFixture>
{
    [Fact, Trait("Category", "Soak")]
    public void AB13_Soak_TheBudgets()
    {
        var t = WoundTables.For(fx.Db) ?? throw new InvalidOperationException("no wound tables in content/base/abstract");
        var over = new List<string>();
        foreach (var (n, budgetMs) in new[] { (100, 3), (200, 8) })
        {
            var warm = Stopwatch.StartNew();
            for (int i = 0; i < 2 || warm.ElapsedMilliseconds < 1500; i++) new AbstractBattle(fx.C, t, AbstractBattleTests.Big(n, (ulong)(100 + i % 8))).Run();
            int k = 0;
            double ms = TestBudget.BestPass(budgetMs, 5, () =>
            {
                var input = AbstractBattleTests.Big(n, (ulong)(2 + k++ % 3));
                var sw = Stopwatch.StartNew(); new AbstractBattle(fx.C, t, input).Run(); return sw.Elapsed.TotalMilliseconds;
            });
            output.WriteLine($"{n} v {n}: {ms:0.0} ms (budget {budgetMs}, here {TestBudget.Ms(budgetMs):0})");
            if (ms > TestBudget.Ms(budgetMs)) over.Add($"{n} v {n} took {ms:0.0} ms (budget {budgetMs}, here {TestBudget.Ms(budgetMs):0})");
        }
        Assert.True(over.Count == 0, string.Join("; ", over));
    }
}
