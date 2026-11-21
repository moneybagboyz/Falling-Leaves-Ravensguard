using Ravensguard.Content;

namespace Ravensguard.Sim.Production.Industry;

/// <summary>One person at a stage for the day: his skill in the stage's trade, his share of a full worker (an apprentice under 16 gives half) and whether he is one of the hands that do the main work (the others carry for them).</summary>
public readonly record struct StageWorker(int Skill, int WeightPm, bool Primary);

/// <summary>The stage's day before inputs and room (industries 4.1): the tier it works at, whether it is short-handed or under-crewed, and the most it can do in grams of the main good.</summary>
public readonly record struct StagePlan(int RecognisedTier, int WorkingTier, int Have, int Need, bool UnderCrewed, long PotentialG, long CrewG, int EffPm, int SpanPm, int PowerPm)
{
    /// <summary>"L2, short-handed: 5 of 6": the recognised tier is higher than the one the crew reaches.</summary>
    public bool ShortHanded => !UnderCrewed && WorkingTier < RecognisedTier;
}

/// <summary>
/// The stage day's pure rules (P7-IN1, industries 4.1, 4.3): <c>out = min(capacity_tier, sum over crew of rate_tier x speed(skill) x eff x span x power) x slider / 100</c>, the tier a crew reaches, the carriers a mine's hewers need, a link's hauling. Integer arithmetic only;
/// <see cref="IndustryWorld"/> reads the crew and the stores and applies them.
/// </summary>
public static class StageMath
{
    /// <summary>A worker's speed at a skill in permille, 1,000 at skill 50: the extraction curve <c>800 + 4 x skill</c> (1.18x between skill 20 and 60) or the recipe curve of Phase 2's batches <c>1,000,000 / (1,300 - 6 x skill)</c> (5.1a).</summary>
    public static int SpeedPm(bool craftCurve, int skill)
    {
        skill = Math.Clamp(skill, 0, 100);
        return craftCurve ? 1_000_000 / (1300 - 6 * skill) : 800 + 4 * skill;
    }

    /// <summary>The highest tier up to <paramref name="recognised"/> whose crew minimum the crew reaches (0: it reaches none, which only a first tier that needs hands can say). A stage whose first tier takes no hands always reaches it.</summary>
    public static int WorkingTier(StageKind k, int recognised, int have)
    {
        for (int t = Math.Min(recognised, k.TierCount); t >= 1; t--) if (have >= k.Tier(t).CrewMin) return t;
        return 0;
    }

    /// <summary>
    /// The day's plan for a crew (4.1). The sum is each primary hand's rate x speed x weight; a tier with carriers (a mine) lets the hewers work only as many as the carriers serve (<c>hands x 1,000,000 / support_pm</c> thousandths), none limiting when there are no carriers at all (Phase 2's
    /// "hauling not limiting" rule); past the tier's maximum crew the extra hands count half (crowding, economies-of-scale 5.1); a crew over its overseers' span works slower (<paramref name="spanPm"/>); <paramref name="powerPm"/> is the machine's share. The slider multiplies the result.
    /// </summary>
    public static StagePlan Plan(StageKind k, int recognised, ReadOnlySpan<StageWorker> crew, int spanPm, int powerPm, int sliderPct)
    {
        recognised = Math.Clamp(recognised, 1, k.TierCount);
        int have = crew.Length, wt = WorkingTier(k, recognised, have);
        if (wt == 0) return new StagePlan(recognised, 0, have, k.Tier(1).CrewMin, true, 0, 0, 1000, spanPm, powerPm);
        var t = k.Tier(wt);
        long sum = 0; int primary = 0, hands = 0;
        foreach (var w in crew)
        {
            if (!w.Primary) { hands++; continue; }
            primary++;
            sum += t.RateG * SpeedPm(k.CraftCurve, w.Skill) / 1000 * w.WeightPm / 1000;
        }
        if (t.SupportPm > 0 && hands > 0 && primary > 0)
        {
            long allowedMilli = (long)hands * 1_000_000 / t.SupportPm;
            if (allowedMilli < primary * 1000L) sum = sum * allowedMilli / (primary * 1000L);
        }
        // the carriers and feeders are the stage's own hands only where the tier has them; a stage with no carriers' role counts every hand as primary (the crew is the sum)
        int n = have;
        int eff = n > t.CrewMax && n > 0 ? (int)((long)Crews.EffectivePm(n, t.CrewMax) * 1000 / (n * 1000L)) : 1000;
        long crewG = sum * eff / 1000 * spanPm / 1000 * powerPm / 1000;
        long cap = t.CapacityG > 0 ? t.CapacityG * powerPm / 1000 : long.MaxValue;
        long pot = Math.Min(crewG, cap) * sliderPct / 100;
        return new StagePlan(recognised, wt, have, k.Tier(recognised).CrewMin, false, pot, crewG, eff, spanPm, powerPm);
    }

    /// <summary>The grams a lent hour is worth at a handling stage (industries 4.1): the first tier's rate at skill 50, a ten-hour day. <paramref name="milliHours"/> are thousandths of a worker-hour.</summary>
    public static long LentG(StageKind k, long milliHours, int workdayHours) => milliHours * k.Tier(1).RateG / (workdayHours * 1000L);

    /// <summary>The thousandths of a worker-hour that grams of a handling stage's work take at its first tier.</summary>
    public static long LentMilliHoursFor(StageKind k, long grams, int workdayHours) => k.Tier(1).RateG <= 0 ? long.MaxValue : (grams * workdayHours * 1000L + k.Tier(1).RateG - 1) / k.Tier(1).RateG;

    /// <summary>
    /// A haul stage's rate scaled to its distance (5.1, hauling): the rates are over the kind's reference distance, and a works d metres from its wood moves <c>ref / d</c> of them (floating, <c>AnyDistance</c>, excepted). Never below a tenth.
    /// </summary>
    public static int HaulScalePm(StageKind k, StageTier t, int distanceM)
    {
        if (k.Role != StageRole.Haul || t.AnyDistance || k.RefDistanceM <= 0 || distanceM <= 0) return 1000;
        return (int)Math.Clamp((long)k.RefDistanceM * 1000 / distanceM, 100, 2000);
    }

    /// <summary>Trips a cart makes in a workday (4.3): <c>floor(10 h / (2 x d / 450 m an hour + load_h))</c>, with the loading a thousandth-hour count (1 h, 0.5 h for packed goods or with a crane). Zero beyond reach.</summary>
    public static long Trips(IndustrySettingsDef s, int distanceM, bool fastLoad)
    {
        long load = fastLoad ? s.PackedLoadMilliHours : s.LoadMilliHours;
        long denom = 2L * distanceM * 1000 + (long)s.CartMetresPerHour * load;
        return denom <= 0 ? 0 : (long)s.WorkdayHours * s.CartMetresPerHour * 1000 / denom;
    }

    /// <summary>
    /// What a link carries in a day, in grams (4.3): within <see cref="IndustrySettingsDef.PassWithinM"/> the crews' own hands pass the goods (no limit but the stages' own handling); beyond it carts, <c>trips x 500 kg</c> each (an ox wagon 1,000),
    /// so a link with no cart beyond reach carries nothing.
    /// </summary>
    public static long LinkCapacityG(IndustrySettingsDef s, WorkLink l)
    {
        if (l.DistanceM <= s.PassWithinM) return long.MaxValue;
        long trips = Trips(s, l.DistanceM, l.Packed || l.Crane);
        return trips * ((long)l.Carts * s.CartKg + (long)l.Wagons * s.WagonKg) * 1000;
    }
}
