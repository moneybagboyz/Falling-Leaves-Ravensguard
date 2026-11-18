using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Farming;

/// <summary>
/// The weather farming reads, by continental cell and game day. A pure function of (seed, cell, time) in production (<see cref="RegionFarmWeather"/>);
/// tests script it to hit a specific frost, drought or hail day. Temperatures are tenths of a degree C, rain tenths of a mm.
/// </summary>
public interface IFarmWeather
{
    /// <summary>Precipitation on the game day as water in 1/65536 mm (the raw value of a <see cref="Fixed"/>): rain plus snow water, since melt waters the soil.</summary>
    long DailyRainRaw(int cell, long day);
    /// <summary>Temperature at 05:00 of the game day (the frost hour), tenths of C, floored.</summary>
    int TempAt5Tenths(int cell, long day);
    /// <summary>True when a thunderstorm occurred on the game day (hail needs one).</summary>
    bool Thunderstorm(int cell, long day);
    /// <summary>Snow depth at 05:00 in cm.</summary>
    int SnowDepthCm(int cell, long day);
    /// <summary>The long condition (drought, wet year, cold summer) of a culture region and year.</summary>
    LongCondition Long(int culture, int year);
}

/// <summary>Production adapter over <see cref="RegionWeather"/> and <see cref="LongConditions"/>.</summary>
public sealed class RegionFarmWeather(RegionWeather weather, LongConditions conditions) : IFarmWeather
{
    public long DailyRainRaw(int cell, long day) => (long)weather.DailyRainMm(cell, day).Raw + weather.DailySnowCm(cell, day).Raw;
    public int TempAt5Tenths(int cell, long day) => (int)(((long)weather.AtLite(cell, day * 24 + 5).TempC.Raw * 10) >> 16);
    public bool Thunderstorm(int cell, long day)
    {
        for (int h = 12; h <= 18; h += 3) if (weather.AtLite(cell, day * 24 + h).State == WeatherState.Thunderstorm) return true;
        return false;
    }
    public int SnowDepthCm(int cell, long day) => (int)(weather.At(cell, day * 24 + 5).SnowDepthCm.Raw >> 16);
    public LongCondition Long(int culture, int year) => conditions.For(culture, year);
}
