using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Production;

// P7-LB2 (large-buildings 4.4): what the large buildings' rooms do. Pure rules over the recognised rooms (RoomV2 with its Primary type) and the works' state in WorksBook. Integer arithmetic only, rooms and
// items walked in key order. A building is named by the key of its room's origin cell (RoomWorld.Key).

/// <summary>A manufactory hall's effects on the business whose works stand in it (4.4): the floor space a worker needs and the overseers' span.</summary>
public readonly record struct HallEffects(int CrowdingM2, int SpanBonus, int MaxWorkers)
{
    public static HallEffects None(LargeBuildingRulesDef r, int area) => new(r.CrowdingM2, 0, area / Math.Max(1, r.CrowdingM2));
}

/// <summary>What an armoury's furniture holds (4.4): a stand one body armour and its helmet; a rack 10 weapons of up to 3 kg or 4 polearms; a shelf 10 helmets or 6 shields; a chest 40 sheaves of arrows.</summary>
public readonly record struct ArmouryCapacity(int Stands, int Racks, int Shelves, int Chests, int Armours, int Weapons, int Polearms, int Helmets, int Shields, int Sheaves);

/// <summary>The kinds of kit an armoury keeps.</summary>
public enum ArmsKind : byte { None, BodyArmour, Helmet, Weapon, Polearm, Shield }

/// <summary>One item of the armoury view: kind, shape, quality tier and condition, and whether an armourer's care covers it.</summary>
public sealed record ArmouryRow(EntityId Item, ArmsKind Kind, string Shape, ItemQuality Quality, int ConditionPermille, bool Cared, bool Issued);

/// <summary>An issue request: so many of a shape, no worse than a quality tier (the military's loadout filler calls this).</summary>
public sealed record ArmsRequest(string Shape, int Count, ItemQuality MinQuality = ItemQuality.Crude);

/// <summary>What <see cref="Armouries.Issue"/> gave: the items issued and, by shape, how many could not be.</summary>
public sealed record IssueResult(IReadOnlyList<EntityId> Items, IReadOnlyDictionary<string, int> Gaps);

public static class LargeRooms
{
    public const string Hall = "manufactory_hall", Warehouse = "warehouse", Depot = "granary_depot", Armoury = "armoury", Barracks = "barracks", Ropewalk = "ropewalk";

    public static long KeyOf(RoomV2 r) => RoomWorld.Key(r.Origin.X, r.Origin.Y, r.Z);
    public static bool Is(RoomV2 r, string type) => r.Primary?.Id == type;

    /// <summary>A hall's crowding floor (3 m2 a worker instead of 4) and overseers' span (+4: one view across the floor); any other room gets the ordinary rule.</summary>
    public static HallEffects HallOf(RoomV2? r, LargeBuildingRulesDef rules)
    {
        if (r == null) return new HallEffects(rules.CrowdingM2, 0, 0);
        return Is(r, Hall) ? new HallEffects(rules.HallCrowdingM2, rules.HallSpanBonus, r.Area / Math.Max(1, rules.HallCrowdingM2)) : HallEffects.None(rules, r.Area);
    }

    public static ArmouryCapacity ArmouryOf(RoomV2 r, LargeBuildingRulesDef rules)
    {
        int stands = r.Tags.GetValueOrDefault("armour_stand"), racks = r.Tags.GetValueOrDefault("weapon_rack"), shelves = r.Tags.GetValueOrDefault("arms_shelf"), chests = r.Tags.GetValueOrDefault("arms_chest");
        return new ArmouryCapacity(stands, racks, shelves, chests, stands * rules.StandArmours, racks * rules.RackWeapons, racks * rules.RackPolearms, stands + shelves * rules.ShelfHelmets, shelves * rules.ShelfShields, chests * rules.ChestSheaves);
    }

    /// <summary>The litres a warehouse stacks (the room's own storage figure: the floor in m2 times 1,500).</summary>
    public static long WarehouseLitres(RoomV2 r) => Is(r, Warehouse) ? r.StorageL : 0;

    /// <summary>The kilograms of grain a depot holds: 750 a m2 of floor.</summary>
    public static long DepotKg(RoomV2 r, LargeBuildingRulesDef rules) => Is(r, Depot) ? (long)r.Area * rules.DepotKgPerM2 : 0;

    /// <summary>A barracks: its beds (the bunks' two each), the floor it gives a man (at least 3 m2) and the straw it burns a 15 days.</summary>
    public static int BarracksBeds(RoomV2 r) => Is(r, Barracks) ? r.Beds : 0;
    public static long StrawKgPerPeriod(RoomV2 r, LargeBuildingRulesDef rules) => (long)BarracksBeds(r) * rules.BarracksStrawKgPerBed;
}

/// <summary>The armouries (4.4): capacity by furniture, the items each holds, the care that keeps them from rusting, the issue book, and the armoury view.</summary>
public sealed class Armouries
{
    private readonly WorksBook _book;
    private readonly ItemTable _items;
    private readonly GoodCatalog _cat;
    private readonly LargeBuildingRulesDef _rules;
    private readonly Dictionary<string, (ArmsKind Kind, bool Polearm)> _shapes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _materialCategory = new(StringComparer.Ordinal);

    public Armouries(WorksBook book, ItemTable items, GoodCatalog cat, ContentDatabase db, LargeBuildingRulesDef rules)
    {
        _book = book; _items = items; _cat = cat; _rules = rules;
        foreach (var w in db.All<WeaponShapeDef>()) _shapes[w.Id] = (ArmsKind.Weapon, w.Family == "polearm");
        foreach (var s in db.All<ShieldShapeDef>()) _shapes[s.Id] = (ArmsKind.Shield, false);
        foreach (var a in db.All<ArmourShapeDef>())
        {
            bool head = a.Coverage.GetValueOrDefault("head") > 0, body = a.Coverage.GetValueOrDefault("upper_body") >= 0.5;
            if (a.Layer < ArmourLayer.Armour) continue;
            if (head && !body) _shapes[a.Id] = (ArmsKind.Helmet, false);
            else if (body) _shapes[a.Id] = (ArmsKind.BodyArmour, false);
        }
        foreach (var m in db.All<MaterialDef>()) _materialCategory[m.Id] = m.Category ?? "";
    }

    /// <summary>The holder an armoury's items belong to while they are stocked.</summary>
    public static EntityId HolderOf(long key) => EntityId.Make(IdKind.Other, (1UL << 53) | (ulong)key);

    public ArmsKind KindOf(int row)
    {
        ref var r = ref _items.At(row);
        if (!_shapes.TryGetValue(r.Shape, out var s)) return ArmsKind.None;
        if (s.Kind != ArmsKind.Weapon) return s.Kind;
        return s.Polearm || _items.MassG(row, _cat) > _rules.RackWeaponMaxG ? ArmsKind.Polearm : ArmsKind.Weapon;
    }

    /// <summary>The items stocked in an armoury, in id order (issued ones are not in the stock).</summary>
    public IReadOnlyList<EntityId> Stock(long key) => _book.ArmouryStock.TryGetValue(key, out var s) ? [.. s.Select(v => new EntityId(v))] : [];

    private (int Body, int HelmOnStand, int HelmOnShelf, int Weapons, int Polearms, int Shields) Use(long key, ArmouryCapacity cap)
    {
        int body = 0, helm = 0, weapons = 0, pole = 0, shields = 0;
        foreach (var id in Stock(key))
        {
            int row = _items.IndexOf(id); if (row < 0) continue;
            switch (KindOf(row)) { case ArmsKind.BodyArmour: body++; break; case ArmsKind.Helmet: helm++; break; case ArmsKind.Weapon: weapons++; break; case ArmsKind.Polearm: pole++; break; case ArmsKind.Shield: shields++; break; }
        }
        int onStand = Math.Min(helm, cap.Stands);
        return (body, onStand, helm - onStand, weapons, pole, shields);
    }

    /// <summary>Whether one more item of this kind fits: stands hold armours and their helmets, a rack's ten places are weapons or (4 to a rack) polearms, a shelf's ten places are helmets or shields (6 to a shelf).</summary>
    public bool Fits(long key, ArmouryCapacity cap, ArmsKind kind)
    {
        var u = Use(key, cap);
        switch (kind)
        {
            case ArmsKind.BodyArmour: return u.Body < cap.Armours;
            case ArmsKind.Helmet: return u.HelmOnStand < cap.Stands || (u.HelmOnShelf + 1) * 6 + u.Shields * 10 <= cap.Shelves * 60;
            case ArmsKind.Weapon: return (u.Weapons + 1) * 4 + u.Polearms * 10 <= cap.Racks * 40;
            case ArmsKind.Polearm: return u.Weapons * 4 + (u.Polearms + 1) * 10 <= cap.Racks * 40;
            case ArmsKind.Shield: return u.HelmOnShelf * 6 + (u.Shields + 1) * 10 <= cap.Shelves * 60;
            default: return false;
        }
    }

    /// <summary>Puts an item into an armoury when it fits (its owner becomes the armoury's holder). False: no place for it, or it is not kit.</summary>
    public bool Add(long key, ArmouryCapacity cap, EntityId item)
    {
        int row = _items.IndexOf(item); if (row < 0) return false;
        var kind = KindOf(row);
        if (kind == ArmsKind.None || !Fits(key, cap, kind)) return false;
        if (_book.ArmouryStock.Values.Any(s => s.Contains(item.Value))) return false;
        if (!_book.ArmouryStock.TryGetValue(key, out var set)) _book.ArmouryStock[key] = set = [];
        set.Add(item.Value);
        _items.At(row).Owner = HolderOf(key);
        return true;
    }

    /// <summary>The items by kind, quality tier and condition (the armoury panel's list), the stocked ones and the issued ones.</summary>
    public IReadOnlyList<ArmouryRow> View(long key, int armourers = 0)
    {
        var rows = new List<ArmouryRow>();
        var ids = Stock(key);
        int careable = armourers * _rules.ItemsPerArmourer;
        for (int i = 0; i < ids.Count; i++)
        {
            int row = _items.IndexOf(ids[i]); if (row < 0) continue;
            ref var r = ref _items.At(row);
            rows.Add(new ArmouryRow(ids[i], KindOf(row), r.Shape, r.Quality, r.ConditionPermille, i < careable, false));
        }
        foreach (var iss in _book.Issues)
        {
            if (iss.Armoury != key) continue;
            int row = _items.IndexOf(new EntityId(iss.Item)); if (row < 0) continue;
            ref var r = ref _items.At(row);
            rows.Add(new ArmouryRow(new EntityId(iss.Item), KindOf(row), r.Shape, r.Quality, r.ConditionPermille, false, true));
        }
        return rows;
    }

    /// <summary>The gaps against a loadout template (shape and count): how many of each the armoury lacks.</summary>
    public IReadOnlyDictionary<string, int> Gaps(long key, IReadOnlyDictionary<string, int> template)
    {
        var have = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in Stock(key)) { int row = _items.IndexOf(id); if (row >= 0) have[_items.At(row).Shape] = have.GetValueOrDefault(_items.At(row).Shape) + 1; }
        var gaps = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (shape, n) in template) { int g = n - have.GetValueOrDefault(shape); if (g > 0) gaps[shape] = g; }
        return gaps;
    }

    /// <summary>Issues kit to a holder: for each request the best available (quality tier, then condition, then id); what is lacking comes back as gaps. Each item issued goes in the issue book.</summary>
    public IssueResult Issue(long key, IReadOnlyList<ArmsRequest> requests, EntityId holder, int day)
    {
        var given = new List<EntityId>(); var gaps = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var rq in requests)
        {
            var pool = Stock(key).Select(id => (Id: id, Row: _items.IndexOf(id))).Where(x => x.Row >= 0 && _items.At(x.Row).Shape == rq.Shape && _items.At(x.Row).Quality >= rq.MinQuality)
                .OrderByDescending(x => _items.At(x.Row).Quality).ThenByDescending(x => _items.At(x.Row).ConditionPermille).ThenBy(x => x.Id.Value).ToList();
            int n = Math.Min(rq.Count, pool.Count);
            for (int i = 0; i < n; i++)
            {
                var it = pool[i];
                _book.ArmouryStock[key].Remove(it.Id.Value);
                _items.At(it.Row).Owner = holder;
                _book.Issues.Add(new IssueRow(key, it.Id.Value, holder.Value, day));
                given.Add(it.Id);
            }
            if (n < rq.Count) gaps[rq.Shape] = gaps.GetValueOrDefault(rq.Shape) + rq.Count - n;
        }
        return new IssueResult(given, gaps);
    }

    /// <summary>Takes issued items back (with whatever wear they have): each must be in the issue book of this armoury. Returns how many went back.</summary>
    public int Return(long key, IReadOnlyList<EntityId> items)
    {
        int back = 0;
        foreach (var id in items)
        {
            int at = _book.Issues.FindIndex(i => i.Armoury == key && i.Item == id.Value);
            int row = _items.IndexOf(id);
            if (at < 0) continue;
            _book.Issues.RemoveAt(at);
            if (row < 0) continue;                                         // lost or broken on campaign: the book is cleared, nothing returns
            _items.At(row).Owner = HolderOf(key);
            if (!_book.ArmouryStock.TryGetValue(key, out var set)) _book.ArmouryStock[key] = set = [];
            set.Add(id.Value); back++;
        }
        return back;
    }

    /// <summary>The hours an armourer's care takes every <c>care_days</c>: 0.1 h an item, so 300 items take 30 h (4.4, LB-10).</summary>
    public double CareHours(int items) => items * _rules.CareMinutes / 60.0;

    /// <summary>How many items an armoury's armourers keep.</summary>
    public int CareCapacity(int armourers) => armourers * _rules.ItemsPerArmourer;

    /// <summary>The permille of rust an item of iron or steel takes in a day: the house rate (2, mail 4) uncared, doubled in a winter with no hearth in the building; a cared item takes a quarter (the sealed rate).</summary>
    public int RustPm(bool mail, bool cared, bool winterNoHearth, int day)
    {
        int rate = Corrosion.RustPmPerDay * (mail ? Corrosion.MailRustMult : 1);
        if (cared) return Corrosion.OiledShare(rate, day);
        return winterNoHearth ? rate * _rules.ColdMultPm / 1000 : rate;
    }

    /// <summary>
    /// One game day of care for an armoury: the armourers' first <c>300 x armourers</c> items (by id) are cared (oiled: a quarter of the rust), the rest are not (the house rate, doubled in a winter with no hearth in the
    /// building); items that fall to nothing break into scrap in the armoury's scrap chest. Idempotent for a day already done. Returns the condition lost, in permille summed.
    /// </summary>
    public int Day(long key, int day, int armourers, bool winter, bool hearthInBuilding, Store? scrap)
    {
        if (_book.CareDay.TryGetValue(key, out var last) && last >= day) return 0;
        _book.CareDay[key] = day;
        if (!_book.ArmouryStock.TryGetValue(key, out var set)) return 0;
        int careable = CareCapacity(armourers), n = 0, lost = 0;
        foreach (var v in set.ToList())
        {
            var id = new EntityId(v); int row = _items.IndexOf(id);
            if (row < 0) { set.Remove(v); continue; }
            bool cared = n++ < careable;
            ref var r = ref _items.At(row);
            if (cared) r.Flags |= ItemFlags.Oiled; else r.Flags &= ~ItemFlags.Oiled;
            bool mail = r.Shape.Contains("mail", StringComparison.Ordinal);
            int passes = !cared && winter && !hearthInBuilding ? _rules.ColdMultPm / 1000 : 1;
            for (int p = 0; p < passes && _items.IndexOf(id) >= 0; p++)
                lost += Corrosion.Day(_items, _items.IndexOf(id), Exposure.Damp, day, mail, cared ? int.MaxValue : -1, scrap, m => _materialCategory.GetValueOrDefault(m));
            if (_items.IndexOf(id) < 0) set.Remove(v);
        }
        return lost;
    }
}

/// <summary>Warehouses (4.4): the litres a floor stacks, the lease book and the season's rent. A warehouse's goods spoil by <see cref="StoreKind.Warehouse"/>: grain at the granary rate, packed goods at the sealed rate, the rest at half the house rate.</summary>
public sealed class Warehouses
{
    private readonly WorksBook _book;
    private readonly LargeBuildingRulesDef _rules;
    public Warehouses(WorksBook book, LargeBuildingRulesDef rules) { _book = book; _rules = rules; }

    public static EntityId OwnerOf(long key) => EntityId.Make(IdKind.Other, (1UL << 54) | (ulong)key);

    public long LeasedM3(long key) => _book.Leases.Where(l => l.Warehouse == key).Sum(l => (long)l.M3);
    public long CapacityM3(RoomV2 room) => LargeRooms.WarehouseLitres(room) / 1000;
    public long FreeM3(long key, RoomV2 room) => CapacityM3(room) - LeasedM3(key);

    /// <summary>The rent for a season: the m3 times the owner's price (default 20 mites a m3).</summary>
    public int RentFor(int m3, int? pricePerM3 = null) => m3 * (pricePerM3 ?? _rules.RentMitesPerM3Season);

    /// <summary>Opens a lease of <paramref name="m3"/> in a warehouse room, tenant and owner by purse. False when the room is no warehouse, there is no room left, or the tenant already rents there.</summary>
    public bool Lease(long key, RoomV2 room, PurseId tenant, PurseId owner, int m3, int season, int? pricePerM3 = null)
    {
        if (m3 <= 0 || !LargeRooms.Is(room, LargeRooms.Warehouse) || m3 > FreeM3(key, room)) return false;
        if (_book.Leases.Any(l => l.Warehouse == key && l.Tenant == tenant.Index)) return false;
        _book.Leases.Add(new WarehouseLease(key, tenant.Index, owner.Index, m3, RentFor(m3, pricePerM3), season, 0));
        return true;
    }

    public bool EndLease(long key, PurseId tenant) => _book.Leases.RemoveAll(l => l.Warehouse == key && l.Tenant == tenant.Index) > 0;

    /// <summary>A season's rent: each lease pays from the tenant's purse to the owner's (Transfer, reason Rent); one the tenant cannot pay lapses. Returns (paid, lapsed).</summary>
    public (int Paid, int Lapsed) Season(int season, Ledger ledger)
    {
        int paid = 0, lapsed = 0;
        foreach (var l in _book.Leases.OrderBy(l => l.Warehouse).ThenBy(l => l.Tenant).ToList())
        {
            if (l.FromSeason >= season) continue;                                    // the season it was signed is not rented yet
            int at = _book.Leases.IndexOf(l);
            if (ledger.Transfer(new PurseId(l.Tenant), new PurseId(l.Owner), new Coin(l.RentMites), TransferReason.Rent)) { _book.Leases[at] = l with { PaidSeasons = l.PaidSeasons + 1 }; paid++; }
            else { _book.Leases.RemoveAt(at); lapsed++; }
        }
        return (paid, lapsed);
    }
}

/// <summary>Granary depots (4.4): the kilograms a floor holds, intake and release by lots (their season of intake), the rotation of grain older than two seasons, the target and its chronicle.</summary>
public sealed class Depots
{
    private readonly WorksBook _book;
    private readonly GoodCatalog _cat;
    private readonly LargeBuildingRulesDef _rules;
    public Depots(WorksBook book, GoodCatalog cat, LargeBuildingRulesDef rules) { _book = book; _cat = cat; _rules = rules; }

    public static EntityId OwnerOf(long key) => EntityId.Make(IdKind.Other, (1UL << 55) | (ulong)key);

    public bool IsGrain(ushort good) => _cat.Def(good).SpoilClass == "grain";

    public DepotPolicy Policy(long key) => _book.Depots.TryGetValue(key, out var p) ? p : new DepotPolicy(key, 0, false, false);
    public void SetPolicy(long key, long targetKg, bool rotation)
    {
        var old = Policy(key);
        _book.Depots[key] = new DepotPolicy(key, Math.Max(0, targetKg), rotation, old.Filled && targetKg > 0);
    }

    /// <summary>The grain in the depot (grams, over the store).</summary>
    public long GrainG(long key, Store store) { long g = 0; for (int i = 0; i < store.RowCount; i++) if (IsGrain(store.GoodAt(i))) g += store.QtyAt(i); return g; }

    /// <summary>Moves grain into the depot's store (whatever fits) and records the lot by its season. Returns the grams taken in; not grain: nothing.</summary>
    public long Intake(long key, Store depot, Store from, ushort good, long grams, int season)
    {
        if (!IsGrain(good)) return 0;
        long q = Store.MoveUpTo(from, depot, good, grams);
        if (q > 0) _book.Lots.Add(new DepotLot(key, _cat.Def(good).Id, q, season));
        return q;
    }

    /// <summary>Takes grain out, oldest lots first; returns the grams moved.</summary>
    public long Release(long key, Store depot, Store to, ushort good, long grams)
    {
        long q = Store.MoveUpTo(depot, to, good, grams);
        Take(key, _cat.Def(good).Id, q, int.MaxValue);
        return q;
    }

    /// <summary>Makes the lots agree with the store (spoilage takes from the oldest first): never more in lots than in the store.</summary>
    public void Reconcile(long key, Store depot)
    {
        foreach (var good in _book.Lots.Where(l => l.Depot == key).Select(l => l.Good).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList())
        {
            long have = depot.Qty(good), lots = _book.Lots.Where(l => l.Depot == key && l.Good == good).Sum(l => l.Grams);
            if (lots > have) Take(key, good, lots - have, int.MaxValue);
        }
    }

    /// <summary>Removes <paramref name="grams"/> from the oldest lots of a good (at most those of season <paramref name="upToSeason"/> and earlier).</summary>
    private long Take(long key, string good, long grams, int upToSeason)
    {
        long left = grams;
        for (int i = 0; i < _book.Lots.Count && left > 0; i++)
        {
            var l = _book.Lots[i];
            if (l.Depot != key || l.Good != good || l.Season > upToSeason) continue;
            long t = Math.Min(left, l.Grams); left -= t;
            if (t == l.Grams) { _book.Lots.RemoveAt(i); i--; } else _book.Lots[i] = l with { Grams = l.Grams - t };
        }
        return grams - left;
    }

    /// <summary>The grams of a good held in lots from <paramref name="upToSeason"/> and earlier.</summary>
    public long OldGrams(long key, string good, int upToSeason) => _book.Lots.Where(l => l.Depot == key && l.Good == good && l.Season <= upToSeason).Sum(l => l.Grams);

    /// <summary>
    /// A season's step for one depot: the lots are reconciled to the store; with rotation on, grain older than the rotation age (default 2 seasons) is offered to <paramref name="sell"/> (good, grams: returns the grams
    /// it took out of the depot's store at the harvest market); the target is checked (true when it was just reached: the caller writes <c>depot_filled</c>). Returns (grams sold, target newly reached).
    /// </summary>
    public (long Sold, bool Reached) Season(long key, Store depot, int season, Func<ushort, long, long>? sell)
    {
        Reconcile(key, depot);
        long sold = 0;
        var p = Policy(key);
        if (p.Rotation && sell != null)
        {
            int cutoff = season - _rules.DepotRotateSeasons;
            foreach (var good in _book.Lots.Where(l => l.Depot == key).Select(l => l.Good).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList())
            {
                long old = OldGrams(key, good, cutoff);
                if (old <= 0) continue;
                long took = sell(_cat.IdOf(good), old);
                if (took > 0) { Take(key, good, took, cutoff); sold += took; }
            }
        }
        long total = GrainG(key, depot);
        bool reached = false;
        if (p.TargetKg > 0)
        {
            bool filled = total >= p.TargetKg * 1000;
            if (filled && !p.Filled) reached = true;
            if (filled != p.Filled) _book.Depots[key] = p with { Filled = filled };
        }
        return (sold, reached);
    }
}

/// <summary>Barracks (4.4): the beds a room of bunks gives, the floor a man has, and the straw the room burns (1 kg a bed every 15 days). Who sleeps there is the military's.</summary>
public static class Barracks
{
    /// <summary>A barracks needs a hearth in its building and at least 3 m2 of floor a man (two to a bunk).</summary>
    public static bool Valid(RoomV2 r, bool hearthInBuilding) => LargeRooms.Is(r, LargeRooms.Barracks) && hearthInBuilding && r.Area >= r.Beds * 3;

    /// <summary>The straw a barracks burns this day: its beds' kilograms on every 15th day, else 0 (grams).</summary>
    public static long StrawGramsToday(RoomV2 r, LargeBuildingRulesDef rules, int day) => day % Math.Max(1, rules.BarracksStrawDays) == 0 ? LargeRooms.StrawKgPerPeriod(r, rules) * 1000 : 0;

    /// <summary>Burns the day's straw from an owner's store (a counted sink, Consumed); returns the grams short (the beds go cold).</summary>
    public static long Upkeep(RoomV2 r, LargeBuildingRulesDef rules, int day, Store owner, ushort straw)
    {
        long need = StrawGramsToday(r, rules, day);
        if (need <= 0) return 0;
        long burnt = owner.DestroyUpTo(straw, need, DestroyReason.Consumed);
        return need - burnt;
    }
}
