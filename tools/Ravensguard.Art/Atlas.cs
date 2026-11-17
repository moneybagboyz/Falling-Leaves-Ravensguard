using System.Text.Json;

namespace Ravensguard.Art;

public static class Atlas
{
    public sealed record Placement(string Id, string Atlas, int X, int Y, int W, int H);

    /// <summary>Packs sprites into grid atlases (one PNG per atlas name), writes PNGs and index.json.</summary>
    public static void Bake(IReadOnlyList<SpriteDef> defs, Palette pal, string outDir, Dictionary<string, PixelBuffer> cache)
    {
        Directory.CreateDirectory(outDir);
        var places = new List<Placement>();
        var atlases = new Dictionary<string, object>();
        foreach (var g in defs.GroupBy(d => d.Atlas))
        {
            var list = g.ToList();
            int cw = list.Max(d => d.W), ch = list.Max(d => d.H);
            int cols = Math.Min(list.Count, Math.Max(1, 1024 / cw)), rows = (list.Count + cols - 1) / cols;
            var img = new PixelBuffer(cols * cw, rows * ch);
            for (int i = 0; i < list.Count; i++)
            {
                int x = (i % cols) * cw, y = (i / cols) * ch;
                img.Blit(cache[list[i].Id], x, y);
                places.Add(new Placement(list[i].Id, g.Key, x, y, list[i].W, list[i].H));
            }
            Png.Write(Path.Combine(outDir, g.Key + ".png"), img);
            atlases[g.Key] = new { file = g.Key + ".png", width = img.W, height = img.H, cell = new[] { cw, ch } };
        }
        var sprites = places.ToDictionary(p => p.Id, p => new { atlas = p.Atlas, x = p.X, y = p.Y, w = p.W, h = p.H });
        var json = JsonSerializer.Serialize(new { atlases, sprites }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outDir, "index.json"), json + "\n");

        Sprites.Seasons.Export(pal, outDir);

        // palette export: swatch strip + ramps for shader lookup tables
        var names = pal.Colors.Keys.ToList();
        var sw = new PixelBuffer(names.Count * 8, 8);
        for (int i = 0; i < names.Count; i++) sw.Rect(i * 8, 0, 8, 8, pal.Colors[names[i]]);
        Png.Write(Path.Combine(outDir, "palette.png"), sw);
        var ramps = pal.RampNames.ToDictionary(kv => kv.Key, kv => kv.Value.Select(n => "#" + (pal.Colors[n] & 0xFFFFFF).ToString("x6")).ToArray());
        File.WriteAllText(Path.Combine(outDir, "palette_ramps.json"), JsonSerializer.Serialize(ramps, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
