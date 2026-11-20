using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Burning;

// Fire and burning (docs/specs/phase6/fire-and-burning.md). All integer. Times: a GAME SECOND is the unit of every stored timestamp; a sim-second is 20 game seconds (one real second at 1x),
// a game hour is 180 sim-seconds. Burn times, fuel left and ages are sim-seconds. Cells are keyed like RoomWorld's ((z, y, x) ordered), and every walk is in key order.

public static class FireClock
{
    /// <summary>Game seconds in one sim-second (wounds spec: one real second at 1x is 20 game seconds).</summary>
    public const int GameSecPerSimS = 20;
    /// <summary>The spread step: every 5 sim-seconds (fire 4.2).</summary>
    public const int SpreadEveryS = 5;
    public static long SimS(long gameSec) => gameSec / GameSecPerSimS;
}

public enum CellFireState : byte { Smouldering, Burning, Burnt }

/// <summary>What a burnt cell leaves: drawn as char and ash tiles (fire 6). Append only.</summary>
public enum ScarKind : byte { Ash, Char, Cracked, Smoulder, Scorched, Rubble }

/// <summary>One burning cell (fire 3, saved in the optional "fire" section while it burns). <see cref="Fire"/> is the incident (the id of its <c>fire_started</c> entry).</summary>
public sealed class FireCell
{
    public long Key;
    public CellFireState State;
    public byte Intensity;
    /// <summary>The game second it caught.</summary>
    public long StartS;
    /// <summary>Sim-seconds of fuel left.</summary>
    public int FuelLeftS;
    public ulong Fire;
    /// <summary>The fuel class index in the <see cref="FuelTable"/>.</summary>
    public byte Class;
    /// <summary>Intensity levels knocked off by buckets (fire 4.4), and the buckets on the cell toward the next level (3 buckets a level).</summary>
    public byte Knock, Buckets;
}

/// <summary>One fire from its first flame to the last: what it cost (fire 4.6), kept until it ends and its <c>fire_ended</c> entry is written.</summary>
public sealed class FireIncident
{
    public ulong Entry;
    public long StartSec;
    public int CellsBurnt;
    public long GoodsMites;
    public long GoodsGrams;
    public SortedSet<int> Structures { get; } = [];
    public SortedSet<ulong> Deaths { get; } = [];
    public SortedSet<ulong> Fighters { get; } = [];
    public SortedSet<ulong> Burned { get; } = [];
    /// <summary>The kind of the cause ("hearth", "torch", "lightning", "riot", "arson", "firedamp", "spread"): the faith and crime hooks read it.</summary>
    public string CauseKind = "";
    /// <summary>The last natural reason a flame stopped at a stone wall (the faith hook, overview 10a): "stone_wall", "wind_change", "rain" or "". The first place it stopped.</summary>
    public string StoppedBy = "";
    public int StoppedAtStructure;
}

/// <summary>A person breathing smoke or standing in flame (fire 4.3): the incident whose smoke it is and the sim-seconds of smoke taken.</summary>
public sealed class Exposure
{
    public ulong Person;
    public ulong Fire;
    public int SmokeS;
    /// <summary>The game second the smoke stopped his breath (-1: breathing).</summary>
    public long StopSec = -1;
    public long LastSeenSec;
}

/// <summary>A fire in the abstract (fire 3): a building that burns for three game hours, with the damage it has done so far (permille).</summary>
public sealed class BuildingFire
{
    public int Structure;
    public long StartHour;
    public int BurnedHours;
    public int DamagePm;
    /// <summary>The flammable cells it had when it caught, and how many have burnt so far (the damage is a share of the first).</summary>
    public int Flammable, Taken;
    public ulong Fire;
    public string CauseKind = "";
    public bool Spread;
}

/// <summary>A fire that stopped without a roof to feed on (the faith hook, overview 10a): the natural reason is always one of the three.</summary>
public readonly record struct FireStop(long Sec, ulong Fire, int Structure, string Reason, bool Sacred);

/// <summary>Where a fire came from: the chronicle causes and subjects of its <c>fire_started</c> entry.</summary>
public sealed record FireCause(string Kind, string NoteKey, IReadOnlyList<Cause> Causes, IReadOnlyList<EntityId> Subjects)
{
    public static FireCause Hearth { get; } = Make("hearth", "ch_note_fire_hearth", CauseKind.Fire);
    public static FireCause Lightning { get; } = Make("lightning", "ch_note_fire_lightning", CauseKind.Weather);
    public static FireCause Firedamp { get; } = Make("firedamp", "ch_note_fire_firedamp", CauseKind.Fire);
    public static FireCause Spread { get; } = Make("spread", "ch_note_fire_spread", CauseKind.Fire);
    public static FireCause Test { get; } = Make("test", "ch_note_fire_test", CauseKind.Other);

    private static FireCause Make(string kind, string note, CauseKind ck) => new(kind, note, [new Cause(ck, null, "", note)], []);

    /// <summary>A person's flame (a torch held to thatch, an arsonist's, the hero's): the person is a subject, the cause is a crime when <paramref name="crime"/> names its entry.</summary>
    public static FireCause Person(EntityId who, string kind = "torch", EntityId? crime = null) =>
        new(kind, "ch_note_fire_" + kind, [crime is { } c ? new Cause(CauseKind.Crime, c, "", "ch_note_fire_" + kind) : new Cause(CauseKind.Crime, null, "", "ch_note_fire_" + kind)], [who]);

    /// <summary>A riot's torches (unrest): the riot's entry is the cause.</summary>
    public static FireCause Riot(EntityId riot) => new("riot", "ch_note_fire_riot", [new Cause(CauseKind.Unrest, riot, "", "ch_note_fire_riot")], []);
}

/// <summary>What the weather does to a fire at a moment (fire 4.2): wind in mm/s with the direction it comes from, rain, a drought, a storm.</summary>
public readonly record struct FireWeather(int WindMmS, int WindFromDeg, int RainMmH, bool Drought, bool Thunder)
{
    public static readonly FireWeather Calm = new(0, 0, 0, false, false);
    public bool Raining => RainMmH > 0;
}

/// <summary>A person where the fire can reach him: the live tier lists them (the bubble's people, the hero).</summary>
public readonly record struct FirePerson(ulong Person, int X, int Y, int Z, bool Asleep = false);
