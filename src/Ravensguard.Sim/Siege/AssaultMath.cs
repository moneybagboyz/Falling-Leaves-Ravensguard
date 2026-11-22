using Ravensguard.Content;

namespace Ravensguard.Sim.Siege;

/// <summary>P7-SG-C: the small integer rules of an assault that have a number to test (sieges-and-engines 4.4). No floats, no square roots from the library: a fall's speed comes from a table.</summary>
public static class AssaultMath
{
    private const int G = 9810;      // mm/s2

    private static readonly int[] FallTable = BuildFallTable();

    private static int ISqrt(long v)
    {
        if (v <= 0) return 0;
        long x = v, y = (x + 1) / 2;
        while (y < x) { x = y; y = (x + v / x) / 2; }
        return (int)x;
    }

    /// <summary>The speed (mm/s) a thing reaches falling a whole metre count, <c>sqrt(2 g h)</c>: 0 m 0, 1 m 4,429, 6 m 10,849 (the spec's "about 10.8").</summary>
    private static int[] BuildFallTable()
    {
        var t = new int[33];
        for (int m = 0; m < t.Length; m++) t[m] = ISqrt(2L * G * m * 1000);
        return t;
    }

    /// <summary>The speed (mm/s) of a body that has fallen <paramref name="heightMm"/>: the table's metres, interpolated between them in integers.</summary>
    public static int FallSpeedMmps(int heightMm)
    {
        if (heightMm <= 0) return 0;
        int m = heightMm / 1000, rem = heightMm % 1000;
        if (m >= FallTable.Length - 1) return FallTable[^1];
        return FallTable[m] + (FallTable[m + 1] - FallTable[m]) * rem / 1000;
    }

    /// <summary>A defender's try at pushing a ladder off, permille: <c>500 + 10 x (Athletics - 30)</c>, 700 at Athletics 50 (the spec's SG-8), kept between 50 and 950.</summary>
    public static int PushPm(AssaultRulesDef r, int athletics) => Math.Clamp(r.PushBasePm + r.PushPerSkillPm * (athletics - r.PushSkillBase), 50, 950);

    /// <summary>Whether a night sally can go out at this hour of the day (20:00 to 05:00 in the content).</summary>
    public static bool IsNight(AssaultRulesDef r, int hourOfDay) => r.NightFromHour > r.NightToHour ? hourOfDay >= r.NightFromHour || hourOfDay < r.NightToHour : hourOfDay >= r.NightFromHour && hourOfDay < r.NightToHour;

    /// <summary>
    /// Whether the garrison sallies (4.5): some besieger's engine stands within <c>sally_range_mm</c> and the sally (men it can spare) is at least <c>sally_over_pm</c> of the guard it would meet (by the defender's sight). It
    /// goes at night if it can: by day only when the guard is under half the sally.
    /// </summary>
    public static bool SallyWanted(AssaultRulesDef r, long nearestEngineMm, int sallyMen, int guardMen, int hourOfDay)
    {
        if (nearestEngineMm > r.SallyRangeMm || sallyMen <= 0) return false;
        if ((long)sallyMen * 1000 < (long)guardMen * r.SallyOverPm) return false;
        return IsNight(r, hourOfDay) || guardMen * 2 < sallyMen;
    }
}
