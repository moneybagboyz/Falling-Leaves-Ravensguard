using System.Collections.Concurrent;
using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Combat.Flavour;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Trade;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Review;

/// <summary>
/// The Phase 4 code and bug check (docs/reviews/phase-4.md): a regression test per simulation finding that can have one (<c>F1</c>...), and the save -> load -> save identity of a populated
/// world that holds every Phase 4 section at once.
/// </summary>
public class Phase4ReviewTests
{
    private static FightContent Fc => _fc ??= new FightContent(new CombatTables(EconFixture.Content), EconFixture.Content);
    private static FightContent? _fc;

    private static FighterKit Kit(string name, int team, int x, int y, ulong person) => new()
    {
        Name = name, Team = team, X = x, Y = y, Person = person, Weapon = new WeaponInstance("arming_sword", "steel"), Skill = 45, Worn = [new WornItem("tunic", "linen"), new WornItem("gambeson", "linen")],
        FaceX = team == 0 ? FightMath.One : -FightMath.One, FaceY = 0,
    };

    /// <summary>A five-a-side fight whose fighters' books are the ledger's (people with ids), as a road fight in the live bubble is.</summary>
    private static FightWorld LedgerFight(WoundLedger ledger, ulong seed)
    {
        var w = new FightWorld(Fc, seed, 77, 8 * 3600, ledger);
        for (int i = 0; i < 5; i++)
        {
            w.Add(Kit("A" + i, 0, 0, i * 1300, 0x1000 + (ulong)i));
            w.Add(Kit("B" + i, 1, 2600, i * 1300, 0x2000 + (ulong)i));
        }
        return w;
    }

    // ------------------------------------------------------------------ F1: the live fight and the tick worker

    [Fact]
    public void F1_a_save_on_the_tick_worker_reads_whole_ticks_of_a_live_fight_and_its_books()
    {
        var ledger = new WoundLedger(9, 8 * 3600);
        var fight = LedgerFight(ledger, 9);
        var st = EconFixture.WorldWith(null);
        st.Wounds = ledger;
        st.FightSource = () => FightRecord.Capture(fight, "", "review");
        var hashes = new ConcurrentDictionary<int, ulong>();
        hashes[fight.Tick] = fight.Hash();
        var taken = new ConcurrentQueue<(FightRecord Fight, WoundSave? Wounds)>();
        Exception? fault = null;
        using var done = new ManualResetEventSlim();
        var saver = new Thread(() =>
        {
            try
            {
                while (!done.IsSet)
                {
                    var t = WorldSave.CaptureLiveTier(st);                     // what a save does on the worker at the barrier
                    if (t.Fight != null) taken.Enqueue((t.Fight, t.Wounds));
                }
            }
            catch (Exception e) { fault = e; }
        }) { IsBackground = true };
        saver.Start();
        for (int i = 0; i < 1200 && !fight.IsOver; i++)
        {
            lock (ledger.Sync) { ledger.AdvanceTo(fight.GameSec); fight.StepShared(); }   // the live tick's wounds and fight step (main thread, as LiveWorld.Step)
            hashes[fight.Tick] = fight.Hash();
        }
        done.Set(); saver.Join();
        Assert.Null(fault);
        Assert.True(taken.Count > 20, $"only {taken.Count} captures");
        int checkedN = 0;
        foreach (var (rec, ws) in taken)
        {
            var back = rec.Restore(Fc, ws?.Restore() ?? new WoundLedger(9, 8 * 3600));
            Assert.True(hashes.TryGetValue(rec.Tick, out var h), $"tick {rec.Tick} was never reached");
            Assert.True(h == back.Hash(), $"the capture at tick {rec.Tick} is not the fight of that tick (torn between the fight, its books and the live step)");
            checkedN++;
        }
        Assert.True(checkedN > 20);
    }

    [Fact]
    public void F1_the_live_step_holds_the_ledgers_lock_so_the_hourly_wound_step_waits_for_it()
    {
        var ledger = new WoundLedger(3, 8 * 3600);
        var fight = LedgerFight(ledger, 3);
        bool heldDuringStep = false;
        fight.OnEvent += _ => heldDuringStep |= Monitor.IsEntered(ledger.Sync);
        for (int i = 0; i < 400 && !fight.IsOver; i++) fight.StepShared();
        Assert.True(heldDuringStep, "the fight stepped its ledger books without the ledger's lock");
        var own = new FightWorld(Fc, 3, 78, 8 * 3600);                        // a fight of its own books needs no lock and takes none
        own.Add(Kit("A", 0, 0, 0, 0)); own.Add(Kit("B", 1, 1600, 0, 0));
        bool held = false;
        own.OnEvent += _ => held |= Monitor.IsEntered(ledger.Sync);
        for (int i = 0; i < 100; i++) own.StepShared();
        Assert.False(held);
    }

    // ------------------------------------------------------------------ F4: a ransom from friends

    [Fact]
    public void F4_friends_of_one_household_pay_its_share_once_and_a_shortfall_moves_no_coin()
    {
        var w = new TradeWorld();
        var l = w.Econ.Ledger;
        var r = AftermathRules.For(EconFixture.Content);
        var hh = EntityId.Make(IdKind.Other, 0x4F_0001);
        var purse = l.Open(PurseKind.Household, hh);
        l.Mint(purse, new Coin(10_000), SourceReason.Seed);
        var captor = l.Open(PurseKind.Household, EntityId.Make(IdKind.Other, 0x4F_0002));
        var twice = new List<PurseRef> { new(PurseKind.Household, hh), new(PurseKind.Household, hh) };       // two companions of the same household
        long share = 10_000L * r.D.FriendSharePm / 1000;
        Assert.Equal(share, Ransom.Payable(r, l, PayerKind.Friends, twice));                                 // counted once
        long total = w.TotalCoin;
        for (int day = 0; day < 5; day++) Assert.False(Ransom.TryPay(r, l, PayerKind.Friends, twice, captor, share + 2_500));   // more than one share: refused every day
        Assert.Equal(10_000, l.Balance(purse).Mites);                                                       // before the fix each day paid part of it to the captors
        Assert.Equal(0, l.Balance(captor).Mites);
        Assert.True(Ransom.TryPay(r, l, PayerKind.Friends, twice, captor, share));
        Assert.Equal(10_000 - share, l.Balance(purse).Mites);
        Assert.Equal(total, w.TotalCoin);
    }

    // ------------------------------------------------------------------ F7: robbery's food tally

    [Fact]
    public void F7_the_robbers_plan_says_how_much_food_they_carried_off()
    {
        var r = AftermathRules.For(EconFixture.Content);
        var bread = new Ravensguard.Sim.Combat.Aftermath.LootItem(EntityId.None, "bread", LootKind.Food, 300, 3000, Good: 7, Qty: 3000);
        var plan = Robbery.Plan(r, 0, false, [bread], false, [new Robber(), new Robber()]);
        Assert.Equal(r.D.FoodLeftG, plan.FoodLeftG);                                         // a day's food is left
        Assert.Equal(3000 - r.D.FoodLeftG, plan.FoodTakenG);                                 // and the rest is taken (always 0 before)
        Assert.Equal(3000, plan.Taken.Concat(plan.Left).Where(i => i.Good == 7).Sum(i => i.MassG));
    }

    // ------------------------------------------------------------------ F3: the fighters' books stay the ledger's

    [Fact]
    public void F3_a_hurt_fighters_book_is_the_ledgers_so_its_wounds_are_saved_and_go_on_after_the_fight()
    {
        var ledger = new WoundLedger(9, 8 * 3600);
        var fight = LedgerFight(ledger, 9);
        for (int t = 0; t < 1500 && !fight.IsOver; t++) lock (ledger.Sync) { ledger.AdvanceTo(fight.GameSec); fight.StepShared(); }   // as the live tick: the ledger advances first
        var hurt = fight.Fighters.Where(f => f.Book.Records.Count > 0).ToList();
        Assert.True(hurt.Count >= 3, "nobody was hurt");
        foreach (var f in hurt) Assert.Same(f.Book, ledger.Find(f.Kit.Person));           // before the fix every one was a book the ledger had dropped at the first advance
        var saved = WoundSave.Capture(ledger).Restore();
        foreach (var f in hurt) Assert.Equal(f.Book.Hash(0), saved.Find(f.Kit.Person)!.Hash(0));   // the wounds are in the "wounds" section
        // and the ledger goes on with them after the fight: an hour later the books have moved on (bleeding, clotting, pain)
        ledger.AdvanceTo(fight.GameSec + 3600);
        Assert.All(hurt.Where(f => !f.Book.Empty), f => Assert.Equal(fight.GameSec + 3600, f.Book.LastSec));
    }

    [Fact]
    public void F3_the_dead_of_a_road_fight_leave_the_ledger_but_the_heros_body_waits_for_the_succession()
    {
        var ledger = new WoundLedger(11, 8 * 3600);
        var fight = LedgerFight(ledger, 11);
        for (int t = 0; t < 3000 && !fight.IsOver; t++) lock (ledger.Sync) { ledger.AdvanceTo(fight.GameSec); fight.StepShared(); }
        ledger.AdvanceTo(fight.GameSec + 7200);
        var dead = fight.Fighters.Where(f => f.Book.Dead).ToList();
        Assert.NotEmpty(dead);
        ulong hero = EconomyState.HeroOwner.Value;
        ledger.Books[hero] = dead[0].Book;                                                  // the hero's body lies there too
        Assert.Equal(dead.Count, WoundSystem.PruneDead(ledger, _ => false));                // travellers: no person row takes their books up
        Assert.All(dead, f => Assert.Null(ledger.Find(f.Kit.Person)));
        Assert.NotNull(ledger.Find(hero));                                                  // the game turns it into the succession (StreetView.SuccessionFrame)
        Assert.Equal(0, WoundSystem.PruneDead(ledger, _ => false));
    }

    [Fact]
    public void F3_a_hero_who_dies_held_is_no_longer_held_so_the_heir_is_free()
    {
        var w = new TradeWorld();
        var cb = AftermathDesk.BookOf(w.State, EconFixture.Content);
        cb.Capture(cb.Rules, CaptorKind.Bandits, w.State.Party!.Hero, true, EntityId.Make(IdKind.Other, 5), new PurseRef(PurseKind.Household, EntityId.Make(IdKind.Other, 5)), w.Sec, CaptiveRank.Wanderer,
            1500, PayerKind.Friends, [], [], (30, 30, 20, 40, 25));
        Assert.True(cb.HeroHeld);
        Assert.True(w.Do(SuccessionDesk.EndCommand(0, 0, w.Sec)).Ok);
        Assert.False(cb.HeroHeld);
        Assert.Equal(CaptiveState.Dead, cb.Hero!.State);
        Assert.Equal(CaptiveState.Dead, cb.View!.State);                                    // the captivity screen closes on it
    }

    // ------------------------------------------------------------------ save -> load -> save with every Phase 4 section

    [Fact]
    public void A_populated_world_with_every_phase_4_section_saves_loads_and_saves_to_the_same_bytes()
    {
        var w = new TradeWorld();
        var st = w.State;
        st.Markets = null;                                                    // this bare world has no production section to restore markets against (as HeroRuntimeTests)
        var db = EconFixture.Content;
        // wounds and a fight in progress on the ledger, with its log
        var ledger = new WoundLedger(st.Seed, w.Sec);
        st.Wounds = ledger;
        var fight = LedgerFight(ledger, 5);
        var book = new FlavourBook(db);
        var rec = FlavourRecorder.Attach(fight, null, book);
        for (int i = 0; i < 1200 && !fight.IsOver && (i < 160 || ledger.IsEmpty); i++) lock (ledger.Sync) { ledger.AdvanceTo(fight.GameSec); fight.StepShared(); }
        Assert.False(ledger.IsEmpty);
        st.FightSource = () => FightRecord.Capture(fight, "", "review");
        st.FightLog = FightLogSave.Capture(rec.Log, book);
        // captivity: the hero taken by bandits
        var cb = AftermathDesk.BookOf(st, db);
        cb.Capture(cb.Rules, CaptorKind.Bandits, st.Party!.Hero, true, EntityId.Make(IdKind.Other, 5), new PurseRef(PurseKind.Household, EntityId.Make(IdKind.Other, 5)), w.Sec, CaptiveRank.Wanderer,
            1500, PayerKind.Friends, [], [], (30, 30, 20, 40, 25), rescueMembers: 1);
        // succession: the kin; watch settings; a searched battlefield or a dug hoard
        var pop = EconFixture.Pop;
        st.Succession = new SuccessionBook();
        st.Succession.Kin.AddRange(HeroKin.Choose(pop.People, pop.Households, pop.Region.Settlements.Count, (int)(w.Sec / 86400)));
        st.Watch = new WatchBook { KeepWatching = true, SpeedCap = 2 };
        st.Watch.Set(st.Party.Hero, WatchStance.Of(Fc, StancePreset.Defensive));
        var site = pop.Region.Sites.FirstOrDefault(s => s.Kind is SiteKind.Battlefield or SiteKind.Hoard);
        if (site != null) w.Do(site.Kind == SiteKind.Battlefield ? SiteDesk.SearchCommand(site.Id.Value, w.Sec) : SiteDesk.DigCommand(site.Id.Value, w.Sec));

        var bytes = SaveCodec.Encode(WorldSave.Capture(st, EconFixture.ContentHash, "0.1.0"));
        var file = SaveCodec.Decode(bytes);
        foreach (var name in new[] { WoundSave.SectionName, FightRecord.SectionName, FightLogSave.SectionName, CaptivitySave.SectionName, SuccessionSave.SectionName, WatchSave.SectionName })
            Assert.True(file.Sections.ContainsKey(name), $"the save has no '{name}' section");
        var back = WorldSave.Restore(file, db, EconFixture.ContentHash);
        // the loaded fight is rebuilt from its record on the loaded ledger (as the game's ResumeFight does) and captured from the live fight again
        var resumed = back.Fight!.Restore(Fc, back.Wounds);
        Assert.Equal(fight.Hash(), resumed.Hash());
        back.Fight = null;
        back.FightSource = () => FightRecord.Capture(resumed, "", "review");
        var bytes2 = SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0"));
        Assert.Equal(bytes.Length, bytes2.Length);
        Assert.True(bytes.AsSpan().SequenceEqual(bytes2), "save -> load -> save changed the bytes");
        // and both go on to the same fight
        for (int i = 0; i < 200 && !fight.IsOver; i++) { fight.StepShared(); resumed.StepShared(); }
        Assert.Equal(fight.Hash(), resumed.Hash());
    }
}
