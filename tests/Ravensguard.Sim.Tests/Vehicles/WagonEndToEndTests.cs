using Ravensguard.Content;
using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.VehicleDesign;

/// <summary>
/// The Phase 5 done-when (overview 4, OV-1): design a wagon, have it built, drive it loaded to market and get it stuck in mud. A whole new game: the hero designs a four-wheeled wagon with a pivoting
/// front axle in the designer, saves it and commissions the town's carpenter through the hero's desk; the world's own hourly passes (his bench, then the vehicle pass) make every part and fit it, never
/// at once; he collects it in person, hitches two oxen, loads 900 kg of grain, drives toward the market, and after 15 mm of rain sticks on the mud road; unloading 500 kg and two pushers free it; he
/// reloads on firm ground, drives on and sells the grain at the market. Coin and goods are audited at the end.
/// </summary>
public class WagonEndToEndTests(ITestOutputHelper output)
{
    private static (SeamWorld W, WorldState State) Game()
    {
        var w = SeamWorld.Get(1234);
        var home = w.BlockOrigin.Offset(1, 1);
        var dir = Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;
        var cs = Ravensguard.Sim.WorldGen.ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = Ravensguard.Sim.WorldGen.History.HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);
        LandmarkPlacer.Place(region, w.History, w.Content);
        var pop = Populator.Populate(region, w.History, w.Content);
        var h = HeroRecord.FromOrigin("Aldric Vane", false, w.Content.All<OriginDef>().OrderBy(o => o.Order).First());
        return (w, NewGameWorld.Create(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, h, null));
    }

    /// <summary>The OV-1 wagon: the ox wagon's body, the front axle a pivoting one, no iron (a carpenter makes it all).</summary>
    private static VehicleDesigner Design(RideBook b)
    {
        var d = new VehicleDesigner(b.Templates, b.Breeds) { Id = "player.market_wagon", Name = "Market wagon" };
        d.New(2, 8);
        string[] bed = ["frame_plank", "cargo_bed"];
        for (int x = 0; x < 2; x++)
        {
            d.PlaceStack(x, 0, bed); d.PlaceStack(x, 2, bed);
            d.PlaceStack(x, 1, x == 0 ? ["axle_wood", "wheel_spoked", "frame_plank", "cargo_bed"] : ["wheel_spoked", "frame_plank", "cargo_bed"]);
            d.PlaceStack(x, 3, x == 0 ? ["axle_pivot", "wheel_spoked", "frame_plank", "cargo_bed"] : ["wheel_spoked", "frame_plank", "cargo_bed"]);
            d.PlaceStack(x, 4, ["frame_plank", "seat_bench"]);
            d.PlaceStack(x, 5, ["pole_beam"]); d.PlaceStack(x, 6, ["pole_beam"]); d.PlaceStack(x, 7, ["pole_yoke"]);
        }
        return d;
    }

    [Fact]
    public void OV1_DesignAWagon_HaveItBuilt_DriveItLoadedToMarket_AndGetItStuckInMud()
    {
        var (w, s) = Game();
        using var rt = new WorldRuntime(s, w.Content, new MemoryRegionPager());
        var content = w.Content; var econ = s.Economy!; var reg = s.Population.Region; var people = s.Population.People;
        var ctx = new HeroContext(s, s.Party!, content);
        var hero = ctx.Party.Hero;
        econ.Ledger.Mint(ctx.Purse, Coin.FromCrowns(1000), SourceReason.Seed);
        var desk = new HeroDesk();
        long Sec() => rt.HourDone * 3600;
        void Do(HeroCommand c) { desk.Issue(c); desk.ApplyDue(ctx, rt.HourDone); Assert.All(desk.View!.Results, r => Assert.True(r.Ok, r.Text)); }

        // 1. the design: four wheels, a pivoting front axle, a pole and yoke for two oxen
        var book = VehicleDesk.BookOf(s, content);
        var des = Design(book);
        var ds = des.Stats();
        Assert.True(ds.CanBuild, string.Join(", ", ds.Issues.Select(i => i.Code)));
        Assert.Equal((4, 2, "pole_yoke", 48), (ds.Stats.Wheels, ds.Stats.Axles, ds.Stats.HitchKind, ds.Stats.TurningRadiusDm));
        output.WriteLine($"design: {ds.Stats.MassKg} kg, {ds.Stats.CargoKg} kg / {ds.Stats.CargoL} L, axle {ds.Stats.AxleLimitKg} kg, radius {ds.Stats.TurningRadiusDm} dm; {ds.AnimalsRoad} oxen on dry road, {ds.AnimalsMud} in mud");
        Do(VehicleDesk.SaveDesign(des.Id, des.Text(), Sec()));
        Assert.Contains(des.Id, s.Designs!.Designs.Keys);

        // 2. the town's carpenter (a wheelwright's bay at his shop): a master of his trade, with the timber in his yard
        var prod = s.Production!; var cs = s.Crafting!;
        var shop = prod.Businesses.Where(b => b.Owner >= 0 && prod.Content.Kinds[b.Kind].Stations.Any(k => k.Kind == "wheelwright_bay") && reg.Settlements[b.Settlement].Class == SettlementClass.Town)
            .OrderBy(b => b.Index).First();
        var maker = people.IdOf(shop.Owner);
        cs.Book.Set(maker, Skill.Carpentry, 65 * CraftMath.MicroPerLevel);
        var bp = book.Templates.Get(des.Id);
        foreach (var p in bp.Parts)
        {
            var r = ctx.Recipes[book.Templates.PartList[p.Part].Recipe];
            foreach (var slot in r.Slots) shop.Store.Create(slot.Alts[0].Good, slot.Alts[0].Qty, CreateReason.Import);
        }
        var town = reg.Settlements[shop.Settlement];
        long yardX = town.CenterX * 1000L, yardY = town.CenterY * 1000L;
        long orderSec = Sec();
        Do(VehicleDesk.Commission(des.Id, maker, yardX, yardY, orderSec));
        var com = book.Works.Commissions.Values.Single();
        var v = book.Vehicles.Find(com.Vehicle)!;
        Assert.Equal(bp.Parts.Count, com.Parts.Count);
        Assert.True(v.Broken && !v.Finished);

        // 3. the world runs: his bench makes the parts in his working hours, the vehicle pass collects them and he fits them at his yard
        long h0 = rt.HourDone;
        while (com.State == VehicleCommissionState.Making && rt.HourDone < h0 + 24 * 60) rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 24));
        Assert.Equal(VehicleCommissionState.Ready, com.State);
        long hours = com.ReadySec / 3600 - orderSec / 3600;
        var (rec, ins) = VehicleSites.BuildMilli(bp, book.Templates, ctx.Recipes);
        output.WriteLine($"built by the carpenter in {hours} game hours ({hours / 24.0:0.0} days) for {(rec + ins) / 1000.0:0} worker-hours of content; price {com.PartsMites + com.InstallMites} mites");
        Assert.True(hours * 10 / 24 >= (rec + ins) / 1000 * 1000 / VehicleSites.MilliPerHour(65) / 2, "never instant: at least his hours");
        Assert.True(v.Finished && v.CanMove);
        Assert.Equal(ds.Stats.MassKg, book.Vehicles.StatsOf(v).MassKg);

        // 4. collected in person at the yard; two oxen hitched (1,300 N); 900 kg of grain loaded
        Do(VehicleDesk.Collect(v.Id, yardX + 2000, yardY, Sec()));
        Assert.Equal(hero, v.Owner);
        var ox1 = book.Animals.Create(book.Breeds.Get("ox"), hero, 900); var ox2 = book.Animals.Create(book.Breeds.Get("ox"), hero, 900);
        Assert.Equal(HitchResult.Ok, book.Vehicles.Hitch(v, ox1)); Assert.Equal(HitchResult.Ok, book.Vehicles.Hitch(v, ox2));
        Assert.Equal(1300, book.Vehicles.Team(v).PullN);
        ushort grain = econ.Goods.IdOf("wheat");
        long coin0 = econ.Ledger.Audit().InPurses.Mites, grain0 = econ.Mass.Net(grain);
        Assert.True(v.Cargo.Create(grain, 900_000, CreateReason.Harvest));                      // his own harvest, loaded at the farm

        // 5. the road to market: 15 mm of rain turns the road to mud past the second kilometre
        var mud = SurfaceModel.Classify(Ground.Road, new WetState(150, 0, false, false));
        Assert.Equal(Surface.MudRoad, mud);
        long startX = 0;
        bool InMud(long x) => x - startX is >= 2_000_000 and < 2_060_000;                        // a 60 m stretch of mud road
        book.SurfaceNow = () => InMud(book.HeroXMm) ? mud : Surface.DryRoad;
        var g = new LevelGrid(8200, 12, 0, 1); g.FillRect(0, 0, 8199, 11, 0, true);
        var live = new LiveWorld(g, new LightGrid(g), new Cell3(2, 6, 0), GameTime.FromCalendar(1, Season.Spring, 8, 9));
        live.AttachRide(book); startX = book.HeroXMm;
        v.XMm = book.HeroXMm; v.YMm = book.HeroYMm;
        live.RideCommand(RideCode.Drive); live.Step();
        Assert.True(book.IsDriving);
        live.SetInput(new MoveInput(1, 0));
        long t = 0; bool stuck = false;
        while (book.HeroXMm - startX < 8_000_000 && t++ < 400_000)
        {
            live.Step();
            if (v.Stuck && !stuck)
            {
                stuck = true;
                // stuck: total 1,200 kg against 1,300 N in mud is a ratio over 1,300 (spec: 1,539)
                var sr = book.Vehicles.Speed(v, mud, 0, book.RiderKg, live.Clock.Now.Seconds);
                output.WriteLine($"stuck at {(book.HeroXMm - startX) / 1000} m: gross {book.Vehicles.GrossKg(v)} kg, ratio {sr.RatioPm}");
                Assert.True(InMud(book.HeroXMm));
                Assert.True(sr.RatioPm > 1300);
                // two pushers alone cannot free it loaded...
                live.SetInput(new MoveInput(0, 0));
                live.RideCommand(RideCode.Drive); live.Step();                                    // the hero steps down to push
                Assert.False(book.Push(2, live.Clock.Now.Seconds));
                // ...so 500 kg come off onto the verge, and the hero and the drover heave: 1,900 N against the 1,751 the stuck wagon needs
                Assert.InRange(book.UnloadToHeap(500), 500, 520);
                Assert.True(book.Push(2, live.Clock.Now.Seconds));
                Assert.False(v.Stuck);
                // on he goes at a crawl (the drover walks beside it): out of the mud, then the load goes back on
                book.RiderKg = 0;
                live.RideCommand(RideCode.Drive); live.Step();
                live.SetInput(new MoveInput(1, 0));
                long guard = 0;
                while (InMud(book.HeroXMm) && guard++ < 20_000) { live.Step(); if (v.Stuck) { Assert.True(book.Push(2, live.Clock.Now.Seconds)); } }
                Assert.False(InMud(book.HeroXMm));
                Assert.True(book.ReloadFromHeap() >= 500);
                Assert.Equal(0, book.Heap.TotalMassG());
                book.RiderKg = 85;
            }
        }
        Assert.True(stuck, "the wagon never stuck in the mud");
        Assert.True(book.HeroXMm - startX >= 8_000_000, $"stopped at {(book.HeroXMm - startX) / 1000} m");
        output.WriteLine($"8 km in {t} game seconds ({t / 3600.0:0.0} h), wheels at {v.WheelPm} permille");
        Assert.True(v.WheelPm < 1000);

        // 6. at the market: the grain into the pack, sold through the hero's desk
        var market = s.Markets!.BookForSettlement(shop.Settlement);
        Assert.NotNull(market);
        long kg = v.Cargo.Qty(grain);
        Assert.True(Store.Move(v.Cargo, ctx.Pack, grain, kg));
        rt.AdvanceTo(GameTime.FromGameHours((rt.HourDone / 24 + 1) * 24 + 10));                  // the next morning, in market hours
        long purse0 = econ.Ledger.Balance(ctx.Purse).Mites;
        long sell = Math.Min(kg, 20_000);                                                       // what the market's merchants can take today
        desk.Issue(new HeroCommand { Kind = HeroCommandKind.VisitMarket, Settlement = shop.Settlement, IssuedSec = Sec() });
        desk.Issue(new HeroCommand { Kind = HeroCommandKind.Sell, Settlement = shop.Settlement, Good = grain, Qty = sell, IssuedSec = Sec() });
        desk.ApplyDue(ctx, rt.HourDone);
        output.WriteLine("market: " + string.Join("; ", desk.View!.Results.Select(r => r.Code + " " + r.Mites)));
        Assert.Contains(desk.View!.Results, r => r.Kind == HeroCommandKind.Sell && r.Ok);
        Assert.True(econ.Ledger.Balance(ctx.Purse).Mites > purse0);

        // 7. the audits: coin only moved, and every gram of grain is somewhere
        Assert.Equal(coin0, econ.Ledger.Audit().InPurses.Mites);
        Assert.Equal(grain0 + 900_000, econ.Mass.Net(grain));
        Assert.True(econ.MassAuditHolds());
    }
}
