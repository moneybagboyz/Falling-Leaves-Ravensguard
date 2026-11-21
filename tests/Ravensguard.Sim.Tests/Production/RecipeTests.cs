using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Production;

/// <summary>Recipe content: mass check at load (G-B2), the spec's chains with exact numbers (G-B4, G-B5, G-B15), the price solver (G-B6, G-B7, G-B12).</summary>
public class RecipeTests
{
    private static readonly GoodCatalog Cat = EconFixture.Catalog();
    private static RecipeDef Def(string id, string station, (string good, double kg)[] ins, (string good, double kg)[] outs, double adds = 0, int hours = 5)
        => new() { Id = id, Name = id, Station = station, WorkerHours = hours, Inputs = [.. ins.Select(i => new RecipeIoDef { Good = i.good, Kg = i.kg })], Outputs = [.. outs.Select(o => new RecipeIoDef { Good = o.good, Kg = o.kg })], AddsKg = adds };

    [Fact]
    public void ContentLoads_AllRecipesBusinessesAndExtractions()
    {
        var c = ProdKit.Content;
        Assert.True(c.Recipes.Recipes.Count >= 40, $"{c.Recipes.Recipes.Count} recipes");
        Assert.Contains(c.Kinds, k => k.Id == "bakery");
        Assert.True(c.Extractions.Length >= 10);
        // the chains named in the plan all have recipes
        foreach (var id in new[] { "mill_flour", "bake_bread", "malt_barley", "brew_ale", "burn_charcoal", "smelt_bloom", "forge_bar", "forge_tools", "spin_yarn", "weave_cloth", "full_cloth", "dye_cloth", "tailor_clothes", "tan_hides",
                     "fire_bricks", "salt_fish", "salt_meat", "smelt_copper", "smelt_tin", "make_bronze", "smelt_galena", "cupel_silver", "make_glass", "saw_planks" })
            Assert.True(c.Recipes.TryGet(id, out _), id);
    }

    [Fact]
    public void GB2_MassCheck_ShippedPasses_LegacyBugFailsWithReadableError()
    {
        Assert.Empty(RecipeBook.Validate(Def("weave_cloth", "loom", [("yarn", 1.6)], [("cloth_wool", 1.5)]), Cat, out var ok));
        Assert.NotNull(ok);
        var errs = RecipeBook.Validate(Def("legacy_weave", "loom", [("wool_raw", 2)], [("cloth_wool", 6)]), Cat, out var bad);
        Assert.Null(bad);
        var msg = Assert.Single(errs);
        Assert.Contains("legacy_weave", msg); Assert.Contains("6 kg", msg); Assert.Contains("2 kg", msg); Assert.Contains("cannot appear from nowhere", msg);
        // water and yeast count only through adds_kg
        Assert.Empty(RecipeBook.Validate(Def("bake", "oven", [("flour", 100), ("firewood", 20)], [("bread", 130)], adds: 30), Cat, out _));
        Assert.NotEmpty(RecipeBook.Validate(Def("bake2", "oven", [("flour", 100), ("firewood", 20)], [("bread", 130)], adds: 9), Cat, out _));
        Assert.Throws<InvalidDataException>(() => RecipeBook.Build([Def("legacy_weave", "loom", [("wool_raw", 2)], [("cloth_wool", 6)])], Cat));
        // unknown goods and stations are reported, not thrown away silently
        Assert.Contains(RecipeBook.Validate(Def("x", "loom", [("nothing", 1)], [("yarn", 1)]), Cat, out _), e => e.Contains("unknown input good"));
        Assert.Contains(RecipeBook.Validate(Def("y", "nowhere", [("yarn", 1)], [("yarn", 1)]), Cat, out _), e => e.Contains("unknown station"));
    }

    [Fact]
    public void EveryShippedRecipe_PassesTheMassCheck_AndRespectsItsUnits()
    {
        foreach (var r in ProdKit.Recipes.Recipes)
        {
            Assert.True(r.OutputMassG(Cat) <= r.InputMassG(Cat) + r.AddsG, r.Id);
            Assert.True(r.WorkerHours > 0 && r.MaxWorkers > 0, r.Id);
            Assert.Contains(ProdKit.Content.Kinds, k => k.Recipes.Any(x => x.Id == r.Id));   // no orphan recipes
        }
    }

    [Fact]
    public void GB4_Spinning_TenSpinnerHoursMakeTwoHundredGrams_EightSpinnersFeedOneWeaver()
    {
        var spin = ProdKit.Recipes["spin_yarn"]; var weave = ProdKit.Recipes["weave_cloth"];
        long wool = spin.Inputs[0].Qty, yarn = spin.Outputs[0].Qty;
        Assert.Equal(100, spin.WorkerHours);
        Assert.Equal(2200, wool); Assert.Equal(2000, yarn);                           // 2.2 kg wool -> 2.0 kg yarn in 100 spinner-hours
        Assert.Equal(200, yarn * 10 / spin.WorkerHours);                              // 10 spinner-hours -> 0.2 kg
        Assert.Equal(220, wool * 10 / spin.WorkerHours);                              // from 0.22 kg wool
        Assert.Equal(weave.Inputs[0].Qty, 8 * yarn * 10 / spin.WorkerHours);           // 8 spinners x 10 h = 1.6 kg = one weaver-day
        Assert.Equal(750, 150 * 1000 / (yarn * 10 / spin.WorkerHours) * 1 / 1);         // put-out pay: 150 mites a day for 0.2 kg = 750 mites/kg
    }

    [Fact]
    public void GB5_IronChain_ThousandKgOre_ExactBatchCounts()
    {
        var smelt = ProdKit.Recipes["smelt_bloom"]; var bar = ProdKit.Recipes["forge_bar"]; var burn = ProdKit.Recipes["burn_charcoal"];
        long oreG = 1_000_000;
        long smelts = oreG / smelt.Inputs.First(i => Cat.StringId(i.Good) == "iron_ore").Qty;
        Assert.Equal(25, smelts);
        long bloomG = smelts * smelt.Outputs[0].Qty; Assert.Equal(150_000, bloomG);
        long bars = bloomG / bar.Inputs[0].Qty; Assert.Equal(25, bars);
        long ironG = bars * bar.Outputs[0].Qty; Assert.Equal(120_000, ironG);
        long charcoalG = smelts * smelt.Inputs.First(i => Cat.StringId(i.Good) == "charcoal").Qty + bars * bar.Inputs.First(i => Cat.StringId(i.Good) == "charcoal").Qty;
        Assert.Equal(1_750_000, charcoalG);
        Assert.Equal(87.5, charcoalG / (double)burn.Outputs[0].Qty);                    // 87.5 burns
        Assert.Equal(8_750_000, charcoalG / burn.Outputs[0].Qty * burn.Inputs[0].Qty + (charcoalG % burn.Outputs[0].Qty) * burn.Inputs[0].Qty / burn.Outputs[0].Qty);   // 8,750 kg logs
        double hours = smelts * smelt.WorkerHours + bars * bar.WorkerHours + charcoalG / (double)burn.Outputs[0].Qty * burn.WorkerHours;
        Assert.Equal(1500.0, hours);                                                   // 250 + 200 + 1,050
        Assert.Equal(5, burn.Inputs[0].Qty / burn.Outputs[0].Qty);                      // 5 kg wood : 1 kg charcoal
    }

    [Fact]
    public void GB15_SilverChain_ThousandKgGalena_150gSilver_115CrownsStruck()
    {
        var smelt = ProdKit.Recipes["smelt_galena"]; var cupel = ProdKit.Recipes["cupel_silver"];
        Assert.Equal(1_000_000, smelt.Inputs.First(i => Cat.StringId(i.Good) == "galena").Qty);
        Assert.Equal(600_000, smelt.Outputs[0].Qty);                                    // 600 kg argentiferous lead
        Assert.Equal(600_000, cupel.Inputs.First(i => Cat.StringId(i.Good) == "lead_argentiferous").Qty);
        long silverG = cupel.Outputs.First(o => Cat.StringId(o.Good) == "silver").Qty;
        Assert.Equal(150, silverG);                                                     // 0.15 kg = 150 g per tonne of galena (P7-EA3c, EA_F4: 0.015%)
        Assert.Equal(540_000, cupel.Outputs.First(o => Cat.StringId(o.Good) == "lead").Qty);
        var strike = MintMath.Strike(silverG * 1000, 750);
        Assert.Equal(115, strike.Struck.WholeCrowns);
    }

    [Fact]
    public void GB6_GB7_PriceSolver_FlourBread_AndTheBakerProfitIs45PercentOfWages()
    {
        var raw = new Dictionary<string, double> { ["wheat"] = 45, ["firewood"] = 1 };
        var p = PriceSolver.Solve(ProdKit.Recipes, raw);
        double flour = p[Cat.IdOf("flour")], bread = p[Cat.IdOf("bread")];
        Assert.Equal(48.93, flour, 2);                                                  // (100 x 45 + 1.45 x 1 x 35) / 93
        Assert.Equal(40.14, bread, 2);                                                  // (100 x flour + 20 + 1.45 x 6 x 35) / 130
        var (rev, inputs, wages, profit) = PriceSolver.BatchEconomics(ProdKit.Recipes, ProdKit.Recipes["bake_bread"], p);
        Assert.Equal(5217.8, rev, 0); Assert.Equal(4913.3, inputs, 0); Assert.Equal(210, wages, 6); Assert.Equal(94.5, profit, 1);
        Assert.Equal(0.45, profit / wages, 3);
    }

    /// <summary>Recipes that share a co-product or make pinned silver are priced by another recipe's solution, so the 45% rule is exact only for the owning recipes.</summary>
    private static readonly HashSet<string> BandExempt = ["slaughter_pig", "slaughter_sheep", "cupel_silver", "forge_sword", "burn_charcoal", "smelt_bloom", "forge_bar", "saw_planks", "fire_bricks", "lay_rope", "refine_gold"];   // P7-LB2: lay_rope makes rope, whose price is the audit's (R1, EA_F6)
    /// <summary>
    /// Goods whose price comes from elsewhere: silver is pegged to the crown, the sword to the items spec value, and (P7-EA3a, the R1 rule) the five finishing goods of the industries' worked
    /// examples (charcoal 12, iron bloom 240, iron bar 480, planks 3.0, bricks 1.2: economy-audit 4.8, industries 5.11), which the user's R1 table sets so that raw goods earn 1.0 to 1.5 times their
    /// wage and the profit of a works is in finishing; their recipes earn more than the 45% band at those prices (the band-exempt list), every good derived from them is still solved.
    /// </summary>
    public static readonly HashSet<string> Pinned = ["silver", "arming_sword", "charcoal", "iron_bloom", "iron_bar", "timber_planks", "bricks", "rope", "gold"];   // gold: pegged to the coin like silver (P7-CN1, 1 gp = 1,000 mites)   // rope: EA_F6 set it from twine (298 a kg); the ropewalk's recipe feeds it

    [Fact]
    public void GB12_RecipeMarginBand_EveryRecipeAtSolverPrices_EarnsFortyFivePercentOfWages()
    {
        var p = PriceSolver.Solve(ProdKit.Recipes, null, Pinned);
        int checkedCount = 0;
        foreach (var r in ProdKit.Recipes.Recipes)
        {
            if (BandExempt.Contains(r.Id)) continue;
            var (_, _, wages, profit) = PriceSolver.BatchEconomics(ProdKit.Recipes, r, p);
            Assert.InRange(profit / wages, 0.44, 0.46);
            checkedCount++;
        }
        Assert.True(checkedCount >= 35);
    }

    [Fact]
    public void ContentPrices_MatchTheSolverForDerivedGoods()
    {
        var p = PriceSolver.Solve(ProdKit.Recipes, null, Pinned);
        var derived = new HashSet<ushort>(); foreach (var r in ProdKit.Recipes.Recipes) foreach (var (g, _) in r.Outputs) derived.Add(g);
        foreach (ushort g in derived)
        {
            if (Pinned.Contains(Cat.StringId(g))) continue;
            double content = Cat.Def(g).BasePriceMitesPerKg;
            Assert.True(Math.Abs(content - p[g]) <= Math.Max(0.02, p[g] * 0.0006), $"{Cat.StringId(g)}: content {content} solver {p[g]:0.####}");
        }
    }

    [Fact]
    public void Dividend_GB8_Wage_GB9_Hiring_GB13()
    {
        Assert.Equal(0, Labour.Dividend(20_000, 700, 1_300, 50));                        // reserve 15 x 2,000 = 30,000
        Assert.Equal(10_000, Labour.Dividend(50_000, 700, 1_300, 50));
        int offer = 350, wage = 350; var seen = new List<int>();
        for (int i = 0; i < 3; i++) { offer = Labour.NextOffer(wage, offer); seen.Add(offer); }
        Assert.Equal([367, 385, 404], seen);
        for (int i = 0; i < 20; i++) offer = Labour.NextOffer(wage, offer);
        Assert.Equal(525, offer);                                                        // capped at 1.5 x
        int down = 350; for (int i = 0; i < 40; i++) down = Labour.NextOfferDown(wage, down);
        Assert.Equal(280, down);                                                         // floored at 0.8 x
        Assert.Equal([4, 6], Labour.Hire(2, [(9, 40), (4, 70), (6, 70)]));
        Assert.Equal([4], Labour.Hire(1, [(6, 70), (9, 40), (4, 70)]));
        Assert.Empty(Labour.Hire(0, [(1, 10)]));
    }
}
