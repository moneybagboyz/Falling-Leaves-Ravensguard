using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Live;

/// <summary>
/// The bubble rules shared by every LODBridge (plan 2.3, abstract spec 3.5, 3.6): promote inside 150 m, demote beyond 200 m after 5 real seconds (100 game seconds at 1x:
/// 20 game seconds per real second), at most 3 bubbles, at most 50 promotions per frame. Distances are millimetres of the continent frame.
/// </summary>
public static class Bubble
{
    public const int PromoteM = 150, DemoteM = 200, MaxBubbles = 3, MaxPromotionsPerFrame = 50;
    public const long DemoteAfterGameSeconds = 5 * TimeConst.GameSecondsPerRealSecond;

    public static long Sq(long v) => v * v;
    public static bool InsidePromote(long d2Mm) => d2Mm <= Sq(PromoteM * 1000L);
    public static bool BeyondDemote(long d2Mm) => d2Mm > Sq(DemoteM * 1000L);

    /// <summary>Squared distance (mm) from (<paramref name="x"/>, <paramref name="y"/>) to the nearest of the first <see cref="MaxBubbles"/> centres, and which one; (long.MaxValue, -1) with none.</summary>
    public static (long D2, int Bubble) Nearest(ReadOnlySpan<(long X, long Y)> centres, long x, long y)
    {
        long best = long.MaxValue; int which = -1;
        for (int i = 0; i < Math.Min(MaxBubbles, centres.Length); i++)
        {
            long d2 = Sq(centres[i].X - x) + Sq(centres[i].Y - y);
            if (d2 < best) { best = d2; which = i; }
        }
        return (best, which);
    }

    /// <summary>
    /// The demotion timer of one live entity: true when it has stayed beyond <see cref="DemoteM"/> for <see cref="DemoteAfterGameSeconds"/>. <paramref name="outsideSince"/> is
    /// the entity's own state (-1 while inside); coming back inside clears it, so a person at 205 m who steps to 190 m starts over.
    /// </summary>
    public static bool ShouldDemote(long d2Mm, long nowSec, ref long outsideSince)
    {
        if (!BeyondDemote(d2Mm)) { outsideSince = -1; return false; }
        if (outsideSince < 0) outsideSince = nowSec;
        return nowSec - outsideSince >= DemoteAfterGameSeconds;
    }
}
