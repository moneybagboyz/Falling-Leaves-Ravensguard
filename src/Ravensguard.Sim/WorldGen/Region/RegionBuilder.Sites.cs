using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.WorldGen.Region;

internal sealed partial class RegionBuilder
{
    private readonly List<(int X, int Y)> _spots = [];

    private EntityId CauseOf(EntityId ev)
    {
        var e = _h.Chronicle.Get(ev);
        if (e == null) return EntityId.None;
        foreach (var c in e.Causes) if (c.EventId is { } id) return id;
        return EntityId.None;
    }

    private Site AddSite(SiteKind kind, string sub, string name, int tile)
    {
        var s = new Site
        {
            Id = _ids.Allocate(IdKind.Other), Kind = kind, SubKind = sub, Name = name,
            TileX = tile % N, TileY = tile / N, X = TileCenter(tile % N), Y = TileCenter(tile / N),
        };
        _r.Sites.Add(s);
        _spots.Add((s.X, s.Y));
        return s;
    }

    private int NearestSettlementM(int x, int y, out int index)
    {
        long best = long.MaxValue; index = -1;
        for (int i = 0; i < _r.Settlements.Count; i++)
        {
            var s = _r.Settlements[i];
            if (s.Class == SettlementClass.Farmstead) continue;
            long d = Dist2(x, y, s.CenterX, s.CenterY);
            if (d < best) { best = d; index = i; }
        }
        return best == long.MaxValue ? int.MaxValue : (int)Region.Isqrt(best);
    }

    private int NearestAnySettlementM(int x, int y)
    {
        long best = long.MaxValue;
        foreach (var s in _r.Settlements) best = Math.Min(best, Dist2(x, y, s.CenterX, s.CenterY));
        return best == long.MaxValue ? int.MaxValue : (int)Region.Isqrt(best);
    }

    private bool SpotFree(int x, int y, int minM)
    {
        foreach (var p in _spots) if (Dist2(x, y, p.X, p.Y) < (long)minM * minM) return false;
        return true;
    }

    private int CellTile(int cell, int salt)
    {
        int rx = cell % Continent.Size - _cx, ry = cell / Continent.Size - _cy;
        int x0 = rx * RegionSettings.CellM / TileM, y0 = ry * RegionSettings.CellM / TileM;
        for (int k = 0; k < 40; k++)
        {
            int x = Math.Clamp(x0 + 4 + (int)(Hash(StSite, cell, salt * 64 + k) % 24), 4, N - 5);
            int y = Math.Clamp(y0 + 4 + (int)(Hash(StSite, cell, salt * 64 + k + 32) % 24), 4, N - 5);
            int t = Idx(x, y);
            if (_r.Water[t] == 0 && _r.Built[t] == 0) return t;
        }
        return Idx(Math.Clamp(x0 + 15, 4, N - 5), Math.Clamp(y0 + 15, 4, N - 5));
    }

    /// <summary>Sites (plan §3b): everything that exists because of history is placed first, then natural places on suitable ground.</summary>
    private void BuildSites()
    {
        // Ruins from history, with their event ids.
        foreach (var hs in _h.Settlements)
        {
            if (!hs.IsRuin || !_ruinTile.TryGetValue(hs.Id, out int t)) continue;
            var s = AddSite(SiteKind.Ruin, hs.RuinKind, $"Ruins of {hs.Name}", t);
            s.HistoryEvent = hs.RuinEvent; s.CauseEvent = hs.RuinCauseEvent; s.HistorySettlement = hs.Id;
            s.Year = hs.RuinTick / 4; s.Levels = 1 + (int)(Hash(StSite, (long)hs.Id.Value, 1) % 3);
            s.Dead = hs.PeakPop;
        }
        // Old battlefields.
        foreach (var b in _h.Battlefields)
        {
            if (!CellInside(b.Cell)) continue;
            var s = AddSite(SiteKind.Battlefield, "battlefield", b.Name, CellTile(b.Cell, 3));
            s.HistoryEvent = b.Event; s.CauseEvent = CauseOf(b.Event); s.Dead = b.Dead; s.Year = b.Tick / 4;
        }
        // Lost and buried artefacts.
        foreach (var a in _h.Artefacts)
        {
            if (a.Status == ArtefactStatus.Held || a.Cell < 0 || !CellInside(a.Cell)) continue;
            var s = AddSite(SiteKind.Hoard, a.Status == ArtefactStatus.Buried ? "buried" : "lost", a.Name, CellTile(a.Cell, 5));
            s.Artefact = a.Id; s.HistoryEvent = a.LossEvent; s.CauseEvent = a.CreatedEvent; s.Year = a.LostTick / 4;
        }
        BuildVeinsAndMines();
        BuildCaves();
        BuildBarrows();
        BuildLairs();
    }

    private void BuildVeinsAndMines()
    {
        var inside = new List<(MineralVein V, int Tile)>();
        for (int k = 0; k < _c.Veins.Count; k++)
        {
            var v = _c.Veins[k];
            if (!CellInside(v.Cell)) continue;
            int t = CellTile(v.Cell, 100 + k % 7);
            inside.Add((v, t));
            int radius = Math.Clamp(1 + (int)Region.Isqrt(Math.Max(1, v.Tonnage) / 20), 2, 6);
            _r.Veins.Add(new RegionVein(v.Type, t % N, t / N, radius, v.Tonnage, v.Cell));
            int cx = t % N, cy = t / N;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if ((uint)nx < N && (uint)ny < N && dx * dx + dy * dy <= radius * radius) _r.Mineral[Idx(nx, ny)] = (byte)((int)v.Type + 1);
                }
        }
        // Mine collapses from history come first, then the richest metal or coal veins.
        var chosen = new List<(MineralVein V, int Tile, EntityId Ev)>();
        foreach (var (cell, ev) in _h.MineCollapses)
        {
            if (!CellInside(cell)) continue;
            var vi = inside.FirstOrDefault(x => x.V.Cell == cell);
            if (vi.V.Tonnage == 0 && vi.Tile == 0) continue;
            chosen.Add((vi.V, vi.Tile, ev));
        }
        int want = _s.MineMin + (int)(Hash(StSite, 4000) % (ulong)Math.Max(1, _s.MineMax - _s.MineMin + 1));
        bool IsMineral(MineralType m) => m is MineralType.Iron or MineralType.Copper or MineralType.Tin or MineralType.Silver or MineralType.Gold or MineralType.Coal or MineralType.Salt;
        var rest = inside.Where(x => IsMineral(x.V.Type) && chosen.All(c => c.V.Cell != x.V.Cell))
            .OrderByDescending(x => x.V.Tonnage).ThenBy(x => x.V.Cell).ToList();
        foreach (var x in rest) { if (chosen.Count >= want) break; chosen.Add((x.V, x.Tile, EntityId.None)); }
        if (chosen.Count < _s.MineMin)
            foreach (var x in inside.Where(x => x.V.Type != MineralType.Peat && chosen.All(c => c.V.Cell != x.V.Cell)).OrderByDescending(x => x.V.Tonnage).ThenBy(x => x.V.Cell))
            { if (chosen.Count >= _s.MineMin) break; chosen.Add((x.V, x.Tile, EntityId.None)); }
        foreach (var (v, t, ev) in chosen)
        {
            int near = NearestSettlementM(TileCenter(t % N), TileCenter(t / N), out int si);
            string place = si >= 0 ? _r.Settlements[si].Name : "the hills";
            var s = AddSite(SiteKind.Mine, v.Type.ToString().ToLowerInvariant(), $"{v.Type} mine near {place}", t);
            s.Mineral = v.Type; s.Tonnage = v.Tonnage;
            s.Levels = 2 + (int)(Hash(StSite, (long)v.Cell, 8) % 3);
            if (ev != EntityId.None) { s.HistoryEvent = ev; s.CauseEvent = CauseOf(ev); s.Mine = MineState.Collapsed; }
            else s.Mine = near < 6000 ? MineState.Active : MineState.Abandoned;
        }
    }

    private int Relief(int t, int r)
    {
        int x = t % N, y = t / N, lo = int.MaxValue, hi = int.MinValue;
        for (int dy = -r; dy <= r; dy += 2)
            for (int dx = -r; dx <= r; dx += 2)
            {
                int nx = Math.Clamp(x + dx, 0, N - 1), ny = Math.Clamp(y + dy, 0, N - 1);
                int h = _r.HeightCm[Idx(nx, ny)];
                lo = Math.Min(lo, h); hi = Math.Max(hi, h);
            }
        return hi - lo;
    }

    private void BuildCaves()
    {
        int want = _s.CaveMin + (int)(Hash(StSite, 4001) % (ulong)Math.Max(1, _s.CaveMax - _s.CaveMin + 1));
        var cands = new List<(int T, long Score)>();
        for (int y = 10; y < N - 10; y += 3)
            for (int x = 10; x < N - 10; x += 3)
            {
                int t = Idx(x, y);
                if (_r.Water[t] != 0 || _r.Built[t] != 0 || _fieldOwner[t] >= 0) continue;
                if (NearestAnySettlementM(TileCenter(x), TileCenter(y)) < 500) continue;
                long sc = Relief(t, 6) + _r.SlopePermille[t] * 20L + (long)(Hash(StSite, 4002, t) % 400);
                if (_r.Biomes[t] == Biome.Mountain) sc += 1500;
                cands.Add((t, sc));
            }
        cands.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.T.CompareTo(b.T));
        int made = 0;
        foreach (var (t, _) in cands)
        {
            if (made >= want) break;
            int x = TileCenter(t % N), y = TileCenter(t / N);
            if (!SpotFree(x, y, 1500)) continue;
            int village = NearestSettlementM(x, y, out int si);
            bool nearWater = WaterDist[t] <= 6;
            bool nearRoad = false;
            for (int dy = -8; dy <= 8 && !nearRoad; dy += 2)
                for (int dx = -8; dx <= 8; dx += 2)
                {
                    int nx = t % N + dx, ny = t / N + dy;
                    if ((uint)nx < N && (uint)ny < N && _r.Road[Idx(nx, ny)] >= (byte)RoadClass.Track) { nearRoad = true; break; }
                }
            int roll = (int)(Hash(StSite, 4003, t) % 100);
            string sub = nearWater && roll < 40 ? "river" : nearRoad && village > 1200 && roll < 70 ? "hideout" : village is > 1500 and < 4500 && roll < 45 ? "hermit" : roll < 80 ? "den" : "hideout";
            var s = AddSite(SiteKind.Cave, sub, $"{(sub == "river" ? "Underwater" : sub == "hideout" ? "Outlaw" : sub == "hermit" ? "Hermit's" : "Beast")} cave {made + 1}", t);
            s.Levels = 1 + (int)(Hash(StSite, 4004, t) % 3);
            made++;
        }
    }

    private void BuildBarrows()
    {
        int want = _s.BarrowMin + (int)(Hash(StSite, 4005) % (ulong)Math.Max(1, _s.BarrowMax - _s.BarrowMin + 1));
        var cands = new List<(int T, long Score)>();
        for (int y = 12; y < N - 12; y += 2)
            for (int x = 12; x < N - 12; x += 2)
            {
                int t = Idx(x, y);
                if (_r.Water[t] != 0 || _r.Built[t] != 0 || _fieldOwner[t] >= 0 || _r.Forest[t] > 85) continue;
                if (NearestAnySettlementM(TileCenter(x), TileCenter(y)) < 700) continue;
                int h = _r.HeightCm[t];
                long ring = 0; int cnt = 0; bool top = true;
                for (int dy = -4; dy <= 4 && top; dy += 2)
                    for (int dx = -4; dx <= 4; dx += 2)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nh = _r.HeightCm[Idx(x + dx, y + dy)];
                        if (nh > h) top = false;
                        ring += nh; cnt++;
                    }
                long prom = h - ring / Math.Max(1, cnt);
                long sc = prom * 4 + (top ? 2000 : 0) + (long)(Hash(StSite, 4006, t) % 300);
                cands.Add((t, sc));
            }
        cands.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.T.CompareTo(b.T));
        int made = 0;
        foreach (var (t, _) in cands)
        {
            if (made >= want) break;
            int x = TileCenter(t % N), y = TileCenter(t / N);
            if (!SpotFree(x, y, 2000)) continue;
            var s = AddSite(SiteKind.Barrow, "barrow", $"Barrow {made + 1}", t);
            s.Levels = 1;
            // A barrow near an old battlefield holds its dead.
            foreach (var b in _r.Sites.Where(q => q.Kind == SiteKind.Battlefield))
                if (Dist(x, y, b.X, b.Y) < 4500) { s.HistoryEvent = b.HistoryEvent; s.CauseEvent = b.CauseEvent; s.Dead = b.Dead; break; }
            made++;
        }
    }

    /// <summary>One to three monster lairs, always deep (2-3 levels) and tied to a legend in the history when the region holds one (plan §3b, §3f).</summary>
    private void BuildLairs()
    {
        int want = _s.LairMin + (int)(Hash(StSite, 4007) % (ulong)Math.Max(1, _s.LairMax - _s.LairMin + 1));
        var pool = _r.Sites.Where(s => s.Kind is SiteKind.Barrow or SiteKind.Cave).ToList();
        pool.Sort((a, b) =>
        {
            int da = NearestAnySettlementM(a.X, a.Y), db = NearestAnySettlementM(b.X, b.Y);
            return da != db ? db.CompareTo(da) : a.Id.CompareTo(b.Id);
        });
        var legends = _r.Sites.Where(s => s.Kind is SiteKind.Ruin or SiteKind.Battlefield).ToList();
        int made = 0; bool haveBarrow = false, haveCave = false;
        // One of each kind first, then whatever is most remote.
        var order = new List<Site>();
        var firstBarrow = pool.FirstOrDefault(s => s.Kind == SiteKind.Barrow); if (firstBarrow != null) order.Add(firstBarrow);
        var firstCave = pool.FirstOrDefault(s => s.Kind == SiteKind.Cave); if (firstCave != null) order.Add(firstCave);
        foreach (var s in pool) if (!order.Contains(s)) order.Add(s);
        foreach (var s in order)
        {
            if (made >= want) break;
            if (s.Kind == SiteKind.Barrow) haveBarrow = true; else haveCave = true;
            s.MonsterLair = true;
            s.LairKind = s.Kind == SiteKind.Barrow ? "draugr" : _r.Biomes[Idx(s.TileX, s.TileY)] is Biome.Marsh or Biome.RiverValley ? "bog_thing" : "cave_beast";
            s.Levels = 2 + (int)(Hash(StSite, (long)s.Id.Value, 9) % 2);
            if (s.HistoryEvent == EntityId.None && legends.Count > 0)
            {
                Site? best = null; long bd = long.MaxValue;
                foreach (var l in legends) { long d = Dist2(s.X, s.Y, l.X, l.Y); if (d < bd) { bd = d; best = l; } }
                if (best != null) { s.HistoryEvent = best.HistoryEvent; s.CauseEvent = best.CauseEvent; }
            }
            made++;
        }
        _ = haveBarrow; _ = haveCave;
    }
}
