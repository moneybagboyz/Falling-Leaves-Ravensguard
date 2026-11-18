namespace Ravensguard.Sim.Production;

/// <summary>Pure labour-market and cash-box rules of production spec 3.3 and 3.5: wage offers, hiring, dividends.</summary>
public static class Labour
{
    /// <summary>Share of cash above the reserve paid out at S5 (decision B4).</summary>
    public const int PayoutPct = 50;

    /// <summary>An unfilled vacancy raises the offer 5% each 15 days (floor), capped at 1.5 x the class wage.</summary>
    public static int NextOffer(int classWagePerDay, int currentOffer) => Math.Min(classWagePerDay * 3 / 2, currentOffer * 105 / 100);

    /// <summary>With three or more applicants queued the offer falls 3% each 15 days (floor), but not below 0.8 x the class wage.</summary>
    public static int NextOfferDown(int classWagePerDay, int currentOffer) => Math.Max(classWagePerDay * 8 / 10, currentOffer * 97 / 100);

    /// <summary>
    /// Hires <paramref name="vacancies"/> of the candidates: highest skill first, ties by lower person id (G-B13). Returns the chosen ids in hiring order.
    /// </summary>
    public static List<int> Hire(int vacancies, IEnumerable<(int Id, int Skill)> candidates)
        => [.. candidates.OrderByDescending(c => c.Skill).ThenBy(c => c.Id).Take(Math.Max(0, vacancies)).Select(c => c.Id)];

    /// <summary>
    /// The seasonal dividend: <c>max(0, cash - 15 x (daily wage bill + daily input spend)) x payout%</c> (G-B8).
    /// </summary>
    public static long Dividend(long cashMites, long wageBillPerDay, long inputSpendPerDay, int payoutPct)
    {
        long reserve = 15 * (wageBillPerDay + inputSpendPerDay);
        return Math.Max(0, cashMites - reserve) * payoutPct / 100;
    }
}
