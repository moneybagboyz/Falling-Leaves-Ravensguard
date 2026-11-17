using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Tests.Content;
using Ravensguard.Sim.World;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Building;

public sealed class BuildingFixture
{
    public string BaseDir { get; }
    public ModLoadResult Load { get; }
    public BuildingLibrary Lib { get; }

    public BuildingFixture()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        BaseDir = Path.Combine(root, "content", "base");
        Load = new ModLoader(ContentTypeRegistry.Default).Load([BaseDir]);
        Assert.True(Load.Success, Load.Report.ToString());
        Lib = BuildingLibrary.FromDatabase(Load.Database);
    }
}

public class BuildingTests(BuildingFixture fx, ITestOutputHelper output) : IClassFixture<BuildingFixture>
{
    private BuildingLibrary Lib => fx.Lib;

    private static string Fmt(IEnumerable<BlueprintIssue> issues) => string.Join("\n", issues);

    private string Dump(BuildingGrid g)
    {
        var rooms = Lib.Detector.Detect(g);
        return BlueprintText.Render(g, Lib.Pieces, showRegions: true, rooms);
    }

    // ---- content ----

    [Fact]
    public void BaseBlueprintsAreRegisteredAndPresent()
    {
        var ids = Lib.Blueprints.Select(b => b.Def.Id).ToHashSet();
        foreach (var id in new[] { "peasant_cottage", "longhouse", "burgage_townhouse", "smithy", "tavern", "chapel", "barn", "granary", "manor_hall", "watch_tower",
                                   "template_bedroom", "template_workshop", "template_hall", "template_cellar" })
            Assert.Contains(id, ids);
    }

    [Fact]
    public void EveryBaseBlueprintValidates()
    {
        var all = new List<BlueprintIssue>();
        foreach (var e in Lib.Blueprints)
        {
            var issues = BlueprintValidator.Validate(e, Lib);
            output.WriteLine($"{e.Def.Id}: {(issues.Count == 0 ? "ok" : issues.Count + " issue(s)")}");
            if (issues.Count > 0)
            {
                var g = Lib.Parse(e, []);
                if (g != null) output.WriteLine(Dump(g));
            }
            all.AddRange(issues);
        }
        Assert.True(all.Count == 0, Fmt(all));
    }

    [Fact]
    public void TextDumpShowsEveryLevel()
    {
        var g = Lib.Grid("burgage_townhouse");
        var text = BlueprintText.Render(g, Lib.Pieces);
        output.WriteLine(text);
        Assert.Contains("z=+2", text); Assert.Contains("z=-1", text);
        Assert.Contains("SS+SSS", text);
        Assert.True(text.IndexOf("z=+2", StringComparison.Ordinal) < text.IndexOf("z=-1", StringComparison.Ordinal));
    }

    // ---- parsing ----

    private BlueprintDef Cottage() => Lib.Entry("peasant_cottage").Def;

    private List<BlueprintIssue> Validate(BlueprintDef def) => BlueprintValidator.Validate(BlueprintEntry.Of(def), Lib);

    [Fact]
    public void ParsingErrorsCarryFileAndLine()
    {
        using var t = new TempDir();
        var bad = """
        [
          { "type": "blueprint", "id": "bad_house", "name": "Bad",
            "legend": { "#": "wall_timber", ".": "floor_plank", "?": "no_such_piece" },
            "rows": [
              "#####",
              "#...",
              "#####"
            ] }
        ]
        """;
        var mod = t.Mod("badmod", bad, "\"dependencies\": [\"base\"]");
        var r = new ModLoader(ContentTypeRegistry.Default).Load([fx.BaseDir, mod]);
        Assert.True(r.Success, r.Report.ToString());
        var lib = BuildingLibrary.FromDatabase(r.Database);
        var issues = BlueprintValidator.Validate(lib.Entry("bad_house"), lib);
        Assert.Contains(issues, i => i.Code == "legend.unknown_piece" && i.Location is { Line: > 0 } l && l.File.EndsWith("a.jsonc"));
        // the ragged row is reported too (legend errors do not hide it), with its line
        Assert.Contains(issues, i => i.Code == "rows.ragged" && i.Location is { Line: > 0 });
        output.WriteLine(Fmt(issues));
    }

    [Fact]
    public void RaggedRowsAndSizeMismatchAreReported()
    {
        var d = Cottage() with { Levels = [new BlueprintLevelDef { Z = 1, Rows = ["##", "##"] }] };
        var issues = Validate(d);
        Assert.Contains(issues, i => i.Code == "rows.ragged");
        Assert.Contains(issues, i => i.Code == "rows.size_mismatch");
    }

    [Fact]
    public void UnknownLegendIdAndUnknownCharAreReported()
    {
        var d = Cottage() with { Legend = new Dictionary<string, string>(Cottage().Legend) { ["k"] = "nope" } };
        Assert.Contains(Validate(d), i => i.Code == "legend.unknown_piece");
        var d2 = Cottage() with { Rows = Cottage().Rows.Select(r => r.Replace('o', '!')).ToList() };
        Assert.Contains(Validate(d2), i => i.Code == "rows.unknown_char");
    }

    [Fact]
    public void WealthChoosesWallMaterial()
    {
        var poor = Lib.Grid("peasant_cottage");
        Assert.Equal("wall_wattle", poor.StructureAt(0, 0, 0));
        var rich = BlueprintParser.Parse(BlueprintEntry.Of(Cottage() with { Wealth = Wealth.Rich }), Lib.Pieces, Lib.Styles, []);
        Assert.Equal("wall_stone", rich!.StructureAt(0, 0, 0));
        var hill = BlueprintParser.Parse(BlueprintEntry.Of(Cottage() with { Style = "hillfolk" }), Lib.Pieces, Lib.Styles, []);
        Assert.Equal("wall_timber", hill!.StructureAt(0, 0, 0));
    }

    // ---- rotation ----

    [Fact]
    public void RotateFourTimesIsIdentity()
    {
        foreach (var e in Lib.Blueprints)
        {
            var g = Lib.Grid(e.Def.Id);
            var r = g.Rotate90();
            Assert.Equal((g.Height, g.Width), (r.Width, r.Height));
            Assert.True(g.ContentEquals(r.Rotate90().Rotate90().Rotate90()), e.Def.Id);
            Assert.True(g.ContentEquals(g.MirrorX().MirrorX()), e.Def.Id);
        }
    }

    [Fact]
    public void RotatedAndMirroredBlueprintsStillValidateAsRooms()
    {
        var g = Lib.Grid("peasant_cottage").Transform(1, true);
        var rooms = Lib.Detector.Detect(g);
        Assert.Equal(2, rooms.Count);
        Assert.All(rooms, r => Assert.True(r.Enclosed));
        Assert.Empty(StructuralSupport.Unsupported(g, Lib.Pieces));
        Assert.Equal(g.Width * g.Height, g.Level(0)!.Structure.Length);
    }

    // ---- enclosure, support ----

    [Fact]
    public void CottageWithRemovedWallFailsEnclosed()
    {
        var c = Cottage();
        var rows = c.Rows.ToList();
        rows[4] = rows[4].Remove(1, 1).Insert(1, "_"); // knock out a south wall cell
        var issues = Validate(c with { Rows = rows });
        output.WriteLine(Fmt(issues));
        Assert.Contains(issues, i => i.Code == "room.not_enclosed");
    }

    [Fact]
    public void UpperFloorWithoutSupportsFails()
    {
        string Row(char c, int n = 10) => new(c, n);
        var ground = new List<string> { "+........." };
        for (int i = 0; i < 7; i++) ground.Add(Row('.'));
        ground.Add("#........<");
        var upper = new List<string>();
        for (int i = 0; i < 8; i++) upper.Add(Row('.'));
        upper.Add("........>_");
        var def = new BlueprintDef
        {
            Id = "floating", Name = "Floating floor",
            Legend = new() { ["#"] = "wall_timber", ["."] = "floor_plank", ["+"] = "door_wood", ["<"] = "stairs_up", [">"] = "stairs_down" },
            Rows = ground,
            Levels = [new BlueprintLevelDef { Z = 1, Rows = upper }],
        };
        var issues = Validate(def);
        Assert.Contains(issues, i => i.Code == "support.missing");
    }

    [Fact]
    public void PostsHoldUpAWideUpperFloor()
    {
        List<string> Hall(params (int X, int Y)[] posts)
        {
            var rows = new List<string>();
            for (int y = 0; y < 11; y++)
            {
                var r = new char[11];
                for (int x = 0; x < 11; x++) r[x] = x is 0 or 10 || y is 0 or 10 ? '#' : posts.Contains((x, y)) ? '|' : '.';
                rows.Add(new string(r));
            }
            return rows;
        }
        BuildingGrid Parse(List<string> ground, List<string> upper)
        {
            var d = new BlueprintDef { Id = "wide", Name = "wide", Rows = ground, Levels = [new BlueprintLevelDef { Z = 1, Rows = upper }],
                Legend = new() { ["#"] = "wall_timber", ["."] = "floor_plank", ["|"] = "post_timber" } };
            var issues = new List<BlueprintIssue>();
            return BlueprintParser.Parse(BlueprintEntry.Of(d), Lib.Pieces, Lib.Styles, issues) ?? throw new Exception(Fmt(issues));
        }
        var upper = Hall();
        var bare = StructuralSupport.Unsupported(Parse(Hall(), upper), Lib.Pieces);
        Assert.Contains(new Cell3(5, 5, 1), bare);
        var posts = new[] { (3, 3), (7, 3), (3, 7), (7, 7) };
        Assert.Empty(StructuralSupport.Unsupported(Parse(Hall(posts), upper), Lib.Pieces));
    }

    [Fact]
    public void MismatchedStairsAreReported()
    {
        var c = Lib.Entry("burgage_townhouse").Def;
        var lv = c.Levels.Select(l => l.Z == 2 ? l with { Rows = l.Rows.Select(r => r.Replace('>', 'v')).ToList() } : l).ToList();
        Assert.Contains(Validate(c with { Levels = lv }), i => i.Code == "stairs.unmatched");
    }

    // ---- detector ----

    private BuildingGrid Tiny(string[] rows, string[] fixtures)
    {
        var legend = new Dictionary<string, string> { ["#"] = "wall_timber", ["+"] = "door_wood", ["."] = "floor_plank", ["B"] = "bed", ["U"] = "bunk", ["E"] = "hearth",
            ["N"] = "anvil", ["T"] = "table", ["C"] = "chest", ["M"] = "loom" };
        var d = new BlueprintDef { Id = "tiny", Name = "tiny", Legend = legend, Rows = rows.ToList(), Fixtures = fixtures.ToList() };
        var issues = new List<BlueprintIssue>();
        var g = BlueprintParser.Parse(BlueprintEntry.Of(d), Lib.Pieces, Lib.Styles, issues);
        Assert.True(g != null, Fmt(issues));
        return g!;
    }

    private static readonly string[] ThreeRooms =
        ["###########",
         "#.....#...#",
         "#.....+...#",
         "#.....#####",
         "#.....#...#",
         "#.....+...#",
         "#####+#####"];

    [Fact]
    public void DetectorFindsBedroomKitchenAndWorkshop()
    {
        var g = Tiny(ThreeRooms,
            ["___________",
             "_B_B_____E_",
             "___________",
             "___________",
             "_______M___",
             "___________",
             "___________"]);
        output.WriteLine(Dump(g));
        var rooms = Lib.Detector.Detect(g);
        Assert.Equal(3, rooms.Count);
        var west = rooms.Single(r => r.Contains(1, 1));
        Assert.Equal("bedroom", west.Primary!.Id);
        Assert.Equal(25, west.Area);
        Assert.Equal(2, west.Beds);
        Assert.True(west.Enclosed);
        Assert.Equal(3, west.Doors.Count);
        Assert.True(west.HasExteriorDoor);
        var kitchen = rooms.Single(r => r.Contains(9, 1));
        Assert.Equal("kitchen", kitchen.Primary!.Id);
        Assert.Equal(6, kitchen.Area);
        var weaving = rooms.Single(r => r.Contains(7, 4));
        Assert.Equal("workshop_weaving", weaving.Primary!.Id);
        Assert.False(weaving.HasExteriorDoor);
    }

    [Fact]
    public void WorkshopOutranksBedroomWhenBothMatch()
    {
        var g = Tiny(ThreeRooms,
            ["___________",
             "_B_B_______",
             "___________",
             "___________",
             "_N_________",
             "___________",
             "___________"]);
        var west = Lib.Detector.Detect(g).Single(r => r.Contains(1, 1));
        Assert.Equal("workshop_smithing", west.Primary!.Id);
        Assert.Contains(west.Matches, m => m.Id == "bedroom");
        Assert.Equal(2, west.Beds);
    }

    [Fact]
    public void BunksCountTwoBeds()
    {
        var g = Tiny(
            ["#########",
             "#..#....#",
             "#..+....#",
             "#..#....#",
             "####+####"],
            ["_________",
             "_BU______",
             "_____E___",
             "_________",
             "_________"]);
        var rooms = Lib.Detector.Detect(g);
        var bed = rooms.Single(r => r.Contains(1, 1));
        Assert.Equal("bedroom", bed.Primary!.Id);
        Assert.Equal(3, bed.Beds);
        var kitchen = rooms.Single(r => r.Contains(5, 2));
        Assert.Equal("kitchen", kitchen.Primary!.Id);
        Assert.Equal(0, kitchen.Beds);
    }

    [Fact]
    public void BedroomWithoutBedsOrOpenToOutsideIsNotABedroom()
    {
        var g = Tiny(["#####", "#...#", "#...+", "#...#", "#####"], ["_____", "_B___", "_____", "_____", "_____"]);
        Assert.Equal("bedroom", Lib.Detector.Detect(g).Single().Primary!.Id);
        var open = Tiny(["#####", "#...#", "#....", "#...#", "#####"], ["_____", "_B___", "_____", "_____", "_____"]);
        var r = Lib.Detector.Detect(open).Single();
        Assert.False(r.Enclosed);
        Assert.Null(r.Primary);
    }

    [Fact]
    public void HallNeedsTableAndArea()
    {
        string[] rows = ["########", "#......#", "#......#", "#......#", "#......#", "###+####"];
        var big = Tiny(rows, ["________", "_T______", "________", "________", "________", "________"]);
        Assert.Equal("hall", Lib.Detector.Detect(big).Single().Primary!.Id);
        var small = Tiny(["#####", "#...#", "##+##"], ["_____", "__T__", "_____"]);
        Assert.Null(Lib.Detector.Detect(small).Single().Primary);
    }

    [Fact]
    public void ContentBlueprintsKeepTheirDeclaredPurposeWhenFurnished()
    {
        // the cottage hearth room is recognised straight from its fixed hearth
        var g = Lib.Grid("peasant_cottage");
        var rooms = Lib.Detector.Detect(g);
        Assert.Equal("kitchen", rooms.Single(r => r.Z == 0).Primary!.Id);
        Assert.True(rooms.Single(r => r.Z == 0).PurposeMatches);
        Assert.Equal("smithing", Lib.Pieces.Get("forge").Station);
        var smithy = Lib.Detector.Detect(Lib.Grid("smithy"));
        Assert.Contains(smithy, r => r.Primary?.Id == "workshop_smithing");
    }

    // ---- housing ----

    [Fact]
    public void DwellingsMeetPlanBedCapacity()
    {
        foreach (var (id, kind) in new[] { ("peasant_cottage", "cottage"), ("longhouse", "longhouse"), ("burgage_townhouse", "burgage") })
        {
            var g = Lib.Grid(id);
            int beds = BlueprintValidator.SleepingCapacity(g, Lib.Detector.Detect(g));
            var (min, max) = BlueprintValidator.HousingBeds[kind];
            output.WriteLine($"{id}: {beds} beds");
            Assert.InRange(beds, min, max);
        }
    }

    // ---- placement and pathing ----

    [Fact]
    public void BurgageIsReachableFromStreetDoorToTopFloorAndCellar()
    {
        var g = Lib.Grid("burgage_townhouse");
        var world = new LevelGrid(20, 20, -5, 3);
        var placed = BlueprintPlacer.Place(g, Lib.Pieces, world, 5, 4);
        Assert.Equal(new Cell3(7, 4, 0), Assert.Single(placed.ExteriorDoors, d => d.Y == 4));
        Assert.Equal(3, placed.Portals.Count); // up to +1, up to +2, down to the cellar
        var path = new LevelPathfinder(world);
        var street = new Cell3(7, 4, 0);
        Assert.True(path.IsReachable(street, new Cell3(6, 5, 2)), "front bedroom at +2"); // (1,1,z2)
        Assert.True(path.IsReachable(street, new Cell3(7, 12, 2)), "rear storeroom at +2");
        Assert.True(path.IsReachable(street, new Cell3(8, 12, -1)), "cellar");
        Assert.NotNull(path.FindPath(street, new Cell3(6, 5, 2)));
        // pulling the stairs' floor out cuts the upper floors off
        foreach (var p in placed.Portals.Where(p => p.A.Z == 0 && p.B.Z == 1)) world.SetPortalEnabled(p.Id, false);
        Assert.False(new LevelPathfinder(world).IsReachable(street, new Cell3(6, 5, 2)));
    }

    [Fact]
    public void PlacementWorksRotatedAndMirrored()
    {
        var g = Lib.Grid("watch_tower");
        var world = new LevelGrid(16, 16, -5, 3);
        var placed = BlueprintPlacer.Place(g, Lib.Pieces, world, 2, 2, quarterTurns: 3, mirror: true);
        var door = Assert.Single(placed.ExteriorDoors);
        Assert.True(new LevelPathfinder(world).IsReachable(door, new Cell3(placed.X + 3, placed.Y + 3, 3)));
    }

    [Fact]
    public void TowerReachesPlusThree()
    {
        var g = Lib.Grid("watch_tower");
        var reached = BlueprintValidator.Reachable(g, Lib.Pieces, Lib.Detector);
        Assert.Contains(new Cell3(3, 3, 3), reached);
        Assert.Equal(new[] { 0, 1, 2, 3 }, g.Levels.Keys.ToArray());
    }

    [Fact]
    public void UnreachableRoomIsReported()
    {
        var c = Lib.Entry("smithy").Def;
        // wall off the store: replace the partition door with a wall
        var rows = c.Rows.Select(r => r).ToList();
        rows[2] = rows[2].Replace("+vv", "#vv");
        var issues = Validate(c with { Rows = rows });
        Assert.Contains(issues, i => i.Code == "room.unreachable");
    }

    [Fact]
    public void UnsatisfiablePurposeIsReported()
    {
        var c = Lib.Entry("template_bedroom").Def with { Rooms = new() { ["s"] = "work:alchemy" } };
        Assert.Contains(Validate(c), i => i.Code == "rooms.unknown_purpose");
        var tiny = Lib.Entry("template_bedroom").Def with { Rooms = new() { ["s"] = "worship" } }; // chapel needs area 12; the bedroom has 6
        Assert.Contains(Validate(tiny), i => i.Code == "room.unsatisfiable");
    }
}
