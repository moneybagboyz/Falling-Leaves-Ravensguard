namespace Ravensguard.Art;

public static class Validator
{
    /// <summary>Checks palette use, declared size and outlines. Returns error messages (empty = pass).</summary>
    public static List<string> Validate(IReadOnlyList<SpriteDef> defs, Palette pal, Dictionary<string, PixelBuffer> cache)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>();
        uint outc = pal.Colors["out"];
        foreach (var d in defs)
        {
            if (!ids.Add(d.Id)) errors.Add($"{d.Id}: duplicate id");
            var b = cache[d.Id];
            if (b.W != d.W || b.H != d.H) { errors.Add($"{d.Id}: size {b.W}x{b.H} but declared {d.W}x{d.H}"); continue; }
            var bad = new Dictionary<uint, int>();
            int opaque = 0, outlinePx = 0, holes = 0, openEdge = 0;
            var openAt = new List<string>();
            for (int y = 0; y < b.H; y++)
                for (int x = 0; x < b.W; x++)
                {
                    uint c = b.Data[y * b.W + x];
                    if (c == 0) { holes++; continue; }
                    if ((c >> 24) != 255 || !pal.Contains(c)) { bad[c] = bad.GetValueOrDefault(c) + 1; continue; }
                    opaque++;
                    if (c == outc) { outlinePx++; continue; }
                    if (d.NeedsOutline && (HasHole(b, x - 1, y) || HasHole(b, x + 1, y) || HasHole(b, x, y - 1) || HasHole(b, x, y + 1))) { openEdge++; if (openAt.Count < 6) openAt.Add($"({x},{y})"); }
                }
            foreach (var (c, n) in bad.OrderByDescending(k => k.Value).Take(3))
                errors.Add($"{d.Id}: {n} pixel(s) with non-palette colour #{c:x8}");
            if (d.Kind == SpriteKind.Terrain && holes > 0) errors.Add($"{d.Id}: terrain tile has {holes} transparent pixel(s)");
            if (d.NeedsOutline)
            {
                if (opaque == 0) errors.Add($"{d.Id}: empty sprite");
                else if (outlinePx == 0) errors.Add($"{d.Id}: no outline pixels");
                if (openEdge > 0) errors.Add($"{d.Id}: {openEdge} edge pixel(s) not outlined, e.g. {string.Join(" ", openAt)}");
            }
        }
        return errors;

        // Outside the sprite counts as empty: a sprite whose body touches the cell edge is clipped, so it is not outlined there.
        static bool HasHole(PixelBuffer b, int x, int y) => x < 0 || y < 0 || x >= b.W || y >= b.H || b.Data[y * b.W + x] == 0;
    }
}
