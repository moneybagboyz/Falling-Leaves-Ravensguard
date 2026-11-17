using System.Diagnostics;
using MessagePack;
using Ravensguard.Content;
using Ravensguard.Sim.Codex;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Save;

/// <summary>A populated world (seed 1234) with landmarks placed, ready to be played in a streamed <see cref="LiveWorld"/> and saved.</summary>
public sealed class LiveSaveFixture
{
    public ContentDatabase Content { get; }
    public string ContentHash { get; }
    public WorldState World { get; }
    public ChunkGenerator Generator { get; }
    public CodexBook NewBook() => CodexBook.Build(World.History, World.Population.Region, [new CodexRule("rule.x", "Rule", "text")]);
    public Cell3 Start { get; }

    public LiveSaveFixture()
    {
        var dir = ContinentSettings.FindBaseContent()!;
        using var cache = new TempDir();
        var load = new ModLoader(ContentTypeRegistry.Default).Load([dir], cache.Path);
        Assert.True(load.Success, load.Report.ToString());
        Content = load.Database; ContentHash = load.Hash!;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var rs = RegionSettings.LoadDefaults(dir); rs.Seed = 1234;
        var c = ContinentGenerator.Generate(cs);
        var h = HistoryGenerator.Generate(c, hs);
        var pick = EmbarkSite.AutoPick(c, h);
        var r = RegionGenerator.Generate(c, h, pick.CellX, pick.CellY, rs);
        LandmarkPlacer.Place(r, h, Content);
        var pop = Populator.Populate(r, h, Content);
        Generator = ChunkGenerator.For(r, pop, Content);
        World = new WorldState
        {
            ContinentSettings = cs, HistorySettings = hs, RegionSettings = rs, EmbarkCellX = pick.CellX, EmbarkCellY = pick.CellY,
            Continent = c, History = h, Population = pop, Clock = new SimClock(GameTime.FromCalendar(pop.NowYear, Season.Summer, 3, 14, 20)),
            Ids = new IdAllocator(), Chunks = new ChunkStore(), Chronicle = new Chronicle(),
        };
        Start = RegionSpawn.TownSquare(new ChunkStreamer(Generator), r)!.Value;
    }
}

public class LiveSaveTests(LiveSaveFixture fx, ITestOutputHelper output) : IClassFixture<LiveSaveFixture>
{
    /// <summary>A fresh play session on its own copy of the chunk store: walks, opens a Codex page, lights the torch and runs the clock.</summary>
    private (LiveWorld Live, ChunkStreamer Streamer, WorldState World, CodexBook Book) Play(out string unlockedId)
    {
        var store = new ChunkStore();
        var streamer = new ChunkStreamer(fx.Generator, store);
        var book = fx.NewBook();
        var live = new LiveWorld(streamer, fx.Start, GameTime.FromCalendar(5, Season.Summer, 3, 14, 20), codex: book) { SynchronousStreaming = true };
        live.SetTorch(true);
        live.SetSpeed(SimSpeed.X4);
        // walk about: a few directions, stepping round scenery
        (int, int)[] dirs = [(1, 0), (0, 1), (1, 1), (-1, 0), (0, -1), (1, -1)];
        for (int leg = 0; leg < dirs.Length; leg++)
        {
            live.SetInput(new MoveInput(dirs[leg].Item1, dirs[leg].Item2, Sprint: true));
            for (int i = 0; i < 160; i++) live.Step();
        }
        live.SetInput(MoveInput.None);
        for (int i = 0; i < 25; i++) live.Step();
        // open a landmark's page the way the tick does when the hero stands near it
        var m = fx.World.Population.Region.Landmarks![0];
        Assert.True(live.VisitNear(m.X, m.Y) > 0, "the landmark page should open");
        unlockedId = "landmark." + m.Index;
        var world = new WorldState
        {
            ContinentSettings = fx.World.ContinentSettings, HistorySettings = fx.World.HistorySettings, RegionSettings = fx.World.RegionSettings,
            EmbarkCellX = fx.World.EmbarkCellX, EmbarkCellY = fx.World.EmbarkCellY, Continent = fx.World.Continent, History = fx.World.History, Population = fx.World.Population,
            Clock = fx.World.Clock, Ids = fx.World.Ids, Chunks = store, Chronicle = fx.World.Chronicle,
        };
        return (live, streamer, world, book);
    }

    [Fact]
    public void Live_state_survives_capture_restore_and_save_load_save_is_byte_identical()
    {
        var (live, _, world, book) = Play(out string unlocked);
        var before = live.CaptureResume();
        Assert.True(live.Explored.Count > 20, "walking explores map tiles");
        Assert.True(book.Get(unlocked)!.Unlocked);
        Assert.True(before.Torch);

        var file = WorldSave.CaptureLive(world, live, fx.ContentHash, "0.1");
        var bytes = SaveCodec.Encode(file);
        var restored = WorldSave.Restore(SaveCodec.Decode(bytes), fx.Content, fx.ContentHash);

        var res = Assert.IsType<LiveResume>(restored.Live);
        Assert.Equal(before.WorldXRaw, res.WorldXRaw);
        Assert.Equal(before.WorldYRaw, res.WorldYRaw);
        Assert.Equal(before.Z, res.Z);
        Assert.Equal(before.Facing, res.Facing);
        Assert.Equal(before.Torch, res.Torch);
        Assert.Equal(before.Time, res.Time);
        Assert.Equal(before.MsRemainder, res.MsRemainder);
        Assert.Equal(before.Speed, res.Speed);
        Assert.True(before.Explored.SameAs(res.Explored));
        Assert.Equal(before.Time, restored.Clock.Now);
        Assert.Contains(unlocked, restored.Codex!.Unlocked);
        Assert.Equal(before.Time.Seconds, SaveCodec.ReadHeader(new MemoryStream(bytes)).GameSeconds);

        // resume: a new live sim stands exactly where the save left the hero, with the same clock, torch, explored tiles and Codex
        var book2 = fx.NewBook();
        var streamer2 = new ChunkStreamer(fx.Generator, restored.Chunks);
        var live2 = new LiveWorld(streamer2, res, codex: book2) { SynchronousStreaming = true };
        var after = live2.CaptureResume();
        Assert.Equal(before.WorldXRaw, after.WorldXRaw);
        Assert.Equal(before.WorldYRaw, after.WorldYRaw);
        Assert.Equal(before.Time, live2.Clock.Now);
        Assert.Equal(before.Speed, live2.Clock.Speed);
        Assert.True(live2.Torch);
        Assert.True(book2.Get(unlocked)!.Unlocked);
        Assert.True(live2.Explored.SameAs(live.Explored));

        // save -> load -> save is byte-identical
        var bytes2 = SaveCodec.Encode(WorldSave.CaptureLive(restored, live2, fx.ContentHash, "0.1"));
        Assert.Equal(bytes.Length, bytes2.Length);
        Assert.True(bytes.AsSpan().SequenceEqual(bytes2), "save -> load -> save changed the bytes");
    }

    [Fact]
    public void The_resumed_hero_keeps_walking_from_the_saved_spot()
    {
        var (live, _, world, _) = Play(out _);
        var restored = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(WorldSave.CaptureLive(world, live, fx.ContentHash, "0.1"))), fx.Content, fx.ContentHash);
        var a = new LiveWorld(new ChunkStreamer(fx.Generator, restored.Chunks), restored.Live!) { SynchronousStreaming = true };
        var b = new LiveWorld(new ChunkStreamer(fx.Generator, restored.Chunks), restored.Live!) { SynchronousStreaming = true };
        a.SetInput(new MoveInput(0, 1)); b.SetInput(new MoveInput(0, 1));
        for (int i = 0; i < 100; i++) { a.Step(); b.Step(); }
        Assert.Equal(a.CaptureResume().WorldYRaw, b.CaptureResume().WorldYRaw); // deterministic from a save
    }

    [Fact]
    public void A_save_without_live_state_restores_with_no_resume()
    {
        var (_, _, world, _) = Play(out _);
        var restored = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(world, fx.ContentHash, "0.1"))), fx.Content, fx.ContentHash);
        Assert.Null(restored.Live);
    }

    [Fact]
    public void Landmark_hash_is_stored_and_checked_on_restore()
    {
        var (live, _, world, _) = Play(out _);
        var file = WorldSave.CaptureLive(world, live, fx.ContentHash, "0.1");
        var meta = SaveCodec.Deserialize<WorldMeta>(file.Sections[WorldSave.MetaSection]);
        Assert.Equal(LandmarkPlacer.ComputeHash(fx.World.Population.Region.Landmarks), meta.LandmarkHash);
        meta.LandmarkHash = "00" + meta.LandmarkHash[2..];
        var tampered = new SaveFile(file.Header, file.Sections.Select(s => new KeyValuePair<string, byte[]>(s.Key, s.Key == WorldSave.MetaSection ? SaveCodec.Serialize(meta) : s.Value.ToArray())));
        var ex = Assert.Throws<SaveException>(() => WorldSave.Restore(tampered, fx.Content, fx.ContentHash));
        Assert.Contains("landmark", ex.Message);
    }

    // ------------------------------------------------------------------ cost of a save

    [Fact]
    public async Task Taking_a_save_snapshot_is_a_short_hitch_and_encoding_runs_on_a_worker()
    {
        var (live, _, world, _) = Play(out _);
        WorldSave.SnapshotLive(world, live, fx.ContentHash).Encode(); // warm up: JIT, layer hashes, per-chunk encodings
        // play a little more so a few chunks change, as between two autosaves
        live.SetInput(new MoveInput(1, 0, Sprint: true)); for (int i = 0; i < 40; i++) live.Step();
        world.Chunks.AppendDelta(new ChunkKey(5, 5, 0), [ChunkOp.SetTile(1, 1, 7)]);

        var sw = Stopwatch.StartNew();
        var snap = WorldSave.SnapshotLive(world, live, fx.ContentHash);
        double hitchMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var file = await Task.Run(snap.Encode);
        double encodeMs = sw.Elapsed.TotalMilliseconds;
        output.WriteLine($"seed 1234: caller-thread snapshot {hitchMs:0.0} ms; worker encode {encodeMs:0.0} ms; chunk entries re-encoded {snap.ChunkEntriesEncoded} of {world.Chunks.Count}; {file.Sections.Values.Sum(v => (long)v.Length):N0} bytes");
        Assert.True(hitchMs < TestBudget.Ms(100), $"snapshot took {hitchMs:0.0} ms");
        Assert.True(snap.ChunkEntriesEncoded < world.Chunks.Count / 4 + 3, $"{snap.ChunkEntriesEncoded} of {world.Chunks.Count} chunk entries re-encoded");

        // and the whole thing through the writer: the snapshot is encoded and written on the worker
        using var dir = new TempDir();
        var path = await new SaveWriter(dir.Path).WriteManualAsync(WorldSave.SnapshotLive(world, live, fx.ContentHash), "live");
        Assert.True(WorldSave.Restore(new SaveReader().Load(path), fx.Content, fx.ContentHash).Live != null);
    }

    [Fact]
    public void The_chunk_section_only_re_encodes_changed_entries_and_keeps_its_wire_format()
    {
        var store = new ChunkStore();
        for (int k = 0; k < 30; k++)
        {
            var c = new ChunkContent();
            for (int t = 0; t < 50; t++) c.Tiles[t * 5 + k % 5] = 1 + (t + k) % 9;
            store.RecordFirstVisit(new ChunkKey(k, 2, 0), 1, "h", c);
        }
        var s1 = store.Snapshot(); var bytes1 = s1.Encode();
        Assert.Equal(30, s1.ReEncoded);
        var s2 = store.Snapshot(); var bytes2 = s2.Encode();
        Assert.Equal(0, s2.ReEncoded);
        Assert.Equal(bytes1, bytes2);

        store.AppendDelta(new ChunkKey(3, 2, 0), [ChunkOp.SetTile(2, 2, 11)]);
        store.RecordFirstVisit(new ChunkKey(40, 2, 0), 1, "h", new ChunkContent { Tiles = { [1] = 1 } });
        var s3 = store.Snapshot(); var bytes3 = s3.Encode();
        Assert.Equal(2, s3.ReEncoded); // the changed chunk and the new one
        Assert.NotEqual(bytes1, bytes3);

        // same wire layout as a ChunkStoreData (it decodes as one), and a round trip is byte-identical
        var data = SaveCodec.DeserializeCompressed<ChunkStoreData>(bytes3);
        Assert.Equal(31, data.Chunks.Count);
        Assert.Equal(SaveCodec.Serialize(data).Length, SaveCodec.Serialize(SaveCodec.DeserializeCompressed<ChunkStoreData>(SaveCodec.SerializeCompressed(data))).Length);
        Assert.Equal(bytes3, ChunkStore.FromSection(bytes3).ToSection());
        Assert.Equal(store.Rebuild(new ChunkKey(3, 2, 0))!.Tiles, ChunkStore.FromSection(bytes3).Rebuild(new ChunkKey(3, 2, 0))!.Tiles);
    }

    [Fact]
    public void A_change_made_while_a_snapshot_encodes_is_not_lost_to_the_cache()
    {
        var store = new ChunkStore();
        var c = new ChunkContent(); c.Tiles[1] = 1;
        store.RecordFirstVisit(new ChunkKey(1, 1, 0), 1, "h", c);
        var snap = store.Snapshot();
        store.AppendDelta(new ChunkKey(1, 1, 0), [ChunkOp.SetTile(4, 4, 9)]); // play changes the chunk after the snapshot was taken
        snap.Encode();                                                          // the worker finishes late; its bytes are for the old version
        var next = store.Snapshot();
        var restored = ChunkStore.FromSection(next.Encode());
        Assert.Equal(9, restored.Rebuild(new ChunkKey(1, 1, 0))!.Tiles[ChunkContent.TileIndex(4, 4)]);
        Assert.Equal(1, next.ReEncoded);
    }

    [Fact]
    public void Chunks_off_the_region_are_never_generated_or_recorded()
    {
        var store = new ChunkStore();
        var streamer = new ChunkStreamer(fx.Generator, store);
        streamer.LoadSync(10, 10);
        int before = store.Count; long gen = streamer.GeneratedCount;
        foreach (var (x, y) in new[] { (-1, 5), (5, -1), (1000, 5), (5, 1000), (-3, -3), (1003, 1003) })
        {
            var c = streamer.LoadSync(x, y);
            Assert.True(streamer.IsLoaded(x, y));
            Assert.Empty(c.Levels);
        }
        Assert.Equal(before, store.Count);
        Assert.Equal(gen, streamer.GeneratedCount);
        Assert.All(store.Keys, k => Assert.True(ChunkStreamer.InRegion(k.X, k.Y), $"{k} is off the region"));
        // the hero can stand at the region's edge without recording a ring of empty chunks
        streamer.Update(0, 0, 1000);
        Assert.All(store.Keys, k => Assert.True(ChunkStreamer.InRegion(k.X, k.Y), $"{k} is off the region"));
    }
}
