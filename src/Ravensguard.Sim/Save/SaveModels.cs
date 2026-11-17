using MessagePack;

namespace Ravensguard.Sim.Save;

[MessagePackObject]
public sealed class ModEntry
{
    [Key(0)] public string Id { get; set; } = "";
    [Key(1)] public string Version { get; set; } = "";
    public ModEntry() { }
    public ModEntry(string id, string version) { Id = id; Version = version; }
}

/// <summary>Save header. No wall-clock data so identical state gives identical bytes.</summary>
[MessagePackObject]
public sealed class SaveHeader
{
    [Key(0)] public int FormatVersion { get; set; } = SaveFormat.CurrentVersion;
    [Key(1)] public string GameVersion { get; set; } = "";
    [Key(2)] public string ContentHash { get; set; } = "";
    [Key(3)] public List<ModEntry> Mods { get; set; } = new();
    [Key(4)] public ulong WorldSeed { get; set; }
    /// <summary>Game seconds (Core.GameTime.Seconds).</summary>
    [Key(5)] public long GameSeconds { get; set; }
    [Key(6)] public bool Ironman { get; set; }

    [IgnoreMember] public Core.GameTime Time { get => new(GameSeconds); set => GameSeconds = value.Seconds; }
}

public static class SaveFormat
{
    public const int CurrentVersion = 1;
    public static readonly byte[] Magic = { (byte)'R', (byte)'V', (byte)'S', (byte)'G' };
}

/// <summary>Header plus named byte[] sections. Sections are copied on construction (immutable).</summary>
public sealed class SaveFile
{
    public SaveHeader Header { get; }
    public IReadOnlyDictionary<string, byte[]> Sections { get; }

    public SaveFile(SaveHeader header, IEnumerable<KeyValuePair<string, byte[]>> sections)
    {
        Header = Clone(header);
        var d = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (k, v) in sections) d[k] = (byte[])v.Clone();
        Sections = d;
    }

    private static SaveHeader Clone(SaveHeader h) => new()
    {
        FormatVersion = h.FormatVersion, GameVersion = h.GameVersion, ContentHash = h.ContentHash,
        Mods = h.Mods.Select(m => new ModEntry(m.Id, m.Version)).ToList(),
        WorldSeed = h.WorldSeed, GameSeconds = h.GameSeconds, Ironman = h.Ironman,
    };
}

public class SaveException : Exception
{
    public SaveException(string message, Exception? inner = null) : base(message, inner) { }
}
