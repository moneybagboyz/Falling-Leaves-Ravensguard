using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen;

namespace Ravensguard.Sim.Tests.Save;

/// <summary>
/// The goods-table fix (phase-5-open, "From step 2"): saved content indices are mapped onto the loaded content by name. Content A is the base game plus two goods a later version
/// drops (<c>aa_old_scrap</c>, which sorts first so every index moves, with a migration to <c>scrap_iron</c>; <c>zz_relic</c>, with none); content B is the base game plus
/// <c>hemp</c>, a good inserted in the middle of the table, and the migration.
/// </summary>
public class ContentRemapTests
{
    private const string Old = "aa_old_scrap", Gone = "zz_relic", New = "hemp";

    private static (ContentDatabase Db, string Hash) LoadWith(string name, string body)
    {
        using var mod = new TempDir(); using var cache = new TempDir();
        File.WriteAllText(Path.Combine(mod.Path, "mod.json"), $$"""{ "id": "{{name}}", "name": "{{name}}", "version": "1.0.0", "load_after": ["base"], "dependencies": ["base"] }""");
        File.WriteAllText(Path.Combine(mod.Path, "goods.jsonc"), body);
        var load = new ModLoader(ContentTypeRegistry.Default).Load([ContinentSettings.FindBaseContent()!, mod.Path], cache.Path);
        Assert.True(load.Success, load.Report.ToString());
        return (load.Database, load.Hash!);
    }

    private static readonly Lazy<(ContentDatabase Db, string Hash)> A = new(() => LoadWith("remap_a", $$"""
        [
          { "type": "good", "id": "{{Old}}", "name": "Old scrap", "class": "hardware", "base_price_mites_per_kg": 5, "spoil_class": "iron" },
          { "type": "good", "id": "{{Gone}}", "name": "A relic", "class": "hardware", "base_price_mites_per_kg": 50, "spoil_class": "inert" }
        ]
        """));
    private static readonly Lazy<(ContentDatabase Db, string Hash)> B = new(() => LoadWith("remap_b", $$"""
        [
          { "type": "good", "id": "{{New}}", "name": "Hemp", "class": "fibre", "base_price_mites_per_kg": 3, "spoil_class": "inert" },
          { "type": "migration", "target_type": "good", "from": "{{Old}}", "to": "scrap_iron" }
        ]
        """));

    private sealed record Made(WorldState World, int Hh, EntityId Chest, EntityId Barrel, EntityId Tool);

    /// <summary>A world on content A with the goods in stores, a market lot and order, an item's contents, the party's records and the production counters.</summary>
    private static Made WorldOnA()
    {
        var db = A.Value.Db; var pop = EconFixture.Pop;
        var e = EconomyState.Create(pop, db, EconFixture.Seed);
        var g = e.Goods; ushort old = g.IdOf(Old), gone = g.IdOf(Gone), scrap = g.IdOf("scrap_iron"), wheat = g.IdOf("wheat");
        Assert.Equal(0, old);                                                       // the first row: every other good's index moves when it goes
        var chestId = EntityId.Make(IdKind.Other, 7001);
        var chest = e.OpenStore(StoreKind.Chest, chestId, 0, 0, enforce: false);
        chest.Create(old, 5000, CreateReason.Seed); chest.Create(gone, 3000, CreateReason.Seed); chest.Create(scrap, 2000, CreateReason.Seed); chest.Create(wheat, 1000, CreateReason.Harvest);
        var ids = new IdAllocator();
        var barrel = e.Items.Add(ids, "barrel", ItemQuality.Common, chestId, [new("oak", 18_000, 1000)]);
        e.Items.At(e.Items.IndexOf(barrel)).ContentsGood = gone; e.Items.At(e.Items.IndexOf(barrel)).ContentsQty = 100;
        var tool = e.Items.Add(ids, "barrel", ItemQuality.Common, chestId, [new("oak", 1_000, 1000)], good: old);

        var markets = new MarketSystem(e, pop, EconFixture.Seed);
        var book = markets.AllBooks().First();
        int hh = Enumerable.Range(0, pop.Households.Count).First(h => markets.BookForHousehold(h) == book);
        var party = markets.HouseholdParty(hh);
        party.Store.Create(old, 1500, CreateReason.Seed); party.Store.Create(gone, 700, CreateReason.Seed);
        Assert.Equal(1000, book.List(party, old, 1000, 500, 0));
        Assert.Equal(200, book.List(party, gone, 200, 500, 0));
        book.Post(new BuyOrder(party, gone, 50, 1500)); book.Post(new BuyOrder(party, wheat, 60, 1500));
        var prod = new ProductionSystem(e, pop, ProductionContent.Load(db, g), EconFixture.Seed, markets, null);
        prod.ProducedUnits[old] = 77; prod.ProducedUnits[scrap] = 5; prod.ConsumedUnits[gone] = 9;

        var hero = new HeroParty();
        hero.Carried.GoodIn[old] = chestId; hero.Carried.GoodIn[gone] = chestId;
        hero.Notebook.Load([new PriceNote(book.SettlementIndex, old, 10, 9, 1000, 1), new PriceNote(book.SettlementIndex, scrap, 20, 19, 2000, 2), new PriceNote(book.SettlementIndex, gone, 30, 29, 200, 3)]);
        hero.Traded.Add(old); hero.Traded.Add(gone); hero.Traded.Add(wheat);
        var w = EconFixture.WorldWith(e); w.Markets = markets; w.Production = prod; w.Party = hero;
        Assert.True(e.MassAuditHolds()); Assert.True(e.Ledger.Audit().IsConserved);
        return new Made(w, hh, chestId, barrel, tool);
    }

    private static byte[] Bytes(WorldState w, string hash) => SaveCodec.Encode(WorldSave.Capture(w, hash, "0.1.0"));

    [Fact]
    public void A_SaveOfOneGoodsTable_LoadsOnAnother_ByName_WithAuditsExact_AndSavesStably()
    {
        var made = WorldOnA(); var a = made.World.Economy!;
        // the save names its goods; the load is told the save's own content hash (the whole-content gate of the header is a separate policy, see crafting-full Notes)
        var bytes = Bytes(made.World, A.Value.Hash);
        var file = SaveCodec.Decode(bytes);
        Assert.Contains(IdTablesSave.SectionName, file.Sections.Keys);
        var st = WorldSave.Restore(file, B.Value.Db, A.Value.Hash);
        var b = st.Economy!; var g = b.Goods;
        ushort scrap = g.IdOf("scrap_iron"), hemp = g.IdOf(New), wheat = g.IdOf("wheat");
        Assert.False(g.TryIdOf(Old, out _)); Assert.False(g.TryIdOf(Gone, out _));
        Assert.True(hemp > 0 && hemp < g.Count - 1);                                           // inserted in the middle: the indices after it shift

        // every store, row by row, by name: the migrated good adds to its target, the dropped one is written off, the new one is empty
        var stores = a.HouseholdStore.Take(a.HouseholdCount).Select((s, i) => (Key: $"h{i}", S: s)).Concat(a.OtherStores.Select(kv => (Key: kv.Key.ToString(), S: kv.Value))).ToList();
        var after = b.HouseholdStore.Take(b.HouseholdCount).Select((s, i) => (Key: $"h{i}", S: s)).Concat(b.OtherStores.Select(kv => (Key: kv.Key.ToString(), S: kv.Value))).ToDictionary(x => x.Key, x => x.S);
        Assert.Equal(stores.Count, after.Count);
        foreach (var (key, s) in stores)
        {
            var want = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var (gg, q) in s.Rows())
            {
                var id = a.Goods.StringId(gg) switch { Old => "scrap_iron", var x => x };
                if (id != Gone) want[id] = want.GetValueOrDefault(id) + q;
            }
            var got = after[key].Rows().ToDictionary(r => g.StringId(r.Good), r => r.Qty);
            Assert.Equal(want, new SortedDictionary<string, long>(got, StringComparer.Ordinal));
            Assert.Equal(0, after[key].Qty(hemp));
        }
        Assert.Equal(7000, b.OtherStores[made.Chest].Qty(scrap)); Assert.Equal(1000, b.OtherStores[made.Chest].Qty(wheat));
        // the mass ledger: the target's columns gained the migrated good's, the dropped good's left with its stock; the audits hold exactly
        ushort aOld = a.Goods.IdOf(Old), aScrap = a.Goods.IdOf("scrap_iron");
        Assert.Equal(a.Mass.TotalCreated(aScrap) + a.Mass.TotalCreated(aOld), b.Mass.TotalCreated(scrap));
        Assert.Equal(a.Mass.TotalDestroyed(aScrap) + a.Mass.TotalDestroyed(aOld), b.Mass.TotalDestroyed(scrap));
        Assert.Equal(0, b.Mass.TotalCreated(hemp));
        Assert.True(b.MassAuditHolds()); Assert.True(b.Ledger.Audit().IsConserved);
        Assert.Equal(a.Ledger.Audit(), b.Ledger.Audit());
        // items: the good an item came from follows the migration; the contents of a dropped good are written off
        Assert.Equal(scrap, b.Items.At(b.Items.IndexOf(made.Tool)).Good);
        Assert.Equal((ItemTable.NoGood, 0L), (b.Items.At(b.Items.IndexOf(made.Barrel)).ContentsGood, b.Items.At(b.Items.IndexOf(made.Barrel)).ContentsQty));

        // markets: the lot of the migrated good is a scrap iron lot, the dropped good's lot and order are gone, the new good's prices are a fresh book's
        var bookA = made.World.Markets!.AllBooks().First(); var bookB = st.Markets!.AllBooks().First();
        Assert.Equal(1000, bookB.Goods[scrap].StockUnits); Assert.Single(bookB.Goods[scrap].Lots); Assert.Equal(scrap, bookB.Goods[scrap].Lots[0].Good);
        Assert.Equal(1000, bookB.BookStore.Qty(scrap));
        Assert.Equal(bookA.Goods[aScrap].RefMicro, bookB.Goods[scrap].RefMicro);                // a good keeps its own prices
        Assert.Equal(bookB.Goods[hemp].BaseRefMicro, bookB.Goods[hemp].RefMicro); Assert.True(bookB.Goods[hemp].RefMicro > 0);
        Assert.Equal(bookA.Goods[a.Goods.IdOf("ale")].RefMicro, bookB.Goods[g.IdOf("ale")].RefMicro);
        Assert.Equal([wheat], bookB.Orders.Select(o => o.Good).ToArray());
        // production counters by good
        Assert.Equal(82, st.Production!.ProducedUnits[scrap]); Assert.Equal(0, st.Production.ConsumedUnits[hemp]); Assert.Equal(g.Count, st.Production.ProducedUnits.Length);
        // the party: carried-in, the notebook (scrap iron's own note wins over the migrated one) and the traded set
        Assert.Equal([scrap], st.Party!.Carried.GoodIn.Keys.ToArray());
        Assert.Equal([(scrap, 20L)], st.Party.Notebook.Notes.Select(n => (n.Good, n.AskMicro)).ToArray());
        Assert.Equal([scrap, wheat], st.Party.Traded.ToArray());
        // one warning for what was written off, one line for the table
        Assert.Contains(st.LoadNotes, n => n.StartsWith("Warning: goods no longer in the game were written off") && n.Contains($"{Gone} 3,700") && !n.Contains(Old));
        Assert.Contains(st.LoadNotes, n => n.Contains($"{Old} -> scrap_iron") && n.Contains("1 new"));

        // save -> load -> save on content B: the same bytes, nothing remapped the second time
        var b1 = Bytes(st, A.Value.Hash);
        var back = WorldSave.Restore(SaveCodec.Decode(b1), B.Value.Db, A.Value.Hash);
        Assert.Empty(back.LoadNotes);
        Assert.Equal(b1, Bytes(back, A.Value.Hash));
        Assert.Equal(b.ComputeHash(), back.Economy!.ComputeHash());
    }

    [Fact]
    public void B_ARegionSection_IsRemappedToo_AndAPagedOneKeepsItsMappedBytes()
    {
        var made = WorldOnA();
        var file = SaveCodec.Decode(Bytes(made.World, A.Value.Hash));
        // a second region's record as it would be paged: the home economy, production and markets again under a region name
        var econ = SaveCodec.DeserializeCompressed<EconomySave>(file.Sections[WorldSave.EconomySection]);
        var region = new RegionSectionData { RegionId = 5, Economy = econ, Production = SaveCodec.DeserializeCompressed<ProductionSave>(file.Sections[ProductionSave.SectionName]),
            Markets = SaveCodec.DeserializeCompressed<MarketsSave>(file.Sections[MarketsSave.SectionName]) };
        var sections = file.Sections.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()); sections[WorldSave.RegionSection(5)] = SaveCodec.SerializeCompressed(region);
        var st = WorldSave.Restore(new SaveFile(file.Header, sections), B.Value.Db, A.Value.Hash);
        var r = SaveCodec.DeserializeCompressed<RegionSectionData>(st.RegionSections[5]);
        var g = st.Economy!.Goods;
        Assert.Equal(g.Count, r.Economy.GoodCount); Assert.Equal(g.Hash(), r.Economy.GoodsHash); Assert.Equal(g.Hash(), r.Markets.GoodsHash); Assert.Equal(g.Hash(), r.Production.GoodsHash);
        Assert.Equal(st.Economy.Mass.CreatedState(), r.Economy.MassCreated);
        Assert.Equal(SaveCodec.DeserializeCompressed<EconomySave>(SaveCodec.SerializeCompressed(EconomySave.Capture(st.Economy))).RowGood, r.Economy.RowGood);
    }

    [Fact]
    public void C_APreFixSave_IsReadWithTheFrozenTables_RecipesByName()
    {
        // a world that knows two recipes, saved as a save from before the fix: no "tables" section, its recipe indices in the frozen v8 numbering
        var db = EconFixture.Content; var rdb = RecipeDb.Build(db, EconFixture.Catalog());
        var v8 = LegacyIdTables.V8.Get(IdTables.CraftRecipes)!;
        var w = EconFixture.WorldWith(null); var cs = new CraftState(); w.Crafting = cs;
        var hero = EconomyState.HeroOwner;
        foreach (var id in new[] { "weave_cloth", "bake_bread", "salt_meat_tub" }) cs.Know.Learn(hero, (ushort)Array.IndexOf(v8, id));
        var file = WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0");
        var sections = file.Sections.Where(kv => kv.Key != IdTablesSave.SectionName).ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        var st = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(new SaveFile(file.Header, sections))), db, EconFixture.ContentHash);
        var known = st.Crafting!.Know.LearntBy(hero).Select(i => rdb.Recipes[i].Id).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["bake_bread", "salt_meat_tub", "weave_cloth"], known);
        Assert.NotEqual(Array.IndexOf(v8, "weave_cloth"), rdb["weave_cloth"].Index);                 // the restored recipes moved it
        Assert.Contains(st.LoadNotes, n => n.Contains("'craft_recipe'"));
    }

    [Fact]
    public void D_TheFrozenTables_AreTheContentBeforeTheRestoredGoodsAndRecipes()
    {
        var cur = IdTables.Of(EconFixture.Content).Tables;
        var restoredGoods = new[] { "arrowheads", "arrows", "daub", "harness", "iron_wire", "mail_rings", "shingles", "auger", "pan", "pick", "spade" };      // P5-F: the prospector's tools
        var p6Goods = new[] { "crutch", "litter", "poppy_milk", "claim_banner", "flint_nodule", "obsidian_nodule", "anvil_block" };                                  // P6-A1: medicine/aids.jsonc and relief.jsonc; P6-B6: the banner, the knapping stock and the forged anvil block
        var p7Goods = new[] { "iron_ore_rough", "tempered_clay", "bricks_green", "dry_cask", "spearheads", "kettle_hats", "hemp_fibre", "gold", "gold_ore" };                                                                                  // P7-IN1: goods of the staged industries
        var p7Stages = new[] { "stage_iron_mine", "stage_iron_crusher", "stage_iron_yard", "stage_iron_smeltery", "stage_iron_packing", "stage_clay_pit", "stage_clay_yard", "stage_clay_moulding", "stage_clay_drying", "stage_clay_kiln", "stage_clay_packing", "stage_wood_felling", "stage_wood_hauling", "stage_wood_yard", "stage_wood_sawmill", "stage_wood_packing", "stage_wood_shipping" };   // P7-IN1: the stage kinds
        var newKinds = new[] { "bloomery", "brickworks", "clay_pit", "peat_cuttings", "quarry", "salt_pan", "sawpit", "water_sawmill", "physician", "stud", "saw_mill", "munition_works", "money_changer" };              // P5-F: kinds the player founds; P6-A1: the physician's business
        var restoredRecipes = new[] { "lay_rope", "draw_wire", "fletch_arrows", "forge_arrowheads", "make_harness", "mix_daub", "rive_shingles", "make_crutch", "make_claim_banner", "knap_flint_blade", "knap_obsidian_blade", "forge_anvil_block" };
        var v8 = LegacyIdTables.V8;
        Assert.Equal(cur.Get(IdTables.Goods)!.Except(restoredGoods).Except(p6Goods).Except(p7Goods), v8.Get(IdTables.Goods)!);
        // P5-G added the vehicle parts' recipes and repair lines (content/base/crafting/recipes_vehicles.jsonc): new ids, so a v8 save's crafting section maps by name
        var p5g = new[] { "make_yoke", "make_pole", "make_traces", "make_cargo_box", "make_barrow_tray", "make_cargo_rack", "make_seat_bench", "make_tilt_hoops", "sew_tilt_cloth", "make_runner", "make_handle_bars", "make_pivot_axle", "make_iron_bound_axle", "make_dished_wheel", "make_brake_shoe", "forge_drag_shoe", "saw_hull_plank", "shape_keel", "make_gunwale", "saw_deck_plank", "make_ferry_ramp", "make_thwart", "make_oars", "make_quant_pole", "make_steering_oar", "make_rudder", "make_tow_post", "make_mast", "sew_square_sail", "make_fish_well", "make_cabin_box", "make_stall_counter", "build_mobile_hearth", "repair_hull_plank", "repair_vehicle_frame" };
        var p7sgb = new[] { "make_siege_beam", "make_siege_axle", "make_siege_wheel", "make_ram_beam", "make_ram_head_iron", "make_penthouse_roof", "make_hide_cover", "make_mantlet_board", "make_tower_floor", "make_tower_ladder", "make_tower_drawbridge", "make_trebuchet_frame", "make_trebuchet_arm", "make_counterweight_box", "make_trebuchet_sling", "make_springald" };          // P7-SG-B: the siege parts' recipes
        var p7sh = new[] { "saw_hull_plank_sea", "saw_hull_plank_long", "make_castle_fore", "make_castle_aft", "make_fighting_top", "make_stem_post", "make_boarding_plank", "make_ship_ladder", "sew_sea_sail" };          // P7-SH-A: the sea parts' recipes
        Assert.Equal(cur.Get(IdTables.CraftRecipes)!.Except(restoredRecipes).Except(p5g).Except(p7sh).Except(p7sgb).Except(["make_dry_cask", "forge_spearheads", "forge_kettle_hats", "refine_gold"]).Except(["make_armour_board", "make_pavise_side", "make_arrow_slit", "forge_chain_hook", "make_chariot_car"]), v8.Get(IdTables.CraftRecipes)!);          // P7-SG-E: the war wagon and chariot parts' recipes          // P7-IN2: the cooperage recipe; P7-ES2: the munition runs
        Assert.Equal(cur.Get(IdTables.Proficiencies)!, v8.Get(IdTables.Proficiencies)!);
        Assert.Equal(cur.Get(IdTables.Extractions)!.Except(["mining_gold"]), v8.Get(IdTables.Extractions)!);                                                                                   // P7-CN1: gold mining
        Assert.Equal(cur.Get(IdTables.BookRecipes)!.Except(["make_dry_cask", "forge_spearheads", "forge_kettle_hats", "lay_rope", "refine_gold"]), v8.Get(IdTables.BookRecipes)!);                            // P7-IN2: the cooperage recipe is in the recipe book
        var p7Late = new[] { "armoury", "fulling_mill", "furnace_house", "granary_depot", "hammer_mill", "ropewalk", "stamp_mill", "warehouse" };                 // P7-LB2: the large buildings' kinds, loaded after the ordinary kinds
        Assert.Equal(cur.Get(IdTables.Businesses)!.Except(newKinds).Except(p7Stages).Except(p7Late), v8.Get(IdTables.Businesses)!);
        // the v7 tables differ only by Phase 3's recipes and proficiencies, every one of which is still in the content
        Assert.Subset(new HashSet<string>(cur.Get(IdTables.CraftRecipes)!), new HashSet<string>(LegacyIdTables.V7.Get(IdTables.CraftRecipes)!));
        Assert.Subset(new HashSet<string>(cur.Get(IdTables.Proficiencies)!), new HashSet<string>(LegacyIdTables.V7.Get(IdTables.Proficiencies)!));
    }

    [Fact]
    public void E_ARemap_FollowsMigrations_DropsTheRest_AndAnUnfinishedCraftOfARemovedRecipe_IsRefused()
    {
        var m = TableRemap.Build(IdTables.Goods, [Old, "ale", Gone], ["ale", New, "scrap_iron"], B.Value.Db);
        Assert.Equal([2, 0, -1], m.Map); Assert.Equal([Gone], m.Dropped); Assert.Equal([$"{Old} -> scrap_iron"], m.Migrated); Assert.Equal(2, m.Added); Assert.False(m.Identity);
        Assert.True(m.Same(1)); Assert.False(m.Same(0));

        var w = EconFixture.WorldWith(null); var cs = new CraftState(); w.Crafting = cs;
        cs.Add(new CraftJob { Id = EntityId.Make(IdKind.Other, 9), Recipe = 3, Crafter = EconomyState.HeroOwner, Owner = EconomyState.HeroOwner, Choice = [] });
        var file = WorldSave.Capture(w, EconFixture.ContentHash, "0.1.0");
        var tables = SaveCodec.Deserialize<IdTablesSave>(file.Sections[IdTablesSave.SectionName].ToArray());
        var t = IdTables.FromSave(tables); t.Tables[IdTables.CraftRecipes] = ["a_recipe_long_gone", "b", "c", "d_recipe_long_gone"];
        var sections = file.Sections.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()); sections[IdTablesSave.SectionName] = SaveCodec.Serialize(t.ToSave());
        var ex = Assert.Throws<SaveException>(() => WorldSave.Restore(new SaveFile(file.Header, sections), EconFixture.Content, EconFixture.ContentHash));
        Assert.Contains("d_recipe_long_gone", ex.Message);
    }
}
