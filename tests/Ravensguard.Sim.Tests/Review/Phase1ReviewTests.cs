using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Review;

/// <summary>Regression tests for the verified Phase 1 code review findings (simulation side).</summary>
public class Phase1ReviewTests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ 6: landmark ordering without Math.Log

    [Fact]
    public void Landmark_order_is_an_integer_weighted_draw()
    {
        string[] names = ["a", "b", "c"];
        int[] w = [1, 1, 8];
        var firsts = new int[3];
        const int Trials = 4000;
        for (int t = 0; t < Trials; t++)
        {
            var order = LandmarkPlacer.WeightedOrder(names, n => w[Array.IndexOf(names, n)], k => DetRng.Mix((ulong)t * 977 + (ulong)k));
            Assert.Equal(names.OrderBy(x => x), order.OrderBy(x => x)); // every item exactly once
            firsts[Array.IndexOf(names, order[0])]++;
        }
        output.WriteLine($"first place over {Trials} draws: a {firsts[0]}, b {firsts[1]}, c {firsts[2]} (weights 1:1:8)");
        Assert.InRange(firsts[2], Trials * 75 / 100, Trials * 85 / 100);
        Assert.InRange(firsts[0], Trials * 6 / 100, Trials * 14 / 100);
        // the same draws give the same order, and a zero or negative weight counts as 1
        Assert.Equal(LandmarkPlacer.WeightedOrder(names, _ => 0, k => (ulong)k * 7), LandmarkPlacer.WeightedOrder(names, _ => 1, k => (ulong)k * 7));
        // cumulative ranges: total 10, draw 9 hits the last item, draw 0 the first
        Assert.Equal("c", LandmarkPlacer.WeightedOrder(names, n => w[Array.IndexOf(names, n)], k => 9UL)[0]);
        Assert.Equal("a", LandmarkPlacer.WeightedOrder(names, n => w[Array.IndexOf(names, n)], k => 0UL)[0]);
    }

    // ------------------------------------------------------------------ 7: roads

    [Theory]
    [InlineData(1UL)]
    [InlineData(1234UL)]
    public void Road_edges_are_at_least_as_long_as_the_straight_line_between_their_nodes(ulong seed)
    {
        var r = TestWorld.Get(seed).Region;
        Assert.NotEmpty(r.RoadEdges);
        foreach (var e in r.RoadEdges)
        {
            var a = r.RoadNodes[e.A]; var b = r.RoadNodes[e.B];
            double straight = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
            Assert.True(e.LengthM >= (int)straight - 2, $"edge {e.Id} {a.Id}->{b.Id}: length {e.LengthM} m, straight line {straight:0} m");
        }
    }

    // ------------------------------------------------------------------ 8: casts in the prop generators

    [Fact]
    public void Shift_and_mod_stays_in_range_for_every_hash()
    {
        ulong h = 0x1234_5678_9ABC_DEF0UL;
        for (int i = 0; i < 20000; i++)
        {
            h = DetRng.Mix(h + (ulong)i);
            foreach (int n in new[] { 3, 15, 29, 1 })
            {
                int v = DetRng.ShiftMod(h, 8, n);
                Assert.InRange(v, 0, n - 1);
                Assert.Equal((int)((h >> 8) % (ulong)n), v);
            }
        }
        // the old (int)(h >> 8) % 3 went negative for half of all hashes
        ulong neg = 0x80UL << 32;
        Assert.True((int)(neg >> 8) % 3 < 0);
        Assert.InRange(DetRng.ShiftMod(neg, 8, 3), 0, 2);
    }

    [Fact]
    public void Site_decoration_props_stay_within_their_radius_and_headstones_have_a_valid_variant()
    {
        var w = TestWorld.Get(1);
        var gen = ChunkGenerator.For(w.Region, Populator.Populate(w.Region, w.History, w.Content), w.Content);
        var streamer = new ChunkStreamer(gen);
        int checkedSites = 0, checkedProps = 0;
        foreach (var kind in new[] { SiteKind.Ruin, SiteKind.Battlefield })
        {
            int rad = kind == SiteKind.Battlefield ? 14 : 7;
            var sites = w.Region.Sites.Where(s => s.Kind == kind).ToList();
            var prop = kind == SiteKind.Battlefield ? PropKind.SiteBattlefield : PropKind.SiteRuin;
            foreach (var s in sites.Take(6))
            {
                checkedSites++;
                int cx0 = ChunkStreamer.FloorDiv(s.X - rad, 32), cx1 = ChunkStreamer.FloorDiv(s.X + rad, 32), cy0 = ChunkStreamer.FloorDiv(s.Y - rad, 32), cy1 = ChunkStreamer.FloorDiv(s.Y + rad, 32);
                for (int cy = cy0 - 1; cy <= cy1 + 1; cy++)
                    for (int cx = cx0 - 1; cx <= cx1 + 1; cx++)
                    {
                        var c = streamer.LoadSync(cx, cy);
                        foreach (var p in c.Level(0).Props.Values.Where(p => p.Kind == prop))
                        {
                            int wx = cx * 32 + p.X, wy = cy * 32 + p.Y;
                            // each decoration belongs to some site of this kind and lies within its radius
                            Assert.Contains(sites, o => Math.Abs(o.X - wx) <= rad && Math.Abs(o.Y - wy) <= rad);
                            checkedProps++;
                        }
                    }
            }
        }
        // churchyards: grave variants are 0..2
        int headstones = 0;
        foreach (var t in w.Region.Settlements.Where(s => s.Plan.ChurchLot >= 0).Take(6))
        {
            var lot = t.Plan.Lots[t.Plan.ChurchLot];
            for (int cy = ChunkStreamer.FloorDiv(lot.Rect.Y - 20, 32); cy <= ChunkStreamer.FloorDiv(lot.Rect.Bottom + 20, 32); cy++)
                for (int cx = ChunkStreamer.FloorDiv(lot.Rect.X - 20, 32); cx <= ChunkStreamer.FloorDiv(lot.Rect.Right + 20, 32); cx++)
                    foreach (var p in streamer.LoadSync(cx, cy).Level(0).Props.Values.Where(p => p.Kind == PropKind.Headstone)) { Assert.InRange((int)p.Variant, 0, 2); headstones++; }
        }
        output.WriteLine($"{checkedSites} sites, {checkedProps} decoration props, {headstones} headstones checked");
        Assert.True(checkedProps > 0);
    }

    [Fact]
    public void Trees_are_spread_evenly_over_the_rows_of_a_chunk()
    {
        var w = TestWorld.Get(1);
        var gen = ChunkGenerator.For(w.Region, Populator.Populate(w.Region, w.History, w.Content), w.Content);
        var rows = new int[32]; int total = 0;
        // chunks in the thickest forest of the region
        var chunks = Enumerable.Range(0, RegionData.Tiles).Where(t => w.Region.Forest[t] >= 90 && w.Region.Water[t] == 0 && w.Region.Built[t] == 0)
            .Select(t => (X: t % RegionData.Size * RegionData.TileM / 32, Y: t / RegionData.Size * RegionData.TileM / 32)).Distinct().Take(40).ToList();
        Assert.True(chunks.Count >= 10, "the region should have forest");
        foreach (var (cx, cy) in chunks)
            foreach (var kv in gen.GenerateLevels(cx, cy).Levels[0].Objects.Values)
            {
                var p = PropRecord.Decode(kv.Data);
                if (p.Kind != PropKind.Tree) continue;
                rows[p.Y]++; total++;
            }
        double mean = total / 32.0;
        output.WriteLine($"{total} trees in {chunks.Count} chunks; per-row min {rows.Min()}, max {rows.Max()}, mean {mean:0.0}");
        Assert.True(total > 500);
        Assert.All(rows, n => Assert.InRange(n, mean * 0.55, mean * 1.6));
    }

    // ------------------------------------------------------------------ 11: ages

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void Nobody_under_sixteen_holds_a_craft_or_management_job_and_nobody_under_twelve_works(ulong seed)
    {
        var w = TestWorld.Get(seed);
        var pop = Populator.Populate(w.Region, w.History, w.Content);
        int children = 0, young = 0;
        for (int i = 0; i < pop.People.Count; i++)
        {
            int age = pop.Age(i);
            var occ = pop.People.Occupation[i];
            if (occ == Occupation.None) continue;
            Assert.True(age >= OccupationInfo.MinWorkAge(occ), $"person {i} is {age} and works as {occ}");
            if (age < 16) { young++; Assert.True(occ is Occupation.Servant or Occupation.Labourer or Occupation.Farmer or Occupation.Ploughman or Occupation.Shepherd or Occupation.Spinner or Occupation.Fisher or Occupation.Woodcutter or Occupation.CharcoalBurner, $"{age}-year-old {occ}"); }
            children++;
        }
        output.WriteLine($"seed {seed}: {children} workers, {young} of them 12-15");
        Assert.True(young > 0, "some 12 to 15 year olds help out");
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(7UL)]
    public void No_notable_mother_was_older_than_45_at_a_birth(ulong seed)
    {
        var h = TestWorld.Get(seed).History;
        int checkedKids = 0;
        foreach (var kid in h.Notables)
        {
            if (kid.Mother.Value == 0) continue;
            var mother = h.GetNotable(kid.Mother);
            if (mother == null) continue;
            int age = kid.BirthYear - mother.BirthYear;
            Assert.InRange(age, 15, HistoryGenerator.MaxMotherAge);
            checkedKids++;
        }
        output.WriteLine($"seed {seed}: {checkedKids} births checked");
        Assert.True(checkedKids > 0);
    }

    // ------------------------------------------------------------------ 12: the front cache

    [Fact]
    public void Concurrent_readers_never_get_another_hours_fronts()
    {
        var cw = new ContinentalWeather(5, WeatherSettings.LoadDefaults(ContinentSettings.FindBaseContent()));
        long[] hours = [1000, 1001, 3300, 3301, 9000, 9001, 14000];
        var bad = 0;
        Parallel.For(0, 8, t =>
        {
            for (int i = 0; i < 4000; i++)
            {
                long hour = hours[(i * 3 + t) % hours.Length];
                foreach (var f in cw.ActiveFronts(hour)) if (!f.ActiveAt(hour)) Interlocked.Increment(ref bad);
            }
        });
        Assert.Equal(0, bad);
    }
}
