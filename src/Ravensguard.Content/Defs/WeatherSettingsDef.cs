namespace Ravensguard.Content;

/// <summary>Seasonal weather table for one biome (plan §3c). Arrays are indexed spring, summer, autumn, winter.</summary>
public sealed record WeatherBiomeRowDef
{
    /// <summary>Biome name as in the Biome enum (Desert, Steppe, Grassland, TemperateForest, Taiga, Tundra, Marsh, RiverValley, Mountain, Sea, Lake).</summary>
    public required string Biome { get; init; }
    /// <summary>Overall precipitation multiplier for this biome, percent.</summary>
    public int RainScalePct { get; init; } = 100;
    /// <summary>Seasonal precipitation multiplier, percent, per season.</summary>
    public List<int> SeasonRainPct { get; init; } = [100, 100, 100, 100];
    /// <summary>Added to cloud cover, permille, per season.</summary>
    public List<int> CloudBiasPermille { get; init; } = [0, 0, 0, 0];
    /// <summary>Chance per day that a morning fog forms on cells near water, percent, per season.</summary>
    public List<int> FogDayPct { get; init; } = [25, 20, 40, 30];
    /// <summary>Chance that heavy summer-type rain carries thunder, percent, per season.</summary>
    public List<int> ThunderPct { get; init; } = [10, 30, 8, 0];
}

/// <summary>Tunables for seasons, fronts, temperature and weather effects (plan §3c). Arrays are indexed spring, summer, autumn, winter.</summary>
[ContentType("weather_settings")]
public sealed record WeatherSettingsDef : ContentDef
{
    public int LatitudeNorthTenths { get; init; } = 660;
    public int LatitudeSouthTenths { get; init; } = 300;
    public int SeasonalAmpBaseTenths { get; init; } = 70;
    public int SeasonalAmpNorthExtraTenths { get; init; } = 70;
    public int DailyAmpTenths { get; init; } = 60;
    public int SynopticAmpTenths { get; init; } = 25;
    public int HeatwaveTenths { get; init; } = 300;
    public int SnowBelowTenths { get; init; } = 10;
    public List<int> FrontCountMin { get; init; } = [4, 3, 4, 5];
    public List<int> FrontCountMax { get; init; } = [7, 6, 7, 8];
    public List<int> ColdFrontPct { get; init; } = [40, 30, 45, 55];
    public List<int> FrontStrengthPct { get; init; } = [95, 70, 100, 110];
    public int FrontSpeedMinCellsDay { get; init; } = 16;
    public int FrontSpeedMaxCellsDay { get; init; } = 30;
    public int DroughtPermilleYear { get; init; } = 70;
    public int WetYearPermilleYear { get; init; } = 70;
    public int ColdSummerPermilleYear { get; init; } = 60;
    public int HeavyRainMmHTenths { get; init; } = 25;
    public int HeavyDayMm { get; init; } = 12;
    public int FordClosedDays { get; init; } = 3;
    public int FordRecedeDays { get; init; } = 2;
    public List<WeatherBiomeRowDef> Biomes { get; init; } = [];
}
