using System.Diagnostics;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Save;

public class WorldSaveTests(ITestOutputHelper output)
{
    private sealed record Built(WorldState World, ContentDatabase Content, string ContentHash);
    private static readonly Lazy<Built> Shared = new(Build);

    private static Built Build()
    {
        var dir = ContinentSettings.FindBaseContent()!;
        using var cache = new TempDir();
        var load = new ModLoader(ContentTypeRegistry.Default).Load([dir], cache.Path);
        Assert.True(load.Success, load.Report.ToString());
        Assert.False(string.IsNullOrEmpty(load.Hash));

        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var rs = RegionSettings.LoadDefaults(dir); rs.Seed = 1234;
        var c = ContinentGenerator.Generate(cs);
        var h = HistoryGenerator.Generate(c, hs);
        var pick = EmbarkSite.AutoPick(c, h);
        var r = RegionGenerator.Generate(c, h, pick.CellX, pick.CellY, rs);
        LandmarkPlacer.Place(r, h, load.Database); // the game places landmarks right after the region; a loaded world always has them
        var pop = Populator.Populate(r, h, load.Database);

        var ids = new IdAllocator();
        for (int i = 0; i < 7; i++) ids.Allocate(IdKind.Event);
        var clock = new SimClock(GameTime.FromCalendar(pop.NowYear, Season.Summer, 3, 14, 20));
        clock.Advance(0.0173);

        var chunks = new ChunkStore();
        for (int k = 0; k < 4; k++)
        {
            var content = new ChunkContent();
            for (int t = 0; t < 40; t++) content.Tiles[t * 7 + k] = 1 + (t + k) % 5;
            content.Objects[1000UL + (ulong)k] = new ObjectRecord { Id = 1000UL + (ulong)k, Data = [1, 2, (byte)k] };
            chunks.RecordFirstVisit(new ChunkKey(10 + k, 20, 0), 1, load.Hash!, content);
        }
        chunks.AppendDelta(new ChunkKey(10, 20, 0), [ChunkOp.SetTile(3, 4, 9), ChunkOp.AddObject(EntityId.Make(IdKind.Item, 77), [5, 6, 7]),
            ChunkOp.SetProperty(EntityId.Make(IdKind.Item, 77), "hp", [3])]);
        chunks.AppendDelta(new ChunkKey(12, 20, 0), [ChunkOp.RemoveObject(new EntityId(1002)), ChunkOp.SetTile(31, 31, 2)]);

        var chron = new Chronicle();
        for (int i = 0; i < 5; i++)
        {
            var id = ids.Allocate(IdKind.Event);
            chron.Add(new ChronicleEntry(id, GameTime.FromGameDays(100 + i), "test.event", [pop.People.IdOf(i)],
                i == 0 ? [new Cause(CauseKind.Fire, null, "lightning")] : [new Cause(CauseKind.Raid, EntityId.Make(IdKind.Event, 1), "")],
                new Dictionary<string, string> { ["zeta"] = "z", ["alpha"] = i.ToString() }));
        }
        var world = new WorldState
        {
            ContinentSettings = cs, HistorySettings = hs, RegionSettings = rs, EmbarkCellX = pick.CellX, EmbarkCellY = pick.CellY,
            Continent = c, History = h, Population = pop, Clock = clock, Ids = ids, Chunks = chunks, Chronicle = chron,
        };
        return new Built(world, load.Database, load.Hash!);
    }

    [Fact]
    public void SaveLoadSave_IsByteIdentical_AndMeetsBudgets()
    {
        var b = Shared.Value;
        WorldSave.Capture(b.World, b.ContentHash, "0.1.0"); // warm up

        var sw = Stopwatch.StartNew();
        var bytes = SaveCodec.Encode(WorldSave.Capture(b.World, b.ContentHash, "0.1.0"));
        long saveMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var loaded = WorldSave.Restore(SaveCodec.Decode(bytes), b.Content, b.ContentHash);
        long loadMs = sw.ElapsedMilliseconds;

        var bytes2 = SaveCodec.Encode(WorldSave.Capture(loaded, b.ContentHash, "0.1.0"));
        Assert.Equal(bytes, bytes2);
        Assert.Equal(b.World.Population.ComputeHash(), loaded.Population.ComputeHash());
        Assert.Equal(b.World.Clock.Now, loaded.Clock.Now);
        Assert.Equal(b.World.Clock.GameMsRemainder, loaded.Clock.GameMsRemainder);
        Assert.Equal(b.World.Ids.GetState(), loaded.Ids.GetState());
        Assert.Equal(b.World.Chronicle.Count, loaded.Chronicle.Count);
        Assert.Equal(b.World.Chunks.Count, loaded.Chunks.Count);
        var k = new ChunkKey(10, 20, 0);
        Assert.Equal(b.World.Chunks.Rebuild(k)!.Tiles, loaded.Chunks.Rebuild(k)!.Tiles);
        Assert.Contains(EntityId.Make(IdKind.Item, 77).Value, loaded.Chunks.Rebuild(k)!.Objects.Keys);

        var header = SaveCodec.ReadHeader(new MemoryStream(bytes));
        Assert.Equal(1234UL, header.WorldSeed);

        output.WriteLine($"people={b.World.Population.People.Count} save={saveMs} ms load={loadMs} ms size={bytes.Length:N0} bytes");
        Assert.True(saveMs < TestBudget.Ms(3000), $"save {saveMs} ms");
        Assert.True(loadMs < TestBudget.Ms(10000), $"load {loadMs} ms");
        Assert.True(bytes.Length < 150L * 1024 * 1024);
    }

    [Fact]
    public void MutatedPerson_SurvivesRoundTrip()
    {
        var b = Shared.Value;
        var pop = b.World.Population;
        // Work on a decoded copy so the shared world stays pristine.
        var copy = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(b.World, b.ContentHash, "0.1.0"))), b.Content, b.ContentHash);
        var p = copy.Population.People; var hh = copy.Population.Households;
        int person = 5, newHh = hh.Count - 1;
        Assert.NotEqual(newHh, p.Household[person]);
        p.Given[person] = "Renamed";
        p.Household[person] = newHh;
        hh.Purse[newHh] += 123;

        var again = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(copy, b.ContentHash, "0.1.0"))), b.Content, b.ContentHash);
        Assert.Equal("Renamed", again.Population.People.Given[person]);
        Assert.Equal(newHh, again.Population.People.Household[person]);
        Assert.Equal(hh.Purse[newHh], again.Population.Households.Purse[newHh]);
        Assert.Equal(copy.Population.ComputeHash(), again.Population.ComputeHash());
        Assert.NotEqual(pop.ComputeHash(), again.Population.ComputeHash());
    }

    [Fact]
    public void ContentHashMismatch_GivesReadableError()
    {
        var b = Shared.Value;
        var file = SaveCodec.Decode(SaveCodec.Encode(WorldSave.Capture(b.World, b.ContentHash, "0.1.0")));
        var ex = Assert.Throws<SaveException>(() => WorldSave.Restore(file, b.Content, "deadbeef" + b.ContentHash[8..]));
        Assert.Contains("content changed", ex.Message);
    }

    [Fact]
    public void TamperedLayerHash_IsRejected()
    {
        var b = Shared.Value;
        var file = WorldSave.Capture(b.World, b.ContentHash, "0.1.0");
        var meta = SaveCodec.Deserialize<WorldMeta>(file.Sections[WorldSave.MetaSection]);
        meta.RegionHash = "00" + meta.RegionHash[2..];
        var tampered = new SaveFile(file.Header, file.Sections.Select(s => new KeyValuePair<string, byte[]>(s.Key,
            s.Key == WorldSave.MetaSection ? SaveCodec.Serialize(meta) : s.Value.ToArray())));
        var ex = Assert.Throws<SaveException>(() => WorldSave.Restore(tampered, b.Content, b.ContentHash));
        Assert.Contains("regenerated region differs", ex.Message);
    }
}
