using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.WorldGen.Region;

/// <summary>
/// Stage 3: builds the embark region at full detail from a continent, its history and a 16 x 16 cell window (plan §1A, §4A.4).
/// All maths is integer or Q16.16 fixed point and every iteration is in index or stable-id order, so a seed gives the same region everywhere.
/// </summary>
public static class RegionGenerator
{
    /// <summary>Generates the region whose north-west continental cell is (cx, cy); the window must lie inside the 256 x 256 grid.</summary>
    public static Region Generate(Continent continent, WorldHistory history, int cx, int cy, RegionSettings settings)
    {
        if (cx < 0 || cy < 0 || cx + 16 > Continent.Size || cy + 16 > Continent.Size)
            throw new ArgumentOutOfRangeException(nameof(cx), "The 16 x 16 cell window must lie inside the continent.");
        return new RegionBuilder(continent, history, cx, cy, settings).Build();
    }
}
