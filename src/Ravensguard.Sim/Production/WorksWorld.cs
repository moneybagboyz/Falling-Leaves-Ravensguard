using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Production;

/// <summary>
/// P7-LB2 (large-buildings 4.4, 5): the large buildings at work over the home region's recognised rooms: armoury care once a game day, warehouse rents, depot rotation and targets and barracks straw once a season
/// (a 15 days for the straw), the hall's span for the businesses whose works stand in one, and the machine-powered labour of a station (the hook <see cref="ProductionSystem.StationLabourPm"/>). The state is the
/// works book's (the "works" section); the rooms and the nets are derived. A business is tied to its premises by a link to a cell of its building (<see cref="LinkPremises"/>).
/// </summary>
public sealed class WorksWorld
{
    public WorksBook Book { get; }
    public LargeBuildingRulesDef Rules { get; }
    public Armouries Armouries { get; }
    public Warehouses Warehouses { get; }
    public Depots Depots { get; }
    private readonly ContentDatabase _db;
    private readonly EconomyState _econ;
    private readonly Func<RoomWorld?> _rooms;
    private readonly Dictionary<string, string> _stationKind = new(StringComparer.Ordinal);

    /// <summary>The power rules whose nets drive the machines of a works (null: none stand).</summary>
    public PowerWorld? Power { get; set; }
    public ProductionSystem? Prod { get; set; }
    /// <summary>The harvest market's purchase for a depot's rotation: (depot key, good, grams) to the grams it took out of the depot's store; null: no rotation sale happens.</summary>
    public Func<long, ushort, long, long>? SellGrain { get; set; }
    public PowerChronicle? Chronicle { get; set; }

    public WorksWorld(WorksBook book, ContentDatabase db, EconomyState econ, Func<RoomWorld?> rooms, LargeBuildingRulesDef? rules = null)
    {
        Book = book; _db = db; _econ = econ; _rooms = rooms;
        Rules = rules ?? db.All<LargeBuildingRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new LargeBuildingRulesDef { Id = "large_buildings" };
        Armouries = new Armouries(book, econ.Items, econ.Goods, db, Rules);
        Warehouses = new Warehouses(book, Rules);
        Depots = new Depots(book, econ.Goods, Rules);
        foreach (var s in db.All<CraftStationDef>()) foreach (var p in s.Pieces) _stationKind[p] = s.Kind ?? s.Id;
    }

    public RoomWorld? Rooms => _rooms();

    /// <summary>The recognised rooms of a large type, in key order.</summary>
    public IEnumerable<(long Key, RoomV2 Room)> RoomsOf(string type) =>
        (Rooms?.Rooms ?? []).Where(r => LargeRooms.Is(r, type)).Select(r => (LargeRooms.KeyOf(r), r)).OrderBy(x => x.Item1);

    public RoomV2? RoomAtKey(long key) { var (x, y, z) = RoomWorld.Unpack(key); return Rooms?.RoomAt(x, y, z); }

    // ------------------------------------------------------------------ premises

    /// <summary>Ties a business to its premises: a cell of the building its works stand in. False when no room is recognised there.</summary>
    public bool LinkPremises(ulong business, int x, int y, int z)
    {
        if (Rooms?.RoomAt(x, y, z) == null) return false;
        Book.Premises[business] = RoomWorld.Key(x, y, z);
        return true;
    }

    public void UnlinkPremises(ulong business) => Book.Premises.Remove(business);

    public RoomV2? PremisesOf(Business b) => Book.Premises.TryGetValue(b.Id.Value, out var cell) ? RoomAtKey(cell) : null;

    /// <summary>The span a manufactory hall adds to each overseer of a business whose works stand in one (4.4).</summary>
    public int SpanBonus(Business b) => LargeRooms.HallOf(PremisesOf(b), Rules).SpanBonus;

    /// <summary>
    /// A station's worker-hours in permille while the machines of its premises run (4.1, 4.3): the lowest <c>labour_pm</c> among the working machines coupled to a station piece of its kind inside the
    /// business's building (the net's share and the machine's condition included); null when the business has no premises or no machine drives that station.
    /// </summary>
    public int? StationLabourPm(Business b, int station)
    {
        var room = PremisesOf(b); var pw = Power;
        if (room == null || pw == null || station < 0 || station >= b.Stations.Count) return null;
        string kind = b.Stations[station].Kind;
        int? best = null;
        foreach (var net in pw.Nets)
            foreach (var m in net.Machines)
            {
                if (m.Station is not { } st || !m.Working || m.Def.LabourPm <= 0 || !room.Contains(m.Cell.X, m.Cell.Y) || room.Z != m.Cell.Z) continue;
                if (_stationKind.GetValueOrDefault(st.Piece, st.Piece) != kind) continue;
                int l = net.LabourPm(m, pw.Rules);
                best = best == null ? l : Math.Min(best.Value, l);
            }
        return best;
    }

    /// <summary>The armourers of an armoury: the staff of the businesses linked to it with smithing at the armourer's skill.</summary>
    public int ArmourersOf(long key)
    {
        if (Prod == null) return 0;
        int n = 0;
        foreach (var b in Prod.Businesses)
        {
            if (!Book.Premises.TryGetValue(b.Id.Value, out var cell) || RoomAtKey(cell) is not { } r || LargeRooms.KeyOf(r) != key) continue;
            foreach (int p in b.Staff) if (Prod.Pop.People.SkillLevelOf(p, Skill.Smithing) >= Rules.ArmourerSkill) n++;
        }
        return n;
    }

    // ------------------------------------------------------------------ stores

    public Store ScrapChestOf(long key) => _econ.OpenStore(StoreKind.Chest, Armouries.HolderOf(key), 0, 0, false);

    /// <summary>A depot's store: a granary of the room's capacity (750 kg a m2 of floor), opened once.</summary>
    public Store? DepotStore(long key)
    {
        var room = RoomAtKey(key); if (room == null) return null;
        return _econ.OpenStore(StoreKind.Granary, Depots.OwnerOf(key), LargeRooms.DepotKg(room, Rules) * 1000, 0, true);
    }

    /// <summary>A warehouse's store: stowage of the room's litres, spoiling by the warehouse rule; opened once.</summary>
    public Store? WarehouseStore(long key)
    {
        var room = RoomAtKey(key); if (room == null) return null;
        return _econ.OpenStore(StoreKind.Warehouse, Warehouses.OwnerOf(key), 0, LargeRooms.WarehouseLitres(room) * 1000, true);
    }

    // ------------------------------------------------------------------ the day

    /// <summary>The last game day <see cref="Day"/> ran for.</summary>
    public long LastDay { get; private set; } = long.MinValue;

    /// <summary>One game day (hour 0 of the abstract tick): every armoury's care, the barracks' straw on every 15th day, and on a season's first day the rents and the depots. Idempotent for a day already done.</summary>
    public void Day(long day)
    {
        if (day <= LastDay) return;
        LastDay = day;
        var rooms = Rooms; if (rooms == null) return;
        bool winter = new GameTime(day * 86400).Season == Season.Winter;
        foreach (var (key, room) in RoomsOf(LargeRooms.Armoury))
        {
            if (!Book.ArmouryStock.ContainsKey(key)) continue;
            bool hearth = rooms.FactsAt(room.Origin.X, room.Origin.Y, room.Z)?.HearthRooms > 0;
            Armouries.Day(key, (int)day, ArmourersOf(key), winter, hearth, ScrapChestOf(key));
        }
        foreach (var (key, room) in RoomsOf(LargeRooms.Barracks))
        {
            if (!Book.BarracksOwner.TryGetValue(key, out var owner) || !_econ.OtherStores.TryGetValue(new EntityId(owner), out var store)) continue;
            bool hearth = rooms.FactsAt(room.Origin.X, room.Origin.Y, room.Z)?.HearthRooms > 0;
            if (!Barracks.Valid(room, hearth)) continue;
            Barracks.Upkeep(room, Rules, (int)day, store, _econ.Goods.IdOf("straw"));
        }
        if (day % TimeConst.DaysPerSeason == 0) SeasonStep((int)(day / TimeConst.DaysPerSeason), day);
    }

    /// <summary>A season's first day: the warehouses' rents, then each depot's rotation and target.</summary>
    public void SeasonStep(int season, long day)
    {
        Warehouses.Season(season, _econ.Ledger);
        foreach (var (key, room) in RoomsOf(LargeRooms.Depot))
        {
            if (!Book.Depots.ContainsKey(key)) continue;
            var store = DepotStore(key); if (store == null) continue;
            var (_, reached) = Depots.Season(key, store, season, SellGrain == null ? null : (good, grams) => SellGrain(key, good, grams));
            if (reached)
            {
                long tonnes = Depots.GrainG(key, store) / 1_000_000;
                Chronicle?.Invoke(new GameTime(day * 86400 + 9 * 3600), "depot_filled", "ch_works_depot_filled",
                    [ChronParam.Count("x", room.Origin.X), ChronParam.Count("y", room.Origin.Y), ChronParam.Count("tonnes", (int)tonnes)], [],
                    [new Cause(CauseKind.Supply, null, "", "ch_note_depot_target")], [("x", room.Origin.X.ToString()), ("y", room.Origin.Y.ToString()), ("tonnes", tonnes.ToString())]);
            }
        }
    }

    public ulong Hash() => Book.Hash();
}

/// <summary>The works' place in the abstract tick: hour 0 of every game day, after the power rules (H5, <c>DailyGlobalsProduction + 20</c>). A no-op in a world without a <see cref="WorksWorld"/>.</summary>
[AbstractSystem]
public sealed class WorksSystem : AbstractSystem
{
    public WorksSystem() : base("works", TickPhase.H5DailyGlobals, SystemOrder.DailyGlobalsProduction + 21) { }

    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0) return;
        var ww = ctx.World.Get<WorksWorld>(); if (ww == null) return;
        var prev = ww.Chronicle;
        ww.Chronicle = (when, type, key, ps, subjects, causes, data) => ctx.Outbox.Chronicle(when, type, key, ps, subjects, causes, data);
        try { ww.Day(ctx.Day); }
        finally { ww.Chronicle = prev; }
    }
}

/// <summary>Installs the large buildings on the home region (a hook in <see cref="WorldRuntime"/>, after the power rules and the businesses): the works' world in the sim's services, the production hooks (the hall's span, the machines' labour).</summary>
public static class WorksWiring
{
    public static WorksWorld? Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        var cw = home.Sim.Get<ConstructionWorld>(); var econ = state.Economy;
        if (cw == null || econ == null) return null;
        var book = state.Works ??= new WorksBook();
        var ww = new WorksWorld(book, content, econ, () => cw.Rooms) { Power = home.Sim.Get<PowerWorld>(), Prod = home.Economy?.Production };
        if (ww.Prod != null)
        {
            ww.Prod.SpanBonus = ww.SpanBonus;
            var prior = ww.Prod.StationLabourPm;
            ww.Prod.StationLabourPm = (b, s) => ww.StationLabourPm(b, s) ?? prior?.Invoke(b, s);
        }
        home.Sim.Set(ww);
        return ww;
    }
}
