using Ravensguard.Content;
using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Autopilot;

/// <summary>
/// The autopilot at work (autopilot spec 4): a thin layer that feeds the same <see cref="UtilityBrain"/> (through <see cref="PersonDecision"/>, the function the live NPCs use), the same job board, the same travel code and
/// the same hero commands that run everything else, with data (a directive, limits, a policy) in place of keyboard input. There is no second AI. It runs on the tick worker at a barrier (<see cref="Advance"/>), in 20-game-second
/// windows keyed on the game second, so it gives the same answers at 1x and 8x and across a save. Everything it does to the world goes through <see cref="HeroDesk"/> commands (tagged <c>Origin = Autopilot</c> and checked by
/// <see cref="LimitGuard"/> at the barrier) or <see cref="ClaimRequest"/>s, so a recorded session replays.
/// </summary>
public sealed partial class AutopilotWorld
{
    public const int WindowSec = 20;
    /// <summary>The jog in millimetres per game second (540 m an hour): the pace of a person on the move.</summary>
    public const int JogMmPerGs = 150;

    public AutopilotBook Book { get; }
    public AutopilotContent Cfg { get; }
    public WorldState State { get; }
    public HeroContext Ctx { get; }
    public HeroDesk Desk { get; }
    public UtilityBrain Brain { get; }
    public EntityId HeroId => Ctx.Party.Hero;
    public AutopilotSettingsDef Settings => Cfg.Settings;

    // ---- wiring (the game and the tests set what they have; the defaults make a self-contained world)
    /// <summary>The job table and environment (the tick worker's); null: no jobs.</summary>
    public Func<(JobEnv Env, JobTable Table)?>? Jobs { get; set; }
    /// <summary>Open jobs at the boards and the job each person holds, from the table (default) or from a snapshot.</summary>
    public Func<IEnumerable<Job>>? OpenJobsView { get; set; }
    public Func<EntityId, Job?>? JobOfView { get; set; }
    /// <summary>Claims waiting for the barrier (the game's <c>RuntimeJobs.StepAsync</c> takes them; a test hands them to <c>ClaimAll</c>).</summary>
    public List<ClaimRequest> Claims { get; } = [];
    /// <summary>The live tier's position of a person (the game), in region millimetres; null: the autopilot's own.</summary>
    public Func<EntityId, (long X, long Y)?>? PositionHook { get; set; }
    /// <summary>Where the autopilot wants the person to walk (the game moves the hero there); the last wish per person.</summary>
    public SortedDictionary<EntityId, (long X, long Y)> Wanted { get; } = [];
    /// <summary>Region danger 0..1000 at a point and game second (default: the quiet region of a test).</summary>
    public Func<long, long, long, int> DangerAt { get; set; } = (_, _, _) => 100;
    public Func<TravelState?>? TravelOf { get; set; }
    public ITravelWorld? TravelWorld { get; set; }
    /// <summary>Plans a road route between two points (region millimetres); null: a straight line.</summary>
    public Func<long, long, long, long, RoutePath?>? Planner { get; set; }
    /// <summary>Actions beyond the brain's content (a test's crime, later phases' tagged actions): the highest-scoring allowed one beats the brain's choice when it scores higher.</summary>
    public List<TaggedAction> Extras { get; } = [];
    /// <summary>Called when a chronicle entry should be written (set by the host; default writes to <see cref="State"/>.Chronicle).</summary>
    public Action<EntityId, string, long, IReadOnlyDictionary<string, string>>? ChronicleHook { get; set; }
    /// <summary>A pending-spend ledger: command sequence number to (person, day, mites reserved).</summary>
    private SortedDictionary<long, (EntityId Person, int Day, long Mites)> _reserved => Book.Reserved;
    private readonly Dictionary<EntityId, (int Day, PersonAnchors A)> _anchors = [];
    private readonly Dictionary<EntityId, int> _busSeen = [];

    public AutopilotWorld(WorldState state, HeroContext ctx, HeroDesk desk, AutopilotBook book, AutopilotContent? cfg = null)
    {
        State = state; Ctx = ctx; Desk = desk; Book = book; Cfg = cfg ?? AutopilotContent.From(ctx.Content);
        Brain = new UtilityBrain(BehaviourContent.From(ctx.Content).Utility);
        Book.Log.Cap = Cfg.Settings.LogLinesMax;
        Book.Router.Chronicle = WriteChronicle;
        Desk.Gate = Gate; Desk.Settled = Settled;
        Jobs ??= ctx.Jobs;
    }

    private void WriteChronicle(EntityId person, string type, long sec, IReadOnlyDictionary<string, string> data)
    {
        if (ChronicleHook != null) { ChronicleHook(person, type, sec, data); return; }
        State.Chronicle.Add(new ChronicleEntry(State.Ids.Allocate(IdKind.Event), new GameTime(sec), type, [person], [], data));
    }

    // ------------------------------------------------------------------ turning it on and off

    /// <summary>Turns autopilot on for a person at game second <paramref name="sec"/> (a new row gets the default limits of open question 1) and logs and chronicles the start.</summary>
    public AutopilotState Enable(EntityId person, long sec, params Directive[] queue)
    {
        var st = Book.GetOrAdd(person, Cfg);
        st.Enabled = true; st.Paused = false;
        if (queue.Length > 0) { st.Queue = [.. queue.Select(d => d.Clone())]; st.ActiveIdx = 0; foreach (var d in st.Queue) { d.State = DirectiveState.Pending; d.StartDay = -1; } }
        st.NextWindow = sec / WindowSec; st.Today = new DaySummary { Day = -1 };
        st.SessionLogStart = Book.Log.Lines.Count;
        var pos = PositionOf(st); st.PosX = pos.X; st.PosY = pos.Y;
        Book.Log.Add(sec, person, LogCode.Start, "Autopilot on" + (st.Active != null ? ": " + st.Active.Name : ""));
        WriteChronicle(person, "autopilot_start", sec, new Dictionary<string, string> { ["autopilot"] = "1", ["directive"] = st.Active?.Name ?? "live like a neighbour" });
        return st;
    }

    public void Disable(EntityId person, long sec)
    {
        var st = Book.Get(person); if (st == null || !st.Enabled) return;
        st.Enabled = false; st.Paused = false;
        Book.Log.Add(sec, person, LogCode.Stop, "Autopilot off");
        WriteChronicle(person, "autopilot_stop", sec, new Dictionary<string, string> { ["autopilot"] = "1", ["directive"] = st.Active?.Name ?? "" });
    }

    /// <summary>The player took control (any movement key, a click on the ground, opening the trade or dialogue screen): the person is the player's again until <see cref="Resume"/>. Instant; the directive and the progress are kept.</summary>
    public void TakeControl(EntityId person, long sec)
    {
        var st = Book.Get(person); if (st == null || !st.Enabled || st.Paused) return;
        st.Paused = true; Wanted.Remove(person);
        Book.Log.Add(sec, person, LogCode.Note, "Autopilot paused: the player took control");
    }

    /// <summary>The player hands the person back: the directive continues from here (no windows are made up for the time away).</summary>
    public void Resume(EntityId person, long sec)
    {
        var st = Book.Get(person); if (st == null || !st.Enabled) return;
        st.Paused = false; st.NextWindow = sec / WindowSec;
        var pos = PositionOf(st); st.PosX = pos.X; st.PosY = pos.Y;
        BusOf(st).Resume();
        Book.Log.Add(sec, person, LogCode.Note, "Autopilot resumed");
    }

    // ------------------------------------------------------------------ the driver

    public InterruptBus BusOf(AutopilotState st) => Book.BusOf(st.Person, Travel?.Bus, HeroId);
    /// <summary>The travel state the autopilot drives for a Travel directive: <see cref="TravelOf"/> when set (the game returns null: its main thread owns the journey), else the world's.</summary>
    private TravelState? Travel => TravelOf != null ? TravelOf() : State.Travel;

    /// <summary>Runs every enabled, unpaused person's windows up to game second <paramref name="toSec"/>. Window <c>w</c> covers game seconds <c>[20w, 20w+20)</c> and is acted on at its first second.</summary>
    public void Advance(long toSec)
    {
        long last = toSec / WindowSec;
        var states = Book.States.Values.Where(s => s.Enabled && !s.Paused).ToArray();
        if (states.Length == 0) return;
        foreach (var st in states) if (st.NextWindow == long.MinValue) st.NextWindow = last;
        long first = states.Min(s => s.NextWindow);
        for (long w = first; w <= last; w++)
        {
            if (DrivesJobs && w * WindowSec > _jobsClock) { StepJobs(w * WindowSec); _jobsClock = w * WindowSec; }
            foreach (var st in states)
            {
                if (st.NextWindow > w || !st.Enabled || st.Paused) continue;
                var bus = BusOf(st);
                if (bus.Paused) { st.NextWindow = last + 1; continue; }          // the game is paused on an alert; no time passes, none is made up
                Step(st, w * WindowSec);
                st.NextWindow = w + 1;
            }
        }
    }

    private (long X, long Y) PositionOf(AutopilotState st)
    {
        if (PositionHook?.Invoke(st.Person) is { } p) return p;
        if (st.PosX == 0 && st.PosY == 0)
        {
            var a = AnchorsOf(st, State.Clock.Now.Seconds);
            return (a.SquareX, a.SquareY);
        }
        return (st.PosX, st.PosY);
    }

    public PersonAnchors AnchorsOf(AutopilotState st, long at)
    {
        int day = (int)(at / 86400);
        if (_anchors.TryGetValue(st.Person, out var c) && c.Day == day) return c.A;
        var pop = State.Population; int idx;
        if (st.Person == HeroId)
        {
            if (Book.StandIn < 0 || Book.StandIn >= pop.People.Count) Book.StandIn = PickStandIn(pop, day, PositionHook?.Invoke(st.Person) is { } here ? NearestSettlement(st, long.MaxValue, here) : -1);
            idx = Book.StandIn;
        }
        else idx = Ctx.Party.Squad.Find(st.Person)?.PersonIndex ?? pop.People.IndexOf(st.Person);
        var a = PersonAnchors.From(pop, Math.Max(0, idx), State.Seed, State.HomeRegion.Id, at);
        if (st.Person == HeroId) a = a with { Id = HeroId };
        _anchors[st.Person] = (day, a);
        return a;
    }

    /// <summary>The row the hero's days are modelled on: the first adult labourer with a home in the settlement the hero stands nearest to (ascending index), so the same on every run; any settlement's, then anyone, when there is none.</summary>
    private static int PickStandIn(Population pop, int day, int settlement)
    {
        var p = pop.People;
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < p.Count; i++)
                if (p.IsAlive(i) && p.Occupation[i] == Occupation.Labourer && p.HomeSettlement[i] >= 0 && (pass == 1 || settlement < 0 || p.HomeSettlement[i] == settlement) && p.AgeYears(i, day) is >= 20 and < 45 && p.Household[i] >= 0) return i;
        for (int i = 0; i < p.Count; i++) if (p.IsAlive(i) && p.AgeYears(i, day) >= 18) return i;
        return 0;
    }

    private long CoinOf(AutopilotState st)
    {
        if (st.Person == HeroId) return Ctx.Coin;
        var e = Ctx.Econ;
        var f = Ctx.Party.Squad.Find(st.Person);
        int hh = f != null ? State.Population.People.Household[Math.Max(0, f.PersonIndex)] : -1;
        if (hh >= 0 && hh < e.HouseholdCount) return e.Ledger.Balance(e.HouseholdPurse[hh]).Mites;
        return e.Ledger.TryFind(Ravensguard.Sim.Economy.PurseKind.Person, st.Person, out var p) ? e.Ledger.Balance(p).Mites : 0;
    }

    private void Step(AutopilotState st, long at)
    {
        int day = (int)(at / 86400);
        if (st.Today.Day != day) RollDay(st, at, day);
        MaybeSummary(st, at);
        var a = AnchorsOf(st, at);
        var pos = PositionOf(st); st.PosX = pos.X; st.PosY = pos.Y;
        long coin = CoinOf(st);
        TrackJob(st, at);
        var d = EnsureActive(st, at, coin);
        RaiseStateInterrupts(st, at, coin);
        if (!st.Enabled || st.Paused || BusOf(st).Paused) return;
        d = st.Active;

        var pol = BuildPolicy(st, d, at, coin, day);
        bool boardHere = BoardHere(a, d);
        var job = JobOfPerson(st.Person);
        ActionKind kind; DecisionResult dr = default;
        if (job != null && st.ThreatPm <= 0) kind = ActionKind.JobStep;
        else
        {
            bool forceBoard = d is { Kind: DirectiveKind.Work or DirectiveKind.Guard } || (d is { Kind: DirectiveKind.Train } && !TrainingByPractice(st, d, at));
            dr = PersonDecision.Decide(Brain, a, at, st.Intent, st.IntentSince, boardHere, 0, 0, 0, false, false, pol, forceBoard, st.ThreatPm);
            kind = dr.Choice.Kind;
            if (d != null && MovesOn(d.Kind) && kind is not (ActionKind.Eat or ActionKind.Sleep or ActionKind.Flee or ActionKind.Shelter)) kind = ActionKind.Travel;
            // an action beyond the brain's content (a crime in a test): the best allowed one that outscores the choice wins
            foreach (var x in Extras)
                if (LimitGuard.Allows(st.Limits, x.Tags) && x.Score > dr.Choice.Score && x.Score > 0) { DoExtra(st, x, at); return; }
        }
        if (kind != st.Intent) { st.Intent = kind; st.IntentSince = at; }
        OnAction?.Invoke(st.Person, at, kind);
        Chores(st, a, at, coin, dr);
        Execute(st, d, a, kind, at, dr);
        if (kind is ActionKind.Sleep or ActionKind.Rest) st.LastProgressSec = at;
        st.Today.SecByAction[(int)kind] += WindowSec;
        CheckStuck(st, d, at);
    }

    private static bool MovesOn(DirectiveKind k) => k is DirectiveKind.Travel or DirectiveKind.TradeRoute or DirectiveKind.Escort;

    // ------------------------------------------------------------------ the day

    private void RollDay(AutopilotState st, long at, int day)
    {
        if (st.Today.Day >= 0)
        {
            st.Today.NutritionCd = st.NutritionDay == st.Today.Day ? st.NutritionCd : 0;
            st.Summaries.Add(st.Today); if (st.Summaries.Count > 30) st.Summaries.RemoveAt(0);
        }
        st.Today = new DaySummary { Day = day };
        if (st.NutritionDay != day) { st.NutritionDay = day; st.NutritionCd = 0; }
        st.CoinAtDayStart = CoinOf(st);
        LimitGuard.Commit(st, day, 0);              // rolls the day's account (the allowance does not carry over)
    }

    private void MaybeSummary(AutopilotState st, long at)
    {
        int day = (int)(at / 86400), hour = (int)(at % 86400 / 3600);
        if (hour < Settings.SummaryHour || st.LastSummaryDay >= day || st.Summaries.Count == 0) return;
        st.LastSummaryDay = day;
        var y = st.Summaries[^1];
        long secWork = y.SecByAction[(int)ActionKind.Work] + y.SecByAction[(int)ActionKind.JobStep] + y.SecByAction[(int)ActionKind.TakeJob];
        Book.Log.Add(at, st.Person, LogCode.Summary,
            $"Yesterday: {secWork / 3600.0:0.#} h working, {y.SecByAction[(int)ActionKind.Sleep] / 3600.0:0.#} h asleep, {y.JobsDone} jobs, earned {Quotes.Money(y.Earned)}, spent {Quotes.Money(y.Spent)}, {y.DistanceM} m walked, {y.Interrupts} interrupts, skill +{y.SkillGainMilli / 1000.0:0.0}.", (byte)(y.Day & 0xFF));
        Book.SummariesMade++;
    }
}
