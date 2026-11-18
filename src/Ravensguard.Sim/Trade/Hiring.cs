using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Trade;

/// <summary>What a hire offer includes (spec 3.3 <c>Terms</c>): food and lodging lower the asking wage by a quarter each; danger raises it.</summary>
[Flags] public enum HireTerms : byte { None = 0, Food = 1, Lodging = 2, Danger = 4 }

/// <summary>How dangerous the work is (spec 4.6): labour 1.0, guard or escort 1.5, fight-risk 2.0.</summary>
public enum HireDanger : byte { Labour, Guard, Fight }

/// <summary>What the hiring rules know of the person asked (spec 4.6). Trust and familiarity are per mille / points the hero has with them.</summary>
public readonly record struct HireCandidate(EntityId Person, int Age, int MaxSkill, int TrustPm, bool SurviveTier, bool HeadWithDependants, bool Alive = true, bool Bonded = false);

public enum HireAnswer : byte { Accept, Refuse, TooYoung, Unavailable }

/// <summary>
/// Hiring anyone (trade-inventory-character spec 4.6): the reservation wage <c>R = W_u x (1000 + 12 x max_skill) / 1000 x danger x (1000 - trust_discount) / 1000</c> with food and lodging
/// taking 25% each, and the acceptance score <c>offer x 1000 / R + trust/10 + need - tie + noise</c> (noise +/-100 from stream 0x321 keyed by day and person). Integer, floor.
/// </summary>
public static class Hiring
{
    public static int DangerPm(HireDanger d) => d switch { HireDanger.Guard => 1500, HireDanger.Fight => 2000, _ => 1000 };

    public static long ReservationWage(TradeSettingsDef t, long wageUnskilledPerDay, int maxSkill, int trustPm, HireDanger danger = HireDanger.Labour, HireTerms terms = HireTerms.None)
    {
        long r = wageUnskilledPerDay * (1000 + t.HireSkillPmPerLevel * Math.Clamp(maxSkill, 0, 100)) / 1000;
        r = r * DangerPm(danger) / 1000;
        int trustDiscount = Math.Clamp(trustPm / 10, 0, 150);
        r = r * (1000 - trustDiscount) / 1000;
        if ((terms & HireTerms.Food) != 0) r = r * (100 - t.HireFoodPct) / 100;
        if ((terms & HireTerms.Lodging) != 0) r = r * (100 - t.HireBedPct) / 100;
        return Math.Max(1, r);
    }

    /// <summary>The mood noise in -100..+100 (stream 0x321, tick = day, key person).</summary>
    public static int Noise(ulong seed, int day, EntityId person, int amp = 100) => amp <= 0 ? 0 : DetRng.Range(seed, day, person.Value, RngStreams.HireMood, 0, -amp, amp + 1);

    /// <summary>The acceptance score before noise (spec 4.6): <c>offer x 1000 / R + trust/10 + need - tie</c>, floor.</summary>
    public static long Score(TradeSettingsDef t, long offer, long reservation, in HireCandidate c)
    {
        long s = offer * 1000 / Math.Max(1, reservation) + c.TrustPm / 10;
        if (c.SurviveTier) s += t.HireNeedBonus;
        if (c.HeadWithDependants) s -= t.HireTiePenalty;
        return s;
    }

    public static (HireAnswer Answer, long Score, long Reservation) Ask(TradeSettingsDef t, ulong seed, int day, long wageU, in HireCandidate c, long offerPerDay, HireDanger danger = HireDanger.Labour, HireTerms terms = HireTerms.None, bool noise = true)
    {
        if (!c.Alive || c.Bonded) return (HireAnswer.Unavailable, 0, 0);
        if (c.Age < 14) return (HireAnswer.TooYoung, 0, 0);
        long r = ReservationWage(t, wageU, c.MaxSkill, c.TrustPm, danger, terms);
        long s = Score(t, offerPerDay, r, c) + (noise ? Noise(seed, day, c.Person, t.HireNoise) : 0);
        return (s >= 1000 ? HireAnswer.Accept : HireAnswer.Refuse, s, r);
    }

    /// <summary>The lowest daily offer that is accepted without noise (what the screen suggests).</summary>
    public static long FairOffer(TradeSettingsDef t, long reservation, in HireCandidate c)
    {
        long need = 1000 - c.TrustPm / 10 - (c.SurviveTier ? t.HireNeedBonus : 0) + (c.HeadWithDependants ? t.HireTiePenalty : 0);
        return Math.Max(1, (need * reservation + 999) / 1000);
    }
}

public enum RecruitAnswer : byte { Accept, NotFamiliar, NoTrust, NoReason, SquadFull, CannotPay, Unavailable }

/// <summary>Recruiting a permanent companion (spec 4.6): familiarity 20, trust 100 and a personal reason; a signing bonus of five days' wage; loyalty starts at <c>400 + trust/4</c>; squad cap <c>2 + leadership/10</c> (2..12).</summary>
public static class Recruiting
{
    public static int SquadCap(TradeSettingsDef t, int leadership) => Math.Clamp(2 + leadership / 10, 2, t.SquadCap);
    public static int LoyaltyStart(TradeSettingsDef t, int trustPm) => Math.Clamp(t.LoyaltyStart + trustPm / 4, 0, 1000);
    public static long SigningBonus(TradeSettingsDef t, long wagePerDay) => wagePerDay * t.SigningBonusDays;
    public static long Severance(TradeSettingsDef t, long wagePerDay) => wagePerDay * t.SeveranceDays;

    public static RecruitAnswer Check(TradeSettingsDef t, int familiarity, int trustPm, bool hasReason, int squadSize, int leadership, long heroMites, long wagePerDay, bool available = true)
    {
        if (!available) return RecruitAnswer.Unavailable;
        if (familiarity < t.RecruitFamiliarityMin) return RecruitAnswer.NotFamiliar;
        if (trustPm < t.RecruitTrustMin) return RecruitAnswer.NoTrust;
        if (!hasReason) return RecruitAnswer.NoReason;
        if (squadSize >= SquadCap(t, leadership)) return RecruitAnswer.SquadFull;
        if (heroMites < SigningBonus(t, wagePerDay)) return RecruitAnswer.CannotPay;
        return RecruitAnswer.Accept;
    }
}

/// <summary>
/// Barter with someone who is not a shopkeeper (spec 4.1): accepted when what the person receives is worth at least what they give, each side valued the same way with
/// <c>want_pm</c>: +20% for what they need, -20% for what they cannot use. Values in mites.
/// </summary>
public static class Barter
{
    public static long Weighted(long valueMites, int wantSign, int wantPm = 200) => valueMites * (1000 + Math.Sign(wantSign) * wantPm) / 1000;
    public static bool Accepts(long receivedByNpc, long givenByNpc) => receivedByNpc >= givenByNpc;
}
