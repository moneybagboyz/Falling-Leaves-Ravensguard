using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Sim.Autopilot;

/// <summary>What a directive asks (autopilot spec 3.1). <c>Tend</c> and <c>PursueAmbition</c> are stubs until Phase 5 and 6; they raise <see cref="DirectiveState.Blocked"/>.</summary>
public enum DirectiveKind : byte { Work, TradeRoute, Travel, Train, Guard, Escort, Rest, Tend, PursueAmbition }

/// <summary>When a directive ends.</summary>
public enum UntilKind : byte { Done, Days, CoinAtLeast, SkillAtLeast, Healed, Forever }

public enum DirectiveState : byte { Pending, Active, Done, Failed, Blocked }

/// <summary>What an action does besides what it is (spec 2, "Crime vocabulary"): a crime flag the <c>NoCrimes</c> limit removes. Phase 6 gives the vocabulary content; Phase 3 has a handful of tagged actions and a test action.</summary>
[Flags] public enum ActionTag : byte { None = 0, Crime = 1 }

/// <summary>A step of the plan the runner sets for the brain (spec 4.1).</summary>
public enum StepKind : byte { Idle, GoTo, Work, Trade, Wait, Sleep, Journey }

/// <summary>
/// One entry of the queue (spec 3.1). <see cref="Target"/> is a place or person or recipe or skill id by <see cref="TargetKind"/> (0 none, 1 settlement index, 2 person id, 3 skill, 4 route); <see cref="Params"/> holds up to four numbers
/// (Travel: x and y in region millimetres; TradeRoute: good id, grams per trip, minimum margin permille; Train: target level).
/// </summary>
public sealed class Directive
{
    public DirectiveKind Kind;
    public byte TargetKind;
    public long Target;
    public long[] Params = new long[4];
    public UntilKind Until;
    public long UntilValue;
    /// <summary>Second value of an Until: the skill id of <c>SkillAtLeast</c>.</summary>
    public long UntilValue2;
    public int StartDay = -1;
    public DirectiveState State;
    /// <summary>Coin when the directive started (for CoinAtLeast reports and the summary).</summary>
    public long StartCoin;
    /// <summary>Directive-private counters saved with it (trade route: trips, low margins; travel: journey started).</summary>
    public long Aux0, Aux1, Aux2;
    /// <summary>A trade route's stock of the trade good the hero already held when the trip's buy was ordered (grams): only what is added to it is the cargo.</summary>
    public long Aux3;
    /// <summary>A trade route's coin in and out for the trip in progress (mites).</summary>
    public long Cost, Revenue;
    public string Name = "";

    public static Directive Work(int days = 0, string name = "Work") => new() { Kind = DirectiveKind.Work, Until = days > 0 ? UntilKind.Days : UntilKind.Forever, UntilValue = days, Name = name };
    public static Directive Train(People.Skill skill, int level) => new() { Kind = DirectiveKind.Train, TargetKind = 3, Target = (long)skill, Params = [level, 0, 0, 0], Until = UntilKind.SkillAtLeast, UntilValue = level, UntilValue2 = (long)skill, Name = "Train " + skill };
    public static Directive TravelTo(long xMm, long yMm, string name) => new() { Kind = DirectiveKind.Travel, Params = [xMm, yMm, 0, 0], Until = UntilKind.Done, Name = name };
    public static Directive Rest(int days = 0) => new() { Kind = DirectiveKind.Rest, Until = days > 0 ? UntilKind.Days : UntilKind.Healed, UntilValue = days, Name = "Rest" };
    public static Directive Guard(int settlement, int days = 0) => new() { Kind = DirectiveKind.Guard, TargetKind = 1, Target = settlement, Until = days > 0 ? UntilKind.Days : UntilKind.Forever, UntilValue = days, Name = "Guard" };
    public static Directive Escort(EntityId target) => new() { Kind = DirectiveKind.Escort, TargetKind = 2, Target = (long)target.Value, Until = UntilKind.Forever, Name = "Escort" };
    public static Directive TradeRoute(int a, int b, ushort good, long gramsPerTrip, int minMarginPm) => new() { Kind = DirectiveKind.TradeRoute, TargetKind = 4, Target = a, Params = [b, good, gramsPerTrip, minMarginPm], Until = UntilKind.Forever, Name = "Trade route" };

    public Directive Clone() { var d = (Directive)MemberwiseClone(); d.Params = (long[])Params.Clone(); return d; }
}

/// <summary>What the AI must never do (spec 3.1, 4.3). Money is in mites; positions in region millimetres.</summary>
public sealed class LimitSet
{
    public long SpendCapMitesPerDay = 300;
    public long SpendCapTotalMites = -1;
    public long PurseReserveMites;
    public bool NoCrimes = true;
    public int MaxRiskPm = 500;
    public bool HomeByNightfall;
    public long HomeX, HomeY;
    public bool NoLethalForce = true;
    /// <summary>Bit per <see cref="Jobs.OddJobKind"/> the AI may accept when offered.</summary>
    public uint AcceptContractKinds = 0xFFFF;
    public LimitSet Clone() => (LimitSet)MemberwiseClone();
    public bool Accepts(Jobs.OddJobKind k) => (AcceptContractKinds & (1u << (int)k)) != 0;
}

/// <summary>A day's account of what the autopilot did, for the daily summary and the Journal tab (spec 4.5): generated from the log, never narrated.</summary>
public sealed class DaySummary
{
    public int Day;
    /// <summary>Game seconds by <see cref="ActionKind"/>.</summary>
    public long[] SecByAction = new long[ActionPolicy.Kinds];
    public long Earned, Spent;
    public int JobsDone, Interrupts, Meals;
    public long DistanceM;
    /// <summary>Skill gain in milli-levels (1 decimal shown).</summary>
    public long SkillGainMilli;
    public long NutritionCd;
}

/// <summary>Everything the autopilot keeps for one person (spec 3.1); saved in the "autopilot" section. At most 13 rows (the hero and 12 party members).</summary>
public sealed class AutopilotState
{
    public EntityId Person;
    public bool Enabled, Paused;
    public List<Directive> Queue = [];
    public int ActiveIdx;
    public LimitSet Limits = new();
    /// <summary>Policy per <see cref="InterruptKind"/>.</summary>
    public InterruptPolicy[] Policy = DefaultPolicies();
    public bool[] Quiet = new bool[InterruptBus.KindCount];
    public int LastSummaryDay = -1;
    public int SpentDay = -1;
    public long SpentToday, SpentTotal;
    public long SessionLogStart;
    // ---- progress of the person (saved, so a load resumes the same step)
    public long NextWindow = long.MinValue;
    public ActionKind Intent = ActionKind.Idle;
    public long IntentSince = long.MinValue;
    public StepKind Step;
    public long StepSince, StepUntil;
    public long LastMealSec = long.MinValue;
    public long LastProgressSec;
    public long LastWorkUseSec;
    public long PosX, PosY;
    public int HealthPm = 1000;
    public bool Bleeding;
    public int ThreatPm;
    public long NutritionCd;
    public int NutritionDay = -1;
    public long CoinAtDayStart;
    public int ReplanCount;
    public List<DaySummary> Summaries = [];
    public DaySummary Today = new() { Day = -1 };
    public EntityId CurrentJob;
    /// <summary>The first game second of the current run of the stuck check, and the interrupts raised since each kind last fired (dedupe), by kind.</summary>
    public long[] LastFiredSec = Enumerable.Repeat(long.MinValue, InterruptBus.KindCount).ToArray();
    /// <summary>Interrupt kinds whose condition is on now (edge-triggered), bit per kind.</summary>
    public uint EdgeBits;
    /// <summary>Hunger of the last window, to see a meal time pass.</summary>
    public int LastHungerMilli;
    /// <summary>The first game second of the open stay at the camp or the bed (Sleep/Rest bookkeeping).</summary>
    public long CampUntil;
    /// <summary>The last game second the person went shopping for food (one trip an hour at most), and the last second a daily check ran.</summary>
    public long LastShopSec = long.MinValue;
    /// <summary>The day the LimitReached for "no job fits" was last raised (once a day).</summary>
    public int LimitDay = -1;

    public Directive? Active => ActiveIdx >= 0 && ActiveIdx < Queue.Count ? Queue[ActiveIdx] : null;

    public static InterruptPolicy[] DefaultPolicies() { var p = new InterruptPolicy[InterruptBus.KindCount]; for (int i = 0; i < p.Length; i++) p[i] = InterruptBus.DefaultPolicy((InterruptKind)i); return p; }

    /// <summary>The "Watch the world" preset (spec 4.5): everything Notify or Ai except Attacked and Injured, which stay Pause unless the player changes them.</summary>
    public void PresetWatch()
    {
        for (int i = 0; i < Policy.Length; i++)
        {
            var k = (InterruptKind)i;
            Policy[i] = k is InterruptKind.Attacked or InterruptKind.Injured or InterruptKind.BigChoice ? InterruptPolicy.Pause : InterruptPolicy.Notify;
            Quiet[i] = false;
        }
    }

    public void ResetPolicy() { Policy = DefaultPolicies(); Quiet = new bool[InterruptBus.KindCount]; }
}
