using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Production.Industry;

/// <summary>
/// What Phase 2's production and labour market ask of the industry runtime (P7-IN1): a business that is a stage of a works is run by <see cref="IndustryWorld"/>, so the production system skips it (no top-ups, no planner, no dividends, no closing for losses)
/// and the labour market hires it to the works' crew target instead of the kind's slots. Null on a world without industries: every other business runs as in Phase 2.
/// </summary>
public interface IStageHost
{
    /// <summary>The posts of a stage business (occupation, count, wage class) for the labour market, by the works' crew target; false for a business that is not a stage.</summary>
    bool SlotsFor(Business b, out (Occupation Occupation, int Count, WageClass Wage)[] slots);
    /// <summary>Whether the business's cash box is counted in the region's coin (a works' shared purse is counted once, by its first stage).</summary>
    bool CountsCash(Business b);
}
