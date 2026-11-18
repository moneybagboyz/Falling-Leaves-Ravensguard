using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Production;

/// <summary>
/// Creates the businesses of a populated region at world start (production spec 2.1): one per distinct workplace lot and trade of the population, one crew per
/// settlement for woodcutters, fishers and charcoal burners, one per active mine; spinners become the put-out staff of the weavers of their settlement. Owner,
/// staff, cash box and store are real records; start-up capital moves from the owner's household purse by a Gift transfer. Deterministic: every loop ascending.
/// </summary>
public static class BusinessBuilder
{
    public const int StartCapitalWorkshopPermille = 500, StartCapitalCrewPermille = 80;

    public static ProductionSystem Create(EconomyState econ, Population pop, ProductionContent content, ulong seed, MarketSystem? markets, ResourceLayer? resources)
    {
        var sys = new ProductionSystem(econ, pop, content, seed, markets, resources);
        var byOcc = new Dictionary<Occupation, BusinessKind>();
        foreach (var k in content.Kinds) if (k.Id != "mine") byOcc[k.OwnerOccupation] = k;
        var people = pop.People;

        var groups = new SortedDictionary<(int Settlement, int Lot, Occupation Occ), List<int>>();
        var spinners = new SortedDictionary<int, List<int>>();
        for (int i = 0; i < people.Count; i++)
        {
            var occ = people.Occupation[i]; int s = people.WorkSettlement[i];
            if (s < 0) continue;
            if (occ == Occupation.Spinner) { if (!spinners.TryGetValue(s, out var sl)) spinners[s] = sl = []; sl.Add(i); continue; }
            if (!byOcc.TryGetValue(occ, out var kind)) continue;
            bool crew = kind.Extractions.Length > 0 || (kind.Stations.Length > 0 && kind.Slots.Length > 0 && kind.Slots[0].Occupation == Occupation.CharcoalBurner);
            int lot = crew ? -1 : people.WorkLot[i];
            if (!crew && lot < 0) continue;
            var key = (s, lot, occ);
            if (!groups.TryGetValue(key, out var l)) groups[key] = l = [];
            l.Add(i);
        }

        foreach (var ((settlement, lot, occ), members) in groups)
        {
            var kind = byOcc[occ];
            bool crew = lot < 0;
            int owner = ChooseOwner(pop, members, kind, crew);
            var b = sys.NewBusiness(kind, settlement, lot, owner);
            b.OwnerWorks = !crew;
            int cap = kind.StaffCapacity;
            foreach (int m in members) if (m != owner && b.Staff.Count < cap) sys.AddStaff(b, m);
            foreach (var (sk, count) in kind.Stations) for (int c = 0; c < count; c++) b.Stations.Add(new Station { Kind = sk });
            Fund(sys, b, crew ? StartCapitalCrewPermille : kind.Trader ? econ.Money.MerchantCashShareBp / 10 : StartCapitalWorkshopPermille);
        }

        // put-out spinners: each weaver business takes spinners of its settlement, up to its slot count (one weaver per 8 spinners)
        var weaveries = sys.Businesses.Where(b => content.Kinds[b.Kind].Id == "weavery").ToList();
        foreach (var (s, list) in spinners)
        {
            var local = weaveries.Where(w => w.Settlement == s).ToList();
            if (local.Count == 0) continue;
            int slots = content.Kinds[local[0].Kind].Slots.Where(x => x.Occupation == Occupation.Spinner).Sum(x => x.Count);
            foreach (int sp in list)
            {
                Business? best = null; int bestN = int.MaxValue;
                foreach (var w in local)
                {
                    int n = w.Staff.Count(p => people.Occupation[p] == Occupation.Spinner);
                    if (n < slots && n < bestN) { best = w; bestN = n; }
                }
                if (best == null) break;
                sys.AddStaff(best, sp);
                best.Stations.Add(new Station { Kind = "spinning_wheel" });
            }
        }

        // mines: one business per active mine, staffed by the miners of the nearest settlement
        if (pop.Region.Sites.Count > 0)
        {
            var mineKind = content.Kinds.FirstOrDefault(k => k.Id == "mine");
            if (mineKind != null)
                for (int si = 0; si < pop.Region.Sites.Count; si++)
                {
                    var site = pop.Region.Sites[si];
                    if (site.Kind != SiteKind.Mine || site.Mine != MineState.Active || MineWorks.OreGood(site.Mineral) == null) continue;
                    int near = NearestSettlement(pop.Region, site.X, site.Y);
                    var miners = Enumerable.Range(0, people.Count).Where(p => people.Occupation[p] == Occupation.Miner && people.WorkSettlement[p] == near).ToList();
                    int owner = miners.Count > 0 && people.Employer[miners[0]] >= 0 ? people.Employer[miners[0]] : miners.Count > 0 ? miners[0] : pop.FiefLord.Length > 0 ? pop.FiefLord[Math.Max(0, pop.Region.Settlements[near].FiefIndex)] : -1;
                    if (owner < 0) continue;
                    var b = sys.NewBusiness(mineKind, near, -1, owner); b.OwnerWorks = false;
                    b.Mine = sys.AddMine(MineWorks.FromSite(si, site));
                    foreach (int m in miners) if (m != owner && b.Staff.Count < mineKind.StaffCapacity) sys.AddStaff(b, m);
                    Fund(sys, b, StartCapitalCrewPermille);
                }
        }
        markets?.ExtraCoin.Clear();
        sys.RegisterTraders();
        return sys;
    }

    private static int NearestSettlement(Region r, int x, int y)
    {
        int best = 0; long bd = long.MaxValue;
        for (int s = 0; s < r.Settlements.Count; s++)
        {
            long dx = r.Settlements[s].CenterX - x, dy = r.Settlements[s].CenterY - y, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = s; }
        }
        return best;
    }

    private static int ChooseOwner(Population pop, List<int> members, BusinessKind kind, bool crew)
    {
        var p = pop.People;
        if (crew)
        {
            // the lord (the common employer) owns a crew; else the most skilled member
            var emp = members.Where(m => p.Employer[m] >= 0).GroupBy(m => p.Employer[m]).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault();
            if (emp != null) return emp.Key;
        }
        var skill = OccupationInfo.Skills(kind.OwnerOccupation).A;
        int best = -1; int bs = -1; bool bFree = false;
        foreach (int m in members)
        {
            bool free = p.Employer[m] < 0; int sk = p.SkillLevelOf(m, skill);
            if (best < 0 || (free && !bFree) || (free == bFree && sk > bs)) { best = m; bs = sk; bFree = free; }
        }
        return best;
    }

    private static void Fund(ProductionSystem sys, Business b, int permille)
    {
        var econ = sys.Econ;
        int hh = b.OwnerHousehold; if (hh < 0) return;
        long bal = econ.Ledger.Balance(econ.HouseholdPurse[hh]).Mites;
        long amt = bal * permille / 1000;
        if (amt > 0) econ.Ledger.Transfer(econ.HouseholdPurse[hh], b.Purse, new Coin(amt), TransferReason.Gift);
        b.CashStart = econ.Ledger.Balance(b.Purse).Mites;
    }
}
