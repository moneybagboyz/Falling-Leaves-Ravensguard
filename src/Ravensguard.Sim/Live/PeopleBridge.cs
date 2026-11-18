using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Clothing;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Live;

/// <summary>What the bridge reads of one resident region: its people (<see cref="Population"/>), where its frame sits on the continent and the seed its schedule draws use.</summary>
public sealed record PeopleRecords(int RegionId, long OriginXM, long OriginYM, Population Pop, ulong Seed, AbstractJobTable Jobs, LiveFacts? Facts = null, ScheduleBook? Book = null);

/// <summary>
/// One live person's link to the record (abstract spec 2.1, plan 2.3): the person's id and row, the region and the bubble they are in. The components (position, plan,
/// action) live in the <see cref="LiveActor"/> cache next to it; the record itself is never copied, and a person has at most one binding.
/// </summary>
public readonly record struct LiveBinding(EntityId Person, int Region, int Index, int Bubble, long PromotedSec);

/// <summary>The component cache of one promoted person: where they stand (continent millimetres), what they do, and the plan they follow. Owned by the main thread.</summary>
public sealed class LiveActor
{
    public LiveBinding Binding;
    public PersonAnchors A;
    public long OriginXMm, OriginYMm;
    public long BlockKey = long.MinValue;
    public PathPlan? Plan;
    public bool PlanOnGround;
    /// <summary>The plan of the next block, made ahead of time (a few seconds before it starts) so a tick never has to make many at once; the same plan as one made at the start, because a plan is a function of its block.</summary>
    public long NextKey = long.MinValue;
    public PathPlan? NextPlan;
    public long X, Y, PrevX, PrevY;
    public int HeadX, HeadY;
    public JobKind Action;
    public long OutsideSince = -1;
    public long WorkSecs, LastSec;
    /// <summary>Level (0 ground, 1 the floor above) and whether the person is walking now.</summary>
    public int Z;
    public bool Walking;
    /// <summary>What the utility scorer chose at the last 20-game-second window, and when it began.</summary>
    public ActionKind Intent = ActionKind.Idle;
    public long IntentSince = long.MinValue, DecisionWindow = long.MinValue;
    public string Why = "";
    /// <summary>The odd job the person is on (an immutable copy of the job at its claim) and the legs of it already planned over the ground.</summary>
    public Job? JobSnap;
    public long JobKey = long.MinValue;
    public PathPlan? JobPlan;
    public int JobPhase = -1;
    public int JobStep;
    /// <summary>The person's clothes and body (clothing spec), when the bridge has a book; and the last minute's cold, wet and heat stress that the scorer reads (permille).</summary>
    public Tracked? Body;
    public int ColdPm, WetPm, HeatPm;
    public bool Indoors;
}

/// <summary>What the street view draws of one promoted person this tick.</summary>
public readonly record struct LivePerson(EntityId Id, string Name, Occupation Occupation, Sex Sex, SocialClass Class, JobKind Action, long XMm, long YMm, long PrevXMm, long PrevYMm, int HeadX, int HeadY, int Bubble, int Z = 0, bool Walking = false, ActionKind Intent = ActionKind.Idle, int JobStep = -1, int LieAxis = 0, Tracked? Body = null, ExposureState State = ExposureState.Normal);

/// <summary>How a live person's body is doing, for the view (a shiver, a wet hem) and the HUD.</summary>
public enum ExposureState : byte { Normal, Chilled, Cold, Severe, Hot }

/// <summary>A person the scan found inside a bubble that is not live yet: the anchors copied out of the records, and the squared distance to the nearest centre (mm).</summary>
public readonly record struct PromotionCandidate(PersonAnchors Anchors, long OriginXMm, long OriginYMm, long D2Mm, int Bubble);

/// <summary>Who is live now and where the bubbles are: what a scan needs from the main thread (an immutable copy, so the scan can run on the tick worker at a barrier).</summary>
public sealed record ScanInput(long NowSec, (long X, long Y)[] Centres, (EntityId Id, int Region, int Index)[] Live);

/// <summary>The scan's answer: people to promote, nearest first (ties ascending region then person), and live people whose record is gone (died, left).</summary>
public sealed record ScanResult(long NowSec, IReadOnlyList<PromotionCandidate> Candidates, IReadOnlyList<EntityId> Gone);

/// <summary>A person given back to the records. <see cref="JobId"/> and <see cref="JobStep"/> name the odd job they were on, which lives on in the job table (its phases are a pure function of the claim).</summary>
public readonly record struct FoldBack(EntityId Person, int Region, int Index, AbstractJob Job, long XMm, long YMm, bool Displaced, long LiveWorkSec, EntityId JobId = default, int JobStep = -1);

/// <summary>
/// The LODBridge for people (plan 2.3, abstract spec 3.5 and 3.6, live-behaviour spec P3-A): promotes the people of the abstract records into live entities inside the
/// bubbles around the hero (and squads), steps them at 20 Hz, and gives them back with their job re-derived.
/// <para>
/// <b>Threads.</b> Records belong to the tick worker. <see cref="Scan"/> reads them and so runs there, at a barrier (<c>WorldRuntime.AtBarrier</c>); it takes an immutable
/// <see cref="ScanInput"/> and returns an immutable <see cref="ScanResult"/>. Everything else runs on the main thread and touches only the bridge's own caches;
/// <see cref="ApplyFolds"/> writes the fold-backs at the next barrier.
/// </para>
/// <para>
/// <b>Determinism.</b> A person's position is a pure function of their anchors and the game second (<see cref="Schedule.Resolve"/>, <see cref="PathPlan.PositionAt"/>); the
/// plan of a block is built once from the block's own start and end, so a run at 1x and at 8x puts the same person at the same place at the same game second. Order is
/// always ascending id; the only RNG is the counter RNG keyed on the game day.
/// </para>
/// </summary>
public sealed class PeopleBridge
{
    private readonly SortedList<EntityId, LiveActor> _live = [];
    private List<PromotionCandidate> _queue = [];
    private readonly List<FoldBack> _folds = [];
    /// <summary>A person further than this from where their schedule puts them has been displaced (fled, pushed): the fold-back makes them travel to the block's place.</summary>
    public const long DisplacedMm = 30_000;
    /// <summary>Game seconds before a block starts at which its plan is made, and how many plans one step may make ahead (the rest wait for the next step).</summary>
    public const long PrefetchSec = 120;
    public const int PrefetchPerStep = 3;
    private int _prefetchLeft;
    private LivePerson[] _snapshot = [];
    private bool _dirty = true;

    // ---- clothing and exposure (docs/specs/phase3/clothing-warmth-exposure.md) ----
    /// <summary>The clothes and bodies of the live people; null until <see cref="AttachClothing"/> (a bridge in a test or a headless run without weather).</summary>
    public ClothingBook? Clothing { get; private set; }
    private Func<long, long, long, ExposureEnv>? _ambient;
    private readonly List<(EntityId Person, ExposureDeath Kind)> _exposureDeaths = [];

    /// <summary>
    /// Gives the live people clothes and a body. <paramref name="ambient"/> answers the open-air weather at (game second, continent mm x, y) as an <see cref="ExposureEnv"/>; it is asked once a game minute per
    /// person, at the minute's own second and at the person's home, so the answer never depends on the game speed.
    /// </summary>
    public void AttachClothing(ClothingBook book, Func<long, long, long, ExposureEnv> ambient) { Clothing = book; _ambient = ambient; }

    /// <summary>People who died of cold or heat since the last call (the caller applies the death at a barrier: <c>DeathCause.Cold</c>, chronicle entry); clears the list.</summary>
    public IReadOnlyList<(EntityId Person, ExposureDeath Kind)> TakeExposureDeaths() { if (_exposureDeaths.Count == 0) return []; var r = _exposureDeaths.ToArray(); _exposureDeaths.Clear(); return r; }

    private static ExposureState StateOfBody(BodyState s) => s switch { BodyState.Normal => ExposureState.Normal, BodyState.Chilled => ExposureState.Chilled, BodyState.Moderate => ExposureState.Cold, BodyState.Severe or BodyState.Lethal => ExposureState.Severe, _ => ExposureState.Hot };

    public int Promotions { get; private set; }
    public int Demotions { get; private set; }
    /// <summary>Most promotions made by one <see cref="Promote"/> call so far.</summary>
    public int MaxPromotedAtOnce { get; private set; }
    public int Count => _live.Count;
    /// <summary>Plans made so far, and during the last <see cref="Step"/> (a diagnostic: a tick that starts many walks is a slow tick).</summary>
    public int PlansBuilt { get; private set; }
    public int PlansLastStep { get; private set; }
    /// <summary>The slowest single plan so far, milliseconds, and what it was.</summary>
    public double MaxPlanMs { get; private set; }
    public string WorstPlan { get; private set; } = "";
    public int Queued => _queue.Count;
    public bool IsLive(EntityId person) => _live.ContainsKey(person);
    public IReadOnlyList<LiveBinding> Bindings => [.. _live.Values.Select(a => a.Binding)];
    public LiveActor? Actor(EntityId person) => _live.GetValueOrDefault(person);

    /// <summary>The people being drawn, ascending id. Rebuilt when something moved.</summary>
    public IReadOnlyList<LivePerson> People
    {
        get
        {
            if (_dirty)
            {
                _snapshot = [.. _live.Values.Select(a => new LivePerson(a.Binding.Person, a.A.Name, a.A.Occupation, a.A.Sex, a.A.Class, a.Action, a.X, a.Y, a.PrevX, a.PrevY, a.HeadX, a.HeadY, a.Binding.Bubble, a.Z, a.Walking, a.Intent, a.JobSnap != null ? a.JobStep : -1, a.A.BedAxis, a.Body, a.Body == null ? ExposureState.Normal : StateOfBody(a.Body.State)))];
                _dirty = false;
            }
            return _snapshot;
        }
    }

    // ------------------------------------------------------------------ the scan (tick worker, at a barrier)

    /// <summary>What the next <see cref="Scan"/> needs from this bridge: the bubble centres (continent millimetres, at most 3) and who is already live.</summary>
    public ScanInput MakeScanInput(IReadOnlyList<(long X, long Y)> centres, long nowSec)
        => new(nowSec, [.. centres.Take(Bubble.MaxBubbles)], [.. _live.Values.Select(a => (a.Binding.Person, a.Binding.Region, a.Binding.Index))]);

    /// <summary>
    /// Finds the people of <paramref name="regions"/> (ascending region id) whose schedule puts them inside 150 m of a centre at <see cref="ScanInput.NowSec"/> and who are not live
    /// yet, nearest first. Settlements are filtered first by their circle (plan radius plus the longest commute plus the bubble), so only the people of nearby places are evaluated.
    /// Pure: it reads the records and nothing of the bridge.
    /// </summary>
    public static ScanResult Scan(IReadOnlyList<PeopleRecords> regions, ScanInput input)
    {
        var cands = new List<PromotionCandidate>();
        var gone = new List<EntityId>();
        var liveSet = new HashSet<EntityId>(input.Live.Select(l => l.Id));
        var centres = input.Centres;
        var tileCache = new Dictionary<int, (int, int)[]?>();
        foreach (var rec in regions.OrderBy(r => r.RegionId))
        {
            var pop = rec.Pop; var p = pop.People; var st = pop.Region.Settlements;
            long ox = rec.OriginXM * 1000, oy = rec.OriginYM * 1000;
            var near = new bool[st.Count];
            for (int s = 0; s < st.Count; s++)
            {
                long reach = (st[s].Plan.RadiusM + 1500L + Bubble.PromoteM) * 1000;
                for (int c = 0; c < centres.Length && !near[s]; c++) near[s] = Bubble.Sq(ox + st[s].CenterX * 1000L - centres[c].X) + Bubble.Sq(oy + st[s].CenterY * 1000L - centres[c].Y) <= Bubble.Sq(reach);
            }
            foreach (var l in input.Live) if (l.Region == rec.RegionId && (l.Index >= p.Count || p.Status[l.Index] != PersonStatus.Alive || p.IdOf(l.Index) != l.Id)) gone.Add(l.Id);
            for (int i = 0; i < p.Count; i++)
            {
                if (p.Status[i] != PersonStatus.Alive) continue;
                int hs = p.HomeSettlement[i], ws = p.WorkSettlement[i];
                if (!((hs >= 0 && hs < near.Length && near[hs]) || (ws >= 0 && ws < near.Length && near[ws]))) continue;
                var id = p.IdOf(i);
                if (liveSet.Contains(id)) continue;
                var a = PersonAnchors.From(pop, i, rec.Seed, rec.RegionId, input.NowSec, tileCache, rec.Facts, rec.Book);
                var r = Schedule.Resolve(a, input.NowSec);
                var (d2, b) = Bubble.Nearest(centres, ox + r.XMm, oy + r.YMm);
                if (b < 0 || !Bubble.InsidePromote(d2)) continue;
                cands.Add(new PromotionCandidate(a with { Name = p.FullName(i) }, ox, oy, d2, b));
            }
        }
        cands.Sort((x, y) => x.D2Mm != y.D2Mm ? x.D2Mm.CompareTo(y.D2Mm) : x.Anchors.Region != y.Anchors.Region ? x.Anchors.Region.CompareTo(y.Anchors.Region) : x.Anchors.Index.CompareTo(y.Anchors.Index));
        return new ScanResult(input.NowSec, cands, gone);
    }

    // ------------------------------------------------------------------ main thread

    /// <summary>Takes a scan's answer: the promotion queue is replaced by it (people who walked away since the last scan drop out), and live people whose record is gone are removed without a fold-back.</summary>
    public void Enqueue(ScanResult r)
    {
        _queue = [.. r.Candidates.Where(c => !_live.ContainsKey(c.Anchors.Id))];
        foreach (var id in r.Gone) if (_live.Remove(id)) { Demotions++; _dirty = true; }
    }

    /// <summary>
    /// Promotes up to <paramref name="max"/> queued people, nearest first (abstract spec 3.5: at most 50 a frame, so a crowd arriving together walks in over a few frames).
    /// Each is placed where the schedule puts them at <paramref name="nowSec"/>, with the action of their job. Returns how many.
    /// </summary>
    public int Promote(long nowSec, ILiveGround? ground, int max = Bubble.MaxPromotionsPerFrame)
    {
        int n = 0;
        while (n < max && _queue.Count > 0)
        {
            var c = _queue[0]; _queue.RemoveAt(0);
            if (_live.ContainsKey(c.Anchors.Id)) continue;
            var actor = new LiveActor { A = c.Anchors, OriginXMm = c.OriginXMm, OriginYMm = c.OriginYMm, Binding = new LiveBinding(c.Anchors.Id, c.Anchors.Region, c.Anchors.Index, c.Bubble, nowSec) };
            _live.Add(c.Anchors.Id, actor);
            if (Clothing != null) actor.Body = Clothing.Track(c.Anchors.Id, c.Anchors.Sex, c.Anchors.Class, c.Anchors.Occupation, 50, c.Anchors.Seed, nowSec);
            Advance(actor, ground, nowSec);
            actor.PrevX = actor.X; actor.PrevY = actor.Y;
            n++;
        }
        if (n > 0) { Promotions += n; MaxPromotedAtOnce = Math.Max(MaxPromotedAtOnce, n); _dirty = true; }
        return n;
    }

    /// <summary>
    /// The 20 Hz live step: every live person (ascending id) is moved to where their plan puts them at <paramref name="nowSec"/>, a plan is made when a block starts, and people who
    /// have stayed beyond 200 m of every bubble for 5 real seconds are demoted into <see cref="TakeFolds"/>. <paramref name="ground"/> is the loaded window of the street view (null:
    /// everyone walks straight); its cells are metres of the continent frame.
    /// </summary>
    public void Step(long nowSec, ILiveGround? ground, IReadOnlyList<(long X, long Y)> centres)
    {
        if (_live.Count == 0) return;
        List<EntityId>? out_ = null;
        var cs = centres.Take(Bubble.MaxBubbles).ToArray(); int plans0 = PlansBuilt; _prefetchLeft = PrefetchPerStep;
        foreach (var (id, a) in _live)
        {
            a.PrevX = a.X; a.PrevY = a.Y;
            if (a.Body != null && _ambient != null) Expose(a, nowSec);
            Advance(a, ground, nowSec);
            if (cs.Length == 0) continue;
            var (d2, b) = Bubble.Nearest(cs, a.X, a.Y);
            a.Binding = a.Binding with { Bubble = b };
            if (Bubble.ShouldDemote(d2, nowSec, ref a.OutsideSince)) (out_ ??= []).Add(id);
        }
        PlansLastStep = PlansBuilt - plans0;
        if (out_ != null) foreach (var id in out_) Demote(_live[id], nowSec);
        _dirty = true;
    }

    /// <summary>Gives every live person back (leaving the street view, loading another world): same fold-back as a demotion.</summary>
    public void DemoteAll(long nowSec)
    {
        foreach (var a in _live.Values.ToList()) Demote(a, nowSec);
        _queue = []; _dirty = true;
    }

    private void Demote(LiveActor a, long nowSec)
    {
        var r = Schedule.Resolve(a.A, nowSec);
        int ppm = r.Job.ProgressPpm; var job = r.Job; bool displaced = false;
        if (a.JobSnap is { } js)
        {
            // an odd job goes on in the job table (its legs are a pure function of the claim): the person is folded back as a worker of it
            var (_, _, phase, _) = JobTable.PositionAt(js, nowSec);
            long end = JobTable.EndSec(js);
            job = new AbstractJob(js.Kind is OddJobKind.Haul or OddJobKind.Deliver or OddJobKind.Fetch ? JobKind.Haul : js.Kind == OddJobKind.Guard ? JobKind.GuardCaravan : JobKind.Work,
                new JobPlace(js.Settlement, -1, (int)(a.X / 1000), (int)(a.Y / 1000)), js.ClaimedSec, end, (int)Math.Clamp((nowSec - js.ClaimedSec) * 1_000_000 / Math.Max(1, end - js.ClaimedSec), 0, 1_000_000));
            _folds.Add(new FoldBack(a.Binding.Person, a.Binding.Region, a.Binding.Index, job, a.X, a.Y, false, a.WorkSecs, js.Id, phase));
        }
        else
        {
            if (r.Seg.Walks && a.Plan != null) ppm = a.Plan.ProgressPpmAt(nowSec);
            // the person is where the plan puts them, so they continue the block; one who was moved far from it (fled, pushed) is travelling to the place of the block
            displaced = PathPlan.Dist(a.X - (a.OriginXMm + r.XMm), a.Y - (a.OriginYMm + r.YMm)) > DisplacedMm && a.Z == r.Z;
            job = displaced ? new AbstractJob(JobKind.Travel, job.Place, nowSec, job.EndSec, 0) : job with { ProgressPpm = ppm };
            _folds.Add(new FoldBack(a.Binding.Person, a.Binding.Region, a.Binding.Index, job, a.X, a.Y, displaced, a.WorkSecs));
        }
        _live.Remove(a.Binding.Person);
        Demotions++; _dirty = true;
    }

    /// <summary>Fold-backs waiting to be applied at the next barrier (<see cref="ApplyFolds"/>); clears the list.</summary>
    public IReadOnlyList<FoldBack> TakeFolds()
    {
        if (_folds.Count == 0) return [];
        var r = _folds.ToArray(); _folds.Clear(); return r;
    }

    /// <summary>
    /// Tick worker, at a barrier: writes each fold-back into its region's <see cref="AbstractJobTable"/>. Only the job is written: the records of coin and goods are the truth while a
    /// person is live (a purchase is a command, a carried good is a stock row), so there is nothing to copy back, which is what keeps people, goods and coin exact (G-A4, L-13).
    /// A fold-back whose person is gone (died while live) or whose row no longer maps to that id is dropped. Returns how many were applied.
    /// </summary>
    public static int ApplyFolds(IReadOnlyList<PeopleRecords> regions, IReadOnlyList<FoldBack> folds)
    {
        int n = 0;
        foreach (var f in folds)
        {
            var rec = regions.FirstOrDefault(r => r.RegionId == f.Region);
            if (rec == null) continue;
            var p = rec.Pop.People;
            if (f.Index < 0 || f.Index >= p.Count || p.IdOf(f.Index) != f.Person || p.Status[f.Index] != PersonStatus.Alive) continue;
            rec.Jobs.Set(f.Index, f.Job); rec.Jobs.AddLiveWork(f.LiveWorkSec);
            n++;
        }
        return n;
    }

    // ------------------------------------------------------------------ odd jobs (main thread)

    private readonly Dictionary<EntityId, Job> _jobSnaps = [];
    private List<Job> _board = [];
    private readonly List<ClaimRequest> _claims = [];

    /// <summary>
    /// Takes copies of the jobs the table holds (at a barrier, then handed over): who is on a job (the live person follows its legs) and which are open at which board (an idle person may take one).
    /// Copies, because the table belongs to the tick worker.
    /// </summary>
    public void SetJobs(IReadOnlyList<Job> live, IReadOnlyList<Job> open)
    {
        _jobSnaps.Clear();
        foreach (var j in live) if (j.Worker != EntityId.None) _jobSnaps[j.Worker] = j;
        _board = [.. open];
        foreach (var (id, a) in _live) { _jobSnaps.TryGetValue(id, out var js); if (a.JobSnap?.Id != js?.Id) { a.JobSnap = js; a.JobKey = long.MinValue; } }
    }

    /// <summary>Claims the live people want to make (NPCs the scorer sent to a board); handed to the barrier by the caller. Clears the list.</summary>
    public IReadOnlyList<ClaimRequest> TakeClaims() { if (_claims.Count == 0) return []; var r = _claims.ToArray(); _claims.Clear(); return r; }

    /// <summary>The scorer's reason for a person's action now (the Q menu shows it); empty for a person who is not live.</summary>
    public string Why(EntityId person) => _live.TryGetValue(person, out var a) ? a.Why : "";

    // ------------------------------------------------------------------ one person's step

    private void Advance(LiveActor a, ILiveGround? ground, long nowSec)
    {
        if (a.JobSnap is { } js && nowSec < JobTable.EndSec(js) + 1) { AdvanceJob(a, js, ground, nowSec); return; }
        a.JobSnap = null;
        var r = Schedule.Resolve(a.A, nowSec);
        long key = r.Day * 64 + r.Segment;
        bool covers = ground != null && Covers(ground, a, r);
        if (key != a.BlockKey || (!a.PlanOnGround && covers))
        {
            if (key == a.NextKey && a.NextPlan != null) { a.Plan = a.NextPlan; a.BlockKey = key; a.PlanOnGround = true; }
            else { long t0 = System.Diagnostics.Stopwatch.GetTimestamp(); Rebuild(a, r, covers ? ground : null); PlansBuilt++; NotePlan(a, r, t0); }
            a.NextPlan = null; a.NextKey = long.MinValue;
        }
        else if (covers && a.NextPlan == null && _prefetchLeft > 0 && r.Job.EndSec - nowSec <= PrefetchSec)
        {
            var rn = Schedule.Resolve(a.A, r.Job.EndSec);
            long nk = rn.Day * 64 + rn.Segment;
            if (nk != key && Covers(ground!, a, rn))
            {
                _prefetchLeft--;
                var keep = (a.Plan, a.BlockKey, a.PlanOnGround);
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                Rebuild(a, rn, ground); PlansBuilt++; NotePlan(a, rn, t0);
                (a.NextPlan, a.NextKey) = (a.Plan, nk);
                (a.Plan, a.BlockKey, a.PlanOnGround) = keep;
            }
        }
        var pos = a.Plan!.PositionAt(nowSec);
        a.X = pos.XMm; a.Y = pos.YMm; a.Z = a.Plan.ZAt(nowSec);
        a.Walking = a.Plan.MovingAt(nowSec);
        if (r.Seg.Walks) { a.HeadX = pos.HeadX; a.HeadY = pos.HeadY; } else { a.HeadX = r.HeadX; a.HeadY = r.HeadY; }
        if (r.Job.Kind == JobKind.Work && a.LastSec > 0 && nowSec > a.LastSec) a.WorkSecs += nowSec - a.LastSec;
        a.LastSec = nowSec; a.Action = r.Job.Kind;
        Decide(a, nowSec);
    }

    /// <summary>
    /// The utility scorer, once per 20-game-second window (never per tick: at 8x a tick is 8 game seconds, so the window is what makes the answer the same at any speed). The needs and the schedule block
    /// are read at the window's first second, so the choice is a function of the window and the person alone.
    /// </summary>
    private void Decide(LiveActor a, long nowSec)
    {
        long window = nowSec / 20;
        if (window == a.DecisionWindow) return;
        a.DecisionWindow = window;
        long at = window * 20;
        var brain = UtilityBrain.Default;
        // the one function every person's choice goes through (the hero and party members on autopilot call it too: Behaviour/PersonDecision)
        bool boardHere = _board.Count > 0 && _board.Any(j => j.Settlement == a.A.HomeSettlement && j.Region == a.A.Region);
        var ch = PersonDecision.Decide(brain, a.A, at, a.Intent, a.IntentSince, boardHere, a.ColdPm, a.WetPm, a.HeatPm, a.Body != null, a.Indoors).Choice;
        if (ch.Kind != a.Intent) { a.Intent = ch.Kind; a.IntentSince = at; }
        a.Why = ch.Kind == ActionKind.TakeJob ? "Looking for work" : $"{ch.Kind} {ch.Score}";
        // a person who would take the job does not rush it the same second as everyone else: they get round to it, on average, once in eight minutes (a draw keyed on the window, never on the tick)
        if (ch.Kind == ActionKind.TakeJob && a.JobSnap == null && DetRng.Range(a.A.Seed, window, a.A.Id.Value, RngStreams.UtilityNoise, 99, 0, 24) == 0 && !_claims.Any(c => c.Person == a.Binding.Person))
        {
            var job = _board.Where(j => j.Settlement == a.A.HomeSettlement && j.Region == a.A.Region && j.MinAge <= 18 && j.MinSkill == 0).OrderBy(j => j.PostedSec).ThenBy(j => j.Id).FirstOrDefault();
            if (job != null) _claims.Add(new ClaimRequest(job.Id, a.Binding.Person, a.Binding.Index, at, _claims.Count, a.X - a.OriginXMm, a.Y - a.OriginYMm, 0, 30));
        }
    }

    /// <summary>
    /// One game minute of a person's body, at the minute boundary (never per tick, so at 8x it runs once per minute of game time, not 8 times as often per real second): the weather at the minute's own second at
    /// their home, the place their schedule puts them at that second (indoors, by a lit hearth, in bed), what they are doing, and a Shelter intent taken from the last scorer window. A pure function of the
    /// person, the minute and that window's choice, so a run at 1x and at 8x lead to the same body.
    /// </summary>
    private void Expose(LiveActor a, long nowSec)
    {
        long minute = nowSec / 60;
        if (a.Body!.LastMinute == minute) return;
        long msec = minute * 60;
        var env = _ambient!(msec, a.OriginXMm + a.A.HomeX, a.OriginYMm + a.A.HomeY);
        var r = Schedule.Resolve(a.A, msec);
        var kind = a.JobSnap != null ? JobKind.Work : r.Job.Kind;      // (never a.Action: it is where the last tick left it, which depends on the game speed)
        bool craft = a.A.HasWork && !a.A.WorkInField && a.A.Occupation is Occupation.Smith or Occupation.Baker or Occupation.Brewer or Occupation.Butcher or Occupation.Carpenter or Occupation.Mason
            or Occupation.Weaver or Occupation.Spinner or Occupation.Tanner or Occupation.Cobbler or Occupation.Merchant or Occupation.Innkeeper or Occupation.Priest or Occupation.Miller or Occupation.Servant or Occupation.Steward;
        bool indoors = r.Z > 0 || kind is JobKind.Sleep or JobKind.Home or JobKind.Eat or JobKind.Church || (kind == JobKind.Work && craft);
        bool cold = env.TempCenti < 1000;
        bool hearth = indoors && cold && kind != JobKind.Church;
        bool sheltering = a.Intent == ActionKind.Shelter && !indoors;
        a.Indoors = indoors;
        env = env with { Roofed = indoors || sheltering, Fire = hearth || (sheltering && cold), InBed = kind == JobKind.Sleep && a.A.HasBed, Shade = sheltering || (indoors && !cold) };
        var act = kind switch { JobKind.Sleep => Activity.Sleep, JobKind.Work or JobKind.Haul or JobKind.GuardCaravan => Activity.Work, JobKind.Commute or JobKind.Travel or JobKind.Shop or JobKind.Forage => Activity.Walk, _ => Activity.Rest };
        // a meal at noon and supper keep the body fed; a person who has gone half a day without one rewarms slowly (the day's meals are the schedule's, see NeedModel)
        var death = Clothing!.StepTo(a.Body, minute, env, act);
        var last = a.Body.Last;
        a.ColdPm = last.ColdStressPm; a.HeatPm = last.HeatStressPm; a.WetPm = last.WetAvgPm;
        if (death != ExposureDeath.None && !_reported.Contains(a.Binding.Person)) { _reported.Add(a.Binding.Person); _exposureDeaths.Add((a.Binding.Person, death)); }
    }

    private readonly HashSet<EntityId> _reported = [];

    /// <summary>One person on an odd job: they walk the legs of the job (routes over the loaded ground, timed by the job's own phases), or stand and work at the end of a leg.</summary>
    private void AdvanceJob(LiveActor a, Job js, ILiveGround? ground, long nowSec)
    {
        var ph = JobTable.Phases(js); var ends = JobTable.Ends(js, ph);
        int i = 0; while (i < ph.Length - 1 && nowSec >= ends[i]) i++;
        long key = (long)(js.Id.Value & 0xFFFFFFFFFFUL) * 16 + i;
        var p = ph[i]; long t0 = i == 0 ? js.ClaimedSec : ends[i - 1], t1 = ends[i];
        bool moves = p.Kind is PhaseKind.Go or PhaseKind.Carry;
        long fx = a.OriginXMm + p.X0, fy = a.OriginYMm + p.Y0, tx = a.OriginXMm + p.X1, ty = a.OriginYMm + p.Y1;
        bool covers = ground != null && ground.Covers(FloorM(fx), FloorM(fy)) && ground.Covers(FloorM(tx), FloorM(ty));
        if (key != a.JobKey || (!a.PlanOnGround && covers))
        {
            a.JobKey = key; a.JobPhase = i;
            a.JobPlan = BuildLeg(covers ? ground : null, fx, fy, 0, tx, ty, 0, t0, t1, moves, false, false, out bool onGround);
            a.PlanOnGround = onGround; a.BlockKey = long.MinValue;
        }
        var pos = a.JobPlan!.PositionAt(nowSec);
        a.X = pos.XMm; a.Y = pos.YMm; a.Z = 0; a.Walking = a.JobPlan.MovingAt(nowSec); a.JobStep = i;
        if (moves) { a.HeadX = pos.HeadX; a.HeadY = pos.HeadY; }
        a.LastSec = nowSec;
        a.Action = p.Kind == PhaseKind.Go ? JobKind.Commute : p.Kind == PhaseKind.Carry ? JobKind.Haul : p.Kind == PhaseKind.Work && js.Kind == OddJobKind.Guard ? JobKind.GuardCaravan : JobKind.Work;
        a.Intent = ActionKind.JobStep; a.Why = $"{js.Kind}: {p.Kind}"; a.DecisionWindow = nowSec / 20;
    }

    private void NotePlan(LiveActor a, in ResolvedJob r, long t0)
    {
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        if (ms > MaxPlanMs) { MaxPlanMs = ms; WorstPlan = $"{r.Seg.Kind} {(a.PlanOnGround ? "ground" : "free")} len {a.Plan!.LengthMm / 1000} m"; }
    }

    private static int FloorM(long mm) => (int)Math.Floor(mm / 1000.0);

    private static bool Covers(ILiveGround g, LiveActor a, in ResolvedJob r)
        => g.Covers(FloorM(a.OriginXMm + r.FromX), FloorM(a.OriginYMm + r.FromY)) && g.Covers(FloorM(a.OriginXMm + r.ToX), FloorM(a.OriginYMm + r.ToY));

    private static void Rebuild(LiveActor a, in ResolvedJob r, ILiveGround? g)
    {
        a.BlockKey = r.Day * 64 + r.Segment;
        long fx = a.OriginXMm + r.FromX, fy = a.OriginYMm + r.FromY, tx = a.OriginXMm + r.ToX, ty = a.OriginYMm + r.ToY;
        a.Plan = BuildLeg(g, fx, fy, r.FromZ, tx, ty, r.ToZ, r.Job.StartSec, r.Job.EndSec, r.Seg.Walks, r.Seg.From == PlaceRef.Bed && a.A.HasBed, r.Seg.To == PlaceRef.Bed && a.A.HasBed, out bool onGround);
        a.PlanOnGround = onGround;
    }

    /// <summary>
    /// A timed plan from one spot to another, possibly on another level: over the ground's routes (through stairs and other portals, string-pulled level by level) when the ground is loaded, else a straight line.
    /// A bed is a cell that is not walkable, so a walk to it ends at the nearest open cell and then steps onto it; a walk from it begins by stepping off it.
    /// </summary>
    internal static PathPlan BuildLeg(ILiveGround? g, long fx, long fy, int fz, long tx, long ty, int tz, long t0, long t1, bool walks, bool fromBed, bool toBed, out bool onGround)
    {
        onGround = false;
        if (g == null) return walks ? PathPlan.Straight(fx, fy, fz, tx, ty, tz, t0, t1) : PathPlan.Point(tx, ty, tz, t0, t1);
        onGround = true;
        var sf = fromBed ? PathSmoother.Snap3(g, FloorM(fx), FloorM(fy), fz) : PathSmoother.Snap3(g, FloorM(fx), FloorM(fy), fz);
        var st = PathSmoother.Snap3(g, FloorM(tx), FloorM(ty), tz);
        long Cx(long raw, int snapped, int original) => snapped == original ? raw : snapped * 1000L + 500;
        long sx = Cx(fx, sf.X, FloorM(fx)), sy = Cx(fy, sf.Y, FloorM(fy)), ex = Cx(tx, st.X, FloorM(tx)), ey = Cx(ty, st.Y, FloorM(ty));
        if (!walks || (sf == st && fz == tz)) return PathPlan.Point(toBed ? tx : ex, toBed ? ty : ey, tz, t0, t1);
        var route = g.Route3(sf.X, sf.Y, fz, st.X, st.Y, tz);
        if (route == null || route.Length < 2) return PathPlan.Straight(sx, sy, fz, ex, ey, tz, t0, t1);
        var cells = PathSmoother.Smooth3(g, route);
        var xs = new List<long>(cells.Length + 2); var ys = new List<long>(cells.Length + 2); var zs = new List<int>(cells.Length + 2);
        if (fromBed) { xs.Add(fx); ys.Add(fy); zs.Add(fz); }
        foreach (var c in cells) { xs.Add(c.X * 1000L + 500); ys.Add(c.Y * 1000L + 500); zs.Add(c.Z); }
        int first = fromBed ? 1 : 0;
        if (!fromBed) { xs[0] = sx; ys[0] = sy; }
        if (toBed) { xs.Add(tx); ys.Add(ty); zs.Add(tz); } else { xs[^1] = ex; ys[^1] = ey; }
        return new PathPlan([.. xs], [.. ys], [.. zs], t0, t1);
    }
}
