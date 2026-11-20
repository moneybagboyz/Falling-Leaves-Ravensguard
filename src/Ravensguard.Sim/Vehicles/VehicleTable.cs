using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Carts;

/// <summary>Where a boat is (vehicle-designer 4.2, 4.5): on land (every land vehicle), on the slipway (a hull being built or waiting to launch), afloat, aground, or sunk.</summary>
public enum WaterState : byte { Land, Slipway, Afloat, Aground, Sunk }

/// <summary>
/// One vehicle in the world (horses-and-carts spec 3.3, vehicle-designer 3.3): where it stands, what pulls it, what it carries (the cargo is a real store, so its goods are counted by the mass ledger) and,
/// from P5-G, its design and part rows: each part's cell, state and hit points. Mass, cargo and the rest are derived from the built parts (<see cref="VehicleTable.StatsOf"/>), recomputed only when a part
/// changes (<see cref="Touch"/>).
/// </summary>
public sealed class VehicleRow
{
    public EntityId Id;
    /// <summary>The design's index in the running catalog (the save keeps <see cref="Design"/>, its id).</summary>
    public int Template;
    public string Design = "";
    public EntityId Owner;
    public long XMm, YMm;
    public byte Facing;
    public Store Cargo = null!;
    public List<EntityId> Team = [];
    public long OdometerMm;
    public bool Stuck;
    public long StuckSinceSec, DugUntilSec;
    public long LastDraw = -1;
    public long WearAccMm;
    public bool Driven;
    /// <summary>P5-G: the parts, ordered by deck, row, column and stack order.</summary>
    public List<PartRow> Parts = [];
    public WaterState Water;
    /// <summary>Water inside a boat, litres (a swamped boat sinks).</summary>
    public int WaterInL;
    internal VehicleCatalog? Cat;
    internal VehicleStats? Cached;
    internal bool? MoveCache;

    /// <summary>A part changed: the derived numbers are recomputed at the next read.</summary>
    public void Touch() { Cached = null; MoveCache = null; }

    /// <summary>
    /// Phase 3's wheel condition, permille (1000 new, 0 broken): the worst wheel's hit points against its new ones. Setting it sets every wheel to that share (the old saves' single value, and tests).
    /// </summary>
    public int WheelPm
    {
        get
        {
            if (Cat == null) return 1000;
            int pm = 1000; bool any = false;
            foreach (var p in Parts)
            {
                var d = Cat.PartList[p.Part]; if (!d.Tags.Contains("wheel") || d.Hp <= 0) continue;
                if (p.State is PartState.Ghost or PartState.InProgress) continue;
                any = true; pm = Math.Min(pm, (int)((long)p.HpMilli / d.Hp));
            }
            return any ? Math.Clamp(pm, 0, 1000) : 1000;
        }
        set
        {
            if (Cat == null) return;
            int v = Math.Clamp(value, 0, 1000);
            for (int i = 0; i < Parts.Count; i++)
            {
                var p = Parts[i]; var d = Cat.PartList[p.Part]; if (!d.Tags.Contains("wheel") || p.State is PartState.Ghost or PartState.InProgress) continue;
                p.HpMilli = d.Hp * v; p.State = v <= 0 ? PartState.Broken : PartState.Built; Parts[i] = p;
            }
            Touch();
        }
    }

    /// <summary>A wheel or an axle is broken (it stops; pushing does not free it), or a part it needs to move is not fitted yet (an unfinished wagon, a hull on the slipway).</summary>
    public bool Broken => Cat != null && (PartGrid.WheelOrAxleBroken(Parts, Cat) || !CanMove);
    public bool CanMove => Cat == null || (MoveCache ??= PartGrid.CanMove(Parts, Cat));
    /// <summary>Every part fitted (a finished vehicle; a damaged part still counts as fitted).</summary>
    public bool Finished { get { foreach (var p in Parts) if (p.State is PartState.Ghost or PartState.InProgress) return false; return true; } }
    public bool IsBoat => Cat != null && Template >= 0 && Template < Cat.All.Count && Cat.At(Template).Stats.Boat;
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

    /// <summary>A new vehicle of a design with every part built (a template cart bought or given), or, with <paramref name="ghosts"/>, every part a ghost: an unfinished vehicle site (vehicle-designer 4.2).</summary>
    public VehicleRow Create(VehicleBlueprint bp, EntityId owner, long xMm = 0, long yMm = 0, bool ghosts = false)
    {
        var id = MakeId(NextSerial++);
        var r = new VehicleRow { Id = id, Template = bp.Index, Design = bp.Id, Owner = owner, XMm = xMm, YMm = yMm, Cargo = NewStore(id, bp.Stats), Cat = Catalog, Parts = [.. bp.Parts] };
        if (ghosts) for (int i = 0; i < r.Parts.Count; i++) { var p = r.Parts[i]; p.State = PartState.Ghost; p.HpMilli = 0; r.Parts[i] = p; }
        if (bp.Stats.Boat) r.Water = ghosts ? WaterState.Slipway : WaterState.Afloat;
        _rows.Add(r);
        return r;
    }

    /// <summary>Puts a saved row back (restore): the caller fills the cargo. Rows are kept in id order.</summary>
    public VehicleRow Restore(VehicleRow r)
    {
        r.Cat = Catalog; r.Touch();
        int i = _rows.Count; while (i > 0 && _rows[i - 1].Id.CompareTo(r.Id) > 0) i--;
        _rows.Insert(i, r); return r;
    }
    public void ClearForRestore(ulong nextSerial) { _rows.Clear(); NextSerial = nextSerial; }
    public void Remove(VehicleRow r) { foreach (var id in r.Team) if (Animals.Find(id) is { } a) { a.Vehicle = EntityId.None; if (a.State == AnimalState.Hitched) a.State = AnimalState.Free; } _rows.Remove(r); }

    /// <summary>The derived numbers of the vehicle's built parts (P5-G: a wagon without its tilt is lighter), cached until a part changes; a finished, undamaged vehicle has its design's numbers exactly.</summary>
    public VehicleStats StatsOf(VehicleRow r)
    {
        if (r.Cached != null) return r.Cached;
        var bp = Catalog.At(r.Template);
        r.Cat ??= Catalog;
        bool all = true; foreach (var p in r.Parts) if (p.State != PartState.Built) { all = false; break; }
        return r.Cached = all && r.Parts.Count == bp.Parts.Count ? bp.Stats : PartGrid.Derive(bp.Def, r.Parts, Catalog, builtOnly: true);
    }

    /// <summary>The design a vehicle is built to.</summary>
    public VehicleBlueprint DesignOf(VehicleRow r) => Catalog.At(r.Template);

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

    /// <summary>Wear of a tier, permille of a Common wheel's (vehicle-designer 4.3: a Fine wheel x0.8).</summary>
    public static int TierWearPm(ItemQuality q) => q switch { ItemQuality.Crude => 1500, ItemQuality.Poor => 1200, ItemQuality.Fine => 800, ItemQuality.Masterwork => 600, _ => 1000 };

    /// <summary>How hard the ground is on wheels (vehicle-designer 4.3): road 1,000, field and mud and snow 2,000, sand and rubble 4,000 (the rock of a ford's approach counts as field).</summary>
    public static int RoughPm(Surface s) => s switch
    {
        Surface.Paved or Surface.DryRoad or Surface.WetRoad or Surface.Track or Surface.Ice or Surface.PackedSnow => 1000,
        Surface.Sand => 4000,
        _ => 2000,
    };

    /// <summary>
    /// Wheels wear on the move (vehicle-designer 4.3, Phase 3 spec 4.4): every 20 m at full axle load each built wheel loses a thousandth of its hit points, scaled by the load (x3 above the axle limit), by
    /// how rough the ground is (<paramref name="roughPm"/>, road 1,000) and by the wheel's own tier and <c>rough_wear_pm</c>. A Common wheel on a road wears exactly as Phase 3's one <c>WheelPm</c> did
    /// (VD-6). A wheel at 0 is broken and the vehicle stops. Deterministic: no roll.
    /// </summary>
    public void Wear(VehicleRow r, long movedMm, int roughPm = 1000)
    {
        var st = StatsOf(r); r.OdometerMm += movedMm;
        if (st.AxleLimitKg <= 0 || st.Wheels == 0) return;
        long loadPm = (long)GrossKg(r) * 1000 / st.AxleLimitKg;
        long mult = loadPm > 1000 ? 3 : 1;
        r.WearAccMm += movedMm * loadPm * mult / 1000 * roughPm / 1000;
        long step = 20_000L / Math.Max(1, Catalog.Tuning.WheelWearPm);
        bool broke = false;
        while (r.WearAccMm >= step)
        {
            r.WearAccMm -= step;
            for (int i = 0; i < r.Parts.Count; i++)
            {
                var p = r.Parts[i]; if (p.State != PartState.Built) continue;
                var d = Catalog.PartList[p.Part]; if (!d.Tags.Contains("wheel")) continue;
                long loss = (long)d.Hp * TierWearPm(p.Tier) / 1000 * d.RoughWearPm / 1000;
                if (roughPm <= 1000) loss = (long)d.Hp * TierWearPm(p.Tier) / 1000;      // the part's own rough factor counts only off the road
                p.HpMilli = (int)Math.Max(0, p.HpMilli - loss);
                if (p.HpMilli == 0) { p.State = PartState.Broken; broke = true; }
                r.Parts[i] = p;
            }
        }
        if (broke) r.Touch();
    }

    /// <summary>Phase 3's repair (the cart wheel): every wheel back to new.</summary>
    public void Repair(VehicleRow r) { r.WheelPm = 1000; r.WearAccMm = 0; }

    /// <summary>Repairs one part by <paramref name="addPm"/> of its hit points (vehicle-designer 4.3: the part recipe's repair line, +300); a broken part mended above 0 is whole again.</summary>
    public void RepairPart(VehicleRow r, int index, int addPm)
    {
        var p = r.Parts[index]; if (p.State is PartState.Ghost or PartState.InProgress) return;
        var d = Catalog.PartList[p.Part];
        p.HpMilli = (int)Math.Min((long)d.Hp * 1000, p.HpMilli + (long)d.Hp * addPm);
        if (p.HpMilli > 0) p.State = PartState.Built;
        r.Parts[index] = p; r.Touch();
    }

    /// <summary>A drop of more than a metre (a bank, a collapsed bridge) or an overload at once: every axle loses half its hit points (vehicle-designer 4.3).</summary>
    public void Jolt(VehicleRow r)
    {
        for (int i = 0; i < r.Parts.Count; i++)
        {
            var p = r.Parts[i]; if (p.State != PartState.Built || !Catalog.PartList[p.Part].Tags.Contains("axle")) continue;
            p.HpMilli /= 2; if (p.HpMilli == 0) p.State = PartState.Broken; r.Parts[i] = p;
        }
        r.Touch();
    }

    /// <summary>Damage to one part (combat, a fall): hit points off; at 0 it is broken (vehicle-designer 4.3).</summary>
    public void Damage(VehicleRow r, int index, int hpMilli)
    {
        var p = r.Parts[index]; if (p.State != PartState.Built) return;
        p.HpMilli = Math.Max(0, p.HpMilli - hpMilli); if (p.HpMilli == 0) p.State = PartState.Broken;
        r.Parts[index] = p; r.Touch();
    }
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
