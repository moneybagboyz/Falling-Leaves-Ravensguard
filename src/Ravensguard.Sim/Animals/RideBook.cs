using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Carts;

namespace Ravensguard.Sim.Animals;

/// <summary>A horse the hero has on loan (onboarding: a farmer lends a horse for the first errands, open question 7: three game days).</summary>
public sealed record HorseLoan(EntityId Animal, EntityId Lender, long DueSec);

/// <summary>What the hero's next game second of riding or driving will be.</summary>
public readonly record struct RideStep(int SpeedMmPerGs, Gait Gait, bool Driving, bool Stuck, bool Blocked);

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

    public bool IsMounted => Mounted != EntityId.None;
    public bool IsDriving => Driving != EntityId.None;
    public bool Busy => MountLeftGs > 0 || DismountLeftGs > 0;
    public bool IsEmpty => Animals.Count == 0 && Vehicles.Rows.Count == 0 && Herds.IsEmpty && Loans.Count == 0 && (_heap == null || _heap.RowCount == 0);
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
        Driving = v.Id; v.Driven = true; v.XMm = HeroXMm; v.YMm = HeroYMm; return true;
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
        if (v is not { Stuck: true }) return false;
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
        if (DrivenVehicle is { } v) return PlanCart(v, walk, nowSec);
        if (MountedAnimal is not { } a) return default;
        var b = Breeds.At(a.Breed);
        int ratio = RideMath.LoadRatioPm(RiderKg, b.CarryKg);
        Gait want = walk ? Gait.Walk : gallop ? Gait.Gallop : Gait.Trot; Want = want;
        var g = RideMath.Allowed(a, b, want, ratio);
        int cmps = RideMath.SpeedCmps(a, b, g, ratio, !a.Saddled, SurfaceModel.PersonSpeedPm(Surface));
        return new RideStep(VehicleMath.MmPerGs(cmps), g, false, false, cmps <= 0);
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
        else if (DrivenVehicle is { } v)
        {
            long before = v.OdometerMm;
            if (movedMm > 0)
            {
                Vehicles.Wear(v, movedMm);
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
