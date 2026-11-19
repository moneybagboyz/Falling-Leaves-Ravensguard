#pragma warning disable xUnit1031 // these tests drive a worker thread on purpose and wait for its tasks
using System.Diagnostics;
using MessagePack;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.RegionsB;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.RegionsC;

/// <summary>Regression tests for the phase 2 review of the seamless continent, saves and runtime (docs/reviews/phase-2.md): each failed before its fix.</summary>
[Collection(TimingCollection.Name)] // contains barrier-timing budgets: run alone
public class Phase2ReviewTests(ITestOutputHelper output)
{
    private const string Hash = "h";

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

    /// <summary>The first region near home that generates with no people (sea or wilderness: terrain only, no records).</summary>
    private static RegionCoord FindRecordLess(RegionRig rig)
    {
        for (int id = 0; id < RegionCoord.Count; id++)
        {
            var rc = RegionCoord.FromId(id);
            if (Math.Abs(rc.X - rig.Home.X) > 2 || Math.Abs(rc.Y - rig.Home.Y) > 2) continue;
            rig.Mgr.GenerateNow(rc); rig.Set.Sync();
            if (rig.Set.TryGet(rc) == null) return rc;
        }
        throw new InvalidOperationException("no record-less region near home for this seed");
    }

    private static ChunkContent Tile(int i) { var c = new ChunkContent(); c.Tiles[i] = 3; return c; }

    // ------------------------------------------------------------------ 1. chunks of record-less regions are saved

    [Fact]
    public void Chunk_snapshots_and_deltas_in_a_region_without_records_are_saved_and_come_back()
    {
        var rig = new RegionRig();
        var empty = FindRecordLess(rig);
        int dx = empty.X - rig.Home.X, dy = empty.Y - rig.Home.Y;
        var deltaKey = new ChunkKey(dx * 1000 + 5, dy * 1000 + 5, 0);
        var visitKey = new ChunkKey(dx * 1000 + 9, dy * 1000 + 9, 0);
        rig.Chunks.AppendDelta(deltaKey, [ChunkOp.SetTile(1, 2, 7)]);
        Assert.True(rig.Chunks.RecordFirstVisit(visitKey, ChunkGenerator.Version, "content", Tile(4)));
        Assert.Null(rig.Set.TryGet(empty)); // the setup: this region has no records, so no region section of its own

        var file = WorldSave.Capture(rig.State(GameTime.FromGameHours(rig.Set.HomeWorld!.HourDone + 1)), Hash, "0.1");
        var back = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(file)), rig.W.Content, Hash, null, rig.W.Continent, rig.W.History);
        Assert.True(back.Chunks.Contains(deltaKey), "a delta in a record-less region survives the save");
        Assert.Equal(1, back.Chunks.DeltaOpCount(deltaKey));
        Assert.True(back.Chunks.Contains(visitKey) && back.Chunks.GetSnapshot(visitKey) != null, "so does a first-visit snapshot");
        Assert.Equal(ChunkGenerator.Version, back.Chunks.GetSnapshot(visitKey)!.GeneratorVersion);
        Assert.Equal("content", back.Chunks.GetSnapshot(visitKey)!.ContentHash);
    }

    // ------------------------------------------------------------------ 4. one consistent view under a forced interleaving

    /// <summary>A pager that lets a test run code at the moment a save has read the ids (the instant the main thread would attach a region).</summary>
    private sealed class HookPager : IRegionPager
    {
        private readonly MemoryRegionPager _m = new();
        public Action? OnIds { get; set; }
        public void Write(int id, byte[] s) => _m.Write(id, s);
        public byte[]? Read(int id) => _m.Read(id);
        public bool Has(int id) => _m.Has(id);
        public void Delete(int id) => _m.Delete(id);
        public IReadOnlyList<int> Ids { get { var ids = _m.Ids; var h = OnIds; if (h != null) { OnIds = null; h(); } return ids; } }
    }

    [Fact]
    public void A_region_the_main_thread_attaches_during_a_save_is_neither_lost_nor_loses_its_page_file()
    {
        var pager = new HookPager();
        var rig = new RegionRig(pager: pager);
        var east = rig.Generate(rig.Home.Offset(1, 0));
        rig.Sched.Update(GameTime.FromGameHours(rig.Set.HomeWorld!.HourDone + 3), rig.Home);
        int id = east.Coord.Id;
        Assert.True(rig.Set.PageOut(east.Coord));
        Assert.True(pager.Has(id));

        // the main thread attaches the region (built from its page file) at the very moment the save has listed the pager's ids
        var built = new ManualResetEventSlim();
        var inner = rig.Mgr.RecordsFactory!;
        rig.Mgr.RecordsFactory = (at, t, p) => { var r = inner(at, t, p); built.Set(); return r; };
        Thread? main = null;
        pager.OnIds = () =>
        {
            main = new Thread(() => rig.Mgr.GenerateNow(east.Coord)) { Name = "main-thread-stand-in" };
            main.Start();
            Assert.True(built.Wait(60_000));
            Thread.Sleep(300); // an unguarded attach has finished by now; a guarded one waits for the save
        };
        var file = WorldSave.Capture(rig.State(GameTime.FromGameHours(rig.Set.HomeWorld!.HourDone + 1)), Hash, "0.1");
        main!.Join();
        Assert.Contains(WorldSave.RegionSection(id), file.Sections.Keys);
        Assert.NotNull(rig.Set.TryGet(east.Coord));
        Assert.False(pager.Has(id), "the attach removed the file after the save had read it");
    }

    // ------------------------------------------------------------------ 3. catch-up does not depend on where it is stopped

    private static RegionWorld Reopen(RegionRig rig, RegionWorld rw, byte[] section)
        => RegionWorld.Restore(rig.Sv, rw.Coord, rw.Population.Region, SaveCodec.DeserializeCompressed<RegionSectionData>(section));

    [Fact]
    public void Catching_up_gives_the_same_records_whatever_hours_it_is_stopped_at()
    {
        var rig = new RegionRig();
        var east = rig.FirstPopulated([rig.Home.Offset(1, 0), rig.Home.Offset(0, 1), rig.Home.Offset(-1, 0)]);
        east.AdvanceTo(east.HourDone + 6);
        var section = east.CaptureBytes(rig.Chunks);
        long t0 = east.HourDone, target = t0 + 24 * 8; // a gap well past FullReplayHours

        var direct = Reopen(rig, east, section); direct.AdvanceTo(target);
        var staged = Reopen(rig, east, section); foreach (long stop in new[] { t0 + 7, t0 + 100, target - 3, target }) staged.AdvanceTo(stop);
        var hourly = Reopen(rig, east, section); for (long h = t0 + 1; h <= target; h++) hourly.AdvanceTo(h);
        Assert.Equal(direct.RecordsHash(), staged.RecordsHash());
        Assert.Equal(direct.RecordsHash(), hourly.RecordsHash());
        Assert.Equal(CoarseOff, direct.CoarseUntilHour); // no anchor: every hour runs every phase, so the result is the exact hourly truth

        // with an anchor (the hour the region was asked for), the old hours of the gap run coarse, on the same hours whatever the stops
        void Anchor(RegionWorld r) => r.SetCatchUpAnchor(target);
        var a = Reopen(rig, east, section); Anchor(a); Assert.True(a.CoarseUntilHour > t0 && a.CoarseUntilHour % 24 == 0, "the coarse/full boundary is a day boundary");
        var b = Reopen(rig, east, section); Anchor(b);
        a.AdvanceTo(target);
        foreach (long stop in new[] { t0 + 5, t0 + 70, a.CoarseUntilHour - 1, a.CoarseUntilHour + 1, target - 11, target }) b.AdvanceTo(stop);
        Assert.Equal(a.RecordsHash(), b.RecordsHash());
        output.WriteLine($"gap {target - t0} h, coarse until {a.CoarseUntilHour - t0} h into it; exact {direct.RecordsHash()[..12]}, coarse {a.RecordsHash()[..12]}");
    }
    private const long CoarseOff = long.MinValue;

    [Fact]
    public void A_region_joining_the_scheduler_at_different_hours_ends_in_the_same_state()
    {
        // the same paged section, asked for at one game hour, joins at hour J1 in one world and J2 in the other (a worker is as slow as it is): the same records at the later hour
        string Run(long joinDelay)
        {
            var rig = new RegionRig();
            var east = rig.FirstPopulated([rig.Home.Offset(1, 0), rig.Home.Offset(0, 1), rig.Home.Offset(-1, 0)]);
            long ask = east.HourDone + 24 * 6;
            var section = east.CaptureBytes(rig.Chunks);
            var rw = Reopen(rig, east, section);
            rw.SetCatchUpAnchor(ask);
            rw.AdvanceTo(ask + joinDelay);                      // joins and is caught up to the hour the scheduler is at
            rw.AdvanceTo(ask + 30);                             // the same later hour in both runs
            return rw.RecordsHash();
        }
        Assert.Equal(Run(1), Run(9));
    }

    // ------------------------------------------------------------------ 5. baseline guard and the format version

    [Fact]
    public void A_region_section_diffed_against_a_baseline_refuses_to_load_onto_a_different_regeneration()
    {
        var rig = new RegionRig();
        var east = rig.FirstPopulated([rig.Home.Offset(1, 0), rig.Home.Offset(0, 1), rig.Home.Offset(-1, 0)]);
        east.AdvanceTo(east.HourDone + 5);
        var good = SaveCodec.DeserializeCompressed<RegionSectionData>(east.CaptureBytes(rig.Chunks));
        Assert.NotNull(good.People.Diff);
        Assert.NotEqual("", good.BaselineHash);
        Assert.Equal(SaveFormat.RegionGeneratorVersion, good.GeneratorVersion);
        _ = RegionWorld.Restore(rig.Sv, east.Coord, east.Population.Region, good); // the matching regeneration loads

        RegionSectionData Copy() => SaveCodec.DeserializeCompressed<RegionSectionData>(SaveCodec.SerializeCompressed(good));
        var bad = Copy(); bad.BaselineHash = new string('0', 64);
        var ex = Assert.Throws<SaveException>(() => RegionWorld.Restore(rig.Sv, east.Coord, east.Population.Region, bad));
        Assert.Contains("regenerated", ex.Message); Assert.Contains(east.Coord.ToString(), ex.Message);
        var old = Copy(); old.GeneratorVersion = SaveFormat.RegionGeneratorVersion + 1;
        ex = Assert.Throws<SaveException>(() => RegionWorld.Restore(rig.Sv, east.Coord, east.Population.Region, old));
        Assert.Contains("generator v" + (SaveFormat.RegionGeneratorVersion + 1), ex.Message);
    }

    [Fact]
    public void The_home_people_are_guarded_the_same_way_and_a_v1_save_migrates_unguarded()
    {
        var rig = new RegionRig();
        var state = rig.State(GameTime.FromGameHours(rig.Set.HomeWorld!.HourDone + 1));
        state.Content = rig.W.Content; // the people go in as a difference from the regenerated ones
        var file = WorldSave.Capture(state, Hash, "0.1");
        Assert.Equal(SaveFormat.CurrentVersion, file.Header.FormatVersion);
        Assert.Equal(5, SaveFormat.CurrentVersion);   // v4: chunk sections carry their piece palette (Phase 3 cleanup); v5: the optional wounds section (Phase 4 P4-B)
        var meta = SaveCodec.Deserialize<WorldMeta>(file.Sections[WorldSave.MetaSection].ToArray());
        Assert.NotEqual("", meta.BaselineHash); Assert.Equal(SaveFormat.RegionGeneratorVersion, meta.GeneratorVersion);
        _ = WorldSave.Restore(file, rig.W.Content, Hash, null, rig.W.Continent, rig.W.History);

        SaveFile With(Action<WorldMeta> edit, int format)
        {
            var m = SaveCodec.Deserialize<WorldMeta>(file.Sections[WorldSave.MetaSection].ToArray()); edit(m);
            var sections = file.Sections.Select(s => new KeyValuePair<string, byte[]>(s.Key, s.Key == WorldSave.MetaSection ? SaveCodec.Serialize(m) : s.Value.ToArray()));
            return new SaveFile(file.Header with { FormatVersion = format }, sections);
        }
        var tampered = With(m => m.BaselineHash = new string('0', 64), 2);
        var ex = Assert.Throws<SaveException>(() => WorldSave.Restore(tampered, rig.W.Content, Hash, null, rig.W.Continent, rig.W.History));
        Assert.Contains("regenerated", ex.Message);

        // a save of format v1 has neither: the default migrator accepts it, unguarded
        var v1 = With(m => { m.BaselineHash = ""; m.GeneratorVersion = 0; }, 1);
        var bytes = SaveCodec.Encode(v1);
        var migrated = SaveMigrator.CreateDefault().Migrate(SaveCodec.Decode(bytes, SaveFormat.CurrentVersion));
        Assert.Equal(SaveFormat.CurrentVersion, migrated.Header.FormatVersion);
        _ = WorldSave.Restore(migrated, rig.W.Content, Hash, null, rig.W.Continent, rig.W.History);
        Assert.Throws<SaveException>(() => new SaveMigrator().Migrate(v1)); // and a migrator with no steps still refuses it with a clear message
    }

    [Fact]
    public void Chunk_snapshots_carry_the_generator_version_and_content_hash()
    {
        // spec 2.2: each chunk snapshot stores the generator version and content hash; a stale one is counted when streamed (ChunkStreamer.StaleSnapshots)
        var store = new ChunkStore();
        store.RecordFirstVisit(new ChunkKey(0, 0, 0), ChunkGenerator.Version, "abc", Tile(1));
        var snap = store.GetSnapshot(new ChunkKey(0, 0, 0))!;
        Assert.Equal(ChunkGenerator.Version, snap.GeneratorVersion);
        Assert.Equal("abc", snap.ContentHash);
        var back = ChunkStore.FromSection(store.ToSection());
        Assert.Equal(("abc", ChunkGenerator.Version), (back.GetSnapshot(new ChunkKey(0, 0, 0))!.ContentHash, back.GetSnapshot(new ChunkKey(0, 0, 0))!.GeneratorVersion));
    }

    // ------------------------------------------------------------------ 6. daily regions are spread over the day

    [Fact]
    public void Daily_regions_are_staggered_so_no_hour_carries_all_of_them_and_the_barrier_stays_in_budget()
    {
        var rig = new RegionRig(dailyRing: 2);
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                var rc = rig.Home.Offset(dx, dy);
                if (!rc.IsValid) continue;
                rig.Mgr.GenerateNow(rc); rig.Set.Sync();
            }
        var daily = rig.Set.Resident.Where(r => r.Coord.Chebyshev(rig.Home) == 2).ToList();
        Assert.True(daily.Count >= 6, $"only {daily.Count} populated regions in the daily ring");
        long h0 = rig.Set.HomeWorld!.HourDone;
        int maxDailySteps = 0, totalDaily = 0, hours = 0;
        long hour = h0;
        List<double> Pass(int n)
        {
            var ms = new List<double>();
            for (int i = 0; i < n; i++)
            {
                var r = rig.Sched.Update(GameTime.FromGameHours(++hour), rig.Home); hours++;
                ms.Add(r.Ms);
                maxDailySteps = Math.Max(maxDailySteps, r.DailyHourSteps); totalDaily += r.DailyHourSteps;
            }
            return ms;
        }
        Pass(12); // JIT, and the regions' first slices
        // the barrier cost is measured over passes of a day and a half (so a day-boundary burst cannot hide); one noisy pass (a GC, another process) is allowed, a persistent burst is not
        var passMax = new List<double>();
        for (int p = 0; p < 3; p++) { Ravensguard.Sim.Diagnostics.GcSettle.Run(); var ms = Pass(36); passMax.Add(ms.Max()); if (ms.Max() < TestBudget.Ms(150)) break; } // each pass on a settled heap
        // the old schedule ran every daily region's 24 hours on the day boundary: the barrier carried N x 24 steps
        int worstOldBarrier = daily.Count * 24;
        int slice = RegionScheduler.DailySliceHours;
        int expectedWorst = Enumerable.Range(0, slice).Max(ph => daily.Count(d => d.Coord.Id % slice == ph)) * slice; // the most regions that share a phase, each with one slice
        output.WriteLine($"{daily.Count} daily regions: most steps in one barrier {maxDailySteps} (was {worstOldBarrier}), per hour on average {totalDaily / (double)hours:0.0}; slowest barrier per pass {string.Join(", ", passMax.Select(m => m.ToString("0")))} ms");
        Assert.True(maxDailySteps <= expectedWorst, $"{maxDailySteps} daily hour steps in one barrier, at most {expectedWorst} by the schedule");
        Assert.True(maxDailySteps < worstOldBarrier / 2);
        Assert.True(totalDaily >= daily.Count * (hours - 2 * slice), "every daily region still gets its hours (they run behind by less than a slice)");
        foreach (var d in daily) Assert.InRange(hour - d.HourDone, 0, slice - 1);
        Assert.True(passMax.Min() < TestBudget.Ms(150), $"the slowest hour barrier took {passMax.Min():0} ms in the best pass (budget 150 ms at the reference PC)");
    }

    [Fact]
    public void The_daily_slice_schedule_is_pure_in_hour_and_region()
    {
        for (int id = 0; id < 40; id++)
            for (long h = 100; h < 160; h++)
            {
                long t = RegionScheduler.DailyTarget(h, id);
                Assert.InRange(h - t, 0, RegionScheduler.DailySliceHours - 1);
                Assert.Equal(0, ((t - id) % RegionScheduler.DailySliceHours + RegionScheduler.DailySliceHours) % RegionScheduler.DailySliceHours);
                Assert.Equal(t, RegionScheduler.DailyTarget(h, id));
            }
    }

    // ------------------------------------------------------------------ 7. moving a household

    private static (long Mites, long MassG) Totals(params RegionWorld[] rs)
    {
        long coin = 0, mass = 0;
        foreach (var r in rs)
        {
            var e = r.Sim.Economy;
            coin += e.Ledger.Audit().InPurses.Mites;
            for (int i = 0; i < e.HouseholdCount; i++) mass += e.HouseholdStore[i].TotalMassG();
            foreach (var s in e.OtherStores.Values) mass += s.TotalMassG();
        }
        return (coin, mass);
    }

    [Fact]
    public void A_household_moves_with_its_members_coin_and_stock_and_nothing_is_created_or_lost()
    {
        var rig = new RegionRig();
        var from = rig.Set.HomeWorld!;
        var to = rig.FirstPopulated([rig.Home.Offset(1, 0), rig.Home.Offset(0, 1), rig.Home.Offset(-1, 0), rig.Home.Offset(0, -1)]);
        var fp = from.Population.People; var fh = from.Population.Households; var tp = to.Population.People; var th = to.Population.Households;
        var when = GameTime.FromGameHours(from.HourDone);
        long day = when.TotalDays;

        // a household with a married couple and a minor child, as the generator makes them
        int hh = -1, husband = -1, wife = -1, child = -1;
        for (int h = 0; h < fh.Count && hh < 0; h++)
        {
            var ms = fh.Members(h).Where(m => fp.Status[m] == PersonStatus.Alive).ToList();
            foreach (int m in ms)
            {
                int sp = fp.Spouse[m];
                if (sp < 0 || fp.Spouse[sp] != m || !ms.Contains(sp) || fp.Sex[m] != Sex.Male) continue;
                int kid = ms.FirstOrDefault(k => k != m && k != sp && from.Sim.AgeOn(k, day) < 16 && (fp.Father[k] == m || fp.Mother[k] == sp), -1);
                if (kid >= 0) { hh = h; husband = m; wife = sp; child = kid; break; }
            }
        }
        Assert.True(hh >= 0, "a household with a couple and a child");
        var members = fh.Members(hh).Where(m => fp.Status[m] == PersonStatus.Alive).OrderBy(m => m).ToArray();
        fp.PregnantUntilDay[wife] = (int)day + 100; fp.FatherOfUnborn[wife] = husband;
        int alive = members.Length;

        // an empty dwelling in the destination with room (its people left)
        int dest = Enumerable.Range(0, th.Count).First(h => th.MemberCount[h] >= 1 && th.Beds[h] >= alive);
        foreach (int m in th.Members(dest).ToArray()) { tp.Status[m] = PersonStatus.Departed; th.RemoveMember(dest, m); to.Sim.Vital.Alive--; }
        Demography.VacateDwelling(to.Sim, dest);

        var coinBefore = from.Sim.Economy.HouseholdCoin(hh);
        long destPurseBefore = to.Sim.Economy.HouseholdCoin(dest).Mites; // what the dead household left in the dwelling's purse stays there
        Assert.True(coinBefore.Mites > 0);
        var (c0, m0) = Totals(from, to);
        var aliveFrom = from.Sim.Vital.Alive; var aliveTo = to.Sim.Vital.Alive;
        var move = RegionTransfer.MoveHousehold(from, to, hh, -1, when);
        Assert.NotNull(move);
        Assert.Equal(dest, move!.Household);
        var (c1, m1) = Totals(from, to);
        Assert.Equal(c0, c1); Assert.Equal(m0, m1);
        Assert.True(from.Sim.Economy.Ledger.Audit().IsConserved && to.Sim.Economy.Ledger.Audit().IsConserved);
        Assert.True(from.Sim.Economy.MassAuditHolds() && to.Sim.Economy.MassAuditHolds());
        Assert.Equal(coinBefore, move.Coin); Assert.True(move.MassG > 0);
        Assert.Equal(0, from.Sim.Economy.HouseholdCoin(hh).Mites); Assert.Equal(0, from.Sim.Economy.HouseholdStore[hh].TotalMassG());
        Assert.Equal(coinBefore.Mites + destPurseBefore, to.Sim.Economy.HouseholdCoin(dest).Mites);
        Assert.Equal(aliveFrom - alive, from.Sim.Vital.Alive); Assert.Equal(aliveTo + alive, to.Sim.Vital.Alive);

        var map = new Dictionary<int, int>();
        for (int i = 0; i < members.Length; i++) map[members[i]] = move.Rows[i];
        foreach (var (old, row) in map)
        {
            Assert.Equal(PersonStatus.Departed, fp.Status[old]);
            Assert.Equal(PersonStatus.Alive, tp.Status[row]);
            Assert.Equal(fp.IdOf(old), tp.IdOf(row));
            Assert.Equal(dest, tp.Household[row]);
            Assert.Contains(row, th.Members(dest));
        }
        Assert.Equal(map[wife], tp.Spouse[map[husband]]); Assert.Equal(map[husband], tp.Spouse[map[wife]]);   // the marriage came along
        Assert.True(map.ContainsKey(child) && tp.Father[map[child]] == map[husband] && tp.Mother[map[child]] == map[wife]); // the minor came along, still linked to both parents
        Assert.Equal((int)day + 100, tp.PregnantUntilDay[map[wife]]); Assert.Equal(map[husband], tp.FatherOfUnborn[map[wife]]); // the pregnancy goes on
        Assert.Equal(-1, fp.PregnantUntilDay[wife]);
        // kin who stayed behind are kept as references by id
        int refs = 0;
        foreach (var (old, row) in map)
        {
            foreach (var (link, oldKin, now) in new[] { (KinLink.Father, fp.Father[old], tp.Father[row]), (KinLink.Mother, fp.Mother[old], tp.Mother[row]) })
            {
                if (oldKin < 0 || map.ContainsKey(oldKin)) continue;
                Assert.Equal(-1, now); Assert.Equal(fp.IdOf(oldKin), tp.ForeignKinOf(row, link)); refs++;
            }
        }
        output.WriteLine($"moved {members.Length} people, {coinBefore}, {move.MassG / 1000} kg; {refs} kin links kept as foreign references");

        // the arrivals tick, save and load like anyone
        to.AdvanceTo(to.HourDone + 24); from.AdvanceTo(from.HourDone + 24);
        Assert.True(from.Sim.Economy.Ledger.Audit().IsConserved && to.Sim.Economy.Ledger.Audit().IsConserved);
        var back = RegionWorld.Restore(rig.Sv, to.Coord, to.Population.Region, SaveCodec.DeserializeCompressed<RegionSectionData>(to.CaptureBytes(rig.Chunks)));
        Assert.Equal(to.RecordsHash(), back.RecordsHash());
        Assert.Equal(to.Population.People.ForeignKin.ToArray(), back.Population.People.ForeignKin.ToArray());
    }

    [Fact]
    public void One_person_moves_only_when_it_splits_no_family_and_takes_no_coin_or_stock()
    {
        var rig = new RegionRig();
        var from = rig.Set.HomeWorld!;
        var to = rig.FirstPopulated([rig.Home.Offset(1, 0), rig.Home.Offset(0, 1), rig.Home.Offset(-1, 0), rig.Home.Offset(0, -1)]);
        var fp = from.Population.People;
        var when = GameTime.FromGameHours(from.HourDone); long day = when.TotalDays;
        int married = Enumerable.Range(0, fp.Count).First(i => fp.Status[i] == PersonStatus.Alive && fp.Spouse[i] >= 0 && fp.Status[fp.Spouse[i]] == PersonStatus.Alive && from.Sim.AgeOn(i, day) >= 16);
        int minor = Enumerable.Range(0, fp.Count).First(i => fp.Status[i] == PersonStatus.Alive && from.Sim.AgeOn(i, day) < 16);
        int pregnant = Enumerable.Range(0, fp.Count).First(i => fp.Status[i] == PersonStatus.Alive && fp.Sex[i] == Sex.Female && fp.Spouse[i] < 0 && from.Sim.AgeOn(i, day) is >= 17 and < 40);
        fp.PregnantUntilDay[pregnant] = (int)day + 50;
        var (c0, m0) = Totals(from, to);
        Assert.Equal(-1, RegionTransfer.MovePerson(from, to, married, -1, when));     // would drop the marriage
        Assert.Equal(-1, RegionTransfer.MovePerson(from, to, minor, -1, when));       // would leave or take a child alone
        Assert.Equal(-1, RegionTransfer.MovePerson(from, to, pregnant, -1, when));    // would clear the pregnancy
        Assert.Equal(PersonStatus.Alive, fp.Status[married]);
        fp.PregnantUntilDay[pregnant] = -1;
        int free = Enumerable.Range(0, fp.Count).First(i => RegionTransfer.CanMovePerson(from, i, when) && from.Population.Age(i) is > 20 and < 50);
        int row = RegionTransfer.MovePerson(from, to, free, -1, when);
        Assert.True(row >= 0);
        var (c1, m1) = Totals(from, to);
        Assert.Equal(c0, c1); Assert.Equal(m0, m1);
        Assert.True(from.Sim.Economy.Ledger.Audit().IsConserved && to.Sim.Economy.Ledger.Audit().IsConserved && from.Sim.Economy.MassAuditHolds() && to.Sim.Economy.MassAuditHolds());
    }

    // ------------------------------------------------------------------ 8, 10, 11. the manager

    [Fact]
    public void Terrain_only_regions_far_from_every_traveller_are_evicted_and_come_back_identically_when_asked_for()
    {
        var rig = new RegionRig();
        var empty = FindRecordLess(rig);
        Assert.True(rig.Mgr.IsReady(empty));
        string hash = rig.Mgr.TryGet(empty)!.Region.ComputeHash();
        var far = new RegionCoord((empty.X + 8) % 16, (empty.Y + 8) % 16);
        long cx = empty.OriginXM + 16000, cy = empty.OriginYM + 16000;
        rig.Mgr.Track(1, cx, cy);                                    // a traveller stands in it
        Assert.Equal(0, rig.Mgr.EvictDistantTerrainOnly(rig.Home, far));
        Assert.True(rig.Mgr.IsReady(empty));
        rig.Mgr.Forget(1);
        Assert.True(rig.Mgr.EvictDistantTerrainOnly(rig.Home, far) >= 1);
        Assert.False(rig.Mgr.IsReady(empty));
        Assert.True(rig.Mgr.IsReady(rig.Home), "the home region is never evicted");
        Assert.Equal(hash, rig.Mgr.GenerateNow(empty).Region.ComputeHash());
    }

    [Fact]
    public void A_generator_is_made_on_the_worker_when_a_traveller_comes_within_the_trigger_distance_of_a_built_region()
    {
        var rig = new RegionRig();
        var east = rig.Generate(rig.Home.Offset(1, 0));
        var g = rig.Mgr.TryGet(east.Coord)!;
        Assert.False(g.HasGenerator);
        Assert.False(rig.Mgr.PrewarmGenerator());                    // no traveller near: nothing to do
        rig.Mgr.Track(1, east.Coord.OriginXM - 2500, east.Coord.OriginYM + 16000); // 2.5 km before its west edge, still in home
        int made = 0;
        while (rig.Mgr.PrewarmGenerator()) made++;                   // one per call, nearest first (the home region the traveller stands in has none either)
        Assert.InRange(made, 1, 2);
        Assert.True(g.HasGenerator);
    }

    [Fact]
    public void A_region_that_never_builds_is_retried_a_few_times_then_given_up_and_logged_once_without_throwing()
    {
        var w = SeamWorld.Get(1234);
        var at = w.BlockOrigin.Offset(1, 1);
        var mgr = new RegionManager(w.Continent, w.History, w.Settings, w.Content) { Synchronous = true, RetryBackoffMs = 0 };
        int calls = 0; var failures = new List<RegionCoord>();
        mgr.RecordsFactory = (_, _, _) => { calls++; throw new InvalidOperationException("the records of this region cannot be made"); };
        mgr.BuildFailed += (rc, _) => failures.Add(rc);
        for (int i = 0; i < 10; i++) mgr.Track(1, at.OriginXM + 16000, at.OriginYM + 16000); // the hero stands in it; every frame asks again
        Assert.Equal(mgr.MaxBuildAttempts, calls);
        Assert.Equal([at], failures);
        Assert.Single(mgr.Failed);
        Assert.False(mgr.IsReady(at));
        mgr.ClearFailure(at);
        mgr.Track(1, at.OriginXM + 16000, at.OriginYM + 16000);
        Assert.Equal(mgr.MaxBuildAttempts + 1, calls);               // a cleared failure is tried again
    }

    [Fact]
    public void A_failed_region_waits_between_tries()
    {
        var w = SeamWorld.Get(1234);
        var at = w.BlockOrigin.Offset(1, 1);
        var mgr = new RegionManager(w.Continent, w.History, w.Settings, w.Content) { Synchronous = true, RetryBackoffMs = 60_000 };
        int calls = 0;
        mgr.RecordsFactory = (_, _, _) => { calls++; throw new InvalidOperationException("boom"); };
        for (int i = 0; i < 5; i++) mgr.Track(1, at.OriginXM + 16000, at.OriginYM + 16000);
        Assert.Equal(1, calls); Assert.Empty(mgr.Failed); // asked again every frame, tried once: the next try is a minute away
    }

    // ------------------------------------------------------------------ 9, 12. the runtime

    /// <summary>A pager that throws on demand (a worker fault the tick can hit) and notes when it is disposed.</summary>
    private sealed class FlakyPager : IRegionPager, IDisposable
    {
        private readonly MemoryRegionPager _m = new();
        public volatile bool Throw;
        public int Disposed;
        public void Write(int id, byte[] s) => _m.Write(id, s);
        public byte[]? Read(int id) => _m.Read(id);
        public bool Has(int id) => _m.Has(id);
        public void Delete(int id) => _m.Delete(id);
        public IReadOnlyList<int> Ids => Throw ? throw new IOException("the page folder is gone") : _m.Ids;
        public void Dispose() => Interlocked.Increment(ref Disposed);
    }

    [Fact]
    public void After_the_tick_has_faulted_a_save_is_refused_with_a_clear_error_and_never_runs()
    {
        var (w, st) = Game();
        var pager = new FlakyPager();
        using var rt = new WorldRuntime(st, w.Content, pager);
        rt.Start();
        var home = st.HomeRegion;
        long hour = rt.HourDone;
        rt.Pump(GameTime.FromGameHours(hour + 1), home.OriginXM + 16000, home.OriginYM + 16000);
        rt.Quiesce();
        Assert.Null(rt.Fault);
        Assert.Equal(7, rt.AtBarrier(() => 7).GetAwaiter().GetResult()); // healthy: jobs run

        pager.Throw = true;
        var sw = Stopwatch.StartNew();
        while (rt.Fault == null && sw.Elapsed.TotalSeconds < 20) { rt.Pump(GameTime.FromGameHours(rt.HourDone + 2), home.OriginXM + 16000, home.OriginYM + 16000); Thread.Sleep(10); }
        Assert.NotNull(rt.Fault);
        bool ran = false;
        var task = rt.AtBarrier(() => { ran = true; return 1; });
        var ex = Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
        Assert.Contains("cannot be saved", ex.Message);
        Assert.False(ran, "the save job never ran on the half-stepped records");
        rt.Quiesce(); // shutting down and waiting still work
    }

    [Fact]
    public void Disposing_the_runtime_does_not_wait_for_a_busy_worker_and_the_worker_cleans_up_after_itself()
    {
        var (w, st) = Game();
        var pager = new FlakyPager();
        var rt = new WorldRuntime(st, w.Content, pager);
        rt.Start();
        var started = new ManualResetEventSlim();
        var slow = rt.AtBarrier(() => { started.Set(); Thread.Sleep(1500); return 1; });
        Assert.True(started.Wait(10_000));
        var sw = Stopwatch.StartNew();
        rt.Dispose();
        Assert.True(sw.ElapsedMilliseconds < 800, $"Dispose took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(0, Volatile.Read(ref pager.Disposed));
        Assert.Equal(1, slow.GetAwaiter().GetResult());              // the job already queued still ran
        var wait = Stopwatch.StartNew();
        while (Volatile.Read(ref pager.Disposed) == 0 && wait.Elapsed.TotalSeconds < 10) Thread.Sleep(10);
        Assert.Equal(1, Volatile.Read(ref pager.Disposed));          // and then the worker removed the page folder
        rt.Dispose();                                                // idempotent
        Assert.Equal(1, Volatile.Read(ref pager.Disposed));
    }

    [Fact]
    public void A_runtime_that_fails_to_start_disposes_its_pager_and_loaded_sections_are_not_kept_twice()
    {
        var (w, st) = Game();
        var bad = new FlakyPager();
        var broken = new WorldState
        {
            ContinentSettings = st.ContinentSettings, HistorySettings = st.HistorySettings, RegionSettings = st.RegionSettings, EmbarkCellX = st.EmbarkCellX, EmbarkCellY = st.EmbarkCellY,
            Continent = st.Continent, History = st.History, Population = st.Population, Clock = st.Clock, Ids = st.Ids, Chunks = st.Chunks, Chronicle = st.Chronicle, // no economy: the home records cannot be made
        };
        Assert.ThrowsAny<Exception>(() => new WorldRuntime(broken, w.Content, bad));
        Assert.Equal(1, bad.Disposed);

        var good = new FlakyPager();
        st.RegionSections = new SortedDictionary<int, byte[]> { [3] = [1, 2, 3] };
        using var rt = new WorldRuntime(st, w.Content, good);
        Assert.True(good.Has(3));
        Assert.Empty(st.RegionSections);
    }
}
