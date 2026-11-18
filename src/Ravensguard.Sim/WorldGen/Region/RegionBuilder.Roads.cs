using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.Region;

internal sealed partial class RegionBuilder
{
    private int[] _g = [];
    private int[] _stamp = [];
    private int _stampNo;
    private int[] _from = [];

    private int TileCost(int t)
    {
        byte w = _r.Water[t];
        if ((w & (WaterFlags.Sea | WaterFlags.Lake)) != 0) return -1;
        int c = 100 + Math.Min((int)_r.SlopePermille[t], 300) * 2 + _r.Forest[t] / 5;
        if (_r.Built[t] != 0) c += 400;
        if (_r.Biomes[t] == Biome.Marsh) c += 120;
        if (_r.Biomes[t] == Biome.Mountain) c += 100;
        if ((w & WaterFlags.River) != 0) c += 4000;
        else if ((w & WaterFlags.Stream) != 0) c += 600;
        byte rc = _r.Road[t];
        if (rc >= (byte)RoadClass.Road) c = Math.Min(c, 55);
        else if (rc == (byte)RoadClass.Track || rc == (byte)RoadClass.Lane) c = Math.Min(c, 75);
        return c;
    }

    /// <summary>Weighted A* over tiles (octile steps, terrain cost); returns the tile path from a to b or null.</summary>
    private List<int>? FindPath(int a, int b, int pad)
    {
        if (_g.Length == 0) { _g = new int[Tiles]; _stamp = new int[Tiles]; _from = new int[Tiles]; }
        int ax = a % N, ay = a / N, bx = b % N, by = b / N;
        int x0 = Math.Max(0, Math.Min(ax, bx) - pad), x1 = Math.Min(N - 1, Math.Max(ax, bx) + pad);
        int y0 = Math.Max(0, Math.Min(ay, by) - pad), y1 = Math.Min(N - 1, Math.Max(ay, by) + pad);
        _stampNo++;
        var heap = new Heap();
        _g[a] = 0; _stamp[a] = _stampNo; _from[a] = -1;
        int H(int x, int y) { int dx = Math.Abs(x - bx), dy = Math.Abs(y - by); return 10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy); }
        heap.Push(((long)H(ax, ay) << 20) | (uint)a);
        while (heap.Count > 0)
        {
            long k = heap.Pop();
            int cur = (int)(k & 0xFFFFF);
            int cx = cur % N, cy = cur / N;
            int f = (int)(k >> 20);
            if (cur == b) break;
            if (f > _g[cur] + H(cx, cy)) continue; // stale
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + Dx8[d], ny = cy + Dy8[d];
                if (nx < x0 || nx > x1 || ny < y0 || ny > y1) continue;
                int n = ny * N + nx;
                int c = n == b ? 100 : TileCost(n);
                if (c < 0) continue;
                if ((d & 1) == 1)
                {
                    // No sneaking diagonally between the tiles of a diagonal river: a crossing must enter a water tile.
                    const byte chan = WaterFlags.River | WaterFlags.Stream;
                    if ((_r.Water[cy * N + nx] & chan) != 0 && (_r.Water[ny * N + cx] & chan) != 0) continue;
                }
                int step = (d & 1) == 0 ? 10 : 14;
                int ng = _g[cur] + step * c / 100;
                if (_stamp[n] == _stampNo && _g[n] <= ng) continue;
                _stamp[n] = _stampNo; _g[n] = ng; _from[n] = cur;
                heap.Push(((long)(ng + H(nx, ny)) << 20) | (uint)n);
            }
        }
        if (_stamp[b] != _stampNo) return null;
        var path = new List<int>();
        for (int t = b; t >= 0; t = _from[t]) { path.Add(t); if (t == a) break; }
        path.Reverse();
        return path;
    }

    private static int ClassWidthCm(RoadClass c) => c switch { RoadClass.Highway => 600, RoadClass.Road => 500, RoadClass.Track => 350, _ => 250 };

    private void BuildRoads()
    {
        var r = _r;
        var S = _r.Settlements;
        for (int i = 0; i < S.Count; i++)
        {
            var kind = S[i].Class == SettlementClass.Town ? RoadNodeKind.Town : S[i].Class == SettlementClass.Village ? RoadNodeKind.Village : RoadNodeKind.Farmstead;
            _r.RoadNodes.Add(new RoadNode(i, kind, i, S[i].CenterX, S[i].CenterY));
            S[i].RoadNode = i;
        }
        // Border gates from the continental road network (BorderGates): the same tile row or column and class on both sides of every edge.
        // Each gate is a node on the edge tile, a straight 200 m segment inward along the route's bearing, then a road on to the nearest town.
        var gates = new List<int>();
        var gateInner = new Dictionary<int, (int Inner, RoadClass Class)>();
        foreach (var g in BorderGates.For(_h).Of(RegionCoord.OfCell(_cx, _cy)))
        {
            r.Gates.Add(g);
            int gx, gy, ix, iy;
            int along = TileCenter(g.Tile);
            switch (g.Side)
            {
                // on the border line itself: the neighbour's gate node is the same point seen from its side, so the road is one road across the edge
                case BorderSide.West: gx = 0; gy = along; break;
                case BorderSide.East: gx = Region.Meters; gy = along; break;
                case BorderSide.North: gx = along; gy = 0; break;
                default: gx = along; gy = Region.Meters; break;
            }
            // inward: opposite to the bearing of the road leaving through the gate
            ix = gx - g.BearingX * BorderGates.SegmentM / 1024; iy = gy - g.BearingY * BorderGates.SegmentM / 1024;
            ix = Math.Clamp(ix, 0, Region.Meters); iy = Math.Clamp(iy, 0, Region.Meters);
            int id = _r.RoadNodes.Count;
            _r.RoadNodes.Add(new RoadNode(id, RoadNodeKind.Gate, -1, gx, gy));
            _r.RoadNodes.Add(new RoadNode(id + 1, RoadNodeKind.Gate, -1, ix, iy));
            gates.Add(id); gates.Add(id + 1);
            gateInner[id] = (id + 1, g.Class);
        }

        // Relative neighbourhood graph over towns and villages.
        var core = new List<int>();
        for (int i = 0; i < S.Count; i++) if (S[i].Class != SettlementClass.Farmstead) core.Add(i);
        int n = core.Count;
        var d = new long[n, n];
        for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
                d[a, b] = Dist2(S[core[a]].CenterX, S[core[a]].CenterY, S[core[b]].CenterX, S[core[b]].CenterY);
        var edges = new List<(int A, int B)>();
        var adj = new bool[n, n];
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
            {
                bool ok = true;
                for (int c = 0; c < n && ok; c++)
                    if (c != a && c != b && Math.Max(d[a, c], d[b, c]) < d[a, b]) ok = false;
                if (ok) { edges.Add((a, b)); adj[a, b] = adj[b, a] = true; }
            }
        // Towns link to each other directly (highways) even when a village lies between them.
        var townIdx = new List<int>();
        for (int a = 0; a < n; a++) if (S[core[a]].Class == SettlementClass.Town) townIdx.Add(a);
        for (int x = 0; x < townIdx.Count; x++)
            for (int y = x + 1; y < townIdx.Count; y++)
            {
                int a = townIdx[x], b = townIdx[y];
                if (adj[a, b]) continue;
                bool ok = true;
                foreach (int c in townIdx) if (c != a && c != b && Math.Max(d[a, c], d[b, c]) < d[a, b]) ok = false;
                if (ok) { edges.Add((a, b)); adj[a, b] = adj[b, a] = true; }
            }
        // Loops: add short shortcuts that save a long detour, so the network is a mesh and not a tree.
        var path = ShortestAll(n, edges, d);
        int loopTarget = Math.Max(4, n / 7);
        var extra = new List<(long D, int A, int B)>();
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
                if (!adj[a, b] && d[a, b] < 6500L * 6500) extra.Add((d[a, b], a, b));
        extra.Sort((p, q) => p.D != q.D ? p.D.CompareTo(q.D) : p.A != q.A ? p.A.CompareTo(q.A) : p.B.CompareTo(q.B));
        int added = 0;
        foreach (var (dd, a, b) in extra)
        {
            if (added >= loopTarget) break;
            long straight = Region.Isqrt(dd);
            if (path[a, b] < straight * 17 / 10 + 1500 && added >= 1) continue; // a loop is only worth it when the detour is long
            edges.Add((a, b)); adj[a, b] = adj[b, a] = true; added++;
            path[a, b] = path[b, a] = straight;
            for (int p = 0; p < n; p++)
                for (int q = 0; q < n; q++)
                {
                    long via = Math.Min(path[p, a] + straight + path[b, q], path[p, b] + straight + path[a, q]);
                    if (via < path[p, q]) path[p, q] = via;
                }
        }

        var work = new List<(RoadClass C, long D, int NodeA, int NodeB)>();
        foreach (var (a, b) in edges)
        {
            var sa = S[core[a]]; var sb = S[core[b]];
            var cls = sa.Class == SettlementClass.Town && sb.Class == SettlementClass.Town ? RoadClass.Highway
                : sa.Class == SettlementClass.Town || sb.Class == SettlementClass.Town ? RoadClass.Road : RoadClass.Track;
            work.Add((cls, d[a, b], core[a], core[b]));
        }
        foreach (var (g, (inner, cls)) in gateInner)
        {
            work.Add((cls, 0, g, inner)); // the straight segment from the edge tile
            int best = -1; long bd = long.MaxValue;
            foreach (int a in townIdx.Count > 0 ? townIdx : Enumerable.Range(0, n))
            {
                long dd = Dist2(_r.RoadNodes[inner].X, _r.RoadNodes[inner].Y, S[core[a]].CenterX, S[core[a]].CenterY);
                if (dd < bd) { bd = dd; best = a; }
            }
            if (best >= 0) work.Add((cls, bd, inner, core[best]));
        }
        work.Sort((p, q) => p.C != q.C ? ((int)q.C).CompareTo((int)p.C) : p.D != q.D ? p.D.CompareTo(q.D) : p.NodeA != q.NodeA ? p.NodeA.CompareTo(q.NodeA) : p.NodeB.CompareTo(q.NodeB));
        var connected = new int[_r.RoadNodes.Count];
        for (int i = 0; i < connected.Length; i++) connected[i] = i;
        int Find(int x) { while (connected[x] != x) { connected[x] = connected[connected[x]]; x = connected[x]; } return x; }
        foreach (var w in work) if (AddRoad(w.C, w.NodeA, w.NodeB, 45)) connected[Find(w.NodeA)] = Find(w.NodeB);

        // Make sure the core graph is one component (a failed A* in a window may have left islands).
        for (int round = 0; round < 6; round++)
        {
            var nodes = new List<int>(core); nodes.AddRange(gates);
            var roots = nodes.Select(Find).Distinct().ToList();
            if (roots.Count <= 1) break;
            int main = Find(nodes[0]);
            foreach (int node in nodes)
            {
                if (Find(node) == main) continue;
                int best = -1; long bd = long.MaxValue;
                foreach (int o in nodes)
                {
                    if (Find(o) != main) continue;
                    long dd = Dist2(_r.RoadNodes[node].X, _r.RoadNodes[node].Y, _r.RoadNodes[o].X, _r.RoadNodes[o].Y);
                    if (dd < bd) { bd = dd; best = o; }
                }
                if (best >= 0 && AddRoad(RoadClass.Track, node, best, 200)) connected[Find(node)] = Find(best);
            }
        }

        // Lanes link every farmstead to the nearest village or town.
        for (int i = 0; i < S.Count; i++)
        {
            if (S[i].Class != SettlementClass.Farmstead) continue;
            int best = -1; long bd = long.MaxValue;
            foreach (int c in core)
            {
                long dd = Dist2(S[i].CenterX, S[i].CenterY, S[c].CenterX, S[c].CenterY);
                if (dd < bd) { bd = dd; best = c; }
            }
            if (best >= 0 && !AddRoad(RoadClass.Lane, i, best, 40)) AddRoad(RoadClass.Lane, i, best, 250);
        }
    }

    private bool CellInside(int cell)
    {
        int x = cell % Continent.Size - _cx, y = cell / Continent.Size - _cy;
        return x >= 0 && x < 16 && y >= 0 && y < 16;
    }

    private static long[,] ShortestAll(int n, List<(int A, int B)> edges, long[,] d2)
    {
        var p = new long[n, n];
        for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) p[a, b] = a == b ? 0 : long.MaxValue / 4;
        foreach (var (a, b) in edges) { long w = Region.Isqrt(d2[a, b]); p[a, b] = p[b, a] = w; }
        for (int k = 0; k < n; k++)
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                    if (p[a, k] + p[k, b] < p[a, b]) p[a, b] = p[a, k] + p[k, b];
        return p;
    }

    /// <summary>Roads end where they meet the outermost street of a settlement; the plan's streets carry the traffic from there to the square.</summary>
    private void TrimToSettlement(List<PointM> pts, RoadNode node, bool atStart)
    {
        if (node.Settlement < 0 || pts.Count < 2) return;
        var s = _r.Settlements[node.Settlement];
        if (s.Class == SettlementClass.Farmstead) return;
        int r = s.Plan.RadiusM + (s.Class == SettlementClass.Town ? 30 : s.Class == SettlementClass.Village ? 24 : 6);
        if (!atStart) pts.Reverse();
        int cut = 0;
        while (cut < pts.Count - 1 && Dist(pts[cut].X, pts[cut].Y, node.X, node.Y) < r) cut++;
        if (cut > 0 && cut < pts.Count)
        {
            var a = pts[cut - 1]; var b = pts[cut];
            int da = Dist(a.X, a.Y, node.X, node.Y), db = Dist(b.X, b.Y, node.X, node.Y);
            int t1000 = db == da ? 0 : Math.Clamp((r - da) * 1000 / (db - da), 0, 1000);
            var p = new PointM(a.X + (b.X - a.X) * t1000 / 1000, a.Y + (b.Y - a.Y) * t1000 / 1000);
            pts.RemoveRange(0, cut);
            pts[0] = p;
        }
        if (!atStart) pts.Reverse();
    }

    private bool AddRoad(RoadClass cls, int nodeA, int nodeB, int pad)
    {
        var A = _r.RoadNodes[nodeA]; var B = _r.RoadNodes[nodeB];
        int ta = Idx(TileOf(A.X), TileOf(A.Y)), tb = Idx(TileOf(B.X), TileOf(B.Y));
        var path = FindPath(ta, tb, pad) ?? FindPath(ta, tb, N);
        if (path == null) return false;
        var edge = new RoadEdge { Id = _r.RoadEdges.Count, A = nodeA, B = nodeB, Class = cls, WidthCm = ClassWidthCm(cls) };
        // Simplify: keep only vertices where the step direction changes.
        var pts = new List<PointM> { new(A.X, A.Y) };
        for (int i = 1; i < path.Count - 1; i++)
        {
            int px = path[i - 1] % N, py = path[i - 1] / N, cx = path[i] % N, cy = path[i] / N, nx = path[i + 1] % N, ny = path[i + 1] / N;
            if (cx - px != nx - cx || cy - py != ny - cy) pts.Add(new PointM(TileCenter(cx), TileCenter(cy)));
        }
        pts.Add(new PointM(B.X, B.Y));
        var trimmed = new List<PointM>(pts);
        TrimToSettlement(trimmed, A, true);
        TrimToSettlement(trimmed, B, false);
        if (trimmed.Count >= 2 && Dist(trimmed[0].X, trimmed[0].Y, trimmed[^1].X, trimmed[^1].Y) > 60) pts = trimmed;
        edge.Points = [.. pts];
        int len = 0;
        for (int i = 1; i < pts.Count; i++) len += Dist(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y);
        // The polyline stops at the settlements' outer streets; the way on to each node centre (along the plan's streets) counts towards the length.
        len += Dist(A.X, A.Y, pts[0].X, pts[0].Y) + Dist(B.X, B.Y, pts[^1].X, pts[^1].Y);
        edge.LengthM = len;
        // Crossings: runs of river or stream tiles along the path.
        for (int i = 0; i < path.Count;)
        {
            int t = path[i];
            if ((_r.Water[t] & (WaterFlags.River | WaterFlags.Stream)) == 0) { i++; continue; }
            int j = i; bool river = false;
            while (j < path.Count && (_r.Water[path[j]] & (WaterFlags.River | WaterFlags.Stream)) != 0) { river |= (_r.Water[path[j]] & WaterFlags.River) != 0; j++; }
            int mid = path[(i + j - 1) / 2];
            long acc = _r.Catchment[mid];
            int width = WidthCm(river, acc, _streamTiles, _riverTiles);
            CrossingKind kind;
            if (river) kind = cls >= RoadClass.Road || width >= 2200 ? CrossingKind.Bridge : CrossingKind.Ford;
            else kind = cls == RoadClass.Highway ? CrossingKind.Bridge : CrossingKind.Ford;
            edge.Crossings.Add(new RoadCrossing(new PointM(TileCenter(mid % N), TileCenter(mid / N)), kind, WaterwayAt[mid], width));
            i = j;
        }
        foreach (int t in path) if (_r.Road[t] < (byte)cls && _r.Built[t] == 0) _r.Road[t] = (byte)cls;
        _r.RoadEdges.Add(edge);
        return true;
    }
}
