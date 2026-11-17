using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.Region;

internal sealed partial class RegionBuilder
{
    private static int PseudoAngle(int dx, int dy)
    {
        int ax = Math.Abs(dx), ay = Math.Abs(dy), sum = ax + ay;
        if (sum == 0) return 0;
        int r = ay * 1000 / sum;
        if (dx >= 0 && dy >= 0) return r;
        if (dx < 0 && dy >= 0) return 2000 - r;
        if (dx < 0) return 2000 + r;
        return 4000 - r;
    }

    private int[] _fieldOwner = [];
    private byte[] _fieldNoise = [];

    /// <summary>
    /// Fields (plan §3a): every settlement claims about 2.3 arable acres per intended person (one acre per tile) by cost-ordered growth from its centre,
    /// preferring fertile ground, then the claimed tiles are grouped into parcels with owner placeholders and a three-field rotation slot.
    /// </summary>
    private void BuildFields()
    {
        var S = _r.Settlements; int n = S.Count;
        _fieldOwner = new int[Tiles]; Array.Fill(_fieldOwner, -1);
        _fieldNoise = new byte[Tiles];
        ulong fns = DetRng.Mix(_seed ^ 0xF1E1D5);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                _fieldNoise[Idx(x, y)] = (byte)(DetNoise.Fbm(fns, Fixed.FromRatio(_ox + x * TileM, 700), Fixed.FromRatio(_oy + y * TileM, 700), 3).Raw * 300 >> 16);
        var got = new int[n];
        var claimOrder = new List<int>[n];
        for (int i = 0; i < n; i++) claimOrder[i] = [];
        var visit = new int[Tiles]; int visitNo = 0;
        var dist = new int[Tiles]; var distStamp = new int[Tiles];

        int Cap(SettlementClass c) => c switch { SettlementClass.Town => 4500, SettlementClass.Village => 3000, _ => 1500 };
        void Pass(int minFert, int capPct)
        {
            visitNo++;
            var heap = new Heap();
            var active = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (got[i] >= S[i].FieldAcresDemand) continue;
                active[i] = true;
                int t = Idx(S[i].TileX, S[i].TileY);
                dist[t] = 0; distStamp[t] = visitNo;
                heap.Push(((long)0 << 30) | ((long)i << 18) | (uint)t);
            }
            while (heap.Count > 0)
            {
                long k = heap.Pop();
                int t = (int)(k & 0x3FFFF), o = (int)((k >> 18) & 0xFFF);
                int cost = (int)(k >> 30);
                if (visit[t] == visitNo) continue;
                visit[t] = visitNo;
                if (got[o] >= S[o].FieldAcresDemand) continue;
                if (_fieldOwner[t] < 0 && FieldEligible(t, minFert) && !(S[o].Class != SettlementClass.Farmstead && _r.Built[t] != 0))
                {
                    _fieldOwner[t] = o; got[o]++; claimOrder[o].Add(t);
                    if (got[o] >= S[o].FieldAcresDemand) continue;
                }
                int x = t % N, y = t / N;
                int cap = Math.Min(Cap(S[o].Class) * capPct / 100, 9000);
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + Dx8[d], ny = y + Dy8[d];
                    if ((uint)nx >= N || (uint)ny >= N) continue;
                    int nt = Idx(nx, ny);
                    if (visit[nt] == visitNo) continue;
                    byte w = _r.Water[nt];
                    if ((w & (WaterFlags.Sea | WaterFlags.Lake)) != 0) continue;
                    int mult;
                    if ((w & WaterFlags.River) != 0) mult = 600;
                    else if ((w & WaterFlags.Stream) != 0) mult = 300;
                    else if (_fieldOwner[nt] >= 0 && _fieldOwner[nt] != o) mult = 250;
                    else if (FieldEligible(nt, minFert)) mult = 100 + (100 - _r.Fertility[nt]) - (WaterDist[nt] <= 3 ? 25 : 0) - (_r.Road[nt] != 0 ? 20 : 0);
                    else mult = 260;
                    mult += _fieldNoise[nt] + (int)(DetRng.Mix(_seed ^ StField ^ ((ulong)nt << 3)) % 30);
                    if (mult < 60) mult = 60;
                    int nc = cost + (d & 1) * 0 + (((d & 1) == 0 ? TileM : TileM * 141 / 100) * mult / 100);
                    if (nc > cap) continue;
                    if (distStamp[nt] == visitNo && dist[nt] <= nc) continue;
                    distStamp[nt] = visitNo; dist[nt] = nc;
                    heap.Push(((long)nc << 30) | ((long)o << 18) | (uint)nt);
                }
            }
        }
        Pass(_s.MinFieldFertility, 100);
        Pass(Math.Max(15, _s.MinFieldFertility - 10), 165);
        Pass(10, 250);

        for (int i = 0; i < n; i++)
        {
            S[i].FieldAcres = got[i];
            if (got[i] * 100 < S[i].FieldAcresDemand * 85 && S[i].Class != SettlementClass.Farmstead)
            {
                // Too little arable land: the settlement is smaller than the history's carrying capacity guess.
                int pop = Math.Max(S[i].Class == SettlementClass.Town ? 300 : 40, got[i] * 10 / _s.AcresPerPersonX10);
                if (pop < S[i].TargetPop) S[i].TargetPop = pop;
                S[i].FieldAcresDemand = S[i].TargetPop * _s.AcresPerPersonX10 / 10;
                S[i].Households = Math.Min(S[i].Households, HouseholdsFor(S[i].Class, S[i].TargetPop));
            }
        }

        BuildParcels(claimOrder);
        foreach (var f in _r.Fiefs) { f.Acres = 0; f.Households = 0; f.Pop = 0; foreach (int i in f.Settlements) { f.Acres += S[i].FieldAcres; f.Households += S[i].Households; f.Pop += S[i].TargetPop; } }
        // Cleared land carries no forest.
        for (int t = 0; t < Tiles; t++)
            if (_fieldOwner[t] >= 0 || _r.Built[t] != 0) _r.Forest[t] = 0;
    }

    private void BuildParcels(List<int>[] claimOrder)
    {
        var S = _r.Settlements; int n = S.Count;
        var parcelOf = new int[Tiles]; Array.Fill(parcelOf, -1);
        var fieldsOut = new List<Field>();
        var tilesOfParcel = new List<List<int>>();
        for (int o = 0; o < n; o++)
        {
            var tiles = claimOrder[o];
            if (tiles.Count == 0) continue;
            tiles.Sort();
            int first = tilesOfParcel.Count;
            var queue = new Queue<int>();
            foreach (int seed in tiles)
            {
                if (parcelOf[seed] >= 0) continue;
                int target = _s.ParcelMinTiles + (int)(Hash(StField, (long)S[o].Id.Value, seed) % (ulong)Math.Max(1, _s.ParcelMaxTiles - _s.ParcelMinTiles + 1));
                var list = new List<int>();
                int id = tilesOfParcel.Count;
                tilesOfParcel.Add(list);
                queue.Clear(); queue.Enqueue(seed); parcelOf[seed] = id;
                while (queue.Count > 0 && list.Count < target)
                {
                    int t = queue.Dequeue(); list.Add(t);
                    int x = t % N, y = t / N;
                    for (int d = 0; d < 8; d += 2)
                    {
                        int nx = x + Dx8[d], ny = y + Dy8[d];
                        if ((uint)nx >= N || (uint)ny >= N) continue;
                        int nt = Idx(nx, ny);
                        if (_fieldOwner[nt] != o || parcelOf[nt] >= 0) continue;
                        parcelOf[nt] = id; queue.Enqueue(nt);
                    }
                }
                // Tiles queued but beyond the target go back to the pool.
                while (queue.Count > 0) parcelOf[queue.Dequeue()] = -1;
            }
            // Merge slivers into a neighbouring parcel of the same holder.
            for (int p = first; p < tilesOfParcel.Count; p++)
            {
                var list = tilesOfParcel[p];
                if (list.Count == 0 || list.Count >= _s.ParcelMinTiles) continue;
                int bestP = -1, bestSize = int.MaxValue;
                foreach (int t in list)
                {
                    int x = t % N, y = t / N;
                    for (int d = 0; d < 8; d += 2)
                    {
                        int nx = x + Dx8[d], ny = y + Dy8[d];
                        if ((uint)nx >= N || (uint)ny >= N) continue;
                        int q = parcelOf[Idx(nx, ny)];
                        if (q < 0 || q == p || tilesOfParcel[q].Count == 0) continue;
                        if (tilesOfParcel[q].Count < bestSize || (tilesOfParcel[q].Count == bestSize && q < bestP)) { bestSize = tilesOfParcel[q].Count; bestP = q; }
                    }
                }
                if (bestP < 0) continue;
                foreach (int t in list) { parcelOf[t] = bestP; tilesOfParcel[bestP].Add(t); }
                list.Clear();
            }

            // Slots by angle around the settlement so the three great fields are contiguous sectors.
            var plist = new List<(int P, int Angle)>();
            int total = 0;
            for (int p = first; p < tilesOfParcel.Count; p++)
            {
                var list = tilesOfParcel[p];
                if (list.Count == 0) continue;
                long sx = 0, sy = 0; foreach (int t in list) { sx += t % N; sy += t / N; }
                int cxp = (int)(sx / list.Count), cyp = (int)(sy / list.Count);
                // Great fields: sorted by a slow noise field (plus a little of the bearing) so each third is an irregular contiguous block.
                int key = (int)(DetNoise.Fbm(DetRng.Mix(_seed ^ 0x3F1E1D), Fixed.FromRatio(_ox + cxp * TileM, 1500), Fixed.FromRatio(_oy + cyp * TileM, 1500), 2).Raw)
                          + PseudoAngle(cxp - S[o].TileX, cyp - S[o].TileY) * 4;
                plist.Add((p, key));
                total += list.Count;
            }
            plist.Sort((a, b) => a.Angle != b.Angle ? a.Angle.CompareTo(b.Angle) : a.P.CompareTo(b.P));
            // Tenant lots for this settlement (longhouses hold more land).
            var plan = S[o].Plan;
            var holders = new List<int>();
            foreach (var l in plan.Lots)
            {
                if (l.Dwelling == DwellingType.Longhouse) { holders.Add(l.Index); holders.Add(l.Index); }
                else if (l.Dwelling is DwellingType.Cottage or DwellingType.Burgage) holders.Add(l.Index);
            }
            if (holders.Count == 0) holders.Add(0);
            int cum = 0, holderCursor = 0;
            var slotOf = new Dictionary<int, byte>();
            var ordered = new List<(int P, byte Slot)>();
            foreach (var (p, _) in plist)
            {
                int sz = tilesOfParcel[p].Count;
                byte slot = (byte)Math.Min(2, (cum + sz / 2) * 3 / Math.Max(1, total));
                cum += sz;
                ordered.Add((p, slot));
            }
            // Tenants walk the slot-major order so each holding spans all three fields.
            var tenantOrder = ordered.OrderBy(q => q.Slot).ThenBy(q => q.P).ToList();
            var owners = new Dictionary<int, ParcelOwner>();
            foreach (var (p, slot) in tenantOrder)
            {
                int roll = (int)(Hash(StField, (long)S[o].Id.Value, 10_000 + p) % 100);
                ParcelOwner po;
                if (S[o].Class == SettlementClass.Farmstead) po = new ParcelOwner(OwnerKind.Freeholder, S[o].FiefIndex, 0);
                else if (roll < _s.DemesnePct && S[o].FiefIndex >= 0) po = new ParcelOwner(OwnerKind.Demesne, S[o].FiefIndex, _r.Fiefs[S[o].FiefIndex].SeatSettlement == o ? plan.ManorLot : -1);
                else if (roll < _s.DemesnePct + _s.GlebePct) po = new ParcelOwner(OwnerKind.Glebe, S[o].FiefIndex, plan.ChurchLot);
                else
                {
                    int lot = holders[holderCursor++ % holders.Count];
                    po = new ParcelOwner(S[o].Class == SettlementClass.Town ? OwnerKind.Burgher : OwnerKind.Tenant, S[o].FiefIndex, lot);
                }
                owners[p] = po;
            }
            foreach (var (p, slot) in ordered)
            {
                var list = tilesOfParcel[p];
                list.Sort();
                var parcel = new Parcel
                {
                    Id = _ids.Allocate(IdKind.Parcel), Settlement = o, Owner = owners[p], Slot = slot,
                    FieldStart = fieldsOut.Count, FieldCount = list.Count,
                };
                int idx = _r.Parcels.Count;
                long fsum = 0, sx = 0, sy = 0;
                foreach (int t in list)
                {
                    byte fert = _r.Fertility[t];
                    var crop = slot == 2 ? Crop.Fallow : slot == 0 ? (fert >= 55 ? Crop.Wheat : Crop.Rye) : PickSpring(t);
                    fieldsOut.Add(new Field { Id = _ids.Allocate(IdKind.Field), Tile = t, Parcel = idx, Fertility = fert, Slot = slot, Crop = crop });
                    _r.ParcelMap[t] = idx + 1;
                    fsum += fert; sx += t % N; sy += t / N;
                }
                parcel.AvgFertility = (int)(fsum / list.Count);
                parcel.TileX = (int)(sx / list.Count); parcel.TileY = (int)(sy / list.Count);
                _r.Parcels.Add(parcel);
                S[o].Parcels.Add(idx);
            }
        }
        _r.Fields = [.. fieldsOut];
    }

    private Crop PickSpring(int t)
    {
        int r = (int)(Hash(StField, 77, t) % 100);
        return r < 45 ? Crop.Barley : r < 70 ? Crop.Oats : r < 85 ? Crop.Peas : Crop.Beans;
    }
}
