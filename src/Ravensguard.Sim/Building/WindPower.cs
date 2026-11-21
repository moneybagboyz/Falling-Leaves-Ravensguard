using Ravensguard.Content;

namespace Ravensguard.Sim.Building;

/// <summary>
/// P7-LB1 (large-buildings 4.2): wind power. A post windmill (a 20 m rotor) gives <c>19 x v^3</c> watts for a wind of v m/s, nothing below 3 m/s, capped at its rating (4,000 W) and stopped above
/// 15 m/s (sails furled, storm days). Only millstones and drainage pumps take wind (<see cref="PowerPieceDef.WindOk"/>). Integer only: the wind comes in mm/s from the weather.
/// </summary>
public static class WindPower
{
    /// <summary>The mill's watts in a wind of <paramref name="windMmps"/> millimetres a second (the whole metres a second are used: 6.9 m/s reads as 6).</summary>
    public static int Watts(WaterPowerRulesDef r, int ratedW, int windMmps)
    {
        int v = windMmps / 1000;
        if (v < r.WindMinMps || v > r.WindMaxMps) return 0;
        return (int)Math.Min(ratedW, (long)r.WindK * v * v * v);
    }

    /// <summary>
    /// A windmill needs open ground: no building cell within <c>WindClearM</c> metres upwind (west), within a strip as wide as its rotor (20 m, 10 either side). <paramref name="blocked"/> says whether
    /// a cell has a roof or wall of a building.
    /// </summary>
    public static bool OpenGround(WaterPowerRulesDef r, int x, int y, Func<int, int, bool> blocked)
    {
        for (int dx = 1; dx <= r.WindClearM; dx++)
            for (int dy = -10; dy <= 10; dy++)
                if (blocked(x - dx, y + dy)) return false;
        return true;
    }
}
