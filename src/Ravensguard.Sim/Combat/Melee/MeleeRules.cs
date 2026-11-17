using Ravensguard.Content;

namespace Ravensguard.Sim.Combat.Melee;

public enum WeaponClass { Dagger, Sword, Axe, Maul, Spear }

/// <summary>Real-time timing of one weapon class (plan §4A.3 table). Seconds at skill 0, no encumbrance.</summary>
public readonly record struct WeaponTiming(double WindupS, double RecoverS, double StrikeS, double ReachM);

/// <summary>Constants of the real-time melee layer (plan §4A.3 / §2.8). One place so tuning is a diff of this file.</summary>
public static class MeleeRules
{
    public const int TickHz = 20;
    public const double Dt = 1.0 / TickHz;

    // --- geometry ---
    public const double BodyRadius = 0.4;
    public const double WalkSpeed = 3.0, SprintSpeed = 5.0, DodgeSpeed = 4.3;
    public const double FrontArcCos = 0.6427876;   // cos 50°: the 100° frontal arc
    public const double RearArcCos = -0.7071068;   // cos 135°: the 90° rear arc
    public const double AttackConeCos = 0.5;       // a blow lands only on a target within ±60° of the attacker's facing

    // --- parry / dodge / feint ---
    public const int ParryWindowTicks = 4;         // 0.20 s before impact
    public const int RiposteTicks = 8;             // 0.4 s
    public const int DodgeTicks = 7;               // 0.35 s
    public const int DodgeCooldownTicks = 20;      // 1.0 s, counted after the dodge ends
    public const double DodgeEvadeChance = 0.8;
    public const int FeintRecoverTicks = 3;
    public const int HeavyHoldTicks = 6;           // held 0.3 s past wind-up = heavy swing (+50% momentum)
    public const int MaxHoldTicks = 20;
    public const double RiposteWindupScale = 0.5;

    // --- shield / weapon block coverage ---
    public const double HeaterCover = 0.45, TowerCover = 0.75, WallBonusPerNeighbour = 0.10;
    public const double WeaponBlockBase = 0.30, WeaponBlockPerSkill = 0.002;
    public const double ExhaustedCoverLoss = 0.20;

    // --- stamina (plan §4A.3) ---
    public const double StaminaMax = 100, ExhaustedBelow = 25, ExhaustedWindupScale = 1.3;
    public const double SwingBase = 4, SwingPerKg = 2, BlockBase = 3, BlockPerJoule = 1.0 / 50, SprintPerS = 8;
    public const double RegenRest = 12, RegenGuard = 5, HeavyArmourKg = 20;
    public const double DodgeCost = 12, FeintCost = 2;

    // --- blood / pain / morale ---
    public const double ClotHalfLifeS = 90;
    /// <summary>Per-tick multiplier giving a 90 s half-life (1 - ln2 * dt / 90, first-order; no transcendental calls in the sim).</summary>
    public const double ClotPerTick = 1.0 - 0.693147 * Dt / ClotHalfLifeS;
    public const double PainDecayPerS = 0.02;
    public const double PainDecayPerTick = 1.0 - PainDecayPerS * Dt;
    public const double ShockWeak = 0.15, ShockCollapse = 0.30, ShockUnconscious = 0.40, ShockDead = 0.50;
    public const double RoutBelow = 20, SurrenderBelow = 10, RallyAbove = 40;
    public const int MoraleEveryTicks = 10; // 2 Hz
    public const double FriendDeathPenalty = 6, FlankedPenalty = 15, RoutingNeighbourPerS = 2, HitTakenPenalty = 2, LimbLossPenalty = 15;
    public const double PenaltyDecayPerS = 0.4;

    public static double KnockoutPain(int toughness) => 60 + 4.0 * toughness;

    private static readonly WeaponTiming Dagger = new(0.30, 0.30, 0.10, 0.5);
    private static readonly WeaponTiming Sword = new(0.45, 0.40, 0.10, 0.9);
    private static readonly WeaponTiming Axe = new(0.60, 0.60, 0.15, 0.9);
    private static readonly WeaponTiming Maul = new(0.90, 0.90, 0.15, 1.1);
    private static readonly WeaponTiming Spear = new(0.40, 0.50, 0.10, 1.9);

    public static WeaponTiming Timing(WeaponClass c) => c switch
    {
        WeaponClass.Dagger => Dagger, WeaponClass.Sword => Sword, WeaponClass.Axe => Axe, WeaponClass.Maul => Maul, _ => Spear,
    };

    public static WeaponClass ClassOf(string shapeId) => shapeId switch
    {
        "dagger" => WeaponClass.Dagger,
        "arming_sword" or "longsword" => WeaponClass.Sword,
        "maul" => WeaponClass.Maul,
        "spear" => WeaponClass.Spear,
        _ => WeaponClass.Axe, // axe, mace, war hammer, club
    };

    /// <summary>Seconds to whole ticks (≥1), rounding half up.</summary>
    public static int Ticks(double seconds) => Math.Max(1, (int)Math.Round(seconds * TickHz, MidpointRounding.AwayFromZero));

    /// <summary>The skill scaling of plan §4A.3: ×(1 − skill/400).</summary>
    public static double SkillScale(int skill) => 1.0 - Math.Clamp(skill, 0, 100) / 400.0;
}
