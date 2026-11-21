using System.Diagnostics;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Combat.Battle;

/// <summary>
/// Phase 7 BB-B (battle-bubble-and-formations 11): formations and orders. BB-6 slots, BB-7 closing ranks, BB-8 anchor braking, BB-9 deployment from column, BB-10 order delay, BB-11 a pause gives no free
/// time, BB-12 the formation's flank term and rout cascade, BB-13 captains; and the pieces around them: leash and steering, the save round trip, determinism, the ring shapes.
/// </summary>
[Collection("fight")]
public class FormationTests(FightFixture fx, ITestOutputHelper output)
{
    private static readonly WeaponInstance Spear = new("spear", "steel");

    // a troop kit by class: 0 shield bearer, 1 polearm, 2 sword, 3 archer
    private FighterKit KitOf(int cls, int team, int x, int y, int skill, int tactics = -1) => (cls switch
    {
        0 => fx.Kit($"s{x}", team, x, y, skill: skill, shield: "heater"),
        1 => fx.Kit($"p{x}", team, x, y, weapon: Spear, skill: skill),
        2 => fx.Kit($"w{x}", team, x, y, skill: skill),
        _ => new FighterKit { Name = $"a{x}", Team = team, Launcher = "longbow", Ammo = "bodkin_arrow", AmmoCount = 12, Worn = [new WornItem("tunic", "linen")], Skill = skill, ArcherySkill = skill, X = x, Y = y, FaceX = team == 0 ? 1024 : -1024 },
    }) with { Tactics = tactics, Courage = 100 };

    private List<int> Add(FightWorld w, int n, Func<int, int> cls, int team = 0, int x0 = 0, int y0 = 0, Func<int, int>? skill = null, Func<int, int>? tactics = null, bool player = false)
    {
        var ids = new List<int>();
        for (int i = 0; i < n; i++) ids.Add(w.Add(KitOf(cls(i), team, x0 + i * 7 % 5, y0 + i, skill?.Invoke(i) ?? 30 + i * 7 % 40, tactics?.Invoke(i) ?? -1), player).Id);
        return ids;
    }

    private static void Tick(FightWorld w) { w.Formations!.Step(w); w.Step(); }

    // ---------------------------------------------------------------- BB-6 slots

    [Fact]
    public void BB6_Slots_100MenInAFourRankLine_ByRoleThenSkillThenId_AndExactToTheMm()
    {
        var w = fx.World(1);
        // 100 men: classes interleaved by id so the order is the sort's doing
        var ids = Add(w, 100, i => i % 4, skill: i => 30 + (i * 13) % 50);
        var book = w.EnsureFormations();
        int fx30 = 887, fy30 = 512;                                             // 30 degrees, Q10
        var r = book.Form(w, 0, 1, FormationKind.Infantry, ids, 1000, 2000, fx30, fy30, Formation.Line, 4);
        Assert.Equal(25, r.Files);
        for (int s = 0; s < 100; s++)
        {
            var f = w.Fighters[r.FighterAt(s)];
            int rank = s / 25;
            Assert.Equal(rank, s < 25 ? 0 : s < 50 ? 1 : s < 75 ? 2 : 3);
            Assert.Equal(rank, Slots.Priority(f, Formation.Line));              // shield bearers rank 0, polearms rank 1, swords rank 2, archers rank 3
        }
        for (int s = 1; s < 100; s++)                                            // within a class: skill descending, then id
        {
            var a = w.Fighters[r.FighterAt(s - 1)]; var b = w.Fighters[r.FighterAt(s)];
            if (Slots.Priority(a, Formation.Line) != Slots.Priority(b, Formation.Line)) continue;
            Assert.True(Slots.SkillOf(a) > Slots.SkillOf(b) || Slots.SkillOf(a) == Slots.SkillOf(b) && a.Id < b.Id, $"slot {s}: skill {Slots.SkillOf(a)}/{Slots.SkillOf(b)} ids {a.Id}/{b.Id}");
        }
        // exact positions: lat = (24 - 2 file) x 500 to the left, back = rank x 1,200; x = ax + ((-fy lat - fx back) >> 10), y = ay + ((fx lat - fy back) >> 10)
        book.SlotWorld(r, 0, 1, out int x0, out int y0);
        Assert.Equal((-5000, 12394), (x0, y0));                                  // left end of the front rank: lat 12,000, back 0
        book.SlotWorld(r, 24, 1, out int x24, out int y24);
        Assert.Equal((7000, -8395), (x24, y24));                                  // right end of the front rank: lat -12,000
        book.SlotWorld(r, 99, 1, out int x99, out int y99);
        Assert.Equal((3881, -10195), (x99, y99));                                 // right end of the rear rank: lat -12,000, back 3,600
        for (int s = 0; s < 100; s++)                                            // every slot is exactly the formula
        {
            book.SlotWorld(r, s, 1, out int x, out int y);
            int lat = (24 - 2 * (s % 25)) * 1000 / 2, back = s / 25 * 1200;
            Assert.Equal(1000 + (int)((-512L * lat - 887L * back) >> 10), x);
            Assert.Equal(2000 + (int)((887L * lat - 512L * back) >> 10), y);
        }
    }

    [Fact]
    public void BB6_Slots_TheOrderDoesNotDependOnTheOrderGiven_AndRingsAreDistinct()
    {
        var w = fx.World(2);
        var ids = Add(w, 60, i => i % 4);
        var a = w.EnsureFormations().Form(w, 0, 1, FormationKind.Infantry, ids, 0, 0, 1024, 0);
        var w2 = fx.World(2);
        var ids2 = Add(w2, 60, i => i % 4);
        ids2.Reverse();
        var b = w2.EnsureFormations().Form(w2, 0, 1, FormationKind.Infantry, ids2, 0, 0, 1024, 0);
        Assert.Equal(a.Members, b.Members);
        // a schiltron: polearms outside, every slot its own place; fall back: rings about the commander, 3 m out
        var def = Ravensguard.Sim.Battle.FormationRules.For(fx.Db).Shape(Formation.Schiltron);
        int[] lat = new int[40], back = new int[40];
        Slots.Layout(def, 40, 2, lat, back);
        Assert.Equal(40, Enumerable.Range(0, 40).Select(i => (lat[i], back[i])).Distinct().Count());
        var outer = Enumerable.Range(0, 27).Select(i => FightMath.Len(lat[i], back[i])).ToArray();
        var inner = Enumerable.Range(27, 13).Select(i => FightMath.Len(lat[i], back[i])).ToArray();
        Assert.True(outer.Min() > inner.Max(), "the polearms' ring is outside");
        var fb = Ravensguard.Sim.Battle.FormationRules.For(fx.Db).Shape(Formation.FallBack);
        int[] l2 = new int[12], b2 = new int[12];
        Slots.Layout(fb, 12, 1, l2, b2);
        Assert.All(Enumerable.Range(0, 12), i => Assert.InRange(FightMath.Len(l2[i], b2[i]), 2900, 3100));
    }

    // ---------------------------------------------------------------- BB-7 closing ranks

    private (FightWorld W, FormationRow R) Line(int n, int drill = 0, int team = 0, ulong seed = 3, bool place = true)
    {
        var w = fx.World(seed);
        var ids = Add(w, n, i => 2, team);
        var book = w.EnsureFormations();
        var r = book.Form(w, team, 1, FormationKind.Infantry, ids, 0, 0, 1024, 0, Formation.Line, 4, drill, place: place);
        return (w, r);
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(300, 28)]
    public void BB7_ClosingRanks_TheManBehindStepsUpAfterFortyTicksLessDrillOver25(int drill, int ticks)
    {
        var (w, r) = Line(40, drill);
        int slot = 3, behind = r.MemberAt(slot + r.Files);
        var down = w.Fighters[r.FighterAt(slot)];
        down.Act = FAct.Down;
        for (int t = 1; t <= ticks + 3; t++)
        {
            w.StepWithAi();
            if (t < ticks) Assert.Equal(slot + r.Files, r.Slot[behind]);          // not before
            else if (t == ticks) Assert.Equal(slot, r.Slot[behind]);               // exactly then
        }
        Assert.Equal(-1, r.Slot[r.IndexOfFighter(down.Id)]);
        // and the line behind him has a vacancy of its own that fills later (with the hesitation of stream 0x782)
        Assert.Equal(1, r.VacCount);
    }

    [Fact]
    public void BB7_ClosingRanks_WithNobodyBehind_TheFlankManStepsIn()
    {
        var (w, r) = Line(10, 0);                                                  // 4 ranks over 3 files... 10 men: files 3, ranks 4
        int files = r.Files;
        int last = r.LayoutN - 1;                                                  // the lone man of the last rank
        int hole = files;                                                          // a front... pick a hole in the last partial rank's row instead
        var down = w.Fighters[r.FighterAt(1)];                                     // front rank middle: someone is behind him
        down.Act = FAct.Down;
        for (int t = 0; t < 120; t++) w.StepWithAi();
        // every slot from the front is full down to the last survivors: the live men are packed, none left behind the hole
        int filled = Enumerable.Range(0, r.LayoutN).Count(s => r.Occ[s] >= 0);
        Assert.Equal(9, filled);
        Assert.Equal(0, Enumerable.Range(0, r.LayoutN).Count(s => r.VacSince[s] >= 0 && r.Occ[s] < 0 && s < files));
        _ = last; _ = hole;
    }

    // ---------------------------------------------------------------- BB-8 anchor braking

    private (FightWorld W, FormationRow R) Marching(int slowOfRank0, int slowPm)
    {
        var w = fx.World(4);
        var ids = Add(w, 40, i => 2);
        var enemy = w.Add(KitOf(2, 1, 600_000, 0, 50), true);                   // a far foe to march on
        var book = w.EnsureFormations();
        var r = book.Form(w, 0, 1, FormationKind.Infantry, ids, 0, 0, 1024, 0, Formation.Line, 4, place: true);
        for (int s = 0; s < slowOfRank0; s++) w.Fighters[r.FighterAt(s)].WalkPm = slowPm;
        r.Micro = int.MaxValue / 2;
        book.Apply(w, r, FormationOrder.Of(OrderKind.Charge), w.Tick + 1);
        return (w, r);
    }

    [Fact]
    public void BB8_AnchorBraking_TheAnchorMovesAtThePaceOfItsSlowestFifthOfRankZero()
    {
        var (w, r) = Marching(0, 1000);
        int x0 = r.AnchorX;
        for (int t = 0; t < 40; t++) w.StepWithAi();
        int free = r.AnchorX - x0;                                                  // a jog: 3 m/s, 150 mm a tick
        Assert.InRange(free, 5900, 6100);

        var (w2, r2) = Marching(3, 500);                                            // 3 of the 10 front men at walk 50%
        Assert.Equal(1500, r2.BrakeMmps);
        int y0 = r2.AnchorX;
        for (int t = 0; t < 40; t++) w2.StepWithAi();
        int slow = r2.AnchorX - y0;
        Assert.InRange(slow, 2900, 3100);                                           // 1.5 m/s: their pace
        Assert.True(r2.Cohesion >= 600, "the line kept together at their pace");

        var (w3, r3) = Marching(2, 500);                                            // only 2 of 10 (a fifth): not slowed
        Assert.Equal(3000, r3.BrakeMmps);
    }

    [Fact]
    public void BB8_AnchorBraking_TheAnchorStopsWhileFewerThan600PermilleAreOnTheirSlots()
    {
        var (w, r) = Marching(0, 1000);
        for (int t = 0; t < 10; t++) w.StepWithAi();
        Assert.True(r.AnchorX > 0);
        for (int s = 0; s < 20; s++) { var f = w.Fighters[r.FighterAt(s)]; f.X += 0; f.Y += 15_000; }   // half the men are 15 m off their slots
        w.StepWithAi();
        Assert.True(r.Cohesion < 600, $"cohesion {r.Cohesion}");
        int x0 = r.AnchorX;
        w.Step();                                                                   // no brain: the men stay where they are
        Tick(w);
        Assert.Equal(x0, r.AnchorX);
    }

    // ---------------------------------------------------------------- BB-9 deployment

    private int DeployTicks(int men, int drill)
    {
        var w = fx.World(5);
        var ids = new List<int>();
        for (int i = 0; i < men; i++) ids.Add(w.Add(KitOf(2, 0, 0, 0, 40), true).Id);
        var book = w.EnsureFormations();
        var r = book.Form(w, 0, 1, FormationKind.Infantry, ids, 0, 0, 1024, 0, Formation.Column, 0, drill, place: true);
        book.Apply(w, r, FormationOrder.Of(OrderKind.Line), w.Tick + 1);
        Assert.True(r.DeployStart >= 0);
        int n = 0;
        while (r.DeployStart >= 0 && n < 20_000) { Tick(w); n++; }
        return n;
    }

    [Fact]
    public void BB9_Deployment_AColumnOf300RanksFormsALineInTheTimeTheRankArithmeticGives()
    {
        // ranks x 1.5 m at 3 m/s: 300 ranks (900 men) is 150 s at Drill 0 and 75 s at Drill 1,000; 100 ranks (300 men) a third of that
        Assert.InRange(DeployTicks(900, 0), 150 * 20 - 40, 150 * 20 + 40);
        Assert.InRange(DeployTicks(900, 1000), 75 * 20 - 40, 75 * 20 + 40);
        Assert.InRange(DeployTicks(300, 0), 50 * 20 - 40, 50 * 20 + 40);
        Assert.InRange(DeployTicks(300, 1000), 25 * 20 - 40, 25 * 20 + 40);
    }

    [Fact]
    public void BB9_Deployment_RanksAreReleasedHeadFirst()
    {
        var w = fx.World(6);
        var ids = new List<int>();
        for (int i = 0; i < 90; i++) ids.Add(w.Add(KitOf(2, 0, 0, 0, 40), true).Id);
        var book = w.EnsureFormations();
        var r = book.Form(w, 0, 1, FormationKind.Infantry, ids, 0, 0, 1024, 0, Formation.Column, 0, 0, place: true);
        book.Apply(w, r, FormationOrder.Of(OrderKind.Line), w.Tick + 1);
        for (int i = 0; i < r.DeployTicks / 2; i++) Tick(w);
        int t = w.Tick + 1;
        book.SlotWorld(r, 0, t, out int hx, out int hy);
        book.SlotWorld(r, 89, t, out int tx, out int ty);
        Slots.ToWorld(r.AnchorX, r.AnchorY, r.FaceX, r.FaceY, r.Lat[0], r.Back[0], out int lx, out int ly);
        Assert.Equal((lx, ly), (hx, hy));                                           // the head is in its place in the line
        Slots.ToWorld(r.AnchorX, r.AnchorY, r.FaceX, r.FaceY, r.ColLat[89], r.ColBack[89], out int cx, out int cy);
        Assert.Equal((cx, cy), (tx, ty));                                           // the tail is still in the column
    }

    // ---------------------------------------------------------------- BB-10 order delay

    private (FightWorld W, FormationBook B, FormationRow Far, int Hero) TwoFormations(bool horn, int farMm)
    {
        var w = fx.World(7);
        var mine = Add(w, 8, i => 2, x0: 0, y0: 0);
        var far = Add(w, 8, i => 2, x0: farMm, y0: 0, tactics: i => i == 0 ? 20 : 0);
        var book = w.EnsureFormations();
        book.Form(w, 0, 1, FormationKind.Retinue, mine, 0, 0, 1024, 0, place: true, banner: horn ? mine[1] : -1);
        var rf = book.Form(w, 0, 2, FormationKind.Infantry, far, farMm, 0, 1024, 0, place: true);
        foreach (var q in book.Rows) q.Micro = int.MaxValue / 2;                    // the captains stay quiet: these tests are about the player's orders
        return (w, book, rf, mine[0]);
    }

    private static int RunUntilApplied(FightWorld w, FormationBook b, FormationRow r, OrderKind kind, int max = 2000)
    {
        for (int t = 1; t <= max; t++) { Tick(w); if (r.Order.Kind == kind) return t; }
        return -1;
    }

    [Fact]
    public void BB10_OrderDelay_VoiceWithinSixtyMetres_FortyTicksAtLeadership50()
    {
        var (w, b, far, hero) = TwoFormations(false, 30_000);
        var o = b.Issue(w, far, FormationOrder.Of(OrderKind.Advance), hero, 50)!.Value;
        Assert.Equal(OrderSignal.Voice, o.Signal);
        Assert.Equal(40, o.ArriveTick - o.IssuedTick);
        Assert.Equal(40, RunUntilApplied(w, b, far, OrderKind.Advance));
        // Leadership 0: 4 s; Leadership 100: 0.5 s
        var (w0, b0, f0, h0) = TwoFormations(false, 30_000);
        Assert.Equal(80, b0.Issue(w0, f0, FormationOrder.Of(OrderKind.Hold), h0, 0)!.Value.ArriveTick);
        Assert.Equal(10, b0.Issue(w0, f0, FormationOrder.Of(OrderKind.Hold), h0, 100)!.Value.ArriveTick);
    }

    [Fact]
    public void BB10_OrderDelay_AtTwoHundredMetres_AHornAddsOneSecond_ARunnerWalksIt()
    {
        var (w, b, far, hero) = TwoFormations(true, 200_000);
        var o = b.Issue(w, far, FormationOrder.Of(OrderKind.Advance), hero, 50)!.Value;
        Assert.Equal(OrderSignal.Horn, o.Signal);
        Assert.Equal(60, RunUntilApplied(w, b, far, OrderKind.Advance));
        // an order a horn cannot carry goes by runner: 800 ticks (200 m at 5 m/s) and the 40
        var o2 = b.Issue(w, far, FormationOrder.Of(OrderKind.FlankLeft), hero, 50)!.Value;
        Assert.Equal(OrderSignal.Runner, o2.Signal);
        Assert.Equal(840, o2.ArriveTick - o2.IssuedTick);
        // no horn at all: even a hold goes by runner
        var (wn, bn, fn, hn) = TwoFormations(false, 200_000);
        var o3 = bn.Issue(wn, fn, FormationOrder.Of(OrderKind.Hold), hn, 50)!.Value;
        Assert.Equal(OrderSignal.Runner, o3.Signal);
        Assert.Equal(840, o3.ArriveTick - o3.IssuedTick);
        Assert.Equal(840, RunUntilApplied(wn, bn, fn, OrderKind.Hold));
    }

    [Fact]
    public void BB10_OrderDelay_TheRunnerKilled_TheOrderIsLostAndShown()
    {
        var (w, b, far, hero) = TwoFormations(false, 200_000);
        var o = b.Issue(w, far, FormationOrder.Of(OrderKind.Advance), hero, 50)!.Value;
        Assert.InRange(o.Runner, 0, 7);                                             // a member of the commander's own formation
        Assert.NotEqual(hero, o.Runner);
        for (int t = 0; t < 100; t++) Tick(w);
        w.Fighters[o.Runner].Act = FAct.Dead;
        for (int t = 0; t < 300; t++) Tick(w);
        Assert.Equal(OrderKind.None, far.Order.Kind);
        Assert.Single(b.Lost);
        Assert.Equal(o.Serial, b.Lost[0].Serial);
        Assert.Equal(0, far.PendingCount);
    }

    [Fact]
    public void BB10_Queue_AnOrderQueuedStartsWhenTheOneBeforeIsDone()
    {
        var (w, b, far, hero) = TwoFormations(false, 30_000);
        var mv = FormationOrder.Of(OrderKind.MoveTo); mv.TargetX = far.AnchorX + 6000; mv.TargetY = 0;
        var mv2 = FormationOrder.Of(OrderKind.MoveTo); mv2.TargetX = far.AnchorX + 6000; mv2.TargetY = 9000;
        b.Issue(w, far, mv, hero, 100);
        b.Issue(w, far, mv2, hero, 100, queue: true);
        for (int t = 0; t < 30; t++) w.StepWithAi();
        Assert.Equal(1, far.QueueCount);
        for (int t = 0; t < 400; t++) w.StepWithAi();
        Assert.InRange(far.AnchorY, 9000 - 3500, 9000 + 3500);
        Assert.Equal(0, far.QueueCount);
    }

    // ---------------------------------------------------------------- BB-11 pause

    [Fact]
    public void BB11_Pause_AnOrderPlacedWhilePausedArrivesTheSameTicksAfterResume()
    {
        int unpaused, paused;
        {
            var (w, b, far, hero) = TwoFormations(false, 30_000);
            b.Issue(w, far, FormationOrder.Of(OrderKind.Advance), hero, 50);
            unpaused = RunUntilApplied(w, b, far, OrderKind.Advance);
        }
        {
            var (w, b, far, hero) = TwoFormations(false, 30_000);
            Tick(w); Tick(w);                                                       // the game pauses here: no sim tick runs while the overlay is open
            var o = b.Issue(w, far, FormationOrder.Of(OrderKind.Advance), hero, 50)!.Value;
            var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < 60) { }   // sixty "real seconds" (scaled) of nothing happening
            Assert.Equal(w.Tick, o.IssuedTick);
            Assert.Equal(40, o.ArriveTick - w.Tick);
            paused = RunUntilApplied(w, b, far, OrderKind.Advance);
        }
        Assert.Equal(40, unpaused);
        Assert.Equal(unpaused, paused);
    }

    // ---------------------------------------------------------------- BB-12 flank and cascade

    [Fact]
    public void BB12_Flank_AFormationWith10PercentFlankBlowsGivesEveryMemberTheFlankTerm()
    {
        var (w, r) = Line(40);
        w.StepWithAi();
        var b = w.Formations!;
        int now = w.Tick + 1;
        for (int i = 0; i < 4; i++) b.NoteFlankBlow(w.Fighters[r.Members[i]], now);        // 4 of 40: 100 permille
        for (int t = 0; t < 12; t++) w.StepWithAi();
        Assert.All(r.Members, id => Assert.True(w.Fighters[id].M.FlankedUntil > w.Tick, $"fighter {id}"));
        // 3 of 40 is not enough
        var (w2, r2) = Line(40, seed: 9);
        w2.StepWithAi();
        for (int i = 0; i < 3; i++) w2.Formations!.NoteFlankBlow(w2.Fighters[r2.Members[i]], w2.Tick + 1);
        for (int t = 0; t < 12; t++) w2.StepWithAi();
        Assert.Equal(0, r2.Members.Count(id => w2.Fighters[id].M.FlankedUntil > w2.Tick));
    }

    [Fact]
    public void BB12_Flank_ATwentyManUnitHittingTheFlank_IsRecordedPerMember()
    {
        var w = fx.World(12);
        var line = Add(w, 40, i => i % 2 == 0 ? 0 : 2, player: true);                // a line caught standing: it does not turn to face the blows
        var raiders = new List<int>();
        for (int i = 0; i < 20; i++) raiders.Add(w.Add(KitOf(2, 1, 4000 + i % 3 * 900, 12_500 + i / 3 * 1100, 70), false).Id);
        var book = w.EnsureFormations();
        var r = book.Form(w, 0, 1, FormationKind.Infantry, line, 0, 0, 1024, 0, Formation.Line, 4, place: true);
        var e = book.Form(w, 1, 1, FormationKind.Infantry, raiders, 4000, 14_000, 0, -1024, Formation.Loose, 5);
        r.Micro = e.Micro = int.MaxValue / 2;                                         // the line is held by its player, the raiders by nobody
        book.Apply(w, r, FormationOrder.Of(OrderKind.Hold), 1);
        book.Apply(w, e, FormationOrder.Of(OrderKind.Advance), 1);                    // the raiders march on the line
        w.LodCentre = (0, 0);
        int sustained = 0; bool spread = false;
        for (int t = 0; t < 600; t++)
        {
            w.StepWithAi();
            int hit = r.FlankBlow.Count(b => b > w.Tick - 100);
            sustained = hit * 1000 >= 100 * Math.Max(1, r.Active) ? sustained + 1 : 0;
            if (sustained >= 12) { spread = r.Members.Where(id => w.Fighters[id].Active).All(id => w.Fighters[id].M.FlankedUntil > w.Tick); break; }
        }
        int recorded = r.FlankBlow.Count(b => b > 0);
        output.WriteLine($"flank unit: {recorded} members took a flank blow; the tenth was reached and spread: {spread}");
        Assert.True(recorded >= 1, "a blow from the side must be recorded against the member who took it");
        if (sustained >= 12) Assert.True(spread, "when a tenth had taken flank blows the whole formation carries the term");
    }

    [Fact]
    public void BB12_Cascade_AtHalfRoutingTheRestTakeMinus20AndANeighbourMinus10_Once()
    {
        var w = fx.World(13);
        var a = Add(w, 40, i => 2, x0: 0);
        var n = Add(w, 40, i => 2, x0: 0, y0: 30);
        var book = w.EnsureFormations();
        var ra = book.Form(w, 0, 1, FormationKind.Infantry, a, 0, 0, 1024, 0, Formation.Line, 4, place: true);
        var rn = book.Form(w, 0, 2, FormationKind.Infantry, n, 0, 12_000, 1024, 0, Formation.Line, 4, place: true);
        w.StepWithAi();
        int before(int id) => w.Fighters[id].M.PenaltyX100;
        var steady = ra.Members.Skip(20).ToArray(); var nb = rn.Members;
        int[] bs = steady.Select(before).ToArray(), bn = nb.Select(before).ToArray();
        for (int i = 0; i < 20; i++) w.Fighters[ra.Members[i]].Routing = true;           // 500 permille
        for (int t = 0; t < 12; t++) w.StepWithAi();
        Assert.True(ra.Cascaded);
        for (int i = 0; i < steady.Length; i++) Assert.InRange(w.Fighters[steady[i]].M.PenaltyX100 - bs[i], 1800, 2300);   // -20 less the 0.2 a second decay
        for (int i = 0; i < nb.Length; i++) Assert.InRange(w.Fighters[nb[i]].M.PenaltyX100 - bn[i], 800, 1300);            // -10
        int[] afterA = steady.Select(before).ToArray(), afterN = nb.Select(before).ToArray();
        for (int t = 0; t < 60; t++) w.StepWithAi();
        for (int i = 0; i < steady.Length; i++) Assert.True(w.Fighters[steady[i]].M.PenaltyX100 <= afterA[i], "no second cascade");
        for (int i = 0; i < nb.Length; i++) Assert.True(w.Fighters[nb[i]].M.PenaltyX100 <= afterN[i], "no second cascade for the neighbour");
    }

    [Fact]
    public void BB12_WallBonus_ShieldWallAtCohesion600_AndTheFallenCaptainIsFeltByAll()
    {
        var w = fx.World(14);
        var ids = Add(w, 40, i => 0, tactics: i => i == 5 ? 60 : 10);
        var book = w.EnsureFormations();
        var r = book.Form(w, 0, 1, FormationKind.Infantry, ids, 0, 0, 1024, 0, Formation.ShieldWall, 3, place: true);
        Assert.True(book.WallBonus(w.Fighters[ids[0]]));
        Assert.Equal(ids[5], r.Captain);
        r.Cohesion = 500;
        Assert.False(book.WallBonus(w.Fighters[ids[0]]));
        r.Cohesion = 1000;
        w.StepWithAi();
        var far = w.Fighters[r.Members.Last(id => id != ids[5])];
        far.X = 80_000;                                                                     // 80 m from the captain: outside the 30 m leader-down radius
        int p0 = far.M.PenaltyX100;
        var cap = w.Fighters[ids[5]];
        cap.Act = FAct.Down;
        book.CaptainFell(w, cap);                                                          // what the fight's announce of a fall calls
        Assert.True(far.M.PenaltyX100 >= p0 + 1800, $"penalty {far.M.PenaltyX100} from {p0}");
    }

    // ---------------------------------------------------------------- BB-13 captains

    private (FightWorld W, FormationRow R) CaptainTest(int tactics, Action<FightWorld> enemies, int men = 40)
    {
        var w = fx.World(20);
        var mine = Add(w, men, i => i % 3 == 0 ? 0 : 2, tactics: i => i == 7 ? tactics : 0);
        enemies(w);
        var book = w.EnsureFormations();
        var r = book.Form(w, 0, 1, FormationKind.Infantry, mine, 0, 0, 1024, 0, Formation.Line, 4, place: true);
        r.CaptainAlways = true;
        return (w, r);
    }

    private void Archers(FightWorld w, int xMm)
    {
        for (int i = 0; i < 10; i++) w.Add(KitOf(3, 1, xMm, -5000 + i * 1000, 60), true);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(70, true)]
    [InlineData(0, false)]
    public void BB13_Captains_ShieldWallWhenArchersLooseAt120m_OnlyFromTactics30(int tactics, bool wall)
    {
        var (w, r) = CaptainTest(tactics, ww => Archers(ww, 120_000));
        for (int t = 0; t < 60; t++) w.StepWithAi();
        Assert.Equal(wall, r.Shape == Formation.ShieldWall);
        Assert.Equal(wall, r.WallByCaptain);
    }

    [Theory]
    [InlineData(50, true)]
    [InlineData(30, false)]
    [InlineData(0, false)]
    public void BB13_Captains_Tactics50WheelsToFaceAFlankAttackWithin5Seconds(int tactics, bool wheels)
    {
        // eight men outnumbered by twenty standing 25 m off their left flank: every captain holds (outnumbered 2:1), only Tactics 50 turns to face them
        var (w, r) = CaptainTest(tactics, ww =>
        {
            var ids = new List<int>();
            for (int i = 0; i < 20; i++) ids.Add(ww.Add(KitOf(2, 1, -4000 + i % 5 * 1500, 25_000 + i / 5 * 1500, 50), true).Id);
            var fb = ww.EnsureFormations();
            fb.Form(ww, 1, 1, FormationKind.Infantry, ids, -4000, 30_000, 0, -1024, Formation.Loose, 4);
        }, 8);
        for (int t = 0; t < 100; t++) w.StepWithAi();                                 // 5 s
        var (ux, uy) = FightMath.Norm(0, 1);
        int dot = FightMath.Dot(r.FaceX, r.FaceY, ux, uy);
        output.WriteLine($"tactics {tactics}: facing ({r.FaceX},{r.FaceY}) dot to the flank {dot}, order {r.Order.Kind}");
        Assert.Equal(OrderKind.Hold, r.Order.Kind);
        if (wheels) Assert.True(dot >= 1000, $"dot {dot}"); else Assert.True(dot < 100, $"dot {dot}");
    }

    [Fact]
    public void BB13_Captains_TheirDecisionsKeepToTheInterval_AndTheCaptainIsTheBestTactician()
    {
        var (w, r) = CaptainTest(50, ww => ww.Add(KitOf(2, 1, 80_000, 0, 50), true));
        Assert.Equal(r.Members[r.IndexOfFighter(r.Captain)], r.Captain);
        Assert.Equal(50, w.Fighters[r.Captain].Kit.Tactics);
        w.StepWithAi();
        int next = r.NextDecision;
        Assert.Equal(1 + Math.Max(40, 200 - 50 * 2), next);                           // Tactics 50: 5 s
        Assert.Equal(OrderKind.Advance, r.Order.Kind);                                 // Tactics 0's rule: advance on the nearest enemy
        // the player ordering a hold keeps the captain quiet for 30 s unless the overlay hands the formation over
        var (w2, r2) = CaptainTest(50, ww => ww.Add(KitOf(2, 1, 80_000, 0, 50), true));
        r2.CaptainAlways = false; r2.Micro = 0;
        w2.Formations!.Apply(w2, r2, FormationOrder.Of(OrderKind.Hold), 1);
        for (int t = 0; t < 400; t++) w2.StepWithAi();
        Assert.Equal(OrderKind.Hold, r2.Order.Kind);
        for (int t = 0; t < 400; t++) w2.StepWithAi();                                 // past 30 s: the captain takes over
        Assert.Equal(OrderKind.Advance, r2.Order.Kind);
    }

    // ---------------------------------------------------------------- members in contact

    [Fact]
    public void Steering_AMemberPutOffHisSlotWalksBackToIt()
    {
        var (w, r) = Line(20);
        var f = w.Fighters[r.FighterAt(12)];
        f.X += 7000; f.Y -= 5000;
        for (int t = 0; t < 200; t++) w.StepWithAi();
        w.Formations!.SlotWorld(r, r.Slot[r.IndexOfFighter(f.Id)], w.Tick + 1, out int sx, out int sy);
        Assert.True(FightMath.Len(f.X - sx, f.Y - sy) < 1000, $"{FightMath.Len(f.X - sx, f.Y - sy)} mm off");
        Assert.True(r.Cohesion >= 950);
    }

    [Fact]
    public void Leash_AMemberInContactMayNotStrayFarFromHisSlot()
    {
        var w = fx.World(30);
        var mine = Add(w, 30, i => i % 2 == 0 ? 0 : 2);
        var foes = Add(w, 30, i => 2, team: 1, x0: 6000, y0: 0);
        var book = w.EnsureFormations();
        var r = book.Form(w, 0, 1, FormationKind.Infantry, mine, 0, 0, 1024, 0, Formation.Line, 3, place: true);
        book.Apply(w, r, FormationOrder.Of(OrderKind.Hold), 1);
        w.LodCentre = (0, 0);
        long worst = 0; var lastSlot = new int[mine.Count]; var changed = new int[mine.Count];
        for (int t = 0; t < 300; t++)
        {
            w.StepWithAi();
            foreach (int id in mine)
            {
                var f = w.Fighters[id]; int mi = r.IndexOfFighter(id); if (r.Slot[mi] < 0 || !f.Active) continue;
                if (r.Slot[mi] != lastSlot[mi]) { lastSlot[mi] = r.Slot[mi]; changed[mi] = t; }
                if (t - changed[mi] < 100) continue;                                     // a man who has just stepped into a new slot is walking to it
                book.SlotWorld(r, r.Slot[mi], w.Tick + 1, out int sx, out int sy);
                worst = Math.Max(worst, FightMath.Len(f.X - sx, f.Y - sy));
            }
        }
        output.WriteLine($"a hold: the furthest any member strayed from his slot: {worst} mm");
        Assert.True(worst < 4000, $"{worst} mm");           // the 1 m leash, a shove, and a body's worth of separation
    }

    [Fact]
    public void Archers_NeverShootAtAMeleeTheirOwnMenAreIn_AndHoldFireHolds()
    {
        var w = fx.World(31);
        var arch = Add(w, 8, i => 3, x0: -12_000, y0: 0);
        var friends = Add(w, 4, i => 2, x0: 20_000, y0: 0);
        var foe = w.Add(KitOf(2, 1, 20_800, 0, 50), true);                       // a foe locked with our men
        var book = w.EnsureFormations();
        var ra = book.Form(w, 0, 1, FormationKind.Archers, arch, -12_000, 0, 1024, 0, Formation.Loose, 2, place: true);
        Assert.False(book.SafeShot(w, w.Fighters[arch[0]], w.Fighters[foe.Id]));        // our men are within 5 m of him
        w.Fighters[friends[0]].X = 60_000;
        w.Fighters[friends[1]].X = 60_000; w.Fighters[friends[2]].X = 60_000; w.Fighters[friends[3]].X = 60_000;
        Assert.True(book.SafeShot(w, w.Fighters[arch[0]], w.Fighters[foe.Id]));
        ra.Fire = FireMode.Hold; ra.Micro = int.MaxValue / 2;
        for (int t = 0; t < 120; t++) w.StepWithAi();
        Assert.Equal(0, w.Fighters.Where(f => arch.Contains(f.Id)).Sum(f => (int)f.Serial + (int)f.ShotSerial));
        book.ShootAt(ra, 5);
        Assert.False(book.SafeShot(w, w.Fighters[arch[0]], w.Fighters[foe.Id]));        // not the formation ordered
    }

    [Fact]
    public void Routed_RunToTheirSidesEntryPoint()
    {
        var (w, r) = Line(12);
        w.Formations!.SetEntry(0, -90_000, 0);
        var f = w.Fighters[r.FighterAt(0)];
        w.Add(KitOf(2, 1, 5000, 0, 50), true);
        f.Routing = true;
        for (int t = 0; t < 100; t++) w.StepWithAi();
        Assert.True(f.X < -2000, $"x {f.X}");
    }

    // ---------------------------------------------------------------- orders in the field

    [Fact]
    public void Orders_MoveToMarchesAndHolds_AndWheelPivotsOnAFlank()
    {
        var (w, r) = Line(40);
        var b = w.Formations!;
        var mv = FormationOrder.Of(OrderKind.MoveTo); mv.TargetX = 30_000; mv.TargetY = 8000;
        b.Apply(w, r, mv, 1);
        for (int t = 0; t < 1000 && r.Order.Kind == OrderKind.MoveTo; t++) w.StepWithAi();
        Assert.Equal(OrderKind.Hold, r.Order.Kind);
        Assert.True(FightMath.Len(r.AnchorX - 30_000, r.AnchorY - 8000) <= 3000);
        Assert.True(r.Cohesion >= 800, $"cohesion {r.Cohesion}");
        // wheel about the right flank: the right flank slot stays put while the line swings 90 degrees to face +y
        int rf = r.Files - 1;
        b.SlotWorld(r, rf, w.Tick + 1, out int px, out int py);
        var wh = FormationOrder.Of(OrderKind.Wheel); wh.FaceX = 0; wh.FaceY = 1024; wh.Arg = 1;
        b.Apply(w, r, wh, w.Tick + 1);
        for (int t = 0; t < 400 && (r.WantFx != 0 || r.WantFy != 0); t++) w.Step(); // (the turn runs in the formation step)
        for (int t = 0; t < 400 && (r.WantFx != 0 || r.WantFy != 0); t++) { b.Step(w); w.Step(); }
        Assert.True(FightMath.Dot(r.FaceX, r.FaceY, 0, 1024) >= 1020);
        b.SlotWorld(r, rf, w.Tick + 1, out int qx, out int qy);
        Assert.InRange(FightMath.Len(qx - px, qy - py), 0, 60);
    }

    [Fact]
    public void Orders_SetAgainstCavalryMakesASchiltronOfPolearms_AndAFrontageRedrawsTheRanks()
    {
        var w = fx.World(33);
        var ids = Add(w, 30, i => i < 20 ? 1 : 2);
        var b = w.EnsureFormations();
        var r = b.Form(w, 0, 1, FormationKind.Infantry, ids, 0, 0, 1024, 0, Formation.Line, 4, place: true);
        var fr = FormationOrder.Of(OrderKind.Line); fr.Frontage = 4000;
        b.Apply(w, r, fr, 1);
        Assert.Equal(4, r.Files);
        Assert.Equal(8, r.Ranks);                                                     // 30 men in files of 4: eight ranks
        b.Apply(w, r, FormationOrder.Of(OrderKind.SetAgainstCavalry), 1);
        Assert.Equal(Formation.Schiltron, r.Shape);
        Assert.Equal(0, FormationRulesPace(w, r));
    }

    private static int FormationRulesPace(FightWorld w, FormationRow r) => w.Formations!.Rules.Shape(r.Shape).WalkMmps;

    // ---------------------------------------------------------------- the save round trip and determinism

    private FightWorld Battle(ulong seed)
    {
        var w = fx.World(seed);
        var a = Add(w, 36, i => i % 3, x0: -9000, y0: -18, tactics: i => i == 4 ? 40 : 10);
        var b = Add(w, 36, i => i % 3, team: 1, x0: 9000, y0: -18, tactics: i => i == 4 ? 40 : 10);
        foreach (int id in b) { var f = w.Fighters[id]; }
        var book = w.EnsureFormations();
        var ra = book.Form(w, 0, 1, FormationKind.Infantry, a, -9000, 0, 1024, 0, Formation.Line, 3, place: true);
        var rb = book.Form(w, 1, 1, FormationKind.Infantry, b, 9000, 0, -1024, 0, Formation.Line, 3, place: true);
        ra.CaptainAlways = rb.CaptainAlways = true;
        book.SetEntry(0, -200_000, 0); book.SetEntry(1, 200_000, 0);
        w.LodCentre = (0, 0);
        return w;
    }

    [Fact]
    public void Determinism_TheSameBattleTwice_TheSameHash()
    {
        var x = Battle(40); var y = Battle(40);
        for (int t = 0; t < 700; t++) { x.StepWithAi(); y.StepWithAi(); }
        Assert.Equal((x.Hash(), x.MoraleHash()), (y.Hash(), y.MoraleHash()));
        Assert.True(x.Fighters.Any(f => f.Act is FAct.Down or FAct.Dead), "the lines should have met and hurt someone");
        output.WriteLine($"battle of 72 after 700 ticks: down or dead {x.Fighters.Count(f => f.Act is FAct.Down or FAct.Dead)}, routing {x.Fighters.Count(f => f.Routing)}, cohesion {x.Formations!.Rows[0].Cohesion}/{x.Formations.Rows[1].Cohesion}");
    }

    [Fact]
    public void Save_SaveLoadContinue_EqualsTheUninterruptedBattle()
    {
        var full = Battle(41); var part = Battle(41);
        for (int t = 0; t < 260; t++) { full.StepWithAi(); part.StepWithAi(); }
        var bytes = FightRecord.Capture(part).ToBytes();
        var back = FightRecord.FromBytes(bytes).Restore(fx.C);
        Assert.NotNull(back.Formations);
        Assert.Equal(part.Formations!.Pack(), back.Formations!.Pack());
        Assert.Equal((part.Hash(), part.MoraleHash()), (back.Hash(), back.MoraleHash()));
        back.LodCentre = part.LodCentre;
        for (int t = 0; t < 300; t++) { full.StepWithAi(); back.StepWithAi(); if (t % 50 == 0) Assert.Equal((full.Hash(), full.MoraleHash()), (back.Hash(), back.MoraleHash())); }
        Assert.Equal((full.Hash(), full.MoraleHash()), (back.Hash(), back.MoraleHash()));
        Assert.Equal(full.Formations!.Pack(), back.Formations!.Pack());
        // a save, a load and a save again give the same bytes
        Assert.Equal(FightRecord.Capture(full).ToBytes(), FightRecord.Capture(back).ToBytes());
    }

    [Fact]
    public void Save_AFightWithoutFormations_CarriesNoFormationSection()
    {
        var w = FightCalibration.Skirmish(fx.C, 5, 5);
        for (int t = 0; t < 40; t++) w.StepWithAi();
        Assert.Null(FightRecord.Capture(w).Formations);
        Assert.Null(w.Formations);
    }
}

/// <summary>BB-B's share of the live tick budget (battle-bubble-and-formations 10), by itself in the timing collection.</summary>
[Collection(TimingCollection.Name)]
public class FormationBudgetTests(FightFixture fx, ITestOutputHelper output) : IClassFixture<FightFixture>
{
    private static readonly WeaponInstance Spear = new("spear", "steel");

    private FighterKit KitOf(int cls, int team, int x, int y, int skill, int tactics = -1) => (cls switch
    {
        0 => fx.Kit($"s{x}", team, x, y, skill: skill, shield: "heater"),
        1 => fx.Kit($"p{x}", team, x, y, weapon: Spear, skill: skill),
        2 => fx.Kit($"w{x}", team, x, y, skill: skill),
        _ => new FighterKit { Name = $"a{x}", Team = team, Launcher = "longbow", Ammo = "bodkin_arrow", AmmoCount = 12, Worn = [new WornItem("tunic", "linen")], Skill = skill, ArcherySkill = skill, X = x, Y = y, FaceX = team == 0 ? 1024 : -1024 },
    }) with { Tactics = tactics, Courage = 100 };


    private FightWorld Big(int perSide, int rowSize)
    {
        var w = fx.World(50); w.RecordEvents = false;
        var book = w.EnsureFormations();
        for (int team = 0; team < 2; team++)
        {
            int rows = perSide / rowSize;
            for (int k = 0; k < rows; k++)
            {
                var ids = new List<int>();
                for (int i = 0; i < rowSize; i++)
                {
                    int cls = i % 5 == 4 ? 3 : i % 3 == 0 ? 0 : i % 3 == 1 ? 1 : 2;
                    ids.Add(w.Add(KitOf(cls, team, 0, 0, 30 + i * 7 % 40, i == 0 ? 30 : 0)).Id);
                }
                int ax = team == 0 ? -10_000 : 10_000;
                var r = book.Form(w, team, (byte)(k + 1), FormationKind.Infantry, ids, ax, (k - rows / 2) * 34_000, team == 0 ? 1024 : -1024, 0, Formation.Line, 4, place: true);
                r.CaptainAlways = true;
            }
        }
        w.LodCentre = (-10_000, 0);
        return w;
    }

    [Fact]
    public void Budget_2000SoldiersInFormations_StepsWithinTheLiveTickBudget()
    {
        var warm = Big(100, 50);
        for (int i = 0; i < 100; i++) warm.StepWithAi();
        double Pass()
        {
            var w = Big(1000, 100);
            for (int i = 0; i < 20; i++) w.StepWithAi();
            var sw = new Stopwatch(); double sum = 0, worst = 0; int n = 600;
            var all = new List<(double Ms, int Tick)>();
            for (int i = 0; i < n; i++) { sw.Restart(); w.StepWithAi(); double ms = sw.Elapsed.TotalMilliseconds; sum += ms; worst = Math.Max(worst, ms); all.Add((ms, w.Tick)); }
            output.WriteLine("worst ticks: " + string.Join(", ", all.OrderByDescending(x => x.Ms).Take(6).Select(x => $"{x.Tick}: {x.Ms:0.0}")));
            output.WriteLine($"2,000 soldiers in 20 formations of 100, ticks 21..620 (contact from about tick 200): average {sum / n:0.00} ms, worst {worst:0.0} ms");
            return sum / n;
        }
        double avg = TestBudget.BestPass(25, 3, Pass);
        Assert.True(avg <= TestBudget.Ms(25), $"average {avg:0.00} ms");
    }
}
