using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Trade;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using LootItem = Ravensguard.Sim.Sites.LootItem;

namespace Ravensguard.Sim.Tests.CloseOut;

/// <summary>
/// Phase 4 close-out (step 8): the gaps of the close-out list (folded into docs/reviews/phase-4.md, "Done in the close-out") closed in the sim. Loot reaches the pack through a hero command (mass and coin conserved, torches burnt), the surface
/// sites' search and dig are barrier commands, the hero's kin are linked at a new game and the heir takes over with the estate settled, the rescue control switch, the weather row of a fight.
/// </summary>
public class Phase4CloseOutTests
{
    private static long PackMassG(TradeWorld w) => w.State.Party!.Carried.Rows(w.Econ).Sum(r => w.Econ.Items.MassG(r, w.Econ.Goods));

    // ------------------------------------------------------------------ loot to the pack

    [Fact]
    public void A_crawls_haul_becomes_item_rows_of_its_own_mass_and_the_burnt_torches_leave_the_pack()
    {
        var w = new TradeWorld();
        w.Ctx.Pack.Create(w.G("torch"), 3, CreateReason.Seed);
        long coin = w.TotalCoin, mass0 = PackMassG(w), torchMass = w.Econ.Goods.MassG(w.G("torch"), 2);
        var loot = new List<LootItem>
        {
            new("silver_chalice", "", 420, 450, 1000),
            new("arming_sword", "iron", 160, 1100, 450, "", Dirty: true),
            new("reliquary", "", 900, 2400, 1000, "the reliquary of Aldwick, sacked in 287, with its relic"),
        };
        var cmd = SiteDesk.TakeCommand(42, loot, 2, w.Sec);
        Assert.Equal(loot, SiteDesk.DecodeTake(cmd.Tag)!.Value.Loot);                     // the haul goes through the tag whole, names with commas and all
        var r = w.Do(cmd);
        Assert.True(r.Ok, r.Text);
        Assert.Equal(3, r.Qty);
        Assert.Equal(1, w.Packed("torch"));
        Assert.Equal(mass0 + 450 + 1100 + 2400, PackMassG(w));                             // the mass that left the site, exactly
        Assert.Equal(coin, w.TotalCoin);                                                    // nothing minted
        var rows = w.State.Party!.Carried.Rows(w.Econ).Select(i => w.Econ.Items.At(i)).ToList();
        var sword = rows.Single(x => x.Shape == "arming_sword" && x.ConditionPermille == 450);
        Assert.Equal(1, sword.Stains);
        Assert.Contains(rows, x => x.Shape == "silver_chalice");
        Assert.Equal("Silver chalice", w.Ctx.Rules.Name("silver_chalice"));                 // the site item's own name in the pack
        Assert.True(torchMass > 0);
    }

    [Fact]
    public void Searching_an_old_battlefield_is_a_barrier_command_keyed_on_the_visit_counter()
    {
        var region = EconFixture.Pop.Region;
        var field = region.Sites.FirstOrDefault(s => s.Kind == SiteKind.Battlefield);
        var hoard = region.Sites.FirstOrDefault(s => s.Kind == SiteKind.Hoard);
        Assert.True(field != null || hoard != null, "the fixture region has no surface site");
        (List<string> Texts, int LootPm) Run()
        {
            var w = new TradeWorld();
            var texts = new List<string>();
            for (int i = 0; i < 12; i++)
            {
                var c = field != null ? SiteDesk.SearchCommand(field.Id.Value, w.Sec) : SiteDesk.DigCommand(hoard!.Id.Value, w.Sec);
                texts.Add(w.Do(c).Message.Key);
                w.Sec += SiteDesk.SearchSec;
            }
            var site = field ?? hoard!;
            return (texts, w.State.Sites!.Record(site.Id.Value).LootPm);
        }
        var a = Run(); var b = Run();
        Assert.Equal(a.Texts, b.Texts);                                                     // the same commands, the same finds
        Assert.Equal(a.LootPm, b.LootPm);
        Assert.Contains(a.Texts, t => t is "ex_ui_search_found" or "ex_ui_search_none" or "ex_ui_dig_none");
        Assert.True(a.LootPm < 1000 || a.Texts.All(t => t != "ex_ui_search_found"));
    }

    // ------------------------------------------------------------------ succession

    [Fact]
    public void A_new_hero_is_born_into_a_household_and_the_kin_save_and_load()
    {
        var pop = EconFixture.Pop; int day = (int)(GameTime.FromCalendar(pop.NowYear, Season.Spring, 1, 8).TotalDays);
        var kin = HeroKin.Choose(pop.People, pop.Households, pop.Region.Settlements.Count, day);
        Assert.Contains(kin, k => k.Relation == KinRelation.Parent);
        Assert.Contains(kin, k => k.Relation == KinRelation.Sibling);
        Assert.Equal(kin, HeroKin.Choose(pop.People, pop.Households, pop.Region.Settlements.Count, day));   // a pure choice of the tables
        var book = new SuccessionBook(); book.Kin.AddRange(kin);
        var bytes = SaveCodec.SerializeCompressed(SuccessionSave.Capture(book));
        var back = SaveCodec.DeserializeCompressed<SuccessionSave>(bytes).Restore();
        Assert.Equal(kin, back.Kin);
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(SuccessionSave.Capture(back)));
        Assert.Equal(book.Hash(), back.Hash());
        Assert.NotEqual(new SuccessionBook().Hash(), book.Hash());
    }

    [Fact]
    public void The_heir_takes_over_the_estate_stays_where_the_hero_fell_and_coin_is_conserved()
    {
        var w = new TradeWorld();
        var pop = EconFixture.Pop; int day = (int)(w.Sec / 86400);
        var kin = HeroKin.Choose(pop.People, pop.Households, pop.Region.Settlements.Count, day);
        w.State.Succession = new SuccessionBook(); w.State.Succession.Kin.AddRange(kin);
        var order = SuccessionDesk.Candidates(w.State, w.State.Party!, day);
        Assert.NotEmpty(order);
        Assert.True(order[0].Rule is SuccessionRule.Sibling or SuccessionRule.Parent, order[0].Rule.ToString());
        long total = w.TotalCoin, heroCoin = w.Coin;
        var items = w.State.Party!.Carried.Rows(w.Econ).Select(r => w.Econ.Items.At(r).Id).ToList();
        Assert.NotEmpty(items);
        var region = pop.Region; var t = region.Settlements[0];
        var r = w.Do(SuccessionDesk.HeirCommand(order[0].Person, t.CenterX * 1000L, t.CenterY * 1000L, w.Sec));
        Assert.True(r.Ok, r.Text);
        var hero = w.State.Hero!;
        Assert.Equal("heir", hero.Origin); Assert.Equal(order[0].Name, hero.Name);
        var book = w.State.Succession!;
        var est = Assert.Single(book.Estates);
        Assert.Equal(items, est.Items);
        foreach (var id in items) Assert.Equal(SuccessionDesk.EstateOwner(est.Id), w.Econ.Items.Get(id).Owner);   // the body's things lie with it
        Assert.Equal(total, w.TotalCoin);                                                   // coin moved, none made or lost
        Assert.Equal(heroCoin * SuccessionOrder.FuneralPm / 1000, est.FuneralMites);
        Assert.Equal(heroCoin - est.FuneralMites, w.Coin);                                  // the heir takes up the purse less the funeral
        Assert.Equal(order[0].Person, book.CurrentHero);
        Assert.NotEmpty(w.State.Party.Carried.Rows(w.Econ));                                // the heir stands in their own clothes
        Assert.Contains(w.State.Chronicle.All, e => e.TypeId == "hero_succession");
        // nobody left: the run ends and the purse waits
        var w2 = new TradeWorld();
        var end = w2.Do(SuccessionDesk.EndCommand(0, 0, w2.Sec));
        Assert.True(end.Ok); Assert.True(w2.State.Succession!.RunEnded);
        Assert.Equal(HeroResultCode.Refused, w2.Do(SuccessionDesk.EndCommand(0, 0, w2.Sec)).Code);
    }

    // ------------------------------------------------------------------ rescue

    [Fact]
    public void The_rescue_control_switch_hands_the_rescue_to_a_free_companion_and_the_run_decides_it()
    {
        (HeroResultCode Control, HeroResultCode Rescue, CaptiveState State) Run(long dy)
        {
            var w = new TradeWorld();
            var friend = EntityId.Make(IdKind.Person, 0x77_0001);
            w.State.Party!.Squad.Members.Add(new Follower { Person = friend, PersonIndex = -1, Name = "Wat", State = HireState.Active, Loyalty = 800, Origin = FollowerOrigin.Recruited });
            var book = AftermathDesk.BookOf(w.State, EconFixture.Content);
            book.Capture(book.Rules, CaptorKind.Bandits, w.State.Party.Hero, true, EntityId.Make(IdKind.Other, 5), new PurseRef(PurseKind.Household, EntityId.Make(IdKind.Other, 5)), w.Sec, CaptiveRank.Wanderer,
                1500, PayerKind.Friends, [], [], (30, 30, 20, 40, 25), rescueMembers: 1);
            Assert.Equal(HeroResultCode.Refused, w.Do(AftermathDesk.RescueCommand(friend, 0, dy, w.Sec)).Code);       // nobody is controlled yet
            var c = w.Do(AftermathDesk.ControlCommand(friend, w.Sec)).Code;
            Assert.Equal(friend, book.Controlled);
            var r = w.Do(AftermathDesk.RescueCommand(friend, 0, dy, w.Sec)).Code;
            Assert.Equal(EntityId.None, book.Controlled);                                  // control comes back whatever happened
            return (c, r, book.Hero!.State);
        }
        var near = Run(2000);
        Assert.Equal(HeroResultCode.Ok, near.Control);
        Assert.Equal(near, Run(2000));                                                      // deterministic
        Assert.Equal(near.Rescue == HeroResultCode.Ok, near.State == CaptiveState.Rescued);
        var w3 = new TradeWorld();
        Assert.Equal(HeroResultCode.Refused, w3.Do(AftermathDesk.ControlCommand(EntityId.Make(IdKind.Person, 9), w3.Sec)).Code);   // the hero is not held: no switch
    }

    [Fact]
    public void A_fights_wear_goes_back_to_the_heros_item_rows_and_never_mends_them()
    {
        var w = new TradeWorld();
        var row = w.State.Party!.Carried.Rows(w.Econ).First(r => w.State.Party.Carried.Equip.ContainsKey(w.Econ.Items.At(r).Id));
        var id = w.Econ.Items.At(row).Id;
        Assert.Equal(1, w.Do(AftermathDesk.WearCommand([(id, 400)], w.Sec)).Qty);
        Assert.Equal(400, w.Econ.Items.Get(id).ConditionPermille);
        Assert.Equal(0, w.Do(AftermathDesk.WearCommand([(id, 700), (EntityId.Make(IdKind.Item, 999_999), 10)], w.Sec)).Qty);   // the lower stays; an unknown row is passed over
        Assert.Equal(400, w.Econ.Items.Get(id).ConditionPermille);
    }

    // ------------------------------------------------------------------ weather in combat

    [Fact]
    public void A_fight_takes_its_weather_row_keeps_it_through_a_save_and_the_row_changes_the_fight()
    {
        var db = EconFixture.Content;
        var fc = new FightContent(new CombatTables(db), db);
        Assert.Equal("HeavyRain", FightWorld.WeatherRowOf(db, "HeavyRain")!.State);
        Assert.Equal("Thunderstorm", FightWorld.WeatherRowOf(db, "storm")!.State);
        Assert.Null(FightWorld.WeatherRowOf(db, ""));
        ulong Run(string weather, out FightWorld f)
        {
            f = FightCalibration.Skirmish(fc, 7, 6);
            f.WeatherRow = FightWorld.WeatherRowOf(db, weather);
            for (int t = 0; t < 400; t++) f.StepWithAi();
            return f.Hash();
        }
        ulong dry = Run("", out _), snow = Run("Blizzard", out var fb);
        Assert.NotEqual(dry, snow);
        Assert.Equal(snow, Run("Blizzard", out _));
        var rec = FightRecord.Capture(fb, "encounter", "bandits");
        Assert.Equal("Blizzard", rec.Weather);
        var back = rec.Restore(fc);
        Assert.Equal("Blizzard", back.WeatherRow?.State);
        Assert.Equal(fb.Hash(), back.Hash());
        Assert.Equal(rec.ToBytes(), FightRecord.Capture(back, "encounter", "bandits").ToBytes());
    }
}
