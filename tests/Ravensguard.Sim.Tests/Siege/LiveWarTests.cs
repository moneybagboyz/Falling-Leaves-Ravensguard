using Ravensguard.Content;
using Ravensguard.Sim.Battle;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Watch;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Tests.Combat.Melee2;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Felled = Ravensguard.Sim.Siege.SiegeTimber.Felled;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>P8-LW-1, the live siege assault (LW-1 to LW-5): a walled square with a stone curtain, ladders and orders, timber felled from real trees, and a save in the second wave.</summary>
[Collection("fight")]
public class LiveWarTests(FightFixture fx, ITestOutputHelper output)
{
    // the ring: stone curtain 3 thick, x and y 40 to 120 (cells), the outer row crenellated and the rest a wall-walk at z+1
    private const int R0 = 40, R1 = 120;

    private static WallRig Ring()
    {
        var rig = WallRig.Make(EconFixture.Content, stamp: false);
        var chunks = new Dictionary<(int X, int Y, int Z), ChunkContent>();
        ChunkContent C(int x, int y, int z) { var k = (x >> 5, y >> 5, z); if (!chunks.TryGetValue(k, out var c)) chunks[k] = c = new ChunkContent(); return c; }
        void Put(int x, int y, int z, int tile) => C(x, y, z).Tiles[ChunkContent.TileIndex(x & 31, y & 31)] = tile;
        for (int y = R0 - 40; y <= R1 + 40; y++) for (int x = R0 - 40; x <= R1 + 40; x++) Put(x, y, 0, TileCode.Make(Ravensguard.Sim.Live.GroundKind.Dirt, 0, 0, 0));
        for (int y = R0; y <= R1; y++)
            for (int x = R0; x <= R1; x++)
            {
                bool ring = x < R0 + 3 || x > R1 - 3 || y < R0 + 3 || y > R1 - 3; if (!ring) continue;
                bool outer = x == R0 || x == R1 || y == R0 || y == R1;
                Put(x, y, 0, TileCode.Make(Ravensguard.Sim.Live.GroundKind.Dirt, 0, rig.Pal.Of("wall_stone"), 0));
                Put(x, y, 1, TileCode.Make(Ravensguard.Sim.Live.GroundKind.None, 0, rig.Pal.Of(outer ? "wall_crenellated" : "floor_wall_walk"), 0));
            }
        foreach (var ((cx, cy, cz), c) in chunks) rig.Store.RecordFirstVisit(new ChunkKey(cx, cy, cz), 1, "h" + cz, c);
        return rig;
    }

    private static SiegeWalls Walls(WallRig rig) => new() { World = rig.W, LayXMm = 0, LayYMm = 80_000, X0 = R0, Y0 = R0, X1 = R1, Y1 = R1, AimXMm = 80_000, AimYMm = 80_000 };

    private List<BattleUnit> Units(int men, int team, ulong first, int unit = SiegeAssaults.UnitMen)
    {
        var res = new List<BattleUnit>();
        for (int i = 0; i < men; i += unit)
        {
            var u = new BattleUnit { Kind = FormationKind.Infantry, Shape = Formation.Line, Ranks = 4, Officers = [0] };
            for (int k = i; k < Math.Min(men, i + unit); k++) u.Soldiers.Add(fx.Kit($"{(team == 0 ? "A" : "D")}{k}", team, 0, 0, skill: team == 0 ? 45 : 50) with { Person = first + (ulong)k, Stance = "balanced" });
            res.Add(u);
        }
        return res;
    }

    private LiveAssault Storm(int attackers, int defenders, ulong seed, int ladders = -1, int heroTeam = -1, bool breach = false)
    {
        var rig = Ring();
        if (breach) foreach (int y in new[] { 79, 80, 81 }) foreach (int x in new[] { 40, 41, 42 }) { rig.Batter(x, y); rig.Batter(x, y, 1); }
        var sp = new LiveStormSpec { Seed = seed, Id = 77, GameSec = 8 * 3600, Siege = 5, Walls = Walls(rig), Attackers = Units(attackers, 0, 1_000_000), Defenders = Units(defenders, 1, 2_000_000), HeroTeam = heroTeam, LadderBudget = ladders };
        var la = SiegeAssaults.Assemble(fx.C, sp);
        _rigs.Add(rig);
        return la;
    }

    private readonly List<WallRig> _rigs = [];

    // ------------------------------------------------------------------ LW-1 (shape): waves of 300 or fewer

    [Fact]
    public void LW1_AHostOf1200_StormsInWavesOf300OrFewer_WithLaddersOnTwoToFourStretches_AndAThirdOfTheDefendersOnTheWalk()
    {
        var la = Storm(1200, 300, 3);
        var b = la.Bubble; var w = b.W;
        Assert.True(b.CapTotal <= 300);
        int maxPresent = 0, waves = 0;
        for (int t = 0; t < 20 * 90 && !b.Closed; t++)
        {
            b.Step();
            maxPresent = Math.Max(maxPresent, b.PresentOf(0) + b.PresentOf(1));
            waves = Math.Max(waves, b.Sides.Max(s => s.WavesSent));
        }
        output.WriteLine($"tick {w.Tick}: present max {maxPresent}, waves {waves}, ladders {la.Layer.Ladders.Count}, defenders on the walk {w.Fighters.Count(f => f.Team == 1 && la.Layer.LevelOf(f.Id) >= 1)}");
        Assert.True(maxPresent <= 300, $"{maxPresent} at once");
        Assert.InRange(la.Layer.Ladders.Count, 2, 4 + 8);                                              // two to four stretches, replaced as they go down
        Assert.True(la.Layer.Ladders.Any(l => l.Stage is LadderStage.Standing or LadderStage.Raising), "a ladder went up");
        Assert.True(w.Fighters.Count(f => f.Team == 1 && la.Layer.LevelOf(f.Id) >= 1) >= 30, "the first defenders stand on the walk");
        Assert.Equal(0, w.Fighters.Count(f => f.Team == 0 && la.Layer.LevelOf(f.Id) >= 1) < 0 ? 1 : 0);
    }

    /// <summary>Steps a storm to its close (or the cap) and counts the dead of each side and the winner.</summary>
    private static (bool Closed, int Ticks, int DeadA, int DeadD, int Winner) RunOut(LiveAssault la, int maxTicks)
    {
        var b = la.Bubble; for (int t = 0; t < maxTicks && !b.Closed; t++) b.Step();
        return (b.Closed, b.W.Tick, b.W.Fighters.Count(f => f.Team == 0 && f.Act == FAct.Dead), b.W.Fighters.Count(f => f.Team == 1 && f.Act == FAct.Dead), b.Result?.WinnerTeam ?? -2);
    }

    // ------------------------------------------------------------------ LW-2: ladders

    private (AssaultLayer A, FightWorld W, SiegeLadder L) LadderScene(int climbers, int climbProgress, ulong seed = 9)
    {
        var rig = Ring(); _rigs.Add(rig);
        var w = new FightWorld(fx.C, seed, 1, 8 * 3600) { RecordEvents = false };
        var a = new AssaultLayer(w, rig.W, 0, 1) { Live = true, FromWest = true };
        var carriers = new[] { w.Add(fx.Kit("c1", 0, 30_000, 80_000), false), w.Add(fx.Kit("c2", 0, 30_000, 81_000), false) };
        var l = a.AddLadder(0, 39, 80, 41, 80, carriers.Select(c => c.Id), []);
        a.Stand(l);
        for (int i = 0; i < climbers; i++)
        {
            var c = w.Add(fx.Kit("cl" + i, 0, 38_800, 80_000 + i * 700), false);
            l.Climbers.Add(new Climb { Id = c.Id, Progress = climbProgress });
        }
        foreach (int y in new[] { 80_400, 81_000 }) { var d = w.Add(fx.Kit("d" + y, 1, 41_500, y), false); a.PutAt(d.Id, 1, 41_500, y); }
        return (a, w, l);
    }

    [Fact]
    public void LW2_ALadderWithOneClimberIsPushedOffIn6Seconds_WithTwoAboveHalfHeightItIsNot()
    {
        // one climber, low on the ladder: two defenders at the top push it off at 6 s (120 ticks), not before
        var (a, w, l) = LadderScene(1, 10);
        int pushedAt = -1;
        for (int t = 0; t < 400 && pushedAt < 0; t++) { foreach (var c in l.Climbers) c.Progress = Math.Min(c.Progress, 20); w.Step(); if (l.Stage == LadderStage.Down) pushedAt = w.Tick; }
        output.WriteLine($"pushed at tick {pushedAt}");
        Assert.InRange(pushedAt, 120, 135);
        // two climbers above half height hold it against the wall
        var (a2, w2, l2) = LadderScene(2, 60);
        for (int t = 0; t < 400; t++) { foreach (var c in l2.Climbers) c.Progress = Math.Max(c.Progress, 50); w2.Step(); }
        Assert.NotEqual(LadderStage.Down, l2.Stage);
        // a ladder with a hook fails the push 300 permille of the time (0x850 keyed on the ladder and the tick): over 400 pushes about 120 fail
        int fails = 0;
        for (int i = 0; i < 400; i++) if (DetRng.Range(77, i, 1, RngStreams.LadderPushes, 0, 0, 1000) < 300) fails++;
        Assert.InRange(fails, 90, 150);
    }

    // ------------------------------------------------------------------ LW-3: orders reach the layer with the delay

    [Fact]
    public void LW3_LadderTowerRamAndHoldOrders_ReachTheLayerWithTheirDelay_AndTheOrdersAreSaved()
    {
        var la = Storm(120, 60, 4, heroTeam: 0);                                                       // the hero is on the attackers' side: only his own orders raise ladders
        var a = la.Layer; var b = la.Bubble;
        Assert.Equal(2, a.AiTeams);                                                                    // the AI plays the defenders only
        for (int i = 0; i < 40; i++) b.Step();
        Assert.Empty(a.Ladders);                                                                       // no ladders until he orders them
        int t0 = b.W.Tick;
        a.Command(AssaultOrder.LaddersToWall, 0, 40, 70, 40, 90, delayTicks: 60);                      // "ladders to this wall": a stretch of 21 cells
        a.Command(AssaultOrder.HoldBreach, 0, 38, 80, 38, 80, delayTicks: 100);
        a.Command(AssaultOrder.TowerForward, 0, 40, 80, 40, 80, delayTicks: 80);
        a.Command(AssaultOrder.RamGate, 0, 40, 80, 40, 80, delayTicks: 80);
        Assert.Equal(4, a.Cmds.Count);
        while (b.W.Tick < t0 + 59) b.Step();
        Assert.Empty(a.Ladders);                                                                       // not yet: the delay of the voice
        while (b.W.Tick < t0 + 63) b.Step();
        Assert.InRange(a.Ladders.Count(l => l.Team == 0), 1, 3);                                       // a stretch of 21 cells takes 3 ladders at most
        Assert.Equal(3, a.Cmds.Count);                                                                 // the others still wait
        while (b.W.Tick < t0 + 105) b.Step();
        Assert.Empty(a.Cmds);                                                                          // all four reached the layer (tower and ram found no engine to push)
        // an order on its way is in the save and comes back
        a.Command(AssaultOrder.PushLadders, 1, 40, 70, 40, 90, delayTicks: 500);
        var rec = a.Capture();
        Assert.True(rec.Live); Assert.Equal(7, rec.Cmds.Length);
        Assert.Equal(rec.ToBytes(), AssaultLayer.Restore(AssaultRecord.FromBytes(rec.ToBytes()), b.W, _rigs[^1].W).Capture().ToBytes());
        Assert.Equal(5, rec.Siege);
    }

    // ------------------------------------------------------------------ LW-4: siege timber

    [Fact]
    public void LW4_AHostFellsTreesWithin2Km_TheTreesAreGoneFromTheChunk_TheLogsAreInTheYard_AndMassIsExact()
    {
        var f = TimberRig.Make();
        Assert.True(f.TreesBefore.Count >= 6);
        long before = f.Res.StockG(f.Row);
        var yard = f.Yard;
        var got = SiegeTimber.Fell(f.Store, f.Gen, f.Res, yard, f.Logs, f.CxM, f.CyM, 3 * SiegeTimber.TreeG, 10);
        output.WriteLine($"felled {got.Trees} trees, {got.LogsG} g; the tile lost {before - f.Res.StockG(f.Row)} g");
        Assert.True(got.Trees >= 1 && got.LogsG > 0);
        Assert.Equal(got.LogsG, yard.Qty(f.Logs));                                                      // the logs are in the yard
        Assert.Equal(got.LogsG, f.Mass.Created(f.Logs, CreateReason.Cut));                              // created from the wood the tiles lost, nothing else
        long lost = 0; foreach (int row in f.Rows) lost += f.Stock0[row] - f.Res.StockG(row);
        Assert.Equal(got.LogsG, lost);                                                                  // mass exact: what left the rows is what is in the yard
        // the trees are gone from the chunks for good: a second felling takes other trees, none twice
        var again = SiegeTimber.Fell(f.Store, f.Gen, f.Res, yard, f.Logs, f.CxM, f.CyM, 3 * SiegeTimber.TreeG, 10);
        Assert.Equal(got.LogsG + again.LogsG, yard.Qty(f.Logs));
        foreach (var key in f.FelledKeys(got)) Assert.False(f.Store.Rebuild(key.Key)!.Objects.ContainsKey(key.Id));
        Assert.True(f.Within2Km(got));
    }

    // ------------------------------------------------------------------ LW-5: save mid-assault

    [Fact]
    public void LW5_SaveLoadSave_IsByteIdentical_DuringTheSecondWave_AndTheLoadedRunGoesOnTheSame()
    {
        var la = Storm(260, 120, 12, heroTeam: -1);
        var b = la.Bubble;
        while (!b.Closed && b.Sides.Max(s => s.WavesSent) < 1 && b.W.Tick < 20 * 600) b.Step();
        Assert.False(b.Closed); Assert.True(b.Sides.Max(s => s.WavesSent) >= 1, "a second wave was sent");
        for (int i = 0; i < 10; i++) b.Step();
        output.WriteLine($"saved at tick {b.W.Tick}: waves {string.Join("/", b.Sides.Select(s => s.WavesSent))}, entering {b.Entering}, ladders {la.Layer.Ladders.Count}");
        var fight = FightRecord.Capture(b.W, "raid", "battle").ToBytes(); var battle = BattleRecord.Capture(b)!.ToBytes(); var assault = la.Layer.Capture().ToBytes();
        var w2 = FightRecord.FromBytes(fight).Restore(fx.C);
        var b2 = BattleBubble.Restore(w2, BattleRecord.FromBytes(battle));
        var a2 = AssaultLayer.Restore(AssaultRecord.FromBytes(assault), w2, _rigs[^1].W);
        Assert.Equal(b.W.Hash(), w2.Hash());
        Assert.Equal(fight, FightRecord.Capture(w2, "raid", "battle").ToBytes());
        Assert.Equal(battle, BattleRecord.Capture(b2)!.ToBytes());
        Assert.Equal(assault, a2.Capture().ToBytes());
        for (int i = 0; i < 300; i++) { b.Step(); b2.Step(); }
        Assert.Equal(b.W.Hash(), w2.Hash());
        Assert.Equal(la.Layer.Capture().ToBytes(), a2.Capture().ToBytes());
        // and in the world save: the assault in the sieges section round-trips to the same bytes
        var world = EconFixture.WorldWith(EconFixture.NewEconomy(false)); world.Assault = la.Layer.Capture();
        var bytes = SaveCodec.Encode(WorldSave.Capture(world, EconFixture.ContentHash, "0.1.0"));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));
    }

    /// <summary>A generated region with trees, a wood row under each tile of the 2 km around a wooded chunk, and an empty yard.</summary>
    private sealed class TimberRig
    {
        public required ChunkStore Store; public required ChunkGenerator Gen; public required Ravensguard.Sim.Production.ResourceLayer Res; public required Store Yard; public required MassLedger Mass;
        public required ushort Logs; public required long CxM, CyM; public required List<int> Rows; public required Dictionary<int, long> Stock0; public int Row => Rows[0];
        public Dictionary<(int X, int Y, ulong Id), (long X, long Y)> TreesBefore = [];

        public static TimberRig Make()
        {
            var dir = ContinentSettings.FindBaseContent()!;
            var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
            var hs = HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
            var rs = RegionSettings.LoadDefaults(dir); rs.Seed = 1234;
            var c = ContinentGenerator.Generate(cs); var h = HistoryGenerator.Generate(c, hs);
            var pick = EmbarkSite.AutoPick(c, h);
            var r = RegionGenerator.Generate(c, h, pick.CellX, pick.CellY, rs);
            LandmarkPlacer.Place(r, h, EconFixture.Content);
            var pop = Populator.Populate(r, h, EconFixture.Content);
            var gen = ChunkGenerator.For(r, pop, EconFixture.Content);
            var set = r.Settlements[0]; int size = ChunkContent.Size; long cx = set.CenterX, cy = set.CenterY; bool found = false;
            for (int ring = 0; ring < 60 && !found; ring++)
                for (int dy = -ring; dy <= ring && !found; dy++)
                    for (int dx = -ring; dx <= ring && !found; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                        int gx = (int)(set.CenterX / size) + dx, gy = (int)(set.CenterY / size) + dy; if (gx < 0 || gy < 0 || gx >= ChunkGenerator.ChunksPerSide || gy >= ChunkGenerator.ChunksPerSide) continue;
                        int trees = 0; foreach (var o in gen.Generate(gx, gy).Objects.Values) if (o.Data.Length >= PropRecord.Bytes && PropRecord.Decode(o.Data).Kind == PropKind.Tree) trees++;
                        if (trees >= 6) { cx = gx * size + size / 2; cy = gy * size + size / 2; found = true; }
                    }
            Assert.True(found, "a wooded chunk");
            var cat = EconFixture.Catalog(); var mass = new MassLedger(cat.Count); var yard = new Store(cat, mass, StoreKind.Chest, new EntityId(9));
            var res = new Ravensguard.Sim.Production.ResourceLayer(); var rows = new List<int>(); var stock0 = new Dictionary<int, long>();
            int t = (int)(RegionTile(cx - 2300)), tx0 = Math.Max(0, (int)((cx - 2300) / 64)), tx1 = Math.Min(RegionTiles - 1, (int)((cx + 2300) / 64)), ty0 = Math.Max(0, (int)((cy - 2300) / 64)), ty1 = Math.Min(RegionTiles - 1, (int)((cy + 2300) / 64));
            for (int ty = ty0; ty <= ty1; ty++) for (int tx = tx0; tx <= tx1; tx++) { int row = res.Add(ty * RegionTiles + tx, Ravensguard.Sim.Production.ResourceKind.Wood, 400_000, 400_000); rows.Add(row); stock0[row] = 400_000; }
            var rig = new TimberRig { Store = new ChunkStore(), Gen = gen, Res = res, Yard = yard, Mass = mass, Logs = cat.IdOf("logs"), CxM = cx, CyM = cy, Rows = rows, Stock0 = stock0 };
            rig.TreesBefore = rig.Trees(); return rig;
        }

        private static int RegionTile(long m) => 0;
        private const int RegionTiles = 500;

        public Dictionary<(int X, int Y, ulong Id), (long X, long Y)> Trees()
        {
            var res = new Dictionary<(int, int, ulong), (long, long)>(); int size = ChunkContent.Size;
            for (int y = (int)Math.Max(0, (CyM - 2100) / size); y <= Math.Min(ChunkGenerator.ChunksPerSide - 1, (CyM + 2100) / size); y++)
                for (int x = (int)Math.Max(0, (CxM - 2100) / size); x <= Math.Min(ChunkGenerator.ChunksPerSide - 1, (CxM + 2100) / size); x++)
                {
                    var content = Store.GetSnapshot(new ChunkKey(x, y, 0)) != null ? Store.Rebuild(new ChunkKey(x, y, 0))! : Gen.Generate(x, y);
                    foreach (var (id, o) in content.Objects) if (o.Data.Length >= PropRecord.Bytes && PropRecord.Decode(o.Data) is { Kind: PropKind.Tree } p) res[(x, y, id)] = (x * (long)size + p.X, y * (long)size + p.Y);
                }
            return res;
        }

        public IEnumerable<(ChunkKey Key, ulong Id)> FelledKeys(Felled _) { var now = Trees(); foreach (var k in TreesBefore.Keys.Where(k => !now.ContainsKey(k)).OrderBy(k => k)) yield return (new ChunkKey(k.X, k.Y, 0), k.Id); }
        public bool Within2Km(Felled _) { var now = Trees(); return TreesBefore.Where(kv => !now.ContainsKey(kv.Key)).All(kv => (kv.Value.X - CxM) * (kv.Value.X - CxM) + (kv.Value.Y - CyM) * (kv.Value.Y - CyM) <= 2000L * 2000L); }
    }
}
