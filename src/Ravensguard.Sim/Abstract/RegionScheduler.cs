using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Abstract;

/// <summary>What one <see cref="RegionScheduler.Update"/> did (counts and wall time), for the perf report and the tests.</summary>
public sealed record SchedulerReport(int Joined, int PagedOut, int PageInRequested, int HourlySteps, int DaysSteps, double Ms);

/// <summary>
/// Drives each generated region's abstract tick at its level (seamless-regions spec 3.4, 3.5; abstract spec 3.3). A region's level is a pure function of its
/// Chebyshev distance to the hero's ring region and of whether it is generated, so it never depends on the order regions were visited or generated:
/// ring 0 is <see cref="RegionLevel.Live"/> and ring 1 <see cref="RegionLevel.Detailed"/> (both stepped every game hour), ring 2 <see cref="RegionLevel.Daily"/> (stepped
/// in whole days when the day turns), anything further is <see cref="RegionLevel.Paged"/> (written to the pager, dropped from memory, caught up on return),
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
            if (Records.TryGet(rc) == null && rc.Chebyshev(ringRegion) <= DailyRing) { Records.PageIn(rc, rc.Chebyshev(ringRegion)); requested++; }
        }
        long hour = now.TotalHours;
        long dayStart = hour - hour % 24;
        int hourly = 0, days = 0;
        var work = Records.Resident;
        long[] before = new long[work.Count];
        for (int i = 0; i < work.Count; i++) before[i] = work[i].HourDone;
        var opt = new ParallelOptions { MaxDegreeOfParallelism = MaxParallel };
        Parallel.For(0, work.Count, opt, i =>
        {
            var rw = work[i];
            long target = rw.Level is RegionLevel.Live or RegionLevel.Detailed ? hour : dayStart;
            rw.AdvanceTo(target);
        });
        for (int i = 0; i < work.Count; i++)
        {
            long steps = work[i].HourDone - before[i];
            if (work[i].Level == RegionLevel.Daily) days += (int)(steps / 24); else hourly += (int)steps;
        }
        return Last = new SchedulerReport(joined, paged, requested, hourly, days, sw.Elapsed.TotalMilliseconds);
    }
}
