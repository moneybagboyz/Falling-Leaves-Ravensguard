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

    public long CharityG, SoldG, Asked, Short;

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
            Asked++;
            if (want > 0)
                foreach (int s2 in _byFief[FiefSlot(s)])
                {
                    if (want <= 0) break;
                    if (s2 != s && e.OtherStores.TryGetValue(FarmIds.ParishGranary(s2), out var other)) want -= Draw(other, house, want);
                }
            // the Church moves grain between its granaries in a famine: the richest granaries of the region make up what the fief's cannot (integration decision I24)
            if (want > 0) foreach (int s3 in Richest()) { if (want <= 0) break; if (e.OtherStores.TryGetValue(FarmIds.ParishGranary(s3), out var far)) want -= Draw(far, house, want); }
            // then the lord's own barn (the almonry in kind, decision I25): what he holds above forty days of his household's food
            if (want > 0) want -= LordAlms(s, house, want);
            if (want > 0) Short++;
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

    /// <summary>Alms (integration decision I25): every season a lord gives a third of the coin he holds above 1,000 crowns to the destitute households of his fief (under ten days of food and under ten crowns), at most 25 crowns a member.</summary>
    public const long AlmsReserveMites = 100_000, AlmsMaxPerMemberMites = 2500, AlmsPoorBelowMites = 1000;
    public long AlmsMites;

    public void Almonry(GameTime now)
    {
        var e = _w.Economy; var hht = _w.Households; var pop = _w.Population; var reg = pop.Region; var led = e.Ledger;
        int nf = reg.Fiefs.Count; if (nf == 0) return;
        var poor = new List<int>[nf]; for (int f = 0; f < nf; f++) poor[f] = [];
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0 || hht.Class[h] == SocialClass.Gentry || e.FoodCoverDaysX10[h] >= 100 || led.Balance(e.HouseholdPurse[h]).Mites >= AlmsPoorBelowMites) continue;
            int f = reg.Settlements[hht.Settlement[h]].FiefIndex; if (f >= 0 && f < nf) poor[f].Add(h);
        }
        for (int f = 0; f < nf; f++)
        {
            if (poor[f].Count == 0 || f >= pop.FiefLordHousehold.Length) continue;
            int lord = pop.FiefLordHousehold[f]; if (lord < 0 || hht.MemberCount[lord] == 0) continue;
            long wealth = led.Balance(e.HouseholdPurse[lord]).Mites - AlmsReserveMites; if (wealth <= 0) continue;
            long pool = wealth / 2, members = 0; foreach (int h in poor[f]) members += hht.MemberCount[h];
            long per = Math.Min(AlmsMaxPerMemberMites, pool / Math.Max(1, members)); if (per <= 0) continue;
            foreach (int h in poor[f])
            {
                long pay = per * hht.MemberCount[h];
                if (pay > 0 && led.Transfer(e.HouseholdPurse[lord], e.HouseholdPurse[h], new Coin(pay), TransferReason.Gift)) AlmsMites += pay;
            }
        }
    }

    private long LordAlms(int settlement, Store house, long want)
    {
        var e = _w.Economy; var hht = _w.Households; var pop = _w.Population;
        int f = pop.Region.Settlements[settlement].FiefIndex; if (f < 0 || f >= pop.FiefLordHousehold.Length) return 0;
        int lord = pop.FiefLordHousehold[f]; if (lord < 0 || hht.MemberCount[lord] == 0) return 0;
        var ls = e.HouseholdStore[lord]; long keep = (long)hht.MemberCount[lord] * HouseholdNeeds.GramsPerMeasure * 40, got = 0;
        long have = 0; foreach (ushort g in _grains) have += ls.Qty(g);
        long spare = have - keep; if (spare <= 0) return 0;
        foreach (ushort g in _grains)
        {
            if (got >= want || got >= spare) break;
            got += Store.MoveUpTo(ls, house, g, Math.Min(want, spare) - got);
        }
        CharityG += got; AlmsG += got;
        return got;
    }
    public long AlmsG;

    private List<int> _richest = []; private long _richestDay = -1;

    /// <summary>The settlements whose granaries hold the most grain, richest first (at most eight), recomputed once a day.</summary>
    private List<int> Richest()
    {
        if (_richestDay == _w.State.HourDone / 24) return _richest;
        _richestDay = _w.State.HourDone / 24; var e = _w.Economy; var list = new List<(long Mass, int S)>();
        for (int s = 0; s < _w.Population.Region.Settlements.Count; s++)
            if (e.OtherStores.TryGetValue(FarmIds.ParishGranary(s), out var g)) { long m = 0; foreach (ushort gr in _grains) m += g.Qty(gr); if (m > 20_000) list.Add((m, s)); }
        _richest = [.. list.OrderByDescending(x => x.Mass).ThenBy(x => x.S).Take(8).Select(x => x.S)];
        return _richest;
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

    private MarketParty Steward(int fief) => _markets.Steward(fief);

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
