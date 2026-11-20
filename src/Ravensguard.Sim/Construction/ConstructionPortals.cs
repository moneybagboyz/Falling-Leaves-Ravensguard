using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Construction;

/// <summary>
/// Stairs and ladders as portals of a level grid (plan 2.2a, spec 4.5): a stair up whose cell above holds a stair down, or a ladder under a trapdoor, is a portal; when either end is gone
/// the portal is disabled (the path re-plans), and it comes back when both stand again. The same rule as <see cref="BlueprintPlacer"/>; the street window calls it for a re-stamped chunk.
/// </summary>
public static class ConstructionPortals
{
    /// <summary>Brings the portals of <paramref name="g"/> whose lower end lies in the rectangle (all of it by default) in line with the pieces <paramref name="kindAt"/> shows (grid cells).</summary>
    public static int Sync(LevelGrid g, Func<int, int, int, PieceKind?> kindAt, int x0 = 0, int y0 = 0, int w = -1, int h = -1)
    {
        if (w < 0) w = g.Width; if (h < 0) h = g.Height;
        int changed = 0;
        // what should be
        var want = new HashSet<(Cell3 A, Cell3 B)>();
        for (int z = g.MinZ; z < g.MaxZ; z++)
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                {
                    var k = kindAt(x, y, z);
                    if (k is not (PieceKind.StairsUp or PieceKind.Ladder)) continue;
                    var up = kindAt(x, y, z + 1);
                    if ((k == PieceKind.StairsUp && up == PieceKind.StairsDown) || (k == PieceKind.Ladder && up == PieceKind.Trapdoor)) want.Add((new Cell3(x, y, z), new Cell3(x, y, z + 1)));
                }
        // what is
        foreach (var p in g.Portals)
        {
            if (p.A.X < x0 || p.A.X >= x0 + w || p.A.Y < y0 || p.A.Y >= y0 + h) continue;
            if (p.Kind is not (PortalKind.Stairs or PortalKind.Ladder)) continue;
            bool keep = want.Remove((p.A, p.B));
            if (p.Enabled != keep) { g.SetPortalEnabled(p.Id, keep); changed++; }
        }
        foreach (var (a, b) in want.OrderBy(t => t.A.Z).ThenBy(t => t.A.Y).ThenBy(t => t.A.X))
        {
            var kind = kindAt(a.X, a.Y, a.Z) == PieceKind.Ladder ? PortalKind.Ladder : PortalKind.Stairs;
            g.AddPortal(kind, a, b, kind == PortalKind.Ladder ? BlueprintPlacer.LadderCost : BlueprintPlacer.StairCost);
            changed++;
        }
        return changed;
    }
}
