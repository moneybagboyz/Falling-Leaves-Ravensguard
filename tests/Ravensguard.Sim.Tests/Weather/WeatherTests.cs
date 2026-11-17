using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Weather;

public class WeatherTests(ITestOutputHelper output)
{
    private const int N = Continent.Size;
    private static readonly object Gate = new();
    private static Continent? _cont;
    private static Continent Cont
    {
        get
        {
            lock (Gate)
            {
                if (_cont != null) return _cont;
                var s = ContinentSettings.LoadDefaults(ContinentSettings.FindBaseContent());
                s.Seed = 1234; s.Layout = ContinentLayout.Continents;
                return _cont = ContinentGenerator.Generate(s);
            }
        }
    }

    private static WeatherSettings Settings() => WeatherSettings.LoadDefaults(ContinentSettings.FindBaseContent());
    private static RegionWeather Make(ulong seed = 77) => new(Cont, new ContinentalWeather(seed, Settings()));

    private static long Hour(int year, int doy, int hod) => ((long)year * 60 + doy - 1) * 24 + hod;

    private static IEnumerable<int> LandCells(Func<int, bool> pred, int step = 1)
    {
        for (int i = 0; i < Continent.Cells; i += step) if (!Cont.Ocean[i] && !Cont.Lake[i] && pred(i)) yield return i;
    }

    // ---------- calendar and daylight ----------

    [Fact]
    public void Content_settings_load_through_registered_def()
    {
        var s = Settings();
        Assert.Equal(55, s.Biomes[Biome.Desert].RainScalePct);
        Assert.Equal(4, s.FrontCountMin[0]);
    }

    [Fact]
    public void Calendar_names_and_feasts()
    {
        var cal = new Calendar();
        Assert.Equal(12, Calendar.MonthsPerYear);
        Assert.Equal("Thawmoon", cal.MonthName(1));
        Assert.Equal("Wolfmoon", cal.MonthName(60));
        Assert.Equal("Midwinter Feast", cal.FeastOn(53)!.Name);
        cal.SetNames(3, new CalendarNames(Enumerable.Range(1, 12).Select(i => $"M{i}").ToArray(), [new Feast("Ashfall", 10)]));
        Assert.Equal("M3", cal.MonthName(11, 3));
        Assert.Equal("Ashfall", cal.NextFeast(2, 3).Feast.Name);
        Assert.Contains("year 2", cal.Describe(GameTime.FromCalendar(2, Season.Winter, 8)));
    }

    [Fact]
    public void Day_length_mid_north_is_16h_summer_8h_winter_and_equal_at_equinox()
    {
        int lat = 480;
        double summer = Daylight.DayLengthMinutes(lat, Calendar.SummerSolsticeDay) / 60.0;
        double winter = Daylight.DayLengthMinutes(lat, Calendar.WinterSolsticeDay) / 60.0;
        double spring = Daylight.DayLengthMinutes(lat, Calendar.SpringEquinoxDay) / 60.0;
        double autumn = Daylight.DayLengthMinutes(lat, Calendar.AutumnEquinoxDay) / 60.0;
        output.WriteLine($"summer {summer:0.0} winter {winter:0.0} spring {spring:0.0} autumn {autumn:0.0}");
        Assert.InRange(summer, 15, 17);
        Assert.InRange(winter, 7, 9);
        Assert.InRange(spring, 11, 13);
        Assert.InRange(autumn, 11, 13);
        Assert.Equal(Daylight.SunriseMinute(lat, 23), 720 - Daylight.DayLengthMinutes(lat, 23) / 2);
        int mid = Daylight.LatitudeTenths(Settings(), 128);
        Assert.InRange(Daylight.DayLengthMinutes(mid, 23) / 60.0, 15, 17);
        Assert.True(Daylight.DayLengthMinutes(600, 23) > Daylight.DayLengthMinutes(400, 23));
        Assert.True(Daylight.DayLengthMinutes(600, 53) < Daylight.DayLengthMinutes(400, 53));
    }

    // ---------- determinism ----------

    [Fact]
    public void Same_seed_and_date_same_weather_regardless_of_order_and_cache()
    {
        var cells = LandCells(_ => true, 997).Take(40).ToArray();
        var hours = Enumerable.Range(0, 30).Select(i => Hour(3, 5, 0) + i * 37).ToArray();
        var a = Make(5);
        var forward = new Dictionary<(int, long), WeatherSample>();
        foreach (var c in cells) foreach (var h in hours) forward[(c, h)] = a.At(c, h);
        var b = Make(5);
        foreach (var h in hours.Reverse()) foreach (var c in cells.Reverse())
            Assert.Equal(forward[(c, h)], b.At(c, h));
        var other = Make(6);
        Assert.Contains(cells, c => hours.Any(h => forward[(c, h)] != other.At(c, h)));
    }

    // ---------- temperature ----------

    private static double Avg(RegionWeather w, int cell, int year, int d0, int d1)
    {
        double sum = 0; int n = 0;
        for (int d = d0; d <= d1; d++) for (int h = 0; h < 24; h += 3) { sum += w.At(cell, Hour(year, d, h)).TempC.ToDouble(); n++; }
        return sum / n;
    }

    [Fact]
    public void Winter_is_colder_than_summer_everywhere_sampled()
    {
        var w = Make();
        int colder = 0, n = 0;
        foreach (int c in LandCells(_ => true, 311))
        {
            double s = Avg(w, c, 2, 18, 28), wi = Avg(w, c, 2, 48, 58);
            n++; if (wi < s - 4) colder++;
        }
        Assert.True(colder == n, $"{colder}/{n}");
    }

    [Fact]
    public void Mountains_are_colder_by_lapse_rate()
    {
        var w = Make();
        double err = 0; int n = 0; double raw = 0;
        for (int y = 5; y < 250 && n < 25; y += 3)
            for (int x = 5; x < 245 && n < 25; x++)
            {
                int hi = y * N + x;
                if (Cont.Ocean[hi] || Cont.Biomes[hi] != Biome.Mountain) continue;
                int lo = y * N + x + 4;
                if (Cont.Ocean[lo] || Cont.Lake[lo]) continue;
                double dh = (Cont.Elevation[hi] - Cont.Elevation[lo]) * 4500.0 / (65536 - Cont.SeaLevelRaw);
                if (dh < 800) continue;
                double dt = Avg(w, hi, 1, 20, 21) - Avg(w, lo, 1, 20, 21);
                err += dt + 6.5 * dh / 1000; raw += dt; n++;
            }
        Assert.True(n >= 5, $"only {n} pairs");
        output.WriteLine($"{n} pairs, mean dT {raw / n:0.0} C, mean error vs lapse {err / n:0.0} C");
        Assert.True(raw / n < -4);
        Assert.InRange(err / n, -2.5, 2.5);
    }

    // ---------- precipitation, snow, fog ----------

    private static (int rainy, int snowy) CountDays(RegionWeather w, int cell, int years)
    {
        int rainy = 0, snowy = 0;
        for (int y = 0; y < years; y++)
            for (int d = 1; d <= 60; d++)
            {
                bool r = false, s = false;
                for (int h = 0; h < 24; h += 2)
                {
                    var q = w.At(cell, Hour(y, d, h));
                    if (q.PrecipMmH.Raw > 0) { if (q.PrecipIsSnow) s = true; else r = true; }
                }
                if (r) rainy++; if (s) snowy++;
            }
        return (rainy, snowy);
    }

    [Fact]
    public void Deserts_rarely_rain_forests_and_coasts_are_wetter()
    {
        var w = Make();
        var deserts = LandCells(i => Cont.Biomes[i] == Biome.Desert).Where((_, k) => k % 40 == 0).Take(6).ToArray();
        var forests = LandCells(i => Cont.Biomes[i] == Biome.TemperateForest).Where((_, k) => k % 200 == 0).Take(6).ToArray();
        Assert.NotEmpty(deserts); Assert.NotEmpty(forests);
        double d = deserts.Average(c => CountDays(w, c, 3).rainy) / 3.0;
        double f = forests.Average(c => CountDays(w, c, 3).rainy) / 3.0;
        output.WriteLine($"rainy days/yr: desert {d:0.0}, forest {f:0.0}");
        Assert.True(d < f * 0.5, $"desert {d} forest {f}");
        Assert.True(d < 12);
        var coast = LandCells(i => Cont.CoastDistance[i] <= 2 && i / N is > 90 and < 170).Where((_, k) => k % 15 == 0).Take(10).ToArray();
        var inland = LandCells(i => Cont.CoastDistance[i] >= 25 && i / N is > 90 and < 170 && Cont.Biomes[i] != Biome.Mountain).Where((_, k) => k % 15 == 0).Take(10).ToArray();
        if (coast.Length > 3 && inland.Length > 3)
        {
            double cr = coast.Average(c => Cont.Rainfall[c]), ir = inland.Average(c => Cont.Rainfall[c]);
            double cd = coast.Average(c => CountDays(w, c, 2).rainy) / 2.0, id = inland.Average(c => CountDays(w, c, 2).rainy) / 2.0;
            output.WriteLine($"coast {cr:0} mm {cd:0.0} d; inland {ir:0} mm {id:0.0} d");
            if (cr > ir * 1.1) Assert.True(cd > id);
        }
    }

    [Fact]
    public void Snow_only_when_cold_and_rain_only_when_not_freezing()
    {
        var w = Make();
        int snow = 0, rain = 0;
        foreach (int c in LandCells(_ => true, 523))
            for (int d = 1; d <= 60; d += 2)
                for (int h = 0; h < 24; h += 4)
                {
                    var q = w.At(c, Hour(1, d, h));
                    if (q.State is WeatherState.Snow or WeatherState.Blizzard) { snow++; Assert.True(q.TempC <= Fixed.FromRatio(11, 10), $"snow at {q.TempC}"); }
                    if (q.State is WeatherState.LightRain or WeatherState.HeavyRain or WeatherState.Thunderstorm) { rain++; Assert.True(q.TempC > Fixed.One); }
                    if (!q.PrecipIsSnow && q.PrecipMmH.Raw > 0) Assert.True(q.TempC > Fixed.One);
                }
        output.WriteLine($"snow samples {snow}, rain samples {rain}");
        Assert.True(snow > 0 && rain > 0);
    }

    [Fact]
    public void Fog_is_more_common_near_water_and_in_the_morning()
    {
        var w = Make();
        var near = LandCells(i => w.IsNearWater(i) && i / N is > 60 and < 190).Where((_, k) => k % 150 == 0).Take(25).ToArray();
        var far = LandCells(i => !w.IsNearWater(i) && Cont.CoastDistance[i] > 8 && i / N is > 60 and < 190).Where((_, k) => k % 150 == 0).Take(25).ToArray();
        int fogNear = 0, fogFar = 0, morning = 0, afternoon = 0;
        foreach (int c in near) for (int d = 1; d <= 60; d++) for (int h = 0; h < 24; h++)
        {
            if (w.At(c, Hour(1, d, h)).State != WeatherState.Fog) continue;
            fogNear++; if (h >= 3 && h <= 9) morning++; if (h >= 12 && h <= 18) afternoon++;
        }
        foreach (int c in far) for (int d = 1; d <= 60; d++) for (int h = 0; h < 24; h++)
            if (w.At(c, Hour(1, d, h)).State == WeatherState.Fog) fogFar++;
        output.WriteLine($"fog hours/cell/yr: near water {fogNear / (double)near.Length:0.0}, inland {fogFar / (double)far.Length:0.0}; morning {morning}, afternoon {afternoon}");
        Assert.True(fogNear / (double)near.Length > 2 * fogFar / (double)far.Length);
        Assert.True(morning > 5 * Math.Max(1, afternoon));
        Assert.Equal(0, afternoon);
    }

    // ---------- fronts ----------

    [Fact]
    public void A_front_moves_east_over_consecutive_days()
    {
        var cw = new ContinentalWeather(31, Settings());
        var f = cw.SeasonFronts(8).OrderByDescending(x => x.Strength).First();
        int y = f.Y.Floor();
        var xs = new List<int>();
        for (int d = 0; d < 6; d++) xs.Add(f.CentreX(f.Y, f.SpawnHour + 24 * d).Floor());
        output.WriteLine($"front {f.Kind} centre x by day: {string.Join(',', xs)}");
        for (int i = 1; i < xs.Count; i++) Assert.True(xs[i] > xs[i - 1] + 10);
        int Peak(long hour) { int bx = 0; Fixed best = Fixed.Zero; for (int x = 0; x < N; x++) { var r = f.Sample(x, y, hour).Rain; if (r > best) { best = r; bx = x; } } return bx; }
        int p1 = Peak(f.SpawnHour + 24 * 6), p2 = Peak(f.SpawnHour + 24 * 8);
        Assert.True(p2 > p1);
        Assert.Equal(cw.SeasonFronts(8)[0], new ContinentalWeather(31, Settings()).SeasonFronts(8)[0]);
    }

    // ---------- rivers ----------

    [Theory]
    [InlineData(new[] { 20, 20, 20, 0, 0, 0 }, 3)]
    [InlineData(new[] { 0, 0, 14, 15, 13 }, 3)]
    [InlineData(new[] { 20, 20, 0, 0 }, 2)]
    [InlineData(new[] { 0, 0, 0, 20, 20, 20 }, 0)]
    public void Heavy_run_rule(int[] mm, int expectedRun)
    {
        var d = mm.Select(Fixed.FromInt).ToArray();
        Assert.Equal(expectedRun, RegionWeather.HeavyRun(d, 12, 2));
    }

    [Fact]
    public void Three_heavy_rain_days_close_fords_in_a_real_run()
    {
        var w = Make(11);
        int closedSeen = 0;
        var cells = LandCells(i => Cont.Rainfall[i] > 900 || Cont.Biomes[i] == Biome.RiverValley).Where((_, k) => k % 90 == 0).Take(40).ToArray();
        foreach (int c in cells)
            for (int day = 20 * 60; day < 24 * 60 && closedSeen < 3; day++)
            {
                int run = w.RiverHighDays(c, day);
                var s = w.At(c, (long)day * 24 + 12);
                Assert.Equal(run >= 3, s.FordClosed);
                if (!s.FordClosed) continue;
                closedSeen++;
                int k = 1; while (w.DailyRainMm(c, day - k) < Fixed.FromInt(12)) k++;
                for (int j = 0; j < 3; j++) Assert.True(w.DailyRainMm(c, day - k - j) >= Fixed.FromInt(12));
                output.WriteLine($"cell {c} day {day}: {RegionWeather.FordCause(run)}");
            }
        Assert.True(closedSeen > 0, "no ford closures found in wet cells across 4 years");
    }

    [Fact]
    public void Long_conditions_are_deterministic_and_occur()
    {
        var lc = new LongConditions(9, Settings());
        var kinds = new Dictionary<LongKind, int>();
        for (int y = 0; y < 400; y++) { var a = lc.For(2, y); Assert.Equal(a, lc.For(2, y)); kinds[a.Kind] = kinds.GetValueOrDefault(a.Kind) + 1; }
        output.WriteLine(string.Join(", ", kinds.Select(k => $"{k.Key}={k.Value}")));
        Assert.True(kinds.ContainsKey(LongKind.Drought) && kinds.ContainsKey(LongKind.WetYear) && kinds.ContainsKey(LongKind.ColdSummer));
        var dr = Enumerable.Range(0, 400).Select(y => lc.For(2, y)).First(c => c.Kind == LongKind.Drought);
        Assert.True(dr.EndDay - dr.StartDay >= 14);
        Assert.True(LongConditions.RainPct(dr, dr.StartDay) < 50);
    }

    // ---------- performance ----------

    [Fact]
    public void Region_of_256_tiles_per_hour_is_fast()
    {
        var w = Make();
        var cells = Enumerable.Range(0, 256).Select(i => (100 + i / 16) * N + 100 + i % 16).ToArray();
        for (int h = 0; h < 48; h++) foreach (var c in cells) w.At(c, Hour(1, 50, 0) + h);
        var sw = Stopwatch.StartNew();
        int hours = 200;
        for (int h = 0; h < hours; h++) foreach (var c in cells) w.At(c, Hour(1, 50, 0) + h % 48);
        double ms = sw.Elapsed.TotalMilliseconds / hours;
        output.WriteLine($"{ms:0.000} ms per game hour for 256 tiles (warm day caches)");
        sw.Restart();
        for (int h = 0; h < 24; h++) foreach (var c in cells) w.At(c, Hour(2, 10, 0) + h);
        output.WriteLine($"cold day: {sw.Elapsed.TotalMilliseconds / 24:0.000} ms per game hour averaged over a day");
#if DEBUG
        Assert.True(ms < 4.0, $"{ms} ms (debug build; the 1 ms budget applies to Release)");
#else
        Assert.True(ms < 1.0, $"{ms} ms");
#endif
    }

    // ---------- year statistics ----------

    [Fact]
    public void Year_stats_per_biome_are_plausible()
    {
        var w = Make();
        var rows = new List<string> { "biome            rainyD snowD  mean sp/su/au/wi C       fogH  thunderD" };
        var stats = new Dictionary<Biome, (double rainy, double snow, double[] t)>();
        foreach (var b in new[] { Biome.Desert, Biome.Steppe, Biome.Grassland, Biome.TemperateForest, Biome.Taiga, Biome.Tundra, Biome.Marsh, Biome.RiverValley, Biome.Mountain })
        {
            var cells = LandCells(i => Cont.Biomes[i] == b).ToArray();
            if (cells.Length == 0) continue;
            int np = Math.Min(6, cells.Length);
            var pick = Enumerable.Range(0, np).Select(k => cells[k * cells.Length / np]).ToArray();
            double rainy = 0, snow = 0, fog = 0, thunder = 0; var t = new double[4];
            int years = 2;
            foreach (int c in pick)
                for (int y = 1; y <= years; y++)
                    for (int d = 1; d <= 60; d++)
                    {
                        bool r = false, s = false, th = false;
                        for (int h = 0; h < 24; h++)
                        {
                            var q = w.At(c, Hour(y, d, h));
                            if (q.PrecipMmH.Raw > 0) { if (q.PrecipIsSnow) s = true; else r = true; }
                            if (q.State == WeatherState.Thunderstorm) th = true;
                            if (q.State == WeatherState.Fog) fog++;
                            t[(d - 1) / 15] += q.TempC.ToDouble();
                        }
                        if (r) rainy++; if (s) snow++; if (th) thunder++;
                    }
            double k2 = pick.Length * years;
            for (int i = 0; i < 4; i++) t[i] /= k2 * 15 * 24;
            stats[b] = (rainy / k2, snow / k2, t);
            rows.Add($"{b,-16} {rainy / k2,6:0.0} {snow / k2,5:0.0}  {t[0],5:0.0}/{t[1],5:0.0}/{t[2],5:0.0}/{t[3],5:0.0}   {fog / k2,8:0.0}  {thunder / k2,5:0.0}");
        }
        foreach (var r in rows) output.WriteLine(r);
        foreach (var (b, s) in stats)
        {
            Assert.True(s.t[1] > s.t[3], $"{b} summer not warmer than winter");
            Assert.InRange(s.rainy, 0, 50);
        }
        if (stats.TryGetValue(Biome.TemperateForest, out var f)) Assert.InRange(f.rainy, 12, 40);
        if (stats.TryGetValue(Biome.Desert, out var de)) Assert.True(de.rainy < 10);
    }
}
