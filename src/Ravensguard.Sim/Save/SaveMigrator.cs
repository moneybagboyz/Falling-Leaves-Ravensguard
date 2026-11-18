namespace Ravensguard.Sim.Save;

/// <summary>Step functions N -> N+1 operating on the raw section map.</summary>
public sealed class SaveMigrator
{
    private readonly Dictionary<int, Action<IDictionary<string, byte[]>>> _steps = new();

    public int TargetVersion { get; }
    public SaveMigrator(int targetVersion = SaveFormat.CurrentVersion) { TargetVersion = targetVersion; }

    /// <summary>
    /// The game's migrations. v1 -> v2: v1 saves have no baseline guard (empty hashes) and are loaded unguarded; their format is otherwise unchanged (every v2 key is additive),
    /// so the step only re-stamps the version. A v1 save made while a region without people held chunks lost those chunks (fixed in v2); nothing can recover them.
    /// </summary>
    public static SaveMigrator CreateDefault() => new SaveMigrator().Register(1, _ => { }).Register(2, MigrateSkillsV3);

    /// <summary>
    /// v2 -> v3: the Skill enum gained seven entries at its end, so the hero's skill lists (parallel arrays of ids and levels) gain them at level 0. People rows store skills as the enum's byte values, which
    /// do not move, so they need nothing; the "crafting" section is optional and new. The hero section is rewritten through the record so the bytes are exactly those a v3 save would hold.
    /// </summary>
    private static void MigrateSkillsV3(IDictionary<string, byte[]> sections)
    {
        if (!sections.TryGetValue(WorldSave.HeroSection, out var bytes)) return;
        var hero = SaveCodec.Deserialize<Ravensguard.Sim.Characters.HeroSave>(bytes);
        sections[WorldSave.HeroSection] = SaveCodec.Serialize(Ravensguard.Sim.Characters.HeroRecord.FromSave(hero).ToSave());
    }

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
