namespace Ravensguard.Sim.Tests;

/// <summary>
/// Time budgets for tests. Budgets are measured on the reference PC (plan §2.11, tools/perf); shared CI runners
/// are slower and noisy, so there a budget only catches gross regressions (5x).
/// </summary>
public static class TestBudget
{
    public static bool OnCi => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));
    public static double Ms(double localBudgetMs) => OnCi ? localBudgetMs * 5 : localBudgetMs;

    /// <summary>
    /// Measures a wall-clock figure fairly (Phase 3 cleanup: the budgets stay, the measuring is made fair). The caller warms up first (JIT, first loads); every pass then starts from a settled heap
    /// (<see cref="Ravensguard.Sim.Diagnostics.GcSettle"/>: the world just built leaves the young generations, as the game does after a load) and the best of up to <paramref name="passes"/> passes
    /// is returned, stopping as soon as one is within <paramref name="budgetMs"/> (<see cref="Ms"/> applied). One noisy pass (a GC, another process) is forgiven; a persistent regression is not.
    /// The tests that use it are in the <see cref="TimingCollection"/>, so the suite's own parallel tests are not the noise.
    /// </summary>
    public static double BestPass(double budgetMs, int passes, Func<double> pass)
    {
        double best = double.MaxValue;
        for (int i = 0; i < passes; i++)
        {
            Ravensguard.Sim.Diagnostics.GcSettle.Run();
            best = Math.Min(best, pass());
            if (best < Ms(budgetMs)) break;
        }
        return best;
    }
}
