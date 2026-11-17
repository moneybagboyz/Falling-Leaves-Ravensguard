namespace Ravensguard.Art;

public static class Preview
{
    /// <summary>Composes every sprite at 2x on a dark background, wrapping rows and breaking between groups.</summary>
    public static PixelBuffer Compose(IReadOnlyList<SpriteDef> defs, Dictionary<string, PixelBuffer> cache, int scale = 2, int width = 1500)
    {
        const uint bg = 0xFF1D181C, cell = 0xFF2B2420;
        int pad = 6, margin = 12;
        var placed = new List<(PixelBuffer Img, int X, int Y, int W, int H)>();
        int x = margin, y = margin, rowH = 0; string? group = null;
        foreach (var d in defs)
        {
            var img = cache[d.Id].Scaled(scale);
            int cw = img.W + pad, chh = img.H + pad;
            if (group != null && (d.Group != group || x + cw > width - margin)) { x = margin; y += rowH + (d.Group != group ? 14 : 4); rowH = 0; }
            group = d.Group;
            placed.Add((img, x, y, cw, chh));
            x += cw + 4; rowH = Math.Max(rowH, chh);
        }
        var sheet = new PixelBuffer(width, y + rowH + margin);
        Array.Fill(sheet.Data, bg);
        foreach (var p in placed)
        {
            sheet.Rect(p.X, p.Y, p.W, p.H, cell);
            sheet.Blit(p.Img, p.X + pad / 2, p.Y + pad / 2);
        }
        return sheet;
    }
}
