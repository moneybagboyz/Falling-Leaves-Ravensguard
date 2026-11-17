using Ravensguard.Content;

namespace Ravensguard.Sim.Combat;

/// <summary>
/// DF attack momentum (plan §2.6 step 4): M = Size * Str * Vel / (10^6 * (1 + i_Size / (w_density * w_size))).
/// Arithmetic uses only + - * / on doubles (IEEE-exact, no transcendental functions), and every value that leaves the
/// resolver is truncated to integers, so results are identical on every platform.
/// </summary>
public static class Momentum
{
    public const double QuickMultiplier = 0.5;
    public const double HeavyMultiplier = 1.5;
    public const double ProneMultiplier = 2.0;

    /// <param name="strength">DF-scale strength (average 1250, range 0–5000).</param>
    /// <param name="velocity">Attack velocity multiplier.</param>
    /// <param name="speciesSizeCm3">Average size of the species (DF "Size", 60000 for dwarves, 70000 for humans).</param>
    /// <param name="creatureSizeCm3">The attacker's own size ("i_Size").</param>
    /// <param name="weaponMassMg">Weapon density (kg/m³) × volume (cm³), i.e. mass in mg (1.41e6 = 1.41 kg).</param>
    public static double Base(double strength, double velocity, double speciesSizeCm3, double creatureSizeCm3, double weaponMassMg)
        => speciesSizeCm3 * strength * velocity / (1_000_000.0 * (1.0 + creatureSizeCm3 / weaponMassMg));

    /// <summary>Skill 0–100 multiplies momentum from ×1 to ×2 (grand master).</summary>
    public static double SkillMultiplier(int skill) => 1.0 + Math.Clamp(skill, 0, 100) / 100.0;

    public static double StyleMultiplier(AttackStyle style) => style switch
    {
        AttackStyle.Quick => QuickMultiplier,
        AttackStyle.Heavy => HeavyMultiplier,
        _ => 1.0,
    };

    /// <summary>Fixed-point milli-units (momentum × 1000, truncated toward zero) for outputs and comparisons in tests.</summary>
    public static long Milli(double momentum) => (long)(momentum * 1000.0);
}
