using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.WorldGen.Region;

internal sealed partial class RegionBuilder
{
    private int[] _siteScore = [];
    private readonly List<(int HistIndex, Settlement S)> _histTowns = [], _histVillages = [];
    private int _ruralTotal, _villageTarget, _farmTarget, _townPop;
    private NameGen? _names;
    private readonly HashSet<string> _usedNames = [];

    /// <summary>True when a tile can carry fields: land, gentle, fertile enough and not mountain, desert, tundra or marsh.</summary>
    internal bool FieldEligible(int t, int minFert)
    {
        if (_r.Water[t] != 0 || _r.Built[t] != 0 || _r.Road[t] != 0) return false;
        if (_r.Fertility[t] < minFert || _r.SlopePermille[t] > _s.MaxFieldSlopePermille) return false;
        return _r.Biomes[t] is not (Biome.Mountain or Biome.Desert or Biome.Tundra or Biome.Marsh or Biome.Sea or Biome.Lake);
    }

    private void PlanPopulation()
    {
        long histRural = 0;
        for (int dy = 0; dy < 16; dy++)
            for (int dx = 0; dx < 16; dx++)
            {
                int i = Continent.Index(_cx + dx, _cy + dy);
                if (i < _h.FinalPop.Length && !_c.Ocean[i]) histRural += _h.FinalPop[i];
            }
        long settlementPop = 0;
        for (int k = 0; k < _h.Settlements.Count; k++)
        {
            var s = _h.Settlements[k];
            if (s.IsRuin) continue;
            int sx = s.Cell % Continent.Size - _cx, sy = s.Cell / Continent.Size - _cy;
            if (sx < 0 || sx >= 16 || sy < 0 || sy >= 16) continue;
            settlementPop += s.Pop;
            if (s.Pop >= _s.TownMinPop) { _histTowns.Add((k, s)); _townPop += s.Pop; }
            else _histVillages.Add((k, s));
        }
        long total = Math.Clamp(histRural * _s.RuralScalePct / 100 + settlementPop, _s.MinRegionPopulation, _s.MaxRegionPopulation);
        _ruralTotal = (int)Math.Max(2000, total - _townPop);
        _farmTarget = Math.Clamp((int)((long)_ruralTotal * 9 / 100 / _s.FarmsteadPop), _s.FarmsteadMin, _s.FarmsteadMax);
        int villagePool = Math.Max(0, _ruralTotal - _farmTarget * _s.FarmsteadPop);
        _villageTarget = Math.Clamp((villagePool + 95) / 190, _s.VillageMin, _s.VillageMax);
    }

    private string MakeName(int tile, EntityId id, string suffix = "")
    {
        _names ??= new NameGen(_h.Seed ^ 0x52454731UL, _c.Cultures);
        int cu = _c.CultureId[_warpCell[tile]];
        if (cu == Continent.None) cu = 0;
        var rs = new RngStream(_seed, 0, id.Value, StNames);
        string n = _names.Place(cu, ref rs) + suffix;
        for (int tries = 0; tries < 8 && _usedNames.Contains(n); tries++) n = _names.Place(cu, ref rs) + suffix;
        if (_usedNames.Contains(n)) n = $"{_names.Feature(0, ref rs)} {n}";
        int k = 2;
        string b = n;
        while (!_usedNames.Add(n)) n = $"{b} {k++}";
        return n;
    }

    private int SiteScore(int t)
    {
        if (_r.Water[t] != 0) return -1;
        int slope = _r.SlopePermille[t], fert = _r.Fertility[t];
        if (slope > 90 || fert < 15) return -1;
        if (_r.Biomes[t] is Biome.Mountain or Biome.Desert or Biome.Tundra or Biome.Marsh or Biome.Sea or Biome.Lake) return -1;
        int wd = WaterDist[t], rd = RiverDist[t];
        int s = fert * 3 - slope / 2 - _r.Forest[t] / 3;
        if (wd >= 2 && wd <= 12) s += 70 - Math.Abs(wd - 4) * 6;
        else if (wd > 30) s -= 40;
        if (rd <= 6) s += 20;
        return Math.Max(0, s);
    }

    private RegionSettlement NewSettlement(SettlementClass cls, int tx, int ty, EntityId id, string name, int pop)
    {
        var s = new RegionSettlement
        {
            Id = id, Name = name, Class = cls, TileX = tx, TileY = ty, CenterX = TileCenter(tx), CenterY = TileCenter(ty), TargetPop = pop,
        };
        int t = Idx(tx, ty);
        byte realm = _r.RealmMap[t];
        s.Realm = realm == Continent.None ? -1 : realm;
        s.Culture = s.Realm >= 0 && s.Realm < _h.Realms.Count ? _h.Realms[s.Realm].CultureId : 0;
        int bestD = int.MaxValue;
        for (int dy = -7; dy <= 7; dy++)
            for (int dx = -7; dx <= 7; dx++)
            {
                int nx = tx + dx, ny = ty + dy;
                if ((uint)nx >= N || (uint)ny >= N) continue;
                int nt = Idx(nx, ny);
                if ((_r.Water[nt] & (WaterFlags.River | WaterFlags.Stream)) == 0) continue;
                int d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; s.NearWaterway = WaterwayAt[nt]; }
            }
        s.OnRiver = bestD <= 49 && s.NearWaterway >= 0;
        if (!s.OnRiver) s.NearWaterway = -1;
        return s;
    }

    private int HouseholdsFor(SettlementClass cls, int pop) => cls switch
    {
        SettlementClass.Town => Math.Max(1, pop * 10 / _s.TownHouseholdSizeX10),
        SettlementClass.Village => Math.Max(1, pop * 10 / _s.VillageHouseholdSizeX10),
        _ => 1,
    };

    private int PickInCell(int relX, int relY, bool wantRiver, ulong salt)
    {
        int x0 = relX * RegionSettings.CellM / TileM, x1 = (relX + 1) * RegionSettings.CellM / TileM;
        int y0 = relY * RegionSettings.CellM / TileM, y1 = (relY + 1) * RegionSettings.CellM / TileM;
        int best = -1, bestS = -1, relaxed = -1, relaxedS = -1;
        for (int y = Math.Max(y0, 8); y < Math.Min(y1, N - 8); y++)
            for (int x = Math.Max(x0, 8); x < Math.Min(x1, N - 8); x++)
            {
                int t = Idx(x, y);
                int inner = Math.Min(Math.Min(x - x0, x1 - 1 - x), Math.Min(y - y0, y1 - 1 - y));
                int noise = (int)(DetRng.Mix(salt ^ (ulong)t) % 31);
                int sc = SiteScore(t);
                if (sc >= 0)
                {
                    sc += noise + Math.Min(inner, 6) * 4;
                    if (wantRiver && RiverDist[t] <= 4) sc += 50;
                    if (sc > bestS) { bestS = sc; best = t; }
                }
                else if ((_r.Water[t] & (WaterFlags.Sea | WaterFlags.Lake | WaterFlags.River | WaterFlags.Stream)) == 0)
                {
                    int rs = _r.Fertility[t] * 2 - _r.SlopePermille[t] + noise;
                    if (rs > relaxedS) { relaxedS = rs; relaxed = t; }
                }
            }
        return best >= 0 ? best : relaxed >= 0 ? relaxed : Idx(Math.Clamp(relX * RegionSettings.CellM / TileM + 15, 8, N - 9), Math.Clamp(relY * RegionSettings.CellM / TileM + 15, 8, N - 9));
    }

    private readonly List<(int X, int Y, int R)> _placed = [];
    private readonly Dictionary<EntityId, int> _ruinTile = [];

    private bool Clear(int x, int y, int r)
    {
        foreach (var p in _placed)
            if (Dist2(x, y, p.X, p.Y) < (long)Math.Max(r, p.R) * Math.Max(r, p.R)) return false;
        return true;
    }

    private static int[] Allocate(int total, int[] weights, int min, int max)
    {
        int n = weights.Length;
        var res = new int[n];
        var fixedv = new bool[n];
        long remaining = total;
        for (int iter = 0; iter < 6; iter++)
        {
            long wsum = 0;
            for (int i = 0; i < n; i++) if (!fixedv[i]) wsum += weights[i];
            if (wsum == 0) break;
            bool changed = false;
            long rem = remaining;
            for (int i = 0; i < n; i++)
            {
                if (fixedv[i]) continue;
                int v = (int)(rem * weights[i] / wsum);
                if (v < min) { res[i] = min; fixedv[i] = true; remaining -= min; changed = true; }
                else if (v > max) { res[i] = max; fixedv[i] = true; remaining -= max; changed = true; }
                else res[i] = v;
            }
            if (!changed) break;
        }
        return res;
    }

    private void PlaceSettlements()
    {
        var order = new List<RegionSettlement>();
        _siteScore = new int[Tiles];
        for (int t = 0; t < Tiles; t++) _siteScore[t] = SiteScore(t);

        // 0. Ruins from history keep their ground: no new village within 1.5 km.
        for (int k = 0; k < _h.Settlements.Count; k++)
        {
            var rs0 = _h.Settlements[k];
            if (!rs0.IsRuin || !CellInside(rs0.Cell)) continue;
            int t0 = PickInCell(rs0.Cell % Continent.Size - _cx, rs0.Cell / Continent.Size - _cy, false, rs0.Id.Value);
            _ruinTile[rs0.Id] = t0;
            _placed.Add((TileCenter(t0 % N), TileCenter(t0 / N), 1500));
        }
        // 1. Towns and villages the history placed here.
        foreach (var (hi, hs) in _histTowns)
        {
            int rx = hs.Cell % Continent.Size - _cx, ry = hs.Cell / Continent.Size - _cy;
            int t = PickInCell(rx, ry, hs.OnRiver, hs.Id.Value);
            var s = NewSettlement(SettlementClass.Town, t % N, t / N, hs.Id, hs.Name, hs.Pop);
            s.HistoryId = hs.Id;
            if (s.Realm < 0) s.Realm = hs.Realm;
            order.Add(s); _placed.Add((s.CenterX, s.CenterY, _s.TownClearanceM));
        }
        var histVillages = new List<RegionSettlement>();
        foreach (var (hi, hs) in _histVillages)
        {
            int rx = hs.Cell % Continent.Size - _cx, ry = hs.Cell / Continent.Size - _cy;
            int t = PickInCell(rx, ry, hs.OnRiver, hs.Id.Value);
            var s = NewSettlement(SettlementClass.Village, t % N, t / N, hs.Id, hs.Name, hs.Pop);
            s.HistoryId = hs.Id;
            if (s.Realm < 0) s.Realm = hs.Realm;
            histVillages.Add(s); _placed.Add((s.CenterX, s.CenterY, _s.VillageSpacingMinM));
        }
        order.AddRange(histVillages);

        // 2. New villages: best-candidate Poisson disk with a 3-4 km spacing radius per candidate.
        int need = Math.Max(0, _villageTarget - histVillages.Count);
        var newVillages = new List<RegionSettlement>();
        var cands = new List<(int T, int Score, int R)>();
        for (int y = 12; y < N - 12; y += 4)
            for (int x = 12; x < N - 12; x += 4)
            {
                int t = Idx(x, y);
                if (_siteScore[t] < 40) continue;
                int noise = (int)(DetRng.Mix(_seed ^ StVillage ^ ((ulong)t << 8)) % 120);
                int r = _s.VillageSpacingMinM + (int)(DetRng.Mix(_seed ^ StVillage ^ ((ulong)t << 20)) % (ulong)Math.Max(1, _s.VillageSpacingMaxM - _s.VillageSpacingMinM + 1));
                cands.Add((t, _siteScore[t] + noise, r));
            }
        cands.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : a.T.CompareTo(b.T));
        for (int pass = 0; pass < 3 && newVillages.Count < need; pass++)
        {
            int pct = pass == 0 ? 100 : pass == 1 ? 86 : 74;
            foreach (var c in cands)
            {
                if (newVillages.Count >= need) break;
                int x = c.T % N, y = c.T / N;
                int cxm = TileCenter(x), cym = TileCenter(y);
                int r = c.R * pct / 100;
                if (!Clear(cxm, cym, r)) continue;
                var id = _ids.Allocate(IdKind.Settlement);
                var s = NewSettlement(SettlementClass.Village, x, y, id, "", 0);
                newVillages.Add(s); _placed.Add((cxm, cym, r));
            }
        }
        order.AddRange(newVillages);

        // 3. Population: villages share what the towns and farmsteads leave of the rural total.
        var villages = new List<RegionSettlement>(histVillages); villages.AddRange(newVillages);
        int farmPop = _farmTarget * _s.FarmsteadPop;
        int villagePool = Math.Max(0, _ruralTotal - farmPop);
        int nv = villages.Count;
        if (nv > 0)
        {
            var w = new int[nv];
            for (int i = 0; i < nv; i++)
            {
                var v = villages[i];
                int pot = 0;
                for (int dy = -10; dy <= 10; dy += 2)
                    for (int dx = -10; dx <= 10; dx += 2)
                    {
                        int nx = v.TileX + dx, ny = v.TileY + dy;
                        if ((uint)nx < N && (uint)ny < N && dx * dx + dy * dy <= 100 && _siteScore[Idx(nx, ny)] >= 0 && _r.Fertility[Idx(nx, ny)] >= _s.MinFieldFertility) pot++;
                    }
                int noise = (int)(DetRng.Mix(_seed ^ StPop ^ v.Id.Value) % 41);
                w[i] = 50 + pot * 100 / 80 + noise;
                if (v.HistoryId != EntityId.None) w[i] = Math.Max(w[i], 60 + (int)((long)_h.GetSettlement(v.HistoryId)!.Pop * 100 / 190));
            }
            var pops = Allocate(villagePool, w, _s.VillagePopMin, _s.VillagePopMax);
            for (int i = 0; i < nv; i++) villages[i].TargetPop = pops[i];
        }

        // 4. Farmsteads: scattered holdings 0.6-2.5 km from a village or town, at least 900 m from each other.
        var anchors = new List<RegionSettlement>(order);
        var fc = new List<(int T, int Score)>();
        for (int y = 7; y < N - 7; y += 2)
            for (int x = 7; x < N - 7; x += 2)
            {
                int t = Idx(x, y);
                int sc = _siteScore[t];
                if (sc < 90 || _r.Fertility[t] < 35 || _r.Forest[t] > 70) continue;
                int cxm = TileCenter(x), cym = TileCenter(y);
                long nearest = long.MaxValue;
                foreach (var a in anchors) nearest = Math.Min(nearest, Dist2(cxm, cym, a.CenterX, a.CenterY));
                if (nearest < 700L * 700) continue;
                int dn = (int)Region.Isqrt(nearest);
                if (dn < 2600) sc += 60;
                int noise = (int)(DetRng.Mix(_seed ^ StFarm ^ ((ulong)t << 4)) % 160);
                fc.Add((t, sc + noise));
            }
        fc.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : a.T.CompareTo(b.T));
        var farms = new List<RegionSettlement>();
        foreach (var c in fc)
        {
            if (farms.Count >= _farmTarget) break;
            int x = c.T % N, y = c.T / N;
            int cxm = TileCenter(x), cym = TileCenter(y);
            bool ok = true;
            foreach (var f in farms) if (Dist2(cxm, cym, f.CenterX, f.CenterY) < (long)_s.FarmsteadSpacingM * _s.FarmsteadSpacingM) { ok = false; break; }
            if (!ok) continue;
            var id = _ids.Allocate(IdKind.Settlement);
            farms.Add(NewSettlement(SettlementClass.Farmstead, x, y, id, "", _s.FarmsteadPop));
        }
        order.AddRange(farms);

        foreach (var s in order)
        {
            if (s.Name.Length == 0) s.Name = MakeName(Idx(s.TileX, s.TileY), s.Id, s.Class == SettlementClass.Farmstead ? " Farm" : "");
            else _usedNames.Add(s.Name);
            s.Households = HouseholdsFor(s.Class, s.TargetPop);
            s.FieldAcresDemand = s.TargetPop * _s.AcresPerPersonX10 / 10;
            _r.Settlements.Add(s);
        }
    }

    // ---------- fiefs: manor + parcels; the region holds parts of 1-3 realms ----------
    private void AssignFiefs()
    {
        var S = _r.Settlements;
        for (int i = 0; i < S.Count; i++)
            if (S[i].Class == SettlementClass.Town) NewFief(i, $"Lordship of {S[i].Name}");
        var vil = new List<int>();
        for (int i = 0; i < S.Count; i++) if (S[i].Class == SettlementClass.Village) vil.Add(i);
        vil.Sort((a, b) => S[b].TargetPop != S[a].TargetPop ? S[b].TargetPop.CompareTo(S[a].TargetPop) : S[a].Id.CompareTo(S[b].Id));
        foreach (int i in vil)
        {
            if (S[i].FiefIndex >= 0) continue;
            int f = NewFief(i, $"Manor of {S[i].Name}");
            int extra = HashRange(StFief, (long)S[i].Id.Value, 0, 0, Math.Max(1, _s.FiefMaxVillages));
            for (int k = 0; k < extra; k++)
            {
                int best = -1; long bd = 6000L * 6000;
                foreach (int j in vil)
                {
                    if (S[j].FiefIndex >= 0 || S[j].Realm != S[i].Realm) continue;
                    long d = Dist2(S[i].CenterX, S[i].CenterY, S[j].CenterX, S[j].CenterY);
                    if (d < bd) { bd = d; best = j; }
                }
                if (best < 0) break;
                Join(f, best);
            }
        }
        // Farmsteads and any unattached village follow the nearest seat.
        for (int i = 0; i < S.Count; i++)
        {
            if (S[i].FiefIndex >= 0) continue;
            int best = -1; long bd = long.MaxValue;
            for (int j = 0; j < S.Count; j++)
            {
                if (S[j].Class == SettlementClass.Farmstead || S[j].FiefIndex < 0) continue;
                long d = Dist2(S[i].CenterX, S[i].CenterY, S[j].CenterX, S[j].CenterY);
                if (S[j].Realm != S[i].Realm) d += 4000L * 4000;
                if (d < bd) { bd = d; best = j; }
            }
            if (best >= 0) Join(S[best].FiefIndex, i);
        }
        foreach (var f in _r.Fiefs)
            foreach (int i in f.Settlements) { f.Households += S[i].Households; f.Pop += S[i].TargetPop; }
        // Fief map: nearest member settlement of the tile's own realm (fall back to any).
        var centres = new List<(int X, int Y, int Fief, int Realm)>();
        foreach (var s in S) if (s.Class != SettlementClass.Farmstead) centres.Add((s.CenterX, s.CenterY, s.FiefIndex, s.Realm));
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int t = Idx(x, y);
                if ((_r.Water[t] & WaterFlags.Sea) != 0) continue;
                int cxm = TileCenter(x), cym = TileCenter(y);
                int realm = _r.RealmMap[t] == Continent.None ? -1 : _r.RealmMap[t];
                long bd = long.MaxValue; int bf = -1;
                foreach (var c in centres)
                {
                    long d = Dist2(cxm, cym, c.X, c.Y);
                    if (c.Realm != realm) d += 6000L * 6000;
                    if (d < bd) { bd = d; bf = c.Fief; }
                }
                _r.FiefMap[t] = (ushort)(bf + 1);
            }
    }

    private int NewFief(int seat, string name)
    {
        var s = _r.Settlements[seat];
        var f = new Fief { Id = _ids.Allocate(IdKind.Other), Name = name, Realm = s.Realm, SeatSettlement = seat };
        f.Settlements.Add(seat);
        s.FiefIndex = _r.Fiefs.Count;
        _r.Fiefs.Add(f);
        return s.FiefIndex;
    }

    private void Join(int fief, int settlement)
    {
        _r.Fiefs[fief].Settlements.Add(settlement);
        _r.Settlements[settlement].FiefIndex = fief;
    }
}
