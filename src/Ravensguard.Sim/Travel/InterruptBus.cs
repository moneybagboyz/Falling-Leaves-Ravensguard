using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Travel;

/// <summary>
/// The canonical interrupt kinds (docs/specs/phase3/autopilot-v1.md 3.2): 23, append only, in this order. Travel raises the rows marked "travel"; autopilot raises the rest. Nothing else keeps a list of kinds.
/// </summary>
public enum InterruptKind : byte
{
    Hostile, Attacked, Weather, Cold, Heat, Hunger, Fatigue, Dusk, Horse, CartStuck, BlockedPath, Companion, Found, Arrived, Contract,
    Threatened, Injured, LowMoney, BigChoice, DirectiveDone, LimitReached, NeedMoney, Stuck,
    /// <summary>Appended (watch-mode 3.2, P4-F): the watched hero is in danger (blood loss 30%, pain 85% of the knock-out, morale under 30, an arterial bleed); the hero's side is losing (odds under 25%
    /// for 3 s); the watched archer is out of ammunition; the watched fight is over.</summary>
    WatchDanger, WatchLosing, WatchAmmoOut, WatchOver,
    /// <summary>Appended (Phase 8 overview 8): the bell rung, an ambition to choose, an ambition stuck, an ending offered.</summary>
    BellRung, AmbitionChoice, AmbitionStuck, EndingOffered,
}

/// <summary>What happens when a kind fires: pause the game, tell the player and go on, or let the autopilot brain handle it (offered only to autopilot persons).</summary>
public enum InterruptPolicy : byte { Pause, Notify, Ai }

/// <summary>One raised interrupt as the log keeps it.</summary>
public readonly record struct InterruptRecord(long Sec, InterruptKind Kind, InterruptPolicy Policy, long XMm, long YMm, string Text, bool Quiet);

/// <summary>
/// The one interrupt channel (travel spec 4.2; autopilot 3.2). <see cref="Raise"/> applies the policy: <c>Pause</c> sets <see cref="PausedBy"/> (the game pauses and shows the panel), <c>Notify</c> logs a toast
/// (unless the kind is quiet) and asks for a slow-down of the clock for <see cref="NotifySlowSeconds"/> real seconds, <c>Ai</c> only logs. <see cref="Edge"/> raises a kind once when its condition turns on.
/// A hostile drops the clock to 1x in the same call (V-6). Unknown kinds default to Pause.
/// </summary>
public sealed class InterruptBus
{
    public const int KindCount = 31;
    public const int LogCap = 500;
    public const int NotifySlowSeconds = 5;
    private readonly InterruptPolicy[] _policy = new InterruptPolicy[KindCount];
    private readonly bool[] _quiet = new bool[KindCount];
    private readonly List<InterruptRecord> _log = [];
    private uint _active;

    public InterruptBus() { for (int i = 0; i < KindCount; i++) _policy[i] = DefaultPolicy((InterruptKind)i); }

    /// <summary>True when the owner is an autopilot person: only then may a kind be set to <see cref="InterruptPolicy.Ai"/>.</summary>
    public bool Autopilot { get; set; }
    public IReadOnlyList<InterruptRecord> Log => _log;
    /// <summary>The interrupt that paused the game, until <see cref="Resume"/>.</summary>
    public InterruptRecord? PausedBy { get; private set; }
    public bool Paused => PausedBy != null;
    /// <summary>The clock is held at 1x while this is true (a hostile in view); cleared by <see cref="ClearSpeedCap"/> once the gate allows 8x again.</summary>
    public bool SpeedCapped { get; private set; }
    public string SpeedCapReason { get; private set; } = "";
    /// <summary>Game second until which a Notify holds the clock at 4x so the player can see it (the 5 real seconds as game seconds at that speed).</summary>
    public long NotifySlowUntilSec { get; private set; }
    public int ToastsPending { get; private set; }
    public InterruptRecord? LastToast { get; private set; }

    public static InterruptPolicy DefaultPolicy(InterruptKind k) => k switch
    {
        InterruptKind.Hostile or InterruptKind.Attacked or InterruptKind.Fatigue or InterruptKind.Horse or InterruptKind.CartStuck or InterruptKind.BlockedPath
            or InterruptKind.Arrived or InterruptKind.Contract or InterruptKind.Threatened or InterruptKind.Injured or InterruptKind.BigChoice or InterruptKind.LimitReached or InterruptKind.Stuck => InterruptPolicy.Pause,
        InterruptKind.Weather or InterruptKind.Cold or InterruptKind.Heat or InterruptKind.Hunger or InterruptKind.Dusk or InterruptKind.Companion or InterruptKind.Found
            or InterruptKind.LowMoney or InterruptKind.DirectiveDone or InterruptKind.NeedMoney => InterruptPolicy.Notify,
        InterruptKind.WatchDanger or InterruptKind.WatchOver or InterruptKind.AmbitionChoice or InterruptKind.EndingOffered => InterruptPolicy.Pause,                     // watch-mode 3.2 defaults
        InterruptKind.WatchLosing or InterruptKind.WatchAmmoOut or InterruptKind.BellRung or InterruptKind.AmbitionStuck => InterruptPolicy.Notify,
        _ => InterruptPolicy.Pause,
    };

    public InterruptPolicy PolicyOf(InterruptKind k) => (int)k < KindCount ? _policy[(int)k] : InterruptPolicy.Pause;
    public bool IsQuiet(InterruptKind k) => (int)k < KindCount && _quiet[(int)k];

    /// <summary>Sets a kind's policy. Refused (false) for <c>Ai</c> in manual play and for <c>quiet</c> on Attacked or on a Pause kind.</summary>
    public bool Set(InterruptKind k, InterruptPolicy p, bool quiet = false)
    {
        if ((int)k >= KindCount) return false;
        if (p == InterruptPolicy.Ai && !Autopilot) return false;
        if (quiet && (k == InterruptKind.Attacked || p != InterruptPolicy.Notify)) return false;
        _policy[(int)k] = p; _quiet[(int)k] = quiet;
        return true;
    }

    /// <summary>The Dusk setting of the HUD: continue (Notify, quiet), stop (Pause) or make camp (Ai-like: raised as Pause with the camp text).</summary>
    public void SetDusk(string mode) { if (mode == "stop") Set(InterruptKind.Dusk, InterruptPolicy.Pause); else if (mode == "continue") Set(InterruptKind.Dusk, InterruptPolicy.Notify, quiet: true); else Set(InterruptKind.Dusk, InterruptPolicy.Notify); }

    /// <summary>Fires one interrupt now and applies its policy. Returns the record.</summary>
    public InterruptRecord Raise(InterruptKind kind, long sec, long xMm, long yMm, string text = "")
    {
        var pol = PolicyOf(kind); bool quiet = IsQuiet(kind);
        var rec = new InterruptRecord(sec, kind, pol, xMm, yMm, text, quiet);
        _log.Add(rec); if (_log.Count > LogCap) _log.RemoveAt(0);
        if (kind == InterruptKind.Hostile) { SpeedCapped = true; SpeedCapReason = text; }
        switch (pol)
        {
            case InterruptPolicy.Pause: PausedBy = rec; break;
            case InterruptPolicy.Notify:
                if (!quiet) { ToastsPending++; LastToast = rec; }
                NotifySlowUntilSec = Math.Max(NotifySlowUntilSec, sec + NotifySlowSeconds * TimeConst.GameSecondsPerRealSecond * 4);
                break;
        }
        return rec;
    }

    /// <summary>Raises <paramref name="kind"/> once when <paramref name="on"/> turns true; clears the memory when it turns false. True when it fired now.</summary>
    public bool Edge(InterruptKind kind, bool on, long sec, long xMm, long yMm, string text = "")
    {
        uint bit = 1u << (int)kind;
        bool was = (_active & bit) != 0;
        if (on && !was) { _active |= bit; Raise(kind, sec, xMm, yMm, text); return true; }
        if (!on && was) _active &= ~bit;
        return false;
    }

    public bool IsActive(InterruptKind kind) => (_active & (1u << (int)kind)) != 0;

    /// <summary>The player took the alert: the game may run again.</summary>
    public void Resume() => PausedBy = null;
    public void ClearSpeedCap() { SpeedCapped = false; SpeedCapReason = ""; }
    public InterruptRecord? TakeToast() { if (ToastsPending == 0) return null; ToastsPending--; return LastToast; }

    /// <summary>The fastest speed the clock may run at now: 1x while a hostile holds it, 4x during a Notify slow-down, otherwise as asked.</summary>
    public SimSpeed AllowedSpeed(SimSpeed wanted, long nowSec)
    {
        if (Paused) return SimSpeed.Paused;
        if (SpeedCapped && wanted > SimSpeed.X1) return SimSpeed.X1;
        if (nowSec < NotifySlowUntilSec && wanted > SimSpeed.X4) return SimSpeed.X4;
        return wanted;
    }

    // ---- save
    public (byte[] Policy, bool[] Quiet, uint Active, InterruptRecord[] Log, InterruptRecord? Paused, bool Capped, string CapReason, long SlowUntil) Capture()
        => ((byte[])_policy.Select(p => (byte)p).ToArray(), (bool[])_quiet.Clone(), _active, [.. _log], PausedBy, SpeedCapped, SpeedCapReason, NotifySlowUntilSec);

    public void Restore(byte[] policy, bool[] quiet, uint active, IEnumerable<InterruptRecord> log, InterruptRecord? paused, bool capped, string capReason, long slowUntil)
    {
        for (int i = 0; i < KindCount && i < policy.Length; i++) { _policy[i] = (InterruptPolicy)policy[i]; _quiet[i] = i < quiet.Length && quiet[i]; }
        _active = active; _log.Clear(); _log.AddRange(log); PausedBy = paused; SpeedCapped = capped; SpeedCapReason = capReason; NotifySlowUntilSec = slowUntil;
    }
}
