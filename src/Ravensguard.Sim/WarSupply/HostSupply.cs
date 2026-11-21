using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.WarSupply;

/// <summary>
/// The interface the military calls (army-economy 3), first cut (P7-EA1): register, update and disband a host; <see cref="Needs"/> (pure) and <see cref="Supply"/> with the baggage and market steps of the ladder, pay and arrears
/// and the hunger and unpaid days; <see cref="Muster"/>, <see cref="Release"/> and <see cref="Desert"/> for levies (a mustered man's household loses his labour: <see cref="IsOnCampaign"/> is the farming planner's hook);
/// <see cref="SiegeBlockade"/>, <see cref="DaysOfFood"/>, <see cref="HarvestRisk"/> and <see cref="Camp"/>. The rest of the ladder (food rents, depots and convoys, sutlers, purveyance, forage), the pay chest's moves and
/// capture, kit and loot are P7-AE1's second half, P7-AE2 and P7-AE3. The economy never moves soldiers; every coin and gram here moves through the ledger and the stores, so both audits stay exact.
/// One writer: the host slot (<see cref="HostSupplySystem"/>, hour 18) or the harness at a barrier.
/// </summary>
public sealed class HostSupply
{
    public WarSupplyBook Book { get; }
    public EconomyWorld Economy { get; }
    private readonly EconomyState _econ;
    private readonly Population _pop;
    private readonly Dictionary<int, Rt> _rt = [];
    private readonly Dictionary<int, DayResult> _last = [];
    private int _lastDay = int.MinValue;
    private readonly ushort[] _stapleGoods, _relishGoods;
    private readonly ushort _hay, _oats, _firewood;
    /// <summary>The hook for a household that evades the levy (the unrest rule, stream 0x682): settlement, household, day. Null until the unrest wiring binds it (P7-AM-B).</summary>
    public Func<int, int, int, bool>? Evades { get; set; }

    /// <summary>The live objects of a host's records, found again by owner after a load.</summary>
    private sealed class Rt
    {
        public PurseId Chest = PurseId.None, Soldiers = PurseId.None, Treasury = PurseId.None;
        public Store Baggage = null!;
        public MarketParty Party = null!;
    }

    public HostSupply(EconomyWorld economy, WarSupplyBook book)
    {
        Economy = economy; Book = book; _econ = economy.World.Economy; _pop = economy.World.Population;
        var c = _econ.Goods;
        _oats = c.TryIdOf("oats", out var o) ? o : ushort.MaxValue; _hay = c.TryIdOf("hay", out var h) ? h : ushort.MaxValue; _firewood = _econ.Food.Firewood;
        _stapleGoods = [.. _econ.Food.Staples.Where(g => g != _oats && !c.IsPiece(g) && c.Def(g).Class is "grain" or "flour")];
        _relishGoods = [.. _econ.Food.Relishes.Where(g => !c.IsPiece(g))];
        foreach (var s in book.Blockades.Keys) { var bk = BookOf(s); if (bk != null) bk.Blockaded = true; }
    }

    /// <summary>Puts the service in the home region's bag and binds the farming planner's hook; the world's save state is <paramref name="book"/>.</summary>
    public static HostSupply Install(EconomyWorld economy, WarSupplyBook book)
    {
        var hs = new HostSupply(economy, book);
        economy.World.Set(hs);
        economy.War = hs;
        return hs;
    }

    private int Day => (int)(Economy.World.State.HourDone / 24);
    private MarketBook? BookOf(int settlement) => settlement >= 0 && settlement < _pop.Region.Settlements.Count ? Economy.Markets.Books[Economy.Markets.BookOf[settlement]] : null;

    // ------------------------------------------------------------------ hosts

    /// <summary>Registers a host (army-economy 3.1) and returns its id; the chest purse and baggage store are the caller's records.</summary>
    public HostId Register(HostSpec spec)
    {
        if (_econ.Ledger.KindOf(spec.PayChest) != PurseKind.Chest) throw new ArgumentException("the pay chest must be a Chest purse", nameof(spec));
        var a = new HostAccount
        {
            Id = Book.NextHost++, Realm = spec.Realm, Lord = spec.Lord.Value,
            ChestKind = (byte)_econ.Ledger.KindOf(spec.PayChest), ChestOwner = _econ.Ledger.OwnerOf(spec.PayChest).Value, BaggageOwner = spec.Baggage.Owner.Value,
            Purveyance = spec.Policy.Purveyance, Plunder = spec.Policy.Plunder, Forage = spec.Policy.Forage, Depots = [.. spec.Policy.Depots ?? []], BuyRadiusM = spec.Policy.BuyRadiusM,
            Followers = spec.Followers, Carts = spec.Carts, Outside = Math.Min(spec.OutsideMen, spec.Men.Total),
            Riding = spec.Animals.Riding, Pack = spec.Animals.Pack, Draught = spec.Animals.Draught, Oxen = spec.Animals.Oxen,
        };
        Array.Copy(spec.Men.Counts, a.Men, Roster.Roles);
        if (!spec.Treasury.IsNone) { a.TreasuryKind = (byte)_econ.Ledger.KindOf(spec.Treasury); a.TreasuryOwner = _econ.Ledger.OwnerOf(spec.Treasury).Value; }
        Book.Hosts[a.Id] = a;
        Resolve(a);
        return new HostId(a.Id);
    }

    /// <summary>The host's place, mode and roster as the military last left them (army-economy 3.1).</summary>
    public void Update(HostId h, HostPlace place, HostMode mode, Roster? men = null)
    {
        var a = Acc(h);
        a.PlaceSettlement = place.Settlement; a.PlaceXM = place.XM; a.PlaceYM = place.YM; a.Pasture = place.Pasture; a.Mode = mode;
        if (men != null) Array.Copy(men.Counts, a.Men, Roster.Roles);
    }

    /// <summary>A place at a settlement's centre.</summary>
    public HostPlace PlaceAt(int settlement, bool pasture = false)
    {
        var s = _pop.Region.Settlements[settlement];
        return new HostPlace(settlement, s.CenterX, s.CenterY, pasture);
    }

    /// <summary>
    /// The host is done: what the chest can pay of the arrears is paid, the men on campaign go home (labour returns), the soldiers' coin goes home (outsiders' share leaves with them), the unspent coin returns to the
    /// treasury the host was funded from, and the account is dropped. Goods stay in the baggage store (the lord's).
    /// </summary>
    public void Disband(HostId h)
    {
        var a = Acc(h); var rt = Resolve(a);
        PayArrears(a, rt, null);
        foreach (int p in Book.OnCampaign.Where(kv => kv.Value == a.Id).Select(kv => kv.Key).ToList()) Book.OnCampaign.Remove(p);
        SettleSoldiers(a, rt);
        var led = _econ.Ledger; long left = led.Balance(rt.Chest).Mites;
        if (left > 0 && !rt.Treasury.IsNone && rt.Treasury != rt.Chest) led.Transfer(rt.Chest, rt.Treasury, new Coin(left), TransferReason.Hoard);
        foreach (int s in Book.Blockades.Where(kv => kv.Value == a.Id).Select(kv => kv.Key).ToList()) SiegeBlockade(s, false, h);
        Book.Hosts.Remove(a.Id); _rt.Remove(a.Id); _last.Remove(a.Id);
    }

    public bool Exists(HostId h) => Book.Hosts.ContainsKey(h.Value);
    public IEnumerable<HostId> Hosts() { foreach (var id in Book.Hosts.Keys) yield return new HostId(id); }
    public HostAccount Account(HostId h) => Acc(h);
    public DayResult? Last(HostId h) => _last.GetValueOrDefault(h.Value);
    public PurseId ChestOf(HostId h) => Resolve(Acc(h)).Chest;
    public PurseId SoldiersPurse(HostId h) => Resolve(Acc(h)).Soldiers;
    public Store BaggageOf(HostId h) => Resolve(Acc(h)).Baggage;

    private HostAccount Acc(HostId h) => Book.Hosts.TryGetValue(h.Value, out var a) ? a : throw new KeyNotFoundException($"no {h}");

    private Rt Resolve(HostAccount a)
    {
        if (_rt.TryGetValue(a.Id, out var rt)) return rt;
        var led = _econ.Ledger;
        rt = new Rt();
        if (!led.TryFind((PurseKind)a.ChestKind, new EntityId(a.ChestOwner), out rt.Chest)) throw new InvalidDataException($"host {a.Id}: pay chest purse is missing");
        if (!_econ.OtherStores.TryGetValue(new EntityId(a.BaggageOwner), out var bag)) throw new InvalidDataException($"host {a.Id}: baggage store is missing");
        rt.Baggage = bag;
        rt.Soldiers = led.Open(PurseKind.Chest, HostIds.Soldiers(new HostId(a.Id)));
        if (a.TreasuryOwner != 0) led.TryFind((PurseKind)a.TreasuryKind, new EntityId(a.TreasuryOwner), out rt.Treasury);
        rt.Party = new MarketParty(new EntityId(a.BaggageOwner), rt.Chest, rt.Baggage, PartyKind.Merchant, -1 - a.Id);
        return _rt[a.Id] = rt;
    }

    // ------------------------------------------------------------------ needs

    /// <summary>A day's needs (army-economy 3.2): pure.</summary>
    public DayNeeds Needs(HostId h, int day) => NeedsOf(Acc(h), day);

    internal static DayNeeds NeedsOf(HostAccount a, int day)
    {
        var season = GameTime.FromGameHours((long)day * 24).Season;
        int men = a.Total;
        var (hay, oats) = SupplyConfig.Fodder(a.Animals);
        if (a.Pasture && season is Season.Summer or Season.Autumn) hay /= 2;
        long wood = (long)(men + a.Followers) * SupplyConfig.FirewoodG * (season == Season.Winter ? 2 : 1);
        long pay = 0;
        for (int r = 0; r < Roster.Roles; r++) pay += (long)a.Men[r] * WageToday(a, (TroopRole)r);
        return new DayNeeds((long)men * SupplyConfig.RationG + (long)a.Followers * SupplyConfig.FollowerRationG, (long)men * SupplyConfig.RelishG, hay, oats, wood, pay);
    }

    private static int WageToday(HostAccount a, TroopRole r) => r == TroopRole.Levy && a.ServiceDays < SupplyConfig.LevyFreeDays ? 0 : SupplyConfig.Wage(r);

    // ------------------------------------------------------------------ the daily call

    /// <summary>The host slot: every host, in id order, once for a day (hour 18 by <see cref="HostSupplySystem"/>).</summary>
    public void RunDay(int day)
    {
        if (day == _lastDay) return;
        _lastDay = day;
        foreach (var id in Book.Hosts.Keys.ToArray()) _last[id] = Supply(new HostId(id), day);
        foreach (var id in _last.Keys.Where(k => !Book.Hosts.ContainsKey(k)).ToArray()) _last.Remove(id);
    }

    /// <summary>
    /// Runs the ladder for the host's day (army-economy 4.1, first cut): each need from the baggage, then the markets within the buy radius at their asks (per-unit repricing), staple first; pay from the chest in the
    /// overview's order, then arrears with what is left; the hunger and unpaid days; the levies' service days. Coin and goods move only through the ledger and the stores.
    /// </summary>
    public DayResult Supply(HostId h, int day)
    {
        var a = Acc(h); var rt = Resolve(a);
        var need = NeedsOf(a, day);
        var res = new DayResult { Day = day, Host = h, Needed = need };
        var books = BooksInReach(a);
        var led = _econ.Ledger;
        long coin0 = led.Balance(rt.Chest).Mites;
        // staple first: the men's lives; then pay; then the animals, the fires and the relish with what the chest has left
        Fill(a, rt, res, SupplyNeed.Staple, need.StapleG, books, day);
        PayDay(a, rt, res, need);
        Fill(a, rt, res, SupplyNeed.Hay, need.FodderHayG, books, day);
        Fill(a, rt, res, SupplyNeed.Oats, need.FodderOatsG, books, day);
        Fill(a, rt, res, SupplyNeed.Firewood, need.FirewoodG, books, day);
        Fill(a, rt, res, SupplyNeed.Relish, need.RelishG, books, day);
        // the days short
        bool hungry = res.ShortG[(int)SupplyNeed.Staple] > 0;
        for (int r = 0; r < Roster.Roles; r++)
        {
            if (a.Men[r] == 0) { a.HungerDays[r] = 0; continue; }
            a.HungerDays[r] = hungry ? a.HungerDays[r] + 1 : 0;
        }
        if (a.Men[(int)TroopRole.Levy] > 0 || a.Total > 0) a.ServiceDays++;
        for (int r = 0; r < Roster.Roles; r++) { res.HungerDays[r] = a.HungerDays[r]; res.UnpaidDays[r] = a.UnpaidDays[r]; res.Arrears[r] = a.Arrears[r]; }
        long staples = 0; foreach (var g in _stapleGoods) staples += EqG(g, rt.Baggage.Qty(g));
        res.DaysOfFoodMilli = need.StapleG > 0 ? staples * 1000 / need.StapleG : 0;
        res.DaysFundedMilli = need.PayMites > 0 ? led.Balance(rt.Chest).Mites * 1000 / need.PayMites : 0;
        res.BoughtMites = Math.Max(0, coin0 - led.Balance(rt.Chest).Mites - res.PaidTotal);
        for (int n = 0; n < DayResult.Needs; n++)
        {
            for (int s = 0; s < DayResult.Sources; s++) a.Totals[n * DayResult.Sources + s] += res.Delivered[n, s];
            a.Totals[DayResult.Needs * DayResult.Sources + n] += res.ShortG[n];
        }
        return res;
    }

    // ------------------------------------------------------------------ the ladder

    private long EqG(ushort good, long qty) => (long)((Int128)qty * _econ.Goods.FoodMeasuresPpm(good) / SupplyConfig.GrainMeasurePpm);
    private bool IsStapleLike(SupplyNeed n) => n == SupplyNeed.Staple;

    private ushort[] GoodsOf(SupplyNeed n) => n switch
    {
        SupplyNeed.Staple => _stapleGoods, SupplyNeed.Relish => _relishGoods,
        SupplyNeed.Hay => _hay == ushort.MaxValue ? [] : [_hay], SupplyNeed.Oats => _oats == ushort.MaxValue ? [] : [_oats], _ => [_firewood],
    };

    /// <summary>The need's measure of a stock quantity of the good: staple goods by their food measures (a grain 1:1), the rest by mass.</summary>
    private long Measure(SupplyNeed n, ushort good, long qty) => IsStapleLike(n) ? EqG(good, qty) : qty;
    /// <summary>The smallest quantity of the good that measures at least <paramref name="eq"/>.</summary>
    private long QtyFor(SupplyNeed n, ushort good, long eq)
    {
        if (!IsStapleLike(n)) return eq;
        long ppm = Math.Max(1, _econ.Goods.FoodMeasuresPpm(good));
        return (long)(((Int128)eq * SupplyConfig.GrainMeasurePpm + ppm - 1) / ppm);
    }

    private void Fill(HostAccount a, Rt rt, DayResult res, SupplyNeed n, long want, List<MarketBook> books, int day)
    {
        if (want <= 0) return;
        var goods = GoodsOf(n);
        var why = n == SupplyNeed.Firewood ? DestroyReason.Burned : DestroyReason.Eaten;
        long left = want;
        // 1. baggage
        foreach (var g in goods)
        {
            if (left <= 0) break;
            long have = rt.Baggage.Qty(g); if (have <= 0) continue;
            long takeQty = Math.Min(have, QtyFor(n, g, left));
            long eq = Math.Min(left, Measure(n, g, takeQty));
            if (eq <= 0) continue;
            // eat exactly what covers the measure (the last unit may over-cover by a rounding gram)
            if (!rt.Baggage.Destroy(g, takeQty, why)) continue;
            res.Delivered[(int)n, (int)SupplySource.Baggage] += eq; left -= eq;
        }
        // 4. the market: books by distance, the cheapest good per measure first, at the books' asks
        if (left > 0 && goods.Length > 0)
        {
            foreach (var bk in books)
            {
                if (left <= 0) break;
                var order = goods.Where(g => bk.Goods[g].StockUnits > 0).OrderBy(g => bk.AskMicro(g) * SupplyConfig.GrainMeasurePpm / Math.Max(1, IsStapleLike(n) ? _econ.Goods.FoodMeasuresPpm(g) : SupplyConfig.GrainMeasurePpm)).ThenBy(g => g).ToArray();
                foreach (var g in order)
                {
                    if (left <= 0) break;
                    long units = QtyFor(n, g, left);
                    var t = bk.Buy(rt.Party, g, units, SupplyConfig.BuyMaxRatioPermille, day);
                    if (t.Qty <= 0) continue;
                    long qty = Math.Min(t.Qty, units);
                    long eq = Math.Min(left, Measure(n, g, qty));
                    rt.Baggage.Destroy(g, qty, why);
                    res.Delivered[(int)n, (int)SupplySource.Market] += eq; left -= eq;
                }
            }
        }
        res.ShortG[(int)n] = Math.Max(0, left);
    }

    private List<MarketBook> BooksInReach(HostAccount a)
    {
        var list = new List<(long D2, int S, MarketBook B)>();
        var region = _pop.Region; long r2 = (long)a.BuyRadiusM * a.BuyRadiusM;
        foreach (var b in Economy.Markets.AllBooks())
        {
            if (b.Blockaded) continue;
            var s = region.Settlements[b.SettlementIndex];
            long dx = s.CenterX - a.PlaceXM, dy = s.CenterY - a.PlaceYM, d2 = dx * dx + dy * dy;
            if (d2 <= r2) list.Add((d2, b.SettlementIndex, b));
        }
        list.Sort((x, y) => x.D2 != y.D2 ? x.D2.CompareTo(y.D2) : x.S.CompareTo(y.S));
        return [.. list.Select(x => x.B)];
    }

    // ------------------------------------------------------------------ pay

    private void PayDay(HostAccount a, Rt rt, DayResult res, DayNeeds need)
    {
        var led = _econ.Ledger;
        var members = LevyHouseholds(a);
        long short_ = 0;
        foreach (var role in SupplyConfig.PayOrder)
        {
            int r = (int)role, n = a.Men[r]; if (n == 0) continue;
            long due = (long)n * WageToday(a, role); if (due <= 0) { if (a.Arrears[r] == 0) a.UnpaidDays[r] = 0; continue; }
            long pay = Math.Min(due, led.Balance(rt.Chest).Mites);
            if (pay > 0) PayRole(a, rt, role, pay, members);
            res.Paid[r] += pay; res.Delivered[(int)SupplyNeed.Pay, (int)SupplySource.Baggage] += pay;
            if (pay < due) { a.Arrears[r] += due - pay; a.UnpaidDays[r]++; short_ += due - pay; }
        }
        PayArrears(a, rt, res);
        res.ShortG[(int)SupplyNeed.Pay] = short_;
        for (int r = 0; r < Roster.Roles; r++) if (a.Arrears[r] == 0) a.UnpaidDays[r] = 0;
    }

    /// <summary>What is left of the chest after the day's wages pays the arrears, in the pay order (<c>res</c> null at Disband).</summary>
    private void PayArrears(HostAccount a, Rt rt, DayResult? res)
    {
        var led = _econ.Ledger; var members = LevyHouseholds(a);
        foreach (var role in SupplyConfig.PayOrder)
        {
            int r = (int)role; if (a.Arrears[r] <= 0) continue;
            long pay = Math.Min(a.Arrears[r], led.Balance(rt.Chest).Mites); if (pay <= 0) continue;
            PayRole(a, rt, role, pay, members);
            a.Arrears[r] -= pay;
            if (res != null) { res.Paid[r] += pay; res.Delivered[(int)SupplyNeed.Pay, (int)SupplySource.Baggage] += pay; }
            if (a.Arrears[r] == 0) a.UnpaidDays[r] = 0;
        }
    }

    /// <summary>The households of the host's levies (person count by household, ascending), for the levies' pay.</summary>
    private SortedDictionary<int, int> LevyHouseholds(HostAccount a)
    {
        var d = new SortedDictionary<int, int>(); var hhOf = _pop.People.Household;
        foreach (var (p, host) in Book.OnCampaign) if (host == a.Id && hhOf[p] >= 0) d[hhOf[p]] = d.GetValueOrDefault(hhOf[p]) + 1;
        return d;
    }

    /// <summary>Pays a role's coin out of the chest: a levy person's share to his household's purse (so the home keeps what he earns), everything else to the host's soldiers' purse (settled at <see cref="Disband"/>).</summary>
    private void PayRole(HostAccount a, Rt rt, TroopRole role, long pay, SortedDictionary<int, int> levyHouseholds)
    {
        var led = _econ.Ledger; long toHouseholds = 0;
        if (role == TroopRole.Levy && levyHouseholds.Count > 0 && a.Men[(int)TroopRole.Levy] > 0)
        {
            int persons = 0; foreach (int c in levyHouseholds.Values) persons += c;
            long share = pay * Math.Min(persons, a.Men[(int)TroopRole.Levy]) / a.Men[(int)TroopRole.Levy], per = persons > 0 ? share / persons : 0;
            if (per > 0)
                foreach (var (hh, c) in levyHouseholds)
                    if (led.Transfer(rt.Chest, _econ.HouseholdPurse[hh], new Coin(per * c), TransferReason.Wage)) toHouseholds += per * c;
        }
        long rest = pay - toHouseholds;
        if (rest > 0) led.Transfer(rt.Chest, rt.Soldiers, new Coin(rest), TransferReason.Wage);
    }

    /// <summary>The soldiers' coin goes home: the outsiders' share leaves the region with them (the continent side takes it), the rest is spent at the settlement the levies came from, shared among its households (ascending).</summary>
    private void SettleSoldiers(HostAccount a, Rt rt)
    {
        var led = _econ.Ledger; long bal = led.Balance(rt.Soldiers).Mites; if (bal <= 0) return;
        int men = Math.Max(1, a.Total), outside = Math.Min(a.Outside, men);
        long away = a.Total > 0 ? bal * outside / men : 0;
        var link = Economy.World.Get<ContinentLink>();
        if (away > 0)
        {
            if (link != null) { if (led.Sink(rt.Soldiers, new Coin(away), SinkReason.Emigrate)) link.Mine.EmigrantCoin += away; }
            else away = 0;
        }
        long home = led.Balance(rt.Soldiers).Mites; if (home <= 0) return;
        var hhs = new List<int>(); int s = a.HomeSettlement >= 0 ? a.HomeSettlement : a.PlaceSettlement;
        var hh = _pop.Households;
        for (int i = 0; i < hh.Count; i++) if (hh.Settlement[i] == s && hh.MemberCount[i] > 0) hhs.Add(i);
        if (hhs.Count == 0) { if (!rt.Treasury.IsNone) led.Transfer(rt.Soldiers, rt.Treasury, new Coin(home), TransferReason.Hoard); return; }
        long per = home / hhs.Count;
        if (per > 0) foreach (int i in hhs) led.Transfer(rt.Soldiers, _econ.HouseholdPurse[i], new Coin(per), TransferReason.Wage);
        long rem = led.Balance(rt.Soldiers).Mites;
        if (rem > 0) led.Transfer(rt.Soldiers, _econ.HouseholdPurse[hhs[0]], new Coin(rem), TransferReason.Wage);
    }

    // ------------------------------------------------------------------ men

    /// <summary>The farming planner's hook: this person is on campaign and his household has lost his labour (army-economy 3.3, 4.9).</summary>
    public bool IsOnCampaign(int person) => Book.OnCampaign.ContainsKey(person);

    /// <summary>
    /// Levies from a fief (army-economy 3.3): the households of the fief's settlements in index order, one man each (a man aged 16 to 45, not the household's head when another can go, not on a staff, not already on campaign);
    /// a household that evades (<see cref="Evades"/>) gives none. The men are marked on campaign and counted as levies of the host.
    /// </summary>
    public MusterResult Muster(HostId h, int fief, int men)
    {
        var a = Acc(h); int day = Day;
        var hh = _pop.Households; var pt = _pop.People; var region = _pop.Region; var picked = new List<int>(); int evaded = 0, asked = 0;
        for (int i = 0; i < hh.Count && picked.Count < men; i++)
        {
            int s = hh.Settlement[i]; if (s < 0 || s >= region.Settlements.Count || region.Settlements[s].FiefIndex != fief || hh.MemberCount[i] == 0) continue;
            int pick = -1;
            foreach (int m in hh.Members(i))
            {
                if (pt.Sex[m] != Sex.Male || pt.Status[m] != PersonStatus.Alive || Book.OnCampaign.ContainsKey(m) || Economy.Labour.IsStaff(m)) continue;
                int age = pt.AgeYears(m, day); if (age < SupplyConfig.LevyMinAge || age > SupplyConfig.LevyMaxAge) continue;
                if (pick < 0 || (pick == hh.Head[i] && m != hh.Head[i])) pick = m;
                if (m != hh.Head[i]) break;
            }
            if (pick < 0) continue;
            asked++;
            if (Evades?.Invoke(s, i, day) == true) { evaded++; continue; }
            picked.Add(pick);
            if (a.HomeSettlement < 0) a.HomeSettlement = s;
        }
        foreach (int p in picked) Book.OnCampaign[p] = a.Id;
        a.Men[(int)TroopRole.Levy] += picked.Count;
        return new MusterResult(picked, evaded, asked);
    }

    /// <summary>The persons the host has on campaign, ascending.</summary>
    public IReadOnlyList<int> Persons(HostId h) => [.. Book.OnCampaign.Where(kv => kv.Value == h.Value).Select(kv => kv.Key)];

    /// <summary>Home again: the persons' labour returns to their households and they leave the levy count.</summary>
    public void Release(HostId h, IReadOnlyList<int> persons)
    {
        var a = Acc(h); int n = 0;
        foreach (int p in persons) if (Book.OnCampaign.TryGetValue(p, out int host) && host == a.Id) { Book.OnCampaign.Remove(p); n++; }
        a.Men[(int)TroopRole.Levy] = Math.Max(0, a.Men[(int)TroopRole.Levy] - n);
    }

    /// <summary>The military's decision: the persons leave. The levies keep their kit, the arrears owed to deserters are forgiven (the share of the levy's arrears they stood for) and they walk home.</summary>
    public void Desert(HostId h, IReadOnlyList<int> persons)
    {
        var a = Acc(h); int before = a.Men[(int)TroopRole.Levy];
        Release(h, persons);
        ForgiveArrears(a, TroopRole.Levy, before, before - a.Men[(int)TroopRole.Levy]);
    }

    /// <summary>Deserters who are counts of the roster rather than persons (retinue, mercenaries, men from outside): the same settlement.</summary>
    public int DesertCounts(HostId h, TroopRole role, int count)
    {
        var a = Acc(h); int before = a.Men[(int)role], n = Math.Min(count, before);
        if (n <= 0) return 0;
        a.Men[(int)role] -= n;
        ForgiveArrears(a, role, before, n);
        a.Outside = Math.Min(a.Outside, a.Total);
        return n;
    }

    private static void ForgiveArrears(HostAccount a, TroopRole role, int before, int gone)
    {
        if (before <= 0 || gone <= 0) return;
        a.Arrears[(int)role] -= a.Arrears[(int)role] * gone / before;
        if (a.Men[(int)role] == 0) { a.Arrears[(int)role] = 0; a.UnpaidDays[(int)role] = 0; }
    }

    // ------------------------------------------------------------------ settlements and the land

    /// <summary>A blockade on or off (army-economy 4.8): the settlement's book takes no imports and sends no exports (caravans, the regional carters and the export desk skip it), and the host does not shop there.</summary>
    public void SiegeBlockade(int settlement, bool on, HostId besieger)
    {
        var bk = BookOf(settlement);
        if (on) Book.Blockades[settlement] = besieger.Value; else Book.Blockades.Remove(settlement);
        if (bk != null) bk.Blockaded = on;
    }

    public bool IsBlockaded(int settlement) => Book.Blockades.ContainsKey(settlement);

    /// <summary>Thousandths of a day of staple the settlement holds (its households' stores, its book's stock when it has its own, and the stores its id owns) over what its people eat (1,100 g of grain a person a day).</summary>
    public long DaysOfFoodMilli(int settlement)
    {
        var hh = _pop.Households; long eq = 0, people = 0;
        for (int i = 0; i < hh.Count; i++)
        {
            if (hh.Settlement[i] != settlement || hh.MemberCount[i] == 0) continue;
            people += hh.MemberCount[i];
            eq += StapleEq(_econ.HouseholdStore[i]);
        }
        var bk = Economy.Markets.Books[settlement] ?? null;
        if (bk != null) eq += StapleEq(bk.BookStore);
        if (_econ.OtherStores.TryGetValue(_pop.Region.Settlements[settlement].Id, out var own)) eq += StapleEq(own);
        return people == 0 ? 0 : eq * 1000 / (people * 1100);
    }

    /// <summary>Whole days of food in the settlement (army-economy 3.4).</summary>
    public int DaysOfFood(int settlement) => (int)(DaysOfFoodMilli(settlement) / 1000);

    private long StapleEq(Store s) { long t = 0; foreach (var g in _stapleGoods) t += EqG(g, s.Qty(g)); if (_oats != ushort.MaxValue) t += EqG(_oats, s.Qty(_oats)); return t; }

    /// <summary>Kg of harvest lost if <paramref name="menAbsent"/> men miss <paramref name="days"/> days of the harvest window (army-economy 4.9): men x min(days, the window's 15) x 10 hours x the region's harvest per worker-hour (last year's harvest over its farming hands' hours in the window).</summary>
    public long HarvestRisk(int fief, int menAbsent, int days)
    {
        var farming = Economy.Farming; var sums = farming.State.Summaries;
        long harvestG = sums.Count > 0 ? sums[^1].HarvestG : farming.State.HarvestG;
        long hands = 0; var pt = _pop.People; var hh = _pop.Households; int day = Day;
        for (int i = 0; i < hh.Count; i++) { if (hh.MemberCount[i] == 0) continue; foreach (int m in hh.Members(i)) { int age = pt.AgeYears(m, day); if (pt.Status[m] == PersonStatus.Alive && age >= 16 && age < 60) hands++; } }
        long hours = Math.Max(1, hands * SupplyConfig.LevyHoursPerDay * TimeConst.DaysPerSeason);
        long perHourG = harvestG / hours;
        return (long)menAbsent * Math.Min(days, TimeConst.DaysPerSeason) * SupplyConfig.LevyHoursPerDay * perHourG / 1000;
    }

    /// <summary>A host pitches camp at a settlement: <see cref="CampSize"/> reads it (the disease rule's <c>ArmyCampSize</c> hook is bound by the military's wiring).</summary>
    public void Camp(HostId h, int settlement) { var a = Acc(h); a.PlaceSettlement = settlement; a.Mode = HostMode.Camp; }

    /// <summary>Men camped at or besieging a settlement.</summary>
    public int CampSize(int settlement) { int t = 0; foreach (var a in Book.Hosts.Values) if (a.PlaceSettlement == settlement && a.Mode is HostMode.Camp or HostMode.Siege) t += a.Total; return t; }

    /// <summary>The pay chest over a day's pay for the realm's hosts, in thousandths of a day (army-economy 3.4, first cut: the hosts' chests only; the treasury and loan room join in P7-AE2).</summary>
    public long DaysFundedMilli(int realm)
    {
        long coin = 0, pay = 0; int day = Day;
        foreach (var a in Book.Hosts.Values) { if (a.Realm != realm) continue; coin += _econ.Ledger.Balance(Resolve(a).Chest).Mites; pay += NeedsOf(a, day).PayMites; }
        return pay > 0 ? coin * 1000 / pay : 0;
    }
}

/// <summary>The host slot (overview 8.4: <c>HostSupply</c> 1,150, H1, acts only at hour 18): the day's <see cref="HostSupply.RunDay"/>. A no-op without a service in the world's bag, so a game with no host pays nothing and draws nothing.</summary>
[AbstractSystem]
public sealed class HostSupplySystem : AbstractSystem
{
    public HostSupplySystem() : base("host_supply", TickPhase.H1Caravans, SystemOrder.HostSupply) { }
    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 18) return;
        var hs = ctx.World.Get<HostSupply>();
        if (hs == null || hs.Book.Hosts.Count == 0) return;
        hs.RunDay((int)ctx.Day);
    }
}
