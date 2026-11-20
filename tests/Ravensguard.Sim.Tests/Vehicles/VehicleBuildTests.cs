using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Crafting;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.World;
using Xunit.Abstractions;
using Sk = Ravensguard.Sim.People.Skill;

namespace Ravensguard.Sim.Tests.VehicleDesign;

/// <summary>P5-G: the vehicle-designer gate, part 2 (VD-4, VD-17, VD-18, VD-19): building part by part, commissions, saves and the abstract barge leg.</summary>
public class VehicleBuildTests(ITestOutputHelper output)
{
    private static readonly EntityId Maker = EntityId.Make(IdKind.Person, 500), Client = EntityId.Make(IdKind.Person, 77), Stranger = EntityId.Make(IdKind.Person, 501);

    /// <summary>Crafts one part's item the way a maker's bench does (the recipe's result shape, its mass) and gives it to <paramref name="owner"/>: the test's stand-in for the crafting screen.</summary>
    internal static EntityId PartItem(EconomyState e, IdAllocator ids, string part, EntityId owner, ItemQuality q = ItemQuality.Common) =>
        e.Items.Add(ids, part, q, owner, [new ItemComponent("oak", 1000, 1000)]);

    // ------------------------------------------------------------------ VD-4: build hours and an unfinished wagon

    [Fact]
    public void VD4_TheOxWagonSite_TakesTheSumOfItsPartRecipesAndInstalls_AndWithTwoWheelsMissingItDoesNotMove()
    {
        var econ = EconFixture.NewEconomy(false);
        var b = new RideBook(EconFixture.Content, econ, 7); var cat = b.Templates;
        var db = RecipeDb.Build(EconFixture.Content, econ.Goods);
        var ox = cat.Get("ox_wagon");
        var (rec, ins) = VehicleSites.BuildMilli(ox, cat, db);
        // the content sums, part by part
        long r2 = 0, i2 = 0;
        foreach (var p in ox.Parts)
        {
            var d = cat.PartList[p.Part]; i2 += (long)Math.Round(d.InstallH * 1000);
            foreach (var s in db[d.Recipe].Steps) if (s.Attended) r2 += s.Seconds * 1000 / 3600;
        }
        Assert.Equal((r2, i2), (rec, ins));
        output.WriteLine($"ox wagon: {ox.Parts.Count} parts, recipes {rec / 1000.0:0.0} h, installs {ins / 1000.0:0.0} h, {(rec + ins) / 1000.0:0.0} worker-hours in all");
        Assert.InRange((rec + ins) / 1000, 150, 260);                                            // "about 182 worker-hours": the content's own sum
        Assert.Equal(38, ox.Parts.Count);
        // the site: the hero puts every part but two wheels on the pile, and works it with his own hands
        var ids = new IdAllocator(); var hero = Client;
        var (a, v) = VehicleSites.Order(b, "ox_wagon", hero, 10_000, 20_000);
        Assert.Equal(SiteAnswer.Ok, a); Assert.NotNull(v);
        Assert.True(v!.Broken && !v.CanMove && !v.Finished);
        Assert.Equal(0, b.Vehicles.StatsOf(v).MassKg);
        int wheelsLeft = 2;
        foreach (var part in VehicleSites.Wanted(v, cat))
        {
            if (part == "wheel_spoked" && wheelsLeft-- > 0) continue;
            Assert.Equal(SiteAnswer.Ok, VehicleSites.Deposit(b, econ.Items, v, PartItem(econ, ids, part, hero), hero));
        }
        Assert.Equal(SiteAnswer.WrongPart, VehicleSites.Deposit(b, econ.Items, v, PartItem(econ, ids, "keel", hero), hero));      // not a part of this wagon
        long used = VehicleSites.Work(b, econ.Items, v, 1_000_000, 0);
        Assert.Equal(10 * 500 + 2 * 1000 + 2 * 500, used);                                     // the frame, the axles and two wheels: the third wheel is missing and the order waits for it...
        Assert.False(v.Finished);
        Assert.True(v.Broken); Assert.False(v.CanMove);                                       // ...and the wagon does not move
        b.Driving = v.Id; v.Driven = true; Assert.True(b.Plan(false, false, 0).Blocked); b.Driving = EntityId.None; v.Driven = false;
        // the wheels come: it is finished, with the template's numbers
        for (int k = 0; k < 2; k++) Assert.Equal(SiteAnswer.Ok, VehicleSites.Deposit(b, econ.Items, v, PartItem(econ, ids, "wheel_spoked", hero), hero));
        Assert.Equal(ins - used, VehicleSites.Work(b, econ.Items, v, 100_000, 3600));        // the rest of the installs, no more
        Assert.True(v.Finished && v.CanMove && !v.Broken);
        Assert.Equal(ox.Stats, b.Vehicles.StatsOf(v));
        Assert.Empty(VehicleSites.Pile(econ.Items, v.Id));
        Assert.Equal(3600, b.Works.Sites[v.Id].DoneSec);
        // one worker-hour rule: skill 50 puts in an hour an hour, skill 0 ten thirteenths of one, skill 100 a hour and two thirds
        Assert.Equal((1000, 769, 1428), (VehicleSites.MilliPerHour(50), VehicleSites.MilliPerHour(0), VehicleSites.MilliPerHour(100)));
    }

    // ------------------------------------------------------------------ VD-17: commission a cartwright

    private static CraftContext Ctx(CraftWorld w, Store makerStore) => new()
    {
        Econ = w.Econ, Runner = w.R,
        PurseOf = p => p == Maker || p == Client || p == Stranger ? w.Econ.Ledger.Open(PurseKind.Person, p) : w.Econ.Ledger.TryFind(PurseKind.Person, p, out var pp) ? pp : null,
        StoreOf = p => p == Maker ? makerStore : w.Econ.OtherStores.GetValueOrDefault(p),
        SiteOf = (p, r) => p == Maker ? CraftWorld.FullSite(w.Db) : null,
        ActorOf = p => new CraftActor(p),
        SettlementOf = p => p == Maker || p == Client || p == Stranger ? 0 : -1,
        PeopleOf = s => s == 0 ? [Maker] : [],
    };

    [Fact]
    public void VD17_ACartwrightIsCommissionedToBuildAHayWain_MadeInHisShop_FittedAtHisYard_CollectedInPerson_NeverInstant()
    {
        var w = new CraftWorld();
        var ms = w.Econ.OpenStore(StoreKind.House, Maker, 0, 0, false);
        var ctx = Ctx(w, ms);
        w.Skill(Sk.Carpentry, 70, Maker); w.Skill(Sk.Smithing, 60, Maker);
        var b = new RideBook(EconFixture.Content, w.Econ, 7);
        var wain = b.Templates.Get("hay_wain");
        // his materials for every part (bought in by him: the test's faucet), and coin
        foreach (var p in wain.Parts)
        {
            var r = w.Db[b.Templates.PartList[p.Part].Recipe];
            foreach (var slot in r.Slots) ms.Create(slot.Alts[0].Good, slot.Alts[0].Qty, CreateReason.Import);
            foreach (var s in r.Steps) if (s.FuelGood != ItemTable.NoGood) ms.Create(s.FuelGood, s.FuelQty, CreateReason.Import);
        }
        w.Econ.Ledger.Mint(w.Econ.Ledger.Open(PurseKind.Person, Client), new Coin(500_000), SourceReason.Seed);
        long coin0 = w.Econ.Ledger.Audit().InPurses.Mites;
        long order = 8 * 3600;
        var (a, com) = VehicleSites.Commission(b, ctx, "hay_wain", Client, Maker, 50_000, 60_000, order, w.Ids);
        Assert.Equal(SiteAnswer.Ok, a); Assert.NotNull(com);
        Assert.Equal(wain.Parts.Count, com!.Parts.Count);
        Assert.All(com.Parts, id => Assert.Equal(Maker, w.St.Commissions[id].Maker));                 // every part made in his shop
        var v = b.Vehicles.Find(com.Vehicle)!;
        Assert.True(v.Broken && !v.Finished);
        output.WriteLine($"hay wain: parts {com.PartsMites} mites, install {com.InstallMites} mites; {com.Parts.Count} part commissions");
        // the hours pass: his bench (P5-E's hourly pass), then the site's
        long h = order / 3600, readyHour = -1;
        for (; h < order / 3600 + 24 * 60 && readyHour < 0; h++)
        {
            Commissions.Hourly(ctx, h);
            VehicleSystem.Hour(b, ctx, _ => null, w.Econ.Items, h);
            if (com.State == VehicleCommissionState.Ready) readyHour = h + 1;
            if (h == order / 3600 + 1) Assert.Equal(VehicleCommissionState.Making, com.State);        // never instant
        }
        Assert.True(readyHour > 0, "the wain was never finished");
        var (rec, ins) = VehicleSites.BuildMilli(wain, b.Templates, w.Db);
        long workHours = readyHour - order / 3600;
        output.WriteLine($"ready after {workHours} hours ({workHours / 24.0:0.0} days); the content's {(rec + ins) / 1000.0:0} worker-hours at a master's pace, 10 hours a day");
        Assert.True(workHours * 10 / 24 >= (rec + ins) / 1000 * 1000 / VehicleSites.MilliPerHour(70) / 2, "faster than his hours allow");
        Assert.True(v.Finished && v.CanMove);
        Assert.Equal(wain.Stats.MassKg, b.Vehicles.StatsOf(v).MassKg);
        // collected only by the client, in person, at the yard
        Assert.Equal(SiteAnswer.NotYours, VehicleSites.Collect(b, ctx, com, Stranger, 50_000, 60_000));
        Assert.Equal(SiteAnswer.NotHere, VehicleSites.Collect(b, ctx, com, Client, 500_000, 60_000));
        long before = w.Econ.Ledger.Balance(w.Econ.Ledger.Open(PurseKind.Person, Client)).Mites;
        Assert.Equal(SiteAnswer.Ok, VehicleSites.Collect(b, ctx, com, Client, 51_000, 60_000));
        Assert.Equal(before - com.InstallMites, w.Econ.Ledger.Balance(w.Econ.Ledger.Open(PurseKind.Person, Client)).Mites);
        Assert.Equal(Client, v.Owner);
        Assert.False(b.Works.Sites.ContainsKey(v.Id));
        Assert.Equal(0, w.Econ.Ledger.Balance(w.Econ.Ledger.Open(PurseKind.Person, v.Id)).Mites);
        Assert.Equal(coin0, w.Econ.Ledger.Audit().InPurses.Mites);   // coin conserved
        Assert.True(w.Audit());                                                                           // goods conserved
    }

    // ------------------------------------------------------------------ VD-18: saves

    [Fact]
    public void VD18_SaveMidBuildAndMidVoyage_IsByteIdentical_AndAnOldSavesWornCartBecomesPartRows()
    {
        var econ = EconFixture.NewEconomy(false);
        var book = new RideBook(EconFixture.Content, econ, EconFixture.Shared.Cs.Seed);
        var ids = new IdAllocator();
        // a player's design, half built
        var des = new VehicleDesigner(book.Templates, book.Breeds); des.Load(book.Templates.Get("ox_wagon").Def); des.Id = "player.my_wagon"; des.Name = "My wagon";
        var designs = new DesignBook(); designs.Put(des.Id, des.Text(), DesignKind.Vehicle);
        Assert.Empty(book.AttachDesigns(designs));
        var (_, site) = VehicleSites.Order(book, "player.my_wagon", Client, 1000, 2000);
        foreach (var part in VehicleSites.Wanted(site!, book.Templates).Take(20)) VehicleSites.Deposit(book, econ.Items, site!, PartItem(econ, ids, part, Client), Client);
        VehicleSites.Work(book, econ.Items, site!, 5250, 7200);
        book.Works.Sites[site!.Id].Crew.Add(Maker); book.Works.Sites[site.Id].CrewSkill.Add(40);
        Assert.Contains(site.Parts, p => p.State == PartState.InProgress);
        // a barge under way on a leg
        var barge = book.Vehicles.Create(book.Templates.Get("barge"), Client, 0, 0);
        barge.Cargo.Create(econ.Goods.IdOf("wheat"), 3_000_000, CreateReason.Other);
        book.Works.Voyages[barge.Id] = new Voyage { Boat = barge.Id, Waterway = 3, AtMm = 1000, ToMm = 9_000_000, GroundMmps = 1234, LegStartSec = 3600, Leg = 1 };
        // a worn cart
        var cart = book.Vehicles.Create(book.Templates.Get("cart"), Client, 5, 5); cart.WheelPm = 525;
        var world = EconFixture.WorldWith(econ); world.Mounts = book; world.Designs = designs;
        var bytes = SaveCodec.Encode(WorldSave.Capture(world, EconFixture.ContentHash, "0.1.0"));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
        var rb = back.Mounts!;
        Assert.Equal(VehicleHash.Of(book), VehicleHash.Of(rb));
        var s2 = rb.Vehicles.Find(site.Id)!;
        Assert.Equal("player.my_wagon", s2.Design);
        Assert.Equal(site.Parts.Select(p => (p.SortKey, p.State, p.HpMilli, p.WorkMilli)), s2.Parts.Select(p => (p.SortKey, p.State, p.HpMilli, p.WorkMilli)));
        Assert.Equal(VehicleSites.Pile(econ.Items, site.Id).Count, VehicleSites.Pile(back.Economy!.Items, site.Id).Count);
        Assert.Equal(1234, rb.Works.Voyages[barge.Id].GroundMmps);
        Assert.Equal(525, rb.Vehicles.Find(cart.Id)!.WheelPm);
        // an old save (before part rows): its rows name the template by the Phase 3 index and keep one WheelPm; they load as part rows with each wheel at that share (VD-6's numbers)
        var m = MountsSave.Capture(book);
        foreach (var r in m.Vehicles) { r.Design = ""; r.PartState = []; r.PartX = []; r.PartY = []; r.PartDeck = []; r.PartOrder = []; r.PartIdx = []; r.PartTier = []; r.PartHp = []; r.PartWork = []; }
        m.Vehicles.RemoveAll(r => r.Id != cart.Id.Value); m.Vehicles[0].Template = Array.IndexOf(MountsSave.LegacyTemplates, "cart"); m.Works = null; m.PartTable = [];
        var old = m.Restore(EconFixture.Content, EconFixture.NewEconomy(false), 7);
        var oc = old.Vehicles.Rows.Single();
        Assert.Equal("cart", oc.Design);
        Assert.Equal(old.Templates.Get("cart").Parts.Count, oc.Parts.Count);
        foreach (var p in oc.Parts) if (old.Templates.PartList[p.Part].Tags.Contains("wheel")) Assert.Equal(63_000, p.HpMilli);
        Assert.Equal(525, oc.WheelPm);
    }

    // ------------------------------------------------------------------ VD-19: the abstract and the live barge leg agree

    [Fact]
    public void VD19_AnEightKmDownstreamLeg_TakesTheSameSecondsLiveAndAbstract()
    {
        var goods = EconFixture.Catalog(); var ledger = new MassLedger(goods.Count);
        var b = new RideBook(EconFixture.Content, goods, ledger, 7);
        var barge = b.Vehicles.Create(b.Templates.Get("barge"), EntityId.None);
        barge.Cargo.Create(goods.IdOf("wheat"), 6_000_000, CreateReason.Other);
        int width = 2000;                                                                         // a river: 1.8 m deep, 0.8 m/s
        int ground = Voyages.GroundMmps(b, barge, width, 0, true);
        long abs = Voyages.LegSeconds(8_000_000, ground);
        var (depth, current) = b.Templates.Water.River(width, 0);
        // live: the hero at the barge's rudder, the barge poled downstream (crew as the abstract leg counts it)
        b.RiderKg = BoatMath.PersonKg; b.Driving = barge.Id; barge.Driven = true;
        b.WaterNow = () => new BoatWater(depth, current, 0, 0, 0, false, 0);
        var g = new LevelGrid(8100, 12, 0, 1); g.FillRect(0, 0, 8099, 11, 0, true);
        var w = new LiveWorld(g, new LightGrid(g), new Cell3(2, 6, 0), GameTime.FromCalendar(1, Season.Spring, 8, 12));
        w.AttachRide(b); w.SetInput(new MoveInput(1, 0));
        long start = b.HeroXMm, ticks = 0;
        while (b.HeroXMm - start < 8_000_000 && ticks < 400_000) { w.Step(); ticks++; }
        output.WriteLine($"barge {b.Vehicles.StatsOf(barge).MassKg} kg + 6 t: {ground} mm/s downstream, live {ticks} gs, abstract {abs} gs");
        Assert.True(Math.Abs(ticks - abs) <= 1, $"{ticks} vs {abs}");
        // and the abstract voyage lands where and when it says
        var path = new WaterPath(new Ravensguard.Sim.WorldGen.Region.Waterway { Id = 9, Points = [new(0, 0), new(8000, 0)], WidthCm = [width, width] });
        var y = Voyages.Start(b, barge, path, 0, 8_000_000, 0);
        barge.Driven = false;
        Assert.Equal(0, y.LegDelaySec);
        long hours = (abs + 3599) / 3600;
        for (long h = 0; h < hours; h++) Voyages.Hour(b, _ => path, h);
        Assert.Equal(abs, y.ArrivedSec);
        Assert.Equal(8_000_000, barge.XMm);
        // a loaded barge on a leg through a ford-shallow stretch is aground a while: the 0x552 delay
        var shallow = new WaterPath(new Ravensguard.Sim.WorldGen.Region.Waterway { Id = 10, Points = [new(0, 0), new(4000, 0), new(8000, 0)], WidthCm = [width, 200, width] });
        barge.Cargo.Create(goods.IdOf("wheat"), 3_000_000, CreateReason.Other);
        var y2 = Voyages.Start(b, barge, shallow, 0, 8_000_000, 0);
        Assert.InRange(y2.LegDelaySec, 0, 7200);
        Assert.Equal(BoatMath.LegDelaySeconds(7, barge.Id, y2.Leg), y2.LegDelaySec);
    }
}
