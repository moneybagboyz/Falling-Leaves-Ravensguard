using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ravensguard.Sim.Diagnostics;

public enum LogLevel : byte { Trace, Debug, Info, Warn, Error, Fatal }

/// <summary>Where the log goes and how it behaves (testers spec 3.1, 3.2).</summary>
public sealed class LogOptions
{
    /// <summary>The <c>logs</c> folder.</summary>
    public required string Directory { get; init; }
    public LogLevel MinLevel { get; set; } = LogLevel.Info;
    public int KeepSessions { get; init; } = 10;
    public long MaxTotalBytes { get; init; } = 50L * 1024 * 1024;
    /// <summary>The same line more than this many times in one second is collapsed.</summary>
    public int RepeatLimitPerSecond { get; init; } = 20;
    public int MaxLinesPerMinute { get; init; } = 20_000;
    /// <summary>Producers drop messages once this many wait for the writer (a runaway loop must not eat memory).</summary>
    public int MaxQueued { get; init; } = 200_000;
    public Func<DateTime> Clock { get; init; } = () => DateTime.Now;
    public Func<long>? Tick { get; set; }
    public Redactor Redactor { get; init; } = Redactor.ForCurrentUser();
    public Action<string>? Echo { get; init; }
}

/// <summary>
/// Rolling local session log: one plain-text line per record, written by a background thread so a frame only pays for a queue push.
/// Repeats are collapsed, lines are rate limited, paths are redacted, and WARN and above are flushed at once. The log is never sent anywhere.
/// </summary>
public sealed class LogService : IDisposable
{
    private sealed record Entry(DateTime Time, long Tick, LogLevel Level, string Category, string Message, string? Kv);
    private sealed class FlushRequest { public readonly ManualResetEventSlim Done = new(false); public bool EmitRepeats; }
    private sealed class RepeatState { public long Window = long.MinValue; public int Count, Suppressed; public Entry? Last; }

    private readonly LogOptions _opt;
    private readonly ConcurrentQueue<object> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Thread _thread;
    private readonly Dictionary<(LogLevel, string, string), RepeatState> _repeats = [];
    private int _queued;
    private long _dropped;
    private volatile bool _stop;
    private StreamWriter? _writer;
    private long _minuteWindow = long.MinValue; private int _minuteLines; private bool _capNoted;
    private readonly string _sessionJson;
    private SessionHeader _header;
    private readonly object _headerLock = new();

    public string Id { get; }
    public string LogPath { get; }
    public string Directory => _opt.Directory;
    public LogLevel MinLevel { get => _opt.MinLevel; set => _opt.MinLevel = value; }

    /// <summary>Starts a session: rotates old logs, opens <c>game-&lt;stamp&gt;.log</c> and writes <c>session-&lt;id&gt;.json</c> with <c>clean_exit = false</c>.</summary>
    public LogService(LogOptions options, SessionHeader? header = null)
    {
        _opt = options;
        System.IO.Directory.CreateDirectory(options.Directory);
        var now = options.Clock();
        Id = Guid.NewGuid().ToString("N")[..6];
        Rotate(options.Directory, options.KeepSessions - 1, options.MaxTotalBytes);
        LogPath = Path.Combine(options.Directory, $"game-{now:yyyyMMdd-HHmmss}-{Id}.log");
        _sessionJson = Path.Combine(options.Directory, $"session-{Id}.json");
        _writer = new StreamWriter(new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false), 64 * 1024) { NewLine = "\n", AutoFlush = false };
        _header = (header ?? new SessionHeader()) with { Id = Id, StartedReal = now.ToString("s", CultureInfo.InvariantCulture), CleanExit = false };
        WriteHeader();
        _thread = new Thread(Loop) { IsBackground = true, Name = "LogWriter" };
        _thread.Start();
    }

    // ------------------------------------------------------------- producing

    public bool Enabled(LogLevel level) => level >= _opt.MinLevel;

    public void Log(LogLevel level, string category, string message, string? kv = null)
    {
        if (level < _opt.MinLevel || _stop) return;
        if (Volatile.Read(ref _queued) >= _opt.MaxQueued) { Interlocked.Increment(ref _dropped); return; }
        Interlocked.Increment(ref _queued);
        _queue.Enqueue(new Entry(_opt.Clock(), _opt.Tick?.Invoke() ?? 0, level, category, message, kv));
        if (level >= LogLevel.Warn) Flush(0); else _signal.Release();
    }

    public void Trace(string cat, string msg, string? kv = null) => Log(LogLevel.Trace, cat, msg, kv);
    public void Debug(string cat, string msg, string? kv = null) => Log(LogLevel.Debug, cat, msg, kv);
    public void Info(string cat, string msg, string? kv = null) => Log(LogLevel.Info, cat, msg, kv);
    public void Warn(string cat, string msg, string? kv = null) => Log(LogLevel.Warn, cat, msg, kv);
    public void Error(string cat, string msg, string? kv = null) => Log(LogLevel.Error, cat, msg, kv);
    public void Fatal(string cat, string msg, string? kv = null) => Log(LogLevel.Fatal, cat, msg, kv);

    /// <summary>Waits until everything queued so far is on disk (<paramref name="timeoutMs"/> 0 only wakes the writer).</summary>
    public void Flush(int timeoutMs = 5000)
    {
        if (_stop && timeoutMs > 0) return;
        var req = new FlushRequest { EmitRepeats = timeoutMs > 0 };
        _queue.Enqueue(req); _signal.Release();
        if (timeoutMs > 0) req.Done.Wait(timeoutMs);
    }

    // ------------------------------------------------------------- session header

    public SessionHeader Header { get { lock (_headerLock) return _header; } }

    /// <summary>Updates the header (build, system, game ids) and rewrites <c>session-&lt;id&gt;.json</c>.</summary>
    public void UpdateHeader(Func<SessionHeader, SessionHeader> change)
    {
        lock (_headerLock) { _header = change(_header) with { Id = Id }; WriteHeader(); }
    }

    private void WriteHeader()
    {
        try { File.WriteAllText(_sessionJson, _opt.Redactor.Redact(JsonSerializer.Serialize(_header, SessionHeader.Json))); }
        catch (Exception) { /* a header that cannot be written must not stop the game */ }
    }

    /// <summary>Marks a normal exit: the next launch does not offer a recovery report.</summary>
    public void MarkCleanExit() => UpdateHeader(h => h with { CleanExit = true });

    // ------------------------------------------------------------- reading

    /// <summary>The last <paramref name="lines"/> lines of this session's log (flushes first).</summary>
    public string Tail(int lines = 5000)
    {
        Flush();
        return TailOf(LogPath, lines);
    }

    public static string TailOf(string path, int lines)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            var ring = new Queue<string>(lines + 1);
            string? l;
            while ((l = sr.ReadLine()) != null) { ring.Enqueue(l); if (ring.Count > lines) ring.Dequeue(); }
            return string.Join('\n', ring) + (ring.Count > 0 ? "\n" : "");
        }
        catch (Exception) { return ""; }
    }

    public long DroppedCount => Interlocked.Read(ref _dropped);

    // ------------------------------------------------------------- the writer

    private void Loop()
    {
        while (!_stop)
        {
            _signal.Wait(250);
            Drain();
        }
        Drain();
    }

    private void Drain()
    {
        bool wrote = false;
        while (_queue.TryDequeue(out var o))
        {
            try
            {
                if (o is Entry e) { Interlocked.Decrement(ref _queued); Handle(e); wrote = true; }
                else if (o is FlushRequest f) { if (f.EmitRepeats) EmitPendingRepeats(); _writer?.Flush(); f.Done.Set(); wrote = false; }
            }
            catch (Exception) { /* a full disk or a closed file must not kill the writer */ }
        }
        if (wrote) { try { _writer?.Flush(); } catch (Exception) { } }
    }

    private void Handle(Entry e)
    {
        long sec = e.Time.Ticks / TimeSpan.TicksPerSecond;
        long minute = sec / 60;
        var key = (e.Level, e.Category, e.Message);
        if (!_repeats.TryGetValue(key, out var st)) { if (_repeats.Count > 5000) _repeats.Clear(); _repeats[key] = st = new RepeatState(); }
        if (st.Window != sec) { EmitRepeat(st); st.Window = sec; st.Count = 0; }
        st.Count++;
        if (st.Count > _opt.RepeatLimitPerSecond) { st.Suppressed++; st.Last = e; return; }
        WriteLine(e, null, minute);
    }

    private void EmitRepeat(RepeatState st)
    {
        if (st.Suppressed > 0 && st.Last != null) WriteLine(st.Last, $"(repeated {st.Suppressed} times)", st.Last.Time.Ticks / TimeSpan.TicksPerSecond / 60);
        st.Suppressed = 0; st.Last = null;
    }

    private void EmitPendingRepeats() { foreach (var st in _repeats.Values) EmitRepeat(st); }

    private void WriteLine(Entry e, string? suffix, long minute)
    {
        if (minute != _minuteWindow) { _minuteWindow = minute; _minuteLines = 0; _capNoted = false; }
        if (++_minuteLines > _opt.MaxLinesPerMinute)
        {
            if (_capNoted) return;
            _capNoted = true;
            RawLine(e.Time, e.Tick, LogLevel.Warn, "boot", $"log rate cap of {_opt.MaxLinesPerMinute} lines per minute reached; further lines this minute are dropped", null);
            return;
        }
        RawLine(e.Time, e.Tick, e.Level, e.Category, suffix == null ? e.Message : e.Message + " " + suffix, e.Kv);
    }

    private void RawLine(DateTime t, long tick, LogLevel level, string cat, string msg, string? kv)
    {
        var sb = new StringBuilder(96);
        sb.Append(t.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(" | tick ").Append(tick).Append(" | ").Append(level.ToString().ToUpperInvariant())
          .Append(" | ").Append(cat).Append(" | ").Append(msg.Replace('\n', ' ').Replace('\r', ' '));
        if (!string.IsNullOrEmpty(kv)) sb.Append(" | ").Append(kv);
        string line = _opt.Redactor.Redact(sb.ToString());
        _writer!.Write(line); _writer.Write('\n');
        _opt.Echo?.Invoke(line);
    }

    public void Dispose()
    {
        if (_stop) return;
        Flush();
        _stop = true; _signal.Release();
        _thread.Join(3000);
        try { _writer?.Flush(); _writer?.Dispose(); } catch (Exception) { }
        _writer = null;
    }

    // ------------------------------------------------------------- retention

    /// <summary>Keeps the newest <paramref name="keep"/> sessions and at most <paramref name="maxBytes"/> of logs, oldest deleted first. Session files go with their log.</summary>
    public static void Rotate(string dir, int keep, long maxBytes)
    {
        try
        {
            var logs = new DirectoryInfo(dir).GetFiles("game-*.log").OrderBy(f => f.Name, StringComparer.Ordinal).ToList(); // the name starts with the timestamp
            long total = logs.Sum(f => f.Length);
            while (logs.Count > 0 && (logs.Count > keep || total > maxBytes))
            {
                var old = logs[0]; logs.RemoveAt(0); total -= old.Length;
                string id = Path.GetFileNameWithoutExtension(old.Name); id = id[(id.LastIndexOf('-') + 1)..];
                try { old.Delete(); File.Delete(Path.Combine(dir, $"session-{id}.json")); } catch (IOException) { }
            }
        }
        catch (Exception) { /* rotation is best effort */ }
    }
}

/// <summary>The build, the machine and the run a session belongs to (testers spec 3.3). No user name, host name, address or path outside <c>~</c>.</summary>
public sealed record SessionHeader
{
    public string Id { get; init; } = "";
    public string StartedReal { get; init; } = "";
    public BuildInfo Build { get; init; } = new();
    public SystemInfo System { get; init; } = new();
    public GameInfo Game { get; init; } = new();
    public bool CleanExit { get; init; }
    /// <summary>Set by the watchdog when the frame counter stopped for 10 s.</summary>
    public bool Hang { get; init; }

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

public sealed record ModRef(string Id, string Version, string Hash);

public sealed record BuildInfo
{
    public string Version { get; init; } = "0.0.0";
    public string Git { get; init; } = "";
    public string ContentHash { get; init; } = "";
    public List<ModRef> Mods { get; init; } = [];
    public string Flavour { get; init; } = "Dev";
}

public sealed record GameInfo
{
    public ulong Seed { get; init; }
    public string WorldId { get; init; } = "";
    public string RunId { get; init; } = "";
    public bool Ironman { get; init; }
    public string Difficulty { get; init; } = "default";
}

/// <summary>The machine, without anything that names its owner.</summary>
public sealed record SystemInfo
{
    public string Os { get; init; } = "";
    public string Cpu { get; init; } = "";
    public long RamMb { get; init; }
    public string Gpu { get; init; } = "";
    public string Driver { get; init; } = "";
    public string Dotnet { get; init; } = "";
    public string Godot { get; init; } = "";
    public string Renderer { get; init; } = "";
    public string Screen { get; init; } = "";
    public string Locale { get; init; } = "";

    /// <summary>What .NET knows. The game adds GPU, renderer, screen and Godot version.</summary>
    public static SystemInfo Detect() => new()
    {
        Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Cpu = $"{Environment.ProcessorCount} cores",
        RamMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024),
        Dotnet = Environment.Version.ToString(),
        Locale = CultureInfo.CurrentUICulture.Name,
    };
}
