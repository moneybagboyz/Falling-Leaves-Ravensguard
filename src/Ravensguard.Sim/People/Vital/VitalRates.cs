using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.People.Vital;

/// <summary>
/// The vital-rate table of population spec 2 (<c>vital_rates</c>), all integers: percentages per game year are stored x100 (0.8 percent = 80), multipliers in
/// permille, rates per day in ppm. A year is 60 game days; the daily hazard is the yearly percentage divided by 60 (decision P1).
/// </summary>
public sealed record VitalRates
{
    /// <summary>Death percent per game year x100 by age band: first row whose AgeMax is at least the age.</summary>
    public (int AgeMax, int PctX100)[] DeathPctPerYearX100 { get; init; } = [(0, 2000), (4, 300), (14, 80), (49, 120), (64, 300), (200, 1000)];
    public int FertilityPctPerYear { get; init; } = 30;
    public int FertilityAgeMin { get; init; } = 18;
    public int FertilityAgeMax { get; init; } = 44;
    public int PregnancyDays { get; init; } = 45;
    public int PostpartumDays { get; init; } = 15;
    public int MarryAgeMinFemale { get; init; } = 16;
    public int MarryAgeMaxFemale { get; init; } = 40;
    public int MarryAgeMinMale { get; init; } = 18;
    public int MarryAgeMaxMale { get; init; } = 50;
    public int MarryChancePctPerSeason { get; init; } = 35;
    public int MarriageWaitCap { get; init; } = 8;
    /// <summary>Migration push (population spec 3.7): households over this crowding percent, and unmarried adults who waited this many seasons, leave with this chance per season.</summary>
    public int EmigrateCrowdingOverPct { get; init; } = 150;
    public int EmigrateAfterWaitSeasons { get; init; } = 8;
    public int EmigrateChancePct { get; init; } = 30;
    /// <summary>A couple may live with the groom's or bride's family only while the household, with the newcomer, stays within this percent of its beds (the stem family, decision V5).</summary>
    public int StemJoinMaxOccupancyPct { get; init; } = 100;
    /// <summary>Death multiplier (permille) by nutrition reserve in centidays: first row whose MinCd is at most the reserve.</summary>
    public (int MinCd, int MultPermille)[] NutritionDeathMult { get; init; } = [(2000, 1000), (1000, 1500), (500, 2000), (0, 3000)];
    /// <summary>Death multiplier (permille) by crowding percent (members x 100 / beds): first row whose MaxPct is at least the crowding.</summary>
    public (int MaxPct, int MultPermille)[] CrowdingDeathMult { get; init; } = [(100, 1000), (125, 1050), (150, 1100), (int.MaxValue, 1200)];
    /// <summary>Fertility multiplier (permille) by nutrition reserve: first row whose MinCd is at most the reserve.</summary>
    public (int MinCd, int MultPermille)[] FertilityFoodMult { get; init; } = [(2400, 1000), (1500, 800), (500, 500), (0, 200)];
    public int FertilityCrowdingOverPct { get; init; } = 150;
    public int FertilityCrowdingMultPermille { get; init; } = 800;
    public int StarvationPpmPerDay { get; init; } = 5000;
    public int TownDiseaseMultPermille { get; init; } = 1200;
    public int ColdDeathMultPermille { get; init; } = 1300;
    public int MaxMultPermille { get; init; } = 6000;
    public int MaxAgeYears { get; init; } = 110;
    /// <summary>Outbreak start chance in ppm per day: winter town, winter village, summer town (population spec 3.6); other seasons none.</summary>
    public int OutbreakWinterTownPpm { get; init; } = 4000;
    public int OutbreakWinterVillagePpm { get; init; } = 1500;
    public int OutbreakSummerTownPpm { get; init; } = 2500;
    public int OutbreakMildMultPermille { get; init; } = 1500;
    public int OutbreakSevereMultPermille { get; init; } = 2500;
    public int OutbreakSeverePct { get; init; } = 10;
    /// <summary>Famine: share of households (percent) with under 3 days of food cover for this many days in a row.</summary>
    public int FamineSharePct { get; init; } = 15;
    public int FamineDays { get; init; } = 5;
    /// <summary>Days without the hungry share before a famine is over (a settlement that flickers around the share is one famine, not a dozen; integration decision I34).</summary>
    public int FamineQuietDays { get; init; } = 60;
    /// <summary>Share (percent) of a departing household's coin that goes to its fief's treasury (chevage, integration decision I11); the rest leaves with it.</summary>
    public int ChevagePct { get; init; }
    /// <summary>Dowry as percent of the bride's household purse.</summary>
    public int DowryPct { get; init; } = 5;
    /// <summary>Share (percent) of each good that both households give a new couple to start with (stand-in until markets exist, decision V4).</summary>
    public int SettleInGoodsPct { get; init; } = 10;
    /// <summary>When true every birth, marriage and death is chronicled (tests, debugging); otherwise only notables, hunger/cold/violence deaths (capped per day) and famine.</summary>
    public bool ChronicleEveryEvent { get; init; }
    /// <summary>Cap of non-notable death entries per game day (population spec 3.1).</summary>
    public int ChronicleDeathsPerDay { get; init; } = 50;

    public static readonly VitalRates Default = new();

    /// <summary>
    /// The rates of a world with the whole economy (the game and the soaks): chevage (decision I11) and the north-west European marriage pattern, brides from 21 and
    /// grooms from 23 (integration decision I39; were 16 and 18): later marriage is what kept the pre-industrial English under-15 share near 30 percent.
    /// </summary>
    public static readonly VitalRates EconomyDefault = Default with { ChevagePct = 50, MarryAgeMinFemale = 21, MarryAgeMinMale = 23 };
}

/// <summary>The one RNG stream numbers of the vital kernels (overview 2.3).</summary>
public static class VitalStreams
{
    public const uint Death = RngStreams.Death, Conception = RngStreams.Conception, Marriage = RngStreams.Marriage, Migrant = RngStreams.Migrant, Outbreak = RngStreams.Outbreak,
        Name = RngStreams.Name, Attributes = RngStreams.Attributes;
}
