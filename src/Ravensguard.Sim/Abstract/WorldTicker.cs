using System.Diagnostics;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Abstract;

public sealed class TickerOptions
{
    /// <summary>Test hook: the worker sleeps this long inside every hour step, to prove the result does not depend on thread timing (G-A7).</summary>
    public int WorkerDelayMs { get; init; }
    /// <summary>Record per-phase and per-system timings (a Stopwatch read per phase and system; negligible).</summary>
    public bool RecordTimings { get; init; } = true;
}

/// <summary>
/// Advances the abstract simulation one game hour at a time (abstract spec 3.1, 3.2). Per hour <c>h</c>:
/// <list type="number">
///   <item><b>Barrier start</b>: the queued live-tier commands are frozen, sorted by (Frame, Seq) and executed against the records (no worker is running).</item>
///   <item><b>Hour step</b>: H0 environment, H1 caravans, H2 work, H3 market, H4 daily slice, H5 daily globals (hour 0), H6 seasonal (first day of a season), H7 outbox;
///   each phase runs its systems by ascending <see cref="IAbstractSystem.Order"/>, and every system iterates its table in ascending index.</item>
///   <item><b>Barrier end</b>: chronicle entries and events of the outbox are applied in draft order, the <see cref="WorldSnapshot"/> is swapped in, <c>HourDone</c> moves on.</item>
/// </list>
/// Synchronous use (tests, soaks): <see cref="AdvanceHours"/>, <see cref="AdvanceTo"/>. Game use: <see cref="StartWorker"/> then <see cref="Pump"/> every frame
/// from the main thread; the hour step then runs on the worker while the main thread keeps rendering. Both run the very same steps, so the result is identical
/// (the tick never skips an hour, however the calls are batched).
/// </summary>
public sealed class WorldTicker : IDisposable
{
    private readonly AbstractSystemRegistry _registry;
    private readonly TickerOptions _opt;
    private readonly RegionOutbox _outbox;
    private readonly TickContext _ctx;
    private readonly Dictionary<long, List<EconCommand>> _replay = [];
    private readonly List<ExecutedCommand> _log = [];
    private readonly SortedDictionary<string, long> _counters = new(StringComparer.Ordinal);

    // worker state
    private Thread? _worker;
    private readonly SemaphoreSlim _go = new(0);
    private readonly ManualResetEventSlim _stepDone = new(true);
    private volatile int _state;                     // 0 idle, 1 stepping, 2 stepped (barrier end pending)
    private long _stepHour;
    private Exception? _fault;
    private volatile bool _stop;
    private WorldSnapshot _snapshot;
    private int _households;

    public WorldTicker(SimWorld world, AbstractSystemRegistry? registry = null, TickerOptions? options = null)
    {
        World = world; _opt = options ?? new TickerOptions();
        _registry = registry ?? AbstractSystemRegistry.CreateDefault();
        _registry.Freeze();
        _outbox = new RegionOutbox(world.Ids);
        _ctx = new TickContext(world, _outbox);
        Handlers = new CommandPump();
        _ = world.Children;                                  // build the child index now, not inside the first hour that needs it
        _snapshot = MakeSnapshot(world.State.HourDone, countHouseholds: true);
    }

    public SimWorld World { get; }
    public AbstractSystemRegistry Registry => _registry;
    public CommandQueue Commands { get; } = new();
    public CommandPump Handlers { get; }
    /// <summary>Events published by systems (<see cref="RegionOutbox.Publish{T}"/>) are delivered here at the barrier.</summary>
    public SimEvents Events { get; } = new();
    public TickerStats Stats { get; } = new();
    /// <summary>Called for every executed command (barrier or between hours), on the thread that executed it.</summary>
    public Action<ExecutedCommand>? OnCommand { get; set; }

    /// <summary>The last completed hour (the world state is exactly the state after this hour's barrier).</summary>
    public long HourDone => World.State.HourDone;
    public GameTime Now => GameTime.FromGameHours(HourDone);
    public WorldSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public IReadOnlyList<ExecutedCommand> CommandLog => _log;
    /// <summary>Sums of the counters systems reported through the outbox (births, deaths, marriages ...), by name.</summary>
    public IReadOnlyDictionary<string, long> Counters => _counters;
    public bool IsRunning => _state == 1;
    public Exception? Fault => _fault;

    // ------------------------------------------------------------------ synchronous driving

    /// <summary>Runs <paramref name="hours"/> hour steps (barrier, step, barrier) on the calling thread.</summary>
    public void AdvanceHours(int hours)
    {
        RequireNoWorker();
        for (int i = 0; i < hours; i++) RunHour(HourDone + 1);
    }

    public void AdvanceDays(int days) => AdvanceHours(days * 24);

    /// <summary>Steps every hour boundary up to and including the hour of <paramref name="t"/>.</summary>
    public void AdvanceTo(GameTime t)
    {
        RequireNoWorker();
        long target = t.TotalHours;
        while (HourDone < target) RunHour(HourDone + 1);
    }

    private void RequireNoWorker() { if (_worker != null) throw new InvalidOperationException("A worker thread is running; drive the ticker with Pump."); }

    private void RunHour(long h)
    {
        long t0 = Stopwatch.GetTimestamp();
        BarrierStart(h);
        StepHour(h);
        BarrierEnd(h, t0);
    }

    // ------------------------------------------------------------------ worker driving

    /// <summary>Starts the worker thread; from now on drive with <see cref="Pump"/>.</summary>
    public void StartWorker()
    {
        if (_worker != null) return;
        _stop = false;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "abstract-tick" };
        _worker.Start();
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _go.Wait();
            if (_stop) return;
            try
            {
                if (_opt.WorkerDelayMs > 0) Thread.Sleep(_opt.WorkerDelayMs);
                StepHour(_stepHour);
            }
            catch (Exception e) { _fault = e; }
            _state = 2;
            _stepDone.Set();
        }
    }

    /// <summary>
    /// The main thread's per-frame call (abstract spec 3.1): applies the finished hour's barrier, then, if the clock is past <see cref="HourDone"/> and the
    /// worker is idle, freezes the command batch, runs it and starts the next hour step on the worker. Between hours it applies waiting commands at once.
    /// Returns true while the worker is still stepping or hours are still owed.
    /// </summary>
    public bool Pump(GameTime now)
    {
        if (_worker == null) throw new InvalidOperationException("Call StartWorker first, or use AdvanceTo.");
        if (_fault != null) { var f = _fault; _fault = null; throw new InvalidOperationException("The abstract worker faulted: " + f.Message, f); }
        if (_state == 1) return true;
        if (_state == 2) { long t0 = Stopwatch.GetTimestamp(); BarrierEnd(_stepHour, t0); _state = 0; }
        if (HourDone < now.TotalHours)
        {
            long h = HourDone + 1;
            BarrierStart(h);
            _stepHour = h; _stepStart = Stopwatch.GetTimestamp();
            _stepDone.Reset(); _state = 1; _go.Release();
            return true;
        }
        if (Commands.Pending > 0) RunBetweenHours();
        return false;
    }

    private long _stepStart;

    /// <summary>Blocks until every hour up to <paramref name="now"/> is stepped and its barrier applied (tests, loading screens).</summary>
    public void WaitIdle(GameTime now)
    {
        while (Pump(now)) { if (_state == 1) _stepDone.Wait(); }
    }

    /// <summary>Applies commands that arrived while the worker was idle (they take the next hour's tick key and run immediately).</summary>
    private void RunBetweenHours()
    {
        long h = HourDone + 1;
        ExecuteBatch(h);
        FlushOutbox();
    }

    // ------------------------------------------------------------------ the barrier and the step

    private void BarrierStart(long h)
    {
        long t = Stopwatch.GetTimestamp();
        ExecuteBatch(h);
        if (_opt.RecordTimings) Stats.BarrierStart.Add(Stopwatch.GetTimestamp() - t);
    }

    private void ExecuteBatch(long h)
    {
        var batch = Commands.DrainAll();
        if (_replay.Remove(h, out var scripted)) { batch.AddRange(scripted); batch = batch.OrderBy(c => c.Frame).ThenBy(c => c.Seq).ToList(); }
        if (batch.Count == 0) return;
        long t = Stopwatch.GetTimestamp();
        var cc = new CommandContext(World, _outbox, h);
        foreach (var c in batch)
        {
            var r = Handlers.Execute(c, cc);
            var ex = new ExecutedCommand(h, c, r);
            _log.Add(ex);
            OnCommand?.Invoke(ex);
        }
        if (_opt.RecordTimings) Stats.Commands.Add(Stopwatch.GetTimestamp() - t);
    }

    /// <summary>Re-injects commands from an earlier run's <see cref="CommandLog"/> at the same tick keys (abstract spec 5, replay).</summary>
    public void ScheduleReplay(IEnumerable<ExecutedCommand> log)
    {
        foreach (var e in log)
        {
            if (!_replay.TryGetValue(e.TickKey, out var l)) _replay[e.TickKey] = l = [];
            l.Add(e.Command);
        }
    }

    private static readonly TickPhase[] PhaseOrder =
    [
        TickPhase.H0Environment, TickPhase.H1Caravans, TickPhase.H2Work, TickPhase.H3Market, TickPhase.H4DailySlice, TickPhase.H5DailyGlobals, TickPhase.H6Seasonal, TickPhase.H7Outbox,
    ];

    private void StepHour(long h)
    {
        var c = _ctx;
        c.Hour = h;
        bool hour0 = c.HourOfDay == 0, seasonDay = c.IsSeasonStartDay;
        foreach (var phase in PhaseOrder)
        {
            if (phase == TickPhase.H5DailyGlobals && !hour0) continue;
            if (phase == TickPhase.H6Seasonal && !seasonDay) continue;
            var systems = _registry.For(phase);
            if (systems.Count == 0) continue;
            c.Phase = phase;
            long p0 = Stopwatch.GetTimestamp();
            foreach (var s in systems)
            {
                long s0 = _opt.RecordTimings ? Stopwatch.GetTimestamp() : 0;
                s.Step(c);
                if (_opt.RecordTimings) Stats.SystemCell(s.Name).Add(Stopwatch.GetTimestamp() - s0);
            }
            if (_opt.RecordTimings) Stats.Phase(phase).Add(Stopwatch.GetTimestamp() - p0);
        }
    }

    private void BarrierEnd(long h, long hourStartTimestamp)
    {
        long t = Stopwatch.GetTimestamp();
        FlushOutbox();
        World.State.HourDone = h;
        bool hour0 = h % 24 == 0;
        Interlocked.Exchange(ref _snapshot, MakeSnapshot(h, hour0));
        if (!_opt.RecordTimings) return;
        long now = Stopwatch.GetTimestamp();
        Stats.BarrierEnd.Add(now - t); Stats.RecordBarrier(now - t);
        long whole = _worker == null ? now - hourStartTimestamp : now - _stepStart + (now - t);
        Stats.RecordHour(h % 24, GameTime.FromGameHours(h).DayOfYear % TimeConst.DaysPerSeason == 1, whole);
    }

    private void FlushOutbox()
    {
        _outbox.ApplyTo(World.Chronicle, Events, _counters);
        Events.Flush();
    }

    private WorldSnapshot MakeSnapshot(long h, bool countHouseholds)
    {
        if (countHouseholds)
        {
            var hht = World.Households; int n = 0;
            for (int i = 0; i < hht.Count; i++) if (hht.MemberCount[i] > 0) n++;
            _households = n;
        }
        var v = World.Vital;
        return new WorldSnapshot(h, h, v.Alive, _households, v.Births, v.Deaths, v.Marriages, _counters.GetValueOrDefault("famines"));
    }

    public void Dispose()
    {
        if (_worker == null) return;
        _stop = true; _go.Release();
        _worker.Join(5000);
        _worker = null;
    }
}
