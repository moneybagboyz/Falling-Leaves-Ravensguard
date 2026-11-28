using System.Diagnostics;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>
/// P8-PF-3: a stopwatch table for profiling the tick outside the abstract systems (the after-hour hooks, the scheduler, and any span a harness marks with <see cref="Span"/>).
/// Off (null <see cref="Table"/>) in the game and in every test that does not ask for it; the sim never reads it, so results cannot depend on it. Wall time stays in the tool.
/// </summary>
public static class PfClock
{
    /// <summary>Wall ticks by span name; set by a soak or a tool, read after the run.</summary>
    public static Dictionary<string, long>? Table { get; set; }

    public static void Add(string name, long ticks) { var t = Table; if (t != null) lock (t) t[name] = t.GetValueOrDefault(name) + ticks; }

    /// <summary>Times the rest of a block: <c>using var _ = PfClock.Span("talk.seed");</c> (a no-op when off).</summary>
    public static Scope Span(string name) => new(Table == null ? null : name);

    public readonly struct Scope(string? name) : IDisposable
    {
        private readonly long _t0 = name == null ? 0 : Stopwatch.GetTimestamp();
        public void Dispose() { if (name != null) Add(name, Stopwatch.GetTimestamp() - _t0); }
    }
}
