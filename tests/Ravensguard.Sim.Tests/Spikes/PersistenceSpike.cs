using System.Security.Cryptography;
using System.Text;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Spikes;

/// <summary>Spike 4: visited chunks survive content/generator changes; unvisited chunks pick up the new content (plan 2.2).</summary>
public class PersistenceSpike(ITestOutputHelper output)
{
    private sealed record TreeDef(string Id, int Weight);
    private sealed record TinyMod(string Name, TreeDef[] Trees)
    {
        public string Hash()
        {
            var sb = new StringBuilder(Name);
            foreach (var t in Trees.OrderBy(t => t.Id, StringComparer.Ordinal)) sb.Append('|').Append(t.Id).Append(':').Append(t.Weight);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
        }
    }

    private const ulong Seed = 0xC0FFEEUL;
    private static readonly uint GenStream = 77;
    private static readonly byte[] WallData = Encoding.UTF8.GetBytes("wall;oak_planks;10;10");

    /// <summary>Fake generator: 64 candidate slots; each places a tree whose type is picked by weight from the mod's defs.
    /// Object ids depend only on (seed, chunk, slot), never on content: that is what makes ID-keyed deltas robust.</summary>
    private static ChunkContent Generate(ChunkKey k, int genVersion, TinyMod mod)
    {
        var c = new ChunkContent();
        ulong chunkId = ((ulong)(uint)k.X << 32) | (uint)k.Y;
        int total = mod.Trees.Sum(t => t.Weight);
        for (int slot = 0; slot < 64; slot++)
        {
            ulong id = DetRng.Next(Seed, 0, chunkId, GenStream, (uint)slot * 4) & 0x00FFFFFFFFFFFFFFUL;
            if (DetRng.Range(Seed, 0, chunkId, GenStream, (uint)slot * 4 + 1, 0, 100) >= 40) continue; // 40% of slots hold a tree
            int roll = DetRng.Range(Seed, 0, chunkId, GenStream, (uint)slot * 4 + 2, 0, total), pick = 0;
            while (roll >= mod.Trees[pick].Weight) roll -= mod.Trees[pick++].Weight;
            int x = slot % 8 * 4, y = slot / 8 * 4;
            var eid = EntityId.Make(IdKind.Other, id);
            c.Objects[eid.Value] = new ObjectRecord { Id = eid.Value, Data = Encoding.UTF8.GetBytes($"{mod.Trees[pick].Id};{x};{y}") };
        }
        c.Tiles[ChunkContent.TileIndex(0, 0)] = genVersion; // tile ground type depends on generator version
        return c;
    }

    private sealed class World(TinyMod mod, int genVersion, ChunkStore store)
    {
        public ChunkStore Store { get; } = store;
        public ChunkContent Visit(ChunkKey k)
        {
            if (!Store.Contains(k)) Store.RecordFirstVisit(k, genVersion, mod.Hash(), Generate(k, genVersion, mod));
            return Store.Rebuild(k)!;
        }
    }

    private static byte[] Bytes(ChunkContent c) => SaveCodec.Serialize(c);

    private static byte[] Save(ChunkStore s, string contentHash) => SaveCodec.Encode(new SaveFile(
        new SaveHeader { GameVersion = "0.1.0", ContentHash = contentHash, WorldSeed = Seed },
        new[] { new KeyValuePair<string, byte[]>(ChunkStore.SectionName, s.ToSection()) }));

    private static ChunkStore Load(byte[] file) => ChunkStore.FromSection(SaveCodec.Decode(file).Sections[ChunkStore.SectionName]);

    [Fact]
    public void VisitedChunksSurviveContentChange_UnvisitedRegenerate()
    {
        var modV1 = new TinyMod("base", new[] { new TreeDef("oak", 3), new TreeDef("birch", 2) });
        var world = new World(modV1, 1, new ChunkStore());
        var chunkA = new ChunkKey(1, 2, 0);
        var chunkB = new ChunkKey(5, 5, 0);

        // (a)+(b): visit, then change by stable ids
        var fresh = world.Visit(chunkA);
        Assert.True(fresh.Objects.Count > 5);
        ulong tree = fresh.Objects.Keys.First();
        var wallId = EntityId.Make(IdKind.Parcel, 1);
        world.Store.AppendDelta(chunkA, new[] { ChunkOp.RemoveObject(new EntityId(tree)), ChunkOp.AddObject(wallId, WallData), ChunkOp.SetTile(10, 10, 99) });
        var beforeChange = world.Visit(chunkA);
        Assert.DoesNotContain(tree, beforeChange.Objects.Keys);
        Assert.Contains(wallId.Value, beforeChange.Objects.Keys);
        var beforeBytes = Bytes(beforeChange);
        var file1 = Save(world.Store, modV1.Hash());

        // What generator v1 would put in chunk B (never visited) and what A would look like if (wrongly) regenerated later
        var bOld = Generate(chunkB, 1, modV1);

        // (c): content change: a new tree type that dominates, plus generator version bump
        var modV2 = new TinyMod("base", new[] { new TreeDef("oak", 3), new TreeDef("birch", 2), new TreeDef("pine", 20) });
        Assert.NotEqual(modV1.Hash(), modV2.Hash());
        var reloaded = new World(modV2, 2, Load(file1));

        // Visited chunk: identical to before (same ids, same types, removed tree still gone, wall present)
        var afterA = reloaded.Visit(chunkA);
        Assert.Equal(beforeBytes, Bytes(afterA));
        Assert.DoesNotContain("pine", afterA.Objects.Values.Select(o => Encoding.UTF8.GetString(o.Data)).Where(s => s.StartsWith("pine")).FirstOrDefault() ?? "");
        var snap = reloaded.Store.GetSnapshot(chunkA)!;
        Assert.Equal(1, snap.GeneratorVersion);
        Assert.Equal(modV1.Hash(), snap.ContentHash);

        // Control: regenerating A under the new content would have changed it (so the test is meaningful)
        var wouldBe = Generate(chunkA, 2, modV2);
        Assert.NotEqual(Bytes(Generate(chunkA, 1, modV1)), Bytes(wouldBe));
        Assert.Contains(wouldBe.Objects.Values, o => Encoding.UTF8.GetString(o.Data).StartsWith("pine"));

        // Unvisited chunk: generated now with the new content and stamped with the new versions
        Assert.False(reloaded.Store.Contains(chunkB));
        var bNew = reloaded.Visit(chunkB);
        Assert.NotEqual(Bytes(bOld), Bytes(bNew));
        Assert.Contains(bNew.Objects.Values, o => Encoding.UTF8.GetString(o.Data).StartsWith("pine"));
        Assert.Equal(2, reloaded.Store.GetSnapshot(chunkB)!.GeneratorVersion);
        Assert.Equal(modV2.Hash(), reloaded.Store.GetSnapshot(chunkB)!.ContentHash);

        // Same ids across versions: ids come from (seed, chunk, slot), so a delta keyed by id still addresses the same object
        Assert.True(bOld.Objects.Keys.Intersect(bNew.Objects.Keys).Count() > bOld.Objects.Count / 2);

        // Save -> load -> save is byte-identical
        var file2 = Save(reloaded.Store, modV2.Hash());
        var file3 = Save(Load(file2), modV2.Hash());
        Assert.Equal(file2, file3);

        int treesA = fresh.Objects.Count;
        output.WriteLine($"chunk A: {treesA} objects at first visit, snapshot {world.Store.GetSnapshot(chunkA)!.Payload.Length} B, delta ops {world.Store.DeltaOpCount(chunkA)}; " +
                         $"save file {file1.Length} B for 1 chunk, {file2.Length} B for 2");
        output.WriteLine($"B regenerated under v2: {bNew.Objects.Count} objects, pine {bNew.Objects.Values.Count(o => Encoding.UTF8.GetString(o.Data).StartsWith("pine"))}; " +
                         $"{bOld.Objects.Keys.Intersect(bNew.Objects.Keys).Count()}/{bOld.Objects.Count} ids shared with v1 generation");
    }

    [Fact]
    public void DeltaOnRemovedObject_IsIgnored_AfterReload()
    {
        var mod = new TinyMod("base", new[] { new TreeDef("oak", 1) });
        var w = new World(mod, 1, new ChunkStore());
        var k = new ChunkKey(0, 0, 0);
        var c = w.Visit(k);
        var id = new EntityId(c.Objects.Keys.First());
        w.Store.AppendDelta(k, new[] { ChunkOp.RemoveObject(id), ChunkOp.SetProperty(id, "burning", new byte[] { 1 }) });
        var back = Load(Save(w.Store, mod.Hash())).Rebuild(k)!;
        Assert.DoesNotContain(id.Value, back.Objects.Keys);
    }
}
