using Ravensguard.Content;
using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Trade;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.CleanupB;

/// <summary>Phase 3 cleanup B: the gameplay gaps (road encounter outcomes, real casts, followers, encumbrance, trade abroad, fairs, carried rumours, camp talk, dialogue effects).</summary>
public class CleanupBTests
{
    private static ContentDatabase Db => EconFixture.Content;
    private static TravelState NewTravel(ulong seed = 11)
    {
        var goods = EconFixture.Catalog();
        return new TravelState(Db, seed, goods, new MassLedger(goods.Count)) { GameStartSec = 0, HeroId = EntityId.Make(IdKind.Person, 1) };
    }
    private static EncounterDef Def(TravelState t, string id) => t.Table.Defs.First(d => d.Id == id);
    private static EncounterEvent Ev(TravelState t, string id, params ulong[] cast) => new(1_000_000, id, Def(t, id).Kind, 0, 0, 0, 0, 0, cast, "");

    // ------------------------------------------------------------------ 1. encounter outcomes

    [Fact]
    public void Paying_a_demand_goes_through_the_ledger_and_conserves_coin()
    {
        var w = new TradeWorld(heroCrowns: 200);
        var p = w.State.Population.People; int i = Enumerable.Range(0, p.Count).First(k => p.IsAlive(k) && p.Household[k] >= 0);
        var t = NewTravel(); var def = Def(t, "highwaymen");
        long coin = w.Coin, total = w.TotalCoin;
        var opts = EncounterOutcomes.Options(def, coin);
        var pay = opts.First(o => o.Choice == EncounterChoice.Pay);
        Assert.Equal(coin / 10, pay.Mites);                                            // highwaymen ask a tenth
        var o = EncounterOutcomes.Resolve(t, def, Ev(t, "highwaymen", p.IdOf(i).Value), EncounterChoice.Pay, coin, 500, id => true);
        Assert.Equal("paid", o.Result); Assert.Equal(pay.Mites, o.PayMites); Assert.Equal(p.IdOf(i), o.Payee);
        long theirs = w.Econ.Ledger.Balance(w.Econ.HouseholdPurse[p.Household[i]]).Mites;
        var r = w.Do(new HeroCommand { Kind = HeroCommandKind.EncounterPay, CoinGive = o.PayMites, Person = o.Payee });
        Assert.True(r.Ok, r.Text);
        Assert.Equal(coin - pay.Mites, w.Coin);
        Assert.Equal(theirs + pay.Mites, w.Econ.Ledger.Balance(w.Econ.HouseholdPurse[p.Household[i]]).Mites);
        Assert.Equal(total, w.TotalCoin);                                              // nothing minted or destroyed
        Assert.False(w.Do(new HeroCommand { Kind = HeroCommandKind.EncounterPay, CoinGive = w.Coin + 1, Person = o.Payee }).Ok);
    }

    [Fact]
    public void A_fight_is_a_real_fight_now_and_its_end_robs_or_lets_go()
    {
        // Phase 4 P4-C replaced the stub: choosing to fight starts a FightSetup from the cast; AfterFight settles it the same way every time
        var t = NewTravel(); var def = Def(t, "bandit_ambush_small");
        var ev = Ev(t, "bandit_ambush_small", 5);
        var a = EncounterOutcomes.Resolve(t, def, ev, EncounterChoice.Fight, 100_000, 300, id => true);
        Assert.Equal(a, EncounterOutcomes.Resolve(t, def, ev, EncounterChoice.Fight, 100_000, 300, id => true));
        Assert.Equal("fight", a.Result);
        Assert.DoesNotContain("STUB", a.Text);
        Assert.Equal("won", EncounterOutcomes.AfterFight(ev, true, false, 100_000, id => true).Result);
        Assert.Equal("fled", EncounterOutcomes.AfterFight(ev, false, true, 100_000, id => true).Result);
        var lost = EncounterOutcomes.AfterFight(ev, false, false, 100_000, id => true);
        Assert.Equal("robbed", lost.Result); Assert.Equal(25_000, lost.PayMites);
        Assert.Equal(0, EncounterOutcomes.AfterFight(ev, false, false, 0, id => true).PayMites);
        // the options: a bandit ambush offers pay, fight and flee; a toll keeper pay and flee; a peddler trade, talk, go on
        Assert.Equal([EncounterChoice.Pay, EncounterChoice.Fight, EncounterChoice.Flee], EncounterOutcomes.Options(def, 5000).Select(o => o.Choice));
        Assert.Equal([EncounterChoice.Pay, EncounterChoice.Flee], EncounterOutcomes.Options(Def(t, "toll_keeper"), 5000).Select(o => o.Choice));
        Assert.Equal([EncounterChoice.Trade, EncounterChoice.Talk, EncounterChoice.GoOn], EncounterOutcomes.Options(Def(t, "peddler"), 5000).Select(o => o.Choice));
        var fled = EncounterOutcomes.Resolve(t, def, ev, EncounterChoice.Flee, 5000, 300, id => true);
        Assert.Equal(EncounterOutcomes.FleeDelaySec, fled.DelaySec);
    }

    [Fact]
    public void The_merchant_you_meet_is_the_real_caravan_on_that_road()
    {
        var w = new TradeWorld(); var pop = w.State.Population; var p = pop.People;
        var rows = Enumerable.Range(0, p.Count).Where(k => p.IsAlive(k)).Take(5).ToArray();
        var car = new Caravan
        {
            Id = EntityId.Make(IdKind.Other, 4242), Home = 0, Region = 0, Master = rows[0], Crew = rows[1..], Guards = 1, State = CaravanState.Outbound, Vehicles = [VehicleKind.OxWagon],
            Path = CaravanPath.FromPoints([(100, 100, RoadClass.Road), (900, 100, RoadClass.Road)]),
        };
        var cast = new RecordCast(() => [car], () => pop, 0, 0);
        var t = NewTravel(); t.CastSource = cast;
        var def = Def(t, "merchant_cart"); var g = def.Groups[0];
        var ids = EncounterPlacement.Cast(t, def, g, 3, 1_000_000, 100_400, 100_000);
        Assert.Equal(3, ids.Count);
        Assert.Equal(p.IdOf(rows[0]), ids[0]);                                         // the master first, then the crew
        Assert.All(ids, id => Assert.Same(car, cast.CaravanOf(id)));
        Assert.Empty(t.Travellers);                                                    // nobody was made up
        var o = EncounterOutcomes.Resolve(t, def, Ev(t, "merchant_cart", [.. ids.Select(i => i.Value)]), EncounterChoice.Trade, 5000, 500, id => cast.CaravanOf(id) != null);
        Assert.Equal("trade", o.Result); Assert.Equal(ids[0], o.Counterpart);
        // far from the road the same call makes up travellers
        var far = EncounterPlacement.Cast(t, def, g, 2, 1_000_000, 9_000_000, 9_000_000);
        Assert.Equal(2, t.Travellers.Count); Assert.All(far, id => Assert.Null(cast.CaravanOf(id)));
    }

    // ------------------------------------------------------------------ 4. encumbrance

    private static LiveWorld Field()
    {
        var g = new LevelGrid(200, 20, 0, 1);
        g.FillRect(0, 0, 199, 19, 0, true);
        return new LiveWorld(g, new Ravensguard.Sim.Lighting.LightGrid(g), new Cell3(5, 10, 0), GameTime.FromCalendar(1, Season.Spring, 8, 12));
    }

    private static double Walked(int pm, int ticks = 100)
    {
        var w = Field(); w.LoadSpeedPm = pm; w.SetInput(new MoveInput(1, 0));
        double x0 = w.Snapshot.Hero.X.ToFloat();
        for (int i = 0; i < ticks; i++) w.Step();
        return w.Snapshot.Hero.X.ToFloat() - x0;
    }

    [Fact]
    public void Encumbrance_sets_an_exact_speed_factor_on_the_live_hero()
    {
        double free = Walked(1000);
        Assert.InRange(free, 14.5, 15.5);                                              // 100 ticks of 3 m/s
        int r = Ravensguard.Sim.Inventory.Encumbrance.RatioPm(105_000, Ravensguard.Sim.Inventory.Encumbrance.CapacityG(50));   // T-6: 105 kg at strength 50
        Assert.Equal(750, r); Assert.Equal(800, Ravensguard.Sim.Inventory.Encumbrance.SpeedPm(r));
        Assert.InRange(Walked(800) / free, 0.799, 0.801);
        Assert.InRange(Walked(350) / free, 0.349, 0.351);
        Assert.Equal(0, Walked(0));
    }

    // ------------------------------------------------------------------ 5. trade in a neighbour region

    [Fact]
    public void The_hero_buys_and_sells_in_another_resident_regions_market_and_nothing_is_made()
    {
        var home = new TradeWorld(heroCrowns: 500); var away = new TradeWorld(heroCrowns: 0, merchantMites: 3_000_000);
        var ctx = new HeroContext(home.State, home.State.Party!, EconFixture.Content) { BookAbroad = (region, s) => region == 7 && s == 0 ? away.Book : null };
        var desk = new HeroDesk();
        long coin0 = home.TotalCoin + away.TotalCoin; var flour = home.G("flour");
        long mass0 = home.GoodsTotals()[flour] + away.GoodsTotals()[flour];
        HeroResult Do(HeroCommand c) { var cmd = desk.Issue(c with { IssuedSec = home.Sec, Region = 7, Settlement = 0 }); desk.ApplyDue(ctx, cmd.Hour); return desk.View!.Results.Last(r => r.Seq == cmd.Seq); }
        Assert.True(Do(new HeroCommand { Kind = HeroCommandKind.VisitMarket }).Ok);
        Assert.Equal(7, desk.View!.Market!.Region);
        long awayStock = away.Book.Goods[flour].StockUnits, before = home.Coin;
        var buy = Do(new HeroCommand { Kind = HeroCommandKind.Buy, Good = flour, Qty = 5_000 });
        Assert.True(buy.Ok, buy.Text);
        Assert.Equal(5_000, home.Packed("flour"));                                      // the flour crossed the border into the pack
        Assert.Equal(before - buy.Mites, home.Coin);
        Assert.Equal(awayStock - 5_000, away.Book.Goods[flour].StockUnits);
        Assert.Equal(coin0, home.TotalCoin + away.TotalCoin);
        Assert.Equal(mass0, home.GoodsTotals()[flour] + away.GoodsTotals()[flour]);
        var sell = Do(new HeroCommand { Kind = HeroCommandKind.Sell, Good = flour, Qty = 2_000 });
        Assert.True(sell.Ok, sell.Text);
        Assert.Equal(3_000, home.Packed("flour"));
        Assert.Equal(coin0, home.TotalCoin + away.TotalCoin);
        Assert.Equal(mass0, home.GoodsTotals()[flour] + away.GoodsTotals()[flour]);
        Assert.Equal(0, away.Econ.Ledger.Balance(away.Econ.Ledger.Open(PurseKind.Person, EconomyState.HeroOwner)).Mites);      // the visitor purse is left empty
    }

    // ------------------------------------------------------------------ 6. fairs

    [Fact]
    public void A_fair_runs_more_market_sessions_and_posts_more_odd_jobs_and_stalls()
    {
        var w = new TradeWorld();
        var fair = GameTime.FromCalendar(1, Season.Autumn, 8, 19);                       // day 38, the Harvest Festival, an hour after the stalls close
        var plain = GameTime.FromCalendar(1, Season.Autumn, 11, 19);
        Assert.Equal(38, fair.DayOfYear); Assert.Equal(41, plain.DayOfYear);
        var m = w.State.Markets!;
        long s0 = m.SessionsRun; m.HourlySession(plain); long plainRuns = m.SessionsRun - s0;
        s0 = m.SessionsRun; m.HourlySession(fair); long fairRuns = m.SessionsRun - s0;
        Assert.Equal(0, plainRuns); Assert.True(fairRuns > 0 && m.FairSessionsRun >= fairRuns);
        Assert.Equal(3, FairCalendar.JobBoardMult(9, SettlementClass.Town)); Assert.Equal(1, FairCalendar.JobBoardMult(9, SettlementClass.Village));
        Assert.Equal(12, FairCalendar.StallBonus(9, SettlementClass.Town)); Assert.Equal(0, FairCalendar.StallBonus(12, SettlementClass.Town));
        // the board: the same settlement posts more on the fair's day than on an ordinary one, paid from the households' coin
        var pop = w.State.Population; var env = new JobEnv { Ledger = w.Econ.Ledger, Econ = w.Econ, Pop = pop, Tuning = new Ravensguard.Content.OddJobTuningDef { Id = "odd_jobs", BoardCap = 60 } };
        int best = Enumerable.Range(0, pop.Region.Settlements.Count).MaxBy(s => Enumerable.Range(0, pop.Households.Count).Count(h => pop.Households.Settlement[h] == s));
        int Posted(long day) { var table = new JobTable(); int n = RoutineJobs.PostDay(table, env, pop, 0, best, day, 1234); return n; }
        long fairDay = 5 * 60 + 37, plainDay = 5 * 60 + 40;                              // doy 38 and 41 of a year
        Assert.Equal(38, new GameTime(fairDay * 86400).DayOfYear);
        Assert.True(Posted(fairDay) > Posted(plainDay), "the fair's day has more postings");
    }

    // ------------------------------------------------------------------ 7. caravans carry rumours

    [Fact]
    public void A_crew_member_who_travels_tells_a_rumour_in_the_destination_region()
    {
        var (home, hc) = Ravensguard.Sim.Tests.Dialogue.TalkKit.Town(120, seed: 21, names: ["Ashby"]);
        var (away, _) = Ravensguard.Sim.Tests.Dialogue.TalkKit.Town(120, seed: 22, names: ["Corvale"]);
        var book = LineBook.Default; var hts = new TalkState(); var ats = new TalkState();
        var id = Ravensguard.Sim.Tests.Dialogue.TalkKit.AddEntry(hc, "fire", 100, "Ashby");
        TalkSystem.AfterHour(home, hts, book, 100 * 24 + 7);
        int teller = hts.Rumours.Get(id)!.Witnesses[0];
        Assert.Equal(0, ats.Rumours.Count);
        int made = RumourCarriers.Deliver(home, hts, away, ats, [teller], 0, 100 * 24 + 12, book.Settings);
        Assert.True(made >= 1 && made <= RumourCarriers.Listeners);
        var copy = ats.Rumours.Get(id)!;
        Assert.Equal(id, copy.Source); Assert.True(copy.FromAfar); Assert.Equal("Ashby", copy.Place);
        foreach (var h in ats.Rumours.HearingsOf(id)) { Assert.Equal(HearHow.Traveller, h.How); Assert.Equal(2, h.Hop); Assert.True(away.People.IsAlive(h.Listener)); }
        // the same call again tells no one twice, and a second call on a fresh copy of the records gives the same hearers (stable order)
        Assert.Equal(0, RumourCarriers.Deliver(home, hts, away, ats, [teller], 0, 100 * 24 + 13, book.Settings));
        var ats2 = new TalkState(); RumourCarriers.Deliver(home, hts, away, ats2, [teller], 0, 100 * 24 + 12, book.Settings);
        Assert.Equal(ats.Rumours.HearingsOf(id).Select(h => h.Listener), ats2.Rumours.HearingsOf(id).Select(h => h.Listener));
        // they bring word back too: the neighbour's rumour reaches the crew
        Ravensguard.Sim.Tests.Dialogue.TalkKit.AddEntry(away.Chronicle, "birth", 101, "Corvale");           // so the two chronicles' entry ids differ, as they do between regions
        var aid = Ravensguard.Sim.Tests.Dialogue.TalkKit.AddEntry(away.Chronicle, "fire", 101, "Corvale");
        TalkSystem.AfterHour(away, ats, book, 101 * 24 + 7);
        var talkers = RumourCarriers.TravellerHearers(away, ats, 0, 101 * 24 + 8);
        var back = ats.Rumours.Rumours.Values.First(r => r.Id == aid);
        int holder = ats.Rumours.Held.First(kv => kv.Value.Contains(aid)).Key;
        Assert.True(RumourCarriers.Deliver(away, ats, home, hts, [holder], [teller], 101 * 24 + 9, book.Settings) >= 1);
        Assert.Equal(HearHow.Traveller, hts.Rumours.Find(aid, teller)!.Value.How);
        Assert.NotEmpty(talkers); Assert.NotNull(back);
    }

    // ------------------------------------------------------------------ 3. followers

    [Fact]
    public void Followers_keep_their_spacing_behind_the_hero_up_the_stairs_and_wait_when_told()
    {
        FollowState[] Walk(int stepMm, long minStep)
        {
            var trail = new HeroTrail(); var f = new[] { new FollowState { Slot = 0 }, new FollowState { Slot = 1 } };
            long x = 0; trail.Note(x, 0, 0);
            void Tick() { foreach (var s in f) s.Step(trail, minStep); }
            for (; x < 30_000; x += stepMm) { trail.Note(x, 0, 0); Tick(); }
            trail.Note(30_000, 0, 1); Tick();                                       // the stairs: same spot, next floor
            long y = 0;
            for (; y < 12_000; y += stepMm) { trail.Note(30_000, y, 1); Tick(); }
            for (int i = 0; i < 400; i++) Tick();                                   // the hero stands; they close up
            return f;
        }
        var trail1 = new HeroTrail();
        var a = Walk(50, 150); var b = Walk(500, 1200);                              // 1x ticks and 8x ticks
        // settled: exactly spacing behind the hero along the trail, on the upper floor, the same at both speeds
        Assert.Equal(FollowState.SpacingMm, a[0].S - a[1].S); Assert.Equal(FollowState.SpacingMm, b[0].S - b[1].S); Assert.Equal(a[0].S, b[0].S); Assert.Equal(a[1].S, b[1].S);
        // an explicit trail: hero at (30000, 12000, z1); follower 0 stands 1.8 m behind on the upper floor, follower 1 3.6 m behind
        var t = new HeroTrail(); for (long x = 0; x <= 30_000; x += 500) t.Note(x, 0, 0); t.Note(30_000, 0, 1); for (long y = 500; y <= 12_000; y += 500) t.Note(30_000, y, 1);
        var p0 = t.At(FollowState.TargetS(t, 0)); var p1 = t.At(FollowState.TargetS(t, 1));
        Assert.Equal((30_000L, 10_200L, 1), (p0.X, p0.Y, p0.Z)); Assert.Equal((30_000L, 8_400L, 1), (p1.X, p1.Y, p1.Z));
        // the one further down the line is still on the lower floor when the hero is just past the stairs
        var t2 = new HeroTrail(); for (long x = 0; x <= 30_000; x += 500) t2.Note(x, 0, 0); t2.Note(30_000, 0, 1); for (long y = 500; y <= 2_500; y += 500) t2.Note(30_000, y, 1);
        Assert.Equal(1, t2.At(FollowState.TargetS(t2, 0)).Z); Assert.Equal(0, t2.At(FollowState.TargetS(t2, 1)).Z);
        // told to wait, they stand where they were however far the hero goes
        var w = new FollowState { Slot = 0, Follow = false, S = 5_000 }; Assert.False(w.Step(t, 150)); Assert.Equal(5_000, w.S);
        // left far behind, they are put at their spot at once
        var lost = new FollowState { Slot = 0, S = 0 }; lost.Step(t, 150); Assert.Equal(FollowState.TargetS(t, 0), lost.S);
        _ = trail1;
    }

    // ------------------------------------------------------------------ 8. camp talk

    [Fact]
    public void The_camp_fire_talk_picks_the_companion_with_a_story_left()
    {
        var t = NewTravel(); var hero = EntityId.Make(IdKind.Person, 1);
        var a = new CampMember { Person = EntityId.Make(IdKind.Person, 10), Name = "Alda", BackstoryTold = true };
        var b = new CampMember { Person = EntityId.Make(IdKind.Person, 11), Name = "Bram" };
        var me = new CampMember { Person = hero, Name = "You" };
        Assert.Equal(b, t.FireSpeaker([me, a, b], hero, 100_000));                       // the one with a backstory left
        b.BackstoryTold = true;
        Assert.Equal(a, t.FireSpeaker([me, a, b], hero, 0)); Assert.Equal(b, t.FireSpeaker([me, a, b], hero, 86_400));        // then the night's turn
        Assert.Null(t.FireSpeaker([me], hero, 0));
    }

    // ------------------------------------------------------------------ 9. dialogue effects

    [Fact]
    public void Dialogue_give_item_moves_goods_with_conservation_and_start_contract_posts_a_job_with_escrow()
    {
        var w = new TradeWorld(heroCrowns: 100); var p = w.State.Population.People;
        int i = Enumerable.Range(0, p.Count).First(k => p.IsAlive(k) && p.Household[k] >= 0 && p.HomeSettlement[k] >= 0);
        int hh = p.Household[i]; var salt = w.G("salt");
        w.Econ.HouseholdStore[hh].Create(salt, 5_000, CreateReason.Seed);
        long house0 = w.Econ.HouseholdStore[hh].Qty(salt), pack0 = w.Ctx.Pack.Qty(salt); long total0 = w.GoodsTotals()[salt];
        long moved = DialogueEffects.GiveItem(w.State, Db, i, new Dictionary<string, string> { ["item"] = "salt", ["count"] = "2" });
        Assert.Equal(2_000, moved);
        Assert.Equal(house0 - 2_000, w.Econ.HouseholdStore[hh].Qty(salt)); Assert.Equal(pack0 + 2_000, w.Ctx.Pack.Qty(salt)); Assert.Equal(total0, w.GoodsTotals()[salt]);
        // they cannot give what they do not have, and the hero can give back
        Assert.Equal(0, DialogueEffects.GiveItem(w.State, Db, i, new Dictionary<string, string> { ["item"] = "no_such_good" }));
        Assert.Equal(1_000, DialogueEffects.GiveItem(w.State, Db, i, new Dictionary<string, string> { ["item"] = "salt", ["count"] = "1", ["to"] = "speaker" }));
        Assert.Equal(total0, w.GoodsTotals()[salt]);
        // the job: the wage leaves the household's purse for a chest
        var purse = w.Econ.HouseholdPurse[hh];
        w.Econ.Ledger.Mint(purse, new Coin(100_000), SourceReason.Seed);
        long coin0 = w.Econ.Ledger.Balance(purse).Mites, all0 = w.TotalCoin;
        var job = DialogueEffects.StartContract(w.State, Db, i, new Dictionary<string, string> { ["kind"] = "guard", ["hours"] = "4" }, w.Sec);
        Assert.NotNull(job); Assert.Equal(JobState.Open, job!.State); Assert.True(job.WageMites > 0);
        Assert.Equal(coin0 - job.WageMites, w.Econ.Ledger.Balance(purse).Mites);
        Assert.Equal(job.WageMites, w.Econ.Ledger.Balance(new PurseId(job.Escrow)).Mites);
        Assert.Equal(all0, w.TotalCoin);
        Assert.Same(job, w.State.Jobs!.Get(job.Id));
    }
}
