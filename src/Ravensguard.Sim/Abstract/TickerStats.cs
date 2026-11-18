using System.Diagnostics;

namespace Ravensguard.Sim.Abstract;

/// <summary>Accumulated wall time of a phase or a system (Stopwatch ticks).</summary>
public sealed class TimingCell
{
    public long Ticks, Calls, MaxTicks;
    public void Add(long ticks) { Ticks += ticks; Calls++; if (ticks > MaxTicks) MaxTicks = ticks; }
    public double TotalMs => Ticks * 1000.0 / Stopwatch.Frequency;
    public double MeanMs => Calls == 0 ? 0 : TotalMs / Calls;
    public double MaxMs => MaxTicks * 1000.0 / Stopwatch.Frequency;
}

/// <summary>
/// Per-phase and per-system timing of the ticker (abstract spec 3.1 budgets: hourly p99 at most 150 ms, barrier apply at most 5 ms). Written by the ticker only;
/// read it after the run or from the main thread between pumps.
/// </summary>
public sealed class TickerStats
{
    public const int MaxRecordedHours = 1 << 20;

    private readonly TimingCell[] _phases = new TimingCell[8];
    private readonly Dictionary<string, TimingCell> _systems = new(StringComparer.Ordinal);
    private readonly List<float> _hourMs = [], _hour0Ms = [], _seasonDayMs = [], _barrierMs = [];
    public TimingCell BarrierStart { get; } = new();
    public TimingCell BarrierEnd { get; } = new();
    public TimingCell Commands { get; } = new();
    public long Hours { get; private set; }

    public TickerStats() { for (int i = 0; i < _phases.Length; i++) _phases[i] = new TimingCell(); }

    public TimingCell Phase(TickPhase p) => _phases[System.Numerics.BitOperations.TrailingZeroCount((int)p)];
    public IReadOnlyDictionary<string, TimingCell> Systems => _systems;

    internal TimingCell SystemCell(string name)
    {
        if (!_systems.TryGetValue(name, out var c)) _systems[name] = c = new TimingCell();
        return c;
    }

    internal void RecordBarrier(long ticks) { if (_barrierMs.Count < MaxRecordedHours) _barrierMs.Add((float)(ticks * 1000.0 / Stopwatch.Frequency)); }

    private readonly List<(string Name, long Ticks)> _curHour = [];
    private int _gen0 = -1, _gen1, _gen2;
    /// <summary>The slowest hour so far: its hour index, milliseconds, the systems that ran in it (slowest first) and the garbage collections during it (diagnostics).</summary>
    public (long Hour, double Ms, string Systems, int Gen0, int Gen1, int Gen2) WorstHour { get; private set; } = (-1, 0, "", 0, 0, 0);

    internal void HourSystem(string name, long ticks) => _curHour.Add((name, ticks));

    internal void RecordHour(long hourOfDay, bool seasonStartDay, long ticks, long hour = -1)
    {
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        if (_gen0 < 0) { _gen0 = g0; _gen1 = g1; _gen2 = g2; }
        double msAll = ticks * 1000.0 / Stopwatch.Frequency;
        if (msAll > WorstHour.Ms)
            WorstHour = (hour, msAll, string.Join(", ", _curHour.GroupBy(x => x.Name).Select(g => (g.Key, Ms: g.Sum(x => x.Ticks) * 1000.0 / Stopwatch.Frequency)).OrderByDescending(x => x.Ms).Take(8).Select(x => $"{x.Key} {x.Ms:0.0}")),
                g0 - _gen0, g1 - _gen1, g2 - _gen2);
        _curHour.Clear(); _gen0 = g0; _gen1 = g1; _gen2 = g2;
        Hours++;
        if (_hourMs.Count >= MaxRecordedHours) return;
        float ms = (float)(ticks * 1000.0 / Stopwatch.Frequency);
        _hourMs.Add(ms);
        if (hourOfDay == 0) _hour0Ms.Add(ms);
        if (seasonStartDay) _seasonDayMs.Add(ms);
    }

    public IReadOnlyList<float> HourMs => _hourMs;
    /// <summary>Hours that were hour 0 of a day (the heaviest slice: daily globals).</summary>
    public IReadOnlyList<float> Hour0Ms => _hour0Ms;
    /// <summary>Every hour of the first day of a season.</summary>
    public IReadOnlyList<float> SeasonDayMs => _seasonDayMs;

    private static double Pct(List<float> xs, double q)
    {
        if (xs.Count == 0) return 0;
        var a = xs.ToArray(); Array.Sort(a);
        return a[Math.Min(a.Length - 1, (int)(q * a.Length))];
    }

    /// <summary>The time the barrier took to apply the outbox and swap the snapshot, per hour (spec: at most 5 ms).</summary>
    public double P99BarrierApplyMs => Pct(_barrierMs, 0.99);
    public double MedianHourMs => Pct(_hourMs, 0.5);
    public double P99HourMs => Pct(_hourMs, 0.99);
    public double MaxHourMs => _hourMs.Count == 0 ? 0 : _hourMs.Max();
    public double MedianHour0Ms => Pct(_hour0Ms, 0.5);
    public double MaxHour0Ms => _hour0Ms.Count == 0 ? 0 : _hour0Ms.Max();
    public double MaxSeasonDayHourMs => _seasonDayMs.Count == 0 ? 0 : _seasonDayMs.Max();

    /// <summary>The slowest hour of the warm-up that <see cref="Reset(bool)"/> set aside (the first day of a run, when the JIT compiles every system), 0 when none.</summary>
    public double WarmupMaxHourMs { get; private set; }

    /// <summary>Clears every timing; with <paramref name="warmup"/> the slowest hour so far is kept as <see cref="WarmupMaxHourMs"/> and reported apart.</summary>
    public void Reset(bool warmup)
    {
        if (warmup) WarmupMaxHourMs = Math.Max(WarmupMaxHourMs, MaxHourMs);
        Reset();
    }

    public void Reset()
    {
        WorstHour = (-1, 0, "", 0, 0, 0);
        _hourMs.Clear(); _barrierMs.Clear(); _hour0Ms.Clear(); _seasonDayMs.Clear(); _systems.Clear(); Hours = 0;
        foreach (var p in _phases) { p.Ticks = p.Calls = p.MaxTicks = 0; }
        foreach (var c in new[] { BarrierStart, BarrierEnd, Commands }) { c.Ticks = c.Calls = c.MaxTicks = 0; }
    }

    /// <summary>A short table of the phases and systems by total time, for logs and the perf report.</summary>
    public string Report()
    {
        var sb = new System.Text.StringBuilder();
        if (WarmupMaxHourMs > 0) sb.AppendLine($"warm-up day (JIT compilation of every system, set aside): slowest hour {WarmupMaxHourMs:0.0} ms");
        sb.AppendLine($"{Hours} hours; hour median {MedianHourMs:0.000} ms, p99 {P99HourMs:0.000} ms, max {MaxHourMs:0.000} ms; hour-0 median {MedianHour0Ms:0.000} ms, max {MaxHour0Ms:0.000} ms");
        foreach (TickPhase p in Enum.GetValues<TickPhase>())
        {
            if (p is TickPhase.None or TickPhase.All) continue;
            var c = Phase(p);
            if (c.Calls > 0) sb.AppendLine($"  {p,-16} {c.TotalMs,10:0.0} ms total  {c.MeanMs,8:0.000} ms/call  max {c.MaxMs:0.000}");
        }
        foreach (var (name, c) in _systems.OrderByDescending(x => x.Value.Ticks)) sb.AppendLine($"  system {name,-22} {c.TotalMs,10:0.0} ms total  {c.MeanMs,8:0.000} ms/call  max {c.MaxMs:0.000}");
        if (WorstHour.Hour >= 0) sb.AppendLine($"  worst hour {WorstHour.Hour} (day {WorstHour.Hour / 24}, hour {WorstHour.Hour % 24}) {WorstHour.Ms:0.0} ms: {WorstHour.Systems}; collections during it gen0 {WorstHour.Gen0}, gen1 {WorstHour.Gen1}, gen2 {WorstHour.Gen2}");
        sb.AppendLine($"  barrier start {BarrierStart.MeanMs:0.000} ms/hour, apply {BarrierEnd.MeanMs:0.000} ms/hour (p99 {P99BarrierApplyMs:0.000}, max {BarrierEnd.MaxMs:0.000})");
        return sb.ToString();
    }
}

/// <summary>An immutable view of the world published once per hour at the barrier (abstract spec 2.5); read it from any thread through <see cref="WorldTicker.Snapshot"/>.</summary>
public sealed record WorldSnapshot(long Version, long Hour, long Population, int Households, long Births, long Deaths, long Marriages, long Famines);
