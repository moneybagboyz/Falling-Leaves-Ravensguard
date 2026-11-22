namespace Ravensguard.Content;

/// <summary>
/// P7-SG-C (sieges-and-engines 4.4, 4.5; content <c>content/base/siege/assault.jsonc</c>): the numbers of an assault on several levels. Times are fight ticks (20 a second), lengths millimetres, chances permille. A level is
/// <see cref="LevelMm"/> high: the wall-walk at +1 stands 3 m up and a tower's top floor is level 1 as well.
/// </summary>
[ContentType("assault_rules")]
public sealed record AssaultRulesDef : ContentDef
{
    public int LevelMm { get; init; } = 3000;
    // ---- ladders ----
    /// <summary>Ticks two carriers take to raise a ladder (4 s), the climbers it holds at once, ticks to climb it (4 s), the ladder's length.</summary>
    public int LadderRaiseTicks { get; init; } = 80;
    public int LadderClimbers { get; init; } = 3;
    public int LadderClimbTicks { get; init; } = 80;
    public int LadderLenMm { get; init; } = 6000;
    /// <summary>A defender at the top pushes it off in this many ticks (3 s); a try succeeds with <c>PushBasePm + PushPerSkillPm x (Athletics - PushSkillBase)</c> permille.</summary>
    public int PushTicks { get; init; } = 60;
    public int PushBasePm { get; init; } = 500;
    public int PushPerSkillPm { get; init; } = 10;
    public int PushSkillBase { get; init; } = 30;
    public int PushReachMm { get; init; } = 1500;
    // ---- height, merlons ----
    /// <summary>A shooter a level up: range x this permille and the aim sigma x <see cref="HeightSigmaPm"/> (about five points more hits for a skill-50 archer at 30 m).</summary>
    public int HeightRangePm { get; init; } = 1150;
    public int HeightSigmaPm { get; init; } = 800;
    /// <summary>The share of missiles from below that a parapet's merlon stops.</summary>
    public int MerlonPm { get; init; } = 600;
    // ---- murder holes ----
    public int HoleEveryTicks { get; init; } = 200;
    public int DropKg { get; init; } = 20;
    /// <summary>The burn a pour makes (1 scorch, 2 burn, 3 deep burn).</summary>
    public int ScaldIntensity { get; init; } = 2;
    public int HoleCellMm { get; init; } = 1000;
    // ---- towers ----
    public int BoardTicks { get; init; } = 120;
    // ---- the gate ----
    /// <summary>What a beam behind a gate adds to the gate's impact threshold (kg m/s).</summary>
    public int GateBeamThreshold { get; init; } = 300;
    // ---- footing ----
    public int RubbleWalkPm { get; init; } = 500;
    // ---- losing the wall (4.4) ----
    public int HeldWalkCells { get; init; } = 10;
    public int HeldBreachMen { get; init; } = 20;
    public int HeldMoraleX100 { get; init; } = 1500;
    // ---- sallies (4.5) ----
    /// <summary>A sally goes out when engines stand within this range and the sally is at least <see cref="SallyOverPm"/> permille of the guard; at night when it can (from, to hour).</summary>
    public int SallyRangeMm { get; init; } = 150_000;
    public int SallyOverPm { get; init; } = 1000;
    public int NightFromHour { get; init; } = 20;
    public int NightToHour { get; init; } = 5;
    /// <summary>How near a sally man gets to an engine's cell to light it (mm), and the ticks a torch takes to catch.</summary>
    public int SallyReachMm { get; init; } = 1500;
    public int SallyTorchTicks { get; init; } = 40;
}
