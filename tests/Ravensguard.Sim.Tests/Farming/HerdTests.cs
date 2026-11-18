using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Farming;

namespace Ravensguard.Sim.Tests.Farming;

/// <summary>Herds as owned stock tied to farming (resources spec 3.3, 3.4; G-R5, G-R6, G-R14; integration decision I3): wool, cheese, calving, cull products, shares by acres.</summary>
public class HerdTests
{
    private static FarmingSettingsDefOverride Herds(Func<FarmingSettingsDef, FarmingSettingsDef>? more = null) => def =>
    {
        var d = def with { CowsPer100FieldAcres = 50, CowHayKgPerHeadDayX10 = 50, WoolGPerSheepYear = 164, CheeseGPerCowYear = 13_100, CowCalvingPermilleYear = 400, CowPerFieldAcreCapX100 = 80, OxenMortalityPermilleYear = 0 };
        return more == null ? d : more(d);
    };

    [Fact]
    public void SpringShearing_GivesEverySheep164Grams_SharedByAcresHeld_G_R6Wool()
    {
        var w = Synth.Build(new SynthSpec(Sheep: 40), Herds());
        w.Run(w.Year, 1, 1);
        Assert.Equal(40 * 164, w.Total("wool_raw"));
        // a tenth of the wool is the parish's tithe (taxes spec 3.3, G-T4: 164 kg of wool, 16.4 kg tithe); the holders share the rest
        long tithe = 40 * 164 / 10, held = 40 * 164 - tithe;
        var holders = Enumerable.Range(0, w.State.Parcels.Count).Select(p => w.State.Parcels.Holder[p]).Distinct().Where(h => h >= 0).ToList();
        Assert.All(holders, h => Assert.InRange(w.Econ.HouseholdStore[h].Qty(w.Good("wool_raw")), held / holders.Count - 1, held / holders.Count + holders.Count));
        Assert.Equal(tithe, Enumerable.Range(0, w.Region.Settlements.Count).Sum(s => w.Econ.OtherStores.TryGetValue(FarmIds.ParishGranary(s), out var g) ? g.Qty(w.Good("wool_raw")) : 0));
        Assert.True(w.MassAudit());
    }

    [Fact]
    public void TheDairy_MakesAGameYearsCheesePerCow_AtTheStartOfAutumn()
    {
        var w = Synth.Build(new SynthSpec(), Herds());
        int cows = w.State.Livestock.Cows[0]; Assert.True(cows > 0);
        w.Run(w.Year, 30, 1);
        Assert.Equal(0, w.Total("cheese"));
        w.Run(w.Year, 31, 1);
        Assert.Equal(cows * 13_100L, w.Total("cheese"));
        Assert.True(w.MassAudit());
    }

    [Fact]
    public void WithoutFodder_SheepAndCowsAreCulled_AndTheCarcassesBecomeLivePiecesForTheirHolders_G_R5()
    {
        var w = Synth.Build(new SynthSpec(Sheep: 100), Herds());
        int sheep0 = w.State.Livestock.Sheep[0], cows0 = w.State.Livestock.Cows[0];
        w.Run(w.Year, 1, 46);                                           // no meadow, no hay: the autumn-end cull at day 46
        int culledSheep = sheep0 - w.State.Livestock.Sheep[0], culledCows = cows0 - w.State.Livestock.Cows[0];
        Assert.True(cows0 > 0 && culledSheep > 0 && culledCows > 0, $"culled {culledSheep} of {sheep0} sheep, {culledCows} of {cows0} cows");   // review finding 16: was "culledCows >= 0"
        Assert.Equal(culledSheep, (int)w.Total("sheep"));               // the pieces are in the holders' stores
        Assert.Equal(culledCows, (int)w.Total("cow"));
        Assert.True(w.MassAudit());
    }

    [Fact]
    public void BullCalves_RefillTheOxenTowardThePloughTarget_AtTheNextYear_AndMortalityTakesSome()
    {
        var w = Synth.Build(new SynthSpec(ParcelsPerSlot: 6, FieldsPerParcel: 20), Herds(d => d with { OxenMortalityPermilleYear = 500 }));
        int target = (w.Region.Fields.Length * 3 + 50) / 100;
        w.State.Livestock.Oxen[0] = target / 2;
        int cows = w.State.Livestock.Cows[0];
        w.Run(w.Year, 1, 60); w.Run(w.Year + 1, 1, 1);                  // through the year and into the next day 1
        Assert.Equal(target, w.State.Livestock.Oxen[0]);                  // 36 bull calves, 11 needed
        Assert.True(w.Total("cow") > 0, "the surplus calves go to the butcher");
        Assert.True(cows > 0 && w.State.Livestock.Cows[0] < cows);                // no meadow: the day-46 cull took part of the herd and, above the cap, no heifer was kept
        Assert.True(w.MassAudit());
    }

    [Fact]
    public void WithTheProductKeysOff_TheHerdsMakeNothing_AsBefore()
    {
        var w = Synth.Build(new SynthSpec());
        w.Run(w.Year, 1, 60);
        Assert.Equal(0, w.Total("wool_raw") + w.Total("cheese") + w.Total("sheep") + w.Total("cow"));
    }
}
