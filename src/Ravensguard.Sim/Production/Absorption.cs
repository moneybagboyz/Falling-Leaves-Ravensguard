namespace Ravensguard.Sim.Production;

/// <summary>What the markets in reach take of a good (economies-of-scale 5.4), as the Business panel shows it.</summary>
public readonly record struct AbsorbView(ushort Good, long CapPerDay, long Held, long BidMicro, long VarCostMicro, bool Stops);

/// <summary>
/// Market absorption, pure rules (P7-ES2, economies-of-scale 5.4): <c>absorb_per_day = sum over the books in reach of (demand a day x (1,000 - the competitors' share) / 1,000) + the gate desk's room a day</c>, and a batch stops when the book's
/// bid at the stock it would reach is under the unit variable cost times 1.05. Integer arithmetic only. Selling beyond the cap stays allowed; the per-unit repricing makes it dear.
/// </summary>
public static class Absorption
{
    /// <summary>The daily cap from the books in reach: each book's demand a day less the share the other producers of the kind there already serve, plus the desk's room (ES-14: 40 kg a day of demand and 10 kg of room is 50 kg).</summary>
    public static long CapPerDay(IEnumerable<(long DemandPerDay, int CompetitorPm)> books, long deskRoomPerDay)
    {
        long sum = 0;
        foreach (var (demand, competitors) in books) sum += demand * (1000 - Math.Clamp(competitors, 0, 1000)) / 1000;
        return sum + Math.Max(0, deskRoomPerDay);
    }

    /// <summary>The share the other producers of a kind take in one book: <c>1,000 x others / (others + 1)</c>, none when the business is alone there.</summary>
    public static int CompetitorsPm(int others) => others <= 0 ? 0 : 1000 * others / (others + 1);

    /// <summary>Whether the bid is under the unit variable cost times the margin (1,050 permille): the batch is not worth starting.</summary>
    public static bool BidBelowCost(long bidMicro, long unitVarCostMicro, int marginPm) => bidMicro < unitVarCostMicro * marginPm / 1000;
}
