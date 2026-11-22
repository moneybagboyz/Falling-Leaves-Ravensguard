using Ravensguard.Content;
using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>P7-SG-C: the worked example, an assault with ladders and a siege tower on a breached curtain, run by the fight's own AI; and its determinism and its save, load and continue.</summary>
[Collection("fight")]
public class AssaultWorkedTests(FightFixture fx, ITestOutputHelper output)
{
    private sealed class Run
    {
        public required Scene S;
        public required SiegeLadder Ladder;
        public required TowerPost Tower;
        public required Fighter[] Ladders, Aboard, Breachers, OnWall, Inside;
        public int MostOnTheWalk, StoodTick = -1, BridgeTick = -1;
        public bool InsideBreached;
    }

    /// <summary>The curtain with a breach torn at x = 45..46 (the engines' work, SG-A's rule), a ladder party at x = 40, a tower at x = 52..53, a party through the breach; the garrison on the walk and in the street.</summary>
    private Run Build(ulong seed)
    {
        var s = Scene.Make(fx, seed, engines: true);
        foreach (int x in new[] { 45, 46 }) foreach (int y in new[] { 40, 41, 42 }) s.Rig.Batter(x, y);
        Assert.Single(s.Rig.W.Breaches(30, 30, 60, 50));
        var carriers = new[] { s.Add(0, 39_300, 36_000, ai: true), s.Add(0, 40_700, 36_000, ai: true) };
        var climbers = Enumerable.Range(0, 3).Select(i => s.Add(0, 38_500 + i * 900, 34_800, ai: true)).ToArray();
        var lad = s.A.AddLadder(0, 40, 38, 40, 41, carriers.Select(c => c.Id), climbers.Select(c => c.Id));
        var (tv, _) = s.Field!.Raise("siege_tower", 52_500, 36_700, now: s.W.GameSec0);
        tv.Facing = 64;
        var tw = s.A.AddTower(tv.Id);
        var party = Enumerable.Range(0, 4).Select(i => s.Add(0, 51_300 + i * 800, 33_500, ai: true)).ToArray();
        s.A.Board(tv.Id, party.Select(p => p.Id));
        var breach = Enumerable.Range(0, 6).Select(i => s.Add(0, 43_800 + i * 700, 35_500, ai: true)).ToArray();
        s.A.Order(breach.Select(b => b.Id), [(45_800, 38_500), (45_800, 41_500), (45_800, 44_500)]);
        var onWall = new List<Fighter>();
        foreach (var (x, y) in new[] { (38_500, 41_500), (41_500, 42_500), (53_500, 41_500), (50_500, 42_500) }) onWall.Add(s.Add(1, x, y, level: 1, ai: true));
        var inside = new[] { s.Add(1, 44_500, 46_500, ai: true), s.Add(1, 47_500, 46_500, ai: true), s.Add(1, 46_000, 48_500, ai: true) };
        s.A.Inside = (36, 43, 55, 63);
        s.A.Place = EntityId.Make(IdKind.Settlement, 1);
        return new Run { S = s, Ladder = lad, Tower = tw, Ladders = [.. carriers, .. climbers], Aboard = party, Breachers = breach, OnWall = [.. onWall], Inside = inside };
    }

    private void Go(Run r, int ticks)
    {
        var s = r.S;
        for (int i = 0; i < ticks; i++)
        {
            s.W.StepWithAi();
            if (r.StoodTick < 0 && r.Ladder.Stage == LadderStage.Standing) r.StoodTick = s.W.Tick;
            if (r.BridgeTick < 0 && r.Tower.Down) r.BridgeTick = s.W.Tick;
            r.MostOnTheWalk = Math.Max(r.MostOnTheWalk, s.W.Fighters.Count(f => f.Team == 0 && f.Active && s.A.LevelOf(f.Id) >= 1 && s.A.RiseOf(f.Id) == 0));
            if (r.Breachers.Any(b => b.Active && s.A.LevelOf(b.Id) == 0 && b.Y > 43_200)) r.InsideBreached = true;
        }
    }

    private static string Summary(Run r) => $"tick {r.S.W.Tick}: ladder {r.Ladder.Stage} (stood at {r.StoodTick}, pushed {r.Ladder.Pushed}), bridge down at {r.BridgeTick}, most attackers on the walk {r.MostOnTheWalk}, breach party inside {r.InsideBreached}, " +
        $"attackers up {r.S.W.Fighters.Count(f => f.Team == 0 && f.Active)}/{r.S.W.Fighters.Count(f => f.Team == 0)}, garrison up {r.S.W.Fighters.Count(f => f.Team == 1 && f.Active)}/{r.S.W.Fighters.Count(f => f.Team == 1)}";

    [Fact]
    public void TheWorkedExample_LaddersAndATowerAndABreachOnACurtain()
    {
        var r = Build(5);
        Go(r, 2400);
        output.WriteLine(Summary(r));
        Assert.True(r.StoodTick > 0 && r.StoodTick < 200);                        // the ladder was raised in the first moments (carriers walk up, 4 s to raise)
        Assert.True(r.BridgeTick > 0, "the drawbridge came down");                // the tower stood 1.3 m from the parapet
        Assert.Equal(4, r.Tower.Aboard);                                           // the party boarded and was put on the deck
        Assert.True(r.MostOnTheWalk >= 3, $"{r.MostOnTheWalk} on the walk");       // by ladder and bridge the besiegers reached the wall-walk at z+1
        Assert.True(r.InsideBreached);                                             // and the breach party walked through the breach (rubble, half pace) into the town
        // the assault is one entry in the chronicle: a root unless the siege entry exists
        var id = r.S.A.Finish(r.S.A.WalkLost ? "ch_sg_out_walk" : r.InsideBreached ? "ch_sg_out_breach" : "ch_sg_out_repulsed");
        var e = Assert.Single(r.S.Chron, c => c.Type == "siege_assault");
        Assert.Equal(CauseKind.Siege, e.Causes[0].Kind);
        Assert.NotEqual(default, id);
        Assert.Contains(e.Ps, p => p.Name == "lost_a"); Assert.Contains(e.Ps, p => p.Name == "lost_b");
    }

    [Fact]
    public void TheAssaultIsDeterministic_AndSaveLoadContinueEqualsTheUninterruptedRun()
    {
        // two runs alike
        var a = Build(11); Go(a, 1200);
        var b = Build(11); Go(b, 1200);
        Assert.Equal(a.S.W.Hash(), b.S.W.Hash());
        Assert.Equal(a.S.A.Capture().ToBytes(), b.S.A.Capture().ToBytes());
        // the uninterrupted run to 1,800
        Go(a, 600);
        // the other saved at 1,200: the fight, the assault and the chunk store (the walls), and the engines; loaded into fresh objects and continued
        var fightBytes = FightRecord.Capture(b.S.W).ToBytes();
        var assaultBytes = b.S.A.Capture().ToBytes();
        var section = b.S.Rig.Store.ToSection();
        var engines = SiegeSave.Capture(b.S.Field!.Book);
        var w2 = FightRecord.FromBytes(fightBytes).Restore(fx.C);
        var rig2 = b.S.Rig.Reload();
        var book2 = engines.Restore(rig2.Db, b.S.Field.B, 7);
        var ew2 = new EngineWorld { Walls = rig2.W, Engineering = _ => 0 };
        var a2 = AssaultLayer.Restore(AssaultRecord.FromBytes(assaultBytes), w2, rig2.W, book2, ew2);
        a2.Chronicle = (when, type, key, ps, subjects, causes, data) => new EntityId(1);
        Assert.Equal(b.S.W.Hash(), w2.Hash());                                       // the loaded fight is the saved one, its assault with it
        Assert.Equal(assaultBytes, a2.Capture().ToBytes());                            // a save, a load and a save: the same bytes
        for (int i = 0; i < 600; i++) { w2.StepWithAi(); }
        Assert.Equal(a.S.W.Hash(), w2.Hash());                                       // the loaded run goes on exactly as the one that was never stopped
        Assert.Equal(a.S.A.Capture().ToBytes(), a2.Capture().ToBytes());
        output.WriteLine(Summary(a));
        // the engine book hash: the tower stood still, nothing in it changed
        Assert.Equal(a.S.Field!.Book.Hash(), book2.Hash());
    }

    [Fact]
    public void TheAssaultSavesInTheSiegesSection_AdditivelyWithoutABump_AndAWorldWithNoAssaultKeepsItsBytes()
    {
        var r = Build(3); Go(r, 300);
        var rec = r.S.A.Capture();
        var world = EconFixture.WorldWith(EconFixture.NewEconomy(false));
        var plainBytes = SaveCodec.Encode(WorldSave.Capture(world, EconFixture.ContentHash, "0.1.0"));
        Assert.False(WorldSave.Capture(world, EconFixture.ContentHash, "0.1.0").Sections.ContainsKey(SiegeSave.SectionName));
        world.Assault = rec;
        var file = WorldSave.Capture(world, EconFixture.ContentHash, "0.1.0");
        Assert.True(file.Sections.ContainsKey(SiegeSave.SectionName));
        var bytes = SaveCodec.Encode(file);
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, EconFixture.ContentHash);
        Assert.NotNull(back.Assault);
        Assert.Equal(rec.ToBytes(), back.Assault!.ToBytes());
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));      // save, load, save
        back.Assault = null;
        Assert.Equal(plainBytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));  // and when the assault is over the section is gone and the bytes are what they were
        // an engine-only section of an older save (no assault key) still loads
        var old = SaveCodec.Serialize(new LegacySieges { Engines = [] });
        Assert.Null(SaveCodec.Deserialize<SiegeSave>(old).Assault);
    }

    [MessagePack.MessagePackObject]
    public sealed class LegacySieges { [MessagePack.Key(0)] public EngineRowSave[] Engines { get; set; } = []; }
}
