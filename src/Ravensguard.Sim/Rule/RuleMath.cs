using Ravensguard.Content;

namespace Ravensguard.Sim.Rule;

/// <summary>
/// The rule and income arithmetic (rule-and-income 4), pure and integer: every number the golden tests pin lives here so a test calls it with no world. Money is mites; percentages are whole percents unless
/// a name says permille. Nothing here draws a random number.
/// </summary>
public static class RuleMath
{
    // ------------------------------------------------------------------ food rents and labour (4.1)

    /// <summary>Measures a share lease puts in the lord's barn: <c>acres x net measures an acre x share percent</c> (RI-2: 20 acres at 38 and 30% is 228).</summary>
    public static long ShareMeasures(int acres, int netMeasuresPerAcre, int sharePct) => (long)acres * netMeasuresPerAcre * sharePct / 100;

    /// <summary>A rent in kind commuted to coin at the season's book price of a measure.</summary>
    public static long Commuted(long measures, long bookPriceMitesPerMeasure) => measures * bookPriceMitesPerMeasure;

    /// <summary>The days of labour service a tenant household owes in a window of <paramref name="windowDays"/> days: one in <c>every</c>, rounded down, at least one in a window of that length.</summary>
    public static int LabourDaysOwed(int windowDays, int every) => every <= 0 ? 0 : windowDays / every;

    /// <summary>Coin owed for the days of service not worked.</summary>
    public static long LabourMissed(int daysOwed, int daysWorked, int mitesPerDay) => (long)Math.Max(0, daysOwed - daysWorked) * mitesPerDay;

    // ------------------------------------------------------------------ vassal dues (4.2)

    /// <summary>The service a fief owes: one man in <c>perMan</c> households (at least one), for the days set; a manor owes the manor's.</summary>
    public static (int Men, int Days) ServiceOf(RuleSettingsDef s, bool manor, int households) =>
        manor ? (s.ServiceManorMen, s.ServiceManorDays) : (Math.Max(1, households / Math.Max(1, s.ServiceFiefHouseholdsPerMan)), s.ServiceFiefDays);

    /// <summary>Relief an heir owes the overlord: a quarter of the fief's last year's receipts (RI-4: 40,000 gives 10,000).</summary>
    public static long Relief(long lastYearReceipts, int pct) => Math.Max(0, lastYearReceipts) * pct / 100;

    /// <summary>An aid: one season of the vassal's fief's hearth tax (RI-6).</summary>
    public static long Aid(long seasonHearthMites, int seasons) => Math.Max(0, seasonHearthMites) * seasons;

    /// <summary>A marriage fee (or the fine for marrying without consent, in seasons): that many seasons of receipts.</summary>
    public static long MarriageFee(long seasonReceipts, int seasons) => Math.Max(0, seasonReceipts) * seasons;

    /// <summary>The guardian's keep from a ward's receipts (RI-5): a fifth.</summary>
    public static long GuardianKeep(long receipts, int pct) => Math.Max(0, receipts) * pct / 100;

    /// <summary>The day a wardship ends: when the heir reaches the age (<paramref name="birthDay"/> plus the years in game days).</summary>
    public static int WardEndsDay(int birthDay, int ageYears) => birthDay + ageYears * Ravensguard.Sim.Core.TimeConst.DaysPerYear;

    // ------------------------------------------------------------------ councils, subsidies, tallage (4.3)

    /// <summary>One member's vote: yes when <c>opinion of the ruler + cause weight + jitter &gt;= 0</c> (RI-7).</summary>
    public static bool Vote(int opinion, int causeWeight, int jitter) => opinion + causeWeight + jitter >= 0;

    /// <summary>The weight of a cause (4.3): defending the realm, a war with a casus belli, no war, and a second subsidy within the years set.</summary>
    public static int CauseWeight(RuleSettingsDef s, string cause, bool secondWithinYears) =>
        (cause switch { "defence" => s.CauseDefence, "casus_belli" => s.CauseCasusBelli, _ => s.CauseNoWar }) + (secondWithinYears ? s.CauseSecondSubsidy : 0);

    /// <summary>A majority of the members weighted by their households passes (strictly more yes weight than no).</summary>
    public static bool Passed(long yesWeight, long noWeight) => yesWeight > noWeight;

    /// <summary>A share of movables in permille (a tenth is 100: RI-8 takes 40,000 from 400,000).</summary>
    public static long OfMovables(long movables, int permille) => Math.Max(0, movables) * permille / 1000;

    // ------------------------------------------------------------------ customs (4.4)

    /// <summary>The duty on goods of a declared value at a percent rate (RI-9: 20,000 of wool at 10% is 2,000).</summary>
    public static long Duty(long valueMites, int pct) => Math.Max(0, valueMites) * Math.Clamp(pct, 0, 100) / 100;

    /// <summary>The share of a gate's caravans that still come at a rate: above the free rate (8%) each point sends 10% fewer (RI-9: 12% leaves 60%).</summary>
    public static int CaravanPct(RuleSettingsDef s, int ratePct) => ratePct <= s.CustomsFreePct ? 100 : Math.Max(0, 100 - (ratePct - s.CustomsFreePct) * s.CustomsFewerPerPoint);

    // ------------------------------------------------------------------ officials (4.8)

    /// <summary>Percent of what he collects an official keeps: <c>(60 - honesty) / 4</c> below honesty 60 (honesty 20 keeps 10%).</summary>
    public static int SkimPct(RuleSettingsDef s, int honesty) => honesty >= s.SkimBase ? 0 : (s.SkimBase - honesty) / Math.Max(1, s.SkimDiv);

    public static long Skim(RuleSettingsDef s, long collected, int honesty) => Math.Max(0, collected) * SkimPct(s, honesty) / 100;

    /// <summary>Chance in percent that an audit finds a thief: <c>min(90, 30 + auditor's Law - official's Law / 2)</c> (RI-11: 60 and 40 give 70).</summary>
    public static int AuditPct(RuleSettingsDef s, int auditorLaw, int officialLaw) => Math.Clamp(s.AuditBasePct + auditorLaw - officialLaw / 2, 0, s.AuditMaxPct);

    /// <summary>A person's honesty from one draw in 0..(max-min) (stream 0x540): 10..100, a trait's shift added by the caller and clamped.</summary>
    public static byte HonestyOf(RuleSettingsDef s, int draw, int shift = 0) => (byte)Math.Clamp(s.HonestyMin + draw + shift, 1, 100);

    // ------------------------------------------------------------------ loans (4.6)

    /// <summary>Interest a season in permille of the principal: 4% (40), and 2% more (20) for a borrower with arrears or standing below zero.</summary>
    public static int RatePermille(RuleSettingsDef s, bool risky) => (s.LoanRatePctSeason + (risky ? s.LoanRiskExtraPct : 0)) * 10;

    public static long Interest(long principalMites, int ratePmPerSeason) => principalMites * ratePmPerSeason / 1000;

    // ------------------------------------------------------------------ charters (4.9)

    /// <summary>The terms of a petition: the ruler's opinion, the petitioner's standing with the realm over ten, five for each ten crowns of the fee, minus fifty when the land is contested and twenty of another realm.</summary>
    public static (int Score, string Largest) CharterScore(RuleSettingsDef s, int opinion, int standing, long feeMites, bool contested, bool otherRealm)
    {
        int stand = standing / 10, fee = (int)(feeMites / 1000) * s.CharterFeeTenCrownsBonus;
        int con = contested ? -s.CharterContestedPenalty : 0, other = otherRealm ? -s.CharterOtherRealmPenalty : 0;
        int score = opinion + stand + fee + con + other;
        // the largest negative term names the refusal; with none negative, the smallest positive term is what fell short
        string largest = "ru_term_none"; int worst = 0;
        if (opinion < worst) { worst = opinion; largest = "ru_term_opinion"; }
        if (stand < worst) { worst = stand; largest = "ru_term_standing"; }
        if (con < worst) { worst = con; largest = "ru_term_contested"; }
        if (other < worst) { worst = other; largest = "ru_term_other_realm"; }
        if (worst == 0 && score < s.CharterGrantThreshold) largest = fee <= stand && fee <= opinion ? "ru_term_fee" : opinion <= stand ? "ru_term_opinion" : "ru_term_standing";
        return (score, largest);
    }

    /// <summary>The fee of one extension of a founding charter's deadline: half the original fee, once.</summary>
    public static long ExtensionFee(long feeMites, int pct) => feeMites * pct / 100;

    // ------------------------------------------------------------------ eviction (4.7)

    /// <summary>The licence a squatter can pay to stay: so much an acre.</summary>
    public static long Licence(RuleSettingsDef s, int acres) => (long)acres * s.EvictLicenceMitesPerAcre;
}
