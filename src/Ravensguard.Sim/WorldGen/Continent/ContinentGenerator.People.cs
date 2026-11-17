using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen;

public sealed partial class ContinentGenerator
{
    // ---------- strata (rock type) and mineral veins as a separate layer ----------
    private void StrataAndMinerals()
    {
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                if (_c.Ocean[i]) continue;
                int nS = Nz(40, x, y, 38, 3).Raw, nV = Nz(41, x, y, 20, 2).Raw;
                int above = AbovePct(i);
                Strata st;
                if (_up[i] > 16384) st = Strata.Metamorphic;
                else if (_up[i] > 7000) st = nV > 32768 ? Strata.Volcanic : Strata.Metamorphic;
                else if (above < 3 && _c.RiverFlow[i] >= _s.StreamFlowThreshold) st = Strata.Sedimentary;
                else if (nS < 26000) st = Strata.Sedimentary;
                else if (nS < 46000) st = Strata.Granite;
                else st = nV > 36000 ? Strata.Volcanic : Strata.Metamorphic;
                _c.Strata[i] = st;
            }

        for (int i = 0; i < A; i++)
        {
            if (_c.Ocean[i] || _c.Lake[i]) continue;
            var st = _c.Strata[i]; int above = AbovePct(i), rain = _c.Rainfall[i], t = _c.Temperature[i];
            var biome = _climate[i];
            int sed = st == Strata.Sedimentary ? 1 : 0, gra = st == Strata.Granite ? 1 : 0, vol = st == Strata.Volcanic ? 1 : 0, met = st == Strata.Metamorphic ? 1 : 0;

            Roll(MineralType.Stone, i, sed * 700 + gra * 1200 + vol * 900 + met * 1000, 5000, 95000);
            Roll(MineralType.Iron, i, sed * 800 + met * 900 + gra * 150 + vol * 200, 800, 11000);
            Roll(MineralType.Copper, i, vol * 700 + met * 500 + gra * 150 + sed * 50, 200, 3800);
            if (above >= 6) Roll(MineralType.Tin, i, gra * 350 + met * 120, 100, 1900);
            if (above >= 8) Roll(MineralType.Silver, i, met * 220 + gra * 90 + vol * 90, 20, 380);
            if (above >= 10) Roll(MineralType.Gold, i, met * 120 + gra * 70, 5, 75);
            if (above >= 3 && _c.RiverFlow[i] >= _s.StreamFlowThreshold && (gra + met) > 0) Roll(MineralType.Gold, i, 150, 5, 60); // placer
            if (sed > 0 && above < 14 && rain >= 450) Roll(MineralType.Coal, i, 1500, 3000, 57000);
            if ((biome is Biome.Marsh or Biome.Taiga or Biome.Tundra) && rain >= 400) Roll(MineralType.Peat, i, 1000, 500, 7500);
            if (sed > 0 && rain < 550) Roll(MineralType.Salt, i, 500, 2000, 38000);
            else if (_c.CoastDistance[i] <= 2 && t >= 100 && rain < 900) Roll(MineralType.Salt, i, 400, 2000, 38000);
        }
    }

    private void Roll(MineralType type, int cell, int per100k, int tonBase, int tonRange)
    {
        if (per100k <= 0) return;
        long scaled = (long)per100k * _s.MineralDensityPct;
        var p = Fixed.FromRaw((int)Math.Min(65535, scaled * 65536 / 10_000_000));
        uint stream = 70u + (uint)type;
        if (!DetRng.Chance(_seed, 0, (ulong)cell, stream, 0, p)) return;
        int u = DetRng.Range(_seed, 0, (ulong)cell, stream, 1, 0, 100);
        _c.Veins.Add(new MineralVein(type, cell, tonBase + tonRange * u * u / 10000));
    }

    // ---------- cultures and realms ----------
    private void CulturesAndRealms()
    {
        Array.Fill(_c.CultureId, Continent.None);
        Array.Fill(_c.RealmId, Continent.None);

        var landFert = new List<int>();
        for (int i = 0; i < A; i++) if (!_c.Ocean[i] && !_c.Lake[i]) landFert.Add(_c.Fertility[i]);
        if (landFert.Count == 0) return;
        landFert.Sort();
        int median = landFert[landFert.Count / 2];
        var good = new List<int>(); var anyLand = new List<int>();
        for (int i = 0; i < A; i++)
        {
            if (_c.Ocean[i] || _c.Lake[i]) continue;
            anyLand.Add(i);
            if (_c.Fertility[i] > median) good.Add(i);
        }
        var cand = good.Count >= 16 ? good : anyLand;

        int nc = Math.Clamp(_s.CultureCount, 3, 8), nr = Math.Clamp(_s.RealmCount, 1, 24);
        var cost = StepCosts();

        var cSeeds = PickSeeds(Math.Min(nc, cand.Count), 50, cand, 2, 10);
        var (cLabel, _) = Regions(cSeeds, cost);
        var cNames = Shuffled(_s.CultureNames, 51);
        for (int k = 0; k < cSeeds.Count; k++)
            _c.Cultures.Add(new Culture(k, NameFor(cNames, k), cSeeds[k]));
        for (int i = 0; i < A; i++) if (!_c.Ocean[i] && cLabel[i] >= 0) _c.CultureId[i] = (byte)cLabel[i];

        var rSeeds = PickSeeds(Math.Min(nr, cand.Count), 52, cand, 1, 20);
        var (rLabel, rDist) = Regions(rSeeds, cost);
        var rNames = Shuffled(_s.RealmNames, 53);
        for (int k = 0; k < rSeeds.Count; k++)
        {
            int cu = _c.CultureId[rSeeds[k]];
            _c.Realms.Add(new Realm(k, NameFor(rNames, k), rSeeds[k], cu == Continent.None ? 0 : cu));
        }
        for (int i = 0; i < A; i++) if (!_c.Ocean[i] && rLabel[i] >= 0) _c.RealmId[i] = (byte)rLabel[i];
        _realmDist = rDist;
    }

    private int[] _realmDist = new int[A];

    private static string NameFor(List<string> pool, int k) =>
        pool.Count == 0 ? $"#{k + 1}" : k < pool.Count ? pool[k] : $"{pool[k % pool.Count]} {(k / pool.Count) + 1}";

    private List<string> Shuffled(List<string> src, uint stream)
    {
        var a = new List<string>(src);
        for (int i = a.Count - 1; i > 0; i--)
        {
            int j = R(stream, 0, (uint)i, 0, i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
        return a;
    }

    /// <summary>Best-candidate seed picking: spread out (weighted by spacing) and fertile.</summary>
    private List<int> PickSeeds(int count, uint stream, List<int> cand, int spacingWeight, int fertWeight)
    {
        var chosen = new List<int>();
        for (int k = 0; k < count; k++)
        {
            long bestScore = long.MinValue; int best = -1;
            for (int t = 0; t < 48; t++)
            {
                int ci = cand[R(stream, (ulong)k, (uint)t, 0, cand.Count)];
                if (chosen.Contains(ci)) continue;
                long md = 40000;
                foreach (var o in chosen)
                {
                    long dx = ci % N - o % N, dy = ci / N - o / N;
                    md = Math.Min(md, dx * dx + dy * dy);
                }
                long score = md * spacingWeight + (long)_c.Fertility[ci] * fertWeight * 4;
                if (score > bestScore) { bestScore = score; best = ci; }
            }
            if (best >= 0) chosen.Add(best);
        }
        return chosen;
    }

    private int[] StepCosts()
    {
        var cost = new int[A];
        for (int i = 0; i < A; i++)
        {
            if (_c.Ocean[i]) { cost[i] = 50; continue; }
            if (_c.Lake[i]) { cost[i] = 40; continue; }
            int k = 10 + Math.Min(30, _slope[i] / 60);
            switch (_c.Biomes[i])
            {
                case Biome.Mountain: k += 30; break;
                case Biome.Marsh: k += 8; break;
                case Biome.Desert: k += 5; break;
                case Biome.Taiga or Biome.Tundra: k += 3; break;
            }
            if (_isRiver[i]) k += 8;
            cost[i] = k;
        }
        return cost;
    }

    /// <summary>Multi-source Dijkstra by terrain cost (oceans are crossable at a high price so islands can be claimed).</summary>
    private (int[] Label, int[] Dist) Regions(List<int> seeds, int[] cost)
    {
        var label = new int[A]; var dist = new int[A];
        Array.Fill(label, -1); Array.Fill(dist, int.MaxValue);
        var pq = new PriorityQueue<int, long>();
        for (int k = 0; k < seeds.Count; k++) { label[seeds[k]] = k; dist[seeds[k]] = 0; pq.Enqueue(seeds[k], seeds[k]); }
        while (pq.Count > 0)
        {
            int cur = pq.Dequeue();
            // Stale entries are harmless: re-relaxing from the final dist yields no improvement.
            int x = cur % N, y = cur / N;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + Dx[d], ny = y + Dy[d];
                if (!In(nx, ny)) continue;
                int n = ny * N + nx;
                int step = (d & 1) == 1 ? cost[n] * 14 / 10 : cost[n];
                int nd = dist[cur] + step;
                if (nd < dist[n]) { dist[n] = nd; label[n] = label[cur]; pq.Enqueue(n, ((long)nd << 20) | (uint)n); }
            }
        }
        return (label, dist);
    }

    private void SavageryLayer()
    {
        for (int i = 0; i < A; i++)
        {
            if (_c.Ocean[i] || _c.Lake[i]) continue;
            int b = _c.Biomes[i] switch
            {
                Biome.Mountain => 40, Biome.Taiga or Biome.Marsh or Biome.Tundra => 35, Biome.Desert => 30,
                Biome.TemperateForest => 28, Biome.Steppe => 22, _ => 15,
            };
            int far = _realmDist[i] == int.MaxValue ? 35 : Math.Min(35, _realmDist[i] / 50);
            _c.Savagery[i] = (byte)Math.Clamp((b + far) * _s.SavageryPct / 100, 0, 100);
        }
    }
}
