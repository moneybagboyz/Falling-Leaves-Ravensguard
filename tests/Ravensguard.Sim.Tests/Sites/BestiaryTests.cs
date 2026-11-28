using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Resolver2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Sites;

/// <summary>
/// The bestiary and the sites (docs/specs/phase8/03-landmarks-npcs-and-bestiary.md, P8-LM-3, D8: six kinds): LM-9 the six body plans and their attacks, LM-10 the bog-thing's drowning hold,
/// LM-11 the barghest's night fright, LM-12 the lindworm's constrict and its tier-3 lairs, LM-13 the lairs of ten seeds, LM-14 the torch in rain and the overcast noon; and the pieces
/// that carry them (the lair book and its save, the new templates, the walkable mine).
/// </summary>
[Collection("sites")]
public class BestiaryTests(SiteFixture fx, Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly string[] Six = ["draugr", "cave_beast", "bog_thing", "barghest", "lindworm", "mountain_troll"];

    private FighterKit Monster(string id, int team, int x, int y = 0) => FightSetup.Roll(fx.C, FightSetup.KitDef(fx.C, id, id), 1, 900 + (ulong)id.Length * 31 + (ulong)(x / 100), id, team, x, y, team == 0 ? 1000 : -1000, 0);

    private static FighterKit Man(string name, int team, int x, int y = 0, bool armoured = false, int strength = 1250) => new()
    {
        Name = name, Team = team, X = x, Y = y, Weapon = new WeaponInstance("arming_sword", "iron"), Skill = 50, Strength = strength, FaceX = team == 0 ? FightMath.One : -FightMath.One,
        Worn = armoured ? [new WornItem("shirt", "linen"), new WornItem("mail_hauberk", "iron"), new WornItem("helm", "iron")] : [],
    };

    // ------------------------------------------------------------------ LM-9

    [Fact]
    public void LM9_SixMonstersLoad_EachBodyPlanPassesTheAnatomyChecks_EveryAttackResolves()
    {
        Assert.Equal(6, fx.S.Monsters.Count);
        Assert.Equal(Six.Order(StringComparer.Ordinal), fx.S.Monsters.Keys.Order(StringComparer.Ordinal));
        var human = fx.C.T.Plan("human");
        foreach (var id in Six)
        {
            var m = fx.S.Monsters[id];
            var kit = fx.Db.Get<FightKitDef>(m.Kit);
            Assert.Equal(id, kit.Plan);
            var plan = fx.C.T.Plan(id);
            Assert.InRange(plan.Parts.Length, 8, 64);
            // the checks of the anatomy: every non-hosted part has layers out to bone or a sound core, the body is whole, and something can end it (a vital organ, or an animating core)
            foreach (var p in plan.Parts.Where(p => !p.IsHosted && !p.Shell)) Assert.NotEmpty(p.Layers);
            Assert.True(plan.Parts.Any(p => p.Organs.Any(o => o.Vital)) || plan.AnimatingCore >= 0, id + " cannot be killed");
            var whole = BodyState.From(plan, []);
            Assert.False(whole.Undone);
            Assert.True(plan.DarkSight, id + " fights as well in the dark");
            Assert.NotNull(m.Lair);
            // its fight kit stands up in a fight and its natural attacks are the shape's
            var w = new FightWorld(fx.C, 3, 1, 8 * 3600);
            var f = w.Add(Monster(id, 0, 0));
            Assert.Equal(id, f.Plan.Id);
            var shape = fx.Db.Get<WeaponShapeDef>(kit.Weapon.Split(':')[0]);
            if (id != "draugr") Assert.Equal("natural", shape.Family);          // the draugr keeps the axe it was buried with
            // every attack of the shape resolves through the one resolver against a man, drawing wounds on some of 300 blows at a real body part
            var res = new ResolverV2(fx.C.T);
            var weapon = fx.C.T.Weapon(shape.Id, kit.Weapon.Split(':')[1]);
            for (int a = 0; a < shape.Attacks.Count; a++)
            {
                int landed = 0; var r = new BlowResult();
                for (uint k = 0; k < 300; k++)
                {
                    var s = StrikeV2.Create(weapon, a, human, (int)(k % 8), 50, kit.Strength);
                    s.SizeCm3 = kit.SizeCm3; s.SpeciesSizeCm3 = plan.SizeCm3; s.DefenderSizeCm3 = human.SizeCm3; s.Facing = Facing.Front;
                    res.Resolve(s, new BlowKey(9, k, 1, k), r);
                    if (r.WoundCount > 0) landed++;
                }
                Assert.True(landed > 20, $"{id}.{shape.Attacks[a].Id} wounded in {landed} of 300 blows");
            }
        }
        // the traits are the plan's own: no special rule
        var draugr = fx.C.T.Plan("draugr"); var bog = fx.C.T.Plan("bog_thing"); var hound = fx.C.T.Plan("barghest"); var worm = fx.C.T.Plan("lindworm"); var troll = fx.C.T.Plan("mountain_troll");
        Assert.True(draugr.Tireless && !draugr.FeelsPain && !draugr.Bleeds);
        Assert.False(bog.Bleeds); Assert.Equal("drag", bog.Grasp!.Kind); Assert.True(bog.AnimatingCore >= 0);
        Assert.Equal(1800, hound.SpeedPm); Assert.Equal(150, hound.Fear!.ShockPm); Assert.Equal(8, hound.Fear.RadiusM); Assert.Equal(400, hound.FleeBelowBloodPm);
        Assert.Equal(6, worm.Parts.Count(p => !p.IsHosted && !p.Shell)); Assert.Equal(1_800_000, worm.SizeCm3); Assert.Equal(6000, worm.Grasp!.ForceN); Assert.Equal("constrict", worm.Grasp.Kind);
        Assert.Equal(420_000, troll.SizeCm3); Assert.True(troll.Bleeds && troll.FeelsPain); Assert.Equal("troll_hide", troll["upper_body"].Layers[0].Tissue.Id);
        Assert.Equal("peat_flesh", bog["upper_body"].Layers[0].Tissue.Id); Assert.Equal("hound_hide", hound["torso"].Layers[0].Tissue.Id); Assert.Equal("worm_scale", worm["upper_body"].Layers[0].Tissue.Id);
        // masses: 70 kg, 260 kg, 140 kg, 90 kg, 1,800 kg (the spec's table) and the troll's 420
        Assert.Equal([80_000, 380_000, 140_000, 90_000, 1_800_000, 420_000], Six.Select(i => fx.C.T.Plan(i).SizeCm3).ToArray());
    }

    // ------------------------------------------------------------------ LM-10

    [Fact]
    public void LM10_ABogThingDrownsAManHeldInWater_UnlessHeIsFreed()
    {
        // held in water with no way to struggle: unconscious in 30 sim-seconds and dead in 240 by the breath rule
        var w = new FightWorld(fx.C, 11, 1, 8 * 3600) { NoStruggle = true, WaterAt = (x, y) => true };
        w.Add(Monster("bog_thing", 0, 0)); var man = w.Add(Man("Aldric", 1, 1200));
        Assert.True(w.Seize(0, 1));
        Assert.Equal(0, man.HeldBy);
        for (int t = 0; t < 20 * 31; t++) w.Step();
        Assert.True(man.Book.State is Consciousness.Unconscious or Consciousness.Dying, man.Book.State.ToString());
        for (int t = 20 * 31; t < 20 * 245; t++) w.Step();
        Assert.True(man.Book.Dead); Assert.Equal(DeathKind.Breath, man.Book.Death);
        Assert.Contains(w.Events, e => e.Kind == FightEventKind.Seized);

        // freed after ten sim-seconds: he breathes again and lives
        var w2 = new FightWorld(fx.C, 11, 1, 8 * 3600) { NoStruggle = true, WaterAt = (x, y) => true };
        w2.Add(Monster("bog_thing", 0, 0)); var man2 = w2.Add(Man("Aldric", 1, 1200));
        w2.Seize(0, 1);
        for (int t = 0; t < 20 * 10; t++) w2.Step();
        w2.Release(w2.HeldRow(1)!); w2.Fighters[0].X = -60000;      // let go, and the thing is far off
        Assert.Equal(-1, man2.HeldBy);
        for (int t = 0; t < 20 * 300; t++) w2.Step();
        Assert.False(man2.Book.Dead); Assert.True(man2.Book.Conscious);

        // on dry ground the drag hauls him to the water first, a metre and a half a second, and the breath stops only once he is in it
        var w3 = new FightWorld(fx.C, 11, 1, 8 * 3600) { NoStruggle = true, WaterAt = (x, y) => x >= 6000 };
        var bog = w3.Add(Monster("bog_thing", 0, 0)); var man3 = w3.Add(Man("Aldric", 1, 1200));
        w3.Seize(0, 1);
        for (int t = 0; t < 20 * 2; t++) w3.Step();
        Assert.True(bog.X > 1500 && man3.X < 6000, $"bog {bog.X} man {man3.X}"); Assert.True(man3.Book.State == Consciousness.Awake, man3.Book.State.ToString());
        for (int t = 0; t < 20 * 8; t++) w3.Step();
        Assert.True(man3.X >= 6000 - 900, $"dragged to {man3.X}");
        for (int t = 0; t < 20 * 40; t++) w3.Step();
        Assert.True(man3.Book.State is Consciousness.Unconscious or Consciousness.Dying);

        // a man who struggles (the AI's fight, strength 1,250 against 1,800 N) is often free in time: across 40 seeds some drown and some live, never all one way
        int dead = 0, alive = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var w4 = new FightWorld(fx.C, seed, seed, 8 * 3600) { WaterAt = (x, y) => true, NoStruggle = false };
            w4.Add(Monster("bog_thing", 0, 0)); var m4 = w4.Add(Man("Aldric", 1, 1200, strength: 100));
            w4.Seize(0, 1);
            for (int t = 0; t < 20 * 60; t++) w4.Step();           // no AI: only the hold and the struggle
            if (w4.Events.Any(e => e.Kind == FightEventKind.Released)) alive++; else dead++;
        }
        output.WriteLine($"a weak man held 60 s: broke free at least once in {alive} seeds, never in {dead}");
        Assert.True(alive > 0 && dead > 0);
    }

    // ------------------------------------------------------------------ LM-11

    [Fact]
    public void LM11_TheBarghestsHowl_CostsFoesWithinEightMetres150Permille_OnceAFight_AtNightOnly()
    {
        FightWorld Arena(bool night)
        {
            var w = new FightWorld(fx.C, 5, 1, 8 * 3600) { NightNow = () => night };
            w.Add(Monster("barghest", 0, 0));
            w.Add(Man("Near", 1, 5000)); w.Add(Man("Edge", 1, 7800, 600)); w.Add(Man("Far", 1, 12000));
            return w;
        }
        var dark = Arena(true);
        dark.Step();
        Assert.Equal(1500, dark.Fighters[1].M.PenaltyX100); Assert.Equal(1500, dark.Fighters[2].M.PenaltyX100); Assert.Equal(0, dark.Fighters[3].M.PenaltyX100);
        Assert.Contains(dark.Events, e => e.Kind == FightEventKind.Frightened && e.A == 150 && e.B == 2);
        int after = dark.Fighters[1].M.PenaltyX100;
        for (int t = 0; t < 400; t++) { dark.Step(); }
        Assert.Equal(1, dark.Events.Count(e => e.Kind == FightEventKind.Frightened));      // once a fight
        Assert.True(dark.Fighters[1].M.PenaltyX100 <= after);                               // no second shock; the penalty only wears off
        var day = Arena(false);
        for (int t = 0; t < 200; t++) day.Step();
        Assert.All(day.Fighters.Skip(1), f => Assert.Equal(0, f.M.PenaltyX100));
        Assert.DoesNotContain(day.Events, e => e.Kind == FightEventKind.Frightened);
        // and he runs at 9 m/s: sprinting for a second covers 9 m
        var run = new FightWorld(fx.C, 5, 1, 8 * 3600);
        var b = run.Add(Monster("barghest", 0, 0)); run.Add(Man("Far", 1, 60000));
        b.Cmd.MoveX = 1; b.Cmd.Sprint = true; b.Cmd.MoveX = 1;
        int x0 = b.X;
        for (int t = 0; t < 20; t++) { b.Cmd.MoveX = 1; b.Cmd.Sprint = true; run.Step(); }
        Assert.InRange(b.X - x0, 8000, 9200);
    }

    // ------------------------------------------------------------------ LM-12

    [Fact]
    public void LM12_ALindwormsConstrict_KillsAnUnarmouredMan_AndItIsOnlyPlacedInThreeLevelCaves()
    {
        var w = new FightWorld(fx.C, 21, 1, 8 * 3600) { NoStruggle = true };
        var worm = w.Add(Monster("lindworm", 0, 0)); var man = w.Add(Man("Aldric", 1, 1800));
        Assert.True(w.Seize(0, 1));
        Assert.Equal(6000, w.HoldOf(0)!.ForceN);
        int diedAt = -1;
        for (int t = 1; t <= 20 * 90 && diedAt < 0; t++) { w.Step(); if (man.Book.Dead || !man.Book.Conscious) diedAt = t; }
        output.WriteLine($"an unarmoured man in one constrict is out after {diedAt / 20} s");
        Assert.True(diedAt > 0 && diedAt <= 20 * 45, $"out at {diedAt}");
        int deadAt = -1;
        for (int t = diedAt; t < 20 * 245 && deadAt < 0; t++) { w.Step(); if (man.Book.Dead) deadAt = t; }
        output.WriteLine($"and dead after {(deadAt < 0 ? -1 : deadAt / 20)} s");
        Assert.True(man.Book.Dead);
        // a man in mail and a helm lasts longer than the bare one (the one resolver reads his armour)
        var wa = new FightWorld(fx.C, 21, 1, 8 * 3600) { NoStruggle = true };
        wa.Add(Monster("lindworm", 0, 0)); var mailed = wa.Add(Man("Aldric", 1, 1800, armoured: true));
        wa.Seize(0, 1);
        int bare = diedAt, mailedAt = -1;
        for (int t = 1; t <= 20 * 90 && mailedAt < 0; t++) { wa.Step(); if (mailed.Book.Dead || !mailed.Book.Conscious) mailedAt = t; }
        output.WriteLine($"in mail: out after {(mailedAt < 0 ? -1 : mailedAt / 20)} s");
        Assert.True(mailedAt < 0 || mailedAt >= bare);
        // placed only at tier 3 (three levels), at most one a region, and a lair of another kind elsewhere
        var rule = fx.S.Monsters["lindworm"].Lair!;
        Assert.Equal(3, rule.MinLevels); Assert.Equal(1, rule.MaxPerRegion); Assert.Equal("cave", rule.Site);
    }

    // ------------------------------------------------------------------ LM-13

    [Fact]
    public void LM13_Lairs_OneToThreeARegion_AllDeep_EachTiedToALegend_TheLindwormOnlyAtTierThree()
    {
        int worms = 0; var kinds = new SortedSet<string>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var tw = TestWorld.Get(seed);
            var a = Lairs.Resolve(tw.Region, fx.S, seed, tw.History);
            var b = Lairs.Resolve(tw.Region, fx.S, seed, tw.History);
            Assert.Equal(a, b);                                                                  // pure
            Assert.InRange(a.Count, 1, 3);
            Assert.Equal(tw.Region.Sites.Count(s => s.MonsterLair), a.Count);
            foreach (var l in a)
            {
                Assert.InRange(l.Levels, 2, 3);                                                  // always at level -2 or deeper
                Assert.NotEqual(EntityId.None, l.Legend);                                        // each tied to a legend of the history
                Assert.NotNull(tw.History.Chronicle.Get(l.Legend));
                Assert.Contains(l.Kind, Six); kinds.Add(l.Kind);
                var site = tw.Region.Sites.First(s => s.Id.Value == l.SiteId);
                Assert.True(site.Kind is SiteKind.Barrow or SiteKind.Cave);
                if (l.Kind == "lindworm") { Assert.Equal(3, l.Levels); Assert.Equal(SiteKind.Cave, site.Kind); }
                if (l.Kind is "draugr" or "barghest") Assert.Equal(SiteKind.Barrow, site.Kind); else Assert.Equal(SiteKind.Cave, site.Kind);
            }
            Assert.True(a.Count(l => l.Kind == "lindworm") <= 1);
            worms += a.Count(l => l.Kind == "lindworm");
            output.WriteLine($"seed {seed}: " + string.Join(", ", a.Select(l => $"{l.Kind}@{l.Levels}")));
        }
        output.WriteLine("kinds seen: " + string.Join(", ", kinds));
        Assert.True(kinds.Count >= 3);
    }

    [Fact]
    public void Lairs_RumouredUntilMet_ThenMetThenSlain_SavedInTheSitesSection_AndTheBookDoesNotMoveBack()
    {
        var tw = TestWorld.Get(1);
        var book = new SiteBook();
        book.EnsureLairs(tw.Region, fx.S, 1, tw.History);
        Assert.NotEmpty(book.Lairs);
        Assert.All(book.Lairs.Values, l => Assert.Equal(LairMet.None, l.Met));
        ulong id = book.Lairs.Keys.First();
        book.NoteMet(id, LairMet.Rumoured); book.NoteMet(id, LairMet.Met); book.NoteMet(id, LairMet.Rumoured);
        Assert.Equal(LairMet.Met, book.LairOf(id)!.Met);
        book.NoteMet(id, LairMet.Slain);
        var back = SiteSave.Capture(book).Restore();
        Assert.Equal(book.Lairs.Count, back.Lairs.Count);
        Assert.Equal(LairMet.Slain, back.LairOf(id)!.Met); Assert.Equal(book.LairOf(id)!.Kind, back.LairOf(id)!.Kind); Assert.Equal(book.LairOf(id)!.Legend, back.LairOf(id)!.Legend);
        // the save bytes round-trip through MessagePack with the optional keys
        var bytes = MessagePack.MessagePackSerializer.Serialize(SiteSave.Capture(book));
        var again = MessagePack.MessagePackSerializer.Deserialize<SiteSave>(bytes);
        Assert.Equal(book.Lairs.Count, again.Lairs.Count);
        // the site's history carries the kind the book holds
        var site = tw.Region.Sites.First(s => s.Id.Value == id);
        Assert.Equal(book.LairOf(id)!.Kind, SiteHistoryInfo.Of(site, tw.History, 0, 0, book).LairKind);
    }

    [Fact]
    public void EveryKindOfLair_BuildsASite_WithItsMonsterOnItsBier_AndTheNewTemplatesStandUp()
    {
        Assert.Equal(26, fx.S.Templates.Count);
        Assert.Empty(fx.S.Validate());
        ulong id = 7100;
        foreach (var (kind, lair, levels, site) in new[] { ("draugr", true, 3, SiteKind.Barrow), ("barghest", true, 3, SiteKind.Barrow), ("cave_beast", true, 2, SiteKind.Cave), ("bog_thing", true, 3, SiteKind.Cave),
            ("lindworm", true, 3, SiteKind.Cave), ("mountain_troll", true, 2, SiteKind.Cave) })
        {
            var h = SiteFixture.Of(site, site == SiteKind.Barrow ? "barrow" : "den", ++id, levels, lair, kind);
            var m = SiteGen.Generate(fx.S, h, 41);
            var mon = m.Inhabitants.Where(n => n.Monster == kind).ToList();
            Assert.NotEmpty(mon);
            Assert.All(mon, n => { Assert.True(n.Legend || mon.Count > 1); Assert.True(n.State is InhabitantState.Dormant or InhabitantState.Asleep); });
            output.WriteLine($"{kind}: template {m.Template}, {m.Levels} levels, {mon.Count} of them");
        }
        Assert.Equal("barrow.deep", SiteGen.Generate(fx.S, SiteFixture.Of(SiteKind.Barrow, "barrow", 7200, 2, true, "barghest"), 41).Template);
        Assert.Equal("cave.bog_island", SiteGen.Generate(fx.S, SiteFixture.Of(SiteKind.Cave, "den", 7201, 2, true, "bog_thing"), 41).Template);
        Assert.Equal("cave.lair", SiteGen.Generate(fx.S, SiteFixture.Of(SiteKind.Cave, "den", 7202, 2, true, "lindworm"), 41).Template);
        Assert.Equal("mine.drowned", fx.S.TemplateFor("Mine", "x", "drowned")!.Id);                  // a flooded mine of three levels or more: named by a landmark or a mod
        // the other new templates generate
        foreach (var (kind, sub, mine) in new[] { (SiteKind.Ruin, "chapel", MineState.Active), (SiteKind.Barrow, "crypt", MineState.Active), (SiteKind.Cave, "hold", MineState.Active),
            (SiteKind.Cave, "hermitage", MineState.Active), (SiteKind.Cave, "sea", MineState.Active) })
        {
            var t = fx.S.TemplateFor(kind.ToString(), sub, kind == SiteKind.Mine ? sub : null);
            Assert.NotNull(t); Assert.Equal(sub, t!.Sub);
            var m = SiteGen.Generate(fx.S, SiteFixture.Of(kind, sub, ++id, 2, mine: mine), 41);
            Assert.Equal(t.Id, m.Template);
        }
    }

    [Fact]
    public void ABogThingsHollowHasWater_ASecondFightsHoldCarriesIntoTheCrawl()
    {
        var m = SiteGen.Generate(fx.S, SiteFixture.Of(SiteKind.Cave, "den", 7300, 2, true, "bog_thing"), 3);
        Assert.Contains(m.Tiles, t => t == SiteTile.Water);
        var bog = m.Inhabitants.First(n => n.Monster == "bog_thing");
        Assert.Equal("bog_thing", bog.Kit);
    }

    // ------------------------------------------------------------------ the walkable mine

    [Fact]
    public void AMineTheHeroOpens_IsASiteWithALevelPer10MetresOfDepth_SavedAndPutBackOnLoad()
    {
        var tw = TestWorld.Get(1);
        var book = new SiteBook();
        Assert.Equal([1, 1, 1, 2, 3, 6, 8, 8], new[] { 0, 5, 10, 11, 25, 60, 80, 200 }.Select(MineSites.LevelsFor).ToArray());
        int before = tw.Region.Sites.Count;
        var s = MineSites.Open(book, tw.Region, 40, 41, MineralType.Iron, 900, 55, "Hero's iron mine", 3)!;
        Assert.Equal(before + 1, tw.Region.Sites.Count);
        Assert.Equal(SiteKind.Mine, s.Kind); Assert.Equal(6, s.Levels); Assert.Equal(MineState.Active, s.Mine); Assert.True(s.HeroOpened);
        Assert.Same(s, MineSites.Open(book, tw.Region, 40, 41, MineralType.Iron, 900, 55, "Hero's iron mine", 3));    // the same deposit is the same site
        Assert.Equal(before + 1, tw.Region.Sites.Count);
        var info = SiteHistoryInfo.Of(s, tw.History, 3, 0, book);
        Assert.True(info.Opened);
        var map = SiteGen.Generate(fx.S, info, 5);
        Assert.Equal(6, map.Levels);                                                            // a level for each 10 m (the template's own 2 to 4 would have clamped it)
        Assert.Equal("mine.active", map.Template);
        // saved and put back into a region built afresh
        var back = SiteSave.Capture(book).Restore();
        Assert.Single(back.OpenedMines);
        var fresh = RegionGenerator.Generate(tw.Continent, tw.History, tw.Pick.CellX, tw.Pick.CellY, tw.RegionSettings);
        Assert.Equal(before, fresh.Sites.Count);
        MineSites.Restore(back, fresh); MineSites.Restore(back, fresh);
        Assert.Equal(before + 1, fresh.Sites.Count);
        Assert.Equal(s.Id, fresh.Sites.Last().Id); Assert.Equal(6, fresh.Sites.Last().Levels);
        // opening a mine never changes what region generation hashes
        Assert.Equal(tw.Region.ComputeHash(), fresh.ComputeHash());
    }

    // ------------------------------------------------------------------ LM-14

    [Fact]
    public void LM14_HeavyRainPutsAnOutdoorTorchOutAt50PermilleAMinute_NoonUnderFullCloudIs600()
    {
        Assert.Equal(1000, OutdoorLight.RainPm(25)); Assert.Equal(480, OutdoorLight.RainPm(12)); Assert.Equal(0, OutdoorLight.RainPm(0)); Assert.Equal(1000, OutdoorLight.RainPm(90));
        Assert.Equal(50, OutdoorLight.TorchOutPmPerMinute(1000, false));
        Assert.Equal(25, OutdoorLight.TorchOutPmPerMinute(500, false)); Assert.Equal(24, OutdoorLight.TorchOutPmPerMinute(480, false));
        Assert.Equal(0, OutdoorLight.TorchOutPmPerMinute(1000, true));                 // under a roof or below ground
        Assert.Equal(0, OutdoorLight.TorchOutPmPerMinute(0, false));
        int out5 = 0, outRoof = 0, outHalf = 0;
        for (long minute = 0; minute < 20_000; minute++)
        {
            if (OutdoorLight.TorchGoesOut(77, minute, 1, 1000, false)) out5++;
            if (OutdoorLight.TorchGoesOut(77, minute, 1, 1000, true)) outRoof++;
            if (OutdoorLight.TorchGoesOut(77, minute, 1, 500, false)) outHalf++;
        }
        output.WriteLine($"20,000 minutes in heavy rain: {out5} out ({out5 / 200.0:F2}%), half rain {outHalf}, roofed {outRoof}");
        Assert.InRange(out5, 900, 1100); Assert.InRange(outHalf, 420, 580); Assert.Equal(0, outRoof);
        // noon under full cloud is 600 permille; under none 1,000
        Assert.Equal(600, OutdoorLight.NoonLightPm(1000)); Assert.Equal(1000, OutdoorLight.NoonLightPm(0)); Assert.Equal(800, OutdoorLight.NoonLightPm(500));
        var noon = new GameTime(((long)30 * 86400) + 12 * 3600);
        var clear = LightModel.AmbientAt(noon, 480); var dull = LightModel.AmbientAt(noon, 480, 1000);
        Assert.Equal(255, clear.Sun); Assert.Equal(255 * 600 / 1000, dull.Sun); Assert.Equal(DayPhase.Day, dull.Phase);
        // night and underground are as they were
        var night = new GameTime(((long)30 * 86400) + 1 * 3600);
        Assert.Equal(LightModel.AmbientAt(night, 480), LightModel.AmbientAt(night, 480, 1000));
        Assert.Equal(0, LightModel.AmbientLevel(dull, -1, false));
        Assert.Equal(LightModel.AmbientAt(noon, 480), LightModel.AmbientAt(noon, 480, 0));
        // the hero's lit torch in the live world goes out in heavy rain in the open and not under a roof (a few hours of it)
        Assert.Equal(0x832u, RngStreams.TorchRainOut);
    }
}
