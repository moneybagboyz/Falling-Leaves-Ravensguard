using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Farming;

/// <summary>The pure integer maths of farming spec 3.1, 3.3, 3.4 and 3.5; no state, so every golden test calls these directly.</summary>
public static class YieldMath
{
    /// <summary>Fertility used in the yield is capped at 91 (70 x 1.3).</summary>
    public const int FertilityCap = 91;

    public static long Gross0G(long baseG, int fertilityUsed) => baseG * Math.Min(Math.Clamp(fertilityUsed, 0, 100), FertilityCap) / 70;

    /// <summary>Grams of grain fixed at ripening: each factor is applied in permille and floored in turn (spec 3.1).</summary>
    public static long GrossG(long baseG, int fertilityUsed, int wxPermille, int varPermille, int damagePermille, int latePermille = 1000)
    {
        long g = Gross0G(baseG, fertilityUsed);
        g = g * wxPermille / 1000;
        g = g * varPermille / 1000;
        g = g * (1000 - Math.Clamp(damagePermille, 0, 1000)) / 1000;
        g = g * latePermille / 1000;
        return Math.Max(0, g);
    }

    public static long NetG(long grossG, long seedG) => Math.Max(grossG - seedG, 0);

    /// <summary>Rain factor in permille from the ratio (permille) of window rain to normal rain: 400 + 600 x min(ratio, 1), and a waterlogging penalty above 1.4 (floor 600).</summary>
    public static int RainFactorPermille(long ratioPermille)
    {
        if (ratioPermille > 1400) return (int)Math.Max(600, 1000 - 400 * (ratioPermille - 1400) / 600);
        return (int)(400 + 600 * Math.Min(ratioPermille, 1000) / 1000);
    }

    /// <summary>Weeding factor: each unweeded ppm costs yield, up to <paramref name="maxLossPermille"/> (250) when nothing was weeded.</summary>
    public static int WeedFactorPermille(int weededPpm, int maxLossPermille = 250) => 1000 - maxLossPermille * (1_000_000 - Math.Clamp(weededPpm, 0, 1_000_000)) / 1_000_000;

    /// <summary>The factors multiplied the way spec 3.1 says (each permille product floored).</summary>
    public static int Combine(params int[] permille) { long p = 1000; foreach (int f in permille) p = p * f / 1000; return (int)p; }

    /// <summary>Wet harvest: 920 when the cell had 6 mm or more that day, 850 from 12 mm; rain in tenths of mm.</summary>
    public static int WetHarvestPermille(int rainTenthsMm, int at6 = 920, int at12 = 850) => rainTenthsMm >= 120 ? at12 : rainTenthsMm >= 60 ? at6 : 1000;

    /// <summary>One day of overdue loss on the remaining yield (floor).</summary>
    public static long Overdue(long yieldG, int lossPermillePerDay = 40) => yieldG * (1000 - lossPermillePerDay) / 1000;

    /// <summary>Harvest split (spec 3.4). Tithe on net of seed unless glebe; share rent only on a tenant's share lease; the holder gets the rest of the yield (the seed was paid out at sowing).</summary>
    public static HarvestSplit Split(long yieldG, long seedG, OwnerKind owner, LeaseKind lease, int shareBp, int tithePermille = 100)
    {
        long net = Math.Max(yieldG - seedG, 0);
        long tithe = owner == OwnerKind.Glebe ? 0 : net * tithePermille / 1000;
        long rent = owner == OwnerKind.Tenant && lease == LeaseKind.Share ? net * shareBp / 10_000 : 0;
        return new HarvestSplit(yieldG, seedG, net, tithe, rent, yieldG - tithe - rent);
    }

    /// <summary>Fertility after a harvest or a fallow year, clamped 0..100.</summary>
    public static byte AddFertility(int fertility, int delta) => (byte)Math.Clamp(fertility + delta, 0, 100);

    /// <summary>Variance in permille: 1000 +- spread, centred (960..1040 for 40), from the field's own stream.</summary>
    public static int VariancePermille(ulong seed, long hourKey, EntityId field, int spread = 40)
        => spread <= 0 ? 1000 : 1000 - spread + DetRng.Range(seed, hourKey, field.Value, RngStreams.YieldVariance, 0, 0, 2 * spread + 1);

    /// <summary>True when a parcel is hit by hail on a storm day (stream 0x210, key parcel id, draw = day of year).</summary>
    public static bool HailHit(ulong seed, long hourKey, EntityId parcel, int doy, int chancePermille = 5)
        => DetRng.Range(seed, hourKey, parcel.Value, RngStreams.FarmEvents, (uint)doy, 0, 1000) < chancePermille;

    /// <summary>Blight roll for a culture-year (stream 0x210, draw 99, key culture&lt;&lt;16|year); x3 in a wet year.</summary>
    public static bool Blight(ulong seed, long hourKey, int culture, int year, bool wetYear, int permille = 30, int wetMult = 3)
        => DetRng.Range(seed, hourKey, ((ulong)(uint)culture << 16) | (uint)year, RngStreams.FarmEvents, 99, 0, 1000) < permille * (wetYear ? wetMult : 1);

    /// <summary>Hours of one task credit: ppm gained by <paramref name="hours"/> worker-hours on a parcel (floor, remainder carried in <paramref name="carry"/>; spec 3.2).</summary>
    public static int CreditPpm(int ratePerDay, int hours, int acres, int workdayHours, ref int carry)
    {
        if (acres <= 0 || hours <= 0) return 0;
        long den = (long)workdayHours * acres;
        long num = (long)hours * ratePerDay * 1_000_000 + carry;
        long ppm = num / den; carry = (int)(num % den);
        return (int)Math.Min(ppm, int.MaxValue);
    }

    /// <summary>Fields done at parcel progress p ppm on n fields: the first floor(p x n / 1e6) in index order.</summary>
    public static int FieldsDone(int progressPpm, int n) => (int)Math.Min(n, (long)Math.Clamp(progressPpm, 0, 1_000_000) * n / 1_000_000);

    /// <summary>Fertility trajectory helper: net change over a wheat, spring-crop, fallow rotation.</summary>
    public static int FallowBonus(bool grazed, int grazedBonus = 15, int ungrazedBonus = 5) => grazed ? grazedBonus : ungrazedBonus;

    /// <summary>Winter fodder cull fraction in permille: <c>clamp(1 - have/needed, 0, max)</c> (resources spec 3.3, G-R5).</summary>
    public static int CullPermille(long haveG, long neededG, int maxPermille = 600)
        => neededG <= 0 ? 0 : (int)Math.Clamp((neededG - haveG) * 1000 / neededG, 0, maxPermille);
}
