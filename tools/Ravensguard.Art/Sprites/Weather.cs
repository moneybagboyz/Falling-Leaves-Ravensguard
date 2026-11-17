using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>Transparent 32x32 overlay layers drawn over terrain: snow cover, puddles, rain, snowfall, fog, splashes.
/// Pixels are exact palette colours or fully transparent; the game applies any translucency (fog, rain) as a layer modulate.</summary>
public sealed class Weather
{
    private readonly ColorLookup P;
    private readonly RampLookup R;
    public Weather(Palette pal) { P = pal.P; R = pal.R; }

    /// <summary>Snow cover. 0/1 full (ground pokes through in a few tufts), 2/3 patchy, 4/5 light dusting.</summary>
    public PixelBuffer Snow(int v)
    {
        var b = new PixelBuffer(32, 32); int s = 901 + v * 11; var sn = R["snow"];
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double n = Periodic(x, y, s, 4) * .75 + Hash(x, y, s) * .25;           // tileable: seams between neighbouring tiles disappear
                bool has;
                if (v < 2) has = Periodic(x, y, s + 9, 8) < .94 || Hash(x, y, s + 3) < .5;
                else if (v < 4) has = n > .5 || (n > .43 && Hash(x, y, s + 5) < .5);
                else has = (Hash(x, y, s + 1) < .10 && Periodic(x, y, s + 2, 4) > .35) || Hash(x, y, s + 8) < .015;
                if (!has) continue;
                double lit = Periodic(x, y, s + 4, 4); int k = lit < .16 ? 1 : lit > .82 ? 3 : 2;
                if (v < 2 && Hash(x, y, s + 6) < .035) k = k == 2 ? 3 : 2;           // fine grain
                if (v >= 4) k = Hash(x, y, s + 2) < .5 ? 3 : 2;
                b.Set(x, y, sn[k]);
            }
        // shade the lower-right rim of each patch so drifts read as raised
        var c = b.Clone();
        if (v >= 2 && v < 4)
            for (int y = 0; y < 31; y++) for (int x = 0; x < 31; x++)
                if (c.Opaque(x, y) && (!c.Opaque(x, y + 1) || !c.Opaque(x + 1, y))) b.Set(x, y, sn[1]);
        return b;
    }

    /// <summary>Standing water laid over any ground after rain.</summary>
    public PixelBuffer Puddle(int v)
    {
        var b = new PixelBuffer(32, 32);
        var pools = v == 0 ? new[] { (15.0, 17.0, 10.0, 6.0) } : v == 1 ? new[] { (9.0, 9.0, 6.0, 4.0), (21.0, 21.0, 8.0, 5.0) } : new[] { (16.0, 15.0, 13.0, 8.0) };
        foreach (var (cx, cy, rx, ry) in pools)
        {
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                double dx = (x - cx) / rx, dy = (y - cy) / ry, d = dx * dx + dy * dy + (Hash(x, y, 911 + v) - .5) * .3;
                if (d > 1) continue;
                b.Set(x, y, d > .8 ? P["wa1"] : d > .3 ? P["wa2"] : P["wa3"]);
            }
            int px = (int)(cx - rx * .4), py = (int)(cy - ry * .4); b.Set(px, py, P["wa4"]); b.Set(px + 1, py, P["wa4"]); b.Set(px + 4, py + 1, P["wa4"]);
        }
        return b;
    }

    private static readonly (int x, int y, int l)[] Drops = Enumerable.Range(0, 16).Select(i => ((int)(Hash(i, 1, 921) * 32), (int)(Hash(i, 2, 921) * 32), 4 + (int)(Hash(i, 3, 921) * 3))).ToArray();

    /// <summary>Rain streaks: 4 frames that loop seamlessly (streaks fall 8 px per frame, the tile wraps at 32).</summary>
    public PixelBuffer Rain(int f)
    {
        var b = new PixelBuffer(32, 32);
        foreach (var (x0, y0, l) in Drops)
            for (int k = 0; k < l; k++)
            {
                int x = ((x0 - k / 3) % 32 + 32) % 32, y = (y0 + f * 8 + k) % 32;
                b.Set(x, y, k == l - 1 ? P["wa4"] : k > l - 3 ? P["s4"] : P["wa3"]);
            }
        return b;
    }

    /// <summary>Snowfall: flakes drift sideways while falling 8 px per frame (wraps at 32 after 4 frames).</summary>
    public PixelBuffer SnowFall(int f)
    {
        var b = new PixelBuffer(32, 32);
        for (int i = 0; i < 22; i++)
        {
            int x0 = (int)(Hash(i, 1, 931) * 32), y0 = (int)(Hash(i, 2, 931) * 32); bool big = i % 4 == 0;
            int x = ((x0 + (int)Math.Round(Math.Sin(i * 1.7 + f * Math.PI / 2) * 1.6)) % 32 + 32) % 32, y = (y0 + f * 8) % 32;
            b.Set(x, y, P["sn4"]);
            if (big) { b.Set(x + 1, y, P["sn3"]); b.Set(x - 1, y, P["sn3"]); b.Set(x, y + 1, P["sn3"]); b.Set(x, y - 1, P["sn3"]); }
            else if (i % 3 == 0) b.Set(x, y + 1, P["sn2"]);
        }
        return b;
    }

    /// <summary>Tileable fog: dithered patches of the two lightest neutrals. v1 is denser. Draw with ~40-60% alpha.</summary>
    public PixelBuffer Fog(int v)
    {
        var b = new PixelBuffer(32, 32); int s = 941 + v * 7;
        int[,] bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double n = Periodic(x, y, s, 4) * .7 + Periodic(x, y, s + 5, 8) * .3;
                double d = n + (v == 1 ? .18 : 0) - .22; if (d <= 0) continue;
                double th = (bayer[y % 4, x % 4] + .5) / 16.0;
                if (d * 1.6 > th) b.Set(x, y, d > .38 ? P["pl4"] : P["ln3"]);
            }
        return b;
    }

    private static double Periodic(int x, int y, int seed, int cells)
    {
        double fx = x * cells / 32.0, fy = y * cells / 32.0; int xi = (int)Math.Floor(fx), yi = (int)Math.Floor(fy);
        double xf = fx - xi, yf = fy - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        double H(int a, int c) => Hash(((a % cells) + cells) % cells, ((c % cells) + cells) % cells, seed);
        double A = H(xi, yi), B = H(xi + 1, yi), C = H(xi, yi + 1), D = H(xi + 1, yi + 1);
        return A + (B - A) * u + (C - A) * v + (A - B - C + D) * u * v;
    }

    /// <summary>Rain splash on the ground / puddle: expanding ring then droplets. 4 frames.</summary>
    public PixelBuffer Splash(int f)
    {
        var b = new PixelBuffer(32, 32);
        foreach (var (cx, cy) in new[] { (9, 10), (22, 19), (13, 25) })
        {
            int o = (f + (cx % 3)) % 4; int rx = 1 + o * 2, ry = Math.Max(1, (1 + o * 2) / 2);
            for (int y = -ry; y <= ry; y++) for (int x = -rx; x <= rx; x++)
            {
                double v = (double)(x * x) / (rx * rx) + (double)(y * y) / (ry * ry);
                if (v <= 1 && v >= (o == 0 ? 0 : .55)) b.Set(cx + x, cy + y, o >= 3 ? P["wa3"] : P["wa4"]);
            }
            if (o == 0) { b.Set(cx, cy - 2, P["pl4"]); b.Set(cx - 2, cy - 1, P["wa4"]); b.Set(cx + 2, cy - 1, P["wa4"]); }
            if (o == 1) { b.Set(cx - 3, cy - 3, P["wa4"]); b.Set(cx + 3, cy - 3, P["wa4"]); b.Set(cx, cy - 4, P["pl4"]); }
        }
        return b;
    }
}
