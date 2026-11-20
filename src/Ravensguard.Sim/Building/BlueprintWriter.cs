using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ravensguard.Content;

namespace Ravensguard.Sim.Building;

/// <summary>
/// The inverse of <see cref="BlueprintParser"/> (plan §2.9, "the in-game build mode saves any structure as a blueprint in the same text format"):
/// turns a <see cref="BuildingGrid"/> plus its header (<see cref="BlueprintDef"/>) back into jsonc text the content loader reads.
/// Formatting is fixed so diffs stay clean: one entry per blueprint, one line each for header, legend and rooms, one string per row,
/// storeys in ascending z. The legend of the previous version is kept (same keys, same order, <c>$wall</c>/<c>$floor</c> tokens preserved
/// where the key still resolves to the piece); only keys the grid no longer uses are dropped and new ones are appended.
/// </summary>
public static class BlueprintWriter
{
    private const string FreeKeys = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789=-*&@^~!%;:.,<>?/";

    private sealed record Layout(List<KeyValuePair<string, string>> Legend, List<KeyValuePair<string, string>> Rooms,
        List<(int Z, List<string> Rows, List<string>? Fixtures)> Storeys);

    /// <summary>The definition that parses back to <paramref name="grid"/>: header from <paramref name="meta"/>, legend, rooms, rows, fixtures and levels from the grid.</summary>
    public static BlueprintDef ToDef(BlueprintDef meta, BuildingGrid grid, PieceCatalog pieces, IReadOnlyDictionary<string, CultureStyleDef> styles, bool pruneLegend = false)
    {
        var l = Build(meta, grid, pieces, styles, pruneLegend);
        var ground = l.Storeys.First(s => s.Z == 0);
        return meta with
        {
                        Legend = ToDictionary(l.Legend),
            Rooms = ToDictionary(l.Rooms),
            Rows = ground.Rows,
            Fixtures = ground.Fixtures,
            Levels = l.Storeys.Where(s => s.Z != 0).Select(s => new BlueprintLevelDef { Z = s.Z, Rows = s.Rows, Fixtures = s.Fixtures }).ToList(),
        };
    }

    private static Dictionary<string, string> ToDictionary(List<KeyValuePair<string, string>> l)
    {
        var d = new Dictionary<string, string>();
        foreach (var (k, v) in l) d[k] = v;
        return d;
    }

    /// <summary>One blueprint entry as indented jsonc (two spaces), no trailing comma.</summary>
    public static string Write(BlueprintDef meta, BuildingGrid grid, PieceCatalog pieces, IReadOnlyDictionary<string, CultureStyleDef> styles, bool pruneLegend = false)
    {
        var l = Build(meta, grid, pieces, styles, pruneLegend);
        string id = meta.Id;
        var head = new StringBuilder("  { \"type\": \"blueprint\", \"id\": ").Append(Q(id)).Append(", \"name\": ").Append(Q(meta.Name));
        head.Append(", \"wealth\": ").Append(Q(meta.Wealth.ToString().ToLowerInvariant()));
        if (meta.Kind == BlueprintKind.RoomTemplate) head.Append(", \"kind\": \"room_template\"");
        if (!string.IsNullOrEmpty(meta.Style)) head.Append(", \"style\": ").Append(Q(meta.Style));
        if (!string.IsNullOrEmpty(meta.Dwelling)) head.Append(", \"dwelling\": ").Append(Q(meta.Dwelling));
        if (meta.Cultures.Count > 0) head.Append(", \"cultures\": [").Append(string.Join(", ", meta.Cultures.Select(Q))).Append(']');   // P5-C: the plan library's culture filter
        head.Append(',');

        // each property is a block of lines; the last block closes the object
        var blocks = new List<List<string>>();
        blocks.Add([head.ToString()]);
        blocks.Add(["    \"legend\": { " + string.Join(", ", l.Legend.Select(p => Q(p.Key) + ": " + Q(p.Value))) + " },"]);
        if (l.Rooms.Count > 0) blocks.Add(["    \"rooms\": { " + string.Join(", ", l.Rooms.Select(p => Q(p.Key) + ": " + Q(p.Value))) + " },"]);
        var ground = l.Storeys.First(s => s.Z == 0);
        blocks.Add(RowBlock("    \"rows\": [", ground.Rows, "    ]", ","));
        if (ground.Fixtures != null) blocks.Add(RowBlock("    \"fixtures\": [", ground.Fixtures, "    ]", ","));
        var upper = l.Storeys.Where(s => s.Z != 0).ToList();
        if (upper.Count > 0)
        {
            var lines = new List<string> { "    \"levels\": [" };
            for (int i = 0; i < upper.Count; i++)
            {
                var s = upper[i];
                string end = i == upper.Count - 1 ? " }" : " },";
                lines.Add($"      {{ \"z\": {s.Z}, \"rows\": [");
                AddRows(lines, s.Rows, "        ");
                if (s.Fixtures == null) lines.Add("      ]" + end);
                else
                {
                    lines.Add("      ], \"fixtures\": [");
                    AddRows(lines, s.Fixtures, "        ");
                    lines.Add("      ]" + end);
                }
            }
            lines.Add("    ],");
            blocks.Add(lines);
        }
        var all = blocks.SelectMany(b => b).ToList();
        // the last property closes the object: drop its trailing comma and add " }"
        string last = all[^1];
        all[^1] = (last.EndsWith(',') ? last[..^1] : last) + " }";
        return string.Join("\n", all);
    }

    /// <summary>A whole content file: <paramref name="headerComment"/> lines (without "//"), then the entries separated by blank lines.</summary>
    public static string WriteFile(IEnumerable<string> headerComment, IEnumerable<string> entries)
    {
        var sb = new StringBuilder();
        foreach (var h in headerComment) sb.Append(h.Length == 0 ? "//" : "// " + h).Append('\n');
        sb.Append("[\n").Append(string.Join(",\n\n", entries)).Append("\n]\n");
        return sb.ToString();
    }

    public static string WriteFile(IEnumerable<string> headerComment, BlueprintDef meta, BuildingGrid grid, PieceCatalog pieces, IReadOnlyDictionary<string, CultureStyleDef> styles, bool pruneLegend = false) =>
        WriteFile(headerComment, [Write(meta, grid, pieces, styles, pruneLegend)]);

    /// <summary>Default header comment for a file the editor creates.</summary>
    public static IEnumerable<string> DefaultHeader(string name) =>
    [
        $"{name} (plan §2.9). Written by the dev blueprint editor (BlueprintWriter); editing by hand stays possible.",
        "Rows are text: '_' is empty, every other character is a key of the legend. Fixtures overlay furniture on floors. Extra storeys are 'levels'.",
    ];

    // ---- editing a file in place ----

    /// <summary>Byte range [Start, End) of the entry with this id in a content file, or null. Comments around the entry are outside the range.</summary>
    public static (int Start, int End)? FindEntry(string fileText, string id)
    {
        var bytes = Encoding.UTF8.GetBytes(fileText);
        foreach (var e in Entries(bytes, out _)) if (e.Id == id) return (e.Start, e.End);
        return null;
    }

    public static string EntryText(string fileText, string id)
    {
        var r = FindEntry(fileText, id);
        if (r == null) throw new KeyNotFoundException(id);
        return Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(fileText).AsSpan(r.Value.Start, r.Value.End - r.Value.Start));
    }

    /// <summary>
    /// Replaces the entry <paramref name="id"/> in an existing file with <paramref name="entryText"/> (as returned by <see cref="Write"/>), keeping every comment
    /// and every other entry byte for byte; appends it before the closing bracket when the id is not there yet; starts a new file when the text is empty.
    /// </summary>
    public static string Upsert(string fileText, string id, string entryText, IEnumerable<string>? headerForNewFile = null)
    {
        if (string.IsNullOrWhiteSpace(fileText)) return WriteFile(headerForNewFile ?? DefaultHeader(id), [entryText]);
        var bytes = Encoding.UTF8.GetBytes(fileText);
        var entries = Entries(bytes, out int lastEnd);
        string body = entryText.TrimStart();
        var hit = entries.FirstOrDefault(e => e.Id == id);
        if (hit != default && hit.Id != null)
            return Encoding.UTF8.GetString(bytes, 0, hit.Start) + body + Encoding.UTF8.GetString(bytes, hit.End, bytes.Length - hit.End);
        if (lastEnd < 0) throw new InvalidOperationException("not a content file (no entries)");
        return Encoding.UTF8.GetString(bytes, 0, lastEnd) + ",\n\n" + entryText + Encoding.UTF8.GetString(bytes, lastEnd, bytes.Length - lastEnd);
    }

    private static List<(int Start, int End, string? Id)> Entries(byte[] bytes, out int lastEnd)
    {
        var result = new List<(int, int, string?)>();
        lastEnd = -1;
        int bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        var reader = new Utf8JsonReader(bytes.AsSpan(bom), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        int start = -1; string? id = null;
        while (reader.Read())
        {
            if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.StartObject) { start = bom + (int)reader.TokenStartIndex; id = null; }
            else if (reader.CurrentDepth == 2 && reader.TokenType == JsonTokenType.PropertyName && reader.GetString() == "id")
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.String) id = reader.GetString();
            }
            else if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.EndObject && start >= 0)
            {
                int end = bom + (int)reader.TokenStartIndex + 1;
                result.Add((start, end, id));
                lastEnd = end;
                start = -1;
            }
        }
        return result;
    }

    // ---- layout ----

    private static Layout Build(BlueprintDef meta, BuildingGrid g, PieceCatalog pieces, IReadOnlyDictionary<string, CultureStyleDef> styles, bool pruneLegend)
    {
        CultureStyleDef? style = null;
        bool hasStyle = styles.TryGetValue(meta.Style ?? "common", out style);
        string wealth = meta.Wealth.ToString().ToLowerInvariant();
        string? Resolve(string value)
        {
            if (!value.StartsWith('$')) return value;
            if (!hasStyle) return null;
            var table = value switch { "$wall" => style!.Walls, "$floor" => style!.Floors, _ => null };
            return table != null && table.TryGetValue(wealth, out var id) ? id : null;
        }
        string TokenOrId(string id)
        {
            if (hasStyle)
            {
                if (style!.Walls.TryGetValue(wealth, out var w) && w == id) return "$wall";
                if (style.Floors.TryGetValue(wealth, out var f) && f == id) return "$floor";
            }
            return id;
        }

        var orig = meta.Legend.Where(p => p.Key.Length == 1 && p.Key[0] != BlueprintParser.Empty).ToList();
        var origKeys = orig.Select(p => p.Key[0]).ToHashSet();
        var taken = new Dictionary<char, string>(); // output key -> piece id
        var legend = new Dictionary<char, string>(); // output key -> value text
        var newKeys = new List<char>(); // keys not from the previous legend, in order of first use

        // 1. room letters: one piece per letter; a letter used on two different pieces gets a fresh letter for the second
        var roomOut = new List<KeyValuePair<string, string>>();
        var letterPiece = new Dictionary<(char, string), char>();
        var letterOfPiece = new Dictionary<char, string>();
        var usedLetters = new HashSet<char>(g.Rooms.Keys);
        char Fresh(IEnumerable<char> avoid)
        {
            var av = avoid.ToHashSet();
            foreach (var c in FreeKeys) if (!av.Contains(c) && !origKeys.Contains(c) && !usedLetters.Contains(c) && !taken.ContainsKey(c)) return c;
            throw new InvalidOperationException("blueprint uses more than 80 distinct room/piece keys");
        }
        var roomOrder = new List<char>(g.Rooms.Keys);
        foreach (var (z, lv) in g.Levels)
            for (int i = 0; i < lv.Structure.Length; i++)
            {
                char c = lv.Letter[i];
                var piece = lv.Structure[i];
                if (c == 0 || piece == null || !g.Rooms.ContainsKey(c) || pieces.KindOf(piece) != PieceKind.Floor) continue;
                if (letterPiece.ContainsKey((c, piece))) continue;
                char outC = c;
                if (letterOfPiece.TryGetValue(c, out var other) && other != piece)
                {
                    outC = Fresh([]);
                    usedLetters.Add(outC);
                    roomOrder.Add(outC);
                    roomOut.Add(new(outC.ToString(), g.Rooms[c]));
                }
                else letterOfPiece[c] = piece;
                letterPiece[(c, piece)] = outC;
                taken[outC] = piece;
                legend[outC] = LegendValue(outC, piece);
                if (!origKeys.Contains(outC)) newKeys.Add(outC);
            }
        string LegendValue(char key, string piece)
        {
            var o = orig.FirstOrDefault(p => p.Key[0] == key);
            return o.Key != null && Resolve(o.Value) == piece ? o.Value : TokenOrId(piece);
        }
        // declared letters that no cell uses still need a legend entry (the parser requires it)
        foreach (var (c, _) in g.Rooms)
        {
            if (taken.ContainsKey(c)) continue;
            var o = orig.FirstOrDefault(p => p.Key[0] == c);
            string value = o.Key != null ? o.Value : style != null && style.Floors.TryGetValue(wealth, out var fl) ? "$floor" : "floor_plank";
            taken[c] = Resolve(value) ?? value;
            legend[c] = value;
            if (!origKeys.Contains(c)) newKeys.Add(c);
        }
        var roomKeys = new HashSet<char>(g.Rooms.Keys.Concat(roomOut.Select(r => r.Key[0])));
        var rooms = g.Rooms.Select(p => new KeyValuePair<string, string>(p.Key.ToString(), p.Value)).Concat(roomOut).ToList();

        // 2. other pieces
        var keyOf = new Dictionary<string, char>();
        char KeyFor(string id)
        {
            if (keyOf.TryGetValue(id, out var k)) return k;
            foreach (var (ok, ov) in orig)
            {
                char c = ok[0];
                if (roomKeys.Contains(c) || (taken.TryGetValue(c, out var t) && t != id)) continue;
                if (Resolve(ov) != id) continue;
                k = c; legend[c] = ov; taken[c] = id;
                return keyOf[id] = k;
            }
            char pick = default;
            if (pieces.TryGet(id, out var p) && p.Glyph.Length == 1 && p.Glyph[0] is > ' ' and not '"' and not '\\' and not BlueprintParser.Empty
                && !taken.ContainsKey(p.Glyph[0]) && !roomKeys.Contains(p.Glyph[0]) && !origKeys.Contains(p.Glyph[0]))
                pick = p.Glyph[0];
            if (pick == default) pick = Fresh([]);
            legend[pick] = TokenOrId(id); taken[pick] = id; newKeys.Add(pick);
            return keyOf[id] = pick;
        }

        // Several legend keys can mean the same piece ("#": "$wall" and "S": "wall_stone" on a rich house). The grid cannot tell them apart,
        // so the previous rows of <meta> are a hint: a cell keeps the character it had when that character still resolves to its piece.
        char? Alias(List<string>? source, int x, int y, string piece)
        {
            if (source == null || y >= source.Count || x >= source[y].Length) return null;
            char c = source[y][x];
            if (c == BlueprintParser.Empty || roomKeys.Contains(c) || (taken.TryGetValue(c, out var t) && t != piece)) return null;
            var o = orig.FirstOrDefault(p => p.Key[0] == c);
            if (o.Key == null || Resolve(o.Value) != piece) return null;
            legend[c] = o.Value; taken[c] = piece;
            return c;
        }
        List<string>? SourceRows(int z, bool fixtures) =>
            z == 0 ? (fixtures ? meta.Fixtures : meta.Rows) : meta.Levels.FirstOrDefault(l => l.Z == z) is { } d ? (fixtures ? d.Fixtures : d.Rows) : null;

        var storeys = new List<(int, List<string>, List<string>?)>();
        foreach (var (z, lv) in g.Levels)
        {
            var rows = new List<string>();
            var fix = new List<string>();
            bool anyFixture = false;
            var srcRows = SourceRows(z, false);
            var srcFix = SourceRows(z, true);
            var line = new char[lv.Width];
            var fline = new char[lv.Width];
            for (int y = 0; y < lv.Height; y++)
            {
                for (int x = 0; x < lv.Width; x++)
                {
                    int i = lv.Idx(x, y);
                    var piece = lv.Structure[i];
                    if (piece == null) line[x] = BlueprintParser.Empty;
                    else if (lv.Letter[i] != 0 && letterPiece.TryGetValue((lv.Letter[i], piece), out var lc)) line[x] = lc;
                    else line[x] = Alias(srcRows, x, y, piece) ?? KeyFor(piece);
                    var f = lv.Fixture[i];
                    if (f == null) fline[x] = BlueprintParser.Empty;
                    else { fline[x] = Alias(srcFix, x, y, f) ?? KeyFor(f); anyFixture = true; }
                }
                rows.Add(new string(line));
                fix.Add(new string(fline));
            }
            storeys.Add((z, rows, anyFixture ? fix : null));
        }

        // 3. legend in the previous order, then new keys in order of first use
        var ordered = new List<KeyValuePair<string, string>>();
        foreach (var (k, ov) in orig)
        {
            if (legend.TryGetValue(k[0], out var v)) ordered.Add(new(k, v));
            else if (!pruneLegend) ordered.Add(new(k, ov)); // keep keys the grid no longer uses so a diff shows only real changes
        }
        foreach (var c in newKeys.Distinct()) if (legend.TryGetValue(c, out var v) && !origKeys.Contains(c)) ordered.Add(new(c.ToString(), v));
        return new Layout(ordered, rooms, storeys);
    }

    private static List<string> RowBlock(string open, List<string> rows, string close, string tail)
    {
        var l = new List<string> { open };
        AddRows(l, rows, "      ");
        l.Add(close + tail);
        return l;
    }

    private static void AddRows(List<string> lines, List<string> rows, string indent)
    {
        for (int i = 0; i < rows.Count; i++) lines.Add(indent + Q(rows[i]) + (i < rows.Count - 1 ? "," : ""));
    }

    private static string Q(string s) => "\"" + JavaScriptEncoder.UnsafeRelaxedJsonEscaping.Encode(s) + "\"";
}
