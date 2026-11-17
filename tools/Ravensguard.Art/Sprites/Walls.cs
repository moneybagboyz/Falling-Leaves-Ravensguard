using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>
/// 16-mask wall sets (N=1 E=2 S=4 W=8 = neighbour is the same wall). Wall cells are full 1 m cells like the v1 walls: if the
/// south neighbour is wall the cell is all top surface ("cap"); otherwise it is an 8 px top strip over a 24 px front face.
/// Unconnected W/E/N sides get a dark edge, so ends, corners, T and crossings all read. Plus doors, windows, arrow slit, gate.
/// </summary>
public sealed class Walls
{
    private readonly ColorLookup P;
    private readonly RampLookup R;
    public static readonly string[] Mats = { "wattle", "timber", "stone", "brick", "palisade", "town" };
    public Walls(Palette pal) { P = pal.P; R = pal.R; }

    // ---------- top surface ----------
    private uint Top(string mat, int x, int y, int lvl)
    {
        double h = Hash(x, y, 501);
        switch (mat)
        {
            case "wattle":
            {
                int k = h < .25 ? 1 : 2; if (x % 8 == 3 && y % 3 != 2) k = 0; return R["dirt"][Math.Clamp(k + (lvl > 0 ? 1 : 0), 0, 3)];
            }
            case "timber":
            {
                int k = 2; if (y % 8 == 0) k = 0; else if (y % 8 == 1) k = 3; if (h < .1 && k == 2) k = 1; if (x % 16 == 0) k = 1; return R["wood"][Math.Clamp(k + (lvl > 0 ? 1 : 0), 0, 4)];
            }
            case "stone":
            {
                int row = y / 8, off = (row % 2) * 8, bx = (x + off) % 16; int k = 3; if (bx == 0 || y % 8 == 7) k = 1; else if (bx == 1 || y % 8 == 0) k = 4; if (h < .08 && k == 3) k = 2; return R["stone"][k];
            }
            case "brick":
            {
                int row = y / 4, off = (row % 2) * 4, bx = (x + off) % 8; int k = 2; if (bx == 7 || y % 4 == 3) k = 0; else if (y % 4 == 0) k = 3; if (h < .12 && k == 2) k = 1; return R["brick"][k];
            }
            case "palisade":
            {
                int i = x % 4; int k = i == 0 ? 1 : i == 1 ? 4 : i == 2 ? 3 : 2; if (y % 4 == 0) k = Math.Max(0, k - 1); return R["wood"][k];
            }
            default: // town wall walkway
            {
                int row = y / 8, off = (row % 2) * 8, bx = (x + off) % 16; int k = 2; if (bx == 0 || y % 8 == 7) k = 1; else if (y % 8 == 0) k = 3; if (h < .08) k = 1; return R["stone"][k];
            }
        }
    }

    // ---------- front face; fy = 0..23 below the top strip ----------
    private uint Face(string mat, int x, int fy)
    {
        double h = Hash(x, fy, 503);
        switch (mat)
        {
            case "wattle":
            {
                uint c = Hash(x, fy, 17) < .22 ? P["pl1"] : P["pl2"];
                foreach (int px in new[] { 0, 10, 21, 30 }) if (x >= px && x < px + 2) c = P["w2"];
                if (fy < 2) c = P["w2"]; if (fy >= 21) c = P["w1"];
                for (int p = 0; p < 4; p++) { int x0 = 3 + p * 8, y0 = 4 + (p % 2) * 7; if (x >= x0 && x < x0 + 6 && fy >= y0 && fy < y0 + 7 && x != 10 && x != 21) c = (x + fy) % 2 == 0 ? P["d3"] : P["d2"]; }
                return c;
            }
            case "timber":
            {
                int j = fy % 6, row = fy / 6; int k = j == 0 ? 0 : j == 1 ? 4 : j == 5 ? 1 : 2;
                int joint = (row * 11 + 5) % 32; if (x == joint || x == (joint + 16) % 32) k = 0;
                if (h < .08 && k == 2) k = 1; if (k == 2 && h > .94) k = 3;
                return R["wood"][k];
            }
            case "stone":
            {
                int row = fy / 5, off = (row % 2) * 5, j = fy % 5, i = (x + off) % 10; int k = 2; if (j == 4 || i == 9) return P["s1"]; if (j == 0) k = 3; if (i == 8 || j == 3) k = 1; if (Hash(x + off, fy, 6) < .08) k = 1; if (row == 0 && j == 0) k = 4; return R["stone"][k];
            }
            case "brick":
            {
                int row = fy / 4, off = (row % 2) * 4, j = fy % 4, i = (x + off) % 8; if (j == 3 || i == 7) return P["pl1"];
                int bn = (int)(Hash((x + off) / 8, row, 505) * 5); int k = bn < 1 ? 1 : bn < 4 ? 2 : 3; if (j == 0) k = Math.Min(3, k + 1); if (i == 6) k = Math.Max(0, k - 1); if (h < .06) k = Math.Max(0, k - 1); return R["brick"][k];
            }
            case "palisade":
            {
                int i = x % 4; if (i == 0) return fy % 9 == 8 ? P["out"] : P["w1"]; int k = i == 1 ? 3 : i == 2 ? 2 : 1;
                if (fy == 6 || fy == 7) return i == 1 ? P["br3"] : P["br2"]; if (h < .1) k = Math.Max(0, k - 1); return R["wood"][k];
            }
            default:
            {
                int row = fy / 7, off = (row % 2) * 7, j = fy % 7, i = (x + off) % 14; if (j == 6 || i == 13) return P["s1"];
                int k = 2; if (j == 0) k = 3; if (i == 12 || j == 5) k = 1; if (Hash((x + off) / 14, row, 507) < .18) k = 3 - (j == 0 ? 0 : 1) ; if (fy >= 20) k = Math.Max(1, k - 1); if (h < .05) k = 1; return R["stone"][k];
            }
        }
    }

    public PixelBuffer Wall(string mat, int mask)
    {
        var b = Ground.New();
        bool nC = (mask & 1) != 0, eC = (mask & 2) != 0, sC = (mask & 4) != 0, wC = (mask & 8) != 0;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                uint c;
                if (sC || y < 8)
                {
                    int lvl = (!nC && y < 2) ? 1 : (!sC && y == 7) ? -1 : 0;
                    c = Top(mat, x, y, lvl);
                    if (!sC && y == 7 && mat != "palisade") c = mat == "wattle" ? P["d1"] : mat == "brick" ? P["bk1"] : mat == "timber" ? P["w1"] : P["s1"];
                    if (mat == "town") c = TownTop(x, y, nC, sC, eC, wC, c);
                }
                else c = Face(mat, x, y - 8);
                b.Set(x, y, c);
            }
        // edges of unconnected sides
        for (int y = 0; y < 32; y++)
        {
            if (!wC) { b.Set(0, y, P["out"]); if (y > 0 && y < 31 && mat != "wattle") b.Set(1, y, Hi(mat, y)); }
            if (!eC) { b.Set(31, y, P["out"]); b.Set(30, y, y < 8 || sC ? Lo(mat) : b.Get(30, y)); }
        }
        if (!nC) for (int x = 0; x < 32; x++) { b.Set(x, 0, P["out"]); if (x > 0 && x < 31) b.Set(x, 1, Hi(mat, x)); }
        for (int x = 0; x < 32; x++) if (!sC) b.Set(x, 31, P["out"]);
        if (!sC && mat != "palisade") for (int x = 0; x < 32; x++) b.Set(x, 8, mat == "wattle" ? P["w2"] : b.Get(x, 8)); // top rail
        return b;
    }

    private uint Hi(string mat, int i) => mat switch { "wattle" => P["d4"], "timber" => P["w5"], "stone" or "town" => P["s4"], "brick" => P["bk4"], _ => P["w5"] };
    private uint Lo(string mat) => mat switch { "wattle" => P["d1"], "timber" => P["w1"], "stone" or "town" => P["s1"], "brick" => P["bk1"], _ => P["w1"] };

    /// <summary>Crenellation: merlons along open edges of the walkway, offset so neighbours line up.</summary>
    private uint TownTop(int x, int y, bool nC, bool sC, bool eC, bool wC, uint walk)
    {
        bool Merlon(int along) => (along / 6) % 2 == 0;
        // front parapet (south edge of the top strip, only when the face is shown)
        if (!sC && y >= 3 && y <= 6 && Merlon(x)) return y == 3 ? P["s5"] : y == 6 ? P["s2"] : (x % 6 == 0 ? P["s4"] : P["s3"]);
        if (!sC && y >= 3 && y <= 6) return y == 3 ? P["s2"] : walk;          // crenel gap: walkway seen low
        if (!nC && y >= 2 && y <= 4 && sC) return walk;
        if (!nC && !sC && y >= 1 && y <= 2 && Merlon(x + 3)) return y == 1 ? P["s5"] : P["s3"];
        if (!wC && x >= 2 && x <= 5 && sC && Merlon(y)) return x == 2 ? P["s5"] : x == 5 ? P["s2"] : P["s4"];
        if (!eC && x >= 26 && x <= 29 && sC && Merlon(y + 3)) return x == 26 ? P["s4"] : x == 29 ? P["s1"] : P["s3"];
        return walk;
    }

    // ---------- features on a straight east-west wall face (mask E|W) ----------
    private uint FrameCol(string mat, int i) => mat switch
    {
        "stone" or "town" => R["stone"][Math.Clamp(i + 2, 0, 4)],
        "brick" => R["brick"][Math.Clamp(i + 1, 0, 3)],
        _ => R["wood"][Math.Clamp(i + 1, 0, 4)],
    };

    public PixelBuffer Feature(string mat, string kind)
    {
        var b = Wall(mat, 10);
        switch (kind)
        {
            case "door_closed": Door(b, mat, false); break;
            case "door_open": Door(b, mat, true); break;
            case "window_shuttered": Window(b, mat, 0); break;
            case "window_open": Window(b, mat, 1); break;
            case "window_glazed": Window(b, mat, 2); break;
            case "arrow_slit": Slit(b, mat); break;
            case "gate_closed": Gate(b, false); break;
            case "gate_open": Gate(b, true); break;
        }
        for (int x = 0; x < 32; x++) b.Set(x, 31, P["out"]);
        return b;
    }

    private void Door(PixelBuffer b, string mat, bool open)
    {
        b.Rect(7, 8, 18, 3, FrameCol(mat, 2)); b.Rect(7, 10, 18, 1, FrameCol(mat, 0));                     // lintel
        b.Rect(8, 11, 16, 21, P["out"]);
        if (!open)
        {
            for (int y = 12; y < 32; y++) for (int x = 9; x < 23; x++) b.Set(x, y, R["wood"][(x - 9) % 4 == 3 ? 1 : (y == 12 ? 4 : 3)]);
            b.Rect(9, 16, 14, 1, P["m2"]); b.Rect(9, 27, 14, 1, P["m2"]); b.Set(9, 15, P["m3"]); b.Set(9, 26, P["m3"]);
            b.Set(20, 21, P["au3"]); b.Set(20, 22, P["au2"]);
        }
        else
        {
            b.Rect(9, 12, 14, 20, P["dk1"]); b.Rect(9, 12, 14, 1, P["dk2"]);
            b.Rect(9, 12, 3, 20, P["w2"]); b.Rect(9, 12, 1, 20, P["w3"]); b.Rect(11, 12, 1, 20, P["w1"]); b.Rect(9, 17, 3, 1, P["m2"]); b.Rect(9, 27, 3, 1, P["m2"]); // leaf edge-on
            b.Rect(12, 30, 11, 2, P["w1"]);                                                                // threshold seen inside
            b.Set(18, 31, P["w2"]); b.Set(14, 31, P["w2"]);
        }
        b.Rect(7, 11, 1, 21, FrameCol(mat, 1)); b.Rect(24, 11, 1, 21, FrameCol(mat, 0));
    }

    private void Window(PixelBuffer b, string mat, int style)
    {
        b.Rect(10, 13, 12, 13, P["out"]);
        if (style == 0) // shuttered
        {
            for (int y = 14; y < 25; y++) for (int x = 11; x < 21; x++) b.Set(x, y, R["wood"][(x - 11) % 3 == 2 ? 1 : 3]);
            b.Rect(15, 14, 2, 11, P["out"]); b.Rect(11, 17, 10, 1, P["w2"]); b.Rect(11, 22, 10, 1, P["w2"]); b.Set(14, 19, P["m3"]); b.Set(17, 19, P["m3"]); b.Rect(11, 14, 10, 1, P["w4"]);
        }
        else if (style == 1) // open
        {
            b.Rect(11, 14, 10, 11, P["dk2"]); b.Rect(11, 14, 10, 1, P["dk1"]); b.Rect(14, 20, 5, 1, P["w1"]);
            b.Rect(8, 13, 2, 13, P["w3"]); b.Rect(8, 13, 1, 13, P["w4"]); b.Rect(9, 13, 1, 13, P["w2"]); b.Rect(22, 13, 2, 13, P["w3"]); b.Rect(23, 13, 1, 13, P["w2"]); b.Rect(22, 13, 1, 13, P["w4"]);
        }
        else // glazed
        {
            for (int y = 14; y < 25; y++) for (int x = 11; x < 21; x++) { int k = y < 17 ? 2 : y < 21 ? 1 : 0; b.Set(x, y, R["water"][k + (Hash(x, y, 9) < .15 ? 1 : 0)]); }
            for (int y = 14; y < 25; y++) for (int x = 11; x < 21; x++) if ((x + y) % 5 == 0 || (x - y + 100) % 5 == 0) b.Set(x, y, P["m2"]);
            b.Rect(11, 14, 10, 1, P["m2"]); b.Rect(11, 24, 10, 1, P["m2"]);
            b.Set(12, 15, P["wa4"]); b.Set(13, 16, P["wa4"]); b.Set(18, 21, P["wa4"]);
        }
        b.Rect(9, 25, 14, 2, FrameCol(mat, 2)); b.Rect(9, 26, 14, 1, FrameCol(mat, 0));                      // sill
        b.Rect(10, 12, 12, 1, FrameCol(mat, 1));                                                              // lintel
    }

    private void Slit(PixelBuffer b, string mat)
    {
        b.Rect(13, 11, 6, 17, FrameCol(mat, 1)); b.Rect(13, 11, 6, 1, FrameCol(mat, 3));
        b.Rect(14, 12, 4, 3, P["out"]); b.Rect(14, 24, 4, 3, P["out"]); b.Rect(15, 12, 2, 15, P["dk1"]); b.Rect(14, 18, 4, 1, P["out"]);
        b.Rect(15, 14, 2, 11, P["dk1"]); b.Set(15, 13, P["dk2"]); b.Set(16, 25, P["dk2"]);
    }

    private void Gate(PixelBuffer b, bool open)
    {
        // arch 20 wide, rounded top
        for (int y = 9; y < 32; y++)
            for (int x = 6; x < 26; x++)
            {
                double cx = 15.5, r = 10; bool inArch = y >= 19 || ((x - cx) * (x - cx) + (y - 19) * (y - 19) <= r * r);
                if (!inArch) continue;
                bool border = y == 9 || x == 6 || x == 25 || ((x - cx) * (x - cx) + (y - 19) * (y - 19) > (r - 1.2) * (r - 1.2) && y < 19);
                b.Set(x, y, border ? P["out"] : open ? P["dk1"] : R["wood"][(x - 7) % 4 == 3 ? 1 : 3]);
            }
        if (!open)
        {
            for (int y = 14; y < 32; y += 6) for (int x = 7; x < 25; x++) if (b.Get(x, y) != P["out"] && b.Get(x, y) != 0) b.Set(x, y, P["m2"]);
            for (int y = 14; y < 32; y += 6) for (int x = 9; x < 25; x += 4) b.Set(x, y, P["m4"]);
            b.Rect(15, 10, 2, 22, P["w1"]);
        }
        else
        {
            for (int x = 8; x < 24; x += 3) { b.Rect(x, 11, 1, 6, P["m3"]); b.Set(x, 17, P["m4"]); }                // portcullis raised, tips showing
            b.Rect(7, 11, 18, 1, P["m2"]); b.Rect(7, 14, 18, 1, P["m2"]); b.Rect(8, 30, 16, 2, P["s2"]);
        }
        b.Rect(4, 8, 2, 24, P["s4"]); b.Rect(26, 8, 2, 24, P["s2"]);
    }

    // ---------- floors ----------
    public PixelBuffer Rush(int v)
    {
        var b = Ground.New(); int s = 601 + v * 5;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { double n = Hash(x, y, s); b.Set(x, y, n < .5 ? P["d2"] : n < .85 ? P["d3"] : P["d1"]); }
        var r = Rng(s);
        for (int i = 0; i < 48; i++)
        {
            int x = (int)(r() * 28), y = (int)(r() * 30), l = 3 + (int)(r() * 4), dy = r() < .5 ? -1 : (r() < .5 ? 0 : 1); var c = P[i % 3 == 0 ? "ln2" : i % 3 == 1 ? "au2" : "au3"];
            for (int k = 0; k < l; k++) b.Set(x + k, y + (dy * k) / 3, c);
            if (i % 5 == 0) b.Set(x + l / 2, y + 1, P["au1"]);
        }
        return b;
    }

    public PixelBuffer BrickFloor(int v)
    {
        var b = Ground.New(); int s = 611 + v;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            int row = y / 4, off = v == 0 ? (row % 2) * 4 : 0, j = y % 4, i = (x + off) % 8;
            if (j == 3 || i == 7) { b.Set(x, y, P["pl1"]); continue; }
            double bn = Hash((x + off) / 8, row, s); int k = bn < .2 ? 1 : bn < .8 ? 2 : 3; if (j == 0) k = Math.Min(3, k + 1); b.Set(x, y, R["brick"][k]);
        }
        return b;
    }

    public PixelBuffer TileFloor(int v)
    {
        var b = Ground.New();
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            int tx = x / 8, ty = y / 8; bool alt = (tx + ty) % 2 == 0; int lx = x % 8, ly = y % 8;
            if (lx == 7 || ly == 7) { b.Set(x, y, v == 0 ? P["bk1"] : P["s1"]); continue; }
            uint[] ramp = alt ? (v == 0 ? R["tile"] : R["slate"]) : (v == 0 ? R["plaster"] : R["stone"]);
            int k = alt ? 2 : 1; if (lx == 0 || ly == 0) k++; if (lx == 6 || ly == 6) k--; if (Hash(x, y, 621) < .06) k--;
            b.Set(x, y, ramp[Math.Clamp(k, 0, ramp.Length - 1)]);
        }
        return b;
    }

    public PixelBuffer PlankV()
    {
        var b = Ground.New();
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            int i = x % 8; int k = i == 0 ? 0 : i == 1 ? 3 : i == 7 ? 1 : 2; int seam = (x / 8 * 13 + 5) % 32; if (y == seam) k = 0;
            if (Hash(x, y, 631) < .1 && k == 2) k = 1; b.Set(x, y, R["wood"][k]);
        }
        for (int p = 0; p < 4; p++) { b.Set(p * 8 + 4, (p * 13 + 12) % 32, P["m3"]); b.Set(p * 8 + 4, (p * 13 + 26) % 32, P["m3"]); }
        return b;
    }

    public PixelBuffer FlagIrregular()
    {
        var b = Ground.New(); b.Rect(0, 0, 32, 32, P["s1"]); var r = Rng(641);
        int y0 = 0;
        while (y0 < 32)
        {
            int h = 9 + (int)(r() * 4); if (y0 + h > 32 || 32 - (y0 + h) < 6) h = 32 - y0; int x0 = -(int)(r() * 8);
            while (x0 < 32)
            {
                int w = 8 + (int)(r() * 8); if (x0 + w > 32 && 32 - x0 < 6) w = 32 - x0;
                for (int j = 1; j < h - 1; j++) for (int i = 1; i < w - 1; i++)
                {
                    int k = 2; if (i == 1 || j == 1) k = 3; if (i == w - 2 || j == h - 2) k = 1; if (Hash(x0 + i, y0 + j, 643) < .07) k = 1; else if (Hash(x0 + i, y0 + j, 644) < .06) k = 3;
                    b.Set(((x0 + i) % 32 + 32) % 32, y0 + j, R["stone"][k]);
                }
                x0 += w;
            }
            y0 += h;
        }
        return b;
    }

    public PixelBuffer StoneStairs(bool up)
    {
        var b = Ground.New(); b.Rect(0, 0, 32, 32, P["s1"]);
        for (int s = 0; s < 7; s++)
        {
            int y = 2 + s * 4; int lvl = up ? 6 - s : s;
            uint tc = R["stone"][Math.Min(4, 1 + lvl * 3 / 6 + (up ? 0 : 0))];
            b.Rect(3, y, 26, 3, tc); b.Rect(3, y + 3, 26, 1, up ? P["s1"] : P["out"]); if (up) b.Rect(3, y, 26, 1, P["s5"]);
        }
        b.Rect(0, 0, 3, 32, P["s3"]); b.Rect(29, 0, 3, 32, P["s2"]); b.Rect(0, 0, 1, 32, P["s4"]); b.Rect(3, 0, 1, 32, P["out"]); b.Rect(28, 0, 1, 32, P["out"]);
        uint arrow = up ? P["f4"] : P["f3"];
        for (int i = 0; i < 5; i++) { if (up) { b.Set(16 - i, 12 + i, arrow); b.Set(16 + i, 12 + i, arrow); } else { b.Set(16 - i, 20 - i, arrow); b.Set(16 + i, 20 - i, arrow); } }
        return b;
    }
}
