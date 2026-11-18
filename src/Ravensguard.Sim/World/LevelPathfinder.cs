namespace Ravensguard.Sim.World;

public sealed class PathResult
{
    public List<Cell3> Cells { get; init; } = new();
    public int Cost { get; init; }
    public int Expanded { get; init; }
    public List<int> PortalsUsed { get; init; } = new();
}

/// <summary>
/// A* over cells + portals (8-way, no corner cutting, integer costs, deterministic tie-break on cell index).
/// FindPath is exact. FindPathAbstract first plans on the chunk-zone graph (HPA*-style) and restricts the cell search to the corridor.
/// </summary>
public sealed class LevelPathfinder
{
    private LevelGrid _g;
    private readonly int[] _gs, _parent, _seen, _closed, _viaPortal;
    private int _cur;
    /// <summary>
    /// The search arrays are stamped per search and shared by every call, so two searches at once on one pathfinder corrupt each other's parent chains (a cycle there made
    /// <see cref="Build"/> grow its list until the process ran out of memory: Phase 3 cleanup, a shared test ground walked from two test classes at once). Every entry point takes this lock;
    /// the live tick calls from one thread, so it is never contended there.
    /// </summary>
    private readonly object _gate = new();
    private static readonly (int dx, int dy)[] Dirs = { (0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1) };

    // Zone abstraction. Zone ids are slots: a chunk-level that is re-zoned gives its old ids back and reuses them, so the
    // slot count is bounded by the peak number of live zones, not by the number of rebuilds.
    private readonly int[] _zoneOf;
    private readonly List<(int cx, int cy, int n)> _zoneInfo = new();
    private readonly List<List<(int to, int cost)>> _zoneEdges = new();
    private readonly List<int>?[] _chunkZones;       // chunk-level -> its live zone ids
    private readonly Stack<int> _freeZones = new();
    private readonly int[] _seenRev;                 // this pathfinder's last-seen revision per chunk-level (-1 = never)
    private int _liveZones;
    private int _abstractRevision = -1;
    // pooled ZonePath buffers (sized to the slot count)
    private int[] _best = [], _par = [];
    private bool[] _pathBuf = [], _ringBuf = [];
    private readonly PriorityQueue<int, long> _zoneOpen = new();
    public int LastZonesRebuilt { get; private set; }
    /// <summary>Live zones.</summary>
    public int ZoneCount => _liveZones;
    /// <summary>Allocated zone id slots (live + free); stays bounded across rebuilds.</summary>
    public int ZoneSlots => _zoneInfo.Count;

    public LevelPathfinder(LevelGrid g)
    {
        _g = g;
        int n = g.CellCount;
        _gs = new int[n]; _parent = new int[n]; _seen = new int[n]; _closed = new int[n]; _viaPortal = new int[n];
        _zoneOf = new int[n];
        Array.Fill(_zoneOf, -1);
        _chunkZones = new List<int>?[g.ChunkLevelCount];
        _seenRev = new int[g.ChunkLevelCount];
        Array.Fill(_seenRev, -1);
    }

    /// <summary>
    /// Points the pathfinder at another grid of the same size (the live window moved) instead of allocating new per-cell arrays (about 11 MB for a 7 x 7 chunk window).
    /// The search arrays stay (they are stamped per search); the chunk-zone abstraction is forgotten and rebuilt on demand.
    /// </summary>
    public void Rebind(LevelGrid g) { lock (_gate) RebindCore(g); }

    private void RebindCore(LevelGrid g)
    {
        if (g.Width != _g.Width || g.Height != _g.Height || g.CellCount != _g.CellCount || g.ChunkLevelCount != _g.ChunkLevelCount)
            throw new ArgumentException("Rebind needs a grid of the same size.", nameof(g));
        _g = g;
        if (_abstractRevision != -1 || _zoneInfo.Count > 0)
        {
            Array.Fill(_zoneOf, -1);
            _zoneInfo.Clear(); _zoneEdges.Clear(); _freeZones.Clear();
            Array.Clear(_chunkZones);
            Array.Fill(_seenRev, -1);
            _liveZones = 0; _abstractRevision = -1;
        }
    }

    private static int Octile(int dx, int dy)
    {
        dx = Math.Abs(dx); dy = Math.Abs(dy);
        return LevelGrid.Straight * Math.Max(dx, dy) + (LevelGrid.Diagonal - LevelGrid.Straight) * Math.Min(dx, dy);
    }

    public PathResult? FindPath(Cell3 from, Cell3 to) { lock (_gate) return Search(from, to, null, int.MaxValue); }

    /// <summary>
    /// <see cref="FindPath"/> that gives up (null) after <paramref name="maxExpanded"/> cells: the search is a pure function of the grid, so the cutoff is deterministic. For the many small routes of
    /// the live people, where a goal that cannot be reached must not flood the whole window.
    /// </summary>
    public PathResult? FindPathBounded(Cell3 from, Cell3 to, int maxExpanded) { lock (_gate) return Search(from, to, null, maxExpanded); }

    public PathResult? FindPathAbstract(Cell3 from, Cell3 to) { lock (_gate) return FindPathAbstractCore(from, to); }

    private PathResult? FindPathAbstractCore(Cell3 from, Cell3 to)
    {
        if (!_g.InBounds(from.X, from.Y, from.Z) || !_g.InBounds(to.X, to.Y, to.Z)) return null;
        RefreshAbstraction();
        int zs = _zoneOf[_g.Index(from.X, from.Y, from.Z)], zg = _zoneOf[_g.Index(to.X, to.Y, to.Z)];
        if (zs < 0 || zg < 0) return null;
        var corridor = ZonePath(zs, zg);
        if (corridor == null) return null; // quick reject: provably unreachable
        return Search(from, to, corridor, int.MaxValue);
    }

    /// <summary>Reachability from the zone graph alone, no cell search.</summary>
    public bool IsReachable(Cell3 from, Cell3 to) { lock (_gate) return IsReachableCore(from, to); }

    private bool IsReachableCore(Cell3 from, Cell3 to)
    {
        if (!_g.InBounds(from.X, from.Y, from.Z) || !_g.InBounds(to.X, to.Y, to.Z)) return false;
        RefreshAbstraction();
        int zs = _zoneOf[_g.Index(from.X, from.Y, from.Z)], zg = _zoneOf[_g.Index(to.X, to.Y, to.Z)];
        return zs >= 0 && zg >= 0 && ZonePath(zs, zg) != null;
    }

    private PathResult? Search(Cell3 from, Cell3 to, bool[]? allowedZones, int maxExpanded)
    {
        if (!_g.InBounds(from.X, from.Y, from.Z) || !_g.InBounds(to.X, to.Y, to.Z)) return null;
        int start = _g.Index(from.X, from.Y, from.Z), goal = _g.Index(to.X, to.Y, to.Z);
        if (!_g.IsOpen(start) || !_g.IsOpen(goal)) return null;
        _cur++;
        int W = _g.Width, H = _g.Height;
        var open = new PriorityQueue<int, long>();
        _gs[start] = 0; _parent[start] = -1; _seen[start] = _cur; _viaPortal[start] = -1;
        open.Enqueue(start, Pack(Octile(from.X - to.X, from.Y - to.Y), start));
        int expanded = 0;
        while (open.TryDequeue(out int cur, out _))
        {
            if (_closed[cur] == _cur) continue;
            _closed[cur] = _cur;
            expanded++;
            if (cur == goal) return Build(goal, expanded);
            if (expanded > maxExpanded) return null;
            int cx = cur % W; int r = cur / W; int cy = r % H; int cz = r / H + _g.MinZ;
            int g0 = _gs[cur];
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + Dirs[d].dx, ny = cy + Dirs[d].dy;
                if ((uint)nx >= (uint)W || (uint)ny >= (uint)H) continue;
                int ni = cur + Dirs[d].dx + Dirs[d].dy * W;
                if (!_g.IsOpen(ni)) continue;
                int step = LevelGrid.Straight;
                if (d >= 4)
                {
                    if (!_g.IsOpen(cur + Dirs[d].dx) || !_g.IsOpen(cur + Dirs[d].dy * W)) continue;
                    step = LevelGrid.Diagonal;
                }
                Relax(cur, ni, g0 + step, -1, to, allowedZones, open);
            }
            foreach (int pid in _g.PortalsAt(cur))
            {
                var p = _g.Portals[pid];
                if (!p.Enabled) continue;
                var other = p.A.X == cx && p.A.Y == cy && p.A.Z == cz ? p.B : p.A;
                int ni = _g.Index(other.X, other.Y, other.Z);
                if (!_g.IsOpen(ni)) continue;
                Relax(cur, ni, g0 + p.Cost, pid, to, allowedZones, open);
            }
        }
        return null;
    }

    private void Relax(int cur, int ni, int ng, int portal, Cell3 to, bool[]? allowed, PriorityQueue<int, long> open)
    {
        if (allowed != null) { int z = _zoneOf[ni]; if (z < 0 || !allowed[z]) return; }
        if (_closed[ni] == _cur) return;
        if (_seen[ni] == _cur && _gs[ni] <= ng) return;
        _seen[ni] = _cur; _gs[ni] = ng; _parent[ni] = cur; _viaPortal[ni] = portal;
        int W = _g.Width, H = _g.Height;
        int x = ni % W; int y = ni / W % H;
        open.Enqueue(ni, Pack(ng + Octile(x - to.X, y - to.Y), ni));
    }

    private static long Pack(int f, int idx) => ((long)f << 32) | (uint)idx;

    private PathResult Build(int goal, int expanded)
    {
        var cells = new List<Cell3>(); var portals = new List<int>();
        for (int i = goal; i != -1; i = _parent[i])
        {
            if (cells.Count > _g.CellCount) throw new InvalidOperationException("The pathfinder's parent chain has a cycle (two searches ran at once on one pathfinder).");
            cells.Add(_g.FromIndex(i));
            if (_viaPortal[i] >= 0) portals.Add(_viaPortal[i]);
        }
        cells.Reverse(); portals.Reverse();
        return new PathResult { Cells = cells, Cost = _gs[goal], Expanded = expanded, PortalsUsed = portals };
    }

    // ---------------- zone abstraction ----------------

    /// <summary>Rebuilds zones only for chunk-levels whose revision changed, then the (cheap) edge list.</summary>
    public void RefreshAbstraction() { lock (_gate) RefreshAbstractionCore(); }

    private void RefreshAbstractionCore()
    {
        if (_abstractRevision == _g.Revision) return;
        int rebuilt = 0;
        for (int cl = 0; cl < _g.ChunkLevelCount; cl++)
            if (_seenRev[cl] != _g.ChunkRevision(cl)) { RebuildChunkZones(cl); _seenRev[cl] = _g.ChunkRevision(cl); rebuilt++; }
        LastZonesRebuilt = rebuilt;
        RebuildEdges();
        _abstractRevision = _g.Revision;
    }

    private void RebuildChunkZones(int cl)
    {
        const int C = ScaleConstants.ChunkCells;
        int cx = cl % _g.ChunksX; int r = cl / _g.ChunksX; int cy = r % _g.ChunksY; int z = r / _g.ChunksY + _g.MinZ;
        int x0 = cx * C, y0 = cy * C, x1 = Math.Min(x0 + C, _g.Width), y1 = Math.Min(y0 + C, _g.Height);
        // Give this chunk's old zone ids back (lowest id is reused first, so the layout is a pure function of the edit history).
        var mine = _chunkZones[cl] ??= new List<int>();
        for (int i = mine.Count - 1; i >= 0; i--) { _zoneEdges[mine[i]].Clear(); _freeZones.Push(mine[i]); _liveZones--; }
        mine.Clear();
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) _zoneOf[_g.Index(x, y, z)] = -1;
        var stack = new Stack<int>();
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = _g.Index(x, y, z);
                if (!_g.IsOpen(i) || _zoneOf[i] >= 0) continue;
                int id = _freeZones.Count > 0 ? _freeZones.Pop() : -1; long sx = 0, sy = 0; int n = 0;
                if (id < 0) { id = _zoneInfo.Count; _zoneInfo.Add(default); _zoneEdges.Add(new()); }
                mine.Add(id); _liveZones++;
                _zoneOf[i] = id; stack.Push(i);
                while (stack.Count > 0)
                {
                    int c = stack.Pop(); int px = c % _g.Width; int py = c / _g.Width % _g.Height;
                    sx += px; sy += py; n++;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = px + Dirs[d].dx, ny = py + Dirs[d].dy;
                        if (nx < x0 || nx >= x1 || ny < y0 || ny >= y1) continue;
                        int ni = _g.Index(nx, ny, z);
                        if (_g.IsOpen(ni) && _zoneOf[ni] < 0) { _zoneOf[ni] = id; stack.Push(ni); }
                    }
                }
                _zoneInfo[id] = ((int)(sx / n), (int)(sy / n), n);
            }
    }

    private void RebuildEdges()
    {
        foreach (var e in _zoneEdges) e.Clear();
        var seen = new HashSet<long>();
        void Link(int a, int b)
        {
            if (a < 0 || b < 0 || a == b) return;
            if (!seen.Add(((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b))) return;
            var ia = _zoneInfo[a]; var ib = _zoneInfo[b];
            int c = Math.Max(LevelGrid.Straight, Octile(ia.cx - ib.cx, ia.cy - ib.cy));
            _zoneEdges[a].Add((b, c)); _zoneEdges[b].Add((a, c));
        }
        const int C = ScaleConstants.ChunkCells;
        for (int z = _g.MinZ; z <= _g.MaxZ; z++)
            for (int y = 0; y < _g.Height; y++)
                for (int x = 0; x < _g.Width; x++)
                {
                    int i = _g.Index(x, y, z);
                    if (_zoneOf[i] < 0) continue;
                    if (x + 1 < _g.Width && (x + 1) % C == 0) Link(_zoneOf[i], _zoneOf[i + 1]);
                    if (y + 1 < _g.Height && (y + 1) % C == 0) Link(_zoneOf[i], _zoneOf[i + _g.Width]);
                }
        foreach (var p in _g.Portals)
            if (p.Enabled) Link(_zoneOf[_g.Index(p.A.X, p.A.Y, p.A.Z)], _zoneOf[_g.Index(p.B.X, p.B.Y, p.B.Z)]);
    }

    /// <summary>Returns the corridor (zones on the best path plus one ring) in a pooled buffer valid until the next call, or null.</summary>
    private bool[]? ZonePath(int zs, int zg)
    {
        int n = _zoneInfo.Count;
        if (_best.Length < n) { _best = new int[n]; _par = new int[n]; _pathBuf = new bool[n]; _ringBuf = new bool[n]; }
        Array.Fill(_best, int.MaxValue, 0, n);
        Array.Clear(_pathBuf, 0, n); Array.Clear(_ringBuf, 0, n);
        var open = _zoneOpen; open.Clear();
        _best[zs] = 0; _par[zs] = -1; open.Enqueue(zs, 0);
        var goal = _zoneInfo[zg];
        while (open.TryDequeue(out int z, out _))
        {
            if (z == zg)
            {
                for (int i = z; i != -1; i = _par[i]) _pathBuf[i] = true;
                // widen the corridor by one ring so the cell search can route around zone shapes
                for (int i = 0; i < n; i++)
                    if (_pathBuf[i]) { _ringBuf[i] = true; foreach (var (to, _) in _zoneEdges[i]) _ringBuf[to] = true; }
                return _ringBuf;
            }
            foreach (var (to, c) in _zoneEdges[z])
            {
                int ng = _best[z] + c;
                if (ng >= _best[to]) continue;
                _best[to] = ng; _par[to] = z;
                var ti = _zoneInfo[to];
                open.Enqueue(to, ((long)(ng + Octile(ti.cx - goal.cx, ti.cy - goal.cy)) << 32) | (uint)to);
            }
        }
        return null;
    }
}
