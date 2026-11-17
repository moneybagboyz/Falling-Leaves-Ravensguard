using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>32x32 terrain tiles (port of tGrass ... tWheat).</summary>
public sealed class Terrain
{
    private readonly ColorLookup P;
    private readonly RampLookup R;
    public Terrain(Palette pal) { P = pal.P; R = pal.R; }

    public PixelBuffer Grass(int v)
    {
        var b = new PixelBuffer(32, 32); int s = 11 + v * 7;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double n = Vn(x / 7.0 + v * 3, y / 7.0, s) * .65 + Hash(x, y, s) * .35;
                b.Set(x, y, R["grass"][n < .33 ? 0 : n < .6 ? 1 : n < .85 ? 2 : 3]);
            }
        var r = Rng(s);
        for (int i = 0; i < 7; i++)
        {
            int x = (int)(2 + r() * 28), y = (int)(3 + r() * 27);
            b.Set(x, y, P["g4"]); b.Set(x, y - 1, P["g4"]); b.Set(x + 1, y, P["g1"]); b.Set(x - 1, y - 1, P["g3"]);
        }
        string[] a = { "lf2", "lf3", "lf1", "lf4" }, c = { "lf1", "lf2", "lf2", "lf3" };
        for (int i = 0; i < 4; i++)
        {
            int x = (int)(r() * 31), y = (int)(r() * 31);
            b.Set(x, y, P[a[i]]); b.Set(x + 1, y, P[c[i]]);
        }
        if (v % 3 == 0) { b.Set(8 + v, 20, P["f4"]); b.Set(20, 9 + v, P["pl4"]); }
        return b;
    }

    public PixelBuffer Dirt(int v)
    {
        var b = new PixelBuffer(32, 32); int s = 31 + v * 5;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double n = Vn(x / 5.0, y / 5.0, s) * .6 + Hash(x, y, s) * .4;
                b.Set(x, y, R["dirt"][n < .3 ? 0 : n < .62 ? 1 : n < .88 ? 2 : 3]);
            }
        var r = Rng(s);
        for (int i = 0; i < 6; i++)
        {
            int x = (int)(r() * 30), y = (int)(r() * 30);
            b.Set(x, y, P["s3"]); b.Set(x + 1, y, P["s4"]); b.Set(x, y + 1, P["s2"]);
        }
        for (int i = 0; i < 3; i++) { int x = (int)(r() * 31), y = (int)(r() * 31); b.Set(x, y, P["lf2"]); }
        return b;
    }

    public PixelBuffer Cobble(int v)
    {
        var b = new PixelBuffer(32, 32); int s = 51 + v * 3;
        b.Rect(0, 0, 32, 32, P["d1"]);
        var r = Rng(s);
        for (int row = 0; row < 6; row++)
        {
            int y0 = row * 6 - 1, off = (row % 2) * 4;
            for (int x0 = -off; x0 < 32; x0 += 8)
            {
                int w = 6 + (r() < .3 ? 1 : 0), h = 4; double bs = r();
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                    {
                        if ((i == 0 || i == w - 1) && (j == 0 || j == h - 1)) continue;
                        int k = bs < .5 ? 2 : 3;
                        if (j == 0 || i == 0) k++;
                        if (j == h - 1 || i == w - 1) k -= 2;
                        b.Set(x0 + i, y0 + j, R["stone"][Math.Max(0, Math.Min(4, k))]);
                    }
            }
        }
        for (int i = 0; i < 2; i++) { int x = (int)(r() * 31), y = (int)(r() * 31); b.Set(x, y, P["lf2"]); }
        return b;
    }

    /// <summary>Damaged cobbles: level 1 or 2 knocks stones out and adds cracks.</summary>
    public PixelBuffer CobbleDamaged(int v, int level)
    {
        var b = Cobble(v); var dirt = Dirt(v + 1);
        double p = level == 1 ? .14 : .34;
        for (int cy = 0; cy < 6; cy++)
            for (int cx = 0; cx < 4; cx++)
                if (Hash(cx, cy, 700 + v * 13 + level) < p)
                    for (int j = 0; j < 5; j++) for (int i = 0; i < 8; i++)
                        b.Set(cx * 8 + i + (cy % 2) * 4, cy * 6 + j - 1, dirt.Get((cx * 8 + i) % 32, (cy * 6 + j + 32) % 32));
        var r = Rng(900 + v + level * 7);
        for (int i = 0; i < level * 2; i++)
        {
            int x = (int)(r() * 26), y = (int)(r() * 26);
            b.Line(x, y, x + 2 + (int)(r() * 4), y + 2 + (int)(r() * 4), P["s1"]);
        }
        return b;
    }

    public PixelBuffer Flag()
    {
        var b = new PixelBuffer(32, 32); b.Rect(0, 0, 32, 32, P["s1"]);
        for (int q = 0; q < 4; q++)
        {
            int x0 = (q % 2) * 16, y0 = (q >> 1) * 16;
            for (int j = 1; j < 15; j++)
                for (int i = 1; i < 15; i++)
                {
                    int k = 2;
                    if (i == 1 || j == 1) k = 3;
                    if (i == 14 || j == 14) k = 1;
                    if (Hash(i + x0, j + y0, 3) < .08) k = 1;
                    b.Set(x0 + i, y0 + j, R["stone"][k]);
                }
        }
        return b;
    }

    public PixelBuffer Wood()
    {
        var b = new PixelBuffer(32, 32);
        for (int row = 0; row < 4; row++)
        {
            int y0 = row * 8, seam = (row * 11) % 32;
            for (int j = 0; j < 8; j++)
                for (int i = 0; i < 32; i++)
                {
                    int k = 2;
                    if (j == 0) k = 3;
                    if (j == 7) k = 0;
                    if (i == seam) k = 0;
                    if (Hash(i, j + row * 8, 9) < .12 && j > 0 && j < 7) k = 1;
                    b.Set(i, y0 + j, R["wood"][k]);
                }
            b.Set((seam + 3) % 32, y0 + 3, P["m3"]);
        }
        return b;
    }

    public PixelBuffer Earth()
    {
        var b = new PixelBuffer(32, 32);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double n = Hash(x, y, 77);
                b.Set(x, y, n < .5 ? P["d2"] : n < .85 ? P["d3"] : P["d1"]);
            }
        for (int i = 0; i < 10; i++) b.Set((int)(Hash(i, 1, 5) * 32), (int)(Hash(i, 2, 5) * 32), P["hbl"]);
        return b;
    }

    public PixelBuffer Water(int v)
    {
        var b = new PixelBuffer(32, 32);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double n = Vn(x / 8.0 + v, y / 4.0, 91);
                b.Set(x, y, R["water"][n < .35 ? 0 : n < .7 ? 1 : 2]);
            }
        var r = Rng(93 + v);
        for (int i = 0; i < 6; i++)
        {
            int x = (int)(r() * 26), y = (int)(r() * 30);
            for (int k = 0; k < 4; k++) b.Set(x + k, y, P["wa4"]);
        }
        return b;
    }

    public PixelBuffer Ford()
    {
        var b = new PixelBuffer(32, 32);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double n = Vn(x / 8.0 + 2, y / 4.0, 91);
                b.Set(x, y, R["water"][n < .35 ? 1 : 2]);
            }
        var r = Rng(97);
        for (int i = 0; i < 5; i++)
        {
            int cx = 3 + i * 6 + (int)(r() * 2), cy = 8 + (int)(r() * 16);
            b.Ellipse(cx, cy, 3, 2, P["s3"], true); b.Set(cx - 1, cy - 1, P["s5"]);
        }
        return b;
    }

    /// <summary>kind: "" plain, "win", "door", "open".</summary>
    public PixelBuffer WallStone(string kind)
    {
        var b = new PixelBuffer(32, 32);
        for (int j = 0; j < 10; j++)
            for (int i = 0; i < 32; i++)
            {
                int k = 3;
                if (j == 0) k = 4;
                if (j == 9) k = 1;
                if (Hash(i, j, 4) < .1) k = 2;
                b.Set(i, j, R["stone"][k]);
            }
        b.Rect(0, 10, 32, 22, P["s1"]); // mortar (the concept leaves gaps transparent; tiles here are opaque)
        for (int row = 0; row < 4; row++)
        {
            int y0 = 10 + row * 5 + 1, off = (row % 2) * 5;
            for (int x0 = -off; x0 < 32; x0 += 10)
                for (int j = 0; j < 4; j++)
                    for (int i = 0; i < 9; i++)
                    {
                        int k = 2;
                        if (j == 0) k = 3;
                        if (i == 8 || j == 3) k = 1;
                        if (Hash(x0 + i, y0 + j, 6) < .08) k = 1;
                        b.Set(x0 + i, y0 + j, R["stone"][k]);
                    }
        }
        for (int i = 0; i < 32; i++) { b.Set(i, 10, P["s1"]); b.Set(i, 31, P["out"]); }
        if (kind == "win")
        {
            b.Rect(10, 13, 12, 12, P["out"]); b.Rect(11, 14, 10, 10, P["wa1"]); b.Rect(11, 14, 10, 1, P["wa2"]);
            b.Rect(15, 14, 1, 10, P["w2"]); b.Rect(11, 18, 10, 1, P["w2"]); b.Rect(8, 13, 2, 12, P["w3"]); b.Rect(22, 13, 2, 12, P["w3"]);
        }
        if (kind == "door")
        {
            b.Rect(8, 4, 16, 28, P["out"]);
            for (int j = 5; j < 32; j++)
                for (int i = 9; i < 23; i++)
                {
                    int k = (i - 9) % 4 == 3 ? 1 : 3;
                    if (j == 5) k = 4;
                    b.Set(i, j, R["wood"][k]);
                }
            b.Rect(9, 12, 14, 1, P["m2"]); b.Rect(9, 24, 14, 1, P["m2"]); b.Set(20, 18, P["au3"]); b.Set(20, 19, P["au2"]);
        }
        if (kind == "open") { b.Rect(8, 4, 16, 28, P["out"]); b.Rect(9, 5, 14, 27, P["dk1"]); }
        return b;
    }

    /// <summary>Damaged wall face: chunks knocked out and cracks.</summary>
    public PixelBuffer WallStoneDamaged()
    {
        var b = WallStone("");
        for (int cy = 0; cy < 4; cy++)
            for (int cx = 0; cx < 4; cx++)
                if (Hash(cx, cy, 811) < .25)
                    for (int j = 0; j < 4; j++) for (int i = 0; i < 9; i++)
                        b.Set(cx * 10 - (cy % 2) * 5 + i, 11 + cy * 5 + j, (i + j) % 3 == 0 ? P["s2"] : P["s1"]);
        b.Line(14, 11, 17, 18, P["s1"]); b.Line(17, 18, 15, 25, P["s1"]);
        for (int i = 0; i < 32; i++) b.Set(i, 31, P["out"]);
        return b;
    }

    public PixelBuffer WallTimber(string kind)
    {
        var b = new PixelBuffer(32, 32);
        for (int j = 0; j < 8; j++)
            for (int i = 0; i < 32; i++)
            {
                int k = 2;
                if (j == 0) k = 3;
                if (j == 7) k = 0;
                b.Set(i, j, R["wood"][k]);
            }
        for (int j = 8; j < 32; j++)
            for (int i = 0; i < 32; i++)
                b.Set(i, j, R["plaster"][Hash(i, j, 8) < .2 ? 1 : 2]);
        void Beam(int x0, int y0, int x1, int y1)
        {
            for (double t = 0; t <= 1; t += 1.0 / 40)
            {
                int x = Round(x0 + (x1 - x0) * t), y = Round(y0 + (y1 - y0) * t);
                b.Set(x, y, P["w2"]); b.Set(x + 1, y, P["w1"]);
            }
        }
        b.Rect(0, 8, 32, 2, P["w2"]); b.Rect(0, 30, 32, 2, P["w1"]); b.Rect(0, 8, 2, 24, P["w2"]); b.Rect(30, 8, 2, 24, P["w1"]);
        Beam(2, 10, 15, 29); b.Rect(15, 8, 2, 24, P["w2"]);
        if (kind == "door")
        {
            b.Rect(9, 6, 14, 26, P["out"]);
            for (int j = 7; j < 32; j++) for (int i = 10; i < 22; i++) b.Set(i, j, R["wood"][(i - 10) % 4 == 3 ? 1 : 3]);
            b.Rect(10, 18, 12, 1, P["w1"]); b.Set(19, 20, P["m4"]);
        }
        if (kind == "win") { b.Rect(18, 13, 10, 9, P["out"]); b.Rect(19, 14, 8, 7, P["wa1"]); b.Rect(22, 14, 1, 7, P["w2"]); }
        b.Rect(0, 31, 32, 1, P["out"]);
        return b;
    }

    public PixelBuffer Wheat()
    {
        var b = new PixelBuffer(32, 32); b.Rect(0, 0, 32, 32, P["d2"]);
        for (int row = 0; row < 4; row++)
        {
            int y0 = row * 8 + 6;
            for (int x = 1; x < 32; x += 3)
            {
                int h = 4 + (int)(Hash(x, row, 3) * 3);
                for (int j = 0; j < h; j++) b.Set(x, y0 - j, j > h - 3 ? P["f3"] : P["au2"]);
                b.Set(x + 1, y0 - h + 1, P["au3"]);
            }
        }
        return b;
    }
}
