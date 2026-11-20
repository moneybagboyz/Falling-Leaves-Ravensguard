using Ravensguard.Content;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Trade;

/// <summary>
/// The hero's link to the running world (trade-inventory-character spec 4.7): owns the <see cref="HeroDesk"/>, hangs it on the runtime's hour barrier and runs a barrier job each time a
/// command is issued, so a command is applied as soon as the world has finished the hour it was asked in and never before. The records are only touched on the tick worker; the main thread
/// reads <see cref="View"/>. A world without a party yet (a new game, or a save from before the item-row inventory) gets one at the first barrier: the hero record's kit becomes item rows.
/// </summary>
public sealed class HeroWorld
{
    public WorldRuntime Runtime { get; }
    public HeroDesk Desk { get; }
    public ContentDatabase Content { get; }
    private HeroContext? _ctx;

    private HeroWorld(WorldRuntime rt, ContentDatabase content)
    {
        Runtime = rt; Content = content; Desk = new HeroDesk(rt.State.Party?.LastSeq ?? 0);
        // the commands a save left waiting are taken up with their own numbers and hours, and a save reads the desk's queue from here on (review finding 6)
        Desk.RestorePending(rt.State.HeroPending);
        rt.State.HeroPending = [];
        rt.State.HeroPendingSource = Desk.PendingCommands;
    }

    /// <summary>Binds the hero of a running world; null when the world has no hero or no economy (the dev hamlet).</summary>
    public static HeroWorld? Attach(WorldRuntime rt, ContentDatabase content)
    {
        if (rt.State.Hero == null || rt.State.Economy == null) return null;
        var hw = new HeroWorld(rt, content);
        rt.AfterHour += hw.OnHour;
        _ = hw.Kick();
        return hw;
    }

    /// <summary>The hero's state as the last barrier left it (null until the first barrier ran).</summary>
    public HeroView? View => Desk.View;

    /// <summary>The context on the worker: makes the party on first use (migration "hero_inventory_to_items").</summary>
    public HeroContext Context()
    {
        if (_ctx != null) return _ctx;
        var st = Runtime.State;
        st.Party ??= HeroParty.Create(st.Economy!, Content, st.Ids, st.Hero!, st.Clock.Now.Seconds);
        var rt = Runtime;
        _ctx = new HeroContext(st, st.Party, Content)
        {
            Jobs = () => Ravensguard.Sim.Live.RuntimeJobs.EnvOf(rt) is { } env ? (env, rt.State.Jobs ??= new JobTable()) : null,
        };
        return _ctx;
    }

    private void OnHour(long h) => Desk.AfterHour(Context(), h);

    /// <summary>Raised on the main thread with every command issued, stamped (the session recorder writes it to the replay trace).</summary>
    public event Action<HeroCommand>? Issued;

    /// <summary>Queues a command and asks the worker to apply what is due. Main thread.</summary>
    public HeroCommand Issue(HeroCommand c)
    {
        c = Desk.Issue(c);
        Issued?.Invoke(c);
        _ = Kick();
        return c;
    }

    /// <summary>A barrier job: apply what is due and publish a fresh view.</summary>
    public Task<int> Kick() => Runtime.AtBarrier(() => { int n = Desk.ApplyDue(Context(), Runtime.HourDone); if (n == 0) Desk.Publish(Context(), Runtime.HourDone); return n; });

    // ------------------------------------------------------------------ the treatment screen (P6-B1, medicine 4.1)

    /// <summary>
    /// Queues a treatment command (<see cref="HeroCommandKind.Treat"/>), recorded like every hero command, and applies it at once at a barrier (a verb takes sim-seconds, so it does not wait for the end
    /// of the game hour), then performs the steps whose work has ended by <paramref name="nowSec"/> (read on the main thread). Main thread.
    /// </summary>
    public HeroCommand IssueTreat(HeroCommand c, long nowSec)
    {
        c = Desk.Issue(c);
        Issued?.Invoke(c);
        _ = Runtime.AtBarrier(() =>
        {
            var ctx = Context();
            Desk.ApplyNow(ctx, HeroCommandKind.Treat, Runtime.HourDone, publish: false);
            Ravensguard.Sim.Medicine.HeroTreatDesk.Advance(ctx, nowSec);
            Desk.Publish(ctx, Runtime.HourDone);
            return 0;
        });
        return c;
    }

    /// <summary>Performs the queue's steps whose work has ended by <paramref name="nowSec"/>; returns how many were done. Main thread (the game calls it each frame while a queue waits).</summary>
    public Task<int> TreatTick(long nowSec) => Runtime.AtBarrier(() =>
    {
        var ctx = Context();
        int n = Ravensguard.Sim.Medicine.HeroTreatDesk.Advance(ctx, nowSec);
        if (n > 0) Desk.Publish(ctx, Runtime.HourDone);
        return n;
    });

    /// <summary>The treatment screen's view of a patient (a physician's quote when <paramref name="physician"/> is given), built at a barrier; null when the patient has no wounds on the books.</summary>
    public Task<Ravensguard.Sim.Medicine.TreatView?> TreatView(ulong patient, long nowSec, ulong? physician = null) =>
        Runtime.AtBarrier(() => Ravensguard.Sim.Medicine.TreatViews.ForHero(Context(), patient, nowSec, physician));
}
