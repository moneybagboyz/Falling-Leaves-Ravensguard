namespace Ravensguard.Sim.Tests;

/// <summary>
/// Time budgets for tests. Budgets are measured on the reference PC (plan §2.11, tools/perf); shared CI runners
/// are slower and noisy, so there a budget only catches gross regressions (5x).
/// </summary>
public static class TestBudget
{
    public static bool OnCi => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));
    public static double Ms(double localBudgetMs) => OnCi ? localBudgetMs * 5 : localBudgetMs;
}
