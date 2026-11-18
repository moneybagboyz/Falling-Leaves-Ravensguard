namespace Ravensguard.Content;

/// <summary>A need's curve: the input it reads (<c>hours_since_meal</c>, <c>fatigue_pm</c>, <c>social_pm</c>, <c>cold_stress_pm</c>, <c>threat_pm</c>, <c>duty_pm</c>) and a piecewise-linear table of (input, permille) points.</summary>
public sealed record NeedCurveDef
{
    public required string Id { get; init; }
    public string From { get; init; } = "";
    /// <summary>Points as [input, output permille], ascending in the input. Inputs are whole units (hours, permille), outputs permille.</summary>
    public List<List<int>> CurvePm { get; init; } = [];
}

/// <summary>One weighted need an action scores on.</summary>
public sealed record UtilityWeightDef { public required string Need { get; init; } public int WeightPm { get; init; } = 1000; }

/// <summary>An action the scorer weighs (live-behaviour spec 3.4): the needs it answers, the place it needs (a gate), and how long a started one is kept (<c>commit_minutes</c>).</summary>
public sealed record UtilityActionDef
{
    public required string Kind { get; init; }
    public List<UtilityWeightDef> Weights { get; init; } = [];
    public string NeedsPlace { get; init; } = "";
    public int CommitMinutes { get; init; } = 5;
    /// <summary>The action is only a candidate when fatigue reaches this (Sleep), or it is dark (0: always a candidate).</summary>
    public int MinDarkOrFatiguePm { get; init; }
}

/// <summary>
/// The utility scorer's tuning (live-behaviour spec 3.4): one profile, every number content. <c>hunger_eat_hours</c> is the one auto-eat threshold shared with travel and autopilot.
/// Rates carry unit suffixes (<c>_pm</c> permille, <c>_ticks</c>, <c>_hours</c>, <c>_minutes</c>).
/// </summary>
[ContentType("utility_profile")]
public sealed record UtilityProfileDef : ContentDef
{
    public int DecisionPeriodTicks { get; init; } = 20;
    public int CommitBonusPm { get; init; } = 150;
    public int NoisePm { get; init; } = 20;
    public int ScheduleBonusPm { get; init; } = 400;
    public int DutyPm { get; init; } = 500;
    public int HungerEatHours { get; init; } = 13;
    /// <summary>Exhaustion above this (permille) overrides any action but Flee.</summary>
    public int ExhaustedPm { get; init; } = 800;
    /// <summary>Flee beats everything above this threat (permille).</summary>
    public int FleeThreatPm { get; init; } = 600;
    public List<NeedCurveDef> Needs { get; init; } = [];
    public List<UtilityActionDef> Actions { get; init; } = [];
}

/// <summary>A named time of day in a schedule, written as an anchor expression (<c>sunrise-1:30</c>, <c>min(sunset+0:30, work_from+11:00)</c>, <c>06:00</c>).</summary>
public sealed record ScheduleBlockDef
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    /// <summary>A <c>JobKind</c> name (Sleep, Home, Work, Shop, Church, Idle).</summary>
    public string Kind { get; init; } = "Home";
    /// <summary>home, bed, work, market, square, church.</summary>
    public string Place { get; init; } = "home";
    /// <summary>Only on days of this kind (empty: always): <c>market</c>, <c>fair</c>, <c>holy</c>, <c>heatwave</c>.</summary>
    public string When { get; init; } = "";
    /// <summary>Share of people (permille) who take the block on a day it applies to; the pick is a stable draw keyed on person and day.</summary>
    public int SharePm { get; init; } = 1000;
}

/// <summary>A day plan (live-behaviour spec 3.3 and 3.5): anchors as expressions, then blocks. People pick one by occupation; times are minutes after midnight, wrapping at 24:00.</summary>
[ContentType("schedule")]
public sealed record ScheduleDef : ContentDef
{
    public Dictionary<string, string> Anchors { get; init; } = [];
    public List<ScheduleBlockDef> Blocks { get; init; } = [];
    /// <summary>Occupations (names of the Occupation enum) that follow this plan; the first plan that lists an occupation wins.</summary>
    public List<string> Occupations { get; init; } = [];
}

/// <summary>Market and crowd tuning: how many of the townsfolk go to the square at the end of the day (permille of adults who are home by then), by kind of day.</summary>
[ContentType("crowd_tuning")]
public sealed record CrowdTuningDef : ContentDef
{
    public int ShopShareNormalPm { get; init; } = 60;
    public int ShopShareMarketPm { get; init; } = 200;
    public int ShopShareFairPm { get; init; } = 450;
    /// <summary>Day of the month a market is held (5-day months: one in each), and the days of the year of the fairs are the calendar's feasts.</summary>
    public int MarketDayOfMonth { get; init; } = 3;
    public int HolyDayOfMonth { get; init; } = 5;
    /// <summary>A person queues this far back from a stall's front, millimetres per place in line.</summary>
    public int QueueGapMm { get; init; } = 850;
}

/// <summary>Odd jobs (live-behaviour spec 4.5, 4.6): the wage multiple over the unskilled wage, the posting cap and the pay terms, by kind.</summary>
[ContentType("odd_job_tuning")]
public sealed record OddJobTuningDef : ContentDef
{
    /// <summary>1.10 x the unskilled wage, as permille.</summary>
    public int PayMultiplePm { get; init; } = 1100;
    public int GuardPayMultiplePm { get; init; } = 1250;
    public int EscortPayMultiplePm { get; init; } = 1500;
    /// <summary>Unskilled wage in mites per game day (150 = 15 mites an hour of a 10 hour day).</summary>
    public int UnskilledWageMitesPerDay { get; init; } = 150;
    public int BoardCap { get; init; } = 24;
    public int GraceHours { get; init; } = 2;
    /// <summary>Extra mites per started 100 kg-km of a haul.</summary>
    public int HaulMitesPer100KgKm { get; init; } = 5;
    public int ReservationTtlSeconds { get; init; } = 3600;
    /// <summary>Walking pace of the haul price estimate, metres per game hour.</summary>
    public int PaceMetresPerHour { get; init; } = 900;
}
