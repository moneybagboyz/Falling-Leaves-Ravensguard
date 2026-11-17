using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen;

public sealed partial class ContinentGenerator
{
    private bool[] _isRiver = new bool[A];
    private readonly int[] _pit = new int[A];   // depth of the priority-flood fill above the original surface

    // ---------- hydrology: priority-flood, D8 routing, flow accumulation, lakes, river polylines ----------
    private void Hydrology()
    {
        var elev = _c.Elevation;
        var filled = (int[])elev.Clone();
        var recv = _c.FlowTo;
        Array.Fill(recv, -1);
        var closed = new bool[A];
        var order = new List<int>(A);
        var open = new PriorityQueue<int, long>();
        for (int i = 0; i < A; i++)
        {
            int x = i % N, y = i / N;
            bool border = x == 0 || y == 0 || x == N - 1 || y == N - 1;
            bool seed = _c.Ocean[i] && HasLandNeighbour(i);
            if (seed || (border && !_c.Ocean[i])) { closed[i] = true; open.Enqueue(i, ((long)filled[i] << 20) | (uint)i); }
            else if (_c.Ocean[i] && !seed) closed[i] = true;   // deep ocean needs no processing
        }
        while (open.Count > 0)
        {
            int cur = open.Dequeue();
            order.Add(cur);
            int x = cur % N, y = cur / N;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + Dx[d], ny = y + Dy[d];
                if (!In(nx, ny)) continue;
                int n = ny * N + nx;
                if (closed[n]) continue;
                closed[n] = true;
                recv[n] = cur;
                filled[n] = Math.Max(elev[n], filled[cur]);
                open.Enqueue(n, ((long)filled[n] << 20) | (uint)n);
            }
        }

        // Flow accumulation: children are popped after parents, so walk the order backwards.
        var flow = _c.RiverFlow;
        for (int i = 0; i < A; i++) if (!_c.Ocean[i]) flow[i] = 1 + _c.Rainfall[i] / 150;
        for (int k = order.Count - 1; k >= 0; k--)
        {
            int cur = order[k];
            int r = recv[cur];
            if (r >= 0 && !_c.Ocean[cur]) flow[r] += flow[cur];
        }

        for (int i = 0; i < A; i++) _pit[i] = filled[i] - elev[i];

        // Lakes: filled depressions with real size and depth (tiny pits just stay wet cells).
        var seen = new bool[A];
        var q = new Queue<int>();
        var comp = new List<int>();
        for (int s0 = 0; s0 < A; s0++)
        {
            if (seen[s0] || _c.Ocean[s0] || filled[s0] <= elev[s0]) continue;
            comp.Clear(); q.Enqueue(s0); seen[s0] = true; int maxDepth = 0;
            while (q.Count > 0)
            {
                int cur = q.Dequeue(); comp.Add(cur);
                maxDepth = Math.Max(maxDepth, filled[cur] - elev[cur]);
                int x = cur % N, y = cur / N;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + Dx[d], ny = y + Dy[d];
                    if (!In(nx, ny)) continue;
                    int n = ny * N + nx;
                    if (!seen[n] && !_c.Ocean[n] && filled[n] > elev[n]) { seen[n] = true; q.Enqueue(n); }
                }
            }
            if (comp.Count >= 5 && comp.Count <= 250 && maxDepth >= 500) foreach (var cell in comp) _c.Lake[cell] = true;
        }

        // Rivers.
        for (int i = 0; i < A; i++) _isRiver[i] = !_c.Ocean[i] && flow[i] >= _s.RiverFlowThreshold;
        var inflow = new int[A];
        for (int i = 0; i < A; i++) if (_isRiver[i] && recv[i] >= 0 && _isRiver[recv[i]]) inflow[recv[i]]++;
        for (int i = 0; i < A; i++)
        {
            if (!_isRiver[i] || inflow[i] == 1) continue;
            var cells = new List<int> { i };
            int cur = i;
            while (true)
            {
                int nxt = recv[cur];
                if (nxt < 0) break;
                cells.Add(nxt);
                if (_c.Ocean[nxt] || !_isRiver[nxt] || inflow[nxt] != 1) break;
                cur = nxt;
            }
            int last = cells[^1];
            int maxFlow = _c.Ocean[last] && cells.Count > 1 ? flow[cells[^2]] : flow[last];
            _c.Rivers.Add(new River([.. cells], maxFlow));
        }
    }

    private bool HasLandNeighbour(int i)
    {
        int x = i % N, y = i / N;
        for (int d = 0; d < 8; d++)
        {
            int nx = x + Dx[d], ny = y + Dy[d];
            if (In(nx, ny) && !_c.Ocean[ny * N + nx]) return true;
        }
        return false;
    }

    // ---------- coast distance (8-neighbour BFS) ----------
    private void CoastDistance()
    {
        BfsDistance(land: true);   // land cells: distance to ocean
        BfsDistance(land: false);  // ocean cells: negative distance to land
    }

    private void BfsDistance(bool land)
    {
        var dist = new int[A]; Array.Fill(dist, -1);
        var q = new Queue<int>();
        for (int i = 0; i < A; i++) if (_c.Ocean[i] == land) { dist[i] = 0; q.Enqueue(i); }
        while (q.Count > 0)
        {
            int cur = q.Dequeue(); int x = cur % N, y = cur / N;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + Dx[d], ny = y + Dy[d];
                if (!In(nx, ny)) continue;
                int n = ny * N + nx;
                if (dist[n] < 0) { dist[n] = dist[cur] + 1; q.Enqueue(n); }
            }
        }
        for (int i = 0; i < A; i++)
        {
            if (land && !_c.Ocean[i]) _c.CoastDistance[i] = (short)(dist[i] < 0 ? 999 : dist[i]);
            if (!land && _c.Ocean[i]) _c.CoastDistance[i] = (short)(dist[i] < 0 ? -999 : -dist[i]);
        }
    }

    // ---------- drainage, biomes, soil ----------
    private bool NextToLake(int i)
    {
        int x = i % N, y = i / N;
        for (int d = 0; d < 8; d++)
        {
            int nx = x + Dx[d], ny = y + Dy[d];
            if (In(nx, ny) && _c.Lake[ny * N + nx]) return true;
        }
        return false;
    }

    private void Drainage()
    {
        for (int i = 0; i < A; i++)
        {
            if (_c.Ocean[i]) continue;
            int above = AbovePct(i);
            int d = 40 + Math.Min(30, _slope[i] / 45) + Math.Min(20, above * 2);
            if (_c.RiverFlow[i] >= _s.StreamFlowThreshold) d -= 12;
            if (NextToLake(i) || _c.Lake[i]) d -= 25;
            if (_c.CoastDistance[i] <= 2 && above < 2) d -= 20;
            if (_pit[i] > 0) d -= 20;                       // basins too big or shallow to be lakes become wet plains
            if (_c.Rainfall[i] > 1400) d -= 8; else if (_c.Rainfall[i] < 400) d += 8;
            _c.Drainage[i] = (byte)Math.Clamp(d, 0, 100);
        }
    }

    private Biome[] _climate = new Biome[A];

    private void Biomes()
    {
        for (int i = 0; i < A; i++)
        {
            if (_c.Ocean[i]) { _c.Biomes[i] = Biome.Sea; _climate[i] = Biome.Sea; continue; }
            if (_c.Lake[i]) { _c.Biomes[i] = Biome.Lake; _climate[i] = Biome.Lake; continue; }
            int t = _c.Temperature[i], rain = _c.Rainfall[i], above = AbovePct(i);
            Biome b;
            if (above >= 30 || (above >= 14 && _slope[i] > 1700)) b = Biome.Mountain;
            else if (_c.Drainage[i] <= 34 && rain >= 450 && t >= -20) b = Biome.Marsh;
            else if (t < 0) b = Biome.Tundra;
            else if (t < 50) b = rain >= 300 ? Biome.Taiga : t < 20 ? Biome.Tundra : Biome.Steppe;
            else if (rain < 250 && t >= 120) b = Biome.Desert;
            else if (rain < 400) b = Biome.Steppe;
            else if (rain < 700) b = Biome.Grassland;
            else b = Biome.TemperateForest;
            _climate[i] = b;
        }
        for (int i = 0; i < A; i++)
        {
            var b = _climate[i];
            if (b is Biome.Sea or Biome.Lake or Biome.Mountain) { _c.Biomes[i] = b; continue; }
            bool valley = _isRiver[i] || (_c.RiverFlow[i] >= _s.StreamFlowThreshold && _slope[i] < 400);
            if (!valley && _slope[i] < 300 && b != Biome.Marsh)
            {
                int x = i % N, y = i / N;
                for (int d = 0; d < 8 && !valley; d++)
                {
                    int nx = x + Dx[d], ny = y + Dy[d];
                    if (In(nx, ny) && _isRiver[ny * N + nx]) valley = true;
                }
            }
            _c.Biomes[i] = valley && b != Biome.Marsh ? Biome.RiverValley : b;
        }
    }

    private void Soil()
    {
        // Distance (Chebyshev, <= 2) to any stream-sized cell, for the alluvial bonus.
        var near = new bool[A];
        for (int i = 0; i < A; i++)
        {
            if (_c.Ocean[i] || _c.Lake[i] || _c.RiverFlow[i] < _s.StreamFlowThreshold) continue;
            int x = i % N, y = i / N;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    if (In(x + dx, y + dy)) near[(y + dy) * N + x + dx] = true;
        }
        for (int i = 0; i < A; i++)
        {
            var b = _climate[i];
            if (b is Biome.Sea or Biome.Lake) continue;
            int f = b switch
            {
                Biome.Grassland => 70, Biome.TemperateForest => 62, Biome.Steppe => 42, Biome.Desert => 6, Biome.Tundra => 8,
                Biome.Taiga => 28, Biome.Marsh => 38, _ => 4,
            };
            f -= Math.Min(50, _slope[i] / 25);
            int t = _c.Temperature[i], rain = _c.Rainfall[i];
            if (t < 50) f -= (50 - t) / 3;
            if (rain < 400) f -= (400 - rain) / 10;
            if (_c.Drainage[i] < 30) f -= 10;
            if (near[i] && _slope[i] < 500 && b != Biome.Mountain) f += _c.Biomes[i] == Biome.RiverValley ? 25 : 18;
            _c.Fertility[i] = (byte)Math.Clamp(f, 0, 100);
        }
    }
}
