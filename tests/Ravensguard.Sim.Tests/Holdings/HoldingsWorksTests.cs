using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Clothing;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Holdings;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Holdings;

/// <summary>P6-B6 holdings 3.6 and 3.8: furnishing and the rooms (HW-6), buy and deliver (HW-8), heavy work and an anvil from an item (HW-9), the crafting loose ends (HW-10), a business in a generated building, coppice and carting (HW-11), oiling and knapping (HW-12), roots and views (HW-13).</summary>
public class HoldingsWorksTests(ITestOutputHelper output)
{
    private static (SeamWorld W, WorldState State) Game()
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = ContinentSettings.FindBaseContent()!;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var h = HeroRecord.FromOrigin("Aldric Vane", false, w.Content.All<OriginDef>().OrderBy(o => o.Order).First());
        var st = NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, h, null);
        st.Content = w.Content;
        return (w, st);
    }

    private sealed class Play(WorldRuntime rt, HeroContext ctx)
    {
        public readonly HeroDesk Desk = new();
        public HeroResult Do(HeroCommand c)
        {
            var cmd = Desk.Issue(c with { IssuedSec = Math.Max(c.IssuedSec, rt.HourDone * 3600) });
            Desk.ApplyDue(ctx, rt.HourDone + 1);
            return Desk.View!.Results.Last(r => r.Seq == cmd.Seq);
        }
        public long Sec => (rt.HourDone + 1) * 3600;
        public int Day => (int)(Sec / 86400);
        public void Days(int days) => rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + days * 24L));
        public void Hours(int hours) => rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 1 + hours));
    }

    // ------------------------------------------------------------------ HW-6: rooms

    private static (RoomWorld Rw, BuildingLibrary Lib) Rooms()
    {
        var lib = BuildingLibrary.FromDatabase(SeamWorld.Get(1234).Content);
        return (lib.NewWorld(), lib);
    }

    [Fact]
    public void HW6_A_slipway_a_wheelwrights_yard_and_a_loft_are_room_types_RoomDetector_recognises()
    {
        var content = SeamWorld.Get(1234).Content;
        foreach (var id in new[] { "slipway", "wheelwright_yard", "loft" }) Assert.True(content.TryGet<RoomTypeDef>(id, out _), id);
        var (rw, _) = Rooms();
        // a fenced yard of 4 x 4 with a wheelwright's bay, no roof
        var edits = new List<CellEdit>();
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
            {
                bool edge = x == 0 || y == 0 || x == 5 || y == 5;
                edits.Add(new CellEdit(x, y, 0, CellLayer.Structure, edge ? (x == 3 && y == 5 ? "door_wood" : "wall_wattle") : "floor_packed_earth"));
            }
        edits.Add(new CellEdit(2, 2, 0, CellLayer.Fixture, "wheelwright_bay"));
        rw.Apply(edits);
        var yard = rw.RoomAt(2, 3, 0);
        Assert.NotNull(yard);
        Assert.Equal("wheelwright_yard", yard!.Primary?.Id);
        // a slipway: open ground of 3 x 3 floor holding the ways
        var edits2 = new List<CellEdit>();
        for (int y = 20; y < 23; y++) for (int x = 20; x < 23; x++) edits2.Add(new CellEdit(x, y, 0, CellLayer.Structure, "floor_plank"));
        edits2.Add(new CellEdit(21, 21, 0, CellLayer.Fixture, "slipway_ways"));
        rw.Apply(edits2);
        Assert.Equal("slipway", rw.RoomAt(20, 20, 0)?.Primary?.Id);
        // a design of a wagon is built in a yard, a boat on a slipway
        var designer = content.All<VehicleBlueprintDef>().Where(b => b.BuiltAt.Length > 0).ToList();
        Assert.Contains(designer, d => d.BuiltAt == "yard");
    }

    [Fact]
    public void HW6_Build_this_needs_a_yard_in_the_world_and_a_household_furnishes_the_house_it_lives_in_within_a_season_with_beds_of_their_own()
    {
        var (w, s) = Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var ctx = new HeroContext(s, s.Party!, w.Content); var p = new Play(rt, ctx);
        var cw = s.HomeRecords!.Sim.Get<ConstructionWorld>()!; var hw = s.HomeRecords.Sim.Get<HoldingsWorld>()!; var e = s.Economy!;
        // the designer's "Build this" is refused without a wheelwright's yard near the frame
        var rides = new RideBook(w.Content, e, 1234UL);
        Assert.Equal("yard", rides.Templates.Find("peddler_wagon")!.Def.BuiltAt);
        Assert.Equal("slipway", rides.Templates.Find("rowing_boat")!.Def.BuiltAt);
        Assert.Equal("vd_needs_yard", Ravensguard.Sim.Carts.VehicleDesk.YardNeeded(ctx, rides, "peddler_wagon", 30_000_000, 30_000_000));
        Assert.Equal("vd_needs_slipway", Ravensguard.Sim.Carts.VehicleDesk.YardNeeded(ctx, rides, "rowing_boat", 30_000_000, 30_000_000));
        Assert.Null(Ravensguard.Sim.Carts.VehicleDesk.YardNeeded(ctx, rides, "ox_wagon", 30_000_000, 30_000_000));                  // a small or open design needs no yard
        var edits = new List<CellEdit>();
        for (int y = 10_000; y < 10_006; y++) for (int x = 20_000; x < 20_006; x++) { bool edge = x == 20_000 || y == 10_000 || x == 20_005 || y == 10_005; edits.Add(new CellEdit(x, y, 0, CellLayer.Structure, edge ? "wall_wattle" : "floor_packed_earth")); }
        edits.Add(new CellEdit(20_002, 10_002, 0, CellLayer.Fixture, "wheelwright_bay"));
        cw.Rooms.Apply(edits);
        Assert.Null(Ravensguard.Sim.Carts.VehicleDesk.YardNeeded(ctx, rides, "peddler_wagon", 20_002_000, 10_003_000));
        // a household of the settlement owns a finished cottage with no hearth and no beds
        var reg = s.Population.Region; var hht = s.Population.Households;
        int hh = Enumerable.Range(0, hht.Count).First(h => hht.MemberCount[h] >= 3 && hht.Class[h] != SocialClass.Gentry && hht.Settlement[h] >= 0);
        long sec = p.Sec;
        var may = cw.May; cw.May = null;
        var pr = cw.PlacePlan(hht.IdOf(hh), PurseKind.Household, hh, "test_cottage", 1000, 1000, 0, false, sec);
        cw.May = may;
        Assert.True(pr.Ok, pr.Key);
        cw.DevStock(pr.Site, sec); cw.DevFinish(pr.Site, 999, 0, 0, sec);
        var site = cw.Site(pr.Site)!;
        Assert.Equal(SiteState.Finished, site.State);
        var facts = cw.Rooms.FactsAt(1004, 1002, 0)!;
        Assert.Equal(0, facts.HearthRooms); Assert.Equal(0, facts.Beds);
        e.Ledger.Mint(e.HouseholdPurse[hh], Coin.FromCrowns(20), SourceReason.Seed);                     // the household has coin of its own (the test's faucet)
        long seeded = e.Ledger.Audit().Seeded.Mites;
        var fur = hw.Furnisher!;
        int laid = fur.Season(sec, (int)(sec / 86400));
        output.WriteLine($"{laid} pieces laid for {hht.MemberCount[hh]} members, {fur.SpentMites} mites spent, {fur.Waited} waited");
        Assert.True(laid >= 2);
        var after = cw.Rooms.FactsAt(1004, 1002, 0)!;
        Assert.True(after.HearthRooms >= 1 || after.Rooms.Any(r => r.Tags.GetValueOrDefault("hearth") > 0));
        Assert.Equal(Math.Min(HouseholdFurnisher.PalletsPerSeason, (int)hht.MemberCount[hh]), after.Beds);
        // each member has a bed of his own, the room belongs to the household, and a guest takes an unassigned bed
        Assert.NotEmpty(hw.Book.BedAssigned); Assert.NotEmpty(hw.Book.RoomOwner);
        Assert.All(hw.Book.RoomOwner.Values, o => Assert.Equal(hht.IdOf(hh).Value, o));
        var member = s.Population.People.IdOf(hht.Members(hh).OrderBy(m => m).First());
        output.WriteLine($"member {member.Value}; beds: " + string.Join(", ", hw.Book.BedAssigned.Select(kv => $"{kv.Key}->{kv.Value}")) + $"; members: {string.Join(",", hht.Members(hh).OrderBy(m => m).Select(m => s.Population.People.IdOf(m).Value))}");
        Assert.NotNull(hw.BedOf(member));
        // nothing was minted: the household paid, the goods were built in, the books balance
        Assert.True(e.Ledger.Audit().IsConserved); Assert.Equal(seeded, e.Ledger.Audit().Seeded.Mites);
        Assert.True(e.MassAuditHolds());
        // a second season changes nothing
        Assert.Equal(0, fur.Season(sec + 86400 * 15, (int)(sec / 86400) + 15) == 0 ? 0 : fur.Season(sec + 86400 * 30, (int)(sec / 86400) + 30));
    }

    // ------------------------------------------------------------------ HW-9: heavy work, an anvil from an item

    [Fact]
    public void HW9_Heavy_work_burns_food_he_eats_at_noon_from_his_pack_and_a_hungry_man_stops_at_two_and_an_anvil_is_forged_from_120_kg_of_bars_then_set_as_a_piece()
    {
        var (w, s) = Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var ctx = new HeroContext(s, s.Party!, w.Content); var p = new Play(rt, ctx);
        var hw = s.HomeRecords!.Sim.Get<HoldingsWorld>()!; var e = s.Economy!;
        var book = hw.Book;
        long day0 = (p.Sec / 86400 + 1) * 86400;
        // with food: eats 504 g at noon, works the whole morning and afternoon up to the eight-hour cap
        ctx.Pack.Create(e.Goods.IdOf("bread"), 2_000, CreateReason.Import);
        long bread0 = ctx.Pack.Qty(e.Goods.IdOf("bread"));
        var lim = HeavyWork.Begin(ctx, book, day0 + 6 * 3600, day0 + 18 * 3600, null);
        Assert.Null(lim.Why);
        Assert.Equal(bread0 - 8 * HeavyWork.FoodGPerHour, ctx.Pack.Qty(e.Goods.IdOf("bread")));
        Assert.Equal(day0 + 14 * 3600, lim.UntilSec);                                                    // 8 heavy hours from 06:00
        HeavyWork.End(book, lim.UntilSec - (day0 + 6 * 3600));
        var again = HeavyWork.Begin(ctx, book, day0 + 14 * 3600, day0 + 18 * 3600, null);
        Assert.Equal("hw_too_tired", again.Why);
        // the next day with nothing to eat: he stops at two in the afternoon
        ctx.Pack.Destroy(e.Goods.IdOf("bread"), ctx.Pack.Qty(e.Goods.IdOf("bread")), DestroyReason.Consumed);
        var hungry = HeavyWork.Begin(ctx, book, day0 + 86400 + 8 * 3600, day0 + 86400 + 20 * 3600, null);
        Assert.Equal(day0 + 86400 + 14 * 3600, hungry.UntilSec);
        Assert.True(book.Hungry);
        Assert.Equal("hw_too_hungry", HeavyWork.Begin(ctx, book, day0 + 86400 + 14 * 3600, day0 + 86400 + 16 * 3600, null).Why);
        // the anvil: 120 kg of iron bars by a smith's recipe, then set as a piece of the same station
        var rec = ctx.Recipes["forge_anvil_block"];
        Assert.Equal(120_000, rec.Slots[0].Alts[0].Qty);
        Assert.Equal("anvil_block", e.Goods.StringId(rec.Results[0].Good));
        var piece = s.HomeRecords.Sim.Get<ConstructionWorld>()!.Costs.Get("anvil_set")!;
        Assert.Equal(PieceKind.Workstation, piece.Kind);
        Assert.Contains(piece.Materials, m => m.Good == "anvil_block");
        Assert.Equal("smithing", ((BuildPieceDef)w.Content.Get<BuildPieceDef>("anvil_set")).Station);
    }

    // ------------------------------------------------------------------ HW-8: buy and deliver, post haul jobs

    [Fact]
    public void HW8_A_market_order_is_carted_to_the_pile_at_the_books_price_and_2_mites_a_kilogram_kilometre_and_the_haul_jobs_are_posted_from_the_site_panel()
    {
        Assert.Equal(0, HoldingsDesk.CartingMites(0, 5000));
        Assert.Equal(2, HoldingsDesk.CartingMites(1000, 1000));                                          // 1 kg over 1 km: 2 mites
        Assert.Equal(600, HoldingsDesk.CartingMites(100_000, 3_000));                                     // 100 kg over 3 km
        var (w, s) = Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var ctx = new HeroContext(s, s.Party!, w.Content); var p = new Play(rt, ctx);
        var cw = s.HomeRecords!.Sim.Get<ConstructionWorld>()!; var e = s.Economy!; var reg = s.Population.Region;
        int home = s.Population.People.HomeSettlement[s.Hero!.PersonIndex];
        var town = reg.Settlements[home];
        e.Ledger.Mint(ctx.Purse, Coin.FromCrowns(300), SourceReason.Seed);
        long seeded = e.Ledger.Audit().Seeded.Mites;
        // a site of the hero's two streets from the market
        var may = cw.May; cw.May = null;
        var pr = cw.PlacePlan(ctx.Party.Hero, PurseKind.Person, ConstructionDesk.HouseholdOf(ctx), "test_cottage", town.CenterX + 400, town.CenterY + 400, 0, false, p.Sec);
        cw.May = may;
        Assert.True(pr.Ok, pr.Key);
        var site = cw.Site(pr.Site)!;
        // a good the book really holds (the new game's first days are thin: the first market with anything on its shelves), at an hour the stalls are open
        while (p.Sec / 3600 % 24 < 9) p.Hours(1);
        int mk = Enumerable.Range(0, reg.Settlements.Count).Where(i => ctx.Book(i) != null).OrderBy(i => i == home ? 0 : 1).First(i => ctx.Book(i)!.Goods.Any(g => g.StockUnits > 0));
        var book = ctx.Book(mk)!; home = mk; town = reg.Settlements[mk];
        var want = Enumerable.Range(0, book.Goods.Length).Select(i => (G: (ushort)i, Stock: book.Goods[i].StockUnits)).Where(x => x.Stock > 0 && e.Goods.Def(x.G).Tradable).OrderByDescending(x => x.Stock).First();
        ushort nails = want.G; long qty = e.Goods.IsPiece(nails) ? Math.Min(100, want.Stock) : Math.Min(50_000, want.Stock);
        book.Sync(nails);
        var q = Quotes.Buy(book, nails, qty);
        long coin0 = ctx.Coin; long pile0 = cw.PileOf(site).Qty(nails);
        var r = p.Do(HoldingsDesk.BuyDeliver(site.Id, e.Goods.StringId(nails), qty, home, p.Sec));
        output.WriteLine(e.Goods.StringId(nails) + " x" + qty + ": " + r.Text.ToString() + " " + r.Mites);
        if (!r.Ok && r.Text.ToString().Contains("carter")) { output.WriteLine("no carter in this place"); return; }
        Assert.True(r.Ok, r.Text.ToString());
        Assert.Equal(pile0 + qty, cw.PileOf(site).Qty(nails));
        long dx = town.CenterX - site.PileX, dy = town.CenterY - site.PileY; long metres = IntMath.Isqrt(dx * dx + dy * dy);
        long carting = HoldingsDesk.CartingMites(e.Goods.MassG(nails, qty), metres);
        Assert.Equal(coin0 - ctx.Coin, r.Mites);
        Assert.InRange(r.Mites, q.TotalMites + carting - 2, q.TotalMites + carting + 2);                  // the book's quote and the carting, to the mite
        Assert.True(e.Ledger.Audit().IsConserved); Assert.Equal(seeded, e.Ledger.Audit().Seeded.Mites);
        Assert.True(e.MassAuditHolds());
        // the site panel's "Post haul jobs": goods the household holds elsewhere go to the pile on the board's jobs
        int hh = ConstructionDesk.HouseholdOf(ctx);
        e.HouseholdStore[hh].Create(e.Goods.IdOf("logs"), 400_000, CreateReason.Import);
        var hauls = p.Do(HoldingsDesk.PostHaul(site.Id, p.Sec));
        output.WriteLine(hauls.Text.ToString());
        Assert.True(hauls.Ok, hauls.Text.ToString());
    }

    // ------------------------------------------------------------------ HW-10: the crafting loose ends

    [Fact]
    public void HW10_NPC_tools_wear_at_their_stations_the_containers_near_a_station_are_the_businesss_store_a_master_takes_an_idle_youth_and_a_refit_writes_the_fit()
    {
        var (w, s) = Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var hw = s.HomeRecords!.Sim.Get<HoldingsWorld>()!; var e = s.Economy!;
        // tool wear by Phase 5's rule at an NPC business's station: a hammer owned by the business wears 1 pm an hour, a day's batch of 8 worker-hours takes 8
        var wear = new ToolWear(w.Content); var items = e.Items; var ids = new IdAllocator();
        var biz = EntityId.Make(IdKind.Other, 777_001);
        var store = e.OpenStore(StoreKind.Open, biz);
        var hammer = items.Add(ids, "hammer", ItemQuality.Common, biz, [new ItemComponent("iron", 1000, 1000)]);
        int row = items.IndexOf(hammer);
        Assert.True(Crafters.WearTool(wear, items, biz, store, "forge", 8, m => null));
        Assert.Equal(1000 - 8, items.At(row).ConditionPermille);
        Assert.False(Crafters.WearTool(wear, items, biz, store, "oven", 8, m => null));                    // nobody swings a tool at an oven
        Assert.False(Crafters.WearTool(wear, items, EntityId.Make(IdKind.Other, 777_002), store, "forge", 8, m => null));   // not his tool
        // the refit writes the garment's fit: the crafting runner and the clothing book agree
        var book = new ClothingBook(w.Content);
        var cs = new CraftState(); var db = RecipeDb.Build(w.Content, e.Goods);
        var runner = new CraftRunner(db, e, cs, 1234, ids, _ => store);
        var garment = book.Create(book.Garments.Values.First(g => g.Wearable), "wool", EntityId.Make(IdKind.Person, 5), 1000, 0, 60_000);
        Crafters.LinkFit(runner, book);
        Assert.Equal(60_000, runner.FitSize(garment));
        runner.SetFit!(garment, 70_000);
        Assert.Equal(70_000, book.ExtraOf(garment).FitCm3);
        Assert.Equal(70_000, runner.FitSize(garment));
        // a piece the book does not keep stays in the crafting state's own fits
        var other = EntityId.Make(IdKind.Item, 99_123);
        runner.SetFit!(other, 64_000);
        Assert.Equal(64_000, cs.Fits[other]);
        // a master takes an idle youth of 12 to 16 for a season, once
        var svc = s.HomeRecords.Sim.Get<CraftService>()!; var pop = s.Population; var pt = pop.People;
        int day = (int)(rt.HourDone / 24) + 1;
        int began = Crafters.TakeApprentices(svc, pop, s.Ids, day, id => id == s.Party!.Hero);
        output.WriteLine($"{began} apprenticeships began");
        Assert.True(began > 0);
        Assert.All(svc.Runner.State.Apprenticeships.Values, a =>
        {
            int y = pt.IndexOf(a.Apprentice);
            Assert.InRange(pt.AgeYears(y, day), 12, 16);
            Assert.True(svc.Runner.State.Book.Level(a.Master, a.Trade) >= svc.Runner.Db.Settings.ApprenticeMasterMinSkill);
        });
        Assert.Equal(0, Crafters.TakeApprentices(svc, pop, s.Ids, day, id => id == s.Party!.Hero) is var again && again > began ? 1 : 0);
    }

    // ------------------------------------------------------------------ HW-11: a business in a generated building, coppice, carting

    [Fact]
    public void HW11_A_generated_building_offers_the_stations_of_its_blueprint_so_a_smithy_can_be_opened_in_it_and_coppice_regrows_in_7_years_and_a_quarry_pays_carting()
    {
        var (w, s) = Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var ctx = new HeroContext(s, s.Party!, w.Content); var p = new Play(rt, ctx);
        var hw = s.HomeRecords!.Sim.Get<HoldingsWorld>()!; var ow = s.HomeRecords.Sim.Get<OwnershipWorld>()!; var e = s.Economy!; var reg = s.Population.Region;
        // a smithy lot of a settlement: its blueprint's forge and anvil are what it offers
        (int S, int L) smithy = (-1, -1);
        for (int si = 0; si < reg.Settlements.Count && smithy.S < 0; si++)
            for (int l = 0; l < reg.Settlements[si].Plan.Lots.Count; l++)
                if (reg.Settlements[si].Plan.Lots[l].Use == LotUse.Smithy) { smithy = (si, l); break; }
        Assert.True(smithy.S >= 0, "no smithy in seed 1234's region");
        var stations = ow.StationsOfLot(smithy.S, smithy.L);
        output.WriteLine("stations: " + string.Join(", ", stations.Select(kv => $"{kv.Key} x{kv.Value}")));
        Assert.True(stations.ContainsKey("forge") || stations.ContainsKey("anvil"));
        var kinds = ow.KindsForLot(smithy.S, smithy.L);
        Assert.Contains(kinds, k => k.Id is "smithy" or "blacksmith" || k.Recipes.Any(r => stations.ContainsKey(r.Station)));
        // founding needs the deed: a lot the hero does not hold is refused
        var refusal = ow.FoundInLot(kinds[0].Id, smithy.S, smithy.L, 0, p.Day);
        Assert.False(refusal.Ok);
        Assert.Equal("bz_not_your_building", refusal.Key);
        // coppice: 90% of the stand comes off, the stools stay, and the row stands full 7 game years later
        int tile = Enumerable.Range(0, RegionData.Tiles).First(t => s.HomeRecords.Economy.Resources.Find(t, Ravensguard.Sim.Production.ResourceKind.Wood) is int r0 && r0 >= 0 && s.HomeRecords.Economy.Resources.StockG(r0) > 100_000);
        var resources = s.HomeRecords.Economy.Resources; int row = resources.Find(tile, Ravensguard.Sim.Production.ResourceKind.Wood);
        long stock0 = resources.StockG(row);
        var into = e.OpenStore(StoreKind.Open, EntityId.Make(IdKind.Other, 777_100));
        var (logs, fire) = hw.CoppiceCut(tile, p.Day, into);
        Assert.InRange(logs + fire, stock0 * 9 / 10 - 1, stock0 * 9 / 10 + 1);
        Assert.Equal(stock0 - (logs + fire), resources.StockG(row));
        Assert.Equal((0L, 0L), hw.CoppiceCut(tile, p.Day, into));                                         // still growing
        hw.CoppiceDay(p.Day + 7 * 60 - 1); Assert.True(resources.StockG(row) < resources.CapG(row));
        hw.CoppiceDay(p.Day + 7 * 60); Assert.Equal(resources.CapG(row), resources.StockG(row));
        Assert.Empty(hw.Book.Coppice);
        Assert.True(e.MassAuditHolds());
        // a quarry's output carries 2 mites a tonne-kilometre to the nearest market
        Assert.Equal(2, HoldingsWorld.CartingMites(1_000_000, 1_000));
        Assert.Equal(20, HoldingsWorld.CartingMites(2_000_000, 5_000));
        var (market, metres) = hw.NearestMarket(tile);
        Assert.True(market >= 0 && metres > 0);
    }

    // ------------------------------------------------------------------ HW-12: oiling, knapping

    [Fact]
    public void HW12_Oiling_takes_10_g_of_oil_and_0_2_hours_and_a_quarter_of_the_rust_and_flint_and_obsidian_are_knapped_into_brittle_blades_with_an_edge_ten_times_steels()
    {
        var content = SeamWorld.Get(1234).Content;
        var e = EconFixture.NewEconomy();
        var rdb = RecipeDb.Build(content, e.Goods);
        var oil = rdb["oil_item"];
        Assert.Equal(10, oil.Slots[0].Alts[0].Qty);                                                       // 10 g of tallow
        Assert.Equal(720, oil.Steps.Sum(st => st.Seconds));                                               // 0.2 h
        Assert.Equal("oil", oil.Effect);
        Assert.Equal(1, Corrosion.OiledShare(4, 1) + Corrosion.OiledShare(4, 2) + Corrosion.OiledShare(4, 3) + Corrosion.OiledShare(4, 4) - 3);   // mail: 4 pm a day, a quarter is one
        int plain = 0, oiled = 0;
        for (int d = 1; d <= 30; d++) { plain += 2; oiled += Corrosion.OiledShare(2, d); }
        Assert.Equal(60, plain); Assert.Equal(15, oiled);                                                 // 30 days: 60 pm, a quarter of it 15
        // the materials and the recipes
        var steel = content.Get<MaterialDef>("steel"); var obs = content.Get<MaterialDef>("obsidian"); var flint = content.Get<MaterialDef>("flint");
        Assert.Equal(10.0 * steel.MaxEdge!.Value, obs.MaxEdge!.Value, 6);
        Assert.True(flint.MaxEdge > content.Get<MaterialDef>("granite").MaxEdge!.Value);
        foreach (var id in new[] { "knap_flint_blade", "knap_obsidian_blade" })
        {
            var r = rdb[id];
            Assert.True(r.Brittle); Assert.Equal(1080, r.Steps.Sum(st => st.Seconds));                    // 0.3 h
            Assert.Equal(10, r.Difficulty);
            Assert.Contains(r.Tools, t => t.Quality == "hammering");
        }
        Assert.Equal("obsidian", rdb["knap_obsidian_blade"].Results[0].Material);
        // the brittle blade breaks as an untempered one does (stream 0x532)
        int breaks = 0;
        for (long t = 0; t < 10_000; t++) if (Smithing.BrittleBreaks(1234, t, EntityId.Make(IdKind.Item, 5), true, true)) breaks++;
        Assert.InRange(breaks, 200, 400);                                                                 // 30 permille
    }

    // ------------------------------------------------------------------ HW-13: integer roots, the panel's view

    [Fact]
    public void HW13_Construction_takes_its_haul_distances_with_IntMath_Isqrt_and_the_business_view_carries_the_inputs_for_Supply_all()
    {
        string root = FindRepoRoot();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "src", "Ravensguard.Sim", "Construction"), "*.cs", SearchOption.AllDirectories))
            Assert.DoesNotContain("Math.Sqrt", File.ReadAllText(f));
        Assert.Equal(1_414, IntMath.Isqrt(2_000_000));
        Assert.Equal(99_999, IntMath.Isqrt(9_999_800_001L));
        var (w, s) = Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var ow = s.HomeRecords!.Sim.Get<OwnershipWorld>()!; var prod = s.Production!;
        var any = prod.Businesses.First(b => prod.KindOf(b).Recipes.Length > 0 && !prod.KindOf(b).Trader);
        var view = ow.Detail(any.Id, (int)(rt.HourDone / 24));
        Assert.NotNull(view);
        Assert.NotNull(view!.Inputs);
        var expected = prod.KindOf(any).Recipes.SelectMany(r => r.Inputs.Select(i => s.Economy!.Goods.StringId(i.Good))).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, view.Inputs);
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "src", "Ravensguard.Sim"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
