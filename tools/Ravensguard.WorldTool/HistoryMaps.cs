using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.WorldTool;

/// <summary>Debug rendering of history output: realm map with ruins and battlefields, plus realms over time.</summary>
public static class HistoryMaps
{
    private const int N = Continent.Size;

    private static uint Rgb(int r, int g, int b) => (uint)((Math.Clamp(r, 0, 255) << 16) | (Math.Clamp(g, 0, 255) << 8) | Math.Clamp(b, 0, 255));
    private static (int R, int G, int B) Un(uint c) => ((int)(c >> 16) & 255, (int)(c >> 8) & 255, (int)c & 255);
    private static uint Mix(uint a, uint b, int pctB)
    {
        var (ar, ag, ab) = Un(a); var (br, bg, bb) = Un(b);
        return Rgb((ar * (100 - pctB) + br * pctB) / 100, (ag * (100 - pctB) + bg * pctB) / 100, (ab * (100 - pctB) + bb * pctB) / 100);
    }

    private static uint RealmColor(int idx)
    {
        // Golden-angle hue steps give well separated colours for any realm count.
        int hue = idx * 137 % 360, sector = hue / 60, f = hue % 60 * 255 / 60;
        int v = 235, p = 90, q = 235 - (235 - 90) * f / 255, t = 90 + (235 - 90) * f / 255;
        return sector switch { 0 => Rgb(v, t, p), 1 => Rgb(q, v, p), 2 => Rgb(p, v, t), 3 => Rgb(p, q, v), 4 => Rgb(t, p, v), _ => Rgb(v, p, q) };
    }

    private static uint BiomeColor(Biome b) => b switch
    {
        Biome.Lake => Rgb(70, 130, 190), Biome.Tundra => Rgb(190, 200, 190), Biome.Taiga => Rgb(50, 100, 70), Biome.TemperateForest => Rgb(60, 130, 60),
        Biome.Grassland => Rgb(130, 170, 70), Biome.Steppe => Rgb(190, 180, 100), Biome.Desert => Rgb(225, 200, 130), Biome.Marsh => Rgb(90, 130, 110),
        Biome.Mountain => Rgb(140, 130, 120), Biome.RiverValley => Rgb(100, 180, 90), _ => Rgb(100, 100, 100),
    };

    private static uint[] RealmLayer(Continent c, byte[] owner)
    {
        var px = new uint[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                if (c.Ocean[i]) { px[i] = Rgb(28, 52, 92); continue; }
                uint baseCol = BiomeColor(c.Biomes[i]);
                int e = c.Elevation[i], nw = c.Elevation[Math.Max(0, y - 1) * N + Math.Max(0, x - 1)];
                int shade = Math.Clamp(100 + (e - nw) / 40, 60, 135);
                var (br, bg, bb) = Un(baseCol);
                baseCol = Rgb(br * shade / 100, bg * shade / 100, bb * shade / 100);
                if (c.Biomes[i] == Biome.Lake || owner[i] == Continent.None) { px[i] = baseCol; continue; }
                uint col = Mix(baseCol, RealmColor(owner[i]), 58);
                bool edge = (x < N - 1 && owner[i + 1] != owner[i] && !c.Ocean[i + 1]) || (y < N - 1 && owner[i + N] != owner[i] && !c.Ocean[i + N]);
                px[i] = edge ? Mix(col, Rgb(0, 0, 0), 55) : col;
            }
        return px;
    }

    private static uint[] Upscale(uint[] cells, int scale)
    {
        int w = N * scale;
        var img = new uint[w * w];
        for (int y = 0; y < w; y++) for (int x = 0; x < w; x++) img[y * w + x] = cells[(y / scale) * N + x / scale];
        return img;
    }

    public static void Write(Continent c, WorldHistory h, string path, string timelinePath, int scale)
    {
        int w = N * scale;
        var img = Upscale(RealmLayer(c, h.FinalOwner), scale);
        foreach (var r in c.Rivers)
            for (int k = 0; k + 1 < r.Cells.Length; k++)
            {
                int a = r.Cells[k], b = r.Cells[k + 1];
                if (c.RiverFlow[a] > 600) Line(img, w, a % N * scale + scale / 2, a / N * scale + scale / 2, b % N * scale + scale / 2, b / N * scale + scale / 2, Rgb(50, 110, 200));
            }
        foreach (var rt in h.TradeRoutes)
        {
            if (rt.Abandoned) continue;
            for (int k = 0; k + 1 < rt.Cells.Length; k += 2) Pixel(img, w, rt.Cells[k] % N * scale + scale / 2, rt.Cells[k] / N * scale + scale / 2, Rgb(235, 225, 190));
        }
        foreach (var b in h.Battlefields) Cross(img, w, b.Cell % N * scale + scale / 2, b.Cell / N * scale + scale / 2, 3, Rgb(255, 30, 30));
        foreach (var s in h.Settlements)
        {
            int cx = s.Cell % N * scale + scale / 2, cy = s.Cell / N * scale + scale / 2;
            if (s.IsRuin) Cross(img, w, cx, cy, 2, Rgb(20, 20, 20), diagonal: true);
            else if (s.Kind >= SettlementKind.Town) Dot(img, w, cx, cy, s.Kind == SettlementKind.City ? 3 : 2, Rgb(255, 255, 255), Rgb(0, 0, 0));
            else if (s.Kind == SettlementKind.Village) Dot(img, w, cx, cy, 1, Rgb(250, 250, 200), Rgb(60, 60, 60));
        }
        foreach (var r in h.Realms)
            if (r.Alive && r.Capital >= 0) Dot(img, w, h.Settlements[r.Capital].Cell % N * scale + scale / 2, h.Settlements[r.Capital].Cell / N * scale + scale / 2, 4, Rgb(255, 215, 0), Rgb(0, 0, 0));
        Png.Write(path, w, w, img);

        // timeline: snapshots in a grid, scale 2
        int cols = Math.Min(3, h.Snapshots.Count), rows = (h.Snapshots.Count + cols - 1) / cols, ts = 2, tw = N * ts;
        var big = new uint[cols * tw * rows * tw];
        int bw = cols * tw;
        for (int k = 0; k < h.Snapshots.Count; k++)
        {
            var tile = Upscale(RealmLayer(c, h.Snapshots[k].Owner), ts);
            int ox = k % cols * tw, oy = k / cols * tw;
            for (int y = 0; y < tw; y++) for (int x = 0; x < tw; x++) big[(oy + y) * bw + ox + x] = tile[y * tw + x];
            // a year tick marker: k+1 white pips in the top-left corner (no font available)
            for (int p = 0; p <= k; p++) Dot(big, bw, ox + 6 + p * 6, oy + 6, 1, Rgb(255, 255, 255), Rgb(0, 0, 0));
        }
        Png.Write(timelinePath, bw, rows * tw, big);
    }

    private static void Pixel(uint[] img, int w, int x, int y, uint col) { if ((uint)x < w && (uint)y < img.Length / w) img[y * w + x] = col; }

    private static void Dot(uint[] img, int w, int cx, int cy, int r, uint col, uint ring)
    {
        for (int dy = -r - 1; dy <= r + 1; dy++)
            for (int dx = -r - 1; dx <= r + 1; dx++)
                Pixel(img, w, cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) <= r ? col : ring);
    }

    private static void Cross(uint[] img, int w, int cx, int cy, int r, uint col, bool diagonal = false)
    {
        for (int d = -r; d <= r; d++)
        {
            if (diagonal) { Pixel(img, w, cx + d, cy + d, col); Pixel(img, w, cx + d, cy - d, col); }
            else { Pixel(img, w, cx + d, cy, col); Pixel(img, w, cx, cy + d, col); }
        }
    }

    private static void Line(uint[] img, int w, int x0, int y0, int x1, int y1, uint col)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            Pixel(img, w, x0, y0, col);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
