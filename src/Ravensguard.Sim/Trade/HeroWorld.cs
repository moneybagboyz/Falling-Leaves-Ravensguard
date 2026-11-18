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
        st.Party ??= HeroParty.Create(st.Economy!, Content, st.Ids, st.Hero!);
        var rt = Runtime;
        _ctx = new HeroContext(st, st.Party, Content)
        {
            Jobs = () => Ravensguard.Sim.Live.RuntimeJobs.EnvOf(rt) is { } env ? (env, rt.State.Jobs ??= new JobTable()) : null,
        };
        return _ctx;
    }

    private void OnHour(long h) => Desk.AfterHour(Context(), h);

    /// <summary>Queues a command and asks the worker to apply what is due. Main thread.</summary>
    public HeroCommand Issue(HeroCommand c)
    {
        c = Desk.Issue(c);
        _ = Kick();
        return c;
    }

    /// <summary>A barrier job: apply what is due and publish a fresh view.</summary>
    public Task<int> Kick() => Runtime.AtBarrier(() => { int n = Desk.ApplyDue(Context(), Runtime.HourDone); if (n == 0) Desk.Publish(Context(), Runtime.HourDone); return n; });
}
