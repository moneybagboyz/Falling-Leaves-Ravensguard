using System.Text;
using Ravensguard.Content;

namespace Ravensguard.Sim.Building;

/// <summary>The `blueprint` text dump: one ASCII map per level, top level first. Fixtures draw over structure.</summary>
public static class BlueprintText
{
    public static string Render(BuildingGrid g, PieceCatalog pieces, bool showRegions = false, IEnumerable<DetectedRoom>? rooms = null)
    {
        var sb = new StringBuilder();
        sb.Append("blueprint ").Append(g.Id).Append(' ').Append(g.Width).Append('x').Append(g.Height).Append('\n');
        foreach (var (z, lv) in g.Levels.Reverse())
        {
            sb.Append("z=").Append(z >= 0 ? "+" : "").Append(z).Append('\n');
            for (int y = 0; y < lv.Height; y++)
            {
                for (int x = 0; x < lv.Width; x++)
                {
                    int i = lv.Idx(x, y);
                    char c = '_';
                    if (lv.Structure[i] != null) c = Glyph(pieces, lv.Structure[i]!);
                    if (showRegions && lv.Letter[i] != 0) c = lv.Letter[i];
                    if (lv.Fixture[i] != null) c = Glyph(pieces, lv.Fixture[i]!);
                    sb.Append(c);
                }
                sb.Append('\n');
            }
        }
        if (rooms != null)
            foreach (var r in rooms)
                sb.Append($"room#{r.Index} z={r.Z} area={r.Area} enclosed={r.Enclosed} beds={r.Beds} doors={r.Doors.Count} primary={r.Primary?.Id ?? "-"} declared=[{string.Join(",", r.DeclaredPurposes)}]\n");
        return sb.ToString();
    }

    private static char Glyph(PieceCatalog pieces, string id) =>
        pieces.TryGet(id, out var p) && p.Glyph.Length > 0 ? p.Glyph[0] : '?';
}
