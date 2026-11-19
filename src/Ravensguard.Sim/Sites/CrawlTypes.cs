using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Sites;

/// <summary>What the player asks of the crawl (recorded and replayed like any live input). Append only.</summary>
public enum CrawlInputKind : byte
{
    None, Move, Act, Torch, Retrace, Mark, PullLadder, Rest, Burn, Leave, MoveTo, DragOut, Search, Stop, Treat,
}

/// <summary>One input: Move (A = direction 0..7 east, south-east, south... or -1 to stop; B = flags 1 sprint, 2 walk, 4 sneak), MoveTo (A = cell), the rest without arguments.</summary>
public readonly record struct CrawlInput(CrawlInputKind Kind, int A = 0, int B = 0);

/// <summary>What happened in the crawl, for the view (warnings, toasts, the map) and the game (chronicle, defeat). Append only.</summary>
public enum CrawlEventKind : byte
{
    Entered, LevelChanged, Left, DoorOpened, Looted, TabooRobbed,
    AirWarning, AirClear, FlameDied, TorchGuttering, TorchOut, TorchLit, NoLight,
    TrapDetected, TrapTriggered, TrapDisarmed, TrapDisarmFailed, TrapCannotDisarm, Fell, Held, Freed,
    CaveInCreak, CaveIn, Dug, WaterRising, Swimming, Sinking, Drowned, Unconscious, Woke, Died,
    MonsterWoke, Noticed, FightStarted, FightEnded, MonsterSlain, LadderPulled, LadderUp, ClimbedRope, RopeFell,
    RetraceStarted, RetraceDone, RetraceStopped, Marked, RestStarted, RestStopped, CannotRest, Lost, Found, RatsAtFood, Busy, Burning, NothingHere, Blocked, Treated,
}

public readonly record struct CrawlEvent(long Sec, CrawlEventKind Kind, int A = 0, int B = 0, string Text = "");

public enum CrawlMode : byte { Walk, Jog, Sprint }

/// <summary>
/// A person in the crawl (the hero or a party member): place (mm on the site grid, level), facing, carried light and spares, the skills the site asks for, the body for the
/// resolver (plan, worn pieces, the wound book of the shared ledger), what is carried, and the timers of the hazards (bad air, water, a held foot, a busy action).
/// </summary>
public sealed class CrawlMember
{
    public ulong Person;
    public string Name = "";
    public bool Hero;
    public FighterKit Kit = new();
    public BodyPlanV2 Plan = null!;
    public WornV2[] Worn = [];
    public WoundBook Book = null!;
    public int X, Y, Level;
    public int FX = 1024, FY;
    public CarriedLight Light;
    public int SpareTorches, SpareCandles, OilMl;
    public bool Tinder = true;
    public int Perception = 50, Lockpicking = 20, Athletics = 30, Stealth = 30, DigQuality;
    public bool Rope;
    /// <summary>Bandages carried, and whether the member dresses its own bleeding when out of a fight (companions always; the hero when the AI plays him).</summary>
    public int Bandages = 2;
    public bool AutoTreat;
    public int FirstAid = 20;
    public int GearG = 8000, CapacityG = 40000;
    public List<LootItem> Loot = [];
    public int LootG;
    public bool Superstitious;
    public int MoraleX100 = 7000;
    public bool HasMap = true;
    // hazards
    public int FoulS, ClearS, BreathS, HeldS, BusyS, SwimChecks;
    public CrawlInputKind BusyKind;
    public int BusyTarget = -1;
    public bool Unconscious, Dead, Sunk, Swimming, WarnedAir, Lost;
    public string DeathCause = "";
    public int DragBy = -1;
    // movement
    public CrawlMode Mode = CrawlMode.Jog;
    public bool Sneak;
    public int MoveDir = -1;
    public List<int>? Path;
    public int PathIdx;
    public bool Retracing;
    public Breadcrumbs Crumbs = new();
    public int Cell = -1;
    public int TorchesUsed, CandlesUsed;
    public int OilUsedMl;
    public bool Active => !Dead;
    public bool CanAct => !Dead && !Unconscious && HeldS <= 0 && BusyS <= 0;
    public int CarriedKg => (GearG + LootG) / 1000;
}

/// <summary>The crawl as the view and the HUD read it at the end of a step.</summary>
public sealed record CrawlSnapshot(long GameSec, int Level, int HeroX, int HeroY, int LightLeftS, bool Lit, LightKind Light, int Torches, int Air, bool AirWarning, int WaterCm,
    bool Lost, bool InFight, int ExitM, int ExitS, int CarriedKg, int CapacityKg, bool Resting, bool Retracing, int KnownCells, string Busy, int MapRevision);
