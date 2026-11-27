using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.WorldGen;

namespace Ravensguard.Sim.Tests.Hero;

/// <summary>P8-AT-1 (ambitions spec 6): AT-1 tree loads, AT-2 vocabulary, AT-3 progress, AT-4 offer, AT-5 requires, AT-6 save.</summary>
public class AmbitionTreeTests
{
    private static readonly Lazy<AmbitionTree> Loaded = new(() =>
    {
        var r = new ModLoader(ContentTypeRegistry.Default, BaseVocabulary.Create()).Load([ContinentSettings.FindBaseContent()!]);
        Assert.True(r.Success, r.Report.ToString());
        return new AmbitionTree(r.Database.All<AmbitionDef>());
    });
    private static AmbitionTree Tree => Loaded.Value;

    private static ConditionRefDef Cond(string id, params (string, string)[] args) => new() { Id = id, Args = args.ToDictionary(a => a.Item1, a => a.Item2) };

    [Fact]
    public void AT1_TreeLoads_30Ambitions_ResolveAcyclic_GreatNamesTheSixEndings()
    {
        Assert.Equal(30, Tree.All.Count);
        Assert.Empty(Tree.Problems());
        Assert.Equal((8, 16, 6), (Tree.All.Count(a => a.Tier == "start"), Tree.All.Count(a => a.Tier == "middle"), Tree.All.Count(a => a.Tier == "great")));
        Assert.Equal(AmbitionTree.Endings.OrderBy(x => x), Tree.All.Where(a => a.Tier == "great").Select(a => a.Ending).OrderBy(x => x));
        Assert.Equal(Tree.All.Count, Tree.All.Select(a => a.Id).Distinct().Count());
        // a cycle is caught
        var a = Tree.Get("master_a_trade")!; var b = Tree.Get("build_a_works")!;
        var cyc = new AmbitionTree([a with { Opens = ["build_a_works"] }, b with { Opens = ["master_a_trade"] }]);
        Assert.Contains(cyc.Problems(), p => p.Contains("cycle"));
    }

    [Fact]
    public void AT2_EveryConditionInTheTree_IsInTheVocabulary_AndHasAProgressRule()
    {
        var vocab = BaseVocabulary.Create();
        foreach (var a in Tree.All)
            foreach (var c in a.Requires.Concat(a.Steps.SelectMany(s => s.Conditions)))
            {
                Assert.True(vocab.IsCondition(c.Id), $"{a.Id}: {c.Id} not in the vocabulary");
                Assert.Contains(c.Id, AmbitionRules.Ruled);
            }
        foreach (var id in new[] { "holds_fief_rank", "rules_realm", "founded_faction", "dynasty_generations", "slew_monster", "owns_works_tier", "holds_guild_office", "holds_church_office", "owns_relic", "wealth_at_least", "leads_men_at_least", "won_battles" })
        {
            Assert.True(vocab.IsCondition(id)); Assert.Contains(id, AmbitionRules.Ruled);
        }
    }

    [Fact]
    public void AT3_Progress_IsTheLowestCondition()
    {
        var f = new AmbitionFacts { CoinPence = 300 }; f.Skills["trading"] = 20;
        var step = new AmbitionStepDef { Id = "s", Name = "s", Conditions = [Cond("has_coin_at_least", ("pence", "1000")), Cond("has_proficiency", ("skill", "trading"), ("level", "40"))] };
        Assert.Equal(300, AmbitionRules.StepProgress(step, f));
        Assert.False(AmbitionRules.StepDone(step, f));
        f.CoinPence = 5000; f.Skills["trading"] = 40;
        Assert.Equal(1000, AmbitionRules.StepProgress(step, f));
        Assert.Equal(0, AmbitionRules.Progress(Cond("rules_realm"), new AmbitionFacts()));
        Assert.Equal(333, AmbitionRules.Numeric(1, 3));
        Assert.Equal(500, AmbitionRules.Progress(Cond("wealth_at_least", ("mites", "10000000")), new AmbitionFacts { WealthMites = 5_000_000 }));
    }

    [Fact]
    public void AT4_FinishingMakeYourFortune_OffersItsOpensWhoseRequiresHold_AtMostFour_InOrder()
    {
        var f = new AmbitionFacts { CoinPence = 1000 }; f.Skills["trading"] = 40;
        f.Ranks.Add("guild_member");
        var offer = Tree.Offer("make_your_fortune", ["make_your_fortune"], null, f);
        Assert.Equal(["master_a_trade", "build_a_works", "grow_a_trade_route", "win_the_guild_office"], offer);
        // a requires that fails drops the open: no guild rank, no guild office offer; the next middle fills in
        f.Ranks.Clear();
        offer = Tree.Offer("make_your_fortune", ["make_your_fortune"], null, f);
        Assert.DoesNotContain("win_the_guild_office", offer);
        Assert.InRange(offer.Count, 1, AmbitionTree.MaxOffered);
        Assert.Equal(["master_a_trade", "build_a_works", "grow_a_trade_route"], offer.Take(3));
        // a done ambition is never offered again
        Assert.DoesNotContain("master_a_trade", Tree.Offer("make_your_fortune", ["make_your_fortune", "master_a_trade"], null, f));
    }

    [Fact]
    public void AT5_TakeACrown_IsNotOfferedWithoutATitledFief()
    {
        var f = new AmbitionFacts { CoinPence = 5000 }; f.Reputation["district"] = 50;
        Assert.DoesNotContain("take_a_crown", Tree.Offer("earn_a_fief", ["earn_a_fief"], null, f));
        f.FiefRank = 1;
        Assert.Contains("take_a_crown", Tree.Offer("earn_a_fief", ["earn_a_fief"], null, f));
    }

    [Fact]
    public void AT6_History_RoundTripsByteIdentical_AndAnOldSaveLoadsEmpty()
    {
        var db = new ModLoader(ContentTypeRegistry.Default, BaseVocabulary.Create()).Load([ContinentSettings.FindBaseContent()!]).Database;
        var h = HeroRecord.FromOrigin("Ysolde", true, db.Get<OriginDef>("minor_heir"));
        var plain = SaveCodec.Serialize(h.ToSave());
        Assert.Empty(HeroRecord.FromSave(SaveCodec.Deserialize<HeroSave>(plain)).AmbitionsDone);

        h.FinishAmbition(40, ["master_a_trade", "hold_a_manor"]);
        h.ChooseAmbition("master_a_trade");
        h.FinishAmbition(90, ["build_a_works", "win_the_guild_office"]);
        h.ChooseAmbition("build_a_works"); h.ChooseAmbition("hold_a_manor");
        Assert.Equal([("claim_your_inheritance", 40), ("master_a_trade", 90)], h.AmbitionsDone);
        Assert.Equal(["build_a_works"], h.Dropped);
        h.FinishAmbition(120, ["found_a_house"]);
        var bytes = SaveCodec.Serialize(h.ToSave());
        var back = HeroRecord.FromSave(SaveCodec.Deserialize<HeroSave>(bytes));
        Assert.Equal(h.AmbitionsDone, back.AmbitionsDone);
        Assert.Equal(h.Offered, back.Offered);
        Assert.Equal(h.Dropped, back.Dropped);
        Assert.Equal(bytes, SaveCodec.Serialize(back.ToSave()));
        // an old save: the 13 original keys (no history) load with an empty history
        var old = SaveCodec.Serialize(HeroRecord.FromOrigin("Old", false, db.Get<OriginDef>("minor_heir")).ToSave());
        var oldBack = HeroRecord.FromSave(SaveCodec.Deserialize<HeroSave>(old));
        Assert.Empty(oldBack.AmbitionsDone); Assert.Empty(oldBack.Offered); Assert.Empty(oldBack.Dropped);
    }
}
