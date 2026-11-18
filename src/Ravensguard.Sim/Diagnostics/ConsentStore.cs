using System.Text.Json;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>What the tester agreed to (testers spec 3.5). Absent file = no consent.</summary>
public sealed record ConsentState(int Version, string AcceptedAt, bool IncludeSave, bool IncludeScreenshot, bool IncludeLog)
{
    public static readonly ConsentState None = new(0, "", false, false, false);
    public bool Accepted => Version > 0;
}

/// <summary>The consent text. Bump <see cref="Version"/> whenever the list below changes: the consent screen then asks again.</summary>
public static class ConsentText
{
    public const int Version = 1;

    /// <summary>The exact list shown on the consent screen: what a report zip contains.</summary>
    public static readonly string[] Contents =
    [
        "report.json: what you typed, the build, your system (OS, CPU, RAM, GPU, screen) and where you were in the game",
        "screenshot.png: the game picture just before the report dialog opened (optional)",
        "log_tail.txt: the last 5,000 lines of the game log (optional)",
        "session.json, system.json, settings.json, content_manifest.json: small files about this session",
        "autosave.bin: the newest autosave, so the problem can be replayed (optional)",
        "replay.trace and hashes.csv: your recent commands as game ticks (never key codes) and state hashes",
        "bug.save: a full save of right now (only if you tick it)",
        "crash.txt: the error and its stack trace (crash reports only)",
    ];

    public const string Statement = "Reports are written to your own disk only. The game never uploads or sends anything. You decide who receives a report. Home folder and user names are replaced by ~.";
}

/// <summary>
/// Reads and writes <c>profile/consent.json</c>. <b>Off by default</b>: without a current-version file no report zip is ever created. Logs are always local and not governed by this.
/// </summary>
public sealed class ConsentStore(string profileDir, string? reportsDir = null)
{
    private string FilePath => Path.Combine(profileDir, "consent.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>The stored consent, or <see cref="ConsentState.None"/> when absent or unreadable.</summary>
    public ConsentState Current
    {
        get
        {
            try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<ConsentState>(File.ReadAllText(FilePath), Json) ?? ConsentState.None : ConsentState.None; }
            catch (Exception) { return ConsentState.None; }
        }
    }

    /// <summary>True only when consent exists for the current text version (a changed text asks again).</summary>
    public bool IsGranted => Current.Version == ConsentText.Version;

    public ConsentState Accept(bool includeSave = true, bool includeScreenshot = true, bool includeLog = true, DateTime? now = null)
    {
        Directory.CreateDirectory(profileDir);
        var s = new ConsentState(ConsentText.Version, (now ?? DateTime.UtcNow).ToString("s"), includeSave, includeScreenshot, includeLog);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(s, Json));
        return s;
    }

    /// <summary>Removes the consent and, when asked, every report zip.</summary>
    public void Withdraw(bool deleteReports = true)
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch (IOException) { }
        if (deleteReports && reportsDir != null && Directory.Exists(reportsDir)) { try { Directory.Delete(reportsDir, true); } catch (IOException) { } }
    }
}
