using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.People.Vital;

/// <summary>Where a person's daily death chance came from, in ppm: the cause is drawn in proportion to these.</summary>
public readonly record struct DeathHazard(int Age, int Other, int Hunger, int Disease, int Cold)
{
    public int TotalPpm => Age + Other + Hunger + Disease + Cold;

    /// <summary>The cause for a draw <paramref name="u"/> in [0, TotalPpm).</summary>
    public DeathCause Pick(int u)
    {
        if (u < Age) return DeathCause.Age; u -= Age;
        if (u < Other) return DeathCause.Other; u -= Other;
        if (u < Hunger) return DeathCause.Hunger; u -= Hunger;
        if (u < Disease) return DeathCause.Disease; u -= Disease;
        return DeathCause.Cold;
    }
}

/// <summary>Pure vital-rate functions (population spec 3): integer maths, no state, so the golden tests pin them exactly.</summary>
public static class VitalMath
{
    /// <summary>Daily base death chance in ppm for an age: the yearly percent over 60 (age 0: 3,333; 1-4: 500; 5-14: 133; 15-49: 200; 50-64: 500; 65+: 1,666).</summary>
    public static int BasePpm(VitalRates r, int ageYears)
    {
        foreach (var (max, pct) in r.DeathPctPerYearX100) if (ageYears <= max) return pct * 100 / TimeConst.DaysPerYear;
        return r.DeathPctPerYearX100[^1].PctX100 * 100 / TimeConst.DaysPerYear;
    }

    public static int NutritionMult(VitalRates r, int nutritionCd)
    {
        foreach (var (min, m) in r.NutritionDeathMult) if (nutritionCd >= min) return m;
        return r.NutritionDeathMult[^1].MultPermille;
    }

    public static int CrowdingMult(VitalRates r, int crowdingPct)
    {
        foreach (var (max, m) in r.CrowdingDeathMult) if (crowdingPct <= max) return m;
        return r.CrowdingDeathMult[^1].MultPermille;
    }

    public static int Crowding(int members, int beds) => beds <= 0 ? 200 : members * 100 / beds;

    /// <summary>
    /// Daily death hazard (population spec 3.1): <c>base x nutrition x crowding x disease x cold x outbreak</c> (capped at 6,000 permille) plus the starvation
    /// ppm when the reserve is empty (doubled under 5 and from 60). The result is split by cause: each factor is credited the share it added on top of the
    /// ones before it (nutrition to hunger, crowding to other, town and outbreak disease to disease, cold to cold); the base is age (50+) or other.
    /// </summary>
    public static DeathHazard Hazard(VitalRates r, int ageYears, int nutritionCd, int crowdingPct, bool town, bool cold, int outbreakMultPermille)
    {
        int b = BasePpm(r, ageYears);
        long nut = NutritionMult(r, nutritionCd), crowd = CrowdingMult(r, crowdingPct);
        long disease = town ? r.TownDiseaseMultPermille : 1000, coldM = cold ? r.ColdDeathMultPermille : 1000, outb = Math.Max(1000, outbreakMultPermille);
        long m1 = nut, m2 = m1 * crowd / 1000, m3 = m2 * disease / 1000, m4 = m3 * coldM / 1000, m5 = m4 * outb / 1000;
        long cap = r.MaxMultPermille;
        long mTotal = Math.Min(m5, cap);
        // excess of each factor over the product before it, scaled down together when the cap bites
        long e1 = m1 - 1000, e2 = m2 - m1, e3 = m3 - m2, e4 = m4 - m3, e5 = m5 - m4, eSum = m5 - 1000;
        long allowed = mTotal - 1000;
        long Share(long e) => eSum <= 0 || e == 0 ? 0 : b * (eSum > allowed ? e * allowed / eSum : e) / 1000;
        int hunger = (int)Share(e1), other = (int)Share(e2), dis = (int)(Share(e3) + Share(e5)), coldPpm = (int)Share(e4);
        int starve = nutritionCd <= 0 ? r.StarvationPpmPerDay * (ageYears < 5 || ageYears >= 60 ? 2 : 1) : 0;
        return ageYears >= 50 ? new DeathHazard(b, other, hunger + starve, dis, coldPpm) : new DeathHazard(0, b + other, hunger + starve, dis, coldPpm);
    }

    /// <summary>Total daily death chance in ppm: <c>base x mult / 1000 + starvation</c>.</summary>
    public static int DeathPpm(VitalRates r, int ageYears, int nutritionCd, int crowdingPct, bool town, bool cold, int outbreakMultPermille)
        => Hazard(r, ageYears, nutritionCd, crowdingPct, town, cold, outbreakMultPermille).TotalPpm;

    public static int FertilityFoodMult(VitalRates r, int nutritionCd)
    {
        foreach (var (min, m) in r.FertilityFoodMult) if (nutritionCd >= min) return m;
        return r.FertilityFoodMult[^1].MultPermille;
    }

    /// <summary>Daily conception chance in ppm for a married woman 18-44 (population spec 3.2): 30 percent a year over 60 days = 5,000, scaled by food and crowding.</summary>
    public static int ConceptionPpm(VitalRates r, int nutritionCd, int crowdingPct)
    {
        long p = r.FertilityPctPerYear * 10_000L / TimeConst.DaysPerYear;
        p = p * FertilityFoodMult(r, nutritionCd) / 1000;
        if (crowdingPct > r.FertilityCrowdingOverPct) p = p * r.FertilityCrowdingMultPermille / 1000;
        return (int)p;
    }

    /// <summary>True with probability <paramref name="ppm"/> per million, drawn from the counter RNG (an integer compare on 32 bits: ppm resolution, not Q16.16).</summary>
    public static bool Roll(ulong seed, long tick, ulong entity, uint stream, uint draw, int ppm)
    {
        if (ppm <= 0) return false;
        if (ppm >= 1_000_000) return true;
        return DetRng.NextUInt(seed, tick, entity, stream, draw) < (ulong)ppm * 4_294_967_296UL / 1_000_000UL;
    }

    /// <summary>Yearly probability of death implied by a daily ppm over a 60-day year, in percent x100 (G-P3): <c>1 - (1 - p)^60</c> computed on a 1e12 integer scale.</summary>
    public static long EffectiveYearlyPctX100(int dailyPpm)
    {
        System.Numerics.BigInteger one = 1_000_000_000_000, q = one - (System.Numerics.BigInteger)dailyPpm * 1_000_000, acc = one;
        for (int i = 0; i < TimeConst.DaysPerYear; i++) acc = acc * q / one;
        return (long)((one - acc) * 10_000 / one);
    }
}
