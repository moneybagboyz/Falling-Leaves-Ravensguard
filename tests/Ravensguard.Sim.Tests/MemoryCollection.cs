namespace Ravensguard.Sim.Tests;

/// <summary>
/// Tests that read the process's managed heap (<c>GC.GetTotalMemory</c>) to check a memory budget: other tests allocate in the same process, so these run alone, after the
/// parallel collections (xUnit runs a collection with <c>DisableParallelization</c> by itself).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MemoryCollection
{
    public const string Name = "Memory";
}
