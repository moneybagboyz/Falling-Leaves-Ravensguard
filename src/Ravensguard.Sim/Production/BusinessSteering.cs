namespace Ravensguard.Sim.Production;

/// <summary>
/// P5-F (businesses spec 4.2 to 4.6): what an owner's or steward's standing orders change in the Phase 2 production steps, for the businesses the ownership world steers (the hero's, and any with
/// shareholders). The production system asks only when <see cref="Steered"/> is true; every other business runs exactly as in Phase 2, so a world without holdings keeps its bytes.
/// </summary>
public interface IBusinessSteering
{
    /// <summary>True when the business is steered (cheap: called per business per step).</summary>
    bool Steered(Business b);
    /// <summary>The hero owns it: no Phase 2 top-ups from a household or a treasury, no Phase 2 closing, no succession.</summary>
    bool Owned(Business b);
    /// <summary>A staff member's wage an hour (the agreed wage of a hire; else <paramref name="classWagePerHour"/>).</summary>
    int WagePerHour(Business b, int person, int classWagePerHour);
    /// <summary>The planner's margin of a batch at the prices the decider sees (a steward's are days old); null for today's book.</summary>
    long? MarginMicro(Business b, Recipe r);
    /// <summary>A recipe the owner ordered for a station kind ("make N a day"), or null for the planner's choice by margin.</summary>
    Recipe? Ordered(Business b, BusinessKind kind, string station);
    /// <summary>Whether the business may post input orders now (the season's budget).</summary>
    bool MayBuyInputs(Business b);
    /// <summary>How a good is sold: the lot's floor (permille of the reference) and the units kept back; null for the Phase 2 default. A business with a stall keeps everything for the stall.</summary>
    (int FloorPm, long Keep)? Sale(Business b, ushort good);
    /// <summary>The resource layer row an extraction business works (its own tiles), or -1 for the Phase 2 pick around the settlement.</summary>
    int TileFor(Business b, ExtractionKind k, int fallback);
    /// <summary>Pays a dividend among the holders (true: paid here; false: the Phase 2 transfer to the owner's household).</summary>
    bool PayDividend(Business b, long dividend);
    /// <summary>A season closed: its net (cash change plus what was paid out), before the season books are reset.</summary>
    void SeasonClosed(Business b, long net, long cash);
    /// <summary>What an extraction or a mine took today (grams of the good), for royalties and exhaustion.</summary>
    void Extracted(Business b, ushort good, long grams, Ravensguard.Sim.Core.GameTime now);
    /// <summary>The most staff the labour market may hire for it (int.MaxValue: the kind's slots).</summary>
    int HireCap(Business b);
}
