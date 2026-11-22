using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Naval;

namespace Ravensguard.Sim.Siege;

/// <summary>Why an aim was refused (sieges-and-engines 4.2).</summary>
public enum AimResult : byte { Ok, NotAShooter, NotFinished, OutOfRange, TooClose }

/// <summary>
/// One siege engine at work (sieges-and-engines 4.2): a finished vehicle of an engine template with its crew, what it is aimed at, when its next shot is ready and what is burning on it. Everything here is saved
/// (<see cref="Save.SiegeSave"/>); the parts, their hit points and the wet hides' clock are the vehicle's and this record's own. All times are game seconds: one live tick is one game second, so the live tier and the abstract
/// hour step the very same numbers.
/// </summary>
public sealed class Engine
{
    public EntityId Vehicle;
    public string Template = "";
    public EngineKind Kind;
    /// <summary>The people who work the engine (swing, wind, load), and those who push it, each in id order.</summary>
    public List<EntityId> Crew = [];
    public List<EntityId> Pushers = [];
    public bool HasAim;
    public long AimX, AimY;
    public int AimZ;
    /// <summary>The game second the engine has been stepped to, and the second its next shot is ready.</summary>
    public long ClockSec, ReadyAtSec;
    /// <summary>Shots loosed (the key of the next landing, stream 0x7C0), fire rolls made and cover rolls made.</summary>
    public uint Serial, FireSerial, CoverSerial;
    /// <summary>The hides on it are wet until this second.</summary>
    public long WetUntilSec;
    /// <summary>The parts (indices of the vehicle's rows, ascending) that are burning, and the burn's remainder in sixtieths of a milli-hit-point.</summary>
    public List<int> Burning = [];
    public long BurnCarry;
    /// <summary>A tower that lost its base and fell, and an engine that can no longer do its work (its chronicle entry is written once).</summary>
    public bool Toppled, Disabled;
    public long Fired;
}

/// <summary>What a shot did where it landed.</summary>
public readonly record struct ShotReport(long X, long Y, WallBlowKind Wall, int Wounded, int PartsBroken, int Engines);

/// <summary>What the book needs of the world around it: the walls, the people, the stores, the skills. Every member is optional; a missing one means nothing of that kind is there.</summary>
public sealed class EngineWorld
{
    public WallWorld? Walls;
    /// <summary>A person's Engineering skill (0 when he has none) and whether he is at his engine and fit to work it.</summary>
    public Func<EntityId, int>? Engineering;
    public Func<EntityId, bool>? Present;
    /// <summary>The store a trebuchet's stones are drawn from, and the goods catalog its grams are counted in.</summary>
    public Func<Engine, Store?>? AmmoOf;
    public GoodCatalog? Goods;
    /// <summary>People near a landing: called with (x mm, y mm, radius mm, momentum milli, a key, whether it is a bolt) and returns how many were struck (the fight's resolver does the harm: <see cref="EngineFight"/>).</summary>
    public Func<long, long, int, long, ulong, bool, int>? People;
    public SiegeChronicle? Chronicle;
    /// <summary>The chronicle entry the blows belong to (the siege), if there is one.</summary>
    public EntityId Cause;
    /// <summary>Called after each shot.</summary>
    public Action<Engine, ShotReport>? OnShot;
}

/// <summary>
/// P7-SG-B: the siege engines of a world (sieges-and-engines 4.2): the book of engines over the ride book's vehicles. An engine is a finished vehicle of a template that <c>siege/engines.jsonc</c> names; the book
/// says who crews and pushes it, where it is aimed, steps it through time (shots, burning), and sends each landing into the walls (SG-A's rule), the people (the one resolver) and other engines' parts.
/// </summary>
public sealed partial class EngineBook
{
    public ContentDatabase Content { get; }
    public EngineTable Table { get; }
    public RideBook Rides { get; }
    public ulong Seed { get; }
    public SortedDictionary<EntityId, Engine> Engines { get; } = [];
    public bool IsEmpty => Engines.Count == 0;

    public EngineBook(ContentDatabase content, RideBook rides, ulong seed) { Content = content; Table = EngineTable.Of(content); Rides = rides; Seed = seed; }

    public Engine? Find(EntityId vehicle) => Engines.GetValueOrDefault(vehicle);
    public VehicleRow? VehicleOf(Engine e) => Rides.Vehicles.Find(e.Vehicle);
    public EngineStatDef Stat(Engine e) => Table.Stat(e.Template)!;
    private VehicleCatalog Cat => Rides.Templates;

    /// <summary>Takes a finished vehicle of an engine template into the book (null when it is none, or unfinished): the hides on it are wet, and an engine that shoots starts winding.</summary>
    public Engine? Raise(VehicleRow v, long nowSec)
    {
        if (Engines.TryGetValue(v.Id, out var had)) return had;
        var st = Table.Stat(v.Design);
        if (st == null || !v.Finished) return null;
        var e = new Engine { Vehicle = v.Id, Template = v.Design, Kind = Table.Kind(v.Design), ClockSec = nowSec, ReadyAtSec = nowSec + st.ReloadS, WetUntilSec = nowSec + Table.Rules.HidesDryHours * 3600L };
        Engines[v.Id] = e;
        return e;
    }

    /// <summary>Sets who works and who pushes the engine (each list is kept in id order).</summary>
    public void Assign(Engine e, IEnumerable<EntityId> crew, IEnumerable<EntityId>? pushers = null)
    {
        e.Crew = [.. crew.Distinct().OrderBy(x => x)];
        if (pushers != null) e.Pushers = [.. pushers.Distinct().OrderBy(x => x)];
    }

    // ------------------------------------------------------------------ the engine as a vehicle: where it is, which of its cells a point is in

    /// <summary>The design's width and length in cells.</summary>
    public (int W, int L) Size(VehicleRow v) { var s = Rides.Vehicles.DesignOf(v).Stats; return (s.WidthCells, s.LengthCells); }

    /// <summary>The cell (column, row from the rear) of the vehicle that a world point is over, or null when it is outside its footprint. The vehicle's centre is its position; its facing is a byte of a full turn (0 along +x) and its rows run from the rear to the bow.</summary>
    public (int X, int Y)? CellAt(VehicleRow v, long wx, long wy)
    {
        var (w, l) = Size(v);
        var (cos, sin) = FrameMath.CosSin(v.Facing << 16);
        var (lx, ly) = FrameMath.ToLocal((int)v.XMm, (int)v.YMm, cos, sin, (int)wx, (int)wy);
        if ((uint)(lx + w * 500) >= (uint)(w * 1000) || (uint)(ly + l * 500) >= (uint)(l * 1000)) return null;
        return (Math.DivRem(lx + w * 500, 1000, out _), Math.DivRem(ly + l * 500, 1000, out _));
    }

    /// <summary>The world point of the vehicle's front (the bow, half its length ahead of its centre).</summary>
    public (long X, long Y) FrontOf(VehicleRow v)
    {
        var (_, l) = Size(v);
        var (cos, sin) = FrameMath.CosSin(v.Facing << 16);
        var (x, y) = FrameMath.ToWorld((int)v.XMm, (int)v.YMm, cos, sin, 0, l * 500);
        return (x, y);
    }

    /// <summary>The vehicle's part rows in one cell, top first: the highest deck, then the last of its stack.</summary>
    public List<int> Stack(VehicleRow v, int cx, int cy)
    {
        var l = new List<int>();
        for (int i = 0; i < v.Parts.Count; i++) if (v.Parts[i].X == cx && v.Parts[i].Y == cy) l.Add(i);
        l.Sort((a, b) => { int c = v.Parts[b].Deck.CompareTo(v.Parts[a].Deck); return c != 0 ? c : v.Parts[b].Order.CompareTo(v.Parts[a].Order); });
        return l;
    }

    // ------------------------------------------------------------------ what works

    private int Count(VehicleRow v, string tag, out int built)
    {
        int total = 0; built = 0;
        foreach (var p in v.Parts)
        {
            if (!Cat.PartList[p.Part].Tags.Contains(tag)) continue;
            total++; if (p.State == PartState.Built) built++;
        }
        return total;
    }

    private int CountPart(VehicleRow v, string id, out int built)
    {
        int total = 0; built = 0;
        foreach (var p in v.Parts) if (Cat.PartList[p.Part].Id == id) { total++; if (p.State == PartState.Built) built++; }
        return total;
    }

    /// <summary>Whether the engine's working parts stand: the ram's beam; a trebuchet's arm in all its lengths, its counterweight, its sling and two thirds of its frame bays; the springald; a tower its floors on the ground; a mantlet one board.</summary>
    public bool Works(Engine e)
    {
        var v = VehicleOf(e); if (v == null || !v.Finished) return false;
        int t, b;
        switch (e.Kind)
        {
            case EngineKind.Ram: t = Count(v, "ram", out b); return t > 0 && b == t;
            case EngineKind.Springald: t = Count(v, "springald", out b); return t > 0 && b == t;
            case EngineKind.Trebuchet:
                t = Count(v, "arm", out b); if (t == 0 || b < t) return false;
                if (Count(v, "counterweight", out b) == 0 || b == 0) return false;
                if (Count(v, "sling", out b) == 0 || b == 0) return false;
                t = CountPart(v, "trebuchet_frame", out b); return t > 0 && b * 3 >= t * 2;
            case EngineKind.Tower: return !e.Toppled;
            default: t = Count(v, "mantlet", out b); return t > 0 && b > 0;
        }
    }

    /// <summary>Whether the engine's vehicle is whole enough to roll: no wheel or axle broken (the cart rule) and it did not topple.</summary>
    public bool CanRoll(Engine e) { var v = VehicleOf(e); return v != null && v.Finished && !v.Broken && !e.Toppled; }

    private int PresentCrew(Engine e, EngineWorld w) { int n = 0; foreach (var c in e.Crew) if (w.Present?.Invoke(c) ?? true) n++; return n; }

    /// <summary>Game seconds between shots with the crew that is there: the full time with the crew the engine needs, longer in proportion with fewer, never with fewer than half.</summary>
    public int ReloadFor(Engine e, int present)
    {
        var st = Stat(e);
        if (st.Crew <= 0) return st.ReloadS;
        if (present * 2 < st.Crew) return 0;
        return present >= st.Crew ? st.ReloadS : (int)((long)st.ReloadS * st.Crew / present);
    }

    // ------------------------------------------------------------------ aiming

    /// <summary>Aims at a point (mm) on level <paramref name="z"/> of the world: refused when the engine does not shoot, is unfinished, or the point is out of its range. A ram must be at the wall (its front within reach); a stone-thrower has a least and a greatest range.</summary>
    public AimResult Aim(Engine e, long x, long y, int z = 0)
    {
        var st = Stat(e); var v = VehicleOf(e);
        if (!EngineTable.Shoots(e.Kind)) return AimResult.NotAShooter;
        if (v == null || !v.Finished) return AimResult.NotFinished;
        long fx = v.XMm, fy = v.YMm;
        if (e.Kind == EngineKind.Ram) (fx, fy) = FrontOf(v);
        long d = Combat.Melee2.FightMath.Len(x - fx, y - fy);
        if (d > st.MaxRangeM * 1000L) return AimResult.OutOfRange;
        if (d < st.MinRangeM * 1000L) return AimResult.TooClose;
        e.HasAim = true; e.AimX = x; e.AimY = y; e.AimZ = z;
        return AimResult.Ok;
    }

    public void ClearAim(Engine e) => e.HasAim = false;

    // ------------------------------------------------------------------ time

    /// <summary>
    /// Steps the engine to game second <paramref name="toSec"/>: whatever burns on it burns for the time, and each shot that falls due is loosed at its second (a trebuchet each 2,400, a ram's swing each 80). The live tier calls it once a tick,
    /// the abstract tier once an hour with <paramref name="daylightOnly"/> (a shot that falls in the night waits for 06:00): the same shots at the same seconds either way by day. Returns the shots loosed.
    /// </summary>
    public int Advance(Engine e, long toSec, EngineWorld w, bool daylightOnly = false)
    {
        int shots = 0;
        while (e.ClockSec < toSec)
        {
            long at = -1;
            if (CanShoot(e, w, out _)) { at = Math.Max(e.ClockSec, e.ReadyAtSec); if (daylightOnly) at = NextShootingSecond(at); }
            if (at < 0 || at > toSec) { Burn(e, toSec - e.ClockSec, toSec, w); e.ClockSec = toSec; break; }     // nothing falls due in this stretch
            Burn(e, at - e.ClockSec, at, w); e.ClockSec = at;
            if (!CanShoot(e, w, out int reload)) continue;                                                       // the fire took a working part on the way
            Fire(e, at, w);
            e.ReadyAtSec = at + Math.Max(1, reload);
            shots++;
        }
        return shots;
    }

    /// <summary>The shooting hours of the abstract tier (12 a day, 06:00 to 18:00): the engines rest at night.</summary>
    public bool ShootingHour(long hourOfDay) => hourOfDay >= Table.Rules.ShootFromHour && hourOfDay < Table.Rules.ShootToHour;

    /// <summary>The second a shot ready at <paramref name="t"/> is loosed in the shooting hours: at once by day, at dawn if it falls in the night.</summary>
    public long NextShootingSecond(long t)
    {
        long hod = t / 3600 % 24;
        if (ShootingHour(hod)) return t;
        long day = t / 86400;
        return (hod < Table.Rules.ShootFromHour ? day : day + 1) * 86400 + Table.Rules.ShootFromHour * 3600L;
    }

    /// <summary>One abstract hour (<paramref name="hour"/> counts from the world's start): every engine steps to the end of the hour, in id order. Returns the shots loosed.</summary>
    public int Hour(long hour, EngineWorld w)
    {
        int n = 0;
        foreach (var e in Engines.Values) n += Advance(e, (hour + 1) * 3600, w, daylightOnly: true);
        return n;
    }

    private bool CanShoot(Engine e, EngineWorld w, out int reload)
    {
        reload = 0;
        if (!EngineTable.Shoots(e.Kind) || !e.HasAim || !Works(e)) return false;
        reload = ReloadFor(e, PresentCrew(e, w));
        if (reload <= 0) return false;
        var st = Stat(e);
        if (st.Ammo.Length > 0 && !HasAmmo(e, st, w)) return false;
        return true;
    }

    private bool HasAmmo(Engine e, EngineStatDef st, EngineWorld w)
    {
        var store = w.AmmoOf?.Invoke(e); if (store == null || w.Goods == null) return false;
        ushort g = w.Goods.IdOf(st.Ammo); long unit = Math.Max(1, w.Goods.MassG(g, 1));
        return store.Qty(g) * unit >= st.ShotG;
    }

    /// <summary>The best Engineering of the crew that is there: the engineer who lays the engine.</summary>
    public int Engineering(Engine e, EngineWorld w)
    {
        int best = 0;
        foreach (var c in e.Crew) if (w.Present?.Invoke(c) ?? true) best = Math.Max(best, w.Engineering?.Invoke(c) ?? 0);
        return Math.Clamp(best, 0, 100);
    }

    // ------------------------------------------------------------------ a stable hash of the book (tests, the save round trip)

    public ulong Hash()
    {
        ulong h = 0xcbf29ce484222325UL;
        void M(long v) { h = (h ^ (ulong)v) * 0x100000001b3UL; h ^= h >> 29; }
        foreach (var e in Engines.Values)
        {
            M((long)e.Vehicle.Value); M((long)e.Kind); M(e.HasAim ? 1 : 0); M(e.AimX); M(e.AimY); M(e.AimZ); M(e.ClockSec); M(e.ReadyAtSec); M(e.Serial); M(e.FireSerial); M(e.CoverSerial); M(e.WetUntilSec); M(e.BurnCarry);
            M(e.Toppled ? 1 : 0); M(e.Disabled ? 1 : 0); M(e.Fired);
            foreach (var c in e.Crew) M((long)c.Value);
            M(-1); foreach (var c in e.Pushers) M((long)c.Value);
            M(-2); foreach (int b in e.Burning) M(b);
        }
        return h;
    }
}
