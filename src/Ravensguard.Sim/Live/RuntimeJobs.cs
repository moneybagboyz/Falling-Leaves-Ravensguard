using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Live;

/// <summary>What the main thread gets back from a barrier: copies of the claimed and open jobs (the table itself belongs to the tick worker) and what the day's posting did.</summary>
public sealed record JobSnapshot(IReadOnlyList<Job> Live, IReadOnlyList<Job> Open, int Posted, int ClaimsTaken, EntityId? LostJob = null);

/// <summary>Where the hero stands (region millimetres) when the barrier looks, and the job that is never lost to distance (the mentor's first contract).</summary>
public readonly record struct HeroSight(long XMm, long YMm, EntityId? Exempt);

/// <summary>Runs the job table at the barrier of the running world: posting each morning, claims in (frame, seq) order, advancing the phases and paying out. The table is the world state's (<see cref="WorldState.Jobs"/>).</summary>
public static class RuntimeJobs
{

    public static Job Copy(Job j) => new()
    {
        Id = j.Id, Kind = j.Kind, State = j.State, Poster = j.Poster, PosterPurse = j.PosterPurse, Region = j.Region, Settlement = j.Settlement, Slots = j.Slots, P0 = j.P0, P1 = j.P1, P2 = j.P2, P3 = j.P3, P4 = j.P4, P5 = j.P5,
        SrcOwner = j.SrcOwner, DstOwner = j.DstOwner, WageMites = j.WageMites, Escrow = j.Escrow, PostedSec = j.PostedSec, DeadlineSec = j.DeadlineSec, MinSkill = j.MinSkill, MinAge = j.MinAge, Worker = j.Worker,
        WorkerIndex = j.WorkerIndex, ClaimedSec = j.ClaimedSec, StartX = j.StartX, StartY = j.StartY, Applied = j.Applied, PaidMites = j.PaidMites, RefundedMites = j.RefundedMites, WorkMilliHours = j.WorkMilliHours, Title = j.Title,
    };

    public static JobEnv? EnvOf(WorldRuntime rt)
    {
        var e = rt.State.Economy; if (e == null) return null;
        return new JobEnv { Ledger = e.Ledger, Econ = e, Pop = rt.State.Population, Tuning = BehaviourContent.From(rt.Services.Content).OddJobs };
    }

    /// <summary>One barrier of work: post the day's jobs at the first call of a day, apply <paramref name="claims"/>, advance every job to <paramref name="nowSec"/>, and copy out what the live tier needs.</summary>
    public static Task<JobSnapshot> StepAsync(WorldRuntime rt, long nowSec, IReadOnlyList<ClaimRequest> claims, HeroSight? hero = null) => rt.AtBarrier(() =>
    {
        var env = EnvOf(rt); if (env == null) return new JobSnapshot([], [], 0, 0);
        var table = rt.State.Jobs ??= new JobTable();
        int posted = 0, taken = 0; long day = nowSec / 86400;
        if (table.LastPostedDay != day)
        {
            // the morning's posting: every settlement of the home region (the live street is one region at a time), ascending; once a day, even across a save and load
            table.LastPostedDay = day;
            for (int s = 0; s < rt.State.Population.Region.Settlements.Count; s++) posted += RoutineJobs.PostDay(table, env, rt.State.Population, rt.State.HomeRegion.Id, s, day, rt.State.Seed);
        }
        if (claims.Count > 0) foreach (var (_, res) in table.ClaimAll(env, claims, nowSec, rt.State.Seed)) if (res == ClaimResult.Taken) taken++;
        table.Advance(env, nowSec);
        EntityId? lost = hero is { } h ? table.WatchHero(EconomyState.HeroOwner, nowSec, h.XMm, h.YMm, h.Exempt) : null;
        return new JobSnapshot([.. table.Jobs.Values.Where(j => j.Live).Select(Copy)], [.. table.Jobs.Values.Where(j => j.State == JobState.Open).Select(Copy)], posted, taken, lost);
    });
}

public static class RuntimeJobsExt
{
    /// <summary>The hero (or an NPC) walks away from a claimed job: at the next barrier the job fails, pays the hours worked and returns the rest of the escrow.</summary>
    public static Task<bool> AbandonAsync(WorldRuntime rt, EntityId job, long nowSec) => rt.AtBarrier(() =>
    {
        var env = RuntimeJobs.EnvOf(rt); var table = rt.State.Jobs;
        return env != null && table != null && table.Abandon(env, job, nowSec);
    });
}
