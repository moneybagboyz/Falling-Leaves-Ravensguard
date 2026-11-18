using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.RegionsB;

/// <summary>
/// The scheduler steps resident regions in parallel; the only things they share are the services and the abstract continent (its caravan table and links).
/// Phase 3 cleanup item: <c>Tick_levels_are_assigned...</c> once threw IndexOutOfRange inside the parallel region step. The cause was the continent's caravan table,
/// a plain sorted dictionary that the home region's worker added caravans to while the neighbours' workers copied it (<c>All.ToList()</c>) in their caravan hour.
/// </summary>
public class RegionParallelStressTests(ITestOutputHelper output)
{
    [Fact]
    public void Caravan_table_survives_many_writers_and_readers()
    {
        var t = new CaravanTable();
        const int Threads = 8, PerThread = 600;
        var errors = 0;
        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, th =>
        {
            try
            {
                for (int i = 0; i < PerThread; i++)
                {
                    var id = EntityId.MakeIn(IdKind.Other, th, (ulong)(0x600_0000 + i));
                    t.Add(new Caravan { Id = id, Home = th, Region = th });
                    int n = 0; foreach (var c in t.All) if (c.Home == th) n++;
                    _ = t.All.ToList(); _ = t.ActiveCount(); _ = t.InRegion(th).Count();
                    if (i % 3 == 0) t.Remove(id);
                    if (n <= 0) Interlocked.Increment(ref errors);
                }
            }
            catch (Exception) { Interlocked.Increment(ref errors); throw; }
        });
        Assert.Equal(0, errors);
        Assert.Equal(Threads * (PerThread - (PerThread + 2) / 3), t.Count);
        // stable order: ascending id whatever order the writers ran in
        var ids = t.All.Select(c => c.Id.Value).ToArray();
        Assert.Equal(ids.OrderBy(x => x).ToArray(), ids);
    }

    [Fact]
    public void Parallel_region_steps_match_a_one_thread_run_over_many_barriers()
    {
        // home and its four neighbours, stepped hour by hour for three days with as many workers as regions, twice, against one worker:
        // the region step must neither throw nor depend on the order the workers ran in
        string Run(int workers)
        {
            var rig = new RegionRig(dailyRing: 2);
            foreach (var off in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) rig.Generate(rig.Home.Offset(off.Item1, off.Item2));
            var sched = new Ravensguard.Sim.Abstract.RegionScheduler(rig.Set) { DailyRing = 2, MaxParallel = workers };
            long h0 = rig.Set.HomeWorld!.HourDone;
            for (long h = h0 + 1; h <= h0 + 72; h++) sched.Update(GameTime.FromGameHours(h), rig.Home);
            var cont = rig.Sv.AbstractContinent!;
            Assert.True(cont.Caravans.Count > 0, "the home region founds caravans while its neighbours walk the table");
            return rig.Set.ComputeHash() + "|" + cont.Caravans.ComputeHash() + "|" + cont.Caravans.Count;
        }
        string one = Run(1);
        for (int i = 0; i < 2; i++) Assert.Equal(one, Run(8));
        output.WriteLine(one);
    }
}
