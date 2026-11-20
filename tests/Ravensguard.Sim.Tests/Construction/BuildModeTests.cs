using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Chunks;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Construction;

/// <summary>P5-C golden tests (build-mode-and-construction.md 7): BC-9, BC-10, BC-19, and build mode's sim side: the plan library, the preview, undo, the brushes, Roof over, the designs section.</summary>
public class BuildModeTests(ITestOutputHelper output)
{
    private const long T0 = ConstructionRig.Day10;
    private static PieceCosts Costs => PieceCosts.Of(EconFixture.Content);
    private static PlanLibrary Lib => PlanLibrary.For(EconFixture.Content);
    private static PieceCatalog Pieces => Costs.Library.Pieces;

    /// <summary>An 11 x 28 m town plot (land 4.2), its street on the north side.</summary>
    private static readonly FitLot TownPlot = new(500, 300, 11, 28, 0);

    // ---- BC-9: a plan fitted to its lot ----

    [Fact]
    public void BC9_BurgageMiddling_OnAnElevenByTwentyEightPlot_FrontDoorToTheStreet_AgainstTheStreetEdge_SameTransformEveryRun()
    {
        var g = Lib.Grid("burgage_middling")!;
        Assert.DoesNotContain(g.Levels.Keys, z => z < 0);                                       // the cellar is not dug in Phase 5
        var fit = PlanFitter.Fit(g, TownPlot, Pieces);
        Assert.True(fit.Ok, fit.Key);
        Assert.True(fit.DoorToStreet);
        Assert.Equal(TownPlot.Y, fit.Y);                                                        // against the street edge (no setback on the street side)
        Assert.Equal(6, fit.W); Assert.Equal(12, fit.H);
        Assert.Equal(TownPlot.X + 1 + (9 - 6) / 2, fit.X);                                      // centred along the street inside the 1 m side setbacks
        // the front door is on the street row of the fitted grid
        var ground = fit.Grid!.Level(0)!;
        Assert.Contains(Enumerable.Range(0, ground.Width), x => Pieces.KindOf(ground.Structure[ground.Idx(x, 0)]) == PieceKind.Door);
        for (int run = 0; run < 5; run++)
        {
            var again = PlanFitter.Fit(Lib.Grid("burgage_middling")!, TownPlot, Pieces);
            Assert.Equal((fit.Index, fit.X, fit.Y), (again.Index, again.X, again.Y));
        }
        // the same plot with its street to the east: the house turns so its door faces east, against the east edge
        var east = PlanFitter.Fit(g, new FitLot(500, 300, 28, 11, 1), Pieces);
        Assert.True(east.Ok && east.DoorToStreet, east.Key);
        Assert.Equal(500 + 28, east.X + east.W);
        Assert.Equal(12, east.W);
        output.WriteLine($"north street: transform {fit.Index} at ({fit.X},{fit.Y}); east street: transform {east.Index} at ({east.X},{east.Y})");
        // placed as ghosts in one command, all or nothing, the cellar's stair down now floor
        var r = ConstructionRig.Make();
        var pr = r.W.PlacePlanGrid(r.Hero, PurseKind.Person, -1, fit.Grid, "burgage_middling", fit.X, fit.Y, T0);
        Assert.True(pr.Ok && pr.Refused.Count == 0, pr.Key + " " + string.Join(", ", pr.Refused.Select(x => $"{x.Piece}@{x.X},{x.Y},{x.Z}:{x.Key}")));
        Assert.All(r.W.Book.Ghosts.Values, x => Assert.InRange(x.Z, 0, 2));
    }

    // ---- BC-10: a plan too big for the lot ----

    [Fact]
    public void BC10_APlanTooWideForTheLot_IsRefusedWithItsWidthAndTheLots()
    {
        // the spec names merchant_house, but it is 7 m wide and fits an 11 m plot inside its 1 m side setbacks; the inn (20 x 14) is the town plan that does not
        Assert.True(PlanFitter.Fit(Lib.Grid("merchant_house")!, TownPlot, Pieces).Ok);
        var no = PlanFitter.Fit(Lib.Grid("inn")!, TownPlot, Pieces);
        Assert.False(no.Ok);
        Assert.Equal("cn_fit_too_wide", no.Key);
        Assert.Equal(14, no.Args[0]);                                                           // its narrower side
        Assert.Equal(11, no.Args[1]);                                                           // the lot's frontage
        string text = Ravensguard.Sim.Trade.HeroText.Of(no.Key, no.Args).Render();
        Assert.StartsWith("14 m wide; the lot is 11 m", text);
        // too deep: a plan that fits the frontage but not the depth
        var deep = PlanFitter.Fit(Lib.Grid("burgage_middling")!, new FitLot(0, 0, 11, 10, 0), Pieces);
        Assert.Equal("cn_fit_too_deep", deep.Key);
        // the ground: a road under the footprint
        var road = PlanFitter.Fit(Lib.Grid("burgage_middling")!, TownPlot, Pieces, (x, y) => y != TownPlot.Y + 3);
        Assert.Equal("cn_fit_ground", road.Key);
    }

    // ---- BC-19: save as blueprint ----

    [Fact]
    public void BC19_ASixByFourShed_SavedAsBlueprint_Parses_PassesTheValidator_AndPlacingItAgainGivesTheSameCells()
    {
        var r = ConstructionRig.Make(); long t = T0;
        const int X = 600, Y = 600;
        var cells = new List<PlaceCell>();
        foreach (var (x, y) in ConstructionDesk.Brush(X, Y, X + 5, Y + 3, "outline")) cells.Add(new PlaceCell(x, y, 0, x == X + 2 && y == Y + 3 ? "door_wood" : "wall_wattle"));
        foreach (var (x, y) in ConstructionDesk.Brush(X + 1, Y + 1, X + 4, Y + 2, "rect")) cells.Add(new PlaceCell(x, y, 0, "floor_packed_earth"));
        var pr = r.W.Place(r.Hero, PurseKind.Person, -1, cells, t);
        Assert.True(pr.Ok && pr.Refused.Count == 0, pr.Key);
        var roof = r.W.RoofOver(r.Hero, PurseKind.Person, -1, X + 2, Y + 1, 0, "roof_thatch", t + 1);
        Assert.True(roof.Ok && roof.Refused.Count == 0, roof.Key + " " + string.Join(", ", roof.Refused.Select(x => $"{x.X},{x.Y}:{x.Key}")));
        Assert.Equal(24, roof.Ghosts.Count);                                                    // the room and the walls round it
        r.W.DevStock(pr.Site, t); r.W.DevFinish(pr.Site, 999, 0, 0, t + 2);
        Assert.Empty(r.W.GhostsOf(pr.Site));
        var shed = r.W.PlannedRooms(pr.Site);
        output.WriteLine(string.Join("; ", shed.Select(x => $"{x.Type} {x.Area} m2 indoor={x.Indoor}")));
        Assert.Contains(shed, x => x.Indoor && x.Area == 8);

        var saved = r.W.Capture(X - 2, Y - 2, X + 8, Y + 6, "Wattle shed")!;
        output.WriteLine(saved.Text);
        Assert.Equal("player.wattle_shed", saved.Id);
        Assert.Equal((6, 4), (saved.Width, saved.Height));
        Assert.Equal(new[] { 0, 1 }, saved.Levels);
        Assert.Empty(saved.Warnings);                                                           // BlueprintValidator passes
        // it parses through the mod loader, as the player's mod on disk does
        var report = new ContentReport();
        var defs = new ModLoader().ReadText<BlueprintDef>(saved.Text, "player/" + saved.Id + ".jsonc", report);
        Assert.False(report.HasErrors, report.ToString());
        Assert.Equal(new[] { "any" }, defs.Single().Def.Cultures);
        Assert.Equal(Wealth.Poor, defs.Single().Def.Wealth);
        // placing it again reproduces the same cells
        var book = new DesignBook(); book.Put(saved.Id, saved.Text);
        r.W.Designs = book;
        var lib = r.W.Plans();
        Assert.Contains(lib.All, p => p.Id == saved.Id && p.Player);
        var g = lib.Grid(saved.Id)!;
        var again = r.W.PlacePlanGrid(r.Hero, PurseKind.Person, -1, g, saved.Id, X + 40, Y, t + 10);
        Assert.True(again.Ok && again.Refused.Count == 0, again.Key);
        var built = r.W.Book.Built.Values.Where(b => b.Site == pr.Site).Select(b => (b.X - X, b.Y - Y, b.Z, b.Piece)).OrderBy(c => c).ToList();
        var placed = r.W.GhostsOf(again.Site).Select(x => (x.X - X - 40, x.Y - Y, x.Z, x.Piece)).OrderBy(c => c).ToList();
        Assert.Equal(built, placed);
    }

    // ---- the library ----

    [Fact]
    public void Library_ListsEveryBuildingPlan_FiltersByCultureWealthAndPurpose_AndResolvesWallsThroughTheCulturesStyle()
    {
        var lib = Lib;
        int buildings = Costs.Library.Blueprints.Count(b => b.Def.Kind == BlueprintKind.Building);
        Assert.Equal(buildings, lib.All.Count);
        Assert.True(buildings >= 55, $"{buildings} plans");                                    // world generation's own, about 60 (57 today)
        Assert.Equal(buildings, lib.Filter("north", null, PlanPurpose.Any).Count());           // no content plan names its cultures: every culture lists them all
        Assert.All(lib.Filter(null, Wealth.Rich, PlanPurpose.Any), p => Assert.Equal(Wealth.Rich, p.Wealth));
        var work = lib.Filter(null, null, PlanPurpose.Workshop).Select(p => p.Id).ToList();
        Assert.Contains("smithy_house", work); Assert.Contains("bakery", work); Assert.DoesNotContain("byre", work);
        Assert.Contains("byre", lib.Filter(null, null, PlanPurpose.Livestock).Select(p => p.Id));
        Assert.Contains("peasant_cottage", lib.Filter(null, null, PlanPurpose.Dwelling).Select(p => p.Id));
        Assert.Contains("chapel", lib.Filter(null, null, PlanPurpose.Civic).Select(p => p.Id));
        Assert.Contains("granary", lib.Filter(null, null, PlanPurpose.Storage).Select(p => p.Id));
        // a plan built at a culture and wealth: $wall through the culture's style, as world generation does
        string Wall(string culture, Wealth w) => lib.Grid("peasant_cottage", culture, w)!.Level(0)!.Structure.First(s => s != null && Pieces.KindOf(s) == PieceKind.Wall)!;
        Assert.Equal("wall_wattle", Wall("vale", Wealth.Poor));
        Assert.Equal("wall_timber", Wall("north", Wealth.Poor));
        Assert.Equal("wall_brick", Wall("vale", Wealth.Rich));
        // a design with cultures named is listed only for them
        var only = new PlanLibrary(Costs.Library, EconFixture.Content.All<CultureDef>(), [("player.hut", Hut("player.hut", "[\"north\"]"))]);
        Assert.Contains(only.Filter("north", null, PlanPurpose.Any), p => p.Id == "player.hut");
        Assert.DoesNotContain(only.Filter("vale", null, PlanPurpose.Any), p => p.Id == "player.hut");
        // a broken design is reported, never listed
        var broken = new PlanLibrary(Costs.Library, [], [("player.bad", "[ { \"type\": \"blueprint\", \"id\": \"player.bad\" } ]")]);
        Assert.Single(broken.Broken);
        Assert.DoesNotContain(broken.All, p => p.Id == "player.bad");
    }

    private static string Hut(string id, string cultures) =>
        "[ { \"type\": \"blueprint\", \"id\": \"" + id + "\", \"name\": \"Hut\", \"wealth\": \"poor\", \"cultures\": " + cultures + ",\n" +
        "  \"legend\": { \"#\": \"wall_wattle\", \"+\": \"door_wood\", \"k\": \"floor_packed_earth\" }, \"rooms\": { \"k\": \"hearth\" },\n" +
        "  \"rows\": [ \"###\", \"#k#\", \"#+#\" ] } ]";

    [Fact]
    public void Library_Preview_CostsTheTestCottageAsBC2_AndNamesItsRooms()
    {
        var lib = Lib;
        var pv = lib.Preview("test_cottage", lib.Grid("test_cottage")!, Costs);
        Assert.Equal(250_500, pv.MilliHours);
        Assert.Equal(3_000_000, pv.Grams["thatch"]);
        Assert.Equal(new[] { 0, 1 }.Take(1), pv.Levels.Take(1));
        Assert.Contains(pv.Rooms, x => x.Indoor && x.Area == 18);
        var smithy = lib.Preview("smithy_house", lib.Grid("smithy_house")!, Costs);
        Assert.Contains(smithy.Rooms, x => x.Type == "workshop_smithing");
        Assert.Contains(smithy.Rooms, x => x.Z == 1 && x.Indoor);
    }

    // ---- the preview, undo, the brushes ----

    [Fact]
    public void Preview_ShowsEveryRefusalBeforePlacing_AndChangesNothing()
    {
        var r = ConstructionRig.Make();
        r.W.Ground = (x, y) => (x == 400, x == 401 ? 150 : 20);
        r.W.PropAt = (x, y, z) => x == 403 ? PropHere.Clears : x == 404 ? PropHere.Blocks : PropHere.None;
        string before = r.W.Book.ComputeHash();
        var cells = new List<PlaceCell> { new(400, 400, 0, "wall_wattle"), new(401, 400, 0, "floor_packed_earth"), new(402, 400, 4, "floor_plank"), new(403, 400, 0, "wall_wattle"), new(404, 400, 0, "wall_wattle") };
        var pv = r.W.Preview(r.Hero, PurseKind.Person, -1, cells);
        Assert.Equal(new[] { "cn_on_road", "cn_too_steep", "cn_too_high", "cn_prop_in_way" }, pv.Refused.Select(x => x.Key).ToArray());
        Assert.Equal(1, pv.Ok);
        Assert.Equal((403, 400, 0), pv.Clears.Single());                                       // the tree is felled by building, not a refusal
        Assert.Equal(before, r.W.Book.ComputeHash());
        Assert.Empty(r.W.Book.Sites);
        // a placement then gives the same refusals
        var pr = r.W.Place(r.Hero, PurseKind.Person, -1, cells, T0);
        Assert.Equal(pv.Refused.Select(x => x.Key), pr.Refused.Select(x => x.Key));
    }

    [Fact]
    public void Undo_TakesOffTheLatestPlacement_NotOnceWorkStarted_AndTheBrushesRemoveAndTakeDown()
    {
        var r = ConstructionRig.Make();
        var a = r.W.Place(r.Hero, PurseKind.Person, -1, [.. ConstructionDesk.Brush(700, 700, 704, 700, "line").Select(c => new PlaceCell(c.X, c.Y, 0, "wall_wattle"))], T0);
        var b = r.W.Place(r.Hero, PurseKind.Person, -1, [.. ConstructionDesk.Brush(700, 702, 704, 702, "line").Select(c => new PlaceCell(c.X, c.Y, 0, "wall_wattle"))], T0 + 5);
        Assert.True(a.Ok && b.Ok);
        var u = r.W.UndoLast(r.Hero, -1, T0 + 6);
        Assert.True(u.Ok, u.Key);
        Assert.Equal(5, r.W.Book.Ghosts.Count);
        Assert.All(r.W.Book.Ghosts.Values, g => Assert.Equal(700, g.Y));
        // a started piece stops the undo
        r.StockAll(a.Site); r.Crew(a.Site, 1); r.W.AdvanceTo(T0 + 3600);
        Assert.Equal("cn_undo_started", r.W.UndoLast(r.Hero, -1, T0 + 3600).Key);
        // right-drag removes ghosts in a rectangle (one in work gives its materials back to the pile)
        var rm = r.W.CancelRect(r.Hero, -1, 703, 699, 710, 701, 0, T0 + 3601);
        Assert.True(rm.Ok, rm.Key);
        Assert.Equal(3, r.W.Book.Ghosts.Count(g => g.Value.Live));
        r.AssertMass();
        // the Deconstruct brush marks built pieces in a rectangle
        r.W.DevFinish(a.Site, 99, 0, 0, T0 + 3700);
        var dc = r.W.DeconstructRect(r.Hero, PurseKind.Person, -1, 699, 699, 701, 701, 0, T0 + 3800);
        Assert.True(dc.Ok, dc.Key);
        Assert.Equal(2, r.W.Book.Ghosts.Values.Count(g => g.Work == GhostWork.Deconstruct));
    }

    [Fact]
    public void Stairs_ThePairGoesOnZAndZPlusOne_InOneCommand()
    {
        var r = ConstructionRig.Make();
        var cells = new List<PlaceCell>();
        foreach (var (x, y) in ConstructionDesk.Brush(800, 800, 803, 803, "outline")) cells.Add(new PlaceCell(x, y, 0, "wall_timber"));
        Assert.True(r.W.Place(r.Hero, PurseKind.Person, -1, cells, T0).Ok);
        var st = r.W.PlaceStairs(r.Hero, PurseKind.Person, -1, 801, 801, 0, 0, T0 + 1);
        Assert.True(st.Ok && st.Refused.Count == 0, st.Key + " " + string.Join(",", st.Refused.Select(x => x.Key)));
        Assert.Equal("stairs_down", r.W.GhostAt(801, 801, 1)!.Piece);
    }

    // ---- the designs section ----

    [Fact]
    public void Designs_SectionRoundTrips_ByteIdentical_AndARefusedHashIsRefused()
    {
        var b = new DesignBook();
        b.Put("player.hut", Hut("player.hut", "[\"any\"]"));
        b.Put("player.barn", Hut("player.barn", "[\"any\"]"));
        var s1 = SaveCodec.SerializeCompressed(DesignsSave.Capture(b));
        var back = SaveCodec.DeserializeCompressed<DesignsSave>(s1).Restore();
        Assert.Equal(b.ComputeHash(), back.ComputeHash());
        Assert.Equal(s1, SaveCodec.SerializeCompressed(DesignsSave.Capture(back)));
        var bad = DesignsSave.Capture(b); bad.Texts[0] += " ";
        Assert.Throws<InvalidDataException>(() => bad.Restore());
    }

    [Fact]
    public void PropClearing_TreesAndCratesClear_WellsAndGravesBlock()
    {
        Assert.Equal(PropHere.Clears, PropClearing.Of(PropKind.Tree));
        Assert.Equal(PropHere.Clears, PropClearing.Of(PropKind.Bush));
        Assert.Equal(PropHere.Clears, PropClearing.Of(PropKind.Crate));
        Assert.Equal(PropHere.Blocks, PropClearing.Of(PropKind.Well));
        Assert.Equal(PropHere.Blocks, PropClearing.Of(PropKind.GraveMound));
        // a tree on a visited chunk's cell is removed when a piece is laid there (an ID-keyed RemoveObject delta), the tile set in the same delta
        var store = new ChunkStore();
        var key = new ChunkKey(3, 4, 0);
        var content = new ChunkContent();
        var tree = new PropRecord(PropKind.Tree, 0, 5, 6);
        ulong id = tree.Id(3, 4, 0);
        content.Objects[id] = new ObjectRecord { Id = id, Data = tree.Encode() };
        store.RecordFirstVisit(key, 1, "h", content);
        var sink = new ChunkTileSink(store, new PiecePalette(Pieces));
        sink.Set(3 * 32 + 5, 4 * 32 + 6, 0, "floor_packed_earth", null);
        var after = store.Rebuild(key)!;
        Assert.False(after.Objects.ContainsKey(id));
        Assert.NotEqual(0, after.Tiles.GetValueOrDefault(6 * 32 + 5));
    }
}
