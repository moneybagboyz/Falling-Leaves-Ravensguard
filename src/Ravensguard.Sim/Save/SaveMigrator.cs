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
    public static SaveMigrator CreateDefault() => new SaveMigrator().Register(1, _ => { }).Register(2, MigrateSkillsV3).Register(3, MigrateChunkPaletteV4).Register(4, _ => { }).Register(5, MigrateWeaponFamiliesV6);

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

    /// <summary>
    /// v3 -> v4: chunk sections gain the palette their tile codes are numbered by. A v3 section was numbered by the piece ids sorted, which for the content of every v3 build is the
    /// legacy palette, so the "chunks" section is stamped with <see cref="Ravensguard.Sim.WorldGen.Chunks.PiecePalette.LegacyIds"/> (the per-region sections inside "region.*" read an
    /// empty palette as the same). The entries' bytes are kept as they are.
    /// </summary>
    private static void MigrateChunkPaletteV4(IDictionary<string, byte[]> sections)
    {
        if (!sections.TryGetValue(WorldSave.ChunksSection, out var bytes) || bytes.Length == 0) return;
        var data = SaveCodec.DeserializeCompressed<ChunkStoreData>(bytes);
        if (data.Palette.Length > 0) return;
        data.Palette = [.. Ravensguard.Sim.WorldGen.Chunks.PiecePalette.LegacyIds];
        sections[WorldSave.ChunksSection] = SaveCodec.SerializeCompressed(data);
    }

    /// <summary>
    /// v5 -> v6 (Phase 4 P4-C): the Skill enum gained the six weapon families (Blades, Axes, Hafted, Polearms, Shields, Throwing). The hero record's skill list gains them at 80% of its
    /// Melee level, and every skill-book row of Melee (the micro-levels of people whose skills are tracked) gets a row per family at 80% where the family has none. People rows store
    /// skills in slots that never held a family; they read a family as Melee x0.8 (<see cref="Ravensguard.Sim.Combat.Melee2.CombatSkills"/>). The optional "fight" section is new.
    /// </summary>
    private static void MigrateWeaponFamiliesV6(IDictionary<string, byte[]> sections)
    {
        if (sections.TryGetValue(WorldSave.HeroSection, out var hb))
        {
            var hero = SaveCodec.Deserialize<Ravensguard.Sim.Characters.HeroSave>(hb);
            var rec = Ravensguard.Sim.Characters.HeroRecord.FromSave(hero);
            var skills = new Dictionary<string, int>(rec.Skills, StringComparer.Ordinal);
            Ravensguard.Sim.Characters.HeroRecord.DefaultWeaponFamilies(skills, hero.SkillIds);
            var save = rec.ToSave();
            save.SkillLevels = [.. save.SkillIds.Select(id => skills.GetValueOrDefault(id))];
            sections[WorldSave.HeroSection] = SaveCodec.Serialize(save);
        }
        if (sections.TryGetValue(WorldSave.CraftingSection, out var cb) && cb.Length > 0)
        {
            var cs = SaveCodec.DeserializeCompressed<CraftingSave>(cb);
            var rows = new SortedDictionary<(ulong, byte), long>();
            for (int i = 0; i < cs.SkillMicro.Length; i++) rows[(cs.SkillPerson[i], cs.SkillId[i])] = cs.SkillMicro[i];
            foreach (var ((p, sk), micro) in rows.ToArray())
            {
                if (sk != (byte)Ravensguard.Sim.People.Skill.Melee) continue;
                foreach (var f in Ravensguard.Sim.Combat.Melee2.CombatSkills.Families)
                    rows.TryAdd((p, (byte)f), micro * Ravensguard.Sim.Combat.Melee2.CombatSkills.MigratePm / 1000);
            }
            cs.SkillPerson = [.. rows.Keys.Select(k => k.Item1)]; cs.SkillId = [.. rows.Keys.Select(k => k.Item2)]; cs.SkillMicro = [.. rows.Values];
            sections[WorldSave.CraftingSection] = SaveCodec.SerializeCompressed(cs);
        }
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
