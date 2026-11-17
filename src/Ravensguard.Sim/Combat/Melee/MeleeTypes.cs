using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Combat.Melee;

/// <summary>Metres, y grows downward (screen convention). Doubles with + - * / and sqrt only.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double Dot(Vec2 o) => X * o.X + Y * o.Y;
    public Vec2 Normalized() { double l = Length; return l < 1e-9 ? Zero : new Vec2(X / l, Y / l); }
    public bool IsZero => X == 0 && Y == 0;
}

public enum Act { Idle, Windup, Strike, Recover, Block, Dodge, Stagger, Down, Dead, Fled, Surrendered }
public enum DownReason { None, Pain, Blood, Heart, Concussion }
public enum Outcome { Death, Knockout, Rout, Surrender, Stalemate }

public sealed record FighterSpec(
    string Name, int Team, WeaponInstance Weapon, IReadOnlyList<WornItem> Worn, Vec2 Start,
    int Skill = 40, int Strength = 1250, int Toughness = 5, double ShieldCover = 0, int Courage = 70, string Sprite = "person.soldier_red")
{
    public Vec2 FaceDir { get; init; } = new(1, 0);
}

public sealed class AttackRequest
{
    public string PartId = "upper_body";
    public string AttackId = "";
    public int TargetId = -1;            // -1 = nearest enemy in the attack cone at impact
    public string? Gap;
    public bool AutoRelease = true;      // AI: release as soon as the wind-up completes
}

/// <summary>One fighter's intent this tick. Persistent fields stay until changed; one-shot fields are consumed by the world.</summary>
public sealed class MeleeCommand
{
    public Vec2 Move;                    // desired walk direction (unit or zero)
    public bool Sprint;
    public Vec2 Face;                    // desired facing (unit or zero = keep)
    public bool Block;                   // held
    public bool Hold;                    // attack button held (player wind-up)
    public AttackRequest? Start;         // one-shot: begin a wind-up
    public bool Feint;                   // one-shot
    public Vec2? Dodge;                  // one-shot: dodge direction
    public void ClearOneShots() { Start = null; Feint = false; Dodge = null; }
}

public enum EventKind { Windup, Feint, Whiff, Blocked, Parried, Dodged, Hit, Wound, Down, Wake, Death, Rout, Rally, Surrender, Fled, Riposte, Info }

public sealed record MeleeEvent(int Tick, EventKind Kind, int Actor, int Target, string Text, string? PartId = null, bool Severe = false);

public sealed class ActiveWound
{
    public required string PartId, TissueId; public string? StructureId;
    public double Rate;                  // ml per sim-second
    public bool Clots;
    public bool Severed;
}

public sealed class Fighter
{
    public int Id { get; init; }
    public required FighterSpec Spec { get; init; }
    public string Name => Spec.Name;
    public int Team => Spec.Team;
    public bool IsPlayer;                // true: world never drives it (no AI, no forced rout)
    public MeleeCommand Cmd { get; } = new();

    // derived
    public required BodyPlan Plan { get; init; }
    public required WeaponStats WeaponStats { get; init; }
    public WeaponClass Class { get; init; }
    public WeaponTiming Timing { get; init; }
    public double ArmourKg, WeaponKg, OverKg;
    public double MaxBlood;
    public double[] AreaWeights = [], VulnWeights = [];
    public string[] PartIds = [];

    // position
    public Vec2 Pos, Face;

    // action
    public Act State = Act.Idle;
    public int StateTicks;               // remaining ticks of a timed state
    public AttackRequest? Req;
    public int WindupElapsed, WindupNeeded, WindupStartTick;
    public bool Heavy, RiposteWindup;
    public int BlockSince = -1000, DodgeReadyTick, RiposteUntil = -1;
    public Vec2 DodgeDir;
    public int ParriedBonusTicks;

    // vitals
    public double Stamina = MeleeRules.StaminaMax, Blood, Pain, MoralePenalty, Morale;
    public List<ActiveWound> Wounds { get; } = [];
    public DownReason Down;
    public int CollapseTick = -1;        // heart wound: collapse time
    public bool Routing, Weakened, WeaponArmLost, ShieldLost, LegsLost;
    public int FlankedUntil = -1;
    public bool Announced;               // death/down already counted against neighbours' morale
    public int WoundsTaken, HitsLanded, BlowsThrown;
    public int FledOrSurrenderTick = -1;

    // AI memory
    public int AiTarget = -1, AiNextAttackTick, AiBlockUntil, AiDecidedEnemy = -1, AiDecidedStart = -1;

    public bool Alive => State != Act.Dead;
    /// <summary>Still in the fight (not down, dead, fled or surrendered).</summary>
    public bool Active => State is not (Act.Down or Act.Dead or Act.Fled or Act.Surrendered);
    public bool CanAct => Active && State != Act.Stagger;
    public double Reach => Timing.ReachM;
    public double BloodLossFrac => 1.0 - Blood / MaxBlood;
    public double BleedRate { get { double r = 0; foreach (var w in Wounds) r += w.Rate; return r; } }
    public double KnockoutPain => MeleeRules.KnockoutPain(Spec.Toughness);
    public double ShieldCover => ShieldLost ? 0 : Spec.ShieldCover;
}
