using Ravensguard.Sim.Behaviour;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.WorldGen.Chunks;

namespace Ravensguard.Sim.Live;

/// <summary>Joins the people bridge to the running world: the scan and the fold-back run on the tick worker between two hours (<see cref="WorldRuntime.AtBarrier{T}"/>), where every record may be read.</summary>
public static class RuntimePeople
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldRuntime, Holder> Facts = new();
    private sealed class Holder { public readonly Dictionary<int, LiveFacts> ByRegion = []; public ScheduleBook? Book; }

    /// <summary>What the bridge reads of every resident region, ascending id. Call on the tick worker (inside a barrier job); makes each region's <see cref="AbstractJobTable"/> on first use.</summary>
    public static List<PeopleRecords> RecordsOf(WorldRuntime rt)
    {
        var res = new List<PeopleRecords>();
        var h = Facts.GetOrCreateValue(rt);
        h.Book ??= new ScheduleBook(BehaviourContent.From(rt.Services.Content)) { FeastMasks = Ravensguard.Sim.Faith.HolyCalendar.FeastMasks(rt.Services.Content) };   // P6-B4: a culture's feasts are holy days of its people
        foreach (var rw in rt.Records.Resident.OrderBy(r => r.Coord.Id))
        {
            var jobs = rw.Sim.Get<AbstractJobTable>();
            if (jobs == null) { jobs = new AbstractJobTable(); rw.Sim.Set(jobs); }
            // the beds and the market stalls are read from the buildings the generator places for the region (the same ones the street view draws)
            if (!h.ByRegion.TryGetValue(rw.Coord.Id, out var facts))
            {
                var gen = ChunkGenerator.For(rw.Population.Region, rw.Population, rt.Services.Content);
                h.ByRegion[rw.Coord.Id] = facts = new LiveFacts(gen);
            }
            var armies = rw.Sim.Get<Ravensguard.Sim.Armies.ArmyWorld>();
            res.Add(new PeopleRecords(rw.Coord.Id, rw.Coord.OriginXM, rw.Coord.OriginYM, rw.Population, rw.Sim.Seed, jobs, facts, h.Book, armies == null ? null : armies.IsInColumn));
        }
        return res;
    }

    /// <summary>Asks the worker to find who the bubbles should hold (see <see cref="PeopleBridge.Scan"/>); the result is applied by the caller on the main thread (<see cref="PeopleBridge.Enqueue"/>).</summary>
    public static Task<ScanResult> ScanAsync(WorldRuntime rt, ScanInput input) => rt.AtBarrier(() => PeopleBridge.Scan(RecordsOf(rt), input));

    /// <summary>
    /// Hands the people the live tier saw die of exposure (<see cref="PeopleBridge.TakeExposureDeaths"/>) to their regions' records: each becomes a <see cref="Abstract.CommandKind.PersonDied"/>
    /// command (cold: <see cref="People.DeathCause.Cold"/>; heat: <see cref="People.DeathCause.Other"/>) on the region's ticker, applied at the next barrier in ascending person id, so the death is on the
    /// person record, in the population tables and in the chronicle. The task's result is how many were queued.
    /// </summary>
    public static Task<int> ExposureDeathsAsync(WorldRuntime rt, IReadOnlyList<(Core.EntityId Person, Clothing.ExposureDeath Kind)> deaths, long nowSec)
        => rt.AtBarrier(() => QueueExposureDeaths(rt.Records.Resident, deaths, nowSec), allowFaulted: true);

    /// <summary>The body of <see cref="ExposureDeathsAsync"/>: queues one command per death on the ticker of the region the person's id names (a person of a region no longer resident is skipped).</summary>
    public static int QueueExposureDeaths(IReadOnlyList<RegionWorld> regions, IReadOnlyList<(Core.EntityId Person, Clothing.ExposureDeath Kind)> deaths, long nowSec)
    {
        int n = 0, seq = 0;
        foreach (var (id, kind) in deaths.OrderBy(d => d.Person.Value))
        {
            var rw = regions.FirstOrDefault(r => r.Coord.Id == id.Region);
            if (rw == null || kind == Clothing.ExposureDeath.None) continue;
            var cause = kind == Clothing.ExposureDeath.Cold ? People.DeathCause.Cold : People.DeathCause.Other;
            rw.Ticker.Commands.Enqueue(new Abstract.EconCommand(nowSec, seq++, Abstract.CommandKind.PersonDied, Core.EntityId.None, id, (long)cause, (long)kind));
            n++;
        }
        return n;
    }

    /// <summary>Hands fold-backs to the worker; they are applied at the next barrier. The task's result is how many were applied.</summary>
    public static Task<int> FoldAsync(WorldRuntime rt, IReadOnlyList<FoldBack> folds) => rt.AtBarrier(() => PeopleBridge.ApplyFolds(RecordsOf(rt), folds), allowFaulted: true);
}
