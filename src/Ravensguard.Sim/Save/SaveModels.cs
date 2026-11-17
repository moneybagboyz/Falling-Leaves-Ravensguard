using MessagePack;

namespace Ravensguard.Sim.Save;

[MessagePackObject]
public sealed class ModEntry
{
    [Key(0)] public string Id { get; init; } = "";
    [Key(1)] public string Version { get; init; } = "";
    public ModEntry() { }
    public ModEntry(string id, string version) { Id = id; Version = version; }
}

/// <summary>Save header (immutable; use <c>with</c> to derive a changed copy). No wall-clock data so identical state gives identical bytes.</summary>
[MessagePackObject]
public sealed record SaveHeader
{
    [Key(0)] public int FormatVersion { get; init; } = SaveFormat.CurrentVersion;
    [Key(1)] public string GameVersion { get; init; } = "";
    [Key(2)] public string ContentHash { get; init; } = "";
    [Key(3)] public IReadOnlyList<ModEntry> Mods { get; init; } = Array.Empty<ModEntry>();
    [Key(4)] public ulong WorldSeed { get; init; }
    /// <summary>Game seconds (Core.GameTime.Seconds).</summary>
    [Key(5)] public long GameSeconds { get; init; }
    [Key(6)] public bool Ironman { get; init; }

    [IgnoreMember] public Core.GameTime Time => new(GameSeconds);
}

public static class SaveFormat
{
    public const int CurrentVersion = 1;
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
