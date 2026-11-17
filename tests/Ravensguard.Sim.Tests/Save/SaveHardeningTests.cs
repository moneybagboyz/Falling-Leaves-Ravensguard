using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Tests.Save;

/// <summary>Phase 0 review fixes for the save layer.</summary>
public class SaveHardeningTests
{
    private static SaveHeader Header(long secs = 1000) => new()
    {
        GameVersion = "0.1.0", ContentHash = "abc123", WorldSeed = 42UL, GameSeconds = secs,
        Mods = [new ModEntry("modA", "1.0")],
    };

    private static SaveFile Make(long secs, byte b = 1) => new(Header(secs), [new KeyValuePair<string, byte[]>("a", new[] { b })]);

    // ------------------------------------------------------------------ writer

    [Fact]
    public async Task Autosave_FaultAfterRotation_StillLeavesCurrentSlot()
    {
        using var d = new TempDir();
        var w = new SaveWriter(d.Path);
        await w.WriteAutosaveAsync(Make(100));
        await w.WriteAutosaveAsync(Make(200));
        var slot1 = SaveWriter.AutosavePath(d.Path, 1);
        var before = File.ReadAllBytes(slot1);

        w.AfterRotate = _ => throw new IOException("disk died after rotation");
        await Assert.ThrowsAsync<IOException>(() => w.WriteAutosaveAsync(Make(300)));

        Assert.True(File.Exists(slot1), "a current autosave slot must exist after a failed save");
        Assert.Equal(before, File.ReadAllBytes(slot1));
        Assert.Equal(200, new SaveReader().ReadHeader(slot1).GameSeconds);
        Assert.Empty(Directory.GetFiles(d.Path, "*.tmp"));
    }

    [Fact]
    public async Task Ironman_FaultAfterRotation_StillLeavesCurrentSlot()
    {
        using var d = new TempDir();
        var w = new SaveWriter(d.Path);
        await w.WriteIronmanAsync(Make(10));
        await w.WriteIronmanAsync(Make(20));
        w.AfterRotate = _ => throw new IOException("boom");
        await Assert.ThrowsAsync<IOException>(() => w.WriteIronmanAsync(Make(30)));
        Assert.Equal(20, new SaveReader().ReadHeader(SaveWriter.IronmanPath(d.Path)).GameSeconds);
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(".")]
    public void WriteManual_RejectsNamesThatAreNotBareFileNames(string name)
    {
        using var d = new TempDir();
        var w = new SaveWriter(Path.Combine(d.Path, "saves"));
        Assert.ThrowsAny<ArgumentException>(() => { _ = w.WriteManualAsync(Make(1), name); });
        Assert.False(File.Exists(Path.Combine(d.Path, "evil.sav")));
    }

    [Fact]
    public async Task WriteManual_AcceptsPlainName()
    {
        using var d = new TempDir();
        var w = new SaveWriter(d.Path);
        var p = await w.WriteManualAsync(Make(1), "my save 1");
        Assert.Equal(d.Path, Path.GetDirectoryName(p));
    }

    [Fact]
    public async Task BackgroundFailure_IsReportedThroughOnError()
    {
        using var d = new TempDir();
        var errors = new List<Exception>();
        var w = new SaveWriter(d.Path) { BeforeCommit = _ => throw new IOException("nope") };
        w.OnError += errors.Add;
        var t = w.WriteAutosaveAsync(Make(1)); // fire and forget: nobody awaits
        await Assert.ThrowsAsync<IOException>(() => t);
        Assert.Single(errors);
        Assert.IsType<IOException>(errors[0]);
    }

    // ------------------------------------------------------------------ immutability / versions / compatibility

    [Fact]
    public void SaveFile_Header_And_Sections_CannotBeMutatedThroughTheApi()
    {
        var mods = new List<ModEntry> { new("m", "1") };
        var h = new SaveHeader { Mods = mods };
        var f = new SaveFile(h, [new KeyValuePair<string, byte[]>("s", new byte[] { 1 })]);
        mods.Add(new ModEntry("x", "9"));
        Assert.Single(f.Header.Mods);                         // copied
        Assert.False(f.Header.Mods is List<ModEntry>);        // not castable back to a mutable list
        Assert.False(f.Sections is IDictionary<string, byte[]>);
        Assert.Equal(new byte[] { 1 }, f.Sections["s"].ToArray());
    }

    [Fact]
    public void Load_NewerFormat_IsRejectedBeforeTheBodyIsDecoded()
    {
        using var d = new TempDir();
        var f = new SaveFile(new SaveHeader { FormatVersion = 9, Mods = [] }, [new KeyValuePair<string, byte[]>("a", new byte[] { 1 })]);
        var bytes = SaveCodec.Encode(f);
        int hlen = BitConverter.ToInt32(bytes, 4);
        for (int i = 8 + hlen; i < bytes.Length; i++) bytes[i] = 0xC1; // 0xC1 is never valid MessagePack
        var path = Path.Combine(d.Path, "new.sav");
        File.WriteAllBytes(path, bytes);
        var ex = Assert.Throws<SaveException>(() => new SaveReader().Load(path));
        Assert.Contains("newer", ex.Message);
        Assert.Throws<SaveException>(() => SaveCodec.Decode(bytes));
    }

    [Fact]
    public void Load_Directory_IsWrappedAsSaveException()
    {
        using var d = new TempDir();
        Assert.Throws<SaveException>(() => new SaveReader().Load(d.Path));
    }

    [Fact]
    public void Compatibility_DuplicateModIds_AreReportedNotThrown()
    {
        var h = new SaveHeader { ContentHash = "h", Mods = [new ModEntry("a", "1"), new ModEntry("a", "2"), new ModEntry("b", "1")] };
        var rep = new SaveReader().CheckCompatibility(h, "h", [new ModEntry("b", "1"), new ModEntry("b", "1"), new ModEntry("c", "1")]);
        Assert.Equal(["a", "b"], rep.DuplicateModIds);
        Assert.False(rep.IsFullyCompatible);
        Assert.Contains("Duplicate mod id: a", rep.ToText());
        Assert.Equal("a", Assert.Single(rep.MissingMods.Select(m => m.Id).Distinct()));
        Assert.Equal("c", Assert.Single(rep.ExtraMods).Id);
    }

    // ------------------------------------------------------------------ chunk persistence

    private static ChunkKey K => new(0, 0, 0);
    private static ChunkContent Some() { var c = new ChunkContent(); c.Tiles[ChunkContent.TileIndex(1, 1)] = 3; return c; }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(32, 0)]
    [InlineData(0, 32)]
    public void SetTile_OutOfChunk_IsRejected(int x, int y)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkOp.SetTile(x, y, 1));
        var store = new ChunkStore();
        var bad = new ChunkOp { Kind = ChunkOpKind.SetTile, LocalX = x, LocalY = y, TileId = 1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => store.AppendDelta(K, [bad]));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void AppendDelta_RejectsMalformedOps_AndLeavesStoreUntouched()
    {
        var store = new ChunkStore();
        store.RecordFirstVisit(K, 1, "h", Some());
        var id = EntityId.Make(IdKind.Item, 1);
        Assert.Throws<ArgumentException>(() => store.AppendDelta(K, [new ChunkOp { Kind = (ChunkOpKind)77 }]));
        Assert.Throws<ArgumentException>(() => store.AppendDelta(K, [new ChunkOp { Kind = ChunkOpKind.SetProperty, ObjectId = id.Value, Key = null }]));
        Assert.Equal(0, store.DeltaOpCount(K));
    }

    [Fact]
    public void AddObject_OverExistingId_IsAnError()
    {
        var store = new ChunkStore();
        var id = EntityId.Make(IdKind.Item, 1);
        store.AppendDelta(K, [ChunkOp.AddObject(id, [1]), ChunkOp.SetProperty(id, "hp", [5])]);
        Assert.Throws<InvalidOperationException>(() => store.AppendDelta(K, [ChunkOp.AddObject(id, [2])]));
        Assert.Equal(2, store.DeltaOpCount(K));
        Assert.Equal(new byte[] { 5 }, store.Rebuild(K)!.Objects[id.Value].Props["hp"]);
        // remove then add is a legitimate replacement
        store.AppendDelta(K, [ChunkOp.RemoveObject(id), ChunkOp.AddObject(id, [3])]);
        Assert.Equal(new byte[] { 3 }, store.Rebuild(K)!.Objects[id.Value].Data);
    }

    [Fact]
    public void RecordFirstVisit_AfterDeltaOnlyEntry_StoresTheSnapshot()
    {
        var store = new ChunkStore();
        store.AppendDelta(K, [ChunkOp.SetTile(5, 5, 9)]);
        Assert.Null(store.GetSnapshot(K));
        Assert.True(store.RecordFirstVisit(K, 7, "h", Some()));
        Assert.Equal(7, store.GetSnapshot(K)!.GeneratorVersion);
        var c = store.Rebuild(K)!;
        Assert.Equal(3, c.Tiles[ChunkContent.TileIndex(1, 1)]);
        Assert.Equal(9, c.Tiles[ChunkContent.TileIndex(5, 5)]);
        Assert.False(store.RecordFirstVisit(K, 8, "h2", Some())); // now really recorded
        Assert.Equal(7, store.GetSnapshot(K)!.GeneratorVersion);
    }
}
