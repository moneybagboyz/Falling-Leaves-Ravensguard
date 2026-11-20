namespace Ravensguard.Content;

/// <summary>
/// The numbers of unrest and revolt (unrest-and-revolt 3, 4; <c>unrest/settings.jsonc</c>): the aggrieved line, the ladder's rungs in permille, the riot's trigger and size, the revolt's triggers and battle, the
/// fear that follows a loss and the lord's concessions. The defaults are the spec's. Integer only; money in mites.
/// </summary>
[ContentType("unrest_settings")]
public sealed record UnrestSettingsDef : ContentDef
{
    /// <summary>Points at which a household is aggrieved.</summary>
    public int AggrievedAt { get; init; } = 3;
    /// <summary>The most points one household carries.</summary>
    public int PointsCap { get; init; } = 8;
    /// <summary>Under this many households a place counts as this many (a lone farmstead's grievance is not a riot).</summary>
    public int MinHouseholds { get; init; } = 10;
    public int Rung1 { get; init; } = 150;
    public int Rung2 { get; init; } = 300;
    public int Rung3 { get; init; } = 450;
    public int Rung4 { get; init; } = 600;
    /// <summary>Days a settlement must stand at the riot rung before the crowd gathers.</summary>
    public int RiotDays { get; init; } = 5;
    /// <summary>The unrest at which a trigger (an eviction, a hanging of a local, a tallage) starts a riot at once.</summary>
    public int TriggerAt { get; init; } = 350;
    /// <summary>Seasons at the revolt rung that raise a revolt.</summary>
    public int RevoltSeasons { get; init; } = 2;
    /// <summary>Deaths in a put-down riot that, at <see cref="RevoltDeathsAt"/> unrest, raise a revolt.</summary>
    public int RevoltDeaths { get; init; } = 3;
    public int RevoltDeathsAt { get; init; } = 500;
    /// <summary>A riot's crowd, in percent of the aggrieved adults: this at <see cref="CrowdFrom"/> unrest, <see cref="CrowdMaxPct"/> at <see cref="CrowdTo"/>, linear between.</summary>
    public int CrowdMinPct { get; init; } = 10;
    public int CrowdMaxPct { get; init; } = 40;
    public int CrowdFrom { get; init; } = 450;
    public int CrowdTo { get; init; } = 800;
    /// <summary>Fewest people who make a crowd.</summary>
    public int CrowdMinPeople { get; init; } = 3;
    /// <summary>Age at which a person is an adult for a riot or a levy.</summary>
    public int AdultAge { get; init; } = 16;
    /// <summary>The hour the crowd gathers and acts, and the hours a riot lasts from then if nothing ends it.</summary>
    public int GatherHour { get; init; } = 10;
    public int RiotHours { get; init; } = 6;
    /// <summary>Days after a riot before the settlement can have another.</summary>
    public int CooldownDays { get; init; } = 30;
    /// <summary>Percent of the barn's grain, or of the fief chest's coin, a riot carries off.</summary>
    public int RiotBarnPct { get; init; } = 25;
    public int RiotChestPct { get; init; } = 20;
    /// <summary>Percent chance an aggrieved household pays no hearth tax at rung 2 (stream 0x682).</summary>
    public int EvasionPct { get; init; } = 50;
    /// <summary>The levy rule: one man in this many of the aggrieved men rises.</summary>
    public int LevyOneIn { get; init; } = 5;
    /// <summary>A guard counts for this many men of the levy in a revolt's battle.</summary>
    public int GuardWeight { get; init; } = 3;
    /// <summary>The battle's variance in percent of each side, drawn with stream 0x682, and the share of each side lost (winner, loser).</summary>
    public int BattleVarPct { get; init; } = 20;
    public int WinnerLossPct { get; init; } = 10;
    public int LoserLossPct { get; init; } = 30;
    /// <summary>A cruel lord's guards kill one rioter in this many (at least one) when they disperse a crowd.</summary>
    public int CruelDeathsDiv { get; init; } = 12;
    /// <summary>Unrest taken off after a revolt is put down, and the seasons fear holds it.</summary>
    public int FearDrop { get; init; } = 200;
    public int FearSeasons { get; init; } = 2;
    /// <summary>A neighbouring lord whose opinion of the lord is at or under this may be a revolt's claimant.</summary>
    public int ClaimantOpinion { get; init; } = -40;
    /// <summary>The liege accepts a new lord whose opinion with him is above this.</summary>
    public int LiegeAccepts { get; init; } = -20;
    /// <summary>A mediator needs the lord's utility for a concession at least this.</summary>
    public int ConcessionUtility { get; init; } = 200;
    /// <summary>Coin a grain dole gives each hungry household and how long it holds the hunger points down.</summary>
    public long DoleMitesPerHousehold { get; init; } = 300;
    public int DoleDays { get; init; } = 15;
    /// <summary>The lord's standing in a settlement under which the people hold it against him.</summary>
    public int StandingBelow { get; init; } = -300;
}

/// <summary>
/// One kind of grievance (<c>society/grievances.jsonc</c>): the points it gives a household, the days it lasts after the event that raised it (0: while the condition lasts) and where a crowd carrying it goes
/// (<see cref="Target"/>: barn, reeve, manor or gaol).
/// </summary>
[ContentType("grievance_kind")]
public sealed record GrievanceKindDef : ContentDef
{
    public int Points { get; init; } = 1;
    public int Days { get; init; }
    public string TextKey { get; init; } = "";
    public string NoteKey { get; init; } = "";
    public string Target { get; init; } = "manor";
    /// <summary>Whether the lord's own acts raised it (a pardon or a concession clears it).</summary>
    public bool Lord { get; init; }
}
