using static Ravensguard.Art.Noise;

namespace Ravensguard.Art.Sprites;

/// <summary>
/// Field (8 px/m) and Command (2 px/m) zoom art (REDESIGN_PLAN 2.7): their own small sprites, not the street sprites scaled down.
/// <c>field_8</c> holds 8x8 ground, wall-top and roof cells. Each is a three-colour dither (dark / mid / light) taken from the matching
/// street tile's own palette entries, so the season swap, snow and ice recolour work on them unchanged; furrows, water streaks and cobble
/// joints are drawn per style. <c>mini_16</c> holds transparent minis: people 8x16 in team colours, trees 16x20, bushes and rocks.
/// Pixels are exact palette colours; the baked chunk images draw from these.
/// </summary>
public sealed class Minis
{
    private readonly ColorLookup P;
    private readonly RampLookup R;
    public static readonly string[] Teams = { "neutral", "red", "blue", "green", "gold", "iron" };
    public static readonly string[] Roles = { "villager", "soldier", "hero" };
    public static readonly string[] Species = { "oak", "ash", "birch", "pine", "apple" };

    public Minis(Palette pal) { P = pal.P; R = pal.R; }

    static double Lum(uint c) => 0.30 * ((c >> 16) & 255) + 0.59 * ((c >> 8) & 255) + 0.11 * (c & 255);

    /// <summary>The (dark, mid, light) colours of a street tile: the colour nearest its average, and the nearest darker and lighter colours of the tile that stay close to it.</summary>
    public static (uint Dark, uint Mid, uint Light) Triple(PixelBuffer src)
    {
        static double Dist(uint a, uint b) { double dr = ((a >> 16) & 255) - ((b >> 16) & 255), dg = ((a >> 8) & 255) - ((b >> 8) & 255), db = (a & 255) - (b & 255); return Math.Sqrt(dr * dr + dg * dg + db * db); }
        var px = src.Data.Where(c => (c >> 24) == 255).ToList();
        double r = px.Average(c => (double)((c >> 16) & 255)), g = px.Average(c => (double)((c >> 8) & 255)), b = px.Average(c => (double)(c & 255));
        uint mean = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
        // candidates: colours that cover at least half a percent of the tile; those far from the mid colour (a flower, a berry) are dropped below
        var common = px.GroupBy(c => c).Where(k => k.Count() * 200 >= px.Count).Select(k => k.Key).OrderBy(c => c).ToList();
        if (common.Count == 0) common.Add(px[0]);
        uint mid = common.OrderBy(c => Dist(c, mean)).ThenBy(c => c).First();
        double lm = Lum(mid);
        var near = common.Where(c => Dist(c, mid) <= 70).ToList();
        uint light = near.Where(c => Lum(c) > lm).OrderBy(c => Lum(c)).FirstOrDefault(mid);
        uint dark = near.Where(c => Lum(c) < lm).OrderByDescending(c => Lum(c)).FirstOrDefault(mid);
        return (dark, mid, light);
    }

    /// <summary>One 8x8 cell. Styles: noise, furrows (horizontal rows), water (light streaks), cobble (joints), planks, thatch (rows).</summary>
    public PixelBuffer Cell((uint Dark, uint Mid, uint Light) t, int seed, string style = "noise")
    {
        var b = new PixelBuffer(8, 8);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                double h = Hash(x, y, seed);
                uint c = t.Mid;
                switch (style)
                {
                    case "furrows": c = (y & 1) == 0 ? (h < .25 ? t.Mid : t.Dark) : (h < .3 ? t.Light : t.Mid); break;
                    case "water": c = h < .08 ? t.Light : h > .93 ? t.Dark : ((x + y * 2 + seed) % 8 is 0 or 1 && y % 3 == 0 ? t.Light : t.Mid); break;
                    case "cobble": c = (y % 4 == 3 || (x + (y / 4) * 2) % 4 == 3) ? t.Dark : (h < .25 ? t.Light : t.Mid); break;
                    case "planks": c = y % 4 == 3 ? t.Dark : (h < .12 ? t.Light : t.Mid); break;
                    case "thatch": c = y % 3 == 2 ? t.Dark : (h < .22 ? t.Light : t.Mid); break;
                    default: c = h < .17 ? t.Light : h > .85 ? t.Dark : t.Mid; break;
                }
                b.Set(x, y, c);
            }
        return b;
    }

    public PixelBuffer Cell(PixelBuffer src, int seed, string style = "noise") => Cell(Triple(src), seed, style);

    /// <summary>Roof cell. lo = shaded slope, mid, hi = lit slope, ridge = the ridge line (horizontal; the game transposes for vertical).</summary>
    public PixelBuffer Roof(string mat, string part, int seed)
    {
        var r = R[mat]; // dark -> light
        uint d = r[Math.Min(1, r.Length - 1)], m = r[Math.Min(2, r.Length - 1)], l = r[^1], dd = r[0];
        string style = mat == "thatch" ? "thatch" : mat == "tile" ? "cobble" : "noise";
        switch (part)
        {
            case "lo": return Cell((dd, d, m), seed, style);
            case "hi": return Cell((d, m, l), seed, style);
            case "mid": return Cell((d, m, l), seed + 3, style);
            default: // ridge: dark line below a light line across the middle
            {
                var b = Cell((d, m, l), seed + 5, style);
                for (int x = 0; x < 8; x++) { b.Set(x, 3, l); b.Set(x, 4, dd); }
                return b;
            }
        }
    }

    // ------------------------------------------------------------------ people (8x16)

    private static readonly string[] Grid =
    {
        "..hhhh..", "..hhhh..", "..ssss..", "..ssss..", "...ss...", ".tttttt.", ".tttttt.", "sttttttS",
        ".tttttt.", ".tttttt.", ".bbbbbb.", ".ll..ll.", ".ll..ll.", ".ll..ll.", ".ll..ll.", ".ff..ff.",
    };

    public PixelBuffer Person(string role, string team)
    {
        var tr = team == "neutral" ? R["brown"] : R[team];
        uint tun = tr[Math.Min(2, tr.Length - 1)], tunD = tr[Math.Min(1, tr.Length - 1)], skin = R["skin"][2];
        uint hair = role == "hero" ? R["gold"][2] : R["brown"][1], belt = R["leather"][0], leg = role == "soldier" ? R["iron"][1] : R["brown"][0], foot = R["leather"][0];
        var b = new PixelBuffer(8, 16);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 8; x++)
            {
                char ch = Grid[y][x]; uint c = 0;
                switch (ch)
                {
                    case 'h': c = role == "soldier" ? R["iron"][3] : hair; break;
                    case 's': case 'S': c = skin; break;
                    case 't': c = (x == 1 || x == 6) && y > 5 ? tunD : tun; break;
                    case 'b': c = role == "hero" ? R["gold"][2] : belt; break;
                    case 'l': c = leg; break;
                    case 'f': c = foot; break;
                }
                if (c != 0) b.Set(x, y, c);
            }
        if (role == "soldier") for (int y = 0; y < 16; y++) b.Set(7, y, y == 0 ? R["iron"][3] : R["wood"][2]); // spear
        if (role == "hero") { b.Set(1, 5, R["gold"][3]); b.Set(6, 5, R["gold"][3]); b.Set(3, 0, R["gold"][3]); b.Set(4, 0, R["gold"][3]); }
        b.Outline(P["out"]);
        return Fit(b, 8, 16);
    }

    /// <summary>The outline pass can overshoot the declared size by one pixel on each side; mini sprites keep their 8x16 box (outline on the inner ring is fine).</summary>
    private static PixelBuffer Fit(PixelBuffer b, int w, int h) => b;

    // ------------------------------------------------------------------ trees and shrubs (16x20 / 8x8)

    /// <summary>A tree crown in the summer foliage colours; the leaf fall of autumn and winter is the season shader thinning the canopy pixels.</summary>
    public PixelBuffer Tree(string species)
    {
        var b = new PixelBuffer(16, 20);
        var wood = R["wood"];
        int seed = 4400 + species.Length * 17 + species[0];
        // trunk
        for (int y = 12; y < 18; y++) { b.Set(7, y, wood[2]); b.Set(8, y, wood[1]); }
        b.Set(6, 17, wood[1]); b.Set(9, 17, wood[1]);
        if (species == "pine")
        {
            var c = R["conifer_summer"];
            for (int y = 1; y < 15; y++)
            {
                int half = 1 + (y - 1) * 6 / 13;
                for (int x = 8 - half - 1; x <= 8 + half; x++)
                {
                    double h = Hash(x, y, seed);
                    int k = x < 8 - half / 2 ? 2 : x > 8 + half / 2 ? 0 : 1;
                    if (h < .18) k = Math.Min(3, k + 1);
                    if ((y & 3) == 3) k = Math.Max(0, k - 1);
                    b.Set(x, y, c[k]);
                }
            }
        }
        else
        {
            var ramp = species switch { "oak" => R["foliage_summer"], _ => R["leaf_summer"] };
            double rx = species == "birch" ? 5.5 : 7, ry = species == "birch" ? 7 : 6;
            for (int y = 0; y < 14; y++)
                for (int x = 0; x < 16; x++)
                {
                    double dx = (x - 7.5) / rx, dy = (y - 6.5) / ry, d = dx * dx + dy * dy + (Hash(x, y, seed) - .5) * .35;
                    if (d > 1) continue;
                    double lit = (x - 7.5) * -.5 + (y - 6.5) * -.8; // light from the top-left
                    int k = lit > 2.6 ? 3 : lit > .4 ? 2 : lit > -2.2 ? 1 : 0;
                    if (Hash(x, y, seed + 1) < .15) k = Math.Clamp(k + (Hash(x, y, seed + 2) < .5 ? 1 : -1), 0, 3);
                    b.Set(x, y, ramp[k]);
                }
            if (species == "apple")
                foreach (var (x, y) in new[] { (5, 5), (9, 4), (10, 8), (6, 9), (8, 6) }) b.Set(x, y, R["red"][3]);
        }
        b.Outline(P["out"]);
        return b;
    }

    public PixelBuffer Bush()
    {
        var b = new PixelBuffer(8, 8); var f = R["foliage_summer"];
        for (int y = 2; y < 7; y++)
            for (int x = 0; x < 8; x++)
            {
                double dx = (x - 3.5) / 3.8, dy = (y - 4.5) / 2.6;
                if (dx * dx + dy * dy > 1) continue;
                b.Set(x, y, f[Math.Clamp((x + (7 - y)) * 4 / 14, 0, 3)]);
            }
        return b.Outline(P["out"]);
    }

    public PixelBuffer Rock()
    {
        var b = new PixelBuffer(8, 8); var s = R["stone"];
        for (int y = 3; y < 7; y++)
            for (int x = 1; x < 7; x++)
            {
                if ((y == 3 && (x == 1 || x == 6)) || (y == 6 && (x == 1 || x == 6))) continue;
                b.Set(x, y, s[Math.Clamp(1 + (x < 3 ? 1 : 0) + (y < 5 ? 1 : 0) - (y == 6 ? 1 : 0), 0, 4)]);
            }
        return b.Outline(P["out"]);
    }

    /// <summary>Small generic object (crate, barrel, stall, hay): a 4x4 block in the given colour, outlined.</summary>
    public PixelBuffer Block(uint mid, uint light)
    {
        var b = new PixelBuffer(8, 8);
        for (int y = 2; y < 6; y++) for (int x = 2; x < 6; x++) b.Set(x, y, y == 2 || x == 2 ? light : mid);
        return b.Outline(P["out"]);
    }
}
