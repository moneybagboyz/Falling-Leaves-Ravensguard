using System.Diagnostics;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Tests.WorldGen.ChunkGen;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen.Chunks;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Live;

/// <summary>Phase 1 review: streaming hitches at 8x and allocation per torch step.</summary>
public class LivePerfTests(ChunkWorldFixture fx, ITestOutputHelper output) : IClassFixture<ChunkWorldFixture>
{
    /// <summary>A west-to-east row of open ground at least <paramref name="len"/> metres long (three cells wide), found on a scratch streamer so the test's own streamer starts cold.</summary>
    private (int X, int Y)? FindOpenRun(int len)
    {
        var scan = new ChunkStreamer(fx.Generator);
        for (int y = 4000; y < 28000; y += 331)
            for (int x0 = 2000; x0 < 30000 - len; x0 += 1777)
            {
                int run = 0;
                for (int x = x0; x < x0 + len; x += 2)
                {
                    if (scan.IsWalkable(x, y) && scan.IsWalkable(x, y - 1) && scan.IsWalkable(x, y + 1) && scan.IsWalkable(x + 1, y)) run += 2; else break;
                }
                if (run >= len) return (x0, y);
                scan.Update(x0 / 32, y / 32, 0); // unload what is far behind
            }
        return null;
    }

    [Fact]
    public void Walking_at_8x_keeps_every_tick_short()
    {
        const int Len = 900;
        var spot = FindOpenRun(Len);
        Assert.NotNull(spot);
        var streamer = new ChunkStreamer(fx.Generator);
        var w = new LiveWorld(streamer, new Cell3(spot!.Value.X, spot.Value.Y, 0), GameTime.FromCalendar(1, Season.Spring, 8, 12));
        w.SetInput(new MoveInput(1, 0, Sprint: true));
        w.SetSpeed(SimSpeed.X8);
        for (int i = 0; i < 40; i++) w.Step(); // warm up the JIT and the first loads
        var sw = new Stopwatch(); var times = new List<double>();
        for (int i = 0; i < 380; i++) { sw.Restart(); w.Step(); times.Add(sw.Elapsed.TotalMilliseconds); }
        double walked = w.OriginX + w.Snapshot.Hero.X.ToDouble() - spot.Value.X;
        times.Sort();
        double max = times[^1], p99 = times[(int)(times.Count * 0.99)], avg = times.Average();
        output.WriteLine($"8x walk {walked:0} m: avg {avg:0.00} ms, p99 {p99:0.00} ms, max {max:0.00} ms, window swaps {w.WindowSwaps}, generated {streamer.GeneratedCount}");
        Assert.True(walked > 600, $"the hero should walk the open run, walked {walked:0} m");
        // before: max 18 ms, p99 11 ms and a window swap at every chunk; the next window is now built off-thread and swapped in at every second chunk
        Assert.True(max < TestBudget.Ms(15), $"slowest tick {max:0.0} ms");
        Assert.True(w.WindowSwaps <= walked / 32 / 2 + 2, $"{w.WindowSwaps} swaps for {walked:0} m");
    }

    [Fact]
    public void The_window_is_not_rebuilt_while_the_hero_wiggles_over_a_chunk_seam_and_follows_him_when_he_goes_on()
    {
        var spot = FindOpenRun(200);
        Assert.NotNull(spot);
        // start next to a chunk boundary so a few metres either way change the hero's chunk
        int sx = (spot!.Value.X / 32 + 1) * 32 - 3;
        var streamer = new ChunkStreamer(fx.Generator);
        var w = new LiveWorld(streamer, new Cell3(sx, spot.Value.Y, 0), GameTime.FromCalendar(1, Season.Spring, 8, 12)) { SynchronousStreaming = true };
        int centre = w.Window!.CenterChunkX;
        for (int rep = 0; rep < 4; rep++)
        {
            w.SetInput(new MoveInput(1, 0, Sprint: true)); for (int i = 0; i < 12; i++) w.Step();
            w.SetInput(new MoveInput(-1, 0, Sprint: true)); for (int i = 0; i < 12; i++) w.Step();
        }
        Assert.Equal(0, w.WindowSwaps);
        Assert.Equal(centre, w.Window!.CenterChunkX);
        // walking on, two chunks out, moves the window
        w.SetInput(new MoveInput(1, 0, Sprint: true));
        for (int i = 0; i < 160 && w.WindowSwaps == 0; i++) w.Step();
        Assert.Equal(1, w.WindowSwaps);
        Assert.True(Math.Abs(w.Window!.CenterChunkX - ChunkStreamer.FloorDiv(w.WorldCell.X, 32)) <= LiveWorld.RecentreDistance);
    }

    [Fact]
    public void The_pathfinder_is_reused_across_window_swaps_and_still_finds_paths()
    {
        var spot = FindOpenRun(300);
        Assert.NotNull(spot);
        var streamer = new ChunkStreamer(fx.Generator);
        var w = new LiveWorld(streamer, new Cell3(spot!.Value.X, spot.Value.Y, 0), GameTime.FromCalendar(1, Season.Spring, 8, 12)) { SynchronousStreaming = true };
        var pf = w.Pathfinder;
        w.SetSpeed(SimSpeed.X8);
        w.SetInput(new MoveInput(1, 0, Sprint: true));
        for (int i = 0; i < 400 && w.WindowSwaps < 2; i++) w.Step();
        Assert.True(w.WindowSwaps >= 2);
        Assert.Same(pf, w.Pathfinder);
        w.SetInput(MoveInput.None); w.SetSpeed(SimSpeed.X1);
        var here = w.Snapshot.Hero.Cell;
        Assert.True(w.CommandMoveTo(new Cell3(here.X + 2, here.Y, here.Z)), "a short path on the new grid");
        for (int i = 0; i < 60 && w.Snapshot.Hero.PathRemaining > 0; i++) w.Step();
        Assert.True(w.Snapshot.Hero.CellX >= here.X + 1, "the hero walked the path on the swapped-in grid");
    }

    [Fact]
    public void An_off_thread_window_swap_does_not_move_the_hero()
    {
        var spot = FindOpenRun(500);
        Assert.NotNull(spot);
        var streamer = new ChunkStreamer(fx.Generator);
        var w = new LiveWorld(streamer, new Cell3(spot!.Value.X, spot.Value.Y, 0), GameTime.FromCalendar(1, Season.Spring, 8, 12));
        w.SetInput(new MoveInput(1, 0, Sprint: true));
        double last = w.OriginX + w.Snapshot.Hero.X.ToDouble();
        for (int i = 0; i < 300; i++)
        {
            w.Step();
            double now = w.OriginX + w.Snapshot.Hero.X.ToDouble();
            Assert.InRange(now - last, -0.001, 0.3); // 5 m/s = 0.25 m per tick, whatever the window does
            last = now;
        }
        Assert.True(w.WindowSwaps >= 1);
    }

    [Fact]
    public void Torch_steps_allocate_little()
    {
        var g = new LevelGrid(WorldWindow.Cells, WorldWindow.Cells, -5, 3);
        g.FillRect(0, 0, WorldWindow.Cells - 1, WorldWindow.Cells - 1, 0, true);
        var w = new LiveWorld(g, new LightGrid(g), new Cell3(100, 100, 0), GameTime.FromCalendar(1, Season.Spring, 8, 23));
        w.SetTorch(true);
        w.SetInput(new MoveInput(1, 0, Walk: true));
        for (int i = 0; i < 40; i++) w.Step();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 400; i++) w.Step();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"400 torch-on steps allocated {bytes:N0} bytes");
        // before: 180 MB (a new 450 KB frame twice per step); the frames are reused and only the torch's square is rewritten
        Assert.True(bytes < 1_000_000, $"{bytes:N0} bytes");
    }
}
