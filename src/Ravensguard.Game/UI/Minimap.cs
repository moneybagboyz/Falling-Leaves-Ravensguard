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
    AtlasTexture? _view;

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

    public override void _Process(double delta) { _map.Poll(); QueueRedraw(); }

    public override void _Draw()
    {
        DrawStyleBox(PixelTheme.PanelBox(new Color("1d1814"), PixelTheme.Rim, 6), new Rect2(Vector2.Zero, Size));
        if (_map.Texture == null) return;
        var (hx, hy) = _hero();
        float cx = hx / RegionData.TileM * RegionMap.Px, cy = hy / RegionData.TileM * RegionMap.Px;
        float x0 = Math.Clamp(cx - Pixels / 2f, 0, RegionMap.Size - Pixels), y0 = Math.Clamp(cy - Pixels / 2f, 0, RegionMap.Size - Pixels);
        _view ??= new AtlasTexture { Atlas = _map.Texture };
        _view.Atlas = _map.Texture;
        _view.Region = new Rect2((int)x0, (int)y0, Pixels, Pixels);
        var origin = new Vector2(6, 6);
        DrawTexture(_view, origin);
        if (_map.Fog != null)
        {
            var fogView = new AtlasTexture { Atlas = _map.Fog, Region = new Rect2((int)x0 / 2, (int)y0 / 2, Pixels / 2, Pixels / 2) };
            DrawTextureRect(fogView, new Rect2(origin, new Vector2(Pixels, Pixels)), false);
        }
        foreach (var s in _map.Region.Settlements)
        {
            var p = origin + new Vector2(s.CenterX / (float)RegionData.TileM * RegionMap.Px - (int)x0, s.CenterY / (float)RegionData.TileM * RegionMap.Px - (int)y0);
            if (p.X < origin.X || p.Y < origin.Y || p.X > origin.X + Pixels || p.Y > origin.Y + Pixels) continue;
            int sz = s.Class == Ravensguard.Sim.WorldGen.Region.SettlementClass.Town ? 5 : 3;
            DrawRect(new Rect2((p - new Vector2(sz / 2f + 1, sz / 2f + 1)).Floor(), new Vector2(sz + 2, sz + 2)), PixelTheme.Border);
            DrawRect(new Rect2((p - new Vector2(sz / 2f, sz / 2f)).Floor(), new Vector2(sz, sz)), s.Class == Ravensguard.Sim.WorldGen.Region.SettlementClass.Town ? PixelTheme.Gold : new Color("e8dcc0"));
        }
        var h = (origin + new Vector2(cx - (int)x0, cy - (int)y0)).Floor();
        DrawRect(new Rect2(h - new Vector2(3, 3), new Vector2(7, 7)), PixelTheme.Border);
        DrawRect(new Rect2(h - new Vector2(2, 2), new Vector2(5, 5)), new Color("fde27a"));
    }
}
