using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.Region;

internal sealed partial class RegionBuilder
{
    // Extended (margin) grid arrays, index (y + Mg*?)... = ey * E + ex.
    private int[] _eH = [];
    private bool[] _eSea = [];
    private int[] _eFill = [];
    private int[] _eRecv = [];
    private int[] _eAcc = [];
    private short[] _eSlope = [];
    private byte[] _eFertBase = [];
    private byte[] _eDrainBase = [];
    private int[] _eLakeW = [];

    private struct Axis
    {
        public int[] I0;   // first cell index (cell centre grid) for Catmull-Rom (taps are I0-1..I0+2)
        public int[] T;    // Q16 fraction between cells I0 and I0+1
        public int[] W;    // 4 Catmull-Rom weights per position (Q16)
    }

    /// <summary>Axis taps for <paramref name="count"/> tiles whose first tile starts at <paramref name="startM"/> continent metres.</summary>
    private static Axis BuildAxis(int startM, int count)
    {
        var a = new Axis { I0 = new int[count], T = new int[count], W = new int[count * 4] };
        for (int e = 0; e < count; e++)
        {
            int x = startM + e * TileM + TileM / 2;
            int p = x - RegionSettings.CellM / 2;
            int i0 = FloorDiv(p, RegionSettings.CellM);
            int rem = p - i0 * RegionSettings.CellM;
            long t = (long)rem * 65536 / RegionSettings.CellM;
            long t2 = t * t >> 16, t3 = t2 * t >> 16;
            a.I0[e] = i0; a.T[e] = (int)t;
            a.W[e * 4 + 0] = (int)((-t3 + 2 * t2 - t) / 2);
            a.W[e * 4 + 1] = (int)((3 * t3 - 5 * t2 + 2 * 65536) / 2);
            a.W[e * 4 + 2] = (int)((-3 * t3 + 4 * t2 + t) / 2);
            a.W[e * 4 + 3] = (int)((t3 - t2) / 2);
        }
        return a;
    }

    private static int CellClamp(int i) => Math.Clamp(i, 0, Continent.Size - 1);

    /// <summary>
    /// Terrain height (cm), the lake indicator and the smooth fertility and drainage layers for a rectangle of tiles whose north-west corner starts at
    /// (<paramref name="startXM"/>, <paramref name="startYM"/>) continent metres. Coarse heights are interpolated bicubically from the continental cells and the detail
    /// noise comes from absolute world coordinates, so the value of a tile is the same whichever region (or block) asks: this is what makes heights match across borders.
    /// </summary>
    private void TerrainBlock(int startXM, int startYM, int w, int h, int[] hOut, int[]? lakeOut, byte[]? fertOut, byte[]? drainOut)
    {
        var ax = BuildAxis(startXM, w); var ay = BuildAxis(startYM, h);
        int sea = _c.SeaLevelRaw;
        ulong ns = DetRng.Mix(_seed ^ 0xD37A11);
        int wl = Math.Max(200, _s.DetailWavelengthM);
        int oct = Math.Clamp(_s.DetailOctaves, 1, 7);
        var rowTmp = new long[4];
        for (int ey = 0; ey < h; ey++)
        {
            int j0 = ay.I0[ey];
            int Y = startYM + ey * TileM + TileM / 2;
            for (int ex = 0; ex < w; ex++)
            {
                int i0 = ax.I0[ex];
                for (int j = 0; j < 4; j++)
                {
                    int row = CellClamp(j0 - 1 + j) * Continent.Size;
                    long sum = 0;
                    for (int i = 0; i < 4; i++) sum += (long)_c.Elevation[row + CellClamp(i0 - 1 + i)] * ax.W[ex * 4 + i];
                    rowTmp[j] = sum >> 16;
                }
                long v = 0;
                for (int j = 0; j < 4; j++) v += rowTmp[j] * ay.W[ey * 4 + j];
                int raw = (int)Math.Clamp(v >> 16, 0, 65535);

                int hCm;
                if (raw <= sea) hCm = (int)((long)(raw - sea) * 3000 / Math.Max(1, sea));
                else
                {
                    long xq = (long)(raw - sea) * 65536 / (65536 - sea);
                    long q = ((xq * xq) >> 16) * 3 / 4 + xq / 4;
                    hCm = (int)(q * _s.ReliefM * 100 >> 16);
                }
                int X = startXM + ex * TileM + TileM / 2;
                if (hCm > -300)
                {
                    var n = DetNoise.Fbm(ns, Fixed.FromRatio(X, wl), Fixed.FromRatio(Y, wl), oct);
                    int amp = (int)((long)_s.DetailBaseCm * Math.Clamp(hCm, 300, 3000) / 3000) + (int)((long)Math.Max(0, hCm) * _s.DetailRelPermille / 1000);
                    hCm += (int)(((long)n.Raw - 32768) * amp >> 15);
                }
                // Bilinear taps for the smooth layers and the lake indicator.
                int t = ax.T[ex], u = ay.T[ey];
                int c00 = CellClamp(j0) * Continent.Size + CellClamp(i0), c10 = CellClamp(j0) * Continent.Size + CellClamp(i0 + 1);
                int c01 = CellClamp(j0 + 1) * Continent.Size + CellClamp(i0), c11 = CellClamp(j0 + 1) * Continent.Size + CellClamp(i0 + 1);
                int Bil(byte[] a) => (int)((((long)a[c00] * (65536 - t) + (long)a[c10] * t >> 16) * (65536 - u) + ((long)a[c01] * (65536 - t) + (long)a[c11] * t >> 16) * u) >> 16);
                int Bilb(bool[] a) => (int)((((a[c00] ? (long)(65536 - t) : 0) + (a[c10] ? (long)t : 0)) * (65536 - u) + ((a[c01] ? (long)(65536 - t) : 0) + (a[c11] ? (long)t : 0)) * u) >> 16);
                int e = ey * w + ex;
                if (fertOut != null) fertOut[e] = (byte)Math.Clamp(Bil(_c.Fertility), 0, 100);
                if (drainOut != null) drainOut[e] = (byte)Math.Clamp(Bil(_c.Drainage), 0, 100);
                int lakeW = Bilb(_c.Lake);
                if (lakeOut != null) lakeOut[e] = lakeW;
                if (hCm > 0 && lakeW > 32768) hCm -= (int)((long)(lakeW - 32768) * 2 * _s.LakeDepressCm >> 16);
                hOut[e] = hCm;
            }
        }
    }

    internal static int[] HeightBlock(Continent c, WorldHistory h, RegionSettings s, int tileX0, int tileY0, int w, int ht)
    {
        var b = new RegionBuilder(c, h, 0, 0, s);
        var o = new int[w * ht];
        b.TerrainBlock(tileX0 * TileM, tileY0 * TileM, w, ht, o, null, null, null);
        return o;
    }

    /// <summary>The extended window (the region plus a one-cell margin): heights, sea, lake indicator and the smooth layers.</summary>
    private void BuildTerrain()
    {
        _eH = new int[E * E]; _eSea = new bool[E * E]; _eFertBase = new byte[E * E]; _eDrainBase = new byte[E * E];
        var lakeW = new int[E * E]; _eLakeW = lakeW;
        TerrainBlock(_ox - Mg * TileM, _oy - Mg * TileM, E, E, _eH, lakeW, _eFertBase, _eDrainBase);
        for (int e = 0; e < E * E; e++) _eSea[e] = _eH[e] <= 0;
        _eSlope = new short[E * E];
        for (int ey = 1; ey < E - 1; ey++)
            for (int ex = 1; ex < E - 1; ex++)
            {
                int e = ey * E + ex;
                long gx = _eH[e + 1] - _eH[e - 1], gy = _eH[e + E] - _eH[e - E];
                _eSlope[e] = (short)Math.Min(1000, Region.Isqrt(gx * gx + gy * gy) * 10 / 128);
            }
    }

    // ---------- hydrology: priority flood + D8 ----------
    private sealed class Heap
    {
        private long[] _a = new long[1024];
        public int Count;
        public void Push(long k)
        {
            if (Count == _a.Length) Array.Resize(ref _a, Count * 2);
            int i = Count++;
            while (i > 0) { int p = (i - 1) >> 1; if (_a[p] <= k) break; _a[i] = _a[p]; i = p; }
            _a[i] = k;
        }
        public long Pop()
        {
            long top = _a[0], last = _a[--Count];
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1; if (l >= Count) break;
                int r = l + 1, m = r < Count && _a[r] < _a[l] ? r : l;
                if (_a[m] >= last) break;
                _a[i] = _a[m]; i = m;
            }
            if (Count > 0) _a[i] = last;
            return top;
        }
    }

    private int _streamTiles, _riverTiles;

    private void BuildHydrology()
    {
        int A = E * E;
        _eFill = new int[A]; _eRecv = new int[A]; _eAcc = new int[A];
        var closed = new bool[A];
        var order = new int[A]; int oc = 0;
        var heap = new Heap();
        long seq = 0;
        const int Off = 10000;
        void Push(int e) => heap.Push(((long)(_eFill[e] + Off) << 24) | (seq++ & 0xFFFFFF)) ;
        // Heap key carries (level, sequence); the tile is looked up through a parallel queue because the key has no room for it.
        var tileOfSeq = new int[A + 1];
        void PushT(int e) { tileOfSeq[seq & 0xFFFFFF] = e; Push(e); }

        for (int ey = 0; ey < E; ey++)
            for (int ex = 0; ex < E; ex++)
            {
                int e = ey * E + ex;
                _eRecv[e] = -1;
                if (_eSea[e])
                {
                    closed[e] = true; _eFill[e] = _eH[e];
                    bool coast = false;
                    for (int d = 0; d < 8 && !coast; d++)
                    {
                        int nx = ex + Dx8[d], ny = ey + Dy8[d];
                        if ((uint)nx < E && (uint)ny < E && !_eSea[ny * E + nx]) coast = true;
                    }
                    if (coast) PushT(e);
                }
                else if (ex == 0 || ey == 0 || ex == E - 1 || ey == E - 1)
                {
                    closed[e] = true; _eFill[e] = _eH[e]; PushT(e);
                }
            }
        while (heap.Count > 0)
        {
            long k = heap.Pop();
            int cur = tileOfSeq[k & 0xFFFFFF];
            order[oc++] = cur;
            int cx = cur % E, cy = cur / E;
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + Dx8[d], ny = cy + Dy8[d];
                if ((uint)nx >= E || (uint)ny >= E) continue;
                int n = ny * E + nx;
                if (closed[n]) continue;
                closed[n] = true;
                _eFill[n] = Math.Max(_eH[n], _eFill[cur]);
                _eRecv[n] = cur;
                PushT(n);
            }
        }
        // The continental rivers are laid first and the local flow network follows them (RegionBuilder.Rivers.cs).
        BuildChains();
        ApplyChainsToFlow();
        // Flow accumulation in tiles; upstream cells feed in at the centre of their ring cell (continental D8 into the region).
        for (int e = 0; e < A; e++) _eAcc[e] = _eSea[e] ? 0 : 1;
        InjectInflow();
        AccumulateFlow();
        _streamTiles = (int)((long)_s.StreamCatchmentTenthsKm2 * 100_000 / (TileM * TileM));
        _riverTiles = (int)((long)_s.RiverCatchmentKm2 * 1_000_000 / (TileM * TileM));

        // Crop to the region.
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int r = Idx(x, y), e = (y + Mg) * E + x + Mg;
                _r.HeightCm[r] = _eH[e];
                _r.SlopePermille[r] = _eSlope[e];
                _r.Catchment[r] = _eAcc[e];
                int rv = _eRecv[e];
                if (rv >= 0)
                {
                    int rx = rv % E - Mg, ry = rv / E - Mg;
                    _r.FlowTo[r] = (uint)rx < N && (uint)ry < N ? Idx(rx, ry) : -1;
                }
                else _r.FlowTo[r] = -1;
                if (_eSea[e]) _r.Water[r] |= WaterFlags.Sea;
            }
        BuildLakes();
        BuildWaterways();
        BuildWaterDistance();
    }

    private void InjectInflow()
    {
        for (int ccy = _cy - 1; ccy <= _cy + 16; ccy++)
            for (int ccx = _cx - 1; ccx <= _cx + 16; ccx++)
            {
                bool inside = ccx >= _cx && ccx < _cx + 16 && ccy >= _cy && ccy < _cy + 16;
                if (inside || ccx < 0 || ccy < 0 || ccx >= Continent.Size || ccy >= Continent.Size) continue;
                int ci = Continent.Index(ccx, ccy);
                if (_c.Ocean[ci]) continue;
                int to = _c.FlowTo[ci];
                if (to < 0) continue;
                int tx = to % Continent.Size, ty = to / Continent.Size;
                if (tx < _cx || tx >= _cx + 16 || ty < _cy || ty >= _cy + 16) continue;
                int cells = Math.Max(1, _c.RiverFlow[ci] / (1 + _c.Rainfall[ci] / 150));
                int ex = FloorDiv(ccx * RegionSettings.CellM + RegionSettings.CellM / 2 - _ox, TileM) + Mg;
                int ey = FloorDiv(ccy * RegionSettings.CellM + RegionSettings.CellM / 2 - _oy, TileM) + Mg;
                if ((uint)ex >= E || (uint)ey >= E) continue;
                int e = ey * E + ex;
                if (_eSea[e]) continue;
                _eAcc[e] += (int)Math.Min(int.MaxValue / 4, (long)cells * 976);
            }
    }

    // ---------- lakes ----------
    private void BuildLakes()
    {
        var depth = new int[Tiles];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int r = Idx(x, y), e = (y + Mg) * E + x + Mg;
                int dp = _eSea[e] ? 0 : _eFill[e] - _eH[e];
                // Only continental lake basins and genuinely deep pits become lakes; shallow noise pits just drain.
                depth[r] = dp >= 2000 || (dp > 0 && _eLakeW[e] > 20000) ? dp : 0;
                // The basin a lake fills comes from this region's own window, so a neighbour may disagree about it: the edge tiles are never lake, which
                // keeps a river that crosses an edge a river on both sides (seamless-regions spec 2.3).
                if (x == 0 || y == 0 || x == N - 1 || y == N - 1) depth[r] = 0;
            }
        var seen = new bool[Tiles];
        var queue = new int[Tiles];
        for (int start = 0; start < Tiles; start++)
        {
            if (seen[start] || depth[start] < _s.LakeMinDepthCm) continue;
            int qh = 0, qt = 0; queue[qt++] = start; seen[start] = true;
            int maxD = 0, surf = int.MinValue; long sx = 0, sy = 0;
            int minX = N, minY = N, maxX = 0, maxY = 0;
            while (qh < qt)
            {
                int t = queue[qh++]; int x = t % N, y = t / N;
                maxD = Math.Max(maxD, depth[t]);
                surf = Math.Max(surf, _r.HeightCm[t] + depth[t]);
                sx += x; sy += y; minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                for (int d = 0; d < 8; d += 2)
                {
                    int nx = x + Dx8[d], ny = y + Dy8[d];
                    if ((uint)nx >= N || (uint)ny >= N) continue;
                    int n = Idx(nx, ny);
                    if (seen[n] || depth[n] < _s.LakeMinDepthCm) continue;
                    seen[n] = true; queue[qt++] = n;
                }
            }
            if (qt < _s.LakeMinTiles || maxD < _s.LakeMinCoreDepthCm) continue;
            int id = _r.Lakes.Count;
            _r.Lakes.Add(new Lake
            {
                Id = id, Tiles = qt, SurfaceCm = surf, MaxDepthCm = maxD,
                Center = new PointM((int)(sx / qt) * TileM + TileM / 2, (int)(sy / qt) * TileM + TileM / 2),
                MinTileX = minX, MinTileY = minY, MaxTileX = maxX, MaxTileY = maxY,
            });
            for (int i = 0; i < qt; i++) { int t = queue[i]; _r.Water[t] |= WaterFlags.Lake; _r.LakeMap[t] = (ushort)(id + 1); }
        }
    }

    // ---------- streams and rivers as polylines ----------
    private static int WidthCm(bool river, long acc, long streamT, long riverT)
    {
        long s = Region.Isqrt(acc * TileM * TileM); // sqrt of catchment area in metres
        long s0 = Region.Isqrt(streamT * TileM * TileM), s1 = Region.Isqrt(riverT * TileM * TileM), s2 = 31_623;
        if (!river) return (int)(150 + (Math.Clamp(s, s0, s1) - s0) * 450 / Math.Max(1, s1 - s0));
        return (int)(1500 + (Math.Clamp(s, s1, s2) - s1) * 2500 / Math.Max(1, s2 - s1));
    }
    private static int DepthCm(bool river, long acc, long streamT, long riverT)
    {
        long s = Region.Isqrt(acc * TileM * TileM);
        long s0 = Region.Isqrt(streamT * TileM * TileM), s1 = Region.Isqrt(riverT * TileM * TileM), s2 = 31_623;
        if (!river) return (int)(25 + (Math.Clamp(s, s0, s1) - s0) * 55 / Math.Max(1, s1 - s0));
        return (int)(120 + (Math.Clamp(s, s1, s2) - s1) * 180 / Math.Max(1, s2 - s1));
    }

    private bool[] _chan = [];

    private void BuildWaterways()
    {
        var acc = _r.Catchment; var recv = _r.FlowTo;
        _chan = new bool[Tiles];
        WaterwayAt = new int[Tiles]; Array.Fill(WaterwayAt, -1);
        var kids = new byte[Tiles]; var bigKids = new byte[Tiles];
        for (int t = 0; t < Tiles; t++)
        {
            _chan[t] = acc[t] >= _streamTiles && (_r.Water[t] & (WaterFlags.Lake | WaterFlags.Sea)) == 0;
            // Only continental rivers cross an edge (they are the same on both sides); a local stream ends one tile short of it.
            int tx = t % N, ty = t / N;
            if (_chan[t] && (tx == 0 || ty == 0 || tx == N - 1 || ty == N - 1) && !OnChain(t)) _chan[t] = false;
        }
        // water that runs into a pruned edge tile drains off the region there
        for (int t = 0; t < Tiles; t++)
        {
            if (!_chan[t]) continue;
            int rv0 = recv[t];
            if (rv0 >= 0 && !_chan[rv0] && (rv0 % N == 0 || rv0 / N == 0 || rv0 % N == N - 1 || rv0 / N == N - 1) && acc[rv0] >= _streamTiles) recv[t] = -1;
        }
        for (int t = 0; t < Tiles; t++)
        {
            if (!_chan[t]) continue;
            int rv = recv[t];
            if (rv >= 0 && _chan[rv]) { kids[rv]++; if (acc[t] >= _riverTiles) bigKids[rv]++; }
        }
        bool IsStart(int t) => _chan[t] && (kids[t] != 1 || (acc[t] >= _riverTiles && bigKids[t] == 0) || BorderPoint(t, false) != null);
        var chainAt = new int[Tiles];
        Array.Fill(chainAt, -1);
        var pending = new List<(int Id, int NextTile)>();
        var chains = new List<List<int>>();
        for (int s = 0; s < Tiles; s++)
        {
            if (!IsStart(s)) continue;
            var tiles = new List<int> { s };
            bool river = acc[s] >= _riverTiles;
            var w = new Waterway { Id = _r.Waterways.Count, Kind = river ? WaterKind.River : WaterKind.Stream };
            chainAt[s] = w.Id;
            int cur = s;
            int endNext = -1;
            while (true)
            {
                int nxt = recv[cur];
                if (nxt < 0) { w.End = WaterEnd.Edge; break; }
                if ((_r.Water[nxt] & WaterFlags.Sea) != 0) { tiles.Add(nxt); w.End = WaterEnd.Sea; break; }
                if ((_r.Water[nxt] & WaterFlags.Lake) != 0) { tiles.Add(nxt); w.End = WaterEnd.Lake; w.EndRef = _r.LakeMap[nxt] - 1; break; }
                if (!_chan[nxt]) { w.End = WaterEnd.Edge; break; }
                tiles.Add(nxt);
                if (IsStart(nxt)) { w.End = kids[nxt] >= 2 ? WaterEnd.Joins : WaterEnd.Continues; endNext = nxt; break; }
                cur = nxt;
            }
            if (endNext >= 0) pending.Add((w.Id, endNext));
            _r.Waterways.Add(w);
            chains.Add(tiles);
        }
        foreach (var (id, nt) in pending) _r.Waterways[id].EndRef = chainAt[nt];

        for (int i = 0; i < _r.Waterways.Count; i++)
        {
            var w = _r.Waterways[i]; var tl = chains[i];
            w.Tiles = [.. tl];
            // A continental river that crosses an edge gets a vertex on the border line itself, the same point the neighbour computes.
            var head = BorderPoint(tl[0], false); var tail = BorderPoint(tl[^1], true);
            int n0 = tl.Count, off = head != null ? 1 : 0;
            int n = n0 + off + (tail != null ? 1 : 0);
            var raw = new PointM[n];
            if (head != null) raw[0] = head.Value;
            for (int k = 0; k < n0; k++) raw[off + k] = new PointM(TileCenter(tl[k] % N), TileCenter(tl[k] / N));
            if (tail != null) raw[n - 1] = tail.Value;
            var pts = new PointM[n];
            for (int k = 0; k < n; k++)
                pts[k] = k == 0 || k == n - 1 ? raw[k] : new PointM((raw[k - 1].X + 2 * raw[k].X + raw[k + 1].X) / 4, (raw[k - 1].Y + 2 * raw[k].Y + raw[k + 1].Y) / 4);
            w.Points = pts;
            w.WidthCm = new int[n]; w.DepthCm = new int[n];
            for (int k = 0; k < n; k++)
            {
                long a = acc[tl[Math.Clamp(k - off, 0, n0 - 1)]];
                bool rv = a >= _riverTiles;
                w.WidthCm[k] = WidthCm(rv, a, _streamTiles, _riverTiles);
                w.DepthCm[k] = DepthCm(rv, a, _streamTiles, _riverTiles);
            }
            int len = 0;
            for (int k = 1; k < n; k++) len += Dist(pts[k - 1].X, pts[k - 1].Y, pts[k].X, pts[k].Y);
            w.LengthM = len;
            w.CatchmentTenthsKm2 = (int)((long)acc[tl[n0 - 1]] * TileM * TileM / 100_000);
            bool river = w.Kind == WaterKind.River;
            foreach (int t in tl) if (_chan[t]) { _r.Water[t] |= river ? WaterFlags.River : WaterFlags.Stream; WaterwayAt[t] = w.Id; }
        }
    }

    internal int[] WaterwayAt = [];

    // Distance (chebyshev, capped) to the nearest stream/river tile or lake tile, and to river/lake only.
    internal byte[] WaterDist = [];
    internal byte[] RiverDist = [];

    private void BuildWaterDistance()
    {
        WaterDist = MultiSourceDist(t => (_r.Water[t] & (WaterFlags.Stream | WaterFlags.River | WaterFlags.Lake)) != 0, 60);
        RiverDist = MultiSourceDist(t => (_r.Water[t] & (WaterFlags.River | WaterFlags.Lake)) != 0, 60);
    }

    internal byte[] MultiSourceDist(Func<int, bool> src, int cap)
    {
        var d = new byte[Tiles];
        Array.Fill(d, (byte)255);
        var q = new int[Tiles]; int qh = 0, qt = 0;
        for (int t = 0; t < Tiles; t++) if (src(t)) { d[t] = 0; q[qt++] = t; }
        while (qh < qt)
        {
            int t = q[qh++]; int x = t % N, y = t / N;
            if (d[t] >= cap) continue;
            for (int k = 0; k < 8; k++)
            {
                int nx = x + Dx8[k], ny = y + Dy8[k];
                if ((uint)nx >= N || (uint)ny >= N) continue;
                int n = Idx(nx, ny);
                if (d[n] != 255) continue;
                d[n] = (byte)(d[t] + 1); q[qt++] = n;
            }
        }
        return d;
    }
}
