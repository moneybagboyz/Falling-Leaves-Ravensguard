using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen;
using static Ravensguard.Sim.Weather.WeatherMath;

namespace Ravensguard.Sim.Weather;

public enum FrontKind : byte { Cold, Warm, Occluded }

/// <summary>
/// A weather front: a deterministic object spawned per season from the seed that drifts west to east.
/// Positions are in continent cells (Q16.16). Everything is a pure function of the seed and the hour.
/// </summary>
public sealed record Front(
    long Id, long SpawnHour, int LifeHours, FrontKind Kind,
    Fixed X0, Fixed Y, Fixed Tilt, Fixed SpeedCellsPerDay, Fixed HalfWidth, Fixed HalfLength,
    Fixed Strength, Fixed Moisture, Fixed TempAnomalyC, ulong WaveSeed)
{
    public bool ActiveAt(long hour) => hour >= SpawnHour && hour < SpawnHour + LifeHours;

    /// <summary>Front line x at row y.</summary>
    public Fixed CentreX(Fixed y, long hour)
    {
        Fixed days = Fixed.FromRatio((int)(hour - SpawnHour), 24);
        Fixed wave = DetNoise.Value(WaveSeed, y / 18, Fixed.Zero) * 14 - Fixed.FromInt(7);
        return X0 + SpeedCellsPerDay * days + Tilt * (y - Y) + wave;
    }

    /// <summary>Strength fades to 55% over the life of the front.</summary>
    public Fixed Fade(long hour) => Fixed.One - Fixed.FromRatio(45 * (int)(hour - SpawnHour), 100 * LifeHours);

    /// <summary>
    /// Influence of this front at a cell: rain (0..~1.3), wind proxy (0..~1.3) and temperature anomaly in C.
    /// dx is positive ahead of the front (east of the line, not yet passed).
    /// </summary>
    public (Fixed Rain, Fixed Wind, Fixed Temp) Sample(int x, int y, long hour)
    {
        Fixed fy = Fixed.FromInt(y);
        Fixed lat = Tri(fy, Y, HalfLength);
        if (lat.Raw == 0) return default;
        lat = Fixed.Min(Fixed.One, lat * 2);   // flat core, tapered ends
        Fixed dx = Fixed.FromInt(x) - CentreX(fy, hour);
        Fixed w = HalfWidth;
        if (dx > w * 5 || dx < -w * 6) return default;
        Fixed amp = Strength * Fade(hour) * lat;
        Fixed rain = Fixed.Zero;
        switch (Kind)
        {
            case FrontKind.Cold: rain = Tri(dx, Fixed.Zero, w) + Tri(dx, -w * 3 / 2, w * 3 / 2) * F(35, 100); break;
            case FrontKind.Warm: rain = Tri(dx, w, w * 2) * F(85, 100); break;
            default: rain = Tri(dx, Fixed.Zero, w * 3 / 2) * F(90, 100); break;
        }
        Fixed wind = Tri(dx, Fixed.Zero, w * 2);
        Fixed t;
        if (dx < Fixed.Zero) t = TempAnomalyC * Fixed.Min(Fixed.One, -dx / w) * Clamp01(Fixed.One - (-dx - w) / (w * 5));
        else t = -TempAnomalyC * F(4, 10) * Clamp01(Fixed.One - dx / (w * 3));
        return (rain * amp * Moisture, wind * amp, t * lat);
    }
}

/// <summary>Continent-scale weather: fronts spawned per season from the seed (plan §3c). Pure and order-independent.</summary>
public sealed class ContinentalWeather
{
    public const int Width = Continent.Size;
    private const int MaxSeasonsBack = 2;

    public ulong Seed { get; }
    public WeatherSettings Settings { get; }
    public LongConditions Long { get; }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, Front[]> _seasonFronts = new();
    /// <summary>One immutable reference swapped atomically, so concurrent readers never see an hour paired with another hour's fronts.</summary>
    private sealed record HourFronts(long Hour, Front[] Fronts);
    private volatile HourFronts _last = new(long.MinValue, []);

    public ContinentalWeather(ulong seed, WeatherSettings? settings = null)
    {
        Seed = seed; Settings = settings ?? new WeatherSettings();
        Long = new LongConditions(seed, Settings);
    }

    /// <summary>All fronts spawned in one season (key = year * 4 + season). A pure function of the seed.</summary>
    public Front[] SeasonFronts(long seasonKey) => _seasonFronts.GetOrAdd(seasonKey, Spawn);

    private Front[] Spawn(long key)
    {
        int season = (int)(((key % 4) + 4) % 4);
        long seasonStartHour = key * (TimeConst.DaysPerSeason * 24L);
        var s = Settings;
        var rng = new RngStream(Seed, key, 0xF407, 0xF1);
        int n = rng.Range(s.FrontCountMin[season], s.FrontCountMax[season] + 1);
        var list = new Front[n];
        for (int i = 0; i < n; i++)
        {
            var r = new RngStream(Seed, key, (ulong)i + 1, 0xF2);
            int kindRoll = r.Range(0, 100);
            var kind = kindRoll < s.ColdFrontPct[season] ? FrontKind.Cold : kindRoll < s.ColdFrontPct[season] + 30 ? FrontKind.Warm : FrontKind.Occluded;
            Fixed W = Fixed.FromInt(r.Range(9, 17));
            Fixed speed = Fixed.FromRatio(r.Range(s.FrontSpeedMinCellsDay * 10, s.FrontSpeedMaxCellsDay * 10 + 1), 10);
            Fixed x0 = -(W * 4 + Fixed.FromInt(4));
            Fixed y = Fixed.FromInt(r.Range(30, 226));
            Fixed halfLen = Fixed.FromInt(r.Range(55, 120));
            Fixed tilt = Fixed.FromRatio(r.Range(-60, 61), 100);
            Fixed strength = Fixed.FromRatio(r.Range(55, 131), 100) * Fixed.FromRatio(s.FrontStrengthPct[season], 100);
            Fixed moist = Fixed.FromRatio(r.Range(45, 101), 100);
            Fixed anom = kind switch
            {
                FrontKind.Cold => -Fixed.FromRatio(r.Range(30, 91), 10),
                FrontKind.Warm => Fixed.FromRatio(r.Range(20, 61), 10),
                _ => -Fixed.FromRatio(r.Range(10, 31), 10),
            };
            long spawn = seasonStartHour + r.Range(0, TimeConst.DaysPerSeason * 24);
            // Lives until its trailing edge has left the east side of the continent.
            Fixed travel = Fixed.FromInt(Width) + W * 6 - x0 + Fixed.FromInt(10);
            int life = (travel / speed * 24).Floor() + 1;
            ulong waveSeed = DetRng.Next(Seed, key, (ulong)i + 1, 0xF3, 0);
            list[i] = new Front(key * 64 + i, spawn, life, kind, x0, y, tilt, speed, W, halfLen, strength, moist, anom, waveSeed);
        }
        return list;
    }

    /// <summary>Fronts affecting the continent at an hour. Cached for the latest hour only; results never depend on the cache.</summary>
    public Front[] ActiveFronts(long hour)
    {
        var last = _last;
        if (last.Hour == hour) return last.Fronts;
        long key = FloorDiv(hour, TimeConst.DaysPerSeason * 24L);
        var res = new List<Front>(12);
        for (int k = 0; k <= MaxSeasonsBack; k++)
            foreach (var f in SeasonFronts(key - k))
                if (f.ActiveAt(hour)) res.Add(f);
        var arr = res.ToArray();
        _last = new HourFronts(hour, arr);
        return arr;
    }

    /// <summary>Summed front influence at a cell and hour.</summary>
    public (Fixed Rain, Fixed Wind, Fixed Temp) Influence(int x, int y, long hour)
    {
        Fixed r = Fixed.Zero, w = Fixed.Zero, t = Fixed.Zero;
        foreach (var f in ActiveFronts(hour))
        {
            var (fr, fw, ft) = f.Sample(x, y, hour);
            r += fr; w += fw; t += ft;
        }
        return (r, w, t);
    }
}
