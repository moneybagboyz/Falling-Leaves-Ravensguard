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
    /// <summary>"rank" (files and ranks from the anchor), "wedge" (rank r holds 1 + 2r riders), "schiltron" (a ring facing out, polearms outside) or "fall_back" (rings about the commander).</summary>
    public string Layout { get; init; } = "rank";
    public int TurnDegPerS { get; init; } = 30;
    /// <summary>A cavalry shape's turn rate at a trot and at a walk (the cavalry spec 3.1: gallop / trot / walk); 0: the shape has one rate.</summary>
    public int TurnTrotDegPerS { get; init; }
    public int TurnWalkDegPerS { get; init; }
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

/// <summary>
/// The cavalry formation layer's constants (cavalry-and-mounted-war 3.2, P7-CV-A; <c>battle/cavalry.jsonc</c>, one <c>cavalry_rules</c> row). Distances in mm, speeds in mm a second, ticks 20 Hz fight ticks,
/// <c>_pm</c> permille, <c>_x100</c> morale points x100.
/// </summary>
[ContentType("cavalry_rules")]
public sealed record CavalryRulesDef : ContentDef
{
    // the charge sequence (4.1): walk while farther than WalkToMm, trot to GallopFromMm, gallop from there, couch at CouchAtMm
    public int WalkToMm { get; init; } = 200_000;
    public int GallopFromMm { get; init; } = 60_000;
    public int CouchAtMm { get; init; } = 20_000;
    /// <summary>The anchor runs at this share of the 20th-percentile gait of the front rank, so the riders can catch up (permille).</summary>
    public int AnchorPm { get; init; } = 950;
    /// <summary>The anchor's acceleration, mm a second each tick (a horse's own is 375).</summary>
    public int AnchorAccelPerTick { get; init; } = 225;
    /// <summary>A cavalry charge's longest approach (a 300 m charge walks for a minute): ticks.</summary>
    public int ChargeMaxTicks { get; init; } = 3000;
    // shock (4.1)
    public int CohesionShockPm { get; init; } = 700;
    public int ShockMinRiders { get; init; } = 10;
    public int ShockWindowTicks { get; init; } = 40;
    public int ShockX100 { get; init; } = 1500;
    public int ShockBracedX100 { get; init; } = 500;
    public int ShockBracedCohesionPm { get; init; } = 600;
    public int ShockRadiusMm { get; init; } = 5000;
    /// <summary>A rider is in contact when a foe stands this near (mm).</summary>
    public int ContactMm { get; init; } = 3000;
    // after contact (4.1)
    public int BreakthroughMmps { get; init; } = 4000;
    public int RideOnMm { get; init; } = 30_000;
    public int ReformMm { get; init; } = 80_000;
    public int ReformS { get; init; } = 15;
    public int StuckS { get; init; } = 4;
    /// <summary>Riders still on their way through give up on the ride-on after this many ticks.</summary>
    public int RideOnMaxTicks { get; init; } = 400;
    // the rider's slot keeping
    public int CatchAccelMmps2 { get; init; } = 5000;
    public int CatchMaxMmps { get; init; } = 8000;
    // captains (4.2)
    public int AvoidRefusalPm { get; init; } = 400;
    public int LaneMm { get; init; } = 30_000;
}
