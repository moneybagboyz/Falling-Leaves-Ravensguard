using System;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Game.World;

/// <summary>Weather at the hero, in the units the view draws with (precipitation 0..1, fog 0..1, snow depth in cm, wet ground 0..1, freeze 0..1).</summary>
public readonly record struct WeatherView(
    WeatherState State, float TempC, float Precip, bool Snowing, float Fog, float Cloud, int WindFrom, float WindMps, float SnowCm, float Wet, float Freeze)
{
    public bool Raining => Precip > 0 && !Snowing;
    public bool Thunder => State == WeatherState.Thunderstorm;
    /// <summary>Snow cover 0..1 for the shader: about 8 cm is a full white field.</summary>
    public float SnowCover => Math.Clamp(SnowCm / 8f, 0f, 1f);

    public static WeatherView Calm(float tempC) => new(WeatherState.Clear, tempC, 0, false, 0, 0.1f, 270, 2, 0, 0, tempC < -3 ? 1 : 0);

    /// <summary>Parses a dev override such as "rain", "storm", "snow", "fog", "overcast", "clear", optionally "rain:12" for the temperature.</summary>
    public static WeatherView? Parse(string? spec)
    {
        if (string.IsNullOrEmpty(spec)) return null;
        var parts = spec.Split(':');
        float temp = parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : float.NaN;
        float T(float def) => float.IsNaN(temp) ? def : temp;
        return parts[0] switch
        {
            "clear" => new WeatherView(WeatherState.Clear, T(16), 0, false, 0, 0.1f, 270, 2, 0, 0, 0),
            "overcast" => new WeatherView(WeatherState.Overcast, T(11), 0, false, 0, 0.85f, 250, 4, 0, 0.2f, 0),
            "rain" => new WeatherView(WeatherState.LightRain, T(9), 0.55f, false, 0, 1, 240, 5, 0, 0.85f, 0),
            "heavy" => new WeatherView(WeatherState.HeavyRain, T(9), 0.95f, false, 0, 1, 235, 8, 0, 1, 0),
            "storm" => new WeatherView(WeatherState.Thunderstorm, T(15), 1, false, 0, 1, 230, 11, 0, 1, 0),
            "snow" => new WeatherView(WeatherState.Snow, T(-3), 0.6f, true, 0.1f, 1, 300, 4, 14, 0, 1),
            "blizzard" => new WeatherView(WeatherState.Blizzard, T(-8), 1, true, 0.3f, 1, 310, 12, 25, 0, 1),
            "fog" => new WeatherView(WeatherState.Fog, T(6), 0, false, 0.9f, 0.6f, 270, 1, 0, 0.3f, 0),
            "frost" => new WeatherView(WeatherState.Frost, T(-4), 0, false, 0, 0.1f, 270, 1, 6, 0, 1),
            _ => null,
        };
    }
}

/// <summary>
/// Looks up <see cref="RegionWeather"/> for the hero's continent cell and turns it into a <see cref="WeatherView"/>, once per game hour. A region started
/// from a seed knows its continent; a region opened from a save regenerates the continent from the region's seed in the background and shows calm weather
/// until it is ready. The test hamlet has no continent: it gets a plain seasonal temperature and clear skies.
/// </summary>
public sealed class WeatherFeed
{
    readonly RegionData? _region;
    RegionWeather? _rw;
    readonly WeatherView? _forced;
    (long Hour, int Cell, WeatherView V) _cache = (long.MinValue, -1, default);

    public WeatherFeed(RegionData? region, RegionWeather? known, WeatherView? forced)
    {
        _region = region; _rw = known; _forced = forced;
        if (region != null && known == null && forced == null)
        {
            string dir = StreetView.ContentDir(); // engine paths are read here, on the main thread; the worker only touches plain data
            ulong seed = region.Seed;
            Task.Run(() => Build(dir, seed));
        }
    }

    public bool Ready => _forced != null || _region == null || _rw != null;

    void Build(string dir, ulong seed)
    {
        try
        {
            var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = seed;
            var continent = ContinentGenerator.Generate(cs);
            _rw = RegionWeather.ForContinent(continent, seed, WeatherSettings.LoadDefaults(dir));
            GD.Print("[weather] continent rebuilt for the region's seed");
        }
        catch (Exception e) { GD.PushWarning("weather unavailable: " + e.Message); }
    }

    /// <summary>The weather at world cell (wx, wy) in metres at game time <paramref name="t"/>.</summary>
    public WeatherView Sample(GameTime t, int wx, int wy)
    {
        if (_forced is { } f) return f;
        if (_region == null || _rw == null) return WeatherView.Calm(Fallback(t));
        int cx = Math.Clamp(_region.CellX + wx / 2000, 0, Continent.Size - 1), cy = Math.Clamp(_region.CellY + wy / 2000, 0, Continent.Size - 1);
        int cell = cy * Continent.Size + cx;
        long hour = t.TotalHours;
        if (_cache.Hour == hour && _cache.Cell == cell) return _cache.V;
        var w = _rw.At(cell, hour);
        // wet ground: rain of the last twelve hours, fading as it dries
        float wet = 0, k = 1;
        for (int i = 0; i < 12; i++, k *= 0.85f)
        {
            var p = _rw.AtLite(cell, hour - i);
            if (!p.PrecipIsSnow) wet += p.PrecipMmH.ToFloat() * k;
        }
        float mm = w.PrecipMmH.ToFloat();
        float precip = mm > 0 ? Math.Clamp(0.3f + mm / 4f, 0.3f, 1f) : 0f;
        long day = hour / 24;
        float mean = (_rw.DailyMeanTemp(cell, day - 1).ToFloat() + _rw.DailyMeanTemp(cell, day - 2).ToFloat() + _rw.DailyMeanTemp(cell, day - 3).ToFloat()) / 3f;
        float freeze = Math.Clamp((-mean - 1f) / 5f, 0f, 1f);
        var v = new WeatherView(w.State, w.TempC.ToFloat(), precip, w.PrecipIsSnow && mm > 0, w.FogDensity.ToFloat(), w.Cloud.ToFloat(), w.WindFromDeg, w.WindMps.ToFloat(),
            w.SnowDepthCm.ToFloat(), Math.Clamp(wet / 3f, 0f, 1f), freeze);
        _cache = (hour, cell, v);
        return v;
    }

    /// <summary>Plain seasonal temperature for a world without a continent: -2 in deep winter to 19 in midsummer, warmest mid-afternoon.</summary>
    static float Fallback(GameTime t)
    {
        float seasonal = 8.5f + 10.5f * MathF.Cos((t.DayOfYear - 23f) / 60f * MathF.Tau);
        return seasonal + 4f * MathF.Sin((t.Hour - 9f) / 24f * MathF.Tau);
    }

    public static string Name(WeatherState s) => s switch
    {
        WeatherState.Clear => "WEATHER_CLEAR", WeatherState.Overcast => "WEATHER_OVERCAST", WeatherState.LightRain => "WEATHER_RAIN", WeatherState.HeavyRain => "WEATHER_HEAVY_RAIN",
        WeatherState.Thunderstorm => "WEATHER_STORM", WeatherState.Fog => "WEATHER_FOG", WeatherState.Frost => "WEATHER_FROST", WeatherState.Snow => "WEATHER_SNOW",
        WeatherState.Blizzard => "WEATHER_BLIZZARD", _ => "WEATHER_HEAT",
    };
}
