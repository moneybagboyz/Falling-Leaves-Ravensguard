using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Game.Inputs;
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
    Vector2 _origin; // image pixel at the screen centre
    int _scale = 1;
    bool _drag;
    Vector2 _mouse;
    double _t;
    Font _font = null!;
    string _hover = "";
    int _shotWait;
    /// <summary>Dev: when set, the screen saves its own picture to this path once the map is drawn and quits.</summary>
    public string? ShotPath { get; init; }
    public int StartScale { get; init; } = 1;

    public WorldMapScreen(ScreenRouter router, RegionMap map, Func<(float X, float Y)> hero)
    {
        _router = router; _map = map; _hero = hero;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
        _font = GetThemeDefaultFont();
        _scale = Math.Clamp(StartScale, 1, 6);
        CenterOnHero();
        ClampOrigin();
    }

    void CenterOnHero()
    {
        var (x, y) = _hero();
        _origin = new Vector2(x / RegionData.TileM * RegionMap.Px, y / RegionData.TileM * RegionMap.Px);
    }

    /// <summary>Keeps the map filling the window: the view never shows past the edge of the image, and a window larger than the map centres it.</summary>
    void ClampOrigin()
    {
        float hx = Size.X / 2f / _scale, hy = Size.Y / 2f / _scale;
        float x = RegionMap.Size <= 2 * hx ? RegionMap.Size / 2f : Math.Clamp(_origin.X, hx, RegionMap.Size - hx);
        float y = RegionMap.Size <= 2 * hy ? RegionMap.Size / 2f : Math.Clamp(_origin.Y, hy, RegionMap.Size - hy);
        _origin = new Vector2(x, y);
    }

    Vector2 ToScreen(Vector2 imagePx) => Size / 2f + (imagePx - _origin) * _scale;
    Vector2 ToImage(Vector2 screen) => (screen - Size / 2f) / _scale + _origin;

    public override void _Process(double delta)
    {
        _t += delta;
        _map.Poll();
        if (!Visible) return;
        var pan = new Vector2((Actions.HeldLoose("world.camera_right") ? 1 : 0) - (Actions.HeldLoose("world.camera_left") ? 1 : 0),
            (Actions.HeldLoose("world.camera_down") ? 1 : 0) - (Actions.HeldLoose("world.camera_up") ? 1 : 0));
        if (pan != Vector2.Zero) _origin += pan * (float)(delta * 420f / _scale);
        ClampOrigin();
        QueueRedraw();
        if (ShotPath != null && _map.Texture != null && _map.Fog != null && ++_shotWait == 8)
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
        int ns = Math.Clamp(_scale + dir, 1, 6);
        if (ns == _scale) return;
        var img = ToImage(at.X > 0 || at.Y > 0 ? at : Size / 2f);
        _scale = ns;
        _origin = img - (at - Size / 2f) / _scale;
        ClampOrigin();
    }

    void UpdateHover()
    {
        var p = ToImage(_mouse);
        int tx = (int)(p.X / RegionMap.Px), ty = (int)(p.Y / RegionMap.Px);
        _hover = "";
        if (!RegionData.InBounds(tx, ty)) return;
        var r = _map.Region;
        foreach (var s in r.Settlements)
            if (Math.Abs(s.TileX - tx) <= 1 && Math.Abs(s.TileY - ty) <= 1) { _hover = $"{s.Name}  ({s.Class}, pop {s.TargetPop})"; return; }
        foreach (var s in r.Sites)
            if (Math.Abs(s.TileX - tx) <= 1 && Math.Abs(s.TileY - ty) <= 1) { _hover = _map.IsExplored(s.TileX, s.TileY) ? $"{s.Name}  ({s.Kind})" : L.T("MAP_UNKNOWN_SITE"); return; }
        int i = RegionData.Index(tx, ty);
        string what = (r.Water[i] & (WaterFlags.Sea | WaterFlags.Lake)) != 0 ? L.T("MAP_WATER") : (r.Water[i] & WaterFlags.River) != 0 ? L.T("MAP_RIVER") : r.Biomes[i].ToString();
        _hover = $"{what}  ({tx}, {ty})  {r.HeightCm[i] / 100} m";
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("141118"));
        if (_map.Texture == null) { DrawString(_font, Size / 2f - new Vector2(60, 0), L.T("MAP_BUILDING"), HorizontalAlignment.Left, -1, 16, PixelTheme.Body); return; }
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
        DrawHero();

        // frame and hints
        DrawRect(new Rect2(0, 0, Size.X, 30), new Color(0.08f, 0.07f, 0.09f, 0.88f));
        DrawString(_font, new Vector2(12, 21), L.F("MAP_TITLE", r.Seed), HorizontalAlignment.Left, -1, 16, PixelTheme.Gold);
        string hint = L.F("MAP_HINTS", H("world.camera_up") + "/" + H("world.camera_left") + "/" + H("world.camera_down") + "/" + H("world.camera_right"), H("world.zoom_in") + "/" + H("world.zoom_out"), H("world.camera_home"), H("world.map"));
        DrawRect(new Rect2(0, Size.Y - 28, Size.X, 28), new Color(0.08f, 0.07f, 0.09f, 0.88f));
        DrawString(_font, new Vector2(12, Size.Y - 9), hint, HorizontalAlignment.Left, Size.X - 260, 14, PixelTheme.Dim);
        if (_hover.Length > 0) DrawString(_font, new Vector2(Size.X - 12, Size.Y - 9), _hover, HorizontalAlignment.Right, Size.X * 0.45f, 14, PixelTheme.Body);
        // legend
        float lx = Size.X - 150, ly = 42;
        DrawRect(new Rect2(lx - 8, ly - 10, 150, 5 * 18 + 6), new Color(0.08f, 0.07f, 0.09f, 0.8f));
        foreach (var (ic, key) in new[] { (MapIcon.Town, "MAP_LEGEND_TOWN"), (MapIcon.Village, "MAP_LEGEND_VILLAGE"), (MapIcon.Ruin, "MAP_LEGEND_SITE"), (MapIcon.Unknown, "MAP_LEGEND_UNKNOWN") })
        {
            DrawTexture(PixelIcons.Map(ic), new Vector2(lx, ly - 8));
            DrawString(_font, new Vector2(lx + 18, ly + 2), L.T(key), HorizontalAlignment.Left, -1, 13, PixelTheme.Body);
            ly += 18;
        }
        DrawRect(new Rect2(lx + 2, ly - 6, 8, 8), new Color("fde27a")); DrawString(_font, new Vector2(lx + 18, ly + 2), L.T("MAP_LEGEND_YOU"), HorizontalAlignment.Left, -1, 13, PixelTheme.Body);
    }

    static string H(string id) => InputBootstrap.Hint(id).Trim('[', ']');

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
