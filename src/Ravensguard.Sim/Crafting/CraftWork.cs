using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;

namespace Ravensguard.Sim.Crafting;

/// <summary>
/// The crafting side of the job system (spec 3.3, 4.8): an unfinished piece can be put on the board as a <c>Craft</c> odd job, paid out of the poster's coin like any other, and the worker who takes it
/// works the same <see cref="CraftJob"/> a hero would: they pick it up, advance it by the job's hours and the result goes to whoever finishes it.
/// </summary>
public static class CraftWork
{
    /// <summary>Posts the work of <paramref name="cj"/> for <paramref name="hours"/> hours; the job names the piece in <c>P5</c>. Null when the poster cannot pay (the board's own rules).</summary>
    public static Job? Post(JobTable table, JobEnv env, CraftJob cj, EntityId poster, PurseId posterPurse, int region, int settlement, long nowSec, long wageMites, int hours, string title)
        => table.Post(env, OddJobKind.Craft, poster, posterPurse, region, settlement, nowSec, nowSec + 2 * 86400, wageMites, j =>
        {
            j.P0 = cj.PlaceX; j.P1 = cj.PlaceY; j.P4 = hours; j.P5 = (long)cj.Id.Value; j.SrcOwner = cj.Owner; j.Title = title; j.MinAge = 14;
        });

    /// <summary>The <see cref="JobEnv.CraftWorked"/> hook: the worker takes the piece up and works it for the seconds of the work phase. <paramref name="actorOf"/> turns a person id into the crafter record.</summary>
    public static Action<Job, int> Hook(CraftRunner runner, Func<EntityId, CraftActor> actorOf, Func<long> nowSec) => (j, sec) =>
    {
        var cj = runner.State.Get(new EntityId((ulong)j.P5)); if (cj == null || cj.Closed) return;
        var who = actorOf(j.Worker); long now = nowSec();
        if (cj.State == CraftJobState.Waiting) { if (!runner.Collect(cj, who, now).Ok) return; }
        else if (!runner.Resume(cj, who)) return;
        runner.Advance(cj, sec, now);
    };
}
