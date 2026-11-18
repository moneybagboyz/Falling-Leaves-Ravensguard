using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Carts;

/// <summary>One vehicle in the world (horses-and-carts spec 3.3): where it stands, what pulls it and what it carries (the cargo is a real store, so its goods are counted by the mass ledger).</summary>
public sealed class VehicleRow
{
    public EntityId Id;
    public int Template;
    public EntityId Owner;
    public long XMm, YMm;
    public byte Facing;
    public Store Cargo = null!;
    public List<EntityId> Team = [];
    public long OdometerMm;
    /// <summary>Wheel condition, permille (1000 new, 0 broken).</summary>
    public int WheelPm = 1000;
    public bool Stuck;
    public long StuckSinceSec, DugUntilSec;
    public long LastDraw = -1;
    public long WearAccMm;
    public bool Broken => WheelPm <= 0;
    public bool Driven;
}

public enum HitchResult : byte { Ok, NoHitch, Full, Untrained, Dead, Taken, AxleWouldBreak, WrongBeast }

/// <summary>Every vehicle row, in id order; ids from a private counter (Other kind, serials from 0x720_0000).</summary>
public sealed class VehicleTable
{
    public const ulong SerialBase = 0x720_0000;
    private readonly List<VehicleRow> _rows = [];
    public ulong NextSerial = 1;
    public IReadOnlyList<VehicleRow> Rows => _rows;
    public VehicleCatalog Catalog { get; }
    public AnimalTable Animals { get; }
    private readonly GoodCatalog _goods;
    private readonly Func<EntityId, long, long, Store> _open;

    /// <summary><paramref name="open"/> makes (or finds) the store of a vehicle's bed: owner, mass limit in grams, volume limit in millilitres. The economy's other stores in a running game, so the cargo is counted and saved with its goods.</summary>
    public VehicleTable(VehicleCatalog catalog, AnimalTable animals, GoodCatalog goods, Func<EntityId, long, long, Store> open) { Catalog = catalog; Animals = animals; _goods = goods; _open = open; }

    public static EntityId MakeId(ulong serial) => EntityId.MakeIn(IdKind.Other, 0, SerialBase + serial);
    public static bool IsVehicleId(EntityId id) => id.Kind == IdKind.Other && (id.Serial & ~0xFFFFFUL) == SerialBase;

    public VehicleRow? Find(EntityId id)
    {
        int lo = 0, hi = _rows.Count - 1;
        while (lo <= hi) { int m = (lo + hi) >> 1; int c = _rows[m].Id.CompareTo(id); if (c == 0) return _rows[m]; if (c < 0) lo = m + 1; else hi = m - 1; }
        return null;
    }

    public Store NewStore(EntityId id, VehicleStats s) => _open(id, s.CargoKg * 1000L, s.CargoL * 1000L);

    public VehicleRow Create(VehicleBlueprint bp, EntityId owner, long xMm = 0, long yMm = 0)
    {
        var id = MakeId(NextSerial++);
        var r = new VehicleRow { Id = id, Template = bp.Index, Owner = owner, XMm = xMm, YMm = yMm, Cargo = NewStore(id, bp.Stats) };
        _rows.Add(r);
        return r;
    }

    /// <summary>Puts a saved row back (restore): the caller fills the cargo.</summary>
    public VehicleRow Restore(VehicleRow r) { _rows.Add(r); return r; }
    public void ClearForRestore(ulong nextSerial) { _rows.Clear(); NextSerial = nextSerial; }
    public void Remove(VehicleRow r) { foreach (var id in r.Team) if (Animals.Find(id) is { } a) { a.Vehicle = EntityId.None; if (a.State == AnimalState.Hitched) a.State = AnimalState.Free; } _rows.Remove(r); }

    public VehicleStats StatsOf(VehicleRow r) => Catalog.At(r.Template).Stats;

    /// <summary>Kilograms in the bed (grams rounded up).</summary>
    public int CargoKg(VehicleRow r) => (int)((r.Cargo.TotalMassG() + 999) / 1000);

    /// <summary>Hitches an animal (spec 4.4): a draught breed trained to 400, a free slot, and a cart whose axle would not break under its load.</summary>
    public HitchResult Hitch(VehicleRow r, Animal a)
    {
        var st = StatsOf(r);
        if (st.HitchSlots <= 0) return HitchResult.NoHitch;
        if (!a.Alive) return HitchResult.Dead;
        if (a.State is AnimalState.Hitched or AnimalState.Ridden) return HitchResult.Taken;
        if (!Animals.Catalog.At(a.Breed).Def.Draught) return HitchResult.WrongBeast;
        if (a.TrainedPm < 400) return HitchResult.Untrained;
        if (r.Team.Count >= st.MaxAnimals) return HitchResult.Full;
        if (st.AxleLimitKg > 0 && (long)(st.MassKg + CargoKg(r)) * 1000 / st.AxleLimitKg > 1300) return HitchResult.AxleWouldBreak;
        r.Team.Add(a.Id); a.Vehicle = r.Id; a.State = AnimalState.Hitched;
        return HitchResult.Ok;
    }

    public void Unhitch(VehicleRow r, Animal a)
    {
        if (!r.Team.Remove(a.Id)) return;
        a.Vehicle = EntityId.None; if (a.State == AnimalState.Hitched) a.State = AnimalState.Free;
    }

    /// <summary>The team's pull (the animals' pull summed, each extra team a tenth less efficient, see <see cref="VehicleMath.TeamEfficiencyPm"/>) and the gait cap of its slowest animal.</summary>
    public (long PullN, int GaitCapCmps) Team(VehicleRow r)
    {
        long pull = 0; int cap = int.MaxValue; int slots = StatsOf(r).HitchSlots, i = 0;
        foreach (var id in r.Team)
        {
            var a = Animals.Find(id); if (a == null || !a.Alive) continue;
            var b = Animals.Catalog.At(a.Breed);
            pull += VehicleMath.EffectivePullN(b.PullN, VehicleMath.LamePm(a.Lameness), a.Weak ? 150 : 0) * VehicleMath.TeamEfficiencyPm(i++, slots) / 1000;
            cap = Math.Min(cap, b.WorkCmps);
        }
        return (pull, cap == int.MaxValue ? 0 : cap);
    }

    /// <summary>
    /// The vehicle's speed on a surface and grade with <paramref name="peopleKg"/> aboard. A vehicle that is stuck is judged with its resistance x1.5, and dug-out mud (a shovel, ten game minutes)
    /// takes a fifth off the surface's resistance.
    /// </summary>
    public SpeedResult Speed(VehicleRow r, Surface s, int slopePm, int peopleKg, long nowSec, int windPm = 0, bool walkOnly = false)
    {
        var st = StatsOf(r); var tu = Catalog.Tuning;
        var (pull, cap) = Team(r);
        if (walkOnly && r.Team.Count > 0) cap = Math.Min(cap, MinWalk(r));
        int crr = tu.Crr(s, st.Runners);
        if (nowSec < r.DugUntilSec) crr = crr * 800 / 1000;
        if (r.Stuck) crr = crr * tu.StuckCrrMultPm / 1000;
        int cargo = CargoKg(r);
        return VehicleMath.Evaluate(st.BaseSpeedCmps, st.MassKg + cargo, st.MassKg + cargo + peopleKg, crr + slopePm + windPm, pull, cap, st.AxleLimitKg, s == Surface.Ice);
    }

    /// <summary>
    /// The beast to count teams of: the first animal hitched, or for an empty hitch the one that usually pulls this kind (an ox at a pole and yoke, a draught horse otherwise). Its breed index and unlamed pull.
    /// </summary>
    public (int Breed, int PullN) ReferenceBeast(VehicleRow r)
    {
        foreach (var id in r.Team) if (Animals.Find(id) is { Alive: true } a) return (a.Breed, Animals.Catalog.At(a.Breed).PullN);
        var b = Animals.Catalog.Find(StatsOf(r).HitchKind == "pole_yoke" ? "ox" : "horse_draught") ?? Animals.Catalog.At(0);
        return (b.Index, b.PullN);
    }

    /// <summary>
    /// The animals this vehicle needs on its current ground to stay clear of bogging (a pull ratio of <see cref="Stuck.SafeRatioPm"/> or less): the force needed is <paramref name="freqMilliN"/>, the animals are
    /// <see cref="ReferenceBeast"/>. 0 when no pull is needed, -1 when even the most the vehicle takes is too few.
    /// </summary>
    public int AnimalsNeeded(VehicleRow r, long freqMilliN)
    {
        var st = StatsOf(r); if (st.HitchSlots <= 0) return 0;
        return VehicleMath.AnimalsNeeded(ReferenceBeast(r).PullN, st.HitchSlots, st.MaxAnimals, freqMilliN, Stuck.SafeRatioPm);
    }

    private int MinWalk(VehicleRow r)
    {
        int m = int.MaxValue;
        foreach (var id in r.Team) if (Animals.Find(id) is { } a) m = Math.Min(m, Animals.Catalog.At(a.Breed).WalkCmps);
        return m == int.MaxValue ? 0 : m;
    }

    /// <summary>Total mass of the loaded vehicle (kg): the template and what is in the bed.</summary>
    public int GrossKg(VehicleRow r) => StatsOf(r).MassKg + CargoKg(r);

    // ------------------------------------------------------------------ cargo (conserving)

    /// <summary>Loads goods from another store into the bed: all or nothing, mass and volume limits together. True when they fit.</summary>
    public long GoodMassG(ushort good) => _goods.MassG(good, 1);
    public bool Load(VehicleRow r, Store from, ushort good, long qty) => Store.Move(from, r.Cargo, good, qty);
    public bool Unload(VehicleRow r, Store to, ushort good, long qty) => Store.Move(r.Cargo, to, good, qty);

    /// <summary>Moves cargo to the ground until at least <paramref name="targetKg"/> kilograms are off the bed (5 kg per game second per person, the caller charges the time); returns grams moved.</summary>
    public long UnloadKg(VehicleRow r, Store ground, long kg)
    {
        long want = kg * 1000, moved = 0;
        foreach (var (good, qty) in r.Cargo.Rows())
        {
            if (moved >= want) break;
            long unit = _goods.MassG(good, 1); if (unit <= 0) continue;
            long take = Math.Min(qty, (want - moved + unit - 1) / unit);
            if (Store.Move(r.Cargo, ground, good, take)) moved += take * unit;
        }
        return moved;
    }

    // ------------------------------------------------------------------ wear

    /// <summary>Wheels wear 1 permille per 20 m at full axle load, scaled by the load (spec 4.4). A broken wheel stops the cart.</summary>
    public void Wear(VehicleRow r, long movedMm)
    {
        var st = StatsOf(r); r.OdometerMm += movedMm;
        if (st.AxleLimitKg <= 0 || st.Wheels == 0) return;
        long loadPm = (long)GrossKg(r) * 1000 / st.AxleLimitKg;
        long mult = loadPm > 1000 ? 3 : 1;
        r.WearAccMm += movedMm * loadPm * mult / 1000;
        long step = 20_000L / Math.Max(1, Catalog.Tuning.WheelWearPm);
        while (r.WearAccMm >= step) { r.WearAccMm -= step; r.WheelPm = Math.Max(0, r.WheelPm - 1); }
    }

    public void Repair(VehicleRow r) { r.WheelPm = 1000; r.WearAccMm = 0; }
}

/// <summary>Getting stuck and getting out (spec 4.6). All integer, all pure.</summary>
public static class Stuck
{
    public const int DigPm = 200, DigMinutes = 10;
    /// <summary>The pull ratio (permille) up to which a team can walk on a bogging surface without risk of sticking (<see cref="StuckPm"/> starts above it): what the cart bar counts animals for.</summary>
    public const int SafeRatioPm = 700;

    /// <summary>Surfaces that can bog a vehicle down: resistance of 170 permille or more.</summary>
    public static bool CanBog(VehicleTuning tu, Surface s, bool runners) => tu.Crr(s, runners) >= 170;

    /// <summary><c>clamp(ratio - 700, 0, 600) / 2</c> permille per check (0..300), x1.5 on deep mud.</summary>
    public static int StuckPm(int ratioPm, Surface s) => Math.Clamp(ratioPm - 700, 0, 600) / 2 * (s == Surface.DeepMud ? 3 : 2) / 2;

    /// <summary>
    /// The stuck roll for a stretch of 20 m: only on bogging surfaces; a ratio above 1,300 is stuck at once; otherwise stream 0x351 keyed on the game hour, the vehicle and <c>odometer_m / 20</c>, so
    /// the roll belongs to a place on the road, whatever the speed.
    /// </summary>
    public static bool Check(ulong seed, EntityId vehicle, long gameHour, long draw20, int ratioPm, Surface s, VehicleTuning tu, bool runners)
    {
        if (!CanBog(tu, s, runners)) return false;
        if (ratioPm > 1300) return true;
        int pm = StuckPm(ratioPm, s);
        return pm > 0 && DetRng.Range(seed, gameHour, vehicle.Value, RngStreams.VehicleStuck, (uint)draw20, 0, 1000) < pm;
    }

    /// <summary>The force (millinewtons) it takes to move a stuck vehicle: <c>mass x 9.81 x (crr x 1.5 + slope)</c>, the surface's resistance cut by a dug-out wheel.</summary>
    public static long FReqStuckMilliN(VehicleTuning tu, int massKg, int crrPm, int slopePm, bool dug)
    {
        long crr = crrPm; if (dug) crr = crr * (1000 - DigPm) / 1000;
        return VehicleMath.FReqMilliN(massKg, (int)(crr * tu.StuckCrrMultPm / 1000) + slopePm);
    }

    /// <summary>Whether the team and the pushers together move a stuck vehicle: <c>F_pull + pushers >= F_req(stuck)</c> (strength 50 gives 300 N each).</summary>
    public static bool Freed(VehicleTuning tu, int massKg, int crrPm, int slopePm, long pullN, long pushN, bool dug)
        => (pullN + pushN) * 1000 >= FReqStuckMilliN(tu, massKg, crrPm, slopePm, dug);

    public static int PushN(VehicleTuning tu, int strength) => tu.PushNPerStrength * strength;

    /// <summary>How long an abstract caravan is held up when its leg's ratio is above 1,300: 3 to 18 game hours, stream 0x351 keyed on the vehicle and the leg index.</summary>
    public static int AbstractDelayHours(ulong seed, EntityId vehicle, int leg) => 3 + DetRng.Range(seed, 0, vehicle.Value, RngStreams.VehicleStuck, 0x8000_0000u | (uint)leg, 0, 16);
}
