using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Mounted;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;

namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>Where a soldier ends (3.3): still standing, down (alive, out of the fight), dead, fled, yielded in the fight, taken prisoner at the aftermath.</summary>
public enum SoldierStatus : byte { Active, Down, Dead, Fled, Surrendered, Captured }

/// <summary>Where a soldier stands against the enemy (3.3): the front line, round a flank, behind it.</summary>
public enum BattlePosition : byte { Front, Flank, Rear }

/// <summary>The leader's and the notables' fate (4.6).</summary>
public enum BattleFate : byte { Unhurt, Wounded, Down, Dead, Fled, Captured, Surrendered }

/// <summary>
/// One soldier of an abstract battle (3.3 <c>BattleSide.Soldiers</c>): the person, the classes the tables are keyed by, skill, the body (its own <see cref="WoundBook"/>, a copy of the
/// person's if he was already hurt), status, morale and what hit him last (the cause chain). Integers only.
/// </summary>
public sealed class Soldier
{
    public int Index;
    public int Side;
    public required FighterKit Kit;
    public ulong Person => Kit.Person;
    public string Name => Kit.Name;
    public WeaponClass Melee;
    public WeaponClass? Shooter;
    public ArmourClass Armour;
    public int Skill, ArcherySkill;
    public bool Shield, Mounted, Braces;
    public int Ammo, Throws;
    public int CourageX100;
    public required WoundBook Book;
    public required BodyPlanV2 Plan;
    public SoldierStatus Status;
    public bool Routing;
    public int RoutStep = -1, OutStep = -1;
    /// <summary>0 front, 1 middle, 2 rear (the hit weights of 4.1 d).</summary>
    public int Row;
    public BattlePosition Position;
    public int PenaltyX100, RoutAccX100, MoraleX100;
    public bool Captain;
    public OutcomeClass Worst;
    public int HitsTaken, HitsThisStep, WoundsSeen = -1;
    public bool WeaponArm = true, ShieldArm = true, CanStand = true;
    public int BlowAccPm, ShotAccPm;
    /// <summary>The opponent he keeps striking in the press (-1 none), and how many keep striking him.</summary>
    public int Target = -1, TargetedBy;
    /// <summary>Who wounded him last, with what (the cause chain of a named casualty): soldier index or -1, and the class.</summary>
    public int LastHitBy = -1;
    public WeaponClass LastHitWith;
    public OutcomeClass LastHitClass;
    public bool Active => Status == SoldierStatus.Active;
    public bool InLine => Status == SoldierStatus.Active && !Routing;
}

/// <summary>Everything an abstract battle needs (built from a <see cref="FightSetup"/> or a scenario): the kits in setup order, who rides, the ground and weather, what the sides are,
/// the seed and the battle id, the game second it starts, what caused it, the place's name, whether each side's most skilled leads it.</summary>
public sealed record BattleInput(IReadOnlyList<FighterKit> Kits, ulong Seed, ulong BattleId, long GameSec, string Cause = "")
{
    public IReadOnlySet<int> Mounted { get; init; } = new HashSet<int>();
    public FightTerrain? Terrain { get; init; }
    public string Weather { get; init; } = "";
    public IReadOnlyDictionary<int, SideKind> SideKinds { get; init; } = new Dictionary<int, SideKind>();
    public string Place { get; init; } = "";
    public bool Leaders { get; init; } = true;
    /// <summary>Narrow ground (a bridge, a gate, a pass: 4.5) and a fight at night or underground.</summary>
    public bool Narrow { get; init; }
    public bool Night { get; init; }
    /// <summary>The first side stands uphill of the second (+1), below it (-1) or level (0).</summary>
    public int Uphill { get; init; }
    /// <summary>The first aid and the aftermath bleeding (4.1.5); off for the equivalence runs that compare the battle itself.</summary>
    public bool Aftermath { get; init; } = true;
    /// <summary>First aid by the side holding the field at the aftermath; off to measure the untreated bleed-out (AB-11).</summary>
    public bool FirstAid { get; init; } = true;
    /// <summary>Existing wound books by person (copied, never changed: the battle's books are applied at the barrier).</summary>
    public Func<ulong, WoundBook?>? BookOf { get; init; }
}

/// <summary>One 15 (here 5) second step of the report's timeline strip: casualties of each side in it.</summary>
public readonly record struct BattleStepLine(int Step, int Down0, int Down1, int Fled0, int Fled1, int Blows0, int Blows1);

/// <summary>A notable's fate (4.6): the person, how it ended, and the cause chain's links (the wound class, the opponent's weapon class, the battle, the cause event).</summary>
public sealed record BattleNotable(ulong Person, string Name, int Side, BattleFate Fate, OutcomeClass Wound, WeaponClass By, bool Leader);

/// <summary>One side's tally (3.3 <c>BattleReport.Casualties</c>): the start, the dead, down (alive and out), captured, fled, yielded, standing, the wound-class counts of its casualties,
/// and those who bled to death after the fight.</summary>
public sealed class SideTally
{
    public int Start, Dead, Down, Captured, Fled, Surrendered, Standing, DiedAfter;
    public readonly int[] WorstClass = new int[AbstractClasses.Outcomes];
    /// <summary>Dead and down (the spec's casualties, 4.4), counted at the end of the fight before the aftermath.</summary>
    public int Casualties;
}

/// <summary>
/// The report of an abstract battle (3.3): where, when, who won, the strengths, the casualties, the notables, how long it lasted (sim-seconds), the cause, the quality factor (x100,
/// 4.2) and the timeline. <see cref="Report"/> is always true: a battle nobody watched is told as word that reached somewhere (2.3).
/// </summary>
public sealed class BattleReport
{
    public ulong Id;
    public string Place = "";
    public long GameSec;
    public int Winner = -1;
    public readonly SideTally[] Sides = [new(), new()];
    public readonly List<int> Teams = [];
    public readonly List<BattleNotable> Notables = [];
    public int DurationS;
    public int ContactS = -1;
    public string Cause = "";
    public int QualityX100;
    public bool Stalemate;
    public readonly List<BattleStepLine> Timeline = [];
    public bool Report => true;
    public ulong Hash;
}

/// <summary>What a battle changes, applied at the tick barrier in stable person order (3.3 <c>BattleCommands</c>).</summary>
public enum BattleCommandKind : byte { Wounds, Status, Loot, Ransom, Chronicle }

/// <summary>One command: the person, the kind, the status (for <see cref="BattleCommandKind.Status"/>), the person's new wound book (for <see cref="BattleCommandKind.Wounds"/>), the
/// side that took him or his things.</summary>
public sealed record BattleCommand(ulong Person, BattleCommandKind Kind, SoldierStatus Status = SoldierStatus.Active, WoundBook? Book = null, int ByTeam = -1);
