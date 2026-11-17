using Ravensguard.Content;

namespace Ravensguard.Sim.Building;

/// <summary>A problem found in a blueprint. <see cref="Code"/> is stable ("rows.ragged", "room.not_enclosed" ...); tests match on it.</summary>
public sealed record BlueprintIssue(SourceLocation? Location, string Blueprint, string Code, string Message)
{
    public override string ToString() => $"{(Location is { } l ? l.ToString() : "?")}: [{Blueprint}] {Code}: {Message}";
}

/// <summary>A blueprint definition plus where it was defined (file/line of the entry and of each top-level property).</summary>
public sealed record BlueprintEntry(BlueprintDef Def, SourceLocation? Location, IReadOnlyDictionary<string, SourceLocation> Props)
{
    public static BlueprintEntry Of(BlueprintDef def) => new(def, null, new Dictionary<string, SourceLocation>());
    public SourceLocation? Where(string property) => Props.TryGetValue(property, out var l) ? l : Location;
}

/// <summary>Turns blueprint text (rows + legend) into a <see cref="BuildingGrid"/>, reporting every problem with the file and line.</summary>
public static class BlueprintParser
{
    public const char Empty = '_';
    public const int MinZ = -5, MaxZ = 3;

    public static BuildingGrid? Parse(BlueprintEntry entry, PieceCatalog pieces, IReadOnlyDictionary<string, CultureStyleDef> styles, List<BlueprintIssue> issues)
    {
        var def = entry.Def;
        int before = issues.Count;
        void Err(string prop, string code, string msg) => issues.Add(new(entry.Where(prop), def.Id, code, msg));

        // legend: one-character keys, ids resolve (after $wall/$floor)
        var legend = new Dictionary<char, string>();
        CultureStyleDef? style = null;
        string styleId = def.Style ?? "common";
        bool needsStyle = def.Legend.Values.Any(v => v.StartsWith('$'));
        if (needsStyle && !styles.TryGetValue(styleId, out style))
            Err("legend", "legend.unknown_style", $"legend uses a $token but culture style '{styleId}' does not exist");
        foreach (var (key, value) in def.Legend)
        {
            if (key.Length != 1) { Err("legend", "legend.bad_key", $"legend key '{key}' must be one character"); continue; }
            if (key[0] == Empty) { Err("legend", "legend.reserved_key", "'_' is reserved for empty cells"); continue; }
            string id = value;
            if (value.StartsWith('$'))
            {
                if (style == null) continue;
                var table = value switch { "$wall" => style.Walls, "$floor" => style.Floors, _ => null };
                if (table == null) { Err("legend", "legend.unknown_token", $"legend '{key}': unknown token '{value}' (use $wall or $floor)"); continue; }
                if (!table.TryGetValue(def.Wealth.ToString().ToLowerInvariant(), out id!))
                { Err("legend", "legend.style_gap", $"style '{styleId}' has no {value} for wealth {def.Wealth}"); continue; }
            }
            if (!pieces.TryGet(id, out _)) { Err("legend", "legend.unknown_piece", $"legend '{key}' -> unknown build_piece '{id}'"); continue; }
            legend[key[0]] = id;
        }

        // collect storeys
        var storeys = new List<(int Z, List<string> Rows, List<string>? Fix, string Prop)> { (0, def.Rows, def.Fixtures, "rows") };
        foreach (var l in def.Levels)
        {
            if (l.Z == 0 || l.Z < MinZ || l.Z > MaxZ) Err("levels", "levels.bad_z", $"extra level z={l.Z} must be -5..-1 or 1..3 (z=0 is 'rows')");
            else if (storeys.Any(s => s.Z == l.Z)) Err("levels", "levels.duplicate_z", $"level z={l.Z} is defined twice");
            else storeys.Add((l.Z, l.Rows, l.Fixtures, "levels"));
        }

        // size: rectangular and identical across levels
        int width = def.Rows.Count > 0 ? def.Rows[0].Length : 0, height = def.Rows.Count;
        if (width == 0 || height == 0) { Err("rows", "rows.empty", "ground level has no rows"); return null; }
        foreach (var s in storeys)
        {
            CheckRect(s.Rows, "rows");
            CheckRect(s.Fix, "fixtures");
            void CheckRect(List<string>? rows, string what)
            {
                if (rows == null) return;
                if (rows.Count != height) Err(s.Prop, "rows.size_mismatch", $"z={s.Z} {what}: {rows.Count} rows, expected {height}");
                for (int y = 0; y < rows.Count; y++)
                    if (rows[y].Length != width) Err(s.Prop, "rows.ragged", $"z={s.Z} {what} row {y}: width {rows[y].Length}, expected {width}");
            }
        }
        if (issues.Count > before) return null;

        var rooms = new Dictionary<char, string>();
        foreach (var (k, purpose) in def.Rooms)
        {
            if (k.Length != 1) { Err("rooms", "rooms.bad_key", $"room letter '{k}' must be one character"); continue; }
            if (!legend.ContainsKey(k[0])) { Err("rooms", "rooms.letter_not_in_legend", $"room letter '{k}' is not in the legend"); continue; }
            rooms[k[0]] = purpose;
        }

        var levels = new List<BuildingLevel>();
        foreach (var s in storeys)
        {
            var lv = new BuildingLevel(s.Z, width, height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    char c = s.Rows[y][x];
                    if (c == Empty) continue;
                    if (!legend.TryGetValue(c, out var id)) { Err(s.Prop, "rows.unknown_char", $"z={s.Z} row {y} col {x}: '{c}' is not in the legend"); continue; }
                    lv.Structure[lv.Idx(x, y)] = id;
                    if (rooms.ContainsKey(c))
                    {
                        if (pieces.KindOf(id) != PieceKind.Floor) Err("rooms", "rooms.letter_not_floor", $"room letter '{c}' maps to '{id}', which is not a floor");
                        lv.Letter[lv.Idx(x, y)] = c;
                    }
                }
            if (s.Fix != null)
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        char c = s.Fix[y][x];
                        if (c == Empty) continue;
                        if (!legend.TryGetValue(c, out var id)) { Err(s.Prop, "fixtures.unknown_char", $"z={s.Z} fixture row {y} col {x}: '{c}' is not in the legend"); continue; }
                        var kind = pieces.KindOf(id);
                        if (kind is not (PieceKind.Furniture or PieceKind.Workstation))
                        { Err(s.Prop, "fixtures.not_furniture", $"z={s.Z} fixture row {y} col {x}: '{id}' is not furniture or a workstation"); continue; }
                        if (pieces.KindOf(lv.Structure[lv.Idx(x, y)]) != PieceKind.Floor)
                            Err(s.Prop, "fixtures.not_on_floor", $"z={s.Z} fixture '{id}' at ({x},{y}) is not on a floor cell");
                        lv.Fixture[lv.Idx(x, y)] = id;
                    }
            levels.Add(lv);
        }
        if (issues.Count > before) return null;
        return new BuildingGrid(def.Id, width, height, rooms, levels);
    }
}
