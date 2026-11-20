using System.Diagnostics;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Trade;
using Ravensguard.Sim.Trade;
using Xunit.Abstractions;
using BusinessRow = Ravensguard.Sim.Production.Business;

namespace Ravensguard.Sim.Tests.Ownership;

/// <summary>P5-F: the businesses, shares, stewards, day labour, stalls and the Ledger gate BS-1 to BS-17 (docs/specs/phase5/businesses-stewards-ledger.md 7); BS-18 is the 100-year soak.</summary>
public class BusinessTests(ITestOutputHelper output)
{
    private static CellEdit S(int x, int y, int z, string? piece) => new(x, y, z, CellLayer.Structure, piece);
    private static CellEdit F(int x, int y, int z, string? piece) => new(x, y, z, CellLayer.Fixture, piece);

    /// <summary>The RD-7 smithy: a walled, roofed room with two forges and an anvil and a door.</summary>
    internal static RoomWorld SmithyRoom(out int structure)
    {
        var w = BuildingLibrary.FromDatabase(EconFixture.Content).NewWorld();
        var e = new List<CellEdit>();
        for (int y = 0; y <= 4; y++) for (int x = 0; x <= 5; x++) e.Add(S(x, y, 0, x >= 1 && x <= 4 && y >= 1 && y <= 3 ? "floor_packed_earth" : "wall_timber"));
        e.RemoveAll(c => c.X == 3 && c.Y == 4 && c.Layer == CellLayer.Structure); e.Add(S(3, 4, 0, "door_wood"));
        e.Add(F(1, 1, 0, "forge")); e.Add(F(2, 1, 0, "forge")); e.Add(F(4, 3, 0, "anvil"));
        w.Apply(e);
        w.RoofOver("roof_thatch");
        structure = w.StructureAt(1, 1, 0);
        return w;
    }

    [Fact]
    public void BS1_Founding_a_smithy_in_a_room_with_two_forges_and_an_anvil()
    {
        using var r = OwnRig.Make(crowns: 200);
        var room = SmithyRoom(out int structure);
        r.O.Rooms = () => room;
        Assert.True(structure > 0);
        var st = r.O.StationsOf(structure);
        Assert.Equal(2, st["forge"]); Assert.Equal(1, st["anvil"]);
        Assert.Contains(r.O.KindsFor(st), k => k.Id == "smithy");
        long coin = r.Coin; int town = r.Town();
        var res = r.O.Found("smithy", town, -1, structure, 5000, r.Day);
        Assert.True(res.Ok, res.Key);
        var b = r.W.Sys.ById(res.Id)!;
        Assert.Equal("smithy", r.W.Sys.KindOf(b).Id);
        Assert.Equal(2, b.Stations.Count(s => s.Kind == "forge")); Assert.Equal(1, b.Stations.Count(s => s.Kind == "anvil")); Assert.Equal(3, b.Stations.Count);
        Assert.Equal(coin - 5000, r.Coin); Assert.Equal(5000, r.Led.Balance(b.Purse).Mites); Assert.Equal(5000, b.Capital);
        Assert.Equal(r.HeroRow, b.Owner); Assert.False(b.OwnerWorks);
        Assert.Contains(structure, r.O.Book.Of(b.Id)!.Premises);
        Assert.False(r.O.Found("bakery", town, -1, structure, 0, r.Day).Ok);            // no oven in the room
        Assert.True(r.CoinExact());
    }

    [Fact]
    public void BS2_Valuation()
    {
        Assert.Equal(4000, Valuation.Goodwill([2000, 1500, 2500, 2000]));
        Assert.Equal(69_000, Valuation.Value(50_000, 8000, 3000, 4000, [2000, 1500, 2500, 2000]));
        Assert.Equal(0, Valuation.Goodwill([-500, -100]));                                // a losing business has no goodwill
        Assert.Equal(4000, Valuation.Goodwill([9999, 2000, 1500, 2500, 2000]));          // the last four seasons only
    }

    [Fact]
    public void BS3_BS4_A_share_its_price_its_dividend_and_control()
    {
        Assert.Equal(27_600, Valuation.SharePrice(69_000, 400));
        var owner = EntityId.Make(IdKind.Household, 5); var hero = EconomyState.HeroOwner;
        var split = Valuation.Split(3000, owner, [new ShareRow(hero, 400)]);
        Assert.Equal(1800, split.Single(x => x.Holder == owner).Mites); Assert.Equal(1200, split.Single(x => x.Holder == hero).Mites);

        using var r = OwnRig.Make(crowns: 2000);
        var b = r.W.Sys.Businesses.First(x => x.State == BusinessState.Open && x.OwnerHousehold >= 0 && r.W.Sys.KindOf(x).Id is "bakery" or "smithy" or "tannery" or "cobbler" && x.Owner != r.HeroRow);
        int hh = b.OwnerHousehold; var hp = r.W.Econ.HouseholdPurse[hh];
        long value = r.O.ValueOf(b), price = Valuation.SharePrice(value, 400);
        long ownerBefore = r.Led.Balance(hp).Mites, coin = r.Coin;
        var res = r.O.BuyShare(b.Id, 400, 0, r.Day);
        Assert.True(res.Ok, res.Key);
        Assert.Equal(price, res.Mites);
        Assert.Equal(ownerBefore + price, r.Led.Balance(hp).Mites); Assert.Equal(coin - price, r.Coin);
        Assert.Equal(600, r.O.Book.Of(b.Id)!.OwnerPm);
        // BS-4: a sale that would leave the owner at 500 or less is refused
        Assert.False(r.O.BuyShare(b.Id, 100, 0, r.Day).Ok);
        // the S5 dividend of 3,000 goes 1,200 to the hero (his chest: he is away) and 1,800 to the owner's household
        var box = r.Led.Open(PurseKind.Chest, EntityId.Make(IdKind.Other, 777_777));
        long cash = r.Led.Balance(b.Purse).Mites;
        if (cash > 0) r.Led.Transfer(b.Purse, box, new Coin(cash), TransferReason.Gift);
        r.Led.Transfer(r.HeroPurse, b.Purse, new Coin(6000), TransferReason.Gift);
        b.Capital = 0; b.WagesSeason = 0; b.InputsSeason = 0; b.DividendsSeason = 0; b.CashStart = 6000;
        long chest = r.Chest; ownerBefore = r.Led.Balance(hp).Mites;
        r.W.Sys.Seasonal(GameTime.FromGameDays(r.Day));
        Assert.Equal(chest + 1200, r.Chest);
        Assert.Equal(ownerBefore + 1800, r.Led.Balance(hp).Mites);
        Assert.True(r.CoinExact());
    }

    [Fact]
    public void BS4_Control_is_kept_above_half()
    {
        using var r = OwnRig.Make(crowns: 5000);
        var b = r.W.Sys.Businesses.First(x => x.State == BusinessState.Open && x.OwnerHousehold >= 0 && r.W.Sys.KindOf(x).Id == "bakery");
        Assert.False(r.O.BuyShare(b.Id, 500, 0, r.Day).Ok);                                // the owner would keep 500: refused
        Assert.False(r.O.BuyShare(b.Id, 600, 0, r.Day).Ok);
        Assert.True(r.O.BuyShare(b.Id, 490, 0, r.Day).Ok);                                 // 490 is the most
        Assert.False(r.O.BuyShare(b.Id, 1, 0, r.Day).Ok);
    }

    [Fact]
    public void BS5_Hands_on_the_hero_forges_nails_at_his_smithys_anvil()
    {
        using var r = OwnRig.Make(crowns: 200, ticker: false);
        var b = r.Own("smithy", r.Town(), 1000);
        b.Store.Create(r.G("iron_bar"), 10_000, CreateReason.Import);
        int anvil = b.Stations.FindIndex(s => s.Kind == "anvil");
        long sec = 8 * 3600;
        // skill 50: 1300 - 300 = 1000 permille, so the 20 worker-hours take 20 hours
        var a = r.O.Work(b.Id, anvil, "forge_nails", sec, sec + 10 * 3600);
        Assert.True(a.Ok, a.Key);
        Assert.Equal(0, b.Store.Qty(r.G("iron_bar")));                                    // the inputs came from the business's store
        Assert.Equal(0, b.Store.Qty(r.G("nails")));
        Assert.False(r.O.Work(b.Id, anvil, "forge_nails", sec, sec + 10 * 3600).Ok);      // the same seconds never count twice
        var done = r.O.Work(b.Id, anvil, "forge_nails", sec + 10 * 3600, sec + 20 * 3600);
        Assert.True(done.Ok); Assert.Equal("bz_batch_done", done.Key);
        Assert.Equal(9_000, b.Store.Qty(r.G("nails")));
        Assert.Equal(1, b.Batches);                                                         // counted in the books
        Assert.False(b.Stations[anvil].Busy);
    }

    [Fact]
    public void BS6_Hiring_a_smith_away_from_his_post_with_a_better_wage()
    {
        using var r = OwnRig.Make(crowns: 2000, ticker: false);
        int town = r.Town();
        var mine = r.Own("smithy", town, 10_000);
        BusinessRow? old = null; int smith = -1;
        foreach (var b in r.W.Sys.Businesses)
        {
            if (b == mine || b.State != BusinessState.Open || r.W.Sys.KindOf(b).Id != "smithy") continue;
            int s = b.Staff.FirstOrDefault(p => r.P.Occupation[p] == Occupation.Smith, -1);
            if (s >= 0) { old = b; smith = s; break; }
        }
        Assert.NotNull(old);
        Assert.Equal(350, r.O.CurrentWage(smith, out _));
        Assert.False(r.O.Hire(mine.Id, smith, 350, r.Day).Ok);                             // no better than now
        int oldStaff = old!.Staff.Count;
        var res = r.O.Hire(mine.Id, smith, 400, r.Day);
        Assert.True(res.Ok, res.Key); Assert.Equal("bz_hired_away", res.Key);
        Assert.Contains(smith, mine.Staff); Assert.DoesNotContain(smith, old.Staff);
        Assert.Equal(oldStaff - 1, old.Staff.Count);
        Assert.Equal(r.HeroRow, r.P.Employer[smith]); Assert.Equal(Occupation.Smith, r.P.Occupation[smith]);
        Assert.Equal(40, r.O.WagePerHour(mine, smith, 35));                                // his agreed 400 a day
        // his old employer's post is vacant: the Phase 2 counters post it
        r.W.Sys.DailyGlobals(GameTime.FromGameDays(r.Day));
        Assert.True(old.Staff.Count < r.W.Sys.KindOf(old).StaffCapacity && old.UnfilledDays > 0);
    }

    [Fact]
    public void BS7_Supplying_iron_moves_goods_and_starts_nail_batches()
    {
        using var r = OwnRig.Make(crowns: 200, ticker: false);
        var b = r.Own("smithy", r.Town(), 2000);
        var pack = r.W.Econ.OpenStore(StoreKind.Open, EntityId.Make(IdKind.Other, 4242));
        pack.Create(r.G("iron_bar"), 200_000, CreateReason.Import);
        long coin = r.Coin, box = r.Led.Balance(b.Purse).Mites;
        var res = r.O.Supply(b.Id, pack, r.G("iron_bar"), 200_000);
        Assert.True(res.Ok, res.Key);
        Assert.Equal(200_000, b.Store.Qty(r.G("iron_bar"))); Assert.Equal(0, pack.Qty(r.G("iron_bar")));
        Assert.Equal(coin, r.Coin); Assert.Equal(box, r.Led.Balance(b.Purse).Mites);       // no coin moves
        Assert.True(r.O.Orders(b.Id, "recipe=forge_nails").Ok);
        r.W.Sys.DailySlice(GameTime.FromGameHours((long)r.Day * 24 + b.Index % 24));
        var anvil = b.Stations.First(s => s.Kind == "anvil");
        Assert.True(anvil.Busy); Assert.Equal("forge_nails", r.W.Sys.Content.Recipes.Recipes[anvil.Recipe].Id);
        Assert.Equal(190_000, b.Store.Qty(r.G("iron_bar")));
    }

    [Fact]
    public void BS8_A_stewards_prices_are_as_old_as_his_trading_says()
    {
        var rules = new Ravensguard.Content.OwnershipRulesDef { Id = "t" };
        Assert.Equal(6, StewardBrain.LagDays(40, rules)); Assert.Equal(0, StewardBrain.LagDays(100, rules)); Assert.Equal(10, StewardBrain.LagDays(5, rules));
        Assert.Equal(3, StewardBrain.Properties(10, rules)); Assert.Equal(5, StewardBrain.Properties(40, rules)); Assert.Equal(6, StewardBrain.Properties(100, rules));
        Assert.Equal(15 * 1000 * 1350 / 1000, StewardBrain.Reserve(1000, 30, rules));

        using var r = OwnRig.Make(crowns: 2000, ticker: false);
        var b = r.Own("smithy", r.Town(), 2000);
        var h = r.O.Book.Of(b.Id)!;
        int st = Enumerable.Range(0, r.P.Count).First(i => r.P.Status[i] == PersonStatus.Alive && r.P.AgeYears(i, r.Day) is >= 20 and < 50 && r.P.Occupation[i] == Occupation.None && r.W.Pop.Households.Class[r.P.Household[i]] != SocialClass.Gentry);
        r.W.Econ.Goods.TryIdOf("iron_bar", out _);
        int slot = 0; r.P.SetSkill(st, slot, Skill.Trading, 40); r.P.SetSkill(st, slot + 1, Skill.Leadership, 30);
        Assert.True(r.O.HireSteward(b.Id, st, r.Day).Ok);
        // eleven days of remembered prices, each day's a different level: he plans with the one six days back
        var goods = h.PriceGoods.Length > 0 ? h.PriceGoods : [r.G("iron_bar"), r.G("nails")];
        h.PriceGoods = goods; h.PriceDays.Clear();
        for (int d = 0; d < 11; d++) h.PriceDays.Add([.. goods.Select(g => (long)(1000 + 100 * d))]);
        var nails = r.W.Sys.Content.Recipes["forge_nails"];
        long expect = StewardBrain.MarginMicro(nails, g => 1000 + 100 * (10 - 6), r.W.Econ.Money.SpreadHalfPermille, r.W.Econ.Money.MarketTollPermille);
        Assert.Equal(expect, r.O.MarginMicro(b, nails));
        r.P.SetSkill(st, slot, Skill.Trading, 100);
        Assert.Null(r.O.MarginMicro(b, nails));                                              // Trading 100: today's book
    }

    [Fact]
    public void BS9_Profit_while_away_at_the_solvers_prices()
    {
        Assert.Equal(2362, Valuation.ProjectedSeasonProfit(1, 50, 20, 35));
        Assert.Equal(7087, Valuation.ProjectedSeasonProfit(3, 50, 20, 35));
        Assert.Equal(5250, 350 * 15);                                                        // the steward's wage, shown against it
        Assert.True(Valuation.ProjectedSeasonProfit(1, 50, 20, 35) < 5250 && Valuation.ProjectedSeasonProfit(3, 50, 20, 35) - 5250 == 1837);
    }

    [Fact]
    public void BS10_The_steward_sends_the_excess_to_the_purse_or_the_home_chest()
    {
        using var r = OwnRig.Make(crowns: 5000, ticker: false);
        int town = r.Town();
        var b = r.Own("smithy", town, 20_000);
        var h = r.O.Book.Of(b.Id)!;
        int st = Enumerable.Range(0, r.P.Count).First(i => r.P.Status[i] == PersonStatus.Alive && r.P.AgeYears(i, r.Day) is >= 20 and < 50 && r.P.Occupation[i] == Occupation.None && r.W.Pop.Households.Class[r.P.Household[i]] != SocialClass.Gentry);
        r.P.SetSkill(st, 0, Skill.Trading, 50); r.P.SetSkill(st, 1, Skill.Leadership, 40);
        Assert.True(r.O.HireSteward(b.Id, st, r.Day).Ok);
        h.FoundedDay = r.Day - 10;
        long daily = r.O.DailyCost(b, h);
        long reserve = StewardBrain.Reserve(daily, 40, r.O.Rules);
        long cash = r.Led.Balance(b.Purse).Mites, chest = r.Chest, coin0 = r.Coin;
        long sent = r.O.SendProfit(b, h, r.Day);
        Assert.Equal(cash - Math.Max(reserve, 1000), sent);
        Assert.Equal(chest + sent, r.Chest);                                                  // away: to the home chest
        Assert.Equal(cash - sent, r.Led.Balance(b.Purse).Mites);
        Assert.Equal(0, r.O.SendProfit(b, h, r.Day + 1));                                    // every 5 days
        // at the place: to his purse
        r.O.Book.HeroAt = town;
        r.Led.Transfer(r.HeroPurse, b.Purse, new Coin(3000), TransferReason.Gift);
        long sent2 = r.O.SendProfit(b, h, r.Day + 5);
        Assert.Equal(3000, sent2); Assert.Equal(coin0 - 3000 + 3000, r.Coin);
        Assert.True(r.CoinExact());
    }

    [Fact]
    public void BS11_Short_wages_are_paid_in_order_and_three_short_days_make_a_worker_quit()
    {
        using var r = OwnRig.Make(crowns: 200, ticker: false);
        var b = r.Own("smithy", r.Town(), 0);
        var two = r.Idle(r.Town()).Take(2).OrderBy(p => p).ToList();
        foreach (int p in two) { r.W.Sys.Employ(b, p, Occupation.Smith); }
        b.Owed[0] = 350; b.Owed[1] = 350;
        r.Led.Transfer(r.HeroPurse, b.Purse, new Coin(350), TransferReason.Gift);
        r.W.Sys.PayWages(b, GameTime.FromGameDays(r.Day));
        Assert.Equal(0, b.Owed[0]); Assert.Equal(350, b.Owed[1]);                            // the lower person paid first
        r.W.Sys.PayWages(b, GameTime.FromGameDays(r.Day + 1));
        r.W.Sys.PayWages(b, GameTime.FromGameDays(r.Day + 2));
        Assert.DoesNotContain(two[1], b.Staff); Assert.Contains(two[0], b.Staff);            // three short days: he quits
    }

    [Fact]
    public void BS12_Day_labour_hires_by_distance_and_id_and_pays_each_150()
    {
        using var r = OwnRig.Make(crowns: 200, ticker: false);
        var rules = r.W.Sys.Content.Ownership with { LordWorksPmPerDay = 0 };
        var payer = r.Led.Open(PurseKind.Chest, EntityId.Make(IdKind.Other, 55_555));
        r.Led.Mint(payer, new Coin(100_000), SourceReason.Seed);
        var dl = new DayLabour(r.W.Sys, r.W.Pop, r.W.Econ, rules, r.W.Economy.Labour);
        int day = r.Day; r.W.Economy.Labour.Rebuild();
        var idle = dl.Idle(day);
        // the hiring hour is for the landless: a fresh world has few, so the town's households give up their fields (the test's doing) until 40 adults stand idle there
        int s = r.Town(); var hht = r.W.Pop.Households;
        for (int h = 0; h < hht.Count && idle[s].Count < 40; h++)
            if (hht.Settlement[h] == s && hht.ParcelCount[h] > 0) { hht.ParcelCount[h] = 0; idle = dl.Idle(day); }
        Assert.True(idle[s].Count >= 40, $"{idle[s].Count} idle");
        var first40 = idle[s].Take(40).ToList();
        dl.Posts = (st, d) => st == s ? [new DayPost(s, 25, payer, "test")] : [];
        var before = first40.ToDictionary(p => p, p => r.Led.Balance(r.W.Econ.HouseholdPurse[r.P.Household[p]]).Mites);
        int hired = dl.Hour(GameTime.FromGameHours((long)day * 24 + 6));
        Assert.Equal(25, hired);
        Assert.Equal(100_000 - 25 * 150, r.Led.Balance(payer).Mites);
        var taken = first40.Take(25).ToList();
        foreach (int p in taken) Assert.Equal(Occupation.Labourer, r.P.Occupation[p]);
        foreach (int p in first40.Skip(25)) Assert.Equal(Occupation.None, r.P.Occupation[p]);
        foreach (var g in first40.GroupBy(p => r.P.Household[p]))
            Assert.Equal(before[g.First()] + 150 * g.Count(p => taken.Contains(p)), r.Led.Balance(r.W.Econ.HouseholdPurse[g.Key]).Mites);
        // the order: by distance from the square, then the person id
        Assert.Equal(idle[s].Take(25), taken);
        Assert.True(r.CoinExact());
        output.WriteLine($"settlement {s}: {idle[s].Count} idle, 25 hired");
    }

    private static (int Stall, long Subject) LeaseStall(OwnRig r, int town, bool fair, int day)
    {
        EntityId C(GameTime w, string t, IReadOnlyList<EntityId> s, IReadOnlyList<Cause> c, params (string, string)[] d) => default;
        var lease = r.L.LeaseStall(r.Hero, PurseKind.Person, town, 0, fair, day, C);
        Assert.True(lease.Ok, lease.Key);
        Assert.True(r.O.OpenStall(town, 0, -1).Ok);
        return (0, LandIds.StallSubject(town, 0));
    }

    [Fact]
    public void BS13_BS14_A_minded_stall_sells_at_the_ask_with_the_toll_and_a_fair_day_sells_more()
    {
        long Sold(bool fair, bool minded, out long takings, out long toll, out long fee)
        {
            using var r = OwnRig.Make(crowns: 500, ticker: false);
            int town = r.Town();
            int day = r.Day;
            if (fair) { while (!r.O.FairDay(town, day)) day++; }
            else while (r.O.FairDay(town, day)) day++;
            var tp = LandRegistry.TownOf(r.W.Pop.Region, town); long town0 = r.Led.Balance(r.Led.Open(tp.Kind, tp.Holder)).Mites, coin0 = r.Coin;
            var (_, subject) = LeaseStall(r, town, fair, day);
            fee = coin0 - r.Coin;
            var pack = r.W.Econ.OpenStore(StoreKind.Open, EntityId.Make(IdKind.Other, 4243));
            pack.Create(r.G("nails"), 20_000, CreateReason.Import);
            Assert.True(r.O.StallStock(subject, pack, r.G("nails"), 20_000).Ok);
            var book = r.W.Markets.BookForSettlement(town);
            long tollPurse0 = r.Led.Balance(book.TollPurse).Mites;
            long coin1 = r.Coin;
            long from = (long)day * 86400 + 8 * 3600;
            if (minded) Assert.True(r.O.Mind(subject, from, from + 8 * 3600).Ok);
            var s = r.O.Book.Stalls[subject];
            takings = s.TakingsSeason; toll = r.Led.Balance(book.TollPurse).Mites - tollPurse0;
            Assert.Equal(coin1 + takings - toll, r.Coin);                                     // the takings less the toll in the hero's purse
            Assert.Equal(20_000 - s.SoldSeason, r.W.Econ.OtherStores[s.StoreId].Qty(r.G("nails")));
            if (s.SoldSeason > 0)
            {
                long unitMicro = book.AskMicro(r.G("nails"));
                Assert.InRange(takings, s.SoldSeason * unitMicro / 1_000_000 - 400, s.SoldSeason * unitMicro / 1_000_000);   // at the book's per-unit ask (a household's part rounded down)
                Assert.InRange(toll, takings * 50 / 1000 - 400, takings * 50 / 1000 + 400);
            }
            Assert.True(r.CoinExact());
            return s.SoldSeason;
        }
        long plain = Sold(false, true, out long t1, out long toll1, out long fee1);
        long none = Sold(false, false, out _, out _, out _);
        long fairSold = Sold(true, true, out long t2, out _, out long fee2);
        output.WriteLine($"plain {plain} g for {t1} (toll {toll1}, rent {fee1}); unminded {none}; fair {fairSold} g for {t2} (fee {fee2})");
        Assert.True(plain > 0); Assert.Equal(0, none);
        Assert.Equal(1500, fee1); Assert.Equal(150, fee2);                                   // a season's rent; the fair day's fee
        Assert.True(fairSold >= plain * 14 / 10 || fairSold == 20_000, $"fair {fairSold} vs {plain}");
    }

    [Fact]
    public void BS15_Item_rows_are_sold_abroad_at_their_value()
    {
        var home = new TradeWorld(heroCrowns: 50); var away = new TradeWorld(heroCrowns: 0, merchantMites: 3_000_000);
        var ctx = new HeroContext(home.State, home.State.Party!, EconFixture.Content) { BookAbroad = (region, s) => region == 7 && s == 0 ? away.Book : null };
        var desk = new HeroDesk(); var e = home.Econ;
        var swords = Enumerable.Range(0, 3).Select(_ => ctx.Party.Carried.MakeItem(e, EconFixture.Content, ctx.Rules, ctx.Factory, home.State.Ids, "arming_sword")).ToList();
        foreach (var id in swords) ctx.Party.Inspected.Add(id);
        long coin0 = home.TotalCoin + away.TotalCoin; int items0 = e.Items.LiveRows().Count() + away.Econ.Items.LiveRows().Count();
        long got = 0;
        foreach (var id in swords)
        {
            long value = e.Items.ValueMites(e.Items.IndexOf(id), m => ctx.Factory.ValueCrownsPerKg(m));
            var c = desk.Issue(new HeroCommand { Kind = HeroCommandKind.SellItem, Item = id, IssuedSec = home.Sec, Region = 7, Settlement = 0 });
            desk.ApplyDue(ctx, c.Hour);
            var res = desk.View!.Results.Last(x => x.Seq == c.Seq);
            Assert.True(res.Ok, res.Text);
            Assert.Equal(ItemPricing.Bid(value, away.Book.Money.SpreadHalfPermille, true), res.Mites);
            got += res.Mites;
            Assert.False(e.Items.IsLive(id));
        }
        Assert.Equal(3, away.Econ.Items.LiveRows().Count(i => away.Econ.Items.At(i).Shape == "arming_sword" && away.Econ.Items.At(i).Owner == away.Merchant.Id));
        Assert.Equal(coin0, home.TotalCoin + away.TotalCoin);                                // coin conserved across the border
        Assert.Equal(items0, e.Items.LiveRows().Count() + away.Econ.Items.LiveRows().Count()); // items neither lost nor doubled
        output.WriteLine($"3 swords for {got} mites");
    }

    [Fact]
    public void BS16_The_autopilot_tends_the_stall_as_the_hero_would()
    {
        long Run(bool autopilot)
        {
            using var r = OwnRig.Make(crowns: 500, ticker: false);
            int town = r.Town(); int day = r.Day; while (r.O.FairDay(town, day)) day++;
            var (_, subject) = LeaseStall(r, town, false, day);
            var pack = r.W.Econ.OpenStore(StoreKind.Open, EntityId.Make(IdKind.Other, 4244));
            pack.Create(r.G("nails"), 20_000, CreateReason.Import);
            r.O.StallStock(subject, pack, r.G("nails"), 20_000);
            long from = (long)day * 86400 + 8 * 3600;
            if (!autopilot) r.O.Mind(subject, from, from + 8 * 3600);
            else
            {
                // the Tend directive's commands, as the autopilot issues them (one mind command each market hour, for the hour)
                for (long h = from / 3600; h < from / 3600 + 8; h++)
                {
                    var c = BusinessDesk.Mind(subject, h * 3600, h * 3600 + 3600);
                    Assert.Equal(HeroCommandKind.Business, c.Kind);
                    var p = c.Tag[BusinessDesk.Prefix.Length..].Split('|');
                    Assert.True(r.O.Mind(long.Parse(p[1]), long.Parse(p[2]), long.Parse(p[3])).Ok);
                }
            }
            return r.O.Book.Stalls[subject].TakingsSeason * 1_000_000 + r.O.Book.Stalls[subject].SoldSeason;
        }
        Assert.Equal(Run(false), Run(true));
        var d = Ravensguard.Sim.Autopilot.Directive.Tend(1234);
        Assert.Equal(Ravensguard.Sim.Autopilot.DirectiveKind.Tend, d.Kind); Assert.Equal(1234, d.Target);
    }

    [Fact]
    public void BS17_The_Ledger_sorts_200_rows_by_profit_and_filters_alerts_in_under_15_ms()
    {
        var rows = new List<LedgerRow>();
        for (int i = 0; i < 200; i++)
        {
            long profit = (i * 7919) % 5000 - 2000;
            var a = i % 7 == 0 ? OwnerAlert.WagesOwed : OwnerAlert.None;
            rows.Add(new LedgerRow(LedgerTab.Businesses, EntityId.Make(IdKind.Other, 1000 + (ulong)i), [$"b{i}", "x", "0", profit.ToString(), "1/2", a.ToString()], [0, 0, 0, profit, 1, (long)a], a));
        }
        var sw = Stopwatch.StartNew();
        var sorted = LedgerView.Sort(rows, 3, descending: true);
        var alerts = LedgerView.Sort(rows, 3, descending: true, alertsOnly: true);
        sw.Stop();
        for (int i = 1; i < sorted.Count; i++)
        {
            Assert.True(sorted[i - 1].Keys[3] >= sorted[i].Keys[3]);
            if (sorted[i - 1].Keys[3] == sorted[i].Keys[3]) Assert.True(sorted[i - 1].Id.CompareTo(sorted[i].Id) < 0);
        }
        Assert.Equal(rows.Count(x => x.Alerts != OwnerAlert.None), alerts.Count);
        Assert.All(alerts, x => Assert.NotEqual(OwnerAlert.None, x.Alerts));
        // the whole Ledger of a rig with businesses, shares and a stall builds in budget too
        using var r = OwnRig.Make(crowns: 20_000, ticker: false);
        for (int i = 0; i < 6; i++) r.Own("smithy", r.Town(), 100);
        r.O.Ledger(r.Day);
        var sw2 = Stopwatch.StartNew(); var view = r.O.Ledger(r.Day); sw2.Stop();
        output.WriteLine($"sort+filter 200 rows {sw.Elapsed.TotalMilliseconds:0.00} ms; ledger build {view.Rows.Count} rows {sw2.Elapsed.TotalMilliseconds:0.00} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds <= TestBudget.Ms(15));
        Assert.True(sw2.Elapsed.TotalMilliseconds <= TestBudget.Ms(15));
        Assert.Equal(6, view.Tab(LedgerTab.Businesses).Count());
    }

    [Fact]
    public void Steward_honesty_and_fair_jitter_streams_are_reserved_and_never_drawn()
    {
        Assert.Equal(0x540u, RngStreams.StewardHonestyReserved); Assert.Equal(0x541u, RngStreams.FairVisitorsReserved);
        var dir = Path.Combine(Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!, "..", "..", "src", "Ravensguard.Sim");
        foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (f.EndsWith("RngStreams.cs", StringComparison.Ordinal) || f.Contains("/obj/", StringComparison.Ordinal)) continue;
            var src = File.ReadAllText(f);
            Assert.DoesNotContain("StewardHonestyReserved", src, StringComparison.Ordinal);
            Assert.DoesNotContain("FairVisitorsReserved", src, StringComparison.Ordinal);
        }
    }
}
