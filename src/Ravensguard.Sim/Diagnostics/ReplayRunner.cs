using System.Diagnostics;
using Ravensguard.Sim.Live;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>How a replay ended.</summary>
public sealed record ReplayResult(long StartTick, long EndTick, long StepsRun, StateHashTrail Replayed, Divergence? Divergence, Exception? Exception, long StopTick, long RecordedEnd = 0)
{
    public bool Matched => Divergence == null && Exception == null;
    /// <summary>True when the replay reached the recorded end (or crash) tick.</summary>
    public bool Completed => EndTick >= RecordedEnd;

    public string Describe()
    {
        if (Exception != null) return $"replay stopped at tick {EndTick} with {Exception.GetType().Name}: {Exception.Message}\n{Exception.StackTrace}";
        if (Divergence != null) return $"DIVERGED: {Divergence}";
        return $"replay reached tick {EndTick} of {RecordedEnd} ({StepsRun} steps); {Replayed.Count} record hashes match";
    }
}

public sealed class ReplayOptions
{
    /// <summary>Stop at this tick instead of the recording's end (for inspection).</summary>
    public long? UntilTick { get; init; }
    /// <summary>Compare against the recorded hashes (false just runs).</summary>
    public bool Verify { get; init; } = true;
    /// <summary>Called after every step with the absolute tick; return false to stop (the dev "step" and "break" tools).</summary>
    public Func<long, bool>? AfterStep { get; init; }
}

/// <summary>
/// Deterministic replay of a bug report (testers spec 4.5): the world loaded from the autosave (T0) is stepped at the fixed 20 Hz with the recorded commands
/// applied at the ticks they were issued, and the record hashes are compared with the recording. It stops at the end of the trace (the crash or report tick),
/// at the first exception, or at the first hash that differs. Replay is a promise about the same build and the same content only.
/// </summary>
public static class ReplayRunner
{
    /// <summary>Runs <paramref name="world"/> (the state at <paramref name="log"/>'s base tick) forward. <paramref name="expected"/> may be null.</summary>
    public static ReplayResult Run(LiveWorld world, CommandLog log, StateHashTrail? expected, ReplayOptions? options = null)
    {
        options ??= new ReplayOptions();
        long baseTick = log.BaseTick;
        long stop = Math.Min(options.UntilTick ?? long.MaxValue, log.EndTick);
        var cmds = log.Items;
        var replayed = new StateHashTrail();
        var want = new Dictionary<long, HashRecord>();
        if (expected != null) foreach (var r in expected.Items) want[r.Tick] = r;
        int next = 0;
        long abs = baseTick, steps = 0;
        Divergence? div = null; Exception? error = null;
        int idx = 0;

        bool Check(long tick)
        {
            var rec = new HashRecord(tick, StateHashTrail.KindLive, LiveStateHash.Compute(world));
            replayed.Add(rec);
            if (options.Verify && want.TryGetValue(tick, out var exp) && exp.Kind == rec.Kind)
            {
                int i = expected!.Items.ToList().FindIndex(x => x.Tick == tick && x.Kind == rec.Kind);
                if (exp.Hash != rec.Hash) { div = new Divergence(i < 0 ? idx : i, tick, rec.Kind, exp.Hash, rec.Hash); return false; }
            }
            idx++;
            return true;
        }

        try
        {
            // commands issued at the base tick stand from the start; the hash of the loaded state itself is the first check
            while (next < cmds.Count && cmds[next].Tick <= abs) cmds[next++].Apply(world);
            if (!Check(abs)) return Finish();
            while (abs < stop)
            {
                world.Step(); steps++; abs++;
                if (abs % StateHashTrail.Interval == 0 && !Check(abs)) break;
                while (next < cmds.Count && cmds[next].Tick <= abs) cmds[next++].Apply(world);
                if (options.AfterStep != null && !options.AfterStep(abs)) break;
            }
        }
        catch (Exception ex) { error = ex; }
        return Finish();

        ReplayResult Finish() => new(baseTick, abs, steps, replayed, div, error, stop, log.EndTick);
    }
}
