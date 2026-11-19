namespace Ravensguard.Sim.Combat.Mounted;

/// <summary>
/// The momentum rules of mounted combat (mounted-combat.md 4.1, 4.3, 4.4) as pure integer functions: the charge's extra momentum, the brace's return, the trample,
/// the fall, the unhorse and refusal chances and the accuracy points. Momentum in milli-units, speeds in mm/s, chances in basis points or permille as named.
/// </summary>
public static class ChargeMath
{
    /// <summary>Closing speed rounded down to 0.1 m/s (spec 4.1), in mm/s.</summary>
    public static int RoundVc(int mmps) => mmps <= 0 ? 0 : mmps / 100 * 100;

    /// <summary><c>M_extra = charge_k x kg x v_c x weapon_pm / 1000</c> milli, v_c in mm/s rounded down to 0.1 m/s: 580 kg at 12 m/s with a couched lance is 69,600.</summary>
    public static long ExtraMilli(int chargeK, int totalKg, int vcMmps, int weaponPm)
    {
        long dm = RoundVc(vcMmps) / 100;                                  // decimetres per second
        return (long)chargeK * totalKg * dm * weaponPm / 10_000;
    }

    /// <summary>The braced polearm's return (4.4): <c>brace_return x charge_k x kg x v_c</c> (the horse impales itself): 0.8 x 69,600 = 55,680 at 12 m/s.</summary>
    public static long BraceReturnMilli(int chargeK, int totalKg, int vcMmps, int braceReturnPm) => ExtraMilli(chargeK, totalKg, vcMmps, 1000) * braceReturnPm / 1000;

    /// <summary>The horse's trample (4.1): <c>trample_k x horse kg x v_c</c> milli (480 kg at 10 m/s: 38,400).</summary>
    public static long TrampleMilli(int trampleK, int horseKg, int vcMmps) => (long)trampleK * horseKg * RoundVc(vcMmps) / 1000;

    /// <summary>A fall from the saddle (4.3, resolver 4.9): <c>100 x kg x (3 x h + 0.5 x v)</c> milli; 80 kg from 1.8 m at 12 m/s is 91,200, standing 43,200.</summary>
    public static long FallMilli(int kg, int speedMmps, int heightMm = 1800) => (long)kg * (3L * heightMm + Math.Max(0, speedMmps) / 2) / 10;

    /// <summary>The unhorse chance (4.3), basis points: <c>clamp((M_in - hold) x k, 0, max)</c>, <c>hold = base + Riding x per</c>; a hook multiplies before the clamp.</summary>
    public static int UnhorseBp(long momentumMilli, int riding, int holdBase = 100, int holdPerRidingPm = 1000, int kBpPerUnit = 80, int maxBp = 8000, int hookPm = 1000)
    {
        long hold = holdBase * 1000L + (long)Math.Max(0, riding) * holdPerRidingPm;
        long over = momentumMilli - hold;
        if (over <= 0) return 0;
        long bp = over * kBpPerUnit / 1000 * hookPm / 1000;
        return (int)Math.Min(maxBp, bp);
    }

    /// <summary>Refusal before braced polearms (4.4), permille: <c>clamp(base + per x n - tk x (trained - ref) - rk x Riding, 0, max)</c>, doubled in a thunderstorm before the clamp.</summary>
    public static int RefusalPm(int bracedPolearms, int trainedPm, int riding, int basePm = 200, int perPm = 150, int trainedK = 2, int trainedRef = 700, int ridingK = 2, int maxPm = 950, int stormPm = 1000)
    {
        long p = basePm + (long)perPm * bracedPolearms - (long)trainedK * (trainedPm - trainedRef) - (long)ridingK * riding;
        p = p * stormPm / 1000;
        return (int)Math.Clamp(p, 0, maxPm);
    }

    /// <summary>The rider's accuracy points while mounted (4.1): -5 a couched charge, -10 at a trot, -20 swinging at a gallop, reduced by <c>Riding / 150</c>.</summary>
    public static int AimPoints(int speedMmps, bool couched, int riding, int gallopMin = 9000, int trotMin = 4000, int charge = 5, int trot = 10, int gallopSwing = 20)
    {
        int pen = speedMmps >= gallopMin ? (couched ? charge : gallopSwing) : speedMmps >= trotMin ? trot : 0;
        return -(pen * (150 - Math.Clamp(riding, 0, 150)) / 150);
    }

    /// <summary>Thrown from a rearing horse each second (4.2), permille: <c>clamp(40 - Riding / 3, 5, 40)</c> percent (a skill-50 rider 23.3%).</summary>
    public static int RearThrowPm(int riding, int basePct = 40, int div = 3, int minPct = 5, int maxPct = 40) => Math.Clamp(basePct * 10 - (riding * 10 + Math.Max(1, div) / 2) / Math.Max(1, div), minPct * 10, maxPct * 10);

    /// <summary>
    /// Panic from new wounds (4.2): the sum of the wound terms <c>x (1 + temper / div)</c>, and <c>x damp</c> when trained to war, rounded half up:
    /// a cut and a torn muscle on a temper-10, trained-900 horse are <c>(80 + 150) x 1.05 x 0.6 = 145</c>.
    /// </summary>
    public static int PanicOf(int rawPm, int temper, int trainedPm, int temperDiv = 200, int trainedAt = 900, int dampPm = 600)
    {
        if (rawPm <= 0) return 0;
        long num = (long)rawPm * (temperDiv + temper) * (trainedPm >= trainedAt ? dampPm : 1000);
        long den = (long)temperDiv * 1000;
        return (int)((num + den / 2) / den);
    }

    /// <summary>The factor (permille) that turns hit chance by <paramref name="pm"/> for a skill delta, as situation points: <c>(500 + 5 d) x (pm - 1000) / 5000</c>.</summary>
    public static int ScalePoints(int delta, int pm) => (int)((500L + 5L * delta) * (pm - 1000) / 5000);
}
