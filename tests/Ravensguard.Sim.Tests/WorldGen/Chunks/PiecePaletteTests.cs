using Ravensguard.Content;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Chunks;

namespace Ravensguard.Sim.Tests.WorldGen.ChunkGen;

/// <summary>
/// Phase 3 cleanup (found by the lead): tile codes name build pieces by palette number. The numbers of the legacy pieces are frozen, saved chunk sections carry their palette and are
/// renumbered when loaded under another one, and chunk hashes are taken over piece ids: adding a piece changes neither a saved chunk nor a freshly generated one.
/// </summary>
public class PiecePaletteTests(ChunkWorldFixture fx) : IClassFixture<ChunkWorldFixture>
{
    /// <summary>The base content plus a mod with one more piece whose id sorts before every other (it would have renumbered them all).</summary>
    private static ContentDatabase ContentWithExtraPiece()
    {
        var mod = Path.Combine(Path.GetTempPath(), "rg-piece-mod-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "mod.json"), """{ "id": "extra_piece", "name": "Extra piece", "version": "1.0.0", "load_after": ["base"], "dependencies": ["base"] }""");
        File.WriteAllText(Path.Combine(mod, "pieces.jsonc"), """[ { "type": "build_piece", "id": "aaa_extra_post", "name": "Post", "kind": "furniture", "glyph": "p", "material": "oak", "tags": [] } ]""");
        try
        {
            var load = new ModLoader(ContentTypeRegistry.Default).Load([ContinentSettings.FindBaseContent()!, mod]);
            Assert.True(load.Success, load.Report.ToString());
            return load.Database;
        }
        finally { Directory.Delete(mod, true); }
    }

    [Fact]
    public void Legacy_pieces_keep_their_numbers_and_new_pieces_go_after_them()
    {
        var sorted = PiecePalette.LegacyIds.OrderBy(i => i, StringComparer.Ordinal).ToArray();
        Assert.Equal(sorted, PiecePalette.LegacyIds);                                   // the frozen list is the old sorted palette
        var pal = new PiecePalette(PiecePalette.LegacyIds.Concat(["aaa_extra_post", "notice_board"]));
        for (int i = 0; i < PiecePalette.LegacyIds.Length; i++) Assert.Equal(i + 1, pal.Of(PiecePalette.LegacyIds[i]));
        Assert.Equal(PiecePalette.LegacyIds.Length + 1, pal.Of("aaa_extra_post"));
        int legacyCode = TileCode.Make(GroundKind.Dirt, 3, pal.Of("wall_stone"), pal.Of("table"));
        Assert.Equal(legacyCode, pal.HashCodeOf(legacyCode));                           // the old hashes are the old codes
        // a new piece hashes by its id: the same in a palette that numbers it differently
        var other = new PiecePalette(PiecePalette.LegacyIds.Concat(["notice_board"]));
        Assert.Equal(pal.HashCodeOf(TileCode.Make(GroundKind.Dirt, 0, 0, pal.Of("notice_board"))), other.HashCodeOf(TileCode.Make(GroundKind.Dirt, 0, 0, other.Of("notice_board"))));
        Assert.NotEqual(pal.Of("notice_board"), other.Of("notice_board"));
        Assert.Null(other.MapFrom(other.Ids.ToArray()));
        var map = pal.MapFrom(other.Ids.ToArray())!;
        Assert.Equal(pal.Of("notice_board"), map[other.Of("notice_board")]);
        Assert.Equal(pal.Of("table"), map[other.Of("table")]);
    }

    [Fact]
    public void Adding_a_piece_leaves_freshly_generated_chunks_unchanged()
    {
        var baseGen = new ChunkGenerator(fx.Region, fx.Population, fx.Content);
        var modGen = new ChunkGenerator(fx.Region, fx.Population, ContentWithExtraPiece());
        Assert.NotEqual(baseGen.Palette.Count, modGen.Palette.Count);
        var town = fx.Region.Settlements.OrderByDescending(s => s.TargetPop).First();
        int cx = town.CenterX / 32, cy = town.CenterY / 32, withPieces = 0;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                var a = baseGen.GenerateLevels(cx + dx, cy + dy); var b = modGen.GenerateLevels(cx + dx, cy + dy);
                Assert.Equal(a.ContentHash, b.ContentHash);
                if (a.Levels.Values.Any(l => l.Tiles.Values.Any(t => TileCode.Structure(t) > 0))) withPieces++;
            }
        Assert.True(withPieces > 3, "the town chunks have building pieces in them");
    }

    [Fact]
    public void Adding_a_piece_leaves_saved_chunks_unchanged_and_save_load_save_is_byte_identical()
    {
        var baseGen = new ChunkGenerator(fx.Region, fx.Population, fx.Content);
        var town = fx.Region.Settlements.OrderByDescending(s => s.TargetPop).First();
        int cx = town.CenterX / 32, cy = town.CenterY / 32;
        var store = new ChunkStore();
        var s1 = new ChunkStreamer(baseGen, store);
        s1.LoadAround(cx, cy, 1);
        Assert.Equal(baseGen.Palette.Ids, store.Palette);
        // the player put a training dummy down (a SetTile delta numbered by the base palette)
        var key = store.Keys.First(k => k.Z == 0);
        store.AppendDelta(key, [ChunkOp.SetTile(4, 4, TileCode.Make(GroundKind.Dirt, 0, 0, baseGen.Palette.Of("training_dummy")))]);
        var section = store.ToSection();
        Assert.Equal(section, ChunkStore.FromSection(section).ToSection());               // save -> load -> save

        // the content gains a piece that sorts before all the others: the saved chunks are renumbered on load and name the same pieces
        var modGen = new ChunkGenerator(fx.Region, fx.Population, ContentWithExtraPiece());
        Assert.NotEqual(baseGen.Palette.Of("training_dummy"), modGen.Palette.Of("training_dummy"));
        var loaded = ChunkStore.FromSection(section);
        var s2 = new ChunkStreamer(modGen, loaded);
        Assert.Equal(modGen.Palette.Ids, loaded.Palette);
        string Named(ChunkContent c, PiecePalette p) => string.Join(";", c.Tiles.Select(kv => $"{kv.Key}:{(int)TileCode.Ground(kv.Value)}/{TileCode.Variant(kv.Value)}/{p.Id(TileCode.Structure(kv.Value))}/{p.Id(TileCode.Fixture(kv.Value))}"));
        foreach (var k in store.Keys)
        {
            Assert.Equal(Named(store.Rebuild(k)!, baseGen.Palette), Named(loaded.Rebuild(k)!, modGen.Palette));
            Assert.Equal(store.GetSnapshot(k)?.ContentHash, loaded.GetSnapshot(k)?.ContentHash);
            if (store.GetSnapshot(k) != null) Assert.Equal(store.GetSnapshot(k)!.ContentHash, modGen.GenerateLevels(k.X, k.Y).ContentHash);
        }
        Assert.Equal("training_dummy", modGen.Palette.Id(TileCode.Fixture(loaded.Rebuild(key)!.Tiles[ChunkContent.TileIndex(4, 4)])));
        // and under the new content a save -> load -> save is byte-identical again
        var section2 = loaded.ToSection();
        var again = ChunkStore.FromSection(section2); _ = new ChunkStreamer(modGen, again);
        Assert.Equal(section2, again.ToSection());
    }

    [Fact]
    public void A_v3_chunk_section_is_stamped_with_the_legacy_palette()
    {
        var store = new ChunkStore();
        var c = new ChunkContent(); c.Tiles[5] = TileCode.Make(GroundKind.Dirt, 0, 3, 0);
        store.RecordFirstVisit(new ChunkKey(1, 2, 0), 1, "h", c);
        var v3 = store.ToSection();
        Assert.Empty(SaveCodec.DeserializeCompressed<ChunkStoreData>(v3).Palette);
        var header = new SaveHeader { GameVersion = "0.1.0", ContentHash = "abc", WorldSeed = 1, GameSeconds = 10, FormatVersion = 3, Mods = [] };
        var file = new SaveFile(header, [new(WorldSave.ChunksSection, v3)]);
        var migrated = SaveMigrator.CreateDefault().Migrate(file);
        Assert.Equal(SaveFormat.CurrentVersion, migrated.Header.FormatVersion);
        var data = SaveCodec.DeserializeCompressed<ChunkStoreData>(migrated.Sections[WorldSave.ChunksSection]);
        Assert.Equal(PiecePalette.LegacyIds, data.Palette);
        Assert.Equal(c.Tiles, ChunkStore.FromSection(migrated.Sections[WorldSave.ChunksSection]).Rebuild(new ChunkKey(1, 2, 0))!.Tiles);
    }
}
