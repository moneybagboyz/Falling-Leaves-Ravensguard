using MessagePack;
using Ravensguard.Content;
using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Dialogue;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.EconomyAudit;
using Ravensguard.Sim.WarSupply;
using Xunit;

namespace Ravensguard.Sim.Tests.WarSupplyPlay;

/// <summary>P8-WS-2 gate: WS-6 an order as a supply contract, WS-7 robbed in transit, WS-8 late pay, WS-9 the steward and commission, WS-10 leftovers to their source; and the physical arms trade and the save.</summary>
public class ArmsInPlayTests
{
    private static ContentDatabase Db => EconFixture.Content;
    private static readonly EntityId Fief = EntityId.Make(IdKind.Faction, 900), Robber = EntityId.Make(IdKind.Person, 777);
    private const long Key = 5151;

    private sealed class Rig
    {
        public EconomyState Econ = EconFixture.NewEconomy();
        public IdAllocator Ids = new();
        public WorksBook Works = new();
        public Armouries Arm; public ArmsBook Book = new(); public ArmsWorld Arms; public ArmsSupply Supply; public ContractWorld Cw; public ContractBook Cb = new(); public Chronicle Chron;
        public PurseId Lord; public ArmsDepot Depot;
        public List<ArmsMaker> Makers = [];
        public ushort Bar, Planks;
        public long CoinAtStart;

        public Rig(long treasury = 2_000_000)
        {
            var rules = Db.All<LargeBuildingRulesDef>().First();
            Arm = new Armouries(Works, Econ.Items, Econ.Goods, Db, rules);
            Arms = new ArmsWorld(Book, Db, Econ.Items, Arm, Econ.Goods, EconFixture.Seed);
            var (tw, chron) = TalkKit.Town(20, 11); Chron = chron;
            Cw = new ContractWorld
            {
                Chronicle = chron, Ids = Ids, Ledger = Econ.Ledger, People = tw.People, SettlementNames = ["Ashby", "Brook"], SettlementIds = [EntityId.Make(IdKind.Settlement, 1), EntityId.Make(IdKind.Settlement, 2)],
                SettlementFief = [0, 0], FiefIds = [Fief], Seed = 11, Content = ContractContent.From(Db), DistanceM = (a, b) => a == b ? 0 : 60_000,
            };
            Supply = new ArmsSupply(Econ, Arms, Arm, Cw, Cb) { Makers = () => Makers };
            Lord = Econ.Ledger.Open(PurseKind.Treasury, Fief); Econ.Ledger.Mint(Lord, new Coin(treasury), SourceReason.Seed);
            Depot = new ArmsDepot(Fief, Lord, 0, 0, Key);
            Bar = Econ.Goods.IdOf("iron_bar"); Planks = Econ.Goods.IdOf("timber_planks");
            CoinAtStart = Econ.Ledger.Audit().InPurses.Mites;
        }

        public ArmsMaker Maker(int n, int realm = 0, int settlement = 0, long barKg = 200, long plankKg = 100, long coin = 1_000, int minutes = 3840)
        {
            var owner = EntityId.Make(IdKind.Other, (ulong)(0x500 + n)); var purse = Econ.Ledger.Open(PurseKind.Business, owner);
            if (coin > 0) { Econ.Ledger.Mint(purse, new Coin(coin), SourceReason.Seed); CoinAtStart += coin; }
            var store = Econ.OpenStore(StoreKind.Chest, owner, 0, 0, enforce: false);
            if (barKg > 0) store.Create(Bar, barKg * 1000, CreateReason.Crafted);
            if (plankKg > 0) store.Create(Planks, plankKg * 1000, CreateReason.Crafted);
            var m = new ArmsMaker(owner, realm, settlement, purse, store, minutes); Makers.Add(m); return m;
        }

        public static readonly Dictionary<string, int> Want = new() { ["spear"] = 50, ["kettle_hat"] = 20 };
        public long Balance(PurseId p) => Econ.Ledger.Balance(p).Mites;
        public WorkContract Contract(int id) => Cb.Get(id)!;
        public void Run(int from, int to) { for (int d = from; d <= to; d++) Supply.Day(d); }
        public bool CoinExact() => Econ.Ledger.Audit().Imbalance.Mites == 0 && Econ.Ledger.Audit().InPurses.Mites == CoinAtStart;
        public int Stocked => Arm.Stock(Key).Count;
    }

    /// <summary>WS-6: a season's order is offered, taken, made and delivered; the contract pays from escrow on delivery; coin and mass exact.</summary>
    [Fact]
    public void WS6_OrderAsContract_OfferedTakenMadeAndDelivered_CoinAndMassExact()
    {
        var r = new Rig(); var m = r.Maker(1);
        var o = r.Supply.PlaceOrder(r.Depot, 960, 1_000_000, 100, Rig.Want);
        Assert.Equal(52_000, o.Mites); Assert.Equal(50, o.Ordered["spear"]); Assert.Equal(20, o.Ordered["kettle_hat"]);
        var c = Assert.Single(r.Cb.Contracts.Values); Assert.Equal("arms_supply", c.Kind);
        Assert.Equal(WorkState.Taken, c.State); Assert.Equal(m.Owner, c.Taker);                                              // offered to the maker of the realm and taken
        Assert.Equal(52_000, r.Balance(new PurseId(c.Escrow))); Assert.Equal(2_000_000 - 52_000, r.Balance(r.Lord));         // the price is in escrow, not the lord's
        long barBefore = m.Store.Qty(r.Bar);
        r.Run(100, 101);                                                                                                       // the maker's hands start: bar and planks leave the store as items
        Assert.Equal(barBefore - 50 * 400 - 20 * 1500, m.Store.Qty(r.Bar)); Assert.Equal(0, r.Stocked);
        Assert.True(r.Supply.InTransitG() > 0); Assert.True(r.Econ.MassAuditHolds());
        r.Run(102, 130);
        Assert.Equal(WorkState.Done, c.State); Assert.Equal(70, r.Stocked);
        Assert.Equal(1_000 + 52_000, r.Balance(m.Purse)); Assert.Equal(0, r.Balance(new PurseId(c.Escrow))); Assert.Equal(2_000_000 - 52_000, r.Balance(r.Lord));
        Assert.Equal(0, r.Supply.InTransitG()); Assert.Equal(70, r.Book.SupplyStats[ArmsSupply.StatDelivered]);
        Assert.True(r.CoinExact()); Assert.True(r.Econ.MassAuditHolds());
        // every kilogram made is a kilogram the maker's store lost: items hold exactly the bar and planks destroyed as Materialised
        long iron = 0, wood = 0; foreach (int row in r.Econ.Items.LiveRows()) foreach (var comp in r.Econ.Items.ComponentsOf(row)) { if (comp.Material == "iron") iron += comp.MassG; else wood += comp.MassG; }
        Assert.Equal(iron, r.Econ.Mass.Destroyed(r.Bar, DestroyReason.Materialised)); Assert.Equal(wood, r.Econ.Mass.Destroyed(r.Planks, DestroyReason.Materialised));
        Assert.Contains(r.Chron.All, e => e.TypeId == "arms_delivered"); Assert.Contains(r.Chron.All, e => e.TypeId == "arms_ordered");
        Assert.Empty(r.Book.Jobs);
    }

    /// <summary>The offer goes to the lord's realm first, then abroad; an export ban stops the contract (a banned maker is not offered it, and a made load waits at the gate).</summary>
    [Fact]
    public void WS6_Offer_RealmFirstThenAbroad_AndTheBanStopsTheContract()
    {
        var r = new Rig(); var abroad = r.Maker(1, realm: 1); var home = r.Maker(2, realm: 0);
        r.Supply.PlaceOrder(r.Depot, 960, 1_000_000, 100, Rig.Want);
        Assert.Equal(home.Owner, r.Cb.Contracts.Values.Single().Taker);                                                      // the realm's maker before the foreign one
        var r2 = new Rig(); var far = r2.Maker(1, realm: 1);
        r2.Supply.PlaceOrder(r2.Depot, 960, 1_000_000, 100, Rig.Want);
        Assert.Equal(far.Owner, r2.Cb.Contracts.Values.Single().Taker);                                                      // then abroad
        var r3 = new Rig(); r3.Maker(1, realm: 1); r3.Book.Bans.Add(ArmsBook.BanKey(1, 0));
        r3.Supply.PlaceOrder(r3.Depot, 960, 1_000_000, 100, Rig.Want);
        var c3 = r3.Cb.Contracts.Values.Single(); Assert.Equal(WorkState.Open, c3.State); Assert.Equal(1, r3.Book.SupplyStats[ArmsSupply.StatBanned]);
        r3.Run(101, c3.DeadlineDay);                                                                                         // nobody may take it: it lapses and the escrow returns
        Assert.Equal(WorkState.Expired, c3.State); Assert.Equal(2_000_000, r3.Balance(r3.Lord)); Assert.True(r3.CoinExact());
        // a ban that falls after the kit is made holds the load at the gate, and the road opens when it lifts
        var r4 = new Rig(); r4.Maker(1, realm: 1); r4.Supply.PlaceOrder(r4.Depot, 960, 1_000_000, 100, Rig.Want);
        r4.Book.Bans.Add(ArmsBook.BanKey(1, 0)); r4.Run(100, 110);
        Assert.Equal(0, r4.Stocked);
        r4.Book.Bans.Clear(); r4.Run(111, 140); Assert.Equal(70, r4.Stocked); Assert.True(r4.CoinExact());
    }

    /// <summary>WS-7: a cart of kit robbed on the road: the kit is the robbers', nothing is paid for it, the order is short and the rest of the escrow goes back; mass and coin exact.</summary>
    [Fact]
    public void WS7_RobbedInTransit_TheKitIsTheRobbers_TheOrderIsShort()
    {
        var r = new Rig(); var m = r.Maker(1, barKg: 16, plankKg: 60);                                                         // bar for the 40 spears and no more: no hat can be made
        r.Supply.PlaceOrder(r.Depot, 960, 1_000_000, 100, new Dictionary<string, int> { ["spear"] = 40, ["kettle_hat"] = 20 });
        var c = r.Cb.Contracts.Values.Single(); long price = c.PayMites; Assert.Equal(48_000, price);
        r.Run(100, 101);                                                                                                       // made, and on the road to the armoury
        var load = Assert.Single(r.Supply.LoadsOf(c.Id), t => t.State == ArmsSupply.OnRoad); Assert.Equal(0, m.Store.Qty(r.Bar));
        long grams = r.Supply.InTransitG(); Assert.True(grams > 0);
        long took = r.Supply.Rob(load.Id, Robber, null, 101);
        Assert.Equal(40, took); Assert.Equal(0, r.Supply.InTransitG());
        var robbed = r.Econ.Items.LiveRows().Where(i => r.Econ.Items.At(i).Owner == Robber).ToList(); Assert.Equal(40, robbed.Count); Assert.All(robbed, i => Assert.Equal("spear", r.Econ.Items.At(i).Shape));
        Assert.Equal(0, r.Stocked); Assert.Equal(40, r.Book.SupplyStats[ArmsSupply.StatRobbed]);
        Assert.Contains(r.Chron.All, e => e.TypeId == "arms_robbed");
        r.Run(102, c.DeadlineDay + 80);                                                                                        // the maker has no bar left for hats: the order lapses short
        Assert.Equal(WorkState.Failed, c.State); Assert.Equal(0, r.Balance(new PurseId(c.Escrow))); Assert.Equal(2_000_000, r.Balance(r.Lord)); Assert.Equal(1_000, r.Balance(m.Purse));
        Assert.True(r.Book.SupplyStats[ArmsSupply.StatShort] > 0); Assert.True(r.CoinExact()); Assert.True(r.Econ.MassAuditHolds());
    }

    /// <summary>An order made in two batches (the spears, then the hats once the maker has bar): each batch is paid as it arrives, on time, and the contract closes when the last piece is in.</summary>
    [Fact]
    public void WS7_TwoBatches_EachPaidAsItArrives()
    {
        var r = new Rig(); var m = r.Maker(1, barKg: 16, plankKg: 60);
        r.Supply.PlaceOrder(r.Depot, 960, 1_000_000, 100, new Dictionary<string, int> { ["spear"] = 40, ["kettle_hat"] = 20 });
        var c = r.Cb.Contracts.Values.Single();
        r.Run(100, 108);
        Assert.Equal(WorkState.Taken, c.State); Assert.Equal(40, r.Stocked); Assert.Equal(1_000 + 16_000, r.Balance(m.Purse)); Assert.Equal(32_000, r.Balance(new PurseId(c.Escrow)));
        m.Store.Create(r.Bar, 30_000, CreateReason.Crafted);                                                                   // the bar comes in
        r.Run(109, 128);
        Assert.Equal(WorkState.Done, c.State); Assert.Equal(60, r.Stocked); Assert.Equal(1_000 + 16_000 + 32_000, r.Balance(m.Purse)); Assert.Equal(2_000_000 - 48_000, r.Balance(r.Lord));
        Assert.True(r.CoinExact());
    }

    private static readonly (int Realm, bool AtWar)[] Realms = [(0, false), (1, true)];

    /// <summary>Runs the trade's seasons from <paramref name="day"/> until <paramref name="done"/> says so (a caravan's draw is a chance), and returns the result and its day.</summary>
    private static (ArmsTradeResult Res, int Day) Seasons(Rig t, int day, Func<int, int, long, bool> pay, Func<ArmsTradeResult, bool> done)
    {
        for (int i = 0; i < 60; i++, day += TimeConst.DaysPerSeason) { var res = t.Supply.TradeSeason(day, Realms, pay); if (done(res)) return (res, day); }
        throw new InvalidOperationException("no caravan drawn in 60 seasons");
    }

    private static (ArmsRealmDepot From, ArmsRealmDepot To, PurseId PayerFrom, PurseId PayerTo) TwoDepots(Rig t)
    {
        var from = new ArmsRealmDepot(0, EntityId.Make(IdKind.Other, 0x601), t.Econ.OpenStore(StoreKind.Chest, EntityId.Make(IdKind.Other, 0x601), 0, 0, enforce: false), 0);
        var to = new ArmsRealmDepot(1, EntityId.Make(IdKind.Other, 0x602), t.Econ.OpenStore(StoreKind.Chest, EntityId.Make(IdKind.Other, 0x602), 0, 0, enforce: false), 1);
        from.Store.Create(t.Bar, 30_000_000, CreateReason.Crafted); t.Supply.RealmDepot = realm => realm == 0 ? from : to;
        var pf = t.Econ.Ledger.Open(PurseKind.Treasury, Ravensguard.Sim.Land.LandWorld.RealmTreasuryId(0)); var pt = t.Econ.Ledger.Open(PurseKind.Treasury, Ravensguard.Sim.Land.LandWorld.RealmTreasuryId(1));
        t.Econ.Ledger.Mint(pt, new Coin(20_000_000), SourceReason.Seed); t.CoinAtStart = t.Econ.Ledger.Audit().InPurses.Mites;
        return (from, to, pf, pt);
    }

    /// <summary>WS-7 (the trade): a cart of bar iron on the road is real goods in the mass ledger; robbed, it is the robbers' goods and never arrives.</summary>
    [Fact]
    public void WS7_ATradeCartRobbed_IsTheRobbersGoods_InTheMassLedger()
    {
        var t = new Rig(); var (from, to, pf, pt) = TwoDepots(t);
        var (res, day) = Seasons(t, 100, (e, i, m) => t.Econ.Ledger.Transfer(pt, pf, new Coin(m), TransferReason.Trade), x => x.Shipments.Count > 0);
        var ship = res.Shipments[0]; Assert.True(ship.Grams > 0);
        Assert.Equal(30_000_000 - ship.Grams, from.Store.Qty(t.Bar)); Assert.Equal(0, to.Store.Qty(t.Bar));                  // the bar left the depot and is on the road
        var cart = Assert.Single(t.Book.Transit.Values); Assert.Equal(ship.Grams, t.Supply.InTransitG()); Assert.True(t.Econ.MassAuditHolds());
        var gang = t.Econ.OpenStore(StoreKind.Chest, Robber, 0, 0, enforce: false);
        Assert.Equal(ship.Grams, t.Supply.Rob(cart.Id, Robber, gang, day + 1)); Assert.Equal(ship.Grams, gang.Qty(t.Bar)); Assert.Equal(0, t.Supply.InTransitG());
        t.Supply.Day(day + 10); Assert.Equal(0, to.Store.Qty(t.Bar)); Assert.True(t.Econ.MassAuditHolds()); Assert.True(t.CoinExact());
    }

    /// <summary>The arms trade's caravans arrive: the importer's depot gains the bar the exporter's lost, a ban stops the caravan before anything is loaded, and a realm beyond the ledger imports and exports through the mass ledger.</summary>
    [Fact]
    public void ArmsTrade_MovesRealBar_TheBanStopsTheCaravan_AndAnOutsideRealmUsesTheMassLedger()
    {
        var t = new Rig(); var (from, to, pf, pt) = TwoDepots(t);
        bool Pay(int e, int i, long m) => t.Econ.Ledger.Transfer(pt, pf, new Coin(m), TransferReason.Trade);
        t.Book.Bans.Add(ArmsBook.BanKey(0, 1));
        var (banned, _) = Seasons(t, 100, Pay, x => x.Stopped > 0); Assert.Empty(banned.Shipments); Assert.Equal(30_000_000, from.Store.Qty(t.Bar)); Assert.Empty(t.Book.Transit);
        t.Book.Bans.Clear();
        var (ran, day) = Seasons(t, 1000, Pay, x => x.Shipments.Count > 0); var ship = ran.Shipments[0];
        t.Supply.Day(day + 1); Assert.Equal(0, to.Store.Qty(t.Bar)); Assert.Single(t.Book.Transit);                          // 60 km at 20 km a day: still on the road
        t.Supply.Day(day + 10); Assert.Equal(ship.Grams, to.Store.Qty(t.Bar)); Assert.Empty(t.Book.Transit); Assert.Equal(30_000_000 - ship.Grams, from.Store.Qty(t.Bar));
        Assert.True(t.Econ.MassAuditHolds()); Assert.True(t.CoinExact());
        // an exporter beyond the ledger: the bar is imported into the transit store, and an importer beyond it exports it on arrival
        var o = new Rig(); var depot = new ArmsRealmDepot(1, EntityId.Make(IdKind.Other, 0x603), o.Econ.OpenStore(StoreKind.Chest, EntityId.Make(IdKind.Other, 0x603), 0, 0, enforce: false), 1);
        o.Supply.RealmDepot = realm => realm == 1 ? depot : null;
        var buyer = o.Econ.Ledger.Open(PurseKind.Treasury, Ravensguard.Sim.Land.LandWorld.RealmTreasuryId(1)); o.Econ.Ledger.Mint(buyer, new Coin(20_000_000), SourceReason.Seed);
        var (_, d2) = Seasons(o, 100, (e, i, m) => o.Econ.Ledger.Sink(buyer, new Coin(m), SinkReason.Emigrate), x => x.Shipments.Count > 0);
        long made = o.Econ.Mass.Created(o.Bar, CreateReason.Import); Assert.True(made > 0); Assert.True(o.Econ.MassAuditHolds());
        o.Supply.Day(d2 + 10); Assert.Equal(made, depot.Store.Qty(o.Bar)); Assert.True(o.Econ.MassAuditHolds());
    }

    /// <summary>WS-8: three weeks late pays 70%, eight weeks pays 50% (the floor); the rest of the escrow goes back to the lord.</summary>
    [Fact]
    public void WS8_LatePay_3WeeksPays70_8WeeksPays50()
    {
        var r = new Rig(); Assert.Equal(1000, r.Arms.LatePm(0)); Assert.Equal(1000, r.Arms.LatePm(6)); Assert.Equal(900, r.Arms.LatePm(7)); Assert.Equal(700, r.Arms.LatePm(21)); Assert.Equal(500, r.Arms.LatePm(35)); Assert.Equal(500, r.Arms.LatePm(56));
        foreach (var (late, pm) in new[] { (21, 700), (56, 500) })
        {
            var q = new Rig(); var m = q.Maker(1);
            q.Supply.PlaceOrder(q.Depot, 960, 1_000_000, 100, Rig.Want);
            var c = q.Cb.Contracts.Values.Single(); int due = c.DeadlineDay;
            // the road: 60 km at 20 km a day; hold the whole kit in the maker's hands until the order is `late` days past due, then let it go
            q.Run(100, 101);
            foreach (var t in q.Book.Transit.Values) t.Arrive = due + late;                                                   // the cart is late: it reaches the armoury `late` days past the due day
            q.Run(102, due + late + 1);
            Assert.Equal(WorkState.Done, c.State);
            Assert.Equal(1_000 + 50 * 400L * pm / 1000 + 20 * 1600L * pm / 1000, q.Balance(m.Purse));
            Assert.Equal(2_000_000 - (50 * 400L * pm / 1000 + 20 * 1600L * pm / 1000), q.Balance(q.Lord));                  // what is cut comes back to the lord
            Assert.True(q.CoinExact()); Assert.True(q.Book.SupplyStats[ArmsSupply.StatLateCut] > 0);
        }
    }

    /// <summary>WS-9: the steward's order stays inside his season's budget and the gaps; a second call in the same season places nothing, the next season only the gaps still open.</summary>
    [Fact]
    public void WS9_StewardOrder_StaysWithinBudget_OncePerSeason_AgainstTheGaps()
    {
        var r = new Rig(); r.Maker(1);
        for (int i = 0; i < 10; i++) r.Arm.Add(Key, new ArmouryCapacity(600, 600, 600, 60, 600, 6000, 2400, 6000, 3600, 2400),
            r.Econ.Items.Add(r.Ids, "spear", ItemQuality.Common, Armouries.HolderOf(Key), [new ItemComponent("iron", 400, 1000)], conditionPermille: 1000));      // ten spears already stocked
        r.Supply.HireSteward(Fief.Value, Key, 30_000, 100);
        var placed = r.Supply.StewardSeason(100, _ => r.Depot, _ => new Dictionary<string, int> { ["spear"] = 60, ["kettle_hat"] = 30 });
        var (_, res) = Assert.Single(placed);
        Assert.True(res.Mites <= 30_000); Assert.Equal(30_000, res.BudgetMites);
        Assert.Equal(50, res.Gaps["spear"]);                                                                                   // 60 wanted, 10 in the armoury
        Assert.Equal(50, res.Ordered["spear"]); Assert.Equal((30_000 - 50 * 400) / 1600, res.Ordered["kettle_hat"]); Assert.True(res.Deferred["kettle_hat"] > 0);
        var c = r.Cb.Contracts.Values.Single(); Assert.Equal(res.Mites, r.Balance(new PurseId(c.Escrow))); Assert.True(r.Balance(new PurseId(c.Escrow)) <= 30_000);
        Assert.Empty(r.Supply.StewardSeason(104, _ => r.Depot));                                                               // the same season: nothing
        var next = r.Supply.StewardSeason(100 + TimeConst.DaysPerSeason, _ => r.Depot, _ => new Dictionary<string, int> { ["spear"] = 60, ["kettle_hat"] = 30 });
        var (_, again) = Assert.Single(next); Assert.True(again.Mites <= 30_000);
        Assert.True(r.Cb.Contracts.Values.Where(x => x.Kind == "arms_supply" && x.PostedDay / TimeConst.DaysPerSeason == (100 + TimeConst.DaysPerSeason) / TimeConst.DaysPerSeason).Sum(x => x.PayMites) <= 30_000);
        Assert.True(r.CoinExact());
    }

    /// <summary>WS-9: a commission names a template and a budget; an armourer buys the missing parts as goods and fits them at the armoury; the gaps close, the supplier is paid for the parts, the rest of the budget comes back.</summary>
    [Fact]
    public void WS9_Commission_FillsTheGapsFromBoughtGoods_AndFitsThemAtTheArmoury()
    {
        var r = new Rig(); var armourer = r.Maker(1, barKg: 0, plankKg: 0, coin: 500);
        var shop = EntityId.Make(IdKind.Other, 0x700); var shopStore = r.Econ.OpenStore(StoreKind.Chest, shop, 0, 0, enforce: false); var shopPurse = r.Econ.Ledger.Open(PurseKind.Business, shop);
        shopStore.Create(r.Bar, 100_000, CreateReason.Crafted); shopStore.Create(r.Planks, 50_000, CreateReason.Crafted);
        r.Supply.Supplier = _ => (shopStore, shopPurse); r.CoinAtStart = r.Econ.Ledger.Audit().InPurses.Mites;
        var o = r.Supply.Commission(r.Depot, new Dictionary<string, int> { ["spear"] = 10, ["kettle_hat"] = 5 }, 20_000, 100);
        Assert.Equal(12_000, o.Mites); Assert.Equal(10, o.Ordered["spear"]); Assert.Equal(5, o.Ordered["kettle_hat"]);
        var c = r.Cb.Contracts.Values.Single(); Assert.Equal("refit_commission", c.Kind); Assert.Equal(armourer.Owner, c.Taker); Assert.Equal(12_000, r.Balance(new PurseId(c.Escrow)));
        r.Run(100, 101);
        Assert.True(shopStore.Qty(r.Bar) < 100_000); Assert.True(r.Balance(shopPurse) > 0);                                  // the parts were bought: goods moved, coin to the supplier
        Assert.True(r.Econ.MassAuditHolds()); Assert.True(r.CoinExact());
        r.Run(102, 120);
        Assert.Equal(WorkState.Done, c.State); Assert.Equal(15, r.Stocked); Assert.Empty(r.Arm.Gaps(Key, new Dictionary<string, int> { ["spear"] = 10, ["kettle_hat"] = 5 }));
        Assert.Equal(0, r.Balance(new PurseId(c.Escrow))); Assert.True(r.Balance(armourer.Purse) > 500);                      // the armourer's labour was paid
        long spent = 2_000_000 - r.Balance(r.Lord); Assert.InRange(spent, 1, 12_000);
        Assert.Equal(spent, r.Balance(shopPurse) + (r.Balance(armourer.Purse) - 500));                                         // the lord paid only what the parts and the hours cost; the rest came back
        Assert.True(r.CoinExact()); Assert.True(r.Econ.MassAuditHolds());
        Assert.Contains(r.Chron.All, e => e.TypeId == "arms_fitted");
    }

    /// <summary>The books of a half-way order, a robbed load, a steward: saved, loaded and saved again, byte for byte, and the hash equal.</summary>
    [Fact]
    public void Save_RoundTrip_IsByteIdentical_AndAnEmptyBookAddsNoKeys()
    {
        var r = new Rig(); r.Maker(1);
        r.Supply.HireSteward(Fief.Value, Key, 30_000, 100);
        r.Supply.PlaceOrder(r.Depot, 960, 1_000_000, 100, Rig.Want); r.Run(100, 101);
        Assert.NotEmpty(r.Book.Jobs); Assert.NotEmpty(r.Book.Transit);
        var save = ArmsSave.Capture(r.Book); var bytes = MessagePackSerializer.Serialize(save);
        var back = new ArmsBook(); MessagePackSerializer.Deserialize<ArmsSave>(bytes).Restore(back);
        Assert.Equal(bytes, MessagePackSerializer.Serialize(ArmsSave.Capture(back)));
        Assert.Equal(Fold(r.Book), Fold(back));
        var empty = ArmsSave.Capture(new ArmsBook()); Assert.Null(empty.Jobs); Assert.Null(empty.Transit); Assert.Null(empty.Stewards); Assert.Null(empty.SupplyExtra); Assert.Null(empty.JobShapes);
        Assert.True(new ArmsBook().IsEmpty);
        Assert.Throws<InvalidDataException>(() => new ArmsSave { Jobs = [[1, 0, 0, 0, 0, 0, 0, 0, -1, 1, 5, 1, 1, 0, 0, 1]], JobShapes = ["spear"] }.Restore(new ArmsBook()));          // a shape index off the table
    }

    private static string Fold(ArmsBook b) { using var h = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256); b.Fold(h); return Convert.ToHexString(h.GetHashAndReset()); }

    /// <summary>WS-10: Disband gives the unspent coin back to the purses that funded the chest, newest first and each up to what it put in (a Subsidy transfer), not to the realm treasury; coin exact.</summary>
    [Fact]
    public void WS10_Disband_ReturnsLeftoversToTheirSourcePurses_CoinExact()
    {
        using var run = AuditWorld.Create(links: false);
        var h = AuditWorld.Host(run, 7, new Roster(), 0);
        var hs = run.Supply; var led = run.Econ.Ledger; var chest = hs.ChestOf(h);
        var lord = led.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 4040)); var loan = led.Open(PurseKind.Treasury, EntityId.Make(IdKind.Faction, 4041)); var other = led.Open(PurseKind.Chest, EntityId.Make(IdKind.Other, 4042));
        led.Mint(lord, new Coin(60_000), SourceReason.Seed); led.Mint(loan, new Coin(40_000), SourceReason.Seed);
        long total = led.Audit().InPurses.Mites;
        Assert.True(hs.Deposit(h, lord, 60_000)); Assert.True(hs.Deposit(h, loan, 40_000));
        Assert.Equal(100_000, led.Balance(chest).Mites);
        Assert.True(led.Transfer(chest, other, new Coin(30_000), TransferReason.Wage));                                // the host spent 30,000 on the way
        hs.Disband(h);
        Assert.Equal(0, led.Balance(chest).Mites);
        Assert.Equal(40_000, led.Balance(loan).Mites);                                                                          // newest first, up to what it put in
        Assert.Equal(30_000, led.Balance(lord).Mites);                                                                          // the rest is what the first purse had left to take back
        Assert.Equal(30_000, led.Balance(other).Mites); Assert.Equal(total, led.Audit().InPurses.Mites); Assert.Equal(0, led.Audit().Imbalance.Mites);
    }
}
