using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.World;

namespace Ravensguard.Game.Tools.BlueprintEditor;

public enum EditLayer { Structure, Fixtures, Rooms }
public enum EditTool { Paint, Rect, Line, Flood, Erase, Eyedropper }

/// <summary>One validation finding. <see cref="Cells"/> are the cells to highlight when it is clicked.</summary>
public sealed record Problem(string Code, string Message, bool IsError, IReadOnlyList<Cell3> Cells);

/// <summary>A room the detector found, for the validation panel.</summary>
public sealed record RoomInfo(int Index, int Z, int Area, string Type, string Declared, bool Ok, IReadOnlyList<Cell3> Cells);

/// <summary>
/// Everything the dev blueprint editor does, without engine types so the self-test can drive it headless. Holds a mutable copy of one blueprint
/// (levels, fixtures, room letters), the header (<see cref="Meta"/>), undo history and the last validation. Saving goes through <see cref="BlueprintWriter"/>.
/// </summary>
public sealed class EditorModel
{
    public const int MinZ = BlueprintParser.MinZ, MaxZ = BlueprintParser.MaxZ, MaxSize = 96;

    public BuildingLibrary Lib { get; private set; }
    public BlueprintDef Meta { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public SortedDictionary<int, BuildingLevel> Levels { get; private set; } = new();
    public Dictionary<char, string> Rooms { get; private set; } = new();
    public string? FilePath { get; private set; }
    public bool Dirty { get; private set; }
    /// <summary>Bumps on every change of the drawing; the canvas redraws and the screen re-validates when it moves.</summary>
    public int Revision { get; private set; }
    public event Action? Changed;
    public string Status { get; private set; } = "";

    // tool state
    public int Z { get; private set; }
    public EditLayer Layer { get; set; } = EditLayer.Structure;
    public EditTool Tool { get; set; } = EditTool.Paint;
    public string? Piece { get; private set; }
    public char Letter { get; set; }
    public Wealth? PreviewWealth { get; private set; }
    public string? PreviewStyle { get; private set; }

    // results
    public IReadOnlyList<Problem> Problems { get; private set; } = [];
    public IReadOnlyList<RoomInfo> RoomInfos { get; private set; } = [];

    readonly List<string> _extraMods = new();
    readonly List<Snapshot> _undo = new(), _redo = new();
    Snapshot? _open;

    sealed record Snapshot(int W, int H, BlueprintDef Meta, Dictionary<int, BuildingLevel> Levels, Dictionary<char, string> Rooms);

    public EditorModel(BuildingLibrary lib)
    {
        Lib = lib;
        Meta = Blank("new_blueprint", "New blueprint");
        New("new_blueprint", 8, 6);
    }

    static BlueprintDef Blank(string id, string name) => new()
    {
        Id = id, Name = name, Wealth = Wealth.Middling, Legend = new(), Rooms = new(), Rows = new(),
    };

    public BuildingGrid Grid => new(Meta.Id, Width, Height, Rooms, Levels.Values);
    public BuildingLevel? Level(int z) => Levels.TryGetValue(z, out var l) ? l : null;
    public PieceCatalog Pieces => Lib.Pieces;

    void Raise(bool dirty = true)
    {
        Revision++;
        if (dirty) Dirty = true;
        Changed?.Invoke();
    }

    void Say(string s) => Status = s;

    /// <summary>Shows a message in the status bar (the screen's own notices).</summary>
    public void SetStatus(string s) { Status = s; Raise(false); }

    // ---- documents ----

    public void New(string id, int w, int h)
    {
        Meta = Blank(id, id.Replace('_', ' ') is { Length: > 0 } n ? char.ToUpperInvariant(n[0]) + n[1..] : id);
        Width = Math.Clamp(w, 1, MaxSize); Height = Math.Clamp(h, 1, MaxSize);
        Levels = new SortedDictionary<int, BuildingLevel> { [0] = new BuildingLevel(0, Width, Height) };
        Rooms = new();
        FilePath = null; Z = 0; Piece = null; Letter = '\0';
        _undo.Clear(); _redo.Clear(); _open = null;
        Say("New blueprint");
        Raise(false);
        Dirty = false;
    }

    /// <summary>Opens a blueprint of the library. Returns the problems when it does not parse.</summary>
    public bool Open(BlueprintEntry entry, out string error)
    {
        var issues = new List<BlueprintIssue>();
        var g = Lib.Parse(entry, issues);
        if (g == null) { error = string.Join("\n", issues); return false; }
        error = "";
        Meta = entry.Def;
        Width = g.Width; Height = g.Height;
        Levels = new SortedDictionary<int, BuildingLevel>(g.Levels.ToDictionary(p => p.Key, p => p.Value.Clone()));
        Rooms = g.Rooms.ToDictionary(p => p.Key, p => p.Value);
        FilePath = entry.Location is { } l && File.Exists(l.File) ? l.File : null;
        Z = 0; Piece = null; Letter = Rooms.Keys.FirstOrDefault();
        _undo.Clear(); _redo.Clear(); _open = null;
        Say($"Opened {entry.Def.Id}");
        Raise(false);
        Dirty = false;
        return true;
    }

    public bool Open(string id, out string error)
    {
        var e = Lib.Blueprints.FirstOrDefault(b => b.Def.Id == id);
        if (e == null) { error = $"no blueprint '{id}'"; return false; }
        return Open(e, out error);
    }

    // ---- undo ----

    Snapshot Take() => new(Width, Height, Meta,
        Levels.ToDictionary(p => p.Key, p => p.Value.Clone()), new Dictionary<char, string>(Rooms));

    void Restore(Snapshot s)
    {
        Width = s.W; Height = s.H; Meta = s.Meta;
        Levels = new SortedDictionary<int, BuildingLevel>(s.Levels.ToDictionary(p => p.Key, p => p.Value.Clone()));
        Rooms = new Dictionary<char, string>(s.Rooms);
        if (!Rooms.ContainsKey(Letter)) Letter = Rooms.Keys.FirstOrDefault();
    }

    static bool Same(Snapshot a, Snapshot b)
    {
        if (a.W != b.W || a.H != b.H || a.Levels.Count != b.Levels.Count || a.Rooms.Count != b.Rooms.Count || a.Meta != b.Meta) return false;
        foreach (var (k, v) in a.Rooms) if (!b.Rooms.TryGetValue(k, out var o) || o != v) return false;
        foreach (var (z, l) in a.Levels)
        {
            if (!b.Levels.TryGetValue(z, out var m)) return false;
            if (!l.Structure.AsSpan().SequenceEqual(m.Structure) || !l.Fixture.AsSpan().SequenceEqual(m.Fixture) || !l.Letter.AsSpan().SequenceEqual(m.Letter)) return false;
        }
        return true;
    }

    /// <summary>Starts one undoable edit (a stroke, a fill, a transform). Pair with <see cref="EndEdit"/>.</summary>
    public void BeginEdit() { _open ??= Take(); }

    public void EndEdit()
    {
        if (_open == null) return;
        var before = _open; _open = null;
        if (Same(before, Take())) return;
        _undo.Add(before);
        if (_undo.Count > 200) _undo.RemoveAt(0);
        _redo.Clear();
        Raise();
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add(Take());
        Restore(_undo[^1]); _undo.RemoveAt(_undo.Count - 1);
        Say("Undo");
        Raise();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(Take());
        Restore(_redo[^1]); _redo.RemoveAt(_redo.Count - 1);
        Say("Redo");
        Raise();
    }

    // ---- level and piece selection ----

    public void SetZ(int z)
    {
        z = Math.Clamp(z, MinZ, MaxZ);
        if (z == Z) return;
        Z = z;
        Raise(false);
    }

    public void SetPiece(string? id)
    {
        if (id != null && !Pieces.TryGet(id, out _)) { Say($"Unknown piece '{id}'"); return; }
        Piece = id;
        if (id == null) return;
        var k = Pieces.KindOf(id);
        if (k is PieceKind.Furniture or PieceKind.Workstation) Layer = EditLayer.Fixtures;
        else if (Layer != EditLayer.Structure) Layer = EditLayer.Structure;
        Raise(false);
    }

    public IEnumerable<string> PieceIds(bool furniture) =>
        Pieces.All.Where(p => (p.Kind is PieceKind.Furniture or PieceKind.Workstation) == furniture).Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal);

    /// <summary>Next piece of the palette in the current layer (for the [ ] keys).</summary>
    public void CyclePiece(int dir)
    {
        var ids = PaletteOrder().Where(i => (Pieces.KindOf(i) is PieceKind.Furniture or PieceKind.Workstation) == (Layer == EditLayer.Fixtures)).ToList();
        if (ids.Count == 0) return;
        int at = Piece == null ? -1 : ids.IndexOf(Piece);
        SetPiece(ids[((at + dir) % ids.Count + ids.Count) % ids.Count]);
    }

    public static readonly PieceKind[][] SectionKinds =
    [
        [PieceKind.Floor], [PieceKind.Wall], [PieceKind.Post], [PieceKind.Door], [PieceKind.Window],
        [PieceKind.StairsUp, PieceKind.StairsDown, PieceKind.Ladder, PieceKind.Trapdoor],
        [PieceKind.Furniture], [PieceKind.Workstation], [PieceKind.Ground],
    ];
    public static readonly string[] SectionKeys =
        ["BPEDIT_SEC_FLOORS", "BPEDIT_SEC_WALLS", "BPEDIT_SEC_POSTS", "BPEDIT_SEC_DOORS", "BPEDIT_SEC_WINDOWS", "BPEDIT_SEC_STAIRS", "BPEDIT_SEC_FURNITURE", "BPEDIT_SEC_WORKSTATIONS", "BPEDIT_SEC_GROUND"];

    public IEnumerable<BuildPieceDef> Section(int i) =>
        Pieces.All.Where(p => SectionKinds[i].Contains(p.Kind)).OrderBy(p => Array.IndexOf(SectionKinds[i], p.Kind)).ThenBy(p => p.Id, StringComparer.Ordinal);

    public IEnumerable<string> PaletteOrder() => Enumerable.Range(0, SectionKinds.Length).SelectMany(Section).Select(p => p.Id);

    // ---- drawing ----

    BuildingLevel LevelForEdit(int z)
    {
        if (!Levels.TryGetValue(z, out var l)) Levels[z] = l = new BuildingLevel(z, Width, Height);
        return l;
    }

    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    bool IsFloor(string? id) => Pieces.KindOf(id) == PieceKind.Floor;

    /// <summary>True when the active layer can take the current piece/letter; otherwise says why in <see cref="Status"/>.</summary>
    public bool CanPaint()
    {
        switch (Layer)
        {
            case EditLayer.Structure:
                if (Piece == null) { Say("Pick a piece from the palette first"); return false; }
                if (Pieces.KindOf(Piece) is PieceKind.Furniture or PieceKind.Workstation) { Say("Furniture goes on the Fixtures layer"); return false; }
                return true;
            case EditLayer.Fixtures:
                if (Piece == null || Pieces.KindOf(Piece) is not (PieceKind.Furniture or PieceKind.Workstation)) { Say("Pick furniture or a workstation first"); return false; }
                return true;
            default:
                if (Letter == '\0' || !Rooms.ContainsKey(Letter)) { Say("Add a room (letter + purpose) first"); return false; }
                return true;
        }
    }

    /// <summary>Applies the active layer's brush (or the eraser) to one cell on the current level. No undo bookkeeping: wrap in Begin/EndEdit.</summary>
    public bool Apply(int x, int y, bool erase)
    {
        if (!InBounds(x, y)) return false;
        if (!erase && !CanPaint()) return false;
        var existing = Level(Z);
        if (erase && existing == null) return false;
        var lv = LevelForEdit(Z);
        int i = lv.Idx(x, y);
        bool changed = false;
        switch (Layer)
        {
            case EditLayer.Structure:
                string? piece = erase ? null : Piece;
                if (lv.Structure[i] == piece) break;
                lv.Structure[i] = piece; changed = true;
                if (!IsFloor(piece)) { lv.Fixture[i] = null; lv.Letter[i] = '\0'; }
                break;
            case EditLayer.Fixtures:
                if (erase) { if (lv.Fixture[i] != null) { lv.Fixture[i] = null; changed = true; } break; }
                if (!IsFloor(lv.Structure[i])) { Say("Fixtures stand on floor cells"); break; }
                if (lv.Fixture[i] != Piece) { lv.Fixture[i] = Piece; changed = true; }
                break;
            default:
                if (erase) { if (lv.Letter[i] != '\0') { lv.Letter[i] = '\0'; changed = true; } break; }
                if (!IsFloor(lv.Structure[i])) { Say("Room letters go on floor cells"); break; }
                if (lv.Letter[i] != Letter) { lv.Letter[i] = Letter; changed = true; }
                break;
        }
        return changed;
    }

    public static IEnumerable<(int X, int Y)> LineCells(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            yield return (x0, y0);
            if (x0 == x1 && y0 == y1) yield break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public static IEnumerable<(int X, int Y)> RectCells(int x0, int y0, int x1, int y1, bool outline)
    {
        int ax = Math.Min(x0, x1), bx = Math.Max(x0, x1), ay = Math.Min(y0, y1), by = Math.Max(y0, y1);
        for (int y = ay; y <= by; y++)
            for (int x = ax; x <= bx; x++)
                if (!outline || x == ax || x == bx || y == ay || y == by) yield return (x, y);
    }

    public int ApplyCells(IEnumerable<(int X, int Y)> cells, bool erase)
    {
        int n = 0;
        foreach (var (x, y) in cells) if (Apply(x, y, erase)) n++;
        return n;
    }

    /// <summary>Flood fill: in the Rooms layer the connected floor of the clicked cell (walls, doors and windows stop it); otherwise the connected cells holding the same piece.</summary>
    public int Flood(int x, int y, bool erase)
    {
        if (!InBounds(x, y)) return 0;
        var lv = Level(Z) ?? LevelForEdit(Z);
        var seen = new HashSet<int>();
        var queue = new Queue<(int, int)>();
        queue.Enqueue((x, y));
        string? SameKey(int cx, int cy)
        {
            int ci = lv.Idx(cx, cy);
            return Layer switch
            {
                EditLayer.Fixtures => lv.Fixture[ci] ?? "_",
                EditLayer.Rooms => PieceCatalog.IsRoomKind(Pieces.KindOf(lv.Structure[ci]) ?? PieceKind.Ground) ? "room" : null,
                _ => lv.Structure[ci] ?? "_",
            };
        }
        var target = SameKey(x, y);
        if (target == null) { Say("Click inside a room"); return 0; }
        var cells = new List<(int, int)>();
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            if (!InBounds(cx, cy) || !seen.Add(lv.Idx(cx, cy)) || SameKey(cx, cy) != target) continue;
            cells.Add((cx, cy));
            queue.Enqueue((cx + 1, cy)); queue.Enqueue((cx - 1, cy)); queue.Enqueue((cx, cy + 1)); queue.Enqueue((cx, cy - 1));
        }
        return ApplyCells(cells, erase);
    }

    public void PickAt(int x, int y)
    {
        var lv = Level(Z);
        if (lv == null || !InBounds(x, y)) return;
        int i = lv.Idx(x, y);
        if (Layer == EditLayer.Rooms)
        {
            if (lv.Letter[i] != '\0') { Letter = lv.Letter[i]; Say($"Room {Letter}: {Rooms.GetValueOrDefault(Letter, "?")}"); Raise(false); }
            return;
        }
        var id = lv.Fixture[i] ?? lv.Structure[i];
        if (id == null) return;
        SetPiece(id);
        if (lv.Letter[i] != '\0') Letter = lv.Letter[i];
        Say($"Picked {id}");
    }

    // ---- rooms ----

    public const string LetterPool = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public char NextFreeLetter()
    {
        foreach (var c in LetterPool)
            if (!Rooms.ContainsKey(c) && !Meta.Legend.ContainsKey(c.ToString())) return c;
        foreach (var c in LetterPool) if (!Rooms.ContainsKey(c)) return c;
        return '\0';
    }

    /// <summary>Distinct purposes the room types deliver, plus the "work:*" wildcard, sorted.</summary>
    public IEnumerable<string> Purposes() =>
        Lib.RoomTypes.Select(t => t.Purpose).Append("work:*").Distinct().OrderBy(p => p, StringComparer.Ordinal);

    public char AddRoom(string purpose)
    {
        char c = NextFreeLetter();
        if (c == '\0') { Say("No free room letters"); return '\0'; }
        BeginEdit();
        Rooms[c] = purpose;
        Letter = c;
        Layer = EditLayer.Rooms;
        EndEdit();
        return c;
    }

    public void SetPurpose(char letter, string purpose)
    {
        if (!Rooms.ContainsKey(letter) || Rooms[letter] == purpose) return;
        BeginEdit(); Rooms[letter] = purpose; EndEdit();
    }

    public void DeleteRoom(char letter)
    {
        if (!Rooms.ContainsKey(letter)) return;
        BeginEdit();
        Rooms.Remove(letter);
        foreach (var lv in Levels.Values) for (int i = 0; i < lv.Letter.Length; i++) if (lv.Letter[i] == letter) lv.Letter[i] = '\0';
        if (Letter == letter) Letter = Rooms.Keys.FirstOrDefault();
        EndEdit();
    }

    // ---- whole-blueprint operations ----

    public void Resize(int w, int h, int offsetX = 0, int offsetY = 0)
    {
        w = Math.Clamp(w, 1, MaxSize); h = Math.Clamp(h, 1, MaxSize);
        if (w == Width && h == Height && offsetX == 0 && offsetY == 0) return;
        BeginEdit();
        var nl = new SortedDictionary<int, BuildingLevel>();
        foreach (var (z, lv) in Levels)
        {
            var n = new BuildingLevel(z, w, h);
            for (int y = 0; y < lv.Height; y++)
                for (int x = 0; x < lv.Width; x++)
                {
                    int nx = x + offsetX, ny = y + offsetY;
                    if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                    int s = lv.Idx(x, y), d = n.Idx(nx, ny);
                    n.Structure[d] = lv.Structure[s]; n.Fixture[d] = lv.Fixture[s]; n.Letter[d] = lv.Letter[s];
                }
            nl[z] = n;
        }
        Levels = nl; Width = w; Height = h;
        Say($"Resized to {w}x{h}");
        EndEdit();
    }

    void Transform(Func<BuildingLevel, BuildingLevel> f, bool swap, string what)
    {
        BeginEdit();
        Levels = new SortedDictionary<int, BuildingLevel>(Levels.ToDictionary(p => p.Key, p => f(p.Value)));
        if (swap) (Width, Height) = (Height, Width);
        Say(what);
        EndEdit();
    }

    public void Rotate() => Transform(l => l.Rotate90(), true, "Rotated 90 degrees clockwise");
    public void MirrorX() => Transform(l => l.MirrorX(), false, "Mirrored left-right");
    public void MirrorY() => Transform(l => l.Rotate90().Rotate90().MirrorX(), false, "Mirrored top-bottom");

    public void ClearLevel()
    {
        if (Level(Z) is not { } lv) return;
        BeginEdit();
        if (Z == 0) Levels[0] = new BuildingLevel(0, Width, Height); else Levels.Remove(Z);
        Say($"Cleared z={Z}");
        EndEdit();
    }

    /// <summary>Copies the level below onto this one (a quick start for an upper storey's walls); empty levels stay empty.</summary>
    public void CopyFromBelow()
    {
        if (Level(Z - 1) is not { } src || Z <= MinZ) return;
        BeginEdit();
        var c = src.Clone();
        Levels[Z] = new BuildingLevel(Z, Width, Height);
        Array.Copy(c.Structure, Levels[Z].Structure, c.Structure.Length);
        Array.Copy(c.Letter, Levels[Z].Letter, c.Letter.Length);
        Say($"Copied z={Z - 1} walls and floors to z={Z}");
        EndEdit();
    }

    // ---- header ----

    public void SetMeta(Func<BlueprintDef, BlueprintDef> change)
    {
        BeginEdit();
        Meta = change(Meta);
        EndEdit();
    }

    /// <summary>
    /// Changes wealth or culture style. Cells that came from a $wall/$floor legend token follow the new style; the rest stay as drawn.
    /// Done by writing the blueprint with the old header and parsing it back with the new one.
    /// </summary>
    public bool Restyle(Wealth wealth, string? style)
    {
        if (wealth == Meta.Wealth && style == Meta.Style) return true;
        BeginEdit();
        try
        {
            var def = ToDef() with { Wealth = wealth, Style = style };
            var issues = new List<BlueprintIssue>();
            var g = Lib.Parse(BlueprintEntry.Of(def), issues);
            if (g == null) { _open = null; Say("Style change failed: " + issues.FirstOrDefault()?.Message); return false; }
            Meta = def;
            Levels = new SortedDictionary<int, BuildingLevel>(g.Levels.ToDictionary(p => p.Key, p => p.Value));
            Say($"Style {style ?? "(default)"}, wealth {wealth}");
            EndEdit();
            return true;
        }
        catch (Exception ex) { _open = null; Say("Style change failed: " + ex.Message); return false; }
    }

    public void SetPreview(Wealth? wealth, string? style)
    {
        PreviewWealth = wealth; PreviewStyle = style;
        _previewKey = -1;
        Raise(false);
    }

    int _previewKey = -1; BuildingGrid? _previewGrid;

    /// <summary>The grid to draw: the real one, or (preview on) the same blueprint re-resolved under another wealth/style.</summary>
    public BuildingGrid DisplayGrid()
    {
        if (PreviewWealth == null && PreviewStyle == null) return Grid;
        if (_previewKey == Revision && _previewGrid != null) return _previewGrid;
        try
        {
            var def = ToDef() with { Wealth = PreviewWealth ?? Meta.Wealth, Style = PreviewStyle ?? Meta.Style };
            _previewGrid = Lib.Parse(BlueprintEntry.Of(def), new List<BlueprintIssue>()) ?? Grid;
        }
        catch { _previewGrid = Grid; }
        _previewKey = Revision;
        return _previewGrid;
    }

    // ---- writing and validation ----

    public BlueprintDef ToDef() => BlueprintWriter.ToDef(Meta, DroppingEmptyLevels(), Pieces, Lib.Styles);

    /// <summary>Levels above and below ground that hold nothing are not written.</summary>
    BuildingGrid DroppingEmptyLevels()
    {
        var keep = Levels.Values.Where(l => l.Z == 0 || l.Structure.Any(s => s != null) || l.Fixture.Any(f => f != null));
        return new BuildingGrid(Meta.Id, Width, Height, Rooms, keep);
    }

    public string EntryText() => BlueprintWriter.Write(Meta, DroppingEmptyLevels(), Pieces, Lib.Styles);

    static readonly Regex RoomRx = new(@"room#(\d+) z=(-?\d+)"), CellRx = new(@"\((\d+),(\d+)\)"), ZRx = new(@"z=(-?\d+)");

    /// <summary>Runs BlueprintValidator, RoomDetector and StructuralSupport on the current drawing and maps findings to cells.</summary>
    public void Validate()
    {
        var problems = new List<Problem>();
        var roomInfos = new List<RoomInfo>();
        try
        {
            if (!Regex.IsMatch(Meta.Id, "^[a-z][a-z0-9_]*$")) problems.Add(new("meta.bad_id", $"id '{Meta.Id}' must be lower_snake_case", true, []));
            if (string.IsNullOrWhiteSpace(Meta.Name)) problems.Add(new("meta.no_name", "the blueprint needs a name", true, []));
            var grid = DroppingEmptyLevels();
            var def = BlueprintWriter.ToDef(Meta, grid, Pieces, Lib.Styles);
            var rooms = Lib.Detector.Detect(grid);
            foreach (var issue in BlueprintValidator.Validate(BlueprintEntry.Of(def), Lib))
                problems.Add(new(issue.Code, issue.Message, true, CellsFor(issue.Message, rooms)));
            foreach (var r in rooms)
            {
                string declared = string.Join(",", r.DeclaredPurposes);
                roomInfos.Add(new(r.Index, r.Z, r.Area, r.Primary?.Id ?? "-", declared.Length == 0 ? "-" : declared,
                    r.DeclaredPurposes.Count == 0 || r.PurposeMatches, r.Cells.Select(c => new Cell3(c.X, c.Y, r.Z)).ToList()));
            }
        }
        catch (Exception ex) { problems.Add(new("editor.exception", ex.Message, true, [])); }
        Problems = problems; RoomInfos = roomInfos;
    }

    IReadOnlyList<Cell3> CellsFor(string message, List<DetectedRoom> rooms)
    {
        var z = ZRx.Match(message) is { Success: true } zm ? int.Parse(zm.Groups[1].Value) : Z;
        if (RoomRx.Match(message) is { Success: true } rm)
        {
            int idx = int.Parse(rm.Groups[1].Value), rz = int.Parse(rm.Groups[2].Value);
            var room = rooms.FirstOrDefault(r => r.Index == idx && r.Z == rz);
            if (room != null) return room.Cells.Select(c => new Cell3(c.X, c.Y, rz)).ToList();
        }
        if (CellRx.Match(message) is { Success: true } cm) return [new Cell3(int.Parse(cm.Groups[1].Value), int.Parse(cm.Groups[2].Value), z)];
        return [];
    }

    public bool HasErrors => Problems.Any(p => p.IsError);

    // ---- saving ----

    /// <summary>Default file for a blueprint that has none yet: the user mod folder.</summary>
    public string DefaultFile() => Path.Combine(EditorLibrary.EnsureUserMod(), "blueprints", Meta.Id + ".jsonc");

    /// <summary>
    /// Writes the blueprint into <paramref name="path"/> (replacing its entry in place when the file has one, keeping other entries and comments),
    /// then re-reads the content so the rest of the game sees it.
    /// </summary>
    public bool SaveTo(string path, out string message)
    {
        try
        {
            if (!Regex.IsMatch(Meta.Id, "^[a-z][a-z0-9_]*$")) { message = $"id '{Meta.Id}' must be lower_snake_case"; Say(message); return false; }
            path = Path.GetFullPath(path);
            string existing = File.Exists(path) ? File.ReadAllText(path) : "";
            string text = BlueprintWriter.Upsert(existing, Meta.Id, EntryText(), BlueprintWriter.DefaultHeader(Meta.Name));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            FilePath = path;
            Reload(path);
            Dirty = false;
            message = $"Saved {Meta.Id} to {EditorLibrary.Describe(path)}";
            Say(message);
            Raise(false);
            return true;
        }
        catch (Exception ex)
        {
            message = "Save failed: " + ex.Message;
            Say(message);
            return false;
        }
    }

    public bool Save(out string message) => SaveTo(FilePath ?? DefaultFile(), out message);

    /// <summary>Re-reads all content (base, user mod and the mod of the file just written) and keeps the library the screen works with up to date.</summary>
    void Reload(string writtenFile)
    {
        var root = EditorLibrary.ModRootOf(writtenFile);
        if (root != null && !_extraMods.Contains(root, StringComparer.OrdinalIgnoreCase)) _extraMods.Add(root);
        Lib = EditorLibrary.Load(EditorLibrary.ModFolders(_extraMods), out _);
        if (Lib.Blueprints.FirstOrDefault(b => b.Def.Id == Meta.Id) is { } e) Meta = e.Def;
        EditorLibrary.RaiseReloaded(Lib);
    }
}
