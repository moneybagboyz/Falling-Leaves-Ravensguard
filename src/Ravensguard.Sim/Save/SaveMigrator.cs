namespace Ravensguard.Sim.Save;

/// <summary>Step functions N -> N+1 operating on the raw section map.</summary>
public sealed class SaveMigrator
{
    private readonly Dictionary<int, Action<IDictionary<string, byte[]>>> _steps = new();

    public int TargetVersion { get; }
    public SaveMigrator(int targetVersion = SaveFormat.CurrentVersion) { TargetVersion = targetVersion; }

    public SaveMigrator Register(int fromVersion, Action<IDictionary<string, byte[]>> step)
    {
        if (!_steps.TryAdd(fromVersion, step)) throw new ArgumentException($"Step from v{fromVersion} already registered.");
        return this;
    }

    public SaveFile Migrate(SaveFile file)
    {
        int v = file.Header.FormatVersion;
        if (v == TargetVersion) return file;
        if (v > TargetVersion)
            throw new SaveException($"Save format v{v} is newer than this game supports (v{TargetVersion}).");
        var sections = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var kv in file.Sections) sections[kv.Key] = kv.Value.ToArray();
        for (; v < TargetVersion; v++)
        {
            if (!_steps.TryGetValue(v, out var step))
                throw new SaveException($"No migration step registered from save format v{v} to v{v + 1}.");
            step(sections);
        }
        var header = file.Header with { FormatVersion = TargetVersion };
        return new SaveFile(header, sections);
    }
}
