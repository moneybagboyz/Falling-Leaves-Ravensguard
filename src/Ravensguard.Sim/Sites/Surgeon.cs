using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Sites;

/// <summary>
/// The nearest surgeon from a site (4.10): the nearest town's name and the distance, and how long the ride or the walk takes in game days. The live world moves the hero at real
/// metres per second while the clock runs 20 game seconds a real second, so a day at 5 m/s covers 21.6 km: <c>days = metres x 20 / (speed x 86,400)</c>. 20 km is 0.93 days by horse
/// (a trot, 5 m/s) and 1.54 on foot (3 m/s), against the 1-day tendon and fracture windows.
/// </summary>
public readonly record struct SurgeonInfo(string Town, int Metres)
{
    public static readonly SurgeonInfo None = new("", -1);
    public bool Known => Metres >= 0;
    public const int HorseMmps = 5000, FootMmps = 3000;

    /// <summary>Game seconds to cover the distance at a speed (real mm per second).</summary>
    public long GameSecAt(int mmps) => Metres < 0 ? -1 : (long)Metres * 1000 * TimeConst.GameSecondsPerRealSecond / Math.Max(1, mmps);
    /// <summary>Game days x 100 (tenths shown as 0.9): by horse and on foot.</summary>
    public int DaysX100Horse => Metres < 0 ? -1 : (int)(GameSecAt(HorseMmps) * 100 / TimeConst.SecondsPerDay);
    public int DaysX100Foot => Metres < 0 ? -1 : (int)(GameSecAt(FootMmps) * 100 / TimeConst.SecondsPerDay);

    /// <summary>The nearest town of the region from a point (region metres).</summary>
    public static SurgeonInfo Nearest(RegionData region, int x, int y)
    {
        long best = long.MaxValue; string name = "";
        foreach (var s in region.Settlements)
        {
            if (s.Class != SettlementClass.Town) continue;
            long dx = s.CenterX - x, dy = s.CenterY - y, d2 = dx * dx + dy * dy;
            if (d2 < best) { best = d2; name = s.Name; }
        }
        return best == long.MaxValue ? None : new SurgeonInfo(name, (int)RegionData.Isqrt(best));
    }

    /// <summary>True when a wound window of <paramref name="windowSec"/> opened at <paramref name="bornSec"/> is still open on arrival at the surgeon, leaving at <paramref name="nowSec"/>.</summary>
    public bool InTime(long bornSec, long windowSec, long nowSec, bool horse) => Metres >= 0 && nowSec + GameSecAt(horse ? HorseMmps : FootMmps) <= bornSec + windowSec;
}
