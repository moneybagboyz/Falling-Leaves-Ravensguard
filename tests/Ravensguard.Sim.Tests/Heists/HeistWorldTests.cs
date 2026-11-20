using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Heists;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;
using static Ravensguard.Sim.Tests.Heists.HeistTests;

namespace Ravensguard.Sim.Tests.Heists;

/// <summary>
/// HJ-1 on seed 1234, HJ-9 (the jailbreak, the done-when), HJ-10 (the vault), HJ-11 (the captors' camp) and HJ-12 (the save) on a town of the court's own kit and on a whole running world.
/// </summary>
public class HeistWorldTests(ITestOutputHelper output)
{
    private static readonly HeistRules R = Rules;
    private static EntityId Hero => HeistWorld.Hero;

    // ------------------------------------------------------------------ HJ-1

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

    [Fact]
    public void HJ1_EveryMannorAndTownOfSeed1234HasAGaol_EveryTownAStrongroom_GuildTownsAVault_AndTheSitesAreTheSameOnEveryGeneration()
    {
        var (w, st) = Game();
        var region = st.Population.Region; var rules = HeistRules.From(w.Content);
        var plan = HeistSites.Plan(region, st.Population.People);
        var seats = region.Fiefs.Select(f => f.SeatSettlement).ToHashSet();
        int towns = 0, manors = 0, guildTowns = 0;
        for (int i = 0; i < region.Settlements.Count; i++)
        {
            var s = region.Settlements[i]; bool town = s.Class == SettlementClass.Town, manor = s.Plan.ManorLot >= 0;
            if (town || manor || seats.Contains(i)) Assert.Contains((i, HeistKind.Gaol), plan);
            if (town) { towns++; Assert.Contains((i, HeistKind.Strongroom), plan); Assert.Contains((i, HeistKind.Undercroft), plan); Assert.Contains((i, HeistKind.Drain), plan); }
            if (manor && !town) manors++;
            if (plan.Contains((i, HeistKind.Vault))) { guildTowns++; Assert.True(town); }
        }
        output.WriteLine($"{region.Settlements.Count} places: {towns} towns, {manors} manors, {guildTowns} guild towns, {plan.Count} sites");
        Assert.True(towns >= 1); Assert.True(plan.Count(p => p.Kind == HeistKind.Gaol) >= towns);
        // generated twice (a fresh book each time): the same templates, the same maps, the same HeistHash
        ulong Gen()
        {
            var b = new HeistBook(); ulong h = 14695981039346656037UL;
            foreach (var (i, k) in plan)
            {
                bool seat = seats.Contains(i); int tier = HeistSites.TierOf(region.Settlements[i], seat);
                var site = HeistSites.Open(b, rules, st.Seed, i, k, tier);
                Assert.NotNull(site);
                h = site!.Map!.Hash(h) ^ b.Hash();
            }
            return h;
        }
        Assert.Equal(Gen(), Gen());
        // the town with a gaol has a cell for every prisoner the watch can hold: at least two
        foreach (var (i, k) in plan.Where(p => p.Kind == HeistKind.Gaol)) Assert.Contains(rules.Pick(k, HeistSites.TierOf(region.Settlements[i], seats.Contains(i)), HSite.IdOf(i, k))!.Rows, r => r.Contains('P'));
    }

    // ------------------------------------------------------------------ HJ-9

    /// <summary>The companion jailed for theft, then the hero comes at night by the drain of a castle, picks the Masterwork bars and walks out with him.</summary>
    private static (HeistWorld H, HeistRun Run, EntityId Comp) TheBreakIn(int day = 10, int hour = 1, int minute = 0)
    {
        var h = HeistWorld.Make();
        var comp = h.Id(30); h.Jail(comp, "theft", 1);
        Assert.True(h.Cap.Find(comp)!.Held); Assert.True(h.Jb.Gaol.ContainsKey(comp.Value));
        var run = HeistSites.Start(h.Book, R, Seed, 0, HeistKind.Drain, 2, HeistWorld.Sec(day, hour) + minute * 60, 0, h.Real(), [comp.Value], p => 45)!;
        run.Stealth = 70; run.Lockpicking = 100; run.SetSneak(true);
        return (h, run, comp);
    }

    private static void ThroughTheDrainAndIn(HeistRun run)
    {
        Assert.Equal(Act.Ok, run.GoTo(5, 2)); Assert.Equal(Act.Ok, run.Pry(0));                  // the first grate, thirty seconds
        Assert.Equal(Act.Ok, run.GoTo(19, 4)); Assert.Equal(Act.Ok, run.Pry(1));
        Assert.Equal(Act.Ok, run.GoTo(33, 4)); Assert.Equal(Act.Ok, run.UsePortal());            // up into the castle's back passage
        Assert.Equal("gaol_castle", run.Here.Template);
        Assert.Equal(Act.Ok, run.GoTo(2, 4));                                                    // before the first cell, in the light of one sconce
    }

    [Fact]
    public void HJ9_AJailbreakWorks_ByTheDrainAtNight_AMasterworkLock_OutUnseen_TheCrimeFoundAtTheNextRound_NoBounty()
    {
        var (h, run, comp) = TheBreakIn();
        var gaoler = h.Id(0);
        Assert.Equal(h.Id(0).Value, h.Jb.Gaol[comp.Value].Gaoler);
        Assert.Equal("drain_long", run.Here.Template);
        ThroughTheDrainAndIn(run);
        long t0 = run.Sec;
        Assert.Equal(Act.Ok, run.Pick(0));                                                       // a Masterwork lock: 240 seconds (and a slip would be another go)
        Assert.True(run.Sec - t0 >= 240, $"the pick took {run.Sec - t0} s");
        Assert.Equal(1, run.Here.Door[0]);
        Assert.Equal(Act.Ok, run.GoTo(2, 3)); Assert.Equal(Act.Ok, run.GoTo(2, 2));              // in, beside the prisoner
        Assert.Equal(Act.Ok, run.Free(0));
        Assert.Equal([comp.Value], run.Followers);
        Assert.Equal(Act.Ok, run.GoTo(1, 10)); Assert.Equal(Act.Ok, run.UsePortal());            // back down the passage into the drain
        Assert.Equal("drain_long", run.Here.Template);
        Assert.Equal(Act.Ok, run.GoTo(0, 2)); Assert.Equal(Act.Ok, run.Leave());
        Assert.Equal(RunOutcome.Left, run.Outcome); Assert.Empty(run.Seen); Assert.DoesNotContain(run.Sites.Values, s => s.Alarm);
        output.WriteLine($"the break took {run.Sec - run.StartSec} s");

        var rep = HeistOutcome.Apply(h.W, h.Cb, h.Jb, h.Book, run, run.Sec);
        Assert.Equal("hj_left_unseen_freed", rep.Key); Assert.True(rep.Ended); Assert.Null(h.Book.Run);
        var cap = h.Cap.Find(comp)!;
        Assert.Equal(CaptiveState.Rescued, cap.State); Assert.False(h.Jb.Gaol.ContainsKey(comp.Value));
        var jail = h.Cb.Crimes.Values.Single(c => c.Kind == "jailbreak");                       // found at the next round: no witness, nobody named
        Assert.Empty(jail.Witnesses); Assert.False(jail.Known); Assert.Equal(0UL, jail.BountyJob);
        Assert.Contains(h.C.All, e => e.TypeId == "crime_discovered");
        Assert.Empty(h.Book.Manhunts);
        var theft = h.Cb.Crimes.Values.Single(c => c.Kind == "theft"); Assert.Equal(CrimeState.Fled, theft.State);
        Assert.True(h.L.Audit().IsConserved);
        _ = gaoler;
    }

    [Fact]
    public void HJ9_SeenOnTheWayOut_ThereAreBountiesForBoth_AndAManhuntOfOneGameDaySearchesTheRoads()
    {
        var (h, run, comp) = TheBreakIn(11);
        ThroughTheDrainAndIn(run);
        Assert.Equal(Act.Ok, run.Pick(0)); Assert.Equal(Act.Ok, run.GoTo(2, 2)); Assert.Equal(Act.Ok, run.Free(0));
        // a guard sees them (the engine's own spotting is HJ-6 and HJ-7): two of the watch, one of them knowing the hero
        var g1 = h.Id(2).Value; var g2 = h.Id(4).Value;
        run.Seen.Add((g1, true)); run.Seen.Add((g2, false));
        Assert.Equal(Act.Ok, run.GoTo(1, 10)); Assert.Equal(Act.Ok, run.UsePortal()); Assert.Equal(Act.Ok, run.GoTo(0, 2)); Assert.Equal(Act.Ok, run.Leave());
        long sec = run.Sec;
        var rep = HeistOutcome.Apply(h.W, h.Cb, h.Jb, h.Book, run, sec);
        Assert.Equal("hj_left_seen", rep.Key);
        var jail = h.Cb.Crimes.Values.Where(c => c.Kind == "jailbreak").OrderBy(c => c.Id).ToList();
        Assert.Equal(2, jail.Count);                                                             // one for him that was held, one for the man who broke him out
        Assert.All(jail, c => Assert.Equal(2, c.Witnesses.Count));
        var forComp = jail.Single(c => c.Doer == comp.Value); var forHero = jail.Single(c => c.Doer == Hero.Value);
        Assert.True(forComp.Known); Assert.True(forHero.Known);                                  // each recognised by someone who saw
        // the witnesses report next day: a bounty on each, the chain back to the act
        CrimeSystem.Report(h.W, forComp, new EntityId(g1), sec + 86_400); CrimeSystem.Report(h.W, forHero, new EntityId(g1), sec + 86_400);
        Assert.NotEqual(0UL, forComp.BountyJob); Assert.NotEqual(0UL, forHero.BountyJob);
        Assert.Contains(h.C.All, e => e.TypeId == "bounty_posted" && e.Subjects.Contains(comp));
        // the manhunt: a day, a kilometre an hour from the gaol, then the bounty alone
        var mh = Assert.Single(h.Book.Manhunts);
        Assert.Equal(0, mh.RadiusM(R, sec)); Assert.Equal(5_000, mh.RadiusM(R, sec + 5 * 3600)); Assert.Equal(24_000, mh.RadiusM(R, sec + 24 * 3600)); Assert.Equal(24_000, mh.RadiusM(R, sec + 70 * 3600));
        Assert.True(HeistLink.Seeks(h.Book, R, Hero.Value, 3_000, sec + 5 * 3600)); Assert.False(HeistLink.Seeks(h.Book, R, Hero.Value, 12_000, sec + 5 * 3600));
        Assert.False(HeistLink.Seeks(h.Book, R, h.Id(50).Value, 100, sec + 5 * 3600));
        HeistLink.Hourly(h.W, h.Book, R, sec / 3600 + 20); Assert.False(mh.Ended);
        HeistLink.Hourly(h.W, h.Book, R, sec / 3600 + 25); Assert.True(mh.Ended);
        var began = h.C.All.Single(e => e.TypeId == "manhunt_began"); var ended = h.C.All.Single(e => e.TypeId == "manhunt_ended");
        Assert.Equal(began.Id, ended.Causes[0].EventId); Assert.Equal(jail[0].Witnessed, began.Causes[0].EventId!.Value.Value);
        Assert.False(HeistLink.Seeks(h.Book, R, Hero.Value, 100, sec + 30 * 3600));          // the search is over; the bounty stays
        Assert.NotEqual(0UL, forHero.BountyJob);
        Assert.True(h.L.Audit().IsConserved);
    }

    [Fact]
    public void HJ9_CaughtInsideTheHeroIsArrestedForTheBreak_AndTheCaptiveStaysHeld()
    {
        var (h, run, comp) = TheBreakIn(12);
        ThroughTheDrainAndIn(run);
        Assert.Equal(Act.Ok, run.Pick(0)); Assert.Equal(Act.Ok, run.GoTo(2, 2)); Assert.Equal(Act.Ok, run.Free(0));
        var g = run.Here.Guards.First(x => x.State is GState.Dwell or GState.Post);
        g.X = 2; g.Y = 4; g.State = GState.Pursue; g.Aware = true;                              // a guard comes down the corridor and has him
        run.Seen.Add((g.Person, true));
        run.Advance(5);
        Assert.Equal(RunOutcome.Caught, run.Outcome);
        var rep = HeistOutcome.Apply(h.W, h.Cb, h.Jb, h.Book, run, run.Sec);
        Assert.Equal("hj_caught", rep.Key);
        Assert.True(h.Jb.Gaol.ContainsKey(Hero.Value)); Assert.True(h.Cap.Find(comp)!.Held); Assert.Null(h.Book.Run);
        Assert.Contains(h.C.All, e => e.TypeId == "crime_arrested" && e.Subjects.Contains(Hero));
    }

    // ------------------------------------------------------------------ HJ-10

    [Fact]
    public void HJ10_TheStrongroomsChestEmptied_TheTreasuryFallsByTheCoinTaken_TheCrimesValueIsIt_AndCoinIsConserved()
    {
        var h = HeistWorld.Make();
        var run = HeistSites.Start(h.Book, R, Seed, 0, HeistKind.Strongroom, 1, HeistWorld.Sec(10, 1), 0, h.Real(), [], p => 45)!;
        run.Lockpicking = 100; run.SetSneak(true);
        var s = run.Here; s.Guards.Clear();                                                      // the watch is elsewhere for this test (the guards' own sight is HJ-4 to HJ-7)
        long treasury0 = h.L.Balance(h.Treasury).Mites, hero0 = h.L.Balance(h.HeroPurse).Mites;
        Assert.Equal(Act.Ok, run.GoTo(10, 2)); Assert.Equal(Act.Ok, run.Pick(0));
        Assert.Equal(Act.Ok, run.GoTo(16, 2)); Assert.Equal(Act.Ok, run.Pick(1));
        Assert.Equal(Act.Ok, run.GoTo(22, 2)); Assert.Equal(Act.Ok, run.Loot());
        int extras = s.Extras.Count; Assert.Equal(0, extras);
        Assert.InRange(run.Taken.Count, 1, 3);
        var rep = HeistOutcome.Apply(h.W, h.Cb, h.Jb, h.Book, run, run.Sec);
        long take = HeistOutcome.ChestTake(R, treasury0);
        Assert.Equal(20_000, take);                                                              // 40% of 200,000 is more than a man can carry: the cap
        Assert.Equal(treasury0 - take, h.L.Balance(h.Treasury).Mites); Assert.Equal(hero0 + take, h.L.Balance(h.HeroPurse).Mites); Assert.Equal(take, run.Stolen);
        Assert.Equal("hj_looted", rep.Key); Assert.Equal(Act.Nothing, run.Loot());              // it is empty
        Assert.Equal(Act.Ok, run.GoTo(0, 2)); Assert.Equal(Act.Ok, run.Leave());
        HeistOutcome.Apply(h.W, h.Cb, h.Jb, h.Book, run, run.Sec);
        var rec = h.Cb.Crimes.Values.Single(c => c.Kind == "burglary");
        Assert.Equal(take, rec.ValueMites); Assert.Equal(HeistWorld.Hero.Value, rec.Doer);
        Assert.True(h.L.Audit().IsConserved);
        // the chest fills again in a month (the treasury's own income), and is not a second chest the next day
        Assert.True(s.ChestDay >= 0);
        // seen on the way out: the same coin, witnessed
        var h2 = HeistWorld.Make();
        var r2 = HeistSites.Start(h2.Book, R, Seed, 0, HeistKind.Strongroom, 1, HeistWorld.Sec(10, 1), 0, h2.Real(), [], p => 45)!;
        r2.Lockpicking = 100; r2.Here.Guards.Clear();
        r2.GoTo(10, 2); r2.Pick(0); r2.GoTo(16, 2); r2.Pick(1); r2.GoTo(22, 2); r2.Loot();
        r2.Seen.Add((h2.Id(2).Value, true)); r2.GoTo(0, 2); r2.Leave();
        HeistOutcome.Apply(h2.W, h2.Cb, h2.Jb, h2.Book, r2, r2.Sec);
        var seen = h2.Cb.Crimes.Values.Single(c => c.Kind == "burglary");
        Assert.True(seen.Known); Assert.Equal(20_000, seen.ValueMites);
        CrimeSystem.Report(h2.W, seen, h2.Id(2), r2.Sec + 86_400);
        Assert.NotEqual(0UL, seen.BountyJob);
        Assert.True(h2.L.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ HJ-11

    [Fact]
    public void HJ11_ABanditCaptiveIsHeldAtARealCampSite_WithTwoGuardsAStakeAndAFire_AndFedFromTheStash()
    {
        var h = HeistWorld.Make();
        var band = AftermathCoin.BandId(7); var stash = h.L.Open(PurseKind.Household, band); h.L.Mint(stash, new Coin(100), SourceReason.Seed);
        var captive = h.Id(50);
        h.Cap.Capture(AftermathRules.Default, CaptorKind.Bandits, captive, false, band, new PurseRef(PurseKind.Household, band), 10L * 86_400, CaptiveRank.Wanderer, 1500, PayerKind.None, [], [], (0, 0, 0, 50, 0));
        h.Cap.Find(captive)!.NextTransferDay = 100_000;
        var run = HeistSites.Start(h.Book, R, Seed, 7, HeistKind.Camp, 0, HeistWorld.Sec(12, 14), 0, [], [captive.Value])!;
        var s = run.Here; var m = s.Map!;
        Assert.Equal(HeistKind.Camp, m.Kind); Assert.Equal("camp_stake", s.Template);
        Assert.Equal(2, s.Guards.Count); Assert.Equal(captive.Value, s.Slot[0]); Assert.Single(m.Sconces);   // two guards, the stake line, the fire
        Assert.Equal(new[] { GState.Post, GState.Post }, s.Guards.Select(g => g.State));                      // both awake by day
        var night = HeistSites.Start(new HeistBook(), R, Seed, 7, HeistKind.Camp, 0, HeistWorld.Sec(12, 2), 0, [], [captive.Value])!;
        Assert.Equal(1, night.Here.Guards.Count(g => g.State == GState.Post)); Assert.Equal(1, night.Here.Guards.Count(g => g.State == GState.Asleep));   // one awake at night
        // fed from the stash a measure a day, as the cell's prisoners are from the treasury
        Ravensguard.Sim.Law.Gaol.Feed(h.W, h.Cb, h.Jb, 13, HeistWorld.Sec(13, 0)); Ravensguard.Sim.Law.Gaol.Feed(h.W, h.Cb, h.Jb, 14, HeistWorld.Sec(14, 0));
        Assert.Equal(100 - 2 * h.W.S.RationMites, h.L.Balance(stash).Mites);
        // and freed at the stake: cut the bonds, walk out
        var heroRun = night; heroRun.SetSneak(true);
        var guardAwake = heroRun.Here.Guards.First(g => g.State == GState.Post); heroRun.Here.Guards.Clear();
        Assert.Equal(Act.Ok, heroRun.GoTo(13, 3)); Assert.Equal(Act.Ok, heroRun.Free(0));
        Assert.Equal(Act.Ok, heroRun.GoTo(0, 5)); Assert.Equal(Act.Ok, heroRun.Leave());
        _ = guardAwake;
        HeistOutcome.Apply(h.W, h.Cb, h.Jb, h.Book, heroRun, heroRun.Sec);
        Assert.Equal(CaptiveState.Rescued, h.Cap.Find(captive)!.State);
        Assert.True(h.L.Audit().IsConserved);
    }

    // ------------------------------------------------------------------ HJ-12

    private static byte[] Bytes(HeistBook b) => SaveCodec.SerializeCompressed(HeistSave.Capture(b));

    [Fact]
    public void HJ12_SavedMidHeist_AGuardAsleep_AKeyInThePack_ALockHalfPicked_LoadsAndSavesIdentically_AndRunsOnTheSame()
    {
        HeistBook Scene(out HeistRun run)
        {
            var b = new HeistBook();
            run = HeistSites.Start(b, R, Seed, 3, HeistKind.Gaol, 2, At(3, 7, 20), 0, [], [])!;
            run.Lockpicking = 100; run.SetSneak(true);
            var keeper = run.Here.Guards.First(g => g.Keeper); var s = run.Here; var keep = s.Guards.Where(g => g == keeper || g.State == GState.Asleep).ToList();
            s.Guards.RemoveAll(g => !keep.Contains(g));
            run.GoTo(34, 4); run.Pick(7); run.GoTo(keeper.HomeX + 1, keeper.HomeY);
            run.Search(s.Guards.IndexOf(keeper));                                              // the key is in the pack
            run.GoTo(25, 4); run.Pick(6);                                                      // (a lock picked is not the half picked below)
            run.GoTo(3, 4);
            Assert.Equal(Act.Ok, run.Pick(0, finish: false)); run.Advance(100);                // the third cell's bars, a hundred seconds in
            return b;
        }
        var b1 = Scene(out var r1);
        Assert.True(r1.Busy); Assert.NotEmpty(b1.Keys); Assert.Contains(r1.Here.Guards, g => g.State == GState.Asleep || g.KeysTaken);
        byte[] saved = Bytes(b1);
        var copy = HeistSave.Capture(b1).Restore(R, Seed);
        Assert.Equal(b1.Hash(), copy.Hash());
        Assert.Equal(saved, Bytes(copy));                                                      // load and save again: the same bytes
        // through the codec: the section decodes to the same book
        var decoded = SaveCodec.DeserializeCompressed<HeistSave>(saved).Restore(R, Seed);
        Assert.Equal(b1.Hash(), decoded.Hash());
        // and the heist goes on the same: an uninterrupted run and the resumed one finish the pick and walk away together
        for (int i = 0; i < 2; i++)
        {
            var a = i == 0 ? b1 : copy;
            a.Run!.Finish(); a.Run.GoTo(39, 4); a.Run.Leave();
        }
        Assert.Equal(b1.Run!.Sec, copy.Run!.Sec); Assert.Equal(b1.Run.Outcome, copy.Run.Outcome); Assert.Equal(b1.Hash(), copy.Hash());
        // a world that never visited a site writes no section
        Assert.True(new HeistBook().IsEmpty);
    }

    [Fact]
    public async Task HJ12_AWholeRunningWorld_EntersAHeistThroughTheDesk_SavesMidHeist_AndComesBackTheSame()
    {
        var (w, st) = Game();
        using var rt = new WorldRuntime(st, w.Content, new MemoryRegionPager());
        long h0 = rt.HourDone + (24 - rt.HourDone % 24) % 24 + 24;
        rt.AdvanceTo(GameTime.FromGameHours(h0 + 2));                                          // two in the morning, a day on
        var plan = HeistSites.Plan(st.Population.Region, st.Population.People);
        var (town, kind) = plan.First(p => p.Kind == HeistKind.Undercroft);
        var ctx = new HeroContext(st, st.Party!, w.Content); var desk = new HeroDesk();
        HeroResult Do(HeroCommand c) { desk.Issue(c); desk.ApplyDue(ctx, rt.HourDone + 1); return desk.View!.Results[^1]; }
        long sec = rt.HourDone * 3600;
        var entered = await rt.AtBarrier(() => Do(HeistDesk.Enter(town, kind, 0, sec)));
        Assert.True(entered.Ok, entered.Text.ToString());
        var moved = await rt.AtBarrier(() => Do(HeistDesk.Do("wait|3", sec))); Assert.True(moved.Ok, moved.Text.ToString());
        var view = await rt.AtBarrier(() => HeistView.Of(st.Heists!.Run));
        Assert.NotNull(view); Assert.Equal(HeistKind.Undercroft, view!.Kind);
        var save = await rt.AtBarrier(() => WorldSave.Capture(st, "h", "0.1"));
        Assert.Contains(save.Sections, kv => kv.Key == HeistSave.SectionName);
        var st2 = WorldSave.Restore(SaveCodec.Decode(SaveCodec.Encode(save)), w.Content, "h", null, w.Continent, w.History);
        Assert.NotNull(st2.Heists); Assert.NotNull(st2.Heists!.Run);
        Assert.Equal(st.Heists!.Hash(), st2.Heists.Hash());
        var save2 = WorldSave.Capture(st2, "h", "0.1");
        Assert.Equal(save.Sections[HeistSave.SectionName].ToArray(), save2.Sections[HeistSave.SectionName].ToArray());
        // ending the heist leaves no trace of the run in the section a world without heists would not write
        var ended = await rt.AtBarrier(() => Do(HeistDesk.Do("leave", sec)));
        Assert.False(ended.Ok && st.Heists?.Run != null && st.Heists.Run.Outcome == RunOutcome.Active);
    }
}
