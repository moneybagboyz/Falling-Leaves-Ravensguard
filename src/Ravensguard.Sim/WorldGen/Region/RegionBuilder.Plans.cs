using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.Region;

internal sealed partial class RegionBuilder
{
    private struct Slot
    {
        public int U0, V0, W, D, Ratio;
        public byte Type; // 0 cottage, 1 longhouse, 2 burgage
        public int Side;
        public long Rank;
    }

    private static string Blueprint(LotUse u) => u switch
    {
        LotUse.Cottage => "peasant_cottage", LotUse.Longhouse => "longhouse", LotUse.Burgage => "burgage_townhouse", LotUse.Manor => "manor_hall",
        LotUse.Chapel => "chapel", LotUse.Smithy => "smithy", LotUse.Tavern => "tavern", LotUse.Barn => "barn", LotUse.Granary => "granary", LotUse.Church => "parish_church", LotUse.Mill => "watermill", _ => "",
    };
    private static DwellingType DwellingOf(LotUse u) => u switch
    {
        LotUse.Cottage => DwellingType.Cottage, LotUse.Longhouse => DwellingType.Longhouse, LotUse.Burgage => DwellingType.Burgage, LotUse.Manor => DwellingType.Manor, _ => DwellingType.None,
    };
    private static byte WealthOf(LotUse u) => u switch { LotUse.Cottage => 0, LotUse.Longhouse => 1, LotUse.Burgage => 2, LotUse.Manor => 2, LotUse.Church => 2, LotUse.Chapel => 1, _ => 1 };

    private void BuildPlans()
    {
        for (int i = 0; i < _r.Settlements.Count; i++)
        {
            var s = _r.Settlements[i];
            if (s.Class == SettlementClass.Farmstead) PlanFarmstead(s); else PlanSettlement(i, s);
            var plan = s.Plan;
            foreach (var l in plan.Lots) MarkBuilt(l.Rect);
            if (plan.Square.W > 0) MarkBuilt(plan.Square);
            foreach (var st in plan.Streets)
                MarkBuilt(new RectM(Math.Min(st.A.X, st.B.X) - st.WidthM / 2, Math.Min(st.A.Y, st.B.Y) - st.WidthM / 2, Math.Abs(st.A.X - st.B.X) + st.WidthM, Math.Abs(st.A.Y - st.B.Y) + st.WidthM));
        }
    }

    private void MarkBuilt(RectM r)
    {
        int x0 = Math.Clamp(r.X / TileM, 0, N - 1), x1 = Math.Clamp((r.Right - 1) / TileM, 0, N - 1);
        int y0 = Math.Clamp(r.Y / TileM, 0, N - 1), y1 = Math.Clamp((r.Bottom - 1) / TileM, 0, N - 1);
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) _r.Built[Idx(x, y)] = 1;
    }

    private bool PointOk(int x, int y)
    {
        if (x < 96 || y < 96 || x >= Region.Meters - 96 || y >= Region.Meters - 96) return false;
        int t = Idx(x / TileM, y / TileM);
        return _r.Water[t] == 0 && _r.SlopePermille[t] <= 140;
    }

    private bool LotOk(RectM r)
        => PointOk(r.X, r.Y) && PointOk(r.Right - 1, r.Y) && PointOk(r.X, r.Bottom - 1) && PointOk(r.Right - 1, r.Bottom - 1)
           && PointOk(r.CenterX, r.CenterY) && PointOk(r.CenterX, r.Y) && PointOk(r.CenterX, r.Bottom - 1) && PointOk(r.X, r.CenterY) && PointOk(r.Right - 1, r.CenterY);

    private static RectM ToWorld(bool horiz, int cx, int cy, int u0, int v0, int du, int dv)
        => horiz ? new RectM(cx + u0, cy + v0, du, dv) : new RectM(cx + v0, cy + u0, dv, du);

    private static byte FacingOf(bool horiz, int side) => horiz ? (byte)(side > 0 ? 0 : 2) : (byte)(side > 0 ? 3 : 1);

    private bool HorizontalFor(RegionSettlement s)
    {
        int t = Idx(s.TileX, s.TileY);
        int gx = 0, gy = 0;
        if (s.TileX > 1 && s.TileX < N - 2 && s.TileY > 1 && s.TileY < N - 2)
        {
            gx = Math.Abs(_r.HeightCm[t + 2] - _r.HeightCm[t - 2]);
            gy = Math.Abs(_r.HeightCm[t + 2 * N] - _r.HeightCm[t - 2 * N]);
        }
        if (Math.Abs(gx - gy) < 40) return (Hash(StPlan, (long)s.Id.Value) & 1) == 0;
        return gx < gy; // street along the contour
    }

    private void PlanSettlement(int index, RegionSettlement s)
    {
        var plan = s.Plan = new SettlementPlan();
        bool town = s.Class == SettlementClass.Town;
        bool horiz = plan.Horizontal = HorizontalFor(s);
        int cx = s.CenterX, cy = s.CenterY;
        int sw = town ? 6 : 5, D = town ? 30 : 40, pitch = 2 * D + sw;
        int sqU = town ? 36 : 38, sqV = town ? 18 : 20; // a modest market place (or a green), not a parade ground
        int pop = s.TargetPop;
        bool seat = s.FiefIndex >= 0 && _r.Fiefs[s.FiefIndex].SeatSettlement == index;

        int tavern = town ? Math.Max(1, pop / 350) : pop >= 150 ? 1 + pop / 450 : 0;
        int smithy = town ? Math.Max(1, pop / 400) : pop >= 120 ? 1 + pop / 400 : 0;
        int granary = town ? pop / 600 : pop / 260;
        int barn = seat && !town ? 1 : 0;
        var roles = new List<LotUse>();
        for (int k = 0; k < tavern; k++) roles.Add(LotUse.Tavern);
        for (int k = 0; k < smithy; k++) roles.Add(LotUse.Smithy);
        for (int k = 0; k < granary; k++) roles.Add(LotUse.Granary);
        for (int k = 0; k < barn; k++) roles.Add(LotUse.Barn);

        int dwellNeeded = Math.Max(1, s.Households - (seat ? 1 : 0));
        int avgW = town ? 11 : 20;
        int need = dwellNeeded + roles.Count;
        int rest = (int)Region.Isqrt((long)need * avgW * D * 12 / 10 * 100 / 220);
        int umax = rest * 2 + 80;

        var reserved = new List<RectM>(); // local (u, v) coordinates stored as (X=u0, Y=v0, W=du, H=dv)
        var sq = new RectM(-sqU / 2, -sqV / 2, sqU, sqV);
        reserved.Add(sq);
        plan.Square = ToWorld(horiz, cx, cy, sq.X, sq.Y, sq.W, sq.H);

        var lots = new List<Lot>();
        void AddLot(LotUse use, RectM world) { lots.Add(new Lot(lots.Count, use, DwellingOf(use), Blueprint(use), world, 0, WealthOf(use))); }

        // Church or chapel next to the square on the far side of the street.
        {
            int cw = town ? 32 : 22, ch = town ? 36 : 22;
            int side = (Hash(StPlan, (long)s.Id.Value, 1) & 1) == 0 ? 1 : -1;
            for (int attempt = 0; attempt < 2; attempt++, side = -side)
            {
                var loc = side > 0 ? new RectM(-cw / 2, sqV / 2 + 2, cw, ch) : new RectM(-cw / 2, -sqV / 2 - 2 - ch, cw, ch);
                var wr = ToWorld(horiz, cx, cy, loc.X, loc.Y, loc.W, loc.H);
                if (!LotOk(wr)) continue;
                reserved.Add(loc);
                plan.ChurchLot = lots.Count;
                lots.Add(new Lot(lots.Count, town ? LotUse.Church : LotUse.Chapel, DwellingType.None, town ? "parish_church" : "chapel", wr, FacingOf(horiz, -side), 2));
                break;
            }
        }
        // Manor on the edge of the settlement.
        if (seat)
        {
            int mw = town ? 56 : 46, mh = town ? 44 : 38;
            int bestScore = int.MinValue; RectM bestLoc = default; bool found = false;
            int rot = (int)(Hash(StPlan, (long)s.Id.Value, 2) % 8);
            for (int k = 0; k < 8; k++)
            {
                int dir = (k + rot) % 8;
                int rr = rest + 30 + mw / 2;
                int u = dir switch { 0 or 1 or 7 => rr, 3 or 4 or 5 => -rr, _ => 0 };
                int v = dir switch { 1 or 2 or 3 => rr * 3 / 4, 5 or 6 or 7 => -rr * 3 / 4, _ => 0 };
                if (dir == 0 || dir == 4) v = 0;
                var loc = new RectM(u - mw / 2, v - mh / 2, mw, mh);
                var wr = ToWorld(horiz, cx, cy, loc.X, loc.Y, loc.W, loc.H);
                if (!LotOk(wr)) continue;
                bool clash = false;
                foreach (var rsv in reserved) if (rsv.Overlaps(loc)) clash = true;
                if (clash) continue;
                int sc = _siteScore[Idx(wr.CenterX / TileM, wr.CenterY / TileM)] - k * 8;
                if (sc > bestScore) { bestScore = sc; bestLoc = loc; found = true; }
            }
            if (found)
            {
                reserved.Add(bestLoc);
                var wr = ToWorld(horiz, cx, cy, bestLoc.X, bestLoc.Y, bestLoc.W, bestLoc.H);
                plan.ManorLot = lots.Count;
                int side = bestLoc.Y + bestLoc.H / 2 >= 0 ? 1 : -1;
                lots.Add(new Lot(lots.Count, LotUse.Manor, DwellingType.Manor, "manor_hall", wr, FacingOf(horiz, -side), 2));
                _r.Fiefs[s.FiefIndex].ManorLot = plan.ManorLot;
            }
        }
        // Mill on the nearest bank when the settlement sits on running water.
        if (s.OnRiver && s.NearWaterway >= 0 && (town || pop >= 100))
        {
            int bestD = int.MaxValue, bx = 0, by = 0;
            for (int dy = -9; dy <= 9; dy++)
                for (int dx = -9; dx <= 9; dx++)
                {
                    int nx = s.TileX + dx, ny = s.TileY + dy;
                    if ((uint)nx >= N || (uint)ny >= N) continue;
                    int nt = Idx(nx, ny);
                    if ((_r.Water[nt] & (WaterFlags.River | WaterFlags.Stream)) == 0) continue;
                    int d = dx * dx + dy * dy;
                    if (d < 9 || d >= bestD) continue; // keep off the very centre
                    bestD = d; bx = nx; by = ny;
                }
            if (bestD != int.MaxValue)
            {
                // Stand on the dry bank tile beside the water, nearest the settlement.
                int mx = bx, my = by, md = int.MaxValue;
                for (int d = 0; d < 8; d++)
                {
                    int nx = bx + Dx8[d], ny = by + Dy8[d];
                    if ((uint)nx >= N || (uint)ny >= N || _r.Water[Idx(nx, ny)] != 0) continue;
                    int dd = (nx - s.TileX) * (nx - s.TileX) + (ny - s.TileY) * (ny - s.TileY);
                    if (dd < md) { md = dd; mx = nx; my = ny; }
                }
                var wr = new RectM(TileCenter(mx) - 9, TileCenter(my) - 7, 18, 14);
                int du = horiz ? wr.X - cx : wr.Y - cy, dv = horiz ? wr.Y - cy : wr.X - cx;
                var loc = horiz ? new RectM(du, dv, 18, 14) : new RectM(du, dv, 14, 18);
                bool clash = false;
                foreach (var rsv in reserved) if (rsv.Overlaps(loc)) clash = true;
                if (!clash && wr.X > 96 && wr.Y > 96 && wr.Right < Region.Meters - 96 && wr.Bottom < Region.Meters - 96)
                {
                    reserved.Add(loc);
                    plan.MillLot = lots.Count;
                    lots.Add(new Lot(lots.Count, LotUse.Mill, DwellingType.None, "watermill", wr, 0, 1));
                }
            }
        }
        // Cross lanes run perpendicular to the main street, every so often, never through the market place or over a reserved plot.
        int lw = town ? 4 : 3;
        var lanes = new List<int>(); // signed positions along the main street
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            int lu = sqU / 2 + 8 + (int)(Hash(StPlan, (long)s.Id.Value, 40 + sgn) % 10);
            for (int k = 0; lu < umax; k++)
            {
                var a = new RectM(sgn > 0 ? lu - lw / 2 : -lu - lw / 2, -umax, lw, 2 * umax);
                bool ok = true;
                foreach (var rsv in reserved) if (rsv.Overlaps(a)) ok = false;
                if (ok) { lanes.Add(sgn * lu); reserved.Add(a); }
                lu += (town ? 44 : 64) + (int)(Hash(StPlan, (long)s.Id.Value, 60 + k * 2 + sgn + 1) % 36);
            }
        }
        // Slots along parallel streets (burgage plots: long and narrow, the house at the street, the yard behind).
        var slots = new List<Slot>();
        int jmax = umax / pitch + 1;
        double vBias = town ? 1.6 : 5.0; // a village is a long row before it grows a second street
        void MakeSlot(long key, int u, int dir, int vlo, int depth, int side, out Slot sl, out int w)
        {
            int hv = (int)(Hash(StLot, (long)s.Id.Value, key) % 100);
            int uc = dir * (u + 8), vcen = vlo + depth / 2;
            int dist = (int)Region.Isqrt((long)uc * uc + (long)(vcen * vBias) * (long)(vcen * vBias));
            int ratio = Math.Min(100, dist * 100 / Math.Max(1, rest * 3 / 2));
            byte type;
            if (town) type = (byte)(hv < 82 - ratio * 40 / 100 ? 2 : 0);
            else type = (byte)(hv < 22 + (100 - ratio) / 8 ? 1 : 0);
            w = type switch { 2 => 10, 1 => 22, _ => town ? 12 : 16 };
            int u0 = dir > 0 ? u : -(u + w);
            long noise = (long)(Hash(StLot, (long)s.Id.Value, key + 7) % 16);
            sl = new Slot { U0 = u0, V0 = vlo, W = w, D = depth, Type = type, Side = side, Ratio = ratio, Rank = (long)dist * 16 + noise };
        }
        // plots facing the market place / green
        {
            int sd = town ? 22 : 26;
            for (int side = 1; side >= -1; side -= 2)
            {
                int vlo = side > 0 ? sqV / 2 : -sqV / 2 - sd;
                for (int dir = 1; dir >= -1; dir -= 2)
                    for (int u = 0, k = 0; u < sqU / 2; k++)
                    {
                        MakeSlot(0x7000000L + ((long)(side + 1) << 12) + ((long)(dir + 1) << 8) + k, u, dir, vlo, sd, side, out var sl, out int w);
                        slots.Add(sl); u += w;
                    }
            }
        }
        for (int j = -jmax; j <= jmax; j++)
        {
            int vs = j * pitch;
            for (int side = 1; side >= -1; side -= 2)
            {
                int vlo = side > 0 ? vs + sw / 2 : vs - sw / 2 - D;
                for (int dir = 1; dir >= -1; dir -= 2)
                {
                    int u = 0;
                    for (int k = 0; u < umax; k++)
                    {
                        long key = ((long)(j + 64) << 24) | ((long)(side + 1) << 20) | ((long)(dir + 1) << 16) | (uint)k;
                        MakeSlot(key, u, dir, vlo, D, side, out var sl, out int w);
                        bool moved = false;
                        foreach (int l in lanes) { int lu = Math.Abs(l); if ((l > 0) == (dir > 0) && u < lu + lw / 2 && u + w > lu - lw / 2) { u = lu + lw / 2; moved = true; break; } }
                        if (moved) continue;
                        slots.Add(sl);
                        u += w;
                    }
                }
            }
        }
        slots.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.U0 != b.U0 ? a.U0.CompareTo(b.U0) : a.V0.CompareTo(b.V0));
        var chosen = new List<(Slot S, RectM World)>();
        var taken = new List<RectM>(); // chosen local rects (overlap only possible between streets of neighbouring blocks, which never share space)
        foreach (var sl in slots)
        {
            if (chosen.Count >= need) break;
            var loc = new RectM(sl.U0, sl.V0, sl.W, sl.D);
            bool clash = false;
            foreach (var rsv in reserved) if (rsv.Overlaps(loc)) { clash = true; break; }
            if (clash) continue;
            bool taken2 = false;
            foreach (var (_, tw) in chosen) if (tw.Overlaps(ToWorld(horiz, cx, cy, sl.U0, sl.V0, sl.W, sl.D))) { taken2 = true; break; }
            if (taken2) continue;
            var wr = ToWorld(horiz, cx, cy, sl.U0, sl.V0, sl.W, sl.D);
            if (!LotOk(wr)) continue;
            chosen.Add((sl, wr));
        }
        // Roles take slots spread over the inner part of the settlement.
        var roleAt = new Dictionary<int, LotUse>();
        for (int k = 0; k < roles.Count && k < chosen.Count; k++)
        {
            int rank = Math.Min(chosen.Count - 1, 2 + k * Math.Max(3, chosen.Count / (roles.Count + 1) / 2) + (int)(Hash(StLot, (long)s.Id.Value, 900 + k) % 3));
            while (roleAt.ContainsKey(rank) && rank + 1 < chosen.Count) rank++;
            roleAt[rank] = roles[k];
        }
        for (int k = 0; k < chosen.Count; k++)
        {
            var (sl, wr) = chosen[k];
            LotUse use = roleAt.TryGetValue(k, out var ru) ? ru : sl.Type switch { 2 => LotUse.Burgage, 1 => LotUse.Longhouse, _ => LotUse.Cottage };
            byte wealth = WealthOf(use);
            // the well-off live near the market, the poor at the edge
            if (use == LotUse.Burgage) wealth = (byte)(sl.Ratio < 35 ? 2 : 1);
            else if (use == LotUse.Cottage && town) wealth = (byte)(sl.Ratio >= 60 ? 0 : 1);
            lots.Add(new Lot(lots.Count, use, DwellingOf(use), Blueprint(use), wr, FacingOf(horiz, sl.Side), wealth));
        }
        int dwellings = lots.Count(l => l.Dwelling is DwellingType.Cottage or DwellingType.Longhouse or DwellingType.Burgage);
        if (dwellings < dwellNeeded)
        {
            // Terrain could not hold every house: the people who would have lived there are not part of this place.
            int shortfall = dwellNeeded - dwellings;
            s.Households = Math.Max(1, s.Households - shortfall);
            s.TargetPop = Math.Max(20, s.TargetPop - (int)((long)shortfall * (town ? _s.TownHouseholdSizeX10 : _s.VillageHouseholdSizeX10) / 10));
            s.FieldAcresDemand = s.TargetPop * _s.AcresPerPersonX10 / 10;
        }
        plan.Lots.AddRange(lots);

        // Streets: main street through the middle, parallel streets and the cross lane, trimmed to their lots.
        int minU = 0, maxU = 0, minV = 0, maxV = 0;
        var perStreet = new Dictionary<int, (int Lo, int Hi)>();
        foreach (var (sl, _) in chosen)
        {
            int vs = sl.Side > 0 ? sl.V0 - sw / 2 : sl.V0 + D + sw / 2;
            int j = (int)Math.Round(vs / (double)pitch);
            var cur = perStreet.TryGetValue(j, out var c) ? c : (int.MaxValue, int.MinValue);
            perStreet[j] = (Math.Min(cur.Item1, sl.U0), Math.Max(cur.Item2, sl.U0 + sl.W));
            minU = Math.Min(minU, sl.U0); maxU = Math.Max(maxU, sl.U0 + sl.W);
            minV = Math.Min(minV, sl.V0); maxV = Math.Max(maxV, sl.V0 + D);
        }
        minU = Math.Min(minU, -sqU / 2 - 20); maxU = Math.Max(maxU, sqU / 2 + 20);
        perStreet[0] = (minU, maxU);
        PointM P(int u, int v) => horiz ? new PointM(cx + u, cy + v) : new PointM(cx + v, cy + u);
        foreach (var kv in perStreet.OrderBy(k => k.Key))
            plan.Streets.Add(new Street(P(kv.Value.Lo, kv.Key * pitch), P(kv.Value.Hi, kv.Key * pitch), sw));
        int jlo = perStreet.Keys.Min(), jhi = perStreet.Keys.Max();
        if (jhi > jlo || town)
            foreach (int l in lanes)
            {
                int ext = l > 0 ? maxU : -minU;
                if (Math.Abs(l) > ext - 6) continue;
                plan.Streets.Add(new Street(P(l, jlo * pitch), P(l, jhi * pitch), lw));
            }
        plan.RadiusM = 0;
        foreach (var l in plan.Lots) plan.RadiusM = Math.Max(plan.RadiusM, Dist(cx, cy, l.Rect.CenterX, l.Rect.CenterY));
    }

    private void PlanFarmstead(RegionSettlement s)
    {
        var plan = s.Plan = new SettlementPlan();
        bool horiz = plan.Horizontal = HorizontalFor(s);
        int cx = s.CenterX, cy = s.CenterY;
        bool longhouse = Hash(StPlan, (long)s.Id.Value, 5) % 100 < 65;
        var house = longhouse ? LotUse.Longhouse : LotUse.Cottage;
        int hw = longhouse ? 28 : 20, hd = longhouse ? 26 : 24;
        // the whole holding: house with its kitchen garden and orchard behind, and the farmyard with the barn beside it
        var hr = ToWorld(horiz, cx, cy, -hw / 2 - 11, 4, hw + 6, hd + 16);
        var br = ToWorld(horiz, cx, cy, hw / 2 + 4, 4, 24, 26);
        plan.Lots.Add(new Lot(0, house, DwellingOf(house), Blueprint(house), hr, FacingOf(horiz, 1), WealthOf(house)));
        plan.Lots.Add(new Lot(1, LotUse.Barn, DwellingType.None, "barn", br, FacingOf(horiz, 1), 1));
        plan.RadiusM = 36;
        var a = ToWorld(horiz, cx, cy, -hw / 2 - 12, -2, 1, 1); var b = ToWorld(horiz, cx, cy, hw / 2 + 24, -2, 1, 1);
        plan.Streets.Add(new Street(new PointM(a.X, a.Y), new PointM(b.X, b.Y), 4));
    }
}
