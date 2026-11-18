namespace Ravensguard.Sim.Tests;

/// <summary>
/// Tests that assert wall-clock budgets (worst frame or tick) on long runs. Running them alongside the parallel collections let other tests steal the CPU and
/// produced false failures (a 297 ms tick on the three-region walk that takes about 65 ms alone), so they run by themselves after the parallel collections.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}
