using Ravensguard.Content;
using Ravensguard.Sim.Tests.Dialogue;
using Xunit;

namespace Ravensguard.Sim.Tests.BalancePass;

/// <summary>P8-BL-1: no piece of the levy kit is priced under its own materials at the books' base prices (the gambeson's 3.2 kg of linen at 1,400 mites a kilogram cost 4,480 against a price of 1,500).</summary>
public class Bl1KitPriceTests
{
    [Fact]
    public void EveryPieceOfTheLevyKit_IsPricedAboveItsMaterialsAtBasePrices()
    {
        var db = TalkKit.LoadBase();
        var rules = db.All<ArmsRulesDef>().Single();
        double Price(string good) => db.All<GoodDef>().Single(g => g.Id == good).BasePriceMitesPerKg;
        foreach (var p in rules.LevyKit)
        {
            double materials = p.IronG * Price("iron_bar") / 1000 + (p.SoftG > 0 ? p.SoftG * Price(p.SoftGood) / 1000 : 0);
            Assert.True(p.PriceMites > materials, $"{p.Shape}: price {p.PriceMites} against {materials} of materials");
        }
        Assert.Equal(4, rules.LevyKit.Count);
        Assert.True(rules.LevyKit.Single(p => p.Shape == "gambeson").PriceMites >= 4_480 + 400, "the gambeson pays its cloth and a worker's hours");
    }
}
