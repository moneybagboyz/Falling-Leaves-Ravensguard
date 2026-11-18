using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.Region;

/// <summary>
/// Continental rivers drawn the same way by every region (seamless-regions spec 2.3 "Rivers"). A continental flow step (cell to the cell it drains into)
/// whose flow reaches the continent's stream threshold becomes a tile path between the two cell centres, found by a terrain-following search over heights that are
/// a pure function of absolute position, with a width taken from the step's flow. Any region whose window the path touches rasterises the identical tiles, so a river
/// crossing an edge leaves one region and enters the next at the same place with the same width and depth, whichever region is generated first.
/// The local flow network is bent to follow these paths; streams smaller than that stay local and are cut at the edge tiles.
/// </summary>
internal sealed partial class RegionBuilder
{
    /// <summary>Tiles around the two cell centres a river path may wander.</summary>
    private const int ChainPad = 20;
    private const int AccPerCell = 976; // tiles in a 2 km cell, as InjectInflow uses
    private const int ChainMinCells = 3;

    // For each tile of the extended window: the next/previous tile along a continental river (extended index), -1 for none.
    private int[] _eChainNext = [];
    private int[] _eChainPrev = [];
    // Accumulation (in tiles) the river carries at a chain tile, 0 for a tile that is not on a continental river.
    private int[] _eChainAcc = [];
    private Dictionary<long, int[]>? _blocks;
    private const int BlockT = 32;

    /// <summary>Height of any global tile: the window's own array when inside it, else a cached pure block.</summary>
    private int HeightAtGlobal(int gtx, int gty)
    {
        int ex = gtx - (_ox / TileM - Mg), ey = gty - (_oy / TileM - Mg);
        if ((uint)ex < E && (uint)ey < E) return _eH[ey * E + ex];
        _blocks ??= [];
        int bx = FloorDiv(gtx, BlockT), by = FloorDiv(gty, BlockT);
        long key = ((long)bx << 32) | (uint)by;
        if (!_blocks.TryGetValue(key, out var blk))
        {
            blk = new int[BlockT * BlockT];
            TerrainBlock(bx * BlockT * TileM, by * BlockT * TileM, BlockT, BlockT, blk, null, null, null);
            _blocks[key] = blk;
        }
        return blk[(gty - by * BlockT) * BlockT + (gtx - bx * BlockT)];
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Continent, int[]> UpstreamCache = new();

    /// <summary>Cells draining through each continental cell (itself included), by the continent's own D8 flow. Computed once per continent.</summary>
    private static int[] UpstreamCells(Continent c)
    {
        lock (UpstreamCache)
        {
            if (UpstreamCache.TryGetValue(c, out var have)) return have;
            var cnt = new int[Continent.Cells]; var deg = new int[Continent.Cells];
            for (int i = 0; i < Continent.Cells; i++) { cnt[i] = 1; if (!c.Ocean[i] && c.FlowTo[i] >= 0) deg[c.FlowTo[i]]++; }
            var q = new Queue<int>();
            for (int i = 0; i < Continent.Cells; i++) if (deg[i] == 0) q.Enqueue(i);
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                if (c.Ocean[i] || c.FlowTo[i] < 0) continue;
                int t = c.FlowTo[i];
                cnt[t] += cnt[i];
                if (--deg[t] == 0) q.Enqueue(t);
            }
            UpstreamCache.Add(c, cnt);
            return cnt;
        }
    }

    /// <summary>Accumulation a continental cell carries in region tiles: the cells draining through it times the tiles of a cell, so it is comparable to the local flow accumulation.</summary>
    private int CellAcc(int cell)
    {
        if (_c.Ocean[cell]) return 0;
        return (int)Math.Min(int.MaxValue / 4, (long)UpstreamCells(_c)[cell] * AccPerCell);
    }

    /// <summary>The tile path of one continental flow step, in global tile coordinates, cut at the first sea tile. A pure function of the cell pair.</summary>
    private List<(int X, int Y)> StepPath(int c, int d)
    {
        int ax = (c % Continent.Size * RegionSettings.CellM + RegionSettings.CellM / 2) / TileM, ay = (c / Continent.Size * RegionSettings.CellM + RegionSettings.CellM / 2) / TileM;
        int bx = (d % Continent.Size * RegionSettings.CellM + RegionSettings.CellM / 2) / TileM, by = (d / Continent.Size * RegionSettings.CellM + RegionSettings.CellM / 2) / TileM;
        int x0 = Math.Min(ax, bx) - ChainPad, y0 = Math.Min(ay, by) - ChainPad;
        int w = Math.Abs(ax - bx) + 2 * ChainPad + 1, h = Math.Abs(ay - by) + 2 * ChainPad + 1;
        var hs = new int[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) hs[y * w + x] = HeightAtGlobal(x0 + x, y0 + y);
        int href = Math.Min(hs[(ay - y0) * w + ax - x0], hs[(by - y0) * w + bx - x0]);
        int Cost(int i) => 100 + Math.Clamp((hs[i] - href) / 12, 0, 600) + (hs[i] <= 0 ? 0 : 0);
        var g = new int[w * h]; var from = new int[w * h]; var seen = new bool[w * h];
        Array.Fill(g, int.MaxValue); Array.Fill(from, -1);
        int s = (ay - y0) * w + ax - x0, t = (by - y0) * w + bx - x0;
        var heap = new Heap();
        g[s] = 0;
        int H(int i) { int dx = Math.Abs(i % w - (t % w)), dy = Math.Abs(i / w - (t / w)); return 10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy); }
        heap.Push(((long)H(s) << 24) | (uint)s);
        while (heap.Count > 0)
        {
            long k = heap.Pop();
            int cur = (int)(k & 0xFFFFFF);
            if (seen[cur]) continue;
            seen[cur] = true;
            if (cur == t) break;
            int cx = cur % w, cy = cur / w;
            for (int dd = 0; dd < 8; dd++)
            {
                int nx = cx + Dx8[dd], ny = cy + Dy8[dd];
                if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                int n = ny * w + nx;
                if (seen[n]) continue;
                int ng = g[cur] + ((dd & 1) == 0 ? 10 : 14) * Cost(n) / 100;
                if (ng < g[n]) { g[n] = ng; from[n] = cur; heap.Push(((long)(ng + H(n)) << 24) | (uint)n); }
            }
        }
        var path = new List<(int X, int Y)>();
        for (int i = t; i >= 0; i = from[i]) { path.Add((x0 + i % w, y0 + i / w)); if (i == s) break; }
        path.Reverse();
        // the river ends where it reaches the sea
        for (int i = 0; i < path.Count; i++) if (HeightAtGlobal(path[i].X, path[i].Y) <= 0) { path.RemoveRange(i + 1, path.Count - i - 1); break; }
        return path;
    }

    /// <summary>Lays every continental river step that touches the extended window onto <see cref="_eChainNext"/>, <see cref="_eChainPrev"/> and <see cref="_eChainAcc"/>.</summary>
    private void BuildChains()
    {
        int A = E * E;
        _eChainNext = new int[A]; _eChainPrev = new int[A]; _eChainAcc = new int[A];
        Array.Fill(_eChainNext, -1); Array.Fill(_eChainPrev, -1);
        int wx0 = _ox / TileM - Mg, wy0 = _oy / TileM - Mg;
        // every cell with a few cells draining through it is drawn from the continent (the river network the regions share); smaller streams stay local
        int thr = ChainMinCells * AccPerCell;
        // Steps in ascending cell index, the same order whichever region asks, so where two paths meet the first one laid wins everywhere.
        for (int ccy = Math.Max(0, _cy - 2); ccy <= Math.Min(Continent.Size - 1, _cy + 17); ccy++)
            for (int ccx = Math.Max(0, _cx - 2); ccx <= Math.Min(Continent.Size - 1, _cx + 17); ccx++)
            {
                int c = Continent.Index(ccx, ccy);
                if (_c.Ocean[c] || CellAcc(c) < thr) continue;
                int d = _c.FlowTo[c];
                if (d < 0) continue;
                int accC = CellAcc(c), accD = _c.Ocean[d] ? accC : CellAcc(d);
                int stepAcc = (int)(((long)accC + Math.Max(accC, accD)) / 2);
                var path = StepPath(c, d);
                int prevE = -1;
                for (int i = 0; i < path.Count; i++)
                {
                    int ex = path[i].X - wx0, ey = path[i].Y - wy0;
                    int e = (uint)ex < E && (uint)ey < E ? ey * E + ex : -1;
                    if (e < 0) { prevE = -1; continue; }
                    if (i == 0 && _eChainNext[e] >= 0) break; // starts in the middle of another river: it flows on as that one
                    _eChainAcc[e] = Math.Max(_eChainAcc[e], stepAcc); // a tile carries the most of any step through it
                    if (prevE >= 0)
                    {
                        LinkChain(prevE, e);
                        if (_eChainNext[e] >= 0) break; // joins a river already laid: merged
                    }
                    prevE = e;
                }
            }
        RaiseChainAccDownstream();
    }

    /// <summary>Makes the river's accumulation never fall downstream (where a small tributary laid a tile first and a bigger river merged into it).</summary>
    private void RaiseChainAccDownstream()
    {
        int A = E * E;
        var deg = new int[A];
        for (int e = 0; e < A; e++) if (_eChainNext[e] >= 0) deg[_eChainNext[e]]++;
        var q = new Queue<int>();
        for (int e = 0; e < A; e++) if (_eChainNext[e] >= 0 && deg[e] == 0) q.Enqueue(e);
        while (q.Count > 0)
        {
            int e = q.Dequeue(), n = _eChainNext[e];
            if (_eChainAcc[n] < _eChainAcc[e]) _eChainAcc[n] = _eChainAcc[e];
            if (--deg[n] == 0 && _eChainNext[n] >= 0) q.Enqueue(n);
        }
    }

    private void LinkChain(int from, int to)
    {
        _eChainNext[from] = to;
        if (_eChainPrev[to] < 0) _eChainPrev[to] = from;
    }

    /// <summary>Bends the local flow network to the continental rivers.</summary>
    private void ApplyChainsToFlow()
    {
        int A = E * E;
        for (int e = 0; e < A; e++)
        {
            int nx = _eChainNext[e];
            if (nx >= 0 && !_eSea[e]) _eRecv[e] = nx;
        }
    }

    /// <summary>
    /// Flow accumulation over the receiver graph in topological order (the receivers were bent, so the priority-flood order no longer holds); tiles on a
    /// continental river then take the accumulation of their step, a pure function of the cell pair. A loop in the bent graph is cut where it closes.
    /// </summary>
    private void AccumulateFlow()
    {
        int A = E * E;
        var indeg = new int[A];
        for (int e = 0; e < A; e++) if (_eRecv[e] >= 0 && !_eSea[e]) indeg[_eRecv[e]]++;
        var queue = new int[A];
        for (int round = 0; round < 8; round++)
        {
            int qh = 0, qt = 0;
            var deg = (int[])indeg.Clone();
            var done = new bool[A];
            var acc = (int[])_eAcc.Clone();
            for (int e = 0; e < A; e++) if (deg[e] == 0) queue[qt++] = e;
            while (qh < qt)
            {
                int e = queue[qh++]; done[e] = true;
                int r = _eRecv[e];
                if (r < 0 || _eSea[e]) continue;
                acc[r] += acc[e];
                if (--deg[r] == 0) queue[qt++] = r;
            }
            if (qt == A) { Array.Copy(acc, _eAcc, A); break; }
            // cut every loop: walk from a tile left over until a tile repeats and drop that tile's receiver
            for (int e = 0; e < A; e++)
            {
                if (done[e]) continue;
                int cur = e, guard = 0;
                var seenAt = new HashSet<int>();
                while (cur >= 0 && !done[cur] && seenAt.Add(cur) && guard++ < A) cur = _eRecv[cur];
                if (cur >= 0 && !done[cur] && seenAt.Contains(cur))
                {
                    int tail = cur; int p = _eRecv[tail];
                    // drop the link out of the tile that closed the loop
                    _eChainNext[tail] = -1; if (p >= 0 && _eChainPrev[p] == tail) _eChainPrev[p] = -1;
                    indeg[p]--; _eRecv[tail] = -1;
                    break;
                }
            }
        }
        for (int e = 0; e < A; e++) if (_eChainAcc[e] > 0 && !_eSea[e]) _eAcc[e] = _eChainAcc[e];
    }

    /// <summary>True when a region tile lies on a continental river.</summary>
    private bool OnChain(int regionTile) => _eChainAcc[(regionTile / N + Mg) * E + regionTile % N + Mg] > 0;

    /// <summary>
    /// Where the continental river through a region tile on an edge meets the shared border: the midpoint of this tile's centre and the centre of the chain tile
    /// across the edge (in region metres, on the border line). Both regions compute the same point from the same pair of tiles. Null when the tile is not on an edge
    /// or the river does not cross it. <paramref name="downstream"/> picks the next tile of the chain, else the previous one.
    /// </summary>
    private PointM? BorderPoint(int regionTile, bool downstream)
    {
        int x = regionTile % N, y = regionTile / N;
        if (x != 0 && y != 0 && x != N - 1 && y != N - 1) return null;
        int e = (y + Mg) * E + x + Mg;
        int o = downstream ? _eChainNext[e] : _eChainPrev[e];
        if (o < 0) return null;
        int ox = o % E - Mg, oy = o / E - Mg;
        if ((uint)ox < N && (uint)oy < N) return null;
        return new PointM((TileCenter(x) + TileCenter(ox)) / 2, (TileCenter(y) + TileCenter(oy)) / 2);
    }
}
