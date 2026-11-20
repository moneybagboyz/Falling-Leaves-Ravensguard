using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Carts;

namespace Ravensguard.Sim.Animals;

/// <summary>A horse the hero has on loan (onboarding: a farmer lends a horse for the first errands, open question 7: three game days).</summary>
public sealed record HorseLoan(EntityId Animal, EntityId Lender, long DueSec);

/// <summary>What the hero's next game second of riding or driving will be.</summary>
/// <remarks>P5-G: <c>RawQ16</c>, when above 0, is the exact step in Q16.16 metres (a boat carries its remainder so a live leg takes exactly the abstract leg's seconds).</remarks>
public readonly record struct RideStep(int SpeedMmPerGs, Gait Gait, bool Driving, bool Stuck, bool Blocked, int RawQ16 = 0);

/// <summary>What the view shows of the hero's mount: the bars and flags of spec 6.</summary>
public readonly record struct RideView(bool Mounted, bool Driving, bool Mounting, Gait Gait, int StaminaPm, int FatiguePm, int FeedCenti, int ShoePm, int Lameness, int SpeedCmps, bool Stuck, int CargoKg, int CargoCapKg, int AxlePm, int RatioPm, int BreedIndex, int TemplateIndex,
    long PullN = 0, long NeedN = 0, int TeamCount = 0, int MaxAnimals = 0, int AnimalsNeeded = 0, int BeastBreed = -1);

/// <summary>
/// Everything about animals and vehicles the running game keeps: the breeds and templates, the animals that are individuals, the vehicles with their cargo, the herd head counts of species farming does
/// not keep, the horses on loan and the hero's own ride (mounted, driving, the half-finished mount). The hero's movement asks it for the next game second's speed and gives back how far it got, so
/// stamina, shoes, wheels and the stuck rolls all follow distance and game time, never the tick (horses-and-carts spec 4, 5).
/// </summary>
public sealed class RideBook
{
    public AnimalCatalog Breeds { get; }
    public GoodCatalog Goods { get; }
    public VehicleCatalog Templates { get; }
    public AnimalTable Animals { get; }
    public VehicleTable Vehicles { get; }
    public HerdLedger Herds { get; } = new();
    public ulong Seed { get; }
    public List<HorseLoan> Loans { get; } = [];
    /// <summary>P5-G: vehicle sites, commissions and voyages (the abstract side of vehicles, stepped hourly by <see cref="VehicleSystem"/>).</summary>
    public VehicleWorks Works { get; } = new();
    /// <summary>
    /// P5-G: the lock of the book. The tick worker's hourly vehicle pass and the hero desk's vehicle commands take it, and so does the live tier around its per-second ride step and the view around
    /// its reading of the rows, so a site finished or a barge moved in the hour never meets a frame half way.
    /// </summary>
    public object Gate { get; } = new();
    /// <summary>P5-G: the ground water under the hero's boat (depth, current along the river, river-high days, wind across), set by the game from the waterway; null on land.</summary>
    public Func<BoatWater?> WaterNow { get; set; } = () => null;
    /// <summary>Goods the hero has dropped from a stuck cart: a heap on the ground, counted by the mass ledger like any store, loaded back when the cart is free.</summary>
    public Store Heap => _heap ??= _openStore(HeapId, 0, 0);
    private Store? _heap;
    private readonly Func<EntityId, long, long, Store> _openStore;

    // ---- the hero's ride (saved)
    public EntityId Mounted, Driving, MountTarget;
    public long MountLeftGs, DismountLeftGs;
    public Gait Want = Gait.Trot;
    public long HeroXMm, HeroYMm;

    // ---- the rider (set by the game from the hero record)
    public int RiderKg = 85, RidingSkill, HusbandrySkill, Strength = 50;
    /// <summary>Riding uses earned since the last drain (one a game minute at trot, as many again at gallop, spec 4.1); the game turns them into skill.</summary>
    public int RidingUses;
    private long _useSec;

    // ---- the ground under the hero (set by the game from the tile and the weather; tests and the debug console force them)
    public Func<Surface> SurfaceNow { get; set; } = () => Surface.DryRoad;
    public Func<int> SlopeNow { get; set; } = () => 0;
    public Surface? SurfaceOverride;

    /// <summary>True when the stores live in the economy (they are counted by its mass ledger and saved in its section); false for a book on a private goods catalog and ledger.</summary>
    public bool Hosted { get; }
    public static readonly EntityId HeapId = EntityId.MakeIn(IdKind.Other, 0, 0x7F0_0000);

    /// <summary>The book of a running game: the vehicles' beds and the heap are stores of the economy, so every kilogram of cargo is in the mass ledger and in the economy's save.</summary>
    public RideBook(ContentDatabase db, EconomyState econ, ulong seed)
        : this(db, econ.Goods, (id, m, v) => econ.OpenStore(StoreKind.Open, id, m, v, enforce: true), seed, hosted: true) { }

    /// <summary>A book on its own goods catalog and ledger (tests, tools): the stores are private.</summary>
    public RideBook(ContentDatabase db, GoodCatalog goods, MassLedger ledger, ulong seed)
        : this(db, goods, PrivateStores(goods, ledger), seed, hosted: false) { }

    private static Func<EntityId, long, long, Store> PrivateStores(GoodCatalog goods, MassLedger ledger)
    {
        var d = new Dictionary<EntityId, Store>();
        return (id, m, v) => d.TryGetValue(id, out var s) ? s : d[id] = new Store(goods, ledger, StoreKind.Open, id, m, v, enforce: m > 0 || v > 0);
    }

    private RideBook(ContentDatabase db, GoodCatalog goods, Func<EntityId, long, long, Store> open, ulong seed, bool hosted)
    {
        Breeds = AnimalCatalog.From(db); Templates = VehicleCatalog.From(db); Seed = seed; Hosted = hosted; Goods = goods;
        Animals = new AnimalTable(Breeds); Vehicles = new VehicleTable(Templates, Animals, goods, open);
        _openStore = open;
    }

    /// <summary>
    /// P5-G: registers the player's vehicle designs of a save's designs (vehicle-designer 4.1) so their vehicles load and can be built. A design that no longer validates is skipped with its reason
    /// (returned), never thrown: its vehicles then fail to load with a clear message.
    /// </summary>
    public List<string> AttachDesigns(Construction.DesignBook designs)
    {
        var problems = new List<string>();
        foreach (var (id, (kind, text)) in designs.Designs)
        {
            if (kind != Construction.DesignKind.Vehicle) continue;
            var def = VehicleCatalog.ParseDesign(text, id, out var why);
            if (def == null) { problems.Add($"{id}: {why}"); continue; }
            Templates.Register(def, problems);
        }
        return problems;
    }

    public bool IsMounted => Mounted != EntityId.None;
    public bool IsDriving => Driving != EntityId.None;
    public bool Busy => MountLeftGs > 0 || DismountLeftGs > 0;
    /// <summary>True when nothing would be saved: no rows and every counter at its start (review finding 5: an emptied book with advanced serials must still be saved, or ids are reused).</summary>
    public bool IsEmpty => Animals.Count == 0 && Vehicles.Rows.Count == 0 && Herds.IsEmpty && Loans.Count == 0 && (_heap == null || _heap.RowCount == 0) && Works.IsEmpty
        && Animals.NextSerial == 1 && Vehicles.NextSerial == 1 && RidingUses == 0 && Mounted == EntityId.None && Driving == EntityId.None;
    public Animal? MountedAnimal => IsMounted ? Animals.Find(Mounted) : null;
    public VehicleRow? DrivenVehicle => IsDriving ? Vehicles.Find(Driving) : null;
    public Surface Surface => SurfaceOverride ?? SurfaceNow();

    // ------------------------------------------------------------------ the hero's commands

    /// <summary>Puts the hero's position (world mm) where the sim has them: a mount and a cart move with them.</summary>
    public void PlaceHero(long xMm, long yMm)
    {
        HeroXMm = xMm; HeroYMm = yMm;
        if (MountedAnimal is { } a) { a.XMm = xMm; a.YMm = yMm; }
        if (DrivenVehicle is { } v)
        {
            v.XMm = xMm; v.YMm = yMm;
            foreach (var id in v.Team) if (Animals.Find(id) is { } t) { t.XMm = xMm; t.YMm = yMm; }
        }
    }

    /// <summary>The nearest standing animal within reach, or null (ties by id).</summary>
    public Animal? NearestAnimal(long reachMm = MountRules.ReachMm)
    {
        Animal? best = null; long bd = long.MaxValue;
        foreach (var a in Animals.Rows)
        {
            if (!a.Alive || a.State is AnimalState.Ridden or AnimalState.Hitched) continue;
            long dx = a.XMm - HeroXMm, dy = a.YMm - HeroYMm, d = dx * dx + dy * dy;
            if (d <= reachMm * reachMm && d < bd) { bd = d; best = a; }
        }
        return best;
    }

    public VehicleRow? NearestVehicle(long reachMm = 3000)
    {
        VehicleRow? best = null; long bd = long.MaxValue;
        foreach (var v in Vehicles.Rows)
        {
            long dx = v.XMm - HeroXMm, dy = v.YMm - HeroYMm, d = dx * dx + dy * dy;
            if (d <= reachMm * reachMm && d < bd) { bd = d; best = v; }
        }
        return best;
    }

    /// <summary>
    /// E: dismounts when mounted; steps down from a cart when driving; otherwise tries the nearest animal within 2 m (a mount takes 40 game seconds and breaks off if the hero moves). Returns what the
    /// attempt came to, for the view's message.
    /// </summary>
    public MountResult Interact(long nowSec)
    {
        if (Busy) { CancelBusy(); return MountResult.Ok; }
        if (IsMounted) { DismountLeftGs = MountRules.DismountSeconds; return MountResult.Ok; }
        if (IsDriving) { StopDriving(); return MountResult.Ok; }
        var a = NearestAnimal(); if (a == null) return MountResult.TooFar;
        var b = Breeds.At(a.Breed);
        var r = MountRules.Check(a, b, HeroXMm, HeroYMm, RidingSkill, RiderKg);
        if (r == MountResult.Untrained)
        {
            // an unbroken horse: one try per ten game minutes; a failed try throws the rider (stun 3 s: the view's business)
            if (!RideMath.TryTame(Seed, a, nowSec, 0, RidingSkill, HusbandrySkill)) return MountResult.Untrained;
            a.TrainedPm = 700; r = MountRules.Check(a, b, HeroXMm, HeroYMm, RidingSkill, RiderKg);
        }
        if (r != MountResult.Ok) return r;
        MountTarget = a.Id; MountLeftGs = MountRules.MountSeconds;
        return MountResult.Ok;
    }

    private void CancelBusy() { MountLeftGs = 0; DismountLeftGs = 0; MountTarget = EntityId.None; }

    /// <summary>Breaks off a mount in progress (the hero moved).</summary>
    public void BreakOffMount() { if (MountLeftGs > 0) CancelBusy(); }

    /// <summary>One game second of mounting or dismounting; true when it finished this second.</summary>
    public bool TickBusy()
    {
        if (MountLeftGs > 0)
        {
            if (--MountLeftGs == 0)
            {
                var a = Animals.Find(MountTarget);
                if (a != null && a.Alive) { Mounted = a.Id; a.State = AnimalState.Ridden; a.XMm = HeroXMm; a.YMm = HeroYMm; }
                MountTarget = EntityId.None; return true;
            }
        }
        else if (DismountLeftGs > 0 && --DismountLeftGs == 0) { Dismount(); return true; }
        return false;
    }

    /// <summary>Gets off at once: the animal stands where the hero is, and follows if it is trained to 700 (the squad order Follow), else stands.</summary>
    public void Dismount()
    {
        if (MountedAnimal is { } a) { a.State = a.TrainedPm >= 700 ? AnimalState.Following : AnimalState.Free; a.XMm = HeroXMm; a.YMm = HeroYMm; }
        Mounted = EntityId.None; DismountLeftGs = 0;
    }

    /// <summary>Climbs onto the nearest cart's bench: the hero drives it (a vehicle with a team that can pull).</summary>
    public bool StartDriving()
    {
        if (IsMounted || IsDriving) return false;
        var v = NearestVehicle(); if (v == null || v.Broken) return false;
        Driving = v.Id; v.Driven = true;
        if (v.IsBoat) { if (v.Water is WaterState.Slipway or WaterState.Sunk) { Driving = EntityId.None; v.Driven = false; return false; } HeroXMm = v.XMm; HeroYMm = v.YMm; return true; }   // P5-G: the hero steps into the boat
        v.XMm = HeroXMm; v.YMm = HeroYMm; return true;
    }

    public void StopDriving()
    {
        if (DrivenVehicle is { } v) { v.Driven = false; foreach (var id in v.Team) if (Animals.Find(id) is { } a) { a.XMm = v.XMm; a.YMm = v.YMm; } }
        Driving = EntityId.None;
    }

    /// <summary>Hitches the nearest standing animal to the nearest vehicle (20 game seconds is the view's pace).</summary>
    public HitchResult HitchNearest()
    {
        var v = NearestVehicle(); var a = NearestAnimal(4000);
        if (v == null || a == null) return HitchResult.NoHitch;
        var r = Vehicles.Hitch(v, a);
        if (r == HitchResult.Ok) { a.XMm = v.XMm; a.YMm = v.YMm; }
        return r;
    }

    public void UnhitchAll()
    {
        if (NearestVehicle() is not { } v) return;
        foreach (var id in v.Team.ToArray()) if (Animals.Find(id) is { } a) Vehicles.Unhitch(v, a);
    }

    // ------------------------------------------------------------------ push, unload, dig (a stuck cart)

    /// <summary>
    /// The hero and helpers heave at a stuck cart: the team and the pushers' strength together against the force it takes (spec 4.6). True when it comes free. <paramref name="pushers"/> counts the hero.
    /// </summary>
    public bool Push(int pushers, long nowSec)
    {
        var v = DrivenVehicle ?? NearestVehicle();
        if (v is not { Stuck: true } || v.Broken) return false;                 // P5-G: a broken wheel or axle is not freed by pushing (VD-7)
        var st = Vehicles.StatsOf(v); var tu = Templates.Tuning;
        var (pull, _) = Vehicles.Team(v);
        int people = IsDriving ? RiderKg : 0;
        bool free = Stuck.Freed(tu, st.MassKg + Vehicles.CargoKg(v) + people, tu.Crr(Surface, st.Runners), SlopeNow(), pull, (long)pushers * Stuck.PushN(tu, Strength), nowSec < v.DugUntilSec);
        if (free) { v.Stuck = false; v.StuckSinceSec = 0; }
        return free;
    }

    public void Dig(long nowSec) { if ((DrivenVehicle ?? NearestVehicle()) is { } v) v.DugUntilSec = nowSec + Stuck.DigMinutes * 60; }

    /// <summary>Drops cargo on the heap until about <paramref name="kg"/> kilograms are off the bed (5 kg per game second per person is the view's pace); returns kilograms moved.</summary>
    public int UnloadToHeap(int kg) => Unload(Heap, kg);

    /// <summary>Loads the heap back into the bed as far as it fits; returns kilograms moved.</summary>
    public int ReloadFromHeap()
    {
        var v = DrivenVehicle ?? NearestVehicle(); if (v == null) return 0;
        long moved = 0;
        foreach (var (good, qty) in Heap.Rows()) moved += Store.MoveUpTo(Heap, v.Cargo, good, qty) * Vehicles.GoodMassG(good);
        return (int)(moved / 1000);
    }

    /// <summary>Drops cargo on the ground store (the caller supplies it) until <paramref name="kg"/> kilograms are off the bed; returns kilograms moved.</summary>
    public int Unload(Store ground, int kg) => (DrivenVehicle ?? NearestVehicle()) is { } v ? (int)(Vehicles.UnloadKg(v, ground, kg) / 1000) : 0;

    // ------------------------------------------------------------------ the next game second

    /// <summary>
    /// What the hero's next game second will be: a horse's gait and speed (with stamina, load, tiredness and the surface), or a cart's speed on this surface and slope. <paramref name="gallop"/> is Shift,
    /// <paramref name="walk"/> the O toggle. Not mounted and not driving: no step (speed 0, Blocked false).
    /// </summary>
    public RideStep Plan(bool gallop, bool walk, long nowSec)
    {
        if (DrivenVehicle is { } v) return v.IsBoat ? PlanBoat(v, gallop, walk, nowSec) : PlanCart(v, walk, nowSec);
        if (MountedAnimal is not { } a) return default;
        var b = Breeds.At(a.Breed);
        int ratio = RideMath.LoadRatioPm(RiderKg, b.CarryKg);
        Gait want = walk ? Gait.Walk : gallop ? Gait.Gallop : Gait.Trot; Want = want;
        var g = RideMath.Allowed(a, b, want, ratio);
        int cmps = RideMath.SpeedCmps(a, b, g, ratio, !a.Saddled, SurfaceModel.PersonSpeedPm(Surface));
        return new RideStep(VehicleMath.MmPerGs(cmps), g, false, false, cmps <= 0);
    }

    // ------------------------------------------------------------------ P5-G: boats (vehicle-designer 4.5, 6)

    /// <summary>The live boat's last numbers, for the boat HUD: water speed, current, ground speed (mm/s), draft, freeboard, GM, the depth under the keel.</summary>
    public BoatView Boat { get; private set; }
    private long _boatAccQ16;

    /// <summary>
    /// A boat's next game second (W/S row harder or back water, Shift a hard stroke, O drifts with the oars shipped): its power against the water's drag at its draft, plus the current along the way it
    /// heads. Aground, sunk or on the slipway it does not move. The speed is in mm per game second with the remainder carried, so a live leg takes exactly the abstract leg's seconds (VD-19).
    /// </summary>
    private RideStep PlanBoat(VehicleRow v, bool hard, bool drift, long nowSec)
    {
        var water = WaterNow();
        if (v.Water is WaterState.Slipway or WaterState.Sunk || water == null) return new RideStep(0, Gait.Walk, true, false, true);
        var st = Vehicles.StatsOf(v); var wt = Templates.Water;
        var load = BoatMath.Load(st, Vehicles.CargoKg(v), RiderKg + st.Rowers * BoatMath.PersonKg, wt);
        bool floats = BoatMath.Floats(load.DraftMm, water.Value.DepthMm, wt);
        if (!floats) { v.Water = WaterState.Aground; Boat = BoatView.Of(load, 0, water.Value, 0, true); return new RideStep(0, Gait.Walk, true, true, true); }
        if (v.Water == WaterState.Aground) v.Water = WaterState.Afloat;
        long tow = 0; if (st.TowPost && v.Team.Count > 0) tow = Vehicles.Team(v).PullN;
        long power = drift ? 0 : BoatMath.PowerN(wt, st.Rowers, st.Polers, water.Value.DepthMm, tow, BoatMath.SailN(st.SailM2, water.Value.WindMps, water.Value.SailCosPm), hard);
        int wv = BoatMath.WaterSpeedMmps(power, wt.DragK, BoatMath.WetMm2(st.WaterplaneMm2, st.PerimeterM, load.DraftMm));
        int ground = Math.Max(0, wv + water.Value.CurrentAlongMmps);
        Boat = BoatView.Of(load, wv, water.Value, ground, false);
        _boatAccQ16 += (long)ground * 65536;                                            // Q16.16 metres x 20,000 (a real second is 20 game seconds, mm to m)
        int raw = (int)(_boatAccQ16 / 20_000); _boatAccQ16 -= raw * 20_000L; int mm = (int)((long)raw * 1000 >> 16);
        return new RideStep(mm, hard ? Gait.Gallop : drift ? Gait.Walk : Gait.Trot, true, false, ground <= 0, raw);
    }

    /// <summary>
    /// A boat's game second closes: every 20 game seconds the capsize check of a tender boat in beam seas (0x550) and the water a swamped or wave-swept boat takes in (0x551); a boat full of water, or with
    /// its freeboard under 0, sinks: its cargo goes to the river bed (<see cref="RiverBed"/>, mass exact), the people to the bank (Phase 4 drowning is not wired: no deaths).
    /// </summary>
    private void AccountBoat(VehicleRow v, long nowSec)
    {
        if (v.Water is not (WaterState.Afloat or WaterState.Aground) || nowSec % 20 != 0) return;
        var water = WaterNow(); if (water == null) return;
        var st = Vehicles.StatsOf(v); var wt = Templates.Water;
        var load = BoatMath.Load(st, Vehicles.CargoKg(v), RiderKg + st.Rowers * BoatMath.PersonKg, wt);
        int broken = 0; foreach (var p in v.Parts) if (p.State == PartState.Broken && Templates.PartList[p.Part].Tags.Contains("hull")) broken++;
        v.WaterInL += BoatMath.WaterInL(Seed, v.Id, nowSec, load.FreeboardMm, water.Value.WindMps, water.Value.GreatRiver, broken, 1, wt);
        bool capsize = BoatMath.CapsizeCheck(Seed, v.Id, nowSec, load.GmMm, water.Value.CrossWindMps, wt);
        if (load.Swamped || capsize || v.WaterInL > st.HullCells * 400) Sink(v);
    }

    /// <summary>Where a sunk boat's cargo lies: a store on the river bed (counted by the mass ledger like any store).</summary>
    public Store RiverBed => _bed ??= _openStore(RiverBedId, 0, 0);
    private Store? _bed;
    public static readonly EntityId RiverBedId = EntityId.MakeIn(IdKind.Other, 0, 0x7F0_0001);

    /// <summary>The boat sinks (vehicle-designer 4.5, VD-14): all its cargo moves to the river bed store, so mass is exact; the hero is put on the bank by the view.</summary>
    public void Sink(VehicleRow v)
    {
        foreach (var (good, qty) in v.Cargo.Rows()) Store.Move(v.Cargo, RiverBed, good, qty);
        v.Water = WaterState.Sunk;
        if (Driving == v.Id) StopDriving();
    }

    private RideStep PlanCart(VehicleRow v, bool walk, long nowSec)
    {
        if (v.Broken) return new RideStep(0, Gait.Walk, true, false, true);
        var r = Vehicles.Speed(v, Surface, SlopeNow(), RiderKg, nowSec, 0, walk);
        int mm = v.Stuck ? 0 : VehicleMath.MmPerGs(r.SpeedCmps);
        return new RideStep(mm, walk ? Gait.Walk : Gait.Trot, true, v.Stuck, r.CannotMove || v.Stuck);
    }

    /// <summary>
    /// Closes a game second: the hero moved <paramref name="movedMm"/>. A horse pays stamina and wears; a cart wears, rolls its stuck checks for each 20 m crossed, and drags its team along. Riding
    /// earns a use a game minute.
    /// </summary>
    public void Account(in RideStep step, long movedMm, long nowSec, bool forced)
    {
        var s = Surface;
        if (MountedAnimal is { } a)
        {
            var b = Breeds.At(a.Breed);
            RideMath.StepSecond(a, b, step.Gait, movedMm, RideMath.LoadRatioPm(RiderKg, b.CarryKg), nowSec, Seed, s, forced);
            if (movedMm > 0 && step.Gait != Gait.Walk && nowSec / 60 != _useSec) { _useSec = nowSec / 60; RidingUses += step.Gait == Gait.Gallop ? 80 : 60; }
        }
        else if (DrivenVehicle is { IsBoat: true } boat) { boat.OdometerMm += movedMm; AccountBoat(boat, nowSec); }
        else if (DrivenVehicle is { } v)
        {
            long before = v.OdometerMm;
            if (movedMm > 0)
            {
                Vehicles.Wear(v, movedMm, VehicleTable.RoughPm(s));
                foreach (var id in v.Team) if (Animals.Find(id) is { } t) AnimalCare.OnMove(t, Breeds.At(t.Breed), movedMm, s, false, Seed);
                var res = Vehicles.Speed(v, s, SlopeNow(), RiderKg, nowSec);
                for (long k = before / 20_000 + 1; k <= v.OdometerMm / 20_000 && !v.Stuck; k++)
                    if (Stuck.Check(Seed, v.Id, nowSec / 3600, k, res.RatioPm, s, Templates.Tuning, Vehicles.StatsOf(v).Runners)) { v.Stuck = true; v.StuckSinceSec = nowSec; }
            }
            else
            {
                // standing still: a stuck check cannot happen, but a heavy load on deep mud that cannot move at all is stuck outright
                var res = Vehicles.Speed(v, s, SlopeNow(), RiderKg, nowSec);
                if (!v.Stuck && res.RatioPm > 1300 && Stuck.CanBog(Templates.Tuning, s, Vehicles.StatsOf(v).Runners)) { v.Stuck = true; v.StuckSinceSec = nowSec; }
            }
        }
    }

    /// <summary>The state of a game second the hero stood still: a mount regains stamina, a cart settles.</summary>
    public void AccountRest(long nowSec) => Account(new RideStep(0, Gait.Walk, IsDriving, false, false), 0, nowSec, false);

    // ------------------------------------------------------------------ the abstract days, loans

    /// <summary>One game hour's thirst for every animal, and once a day its night's rest; the hero's mount counts as working. Call with the game hour.</summary>
    public void Hour(long gameHour)
    {
        foreach (var a in Animals.Rows) AnimalCare.Hour(a);
        if (gameHour % 24 == 0) foreach (var a in Animals.Rows) AnimalCare.Day(a, gameHour / 24, a.State == AnimalState.Stabled);
    }

    /// <summary>Lends the hero a horse (onboarding hook, open question 7): the animal belongs to the lender and is due back in three game days.</summary>
    public Animal Loan(Breed b, EntityId lender, long nowSec, long xMm, long yMm, int days = 3)
    {
        var a = Animals.Create(b, lender, trainedPm: 900, temper: -20, xMm: xMm, yMm: yMm);
        Loans.Add(new HorseLoan(a.Id, lender, nowSec + days * 86_400L));
        return a;
    }

    /// <summary>Takes back loans that are due: the hero is helped down, the horse goes home (its row is removed; the lender owns it again in the abstract herds).</summary>
    public int ExpireLoans(long nowSec)
    {
        int n = 0;
        for (int i = Loans.Count - 1; i >= 0; i--)
        {
            if (Loans[i].DueSec > nowSec) continue;
            var a = Animals.Find(Loans[i].Animal);
            if (a != null) { if (Mounted == a.Id) Dismount(); Animals.Remove(a); }
            Loans.RemoveAt(i); n++;
        }
        return n;
    }

    /// <summary>The view of the hero's ride for the bars.</summary>
    public RideView View(long nowSec)
    {
        var a = MountedAnimal; var v = DrivenVehicle;
        if (a != null)
        {
            var b = Breeds.At(a.Breed);
            var st = Plan(false, false, nowSec);
            return new RideView(true, false, false, st.Gait, a.StaminaSp / 10, a.WorkFatiguePm, a.FeedCenti, a.LowestShoe / 10, a.Lameness, RideMath.SpeedCmps(a, b, st.Gait, RideMath.LoadRatioPm(RiderKg, b.CarryKg), !a.Saddled), false, 0, 0, 0, 0, a.Breed, -1);
        }
        if (v != null)
        {
            var st = Vehicles.StatsOf(v); var r = Vehicles.Speed(v, Surface, SlopeNow(), RiderKg, nowSec);
            return new RideView(false, true, false, Gait.Walk, 0, 0, 0, 0, 0, v.Stuck ? 0 : r.SpeedCmps, v.Stuck, Vehicles.CargoKg(v), st.CargoKg, r.AxlePm, r.RatioPm, -1, v.Template,
                r.FPullN, r.FReqMilliN / 1000, v.Team.Count, st.MaxAnimals, Vehicles.AnimalsNeeded(v, r.FReqMilliN), Vehicles.ReferenceBeast(v).Breed);
        }
        return new RideView(false, false, MountLeftGs > 0, Gait.Walk, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, -1, -1);
    }
}

/// <summary>P5-G: the water under the hero's boat as the game reads it from the waterway: depth, the current along the way the boat heads (signed), the wind, across the river, the sail's angle, river-high days.</summary>
public readonly record struct BoatWater(int DepthMm, int CurrentAlongMmps, int WindMps, int CrossWindMps, int SailCosPm, bool GreatRiver, int HighDays);

/// <summary>P5-G: the boat HUD's numbers (vehicle-designer 6).</summary>
public readonly record struct BoatView(int WaterMmps, int CurrentMmps, int GroundMmps, int DraftMm, int FreeboardMm, int GmMm, int UnderKeelMm, bool Aground)
{
    public static BoatView Of(BoatLoad l, int water, BoatWater w, int ground, bool aground) => new(water, w.CurrentAlongMmps, ground, l.DraftMm, l.FreeboardMm, l.GmMm, w.DepthMm - l.DraftMm, aground);
}
