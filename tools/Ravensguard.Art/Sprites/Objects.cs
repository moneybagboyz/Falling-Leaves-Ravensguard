using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>Transparent, outlined props (port of anvil ... cart).</summary>
public sealed class Objects
{
    private readonly ColorLookup P;
    private readonly RampLookup R;
    private static readonly ShadeOptions Top = new() { TopHi = true };

    public Objects(Palette pal) { P = pal.P; R = pal.R; }

    private uint[] Rp(params string[] names) => names.Select(n => P[n]).ToArray();

    private PixelBuffer Obj(int w, int h, Action<PixelBuffer> fn)
    {
        var b = new PixelBuffer(w, h); fn(b); b.Outline(P["out"]); return b;
    }

    public PixelBuffer Anvil() => Obj(32, 32, b =>
    {
        b.Shade(11, 20, 10, 10, R["wood"]); b.Shade(6, 13, 22, 5, R["iron"], Top); b.Shade(10, 18, 12, 3, R["iron"]);
        for (int i = 0; i < 5; i++) b.Set(1 + i, 14 + (i >> 1), R["iron"][3]);
        b.Set(9, 13, P["m5"]); b.Set(10, 13, P["m5"]);
    });

    public PixelBuffer Forge() => Obj(32, 32, b =>
    {
        b.Shade(3, 12, 26, 18, R["stone"], Top);
        for (int r = 0; r < 3; r++) for (int i = 0; i < 26; i += 7) b.Rect(3 + i + (r % 2) * 3, 15 + r * 5, 1, 4, P["s1"]);
        b.Rect(6, 9, 20, 5, P["out"]);
        string[] a = { "f2", "f3", "f4", "f3" }, c = { "f1", "f2", "f3", "f2" };
        for (int i = 7; i < 25; i++) { b.Set(i, 10, P[a[i % 4]]); b.Set(i, 11, P[c[(i + 1) % 4]]); b.Set(i, 12, P["f1"]); }
        b.Set(12, 8, P["f3"]); b.Set(18, 7, P["f4"]); b.Set(15, 6, P["f3"]);
    });

    public PixelBuffer Barrel(bool water) => Obj(32, 32, b =>
    {
        for (int y = 8; y < 30; y++)
        {
            int w = y < 12 || y > 26 ? 9 : 11;
            for (int x = 16 - w; x < 16 + w; x++) b.Set(x, y, R["wood"][(x - 16 + w) < 3 ? 4 : (16 + w - x) < 4 ? 1 : 3]);
        }
        foreach (int y in new[] { 10, 18, 26 }) for (int x = 6; x < 26; x++) b.Set(x, y, P["m2"]);
        b.Ellipse(16, 8, 8, 2, P["w2"], true); b.Ellipse(16, 8, 6, 1, water ? P["wa2"] : P["w3"], true);
    });

    public PixelBuffer Crate() => Obj(32, 32, b =>
    {
        b.Shade(7, 12, 18, 16, R["wood"], Top); b.Rect(7, 12, 18, 2, P["w4"]); b.Line(8, 14, 24, 27, P["w2"]); b.Rect(7, 19, 18, 1, P["w2"]);
    });

    public PixelBuffer Chest() => Obj(32, 32, b =>
    {
        b.Shade(5, 14, 22, 14, R["wood"], Top); b.Shade(5, 12, 22, 5, R["wood"], Top); b.Rect(5, 17, 22, 1, P["out"]);
        foreach (int x in new[] { 8, 23 }) b.Rect(x, 12, 2, 16, P["m2"]);
        b.Rect(15, 17, 3, 4, P["au3"]); b.Set(16, 19, P["out"]);
    });

    public PixelBuffer Bed() => Obj(32, 32, b =>
    {
        b.Shade(3, 2, 26, 28, R["wood"]); b.Shade(5, 4, 22, 24, Rp("hbl", "f3", "au3", "f4")); b.Shade(5, 4, 22, 6, R["linen"], Top);
        b.Shade(5, 12, 22, 16, R["red"]);
        for (int j = 12; j < 28; j += 3) b.Rect(5, j, 22, 1, P["r2"]);
    });

    public PixelBuffer Table() => Obj(32, 32, b =>
    {
        b.Shade(2, 8, 28, 12, R["wood"], Top); b.Rect(2, 19, 28, 2, P["w1"]); b.Rect(4, 21, 2, 8, P["w2"]); b.Rect(26, 21, 2, 8, P["w2"]);
        b.Set(9, 11, P["pl4"]); b.Set(10, 11, P["pl3"]); b.Ellipse(20, 12, 3, 2, P["m3"], true);
    });

    public PixelBuffer Bench() => Obj(32, 32, b =>
    {
        b.Shade(2, 18, 28, 5, R["wood"], Top); b.Rect(4, 23, 2, 6, P["w1"]); b.Rect(26, 23, 2, 6, P["w1"]);
    });

    public PixelBuffer Rack() => Obj(32, 32, b =>
    {
        b.Rect(4, 4, 2, 26, P["w2"]); b.Rect(26, 4, 2, 26, P["w2"]); b.Rect(4, 4, 24, 2, P["w3"]);
        b.Shade(8, 7, 16, 19, R["skin"]);
        for (int i = 0; i < 4; i++) { b.Set(8 + i * 5, 7, P["out"]); b.Set(8 + i * 5, 25, P["out"]); }
        b.Set(14, 14, P["k1"]); b.Set(17, 18, P["k1"]);
    });

    public PixelBuffer Sacks() => Obj(32, 32, b =>
    {
        foreach (var (x, y) in new[] { (6, 12), (17, 14) })
        {
            b.Shade(x, y, 11, 15, R["linen"], Top); b.Rect(x + 3, y - 2, 5, 3, P["ln2"]); b.Rect(x + 3, y + 1, 5, 1, P["br2"]);
        }
    });

    /// <summary>Straw bales, stacked: streaky straw, twine round each, ragged ends. Reads as hay, not as a crate.</summary>
    public PixelBuffer Hay() => Obj(32, 32, b =>
    {
        var g = R["gold"];
        void Bale(int x0, int y0, int w, int h, int seed)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int k = y < 2 ? 3 : y >= h - 3 ? 0 : y >= h - 5 ? 1 : 2;
                    double n = Hash(x / 3, y, seed) * .7 + Hash(x, y, seed + 1) * .3;
                    if (n < .18) k = Math.Max(0, k - 1); else if (n > .86) k = Math.Min(3, k + 1);
                    if (x == 0 || x == w - 1) k = Math.Max(0, k - 1);
                    b.Set(x0 + x, y0 + y, g[k]);
                }
            foreach (int tx in new[] { w / 4, w - 1 - w / 4 }) for (int y = 0; y < h; y++) { b.Set(x0 + tx, y0 + y, P["br2"]); if (y % 4 == 1) b.Set(x0 + tx + 1, y0 + y, P["br3"]); }
            for (int i = 0; i < 9; i++)
            {
                int ey = y0 + 1 + (int)(Hash(i, 3, seed) * (h - 2)); bool left = (i & 1) == 0;
                int len = 1 + (int)(Hash(i, 4, seed) * 2.5);
                for (int k = 0; k < len; k++) b.Set(left ? x0 - 1 - k : x0 + w + k, ey - (k >> 1), g[1 + (i + k) % 3]);
            }
            for (int i = 0; i < 5; i++) b.Set(x0 + 1 + (int)(Hash(i, 5, seed) * (w - 2)), y0 - 1, g[2 + i % 2]);
        }
        Bale(3, 17, 17, 11, 1151);
        Bale(12, 7, 17, 10, 1157);
        Bale(10, 20, 19, 9, 1163);
    });

    public PixelBuffer Well() => Obj(32, 32, b =>
    {
        b.Ellipse(16, 18, 13, 9, P["s3"], true); b.Ellipse(16, 17, 9, 6, P["wa1"], true); b.Ellipse(16, 18, 13, 9, P["s2"], false);
        b.Rect(3, 4, 2, 15, P["w2"]); b.Rect(27, 4, 2, 15, P["w2"]); b.Rect(3, 4, 26, 2, P["w3"]); b.Rect(15, 6, 1, 8, P["br2"]); b.Rect(13, 13, 5, 3, P["w3"]);
    });

    public PixelBuffer Hearth(bool lit) => Obj(32, 32, b =>
    {
        b.Shade(2, 14, 28, 16, R["stone"], Top); b.Rect(6, 16, 20, 10, P["out"]); b.Rect(7, 17, 18, 9, P["dk2"]);
        if (lit)
        {
            string[] f = { "f1", "f2", "f3", "f4" };
            for (int i = 8; i < 24; i++)
            {
                int h = 3 + (int)(Hash(i, 1, 2) * 5);
                for (int j = 0; j < h; j++) b.Set(i, 25 - j, P[f[Math.Min(3, j)]]);
            }
        }
        b.Rect(8, 24, 16, 2, P["w1"]);
    });

    public PixelBuffer Sign() => Obj(32, 32, b =>
    {
        b.Rect(15, 10, 2, 20, P["w2"]); b.Shade(5, 4, 22, 9, R["wood"], Top);
        for (int x = 8; x < 25; x += 3) b.Set(x, 8, P["out"]);
        b.Set(9, 7, P["out"]); b.Set(22, 10, P["out"]);
    });

    public PixelBuffer Stake() => Obj(32, 32, b => { b.Rect(15, 18, 2, 12, P["w3"]); b.Set(15, 17, P["w4"]); });

    public PixelBuffer Sconce(bool lit) => Obj(32, 32, b =>
    {
        b.Rect(14, 12, 4, 2, P["m2"]); b.Rect(15, 8, 2, 5, P["w3"]);
        if (lit)
        {
            b.Set(15, 5, P["f2"]); b.Set(16, 5, P["f3"]); b.Set(15, 6, P["f3"]); b.Set(16, 6, P["f4"]); b.Set(15, 4, P["f1"]); b.Set(16, 7, P["f2"]);
        }
    });

    /// <summary>Market stall, 64x48. col is a ramp name: "red", "blue", "green"...</summary>
    public PixelBuffer Stall(string col) => Obj(64, 48, b =>
    {
        var rc = R[col];
        b.Rect(4, 14, 2, 30, P["w2"]); b.Rect(58, 14, 2, 30, P["w2"]);
        for (int x = 2; x < 62; x++)
            for (int y = 6; y < 16; y++)
            {
                int st = ((x - 2) >> 2) % 2;
                b.Set(x, y, st != 0 ? P["pl4"] : rc[2]);
                if (y == 15) b.Set(x, y, st != 0 ? P["pl2"] : rc[1]);
            }
        for (int x = 2; x < 62; x += 4) { b.Set(x, 16, rc[1]); b.Set(x + 1, 17, rc[1]); }
        b.Shade(6, 30, 52, 12, R["wood"], Top);
        foreach (var (qx, qy, qc) in new[] { (10, 26, "f3"), (16, 26, "r3"), (22, 27, "g4"), (30, 26, "au3"), (38, 27, "k3"), (46, 26, "r3"), (52, 27, "f3") })
        {
            b.Ellipse(qx, qy + 2, 3, 3, P[qc], true); b.Set(qx - 1, qy, P["pl4"]);
        }
    });

    /// <summary>Tree, 64x80; v picks autumn / green / red foliage.</summary>
    public PixelBuffer Tree(int v) => Obj(64, 80, b =>
    {
        var r = Rng(7 + v);
        b.Shade(28, 52, 8, 26, R["wood"]); b.Rect(26, 74, 12, 4, P["w2"]);
        uint[][] sets = { Rp("lf1", "lf2", "lf3", "lf4"), Rp("gr1", "gr2", "gr3", "g4"), Rp("r1", "r2", "lf2", "lf3") };
        var cols = sets[v % 3];
        for (int i = 0; i < 14; i++)
        {
            double cx = 14 + r() * 36, cy = 12 + r() * 36, rr = 8 + r() * 8;
            cy = Math.Max(cy, rr + 1.5); cx = Math.Clamp(cx, rr + 1.5, 62.5 - rr); // keep the canopy inside the cell so it keeps its outline (no clipping at y=0)
            for (double y = -rr; y <= rr; y++)
                for (double x = -rr; x <= rr; x++)
                {
                    if (x * x + y * y > rr * rr) continue;
                    double lit = (-x - y) / (rr * 1.4);
                    double n = Hash((int)(cx + x), (int)(cy + y), v) * .4 + lit * .6 + .4;
                    b.Set(cx + x, cy + y, cols[n < .35 ? 0 : n < .65 ? 1 : n < .9 ? 2 : 3]);
                }
        }
    });

    public PixelBuffer Bush() => Obj(32, 32, b =>
    {
        for (int i = 0; i < 5; i++) { double cx = 9 + i * 3.5, cy = 20 + (i % 2) * 2; b.Ellipse((int)cx, (int)cy, 6, 5, P["gr2"], true); }
        for (int i = 0; i < 14; i++) b.Set((int)(5 + Hash(i, 1, 1) * 22), (int)(16 + Hash(i, 2, 1) * 10), i % 3 != 0 ? P["gr3"] : P["lf3"]);
    });

    public PixelBuffer Rocks() => Obj(32, 32, b =>
    {
        b.Ellipse(12, 22, 7, 5, P["s3"], true); b.Ellipse(21, 24, 5, 4, P["s2"], true);
        b.Set(9, 19, P["s5"]); b.Set(10, 19, P["s4"]); b.Set(19, 22, P["s4"]);
    });

    public PixelBuffer Cart() => Obj(64, 48, b =>
    {
        b.Shade(4, 14, 52, 16, R["wood"], Top);
        for (int x = 8; x < 56; x += 10) b.Rect(x, 14, 1, 16, P["w1"]);
        b.Rect(54, 22, 9, 2, P["w2"]); b.Shade(10, 6, 14, 9, R["wood"], Top); b.Shade(28, 4, 12, 11, R["linen"], Top);
        foreach (var (wx, wy) in new[] { (14, 36), (46, 36) })
        {
            b.Ellipse(wx, wy, 9, 9, P["w2"], false);
            for (double a = 0; a < 6.28; a += .785) b.Line(wx, wy, Round(wx + Math.Cos(a) * 8), Round(wy + Math.Sin(a) * 8), P["w3"]);
            b.Rect(wx - 1, wy - 1, 3, 3, P["m2"]);
        }
    });
}
