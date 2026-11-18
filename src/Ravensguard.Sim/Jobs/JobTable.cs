using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Jobs;

/// <summary>What a job table needs from the world to move coin and goods: the ledger, the stores, who a person's wages go to. Everything it touches is a row of the economy, never a copy.</summary>
public sealed class JobEnv
{
    public required Ledger Ledger { get; init; }
    public EconomyState? Econ { get; init; }
    public Population? Pop { get; init; }
    public OddJobTuningDef Tuning { get; init; } = new() { Id = "odd_jobs" };
    public IWorkSink Work { get; init; } = new WorkLog();
    /// <summary>Called when a Craft job's work phase is done (the job, the game seconds worked): the crafting engine advances the unfinished piece the job names in <c>P5</c> (<c>Crafting.CraftWork</c>).</summary>
    public Action<Job, int>? CraftWorked { get; init; }
    /// <summary>The purse a person's pay goes to: the hero's own, else the household's.</summary>
    public Func<EntityId, PurseId?>? PurseOf { get; init; }

    public PurseId? Payee(EntityId person)
    {
        if (PurseOf != null) return PurseOf(person);
        if (Ledger.TryFind(PurseKind.Person, person, out var p)) return p;
        if (Pop != null && Econ != null && person.Kind == IdKind.Person && person.Counter > 0 && Pop.People.IndexOf(person) is >= 0 and var i && Pop.People.Household[i] is >= 0 and var h && h < Econ.HouseholdCount) return Econ.HouseholdPurse[h];
        return null;
    }

    public Store? StoreOf(EntityId owner)
    {
        if (Econ == null) return null;
        if (Econ.OtherStores.TryGetValue(owner, out var s)) return s;
        if (Pop != null && Pop.Households.IndexOf(owner) is >= 0 and var h && h < Econ.HouseholdCount) return Econ.HouseholdStore[h];
        return null;
    }

    public long UnskilledWage => Tuning.UnskilledWageMitesPerDay;
}

public enum ClaimResult : byte { Taken, NoSuchJob, NotOpen, AlreadyHasJob, TooYoung, TooUnskilled, Reserved, TooFar }

/// <summary>
/// The job and reservation system (live-behaviour spec 4.5, 4.6): one table for every person, the hero's autopilot and the NPCs. A job is posted with its wage escrowed in the ledger, claimed by one worker
/// at a barrier in <c>(Frame, Seq)</c> order, run as a list of phases whose times are a pure function of the job (so the abstract tier and the live tier agree to the second), and paid out of the escrow when
/// it is done. Coin and goods only ever move between rows (poster, escrow, worker; store, pack, store), so nothing is made or lost. Everything iterates in id order.
/// </summary>
public sealed class JobTable
{
    private readonly SortedDictionary<EntityId, Job> _jobs = [];
    private readonly Dictionary<EntityId, EntityId> _byWorker = [];
    public ReservationTable Reservations { get; } = new();
    public ulong NextSerial { get; set; } = 1;
    /// <summary>The last game day whose routine jobs were posted (saved, so loading mid-day does not post the day twice).</summary>
    public long LastPostedDay { get; set; } = -1;
    public IReadOnlyDictionary<EntityId, Job> Jobs => _jobs;
    public int Count => _jobs.Count;
    /// <summary>True when there is nothing to save: no jobs and both counters at their start (review finding 5: a table whose jobs are all gone still holds the next serial and the last posted day).</summary>
    public bool IsEmpty => _jobs.Count == 0 && NextSerial == 1 && LastPostedDay == -1;

    public Job? Get(EntityId id) => _jobs.GetValueOrDefault(id);
    public Job? JobOf(EntityId worker) => _byWorker.TryGetValue(worker, out var j) ? _jobs[j] : null;
    public bool HasJob(EntityId worker) => _byWorker.ContainsKey(worker);

    /// <summary>Open jobs listed at a settlement, oldest first (the board, or what the reeve and innkeeper tell).</summary>
    public List<Job> Open(int region, int settlement) => [.. _jobs.Values.Where(j => j.State == JobState.Open && j.Region == region && j.Settlement == settlement).OrderBy(j => j.PostedSec).ThenBy(j => j.Id)];

    public static EntityId MakeId(int region, ulong serial) => EntityId.MakeIn(IdKind.Other, region, EntityId.ReservedSerialBit | serial);

    // ------------------------------------------------------------------ posting

    /// <summary>Posts a job: the wage moves from the poster's purse into a new escrow chest. Null when the poster cannot pay, or the board is full.</summary>
    public Job? Post(JobEnv env, OddJobKind kind, EntityId poster, PurseId posterPurse, int region, int settlement, long nowSec, long deadlineSec, long wageMites, Action<Job>? init = null)
    {
        if (wageMites <= 0 || env.Ledger.Balance(posterPurse).Mites < wageMites) return null;
        if (_jobs.Values.Count(j => j.State == JobState.Open && j.Region == region && j.Settlement == settlement) >= env.Tuning.BoardCap) return null;
        var id = MakeId(region, NextSerial++);
        var escrow = env.Ledger.Open(PurseKind.Chest, id);
        if (!env.Ledger.Transfer(posterPurse, escrow, new Coin(wageMites), JobReasons.Escrow)) throw new InvalidOperationException("escrow transfer failed after a balance check");
        var j = new Job { Id = id, Kind = kind, Poster = poster, PosterPurse = posterPurse.Index, Region = region, Settlement = settlement, WageMites = wageMites, Escrow = escrow.Index, PostedSec = nowSec, DeadlineSec = deadlineSec, State = JobState.Open };
        init?.Invoke(j);
        _jobs[id] = j;
        return j;
    }

    /// <summary>Cancels a job nobody has started: the escrow goes back to the poster. A claimed job is abandoned, not cancelled.</summary>
    public bool Cancel(JobEnv env, EntityId id, long nowSec)
    {
        if (!_jobs.TryGetValue(id, out var j) || j.State != JobState.Open) return false;
        Refund(env, j); j.State = JobState.Cancelled; return true;
    }

    private void Refund(JobEnv env, Job j)
    {
        var escrow = new PurseId(j.Escrow); long left = env.Ledger.Balance(escrow).Mites;
        if (left > 0) { env.Ledger.Transfer(escrow, new PurseId(j.PosterPurse), new Coin(left), JobReasons.Refund); j.RefundedMites += left; }
    }

    // ------------------------------------------------------------------ claiming

    private static ulong Cell(long x, long y) => ((ulong)(x / 1000) << 24) ^ (ulong)(y / 1000);
    private static ulong Mix(ulong a, ulong b) => DetRng.Mix(a * 0x9E3779B97F4A7C15UL ^ b);

    /// <summary>What a job needs held while it runs, as reservation keys.</summary>
    public static IEnumerable<(ReserveKind Kind, ulong Id)> Needs(Job j)
    {
        switch (j.Kind)
        {
            case OddJobKind.Haul or OddJobKind.Deliver or OddJobKind.Fetch: yield return (ReserveKind.Stock, Mix(j.SrcOwner.Value, (ulong)j.P4)); break;
            case OddJobKind.Guard or OddJobKind.FarmWork: yield return (ReserveKind.Tile, Cell(j.P0, j.P1)); break;
            case OddJobKind.TendShop: yield return (ReserveKind.Station, Mix(j.SrcOwner.Value, Cell(j.P0, j.P1))); break;
            case OddJobKind.Escort: yield return (ReserveKind.Seat, j.Id.Value); break;
        }
    }

    /// <summary>Applies one claim now. The caller orders the claims (<see cref="ClaimAll"/>).</summary>
    public ClaimResult Claim(JobEnv env, in ClaimRequest r, long nowSec)
    {
        if (!_jobs.TryGetValue(r.Job, out var j)) return ClaimResult.NoSuchJob;
        if (j.State != JobState.Open) return ClaimResult.NotOpen;
        if (_byWorker.ContainsKey(r.Person)) return ClaimResult.AlreadyHasJob;
        if (r.Age < j.MinAge) return ClaimResult.TooYoung;
        if (r.Skill < j.MinSkill) return ClaimResult.TooUnskilled;
        var needs = Needs(j).ToList();
        foreach (var (k, id) in needs) if (Reservations.IsHeldByOther(k, id, j.Id, nowSec)) return ClaimResult.Reserved;
        foreach (var (k, id) in needs) Reservations.TryReserve(k, id, r.Person, j.Id, nowSec, env.Tuning.ReservationTtlSeconds);
        j.State = JobState.Claimed; j.Worker = r.Person; j.WorkerIndex = r.PersonIndex; j.ClaimedSec = nowSec; j.StartX = r.X; j.StartY = r.Y; j.Applied = 0;
        _byWorker[r.Person] = j.Id;
        return ClaimResult.Taken;
    }

    /// <summary>
    /// Resolves a batch of claims: in <c>(Frame, Seq)</c> order, so the claim that was issued first wins whatever thread it arrived on; two claims with the same frame and sequence are ranked by the claim stream
    /// (<c>0x301</c>, keyed on the job and the game hour, drawn per claimant), so the winner never depends on the order the batch lists them in.
    /// </summary>
    public IReadOnlyList<(ClaimRequest Req, ClaimResult Result)> ClaimAll(JobEnv env, IEnumerable<ClaimRequest> requests, long nowSec, ulong seed = 0)
    {
        long hour = nowSec / 3600;
        var ordered = requests.OrderBy(r => r.Frame).ThenBy(r => r.Seq)
            .ThenBy(r => DetRng.Range(seed, hour, r.Job.Value, RngStreams.JobClaim, (uint)r.PersonIndex, 0, int.MaxValue)).ThenBy(r => r.PersonIndex).ToList();
        var res = new List<(ClaimRequest, ClaimResult)>(ordered.Count);
        foreach (var r in ordered) res.Add((r, Claim(env, r, nowSec)));
        return res;
    }

    // ------------------------------------------------------------------ the phases of a job

    private static int Secs(long mm) => (int)Math.Min(int.MaxValue / 2, (mm + ScheduleTuning.CommuteMmPerGs - 1) / ScheduleTuning.CommuteMmPerGs);
    private static int Dist(long x0, long y0, long x1, long y1) => Secs(PathPlan.Dist(x1 - x0, y1 - y0));

    /// <summary>The legs of the job from the worker's start, in order. A pure function of the job: the live tier walks them, the abstract tier only reads their end times.</summary>
    public static Phase[] Phases(Job j)
    {
        long sx = j.StartX, sy = j.StartY;
        switch (j.Kind)
        {
            case OddJobKind.Haul or OddJobKind.Deliver or OddJobKind.Fetch:
            {
                int load = (int)Math.Max(5, j.P5 / 2000);   // one second per two kilograms, at the pace of a carrier
                return
                [
                    new(PhaseKind.Go, Dist(sx, sy, j.P0, j.P1), sx, sy, j.P0, j.P1), new(PhaseKind.Load, load, j.P0, j.P1, j.P0, j.P1),
                    new(PhaseKind.Carry, Dist(j.P0, j.P1, j.P2, j.P3), j.P0, j.P1, j.P2, j.P3), new(PhaseKind.Unload, load, j.P2, j.P3, j.P2, j.P3), new(PhaseKind.Report, 10, j.P2, j.P3, j.P2, j.P3),
                ];
            }
            case OddJobKind.Escort:
                return [new(PhaseKind.Go, Dist(sx, sy, j.P0, j.P1), sx, sy, j.P0, j.P1), new(PhaseKind.Carry, (int)Math.Max(60, j.P4 * 3600), j.P0, j.P1, j.P2, j.P3), new(PhaseKind.Handover, 30, j.P2, j.P3, j.P2, j.P3)];
            default:    // Guard, TendShop, FarmWork, Craft: go there, work the hours, hand over
                return [new(PhaseKind.Go, Dist(sx, sy, j.P0, j.P1), sx, sy, j.P0, j.P1), new(PhaseKind.Work, (int)Math.Max(60, j.P4 * 3600), j.P0, j.P1, j.P0, j.P1), new(PhaseKind.Handover, 30, j.P0, j.P1, j.P0, j.P1)];
        }
    }

    /// <summary>The game second each phase ends at (cumulative from the claim).</summary>
    public static long[] Ends(Job j, Phase[] ph)
    {
        var e = new long[ph.Length]; long t = j.ClaimedSec;
        for (int i = 0; i < ph.Length; i++) { t += ph[i].DurSec; e[i] = t; }
        return e;
    }

    public static long EndSec(Job j) { var ph = Phases(j); return Ends(j, ph)[^1]; }

    /// <summary>Where the worker is at <paramref name="nowSec"/> (region millimetres), the phase they are in and how many have finished.</summary>
    public static (long X, long Y, int Phase, int Finished) PositionAt(Job j, long nowSec)
    {
        var ph = Phases(j); var ends = Ends(j, ph);
        long start = j.ClaimedSec;
        for (int i = 0; i < ph.Length; i++)
        {
            if (nowSec < ends[i])
            {
                var p = ph[i]; long t = Math.Max(0, nowSec - start), d = Math.Max(1, p.DurSec);
                bool moves = p.Kind is PhaseKind.Go or PhaseKind.Carry;
                return moves ? (p.X0 + (p.X1 - p.X0) * t / d, p.Y0 + (p.Y1 - p.Y0) * t / d, i, i) : (p.X1, p.Y1, i, i);
            }
            start = ends[i];
        }
        var last = ph[^1];
        return (last.X1, last.Y1, ph.Length - 1, ph.Length);
    }

    // ------------------------------------------------------------------ running

    /// <summary>
    /// Brings every claimed job up to <paramref name="nowSec"/>: applies the effects of each phase that has finished (the goods leave the source at the end of loading and reach the destination at the end of
    /// unloading, farm hours are credited as WorkDone when the work ends), refreshes what the running jobs hold, pays and closes the finished, expires the open ones past their deadline and fails the claimed
    /// ones that overran. Ascending job id; the same result however often it is called.
    /// </summary>
    public void Advance(JobEnv env, long nowSec)
    {
        Reservations.Expire(nowSec);
        long grace = env.Tuning.GraceHours * 3600L;
        // closed jobs are kept for five days (the Journal shows recent ones), then dropped; their escrow chests are empty by then
        foreach (var id in _jobs.Where(kv => kv.Value.Closed && kv.Value.PostedSec < nowSec - 5 * 86400L).Select(kv => kv.Key).ToList()) _jobs.Remove(id);
        foreach (var j in _jobs.Values.ToList())
        {
            if (j.State == JobState.Open) { if (nowSec > j.DeadlineSec) { Refund(env, j); j.State = JobState.Expired; } continue; }
            if (!j.Live) continue;
            var ph = Phases(j); var ends = Ends(j, ph);
            int finished = 0; while (finished < ph.Length && ends[finished] <= nowSec) finished++;
            bool failed = false;
            for (; j.Applied < finished && !failed; j.Applied++) failed = !ApplyPhase(env, j, ph, j.Applied);
            if (failed) { Fail(env, j, nowSec); continue; }
            if (finished >= ph.Length) { Finish(env, j); continue; }
            if (nowSec > j.DeadlineSec + grace) { Fail(env, j, nowSec); continue; }
            j.State = finished == 0 ? JobState.Claimed : JobState.Active;
            Reservations.Refresh(j.Id, nowSec, env.Tuning.ReservationTtlSeconds);
        }
    }

    private bool ApplyPhase(JobEnv env, Job j, Phase[] ph, int i)
    {
        var p = ph[i];
        switch (j.Kind)
        {
            case OddJobKind.Haul or OddJobKind.Deliver or OddJobKind.Fetch when p.Kind == PhaseKind.Load:
            {
                var src = env.StoreOf(j.SrcOwner); if (src == null || env.Econ == null) return true;   // a job without goods (a world with no stores)
                var pack = env.Econ.OpenStore(StoreKind.House, j.Id, 0, 0, false);
                return Store.Move(src, pack, (ushort)j.P4, j.P5);
            }
            case OddJobKind.Haul or OddJobKind.Deliver or OddJobKind.Fetch when p.Kind == PhaseKind.Unload:
            {
                var dst = env.StoreOf(j.DstOwner); if (dst == null || env.Econ == null) return true;
                var pack = env.Econ.OpenStore(StoreKind.House, j.Id, 0, 0, false);
                if (pack.Qty((ushort)j.P4) < j.P5) return true;          // nothing was loaded (no store at the source)
                return Store.Move(pack, dst, (ushort)j.P4, j.P5);
            }
            case OddJobKind.FarmWork when p.Kind == PhaseKind.Work:
                j.WorkMilliHours += p.DurSec * 1000L / 3600; env.Work.WorkDone(j.P5, p.DurSec * 1000L / 3600); return true;
            case OddJobKind.Craft when p.Kind == PhaseKind.Work:
                j.WorkMilliHours += p.DurSec * 1000L / 3600; env.CraftWorked?.Invoke(j, p.DurSec); return true;
            case OddJobKind.Guard or OddJobKind.TendShop when p.Kind == PhaseKind.Work:
                j.WorkMilliHours += p.DurSec * 1000L / 3600; return true;
        }
        return true;
    }

    private void Finish(JobEnv env, Job j)
    {
        var escrow = new PurseId(j.Escrow); long pay = env.Ledger.Balance(escrow).Mites;
        var payee = env.Payee(j.Worker);
        if (pay > 0 && payee is { } to) { env.Ledger.Transfer(escrow, to, new Coin(pay), JobReasons.Pay); j.PaidMites += pay; }
        else Refund(env, j);          // nobody to pay (the worker has no purse): the poster keeps the coin
        End(j, JobState.Done);
    }

    /// <summary>A failed job returns what is in the pack to the source, pays the hours already worked (guard, tending and farm work pay per hour; haul and escort pay all or nothing), and refunds the rest.</summary>
    private void Fail(JobEnv env, Job j, long nowSec)
    {
        if (env.Econ != null && j.Kind is OddJobKind.Haul or OddJobKind.Deliver or OddJobKind.Fetch && env.Econ.OtherStores.TryGetValue(j.Id, out var pack) && env.StoreOf(j.SrcOwner) is { } src)
            Store.MoveUpTo(pack, src, (ushort)j.P4, pack.Qty((ushort)j.P4));
        if (j.Kind is OddJobKind.Guard or OddJobKind.TendShop or OddJobKind.FarmWork && j.P4 > 0 && env.Payee(j.Worker) is { } to)
        {
            var ph = Phases(j); var ends = Ends(j, ph);
            long worked = Math.Clamp(nowSec - (ends[0]), 0, ph[1].DurSec);
            long share = j.WageMites * worked / ph[1].DurSec;
            if (share > 0 && env.Ledger.Balance(new PurseId(j.Escrow)).Mites >= share) { env.Ledger.Transfer(new PurseId(j.Escrow), to, new Coin(share), JobReasons.Pay); j.PaidMites += share; }
        }
        Refund(env, j);
        End(j, JobState.Failed);
    }

    /// <summary>The worker walks away from a claimed job (or dies): same as a failed one.</summary>
    public bool Abandon(JobEnv env, EntityId id, long nowSec)
    {
        if (!_jobs.TryGetValue(id, out var j) || !j.Live) return false;
        Advance(env, nowSec);
        if (!j.Live) return false;
        Fail(env, j, nowSec); return true;
    }

    private void End(Job j, JobState s)
    {
        j.State = s; Reservations.Release(j.Id);
        // (the hero's id is the zero id, so "has a worker" cannot be tested against EntityId.None: a worker's entry goes when it points at this very job)
        if (_byWorker.TryGetValue(j.Worker, out var cur) && cur == j.Id) _byWorker.Remove(j.Worker);
    }

    // ------------------------------------------------------------------ saving

    public void Restore(Job j)
    {
        _jobs[j.Id] = j;
        if (j.Live && (j.Worker != EntityId.None || j.ClaimedSec > 0)) _byWorker[j.Worker] = j.Id;
    }
}
