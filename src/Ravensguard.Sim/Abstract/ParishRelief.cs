using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Abstract;

/// <summary>
/// What the parish does with the tithe grain in its granary (households spec 3.6, overview S5 "parish sales"): it feeds the households that have under three days
/// of food (up to half a measure per member and day, reason Gift in goods) and sells a fifteenth of what is left each day to the market through the priest's
/// household (else the fief lord's), so tithe grain does not sit in the barn for ever. Runs at the daily globals.
/// </summary>
public sealed class ParishRelief
{
    public const int CharityMeasureX100PerMember = 100, SellDivisor = 60, HungryBelowDaysX10 = 30;
    private readonly SimWorld _w;
    private readonly MarketSystem _markets;
    private readonly ushort[] _grains;
    private readonly List<int>[] _byFief;
    private bool[] _fiefHungry = [];

    public long CharityG, SoldG;

    public ParishRelief(SimWorld w, MarketSystem markets)
    {
        _w = w; _markets = markets;
        _grains = [.. w.Economy.Food.Staples.Where(g => w.Economy.Food.IsGrain(g))];
        int nf = Math.Max(1, w.Population.Region.Fiefs.Count);
        _byFief = new List<int>[nf + 1]; for (int i = 0; i < _byFief.Length; i++) _byFief[i] = [];
        for (int s = 0; s < w.Population.Region.Settlements.Count; s++) _byFief[FiefSlot(s)].Add(s);
    }

    private int FiefSlot(int settlement) { int f = _w.Population.Region.Settlements[settlement].FiefIndex; return f >= 0 && f < _byFief?.Length - 1 ? f : _byFief!.Length - 1; }

    /// <summary>
    /// The day's relief (households spec 3.6, taxes spec 3.6 famine relief): a household with under three days of food draws a ration of 0.8 measure a member from
    /// the parish granaries of its fief (its own village's first, then the others in order: the fief steward shares the tithe), and the parish sells the tithe
    /// only in a fief where no household is hungry, a sixtieth of a granary a day, through the priest's household (else the lord's).
    /// </summary>
    public void Daily(GameTime now)
    {
        var e = _w.Economy; var hht = _w.Households; var pop = _w.Population; int day = (int)now.TotalDays;
        int ns = pop.Region.Settlements.Count;
        if (_fiefHungry.Length != _byFief.Length) _fiefHungry = new bool[_byFief.Length]; else Array.Clear(_fiefHungry);
        for (int h = 0; h < hht.Count; h++)
        {
            int n = hht.MemberCount[h];
            if (n == 0 || e.FoodCoverDaysX10[h] >= HungryBelowDaysX10) continue;
            int s = hht.Settlement[h]; _fiefHungry[FiefSlot(s)] = true;
            Dole(h, n, s);
            long want = (long)n * CharityMeasureX100PerMember * HouseholdNeeds.GramsPerMeasure / 100;
            var house = e.HouseholdStore[h];
            if (e.OtherStores.TryGetValue(FarmIds.ParishGranary(s), out var own)) want -= Draw(own, house, want);
            if (want <= 0) continue;
            foreach (int s2 in _byFief[FiefSlot(s)])
            {
                if (want <= 0) break;
                if (s2 != s && e.OtherStores.TryGetValue(FarmIds.ParishGranary(s2), out var other)) want -= Draw(other, house, want);
            }
        }
        Relief(now);
        for (int s = 0; s < ns; s++)
        {
            if (_fiefHungry[FiefSlot(s)]) continue;
            if (!e.OtherStores.TryGetValue(FarmIds.ParishGranary(s), out var granary)) continue;
            int seller = pop.PriestHousehold.Length > s ? pop.PriestHousehold[s] : -1;
            if (seller < 0 || hht.MemberCount[seller] == 0)
            {
                int fief = pop.Region.Settlements[s].FiefIndex;
                seller = fief >= 0 && fief < pop.FiefLordHousehold.Length ? pop.FiefLordHousehold[fief] : -1;
                if (seller < 0 || hht.MemberCount[seller] == 0) continue;
            }
            var book = _markets.BookForSettlement(s); var party = _markets.HouseholdParty(seller);
            foreach (ushort g in _grains)
            {
                long have = granary.Qty(g); if (have <= 0) continue;
                long moved = Store.MoveUpTo(granary, e.HouseholdStore[seller], g, Math.Max(1, have / SellDivisor));
                if (moved <= 0) continue;
                SoldG += book.SellOrList(party, g, moved, 800, day);
            }
        }
    }

    /// <summary>Famine relief in coin (taxes spec 3.6): the fief's treasury gives a household with under three days of food <see cref="DoleMitesPerMemberDay"/> a member, as long as it holds the coin.</summary>
    private void Dole(int hh, int members, int settlement)
    {
        var e = _w.Economy; var reg = _w.Population.Region;
        int fief = reg.Settlements[settlement].FiefIndex; if (fief < 0 || fief >= reg.Fiefs.Count) return;
        if (!e.Ledger.TryFind(PurseKind.Treasury, reg.Fiefs[fief].Id, out var t)) return;
        long pay = Math.Min(members * (long)DoleMitesPerMemberDay, e.Ledger.Balance(t).Mites);
        if (pay > 0 && e.Ledger.Transfer(t, e.HouseholdPurse[hh], new Coin(pay), TransferReason.Gift)) DoleMites += pay;
    }
    public const int DoleMitesPerMemberDay = 45;
    public long DoleMites;

    // ---------- famine relief purchases (taxes spec 3.6) ----------

    private MarketParty?[] _stewards = [];

    private MarketParty Steward(int fief)
    {
        var reg = _w.Population.Region; var e = _w.Economy;
        if (_stewards.Length != reg.Fiefs.Count) _stewards = new MarketParty?[reg.Fiefs.Count];
        return _stewards[fief] ??= new MarketParty(reg.Fiefs[fief].Id, e.Ledger.Open(PurseKind.Treasury, reg.Fiefs[fief].Id), e.OpenStore(StoreKind.Chest, reg.Fiefs[fief].Id, 0, 0, enforce: false), PartyKind.Household, 1_000_000 + fief);
    }

    /// <summary>
    /// The fief steward feeds the hungry from the treasury's own purchases: the day's rations leave the fief chest for the households under three days of food (lowest cover
    /// first), and grain for the next days is bought at the market of every settlement that has hungry households, paid from the treasury (so the idle coin of the treasury
    /// goes to the farmers who hold the grain). Capped at 2 percent of the treasury a day.
    /// </summary>
    private void Relief(GameTime now)
    {
        var e = _w.Economy; var hht = _w.Households; var reg = _w.Population.Region;
        int nf = reg.Fiefs.Count; if (nf == 0) return;
        var needBySettlement = new Dictionary<int, long>();
        var hungry = new List<int>[nf]; for (int f = 0; f < nf; f++) hungry[f] = [];
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0 || e.FoodCoverDaysX10[h] >= HungryBelowDaysX10) continue;
            int s = hht.Settlement[h]; int f = reg.Settlements[s].FiefIndex; if (f < 0 || f >= nf) continue;
            hungry[f].Add(h); needBySettlement[s] = needBySettlement.GetValueOrDefault(s) + (long)hht.MemberCount[h] * HouseholdNeeds.GramsPerMeasure;
        }
        int day = (int)now.TotalDays;
        for (int f = 0; f < nf; f++)
        {
            if (hungry[f].Count == 0) continue;
            var st = Steward(f); var chest = st.Store;
            foreach (int h in hungry[f].OrderBy(x => e.FoodCoverDaysX10[x]).ThenBy(x => x))
            {
                long want = (long)hht.MemberCount[h] * HouseholdNeeds.GramsPerMeasure, got = 0;
                foreach (ushort g in _grains) { if (got >= want) break; got += Store.MoveUpTo(chest, e.HouseholdStore[h], g, want - got); }
                CharityG += got; ReliefG += got;
            }
        }
        foreach (var (s, need) in needBySettlement.OrderBy(kv => kv.Key))
        {
            int f = reg.Settlements[s].FiefIndex; var st = Steward(f);
            long budget = e.Ledger.Balance(st.Purse).Mites * 2 / 100;
            if (budget <= 0) continue;
            var book = _markets.BookForSettlement(s);
            ushort best = ushort.MaxValue; long bestStock = 0;
            foreach (ushort g in _grains) { long stock = book.Goods[g].StockUnits; if (stock > bestStock) { bestStock = stock; best = g; } }
            if (best == ushort.MaxValue) continue;
            long qty = Math.Min(need * 2, bestStock);
            book.Post(new BuyOrder(st, best, qty, 3500) { Priority = 0 });
        }
    }
    public long ReliefG;

    private long Draw(Store granary, Store house, long want)
    {
        long got = 0;
        foreach (ushort g in _grains)
        {
            if (got >= want) break;
            got += Store.MoveUpTo(granary, house, g, want - got);
        }
        CharityG += got;
        return got;
    }
}
