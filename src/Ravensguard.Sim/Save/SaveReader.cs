using System.Text;

namespace Ravensguard.Sim.Save;

public sealed record ModChange(string Id, string SavedVersion, string InstalledVersion);

public sealed class CompatibilityReport
{
    public bool FormatSupported { get; init; }
    public bool NeedsMigration { get; init; }
    public bool ContentHashMatches { get; init; }
    public IReadOnlyList<ModEntry> MissingMods { get; init; } = Array.Empty<ModEntry>();
    public IReadOnlyList<ModChange> ChangedMods { get; init; } = Array.Empty<ModChange>();
    public IReadOnlyList<ModEntry> ExtraMods { get; init; } = Array.Empty<ModEntry>();

    public bool IsFullyCompatible => FormatSupported && ContentHashMatches && MissingMods.Count == 0 && ChangedMods.Count == 0 && ExtraMods.Count == 0;
    /// <summary>Loadable (possibly with warnings). Only a too-new format blocks loading.</summary>
    public bool CanLoad => FormatSupported;

    public string ToText()
    {
        var sb = new StringBuilder();
        if (!FormatSupported) sb.AppendLine("This save was made by a newer version of the game and cannot be loaded.");
        else if (NeedsMigration) sb.AppendLine("This save is from an older version and will be upgraded when loaded.");
        if (!ContentHashMatches) sb.AppendLine("Game content has changed since this save was made; some items may differ.");
        foreach (var m in MissingMods) sb.AppendLine($"Missing mod: {m.Id} (version {m.Version}). Things it added may be lost.");
        foreach (var m in ChangedMods) sb.AppendLine($"Mod changed: {m.Id} (saved with {m.SavedVersion}, installed {m.InstalledVersion}).");
        foreach (var m in ExtraMods) sb.AppendLine($"New mod not in this save: {m.Id} ({m.Version}).");
        if (sb.Length == 0) sb.AppendLine("Compatible.");
        return sb.ToString();
    }
}

public sealed class SaveReader
{
    private readonly SaveMigrator _migrator;
    public SaveReader(SaveMigrator? migrator = null) { _migrator = migrator ?? new SaveMigrator(); }

    /// <summary>Reads only the header (load screen).</summary>
    public SaveHeader ReadHeader(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SaveCodec.ReadHeader(fs);
    }

    public CompatibilityReport CheckCompatibility(SaveHeader header, string currentContentHash, IReadOnlyList<ModEntry> installedMods)
    {
        var installed = installedMods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var saved = header.Mods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        return new CompatibilityReport
        {
            FormatSupported = header.FormatVersion <= _migrator.TargetVersion,
            NeedsMigration = header.FormatVersion < _migrator.TargetVersion,
            ContentHashMatches = header.ContentHash == currentContentHash,
            MissingMods = header.Mods.Where(m => !installed.ContainsKey(m.Id)).OrderBy(m => m.Id, StringComparer.Ordinal).ToList(),
            ChangedMods = header.Mods.Where(m => installed.TryGetValue(m.Id, out var i) && i.Version != m.Version)
                .Select(m => new ModChange(m.Id, m.Version, installed[m.Id].Version)).OrderBy(m => m.Id, StringComparer.Ordinal).ToList(),
            ExtraMods = installedMods.Where(m => !saved.ContainsKey(m.Id)).OrderBy(m => m.Id, StringComparer.Ordinal).ToList(),
        };
    }

    public CompatibilityReport CheckCompatibility(string path, string currentContentHash, IReadOnlyList<ModEntry> installedMods)
        => CheckCompatibility(ReadHeader(path), currentContentHash, installedMods);

    /// <summary>Full load, running the migration chain if the save is older.</summary>
    public SaveFile Load(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (IOException e) { throw new SaveException($"Cannot read save '{path}'.", e); }
        return _migrator.Migrate(SaveCodec.Decode(bytes));
    }
}
