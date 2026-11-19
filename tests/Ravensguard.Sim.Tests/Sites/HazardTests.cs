using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Sites;

/// <summary>Exploration and sites (P4-H): gates EX-7 to EX-11 (bad air, flooding and swimming, cave-ins, traps, the auto-map and retracing).</summary>
[Collection("sites")]
public class HazardTests(SiteFixture fx)
{
    [Fact]
    public void EX7_Air_TheCandleGutters_TorchesDim_DazedThenUnconsciousIn20AndDeadAt120()
    {
        var K = fx.S.K;
        Assert.Equal(700, SiteLight.Air(750, K).CandleRadiusPm); Assert.True(SiteLight.Air(750, K).Warning); Assert.False(SiteLight.Air(850, K).Warning);
        var a5 = SiteLight.Air(500, K); Assert.Equal(500, a5.TorchRadiusPm); Assert.Equal(600, a5.RegenPm); Assert.Equal(-10, a5.AccuracyPoints);
        var a3 = SiteLight.Air(300, K); Assert.True(a3.Dazed); Assert.Equal(700, a3.SpeedPm); Assert.True(a3.FlameOut); Assert.False(a3.Foul);
        Assert.True(SiteLight.Air(200, K).Foul);
        // a guttering candle casts 0.7 of its radius
        var cd = CarriedLight.Candle(K); cd.Lit = true;
        Assert.Equal(3, SiteLight.Cast(cd, K, SiteLight.Air(900, K)).Radius); Assert.Equal(2, SiteLight.Cast(cd, K, SiteLight.Air(700, K)).Radius);
        // standing in air 200: unconscious after 20 sim-seconds, dead at 120
        var m = TestMaps.Corridor(10);
        for (int i = 0; i < m.Cells; i++) m.Air[i] = 200;
        var c = TestMaps.Crawl(fx, m);
        var h = c.Add(TestMaps.Member(7));
        int unconsciousAt = -1, deadAt = -1;
        for (int s = 1; s <= 130 * 20 && deadAt < 0; s++)
        {
            c.Step();
            if (h.Unconscious && unconsciousAt < 0) unconsciousAt = s / 20;
            if (h.Dead) deadAt = s / 20;
        }
        Assert.Equal(20, unconsciousAt); Assert.Equal(120, deadAt);
        Assert.Equal("Suffocation", h.DeathCause);
    }

    [Fact]
    public void EX8_Flooding_4PercentADayAfterStorms_30cmAnHourForSixHours_AnUnskilledSwimmerIn18kgDrownsIn35s()
    {
        var K = fx.S.K;
        int floods = 0, dry = 0;
        for (ulong s = 0; s < 10_000; s++) { if (SiteBook.FloodRolls(s, 40, 4401, 5, K)) floods++; if (SiteBook.FloodRolls(s, 40, 4401, 3, K)) dry++; }
        Assert.InRange(floods / 100.0, 3.5, 4.5); Assert.Equal(0, dry);
        // the book counts the heavy hours of the last day
        var book = new SiteBook();
        for (long h = 0; h < 30; h++) book.NoteHour(h, h is >= 20 and < 25);
        Assert.Equal(5, book.HeavyOfLastDay);
        // the rise: 30 cm an hour for six hours on the lowest level
        var m = TestMaps.Corridor(10);
        var c = TestMaps.Crawl(fx, m, sec: 3600 * 10);
        c.Add(TestMaps.Member(7));
        c.FloodSince = c.GameSec;
        var seen = new List<int>();
        for (int hour = 1; hour <= 8; hour++) { for (int s = 0; s < 3600; s++) c.Step(); seen.Add(m.FloodCm[0]); }
        Assert.Equal(new[] { 30, 60, 90, 120, 150, 180, 180, 180 }, seen);
        // swimming: an unskilled swimmer in 18 kg of gear sinks and drowns
        long total = 0; int n = 0;
        for (ulong seed = 1; seed <= 400; seed++)
        {
            var pool = TestMaps.Level(["#####", "#<~~#", "#~~~#", "#####"], waterCm: 200, id: 990 + seed);
            var pc = TestMaps.Crawl(fx, pool, seed: seed);
            var sw = pc.Add(TestMaps.Member(7)); sw.Athletics = 0; sw.GearG = 18_000; sw.Worn = [];
            TestMaps.Place(sw, pool, 2, 2);
            int s = 0;
            while (!sw.Dead && s < 20 * 200) { pc.Step(); s++; }
            Assert.True(sw.Dead); Assert.Equal("Drowned", sw.DeathCause);
            total += s; n++;
        }
        double mean = total / (double)n / 20;
        Assert.InRange(mean, 30, 40);
    }

    [Fact]
    public void EX9_CaveIns_5PercentAMinuteAt1000pm_ASixSecondWarning_TheRubbleBlowAndTheDodge()
    {
        int falls = 0;
        for (long t = 0; t < 10_000; t++) if (SiteCrawl.CaveInRolls(1, t, 4401, 0, 1000, 1000)) falls++;
        Assert.InRange(falls / 100.0, 4.5, 5.5);
        int loud = 0; for (long t = 0; t < 10_000; t++) if (SiteCrawl.CaveInRolls(1, t, 4401, 0, 1000, 3000)) loud++;
        Assert.InRange(loud / 100.0, 13.5, 16.5);
        Assert.Equal(150_000, fx.S.K.CaveInBlowMilli); Assert.Equal(800, fx.S.K.CaveInDodgePm); Assert.Equal(6, fx.S.K.CaveInWarnS);
        // in a crawl: the creak, six sim-seconds, the roof; rubble stays; free people dodge four times in five
        int dodged = 0, hit = 0, creakToFall = -1;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var m = TestMaps.Corridor(12, id: 3000 + seed);
            m.Unstable.Add(new UnstableZone { Id = 0, Level = 0, X0 = 1, Y0 = 1, X1 = 12, Y1 = 3, InstabilityPm = 20_000 });   // certain to fall at the first window
            var c = TestMaps.Crawl(fx, m, sec: 0, seed: seed);
            var a = c.Add(TestMaps.Member(7)); TestMaps.Place(a, m, 6, 2);
            long creak = -1, fall = -1;
            for (int s = 0; s < 20 * 80 && fall < 0; s++)
            {
                c.Step();
                foreach (var e in c.TakeEvents()) { if (e.Kind == CrawlEventKind.CaveInCreak && creak < 0) creak = c.GameSec; if (e.Kind == CrawlEventKind.CaveIn) fall = c.GameSec; }
            }
            Assert.True(fall > 0);
            if (creak > 0) creakToFall = (int)((fall - creak) / 20);
            if (a.Book.Records.Count > 0) hit++; else dodged++;
            Assert.Contains(m.Tiles, t => t == SiteTile.Rubble);
        }
        Assert.Equal(6, creakToFall);
        Assert.InRange(dodged * 1000 / (dodged + hit), 740, 860);
    }

    [Fact]
    public void EX10_Traps_DetectionAndDisarmOdds_SpringSpearsWoundAGambesonWearerAtTheChest()
    {
        Assert.Equal(200, SiteTraps.DetectPm(50, 40, 235, false));         // 5 + 5 + 10 = 20% in torchlight
        Assert.Equal(20, SiteTraps.DetectPm(50, 40, 10, false));           // 5 + 5 - 15 = -5: the 2% floor unlit
        Assert.Equal(300, SiteTraps.DetectPm(50, 40, 235, true));          // x1.5 sneaking
        Assert.Equal(750, SiteTraps.DisarmPm(50, 40));
        // in the crawl the roll runs once a sim-second while one is within 2.5 m and facing it
        var m = TestMaps.Corridor(12);
        m.Traps.Add(new SiteTrap { Id = 0, Def = "deadfall", Cell = m.Index(0, 6, 2) });
        int found = 0, tries = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            m.Traps[0].Detected = false; m.Traps[0].Armed = true;
            var c = TestMaps.Crawl(fx, m, seed: seed);
            var h = c.Add(TestMaps.Member(7)); h.Perception = 50; c.ToggleLight(h);
            TestMaps.Place(h, m, 4, 2); h.FX = 1024; h.FY = 0;
            c.Step();
            for (int s = 0; s < 20; s++) c.Step();                               // one sim-second
            tries++; if (m.Traps[0].Detected) found++;
        }
        Assert.InRange(found * 1000 / tries, 120, 290);
        // spring spears against a gambeson: the resolver's wounds at the chest (upper body) or the thigh
        var blows = new SiteBlows(fx.C);
        var plan = fx.C.T.Plan("human");
        var kit = new FighterKit { Worn = [new("shirt", "linen"), new("gambeson", "linen")] };
        var worn = blows.Worn(kit);
        var spears = fx.S.Traps["spring_spears"];
        int chest = plan.IndexOf("upper_body"), wounded = 0;
        for (uint k = 0; k < 60; k++)
        {
            var book = new WoundBook(1, k + 1, plan.BloodMl);
            int n = blows.Blow(book, plan, worn, 70000, spears.Attack, spears.MomentumMilli, spears.AreaMm2, spears.PenMm, chest, new BlowKey(1, k, 7, k), 100, 1);
            if (n > 0) { wounded++; Assert.Contains(book.Records, w => w.Part == chest || plan.Parts[w.Part].Host == chest); }
        }
        Assert.True(wounded > 30, $"{wounded} of 60");
    }

    [Fact]
    public void EX11_TheAutoMap_IsWhatWasSeen_MergesWithin20m_StaysSmall_AndRetraceWalksTheWayBack()
    {
        var m = SiteGen.Generate(fx.S, SiteFixture.Abbey(7100), 5);
        var c = TestMaps.Crawl(fx, m);
        var h = c.Add(TestMaps.Member(7)); c.ToggleLight(h);
        int goal = m.Arrival[m.Levels - 1];
        Assert.True(c.GoTo(h, goal));
        var visited = new HashSet<int>();
        for (int s = 0; s < 20 * 900 && h.Cell != goal; s++)
        {
            c.Step(); visited.Add(h.Cell);
            if (c.Fight == null && h.Path == null && h.CanAct) c.GoTo(h, goal);
            foreach (var e in c.TakeEvents()) { }
        }
        Assert.Equal(goal, h.Cell);
        // every cell walked is known and so is every cell beside it; nothing is known that was never in reach of a vision radius of the walk
        foreach (int v in visited) { Assert.True(c.Known.Known(v)); foreach (int n in c.Around(v)) Assert.True(c.Known.Known(n)); }
        var crumbs = h.Crumbs.ToArray();
        for (int i = 0; i < m.Cells; i++)
        {
            if (!c.Known.Known(i)) continue;
            var (l, x, y) = m.At(i);
            Assert.Contains(visited, v => { var (vl, vx, vy) = m.At(v); return vl == l && (vx - x) * (vx - x) + (vy - y) * (vy - y) <= 37 * 37; });
        }
        // saved small: about a kilobyte a level
        Assert.True(c.Known.ToRle().Length < 1024 * m.Levels, $"{c.Known.ToRle().Length} B");
        var back = AutoMap.FromRle(m.W, m.H, m.Levels, c.Known.ToRle());
        Assert.Equal(c.Known.Count, back.Count);
        // two parties within 20 m share their maps
        var other = new AutoMap(m.W, m.H, m.Levels); other.Mark(5); other.Mark(m.Cells - 1);
        int before = c.Known.Count; c.Known.Merge(other);
        Assert.True(c.Known.Known(5) && c.Known.Known(m.Cells - 1) && c.Known.Count >= before);
        // Retrace steps: back to the mouth along the walked cells only
        h.Retracing = false; c.Retrace(h);
        Assert.True(h.Retracing);
        var path = new List<int>(h.Path!);
        Assert.All(path, p => Assert.Contains(p, crumbs));
        for (int s = 0; s < 20 * 900 && h.Cell != m.Mouth; s++) { c.Step(); if (c.Fight != null) break; }
        if (c.Fight == null) Assert.Equal(m.Mouth, h.Cell);
    }
}
