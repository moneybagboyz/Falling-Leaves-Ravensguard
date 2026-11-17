using System;
using Godot;
using Ravensguard.Game.World;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Game.UI;

/// <summary>The street view's corner minimap: the region map around the hero, 80 x 80 tiles (about 5 km), settlement dots and the hero. Clicking it opens the world map.</summary>
public partial class Minimap : Control
{
    public const int Pixels = 160;
    readonly RegionMap _map;
    readonly Func<(float X, float Y)> _hero;
    readonly Action _open;
    AtlasTexture? _view, _fogView;
    StyleBox? _frame;
    (int X0, int Y0, int HX, int HY, int Rev) _drawn = (-1, -1, -1, -1, -1);

    public Minimap(RegionMap map, Func<(float X, float Y)> hero, Action open)
    {
        _map = map; _hero = hero; _open = open;
        CustomMinimumSize = new Vector2(Pixels + 12, Pixels + 12);
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { _open(); AcceptEvent(); }
    }

    /// <summary>The view window in map pixels, even so the half-resolution fog lines up with the map exactly, and the hero's pixel in the map.</summary>
    (int X0, int Y0, float CX, float CY) Window()
    {
        var (hx, hy) = _hero();
        float cx = hx / RegionData.TileM * RegionMap.Px, cy = hy / RegionData.TileM * RegionMap.Px;
        int x0 = ((int)Math.Clamp(cx - Pixels / 2f, 0, RegionMap.Size - Pixels)) & ~1, y0 = ((int)Math.Clamp(cy - Pixels / 2f, 0, RegionMap.Size - Pixels)) & ~1;
        return (x0, y0, cx, cy);
    }

    // redraw only when what is on screen can differ: the window moved, the hero crossed a pixel, or the map image or fog changed
    public override void _Process(double delta)
    {
        _map.Poll();
        var (x0, y0, cx, cy) = Window();
        var key = (x0, y0, (int)cx, (int)cy, _map.Revision);
        if (key == _drawn) return;
        _drawn = key;
        QueueRedraw();
    }

    public override void _Draw()
    {
        _frame ??= PixelTheme.PanelBox(new Color("1d1814"), PixelTheme.Rim, 6);
        DrawStyleBox(_frame, new Rect2(Vector2.Zero, Size));
        if (_map.Texture == null) return;
        var (x0, y0, cx, cy) = Window();
        _view ??= new AtlasTexture { Atlas = _map.Texture };
        _view.Atlas = _map.Texture;
        _view.Region = new Rect2(x0, y0, Pixels, Pixels);
        var origin = new Vector2(6, 6);
        DrawTexture(_view, origin);
        if (_map.Fog != null)
        {
            _fogView ??= new AtlasTexture();
            _fogView.Atlas = _map.Fog;
            _fogView.Region = new Rect2(x0 / 2, y0 / 2, Pixels / 2, Pixels / 2);
            DrawTextureRect(_fogView, new Rect2(origin, new Vector2(Pixels, Pixels)), false);
        }
        foreach (var s in _map.Region.Settlements)
        {
            var p = origin + new Vector2(s.CenterX / (float)RegionData.TileM * RegionMap.Px - x0, s.CenterY / (float)RegionData.TileM * RegionMap.Px - y0);
            if (p.X < origin.X || p.Y < origin.Y || p.X > origin.X + Pixels || p.Y > origin.Y + Pixels) continue;
            int sz = s.Class == Ravensguard.Sim.WorldGen.Region.SettlementClass.Town ? 5 : 3;
            DrawRect(new Rect2((p - new Vector2(sz / 2f + 1, sz / 2f + 1)).Floor(), new Vector2(sz + 2, sz + 2)), PixelTheme.Border);
            DrawRect(new Rect2((p - new Vector2(sz / 2f, sz / 2f)).Floor(), new Vector2(sz, sz)), s.Class == Ravensguard.Sim.WorldGen.Region.SettlementClass.Town ? PixelTheme.Gold : new Color("e8dcc0"));
        }
        var h = (origin + new Vector2(cx - x0, cy - y0)).Floor();
        DrawRect(new Rect2(h - new Vector2(3, 3), new Vector2(7, 7)), PixelTheme.Border);
        DrawRect(new Rect2(h - new Vector2(2, 2), new Vector2(5, 5)), new Color("fde27a"));
    }
}
