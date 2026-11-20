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
    public static SaveMigrator CreateDefault() => new SaveMigrator().Register(1, _ => { }).Register(2, MigrateSkillsV3).Register(3, MigrateChunkPaletteV4).Register(4, _ => { }).Register(5, MigrateWeaponFamiliesV6).Register(6, MigrateMoraleV7)
        .Register(7, MigratePhase5V8);

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
            // a v5 save never trained a family: an id written at 0 by an earlier step's rewrite (v3 lists every enum id) counts as not given
            Ravensguard.Sim.Characters.HeroRecord.DefaultWeaponFamilies(skills, hero.SkillIds.Where((id, i) => i < hero.SkillLevels.Length && hero.SkillLevels[i] > 0));
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

    /// <summary>
    /// v6 -> v7 (Phase 4 P4-D, morale, capture and succession): the optional "captivity" and "succession" sections are new, and a fight record in progress gains each fighter's morale state
    /// (key 20). A v6 fight record is re-stamped with the morale state the model starts a fighter with, so a load resumes it the same way a v7 save of it would.
    /// </summary>
    private static void MigrateMoraleV7(IDictionary<string, byte[]> sections)
    {
        if (!sections.TryGetValue(FightRecord.SectionName, out var fb) || fb.Length == 0) return;
        var rec = SaveCodec.DeserializeCompressed<FightRecord>(fb);
        if (rec.Morale != null) return;
        rec.Morale = [];          // an empty table: every fighter's morale is initialised from its kit on the next step
        sections[FightRecord.SectionName] = SaveCodec.SerializeCompressed(rec);
    }

    /// <summary>
    /// The old DF scale's byte (Standard 0, WellCrafted 1, FinelyCrafted 2, Superior 3, Exceptional 4, Masterwork 5, Artifact 6) as the one scale's (<c>ItemQuality</c>: Crude 0, Poor 1, Common 2,
    /// Fine 3, Masterwork 4), through the content alias map (items-quality-and-wear 6): standard and well crafted are common, finely crafted, superior and exceptional fine, masterwork and
    /// artifact masterwork. Without it a v7 byte 0 would read as Crude.
    /// </summary>
    public static byte QualityV8(byte old) => old switch { 0 or 1 => 2, 2 or 3 or 4 => 3, _ => 4 };

    /// <summary>
    /// v7 -> v8 (Phase 5 step 1, the one migrator step of the phase: overview 7). Three stored meanings change:
    /// (1) P5-A1: a fight record's quality bytes (each fighter's weapon, key 6; the weapon now held, key 25; the worn pieces, key 10) are remapped from the DF scale to <c>ItemQuality</c>
    /// (<see cref="QualityV8"/>; item rows always stored <c>ItemQuality</c> and keep their bytes);
    /// (2) P5-A4: the hero becomes a person row: the hero section's <c>PersonIndex</c> is set to pending, and the load inserts the row and the hero's household from the Phase 4 kin links
    /// (<see cref="Ravensguard.Sim.Succession.HeroPerson.InsertForLoad"/>: it needs the regenerated people, which a section step does not have); an inheritance record's <c>DeedsPending</c>
    /// is now <c>Deeds</c> (the same key, empty in every v7 save);
    /// (3) the hook for P5-G (vehicle-designer 3.3, not part of step 1): the "mounts" section's vehicle rows keep <c>WheelPm</c> here. When part rows land they are additive keys on the
    /// vehicle row, and a v8 row without them is expanded from its template at load, so P5-G needs no second step; if it cannot be additive, P5-G registers a v8 -> v9 step here.
    /// (4) the goods-table fix (after step 2): a v7 save's content indices were numbered by the Phase 4 content, so the step stamps the "tables" section with those tables
    /// (<see cref="LegacyIdTables.V7"/>: Phase 3's 34 craft recipes and 12 proficiencies, which Phase 5 renumbered) and the load maps them by name like any other content change.
    /// </summary>
    private static void MigratePhase5V8(IDictionary<string, byte[]> sections)
    {
        if (!sections.ContainsKey(IdTablesSave.SectionName)) sections[IdTablesSave.SectionName] = SaveCodec.Serialize(LegacyIdTables.V7.ToSave());
        if (sections.TryGetValue(FightRecord.SectionName, out var fb) && fb.Length > 0)
        {
            var rec = SaveCodec.DeserializeCompressed<FightRecord>(fb);
            QualitiesV8(rec);
            sections[FightRecord.SectionName] = SaveCodec.SerializeCompressed(rec);
        }
        // P5-I review (finding 5): a crawl in progress keeps its own fight record and each member's kit (JSON, the quality as the old enum's number), so their bytes move the same way
        if (sections.TryGetValue(SiteSave.SectionName, out var sb) && sb.Length > 0)
        {
            var sites = SaveCodec.DeserializeCompressed<SiteSave>(sb);
            if (sites.Active is { } crawl)
            {
                if (crawl.Fight.Length > 0) { var rec = FightRecord.FromBytes(crawl.Fight); QualitiesV8(rec); crawl.Fight = rec.ToBytes(); }
                foreach (var m in crawl.Members) m.KitJson = KitQualitiesV8(m.KitJson);
                sections[SiteSave.SectionName] = SaveCodec.SerializeCompressed(sites);
            }
        }
        if (sections.TryGetValue(WorldSave.HeroSection, out var hb))
        {
            var hero = SaveCodec.Deserialize<Ravensguard.Sim.Characters.HeroSave>(hb);
            hero.PersonIndex = Ravensguard.Sim.Succession.HeroPerson.Pending;
            sections[WorldSave.HeroSection] = SaveCodec.Serialize(hero);
        }
    }

    private static void QualitiesV8(FightRecord rec)
    {
        foreach (var f in rec.Fighters)
        {
            f.WeaponQuality = QualityV8(f.WeaponQuality);
            f.NowQuality = QualityV8(f.NowQuality);
            f.WornQualities = [.. f.WornQualities.Select(QualityV8)];
        }
    }

    /// <summary>A crawl member's kit as saved (<c>FighterKit</c> JSON): the weapon's and every worn piece's <c>Quality</c> number moved to the one scale; anything else is kept as written.</summary>
    public static string KitQualitiesV8(string json)
    {
        if (json.Length == 0) return json;
        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        if (node is not System.Text.Json.Nodes.JsonObject kit) return json;
        static void Fix(System.Text.Json.Nodes.JsonNode? n)
        {
            if (n is System.Text.Json.Nodes.JsonObject o && o["Quality"] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out int q)) o["Quality"] = (int)QualityV8((byte)Math.Clamp(q, 0, 255));
        }
        Fix(kit["Weapon"]);
        if (kit["Worn"] is System.Text.Json.Nodes.JsonArray worn) foreach (var w in worn) Fix(w);
        return kit.ToJsonString();
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
