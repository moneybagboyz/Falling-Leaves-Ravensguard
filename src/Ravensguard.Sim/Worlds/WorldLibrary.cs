using System.Text;
using System.Text.Json;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.Worlds;

public enum WorldGenPhase { Continent, History, Done }

/// <summary>
/// Progress of world creation or loading. During <see cref="WorldGenPhase.History"/> every chronicle entry arrives as it is written
/// (<see cref="Entry"/>), so a UI can scroll the chronicle while history unfolds. <see cref="Continent"/> arrives with the first history event (the finished terrain) and
/// <see cref="Owner"/> (realm per cell, 255 none) every ten years. Fraction runs 0..1 over the whole job.
/// </summary>
public sealed record WorldGenProgress(WorldGenPhase Phase, string Stage, int Step, int Steps, int Year, int Years, ChronicleEntry? Entry,
    Continent? Continent = null, byte[]? Owner = null)
{
    public double Fraction => Phase switch
    {
        WorldGenPhase.Continent => 0.15 * Step / Math.Max(1, Steps),
        WorldGenPhase.History => 0.15 + 0.85 * Year / Math.Max(1, Years),
        _ => 1.0,
    };
}

/// <summary>Counts shown on the world library page; stored in world.json.</summary>
public sealed record WorldSummary(int Years, int ChronicleEntries, int RealmsTotal, int RealmsAlive, int Settlements, int Ruins, int Wars, int Battlefields, long StartPopulation, long EndPopulation, int LandPct);

/// <summary>On-disk world.json. Everything is derived from the settings, so the world is regenerated (and hash-checked) rather than stored whole.</summary>
public sealed class WorldFile
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public ulong Seed { get; set; }
    /// <summary>Text supplied by the caller (the sim never reads the wall clock).</summary>
    public string Created { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string ContinentHash { get; set; } = "";
    public string HistoryHash { get; set; } = "";
    public ContinentSettings Continent { get; set; } = new();
    public HistorySettings History { get; set; } = new();
    public WorldSummary Summary { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>A world in the library: its folder id, the file contents and the saved runs.</summary>
public sealed record WorldInfo(string Id, WorldFile File, IReadOnlyList<string> Runs)
{
    public string Name => File.Name;
    public ulong Seed => File.Seed;
}

/// <summary>A library world with its generated layers.</summary>
public sealed record LoadedWorld(WorldInfo Info, Continent Continent, WorldHistory History);

public sealed class WorldLibraryException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The world library (plan §1A "a world is saved once and played many times"): <c>root/&lt;id&gt;/world.json</c> plus
/// <c>root/&lt;id&gt;/saves/*.sav</c>, one save per character or run. Creating a world runs continent and history once and records their hashes;
/// loading regenerates them from the settings exactly once and verifies the hashes.
/// </summary>
public sealed class WorldLibrary
{
    public const string WorldFileName = "world.json", SavesFolder = "saves";
    public const int MaxNameLength = 40;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Root { get; }
    public WorldLibrary(string root) { Root = root; }

    public string FolderOf(string id) => Path.Combine(Root, id);
    public string RunsDirectory(string id) => Path.Combine(FolderOf(id), SavesFolder);

    /// <summary>All worlds, newest first by the caller-supplied created text then by name. Broken folders are skipped.</summary>
    public List<WorldInfo> List()
    {
        var res = new List<WorldInfo>();
        if (!Directory.Exists(Root)) return res;
        foreach (var dir in Directory.GetDirectories(Root).OrderBy(d => d, StringComparer.Ordinal))
        {
            var id = Path.GetFileName(dir);
            try { if (TryRead(id) is { } w) res.Add(w); }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { /* unreadable folder: not listed */ }
        }
        return res.OrderByDescending(w => w.File.Created, StringComparer.Ordinal).ThenBy(w => w.Name, StringComparer.Ordinal).ThenBy(w => w.Id, StringComparer.Ordinal).ToList();
    }

    public WorldInfo? TryRead(string id)
    {
        var path = Path.Combine(FolderOf(id), WorldFileName);
        if (!File.Exists(path)) return null;
        var f = JsonSerializer.Deserialize<WorldFile>(File.ReadAllText(path), Json);
        return f == null ? null : new WorldInfo(id, f, ListRuns(id));
    }

    public IReadOnlyList<string> ListRuns(string id)
    {
        var dir = RunsDirectory(id);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*" + SaveWriter.Extension).Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    public void Delete(string id)
    {
        if (id.Length == 0 || id.Contains('/') || id.Contains('\\') || id.Contains("..")) throw new WorldLibraryException("Bad world id.");
        var dir = FolderOf(id);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    public static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "world" : s.Length > 32 ? s[..32].Trim('-') : s;
    }

    /// <summary>Runs continent and history (call from a worker thread; <paramref name="progress"/> fires on that thread), then writes the world folder.</summary>
    public LoadedWorld Create(string name, ContinentSettings cs, HistorySettings hs, string contentHash, string createdText,
        Action<WorldGenProgress>? progress = null, CancellationToken ct = default)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Length > MaxNameLength) throw new WorldLibraryException($"A world name needs 1 to {MaxNameLength} characters.");
        hs.Seed = cs.Seed;
        var (continent, history) = Generate(cs, hs, progress, ct);
        var id = FreeId(Slug(name) + "-" + cs.Seed.ToString("x"));
        var file = new WorldFile
        {
            Name = name, Seed = cs.Seed, Created = createdText, ContentHash = contentHash,
            ContinentHash = continent.ComputeHash(), HistoryHash = history.ComputeHash(),
            Continent = cs, History = hs, Summary = Summarise(continent, history),
        };
        Directory.CreateDirectory(RunsDirectory(id));
        var path = Path.Combine(FolderOf(id), WorldFileName);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(file, Json));
        File.Move(path + ".tmp", path, true);
        progress?.Invoke(new WorldGenProgress(WorldGenPhase.Done, "done", 1, 1, hs.Years, hs.Years, null));
        return new LoadedWorld(new WorldInfo(id, file, []), continent, history);
    }

    /// <summary>Regenerates the continent and history of a library world once and verifies them against the stored hashes. A world made with other content opens when they still verify (<see cref="LoadReport.ForWorld"/> tells the player first); it is refused, naming the content, when they do not.</summary>
    public LoadedWorld Load(string id, string? currentContentHash = null, Action<WorldGenProgress>? progress = null, CancellationToken ct = default)
    {
        var info = TryRead(id) ?? throw new WorldLibraryException($"World '{id}' is not in the library.");
        var f = info.File;
        var (continent, history) = Generate(f.Continent, f.History, progress, ct);
        if (continent.ComputeHash() != f.ContinentHash || history.ComputeHash() != f.HistoryHash)
            throw new WorldLibraryException($"World '{f.Name}' no longer regenerates identically ({(currentContentHash != null && f.ContentHash.Length > 0 && f.ContentHash != currentContentHash ? $"the game content changed since it was made (saved {Short(f.ContentHash)}, installed {Short(currentContentHash)}) and its land or history now come out differently" : "the world generator changed since it was made")}).");
        progress?.Invoke(new WorldGenProgress(WorldGenPhase.Done, "done", 1, 1, f.History.Years, f.History.Years, null));
        return new LoadedWorld(info, continent, history);
    }

    private static (Continent, WorldHistory) Generate(ContinentSettings cs, HistorySettings hs, Action<WorldGenProgress>? progress, CancellationToken ct)
    {
        Continent? done = null;
        var c = ContinentGenerator.Generate(cs, (stage, i, n) =>
        {
            ct.ThrowIfCancellationRequested();
            progress?.Invoke(new WorldGenProgress(WorldGenPhase.Continent, stage, i, n, 0, hs.Years, null));
        });
        done = c;
        var h = HistoryGenerator.Generate(c, hs, p =>
        {
            ct.ThrowIfCancellationRequested();
            var first = done;
            done = null; // hand the finished terrain over once
            progress?.Invoke(new WorldGenProgress(WorldGenPhase.History, "history", 0, 0, p.Year, p.Years, p.Entry, first, p.Owner));
        });
        return (c, h);
    }

    /// <summary>A deterministic default name for a seed, e.g. "The Marches of Aldmoor".</summary>
    public static string DefaultName(ulong seed)
    {
        string[] kinds = ["The Marches of", "The Realms of", "The Shires of", "The Vale of", "The Reaches of", "The Lands of", "The Kingdoms of", "The Hollows of"];
        string[] a = ["Ald", "Ash", "Bren", "Cor", "Dun", "Ever", "Fen", "Gar", "Har", "Iron", "Kel", "Lor", "Mar", "Nor", "Oak", "Ra", "Stor", "Thorn", "Wil", "Wyn"];
        string[] b = ["moor", "wick", "ford", "mark", "holt", "gate", "vale", "wald", "stead", "haven", "ridge", "mere", "combe", "dale"];
        ulong x = Core.DetRng.Mix(seed ^ 0xA11CE5EEDUL);
        string name = a[(int)(x % (ulong)a.Length)].Trim() + b[(int)((x >> 16) % (ulong)b.Length)];
        return kinds[(int)((x >> 32) % (ulong)kinds.Length)] + " " + char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>Starting details of a run, stored next to its save (the character itself is made when the hero entity exists).</summary>
    public sealed record RunInfo(string CharacterName, bool Female, string OriginId, string AmbitionId, bool Ironman, int EmbarkCellX, int EmbarkCellY, string Created, int[]? Difficulty = null);

    public void WriteRunInfo(string id, string runName, RunInfo info)
    {
        Directory.CreateDirectory(RunsDirectory(id));
        File.WriteAllText(Path.Combine(RunsDirectory(id), runName + ".run.json"), JsonSerializer.Serialize(info, Json));
    }

    public RunInfo? ReadRunInfo(string id, string runName)
    {
        var p = Path.Combine(RunsDirectory(id), runName + ".run.json");
        return File.Exists(p) ? JsonSerializer.Deserialize<RunInfo>(File.ReadAllText(p), Json) : null;
    }

    private string FreeId(string baseId)
    {
        var id = baseId;
        for (int n = 2; Directory.Exists(FolderOf(id)); n++) id = baseId + "-" + n;
        return id;
    }

    private static string Short(string h) => h.Length > 10 ? h[..10] : h;

    public static WorldSummary Summarise(Continent c, WorldHistory h) => new(
        h.Years, h.Chronicle.Count, h.Realms.Count, h.Realms.Count(r => r.Alive), h.Settlements.Count(s => !s.IsRuin), h.Settlements.Count(s => s.IsRuin),
        h.Wars.Count, h.Battlefields.Count, h.StartPopulation, h.EndPopulation, (int)Math.Round(c.LandFraction() * 100));
}
