using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Burning;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Siege;
using Ravensguard.Sim.Tests.Building;
using Ravensguard.Sim.Tests.Construction;
using Ravensguard.Sim.Tests.Fire;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.World;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Siege;

/// <summary>A curtain wall in the chunk store as generation would leave it: a 3 m thick stone wall along x = 36..55 at y = 40..42, a parapet at +1 on its outer face and a 2 m walk behind it, ground around it, the chunk visited.</summary>
internal sealed class WallRig
{
    public required ContentDatabase Db { get; init; }
    public required ChunkStore Store { get; init; }
    public required PiecePalette Pal { get; init; }
    public required WallWorld W { get; init; }
    public List<(string Type, string Key, IReadOnlyList<ChronParam> Ps, IReadOnlyList<Cause> Causes)> Chron { get; } = [];
    public List<(EntityId P, int Mm)> Falls { get; } = [];
    public List<(EntityId P, int X, int Y, int Z)> Standing { get; } = [];
    public const long Sec = 10L * 86400 + 6 * 3600;

    public static WallRig Make(ContentDatabase db, ulong seed = 1234, ChunkStore? store = null, bool stamp = true)
    {
        var pal = new PiecePalette(PieceCosts.Of(db).Library.Pieces);
        store ??= new ChunkStore();
        if (stamp) Stamp(store, pal);
        var w = new WallWorld(store, db, seed);
        var rig = new WallRig { Db = db, Store = store, Pal = pal, W = w };
        w.PeopleOn = cells => rig.Standing.Where(s => cells.Contains(ConstructionIds.CellKey(s.X, s.Y, s.Z))).ToList();
        w.Fall = (p, mm, ev, sec) => rig.Falls.Add((p, mm));
        w.Chronicle = (when, type, key, ps, subjects, causes, data) => { rig.Chron.Add((type, key, ps, causes)); return new EntityId(900_000UL + (ulong)rig.Chron.Count); };
        return rig;
    }

    /// <summary>Another world over the same (reloaded) store: what a load builds.</summary>
    public WallRig Reload()
    {
        var back = ChunkStore.FromSection(Store.ToSection());
        return Make(Db, W.Seed, back, stamp: false);
    }

    public static void Stamp(ChunkStore store, PiecePalette pal)
    {
        var ground = new ChunkContent(); var upper = new ChunkContent();
        for (int i = 0; i < ChunkContent.Size * ChunkContent.Size; i++) ground.Tiles[i] = TileCode.Make(Ravensguard.Sim.Live.GroundKind.Dirt, 0, 0, 0);
        int Idx(int x, int y) => ChunkContent.TileIndex(x - 32, y - 32);
        for (int x = 36; x <= 55; x++)
            for (int y = 40; y <= 42; y++)
            {
                ground.Tiles[Idx(x, y)] = TileCode.Make(Ravensguard.Sim.Live.GroundKind.Dirt, 0, pal.Of("wall_stone"), 0);
                upper.Tiles[Idx(x, y)] = TileCode.Make(Ravensguard.Sim.Live.GroundKind.None, 0, pal.Of(y == 40 ? "wall_crenellated" : "floor_wall_walk"), 0);
            }
        Assert.True(store.RecordFirstVisit(new ChunkKey(1, 1, 0), 1, "h0", ground));
        Assert.True(store.RecordFirstVisit(new ChunkKey(1, 1, 1), 1, "h1", upper));
    }

    public string? Piece(int x, int y, int z) => W.StructureAt(x, y, z);

    /// <summary>Strikes the cell with stones of 4,500 kg m/s until it is down; returns the blows it took.</summary>
    public int Batter(int x, int y, int z = 0, long momentum = 4500)
    {
        int n = 0;
        while (W.Strike(x, y, z, momentum, Sec + n).Kind is WallBlowKind.Damaged) n++;
        return n + 1;
    }

    public LevelGrid Grid(int x0, int y0, int w, int h)
    {
        var g = new LevelGrid(w, h, 0, 1);
        for (int z = 0; z <= 1; z++) for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) g.SetOpen(x, y, z, W.IsOpen(x0 + x, y0 + y, z));
        return g;
    }
}

/// <summary>P7-SG-A: walls with hit points, breaches and what they undermine, saved in the chunk deltas (sieges-and-engines SG-1 to SG-4).</summary>
public class WallTests(BuildingFixture fx, ITestOutputHelper output) : IClassFixture<BuildingFixture>
{
    private ContentDatabase Db => fx.Load.Database;
    private WallRig Rig(ulong seed = 1234) => WallRig.Make(Db, seed);

    // ------------------------------------------------------------------ SG-1

    [Fact]
    public void SG1_TheDamageRule_AStoneOnStone50_ARamOnAGate26_ARamOnStoneNothing()
    {
        var t = WallTable.Of(Db); var costs = PieceCosts.Of(Db);
        int Dmg(long m, string piece) => WallDamage.Impact(m, t.Stat(piece)!.Threshold, t.Rules.DamageDivisor);
        Assert.Equal(50, Dmg(4500, "wall_stone"));                      // a trebuchet stone: 100 kg x 45 m/s
        Assert.Equal(26, Dmg(1800, "gate_wood"));                       // a covered ram with an iron head
        Assert.Equal(0, Dmg(1500, "wall_stone"));                       // a plain ram does nothing to stone
        Assert.Equal(5, Dmg(1800, "wall_stone"));                       // the iron-headed ram a little
        Assert.Equal(0, WallDamage.Impact(1500, 1500)); Assert.Equal(0, WallDamage.Impact(10, 1500)); Assert.Equal(1, WallDamage.Impact(1560, 1500)); Assert.Equal(0, WallDamage.Impact(1559, 1500));
        Assert.Equal(WallDamage.Impact(4500, 1500), WallDamage.ImpactMilli(4_500_999, 1500));
        // the table is the spec's (3.1): hit points from the pieces, thresholds from walls.jsonc
        foreach (var (id, hp, thr) in new[] { ("wall_stone", 2000, 1500), ("wall_crenellated", 1500, 1200), ("wall_brick", 1500, 1000), ("wall_timber", 600, 300), ("palisade", 500, 250), ("gate_wood", 300, 200), ("gate_iron", 600, 400), ("floor_wall_walk", 150, 300) })
        {
            Assert.Equal(hp, costs[id].Hp); Assert.Equal(thr, t.Stat(id)!.Threshold);
        }
        // a cell of stone falls in 40 stones; a wooden gate in 12 swings of the iron-headed ram; no blow at all below the threshold
        var r = Rig();
        Assert.Equal(2000, r.W.HpAt(45, 41, 0)!.Value.Hp);
        Assert.Equal(40, r.Batter(45, 41));
        Assert.Equal(WallBlowKind.NoEffect, r.W.Strike(46, 41, 0, 1500, WallRig.Sec).Kind);
        Assert.Equal(2000, r.W.HpAt(46, 41, 0)!.Value.Hp);
        Assert.Equal(WallBlowKind.NotAWall, r.W.Strike(10, 10, 0, 9999, WallRig.Sec).Kind);                     // a place never visited is left alone
        Assert.Equal(WallBlowKind.NotAWall, r.W.Strike(33, 33, 0, 9999, WallRig.Sec).Kind);                     // open ground is no wall
        int swings = 0; int gate = 300; while (gate > 0) { gate -= Dmg(1800, "gate_wood"); swings++; }
        Assert.Equal(12, swings);
    }

    // ------------------------------------------------------------------ SG-2

    [Fact]
    public void SG2_AColumnTornThrough_IsRubbleThroughTheThickness_TwoMetresIsABreach_AndTheWalkAboveFalls()
    {
        var r = Rig();
        var grid0 = r.Grid(36, 36, 20, 10);
        Assert.Null(new LevelPathfinder(grid0).FindPath(new Cell3(9, 1, 0), new Cell3(9, 8, 0)));              // the wall shuts the way
        var d1 = new EntityId(IdKindPerson(1)); var d2 = new EntityId(IdKindPerson(2)); var d3 = new EntityId(IdKindPerson(3));
        r.Standing.AddRange([(d1, 45, 41, 1), (d2, 46, 42, 1), (d3, 50, 41, 1)]);                              // two on the walk over the breach, one well away
        int blows = 0;
        foreach (int y in new[] { 40, 41, 42 }) blows += r.Batter(45, y);
        Assert.Equal(120, blows);                                                                                // 120 stones on one 3-cell column
        foreach (int y in new[] { 40, 41, 42 }) Assert.Equal("rubble_stone", r.Piece(45, y, 0));
        Assert.Empty(r.W.Breaches(30, 30, 60, 50));                                                              // one metre wide is a gap, not yet a breach
        Assert.NotNull(new LevelPathfinder(r.Grid(36, 36, 20, 10)).FindPath(new Cell3(9, 1, 0), new Cell3(9, 8, 0)));   // but feet cross it
        foreach (int y in new[] { 40, 41, 42 }) blows += r.Batter(46, y);
        Assert.Equal(240, blows);
        var b = Assert.Single(r.W.Breaches(30, 30, 60, 50));
        Assert.Equal((2, 3, 'x'), (b.WidthM, b.ThicknessM, b.Along));                                           // the wall runs along x, the breach is 3 deep
        Assert.True(b.Contains(45, 40) && b.Contains(46, 42));
        Assert.True(r.W.IsRubble(46, 41, 0));
        var path = new LevelPathfinder(r.Grid(36, 36, 20, 10)).FindPath(new Cell3(9, 1, 0), new Cell3(10, 8, 0));
        Assert.NotNull(path); Assert.Contains(path!.Cells, c => c.X is 9 or 10 && c.Y is 4 or 5 or 6);
        // the walk and parapet over the breach rest on the wall under them: they fell; the rest of the walk stands
        foreach (int x in new[] { 45, 46 }) foreach (int y in new[] { 40, 41, 42 }) Assert.Null(r.Piece(x, y, 1));
        foreach (int x in new[] { 44, 47, 36, 55 }) foreach (int y in new[] { 40, 41, 42 }) Assert.NotNull(r.Piece(x, y, 1));
        // the two on the walk dropped three metres, the third did not
        Assert.Equal([(d1, 3000), (d2, 3000)], r.Falls.OrderBy(f => f.P.Value).ToArray());
        // the chronicle: one breach, with its cause, and the fall
        Assert.Single(r.Chron, c => c.Type == "wall_breached");
        Assert.Equal(CauseKind.Siege, r.Chron.Single(c => c.Type == "wall_breached").Causes[0].Kind);
        Assert.Equal("ch_note_sg_blow", r.Chron.Single(c => c.Type == "wall_breached").Causes[0].NoteKey);
        Assert.Contains(r.Chron, c => c.Type == "wall_fell");
        output.WriteLine($"{blows} blows, {r.Chron.Count} chronicle entries");
    }

    private static ulong IdKindPerson(ulong n) => EntityId.Make(IdKind.Person, 7_000_000 + n).Value;

    [Fact]
    public void SG2_TheFall_IsARealWound_ThroughThePhase4FallRules()
    {
        var r = Rig();
        var ledger = new WoundLedger(1234, WallRig.Sec);
        var man = new EntityId(IdKindPerson(9)); var hits = new List<int>();
        r.W.Fall = (p, mm, ev, sec) => hits.Add(FallWounds.Apply(Db, ledger, p, mm, ev, sec, 1234));
        r.Standing.Add((man, 45, 41, 1));
        foreach (int x in new[] { 45, 46 }) foreach (int y in new[] { 40, 41, 42 }) r.Batter(x, y);
        Assert.Single(hits); Assert.True(hits[0] > 0, "the fall left no wound");
    }

    [Fact]
    public void SG2_AGateAndTheWallNextToIt_MakeABreach_AGateAloneIsAnOpenGate()
    {
        var r = Rig();
        // a wooden gate where the wall is: a 3 m deep passage, one metre wide
        var store = r.Store; var pal = r.Pal;
        var c = store.Rebuild(new ChunkKey(1, 1, 0))!; var ops = new List<ChunkOp>();
        foreach (int y in new[] { 40, 41, 42 }) ops.Add(ChunkOp.SetTile(40 - 32, y - 32, TileCode.Make(Ravensguard.Sim.Live.GroundKind.Dirt, 0, pal.Of("gate_wood"), 0)));
        store.AppendDelta(new ChunkKey(1, 1, 0), ops);
        foreach (int y in new[] { 40, 41, 42 }) Assert.Equal(WallBlowKind.Destroyed, r.W.Strike(40, y, 0, 30_000, WallRig.Sec).Kind);   // a blow hard enough to take a gate cell at once
        Assert.Equal("rubble_timber", r.Piece(40, 41, 0));
        Assert.Empty(r.W.Breaches(30, 30, 60, 50));
        r.Batter(41, 40); r.Batter(41, 41); r.Batter(41, 42);
        Assert.Single(r.W.Breaches(30, 30, 60, 50));
    }

    // ------------------------------------------------------------------ SG-3

    [Fact]
    public void SG3_ADamagedAndBreachedWall_IsInTheChunkDeltas_ReloadShowsTheBreach_AndTheRoundTripIsExact()
    {
        var r = Rig();
        r.W.Strike(50, 41, 0, 4500, WallRig.Sec); r.W.Strike(50, 41, 0, 4500, WallRig.Sec);                    // a damaged cell: 100 lost
        foreach (int x in new[] { 45, 46 }) foreach (int y in new[] { 40, 41, 42 }) r.Batter(x, y);
        var key0 = new ChunkKey(1, 1, 0);
        var oid = WallWorld.ObjectId(key0);
        var content = r.Store.Rebuild(key0)!;
        var rec = content.Objects[oid];                                                                          // the one siege_damage object of the chunk
        var hits = SaveCodec.Deserialize<WallHits>(rec.Props[WallWorld.Prop]);
        Assert.Equal(7, hits.Cells.Count);
        Assert.Equal(100, hits.Cells.Single(h => !h.Down).Lost);
        Assert.Equal(6, hits.Cells.Count(h => h.Down && h.Piece == "wall_stone"));
        // the delta (not the snapshot) carries it: AddObject, SetProperty, and the rubble tiles
        Assert.Single(DeltaOps(r.Store, key0, ChunkOpKind.AddObject));
        Assert.True(DeltaOps(r.Store, key0, ChunkOpKind.SetProperty).Count > 0);
        Assert.Equal(6, DeltaOps(r.Store, key0, ChunkOpKind.SetTile).Count);
        Assert.Equal(oid, DeltaOps(r.Store, key0, ChunkOpKind.AddObject)[0].ObjectId);
        // the snapshot is what generation made: untouched
        Assert.Equal(pal("wall_stone"), TileCode.Structure(r.Store.GetSnapshot(key0)!.Decode().Tiles[ChunkContent.TileIndex(45 - 32, 41 - 32)]));
        // reload: the same breach, the same hit points, the same bytes
        var back = r.Reload();
        var b = Assert.Single(back.W.Breaches(30, 30, 60, 50));
        Assert.Equal(2, b.WidthM);
        Assert.Equal(1900, back.W.HpAt(50, 41, 0)!.Value.Hp);
        Assert.Equal(r.Store.ToSection(), back.Store.ToSection());
        Assert.Equal(r.Store.ToSection(), ChunkStore.FromSection(back.Store.ToSection()).ToSection());           // save, load, save
        // save -> load -> continue equals the uninterrupted run
        var a = Rig(); var c = Rig();
        void First(WallRig q) { q.W.Strike(50, 41, 0, 4500, WallRig.Sec); foreach (int y in new[] { 40, 41, 42 }) q.Batter(45, y); }
        void Rest(WallRig q) { foreach (int y in new[] { 40, 41, 42 }) q.Batter(46, y); q.W.Strike(50, 41, 0, 4500, WallRig.Sec + 5); }
        First(a); Rest(a);
        First(c); var c2 = c.Reload(); Rest(c2);
        Assert.Equal(a.Store.ToSection(), c2.Store.ToSection());
        // an older store with no siege object is unchanged by loading and saving
        var plain = WallRig.Make(Db); var before = plain.Store.ToSection();
        Assert.Equal(before, ChunkStore.FromSection(before).ToSection());
        Assert.Null(plain.W.HitsOf(key0));
        int pal(string id) => r.Pal.Of(id);
    }

    private static List<ChunkOp> DeltaOps(ChunkStore s, ChunkKey k, ChunkOpKind kind)
    {
        var e = SaveCodec.DeserializeCompressed<ChunkStoreData>(s.ToSection()).Chunks.Single(c => c.Key == k);
        return e.Delta.Ops.Where(o => o.Kind == kind).ToList();
    }

    [Fact]
    public void SG3_ABuiltWall_KeepsItsHitPointsInTheBuiltCell_AndFallsByThePhase5Path()
    {
        var r = ConstructionRig.Make(); long t = ConstructionRig.Day10;
        var db = EconFixture.Content;
        var pal = new PiecePalette(PieceCosts.Of(db).Library.Pieces);
        var store = new ChunkStore();
        var ground = new ChunkContent(); for (int i = 0; i < 1024; i++) ground.Tiles[i] = TileCode.Make(Ravensguard.Sim.Live.GroundKind.Dirt, 0, 0, 0);
        Assert.True(store.RecordFirstVisit(new ChunkKey(18, 18, 0), 1, "h", ground));           // x, y 576..607
        r.W.Tiles = new ChunkTileSink(store, pal);
        var pr = r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(600, 600, 0, "wall_stone")], t);
        Assert.True(pr.Ok && pr.Refused.Count == 0);
        r.StockAll(pr.Site); r.Crew(pr.Site, 8);
        for (int d = 0; d < 30 && r.W.GhostsOf(pr.Site).Any(); d++) { t += 86400; r.W.AdvanceTo(t); }
        var cell = r.W.BuiltAt(600, 600, 0)!;
        int max = PieceCosts.BuiltHp(r.W.Costs["wall_stone"], cell.BuiltSkill);
        Assert.Equal(cell.Hp, max);
        var w = new WallWorld(store, db, 1234) { Construction = r.W };
        Assert.Equal(WallBlowKind.Damaged, w.Strike(600, 600, 0, 4500, t).Kind);
        Assert.Equal(max - 50, r.W.BuiltAt(600, 600, 0)!.Hp);                                    // the hit points are the built cell's
        Assert.Equal((("wall_stone"), max - 50, max), w.HpAt(600, 600, 0)!.Value);
        // saved with the construction book: a round trip keeps the hit points
        var back = ConstructionSave.Capture(r.W.Book).Restore();
        Assert.Equal(max - 50, back.Built.Values.Single(b => b.Piece == "wall_stone").Hp);
        int n = 1; while (w.Strike(600, 600, 0, 4500, t + n).Kind == WallBlowKind.Damaged) n++;
        Assert.Null(r.W.BuiltAt(600, 600, 0));                                                    // gone by the Phase 5 path
        Assert.Equal("rubble_stone", w.StructureAt(600, 600, 0));                                  // and rubble in the street's tiles
        Assert.True(w.IsRubble(600, 600, 0));
        Assert.True(w.RepairNeedAt(600, 600, 0)!.Down);
    }

    // ------------------------------------------------------------------ SG-4

    [Fact]
    public void SG4_Repair_NeedsThePiecesMaterialsAndHours_RestoresTheCellAndTheDelta()
    {
        var r = Rig();
        var costs = PieceCosts.Of(Db);
        foreach (int x in new[] { 45, 46 }) foreach (int y in new[] { 40, 41, 42 }) r.Batter(x, y);
        r.W.Strike(50, 41, 0, 4500, WallRig.Sec);                                                                // a cell with 50 lost
        Assert.NotEmpty(r.W.Breaches(30, 30, 60, 50));
        var need = r.W.RepairNeedAt(45, 41, 0)!;
        Assert.Equal(("wall_stone", 1000, true), (need.Piece, need.SharePm, need.Down));
        Assert.Equal(costs["wall_stone"].MilliHours, need.MilliHours);                                            // 16 worker-hours a cell
        Assert.Equal(costs["wall_stone"].Materials, need.Materials.ToArray());                                    // stone, quicklime, sand
        Assert.True(need.Materials.Single(m => m.Good == "stone_raw").Grams >= 2_000_000);
        var part = r.W.RepairNeedAt(50, 41, 0)!;
        Assert.Equal((25, false), (part.SharePm, part.Down));                                                    // 50 of 2,000 hp: 25 permille of the piece
        Assert.Equal(costs["wall_stone"].MilliHours * 25 / 1000, part.MilliHours);
        // nothing is mended unless the caller pays
        Assert.Empty(r.W.Repair([(45, 41, 0)], WallRig.Sec + 100, _ => false));
        Assert.Equal("rubble_stone", r.Piece(45, 41, 0));
        // the masons: ground cells first, then the walk over them (a walk cell needs the wall under it)
        var paid = new List<RepairNeed>();
        var walk = new[] { (45, 41, 1), (45, 40, 1) };
        var cells = new[] { (45, 40, 0), (45, 41, 0), (45, 42, 0), (46, 40, 0), (46, 41, 0), (46, 42, 0), (45, 41, 1), (45, 40, 1), (45, 42, 1), (46, 40, 1), (46, 41, 1), (46, 42, 1), (50, 41, 0) };
        var done = r.W.Repair(cells.Reverse(), WallRig.Sec + 200, n => { paid.Add(n); return true; });
        Assert.Equal(13, done.Count); Assert.Equal(13, paid.Count);
        Assert.Equal("wall_stone", r.Piece(45, 41, 0)); Assert.Equal("floor_wall_walk", r.Piece(45, 41, 1)); Assert.Equal("wall_crenellated", r.Piece(45, 40, 1));
        Assert.Empty(r.W.Breaches(30, 30, 60, 50));
        Assert.Equal(2000, r.W.HpAt(50, 41, 0)!.Value.Hp);
        Assert.Null(r.W.HitsOf(new ChunkKey(1, 1, 0)));                                                          // the damage object is gone from the chunk
        Assert.Null(r.W.HitsOf(new ChunkKey(1, 1, 1)));
        Assert.Contains(r.Chron, c => c.Type == "wall_repaired");
        // the delta's cell is restored: the tiles read as generation made them (same structure at every wall cell)
        var fresh = Rig();
        foreach (int x in new[] { 45, 46, 50 }) foreach (int y in new[] { 40, 41, 42 }) foreach (int z in new[] { 0, 1 }) Assert.Equal(fresh.Piece(x, y, z), r.Piece(x, y, z));
        // a wall-walk cell is not set back over a hole
        var q = Rig(); foreach (int x in new[] { 45, 46 }) foreach (int y in new[] { 40, 41, 42 }) q.Batter(x, y);
        Assert.Empty(q.W.Repair([(45, 41, 1)], WallRig.Sec, _ => true));
        Assert.Null(q.Piece(45, 41, 1));
        _ = walk;
    }

    // ------------------------------------------------------------------ fire

    [Fact]
    public void Fire_ABurntGate_BecomesRubble_AndWhatItHeldFalls_WithTheFiresCause()
    {
        var pal = new PiecePalette(PieceCosts.Of(Db).Library.Pieces);
        var store = new ChunkStore();
        WallRig.Stamp(store, pal);
        // a wooden gate and a timber walk over it, in the middle of the stone wall
        var ground = new List<ChunkOp>(); var up = new List<ChunkOp>();
        foreach (int y in new[] { 40, 41, 42 }) { ground.Add(ChunkOp.SetTile(48 - 32, y - 32, TileCode.Make(Ravensguard.Sim.Live.GroundKind.Dirt, 0, pal.Of("gate_wood"), 0))); }
        store.AppendDelta(new ChunkKey(1, 1, 0), ground);
        var rig = FireRig.Make(fx, 5);
        var w = new WallWorld(store, Db, 1234); w.Hook(rig.Fw);
        var chron = new List<(string Type, CauseKind Kind, string Note)>();
        w.Chronicle = (when, type, key, ps, subjects, causes, data) => { chron.Add((type, causes[0].Kind, causes[0].NoteKey)); return new EntityId(777); };
        rig.Fw.Adopt = (x0, y0, x1, y1) => FireWiring.Scan(store, pal, x0, y0, x1, y1);
        var man = new EntityId(IdKindPerson(4)); var falls = new List<int>();
        w.PeopleOn = _ => [(man, 48, 41, 1)]; w.Fall = (p, mm, ev, sec) => falls.Add(mm);
        Assert.True(rig.Fw.Ignite(48, 41, 0, rig.Sec, FireCause.Test));
        rig.RunOut(2000);
        Assert.Equal("rubble_timber", w.StructureAt(48, 41, 0));                                       // the burnt gate cell is rubble in the chunk
        Assert.Null(rig.Rooms.PieceAt(48, 41, 0) is { Kind: not PieceKind.Ground } ? "x" : null);        // and a gap to the fire's room world
        Assert.Null(w.StructureAt(48, 41, 1));                                                          // the walk over it fell
        Assert.Equal([3000], falls);
        Assert.Contains(chron, c => c.Type == "wall_fell" && c.Kind == CauseKind.Fire);
        Assert.NotNull(w.RepairNeedAt(48, 41, 0));
    }

    // ------------------------------------------------------------------ the table and the plumbing

    [Fact]
    public void TheWallTable_NamesRealPieces_AndTheirRubbleIsWalkableGround()
    {
        var costs = PieceCosts.Of(Db); var t = WallTable.Of(Db);
        Assert.Equal(8, t.All.Count);
        foreach (var s in t.All)
        {
            Assert.NotNull(costs.Get(s.Id));
            if (s.Rubble.Length > 0) { Assert.Equal(PieceKind.Ground, costs[s.Rubble].Kind); Assert.True(t.IsRubble(s.Rubble)); Assert.False(FireFixture.Fuels(fx).ClassOf(s.Rubble).Burns); }
        }
        Assert.Equal((60, 2, 8), (t.Rules.DamageDivisor, t.Rules.BreachMinWidthM, t.Rules.MaxThicknessM));
    }

    [Fact]
    public void TheDamageObjectIds_AreStablePerChunkLevel_AndNeverMeetEachOther()
    {
        var ids = new HashSet<ulong>();
        for (int z = ChunkKey.MinZ; z <= ChunkKey.MaxZ; z++) for (int y = -3; y < 4; y++) for (int x = -3; x < 4; x++) Assert.True(ids.Add(WallWorld.ObjectId(new ChunkKey(x * 700, y * 700, z))));
        Assert.Equal(WallWorld.ObjectId(new ChunkKey(5, 6, 1)), WallWorld.ObjectId(new ChunkKey(5, 6, 1)));
        Assert.True(new EntityId(WallWorld.ObjectId(new ChunkKey(5, 6, 1))).IsReservedSerial);
    }

    [Fact]
    public void ABlowIsDeterministic_TheSameStrikesTwiceGiveTheSameChunkBytes()
    {
        var a = Rig(); var b = Rig();
        foreach (var r in new[] { a, b }) { foreach (int x in new[] { 45, 46 }) foreach (int y in new[] { 40, 41, 42 }) r.Batter(x, y); r.W.Strike(50, 41, 0, 4500, WallRig.Sec); }
        Assert.Equal(a.Store.ToSection(), b.Store.ToSection());
        Assert.Equal(a.Chron.Select(c => c.Type), b.Chron.Select(c => c.Type));
    }
}
