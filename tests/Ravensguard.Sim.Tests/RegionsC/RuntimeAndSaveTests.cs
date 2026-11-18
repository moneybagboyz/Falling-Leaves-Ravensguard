#pragma warning disable xUnit1031 // these tests drive a worker thread on purpose and wait for its tasks
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Tests.Save;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.RegionsC;

public class RuntimeAndSaveTests(ITestOutputHelper output)
{
    private static (SeamWorld W, WorldState State) Game()
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        return (w, NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, null, null));
    }

    [Fact]
    public void The_tick_worker_and_a_synchronous_run_reach_the_same_records_hour_for_hour()
    {
        // G-A7 for the runtime: the same hours, one on the worker thread (as the game runs them), one on the calling thread, give the same world
        var (w, a) = Game();
        var (_, b) = Game();
        var target = GameTime.FromGameHours(a.Abstract!.HourDone + 30);
        using (var ra = new WorldRuntime(a, w.Content, new MemoryRegionPager()))
        using (var rb = new WorldRuntime(b, w.Content, new MemoryRegionPager()))
        {
            rb.AdvanceTo(target);
            ra.Start();
            var home = a.HomeRegion;
            while (ra.HourDone < target.TotalHours) { ra.Pump(target, home.OriginXM + 16000, home.OriginYM + 16000); Thread.Sleep(5); }
            ra.Quiesce();
            Assert.Null(ra.Fault);
            Assert.Equal(rb.Records.HomeWorld!.RecordsHash(), ra.Records.HomeWorld!.RecordsHash());
            Assert.Equal(target.TotalHours, ra.Snapshot.Hour);
        }
    }

    [Fact]
    public void A_barrier_job_runs_on_the_worker_between_hours_and_sees_a_whole_hour()
    {
        var (w, a) = Game();
        using var rt = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        rt.Start();
        var home = a.HomeRegion;
        var target = GameTime.FromGameHours(a.Abstract!.HourDone + 6);
        var seen = new List<long>();
        int mainThread = Environment.CurrentManagedThreadId;
        var jobs = new List<Task<(long Hour, int Thread)>>();
        while (rt.HourDone < target.TotalHours)
        {
            rt.Pump(target, home.OriginXM + 16000, home.OriginYM + 16000);
            jobs.Add(rt.AtBarrier(() => (rt.Records.HomeWorld!.HourDone, Environment.CurrentManagedThreadId)));
            Thread.Sleep(10);
        }
        foreach (var j in jobs) { var (h, t) = j.GetAwaiter().GetResult(); Assert.NotEqual(mainThread, t); seen.Add(h); }
        Assert.Equal(seen.OrderBy(x => x), seen); // jobs see the hours in order: a job never sees a half-stepped hour
        Assert.Equal(rt.HourDone, rt.Records.HomeWorld!.HourDone);
    }

    [Fact]
    public void A_save_of_a_ticked_game_keeps_its_people_as_a_small_difference_and_loads_back_identically()
    {
        var (w, a) = Game();
        using var rt = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        rt.AdvanceTo(GameTime.FromGameHours(a.Abstract!.HourDone + 72));
        var whole = SaveCodec.Encode(WorldSave.Capture(a, "h", "0.1"));
        a.Content = null; // no baseline: the people go in whole
        var big = SaveCodec.Encode(WorldSave.Capture(a, "h", "0.1"));
        output.WriteLine($"save with the people as a difference {whole.Length / 1e6:0.00} MB, whole {big.Length / 1e6:0.00} MB");
        Assert.True(whole.Length < big.Length - 400_000, "the diff saves most of the people section");
        var file = SaveCodec.Decode(whole);
        var back = WorldSave.Restore(file, w.Content, "h", null, w.Continent, w.History);
        Assert.Equal(a.Population.ComputeHash(), back.Population.ComputeHash());
        Assert.Equal(a.Population.ComputeSimHash(), back.Population.ComputeSimHash());
    }

    [Fact]
    public void The_resource_layer_diff_restores_every_stock_and_last_visit_exactly()
    {
        var (w, a) = Game();
        using var rt = new WorldRuntime(a, w.Content, new MemoryRegionPager());
        var home = rt.Records.HomeWorld!;
        home.AdvanceTo(home.HourDone + 24 * 20); // three weeks: regrowth moves most rows, work takes from some
        var prod = home.Economy.Production;
        // some rows nobody would regrow by rule: explicit values and a visit day
        for (int i = 0; i < prod.Resources!.Count; i += 997) prod.Resources.Take(i, 12345, 77);
        var save = ProductionSave.Capture(prod);
        Assert.True(save.ResClass.Length > 0 && save.ResDiffRow.Length == 0, "the class diff is used");
        var bytes = SaveCodec.SerializeCompressed(save);
        output.WriteLine($"production section {bytes.Length / 1000} KB, {save.ResDiffStock.Length} explicit rows of {save.ResClass.Length}");
        var again = SaveCodec.DeserializeCompressed<ProductionSave>(bytes);
        var content = ProductionContent.Load(w.Content, home.Sim.Economy.Goods);
        var markets = new Ravensguard.Sim.Economy.Markets.MarketSystem(home.Sim.Economy, home.Population, home.Sim.Seed);
        var restored = again.Restore(home.Sim.Economy, home.Population, content, home.Sim.Seed, markets);
        var x = prod.Resources.Export(); var y = restored.Resources!.Export();
        Assert.True(x.Stock.AsSpan().SequenceEqual(y.Stock) && x.LastDay.AsSpan().SequenceEqual(y.LastDay) && x.Cap.AsSpan().SequenceEqual(y.Cap) && x.Tile.AsSpan().SequenceEqual(y.Tile));
        Assert.True(bytes.Length < 600_000, $"the production section is {bytes.Length / 1000} KB");
    }

    [Fact]
    public void Autosave_and_Ironman_slots_belong_to_a_run_so_two_runs_of_one_world_never_overwrite_each_other()
    {
        using var d = new TempDir();
        var w = SeamWorld.Get(1234);
        var (_, st) = Game();
        using var rt = new WorldRuntime(st, w.Content, new MemoryRegionPager());
        var a = new SaveWriter(d.Path, "aldric-1"); var b = new SaveWriter(d.Path, "bera-2");
        var f1 = WorldSave.Capture(st, "h", "0.1"); var f2 = WorldSave.Capture(st, "h", "0.1");
        a.WriteAutosaveAsync(f1).GetAwaiter().GetResult(); b.WriteAutosaveAsync(f2).GetAwaiter().GetResult();
        a.WriteIronmanAsync(f1).GetAwaiter().GetResult(); a.WriteIronmanAsync(f1).GetAwaiter().GetResult();
        Assert.True(File.Exists(SaveWriter.AutosavePath(d.Path, 1, "aldric-1")) && File.Exists(SaveWriter.AutosavePath(d.Path, 1, "bera-2")));
        Assert.False(File.Exists(SaveWriter.AutosavePath(d.Path, 1)), "no shared slot is written");
        Assert.True(File.Exists(SaveWriter.IronmanPath(d.Path, "aldric-1")) && File.Exists(SaveWriter.IronmanBackupPath(d.Path, 1, "aldric-1")));
        Assert.False(File.Exists(SaveWriter.IronmanPath(d.Path, "bera-2")));
    }

    [Fact]
    public void The_ring_region_follows_a_hero_walking_along_a_border_only_once_it_is_a_kilometre_beyond_it()
    {
        var home = new RegionCoord(5, 5);
        var t = new HeroRegionTracker(home);
        // 500 m inside the east neighbour, walking south for 40 km: still the home ring until the hero is 1,000 m beyond the home square on some axis
        long x = home.OriginXM + 32_500;
        Assert.False(t.Update(x, home.OriginYM + 20_000));
        Assert.False(t.Update(x, home.OriginYM + 31_900));
        Assert.False(t.Update(x, home.OriginYM + 32_900)); // in the south-east region, 900 m south of the home square
        Assert.True(t.Update(x, home.OriginYM + 33_100));  // 1,100 m beyond: the ring region moves to where the hero is
        Assert.Equal(home.Offset(1, 1), t.RingRegion);
    }

    [Fact]
    public void The_chunk_store_survives_a_streamer_and_a_page_out_using_it_at_once()
    {
        var store = new ChunkStore();
        var content = new ChunkContent();
        var errors = new List<Exception>();
        void Work(int offset)
        {
            try
            {
                for (int i = 0; i < 300; i++)
                {
                    var k = new ChunkKey(offset + i, i % 7, 0);
                    store.Contains(k); store.Count.ToString(); _ = store.Keys.Count();
                    store.RemoveWhere(key => key.X == offset + i - 3);
                    store.ToSection(key => key.X >= offset && key.X < offset + 5);
                }
            }
            catch (Exception e) { lock (errors) errors.Add(e); }
        }
        var ts = new[] { new Thread(() => Work(0)), new Thread(() => Work(1000)), new Thread(() => Work(2000)) };
        foreach (var t in ts) t.Start();
        foreach (var t in ts) t.Join();
        Assert.Empty(errors);
        _ = content;
    }
}
