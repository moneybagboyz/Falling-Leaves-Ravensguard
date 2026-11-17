using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ravensguard.Content;

public sealed class ModLoadResult
{
    public required ContentDatabase Database { get; init; }
    public required ContentReport Report { get; init; }
    public IReadOnlyList<string> LoadOrder { get; init; } = [];
    public bool FromCache { get; init; }
    public string? Hash { get; init; }
    public bool Success => !Report.HasErrors;
}

/// <summary>Loads mod folders (the base game is just <c>content/base</c>) in dependency order.</summary>
public sealed class ModLoader(ContentTypeRegistry? registry = null, ContentVocabulary? vocabulary = null)
{
    private readonly ContentTypeRegistry _registry = registry ?? ContentTypeRegistry.Default;

    public static IEnumerable<string> EnumerateFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal);

    public static ModInfo ReadManifest(string folder, int index, ContentReport report)
    {
        var path = Path.Combine(folder, "mod.json");
        try
        {
            var bytes = File.ReadAllBytes(path);
            var node = ContentFileParserSingle(bytes, path, report);
            var m = node?.Deserialize<ModManifest>(ContentDatabase.SerializerOptions);
            if (m != null) return new ModInfo(m, folder, index);
        }
        catch (FileNotFoundException) { report.Error(new(path, 0, 0), "mod.json not found"); }
        catch (DirectoryNotFoundException) { report.Error(new(folder, 0, 0), "mod folder not found"); }
        catch (JsonException ex)
        {
            report.Error(new(path, (int)(ex.LineNumber ?? 0) + 1, (int)(ex.BytePositionInLine ?? 0) + 1), $"Invalid mod.json: {ex.Message}");
        }
        return null!;
    }

    private static JsonNode? ContentFileParserSingle(byte[] bytes, string path, ContentReport report) =>
        JsonNode.Parse(bytes, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

    /// <summary>Stable topological sort by dependencies and load_after. Throws <see cref="ContentException"/> on cycles or missing dependencies.</summary>
    public static List<ModInfo> Sort(IReadOnlyList<ModInfo> mods)
    {
        var byId = new Dictionary<string, ModInfo>(StringComparer.Ordinal);
        foreach (var m in mods)
            if (!byId.TryAdd(m.Manifest.Id, m)) throw new ContentException($"Duplicate mod id '{m.Manifest.Id}'");

        var before = mods.ToDictionary(m => m.Manifest.Id, _ => new HashSet<string>(StringComparer.Ordinal));
        foreach (var m in mods)
        {
            foreach (var d in m.Manifest.Dependencies)
            {
                if (!byId.ContainsKey(d)) throw new ContentException($"Mod '{m.Manifest.Id}' depends on missing mod '{d}'");
                before[m.Manifest.Id].Add(d);
            }
            foreach (var d in m.Manifest.LoadAfter)
                if (byId.ContainsKey(d)) before[m.Manifest.Id].Add(d);
        }

        var result = new List<ModInfo>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        while (result.Count < mods.Count)
        {
            var next = mods.FirstOrDefault(m => !done.Contains(m.Manifest.Id) && before[m.Manifest.Id].All(done.Contains));
            if (next == null)
            {
                var stuck = string.Join(", ", mods.Where(m => !done.Contains(m.Manifest.Id)).Select(m => m.Manifest.Id));
                throw new ContentException($"Mod dependency cycle among: {stuck}");
            }
            done.Add(next.Manifest.Id);
            result.Add(next);
        }
        return result;
    }

    public ModLoadResult Load(IEnumerable<string> modFolders, string? cacheDirectory = null)
    {
        var report = new ContentReport();
        var infos = new List<ModInfo>();
        int idx = 0;
        foreach (var f in modFolders)
        {
            var info = ReadManifest(f, idx++, report);
            if (info != null) infos.Add(info);
        }

        List<ModInfo> order;
        try { order = Sort(infos); }
        catch (ContentException ex)
        {
            report.Error(null, ex.Message);
            return new ModLoadResult { Database = new ContentDatabase(_registry), Report = report };
        }
        if (report.HasErrors)
            return new ModLoadResult { Database = new ContentDatabase(_registry), Report = report };

        var ids = order.Select(o => o.Manifest.Id).ToList();
        string? hash = null;
        ContentCache? cache = cacheDirectory == null ? null : new ContentCache(cacheDirectory);
        if (cache != null)
        {
            hash = ContentHasher.Compute(order);
            var cached = cache.TryLoad(hash, _registry);
            if (cached != null)
                return new ModLoadResult { Database = cached, Report = report, LoadOrder = ids, FromCache = true, Hash = hash };
        }

        var db = new ContentDatabase(_registry);
        foreach (var mod in order) LoadMod(mod, db, report);
        db.Materialize(report);
        ContentValidator.Validate(db, vocabulary, report);

        if (cache != null && hash != null && !report.HasErrors) cache.Store(hash, db);
        return new ModLoadResult { Database = db, Report = report, LoadOrder = ids, Hash = hash };
    }

    private void LoadMod(ModInfo mod, ContentDatabase db, ContentReport report)
    {
        var modId = mod.Manifest.Id;
        var defined = new Dictionary<(string, string), SourceLocation>();
        var patches = new List<RawDefinition>();
        var migrations = new List<(string Type, string From, string To, SourceLocation Loc)>();

        foreach (var rel in EnumerateFiles(mod.Folder))
        {
            if (rel.Equals("mod.json", StringComparison.OrdinalIgnoreCase)) continue;
            var full = Path.Combine(mod.Folder, rel);
            foreach (var raw in ContentFileParser.Parse(File.ReadAllBytes(full), full, report))
            {
                var loc = raw.Location;
                if (!TryString(raw.Node, "type", out var type)) { report.Error(loc, "Definition is missing a string \"type\""); continue; }

                if (type == "patch") { patches.Add(raw); continue; }
                if (type == "migration")
                {
                    if (TryString(raw.Node, "target_type", out var tt) && TryString(raw.Node, "from", out var from) && TryString(raw.Node, "to", out var to))
                    {
                        db.AddMigration(tt, from, to);
                        migrations.Add((tt, from, to, loc));
                    }
                    else report.Error(loc, "migration requires string fields target_type, from and to");
                    continue;
                }
                if (!_registry.TryGetType(type, out _)) { report.Error(loc, $"Unknown content type '{type}'"); continue; }
                if (!TryString(raw.Node, "id", out var id) || id.Length == 0) { report.Error(loc, $"{type} definition is missing a string \"id\""); continue; }

                if (defined.TryGetValue((type, id), out var first))
                {
                    report.Error(loc, $"Duplicate {type} id '{id}' in mod '{modId}' (first defined at {first})");
                    continue;
                }
                defined[(type, id)] = loc;
                var prev = db.Put(new ContentEntry
                {
                    Type = type, Id = id, Mod = modId, Location = loc, Json = raw.Node, PropertyLocations = raw.PropertyLocations,
                });
                if (prev != null)
                {
                    db.AddOverride(new ContentOverride(type, id, prev.Mod, modId));
                    report.Warning(loc, $"{type} '{id}' from mod '{modId}' overrides definition from mod '{prev.Mod}'");
                }
            }
        }

        foreach (var p in patches)
        {
            var loc = p.Location;
            if (!TryString(p.Node, "target_type", out var tt) || !TryString(p.Node, "target_id", out var tid))
            { report.Error(loc, "patch requires string fields target_type and target_id"); continue; }
            if (p.Node["set"] is not JsonObject set) { report.Error(loc, "patch requires a \"set\" object"); continue; }
            var entry = db.GetEntry(tt, tid);
            if (entry == null) { report.Error(loc, $"patch target {tt} '{tid}' does not exist"); continue; }
            foreach (var (k, v) in set)
            {
                if (k is "id" or "type") { report.Error(loc, $"patch cannot change \"{k}\""); continue; }
                entry.Json[k] = v?.DeepClone();
            }
        }

        foreach (var m in migrations)
        {
            try { db.Resolve(m.Type, m.From); }
            catch (ContentException ex) { report.Error(m.Loc, ex.Message); }
        }
    }

    private static bool TryString(JsonObject o, string key, out string value)
    {
        if (o[key] is JsonValue v && v.TryGetValue<string>(out var s)) { value = s; return true; }
        value = "";
        return false;
    }
}
