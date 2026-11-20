using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Crafting;

/// <summary>
/// Smithing quality (crafting-full 4.3, P5-E). A recipe with <c>process: "smithing"</c> has the steps heat (the forge brought to working heat: skipped when the forge is already hot, so a cold
/// start costs its 30 minutes), forge (hammering at the anvil), quench (the critical step: Phase 3's failure roll) and, for blades, springs and edged tools, temper. The material sets the
/// ceiling (the edge is the material's maximum edge x Qw, items 4.1), and the process adds to the score: +5 when the smith's effective skill is at least the difficulty + 20 at the quench (he
/// reads the colours), -5 and a brittle piece when the temper is left out, -15 for an edged piece made with no quench at all. A brittle blade whose blow is stopped by a rigid layer breaks
/// with 30 pm a blow (stream 0x532, key the fight tick and the item).
/// </summary>
public static class Smithing
{
    public const int QuenchMargin = 20, ReadColoursBonus = 5, SkipTemperBonus = -5, NoQuenchBonus = -15;
    /// <summary>Chance a brittle blade breaks when a rigid layer stops its blow, permille.</summary>
    public const int BrittleBreakPm = 30;
    /// <summary>How long a forge stays at working heat after the last heat-hour, seconds.</summary>
    public const long HotAfterSec = 3600;

    /// <summary>The process term at the quench: +5 when the smith reads the colours (effective skill at least the difficulty + 20), else 0.</summary>
    public static int QuenchBonus(int effectiveSkill, int difficulty) => effectiveSkill >= difficulty + QuenchMargin ? ReadColoursBonus : 0;

    /// <summary>The process term fixed at the start: -5 for a temper left out, -15 for an edged smithing result whose recipe has no quench step (a soft edge).</summary>
    public static int StartBonus(CraftRecipe r, bool skipTemper)
    {
        if (!r.IsSmithing) return 0;
        int b = 0;
        if (skipTemper && r.HasStep(SmithStep.Temper)) b += SkipTemperBonus;
        if (r.Edged && !r.HasStep(SmithStep.Quench)) b += NoQuenchBonus;
        return b;
    }

    /// <summary>True when the job left its temper out (the result is <c>Brittle</c>).</summary>
    public static bool TemperSkipped(CraftRecipe r, CraftJob j)
    {
        for (int i = 0; i < r.Steps.Length; i++) if (r.Steps[i].Smith == SmithStep.Temper && j.Skipped(i)) return true;
        return false;
    }

    /// <summary>
    /// Whether a brittle blade breaks on this blow (crafting-full 4.3): a blow stopped by a rigid layer breaks it with <see cref="BrittleBreakPm"/> permille, drawn from stream 0x532 keyed on the
    /// fight tick and the item id (the one place Phase 4's FightTick rule applies). A blade that is not brittle never breaks this way.
    /// </summary>
    public static bool BrittleBreaks(ulong seed, long fightTick, EntityId item, bool brittle, bool stoppedByRigid)
        => brittle && stoppedByRigid && DetRng.Range(seed, fightTick, item.Value, RngStreams.BrittleBreak, 0, 0, 1000) < BrittleBreakPm;
}
