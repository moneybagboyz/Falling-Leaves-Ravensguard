using System.Collections.Concurrent;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Growth;
using Ravensguard.Sim.Tests.Production;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Review;

/// <summary>
/// The Phase 3 code and bug check, simulation half (docs/reviews/phase-3.md): one regression test or more per finding, each failing before its fix. The reviewer's probes
/// (<c>review4/probe</c>: traveller ids, the job table's counters, the cut report.json) are F2, F5 and F10 here.
/// </summary>
public class Phase3SimReviewTests(ITestOutputHelper output)
{
    // ---------------------------------------------------------------- F1: growth on the worker against chunk generation on the pool

    private static List<LotRecord> Plots(SoakWorld w, int count)
    {
        // the same plots on any copy of the seed's region: each found after the previous one was built, villages in turn
        var reg = w.Pop.Region; var res = new List<LotRecord>();
        var villages = Enumerable.Range(0, reg.Settlements.Count).Where(s => reg.Settlements[s].Class == SettlementClass.Village).ToList();
        for (int k = 0, v = 0; res.Count < count && k < count * 8; k++, v++)
        {
            int s = villages[v % villages.Count];
            if (!RegionGrowth.FindPlot(reg, s, 16, 38, 30, [], out var rect, out byte f)) continue;
            var rec = new LotRecord(s, reg.Settlements[s].Plan.Lots.Count, true, LotUse.Cottage, DwellingType.Cottage, "peasant_cottage", rect, f, 0, 5, 1);
            RegionGrowth.AddLot(reg, w.Pop, rec); res.Add(rec);
        }
        return res;
    }

    private static List<(int X, int Y)> ChunksOf(IEnumerable<LotRecord> lots)
    {
        var keys = new SortedSet<(int, int)>();
        foreach (var l in lots)
            for (int y = (l.Rect.Y - 8) / 32; y <= (l.Rect.Bottom + 8) / 32; y++)
                for (int x = (l.Rect.X - 8) / 32; x <= (l.Rect.Right + 8) / 32; x++) keys.Add((x, y));
        return [.. keys];
    }

    [Fact]
    public void F1_Growth_applying_lots_while_chunks_generate_in_parallel_agrees_with_the_records()
    {
        const int Lots = 10, Rounds = 2;
        // the reference: the same lots built one by one on a twin, every chunk they touch generated at every version
        using var twin = SoakWorld.Create(withTicker: false);
        var fresh = new ChunkGenerator(twin.Pop.Region, twin.Pop, EconFixture.Content);   // the versions are read from this one, built before any lot
        var plots = new List<LotRecord>(); List<(int X, int Y)> chunks;
        var refs = new Dictionary<(int, int), HashSet<string>>();
        {
            using var probe = SoakWorld.Create(withTicker: false);
            plots = Plots(probe, Lots);
            chunks = ChunksOf(plots);
        }
        Assert.Equal(Lots, plots.Count);
        void Reference() { foreach (var c in chunks) { if (!refs.TryGetValue(c, out var set)) refs[c] = set = []; set.Add(fresh.GenerateLevels(c.X, c.Y).ContentHash); } }
        Reference();
        foreach (var rec in plots) { RegionGrowth.AddLot(twin.Pop.Region, twin.Pop, rec); Reference(); }
        var final = chunks.ToDictionary(c => c, c => fresh.GenerateLevels(c.X, c.Y));
        output.WriteLine($"{plots.Count} lots, {chunks.Count} chunks, {refs.Values.Sum(s => s.Count)} reference versions");

        for (int round = 0; round < Rounds; round++)
        {
            using var w = SoakWorld.Create(withTicker: false);
            var reg = w.Pop.Region;
            var gen = new ChunkGenerator(reg, w.Pop, EconFixture.Content);
            var store = new ChunkStore();
            var writer = new ChunkDeltaWriter(gen, store);
            var streamer = new ChunkStreamer(gen, store);
            for (int i = 0; i < chunks.Count; i += 3) streamer.LoadSync(chunks[i].X, chunks[i].Y);     // a third visited before anything is built
            var errors = new ConcurrentQueue<string>(); var seen = new ConcurrentQueue<((int, int) C, string Hash)>();
            int done = 0;
            var growth = new Thread(() =>
            {
                try
                {
                    var rng = new Random(round);
                    foreach (var rec in plots)
                    {
                        RegionGrowth.Change(reg, writer, [new RectM(rec.Rect.X - 6, rec.Rect.Y - 6, rec.Rect.W + 12, rec.Rect.H + 12)], () => RegionGrowth.AddLot(reg, w.Pop, rec));
                        Thread.SpinWait(rng.Next(20_000));
                    }
                }
                catch (Exception e) { errors.Enqueue("growth: " + e); }
                finally { Volatile.Write(ref done, 1); }
            });
            var pool = Enumerable.Range(0, 3).Select(t => new Thread(() =>
            {
                try
                {
                    int n = 0;
                    while (Volatile.Read(ref done) == 0 || n < chunks.Count)
                    {
                        var c = chunks[(n++ * 7 + t * 5) % chunks.Count];
                        seen.Enqueue((c, gen.GenerateLevels(c.X, c.Y).ContentHash));
                    }
                }
                catch (Exception e) { errors.Enqueue($"pool {t}: " + e); }
            })).ToList();
            var visitor = new Thread(() =>
            {
                try
                {
                    // the streamer's thread: first visits of the other chunks while the lots go up, then unload and reload a few
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        if (i % 3 == 0) continue;
                        streamer.LoadSync(chunks[i].X, chunks[i].Y);
                        Thread.SpinWait(5_000);
                    }
                    for (int i = 0; i < chunks.Count; i += 2) { streamer.Unload(chunks[i].X, chunks[i].Y); streamer.LoadSync(chunks[i].X, chunks[i].Y); }
                }
                catch (Exception e) { errors.Enqueue("streamer: " + e); }
            });
            growth.Start(); foreach (var p in pool) p.Start(); visitor.Start();
            growth.Join(); foreach (var p in pool) p.Join(); visitor.Join();
            Assert.True(errors.IsEmpty, string.Join("\n", errors));
            int bad = seen.Count(s => !refs[s.C].Contains(s.Hash));
            output.WriteLine($"round {round}: {seen.Count} chunks generated in parallel, {bad} matching no version of the records");
            Assert.Equal(0, bad);
            // every chunk as the store holds it (first-visit snapshot plus growth's deltas) is the chunk of the grown records
            foreach (var c in chunks)
                foreach (var (z, want) in final[c].Levels)
                {
                    if (want.IsEmpty) continue;
                    var have = store.Rebuild(new ChunkKey(c.X, c.Y, z));
                    Assert.True(have != null, $"chunk {c} level {z} was never recorded");
                    Assert.True(want.ContentHash() == have!.ContentHash(), $"round {round}: chunk {c} level {z} in the store differs from the grown records");
                }
        }
    }
}
