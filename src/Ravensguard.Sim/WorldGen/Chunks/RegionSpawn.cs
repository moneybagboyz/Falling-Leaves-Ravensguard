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

    /// <summary>A walkable street cell near the outer end of a village's main street, where the houses give way to fields.</summary>
    public static Cell3? VillageEdge(ChunkStreamer s, RegionData r)
    {
        foreach (var v in r.Villages.OrderByDescending(v => v.TargetPop))
        {
            if (v.Parcels.Count == 0 || v.Plan.Streets.Count == 0) continue;
            var st = v.Plan.Streets[0];
            var end = v.Plan.Horizontal ? (st.B.X > st.A.X ? st.B : st.A) : (st.B.Y > st.A.Y ? st.B : st.A);
            var c = NearestWalkable(s, end.X - (v.Plan.Horizontal ? 4 : 0), end.Y - (v.Plan.Horizontal ? 0 : 4), 8, paved: true);
            if (c != null) return c;
        }
        return null;
    }

    /// <summary>A walkable cell by the first bridge of the region (a ford if there is no bridge).</summary>
    public static Cell3? RiverCrossing(ChunkStreamer s, RegionData r)
    {
        // a bridge over a proper river, of middling width (a wide one is a lake shore, a narrow one a ditch)
        RoadCrossing? best = null; int bestScore = int.MaxValue;
        foreach (var e in r.RoadEdges)
            foreach (var c in e.Crossings)
            {
                var w = r.Waterways.FirstOrDefault(x => x.Id == c.WaterwayId);
                int score = Math.Abs(c.WidthCm - 1200) + (c.Kind == CrossingKind.Bridge ? 0 : 5000) + (w?.Kind == WaterKind.River ? 0 : 5000);
                if (score < bestScore) { bestScore = score; best = c; }
            }
        return best == null ? null : NearestWalkable(s, best.At.X, best.At.Y, 14, paved: false);
    }

    /// <summary>A walkable street cell in the named settlement: its square if it has one, else its centre. Null if no such settlement.</summary>
    public static Cell3? AtSettlement(ChunkStreamer s, RegionData r, string name)
    {
        var t = r.Settlements.FirstOrDefault(x => x.Name == name);
        if (t == null) return null;
        var q = t.Plan.Square;
        return (q.W > 0 ? NearestWalkable(s, q.CenterX, q.CenterY + 3, 12, paved: true) : null)
            ?? NearestWalkable(s, t.CenterX, t.CenterY, 24, paved: true)
            ?? NearestWalkable(s, t.CenterX, t.CenterY, 40, paved: false);
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
