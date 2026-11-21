using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Sim.Diagnostics;

namespace Ravensguard.Game.Diagnostics;

/// <summary>
/// The game's side of testers and crash reports (docs/specs/phase3/testers-and-crash-reports.md): the local session log in <c>user://logs</c>, the crash handler,
/// the consent store and the report builder. No networking anywhere: a report is a zip written to <c>user://reports</c>.
/// </summary>
public static class GameDiagnostics
{
    static LogService? _log;
    static ConsentStore? _consent;
    static ReportBuilder? _reports;
    static Redactor _redactor = Redactor.ForCurrentUser();
    static int _crashedMask;      // one bit per kind of fatal error: a tick-fault report must not use up the guard of a later real crash
    internal enum FatalKind { Crash = 1, Worker = 2 }

    /// <summary>True the first time a kind of fatal error is claimed in a session; the second claim of the same kind is false.</summary>
    internal static bool Claim(FatalKind kind) => (Interlocked.Or(ref _crashedMask, (int)kind) & (int)kind) == 0;
    static int _mainThread;
    static SceneTree? _tree;
    static string _t0Path = "";
    static long _t0Tick = -1, _pendingTick = -1;
    static readonly object _t0Lock = new();

    public static LogService? Log => _log;
    public static ConsentStore Consent => _consent ??= new ConsentStore(Path("profile"), Path("reports"));
    public static ReportBuilder Reports => _reports ??= new ReportBuilder(Consent, _redactor, Path("reports"));
    public static Redactor Redactor => _redactor;

    /// <summary>The recorder of the live view being played (commands and record hashes since the last autosave); null outside a street view.</summary>
    public static SessionRecorder? Recorder { get; set; }
    /// <summary>Asks the active screen where the tester is (screen, entity, region ...). Set by the views.</summary>
    public static Func<System.Collections.Generic.Dictionary<string, string>>? WhereProvider { get; set; }
    /// <summary>Raised on the main thread each frame by the App; the heartbeat reads the counter.</summary>
    public static long Frames;
    public static string? LastCrashId { get; private set; }
    /// <summary>The last crash dialog text (for tests).</summary>
    public static string? LastCrashDialog { get; private set; }
    /// <summary>Tests turn the blocking OS dialog off.</summary>
    public static bool ShowDialogs { get; set; } = true;

    static string Path(string sub) => ProjectSettings.GlobalizePath("user://" + sub);

    // ------------------------------------------------------------------ start up

    /// <summary>Starts the log, the crash hooks and the heartbeat. Safe to call once, first thing in <c>App._Ready</c>.</summary>
    public static void Init(SceneTree tree, string[] args)
    {
        if (_log != null) return;
        _tree = tree; _mainThread = System.Environment.CurrentManagedThreadId;
        try
        {
            bool dev = OS.IsDebugBuild();
            _redactor = new Redactor(OS.GetEnvironment(OS.GetName() == "Windows" ? "USERPROFILE" : "HOME"), OS.GetEnvironment(OS.GetName() == "Windows" ? "USERNAME" : "USER"));
            var opt = new LogOptions
            {
                Directory = Path("logs"), Redactor = _redactor,
                MinLevel = args.Contains("--verbose") ? Ravensguard.Sim.Diagnostics.LogLevel.Debug : Ravensguard.Sim.Diagnostics.LogLevel.Info,
                Echo = dev ? l => GD.Print("[log] " + l) : null,
            };
            var sys = SystemInfo.Detect() with
            {
                Godot = Engine.GetVersionInfo()["string"].AsString(), Locale = TranslationServer.GetLocale(),
                Gpu = Safe(() => RenderingServer.GetVideoAdapterName()), Driver = Safe(() => RenderingServer.GetVideoAdapterApiVersion()),
                Renderer = Safe(() => RenderingServer.GetCurrentRenderingMethod()), Screen = Safe(() => $"{DisplayServer.ScreenGetSize().X}x{DisplayServer.ScreenGetSize().Y}"),
                Os = OS.GetName() + " " + OS.GetVersion(),
            };
            var previous = FindUncleanSession(opt.Directory);
            _log = new LogService(opt, new SessionHeader { System = sys, Build = BuildInfoNow() });
            _log.Info("boot", "session start", $"version={BuildVersion} godot={sys.Godot} os={sys.Os}");
            if (previous != null) { LastCrashId = previous; _log.Warn("boot", "the previous session did not close properly", "session=" + previous); }
            OS.AddLogger(new EngineLogBridge());
            RedactEngineLogs();
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Fatal("unhandled exception", e.ExceptionObject as Exception, e.ExceptionObject?.ToString() ?? "unknown", Thread.CurrentThread.Name ?? "worker");
            TaskScheduler.UnobservedTaskException += (_, e) => { _log?.Error("sim", "unobserved task exception: " + e.Exception.GetBaseException().Message); e.SetObserved(); };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { _log?.MarkCleanExit(); _log?.Dispose(); } catch (Exception) { } };
            StartHeartbeat();
        }
        catch (Exception ex) { GD.PushWarning("diagnostics could not start: " + ex.Message); }
    }

    /// <summary>The localised text of a key, or the English fallback when the translation server is not usable (a crash can happen before it is loaded or on another thread).</summary>
    static string Tr(string key, string fallback) { try { var t = L.T(key); return t == key || t.Length == 0 ? fallback : t; } catch (Exception) { return fallback; } }

    /// <summary>
    /// The engine's own log (<c>godot.log</c>, written by the engine itself and so never redacted as it is written) lives in <c>user://engine</c>, not in <c>logs</c>: no report takes it and the folder a crash dialog names holds only
    /// redacted files. The rotated copies of earlier sessions are redacted here (home folder and user name become ~) and only the newest five are kept.
    /// </summary>
    static void RedactEngineLogs()
    {
        try
        {
            string dir = Path("engine"); if (!Directory.Exists(dir)) return;
            var old = new DirectoryInfo(dir).GetFiles("godot*.log").Where(f => f.Name != "godot.log").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            for (int i = 0; i < old.Count; i++)
            {
                if (i >= 5) { old[i].Delete(); continue; }
                string text = File.ReadAllText(old[i].FullName), red = _redactor.Redact(text);
                if (red != text) File.WriteAllText(old[i].FullName, red);
            }
        }
        catch (Exception ex) { GD.PushWarning("engine logs could not be redacted: " + ex.Message); }
    }

    static string Safe(Func<string> f) { try { return f(); } catch (Exception) { return ""; } }

    public const string BuildVersion = "0.4.0-dev";
    public static BuildInfo BuildInfoNow() => new() { Version = BuildVersion, Git = Safe(() => ReadGit()), Flavour = OS.IsDebugBuild() ? "Dev" : "Release", ContentHash = ContentHashProvider?.Invoke() ?? "" };
    /// <summary>Set once the content is loaded (the hash of base content and mods).</summary>
    public static Func<string>? ContentHashProvider { get; set; }

    static string ReadGit()
    {
        try
        {
            string dir = ProjectSettings.GlobalizePath("res://");
            for (int i = 0; i < 4 && dir.Length > 0; i++, dir = System.IO.Path.GetDirectoryName(dir) ?? "")
            {
                string head = System.IO.Path.Combine(dir, ".git", "HEAD");
                if (!File.Exists(head)) continue;
                string h = File.ReadAllText(head).Trim();
                if (h.StartsWith("ref: ")) { string r = System.IO.Path.Combine(dir, ".git", h[5..]); h = File.Exists(r) ? File.ReadAllText(r).Trim() : h; }
                return h.Length >= 7 ? h[..7] : h;
            }
        }
        catch (Exception) { }
        return "";
    }

    /// <summary>The id of the newest earlier session that did not close cleanly, or null.</summary>
    static string? FindUncleanSession(string dir)
    {
        try
        {
            var last = new DirectoryInfo(dir).GetFiles("session-*.json").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (last == null) return null;
            var h = JsonSerializer.Deserialize<SessionHeader>(File.ReadAllText(last.FullName), SessionHeader.Json);
            return h is { CleanExit: false } ? h.Id : null;
        }
        catch (Exception) { return null; }
    }

    // ------------------------------------------------------------------ logging shortcuts (no-ops before Init)

    public static void Info(string cat, string msg, string? kv = null) => _log?.Info(cat, msg, kv);
    public static void Warn(string cat, string msg, string? kv = null) => _log?.Warn(cat, msg, kv);
    public static void Error(string cat, string msg, string? kv = null) => _log?.Error(cat, msg, kv);
    public static void Debug(string cat, string msg, string? kv = null) => _log?.Debug(cat, msg, kv);

    /// <summary>Normal quit: the next launch will not offer a recovery report.</summary>
    public static void CleanExit() { try { _log?.Info("boot", "clean exit"); _log?.MarkCleanExit(); _log?.Flush(); } catch (Exception) { } }

    // ------------------------------------------------------------------ heartbeat (hang detection)

    static void StartHeartbeat()
    {
        long lastFrames = -1; int still = 0;
        var t = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(1000);
                try
                {
                    long f = Interlocked.Read(ref Frames);
                    if (f == lastFrames && f > 0) { if (++still == 10) { _log?.Fatal("boot", "hang: the frame counter has not advanced for 10 s", $"frames={f}"); _log?.UpdateHeader(h => h with { Hang = true }); } }
                    else still = 0;
                    lastFrames = f;
                    File.WriteAllText(Path("heartbeat.json"), JsonSerializer.Serialize(new { real = DateTime.UtcNow.ToString("s"), frames = f, tick = Recorder?.World.Tick ?? 0 }));
                }
                catch (Exception) { }
            }
        }) { IsBackground = true, Name = "Heartbeat" };
        t.Start();
    }

    // ------------------------------------------------------------------ the crash handler

    /// <summary>
    /// A fatal error: log it, write the crash report (a zip when the tester consented, plain files in the log folder when not), tell the tester where it is.
    /// Never throws, and only the first fatal error of each kind (a crash, a worker fault) in a session is reported.
    /// </summary>
    public static void Fatal(string what, Exception? ex, string text, string thread) => Fatal(FatalKind.Crash, what, ex, text, thread);

    static void Fatal(FatalKind kind, string what, Exception? ex, string text, string thread)
    {
        if (!Claim(kind)) return;
        string path = "", message;
        try
        {
            _log?.Fatal("boot", what + " on " + thread, ex == null ? null : "type=" + ex.GetType().Name);
            _log?.Fatal("boot", (ex?.ToString() ?? text).Replace('\n', ' '));
            _log?.Flush();
            byte[]? shot = System.Environment.CurrentManagedThreadId == _mainThread ? TryScreenshot() : null; // the render thread may be gone; never touch it from a worker
            var rec = Recorder;
            var crashText = new StringBuilder();
            crashText.AppendLine($"{what}\nthread: {thread}\ntick: {rec?.World.Tick ?? 0}\ntime: {DateTime.Now:s}\nbuild: {BuildVersion}\n");
            crashText.AppendLine(ex?.ToString() ?? text);
            var (zip, isZip) = Reports.WriteCrash(BuildRequest(ReportKind.Crash, crashText.ToString(), shot, "Bug", "", "", "", true), Path("logs"));
            path = zip;
            message = isZip
                ? string.Format(Tr("CRASH_SAVED", "The game stopped. A report was saved to:\n{0}\nNothing was sent."), _redactor.Redact(path))
                : string.Format(Tr("CRASH_NO_ZIP", "The game stopped. Nothing was sent and no report zip was made (reports are off until you agree in the F8 screen). The log is in:\n{0}"), _redactor.Redact(Path("logs")));
        }
        catch (Exception inner)
        {
            message = string.Format(Tr("CRASH_PLAIN", "The game stopped. The log is in the logs folder. ({0})"), inner.GetType().Name);
        }
        LastCrashDialog = message;
        _log?.Info("boot", "crash handled", "path=" + path);
        try { _log?.Flush(); } catch (Exception) { }
        if (ShowDialogs) { try { OS.Alert(message, "Falling Leaves: Ravensguard"); } catch (Exception) { } }
    }

    /// <summary>Called by the engine log bridge for a C# exception that Godot caught in a callback (a frame, a signal): reported like any fatal error, then the game quits.</summary>
    internal static void ScriptException(string text)
    {
        Fatal("exception in a frame or callback", null, text, System.Environment.CurrentManagedThreadId == _mainThread ? "main" : "worker");
        if (_tree != null && ShowDialogs) Callable.From(() => _tree.Quit(1)).CallDeferred();
    }

    /// <summary>A background worker (the world tick, a region build) failed: the crash report is written and shown, the game keeps running on what it has.</summary>
    public static void ReportWorkerFault(Exception ex, string thread) => Fatal(FatalKind.Worker, "worker thread fault", ex, ex.ToString(), thread);

    /// <summary>Test hook: allows another simulated crash.</summary>
    public static void ResetCrashGuard() => Interlocked.Exchange(ref _crashedMask, 0);

    // ------------------------------------------------------------------ report requests

    public static byte[]? TryScreenshot()
    {
        try
        {
            if (_tree == null || DisplayServer.GetName() == "headless") return null;
            var img = _tree.Root.GetViewport().GetTexture().GetImage();
            if (img == null || img.IsEmpty()) return null;
            if (img.GetWidth() > 1920) img.Resize(1920, (int)(img.GetHeight() * 1920.0 / img.GetWidth()));
            return img.SavePngToBuffer();
        }
        catch (Exception) { return null; }
    }

    /// <summary>Everything a report holds, taken on the calling thread (call it on the main thread).</summary>
    public static ReportRequest BuildRequest(ReportKind kind, string? crashText, byte[]? screenshot, string category, string happened, string expected, string understood, bool includeAutosave, bool includeLog = true)
    {
        var rec = Recorder;
        byte[]? trace = null; string? csv = null; string? commands = null; long bt = 0, et = 0;
        if (rec != null) { (trace, csv) = rec.Export(); commands = rec.Commands.Recent(50); bt = rec.Commands.BaseTick; et = rec.Commands.EndTick; }
        var where = new System.Collections.Generic.Dictionary<string, string>();
        try { if (WhereProvider != null) foreach (var kv in WhereProvider()) where[kv.Key] = kv.Value; } catch (Exception) { }
        var header = _log?.Header ?? new SessionHeader { System = SystemInfo.Detect(), Build = BuildInfoNow() };
        long t0Tick; string t0Path; lock (_t0Lock) { t0Tick = _t0Tick; t0Path = _t0Path; }
        string? t0 = !string.IsNullOrEmpty(t0Path) && File.Exists(t0Path) ? t0Path : null;
        return new ReportRequest
        {
            Kind = kind, Category = category, WhatHappened = happened, WhatExpected = expected, Understood = understood,
            Session = header with { Build = BuildInfoNow() }, Where = where,
            SettingsJson = JsonSerializer.Serialize(new { ui_scale = Settings.UiScale, screen_shake = Settings.ScreenShake, flashes = Settings.Flashes, slow_motion = Settings.SlowMotion }),
            ContentManifestJson = JsonSerializer.Serialize(new { content_hash = BuildInfoNow().ContentHash, mods = header.Build.Mods }),
            LogPath = _log?.LogPath, CrashText = crashText, AutosavePath = rec != null && t0Tick == bt ? t0 : null,
            Screenshot = screenshot, ReplayTrace = trace, HashesCsv = csv, RecentCommands = commands, ReplayBaseTick = bt, ReplayEndTick = et,
            IncludeAutosave = includeAutosave, IncludeLog = includeLog, PreviousCrashId = LastCrashId,
        };
    }

    /// <summary>True when the kept autosave copy is the start of the recorder's trace window (a report made now can be replayed).</summary>
    public static bool ReplayReady { get { long tk; string p; lock (_t0Lock) { tk = _t0Tick; p = _t0Path; } return Recorder is { } r && tk == r.Commands.BaseTick && File.Exists(p); } }

    // ------------------------------------------------------------------ replay window (the autosave the trace starts from)

    /// <summary>A run was loaded or created: the file it came from is T0 of the trace (tick 0 of this live world).</summary>
    public static void NoteSessionStart(string? savePath)
    {
        lock (_t0Lock) { _t0Tick = -1; _t0Path = ""; }
        if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath)) return;
        CopyT0(savePath, 0);
    }

    /// <summary>An autosave snapshot was taken at <paramref name="tick"/> (the recorder restarted its window there).</summary>
    public static void NoteAutosaveBegin(long tick) => _pendingTick = tick;

    /// <summary>The autosave of the last <see cref="NoteAutosaveBegin"/> reached the disk: keep a copy as T0, safe from the slot rotation.</summary>
    public static void NoteAutosaveDone(string path)
    {
        if (_pendingTick >= 0) CopyT0(path, _pendingTick);
    }

    static void CopyT0(string source, long tick)
    {
        string dest = System.IO.Path.Combine(Path("replay"), "t0.sav");
        lock (_t0Lock) _t0Tick = -1;
        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
                string tmp = dest + ".tmp." + tick;          // copy aside and move into place: a report reading t0.sav never sees a half-written file
                File.Copy(source, tmp, true);
                lock (_t0Lock) { File.Move(tmp, dest, true); _t0Path = dest; _t0Tick = tick; }   // the tick is set last: a reader that sees the tick sees the file
            }
            catch (Exception ex) { _log?.Warn("report", "could not keep the autosave copy for replay: " + ex.Message); }
        });
    }
}

/// <summary>Engine messages (push_error, push_warning, C# exceptions Godot catches) land in the same log file; a C# exception is a fatal error.</summary>
sealed partial class EngineLogBridge : Logger
{
    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        string msg = $"{file}:{line} {function}: {code} {rationale}";
        if (errorType == 1) { GameDiagnostics.Log?.Warn("engine", msg); return; }
        GameDiagnostics.Log?.Error("engine", msg);
        if (errorType == 2 && (code.Contains("Exception") || rationale.Contains("Exception")) || IsManagedException(code, rationale)) GameDiagnostics.ScriptException(msg);
    }

    static readonly System.Text.RegularExpressions.Regex ManagedException = new(@"Unhandled exception|\bSystem\.[A-Za-z.]*Exception\b|\bGodot\.[A-Za-z.]*Exception\b|\b[A-Z][A-Za-z]+Exception:", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>A C# exception that escaped a callback on the main thread (a signal, a timer, a _Process) reaches the engine log as a plain error (type 0, not a script error), so it is recognised by what it says.</summary>
    internal static bool IsManagedException(string code, string rationale) => ManagedException.IsMatch(code ?? "") || ManagedException.IsMatch(rationale ?? "");

    public override void _LogMessage(string message, bool error) { }
}
