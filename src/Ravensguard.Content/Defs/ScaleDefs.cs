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
