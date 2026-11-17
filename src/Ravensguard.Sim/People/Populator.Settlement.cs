using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.People;

public sealed partial class Populator
{
    private readonly record struct Job(Occupation Occ, int WorkLot, bool Celibate = false);

    private static (Occupation Occ, int W)[] TownBurgage = [(Occupation.Merchant, 22), (Occupation.Baker, 8), (Occupation.Brewer, 7), (Occupation.Butcher, 6), (Occupation.Carpenter, 9),
        (Occupation.Mason, 6), (Occupation.Weaver, 10), (Occupation.Tanner, 5), (Occupation.Cobbler, 7), (Occupation.Smith, 5)];
    private static (Occupation Occ, int W)[] TownHumble = [(Occupation.Labourer, 30), (Occupation.Weaver, 8), (Occupation.Cobbler, 6), (Occupation.Carpenter, 6), (Occupation.Mason, 5),
        (Occupation.Woodcutter, 3), (Occupation.Servant, 4), (Occupation.Baker, 3), (Occupation.Brewer, 3), (Occupation.Tanner, 2)];
    private static (Occupation Occ, int W)[] VillageHumble = [(Occupation.Labourer, 40), (Occupation.Woodcutter, 8), (Occupation.Shepherd, 8), (Occupation.Carpenter, 6), (Occupation.Weaver, 5),
        (Occupation.Cobbler, 3), (Occupation.Baker, 3), (Occupation.Brewer, 4), (Occupation.CharcoalBurner, 4), (Occupation.Mason, 2), (Occupation.Tanner, 2), (Occupation.Butcher, 2)];

    private static Occupation Weighted(Rnd r, (Occupation Occ, int W)[] table, bool river, bool mining)
    {
        int total = 0;
        foreach (var (_, w) in table) total += w;
        int extra = (river ? 12 : 0) + (mining ? 12 : 0);
        int x = r.Range(0, total + extra);
        foreach (var (o, w) in table) { x -= w; if (x < 0) return o; }
        // The river and mining shares sit after the base table.
        return river && (!mining || x < 12) ? Occupation.Fisher : mining ? Occupation.Miner : Occupation.Labourer;
    }

    private static long D2(RectM a, int x, int y) { long dx = a.CenterX - x, dy = a.CenterY - y; return dx * dx + dy * dy; }

    private void BuildSettlement(int si)
    {
        var s = _r.Settlements[si];
        var plan = s.Plan;
        int culture = CultureOf(s);
        var np = _names.For(culture);
        var r = new Rnd(_seed, s.Id.Value, StSettlement);
        bool town = s.Class == SettlementClass.Town, farmstead = s.Class == SettlementClass.Farmstead;
        bool mining = s.HistoryId != EntityId.None && (_h.GetSettlement(s.HistoryId)?.Mining ?? 0) > 0;
        int fief = s.FiefIndex;
        int lord = fief >= 0 ? _fiefLord[fief] : -1;
        int Q = s.TargetPop - _lordPeople[si];
        if (Q <= 0) return;

        // Dwelling lots available to ordinary households (the lord's lot is taken).
        var ordinary = new List<int>();
        foreach (var l in plan.Lots)
            if (!l.Vacant && l.Dwelling is DwellingType.Cottage or DwellingType.Longhouse or DwellingType.Burgage && l.Index != _lordLot[si]) ordinary.Add(l.Index);
        int hCount = Math.Min(s.Households - (_lordPeople[si] > 0 ? 1 : 0), ordinary.Count);
        hCount = Math.Min(hCount, Q);
        if (hCount <= 0) { BuildVagrants(si, Q, culture, np, r); return; }

        // Occupied lots: field-holding lots first, then a stable shuffle.
        var occupied = ordinary.OrderBy(l => _lotFarm[si][l] ? 0 : 1).ThenBy(l => DetRng.Mix(s.Id.Value ^ (ulong)l * 0x9E3779B97F4A7C15UL)).Take(hCount).OrderBy(l => l).ToList();

        // Jobs: workplaces and town offices first, nearest free house each, then fields, then the trades of the place.
        var job = new Dictionary<int, Job>();
        var free = new List<int>(occupied);
        int TakeNear(RectM anchor, params DwellingType[] prefer)
        {
            int best = -1; long bd = long.MaxValue;
            foreach (int l in free)
            {
                long d = D2(plan.Lots[l].Rect, anchor.CenterX, anchor.CenterY);
                if (prefer.Length > 0 && Array.IndexOf(prefer, plan.Lots[l].Dwelling) < 0) d += 4_000_000_000L;
                if (d < bd) { bd = d; best = l; }
            }
            if (best >= 0) free.Remove(best);
            return best;
        }
        void Reserve(Occupation occ, int workLot, RectM anchor, bool celibate, params DwellingType[] prefer)
        {
            if (free.Count == 0) return;
            int l = TakeNear(anchor, prefer);
            if (l >= 0) job[l] = new Job(occ, workLot, celibate);
        }
        var square = plan.Square.W > 0 ? plan.Square : new RectM(s.CenterX, s.CenterY, 1, 1);
        if (town) { Reserve(Occupation.Mayor, -1, square, false, DwellingType.Burgage); Reserve(Occupation.GuildMaster, -1, square, false, DwellingType.Burgage); }
        if (plan.ChurchLot >= 0) Reserve(Occupation.Priest, plan.ChurchLot, plan.Lots[plan.ChurchLot].Rect, true, DwellingType.Cottage, DwellingType.Longhouse);
        foreach (var l in plan.Lots.Where(x => x.Use == LotUse.Smithy)) Reserve(Occupation.Smith, l.Index, l.Rect, false);
        foreach (var l in plan.Lots.Where(x => x.Use == LotUse.Tavern)) Reserve(Occupation.Innkeeper, l.Index, l.Rect, false, DwellingType.Burgage, DwellingType.Longhouse);
        if (plan.MillLot >= 0) Reserve(Occupation.Miller, plan.MillLot, plan.Lots[plan.MillLot].Rect, false, DwellingType.Longhouse, DwellingType.Cottage);
        if (!town && !farmstead) Reserve(Occupation.Reeve, -1, square, false, DwellingType.Longhouse);
        if (town)
        {
            int guards = Math.Max(2, s.TargetPop / 120);
            int manor = plan.ManorLot;
            for (int g = 0; g < guards; g++) Reserve(Occupation.Guard, manor, manor >= 0 ? plan.Lots[manor].Rect : square, false, DwellingType.Cottage);
        }
        foreach (int l in free.ToList())
        {
            var dw = plan.Lots[l].Dwelling;
            Occupation occ;
            if (_lotFarm[si][l]) occ = farmstead ? Occupation.Farmer : r.Pct(88) ? Occupation.Farmer : Occupation.Ploughman;
            else if (town) occ = Weighted(r, dw == DwellingType.Burgage ? TownBurgage : TownHumble, s.OnRiver, mining);
            else occ = Weighted(r, VillageHumble, s.OnRiver, mining);
            job[l] = new Job(occ, -1);
        }

        // Household sizes: bell draws around the mean, trimmed to each dwelling's beds, then nudged to the exact head count.
        var caps = new int[occupied.Count]; var size = new int[occupied.Count];
        long sum = 0;
        int mu100 = Q * 100 / occupied.Count;
        for (int j = 0; j < occupied.Count; j++)
        {
            caps[j] = _lotBeds[si][occupied[j]];
            if (job[occupied[j]].Celibate) caps[j] = Math.Min(caps[j], 3);
            int x = (r.Range(0, 1000) + r.Range(0, 1000) + r.Range(0, 1000)) / 3;
            int n = 1 + (int)((long)x * Math.Max(0, 2 * mu100 - 200) / 100_000);
            size[j] = Math.Clamp(n, 1, caps[j]);
            sum += size[j];
        }
        int guardPos = r.Range(0, occupied.Count);
        for (int pass = 0; pass < 2; pass++)
        {
            int guard = 0;
            while (sum != Q && guard++ < 100_000)
            {
                int j = (guardPos + r.Range(0, occupied.Count)) % occupied.Count;
                bool up = sum < Q;
                int tries = 0;
                while (tries++ < occupied.Count && (up ? size[j] >= caps[j] : size[j] <= 1)) j = (j + 1) % occupied.Count;
                if (up ? size[j] >= caps[j] : size[j] <= 1) break;
                size[j] += up ? 1 : -1; sum += up ? 1 : -1;
            }
        }
        int leftover = (int)(Q - sum);

        for (int j = 0; j < occupied.Count; j++)
        {
            int l = occupied[j];
            var lot = plan.Lots[l];
            var jb = job[l];
            var occ = jb.Occ;
            SocialClass cls = lot.Dwelling == DwellingType.Burgage || (town && OccupationInfo.IsTrade(occ)) ? SocialClass.Burgher : SocialClass.Peasant;
            if (!town && occ is Occupation.Miller or Occupation.Innkeeper) cls = SocialClass.Peasant;
            if (town && occ is Occupation.Labourer or Occupation.Servant or Occupation.Woodcutter or Occupation.Fisher or Occupation.Miner) cls = lot.Dwelling == DwellingType.Burgage ? SocialClass.Burgher : SocialClass.Peasant;
            DeedKind deed = DeedFor(si, l, lot, farmstead, town, occ, r);
            int employer = occ switch
            {
                Occupation.Farmer or Occupation.Ploughman => deed == DeedKind.Tenant ? lord : -1,
                Occupation.Labourer or Occupation.Shepherd or Occupation.Woodcutter or Occupation.CharcoalBurner or Occupation.Miner or Occupation.Guard or Occupation.Reeve or Occupation.Servant => lord,
                _ => -1,
            };
            var pl = new HhPlan
            {
                Lot = l, Cap = _lotBeds[si][l], Size = size[j], Dwelling = lot.Dwelling, HeadOcc = occ, WorkLot = jb.WorkLot, HeadEmployer = employer,
                Celibate = jb.Celibate, Class = cls, Deed = deed, Farm = _lotFarm[si][l],
            };
            if (occ is Occupation.Labourer or Occupation.Farmer or Occupation.Ploughman or Occupation.Shepherd or Occupation.Woodcutter or Occupation.Fisher or Occupation.Miner or Occupation.CharcoalBurner) pl.WorkLot = -1;
            else if (pl.WorkLot < 0) pl.WorkLot = l; // home workshop
            int hh = BuildHousehold(pl, si, culture, np, r);
            _lotHousehold[si][l] = hh;
            int head = _hh.Head[hh];
            if (jb.WorkLot >= 0 && occ is Occupation.Smith or Occupation.Innkeeper or Occupation.Miller or Occupation.Priest) _lotKeeper[si][jb.WorkLot] = head;
            if (occ == Occupation.Priest && _priestHh[si] < 0) _priestHh[si] = hh;
        }
        BuildVagrants(si, leftover, culture, np, r);
    }

    private DeedKind DeedFor(int si, int lot, Lot l, bool farmstead, bool town, Occupation occ, Rnd r)
    {
        if (farmstead) return DeedKind.Owned;
        if (town)
        {
            if (l.Dwelling == DwellingType.Burgage) return r.Pct(75) ? DeedKind.Owned : DeedKind.Rented;
            return _lotOwnedParcel[si][lot] || r.Pct(20) ? DeedKind.Owned : DeedKind.Rented;
        }
        if (_lotFarm[si][lot]) return DeedKind.Tenant;
        if (occ == Occupation.Priest) return DeedKind.Tenant;
        return r.Pct(12) ? DeedKind.Owned : DeedKind.Rented;
    }
}
