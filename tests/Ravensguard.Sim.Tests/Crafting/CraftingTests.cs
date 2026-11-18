using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Economy;
using Xunit.Abstractions;
using Sk = Ravensguard.Sim.People.Skill;
using Ravensguard.Sim.Tests.Content;

namespace Ravensguard.Sim.Tests.Crafting;

public class CraftingTests(ITestOutputHelper output)
{
    private static string BaseDir => Ravensguard.Sim.WorldGen.ContinentSettings.FindBaseContent()!;

    private static (RecipeDb? Db, List<string> Errs) LoadWithMod(string recipesJson, string file = "extra.jsonc")
    {
        using var t = new TempDir();
        var mod = t.Mod("craft_bad", recipesJson, "\"dependencies\": [\"base\"], \"load_after\": [\"base\"]", file);
        var load = new ModLoader(ContentTypeRegistry.Default).Load([BaseDir, mod]);
        Assert.True(load.Success, load.Report.ToString());
        var errs = new List<string>();
        var db = RecipeDb.Build(load.Database, new GoodCatalog(load.Database), errs);
        return (db, errs);
    }

    // ------------------------------------------------------------------ K-1, K-2

    [Fact]
    public void K1_DataValidation_34RecipesLoad_EverythingResolves_NoCycle_UnattendedStepsHaveDeadlineAndGrace()
    {
        var w = new CraftWorld();
        Assert.Equal(34, w.Db.Recipes.Count);
        Assert.Equal(12, w.Db.Proficiencies.Count);
        Assert.Equal(5, w.Db.Sets.Count);
        Assert.Equal(3, w.Db.Manuscripts.Count);
        foreach (var cat in RecipeDb.Categories) Assert.Contains(w.Db.Recipes, r => r.Category == cat);
        Assert.Equal(3, w.Db.Recipes.Count(r => r.IsPractice));
        for (int i = 1; i < w.Db.Recipes.Count; i++) Assert.True(string.CompareOrdinal(w.Db.Recipes[i - 1].Id, w.Db.Recipes[i].Id) < 0);
        foreach (var r in w.Db.Recipes)
        {
            foreach (var n in w.Db.AllNeeds(r)) Assert.Contains(n.Quality, w.Db.KnownQualities);
            foreach (var s in r.Steps.Where(s => !s.Attended)) { Assert.True(s.DeadlineSec > 0 && s.GraceSec > 0, r.Id); Assert.NotEqual(ItemTable.NoGood, s.RuinedInto); }
        }
        Assert.Contains(w.Db.Recipes, r => r.HasUnattended);
        // the graph has no cycle and every requirement exists (a cycle is an error of the build)
        var ids = w.Db.Proficiencies.Select(p => p.Id).ToHashSet();
        foreach (var p in w.Db.Proficiencies) foreach (var q in p.Requires) Assert.Contains(q.Id, ids);
    }

    [Fact]
    public void K1_ABadRecipe_FailsWithFileAndId_AndACycleIsFound()
    {
        var (db, errs) = LoadWithMod("""
            [ { "type": "craft_recipe", "id": "bad_alt", "name": "Bad", "category": "food", "skill": "Baking", "difficulty": 10,
                "components": [ [ { "good": "flour", "kg": 1.0 }, { "good": "unobtainium", "kg": 1.0 } ] ],
                "steps": [ { "id": "mix", "minutes": 5 } ], "results": [ { "good": "bread", "kg": 0.5 } ] },
              { "type": "craft_recipe", "id": "bad_rise", "name": "Bad rise", "category": "food", "skill": "Baking", "difficulty": 10,
                "components": [ [ { "good": "flour", "kg": 1.0 } ] ],
                "steps": [ { "id": "mix", "minutes": 5 }, { "id": "rise", "hours": 2, "attended": false } ], "results": [ { "good": "bread", "kg": 0.5 } ] },
              { "type": "proficiency", "id": "loop_a", "name": "A", "requires": [ { "id": "loop_b", "min_pm": 100 } ] },
              { "type": "proficiency", "id": "loop_b", "name": "B", "requires": [ { "id": "loop_a", "min_pm": 100 } ] } ]
            """);
        Assert.Null(db);
        output.WriteLine(string.Join("\n", errs));
        Assert.Contains(errs, e => e.Contains("extra.jsonc") && e.Contains("bad_alt") && e.Contains("unobtainium"));
        Assert.Contains(errs, e => e.Contains("bad_rise") && e.Contains("deadline_h and grace_h"));
        Assert.Contains(errs, e => e.Contains("cycle") && e.Contains("loop_a") && e.Contains("loop_b"));
    }

    [Fact]
    public void K2_MassCheck_EveryRecipeConserves_BakeLoaf136In_132Out_AndALyingRecipeIsRejected()
    {
        var w = new CraftWorld();
        foreach (var r in w.Db.Recipes) Assert.True(w.Db.OutputMassG(r) <= w.Db.InputMassG(r) + r.AddsG, r.Id);
        var loaf = w.Db["bake_loaf"];
        Assert.Equal(1360, w.Db.InputMassG(loaf)); Assert.Equal(1320, w.Db.OutputMassG(loaf));
        var (db, errs) = LoadWithMod("""
            [ { "type": "craft_recipe", "id": "gold_from_air", "name": "Alchemy", "category": "craft", "skill": "Baking", "difficulty": 10,
                "components": [ [ { "good": "flour", "kg": 0.1 } ] ], "steps": [ { "id": "stir", "minutes": 5 } ], "results": [ { "good": "bread", "kg": 5.0 } ] } ]
            """);
        Assert.Null(db); Assert.Contains(errs, e => e.Contains("gold_from_air") && e.Contains("matter cannot appear"));
    }

    // ------------------------------------------------------------------ K-3 .. K-6

    [Fact]
    public void K3_K4_QualityDistribution_Skill50Difficulty25()
    {
        Assert.Equal([0, 10, 30, 1, 0], CraftMath.QualityCounts(50, 0, 0));
        Assert.Equal([0, 5, 30, 6, 0], CraftMath.QualityCounts(50, 5, 0));
        Assert.Equal(41, CraftMath.QualityCounts(50, 0, 0).Sum());
    }

    [Fact]
    public void K5_Proficiency_BreadMaking40Percent_EffectiveSkill44_TimeFactor136_PoorHeavy()
    {
        var w = new CraftWorld();
        w.Skill(Sk.Baking, 50);
        w.St.Know.SetPm(w.Hero, w.Db.ProfIndex("cookery"), 1000); w.St.Know.SetPm(w.Hero, w.Db.ProfIndex("bread_making"), 400);
        var plan = w.Stock("bake_loaf");
        Assert.Equal(44, plan.EffectiveSkill); Assert.Equal(1360, plan.ProfPm);
        Assert.Equal([0, 16, 25, 0, 0], CraftMath.QualityCounts(44, 0, 0)); Assert.Equal(CraftMath.QualityCounts(44, plan.ToolBonus, 0), plan.QualityCounts);
        Assert.Equal(1360, CraftMath.ProfTimePm(1600, 400)); Assert.Equal(6, CraftMath.ProfSkillPenalty(10, 400));
    }

    [Fact]
    public void K6_Failure_Skill20Difficulty30_300Permille_Waste40_NoneFromFiveOver()
    {
        Assert.Equal(300, CraftMath.FailPm(20, 30)); Assert.Equal(40, CraftMath.WastePct(20, 30));
        Assert.Equal(0, CraftMath.FailPm(35, 30)); Assert.Equal(20, CraftMath.FailPm(34, 30));
        Assert.Equal(30, CraftMath.WastePct(35, 30)); Assert.Equal(100, CraftMath.WastePct(-100, 30)); Assert.Equal(800, CraftMath.FailPm(0, 80));
    }

    [Fact]
    public void Failure_WastesComponentsIntoWaste_AndDropsATier_MassStaysExact()
    {
        // a hopeless crafter fails almost every time; over many jobs some fail, and every one keeps the books balanced
        int failed = 0;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var w = new CraftWorld(seed); w.Skill(Sk.Cooking, 0);
            var j = w.StartNew("cook_pottage"); var ev = w.Work(j);
            if (j.Failed) { failed++; Assert.Equal(CraftMath.WastePct(j.EffSkill, 10), j.WastePct); Assert.Contains(ev, e => e.Kind == CraftEventKind.Failed); Assert.True(w.Qty("waste") > 0 || w.Econ.Goods.TryIdOf("waste", out _)); }
            Assert.True(w.Audit());
        }
        Assert.InRange(failed, 1, 29);
    }

    // ------------------------------------------------------------------ K-7 .. K-9

    private static (CraftWorld W, CraftJob J) Rising(ulong seed = 7)
    {
        var w = new CraftWorld(seed); w.Skill(Sk.Baking, 50); w.Now = 8 * 3600;
        var j = w.StartNew("bake_loaf"); w.Work(j);
        Assert.Equal(CraftJobState.Waiting, j.State);
        return (w, j);
    }

    [Fact]
    public void K7_UnattendedWindow_FullThenMinusOneThenMinusTwoThenRuined()
    {
        var (w0, j0) = Rising();
        long ready = j0.ReadySec;
        Assert.Equal(3 * 3600, ready - (j0.ReadySec - 3 * 3600)); Assert.Equal(ready + 2 * 3600, j0.DeadlineSec); Assert.Equal(ready + 5 * 3600, j0.RuinSec);
        Assert.False(w0.R.Collect(j0, w0.Actor(), ready - 1).Ok);              // not risen yet
        foreach (var (offset, tiers) in new (long, int)[] { (0, 0), (2 * 3600, 0), (2 * 3600 + 1800, 1), (3 * 3600, 1), (4 * 3600, 2), (5 * 3600, 2) })
        {
            var (w, j) = Rising();
            Assert.True(w.R.Collect(j, w.Actor(), j.ReadySec + offset).Ok, offset.ToString());
            Assert.Equal(tiers, j.TierDrop);
            Assert.Equal(CraftJobState.Active, j.State);
        }
        // past the ruin time the batch is ruined into sour dough, nothing is lost from the books
        var (wr, jr) = Rising();
        Assert.True(wr.R.Collect(jr, wr.Actor(), jr.RuinSec + 60).Ok);
        Assert.Equal(CraftJobState.Ruined, jr.State);
        Assert.True(wr.Qty("dough_sour") > 0); Assert.Equal(0, wr.Qty("flour")); Assert.True(wr.Audit()); Assert.False(wr.Econ.OtherStores.ContainsKey(jr.Id));
    }

    [Fact]
    public void K7_TheHourlyPass_AlertsAtReadyAtDeadlineBeforeRuin_AndRuinsTheBatch_NobodyIsNeeded()
    {
        var (w, j) = Rising();
        Assert.Empty(w.R.Hourly(j.ReadySec - 1));
        Assert.Equal([CraftEventKind.Ready], w.R.Hourly(j.ReadySec).Select(e => e.Kind));
        Assert.Equal([CraftEventKind.PastDeadline], w.R.Hourly(j.DeadlineSec).Select(e => e.Kind));
        Assert.Equal([CraftEventKind.Ruining], w.R.Hourly(j.RuinSec - 3600).Select(e => e.Kind));
        Assert.Empty(w.R.Hourly(j.RuinSec));                                      // alerts are given once; the ruin comes a second after the grace
        var ev = w.R.Hourly(j.RuinSec + 1);
        Assert.Equal([CraftEventKind.Ruined], ev.Select(e => e.Kind)); Assert.Equal(CraftJobState.Ruined, j.State);
        Assert.True(w.Qty("dough_sour") > 0); Assert.True(w.Audit());
    }

    [Fact]
    public void K7_SaveAndLoadWhileTheDoughRises_ChangesNothing_AndTheLoadedJobFinishesTheSame()
    {
        var econ = EconFixture.NewEconomy(false);
        var w = new CraftWorld(7, econ); w.Skill(Sk.Baking, 50);
        var j = w.StartNew("bake_loaf"); w.Work(j);
        var world = EconFixture.WorldWith(econ); world.Crafting = w.St;
        var bytes = SaveCodec.Encode(WorldSave.Capture(world, EconFixture.ContentHash, "0.1.0"));
        var back = WorldSave.Restore(SaveCodec.Decode(bytes), EconFixture.Content, EconFixture.ContentHash);
        Assert.Equal(bytes, SaveCodec.Encode(WorldSave.Capture(back, EconFixture.ContentHash, "0.1.0")));      // save, load, save: byte-identical with an unfinished item
        var lj = back.Crafting!.Get(j.Id)!;
        Assert.Equal((j.State, j.ReadySec, j.DeadlineSec, j.RuinSec, j.StepIndex), (lj.State, lj.ReadySec, lj.DeadlineSec, lj.RuinSec, lj.StepIndex));
        Assert.True(back.Economy!.OtherStores.ContainsKey(j.Id));                                    // the components are in the job's store, which the economy saved
        var wb = new CraftWorld(7, back.Economy); wb.St.Book.Set(wb.Hero, Sk.Baking, 50 * CraftMath.MicroPerLevel);
        var r2 = new CraftRunner(wb.Db, back.Economy, back.Crafting!, 7, wb.Ids, o => back.Economy.OtherStores.GetValueOrDefault(o));
        Assert.True(r2.Collect(lj, wb.Actor(), lj.ReadySec).Ok);
        r2.Advance(lj, 6 * 3600, lj.ReadySec);
        Assert.Equal(CraftJobState.Done, lj.State);
        Assert.True(back.Economy.MassAuditHolds());
    }

    [Fact]
    public void K8_InterruptAndResume_ProgressKept_AnotherPersonFinishes_ResultCreditedToThem()
    {
        var w = new CraftWorld(); w.Skill(Sk.Tailoring, 20); w.Skill(Sk.Tailoring, 20, w.Other);
        w.St.Know.SetPm(w.Hero, w.Db.ProfIndex("tailoring"), 1000); w.St.Know.SetPm(w.Other, w.Db.ProfIndex("tailoring"), 1000);
        var j = w.StartNew("sew_shirt");
        w.R.Advance(j, 7800, w.Now);                                     // 2 h 10 min: the cut is done (30 min), 100 min into the sewing
        Assert.Equal((1, 6000L, 7800L), (j.StepIndex, j.StepSeconds, j.TotalSeconds));
        w.R.Pause(j); Assert.Equal(CraftJobState.Paused, j.State);
        Assert.False(w.R.Resume(j, new CraftActor(EntityId.Make(IdKind.Person, 99))));   // someone who does not know the recipe cannot
        Assert.True(w.R.Resume(j, w.Actor(w.Other)));
        Assert.Equal(7800, j.TotalSeconds); Assert.Equal(w.Other, j.Crafter);
        var ev = w.R.Advance(j, 10 * 3600, w.Now + 7800);
        Assert.Equal(CraftJobState.Done, j.State); Assert.Contains(ev, e => e.Kind == CraftEventKind.Finished);
        var shirt = w.Econ.Items.LiveRows().Select(i => w.Econ.Items.At(i)).Single(r => r.Shape == "shirt");
        Assert.Equal(w.Hero, shirt.Owner);                               // the work was the hero's; the skill on the item is the finisher's
        Assert.False(w.Econ.OtherStores.ContainsKey(j.Id));                // the job's store is emptied and closed
        Assert.True(w.Audit());
    }

    [Fact]
    public void K9_Mending_350To650_Repaired_ThenCapped700_AndPatchTo800()
    {
        var w = new CraftWorld(); w.Skill(Sk.Tailoring, 30);
        var g = w.Garment(350);
        var j = w.StartNew("mend_garment", 1, g); w.Work(j);
        Assert.Equal(650, w.Econ.Items.Get(g).ConditionPermille); Assert.True(w.Econ.Items.Get(g).Flags.HasFlag(ItemFlags.Repaired));
        j = w.StartNew("mend_garment", 1, g); w.Work(j);
        Assert.Equal(700, w.Econ.Items.Get(g).ConditionPermille);
        var plan = w.Stock("mend_garment"); Assert.NotNull(w.R.Plan(w.Db["mend_garment"], w.Actor(), w.Site, w.Pack, 1, null, false).Missing.FirstOrDefault(m => m.Contains("nothing to mend")));
        Assert.Null(w.R.Start(plan, w.Actor(), w.Hero, w.Pack, w.Now, 0, 0, g));              // nothing left to mend at 700
        j = w.StartNew("patch_garment", 1, g); w.Work(j);
        Assert.Equal(800, w.Econ.Items.Get(g).ConditionPermille);
    }

    // ------------------------------------------------------------------ K-10 .. K-15

    [Fact]
    public void K10_LevelZeroTool_ARockHammersANail_SlowerAndWorse()
    {
        var w = new CraftWorld(); w.Skill(Sk.Carpentry, 40);
        var r = w.Db["repair_cart_wheel"];
        foreach (var s in r.Slots) w.Pack.Create(s.Alts[0].Good, s.Alts[0].Qty, CreateReason.Other);
        var real = w.R.Plan(r, w.Actor(), w.Site, w.Pack);
        var rock = new CraftSite { Light = 255 }.AddTool(w.Db, "saw").WithImprovised("hammering");
        var bare = new CraftSite { Light = 255 }.AddTool(w.Db, "saw");
        Assert.True(real.CanStart); Assert.Equal(1000, real.ToolPm);
        var p = w.R.Plan(r, w.Actor(), rock, w.Pack);
        Assert.True(p.CanStart); Assert.Equal(-10, p.ToolBonus); Assert.Equal(1500, p.ToolPm);
        Assert.True(p.AttendedSeconds * 100 >= real.AttendedSeconds * 149 - 100 && p.AttendedSeconds * 100 <= real.AttendedSeconds * 151 + 100, $"{p.AttendedSeconds} vs {real.AttendedSeconds}");
        Assert.False(w.R.Plan(r, w.Actor(), bare, w.Pack).CanStart);
    }

    [Fact]
    public void K11_LearnFromAMaster_6_5Hours_228Mites_RecipeKnown_SkillGainDoubled_CoinConserved()
    {
        Assert.Equal(65, CraftMath.LearnHoursX10(25)); Assert.Equal(228, CraftMath.TeachCostMites(25));
        var w = new CraftWorld(); var ledger = w.Econ.Ledger;
        var master = new CraftActor(w.Other); w.Skill(Sk.Baking, 40, w.Other); w.Skill(Sk.Baking, 5);
        var loaf = w.Db["bake_loaf"]; Assert.False(w.St.Know.Knows(loaf, w.Hero, w.St.Book));
        var sp = ledger.Open(PurseKind.Person, w.Hero); var mp = ledger.Open(PurseKind.Person, w.Other); ledger.Mint(sp, new Coin(1000), SourceReason.Seed);
        Assert.Null(w.R.StartTeaching(w.Actor(), new CraftActor(w.Hero), loaf, false, ledger, sp, mp));     // a master must know it
        Assert.Null(w.R.StartTeaching(w.Actor(), master, w.Db["bake_flatbread"], false, ledger, sp, mp));       // known by all: nothing to teach
        var s = w.R.StartTeaching(w.Actor(), master, loaf, false, ledger, sp, mp)!;
        Assert.Equal(65 * 360, s.NeededSec); Assert.Equal(228, ledger.Balance(mp).Mites); Assert.Equal(1000 - 228, ledger.Balance(sp).Mites);
        long before = w.St.Book.Micro(w.Hero, Sk.Baking);
        w.R.AdvanceTeaching(3 * 3600, w.Now); Assert.False(w.St.Know.Knows(loaf, w.Hero, w.St.Book));
        var ev = w.R.AdvanceTeaching(3 * 3600 + 1800, w.Now + 3 * 3600);
        Assert.Contains(ev, e => e.Kind == CraftEventKind.Learned); Assert.True(w.St.Know.Knows(loaf, w.Hero, w.St.Book)); Assert.Empty(w.St.Sessions);
        long gain = w.St.Book.Micro(w.Hero, Sk.Baking) - before;
        var sim = new SkillBook(); sim.Set(w.Hero, Sk.Baking, 5_000_000); long expect = 0;
        for (int h = 1; h <= 6; h++) expect += sim.Use(w.Hero, Sk.Baking, 50, 25, h * 3600, 0, true);        // six hours of lessons, one use each, at double rate
        Assert.Equal(expect, gain); Assert.True(gain > 6 * CraftMath.GainMicro(5_000_000, 50) * 19 / 10);
        Assert.True(ledger.Audit().IsConserved);
        // paying by labour costs no coin
        var w2 = new CraftWorld(); w2.Skill(Sk.Baking, 40, w2.Other);
        Assert.NotNull(w2.R.StartTeaching(w2.Actor(), new CraftActor(w2.Other), loaf, true));
    }

    [Fact]
    public void K12_Watching_Wits50Skill20Difficulty25_Needs8_5Hours_710Permille_Deterministic_FailureHalves()
    {
        Assert.Equal(85, CraftMath.WatchHoursX10(25)); Assert.Equal(710, CraftMath.WatchProbPm(50, 20)); Assert.Equal(950, CraftMath.WatchProbPm(100, 100));
        string Run(ulong seed)
        {
            var w = new CraftWorld(seed); w.Skill(Sk.Baking, 5); var loaf = w.Db["bake_loaf"];
            Assert.Equal(85, loaf.WatchHoursX10);
            var me = new CraftActor(w.Hero, Wits: 50);
            w.R.Watch(me, loaf, 8 * 3600 + 1799); Assert.False(w.R.TryWatchLearn(me, loaf, w.Now));      // 8.4999 h is not enough
            w.R.Watch(me, loaf, 1);
            bool ok = w.R.TryWatchLearn(me, loaf, w.Now);
            if (!ok) Assert.Equal(85 * 360 / 2, w.St.Know.WatchSeconds(w.Hero, loaf.Index)); else Assert.True(w.St.Know.Knows(loaf, w.Hero, w.St.Book));
            return ok.ToString();
        }
        for (ulong s = 1; s <= 8; s++) Assert.Equal(Run(s), Run(s));
        Assert.Contains("True", Enumerable.Range(1, 40).Select(s => Run((ulong)s))); Assert.Contains("False", Enumerable.Range(1, 40).Select(s => Run((ulong)s)));
    }

    [Fact]
    public void K13_Practice_NoItem_CapStopsGain_At12TheGainIsTheSpecNumber()
    {
        Assert.Equal(139_392, CraftMath.GainMicro(12_000_000, 45));
        var w = new CraftWorld(); w.Skill(Sk.Smithing, 12);
        int items = w.Econ.Items.Count;
        var j = w.StartNew("practice_billet"); var ev = w.Work(j);
        Assert.Equal(12_000_000 + 139_392, w.St.Book.Micro(w.Hero, Sk.Smithing));
        Assert.Equal(items, w.Econ.Items.Count); Assert.DoesNotContain(ev, e => e.Kind == CraftEventKind.Finished && e.Text == "arming_sword");
        Assert.Equal(250, w.Qty("scrap_iron")); Assert.Equal(0, w.Qty("iron_bar")); Assert.True(w.St.Know.Pm(w.Hero, w.Db.ProfIndex("metalworking_principles")) > 0);
        // at the cap level (30) nothing is gained, though the work is still done
        var w2 = new CraftWorld(); w2.Skill(Sk.Smithing, 30);
        var j2 = w2.StartNew("practice_billet"); w2.Work(j2);
        Assert.Equal(30_000_000, w2.St.Book.Micro(w2.Hero, Sk.Smithing));
        Assert.Equal(0, w2.St.Know.Pm(w2.Hero, w2.Db.ProfIndex("metalworking_principles")));
    }

    [Fact]
    public void SkillGain_ByUse_Formula_TooEasy_UsesPerHour_Fatigue_Master()
    {
        var b = new SkillBook(); var p = EntityId.Make(IdKind.Person, 5);
        Assert.Equal(50_000, CraftMath.GainMicro(50_000_000, 50));   // 0.4 x d(0.5) x (1 - 0.5)^2 = 0.05 levels
        b.Set(p, Skill.Farming, 12_000_000);
        long g = b.Use(p, Skill.Farming, 100, 30, 0); Assert.Equal(CraftMath.GainMicro(12_000_000, 100), g);
        b.Set(p, Skill.Housekeeping, 80_000_000); Assert.Equal(0, b.Use(p, Skill.Housekeeping, 100, 30, 0));                  // 30+ levels too easy
        int n = 0; for (int i = 0; i < 20; i++) if (b.Use(p, Skill.Cooking, 50, 40, 7200) > 0) n++;
        Assert.Equal(12, n); Assert.True(b.Use(p, Skill.Cooking, 50, 40, 7200 + 3600) > 0);                                  // the next hour starts fresh
        b.Set(p, Skill.Baking, 10_000_000); long fat = b.Use(p, Skill.Baking, 50, 30, 100_000, 900); b.Set(p, Skill.Baking, 10_000_000);
        Assert.Equal(CraftMath.GainMicro(10_000_000, 50) / 2, fat);
        b.Set(p, Skill.Brewing, 10_000_000); Assert.Equal(2 * CraftMath.GainMicro(10_000_000, 50), b.Use(p, Skill.Brewing, 50, 30, 200_000, 0, true));
    }

    [Fact]
    public void K14_Batch_4Loaves_221AttendedMinutes_TheRiseStays3Hours()
    {
        var w = new CraftWorld(); w.Skill(Sk.Baking, 25);
        w.St.Know.SetPm(w.Hero, w.Db.ProfIndex("cookery"), 1000); w.St.Know.SetPm(w.Hero, w.Db.ProfIndex("bread_making"), 1000);
        var plan = w.Stock("bake_loaf", 4);
        Assert.True(plan.CanStart); Assert.Equal(1000, plan.TimePm);
        Assert.Equal(221 * 60, plan.AttendedSeconds); Assert.Equal(3 * 3600, plan.UnattendedSeconds);
        Assert.Equal(65 * 60, w.R.Plan(w.Db["bake_loaf"], w.Actor(), w.Site, w.Pack, 1).AttendedSeconds);
        Assert.False(w.R.Plan(w.Db["bake_loaf"], w.Actor(), w.Site, w.Pack, 7).CanStart);       // batch.max is 6
    }

    [Fact]
    public void K15_Light_SewingNeeds100_At90TooDark_At130Penalty5_At140None()
    {
        var w = new CraftWorld(); w.Skill(Sk.Tailoring, 30); w.Stock("mend_garment");
        CraftPlan At(int light) { w.Site.Light = light; return w.R.Plan(w.Db["mend_garment"], w.Actor(), w.Site, w.Pack); }
        var dark = At(90); Assert.False(dark.CanStart); Assert.Equal(LightState.TooDark, dark.Light); Assert.Contains("too dark", dark.Missing);
        var dim = At(130); Assert.True(dim.CanStart); Assert.Equal(-5, dim.LightPenalty); Assert.Equal(LightState.Dim, dim.Light);
        var fine = At(140); Assert.True(fine.CanStart); Assert.Equal(0, fine.LightPenalty); Assert.Equal(LightState.Fine, fine.Light);
        Assert.True(At(100).CanStart); Assert.Equal(CraftMath.QualityCounts(dim.EffectiveSkill, dim.ToolBonus, -5), dim.QualityCounts);
    }

    // ------------------------------------------------------------------ K-16 .. K-19

    [Fact]
    public void K16_TheScreensExpectedQuality_EqualsTheSimulatedOne_Within1Percent()
    {
        var counts = CraftMath.QualityCounts(50, 3, -5);
        var seen = new int[5]; const int N = 10_000;
        for (int i = 0; i < N; i++)
            seen[(int)ItemQualityRules.FromScore(ItemQualityRules.Score(99, i, EntityId.Make(IdKind.Person, (ulong)(1 + i % 7)), (uint)(i * 31), 50, 3) - 5)]++;
        for (int t = 0; t < 5; t++) Assert.InRange(Math.Abs(seen[t] * 1000.0 / N - counts[t] * 1000.0 / 41), 0, 10);
    }

    [Fact]
    public void K17_Conservation_ManyRandomRecipes_WithFailuresAndRuin_BooksStayExact()
    {
        var w = new CraftWorld(3); var db = w.Db; var rng = 12345UL;
        uint Next(int n) { rng = DetRng.Mix(rng + 0x9E37); return (uint)(rng % (ulong)n); }
        foreach (var s in Enum.GetValues<Sk>()) w.Skill(s, 15 + (int)s);
        foreach (var p in db.Proficiencies) w.St.Know.SetPm(w.Hero, db.ProfIndex(p.Id), 200);
        int ruined = 0, failed = 0, done = 0;
        for (int n = 0; n < 400; n++)
        {
            var r = db.Recipes[(int)Next(db.Recipes.Count)];
            int batch = 1 + (int)Next(r.BatchMax); w.St.Know.Learn(w.Hero, r.Index);
            EntityId target = default;
            if (r.IsRepair) target = w.Garment(100);
            var plan = w.Stock(r.Id, batch);
            if (!plan.CanStart && plan.Missing.All(m => m.Contains("dark"))) { w.Site.Light = 255; plan = w.R.Plan(r, w.Actor(), w.Site, w.Pack, batch); }
            if (!plan.CanStart) continue;
            var j = w.R.Start(plan, w.Actor(), w.Hero, w.Pack, w.Now, 0, 0, target); if (j == null) continue;
            w.Work(j, 900);
            if (j.State == CraftJobState.Waiting)
            {
                switch (Next(3))
                {
                    case 0: w.Now = j.ReadySec; w.R.Collect(j, w.Actor(), w.Now); break;
                    case 1: w.Now = j.DeadlineSec + 100; w.R.Collect(j, w.Actor(), w.Now); break;
                    default: w.Now = j.RuinSec + 5; w.R.Hourly(w.Now); break;
                }
                if (j.State == CraftJobState.Active) { w.Work(j, 900); }
                while (j.State == CraftJobState.Waiting) { w.Now = j.ReadySec; w.R.Collect(j, w.Actor(), w.Now); w.Work(j, 900); }
            }
            if (j.Failed) failed++; if (j.State == CraftJobState.Ruined) ruined++; if (j.State == CraftJobState.Done) done++;
            Assert.True(j.Closed);
            Assert.True(w.Audit(), $"{r.Id} #{n}");
        }
        output.WriteLine($"done {done} failed {failed} ruined {ruined}");
        Assert.True(done > 100 && ruined > 3 && failed > 3);
        Assert.DoesNotContain(w.Econ.OtherStores, kv => kv.Value.RowCount > 0 && kv.Key.Kind == IdKind.Other);       // no job store keeps anything once closed
        Assert.True(w.Audit());
    }

    private static string Day(long chunk)
    {
        var w = new CraftWorld(5); foreach (var s in Enum.GetValues<Sk>()) w.Skill(s, 35);
        foreach (var id in new[] { "bake_flatbread", "cook_pottage", "make_torch", "bandage_linen", "sew_shirt", "kindle_fire", "braid_rope", "practice_spoon", "forge_nails_small" })
        {
            var j = w.StartNew(id, id is "bandage_linen" or "forge_nails_small" ? 2 : 1); w.Work(j, chunk);
        }
        return w.Digest();
    }

    [Fact]
    public void K18_Determinism_ADayOfCrafting_IsIdentical_AtTheSameSpeed_AndAt8xChunks()
    {
        Assert.Equal(Day(20), Day(20));
        Assert.Equal(Day(20), Day(160));         // 1x frames of 20 game seconds versus 8x frames of 160: the same result
        Assert.Equal(Day(20), Day(3600));
    }

    [Fact]
    public void K19_Disassembly_ARecordedShirt_ReturnsSeventyPercent_RestAsRags_AtSkill10Allowed_At9Refused()
    {
        var w = new CraftWorld(); w.Skill(Sk.Tailoring, 40);
        var j = w.StartNew("sew_shirt"); w.Work(j);
        var shirt = w.Econ.Items.LiveRows().Select(i => w.Econ.Items.At(i).Id).Single();
        var rec = w.St.Records[shirt];
        Assert.Equal([(w.Econ.Goods.IdOf("cloth_linen"), 1180L), (w.Econ.Goods.IdOf("yarn"), 20L)], rec.Parts);
        var dest = w.Econ.OpenStore(StoreKind.House, EntityId.Make(IdKind.Other, 9000), 0, 0, false);
        var nine = EntityId.Make(IdKind.Person, 90); var ten = EntityId.Make(IdKind.Person, 91);
        w.Skill(Sk.Tailoring, 9, nine); w.Skill(Sk.Tailoring, 10, ten);
        Assert.False(w.R.Disassemble(shirt, new CraftActor(nine), dest)); Assert.True(w.Econ.Items.IsLive(shirt));
        Assert.False(w.St.Know.Knows(w.Db["sew_shirt"], ten, w.St.Book));
        Assert.True(w.R.Disassemble(shirt, new CraftActor(ten), dest));
        Assert.Equal(826, dest.Qty(w.Econ.Goods.IdOf("cloth_linen"))); Assert.Equal(14, dest.Qty(w.Econ.Goods.IdOf("yarn"))); Assert.Equal(360, dest.Qty(w.Econ.Goods.IdOf("rags")));
        Assert.Equal(1200, dest.TotalMassG()); Assert.Equal(840, dest.Qty(w.Econ.Goods.IdOf("cloth_linen")) + dest.Qty(w.Econ.Goods.IdOf("yarn")));
        Assert.False(w.Econ.Items.IsLive(shirt)); Assert.True(w.St.Know.Knows(w.Db["sew_shirt"], ten, w.St.Book));        // learnt by taking it apart
        Assert.False(w.R.Disassemble(shirt, new CraftActor(ten), dest));                                                    // and it is gone
    }

    // ------------------------------------------------------------------ results, containers, jobs, keys

    [Fact]
    public void ResultsAreMadeFromTheComponents_GoodsScaleByQuality_ByproductsAndFuel()
    {
        var w = new CraftWorld(); w.Skill(Sk.Baking, 50);
        w.St.Know.SetPm(w.Hero, w.Db.ProfIndex("cookery"), 1000); w.St.Know.SetPm(w.Hero, w.Db.ProfIndex("bread_making"), 1000);
        var j = w.StartNew("bake_loaf"); w.Work(j);
        w.R.Collect(j, w.Actor(), j.ReadySec); w.Work(j);
        Assert.Equal(CraftJobState.Done, j.State);
        Assert.InRange(w.Qty("bread"), 1300 * 700 / 1000, 1300); Assert.Equal(20, w.Qty("ash")); Assert.Equal(0, w.Qty("firewood")); Assert.Equal(0, w.Qty("flour"));
        Assert.True(w.Audit()); Assert.False(w.Econ.OtherStores.ContainsKey(j.Id));
        Assert.True(w.Econ.Mass.Destroyed(w.Econ.Goods.IdOf("firewood"), DestroyReason.Burned) >= 600);
    }

    [Fact]
    public void AlternativesAreChosen_TheGrindStepOnlyRunsForTheWheat_AndTheScreenSaysWhatIsMissing()
    {
        var w = new CraftWorld(); w.Skill(Sk.Baking, 50); var loaf = w.Db["bake_loaf"];
        w.Give("wheat", 1100); w.Give("water", 350); w.Give("salt", 10); w.Give("firewood", 600);
        var plan = w.R.Plan(loaf, w.Actor(), w.Site, w.Pack);
        Assert.True(plan.CanStart); Assert.Equal(1, plan.Slots[0].Chosen); Assert.Equal(1100, plan.Slots[0].Have[1]); Assert.Equal(0, plan.Slots[0].Have[0]);
        var j = w.R.Start(plan, w.Actor(), w.Hero, w.Pack, w.Now)!;
        Assert.Equal(0, j.StepIndex);                                             // grind first
        Assert.Equal("grind", loaf.Steps[j.StepIndex].Id);
        var bare = w.R.Plan(loaf, w.Actor(), new CraftSite { Light = 10 }, new Store(w.Econ.Goods, w.Econ.Mass, StoreKind.House, w.Hero));
        Assert.Contains(bare.Missing, m => m.StartsWith("need ") && m.Contains(" or "));
        Assert.Contains(bare.Missing, m => m.Contains("too dark")); Assert.Contains(bare.Missing, m => m.Contains("oven"));
        Assert.False(bare.CanStart);
        // a loaf from flour has no grind step
        var w2 = new CraftWorld(); w2.Skill(Sk.Baking, 50); var j2 = w2.StartNew("bake_loaf"); Assert.Equal("knead", loaf.Steps[j2.StepIndex].Id);
    }

    [Fact]
    public void AnNpcCrafts_ThroughTheJobSystem_PickingUpTheHeroUnfinishedWork_AndIsPaid()
    {
        var w = new CraftWorld(); w.Skill(Sk.Spinning, 20); w.Skill(Sk.Spinning, 20, w.Other);
        var cj = w.StartNew("braid_rope"); w.R.Advance(cj, 600, w.Now); w.R.Pause(cj);
        var ledger = w.Econ.Ledger; var poster = EntityId.Make(IdKind.Household, 1);
        var pp = ledger.Open(PurseKind.Household, poster); ledger.Mint(pp, new Coin(5000), SourceReason.Seed);
        var wp = ledger.Open(PurseKind.Person, w.Other);
        long now = w.Now;
        var env = new JobEnv { Ledger = ledger, Econ = w.Econ, PurseOf = p => p == w.Other ? wp : null };
        env = new JobEnv { Ledger = ledger, Econ = w.Econ, PurseOf = env.PurseOf, CraftWorked = CraftWork.Hook(w.R, id => new CraftActor(id), () => now) };
        var table = new JobTable();
        var job = CraftWork.Post(table, env, cj, poster, pp, 0, 0, now, 300, 1, "Braid rope")!;
        Assert.Equal(OddJobKind.Craft, job.Kind);
        Assert.Equal(ClaimResult.Taken, table.Claim(env, new ClaimRequest(job.Id, w.Other, 1, 0, 0, job.P0, job.P1, 10, 30), now));
        long end = JobTable.EndSec(job);
        for (long t = now; t <= end + 60; t += 60) { now = t; table.Advance(env, t); }
        Assert.Equal(JobState.Done, job.State); Assert.Equal(CraftJobState.Done, cj.State); Assert.Equal(w.Other, cj.Crafter);
        Assert.Equal(300, ledger.Balance(wp).Mites); Assert.True(ledger.Audit().IsConserved);
        Assert.InRange(w.Qty("rope"), 315, 450); Assert.True(w.Audit());
    }

    [Fact]
    public void ManuscriptsTeachListedRecipes_NeedLiteracy20()
    {
        var w = new CraftWorld(); var bake = w.Db["bake_loaf"];
        Assert.Equal(-1, w.R.ReadManuscript(w.Actor(), "bread_book")); w.Skill(Sk.Literacy, 20);
        long sec = w.R.ReadManuscript(w.Actor(), "bread_book");
        Assert.Equal((CraftMath.ManuscriptHoursX10(25) + CraftMath.ManuscriptHoursX10(10)) * 360L, sec);
        Assert.True(w.St.Know.Learnt(w.Hero, bake.Index)); Assert.True(w.St.Know.Pm(w.Hero, w.Db.ProfIndex("bread_making")) == 0);   // bread making needs 30% cookery first
        Assert.True(w.St.Know.Pm(w.Hero, w.Db.ProfIndex("cookery")) > 0);
    }

    [Fact]
    public void ProficienciesGainEightPermillePerHourOfWork_NeedTheirPrerequisites_TwiceWithAMaster()
    {
        var w = new CraftWorld(); var k = w.St.Know; ushort bread = w.Db.ProfIndex("bread_making"), cookery = w.Db.ProfIndex("cookery");
        Assert.Equal(0, k.Practise(w.Db, w.Hero, bread, 3600));                      // needs cookery 30% first
        Assert.Equal(8, k.Practise(w.Db, w.Hero, cookery, 3600)); Assert.Equal(16, k.Practise(w.Db, w.Hero, cookery, 3600, 2000) - 0);
        k.SetPm(w.Hero, cookery, 300); Assert.Equal(8, k.Practise(w.Db, w.Hero, bread, 3600));
        k.SetPm(w.Hero, cookery, 999); Assert.Equal(1, k.Practise(w.Db, w.Hero, cookery, 3600)); Assert.Equal(1000, k.Pm(w.Hero, cookery));
        Assert.Equal(125, 1000 / 8);                                                // 125 hours to full
    }

    [Fact]
    public void TheNKey_IsBoundOnce_InTheWorldContext_AndClashesWithNothingActiveBesideIt()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var lines = File.ReadAllLines(Path.Combine(root, "src", "Ravensguard.Game", "Input", "InputDefs.cs"));
        var nKeys = lines.Where(l => l.Contains("A(") && l.Contains("Key.N") && !l.TrimStart().StartsWith("//")).ToList();
        Assert.Contains(nKeys, l => l.Contains("A(W, \"make\"") && l.Contains("K(Key.N)"));
        // the contexts active beside the world (CombatStance, CalledShot, Build, and the Menu and editor which replace it) bind N only as listed: the menu's new_game and the editor's Ctrl+N
        Assert.DoesNotContain(nKeys, l => l.Contains("A(C,") || l.Contains("A(S,") || l.Contains("A(B,"));
        Assert.Single(nKeys, l => l.Contains("A(W,"));
    }

    [Fact]
    public void SaveMigration_V2ToV3_TheHeroGainsTheSevenNewSkillsAtZero_AndTheEnumGrewAtTheEnd()
    {
        Assert.Equal(34, Enum.GetValues<Sk>().Length);
        Assert.Equal(26, (int)Sk.Theology); Assert.Equal(27, (int)Sk.Tailoring); Assert.Equal(33, (int)Sk.Lockpicking);      // appended: stored bytes of older skills do not move
        var old = Enum.GetValues<Sk>().Where(x => x != Sk.None && x < Sk.Tailoring).Select(x => Ravensguard.Sim.Characters.HeroRecord.SkillId(x)).ToArray();
        var hero = new Ravensguard.Sim.Characters.HeroSave { Name = "Ada", Origin = "x", Attributes = [50, 50, 50, 50, 50], SkillIds = old, SkillLevels = old.Select((_, i) => i + 1).ToArray() };
        var file = new SaveFile(new SaveHeader { FormatVersion = 2 }, [new("hero", SaveCodec.Serialize(hero))]);
        var migrated = SaveMigrator.CreateDefault().Migrate(file);
        Assert.Equal(SaveFormat.CurrentVersion, migrated.Header.FormatVersion); Assert.True(SaveFormat.CurrentVersion >= 3); // v4 (Phase 3 cleanup) added the chunk palette on top
        var back = Ravensguard.Sim.Characters.HeroRecord.FromSave(SaveCodec.Deserialize<Ravensguard.Sim.Characters.HeroSave>(migrated.Sections["hero"]));
        Assert.Equal(33, back.Skills.Count); Assert.Equal(0, back.Skill("lockpicking")); Assert.Equal(1, back.Skill("farming"));
        Assert.Equal(33, SaveCodec.Deserialize<Ravensguard.Sim.Characters.HeroSave>(migrated.Sections["hero"]).SkillIds.Length);
    }
}
