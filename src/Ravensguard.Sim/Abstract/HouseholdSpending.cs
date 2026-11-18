using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.Abstract;

/// <summary>
/// The spending of households that hold coin above their reserve (integration decision I37; the rules are <see cref="SpendingRuleDef"/> content). Once a season, on a
/// day of the season fixed by its index, a household of the hour's slice spends a share of the coin it holds above <c>reserve_days</c> of its food: wages to the idle
/// hands of its village (the poorest households first, a labourer's day wage for each day of service, at most a season's), works (builders' wages to the same idle hands
/// and building materials bought at the market), craft goods bought at the market and used up, and alms to the destitute of its village and then its fief. A priest's
/// household spends the parish chest with its own purse. Every coin moves by a ledger transfer or a market trade and every good is bought and then destroyed for a
/// documented reason (Worn, Consumed), so coin and mass stay exact. Market purchases are buy orders filled at the next session; what a book cannot supply is not
/// spent, but the order still counts in the book's demand, so the workshops see it. Not saved: it keeps no state between calls, only diagnostic totals.
/// </summary>
public sealed class HouseholdSpending
{
    public const int ServiceDaysMax = 15, PoorBelowMites = 3000, AlmsPerMemberMites = 1000, OrderMaxRatioPermille = 2000;
    public const int RolePeasant = 0, RoleBurgher = 1, RoleGentry = 2, RoleLord = 3, RoleParish = 4;
    private static readonly string[] RoleIds = ["peasant", "burgher", "gentry", "lord", "parish"];

    private readonly SimWorld _w; private readonly EconomyWorld _ew;
    private readonly SpendingRuleDef?[] _rules = new SpendingRuleDef?[5];
    private readonly (ushort Good, int Weight)[][] _goods = new (ushort, int)[5][], _materials = new (ushort, int)[5][];
    private readonly long _measureMites;
    private readonly int _dayWage;

    public long WagesMites, WorksLabourMites, AlmsMites, GoodsBudgetMites, MaterialsBudgetMites, Spenders;

    public HouseholdSpending(SimWorld w, EconomyWorld ew, IReadOnlyList<SpendingRuleDef> rules)
    {
        _w = w; _ew = ew; var cat = w.Economy.Goods;
        (ushort, int)[] Ids(Dictionary<string, int> d) => [.. d.OrderBy(kv => kv.Key, StringComparer.Ordinal).Where(kv => kv.Value > 0 && cat.TryIdOf(kv.Key, out _)).Select(kv => (cat.IdOf(kv.Key), kv.Value))];
        for (int r = 0; r < RoleIds.Length; r++)
        {
            var rule = rules.FirstOrDefault(x => x.Role == RoleIds[r]);
            _rules[r] = rule; _goods[r] = rule == null ? [] : Ids(rule.GoodsWeights); _materials[r] = rule == null ? [] : Ids(rule.MaterialsWeights);
        }
        _measureMites = Math.Max(1, cat.BasePriceMilliMites(cat.IdOf("wheat")) * HouseholdNeeds.GramsPerMeasure / 1_000_000);
        _dayWage = RecipeBook.WagePerDay(WageClass.Unskilled);
    }

    public bool HasRules => _rules.Any(r => r != null);

    /// <summary>H4: the households of the hour's slice whose day of the season it is (index / 24 mod 15) spend.</summary>
    public void Slice(GameTime now)
    {
        if (!HasRules) return;
        var hht = _w.Households; int hour = now.Hour, day = (int)now.TotalDays, dos = day % TimeConst.DaysPerSeason;
        List<int>? spenders = null;
        for (int hh = hour; hh < hht.Count; hh += 24)
            if (hht.MemberCount[hh] > 0 && hh / 24 % TimeConst.DaysPerSeason == dos) (spenders ??= []).Add(hh);
        if (spenders == null) return;
        _poor = null; _destitute = null;
        foreach (int hh in spenders) Spend(hh, day);
    }

    private int RoleOf(int hh)
    {
        var pop = _w.Population;
        foreach (int l in pop.FiefLordHousehold) if (l == hh) return RoleLord;
        foreach (int p in pop.PriestHousehold) if (p == hh) return RoleParish;
        return _w.Households.Class[hh] switch { SocialClass.Gentry => RoleGentry, SocialClass.Burgher => RoleBurgher, _ => RolePeasant };
    }

    private void Spend(int hh, int day)
    {
        var e = _w.Economy; var led = e.Ledger; var hht = _w.Households;
        int role = RoleOf(hh); var rule = _rules[role]; if (rule == null || rule.SpendPctPerSeason <= 0) return;
        var purse = e.HouseholdPurse[hh];
        PurseId chest = PurseId.None;
        if (role == RoleParish) { var sid = _w.Population.Region.Settlements[hht.Settlement[hh]].Id; if (led.TryFind(PurseKind.Chest, sid, out var c)) chest = c; }
        long have = led.Balance(purse).Mites + (chest.IsNone ? 0 : led.Balance(chest).Mites);
        long reserve = (long)hht.MemberCount[hh] * rule.ReserveDays * _measureMites;
        if (role == RoleLord) reserve += ManorReserve(hh);
        long spend = (have - reserve) * rule.SpendPctPerSeason / 100;
        if (spend < _dayWage) return;
        Spenders++;
        long wages = spend * rule.WagesPct / 100, works = spend * rule.WorksPct / 100, goods = spend * rule.GoodsPct / 100, alms = spend * rule.AlmsPct / 100;
        long worksLabour = works * rule.WorksLabourPct / 100, materials = works - worksLabour;
        int s = hht.Settlement[hh];
        // coin first: alms, then the wages of service and of the builders (from the parish chest first, for a priest)
        AlmsMites += Alms(hh, s, chest, purse, alms);
        WagesMites += Hire(hh, s, chest, purse, wages);
        WorksLabourMites += Hire(hh, s, chest, purse, worksLabour);
        // then the market: craft goods and materials as buy orders for the next session
        GoodsBudgetMites += Order(hh, s, _goods[role], goods, 3, role);
        MaterialsBudgetMites += Order(hh, s, _materials[role], materials, 4, role);
    }

    /// <summary>A lord keeps a month of his manors' wage bill before he spends.</summary>
    private long ManorReserve(int hh)
    {
        long r = 0; var prod = _ew.Production;
        foreach (var b in prod.Businesses) if (b.OwnerHousehold == hh && b.State == BusinessState.Open && prod.KindOf(b).Id == "manor") r += (long)b.Staff.Count * 200 * 30;
        return r;
    }

    private bool Pay(PurseId chest, PurseId purse, PurseId to, long mites, TransferReason why)
    {
        var led = _w.Economy.Ledger;
        long fromChest = chest.IsNone ? 0 : Math.Min(mites, led.Balance(chest).Mites);
        if (fromChest > 0 && !led.Transfer(chest, to, new Coin(fromChest), why)) fromChest = 0;
        long rest = mites - fromChest;
        if (rest > 0 && !led.Transfer(purse, to, new Coin(rest), why)) return fromChest > 0;
        return true;
    }

    // ---------- wages: the poorest idle hands of the village, then of the fief ----------

    private Dictionary<int, List<int>>? _poor;

    /// <summary>Idle adults (16 to 59, no post, not on a business's staff) of households that are not gentry and hold under 30 crowns, per settlement, poorest household first.</summary>
    private Dictionary<int, List<int>> Poor()
    {
        if (_poor != null) return _poor;
        var p = _w.People; var hht = _w.Households; var e = _w.Economy; int day = (int)(_w.State.HourDone / 24);
        // staff counted from the businesses now, not from the labour market's snapshot (that one is taken at the day's hiring and goes stale with the hourly quits;
        // a loaded world would take it fresh and differ, review finding 2)
        var staff = new bool[p.Count];
        foreach (var b in _ew.Production.Businesses) foreach (int x in b.Staff) if (x < staff.Length) staff[x] = true;
        var tmp = new Dictionary<int, List<(long Coin, int Person)>>();
        for (int i = 0; i < p.Count; i++)
        {
            if (p.Status[i] != PersonStatus.Alive || staff[i]) continue;
            var occ = p.Occupation[i]; if (occ is not (Occupation.None or Occupation.Labourer or Occupation.Servant) || (occ == Occupation.Servant && p.Employer[i] >= 0)) continue;
            int age = p.AgeYears(i, day); if (age < 16 || age >= 60) continue;
            int h = p.Household[i]; if (h < 0 || hht.MemberCount[h] == 0 || hht.Class[h] == SocialClass.Gentry) continue;
            long coin = e.Ledger.Balance(e.HouseholdPurse[h]).Mites; if (coin >= PoorBelowMites) continue;
            int s = hht.Settlement[h];
            if (!tmp.TryGetValue(s, out var l)) tmp[s] = l = [];
            l.Add((coin, i));
        }
        _poor = [];
        foreach (var (s, l) in tmp) _poor[s] = [.. l.OrderBy(x => x.Coin).ThenBy(x => x.Person).Select(x => x.Person)];
        return _poor;
    }

    private long Hire(int payerHh, int settlement, PurseId chest, PurseId purse, long budget)
    {
        if (budget < _dayWage) return 0;
        var e = _w.Economy; long paid = 0;
        foreach (var list in Lists(Poor(), settlement))
        {
            while (budget - paid >= _dayWage && list.Count > 0)
            {
                int person = list[0]; list.RemoveAt(0);
                int h = _w.People.Household[person]; if (h < 0 || h == payerHh) continue;
                long days = Math.Min(ServiceDaysMax, (budget - paid) / _dayWage), pay = days * _dayWage;
                if (!Pay(chest, purse, e.HouseholdPurse[h], pay, TransferReason.Wage)) return paid;
                paid += pay;
            }
            if (budget - paid < _dayWage) break;
        }
        return paid;
    }

    /// <summary>The lists to draw from: the settlement's, then the other settlements of its fief, then the rest of the region, each in ascending index (a rich lord's
    /// works and alms reach beyond his own villages once those are served).</summary>
    private IEnumerable<List<int>> Lists(Dictionary<int, List<int>> by, int settlement)
    {
        if (by.TryGetValue(settlement, out var own)) yield return own;
        var reg = _w.Population.Region; int f = reg.Settlements[settlement].FiefIndex;
        var keys = by.Keys.Order().ToList();
        if (f >= 0) foreach (int s in keys) if (s != settlement && reg.Settlements[s].FiefIndex == f) yield return by[s];
        foreach (int s in keys) if (s != settlement && (f < 0 || reg.Settlements[s].FiefIndex != f)) yield return by[s];
    }

    // ---------- alms: the destitute households of the village, then of the fief ----------

    private Dictionary<int, List<int>>? _destitute;

    private Dictionary<int, List<int>> Destitute()
    {
        if (_destitute != null) return _destitute;
        var hht = _w.Households; var e = _w.Economy;
        var tmp = new Dictionary<int, List<(long Coin, int Hh)>>();
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0 || hht.Class[h] == SocialClass.Gentry) continue;
            long coin = e.Ledger.Balance(e.HouseholdPurse[h]).Mites;
            if (e.FoodCoverDaysX10[h] >= 100 || coin >= (long)hht.MemberCount[h] * AlmsPerMemberMites) continue;
            int s = hht.Settlement[h];
            if (!tmp.TryGetValue(s, out var l)) tmp[s] = l = [];
            l.Add((coin, h));
        }
        _destitute = [];
        foreach (var (s, l) in tmp) _destitute[s] = [.. l.OrderBy(x => x.Coin).ThenBy(x => x.Hh).Select(x => x.Hh)];
        return _destitute;
    }

    private long Alms(int payerHh, int settlement, PurseId chest, PurseId purse, long budget)
    {
        if (budget <= 0) return 0;
        var e = _w.Economy; long paid = 0;
        foreach (var list in Lists(Destitute(), settlement))
        {
            while (budget - paid > 0 && list.Count > 0)
            {
                int h = list[0]; list.RemoveAt(0); if (h == payerHh) continue;
                long give = Math.Min(budget - paid, (long)_w.Households.MemberCount[h] * AlmsPerMemberMites);
                if (give <= 0 || !Pay(chest, purse, e.HouseholdPurse[h], give, TransferReason.Alms)) return paid;
                paid += give;
            }
            if (budget - paid <= 0) break;
        }
        return paid;
    }

    // ---------- the market: craft goods and building materials ----------

    private long Order(int hh, int settlement, (ushort Good, int Weight)[] mix, long budget, byte hook, int role)
    {
        if (budget <= 0 || mix.Length == 0) return 0;
        // a peasant buys at his village market; the lord, the gentry, the parish and the burghers buy their cloth, candles and stone in the market town
        var ms = _ew.Markets; var book = role == RolePeasant ? ms.BookForSettlement(settlement) : ms.MarketTownBook(settlement); var party = ms.HouseholdParty(hh);
        int tot = 0; foreach (var (_, wt) in mix) tot += wt;
        long posted = 0;
        foreach (var (g, wt) in mix)
        {
            long part = budget * wt / tot;
            long unitMicro = book.Goods[g].RefMicro * (1000 + book.Money.SpreadHalfPermille) / 1000;
            if (part <= 0 || unitMicro <= 0) continue;
            long qty = (long)((Int128)part * PriceCurve.Micro / unitMicro);
            if (qty <= 0) continue;
            book.Post(new BuyOrder(party, g, qty, OrderMaxRatioPermille) { Priority = 7, OnFilled = ms.DeliveryHook(hh, g, hook), HookKind = hook, HookArg = hh });
            posted += part;
        }
        return posted;
    }
}
