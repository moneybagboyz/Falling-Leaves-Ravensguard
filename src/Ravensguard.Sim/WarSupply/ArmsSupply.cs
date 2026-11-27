using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.WarSupply;

/// <summary>A maker of arms in the world (P8-WS-2): an armourer or a works, its owner id, realm and settlement, its purse and store, and the minutes of work its hands do in a day.</summary>
public sealed record ArmsMaker(EntityId Owner, int Realm, int Settlement, PurseId Purse, Store Store, int MinutesPerDay);

/// <summary>The lord an order is for: his id and purse, his realm and settlement, and the armoury (room key) the kit goes to.</summary>
public sealed record ArmsDepot(EntityId Lord, PurseId Purse, int Realm, int Settlement, long Armoury);

/// <summary>A realm's arms depot for the trade: the store its bar iron is held in (a realm beyond the ledger has none).</summary>
public sealed record ArmsRealmDepot(int Realm, EntityId Owner, Store Store, int Settlement);

/// <summary>How long a load takes between two settlements: days on the road, and whether it goes by ship.</summary>
public readonly record struct ArmsRoute(int Days, bool Ship);

/// <summary>
/// P8-WS-2 (war-screens-and-arms 3.2, 3.3): the arms industry in play. A lord's order is a supply contract (<c>arms_supply</c>, Phase 6 contracts v2) with the whole price in escrow, offered to the makers of his realm and then
/// abroad (an export ban stops the contract: a banned maker is not offered it, and a made load waits at the gate). The maker draws the materials from its own store (bar iron and the soft part: real goods, destroyed as
/// <c>Materialised</c> into real items), works the pieces' minutes, and the kit goes by cart or ship to the armoury; in transit the items belong to the load's own owner id, so a robbery moves them to the robbers and the
/// order is short. Pay is on delivery, a piece at a time, from the escrow (late: <c>max(500, 1000 - 100 x weeks)</c> permille), and what is not paid goes back to the lord's purse. A steward places the season's order within
/// a budget; a commission buys the missing kit as goods from a supplier and has an armourer fit it at the armoury. The arms trade between realms ships real bar iron the same way. Coin moves only by ledger transfer;
/// goods only through stores; the mass audit stays exact (an item is a materialised good, a salvaged or exported one goes the other way).
/// </summary>
public sealed class ArmsSupply
{
    public const int StatOrders = 0, StatDelivered = 1, StatPaid = 2, StatLateCut = 3, StatRobbed = 4, StatTradeG = 5, StatFitted = 6, StatBanned = 7, StatShort = 8;
    /// <summary>The transit's states: on the road, held at the gate by a ban, in the making (arrives on its day).</summary>
    public const byte OnRoad = 0, Held = 1, Making = 4;

    private readonly EconomyState _econ;
    private readonly ArmsWorld _arms;
    private readonly Armouries _arm;
    private readonly ContractWorld _cw;
    private readonly ContractBook _cb;
    private static readonly ArmouryCapacity Roomy = new(600, 600, 600, 60, 600, 6000, 2400, 6000, 3600, 2400);

    public ArmsSupply(EconomyState econ, ArmsWorld arms, Armouries armouries, ContractWorld cw, ContractBook cb) { _econ = econ; _arms = arms; _arm = armouries; _cw = cw; _cb = cb; }

    public ArmsBook Book => _arms.Book;
    private long[] Stats => Book.SupplyStats;

    /// <summary>The makers of arms, in any order (they are sorted by id here).</summary>
    public Func<IEnumerable<ArmsMaker>> Makers { get; set; } = () => [];
    /// <summary>The armoury's capacity (the room's stands and racks).</summary>
    public Func<long, ArmouryCapacity> Capacity { get; set; } = _ => Roomy;
    /// <summary>The road between two settlements; the default is a cart over <c>CartMPerDay</c>.</summary>
    public Func<int, int, ArmsRoute>? Route { get; set; }
    /// <summary>Where a commission buys its parts: the store and purse of the merchant at a settlement; null: none.</summary>
    public Func<int, (Store Store, PurseId Purse)?>? Supplier { get; set; }
    /// <summary>The arms depot of a realm for the trade (null: the realm is beyond the ledger).</summary>
    public Func<int, ArmsRealmDepot?>? RealmDepot { get; set; }

    public static EntityId TransitId(int id) => EntityId.Make(IdKind.Other, 0x4A_0000_0000_0000UL | (uint)id);
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ orders

    /// <summary>
    /// The season's arms order of a lord as a supply contract (3.2): the gap between the armoury and <paramref name="want"/> (default <paramref name="kits"/> levy kits), less what his live orders still owe, ordered piece by piece
    /// while the cost stays inside <paramref name="budgetMites"/> less what his orders of this season already cost. The cost goes into escrow at once and the contract is offered to the makers. Nothing is ordered when nothing fits
    /// or the purse cannot pay.
    /// </summary>
    public ArmsOrderResult PlaceOrder(ArmsDepot d, int kits, long budgetMites, int day, IReadOnlyDictionary<string, int>? want = null)
    {
        int season = day / TimeConst.DaysPerSeason;
        var gaps = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (k, v) in _arm.Gaps(d.Armoury, want ?? _arms.Template(kits))) gaps[k] = v;
        long pending = 0;
        foreach (var j in Book.Jobs.Values.Where(j => j.Kind == 0 && j.Lord == d.Lord.Value))
        {
            var c = _cb.Get(j.Contract); if (c == null || !c.Live) continue;
            foreach (var l in j.Lines) if (gaps.TryGetValue(l.Shape, out int g)) gaps[l.Shape] = Math.Max(0, g - (l.Count - l.Delivered));
            if (c.PostedDay / TimeConst.DaysPerSeason == season) pending += c.PayMites;
        }
        long left = budgetMites - pending, total = 0, mass = 0; int pieces = 0;
        var ordered = new SortedDictionary<string, int>(StringComparer.Ordinal); var deferred = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var lines = new List<ArmsLine>();
        foreach (var p in _arms.Rules.LevyKit)
        {
            if (!gaps.TryGetValue(p.Shape, out int gap) || gap <= 0) continue;
            int n = p.PriceMites <= 0 ? gap : (int)Math.Min(gap, Math.Max(0, left) / p.PriceMites);
            if (n > 0)
            {
                long cost = (long)n * p.PriceMites; left -= cost; total += cost; pieces += n; mass += (long)n * (p.IronG + p.SoftG); ordered[p.Shape] = n;
                lines.Add(new ArmsLine { Shape = p.Shape, Count = n, Price = p.PriceMites });
            }
            if (n < gap) deferred[p.Shape] = gap - n;
        }
        if (pieces == 0) return new ArmsOrderResult(gaps, ordered, deferred, 0, budgetMites);
        var note = Write("arms_ordered", "ch_war_arms_ordered", day, 5, d.Lord != EntityId.None ? [d.Lord] : [], [ChronParam.Person("lord", d.Lord), ChronParam.Count("kits", pieces), ChronParam.Mites("budget", total)], "ch_note_war_arms",
            ("kind", "order"), ("lord", N((long)d.Lord.Value)));
        var contract = ContractSystem.Post(_cw, _cb, "arms_supply", d.Lord, d.Purse, d.Settlement, total, day, note, c => { c.Grams = mass; c.Param = pieces; c.Target = Armouries.HolderOf(d.Armoury); });
        if (contract == null) return new ArmsOrderResult(gaps, new SortedDictionary<string, int>(StringComparer.Ordinal), gaps.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value), 0, budgetMites);
        var job = new ArmsJob { Contract = contract.Id, Kind = 0, Lord = d.Lord.Value, Purse = d.Purse.Index, Realm = d.Realm, Settlement = d.Settlement, Armoury = d.Armoury, Lines = lines };
        Book.Jobs[job.Contract] = job;
        Stats[StatOrders]++; _arms.Book.Stats[3] += pieces;
        Offer(job, contract, day);
        return new ArmsOrderResult(gaps, ordered, deferred, total, budgetMites);
    }

    /// <summary>
    /// A refit by commission (3.3): the lord names a template and a budget; the gaps of his armoury against it, as many pieces as the budget buys at the pieces' prices, become a <c>refit_commission</c> contract (the budget's
    /// worth in escrow) offered to an armourer of the realm, nearest first. The armourer buys the missing parts as goods from the settlement's supplier, works the hours and fits the pieces at the armoury.
    /// </summary>
    public ArmsOrderResult Commission(ArmsDepot d, IReadOnlyDictionary<string, int> template, long budgetMites, int day)
    {
        var gaps = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (k, v) in _arm.Gaps(d.Armoury, template)) gaps[k] = v;
        foreach (var j in Book.Jobs.Values.Where(j => j.Kind == 1 && j.Lord == d.Lord.Value && _cb.Get(j.Contract) is { Live: true }))
            foreach (var l in j.Lines) if (gaps.TryGetValue(l.Shape, out int g)) gaps[l.Shape] = Math.Max(0, g - (l.Count - l.Delivered));
        long left = budgetMites, total = 0; int pieces = 0;
        var ordered = new SortedDictionary<string, int>(StringComparer.Ordinal); var deferred = new SortedDictionary<string, int>(StringComparer.Ordinal); var lines = new List<ArmsLine>();
        foreach (var p in _arms.Rules.LevyKit)
        {
            if (!gaps.TryGetValue(p.Shape, out int gap) || gap <= 0) continue;
            int n = p.PriceMites <= 0 ? gap : (int)Math.Min(gap, Math.Max(0, left) / p.PriceMites);
            if (n > 0) { long cost = (long)n * p.PriceMites; left -= cost; total += cost; pieces += n; ordered[p.Shape] = n; lines.Add(new ArmsLine { Shape = p.Shape, Count = n, Price = p.PriceMites }); }
            if (n < gap) deferred[p.Shape] = gap - n;
        }
        if (pieces == 0) return new ArmsOrderResult(gaps, ordered, deferred, 0, budgetMites);
        var note = Write("arms_ordered", "ch_war_arms_ordered", day, 5, d.Lord != EntityId.None ? [d.Lord] : [], [ChronParam.Person("lord", d.Lord), ChronParam.Count("kits", pieces), ChronParam.Mites("budget", total)], "ch_note_war_arms",
            ("kind", "commission"), ("lord", N((long)d.Lord.Value)));
        var contract = ContractSystem.Post(_cw, _cb, "refit_commission", d.Lord, d.Purse, d.Settlement, total, day, note, c => { c.Param = pieces; c.Target = Armouries.HolderOf(d.Armoury); });
        if (contract == null) return new ArmsOrderResult(gaps, new SortedDictionary<string, int>(StringComparer.Ordinal), gaps.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value), 0, budgetMites);
        var job = new ArmsJob { Contract = contract.Id, Kind = 1, Lord = d.Lord.Value, Purse = d.Purse.Index, Realm = d.Realm, Settlement = d.Settlement, Armoury = d.Armoury, Lines = lines };
        Book.Jobs[job.Contract] = job;
        Offer(job, contract, day);
        return new ArmsOrderResult(gaps, ordered, deferred, total, budgetMites);
    }

    // ------------------------------------------------------------------ the steward

    /// <summary>Hires (or re-sets) a steward for a lord: each season he places the arms order against the armoury for <paramref name="kits"/> levy kits (or the want he is given) within <paramref name="budgetMites"/> a season.</summary>
    public void HireSteward(ulong lord, long armoury, long budgetMites, int kits)
    {
        if (Book.Stewards.TryGetValue(lord, out var w)) { w.Armoury = armoury; w.BudgetMites = Math.Max(0, budgetMites); w.Kits = Math.Max(0, kits); return; }
        Book.Stewards[lord] = new ArmsSteward { Lord = lord, Armoury = armoury, BudgetMites = Math.Max(0, budgetMites), Kits = Math.Max(0, kits) };
    }

    /// <summary>Lets the steward go; his live orders stand.</summary>
    public bool DismissSteward(ulong lord) => Book.Stewards.Remove(lord);

    /// <summary>
    /// The season's orders of the stewards, in lord id order, once a season each: <paramref name="depotOf"/> says where the lord's purse, realm and armoury are; <paramref name="wantOf"/> (optional) gives the templates' demand,
    /// piece by piece, in place of the levy kit. Returns the orders placed.
    /// </summary>
    public IReadOnlyList<(ulong Lord, ArmsOrderResult Result)> StewardSeason(int day, Func<ulong, ArmsDepot?> depotOf, Func<ulong, IReadOnlyDictionary<string, int>?>? wantOf = null)
    {
        int season = day / TimeConst.DaysPerSeason; var done = new List<(ulong, ArmsOrderResult)>();
        foreach (var w in Book.Stewards.Values.ToList())
        {
            if (w.LastSeason >= season) continue;
            var depot = depotOf(w.Lord); if (depot == null) continue;
            w.LastSeason = season;
            done.Add((w.Lord, PlaceOrder(depot with { Armoury = w.Armoury }, w.Kits, w.BudgetMites, day, wantOf?.Invoke(w.Lord))));
        }
        return done;
    }

    // ------------------------------------------------------------------ offering

    private IEnumerable<ArmsMaker> MakersFor(ArmsJob j) => Makers().Where(m => m.MinutesPerDay > 0)
        .OrderBy(m => j.Kind == 1 ? (m.Settlement == j.Settlement ? 0 : m.Realm == j.Realm ? 1 : 2) : (m.Realm == j.Realm ? 0 : 1)).ThenBy(m => m.Owner.Value);

    /// <summary>Offers an open contract to the makers: those of the lord's realm first, then abroad, in id order. A banned maker is skipped (the ban stops the contract). An order goes to a maker that holds materials for a piece; a commission to an armourer of the realm.</summary>
    private bool Offer(ArmsJob j, WorkContract c, int day)
    {
        bool banned = false;
        foreach (var m in MakersFor(j))
        {
            if (_arms.Book.Banned(m.Realm, j.Realm)) { banned = true; continue; }
            if (j.Kind == 1 && m.Realm != j.Realm) continue;
            if (j.Kind == 0 && !j.Lines.Any(l => _arms.Piece(l.Shape) is { } p && Affordable(m.Store, p, 1) > 0)) continue;
            if (ContractSystem.Take(_cw, _cb, c.Id, m.Owner, m.Purse, day, byCompany: true) != TakeResult.Taken) continue;
            j.Maker = m.Owner.Value; j.MakerPurse = m.Purse.Index; j.Phase = 1;
            return true;
        }
        if (banned && day == c.PostedDay) Stats[StatBanned]++;
        return false;
    }

    private int Affordable(Store s, ArmsKitPieceDef p, int n)
    {
        long k = n; foreach (var (g, grams) in _arms.PieceGoods(p)) k = Math.Min(k, s.Qty(g) / grams);
        return (int)Math.Max(0, k);
    }

    // ------------------------------------------------------------------ the day

    /// <summary>
    /// One game day, once a day: loads in the making come out and go on the road (or wait at the gate under a ban), loads on the road arrive (or are held), open contracts are offered or lapse on their deadline, makers start what
    /// their materials allow, and finished or given-up contracts close. Ascending ids.
    /// </summary>
    public void Day(int day)
    {
        foreach (var t in Book.Transit.Values.ToList())
        {
            if (t.Kind == 1) { TradeStep(t, day); continue; }
            switch (t.State)
            {
                case Making when t.Arrive <= day: Finish(t, day); break;
                case Held: Dispatch(t, day); break;
                case OnRoad when t.Arrive <= day: Arrive(t, day); break;
            }
        }
        foreach (var j in Book.Jobs.Values.ToList())
        {
            var c = _cb.Get(j.Contract);
            if (c == null || c.Closed) { Book.Jobs.Remove(j.Contract); continue; }
            int lapse = j.Kind == 0 ? c.DeadlineDay + _arms.Rules.LapseDays : c.DeadlineDay;
            if (c.State == WorkState.Open)
            {
                if (day >= c.DeadlineDay) { ContractSystem.Expire(_cw, _cb, c, day); Book.Jobs.Remove(j.Contract); continue; }
                if (!Offer(j, c, day)) continue;
            }
            if (c.State == WorkState.Taken && day < lapse) StartWork(j, c, day);
            Settle(j, c, day, lapse);
        }
    }

    /// <summary>The maker starts what is left: for an order from its own store, for a commission after buying the missing parts from the supplier (paid from the escrow at the books' base price). The pieces are made at once as items held by the load's id and come out after the hours.</summary>
    private void StartWork(ArmsJob j, WorkContract c, int day)
    {
        var maker = Makers().FirstOrDefault(m => m.Owner.Value == j.Maker); if (maker == null) return;
        var items = _econ.Items; var batch = new List<ulong>(); long minutes = 0, mass = 0;
        foreach (var line in j.Lines)
        {
            int rem = line.Count - line.Delivered - line.Made - line.Making; if (rem <= 0) continue;
            var p = _arms.Piece(line.Shape); if (p == null) continue;
            if (j.Kind == 1) Buy(j, c, maker, p, rem);
            int n = Affordable(maker.Store, p, rem); if (n <= 0) continue;
            foreach (var (g, grams) in _arms.PieceGoods(p)) if (!maker.Store.Destroy(g, grams * n, DestroyReason.Materialised)) throw new InvalidOperationException("the maker's materials vanished");
            var parts = new List<ItemComponent>();
            if (p.IronG > 0) parts.Add(new ItemComponent("iron", p.IronG, 1000));
            if (p.SoftG > 0) parts.Add(new ItemComponent(p.SoftMaterial.Length > 0 ? p.SoftMaterial : "oak", p.SoftG, 1000));
            int tid = Book.NextTransit;
            for (int i = 0; i < n; i++) batch.Add(items.Add(_cw.Ids, p.Shape, ItemQuality.Common, TransitId(tid), parts, conditionPermille: 1000).Value);
            line.Making += n; minutes += _arms.PieceMinutes(p, n); mass += (long)n * (p.IronG + p.SoftG);
        }
        if (batch.Count == 0) return;
        int id = Book.NextTransit++; int days = (int)Math.Max(1, (minutes + maker.MinutesPerDay - 1) / maker.MinutesPerDay);
        Book.Transit[id] = new ArmsTransit { Id = id, Job = j.Contract, Kind = 0, State = Making, From = j.Maker, To = Armouries.HolderOf(j.Armoury).Value, FromRealm = maker.Realm, ToRealm = j.Realm, Depart = day, Arrive = day + days, Grams = mass, Items = batch };
    }

    /// <summary>A commission's armourer buys the parts it lacks for <paramref name="n"/> pieces from the settlement's supplier, paid from the contract's escrow at the books' base price (a ledger transfer; the goods move between stores).</summary>
    private void Buy(ArmsJob j, WorkContract c, ArmsMaker maker, ArmsKitPieceDef p, int n)
    {
        var sup = Supplier?.Invoke(j.Settlement); if (sup == null) return;
        var (store, purse) = sup.Value; var led = _cw.Ledger; var escrow = new PurseId(c.Escrow); var cat = _econ.Goods;
        foreach (var (g, grams) in _arms.PieceGoods(p))
        {
            long need = grams * n - maker.Store.Qty(g); if (need <= 0) continue;
            long per = Math.Max(1, cat.BasePriceMilliMites(g));
            long can = Math.Min(need, store.Qty(g)); if (can <= 0) continue;
            long cost = per * can / 1_000_000;
            if (cost > led.Balance(escrow).Mites) { cost = led.Balance(escrow).Mites; can = per <= 0 ? 0 : cost * 1_000_000 / per; if (can <= 0) continue; }
            if (cost > 0 && !led.Transfer(escrow, purse, new Coin(cost), TransferReason.Trade)) continue;
            Store.Move(store, maker.Store, g, can);
        }
    }

    /// <summary>The making is done at the load's day: an order's pieces set out for the armoury; a commission's are fitted at once and the armourer's labour is paid.</summary>
    private void Finish(ArmsTransit t, int day)
    {
        if (!Book.Jobs.TryGetValue(t.Job, out var j)) { Release(t, day); return; }
        // the lines' counts: what was in the making is made now
        foreach (var l in j.Lines) { int n = CountShape(t, l.Shape); l.Making = Math.Max(0, l.Making - n); l.Made += n; }
        if (j.Kind == 0) { Dispatch(t, day); return; }
        Fit(t, j, day);
    }

    private int CountShape(ArmsTransit t, string shape) { int n = 0; foreach (var i in t.Items) { var id = new EntityId(i); if (_econ.Items.IsLive(id) && _econ.Items.Get(id).Shape == shape) n++; } return n; }

    /// <summary>A commission's pieces are fitted at the armoury (the room takes them, else the lord keeps them), each paid its price less the parts already bought; the contract closes when every piece is fitted.</summary>
    private void Fit(ArmsTransit t, ArmsJob j, int day)
    {
        var c = _cb.Get(j.Contract); if (c == null) { Release(t, day); return; }
        var led = _cw.Ledger; var items = _econ.Items; long paid = 0; int fitted = 0;
        foreach (ulong v in t.Items)
        {
            var id = new EntityId(v); if (!items.IsLive(id)) continue;
            ref var r = ref items.Get(id); if (r.Owner != TransitId(t.Id)) continue; string shape = r.Shape;
            var line = j.Lines.First(l => l.Shape == shape); var p = _arms.Piece(shape)!;
            if (!_arm.Add(j.Armoury, Capacity(j.Armoury), id)) items.Get(id).Owner = new EntityId(j.Lord);
            line.Made = Math.Max(0, line.Made - 1); line.Delivered++; fitted++;
            long labour = Math.Max(0, line.Price - _arms.PartsMites(p));
            long pay = Math.Min(labour, led.Balance(new PurseId(c.Escrow)).Mites);
            if (pay > 0 && led.Transfer(new PurseId(c.Escrow), new PurseId(j.MakerPurse), new Coin(pay), TransferReason.ContractPay)) paid += pay;
        }
        c.Delivered += fitted; Stats[StatFitted] += fitted; Stats[StatPaid] += paid;
        Book.Transit.Remove(t.Id);
        if (fitted > 0)
            Write("arms_fitted", "ch_war_arms_fitted", day, 9, j.Lord != 0 ? [new EntityId(j.Lord)] : [], [ChronParam.Count("pieces", fitted), ChronParam.Mites("paid", paid)], "ch_note_war_arms_fitted", ("contract", N(c.Id)));
    }

    /// <summary>Sets a made load on the road: held at the gate when the maker's realm bans exports to the lord's; otherwise by cart (or ship) for the days the road takes.</summary>
    private void Dispatch(ArmsTransit t, int day)
    {
        if (_arms.Book.Banned(t.FromRealm, t.ToRealm)) { if (t.State != Held) Stats[StatBanned]++; t.State = Held; return; }
        var j = Book.Jobs.GetValueOrDefault(t.Job);
        int from = Makers().FirstOrDefault(m => m.Owner.Value == t.From)?.Settlement ?? (j?.Settlement ?? -1), to = j?.Settlement ?? -1;
        var route = RouteOf(from, to);
        t.State = OnRoad; t.Mode = (byte)(route.Ship ? 1 : 0); t.Depart = day; t.Arrive = day + Math.Max(1, route.Days);
    }

    private ArmsRoute RouteOf(int from, int to)
    {
        if (Route != null) return Route(from, to);
        long m = from < 0 || to < 0 ? 0 : _cw.DistanceM(from, to);
        return new ArmsRoute((int)Math.Max(1, (m + _arms.Rules.CartMPerDay - 1) / Math.Max(1, _arms.Rules.CartMPerDay)), false);
    }

    /// <summary>The load reaches the armoury: each piece goes onto the stands (the lord keeps it when the room is full), and each is paid its price at the day's late rate from the escrow to the maker.</summary>
    private void Arrive(ArmsTransit t, int day)
    {
        if (!Book.Jobs.TryGetValue(t.Job, out var j) || _cb.Get(j.Contract) is not { } c) { Release(t, day); return; }
        var led = _cw.Ledger; var items = _econ.Items; long paid = 0, cut = 0, mass = 0; int n = 0;
        int pm = _arms.LatePm(day - c.DeadlineDay);
        foreach (ulong v in t.Items)
        {
            var id = new EntityId(v); if (!items.IsLive(id)) continue;
            ref var r = ref items.Get(id); if (r.Owner != TransitId(t.Id)) continue; string shape = r.Shape;
            var line = j.Lines.FirstOrDefault(l => l.Shape == shape); if (line == null) continue;
            mass += items.MassG(items.IndexOf(id), _econ.Goods);
            if (!_arm.Add(j.Armoury, Capacity(j.Armoury), id)) items.Get(id).Owner = new EntityId(j.Lord);
            line.Made = Math.Max(0, line.Made - 1); line.Delivered++; n++;
            long due = line.Price * pm / 1000, pay = Math.Min(due, led.Balance(new PurseId(c.Escrow)).Mites);
            if (pay > 0 && led.Transfer(new PurseId(c.Escrow), new PurseId(j.MakerPurse), new Coin(pay), TransferReason.ContractPay)) { paid += pay; cut += line.Price - pay; }
        }
        c.Delivered += mass; Stats[StatDelivered] += n; Stats[StatPaid] += paid; Stats[StatLateCut] += cut;
        Book.Transit.Remove(t.Id);
        if (n > 0)
            Write("arms_delivered", "ch_war_arms_delivered", day, 9, j.Lord != 0 ? [new EntityId(j.Lord)] : [], [ChronParam.Count("pieces", n), ChronParam.Mites("paid", paid)], "ch_note_war_arms_delivered", ("contract", N(c.Id)), ("pm", N(pm)));
    }

    /// <summary>A load whose job is gone: its items stay with the lord (they were made for him), nothing is paid.</summary>
    private void Release(ArmsTransit t, int day)
    {
        _ = day; var tid = TransitId(t.Id);
        foreach (ulong v in t.Items) { var id = new EntityId(v); if (_econ.Items.IsLive(id) && _econ.Items.Get(id).Owner == tid) _econ.Items.Get(id).Owner = new EntityId(t.To); }
        Book.Transit.Remove(t.Id);
    }

    /// <summary>Closes a contract that is done (every piece in, the rest of the escrow back to the lord's purse) or given up past its lapse day with no load still on the road (what was delivered was paid; the rest is short and refunded).</summary>
    private void Settle(ArmsJob j, WorkContract c, int day, int lapse)
    {
        var led = _cw.Ledger;
        bool all = j.Lines.All(l => l.Delivered >= l.Count);
        bool live = Book.Transit.Values.Any(t => t.Job == j.Contract);
        if (!all && (day < lapse || live)) return;
        long paid = c.PayMites - led.Balance(new PurseId(c.Escrow)).Mites; int delivered = j.Lines.Sum(l => l.Delivered), short_ = j.Lines.Sum(l => Math.Max(0, l.Count - l.Delivered));
        Stats[StatShort] += short_;
        if (all || delivered > 0) ContractSystem.CompleteSettled(_cw, c, day, paid); else ContractSystem.Fail(_cw, _cb, c, day, "lapsed");
        Book.Jobs.Remove(j.Contract);
    }

    // ------------------------------------------------------------------ the road

    /// <summary>
    /// A load on the road is taken (3.2, WS-7): its kit (or bar) goes to the robbers, a robber's owner id and, for goods, a store; the lines forget what was made of it so a maker with materials may make it again before the
    /// order lapses, and the order is short otherwise. Returns the pieces (or the grams of goods) taken. Writes <c>arms_robbed</c>.
    /// </summary>
    public long Rob(int transit, EntityId robber, Store? robberStore, int day)
    {
        if (!Book.Transit.TryGetValue(transit, out var t) || t.State == Making) return 0;
        var items = _econ.Items; var tid = TransitId(t.Id); long took = 0, grams = 0;
        if (t.Kind == 0)
        {
            Book.Jobs.TryGetValue(t.Job, out var j);
            foreach (ulong v in t.Items)
            {
                var id = new EntityId(v); if (!items.IsLive(id)) continue;
                int row = items.IndexOf(id); if (items.At(row).Owner != tid) continue;
                grams += items.MassG(row, _econ.Goods); items.At(row).Owner = robber; took++;
                if (j?.Lines.FirstOrDefault(l => l.Shape == items.At(row).Shape) is { } line) line.Made = Math.Max(0, line.Made - 1);
            }
        }
        else
        {
            var store = _econ.OpenStore(StoreKind.Chest, tid, 0, 0, enforce: false);
            foreach (var (g, q) in store.Rows()) { grams += _econ.Goods.MassG(g, q); took += q; if (robberStore != null) Store.MoveUpTo(store, robberStore, g, q); else store.Destroy(g, q, DestroyReason.Lost); }
        }
        Stats[StatRobbed] += took;
        Book.Transit.Remove(t.Id);
        if (took > 0)
            Write("arms_robbed", "ch_war_arms_robbed", day, 12, t.Kind == 0 && Book.Jobs.TryGetValue(t.Job, out var jj) && jj.Lord != 0 ? [new EntityId(jj.Lord)] : [], [ChronParam.Count("kg", grams / 1000)], "ch_note_war_arms_robbed", ("transit", N(t.Id)));
        return took;
    }

    /// <summary>The loads on the road for a contract (an order's), in id order.</summary>
    public IEnumerable<ArmsTransit> LoadsOf(int contract) => Book.Transit.Values.Where(t => t.Job == contract);

    /// <summary>The mass, in grams, of the kit in transit (items held by a load's owner id): what the books say is on the road.</summary>
    public long InTransitG()
    {
        long g = 0; var items = _econ.Items;
        foreach (var t in Book.Transit.Values) { var tid = TransitId(t.Id); if (t.Kind == 0) foreach (ulong v in t.Items) { var id = new EntityId(v); if (items.IsLive(id) && items.Get(id).Owner == tid) g += items.MassG(items.IndexOf(id), _econ.Goods); } else g += _econ.OpenStore(StoreKind.Chest, tid, 0, 0, enforce: false).TotalMassG(); }
        return g;
    }

    // ------------------------------------------------------------------ the trade

    /// <summary>The season's arms trade with real goods (3.2): bar iron leaves the exporter's depot into a transit store and reaches the importer's days later; a realm beyond the ledger imports the bar at the dispatch and exports it at the arrival. A ban stops the caravan before anything is loaded.</summary>
    public ArmsTradeResult TradeSeason(int day, IReadOnlyList<(int Realm, bool AtWar)> realms, Func<int, int, long, bool> pay) => _arms.Season(day, realms, pay, new Shipper(this));

    private sealed class Shipper(ArmsSupply s) : IArmsShipper
    {
        public long Available(int e, int i, long wantG)
        {
            var from = s.RealmDepot?.Invoke(e); var to = s.RealmDepot?.Invoke(i);
            if (from == null && to == null) return wantG;
            return from == null ? wantG : Math.Min(wantG, from.Store.Qty(s._econ.Goods.IdOf("iron_bar")));
        }
        public void Dispatch(int e, int i, long grams, int day) => s.DispatchTrade(e, i, grams, day);
    }

    private void DispatchTrade(int e, int i, long grams, int day)
    {
        var from = RealmDepot?.Invoke(e); var to = RealmDepot?.Invoke(i);
        if ((from == null && to == null) || grams <= 0) return;
        ushort bar = _econ.Goods.IdOf("iron_bar");
        int id = Book.NextTransit++; var tid = TransitId(id);
        var store = _econ.OpenStore(StoreKind.Chest, tid, 0, 0, enforce: false);
        if (from != null) { grams = Store.MoveUpTo(from.Store, store, bar, grams); if (grams <= 0) { Book.NextTransit--; return; } }
        else store.Create(bar, grams, CreateReason.Import);                                                                 // a realm beyond the ledger sends its bar in
        var route = RouteOf(from?.Settlement ?? -1, to?.Settlement ?? -1);
        Book.Transit[id] = new ArmsTransit { Id = id, Job = 0, Kind = 1, Mode = (byte)(route.Ship ? 1 : 0), State = OnRoad, From = from?.Owner.Value ?? 0, To = to?.Owner.Value ?? 0, FromRealm = e, ToRealm = i, Depart = day, Arrive = day + Math.Max(1, route.Days), Grams = grams, Good = bar, Qty = grams };
        Stats[StatTradeG] += grams;
    }

    private void TradeStep(ArmsTransit t, int day)
    {
        if (t.State != OnRoad || t.Arrive > day) return;
        var tid = TransitId(t.Id); var store = _econ.OpenStore(StoreKind.Chest, tid, 0, 0, enforce: false); var to = RealmDepot?.Invoke(t.ToRealm);
        ushort bar = (ushort)t.Good; long have = store.Qty(bar);
        if (to == null) { if (have > 0) store.Destroy(bar, have, DestroyReason.Export); }                                  // a realm beyond the ledger takes its bar out of the world
        else { Store.MoveUpTo(store, to.Store, bar, have); if (store.Qty(bar) > 0) { t.Arrive = day + 1; return; } }       // the depot is full: the carts wait a day at its gate
        Book.Transit.Remove(t.Id);
    }

    // ------------------------------------------------------------------ the chronicle

    private ChronicleEntry Write(string type, string key, int day, int hour, IReadOnlyList<EntityId> subjects, IReadOnlyList<ChronParam> ps, string note, params (string, string)[] data)
    {
        var d = new Dictionary<string, string>(); foreach (var (k, v) in data) d[k] = v;
        var e = new ChronicleEntry(_cw.Ids.Allocate(IdKind.Event), new GameTime(day * 86_400L + hour * 3600L), type, subjects, [new Cause(CauseKind.Supply, null, NoteKey: note)], d, TextKey: key, Params: ps);
        _cw.Chronicle.Add(e);
        return e;
    }
}
