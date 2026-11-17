using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;

namespace Ravensguard.WorldTool;

/// <summary>
/// weather: writes a year chart (state strip, temperature, precipitation, snow depth, daylight) for one cell
/// and a continent snapshot (weather overlay with fronts, plus a temperature map) at a date. Debug visualisation only.
/// </summary>
public static class WeatherCommand
{
    private const int N = Continent.Size;
    private static uint Rgb(int r, int g, int b) => (uint)((Math.Clamp(r, 0, 255) << 16) | (Math.Clamp(g, 0, 255) << 8) | Math.Clamp(b, 0, 255));

    private static uint StateColor(WeatherState s) => s switch
    {
        WeatherState.Clear => Rgb(250, 215, 90), WeatherState.Overcast => Rgb(150, 150, 155), WeatherState.LightRain => Rgb(110, 170, 230),
        WeatherState.HeavyRain => Rgb(40, 90, 200), WeatherState.Thunderstorm => Rgb(120, 50, 190), WeatherState.Fog => Rgb(205, 205, 215),
        WeatherState.Frost => Rgb(150, 220, 235), WeatherState.Snow => Rgb(245, 245, 250), WeatherState.Blizzard => Rgb(200, 190, 245),
        WeatherState.Heatwave => Rgb(235, 80, 50), _ => 0,
    };

    public static int Run(string[] args)
    {
        var settings = ContinentSettings.LoadDefaults(ContinentSettings.FindBaseContent());
        string outDir = "art/out/weather";
        int year = 3, day = 8, hour = 12, scale = 3;
        int? cx = null, cy = null;
        string? biomeArg = null;
        for (int i = 1; i + 1 < args.Length; i += 2)
        {
            string v = args[i + 1];
            switch (args[i])
            {
                case "--seed": settings.Seed = ulong.Parse(v); break;
                case "--layout": settings.Layout = Enum.Parse<ContinentLayout>(v, true); break;
                case "--out": outDir = v; break;
                case "--year": year = int.Parse(v); break;
                case "--day": day = int.Parse(v); break;
                case "--hour": hour = int.Parse(v); break;
                case "--scale": scale = int.Parse(v); break;
                case "--cell": { var p = v.Split(','); cx = int.Parse(p[0]); cy = int.Parse(p[1]); break; }
                case "--biome": biomeArg = v; break;
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 1;
            }
        }
        var c = ContinentGenerator.Generate(settings);
        var wset = WeatherSettings.LoadDefaults(ContinentSettings.FindBaseContent());
        var cw = new ContinentalWeather(settings.Seed, wset);
        var rw = new RegionWeather(c, cw);

        int cell = PickCell(c, cx, cy, biomeArg);
        Console.WriteLine($"cell {cell % N},{cell / N} {c.Biomes[cell]} mean {c.Temperature[cell] / 10.0:0.0} C, {c.Rainfall[cell]} mm, lat {rw.LatitudeTenths(cell) / 10.0:0.0}");
        string chart = Path.Combine(outDir, "weather.png");
        YearChart(rw, cell, year, chart);
        string snap = Path.Combine(outDir, "weather_continent.png");
        Snapshot(c, rw, cw, GameTime.FromCalendar(year, (Season)((day - 1) / 15), (day - 1) % 15 + 1, hour).TotalHours, scale, snap);
        var lc = cw.Long.For(c.CultureId[cell], year);
        Console.WriteLine($"year {year} long condition for culture {c.CultureId[cell]}: {lc.Kind} days {lc.StartDay}-{lc.EndDay}");
        Console.WriteLine($"-> {chart}\n-> {snap}");
        return 0;
    }

    private static int PickCell(Continent c, int? x, int? y, string? biome)
    {
        if (x != null && y != null) return y.Value * N + x.Value;
        var want = biome != null ? Enum.Parse<Biome>(biome, true) : Biome.TemperateForest;
        int best = -1, bd = int.MaxValue;
        for (int i = 0; i < Continent.Cells; i++)
        {
            if (c.Biomes[i] != want) continue;
            int d = Math.Abs(i % N - 128) + Math.Abs(i / N - 128);
            if (d < bd) { bd = d; best = i; }
        }
        if (best < 0) best = Enumerable.Range(0, Continent.Cells).First(i => !c.Ocean[i]);
        return best;
    }

    private static void Fill(uint[] px, int w, int x0, int y0, int ww, int hh, uint col)
    {
        for (int y = y0; y < y0 + hh; y++) for (int x = x0; x < x0 + ww; x++) if (x >= 0 && x < w && y >= 0 && y * w + x < px.Length) px[y * w + x] = col;
    }

    private static void YearChart(RegionWeather rw, int cell, int year, string path)
    {
        const int W = 1440, H = 330;
        var px = new uint[W * H];
        Array.Fill(px, Rgb(24, 26, 32));
        int yState = 4, hState = 18, yTemp = 28, hTemp = 130, yPre = 164, hPre = 60, ySnow = 230, hSnow = 40, yDay = 276, hDay = 48;
        Fill(px, W, 0, yTemp, W, hTemp, Rgb(34, 37, 46));
        Fill(px, W, 0, yPre, W, hPre, Rgb(34, 37, 46));
        Fill(px, W, 0, ySnow, W, hSnow, Rgb(34, 37, 46));
        Fill(px, W, 0, yDay, W, hDay, Rgb(20, 22, 40));
        // temperature -30..+40 C
        int TY(double t) => yTemp + hTemp - 1 - (int)Math.Clamp((t + 30) / 70.0 * (hTemp - 1), 0, hTemp - 1);
        Fill(px, W, 0, TY(0), W, 1, Rgb(90, 110, 140));
        long h0 = (long)year * 1440;
        int prevY = -1;
        for (int h = 0; h < 1440; h++)
        {
            var q = rw.At(cell, h0 + h);
            Fill(px, W, h, yState, 1, hState, StateColor(q.State));
            int ty = TY(q.TempC.ToDouble());
            uint tc = q.TempC.Raw < 0 ? Rgb(120, 190, 255) : Rgb(255, 170, 90);
            Fill(px, W, h, Math.Min(ty, prevY < 0 ? ty : prevY), 1, Math.Abs(ty - (prevY < 0 ? ty : prevY)) + 1, tc);
            prevY = ty;
            double mm = q.PrecipMmH.ToDouble();
            int bh = (int)Math.Min(hPre, mm * 14);
            if (bh > 0) Fill(px, W, h, yPre + hPre - bh, 1, bh, q.PrecipIsSnow ? Rgb(240, 240, 250) : Rgb(80, 150, 240));
            int sd = (int)Math.Min(hSnow, q.SnowDepthCm.ToDouble() * hSnow / 60.0);
            if (sd > 0) Fill(px, W, h, ySnow + hSnow - sd, 1, sd, Rgb(225, 235, 245));
            if (q.FordClosed) Fill(px, W, h, ySnow + hSnow - 3, 1, 3, Rgb(230, 60, 60));
            if (q.FogDensity.Raw > 0) Fill(px, W, h, yState + hState, 1, 3, Rgb(205, 205, 215));
        }
        int lat = rw.LatitudeTenths(cell);
        for (int d = 1; d <= 60; d++)
        {
            int sr = Daylight.SunriseMinute(lat, d), ss = Daylight.SunsetMinute(lat, d);
            int ya = yDay + sr * hDay / 1440, yb = yDay + ss * hDay / 1440;
            Fill(px, W, (d - 1) * 24, ya, 24, Math.Max(1, yb - ya), Rgb(250, 215, 90));
        }
        var lc = rw.Continent.CultureId[cell];
        var cond = rw.Fronts.Long.For(lc, year);
        if (cond.Kind != LongKind.None)
        {
            uint col = cond.Kind switch { LongKind.Drought => Rgb(230, 160, 40), LongKind.WetYear => Rgb(60, 130, 230), _ => Rgb(140, 200, 240) };
            Fill(px, W, (cond.StartDay - 1) * 24, 0, (cond.EndDay - cond.StartDay + 1) * 24, 3, col);
        }
        for (int s = 0; s < 4; s++) for (int y = 0; y < H; y++) px[y * W + Math.Min(W - 1, s * 360)] = Rgb(110, 115, 130);
        Png.Write(path, W, H, px);
    }

    private static uint BiomeCol(Biome b) => b switch
    {
        Biome.Sea => Rgb(30, 70, 120), Biome.Lake => Rgb(70, 130, 190), Biome.Tundra => Rgb(190, 200, 190),
        Biome.Taiga => Rgb(50, 100, 70), Biome.TemperateForest => Rgb(60, 130, 60), Biome.Grassland => Rgb(130, 170, 70),
        Biome.Steppe => Rgb(190, 180, 100), Biome.Desert => Rgb(225, 200, 130), Biome.Marsh => Rgb(90, 130, 110),
        Biome.Mountain => Rgb(140, 130, 120), Biome.RiverValley => Rgb(100, 180, 90), _ => 0,
    };

    private static void Snapshot(Continent c, RegionWeather rw, ContinentalWeather cw, long hour, int scale, string path)
    {
        int S = N * scale, W = S * 2 + 8;
        var px = new uint[W * S];
        Array.Fill(px, Rgb(24, 26, 32));
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                var q = rw.AtLite(i, hour);
                uint basec = BiomeCol(c.Biomes[i]);
                int br = (int)(basec >> 16 & 255) * 60 / 100, bg = (int)(basec >> 8 & 255) * 60 / 100, bb = (int)(basec & 255) * 60 / 100;
                // cloud: grey wash; precipitation: blue (rain) or white (snow); fog: pale
                int cl = (int)(q.Cloud.ToDouble() * 55);
                uint left = Rgb(br + (170 - br) * cl / 100, bg + (170 - bg) * cl / 100, bb + (175 - bb) * cl / 100);
                if (q.PrecipMmH.Raw > 0)
                {
                    int k = (int)Math.Min(100, 40 + q.PrecipMmH.ToDouble() * 25);
                    left = q.PrecipIsSnow ? Rgb(240, 240, 250) : Rgb(60 - k / 3, 110 - k / 3, 230 - k / 5);
                }
                else if (q.FogDensity.Raw > 0) left = Rgb(205, 205, 215);
                double t = q.TempC.ToDouble();
                uint right = c.Ocean[i] ? Rgb(30, 50, 90) : Ramp(t);
                for (int dy = 0; dy < scale; dy++) for (int dx = 0; dx < scale; dx++)
                {
                    px[(y * scale + dy) * W + x * scale + dx] = left;
                    px[(y * scale + dy) * W + S + 8 + x * scale + dx] = right;
                }
            }
        foreach (var f in cw.ActiveFronts(hour))
        {
            uint col = f.Kind switch { FrontKind.Cold => Rgb(40, 60, 255), FrontKind.Warm => Rgb(255, 40, 40), _ => Rgb(190, 40, 220) };
            for (int y = Math.Max(0, f.Y.Floor() - f.HalfLength.Floor()); y < Math.Min(N, f.Y.Floor() + f.HalfLength.Floor()); y++)
            {
                int x = f.CentreX(Fixed.FromInt(y), hour).Floor();
                if (x < 0 || x >= N) continue;
                for (int dy = 0; dy < scale; dy++) for (int dx = 0; dx < 2; dx++)
                {
                    px[(y * scale + dy) * W + x * scale + dx] = col;
                    px[(y * scale + dy) * W + S + 8 + x * scale + dx] = col;
                }
            }
        }
        Png.Write(path, W, S, px);
    }

    private static uint Ramp(double t)
    {
        double u = Math.Clamp((t + 25) / 60.0, 0, 1);
        if (u < 0.5) { double k = u * 2; return Rgb((int)(40 + 190 * k), (int)(60 + 170 * k), (int)(200 + 40 * k)); }
        double m = (u - 0.5) * 2; return Rgb((int)(230 + 25 * m), (int)(230 - 170 * m), (int)(240 - 200 * m));
    }
}
