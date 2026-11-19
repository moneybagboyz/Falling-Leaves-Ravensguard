using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Morale;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Sites;

/// <summary>Exploration and sites (P4-H): the crypt crawl EX-12 (the Phase 4 done-when), the monsters EX-13 and EX-14, reoccupation EX-15, weather and light in combat EX-16, EX-17, bad air EX-18, and the crawl's save.</summary>
[Collection("sites")]
public class CrawlTests(SiteFixture fx)
{
    private const ulong CryptSeed = 59;

    private (SiteCrawl C, CrawlMember Hero, SiteMap M) Crypt()
    {
        var m = SiteGen.Generate(fx.S, SiteFixture.Abbey(4401), CryptSeed);
        ExFixtures.OneGuard(fx, m);
        var c = TestMaps.Crawl(fx, m, sec: 9 * 3600, seed: CryptSeed);
        var h = c.Add(ExFixtures.Hero()); c.ToggleLight(h);
        c.Surgeon = new SurgeonInfo("Aldwick Market", 15_552);
        return (c, h, m);
    }

    private static bool Injured(CrawlMember h, out string what, out long born)
    {
        var bs = h.Book.Function(h.Plan); what = ""; born = -1;
        foreach (var w in h.Book.Records)
        {
            var p = h.Plan.Parts[w.Part];
            if (bs.Parts[w.Part].TendonCut && w.Structure >= 0 && p.Structures[w.Structure].Kind == StructureKind.Tendon) { what = "tendon " + p.Id; born = w.Born; return true; }
            if (w.Cat == WoundCat.Bone && w.Grade >= (byte)BoneGrade.Broken && p.Limb == "leg") { what = "broken " + p.Id; born = w.Born; return true; }
        }
        return false;
    }

    [Fact]
    public void EX12_ACryptCrawlByTorchlight_EndsWithAnInjuryFarFromASurgeonThatMatters()
    {
        var (c, h, m) = Crypt();
        Assert.Equal(3, m.Levels);
        Assert.DoesNotContain(m.Inhabitants, n => n.Monster == "draugr");
        Assert.NotEmpty(m.Traps);
        int goal = ExFixtures.CryptCell(m);
        c.GoTo(h, goal);
        int traps = 0, fights = 0; string what = ""; long born = -1;
        for (int s = 0; s < 20 * 1200 && !h.Dead; s++)
        {
            c.Step();
            foreach (var e in c.TakeEvents()) { if (e.Kind == CrawlEventKind.TrapTriggered) traps++; if (e.Kind == CrawlEventKind.FightStarted) fights++; }
            if (Injured(h, out what, out born)) break;
            if (c.Fight == null && h.Path == null && h.CanAct && h.Cell != goal) c.GoTo(h, goal);
        }
        Assert.True(what.Length > 0, "no tendon or broken leg");
        Assert.StartsWith("tendon", what);
        Assert.True(traps >= 1, "the crypt's trap went off on the way");
        Assert.Equal(1, fights);
        while (c.Fight != null) c.Step();
        Assert.False(h.Dead);
        // the surgeon: 1.2 game days on foot, 0.7 by horse (the HUD's line)
        Assert.Equal(120, c.Surgeon.DaysX100Foot); Assert.Equal(72, c.Surgeon.DaysX100Horse);
        // getting out is minutes; the trip to the surgeon is the cost
        var (exitM, exitS) = c.ExitTime(h);
        Assert.InRange(exitM, 20, 400); Assert.InRange(exitS, 10, 20 * 60);
        long leave = c.GameSec + exitS * 20L;
        Assert.True(c.Surgeon.InTime(born, 86_400, leave, horse: true), "a ride makes it");
        Assert.False(c.Surgeon.InTime(born, 86_400, leave, horse: false), "a walk loses the leg");
        // a tendon repair after a game day fails; inside it a surgeon can try
        var late = Treatment.Perform(h.Book, h.Plan, new TreatRequest(TreatKind.TendonRepair, -1, born + 86_400 + 60, 90, 9));
        Assert.False(late.Ok);
        var inTime = Treatment.Perform(h.Book, h.Plan, new TreatRequest(TreatKind.TendonRepair, -1, leave + c.Surgeon.GameSecAt(SurgeonInfo.HorseMmps), 90, 9));
        Assert.True(inTime.Ok);
        // the same seed, the same crawl
        var (c2, h2, _) = Crypt(); c2.GoTo(h2, goal);
        for (int s = 0; s < 20 * 1200 && !Injured(h2, out _, out _); s++) { c2.Step(); if (c2.Fight == null && h2.Path == null && h2.CanAct && h2.Cell != goal) c2.GoTo(h2, goal); }
        Assert.Equal(born, Injured(h2, out _, out long b2) ? b2 : -1);
    }

    [Fact]
    public void EX13_TheDraugr_WakesWhenItsGraveGoodsGo_StopsAt100mFromTheMouth_ReturnsToItsBier_NeverRoutes_BurnsIn30s()
    {
        var d = fx.S.Monsters["draugr"];
        // (a) the grave good taken: a 10% chance a sim-second to wake, a mean of 10 sim-seconds
        long total = 0; int n = 0;
        for (ulong seed = 1; seed <= 600; seed++)
        {
            var m = SiteGen.Generate(fx.S, SiteFixture.Of(SiteKind.Barrow, "barrow", 7300, 2, true, "draugr"), seed);
            var dr = m.Inhabitants.First(x => x.Monster == "draugr");
            Assert.Equal(InhabitantState.Dormant, dr.State);
            var c = TestMaps.Crawl(fx, m, seed: seed);
            var h = c.Add(TestMaps.Member(7));
            var grave = m.Containers.First(x => x.Room == m.Room[dr.Home] && x.Items.Count > 0);
            for (int s = 0; s < 20 * 5; s++) c.Step();
            Assert.Equal(InhabitantState.Dormant, dr.State);            // nothing yet: it sleeps
            c.Loot(h, grave);
            int t = 0;
            while (dr.State == InhabitantState.Dormant && t < 20 * 200) { c.Step(); t++; }
            Assert.NotEqual(InhabitantState.Dormant, dr.State);
            total += t; n++;
        }
        Assert.InRange(total / (double)n / 20, 8.5, 12);
        // (b, c) the leash: it follows a living target out to 100 m from the mouth, no further, and goes home to its bier
        var cor = TestMaps.Corridor(160);
        var cc = TestMaps.Crawl(fx, cor);
        var bait = cc.Add(TestMaps.Member(7)); TestMaps.Place(bait, cor, 30, 2);
        var dg = new Inhabitant { Id = 77, Role = "draugr", Kit = "draugr", Monster = "draugr", Name = "Ketil Ravenfoot", Hostile = true, State = InhabitantState.Hunt, Home = cor.Index(0, 20, 2) };
        TestMaps.Place(dg, 20, 2); cor.Inhabitants.Add(dg);
        int maxX = 0; bool returned = false;
        for (int s = 0; s < 20 * 300; s++)
        {
            // the bait keeps 9 m ahead (out of the fight's 4 m, inside the 60 m it hunts by)
            if (s % 20 == 0 && bait.X / 1000 < 150) { int bx = Math.Min(155, dg.X / 1000 + 9); TestMaps.Place(bait, cor, bx, 2); }
            cc.Step();
            maxX = Math.Max(maxX, dg.X / 1000);
            if (dg.State == InhabitantState.Return) returned = true;
            if (returned && dg.State == InhabitantState.Dormant) break;
        }
        Assert.True(returned);
        Assert.InRange(maxX, 95, 103);
        Assert.Equal(InhabitantState.Dormant, dg.State);
        Assert.Equal(cor.Index(0, 20, 2), cc.CellOf(dg));
        // (c) no living target within 60 m for 120 sim-seconds: back to the bier
        var cor2 = TestMaps.Corridor(160, id: 902);
        var c3 = TestMaps.Crawl(fx, cor2);
        var far = c3.Add(TestMaps.Member(8)); TestMaps.Place(far, cor2, 150, 2);
        var dg2 = new Inhabitant { Id = 78, Role = "draugr", Kit = "draugr", Monster = "draugr", Hostile = true, State = InhabitantState.Hunt, Home = cor2.Index(0, 10, 2) };
        TestMaps.Place(dg2, 12, 2); cor2.Inhabitants.Add(dg2);
        int back = -1;
        for (int s = 0; s < 20 * 200 && back < 0; s++) { c3.Step(); if (dg2.State != InhabitantState.Hunt) back = (s + 1) / 20; }
        Assert.Equal(d.ReturnAfterS, back);
        // (d) never routs, never yields: fights against two draugr
        int fled = 0;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var hero = TestMaps.Member(7, skill: 55, worn: FightCalibration.Mailed.Worn).Kit with { Shield = "heater" };
            var def = FightSetup.KitDef(fx.C, "draugr", "draugr");
            var a = FightSetup.Roll(fx.C, def, seed, 501, "Draugr", 1, 2500, 0, -1000, 0);
            var b = FightSetup.Roll(fx.C, def, seed, 502, "Draugr 2", 1, 2500, 1500, -1000, 0);
            var f = FightSetup.SiteInhabitants([hero], [a, b], SideKind.Draugr, seed, 7300, 0, 9 * 3600, 0).Start(fx.C, new WoundLedger(seed, 9 * 3600));
            f.Fighters[0].Player = false;
            for (int t = 0; t < 20 * 120 && !f.IsOver; t++) f.StepWithAi();
            fled += f.Fighters.Count(x => x.Plan.Id == "draugr" && x.Act is FAct.Fled or FAct.Surrendered);
            Assert.All(f.Fighters.Where(x => x.Plan.Id == "draugr"), x => Assert.Equal(0, x.Book.PainX100));
        }
        Assert.Equal(0, fled);
        // (e) fire: a burning head undoes it in 30 +- 5 sim-seconds
        var fw = new FightWorld(fx.C, 1, 1, 9 * 3600);
        var dk = FightSetup.Roll(fx.C, FightSetup.KitDef(fx.C, "draugr", "draugr"), 1, 600, "Draugr", 1, 0, 0, 1000, 0);
        var df = fw.Add(dk);
        fw.Add(new FighterKit { Name = "Torchbearer", Team = 0, X = 30_000, Weapon = new WeaponInstance("dagger", "iron"), Stance = "defensive" });
        Assert.True(fw.Ignite(df.Id, df.Plan.IndexOf("head")));
        int ticks = 0; while (df.Act != FAct.Dead && ticks < 20 * 60) { fw.Step(); ticks++; }
        Assert.Equal(FAct.Dead, df.Act);
        Assert.InRange(ticks / 20, 25, 35);
    }

    [Fact]
    public void EX14_TheCaveBeast_Sleeps16Of24Hours_WakesToARunningPartyAt25m_FleesAt30PercentBlood_IgnoresASneakingPartyBeyond8m()
    {
        var d = fx.S.Monsters["cave_beast"];
        Assert.Equal(16, Enumerable.Range(0, 24).Count(h => SiteCrawl.AsleepAt(d, h * 3600 + 1800)));
        var cor = TestMaps.Corridor(60);
        var c = TestMaps.Crawl(fx, cor, sec: 12 * 3600);
        var m = c.Add(TestMaps.Member(7)); m.Stealth = 30;
        var beast = new Inhabitant { Id = 90, Role = "cave_beast", Kit = "cave_beast", Monster = "cave_beast", Hostile = true, State = InhabitantState.Asleep, Home = cor.Index(0, 50, 2) };
        TestMaps.Place(beast, 50, 2); cor.Inhabitants.Add(beast);
        bool Hears(int dist, CrawlMode mode, bool sneak) { TestMaps.Place(m, cor, 50 - dist, 2); m.MoveDir = 0; m.Mode = mode; m.Sneak = sneak; return c.Notices(beast, m, d, asleep: true); }
        Assert.True(Hears(24, CrawlMode.Sprint, false)); Assert.False(Hears(26, CrawlMode.Sprint, false));
        Assert.True(Hears(14, CrawlMode.Jog, false)); Assert.False(Hears(16, CrawlMode.Jog, false));
        // a sneaking party beyond 8 m is never heard; inside it, at 1% a sim-second per 10 Stealth below 50 (Stealth 30: 2%)
        for (int s = 0; s < 10_000; s++) { TestMaps.Place(m, cor, 41, 2); m.MoveDir = 0; m.Sneak = true; Assert.False(c.Notices(beast, m, d, true)); }
        int heard = 0;
        for (ulong seed = 1; seed <= 10_000; seed++)
        {
            var cs = new SiteCrawl(cor, fx.S, fx.C, new WoundLedger(seed, 0), 12 * 3600, seed);
            var ms = cs.Add(TestMaps.Member(7)); ms.Stealth = 30; TestMaps.Place(ms, cor, 44, 2); ms.MoveDir = 0; ms.Sneak = true;
            if (cs.Notices(beast, ms, d, true)) heard++;
        }
        Assert.InRange(heard / 100.0, 1.5, 2.5);
        cor.Inhabitants.Clear();
        // flees below 30% blood: the fight's morale for the cave beast's plan
        Assert.Equal(300, fx.C.T.Plan("cave_beast").FleeBelowBloodPm);
        var w = new FightWorld(fx.C, 3, 1, 9 * 3600);
        var bk = FightSetup.Roll(fx.C, FightSetup.KitDef(fx.C, "cave_beast", "cave_beast"), 3, 700, "Beast", 1, 0, 0, 1000, 0);
        var bf = w.Add(bk);
        w.Add(new FighterKit { Name = "Hunter", Team = 0, X = 20_000, Weapon = new WeaponInstance("spear", "iron"), Stance = "defensive" });
        bf.Book.BloodUl = bf.Book.BloodMaxUl * 76 / 100;                  // it breaks before it collapses (the morale table: 20% lost; the collapse comes at 30%)
        for (int t = 0; t < 20 * 10 && bf.Act != FAct.Fled; t++) w.StepWithAi();
        Assert.Equal(FAct.Fled, bf.Act);
    }

    [Fact]
    public void EX15_Reoccupation_2PercentADayAfterDay20AtPressure500_AChronicleLine_ANewBandOfNewPeople()
    {
        var K = fx.S.K;
        int hits = 0; for (int day = 0; day < 10_000; day++) if (SiteBook.ReoccupyRolls(9, day, 4401, 500, K)) hits++;
        Assert.InRange(hits / 100.0, 1.6, 2.4);
        int half = 0; for (int day = 0; day < 10_000; day++) if (SiteBook.ReoccupyRolls(9, day, 4401, 250, K)) half++;
        Assert.InRange(half / 100.0, 0.7, 1.3);
        // a cleared den, its wolves dead: nothing for 20 days, then a band moves in
        var region = new Ravensguard.Sim.WorldGen.Region.Region(); var site = new Site { Id = new EntityId(4401), Kind = SiteKind.Cave, SubKind = "den", Name = "Beast cave 3" };
        region.Sites.Add(site);
        var book = new SiteBook();
        var r = book.Open(fx.S, SiteFixture.Of(SiteKind.Cave, "den", 4401), 9);
        var old = r.Map!.Inhabitants.Select(x => x.Id).ToHashSet();
        foreach (var x in r.Map.Inhabitants) x.State = InhabitantState.Dead;
        r.Cleared = 100;
        var lines = new List<(string Type, string Key)>();
        int movedIn = -1;
        for (int day = 100; day < 1100 && movedIn < 0; day++)
        {
            SiteSystem.Daily(book, fx.S, region, 9, day, (t, s, note, data) => lines.Add((t, data.First(d => d.Item1 == "text_key").Item2)));
            if (r.Reoccupied > 0) movedIn = day;
        }
        Assert.True(movedIn >= 120, $"moved in on day {movedIn}");
        Assert.Contains(lines, l => l.Type == "site_reoccupied" && l.Key == "ex_chron_reoccupied");
        var band = r.Map.Inhabitants.Where(x => x.State != InhabitantState.Dead).ToList();
        Assert.InRange(band.Count, 3, 8);
        Assert.Equal(band.Count, band.Select(x => x.Id).Distinct().Count());
        Assert.DoesNotContain(band, x => old.Contains(x.Id));
        Assert.All(band, x => Assert.NotEmpty(x.Name));
    }

    [Fact]
    public void EX16_WeatherInCombat_TheTableAsContent_AndTheRangedSigma()
    {
        var w = fx.S.Weather;
        Assert.Equal(1300, w["HeavyRain"].SigmaPm); Assert.Equal(60, w["HeavyRain"].VisionCapM); Assert.Equal(2500, w["HeavyRain"].TorchBurnPm);
        Assert.Equal(1400, w["Fog"].SigmaPm); Assert.Equal(15, w["Fog"].SigmaBeyondM); Assert.Equal(15, w["Fog"].VisionCapM);
        Assert.Equal(850, w["Heatwave"].HeavyRegenPm); Assert.Equal(10, w["Heatwave"].HeavyArmourKg);
        Assert.Equal(600, w["Snow"].CavalrySpeedPm); Assert.Equal(800, w["Snow"].SpeedPm);
        Assert.Equal(1150, w["LightRain"].SigmaPm); Assert.Equal(1500, w["Blizzard"].SigmaPm);
        Assert.Equal(Enum.GetValues<WeatherState>().Length, w.Count);
        var f = new FightWorld(fx.C, 1);
        Assert.Equal(1000, f.WeatherSigmaPm(40_000));
        f.WeatherRow = w["HeavyRain"]; Assert.Equal(1300, f.WeatherSigmaPm(40_000));
        f.WeatherRow = w["Fog"]; Assert.Equal(1000, f.WeatherSigmaPm(10_000)); Assert.Equal(1400, f.WeatherSigmaPm(20_000));
    }

    [Fact]
    public void EX17_LightModifiesTheHit_40LightIsMinus15Points_OnTheHeadAtEqualSkill()
    {
        Assert.Equal(0, SiteLight.LightPoints(235)); Assert.Equal(-5, SiteLight.LightPoints(100)); Assert.Equal(-15, SiteLight.LightPoints(40)); Assert.Equal(-30, SiteLight.LightPoints(10));
        var plan = fx.C.T.Plan("human");
        var r = new ResolverV2(fx.C.T);
        var s = StrikeV2.Create(fx.C.T.Weapon("arming_sword", "steel"), 0, plan, plan.IndexOf("head"), 50); s.DefenderSkill = 50;
        int lit = r.HitChanceBp(s);
        s.SituationPoints = SiteLight.LightPoints(40);
        int dark = r.HitChanceBp(s);
        Assert.InRange(lit, 2150, 2250);                                   // 22.0%
        Assert.InRange(dark, lit * 425 / 500 - 1, lit * 425 / 500 + 1);                            // x (500 - 5 x 15) / 500 in the resolver's own formula (Notes: the spec's 7.0% does not follow from it)
        // in a site fight the target's light goes on every blow as situational points; the draugr sees in the dark
        var w = new FightWorld(fx.C, 5, 1, 9 * 3600) { SiteLight = (x, y) => 40 };
        var a = w.Add(new FighterKit { Name = "A", Team = 0, Weapon = new WeaponInstance("arming_sword", "steel"), Skill = 50 });
        var b = w.Add(new FighterKit { Name = "B", Team = 1, X = 1500, Weapon = new WeaponInstance("arming_sword", "steel"), Skill = 50, FaceX = -1000 });
        var points = new List<int>();
        w.OnBlow = (att, _, _, st) => points.Add(st.SituationPoints);
        for (int t = 0; t < 20 * 20 && !w.IsOver; t++) w.StepWithAi();
        Assert.NotEmpty(points); Assert.All(points, p => Assert.Equal(-15, p));
        Assert.True(FightWorld.SeesInDark(new FightWorld(fx.C, 1).Add(FightSetup.Roll(fx.C, FightSetup.KitDef(fx.C, "draugr", "draugr"), 1, 9, "D", 1, 0, 0, 1000, 0))));
    }

    [Fact]
    public void EX18_BadAir_TheGutteringCandleComesAtLeast30SimSecondsBeforeTheFoulAir_AndWhoTurnsBackLives()
    {
        int mines = 0, cores = 0;
        for (ulong s = 1; s <= 150; s++)
            foreach (var st in new[] { MineState.Abandoned, MineState.Active, MineState.Flooded })
            {
                var m = SiteGen.Generate(fx.S, SiteFixture.Of(SiteKind.Mine, "iron", 8000 + s, 3, mine: st), s);
                mines++;
                for (int l = 0; l < m.Levels; l++)
                {
                    // from every walkable cell where the warning starts (air under 800 beside air of 800 or more) to the nearest foul cell, walking
                    var foul = Enumerable.Range(0, m.Plane).Where(i => SiteTiles.Walkable(m.Tiles[l * m.Plane + i]) && m.Air[l * m.Plane + i] < 250).ToList();
                    if (foul.Count == 0) continue;
                    cores++;
                    var d = new int[m.Plane]; Array.Fill(d, -1); var q = new Queue<int>();
                    foreach (int f in foul) { d[f] = 0; q.Enqueue(f); }
                    while (q.Count > 0)
                    {
                        int c = q.Dequeue(); int x = c % m.W, y = c / m.W;
                        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int nx = x + dx, ny = y + dy; if ((uint)nx >= m.W || (uint)ny >= m.H) continue;
                            int j = ny * m.W + nx; if (d[j] >= 0 || !SiteTiles.Walkable(m.Tiles[l * m.Plane + j])) continue;
                            d[j] = d[c] + 1; q.Enqueue(j);
                        }
                    }
                    for (int i = 0; i < m.Plane; i++)
                    {
                        if (d[i] < 0 || m.Air[l * m.Plane + i] < 800) continue;
                        // a cell before the warning: walking at 3 m/s from here, the foul air is 10 sim-seconds or more away, then 20 to fall unconscious
                        Assert.True(d[i] / 3 + 20 >= 30, $"seed {s} {st} level {l}: warning only {d[i]} cells before the foul air");
                    }
                }
            }
        Assert.True(cores >= 20, $"{cores} foul cores in {mines} mines");
        // a miner walks in at 3 m/s; at the warning he turns round within 100 sim-seconds' walk of light: he lives
        var cor = TestMaps.Corridor(80);
        for (int x = 0; x < cor.W; x++) for (int y = 0; y < cor.H; y++) cor.Air[cor.Index(0, x, y)] = (ushort)Math.Min(1000, Math.Max(0, 100 + 18 * (75 - x)));
        var c2 = TestMaps.Crawl(fx, cor);
        var walker = c2.Add(TestMaps.Member(7));
        walker.Mode = CrawlMode.Jog; walker.MoveDir = 0;
        int warnedAt = -1;
        for (int s = 0; s < 20 * 300 && !walker.Dead; s++)
        {
            c2.Step();
            foreach (var e in c2.TakeEvents()) if (e.Kind == CrawlEventKind.AirWarning && warnedAt < 0) { warnedAt = s; walker.MoveDir = 4; }
        }
        Assert.True(warnedAt > 0);
        Assert.False(walker.Dead); Assert.False(walker.Unconscious);
    }

    [Fact]
    public void TheCrawl_SavedAndLoaded_ContinuesAsAnUninterruptedRun_AndSavesByteIdentical()
    {
        SiteCrawl Make(out SiteBook book, out SiteRecord rec)
        {
            book = new SiteBook();
            rec = book.Open(fx.S, SiteFixture.Abbey(4401), CryptSeed);
            ExFixtures.OneGuard(fx, rec.Map!);
            var c = new SiteCrawl(rec.Map!, fx.S, fx.C, new WoundLedger(CryptSeed, 9 * 3600), 9 * 3600, CryptSeed, rec.Known) { HeroAi = true };
            var h = c.Add(ExFixtures.Hero()); c.ToggleLight(h);
            c.GoTo(h, ExFixtures.CryptCell(rec.Map!));
            return c;
        }
        var a = Make(out var bookA, out _);
        for (int s = 0; s < 20 * 40; s++) a.Step();
        var save = SiteSave.Capture(bookA, a);
        var bytes = SaveCodec.SerializeCompressed(save);
        // load: the book from its bytes, the crawl on the loaded map; the wounds come back from the ledger (saved by the wound section in a real save)
        var back = SaveCodec.DeserializeCompressed<SiteSave>(bytes).Restore();
        var ledger = new WoundLedger(CryptSeed, a.GameSec);
        foreach (var (k, b) in a.Ledger.Books) ledger.Books[k] = WoundSave.Capture(a.Ledger).Restore().Books[k];
        var b2 = SiteCrawl.Restore(back.ActiveCrawl!, back.Records[4401], fx.S, fx.C, ledger, CryptSeed);
        Assert.Equal(a.Hash(), b2.Hash());
        Assert.Equal(bytes, SaveCodec.SerializeCompressed(SiteSave.Capture(back, b2)));
        for (int s = 0; s < 20 * 60; s++)
        {
            a.Step(); b2.Step();
            if (a.Fight == null && a.Hero.Path == null && a.Hero.CanAct) { a.GoTo(a.Hero, ExFixtures.CryptCell(a.Map)); b2.GoTo(b2.Hero, ExFixtures.CryptCell(b2.Map)); }
            Assert.Equal(a.Hash(), b2.Hash());
        }
    }
}
