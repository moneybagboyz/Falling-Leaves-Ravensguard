using System.Text;
using System.Text.Json;
using Ravensguard.Content;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Sites;

/// <summary>A room template parsed from its rows (3.1): size, cells, doors and anchors.</summary>
public sealed class RoomShape
{
    public required SiteRoomDef Def { get; init; }
    public int W, H;
    public char[] Cells = [];
    public List<(int X, int Y)> Doors = [];
    public char At(int x, int y) => Cells[y * W + x];
    public bool Rock => Def.Walls == "rock";
}

/// <summary>An issue the site CI found in content (test EX-3): an id that does not resolve, a room that cannot be reached, a purpose nothing satisfies.</summary>
public sealed record SiteIssue(string Id, string Code, string Message);

/// <summary>
/// The site content (exploration-and-sites 3.1, 3.3): templates, room templates, loot tables, site items, traps, monsters, constants and the weather-in-combat table, from the
/// <see cref="ContentDatabase"/> (loaded and schema-checked by the ModLoader). <see cref="Hash"/> is the content hash generation is a pure function of.
/// </summary>
public sealed class SiteContent
{
    public ContentDatabase Db { get; }
    public SiteConstantsDef K { get; }
    public IReadOnlyList<SiteTemplateDef> Templates { get; }
    public IReadOnlyDictionary<string, RoomShape> Rooms { get; }
    public IReadOnlyDictionary<string, SiteLootTableDef> Loot { get; }
    public IReadOnlyDictionary<string, SiteItemDef> Items { get; }
    public IReadOnlyDictionary<string, SiteTrapDef> Traps { get; }
    public IReadOnlyDictionary<string, SiteMonsterDef> Monsters { get; }
    public IReadOnlyDictionary<string, WeatherCombatDef> Weather { get; }
    public IReadOnlyList<NamePoolDef> Names { get; }
    public string Hash { get; }

    /// <summary>Room purposes the generator and the CI know.</summary>
    public static readonly HashSet<string> KnownPurposes = new(StringComparer.Ordinal)
    { "entrance", "storage", "rest", "water", "passage", "burial", "worship", "lair", "den", "guard", "captive", "dock", "work" };

    public SiteContent(ContentDatabase db)
    {
        Db = db;
        K = db.TryGet<SiteConstantsDef>("default", out var k) ? k : new SiteConstantsDef { Id = "default" };
        Templates = [.. db.All<SiteTemplateDef>()];
        Rooms = db.All<SiteRoomDef>().ToDictionary(r => r.Id, Parse, StringComparer.Ordinal);
        Loot = db.All<SiteLootTableDef>().ToDictionary(t => t.Id, StringComparer.Ordinal);
        Items = db.All<SiteItemDef>().ToDictionary(t => t.Id, StringComparer.Ordinal);
        Traps = db.All<SiteTrapDef>().ToDictionary(t => t.Id, StringComparer.Ordinal);
        Monsters = db.All<SiteMonsterDef>().ToDictionary(t => t.Id, StringComparer.Ordinal);
        Weather = db.All<WeatherCombatDef>().ToDictionary(t => t.State, StringComparer.Ordinal);
        Names = [.. db.All<NamePoolDef>().OrderBy(n => n.Id, StringComparer.Ordinal)];
        Hash = ComputeHash();
    }

    private static RoomShape Parse(SiteRoomDef d)
    {
        int h = d.Rows.Count, w = h == 0 ? 0 : d.Rows[0].Length;
        var s = new RoomShape { Def = d, W = w, H = h, Cells = new char[w * h] };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char c = x < d.Rows[y].Length ? d.Rows[y][x] : '#';
                s.Cells[y * w + x] = c;
                if (c == '+') s.Doors.Add((x, y));
            }
        return s;
    }

    private string ComputeHash()
    {
        var sb = new StringBuilder();
        var o = new JsonSerializerOptions { WriteIndented = false };
        void Add<T>(IEnumerable<T> rows) where T : ContentDef { foreach (var r in rows.OrderBy(r => r.Id, StringComparer.Ordinal)) sb.Append(JsonSerializer.Serialize(r, o)).Append('\n'); }
        Add(Templates); Add(Rooms.Values.Select(r => r.Def)); Add(Loot.Values); Add(Items.Values); Add(Traps.Values); Add(Monsters.Values); Add([K]);
        ulong h = 14695981039346656037UL;
        foreach (char c in sb.ToString()) { h ^= c; h *= 1099511628211UL; }
        return h.ToString("X16");
    }

    /// <summary>The template for a site: the first whose kind matches and whose sub-kind is the site's, "lair" for a monster lair, or "*".</summary>
    public SiteTemplateDef? TemplateFor(Site s) => TemplateFor(s.Kind.ToString(), s.MonsterLair ? "lair" : s.SubKind, s.Kind == SiteKind.Mine ? s.Mine.ToString().ToLowerInvariant() : null);

    /// <summary>The template for a kind and sub-kind; a monster lair first tries the template of its own kind of monster (<c>lair:bog_thing</c>: P8-LM-3), then plain <c>lair</c>.</summary>
    public SiteTemplateDef? TemplateFor(string kind, string sub, string? mineState = null, string lairKind = "")
    {
        string want = mineState ?? sub;
        if (sub == "lair" && lairKind.Length > 0 && Templates.FirstOrDefault(t => t.Kind == kind && t.Sub == "lair:" + lairKind) is { } own) return own;
        return Templates.FirstOrDefault(t => t.Kind == kind && t.Sub == want) ?? Templates.FirstOrDefault(t => t.Kind == kind && t.Sub == "*")
            ?? (sub is "burned" or "flooded" or "plague" or "famine" or "collapse" ? Templates.FirstOrDefault(t => t.Kind == kind && t.Sub == "*") : null);
    }

    public SiteMonsterDef? MonsterFor(string lairKind) => Monsters.TryGetValue(lairKind, out var m) ? m : null;

    /// <summary>The CI check of the room templates (EX-3): rows rectangular, characters known, every floor reachable from a door or portal, every purpose known, every template's room ids resolve.</summary>
    public List<SiteIssue> Validate()
    {
        var issues = new List<SiteIssue>();
        const string known = "#.+<>Ho$nb^l~:v";
        foreach (var r in Rooms.Values)
        {
            var d = r.Def;
            if (d.Rows.Any(row => row.Length != r.W)) issues.Add(new(d.Id, "rows.ragged", "rows differ in width"));
            foreach (char c in r.Cells) if (!known.Contains(c)) issues.Add(new(d.Id, "rows.char", $"unknown character '{c}'"));
            foreach (var p in d.Purposes) if (!KnownPurposes.Contains(p)) issues.Add(new(d.Id, "purpose.unknown", $"no generator use for purpose '{p}'"));
            if (d.Purposes.Count == 0) issues.Add(new(d.Id, "purpose.none", "a room needs a purpose"));
            // reachability: from every door and portal cell, flood over walkable characters
            bool Walk(char c) => c is '.' or '+' or '<' or '>' or 'H' or '^' or '~' or 'l';
            var seen = new bool[r.W * r.H]; var q = new Queue<int>();
            for (int i = 0; i < r.Cells.Length; i++) if (r.Cells[i] is '+' or '<' or '>' or 'H') { seen[i] = true; q.Enqueue(i); }
            if (q.Count == 0) issues.Add(new(d.Id, "room.no_door", "no door or portal"));
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i % r.W, y = i / r.W;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy; if ((uint)nx >= r.W || (uint)ny >= r.H) continue;
                    int j = ny * r.W + nx; if (seen[j] || !Walk(r.Cells[j])) continue;
                    seen[j] = true; q.Enqueue(j);
                }
            }
            for (int i = 0; i < r.Cells.Length; i++)
            {
                char c = r.Cells[i]; int x = i % r.W, y = i / r.W;
                if (Walk(c) && !seen[i]) issues.Add(new(d.Id, "room.unreachable", $"floor at ({x},{y}) cannot be reached from a door"));
                if (c is '$' or 'n' or 'b')
                {
                    bool touch = false;
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    { int nx = x + dx, ny = y + dy; if ((uint)nx < r.W && (uint)ny < r.H && seen[ny * r.W + nx]) touch = true; }
                    if (!touch) issues.Add(new(d.Id, "anchor.unreachable", $"'{c}' at ({x},{y}) has no reachable floor beside it"));
                }
            }
            if (d.Purposes.Contains("lair") && !r.Cells.Contains('b')) issues.Add(new(d.Id, "purpose.lair", "a lair needs a bier or bed"));
            if (d.Purposes.Contains("storage") && !r.Cells.Contains('$')) issues.Add(new(d.Id, "purpose.storage", "storage needs a loot spot"));
            if (d.Purposes.Contains("burial") && !r.Cells.Contains('n') && !r.Cells.Contains('b')) issues.Add(new(d.Id, "purpose.burial", "a burial room needs a niche or bier"));
            if (d.Purposes.Contains("water") && !r.Cells.Contains('~')) issues.Add(new(d.Id, "purpose.water", "a water room needs water"));
        }
        foreach (var t in Templates)
        {
            foreach (var id in t.Rooms.Required.Concat(t.Rooms.Weighted.Select(w => w.Room)).Concat(t.Rooms.Lair is { } l ? [l] : Array.Empty<string>()))
                if (!Rooms.ContainsKey(id)) issues.Add(new(t.Id, "room.missing", $"room '{id}' does not exist"));
            foreach (var id in t.Loot.Tables) if (!Loot.ContainsKey(id)) issues.Add(new(t.Id, "loot.missing", $"loot table '{id}' does not exist"));
            foreach (var tr in t.Traps) if (!Traps.ContainsKey(tr.Id)) issues.Add(new(t.Id, "trap.missing", $"trap '{tr.Id}' does not exist"));
            foreach (var ih in t.Inhabitants) if (!Db.TryGet<FightKitDef>(ih.Role == "wolves" ? "wolf" : ih.Role == "bandits" ? "bandit_light" : ih.Role, out _)) issues.Add(new(t.Id, "kit.missing", $"no fight kit for '{ih.Role}'"));
            if (!Enum.TryParse<SiteKind>(t.Kind, out _)) issues.Add(new(t.Id, "kind.unknown", $"site kind '{t.Kind}'"));
            if (t.Layout is not ("rooms" or "cave" or "mine" or "surface")) issues.Add(new(t.Id, "layout.unknown", $"layout '{t.Layout}'"));
        }
        foreach (var tb in Loot.Values)
            foreach (var e in tb.Entries)
                if (!Items.ContainsKey(e.Item) && !Db.TryGet<WeaponShapeDef>(e.Item, out _) && !Db.TryGet<ArmourShapeDef>(e.Item, out _))
                    issues.Add(new(tb.Id, "item.missing", $"item '{e.Item}' is neither a site item, a weapon nor armour"));
        foreach (var m in Monsters.Values) if (!Db.TryGet<FightKitDef>(m.Kit, out _)) issues.Add(new(m.Id, "kit.missing", $"no fight kit '{m.Kit}'"));
        return issues;
    }

    /// <summary>The fight kit row of an inhabitant role.</summary>
    public static string KitOf(string role) => role switch { "wolves" => "wolf", "bandits" => "bandit_light", _ => role };
}
