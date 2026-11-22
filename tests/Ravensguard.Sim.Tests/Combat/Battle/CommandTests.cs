using System.Diagnostics;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Tests.Combat.Melee2;

namespace Ravensguard.Sim.Tests.Combat.Battle;

/// <summary>
/// Phase 7 BB-D (battle-bubble-and-formations 4.8, 11): the command path under the F-keys, the order wheel and the tactical overlay. BB-11 again, through the hero's recorded inputs and the overlay's draft (a
/// paused game gives no free time); the encoding of the three inputs and their replay form; the draft's undo and clear. BB-17 (keys) is the Godot side: <c>-- --check-keymap</c> and the self-test.
/// </summary>
[Collection("fight")]
public class CommandTests(FightFixture fx)
{
    private (FightWorld W, FormationBook B, FormationRow Far, int Hero) Scene(int farMm = 30_000, int leadership = 50)
    {
        var w = fx.World(7);
        var mine = new List<int>(); var far = new List<int>();
        for (int i = 0; i < 8; i++) mine.Add(w.Add(fx.Kit($"m{i}", 0, i * 7 % 5, i, skill: 40) with { Courage = 100 }).Id);
        for (int i = 0; i < 8; i++) far.Add(w.Add(fx.Kit($"f{i}", 0, farMm + i * 7 % 5, i, skill: 40) with { Courage = 100, Tactics = i == 0 ? 20 : 0 }).Id);
        var book = w.EnsureFormations();
        book.Form(w, 0, 1, FormationKind.Retinue, mine, 0, 0, 1024, 0, place: true);
        var rf = book.Form(w, 0, 2, FormationKind.Infantry, far, farMm, 0, 1024, 0, place: true);
        book.SetHero(w, mine[0], leadership);
        foreach (var q in book.Rows) q.Micro = int.MaxValue / 2;                    // the captains stay quiet: the tests are about the player's orders
        return (w, book, rf, mine[0]);
    }

    private static void Tick(FightWorld w) { w.Formations!.Step(w); w.Step(); }

    private static int Until(FightWorld w, FormationRow r, OrderKind k, int max = 2000)
    {
        for (int t = 1; t <= max; t++) { Tick(w); if (r.Order.Kind == k) return t; }
        return -1;
    }

    [Fact]
    public void BB11_TheHeroOrder_GoesThroughTheRecordedInput_AndArrivesAfterTheLeadershipDelay()
    {
        var (w, b, far, hero) = Scene();
        w.Input(hero, FormationCommands.Order(2, OrderKind.Advance));
        var o = far.Pending[0];
        Assert.Equal(40, o.ArriveTick - o.IssuedTick);                              // Leadership 50: 80 - 40
        Assert.Equal(hero, o.By);
        Assert.Equal(40, Until(w, far, OrderKind.Advance));
    }

    [Fact]
    public void BB11_Pause_AnOrderStagedInTheOverlayAndSentOnResumeArrivesTheSameTicksAfterResume()
    {
        int unpaused, paused;
        ulong hashUnpaused, hashPaused;
        {
            var (w, _, far, hero) = Scene();
            Tick(w); Tick(w);
            w.Input(hero, FormationCommands.Order(2, OrderKind.Advance));
            unpaused = Until(w, far, OrderKind.Advance);
            for (int t = 0; t < 100; t++) Tick(w);
            hashUnpaused = w.Hash();
        }
        {
            var (w, _, far, hero) = Scene();
            Tick(w); Tick(w);                                                       // the overlay opens: the fight does not step
            var draft = new OrderDraft();
            draft.Place(2, FormationCommands.Order(2, OrderKind.Advance));
            var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < 60) { }   // sixty "real seconds" (scaled) of the game standing still
            Assert.Equal(0, far.PendingCount);                                      // staged, not sent: nothing has reached the sim
            draft.Commit(i => w.Input(hero, i));
            Assert.Equal(2, far.Pending[0].IssuedTick);
            Assert.Equal(42, far.Pending[0].ArriveTick);
            paused = Until(w, far, OrderKind.Advance);
            for (int t = 0; t < 100; t++) Tick(w);
            hashPaused = w.Hash();
        }
        Assert.Equal(40, unpaused);
        Assert.Equal(unpaused, paused);
        Assert.Equal(hashUnpaused, hashPaused);                                     // the paused run is the unpaused run
    }

    [Fact]
    public void BB11_TheDraft_UndoTakesTheLastPlacementBack_ClearTakesARow_CommitEmptiesIt()
    {
        var d = new OrderDraft();
        d.Place(2, FormationCommands.Order(2, OrderKind.Hold));
        d.Place(2, FormationCommands.Order(2, OrderKind.MoveTo, 1000, 2000, queue: true), FormationCommands.Face(2, 0, 1024, 20_000));
        d.Place(3, FormationCommands.Order(3, OrderKind.Retreat));
        Assert.Equal(3, d.Count);
        Assert.True(d.Undo());
        Assert.Equal(2, d.Count);
        d.Clear(2);
        Assert.Equal(0, d.Count);
        Assert.False(d.Undo());
        d.Place(2, FormationCommands.Order(2, OrderKind.Hold), FormationCommands.Captain(2, true));
        var sent = new List<FightInput>();
        d.Commit(sent.Add);
        Assert.Equal(2, sent.Count);
        Assert.Equal(0, d.Count);
        d.Commit(sent.Add);
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void Encoding_AnOrderWithAPointAndADrawnFrontage_ReachesTheRowAsPlaced()
    {
        var (w, b, far, hero) = Scene();
        w.Input(hero, FormationCommands.Order(2, OrderKind.MoveTo, 31_000, -4_500));
        w.Input(hero, FormationCommands.Face(2, 0, 1024, 20_000));
        var o = far.Pending[0];
        Assert.Equal(OrderKind.MoveTo, o.Kind);
        Assert.Equal((31_000, -4_500), (o.TargetX, o.TargetY));
        Assert.Equal((0, 1024), (o.FaceX, o.FaceY));
        Assert.Equal(20_000, o.Frontage);
        // a face with no order given this tick decorates nothing
        for (int t = 0; t < 3; t++) Tick(w);
        w.Input(hero, FormationCommands.Face(2, 1024, 0, 5_000));
        Assert.Equal(20_000, far.Pending[0].Frontage);
        // negative components survive the packing
        w.Input(hero, FormationCommands.Order(2, OrderKind.Hold));
        w.Input(hero, FormationCommands.Face(2, -724, -724, 0));
        Assert.Equal((-724, -724), (far.Pending[1].FaceX, far.Pending[1].FaceY));
    }

    [Fact]
    public void Queue_ShiftPlacesAnOrderAfterTheOneInForce_AndFollowMeFollowsTheHero()
    {
        var (w, b, far, hero) = Scene();
        w.Input(hero, FormationCommands.Order(2, OrderKind.MoveTo, far.AnchorX + 6000, 0));
        w.Input(hero, FormationCommands.Order(2, OrderKind.MoveTo, far.AnchorX + 6000, 9000, queue: true));
        Assert.True(far.Pending[1].Queued);
        w.Input(hero, FormationCommands.Order(2, OrderKind.FollowMe));
        Assert.Equal(hero, far.Pending[2].TargetFighter);
    }

    [Fact]
    public void Target_AnAttackOnAnEnemyFormationNamesItByRowIndex_AndTheDelayPreviewIsTheDelayGiven()
    {
        var w = fx.World(7);
        var mine = new List<int>(); var foe = new List<int>();
        for (int i = 0; i < 6; i++) { mine.Add(w.Add(fx.Kit($"m{i}", 0, i, i, skill: 40)).Id); foe.Add(w.Add(fx.Kit($"e{i}", 1, 20_000 + i, i, skill: 40)).Id); }
        var book = w.EnsureFormations();
        var r1 = book.Form(w, 0, 1, FormationKind.Infantry, mine, 0, 0, 1024, 0, place: true);
        var re = book.Form(w, 1, 1, FormationKind.Infantry, foe, 20_000, 0, -1024, 0, place: true);
        book.SetHero(w, mine[0], 50);
        var probe = FormationOrder.Of(OrderKind.AttackFormation);
        int preview = book.DelayOf(w, r1, ref probe, mine[0], 50, out _);
        w.Input(mine[0], FormationCommands.At(1, OrderKind.AttackFormation, re.Index));
        var o = r1.Pending[0];
        Assert.Equal(re.Index + 1, o.TargetFormation);
        Assert.Equal(preview, o.ArriveTick - o.IssuedTick);
        Assert.Equal(OrderSignal.Voice, probe.Signal);
        for (int t = 0; t < 60; t++) w.StepWithAi();
        Assert.Equal(OrderKind.AttackFormation, r1.Order.Kind);
        Assert.Equal(re.Index, r1.TgtRow);                                          // it marches on the formation named
    }

    [Fact]
    public void Captain_TheToggleSetsTheFormationToBeCommandedByHim()
    {
        var (w, b, far, hero) = Scene();
        Assert.False(far.CaptainAlways);
        w.Input(hero, FormationCommands.Captain(2, true));
        Assert.True(far.CaptainAlways);
        w.Input(hero, FormationCommands.Captain(2, false));
        Assert.False(far.CaptainAlways);
    }

    [Fact]
    public void OnlyTheHeroesOwnSide_ACommandToARowOfTheEnemyOrToNoRowIsIgnored()
    {
        var w = fx.World(7);
        var mine = new List<int> { w.Add(fx.Kit("m0", 0, 0, 0, skill: 40)).Id, w.Add(fx.Kit("m1", 0, 1, 1, skill: 40)).Id };
        var foe = new List<int> { w.Add(fx.Kit("e0", 1, 5000, 0, skill: 40)).Id, w.Add(fx.Kit("e1", 1, 5001, 1, skill: 40)).Id };
        var book = w.EnsureFormations();
        book.Form(w, 0, 1, FormationKind.Infantry, mine, 0, 0, 1024, 0, place: true);
        var re = book.Form(w, 1, 1, FormationKind.Infantry, foe, 5000, 0, -1024, 0, place: true);
        book.SetHero(w, mine[0], 50);
        w.Input(mine[0], FormationCommands.Order(1, OrderKind.Retreat));            // row 1 of HIS side is his own
        Assert.Equal(0, re.PendingCount);
        w.Input(mine[0], FormationCommands.Order(9, OrderKind.Retreat));            // no such row
        Assert.Equal(0, re.PendingCount);
        var bare = fx.World(3);
        bare.Add(fx.Kit("x", 0, 0, 0, skill: 40));
        bare.Input(0, FormationCommands.Order(1, OrderKind.Hold));                   // a fight with no formations
    }

    [Fact]
    public void Replay_TheThreeInputsSurviveTheCommandLog()
    {
        foreach (var i in new[]
        {
            FormationCommands.Order(7, OrderKind.MoveTo, -123_456, 654_321, queue: true), FormationCommands.Order(3, OrderKind.Wheel, 0, 0, pivotRight: true),
            FormationCommands.Face(4, -1024, 887, 33_000), FormationCommands.Captain(200, true),
        })
        {
            var back = ReplayCommand.Fight(41, i).AsFightInput;
            Assert.Equal(i, back);
        }
    }

    [Fact]
    public void Determinism_TheSameCommandsOnTheSameTicks_GiveTheSameFight()
    {
        ulong Run()
        {
            var (w, _, _, hero) = Scene(60_000);
            for (int t = 0; t < 300; t++)
            {
                if (t == 10) w.Input(hero, FormationCommands.Order(2, OrderKind.MoveTo, 20_000, 3_000));
                if (t == 10) w.Input(hero, FormationCommands.Face(2, 0, 1024, 12_000));
                if (t == 90) w.Input(hero, FormationCommands.Order(2, OrderKind.ShieldWall));
                if (t == 120) w.Input(hero, FormationCommands.Order(1, OrderKind.Column));
                Tick(w);
            }
            return w.Hash();
        }
        Assert.Equal(Run(), Run());
    }
}
