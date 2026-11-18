using System.Diagnostics;
using MessagePack;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Save;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rvsg_" + Guid.NewGuid().ToString("N"));
    public TempDir() { Directory.CreateDirectory(Path); }
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public class SaveTests
{
    private static SaveHeader Header(long secs = 1000, bool ironman = false) => new()
    {
        GameVersion = "0.1.0", ContentHash = "abc123", WorldSeed = 42UL, GameSeconds = secs, Ironman = ironman,
        Mods = [new ModEntry("modA", "1.0"), new ModEntry("modB", "2.1")],
    };

    private static SaveFile Make(long secs = 1000, bool ironman = false, params (string, byte[])[] sections)
        => new(Header(secs, ironman), sections.Select(s => new KeyValuePair<string, byte[]>(s.Item1, s.Item2)));

    [Fact]
    public void RoundTrip_IsByteIdentical()
    {
        var f = Make(5000, false, ("people", new byte[] { 1, 2, 3 }), ("economy", new byte[] { 9 }));
        var b1 = SaveCodec.Encode(f);
        var loaded = SaveCodec.Decode(b1);
        var b2 = SaveCodec.Encode(loaded);
        Assert.Equal(b1, b2);
        Assert.Equal(new GameTime(5000), loaded.Header.Time);
        Assert.Equal(42UL, loaded.Header.WorldSeed);
        Assert.Equal(new byte[] { 1, 2, 3 }, loaded.Sections["people"].ToArray());
    }

    [Fact]
    public void SectionOrder_DoesNotAffectBytes()
    {
        var a = Make(1, false, ("z", new byte[] { 1 }), ("a", new byte[] { 2 }), ("m", new byte[] { 3 }));
        var b = Make(1, false, ("m", new byte[] { 3 }), ("z", new byte[] { 1 }), ("a", new byte[] { 2 }));
        Assert.Equal(SaveCodec.Encode(a), SaveCodec.Encode(b));
        Assert.Equal(new[] { "a", "m", "z" }, a.Sections.Keys.ToArray());
    }

    [Fact]
    public void Snapshot_IsImmutableCopy()
    {
        var data = new byte[] { 1, 2 };
        var f = Make(1, false, ("s", data));
        data[0] = 99;
        Assert.Equal(1, f.Sections["s"].Span[0]);
    }

    [Fact]
    public void Migration_ChainRuns()
    {
        var old = new SaveFile(new SaveHeader { FormatVersion = 1, GameVersion = "0", ContentHash = "h" },
            new[] { new KeyValuePair<string, byte[]>("a", new byte[] { 1 }) });
        var m = new SaveMigrator(3)
            .Register(1, s => s["b"] = new byte[] { 2 })
            .Register(2, s => { s["a"] = new byte[] { 10 }; });
        var r = m.Migrate(old);
        Assert.Equal(3, r.Header.FormatVersion);
        Assert.Equal(new byte[] { 2 }, r.Sections["b"].ToArray());
        Assert.Equal(new byte[] { 10 }, r.Sections["a"].ToArray());
    }

    [Fact]
    public void Migration_MissingStep_IsClearError()
    {
        var old = new SaveFile(new SaveHeader { FormatVersion = 1 }, Array.Empty<KeyValuePair<string, byte[]>>());
        var m = new SaveMigrator(3).Register(1, _ => { });
        var ex = Assert.Throws<SaveException>(() => m.Migrate(old));
        Assert.Contains("v2", ex.Message);
    }

    [Fact]
    public void Migration_NewerSave_Rejected()
    {
        var f = new SaveFile(new SaveHeader { FormatVersion = 9 }, Array.Empty<KeyValuePair<string, byte[]>>());
        Assert.Throws<SaveException>(() => new SaveMigrator().Migrate(f));
    }

    private static ChunkContent Content()
    {
        var c = new ChunkContent();
        c.Tiles[ChunkContent.TileIndex(1, 2)] = 7;
        c.Objects[EntityId.Make(IdKind.Item, 1).Value] = new ObjectRecord { Id = EntityId.Make(IdKind.Item, 1).Value, Data = new byte[] { 5 } };
        c.Objects[EntityId.Make(IdKind.Item, 2).Value] = new ObjectRecord { Id = EntityId.Make(IdKind.Item, 2).Value, Data = new byte[] { 6 } };
        return c;
    }

    [Fact]
    public void ChunkStore_SnapshotPlusDeltas_Rebuild()
    {
        var store = new ChunkStore();
        var key = new ChunkKey(3, -4, -2);
        var i1 = EntityId.Make(IdKind.Item, 1); var i2 = EntityId.Make(IdKind.Item, 2); var i3 = EntityId.Make(IdKind.Item, 3);
        Assert.True(store.RecordFirstVisit(key, 4, "h", Content()));
        Assert.False(store.RecordFirstVisit(key, 5, "h2", new ChunkContent())); // snapshot kept from first visit
        Assert.Equal(4, store.GetSnapshot(key)!.GeneratorVersion);

        store.AppendDelta(key, new[]
        {
            ChunkOp.SetProperty(i1, "hp", new byte[] { 50 }),
            ChunkOp.RemoveObject(i2),
            ChunkOp.AddObject(i3, new byte[] { 8 }),
            ChunkOp.SetTile(1, 2, 9),
            ChunkOp.SetProperty(i2, "hp", new byte[] { 1 }), // removed: ignored
        });
        var c = store.Rebuild(key)!;
        Assert.Equal(9, c.Tiles[ChunkContent.TileIndex(1, 2)]);
        Assert.False(c.Objects.ContainsKey(i2.Value));
        Assert.Equal(new byte[] { 8 }, c.Objects[i3.Value].Data);
        Assert.Equal(new byte[] { 50 }, c.Objects[i1.Value].Props["hp"]);

        // survives section round trip, deterministically
        var s1 = store.ToSection();
        var again = ChunkStore.FromSection(s1);
        Assert.Equal(s1, again.ToSection());
        Assert.Equal(9, again.Rebuild(key)!.Tiles[ChunkContent.TileIndex(1, 2)]);
    }

    [Fact]
    public void ChunkStore_ZLevels_AreDistinctAndValidated()
    {
        var store = new ChunkStore();
        for (int z = ChunkKey.MinZ; z <= ChunkKey.MaxZ; z++)
            Assert.True(store.RecordFirstVisit(new ChunkKey(0, 0, z), 1, "h", Content()));
        Assert.Equal(9, store.Count);
        Assert.Equal(-5, store.Keys.First().Z);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.RecordFirstVisit(new ChunkKey(0, 0, 4), 1, "h", Content()));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.AppendDelta(new ChunkKey(0, 0, -6), new[] { ChunkOp.SetTile(0, 0, 1) }));
    }

    [Fact]
    public void ChunkStore_EmptyChunks_NotStored()
    {
        var store = new ChunkStore();
        Assert.False(store.RecordFirstVisit(new ChunkKey(0, 0, 0), 1, "h", new ChunkContent()));
        Assert.Equal(0, store.Count);
        Assert.Null(store.Rebuild(new ChunkKey(0, 0, 0)));
    }

    [Fact]
    public async Task Writer_AtomicWrite_NoPartialFileOnFailure()
    {
        using var d = new TempDir();
        var w = new SaveWriter(d.Path);
        var good = Make(1, false, ("a", new byte[] { 1 }));
        var p = await w.WriteAutosaveAsync(good);
        var before = File.ReadAllBytes(p);

        w.BeforeCommit = _ => throw new IOException("simulated disk failure");
        await Assert.ThrowsAsync<IOException>(() => w.WriteAutosaveAsync(Make(2, false, ("a", new byte[] { 2 }))));
        Assert.Equal(before, File.ReadAllBytes(p));
        Assert.Empty(Directory.GetFiles(d.Path, "*.tmp"));
        Assert.Single(Directory.GetFiles(d.Path)); // no rotation happened either
    }

    [Fact]
    public async Task Writer_FirstWriteFailure_LeavesNothing()
    {
        using var d = new TempDir();
        var w = new SaveWriter(d.Path) { BeforeCommit = _ => throw new IOException("x") };
        await Assert.ThrowsAsync<IOException>(() => w.WriteManualAsync(Make(), "slot"));
        Assert.Empty(Directory.GetFiles(d.Path));
    }

    [Fact]
    public async Task Autosave_RotatesThreeSlots()
    {
        using var d = new TempDir();
        var w = new SaveWriter(d.Path);
        var r = new SaveReader();
        for (int i = 1; i <= 5; i++) await w.WriteAutosaveAsync(Make(i * 100));
        Assert.Equal(500, r.ReadHeader(SaveWriter.AutosavePath(d.Path, 1)).GameSeconds);
        Assert.Equal(400, r.ReadHeader(SaveWriter.AutosavePath(d.Path, 2)).GameSeconds);
        Assert.Equal(300, r.ReadHeader(SaveWriter.AutosavePath(d.Path, 3)).GameSeconds);
        Assert.False(File.Exists(SaveWriter.AutosavePath(d.Path, 4)));
        Assert.Equal(3, Directory.GetFiles(d.Path).Length);
    }

    [Fact]
    public async Task Ironman_SingleSlotPlusTwoBackups()
    {
        using var d = new TempDir();
        var w = new SaveWriter(d.Path);
        var r = new SaveReader();
        for (int i = 1; i <= 4; i++) await w.WriteIronmanAsync(Make(i * 10, true));
        Assert.Equal(40, r.ReadHeader(SaveWriter.IronmanPath(d.Path)).GameSeconds);
        Assert.Equal(30, r.ReadHeader(SaveWriter.IronmanBackupPath(d.Path, 1)).GameSeconds);
        Assert.Equal(20, r.ReadHeader(SaveWriter.IronmanBackupPath(d.Path, 2)).GameSeconds);
        Assert.Equal(3, Directory.GetFiles(d.Path).Length);
    }

    [Fact]
    public async Task Writer_ReturnsTaskAndLoadsBack()
    {
        using var d = new TempDir();
        var w = new SaveWriter(d.Path);
        var f = Make(77, false, ("x", new byte[] { 4, 5 }));
        var p = await w.WriteManualAsync(f, "mysave");
        var loaded = new SaveReader().Load(p);
        Assert.Equal(SaveCodec.Encode(f), SaveCodec.Encode(loaded));
    }

    [Fact]
    public async Task Reader_HeaderOnly_DoesNotNeedBody()
    {
        using var d = new TempDir();
        var f = Make(123, true, ("big", new byte[10000]));
        var bytes = SaveCodec.Encode(f);
        var path = Path.Combine(d.Path, "trunc.sav");
        // keep only magic + header: body is gone, header still readable
        int hlen = BitConverter.ToInt32(bytes, 4);
        await File.WriteAllBytesAsync(path, bytes[..(8 + hlen)]);
        var h = new SaveReader().ReadHeader(path);
        Assert.Equal(123, h.GameSeconds);
        Assert.True(h.Ironman);
        Assert.Equal("abc123", h.ContentHash);
        Assert.Throws<SaveException>(() => new SaveReader().Load(path));
    }

    [Fact]
    public void Reader_BadMagic_Throws()
    {
        using var d = new TempDir();
        var p = Path.Combine(d.Path, "x.sav");
        File.WriteAllBytes(p, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        Assert.Throws<SaveException>(() => new SaveReader().ReadHeader(p));
    }

    [Fact]
    public void Compatibility_Report()
    {
        var r = new SaveReader();
        var h = Header();
        var ok = r.CheckCompatibility(h, "abc123", new[] { new ModEntry("modA", "1.0"), new ModEntry("modB", "2.1") });
        Assert.True(ok.IsFullyCompatible);
        Assert.Contains("Compatible", ok.ToText());

        var bad = r.CheckCompatibility(h, "zzz", new[] { new ModEntry("modA", "1.5"), new ModEntry("modC", "1.0") });
        Assert.False(bad.ContentHashMatches);
        Assert.Equal("modB", Assert.Single(bad.MissingMods).Id);
        Assert.Equal("modA", Assert.Single(bad.ChangedMods).Id);
        Assert.Equal("modC", Assert.Single(bad.ExtraMods).Id);
        Assert.True(bad.CanLoad);
        var text = bad.ToText();
        Assert.Contains("Missing mod: modB", text);
        Assert.Contains("saved with 1.0, installed 1.5", text);

        var newer = h with { FormatVersion = 99 };
        Assert.False(r.CheckCompatibility(newer, "abc123", h.Mods.ToList()).CanLoad);
    }

    [MessagePackObject]
    public sealed class DummyPerson
    {
        [Key(0)] public ulong Id { get; set; }
        [Key(1)] public string Name { get; set; } = "";
        [Key(2)] public int[] Skills { get; set; } = Array.Empty<int>();
        [Key(3)] public byte[] Blob { get; set; } = Array.Empty<byte>();
        [Key(4)] public string Notes { get; set; } = "";
    }

    /// <summary>The save size and time budget (Phase 3 cleanup: wall-clock, so in the Timing collection).</summary>
    [Collection(TimingCollection.Name)]
    public class Timing(ITestOutputHelper output)
    {
        [Fact]
        public async Task SizeAndTime_10kPeople()
        {
            var rng = new Random(1);
            var people = new List<DummyPerson>();
            for (int i = 0; i < 10_000; i++)
            {
                var blob = new byte[512]; rng.NextBytes(blob);          // incompressible worst case
                people.Add(new DummyPerson
                {
                    Id = EntityId.Make(IdKind.Person, (ulong)i + 1).Value,
                    Name = "Person " + i,
                    Skills = Enumerable.Range(0, 40).Select(_ => rng.Next(0, 100)).ToArray(),
                    Blob = blob,
                    Notes = new string('x', 200) + i,
                });
            }
            var raw = SaveCodec.Serialize(people);
            output.WriteLine($"raw people section: {raw.Length / 1024} KB ({raw.Length / people.Count} B/person)");

            using var d = new TempDir();
            var w = new SaveWriter(d.Path);
            var sw = Stopwatch.StartNew();
            var snap = new SaveFile(Header(), new[] { new KeyValuePair<string, byte[]>("people", raw) });
            var captureMs = sw.ElapsedMilliseconds;
            var p = await w.WriteAutosaveAsync(snap);
            sw.Stop();
            var size = new FileInfo(p).Length;
            output.WriteLine($"snapshot capture: {captureMs} ms; total save: {sw.ElapsedMilliseconds} ms; file: {size / 1024} KB");
            Assert.True(sw.Elapsed.TotalMilliseconds < TestBudget.Ms(3000), $"save took {sw.Elapsed.TotalSeconds}s");
            Assert.True(size < 20 * 1024 * 1024);

            sw.Restart();
            var loaded = new SaveReader().Load(p);
            output.WriteLine($"load: {sw.ElapsedMilliseconds} ms");
            Assert.Equal(raw, loaded.Sections["people"].ToArray());
        }
    }
}
