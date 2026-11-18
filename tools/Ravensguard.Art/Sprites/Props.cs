using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>Tileset v2 props (transparent, outlined): vegetation, field and farm goods, graves, ruins, stalls, cart, trees in three seasons.
/// Foliage is authored in the summer rows (foliage_summer / leaf_summer / conifer_summer / grass_summer) so Seasons.Apply recolours it.</summary>
public sealed class Props
{
    private readonly ColorLookup P;
    private readonly RampLookup R;
    private static readonly ShadeOptions Top = new() { TopHi = true };
    public Props(Palette pal) { P = pal.P; R = pal.R; }

    private PixelBuffer Obj(int w, int h, Action<PixelBuffer> fn) { var b = new PixelBuffer(w, h); fn(b); b.Outline(P["out"]); return b; }

    /// <summary>Paints a snow cap `depth` pixels deep on every pixel with nothing opaque directly above it (call before Outline).</summary>
    private void SnowTops(PixelBuffer b, int depth = 2)
    {
        var c = b.Clone();
        for (int y = 0; y < b.H; y++)
            for (int x = 0; x < b.W; x++)
            {
                if (!c.Opaque(x, y) || c.Get(x, y) == P["out"] || c.Opaque(x, y - 1)) continue;
                for (int k = 0; k < depth; k++)
                    if (c.Opaque(x, y + k) && c.Get(x, y + k) != P["out"]) b.Set(x, y + k, P[k == 0 ? (Hash(x, y, 1101) < .4 ? "sn4" : "sn3") : k == 1 ? "sn3" : "sn2"]);
            }
    }

    /// <summary>Snow cap on a finished sprite: non-outline pixels directly under the outline's top edge become snow (2 px deep).</summary>
    public PixelBuffer Snowy(PixelBuffer src)
    {
        var b = src.Clone(); uint o = P["out"];
        for (int x = 0; x < src.W; x++)
            for (int y = 1; y < src.H; y++)
            {
                uint c = src.Get(x, y);
                if (c == 0 || c == o || src.Get(x, y - 1) != o) continue;
                int depth = 2 + (int)(Hash(x, y, 1103) * 3);                         // ragged cap, 2-4 px
                for (int k = 0; k < depth; k++)
                {
                    uint ck = src.Get(x, y + k); if (ck == 0 || ck == o) break;
                    b.Set(x, y + k, P[k == 0 ? (Hash(x, y, 1104) < .4 ? "sn4" : "sn3") : k == depth - 1 ? "sn2" : "sn3"]);
                }
                break;                                                               // only the topmost surface in this column
            }
        return b;
    }

    public PixelBuffer TrapdoorOpen() => Obj(32, 32, b =>
    {
        b.Rect(3, 12, 26, 17, P["w1"]); b.Rect(5, 14, 22, 13, P["dk1"]); b.Rect(5, 14, 22, 2, P["dk2"]);
        b.Shade(4, 3, 24, 9, R["wood"], Top); b.Rect(4, 7, 24, 1, P["w1"]); b.Rect(4, 10, 24, 1, P["w1"]); b.Rect(5, 11, 4, 1, P["m2"]); b.Rect(23, 11, 4, 1, P["m2"]);
        b.Rect(13, 8, 6, 2, P["m3"]);
    });

    public PixelBuffer LadderDown() => Obj(32, 32, b =>
    {
        b.Rect(3, 4, 26, 25, P["w1"]); b.Rect(5, 6, 22, 21, P["dk1"]);
        b.Rect(9, 3, 3, 14, P["w3"]); b.Rect(20, 3, 3, 14, P["w3"]); b.Rect(9, 3, 1, 14, P["w5"]);
        for (int y = 5; y < 17; y += 4) { b.Rect(12, y, 8, 2, P["w4"]); b.Rect(12, y + 1, 8, 1, P["w2"]); }
        b.Rect(9, 17, 3, 6, P["w2"]); b.Rect(20, 17, 3, 6, P["w2"]);
        for (int y = 18; y < 24; y += 4) b.Rect(12, y, 8, 1, P["w1"]);
    });

    // ------------------------------------------------------------ vegetation
    public PixelBuffer Stump(int v) => Obj(32, 32, b =>
    {
        int h = v == 0 ? 8 : 5;
        for (int y = 0; y < h; y++) for (int x = 8; x < 24; x++) { int k = x < 11 ? 3 : x > 20 ? 1 : 2; if (Hash(x, y, 1111 + v) < .15) k = Math.Max(0, k - 1); b.Set(x, 20 + y, R["wood"][k]); }
        b.Ellipse(16, 20, 8, 4, P["w4"], true); b.Ellipse(16, 20, 5, 2, P["w5"], true); b.Ellipse(16, 20, 2, 1, P["w3"], true); b.Ellipse(16, 20, 8, 4, P["w3"], false);
        b.Set(13, 20, P["w3"]); b.Set(19, 21, P["w3"]);
        for (int x = 6; x < 10; x++) b.Set(x, 24 + h - 5, P["w2"]); for (int x = 22; x < 26; x++) b.Set(x, 24 + h - 5, P["w1"]);   // root flare
        if (v == 1) { b.Rect(10, 14, 12, 3, P["w2"]); b.Set(11, 15, P["w3"]); b.Ellipse(16, 14, 6, 2, P["w4"], true); b.Set(14, 14, P["w5"]); }          // snapped splinters
    });

    /// <summary>kind: round, berry, thorn, bare.</summary>
    public PixelBuffer Bush(string kind) => Obj(32, 32, b =>
    {
        var f = R["foliage_summer"];
        if (kind == "bare")
        {
            for (int i = 0; i < 7; i++) { int x = 8 + i * 2 + (i % 2); b.Line(16, 26, x, 12 + (i % 3) * 2, P["w2"]); }
            for (int i = 0; i < 7; i++) { int x = 8 + i * 2 + (i % 2); b.Set(x, 11 + (i % 3) * 2, P["w3"]); b.Set(x - 1, 14 + (i % 2) * 3, P["w3"]); }
            b.Rect(14, 26, 4, 3, P["w2"]);
            return;
        }
        int rr = kind == "thorn" ? 4 : 5;
        for (int i = 0; i < 6; i++) { double cx = 8 + i * 3.2, cy = 19 + (i % 2) * 2 + (kind == "thorn" ? -2 * (i % 3) : 0); b.Ellipse((int)cx, (int)cy, rr + 1, rr, f[1], true); }
        for (int y = 8; y < 30; y++) for (int x = 3; x < 29; x++)
        {
            if (!b.Opaque(x, y)) continue;
            double lit = (-(x - 16) * .5 - (y - 20)) / 12.0; double n = Hash(x, y, 1121) * .4 + lit * .6 + .4;
            b.Set(x, y, f[n < .3 ? 0 : n < .6 ? 1 : n < .88 ? 2 : 3]);
        }
        if (kind == "berry") for (int i = 0; i < 9; i++) { int x = 7 + (int)(Hash(i, 1, 1123) * 18), y = 14 + (int)(Hash(i, 2, 1123) * 11); if (b.Opaque(x, y)) { b.Set(x, y, P["r3"]); b.Set(x, y + 1, P["r1"]); } }
        if (kind == "thorn") for (int i = 0; i < 8; i++) { int x = 7 + (int)(Hash(i, 1, 1125) * 18), y = 12 + (int)(Hash(i, 2, 1125) * 12); if (b.Opaque(x, y)) b.Set(x, y, P["w2"]); }
    });

    /// <summary>size 0 small, 1 medium, 2 boulder.</summary>
    public PixelBuffer Rock(int size) => Obj(32, 32, b =>
    {
        if (size == 0) { b.Ellipse(16, 23, 5, 3, P["s3"], true); b.Set(14, 21, P["s5"]); b.Set(15, 21, P["s4"]); b.Rect(18, 24, 3, 1, P["s2"]); b.Set(11, 26, P["s2"]); }
        else if (size == 1)
        {
            b.Ellipse(15, 21, 9, 6, P["s3"], true); b.Ellipse(15, 19, 7, 4, P["s4"], true); b.Ellipse(21, 25, 5, 3, P["s2"], true);
            b.Rect(11, 17, 4, 1, P["s5"]); b.Set(10, 18, P["s5"]); b.Line(16, 20, 19, 24, P["s1"]); b.Set(8, 24, P["g2"]); b.Set(9, 25, P["g3"]);
        }
        else
        {
            b.Ellipse(16, 18, 13, 11, P["s3"], true); b.Ellipse(14, 15, 10, 7, P["s4"], true); b.Ellipse(24, 23, 6, 5, P["s2"], true);
            b.Rect(8, 11, 7, 1, P["s5"]); b.Rect(7, 12, 3, 1, P["s5"]); b.Line(15, 18, 20, 27, P["s1"]); b.Line(17, 21, 14, 26, P["s1"]);
            for (int i = 0; i < 8; i++) b.Set(6 + i * 2, 24 + (i % 2), P[i % 3 == 0 ? "g3" : "g2"]);
        }
    });

    public PixelBuffer Haystack() => Obj(32, 32, b =>
    {
        for (int y = 5; y < 29; y++)
        {
            double t = (y - 5) / 23.0; int hw = (int)(3 + t * 11 + Math.Sin(t * Math.PI) * 1.5);
            for (int x = 16 - hw; x <= 16 + hw; x++)
            {
                int k = x < 16 - hw / 2 ? 3 : x > 16 + hw / 2 ? 1 : 2; if ((y + x / 3) % 5 == 0) k = Math.Max(0, k - 1); if (Hash(x, y, 1131) < .08) k = Math.Min(3, k + 1);
                b.Set(x, y, R["gold"][k]);
            }
        }
        for (int i = 0; i < 6; i++) b.Set(14 + i, 4 - (i == 2 || i == 3 ? 1 : 0), P["au3"]);
        b.Rect(4, 26, 24, 2, P["au1"]); b.Rect(9, 17, 14, 1, P["br2"]);
    });

    public PixelBuffer Sheaves() => Obj(32, 32, b =>
    {
        foreach (var (cx, lean) in new[] { (9, -1), (16, 0), (23, 1) })
        {
            for (int y = 10; y < 27; y++)
            {
                int xo = lean * (26 - y) / 5;
                b.Set(cx + xo - 1, y, y < 14 ? P["au3"] : P["au2"]); b.Set(cx + xo, y, y < 14 ? P["f4"] : P["au3"]); b.Set(cx + xo + 1, y, y < 14 ? P["au3"] : P["au1"]);
                if (y > 11 && y < 20) { b.Set(cx + xo - 2, y, P["au2"]); b.Set(cx + xo + 2, y, P["au1"]); }
            }
            b.Rect(cx - 2 + lean * 2, 19, 5, 1, P["br2"]);
        }
        b.Rect(7, 27, 18, 1, P["au1"]);
    });

    public PixelBuffer Woodpile() => Obj(32, 32, b =>
    {
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < 5 - (row == 2 ? 1 : 0); i++)
            {
                int cx = 6 + i * 5 + (row == 2 ? 3 : 0), cy = 25 - row * 5;
                b.Ellipse(cx, cy, 2, 2, P["w3"], true); b.Set(cx, cy, P["w5"]); b.Set(cx - 1, cy, P["w4"]); b.Set(cx + 1, cy + 1, P["w2"]); b.Ellipse(cx, cy, 2, 2, P["w2"], false);
            }
        b.Rect(4, 27, 24, 2, P["w1"]);
    });

    public PixelBuffer Trough(bool water) => Obj(32, 32, b =>
    {
        b.Shade(3, 14, 26, 11, R["wood"], Top); b.Rect(5, 16, 22, 5, water ? P["wa2"] : P["au2"]); b.Rect(5, 16, 22, 1, water ? P["wa4"] : P["au3"]);
        if (water) { b.Set(10, 18, P["wa3"]); b.Set(18, 19, P["wa4"]); } else for (int x = 6; x < 26; x += 3) b.Set(x, 17, P["au1"]);
        b.Rect(5, 25, 3, 4, P["w2"]); b.Rect(24, 25, 3, 4, P["w2"]);
    });

    public PixelBuffer BarrelStack() => Obj(32, 32, b =>
    {
        foreach (var (cx, cy) in new[] { (9, 22), (23, 22), (16, 12) })
        {
            for (int y = cy - 6; y < cy + 6; y++) { int w = (y < cy - 4 || y > cy + 3) ? 6 : 7; for (int x = cx - w; x < cx + w; x++) b.Set(x, y, R["wood"][(x - cx + w) < 2 ? 4 : (cx + w - x) < 3 ? 1 : 3]); }
            b.Rect(cx - 6, cy - 3, 12, 1, P["m2"]); b.Rect(cx - 6, cy + 2, 12, 1, P["m2"]);
            if (cy == 12) b.Ellipse(cx, cy - 6, 5, 1, P["w4"], true);
        }
    });

    public PixelBuffer BarrelOpen() => Obj(32, 32, b =>
    {
        for (int y = 8; y < 29; y++) { int w = y < 12 || y > 25 ? 9 : 11; for (int x = 16 - w; x < 16 + w; x++) b.Set(x, y, R["wood"][(x - 16 + w) < 3 ? 4 : (16 + w - x) < 4 ? 1 : 3]); }
        foreach (int y in new[] { 11, 18, 25 }) for (int x = 6; x < 26; x++) b.Set(x, y, P["m2"]);
        b.Ellipse(16, 8, 8, 2, P["w2"], true); b.Ellipse(16, 8, 6, 1, P["dk2"], true);
        for (int i = 0; i < 6; i++) b.Ellipse(11 + i * 2, 7, 2, 2, i % 2 == 0 ? P["r3"] : P["f3"], true);
    });

    public PixelBuffer CrateStack() => Obj(32, 32, b =>
    {
        b.Shade(3, 17, 14, 12, R["wood"], Top); b.Rect(3, 17, 14, 2, P["w4"]); b.Line(4, 19, 16, 28, P["w2"]);
        b.Shade(15, 20, 14, 9, R["wood"], Top); b.Rect(15, 20, 14, 2, P["w4"]); b.Rect(15, 24, 14, 1, P["w2"]);
        b.Shade(7, 7, 13, 11, R["wood"], Top); b.Rect(7, 7, 13, 2, P["w4"]); b.Line(8, 9, 19, 17, P["w2"]);
    });

    public PixelBuffer SackPile(bool grain) => Obj(32, 32, b =>
    {
        foreach (var (x, y) in new[] { (4, 17), (15, 18), (9, 8) })
        {
            b.Shade(x, y, 11, 11, grain ? R["gold"] : R["linen"], Top); b.Rect(x + 3, y - 1, 5, 2, grain ? P["au3"] : P["ln2"]); b.Rect(x + 3, y + 1, 5, 1, P["br2"]);
        }
    });

    // ------------------------------------------------------------ signs, graves, ruins
    public PixelBuffer Signpost() => Obj(32, 32, b =>
    {
        b.Rect(15, 7, 3, 22, P["w2"]); b.Rect(15, 7, 1, 22, P["w3"]);
        b.Shade(5, 8, 15, 5, R["wood"], Top); b.Set(20, 8, P["w3"]); b.Set(21, 10, P["w3"]); b.Set(20, 12, P["w3"]); b.Set(8, 10, P["w1"]); b.Set(10, 10, P["w1"]); b.Set(13, 10, P["w1"]);
        b.Shade(15, 15, 13, 5, R["wood"], Top); b.Set(28, 17, P["w3"]); b.Set(18, 17, P["w1"]); b.Set(21, 17, P["w1"]); b.Set(24, 17, P["w1"]);
        b.Rect(13, 28, 7, 2, P["s2"]);
    });

    public PixelBuffer InnSign() => Obj(32, 32, b =>
    {
        b.Rect(6, 4, 2, 25, P["w2"]); b.Rect(6, 4, 17, 2, P["w3"]); b.Line(8, 12, 14, 6, P["w2"]);
        b.Rect(19, 6, 1, 3, P["m2"]); b.Rect(25, 6, 1, 3, P["m2"]);
        b.Shade(16, 9, 14, 11, R["wood"], Top); b.Rect(21, 12, 4, 5, P["au3"]); b.Rect(25, 13, 2, 3, P["au2"]); b.Rect(21, 12, 4, 1, P["pl4"]);
    });

    public PixelBuffer Headstone(int kind) => Obj(32, 32, b =>
    {
        b.Rect(7, 25, 18, 3, P["g2"]); for (int i = 0; i < 7; i++) b.Set(7 + i * 3, 24, P["g3"]);                  // turf at the foot
        if (kind == 0) { b.Shade(11, 14, 10, 12, R["stone"], Top); b.Ellipse(16, 14, 5, 3, P["s3"], true); b.Rect(14, 16, 4, 1, P["s1"]); b.Rect(15, 18, 2, 1, P["s1"]); b.Rect(14, 20, 4, 1, P["s1"]); b.Set(12, 13, P["s4"]); }
        else if (kind == 1) { b.Shade(13, 9, 6, 17, R["stone"], Top); b.Shade(8, 13, 16, 5, R["stone"], Top); b.Rect(15, 11, 2, 12, P["s1"]); b.Rect(11, 15, 10, 1, P["s1"]); }
        else if (kind == 2) { for (int y = 14; y < 26; y++) for (int x = 10 + (26 - y) / 4; x < 21 + (26 - y) / 4; x++) b.Set(x, y, R["stone"][x < 13 + (26 - y) / 4 ? 3 : x > 18 + (26 - y) / 4 ? 1 : 2]); b.Rect(14, 17, 4, 1, P["s1"]); b.Rect(13, 20, 5, 1, P["s1"]); }
        else { b.Ellipse(16, 22, 11, 5, P["d3"], true); b.Ellipse(14, 21, 7, 3, P["d4"], true); for (int i = 0; i < 8; i++) b.Set(8 + i * 2, 24 + (i % 2), P["g2"]); b.Rect(15, 9, 2, 14, P["w3"]); b.Rect(11, 13, 10, 2, P["w3"]); b.Rect(15, 9, 1, 14, P["w4"]); }
    });

    public PixelBuffer RuinWall(string kind) => Obj(32, 32, b =>
    {
        int[] hs = kind == "h" ? new[] { 8, 12, 14, 10, 7, 9, 13, 11, 6, 8 } : kind == "low" ? new[] { 4, 5, 4, 5, 3, 4, 5, 4, 4, 3 } : new[] { 4, 4, 4, 4, 5, 8, 14, 17, 15, 12 };
        for (int c = 0; c < 10; c++)
        {
            int x0 = 1 + c * 3, h = hs[c];
            for (int w = 0; w < 3; w++) for (int j = 0; j < h; j++)
            {
                int y = 28 - j; int k = 2; if (j == h - 1) k = 4; else if (j % 4 == 0) k = 1; if ((x0 + w + j / 4 * 2) % 5 == 0) k = 1; if (Hash(x0 + w, y, 1141) < .1) k = 3;
                b.Set(x0 + w, y, R["stone"][Math.Clamp(k, 0, 4)]);
            }
        }
        if (kind == "corner") for (int j = 0; j < 12; j++) for (int w = 0; w < 4; w++) b.Set(26 + w, 28 - j - (w == 3 ? 0 : 0) , R["stone"][j % 4 == 0 ? 1 : w == 0 ? 3 : 2]);
        for (int i = 0; i < 5; i++) b.Set(3 + i * 5, 29, P["g2"]);
        b.Set(9, 14, P["g3"]); b.Set(10, 14, P["g2"]);
    });

    public PixelBuffer Rubble(int v) => Obj(32, 32, b =>
    {
        var r = Rng(1151 + v);
        for (int i = 0; i < 11; i++)
        {
            int x = 4 + (int)(r() * 20), y = 15 + (int)(r() * 12), w = 3 + (int)(r() * 4), h = 2 + (int)(r() * 3), k = 1 + (int)(r() * 3);
            for (int j = 0; j < h; j++) for (int a = 0; a < w; a++) { if ((a == 0 || a == w - 1) && (j == 0 || j == h - 1)) continue; b.Set(x + a, y + j, R["stone"][Math.Clamp(k + (j == 0 ? 1 : j == h - 1 ? -1 : 0), 0, 4)]); }
        }
        if (v == 1) { b.Rect(8, 21, 12, 2, P["w2"]); b.Rect(9, 21, 6, 1, P["w3"]); b.Set(21, 21, P["br3"]); }      // fallen beam
        for (int i = 0; i < 6; i++) b.Set(5 + (int)(r() * 22), 27 + (int)(r() * 2), P["d2"]);
    });

    public PixelBuffer Gate(bool open) => Obj(32, 32, b =>
    {
        b.Rect(2, 8, 3, 21, P["w2"]); b.Rect(27, 8, 3, 21, P["w2"]); b.Rect(2, 8, 1, 21, P["w3"]);
        if (!open) { b.Rect(5, 12, 22, 3, P["w3"]); b.Rect(5, 20, 22, 3, P["w3"]); b.Line(5, 22, 26, 13, P["w3"]); b.Rect(5, 12, 22, 1, P["w4"]); b.Rect(5, 20, 22, 1, P["w4"]); b.Set(26, 16, P["m3"]); }
        else { b.Rect(5, 12, 3, 17, P["w3"]); b.Rect(5, 12, 1, 17, P["w4"]); b.Rect(8, 12, 7, 2, P["w3"]); b.Rect(8, 22, 7, 2, P["w3"]); b.Set(5, 14, P["m3"]); b.Set(5, 24, P["m3"]); }
    });

    public PixelBuffer Hurdle() => Obj(32, 32, b =>
    {
        foreach (int x in new[] { 4, 11, 18, 25 }) b.Rect(x, 14, 2, 14, P["w2"]);
        for (int y = 16; y < 26; y += 4) { b.Rect(4, y, 23, 2, P["w3"]); b.Rect(4, y, 23, 1, P["w4"]); }
    });

    public PixelBuffer Scarecrow() => Obj(32, 32, b =>
    {
        b.Rect(15, 11, 2, 18, P["w2"]); b.Rect(7, 14, 18, 2, P["w3"]);
        b.Ellipse(16, 8, 3, 3, P["ln2"], true); b.Set(15, 8, P["out"]); b.Set(17, 8, P["out"]); b.Rect(12, 4, 9, 2, P["w3"]); b.Rect(14, 2, 5, 3, P["w2"]);
        b.Shade(11, 15, 11, 8, R["brown"], Top); b.Set(8, 17, P["au3"]); b.Set(9, 18, P["au2"]); b.Set(24, 17, P["au3"]); b.Set(23, 18, P["au2"]); b.Set(12, 24, P["au2"]); b.Set(19, 24, P["au3"]);
    });

    // ------------------------------------------------------------ 64x48 pieces
    public PixelBuffer Stall(string kind) => Obj(64, 48, b =>
    {
        var rc = kind == "yellow" ? R["gold"] : kind == "brown" ? R["brown"] : R["plaster"];
        b.Rect(4, 14, 2, 30, P["w2"]); b.Rect(58, 14, 2, 30, P["w2"]);
        if (kind != "plain")
            for (int x = 2; x < 62; x++) for (int y = 6; y < 16; y++)
            {
                int st = ((x - 2) >> 2) % 2; b.Set(x, y, st != 0 ? P["pl4"] : rc[2]); if (y == 15) b.Set(x, y, st != 0 ? P["pl2"] : rc[1]);
            }
        else { b.Rect(2, 8, 60, 8, P["w3"]); b.Rect(2, 8, 60, 1, P["w4"]); b.Rect(2, 15, 60, 1, P["w1"]); for (int x = 6; x < 60; x += 8) b.Set(x, 11, P["w2"]); }
        b.Shade(6, 30, 52, 12, R["wood"], Top);
        if (kind == "yellow") foreach (var (qx, c) in new[] { (12, "pl4"), (20, "r3"), (28, "b3"), (36, "pl3"), (44, "au3"), (52, "r2") }) { b.Rect(qx - 4, 26, 8, 4, P[c]); b.Rect(qx - 4, 26, 8, 1, P["pl4"]); }       // cloth bolts
        else if (kind == "brown") foreach (var qx in new[] { 12, 22, 32, 42, 52 }) { b.Ellipse(qx, 27, 3, 3, P["r2"], true); b.Set(qx - 1, 26, P["r4"]); b.Rect(qx, 17, 1, 6, P["w2"]); b.Ellipse(qx, 23, 2, 3, P["k2"], true); } // hanging meat and joints
        else foreach (var qx in new[] { 12, 24, 36, 48 }) { b.Shade(qx - 5, 24, 10, 7, R["wood"], Top); b.Ellipse(qx - 2, 24, 2, 2, P["au3"], true); b.Ellipse(qx + 2, 24, 2, 2, P["f3"], true); }                    // crates of grain
    });

    public PixelBuffer CartLoaded(string load) => Obj(64, 48, b =>
    {
        b.Shade(4, 14, 52, 16, R["wood"], Top);
        for (int x = 8; x < 56; x += 10) b.Rect(x, 14, 1, 16, P["w1"]);
        b.Rect(54, 22, 9, 2, P["w2"]);
        if (load == "hay") { b.Shade(6, 4, 46, 12, R["gold"], Top); for (int x = 8; x < 50; x += 3) b.Set(x, 5 + (x % 4), P["au1"]); }
        else if (load == "barrels") foreach (int cx in new[] { 14, 28, 42 }) { b.Shade(cx - 7, 4, 14, 12, R["wood"]); b.Rect(cx - 7, 8, 14, 1, P["m2"]); b.Rect(cx - 7, 13, 14, 1, P["m2"]); }
        else { b.Shade(8, 6, 14, 9, R["wood"], Top); b.Shade(26, 5, 12, 10, R["linen"], Top); b.Shade(40, 7, 12, 8, R["linen"], Top); }
        foreach (var (wx, wy) in new[] { (14, 36), (46, 36) })
        {
            b.Ellipse(wx, wy, 9, 9, P["w2"], false);
            for (double a = 0; a < 6.28; a += .785) b.Line(wx, wy, Round(wx + Math.Cos(a) * 8), Round(wy + Math.Sin(a) * 8), P["w3"]);
            b.Rect(wx - 1, wy - 1, 3, 3, P["m2"]);
        }
    });

    /// <summary>A draught animal in side view facing east (continent tick v1, caravans): an ox (heavy, horned, brown) or a horse (leaner, mane and tail, leather and brown). 40 wide on a 64 x 48 sheet cell, standing on the bottom rows.</summary>
    public PixelBuffer Draught(string kind) => Obj(64, 48, b =>
    {
        bool ox = kind == "ox";
        var coat = ox ? R["brown"] : R["leather"];
        int bx = 10, by = ox ? 20 : 18, bw = ox ? 34 : 30, bh = ox ? 15 : 13;
        b.Shade(bx, by, bw, bh, coat, Top);                                                          // barrel of the body
        if (ox) b.Shade(bx + 20, by - 3, 11, 5, coat, Top);                                          // the withers' hump
        // four legs with dark hooves; the far pair a shade darker
        foreach (var lx in new[] { bx + 2, bx + 7, bx + bw - 10, bx + bw - 5 }) { b.Shade(lx, by + bh - 1, 4, 11, coat); b.Rect(lx, by + bh + 8, 4, 2, P["dk1"]); }
        // neck, head and muzzle to the east
        b.Shade(bx + bw - 4, by - (ox ? 1 : 5), 9, ox ? 10 : 13, coat, Top);
        b.Shade(bx + bw + 3, by + (ox ? 6 : 3), ox ? 11 : 10, ox ? 8 : 6, coat, Top);
        b.Rect(bx + bw + (ox ? 11 : 10), by + (ox ? 10 : 6), 3, 3, P["pl3"]);                        // pale muzzle
        b.Rect(bx + bw + 6, by + (ox ? 7 : 4), 2, 2, P["dk1"]);                                       // eye
        if (ox) { b.Line(bx + bw + 3, by + 5, bx + bw + 1, by + 1, P["pl2"]); b.Line(bx + bw + 8, by + 5, bx + bw + 10, by + 1, P["pl2"]); }   // horns
        else { b.Rect(bx + bw - 3, by - 5, 3, 9, P["w1"]); b.Rect(bx + bw + 5, by + 1, 2, 3, P["w2"]); }                                         // mane, ears
        b.Line(bx, by + 3, bx - 3, by + bh + 3, ox ? P["br1"] : P["w1"]);                             // tail
        // a yoke or collar
        b.Rect(bx + bw - 6, by + 1, 3, 9, P["w3"]);
    });

    /// <summary>A riding horse in side view facing east, saddled (horses-and-carts spec 6): taller and finer than the draught horse, 64 x 64 with its hooves 6 rows above the bottom. The rider is the rig's "ride" pose drawn over it.</summary>
    public PixelBuffer Mount() => Obj(64, 64, b =>
    {
        var coat = R["leather"];
        int bx = 9, by = 26, bw = 36, bh = 15;
        b.Shade(bx, by, bw, bh, coat, Top);                                                                  // barrel
        b.Shade(bx + bw - 6, by - 1, 8, bh - 2, coat, Top);                                                  // chest
        foreach (var lx in new[] { bx + 2, bx + 8, bx + bw - 11, bx + bw - 5 })                              // four legs, long, with hooves and a darker far pair
        {
            b.Shade(lx, by + bh - 1, 4, 18, coat); b.Rect(lx, by + bh + 14, 4, 4, coat[1]); b.Rect(lx, by + bh + 17, 4, 2, P["dk1"]);
        }
        b.Shade(bx + bw - 3, by - 17, 8, 22, coat, Top);                                                      // the neck rises to the east
        b.Shade(bx + bw + 2, by - 20, 12, 7, coat, Top);                                                      // head
        b.Shade(bx + bw + 8, by - 15, 7, 6, coat, Top);                                                       // muzzle
        b.Rect(bx + bw + 12, by - 12, 3, 3, P["pl3"]); b.Rect(bx + bw + 7, by - 18, 2, 2, P["dk1"]);        // pale nose, eye
        b.Rect(bx + bw + 3, by - 24, 2, 4, P["w1"]); b.Rect(bx + bw + 6, by - 24, 2, 4, P["w1"]);            // ears
        b.Rect(bx + bw - 4, by - 18, 3, 18, P["w1"]);                                                         // mane down the neck
        b.Line(bx, by + 2, bx - 4, by + 20, P["w1"]); b.Line(bx + 1, by + 3, bx - 3, by + 21, P["w1"]);     // tail
        b.Shade(bx + 11, by - 2, 16, 5, R["red"], Top);                                                       // saddle cloth
        b.Shade(bx + 13, by - 5, 12, 4, R["brown"], Top);                                                     // the saddle
        b.Rect(bx + 24, by - 7, 2, 3, P["br1"]);                                                              // pommel
        b.Line(bx + 18, by + 3, bx + 18, by + 13, P["br1"]); b.Rect(bx + 17, by + 13, 3, 2, P["m3"]);        // stirrup leather and iron
        b.Line(bx + bw + 9, by - 12, bx + bw + 3, by + 1, P["br1"]);                                          // rein
    });

    public PixelBuffer Barrow() => Obj(64, 48, b =>
    {
        for (int y = 8; y < 44; y++)
            for (int x = 3; x < 61; x++)
            {
                double dx = (x - 32) / 28.0, dy = (y - 30) / 20.0; if (dx * dx + dy * dy > 1 || y > 40 + (int)(Hash(x, 1, 1161) * 3)) continue;
                double lit = (-(x - 32) * .4 - (y - 24)) / 26.0; double n = Vn(x / 5.0, y / 5.0, 1163) * .35 + Hash(x, y, 1165) * .25 + lit * .5 + .3;
                b.Set(x, y, R["grass"][n < .35 ? 0 : n < .62 ? 1 : n < .85 ? 2 : 3]);
            }
        b.Rect(23, 28, 18, 13, P["s2"]); b.Rect(24, 29, 16, 12, P["out"]); b.Rect(25, 31, 14, 10, P["dk1"]);                 // entrance passage
        b.Shade(21, 25, 22, 5, R["stone"], Top); b.Shade(21, 29, 4, 12, R["stone"]); b.Shade(39, 29, 4, 12, R["stone"]);   // lintel and jambs
        b.Rect(22, 40, 20, 2, P["s3"]);
        b.Shade(28, 3, 5, 10, R["stone"], Top); b.Rect(28, 3, 5, 1, P["s5"]);                                              // standing stone on the crown
        for (int i = 0; i < 9; i++) b.Set(8 + i * 5, 36 + (i % 3), P[i % 2 == 0 ? "g1" : "s3"]);
    });

    // ------------------------------------------------------------ trees (64x80)
    /// <summary>Canopy as overlapping shaded blobs scattered around (cx0, cy0) within +-sx / +-sy; radii r0..r1.</summary>
    private void Blobs(PixelBuffer b, uint[] cols, int seed, int n, double cx0, double cy0, double sx, double sy, double r0, double r1)
    {
        var r = Rng(seed);
        for (int i = 0; i < n; i++)
        {
            double rr = r0 + r() * (r1 - r0);
            double a = i * 2.399963 + r() * .6, d = Math.Sqrt((i + .5) / n);          // sunflower spread keeps the crown evenly covered
            double cx = cx0 + Math.Cos(a) * d * sx, cy = cy0 + Math.Sin(a) * d * sy;
            cy = Math.Max(cy, rr + 1.5); cx = Math.Clamp(cx, rr + 1.5, 62.5 - rr);
            for (double y = -rr; y <= rr; y++)
                for (double x = -rr; x <= rr; x++)
                {
                    if (x * x + y * y > rr * rr) continue;
                    double lit = (-x - y) / (rr * 1.4); double n2 = Hash((int)(cx + x), (int)(cy + y), seed) * .4 + lit * .6 + .4;
                    b.Set(cx + x, cy + y, cols[n2 < .35 ? 0 : n2 < .65 ? 1 : n2 < .9 ? 2 : 3]);
                }
        }
    }

    private void Trunk(PixelBuffer b, int x, int y0, int y1, int w, uint[] ramp, bool flare = true)
    {
        b.Shade(x, y0, w, y1 - y0, ramp);
        if (flare) { b.Rect(x - 2, y1 - 3, w + 4, 3, ramp[1]); b.Rect(x - 2, y1 - 3, 2, 1, ramp[2]); }
    }

    private void Branch(PixelBuffer b, double x, double y, double ang, double len, int th, int depth, Func<double> r, uint[] ramp, List<(int, int, int, int)> twigs)
    {
        double x1 = x + Math.Cos(ang) * len, y1 = y - Math.Sin(ang) * len;
        for (double t = 0; t <= 1; t += 1.0 / (len + 1))
        {
            double px = x + (x1 - x) * t, py = y + (y1 - y) * t;
            if (px < 3 || px > 59 || py < 3) continue;
            for (int a = 0; a < th; a++) for (int c = 0; c < th; c++) b.Set(px + a, py + c, ramp[a == 0 ? 3 : a == th - 1 ? 1 : 2]);
        }
        if (depth <= 0 || len < 4) { twigs.Add(((int)x1, (int)y1, (int)Math.Round(Math.Cos(ang) * 4), (int)Math.Round(-Math.Sin(ang) * 4))); return; }
        int nb = depth > 1 ? 2 : 3;
        for (int i = 0; i < nb; i++)
        {
            double a2 = ang + (i - (nb - 1) / 2.0) * (0.55 + r() * .35) + (r() - .5) * .25;
            Branch(b, x1, y1, a2, len * (0.62 + r() * .15), Math.Max(1, th - 1), depth - 1, r, ramp, twigs);
        }
    }

    /// <summary>Apple crown dressing by season: "blossom" (spring: white and pink), "green" (summer: a few small unripe apples), "ripe" (autumn: red apples, some fallen at the foot).</summary>
    private void AppleFruit(PixelBuffer b, string fruit)
    {
        if (fruit == "blossom")
        {
            for (int i = 0; i < 70; i++)
            {
                int x = 14 + (int)(Hash(i, 1, 1181) * 38), y = 14 + (int)(Hash(i, 2, 1181) * 28);
                if (!b.Opaque(x, y)) continue;
                bool pink = Hash(i, 3, 1181) < .45;
                b.Set(x, y, pink ? P["fp"] : P["pl4"]);
                if (Hash(i, 4, 1181) < .6) b.Set(x + 1, y, pink ? P["pl4"] : P["fp"]);
                if (Hash(i, 5, 1181) < .3) b.Set(x, y + 1, P["pl4"]);
            }
        }
        else if (fruit == "green")
        {
            for (int i = 0; i < 7; i++)
            {
                int x = 17 + (int)(Hash(i, 1, 1191) * 30), y = 18 + (int)(Hash(i, 2, 1191) * 22);
                if (b.Opaque(x, y) && b.Opaque(x + 1, y + 1)) { b.Set(x, y, P["gs4"]); b.Set(x + 1, y, P["gs3"]); b.Set(x, y + 1, P["gs3"]); }
            }
        }
        else if (fruit == "ripe")
        {
            for (int i = 0; i < 20; i++)
            {
                int x = 15 + (int)(Hash(i, 1, 1171) * 34), y = 15 + (int)(Hash(i, 2, 1171) * 26);
                if (!b.Opaque(x, y) || !b.Opaque(x + 1, y + 1)) continue;
                b.Set(x, y, P["r4"]); b.Set(x + 1, y, P["r3"]); b.Set(x, y + 1, P["r3"]); b.Set(x + 1, y + 1, P["r2"]);
            }
            for (int i = 0; i < 4; i++) { int x = 19 + i * 6 + (i & 1) * 2, y = 76 + (i % 2); b.Set(x, y, P["r3"]); b.Set(x + 1, y, P["r2"]); b.Set(x, y - 1, P["r4"]); }
        }
    }

    /// <summary>A timber lychgate (64 x 48): two posts one cell apart, a cross beam and a tiled gable, open between the posts so the path shows through.</summary>
    public PixelBuffer Lychgate() => Obj(64, 48, b =>
    {
        var t = R["tile"];
        foreach (int px in new[] { 17, 43 }) { b.Rect(px, 15, 4, 32, P["w2"]); b.Rect(px, 15, 1, 32, P["w4"]); b.Rect(px + 3, 15, 1, 32, P["w1"]); }
        b.Rect(15, 45, 8, 2, P["s3"]); b.Rect(41, 45, 8, 2, P["s3"]); b.Rect(15, 45, 8, 1, P["s4"]); b.Rect(41, 45, 8, 1, P["s4"]);
        b.Rect(17, 15, 30, 3, P["w3"]); b.Rect(17, 15, 30, 1, P["w4"]); b.Rect(17, 17, 30, 1, P["w1"]);
        b.Line(21, 26, 27, 18, P["w3"]); b.Line(22, 26, 28, 18, P["w2"]); b.Line(42, 26, 36, 18, P["w3"]); b.Line(41, 26, 35, 18, P["w2"]);
        for (int y = 4; y <= 14; y++)
        {
            int hw = 5 + (y - 4) * 22 / 10;
            for (int x = 32 - hw; x <= 32 + hw; x++)
            {
                bool edge = x - (32 - hw) < 3 || (32 + hw) - x < 3 || y >= 13;
                if (edge)
                {
                    int course = (y + (x >> 1)) % 4 == 0 ? 0 : 1;
                    int k = x < 32 ? 2 + course : 1 + course; if (y >= 13) k = 0;
                    b.Set(x, y, t[Math.Clamp(k, 0, 3)]);
                }
                else b.Set(x, y, (x + y) % 7 == 0 ? P["pl2"] : P["pl3"]);
            }
        }
        b.Rect(31, 5, 2, 8, P["w2"]);
        for (int x = 29; x <= 35; x++) b.Set(x, 3, t[3]);
    });

    /// <summary>species: oak ash birch pine apple; style: summer | winter. Autumn = Seasons recolour of the summer sprite.</summary>
    public PixelBuffer Tree(string species, bool winter, string fruit = "")
    {
        var b = new PixelBuffer(64, 80);
        int seed = 1200 + species.Length * 17 + species[0];
        var wood = species == "birch" ? new[] { P["ln1"], P["ln2"], P["ln3"], P["pl4"] } : species == "ash" ? new[] { P["br1"], P["br2"], P["br3"], P["br4"] } : R["wood"];
        var twigs = new List<(int, int, int, int)>();
        if (!winter || species == "pine")
        {
            if (species == "pine")
            {
                var c = R["conifer_summer"]; var rg = Rng(seed);
                Trunk(b, 29, 58, 77, 6, R["wood"]);
                for (int t = 0; t < 6; t++)
                {
                    int y0 = 6 + t * 9, hw = 6 + t * 4, hh = 14;
                    for (int y = 0; y < hh; y++)
                    {
                        double w = hw * (0.15 + 0.85 * y / (hh - 1.0)) + (Hash(y, t, seed) - .5) * 2.2;
                        for (double x = -w; x <= w; x++)
                        {
                            double lit = (-x / (hw + 1.0)) * .6 - (y / (double)hh) * .45 + .55 + (Hash((int)x + 40, y + t * 14, seed) - .5) * .4;
                            int k = lit < .3 ? 0 : lit < .55 ? 1 : lit < .8 ? 2 : 3;
                            if (y > hh - 3) k = Math.Max(0, k - 1);
                            b.Set(32 + x, y0 + y, c[k]);
                        }
                    }
                }
                if (winter) SnowTops(b, 2);
            }
            else
            {
                Trunk(b, species == "birch" ? 30 : species == "apple" ? 27 : 28, species == "apple" ? 38 : species == "birch" ? 30 : species == "ash" ? 34 : 40, 77, species == "birch" ? 4 : species == "apple" ? 9 : 8, wood);
                if (species == "birch") { for (int y = 54; y < 76; y += 4) { b.Rect(30, y, 2, 1, P["hb"]); b.Set(33, y + 2, P["hb"]); } }
                if (species == "oak") { b.Line(28, 52, 18, 40, P["w2"]); b.Line(35, 52, 46, 41, P["w2"]); b.Line(29, 53, 20, 44, P["w3"]); }
                var f = species == "oak" ? R["foliage_summer"] : R["leaf_summer"];
                switch (species)
                {
                    case "oak": Blobs(b, f, seed, 15, 32, 29, 21, 11, 9, 14); break;
                    case "ash": Blobs(b, f, seed, 15, 32, 29, 14, 17, 7, 11); break;
                    case "birch": Blobs(b, f, seed, 12, 32, 28, 12, 13, 6, 9); for (int i = 0; i < 10; i++) { int x = 20 + (int)(Hash(i, 1, seed) * 24); for (int j = 0; j < 4; j++) b.Set(x, 38 + j + (int)(Hash(i, 2, seed) * 5), j == 3 ? f[3] : f[1]); } break;
                    default: Blobs(b, f, seed, 12, 32, 31, 17, 9, 8, 11); break;
                }
                if (species == "apple") AppleFruit(b, fruit == "" ? "green" : fruit);
            }
        }
        else
        {
            // bare winter silhouette
            var rg = Rng(seed + 3);
            int tx = species == "birch" ? 30 : species == "apple" ? 27 : 28, tw = species == "birch" ? 4 : species == "apple" ? 9 : 8, ty = species == "apple" ? 46 : 48;
            Trunk(b, tx, ty, 77, tw, wood);
            if (species == "birch") for (int y = 54; y < 76; y += 4) { b.Rect(30, y, 2, 1, P["hb"]); b.Set(33, y + 2, P["hb"]); }
            double bx = tx + tw / 2.0, by = ty + 2;
            int th = species == "oak" ? 4 : species == "apple" ? 3 : species == "ash" ? 3 : 2;
            double len = species == "oak" ? 15 : species == "ash" ? 16 : species == "birch" ? 15 : 11;
            int n = species == "birch" ? 4 : 5;
            for (int i = 0; i < n; i++)
            {
                double ang = Math.PI / 2 + (i - (n - 1) / 2.0) * (species == "ash" || species == "birch" ? .42 : .62) + (rg() - .5) * .2;
                Branch(b, bx - th / 2.0, by + (i % 2) * 3, ang, len * (.85 + rg() * .3), th, species == "oak" ? 3 : 3, rg, wood, twigs);
            }
        }
        b.Outline(P["out"]);
        if (winter && species != "pine")
            foreach (var (x, y, dx, dy) in twigs)
            {
                // twig tips in the outline colour so thin ends need no halo
                for (int k = 1; k <= 3; k++) { int px = x + dx * k / 3, py = y + dy * k / 3; if (px > 0 && px < 63 && py > 0 && py < 79 && !b.Opaque(px, py)) b.Set(px, py, P["out"]); }
            }
        return b;
    }
}
