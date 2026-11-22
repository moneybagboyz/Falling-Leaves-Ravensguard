using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Siege;

/// <summary>
/// P7-SG-B: where an engine's shot meets the people of a live fight. The fight keeps its own millimetres (a battle bubble's box has an origin in the world); the hook turns a landing in world millimetres into the fight's and has the
/// fight's one resolver strike everyone there (<see cref="FightWorld.EngineImpact"/>). A bolt lands as an edged quarrel, a stone as a thrown rock.
/// </summary>
public static class EngineFight
{
    /// <summary>The <see cref="EngineWorld.People"/> hook of a fight whose origin sits at (<paramref name="originX"/>, <paramref name="originY"/>) of the world.</summary>
    public static Func<long, long, int, long, ulong, bool, int> People(FightWorld fight, long originX = 0, long originY = 0)
        => (x, y, radiusMm, momentumMilli, key, bolt) => fight.EngineImpact(x - originX, y - originY, radiusMm, momentumMilli, key, bolt);
}
