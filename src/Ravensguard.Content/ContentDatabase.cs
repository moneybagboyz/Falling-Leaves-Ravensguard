using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ravensguard.Content;

public sealed class ContentEntry
{
    public required string Type { get; init; }
    public required string Id { get; init; }
    public required string Mod { get; set; }
    public required SourceLocation Location { get; set; }
    /// <summary>Merged JSON (patches applied), including the "type" field.</summary>
    public required JsonObject Json { get; set; }
    /// <summary>Positions of top-level properties in the defining file (empty when loaded from cache).</summary>
    public Dictionary<string, SourceLocation> PropertyLocations { get; set; } = [];
    public ContentDef? Value { get; internal set; }
}

/// <summary>Records that <see cref="NewMod"/> redefined an entry first defined by <see cref="PreviousMod"/>.</summary>
public sealed record ContentOverride(string Type, string Id, string PreviousMod, string NewMod);

public sealed class ContentDatabase
{
    private readonly Dictionary<string, Dictionary<string, ContentEntry>> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _migrations = new(StringComparer.Ordinal);
    private readonly List<ContentOverride> _overrides = [];

    public ContentTypeRegistry Registry { get; }
    public IReadOnlyList<ContentOverride> Overrides => _overrides;

    public ContentDatabase(ContentTypeRegistry? registry = null) => Registry = registry ?? ContentTypeRegistry.Default;

    internal static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    // ---- queries ----

    public IEnumerable<ContentEntry> Entries => _entries.Values.SelectMany(d => d.Values);
    public IEnumerable<string> Ids(string type) => _entries.TryGetValue(type, out var d) ? d.Keys : [];
    public bool Contains(string type, string id) => _entries.TryGetValue(type, out var d) && d.ContainsKey(id);
    public ContentEntry? GetEntry(string type, string id) =>
        _entries.TryGetValue(type, out var d) && d.TryGetValue(id, out var e) ? e : null;

    public bool TryGet<T>(string id, out T value) where T : ContentDef
    {
        var type = Registry.NameOf(typeof(T));
        var e = GetEntry(type, id);
        if (e?.Value == null && _migrations.ContainsKey(type))
        {
            try { e = GetEntry(type, Resolve(type, id)); } catch (ContentException) { e = null; }
        }
        if (e?.Value is T t) { value = t; return true; }
        value = null!;
        return false;
    }

    public T Get<T>(string id) where T : ContentDef =>
        TryGet<T>(id, out var v) ? v : throw new KeyNotFoundException($"No {Registry.NameOf(typeof(T))} with id '{id}'");

    public IEnumerable<T> All<T>() where T : ContentDef =>
        (_entries.TryGetValue(Registry.NameOf(typeof(T)), out var d) ? d.Values : Enumerable.Empty<ContentEntry>())
            .Select(e => e.Value).OfType<T>();

    /// <summary>Follows migrations (old id -> new id, chains allowed). Cycles throw <see cref="ContentException"/>.</summary>
    public string Resolve(string type, string id)
    {
        if (!_migrations.TryGetValue(type, out var map)) return id;
        var seen = new HashSet<string> { id };
        var cur = id;
        while (map.TryGetValue(cur, out var next))
        {
            if (!seen.Add(next))
                throw new ContentException($"Migration cycle for {type} '{id}' (revisits '{next}')");
            cur = next;
        }
        return cur;
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Migrations =>
        _migrations.ToDictionary(k => k.Key, k => (IReadOnlyDictionary<string, string>)k.Value);

    /// <summary>Human-readable list of every overridden entry with both mods.</summary>
    public string ConflictReport() =>
        string.Join(Environment.NewLine, _overrides.Select(o => $"{o.Type} '{o.Id}': '{o.NewMod}' overrides '{o.PreviousMod}'"));

    // ---- mutation (loader) ----

    internal ContentEntry? Put(ContentEntry entry)
    {
        if (!_entries.TryGetValue(entry.Type, out var d)) _entries[entry.Type] = d = new(StringComparer.Ordinal);
        d.TryGetValue(entry.Id, out var previous);
        d[entry.Id] = entry;
        return previous;
    }

    internal void AddOverride(ContentOverride o) => _overrides.Add(o);

    internal void AddMigration(string type, string from, string to)
    {
        if (!_migrations.TryGetValue(type, out var m)) _migrations[type] = m = new(StringComparer.Ordinal);
        m[from] = to;
    }

    /// <summary>Deserialises every entry's JSON into its record. Errors go to the report.</summary>
    internal void Materialize(ContentReport report)
    {
        foreach (var e in Entries)
        {
            if (!Registry.TryGetType(e.Type, out var clr)) continue;
            var node = (JsonObject)e.Json.DeepClone();
            node.Remove("type");
            try
            {
                e.Value = (ContentDef?)node.Deserialize(clr, SerializerOptions);
                if (e.Value == null) report.Error(e.Location, $"{e.Type} '{e.Id}' deserialised to null");
            }
            catch (JsonException ex)
            {
                // Prefer the property's own position if the message names one.
                var loc = e.Location;
                if (ex.Path is { Length: > 2 } p && p.StartsWith("$.") &&
                    e.PropertyLocations.TryGetValue(p[2..].Split('.', '[')[0], out var pl)) loc = pl;
                report.Error(loc, $"{e.Type} '{e.Id}': {ex.Message}");
            }
        }
    }
}
