using System.IO.Compression;
using System.Text;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Tests.Diagnostics;

/// <summary>Testers and crash reports (docs/specs/phase3/testers-and-crash-reports.md): TR-1..TR-4, TR-6, TR-10, TR-12, TR-14.</summary>
public sealed class DiagnosticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rg-diag-" + Guid.NewGuid().ToString("N"));
    private static readonly GameTime Noon = GameTime.FromCalendar(1, Season.Spring, 8, 12);
    private static readonly Redactor Alice = new("/home/alice", "alice");

    public DiagnosticsTests() { Directory.CreateDirectory(_root); }
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private string Sub(string n) { var p = Path.Combine(_root, n); Directory.CreateDirectory(p); return p; }

    private static LiveWorld Field()
    {
        var g = new LevelGrid(60, 30, 0, 1);
        g.FillRect(0, 0, 59, 29, 0, true);
        for (int y = 0; y < 30; y++) if (y != 15) g.SetOpen(30, y, 0, false);
        return new LiveWorld(g, new LightGrid(g), new Cell3(5, 5, 0), Noon);
    }

    private static LogService NewLog(string dir, int keep = 10, long max = 50L << 20, DateTime? at = null, Redactor? red = null, int maxQueued = 200_000)
        => new(new LogOptions { Directory = dir, KeepSessions = keep, MaxTotalBytes = max, Clock = () => at ?? DateTime.Now, Redactor = red ?? Alice, MaxQueued = maxQueued });

    // ------------------------------------------------------------------ logging

    [Fact]
    public void Log_lines_have_the_documented_format_and_flush_on_warn()
    {
        using var log = NewLog(Sub("logs"), at: new DateTime(2026, 12, 1, 18, 20, 41, 123));
        log.Info("boot", "hello", "k=v");
        log.Warn("save", "careful");
        log.Flush();
        var lines = File.ReadAllLines(log.LogPath);
        Assert.Equal("18:20:41.123 | tick 0 | INFO | boot | hello | k=v", lines[0]);
        Assert.Equal("18:20:41.123 | tick 0 | WARN | save | careful", lines[1]);
        Assert.False(log.Header.CleanExit);
        log.MarkCleanExit();
        Assert.True(log.Header.CleanExit);
    }

    [Fact]
    public void Default_level_is_info_so_debug_is_dropped()
    {
        using var log = NewLog(Sub("logs"));
        log.Debug("sim", "noise"); log.Info("sim", "kept"); log.Flush();
        Assert.DoesNotContain("noise", File.ReadAllText(log.LogPath));
        log.MinLevel = LogLevel.Debug; log.Debug("sim", "now seen"); log.Flush();
        Assert.Contains("now seen", File.ReadAllText(log.LogPath));
    }

    [Fact] // TR-1
    public void Rotation_keeps_the_newest_ten_sessions_and_trims_to_the_size_cap()
    {
        var dir = Sub("logs");
        for (int i = 0; i < 12; i++)
        {
            File.WriteAllText(Path.Combine(dir, $"game-20260101-{i:D6}-a{i:D5}.log"), "x");
            File.WriteAllText(Path.Combine(dir, $"session-a{i:D5}.json"), "{}");
        }
        LogService.Rotate(dir, 10, 50L << 20);
        var left = Directory.GetFiles(dir, "game-*.log").Select(Path.GetFileName).OrderBy(x => x).ToList();
        Assert.Equal(10, left.Count);
        Assert.DoesNotContain("game-20260101-000000-a00000.log", left);
        Assert.Equal(10, Directory.GetFiles(dir, "session-*.json").Length);

        dir = Sub("big");
        for (int i = 0; i < 6; i++) File.WriteAllBytes(Path.Combine(dir, $"game-20260101-{i:D6}-b{i:D5}.log"), new byte[10 << 20]); // 60 MB
        LogService.Rotate(dir, 10, 50L << 20);
        Assert.True(Directory.GetFiles(dir, "game-*.log").Sum(f => new FileInfo(f).Length) <= 50L << 20);
        Assert.False(File.Exists(Path.Combine(dir, "game-20260101-000000-b00000.log"))); // oldest first
    }

    [Fact] // TR-2 (scaled down: the rule, not the speed)
    public void A_message_repeated_in_one_second_collapses_to_a_few_lines_and_a_count()
    {
        var at = new DateTime(2026, 1, 1, 12, 0, 0);
        using var log = NewLog(Sub("logs"), at: at, maxQueued: int.MaxValue);
        for (int i = 0; i < 100_000; i++) log.Error("sim", "same failure");
        log.Info("sim", "next"); // a different line; the flush below emits the repeat count
        log.Flush();
        var lines = File.ReadAllLines(log.LogPath);
        Assert.InRange(lines.Count(l => l.Contains("same failure")), 21, 25);
        Assert.Contains(lines, l => l.Contains("(repeated 99980 times)"));
    }

    [Fact] // TR-3
    public void Home_and_user_names_never_reach_the_log_or_the_session_file()
    {
        using var log = NewLog(Sub("logs"));
        log.Info("save", "wrote /home/alice/.local/share/godot/saves/a.sav for alice", "path=C:\\Users\\alice\\x");
        log.Flush();
        string all = File.ReadAllText(log.LogPath);
        Assert.DoesNotContain("alice", all); Assert.DoesNotContain("/home/", all);
        Assert.Contains("~/.local/share/godot/saves/a.sav", all);
        Assert.Equal("opened ~/a.txt and ~\\b", Alice.Redact("opened /home/alice/a.txt and C:\\Users\\alice\\b"));
    }

    // ------------------------------------------------------------------ consent

    [Fact] // TR-6
    public void Consent_is_off_by_default_and_no_report_is_written_without_it()
    {
        var profile = Sub("profile"); var reports = Path.Combine(_root, "reports");
        var consent = new ConsentStore(profile, reports);
        Assert.False(consent.Current.Accepted);
        Assert.False(consent.IsGranted);
        var builder = new ReportBuilder(consent, Alice, reports);
        Assert.Throws<ConsentRequiredException>(() => builder.Write(new ReportRequest()));
        Assert.False(Directory.Exists(reports) && Directory.GetFiles(reports).Length > 0);
    }

    [Fact]
    public void Consent_round_trips_a_changed_text_version_asks_again_and_withdraw_deletes_reports()
    {
        var profile = Sub("profile"); var reports = Sub("reports");
        var consent = new ConsentStore(profile, reports);
        consent.Accept(includeSave: false);
        Assert.True(consent.IsGranted); Assert.False(consent.Current.IncludeSave); Assert.True(consent.Current.IncludeScreenshot);
        File.WriteAllText(Path.Combine(profile, "consent.json"), File.ReadAllText(Path.Combine(profile, "consent.json")).Replace("\"version\": " + ConsentText.Version, "\"version\": 0"));
        Assert.False(consent.IsGranted);
        consent.Accept();
        File.WriteAllText(Path.Combine(reports, "report-x.zip"), "z");
        consent.Withdraw();
        Assert.False(consent.IsGranted); Assert.False(Directory.Exists(reports));
    }

    // ------------------------------------------------------------------ the crash zip

    private (ReportBuilder Builder, ReportRequest Request, string Log) MakeCrashRig(bool consented, byte[]? shot = null)
    {
        var profile = Sub("profile"); var reports = Path.Combine(_root, "reports"); var logs = Sub("logs");
        var consent = new ConsentStore(profile, reports);
        if (consented) consent.Accept();
        using (var log = NewLog(logs)) { for (int i = 0; i < 6000; i++) log.Info("sim", $"line {i} at /home/alice/x"); log.Flush(); }
        string logFile = Directory.GetFiles(logs, "game-*.log").Single();
        string autosave = Path.Combine(_root, "autosave_1.sav"); File.WriteAllBytes(autosave, [1, 2, 3, 4, 5]);
        var cmds = new CommandLog(); cmds.Append(ReplayCommand.MoveTo(3, new Cell3(1, 2, 0)));
        var req = new ReportRequest
        {
            Kind = ReportKind.Crash, CrashText = "System.InvalidOperationException: boom at /home/alice/Game/x.cs\n   at Foo.Bar()",
            LogPath = logFile, AutosavePath = autosave, Screenshot = shot, ReplayTrace = cmds.ToBytes(), HashesCsv = new StateHashTrail().ToCsv(),
            RecentCommands = cmds.Recent(), WhatHappened = "I was at /home/alice/saves when alice's game stopped",
            Session = new SessionHeader { Build = new BuildInfo { Version = "0.3.4-tester", Git = "5f3a9c2", ContentHash = "e9b1", Mods = [new ModRef("base", "0.3.4", "h")] }, System = SystemInfo.Detect() },
            ContentManifestJson = "{\"hash\":\"e9b1\",\"mods\":[\"base\"]}", SettingsJson = "{\"ui_scale\":0}",
        };
        return (new ReportBuilder(consent, Alice, reports), req, logs);
    }

    [Fact] // TR-4 / TR-3
    public void The_crash_zip_has_the_required_files_and_is_redacted()
    {
        var (builder, req, _) = MakeCrashRig(true, shot: [0x89, 0x50, 0x4E, 0x47]);
        var res = builder.Write(req);
        using var zip = ZipFile.OpenRead(res.Path);
        var names = zip.Entries.Select(e => e.FullName).ToHashSet();
        foreach (var n in new[] { "report.json", "crash.txt", "log_tail.txt", "session.json", "system.json", "content_manifest.json", "settings.json", "autosave.bin", "replay.trace", "hashes.csv", "screenshot.png", "README.txt" })
            Assert.Contains(n, names);
        Assert.DoesNotContain("bug.save", names);
        foreach (var e in zip.Entries.Where(e => e.Name is not ("autosave.bin" or "screenshot.png" or "replay.trace")))
        {
            using var r = new StreamReader(e.Open()); string t = r.ReadToEnd();
            Assert.DoesNotContain("alice", t); Assert.DoesNotContain("/home/", t);
        }
        using (var r = new StreamReader(zip.GetEntry("log_tail.txt")!.Open())) Assert.Equal(5000, r.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        using (var r = new StreamReader(zip.GetEntry("crash.txt")!.Open())) { var t = r.ReadToEnd(); Assert.Contains("last commands:", t); Assert.Contains("move_to 1,2,0", t); }
        var rep = System.Text.Json.Nodes.JsonNode.Parse(new StreamReader(zip.GetEntry("report.json")!.Open()).ReadToEnd())!;
        Assert.Equal("crash", (string?)rep["kind"]); Assert.Equal(false, (bool?)rep["sent"]);
        Assert.Contains(rep["files"]!.AsArray(), f => (string?)f!["name"] == "autosave.bin" && (long?)f["bytes"] == 5 && ((string?)f["sha256"])!.Length == 64);
        Assert.Equal(ConsentText.Version, (int?)rep["consent"]!["version"]);
    }

    [Fact]
    public void Switching_screenshot_log_and_save_off_leaves_them_out_and_oversize_files_are_dropped_with_a_note()
    {
        var (builder, req, _) = MakeCrashRig(true, shot: [1, 2, 3]);
        var slim = new ReportRequest { Kind = req.Kind, CrashText = req.CrashText, LogPath = req.LogPath, AutosavePath = req.AutosavePath, Screenshot = req.Screenshot, Session = req.Session, IncludeLog = false, IncludeScreenshot = false, IncludeAutosave = false };
        using (var zip = ZipFile.OpenRead(builder.Write(slim).Path))
        { var n = zip.Entries.Select(e => e.Name).ToList(); Assert.DoesNotContain("log_tail.txt", n); Assert.DoesNotContain("screenshot.png", n); Assert.DoesNotContain("autosave.bin", n); Assert.Contains("crash.txt", n); }
        var big = new ReportRequest { Kind = req.Kind, CrashText = "x", Screenshot = new byte[(int)ReportBuilder.ScreenshotMaxBytes + 1], Session = req.Session };
        var res = builder.Write(big);
        Assert.Contains(res.Notes, n => n.StartsWith("screenshot.png left out"));
    }

    [Fact]
    public void A_crash_without_consent_writes_plain_files_in_the_log_folder_and_no_zip()
    {
        var (builder, req, logs) = MakeCrashRig(false);
        var (path, zipped) = builder.WriteCrash(req, logs);
        Assert.False(zipped); Assert.Equal(logs, path);
        Assert.Single(Directory.GetFiles(logs, "crash-*-log_tail.txt")); Assert.Single(Directory.GetFiles(logs, "crash-*.txt").Where(f => !f.EndsWith("-log_tail.txt")));
        Assert.False(Directory.Exists(builder.ReportsDir));
        Assert.DoesNotContain("alice", File.ReadAllText(Directory.GetFiles(logs, "crash-*.txt").First()));
    }

    [Fact]
    public void A_consented_crash_writes_a_zip_and_never_throws()
    {
        var (builder, req, logs) = MakeCrashRig(true);
        var (path, zipped) = builder.WriteCrash(req, logs);
        Assert.True(zipped); Assert.True(File.Exists(path)); Assert.EndsWith(".zip", path);
        var archive = ReportArchive.Open(path);
        Assert.Equal("crash", archive.Kind); Assert.NotNull(archive.Autosave); Assert.Contains("boom", archive.CrashText);
        Assert.Null(archive.MismatchWarning(new BuildInfo { Git = "5f3a9c2", ContentHash = "e9b1" }));
        Assert.Contains("Build differs", archive.MismatchWarning(new BuildInfo { Git = "other", ContentHash = "e9b1" }));
        // a reports folder that cannot be created must not throw either
        var blocked = new ReportBuilder(new ConsentStore(Sub("p2"), null), Alice, Path.Combine(_root, "autosave_1.sav", "nope"));
        var bad = blocked.WriteCrash(req, logs); Assert.False(bad.IsZip);
    }

    // ------------------------------------------------------------------ the command log and replay

    private static CommandLog Sample()
    {
        var l = new CommandLog();
        l.Rebase(1200, [ReplayCommand.Input(1200, new MoveInput(1, 0, true)), ReplayCommand.Speed(1200, SimSpeed.X2), ReplayCommand.Torch(1200, true)]);
        l.Append(ReplayCommand.Input(1230, new MoveInput(0, -1)));
        l.Append(ReplayCommand.MoveTo(1300, new Cell3(40, 7, 1)));
        l.Append(ReplayCommand.UsePortal(1301)); l.Append(ReplayCommand.ClearPath(1400));
        l.Append(ReplayCommand.Speed(1500, SimSpeed.Paused));
        l.NoteTick(1900);
        return l;
    }

    [Fact]
    public void The_command_log_round_trips_and_rebase_keeps_the_standing_state()
    {
        var a = Sample();
        var b = CommandLog.FromBytes(a.ToBytes());
        Assert.Equal(1200, b.BaseTick); Assert.Equal(1900, b.EndTick);
        Assert.Equal(a.Items, b.Items);
        Assert.Equal(3, a.Items.Count(c => c.Tick == 1200));
        Assert.Throws<InvalidDataException>(() => CommandLog.FromBytes([1, 2, 3, 4, 5, 6]));
        var t = new StateHashTrail(); t.Add(new HashRecord(400, "live", 0xDEADBEEFCAFEF00DUL)); t.Add(new HashRecord(800, "live", 1));
        Assert.Equal(t.Items, StateHashTrail.FromCsv(t.ToCsv()).Items);
    }

    /// <summary>40 input changes, 30 other commands, speed and torch changes over 12,000 ticks (TR-10), then returns the recorder.</summary>
    private static SessionRecorder RecordSession(int ticks)
    {
        var rec = new SessionRecorder(Field());
        var rng = new Random(7);
        for (int t = 0; t < ticks; t++)
        {
            if (t % 300 == 0) rec.SetInput(new MoveInput(rng.Next(-1, 2), rng.Next(-1, 2), rng.Next(3) == 0, rng.Next(4) == 0));
            if (t % 400 == 111) rec.CommandMoveTo(new Cell3(rng.Next(2, 28), rng.Next(2, 28), 0));
            if (t % 1000 == 500) rec.SetSpeed(rng.Next(3) switch { 0 => SimSpeed.X2, 1 => SimSpeed.X4, _ => SimSpeed.X1 });
            if (t % 777 == 5) rec.ToggleTorch();
            if (t % 1500 == 900) rec.ClearPath();
            rec.Step();
        }
        return rec;
    }

    [Fact] // TR-10, TR-14
    public void Replaying_a_recorded_session_reproduces_every_record_hash_and_the_final_state()
    {
        var rec = RecordSession(12_000);
        Assert.Equal(31, rec.Hashes.Count); // T0 + 30 live hashes
        var (trace, csv) = rec.Export();
        Assert.True(trace.Length < 40 * 1024, "trace " + trace.Length);

        var fresh = Field();
        var res = ReplayRunner.Run(fresh, CommandLog.FromBytes(trace), StateHashTrail.FromCsv(csv));
        Assert.True(res.Matched, res.Describe());
        Assert.True(res.Completed);
        Assert.Equal(12_000, res.StepsRun);
        Assert.Equal(rec.Hashes.Items, res.Replayed.Items);
        Assert.Equal(LiveStateHash.Compute(rec.World), LiveStateHash.Compute(fresh));
        Assert.Null(StateHashTrail.FirstMismatch(rec.Hashes, res.Replayed));
        Assert.True(rec.World.Snapshot.Hero.X != Fixed.FromInt(5) || rec.World.Snapshot.Hero.Y != Fixed.FromInt(5), "the session did move the hero");
    }

    [Fact] // TR-12
    public void A_changed_command_is_reported_at_the_first_mismatching_hash()
    {
        var rec = new SessionRecorder(Field());
        rec.SetInput(MoveInput.None);
        for (int t = 0; t < 1600; t++) { if (t == 10) rec.CommandMoveTo(new Cell3(20, 20, 0)); rec.Step(); }
        var log = CommandLog.FromBytes(rec.Export().Trace);
        var items = log.Items.ToList();
        int i = items.FindIndex(c => c.Kind == ReplayKind.MoveTo);
        var tampered = new CommandLog();
        tampered.Rebase(log.BaseTick, []);
        for (int k = 0; k < items.Count; k++) tampered.Append(k == i ? items[k] with { A = 22, B = 18 } : items[k]);
        tampered.NoteTick(log.EndTick);
        var res = ReplayRunner.Run(Field(), tampered, rec.Hashes);
        Assert.NotNull(res.Divergence);
        Assert.False(res.Matched);
        Assert.True(res.Divergence!.Tick >= items[i].Tick);
        Assert.Equal("live", res.Divergence.Kind);
        Assert.Contains("first mismatch", res.Describe());
        Assert.True(res.EndTick < log.EndTick, "the replay stops at the divergence");
        Assert.True(ReplayRunner.Run(Field(), log, rec.Hashes).Matched); // the untouched trace still matches
    }

    [Fact]
    public void Replay_can_stop_at_a_tick_and_stops_at_an_exception_with_its_stack()
    {
        var rec = RecordSession(1000);
        var log = CommandLog.FromBytes(rec.Export().Trace);
        var until = ReplayRunner.Run(Field(), log, rec.Hashes, new ReplayOptions { UntilTick = 450 });
        Assert.Equal(450, until.EndTick); Assert.False(until.Completed);
        // the world blows up mid-replay (a command handler throws): the result carries the exception and the tick
        var boom = ReplayRunner.Run(Field(), log, null, new ReplayOptions { AfterStep = t => t < 300 ? true : throw new InvalidOperationException("recurred") });
        Assert.NotNull(boom.Exception); Assert.Equal(300, boom.EndTick); Assert.Contains("recurred", boom.Describe());
    }

    [Fact]
    public void A_recorder_rebases_at_an_autosave_and_a_new_trace_starts_there_with_the_standing_input()
    {
        var rec = new SessionRecorder(Field());
        rec.SetInput(new MoveInput(1, 0)); rec.SetSpeed(SimSpeed.X4);
        for (int i = 0; i < 450; i++) rec.Step();
        rec.Rebase();
        Assert.Equal(450, rec.Commands.BaseTick);
        var items = rec.Commands.Items;
        Assert.Contains(items, c => c.Kind == ReplayKind.Input && c.A == 1 && c.Tick == 450);
        Assert.Contains(items, c => c.Kind == ReplayKind.Speed && c.A == (int)SimSpeed.X4);
        Assert.Single(rec.Hashes.Items);
        rec.CommandMoveTo(new Cell3(9, 9, 0));
        Assert.Contains("move_to 9,9,0", rec.Commands.Recent());
    }

    [Fact]
    public void The_diagnostics_code_has_no_network_references()
    {
        // TR-8 for the sim assembly: no System.Net.* type is referenced by Ravensguard.Sim
        var refs = typeof(LogService).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("System.Net.Http", refs); Assert.DoesNotContain("System.Net.Sockets", refs); Assert.DoesNotContain("System.Net.WebSockets", refs);
    }
}
