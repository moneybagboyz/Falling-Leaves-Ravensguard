using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Construction;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Building;

/// <summary>P7-LB2 golden tests (large-buildings.md 9): LB-8 to LB-12 and LB-16, with the room types, the plans and the saves of the large buildings.</summary>
public class LargeBuildingTests(ITestOutputHelper output)
{
    private static ContentDatabase Db => EconFixture.Content;
    private static readonly Lazy<BuildingLibrary> LibLazy = new(() => BuildingLibrary.FromDatabase(Db));
    private static BuildingLibrary Lib => LibLazy.Value;
    private static LargeBuildingRulesDef Rules => Db.All<LargeBuildingRulesDef>().First();

    private static RoomWorld PlanWorld(string plan, int ox = 0, int oy = 0, RoomWorld? into = null)
    {
        var w = into ?? Lib.NewWorld();
        w.LoadBlueprint(Lib.Grid(plan), ox, oy, roofed: true);
        return w;
    }

    private static CellEdit S(int x, int y, int z, string? piece) => new(x, y, z, CellLayer.Structure, piece);
    private static CellEdit F(int x, int y, int z, string? piece) => new(x, y, z, CellLayer.Fixture, piece);

    /// <summary>A walled room with an interior iw x ih at (x0, y0) on level z.</summary>
    private static List<CellEdit> Box(int x0, int y0, int z, int iw, int ih, string floor = "floor_plank")
    {
        var e = new List<CellEdit>();
        for (int y = y0 - 1; y <= y0 + ih; y++)
            for (int x = x0 - 1; x <= x0 + iw; x++)
                e.Add(S(x, y, z, x >= x0 && x < x0 + iw && y >= y0 && y < y0 + ih ? floor : "wall_timber"));
        return e;
    }

    // ---------------------------------------------------------------- the content

    [Fact]
    public void Content_ElevenRoomTypes_ThirteenPlans_AndTheKindsAreLoadedLast()
    {
        var large = Db.All<RoomTypeDef>().Where(t => t.Large).Select(t => t.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
        Assert.Equal(["armoury", "barracks", "fulling_mill", "furnace_house", "granary_depot", "hammer_mill", "manufactory_hall", "ropewalk", "saw_mill", "stamp_mill", "warehouse"], large);
        var plans = new[] { "hammer_mill_small", "fulling_mill", "saw_mill_gang", "saw_shed_frame", "furnace_house", "stamp_mill", "ropewalk_120", "warehouse_stone", "granary_depot", "armoury_keep", "barracks_40", "manufactory_hall_smiths", "windmill_post" };
        foreach (var p in plans) Assert.True(Lib.Blueprints.Any(b => b.Def.Id == p), p);
        // every large type sits above its small cousin
        foreach (var t in Db.All<RoomTypeDef>().Where(t => t.Large && t.Purpose.StartsWith("work:", StringComparison.Ordinal) || t.Id is "warehouse" or "granary_depot"))
            foreach (var small in Db.All<RoomTypeDef>().Where(s => !s.Large && s.Purpose == t.Purpose)) Assert.True(t.Priority > small.Priority, $"{t.Id} over {small.Id}");
        // the late kinds come after every other kind (and the stage kinds), so no earlier index moved
        var prod = ProductionContent.Load(Db, new GoodCatalog(Db));
        var late = new[] { "armoury", "fulling_mill", "furnace_house", "granary_depot", "hammer_mill", "ropewalk", "stamp_mill", "warehouse" };
        int first = Array.FindIndex(prod.Kinds, k => k.Id == late[0]);
        Assert.Equal(late, prod.Kinds.Skip(first).Take(late.Length).Select(k => k.Id).ToArray());          // right after the ordinary kinds, before the stage kinds
        Assert.True(prod.Kinds.Take(first).All(k => k.Stage == null) && prod.Kinds.Skip(first + late.Length).All(k => k.Stage != null));
        Assert.Equal(375, prod.Kind("hammer_mill").PoweredLabourPm); Assert.Equal(500, prod.Kind("furnace_house").PoweredLabourPm);
        Assert.Equal(125, prod.Kind("fulling_mill").PoweredLabourPm); Assert.Equal(100, prod.Kind("stamp_mill").PoweredLabourPm);
        Assert.True(prod.Kind("armoury").Salaried && prod.Kind("granary_depot").Salaried && prod.Kind("warehouse").Salaried);
    }

    [Theory]
    [InlineData("hammer_mill_small", "hammer_mill")]
    [InlineData("fulling_mill", "fulling_mill")]
    [InlineData("saw_mill_gang", "saw_mill")]
    [InlineData("saw_shed_frame", "saw_mill")]
    [InlineData("furnace_house", "furnace_house")]
    [InlineData("stamp_mill", "stamp_mill")]
    [InlineData("ropewalk_120", "ropewalk")]
    [InlineData("granary_depot", "granary_depot")]
    [InlineData("armoury_keep", "armoury")]
    [InlineData("manufactory_hall_smiths", "manufactory_hall")]
    [InlineData("windmill_post", "workshop_milling")]
    public void EveryPlan_IsRecognisedAsItsRoomType_ByBothDetectors(string plan, string type)
    {
        var w = PlanWorld(plan);
        Assert.Contains(w.Rooms, r => r.Primary?.Id == type);
        var v1 = Lib.Detector.Detect(Lib.Grid(plan));
        Assert.Contains(v1, r => r.Primary?.Id == type);
    }

    [Fact]
    public void ThePlans_BarracksAndWarehouse_AreRecognised_AndTheSmallRoomsAreNotLargeOnes()
    {
        var w = PlanWorld("barracks_40");
        Assert.Single(w.Rooms, r => r.Primary?.Id == "barracks");
        var wh = PlanWorld("warehouse_stone");
        Assert.Equal(2, wh.Rooms.Count(r => r.Primary?.Id == "warehouse"));
        // a plain storeroom, a guardroom and a smithy stay what they were
        Assert.DoesNotContain(PlanWorld("smithy_house").Rooms, r => r.Primary?.Large == true);
        foreach (var e in Lib.Blueprints.Where(b => !new[] { "hammer_mill_small", "fulling_mill", "saw_mill_gang", "saw_shed_frame", "furnace_house", "stamp_mill", "ropewalk_120", "warehouse_stone", "granary_depot", "armoury_keep", "barracks_40", "manufactory_hall_smiths" }.Contains(b.Def.Id)))
            Assert.DoesNotContain(PlanWorld(e.Def.Id).Rooms, r => r.Primary?.Large == true);
    }

    // ---------------------------------------------------------------- LB-8

    [Fact]
    public void LB8_AManufactoryHall_12x24_With6Anvils_AndLight_Is_AHall_Crowding3_Span4()
    {
        var w = PlanWorld("manufactory_hall_smiths");
        var hall = w.Rooms.Single(r => r.Primary?.Id == "manufactory_hall");
        Assert.Equal(288, hall.Area);
        var fx = LargeRooms.HallOf(hall, Rules);
        Assert.Equal(3, fx.CrowdingM2); Assert.Equal(4, fx.SpanBonus); Assert.Equal(96, fx.MaxWorkers);
        var plain = LargeRooms.HallOf(null, Rules); Assert.Equal(4, plain.CrowdingM2); Assert.Equal(0, plain.SpanBonus);
        Assert.Equal(4, LargeRooms.HallOf(PlanWorld("smithy_house").Rooms.First(), Rules).CrowdingM2);
        // too few stations, or too little light, and it is the smithy it was
        var dark = Lib.NewWorld(); dark.Apply(Box(1, 1, 0, 12, 24, "floor_flagstone"));
        for (int i = 0; i < 6; i++) dark.Apply([F(2 + i, 2, 0, i < 3 ? "forge" : "anvil")]);
        dark.RoofOver("roof_thatch");
        Assert.Equal("workshop_smithing", dark.Rooms.Single().Primary?.Id);                 // 6 stations, a 288 m2 room, but no window
        var few = Lib.NewWorld(); few.Apply(Box(1, 1, 0, 12, 24, "floor_flagstone"));
        for (int i = 0; i < 5; i++) few.Apply([F(2 + i, 2, 0, "anvil")]);
        for (int i = 0; i < 14; i++) few.Apply([F(1 + i % 12, 20 + i / 12, 0, "lamp_stand")]);
        few.RoofOver("roof_thatch");
        Assert.Equal("workshop_smithing", few.Rooms.Single().Primary?.Id);                  // light but only 5 stations
        few.Apply([F(10, 5, 0, "anvil")]);
        Assert.Equal("manufactory_hall", few.Rooms.Single().Primary?.Id);                   // lamps count as light
        // the span: every overseer of a business in a hall oversees 4 more
        var sc = Db.All<ScaleSettingsDef>().First();
        Assert.Equal(Crews.Span(sc, 40) + 4, Crews.Span(sc, 40) + fx.SpanBonus);
    }

    // ---------------------------------------------------------------- LB-9

    private static RoomWorld Walk(int length, int width, bool wheel = true)
    {
        var w = Lib.NewWorld(); w.Apply(Box(1, 1, 0, length, width, "floor_packed_earth"));
        if (wheel) w.Apply([F(1, 1, 0, "rope_wheel"), F(length, 1, 0, "rope_traveller")]);
        w.RoofOver("roof_thatch");
        return w;
    }

    [Fact]
    public void LB9_Ropewalk_70x4_WithWheelAndTraveller_IsRecognised_50x4_And_NoWheel_AreNot()
    {
        Assert.Equal("ropewalk", Walk(70, 4).Rooms.Single().Primary?.Id);
        Assert.NotEqual("ropewalk", Walk(50, 4).Rooms.Single().Primary?.Id);
        Assert.NotEqual("ropewalk", Walk(70, 7).Rooms.Single().Primary?.Id);             // too wide
        Assert.NotEqual("ropewalk", Walk(70, 2).Rooms.Single().Primary?.Id);             // too narrow
        Assert.NotEqual("ropewalk", Walk(70, 4, wheel: false).Rooms.Single().Primary?.Id);
        Assert.Equal("ropewalk", PlanWorld("ropewalk_120").Rooms.Single().Primary?.Id);
    }

    [Fact]
    public void Ropewalk_LayRope_100kgHemp_To_90kgRope_Crew4_12Hours()
    {
        var prod = ProductionContent.Load(Db, new GoodCatalog(Db));
        var r = prod.Recipes.Recipes.First(x => x.Id == "lay_rope");
        Assert.Equal("rope_walk", r.Station); Assert.Equal(4, r.MaxWorkers); Assert.Equal(12, r.WorkerHours);
        Assert.Equal(100_000, r.Inputs[0].Qty); Assert.Equal(90_000, r.Outputs[0].Qty);
        Assert.Contains(prod.Kind("ropewalk").Recipes, x => x.Id == "lay_rope");
    }

    // ---------------------------------------------------------------- LB-10

    private sealed class Kit
    {
        public EconomyState Econ = EconFixture.NewEconomy();
        public IdAllocator Ids = new();
        public WorksBook Book = new();
        public Armouries Arm;
        public Kit() { Arm = new Armouries(Book, Econ.Items, Econ.Goods, Db, Rules); }
        public EntityId Item(string shape, string material, int massG, ItemQuality q = ItemQuality.Common, int cond = 1000) =>
            Econ.Items.Add(Ids, shape, q, EntityId.Make(IdKind.Household, 1), [new ItemComponent(material, massG, (short)cond)], conditionPermille: cond);
    }

    [Fact]
    public void LB10_Armoury_10Stands_12Racks_Holds_10Armours_120Weapons_AndCare_30Hours_Per30Days()
    {
        var w = PlanWorld("armoury_keep");
        var room = w.Rooms.Single(r => r.Primary?.Id == "armoury");
        var cap = LargeRooms.ArmouryOf(room, Rules);
        Assert.Equal(10, cap.Stands); Assert.Equal(12, cap.Racks); Assert.Equal(8, cap.Shelves); Assert.Equal(4, cap.Chests);
        Assert.Equal(10, cap.Armours); Assert.Equal(120, cap.Weapons); Assert.Equal(48, cap.Polearms); Assert.Equal(160, cap.Sheaves);

        var k = new Kit(); long key = LargeRooms.KeyOf(room);
        int armours = 0, swords = 0;
        for (int i = 0; i < 14; i++) if (k.Arm.Add(key, cap, k.Item("mail_hauberk", "steel", 9000))) armours++;
        for (int i = 0; i < 130; i++) if (k.Arm.Add(key, cap, k.Item("arming_sword", "steel", 1200))) swords++;
        Assert.Equal(10, armours); Assert.Equal(120, swords);                                  // the rest do not fit
        Assert.False(k.Arm.Add(key, cap, k.Item("spear", "iron", 900)));                        // racks are full: 120 weapons use all 12 racks
        Assert.False(k.Arm.Add(key, cap, k.Item("shirt", "linen", 350)));                       // clothing is not kit

        Assert.Equal(30.0, k.Arm.CareHours(300)); Assert.Equal(300, k.Arm.CareCapacity(1)); Assert.Equal(600, k.Arm.CareCapacity(2));
        Assert.Equal(4, k.Arm.RustPm(mail: true, cared: false, winterNoHearth: false, day: 7));  // an uncared mail shirt: the house rate
        Assert.Equal(8, k.Arm.RustPm(true, false, true, 7));                                      // doubled in winter with no hearth in the building
        Assert.Equal(1, k.Arm.RustPm(true, true, true, 7));                                       // cared: a quarter, the sealed rate
        Assert.Equal(4, Enumerable.Range(0, 4).Sum(d => Corrosion.OiledShare(4, d)));      // a quarter of 4 a day for 4 days: 4 in all (1 a day), against 16
    }

    [Fact]
    public void LB10_Care_KeepsTheCaredItemsFromRusting_AndTheUncaredRustAtTheHouseRate()
    {
        var k = new Kit(); const long key = 77;
        var cap = new ArmouryCapacity(10, 12, 8, 4, 10, 120, 48, 18, 48, 160);
        var a = k.Item("mail_hauberk", "steel", 9000); var b = k.Item("mail_hauberk", "steel", 9000);
        Assert.True(k.Arm.Add(key, cap, a)); Assert.True(k.Arm.Add(key, cap, b));
        // one armourer's capacity is 300: both cared; with none, neither
        for (int d = 0; d < 40; d++) k.Arm.Day(key, d, armourers: 1, winter: false, hearthInBuilding: true, null);
        int cared = k.Econ.Items.Get(a).ConditionPermille;
        var k2 = new Kit();
        var c = k2.Item("mail_hauberk", "steel", 9000); Assert.True(k2.Arm.Add(key, cap, c));
        for (int d = 0; d < 40; d++) k2.Arm.Day(key, d, armourers: 0, winter: false, hearthInBuilding: true, null);
        int uncared = k2.Econ.Items.Get(c).ConditionPermille;
        output.WriteLine($"40 days: cared {cared}, uncared {uncared}");
        Assert.Equal(1000 - 4 * 40, uncared);                                                  // mail: 4 a day
        Assert.InRange(cared, 1000 - 40, 1000 - 38);                                           // a quarter: 1 a day
        // winter with no hearth: twice the uncared rate
        var k3 = new Kit(); var e = k3.Item("mail_hauberk", "steel", 9000); k3.Arm.Add(key, cap, e);
        for (int d = 0; d < 10; d++) k3.Arm.Day(key, d, 0, winter: true, hearthInBuilding: false, null);
        Assert.Equal(1000 - 8 * 10, k3.Econ.Items.Get(e).ConditionPermille);
        // a day is never done twice
        Assert.Equal(0, k3.Arm.Day(key, 9, 0, true, false, null));
    }

    [Fact]
    public void Armoury_IssueAndReturn_ThroughTheIssueBook_BestFirst_WithGaps()
    {
        var k = new Kit(); const long key = 5;
        var cap = new ArmouryCapacity(10, 12, 8, 4, 10, 120, 48, 18, 48, 160);
        var good = k.Item("arming_sword", "steel", 1200, ItemQuality.Fine, 950); var worn = k.Item("arming_sword", "steel", 1200, ItemQuality.Common, 500); var plain = k.Item("arming_sword", "steel", 1200, ItemQuality.Common, 900);
        foreach (var i in new[] { worn, good, plain }) Assert.True(k.Arm.Add(key, cap, i));
        var holder = EntityId.Make(IdKind.Person, 9);
        var res = k.Arm.Issue(key, [new ArmsRequest("arming_sword", 2), new ArmsRequest("kettle_hat", 3)], holder, 10);
        Assert.Equal([good, plain], res.Items);                                                 // the best first (tier, then condition)
        Assert.Equal(3, res.Gaps["kettle_hat"]);
        Assert.Equal(2, k.Book.Issues.Count); Assert.Equal(holder, k.Econ.Items.Get(good).Owner);
        Assert.Single(k.Arm.Stock(key));
        Assert.Equal(2, k.Arm.View(key).Count(r => r.Issued));
        Assert.Equal(["arming_sword"], k.Arm.Gaps(key, new Dictionary<string, int> { ["arming_sword"] = 3, ["kettle_hat"] = 0 }).Keys);
        Assert.Equal(2, k.Arm.Return(key, [good, plain]));
        Assert.Empty(k.Book.Issues); Assert.Equal(3, k.Arm.Stock(key).Count); Assert.Equal(Armouries.HolderOf(key), k.Econ.Items.Get(good).Owner);
        Assert.Equal(0, k.Arm.Return(key, [good]));                                             // not in the book any more
    }

    // ---------------------------------------------------------------- LB-11

    [Fact]
    public void LB11_GranaryDepot_10x20_Holds150t_Spoils670ppm_AndRotationSellsGrainOlderThan2Seasons()
    {
        var w = PlanWorld("granary_depot");
        var room = w.Rooms.Single(r => r.Primary?.Id == "granary_depot");
        Assert.Equal(200, room.Area);
        Assert.Equal(150_000, LargeRooms.DepotKg(room, Rules));
        var cat = EconFixture.Catalog(); ushort wheat = cat.IdOf("wheat");
        Assert.Equal(670, cat.SpoilPpm(wheat, StoreKind.Granary));
        Assert.InRange(670 * 15, 10_000, 10_100);

        var econ = EconFixture.NewEconomy(); long key = LargeRooms.KeyOf(room);
        var book = new WorksBook(); var dep = new Depots(book, econ.Goods, Rules);
        var store = econ.OpenStore(StoreKind.Granary, Depots.OwnerOf(key), LargeRooms.DepotKg(room, Rules) * 1000, 0, true);
        var farm = econ.OpenStore(StoreKind.Chest, EntityId.Make(IdKind.Household, 3), 0, 0, false);
        var market = econ.OpenStore(StoreKind.Chest, EntityId.Make(IdKind.Household, 4), 0, 0, false);
        farm.Create(wheat, 100_000_000, CreateReason.Harvest);
        Assert.Equal(40_000_000, dep.Intake(key, store, farm, wheat, 40_000_000, season: 10));
        Assert.Equal(30_000_000, dep.Intake(key, store, farm, wheat, 30_000_000, season: 11));
        Assert.Equal(20_000_000, dep.Intake(key, store, farm, wheat, 20_000_000, season: 13));
        Assert.Equal(0, dep.Intake(key, store, farm, econ.Goods.IdOf("rope"), 1000, 13));        // not grain
        dep.SetPolicy(key, targetKg: 90_000, rotation: true);
        // season 13: lots of 10 and 11 are older than 2 seasons (<= 11)
        long Sell(ushort g, long grams) { Assert.Equal(wheat, g); return Store.MoveUpTo(store, market, g, grams); }
        var (sold, reached) = dep.Season(key, store, 13, Sell);
        Assert.Equal(70_000_000, sold); Assert.Equal(70_000_000, market.Qty(wheat));
        Assert.Equal(20_000_000, store.Qty(wheat)); Assert.False(reached);
        Assert.Equal(20_000_000, book.Lots.Sum(l => l.Grams));
        // off: nothing is sold; the target is reached once
        dep.SetPolicy(key, 15_000, rotation: false);
        var (s2, r2) = dep.Season(key, store, 20, Sell); Assert.Equal(0, s2); Assert.True(r2);
        Assert.False(dep.Season(key, store, 21, Sell).Reached);
        // spoilage takes from the store; the lots follow it
        store.ApplySpoilage(1, 100, 10);
        dep.Reconcile(key, store);
        Assert.Equal(store.Qty(wheat), book.Lots.Sum(l => l.Grams));
        // a depot stock is a granary: a 10 t depot refuses more than it holds
        var small = econ.OpenStore(StoreKind.Granary, Depots.OwnerOf(key + 1), 10_000_000, 0, true);
        Assert.Equal(10_000_000, Store.MoveUpTo(farm, small, wheat, 40_000_000));
    }

    // ---------------------------------------------------------------- LB-12

    [Fact]
    public void LB12_Warehouse_12x20_TwoFloors_Stacks720000Litres_AndSpoilsGrainAtTheGranaryRate_PackedAtTheSealed_OthersAtHalfTheHouseRate()
    {
        var w = Lib.NewWorld();
        for (int z = 0; z < 2; z++)
        {
            w.Apply(Box(1, 1, z, 12, 20));
            for (int i = 0; i < 6; i++) w.Apply([F(2 + i, 2, z, "warehouse_bay")]);
        }
        w.RoofOver("roof_thatch");
        var rooms = w.Rooms.Where(r => r.Primary?.Id == "warehouse").ToList();
        Assert.Equal(2, rooms.Count);
        Assert.Equal(720_000, rooms.Sum(LargeRooms.WarehouseLitres));
        Assert.Equal(720, rooms.Sum(r => new Warehouses(new WorksBook(), Rules).CapacityM3(r)));
        // the library's plan: two floors, the floor in m2 times 1,500 each
        var plan = PlanWorld("warehouse_stone").Rooms.Where(r => r.Primary?.Id == "warehouse").ToList();
        Assert.Equal(plan.Sum(r => r.Area) * 1500L, plan.Sum(LargeRooms.WarehouseLitres));

        var cat = EconFixture.Catalog();
        int Ppm(string good, StoreKind k) => cat.SpoilPpm(cat.IdOf(good), k);
        Assert.Equal(Ppm("wheat", StoreKind.Granary), Ppm("wheat", StoreKind.Warehouse));       // grain at the granary rate
        Assert.Equal(Ppm("flour", StoreKind.Sealed), Ppm("flour", StoreKind.Warehouse));        // packed goods at the sealed rate
        Assert.Equal(Ppm("ale", StoreKind.Sealed), Ppm("ale", StoreKind.Warehouse));
        Assert.Equal(Ppm("timber_planks", StoreKind.House) / 2, Ppm("timber_planks", StoreKind.Warehouse));   // the rest at half the house rate
        Assert.Equal(Ppm("firewood", StoreKind.House) / 2, Ppm("firewood", StoreKind.Warehouse));
        Assert.True(Ppm("flour", StoreKind.Warehouse) < Ppm("flour", StoreKind.House));
    }

    [Fact]
    public void Warehouse_LeasesRentBySeason_AtThePrice_AndLapseWhenTheTenantCannotPay()
    {
        var econ = EconFixture.NewEconomy();
        var tenant = econ.Ledger.Open(PurseKind.Person, EntityId.Make(IdKind.Person, 50)); var owner = econ.Ledger.Open(PurseKind.Person, EntityId.Make(IdKind.Person, 51));
        econ.Ledger.Mint(tenant, new Coin(3000), SourceReason.Seed);
        var w = Lib.NewWorld();
        w.Apply(Box(1, 1, 0, 12, 20)); for (int i = 0; i < 6; i++) w.Apply([F(2 + i, 2, 0, "warehouse_bay")]);
        w.RoofOver("roof_thatch");
        var room = w.Rooms.Single(); long key = LargeRooms.KeyOf(room);
        var book = new WorksBook(); var wh = new Warehouses(book, Rules);
        Assert.Equal(360, wh.CapacityM3(room));
        Assert.True(wh.Lease(key, room, tenant, owner, 100, season: 4));
        Assert.False(wh.Lease(key, room, tenant, owner, 100, 4));                              // already rents
        Assert.False(wh.Lease(key, room, econ.Ledger.Open(PurseKind.Person, EntityId.Make(IdKind.Person, 52)), owner, 300, 4));   // not enough left
        Assert.Equal(2000, book.Leases[0].RentMites);                                          // 100 m3 at 20 mites
        Assert.Equal((0, 0), wh.Season(4, econ.Ledger));                                       // the season it was signed
        Assert.Equal((1, 0), wh.Season(5, econ.Ledger));
        Assert.Equal(1000, econ.Ledger.Balance(tenant).Mites); Assert.Equal(2000, econ.Ledger.Balance(owner).Mites); Assert.Equal(1, book.Leases[0].PaidSeasons);
        Assert.Equal((0, 1), wh.Season(6, econ.Ledger));                                       // 1,000 left cannot pay 2,000: the lease lapses
        Assert.Empty(book.Leases); Assert.Equal(1000, econ.Ledger.Balance(tenant).Mites);
    }

    // ---------------------------------------------------------------- barracks

    [Fact]
    public void Barracks_40Men_20Bunks_3m2AMan_BurnStraw1kgABedEvery15Days()
    {
        var w = PlanWorld("barracks_40");
        var room = w.Rooms.Single(r => r.Primary?.Id == "barracks");
        Assert.Equal(40, LargeRooms.BarracksBeds(room));
        Assert.True(Barracks.Valid(room, hearthInBuilding: true)); Assert.False(Barracks.Valid(room, false));
        Assert.Equal(40, LargeRooms.StrawKgPerPeriod(room, Rules));
        Assert.Equal(40_000, Barracks.StrawGramsToday(room, Rules, 30)); Assert.Equal(0, Barracks.StrawGramsToday(room, Rules, 31));
        var econ = EconFixture.NewEconomy(); ushort straw = econ.Goods.IdOf("straw");
        var s = econ.OpenStore(StoreKind.Chest, EntityId.Make(IdKind.Household, 8), 0, 0, false);
        s.Create(straw, 30_000, CreateReason.Harvest);
        Assert.Equal(10_000, Barracks.Upkeep(room, Rules, 30, s, straw));                       // 10 kg short
        Assert.Equal(0, s.Qty(straw));
    }

    // ---------------------------------------------------------------- the hall and the machines in a business

    [Fact]
    public void AWorks_InAHall_GetsTheHallsSpan_AndAHammerMillItsMachinesLabour()
    {
        var rig = ConstructionRig.Make();
        var rooms = rig.W.Rooms;
        PlanWorld("hammer_mill_small", 5100, 5000, rooms);
        PlanWorld("manufactory_hall_smiths", 5200, 5000, rooms);
        var ww = new WorksWorld(new WorksBook(), Db, rig.Econ, () => rooms);
        var biz = new Business { Id = EntityId.Make(IdKind.Other, 4242) };
        biz.Stations.Add(new Station { Kind = "anvil" });
        Assert.Equal(0, ww.SpanBonus(biz)); Assert.Null(ww.StationLabourPm(biz, 0));            // no premises
        Assert.True(ww.LinkPremises(biz.Id.Value, 5207, 5010, 0)); Assert.False(ww.LinkPremises(1, 0, 0, 0));
        Assert.Equal(4, ww.SpanBonus(biz));
        Assert.True(ww.LinkPremises(biz.Id.Value, 5106, 5004, 0));                              // now the hammer mill
        Assert.Equal(0, ww.SpanBonus(biz));
        Assert.Equal(["hammer_mill"], ww.RoomsOf("hammer_mill").Select(x => x.Room.Primary!.Id));
        Assert.Null(ww.StationLabourPm(biz, 0));                                                // built plans are not built pieces: no machine runs until the pieces stand
    }

    // ---------------------------------------------------------------- LB-16

    private static (ulong Hash, byte[] Bytes, long Mass, string Dump) Year()
    {
        var rig = ConstructionRig.Make(); var econ = rig.Econ; var rooms = rig.W.Rooms;
        PlanWorld("hammer_mill_small", 5100, 5000, rooms); PlanWorld("granary_depot", 5200, 5000, rooms); PlanWorld("armoury_keep", 5300, 5000, rooms);
        var book = new WorksBook();
        var ww = new WorksWorld(book, Db, econ, () => rooms);
        var armoury = ww.RoomsOf("armoury").Single(); var depot = ww.RoomsOf("granary_depot").Single();
        Assert.Single(ww.RoomsOf("hammer_mill"));
        var ids = new IdAllocator(); var cap = LargeRooms.ArmouryOf(armoury.Room, ww.Rules);
        var owner = EntityId.Make(IdKind.Household, 1);
        var items = new List<EntityId>();
        for (int i = 0; i < 6; i++) items.Add(econ.Items.Add(ids, "mail_hauberk", ItemQuality.Common, owner, [new ItemComponent("steel", 9000, 1000)]));
        for (int i = 0; i < 30; i++) items.Add(econ.Items.Add(ids, "arming_sword", ItemQuality.Common, owner, [new ItemComponent("steel", 1200, 1000)]));
        foreach (var it in items) Assert.True(ww.Armouries.Add(armoury.Key, cap, it));
        var store = ww.DepotStore(depot.Key)!; var farm = econ.OpenStore(StoreKind.Chest, EntityId.Make(IdKind.Household, 3), 0, 0, false);
        var market = econ.OpenStore(StoreKind.Chest, EntityId.Make(IdKind.Household, 4), 0, 0, false);
        ushort wheat = econ.Goods.IdOf("wheat"); farm.Create(wheat, 90_000_000, CreateReason.Harvest);
        ww.Depots.SetPolicy(depot.Key, 8_000, true);
        ww.SellGrain = (k, g, grams) => Store.MoveUpTo(ww.DepotStore(k)!, market, g, grams);
        var holder = EntityId.Make(IdKind.Person, 77); List<EntityId> out1 = [];
        var lines = new List<string>(); ww.Chronicle = (when, type, key, ps, subj, causes, data) => { lines.Add(type + "@" + when.Seconds); return EntityId.None; };
        for (long day = 1; day <= 180; day++)
        {
            if (day % 15 == 1 && day < 100) ww.Depots.Intake(depot.Key, store, farm, wheat, 10_000_000, (int)(day / 15));
            if (day == 20) out1 = [.. ww.Armouries.Issue(armoury.Key, [new ArmsRequest("arming_sword", 10), new ArmsRequest("mail_hauberk", 2)], holder, (int)day).Items];
            if (day == 120) ww.Armouries.Return(armoury.Key, out1);
            ww.Day(day);
            store.ApplySpoilage(1, day, 10);
        }
        var bytes = SaveCodec.SerializeCompressed(WorksSave.Capture(book));
        long mass = econ.Mass.Net(wheat) - (store.Qty(wheat) + farm.Qty(wheat) + market.Qty(wheat));
        return (ww.Hash(), bytes, mass, string.Join("|", lines));
    }

    [Fact]
    public void LB16_AYearWithAHammerMill_ADepot_AndAnArmouryIssuingKit_IsDeterministic_SavesAndRestoresIdentically_AndMassIsExact()
    {
        var a = Year(); var b = Year();
        Assert.NotEqual(0UL, a.Hash);
        Assert.Equal(a.Hash, b.Hash); Assert.Equal(a.Bytes, b.Bytes); Assert.Equal(a.Dump, b.Dump);
        Assert.Equal(0, a.Mass);                                                               // created minus destroyed is exactly what the stores hold
        Assert.Contains("depot_filled", a.Dump);
        // save, load, save: identical bytes, equal hash
        var restored = SaveCodec.DeserializeCompressed<WorksSave>(a.Bytes).Restore();
        Assert.Equal(a.Hash, restored.Hash());
        Assert.Equal(a.Bytes, SaveCodec.SerializeCompressed(WorksSave.Capture(restored)));
        output.WriteLine($"works hash {a.Hash}, {a.Bytes.Length} bytes, {a.Dump}");
        Assert.True(a.Bytes.Length < 30_000);                                                  // <= 30 KB a region
    }

    [Fact]
    public void RealmSeats_GetAKeepAndADepot_ByTheSeedAndTheSettlementsId_NotTheDay()
    {
        var region = EconFixture.Pop.Region;
        var plan = SeatBuildings.Plan(region, EconFixture.Seed);
        Assert.NotEmpty(plan);
        Assert.Equal(plan, SeatBuildings.Plan(region, EconFixture.Seed));
        Assert.All(plan.GroupBy(p => p.Settlement), g => Assert.Equal([SeatBuildings.Keep, SeatBuildings.Depot], g.Select(p => p.Plan).ToArray()));
        Assert.All(plan, p => Assert.InRange(p.Turns, 0, 3));
        Assert.Equal(plan.Select(p => p.Settlement).OrderBy(s => s), plan.Select(p => p.Settlement));
        var seats = SeatBuildings.Seats(region);
        Assert.All(seats, kv => Assert.Contains(plan, p => p.Settlement == kv.Value));
    }

    [Fact]
    public void TheSection_IsEmptyAndAbsentUntilSomethingIsSet_AndAnLB1SectionReadsWithNothingElse()
    {
        Assert.True(new WorksBook().IsEmpty); Assert.Equal(0UL, new WorksBook().Hash());
        var b = new WorksBook(); b.SluiceShut.Add(5);
        var s = WorksSave.Capture(b);
        Assert.Empty(s.Leases); Assert.Empty(s.Depots);
        var back = s.Restore(); Assert.Contains(5L, back.SluiceShut); Assert.False(back.HasBuildings);
        // an LB1 section (one key) is a prefix of the new one
        var lb1 = MessagePack.MessagePackSerializer.Serialize(new object[] { new long[] { 5 } });
        var read = MessagePack.MessagePackSerializer.Deserialize<WorksSave>(lb1);
        Assert.Equal([5L], read.SluiceShut); Assert.Empty(read.Armouries);
    }
}
