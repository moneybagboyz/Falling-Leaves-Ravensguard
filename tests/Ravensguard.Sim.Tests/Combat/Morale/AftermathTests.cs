using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Morale;

/// <summary>Defeat outcomes, robbery, capture, ransom, escapes, rescue and the time limits (morale-capture-succession 4.4 to 4.7; gates MC-7 to MC-13 and the overview's OV-4).</summary>
[Collection("fight")]
public class AftermathTests(FightFixture fx, ITestOutputHelper output)
{
    private AftermathRules R => AftermathRules.For(fx.Db);
    private static readonly EntityId Hero = EconomyState.HeroOwner;

    // ---------------------------------------------------------------- a hero with things

    private sealed record Kit(EconomyState E, Carried Carried, PurseId Purse, IdAllocator Ids, Dictionary<string, EntityId> Item);

    private static EntityId AddItem(EconomyState e, IdAllocator ids, EntityId owner, string shape, string material, int massG) =>
        e.Items.Add(ids, shape, ItemQuality.Common, owner, [new ItemComponent(material, massG, 1000)]);

    /// <summary>A sword, a dagger, a mail hauberk, a giant's gambeson, a cheap cap, a shirt and boots worn, bread and cloth in the pack, and <paramref name="coin"/> mites.</summary>
    private static Kit HeroKit(EconomyState e, long coin)
    {
        var ids = new IdAllocator();
        var carried = new Carried(Hero);
        var item = new Dictionary<string, EntityId>
        {
            ["sword"] = AddItem(e, ids, Hero, "arming_sword", "steel", 1100), ["dagger"] = AddItem(e, ids, Hero, "dagger", "iron", 400),
            ["mail"] = AddItem(e, ids, Hero, "mail_hauberk", "iron", 9000), ["giant_gambeson"] = AddItem(e, ids, Hero, "gambeson", "linen", 3000),
            ["cap"] = AddItem(e, ids, Hero, "cap", "leather", 300), ["shirt"] = AddItem(e, ids, Hero, "shirt", "linen", 300), ["boots"] = AddItem(e, ids, Hero, "boots", "leather", 1200),
        };
        carried.Equip[item["sword"]] = EquipSlot.MainHand; carried.Equip[item["mail"]] = EquipSlot.Body; carried.Equip[item["shirt"]] = EquipSlot.Body; carried.Equip[item["boots"]] = EquipSlot.Feet;
        var pack = carried.Pack(e);
        pack.Create(e.Goods.IdOf("bread"), 1500, CreateReason.Seed);
        pack.Create(e.Goods.IdOf("cloth_wool"), 2000, CreateReason.Seed);
        var purse = e.Ledger.Open(PurseKind.Person, Hero);
        if (coin > 0) e.Ledger.Mint(purse, Coin.FromMites(coin), SourceReason.Seed);
        return new Kit(e, carried, purse, ids, item);
    }

    private static Func<int, long> Values(EconomyState e) => row => e.Items.At(row).Shape switch
    {
        "arming_sword" => 3000, "dagger" => 300, "mail_hauberk" => 9000, "gambeson" => 250, "cap" => 40, "shirt" => 60, "boots" => 200, _ => 10,
    };

    // ---------------------------------------------------------------- MC-7

    [Fact]
    public void MC7_BanditRobbery_PurseTaken_PittanceWhenWounded_FitHonoured_UnderLayerLeft()
    {
        var e = EconFixture.NewEconomy(goods: false);
        var k = HeroKit(e, 340);
        var holdings = Robbery.Holdings(fx.Db, e, k.Carried, 70000, Values(e)).Select(i => i.Id == k.Item["giant_gambeson"] ? i with { MadeForCm3 = 120000 } : i).ToList();
        var robbers = new[] { new Robber(1250, 70000), new Robber(1250, 70000) };
        var plan = Robbery.Plan(R, 340, needsDoctor: false, holdings, resisted: false, robbers);
        Assert.Equal(340, plan.CoinTaken); Assert.Equal(0, plan.CoinLeft);
        var taken = plan.Taken.Select(i => i.Id).ToHashSet();
        Assert.Contains(k.Item["sword"], taken);                              // every weapon
        Assert.Contains(k.Item["mail"], taken);                               // armour that fits a bandit
        Assert.DoesNotContain(k.Item["giant_gambeson"], taken);               // it does not fit them and is worth less than 400
        Assert.DoesNotContain(k.Item["dagger"], taken);                       // a dagger is left (the victim did not fight to the end)
        Assert.DoesNotContain(k.Item["shirt"], taken); Assert.DoesNotContain(k.Item["boots"], taken);   // the under layer and boots
        Assert.Contains(plan.Left, i => i.Kind == LootKind.Food && i.MassG == R.D.FoodLeftG);           // a day's food
        Assert.Contains(plan.Taken, i => i.Good == e.Goods.IdOf("cloth_wool"));
        // a wound that needs a doctor: 5% left, at least 20 mites
        Assert.Equal(20, Robbery.Plan(R, 340, true, holdings, false, robbers).CoinLeft);
        Assert.Equal(500, Robbery.Plan(R, 10_000, true, holdings, false, robbers).CoinLeft);
        // fought to the end: the dagger goes too
        Assert.Contains(Robbery.Plan(R, 340, false, holdings, true, robbers).Taken, i => i.Id == k.Item["dagger"]);
        // carrying capacity: one weak robber cannot take the mail after the sword
        var weak = Robbery.Plan(R, 340, false, holdings, false, [new Robber(100, 70000)]);
        Assert.DoesNotContain(weak.Taken, i => i.Id == k.Item["mail"]);

        // carried out on the records: nothing created or destroyed
        long massBefore = Robbery.ItemMassG(e); var audit = e.Ledger.Audit();
        var band = e.Ledger.Open(PurseKind.Household, AftermathCoin.BandId(9));
        var moved = Robbery.Apply(e, k.Carried, k.Purse, band, AftermathCoin.BandId(9), plan);
        Assert.Equal(0, e.Ledger.Balance(k.Purse).Mites); Assert.Equal(340, e.Ledger.Balance(band).Mites);
        Assert.True(e.Ledger.Audit().IsConserved); Assert.Equal(audit.InPurses, e.Ledger.Audit().InPurses);
        Assert.Equal(massBefore, Robbery.ItemMassG(e));
        Assert.All(moved, id => Assert.Equal(AftermathCoin.BandId(9), e.Items.Get(id).Owner));
        Assert.Equal(Hero, e.Items.Get(k.Item["shirt"]).Owner);
        Assert.False(k.Carried.Equip.ContainsKey(k.Item["sword"]));
        Assert.True(e.MassAuditHolds());
    }

    // ---------------------------------------------------------------- MC-8

    [Fact]
    public void MC8_Mercy_HalfOfTheBleedingRobbedGetAWoundPressed()
    {
        int n = 10_000, pressed = 0;
        var robbers = new[] { new Robber(FirstAid: true), new Robber(FirstAid: true) };
        for (int s = 0; s < n; s++) if (DefeatTable.Mercy(R, (ulong)s + 1, 77, Hero, bleeding: true, robbers)) pressed++;
        output.WriteLine($"mercy {100.0 * pressed / n:0.00}%");
        Assert.InRange(100.0 * pressed / n, 48.5, 51.5);
        Assert.False(DefeatTable.Mercy(R, 1, 77, Hero, bleeding: false, robbers));
        Assert.False(DefeatTable.Mercy(R, 1, 77, Hero, bleeding: true, [new Robber()]));
    }

    // ---------------------------------------------------------------- MC-9

    [Fact]
    public void MC9_ArmyCapture_RansomByRank_PaidFromTheFactionTreasuryAtTheDueDay_UnpaidHalvesAndQuarters_NoCoinCreated()
    {
        Assert.Equal(1300, Ransom.ForRank(R, CaptiveRank.Levy)); Assert.Equal(4000, Ransom.ForRank(R, CaptiveRank.ManAtArms));
        Assert.Equal(20000, Ransom.ForRank(R, CaptiveRank.Knight)); Assert.Equal(100000, Ransom.ForRank(R, CaptiveRank.Baron));
        Assert.Equal(6000, Ransom.ForRank(R, CaptiveRank.ManAtArms, 500));
        Assert.Equal(1500, Ransom.ForWanderer(R, 300)); Assert.Equal(4400, Ransom.ForWanderer(R, 2000, 4000));

        var l = new Ledger();
        var faction = EntityId.Make(IdKind.Faction, 3);
        var treasury = l.Open(PurseKind.Treasury, faction);
        l.Mint(treasury, Coin.FromMites(50_000), SourceReason.Seed);
        var det = AftermathCoin.DetachmentId(1);
        var chest = l.Open(PurseKind.Chest, det);
        var minted = l.Audit().Sourced;
        var book = new CaptiveBook(5);
        var levy = EntityId.Make(IdKind.Person, 500);
        long sec0 = 100L * 86400 + 10 * 3600;
        var c = book.Capture(R, CaptorKind.Army, levy, false, det, new PurseRef(PurseKind.Chest, det), sec0, CaptiveRank.Levy, Ransom.ForRank(R, CaptiveRank.Levy),
            PayerKind.Faction, [new PurseRef(PurseKind.Treasury, faction)], [], (0, 0, 0, 50, 0));
        Assert.Equal(Restraint.Caged, c.Restraint); Assert.Equal(CareLevel.Basic, c.Care); Assert.Equal(106, c.DueDay);
        c.NextTransferDay = 1000;                                            // no transfer escape in this check
        for (long h = sec0 / 3600; h < 106 * 24; h++) book.StepHour(R, l, h);
        Assert.Equal(CaptiveState.Held, c.State);                            // not before the due day
        book.StepHour(R, l, 106 * 24);
        Assert.Equal(CaptiveState.Ransomed, c.State);
        Assert.Equal(50_000 - 1300, l.Balance(treasury).Mites); Assert.Equal(1300, l.Balance(chest).Mites);
        Assert.Equal(minted, l.Audit().Sourced); Assert.True(l.Audit().IsConserved);

        // an empty treasury: the ransom is halved 5 days past the due day and quartered after 10
        var l2 = new Ledger(); var poor = EntityId.Make(IdKind.Faction, 4); l2.Open(PurseKind.Treasury, poor); l2.Open(PurseKind.Chest, det);
        var b2 = new CaptiveBook(5);
        var c2 = b2.Capture(R, CaptorKind.Army, levy, false, det, new PurseRef(PurseKind.Chest, det), sec0, CaptiveRank.Knight, 20000, PayerKind.Faction, [new PurseRef(PurseKind.Treasury, poor)], [], (0, 0, 0, 50, 0));
        c2.NextTransferDay = 1000;
        long Ask(int day) { for (long h = b2.Captives.Values.First().LastDay * 24L + 1; h <= day * 24L; h++) b2.StepHour(R, l2, h); return c2.RansomMites; }
        Assert.Equal(106, c2.DueDay);
        Assert.Equal(20000, Ask(110)); Assert.Equal(10000, Ask(111)); Assert.Equal(10000, Ask(115)); Assert.Equal(5000, Ask(116));
        Assert.Equal(CaptiveState.Held, c2.State);
        Assert.Equal(0, l2.Audit().InPurses.Mites);
    }

    // ---------------------------------------------------------------- MC-10

    [Fact]
    public void MC10_EscapeOdds_Bonds50AtPar_Lock70AtSkill50Against30_Notice25AtStealth0()
    {
        Assert.Equal(500, EscapeTable.BondsPm(R, 40, 40));
        Assert.Equal(700, EscapeTable.LockPm(R, 50, CaptorKind.Bandits, false));
        Assert.Equal(250, EscapeTable.NoticePm(R, 0, false));
        Assert.Equal(125, EscapeTable.NoticePm(R, 0, true));
        const int N = 10_000;
        int bonds = 0, locks = 0, caught = 0;
        long day = 50L * 86400 + 12 * 3600;                                  // noon: the guards are all awake
        for (int s = 0; s < N; s++)
        {
            var b = new CaptiveBook((ulong)s + 11);
            var p = EntityId.Make(IdKind.Person, 900);
            var c = b.Capture(R, CaptorKind.Bandits, p, true, AftermathCoin.BandId(1), default, day, CaptiveRank.Wanderer, 1500, PayerKind.None, [], [], (40, 40, 50, 50, 0));
            c.Guards = 0;                                                    // the skill alone
            if (b.Attempt(R, p, CaptiveVerb.WorkBonds, day).Ok) bonds++;
            var b2 = new CaptiveBook((ulong)s + 11);
            var c2 = b2.Capture(R, CaptorKind.Bandits, p, true, AftermathCoin.BandId(1), default, day, CaptiveRank.Wanderer, 1500, PayerKind.None, [], [], (0, 0, 50, 50, 0));
            c2.Restraint = Restraint.Chained; c2.HasPick = true; c2.Guards = 0;
            if (b2.Attempt(R, p, CaptiveVerb.PickLock, day).Ok) locks++;
            var b3 = new CaptiveBook((ulong)s + 11);
            var c3 = b3.Capture(R, CaptorKind.Bandits, p, true, AftermathCoin.BandId(1), default, day, CaptiveRank.Wanderer, 1500, PayerKind.None, [], [], (0, 0, 0, 50, 0));
            if (b3.Attempt(R, p, CaptiveVerb.WorkBonds, day).Caught) { caught++; Assert.Equal(Restraint.Chained, c3.Restraint); }
        }
        output.WriteLine($"bonds {100.0 * bonds / N:0.0}% lock {100.0 * locks / N:0.0}% notice {100.0 * caught / N:0.0}%");
        Assert.InRange(100.0 * bonds / N, 48.5, 51.5);
        Assert.InRange(100.0 * locks / N, 68.5, 71.5);
        Assert.InRange(100.0 * caught / N, 23.5, 26.5);
    }

    // ---------------------------------------------------------------- MC-11

    [Fact]
    public void MC11_AFreeCompanionReachesTheCell_AndFreesTheHero_WithTheControlSwitch()
    {
        var hero = Hero; var companion = EntityId.Make(IdKind.Person, 42);
        long night = 30L * 86400 + 23 * 3600;
        RescueOutcome? win = null; ulong winSeed = 0;
        for (ulong s = 1; s <= 40 && win == null; s++)
        {
            var b = new CaptiveBook(s);
            var c = b.Capture(R, CaptorKind.Bandits, hero, true, AftermathCoin.BandId(2), default, night - 3600, CaptiveRank.Wanderer, 1500, PayerKind.None, [], [], (10, 10, 10, 40, 0), rescueMembers: 1);
            Assert.False(b.TakeControl(hero));                               // the hero cannot rescue himself
            Assert.True(b.TakeControl(companion));
            Assert.Equal(companion, b.Controlled);
            var r = RescueRun.Run(R, s, c, companion, 50, 40, 60_000, 0, [new GuardPost(4000, 3000), new GuardPost(-5000, 2000)], night);
            if (!r.Freed) continue;
            Assert.True(b.Rescued(hero, night + r.Seconds));
            Assert.Equal(CaptiveState.Rescued, c.State);
            Assert.Equal(EntityId.None, b.Controlled);                       // control comes back to the hero
            win = r; winSeed = s;
        }
        Assert.NotNull(win);
        output.WriteLine($"seed {winSeed}: freed after {win!.Seconds} s ({win.Text})");
        // the same seed is the same rescue
        var again = new CaptiveBook(winSeed);
        var ca = again.Capture(R, CaptorKind.Bandits, hero, true, AftermathCoin.BandId(2), default, night - 3600, CaptiveRank.Wanderer, 1500, PayerKind.None, [], [], (10, 10, 10, 40, 0));
        Assert.Equal(win, RescueRun.Run(R, winSeed, ca, companion, 50, 40, 60_000, 0, [new GuardPost(4000, 3000), new GuardPost(-5000, 2000)], night));
        // a locked cell needs the lock picked
        var lk = new CaptiveBook(3);
        var cl = lk.Capture(R, CaptorKind.Army, hero, true, AftermathCoin.DetachmentId(2), default, night, CaptiveRank.Wanderer, 1500, PayerKind.None, [], [], (10, 10, 10, 40, 0));
        var rl = RescueRun.Run(R, 3, cl, companion, 100, 90, 2000, 0, [], night);
        Assert.True(rl.Freed); Assert.Equal("MC_RESCUE_PICKED", rl.Text);
    }

    // ---------------------------------------------------------------- MC-12 and MC-13

    private WorldState World(EconomyState e, HeroRecord? hero = null)
    {
        var b = EconFixture.Shared;
        return new WorldState
        {
            ContinentSettings = b.Cs, HistorySettings = b.Hs, RegionSettings = b.Rs, EmbarkCellX = b.CellX, EmbarkCellY = b.CellY, Continent = b.Continent, History = b.History,
            Population = b.Pop, Clock = new SimClock(GameTime.FromCalendar(b.Pop.NowYear, Season.Summer, 3, 14, 20)), Ids = new IdAllocator(), Chunks = new ChunkStore(),
            Chronicle = new Chronicle(), Economy = e, Hero = hero, Wounds = new WoundLedger(7),
        };
    }

    private static HeroRecord HeroRec(int lockpicking = 50, int stealth = 30)
    {
        var skills = HeroRecord.SkillIds.ToDictionary(s => s, _ => 0, StringComparer.Ordinal);
        skills["lockpicking"] = lockpicking; skills["stealth"] = stealth; skills["athletics"] = 30; skills["speech"] = 20;
        return new HeroRecord { Name = "Bot", Origin = "test", Attributes = [50, 50, 50, 50, 50], Skills = skills };
    }

    [Theory]
    [InlineData(WinnerKind.Bandits)]
    [InlineData(WinnerKind.Army)]
    [InlineData(WinnerKind.Locals)]
    [InlineData(WinnerKind.Guards)]
    public void MC12_ConservationAcrossADefeat_CoinItemsByMassAndPersons(WinnerKind winner)
    {
        var e = EconFixture.NewEconomy(goods: false);
        var k = HeroKit(e, 5000);
        var state = World(e, HeroRec());
        var party = new HeroParty();
        foreach (var (id, slot) in k.Carried.Equip) party.Carried.Equip[id] = slot;
        var pack = party.Carried.Pack(e);
        long persons = state.Population.People.Count, mass = Robbery.ItemMassG(e);
        var audit = e.Ledger.Audit(); long goods = pack.TotalMassG();
        var req = new DefeatRequest(31, winner, LoserState.KnockedOut, false, false, true, true, [new Robber(1250, 70000, true), new Robber(1250, 70000, true)], 1000, 2000);
        var rep = AftermathDesk.Defeat(state, fx.Db, party, k.Purse, req, 200L * 86400);
        output.WriteLine($"{winner}: {rep.Kind}, coin taken {rep.CoinTaken}, items {string.Join(",", rep.ItemsTaken)}");
        Assert.Equal(persons, state.Population.People.Count);
        Assert.Equal(mass, Robbery.ItemMassG(e));
        Assert.True(e.Ledger.Audit().IsConserved); Assert.Equal(audit.InPurses, e.Ledger.Audit().InPurses); Assert.Equal(audit.Sourced, e.Ledger.Audit().Sourced);
        Assert.True(e.MassAuditHolds());
        var stash = e.OtherStores.GetValueOrDefault(winner == WinnerKind.Army ? AftermathCoin.DetachmentId(31) : AftermathCoin.BandId(31));
        Assert.Equal(goods, pack.TotalMassG() + (stash?.TotalMassG() ?? 0));
        switch (winner)
        {
            case WinnerKind.Bandits:
                Assert.True(rep.Kind is DefeatKind.Robbed or DefeatKind.Captured or DefeatKind.Killed);
                Assert.Equal(rep.Kind == DefeatKind.Killed ? 0 : 5000, rep.CoinTaken);
                break;
            case WinnerKind.Army:
                Assert.Equal(DefeatKind.Captured, rep.Kind);
                Assert.Equal(5000, e.Ledger.Balance(k.Purse).Mites);                       // the purse stays (it pays the ransom)
                Assert.True(state.Captivity!.HeroHeld); Assert.Equal(Restraint.Caged, state.Captivity.Hero!.Restraint);
                Assert.Contains(k.Item["sword"], state.Captivity.Hero.StashItems);
                break;
            case WinnerKind.Locals: Assert.Equal(DefeatKind.Robbed, rep.Kind); Assert.Equal(5000, rep.CoinTaken); Assert.Empty(rep.ItemsTaken); break;
            case WinnerKind.Guards:                                                  // P6-C1: an arrest, not a fine and release (CJ-5)
                Assert.Equal(DefeatKind.Captured, rep.Kind); Assert.Equal(0, rep.Fine); Assert.Equal(5000, e.Ledger.Balance(k.Purse).Mites);
                Assert.True(state.Captivity!.HeroHeld); Assert.Equal(CaptorKind.Gaol, state.Captivity.Hero!.Captor); Assert.Equal(Restraint.Bound, state.Captivity.Hero.Restraint);
                Assert.True(state.Justice!.Gaol.ContainsKey(party.Hero.Value)); Assert.Contains(state.Law!.Crimes.Values, c => c.Kind == "assault" && c.State == Ravensguard.Sim.Law.CrimeState.Held);
                break;
        }
    }

    [Fact]
    public void MC13_TimeLimits_BanditsReleaseByDay10_ArmiesByTheCampaignsEnd()
    {
        var l = new Ledger();
        int released = 0, sold = 0, ransomed = 0;
        for (ulong s = 1; s <= 60; s++)
        {
            var b = new CaptiveBook(s);
            var p = EntityId.Make(IdKind.Person, 700 + s);
            var c = b.Capture(R, CaptorKind.Bandits, p, false, AftermathCoin.BandId(s), new PurseRef(PurseKind.Household, AftermathCoin.BandId(s)), 10L * 86400, CaptiveRank.Wanderer, 1500,
                PayerKind.None, [], [], (0, 0, 0, 50, 0));
            c.NextTransferDay = 100_000;                                     // no transfer escape: the limit decides
            for (long h = 10 * 24 + 1; h <= 20 * 24 && c.Held; h++) b.StepHour(R, l, h);
            Assert.False(c.Held, $"seed {s} still held");
            Assert.True(c.EndedSec <= 20L * 86400);
            if (c.Note == "sold") sold++; else if (c.State == CaptiveState.Ransomed) ransomed++; else released++;
        }
        output.WriteLine($"released {released}, sold {sold}, ransomed {ransomed}");
        Assert.True(sold > 0 && released > 0);
        var ba = new CaptiveBook(9);
        var pa = EntityId.Make(IdKind.Person, 800);
        var ca = ba.Capture(R, CaptorKind.Army, pa, false, AftermathCoin.DetachmentId(9), new PurseRef(PurseKind.Chest, AftermathCoin.DetachmentId(9)), 10L * 86400, CaptiveRank.Levy, 1300,
            PayerKind.None, [], [], (0, 0, 0, 50, 0));
        ca.NextTransferDay = 100_000;
        for (long h = 10 * 24 + 1; h < 50 * 24; h++) ba.StepHour(R, l, h);
        Assert.True(ca.Held);                                                // the campaign holds it 40 days
        ba.StepHour(R, l, 50 * 24);
        Assert.Equal(CaptiveState.Released, ca.State); Assert.Equal("exchanged", ca.Note);
    }

    // ---------------------------------------------------------------- OV-4

    [Fact]
    public void OV4_ABotKnockedOutByAnArmyDetachment_IsCaptured_PicksTheLockAtSkill50_AndReachesTheRoad_CoinAndItemsConserved()
    {
        // the fight: the bot (driven by the brain, as the autoplay bot is) against an army detachment of soldiers, seeds until it is knocked out (not killed)
        var kitRow = FightSetup.KitDef(fx.C, "soldier", "soldier");
        FightWorld? fight = null;
        for (ulong s = 1; s <= 60 && fight == null; s++)
        {
            var w = new FightWorld(fx.C, s, 4000 + s, 8 * 3600);
            var bot = w.Add(new FighterKit { Name = "Bot", Team = 0, Person = Hero.Value, Weapon = new WeaponInstance("dagger", "iron"), Worn = [new("shirt", "linen")], Skill = 25, Courage = 70 });
            for (int i = 0; i < 4; i++) w.Add(FightSetup.Roll(fx.C, kitRow, s, 9000 + (ulong)i, $"Soldier {i + 1}", 1, 3000 + 600 * i, (i - 2) * 1200, -FightMath.One, 0));
            w.SetSide(1, Ravensguard.Sim.Combat.Morale.SideKind.Army);
            bot.Player = true;                                               // it never routs or yields by morale (the hero's rule), and the brain drives it (watch mode's contract)
            for (int t = 0; t < 20 * 120 && !w.IsOver; t++) { foreach (var f in w.Fighters) CombatBrain.Think(w, f); w.Step(); }
            if (bot.Act == FAct.Down && !bot.Book.Dead) fight = w;
        }
        Assert.NotNull(fight);
        var req = DefeatRequest.Of(fight!, 0, WinnerKind.Army, placeX: 123_000, placeY: 456_000);
        Assert.Equal(LoserState.KnockedOut, req.State);

        var e = EconFixture.NewEconomy(goods: false);
        var k = HeroKit(e, 800);
        var state = World(e, HeroRec(lockpicking: 50, stealth: 30));
        var party = new HeroParty();
        foreach (var (id, slot) in k.Carried.Equip) party.Carried.Equip[id] = slot;
        long mass = Robbery.ItemMassG(e); var audit = e.Ledger.Audit();
        long sec = 300L * 86400 + 21 * 3600;                                // taken at nine in the evening
        var rep = AftermathDesk.Defeat(state, fx.Db, party, k.Purse, req, sec);
        Assert.Equal(DefeatKind.Captured, rep.Kind);
        var book = state.Captivity!;
        var c = book.Hero!;
        Assert.Equal(Restraint.Caged, c.Restraint); Assert.Equal(50, c.Lockpicking);
        Assert.Equal(50, EscapeTable.LockDifficulty(R, CaptorKind.Army, false));
        // the bot: find something to pick the lock with, then work the cage's lock an hour at a time until it opens
        int tries = 0;
        for (; tries < 400 && c.Held; tries++)
        {
            sec += 3600;
            book.StepHour(R, e.Ledger, sec / 3600);
            if (!c.Held) break;
            var r = c.HasPick ? book.Attempt(R, Hero, CaptiveVerb.PickLock, sec, state.Wounds!.Find(Hero.Value), e.Ledger) : book.Attempt(R, Hero, CaptiveVerb.Search, sec);
            if (r.Ok && r.Verb == CaptiveVerb.PickLock) Assert.Equal("MC_PICKED_LOCK", r.Text);
        }
        output.WriteLine($"escaped after {tries} hours ({c.EscapeAttempts} attempts), state {c.State} {c.Note}");
        Assert.Equal(CaptiveState.Escaped, c.State);
        Assert.Equal("lock", c.Note);
        // it reaches the road where it was taken
        Assert.Equal((123_000L, 456_000L), (c.PlaceX, c.PlaceY));
        book.Publish(R, e.Ledger, sec);
        Assert.Equal(CaptiveState.Escaped, book.View!.State);
        Assert.Equal((123_000L, 456_000L), (book.View.PlaceX, book.View.PlaceY));
        // coin and items conserved: the purse is the bot's, the gear is in the detachment's stash, nothing made or lost
        Assert.Equal(800, e.Ledger.Balance(k.Purse).Mites);
        Assert.True(e.Ledger.Audit().IsConserved); Assert.Equal(audit.InPurses, e.Ledger.Audit().InPurses);
        Assert.Equal(mass, Robbery.ItemMassG(e));
        Assert.All(c.StashItems, id => Assert.Equal(AftermathCoin.DetachmentId(req.FightId), e.Items.Get(id).Owner));
    }

    // ---------------------------------------------------------------- the defeat table, saving

    [Fact]
    public void TheDefeatTable_ByWinner()
    {
        LoserFacts F(LoserState st = LoserState.KnockedOut, bool wounded = true, bool lone = true, long ransom = 1500, long payable = 0, LoserRole role = LoserRole.Hero, bool ally = false) =>
            new(Hero, role, st, wounded, false, lone, ransom, payable, ally);
        var robber = R.Styles.First(s => s.Id == "robber"); var kidnapper = R.Styles.First(s => s.Id == "kidnapper"); var cruel = R.Styles.First(s => s.Id == "cruel");
        Assert.Equal(DefeatKind.Robbed, DefeatTable.Decide(R, 1, 1, WinnerKind.Bandits, robber, F()).Kind);
        Assert.Equal(DefeatKind.Captured, DefeatTable.Decide(R, 1, 1, WinnerKind.Bandits, kidnapper, F()).Kind);
        Assert.Equal(DefeatKind.Captured, DefeatTable.Decide(R, 1, 1, WinnerKind.Bandits, robber, F(payable: 2000)).Kind);
        Assert.Equal(DefeatKind.Left, DefeatTable.Decide(R, 1, 1, WinnerKind.Bandits, robber, F(role: LoserRole.Hireling)).Kind);
        int killed = 0;
        for (ulong f = 0; f < 2000; f++) if (DefeatTable.Decide(R, 1, f, WinnerKind.Bandits, cruel, F()).Kind == DefeatKind.Killed) killed++;
        Assert.InRange(killed, 900, 1100);                                   // a cruel band kills a wounded lone victim with 50%
        Assert.Equal(DefeatKind.Captured, DefeatTable.Decide(R, 1, 1, WinnerKind.Army, null, F()).Kind);
        Assert.Equal(DefeatKind.Left, DefeatTable.Decide(R, 1, 1, WinnerKind.Army, null, F(st: LoserState.Standing, role: LoserRole.Soldier)).Kind);
        Assert.Equal(DefeatKind.Killed, DefeatTable.Decide(R, 1, 1, WinnerKind.Draugr, null, F()).Kind);
        Assert.Equal(DefeatKind.Mauled, DefeatTable.Decide(R, 1, 1, WinnerKind.Beasts, null, F(ally: true)).Kind);
        Assert.Equal(DefeatKind.Captured, DefeatTable.Decide(R, 1, 1, WinnerKind.Guards, null, F()).Kind);       // P6-C1: guards arrest
        Assert.Equal(WinnerKind.Army, R.WinnerOf("soldier")); Assert.Equal(WinnerKind.Bandits, R.WinnerOf("bandit")); Assert.Equal(WinnerKind.Beasts, R.WinnerOf("wolf"));
        // the band's style is one per band: 70 / 15 / 15
        var counts = new Dictionary<string, int>();
        for (ulong f = 0; f < 4000; f++) { var s = R.StyleOf(9, f).Id; counts[s] = counts.GetValueOrDefault(s) + 1; }
        Assert.InRange(counts["robber"], 2650, 2950); Assert.InRange(counts["cruel"], 480, 720); Assert.InRange(counts["kidnapper"], 480, 720);
        // the request travels in the command's tag
        var req = new DefeatRequest(77, WinnerKind.Bandits, LoserState.Dying, true, true, false, true, [new Robber(1300, 71000, true)], -5, 9);
        Assert.Equal(req.Encode(), DefeatRequest.Decode(req.Encode())!.Encode());
    }

    [Fact]
    public void TheCaptivitySection_SaveLoadSave_IsByteIdentical_AndContinuesTheSame()
    {
        var b = new CaptiveBook(21);
        var p = EntityId.Make(IdKind.Person, 1);
        var c = b.Capture(R, CaptorKind.Bandits, Hero, true, AftermathCoin.BandId(5), new PurseRef(PurseKind.Household, AftermathCoin.BandId(5)), 40L * 86400, CaptiveRank.Wanderer, 1500,
            PayerKind.Friends, [new PurseRef(PurseKind.Household, EntityId.Make(IdKind.Household, 3))], [EntityId.Make(IdKind.Item, 9)], (20, 30, 40, 50, 10), rescueMembers: 1);
        b.Capture(R, CaptorKind.Army, p, false, AftermathCoin.DetachmentId(5), new PurseRef(PurseKind.Chest, AftermathCoin.DetachmentId(5)), 40L * 86400, CaptiveRank.Levy, 1300, PayerKind.None, [], [], (0, 0, 0, 50, 0));
        b.Attempt(R, Hero, CaptiveVerb.Search, 40L * 86400 + 7200);
        b.TakeControl(EntityId.Make(IdKind.Person, 77));
        var bytes = SaveCodec.SerializeCompressed(CaptivitySave.Capture(b));
        var back = SaveCodec.DeserializeCompressed<CaptivitySave>(bytes).Restore();
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(CaptivitySave.Capture(back)));
        Assert.Equal(b.Hash(), back.Hash());
        var l = new Ledger();
        for (long h = 40 * 24 + 1; h < 60 * 24; h++) { b.StepHour(R, l, h); back.StepHour(R, l, h); }
        Assert.Equal(b.Hash(), back.Hash());
        Assert.Equal(c.State, back.Find(Hero)!.State);
    }
}
