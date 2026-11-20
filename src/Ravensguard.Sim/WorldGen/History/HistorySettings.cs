using Ravensguard.Content;

namespace Ravensguard.Sim.WorldGen.History;

/// <summary>Stage 2 settings (plan §1A). Plain POCO; defaults come from content/base/worldgen/history.jsonc.</summary>
public sealed class HistorySettings
{
    public ulong Seed { get; set; } = 1;
    public int Years { get; set; } = 250;
    /// <summary>The world's <c>aging_pace</c> world setting: 1 (historical, the default), 2, 3 or 4. Only the ageing clock scales; yearly chances stay per game year.</summary>
    public int AgingPace { get; set; } = 1;
    public int RuralGrowthPermilleYear { get; set; } = 30;
    public int CapacityScalePct { get; set; } = 240;
    public int StartFillPct { get; set; } = 72;
    public int FoundAttemptsPerTick { get; set; } = 40;
    public int FoundMinHinterland { get; set; } = 2600;
    public int FoundSpacing { get; set; } = 3;
    public int FoundChancePct { get; set; } = 22;
    public int UrbanCapPct { get; set; } = 170;
    public int LevyPermille { get; set; } = 10;
    public int BadYearPermille { get; set; } = 85;
    public int FamineMaxLossPct { get; set; } = 16;
    public int PlaguePermilleYear { get; set; } = 22;
    public int PlagueSpreadPct { get; set; } = 45;
    public int PlagueTownMortalityPct { get; set; } = 20;
    public int FirePermilleYear { get; set; } = 2;
    public int FloodPermilleYear { get; set; } = 4;
    public int MineCollapsePermilleYear { get; set; } = 8;
    public int IncidentPpmTick { get; set; } = 15000;
    public int WarUtilityThreshold { get; set; } = 76;
    public int WarChancePct { get; set; } = 12;
    public int TruceYearsMin { get; set; } = 8;
    public int TruceYearsMax { get; set; } = 20;
    public int RebellionPpmTick { get; set; } = 2500;
    public int CoupPpmTick { get; set; } = 600;
    public int MarriagePctYear { get; set; } = 35;
    public int CrafterPermilleYear { get; set; } = 2;
    public int ArtefactPermilleYear { get; set; } = 60;
    public int RouteRefreshYears { get; set; } = 25;
    public int MaxRealms { get; set; } = 60;
    public int FoundInitialAttempts { get; set; } = 120000;
    public int MinorInitialAttempts { get; set; } = 250000;
    public int FoundRichCluster { get; set; } = 3;
    public int WarRealmsRef { get; set; } = 24;
    public int WarPacePerCentury { get; set; } = 14;
    public int AncientBattles { get; set; } = 400;
    public int RuinCrowdMax { get; set; } = 3;
    public int FoundCrowdMax { get; set; } = 3;
    public int FoundRegrowPct { get; set; } = 45;
    public int FoundSpacingRich { get; set; } = 2;
    public int FoundRichHinterland { get; set; } = 2300;
    public int UrbanMaxPop { get; set; } = 1800;
    public int MinorFoundAttemptsPerTick { get; set; } = 400;
    public int MinorSpacing { get; set; } = 2;
    public int MinorFoundChancePct { get; set; } = 40;
    public int MinorMaxPop { get; set; } = 320;
    public int ManorPct { get; set; } = 18;
    public int MinorAbandonPer10kYear { get; set; } = 20;
    public int PlagueEmptyPct { get; set; } = 18;
    public int FamineEmptyPermille { get; set; } = 6;
    public int PillageReach { get; set; } = 5;
    public int SeaReach { get; set; } = 12;
    public int SeaRaidPct { get; set; } = 55;
    public int TributePct { get; set; } = 40;
    public int VassalagePct { get; set; } = 25;
    public int VassalYearsMin { get; set; } = 20;
    public int VassalYearsMax { get; set; } = 45;

    public static HistorySettings FromDef(HistorySettingsDef d) => new()
    {
        Years = d.Years, RuralGrowthPermilleYear = d.RuralGrowthPermilleYear, CapacityScalePct = d.CapacityScalePct,
        StartFillPct = d.StartFillPct, FoundAttemptsPerTick = d.FoundAttemptsPerTick, FoundMinHinterland = d.FoundMinHinterland,
        FoundSpacing = d.FoundSpacing, FoundChancePct = d.FoundChancePct, UrbanCapPct = d.UrbanCapPct, LevyPermille = d.LevyPermille,
        BadYearPermille = d.BadYearPermille, FamineMaxLossPct = d.FamineMaxLossPct, PlaguePermilleYear = d.PlaguePermilleYear,
        PlagueSpreadPct = d.PlagueSpreadPct, PlagueTownMortalityPct = d.PlagueTownMortalityPct, FirePermilleYear = d.FirePermilleYear,
        FloodPermilleYear = d.FloodPermilleYear, MineCollapsePermilleYear = d.MineCollapsePermilleYear, IncidentPpmTick = d.IncidentPpmTick,
        WarUtilityThreshold = d.WarUtilityThreshold, WarChancePct = d.WarChancePct, TruceYearsMin = d.TruceYearsMin,
        TruceYearsMax = d.TruceYearsMax, RebellionPpmTick = d.RebellionPpmTick, CoupPpmTick = d.CoupPpmTick,
        MarriagePctYear = d.MarriagePctYear, CrafterPermilleYear = d.CrafterPermilleYear, ArtefactPermilleYear = d.ArtefactPermilleYear,
        RouteRefreshYears = d.RouteRefreshYears, MaxRealms = d.MaxRealms,
        FoundInitialAttempts = d.FoundInitialAttempts, FoundRegrowPct = d.FoundRegrowPct, FoundCrowdMax = d.FoundCrowdMax, RuinCrowdMax = d.RuinCrowdMax, AncientBattles = d.AncientBattles, WarPacePerCentury = d.WarPacePerCentury, WarRealmsRef = d.WarRealmsRef, FoundRichCluster = d.FoundRichCluster, MinorInitialAttempts = d.MinorInitialAttempts, FoundSpacingRich = d.FoundSpacingRich, FoundRichHinterland = d.FoundRichHinterland, UrbanMaxPop = d.UrbanMaxPop, MinorFoundAttemptsPerTick = d.MinorFoundAttemptsPerTick, MinorSpacing = d.MinorSpacing, MinorFoundChancePct = d.MinorFoundChancePct, MinorMaxPop = d.MinorMaxPop, ManorPct = d.ManorPct, MinorAbandonPer10kYear = d.MinorAbandonPer10kYear, PlagueEmptyPct = d.PlagueEmptyPct, FamineEmptyPermille = d.FamineEmptyPermille, PillageReach = d.PillageReach, SeaReach = d.SeaReach, SeaRaidPct = d.SeaRaidPct, TributePct = d.TributePct, VassalagePct = d.VassalagePct, VassalYearsMin = d.VassalYearsMin, VassalYearsMax = d.VassalYearsMax,
    };

    /// <summary>Loads defaults through the content pipeline (e.g. content/base); built-in defaults if absent.</summary>
    public static HistorySettings LoadDefaults(string? baseModFolder)
    {
        if (baseModFolder != null && Directory.Exists(baseModFolder))
        {
            var r = new ModLoader(ContentTypeRegistry.Default).Load([baseModFolder]);
            if (r.Database.TryGet<HistorySettingsDef>("history_defaults", out var def)) return FromDef(def);
        }
        return new HistorySettings();
    }
}
