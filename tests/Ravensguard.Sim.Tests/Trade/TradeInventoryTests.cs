using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Markets;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Tests.Trade;

/// <summary>The golden tests of docs/specs/phase3/trade-inventory-character.md section 7 (T-1 to T-16) and the desk's conservation and ordering.</summary>
public class TradeInventoryTests
{
    private static TradeSettingsDef Settings => EconFixture.Content.All<TradeSettingsDef>().Single();

    // ------------------------------------------------------------------ skills and attributes (T-1 to T-5)

    [Fact]
    public void T1_skill_gain_at_20_for_an_ordinary_task_is_256000_micro_levels()
    {
        long s = 20 * CraftMath.MicroPerLevel;
        Assert.Equal(8_000, (CraftMath.MaxMicro - s) / 10_000);
        Assert.Equal(256_000, Progress.GainMicro(s, 100));
    }

    [Fact]
    public void T2_uses_needed_from_20_to_40_60_and_80()
    {
        long s = 20 * CraftMath.MicroPerLevel; int uses = 0; int at40 = 0, at60 = 0, at80 = 0;
        while (s < 80 * CraftMath.MicroPerLevel)
        {
            s += Progress.GainMicro(s, 100); uses++;
            if (at40 == 0 && s >= 40 * CraftMath.MicroPerLevel) at40 = uses;
            if (at60 == 0 && s >= 60 * CraftMath.MicroPerLevel) at60 = uses;
            if (at80 == 0 && s >= 80 * CraftMath.MicroPerLevel) at80 = uses;
        }
        Assert.InRange(at40, 104, 105);
        Assert.InRange(at60, 310, 314);
        Assert.InRange(at80, 934, 940);
    }

    [Fact]
    public void T3_only_twelve_uses_count_in_one_game_hour()
    {
        var book = new SkillBook(); var p = EntityId.Make(IdKind.Person, 5);
        book.Set(p, Skill.Trading, 20 * CraftMath.MicroPerLevel);
        int counted = 0; UseOutcome thirteenth = UseOutcome.Gained;
        for (int i = 0; i < 100; i++)
        {
            var (o, _) = Progress.Use(book, p, Progress.TradeDeal, 40, 3600 * 10 + i * 30);
            if (o == UseOutcome.Gained) counted++;
            if (i == 12) thirteenth = o;
        }
        Assert.Equal(12, counted);
        Assert.Equal(UseOutcome.CappedThisHour, thirteenth);
        Assert.Equal(UseOutcome.Gained, Progress.Use(book, p, Progress.TradeDeal, 40, 3600 * 11).Outcome); // the next hour counts again
    }

    [Fact]
    public void T4_too_easy_work_teaches_nothing()
    {
        var book = new SkillBook(); var p = EntityId.Make(IdKind.Person, 6);
        book.Set(p, Skill.Melee, 70 * CraftMath.MicroPerLevel);
        var use = new SkillUse("task", Skill.Melee, 100);
        Assert.Equal((UseOutcome.TooEasy, 0L), Progress.Use(book, p, use, 30, 0));
        book.Set(p, Skill.Melee, 55 * CraftMath.MicroPerLevel);
        var (o, g) = Progress.Use(book, p, use, 30, 0);
        Assert.Equal(UseOutcome.Gained, o); Assert.True(g > 0);
    }

    [Fact]
    public void T5_fifty_activity_days_give_one_point_and_half_above_seventy()
    {
        int m = 0; for (int d = 0; d < 50; d++) m = Progress.AttrDay(50, m);
        Assert.Equal(1000, m);
        Assert.Equal(51, Progress.Effective(50, m));
        int h = 0; for (int d = 0; d < 50; d++) h = Progress.AttrDay(75, h);
        Assert.Equal(500, h);
        Assert.Equal(45, Progress.Effective(50, -5000));          // injuries may take up to five points
        Assert.Equal(45, Progress.Effective(50, -9000));
    }

    // ------------------------------------------------------------------ carrying (T-6 to T-8)

    [Fact]
    public void T6_encumbrance_at_strength_50()
    {
        long cap = Encumbrance.CapacityG(50);
        Assert.Equal(140_000, cap);
        Assert.Equal(125_000, Encumbrance.CapacityG(44));
        int r = Encumbrance.RatioPm(105_000, cap);
        Assert.Equal(750, r);
        Assert.Equal(800, Encumbrance.SpeedPm(r));                     // jog 3 m/s -> 2.4
        Assert.Equal(2400, 3000 * Encumbrance.SpeedPm(r) / 1000);
        Assert.Equal(1250, Encumbrance.StaminaPm(r));
        int r2 = Encumbrance.RatioPm(175_000, cap);
        Assert.Equal(1250, r2);
        Assert.Equal(350, Encumbrance.SpeedPm(r2));
        Assert.Equal(1000, Encumbrance.SpeedPm(500));
        Assert.Equal(0, Encumbrance.SpeedPm(1501));
        Assert.False(Encumbrance.CanWalk(1501));
    }

    [Fact]
    public void T7_a_thousand_crowns_weigh_1300_grams()
    {
        Assert.Equal(1_300, Encumbrance.CoinMassG(100_000));
        Assert.Equal(13, Encumbrance.CoinMassG(1_000));
    }

    [Fact]
    public void T8_containers_by_volume_and_depth()
    {
        var e = EconomyState.FromContent(EconFixture.Content, 1, 1);
        var rules = new ContainerRules(EconFixture.Content, e.Goods);
        var ids = new IdAllocator(); var owner = EntityId.Make(IdKind.Person, 9);
        var c = new Carried(owner);
        EntityId Make(string shape) => c.MakeItem(e, EconFixture.Content, rules, null, ids, shape);
        var satchel = Make("satchel"); var sack = Make("sack"); var ruck = Make("rucksack"); var pouch = Make("belt_pouch"); var pouch2 = Make("belt_pouch");
        ushort flour = e.Goods.IdOf("flour");
        c.Pack(e).Create(flour, 6000, CreateReason.Seed);
        Assert.Equal(9_600, e.Goods.VolumeMl(flour, 6000));                 // 1.6 L/kg x 6 kg
        Assert.Equal(PutRefusal.Volume, c.PutGood(e, rules, flour, satchel));
        Assert.Equal(PutRefusal.None, c.PutGood(e, rules, flour, sack));
        Assert.Equal(PutRefusal.None, c.PutItem(e, rules, satchel, ruck));   // depth 2
        Assert.Equal(PutRefusal.None, c.PutItem(e, rules, pouch, satchel));  // depth 3
        Assert.Equal(3, Carried.Depth(e.Items, e.Items.IndexOf(pouch)));
        Assert.Equal(PutRefusal.TooDeep, c.PutItem(e, rules, pouch2, pouch)); // depth 4
        Assert.Equal(PutRefusal.Itself, c.PutItem(e, rules, ruck, pouch));    // no loops
        // a sword does not go into a pouch
        var f = new ItemFactory(EconFixture.Content, e.Goods);
        var sword = c.MakeItem(e, EconFixture.Content, rules, f, ids, "arming_sword");
        Assert.Equal(PutRefusal.TooLong, c.PutItem(e, rules, sword, ruck));
        Assert.Equal(EquipSlot.MainHand, rules.SlotFor("arming_sword"));
        Assert.True(c.EquipItem(e, rules, sword));
        // carried mass: everything, including the grain and the containers
        long expect = c.Rows(e).Sum(r => e.Items.MassG(r, e.Goods)) + 6000;
        Assert.Equal(expect, c.MassG(e));
    }

    // ------------------------------------------------------------------ quotes (T-9, price moves, T-10)

    [Fact]
    public void T9_quote_for_twenty_kilograms_of_flour_in_the_flat_part()
    {
        var k = new MarketKit();
        k.Stock(50_000);                                                 // below TD/16 = 100 kg: the flat 4x part, mid 180 mites/kg (20 kg either way stays in it)
        Assert.Equal(180.0, k.Book.MidMitesPerKg(k.Grain), 3);
        var buy = Quotes.Buy(k.Book, k.Grain, 20_000);
        Assert.Equal(194.4, buy.FirstPerUnitMites, 6);
        Assert.Equal(194.4, buy.AveragePerUnitMites, 6);
        Assert.Equal(3_888, buy.TotalMites);
        Assert.Equal(k.Book.QuoteBuy(k.Grain, 20_000), buy.TotalMites);
        var sell = Quotes.Sell(k.Book, k.Grain, 20_000);
        Assert.Equal(165.6, sell.FirstPerUnitMites, 6);
        Assert.Equal(3_312, sell.TotalMites);
        Assert.Equal(166, sell.TollMites);
        Assert.Equal(3_146, sell.NetMites);
        Assert.Equal("1 sp 9.4 cp a kg", Quotes.PerUnit(buy.FirstUnitMicro, false));
        Assert.Equal("3 gp 8 sp 9 cp", Quotes.Money(3_888));
    }

    [Fact]
    public void Large_trades_move_the_price_against_you_unit_by_unit()
    {
        var k = new MarketKit();
        k.Stock(400_000);                                                // on the curve: price rises as stock falls
        var small = Quotes.Buy(k.Book, k.Grain, 1_000);
        var large = Quotes.Buy(k.Book, k.Grain, 200_000);
        Assert.True(large.AverageMicro > small.AverageMicro);
        Assert.True(large.AverageMicro > large.FirstUnitMicro);
        // the per-unit total is the integral: two halves bought one after the other cost what the whole does (splitting never beats a single trade)
        var whole = k.Book.QuoteBuy(k.Grain, 200_000);
        var buyer = k.Party(PartyKind.Household, 1_000_000);
        long a = k.Book.Buy(buyer, k.Grain, 100_000, 4000, 1).PaidMites;
        long b = k.Book.Buy(buyer, k.Grain, 100_000, 4000, 1).PaidMites;
        Assert.InRange(a + b - whole, 0, 1);
        Assert.True(b > a);
        var sell = Quotes.Sell(k.Book, k.Grain, 100_000);
        Assert.True(sell.AverageMicro < sell.FirstUnitMicro);           // selling lowers the price as you go
    }

    [Fact]
    public void T10_a_price_that_moved_between_quote_and_barrier_spends_nothing()
    {
        var w = new TradeWorld();
        ushort flour = w.G("flour");
        long q = Quotes.Buy(w.Book, flour, 20_000).TotalMites;
        long coin0 = w.Coin, total0 = w.TotalCoin; var goods0 = w.GoodsTotals();
        var cmd = w.Desk.Issue(new HeroCommand { Kind = HeroCommandKind.Buy, Good = flour, Qty = 20_000, Limit = Quotes.MaxTotal(q, Settings.SlippagePm), IssuedSec = w.Sec, Settlement = w.Settlement });
        // the world moves first: someone buys most of the flour
        var other = w.Party(PartyKind.Household, 5_000_000);
        Assert.True(w.Book.Buy(other, flour, 300_000, 4000, 1).Qty > 0);
        w.Desk.ApplyDue(w.Ctx, cmd.Hour);
        var res = w.Desk.View!.Results.Single(r => r.Seq == cmd.Seq);
        Assert.Equal(HeroResultCode.PriceMoved, res.Code);
        Assert.Equal(coin0, w.Coin);
        Assert.Equal(0, w.Packed("flour"));
        Assert.Equal(total0 + 5_000_000, w.TotalCoin);                  // only the new party's seed money
        Assert.True(w.Econ.Ledger.Audit().IsConserved);
        Assert.True(w.Econ.MassAuditHolds());
        Assert.Equal(goods0, w.GoodsTotals());
        // and at the new quote it goes through
        long q2 = Quotes.Buy(w.Book, flour, 20_000).TotalMites;
        var ok = w.Do(new HeroCommand { Kind = HeroCommandKind.Buy, Good = flour, Qty = 20_000, Limit = Quotes.MaxTotal(q2, Settings.SlippagePm) });
        Assert.True(ok.Ok, ok.Text);
        Assert.Equal(q2, ok.Mites);
        Assert.Equal(coin0 - q2, w.Coin);
        Assert.Equal(20_000, w.Packed("flour"));
    }

    [Fact]
    public void Selling_pays_the_bid_less_the_toll_through_the_ledger()
    {
        var w = new TradeWorld();
        ushort salt = w.G("salt");
        w.Ctx.Pack.Create(salt, 5_000, CreateReason.Seed);
        var q = Quotes.Sell(w.Book, salt, 5_000);
        long coin0 = w.Coin, m0 = w.Econ.Ledger.Balance(w.Merchant.Purse).Mites, toll0 = w.Econ.Ledger.Balance(w.Book.TollPurse).Mites;
        var r = w.Do(new HeroCommand { Kind = HeroCommandKind.Sell, Good = salt, Qty = 5_000, Limit = Quotes.MinTotal(q.NetMites, Settings.SlippagePm) });
        Assert.True(r.Ok, r.Text);
        Assert.Equal(q.NetMites, w.Coin - coin0);
        Assert.Equal(q.TotalMites, m0 - w.Econ.Ledger.Balance(w.Merchant.Purse).Mites);
        Assert.Equal(q.TollMites, w.Econ.Ledger.Balance(w.Book.TollPurse).Mites - toll0);
        Assert.Equal(0, w.Packed("salt"));
    }

    // ------------------------------------------------------------------ last heard (T-11)

    [Fact]
    public void T11_a_price_heard_six_days_ago_reads_aged_until_the_next_visit()
    {
        var nb = new PriceNotebook();
        nb.Hear(new PriceNote(3, 7, 184_000, 160_000, 1000, 100));
        Assert.True(nb.TryGet(3, 7, out var n));
        int age = n.AgeDays(106);
        Assert.Equal(6, age);
        var cls = LastHeard.AgeClass(age, Settings.PriceAgeFreshDays, Settings.PriceAgeOldDays);
        Assert.Equal(PriceAge.Aged, cls);
        Assert.Equal("1 sp 8.4 cp a kg (6 d)", LastHeard.Cell(n.AskMicro, false, age, cls));
        Assert.Equal(PriceAge.Fresh, LastHeard.AgeClass(3));
        Assert.Equal(PriceAge.Old, LastHeard.AgeClass(16));
        Assert.EndsWith("?", LastHeard.Cell(n.AskMicro, false, 16, PriceAge.Old));
        // a new visit on day 107 replaces it with the exact price, age 0
        var k = new MarketKit(settlementIndex: 3);
        k.Stock(10_000);
        nb.Visit(k.Book, 107);
        Assert.True(nb.TryGet(3, k.Grain, out var fresh));
        Assert.Equal(0, fresh.AgeDays(107));
        Assert.Equal(k.Book.AskMicro(k.Grain), fresh.AskMicro);
        Assert.Equal(PriceAge.Exact, LastHeard.AgeClass(0, local: true));
        // an older note never replaces a newer one
        nb.Hear(new PriceNote(3, k.Grain, 1, 1, 1, 90));
        Assert.True(nb.TryGet(3, k.Grain, out var still)); Assert.Equal(107, still.HeardDay);
    }

    [Fact]
    public void Visiting_a_market_hears_the_other_markets_with_their_age()
    {
        var w = new TradeWorld();
        foreach (var other in w.State.Markets!.AllBooks().Where(b => b != w.Book).ToList())
        { w.Seller.Store.Create(w.G("wheat"), 10_000, CreateReason.Seed); Assert.Equal(10_000, other.List(w.Seller, w.G("wheat"), 10_000, 300, 0)); }
        var r = w.Do(new HeroCommand { Kind = HeroCommandKind.VisitMarket });
        Assert.True(r.Ok);
        int today = (int)(w.Sec / 86400);
        var notes = w.Desk.View!.Notes;
        Assert.Contains(notes, n => n.Book == w.Book.SettlementIndex && n.HeardDay == today);
        if (w.State.Markets!.AllBooks().Count() > 1) Assert.Contains(notes, n => n.Book != w.Book.SettlementIndex && n.HeardDay < today);
        Assert.NotNull(w.Desk.View.Market);
        Assert.Equal(w.Book.Goods.Length, w.Desk.View.Market!.Lines.Count);
    }

    // ------------------------------------------------------------------ hiring and recruiting (T-12 to T-14)

    [Fact]
    public void T12_hire_acceptance()
    {
        var t = Settings;
        var c0 = new HireCandidate(EntityId.Make(IdKind.Person, 11), 30, 20, 0, false, false);
        Assert.Equal(186, Hiring.ReservationWage(t, 150, 20, 0));            // 150 x 1.24
        Assert.Equal(967, Hiring.Score(t, 180, 186, c0));                       // 180,000 / 186
        // trust 100 adds 10 to the score and, by the formula, takes 1% off the asking wage (R = 184)
        var c = c0 with { TrustPm = 100 };
        long r = Hiring.ReservationWage(t, 150, 20, 100);
        Assert.Equal(184, r);
        Assert.Equal(977, Hiring.Score(t, 180, 186, c));                        // the spec's 977 < 1000: refused
        Assert.Equal(1_058, Hiring.Score(t, 195, 186, c));                      // the spec's 1,048 + 10: accepted
        Assert.Equal(HireAnswer.Refuse, Hiring.Ask(t, 1, 10, 150, c, 180, noise: false).Answer);
        Assert.Equal(HireAnswer.Accept, Hiring.Ask(t, 1, 10, 150, c, 195, noise: false).Answer);
        Assert.Equal(1_069, Hiring.Ask(t, 1, 10, 150, c, 195, noise: false).Score);
        // the noise is within +/-100, a pure function of (day, person), and decides at the boundary
        int accepted = 0, refused = 0;
        for (int day = 0; day < 400; day++)
        {
            int n = Hiring.Noise(77, day, c.Person);
            Assert.InRange(n, -100, 100);
            Assert.Equal(n, Hiring.Noise(77, day, c.Person));
            var a = Hiring.Ask(t, 77, day, 150, c, 184, noise: true);    // score 1,010 before noise
            Assert.Equal(1_010 + n >= 1000 ? HireAnswer.Accept : HireAnswer.Refuse, a.Answer);
            if (a.Answer == HireAnswer.Accept) accepted++; else refused++;
        }
        Assert.True(accepted > 0 && refused > 0);
        Assert.Equal(HireAnswer.TooYoung, Hiring.Ask(t, 1, 1, 150, c with { Age = 12 }, 500).Answer);
        Assert.True(Hiring.Score(t, Hiring.FairOffer(t, r, c), r, c) >= 1000);
        Assert.True(Hiring.Score(t, Hiring.FairOffer(t, r, c) - 1, r, c) < 1000);
    }

    private static int AdultIndex(TradeWorld w, int skip = 0)
    {
        var p = w.State.Population.People; int day = (int)(w.Sec / 86400);
        for (int i = 0; i < p.Count; i++)
            if (p.IsAlive(i) && p.AgeYears(i, day) is >= 20 and < 50 && p.Household[i] >= 0 && skip-- <= 0) return i;
        throw new InvalidOperationException("no adult");
    }

    [Fact]
    public void T13_recruit_needs_familiarity_then_pays_the_signing_bonus()
    {
        var w = new TradeWorld(heroCrowns: 200);
        int i = AdultIndex(w); var id = w.State.Population.People.IdOf(i);
        var r0 = w.Do(new HeroCommand { Kind = HeroCommandKind.Recruit, Person = id, Wage = 200 });
        Assert.Equal(HeroResultCode.Refused, r0.Code);
        Assert.Contains("hardly knows", r0.Text);
        Assert.True(w.Do(new HeroCommand { Kind = HeroCommandKind.Talk, Person = id }).Ok);
        Assert.True(w.Do(new HeroCommand { Kind = HeroCommandKind.Talk, Person = id }).Ok);   // the same hour: counts once
        Assert.Equal(10, w.State.Party!.Known[id].Familiarity);
        w.Sec += 3600;
        Assert.True(w.Do(new HeroCommand { Kind = HeroCommandKind.Talk, Person = id }).Ok);
        Assert.Equal(20, w.State.Party.Known[id].Familiarity);
        Assert.True(w.State.Party.Known[id].ReasonKnown);
        int trust = w.State.Party.Known[id].TrustPm;
        int hh = w.State.Population.People.Household[i];
        long hh0 = w.Econ.Ledger.Balance(w.Econ.HouseholdPurse[hh]).Mites, coin0 = w.Coin;
        var r = w.Do(new HeroCommand { Kind = HeroCommandKind.Recruit, Person = id, Wage = 200 });
        Assert.True(r.Ok, r.Text);
        Assert.Equal(5 * 200, coin0 - w.Coin);
        Assert.Equal(5 * 200, w.Econ.Ledger.Balance(w.Econ.HouseholdPurse[hh]).Mites - hh0);
        var f = w.State.Party.Squad.Find(id)!;
        Assert.Equal(400 + trust / 4, f.Loyalty);
        Assert.Equal(FollowerOrigin.Recruited, f.Origin);
        Assert.Single(w.Desk.View!.Squad);
    }

    [Fact]
    public void T14_three_unpaid_days_and_the_recruit_quits()
    {
        var w = new TradeWorld(heroCrowns: 20);
        int i = AdultIndex(w); var id = w.State.Population.People.IdOf(i);
        var k = w.State.Party!.Know(id); k.Familiarity = 30; k.TrustPm = 200; k.ReasonKnown = true;
        var r = w.Do(new HeroCommand { Kind = HeroCommandKind.Recruit, Person = id, Wage = 300 });
        Assert.True(r.Ok, r.Text);
        long left = w.Coin;
        // empty the purse: the hero gives the rest away
        var sink = w.Party(PartyKind.Household, 0);
        if (left > 0) Assert.True(w.Econ.Ledger.Transfer(w.Ctx.Purse, sink.Purse, Coin.FromMites(left), TransferReason.Gift));
        var f = w.State.Party.Squad.Find(id)!; int loyalty0 = f.Loyalty; int day0 = f.JoinedDay;
        int chron0 = w.State.Chronicle.Count;
        w.Desk.AfterHour(w.Ctx, (day0 + 1) * 24L);
        w.Desk.AfterHour(w.Ctx, (day0 + 2) * 24L);
        Assert.Equal(loyalty0 - 200, w.State.Party.Squad.Find(id)!.Loyalty);
        w.Desk.AfterHour(w.Ctx, (day0 + 3) * 24L);
        Assert.Null(w.State.Party.Squad.Find(id));
        Assert.Empty(w.State.Party.Squad.Members);
        Assert.Equal(loyalty0 - 300, f.Loyalty);
        Assert.Equal(HireState.Quit, f.State);
        Assert.Equal(chron0 + 1, w.State.Chronicle.Count);
        Assert.Contains(w.Desk.View!.Results, x => x.Text.Contains("left your service"));
    }

    [Fact]
    public void A_hired_hand_is_paid_daily_through_the_ledger_until_the_term_ends()
    {
        var w = new TradeWorld(heroCrowns: 500);
        int i = AdultIndex(w, 3); var id = w.State.Population.People.IdOf(i);
        var r = w.Do(new HeroCommand { Kind = HeroCommandKind.Hire, Person = id, Wage = 900, Days = 2 });
        Assert.True(r.Ok, r.Text);
        int hh = w.State.Population.People.Household[i]; long hh0 = w.Econ.Ledger.Balance(w.Econ.HouseholdPurse[hh]).Mites; long c0 = w.Coin;
        int day0 = w.State.Party!.Squad.Find(id)!.JoinedDay;
        for (int d = 1; d <= 3; d++) w.Desk.AfterHour(w.Ctx, (day0 + d) * 24L);
        Assert.Equal(2 * 900, c0 - w.Coin);
        Assert.Equal(2 * 900, w.Econ.Ledger.Balance(w.Econ.HouseholdPurse[hh]).Mites - hh0);
        Assert.Null(w.State.Party.Squad.Find(id));                       // the two days are over
        var no = w.Do(new HeroCommand { Kind = HeroCommandKind.Hire, Person = id, Wage = 1 });
        Assert.Equal(HeroResultCode.Refused, no.Code);
    }

    // ------------------------------------------------------------------ conservation (T-15) and the item-row inventory (T-16)

    [Fact]
    public void T15_a_thousand_random_trades_conserve_coin_and_goods_exactly()
    {
        var w = new TradeWorld(heroCrowns: 5_000, merchantMites: 50_000_000);
        var goods = new[] { "flour", "wheat", "salt", "ale" }.Select(w.G).ToArray();
        var audit0 = w.Econ.Ledger.Audit(); long coin0 = w.TotalCoin; var g0 = w.GoodsTotals(); int items0 = w.Econ.Items.Count;
        int ok = 0;
        for (int n = 0; n < 1000; n++)
        {
            ushort g = goods[DetRng.Range(3, n, 1, 0x999, 0, 0, goods.Length)];
            long qty = DetRng.Range(3, n, 1, 0x999, 1, 1, 4000);
            bool buy = DetRng.Range(3, n, 1, 0x999, 2, 0, 2) == 0 || w.Ctx.Pack.Qty(g) < qty;
            var r = w.Do(new HeroCommand { Kind = buy ? HeroCommandKind.Buy : HeroCommandKind.Sell, Good = g, Qty = qty });
            if (r.Ok) ok++;
            if (n % 97 == 0 && w.Desk.View!.Items.FirstOrDefault() is { } it) w.Do(new HeroCommand { Kind = HeroCommandKind.SellItem, Item = it.Id });
        }
        Assert.True(ok > 500, $"only {ok} trades went through");
        Assert.Equal(coin0, w.TotalCoin);
        var a = w.Econ.Ledger.Audit();
        Assert.True(a.IsConserved);
        Assert.Equal(audit0.Seeded, a.Seeded); Assert.Equal(audit0.Minted, a.Minted);
        Assert.Equal(g0, w.GoodsTotals());
        Assert.True(w.Econ.MassAuditHolds());
        Assert.Equal(items0, w.Econ.Items.Count);                         // items change owner, never vanish
        // the trading skill grew by use
        Assert.True(w.Desk.View!.Skills.Single(s => s.Skill == Skill.Trading).Micro > 0);
    }

    [Fact]
    public void T16_the_old_kit_becomes_item_rows_of_the_same_mass()
    {
        var e = EconomyState.FromContent(EconFixture.Content, 1, 1);
        var origin = EconFixture.Content.All<OriginDef>().OrderBy(o => o.Order).First();
        var kit = new List<HeroItem>();
        string[] shapes = ["arming_sword", "dagger", "spear", "club", "gambeson", "cloak", "tunic", "shirt", "helm", "boots", "staff", "axe"];
        foreach (var s in shapes) kit.Add(new HeroItem(s, HeroRecord.DefaultMaterial(s)));
        var hero = HeroRecord.FromOrigin("Old", false, origin);
        hero = new HeroRecord { Name = hero.Name, Origin = hero.Origin, Attributes = hero.Attributes, Skills = hero.Skills, Inventory = kit, Coin = 3 };
        var party = HeroParty.Create(e, EconFixture.Content, new IdAllocator(), hero);
        var rules = new ContainerRules(EconFixture.Content, e.Goods); var f = new ItemFactory(EconFixture.Content, e.Goods);
        var rows = party.Carried.Rows(e).Select(r => e.Items.At(r)).ToList();
        Assert.Equal(12 + 2, rows.Count);                                    // twelve things and the pouch and satchel every hero gets
        Assert.All(rows, r => { Assert.Equal(ItemQuality.Common, r.Quality); Assert.Equal(1000, r.ConditionPermille); });
        var e2 = EconomyState.FromContent(EconFixture.Content, 1, 1); var c2 = new Carried(EconomyState.HeroOwner); var ids2 = new IdAllocator();
        long expect = shapes.Sum(s => { var id = c2.MakeItem(e2, EconFixture.Content, rules, f, ids2, s, HeroRecord.DefaultMaterial(s)); return e2.Items.MassG(e2.Items.IndexOf(id), e2.Goods); });
        long containers = rules.Container("belt_pouch")!.EmptyMassG + rules.Container("satchel")!.EmptyMassG;
        Assert.Equal(expect + containers, party.Carried.MassG(e));
        Assert.Equal(shapes.Order(StringComparer.Ordinal), rows.Where(r => !rules.IsContainer(r.Shape)).Select(r => r.Shape).Order(StringComparer.Ordinal));
        // the party's save form round-trips byte for byte
        var b1 = Ravensguard.Sim.Save.SaveCodec.SerializeCompressed(HeroPartySave.Capture(party));
        var b2 = Ravensguard.Sim.Save.SaveCodec.SerializeCompressed(HeroPartySave.Capture(Ravensguard.Sim.Save.SaveCodec.DeserializeCompressed<HeroPartySave>(b1).Restore()));
        Assert.Equal(b1, b2);
    }

    // ------------------------------------------------------------------ inventory operations through the desk

    [Fact]
    public void Bought_goods_are_stowed_and_slow_the_hero_down()
    {
        var w = new TradeWorld(heroCrowns: 2000);
        var v0 = w.Desk.View ?? w.Desk.BuildView(w.Ctx, 0);
        var r = w.Do(new HeroCommand { Kind = HeroCommandKind.Buy, Good = w.G("wheat"), Qty = 5_000 });
        Assert.True(r.Ok, r.Text);
        var v = w.Desk.View!;
        var stack = v.Goods.Single(g => g.Id == "wheat");
        Assert.NotEqual(EntityId.None, stack.Container);                     // into the satchel
        Assert.InRange(v.MassG - (v0.MassG + 5_000 - Encumbrance.CoinMassG(r.Mites)), -1, 1);   // the coin's grams round down on the whole purse
        var big = w.Do(new HeroCommand { Kind = HeroCommandKind.Buy, Good = w.G("wheat"), Qty = 100_000 });
        Assert.True(big.Ok, big.Text);
        v = w.Desk.View!;
        Assert.True(v.RatioPm > 500);
        Assert.True(v.SpeedPm < 1000);
        Assert.Equal(EntityId.None, v.Goods.Single(g => g.Id == "wheat").Container);   // 105 kg does not fit a satchel: carried loose
    }

    [Fact]
    public void Equip_and_put_away_through_commands()
    {
        var w = new TradeWorld();
        var v = w.Desk.BuildView(w.Ctx, 0);
        var satchel = v.Items.Single(i => i.Shape == "satchel");
        var weapon = v.Items.First(i => i.Slot == EquipSlot.MainHand || i.Slot == EquipSlot.BothHands);
        Assert.True(w.Do(new HeroCommand { Kind = HeroCommandKind.Unequip, Item = weapon.Id }).Ok);
        var put = w.Do(new HeroCommand { Kind = HeroCommandKind.PutItem, Item = weapon.Id, Container = satchel.Id });
        Assert.False(put.Ok);                                                // too long for a satchel
        Assert.True(w.Do(new HeroCommand { Kind = HeroCommandKind.Equip, Item = weapon.Id }).Ok);
        Assert.Equal(weapon.Slot, w.Desk.View!.Items.Single(i => i.Id == weapon.Id).Slot);
    }

    [Fact]
    public void Barter_with_a_person_is_accepted_only_when_they_do_not_lose()
    {
        var w = new TradeWorld();
        int i = AdultIndex(w, 5); var id = w.State.Population.People.IdOf(i); int hh = w.State.Population.People.Household[i];
        var store = w.Econ.HouseholdStore[hh];
        ushort g = store.Rows().OrderByDescending(r => r.Qty).First().Good;
        long take = Math.Min(1000, store.Qty(g));
        var greedy = w.Do(new HeroCommand { Kind = HeroCommandKind.Barter, Person = id, Take = [(g, take)] });
        Assert.Equal(HeroResultCode.Refused, greedy.Code);
        long coin0 = w.Coin, total0 = w.TotalCoin; var g0 = w.GoodsTotals();
        var fair = w.Do(new HeroCommand { Kind = HeroCommandKind.Barter, Person = id, Take = [(g, take)], CoinGive = 10_000 });
        Assert.True(fair.Ok, fair.Text);
        Assert.Equal(coin0 - 10_000, w.Coin);
        Assert.Equal(take, w.Ctx.Pack.Qty(g));
        Assert.Equal(total0, w.TotalCoin);
        Assert.Equal(g0, w.GoodsTotals());
    }
}
