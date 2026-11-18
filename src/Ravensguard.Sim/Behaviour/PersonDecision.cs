using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Behaviour;

/// <summary>What the brain was fed and what it said, for one person at one window (the explanation and the tests read it).</summary>
public readonly record struct DecisionResult(Choice Choice, NeedInputs Needs, ChoiceContext Context);

/// <summary>
/// The one place where a person's situation becomes the scorer's inputs (live-behaviour spec 4.2; autopilot spec 4.1): the live people read it, and so does the hero or a party member on autopilot, which is what makes
/// "one AI for everyone" literally one code path. Everything is a function of the person's anchors, the 20-second window's first second and what the caller knows of the world; <paramref name="policy"/> is null for an NPC.
/// </summary>
public static class PersonDecision
{
    /// <summary>The window of 20 game seconds a decision belongs to (never the tick, so the answer is the same at 1x and 8x).</summary>
    public static long WindowOf(long sec) => sec / 20;

    /// <summary>
    /// Whether the odd-job board is a way out for this person now: jobs are posted at their home settlement, nothing is scheduled, it is light, and they are a labourer or a peasant (or have no work).
    /// </summary>
    public static bool BoardOpen(in PersonAnchors a, in ChoiceContext ctx, bool boardHasJobsHere)
        => boardHasJobsHere && ctx.Scheduled == ActionKind.Idle && !ctx.Dark && !a.Child && (!a.HasWork || a.Occupation == Occupation.Labourer || a.Class == SocialClass.Peasant);

    public static DecisionResult Decide(UtilityBrain brain, in PersonAnchors a, long at, ActionKind current, long intentSince, bool boardHasJobsHere, int coldPm, int wetPm, int heatPm, bool hasBody, bool indoors, ActionPolicy? policy = null, bool forceBoard = false, int threatPm = 0)
    {
        NeedModel.Read(a, at, out var needs, out var ctx);
        ctx.Current = current; ctx.MinutesInCurrent = intentSince == long.MinValue ? 999 : (int)((at - intentSince) / 60);
        ctx.HasBoard = BoardOpen(a, ctx, boardHasJobsHere) || (forceBoard && boardHasJobsHere && !ctx.Dark);
        needs.WageRatioPm = ctx.HasBoard ? 2000 : 0;
        needs.ColdPm = coldPm; needs.WetPm = wetPm; needs.HeatPm = heatPm; needs.ThreatPm = threatPm;
        ctx.CanShelter = hasBody && !indoors && (coldPm > 0 || wetPm > 200 || heatPm > 0);
        ctx.CanFlee = threatPm > 0;
        ctx.Policy = policy;
        return new DecisionResult(brain.Choose(needs, ctx), needs, ctx);
    }
}
