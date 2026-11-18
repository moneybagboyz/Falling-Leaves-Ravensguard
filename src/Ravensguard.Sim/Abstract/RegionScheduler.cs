using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Abstract;

/// <summary>What one <see cref="RegionScheduler.Update"/> did (counts and wall time), for the perf report and the tests.</summary>
/// <param name="DaysSteps">Whole days of region time the Daily regions advanced this hour (their hour steps / 24).</param>
/// <param name="DailyHourSteps">Hour steps the Daily regions advanced this hour (the work the staggered schedule spreads over the day).</param>
public sealed record SchedulerReport(int Joined, int PagedOut, int PageInRequested, int HourlySteps, int DaysSteps, double Ms, int DailyHourSteps = 0);

/// <summary>
/// Drives each generated region's abstract tick at its level (seamless-regions spec 3.4, 3.5; abstract spec 3.3). A region's level is a pure function of its
/// Chebyshev distance to the hero's ring region and of whether it is generated, so it never depends on the order regions were visited or generated:
/// ring 0 is <see cref="RegionLevel.Live"/> and ring 1 <see cref="RegionLevel.Detailed"/> (both stepped every game hour), ring 2 <see cref="RegionLevel.Daily"/> (stepped
/// in <see cref="DailySliceHours"/>-hour slices staggered by region id), anything further is <see cref="RegionLevel.Paged"/> (written to the pager, dropped from memory, caught up on return),
/// and a region never generated is <see cref="RegionLevel.Abstract"/> (continental numbers only, nothing here).
/// The steps are the abstract tick's own (a region's <see cref="WorldTicker"/>), so a region's records at hour H are the same whatever levels it passed through
/// (note N13: the systems have no daily-mode kernel yet, so Daily saves scheduling, not CPU).
/// </summary>
public sealed class RegionScheduler
{
    public RegionRecordSet Records { get; }
    /// <summary>Rings 0 and 1 (the 3 x 3 block).</summary>
    public int DetailedRing { get; init; } = 1;
    /// <summary>Last ring kept resident; beyond it regions are paged.</summary>
    public int DailyRing { get; init; } = 2;
    /// <summary>Regions stepped at the same time (they are independent: own records, chronicle and id counters).</summary>
    public int MaxParallel { get; init; } = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
    /// <summary>
    /// A Daily region catches up in slices of this many hours, each region on its own phase (<c>hour - id</c> modulo the slice), so no barrier carries every daily region's work at once
    /// (13 daily regions on one day boundary cost 340 to 600 ms in a single hour; spread, a barrier carries about one slice in <c>DailySliceHours</c> of them). Pure in (hour, region id).
    /// </summary>
    public const int DailySliceHours = 6;

    /// <summary>The hour a Daily region is stepped to at barrier <paramref name="hour"/>: the latest hour at or before it that is on the region's own slice phase.</summary>
    public static long DailyTarget(long hour, int regionId)
    {
        long m = ((hour - regionId) % DailySliceHours + DailySliceHours) % DailySliceHours;
        return hour - m;
    }

    /// <summary>Every earlier hour of a Daily region that has not yet been stepped is run before the hero's region needs it; see <see cref="Update"/>.</summary>
    public SchedulerReport Last { get; private set; } = new(0, 0, 0, 0, 0, 0);

    public RegionScheduler(RegionRecordSet records) { Records = records; }

    /// <summary>The level of a region <paramref name="ring"/> squares from the hero's ring region (Chebyshev), generated or not.</summary>
    public static RegionLevel LevelFor(int ring, bool generated, int detailedRing = 1, int dailyRing = 2)
        => !generated ? RegionLevel.Abstract : ring == 0 ? RegionLevel.Live : ring <= detailedRing ? RegionLevel.Detailed : ring <= dailyRing ? RegionLevel.Daily : RegionLevel.Paged;

    /// <summary>The level every region of the grid would have, ascending id (tests and the world map).</summary>
    public IEnumerable<(RegionCoord Region, RegionLevel Level)> Levels(RegionCoord ringRegion)
    {
        for (int id = 0; id < RegionCoord.Count; id++)
        {
            var rc = RegionCoord.FromId(id);
            bool generated = Records.TryGet(rc) != null || Records.Pager.Has(id);
            yield return (rc, LevelFor(rc.Chebyshev(ringRegion), generated, DetailedRing, DailyRing));
        }
    }

    /// <summary>
    /// One barrier step at game time <paramref name="now"/> with the hero's ring region: joins finished regions, sets levels (paging out those that fell beyond the
    /// daily ring, asking for paged ones that came inside it), then steps every resident region ascending id to the hour its level calls for (in parallel; regions
    /// share nothing). A returning region first catches up (<see cref="RegionWorld.AdvanceTo"/>).
    /// </summary>
    public SchedulerReport Update(GameTime now, RegionCoord ringRegion)
    {
        var sw = Stopwatch.StartNew();
        int joined = Records.Sync(), paged = 0, requested = 0;
        var resident = Records.Resident;
        foreach (var rw in resident)
        {
            int ring = rw.Coord.Chebyshev(ringRegion);
            var lvl = LevelFor(ring, true, DetailedRing, DailyRing);
            if (lvl == RegionLevel.Paged && rw.Coord != Records.Home) { if (Records.PageOut(rw.Coord)) { paged++; continue; } }
            rw.Level = lvl == RegionLevel.Paged ? RegionLevel.Daily : lvl;
        }
        foreach (int id in Records.Pager.Ids)
        {
            var rc = RegionCoord.FromId(id);
            if (Records.TryGet(rc) == null && rc.Chebyshev(ringRegion) <= DailyRing) { Records.PageIn(rc, rc.Chebyshev(ringRegion), now.TotalHours); requested++; }
        }
        long hour = now.TotalHours;
        int hourly = 0, dailyHours = 0;
        var work = Records.Resident;
        long[] before = new long[work.Count];
        for (int i = 0; i < work.Count; i++) before[i] = work[i].HourDone;
        // the continent's caravan table is the one thing the workers share: its cap counts the other regions' caravans as they are now, at the barrier
        Records.Services.AbstractContinent?.Caravans.Freeze();
        var opt = new ParallelOptions { MaxDegreeOfParallelism = MaxParallel };
        Parallel.For(0, work.Count, opt, i =>
        {
            var rw = work[i];
            long target = rw.Level is RegionLevel.Live or RegionLevel.Detailed ? hour : DailyTarget(hour, rw.Coord.Id);
            rw.AdvanceTo(target);
        });
        for (int i = 0; i < work.Count; i++)
        {
            long steps = work[i].HourDone - before[i];
            if (work[i].Level == RegionLevel.Daily) dailyHours += (int)steps; else hourly += (int)steps;
        }
        // the continent's season tick at the barrier, once every seasonal slice of the first day has run (continent-tick spec 3.0); never while a region is half-stepped
        var cont = Records.Services.AbstractContinent;
        if (cont != null && now.DayOfSeason >= 2 && Ravensguard.Sim.Continental.ContinentTick.SeasonOf(now) > cont.LastSeason)
            Ravensguard.Sim.Continental.ContinentTick.Run(cont, Records.Resident.Select(r => new Ravensguard.Sim.Continental.ResidentRegion(r.Coord.Id, r.Economy)).ToArray(), now);
        return Last = new SchedulerReport(joined, paged, requested, hourly, dailyHours / 24, sw.Elapsed.TotalMilliseconds, dailyHours);
    }
}
