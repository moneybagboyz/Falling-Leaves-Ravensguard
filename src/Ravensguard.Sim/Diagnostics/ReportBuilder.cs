using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ravensguard.Sim.Diagnostics;

public enum ReportKind { Bug, Crash, Hang, Feedback }

/// <summary>Everything a report is made of. Files are given as paths or bytes; nothing is read until <see cref="ReportBuilder.Write"/>.</summary>
public sealed class ReportRequest
{
    public ReportKind Kind { get; init; } = ReportKind.Bug;
    public string Category { get; init; } = "Bug";
    public string WhatHappened { get; init; } = "";
    public string WhatExpected { get; init; } = "";
    /// <summary>yes, partly, no or empty (spec 4.3: "Did you understand why that happened?").</summary>
    public string Understood { get; init; } = "";
    public SessionHeader Session { get; init; } = new();
    /// <summary>Where the tester was: screen or context, selected entity, region, game time, tick, speed.</summary>
    public Dictionary<string, string> Where { get; init; } = [];
    public string SettingsJson { get; init; } = "{}";
    public string ContentManifestJson { get; init; } = "{}";
    public string? LogPath { get; init; }
    public string? CrashText { get; init; }
    /// <summary>The newest autosave (T0).</summary>
    public string? AutosavePath { get; init; }
    /// <summary>A full save of the moment of the report (only when the tester ticked it).</summary>
    public string? BugSavePath { get; init; }
    public byte[]? Screenshot { get; init; }
    public byte[]? ReplayTrace { get; init; }
    public string? HashesCsv { get; init; }
    public string? RecentCommands { get; init; }
    /// <summary>Optional pieces the tester may leave out (consent defaults).</summary>
    public bool IncludeLog { get; init; } = true;
    public bool IncludeScreenshot { get; init; } = true;
    public bool IncludeAutosave { get; init; } = true;
    /// <summary>The live tick range the trace covers, for the replay tool.</summary>
    public long ReplayBaseTick { get; init; }
    public long ReplayEndTick { get; init; }
    public string? PreviousCrashId { get; init; }
    /// <summary>Extra named text files (e.g. <c>playtest_summary.json</c>).</summary>
    public Dictionary<string, string> ExtraFiles { get; init; } = [];
}

public sealed record ReportFile(string Name, long Bytes, string Sha256, string? Note = null);

public sealed record ReportResult(string Path, string Id, IReadOnlyList<ReportFile> Files, IReadOnlyList<string> Notes);

/// <summary>Thrown when a report is asked for and the tester has not consented.</summary>
public sealed class ConsentRequiredException() : InvalidOperationException("No consent for report files: nothing was written.");

/// <summary>
/// Writes the report zip (testers spec 3.4): local, redacted, size capped, never sent. Without consent it writes nothing and throws
/// <see cref="ConsentRequiredException"/>; the crash handler then points at the log instead.
/// </summary>
public sealed class ReportBuilder(ConsentStore consent, Redactor redactor, string reportsDir)
{
    public const long ReportMaxBytes = 250L * 1024 * 1024;
    public const long AutosaveMaxBytes = 200L * 1024 * 1024;
    public const long ScreenshotMaxBytes = 2L * 1024 * 1024;
    public const long LogTailMaxBytes = 5L * 1024 * 1024;
    public const long ReplayTraceMaxBytes = 5L * 1024 * 1024;
    public const int LogTailLines = 5000;
    public const int ReportJsonMaxBytes = 100 * 1024;
    public const int CrashTextMaxBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public string ReportsDir => reportsDir;

    /// <summary>The files a report would hold with their sizes (what the F8 screen lists live). Names and byte counts only.</summary>
    public List<(string Name, long Bytes)> Preview(ReportRequest r)
    {
        var l = new List<(string, long)>();
        foreach (var f in Collect(r, new List<string>())) l.Add((f.Name, f.Data?.LongLength ?? new FileInfo(f.File!).Length));
        return l;
    }

    private sealed record Part(string Name, byte[]? Data = null, string? File = null);

    private IEnumerable<Part> Collect(ReportRequest r, List<string> notes)
    {
        string T(string s) => redactor.Redact(s);
        byte[] U(string s) => Encoding.UTF8.GetBytes(T(s));
        yield return new Part("session.json", U(JsonSerializer.Serialize(r.Session, SessionHeader.Json)));
        yield return new Part("system.json", U(JsonSerializer.Serialize(r.Session.System, SessionHeader.Json)));
        yield return new Part("settings.json", U(r.SettingsJson));
        yield return new Part("content_manifest.json", U(r.ContentManifestJson));
        if (r.IncludeLog && r.LogPath != null && File.Exists(r.LogPath))
        {
            var tail = Encoding.UTF8.GetBytes(T(LogService.TailOf(r.LogPath, LogTailLines)));
            if (tail.Length > LogTailMaxBytes) tail = tail[^(int)LogTailMaxBytes..];
            yield return new Part("log_tail.txt", tail);
        }
        if (r.IncludeScreenshot && r.Screenshot is { Length: > 0 } shot)
        {
            if (shot.Length <= ScreenshotMaxBytes) yield return new Part("screenshot.png", shot);
            else notes.Add($"screenshot.png left out: {shot.Length / 1024} KB is over the {ScreenshotMaxBytes / 1024} KB cap");
        }
        if (r.CrashText != null) yield return new Part("crash.txt", Cap(U(r.CrashText + (r.RecentCommands is { Length: > 0 } rc ? "\n\nlast commands:\n" + rc : "")), CrashTextMaxBytes));
        if (r.ReplayTrace is { Length: > 0 } tr)
        {
            if (tr.Length <= ReplayTraceMaxBytes) yield return new Part("replay.trace", tr);
            else notes.Add("replay.trace left out: over the 5 MB cap");
        }
        if (r.HashesCsv != null) yield return new Part("hashes.csv", Cap(Encoding.UTF8.GetBytes(r.HashesCsv), 100 * 1024));
        foreach (var kv in r.ExtraFiles) yield return new Part(kv.Key, U(kv.Value));
        if (r.IncludeAutosave && r.AutosavePath != null && File.Exists(r.AutosavePath))
        {
            long n = new FileInfo(r.AutosavePath).Length;
            if (n <= AutosaveMaxBytes) yield return new Part("autosave.bin", null, r.AutosavePath);
            else notes.Add($"autosave.bin left out: {n / (1024 * 1024)} MB is over the {AutosaveMaxBytes / (1024 * 1024)} MB cap");
        }
        if (r.BugSavePath != null && File.Exists(r.BugSavePath)) yield return new Part("bug.save", null, r.BugSavePath);
    }

    private static byte[] Cap(byte[] b, long max) => b.Length <= max ? b : b[..(int)max];

    private static string Sha(byte[] d) => Convert.ToHexString(SHA256.HashData(d)).ToLowerInvariant();
    private static string Sha(string path) { using var fs = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant(); }

    /// <summary>Writes <c>report-&lt;stamp&gt;-&lt;id&gt;.zip</c> into the reports folder. Throws <see cref="ConsentRequiredException"/> without consent.</summary>
    public ReportResult Write(ReportRequest r, DateTime? now = null)
    {
        if (!consent.IsGranted) throw new ConsentRequiredException();
        return WriteCore(r, now ?? DateTime.Now);
    }

    private ReportResult WriteCore(ReportRequest r, DateTime now)
    {
        Directory.CreateDirectory(reportsDir);
        string id = Guid.NewGuid().ToString("N")[..6];
        string path = Path.Combine(reportsDir, $"report-{now:yyyyMMdd-HHmmss}-{id}.zip");
        var notes = new List<string>();
        var parts = Collect(r, notes).ToList();

        // total cap: bug.save goes first, then autosave.bin (spec 3.4)
        long Total() => parts.Sum(p => p.Data?.LongLength ?? new FileInfo(p.File!).Length);
        foreach (var drop in new[] { "bug.save", "autosave.bin" })
        {
            if (Total() <= ReportMaxBytes) break;
            if (parts.RemoveAll(p => p.Name == drop) > 0) notes.Add($"{drop} left out: the report would be over {ReportMaxBytes / (1024 * 1024)} MB");
        }

        var files = new List<ReportFile>();
        foreach (var p in parts)
            files.Add(new ReportFile(p.Name, p.Data?.LongLength ?? new FileInfo(p.File!).Length, p.Data != null ? Sha(p.Data) : Sha(p.File!)));

        string reportJson = BuildReportJson(r, id, now, files, notes);
        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            void Put(string name, byte[] data)
            {
                var e = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var s = e.Open(); s.Write(data);
            }
            Put("report.json", Encoding.UTF8.GetBytes(reportJson));
            Put("README.txt", Encoding.UTF8.GetBytes(Readme(files)));
            foreach (var p in parts)
            {
                if (p.Data != null) { Put(p.Name, p.Data); continue; }
                var e = zip.CreateEntry(p.Name, CompressionLevel.Fastest);
                using var s = e.Open(); using var src = new FileStream(p.File!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                src.CopyTo(s);
            }
        }
        File.Move(tmp, path, true);
        return new ReportResult(path, id, files, notes);
    }

    private string BuildReportJson(ReportRequest r, string id, DateTime now, List<ReportFile> files, List<string> notes)
    {
        string T(string s) => redactor.Redact(s);
        var c = consent.Current;
        var o = new JsonObject
        {
            ["id"] = id,
            ["kind"] = r.Kind.ToString().ToLowerInvariant(),
            ["category"] = r.Category,
            ["created"] = now.ToString("s"),
            ["what_happened"] = T(Trim(r.WhatHappened, 2000)),
            ["what_expected"] = T(Trim(r.WhatExpected, 2000)),
            ["understood"] = r.Understood,
            ["build"] = JsonSerializer.SerializeToNode(r.Session.Build, SessionHeader.Json),
            ["system"] = JsonSerializer.SerializeToNode(r.Session.System, SessionHeader.Json),
            ["game"] = JsonSerializer.SerializeToNode(r.Session.Game, SessionHeader.Json),
            ["where"] = new JsonObject(r.Where.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)JsonValue.Create(T(kv.Value))))),
            ["replay"] = new JsonObject { ["base_tick"] = r.ReplayBaseTick, ["end_tick"] = r.ReplayEndTick, ["has_trace"] = files.Any(f => f.Name == "replay.trace"), ["has_autosave"] = files.Any(f => f.Name == "autosave.bin") },
            ["files"] = new JsonArray(files.Select(f => (JsonNode)new JsonObject { ["name"] = f.Name, ["bytes"] = f.Bytes, ["sha256"] = f.Sha256 }).ToArray()),
            ["notes"] = new JsonArray(notes.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray()),
            ["consent"] = new JsonObject { ["version"] = c.Version, ["time"] = c.AcceptedAt },
            ["sent"] = false,
        };
        if (r.PreviousCrashId != null) o["previous_crash_id"] = r.PreviousCrashId;
        string s = o.ToJsonString(Json);
        return Encoding.UTF8.GetByteCount(s) > ReportJsonMaxBytes ? s[..ReportJsonMaxBytes] : s;
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s[..n];

    private static string Readme(List<ReportFile> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Falling Leaves: Ravensguard - problem report");
        sb.AppendLine();
        sb.AppendLine(ConsentText.Statement);
        sb.AppendLine("This zip was made on your computer and stays there until you choose to send it. To delete it, delete the file (Settings > Privacy > Delete all reports removes all of them).");
        sb.AppendLine();
        foreach (var f in files) sb.AppendLine($"{f.Name} ({f.Bytes} bytes)");
        sb.AppendLine("report.json: the summary of everything below, with a SHA-256 for each file.");
        return sb.ToString();
    }

    /// <summary>
    /// The crash path: never throws. With consent it writes the full zip; without it (or when the zip fails) it writes only <c>crash.txt</c> and the log tail as plain files
    /// into the log folder. Returns the path to show the tester and whether a zip was made.
    /// </summary>
    public (string Path, bool IsZip) WriteCrash(ReportRequest r, string logDir)
    {
        try { return (Write(r).Path, true); }
        catch (Exception) { /* consent missing or the zip failed: fall through to plain files */ }
        try
        {
            Directory.CreateDirectory(logDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string txt = Path.Combine(logDir, $"crash-{stamp}.txt");
            File.WriteAllText(txt, redactor.Redact(r.CrashText ?? "crash"));
            if (r.LogPath != null) File.WriteAllText(Path.Combine(logDir, $"crash-{stamp}-log_tail.txt"), redactor.Redact(LogService.TailOf(r.LogPath, LogTailLines)));
            return (logDir, false);
        }
        catch (Exception) { return (logDir, false); }
    }
}

/// <summary>A report zip opened for replay.</summary>
public sealed class ReportArchive
{
    public required string ReportJson { get; init; }
    public byte[]? Autosave { get; init; }
    public byte[]? Trace { get; init; }
    public string? HashesCsv { get; init; }
    public required BuildInfo Build { get; init; }
    public string? CrashText { get; init; }
    public string Kind { get; init; } = "bug";

    public static ReportArchive Open(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        byte[]? Bytes(string n) { var e = zip.GetEntry(n); if (e == null) return null; using var s = e.Open(); using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray(); }
        string json = Encoding.UTF8.GetString(Bytes("report.json") ?? throw new InvalidDataException("report.json is missing: not a report zip"));
        var node = JsonNode.Parse(json)!;
        var build = node["build"]?.Deserialize<BuildInfo>(SessionHeader.Json) ?? new BuildInfo();
        var crash = Bytes("crash.txt");
        return new ReportArchive
        {
            ReportJson = json, Build = build, Kind = node["kind"]?.GetValue<string>() ?? "bug",
            Autosave = Bytes("autosave.bin"), Trace = Bytes("replay.trace"), HashesCsv = Bytes("hashes.csv") is { } h ? Encoding.UTF8.GetString(h) : null,
            CrashText = crash == null ? null : Encoding.UTF8.GetString(crash),
        };
    }

    /// <summary>Null when the report was made by this build and content; otherwise the warning to show ("Build differs: results may differ").</summary>
    public string? MismatchWarning(BuildInfo current)
    {
        var diffs = new List<string>();
        if (Build.Git.Length > 0 && current.Git.Length > 0 && Build.Git != current.Git) diffs.Add($"build {Build.Git} vs {current.Git}");
        if (Build.ContentHash.Length > 0 && current.ContentHash.Length > 0 && Build.ContentHash != current.ContentHash) diffs.Add("content hash differs");
        return diffs.Count == 0 ? null : "Build differs: results may differ (" + string.Join("; ", diffs) + ")";
    }
}
