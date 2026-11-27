namespace Ravensguard.Content;

/// <summary>
/// P7-SG-E (sieges-and-engines 4.8; content <c>siege/wagons.jsonc</c>): the numbers of the wagon fort. Times are game seconds, lengths millimetres. A fort is a ring of wagons, each driven to its slot at the drivers' pace,
/// unhitched, and chained to its neighbours; a chain is cut by an axeman or loosed for a sortie; the fort's standing cells count against a horse as braced polearms.
/// </summary>
[ContentType("wagon_rules")]
public sealed record WagonRulesDef : ContentDef
{
    /// <summary>The vehicle template of a war wagon.</summary>
    public string Template { get; init; } = "war_wagon";
    /// <summary>The gap between the end of one wagon and the next on the ring, where the chain hangs.</summary>
    public int GapMm { get; init; } = 1000;
    /// <summary>A wagon's pace to its slot: <c>drive_mmps x (drive_base_pm + drive_per_riding_pm x Riding) / 1000</c> (the driver's Riding is his driving).</summary>
    public int DriveMmps { get; init; } = 1500;
    public int DriveBasePm { get; init; } = 800;
    public int DrivePerRidingPm { get; init; } = 4;
    /// <summary>Unhitching and leading the team inside (a wagon's own men), and chaining a gap, each with this many men; cutting a chain with an axe.</summary>
    public int UnhitchS { get; init; } = 30;
    public int ChainS { get; init; } = 20;
    public int ChainMen { get; init; } = 2;
    public int CutS { get; init; } = 30;
    public int CutMen { get; init; } = 1;
    /// <summary>Permille of missiles a shooter behind a slit board is covered from (the board stops what is not at the slit).</summary>
    public int SlitCoverPm { get; init; } = 800;
    /// <summary>The arc in front of a horse in which the fort's standing cells count against it, each as one braced polearm (the mounted spec's 150 pm each), and the most that count.</summary>
    public int ArcMm { get; init; } = 6000;
    public int MaxBraced { get; init; } = 6;
    /// <summary>A ring needs at least this many wagons.</summary>
    public int MinWagons { get; init; } = 4;
}

/// <summary>One footing's say on a chariot (sieges-and-engines 4.9): the share of the wanted speed (permille), a cap in mm/s (0 none), whether it is forbidden, whether the car crashes on it at speed.</summary>
public sealed record ChariotGroundDef
{
    public int SpeedPm { get; init; } = 1000;
    public int MaxMmps { get; init; }
    public bool Forbidden { get; init; }
    public bool Crash { get; init; }
}

/// <summary>P7-SG-E (sieges-and-engines 4.9; content <c>siege/wagons.jsonc</c>): what a chariot does. Momentum is the horses' rule with the car's mass; the ground is the plan's: fast on open dry flat ground, nothing in woods, mud and ploughed fields.</summary>
[ContentType("chariot_rules")]
public sealed record ChariotRulesDef : ContentDef
{
    public string Template { get; init; } = "war_chariot";
    /// <summary>The momentum rule: <c>charge_k x kg x v_c x transfer / 1000</c> (the cavalry spec's constant, a spear from a car transfers 300 permille).</summary>
    public int ChargeK { get; init; } = 10;
    public int TransferPm { get; init; } = 300;
    /// <summary>A horse of the team, a man of the crew (kg), and the team's trample (the cavalry spec's constant) on a front man in passing.</summary>
    public int HorseKg { get; init; } = 450;
    public int CrewKg { get; init; } = 80;
    public int TrampleK { get; init; } = 8;
    /// <summary>The fastest a team pulls a car on the open, mm/s, and how fast it speeds up and slows, mm/s each game second.</summary>
    public int TopMmps { get; init; } = 10000;
    public int AccelMmps { get; init; } = 2000;
    public int BrakeMmps { get; init; } = 4000;
    /// <summary>Above this speed the car turns no faster than <c>turn_deg_per_s</c> degrees a second.</summary>
    public int TurnFromMmps { get; init; } = 4000;
    public int TurnDegPerS { get; init; } = 45;
    /// <summary>A crashed car throws each crewman as a fall from this height at its speed; ditch and stakes crash a car going faster than <see cref="CrashMinMmps"/>.</summary>
    public int FallHeightMm { get; init; } = 1000;
    public int CrashMinMmps { get; init; } = 3000;
    /// <summary>Chance (permille) a trampling horse's hooves land on the man in its path.</summary>
    public int TrampleHitPm { get; init; } = 500;
    /// <summary>The ground by footing id (the cavalry spec's names); a footing not listed is open ground at full speed.</summary>
    public Dictionary<string, ChariotGroundDef> Ground { get; init; } = [];
}
