using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Weather;

public enum LongKind : byte { None, Drought, WetYear, ColdSummer }

/// <summary>A multi-week condition for one culture region and year. Days are 1..60 inclusive.</summary>
public readonly record struct LongCondition(LongKind Kind, int StartDay, int EndDay)
{
    public bool Covers(int dayOfYear) => Kind != LongKind.None && dayOfYear >= StartDay && dayOfYear <= EndDay;
}

/// <summary>Drought, wet year and cold summer per culture region and year; deterministic from the seed (plan §3c).</summary>
public sealed class LongConditions(ulong seed, WeatherSettings settings)
{
    public LongCondition For(int cultureId, int year)
    {
        var r = new RngStream(seed, year, (ulong)(uint)cultureId + 0x100, RngStreams.WeatherLong);
        int roll = r.Range(0, 1000);
        var s = settings;
        if (roll < s.DroughtPermilleYear)
        {
            int start = r.Range(8, 29), len = r.Range(21, 36);
            return new(LongKind.Drought, start, Math.Min(60, start + len - 1));
        }
        roll -= s.DroughtPermilleYear;
        if (roll < s.WetYearPermilleYear) return new(LongKind.WetYear, 1, 60);
        roll -= s.WetYearPermilleYear;
        if (roll < s.ColdSummerPermilleYear) return new(LongKind.ColdSummer, 14, 44);
        return new(LongKind.None, 0, 0);
    }

    /// <summary>Precipitation multiplier in percent.</summary>
    public static int RainPct(LongCondition c, int dayOfYear) => !c.Covers(dayOfYear) ? 100 : c.Kind switch
    {
        LongKind.Drought => 22, LongKind.WetYear => 150, LongKind.ColdSummer => 110, _ => 100,
    };

    /// <summary>Temperature offset in C.</summary>
    public static Fixed TempOffset(LongCondition c, int dayOfYear) => !c.Covers(dayOfYear) ? Fixed.Zero : c.Kind switch
    {
        LongKind.ColdSummer => -Fixed.FromRatio(35, 10), LongKind.Drought => Fixed.FromRatio(15, 10), _ => Fixed.Zero,
    };

    /// <summary>A crude 0..100 harvest hazard for a culture-year, for the history sim (famines from real bad years).</summary>
    public int BadYearScore(int cultureId, int year) => For(cultureId, year).Kind switch
    {
        LongKind.Drought => 80, LongKind.WetYear => 55, LongKind.ColdSummer => 65, _ => 0,
    };
}
