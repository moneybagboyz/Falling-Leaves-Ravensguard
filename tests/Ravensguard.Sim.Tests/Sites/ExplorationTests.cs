using System.Diagnostics;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.World;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Sites;

/// <summary>Exploration and sites (docs/specs/phase4/exploration-and-sites.md, P4-H): gates EX-1 to EX-6 (generation, persistence, blueprints, loot, light).</summary>
[Collection("sites")]
public class ExplorationTests(SiteFixture fx)
{
    private static IEnumerable<SiteHistoryInfo> Kinds(ulong id)
    {
        yield return SiteFixture.Abbey(id);
        yield return SiteFixture.Of(SiteKind.Ruin, "razed", id + 1);
        yield return SiteFixture.Of(SiteKind.Cave, "den", id + 2);
        yield return SiteFixture.Of(SiteKind.Cave, "hideout", id + 3);
        yield return SiteFixture.Of(SiteKind.Cave, "river", id + 4, 3);
        yield return SiteFixture.Of(SiteKind.Mine, "iron", id + 5, 3, mine: MineState.Abandoned);
        yield return SiteFixture.Of(SiteKind.Mine, "iron", id + 6, 4, mine: MineState.Flooded);
        yield return SiteFixture.Of(SiteKind.Barrow, "barrow", id + 7, 3, true, "draugr", dead: 40);
        yield return SiteFixture.Of(SiteKind.Cave, "den", id + 8, 3, true, "cave_beast");
        yield return SiteFixture.Of(SiteKind.Mine, "iron", id + 9, 2, mine: MineState.Collapsed);
    }

    [Fact]
    public void EX1_Generation_IsAPureFunctionOfSeedSiteAndContent_TimeSlicedByLevel()
    {
        foreach (var h in Kinds(5000))
        {
            var a = SiteGen.Generate(fx.S, h, 77); var b = SiteGen.Generate(fx.S, h, 77);
            Assert.Equal(a.Hash(), b.Hash());
            Assert.Equal(SiteCodec.Encode(a), SiteCodec.Encode(b));
            Assert.NotEqual(a.Hash(), SiteGen.Generate(fx.S, h with { SiteId = h.SiteId + 100 }, 77).Hash());
            Assert.NotEqual(a.Hash(), SiteGen.Generate(fx.S, h, 78).Hash());
            Assert.Equal(fx.S.Hash, a.ContentHash);
        }
        // the job lays out one level a call (the game runs it on a worker behind the fade: the frame never waits); each call stays inside the 30 ms of a level
        var job = SiteGen.Begin(fx.S, SiteFixture.Abbey(5100), 77);
        var times = new List<double>();
        var sw = new Stopwatch();
        bool done = false;
        while (!done) { sw.Restart(); done = job.Step(); times.Add(sw.Elapsed.TotalMilliseconds); }
        Assert.Equal(3 + 2, times.Count);                 // init, three levels, the fill
        Assert.True(times.Skip(1).Max() < 60, $"slowest slice {times.Max():0.0} ms");
        Assert.Equal(SiteGen.Generate(fx.S, SiteFixture.Abbey(5100), 77).Hash(), job.Result!.Hash());
    }

    [Fact]
    public void EX2_Persistence_LootDoorsCollapsesAndLaddersSurviveSaveAndLoad_ARegeneratedSiteKeepsThem()
    {
        var book = new SiteBook();
        var abbey = book.Open(fx.S, SiteFixture.Abbey(5200), 3);
        var mine = book.Open(fx.S, SiteFixture.Of(SiteKind.Mine, "iron", 5201, 3, mine: MineState.Active), 3);
        var m = abbey.Map!;
        // loot a chest, open a door, bring a roof down
        var box = m.Containers.First(c => c.Items.Count > 0);
        var c = new SiteCrawl(m, fx.S, fx.C, new WoundLedger(1, 0), 0, 1, abbey.Known) { HeroAi = true };
        var hero = c.Add(TestMaps.Member(7));
        var (bl, bx, by) = m.At(box.Cell);
        int beside = c.Around(box.Cell).First(x => SiteTiles.Walkable(m.Tiles[x]));
        var (_, sx, sy) = m.At(beside); TestMaps.Place(hero, m, sx, sy, bl);
        c.Loot(hero, box);
        Assert.Empty(box.Items); Assert.True(hero.Loot.Count > 0);
        int door = Enumerable.Range(0, m.Cells).First(i => m.Tiles[i] == SiteTile.Door);
        c.OpenDoor(door);
        int rubble = Enumerable.Range(0, m.Cells).First(i => m.Tiles[i] == SiteTile.Floor && m.Room[i] > 0);
        m.Tiles[rubble] = SiteTile.Rubble;
        // pull a ladder up in the mine
        var mm = mine.Map!;
        var ladder = mm.Portals.First(p => p.Kind == PortalKindSite.Ladder);
        var mc = new SiteCrawl(mm, fx.S, fx.C, new WoundLedger(1, 0), 0, 1);
        var miner = mc.Add(TestMaps.Member(8));
        var (ul, ux, uy) = mm.At(ladder.UpperCell); TestMaps.Place(miner, mm, ux, uy, ul);
        mc.PullLadder(miner);
        Assert.False(ladder.Enabled);
        ulong before = book.Hash();
        var bytes = SaveCodec.SerializeCompressed(SiteSave.Capture(book));
        var back = SaveCodec.DeserializeCompressed<SiteSave>(bytes).Restore();
        Assert.Equal(before, back.Hash());
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(SiteSave.Capture(back)));          // save, load, save: byte-identical
        var m2 = back.Records[5200].Map!;
        Assert.Empty(m2.Containers[box.Id].Items); Assert.True(m2.Containers[box.Id].Opened);
        Assert.Equal(SiteTile.DoorOpen, m2.Tiles[door]); Assert.Equal(SiteTile.Rubble, m2.Tiles[rubble]);
        Assert.False(back.Records[5201].Map!.Portals[ladder.Id].Enabled);
        // a regenerated site is the snapshot, and the delta puts play's changes back on it
        var regen = SiteGen.Generate(fx.S, SiteFixture.Abbey(5200), 3);
        Assert.Equal(abbey.Base!.Hash(), regen.Hash());
        Assert.Equal(m.Hash(), SiteCodec.Apply(regen, SiteCodec.Delta(abbey.Base, m)).Hash());
        // the delta is small (3.2: 4 KB a site, 1 KB of map a level)
        Assert.True(SiteCodec.Delta(abbey.Base, m).Length < 4096 + 2048, $"delta {SiteCodec.Delta(abbey.Base, m).Length} B");
    }

    [Fact]
    public void EX3_RoomTemplates_PassTheCi_AndEveryGeneratedLevelIsConnectedFromTheEntrance()
    {
        Assert.Empty(fx.S.Validate());
        Assert.True(fx.S.Rooms.Count >= 30, $"{fx.S.Rooms.Count} room templates");
        int sites = 0;
        for (ulong s = 0; s < 120; s++)
            foreach (var h in Kinds(6000 + s * 16))
            {
                var m = SiteGen.Generate(fx.S, h, s);
                sites++;
                for (int l = 0; l < m.Levels; l++)
                {
                    var d = SiteGen.Distances(m, l, m.Arrival[l], throughRubble: true);     // a blocked entrance is dug through
                    for (int i = 0; i < m.Plane; i++)
                    {
                        var t = m.Tiles[l * m.Plane + i];
                        if (SiteTiles.Walkable(t)) Assert.True(d[i] >= 0, $"{m.Template} seed {s} level {l} cell {i % m.W},{i / m.W} unreachable");
                    }
                    foreach (var c in m.Containers.Where(c => m.At(c.Cell).Level == l))
                    {
                        int x = c.Cell % m.W, y = (c.Cell / m.W) % m.H;
                        bool touch = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(o => m.In(l, x + o.Item1, y + o.Item2) && d[(y + o.Item2) * m.W + x + o.Item1] >= 0);
                        Assert.True(touch, $"{m.Template} seed {s} container {c.Id} out of reach");
                    }
                }
                // the levels are joined: the pathfinder walks from the mouth to every level's arrival
                var g = m.BuildGrid();
                for (int i = 0; i < m.Cells; i++) if (m.Tiles[i] == SiteTile.Rubble) { var (rl, rx, ry) = m.At(i); g.SetOpen(rx, ry, SiteMap.Z(rl), true); }   // dug through
                var pf = new LevelPathfinder(g);
                var (ml, mx, my) = m.At(m.Mouth);
                for (int l = 1; l < m.Levels; l++)
                {
                    var (al, ax, ay) = m.At(m.Arrival[l]);
                    Assert.NotNull(pf.FindPath(new Cell3(mx, my, SiteMap.Z(ml)), new Cell3(ax, ay, SiteMap.Z(al))));
                }
            }
        Assert.True(sites >= 1000, $"{sites} sites");
    }

    [Fact]
    public void EX4_LootComesFromHistory_TheAbbeyBudget_TheOldBattlefield_TheMassGrave_TheBandsStash()
    {
        var rule = fx.S.Templates.First(t => t.Id == "ruin.sacked").Loot;
        Assert.Equal(1080, SiteHistoryInfo.LootBudget(rule, SiteFixture.Abbey()));          // 120 x 150 x 0.15 x 0.4
        var abbey = SiteGen.Generate(fx.S, SiteFixture.Abbey(5300), 9);
        Assert.Equal(1080, abbey.LootBudgetMites);
        Assert.InRange(abbey.LootPlacedMites, 1080 * 95 / 100, 1080);
        Assert.Equal(abbey.LootPlacedMites, abbey.LootLeftMites);
        // the event's own named piece of history
        Assert.Contains(abbey.Containers.SelectMany(c => c.Items), i => i.Named.Contains("Aldwick") && i.Named.Contains("287"));
        Assert.Equal(680, SiteHistoryInfo.BattlefieldRemainingPm(40));
        var barrow = SiteGen.Generate(fx.S, SiteFixture.Of(SiteKind.Barrow, "barrow", 5301, 1, dead: 30), 9);
        Assert.Contains(barrow.Rooms, r => r.Template == "mass_grave");
        Assert.DoesNotContain(SiteGen.Generate(fx.S, SiteFixture.Of(SiteKind.Barrow, "barrow", 5301, 1, dead: 0), 9).Rooms, r => r.Template == "mass_grave");
        // a hideout's stash is the band's recorded robberies, item for item and to the mite
        var robbed = new List<LootItem> { new("cloth_bale", "", 1100, 12000, 1000), new("silver_brooch", "", 220, 60, 1000), new("salt_barrel", "", 900, 30000, 1000) };
        var hide = SiteGen.Generate(fx.S, SiteFixture.Of(SiteKind.Cave, "hideout", 5302) with { Stash = robbed, StashCoinMites = 4321 }, 9);
        var stash = hide.Containers.Single(c => c.Kind == ContainerKind.Stash && c.Items.Any(i => i.Item == "coin"));
        Assert.Equal(robbed, stash.Items.Where(i => i.Item != "coin"));
        Assert.Equal(4321, stash.Items.Single(i => i.Item == "coin").ValueMites);
        // the named dead lie in the niches of the crypt
        Assert.InRange(abbey.Containers.Count(c => c.Dead.Length > 0), 1, 12);
    }

    [Fact]
    public void EX5_ATorchBurns1200SimSeconds_TheSameAt1xAnd8x_ALanternAnHourOn300Ml_RainBurnsFaster()
    {
        var K = fx.S.K;
        var t = CarriedLight.Torch(K); t.Lit = true;
        int steps = 0; while (!SiteLight.Burn(ref t)) steps++;
        Assert.Equal(24_000, steps + 1);                     // 1,200 sim-seconds = 24,000 steps = 20 real minutes at 1x, 20 Hz
        var l = CarriedLight.Lantern(K, 300); l.Lit = true; steps = 0; while (!SiteLight.Burn(ref l)) steps++;
        Assert.Equal(72_000, steps + 1);
        var r = CarriedLight.Torch(K); r.Lit = true; steps = 0; while (!SiteLight.Burn(ref r, SiteLight.BurnPm(Ravensguard.Sim.Weather.WeatherState.HeavyRain, LightKind.Torch, K))) steps++;
        Assert.Equal(9_600, steps + 1);                      // x2.5
        Assert.Equal(1000, SiteLight.BurnPm(Ravensguard.Sim.Weather.WeatherState.HeavyRain, LightKind.Lantern, K));
        // through the live world: 24,000 ticks at 1x, 3,000 at 8x, the same 1,200 sim-seconds of fuel
        int Ticks(SimSpeed speed)
        {
            var m = TestMaps.Corridor(20);
            var c = TestMaps.Crawl(fx, m);
            var h = c.Add(TestMaps.Member(7)); h.SpareTorches = 0; h.Light = CarriedLight.Torch(K); h.Light.Lit = true;
            var g = new LevelGrid(4, 4, 0, 0); g.FillRect(0, 0, 3, 3, 0, true);
            var w = new Ravensguard.Sim.Live.LiveWorld(g, new LightGrid(g), new Cell3(1, 1, 0), new GameTime(8 * 3600));
            w.AttachSite(c); w.SetSpeed(speed);
            int n = 0; while (h.Light.Lit && n < 30_000) { w.Step(); n++; }
            return n;
        }
        Assert.Equal(24_000, Ticks(SimSpeed.X1));
        Assert.Equal(3_000, Ticks(SimSpeed.X8));
    }

    [Fact]
    public void EX6_VisionByLight_36_16_7_1_Metres_ADarkTargetIsSeenOnlyInside56Metres()
    {
        Assert.Equal(36, LightModel.VisionRadius(235)); Assert.Equal(16, LightModel.VisionRadius(100)); Assert.Equal(7, LightModel.VisionRadius(40)); Assert.Equal(1, LightModel.VisionRadius(0));
        Assert.Equal(5588, SiteLight.VisionRadiusMm(30));
        var m = TestMaps.Corridor(30);
        var c = TestMaps.Crawl(fx, m);
        var h = c.Add(TestMaps.Member(7)); h.Light.Lit = false;
        var guard = new Inhabitant { Id = 99, Role = "scavengers", Kit = "scavengers", Hostile = true, State = InhabitantState.Home };
        m.Inhabitants.Add(guard);
        c.Step();
        bool Seen(int dx) { TestMaps.Place(h, m, 2, 2); TestMaps.Place(guard, 2 + dx, 2); return c.Notices(guard, h, null, false); }
        Assert.True(Seen(1));                                 // the hero in the dark: felt beside one
        for (int d = 6; d < 20; d++) Assert.False(Seen(d));   // and never seen beyond 5.6 m (still, silent)
        c.ToggleLight(h); c.Step();
        Assert.True(Seen(25));                                // a torchbearer is a lit target, a beacon
    }
}
