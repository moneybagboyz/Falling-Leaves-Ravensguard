using System.Collections.Concurrent;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen;
using static Ravensguard.Sim.Weather.WeatherMath;

namespace Ravensguard.Sim.Weather;

public enum WeatherState : byte { Clear, Overcast, LightRain, HeavyRain, Thunderstorm, Fog, Frost, Snow, Blizzard, Heatwave }

/// <summary>The layers of ambient temperature (plan §3c), all in degrees C. Elevation lapse is already inside <see cref="Climate"/>.</summary>
public readonly record struct TempLayers(Fixed Climate, Fixed Seasonal, Fixed Daily, Fixed Synoptic, Fixed Front, Fixed Long)
{
    public Fixed Total => Climate + Seasonal + Daily + Synoptic + Front + Long;
}

/// <summary>Weather at one continent cell at one game hour.</summary>
public readonly record struct WeatherSample(
    WeatherState State, Fixed TempC, Fixed PrecipMmH, bool PrecipIsSnow, Fixed Cloud,
    int WindFromDeg, Fixed WindMps, Fixed FogDensity, Fixed SnowDepthCm, int RiverHighDays, bool FordClosed, TempLayers Layers)
{
    public bool Raining => PrecipMmH.Raw > 0 && !PrecipIsSnow;
}

/// <summary>
/// Turns the continental fronts into hourly weather for a cell (plan §3c). Everything is a pure function of
/// (seed, cell, hour); internal caches only memoise values that are themselves pure.
/// </summary>
public sealed class RegionWeather
{
    private const int N = Continent.Size;
    private readonly Continent _c;
    private readonly ContinentalWeather _w;
    private readonly WeatherSettings _s;
    private readonly ulong _seed;
    private readonly byte[] _nearWater = new byte[Continent.Cells];
    private readonly ConcurrentDictionary<long, DaySummary> _days = new();
    private readonly ConcurrentDictionary<long, int> _snow = new();
    private readonly ConcurrentDictionary<long, int> _river = new();
    private readonly Fixed _snowBelow, _heatwave, _heavyMmH;

    public ContinentalWeather Fronts => _w;
    public Continent Continent => _c;

    public RegionWeather(Continent c, ContinentalWeather w)
    {
        _c = c; _w = w; _s = w.Settings; _seed = w.Seed;
        _snowBelow = Fixed.FromRatio(_s.SnowBelowTenths, 10);
        _heatwave = Fixed.FromRatio(_s.HeatwaveTenths, 10);
        _heavyMmH = Fixed.FromRatio(_s.HeavyRainMmHTenths, 10);
        for (int i = 0; i < Continent.Cells; i++)
        {
            if (c.Ocean[i] || c.Lake[i]) continue;
            int x = i % N, y = i / N;
            bool near = c.Biomes[i] is Biome.Marsh or Biome.RiverValley || c.RiverFlow[i] >= 70;
            for (int dy = -2; dy <= 2 && !near; dy++)
                for (int dx = -2; dx <= 2 && !near; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= N || ny >= N) continue;
                    int j = ny * N + nx;
                    near = c.Ocean[j] || c.Lake[j];
                }
            _nearWater[i] = near ? (byte)1 : (byte)0;
        }
    }

    /// <summary>
    /// Weather for an already generated continent (no regeneration): the weather seed defaults to the continent's seed, which is what a world
    /// made from that seed uses. <paramref name="settings"/> defaults to the built-in weather settings.
    /// </summary>
    public static RegionWeather ForContinent(Continent c, ulong? seed = null, WeatherSettings? settings = null)
        => new(c, new ContinentalWeather(seed ?? c.Settings.Seed, settings));

    public bool IsNearWater(int cell) => _nearWater[cell] != 0 || _c.Ocean[cell] || _c.Lake[cell];

    // ---------------- temperature ----------------

    private static Fixed DayPos(long hour) => Fixed.FromRatio((int)(((hour % 1440) + 1440) % 1440), 24) + Fixed.Half;

    public int LatitudeTenths(int cell) => Daylight.LatitudeTenths(_s, cell / N);

    /// <summary>Seasonal swing amplitude in C: bigger in the north and far from the sea.</summary>
    private Fixed SeasonalAmp(int cell)
    {
        int y = cell / N;
        int tenths = _s.SeasonalAmpBaseTenths + _s.SeasonalAmpNorthExtraTenths * (N - 1 - y) / (N - 1);
        int coast = Math.Clamp((int)_c.CoastDistance[cell], 0, 20);
        return Fixed.FromRatio(tenths, 10) + Fixed.FromRatio(coast, 5);
    }

    /// <summary>The layered ambient temperature: climate + seasonal + daily + synoptic + fronts (+ long conditions).</summary>
    public TempLayers Temperature(int cell, long hour, Fixed cloud, (Fixed Rain, Fixed Wind, Fixed Temp) front, LongCondition lc)
    {
        int x = cell % N, y = cell / N;
        Fixed climate = Fixed.FromRatio(_c.Temperature[cell], 10);
        // Seasonal curve peaks 3 days after midsummer, and the coldest day 3 days after midwinter.
        Fixed seasonal = SinDeg(Daylight.Phase(DayPos(hour) - Fixed.FromInt(3))) * SeasonalAmp(cell);
        int hod = (int)(((hour % 24) + 24) % 24);
        Fixed dailyAmp = Fixed.FromRatio(_s.DailyAmpTenths, 10) * (Fixed.One - cloud * F(35, 100));
        Fixed daily = SinDeg(Fixed.FromInt(15 * (hod - 9))) * dailyAmp;
        Fixed syn = (Noise3(_seed ^ 0x51, x * 16, y * 16, hour, 960, 960, 72) - Fixed.Half) * 2 * Fixed.FromRatio(_s.SynopticAmpTenths, 10);
        int doy = (int)(((hour / 24) % 60 + 60) % 60) + 1;
        return new TempLayers(climate, seasonal, daily, syn, front.Temp, LongConditions.TempOffset(lc, doy));
    }

    // ---------------- hourly atmosphere ----------------

    private struct Atmos
    {
        public Fixed Rain, Wind, FrontTemp, Cloud, PrecipMmH, Bg, WindMps;
        public TempLayers Layers;
        public bool Snow;
        public LongCondition Long;
        public int Season, Doy, Culture;
        public Biome Biome;
    }

    private Atmos Atmosphere(int cell, long hour)
    {
        int x = cell % N, y = cell / N;
        long day = FloorDiv(hour, 24);
        var a = new Atmos { Doy = (int)(((day % 60) + 60) % 60) + 1 };
        a.Season = (a.Doy - 1) / 15;
        a.Biome = _c.Biomes[cell];
        a.Culture = _c.CultureId[cell];
        int year = (int)FloorDiv(day, 60);
        a.Long = _w.Long.For(a.Culture, year);
        var row = _s.Row(a.Biome);

        var f = _w.Influence(x, y, hour);
        a.Rain = f.Rain; a.Wind = f.Wind; a.FrontTemp = f.Temp;
        a.Bg = Noise3(_seed ^ 0xB6, x * 16 - hour * 3, y * 16 + hour, hour, 160, 160, 18);

        long wetPm = Math.Clamp((long)_c.Rainfall[cell] * 1000 / 700, 120, 1800);
        wetPm = wetPm * row.RainScalePct / 100 * row.SeasonRainPct[a.Season] / 100 * LongConditions.RainPct(a.Long, a.Doy) / 100;
        Fixed wet = Fixed.FromRatio((int)wetPm, 1000);

        Fixed cloud = F(28, 100) + a.Rain * F(60, 100) + (a.Bg - Fixed.Half) * F(80, 100) + Fixed.FromRatio(row.CloudBiasPermille[a.Season], 1000);
        cloud = Clamp01(cloud);

        Fixed conv = Fixed.Zero;
        if (a.Season is 0 or 1)
        {
            Fixed b2 = Noise3(_seed ^ 0xC7, x * 16 - hour * 4, y * 16, hour, 64, 64, 6);
            conv = Fixed.Max(Fixed.Zero, b2 - F(80, 100)) * 3;
        }
        Fixed p = (a.Rain + conv) * wet;
        a.PrecipMmH = Fixed.Max(Fixed.Zero, p - F(22, 100)) * 5;
        if (a.PrecipMmH.Raw > 0) cloud = Fixed.One;
        a.Cloud = cloud;

        a.Layers = Temperature(cell, hour, cloud, f, a.Long);
        a.Snow = a.Layers.Total <= _snowBelow;

        Fixed ws = Fixed.FromInt(2) + a.Bg * 3 + a.Wind * 5 + (a.Season == 3 ? Fixed.One : Fixed.Zero) + (a.Biome == Biome.Mountain ? Fixed.FromInt(2) : Fixed.Zero);
        a.WindMps = ws;
        return a;
    }

    private int WindFrom(int cell, long hour, in Atmos a)
    {
        int x = cell % N, y = cell / N;
        Fixed n = Noise3(_seed ^ 0xD8, x * 16, y * 16, hour, 480, 480, 30);
        int dir = 270 + ((n - Fixed.Half) * 100).Floor();
        if (a.FrontTemp.Raw < -(Fixed.OneRaw / 2)) dir += 35;      // north-westerly behind a cold front
        else if (a.FrontTemp.Raw > Fixed.OneRaw / 4) dir -= 35;    // south-westerly in the warm sector
        return ((dir % 360) + 360) % 360;
    }

    // ---------------- daily summaries, snow, rivers ----------------

    private readonly record struct DaySummary(Fixed RainMm, Fixed SnowMm, Fixed MeanTemp);

    private static long DayKey(int cell, long day) => (day << 20) | (uint)cell;

    /// <summary>
    /// Trims a full cache to the days a query near <paramref name="day"/> can still ask for (the snow chain reaches back two midsummers, at most 130 days), instead of
    /// clearing it: a cleared snow cache made the next winter query re-chain 120 days for every cell, a 160 ms hour (integration decision I40). Values are pure functions
    /// of the cell and day, so what the cache holds never changes a result.
    /// </summary>
    private static void Prune<T>(ConcurrentDictionary<long, T> cache, long day)
    {
        long keepFrom = day - 130;
        foreach (var k in cache.Keys) if ((k >> 20) < keepFrom || (k >> 20) > day + 130) cache.TryRemove(k, out _);
        if (cache.Count > 400_000) cache.Clear();
    }

    private DaySummary Day(int cell, long day)
    {
        long key = DayKey(cell, day);
        if (_days.TryGetValue(key, out var d)) return d;
        Fixed rain = Fixed.Zero, snow = Fixed.Zero, t = Fixed.Zero;
        for (int h = 2; h < 24; h += 4)
        {
            var a = Atmosphere(cell, day * 24 + h);
            Fixed mm = a.PrecipMmH * 4;
            if (a.Snow) snow += mm; else rain += mm;
            t += a.Layers.Total;
        }
        d = new DaySummary(rain, snow, t / 6);
        if (_days.Count > 400_000) Prune(_days, day);
        _days[key] = d;
        return d;
    }

    /// <summary>Day of the year index (0-based) of midsummer, the day the snow chain restarts from.</summary>
    private const int MidsummerIndex = 22;

    /// <summary>
    /// First day of the chain that ends at <paramref name="day"/>: two midsummers back from the latest one. Midsummer snow is gone everywhere
    /// except permanent snowfields, and a depth that hits zero erases its history, so the chain agrees with an endless one and depth is continuous
    /// from day to day (the cycle boundary only moves the start of a chain that has long since melted out).
    /// </summary>
    private static long ChainStart(long day)
    {
        long m = FloorDiv(day, 60) * 60 + MidsummerIndex;
        if (day < m) m -= 60;
        return m - 60;
    }

    /// <summary>Snow depth in cm at the end of a day, chained from a start fixed by the day alone so it never depends on cache contents.</summary>
    public Fixed SnowDepthEndOfDay(int cell, long day)
    {
        // Cells whose coldest plausible day stays well above freezing never hold snow.
        Fixed floor = Fixed.FromRatio(_c.Temperature[cell], 10) - SeasonalAmp(cell) - Fixed.FromInt(10);
        if (floor > Fixed.FromInt(4)) return Fixed.Zero;
        if (_snow.TryGetValue(DayKey(cell, day), out var cached)) return Fixed.FromRaw(cached);
        long start = ChainStart(day);
        // resume from the latest cached day of this same chain (cached values of other chains are keyed by their own days only)
        long from = start; Fixed depth = Fixed.Zero;
        for (long d = day - 1; d >= start; d--)
            if (_snow.TryGetValue(DayKey(cell, d), out var c) && ChainStart(d) == start) { from = d + 1; depth = Fixed.FromRaw(c); break; }
        for (long d = from; d <= day; d++)
        {
            var s = Day(cell, d);
            depth = depth * F(97, 100) + s.SnowMm;                 // 1 mm water equivalent ~ 1 cm of fresh snow, settling 3% a day
            if (s.MeanTemp > Fixed.Zero) depth -= s.MeanTemp * F(15, 10) + s.RainMm * F(3, 10);
            depth = Fixed.Clamp(depth, Fixed.Zero, Fixed.FromInt(200));
            if (ChainStart(d) == start) _snow[DayKey(cell, d)] = depth.Raw;
        }
        if (_snow.Count > 400_000) Prune(_snow, day);
        return depth;
    }

    /// <summary>
    /// Pure rule for river-high: given daily rain totals for the days before today (index 0 = yesterday),
    /// returns the length of the heavy-rain run if it ended within the recede window, else 0.
    /// </summary>
    public static int HeavyRun(ReadOnlySpan<Fixed> dailyRainMm, int heavyDayMm, int recedeDays)
    {
        Fixed thr = Fixed.FromInt(heavyDayMm);
        int start = -1;
        for (int i = 0; i <= recedeDays && i < dailyRainMm.Length; i++)
            if (dailyRainMm[i] >= thr) { start = i; break; }
        if (start < 0) return 0;
        int run = 0;
        for (int i = start; i < dailyRainMm.Length && dailyRainMm[i] >= thr; i++) run++;
        return run;
    }

    public int RiverHighDays(int cell, long day)
    {
        long key = DayKey(cell, day);
        if (_river.TryGetValue(key, out int v)) return v;
        Span<Fixed> hist = stackalloc Fixed[12];
        for (int i = 0; i < hist.Length; i++) hist[i] = Day(cell, day - 1 - i).RainMm;
        v = HeavyRun(hist, _s.HeavyDayMm, _s.FordRecedeDays);
        if (_river.Count > 400_000) Prune(_river, day);
        _river[key] = v;
        return v;
    }

    /// <summary>Total rain (mm) on a game day, as used for the river rule. Exposed for tools and tests.</summary>
    public Fixed DailyRainMm(int cell, long day) => Day(cell, day).RainMm;
    public Fixed DailyMeanTemp(int cell, long day) => Day(cell, day).MeanTemp;
    /// <summary>Snowfall (cm of fresh snow, 1 mm water = 1 cm) on a game day. Exposed for tools and tests.</summary>
    public Fixed DailySnowCm(int cell, long day) => Day(cell, day).SnowMm;

    // ---------------- the public query ----------------

    public WeatherSample At(int cell, GameTime t) => At(cell, t.TotalHours);

    public WeatherSample At(int cell, long gameHour) => AtCore(cell, gameHour, true);

    /// <summary>Same as <see cref="At(int,long)"/> but skips snow cover and river history (cheap; for whole-continent maps).</summary>
    public WeatherSample AtLite(int cell, long gameHour) => AtCore(cell, gameHour, false);

    private WeatherSample AtCore(int cell, long gameHour, bool full)
    {
        var a = Atmosphere(cell, gameHour);
        var row = _s.Row(a.Biome);
        long day = FloorDiv(gameHour, 24);
        int hod = (int)(gameHour - day * 24);
        Fixed temp = a.Layers.Total;
        Fixed wind = a.WindMps;
        int windDir = WindFrom(cell, gameHour, a);
        int lat = LatitudeTenths(cell);

        // Fog: coherent patches per day; morning window around sunrise; calm, dry air near water.
        Fixed fog = Fixed.Zero;
        if (a.PrecipMmH.Raw == 0 && wind < Fixed.FromInt(5))
        {
            int sunriseH = Daylight.SunriseMinute(lat, a.Doy) / 60;
            Fixed centre = Fixed.FromInt(sunriseH) + F(5, 10);
            Fixed prof = Tri(Fixed.FromInt(hod) + F(5, 10), centre, Fixed.FromInt(3));
            if (prof.Raw > 0)
            {
                int x = cell % N, y = cell / N;
                int chance = row.FogDayPct[a.Season] * (IsNearWater(cell) ? 100 : 22) / 100;
                if (Pct(_seed ^ 0xF06, x / 4, y / 4, day) < chance) fog = Clamp01(prof * F(15, 10));
            }
        }

        Fixed snowDepth = full ? SnowDepthEndOfDay(cell, day - 1) : Fixed.Zero;
        bool snowFall = a.Snow && a.PrecipMmH.Raw > 0;
        if (snowFall && full) snowDepth += Fixed.Min(a.PrecipMmH, Fixed.FromInt(3));
        int riverDays = full ? RiverHighDays(cell, day) : 0;
        bool ford = riverDays >= _s.FordClosedDays;

        WeatherState st;
        if (a.PrecipMmH.Raw > 0)
        {
            if (snowFall)
                st = (a.PrecipMmH >= Fixed.One && wind >= Fixed.FromInt(9)) ? WeatherState.Blizzard : WeatherState.Snow;
            else if (a.PrecipMmH >= F(15, 10) && temp >= Fixed.FromInt(10) && Pct(_seed ^ 0x7A, cell, day, 0) < row.ThunderPct[a.Season] * 3)
                st = WeatherState.Thunderstorm;
            else st = a.PrecipMmH >= _heavyMmH ? WeatherState.HeavyRain : WeatherState.LightRain;
        }
        else if (fog >= F(35, 100)) st = WeatherState.Fog;
        else if (temp >= _heatwave && a.Layers.Synoptic + a.Layers.Front >= F(15, 10)) st = WeatherState.Heatwave;
        else if (temp <= Fixed.Zero) st = WeatherState.Frost;
        else if (a.Cloud >= F(70, 100)) st = WeatherState.Overcast;
        else st = WeatherState.Clear;

        return new WeatherSample(st, temp, a.PrecipMmH, a.Snow, a.Cloud, windDir, wind, fog, snowDepth, riverDays, ford, a.Layers);
    }

    /// <summary>
    /// SHA-256 over the full weather sample (state, temperature layers, precipitation, cloud, wind, fog, snow depth, river state) of every cell at every hour,
    /// for determinism tests and the `hashes` tool. Cell-major, hours in the order given.
    /// </summary>
    public string SampleHash(IEnumerable<int> cells, IReadOnlyList<long> hours)
    {
        using var h = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        foreach (int cell in cells)
            foreach (long hour in hours)
            {
                var w = At(cell, hour);
                L(cell); L(hour); L((long)w.State); L(w.TempC.Raw); L(w.PrecipMmH.Raw); L(w.PrecipIsSnow ? 1 : 0); L(w.Cloud.Raw); L(w.WindFromDeg); L(w.WindMps.Raw);
                L(w.FogDensity.Raw); L(w.SnowDepthCm.Raw); L(w.RiverHighDays); L(w.FordClosed ? 1 : 0);
                L(w.Layers.Climate.Raw); L(w.Layers.Seasonal.Raw); L(w.Layers.Daily.Raw); L(w.Layers.Synoptic.Raw); L(w.Layers.Front.Raw); L(w.Layers.Long.Raw);
            }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    /// <summary>
    /// The standard determinism sample for an embark window: four cells (the window centre and three more, land or not) at eight hours (dawn and dusk on
    /// days 8, 23, 38 and 53 of year 3), hashed with <see cref="SampleHash"/>. Used by the `hashes` tool and the golden-hash tests.
    /// </summary>
    public string StandardSampleHash(int embarkCellX, int embarkCellY)
    {
        int[] cells = [(embarkCellY + 8) * N + embarkCellX + 8, (embarkCellY + 3) * N + embarkCellX + 3, (embarkCellY + 5) * N + embarkCellX + 12, (embarkCellY + 12) * N + embarkCellX + 5];
        var hours = new List<long>();
        foreach (int doy in new[] { 8, 23, 38, 53 })
            foreach (int hod in new[] { 6, 18 })
                hours.Add(((long)3 * 60 + doy - 1) * 24 + hod);
        return SampleHash(cells, hours);
    }

    /// <summary>Chronicle-style cause text for a closed ford, e.g. "Ford closed: river high after three days of rain".</summary>
    public static string FordCause(int days) => $"Ford closed: river high after {(days is >= 0 and <= 9 ? new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" }[days] : days.ToString())} days of rain";
}
