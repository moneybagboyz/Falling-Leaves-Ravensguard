using Ravensguard.Content;
using Ravensguard.Sim.Economy.Markets;

namespace Ravensguard.Sim.Production;

/// <summary>
/// The steward's rules (businesses spec 4.4), honest in Phase 5 (overview 10a: skimming is Phase 6, stream 0x540 reserved and never drawn); skill decides how well he runs a property, not luck:
/// he plans with prices <c>max(0, 10 - Trading / 10)</c> days old and keeps <c>reserve x (1500 - 5 x Leadership) / 1000</c>. Pure functions; the ownership world applies them in the business's
/// daily slice.
/// </summary>
public static class StewardBrain
{
    /// <summary>How many days old the prices are a steward plans with (Trading 40: 6 days; 100: 0).</summary>
    public static int LagDays(int trading, OwnershipRulesDef r) => Math.Max(0, r.StewardLagMaxDays - Math.Clamp(trading, 0, 100) / Math.Max(1, r.StewardLagPerTrading));

    /// <summary>How many properties a steward runs: <c>3 + Leadership / 20</c>, at most 6.</summary>
    public static int Properties(int leadership, OwnershipRulesDef r) => Math.Min(r.StewardPropertiesMax, r.StewardPropertiesBase + Math.Clamp(leadership, 0, 100) / Math.Max(1, r.StewardPropertiesPerLeadership));

    /// <summary>The cash he keeps back, mites: 15 days of the business's daily costs, times <c>(1500 - 5 x Leadership) / 1000</c> (a careless steward hoards or runs dry).</summary>
    public static long Reserve(long dailyCostMites, int leadership, OwnershipRulesDef r)
        => (long)r.ReserveDays * dailyCostMites * Math.Max(0, r.ReserveBasePm - r.ReservePerLeadershipPm * Math.Clamp(leadership, 0, 100)) / 1000;

    public static bool Qualifies(int trading, int leadership, OwnershipRulesDef r) => trading >= r.StewardMinTrading && leadership >= r.StewardMinLeadership;

    /// <summary>
    /// What he sends to the owner (spec 4.4): everything above both the reserve and the owner's <c>keep_mites</c>; nothing when the box holds less.
    /// </summary>
    public static long ToSend(long cash, long reserve, long keepMites) => Math.Max(0, cash - Math.Max(reserve, keepMites));

    /// <summary>
    /// A recipe's margin of a full batch at prices <paramref name="mid"/> (micro-mites a unit, by good, as the steward last saw them): outputs at mid less the toll, inputs at the ask, less wages
    /// (the Phase 2 planner's formula, production spec 3.2, with old prices).
    /// </summary>
    public static long MarginMicro(Recipe r, Func<ushort, long> mid, int spreadHalfPm, int tollPm)
    {
        Int128 rev = 0, cost = 0; int keep = 1000 - tollPm;
        foreach (var (g, q) in r.Outputs) rev += (Int128)q * mid(g) * keep / 1000;
        foreach (var (g, q) in r.Inputs) cost += (Int128)q * (mid(g) * (1000 + spreadHalfPm) / 1000);
        Int128 m = rev - cost - (Int128)r.WorkerHours * RecipeBook.WagePerHour(r.Wage) * PriceCurve.Micro;
        return (long)Int128.Clamp(m, long.MinValue, long.MaxValue);
    }
}
