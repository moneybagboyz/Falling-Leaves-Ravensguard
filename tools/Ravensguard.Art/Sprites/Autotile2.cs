using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>Autotile builders added in v2: banded shores (sand lip, shallows, deep water) and linear boundaries
/// (hedge, wattle fence, field wall, palisade) drawn over grass with top surface and short front face.</summary>
public static class Autotile2
{
    /// <summary>Like Autotile.Build, but the area inside the ragged edge is made of bands from outside in:
    /// each (tile, thickness) is shown while the distance past the ragged edge is within its thickness; `inner` fills the rest.</summary>
    public static PixelBuffer BuildBanded(PixelBuffer baseTile, (PixelBuffer Tile, int Thick)[] bands, PixelBuffer inner, int mask, int seed)
    {
        var b = new PixelBuffer(32, 32);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                int w = int.MaxValue;
                if ((mask & Autotile.N) == 0) w = Math.Min(w, y - Rag(x, 0, seed));
                if ((mask & Autotile.S) == 0) w = Math.Min(w, 31 - y - Rag(x, 1, seed));
                if ((mask & Autotile.W) == 0) w = Math.Min(w, x - Rag(y, 2, seed));
                if ((mask & Autotile.E) == 0) w = Math.Min(w, 31 - x - Rag(y, 3, seed));
                PixelBuffer src;
                if (w == int.MaxValue) src = inner;
                else if (w < 0) src = baseTile;
                else
                {
                    src = inner; int acc = 0;
                    foreach (var (t, th) in bands) { acc += th; if (w < acc) { src = t; break; } }
                }
                b.Set(x, y, src.Get(x, y));
            }
        return b;
    }

    private static int Rag(int along, int side, int seed) => 3 + (int)(Hash(along / 2, side, seed) * 3);

    /// <summary>Visual spec of a boundary line: top-strip half width, face height, colours as functions of position.</summary>
    public sealed record LineStyle(int Tw, int Y0, int F, Func<int, int, bool, int, uint> Top, Func<int, int, uint> Face, uint Shadow, uint Outline);

    public static PixelBuffer BuildLine(PixelBuffer grass, LineStyle st, int mask)
    {
        var b = grass.Clone();
        int cx0 = 16 - st.Tw, cx1 = 16 + st.Tw - 1, y0 = st.Y0, y1 = st.Y0 + 2 * st.Tw - 1;
        bool N = (mask & 1) != 0, E = (mask & 2) != 0, S = (mask & 4) != 0, W = (mask & 8) != 0;
        bool T(int x, int y)
        {
            if (x < 0 || x > 31 || y < 0 || y > 31) return false;
            if (x >= cx0 && x <= cx1 && y >= y0 && y <= y1) return true;
            if (N && x >= cx0 && x <= cx1 && y <= y1) return true;
            if (S && x >= cx0 && x <= cx1 && y >= y0) return true;
            if (W && y >= y0 && y <= y1 && x <= cx1) return true;
            if (E && y >= y0 && y <= y1 && x >= cx0) return true;
            return false;
        }
        // shadow first (cast down-right a little), then face, then top
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                if (T(x, y)) continue;
                int d = 0; for (int k = 1; k <= st.F + 3 && d == 0; k++) if (T(x, y - k) || (k <= 2 && T(x - 1, y - k + 1))) d = k;
                if (d == 0) continue;
                if (d <= st.F) { if (T(x, y - d)) b.Set(x, y, st.Face(x, d - 1)); else b.Set(x, y, st.Shadow); }
                else b.Set(x, y, st.Shadow);
            }
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                if (!T(x, y)) continue;
                bool vert = (x >= cx0 && x <= cx1) && !(y >= y0 && y <= y1 && ((W && x <= cx1) || (E && x >= cx0)) ) ;
                int lvl = !T(x, y - 1) ? 1 : (!T(x + 1, y) ? -1 : 0);
                b.Set(x, y, st.Top(x, y, vert, lvl));
            }
        // dark outline around the raised shape (same 1 px `out` ring the props use) so it reads against grass of any season
        bool Shape(int x, int y) { if (T(x, y)) return true; for (int k = 1; k <= st.F; k++) if (T(x, y - k)) return true; return false; }
        var ring = new List<(int, int)>();
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            if (!Shape(x, y) && (Shape(x - 1, y) || Shape(x + 1, y) || Shape(x, y - 1) || Shape(x, y + 1))) ring.Add((x, y));
        foreach (var (x, y) in ring) b.Set(x, y, st.Outline);
        return b;
    }

    public static LineStyle Hedge(Palette pal)
    {
        var P = pal.P; var R = pal.R; var f = R["foliage_summer"]; var lt = R["leaf_summer"];
        return new LineStyle(6, 9, 7,
            (x, y, v, lvl) => { double n = Vn(x / 3.0, y / 3.0, 801) * .6 + Hash(x, y, 802) * .4; int k = n < .3 ? 1 : n < .62 ? 2 : 3; if (lvl < 0) k = Math.Max(1, k - 1); return Hash(x, y, 803) < .03 ? P["lf3"] : f[k]; },
            (x, d) => { double h = Hash(x, d, 804); int k = d < 2 ? 1 : 0; if (h < .12) k = 1; if (h > .95) k = 2; return h > .985 ? P["r3"] : f[k]; },
            P["g1"], P["out"]);
    }

    public static LineStyle Wattle(Palette pal)
    {
        var P = pal.P; var R = pal.R; var d = R["dirt"]; var w = R["wood"];
        return new LineStyle(2, 13, 9,
            (x, y, v, lvl) => (x % 8 == 3 || x % 8 == 4) && !v ? w[3] : v ? ((y % 8 == 3 || y % 8 == 4) ? w[3] : d[lvl > 0 ? 3 : 2]) : d[lvl > 0 ? 3 : 2],
            (x, dd) => { if (x % 8 == 3) return w[2]; if (x % 8 == 4) return w[1]; if (x % 8 == 2) return w[3]; return ((x + dd) % 2 == 0) ? (dd % 2 == 0 ? d[3] : d[2]) : d[dd % 3 == 0 ? 1 : 2]; },
            P["g1"], P["out"]);
    }

    public static LineStyle FieldWall(Palette pal)
    {
        var P = pal.P; var R = pal.R; var s = R["stone"];
        return new LineStyle(4, 11, 7,
            (x, y, v, lvl) => { int i = v ? y % 8 : x % 8; int k = 3; if (i == 0) k = 1; else if (i == 1) k = 4; if (Hash(x, y, 811) < .12) k = 2; if (Hash(x, y, 812) < .03) return P["g3"]; return s[Math.Clamp(k + (lvl > 0 ? 0 : 0), 0, 4)]; },
            (x, d) => { int row = d / 3; int off = row * 4; int i = (x + off) % 8; if (i == 7) return s[0]; if (d % 3 == 2) return s[1]; int k = d % 3 == 0 ? 3 : 2; if (Hash(x + off, d, 813) < .1) k = 1; return s[k]; },
            P["g1"], P["out"]);
    }

    /// <summary>Low churchyard wall: dressed ashlar courses under a pale coping, lower and neater than the dry-stone field wall.</summary>
    public static LineStyle ChurchWall(Palette pal)
    {
        var P = pal.P; var R = pal.R; var s = R["stone"];
        return new LineStyle(3, 13, 7,
            (x, y, v, lvl) => { int i = v ? y % 6 : x % 6; if (i == 0) return s[2]; if (Hash(x, y, 831) < .08) return s[3]; return lvl > 0 ? s[4] : s[3]; },
            (x, d) => { int row = d / 3; int i = (x + (row & 1) * 3) % 6; if (d == 0) return s[1]; if (i == 5) return s[0]; int k = d % 3 == 2 ? 1 : 2; if (Hash(x + row * 5, d, 832) < .12) k = Math.Max(0, k - 1); return s[k + 1]; },
            P["g1"], P["out"]);
    }

    public static LineStyle Palisade(Palette pal)
    {
        var P = pal.P; var R = pal.R; var w = R["wood"];
        return new LineStyle(3, 9, 12,
            (x, y, v, lvl) => { int i = v ? y % 4 : x % 4; return i == 0 ? w[1] : i == 1 ? w[4] : i == 2 ? w[3] : w[2]; },
            (x, d) => { int i = x % 4; if (i == 0) return d > 10 ? P["out"] : w[0]; if (d == 6 || d == 7) return i == 1 ? P["br3"] : P["br2"]; int k = i == 1 ? 3 : i == 2 ? 2 : 1; if (Hash(x, d, 821) < .1) k = Math.Max(0, k - 1); return w[k]; },
            P["g1"], P["out"]);
    }
}
