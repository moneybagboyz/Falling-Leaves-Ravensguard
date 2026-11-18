using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Onboarding;

/// <summary>
/// The fair-weather start (onboarding spec 4.1, open question 4): a new game starts at 08:00 on the nominal day, or on the first later day within ten that has no thunderstorm, blizzard, heatwave or deep frost in its first 24 game
/// hours. Weather is a pure function of (seed, cell, hour), so the search is too: the same world gives the same start on every machine. An Ironman run with a fixed date asks for no shift.
/// </summary>
public static class StartDateFinder
{
    public const int MaxShiftDays = 10, DeepFrostC = -8;

    public static bool Harsh(WeatherSample s) => s.State is WeatherState.Thunderstorm or WeatherState.Blizzard or WeatherState.Heatwave || s.TempC.ToDouble() <= DeepFrostC;

    /// <summary>The days to shift the start by (0 to 10): the first <c>d</c> for which <paramref name="harshInFirst24h"/> is false for the day <c>nominalDay + d</c>; 0 when none within the limit is better (the nominal day stays).</summary>
    public static int Shift(Func<long, bool> harshInFirst24h, long nominalDay, bool fixedDate = false)
    {
        if (fixedDate || !harshInFirst24h(nominalDay)) return 0;
        for (int d = 1; d <= MaxShiftDays; d++) if (!harshInFirst24h(nominalDay + d)) return d;
        return 0;
    }

    /// <summary>The same over the real weather of a continent cell: hours 08:00 of the day to 08:00 of the next.</summary>
    public static int Shift(RegionWeather w, int cell, long nominalDay, bool fixedDate = false)
        => Shift(day => { for (int h = 8; h < 32; h++) if (Harsh(w.AtLite(cell, day * 24 + h))) return true; return false; }, nominalDay, fixedDate);
}
