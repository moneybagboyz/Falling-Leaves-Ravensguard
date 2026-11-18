using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Live;

/// <summary>What the bridge reads of one resident region: its people (<see cref="Population"/>), where its frame sits on the continent and the seed its schedule draws use.</summary>
public sealed record PeopleRecords(int RegionId, long OriginXM, long OriginYM, Population Pop, ulong Seed, AbstractJobTable Jobs);

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
}

/// <summary>What the street view draws of one promoted person this tick.</summary>
public readonly record struct LivePerson(EntityId Id, string Name, Occupation Occupation, Sex Sex, SocialClass Class, JobKind Action, long XMm, long YMm, long PrevXMm, long PrevYMm, int HeadX, int HeadY, int Bubble);

/// <summary>A person the scan found inside a bubble that is not live yet: the anchors copied out of the records, and the squared distance to the nearest centre (mm).</summary>
public readonly record struct PromotionCandidate(PersonAnchors Anchors, long OriginXMm, long OriginYMm, long D2Mm, int Bubble);

/// <summary>Who is live now and where the bubbles are: what a scan needs from the main thread (an immutable copy, so the scan can run on the tick worker at a barrier).</summary>
public sealed record ScanInput(long NowSec, (long X, long Y)[] Centres, (EntityId Id, int Region, int Index)[] Live);

/// <summary>The scan's answer: people to promote, nearest first (ties ascending region then person), and live people whose record is gone (died, left).</summary>
public sealed record ScanResult(long NowSec, IReadOnlyList<PromotionCandidate> Candidates, IReadOnlyList<EntityId> Gone);

/// <summary>A person given back to the records: the abstract job they continue with, and what the live tier did meanwhile.</summary>
public readonly record struct FoldBack(EntityId Person, int Region, int Index, AbstractJob Job, long XMm, long YMm, bool Displaced, long LiveWorkSec);

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
                _snapshot = [.. _live.Values.Select(a => new LivePerson(a.Binding.Person, a.A.Name, a.A.Occupation, a.A.Sex, a.A.Class, a.Action, a.X, a.Y, a.PrevX, a.PrevY, a.HeadX, a.HeadY, a.Binding.Bubble))];
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
                var a = PersonAnchors.From(pop, i, rec.Seed, rec.RegionId, input.NowSec, tileCache);
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
        int ppm = r.Job.ProgressPpm; var job = r.Job;
        if (r.Seg.Kind == JobKind.Commute && a.Plan != null) ppm = a.Plan.ProgressPpmAt(nowSec);
        // the person is where the plan puts them, so they continue the block; one who was moved far from it (fled, pushed) is travelling to the place of the block
        bool displaced = PathPlan.Dist(a.X - (a.OriginXMm + r.XMm), a.Y - (a.OriginYMm + r.YMm)) > DisplacedMm;
        job = displaced ? new AbstractJob(JobKind.Travel, job.Place, nowSec, job.EndSec, 0) : job with { ProgressPpm = ppm };
        _folds.Add(new FoldBack(a.Binding.Person, a.Binding.Region, a.Binding.Index, job, a.X, a.Y, displaced, a.WorkSecs));
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

    // ------------------------------------------------------------------ one person's step

    private void Advance(LiveActor a, ILiveGround? ground, long nowSec)
    {
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
        a.X = pos.XMm; a.Y = pos.YMm;
        if (r.Seg.Kind == JobKind.Commute) { a.HeadX = pos.HeadX; a.HeadY = pos.HeadY; } else { a.HeadX = r.HeadX; a.HeadY = r.HeadY; }
        if (r.Job.Kind == JobKind.Work && a.LastSec > 0 && nowSec > a.LastSec) a.WorkSecs += nowSec - a.LastSec;
        a.LastSec = nowSec; a.Action = r.Job.Kind;
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
        long t0 = r.Job.StartSec, t1 = r.Job.EndSec;
        long fx = a.OriginXMm + r.FromX, fy = a.OriginYMm + r.FromY, tx = a.OriginXMm + r.ToX, ty = a.OriginYMm + r.ToY;
        bool commute = r.Seg.Kind == JobKind.Commute;
        if (g == null) { a.Plan = commute ? PathPlan.Straight(fx, fy, tx, ty, t0, t1) : PathPlan.Point(tx, ty, t0, t1); a.PlanOnGround = false; return; }
        a.PlanOnGround = true;
        var sf = PathSmoother.Snap(g, FloorM(fx), FloorM(fy)); var st = PathSmoother.Snap(g, FloorM(tx), FloorM(ty));
        // a spot that is already open ground is used as it is; a blocked one moves to the nearest open cell
        long Cx(long raw, int snapped, int original) => snapped == original ? raw : snapped * 1000L + 500;
        long sx = Cx(fx, sf.X, FloorM(fx)), sy = Cx(fy, sf.Y, FloorM(fy)), ex = Cx(tx, st.X, FloorM(tx)), ey = Cx(ty, st.Y, FloorM(ty));
        if (!commute || (sf == st)) { a.Plan = PathPlan.Point(ex, ey, t0, t1); return; }
        var route = g.Route(sf.X, sf.Y, st.X, st.Y);
        if (route == null || route.Length < 2) { a.Plan = PathPlan.Straight(sx, sy, ex, ey, t0, t1); return; }
        var cells = PathSmoother.Smooth(g, route);
        var xs = new long[cells.Length]; var ys = new long[cells.Length];
        for (int i = 0; i < cells.Length; i++) { xs[i] = cells[i].X * 1000L + 500; ys[i] = cells[i].Y * 1000L + 500; }
        xs[0] = sx; ys[0] = sy; xs[^1] = ex; ys[^1] = ey;
        a.Plan = new PathPlan(xs, ys, t0, t1);
    }
}
