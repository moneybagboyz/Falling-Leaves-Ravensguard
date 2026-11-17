using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>Tileset v2 ground: grass states, farmland, mud, tracks, sand, gravel, rock, marsh, water, ice, forest floor. All opaque 32x32.
/// Grass-family and foliage-family colours are authored in the summer rows so the season swap works on every tile here.</summary>
public sealed class Ground
{
    private readonly ColorLookup P;
    private readonly RampLookup R;
    public Ground(Palette pal) { P = pal.P; R = pal.R; }

    internal static PixelBuffer New() => new(32, 32);

    /// <summary>Noise fill: idx = number of thresholds below the mixed value-noise/hash sample.</summary>
    internal static void Fill(PixelBuffer b, uint[] ramp, double scale, int seed, double[] th, double mix = .6, double sy = 1)
    {
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double n = Vn(x / scale, y / (scale * sy), seed) * mix + Hash(x, y, seed) * (1 - mix);
                int i = 0; while (i < th.Length && n >= th[i]) i++;
                b.Set(x, y, ramp[Math.Min(i, ramp.Length - 1)]);
            }
    }

    private PixelBuffer GrassBase(int s, int shift = 0)
    {
        var b = New();
        Fill(b, R["grass"], 7, s, new[] { .33, .6, .85 }, .65);
        return b;
    }

    public PixelBuffer GrassLong(int v)
    {
        var b = New(); int s = 211 + v * 9;
        Fill(b, R["grass"], 6, s, new[] { .45, .72, .93 }, .7);
        var r = Rng(s);
        for (int i = 0; i < 90; i++)
        {
            int x = (int)(r() * 32), y = (int)(3 + r() * 29), h = 3 + (int)(r() * 3), lean = r() < .5 ? -1 : 1; bool lns = r() < .4;
            for (int j = 0; j < h; j++)
            {
                int xx = x + (lns && j > h / 2 ? lean : 0);
                b.Set(xx, y - j, j == 0 ? P["g1"] : j == h - 1 ? P["g4"] : j > h / 2 ? P["g3"] : P["g2"]);
            }
        }
        if (v == 1) for (int i = 0; i < 9; i++) { int x = (int)(r() * 31), y = (int)(r() * 28) + 2; b.Set(x, y, P["au2"]); b.Set(x, y - 1, P["au3"]); } // seeding heads
        return b;
    }

    /// <summary>Meadow: grass with flowers. v0 white + yellow, v1 red + blue, v2 mixed.</summary>
    public PixelBuffer Meadow(int v)
    {
        var b = New(); int s = 233 + v * 5;
        Fill(b, R["grass"], 7, s, new[] { .33, .6, .85 }, .65);
        string[][] cols = { new[] { "pl4", "f4", "pl4" }, new[] { "r4", "b4", "r3" }, new[] { "fv", "fp", "f3", "pl4" } };
        var r = Rng(s);
        for (int i = 0; i < 16; i++)
        {
            int x = 1 + (int)(r() * 29), y = 2 + (int)(r() * 28); var c = P[cols[v][i % cols[v].Length]];
            b.Set(x, y, c); b.Set(x + 1, y, c); b.Set(x, y + 1, P["f4"] == c ? P["f3"] : c); b.Set(x, y + 2, P["g1"]);
            if (i % 3 == 0) { b.Set(x - 1, y - 1, c); b.Set(x, y, P["f3"]); }
        }
        return b;
    }

    // ---- farmland: furrow rows run east-west, 6px period ----
    private PixelBuffer Furrows(int seed, bool wet = false)
    {
        var b = New();
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                int ph = y % 6; double h = Hash(x, y, seed);
                uint c = ph switch { 0 => R["dirt"][3], 1 => R["dirt"][2], 2 => R["dirt"][2], 3 => R["dirt"][1], 4 => R["dirt"][0], _ => R["dirt"][1] };
                if (ph == 0 && h < .3) c = R["dirt"][2];
                if ((ph == 4 || ph == 5) && h < .12) c = R["dirt"][0];
                if (wet) c = ph == 0 ? P["mu3"] : ph < 3 ? P["mu2"] : P["mu1"];
                b.Set(x, y, c);
            }
        var r = Rng(seed);
        for (int i = 0; i < 6; i++) { int x = (int)(r() * 30), y = (int)(r() * 30); b.Set(x, y, P["d4"]); b.Set(x + 1, y, P["d3"]); b.Set(x, y + 1, P["d1"]); }
        return b;
    }
    public PixelBuffer Ploughed() => Furrows(241);
    public PixelBuffer Sown()
    {
        var b = Furrows(242); var r = Rng(243);
        for (int row = 0; row < 6; row++)
            for (int i = 0; i < 6; i++) { int x = (int)(r() * 31), y = row * 6 + 3 + (int)(r() * 2); b.Set(x, y, P["au3"]); if (i % 3 == 0) b.Set(x + 1, y, P["pl3"]); }
        return b;
    }
    public PixelBuffer YoungCrop()
    {
        var b = Furrows(244);
        for (int row = 0; row < 6; row++)
            for (int x = 1 + (row % 2) * 2; x < 32; x += 4)
            {
                int y = row * 6 + 3;
                b.Set(x, y, P["gr2"]); b.Set(x, y - 1, P["gr3"]); b.Set(x - 1, y - 2, P["fo4"]); b.Set(x + 1, y - 2, P["gr3"]); b.Set(x, y - 2, P["fo4"]);
            }
        return b;
    }
    /// <summary>Ripe crop: dense standing stalks in rows. wheat = golden straight ears; barley = paler, bent ears with awns.</summary>
    public PixelBuffer Ripe(bool barley)
    {
        var b = New(); int s = barley ? 262 : 252;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) b.Set(x, y, Hash(x, y, s) < .3 ? P["d1"] : P["d2"]);
        for (int row = 0; row < 4; row++)
        {
            int y0 = row * 8 + 7;
            for (int x = 0; x < 32; x++)
            {
                if (((x + row) & 1) != 0 && Hash(x, row, s + 1) < .35) continue;
                int h = 5 + (int)(Hash(x, row, s) * 3);
                for (int j = 0; j < h; j++)
                {
                    uint c = barley ? (j > h - 3 ? P["pl3"] : j > h - 5 ? P["au3"] : P["au2"]) : (j > h - 3 ? P["au3"] : j > h - 5 ? P["au2"] : P["au1"]);
                    b.Set(x, y0 - j, c);
                }
                if (barley) { b.Set(x + 1, y0 - h + 1, P["f4"]); b.Set(x + 1, y0 - h, P["pl3"]); b.Set(x + 2, y0 - h - 1 + 1, P["au3"]); }
                else { b.Set(x, y0 - h, P["f4"]); if (x % 3 == 0) b.Set(x + 1, y0 - h + 2, P["f4"]); }
            }
        }
        return b;
    }
    public PixelBuffer Stubble()
    {
        var b = New(); int s = 271;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { double h = Hash(x, y, s); b.Set(x, y, (y % 6 < 2) ? (h < .5 ? P["d3"] : P["d2"]) : (h < .5 ? P["d2"] : P["d1"])); }
        var r = Rng(s);
        for (int i = 0; i < 90; i++)
        {
            int x = (int)(r() * 31), y = (int)(r() * 31), l = 1 + (int)(r() * 2);
            for (int k = 0; k < l; k++) b.Set(x, y - k, k == l - 1 ? P["au3"] : P["au2"]);
        }
        return b;
    }
    public PixelBuffer Fallow()
    {
        var b = New(); int s = 281;
        Fill(b, R["dirt"], 5, s, new[] { .3, .62, .9 }, .6);
        var r = Rng(s);
        for (int i = 0; i < 70; i++)
        {
            int x = (int)(r() * 31), y = 2 + (int)(r() * 29);
            b.Set(x, y, P["g2"]); b.Set(x, y - 1, P["g3"]); if (i % 3 == 0) { b.Set(x + 1, y, P["g1"]); b.Set(x - 1, y - 1, P["g3"]); }
        }
        for (int i = 0; i < 4; i++) { int x = (int)(r() * 30), y = (int)(r() * 30); b.Set(x, y, P["pl4"]); b.Set(x, y + 1, P["g1"]); }
        return b;
    }

    // ---- mud / puddle / track ----
    public PixelBuffer Mud(int v)
    {
        var b = New(); int s = 291 + v * 3;
        Fill(b, R["mud"], 4.5, s, new[] { .42, .78 }, .7);
        var r = Rng(s);
        for (int i = 0; i < 9; i++) { int x = (int)(r() * 28), y = (int)(r() * 30); b.Set(x, y, P["mu3"]); b.Set(x + 1, y, P["mu3"]); b.Set(x + 2, y, P["d3"]); }       // wet sheen
        for (int i = 0; i < 4; i++) { int x = (int)(r() * 28), y = (int)(r() * 30); b.Set(x, y, P["wa2"]); b.Set(x + 1, y, P["wa3"]); }                        // tiny wet glints
        if (v == 1) for (int i = 0; i < 24; i++) b.Set(2 + i, 14 + (int)(Math.Sin(i * .5) * 2), P["mu1"]);                                                  // a drag mark
        return b;
    }

    /// <summary>Mud with standing water. v0 one pool, v1 two small ones.</summary>
    public PixelBuffer PuddleMud(int v)
    {
        var b = Mud(v + 2);
        var pools = v == 0 ? new[] { (15.0, 16.0, 11.0, 7.0) } : new[] { (9.0, 10.0, 6.0, 4.0), (22.0, 22.0, 7.0, 5.0) };
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                foreach (var (cx, cy, rx, ry) in pools)
                {
                    double dx = (x - cx) / rx, dy = (y - cy) / ry, d = dx * dx + dy * dy + (Hash(x, y, 301) - .5) * .25;
                    if (d > 1) continue;
                    uint c = d > .78 ? P["wa1"] : d > .3 ? P["wa2"] : P["wa3"];
                    b.Set(x, y, c);
                }
        foreach (var (cx, cy, rx, ry) in pools) { int px = (int)(cx - rx * .35), py = (int)(cy - ry * .35); b.Set(px, py, P["wa4"]); b.Set(px + 1, py, P["wa4"]); b.Set(px + 3, py + 1, P["wa3"]); }
        return b;
    }

    /// <summary>Hard-packed track with two wheel ruts. horizontal: ruts run east-west.</summary>
    public PixelBuffer Track(int v, bool horizontal)
    {
        var b = New(); int s = 311 + v * 4;
        Fill(b, R["dirt"], 6, s, new[] { .22, .6, .9 }, .5);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                int u = horizontal ? y : x; // across the track
                bool rut = (u >= 8 && u <= 11) || (u >= 20 && u <= 23);
                if (!rut) continue;
                bool edge = u == 8 || u == 20, lip = u == 11 || u == 23;
                double h = Hash(x, y, s + 2);
                b.Set(x, y, edge ? P["d1"] : lip ? (h < .5 ? P["d4"] : P["d3"]) : (h < .3 ? P["d1"] : P["d2"]));
            }
        var r = Rng(s);
        for (int i = 0; i < 5; i++) { int x = (int)(r() * 30), y = (int)(r() * 30); b.Set(x, y, P["s3"]); b.Set(x + 1, y, P["s4"]); b.Set(x, y + 1, P["s2"]); }
        return b;
    }

    // ---- sand / gravel / rock / scree ----
    public PixelBuffer Sand(int v)
    {
        var b = New(); int s = 331 + v * 5;
        Fill(b, R["sand"], 8, s, new[] { .3, .62, .9 }, .75);
        var r = Rng(s);
        for (int i = 0; i < 6; i++) { int x = (int)(r() * 24), y = 2 + (int)(r() * 28); for (int k = 0; k < 6; k++) { b.Set(x + k, y + (k == 2 || k == 3 ? -1 : 0), P["sd4"]); b.Set(x + k, y + 1 + (k == 2 || k == 3 ? -1 : 0), P["sd1"]); } } // wind ripples
        for (int i = 0; i < 5; i++) b.Set((int)(r() * 31), (int)(r() * 31), P["s3"]);
        return b;
    }

    public PixelBuffer Gravel(int v)
    {
        var b = New(); int s = 351 + v * 3;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) b.Set(x, y, Hash(x, y, s) < .4 ? P["d1"] : P["d2"]);
        var r = Rng(s);
        for (int i = 0; i < 34; i++)
        {
            int x = (int)(r() * 29), y = (int)(r() * 29), k = (int)(r() * 3); int w = 2 + (int)(r() * 2);
            b.Set(x, y, R["stone"][k + 2]); b.Set(x + 1, y, R["stone"][k + 2]); if (w == 3) b.Set(x + 2, y, R["stone"][k + 1]);
            b.Set(x, y + 1, R["stone"][k]); b.Set(x + 1, y + 1, R["stone"][k + 1]); if (w == 3) b.Set(x + 2, y + 1, R["stone"][k]);
            if (k == 2) b.Set(x, y, P["s5"]);
        }
        return b;
    }

    /// <summary>Bare rock: flat slabs with cracks and a little lichen.</summary>
    public PixelBuffer Rock(int v)
    {
        var b = New(); int s = 371 + v * 9;
        // voronoi-ish plates
        var r = Rng(s); var pts = new List<(double, double)>();
        for (int i = 0; i < 7; i++) pts.Add((r() * 32, r() * 32));
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double d1 = 1e9, d2 = 1e9; int k = 0;
                for (int i = 0; i < pts.Count; i++)
                    foreach (int ox in new[] { -32, 0, 32 }) foreach (int oy in new[] { -32, 0, 32 })
                    {
                        double dx = x - pts[i].Item1 - ox, dy = y - pts[i].Item2 - oy, d = Math.Sqrt(dx * dx + dy * dy);
                        if (d < d1) { d2 = d1; d1 = d; k = i; } else if (d < d2) d2 = d;
                    }
                int baseK = 2 + (k % 3 == 0 ? 1 : 0) - (k % 3 == 1 ? 1 : 0);
                uint c = R["stone"][baseK]; if (Hash(x, y, s) < .1) c = R["stone"][Math.Max(0, baseK - 1)];
                if (d2 - d1 < 1.3) c = P["s1"]; else if (d2 - d1 < 2.4 && y > 0 && (x + y) % 2 == 0) c = R["stone"][Math.Max(0, baseK - 1)];
                b.Set(x, y, c);
            }
        for (int i = 0; i < 6; i++) { int x = (int)(r() * 30), y = (int)(r() * 30); b.Set(x, y, P["g2"]); b.Set(x + 1, y, P["g3"]); }
        return b;
    }

    /// <summary>Scree: loose angular stones over dark ground, bigger than gravel.</summary>
    public PixelBuffer Scree(int v)
    {
        var b = New(); int s = 391 + v * 7;
        Fill(b, new[] { P["s1"], P["d1"], P["s1"] }, 4, s, new[] { .4, .75 }, .5);
        var r = Rng(s);
        for (int i = 0; i < 12; i++)
        {
            int x = 1 + (int)(r() * 25), y = 1 + (int)(r() * 25), w = 4 + (int)(r() * 4), h = 3 + (int)(r() * 3), k = 1 + (int)(r() * 3);
            for (int j = 0; j < h; j++)
                for (int a = 0; a < w; a++)
                {
                    if ((a == 0 || a == w - 1) && (j == 0 || j == h - 1)) continue;
                    int c = k; if (j == 0 || a == 0) c++; if (j == h - 1 || a == w - 1) c--;
                    b.Set(x + a + (j == 0 ? 1 : 0), y + j, R["stone"][Math.Clamp(c, 0, 4)]);
                }
        }
        return b;
    }

    // ---- wetlands ----
    public PixelBuffer Peat(int v)
    {
        var b = New(); int s = 411 + v;
        Fill(b, R["peat"], 4, s, new[] { .4, .78 }, .6);
        var r = Rng(s);
        for (int i = 0; i < 16; i++) { int x = (int)(r() * 28), y = (int)(r() * 31), l = 2 + (int)(r() * 3); for (int k = 0; k < l; k++) b.Set(x + k, y + (k > 1 ? 1 : 0), P["br2"]); }   // fibres
        for (int i = 0; i < 4; i++) b.Set((int)(r() * 31), (int)(r() * 31), P["mu3"]);
        return b;
    }

    public PixelBuffer Marsh(int v)
    {
        var b = Peat(v + 3); var r = Rng(421 + v);
        for (int p = 0; p < 3; p++)
        {
            double cx = 5 + r() * 22, cy = 5 + r() * 22, rx = 4 + r() * 4, ry = 2.5 + r() * 2;
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                double dx = (x - cx) / rx, dy = (y - cy) / ry, d = dx * dx + dy * dy + (Hash(x, y, 422) - .5) * .3;
                if (d <= 1) b.Set(x, y, d > .6 ? P["mg1"] : P["mg2"]);
            }
            b.Set((int)cx - 1, (int)cy - 1, P["wa4"]);
        }
        for (int i = 0; i < 12; i++)   // grass tussocks
        {
            int x = 1 + (int)(r() * 29), y = 3 + (int)(r() * 28);
            b.Set(x, y, P["g2"]); b.Set(x + 1, y, P["g2"]); b.Set(x, y - 1, P["g3"]); b.Set(x + 1, y - 2, P["g4"]); b.Set(x - 1, y - 1, P["g3"]);
        }
        return b;
    }

    public PixelBuffer Reeds(int v)
    {
        var b = Marsh(v + 2); var r = Rng(431 + v);
        for (int i = 0; i < 26; i++)
        {
            int x = (int)(r() * 31), y = 8 + (int)(r() * 22), h = 6 + (int)(r() * 4); bool cat = i % 3 == 0;
            for (int j = 0; j < h; j++) b.Set(x + (j > h - 3 ? (i % 2 == 0 ? 1 : 0) : 0), y - j, j > h * 2 / 3 ? P["rd3"] : j > h / 3 ? P["rd2"] : P["rd1"]);
            if (cat) { b.Set(x, y - h, P["br1"]); b.Set(x, y - h - 1, P["br2"]); b.Set(x, y - h + 1, P["br1"]); }
        }
        return b;
    }

    // ---- water ----
    public PixelBuffer WaterShallow(int v)
    {
        var b = New(); int s = 441 + v;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            double n = Vn(x / 7.0 + v, y / 4.0, s);
            b.Set(x, y, n < .3 ? P["wa2"] : n < .66 ? P["wa3"] : P["wa4"]);
        }
        var r = Rng(s);
        for (int i = 0; i < 18; i++) { int x = (int)(r() * 30), y = (int)(r() * 31); b.Set(x, y, P["sd2"]); if (i % 2 == 0) b.Set(x + 1, y, P["sd1"]); }      // sandy bottom shows
        for (int i = 0; i < 6; i++) { int x = (int)(r() * 26), y = (int)(r() * 30); for (int k = 0; k < 4; k++) b.Set(x + k, y, P["pl4"]); }          // glints
        return b;
    }
    public PixelBuffer WaterDeep(int v)
    {
        var b = New(); int s = 451 + v;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            double n = Vn(x / 9.0 + v, y / 5.0, s);
            b.Set(x, y, n < .5 ? P["wa1"] : n < .88 ? P["wa2"] : P["wa3"]);
        }
        var r = Rng(s);
        for (int i = 0; i < 5; i++) { int x = (int)(r() * 26), y = (int)(r() * 30); for (int k = 0; k < 4; k++) b.Set(x + k, y, P["wa3"]); b.Set(x + 1, y - 1, P["wa2"]); }
        return b;
    }
    /// <summary>Ice: frozen water with cracks and frost.</summary>
    public PixelBuffer Ice(int v)
    {
        var b = New(); int s = 461 + v * 3;
        Fill(b, R["ice"], 9, s, new[] { .3, .62, .88 }, .8, .6);
        var r = Rng(s);
        for (int i = 0; i < 3 + v * 2; i++)
        {
            int x = (int)(r() * 28), y = (int)(r() * 28);
            for (int k = 0; k < 10; k++) { b.Set(x, y, P["sn4"]); x += r() < .55 ? 1 : 0; y += r() < .6 ? 1 : (r() < .3 ? -1 : 0); if (r() < .2) b.Set(x, y + 1, P["ic1"]); }
        }
        for (int i = 0; i < 12; i++) b.Set((int)(r() * 31), (int)(r() * 31), P["sn3"]);
        for (int i = 0; i < 6; i++) { int x = (int)(r() * 26), y = (int)(r() * 30); b.Set(x, y, P["sn4"]); b.Set(x + 1, y, P["sn4"]); b.Set(x + 2, y, P["sn3"]); }
        return b;
    }

    // ---- woodland ----
    public PixelBuffer ForestFloor(int v)
    {
        var b = New(); int s = 471 + v * 5;
        Fill(b, new[] { P["d1"], P["gr1"], P["d1"], P["gr2"] }, 6, s, new[] { .34, .55, .8 }, .65);
        var r = Rng(s);
        for (int i = 0; i < 18; i++) { int x = (int)(r() * 29), y = (int)(r() * 31); b.Set(x, y, P[i % 3 == 0 ? "lf1" : "br2"]); b.Set(x + 1, y, P[i % 4 == 0 ? "lf2" : "br3"]); }
        for (int i = 0; i < 3; i++) { int x = (int)(r() * 24), y = (int)(r() * 28); b.Line(x, y, x + 5, y + 2 - (int)(r() * 4), P["w2"]); b.Set(x + 2, y + 1, P["w3"]); }     // twigs
        for (int i = 0; i < 8; i++) { int x = (int)(r() * 30), y = (int)(r() * 30); b.Set(x, y, P["g3"]); b.Set(x + 1, y, P["g4"]); b.Set(x, y + 1, P["g2"]); }              // moss
        if (v == 2) for (int i = 0; i < 24; i++) { int x = (int)(r() * 30), y = (int)(r() * 30); b.Set(x, y, P["au1"]); b.Set(x + 1, y + 1, P["au2"]); }                     // needles
        return b;
    }

    public PixelBuffer LeafLitter(int v)
    {
        var b = New(); int s = 481 + v * 6;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) b.Set(x, y, Hash(x, y, s) < .55 ? P["d1"] : P["d2"]);
        var r = Rng(s); string[] lc = { "lf1", "lf2", "lf3", "lf4", "lf2", "br3" };
        int n = v == 0 ? 55 : 90;
        for (int i = 0; i < n; i++)
        {
            int x = (int)(r() * 30), y = (int)(r() * 31); var c = P[lc[(int)(r() * lc.Length)]]; bool vert = r() < .5;
            b.Set(x, y, c); b.Set(vert ? x : x + 1, vert ? y + 1 : y, c); if (r() < .5) b.Set(vert ? x + 1 : x, vert ? y : y + 1, P["lf1"]);
        }
        if (v == 0) for (int i = 0; i < 8; i++) { int x = (int)(r() * 30), y = (int)(r() * 30); b.Set(x, y, P["g2"]); b.Set(x + 1, y, P["g3"]); }
        return b;
    }
}
