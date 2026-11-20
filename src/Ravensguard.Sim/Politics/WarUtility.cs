using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Politics;

/// <summary>What the war utility reads about one realm facing another (4.1): the rival's strength is last-known, so <c>KnowPm</c> (permille, from <see cref="Ravensguard.Sim.Contracts.RumourAge"/>) weighs the strength term: a fact older than two seasons counts at half.</summary>
public readonly record struct WarInputs(bool Claim, int CbWeight, long StrengthA, long StrengthB, int TreasurySeasons, Season Season, int HarvestCost, bool Allied, bool TruceHolds, int KnowPm = 1000);

/// <summary>A realm's traits that move how readily it declares (4.1): the cautious need more, the martial less.</summary>
[Flags] public enum LordTrait : byte { None = 0, Greedy = 1, Pious = 2, Martial = 4, Just = 8, Cautious = 16, Cruel = 32, Generous = 64 }

/// <summary>
/// The one war utility (overview 8.3, diplomacy-and-ai-lords 4.1): <c>claim + casus belli + 30 x (strength ratio - 1) (clamped) + 5 x treasury seasons (max 20) + season - harvest cost - 20 if allied - 30 if a truce holds</c>.
/// Live rulers, the continent's realms and history v1 all call this function. Integer arithmetic only.
/// </summary>
public static class WarUtility
{
    /// <summary>The casus belli's weight by kind (a chronicle event's: 10 an insult, 20 unpaid dues, 30 a broken treaty, 40 a seized fief or a raid, 50 excommunication; a claim is its own term).</summary>
    public static int CbWeight(PoliticsSettingsDef s, CbKind k) => k switch
    {
        CbKind.Insult => s.CbInsult, CbKind.UnpaidDues => s.CbUnpaidDues, CbKind.BrokenTreaty => s.CbBrokenTreaty, CbKind.SeizedFief => s.CbSeizedFief, CbKind.Raid => s.CbRaid,
        CbKind.Excommunication => s.CbExcommunication, _ => 0,
    };

    public static int SeasonPts(PoliticsSettingsDef s, Season se) => se switch { Season.Spring => s.SpringPts, Season.Summer => s.SummerPts, Season.Autumn => s.AutumnPts, _ => s.WinterPts };

    /// <summary>The strength term: <c>30 x (ratio - 1)</c>, the ratio taken in permille so no float is needed, clamped to plus or minus 60.</summary>
    public static int RatioTerm(PoliticsSettingsDef s, long strengthA, long strengthB)
    {
        long ratioPm = strengthA * 1000 / Math.Max(1, strengthB);
        return (int)Math.Clamp((ratioPm - 1000) * s.RatioPts / 1000, -s.RatioClamp, s.RatioClamp);
    }

    public static int Score(PoliticsSettingsDef s, in WarInputs i)
    {
        int u = (i.Claim ? s.ClaimPts : 0) + i.CbWeight + RatioTerm(s, i.StrengthA, i.StrengthB) * i.KnowPm / 1000
            + Math.Min(s.TreasurySeasonsMax, Math.Max(0, i.TreasurySeasons) * s.TreasurySeasonPts) + SeasonPts(s, i.Season) - i.HarvestCost;
        if (i.Allied) u -= s.AllyPenalty;
        if (i.TruceHolds) u -= s.TrucePenalty;
        return u;
    }

    /// <summary>The utility at which a ruler declares: 50, 70 for a cautious ruler and 40 for a martial one (a ruler with both is held at 50).</summary>
    public static int Threshold(PoliticsSettingsDef s, LordTrait t)
    {
        bool c = (t & LordTrait.Cautious) != 0, m = (t & LordTrait.Martial) != 0;
        return c && m ? s.DeclareAt : c ? s.CautiousDeclareAt : m ? s.MartialDeclareAt : s.DeclareAt;
    }

    public static bool Declares(PoliticsSettingsDef s, int utility, LordTrait t) => utility >= Threshold(s, t);

    /// <summary>The harvest cost: the levies' lost yield in permille of a year's receipts, divided down (4.1).</summary>
    public static int HarvestCost(PoliticsSettingsDef s, int lostPermille) => lostPermille / Math.Max(1, s.HarvestCostDiv);

    // ---------------------------------------------------------------- peace (4.1)

    /// <summary>Weariness of a side: 10 a season at war, + the dead as a percent of its people times 5, + 10 when its treasury holds under one season.</summary>
    public static int Weariness(PoliticsSettingsDef s, int seasons, int deadPct, int treasurySeasons)
        => seasons * s.WearinessPerSeason + deadPct * s.WearinessDeadPct + (treasurySeasons < 1 ? s.WearinessThinTreasury : 0);

    /// <summary>Whether a side sues for peace: <c>weariness - war score &gt;= 40</c> (the score is the side's own, a loser's counts as 0).</summary>
    public static bool Sues(PoliticsSettingsDef s, int weariness, int ownScore) => weariness - Math.Max(0, ownScore) >= s.SueGap;

    public enum Terms : byte { Truce, Tribute, CedeFief }

    /// <summary>What the winner may ask by his score: a truce, tribute (5 percent of receipts for 2 years) at half the ceding score, a fief ceded at 60 and over.</summary>
    public static Terms TermsFor(PoliticsSettingsDef s, int winnerScore) => winnerScore >= s.CedeScore ? Terms.CedeFief : winnerScore >= s.CedeScore / 2 ? Terms.Tribute : Terms.Truce;
}

/// <summary>Lords' traits (3): two of seven, from the history's notables where one stands for the lord, else rolled by person id with stream 0x670 (pure: a new game and a loaded old save agree).</summary>
public static class LordTraits
{
    private static readonly LordTrait[] All = [LordTrait.Greedy, LordTrait.Pious, LordTrait.Martial, LordTrait.Just, LordTrait.Cautious, LordTrait.Cruel, LordTrait.Generous];

    public static LordTrait Of(ulong seed, ulong person)
    {
        int a = DetRng.Range(seed, 0, person, RngStreams.LordTraits, 0, 0, All.Length);
        int b = DetRng.Range(seed, 0, person, RngStreams.LordTraits, 1, 0, All.Length - 1);
        if (b >= a) b++;
        return All[a] | All[b];
    }

    /// <summary>A ruler of history: a high ambition makes him martial, a low one cautious, with one more rolled; the people's own are rolled outright.</summary>
    public static LordTrait OfRuler(ulong seed, ulong person, int ambition)
    {
        var t = Of(seed, person);
        if (ambition >= 70) t = (t & ~LordTrait.Cautious) | LordTrait.Martial;
        else if (ambition <= 30) t = (t & ~LordTrait.Martial) | LordTrait.Cautious;
        return t;
    }

    public static IEnumerable<string> Names(LordTrait t) { foreach (var x in All) if ((t & x) != 0) yield return x.ToString().ToLowerInvariant(); }
    public static bool Has(LordTrait t, LordTrait x) => (t & x) != 0;
}
