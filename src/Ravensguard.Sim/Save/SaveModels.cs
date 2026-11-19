using MessagePack;

namespace Ravensguard.Sim.Save;

[MessagePackObject]
public sealed class ModEntry
{
    [Key(0)] public string Id { get; }
    [Key(1)] public string Version { get; }
    /// <summary>Constructor-based (no init accessors with initializers, which MessagePack would reset on deserialisation); a missing value reads as empty.</summary>
    [SerializationConstructor]
    public ModEntry(string? id, string? version) { Id = id ?? ""; Version = version ?? ""; }
}

/// <summary>Save header (immutable; use <c>with</c> to derive a changed copy). No wall-clock data so identical state gives identical bytes.</summary>
[MessagePackObject]
public sealed record SaveHeader
{
    // Properties have no initializers (MessagePack would reset them on read, MsgPack017); nullable backing fields give the defaults instead.
    private int? _formatVersion;
    private string? _gameVersion, _contentHash;
    private IReadOnlyList<ModEntry>? _mods;

    /// <summary>Defaults to the current format when never set; a header read from disk always carries its stored value (0 when the key was missing).</summary>
    [Key(0)] public int FormatVersion { get => _formatVersion ?? SaveFormat.CurrentVersion; init => _formatVersion = value; }
    [Key(1)] public string GameVersion { get => _gameVersion ?? ""; init => _gameVersion = value; }
    [Key(2)] public string ContentHash { get => _contentHash ?? ""; init => _contentHash = value; }
    /// <summary>A missing or null mod list reads as empty.</summary>
    [Key(3)] public IReadOnlyList<ModEntry> Mods { get => _mods ?? Array.Empty<ModEntry>(); init => _mods = value; }
    [Key(4)] public ulong WorldSeed { get; init; }
    /// <summary>Game seconds (Core.GameTime.Seconds).</summary>
    [Key(5)] public long GameSeconds { get; init; }
    [Key(6)] public bool Ironman { get; init; }
    /// <summary>Number of header keys this format writes; a stored header with fewer is from an incompatible version.</summary>
    [IgnoreMember] public const int KeyCount = 7;

    [IgnoreMember] public Core.GameTime Time => new(GameSeconds);
}

public static class SaveFormat
{
    /// <summary>v2: region sections and the home people carry a baseline hash and generator version (<see cref="BaselineGuard"/>).
    /// v3: seven skills are appended to the Skill enum (Tailoring, Surgery, Speech, Athletics, Tactics, Stealth, Lockpicking), so the hero's skill list grows; the optional "crafting" section is new.
    /// v5: the optional "wounds" section (Phase 4 P4-B: blood, pain, wound records, permanent damage); a v4 save has none and loads with nobody hurt.
    /// v4: the chunk sections carry the build-piece palette their tile codes are numbered by (<see cref="ChunkStoreData.Palette"/>); a v3 section has none and is read with the legacy palette.
    /// v6: six weapon-family skills appended to the Skill enum (the hero record and skill book gain them at 80% of Melee) and the optional "fight" section (a fight in progress, P4-C).</summary>
    public const int CurrentVersion = 6;
    /// <summary>
    /// Version of everything a saved region is regenerated from (region terrain, landmarks, populator, chunk streaming frame). Bump it whenever generation output changes on
    /// purpose, together with the golden hashes: saves diffed against the old output are then refused with a clear message instead of loading onto different people.
    /// </summary>
    public const int RegionGeneratorVersion = 1;
    public static readonly byte[] Magic = { (byte)'R', (byte)'V', (byte)'S', (byte)'G' };
}

/// <summary>
/// Header plus named sections. Fully immutable: the header is copied (mods list included) and every section is copied
/// into private storage on construction; sections are exposed only as <see cref="ReadOnlyMemory{T}"/>.
/// </summary>
public sealed class SaveFile
{
    public SaveHeader Header { get; }
    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Sections { get; }

    public SaveFile(SaveHeader header, IEnumerable<KeyValuePair<string, byte[]>> sections)
    {
        Header = header with { Mods = new System.Collections.ObjectModel.ReadOnlyCollection<ModEntry>(header.Mods.Select(m => new ModEntry(m.Id, m.Version)).ToList()) };
        var d = new SortedDictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        foreach (var (k, v) in sections) d[k] = (byte[])v.Clone();
        Sections = d;
    }
}

public class SaveException : Exception
{
    public SaveException(string message, Exception? inner = null) : base(message, inner) { }
}
