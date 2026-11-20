using Ravensguard.Sim.Core;
using Ravensguard.Sim.Production;

namespace Ravensguard.Sim.Crafting;

/// <summary>A recipe queued at a business station (crafting-full 4.4): what, how many, for whom (a commission), since when. Businesses (P5-F) and households put work here.</summary>
public sealed class QueuedCraft
{
    public EntityId Id, Business, Commission;
    public int StationIndex;
    public ushort Recipe;
    public int Batch = 1;
    public long QueuedAtHour;
}

/// <summary>
/// The live and abstract bridge of workshop batches (crafting-full 4.4, P5-E). Abstract, outside the bubble, <see cref="ProductionSystem"/> credits <see cref="Station.ProgressMicro"/> as in Phase 2.
/// Live, the batch is a <see cref="CraftJob"/> bound to its station: staff in the bubble work it with <see cref="CraftRunner.Advance"/> (the same calls as the hero; several workers each advance it),
/// while the production system leaves the station alone (<see cref="ProductionSystem.LiveStation"/>). At promotion the attended progress is <c>ProgressPpm x step seconds / 1,000,000</c> (floor); at
/// demotion the reverse (floor), so a batch is never done twice or lost (CF-9). The batch's inputs were consumed when it started and its outputs are made once, by the production system, when the
/// station's progress is complete, so the live work only moves progress and the goods stay exact. Step seconds follow the one worker-hour rule: worker-hours x the part-batch scale x
/// <c>(1300 - 6 x skill)</c> permille (overview 8.3), the same as the abstract credit.
/// </summary>
public static class WorkshopBridge
{
    /// <summary>The live jobs of business stations, by (business, station).</summary>
    public static CraftJob? JobAt(CraftState st, EntityId business, int station)
    {
        foreach (var j in st.Jobs.Values) if (j.StationBound && j.Business == business && j.StationIndex == station && !j.Closed) return j;
        return null;
    }

    /// <summary>Whether a station's batch is live (the production system's hook).</summary>
    public static bool IsLive(CraftState st, Business b, int station) => JobAt(st, b.Id, station) != null;

    private static int WorkStep(CraftRecipe r) { for (int i = 0; i < r.Steps.Length; i++) if (r.Steps[i].Attended) return i; return -1; }

    /// <summary>
    /// Promotes a station's batch: a station-bound job at the batch's progress (floor). <paramref name="skill"/> is the worker's skill in the recipe (the time factor). Null when the station is idle, its
    /// batch is already complete or already live, or the recipe has no business twin.
    /// </summary>
    public static CraftJob? Promote(CraftRunner run, RecipeBook book, Business b, int station, EntityId worker, int skill, long nowSec, IdAllocator ids)
    {
        var st = b.Stations[station];
        if (!st.Busy || st.ProgressMicro >= Station.Complete || JobAt(run.State, b.Id, station) != null) return null;
        var cr = run.Db.ByBookId(book.Recipes[st.Recipe].Id);
        if (cr == null) return null;
        int step = WorkStep(cr); if (step < 0) return null;
        var job = new CraftJob
        {
            Id = ids.Allocate(IdKind.Other), Recipe = cr.Index, Crafter = worker, Owner = b.Id, Business = b.Id, StationIndex = station, Batch = 1, StepIndex = step, StartedSec = nowSec,
            Choice = new byte[cr.Slots.Length], ProfPm = Math.Max(1, st.ScalePermille), ToolPm = 1000, SkillPm = (int)Math.Max(1, 1300 - 6L * skill), EffSkill = skill,
        };
        long need = run.StepNeed(job, step);
        job.StepSeconds = (long)((Int128)(st.ProgressMicro / 1_000_000) * need / 1_000_000);
        job.TotalSeconds = job.StepSeconds;
        run.State.Add(job);
        return job;
    }

    /// <summary>Demotes a live job back into its station: <c>ProgressMicro = floor(StepSeconds x 1e6 / step seconds) x 1e6</c>; the job ends.</summary>
    public static void Demote(CraftRunner run, Business b, CraftJob j)
    {
        if (!j.StationBound || j.Business != b.Id) return;
        var st = b.Stations[j.StationIndex];
        if (!j.Closed)
        {
            long need = run.StepNeed(j, j.StepIndex);
            long ppm = Math.Min(1_000_000, j.StepSeconds * 1_000_000 / Math.Max(1, need));
            st.ProgressMicro = Math.Max(0, Math.Min(Station.Complete, ppm * 1_000_000));
        }
        run.State.Remove(j.Id);
    }

    /// <summary>The runner's <see cref="CraftRunner.StationDone"/> for a set of businesses: a live batch worked to its end completes its station (the production system makes the outputs once).</summary>
    public static Action<CraftJob> Done(Func<EntityId, Business?> byId) => j =>
    {
        if (byId(j.Business) is { } b && j.StationIndex < b.Stations.Count) b.Stations[j.StationIndex].ProgressMicro = Station.Complete;
    };

    // ---- the queue API (businesses, P5-F) ----

    /// <summary>Queues a recipe at a business station (a commission on a business names itself).</summary>
    public static QueuedCraft Enqueue(CraftState st, EntityId business, int station, CraftRecipe r, int batch, long hour, IdAllocator ids, EntityId commission = default)
    {
        var q = new QueuedCraft { Id = ids.Allocate(IdKind.Other), Business = business, StationIndex = station, Recipe = r.Index, Batch = Math.Clamp(batch, 1, Math.Max(1, r.BatchMax)), QueuedAtHour = hour, Commission = commission };
        st.Queue.Add(q);
        return q;
    }

    /// <summary>The next queued work at a station (oldest first, then id), or null.</summary>
    public static QueuedCraft? Next(CraftState st, EntityId business, int station)
        => st.Queue.Where(q => q.Business == business && q.StationIndex == station).OrderBy(q => q.QueuedAtHour).ThenBy(q => q.Id).FirstOrDefault();

    public static bool Dequeue(CraftState st, EntityId id) => st.Queue.RemoveAll(q => q.Id == id) > 0;

    /// <summary>Starts the next queued business recipe on an idle station through the production system (its Phase 2 batch lifecycle). True when it started.</summary>
    public static bool StartNext(CraftState st, RecipeDb db, ProductionSystem prod, Business b, int station, GameTime now)
    {
        if (Next(st, b.Id, station) is not { } q) return false;
        var r = db[q.Recipe];
        if (!r.Business || !prod.Content.Recipes.TryGet(r.BookId, out var book)) return false;
        if (!prod.TryStart(b, b.Stations[station], book, now)) return false;
        Dequeue(st, q.Id);
        return true;
    }
}
