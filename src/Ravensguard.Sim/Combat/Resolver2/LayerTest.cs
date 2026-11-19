using Ravensguard.Content;

namespace Ravensguard.Sim.Combat.Resolver2;

/// <summary>
/// The integer forms of the DF momentum rules (resolver-v2 3.1 and 4.3, plan §2.6 step 4). Every function is pure integer arithmetic with floor
/// division in a fixed order (test RS-24); momentum and needs are milli-units, areas milli-cm2, stresses DF raw kPa, strains per 100,000.
/// </summary>
public static class LayerTest
{
    /// <summary>x x pm / 1000, rounded half up (the momentum multipliers: skill, style, prone; RS-1's heavy 234,788).</summary>
    public static long MulPm(long x, long pm) => (x * pm + 500) / 1000;

    /// <summary>
    /// Initial momentum before skill and style: <c>((sp x str / 1000 x velPm) x mass / (mass + size)) / 1000</c> (DF Weapon page:
    /// M = Size x Str x Vel / (10^6 x (1 + i_Size / (w_density x w_size))); weight matters little once the weapon is heavy relative to the wielder).
    /// </summary>
    public static long BaseMomentumMilli(int speciesSizeCm3, int strength, int velPm, long massMg, int sizeCm3)
    {
        if (massMg <= 0) return 0;
        long a = (long)speciesSizeCm3 * strength / 1000 * velPm;
        return (long)((Int128)a * massMg / (massMg + sizeCm3) / 1000);
    }

    /// <summary>
    /// The edged need, milli-units: <c>(rSY + (A + 1) x rSF) x (10 + 2 Qa) x Cm x CutResist / (S x Qw)</c>, with rSY and rSF the layer-to-weapon
    /// shear ratios (ppm here), A the contact area, Qa and Qw the quality multipliers, Cm the condition multiplier, S the weapon's max edge.
    /// </summary>
    public static long CutNeedMilli(long layerSY, long layerSF, MaterialV2 weapon, int qwPm, long areaMilli, int qaPm, int cmPm, int resistPm)
    {
        if (weapon.ShearYield <= 0 || weapon.ShearFracture <= 0 || weapon.SharpPm <= 0 || qwPm <= 0) return long.MaxValue;
        long rsy = layerSY * 1_000_000 / weapon.ShearYield;
        long rsf = layerSF * 1_000_000 / weapon.ShearFracture;
        long basePpm = rsy + (areaMilli + 1000) * rsf / 1000;
        long need = basePpm * (10_000 + 2L * qaPm) / 1_000_000;
        need = need * cmPm / 1000;
        need = need * resistPm / 1000;
        return (long)Int128.Min(long.MaxValue, (Int128)need * 1_000_000 / ((long)weapon.SharpPm * qwPm));   // Int128: the four products before it can grow past long for an extreme pairing (review W4)
    }

    /// <summary>The blunt need, milli-units: <c>(2 IF - IY) x (2 + 0.4 Qa) x A x Cm x CrushResist</c> (IF and IY in MPa = raw / 10^6; floored at 1 raw).</summary>
    public static long BluntNeedMilli(long impactFracture, long impactYield, long areaMilli, int qaPm, int cmPm, int resistPm)
    {
        long bb = 2 * impactFracture - impactYield;
        if (bb < 1) bb = 1;
        long t = bb * (2000 + 2L * qaPm / 5) / 1000;
        t = t * areaMilli / 1000;
        t = t * cmPm / 1000;
        t = t * resistPm / 1000;
        return t / 1000;
    }

    /// <summary>The deflection test (worn layers only): a blunt or failed-edge blow bounces when <c>2 x weapon volume x weapon IY &lt; A x layer density</c>.</summary>
    public static bool Deflects(WeaponV2 w, long areaMilli, int layerDensityKgM3)
    {
        long lhs = 2 * w.VolumeMilli * w.Head.ImpactYield / 1_000_000;
        long rhs = areaMilli * layerDensityKgM3 / 1000;
        return lhs < rhs;
    }

    /// <summary>Momentum after a cut (rule C1 conservation): <c>max(0, min(keep x M, M - cost x need))</c>.</summary>
    public static long AfterCut(long m, long need, CalibrationV2 c)
    {
        long a = m * c.KeepPm / 1000, b = m - need * c.CutCostPm / 1000;
        long r = a < b ? a : b;
        return r < 0 ? 0 : r;
    }

    /// <summary>Momentum after a crush: elastic layers keep <c>keep x M</c> (mail does not soften a blow), others <c>min(keep x M, M - cost x need)</c>; then padding absorbs (rule C3).</summary>
    public static long AfterCrush(long m, long need, bool elastic, int absorbPm, CalibrationV2 c)
    {
        long r = m * c.KeepPm / 1000;
        if (!elastic) { long b = m - need * c.CrushCostPm / 1000; if (b < r) r = b; }
        if (r < 0) r = 0;
        return r * (1000 - absorbPm) / 1000;
    }

    /// <summary>The failure conversion: a blow that fails both tests keeps <c>M x min(1, strain / 50,000)</c> (rigid metal soaks 98 to 99.6%).</summary>
    public static long AfterFail(long m, int strain, CalibrationV2 c)
    {
        int s = strain < c.StrainReference ? strain : c.StrainReference;
        return m * s / c.StrainReference;
    }

    /// <summary>Fracture grade from the ratio (permille) of the momentum entering the bone to its crush need (resolver-v2 4.6).</summary>
    public static BoneGrade Fracture(long ratioPm, CalibrationV2 c) =>
        ratioPm >= c.ShatteredPm ? BoneGrade.Shattered : ratioPm >= c.DisplacedPm ? BoneGrade.Displaced : ratioPm >= c.BrokenPm ? BoneGrade.Broken
        : ratioPm >= c.CrackedPm ? BoneGrade.Cracked : BoneGrade.Sound;

    /// <summary>Condition multiplier Cm = (500 + cond / 2) / 1000, permille (tattered armour needs about half the momentum).</summary>
    public static int ConditionPm(int conditionPm) => 500 + (conditionPm < 0 ? 0 : conditionPm > 1000 ? 1000 : conditionPm) / 2;

    /// <summary>Organ reach multiplier, permille: point 2000, edge 1000 + A/20 capped at 4000, blunt 500 (v1's rule).</summary>
    public static int ReachPm(AttackType t, long areaMilli) => t switch
    {
        AttackType.Point => 2000,
        AttackType.Edge => (int)Math.Min(4000, 1000 + Math.Min(areaMilli, 60_000) / 20),
        _ => 500,
    };

    /// <summary>Structure width multiplier, permille: <c>1 + min(A, 60) / 20</c>.</summary>
    public static int WidthPm(long areaMilli) => (int)(1000 + Math.Min(areaMilli, 60_000) / 20);

    /// <summary>Fall momentum (resolver-v2 4.9): <c>100 x mass_kg x (3 x height_m + 0.5 x speed_mps)</c> milli-units (80 kg from 3 m = 72,000).</summary>
    public static long FallMomentumMilli(int massKg, int heightMm, int speedMmPerS) => 100L * massKg * (3L * heightMm + speedMmPerS / 2) / 1000;
}
