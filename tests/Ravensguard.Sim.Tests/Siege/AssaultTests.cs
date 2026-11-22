using Ravensguard.Content;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>A fight over the SG-A curtain (x 36..55 m, y 40..42 m, a walk and a parapet at +1) with the assault laid over it: the fight's mm are the world's (origin 0).</summary>
internal sealed class Scene
{
    public required FightFixture Fx;
    public required FightWorld W;
    public required WallRig Rig;
    public required AssaultLayer A;
    public EngineTests.Field? Field;
    public List<(string Type, string Key, IReadOnlyList<ChronParam> Ps, IReadOnlyList<Cause> Causes)> Chron => Rig.Chron;

    public static Scene Make(FightFixture fx, ulong seed = 1, bool engines = false, long gameSec0 = 8 * 3600)
    {
        var w = new FightWorld(fx.C, seed, 1, gameSec0) { RecordEvents = true };
        var field = engines ? EngineTests.Field.Make() : null;
        var rig = field?.Rig ?? WallRig.Make(EconFixture.Content);
        var a = new AssaultLayer(w, rig.W, attackTeam: 0, defendTeam: 1) { Chronicle = (when, type, key, ps, subjects, causes, data) => { rig.Chron.Add((type, key, ps, causes)); return new EntityId(950_000UL + (ulong)rig.Chron.Count); } };
        var s = new Scene { Fx = fx, W = w, Rig = rig, A = a, Field = field };
        if (field != null) { a.Engines = field.Book; a.EngineW = field.W; field.W.People = EngineFight.People(w); }
        return s;
    }

    /// <summary>A fighter of a team standing at world (x, y) mm, optionally on a level; no brain drives him unless <paramref name="ai"/>.</summary>
    public Fighter Add(int team, int x, int y, int level = 0, int faceY = 0, bool ai = false, string? launcher = null, string? ammo = null, int ammoCount = 0, int skill = 50)
    {
        var f = W.Add(Fx.Kit($"{(team == 0 ? "A" : "D")}{W.Fighters.Count}", team, x, y, faceX: faceY == 0 ? (team == 0 ? 1024 : -1024) : 0, faceY: faceY, launcher: launcher, ammo: ammo, ammoCount: ammoCount, skill: skill), !ai);
        if (level != 0) A.SetLevel(f.Id, level);
        return f;
    }

    /// <summary>A wooden gate in the curtain at x, the whole thickness (y 40 to 42), set into the chunk store before anyone looks.</summary>
    public void Gate(int x, string piece = "gate_wood")
    {
        var ops = new List<Ravensguard.Sim.Save.ChunkOp>();
        foreach (int y in new[] { 40, 41, 42 }) ops.Add(Ravensguard.Sim.Save.ChunkOp.SetTile(x - 32, y - 32, Ravensguard.Sim.WorldGen.Chunks.TileCode.Make(Ravensguard.Sim.Live.GroundKind.Dirt, 0, Rig.Pal.Of(piece), 0)));
        Rig.Store.AppendDelta(new Ravensguard.Sim.Save.ChunkKey(1, 1, 0), ops);
    }

    public void Step(int n, Action<int>? each = null) { for (int i = 0; i < n; i++) { each?.Invoke(i); W.Step(); } }

    /// <summary>Everyone up on a level (and not held) walks east along it.</summary>
    public void WalkersEast()
    {
        foreach (var f in W.Fighters) if (A.LevelOf(f.Id) >= 1 && !A.IsHeld(f.Id) && f.Active) f.Cmd.MoveX = 1024;
    }

    public static int MassKg(Fighter f) => Math.Max(20, f.Kit.SizeCm3 / 1000 + f.ArmourG / 1000 + f.OverKg);
}

/// <summary>P7-SG-C: the assault (sieges-and-engines SG-8 to SG-12): ladders, a siege tower and its drawbridge, height and merlons, murder holes, a night sally; and a worked assault with ladders and a tower on a breached curtain.</summary>
[Collection("fight")]
public class AssaultTests(FightFixture fx, ITestOutputHelper output)
{
    // ------------------------------------------------------------------ SG-8: ladders

    [Fact]
    public void SG8_ALadderIsRaisedIn4Seconds_HoldsThreeClimbers_EachClimbs4Seconds_AndIsPushedOffAtAthletics50With700pm_ClimbersFall6m()
    {
        var s = Scene.Make(fx);
        Assert.Equal(80, s.A.R.LadderRaiseTicks); Assert.Equal(3, s.A.R.LadderClimbers); Assert.Equal(80, s.A.R.LadderClimbTicks); Assert.Equal(60, s.A.R.PushTicks);
        var carriers = new[] { s.Add(0, 44_800, 37_000), s.Add(0, 46_200, 37_000) };
        var ups = Enumerable.Range(0, 5).Select(i => s.Add(0, 43_600 + i * 700, 35_500)).ToArray();
        var lad = s.A.AddLadder(0, 45, 38, 45, 41, carriers.Select(c => c.Id), ups.Select(u => u.Id));
        int raising = -1, standing = -1, most = 0;
        var joined = new Dictionary<int, int>(); var arrived = new Dictionary<int, int>();
        for (int i = 0; i < 900; i++)
        {
            s.WalkersEast(); s.W.Step();
            if (raising < 0 && lad.Stage == LadderStage.Raising) raising = s.W.Tick;
            if (standing < 0 && lad.Stage == LadderStage.Standing) standing = s.W.Tick;
            most = Math.Max(most, lad.Climbers.Count);
            foreach (var c in lad.Climbers) joined.TryAdd(c.Id, s.W.Tick);
            foreach (var f in s.W.Fighters) if (s.A.LevelOf(f.Id) == 1 && !arrived.ContainsKey(f.Id)) arrived[f.Id] = s.W.Tick;
        }
        Assert.Equal(80, standing - raising);                                              // raised in 4 s
        Assert.Equal(3, most);                                                             // three climbers at once, no more
        Assert.Equal(7, arrived.Count);                                                    // the carriers climb behind the rest
        Assert.InRange(joined.Min(j => arrived[j.Key] - j.Value), 80, 82); Assert.All(joined, j => Assert.InRange(arrived[j.Key] - j.Value, 80, 100));       // 4 s up (a man waits at the top only while the one before steps clear)
        Assert.All(arrived.Keys, id => Assert.Equal(1, s.A.LevelOf(id)));
        output.WriteLine($"raised in {standing - raising} ticks; {joined.Count} climbed, at most {most} at once; climbs took {string.Join(",", joined.Select(j => arrived[j.Key] - j.Value))} ticks");

        // pushed off: Athletics 50 is 700 permille a try (500 + 10 x (50 - 30)); the climbers on it fall 6 m through the resolver
        Assert.Equal(700, AssaultMath.PushPm(s.A.R, 50)); Assert.Equal(500, AssaultMath.PushPm(s.A.R, 30)); Assert.Equal(800, AssaultMath.PushPm(s.A.R, 60)); Assert.Equal(950, AssaultMath.PushPm(s.A.R, 80));
        int first = 0, n = 160, falls = 0, tries = 0;
        for (ulong seed = 1; seed <= (ulong)n; seed++)
        {
            var (ps, l, d, climbers) = PushScene(seed);
            bool fell = false;
            for (int i = 0; i < 600 && l.Stage != LadderStage.Down; i++) { ps.W.Step(); if (i == 69) ps.A.PutAt(d.Id, 1, 45_500, 41_500); }
            Assert.Equal(LadderStage.Down, l.Stage);
            if (l.PushTries == 0) first++;
            tries += l.PushTries + 1;
            var fell6 = ps.W.Events.Where(e => e.Kind == FightEventKind.Fell).ToList();
            Assert.Equal(3, fell6.Count);                                                 // all three on it fell
            Assert.All(climbers, c => { Assert.Equal(0, ps.A.LevelOf(c.Id)); Assert.False(ps.A.IsHeld(c.Id)); });
            int expect = (int)(LayerTest.FallMomentumMilli(Scene.MassKg(climbers[0]), 6000, 0) / 1000);
            Assert.All(fell6, e => Assert.Equal(expect, e.A));                            // 6 m, the Phase 4 fall (100 x kg x 3 x 6)
            falls += fell6.Count; if (fell6.Count > 0) fell = true;
            Assert.True(fell);
        }
        output.WriteLine($"first-try pushes {first} of {n} ({first * 1000 / n} pm), {tries / (double)n:0.00} tries each, {falls} climbers fell 6 m");
        Assert.InRange(first * 1000 / n, 620, 780);
    }

    /// <summary>A standing ladder with three climbers waiting at its top and a defender (Athletics 50) three metres along the walk who steps to the top at tick 70.</summary>
    private (Scene S, SiegeLadder L, Fighter D, Fighter[] Climbers) PushScene(ulong seed)
    {
        var s = Scene.Make(fx, seed);
        var ups = Enumerable.Range(0, 3).Select(i => s.Add(0, 44_800 + i * 700, 37_500)).ToArray();
        var d = s.Add(1, 48_500, 41_500, level: 1);
        s.A.AthleticsOf = f => f.Id == d.Id ? 50 : 30;
        var l = s.A.Stand(s.A.AddLadder(0, 45, 38, 45, 41, [], ups.Select(u => u.Id)));
        return (s, l, d, ups);
    }

    // ------------------------------------------------------------------ SG-9: the tower

    /// <summary>A siege tower 4 x 4 facing along +y with its centre at (45.5 m, y), a springald-free field over the same curtain.</summary>
    private static (VehicleRow V, Engine E) Tower(Scene s, long yMm)
    {
        var (v, e) = s.Field!.Raise("siege_tower", 45_500, yMm);
        v.Facing = 64;
        return (v, e);
    }

    [Fact]
    public void SG9_ATowerIsPushedAt0_25MetresASecondOnFirmGround_StopsInMudOrOnASlope_AndItsDrawbridgeIsA2mPortalToTheWalk()
    {
        var s = Scene.Make(fx, engines: true);
        var (v, e) = Tower(s, 30_000);
        s.Field!.Book.Assign(e, [], Enumerable.Range(0, 30).Select(i => EntityId.Make(IdKind.Person, 800UL + (ulong)i)));
        var book = s.Field.Book;
        Assert.Equal(25, book.SpeedOn(e, Surface.DryRoad, 0).SpeedCmps);                                    // 0.25 m/s with thirty on dry level ground
        Assert.True(book.SpeedOn(e, Surface.MudRoad, 0).CannotMove);                                       // mud stops it
        Assert.True(book.SpeedOn(e, Surface.DryRoad, 60).CannotMove);                                      // a 6% slope stops it
        Assert.Equal(1500, book.Stat(e).BridgeMm); Assert.Equal(1, book.Stat(e).TopZ);
        var tw = s.A.AddTower(v.Id);
        // pushed toward the wall at a quarter of a metre a second: 10 s (200 ticks) is 2.5 m
        long y0 = v.YMm; book.Push(e, 45_500, 40_000, 200, Surface.DryRoad, 0);
        Assert.InRange(v.YMm - y0, 2400, 2700);
        // far from the wall (front at 34.7 m... the walk's parapet is 5 m on) the bridge stays up and the deck ends at the tower's edge
        v.YMm = 32_700;
        var deckMan = s.Add(0, 45_500, 33_000, level: 1);
        for (int i = 0; i < 5; i++) s.W.Step();
        Assert.False(tw.Down); Assert.NotEmpty(tw.Deck);
        deckMan.Cmd.MoveY = 1024;
        for (int i = 0; i < 200; i++) s.W.Step();
        Assert.InRange(deckMan.Y, 33_000, 35_000);                                                          // he walked to the deck's front edge and no further
        // pushed up until its front is 1.3 m from the parapet: the bridge comes down, two metres wide
        v.YMm = 36_700;
        for (int i = 0; i < 5; i++) s.W.Step();
        Assert.True(tw.Down);
        Assert.Equal(2, tw.Bridge.Select(k => Ravensguard.Sim.Construction.ConstructionIds.Unpack(k).X).Distinct().Count());     // a 2 m front
        deckMan.Cmd.MoveY = 1024; s.A.PutAt(deckMan.Id, 1, 45_500, 38_000); deckMan.Cmd.MoveY = 1024;
        for (int i = 0; i < 300; i++) s.W.Step();
        Assert.True(deckMan.Y > 41_000, $"he stopped at {deckMan.Y}");                                         // over the bridge, over the parapet, onto the walk at z+1
        Assert.Equal(1, s.A.LevelOf(deckMan.Id));
        // a man beside the bridge is stopped by the deck's edge: the portal is two metres
        var wide = s.Add(0, 43_600, 38_000, level: 1); wide.Cmd.MoveY = 1024;
        for (int i = 0; i < 300; i++) s.W.Step();
        Assert.True(wide.Y < 39_800, $"he got to {wide.Y}");
        // the same on the ground: a man below the walk cannot use it
        var below = s.Add(0, 45_500, 38_000); below.Cmd.MoveY = 1024;
        for (int i = 0; i < 300; i++) s.W.Step();
        Assert.True(below.Y < 40_000);
        output.WriteLine($"tower deck {tw.Deck.Count} cells, bridge {tw.Bridge.Count} cells");
    }
}
