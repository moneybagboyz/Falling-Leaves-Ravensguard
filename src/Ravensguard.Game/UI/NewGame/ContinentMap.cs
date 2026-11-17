using System;
using System.Collections.Generic;
using System.Linq;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Game.UI.NewGame;

public enum MapLayer { Biome, Realm, Culture, Wealth, Danger }

public sealed record MapOptions(MapLayer Layer = MapLayer.Biome, bool Rivers = true, bool Towns = true, bool Legends = true, byte[]? OwnerOverride = null);

public enum MarkerKind { Battlefield, Ruin, MineCollapse }

/// <summary>A legend marker on the continent map: where it is, what to say about it and which chronicle entry explains it.</summary>
public sealed record LegendMarker(int Cell, MarkerKind Kind, string Text, Ravensguard.Sim.Core.EntityId Event);

/// <summary>
/// Renders the continent as a pixel map (2 image pixels per 2 km cell, so 512 x 512) with the overlay layers of plan §1A Stage 3.
/// Pure CPU code over the sim's arrays; the screens turn the bytes into an ImageTexture. Colouring follows tools/Ravensguard.WorldTool/HistoryMaps.
/// </summary>
public static class ContinentMap
{
    public const int N = Continent.Size, Scale = 2, Px = N * Scale;

    static uint Rgb(int r, int g, int b) => (uint)((Math.Clamp(r, 0, 255) << 16) | (Math.Clamp(g, 0, 255) << 8) | Math.Clamp(b, 0, 255));
    static (int R, int G, int B) Un(uint c) => ((int)(c >> 16) & 255, (int)(c >> 8) & 255, (int)c & 255);
    static uint Mix(uint a, uint b, int pctB)
    {
        var (ar, ag, ab) = Un(a); var (br, bg, bb) = Un(b);
        return Rgb((ar * (100 - pctB) + br * pctB) / 100, (ag * (100 - pctB) + bg * pctB) / 100, (ab * (100 - pctB) + bb * pctB) / 100);
    }
    static uint Shade(uint c, int pct) { var (r, g, b) = Un(c); return Rgb(r * pct / 100, g * pct / 100, b * pct / 100); }

    /// <summary>Golden-angle hue steps give well separated colours for any realm count.</summary>
    public static uint RealmColor(int idx)
    {
        int hue = idx * 137 % 360, sector = hue / 60, f = hue % 60 * 255 / 60;
        int v = 235, p = 90, q = 235 - (235 - 90) * f / 255, t = 90 + (235 - 90) * f / 255;
        return sector switch { 0 => Rgb(v, t, p), 1 => Rgb(q, v, p), 2 => Rgb(p, v, t), 3 => Rgb(p, q, v), 4 => Rgb(t, p, v), _ => Rgb(v, p, q) };
    }

    public static uint CultureColor(int idx) => RealmColor(idx * 3 + 1);

    public static uint BiomeColor(Biome b) => b switch
    {
        Biome.Lake => Rgb(70, 130, 190), Biome.Tundra => Rgb(190, 200, 190), Biome.Taiga => Rgb(50, 100, 70), Biome.TemperateForest => Rgb(60, 130, 60),
        Biome.Grassland => Rgb(130, 170, 70), Biome.Steppe => Rgb(190, 180, 100), Biome.Desert => Rgb(225, 200, 130), Biome.Marsh => Rgb(90, 130, 110),
        Biome.Mountain => Rgb(140, 130, 120), Biome.RiverValley => Rgb(100, 180, 90), _ => Rgb(100, 100, 100),
    };

    static uint Terrain(Continent c, int x, int y)
    {
        int i = y * N + x;
        if (c.Ocean[i])
        {
            int depth = Math.Clamp(-c.CoastDistance[i], 0, 10);
            return Rgb(40 - depth * 2, 78 - depth * 3, 130 - depth * 5);
        }
        int e = c.Elevation[i], nw = c.Elevation[Math.Max(0, y - 1) * N + Math.Max(0, x - 1)];
        return Shade(BiomeColor(c.Biomes[i]), Math.Clamp(100 + (e - nw) / 40, 60, 135));
    }

    /// <summary>Wealth per cell: rural people plus the people of living settlements.</summary>
    static int[] Wealth(Continent c, WorldHistory h)
    {
        var w = new int[Continent.Cells];
        for (int i = 0; i < w.Length && i < h.FinalPop.Length; i++) w[i] = h.FinalPop[i];
        foreach (var s in h.Settlements) if (!s.IsRuin) w[s.Cell] += s.Pop;
        return w;
    }

    public static byte[] Render(Continent c, WorldHistory? h, MapOptions o)
    {
        var owner = o.OwnerOverride ?? (h != null && h.FinalOwner.Length == Continent.Cells ? h.FinalOwner : null);
        var cell = new uint[Continent.Cells];
        int[]? wealth = o.Layer == MapLayer.Wealth && h != null ? Wealth(c, h) : null;
        int wMax = 1;
        if (wealth != null)
        {
            var land = wealth.Where(v => v > 0).OrderBy(v => v).ToArray();
            wMax = land.Length == 0 ? 1 : Math.Max(1, land[(int)(land.Length * 0.97)]);
        }
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                uint t = Terrain(c, x, y);
                if (c.Ocean[i]) { cell[i] = t; continue; }
                switch (o.Layer)
                {
                    case MapLayer.Realm when owner != null && owner[i] != Continent.None && !c.Lake[i]:
                        {
                            uint col = Mix(Shade(t, 85), RealmColor(owner[i]), 58);
                            bool edge = (x < N - 1 && owner[i + 1] != owner[i] && !c.Ocean[i + 1]) || (y < N - 1 && owner[i + N] != owner[i] && !c.Ocean[i + N]);
                            cell[i] = edge ? Shade(col, 55) : col;
                            break;
                        }
                    case MapLayer.Culture when c.CultureId[i] != Continent.None && !c.Lake[i]:
                        {
                            uint col = Mix(Shade(t, 85), CultureColor(c.CultureId[i]), 58);
                            bool edge = (x < N - 1 && c.CultureId[i + 1] != c.CultureId[i] && !c.Ocean[i + 1]) || (y < N - 1 && c.CultureId[i + N] != c.CultureId[i] && !c.Ocean[i + N]);
                            cell[i] = edge ? Shade(col, 55) : col;
                            break;
                        }
                    case MapLayer.Wealth when wealth != null:
                        {
                            double f = Math.Sqrt(Math.Min(1.0, wealth[i] / (double)wMax));
                            cell[i] = Mix(Shade(t, 45), Rgb(255, 210, 80), (int)(f * 85));
                            break;
                        }
                    case MapLayer.Danger:
                        {
                            int s = c.Savagery[i];
                            uint ramp = s < 50 ? Mix(Rgb(60, 150, 70), Rgb(230, 210, 70), s * 2) : Mix(Rgb(230, 210, 70), Rgb(220, 50, 40), (s - 50) * 2);
                            cell[i] = Mix(Shade(t, 55), ramp, 65);
                            break;
                        }
                    default: cell[i] = t; break;
                }
            }

        var img = new uint[Px * Px];
        for (int y = 0; y < Px; y++) for (int x = 0; x < Px; x++) img[y * Px + x] = cell[(y / Scale) * N + x / Scale];

        if (o.Rivers)
            foreach (var r in c.Rivers)
                for (int k = 0; k + 1 < r.Cells.Length; k++)
                {
                    int a = r.Cells[k], b = r.Cells[k + 1];
                    Line(img, a % N * Scale + 1, a / N * Scale + 1, b % N * Scale + 1, b / N * Scale + 1, c.Ocean[a] ? 0u : Rgb(70, 130, 220), c.Ocean[a]);
                }
        if (h != null)
        {
            if (o.Legends) DrawLegends(img, h);
            if (o.Towns) DrawTowns(img, h);
        }
        var bytes = new byte[Px * Px * 4];
        for (int i = 0; i < img.Length; i++)
        {
            var (r, g, b) = Un(img[i]);
            bytes[i * 4] = (byte)r; bytes[i * 4 + 1] = (byte)g; bytes[i * 4 + 2] = (byte)b; bytes[i * 4 + 3] = 255;
        }
        return bytes;
    }

    static void DrawTowns(uint[] img, WorldHistory h)
    {
        foreach (var s in h.Settlements)
        {
            if (s.IsRuin) continue;
            int cx = s.Cell % N * Scale + 1, cy = s.Cell / N * Scale + 1;
            if (s.Kind == SettlementKind.City) Square(img, cx, cy, 3, Rgb(255, 255, 255), Rgb(20, 14, 12));
            else if (s.Kind == SettlementKind.Town) Square(img, cx, cy, 2, Rgb(255, 255, 255), Rgb(20, 14, 12));
            else if (s.Kind == SettlementKind.Village) Square(img, cx, cy, 1, Rgb(250, 240, 200), Rgb(60, 50, 40));
        }
        foreach (var r in h.Realms)
            if (r.Alive && r.Capital >= 0 && r.Capital < h.Settlements.Count)
            {
                int cell = h.Settlements[r.Capital].Cell;
                Square(img, cell % N * Scale + 1, cell / N * Scale + 1, 3, Rgb(255, 215, 0), Rgb(20, 14, 12));
            }
    }

    static void DrawLegends(uint[] img, WorldHistory h)
    {
        foreach (var s in h.Settlements)
            if (s.IsRuin) Cross(img, s.Cell % N * Scale + 1, s.Cell / N * Scale + 1, 2, Rgb(235, 225, 205), Rgb(30, 20, 20), diagonal: true);
        foreach (var b in h.Battlefields) Cross(img, b.Cell % N * Scale + 1, b.Cell / N * Scale + 1, 3, Rgb(240, 50, 40), Rgb(30, 10, 10), diagonal: false);
    }

    static void Pixel(uint[] img, int x, int y, uint col) { if ((uint)x < Px && (uint)y < Px) img[y * Px + x] = col; }

    static void Square(uint[] img, int cx, int cy, int r, uint fill, uint ring)
    {
        for (int dy = -r - 1; dy <= r; dy++)
            for (int dx = -r - 1; dx <= r; dx++)
                Pixel(img, cx + dx, cy + dy, dx >= -r && dx < r && dy >= -r && dy < r ? fill : ring);
    }

    static void Cross(uint[] img, int cx, int cy, int r, uint col, uint outline, bool diagonal)
    {
        for (int pass = 0; pass < 2; pass++)
            for (int d = -r; d <= r; d++)
            {
                uint cc = pass == 0 ? outline : col;
                int w = pass == 0 ? 1 : 0;
                for (int o = -w; o <= w; o++)
                    for (int o2 = -w; o2 <= w; o2++)
                    {
                        if (diagonal) { Pixel(img, cx + d + o, cy + d + o2, cc); Pixel(img, cx + d + o, cy - d + o2, cc); }
                        else { Pixel(img, cx + d + o, cy + o2, cc); Pixel(img, cx + o, cy + d + o2, cc); }
                    }
            }
    }

    static void Line(uint[] img, int x0, int y0, int x1, int y1, uint col, bool skip)
    {
        if (skip) return;
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        for (int guard = 0; guard < 4096; guard++)
        {
            Pixel(img, x0, y0, col);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>Hatched dark overlay (RGBA, alpha 0 where allowed) for squares where no embark window centred on the cell is allowed.</summary>
    public static byte[] RenderExclusion(bool[] excludedCell)
    {
        var b = new byte[Px * Px * 4];
        for (int y = 0; y < Px; y++)
            for (int x = 0; x < Px; x++)
            {
                if (!excludedCell[(y / Scale) * N + x / Scale]) continue;
                int o = (y * Px + x) * 4;
                bool hatch = ((x + y) & 3) == 0;
                b[o] = 20; b[o + 1] = 18; b[o + 2] = 22; b[o + 3] = (byte)(hatch ? 190 : 140);
            }
        return b;
    }

    /// <summary>Battlefields, ruins and mine collapses with a tooltip line each (plan §1A: "Legends" overlay).</summary>
    public static List<LegendMarker> Markers(WorldHistory h)
    {
        var list = new List<LegendMarker>();
        foreach (var b in h.Battlefields)
        {
            int year = b.Tick / 4;
            list.Add(new LegendMarker(b.Cell, MarkerKind.Battlefield, L.F("LEGEND_MARKER_BATTLE", b.Name, year, b.Dead), b.Event));
        }
        foreach (var s in h.Settlements)
        {
            if (!s.IsRuin) continue;
            string cause = s.RuinCauseEvent.Value != 0 && h.Chronicle.Get(s.RuinCauseEvent) is { } ce ? ce.Data.GetValueOrDefault("title", ce.TypeId) : "";
            list.Add(new LegendMarker(s.Cell, MarkerKind.Ruin,
                cause.Length > 0 ? L.F("LEGEND_MARKER_RUIN_CAUSE", s.Name, s.RuinKind, s.RuinTick / 4, cause) : L.F("LEGEND_MARKER_RUIN", s.Name, s.RuinKind, s.RuinTick / 4),
                s.RuinEvent));
        }
        return list;
    }
}
