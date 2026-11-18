using System.Text.Json.Serialization;

namespace Ravensguard.Content;

/// <summary>
/// A crop (farming spec 2.4, content/base/farming/crops.jsonc). <c>Kind</c> is <c>winter_grain</c>, <c>spring_grain</c> or <c>spring_legume</c>.
/// Yields are in measures per acre of average soil (fertility 70); one measure is 1,100 g.
/// </summary>
[ContentType("crop")]
public sealed record CropDef : ContentDef
{
    public required string Kind { get; init; }
    /// <summary>The stock good this crop produces and is sown from.</summary>
    [ContentRef(typeof(GoodDef))] public required string Good { get; init; }
    public int BaseYieldMeasures { get; init; } = 48;
    public int SeedPct { get; init; } = 20;
    public int FertilityDelta { get; init; }
    /// <summary>Sowing window, first and last day of the 60-day year.</summary>
    public List<int> SowDoy { get; init; } = [];
    public int RipeDoy { get; init; }
    public int ReapGraceDays { get; init; } = 10;
    public int MinFertility { get; init; } = 40;
}

/// <summary>Farming tunables (farming spec 2.4, 3; content/base/farming/farming.jsonc). Rate keys carry unit suffixes.</summary>
[ContentType("farming_settings")]
public sealed record FarmingSettingsDef : ContentDef
{
    public int GramsPerMeasure { get; init; } = 1100;
    public int WorkdayHours { get; init; } = 10;
    // task rates, acres per worker-day
    public int PloughAcresPerWorkerDay { get; init; } = 6;
    public int SpadeAcresPerWorkerDay { get; init; } = 1;
    public int SowAcresPerWorkerDay { get; init; } = 8;
    public int WeedAcresPerWorkerDay { get; init; } = 2;
    public int ReapAcresPerWorkerDay { get; init; } = 2;
    public int BindAcresPerWorkerDay { get; init; } = 4;
    public int ThreshAcresPerWorkerDay { get; init; } = 2;
    public int HayAcresPerWorkerDay { get; init; } = 2;
    // harvest split
    public int TithePermille { get; init; } = 100;
    public int RentShareBpDefault { get; init; } = 2500;
    public int RentCoinMitesPerFieldYear { get; init; } = 320;
    /// <summary>Share of tenant parcels let for coin instead of a share (deterministic per parcel).</summary>
    public int CoinLeasePct { get; init; } = 15;
    // weather
    public int HailChancePermillePerStormDay { get; init; } = 5;
    public int HailDamagePermille { get; init; } = 500;
    public int BlightPermilleYear { get; init; } = 30;
    public int BlightWetYearMult { get; init; } = 3;
    public int BlightYieldPermille { get; init; } = 750;
    public int LateFrostTenthsC { get; init; } = -20;
    public int FrostKillWindowDays { get; init; } = 12;
    public int ResowLastDoy { get; init; } = 15;
    public int ResowYieldPermille { get; init; } = 750;
    public int ColdSummerSpringPermille { get; init; } = 850;
    public int ColdSummerWinterPermille { get; init; } = 900;
    public int WinterkillLossPermille { get; init; } = 400;
    /// <summary>The spec's -10 C and 3 days kills the winter grain every year in the seed-1234 climate (10-19 days a winter below -10 C, almost no snow); calibrated to -20 C and 2 days.</summary>
    public int WinterkillTenthsC { get; init; } = -200;
    public int WinterkillDays { get; init; } = 2;
    public int WinterkillSnowMinCm { get; init; } = 5;
    public int WeedingMaxLossPermille { get; init; } = 250;
    public int OverdueLossPermillePerDay { get; init; } = 40;
    public int WetHarvest6mmPermille { get; init; } = 920;
    public int WetHarvest12mmPermille { get; init; } = 850;
    /// <summary>Share of a drought's window that must be covered for the full drought ratio (0.3), in permille; below it the ratio eases linearly (calibration, see the farming spec notes).</summary>
    public int DroughtFullCoverPermille { get; init; } = 400;
    public int DroughtRatioPermille { get; init; } = 300;
    public int WetYearRatioPermille { get; init; } = 1500;
    /// <summary>Spring ploughing and sowing wait for a thaw (05:00 temperature at or above this, tenths of C) and may run to this day when the thaw is late.</summary>
    public int ThawTenthsC { get; init; } = 0;
    public int SpringWorkLastDoy { get; init; } = 15;
    /// <summary>Half-width of field-to-field noise in permille (+-4% = 40); 0 switches noise off.</summary>
    public int VarianceSpreadPermille { get; init; } = 40;
    // fertility and mixes
    /// <summary>Phase 1 field fertility averages far below the plan's 70 (seed 1234: mean 39). At state creation field fertility is scaled so the mean fertility used by the yield (capped at 91) equals this; 0 keeps Phase 1 values.</summary>
    public int TargetMeanFertility { get; init; } = 74;
    /// <summary>The plan's reference density is 62,400 fields for 25,000 people, 2,496 per 1,000 people. Off by default (0); when positive the target mean fertility is scaled by this over the region's own density (fewer acres per person need richer soil to keep the plan's balance); 0 switches it off.</summary>
    public int ReferenceFieldsPerKiloPerson { get; init; } = 0;
    public int FallowGrazedFertility { get; init; } = 15;
    public int FallowUngrazedFertility { get; init; } = 5;
    public int WinterWheatPct { get; init; } = 60;
    public int WheatMinFertilityForWinterPick { get; init; } = 55;
    public List<int> SpringMixPct { get; init; } = [40, 27, 20, 13];
    public int LegumeForcedBelowFertility { get; init; } = 45;
    // threshing and straw
    public int StrawGPerKgGrain { get; init; } = 800;
    // labour and wages
    public int WageLabourerMitesPerDay { get; init; } = 40;
    public int HarvestWagePct { get; init; } = 150;
    // livestock coupling (resources spec)
    [JsonPropertyName("oxen_per_100_arable_acres")] public int OxenPer100ArableAcres { get; init; } = 3;
    public int GrazingSheepPerFallowAcreX100 { get; init; } = 40;
    public int SheepStartPerFallowAcreX100 { get; init; } = 50;
    public int MeadowPctOfFieldAcres { get; init; } = 16;
    public int HayKgPerMeadowAcre { get; init; } = 197;
    public int SheepHayKgPerHeadDayX10 { get; init; } = 15;
    public int OxHayKgPerHeadDayX10 { get; init; } = 60;
    public int WinterFodderDays { get; init; } = 15;
    public int CullMaxPermille { get; init; } = 600;
    public int SheepGrowthPermilleYear { get; init; } = 57;
    public int SheepPerFallowAcreCapX100 { get; init; } = 200;
    // herds as owned stock (resources spec 3.3, 3.4; integration decision I3). Every key defaults to 0 = off, so the farming unit tests keep their old herds; content turns them on.
    /// <summary>Cows per 100 field acres at world start (24 per 100 persons in the spec is about 10 per 100 acres in the seed-1234 region).</summary>
    [JsonPropertyName("cows_per_100_field_acres")] public int CowsPer100FieldAcres { get; init; }
    public int CowHayKgPerHeadDayX10 { get; init; }
    /// <summary>Grams of raw wool per sheep per game year (1 kg fleece x 60/365 = 164 g), sheared at the first day of spring.</summary>
    public int WoolGPerSheepYear { get; init; }
    /// <summary>Grams of cheese per cow per game year (80 kg x 60/365 = 13.1 kg), made at the start of autumn.</summary>
    public int CheeseGPerCowYear { get; init; }
    /// <summary>Calves per 1000 cows per game year; bull calves refill the oxen up to the target, the rest and the surplus heifers are sold for beef.</summary>
    public int CowCalvingPermilleYear { get; init; }
    public int CowPerFieldAcreCapX100 { get; init; }
    public int OxenMortalityPermilleYear { get; init; }
    public int SheepMortalityPermilleYear { get; init; }
    public int CowMortalityPermilleYear { get; init; }
}
