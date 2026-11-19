using Ravensguard.Content;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;

namespace Ravensguard.Sim.Combat.Melee2;

/// <summary>Something carried to throw: a javelin, a dagger, a hand axe, a rock (melee-and-ranged 4.6).</summary>
public sealed record ThrowStock(string Shape, string Material, int Count, bool Improvised = false);

/// <summary>
/// Who fights with what (the input of <see cref="FightWorld.Add"/>): the weapon, shield, worn pieces, launcher and ammunition, things to throw, the skills by weapon
/// family, the body (strength, toughness, size), the stance and the place. Built by <see cref="FightSetup"/> from a person, or by hand in tests and the harness.
/// </summary>
public sealed record FighterKit
{
    public string Name { get; init; } = "";
    public int Team { get; init; }
    /// <summary>The person this fighter is (the wound book key); 0 for a harness fighter (the book is the fight's own).</summary>
    public ulong Person { get; init; }
    /// <summary>The weapon in hand; null for bare hands (an improvised object, if any, is held instead).</summary>
    public WeaponInstance? Weapon { get; init; }
    public string? Shield { get; init; }
    public IReadOnlyList<WornItem> Worn { get; init; } = [];
    public string? Launcher { get; init; }
    public string? Ammo { get; init; }
    public int AmmoCount { get; init; }
    public IReadOnlyList<ThrowStock> Throwables { get; init; } = [];
    public string? Improvised { get; init; }
    /// <summary>Weapon-family skill of the weapon in hand (Blades, Axes, Hafted, Polearms; Melee for bare hands), Shields, Throwing, Archery.</summary>
    public int Skill { get; init; } = 40;
    public int ShieldSkill { get; init; } = -1;
    public int ThrowSkill { get; init; } = -1;
    public int ArcherySkill { get; init; } = -1;
    public int Strength { get; init; } = 1250;
    public int Toughness { get; init; } = 10;
    public int SizeCm3 { get; init; } = 70000;
    public int Courage { get; init; } = 70;
    public string Stance { get; init; } = "balanced";
    public string Plan { get; init; } = "human";
    /// <summary>Start position (mm) and facing (any vector; normalised).</summary>
    public int X { get; init; }
    public int Y { get; init; }
    public int FaceX { get; init; } = FightMath.One;
    public int FaceY { get; init; }
    /// <summary>Practice weapons (blunted x0.5 momentum): the yard, tournaments.</summary>
    public bool Blunted { get; init; }
    /// <summary>The Tactics skill (a squad leader's picks the side's formation, watch-mode 4.4); -1 when unknown.</summary>
    public int Tactics { get; init; } = -1;
}

/// <summary>The AI's memory of one fighter (live only, rebuilt from nothing after a load: every field is re-derived within a few ticks).</summary>
public struct BrainMemory
{
    public int Target;
    public int NextThink, AttackAfter, ReactSerialFoe, ReactSerial, ReactAt, HoldBlockUntil, FeintChecked;
    public byte React;     // 0 none, 1 parry, 2 dodge, 3 block, 4 take
    public int PatienceSet;
}

/// <summary>One combatant (melee-and-ranged 3.3). Positions in mm, facing Q10, stamina x100; everything the fight step reads is an integer.</summary>
public sealed class Fighter
{
    public int Id { get; init; }
    public required FighterKit Kit { get; init; }
    public string Name => Kit.Name;
    public int Team => Kit.Team;
    public ulong Person => Kit.Person;
    /// <summary>Driven by the player's inputs, not the brain (the hero; watch mode sets it false).</summary>
    public bool Player;
    public FightCommand Cmd { get; } = new();

    // ---- the kit as the step reads it ----
    public required WeaponV2 Weapon;
    public string Family = "";
    public int ReachMm, StrikeTicks, BaseWindTicks, BaseRecoverTicks;
    public ShieldV2? Shield;
    public int ShieldPm = 1000;
    public required WornV2[] Worn;
    public int ArmourG, OverKg;
    public LauncherDef? Launcher;
    public AmmoDef? AmmoDef;
    public WeaponV2? AmmoWeapon;
    public int Ammo;
    public bool Loaded;
    public readonly List<ThrowStock> Throwables = [];
    public ImprovisedDef? Improvised;
    public int ImprovisedPm = 1000, ImprovisedBlows;
    public required BodyPlanV2 Plan;
    public required WoundBook Book;
    public required CombatStanceDef Stance;
    public int Skill, ShieldSkill, ThrowSkill, ArcherySkill;

    // ---- position ----
    public int X, Y, FX, FY;
    public int BraceX, BraceY, BraceSince, UnbracedUntil = -1;
    public int LastMoveMm;

    // ---- the action ----
    public FAct Act;
    public int ActTicks;
    public int WindNeeded, WindElapsed, WindStart;
    public bool Heavy, Riposte;
    public AttackReq Req;
    /// <summary>Swing serial: counts every blow begun (the resolver's draw key, the AI's reaction memory).</summary>
    public uint Serial;
    public int ImpactAt = -1;
    public int Stamina;
    public int DodgeReady, DodgeX, DodgeY;
    public int BlockSince = -1000, RiposteUntil = -1, FeintReady, ParryDeadUntil = -1;
    // ranged
    public int DrawElapsed, AimSince = -1, ReloadLeft;
    public uint ShotSerial;
    public int ThrowTargetX, ThrowTargetY;

    // ---- the body now (from the wound book) ----
    public bool WeaponArm = true, ShieldArm = true, CanStand = true, OneEye;
    public int WalkPm = 1000, StrengthPm = 1000, BlowPm = 1000;
    public ulong MissingMask;
    internal int WoundsSeen = -1;
    public bool Crawling;

    // ---- morale (the spike's rules until morale-capture-succession) ----
    public int Morale, MoralePenalty;
    public bool Routing, Announced;
    public int FlankedUntil = -1, EndedAt = -1;
    /// <summary>P4-D hook: the morale model's state (morale-capture-succession 3.1; Morale/FightWorld.Morale.cs). <see cref="Morale"/> and <see cref="Routing"/> mirror it for readers.</summary>
    public Ravensguard.Sim.Combat.Morale.MoraleState M;

    // ---- statistics ----
    public int Thrown, Landed, BlockedBy, ParriedBy, DodgedBy, Whiffs;

    public BrainMemory Brain;

    // ---- watch-mode (P4-F): orders and the treat job (Combat/Watch) ----
    /// <summary>The fighter the protect stance guards (-1: none); set by the stance, an order ("protect X") or the setup.</summary>
    public int ProtectFighter = -1;
    /// <summary>Close-out item 1: inside a longer weapon's reach (its point parried or dodged) until this tick, against fighter <see cref="InsideOf"/>.</summary>
    public int InsideUntil = -1, InsideOf = -1;
    /// <summary>Busy with a treatment (pressure, bandage) until this tick.</summary>
    public int TreatBusyUntil;
    /// <summary>The weapon was put away for bare hands by a no-lethal stance (a blade without a flat, a javelin): the kit's weapon comes back when the stance changes.</summary>
    public bool Sheathed;

    public bool Alive => Act != FAct.Dead;
    /// <summary>Still in the fight (not down, dead, fled or yielded).</summary>
    public bool Active => Act is not (FAct.Down or FAct.Dead or FAct.Fled or FAct.Surrendered);
    public bool CanAct => Active && Act != FAct.Stagger;
    public bool Guarding => Act == FAct.Block;
    public bool Ranged => Launcher != null;
    public int StaminaMax => 10000;
}
