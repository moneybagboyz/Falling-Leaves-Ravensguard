using System.Text.Json;

namespace Ravensguard.Art.Sprites;

/// <summary>
/// Seasonal recolour. Sprites are authored in the summer rows of a family (grass, foliage, leaf, conifer); a season is a
/// per-index palette swap summer -> season. All families are merged into ONE single-pass map (keys are summer colours,
/// which never collide between families), so a whole scene or a single sprite can be recoloured in one lookup.
/// </summary>
public static class Seasons
{
    public static readonly string[] Names = { "spring", "summer", "autumn", "winter" };
    public static readonly string[] Families = { "grass", "foliage", "leaf", "conifer" };

    public static string Ramp(string family, string season) => $"{family}_{season}";

    /// <summary>One merged summer -> season map over every seasonal family.</summary>
    public static Dictionary<uint, uint> Map(Palette pal, string season)
    {
        var m = new Dictionary<uint, uint>();
        foreach (var f in Families)
            foreach (var kv in PixelBuffer.RampSwap(pal.R[Ramp(f, "summer")], pal.R[Ramp(f, season)]))
                m[kv.Key] = kv.Value;
        return m;
    }

    public static PixelBuffer Apply(Palette pal, PixelBuffer b, string season) => season == "summer" ? b : b.Recolor(Map(pal, season));

    /// <summary>
    /// Snow cover that follows autotile edges. A pixel of <paramref name="summer"/> (the unrecoloured sprite) that is one of the grass-family
    /// colours takes the full-cover overlay pixel, so the grass lip of a road, field, hedge or shore tile turns white like the open grass around it
    /// (the old whole-tile overlay left green strips). Every other pixel keeps <paramref name="seasoned"/>, or takes the dusting overlay when
    /// <paramref name="dustOthers"/> is set (roads and fields). Overlay pixels that are transparent leave the ground showing (patchy snow).
    /// </summary>
    public static PixelBuffer SnowOver(Palette pal, PixelBuffer summer, PixelBuffer seasoned, PixelBuffer full, PixelBuffer dust, bool dustOthers)
    {
        var grass = new HashSet<uint>(pal.R[Ramp("grass", "summer")]);
        var r = seasoned.Clone();
        for (int y = 0; y < summer.H; y++)
            for (int x = 0; x < summer.W; x++)
            {
                int i = y * summer.W + x;
                uint c = summer.Data[i];
                bool isGrass = grass.Contains(c);
                if (!isGrass)
                {
                    // a flower or leaf speck counts as grass when three of its four neighbours are grass (a berry on a hedge is not); the game shader uses the same rule
                    int n = (grass.Contains(summer.Get(x - 1, y)) ? 1 : 0) + (grass.Contains(summer.Get(x + 1, y)) ? 1 : 0) + (grass.Contains(summer.Get(x, y - 1)) ? 1 : 0) + (grass.Contains(summer.Get(x, y + 1)) ? 1 : 0);
                    isGrass = n >= 3;
                }
                if (isGrass) { if ((full.Data[i] >> 24) != 0) r.Data[i] = full.Data[i]; }
                else if (dustOthers && (dust.Data[i] >> 24) != 0) r.Data[i] = dust.Data[i];
            }
        return r;
    }

    /// <summary>season_ramps.png: one row per (family, season) = family*4+season, 4 swatches (x = ramp index); plus season_ramps.json.</summary>
    public static void Export(Palette pal, string outDir)
    {
        var img = new PixelBuffer(4 * 8, Families.Length * Names.Length * 8);
        var rows = new List<object>();
        for (int f = 0; f < Families.Length; f++)
            for (int s = 0; s < Names.Length; s++)
            {
                int row = f * Names.Length + s;
                var cols = pal.R[Ramp(Families[f], Names[s])];
                for (int i = 0; i < cols.Length; i++) img.Rect(i * 8, row * 8, 8, 8, cols[i]);
                rows.Add(new { row, family = Families[f], season = Names[s], ramp = Ramp(Families[f], Names[s]), colors = cols.Select(c => "#" + (c & 0xFFFFFF).ToString("x6")).ToArray() });
            }
        Png.Write(Path.Combine(outDir, "season_ramps.png"), img);
        var doc = new
        {
            note = "Author = summer row. Game recolour: for a texel equal to summer[family][i], output ramp[family][season][i] (one merged lookup). season_ramps.png row = familyIndex*4 + seasonIndex, x = i*8.",
            seasons = Names, families = Families, swatch = 8, rows,
            water = pal.R["water"].Select(c => "#" + (c & 0xFFFFFF).ToString("x6")).ToArray(),
            ice = pal.R["ice"].Select(c => "#" + (c & 0xFFFFFF).ToString("x6")).ToArray(),
            snow = pal.R["snow"].Select(c => "#" + (c & 0xFFFFFF).ToString("x6")).ToArray()
        };
        File.WriteAllText(Path.Combine(outDir, "season_ramps.json"), JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
