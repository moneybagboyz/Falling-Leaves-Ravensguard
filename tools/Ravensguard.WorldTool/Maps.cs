using Ravensguard.Sim.WorldGen;

namespace Ravensguard.WorldTool;

/// <summary>Renders continent layers to PNGs (debug visualisation only; float-free but not part of the simulation).</summary>
public static class Maps
{
    private const int N = Continent.Size;

    private static uint Rgb(int r, int g, int b) => (uint)((Math.Clamp(r, 0, 255) << 16) | (Math.Clamp(g, 0, 255) << 8) | Math.Clamp(b, 0, 255));
    private static (int R, int G, int B) Un(uint c) => ((int)(c >> 16) & 255, (int)(c >> 8) & 255, (int)c & 255);
    private static uint Mul(uint c, int pct) { var (r, g, b) = Un(c); return Rgb(r * pct / 100, g * pct / 100, b * pct / 100); }
    private static uint Mix(uint a, uint b, int pctB)
    {
        var (ar, ag, ab) = Un(a); var (br, bg, bb) = Un(b);
        return Rgb((ar * (100 - pctB) + br * pctB) / 100, (ag * (100 - pctB) + bg * pctB) / 100, (ab * (100 - pctB) + bb * pctB) / 100);
    }

    private static uint BiomeColor(Biome b) => b switch
    {
        Biome.Sea => Rgb(30, 70, 120), Biome.Lake => Rgb(70, 130, 190), Biome.Tundra => Rgb(190, 200, 190),
        Biome.Taiga => Rgb(50, 100, 70), Biome.TemperateForest => Rgb(60, 130, 60), Biome.Grassland => Rgb(130, 170, 70),
        Biome.Steppe => Rgb(190, 180, 100), Biome.Desert => Rgb(225, 200, 130), Biome.Marsh => Rgb(90, 130, 110),
        Biome.Mountain => Rgb(140, 130, 120), Biome.RiverValley => Rgb(100, 180, 90), _ => 0,
    };

    private static readonly uint[] Palette =
    [
        Rgb(220, 80, 70), Rgb(70, 130, 220), Rgb(230, 190, 60), Rgb(90, 190, 100), Rgb(180, 90, 200), Rgb(60, 190, 200),
        Rgb(240, 140, 60), Rgb(150, 150, 150), Rgb(200, 120, 140), Rgb(120, 90, 50), Rgb(100, 100, 230), Rgb(160, 200, 60),
    ];

    private static int Shade(Continent c, int x, int y)
    {
        // Hillshade from the north-west: difference with the NW neighbour.
        int i = y * N + x;
        int e = c.Elevation[i];
        int nw = c.Elevation[Math.Max(0, y - 1) * N + Math.Max(0, x - 1)];
        return Math.Clamp(100 + (e - nw) / 40, 55, 140);
    }

    public static void WriteAll(Continent c, string prefix, int scale)
    {
        Save(c, prefix + "_biome.png", scale, BiomeMap(c), rivers: true);
        Save(c, prefix + "_elevation.png", scale, ElevationMap(c), rivers: true);
        Save(c, prefix + "_rainfall.png", scale, Ramp(c, i => c.Ocean[i] ? -1 : c.Rainfall[i], 0, 2200, 0xE8D8A0, 0x1030A0), rivers: false);
        Save(c, prefix + "_temperature.png", scale, Ramp(c, i => c.Temperature[i], -100, 280, 0x3050E0, 0xE04020), rivers: false);
        Save(c, prefix + "_realms.png", scale, RealmMap(c), rivers: true, markers: true);
        Save(c, prefix + "_minerals.png", scale, MineralMap(c, scale), rivers: false);
    }

    private static uint[] BiomeMap(Continent c)
    {
        var px = new uint[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                px[i] = c.Ocean[i] ? Mul(BiomeColor(Biome.Sea), 60 + Math.Max(-40, c.CoastDistance[i] * 2) / 2 + 20) : Mul(BiomeColor(c.Biomes[i]), Shade(c, x, y));
            }
        return px;
    }

    private static uint[] ElevationMap(Continent c)
    {
        var px = new uint[N * N];
        int sea = c.SeaLevelRaw;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x; int e = c.Elevation[i];
                if (c.Ocean[i]) px[i] = Mix(Rgb(10, 30, 80), Rgb(60, 120, 170), e * 100 / sea);
                else if (c.Lake[i]) px[i] = Rgb(70, 130, 190);
                else
                {
                    int t = (e - sea) * 100 / (65536 - sea);
                    uint col = t < 20 ? Mix(Rgb(70, 140, 70), Rgb(190, 190, 90), t * 5) : t < 55 ? Mix(Rgb(190, 190, 90), Rgb(130, 100, 70), (t - 20) * 100 / 35) : Mix(Rgb(130, 100, 70), Rgb(250, 250, 250), (t - 55) * 100 / 45);
                    px[i] = Mul(col, Shade(c, x, y));
                }
            }
        return px;
    }

    private static uint[] Ramp(Continent c, Func<int, int> f, int lo, int hi, uint a, uint b)
    {
        var px = new uint[N * N];
        for (int i = 0; i < N * N; i++)
        {
            int v = f(i);
            px[i] = v == -1 && c.Ocean[i] ? Rgb(25, 45, 80) : Mix(a, b, Math.Clamp((v - lo) * 100 / (hi - lo), 0, 100));
            if (c.Ocean[i]) px[i] = Mul(Rgb(30, 50, 90), 100);
        }
        return px;
    }

    private static uint[] RealmMap(Continent c)
    {
        var px = new uint[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                if (c.Ocean[i]) { px[i] = Rgb(30, 55, 95); continue; }
                uint baseCol = c.Biomes[i] == Biome.Lake ? Rgb(70, 130, 190) : c.RealmId[i] == Continent.None ? Rgb(120, 120, 120) : Palette[c.RealmId[i] % Palette.Length];
                // Culture shows as a diagonal stripe tint so both layers read at once.
                bool stripe = c.CultureId[i] != Continent.None && ((x + y) / 3 + c.CultureId[i]) % 2 == 0;
                px[i] = Mul(baseCol, (stripe ? 85 : 105) * Shade(c, x, y) / 100);
            }
        return px;
    }

    private static uint[] MineralMap(Continent c, int scale)
    {
        var px = new uint[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                px[i] = c.Ocean[i] ? Rgb(20, 30, 50) : Mul(Mix(BiomeColor(c.Biomes[i]), Rgb(90, 90, 90), 70), Shade(c, x, y) * 60 / 100);
            }
        return px;
    }

    private static uint MineralColor(MineralType t) => t switch
    {
        MineralType.Iron => Rgb(200, 90, 50), MineralType.Copper => Rgb(60, 220, 160), MineralType.Tin => Rgb(200, 200, 220),
        MineralType.Silver => Rgb(255, 255, 255), MineralType.Gold => Rgb(255, 215, 0), MineralType.Coal => Rgb(10, 10, 10),
        MineralType.Peat => Rgb(120, 70, 30), MineralType.Salt => Rgb(255, 150, 220), _ => Rgb(150, 150, 170),
    };

    private static void Save(Continent c, string path, int scale, uint[] cells, bool rivers, bool markers = false)
    {
        int w = N * scale;
        var img = new uint[w * w];
        for (int y = 0; y < w; y++)
            for (int x = 0; x < w; x++) img[y * w + x] = cells[(y / scale) * N + x / scale];

        if (rivers)
            foreach (var r in c.Rivers)
                for (int k = 0; k + 1 < r.Cells.Length; k++)
                {
                    int a = r.Cells[k], b = r.Cells[k + 1];
                    int flow = c.RiverFlow[a];
                    int thick = flow > 2000 ? 2 : flow > 600 ? 1 : 0;
                    Line(img, w, Cx(a, scale), Cy(a, scale), Cx(b, scale), Cy(b, scale), Rgb(40, 100, 200), thick);
                }

        if (path.EndsWith("_minerals.png"))
            foreach (var v in c.Veins)
            {
                if (v.Type == MineralType.Stone) continue;
                int cx = Cx(v.Cell, scale), cy = Cy(v.Cell, scale);
                Dot(img, w, cx, cy, MineralColor(v.Type), v.Type is MineralType.Gold or MineralType.Silver ? 2 : 1);
            }

        if (markers)
        {
            foreach (var r in c.Realms) Dot(img, w, Cx(r.SeedCell, scale), Cy(r.SeedCell, scale), Rgb(255, 255, 255), 3, Rgb(0, 0, 0));
            foreach (var cu in c.Cultures) Dot(img, w, Cx(cu.SeedCell, scale), Cy(cu.SeedCell, scale), Rgb(0, 0, 0), 2, Rgb(255, 255, 0));
        }
        Png.Write(path, w, w, img);
    }

    private static int Cx(int cell, int s) => (cell % N) * s + s / 2;
    private static int Cy(int cell, int s) => (cell / N) * s + s / 2;

    private static void Dot(uint[] img, int w, int cx, int cy, uint col, int r, uint? ring = null)
    {
        for (int dy = -r - 1; dy <= r + 1; dy++)
            for (int dx = -r - 1; dx <= r + 1; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if ((uint)x >= w || (uint)y >= w) continue;
                int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (d <= r) img[y * w + x] = col; else if (ring != null) img[y * w + x] = ring.Value;
            }
    }

    private static void Line(uint[] img, int w, int x0, int y0, int x1, int y1, uint col, int thick)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            for (int ty = 0; ty <= thick; ty++)
                for (int tx = 0; tx <= thick; tx++)
                {
                    int px = x0 + tx, py = y0 + ty;
                    if ((uint)px < w && (uint)py < w) img[py * w + px] = col;
                }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
