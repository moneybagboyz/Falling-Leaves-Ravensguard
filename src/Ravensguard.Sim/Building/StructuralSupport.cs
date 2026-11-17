using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Building;

/// <summary>Plan §2.2a: an upper floor tile needs a wall or post on the level below within 3 tiles (no physics).</summary>
public static class StructuralSupport
{
    public const int Reach = 3;

    /// <summary>Floor-bearing cells on levels above ground with no wall or post within <see cref="Reach"/> tiles directly below.</summary>
    public static List<Cell3> Unsupported(BuildingGrid g, PieceCatalog pieces)
    {
        var bad = new List<Cell3>();
        foreach (var (z, lv) in g.Levels)
        {
            if (z <= 0) continue;
            var below = g.Level(z - 1);
            for (int y = 0; y < lv.Height; y++)
                for (int x = 0; x < lv.Width; x++)
                {
                    var k = pieces.KindOf(lv.Structure[lv.Idx(x, y)]);
                    if (k == null || !PieceCatalog.IsFloorBearing(k.Value)) continue;
                    if (below == null || !HasSupportNear(below, pieces, x, y)) bad.Add(new Cell3(x, y, z));
                }
        }
        return bad;
    }

    private static bool HasSupportNear(BuildingLevel below, PieceCatalog pieces, int x, int y)
    {
        for (int dy = -Reach; dy <= Reach; dy++)
            for (int dx = -Reach; dx <= Reach; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (!below.InBounds(nx, ny)) continue;
                var k = pieces.KindOf(below.Structure[below.Idx(nx, ny)]);
                if (k != null && PieceCatalog.IsSupport(k.Value)) return true;
            }
        return false;
    }
}
