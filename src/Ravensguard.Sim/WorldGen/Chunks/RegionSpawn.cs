using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.WorldGen.Chunks;

/// <summary>Where to put the hero in a generated region: spots found from the plans, then checked against the generated cells so the hero never starts in a wall.</summary>
public static class RegionSpawn
{
    /// <summary>The largest town (by population), or the largest village if the region has none.</summary>
    public static RegionSettlement? LargestTown(RegionData r)
        => r.Towns.OrderByDescending(s => s.TargetPop).FirstOrDefault() ?? r.Villages.OrderByDescending(s => s.TargetPop).FirstOrDefault();

    /// <summary>A walkable paved cell in the square of the largest town (next to the well, on the cobbles).</summary>
    public static Cell3? TownSquare(ChunkStreamer s, RegionData r)
    {
        var t = LargestTown(r);
        if (t == null) return null;
        var q = t.Plan.Square;
        return NearestWalkable(s, q.CenterX, q.CenterY + 3, 12, paved: true) ?? NearestWalkable(s, t.CenterX, t.CenterY, 20, paved: false);
    }

    /// <summary>A walkable cell on the first lane of a village's fields, a little way out from its last house.</summary>
    public static Cell3? VillageEdge(ChunkStreamer s, RegionData r)
    {
        foreach (var v in r.Villages.OrderByDescending(v => v.TargetPop))
        {
            if (v.Parcels.Count == 0) continue;
            // the field tile nearest the village centre
            int best = int.MaxValue, bx = 0, by = 0;
            foreach (int pi in v.Parcels)
            {
                var p = r.Parcels[pi];
                for (int k = 0; k < p.FieldCount; k++)
                {
                    int tile = r.Fields[p.FieldStart + k].Tile;
                    int fx = tile % RegionData.Size * RegionData.TileM + 32, fy = tile / RegionData.Size * RegionData.TileM + 32;
                    int d = (int)Math.Min(int.MaxValue, (long)(fx - v.CenterX) * (fx - v.CenterX) + (long)(fy - v.CenterY) * (fy - v.CenterY));
                    if (d < best) { best = d; bx = fx; by = fy; }
                }
            }
            if (best == int.MaxValue) continue;
            var c = NearestWalkable(s, bx, by, 16, paved: false);
            if (c != null) return c;
        }
        return null;
    }

    /// <summary>A walkable cell by the first bridge of the region (a ford if there is no bridge).</summary>
    public static Cell3? RiverCrossing(ChunkStreamer s, RegionData r)
    {
        RoadCrossing? best = null;
        foreach (var e in r.RoadEdges)
            foreach (var c in e.Crossings)
                if (best == null || (c.Kind == CrossingKind.Bridge && best.Kind != CrossingKind.Bridge) || (c.Kind == best.Kind && c.WidthCm > best.WidthCm)) best = c;
        return best == null ? null : NearestWalkable(s, best.At.X, best.At.Y, 14, paved: false);
    }

    public static Cell3? NearestWalkable(ChunkStreamer s, int wx, int wy, int radius, bool paved)
    {
        for (int r = 0; r <= radius; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = wx + dx, y = wy + dy;
                    if (!s.IsWalkable(x, y)) continue;
                    var g = Ravensguard.Sim.WorldGen.Chunks.TileCode.Ground(s.TileAt(x, y, 0));
                    if (paved && g is not (Ravensguard.Sim.Live.GroundKind.Cobble or Ravensguard.Sim.Live.GroundKind.Dirt)) continue;
                    if (s.TileAt(x, y, 0) != 0 && Ravensguard.Sim.WorldGen.Chunks.TileCode.Structure(s.TileAt(x, y, 0)) != 0) continue;
                    return new Cell3(x, y, 0);
                }
        return null;
    }
}
