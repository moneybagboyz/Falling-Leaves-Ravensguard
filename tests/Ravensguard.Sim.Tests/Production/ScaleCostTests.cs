using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Construction;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>P7-ES2 gate tests (economies-of-scale 10): ES-4, ES-5, ES-8 to ES-11, ES-14 to ES-16, and the save section's new rows.</summary>
public class ScaleCostTests
{
    private static ScaleSettingsDef Ss => ProdKit.Content.Scale;
    private static GameTime At(long hour) => GameTime.FromGameHours(hour);

    /// <summary>Two villages 8 km apart in one fief (the carting leg of the spec's table), a market in each, one household of one adult each; person 2 is the carter of the first.</summary>
    private sealed class Towns
    {
        public EconomyState Econ = null!; public Population Pop = null!; public ProductionSystem Sys = null!; public MarketSystem Markets = null!; public ScaleBook Book = new(); public ScaleWorld Sw = null!;
        public PurseId Treasury; public List<(ulong Who, ulong Of, int Delta)> Opinions = [];
        public PersonTable People => Pop.People;

        public static Towns Make(int households = 6, ulong seed = 42, int distanceM = 8000)
        {
            var t = new Towns();
            var region = new RegionData { Seed = seed };
            for (int s = 0; s < 2; s++)
                region.Settlements.Add(new RegionSettlement { Id = EntityId.Make(IdKind.Settlement, (uint)(s + 1)), Name = "Town" + s, Class = SettlementClass.Village, CenterX = 16000 + s * distanceM, CenterY = 16000, TargetPop = households, FiefIndex = 0 });
            region.Fiefs.Add(new Fief { Id = EntityId.Make(IdKind.Faction, 77), Name = "Fief", Realm = 0, SeatSettlement = 0 });
            region.Fiefs[0].Settlements.AddRange([0, 1]);
            var people = new PersonTable(households, 1); var hh = new HouseholdTable(households, 1);
            for (int i = 0; i < households; i++)
            {
                int h = hh.Add(0, 0, DwellingType.Cottage, 6); hh.Class[h] = SocialClass.Peasant;
                int p = people.Add(); people.Household[p] = h; people.BirthDay[p] = -30 * TimeConst.DaysPerYear; people.HomeSettlement[p] = 0; hh.AddMember(h, p); hh.Head[h] = p;
            }
            t.Pop = new Population
            {
                Region = region, People = people, Households = hh, NowDay = 0, LotHousehold = [[], []], LotBeds = [[], []], LotKeeper = [[], []], FiefLord = [], FiefLordHousehold = [],
                ParcelHolder = [], PriestHousehold = [-1, -1], SettlementPeople = [households, 0],
            };
            t.Econ = EconomyState.FromContent(EconFixture.Content, hh.Count, people.Count);
            t.Econ.OpenHouseholds(hh);
            t.Treasury = t.Econ.Ledger.Open(PurseKind.Treasury, region.Fiefs[0].Id);
            t.Markets = new MarketSystem(t.Econ, t.Pop, seed);
            t.Sys = new ProductionSystem(t.Econ, t.Pop, ProdKit.Content, seed, t.Markets, null);
            t.Sw = new ScaleWorld(t.Book, t.Sys, EconFixture.Content, 99) { AddOpinion = (w, o, d) => t.Opinions.Add((w, o, d)) };
            return t;
        }

        public Business Make(string kind, int settlement, int owner, long cash, Occupation occ = Occupation.Smith, Skill skill = Skill.Smithing, int level = 50)
        {
            var k = Sys.Content.Kind(kind);
            People.Occupation[owner] = occ; People.SetSkill(owner, 0, skill, level);
            var b = Sys.NewBusiness(k, settlement, 0, owner); b.OwnerWorks = true;
            foreach (var (sk, count) in k.Stations) for (int c = 0; c < count; c++) b.Stations.Add(new Station { Kind = sk });
            if (cash > 0) Econ.Ledger.Mint(b.Purse, new Coin(cash), SourceReason.Seed);
            return b;
        }
        public void Give(Business b, string good, long qty) => b.Store.Create(Econ.Goods.IdOf(good), qty, CreateReason.Seed);
        public ushort G(string good) => Econ.Goods.IdOf(good);
        public long Cash(Business b) => Econ.Ledger.Balance(b.Purse).Mites;
    }

    // ---------------------------------------------------------------- ES-4: shared heat

    [Fact]
    public void ES4_Pure_TwoBarsSideBySide_BurnTwelveKilos_SixEach()
    {
        long fixedG = FixedCosts.HeatFixedG(10_000, 1000, 8);
        Assert.Equal(8_000, fixedG);                                                         // 8 lit hours x 1 kg
        long second = 10_000 - FixedCosts.SharedDiscountG(fixedG, 8 * 60, 8 * 60);
        Assert.Equal(2_000, second);                                                         // the second bar burns only its own 2 kg
        Assert.Equal(6_000, (10_000 + second) / 2);                                          // (8 + 2 + 2) / 2 kg each
        Assert.Equal(10_000, 10_000 - FixedCosts.SharedDiscountG(fixedG, 8 * 60, 0));        // one alone: 10 kg
        Assert.Equal(10_000 - 8_000 * 120 / 480, 10_000 - FixedCosts.SharedDiscountG(fixedG, 8 * 60, 120));   // a mate with 2 h left saves a quarter of the lit hours
    }

    [Fact]
    public void ES4_TwoForges_OneBusiness_TheSecondBatchBurnsOnlyItsVariableFuel()
    {
        var k = new ProdKit(households: 2);
        var b = k.Make("smithy", 100_000, 1, Occupation.Smith, Skill.Smithing, 50);
        b.Stations.Add(new Station { Kind = "forge" });                                       // a second forge beside the smithy's one
        k.Give(b, "iron_bloom", 12_000); k.Give(b, "charcoal", 30_000);
        var forges = b.Stations.Where(s => s.Kind == "forge").ToList(); Assert.Equal(2, forges.Count);
        long c0 = k.Have(b, "charcoal");
        Assert.True(k.Sys.TryStart(b, forges[0], k.R("forge_bar"), At(6)));
        Assert.Equal(10_000, c0 - k.Have(b, "charcoal"));                                     // the first lights the hearth: 10 kg
        long c1 = k.Have(b, "charcoal");
        Assert.True(k.Sys.TryStart(b, forges[1], k.R("forge_bar"), At(6)));
        Assert.Equal(2_000, c1 - k.Have(b, "charcoal"));                                      // the second: 2 kg
        Assert.True(k.Econ.MassAuditHolds());
    }

    [Fact]
    public void ES4_AForgeAlone_BurnsTheRecipesWholeFuel()
    {
        var k = new ProdKit(households: 2);
        var b = k.Make("smithy", 100_000, 1, Occupation.Smith, Skill.Smithing, 50);
        k.Give(b, "iron_bloom", 6_000); k.Give(b, "charcoal", 30_000);
        var forge = b.Stations.First(s => s.Kind == "forge");
        Assert.True(k.Sys.TryStart(b, forge, k.R("forge_bar"), At(6)));
        Assert.Equal(20_000, k.Have(b, "charcoal"));
    }

    // ---------------------------------------------------------------- ES-5: firings

    [Fact]
    public void ES5_ScotchBrickKiln_FullLoadAndATenTonneLoad()
    {
        // fixed 4,000 kg of firewood, 0.3 kg a kg of load, 60 t capacity
        long full = FixedCosts.FiringFuelG(4_000_000, 300, 60_000_000);
        long ten = FixedCosts.FiringFuelG(4_000_000, 300, 10_000_000);
        Assert.Equal(22_000_000, full); Assert.Equal(7_000_000, ten);
        Assert.Equal(366, FixedCosts.FuelPerKgPm(full, 60_000_000)); Assert.Equal(700, FixedCosts.FuelPerKgPm(ten, 10_000_000));   // 0.37 and 0.70 a kg
    }

    [Fact]
    public void ES5_AFiringBurnsItsFixedPart_HoweverSmallTheLoad()
    {
        var k = new ProdKit(households: 2);
        var b = k.Make("masonry", 100_000, 1, Occupation.Mason, Skill.Masonry, 50);
        var clamp = b.Stations.First(s => s.Kind == "brick_clamp");
        k.Give(b, "clay", 25_000); k.Give(b, "firewood", 40_000);
        // a quarter load of fire_bricks (100 kg clay, 40 kg firewood a full batch): the fixed 18% of the firewood (7.2 kg) plus a quarter of the rest
        Assert.True(k.Sys.TryStart(b, clamp, k.R("fire_bricks"), At(6)));
        Assert.Equal(250, clamp.ScalePermille);
        Assert.Equal(7_200 + (32_800 * 250 + 999) / 1000, 40_000 - k.Have(b, "firewood"));
        // below the fixed part there is no firing at all
        var k2 = new ProdKit(households: 2);
        var b2 = k2.Make("masonry", 100_000, 1, Occupation.Mason, Skill.Masonry, 50);
        k2.Give(b2, "clay", 100_000); k2.Give(b2, "firewood", 7_000);
        Assert.False(k2.Sys.TryStart(b2, b2.Stations.First(s => s.Kind == "brick_clamp"), k2.R("fire_bricks"), At(6)));
        Assert.Equal(7_000, k2.Have(b2, "firewood"));
        // a full batch is Phase 2's rule exactly
        var k3 = new ProdKit(households: 2);
        var b3 = k3.Make("masonry", 100_000, 1, Occupation.Mason, Skill.Masonry, 50);
        k3.Give(b3, "clay", 100_000); k3.Give(b3, "firewood", 100_000);
        Assert.True(k3.Sys.TryStart(b3, b3.Stations.First(s => s.Kind == "brick_clamp"), k3.R("fire_bricks"), At(6)));
        Assert.Equal(40_000, 100_000 - k3.Have(b3, "firewood"));
    }

    // ---------------------------------------------------------------- ES-8, ES-9: supply contracts

    [Fact]
    public void ES8_ASupplyContract_PaysTheMid_NoTollNoSpread_AndMovesRealGoods()
    {
        var t = Towns.Make();
        var seller = t.Make("bloomery", 0, 0, 500_000); var buyer = t.Make("smithy", 0, 1, 500_000);
        t.Give(seller, "charcoal", 5_000_000);
        ushort g = t.G("charcoal"); var book = t.Markets.BookForSettlement(0);
        var r = t.Sw.Offer(seller, buyer, g, 1_000_000, 5, 2, true, 0);
        Assert.True(r.Ok, r.Key);
        var c = Assert.Single(t.Book.Contracts);
        Assert.Equal(book.MidMicro(g), c.PriceMicro);                                         // equal Trading: the seller's book mid itself
        long pay = SupplyTerms.PayMites(1_000_000, c.PriceMicro);
        Assert.InRange(pay - 1_000_000 * book.MidMicro(g) / 1_000_000, 0, 1);
        long ask = SupplyTerms.PayMites(1_000_000, book.AskMicro(g));
        Assert.True(ask > pay);                                                                // the market would charge the ask
        long toll0 = t.Econ.Ledger.Balance(t.Markets.BookForSettlement(0).TollPurse).Mites, sb = t.Cash(seller), bb = t.Cash(buyer);
        long mass0 = seller.Store.Qty(g) + buyer.Store.Qty(g);
        Assert.Equal(1, t.Sw.Daily(1));
        Assert.Equal(sb + pay, t.Cash(seller)); Assert.Equal(bb - pay, t.Cash(buyer));
        Assert.Equal(toll0, t.Econ.Ledger.Balance(t.Markets.BookForSettlement(0).TollPurse).Mites);   // no market toll
        Assert.Equal(1_000_000, buyer.Store.Qty(g)); Assert.Equal(mass0, seller.Store.Qty(g) + buyer.Store.Qty(g));
        Assert.Equal(1, c.Delivered); Assert.Equal(6, c.NextDue);
        Assert.True(t.Econ.Ledger.Audit().IsConserved);
        // nothing falls due again before the 5 days are out
        Assert.Equal(0, t.Sw.Daily(3)); Assert.Equal(1, t.Sw.Daily(6));
    }

    [Fact]
    public void ES8_Terms_AreChecked_AndTradingMovesThePriceWithinTheBand()
    {
        Assert.Equal(12_000, SupplyTerms.PriceMicro(12_000, 40, 40, 50));
        Assert.Equal(12_000 * 1010 / 1000, SupplyTerms.PriceMicro(12_000, 40, 20, 50));          // (40 - 20) / 2 = +10 permille
        Assert.Equal(12_000 * 1050 / 1000, SupplyTerms.PriceMicro(12_000, 100, 0, 50));          // clamped to +50
        Assert.Equal(12_000 * 950 / 1000, SupplyTerms.PriceMicro(12_000, 0, 100, 50));
        var t = Towns.Make();
        var s = t.Make("bloomery", 0, 0, 1_000); var b = t.Make("smithy", 0, 1, 1_000);
        ushort g = t.G("charcoal");
        Assert.Equal("sc_contract_too_small", t.Sw.Offer(s, b, g, 100_000, 5, 1, true, 0).Key);       // under 500 kg
        Assert.Equal("sc_contract_terms", t.Sw.Offer(s, b, g, 600_000, 5, 5, true, 0).Key);            // five seasons
        Assert.Equal("sc_contract_same", t.Sw.Offer(s, s, g, 600_000, 5, 1, true, 0).Key);
        Assert.Empty(t.Book.Contracts);
    }

    [Fact]
    public void ES9_TwoMissedDeliveriesInASeason_BreakTheContract_TheSellerPaysTheCover_OpinionFalls()
    {
        var t = Towns.Make();
        var seller = t.Make("bloomery", 0, 0, 200_000); var buyer = t.Make("smithy", 0, 1, 200_000);
        ushort g = t.G("charcoal"); var book = t.Markets.BookForSettlement(0);
        Assert.True(t.Sw.Offer(seller, buyer, g, 1_000_000, 5, 3, true, 0).Ok);                // the seller has no charcoal at all
        var c = t.Book.Contracts[0];
        long cover = (1_000_000 * Math.Max(0, book.AskMicro(g) - c.PriceMicro) + 500_000) / 1_000_000;
        Assert.True(cover > 0);
        long sb = t.Cash(seller), bb = t.Cash(buyer);
        for (int day = 1; day <= 3; day++) t.Sw.Daily(day);                                    // due day 1: up to two days late is waited for
        Assert.Equal(0, c.Missed);
        t.Sw.Daily(4);                                                                         // three days late: Missed
        Assert.Equal(1, c.Missed); Assert.Equal(SupplyState.Open, c.State);
        Assert.Equal(sb - cover, t.Cash(seller)); Assert.Equal(bb + cover, t.Cash(buyer));     // the buyer's market cover, as Restitution
        for (int day = 5; day <= 9; day++) t.Sw.Daily(day);
        Assert.Equal(2, c.Missed); Assert.Equal(SupplyState.Broken, c.State);
        Assert.Equal(2, t.Opinions.Count); Assert.All(t.Opinions, o => Assert.Equal(-20, o.Delta));
        Assert.True(t.Econ.Ledger.Audit().IsConserved);
        Assert.Equal(0, t.Sw.Daily(10));                                                       // a broken contract delivers nothing more
    }

    [Fact]
    public void ES9_TwoMissesInDifferentSeasons_DoNotBreak_AndTheBuyerAtFaultOwesNoCover()
    {
        var t = Towns.Make();
        var seller = t.Make("bloomery", 0, 0, 20_000); var buyer = t.Make("smithy", 0, 1, 0);
        ushort g = t.G("charcoal");
        t.Give(seller, "charcoal", 3_000_000);
        Assert.True(t.Sw.Offer(seller, buyer, g, 1_000_000, 13, 3, true, 0).Ok);               // the buyer has no coin: his fault
        var c = t.Book.Contracts[0]; long sb = t.Cash(seller);
        for (int day = 1; day <= 30; day++) t.Sw.Daily(day);
        Assert.True(c.Missed >= 1); Assert.Equal(sb, t.Cash(seller));                          // no restitution when the buyer is at fault
        Assert.Equal(3_000_000, seller.Store.Qty(g));
    }

    // ---------------------------------------------------------------- ES-10: carting

    [Fact]
    public void ES10_CartingTable_CartOxWagonAndFourHorseWagon_OverEightKilometres()
    {
        var cart = Ss.Vehicles.First(v => v.Id == "cart"); var ox = Ss.Vehicles.First(v => v.Id == "ox_wagon"); var horses = Ss.Vehicles.First(v => v.Id == "horse_wagon");
        Assert.Equal(321, Carting.DayMites(cart, 12, 30));                                      // 150 + 8 kg x 12 + 2.5 kg x 30
        Assert.Equal(390, Carting.DayMites(ox, 12, 30));                                        // 150 + 240
        Assert.Equal(834, Carting.DayMites(horses, 12, 30));                                    // 150 + 4 horses 684
        long c = Carting.TripMites(36 * 60, 321), o = Carting.TripMites((long)(2.3 * 1440), 390), h = Carting.TripMites(36 * 60, 834);
        Assert.Equal((482, 897, 1251), (c, o, h));
        Assert.Equal(1044, Carting.MilliPerKg(c, 40, 500)); Assert.Equal(937, Carting.MilliPerKg(o, 40, 1000)); Assert.Equal(645, Carting.MilliPerKg(h, 40, 2000));   // 1.04, 0.94, 0.65 a kg
    }

    [Fact]
    public void ES10_Plan_ChoosesTheCheapestConvoy_FlagsAHalfEmptyCart_AndPaysNothingInTown()
    {
        var plan = Carting.Plan(Ss, 8000, 2_000_000, 12, 30, 20, true);
        Assert.Equal("horse_wagon", plan.Vehicle); Assert.Equal(1, plan.Vehicles); Assert.False(plan.HalfEmpty);
        Assert.Equal(Carting.TripMites(Carting.RoundTripMinutes(8000, 450), 834) + 40, plan.TotalMites);      // 2 x 8 km at 450 m an hour, to the minute
        var small = Carting.Plan(Ss, 8000, 200_000, 12, 30, 20, true);                          // 200 kg: well under 60% of anything
        Assert.True(small.HalfEmpty); Assert.Equal("cart", small.Vehicle);
        var town = Carting.Plan(Ss, 0, 2_000_000, 12, 30, 20, false);
        Assert.Equal(0, town.TotalMites);
        Assert.Equal(0, Carting.Plan(Ss, 8000, 0, 12, 30, 20, true).TotalMites);
    }

    [Fact]
    public void ES10_ADeliveryBetweenTowns_PaysTheCarterAndTheGate_ByRealTransfers()
    {
        var t = Towns.Make();
        var seller = t.Make("bloomery", 0, 0, 500_000); var buyer = t.Make("smithy", 1, 1, 500_000);
        t.People.Occupation[2] = Occupation.Labourer;
        t.Sw.CarterOf = (s, d) => t.Pop.Households.Count > 2 ? t.People.Household[2] : -1;
        t.Give(seller, "charcoal", 2_000_000);
        ushort g = t.G("charcoal");
        Assert.True(t.Sw.Offer(seller, buyer, g, 1_000_000, 5, 2, true, 0).Ok);
        var c = t.Book.Contracts[0];
        long pay = SupplyTerms.PayMites(1_000_000, c.PriceMicro);
        var drv = t.Econ.HouseholdPurse[t.People.Household[2]];
        long sb = t.Cash(seller), bb = t.Cash(buyer), carter = t.Econ.Ledger.Balance(drv).Mites, tr = t.Econ.Ledger.Balance(t.Treasury).Mites;
        Assert.Equal(1, t.Sw.Daily(1));
        long toCarter = t.Econ.Ledger.Balance(drv).Mites - carter, toGate = t.Econ.Ledger.Balance(t.Treasury).Mites - tr;
        Assert.True(toCarter > 0);                                                              // crew and team: one cart's day or a wagon's, at the book's feed prices
        Assert.True(toGate > 0 && toGate % (2 * ProdKit.Content.Tax.GateTollMitesPerCart) == 0);   // a toll each way for every vehicle of the convoy
        Assert.Equal(bb - pay, t.Cash(buyer));
        Assert.Equal(sb + pay - toCarter - toGate, t.Cash(seller));                              // the seller is the carter: his the cost, and every mite went somewhere
        Assert.True(t.Econ.Ledger.Audit().IsConserved);
        Assert.Equal(1_000_000, buyer.Store.Qty(g)); Assert.Equal(1_000_000, seller.Store.Qty(g));
    }

    // ---------------------------------------------------------------- ES-11: patterns

    [Fact]
    public void ES11_TwentySpearheads_ThirtyHours_ThenWithPractice_OneThirtyTwoEach()
    {
        var p = Patterns.Of(Ss, "forge_spearheads")!;
        Assert.Equal(1800, Patterns.RunMinutes(p, 20, false, 850));                             // 6 h setup + 20 x 1.2 h = 30 h: 1.5 h a piece
        Assert.Equal(1584, Patterns.RunMinutes(p, 20, true, 850));                              // 6 + 20 x 1.02 = 26.4 h: 1.32 a piece
        Assert.Equal(30, Patterns.FullHours(p));
        Assert.Equal(30_000, Patterns.HoursTimesScale(p, 1000, false, 850));
        Assert.Equal(26_400, Patterns.HoursTimesScale(p, 1000, true, 850));
        Assert.Equal(6_000 + 1_200 * 10, Patterns.HoursTimesScale(p, 500, false, 850));          // ten pieces: the setup is paid whole
        Assert.Equal(ItemQuality.Common, Patterns.Capped(p, ItemQuality.Masterwork)); Assert.Equal(ItemQuality.Poor, Patterns.Capped(p, ItemQuality.Poor));
        Assert.Equal(30, ProdKit.Recipes["forge_spearheads"].WorkerHours);
    }

    [Fact]
    public void ES11_ARunOfSpearheads_IsCappedAtCommon_AndThreeRunsGivePractice()
    {
        var k = new ProdKit(households: 2) { };
        k.Sys.Scale = new ScaleBook();
        var b = k.Make("munition_works", 500_000, 1, Occupation.Smith, Skill.Smithing, 95);       // a master of the trade
        var anvil = b.Stations.First(s => s.Kind == "anvil"); var r = k.R("forge_spearheads");
        Assert.Equal(30_000, k.Sys.HoursTimesScale(b, r, 1000, 0));
        for (int run = 0; run < 3; run++)
        {
            k.Give(b, "iron_bar", 8_000); k.Give(b, "charcoal", 3_000);
            Assert.True(k.Sys.TryStart(b, anvil, r, At(6 + run * 24 * 5)));
            Assert.Equal(30_000, k.Sys.HoursTimesScale(b, r, 1000, run * 5));                  // no practice before the third run is finished
            anvil.ProgressMicro = Station.Complete - 1;                                       // run it to the end
            k.Sys.WorkHour(At(6 + run * 24 * 5));
            Assert.False(anvil.Busy);
            Assert.Equal(20 * (run + 1), k.Have(b, "spearheads"));
        }
        Assert.Equal(0, b.Quality[(int)ItemQuality.Fine] + b.Quality[(int)ItemQuality.Masterwork]);   // a master's runs are never better than common
        Assert.Equal(3, b.Quality.Sum());
        Assert.True(k.Sys.Practised(b, r, 11));
        Assert.Equal(26_400, k.Sys.HoursTimesScale(b, r, 1000, 11));                            // practice: 3 runs in the last 2 seasons
        Assert.False(k.Sys.Practised(b, r, 11 + 2 * TimeConst.DaysPerSeason + 1));             // forgotten after two seasons
        Assert.True(k.Econ.MassAuditHolds());
    }

    [Fact]
    public void ES11_AnOrdinaryRecipe_IsUntouchedByPatternsOrFuelShares()
    {
        var k = new ProdKit(households: 2);
        var b = k.Make("smithy", 100_000, 1, Occupation.Smith, Skill.Smithing, 50);
        var r = k.R("forge_bar"); Assert.Null(r.Pattern);
        Assert.Equal(8_000, k.Sys.HoursTimesScale(b, r, 1000, 0));
        Assert.Equal(4_000, k.Sys.HoursTimesScale(b, r, 500, 0));
    }

    // ---------------------------------------------------------------- ES-14: absorption

    [Fact]
    public void ES14_Absorption_DemandAndTheDeskRoom_AreTheCap()
    {
        Assert.Equal(50_000, Absorption.CapPerDay([(40_000, 0)], 10_000));                       // 40 kg a day of demand and 10 kg of room
        Assert.Equal(20_000 + 10_000, Absorption.CapPerDay([(40_000, 500)], 10_000));            // a rival that serves half of it
        Assert.Equal(0, Absorption.CompetitorsPm(0)); Assert.Equal(500, Absorption.CompetitorsPm(1)); Assert.Equal(750, Absorption.CompetitorsPm(3));
        Assert.True(Absorption.BidBelowCost(1_000, 1_000, 1050)); Assert.False(Absorption.BidBelowCost(1_050, 1_000, 1050));
    }

    [Fact]
    public void ES14_ASmithyMakingNails_IsStoppedPastTheCap_WhenTheBidIsBelowCost()
    {
        var k = new ProdKit(households: 2, markets: true);
        var b = k.Make("smithy", 100_000, 1, Occupation.Smith, Skill.Smithing, 50);
        var book = k.Markets!.BookForSettlement(0); ushort nails = k.Cat.IdOf("nails"); var r = k.R("forge_nails");
        var gb = book.Goods[nails]; gb.StructuralDemand = 40_000; gb.DemandEma = 40_000;
        book.ImportRequests.Add(new ImportRequest(0, nails, 10_000L * gb.TargetCoverDays, 0, b.Id));      // the desk takes 10 kg a day
        var v0 = k.Sys.AbsorbView(b, r, book);
        Assert.Equal(50_000, v0.CapPerDay);                                                        // the planner's cap: 50 kg a day
        Assert.False(k.Sys.AbsorbView(b, r, book, planner: true).Stops);                           // nothing on hand: a first batch is always allowed
        gb.StockUnits = 40_000L * gb.TargetCoverDays * 3;                                          // the town's shelves are full: the price is on the floor
        k.Give(b, "nails", 200_000);                                                               // and the smithy holds four days of the cap unsold
        var v1 = k.Sys.AbsorbView(b, r, book);
        Assert.True(v1.Held >= 3 * v1.CapPerDay); Assert.True(v1.BidMicro < v1.VarCostMicro * 1050 / 1000);
        Assert.True(v1.Stops);
        // under the cap the same shelves do not stop it
        var b2 = k.Make("smithy", 100_000, 1, Occupation.Smith, Skill.Smithing, 50);
        k.Give(b2, "nails", 20_000);
        Assert.False(k.Sys.AbsorbView(b2, r, book, planner: true).Stops);
    }

    // ---------------------------------------------------------------- ES-15: machines

    private const int WheelX = 5150, WheelY = 5003;

    private static WaterSites StreamSites(WaterPowerRulesDef rules)
    {
        var r = new RegionData { Seed = 1 };
        int ty = 5000 / RegionData.TileM;
        for (int tx = 0; tx < RegionData.Size; tx++) r.HeightCm[ty * RegionData.Size + tx] = Math.Max(0, 30000 - (tx - 60) * 427);
        var pts = new List<PointM>(); for (int k = 60; k <= 80; k++) pts.Add(new PointM(k * 64 + 32, 5000));
        r.Waterways.Add(new Waterway { Id = 0, Kind = WaterKind.Stream, Points = [.. pts], WidthCm = [.. pts.Select(_ => 400)], DepthCm = [.. pts.Select(_ => 60)], CatchmentTenthsKm2 = 200, LengthM = 20 * 64 });
        return new WaterSites(r, rules);
    }

    private static void Put(ConstructionRig rig, int x, int y, string piece)
    {
        var c = rig.W.Costs.Get(piece)!;
        var b = new BuiltCell { X = x, Y = y, Z = 0, Slot = GhostSlot.Fixture, Piece = piece, Hp = PieceCosts.BuiltHp(c, 50), BuiltSkill = 50 };
        rig.W.Book.Built[b.SlotKey] = b;
    }

    [Fact]
    public void ES15_APoweredTripHammer_CutsForgeBar_AndWhenTheWheelFreezesItIsBackToEightHours()
    {
        var rig = ConstructionRig.Make(); var cat = new PowerCatalog(EconFixture.Content);
        var pw = new PowerWorld(rig.W, cat, 1234) { Sites = StreamSites(cat.Rules) };
        Put(rig, WheelX, WheelY, "waterwheel");
        for (int i = 1; i <= 2; i++) Put(rig, WheelX - i, WheelY, "cam_shaft");
        Put(rig, WheelX - 3, WheelY, "trip_hammer"); Put(rig, WheelX - 3, WheelY + 2, "anvil");
        pw.Day(35);                                                                              // an autumn day with water
        Assert.True(pw.Nets[0].Watts > 0);

        var k = new ProdKit(households: 2);
        var book = new ScaleBook(); k.Sys.Scale = book;
        k.Sys.StationLabourPm = ScaleWiring.MachineLabour(() => book, () => pw);
        k.Sys.StationMachineWhy = (b0, si) => pw.Nets[0].Why;
        var b = k.Make("smithy", 100_000, 1, Occupation.Smith, Skill.Smithing, 50);
        int forge = b.Stations.FindIndex(s => s.Kind == "forge");
        // no coupling yet: the kind's 1,000
        Assert.Null(k.Sys.StationLabourPm(b, forge));
        book.Couplings.Add(new MachineCoupling { Business = b.Id.Value, Station = forge, X = WheelX - 3, Y = WheelY + 2, Z = 0 });
        Assert.Equal(375, k.Sys.StationLabourPm(b, forge));                                      // the trip-hammer's labour_pm
        long Progress(long hour)
        {
            var kk = new ProdKit(households: 2); kk.Sys.Scale = book; kk.Sys.StationLabourPm = k.Sys.StationLabourPm;
            var bb = kk.Make("smithy", 100_000, 1, Occupation.Smith, Skill.Smithing, 50);
            // the same business id as the coupled one, so the hook finds its coupling
            book.Couplings.Clear(); book.Couplings.Add(new MachineCoupling { Business = bb.Id.Value, Station = forge, X = WheelX - 3, Y = WheelY + 2, Z = 0 });
            kk.Give(bb, "iron_bloom", 6_000); kk.Give(bb, "charcoal", 10_000);
            Assert.True(kk.Sys.TryStart(bb, bb.Stations[forge], kk.R("forge_bar"), At(6)));
            kk.Sys.WorkHour(At(6));
            return bb.Stations[forge].ProgressMicro;
        }
        long powered = Progress(6);
        pw.MeanC = d => -8; pw.Day(35 + 30 + 4);                                                 // the stream freezes
        Assert.True(pw.Nets[0].Stopped); Assert.Equal("lb_frozen", pw.Nets[0].Why);
        long frozen = Progress(6);
        Assert.Equal(1000, k.Sys.StationLabourPm(b, forge) ?? 1000);                             // back to labour_pm 1,000
        Assert.True(powered > frozen * 2);                                                       // 375 against 1,000: 2.67 times the work an hour
        Assert.InRange(powered - frozen * 1000 / 375, -1500, 1500);
        // the Costs section names the cause
        var cv = CostViews.Of(k.Sys, book, b, 0);
        var m = Assert.Single(cv.Machines); Assert.Equal("lb_frozen", m.Cause); Assert.Equal(1000, m.KindPm);
    }

    // ---------------------------------------------------------------- ES-16: determinism, save, conservation

    private sealed class YearResult { public ulong Hash; public long Coin, Mass; public int Delivered; public byte[] Bytes = []; public int Runs; }

    private static YearResult RunYear()
    {
        var t = Towns.Make(households: 8);
        var seller = t.Make("bloomery", 0, 0, 600_000); var buyer = t.Make("smithy", 1, 1, 600_000);
        var works = t.Make("munition_works", 0, 3, 200_000, Occupation.Smith, Skill.Smithing, 80);
        t.People.Occupation[2] = Occupation.Labourer; t.Sw.CarterOf = (s, d) => t.People.Household[2];
        t.Pop.Region.Settlements[0].Class = SettlementClass.Town;
        Assert.True(t.Sw.Offer(seller, buyer, t.G("charcoal"), 600_000, 5, 4, true, 0).Ok);
        var anvil = works.Stations.First(s => s.Kind == "anvil"); var spear = t.Sys.Content.Recipes["forge_spearheads"];
        t.Sys.Scale = t.Book;
        long coin0 = t.Econ.Ledger.Audit().InPurses.Mites;
        for (long h = 0; h < 24L * 60; h++)
        {
            var now = At(h); int day = (int)(h / 24);
            if (now.Hour == 0) { t.Give(seller, "charcoal", 300_000); t.Give(works, "iron_bar", 8_000); t.Give(works, "charcoal", 3_000); }
            t.Sys.WorkHour(now); t.Sys.DailySlice(now);
            if (now.Hour == 9) { if (day % TimeConst.DaysPerSeason == 0) t.Sw.Seasonal(day); t.Sw.Daily(day); }
        }
        Assert.True(t.Econ.MassAuditHolds());
        Assert.True(t.Econ.Ledger.Audit().IsConserved);
        var back = ScaleSave.Capture(t.Book).Restore();
        Assert.Equal(t.Book.Hash(), back.Hash());
        var bytes = SaveCodec.SerializeCompressed(ScaleSave.Capture(t.Book)); Assert.Equal(bytes, SaveCodec.SerializeCompressed(ScaleSave.Capture(back)));
        return new YearResult { Hash = t.Book.Hash(), Coin = t.Econ.Ledger.Audit().InPurses.Mites, Mass = works.Store.Qty(t.G("spearheads")) + buyer.Store.Qty(t.G("charcoal")), Delivered = t.Book.Contracts.Sum(c => c.Delivered), Bytes = bytes, Runs = t.Book.Runs.Sum(r => r.Days.Count) };
    }

    [Fact]
    public void ES16_AYearWithContractsAndPatterns_IsDeterministic_Conserved_AndSavesTheSameBytes()
    {
        var a = RunYear(); var b = RunYear();
        Assert.Equal(a.Hash, b.Hash); Assert.NotEqual(0UL, a.Hash);
        Assert.Equal(a.Coin, b.Coin); Assert.Equal(a.Mass, b.Mass); Assert.Equal(a.Bytes, b.Bytes);
        Assert.True(a.Delivered >= 8, $"delivered {a.Delivered}"); Assert.True(a.Runs >= 1, $"runs {a.Runs}");
    }

    [Fact]
    public void ScaleSection_CarriesContractsRunsAndCouplings_AndAnOlderSectionLoadsWithNone()
    {
        var b = new ScaleBook(); b.Foremen[7] = [3];
        var c = new SupplyContract { Id = 4, Seller = 7, Buyer = 9, Good = "charcoal", QtyPerDelivery = 1_000_000, EveryDays = 5, StartDay = 2, TermSeasons = 3, PriceMicro = 12_000, CarterIsSeller = false, Delivered = 2, Missed = 1, SeasonOfMiss = 0, SeasonMisses = 1, NextDue = 12, EndDay = 0, State = SupplyState.Open };
        b.Contracts.Add(c); b.NextContractId = 5;
        b.RecordRun(7, "forge_spearheads", 3); b.RecordRun(7, "forge_spearheads", 9);
        b.Couplings.Add(new MachineCoupling { Business = 7, Station = 1, X = 4, Y = 5, Z = 0 });
        var back = ScaleSave.Capture(b).Restore();
        Assert.Equal(b.Hash(), back.Hash());
        Assert.Equal("charcoal", back.Contracts[0].Good); Assert.False(back.Contracts[0].CarterIsSeller); Assert.Equal(5, back.NextContractId);
        Assert.Equal([3, 9], back.Runs[0].Days.ToArray()); Assert.Equal(4, back.Couplings[0].X);
        Assert.Equal(SaveCodec.SerializeCompressed(ScaleSave.Capture(b)), SaveCodec.SerializeCompressed(ScaleSave.Capture(back)));
        // an ES1 section (only the first five keys) loads with no contracts, runs or couplings
        var old = new ScaleSave { Foremen = [[7, 3]] }; var restored = old.Restore();
        Assert.Empty(restored.Contracts); Assert.Empty(restored.Runs); Assert.Empty(restored.Couplings); Assert.Equal(1, restored.NextContractId);
        Assert.True(new ScaleBook().IsEmpty);
    }
}
