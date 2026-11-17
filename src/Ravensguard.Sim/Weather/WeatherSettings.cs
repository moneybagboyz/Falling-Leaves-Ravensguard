using Ravensguard.Content;
using Ravensguard.Sim.WorldGen;

namespace Ravensguard.Sim.Weather;

/// <summary>Seasonal table for one biome. Arrays have 4 entries: spring, summer, autumn, winter.</summary>
public sealed class BiomeWeatherRow
{
    public int RainScalePct = 100;
    public int[] SeasonRainPct = [100, 100, 100, 100];
    public int[] CloudBiasPermille = [0, 0, 0, 0];
    public int[] FogDayPct = [25, 20, 40, 30];
    public int[] ThunderPct = [10, 30, 8, 0];
}

/// <summary>Weather tunables. Defaults live here and in content/base/weather/weather.jsonc (type weather_settings).</summary>
public sealed class WeatherSettings
{
    public int LatitudeNorthTenths = 660, LatitudeSouthTenths = 300;
    public int SeasonalAmpBaseTenths = 70, SeasonalAmpNorthExtraTenths = 70;
    public int DailyAmpTenths = 60, SynopticAmpTenths = 25, HeatwaveTenths = 300, SnowBelowTenths = 10;
    public int[] FrontCountMin = [4, 3, 4, 5], FrontCountMax = [7, 6, 7, 8];
    public int[] ColdFrontPct = [40, 30, 45, 55], FrontStrengthPct = [95, 70, 100, 110];
    public int FrontSpeedMinCellsDay = 16, FrontSpeedMaxCellsDay = 30;
    public int DroughtPermilleYear = 70, WetYearPermilleYear = 70, ColdSummerPermilleYear = 60;
    public int HeavyRainMmHTenths = 25, HeavyDayMm = 12, FordClosedDays = 3, FordRecedeDays = 2;
    public Dictionary<Biome, BiomeWeatherRow> Biomes = DefaultBiomes();

    public BiomeWeatherRow Row(Biome b) => Biomes.TryGetValue(b, out var r) ? r : Biomes[Biome.Grassland];

    private static BiomeWeatherRow R(int scale, int[] rain, int[] cloud, int[] fog, int[] thunder)
        => new() { RainScalePct = scale, SeasonRainPct = rain, CloudBiasPermille = cloud, FogDayPct = fog, ThunderPct = thunder };

    public static Dictionary<Biome, BiomeWeatherRow> DefaultBiomes() => new()
    {
        [Biome.Desert] = R(55, [110, 60, 90, 140], [-150, -200, -150, -100], [4, 3, 6, 6], [6, 25, 4, 0]),
        [Biome.Steppe] = R(85, [120, 95, 90, 90], [-60, -80, -40, 0], [8, 6, 12, 10], [10, 30, 6, 0]),
        [Biome.Grassland] = R(100, [110, 95, 105, 90], [0, -20, 20, 40], [20, 15, 35, 28], [10, 30, 8, 0]),
        [Biome.TemperateForest] = R(100, [105, 90, 115, 100], [30, 0, 50, 70], [28, 20, 45, 35], [10, 28, 8, 0]),
        [Biome.Taiga] = R(90, [90, 115, 105, 90], [40, 20, 70, 90], [22, 20, 35, 25], [4, 18, 3, 0]),
        [Biome.Tundra] = R(75, [80, 110, 110, 80], [60, 40, 90, 70], [20, 25, 30, 25], [2, 10, 2, 0]),
        [Biome.Marsh] = R(110, [100, 100, 110, 90], [40, 20, 60, 60], [50, 38, 65, 50], [8, 25, 6, 0]),
        [Biome.RiverValley] = R(100, [105, 95, 105, 95], [10, 0, 30, 50], [45, 35, 60, 45], [10, 28, 8, 0]),
        [Biome.Mountain] = R(125, [100, 105, 100, 100], [60, 40, 70, 80], [20, 15, 25, 20], [14, 40, 8, 0]),
        [Biome.Lake] = R(100, [100, 90, 110, 100], [20, 0, 50, 60], [50, 38, 65, 50], [8, 25, 6, 0]),
        [Biome.Sea] = R(100, [100, 90, 110, 100], [20, 0, 50, 60], [50, 38, 65, 50], [8, 25, 6, 0]),
    };

    public static WeatherSettings FromDef(WeatherSettingsDef d)
    {
        int[] A(List<int> l, int[] fb) => l.Count == 4 ? [.. l] : fb;
        var s = new WeatherSettings
        {
            LatitudeNorthTenths = d.LatitudeNorthTenths, LatitudeSouthTenths = d.LatitudeSouthTenths,
            SeasonalAmpBaseTenths = d.SeasonalAmpBaseTenths, SeasonalAmpNorthExtraTenths = d.SeasonalAmpNorthExtraTenths,
            DailyAmpTenths = d.DailyAmpTenths, SynopticAmpTenths = d.SynopticAmpTenths, HeatwaveTenths = d.HeatwaveTenths,
            SnowBelowTenths = d.SnowBelowTenths,
            FrontCountMin = A(d.FrontCountMin, [4, 3, 4, 5]), FrontCountMax = A(d.FrontCountMax, [7, 6, 7, 8]),
            ColdFrontPct = A(d.ColdFrontPct, [40, 30, 45, 55]), FrontStrengthPct = A(d.FrontStrengthPct, [95, 70, 100, 110]),
            FrontSpeedMinCellsDay = d.FrontSpeedMinCellsDay, FrontSpeedMaxCellsDay = d.FrontSpeedMaxCellsDay,
            DroughtPermilleYear = d.DroughtPermilleYear, WetYearPermilleYear = d.WetYearPermilleYear,
            ColdSummerPermilleYear = d.ColdSummerPermilleYear, HeavyRainMmHTenths = d.HeavyRainMmHTenths,
            HeavyDayMm = d.HeavyDayMm, FordClosedDays = d.FordClosedDays, FordRecedeDays = d.FordRecedeDays,
        };
        foreach (var r in d.Biomes)
            if (Enum.TryParse<Biome>(r.Biome, true, out var b))
                s.Biomes[b] = new BiomeWeatherRow
                {
                    RainScalePct = r.RainScalePct, SeasonRainPct = A(r.SeasonRainPct, [100, 100, 100, 100]),
                    CloudBiasPermille = A(r.CloudBiasPermille, [0, 0, 0, 0]), FogDayPct = A(r.FogDayPct, [25, 20, 40, 30]),
                    ThunderPct = A(r.ThunderPct, [10, 30, 8, 0]),
                };
        return s;
    }

    /// <summary>Loads through the content pipeline (content/base); built-in defaults if absent.</summary>
    public static WeatherSettings LoadDefaults(string? baseModFolder)
    {
        if (baseModFolder != null && Directory.Exists(baseModFolder))
        {
            var r = new ModLoader(ContentTypeRegistry.Default).Load([baseModFolder]);
            if (r.Database.TryGet<WeatherSettingsDef>("weather_defaults", out var def)) return FromDef(def);
        }
        return new WeatherSettings();
    }
}
