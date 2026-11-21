namespace Ravensguard.Content;

/// <summary>
/// P7-ES1 (economies-of-scale 3, 4.1, 5): the numbers of division of labour and the limits of size. Content, never code: the grades a recipe's worker-hours split into (master / journeyman / apprentice and hand,
/// summing 1,000), the station kinds' crew limits, the span of control, the guild caps' defaults and the manufactory licence. Money in mites.
/// </summary>
[ContentType("scale_settings")]
public sealed record ScaleSettingsDef : ContentDef
{
    /// <summary>The recipe's skill when it names none that has a row (the whole batch is "A" steps: no division of labour).</summary>
    public List<ScaleGradeRow> Grades { get; init; } = [];
    /// <summary>The crew limit k of a station kind: workers beyond k count half, beyond 2k not at all (5.1).</summary>
    public List<ScaleCrewRow> CrewMax { get; init; } = [];
    public int CrewMaxDefault { get; init; } = 6;
    /// <summary>A journeyman doing the master's steps with no master present works at this many permille of the time (1,500 = half as slow again, 4.1).</summary>
    public int UnsupervisedPm { get; init; } = 1500;
    /// <summary>Skill thresholds of the roles (4.1 / 3): master from, journeyman from.</summary>
    public int MasterSkill { get; init; } = 60;
    public int JourneymanSkill { get; init; } = 30;
    /// <summary>A master serves the M steps of <c>2 + Leadership / per</c> stations (at most max, 4.1).</summary>
    public int ReachBase { get; init; } = 2;
    public int ReachPerLeadership { get; init; } = 30;
    public int ReachMax { get; init; } = 4;
    /// <summary>An overseer oversees <c>base + Leadership / per</c> workers (at most max); a crew beyond the sum works at <c>max(floor, 1,000 - per_excess x excess)</c> permille (5.2).</summary>
    public int SpanBase { get; init; } = 6;
    public int SpanPerLeadership { get; init; } = 10;
    public int SpanMax { get; init; } = 16;
    public int SpanPerExcessPm { get; init; } = 25;
    public int SpanFloorPm { get; init; } = 500;
    /// <summary>The appointed foreman: Leadership at least this, and the extra day wage in mites (5.2).</summary>
    public int ForemanMinLeadership { get; init; } = 20;
    public int ForemanWageMitesPerDay { get; init; } = 50;
    /// <summary>Guild size rules (5.3): the fine a season per worker or station over a cap, the draw's chance in percent, the fines after which the lord's court may close the shop, the opinion cost, the days fines are kept.</summary>
    public int GuildFinePerOverMites { get; init; } = 2000;
    public int InspectionPct { get; init; } = 50;
    public int FinesToClose { get; init; } = 3;
    public int FineOpinion { get; init; } = -20;
    public int FineKeepDays { get; init; } = 120;
    /// <summary>The manufactory licence (5.3): the fee, the season's rent, the guild master's opinion as an extra term, the term for every licence already granted in the trade in the town.</summary>
    public long LicenceFeeMites { get; init; } = 5000;
    public long LicenceSeasonMites { get; init; } = 500;
    public int LicencePerGrantedPenalty { get; init; } = 20;

    // ---- P7-ES2 (economies-of-scale 4.2 to 4.6, 5.4): shared fixed costs, supply contracts, carting, patterns, absorption ----
    /// <summary>Heat stations (4.2): a lit station burns <c>lit_fuel_g_per_h</c> whatever works it, and up to <c>share_max</c> batches use one lit station at once.</summary>
    public List<ScaleHeatRow> Heat { get; init; } = [];
    /// <summary>Firing stations (4.2): the permille of a full load's fuel that burns however small the load is.</summary>
    public List<ScaleFiringRow> Firing { get; init; } = [];
    /// <summary>Munition-grade standard patterns (4.6), by recipe id.</summary>
    public List<ScalePatternRow> Patterns { get; init; } = [];
    /// <summary>A pattern's practice (4.6): this many runs of one pattern within this many seasons make the piece hours <c>practice_pm</c> permille of themselves.</summary>
    public int PracticeRuns { get; init; } = 3;
    public int PracticeSeasons { get; init; } = 2;
    public int PracticePm { get; init; } = 850;
    /// <summary>The quality a pattern run may not exceed when its row names none: tier 2 of crude-poor-common-fine-masterwork.</summary>
    public string PatternQualityCap { get; init; } = "Common";
    /// <summary>Supply contracts (4.3): the smallest delivery in grams (or the smallest value a season for piece goods, in mites), the default days between deliveries, the longest term in seasons, the price band round the mid in permille, how late a delivery may be, the misses in a season that let the buyer end the contract, and the opinion cost of a broken one.</summary>
    public long ContractMinG { get; init; } = 500_000;
    public long ContractMinSeasonMites { get; init; } = 10_000;
    public int ContractEveryDays { get; init; } = 5;
    public int ContractMaxSeasons { get; init; } = 4;
    public int ContractBandPm { get; init; } = 50;
    public int ContractLateDays { get; init; } = 2;
    public int ContractMissesToBreak { get; init; } = 2;
    public int ContractBrokenOpinion { get; init; } = -20;
    /// <summary>Bulk carting (4.4): the vehicles, what the team eats a day when the book names no price, and a load under this permille of capacity is "half-empty".</summary>
    public List<ScaleVehicleRow> Vehicles { get; init; } = [];
    public int HayMitesPerKg { get; init; } = 12;
    public int OatsMitesPerKg { get; init; } = 30;
    public int HalfEmptyPm { get; init; } = 600;
    /// <summary>Market absorption (5.4): the reach of the books that count, in metres.</summary>
    public int AbsorbReachM { get; init; } = 8000;
    /// <summary>The bid test: a batch stops when the book's bid at the stock it would reach is under the unit variable cost times this permille.</summary>
    public int AbsorbBidMarginPm { get; init; } = 1050;
    /// <summary>How many days of the cap a business may hold unsold before the planner stops.</summary>
    public int AbsorbHoldDays { get; init; } = 3;
}

/// <summary>A heat station kind's shared hearth (4.2): the fuel good, its fixed burn per lit hour in grams, and how many batches one lit station serves at once.</summary>
public sealed record ScaleHeatRow
{
    public string Station { get; init; } = "";
    public string Fuel { get; init; } = "";
    public long LitFuelGPerH { get; init; }
    public int ShareMax { get; init; } = 2;
}

/// <summary>A firing station kind (4.2): the fuel good and the permille of a full load's fuel that is the fixed part of one firing.</summary>
public sealed record ScaleFiringRow
{
    public string Station { get; init; } = "";
    public string Fuel { get; init; } = "";
    public int FixedPm { get; init; }
}

/// <summary>A recipe's standard pattern (4.6): a run of up to <c>run_max</c> identical pieces takes <c>setup_h + k x piece_h</c> hours, its quality capped.</summary>
public sealed record ScalePatternRow
{
    public string Recipe { get; init; } = "";
    public double SetupH { get; init; }
    public double PieceH { get; init; }
    public int RunMax { get; init; } = 20;
    public string QualityCap { get; init; } = "";
}

/// <summary>A carting vehicle (4.4): load in kilograms, the crew's daily wage, the team (hay and oats a day in kilograms), the speed in metres an hour.</summary>
public sealed record ScaleVehicleRow
{
    public string Id { get; init; } = "";
    public int LoadKg { get; init; }
    public int WageMitesPerDay { get; init; } = 150;
    public double HayKgPerDay { get; init; }
    public double OatsKgPerDay { get; init; }
    public int SpeedMPerH { get; init; } = 450;
}

/// <summary>A default grade split: a recipe id or a skill name, and the permille of worker-hours by master, journeyman, apprentice and hand (sum 1,000).</summary>
public sealed record ScaleGradeRow
{
    public string Recipe { get; init; } = "";
    public string Skill { get; init; } = "";
    public List<int> GradesPm { get; init; } = [];
}

/// <summary>A station kind's crew limit k.</summary>
public sealed record ScaleCrewRow
{
    public string Station { get; init; } = "";
    public int Max { get; init; }
}
