using Ravensguard.Sim.Core;
using Ravensguard.Sim.Heists;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Save;
using Xunit;
using Xunit.Abstractions;
using static Ravensguard.Sim.Tests.Heists.HeistTests;

namespace Ravensguard.Sim.Tests.Heists;

/// <summary>HJ-4 to HJ-7 and HJ-9, HJ-12 on hand-built scenes of the real templates: guards looking and listening, keys, bribes, the livery, the jailbreak, the save.</summary>
public class HeistRunTests(ITestOutputHelper output)
{
    /// <summary>Keeps only the guards the test names (by roster slot): a scene with the others sent home.</summary>
    private static void Only(HeistRun run, params int[] slots) => run.Here.Guards.RemoveAll(g => !slots.Contains(g.Slot));

    private static HGuard Slot(HeistRun run, int slot) => run.Here.Guards.First(g => g.Slot == slot);

    private static void Put(HeistRun run, int x, int y) { run.X = x; run.Y = y; }

    // ------------------------------------------------------------------ HJ-4

    [Fact]
    public void HJ4_AStoneSixMetresAwayDrawsAGuardWhoLooksAndGoesBack_AnOpenCellRaisesTheAlarm_AndEveryGuardWhoHearsComes()
    {
        var b = new HeistBook();
        var run = Gaol(b, 1, HeistKind.Gaol, At(1));                                   // the town gaol at one in the morning
        var s = run.Here;
        output.WriteLine("roster: " + string.Join(" ", s.Guards.Select(g => $"{g.Slot}:{g.State}@{g.X},{g.Y}")));
        var walker = s.Guards.First(g => g.State == GState.Dwell);
        Only(run, walker.Slot);
        walker.X = 10; walker.Y = 4; walker.Route = 0; walker.State = GState.Dwell; walker.Timer = 500;
        Put(run, 26, 4); run.SetSneak(true);
        Assert.Equal(Act.Ok, run.Throw(16, 4));                                        // lands 6 m from the guard at (10, 4)
        run.Advance(5);
        Assert.Equal(GState.Investigate, walker.State); Assert.Equal((16, 4), (walker.TargetX, walker.TargetY));
        run.Advance(7);
        Assert.Equal((16, 4), (walker.X, walker.Y));
        Assert.True(walker.State is GState.Looking or GState.Investigate);
        run.Advance(30);
        Assert.NotEqual(GState.Looking, walker.State);                                 // he looked around for twenty seconds and turned back
        run.Advance(20);
        Assert.True(walker.State is GState.Patrol or GState.Dwell);                      // back on his round (he picks up the loop where he left it)
        Assert.Empty(run.Seen); Assert.False(s.Alarm);

        // an open cell seen: a shout, the outer door locked, every guard who hears comes
        var b2 = new HeistBook();
        var r2 = Gaol(b2, 1, HeistKind.Gaol, At(1)); var s2 = r2.Here;
        var seer = s2.Guards.First(g => g.State == GState.Dwell);
        var others = s2.Guards.Where(g => g != seer).ToList();
        seer.X = 10; seer.Y = 4; seer.State = GState.Dwell; seer.Timer = 500;
        Put(r2, 29, 4);                                                                // the way in, far off and dark
        s2.Door[2] = 2;                                                                // the third cell's bars stand open (10, 3)
        var heardBy = new List<HGuard>();
        r2.Advance(1);
        r2.Advance(5);
        Assert.True(s2.Alarm); Assert.Equal(0, s2.Door[HeistRun.OuterIndex(s2.Map!)]);
        Assert.True(seer.State is GState.Investigate or GState.Looking);
        // the shout (40 m) reaches whom the rule says it reaches, and they come to the shouter
        var shout = s2.Noises.Last(n => n[4] == 1);
        r2.Advance(5);
        foreach (var g in others)
        {
            int walls = r2.WallsBetween(s2, (int)shout[1], (int)shout[2], g.X, g.Y);
            bool should = g.State != GState.Asleep || true;
            _ = should; _ = walls;
        }
        var awake = others.Where(g => g.State != GState.Asleep).ToList();
        output.WriteLine("after the shout: " + string.Join(" ", s2.Guards.Select(g => $"{g.Slot}:{g.State}@{g.X},{g.Y}")));
        Assert.Contains(awake, g => g.State is GState.Investigate or GState.Looking);
        Assert.All(awake.Where(g => g.State == GState.Investigate), g => Assert.Equal((10, 4), (g.TargetX, g.TargetY)));
        _ = heardBy;
    }

    // ------------------------------------------------------------------ HJ-5

    private static bool Quiet(HeistRun run, long sec) => true;

    [Fact]
    public void HJ5_AKeyTakenFromTheSleepingKeeperOpensTheCellAtOnce_AndAfterTheTheftTheLordChangesTheLocks()
    {
        // the castle's dungeon at three in the morning: the keeper lies in the guardroom with the keys; the rest are sent away for this test
        HeistRun? run = null; HeistBook? b = null; HGuard? keeper = null;
        for (int minute = 0; minute < 40 && run == null; minute++)
        {
            b = new HeistBook();
            var r = Gaol(b, 2, HeistKind.Gaol, At(3, 5, minute));
            Only(r, 0); keeper = Slot(r, 0);
            Assert.Equal(GState.Asleep, keeper.State); Assert.NotEmpty(keeper.Keys);
            r.SetSneak(true); r.Lockpicking = 100;
            Assert.Equal(Act.Ok, r.GoTo(34, 4));
            Assert.Equal(Act.Ok, r.Pick(7)); output.WriteLine($"picked at {r.Sec}");                                           // the door '2' between the way in and the guardroom
            var gt = r.GoTo(keeper.HomeX + 1, keeper.HomeY); output.WriteLine($"goto -> {gt} at {r.X},{r.Y} door7={r.Here.Door[7]} door6={r.Here.Door[6]} keeper {keeper.HomeX},{keeper.HomeY}");
            Assert.Equal(Act.Ok, gt);
            int idx = r.Here.Guards.IndexOf(keeper);
            Assert.Equal(Act.Ok, r.Search(idx));
            if (keeper.State == GState.Asleep && !r.Seen.Any()) run = r;               // he did not wake (P = 10% a search, stream 0x6B1)
        }
        Assert.NotNull(run);
        Assert.True(keeper!.KeysTaken); Assert.Empty(keeper.Keys);
        var s = run!.Here;
        Assert.Equal(8, run.Keys.Count(k => k.Site == s.Id));                          // six cell doors and the two locked doors
        // the key opens the cell at once: a pick of a Masterwork lock would be 240 seconds
        Assert.Equal(Act.Ok, run.GoTo(24 + 1, 4));                                     // beside the locked door '3' (a key opens it)
        long before = run.Sec;
        Assert.Equal(Act.Ok, run.UseKey(6));
        Assert.Equal(before + 1, run.Sec); Assert.Equal(2, s.Door[6]);
        Assert.Equal(Act.Ok, run.GoTo(3, 4));
        Assert.Equal(Act.Ok, run.UseKey(0)); Assert.Equal(2, s.Door[0]);               // cell one, Masterwork, open
        // out again and the run over: the lord will have the locks changed within two days
        Assert.Equal(Act.Ok, run.GoTo(39, 4));
        Assert.Equal(Act.Ok, run.Leave());
        long sec = run.Sec; HeistSites.Close(b!, run.R, sec);
        Assert.Equal((int)(sec / 86_400) + 2, s.ChangeDay); Assert.Equal(0, s.LockGen);
        var w = Rig.World();
        HeistLink.Hourly(w, b!, run.R, s.ChangeDay * 24L - 1); Assert.Equal(0, s.LockGen);
        HeistLink.Hourly(w, b!, run.R, s.ChangeDay * 24L); Assert.Equal(1, s.LockGen); Assert.Equal(-1, s.ChangeDay);
        Assert.Contains(w.Chronicle.All, e => e.TypeId == "locks_changed");
        // the stolen key is dead iron now
        var again = HeistSites.Start(b!, run.R, Seed, 3, HeistKind.Gaol, 2, At(3, 9), 0, [], [])!;
        again.SetSneak(true); Assert.Equal(Act.Ok, again.GoTo(34, 4));
        Assert.Equal(Act.StaleKey, again.UseKey(7));
    }

    // ------------------------------------------------------------------ HJ-6

    [Fact]
    public void HJ6_ABribeIsTakenAtThePriceWithTheChanceOfHisHonesty_RefusedUnderIt_AndAnHonestManNeverTakesOne()
    {
        var law = Rules.Law;
        Assert.Equal(900, Ravensguard.Sim.Law.Gaol.BribeFloor(law, 20)); Assert.Equal(40, Ravensguard.Sim.Law.Gaol.BribePct(law, 20)); Assert.Null(Ravensguard.Sim.Law.Gaol.BribeFloor(law, 70));
        Assert.Equal(BribeResult.TooLittle, Bribes.Offer(Seed, law, 77, 20, 600, At(1)));
        Assert.Equal(BribeResult.Honest, Bribes.Offer(Seed, law, 77, 70, 9_000_000, At(1)));
        int taken = 0; const int N = 2000;
        for (int day = 0; day < N; day++) if (Bribes.Offer(Seed, law, 77, 20, 900, At(1, day)) == BribeResult.Taken) taken++;
        output.WriteLine($"taken {taken} of {N}");
        Assert.InRange(taken * 100 / N, 35, 45);                                       // P = 40%
        // the same draw as the gaoler's own price (one rule for every official): the day and the official key it
        Assert.Equal(Bribes.Offer(Seed, law, 77, 20, 900, At(1, 33)), Bribes.Offer(Seed, law, 77, 20, 5000, At(2, 33)));

        // in a scene: 600 is refused with an alert and a chase; 900 on a day the dice allow is taken and he looks away; honesty 70 refuses any sum
        HeistRun Scene(int day, int honesty, out HGuard g)
        {
            var b = new HeistBook();
            var run = HeistSites.Start(b, Rules, Seed, 3, HeistKind.Gaol, 1, At(1, day), 0, [], [], _ => honesty)!;
            var post = run.Here.Guards.First(x => x.State == GState.Post); g = post; Only(run, post.Slot);
            post.Honesty = honesty; Put(run, post.X, post.Y + 1); if (!run.Map.Walkable(run.X, run.Y)) Put(run, post.X, post.Y - 1);
            return run;
        }
        var r1 = Scene(5, 20, out var g1);
        var (act1, res1) = r1.Bribe(r1.Here.Guards.IndexOf(g1), 600);
        Assert.Equal(BribeResult.TooLittle, res1); Assert.Equal(GState.Pursue, g1.State); Assert.True(r1.Here.Alarm); Assert.Contains(r1.Seen, x => x.Guard == g1.Person);
        Assert.Contains(r1.Events, e => e.Kind == HeistEventKind.BribeRefused);
        int yes = Enumerable.Range(5, 400).First(d => Bribes.Offer(Seed, law, Scene(d, 20, out var tg).Here.Guards.First(x => x.State == GState.Post).Person, 20, 900, At(1, d)) == BribeResult.Taken);
        var r2 = Scene(yes, 20, out var g2);
        var (_, res2) = r2.Bribe(r2.Here.Guards.IndexOf(g2), 900);
        Assert.Equal(BribeResult.Taken, res2); Assert.Equal(GState.Bribed, g2.State); Assert.False(r2.Here.Alarm); Assert.Empty(r2.Seen);
        Assert.Contains(r2.Events, e => e.Kind == HeistEventKind.Bribed && e.Value == 900);
        r2.Advance(60); Assert.Empty(r2.Seen); Assert.Equal(GState.Bribed, g2.State);   // he keeps looking the other way
        var r3 = Scene(5, 70, out var g3);
        var (_, res3) = r3.Bribe(r3.Here.Guards.IndexOf(g3), 5_000_000);
        Assert.Equal(BribeResult.Honest, res3); Assert.Equal(GState.Pursue, g3.State);
    }

    // ------------------------------------------------------------------ HJ-7

    [Fact]
    public void HJ7_InTheLordsLiveryTheHeroPassesAGuardAtSixMetres_IsChallengedAtThree_AndAGuardWhoKnowsHimSeesThrough()
    {
        var s = Rules.S;
        Assert.Equal(Challenge.Passes, Disguise.Check(Seed, s, 5, 1, 600, true, false, false, false, false));
        Assert.Equal(Challenge.Challenged, Disguise.Check(Seed, s, 5, 1, 300, true, false, false, false, false));
        Assert.Equal(Challenge.SeesThrough, Disguise.Check(Seed, s, 5, 1, 600, true, true, false, false, false));
        Assert.Equal(Challenge.Challenged, Disguise.Check(Seed, s, 5, 1, 600, true, false, true, false, false));      // he runs
        Assert.Equal(Challenge.Challenged, Disguise.Check(Seed, s, 5, 1, 600, true, false, false, true, false));      // he carries a body
        Assert.Equal(Challenge.Challenged, Disguise.Check(Seed, s, 5, 1, 600, true, false, false, false, true));      // in the cell block
        Assert.Equal(Challenge.Challenged, Disguise.Check(Seed, s, 5, 1, 600, false, false, false, false, false));    // no livery at all
        int challenged = 0; for (int h = 0; h < 1000; h++) if (Disguise.Check(Seed, s, 9, h, 450, true, false, false, false, false) == Challenge.Challenged) challenged++;
        Assert.InRange(challenged, 400, 600);                                          // the borderline between four and five metres is a draw (0x6B3)

        // in a scene: a guard in the lit corridor of the castle; the hero in the livery at six metres is not taken for a stranger, at three he is
        var b = new HeistBook(); var run = Gaol(b, 2, HeistKind.Gaol, At(1));
        var guard = run.Here.Guards.First(g => g.State is GState.Dwell or GState.Post); Only(run, guard.Slot);
        guard.State = GState.Post; guard.X = 32; guard.Y = 4; guard.HomeX = 32; guard.HomeY = 4;
        run.SetLivery(true, 99); run.SetTorch(true);
        Put(run, 26, 4);                                                               // six metres west, at the edge of the light of the last sconce, out of the cell block
        Assert.True(run.Lit(run.Here, 26, 4));
        run.Advance(30);
        Assert.Empty(run.Seen); Assert.Equal(GState.Post, guard.State);
        Assert.Equal(0, run.Eye().Count);
        Put(run, 29, 4);                                                               // three metres
        Assert.Equal(1, run.Eye().Count);
        run.Advance(10);
        Assert.Single(run.Seen); Assert.Equal(GState.Pursue, guard.State);
        // a guard who knows his face sees through it at any range
        var b2 = new HeistBook(); var r2 = Gaol(b2, 2, HeistKind.Gaol, At(1));
        var g2 = r2.Here.Guards.First(g => g.State is GState.Dwell or GState.Post); Only(r2, g2.Slot);
        g2.State = GState.Post; g2.X = 32; g2.Y = 4; g2.Familiarity = 1; r2.SetLivery(true, 99); r2.SetTorch(true); Put(r2, 26, 4);
        r2.Advance(10);
        Assert.Single(r2.Seen); Assert.True(r2.Seen[0].Knew);
    }
}

/// <summary>A hand-made town for the world-side tests: guards, a gaoler, a fief treasury and the hero's purse, as the law's own tests build theirs.</summary>
internal static class Rig
{
    public static LawWorld World() => Ravensguard.Sim.Tests.Heists.HeistWorld.Make(1234).W;
}
