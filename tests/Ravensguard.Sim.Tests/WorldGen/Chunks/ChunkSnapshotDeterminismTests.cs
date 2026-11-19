using System.Diagnostics;
using Ravensguard.Sim.WorldGen.Chunks;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.WorldGen.ChunkGen;

/// <summary>
/// Chunks are generated and their snapshots compressed on worker threads (<see cref="ChunkStreamer.Update"/> with background work) while other
/// streamers and saves compress too. Whatever the interleaving, the stored bytes must equal those of a chunk made in place on one thread.
/// </summary>
public class ChunkSnapshotDeterminismTests(ChunkWorldFixture fx, ITestOutputHelper output) : IClassFixture<ChunkWorldFixture>
{
    [Fact]
    public void Snapshots_made_on_many_worker_threads_at_once_are_byte_identical_to_those_made_in_place()
    {
        var t = fx.Region.Settlements.OrderByDescending(x => x.TargetPop).First();
        int cx = t.CenterX / 32, cy = t.CenterY / 32;
        var inPlace = new ChunkStreamer(fx.Generator); inPlace.LoadAround(cx, cy, 3);
        var expect = inPlace.Store.ToSection();
        int bad = 0, slow = 0;
        var sw = Stopwatch.StartNew();
        for (int round = 0; round < 3; round++)
            Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
            {
                var b = new ChunkStreamer(fx.Generator);
                var clock = Stopwatch.StartNew();
                while (b.LoadedCount < 49 && clock.Elapsed.TotalSeconds < 600) { b.Update(cx, cy, 2.0, background: true); Thread.Sleep(1); }
                if (b.LoadedCount < 49) Interlocked.Increment(ref slow);            // ran out of time under load: not a determinism fault, reported apart
                else if (!expect.AsSpan().SequenceEqual(b.Store.ToSection())) Interlocked.Increment(ref bad);
            });
        output.WriteLine($"{bad} of 24 streamers differed, {slow} did not finish, {sw.ElapsedMilliseconds} ms");
        Assert.Equal(0, bad);
        Assert.Equal(0, slow);
    }
}
