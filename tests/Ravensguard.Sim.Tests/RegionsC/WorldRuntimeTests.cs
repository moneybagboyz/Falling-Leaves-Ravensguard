using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.RegionsC;

/// <summary>Part C: the game runs the world. A headless game on the real runtime (worker thread, barrier snapshots, workers building neighbours), driven like the street view drives it.</summary>
[Collection(TimingCollection.Name)] // frame and tick timing: run alone so other tests don't share the CPU
public class WorldRuntimeTests(ITestOutputHelper output)
{
    private const string Hash = "content-hash";

    private static WorldState NewGame(SeamWorld w, RegionCoord home)
    {
        var dir = ContinentSettings.FindBaseContent()!;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = w.Seed;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = w.Seed;
        var rs = RegionSettings.LoadDefaults(dir); rs.Seed = w.Seed;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, rs);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        return NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, rs, home.CellX, home.CellY, pop, null, null);
    }

    /// <summary>Takes a save the way the street view does: the hero's state now, the world's copy on the tick worker, the encoding on the caller.</summary>
    private static byte[] Save(WorldRuntime rt, LiveWorld live)
    {
        var resume = live.CaptureResume();
        var cap = rt.AtBarrier(() => WorldSave.SnapshotLive(rt.State, resume, Hash)).GetAwaiter().GetResult();
        return SaveCodec.Encode(cap.Encode());
    }

    [Fact]
    public void A_new_game_ticks_three_days_at_8x_across_a_border_and_saves_loads_and_saves_identically()
    {
        var (w, home, route) = SeamlessStreamingTests.FindRoadRoute(1500, 1500);
        Assert.NotNull(route);
        var path = SeamlessStreamingTests.Resample(route!, 30);
        var state = NewGame(w!, home);
        var clock0 = state.Clock.Now; // a new game's opening hour
        using var rt = new WorldRuntime(state, w!.Content, new MemoryRegionPager());
        rt.Start();
        Assert.NotNull(state.Production); Assert.NotNull(state.Markets); Assert.NotNull(state.Farming); // the economy a new game used to lack
        var streamer = new ChunkStreamer(rt.Manager, home, state.Chunks);
        int ox = home.OriginXM, oy = home.OriginYM;
        var start = RegionSpawn.NearestWalkable(streamer, (int)(path[0].X - ox), (int)(path[0].Y - oy), 12, paved: false);
        Assert.NotNull(start);
        var live = new LiveWorld(streamer, start!.Value, clock0) { SynchronousStreaming = true };
        live.SetSpeed(SimSpeed.X8);
        rt.PlaceHero(ox + live.WorldCell.X, oy + live.WorldCell.Y);
        var bot = new SeamlessStreamingTests.RoadWalker(live, path.Select(p => (X: p.X - ox, Y: p.Y - oy)).ToList());

        const int Ticks = 3 * 24 * 3600 / 8; // three game days of 20 Hz ticks at 8x (8 game seconds each)
        var pump = new List<double>(Ticks);
        var regionsSeen = new HashSet<int>();
        var wall = Stopwatch.StartNew();
        for (int t = 0; t < Ticks; t++)
        {
            bot.Steer();
            live.Step();
            var (wx, wy) = live.WorldCell;
            long sw0 = Stopwatch.GetTimestamp();
            rt.Pump(live.Clock.Now, ox + wx, oy + wy);
            pump.Add(Stopwatch.GetElapsedTime(sw0).TotalMilliseconds);
            regionsSeen.Add(rt.Hero.Region.Id);
            if (rt.Manager.IsBusy) Thread.Sleep(50); // a neighbour is building: tick at the game's own pace meanwhile
            if (t % 2000 == 0 && Environment.GetEnvironmentVariable("WALKLOG") is { } f) File.AppendAllText(f, $"tick {t} wall {wall.Elapsed.TotalSeconds:0}s hour {rt.HourDone} of {live.Clock.Now.TotalHours} x={ox + wx}\n");
        }
        double walkS = wall.Elapsed.TotalSeconds;
        // the worker may be behind the clock: let it catch up (the game would simply go on drawing)
        var now = live.Clock.Now;
        var catchUp = Stopwatch.StartNew();
        while (rt.HourDone < now.TotalHours && catchUp.Elapsed.TotalMinutes < 6) { rt.Pump(now, ox + live.WorldCell.X, oy + live.WorldCell.Y); Thread.Sleep(20); }
        rt.Quiesce();
        long hourAtSave = rt.HourDone;
        Assert.Null(rt.Fault);
        Assert.True(rt.HourDone >= now.TotalHours, $"the tick caught up: hour {rt.HourDone} of {now.TotalHours}");
        var snap = rt.Snapshot;
        int maxAt = pump.IndexOf(pump.Max());
        var warm = pump.Skip(200).Order().ToList(); // the first calls pay for JIT
        pump.Sort();
        output.WriteLine($"3 game days at 8x in {walkS:0} s wall ({Ticks} ticks): hero regions {string.Join(",", regionsSeen.Select(i => RegionCoord.FromId(i).ToString()))}; hero ended at {live.WorldCell}");
        output.WriteLine($"main thread Pump: avg {pump.Average() * 1000:0.0} us, p99 {pump[(int)(pump.Count * 0.99)] * 1000:0.0} us, max {pump[^1]:0.000} ms at call {maxAt} ({rt.Pumps} calls)");
        output.WriteLine($"tick worker: {snap.HoursStepped} hours stepped, last {snap.HourMs:0} ms, slowest {snap.MaxHourMs:0} ms; resident {snap.Resident} ({snap.Detailed} detailed), paged {snap.Paged}; home alive {snap.HomeAlive}, {snap.HomeHouseholds} households");
        Assert.True(regionsSeen.Count >= 2, "the hero crossed a border");
        Assert.True(snap.Resident >= 2, "a neighbour joined the scheduler with its economy");
        // The 99.9th percentile is the real budget; the single worst pump can include a process-wide pause when the whole suite shares the CPU (seen: 25 ms), so its bound only catches gross stalls.
        Assert.True(warm[(int)(warm.Count * 0.999)] < TestBudget.Ms(1) && warm[^1] < TestBudget.Ms(60), $"Pump cost {warm[^1]:0.00} ms on the main thread after warm-up (99.9th {warm[(int)(warm.Count * 0.999)]:0.00})");
        foreach (var r in rt.Records.Resident) Assert.True(r.Sim.State.HourDone >= now.TotalHours - 24, $"region {r.Coord} is at hour {r.HourDone}, clock {now.TotalHours}");

        // save, load, save: identical bytes
        var a = Save(rt, live);
        var file = SaveCodec.Decode(a);
        output.WriteLine($"save {a.Length / 1e6:0.00} MB; sections: " + string.Join(", ", file.Sections.OrderByDescending(s => s.Value.Length).Take(8).Select(s => $"{s.Key} {s.Value.Length / 1000} KB")));
        rt.Dispose();

        var st2 = WorldSave.Restore(file, w.Content, Hash, null, w.Continent, w.History);
        st2.Content = w.Content;
        using var rt2 = new WorldRuntime(st2, w.Content, new MemoryRegionPager());
        rt2.Start();
        var streamer2 = new ChunkStreamer(rt2.Manager, home, st2.Chunks);
        var live2 = new LiveWorld(streamer2, st2.Live!) { SynchronousStreaming = true };
        rt2.PlaceHero(ox + live2.WorldCell.X, oy + live2.WorldCell.Y);
        rt2.Quiesce();
        var b = Save(rt2, live2);
        Assert.Equal(a.Length, b.Length);
        Assert.True(a.AsSpan().SequenceEqual(b), "save -> load -> save is byte-identical");
        // and the loaded game goes on ticking
        rt2.Pump(live2.Clock.Now + Duration.FromGameHours(2), ox + live2.WorldCell.X, oy + live2.WorldCell.Y);
        rt2.Quiesce();
        Assert.True(rt2.HourDone > hourAtSave, "the loaded game steps on from the saved hour");
        Assert.Null(rt2.Fault);
    }
}
