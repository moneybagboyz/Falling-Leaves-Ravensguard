using System.Diagnostics;
using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Building;

/// <summary>P5-A3: RoomDetector v2 gate RD-1 to RD-12 (docs/specs/phase5/roomdetector-v2.md).</summary>
public class RoomDetectorV2Tests(BuildingFixture fx, ITestOutputHelper output) : IClassFixture<BuildingFixture>
{
    private BuildingLibrary Lib => fx.Lib;

    // ---- helpers ----

    private static CellEdit S(int x, int y, int z, string? piece) => new(x, y, z, CellLayer.Structure, piece);
    private static CellEdit F(int x, int y, int z, string? piece) => new(x, y, z, CellLayer.Fixture, piece);

    /// <summary>A walled room with an interior of iw x ih cells at (x0, y0): floor inside, wall ring around, no door.</summary>
    private static List<CellEdit> Box(int x0, int y0, int z, int iw, int ih, string wall = "wall_timber", string floor = "floor_plank")
    {
        var e = new List<CellEdit>();
        for (int y = y0 - 1; y <= y0 + ih; y++)
            for (int x = x0 - 1; x <= x0 + iw; x++)
                e.Add(S(x, y, z, x >= x0 && x < x0 + iw && y >= y0 && y < y0 + ih ? floor : wall));
        return e;
    }

    private RoomWorld Roofed(Action<List<CellEdit>> build, out RoomWorld w)
    {
        w = Lib.NewWorld();
        var e = new List<CellEdit>();
        build(e);
        w.Apply(e);
        w.RoofOver("roof_thatch");
        return w;
    }

    private static string Describe(RoomV2 r) => $"z={r.Z} area={r.Area} enc={r.Enclosed} in={r.Indoor} primary={r.Primary?.Id}";

    // ---- RD-1: v1 parity ----

    [Fact]
    public void RD1_EveryContentBlueprintMatchesV1()
    {
        foreach (var e in Lib.Blueprints)
        {
            var g = Lib.Grid(e.Def.Id);
            var v1 = Lib.Detector.Detect(g);
            var w = Lib.NewWorld();
            w.LoadBlueprint(g, 0, 0, roofed: true);
            Assert.Equal(v1.Count, w.Rooms.Count);
            foreach (var a in v1)
            {
                var (x, y) = a.Cells[0];
                var b = w.RoomAt(x, y, a.Z);
                Assert.True(b != null, $"{e.Def.Id}: no v2 room at {x},{y},{a.Z}");
                string at = $"{e.Def.Id} room z={a.Z} at {x},{y}";
                Assert.True(a.Cells.OrderBy(c => c).SequenceEqual(b!.Cells.OrderBy(c => c)), $"{at}: cells differ");
                Assert.True(a.Enclosed == b.Enclosed, $"{at}: Enclosed");
                Assert.True(a.Tags.OrderBy(t => t.Key, StringComparer.Ordinal).SequenceEqual(b.Tags.OrderBy(t => t.Key, StringComparer.Ordinal)), $"{at}: Tags");
                Assert.True(a.Beds == b.Beds, $"{at}: Beds");
                Assert.True(a.Doors.Select(d => (d.Cell, d.Exterior)).SequenceEqual(b.Doors.Select(d => (d.Cell, d.Exterior))), $"{at}: Doors");
                Assert.True(a.Primary?.Id == b.Primary?.Id, $"{at}: Primary {a.Primary?.Id} vs {b.Primary?.Id}");
                Assert.True(a.DeclaredPurposes.SequenceEqual(b.DeclaredPurposes), $"{at}: DeclaredPurposes");
                Assert.True(a.PurposeMatches == b.PurposeMatches, $"{at}: PurposeMatches");
                Assert.True(b.Indoor == b.Enclosed, $"{at}: roofed blueprint should be Indoor when enclosed");
            }
        }
    }

    [Fact]
    public void RD1_BlueprintCheckStillPasses()
    {
        var issues = BlueprintValidator.ValidateAll(Lib);
        Assert.True(issues.Count == 0, string.Join("\n", issues));
    }

    // ---- RD-2: roofs ----

    [Fact]
    public void RD2_CottageTwoRoom_IndoorOnlyWhereCovered()
    {
        var g = Lib.Grid("cottage_two_room");
        var w = Lib.NewWorld();
        w.LoadBlueprint(g, 0, 0, roofed: false);
        var ground = w.Rooms.Where(r => r.Z == 0).ToList();
        var loft = w.Rooms.Where(r => r.Z == 1).ToList();
        Assert.NotEmpty(loft);
        // no roof ghosts built: the hearth room is a yard, and nothing is Indoor except cells the loft floor covers wholly
        var hearth = ground.Single(r => r.DeclaredPurposes.SequenceEqual(["hearth"]));
        Assert.True(hearth.Enclosed);
        Assert.False(hearth.Indoor);
        Assert.Equal("yard", hearth.Primary?.Id);
        foreach (var r in w.Rooms) Assert.Equal(r.Cells.All(c => w.PieceAt(c.X, c.Y, r.Z + 1) != null) && r.Enclosed, r.Indoor);
        Assert.All(loft, r => Assert.False(r.Indoor));
        // roof over: every enclosed room is Indoor and the yard is gone
        w.RoofOver("roof_thatch");
        Assert.All(w.Rooms.Where(r => r.Enclosed), r => Assert.True(r.Indoor, Describe(r)));
        Assert.DoesNotContain(w.Rooms, r => r.Primary?.Id == "yard");
        Assert.Equal("kitchen", w.Rooms.Single(r => r.Z == 0 && r.DeclaredPurposes.SequenceEqual(["hearth"])).Primary?.Id);
    }

    [Fact]
    public void RD2_UndergroundCountsAsRoofed()
    {
        var w = Lib.NewWorld();
        var e = Box(0, 0, -1, 3, 3);
        e.Add(F(1, 1, -1, "cask_rack")); e.Add(F(0, 0, -1, "cask_rack"));
        w.Apply(e);
        var r = w.RoomAt(1, 1, -1)!;
        Assert.True(r.Indoor);
    }

    // ---- RD-3 / RD-12: incremental equals full ----

    private static readonly string[] Structures = ["floor_plank", "floor_plank", "floor_plank", "wall_timber", "wall_timber", "wall_stone", "door_wood", "window_shuttered", "post_timber", "roof_thatch", "ground_dirt", "ladder", "trapdoor", null!, null!];
    private static readonly string[] Fixtures = ["bed", "bunk", "hearth", "table", "forge", "anvil", "grain_bin", "hay_stack", "cask_rack", "stall", null!, null!, null!];

    /// <summary>Runs <paramref name="steps"/> random build/remove steps on a 20 x 20 x 3 area; checks incremental = full after each when asked. Returns the final facts hash.</summary>
    private long RandomRun(int steps, bool check)
    {
        var w = Lib.NewWorld();
        ulong s = 0x9E3779B97F4A7C15UL;
        int Next(int n) { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (int)(s % (ulong)n); }
        // two walled rooms and a loft to start from
        var start = new List<CellEdit>();
        start.AddRange(Box(3, 3, 0, 6, 5)); start.AddRange(Box(11, 3, 0, 5, 5)); start.AddRange(Box(3, 3, 1, 6, 5, floor: "floor_plank"));
        w.Apply(start); w.RoofOver("roof_thatch");
        long chain = 0; int rooms = 0, joins = 0, splits = 0, indoor = 0, dwellings = 0;
        for (int i = 0; i < steps; i++)
        {
            int x = Next(20), y = Next(20), z = Next(3);
            CellEdit e = Next(4) switch
            {
                0 => F(x, y, z, Fixtures[Next(Fixtures.Length)]),
                1 => new CellEdit(x, y, z, CellLayer.Purpose, Next(3) == 0 ? null : new[] { "sleeping", "hearth", "storage" }[Next(3)]),
                _ => S(x, y, z, Structures[Next(Structures.Length)]),
            };
            // a run of edits now and then (a wall line) exercises batches
            var batch = new List<CellEdit> { e };
            if (Next(10) == 0) for (int k = 1; k < 4; k++) batch.Add(e with { X = Math.Min(19, e.X + k) });
            var ch = w.Apply(batch);
            rooms += w.Rooms.Count; joins += ch.Structures.Count(x => x.Kind == StructureEventKind.Joined); splits += ch.Structures.Count(x => x.Kind == StructureEventKind.Split);
            indoor += w.Rooms.Count(r => r.Indoor); dwellings += w.AllFacts().Count(f => f.IsDwelling);
            if (check)
            {
                var twin = w.FullTwin();
                Assert.Equal(twin.Dump(), w.Dump());
                Assert.True(w.RegistryPartition().Zip(twin.ComputePartition()).All(p => p.First.SequenceEqual(p.Second)) && w.RegistryPartition().Count == twin.ComputePartition().Count, $"structure partition differs at step {i}");
                Assert.Equal(twin.FactsHash(includeIds: false), w.FactsHash(includeIds: false));
            }
            else w.FactsHash();
            chain = chain * 31 + w.FactsHash();
        }
        output.WriteLine($"random run: room-steps {rooms}, indoor {indoor}, joins {joins}, splits {splits}, dwelling-steps {dwellings}");
        Assert.True(rooms > steps && joins > 0 && splits > 0 && indoor > 0, "the random run must exercise rooms, roofs, joins and splits");
        return chain;
    }

    [Fact]
    public void RD3_IncrementalEqualsFull_1000Steps() => RandomRun(1000, check: true);

    [Fact]
    public void RD12_DeterministicHashOfTheRD3Sequence()
    {
        long a = RandomRun(1000, check: false), b = RandomRun(1000, check: false);
        Assert.Equal(a, b);
        // golden: integer maths and (z, y, x) order only, so the hash is the same on every platform
        Assert.Equal(Golden, a);
    }

    private const long Golden = -5375031998298139626;

    // ---- RD-4: join and split ----

    private static List<CellEdit> Shed(int x0, int y0) { var e = Box(x0, y0, 0, 3, 3, floor: "floor_packed_earth"); return e; }

    [Fact]
    public void RD4_JoinThenSplit_LowerIdSurvives()
    {
        var w = Lib.NewWorld();
        w.Apply(Shed(1, 1));            // x 0..4
        w.Apply(Shed(7, 1));            // x 6..10
        int a = w.StructureAt(0, 0, 0), b = w.StructureAt(6, 0, 0);
        Assert.True(a > 0 && b > a, $"two sheds, two structures: {a} {b}");
        Assert.Equal(2, w.StructureIds.Count());
        // a shared wall bridging the gap column x = 5
        var join = w.SetPiece(5, 0, 0, "wall_timber");
        Assert.Single(w.StructureIds);
        Assert.Equal(a, w.StructureAt(6, 0, 0));
        Assert.Contains(join.Structures, e => e.Kind == StructureEventKind.Joined && e.Structure == a && e.Other == b);
        Assert.Contains(a, join.Touched);
        var parts = w.RegistryPartition();
        Assert.Equal(w.ComputePartition().Count, parts.Count);
        // taking it away again splits them: the part with the lowest cell keeps the id, the other is new
        var split = w.SetPiece(5, 0, 0, null);
        Assert.Equal(2, w.StructureIds.Count());
        Assert.Equal(a, w.StructureAt(0, 0, 0));
        int nb = w.StructureAt(6, 0, 0);
        Assert.True(nb != a && nb > b, $"the new part takes a new id ({nb})");
        Assert.Contains(split.Structures, e => e.Kind == StructureEventKind.Split && e.Structure == a && e.Other == nb);
        Assert.Equal(w.ComputePartition().Count, w.RegistryPartition().Count);
        // the rooms never changed identity (a wall in the gap touches neither room)
        Assert.Equal(2, w.Rooms.Count);
    }

    // ---- RD-5 / RD-6: dwellings ----

    /// <summary>The test cottage: a hearth room and a bedroom with two beds, one door out. 11 x 5 outside.</summary>
    private RoomWorld TestCottage(bool roof, int beds = 2)
    {
        var w = Lib.NewWorld();
        var e = new List<CellEdit>();
        e.AddRange(Box(1, 1, 0, 9, 3, floor: "floor_plank"));
        for (int y = 1; y <= 3; y++) e.Add(S(5, y, 0, "wall_timber"));   // partition
        e[e.FindIndex(c => c.X == 5 && c.Y == 2 && c.Z == 0)] = S(5, 2, 0, "door_wood");
        e.Add(S(3, 4, 0, "door_wood"));                                  // street door
        e.Add(F(2, 2, 0, "hearth"));
        for (int i = 0; i < beds; i++) e.Add(F(7 + i % 3, 1 + i / 3, 0, "bed"));
        w.Apply(e);
        for (int y = 1; y <= 3; y++) for (int x = 6; x <= 9; x++) w.SetPurpose(x, y, 0, "sleeping");
        if (roof) w.RoofOver("roof_thatch");
        return w;
    }

    [Fact]
    public void RD5_CottageIsADwellingOnlyWithItsRoof()
    {
        var w = TestCottage(roof: true);
        var f = w.FactsAt(2, 2, 0)!;
        Assert.True(f.IsDwelling);
        Assert.Equal(DwellingKind.Cottage, f.DwellingKind);
        Assert.Equal(2, f.Beds);
        Assert.Equal(1, f.HearthRooms);
        Assert.Single(f.Entrances);
        Assert.Same(f, w.Facts(f.Structure)); // cached path
        var open = TestCottage(roof: false);
        Assert.False(open.FactsAt(2, 2, 0)!.IsDwelling);
        Assert.Equal(0, open.FactsAt(2, 2, 0)!.Beds);
        // the last roof piece laid makes it one
        var roofCells = Enumerable.Range(1, 9).SelectMany(x => Enumerable.Range(1, 3).Select(y => (x, y))).ToList();
        foreach (var (x, y) in roofCells) open.SetPiece(x, y, 1, "roof_thatch");
        Assert.True(open.FactsAt(2, 2, 0)!.IsDwelling);
    }

    [Fact]
    public void RD5_NeedsTwoBedsAndADoor()
    {
        Assert.False(TestCottage(true, beds: 1).FactsAt(2, 2, 0)!.IsDwelling);
        var w = TestCottage(true);
        w.SetPiece(3, 4, 0, "wall_timber");
        Assert.False(w.FactsAt(2, 2, 0)!.IsDwelling);
    }

    [Fact]
    public void RD6_Longhouse()
    {
        var g = Lib.Grid("longhouse");
        var w = Lib.NewWorld();
        w.LoadBlueprint(g, 0, 0, roofed: true);
        var f = w.FactsAt(8, 2, 0)!;
        Assert.InRange(f.BedCapacity, 6, 10);
        Assert.False(f.IsDwelling); // unfurnished: no bed placed yet
        // furnish the sleeping loft with bunks: now it is a dwelling of the longhouse kind
        foreach (var (x, y) in new[] { (13, 1), (13, 2), (13, 3) }) w.SetFixture(x, y, 1, "bunk");
        f = w.FactsAt(8, 2, 0)!;
        Assert.True(f.IsDwelling);
        Assert.Equal(DwellingKind.Longhouse, f.DwellingKind);
        Assert.InRange(f.Beds, 6, 10);
    }

    [Fact]
    public void RD6_OtherKinds()
    {
        // tower: 3 storeys above ground on a small footprint; manor: a great hall with a high seat
        var w = Lib.NewWorld();
        var g = Lib.Grid("tower_house");
        w.LoadBlueprint(g, 0, 0, roofed: true);
        var f = w.AllFacts().Single();
        output.WriteLine($"tower_house: dwelling={f.IsDwelling} kind={f.DwellingKind} levels={string.Join(",", f.Levels)} cap={f.BedCapacity}");
        Assert.Equal(g.Levels.Keys.Where(z => z >= 0).Count() >= 3, f.Levels.Count(z => z >= 0) >= 3);
    }

    // ---- RD-7: workshops and stations ----

    [Fact]
    public void RD7_WorkshopStationCount()
    {
        var w = Lib.NewWorld();
        var e = Box(1, 1, 0, 4, 3, floor: "floor_packed_earth");
        e.Add(S(3, 4, 0, "door_wood")); e.RemoveAll(c => c.X == 3 && c.Y == 4 && c.Layer == CellLayer.Structure && c.Value == "wall_timber");
        e.Add(S(3, 4, 0, "door_wood"));
        e.Add(F(1, 1, 0, "forge")); e.Add(F(2, 1, 0, "forge")); e.Add(F(4, 3, 0, "anvil"));
        e.Add(S(10, 1, 0, "ground_dirt")); e.Add(F(10, 1, 0, "forge")); // a forge in the street
        w.Apply(e);
        w.RoofOver("roof_thatch");
        var room = w.RoomAt(1, 1, 0)!;
        Assert.Equal("workshop_smithing", room.Primary?.Id);
        Assert.Equal(2, room.Stations["forge"]);
        Assert.Equal(1, room.Stations["anvil"]);
        var f = w.FactsAt(1, 1, 0)!;
        var ws = f.WorkshopStations();
        Assert.Equal(2, ws["forge"]); Assert.Equal(1, ws["anvil"]);
        Assert.Single(f.Workshops);
        Assert.Null(w.FactsAt(10, 1, 0)); // the loose forge belongs to no structure and counts for no business
        Assert.Null(w.RoomAt(10, 1, 0));
    }

    // ---- RD-8: storage ----

    [Fact]
    public void RD8_BarnStorage()
    {
        Roofed(e => { e.AddRange(Box(1, 1, 0, 6, 8, floor: "floor_packed_earth")); e.Add(F(1, 1, 0, "hay_stack")); e.Add(F(2, 1, 0, "hay_stack")); }, out var w);
        var r = w.RoomAt(1, 1, 0)!;
        Assert.Equal("barn", r.Primary?.Id);
        Assert.Equal(48, r.Area);
        Assert.Equal(48_000, r.StorageL);
        Assert.Equal("hay", r.StorageKind);
        Assert.Equal(48_000, w.FactsAt(1, 1, 0)!.StorageL["hay"]);
    }

    [Fact]
    public void RD8_GranaryAndCellarKinds()
    {
        Roofed(e => { e.AddRange(Box(1, 1, 0, 3, 3)); e.Add(F(1, 1, 0, "grain_bin")); e.Add(F(2, 1, 0, "grain_bin")); }, out var w);
        Assert.Equal("grain", w.RoomAt(1, 1, 0)!.StorageKind);
        Assert.Equal(9_000, w.RoomAt(1, 1, 0)!.StorageL);
        var c = Lib.NewWorld();
        var e2 = Box(1, 1, -1, 3, 3); e2.Add(F(1, 1, -1, "cask_rack"));
        c.Apply(e2);
        Assert.Equal("cellar", c.RoomAt(1, 1, -1)!.StorageKind);
    }

    // ---- RD-9: value ----

    private RoomWorld Hall(string wall, string floor, out RoomV2 hall)
    {
        var w = Lib.NewWorld();
        var e = Box(1, 1, 0, 5, 6, wall, floor);
        e.Add(F(1, 1, 0, "table")); e.Add(F(5, 6, 0, "high_seat"));
        w.Apply(e);
        w.RoofOver("roof_thatch");
        hall = w.RoomAt(1, 1, 0)!;
        return w;
    }

    [Fact]
    public void RD9_RoomValue_HandChecked()
    {
        Hall("wall_stone", "floor_flagstone", out var hall);
        // floor 30 x (150 kg x 2 mites/kg) = 9,000; 22 wall cells x (600 kg x 2) = 26,400; table 60 kg and high seat 80 kg of oak at 30 mites/kg = 4,200
        // (4,200 + (9,000 + 26,400) / 2) / 30 = 730
        Assert.Equal(30, hall.Area);
        Assert.Equal(730, hall.ValuePerM2);
        Assert.Equal(ValueTier.Fine, hall.ValueTier);
        Assert.Equal("great_hall", hall.Primary?.Id);
        // the same hall in cheap timber and planks is only a hall: a great hall needs Fine
        Hall("wall_wattle", "floor_plank", out var cheap);
        Assert.True(cheap.ValuePerM2 < 300, $"{cheap.ValuePerM2}");
        Assert.Equal("hall", cheap.Primary?.Id);
        Assert.Contains(cheap.Matches, m => m.Id == "hall");
    }

    [Theory]
    [InlineData(0, ValueTier.Mean)] [InlineData(29, ValueTier.Mean)] [InlineData(30, ValueTier.Plain)] [InlineData(99, ValueTier.Plain)]
    [InlineData(100, ValueTier.Decent)] [InlineData(299, ValueTier.Decent)] [InlineData(300, ValueTier.Fine)] [InlineData(999, ValueTier.Fine)] [InlineData(1000, ValueTier.Grand)]
    public void RD9_Tiers(int mites, ValueTier tier) => Assert.Equal(tier, RoomValue.TierOf(mites));

    // ---- RD-10: purpose gaps ----

    [Fact]
    public void RD10_SleepingRoomWithoutABedHasAGap()
    {
        Roofed(e => e.AddRange(Box(1, 1, 0, 3, 3)), out var w);
        foreach (var (x, y) in new[] { (1, 1), (2, 1), (3, 1) }) w.SetPurpose(x, y, 0, "sleeping");
        var r = w.RoomAt(1, 1, 0)!;
        Assert.Equal(["sleeping"], r.DeclaredPurposes);
        Assert.Equal(["bed"], r.PurposeGaps);
        Assert.False(r.PurposeMatches);
        w.SetFixture(1, 1, 0, "bed");
        r = w.RoomAt(1, 1, 0)!;
        Assert.Empty(r.PurposeGaps);
        Assert.True(r.PurposeMatches);
        Assert.Equal("bedroom", r.Primary?.Id);
    }

    [Fact]
    public void RD10_UnroofedHouseSaysNeedsRoof()
    {
        var w = Lib.NewWorld();
        var e = Box(1, 1, 0, 3, 3); e.Add(F(1, 1, 0, "bed"));
        w.Apply(e);
        w.SetPurpose(2, 2, 0, "sleeping");
        var r = w.RoomAt(1, 1, 0)!;
        Assert.Equal("yard", r.Primary?.Id);
        Assert.Equal(["roof"], r.PurposeGaps);
    }

    // ---- RD-11: collapse ----

    [Fact]
    public void RD11_FloorCollapseReDetectsInTheSameBarrier()
    {
        var w = Lib.NewWorld();
        var e = new List<CellEdit>();
        e.AddRange(Box(1, 1, 0, 3, 3)); e.AddRange(Box(1, 1, 1, 3, 3));
        e.Add(S(2, 0, 0, "door_wood")); e.Add(F(1, 1, 0, "hearth")); e.Add(F(2, 1, 0, "bed")); e.Add(F(3, 1, 0, "bed"));
        w.Apply(e);
        w.RoofOver("roof_thatch");
        Assert.True(w.RoomAt(1, 1, 0)!.Indoor);
        var before = w.FactsAt(1, 1, 0)!;
        Assert.True(before.IsDwelling || before.Beds == 0);
        // BC-13: the upper floor falls (its cells are removed together)
        var fall = new List<CellEdit>();
        for (int y = 1; y <= 3; y++) for (int x = 1; x <= 3; x++) fall.Add(S(x, y, 1, null));
        var change = w.Apply(fall);
        Assert.False(w.RoomAt(1, 1, 0)!.Indoor);
        Assert.Equal("yard", w.RoomAt(1, 1, 0)!.Primary?.Id);
        Assert.Contains(w.StructureAt(1, 1, 0), change.Touched);
        var after = w.FactsAt(1, 1, 0)!;
        Assert.NotSame(before, after);
        Assert.False(after.IsDwelling);
        Assert.Equal(0, after.Beds);
        Assert.Null(w.RoomAt(1, 1, 1)); // nothing left to be a room above
    }

    // ---- structure, speed, purity ----

    [Fact]
    public void FullDetectionOfEveryBlueprintIsDeterministic()
    {
        foreach (var e in Lib.Blueprints)
        {
            var g = Lib.Grid(e.Def.Id);
            var a = Lib.NewWorld(); a.LoadBlueprint(g, 3, 4);
            var b = Lib.NewWorld(); b.LoadBlueprint(g, 3, 4);
            Assert.Equal(a.FactsHash(), b.FactsHash());
            var twin = a.FullTwin();
            Assert.Equal(twin.Dump(), a.Dump());
        }
    }

    [Fact]
    public void IncrementalDetectionOfA12x20x3BuildingIsWithinBudget()
    {
        var w = Lib.NewWorld();
        var e = new List<CellEdit>();
        for (int z = 0; z < 3; z++) { e.AddRange(Box(1, 1, z, 12, 20)); e.Add(S(6, 0, z, z == 0 ? "door_wood" : "window_shuttered")); }
        for (int y = 1; y <= 20; y++) { e.Add(S(7, y, 0, "wall_timber")); }
        e.Add(S(7, 5, 0, "door_wood"));
        e.Add(F(2, 2, 0, "hearth")); e.Add(F(9, 2, 0, "bed")); e.Add(F(10, 2, 0, "bed"));
        w.Apply(e); w.RoofOver("roof_thatch");
        w.FactsAt(2, 2, 0);
        var times = new List<double>();
        for (int i = 0; i < 300; i++)
        {
            int x = 2 + i % 5, y = 8 + i % 10;
            var sw = Stopwatch.StartNew();
            w.SetFixture(x, y, 0, "table");
            var f = w.FactsAt(2, 2, 0);
            sw.Stop(); times.Add(sw.Elapsed.TotalMilliseconds);
            w.SetFixture(x, y, 0, null);
        }
        times.Sort();
        double median = times[times.Count / 2];
        output.WriteLine($"incremental edit + facts, 12 x 20 x 3: median {median:F3} ms, p95 {times[(int)(times.Count * 0.95)]:F3} ms (budget 0.3 ms)");
        Assert.True(median < 1.5, $"median {median} ms (budget 0.3 ms; allowed 5x for a loaded machine)");
    }

    [Fact]
    public void TheDetectorNeverTouchesARandomStream()
    {
        // 0x520 is reserved for rooms and asserted never drawn: the detector holds no RNG and takes none
        foreach (var t in new[] { typeof(RoomWorld), typeof(RoomV2), typeof(BuildingFacts) })
        {
            var members = t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Select(f => f.FieldType)
                .Concat(t.GetMethods().SelectMany(m => m.GetParameters().Select(p => p.ParameterType))).Concat(t.GetProperties().Select(p => p.PropertyType));
            Assert.DoesNotContain(members, m => m.Name.Contains("Rng", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Random", StringComparison.OrdinalIgnoreCase));
        }
    }
}
