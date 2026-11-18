namespace Ravensguard.Content;

/// <summary>
/// The economy's tuning numbers that were constants in the code (review finding 12; <c>content/base/economy/tuning.jsonc</c>): parish relief and the fief's dole,
/// the lords' almonry, the households' spending, fuel gathering and the woodward, home brewing and the alewife, farm sales. Defaults are the values the code had,
/// so a world without the file runs as before. Rates carry their unit suffix.
/// </summary>
[ContentType("economy_tuning")]
public sealed record EconomyTuningDef : ContentDef
{
    // ---------- parish relief (households spec 3.6, taxes spec 3.6) ----------
    /// <summary>A household under this many tenths of a day of food draws the parish ration and the fief's dole.</summary>
    public int ReliefBelowDaysX10 { get; init; } = 30;
    /// <summary>The parish ration, hundredths of a measure per member and day.</summary>
    public int CharityMeasureX100PerMember { get; init; } = 100;
    /// <summary>The parish sells one part in this many of its granary's tithe a day where no household of the fief is hungry.</summary>
    public int TitheSellDivisorPerDay { get; init; } = 60;
    public int TitheSaleFloorPermille { get; init; } = 800;
    /// <summary>The fief treasury's coin dole to a hungry household, mites per member and day.</summary>
    public int DoleMitesPerMemberDay { get; init; } = 45;
    /// <summary>The fief steward's relief purchases: at most this share of the treasury a day (split over the settlements by their need), at most this price ratio, this many days of rations.</summary>
    public int ReliefBudgetPermillePerDay { get; init; } = 20;
    public int ReliefMaxRatioPermille { get; init; } = 3500;
    public int ReliefBuyDays { get; init; } = 2;
    /// <summary>The lord's barn feeds the destitute from what it holds above this many days of his household's food.</summary>
    public int LordBarnKeepDays { get; init; } = 40;
    /// <summary>The Church moves grain from this many of the region's richest parish granaries, each holding at least this much.</summary>
    public int ChurchRichestGranaries { get; init; } = 8;
    public int ChurchGranaryMinG { get; init; } = 20_000;

    // ---------- the lords' almonry (integration decision I25) ----------
    public long AlmsReserveMites { get; init; } = 100_000;
    public int AlmsSharePct { get; init; } = 50;
    public int AlmsMaxPerMemberMites { get; init; } = 2500;
    public int AlmsPoorBelowMites { get; init; } = 1000;
    public int AlmsPoorBelowDaysX10 { get; init; } = 100;

    // ---------- households' spending (integration decision I37) ----------
    public int SpendingServiceDaysMax { get; init; } = 15;
    public int SpendingPoorBelowMites { get; init; } = 3000;
    public int SpendingAlmsPerMemberMites { get; init; } = 1000;
    public int SpendingOrderMaxRatioPermille { get; init; } = 2000;
    /// <summary>A lord keeps this many days of his manors' wages, at this day wage a head, before he spends.</summary>
    public int ManorReserveDays { get; init; } = 30;
    public int ManorReserveWageMitesPerDay { get; init; } = 200;

    // ---------- fuel (integration decisions I22, I36, I38; review finding 4) ----------
    /// <summary>A household gathers its day's burn plus this buffer, up to <see cref="FuelKeepDays"/> days of today's burn in store.</summary>
    public int GatherFuelBufferPct { get; init; } = 10;
    /// <summary>Hours a household member may spend gathering fuel a day: an adult with no work, an adult with work (or a farm holder), a child of 8 to 15.</summary>
    public int GatherHoursIdleAdult { get; init; } = 4;
    public int GatherHoursWorkingAdult { get; init; } = 1;
    public int GatherHoursChild { get; init; } = 1;
    /// <summary>Dead wood and brush gathered by hand, grams per worker-hour (felling and splitting with an axe is the extraction rate, 700 kg a worker-day).</summary>
    public int GatherFuelGPerWorkerHour { get; init; } = 7_500;
    /// <summary>The woodland a settlement's households gather and cut in: wood rows within this radius of its centre.</summary>
    public int GatherRadiusM { get; init; } = 1_500;
    /// <summary>Wood rows are not cut below this share of their cap (the extraction floor).</summary>
    public int GatherStockFloorPct { get; init; } = 30;
    /// <summary>Days of today's burn a household keeps before it sells fuel.</summary>
    public int FuelKeepDays { get; init; } = 25;
    /// <summary>The woodward: hours an idle adult of a landed peasant household cuts for the market, at most this many a day.</summary>
    public int WoodwardHoursPerIdleAdult { get; init; } = 6;
    public int WoodwardFloorPermille { get; init; } = 800;

    // ---------- home brewing and the alewife (integration decisions I23, I38) ----------
    public int BrewBarleyPermille { get; init; } = 520;
    public int BrewFuelPermille { get; init; } = 350;
    public int BrewDaysCover { get; init; } = 3;
    public int BrewBatchDays { get; init; } = 6;
    public int BrewMinFoodDaysX10 { get; init; } = 150;
    public int AlewifeBatchDays { get; init; } = 10;
    public int AlewifeFloorPermille { get; init; } = 850;

    // ---------- gardens and hens (integration decision I12) ----------
    public int GardenGPerPersonDay { get; init; } = 1100;
    public int GardenFirstDoy { get; init; } = 11;
    public int GardenLastDoy { get; init; } = 40;
    public int HenEggGPerPersonDay { get; init; } = 14;

    // ---------- immigration from the continent (continent-tick spec 3.7) ----------
    /// <summary>The coin and grain an immigrant brings, a head (taken from the neighbour's summary).</summary>
    public int ImmigrantStakeMitesPerHead { get; init; } = 2000;
    public int ImmigrantStakeMeasuresPerHead { get; init; } = 13;

    // ---------- merchants (decision I43) ----------
    /// <summary>A market book keeps one merchant's stall for every this many of its people (at least one; 0: every merchant of the population keeps his own stall).</summary>
    public int PeoplePerStall { get; init; }

    // ---------- farm sales (integration decisions I30, I38) ----------
    public int FarmSalesKeepMarginDays { get; init; } = 45;
    public int FarmSalesSpreadDays { get; init; } = 3;
    public int FarmSalesFloorPermille { get; init; } = 800;
    /// <summary>A producer carts to the market town when it pays at least this many permille of the home book's mid.</summary>
    public int CartToTownPermille { get; init; } = 1150;
}
