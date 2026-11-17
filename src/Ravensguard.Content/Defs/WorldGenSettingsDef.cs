namespace Ravensguard.Content;

/// <summary>
/// Tunable defaults for Stage 1 world generation (the continent; plan §1A). The seed and layout are
/// chosen per world on the start screen, everything else defaults from here and can be moddable.
/// </summary>
[ContentType("worldgen_settings")]
public sealed record WorldGenSettingsDef : ContentDef
{
    public int PlateCount { get; init; } = 13;
    /// <summary>The one sea level used by every phase, as Q16.16 raw (22938 = 0.35).</summary>
    public int SeaLevelRaw { get; init; } = 22938;
    public int MineralDensityPct { get; init; } = 100;
    public int SavageryPct { get; init; } = 100;
    public int MoisturePct { get; init; } = 100;
    /// <summary>Added to every cell's mean annual temperature, in tenths of a degree C.</summary>
    public int TemperatureOffsetTenths { get; init; }
    public int CultureCount { get; init; } = 5;
    public int RealmCount { get; init; } = 8;
    public int LandPctPangea { get; init; } = 47;
    public int LandPctContinents { get; init; } = 38;
    public int LandPctArchipelago { get; init; } = 24;
    /// <summary>Accumulated flow (rain-weighted catchment cells) above which a cell is a river / a stream.</summary>
    public int RiverFlowThreshold { get; init; } = 220;
    public int StreamFlowThreshold { get; init; } = 70;
    public List<string> CultureNames { get; init; } = [];
    public List<string> RealmNames { get; init; } = [];
}
