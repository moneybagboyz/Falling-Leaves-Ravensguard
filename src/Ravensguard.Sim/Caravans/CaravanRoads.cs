using System.Runtime.CompilerServices;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Caravans;

/// <summary>The road graph of one region (Phase 1 <c>RoadNode</c>/<c>RoadEdge</c>) with a deterministic integer Dijkstra (caravans spec 3.2): ties go to the lowest node and edge id.</summary>
public sealed class RoadGraph
{
    private readonly RegionData _r;
    private readonly List<(int Edge, int Other)>[] _adj;
    private static readonly ConditionalWeakTable<RegionData, RoadGraph> Cache = new();

    private RoadGraph(RegionData r)
    {
        _r = r; _adj = new List<(int, int)>[r.RoadNodes.Count];
        for (int i = 0; i < _adj.Length; i++) _adj[i] = [];
        foreach (var e in r.RoadEdges)
        {
            if (e.A < 0 || e.B < 0 || e.A >= _adj.Length || e.B >= _adj.Length) continue;
            _adj[e.A].Add((e.Id, e.B)); _adj[e.B].Add((e.Id, e.A));
        }
        foreach (var l in _adj) l.Sort((a, b) => a.Edge.CompareTo(b.Edge));
    }

    public static RoadGraph For(RegionData r) { lock (Cache) { if (Cache.TryGetValue(r, out var g)) return g; g = new RoadGraph(r); Cache.Add(r, g); return g; } }

    /// <summary>Edge indices (into <see cref="RegionData.RoadEdges"/>) of the cheapest route, in walking order, with the node reached after each; null when unreachable.</summary>
    public List<(int Edge, int From, int To)>? Route(int from, int to)
    {
        int n = _adj.Length; if ((uint)from >= (uint)n || (uint)to >= (uint)n) return null;
        var dist = new long[n]; var prevEdge = new int[n]; var prevNode = new int[n]; var done = new bool[n];
        Array.Fill(dist, long.MaxValue); Array.Fill(prevEdge, -1); Array.Fill(prevNode, -1);
        dist[from] = 0;
        var pq = new PriorityQueue<int, (long, int)>(); pq.Enqueue(from, (0, from));
        while (pq.TryDequeue(out int u, out _))
        {
            if (done[u]) continue; done[u] = true;
            if (u == to) break;
            foreach (var (ei, v) in _adj[u])
            {
                var e = _r.RoadEdges[ei];
                long cost = (long)Math.Max(1, e.LengthM) * 1000 / Math.Max(1, Vehicles.ClassPermille(e.Class));
                long nd = dist[u] + cost;
                if (nd < dist[v] || (nd == dist[v] && !done[v] && ei < prevEdge[v])) { dist[v] = nd; prevEdge[v] = ei; prevNode[v] = u; pq.Enqueue(v, (nd, v)); }
            }
        }
        if (dist[to] == long.MaxValue) return null;
        var path = new List<(int, int, int)>();
        for (int v = to; v != from; v = prevNode[v]) path.Add((prevEdge[v], prevNode[v], v));
        path.Reverse();
        return path;
    }
}

/// <summary>Builds the <see cref="CaravanPath"/> from a market town to a border gate over the region's real roads.</summary>
public static class CaravanRoads
{
    /// <summary>The neighbour a gate on <paramref name="side"/> leads to.</summary>
    public static RegionCoord Across(RegionCoord home, BorderSide side) => side switch
    {
        BorderSide.East => home.Offset(1, 0), BorderSide.West => home.Offset(-1, 0), BorderSide.South => home.Offset(0, 1), _ => home.Offset(0, -1),
    };

    /// <summary>
    /// The path from the road node of settlement <paramref name="settlement"/> to the outer node of gate <paramref name="gate"/> (index into <see cref="RegionData.Gates"/>),
    /// in continent metres, with the bridge tolls on it. Null when the roads do not join them.
    /// </summary>
    public static CaravanPath? Build(RegionData region, int settlement, int gate, bool realmToll)
    {
        if ((uint)settlement >= (uint)region.Settlements.Count || (uint)gate >= (uint)region.Gates.Count) return null;
        int from = region.Settlements[settlement].RoadNode;
        int outer = region.Settlements.Count + 2 * gate;
        if (from < 0 || outer >= region.RoadNodes.Count) return null;
        var route = RoadGraph.For(region).Route(from, outer); if (route == null) return null;
        var home = RegionCoord.OfCell(region.CellX, region.CellY);
        int ox = home.OriginXM, oy = home.OriginYM;
        var pts = new List<(int X, int Y, RoadClass Class)>();
        var bridges = new List<int>();                      // index of the vertex nearest each bridge
        var start = region.RoadNodes[from];
        pts.Add((ox + start.X, oy + start.Y, RoadClass.Road));
        foreach (var (ei, a, b) in route)
        {
            var e = region.RoadEdges[ei];
            var pp = e.Points;
            bool forward = e.A == a;
            int first = pts.Count;
            if (pp.Length == 0) { var nb = region.RoadNodes[b]; pts.Add((ox + nb.X, oy + nb.Y, e.Class)); }
            else
                for (int k = 0; k < pp.Length; k++)
                {
                    var p = pp[forward ? k : pp.Length - 1 - k];
                    int px = ox + p.X, py = oy + p.Y;
                    if (pts[^1].X == px && pts[^1].Y == py) continue;
                    pts.Add((px, py, e.Class));
                }
            var end = region.RoadNodes[b];
            if (pts[^1].X != ox + end.X || pts[^1].Y != oy + end.Y) pts.Add((ox + end.X, oy + end.Y, e.Class));
            foreach (var c in e.Crossings)
            {
                if (c.Kind != CrossingKind.Bridge) continue;
                int best = first, bd = int.MaxValue;
                for (int i = Math.Max(1, first); i < pts.Count; i++) { int dx = pts[i].X - ox - c.At.X, dy = pts[i].Y - oy - c.At.Y; int d = Math.Abs(dx) + Math.Abs(dy); if (d < bd) { bd = d; best = i; } }
                bridges.Add(best);
            }
        }
        if (pts.Count < 2) return null;
        var path = CaravanPath.FromPoints(pts);
        var tolls = bridges.Select(i => new TollMark(path.CumMm[Math.Clamp(i, 0, path.CumMm.Length - 1)], TollKind.Bridge)).ToList();
        if (realmToll) tolls.Add(new TollMark(path.LengthMm, TollKind.RealmGate));
        return CaravanPath.FromPoints(pts, tolls);
    }
}
