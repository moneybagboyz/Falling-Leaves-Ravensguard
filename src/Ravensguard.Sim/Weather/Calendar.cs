using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Weather;

/// <summary>A named feast day. DayOfYear is 1..60.</summary>
public sealed record Feast(string Name, int DayOfYear);

/// <summary>Month and feast names for one culture. 12 months of 5 days (three per season).</summary>
public sealed record CalendarNames(string[] Months, Feast[] Feasts)
{
    public static readonly CalendarNames Default = new(
        ["Thawmoon", "Seedmoon", "Greenmoon", "Hayrise", "Highsun", "Cornmoon", "Reapmoon", "Leaffall", "Mistmoon", "Frostmoon", "Deepsnow", "Wolfmoon"],
        [new("Spring Fair", 8), new("Midsummer", 23), new("Harvest Festival", 38), new("Midwinter Feast", 53)]);
}

/// <summary>Hook so cultures can supply their own month and feast names (plan §3c).</summary>
public interface ICalendarNameProvider { CalendarNames NamesFor(int cultureId); }

/// <summary>Calendar: the 60-day year of plan §1, with per-culture names.</summary>
public sealed class Calendar
{
    public const int DaysPerMonth = 5;
    public const int MonthsPerYear = TimeConst.DaysPerYear / DaysPerMonth;
    public const int SummerSolsticeDay = 23, WinterSolsticeDay = 53, SpringEquinoxDay = 8, AutumnEquinoxDay = 38;

    private readonly Dictionary<int, CalendarNames> _byCulture = [];
    private readonly ICalendarNameProvider? _provider;

    public Calendar(ICalendarNameProvider? provider = null) { _provider = provider; }

    public void SetNames(int cultureId, CalendarNames names)
    {
        if (names.Months.Length != MonthsPerYear) throw new ArgumentException($"need {MonthsPerYear} month names");
        _byCulture[cultureId] = names;
    }

    public CalendarNames Names(int cultureId)
        => _byCulture.TryGetValue(cultureId, out var n) ? n : _provider?.NamesFor(cultureId) ?? CalendarNames.Default;

    /// <summary>0..11</summary>
    public static int MonthIndex(int dayOfYear) => (dayOfYear - 1) / DaysPerMonth;
    public static int DayOfMonth(int dayOfYear) => (dayOfYear - 1) % DaysPerMonth + 1;

    public string MonthName(int dayOfYear, int cultureId = -1) => Names(cultureId).Months[MonthIndex(dayOfYear)];

    public Feast? FeastOn(int dayOfYear, int cultureId = -1)
        => Names(cultureId).Feasts.FirstOrDefault(f => f.DayOfYear == dayOfYear);

    /// <summary>Next feast on or after the given day, wrapping into next year (daysUntil 0 = today).</summary>
    public (Feast Feast, int DaysUntil) NextFeast(int dayOfYear, int cultureId = -1)
    {
        var fs = Names(cultureId).Feasts;
        Feast best = fs[0]; int bd = int.MaxValue;
        foreach (var f in fs)
        {
            int d = (f.DayOfYear - dayOfYear + TimeConst.DaysPerYear) % TimeConst.DaysPerYear;
            if (d < bd) { bd = d; best = f; }
        }
        return (best, bd);
    }

    public string Describe(GameTime t, int cultureId = -1)
    {
        var f = FeastOn(t.DayOfYear, cultureId);
        string s = $"{DayOfMonth(t.DayOfYear)} {MonthName(t.DayOfYear, cultureId)}, {t.Season}, year {t.Year}";
        return f == null ? s : $"{s} ({f.Name})";
    }
}

/// <summary>Day length by latitude and day of year; sunrise and sunset (plan §3c). Integer/Fixed only.</summary>
public static class Daylight
{
    // Half-swing of day length around 12 h at the solstice, in minutes, by latitude in whole degrees (astronomical, with refraction).
    private static readonly int[] LatDeg = [0, 10, 20, 30, 35, 40, 45, 48, 50, 55, 60, 64, 66];
    private static readonly int[] AmpMin = [7, 42, 80, 125, 151, 181, 217, 243, 262, 322, 412, 541, 720];

    /// <summary>Latitude in tenths of a degree for a continent row (y = 0 is the north edge).</summary>
    public static int LatitudeTenths(WeatherSettings s, int row)
        => s.LatitudeNorthTenths + (s.LatitudeSouthTenths - s.LatitudeNorthTenths) * row / (Continent_Size - 1);
    private const int Continent_Size = 256;

    private static int Amplitude(int latTenths)
    {
        int lat = Math.Clamp(latTenths, 0, 665);
        int i = 0;
        while (i < LatDeg.Length - 2 && lat >= LatDeg[i + 1] * 10) i++;
        long span = (LatDeg[i + 1] - LatDeg[i]) * 10;
        return (int)(AmpMin[i] + (AmpMin[i + 1] - AmpMin[i]) * (lat - LatDeg[i] * 10L) / span);
    }

    /// <summary>Sun phase in degrees: 0 at the spring equinox, 90 at midsummer (day 23), 270 at midwinter (day 53).</summary>
    public static Fixed Phase(Fixed dayOfYearPosition) => (dayOfYearPosition - Fixed.FromInt(Calendar.SpringEquinoxDay)) * 6;

    /// <summary>Minutes of daylight on a day (1..60). Northern hemisphere.</summary>
    public static int DayLengthMinutes(int latitudeTenths, int dayOfYear)
    {
        Fixed s = WeatherMath.SinDeg(Phase(Fixed.FromInt(dayOfYear)));
        int m = 720 + (s * Amplitude(latitudeTenths)).Floor();
        return Math.Clamp(m, 60, 1380);
    }

    public static Fixed DayLengthHours(int latitudeTenths, int dayOfYear) => Fixed.FromRatio(DayLengthMinutes(latitudeTenths, dayOfYear), 60);

    /// <summary>Minutes after midnight (solar noon is 12:00).</summary>
    public static int SunriseMinute(int latitudeTenths, int dayOfYear) => 720 - DayLengthMinutes(latitudeTenths, dayOfYear) / 2;
    public static int SunsetMinute(int latitudeTenths, int dayOfYear) => 720 + DayLengthMinutes(latitudeTenths, dayOfYear) / 2;

    public static bool IsDaylight(int latitudeTenths, GameTime t)
    {
        int m = t.Hour * 60 + t.Minute;
        return m >= SunriseMinute(latitudeTenths, t.DayOfYear) && m < SunsetMinute(latitudeTenths, t.DayOfYear);
    }
}
