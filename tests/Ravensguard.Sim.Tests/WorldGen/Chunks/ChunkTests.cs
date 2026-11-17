using System.Diagnostics;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Chunks;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.WorldGen.ChunkGen;

/// <summary>One generated region with its population, shared by the chunk tests (building it takes seconds).</summary>
public sealed class ChunkWorldFixture
{
    public RegionData Region { get; }
    public Population Population { get; }
    public ContentDatabase Content { get; }
    public ChunkGenerator Generator { get; }

    public ChunkWorldFixture()
    {
        var dir = ContinentSettings.FindBaseContent()!;
        var load = new ModLoader(ContentTypeRegistry.Default).Load([dir]);
        Assert.True(load.Success, load.Report.ToString());
        Content = load.Database;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = 1;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = 1;
        var rs = RegionSettings.LoadDefaults(dir); rs.Seed = 1;
        var c = ContinentGenerator.Generate(cs);
        var h = HistoryGenerator.Generate(c, hs);
        var pick = EmbarkSite.AutoPick(c, h);
        Region = RegionGenerator.Generate(c, h, pick.CellX, pick.CellY, rs);
        Population = Populator.Populate(Region, h, Content);
        Generator = ChunkGenerator.For(Region, Population, Content);
    }
}

public class ChunkTests(ChunkWorldFixture fx, ITestOutputHelper output) : IClassFixture<ChunkWorldFixture>
{
    private RegionData R => fx.Region;
    private ChunkGenerator G => fx.Generator;

    private static (int X, int Y) ChunkOf(int wx, int wy) => (ChunkStreamer.FloorDiv(wx, 32), ChunkStreamer.FloorDiv(wy, 32));

    /// <summary>Tile code at a world cell, generating the chunk as needed (cached per call site).</summary>
    private sealed class Lookup(ChunkGenerator g)
    {
        private readonly Dictionary<(int, int), GeneratedChunk> _c = [];
        public GeneratedChunk Chunk(int cx, int cy) { if (!_c.TryGetValue((cx, cy), out var c)) _c[(cx, cy)] = c = g.GenerateLevels(cx, cy); return c; }
        public int Code(int wx, int wy, int z = 0)
        {
            var (cx, cy) = ChunkOf(wx, wy);
            return Chunk(cx, cy).Levels.TryGetValue(z, out var l) && l.Tiles.TryGetValue((wy - cy * 32) * 32 + (wx - cx * 32), out var t) ? t : 0;
        }
        public GroundKind Ground(int wx, int wy) => TileCode.Ground(Code(wx, wy));
    }

    private (RegionSettlement S, int Index) Town()
    {
        var t = RegionSpawn.LargestTown(R)!;
        return (t, R.Settlements.IndexOf(t));
    }

    [Fact]
    public void Same_chunk_same_content_regardless_of_order()
    {
        var t = Town().S;
        int cx = t.CenterX / 32, cy = t.CenterY / 32;
        var coords = new List<(int, int)>();
        for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) coords.Add((cx + dx, cy + dy));
        coords.Add((3, 3)); coords.Add((999, 999)); coords.Add((0, 500));
        var forward = coords.ToDictionary(c => c, c => G.GenerateLevels(c.Item1, c.Item2).ContentHash);
        // a second generator over the same inputs, visiting in the opposite order, one chunk at a time
        var other = new ChunkGenerator(R, fx.Population, fx.Content);
        coords.Reverse();
        foreach (var c in coords) Assert.Equal(forward[c], other.GenerateLevels(c.Item1, c.Item2).ContentHash);
        // and the same generator again
        foreach (var c in coords) Assert.Equal(forward[c], G.GenerateLevels(c.Item1, c.Item2).ContentHash);
    }

    [Fact]
    public void Static_entry_point_matches_the_instance()
    {
        var a = ChunkGenerator.Generate(R, fx.Population, fx.Content, 100, 100);
        var b = G.Generate(100, 100);
        Assert.Equal(a.Tiles.Count, b.Tiles.Count);
        Assert.Equal(1024, a.Tiles.Count);
    }

    [Fact]
    public void Roads_cross_chunk_seams_without_gaps()
    {
        var look = new Lookup(G);
        int checkedCells = 0, seams = 0;
        foreach (var e in R.RoadEdges.OrderByDescending(e => e.LengthM).Take(3))
        {
            int prevCx = int.MinValue, prevCy = 0;
            for (int i = 0; i + 1 < e.Points.Length; i++)
            {
                var a = e.Points[i]; var b = e.Points[i + 1];
                int len = (int)Math.Max(1, Math.Sqrt((double)(b.X - a.X) * (b.X - a.X) + (double)(b.Y - a.Y) * (b.Y - a.Y)));
                for (int k = 0; k < len; k += 2)
                {
                    int x = a.X + (int)((long)(b.X - a.X) * k / len), y = a.Y + (int)((long)(b.Y - a.Y) * k / len);
                    var (cx, cy) = ChunkOf(x, y);
                    if (cx != prevCx || cy != prevCy) { if (prevCx != int.MinValue) seams++; prevCx = cx; prevCy = cy; }
                    var g = look.Ground(x, y);
                    Assert.True(g is GroundKind.Dirt or GroundKind.Cobble or GroundKind.Ford or GroundKind.Bridge,
                        $"road {e.Id} at ({x},{y}) in chunk ({cx},{cy}) is {g}");
                    checkedCells++;
                }
            }
        }
        output.WriteLine($"{checkedCells} road cells checked across {seams} chunk seams");
        Assert.True(seams > 20);
    }

    [Fact]
    public void Rivers_cross_chunk_seams_without_gaps()
    {
        var look = new Lookup(G);
        int checkedCells = 0, seams = 0;
        foreach (var w in R.Waterways.Where(w => w.Kind == WaterKind.River).OrderByDescending(w => w.LengthM).Take(2))
        {
            int prevCx = int.MinValue, prevCy = 0;
            for (int i = 0; i + 1 < w.Points.Length; i++)
            {
                var a = w.Points[i]; var b = w.Points[i + 1];
                int len = (int)Math.Max(1, Math.Sqrt((double)(b.X - a.X) * (b.X - a.X) + (double)(b.Y - a.Y) * (b.Y - a.Y)));
                for (int k = 0; k < len; k += 3)
                {
                    int x = a.X + (int)((long)(b.X - a.X) * k / len), y = a.Y + (int)((long)(b.Y - a.Y) * k / len);
                    var (cx, cy) = ChunkOf(x, y);
                    if (cx != prevCx || cy != prevCy) { if (prevCx != int.MinValue) seams++; prevCx = cx; prevCy = cy; }
                    var g = look.Ground(x, y);
                    Assert.True(g is GroundKind.Water or GroundKind.DeepWater or GroundKind.Ford or GroundKind.Bridge || look.Code(x, y, 0) != 0 && TileCode.Structure(look.Code(x, y)) != 0,
                        $"river {w.Id} at ({x},{y}) in chunk ({cx},{cy}) is {g}");
                    checkedCells++;
                }
            }
        }
        output.WriteLine($"{checkedCells} river cells checked across {seams} chunk seams");
        Assert.True(seams > 10);
    }

    /// <summary>Every structure cell of a stamp, at every level, must be in the generated chunks it touches.</summary>
    private int CountMissing(Lookup look, ChunkGenerator.Stamp st)
    {
        int missing = 0;
        foreach (var (z, (sa, fa)) in st.Levels)
            for (int y = 0; y < st.H; y++)
                for (int x = 0; x < st.W; x++)
                {
                    int s = sa[y * st.W + x];
                    if (s == 0) continue;
                    int code = look.Code(st.X0 + x, st.Y0 + y, z);
                    if (TileCode.Structure(code) != s || TileCode.Fixture(code) != fa[y * st.W + x]) missing++;
                }
        return missing;
    }

    [Fact]
    public void A_building_across_a_seam_is_whole()
    {
        var look = new Lookup(G);
        int seamBuildings = 0;
        for (int s = 0; s < R.Settlements.Count && seamBuildings < 12; s++)
            for (int l = 0; l < R.Settlements[s].Plan.Lots.Count && seamBuildings < 12; l++)
            {
                var st = G.StampOf(s, l);
                if (st == null) continue;
                var (c0x, c0y) = ChunkOf(st.X0, st.Y0); var (c1x, c1y) = ChunkOf(st.X0 + st.W - 1, st.Y0 + st.H - 1);
                if (c0x == c1x && c0y == c1y) continue;
                seamBuildings++;
                Assert.Equal(0, CountMissing(look, st));
            }
        output.WriteLine($"{seamBuildings} buildings across seams checked");
        Assert.True(seamBuildings >= 3, "expected some buildings to straddle chunk seams");
    }

    [Fact]
    public void Every_lot_of_the_test_settlements_produces_its_building()
    {
        var look = new Lookup(G);
        var picks = new List<int> { Town().Index };
        picks.Add(R.Settlements.IndexOf(R.Villages.OrderByDescending(v => v.TargetPop).First()));
        picks.Add(R.Settlements.IndexOf(R.Farmsteads.First()));
        int lots = 0, unfit = 0, fallback = 0;
        foreach (int s in picks)
        {
            var plan = R.Settlements[s].Plan;
            for (int l = 0; l < plan.Lots.Count; l++)
            {
                var lot = plan.Lots[l];
                var st = G.StampOf(s, l);
                Assert.True(st != null, $"{R.Settlements[s].Name} lot {l} ({lot.Use}, '{lot.BlueprintId}') has no building");
                Assert.True(st!.Cells >= 20, $"{R.Settlements[s].Name} lot {l} building is tiny ({st.Cells} cells)");
                Assert.Equal(0, CountMissing(look, st));
                lots++; if (!st.Fits) unfit++; if (lot.BlueprintId.Length == 0) fallback++;
                // the building stands inside its plot (unless it had to overflow)
                if (st.Fits) Assert.True(st.X0 >= lot.Rect.X && st.Y0 >= lot.Rect.Y && st.X0 + st.W <= lot.Rect.Right && st.Y0 + st.H <= lot.Rect.Bottom);
            }
        }
        output.WriteLine($"{lots} lots checked, {unfit} overflow their plot, {fallback} used a stand-in blueprint");
        Assert.True(lots > 30);
    }

    [Fact]
    public void Ground_is_varied_and_fields_have_furrows()
    {
        var look = new Lookup(G);
        var v = RegionSpawn.VillageEdge(new ChunkStreamer(G), R)!.Value;
        var kinds = new HashSet<GroundKind>();
        for (int dy = -48; dy < 48; dy++) for (int dx = -48; dx < 48; dx++) kinds.Add(look.Ground(v.X + dx, v.Y + dy));
        output.WriteLine("kinds around the village edge: " + string.Join(", ", kinds));
        Assert.Contains(GroundKind.Furrow, kinds);
        Assert.True(kinds.Count >= 4);
    }

    [Fact]
    public void First_visit_is_snapshotted_and_a_changed_tile_survives_unload()
    {
        var t = Town().S;
        int cx = t.CenterX / 32, cy = t.CenterY / 32;
        var streamer = new ChunkStreamer(new ChunkGenerator(R, fx.Population, fx.Content));
        Assert.Equal(0, streamer.Store.Count);
        var c = streamer.LoadSync(cx, cy);
        Assert.Equal(1, streamer.GeneratedCount);
        Assert.True(streamer.Store.Contains(new ChunkKey(cx, cy, 0)));
        var snap = streamer.Store.GetSnapshot(new ChunkKey(cx, cy, 0))!;
        Assert.Equal(ChunkGenerator.Version, snap.GeneratorVersion);
        Assert.Equal(32, snap.ContentHash.Length);

        int wx = cx * 32 + 5, wy = cy * 32 + 9;
        int before = streamer.TileAt(wx, wy, 0);
        int changed = TileCode.Make(GroundKind.Cobble, 2, 0, 0);
        Assert.NotEqual(before, changed);
        streamer.SetTile(wx, wy, 0, changed);
        Assert.Equal(1, streamer.Store.DeltaOpCount(new ChunkKey(cx, cy, 0)));

        // fell a tree if the chunk has one
        var tree = c.Level(0).Props.FirstOrDefault(p => p.Value.Kind == PropKind.Tree);
        int propsBefore = c.Level(0).Props.Count;
        if (tree.Key != 0) Assert.True(streamer.RemoveProp(cx, cy, 0, tree.Key));

        Assert.True(streamer.Unload(cx, cy));
        Assert.False(streamer.IsLoaded(cx, cy));
        var again = streamer.LoadSync(cx, cy);
        Assert.Equal(1, streamer.GeneratedCount);   // not regenerated
        Assert.Equal(1, streamer.ReloadedCount);
        Assert.Equal(changed, again.TileAt(5, 9, 0));
        if (tree.Key != 0) Assert.Equal(propsBefore - 1, again.Level(0).Props.Count);

        // and through a save: the section round-trips with the delta
        var restored = ChunkStore.FromSection(streamer.Store.ToSection());
        var s2 = new ChunkStreamer(new ChunkGenerator(R, fx.Population, fx.Content), restored);
        Assert.Equal(changed, s2.LoadSync(cx, cy).TileAt(5, 9, 0));
        Assert.Equal(0, s2.GeneratedCount);
    }

    [Fact]
    public void Streamer_loads_around_the_hero_in_slices_and_unloads_far_chunks()
    {
        var streamer = new ChunkStreamer(new ChunkGenerator(R, fx.Population, fx.Content), settings: new ChunkStreamerSettings { LoadRadius = 2, KeepRadius = 3 });
        int n = streamer.Update(200, 200, budgetMs: 0);
        Assert.Equal(10, n);                      // the sync ring (9) and, with no budget, just one more
        int steps = 0;
        while (streamer.LoadedCount < 25 && steps++ < 100) streamer.Update(200, 200, budgetMs: 0);
        Assert.Equal(25, streamer.LoadedCount);
        Assert.True(steps >= 14, "the outer ring should arrive one chunk per call with a zero budget");
        streamer.Update(210, 200, budgetMs: 1000);
        Assert.False(streamer.IsLoaded(197, 200));
        Assert.True(streamer.IsLoaded(212, 200));
        Assert.True(streamer.LoadedCount <= 49);
    }

    [Fact]
    public void Hero_can_walk_from_the_street_into_a_house_and_upstairs()
    {
        var (town, ti) = Town();
        // a building in the town with an upper floor and a front door
        ChunkGenerator.Stamp? st = null; Lot? lot = null;
        for (int l = 0; l < town.Plan.Lots.Count && st == null; l++)
        {
            var cand = G.StampOf(ti, l);
            if (cand != null && cand.Levels.ContainsKey(1) && cand.FrontDoors.Count > 0) { st = cand; lot = town.Plan.Lots[l]; }
        }
        Assert.NotNull(st);
        var door = st!.FrontDoors[0];
        var (sx, sy) = (lot!.Facing & 3) switch { 0 => (0, -1), 1 => (1, 0), 2 => (0, 1), _ => (-1, 0) };
        // walk out from the door to the street: the lot's edge is the street's edge
        int sxw = door.X, syw = door.Y;
        for (int k = 0; k < 14; k++) { sxw += sx; syw += sy; if (!lot.Rect.Overlaps(new RectM(sxw, syw, 1, 1))) { sxw += sx * 2; syw += sy * 2; break; } }

        var streamer = new ChunkStreamer(G);
        Assert.True(streamer.IsWalkable(sxw, syw), $"street cell ({sxw},{syw}) should be open");
        var w = new LiveWorld(streamer, new Cell3(sxw, syw, 0), GameTime.FromCalendar(1, Season.Spring, 8, 12));
        // an upstairs floor cell
        var (sa, _) = st.Levels[1];
        Cell3? up = null;
        for (int i = 0; i < sa.Length && up == null; i++)
        {
            if (sa[i] == 0) continue;
            int wx = st.X0 + i % st.W, wy = st.Y0 + i / st.W;
            var lc = new Cell3(wx - w.OriginX, wy - w.OriginY, 1);
            if (w.Grid.InBounds(lc.X, lc.Y, 1) && w.Grid.IsOpen(lc.X, lc.Y, 1)) up = lc;
        }
        Assert.NotNull(up);
        Assert.True(w.CommandMoveTo(up!.Value), "a path from the street to the upper floor should exist");
        int ticks = 0;
        do { w.Step(); } while (w.Snapshot.Hero.PathRemaining > 0 && ticks++ < 4000);
        var h = w.Snapshot.Hero;
        output.WriteLine($"{st.Blueprint}: walked {ticks} ticks, hero z={h.Z} at ({h.CellX},{h.CellY}) wanted ({up.Value.X},{up.Value.Y})");
        Assert.Equal(1, h.Z);
    }

    [Fact]
    public void The_hero_walks_across_chunks_and_the_window_follows()
    {
        var streamer = new ChunkStreamer(G);
        var start = RegionSpawn.TownSquare(streamer, R)!.Value;
        var w = new LiveWorld(streamer, start, GameTime.FromCalendar(1, Season.Spring, 8, 12));
        int ox0 = w.OriginX;
        w.SetInput(new MoveInput(1, 0, Sprint: true));
        double worldX0 = w.OriginX + w.Snapshot.Hero.X.ToDouble();
        for (int i = 0; i < 700; i++) w.Step();
        double worldX1 = w.OriginX + w.Snapshot.Hero.X.ToDouble();
        output.WriteLine($"walked {worldX1 - worldX0:0.0} m east, {w.WindowSwaps} window swaps, {streamer.LoadedCount} chunks loaded, {streamer.GeneratedCount} generated");
        Assert.True(worldX1 - worldX0 > 20, "the hero should make headway (blocked only by scenery)");
        Assert.True(w.Window!.Contains(streamer.LoadedCount > 0 ? (int)(worldX1 / 32) : 0, (int)((w.OriginY + w.Snapshot.Hero.Y.ToDouble()) / 32)));
        Assert.True(streamer.LoadedCount <= 81);
    }

    [Fact]
    public void Generation_time_per_chunk()
    {
        var t = Town().S;
        int cx = t.CenterX / 32, cy = t.CenterY / 32;
        var g = new ChunkGenerator(R, fx.Population, fx.Content);
        for (int i = 0; i < 6; i++) g.GenerateLevels(cx + i, cy);   // warm the JIT and the stamp caches
        var coords = new List<(int, int)>();
        for (int dy = -6; dy <= 6; dy++) for (int dx = -6; dx <= 6; dx++) coords.Add((cx + dx, cy + dy));      // the town
        for (int i = 0; i < 100; i++) coords.Add((20 + i * 9 % 960, 30 + i * 37 % 940));                      // the countryside
        var sw = Stopwatch.StartNew(); double max = 0; var one = new Stopwatch();
        foreach (var (x, y) in coords) { one.Restart(); g.GenerateLevels(x, y); max = Math.Max(max, one.Elapsed.TotalMilliseconds); }
        double avg = sw.Elapsed.TotalMilliseconds / coords.Count;
        output.WriteLine($"{coords.Count} chunks: {avg:0.00} ms average, {max:0.0} ms worst");
#if DEBUG
        Assert.True(avg < TestBudget.Ms(30.0), $"{avg} ms per chunk (debug build; the 4 ms budget applies to Release)");
#else
        Assert.True(avg < TestBudget.Ms(8.0), $"{avg} ms per chunk (target 4 ms; the gate is loose because tests run in parallel)");
#endif
    }
}
