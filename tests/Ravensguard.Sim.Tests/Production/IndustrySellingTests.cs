using Ravensguard.Content;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Production.Industry;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>
/// P7-IN4 (industries 5.10, 10): IN-17 (the gate sale), IN-18 (the own caravan's quote by part), the contract routing, the absorption view, and the own caravan end to end on a whole runtime (a town trip and a border trip, coin and mass exact,
/// a lost caravan is lost capital, the quote against what the trip made, save and load with a caravan on the road equal to the uninterrupted run).
/// </summary>
public class IndustrySellingTests(ITestOutputHelper output)
{
    private static GameTime At(long day, int hour = 9) => GameTime.FromGameHours(day * 24 + hour);

    // ================================================================== the gate: a small world of two villages

    private sealed class SellKit
    {
        public EconomyState Econ; public Population Pop; public ProductionSystem Sys; public MarketSystem Markets; public IndustryBook Book = new(); public IndustryWorld World; public Works W = null!;
        public PersonTable People => Pop.People; public GoodCatalog Cat => Econ.Goods;
        public SellKit(int distM = 2000)
        {
            var region = new RegionData { Seed = 42 };
            for (int s = 0; s < 2; s++)
                region.Settlements.Add(new RegionSettlement { Id = EntityId.Make(IdKind.Settlement, (ulong)(s + 1)), Name = s == 0 ? "Testby" : "Faraway", Class = SettlementClass.Village, CenterX = 16000 + s * distM, CenterY = 16000, TargetPop = 6 });
            int per = 6, n = 2 * per;
            var people = new PersonTable(n, 1); var hh = new HouseholdTable(n, 1);
            for (int s = 0; s < 2; s++)
                for (int i = 0; i < per; i++)
                {
                    int h = hh.Add(s, 0, DwellingType.Cottage, 6); hh.Class[h] = SocialClass.Peasant;
                    int p = people.Add(); people.Household[p] = h; people.BirthDay[p] = -30 * TimeConst.DaysPerYear; people.HomeSettlement[p] = s; hh.AddMember(h, p); hh.Head[h] = p;
                }
            Pop = new Population
            {
                Region = region, People = people, Households = hh, NowDay = 0, LotHousehold = [[], []], LotBeds = [[], []], LotKeeper = [[], []], FiefLord = [], FiefLordHousehold = [],
                ParcelHolder = [], PriestHousehold = [-1, -1], SettlementPeople = [per, per],
            };
            Econ = EconomyState.FromContent(EconFixture.Content, hh.Count, people.Count);
            Econ.OpenHouseholds(hh);
            Markets = new MarketSystem(Econ, Pop, 42);
            Sys = new ProductionSystem(Econ, Pop, ProdKit.Content, 42, Markets, null);
            World = new IndustryWorld(Book, Sys, EconFixture.Content, 42);
            Econ.Ledger.Mint(Econ.HouseholdPurse[0], new Coin(2_000_000), SourceReason.Seed);
            // person 6 of the far village is not a carter; person 1 of the home village is (the first adult labourer of the place)
            People.Occupation[1] = Occupation.Labourer;
            var r = World.Found("iron", 0, 0, Econ.HouseholdPurse[0], 1_000_000, 0);
            Assert.True(r.Ok, r.Key);
            W = Book.Works[0];
        }
        public Business B(int stage) => Sys.ById(new EntityId(W.Stages[stage].Business))!;
        public MarketBook Book0 => Markets.BookForSettlement(0);
        public MarketBook Book1 => Markets.BookForSettlement(1);
        public ushort Bar => Cat.IdOf("iron_bar");
        public void Crew(int stage, int n)
        {
            var k = World.KindOf(W.Stages[stage]); var b = B(stage);
            for (int i = 0; i < n; i++) { int p = 2 + i; People.Occupation[p] = k.Primary; People.SetSkill(p, 0, k.Skill == Skill.None ? Skill.Farming : k.Skill, 50); Sys.Employ(b, p, k.Primary); }
        }
        /// <summary>Sets the book's price of bar so the bid is <paramref name="bidMitesPerKg"/> at an empty book (the curve's flat top, so a small lot is not repriced).</summary>
        public void Price(MarketBook book, long bidMitesPerKg)
        {
            var gb = book.Goods[Bar]; gb.StockUnits = 0; gb.StructuralDemand = 1_000_000_000;
            long bid0 = book.BidMicro(Bar); gb.RefMicro = gb.RefMicro * (bidMitesPerKg * 1000) / bid0;
        }
    }

    [Fact]
    public void IN17_GateSale_100KgOfBar_IsTheBooksPriceLessTheCarting_AndTheNearestWorthwhileBookWins()
    {
        var t = new SellKit(distM: 2000);
        t.Price(t.Book0, 300); t.Price(t.Book1, 440);                              // the far village (2 km) pays 440 a kg, the works' own town 300
        var s = t.W.Stages[4]; var b = t.B(4); const long qty = 100_000;
        var q = t.World.QuoteGate(t.W, s, t.Bar, qty);
        Assert.Equal(1, q.Settlement);
        Assert.InRange(q.GrossMites, 43_990, 44_010);                                    // IN-17: 100 kg at a bid of 440 is 44,000
        Assert.Equal(t.Book1.QuoteSell(t.Bar, qty), q.GrossMites);                          // the book's own price for the lot: the per-unit repricing is in it
        var ss = ProdKit.Content.Scale;
        long hay = Math.Max(1, (t.Book0.MidMicro(t.Cat.IdOf("hay")) + 500) / 1000), oats = Math.Max(1, (t.Book0.MidMicro(t.Cat.IdOf("oats")) + 500) / 1000);
        var cart = Carting.Plan(ss, 2000, qty, hay, oats, ProdKit.Content.Tax.GateTollMitesPerCart, true);
        Assert.Equal(cart.CrewAndTeamMites, q.CartMites); Assert.Equal(cart.TollMites, q.CartTollMites);
        Assert.True(q.CartMites > 0);
        Assert.Equal(q.GrossMites - q.CartMites - q.CartTollMites, q.NetOfCartingMites);
        Assert.Equal(q.NetOfCartingMites - q.MarketTollMites, q.NetMites);
        output.WriteLine($"gate quote: book {q.Settlement}, gross {q.GrossMites}, carting {q.CartMites} + toll {q.CartTollMites}, market toll {q.MarketTollMites}, net {q.NetMites}");
        // a far book that pays only a little more is not worth the cart: the works' own town takes it
        t.Price(t.Book1, 300);
        Assert.Equal(0, t.World.QuoteGate(t.W, s, t.Bar, qty).Settlement);
        _ = b;
    }

    [Fact]
    public void GateSale_ThroughTheWorksDay_MovesGoodsToTheFarBook_PaysTheCarter_AndKeepsCoinAndMassExact()
    {
        var t = new SellKit(distM: 2000);
        t.Price(t.Book0, 300); t.Price(t.Book1, 440);
        t.Crew(4, 1);
        var packing = t.B(4); packing.Store.Create(t.Bar, 100_000, CreateReason.Seed);
        var s = t.W.Stages[4]; Assert.Equal(Routing.Gate, s.Route);
        long carterBefore = t.Econ.Ledger.Balance(t.Econ.HouseholdPurse[t.People.Household[1]]).Mites, purse0 = t.Econ.Ledger.Balance(packing.Purse).Mites;
        var q = t.World.QuoteGate(t.W, s, t.Bar, 100_000);
        t.World.Day(t.W, 0, At(0));
        Assert.Equal(0, packing.Store.Qty(t.Bar));
        Assert.Equal(100_000, t.Book1.Goods[t.Bar].StockUnits);                          // listed at the far book, a real lot
        Assert.Equal(0, t.Book0.Goods[t.Bar].StockUnits);
        long carterAfter = t.Econ.Ledger.Balance(t.Econ.HouseholdPurse[t.People.Household[1]]).Mites;
        Assert.Equal(q.CartMites, carterAfter - carterBefore);                              // the crew and team are paid to the carter's household
        Assert.True(t.Econ.Ledger.Audit().IsConserved);
        Assert.True(t.Econ.MassAuditHolds());
        Assert.True(t.Econ.Ledger.Balance(packing.Purse).Mites <= purse0);
    }

    [Fact]
    public void GateSale_WithNoCarter_OrNoGainInTheRoad_SellsAtTheWorksOwnTown()
    {
        var t = new SellKit(distM: 2000);
        t.Price(t.Book0, 300); t.Price(t.Book1, 440);
        t.World.CarterOf = (_, _) => -1;                                                    // nobody to cart it
        t.Crew(4, 1);
        t.B(4).Store.Create(t.Bar, 100_000, CreateReason.Seed);
        t.World.Day(t.W, 0, At(0));
        Assert.Equal(100_000, t.Book0.Goods[t.Bar].StockUnits);
        Assert.Equal(0, t.Book1.Goods[t.Bar].StockUnits);
    }

    [Fact]
    public void Absorption_View_SaysWhatTheBooksTakeADay_AndWhenTheStockIsMoreThanAWeekOfIt()
    {
        var t = new SellKit(distM: 2000);
        t.Price(t.Book0, 300); t.Price(t.Book1, 440);
        foreach (var bk in new[] { t.Book0, t.Book1 }) bk.Goods[t.Bar].StructuralDemand = 50_000;       // 50 kg a day each
        var bar = t.B(4); bar.Store.Create(t.Bar, 300_000, CreateReason.Seed);
        var v = t.World.ViewGate(t.W.Stages[4].Business, t.Bar);
        Assert.Equal(100_000, v.AbsorbPerDay);                                              // two books in reach, 50 kg a day each, no rivals counted, no desk room in a bare kit
        Assert.Equal(3, v.DaysToClear); Assert.False(v.OverAbsorption);
        Assert.Equal(1, v.Settlement);
        bar.Store.Create(t.Bar, 1_000_000, CreateReason.Seed);
        var w = t.World.ViewGate(t.W.Stages[4].Business, t.Bar);
        Assert.True(w.OverAbsorption); Assert.Equal(13, w.DaysToClear);
        Assert.True(w.NetPerKgMilli > 0);
    }

    // ================================================================== contract routing

    [Fact]
    public void ContractRoute_NeedsAContract_HoldsItsDeliveriesBack_AndGoesToTheGateWhenItEnds()
    {
        var t = new SellKit(distM: 2000);
        var stage = t.W.Stages[4]; var id = stage.Business;
        var contracts = new List<SupplyContract>();
        t.World.ContractList = () => contracts;
        Assert.Equal("in_route_no_contract", t.World.SetRoute(id, Routing.Contract).Key);
        contracts.Add(new SupplyContract { Id = 1, Seller = id, Buyer = 99, Good = "iron_bar", QtyPerDelivery = 100_000, EveryDays = 5, StartDay = 1, TermSeasons = 1, PriceMicro = 400_000 });
        Assert.True(t.World.SetRoute(id, Routing.Contract).Ok);
        Assert.Equal(300_000, t.World.ContractHold(id, t.Bar));                              // three deliveries
        t.Crew(4, 1);
        var packing = t.B(4); packing.Store.Create(t.Bar, 450_000, CreateReason.Seed);
        t.World.Day(t.W, 0, At(0));
        Assert.Equal(300_000, packing.Store.Qty(t.Bar));                                      // the contract's deliveries stay; the surplus went to the gate
        Assert.Equal(150_000, t.Book0.Goods[t.Bar].StockUnits + t.Book1.Goods[t.Bar].StockUnits);
        contracts[0].State = SupplyState.Done;
        t.World.Day(t.W, 1, At(1));
        Assert.Equal(Routing.Gate, stage.Route);
        Assert.True(packing.Store.Qty(t.Bar) < 300_000);
        Assert.True(t.Econ.MassAuditHolds());
    }

    [Fact]
    public void CaravanRoute_NeedsAnOrder_AndTheOrderNeedsAContinent()
    {
        var t = new SellKit();
        var id = t.W.Stages[4].Business;
        Assert.Equal("in_caravan_no_order", t.World.SetRoute(id, Routing.Caravan).Key);
        Assert.Equal("in_caravan_no_continent", t.World.SetCaravanOrder(id, DestKind.Town, 1, 2, 2, 2, false).Key);
    }

    [Fact]
    public void SellingRows_SaveAndLoad_AndAnOldSectionKeepsItsBytes()
    {
        var t = new SellKit();
        var plain = SaveCodec.SerializeCompressed(IndustrySave.Capture(t.Book));
        var back = SaveCodec.DeserializeCompressed<IndustrySave>(plain).Restore();
        Assert.Equal(plain, SaveCodec.SerializeCompressed(IndustrySave.Capture(back)));
        Assert.Null(IndustrySave.Capture(t.Book).Selling);
        // an order, a trip with a manifest and a tally survive a round trip byte for byte
        var s = t.W.Stages[4]; s.Order = new CaravanOrder { Kind = DestKind.Neighbour, Dest = 17, Vehicle = 1, Wagons = 3, Guards = 2, Standing = true, Fleet = 0x1234, Master = 5 };
        var trip = new OwnTrip { Caravan = 0x1234, Stage = s.Business, Kind = DestKind.Neighbour, Dest = 17, Master = 5, DepartDay = 9, Days = 6, Wagons = 3, Guards = 2, Float = 12_000, Sales = 7_000, QuotedMargin = 5_000, QuotedRisk = 200, QuotedValue = 40_000, QuotedGross = 40_000, FodderPerDay = 90, Sold = true };
        trip.Manifest.Add((t.Bar, 500_000)); t.Book.AddTrip(trip);
        t.W.CaravanTrips = 3; t.W.CaravansLost = 1; t.W.CaravanSoldMites = 90_000; t.W.CaravanNetMites = 33_000;
        var bytes = SaveCodec.SerializeCompressed(IndustrySave.Capture(t.Book));
        var book2 = SaveCodec.DeserializeCompressed<IndustrySave>(bytes).Restore();
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(IndustrySave.Capture(book2)));
        Assert.Equal(t.Book.Hash(), book2.Hash());
        Assert.Equal(DestKind.Neighbour, book2.Works[0].Stages[4].Order!.Kind); Assert.Single(book2.Trips); Assert.Equal(3, book2.Works[0].CaravanTrips);
    }

    // ================================================================== the own caravan on a whole runtime

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

    private const string Hash = "h";

    /// <summary>What the runtime tests share: a hero with an iron works at a town of the region, its packing stage holding a tonne of bar, a free trader and a destination.</summary>
    private sealed class Rig : IDisposable
    {
        public SeamWorld W; public WorldState State; public WorldRuntime Rt; public IndustryWorld Iw; public Works Works = null!; public WorkStage Stage = null!; public Business Biz = null!;
        public HeroDesk Desk = new(); public HeroContext Ctx; public int Town, Dest; public long Sec => Rt.HourDone * 3600;
        public Rig(long capital = 800_000, long barG = 1_000_000)
        {
            (W, State) = Game();
            Rt = new WorldRuntime(State, W.Content, new MemoryRegionPager());
            var s = Rt.State; Iw = Rt.State.HomeRecords!.Sim.Get<IndustryWorld>()!;
            Ctx = new HeroContext(s, s.Party!, W.Content);
            s.Economy!.Ledger.Mint(Ctx.Purse, new Coin(capital + 100_000), SourceReason.Seed);
            var reg = s.Population.Region;
            Town = reg.Settlements.FindIndex(x => x.Class == SettlementClass.Town);
            Desk.Issue(IndustryDesk.Found("iron", Town, capital, Sec)); Desk.ApplyDue(Ctx, Rt.HourDone);
            Assert.All(Desk.View!.Results, r => Assert.True(r.Ok, r.Text));
            Works = s.Industry!.Works[0]; Stage = Works.Stages[^1]; Biz = s.Production!.ById(new EntityId(Stage.Business))!;
            Biz.Store.Create(s.Economy.Goods.IdOf("iron_bar"), barG, CreateReason.Seed);
            // a free trader of the town (Trading 60) and a destination: the nearest other settlement with its own book that the roads reach
            var pt = s.Population.People;
            for (int p = 0; p < pt.Count; p++) if (pt.IsAlive(p) && pt.Status[p] == PersonStatus.Alive && pt.HomeSettlement[p] == Town && pt.Sex[p] == Sex.Male && pt.AgeYears(p, (int)(Rt.HourDone / 24)) is >= 25 and < 50 && pt.Occupation[p] == Occupation.None && pt.Household[p] >= 0) { pt.SetSkill(p, 0, Skill.Trading, 60); break; }
            Dest = -1; long best = long.MaxValue;
            for (int i = 0; i < reg.Settlements.Count; i++)
            {
                if (i == Town || reg.Settlements[i].Class != SettlementClass.Town && reg.Settlements[i].Class != SettlementClass.Village) continue;
                var o = new CaravanOrder { Kind = DestKind.Town, Dest = i };
                if (Iw.Caravans.PathOf(Town, o, out _) is not { } path || path.LengthMm < 3_000_000) continue;
                if (path.LengthMm < best) { best = path.LengthMm; Dest = i; }
            }
            Assert.True(Dest >= 0, "no town to send a caravan to");
        }
        public ushort Bar => State.Economy!.Goods.IdOf("iron_bar");
        public void Run(long hours) => Rt.AdvanceTo(GameTime.FromGameHours(Rt.HourDone + hours));
        public void Issue(HeroCommand c) { Desk.Issue(c); Desk.ApplyDue(Ctx, Rt.HourDone); }
        public HeroResult Last => Desk.View!.Results[^1];
        public (long Coin, long Mass) Audit() => (State.Economy!.Ledger.Audit().InPurses.Mites + State.ContinentState!.TotalCoin(), 0);
        public void Dispose() => Rt.Dispose();
    }

    [Fact]
    public void IN18_OwnCaravanQuote_IsPrintedByPart_AndTheMarginIsTheSalesLessEveryPart()
    {
        using var rig = new Rig();
        var iw = rig.Iw; var day = (int)(rig.Rt.HourDone / 24); var set = ProdKit.Content.Industries.Settings;
        var order = new CaravanOrder { Kind = DestKind.Town, Dest = rig.Dest, Vehicle = 2, Wagons = 1, Guards = 2 };
        var q = iw.Caravans.Quote(rig.Stage.Business, order, day, ambushPpm: 40_000);             // IN-18: 1 t of bar, a master and 2 guards, a 4 percent chance of ambush
        Assert.True(q.Ok, q.Why);
        Assert.Equal(1_000_000, q.LoadG);
        Assert.Equal(0, q.Drivers); Assert.Equal(2, q.Guards);
        Assert.Equal((long)set.MasterWageMites * q.Days, q.MasterMites);
        Assert.Equal((long)set.GuardWageMites * 2 * q.Days, q.GuardMites);
        Assert.Equal(q.MasterMites + q.GuardMites, q.WagesMites);
        Assert.Equal(q.FodderPerDay * q.Days, q.FodderMites);
        Assert.Equal((long)Vehicles.PriceMites(VehicleKind.OxWagon) * set.WagonWearPmPerDay * q.Days / 1000, q.WearMites);
        Assert.Equal(q.GrossMites * set.MasterSharePm / 1000, q.MasterShareMites);
        Assert.Equal(q.GrossMites * 40_000 * set.AmbushTakePm / 1_000_000_000, q.RiskMites);       // 4 percent x value x 600 / 1,000
        Assert.Equal(q.GrossMites - (q.WagesMites + q.FodderMites + q.TollMites + q.CustomsMites + q.WearMites + q.MarketTollMites + q.MasterShareMites) - q.RiskMites, q.MarginMites);
        Assert.Equal(q.MarginMites, q.Parts().First().Mites - q.Parts().Skip(1).Sum(p => p.Mites));
        Assert.True(q.GrossMites > 0 && q.Days >= 2);
        output.WriteLine($"IN-18 quote to town {rig.Dest}: {q.PathMm / 1000} m, {q.Days} days, {q.MovingHours} moving hours, danger {q.Danger}");
        foreach (var (k, v) in q.Parts()) output.WriteLine($"   {k,-18} {v,10:N0}");
        output.WriteLine($"   margin {q.MarginMites:N0}; sold {q.LoadG - q.UnsoldG} of {q.LoadG} g; price age {q.PriceAgeDays}, known {q.PriceKnown}");
        // the hero's notebook: an old price is discounted by its age (1 percent a day), a price the book shows today is not
        var book = rig.State.Markets!.BookForSettlement(rig.Dest); var nb = new PriceNotebook();
        nb.Hear(new PriceNote(rig.Dest, rig.Bar, book.AskMicro(rig.Bar), book.BidMicro(rig.Bar), 0, day));
        var fresh = iw.Caravans.Quote(rig.Stage.Business, order, day, nb, 40_000);
        nb.Hear(new PriceNote(rig.Dest, rig.Bar, book.AskMicro(rig.Bar), book.BidMicro(rig.Bar), 0, day - 10)); nb = new PriceNotebook(); nb.Hear(new PriceNote(rig.Dest, rig.Bar, book.AskMicro(rig.Bar), book.BidMicro(rig.Bar), 0, day - 10));
        var old = iw.Caravans.Quote(rig.Stage.Business, order, day, nb, 40_000);
        Assert.Equal(10, old.PriceAgeDays); Assert.True(fresh.PriceKnown);
        Assert.True(old.GrossMites < fresh.GrossMites);
        Assert.InRange(old.GrossMites, fresh.GrossMites * 899 / 1000, fresh.GrossMites * 901 / 1000);
    }

    [Fact]
    public void OwnCaravan_ToATown_ShipsRealGoodsOnRealRoads_PaysEveryone_AndConservesCoinAndMass()
    {
        using var rig = new Rig();
        var s = rig.State; var led = s.Economy!.Ledger; var iw = rig.Iw;
        rig.Issue(IndustryDesk.Order(new EntityId(rig.Stage.Business), "town", rig.Dest, "ox", 1, 2, false, rig.Sec));
        Assert.True(rig.Last.Ok, rig.Last.Text.ToString());
        long coin0 = led.Audit().InPurses.Mites + s.ContinentState!.TotalCoin();
        var quote = iw.Caravans.Quote(rig.Stage.Business, null, (int)(rig.Rt.HourDone / 24), rig.Ctx.Party.Notebook);
        long purse0 = led.Balance(rig.Biz.Purse).Mites;
        rig.Issue(IndustryDesk.Send(new EntityId(rig.Stage.Business), rig.Sec));
        Assert.True(rig.Last.Ok, rig.Last.Text.ToString());
        var trip = Assert.Single(s.Industry!.Trips);
        var car = s.ContinentState.Caravans.Get(new EntityId(trip.Caravan))!;
        Assert.Equal(CaravanState.Loading, car.State); Assert.Equal(rig.Biz.Index, car.OwnerBusiness); Assert.True(car.Master >= 0); Assert.Equal(2, car.Guards);
        Assert.Equal(1_000_000, s.Economy.Goods.MassG(rig.Bar, s.Economy.OpenStore(StoreKind.Chest, car.Id, 0, 0, enforce: false).Qty(rig.Bar)));
        Assert.Equal(0, rig.Biz.Store.Qty(rig.Bar));                                          // the goods left the works' store for the wagon's
        Assert.Equal(coin0, led.Audit().InPurses.Mites + s.ContinentState.TotalCoin());          // the wagons and the float are transfers
        Assert.True(led.Balance(rig.Biz.Purse).Mites < purse0);
        output.WriteLine($"quote margin {quote.MarginMites:N0} (sales {quote.GrossMites:N0}, costs {quote.CostsMites:N0}, risk {quote.RiskMites:N0}), {quote.Days} days; float {trip.Float:N0}");
        long soldSeen = 0;
        for (int h = 0; h < 24 * 40 && s.Industry.Trips.Count > 0; h++) { rig.Run(1); soldSeen = Math.Max(soldSeen, trip.Sales); if (h % 24 == 0) { Assert.True(led.Audit().IsConserved); Assert.True(s.Economy.MassAuditHolds()); } }
        var works = s.Industry.Works[0];
        output.WriteLine($"trip ended after {(rig.Rt.HourDone - rig.Sec / 3600)} h: trips {works.CaravanTrips}, lost {works.CaravansLost}, sold {works.CaravanSoldMites:N0}, net {works.CaravanNetMites:N0}; chronicle {s.Chronicle.ByType("caravan_returned").Count()}");
        Assert.Empty(s.Industry.Trips);
        Assert.True(led.Audit().IsConserved); Assert.True(s.Economy.MassAuditHolds());
        Assert.Equal(coin0, led.Audit().InPurses.Mites + s.ContinentState.TotalCoin());
        Assert.Equal(CaravanState.Idle, car.State == CaravanState.Lost ? CaravanState.Idle : car.State);
        if (works.CaravanTrips == 1)
        {
            Assert.True(works.CaravanSoldMites > 0);
            Assert.Single(s.Chronicle.ByType("caravan_returned"));
            Assert.Equal(0, led.Balance(car.Account).Mites);                                  // everything left in the wagon's purse came home
            Assert.Equal(CaravanState.Idle, car.State); Assert.Equal(-1, car.Master); Assert.Equal(0, car.Guards);
            Assert.True(works.CaravanNetMites > -(trip.Float));
        }
        else Assert.Equal(1, works.CaravansLost);                                             // or the road took it: capital lost, never conjured back
    }

    [Fact]
    public void OwnCaravan_NeedsAMaster_AFreeCrew_AndCoin_AndOnlyOneTripAtATime()
    {
        using var rig = new Rig(capital: 20_000);
        var id = new EntityId(rig.Stage.Business);
        rig.Issue(IndustryDesk.Send(id, rig.Sec)); Assert.False(rig.Last.Ok);                  // no order yet
        rig.Issue(IndustryDesk.Order(id, "town", rig.Dest, "ox", 2, 2, false, rig.Sec)); Assert.True(rig.Last.Ok);
        rig.Issue(IndustryDesk.Send(id, rig.Sec)); Assert.Equal(HeroResultCode.CannotAfford, rig.Last.Code);        // two wagons are 72,000 mites
        rig.State.Economy!.Ledger.Mint(rig.Biz.Purse, new Coin(400_000), SourceReason.Seed);
        var pt = rig.State.Population.People;
        for (int p = 0; p < pt.Count; p++) if (pt.IsAlive(p)) pt.SetSkill(p, 0, Skill.Trading, 10);   // nobody a trader
        rig.Issue(IndustryDesk.Send(id, rig.Sec)); Assert.False(rig.Last.Ok); Assert.Equal(HeroResultCode.Refused, rig.Last.Code);
        Assert.Empty(rig.State.Industry!.Trips);
    }

    [Fact]
    public void OwnCaravan_ToAGate_SellsIntoTheNeighbourWithinItsBudget_PaysCustoms_AndBooksTheCrossing()
    {
        using var rig = new Rig();
        var s = rig.State; var led = s.Economy!.Ledger; var st = s.ContinentState!; int home = s.HomeRegion.Id;
        var rl = st.Link(home);
        var edge = rl.Edges.Where(e => !st.Summaries[e.Neighbour].Detailed && st.Summaries[e.Neighbour].Pop > 0 && !st.Edges[e.Edge].Closed && rig.Iw.Caravans.PathOf(rig.Town, new CaravanOrder { Kind = DestKind.Neighbour, Dest = e.Neighbour }, out _) != null).OrderBy(e => e.Neighbour).First();
        long coin0 = led.Audit().InPurses.Mites + st.TotalCoin();
        rig.Issue(IndustryDesk.Order(new EntityId(rig.Stage.Business), "gate", edge.Neighbour, "cart", 2, 1, false, rig.Sec)); Assert.True(rig.Last.Ok, rig.Last.Text.ToString());
        var q = rig.Iw.Caravans.Quote(rig.Stage.Business, null, (int)(rig.Rt.HourDone / 24));
        Assert.True(q.Ok, q.Why); Assert.Equal(DestKind.Neighbour, q.Kind);
        output.WriteLine($"gate quote: {q.PathMm / 1000} m, {q.Days} days, gross {q.GrossMites:N0}, tolls {q.TollMites}, customs {q.CustomsMites}, margin {q.MarginMites:N0}, unsold {q.UnsoldG / 1000} kg");
        rig.Issue(IndustryDesk.Send(new EntityId(rig.Stage.Business), rig.Sec)); Assert.True(rig.Last.Ok, rig.Last.Text.ToString());
        var trip = Assert.Single(s.Industry!.Trips);
        long exported0 = rig.State.HomeRecords!.Economy.Trade.ExportedMites;
        for (int h = 0; h < 24 * 60 && s.Industry.Trips.Count > 0; h++) { rig.Run(1); if (h % 48 == 0) Assert.True(led.Audit().IsConserved); }
        var works = s.Industry.Works[0];
        output.WriteLine($"gate trip: trips {works.CaravanTrips}, lost {works.CaravansLost}, sold {works.CaravanSoldMites:N0}, net {works.CaravanNetMites:N0}, exported {rig.State.HomeRecords.Economy.Trade.ExportedMites - exported0:N0}");
        Assert.Empty(s.Industry.Trips);
        Assert.True(led.Audit().IsConserved); Assert.True(s.Economy.MassAuditHolds());
        Assert.Equal(coin0, led.Audit().InPurses.Mites + st.TotalCoin());                          // coin that crossed is on the continent's books: the total is whole
        if (works.CaravanTrips == 1) Assert.True(rig.State.HomeRecords.Economy.Trade.ExportedMites > exported0);
        _ = trip;
    }

    [Fact]
    public void OwnCaravan_LostOnTheRoad_IsLostCapital_NotConjuredBack()
    {
        using var rig = new Rig();
        var s = rig.State; var led = s.Economy!.Ledger; var st = s.ContinentState!;
        st.DangerBonus = 100;                                                               // a road at danger 100 is ambushed within weeks
        long coin0 = led.Audit().InPurses.Mites + st.TotalCoin();
        rig.Issue(IndustryDesk.Order(new EntityId(rig.Stage.Business), "town", rig.Dest, "ox", 1, 0, true, rig.Sec)); Assert.True(rig.Last.Ok);
        int sent = 0, lost0 = 0;
        for (int day = 0; day < 200 && s.Industry!.Works[0].CaravansLost == 0; day++)
        {
            if (s.Industry.Trips.Count == 0 && rig.Biz.Store.Qty(rig.Bar) < 500_000) rig.Biz.Store.Create(rig.Bar, 1_000_000, CreateReason.Seed);
            if (s.Industry.Trips.Count == 0) { rig.Issue(IndustryDesk.Send(new EntityId(rig.Stage.Business), rig.Sec)); if (rig.Last.Ok) sent++; else break; }
            rig.Run(24);
            Assert.True(led.Audit().IsConserved);
        }
        var works = s.Industry!.Works[0];
        output.WriteLine($"sent {sent} caravans, trips {works.CaravanTrips}, lost {works.CaravansLost}, net {works.CaravanNetMites:N0}");
        Assert.True(sent >= 1);
        Assert.True(s.Economy.MassAuditHolds());
        long coin1 = led.Audit().InPurses.Mites + st.TotalCoin();
        Assert.Equal(coin0, coin1);                                                         // the test's own bar is no coin
        if (works.CaravansLost > 0) { Assert.True(works.CaravanNetMites < 0 || works.CaravanTrips > 0); Assert.Equal(0UL, s.Industry.Works[0].Stages[^1].Order!.Fleet); }
        _ = lost0;
    }

    [Fact]
    public void OwnCaravan_StandingOrder_SendsWhenTheStageHoldsALoad_AndNotBefore()
    {
        using var rig = new Rig(barG: 300_000);
        var s = rig.State; var id = new EntityId(rig.Stage.Business);
        rig.Issue(IndustryDesk.Order(id, "town", rig.Dest, "ox", 1, 1, true, rig.Sec)); Assert.True(rig.Last.Ok);
        rig.Issue(new HeroCommand { Kind = HeroCommandKind.Industry, IssuedSec = rig.Sec, Tag = $"in:route|{id.Value}|caravan", Origin = CommandOrigin.Human }); Assert.True(rig.Last.Ok, rig.Last.Text.ToString());
        Assert.Equal(Routing.Caravan, rig.Stage.Route);
        rig.Run(48);
        Assert.Empty(s.Industry!.Trips);                                                    // 300 kg is under 60 percent of a wagon's tonne
        rig.Biz.Store.Create(rig.Bar, 500_000, CreateReason.Seed);
        rig.Run(24 * 2);
        Assert.True(s.Industry.Trips.Count == 1 || s.Industry.Works[0].CaravanTrips + s.Industry.Works[0].CaravansLost > 0, "the standing order sent a caravan");
    }

    // ------------------------------------------------------------------ save, load, continue with a caravan on the road

    private static SaveFile SaveAt(WorldRuntime rt) => rt.AtBarrier(() => WorldSave.Capture(rt.State, Hash, "0.1")).GetAwaiter().GetResult();

    private static SortedDictionary<string, byte[]> Sections(WorldRuntime rt) => rt.AtBarrier(() =>
    {
        var d = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (k, v) in WorldSave.Capture(rt.State, Hash, "0.1").Sections) if (k != WorldSave.MetaSection) d[k] = v.ToArray();
        d["~continent"] = SaveCodec.SerializeCompressed(ContinentSave.Capture(rt.State.ContinentState!));
        foreach (var r in rt.Records.Resident) d[$"~records.{r.Coord.Id:D3}"] = System.Text.Encoding.ASCII.GetBytes(r.RecordsHash());
        return d;
    }).GetAwaiter().GetResult();

    [Fact]
    public void SaveLoadContinue_WithAnOwnCaravanOnTheRoad_EqualsTheUninterruptedRun()
    {
        using var straight = new Rig();
        var id = new EntityId(straight.Stage.Business);
        straight.Issue(IndustryDesk.Order(id, "town", straight.Dest, "ox", 1, 2, false, straight.Sec)); Assert.True(straight.Last.Ok);
        straight.Issue(IndustryDesk.Send(id, straight.Sec)); Assert.True(straight.Last.Ok, straight.Last.Text.ToString());
        long h0 = straight.Rt.HourDone;
        straight.Run(40);
        var trip = Assert.Single(straight.State.Industry!.Trips);
        var car = straight.State.ContinentState!.Caravans.Get(new EntityId(trip.Caravan))!;
        Assert.True(car.Active && car.Dist > 0, "the caravan is on the road at the save");
        var file = SaveAt(straight.Rt);
        Assert.Contains(IndustrySave.SectionName, file.Sections.Keys);
        var bytes = SaveCodec.Encode(file);
        straight.Run(24 * 14);
        var want = Sections(straight.Rt);

        var st = WorldSave.Restore(SaveCodec.Decode(bytes), straight.W.Content, Hash, null, straight.W.Continent, straight.W.History);
        st.Content = straight.W.Content;
        using var resumed = new WorldRuntime(st, straight.W.Content, new MemoryRegionPager());
        Assert.Single(resumed.State.Industry!.Trips);
        Assert.NotNull(resumed.State.HomeRecords!.Economy.Trade.Own);
        resumed.AdvanceTo(GameTime.FromGameHours(h0 + 40 + 24 * 14));
        var got = Sections(resumed);
        output.WriteLine($"trips {straight.State.Industry.Works[0].CaravanTrips} lost {straight.State.Industry.Works[0].CaravansLost}; industry hash {straight.State.Industry.Hash():x} v {resumed.State.Industry!.Hash():x}");
        Assert.Equal(straight.State.Industry.Hash(), resumed.State.Industry.Hash());
        Assert.Equal(straight.State.Industry.Works[0].CaravanSoldMites, resumed.State.Industry.Works[0].CaravanSoldMites);
        var bad = want.Keys.Union(got.Keys).Where(k => !(want.TryGetValue(k, out var x) && got.TryGetValue(k, out var y) && x.AsSpan().SequenceEqual(y))).ToList();
        Assert.Empty(bad);
    }

    [Fact]
    public void TwoIdenticalRuns_OfAnOwnCaravanTrip_GiveTheSameIndustryHash()
    {
        string Run()
        {
            using var rig = new Rig();
            var id = new EntityId(rig.Stage.Business);
            rig.Issue(IndustryDesk.Order(id, "town", rig.Dest, "ox", 1, 2, false, rig.Sec));
            rig.Issue(IndustryDesk.Send(id, rig.Sec));
            rig.Run(24 * 20);
            return rig.State.Industry!.Hash().ToString("x") + "/" + rig.State.ContinentState!.ComputeHash();
        }
        Assert.Equal(Run(), Run());
    }
}
