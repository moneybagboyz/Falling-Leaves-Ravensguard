using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.World;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Construction;

/// <summary>P5-B golden tests (build-mode-and-construction.md 7): BC-1 to BC-8, BC-11 to BC-15, BC-20 here; BC-18 and BC-21 in their own files.</summary>
public class ConstructionTests(ITestOutputHelper output)
{
    private static PieceCosts Costs => PieceCosts.Of(EconFixture.Content);
    private const long T0 = ConstructionRig.Day10;

    // ---- BC-1: content ----

    [Fact]
    public void BC1_EveryPieceHasACost_EveryMaterialResolves_EveryPlanResolvesItsPieces()
    {
        var db = EconFixture.Content;
        var goods = new GoodCatalog(db);
        var tools = db.All<CraftToolDef>().SelectMany(t => t.Qualities.Keys).ToHashSet();
        var pieces = db.All<BuildPieceDef>().ToList();
        Assert.Equal(163, pieces.Count);                                      // P7-AM-C: +6 (tent_soldier, tent_pavilion, campfire, horse_line, latrine, ditch_shallow); P7-SG-A: +2 (rubble_stone, rubble_timber); P7-LB2: +3 (door_strong, lamp_stand, dressing_floor); P7-IN2: +24 (the industry tiers' pieces, build/pieces_industry.jsonc); before that // 80 with P5-B, plus the 19 workstation pieces of P5-E (crafting-full 3.2), plus P6-B6's wheelwright's bay and the anvil set from a forged block (holdings 3.6, 3.8), plus the 27 power, machine and large-building pieces of P7-LB1 (large-buildings 3.2)
        foreach (var p in pieces)
        {
            Assert.True(p.Build != null, $"{p.Id} has no build cost");
            Assert.True(p.Build!.WorkerHours > 0, $"{p.Id}: no worker-hours");
            Assert.Contains(p.Build.Skill, new[] { "none", "carpentry", "masonry" });
            foreach (var m in p.Build.Materials) Assert.True(goods.TryIdOf(m.Good, out _) && m.Kg > 0, $"{p.Id}: material {m.Good}");
            foreach (var t in p.Build.Tools) Assert.True(tools.Contains(t.Quality), $"{p.Id}: tool quality {t.Quality}");
            Assert.InRange(p.Build.SalvagePm, 0, 1000);
        }
        Assert.Contains(pieces, p => p.Kind == PieceKind.Beam);
        Assert.NotNull(db.All<ConstructionRulesDef>().SingleOrDefault());
        var lib = Costs.Library;
        foreach (var e in lib.Blueprints.Where(b => b.Def.Kind == BlueprintKind.Building))
        {
            var issues = new List<BlueprintIssue>();
            var g = lib.Parse(e, issues);
            Assert.True(g != null, $"{e.Def.Id}: {string.Join("; ", issues)}");
            foreach (var c in Costs.PlanCells(g!)) Assert.True(Costs.Get(c.Piece) is { HasBuild: true }, $"{e.Def.Id}: {c.Piece}");
        }
    }

    // ---- BC-2: the golden cottage ----

    [Fact]
    public void BC2_TestCottage_Costs2505WorkerHours_AndItsMaterials()
    {
        var g = Costs.Library.Grid("test_cottage");
        var cells = Costs.PlanCells(g);
        Assert.Equal(19, cells.Count(c => c.Piece == "wall_wattle"));
        Assert.Equal(40, cells.Count(c => c.Piece == "roof_thatch"));
        Assert.Equal(18, cells.Count(c => c.Piece == "floor_packed_earth"));
        var (h, grams) = Costs.Sum(cells.Select(c => c.Piece));
        Assert.Equal(250_500, h);
        Assert.Equal(394_000, grams["logs"]);
        Assert.Equal(1_501_000, grams["clay"]);
        Assert.Equal(3_000_000, grams["thatch"]);
        Assert.Equal(30_000, grams["timber_planks"]);
        Assert.Equal(300, grams["nails"]);
        Assert.Equal(5, grams.Count);
    }

    // ---- BC-3, BC-4: hands-on ----

    private static (ConstructionRig R, EntityId Ghost, EntityId Site) OneWall(string piece = "wall_timber")
    {
        var r = ConstructionRig.Make();
        var pr = r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(100, 100, 0, piece)], T0);
        Assert.True(pr.Ok, pr.Key);
        r.StockAll(pr.Site);
        return (r, pr.Ghosts[0], pr.Site);
    }

    [Theory]
    [InlineData(true, 42_480)]       // BC-3: Carpentry 20 (time_mult 1,180), axe and saw: 11.8 h
    [InlineData(false, 63_720)]      // BC-4: no saw: x1.5, 17.7 h
    public void BC3_BC4_HeroBuildsATimberWallByHand(bool saw, long seconds)
    {
        var (r, ghost, site) = OneWall();
        r.Skills[(r.Hero.Value, Skill.Carpentry)] = 20;
        var tools = r.W.ToolQualities(saw ? ["axe", "saw"] : ["axe"]);
        var pile = r.W.PileOf(r.W.Site(site)!);
        long logs = pile.Qty(r.G("logs"));
        Assert.Equal(120_000, logs);
        // in daylight, six hours a day from 06:00, until a second short: the wall stands not yet, its materials are in the piece (pile -> ghost)
        long done = 0, day = T0 / 86400;
        while (done < seconds - 1)
        {
            long len = Math.Min(6 * 3600, seconds - 1 - done), from = day * 86400 + 6 * 3600;
            var res = r.W.HeroWork(r.Hero, PurseKind.Person, -1, ghost, from, from + len, 100, 101, 0, tools);
            Assert.True(res.Ok, res.Key);
            done += len; day++;
        }
        Assert.Null(r.W.BuiltAt(100, 100, 0));
        Assert.Equal(0, pile.Qty(r.G("logs")));
        Assert.Equal(120_000, r.Econ.OtherStores[ghost].Qty(r.G("logs")));
        r.AssertMass();
        long at = day * 86400 + 6 * 3600;
        var last = r.W.HeroWork(r.Hero, PurseKind.Person, -1, ghost, at, at + 1, 100, 101, 0, tools);
        Assert.True(last.Ok, last.Key);
        var b = r.W.BuiltAt(100, 100, 0);
        Assert.NotNull(b);
        Assert.Equal(20, b!.BuiltSkill);
        Assert.Equal(600 * (700 + 6 * 20) / 1000, b.Hp);                       // a weaker wall at skill 20, but a wall
        Assert.False(r.Econ.OtherStores.ContainsKey(ghost));
        Assert.Equal(120_000, r.W.Book.MaterialisedG[r.G("logs")]);
        r.AssertMass();
        // skill gain: one use of Carpentry for every attended hour
        Assert.Equal((int)(seconds / 3600), r.Trained.Count(t => t.P == r.Hero && t.S == Skill.Carpentry));
        output.WriteLine($"{seconds} s = {seconds / 3600.0:0.0} h; {r.Trained.Count} skill uses");
    }

    // ---- BC-5: materials stop ----

    [Fact]
    public void BC5_TenWalls_ClayForSix_SixBuilt_FourWait_ThreeMoreBagsResume()
    {
        var r = ConstructionRig.Make();
        var cells = Enumerable.Range(0, 10).Select(i => new PlaceCell(200 + i, 200, 0, "wall_wattle")).ToList();
        var pr = r.W.Place(r.Hero, PurseKind.Person, -1, cells, T0);
        Assert.True(pr.Ok, pr.Key);
        r.Stock(pr.Site, ("logs", 160_000), ("clay", 6 * 79_000));
        r.Crew(pr.Site, 2);
        r.W.AdvanceTo(T0 + 3 * 86400);
        var s = r.W.Site(pr.Site)!;
        Assert.Equal(6, r.W.Book.Built.Count);
        var left = r.W.GhostsOf(pr.Site).ToList();
        Assert.Equal(4, left.Count);
        Assert.All(left, g => { Assert.Equal(GhostState.Blocked, g.State); Assert.Equal(GhostBlock.Materials, g.Block); });
        Assert.Equal(PauseReason.NoMaterials, s.Pause);
        r.Stock(pr.Site, ("clay", 3 * 79_000));
        Assert.Equal(PauseReason.None, s.Pause);
        r.W.AdvanceTo(T0 + 5 * 86400);
        Assert.Equal(9, r.W.Book.Built.Count);
        Assert.Single(r.W.GhostsOf(pr.Site));
        r.AssertMass();
    }

    // ---- BC-6: wages stop ----

    [Fact]
    public void BC6_PayerWith450_TwoLabourers_DayOneWorks_DayTwoStops_PaidUpItResumes()
    {
        var r = ConstructionRig.Make(crowns: 0);
        var payer = r.HeroPurse;
        r.Econ.Ledger.Mint(payer, new Coin(450), SourceReason.Seed);
        var cells = Enumerable.Range(0, 20).Select(i => new PlaceCell(300 + i, 300, 0, "wall_wattle")).ToList();
        var pr = r.W.Place(r.Hero, PurseKind.Person, -1, cells, T0 - 10 * 3600);   // the evening before
        r.StockAll(pr.Site);
        var a = EntityId.Make(IdKind.Person, 8_000_001); var b = EntityId.Make(IdKind.Person, 8_000_002);
        Assert.True(r.W.AddWorker(pr.Site, a, -1, CrewClass.Labourer, 150).Ok);
        Assert.True(r.W.AddWorker(pr.Site, b, -1, CrewClass.Labourer, 150).Ok);
        var s = r.W.Site(pr.Site)!;
        r.W.AdvanceTo(T0 + 300);                                                       // day 10, 06:00 (the first quantum): escrow 300
        Assert.Equal(300, r.Econ.Ledger.Balance(r.Econ.Ledger.Open(PurseKind.Chest, s.Escrow)).Mites);
        Assert.Equal(150, r.Econ.Ledger.Balance(payer).Mites);
        r.W.AdvanceTo(T0 + 10 * 3600 + 300);                                           // 16:00: paid a full day each
        Assert.Equal(300, s.PaidMites);
        int built = r.W.Book.Built.Count;
        Assert.True(built > 0);
        r.W.AdvanceTo(T0 + 86400 + 300);                                                // day 11, 06:00: 300 needed, 150 left
        Assert.Equal(PauseReason.NoWages, s.Pause);
        r.W.AdvanceTo(T0 + 86400 + 10 * 3600);
        Assert.Equal(built, r.W.Book.Built.Count);                                      // progress kept, nothing more done
        r.Econ.Ledger.Mint(payer, new Coin(600), SourceReason.Seed);
        r.W.AdvanceTo(T0 + 86400 + 12 * 3600);
        Assert.Equal(PauseReason.NoWages, s.Pause);                                     // not before the next 06:00
        r.W.AdvanceTo(T0 + 2 * 86400 + 300);
        Assert.Equal(PauseReason.None, s.Pause);
        r.W.AdvanceTo(T0 + 2 * 86400 + 3 * 3600);
        Assert.True(r.W.Book.Built.Count > built);
        r.AssertCoin(); r.AssertMass();
    }

    // ---- BC-8: free placement validity ----

    [Fact]
    public void BC8_RoadSlopeAndHeight_AreRefusedWithTheirReasons_NothingPlaced()
    {
        var r = ConstructionRig.Make();
        r.W.Ground = (x, y) => (x == 400, x == 401 ? 150 : 20);
        var pr = r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(400, 400, 0, "wall_wattle"), new PlaceCell(401, 400, 0, "floor_packed_earth"), new PlaceCell(402, 400, 4, "floor_plank")], T0);
        Assert.False(pr.Ok);
        Assert.Equal(new[] { "cn_on_road", "cn_too_steep", "cn_too_high" }, pr.Refused.Select(x => x.Key).ToArray());
        Assert.Empty(r.W.Book.Ghosts);
        Assert.Empty(r.W.Book.Sites);
        // the land rule: a neighbour's lot is refused with the holder's name
        r.W.Ground = null;
        r.W.May = (p, hh, act, x, y) => new MayResult(false, "Hedda Tanner", EntityId.None, "ld_may_theirs");
        var no = r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(400, 400, 0, "wall_wattle")], T0);
        Assert.Equal("ld_may_theirs", no.Refused.Single().Key);
        Assert.Equal("Hedda Tanner", no.Refused.Single().Args[0]);
        // a built or planned cell, and a fixture without a floor
        r.W.May = null;
        Assert.True(r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(410, 400, 0, "wall_wattle")], T0).Ok);
        Assert.Equal("cn_occupied", r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(410, 400, 0, "wall_timber")], T0).Refused.Single().Key);
        Assert.Equal("cn_needs_floor", r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(420, 400, 0, "bed")], T0).Refused.Single().Key);
    }

    // ---- BC-11, BC-12: support and load ----

    /// <summary>Builds pieces through the real path: ghosts, the pile stocked, a crew of eight working until nothing is left.</summary>
    private static EntityId Build(ConstructionRig r, IReadOnlyList<PlaceCell> cells, ref long t)
    {
        var pr = r.W.Place(r.Hero, PurseKind.Person, -1, cells, t);
        Assert.True(pr.Ok && pr.Refused.Count == 0, pr.Key + " " + string.Join(", ", pr.Refused.Select(x => $"{x.Piece}@{x.X},{x.Y},{x.Z}:{x.Key}")));
        r.StockAll(pr.Site);
        if (r.W.Site(pr.Site)!.Crew.Count == 0) r.Crew(pr.Site, 8);
        for (int d = 0; d < 30 && r.W.GhostsOf(pr.Site).Any(); d++) { t += 86400; r.W.AdvanceTo(t); }
        Assert.Empty(r.W.GhostsOf(pr.Site));
        return pr.Site;
    }

    [Fact]
    public void BC11_AFloorFourTilesFromAnyPost_IsRefused_ABeamOnPostsSixApart_CarriesIt()
    {
        var r = ConstructionRig.Make(); long t = T0;
        Build(r, [new PlaceCell(500, 500, 0, "post_timber"), new PlaceCell(506, 500, 0, "post_timber")], ref t);
        var no = r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(503, 504, 1, "floor_plank")], t);
        Assert.Equal("cn_no_support", no.Refused.Single().Key);
        // a beam whose ends rest on the posts, then the floor four tiles off the posts rests on the beam's joists
        var beams = Enumerable.Range(0, 7).Select(i => new PlaceCell(500 + i, 500, 1, "beam_timber")).ToList();
        var yes = r.W.Place(r.Hero, PurseKind.Person, -1, [.. beams, new PlaceCell(503, 504, 1, "floor_plank")], t);
        Assert.True(yes.Ok && yes.Refused.Count == 0, string.Join(",", yes.Refused.Select(x => x.Key)));
        // until the beams are built the floor waits (Blocked: Support)
        r.StockAll(yes.Site);
        Assert.Equal(GhostBlock.Support, r.W.GhostAt(503, 504, 1)!.Block);
        // a span of 7 is too long
        var r2 = ConstructionRig.Make(); long t2 = T0;
        Build(r2, [new PlaceCell(500, 500, 0, "post_timber"), new PlaceCell(507, 500, 0, "post_timber")], ref t2);
        var longBeam = Enumerable.Range(0, 8).Select(i => new PlaceCell(500 + i, 500, 1, "beam_timber")).ToList();
        var too = r2.W.Place(r2.Hero, PurseKind.Person, -1, [.. longBeam, new PlaceCell(503, 504, 1, "floor_plank")], t2);
        Assert.Contains(too.Refused, x => x.Y == 504 && x.Key == "cn_no_support");
    }

    [Fact]
    public void BC12_ASecondStoreyFloorOverWattle_IsRefused_OverTimberItStands()
    {
        foreach (var (wall, ok) in new[] { ("wall_wattle", false), ("wall_timber", true) })
        {
            var r = ConstructionRig.Make(); long t = T0;
            // two storeys of walls round a 4 x 4 room
            var cells = new List<PlaceCell>();
            for (int z = 0; z <= 1; z++)
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
                        if (x == 0 || y == 0 || x == 3 || y == 3) cells.Add(new PlaceCell(600 + x, 600 + y, z, wall));
            var first = r.W.Place(r.Hero, PurseKind.Person, -1, cells, t);
            Assert.True(first.Ok && first.Refused.Count == 0, string.Join(",", first.Refused.Select(x => x.Key)));
            var floor = r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(601, 601, 2, "floor_plank")], t);
            if (ok) Assert.True(floor.Ok && floor.Refused.Count == 0);
            else
            {
                var x = floor.Refused.Single();
                Assert.Equal("cn_load", x.Key);
                Assert.Equal("Wattle-and-daub wall", x.Args[0]);
                Assert.Equal(1, x.Args[1]);
            }
        }
    }

    // ---- BC-13, BC-14: collapse ----

    /// <summary>A 3 x 3 floor at z = 1 held up by one post, with a stair down in it over a stair up; a chest on it.</summary>
    private static (ConstructionRig R, long T) Platform(bool upperStorey = false)
    {
        var r = ConstructionRig.Make(); long t = T0;
        Build(r, [new PlaceCell(705, 705, 0, "post_timber"), new PlaceCell(704, 705, 0, "stairs_up")], ref t);
        var floor = new List<PlaceCell>();
        for (int y = 704; y <= 706; y++) for (int x = 704; x <= 706; x++) floor.Add(new PlaceCell(x, y, 1, x == 704 && y == 705 ? "stairs_down" : "floor_plank"));
        Build(r, floor, ref t);
        if (upperStorey)
        {
            // a post on the platform's level, a jetty off the post below, holds a floor at z = 2
            r.W.Destroy(706, 706, 1, t, EntityId.None, "test");                // make room: the corner cell becomes the upper post's
            t += 60; r.W.AdvanceTo(t);
            Build(r, [new PlaceCell(706, 706, 1, "post_timber")], ref t);
            Build(r, [new PlaceCell(706, 706, 2, "floor_plank"), new PlaceCell(707, 706, 2, "floor_plank")], ref t);
        }
        return (r, t);
    }

    [Fact]
    public void BC13_TheOnlyPostTakenDown_TheFloorGroans10s_ThenFalls_WithAPersonAndAChest()
    {
        var (r, t) = Platform();
        Assert.Equal(9, r.W.Book.Built.Values.Count(b => b.Z == 1));
        var grid0 = r.Grid(700, 700, 12, 12);
        Assert.NotNull(new LevelPathfinder(grid0).FindPath(new Cell3(1, 1, 0), new Cell3(6, 5, 1)));
        // a person and a chest on the floor
        var man = EntityId.Make(IdKind.Person, 7_000_001);
        r.Standing.Add((man, 706, 704, 1));
        var chestId = ConstructionIds.Make(0, ConstructionIds.ChestSlot, 99);
        r.Econ.OpenStore(StoreKind.Chest, chestId).Create(r.G("iron_bar"), 5_000, CreateReason.Import);
        r.W.Book.Placed[chestId] = new PlacedStore(chestId, 705, 704, 1);
        long massBefore = TotalMass(r);
        // take the post down by hand
        var mark = r.W.MarkDeconstruct(r.Hero, PurseKind.Person, -1, 705, 705, 0, GhostSlot.Structure, t);
        Assert.Equal("cn_deconstruct_will_fall", mark.Key);
        var site = r.W.Book.Ghosts[mark.Subject].Site;
        r.W.Site(site)!.Crew.Clear();
        r.Crew(site, 1);
        long fellAt = -1, groanAt = -1;
        for (long s = t; s < t + 86400 && fellAt < 0; s += 60)
        {
            r.W.AdvanceTo(s);
            if (groanAt < 0 && r.W.Book.Groans.Count > 0) groanAt = r.W.Book.Groans[0].StartSec;
            if (r.Falls.Count > 0) fellAt = r.Falls[0].Sec;
        }
        Assert.True(groanAt > 0 && fellAt > 0);
        Assert.Equal(groanAt + 10, fellAt);                                        // groans 10 game seconds, then falls
        Assert.DoesNotContain(r.W.Book.Built.Values, b => b.Z == 1);                 // the 9 cells fell
        Assert.Equal(3000, r.Falls.Single().Mm);                                    // the person takes a 3,000 mm fall
        Assert.Equal(0, r.W.Book.Placed[chestId].Z);                                // the chest lands below
        Assert.Contains(r.Chron, c => c.Type == "collapse" && c.Sec == fellAt);
        Assert.Equal(massBefore, TotalMass(r));                                     // rubble = salvage + waste: mass exact
        r.AssertMass();
        // the stair portal is gone and the path re-plans (no way up)
        var grid1 = r.Grid(700, 700, 12, 12);
        Assert.DoesNotContain(grid1.Portals, p => p.Enabled);
        Assert.Null(new LevelPathfinder(grid1).FindPath(new Cell3(1, 1, 0), new Cell3(6, 5, 1)));
        // rooms below lose their roof (RD-11): the cell under the platform has nothing above any more
        Assert.Null(r.W.Rooms.PieceAt(705, 705, 1));
    }

    /// <summary>Mass of every store plus what stands built (grams).</summary>
    private static long TotalMass(ConstructionRig r)
    {
        long m = 0;
        foreach (var s in r.Econ.OtherStores.Values) foreach (var (_, q) in s.Rows()) m += q;
        foreach (var b in r.W.Book.Built.Values) m += r.W.Costs[b.Piece].TotalGrams;
        return m;
    }

    [Fact]
    public void BC14_AFloorAtZ2_OnAPostThatStoodOnTheFallenLevel_FallsInTheSameEvent_InZOrder()
    {
        var (r, t) = Platform(upperStorey: true);
        Assert.NotNull(r.W.BuiltAt(706, 706, 2));
        var res = r.W.Destroy(705, 705, 0, t, EntityId.None, "test");
        Assert.True(res.Ok);
        var g = Assert.Single(r.W.Book.Groans);
        var zs = g.Cells.Select(k => ConstructionIds.Unpack(k).Z).ToList();
        Assert.Contains(2, zs);
        Assert.Equal(zs.OrderBy(z => z).ToList(), zs);                             // (z, y, x) order
        r.W.AdvanceTo(g.FallSec);
        Assert.Empty(r.W.Book.Groans);
        Assert.DoesNotContain(r.W.Book.Built.Values, b => b.Z >= 1);
        Assert.Single(r.Chron, c => c.Type == "collapse");                    // one event
        r.AssertMass();
        // deterministic: the same steps again give the same book
        var (r2, t2) = Platform(upperStorey: true);
        r2.W.Destroy(705, 705, 0, t2, EntityId.None, "test");
        r2.W.AdvanceTo(r2.W.Book.Groans[0].FallSec);
        Assert.Equal(r.W.Book.ComputeHash(), r2.W.Book.ComputeHash());
    }

    // ---- BC-15: multi-storey ----

    [Fact]
    public void BC15_TwoStoreySmithyHouse_StairPortalOnlyWhenBothStairsStand_SmithyAndLodgingFound_PathReachesZ1()
    {
        var r = ConstructionRig.Make(); long t = T0;
        var pr = r.W.PlacePlan(r.Hero, PurseKind.Person, -1, "smithy_house", 800, 800, 0, false, t);
        Assert.True(pr.Ok, pr.Key + " " + string.Join(", ", pr.Refused.Select(x => $"{x.Piece}@{x.X},{x.Y},{x.Z}:{x.Key}")));
        r.StockAll(pr.Site);
        r.Crew(pr.Site, 8);
        bool sawHalfStair = false;
        for (int step = 0; step < 4000 && r.W.GhostsOf(pr.Site).Any(); step++)
        {
            t += 600; r.W.AdvanceTo(t);
            bool up = r.W.BuiltAt(803, 804, 0)?.Piece == "stairs_up", down = r.W.BuiltAt(803, 804, 1)?.Piece == "stairs_down";
            var grid = r.Grid(795, 795, 18, 16);
            bool portal = grid.Portals.Any(p => p.Enabled);
            Assert.Equal(up && down, portal);
            if (up && !down) sawHalfStair = true;
        }
        Assert.Empty(r.W.GhostsOf(pr.Site));
        Assert.True(sawHalfStair);
        var facts = r.W.Rooms.FactsAt(801, 801, 0)!;
        Assert.Contains(facts.Rooms, x => x.Primary?.Id == "workshop_smithing" && x.Z == 0);
        Assert.Contains(facts.Rooms, x => x.Primary?.Purpose == "sleeping" && x.Z == 1 && x.Indoor);
        Assert.Equal(2, facts.WorkshopStations().Values.Sum());                       // a forge and an anvil
        var g = r.Grid(795, 795, 18, 16);
        Assert.NotNull(new LevelPathfinder(g).FindPath(new Cell3(2, 7, 0), new Cell3(7, 6, 1)));   // from the street through the door, up the stair
        output.WriteLine($"smithy_house built by day {t / 86400}, {r.W.Book.Built.Count} pieces");
        r.AssertMass();
    }

    // ---- BC-20: rain ----

    [Fact]
    public void BC20_ThatchingIn5mmRain_PausesRain_TimberWallsGoOn()
    {
        var r = ConstructionRig.Make(); long t = T0;
        Build(r, [new PlaceCell(900, 900, 0, "wall_timber")], ref t);
        var pr = r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(900, 900, 1, "roof_thatch")], t);
        Assert.True(pr.Ok);
        r.StockAll(pr.Site);
        r.Wx = (50, 10);                                                             // 5 mm an hour
        var s = r.W.Site(pr.Site)!;
        if (s.Crew.Count == 0) r.Crew(s.Id, 4);
        t = (t / 86400) * 86400 + 86400 + 7 * 3600;
        r.W.AdvanceTo(t + 3600);
        Assert.Equal(0, r.W.GhostAt(900, 900, 1)!.WorkDone);
        Assert.Equal(PauseReason.Rain, s.Pause);
        // a timber wall on the same site goes on in the rain
        Assert.True(r.W.Place(r.Hero, PurseKind.Person, -1, [new PlaceCell(902, 900, 0, "wall_timber")], t + 3600, s.Id).Ok);
        r.StockAll(s.Id);
        r.W.AdvanceTo(t + 2 * 3600);
        Assert.True(r.W.GhostAt(902, 900, 0)!.WorkDone > 0);
        Assert.Equal(0, r.W.GhostAt(900, 900, 1)!.WorkDone);
        // the rain stops: the thatch goes on
        r.Wx = (0, 10);
        r.W.AdvanceTo(t + 4 * 3600);
        Assert.Null(r.W.GhostAt(900, 900, 1));
        Assert.NotNull(r.W.BuiltAt(900, 900, 1));
    }

    // ---- hovels count as dwellings (the Notes' decision) ----

    [Fact]
    public void AOneRoomCottageWithAHearthAndTwoPallets_IsADwelling()
    {
        var r = ConstructionRig.Make(); long t = T0;
        var pr = r.W.PlacePlan(r.Hero, PurseKind.Person, -1, "test_cottage", 1000, 1000, 0, false, t);
        Assert.True(pr.Ok, pr.Key);
        r.StockAll(pr.Site); r.Crew(pr.Site, 6);
        for (int d = 0; d < 10 && r.W.GhostsOf(pr.Site).Any(); d++) { t += 86400; r.W.AdvanceTo(t); }
        Assert.Empty(r.W.GhostsOf(pr.Site));
        output.WriteLine($"test_cottage: crew of 6 took to day {t / 86400 - 10} ({r.W.Book.Built.Count} pieces)");
        Assert.False(r.W.Rooms.FactsAt(1001, 1001, 0)!.IsDwelling);
        Build(r, [new PlaceCell(1002, 1002, 0, "hearth_central"), new PlaceCell(1005, 1001, 0, "straw_pallet"), new PlaceCell(1006, 1001, 0, "straw_pallet")], ref t);
        var f = r.W.Rooms.FactsAt(1001, 1001, 0)!;
        Assert.True(f.IsDwelling);
        Assert.Equal(DwellingKind.Cottage, f.DwellingKind);
        Assert.Equal(2, f.Beds);
    }
}
