using System;
using System.Collections.Generic;
using Godot;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Game.UI;

public enum MapIcon { Town, Village, Farmstead, Ruin, Cave, Mine, Barrow, Battlefield, Hoard, Unknown }

/// <summary>Small pixel icons drawn in code: the weather glyphs for the time widget (16 x 16) and the world-map symbols (12 x 12). Palette matches the art tool's ramps.</summary>
public static class PixelIcons
{
    static readonly Dictionary<char, Color> Pal = new()
    {
        ['s'] = new("9a99a0"), ['S'] = new("75747c"), ['d'] = new("1a1418"), ['r'] = new("b33a2c"), ['R'] = new("8c2a1e"),
        ['t'] = new("cab064"), ['T'] = new("aa9048"), ['g'] = new("56873a"), ['G'] = new("355c26"), ['y'] = new("e0b450"), ['Y'] = new("fde27a"),
        ['b'] = new("8e6842"), ['B'] = new("5a3e28"), ['w'] = new("dce6f2"), ['k'] = new("57565e"), ['o'] = new("d4573f"), ['n'] = new("3d6b9a"),
    };

    static readonly Dictionary<MapIcon, string[]> Rows = new()
    {
        [MapIcon.Town] = new[]
        {
            "s.s.ss.s.s", "ssSssssSss", ".ssssssss.", ".sssddsss.", ".ssddddss.", ".ssssssss.", ".sssddsss.", ".sssddsss.", ".SssddssS.", ".SSSSSSSS.",
        },
        [MapIcon.Village] = new[]
        {
            "....rr....", "...rrrR...", "..rrrrrR..", ".rrrrrrrR.", "RRRRRRRRRR", ".tttttttT.", ".ttbbtttT.", ".ttbbtwtT.", ".ttbbtttT.", ".TTTTTTTT.",
        },
        [MapIcon.Farmstead] = new[]
        {
            "..........", "...yyyy...", "..yyyyyy..", ".yyyyyyyy.", "TTTTTTTTTT", ".bbbbbbbb.", ".bBBBBBBb.", ".bBbbbbBb.", ".bBbbbbBb.", ".BBBBBBBB.",
        },
        [MapIcon.Ruin] = new[]
        {
            "..........", ".s....s...", ".ss..ss...", ".ssssss.s.", ".sSsSss.ss", ".sSSSSsSss", ".s.SSs.SSs", "ss.....SSs", "SS.s...SS.", "SSSSSSSSS.",
        },
        [MapIcon.Cave] = new[]
        {
            "..........", "...SSSS...", "..SssssS..", ".SssssssS.", ".SsddddsS.", ".SsddddsS.", "SSddddddSS", "SSddddddSS", "SSddddddSS", "SSSSSSSSSS",
        },
        [MapIcon.Mine] = new[]
        {
            "..ssss....", ".ssSSss...", "ss...sSs..", "s.....Bs..", "......B.s.", ".....B..s.", "....B.....", "...B......", "..B.......", ".B........",
        },
        [MapIcon.Barrow] = new[]
        {
            "..........", "...gggg...", "..gggggG..", ".ggggggGG.", ".gggddgGG.", "ggggddggGG", "gggddddgGG", "GGGddddGGG", "GGGGGGGGGG", "..........",
        },
        [MapIcon.Battlefield] = new[]
        {
            "w........w", ".w......w.", "..w....w..", "...w..w...", "....ww....", "....ww....", "...r..r...", "..r....r..", ".r......r.", "r........r",
        },
        [MapIcon.Hoard] = new[]
        {
            "..........", "..yyyyyy..", ".yYYYYYYy.", ".yyyyyyyy.", ".bbbbbbbb.", ".bYYyybBb.", ".bbbyybbb.", ".bBBBBBBb.", ".BBBBBBBB.", "..........",
        },
        [MapIcon.Unknown] = new[]
        {
            "..SSSSSS..", ".SssssssS.", "SssSSSSssS", "SsSsssSsSS", "SssssSssSS", "SsssSssssS", "SsssssssSS", "SsssSssssS", ".SssssssS.", "..SSSSSS..",
        },
    };

    static readonly Dictionary<MapIcon, ImageTexture> _map = new();

    /// <summary>A 12 x 12 map symbol (10 x 10 drawing with a dark outline).</summary>
    public static ImageTexture Map(MapIcon icon)
    {
        if (_map.TryGetValue(icon, out var t)) return t;
        var img = Image.CreateEmpty(12, 12, false, Image.Format.Rgba8);
        var rows = Rows[icon];
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
                if (rows[y][x] != '.' && Pal.TryGetValue(rows[y][x], out var c)) img.SetPixel(x + 1, y + 1, c);
        Outline(img, Pal['d']);
        return _map[icon] = ImageTexture.CreateFromImage(img);
    }

    static void Outline(Image img, Color c)
    {
        var add = new List<Vector2I>();
        for (int y = 0; y < img.GetHeight(); y++)
            for (int x = 0; x < img.GetWidth(); x++)
            {
                if (img.GetPixel(x, y).A > 0) continue;
                bool Has(int px, int py) => px >= 0 && py >= 0 && px < img.GetWidth() && py < img.GetHeight() && img.GetPixel(px, py).A > 0;
                if (Has(x - 1, y) || Has(x + 1, y) || Has(x, y - 1) || Has(x, y + 1)) add.Add(new Vector2I(x, y));
            }
        foreach (var p in add) img.SetPixel(p.X, p.Y, c);
    }

    static readonly Dictionary<(WeatherState, bool), ImageTexture> _weather = new();

    /// <summary>A 16 x 16 weather glyph: sun or moon, cloud, rain, storm, snow, fog, frost, heat.</summary>
    public static ImageTexture Weather(WeatherState s, bool night)
    {
        if (_weather.TryGetValue((s, night), out var cached)) return cached;
        var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        void P(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < 16 && y < 16) img.SetPixel(x, y, c); }
        void Disc(int cx, int cy, float r, Color c) { for (int y = -8; y <= 8; y++) for (int x = -8; x <= 8; x++) if (x * x + y * y <= r * r) P(cx + x, cy + y, c); }
        Color white = new("dce6f2"), cloud = new("b4c4da"), cloudD = new("8a9cb8"), dark = new("57565e"), blue = new("6a9cc4"), gold = new("e0b450"), goldL = new("fde27a"), orange = new("d4573f");
        void Cloud(int dy, bool grim)
        {
            var hi = grim ? new Color("75747c") : white; var mid = grim ? new Color("57565e") : cloud; var lo = grim ? new Color("3e3d44") : cloudD;
            Disc(5, 8 + dy, 3.4f, mid); Disc(9, 6 + dy, 4.2f, mid); Disc(12, 9 + dy, 2.8f, mid);
            for (int y = 8 + dy; y <= 11 + dy; y++) for (int x = 5; x <= 12; x++) P(x, y, mid);
            for (int x = 4; x <= 13; x++) P(x, 11 + dy, lo);
            P(8, 3 + dy, hi); P(9, 3 + dy, hi); P(10, 4 + dy, hi); P(6, 5 + dy, hi);
        }
        void Sun(int cx, int cy, Color body, Color ray, float r)
        {
            Disc(cx, cy, r, body);
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4;
                for (int d = (int)r + 2; d <= (int)r + 3; d++) P(cx + (int)Math.Round(Math.Cos(a) * d), cy + (int)Math.Round(Math.Sin(a) * d), ray);
            }
        }
        switch (s)
        {
            case WeatherState.Clear when !night: Sun(8, 8, goldL, gold, 3.2f); break;
            case WeatherState.Clear:
                Disc(8, 8, 5f, white); Disc(11, 6, 4.4f, new Color(0, 0, 0, 0));
                for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) if ((x - 8) * (x - 8) + (y - 8) * (y - 8) <= 25 && (x - 11) * (x - 11) + (y - 6) * (y - 6) > 19) P(x, y, white);
                break;
            case WeatherState.Heatwave: Sun(8, 8, orange, gold, 3.6f); break;
            case WeatherState.Overcast: Cloud(1, true); break;
            case WeatherState.LightRain: Cloud(-2, false); for (int i = 0; i < 3; i++) { P(5 + i * 3, 12, blue); P(4 + i * 3, 13, blue); } break;
            case WeatherState.HeavyRain: Cloud(-2, true); for (int i = 0; i < 4; i++) { P(4 + i * 3, 12, blue); P(3 + i * 3, 13, blue); P(3 + i * 3, 14, blue); } break;
            case WeatherState.Thunderstorm:
                Cloud(-2, true);
                foreach (var (x, y) in new[] { (9, 10), (8, 11), (7, 12), (9, 12), (8, 13), (7, 14) }) P(x, y, goldL);
                break;
            case WeatherState.Snow: Cloud(-2, false); foreach (var (x, y) in new[] { (5, 12), (8, 13), (11, 12), (6, 14), (10, 14) }) P(x, y, white); break;
            case WeatherState.Blizzard: Cloud(-2, true); foreach (var (x, y) in new[] { (4, 12), (7, 13), (10, 12), (13, 13), (5, 14), (9, 14) }) P(x, y, white); break;
            case WeatherState.Fog: for (int i = 0; i < 4; i++) for (int x = 2 + (i % 2) * 2; x < 14 - ((i + 1) % 2) * 2; x++) P(x, 3 + i * 3, i % 2 == 0 ? cloud : cloudD); break;
            case WeatherState.Frost:
                foreach (var (x, y) in new[] { (8, 2), (8, 3), (8, 4), (8, 5), (8, 6), (8, 7), (8, 8), (8, 9), (8, 10), (8, 11), (8, 12), (8, 13), (2, 8), (3, 8), (4, 8), (5, 8), (6, 8), (7, 8), (9, 8), (10, 8), (11, 8), (12, 8), (13, 8),
                    (4, 4), (5, 5), (6, 6), (10, 10), (11, 11), (12, 12), (12, 4), (11, 5), (10, 6), (6, 10), (5, 11), (4, 12) }) P(x, y, blue);
                P(8, 8, white);
                break;
        }
        return _weather[(s, night)] = ImageTexture.CreateFromImage(img);
    }
}
