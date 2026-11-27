using Ravensguard.Sim.Core;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Sim.Autopilot;

/// <summary>What the brain does about an interrupt set to <see cref="InterruptPolicy.Ai"/> (the "Ai means" column of autopilot spec 3.2).</summary>
public enum AiResponse : byte
{
    None, AvoidOrFlee, FleeShelterOrFight, ShelterFromStorm, FindShelterOrClothing, RestInShade, EatBuyOrForage, MakeCamp, RestTiredHorse, PushUnloadOrWait, ReplanOnce, PayWageWaitOrHeal, IgnoreOrForage,
    NextDirective, AcceptIfAllowedElseDecline, DeclineAndRecord, Bandage, SwitchToWork, IdleHome, SwitchToEarning,
    /// <summary>Appended (watch-mode 3.2): the watch kinds set to Ai: keep watching under the stance (its flee triggers decide), treat and loot after the fight.</summary>
    KeepWatching, TreatAfterFight,
    /// <summary>Appended (P8-AT-2, ambitions spec 3.1): an offered ambition's choice set to Ai: pick the one the planner scores best (none scoring leaves the offer open).</summary>
    PickByScore,
}

/// <summary>What <see cref="InterruptRouter.Raise"/> did: the record the bus kept, the policy applied, what the AI answers, and whether the event was dropped as a duplicate.</summary>
public readonly record struct RouteResult(InterruptKind Kind, InterruptPolicy Policy, AiResponse Response, bool Duplicate, bool Paused, bool Toast);

/// <summary>
/// The interrupt router (autopilot spec 4.4). It receives interrupt events from the sim (travel's <see cref="InterruptBus"/> and the autopilot's own kinds), looks up the person's policy by kind and applies Pause, Notify or Ai.
/// Raised only for autopilot persons; identical to what manual play sees on the same bus. The same kind within ten real seconds (here: <see cref="DedupeGameSeconds"/> game seconds) raises once; every interrupt writes a log
/// line and, for Attacked, Injured, BigChoice and Arrived, a chronicle entry (through <see cref="Chronicle"/>). Unknown kinds default to Pause.
/// </summary>
public sealed class InterruptRouter(AutopilotLog log)
{
    /// <summary>Ten real seconds are 200 game seconds at 1x: events of one kind inside that window are one event.</summary>
    public const int DedupeGameSeconds = 10 * TimeConst.GameSecondsPerRealSecond;

    /// <summary>The chronicle hook: (person, type id, game second, data). Set by the world; null in a bare router.</summary>
    public Action<EntityId, string, long, IReadOnlyDictionary<string, string>>? Chronicle { get; set; }

    public static bool Chronicled(InterruptKind k) => k is InterruptKind.Attacked or InterruptKind.Injured or InterruptKind.BigChoice or InterruptKind.Arrived;

    /// <summary>Which answer the brain gives to an interrupt set to Ai (spec 3.2).</summary>
    public static AiResponse AiAnswer(InterruptKind k) => k switch
    {
        InterruptKind.Hostile => AiResponse.AvoidOrFlee,
        InterruptKind.Attacked => AiResponse.FleeShelterOrFight,
        InterruptKind.Weather => AiResponse.ShelterFromStorm,
        InterruptKind.Cold => AiResponse.FindShelterOrClothing,
        InterruptKind.Heat => AiResponse.RestInShade,
        InterruptKind.Hunger => AiResponse.EatBuyOrForage,
        InterruptKind.Fatigue => AiResponse.MakeCamp,
        InterruptKind.Dusk => AiResponse.MakeCamp,
        InterruptKind.Horse => AiResponse.RestTiredHorse,
        InterruptKind.CartStuck => AiResponse.PushUnloadOrWait,
        InterruptKind.BlockedPath => AiResponse.ReplanOnce,
        InterruptKind.Companion => AiResponse.PayWageWaitOrHeal,
        InterruptKind.Found => AiResponse.IgnoreOrForage,
        InterruptKind.Arrived => AiResponse.NextDirective,
        InterruptKind.Contract => AiResponse.AcceptIfAllowedElseDecline,
        InterruptKind.Threatened => AiResponse.AvoidOrFlee,
        InterruptKind.Injured => AiResponse.Bandage,
        InterruptKind.LowMoney => AiResponse.SwitchToWork,
        InterruptKind.BigChoice => AiResponse.DeclineAndRecord,
        InterruptKind.DirectiveDone => AiResponse.NextDirective,
        InterruptKind.LimitReached => AiResponse.IdleHome,
        InterruptKind.NeedMoney => AiResponse.SwitchToEarning,
        InterruptKind.Stuck => AiResponse.ReplanOnce,
        InterruptKind.WatchDanger or InterruptKind.WatchLosing or InterruptKind.WatchAmmoOut => AiResponse.KeepWatching,
        InterruptKind.WatchOver => AiResponse.TreatAfterFight,
        // P8-0A: the Phase 8 kinds set to Ai. The bell is answered like an attack (shelter, flee or fight); an ambition's choice is made by the planner's score (P8-AT-2, ambitions spec 3.1); a stuck ambition moves to the next directive; an offered ending is declined (play carries on). P8-EN-1 makes EndingOffered always pause.
        InterruptKind.BellRung => AiResponse.FleeShelterOrFight,
        InterruptKind.AmbitionChoice => AiResponse.PickByScore,
        InterruptKind.AmbitionStuck => AiResponse.NextDirective,
        InterruptKind.EndingOffered => AiResponse.DeclineAndRecord,
        _ => AiResponse.None,
    };

    /// <summary>
    /// Notes an interrupt the sim already raised on the bus (travel's own kinds): the bus applied the policy; the router writes the log line and the chronicle entry and says what the brain answers. No second raise.
    /// </summary>
    public AiResponse Observe(AutopilotState st, in InterruptRecord rec)
    {
        var policy = PolicyOf(st, rec.Kind);
        var resp = policy == InterruptPolicy.Ai ? AiAnswer(rec.Kind) : AiResponse.None;
        st.Today.Interrupts++;
        log.Add(rec.Sec, st.Person, LogCode.Interrupt, $"{rec.Kind}: {rec.Text} [{policy}{(resp != AiResponse.None ? ", " + resp : "")}]", (byte)rec.Kind);
        if (Chronicled(rec.Kind)) Chronicle?.Invoke(st.Person, "autopilot_" + rec.Kind.ToString().ToLowerInvariant(), rec.Sec, new Dictionary<string, string> { ["autopilot"] = "1", ["text"] = rec.Text, ["policy"] = policy.ToString() });
        return resp;
    }

    /// <summary>Policy of a kind for the person: a kind this build does not know is Pause. <c>Ai</c> is only honoured while the person's row is enabled (a paused person is in manual play: Ai reads as Pause).</summary>
    public static InterruptPolicy PolicyOf(AutopilotState st, InterruptKind k)
    {
        if ((int)k >= InterruptBus.KindCount) return InterruptPolicy.Pause;
        var p = st.Policy[(int)k];
        if (k == InterruptKind.BigChoice && p == InterruptPolicy.Notify) return InterruptPolicy.Pause;      // a big choice always pauses unless the AI declines it (hard rule)
        if (p == InterruptPolicy.Ai && (!st.Enabled || st.Paused)) return InterruptPolicy.Pause;
        return p;
    }

    /// <summary>
    /// Raises one interrupt for the person: applies the policy to <paramref name="bus"/> (Pause sets the game pause, Notify queues a toast and the 4x slow-down, Ai only logs), writes the log line and the chronicle entry,
    /// and returns what the AI answers. A duplicate (the same kind within <see cref="DedupeGameSeconds"/>) changes nothing. <c>BigChoice</c> with <c>Ai</c> is always the decline (hard rule), and an <c>Attacked</c> is never quiet.
    /// </summary>
    public RouteResult Raise(AutopilotState st, InterruptBus bus, InterruptKind kind, long sec, long xMm, long yMm, string text = "")
    {
        int ki = (int)kind;
        if ((uint)ki < (uint)st.LastFiredSec.Length)
        {
            if (st.LastFiredSec[ki] != long.MinValue && sec - st.LastFiredSec[ki] < DedupeGameSeconds && sec >= st.LastFiredSec[ki]) return new RouteResult(kind, PolicyOf(st, kind), AiResponse.None, true, false, false);
            st.LastFiredSec[ki] = sec;
        }
        var policy = PolicyOf(st, kind);
        bool quiet = (uint)ki < (uint)st.Quiet.Length && st.Quiet[ki] && kind != InterruptKind.Attacked && policy == InterruptPolicy.Notify;
        // the bus applies the policy of its own table: mirror the person's choice into it, so the HUD and the game clock see one rule
        bus.Autopilot = true;
        if ((uint)ki < (uint)InterruptBus.KindCount) bus.Set(kind, policy, quiet);
        var before = bus.ToastsPending;
        var rec = bus.Raise(kind, sec, xMm, yMm, text);
        bool toast = bus.ToastsPending > before;
        var resp = policy == InterruptPolicy.Ai ? AiAnswer(kind) : AiResponse.None;
        st.Today.Interrupts++;
        log.Add(sec, st.Person, LogCode.Interrupt, $"{kind}: {text} [{policy}{(resp != AiResponse.None ? ", " + resp : "")}]", (byte)kind);
        if (Chronicled(kind)) Chronicle?.Invoke(st.Person, "autopilot_" + kind.ToString().ToLowerInvariant(), sec, new Dictionary<string, string> { ["autopilot"] = "1", ["text"] = text, ["policy"] = policy.ToString() });
        return new RouteResult(kind, policy, resp, false, bus.Paused && policy == InterruptPolicy.Pause, toast);
    }
}
