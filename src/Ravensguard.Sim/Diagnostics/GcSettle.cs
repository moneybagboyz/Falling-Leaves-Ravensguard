namespace Ravensguard.Sim.Diagnostics;

/// <summary>
/// Moves a freshly built world out of the young generations. A world just built (regions, ground, bodies, wardrobes) sits in generation 0, and the first two collections after it each copy it on:
/// a 30-60 ms pause that lands on a live tick (the 20 Hz tick is 50 ms). Two forced, compacting full collections at load time (behind the loading screen, or before a measured run) pay that once, so
/// the ordinary collections of play find nothing young to copy. It has no effect on the simulation, only on when the pause falls.
/// </summary>
public static class GcSettle
{
    public static void Run()
    {
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.Collect(2, GCCollectionMode.Forced, true, true);
    }
}
