namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>
/// The named ratchets of the economy and growth audit (P8-EA-1, decision D11; findings in docs/reviews/phase-8-economy-audit.md). A gate the honest fix could not meet ships as the measured figure,
/// asserted by the century soak (<c>Phase6SoakTests.Ea1Century</c>, when a run reaches 70 game years). A ratchet may only tighten: a later part that measures better replaces the figure in the same commit.
/// No existing bound is loosened by these (the soak's own 90 to 125 percent band and BL-6's bounds stand as they were).
/// </summary>
public static class Ea1Ratchets
{
    /// <summary>EA8_F1: the head count's furthest reach above the start in any year of the century, percent. Target 10 (plan 4A.2, the user's decision of 28 Nov). Measured at P8-EA-1: +16.3 (no bot) and +16.7 (bot) at year 100 before; the 5 percent housing headroom tried in the audit gave +13.3 and +15.9 but more famine and was not shipped. Set at 17.</summary>
    public const int PopulationUpPctMax = 17;

    /// <summary>EA8_F1: famine entries in the worst decade of the century. Target: the first decades' 4 to 25. Measured at P8-EA-1: 109 (no bot) and 129 (bot), from the seventh decade on (11 to 25 before it). Set at 130.</summary>
    public const int FamineWorstDecadeMax = 130;

    /// <summary>EA8_F2: mines worked out and closed over the century. Target at least one (ore bodies are finite). Measured at P8-EA-1: 0 before and after (the region's ore demand is tens of tonnes a year; the smallest body found was 310 t). Set at 0.</summary>
    public const int MinesWorkedOutMin = 0;

    /// <summary>EA8_F2: new ore bodies found over the century. Target: some. Measured at P8-EA-1: 0 before (no NPC prospected), 6 and 3 after (no bot, bot). Set at 3.</summary>
    public const int VeinsFoundMin = 3;

    /// <summary>EA8_F4: the woodland's standing timber lost over the century, percent of the start. Target 0 (woods renew). Before P8-EA-1: 14.6 (no bot) and 13.7 (bot), by assarting (cleared rows never regrow); 13.3 and 13.5 after. Set at 15.</summary>
    public const int WoodlandLossPctMax = 15;

    /// <summary>EA8_F5: the coin's yearly growth over the century, basis points. Target 100 to 200 (plan 4A.1). Measured at P8-EA-1: 83 (no bot) and 74 (bot) before, 75 and 77 after. Set at 74.</summary>
    public const int CoinGrowthBpMin = 74;
}
