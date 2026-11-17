using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI;
using Ravensguard.Game.World;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.World;

namespace Ravensguard.Game.Tools.BlueprintEditor;

/// <summary>The drawing surface: the blueprint's current level drawn with the game's own sprites, the brush tools, pan and zoom, and the overlays (room letters, problems, grid).</summary>
public partial class EditorCanvas : Control
{
    public const int Tile = 32;
    static readonly float[] Zooms = [0.5f, 1f, 1.5f, 2f, 3f, 4f];

    readonly EditorModel _m;
    int _zoomIx = 3;
    Vector2 _origin = new(24, 24);
    (int X, int Y)? _hover;
    (int X, int Y)? _start, _last;
    bool _stroke, _eraseStroke, _autoFit = true;
    int _builtRevision = -1;
    HamletMap? _map;
    BuildingGrid? _drawGrid;
    HashSet<(int X, int Y)> _highlight = new();

    public bool ShowGrid { get; set; } = true;
    public bool ShowRegions { get; set; } = true;
    public (int X, int Y)? Hover => _hover;
    public event Action<(int X, int Y)?>? HoverChanged;
    public float Zoom => Zooms[_zoomIx];
    float Cell => Tile * Zoom;

    public EditorCanvas(EditorModel model)
    {
        _m = model;
        ClipContents = true;
        FocusMode = FocusModeEnum.Click;
        MouseDefaultCursorShape = CursorShape.Cross;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(320, 240);
        TextureFilter = TextureFilterEnum.Nearest;
        _m.Changed += QueueRedraw;
    }

    public override void _ExitTree() => _m.Changed -= QueueRedraw;

    /// <summary>Cells to flash on the current level (a clicked problem).</summary>
    public void Highlight(IEnumerable<(int X, int Y)> cells)
    {
        _highlight = cells.ToHashSet();
        QueueRedraw();
        if (_highlight.Count == 0) return;
        // bring the first cell into view
        var (hx, hy) = _highlight.First();
        var p = _origin + new Vector2((hx + 0.5f) * Cell, (hy + 0.5f) * Cell);
        if (!new Rect2(Vector2.Zero, Size).Grow(-Cell).HasPoint(p)) _origin += Size / 2 - p;
    }

    public void Fit()
    {
        if (Size.X < 10 || Size.Y < 10) return;
        int best = 0;
        for (int i = 0; i < Zooms.Length; i++) if (_m.Width * Tile * Zooms[i] + 48 <= Size.X && _m.Height * Tile * Zooms[i] + 48 <= Size.Y) best = i;
        _zoomIx = best;
        _origin = ((Size - new Vector2(_m.Width, _m.Height) * Cell) / 2).Floor();
        _autoFit = true;
        QueueRedraw();
    }

    /// <summary>Screen position of the middle of a cell (for synthetic input in the screenshot and tests).</summary>
    public Vector2 CellCenter(int x, int y) => GlobalPosition + _origin + new Vector2((x + 0.5f) * Cell, (y + 0.5f) * Cell);

    public void Pan(Vector2 d) { _origin += d; _autoFit = false; QueueRedraw(); }

    public void ZoomBy(int dir, Vector2? around = null)
    {
        int n = Math.Clamp(_zoomIx + dir, 0, Zooms.Length - 1);
        if (n == _zoomIx) return;
        var at = around ?? Size / 2;
        var cellPos = (at - _origin) / Cell;
        _zoomIx = n;
        _origin = at - cellPos * Cell;
        _autoFit = false;
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) { if (_autoFit) Fit(); QueueRedraw(); }
    }

    (int X, int Y) CellAt(Vector2 p) => ((int)MathF.Floor((p.X - _origin.X) / Cell), (int)MathF.Floor((p.Y - _origin.Y) / Cell));

    // ---- input ----

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            if (Input.IsMouseButtonPressed(MouseButton.Middle)) { Pan(mm.Relative); return; }
            var c = CellAt(mm.Position);
            var h = _m.InBounds(c.X, c.Y) ? c : ((int, int)?)null;
            if (h != _hover) { _hover = h; HoverChanged?.Invoke(h); QueueRedraw(); }
            if (_stroke) Drag(c);
            return;
        }
        if (e is not InputEventMouseButton mb) return;
        if (!mb.Pressed)
        {
            if (_stroke && mb.ButtonIndex is MouseButton.Left or MouseButton.Right) Finish(CellAt(mb.Position));
            return;
        }
        if (Actions.Pressed(e, "blueprinteditor.zoom_in")) { ZoomBy(+1, mb.Position); AcceptEvent(); return; }
        if (Actions.Pressed(e, "blueprinteditor.zoom_out")) { ZoomBy(-1, mb.Position); AcceptEvent(); return; }
        bool primary = Actions.Pressed(e, "blueprinteditor.primary"), secondary = Actions.Pressed(e, "blueprinteditor.secondary");
        if (!primary && !secondary) return;
        GrabFocus();
        AcceptEvent();
        var cell = CellAt(mb.Position);
        if (!_m.InBounds(cell.X, cell.Y)) return;
        Begin(cell, secondary);
    }

    bool Outline => Input.IsKeyPressed(Key.Shift);

    void Begin(( int X, int Y) cell, bool erase)
    {
        var tool = erase && _m.Tool == EditTool.Eyedropper ? EditTool.Erase : _m.Tool;
        if (tool == EditTool.Eyedropper) { _m.PickAt(cell.X, cell.Y); QueueRedraw(); return; }
        erase |= tool == EditTool.Erase;
        _m.BeginEdit();
        _stroke = true; _eraseStroke = erase; _start = _last = cell;
        switch (tool)
        {
            case EditTool.Paint or EditTool.Erase: _m.Apply(cell.X, cell.Y, erase); break;
            case EditTool.Flood: _m.Flood(cell.X, cell.Y, erase); break;
        }
        QueueRedraw();
    }

    void Drag((int X, int Y) cell)
    {
        var tool = _m.Tool;
        if (tool is EditTool.Paint or EditTool.Erase && _last is { } l && cell != l)
        {
            _m.ApplyCells(EditorModel.LineCells(l.X, l.Y, cell.X, cell.Y), _eraseStroke);
            _last = cell;
        }
        QueueRedraw();
        _lastMouse = cell;
    }
    (int X, int Y) _lastMouse;

    void Finish((int X, int Y) cell)
    {
        if (!_stroke) return;
        var tool = _m.Tool;
        if (_start is { } s)
        {
            var c = (Math.Clamp(cell.X, 0, _m.Width - 1), Math.Clamp(cell.Y, 0, _m.Height - 1));
            if (tool == EditTool.Rect) _m.ApplyCells(EditorModel.RectCells(s.X, s.Y, c.Item1, c.Item2, Outline), _eraseStroke);
            else if (tool == EditTool.Line) _m.ApplyCells(EditorModel.LineCells(s.X, s.Y, c.Item1, c.Item2), _eraseStroke);
        }
        _stroke = false; _start = _last = null;
        _m.EndEdit();
        QueueRedraw();
    }

    // ---- drawing ----

    static readonly Color Dim = new(1, 1, 1, 0.38f);

    void Rebuild()
    {
        var g = _m.DisplayGrid();
        if (ReferenceEquals(g, _drawGrid) && _builtRevision == _m.Revision && _map != null) return;
        _drawGrid = g;
        _map = EditorArt.BuildMap(g, _m.Pieces);
        _builtRevision = _m.Revision;
    }

    static Color LetterColor(char c)
    {
        float h = (c * 0.61803f) % 1f;
        return Color.FromHsv(h, 0.65f, 0.95f);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.08f, 0.07f, 0.09f));
        Rebuild();
        var map = _map!;
        int z = _m.Z;
        float s = Cell;
        var font = GetThemeDefaultFont();
        var lv = _m.Level(z);
        for (int y = 0; y < _m.Height; y++)
            for (int x = 0; x < _m.Width; x++)
            {
                var r = new Rect2(_origin + new Vector2(x * s, y * s), new Vector2(s, s));
                if (r.End.X < 0 || r.End.Y < 0 || r.Position.X > Size.X || r.Position.Y > Size.Y) continue;
                if (z > 0 && map.StructureAt(x, y, z) == null && TileSprites.Terrain(map, x, y, z - 1) is { } below && EditorArt.Texture(below) is { } bt)
                    DrawTextureRect(bt, r, false, Dim);
                if (TileSprites.Terrain(map, x, y, z) is { } t && EditorArt.Texture(t) is { } tex) DrawTextureRect(tex, r, false);
                if (TileSprites.Object(map, x, y, z) is { } o && EditorArt.Texture(o) is { } otex) DrawTextureRect(otex, r, false);
            }

        var real = _m.Grid.Level(z);
        if (ShowRegions && real != null)
            for (int y = 0; y < _m.Height; y++)
                for (int x = 0; x < _m.Width; x++)
                {
                    char c = real.Letter[real.Idx(x, y)];
                    if (c == '\0') continue;
                    var r = new Rect2(_origin + new Vector2(x * s, y * s), new Vector2(s, s));
                    var col = LetterColor(c);
                    DrawRect(r, new Color(col, c == _m.Letter && _m.Layer == EditLayer.Rooms ? 0.5f : 0.32f));
                    if (s >= 24) DrawString(font, r.Position + new Vector2(2, s - 4), c.ToString(), HorizontalAlignment.Left, s - 4, (int)Math.Max(10, s * 0.4f), new Color(col, 1).Lightened(0.4f));
                }

        // footprint frame and grid
        var area = new Rect2(_origin, new Vector2(_m.Width * s, _m.Height * s));
        if (ShowGrid && s >= 16)
        {
            var gc = new Color(1, 1, 1, 0.10f);
            for (int x = 0; x <= _m.Width; x++) DrawLine(_origin + new Vector2(x * s, 0), _origin + new Vector2(x * s, _m.Height * s), gc);
            for (int y = 0; y <= _m.Height; y++) DrawLine(_origin + new Vector2(0, y * s), _origin + new Vector2(_m.Width * s, y * s), gc);
        }
        DrawRect(area, PixelTheme.Rim, false, 2);

        // tool preview
        var brush = BrushCells().ToList();
        foreach (var (bx, by) in brush)
        {
            var r = new Rect2(_origin + new Vector2(bx * s, by * s), new Vector2(s, s));
            DrawRect(r, new Color(PixelTheme.Gold, 0.22f));
            if (_m.Tool != EditTool.Erase && !_eraseStroke && _m.Piece != null && _m.Layer != EditLayer.Rooms)
            {
                var (ti, oi) = EditorArt.Icon(_m.Pieces, _m.Piece);
                if (EditorArt.Texture(ti) is { } a) DrawTextureRect(a, r, false, new Color(1, 1, 1, 0.55f));
                if (EditorArt.Texture(oi) is { } b) DrawTextureRect(b, r, false, new Color(1, 1, 1, 0.55f));
            }
        }
        if (_hover is { } h)
            DrawRect(new Rect2(_origin + new Vector2(h.X * s, h.Y * s), new Vector2(s, s)), PixelTheme.Gold, false, 2);

        // problems
        if (_highlight.Count > 0)
        {
            float pulse = 0.5f + 0.5f * MathF.Sin(Time.GetTicksMsec() / 180f);
            foreach (var (hx, hy) in _highlight)
            {
                var r = new Rect2(_origin + new Vector2(hx * s, hy * s), new Vector2(s, s));
                DrawRect(r, new Color(PixelTheme.Warn, 0.25f + 0.25f * pulse));
                DrawRect(r, PixelTheme.Warn, false, 2);
            }
            QueueRedraw();
        }
    }

    IEnumerable<(int X, int Y)> BrushCells()
    {
        if (_hover is not { } h) yield break;
        var tool = _m.Tool;
        if (_stroke && _start is { } s && tool is EditTool.Rect or EditTool.Line)
        {
            foreach (var c in tool == EditTool.Rect ? EditorModel.RectCells(s.X, s.Y, h.X, h.Y, Outline) : EditorModel.LineCells(s.X, s.Y, h.X, h.Y)) yield return c;
        }
        else if (tool != EditTool.Eyedropper) yield return h;
    }
}
