using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Game.Inputs;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Game.World;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Game.UI;

/// <summary>
/// The world map (M, REDESIGN_PLAN 2.7 and 2.8): the pixel overview of the whole embark region with drawn settlement and site icons and their names, roads, rivers,
/// fields, roofs on built tiles, the hero marker and fog over the places the player has not been near. Pan with the camera keys, a drag or the edge of the
/// window; zoom with = and - (whole-number pixel steps); Home centres on the hero; M or Esc closes. Sites the hero has not been near show as a grey "?".
/// </summary>
public partial class WorldMapScreen : Control
{
    readonly ScreenRouter _router;
    readonly RegionMap _map;
    readonly Func<(float X, float Y)> _hero;
    readonly ContinentOverview? _overview;
    // continent mode (zoom out past the region map): the whole continent with the region grid and the generated regions in detail
    bool _continent;
    Vector2 _cOrigin;
    int _cScale = 1;
    Vector2 _origin; // image pixel at the screen centre
    int _scale = 1;
    bool _drag;
    Vector2 _mouse;
    double _t;
    Font _font = null!;
    string _hover = "", _titleText = "", _hintText = "";
    (MapIcon Icon, string Text)[] _legend = Array.Empty<(MapIcon, string)>();
    string _landmarkText = "", _youText = "", _buildingText = "";
    int _shotWait;
    /// <summary>Dev: when set, the screen saves its own picture to this path once the map is drawn and quits.</summary>
    public string? ShotPath { get; init; }
    public int StartScale { get; init; } = 1;

    /// <summary>The caravans on the road now as continent millimetres (lead position), asked for at every draw; null for a world without a runtime. The map shows each as a small brown wagon mark.</summary>
    public Func<IReadOnlyList<(long XMm, long YMm)>>? CaravanMarks { get; init; }
    /// <summary>Continent metres of the map region's corner.</summary>
    public (long X, long Y) RegionOrigin { get; init; }

    /// <summary>Dev: opens in the continent overview (--shot-map-scale 0).</summary>
    public bool StartContinent { get; init; }

    public WorldMapScreen(ScreenRouter router, RegionMap map, Func<(float X, float Y)> hero, ContinentOverview? overview = null)
    {
        _router = router; _map = map; _hero = hero; _overview = overview;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        _font = GetThemeDefaultFont();
        _scale = Math.Clamp(StartScale, 1, 6);
        RefreshTexts();
        InputBootstrap.Hints.Changed += RefreshTexts; // a rebind shows in the hint line
        if (StartContinent && _overview != null) { _continent = true; _cScale = Math.Clamp(StartScale, 1, 6); }
        CenterOnHero();
        ClampOrigin();
    }

    public override void _ExitTree() => InputBootstrap.Hints.Changed -= RefreshTexts;

    /// <summary>Translated strings are built here, not in every _Draw.</summary>
    void RefreshTexts()
    {
        _titleText = L.F("MAP_TITLE", _map.Region.Seed);
        _hintText = L.F("MAP_HINTS", H("world.camera_up") + "/" + H("world.camera_left") + "/" + H("world.camera_down") + "/" + H("world.camera_right"), H("world.zoom_in") + "/" + H("world.zoom_out"), H("world.camera_home"), H("world.map"));
        _legend = new[] { (MapIcon.Town, L.T("MAP_LEGEND_TOWN")), (MapIcon.Village, L.T("MAP_LEGEND_VILLAGE")), (MapIcon.Ruin, L.T("MAP_LEGEND_SITE")), (MapIcon.Unknown, L.T("MAP_LEGEND_UNKNOWN")) };
        _landmarkText = L.T("MAP_LEGEND_LANDMARK"); _youText = L.T("MAP_LEGEND_YOU"); _buildingText = L.T("MAP_BUILDING");
    }

    void CenterOnHero()
    {
        if (_continent && _overview != null) { _cOrigin = _overview.HeroPx(); return; }
        var (x, y) = _hero();
        _origin = new Vector2(x / RegionData.TileM * RegionMap.Px, y / RegionData.TileM * RegionMap.Px);
    }

    /// <summary>Keeps the map filling the window: the view never shows past the edge of the image, and a window larger than the map centres it.</summary>
    void ClampOrigin()
    {
        int sc = _continent ? _cScale : _scale; float img = _continent ? ContinentMap.Px : RegionMap.Size;
        float hx = Size.X / 2f / sc, hy = Size.Y / 2f / sc;
        float ox = _continent ? _cOrigin.X : _origin.X, oy = _continent ? _cOrigin.Y : _origin.Y;
        float x = img <= 2 * hx ? img / 2f : Math.Clamp(ox, hx, img - hx);
        float y = img <= 2 * hy ? img / 2f : Math.Clamp(oy, hy, img - hy);
        if (_continent) _cOrigin = new Vector2(x, y); else _origin = new Vector2(x, y);
    }

    Vector2 ToScreen(Vector2 imagePx) => _continent ? Size / 2f + (imagePx - _cOrigin) * _cScale : Size / 2f + (imagePx - _origin) * _scale;
    Vector2 ToImage(Vector2 screen) => _continent ? (screen - Size / 2f) / _cScale + _cOrigin : (screen - Size / 2f) / _scale + _origin;

    public override void _Process(double delta)
    {
        _t += delta;
        _map.Poll();
        if (!Visible) return;
        var pan = new Vector2((Actions.HeldLoose("world.camera_right") ? 1 : 0) - (Actions.HeldLoose("world.camera_left") ? 1 : 0),
            (Actions.HeldLoose("world.camera_down") ? 1 : 0) - (Actions.HeldLoose("world.camera_up") ? 1 : 0));
        if (pan != Vector2.Zero) { if (_continent) _cOrigin += pan * (float)(delta * 420f / _cScale); else _origin += pan * (float)(delta * 420f / _scale); }
        ClampOrigin();
        QueueRedraw();
        if (ShotPath != null && (_continent || (_map.Texture != null && _map.Fog != null)) && ++_shotWait == 8)
        {
            var img = GetViewport().GetTexture().GetImage();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ShotPath) ?? ".");
            GD.Print($"[map] screenshot {ShotPath}: {img.SavePng(ShotPath)}");
            GetTree().Quit();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (Actions.Pressed(e, "world.map")) { _router.Pop(); AcceptEvent(); return; }
        if (Actions.Pressed(e, "world.zoom_in")) { Zoom(1, _mouse); AcceptEvent(); }
        else if (Actions.Pressed(e, "world.zoom_out")) { Zoom(-1, _mouse); AcceptEvent(); }
        else if (Actions.Pressed(e, "world.camera_home")) { CenterOnHero(); ClampOrigin(); AcceptEvent(); }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex is MouseButton.Left or MouseButton.Middle) { _drag = mb.Pressed; AcceptEvent(); }
        else if (e is InputEventMouseMotion mm)
        {
            _mouse = mm.Position;
            if (_drag) { _origin -= mm.Relative / _scale; ClampOrigin(); }
            UpdateHover();
        }
    }

    void Zoom(int dir, Vector2 at)
    {
        if (_continent)
        {
            int cs = _cScale + dir;
            if (cs < 1) return;
            if (cs > 6) { _continent = false; CenterOnHero(); ClampOrigin(); return; } // zoom in past the overview: back to the map of the region
            var cimg = ToImage(at.X > 0 || at.Y > 0 ? at : Size / 2f);
            _cScale = cs;
            _cOrigin = cimg - (at - Size / 2f) / _cScale;
            ClampOrigin();
            return;
        }
        if (dir < 0 && _scale == 1 && _overview != null) { _continent = true; _cScale = Math.Clamp(Math.Max(1, (int)(Math.Min(Size.X, Size.Y - 60) / ContinentMap.Px)), 1, 6); CenterOnHero(); ClampOrigin(); return; }
        int ns = Math.Clamp(_scale + dir, 1, 6);
        if (ns == _scale) return;
        var img = ToImage(at.X > 0 || at.Y > 0 ? at : Size / 2f);
        _scale = ns;
        _origin = img - (at - Size / 2f) / _scale;
        ClampOrigin();
    }

    void UpdateHover()
    {
        if (_continent && _overview != null) { _hover = _overview.Describe(ToImage(_mouse)); return; }
        var p = ToImage(_mouse);
        int tx = (int)(p.X / RegionMap.Px), ty = (int)(p.Y / RegionMap.Px);
        _hover = "";
        if (!RegionData.InBounds(tx, ty)) return;
        var r = _map.Region;
        foreach (var s in r.Settlements)
            if (Math.Abs(s.TileX - tx) <= 1 && Math.Abs(s.TileY - ty) <= 1) { _hover = $"{s.Name}  ({s.Class}, pop {s.TargetPop})"; return; }
        foreach (var lm in r.Landmarks ?? [])
            if (Math.Abs(lm.TileX - tx) <= 1 && Math.Abs(lm.TileY - ty) <= 1 && _map.IsExplored(lm.TileX, lm.TileY)) { _hover = $"{lm.Name}  ({L.T("MAP_LEGEND_LANDMARK")})"; return; }
        foreach (var s in r.Sites)
            if (Math.Abs(s.TileX - tx) <= 1 && Math.Abs(s.TileY - ty) <= 1) { _hover = _map.IsExplored(s.TileX, s.TileY) ? $"{s.Name}  ({s.Kind})" : L.T("MAP_UNKNOWN_SITE"); return; }
        int i = RegionData.Index(tx, ty);
        string what = (r.Water[i] & (WaterFlags.Sea | WaterFlags.Lake)) != 0 ? L.T("MAP_WATER") : (r.Water[i] & WaterFlags.River) != 0 ? L.T("MAP_RIVER") : r.Biomes[i].ToString();
        _hover = $"{what}  ({tx}, {ty})  {r.HeightCm[i] / 100} m";
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("141118"));
        if (_continent && _overview != null) { DrawContinent(); return; }
        if (_map.Texture == null) { DrawString(_font, Size / 2f - new Vector2(60, 0), _buildingText, HorizontalAlignment.Left, -1, 16, PixelTheme.Body); return; }
        float s = _scale;
        var tl = ToScreen(Vector2.Zero);
        DrawTextureRect(_map.Texture, new Rect2(tl, new Vector2(RegionMap.Size, RegionMap.Size) * s), false);
        if (_map.Fog != null) DrawTextureRect(_map.Fog, new Rect2(tl, new Vector2(RegionMap.Size, RegionMap.Size) * s), false);
        DrawRect(new Rect2(tl, new Vector2(RegionMap.Size, RegionMap.Size) * s), PixelTheme.Rim, false, 2f);

        var r = _map.Region;
        var view = new Rect2(-20, -20, Size.X + 40, Size.Y + 40);
        // sites first, then settlements over them
        foreach (var site in r.Sites)
        {
            var pos = ToScreen(new Vector2(site.X / (float)RegionData.TileM * RegionMap.Px, site.Y / (float)RegionData.TileM * RegionMap.Px));
            if (!view.HasPoint(pos)) continue;
            bool seen = _map.IsExplored(site.TileX, site.TileY);
            var icon = PixelIcons.Map(seen ? RegionMap.IconOf(site.Kind) : MapIcon.Unknown);
            DrawIcon(icon, pos, seen ? 1f : 0.7f);
            if (seen && _scale >= 2) Label(site.Name, pos + new Vector2(0, 14), 12, PixelTheme.Body);
        }
        // authored landmarks show once the hero has been near them (the same explored tiles the fog uses)
        foreach (var lm in r.Landmarks ?? [])
        {
            if (!_map.IsExplored(lm.TileX, lm.TileY)) continue;
            var pos = ToScreen(new Vector2(lm.X / (float)RegionData.TileM * RegionMap.Px, lm.Y / (float)RegionData.TileM * RegionMap.Px)).Floor();
            if (!view.HasPoint(pos)) continue;
            DrawLandmark(pos);
            if (_scale >= 2) Label(lm.Name, pos + new Vector2(0, 14), 12, PixelTheme.Gold);
        }
        foreach (var st in r.Settlements)
        {
            var pos = ToScreen(new Vector2(st.CenterX / (float)RegionData.TileM * RegionMap.Px, st.CenterY / (float)RegionData.TileM * RegionMap.Px));
            if (!view.HasPoint(pos)) continue;
            var icon = PixelIcons.Map(st.Class == SettlementClass.Town ? MapIcon.Town : st.Class == SettlementClass.Village ? MapIcon.Village : MapIcon.Farmstead);
            DrawIcon(icon, pos, 1f, st.Class == SettlementClass.Town ? 2 : 1);
            if (st.Class == SettlementClass.Town) Label(st.Name, pos + new Vector2(0, 20), 16, PixelTheme.Gold);
            else if (st.Class == SettlementClass.Village && _scale >= 2) Label(st.Name, pos + new Vector2(0, 14), 14, PixelTheme.Body);
            else if (_scale >= 4) Label(st.Name, pos + new Vector2(0, 14), 12, PixelTheme.Dim);
        }
        DrawCaravans();
        DrawHero();

        // frame and hints
        DrawRect(new Rect2(0, 0, Size.X, 30), new Color(0.08f, 0.07f, 0.09f, 0.88f));
        DrawString(_font, new Vector2(12, 21), _titleText, HorizontalAlignment.Left, -1, 16, PixelTheme.Gold);
        DrawRect(new Rect2(0, Size.Y - 28, Size.X, 28), new Color(0.08f, 0.07f, 0.09f, 0.88f));
        // the hint takes the left part of the bar; the hover text the right part, never over it
        float hoverW = _hover.Length > 0 ? Math.Min(Size.X * 0.4f, _font.GetStringSize(_hover, HorizontalAlignment.Left, -1, 14).X) : 0;
        DrawString(_font, new Vector2(12, Size.Y - 9), _hintText, HorizontalAlignment.Left, Math.Max(60, Size.X - 36 - hoverW), 14, PixelTheme.Dim);
        if (_hover.Length > 0) DrawString(_font, new Vector2(Size.X - 12, Size.Y - 9), _hover, HorizontalAlignment.Right, hoverW + 2, 14, PixelTheme.Body);
        // legend
        float lx = Size.X - 150, ly = 42;
        DrawRect(new Rect2(lx - 8, ly - 10, 150, 6 * 18 + 6), new Color(0.08f, 0.07f, 0.09f, 0.8f));
        foreach (var (ic, text) in _legend)
        {
            DrawTexture(PixelIcons.Map(ic), new Vector2(lx, ly - 8));
            DrawString(_font, new Vector2(lx + 18, ly + 2), text, HorizontalAlignment.Left, -1, 13, PixelTheme.Body);
            ly += 18;
        }
        DrawLandmark(new Vector2(lx + 6, ly - 2)); DrawString(_font, new Vector2(lx + 18, ly + 2), _landmarkText, HorizontalAlignment.Left, -1, 13, PixelTheme.Body);
        ly += 18;
        DrawRect(new Rect2(lx + 2, ly - 6, 8, 8), new Color("fde27a")); DrawString(_font, new Vector2(lx + 18, ly + 2), _youText, HorizontalAlignment.Left, -1, 13, PixelTheme.Body);
    }

    /// <summary>The continent overview: the pixel map, the region grid, the generated regions in detail, the hero.</summary>
    void DrawCaravans()
    {
        if (CaravanMarks == null) return;
        var view = new Rect2(-20, -20, Size.X + 40, Size.Y + 40);
        foreach (var (x, y) in CaravanMarks())
        {
            float rx = (x / 1000f - RegionOrigin.X), ry = (y / 1000f - RegionOrigin.Y);
            if (rx < 0 || ry < 0 || rx > RegionData.Meters || ry > RegionData.Meters) continue;
            var pos = ToScreen(new Vector2(rx / RegionData.TileM * RegionMap.Px, ry / RegionData.TileM * RegionMap.Px)).Floor();
            if (!view.HasPoint(pos)) continue;
            DrawRect(new Rect2(pos.X - 3, pos.Y - 2, 7, 5), PixelTheme.Border);
            DrawRect(new Rect2(pos.X - 2, pos.Y - 1, 5, 3), new Color("c9873c"));
        }
    }

    void DrawContinent()
    {
        var ov = _overview!;
        ov.Draw(this, ToScreen, _cScale, _font, new Rect2(0, 0, Size.X, Size.Y));
        var hp = ToScreen(ov.HeroPx()).Floor();
        for (int d = -4; d <= 4; d++) DrawRect(new Rect2(hp.X - (4 - Math.Abs(d)) - 1, hp.Y + d, 2 * (4 - Math.Abs(d)) + 2, 1), PixelTheme.Border);
        for (int d = -3; d <= 3; d++) DrawRect(new Rect2(hp.X - (3 - Math.Abs(d)), hp.Y + d, 2 * (3 - Math.Abs(d)) + 1, 1), new Color("fde27a"));
        DrawRect(new Rect2(0, 0, Size.X, 30), new Color(0.08f, 0.07f, 0.09f, 0.88f));
        DrawString(_font, new Vector2(12, 21), L.F("MAP_CONTINENT_TITLE", ov.Regions.Ready.Count), HorizontalAlignment.Left, -1, 16, PixelTheme.Gold);
        DrawRect(new Rect2(0, Size.Y - 28, Size.X, 28), new Color(0.08f, 0.07f, 0.09f, 0.88f));
        float hoverW = _hover.Length > 0 ? Math.Min(Size.X * 0.5f, _font.GetStringSize(_hover, HorizontalAlignment.Left, -1, 14).X) : 0;
        DrawString(_font, new Vector2(12, Size.Y - 9), _hintText, HorizontalAlignment.Left, Math.Max(60, Size.X - 36 - hoverW), 14, PixelTheme.Dim);
        if (_hover.Length > 0) DrawString(_font, new Vector2(Size.X - 12, Size.Y - 9), _hover, HorizontalAlignment.Right, hoverW + 2, 14, PixelTheme.Body);
    }

    static string H(string id) => InputBootstrap.Hint(id).Trim('[', ']');

    /// <summary>The map mark of an authored landmark: a small gold diamond with a dark edge.</summary>
    void DrawLandmark(Vector2 pos)
    {
        for (int d = -4; d <= 4; d++) { int w = 4 - Math.Abs(d); DrawRect(new Rect2(pos.X - w - 1, pos.Y + d, 2 * w + 2, 1), PixelTheme.Border); }
        for (int d = -3; d <= 3; d++) { int w = 3 - Math.Abs(d); DrawRect(new Rect2(pos.X - w, pos.Y + d, 2 * w + 1, 1), PixelTheme.Gold); }
    }

    void DrawIcon(Texture2D tex, Vector2 centre, float alpha, int mult = 1)
    {
        var size = tex.GetSize() * mult;
        DrawTextureRect(tex, new Rect2((centre - size / 2f).Floor(), size), false, new Color(1, 1, 1, alpha));
    }

    void Label(string text, Vector2 at, int size, Color c)
    {
        var w = _font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var p = (at - new Vector2(w / 2f, 0)).Floor();
        DrawString(_font, p + new Vector2(1, 1), text, HorizontalAlignment.Left, -1, size, PixelTheme.Border);
        DrawString(_font, p + new Vector2(-1, 1), text, HorizontalAlignment.Left, -1, size, PixelTheme.Border);
        DrawString(_font, p, text, HorizontalAlignment.Left, -1, size, c);
    }

    void DrawHero()
    {
        var (x, y) = _hero();
        var pos = ToScreen(new Vector2(x / RegionData.TileM * RegionMap.Px, y / RegionData.TileM * RegionMap.Px)).Floor();
        float pulse = (float)(0.5 + 0.5 * Math.Sin(_t * 5));
        int rr = 7 + (int)(pulse * 2);
        var gold = new Color("fde27a"); var dark = PixelTheme.Border;
        // a ring that breathes, a dark-edged gold diamond in the middle
        for (int a = 0; a < 40; a++)
        {
            double t = a / 40.0 * Math.PI * 2;
            DrawRect(new Rect2(pos.X + (int)Math.Round(Math.Cos(t) * rr), pos.Y + (int)Math.Round(Math.Sin(t) * rr), 2, 2), gold);
        }
        for (int d = -4; d <= 4; d++)
        {
            int w = 4 - Math.Abs(d);
            DrawRect(new Rect2(pos.X - w - 1, pos.Y + d, 2 * w + 2, 1), dark);
        }
        for (int d = -3; d <= 3; d++)
        {
            int w = 3 - Math.Abs(d);
            DrawRect(new Rect2(pos.X - w, pos.Y + d, 2 * w + 1, 1), gold);
        }
    }
}
