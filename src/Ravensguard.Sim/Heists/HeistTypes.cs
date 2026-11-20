using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Heists;

/// <summary>What a guard is doing (heists-and-jailbreaks 4.2). Append only (saved).</summary>
public enum GState : byte { Patrol, Dwell, Post, Asleep, Investigate, Looking, Return, Alert, Pursue, Down, Bribed, Fire }

/// <summary>
/// A guard of a site on a run: a person (a real guard of the place, or a jailer made up to fill the roster), his post and bed, where he stands and what he does, the keys on his belt (door indices), how well he knows the hero's face, his honesty
/// (a bribe's measure) and the seconds his state has left. A pure function of the roster and the hour when the run starts; changed only by the run's seconds.
/// </summary>
public sealed class HGuard
{
    public ulong Person;
    public bool Synthetic;
    /// <summary>Roster order: even is the day shift, odd the night shift; 0 is the keeper who lodges at the site.</summary>
    public int Slot;
    public bool Keeper;
    public int X, Y;
    public int HomeX, HomeY;
    public GState State;
    /// <summary>The next waypoint of the loop and the seconds left of the dwell, the investigation, the downing or the fire watch.</summary>
    public int Route, Timer;
    public int TargetX, TargetY;
    public int Familiarity;
    public int Honesty = 45;
    public List<int> Keys = [];
    public bool KeysTaken;
    public int Searches;
    public long Listened;
    public bool Aware;
    /// <summary>The guard's own noise listened to up to this second (the next listen takes what came after).</summary>
    public bool Awake => State is not (GState.Asleep or GState.Down or GState.Bribed);
}

/// <summary>A key the hero carries: the site, the door and the lock generation it was cut for.</summary>
public readonly record struct KeyRef(ulong Site, int Door, int Generation);

/// <summary>Something that happened in a run, for the barrier to turn into coin, a crime and a chronicle entry (heists 4.5, 4.6). The run itself moves no coin.</summary>
public enum HeistEventKind : byte { Entered, Noise, KeyTaken, DoorOpened, DoorPicked, DoorBroken, Bribed, BribeRefused, Looted, CaptiveFreed, Alarm, Spotted, Subdued, Caught, Left, FireSet, Doused, StoneThrown, Portal }

public readonly record struct HeistEvent(HeistEventKind Kind, long Sec, ulong A, ulong B, long Value);

/// <summary>How a run ended.</summary>
public enum RunOutcome : byte { Active, Left, Caught, Aborted }

/// <summary>The persistent part of a heist site (heists-and-jailbreaks 3): which place and kind, the lock generation, when the locks are to be changed, when the chest was emptied, and the run's own state while someone is in it.</summary>
public sealed class HSite
{
    public ulong Id;
    public int Settlement;
    public HeistKind Kind;
    public string Template = "";
    public int LockGen;
    public int ChangeDay = -1;
    public int ChestDay = -1;
    public int Visits;
    /// <summary>The chronicle entry that explains a lock change (the theft's), 0 when none.</summary>
    public ulong ChangeCause;
    /// <summary>The goods and papers in the chest beyond its purse (rolled once at generation, stream 0x6B2), item ids; taken with the chest.</summary>
    public List<string> Extras = [];
    // ---- run state (cleared when nobody is in)
    /// <summary>Per door: 0 locked or shut, 1 unlocked, 2 open, 3 broken (stays broken).</summary>
    public byte[] Door = [];
    public bool[] SconceOut = [];
    public ulong[] Slot = [];
    public bool[] Freed = [];
    public List<HGuard> Guards = [];
    public bool Alarm, OuterLocked;
    public int FireX = -1, FireY = -1;
    public long FireUntil;
    /// <summary>The noises of the last seconds that guards have yet to listen to: second, x, y, metres, kind (0 ordinary, 1 shout), a source tag.</summary>
    public List<long[]> Noises = [];
    public long NoiseSeq;

    public HeistMap? Map { get; set; }

    public static ulong IdOf(int settlement, HeistKind kind) => DetRng.Mix(((ulong)(uint)settlement << 8) ^ (ulong)kind ^ 0x6B5E_0000UL);
}

/// <summary>A hue and cry after a seen escape (heists-and-jailbreaks 4.5): the fief's guards search a radius growing 1 km an hour from the gaol for a day, road checks looking at passers-by, then give up; the bounty stays.</summary>
public sealed class Manhunt
{
    public int Id;
    public int Settlement;
    public long StartSec;
    public List<ulong> Persons = [];
    public ulong Entry;
    public bool Ended;

    public int RadiusM(HeistRules r, long sec) => (int)Math.Min(r.S.ManhuntHours, Math.Max(0L, (sec - StartSec) / 3600)) * r.S.ManhuntKmPerHour * 1000;
    public bool Active(HeistRules r, long sec) => !Ended && sec < StartSec + r.S.ManhuntHours * 3600L;
}
