using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>Mouse surface over the map: reports the hovered cell and clicks, draws the hovered and chosen 16 x 16 squares.</summary>
public partial class MapOverlay : Control
{
    public event Action<Vector2I?>? Hover;
    public event Action<Vector2I>? Click;
    public Rect2I? HoverRect, LockedRect;
    public bool HoverAllowed = true;

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m) Hover?.Invoke(Cell(m.Position));
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b) Click?.Invoke(Cell(b.Position));
    }

    static Vector2I Cell(Vector2 p) => new(Math.Clamp((int)p.X / ContinentMap.Scale, 0, Continent.Size - 1), Math.Clamp((int)p.Y / ContinentMap.Scale, 0, Continent.Size - 1));

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit) Hover?.Invoke(null);
    }

    static void Frame(CanvasItem c, Rect2I r, Color col)
    {
        var rect = new Rect2(r.Position.X * ContinentMap.Scale, r.Position.Y * ContinentMap.Scale, r.Size.X * ContinentMap.Scale, r.Size.Y * ContinentMap.Scale);
        c.DrawRect(rect.Grow(1), new Color(0, 0, 0, 0.9f), false, 2);
        c.DrawRect(rect, col, false, 2);
    }

    public override void _Draw()
    {
        if (LockedRect is { } l) Frame(this, l, PixelTheme.Gold);
        if (HoverRect is { } h && h != LockedRect) Frame(this, h, HoverAllowed ? new Color("f4efe0") : new Color("d96a4a"));
    }
}

/// <summary>
/// The embark map (plan §1A Stage 3): the continent as a pixel map with layer and marker toggles; hover a 16 x 16 cell square (32 km) to see its
/// EmbarkSite score breakdown; legend markers list battlefields and ruins; disallowed squares are greyed with their reason. Confirm picks the region.
/// </summary>
public partial class EmbarkMapScreen : Control
{
    readonly ScreenRouter _router;
    readonly NewGameDraft _d;
    readonly LoadedWorld _w;
    readonly Continent _c;
    readonly WorldHistory _h;
    readonly List<LegendMarker> _markers;
    readonly Dictionary<int, List<LegendMarker>> _markerAt = new();
    readonly Dictionary<(int, int), EmbarkScore> _scores = new();
    MapLayer _layer = MapLayer.Realm;
    bool _rivers = true, _towns = true, _legends = true;
    TextureRect _map = null!, _mask = null!;
    MapOverlay _over = null!;
    RichTextLabel _info = null!;
    Label _cellInfo = null!;
    PanelContainer _tip = null!;
    Label _tipText = null!;
    Button _confirm = null!;
    readonly Dictionary<MapLayer, Button> _layerButtons = new();
    Button _bRivers = null!, _bTowns = null!, _bLegends = null!;
    EmbarkScore? _locked, _auto, _shown;
    Vector2I? _hoverCell;
    Task<(bool[] Mask, EmbarkScore Auto)>? _task;

    public EmbarkMapScreen(ScreenRouter router, NewGameDraft d, LoadedWorld w)
    {
        _router = router; _d = d; _w = w; _c = w.Continent; _h = w.History;
        _markers = ContinentMap.Markers(_h);
        foreach (var m in _markers) { if (!_markerAt.TryGetValue(m.Cell, out var l)) _markerAt[m.Cell] = l = new(); l.Add(m); }
    }

    public override void _Ready()
    {
        var margin = UiKit.Margin(this, 8);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        margin.AddChild(row);

        var holder = new Control { CustomMinimumSize = new Vector2(ContinentMap.Px, ContinentMap.Px) };
        _map = new TextureRect { StretchMode = TextureRect.StretchModeEnum.Keep, TextureFilter = TextureFilterEnum.Nearest, MouseFilter = MouseFilterEnum.Ignore };
        _mask = new TextureRect { StretchMode = TextureRect.StretchModeEnum.Keep, TextureFilter = TextureFilterEnum.Nearest, MouseFilter = MouseFilterEnum.Ignore };
        _over = new MapOverlay { MouseFilter = MouseFilterEnum.Stop };
        foreach (var c in new Control[] { _map, _mask, _over }) { c.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); holder.AddChild(c); }
        var frame = UiKit.Panel(holder);
        frame.AddThemeStyleboxOverride("panel", PixelTheme.PanelBox(new Color("141118"), PixelTheme.Rim, 2));
        row.AddChild(frame);
        _over.Hover += OnHover;
        _over.Click += OnClick;

        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 6);
        row.AddChild(v);
        v.AddChild(UiKit.Title(L.T("EM_TITLE")));
        v.AddChild(UiKit.Hint(L.T("EM_HELP"), true));

        v.AddChild(UiKit.Hint(L.T("EM_LAYERS")));
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        v.AddChild(grid);
        int n = 1;
        foreach (var layer in Enum.GetValues<MapLayer>())
        {
            var b = UiKit.Key(L.T("LAYER_" + layer.ToString().ToUpperInvariant()), "menu.overlay_" + n++, 0, 28);
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var ly = layer;
            b.Pressed += () => SetLayer(ly);
            _layerButtons[layer] = b;
            grid.AddChild(b);
        }
        _bRivers = UiKit.Key(L.T("LAYER_RIVERS"), "menu.toggle_rivers", 0, 28); _bRivers.Pressed += () => { _rivers = !_rivers; Redraw(); };
        _bTowns = UiKit.Key(L.T("LAYER_TOWNS"), "menu.toggle_towns", 0, 28); _bTowns.Pressed += () => { _towns = !_towns; Redraw(); };
        _bLegends = UiKit.Key(L.T("LAYER_LEGENDS"), "menu.toggle_legends", 0, 28); _bLegends.Pressed += () => { _legends = !_legends; Redraw(); };
        foreach (var b in new[] { _bRivers, _bTowns, _bLegends }) { b.SizeFlagsHorizontal = SizeFlags.ExpandFill; grid.AddChild(b); }

        _cellInfo = new Label { ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, ThemeTypeVariation = "HintLabel" };
        v.AddChild(_cellInfo);
        _info = new RichTextLabel { BbcodeEnabled = true, ScrollActive = true, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var ip = UiKit.Panel(_info);
        ip.SizeFlagsVertical = SizeFlags.ExpandFill;
        v.AddChild(ip);

        var legends = UiKit.Key(L.T("EM_LEGENDS"), "menu.legends", 0, 30);
        legends.Pressed += () => _router.Push(new LegendsScreen(_router, _w));
        v.AddChild(legends);
        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 6);
        var back = UiKit.Key(L.T("UI_BACK"), "menu.back", 0, 32);
        back.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        back.Pressed += _router.Pop;
        var auto = UiKit.Key(L.T("EM_AUTO"), "menu.auto_pick", 0, 32);
        auto.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        auto.Pressed += AutoPick;
        _confirm = UiKit.Key(L.T("EM_CONFIRM"), "menu.accept", 0, 32);
        _confirm.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _confirm.Disabled = true;
        _confirm.Pressed += Confirm;
        bottom.AddChild(back); bottom.AddChild(auto); bottom.AddChild(_confirm);
        v.AddChild(bottom);

        _tip = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Ignore, ZIndex = 50 };
        _tipText = new Label { MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) };
        _tip.AddChild(_tipText);
        AddChild(_tip);

        Redraw();
        ShowInfo(null);
        // Score every second window once, in the background: the grey mask and the auto pick come from it.
        var cont = _c; var hist = _h;
        _task = Task.Run(() =>
        {
            int ws = Continent.Size - EmbarkSite.Window;
            var win = new bool[(ws / 2 + 1) * (ws / 2 + 1)];
            int stride = ws / 2 + 1;
            for (int wy = 0; wy <= ws / 2; wy++)
                for (int wx = 0; wx <= ws / 2; wx++) win[wy * stride + wx] = EmbarkSite.Score(cont, hist, wx * 2, wy * 2).Excluded;
            var mask = new bool[Continent.Cells];
            for (int y = 0; y < Continent.Size; y++)
                for (int x = 0; x < Continent.Size; x++)
                {
                    int cx = Math.Clamp(x - 8, 0, ws), cy = Math.Clamp(y - 8, 0, ws);
                    mask[y * Continent.Size + x] = win[Math.Min(cy / 2, ws / 2) * stride + Math.Min(cx / 2, ws / 2)];
                }
            return (mask, EmbarkSite.AutoPick(cont, hist));
        });
    }

    public override void _Process(double delta)
    {
        if (_task is { IsCompleted: true } t)
        {
            _task = null;
            if (t.IsCompletedSuccessfully)
            {
                _mask.Texture = UiKit.Tex(ContinentMap.RenderExclusion(t.Result.Mask), ContinentMap.Px, ContinentMap.Px);
                _auto = t.Result.Auto;
                if (_locked == null) Lock(_auto);
            }
            else GD.PushWarning("embark scoring failed: " + t.Exception);
        }
        if (_tip.Visible) _tip.Position = (GetViewport().GetMousePosition() + new Vector2(14, 14)).Min(GetViewportRect().Size - _tip.Size);
    }

    public void DebugSetLayer(int n) => SetLayer((MapLayer)Math.Clamp(n - 1, 0, 4));
    public void DebugHover(Vector2I cell) { OnHover(cell); }

    void SetLayer(MapLayer l) { _layer = l; Redraw(); }

    void Redraw()
    {
        var bytes = ContinentMap.Render(_c, _h, new MapOptions(_layer, _rivers, _towns, _legends));
        _map.Texture = UiKit.Tex(bytes, ContinentMap.Px, ContinentMap.Px);
        foreach (var (l, b) in _layerButtons) b.AddThemeColorOverride("font_color", l == _layer ? PixelTheme.Gold : PixelTheme.Body);
        foreach (var (b, on) in new[] { (_bRivers, _rivers), (_bTowns, _towns), (_bLegends, _legends) }) b.AddThemeColorOverride("font_color", on ? PixelTheme.Gold : PixelTheme.Dim);
    }

    EmbarkScore ScoreAt(Vector2I cell)
    {
        int ws = Continent.Size - EmbarkSite.Window;
        var key = (Math.Clamp(cell.X - 8, 0, ws), Math.Clamp(cell.Y - 8, 0, ws));
        if (!_scores.TryGetValue(key, out var s)) _scores[key] = s = EmbarkSite.Score(_c, _h, key.Item1, key.Item2);
        return s;
    }

    static Rect2I RectOf(EmbarkScore s) => new(s.CellX, s.CellY, EmbarkSite.Window, EmbarkSite.Window);

    void OnHover(Vector2I? cell)
    {
        _hoverCell = cell;
        if (cell is not { } c) { _over.HoverRect = null; _over.QueueRedraw(); _tip.Visible = false; ShowInfo(_locked); return; }
        var s = ScoreAt(c);
        _over.HoverRect = RectOf(s); _over.HoverAllowed = !s.Excluded; _over.QueueRedraw();
        ShowInfo(s);
        _cellInfo.Text = CellLine(c);
        // tooltip: legend markers within one cell of the pointer
        var lines = new List<string>();
        if (_legends)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = c.X + dx, y = c.Y + dy;
                    if ((uint)x < Continent.Size && (uint)y < Continent.Size && _markerAt.TryGetValue(y * Continent.Size + x, out var l)) lines.AddRange(l.Select(m => m.Text));
                }
        _tip.Visible = lines.Count > 0;
        if (lines.Count > 0) _tipText.Text = string.Join("\n", lines.Take(5));
    }

    void OnClick(Vector2I cell)
    {
        var s = ScoreAt(cell);
        if (s.Excluded) { _router.ShowToast(L.F("EM_NOT_ALLOWED", UiKit.Reason(s.Reason))); return; }
        Lock(s);
    }

    void Lock(EmbarkScore s)
    {
        _locked = s;
        _over.LockedRect = RectOf(s); _over.QueueRedraw();
        _confirm.Disabled = s.Excluded;
        if (_hoverCell == null) ShowInfo(s);
    }

    void AutoPick() { if (_auto != null) Lock(_auto); else _router.ShowToast(L.T("EM_WAIT")); }

    void Confirm()
    {
        if (_locked == null || _locked.Excluded) return;
        _d.Embark = _locked;
        _router.Replace(new RegionBuildScreen(_router, _d, _w));
    }

    string CellLine(Vector2I c)
    {
        int i = c.Y * Continent.Size + c.X;
        if (_c.Ocean[i]) return L.T("BIOME_SEA");
        string realm = _h.FinalOwner.Length > i && _h.FinalOwner[i] != Continent.None && _h.FinalOwner[i] < _h.Realms.Count ? _h.Realms[_h.FinalOwner[i]].Name : L.T("EM_NO_REALM");
        string culture = _c.CultureId[i] != Continent.None && _c.CultureId[i] < _c.Cultures.Count ? _c.Cultures[_c.CultureId[i]].Name : "-";
        return L.F("EM_CELL", L.T("BIOME_" + _c.Biomes[i].ToString().ToUpperInvariant()), realm, culture, _c.Savagery[i]);
    }

    void ShowInfo(EmbarkScore? s)
    {
        _shown = s;
        if (s == null) { _info.Text = L.T("EM_HOVER_HELP"); return; }
        string gold = PixelTheme.Gold.ToHtml(false), dim = PixelTheme.Dim.ToHtml(false), warn = PixelTheme.Warn.ToHtml(false), good = "8fc46a";
        var sb = new StringBuilder();
        sb.Append($"[color=#{dim}]{L.F("EM_SQUARE", s.CellX, s.CellY)}[/color]");
        if (_locked != null && s.CellX == _locked.CellX && s.CellY == _locked.CellY) sb.Append($"  [color=#{gold}]{L.T("EM_CHOSEN")}[/color]");
        sb.Append('\n');
        if (s.Excluded)
        {
            sb.Append($"[color=#{warn}]{L.F("EM_NOT_ALLOWED", UiKit.Reason(s.Reason))}[/color]\n");
        }
        else
        {
            sb.Append($"[color=#{gold}]{L.F("EM_SCORE", s.Total)}[/color]\n");
            foreach (var p in s.Parts)
                sb.Append($"[color=#{(p.Points >= 0 ? good : warn)}]{(p.Points >= 0 ? "+" : "")}{p.Points}[/color] {L.T("SCORE_" + p.Key.ToUpperInvariant())}\n");
        }
        sb.Append('\n');
        sb.Append(L.F("EM_COUNTS", s.Towns, s.Villages, s.Ruins, s.RiverCells, s.BiomeCount, s.LandPct, s.Population.ToString("N0"))).Append('\n');
        sb.Append(s.NearestWarKm < 0 ? L.T("EM_NO_WAR") : L.F("EM_WAR_KM", s.NearestWarKm)).Append('\n');
        var realms = new SortedSet<int>();
        for (int y = 0; y < EmbarkSite.Window; y++)
            for (int x = 0; x < EmbarkSite.Window; x++)
            {
                int i = (s.CellY + y) * Continent.Size + s.CellX + x;
                if (_h.FinalOwner.Length > i && _h.FinalOwner[i] != Continent.None && !_c.Ocean[i]) realms.Add(_h.FinalOwner[i]);
            }
        if (realms.Count > 0) sb.Append(L.T("EM_REALMS")).Append(' ').Append(string.Join(", ", realms.Where(r => r < _h.Realms.Count).Select(r => _h.Realms[r].Name))).Append('\n');
        var here = _markers.Where(m => m.Cell % Continent.Size >= s.CellX && m.Cell % Continent.Size < s.CellX + EmbarkSite.Window
            && m.Cell / Continent.Size >= s.CellY && m.Cell / Continent.Size < s.CellY + EmbarkSite.Window).Take(6).ToList();
        if (here.Count > 0)
        {
            sb.Append($"\n[color=#{dim}]{L.T("EM_LEGENDS_HERE")}[/color]\n");
            foreach (var m in here) sb.Append("- ").Append(m.Text).Append('\n');
        }
        _info.Text = sb.ToString();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        int n = 0;
        foreach (var l in Enum.GetValues<MapLayer>())
            if (Actions.Pressed(e, "menu.overlay_" + ++n)) { SetLayer(l); GetViewport().SetInputAsHandled(); return; }
        if (Actions.Pressed(e, "menu.overlay_next")) SetLayer((MapLayer)(((int)_layer + 1) % 5));
        else if (Actions.Pressed(e, "menu.toggle_rivers")) { _rivers = !_rivers; Redraw(); }
        else if (Actions.Pressed(e, "menu.toggle_towns")) { _towns = !_towns; Redraw(); }
        else if (Actions.Pressed(e, "menu.toggle_legends")) { _legends = !_legends; Redraw(); }
        else if (Actions.Pressed(e, "menu.auto_pick")) AutoPick();
        else if (Actions.Pressed(e, "menu.legends")) _router.Push(new LegendsScreen(_router, _w));
        else if (Actions.Pressed(e, "menu.accept")) Confirm();
        else return;
        GetViewport().SetInputAsHandled();
    }
}
