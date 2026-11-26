using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Siege;

namespace Ravensguard.Sim.Naval;

public enum ShipState : byte { Afloat, Taken, Sunk }
public enum ShipEndKind : byte { Taken, Sunk }
public enum IgniteKind : byte { Arrow, Pitch }

/// <summary>A ship's end (4.6 "Ending"): taken by a captor, or sunk (holed, burnt out). <see cref="Cause"/> is a short word for the chronicle's note.</summary>
public sealed record ShipEnd(ShipEndKind Kind, ulong Ship, EntityId Master, EntityId Captor, string Cause, long Sec);

/// <summary>
/// The numbers of naval combat (ships-and-naval-war 4.6) that the spec names, with the readings that fill its gaps (each is said in the Notes of the spec). Whole numbers only.
/// </summary>
public sealed record ShipFightRules
{
    /// <summary>The wall rule of sieges-and-engines 3.1 for a hull cell: <c>max(0, M - threshold) / 60</c> hit points (<c>hull_plank_sea</c> threshold 600).</summary>
    public int RamThreshold { get; init; } = 600;
    public int RamDivisor { get; init; } = 60;
    /// <summary><c>M = mass kg x closing mm/s / RamMassDiv</c> (the spec's "mass t x 1,000 x v_c m/s / 10": a 9 t longship at 4 m/s is 3,600).</summary>
    public int RamMassDiv { get; init; } = 10_000;
    /// <summary>The hulls stay locked for this many seconds after the strike, the closing speed halving each second (a reading: one blow cannot hole a 250 hp strake from 12,000, three can).</summary>
    public int RamContactS { get; init; } = 3;
    public int BowShareDiv { get; init; } = 3;
    public int SailIgnitePm { get; init; } = 600;
    public int DeckIgnitePm { get; init; } = 200;
    public int PitchPm { get; init; } = 1500;
    /// <summary>Hit points burnt off each burning part a game minute.</summary>
    public int BurnHpPerMin { get; init; } = 12;
    /// <summary>The chance (permille, every 10 s) that a burning part lights each neighbour (a sail's neighbour catches three times as readily).</summary>
    public int SpreadPm { get; init; } = 40;
    public int MastHitHp { get; init; } = 30;
    /// <summary>A bucket crew puts out one burning part for each 20 man-seconds, twice that with the sea at hand (Phase 6 firefighting).</summary>
    public int DouseManSeconds { get; init; } = 20;
    public int SeaWaterX { get; init; } = 2;
    public int GrappleRangeMm { get; init; } = 6000;
    public int CatchBasePm { get; init; } = 450;
    public int CatchPerSkillPm { get; init; } = 6;
    public int CatchWaveDiv { get; init; } = 4;
    public int HaulBaseS { get; init; } = 40;
    public int HaulMinS { get; init; } = 20;
    public int HaulSecondsPerMan { get; init; } = 5;
    public int HaulBaseMen { get; init; } = 4;
    /// <summary>A man's climb on a rope up a side 1 m higher or more takes 4 s (80 ticks); over a low rail, 1 s.</summary>
    public int ClimbTicks { get; init; } = 80;
    public int StepOverTicks { get; init; } = 20;
    public int ClimbOverMm { get; init; } = 1000;
    public int SinkLitresPerCell { get; init; } = 400;
    public static readonly ShipFightRules Default = new();
}

/// <summary>What a ship in a fight has suffered that the part rows do not show (the optional <c>naval</c> section's ship rows): its end, what burns, the fire's draws.</summary>
public sealed class ShipCombatRow
{
    public ulong Ship;
    public ShipState State;
    public EntityId Captor;
    /// <summary>The chronicle entry of its end (0 until written).</summary>
    public ulong Entry;
    /// <summary>The fire's draw counter (the key of every ignition draw).</summary>
    public uint Serial;
    public long BurnCarry;
    public bool SailDown;
    public int Bailers;
    /// <summary>Indices into the vehicle's part rows, ascending.</summary>
    public List<int> Burning { get; } = [];
}

/// <summary>Two ships bound by grapples: the lines that caught, the men hauling, and the joined pose once drawn together (the offset of the second in the first's frame).</summary>
public sealed class GrappleBind
{
    public ulong A, B;
    public int Lines;
    public int HaulMen;
    public int HaulS;
    public bool Joined;
    public int OffLx, OffLy, OffHeading;
    public int Throws;
}

/// <summary>
/// P7-SH-D: the part of naval combat that is the ship's own and outlives a fight (ships-and-naval-war 4.6): ramming through the Siege wall rule, water in a holed hull and the sinking, fire on sails and decks, grapples and the haul that binds
/// two ships, and a ship taken as a prize. The fight's own part (castles' missiles, boarding on the decks) is <see cref="CarrierLayer"/>. Every number is whole; draws are 0x7D5 (the catch) and 0x7D6 (ignition and spread), keyed on the ship and a
/// counter; ships and their rows are walked in id order.
/// </summary>
public sealed class ShipCombat(RideBook rides, NavalBook book, ulong seed, ShipFightRules? rules = null)
{
    public RideBook Rides { get; } = rides;
    public NavalBook Book { get; } = book;
    public ShipFightRules R { get; } = rules ?? ShipFightRules.Default;
    private VehicleCatalog Cat => Rides.Templates;

    public VehicleRow? Ship(ulong id) => Rides.Vehicles.Find(new EntityId(id));

    public ShipCombatRow Row(ulong ship)
    {
        if (!Book.Ships.TryGetValue(ship, out var r)) { Book.Ships[ship] = r = new ShipCombatRow { Ship = ship }; Book.Touch(); }
        return r;
    }

    private IReadOnlyList<string> Tags(VehicleRow v, int i) => Cat.PartList[v.Parts[i].Part].Tags;

    /// <summary>The part rows at a cell of a deck, in stack order (bottom first).</summary>
    public List<int> Stack(VehicleRow v, int x, int y, int deck)
    {
        var l = new List<int>();
        for (int i = 0; i < v.Parts.Count; i++) { var p = v.Parts[i]; if (p.X == x && p.Y == y && p.Deck == deck) l.Add(i); }
        return l;
    }

    // ------------------------------------------------------------------ ramming

    /// <summary>What a ram did: the hit points taken off the struck cell (the seconds of contact summed), whether it broke, the bow's share, and the first blow's momentum.</summary>
    public sealed record RamResult(bool Struck, int HullHp, bool Holed, int BowHp, long MomentumKgMs);

    /// <summary>
    /// A rammer's <c>stem_post</c> strikes the hull cell (cx, cy) of deck 0 of <paramref name="target"/> at <paramref name="closingMmps"/> (4.6). <c>M = mass x v_c / 10</c> and the cell takes <c>max(0, M - 600) / 60</c> hit points; the hulls stay
    /// locked for <see cref="ShipFightRules.RamContactS"/> seconds with the closing speed halving each second, each second a blow of its own. The rammer's bow takes a third of what the hull took. A cell at 0 is a hole.
    /// </summary>
    public RamResult Ram(ulong rammer, ulong target, int cx, int cy, int closingMmps)
    {
        var rv = Ship(rammer); var tv = Ship(target);
        if (rv == null || tv == null || closingMmps <= 0) return new RamResult(false, 0, false, 0, 0);
        int stem = -1; for (int i = 0; i < rv.Parts.Count && stem < 0; i++) if (rv.Parts[i].Built && Tags(rv, i).Contains("stem")) stem = i;
        if (stem < 0)                                                                                // the cog has no stem post (SH-A): her bow is the foremost planking of her keel line
            for (int i = 0; i < rv.Parts.Count; i++)
                if (rv.Parts[i].Built && rv.Parts[i].Deck == 0 && Tags(rv, i).Contains("hull") && (stem < 0 || rv.Parts[i].Y > rv.Parts[stem].Y)) stem = i;
        int hull = -1; foreach (int i in Stack(tv, cx, cy, 0)) if (tv.Parts[i].Built && Tags(tv, i).Contains("hull")) hull = i;
        if (stem < 0 || hull < 0) return new RamResult(false, 0, false, 0, 0);
        long mass = Rides.Vehicles.GrossKg(rv), first = mass * closingMmps / R.RamMassDiv, hp = 0;
        for (int s = 0; s < R.RamContactS; s++) hp += WallDamage.Impact(mass * (closingMmps >> s) / R.RamMassDiv, R.RamThreshold, R.RamDivisor);
        Rides.Vehicles.Damage(tv, hull, (int)Math.Min(int.MaxValue, hp * 1000));
        int bow = (int)(hp / R.BowShareDiv);
        if (bow > 0) Rides.Vehicles.Damage(rv, stem, bow * 1000);
        Row(target); Row(rammer);                                                  // both ships are in the book: one takes water, the other may burn
        Book.Touch();
        return new RamResult(true, (int)hp, tv.Parts[hull].State == PartState.Broken, bow, first);
    }

    /// <summary>The AI rams only with a heavier ship against a lighter one that cannot flee (4.6): medieval ships rarely rammed.</summary>
    public static bool WouldRam(int rammerKg, int targetKg, bool targetCanFlee) => rammerKg > targetKg && !targetCanFlee;

    /// <summary>The hull cells of deck 0 that are broken (each a hole: 20 L/s).</summary>
    public int Holes(VehicleRow v)
    {
        int n = 0;
        for (int i = 0; i < v.Parts.Count; i++) if (v.Parts[i].State == PartState.Broken && v.Parts[i].Deck == 0 && Tags(v, i).Contains("hull")) n++;
        return n;
    }

    // ------------------------------------------------------------------ fire

    private bool Burns(VehicleRow v, int i) => v.Parts[i].State == PartState.Built && !Tags(v, i).Contains("iron");

    private int TopBurnable(VehicleRow v, int x, int y, int deck, ShipCombatRow row)
    {
        int top = -1;
        foreach (int i in Stack(v, x, y, deck)) if (Burns(v, i)) top = i;
        return top >= 0 && !row.Burning.Contains(top) ? top : -1;
    }

    private void Light(ShipCombatRow row, int i) { if (!row.Burning.Contains(i)) { row.Burning.Add(i); row.Burning.Sort(); Book.Touch(); } }

    /// <summary>
    /// A fire arrow lands in the cell (cx, cy) of a deck, or a pitch pot bursts there (a 1 m radius: the cell and its neighbours): the top part that burns catches with its chance (canvas 600, planking 200; pitch x1.5), draw 0x7D6 keyed on the
    /// ship and the ship's fire counter. Returns how many parts took light.
    /// </summary>
    public int Ignite(ulong ship, int cx, int cy, int deck, IgniteKind kind)
    {
        var v = Ship(ship); if (v == null) return 0;
        var row = Row(ship); int lit = 0;
        var cells = new List<(int X, int Y)> { (cx, cy) };
        if (kind == IgniteKind.Pitch) { cells.Add((cx - 1, cy)); cells.Add((cx + 1, cy)); cells.Add((cx, cy - 1)); cells.Add((cx, cy + 1)); }
        foreach (var (x, y) in cells)
        {
            int top = TopBurnable(v, x, y, deck, row); if (top < 0) continue;
            int pm = Tags(v, top).Contains("sail") ? R.SailIgnitePm : R.DeckIgnitePm;
            if (kind == IgniteKind.Pitch) pm = Math.Min(1000, pm * R.PitchPm / 1000);
            uint serial = row.Serial++;
            if (DetRng.Range(seed, serial, ship, RngStreams.ShipIgnition, 0x8000_0000u | serial, 0, 1000) >= pm) continue;
            Light(row, top); lit++;
        }
        return lit;
    }

    /// <summary>A bucket crew of <paramref name="men"/> works for <paramref name="seconds"/> at sea (twice the Phase 6 rate): it puts out one burning part for each 20 man-seconds, lowest part first. Returns the parts put out.</summary>
    public int Douse(ulong ship, int men, int seconds)
    {
        if (!Book.Ships.TryGetValue(ship, out var row) || row.Burning.Count == 0) return 0;
        int n = (int)Math.Min(row.Burning.Count, (long)men * seconds * R.SeaWaterX / R.DouseManSeconds);
        row.Burning.RemoveRange(0, n); Book.Touch();
        return n;
    }

    // ------------------------------------------------------------------ the hour (or any span): fire, water, sinking

    private static readonly (int X, int Y)[] Around = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>
    /// Runs every ship of the book that is afloat for <paramref name="seconds"/> of game time ending at <paramref name="gameSec"/> (in ship id order, in steps of at most 10 s): what burns loses hit points and lights its neighbours, a burnt sail
    /// drops and wounds the mast, a holed hull takes in 20 L/s a hole less the bailers' 5, and a ship whose water passes what her hull can float (freeboard gone, or 400 L a hull cell) founders. Returns the ships that ended.
    /// </summary>
    public List<ShipEnd> Step(int seconds, long gameSec)
    {
        var ends = new List<ShipEnd>();
        if (seconds <= 0) return ends;
        var wt = Cat.Water;
        foreach (var row in Book.Ships.Values.ToList())
        {
            if (row.State != ShipState.Afloat) continue;
            var v = Ship(row.Ship); if (v == null || v.Water == WaterState.Sunk) continue;
            long start = gameSec - seconds;
            for (int s = 0; s < seconds && row.State == ShipState.Afloat; s += 10)
            {
                int dt = Math.Min(10, seconds - s); long now = start + s + dt;
                if (row.Burning.Count > 0) { Burn(v, row, dt); Spread(v, row, now); }
                int holes = Holes(v);
                if (holes > 0 || v.WaterInL > 0)
                {
                    int flow = SeaState.InflowLps(wt, 0, int.MaxValue / 4, holes) - SeaState.BailLps(wt, row.Bailers, 0);
                    v.WaterInL = (int)Math.Min(int.MaxValue / 2, SeaState.WaterAfter(v.WaterInL, flow, 0, dt));
                }
                if (Floundered(v) || BurntOut(v)) { ends.Add(Founder(v, row, Holes(v) > 0 ? "holed" : "fire", now)); break; }
            }
        }
        return ends;
    }

    private bool Floundered(VehicleRow v)
    {
        if (v.WaterInL <= 0) return false;
        var st = Rides.Vehicles.StatsOf(v);
        var load = BoatMath.Load(st, Rides.Vehicles.CargoKg(v), st.Rowers * BoatMath.PersonKg, Cat.Water);
        return SeaState.FreeboardNow(load, v.WaterInL, st.WaterplaneMm2, st.BlockPm) <= 0 || v.WaterInL > st.HullCells * R.SinkLitresPerCell;
    }

    /// <summary>A hull with over half of its deck-0 planking gone to fire or blows founders however little water stands in her.</summary>
    private bool BurntOut(VehicleRow v)
    {
        int hull = 0, broken = 0;
        for (int i = 0; i < v.Parts.Count; i++) if (v.Parts[i].Deck == 0 && Tags(v, i).Contains("hull")) { hull++; if (v.Parts[i].State == PartState.Broken) broken++; }
        return hull > 0 && broken * 2 > hull;
    }

    private ShipEnd Founder(VehicleRow v, ShipCombatRow row, string cause, long sec)
    {
        Rides.Sink(v);                                        // the cargo goes to the sea bed (mass exact); the people are told through RideBook.OnSunk
        row.State = ShipState.Sunk; row.Burning.Clear(); Book.Touch();
        return new ShipEnd(ShipEndKind.Sunk, row.Ship, v.Owner, EntityId.None, cause, sec);
    }

    private void Burn(VehicleRow v, ShipCombatRow row, int seconds)
    {
        row.BurnCarry += R.BurnHpPerMin * 1000L * seconds;
        long loss = row.BurnCarry / 60; row.BurnCarry %= 60;
        if (loss <= 0) return;
        foreach (int i in row.Burning.ToList())
        {
            if (v.Parts[i].State != PartState.Built) { row.Burning.Remove(i); continue; }
            Rides.Vehicles.Damage(v, i, (int)Math.Min(loss, v.Parts[i].HpMilli));
            if (v.Parts[i].State != PartState.Broken) continue;
            row.Burning.Remove(i);
            var p = v.Parts[i]; bool sail = Cat.PartList[p.Part].Tags.Contains("sail");
            // the fire goes down the stack to the next part that burns; a sail that burns away drops, and the mast it hung on takes a blow
            var stack = Stack(v, p.X, p.Y, p.Deck); bool below = false;
            for (int k = stack.Count - 1; k >= 0; k--)
            {
                int j = stack[k];
                if (j == i) { below = true; continue; }
                if (below && Burns(v, j)) { Light(row, j); break; }
            }
            if (sail)
            {
                row.SailDown = true;
                foreach (int j in stack) if (v.Parts[j].State == PartState.Built && Cat.PartList[v.Parts[j].Part].Tags.Contains("mast")) { Rides.Vehicles.Damage(v, j, R.MastHitHp * 1000); break; }
            }
        }
        Book.Touch();
    }

    private void Spread(VehicleRow v, ShipCombatRow row, long now)
    {
        foreach (int i in row.Burning.ToList())
        {
            var p = v.Parts[i];
            for (int d = 0; d < 4; d++)
            {
                int nx = p.X + Around[d].X, ny = p.Y + Around[d].Y;
                int top = TopBurnable(v, nx, ny, p.Deck, row); if (top < 0) continue;
                int pm = R.SpreadPm * (Tags(v, top).Contains("sail") ? 3 : 1);
                if (DetRng.Range(seed, now / 10, row.Ship, RngStreams.ShipIgnition, (uint)(i << 3 | d), 0, 1000) < pm) Light(row, top);
            }
        }
    }

    // ------------------------------------------------------------------ grapples (4.6)

    public GrappleBind? BindOf(ulong a, ulong b)
    {
        ulong lo = Math.Min(a, b), hi = Math.Max(a, b);
        return Book.Binds.Find(x => x.A == lo && x.B == hi);
    }

    /// <summary>
    /// A man within 6 m of the other ship's gunwale throws a grapple (the throw is the Phase 4 thrown-object rule; here is only whether the hook <i>catches</i> a moving gunwale): <c>450 + 6 x Throwing - waves / 4</c> permille, between 50 and 950
    /// (stream 0x7D5, keyed on the pair and the pair's throw counter). Two lines that caught bind the ships once hauled in.
    /// </summary>
    public bool Throw(ulong from, ulong to, int distMm, int throwingSkill, int waveMm)
    {
        if (distMm > R.GrappleRangeMm || Ship(from) == null || Ship(to) == null) return false;
        ulong lo = Math.Min(from, to), hi = Math.Max(from, to);
        var b = BindOf(from, to);
        if (b == null) { b = new GrappleBind { A = lo, B = hi }; Book.Binds.Add(b); Book.Binds.Sort((p, q) => p.A != q.A ? p.A.CompareTo(q.A) : p.B.CompareTo(q.B)); }
        int pm = Math.Clamp(R.CatchBasePm + R.CatchPerSkillPm * throwingSkill - waveMm / R.CatchWaveDiv, 50, 950);
        bool caught = DetRng.Range(seed, b.Throws, lo ^ (hi << 21), RngStreams.GrappleCatch, 0, 0, 1000) < pm;
        b.Throws++;
        if (caught) b.Lines++;
        Book.Touch();
        return caught;
    }

    /// <summary>The seconds to haul two ships together: 40 with four men on the two lines (2 a grapple at 300 N), 5 less for each man more, never under 20.</summary>
    public int HaulSeconds(int men) => Math.Clamp(R.HaulBaseS - (men - R.HaulBaseMen) * R.HaulSecondsPerMan, R.HaulMinS, R.HaulBaseS);

    /// <summary>The haulers work for <paramref name="seconds"/> more; with two lines caught, when the time is done the ships are bound (true).</summary>
    public bool Haul(GrappleBind b, int men, int seconds)
    {
        if (b.Lines < 2 || men <= 0) return b.Joined;
        b.HaulMen = men; b.HaulS += seconds;
        if (b.HaulS >= HaulSeconds(men)) b.Joined = true;
        Book.Touch();
        return b.Joined;
    }

    // ------------------------------------------------------------------ prizes

    /// <summary>
    /// A ship whose crew are all down, overboard, yielded or dead is taken (4.6 "Ending"): the captor's ship and cargo (the vehicle row and its store change hands; nothing is made or lost, the Phase 4 loot rule is only that goods move). Returns the end,
    /// or null if she is not afloat.
    /// </summary>
    public ShipEnd? Take(ulong ship, EntityId captor, long sec)
    {
        var v = Ship(ship); if (v == null) return null;
        var row = Row(ship); if (row.State != ShipState.Afloat || v.Water == WaterState.Sunk) return null;
        var master = v.Owner;
        v.Owner = captor; row.State = ShipState.Taken; row.Captor = captor; Book.Touch();
        return new ShipEnd(ShipEndKind.Taken, ship, master, captor, "boarded", sec);
    }
}
