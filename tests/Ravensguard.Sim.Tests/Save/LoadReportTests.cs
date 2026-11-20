using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Worlds;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.Tests.Save;

/// <summary>
/// The pre-load report (plan 2.11, phase-5-open "Still a gate"): a save made with other content loads, and the report names what the change does; it is refused only for a layer
/// that no longer regenerates identically or content in use that is gone, each naming its cause.
/// </summary>
public class LoadReportTests
{
    private static (ContentDatabase Db, string Hash) LoadWith(string name, string? goods)
    {
        using var mod = new TempDir(); using var cache = new TempDir();
        File.WriteAllText(Path.Combine(mod.Path, "mod.json"), $$"""{ "id": "{{name}}", "name": "{{name}}", "version": "1.0.0", "load_after": ["base"], "dependencies": ["base"] }""");
        if (goods != null) File.WriteAllText(Path.Combine(mod.Path, "goods.jsonc"), goods);
        var load = new ModLoader(ContentTypeRegistry.Default).Load([ContinentSettings.FindBaseContent()!, mod.Path], cache.Path);
        Assert.True(load.Success, load.Report.ToString());
        return (load.Database, load.Hash!);
    }

    // content A: the base game plus a mod with a good nobody holds; content B: the base game plus a new good, a good migration and a recipe migration (the mod of A is gone)
    private static readonly Lazy<(ContentDatabase Db, string Hash)> A = new(() => LoadWith("lr_a", """
        [ { "type": "good", "id": "aa_unused_scrap", "name": "Unused scrap", "class": "hardware", "base_price_mites_per_kg": 5, "spoil_class": "iron" } ]
        """));
    private static readonly Lazy<(ContentDatabase Db, string Hash)> B = new(() => LoadWith("lr_b", """
        [
          { "type": "good", "id": "hemp", "name": "Hemp", "class": "fibre", "base_price_mites_per_kg": 3, "spoil_class": "inert" },
          { "type": "migration", "target_type": "craft_recipe", "from": "bake_bread_old", "to": "bake_bread" }
        ]
        """));

    private static ModEntry[] ModsA => [new("base", "1.0.0"), new("lr_a", "1.0.0")];
    private static ModEntry[] ModsB => [new("base", "1.0.0"), new("lr_b", "1.0.0")];

    private static byte[] Bytes(WorldState w, string hash, ModEntry[] mods) => SaveCodec.Encode(WorldSave.Capture(w, hash, "0.1.0", mods));

    /// <summary>A world on content A whose hero knows a recipe the save names by its old id (renamed since, with a migration).</summary>
    private static WorldState WorldOnA()
    {
        var w = EconFixture.WorldWith(EconomyState_(A.Value.Db)); var cs = new CraftState(); w.Crafting = cs;
        var db = A.Value.Db; var rdb = RecipeDb.Build(db, EconFixture.Catalog());
        cs.Know.Learn(EconomyState.HeroOwner, (ushort)rdb["bake_bread"].Index);
        return w;
    }

    private static EconomyState EconomyState_(ContentDatabase db) => EconomyState.Create(EconFixture.Pop, db, EconFixture.Seed);

    private static SaveFile Renamed(byte[] bytes)
    {
        // the save of A names the recipe "bake_bread_old": its table says so (the content has since renamed it, with a migration)
        var file = SaveCodec.Decode(bytes);
        var t = IdTables.FromSave(SaveCodec.Deserialize<IdTablesSave>(file.Sections[IdTablesSave.SectionName].ToArray()));
        var recipes = (t.Get(IdTables.CraftRecipes) ?? IdTables.Of(A.Value.Db).Tables.Get(IdTables.CraftRecipes)!).ToArray(); int at = Array.IndexOf(recipes, "bake_bread"); recipes[at] = "bake_bread_old"; Array.Sort(recipes, StringComparer.Ordinal);
        Assert.Equal(at, Array.IndexOf(recipes, "bake_bread_old"));   // the rename keeps the row (the old id sorts where the new one does)
        t.Tables[IdTables.CraftRecipes] = recipes;
        var sections = file.Sections.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()); sections[IdTablesSave.SectionName] = SaveCodec.Serialize(t.ToSave());
        return new SaveFile(file.Header, sections);
    }

    [Fact]
    public void ASaveOfContentA_LoadsOnContentB_TheReportListsExactlyWhatChanged_AndSaveLoadSaveIsStable()
    {
        var file = Renamed(Bytes(WorldOnA(), A.Value.Hash, ModsA));
        var rep = WorldSave.Inspect(file, B.Value.Db, B.Value.Hash, ModsB);
        Assert.True(rep.CanLoad, rep.ToText()); Assert.True(rep.NeedsConfirmation);
        Assert.True(rep.ContentChanged); Assert.True(rep.LayersChecked);
        Assert.Equal("lr_a", Assert.Single(rep.MissingMods).Id); Assert.Equal("lr_b", Assert.Single(rep.ExtraMods).Id); Assert.Empty(rep.ChangedMods);
        // the goods: one new (hemp), the old mod's good gone and unused; the recipe moved by its migration
        var good = Assert.Single(rep.Changes, c => c.Contains("'good'"));
        Assert.Contains("1 new", good); Assert.Contains("aa_unused_scrap", good);
        var recipe = Assert.Single(rep.Changes, c => c.Contains("'craft_recipe'"));
        Assert.Contains("bake_bread_old -> bake_bread", recipe);
        Assert.Equal(2, rep.Changes.Count);                                                   // nothing written off, nothing else
        var text = rep.ToText();
        Assert.Contains("Missing mod: lr_a", text); Assert.Contains("New mod not in this save: lr_b", text); Assert.Contains("still regenerate exactly as saved", text);

        var st = WorldSave.Restore(file, B.Value.Db, B.Value.Hash, ModsB);
        var rdb = RecipeDb.Build(B.Value.Db, st.Economy!.Goods);
        Assert.Equal([rdb["bake_bread"].Index], st.Crafting!.Know.LearntBy(EconomyState.HeroOwner).Select(i => (int)i).ToArray());
        Assert.True(st.Economy.Goods.TryIdOf("hemp", out _)); Assert.False(st.Economy.Goods.TryIdOf("aa_unused_scrap", out _));
        var b1 = Bytes(st, B.Value.Hash, ModsB);
        var back = WorldSave.Restore(SaveCodec.Decode(b1), B.Value.Db, B.Value.Hash, ModsB);
        Assert.Empty(back.LoadNotes);
        Assert.Equal(b1, Bytes(back, B.Value.Hash, ModsB));
        // and its own report is clean
        Assert.False(WorldSave.Inspect(SaveCodec.Decode(b1), B.Value.Db, B.Value.Hash, ModsB).NeedsConfirmation);
    }

    [Fact]
    public void ASaveOfTheSameContent_NeedsNoConfirmation_AndChecksNoLayers()
    {
        var file = SaveCodec.Decode(Bytes(WorldOnA(), A.Value.Hash, ModsA));
        var rep = WorldSave.Inspect(file, A.Value.Db, A.Value.Hash, ModsA);
        Assert.False(rep.NeedsConfirmation); Assert.False(rep.ContentChanged); Assert.False(rep.LayersChecked); Assert.True(rep.CanLoad);
        Assert.Equal("Nothing changed.", rep.ToText());
    }

    [Fact]
    public void ALayerThatWouldRegenerateDifferently_IsRefused_NamingIt_BothByTheReportAndTheLoad()
    {
        var file = SaveCodec.Decode(Bytes(WorldOnA(), A.Value.Hash, ModsA));
        var meta = SaveCodec.Deserialize<WorldMeta>(file.Sections[WorldSave.MetaSection].ToArray());
        meta.RegionHash = "0000" + meta.RegionHash[4..];                                      // the region this content regenerates is not the one the save was made on
        var sections = file.Sections.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()); sections[WorldSave.MetaSection] = SaveCodec.Serialize(meta);
        var bad = new SaveFile(file.Header, sections);
        var rep = WorldSave.Inspect(bad, B.Value.Db, B.Value.Hash, ModsB);
        Assert.False(rep.CanLoad); Assert.False(rep.NeedsConfirmation); Assert.True(rep.LayersChecked);
        var why = Assert.Single(rep.Refusals);
        Assert.Contains("regenerated region differs", why); Assert.Contains("Cannot load: ", rep.ToText());
        var ex = Assert.Throws<SaveException>(() => WorldSave.Restore(bad, B.Value.Db, B.Value.Hash, ModsB));
        Assert.Contains("regenerated region differs", ex.Message);
    }

    [Fact]
    public void ContentInUseThatIsGone_IsRefused_NamingTheCauseAndTheMissingMod()
    {
        var w = WorldOnA(); var cs = (CraftState)w.Crafting!;
        cs.Add(new CraftJob { Id = EntityId.Make(IdKind.Other, 9), Recipe = 3, Crafter = EconomyState.HeroOwner, Owner = EconomyState.HeroOwner, Choice = [] });
        var file = SaveCodec.Decode(Bytes(w, A.Value.Hash, ModsA));
        var t = IdTables.FromSave(SaveCodec.Deserialize<IdTablesSave>(file.Sections[IdTablesSave.SectionName].ToArray()));
        var recipes = (t.Get(IdTables.CraftRecipes) ?? IdTables.Of(A.Value.Db).Tables.Get(IdTables.CraftRecipes)!).ToArray(); recipes[3] = "zz_recipe_of_a_removed_mod"; t.Tables[IdTables.CraftRecipes] = recipes;
        var sections = file.Sections.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()); sections[IdTablesSave.SectionName] = SaveCodec.Serialize(t.ToSave());
        var rep = WorldSave.Inspect(new SaveFile(file.Header, sections), B.Value.Db, B.Value.Hash, ModsB);
        Assert.False(rep.CanLoad);
        var why = Assert.Single(rep.Refusals);
        Assert.Contains("zz_recipe_of_a_removed_mod", why); Assert.Contains("Mods missing since the save was made: lr_a", why);
    }

    [Fact]
    public void ALibraryWorldOfOtherContent_IsReported_AndOpens_WhenItStillRegeneratesTheSame()
    {
        using var d = new TempDir(); var lib = new WorldLibrary(d.Path);
        var cs = new ContinentSettings { Seed = 7 }; var hs = new HistorySettings { Years = 20 };
        var made = lib.Create("Report world", cs, hs, "oldhash", "t");
        var info = lib.TryRead(made.Info.Id)!;
        var rep = LoadReport.ForWorld(info.File, "newhash");
        Assert.True(rep.ContentChanged); Assert.True(rep.NeedsConfirmation); Assert.True(rep.CanLoad);
        Assert.False(LoadReport.ForWorld(info.File, "oldhash").NeedsConfirmation);
        var opened = lib.Load(made.Info.Id, "newhash");                                       // the content hash is no gate: the layers verify by their own hashes
        Assert.Equal(made.Continent.ComputeHash(), opened.Continent.ComputeHash());
        var f = info.File; f.HistoryHash = "0000" + f.HistoryHash[4..];
        File.WriteAllText(Path.Combine(lib.FolderOf(made.Info.Id), WorldLibrary.WorldFileName), System.Text.Json.JsonSerializer.Serialize(f));
        var ex = Assert.Throws<WorldLibraryException>(() => lib.Load(made.Info.Id, "newhash"));
        Assert.Contains("game content changed", ex.Message);
    }
}
