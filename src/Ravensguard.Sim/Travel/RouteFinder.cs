using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Travel;

/// <summary>The grid a route is planned on: time cost of entering a tile in permille of a road tile (1000 = a road; 0 = impassable).</summary>
public interface IRouteGrid
{
    int Size { get; }
    int TileM { get; }
    int Cost(int x, int y);
}

/// <summary>A uniform test grid.</summary>
public sealed class OpenGrid(int size, int tileM = 64, Func<int, int, int>? cost = null) : IRouteGrid
{
    public int Size => size; public int TileM => tileM;
    public int Cost(int x, int y) => cost?.Invoke(x, y) ?? 1000;
}

/// <summary>The region as a route grid (spec 4.2): roads are cheapest, tracks and fields cost more, woodland and marsh more again, water cannot be crossed except on a road (a bridge or a ford); a closed ford is blocked.</summary>
public sealed class RegionRouteGrid(RegionData r, Func<int, int, bool>? fordClosed = null) : IRouteGrid
{
    public int Size => RegionData.Size; public int TileM => RegionData.TileM;
    public int Cost(int x, int y)
    {
        int t = RegionData.Index(x, y); byte w = r.Water[t]; var road = (RoadClass)r.Road[t];
        if (road != RoadClass.None)
        {
            if ((w & (WaterFlags.River | WaterFlags.Stream | WaterFlags.Lake | WaterFlags.Sea)) != 0 && fordClosed != null && fordClosed(x, y)) return 0;
            return road switch { RoadClass.Highway => 1000, RoadClass.Road => 1000, RoadClass.Track => 1100, _ => 1250 };
        }
        if ((w & (WaterFlags.River | WaterFlags.Lake | WaterFlags.Sea)) != 0) return 0;
        if (r.Built[t] != 0) return 1250;
        int c = 1250;                                                             // field and grass
        if ((w & WaterFlags.Stream) != 0) c = 2500;
        else if (r.Biomes[t] == Biome.Marsh) c = 2200;
        else if (r.Forest[t] >= 30) c = 1670;
        else if (r.Biomes[t] == Biome.Mountain) c = 2500;
        return c;
    }
}

/// <summary>A* over a route grid, eight neighbours, deterministic ties. Returns the path as tile centres in millimetres (region frame), start and goal exact.</summary>
public static class RouteFinder
{
    public static RoutePath? Plan(IRouteGrid g, long fromXMm, long fromYMm, long toXMm, long toYMm)
    {
        int n = g.Size, tm = g.TileM;
        int sx = (int)Math.Clamp(fromXMm / 1000 / tm, 0, n - 1), sy = (int)Math.Clamp(fromYMm / 1000 / tm, 0, n - 1);
        int gx = (int)Math.Clamp(toXMm / 1000 / tm, 0, n - 1), gy = (int)Math.Clamp(toYMm / 1000 / tm, 0, n - 1);
        int s = sy * n + sx, goal = gy * n + gx;
        var best = new Dictionary<int, long> { [s] = 0 }; var from = new Dictionary<int, int>();
        var open = new PriorityQueue<int, (long F, int Tie)>(); open.Enqueue(s, (H(sx, sy, gx, gy), s));
        var closed = new HashSet<int>();
        int[] dx = [1, -1, 0, 0, 1, 1, -1, -1], dy = [0, 0, 1, -1, 1, -1, 1, -1];
        while (open.Count > 0)
        {
            int cur = open.Dequeue(); if (!closed.Add(cur)) continue;
            if (cur == goal) break;
            int cx = cur % n, cy = cur / n;
            for (int k = 0; k < 8; k++)
            {
                int x = cx + dx[k], y = cy + dy[k]; if ((uint)x >= (uint)n || (uint)y >= (uint)n) continue;
                int c = x == gx && y == gy ? Math.Max(1000, g.Cost(x, y)) : g.Cost(x, y); if (c <= 0) continue;
                long step = (long)c * (k < 4 ? 1000 : 1414) / 1000;
                int nx = y * n + x; long ng = best[cur] + step;
                if (best.TryGetValue(nx, out long old) && old <= ng) continue;
                best[nx] = ng; from[nx] = cur; open.Enqueue(nx, (ng + H(x, y, gx, gy), nx));
            }
        }
        if (!closed.Contains(goal) && !from.ContainsKey(goal) && s != goal) return null;
        var tiles = new List<int> { goal }; int at = goal;
        while (at != s && from.TryGetValue(at, out int p)) { tiles.Add(p); at = p; }
        tiles.Reverse();
        var xs = new List<long> { fromXMm }; var ys = new List<long> { fromYMm };
        for (int i = 1; i < tiles.Count - 1; i++) { xs.Add((tiles[i] % n * (long)tm + tm / 2) * 1000); ys.Add((tiles[i] / n * (long)tm + tm / 2) * 1000); }
        xs.Add(toXMm); ys.Add(toYMm);
        return new RoutePath([.. xs], [.. ys]);
    }

    private static long H(int x, int y, int gx, int gy) { int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy); return 1000L * (Math.Max(dx, dy) + 414L * Math.Min(dx, dy) / 1000); }
}
