namespace Ravensguard.Content;

/// <summary>
/// Tunable defaults for Stage 2 world generation (history v0; plan §1A). Probabilities are integers:
/// "permille_year" is per thousand per year, "ppm_tick" is per million per season-tick, "pct" is percent.
/// </summary>
[ContentType("worldgen_history")]
public sealed record HistorySettingsDef : ContentDef
{
    public int Years { get; init; } = 250;
    public int RuralGrowthPermilleYear { get; init; } = 30;
    public int CapacityScalePct { get; init; } = 100;
    public int StartFillPct { get; init; } = 72;
    public int FoundAttemptsPerTick { get; init; } = 40;
    public int FoundMinHinterland { get; init; } = 650;
    public int FoundSpacing { get; init; } = 5;
    public int FoundChancePct { get; init; } = 22;
    public int UrbanCapPct { get; init; } = 90;
    public int LevyPermille { get; init; } = 10;
    public int BadYearPermille { get; init; } = 85;
    public int FamineMaxLossPct { get; init; } = 16;
    public int PlaguePermilleYear { get; init; } = 22;
    public int PlagueSpreadPct { get; init; } = 45;
    public int PlagueTownMortalityPct { get; init; } = 20;
    public int FirePermilleYear { get; init; } = 2;
    public int FloodPermilleYear { get; init; } = 4;
    public int MineCollapsePermilleYear { get; init; } = 8;
    public int IncidentPpmTick { get; init; } = 9000;
    public int WarUtilityThreshold { get; init; } = 68;
    public int WarChancePct { get; init; } = 22;
    public int TruceYearsMin { get; init; } = 8;
    public int TruceYearsMax { get; init; } = 20;
    public int RebellionPpmTick { get; init; } = 2500;
    public int CoupPpmTick { get; init; } = 600;
    public int MarriagePctYear { get; init; } = 35;
    public int CrafterPermilleYear { get; init; } = 2;
    public int ArtefactPermilleYear { get; init; } = 60;
    public int RouteRefreshYears { get; init; } = 25;
    public int MaxRealms { get; init; } = 60;
    public int FoundInitialAttempts { get; init; } = 30000;
    public int MinorInitialAttempts { get; init; } = 250000;
    public int FoundRichCluster { get; init; } = 3;
    public int FoundRegrowPct { get; init; } = 45;
    public int FoundSpacingRich { get; init; } = 3;
    public int FoundRichHinterland { get; init; } = 2400;
    public int UrbanMaxPop { get; init; } = 3000;
    public int MinorFoundAttemptsPerTick { get; init; } = 120;
    public int MinorSpacing { get; init; } = 2;
    public int MinorFoundChancePct { get; init; } = 40;
    public int MinorMaxPop { get; init; } = 320;
    public int ManorPct { get; init; } = 18;
    public int MinorAbandonPermilleYear { get; init; } = 3;
    public int PlagueEmptyPct { get; init; } = 18;
    public int FamineEmptyPct { get; init; } = 3;
    public int PillageReach { get; init; } = 5;
    public int SeaReach { get; init; } = 12;
    public int SeaRaidPct { get; init; } = 55;
    public int TributePct { get; init; } = 40;
    public int VassalagePct { get; init; } = 25;
    public int VassalYearsMin { get; init; } = 20;
    public int VassalYearsMax { get; init; } = 45;
}
