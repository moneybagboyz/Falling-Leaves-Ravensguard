using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.WorldGen.Region;

/// <summary>
/// Stage 3: builds the embark region at full detail from a continent, its history and a 16 x 16 cell window (plan §1A, §4A.4).
/// All maths is integer or Q16.16 fixed point and every iteration is in index or stable-id order, so a seed gives the same region everywhere.
/// </summary>
public static class RegionGenerator
{
    /// <summary>Generates the region whose north-west continental cell is (cx, cy). The window is a square of the 16 x 16 region grid, so both must be multiples of 16 (decision O1).</summary>
    public static Region Generate(Continent continent, WorldHistory history, int cx, int cy, RegionSettings settings)
    {
        if (cx < 0 || cy < 0 || cx + 16 > Continent.Size || cy + 16 > Continent.Size || cx % 16 != 0 || cy % 16 != 0)
            throw new ArgumentOutOfRangeException(nameof(cx), "The window must be a square of the 16 x 16 region grid (origin cell a multiple of 16, inside the continent).");
        return new RegionBuilder(continent, history, cx, cy, settings).Build();
    }

    /// <summary>Generates the region at a grid square; independent of what else has been generated.</summary>
    public static Region Generate(Continent continent, WorldHistory history, RegionCoord at, RegionSettings settings)
        => Generate(continent, history, at.CellX, at.CellY, settings);

    /// <summary>
    /// Terrain heights (cm) of a rectangle of global tiles (64 m tiles counted from the continent's north-west corner), exactly as the regions compute them: a pure
    /// function of the continent and the settings, so a tile on a region edge has the same height whichever region asks (and for tests of that).
    /// </summary>
    public static int[] HeightBlock(Continent continent, WorldHistory history, RegionSettings settings, int tileX0, int tileY0, int w, int h)
        => RegionBuilder.HeightBlock(continent, history, settings, tileX0, tileY0, w, h);
}
