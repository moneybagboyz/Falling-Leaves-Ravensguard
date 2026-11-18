using System.Diagnostics;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.World;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.RegionsB;

/// <summary>Part B of the seamless continent: records keyed by region, levels, paging, per-region save sections, the hero crossing a border.</summary>
public class RegionRecordsTests(ITestOutputHelper output)
{
    [Fact]
    public void Records_stay_in_their_regions_and_ids_carry_the_region()
    {
        var rig = new RegionRig();
        var east = rig.Generate(rig.Home.Offset(1, 0));
        var home = rig.Set.HomeWorld!;
        foreach (var rw in new[] { home, east })
        {
            var p = rw.Population.People; var h = rw.Population.Households;
            for (int i = 0; i < p.Count; i++) Assert.Equal(rw.Coord.Id, p.IdOf(i).Region);
            for (int i = 0; i < h.Count; i++) Assert.Equal(rw.Coord.Id, h.IdOf(i).Region);
            foreach (var s in rw.Population.Region.Settlements) Assert.True(s.Id.Region == rw.Coord.Id || s.Id.Counter < 4096, "a settlement has its region's bits, or is a history settlement (global ids)");
            foreach (var f in rw.Population.Region.Fields) Assert.Equal(rw.Coord.Id, f.Id.Region);
        }
        var ids = new HashSet<EntityId>();
        foreach (var rw in new[] { home, east }) for (int i = 0; i < rw.Population.People.Count; i++) Assert.True(ids.Add(rw.Population.People.IdOf(i)), "person ids of two regions never collide");
        Assert.NotEqual(home.Population.People.IdOf(0), east.Population.People.IdOf(0));
        // a region's own businesses and fields live in its own economy
        Assert.NotSame(home.Economy.Production, east.Economy.Production);
        output.WriteLine($"home {home.Coord} {home.Population.People.Count} people, east {east.Coord} {east.Population.People.Count} people");
    }

    [Fact]
    public void A_person_moves_between_regions_deterministically_and_keeps_the_origin_id()
    {
        string Run(out EntityId movedId, out int row)
        {
            var rig = new RegionRig();
            var east = rig.Generate(rig.Home.Offset(1, 0)); var home = rig.Set.HomeWorld!;
            int p = Enumerable.Range(0, home.Population.People.Count).First(i => home.Population.People.IsAlive(i) && home.Population.People.Household[i] >= 0 && home.Population.Age(i) is > 20 and < 50
                && RegionTransfer.CanMovePerson(home, i, GameTime.FromGameHours(home.HourDone)));
            movedId = home.Population.People.IdOf(p);
            int aliveH = (int)home.Sim.Vital.Alive, aliveE = (int)east.Sim.Vital.Alive;
            row = RegionTransfer.MovePerson(home, east, p, -1, GameTime.FromGameHours(home.HourDone));
            Assert.True(row >= 0);
            Assert.Equal(PersonStatus.Departed, home.Population.People.Status[p]);
            Assert.Equal(aliveH - 1, (int)home.Sim.Vital.Alive); Assert.Equal(aliveE + 1, (int)east.Sim.Vital.Alive);
            Assert.Equal(movedId, east.Population.People.IdOf(row));
            Assert.Equal(row, east.Population.People.IndexOf(movedId));
            Assert.Equal(home.Coord.Id, east.Population.People.IdOf(row).Region);
            Assert.Contains(row, east.Population.Households.Members(east.Population.People.Household[row]));
            // the arrival is a normal person: the region keeps ticking and saves with its foreign id
            east.AdvanceTo(east.HourDone + 48);
            var back = RegionWorld.Restore(rig.Sv, east.Coord, east.Population.Region, SaveCodec.DeserializeCompressed<RegionSectionData>(east.CaptureBytes()));
            Assert.Equal(movedId, back.Population.People.IdOf(row));
            Assert.Equal(east.CaptureBytes(), back.CaptureBytes());
            return east.RecordsHash() + home.RecordsHash();
        }
        Assert.Equal(Run(out var id1, out int r1), Run(out var id2, out int r2));
        Assert.Equal(id1, id2); Assert.Equal(r1, r2);
    }

    [Fact]
    public void Tick_levels_are_assigned_by_distance_and_do_not_depend_on_generation_order()
    {
        Assert.Equal(RegionLevel.Live, RegionScheduler.LevelFor(0, true));
        Assert.Equal(RegionLevel.Detailed, RegionScheduler.LevelFor(1, true));
        Assert.Equal(RegionLevel.Daily, RegionScheduler.LevelFor(2, true));
        Assert.Equal(RegionLevel.Paged, RegionScheduler.LevelFor(3, true));
        Assert.Equal(RegionLevel.Abstract, RegionScheduler.LevelFor(0, false));
        // ring 0..1 hold 9 regions, ring 2 the 16 around them
        var home = new RegionCoord(8, 8);
        var levels = Enumerable.Range(0, 256).Select(i => RegionCoord.FromId(i)).GroupBy(r => RegionScheduler.LevelFor(r.Chebyshev(home), true)).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(1, levels[RegionLevel.Live]); Assert.Equal(8, levels[RegionLevel.Detailed]); Assert.Equal(16, levels[RegionLevel.Daily]);

        // two rigs generate the same three regions in opposite orders and run the same day: identical levels and hashes
        string Go(bool reverse)
        {
            var rig = new RegionRig();
            var order = new[] { rig.Home.Offset(1, 0), rig.Home.Offset(-1, 0), rig.Home.Offset(0, 1) };
            foreach (var r in reverse ? order.Reverse() : order) rig.Generate(r);
            var t = GameTime.FromGameHours(rig.Set.HomeWorld!.HourDone + 30);
            rig.Sched.Update(t, rig.Home);
            var l = string.Join(",", rig.Set.Resident.Select(r => r.Coord.Id + ":" + r.Level));
            return l + "|" + rig.Set.ComputeHash();
        }
        Assert.Equal(Go(false), Go(true));
    }

    [Fact]
    public void Daily_regions_step_in_staggered_slices_and_end_where_hourly_stepping_ends()
    {
        var rig = new RegionRig(dailyRing: 2);
        var near = rig.Generate(rig.Home.Offset(1, 0));
        var far = rig.FirstPopulated([rig.Home.Offset(2, 0), rig.Home.Offset(2, 1), rig.Home.Offset(2, -1), rig.Home.Offset(-2, 0), rig.Home.Offset(0, 2), rig.Home.Offset(0, -2), rig.Home.Offset(-2, 1)]);
        long h0 = rig.Set.HomeWorld!.HourDone;
        long end = h0 + 30; end -= (end % 24 == 0) ? 0 : 0;
        var r = rig.Sched.Update(GameTime.FromGameHours(end), rig.Home);
        Assert.Equal(RegionLevel.Daily, far.Level); Assert.Equal(RegionLevel.Detailed, near.Level);
        Assert.Equal(end, near.HourDone);
        Assert.Equal(RegionScheduler.DailyTarget(end, far.Coord.Id), far.HourDone);
        output.WriteLine($"update: {r.HourlySteps} hourly, {r.DaysSteps} day steps, {r.Ms:0} ms");
        // promote: the same region at the same hour has the same records whichever level it ran at
        rig.Sched.Update(GameTime.FromGameHours(end), far.Coord);
        Assert.Equal(end, far.HourDone);
        var ref2 = new RegionRig(dailyRing: 2); var f2 = ref2.Generate(far.Coord);
        ref2.Sched.Update(GameTime.FromGameHours(end), f2.Coord); // it was the hero's own region all the time
        Assert.Equal(f2.RecordsHash(), far.RecordsHash());
    }

    [Fact]
    public void Paging_a_region_out_and_back_in_is_byte_identical_and_the_chunks_go_with_it()
    {
        var rig = new RegionRig();
        var east = rig.Generate(rig.Home.Offset(1, 0));
        rig.Sched.Update(GameTime.FromGameHours(rig.Set.HomeWorld!.HourDone + 26), rig.Home);
        // a chunk entry that lies in the east region (home frame: chunk x 1000 and up)
        var key = new ChunkKey(1005, 10, 0);
        rig.Chunks.AppendDelta(key, [ChunkOp.SetTile(1, 2, 7)]);
        var before = east.CaptureBytes(rig.Chunks); string hash = east.RecordsHash();
        Assert.True(rig.Set.PageOut(east.Coord));
        Assert.Null(rig.Set.TryGet(east.Coord)); Assert.False(rig.Chunks.Contains(key)); Assert.False(rig.Mgr.IsReady(east.Coord));
        Assert.True(rig.Set.IsPaged(east.Coord));
        var sw = Stopwatch.StartNew();
        rig.Set.PageIn(east.Coord); rig.Set.Sync();
        double ms = sw.Elapsed.TotalMilliseconds;
        var back = rig.Set.TryGet(east.Coord)!;
        Assert.NotNull(back); Assert.NotSame(east, back);
        Assert.True(rig.Chunks.Contains(key));
        Assert.Equal(hash, back.RecordsHash());
        Assert.Equal(before, back.CaptureBytes(rig.Chunks));
        Assert.False(rig.Set.Pager.Has(east.Coord.Id), "a resident region has no stale page file");
        output.WriteLine($"page-in (terrain + people + restore) {ms:0} ms, section {before.Length / 1e6:0.00} MB");
        // after paging, the region catches up deterministically (an hour-aligned target)
        long hourBefore = back.HourDone; string hashBefore = back.RecordsHash();
        back.AdvanceTo(back.HourDone + 10);
        Assert.Equal(hourBefore + 10, back.HourDone);
        Assert.NotEqual(hashBefore, back.RecordsHash());
        Assert.Equal(PersonStatus.Alive, back.Population.People.Status[Enumerable.Range(0, back.Population.People.Count).First(i => back.Population.People.Status[i] == PersonStatus.Alive)]);
    }

    [Fact]
    public void A_save_with_three_regions_round_trips_byte_identically_including_paged_ones()
    {
        var rig = new RegionRig();
        rig.Generate(rig.Home.Offset(1, 0)); rig.Generate(rig.Home.Offset(0, 1)); rig.Generate(rig.Home.Offset(-1, 0));
        rig.Sched.Update(GameTime.FromGameHours(rig.Set.HomeWorld!.HourDone + 20), rig.Home);
        Assert.True(rig.Set.PageOut(rig.Home.Offset(0, 1)));          // one of them paged
        var state = rig.State(GameTime.FromGameHours(rig.Set.HomeWorld!.HourDone + 20));
        var content = rig.W.Content; string hash = "contenthash";
        var file1 = WorldSave.Capture(state, hash, "0.1");
        var names = file1.Sections.Keys.Where(k => k.StartsWith("region.") && char.IsDigit(k[7])).ToList();
        output.WriteLine(string.Join(",", names)); Assert.Equal(3, names.Count);
        Assert.Contains("production", file1.Sections.Keys); Assert.Contains("markets", file1.Sections.Keys);
        var bytes1 = SaveCodec.Encode(file1);
        output.WriteLine($"save {bytes1.Length / 1e6:0.0} MB, region sections " + string.Join(", ", names.Select(n => $"{n} {file1.Sections[n].Length / 1e6:0.00} MB")) + $", home people {file1.Sections["people"].Length / 1e6:0.00} MB");

        var state2 = WorldSave.Restore(SaveCodec.Decode(bytes1), content, hash, null, rig.W.Continent, rig.W.History);
        Assert.Equal(3, state2.RegionSections.Count);
        Assert.NotNull(state2.Production); Assert.NotNull(state2.Markets);
        var bytes2 = SaveCodec.Encode(WorldSave.Capture(state2, hash, "0.1"));
        Assert.Equal(bytes1, bytes2);

        // and through a second record set: sections attached, regions restored on approach, resaved
        var mgr = new Ravensguard.Sim.Worlds.RegionManager(rig.W.Continent, rig.W.History, rig.W.Settings, content) { Synchronous = true };
        var set2 = new RegionRecordSet(rig.Sv, mgr, state2.Chunks);
        set2.AttachSections(state2.RegionSections);
        mgr.GenerateNow(rig.Home); set2.Sync();
        foreach (int id in state2.RegionSections.Keys) { mgr.GenerateNow(RegionCoord.FromId(id)); set2.Sync(); }
        Assert.Equal(4, set2.ResidentCount);
        foreach (var rw in set2.Resident.Where(r => r.Coord != rig.Home))
            Assert.Equal(state2.RegionSections[rw.Coord.Id], rw.CaptureBytes(state2.Chunks));
    }

    [Fact]
    public void The_hero_region_changes_at_the_border_and_a_save_across_it_resumes_there()
    {
        var home = new RegionCoord(5, 5);
        var t = new HeroRegionTracker(home);
        Assert.False(t.Update(home.OriginXM + 31_900, home.OriginYM + 16_000));
        Assert.Equal(home, t.Region);
        Assert.False(t.Update(home.OriginXM + 32_100, home.OriginYM + 16_000));      // across: the hero's region follows at once ...
        Assert.Equal(home.Offset(1, 0), t.Region); Assert.Equal(home, t.RingRegion);  // ... the ring region waits for the hysteresis
        Assert.False(t.Update(home.OriginXM + 32_900, home.OriginYM + 16_000));
        Assert.True(t.Update(home.OriginXM + 33_100, home.OriginYM + 16_000));
        Assert.Equal(home.Offset(1, 0), t.RingRegion);
        Assert.False(t.Update(home.OriginXM + 31_900, home.OriginYM + 16_000));       // back over the border: nothing changes
        Assert.Equal(home.Offset(1, 0), t.RingRegion); Assert.Equal(home, t.Region);

        // a live save with the hero 40 m into the east neighbour, and 40 m before it
        var rig = new RegionRig();
        rig.Generate(rig.Home.Offset(1, 0));
        var state = rig.State(GameTime.FromCalendar(rig.Set.HomeWorld!.Population.NowYear, Season.Spring, 2));
        foreach (int dx in new[] { 32_040, 31_960 })
        {
            var streamer = new ChunkStreamer(rig.Mgr, rig.Home, state.Chunks);
            var live = new LiveWorld(streamer, new Cell3(dx, 16_000, 0), state.Clock.Now);
            var file = WorldSave.CaptureLive(state, live, "contenthash");
            var back = WorldSave.Restore(file, rig.W.Content, "contenthash", null, rig.W.Continent, rig.W.History);
            int expect = rig.Home.Offset(dx >= 32_000 ? 1 : 0, 0).Id;
            Assert.Equal(expect, back.Live!.RegionId);
            Assert.Equal(live.CaptureResume().WorldXRaw, back.Live.WorldXRaw);
            var again = new LiveWorld(new ChunkStreamer(rig.Mgr, rig.Home, back.Chunks), back.Live);
            Assert.Equal(live.CaptureResume().WorldXRaw, again.CaptureResume().WorldXRaw);
            Assert.Equal(file.Sections["live"].ToArray(), WorldSave.CaptureLive(back, again, "contenthash").Sections["live"].ToArray());
        }
    }
}
