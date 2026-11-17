using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>Field / Command zoom and world-map roof pieces. Opaque 32x32. Slope tiles n/e/s/w descend toward that side (ridge on the opposite
/// edge, drip edge on the named side); ridge_h / ridge_v are one-cell-wide roofs with the ridge through the middle. Light comes from the top-left.</summary>
public sealed class Roofs
{
    private readonly ColorLookup P;
    private readonly RampLookup R;
    public static readonly string[] Mats = { "thatch", "shingle", "slate", "tile" };
    public static readonly string[] Kinds = { "n", "e", "s", "w", "ridge_h", "ridge_v" };
    public Roofs(Palette pal) { P = pal.P; R = pal.R; }

    /// <summary>Ramp index 0..3 for a roof texel; u = distance down the slope from the ridge, v = along the ridge.</summary>
    private int Pat(string mat, int u, int v)
    {
        double h = Hash(u, v, 1001);
        switch (mat)
        {
            case "thatch":
            {
                int k = 2; int band = u % 9;
                if (band == 8) k = 0; else if (band == 7) k = 1;
                if (v % 3 == 0 && band < 7) k = 3 - (h < .5 ? 1 : 0); else if (h < .2) k = Math.Max(0, k - 1);
                if (h > .9 && band < 7) k = 3;
                return k;
            }
            case "shingle":
            {
                int row = u / 5, off = (row % 2) * 4, i = (v + off) % 8, j = u % 5; if (j == 4 || i == 7) return 0;
                int k = Hash((v + off) / 8, row, 1003) < .5 ? 1 : 2; if (j == 0) k = Math.Min(3, k + 1); if (h < .08) k = 3; return k;
            }
            case "slate":
            {
                int row = u / 4, off = (row % 2) * 4, i = (v + off) % 8, j = u % 4; if (j == 3 || i == 7) return 0;
                int k = Hash((v + off) / 8, row, 1005) < .3 ? 2 : 1; if (j == 0 || i == 0) k = Math.Min(3, k + 1); return k;
            }
            default: // pan tiles: ribbed columns and scalloped courses
            {
                int row = u / 7, j = u % 7, i = v % 6; int k = i == 0 ? 0 : i == 1 ? 3 : i == 5 ? 1 : 2;
                if (j == 6) k = 0; else if (j == 5 && i > 0) k = Math.Max(0, k - 1); if (h < .07) k = Math.Max(0, k - 1); if ((row + i) % 4 == 0 && j == 0) k = Math.Min(3, k + 1);
                return k;
            }
        }
    }

    private uint[] Ramp(string mat) => mat switch { "thatch" => R["thatch"], "shingle" => R["shingle"], "slate" => R["slate"], _ => R["tile"] };

    public PixelBuffer Roof(string mat, string kind)
    {
        var b = Ground.New(); var ramp = Ramp(mat);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                int u, v, lit; bool ridgeEdge = false, eave = false;
                switch (kind)
                {
                    case "s": u = y; v = x; lit = 0; ridgeEdge = y < 3; eave = y >= 30; break;
                    case "n": u = 31 - y; v = x; lit = 1; ridgeEdge = y > 28; eave = y <= 1; break;
                    case "e": u = x; v = y; lit = 0; ridgeEdge = x < 3; eave = x >= 30; break;
                    case "w": u = 31 - x; v = y; lit = 1; ridgeEdge = x > 28; eave = x <= 1; break;
                    case "ridge_h": { int dr = y < 16 ? 15 - y : y - 16; u = dr; v = x; lit = y < 16 ? 1 : 0; ridgeEdge = dr < 3; eave = dr >= 15; break; }
                    default: { int dr = x < 16 ? 15 - x : x - 16; u = dr; v = y; lit = x < 16 ? 1 : 0; ridgeEdge = dr < 3; eave = dr >= 15; break; }
                }
                int k = Pat(mat, u, v);
                if (lit == 1) k = Math.Min(3, k + (k == 0 ? 0 : 1)); else if (k == 3 && Hash(x, y, 1007) < .5) k = 2;
                uint c = ramp[k];
                if (ridgeEdge)
                {
                    bool apex = kind == "ridge_h" ? (y == 15 || y == 16) : kind == "ridge_v" ? (x == 15 || x == 16) : false;
                    c = ramp[apex ? 3 : Math.Clamp(1 + lit + (Hash(x, y, 1009) < .3 ? 1 : 0), 0, 3)];
                    if (mat == "thatch") c = ramp[Hash(x, y, 1011) < .5 ? 3 : 2];
                }
                if (eave) c = (kind == "n" ? y == 0 : kind == "w" ? x == 0 : kind == "s" ? y == 31 : kind == "e" ? x == 31 : (kind == "ridge_h" ? (y == 0 || y == 31) : (x == 0 || x == 31))) ? P["out"] : ramp[0];
                b.Set(x, y, c);
            }
        // eave edges for ridge pieces
        if (kind == "ridge_h") for (int x = 0; x < 32; x++) { b.Set(x, 0, P["out"]); b.Set(x, 31, P["out"]); b.Set(x, 30, ramp[0]); }
        if (kind == "ridge_v") for (int y = 0; y < 32; y++) { b.Set(0, y, P["out"]); b.Set(31, y, P["out"]); b.Set(30, y, ramp[0]); }
        return b;
    }
}
