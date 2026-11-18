namespace Ravensguard.Sim.Inventory;

/// <summary>
/// Carrying (trade-inventory-character spec 4.3, plan 4A.3): capacity <c>C = 15 + 2.5 x strength</c> kg, load ratio <c>r = mass / C</c> and the move-speed and stamina factors of
/// the table, all in integer permille. Coin weighs 1.3 g a crown (13 g per 1,000 mites). Above <c>r = 1.5</c> the carrier cannot walk.
/// </summary>
public static class Encumbrance
{
    /// <summary>Capacity in grams: <c>15,000 + 2,500 x strength</c> (strength 50: 140 kg; 44: 125 kg).</summary>
    public static long CapacityG(int strength) => 15_000 + 2_500L * Math.Max(0, strength);

    /// <summary>Mass of coin: <c>mites x 13 / 1000</c> grams (1,000 crowns = 100,000 mites = 1,300 g; T-7).</summary>
    public static long CoinMassG(long mites) => Math.Max(0, mites) * 13 / 1000;

    /// <summary>Load ratio in permille, rounded down.</summary>
    public static int RatioPm(long massG, long capacityG) => capacityG <= 0 ? int.MaxValue : (int)Math.Min(int.MaxValue, Math.Max(0, massG) * 1000 / capacityG);

    /// <summary>
    /// Move speed factor in permille of the load ratio: 1000 up to r = 0.5, then <c>1 - 0.4 x (r - 0.5) / 0.5</c> down to 600 at r = 1.0, then <c>0.6 - 0.5 x (r - 1) / 0.5</c> down to
    /// 100 at r = 1.5, then 0 (cannot walk). r = 0.75 gives 800 (jog 3 m/s becomes 2.4), r = 1.25 gives 350 (T-6).
    /// </summary>
    public static int SpeedPm(int rPm)
    {
        if (rPm <= 500) return 1000;
        if (rPm <= 1000) return 1000 - 400 * (rPm - 500) / 500;
        if (rPm <= 1500) return 600 - 500 * (rPm - 1000) / 500;
        return 0;
    }

    /// <summary>Stamina cost factor in permille: 1000 up to r = 0.5, <c>1 + (r - 0.5)</c> to r = 1.0 (1,500), then <c>1.5 + 2 x (r - 1)</c> (2,500 at r = 1.5).</summary>
    public static int StaminaPm(int rPm)
    {
        if (rPm <= 500) return 1000;
        if (rPm <= 1000) return 1000 + (rPm - 500);
        return 1500 + 2 * (Math.Min(rPm, 1500) - 1000);
    }

    public static bool CanWalk(int rPm) => rPm <= 1500;

    /// <summary>Melee timing penalty in permille: +1% per kg over capacity (plan 4A.3).</summary>
    public static int MeleeTimingPm(long massG, long capacityG) => 1000 + (int)Math.Clamp((massG - capacityG) / 1000 * 10, 0, 10_000);

    /// <summary>The colour band of the load bar: 0 green (r up to 0.5), 1 amber (to 1.0), 2 red.</summary>
    public static int Band(int rPm) => rPm <= 500 ? 0 : rPm <= 1000 ? 1 : 2;
}
