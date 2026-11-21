namespace Ravensguard.Content;

/// <summary>
/// One formation shape's layout and pace (battle-bubble-and-formations 3.2, P7-BB-B; <c>battle/formations.jsonc</c>, row type <c>formation_shape</c>; the id is the snake-case name of the
/// <c>Formation</c> enum value). Distances in mm, speeds in mm a second, turn in degrees a second.
/// </summary>
[ContentType("formation_shape")]
public sealed record FormationShapeDef : ContentDef
{
    /// <summary>The gap between files (lateral) and between ranks, mm. For a ring: the gap between neighbours on a ring and between rings.</summary>
    public int LateralMm { get; init; } = 1000;
    public int RankMm { get; init; } = 1200;
    public int Ranks { get; init; } = 4;
    /// <summary>A fixed number of files (the column's 3 abreast); 0: the files follow from the ranks.</summary>
    public int Abreast { get; init; }
    /// <summary>"rank" (files and ranks from the anchor), "schiltron" (a ring facing out, polearms outside) or "fall_back" (rings about the commander).</summary>
    public string Layout { get; init; } = "rank";
    public int TurnDegPerS { get; init; } = 30;
    public int WalkMmps { get; init; } = 1500;
    public int JogMmps { get; init; } = 3000;
    /// <summary>How far a member in contact may stray from his slot (4.4).</summary>
    public int LeashMm { get; init; } = 2000;
    /// <summary>The ring's radius about the commander (fall back), mm.</summary>
    public int RingMm { get; init; } = 3000;
}

/// <summary>An order kind's row (3.3; <c>battle/orders.jsonc</c>, <c>battle_order</c>; the id is the snake-case <c>OrderKind</c>): whether a horn carries it, whether it is carried out at a jog, and whether it is momentary (sets a shape or a fire mode and does not replace the movement order).</summary>
[ContentType("battle_order")]
public sealed record BattleOrderDef : ContentDef
{
    public bool Horn { get; init; }
    public bool Jog { get; init; }
    public bool Momentary { get; init; }
}

/// <summary>A captain's rule (4.7; <c>battle/captains.jsonc</c>, <c>captain_rule</c>): the lowest Tactics that may use it, and the rule's code name.</summary>
[ContentType("captain_rule")]
public sealed record CaptainRuleDef : ContentDef
{
    public int Tactics { get; init; }
    public string Rule { get; init; } = "";
}

/// <summary>The formation layer's constants (4.3 to 4.7; <c>battle/formations.jsonc</c>, one <c>battle_rules</c> row). Ticks are 20 Hz fight ticks, <c>_pm</c> permille, <c>_x100</c> morale points x100.</summary>
[ContentType("battle_rules")]
public sealed record BattleRulesDef : ContentDef
{
    // closing ranks
    public int CloseTicks { get; init; } = 40;
    public int CloseDrillDiv { get; init; } = 25;
    public int HesitateMaxTicks { get; init; } = 10;
    // cohesion and braking
    public int CohesionMm { get; init; } = 2000;
    public int CohesionMinPm { get; init; } = 600;
    public int BrakePercentile { get; init; } = 20;
    public int ContactMm { get; init; } = 3000;
    // deployment from column
    public int DeployRankMm { get; init; } = 1500;
    public int DeployPaceMmps { get; init; } = 3000;
    public int DeployDrillCutPm { get; init; } = 500;
    // orders
    public int DelayBaseTicks { get; init; } = 80;
    public int DelayLeadershipNum { get; init; } = 4;
    public int DelayLeadershipDen { get; init; } = 5;
    public int DelayMinTicks { get; init; } = 10;
    public int VoiceRangeMm { get; init; } = 60000;
    public int HornExtraTicks { get; init; } = 20;
    public int RunnerMmps { get; init; } = 5000;
    public int MaxQueue { get; init; } = 4;
    public int StaggerTicks { get; init; } = 10;
    public int ChargeMaxTicks { get; init; } = 600;
    public int ArriveMm { get; init; } = 3000;
    public int RetreatMm { get; init; } = 80000;
    public int FlankOffsetMm { get; init; } = 15000;
    // morale at formation scale (4.6)
    public int FlankedSharePm { get; init; } = 100;
    public int RoutShare { get; init; } = 500;
    public int RoutCascadeX100 { get; init; } = 2000;
    public int NeighbourCascadeX100 { get; init; } = 1000;
    public int NeighbourCascadeRadiusM { get; init; } = 30;
    public int AuraBaseM { get; init; } = 10;
    public int AuraLeadershipDiv { get; init; } = 5;
    // captains (4.7)
    public int CaptainTakeoverS { get; init; } = 30;
    public int RedecideMinTicks { get; init; } = 40;
    public int RedecideBaseTicks { get; init; } = 200;
    public int RedecideTacticsMul { get; init; } = 2;
    public int MissileRangeM { get; init; } = 150;
    public int FlankAlertTicks { get; init; } = 20;
    public int PullBackArchersM { get; init; } = 40;
    public int ArcherSafeMm { get; init; } = 5000;
    public int OutnumberedPm { get; init; } = 2000;
}
