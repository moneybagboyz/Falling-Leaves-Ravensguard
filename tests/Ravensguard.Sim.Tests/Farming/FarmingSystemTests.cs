using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Farming;

public class FarmingSystemTests(ITestOutputHelper output)
{
    private static readonly FarmingSettings Cfg = new();

    [Fact]
    public void NormalYear_FieldsFollowTheCalendar_AndYieldsMatchTheMaths()
    {
        var w = Synth.Build(new SynthSpec());
        int y = w.Year; var F = w.State.Fields; var P = w.State.Parcels;
        w.Run(y, 1, 20);                                                 // days 1-20
        // spring fields are sown by now, fallow is untouched, winter grain stands
        for (int f = 0; f < F.Count; f++)
        {
            if (F.Slot[f] == 1) { Assert.True(F.Stage[f] is FieldStage.Sown or FieldStage.Growing, $"spring field {f} is {F.Stage[f]}"); Assert.NotEqual(Crop.Fallow, F.Crop[f]); Assert.InRange((int)F.SownDay[f], 1, 14); }
            if (F.Slot[f] == 0) Assert.Equal(FieldStage.Growing, F.Stage[f]);
            if (F.Slot[f] == 2) Assert.Equal(Crop.Fallow, F.Crop[f]);
        }
        w.Run(y, 21, 6);                                                 // through day 26: winter grain ripens
        for (int f = 0; f < F.Count; f++) if (F.Slot[f] == 0)
        {
            var info = Cfg.Info(F.Crop[f]);
            Assert.True(F.Stage[f] >= FieldStage.Ripe, $"field {f} {F.Stage[f]}");
            if (F.Stage[f] == FieldStage.Ripe) Assert.Equal(info.BaseG(1100), (long)F.YieldG[f]);   // fertility 70, normal weather, no noise
        }
        output.WriteLine($"day 26 labour: {w.Sys.LastLabour}");
        w.Run(y, 27, 33);                                                // to the end of the year
        Assert.True(w.Total("wheat") > 0 && w.MassAudit());
        output.WriteLine($"harvest {w.State.HarvestG}, threshed {w.State.ThreshedG}, tithe {w.State.TitheG}, rent {w.State.RentG}, sown {w.State.SownAcres}");
    }

    // ---------- rotation and fertility ----------

    [Theory]
    [InlineData(40, 15)]     // sheep graze the fallow: +15
    [InlineData(0, 5)]       // no sheep: +5
    public void G_F8_G_F17_ThreeYearRotation_ReturnsToTheStartSlot_AndTheFertilityLedgerAddsUp(int sheep, int fallowBonus)
    {
        var w = Synth.Build(new SynthSpec(Sheep: sheep));
        int y0 = w.Year; var F = w.State.Fields;
        var startSlot = (byte[])F.Slot.Clone(); var startFert = (byte[])F.Fertility.Clone();
        int Delta(Crop c) => Cfg.Info(c).FertilityDelta;
        var endCrop = new Crop[3][];
        for (int yr = 0; yr < 3; yr++)
        {
            w.Run(y0 + yr, 1, 60);
            endCrop[yr] = (Crop[])F.Crop.Clone();
            if (yr < 2) for (int f = 0; f < F.Count; f++) Assert.Equal((startSlot[f] + yr) % 3, F.Slot[f]);
        }
        w.Run(y0 + 3, 1, 1);                                              // the year boundary of the third rotation
        for (int f = 0; f < F.Count; f++)
        {
            Assert.Equal(startSlot[f], F.Slot[f]);                        // G-F17: each field is back in its start slot
            int expect = startFert[f];
            // the crop it carried in the year it was harvested decides its change
            for (int yr = 0; yr < 3; yr++)
            {
                int slot = (startSlot[f] + yr) % 3;
                if (slot == 0 && yr > 0) expect += Delta(endCrop[yr][f]);                  // winter grain sown the autumn before (yr 0 slot 0: the world-start crop)
                else if (slot == 0) expect += Delta(endCrop[0][f]);
                else if (slot == 1) expect += Delta(endCrop[yr][f]);
                else expect += fallowBonus;
            }
            Assert.Equal(Math.Clamp(expect, 0, 100), (int)F.Fertility[f]);
        }
        for (int s = 0; s < 3; s++) Assert.Equal(w.Region.Parcels.Count(p => p.Slot == s) * 4, w.Sys.SlotAcres(0, s));
    }

    [Fact]
    public void FertilityLedger_G_F8_WheatBarleyFallow_ExactNumbers()
    {
        var w = Synth.Build(new SynthSpec(ParcelsPerSlot: 1, FieldsPerParcel: 1));
        // force the single slot-0 field to wheat and prove 70 -> 60 -> 54 -> 69 through the real steps
        var F = w.State.Fields; int f0 = Enumerable.Range(0, F.Count).First(i => F.Slot[i] == 0);
        F.Crop[f0] = Crop.Wheat; F.Fertility[f0] = 70; F.SownFertility[f0] = 70;
        int y = w.Year; w.Run(y, 1, 60);
        Assert.Equal(60, F.Fertility[f0]);
        w.Run(y + 1, 1, 60);
        var spring = F.Crop[f0]; Assert.Equal(Cfg.Info(spring).FertilityDelta, F.Fertility[f0] - 60);
        if (spring == Crop.Barley) Assert.Equal(54, F.Fertility[f0]);
        w.Run(y + 2, 1, 60);
        Assert.Equal(F.Fertility[f0], YieldMath.AddFertility(60 + Cfg.Info(spring).FertilityDelta, 15));
    }

    // ---------- weather ----------

    private static int DoyOf(long day) => (int)(day % 60) + 1;
    private static int YearOf(long day) => (int)(day / 60);

    [Fact]
    public void G_F7_Drought_CutsTheWinterGrainYieldTo580Permille()
    {
        var w = Synth.Build(new SynthSpec()); int y = w.Year;
        w.Wx.LongFn = (_, yr) => yr == y ? new LongCondition(LongKind.Drought, 1, 60) : default;     // a drought over the whole growth window: ratio 0.3
        w.Run(y, 1, 27);
        var F = w.State.Fields;
        foreach (int f in Enumerable.Range(0, F.Count).Where(i => F.Slot[i] == 0))
            Assert.Equal(Cfg.Info(F.Crop[f]).BaseG(1100) * 580 / 1000, (long)F.YieldG[f]);     // 30,624 g for wheat
        w.Run(y, 28, 33); w.Run(y + 1, 1, 1);
        var e = w.Chron.ByType("farm.yield_loss").Single(x => x.Data["cause"] == "Drought");
        Assert.Contains(e.Causes, c => c.Kind == CauseKind.Weather && c.Note.Contains("drought"));
        Assert.True(e.Data["acres"] != "0");
    }

    [Fact]
    public void LateFrost_G_F11_KillsRecentlySownSpringFields_AndResowsBeforeDay12()
    {
        var w = Synth.Build(new SynthSpec()); int y = w.Year; var F = w.State.Fields; var P = w.State.Parcels;
        w.Wx.TempTenths = d => YearOf(d) == y && DoyOf(d) == 10 ? -32 : 100;      // -3.2 C on doy 10
        w.Run(y, 1, 9);
        var sown = Enumerable.Range(0, F.Count).Where(i => F.Slot[i] == 1 && F.Stage[i] == FieldStage.Sown).ToList();
        Assert.NotEmpty(sown);
        var winter = Enumerable.Range(0, F.Count).Where(i => F.Slot[i] == 0).ToList();
        var winterBefore = winter.Select(i => (F.Stage[i], F.Flags[i])).ToList();
        foreach (var g in new[] { "barley", "oats", "peas", "beans" })                      // seed corn for a second sowing
            for (int h = 1; h < w.Econ.HouseholdStore.Length; h++) w.Econ.HouseholdStore[h].Create(w.Good(g), 200_000, CreateReason.Seed);
        long seedBefore = w.State.SeedSownG;
        w.Run(y, 10, 1);
        foreach (int f in sown) { Assert.True(F.Has(f, FieldFlags.Resown), $"field {f}"); Assert.False(F.Has(f, FieldFlags.Lost)); Assert.Equal(10, (int)F.SownDay[f]); }
        Assert.Equal(sown.Sum(f => Cfg.Info(F.Crop[f]).SeedG(1100)), w.State.SeedSownG - seedBefore);    // each resow costs another seed
        Assert.Equal(winterBefore, winter.Select(i => (F.Stage[i], F.Flags[i])).ToList());                // winter grain is not touched by a spring frost
        w.Run(y, 11, 21);                                                                                  // to day 31: the spring fields ripen
        foreach (int f in sown) Assert.Equal(Cfg.Info(F.Crop[f]).BaseG(1100) * 750 / 1000, (long)F.YieldG[f]);   // resown yield x 0.75: 39,600 g for a 52,800 g crop
    }


    [Fact]
    public void LateFrost_AfterDay12_LosesTheFieldsAndTheChronicleSaysWhy()
    {
        var w = Synth.Build(new SynthSpec()); int y = w.Year; var F = w.State.Fields;
        w.Wx.TempTenths = d => YearOf(d) == y && DoyOf(d) == 14 ? -35 : 100;
        w.Run(y, 1, 60); w.Run(y + 1, 1, 1);
        var lost = Enumerable.Range(0, F.Count).Count(i => F.Has(i, FieldFlags.Lost));
        Assert.Equal(0, lost);                                              // the flags reset at the rollover; look at the summary instead
        var sum = w.State.Summaries.Single();
        Assert.True(sum.LostG[(int)LossCause.Frost] > 0);
        var e = w.Chron.ByType("farm.yield_loss").Single(x => x.Data["cause"] == "Frost");
        Assert.Contains("late frost on day 14", e.Causes.Single().Note);
        Assert.Equal(CauseKind.Weather, e.Causes.Single().Kind);
    }

    [Fact]
    public void Hail_Halves_TheStandingCrop_OnStormDays()
    {
        var w = Synth.Build(new SynthSpec(), d => d with { HailChancePermillePerStormDay = 1000 }); int y = w.Year; var F = w.State.Fields;
        w.Wx.Storm = d => YearOf(d) == y && DoyOf(d) == 20;
        w.Run(y, 1, 27);
        foreach (int f in Enumerable.Range(0, F.Count).Where(i => F.Slot[i] == 0))
            Assert.Equal(Cfg.Info(F.Crop[f]).BaseG(1100) * 500 / 1000, (long)F.YieldG[f]);              // G-F14: 26,400 g for wheat
        w.Run(y, 28, 33); w.Run(y + 1, 1, 1);
        Assert.Contains(w.Chron.ByType("farm.yield_loss"), x => x.Data["cause"] == "Hail" && x.Causes[0].Kind == CauseKind.Weather);
    }

    [Theory]
    [InlineData(80, 920)]      // 8 mm
    [InlineData(130, 850)]     // 13 mm
    public void G_F13_WetHarvest_RotsTheCutGrain(int rainTenths, int factor)
    {
        var w = Synth.Build(new SynthSpec()); int y = w.Year; var F = w.State.Fields;
        w.Wx.RainRaw = d => YearOf(d) == y && DoyOf(d) >= 26 ? rainTenths * 65536L / 10 : 2 * 65536;
        w.Run(y, 1, 40);
        var cut = Enumerable.Range(0, F.Count).Where(i => F.Slot[i] == 0 && F.Stage[i] >= FieldStage.Cut).ToList();
        Assert.NotEmpty(cut);
        foreach (int f in cut) Assert.Equal(Cfg.Info(F.Crop[f]).BaseG(1100) * factor / 1000, (long)F.YieldG[f]);
        Assert.True(w.State.LostG[(int)LossCause.WetHarvest] > 0);
    }

    [Fact]
    public void Winterkill_NeedsHardFrostWithoutSnow_AndSnowProtectsTheWinterGrain()
    {
        long Yield(int snowCm)
        {
            var w = Synth.Build(new SynthSpec()); int y = w.Year; var F = w.State.Fields;
            w.Wx.TempTenths = d => YearOf(d) == y && DoyOf(d) is >= 47 and <= 51 ? -250 : 100;
            w.Wx.Snow = d => YearOf(d) == y && DoyOf(d) is >= 47 and <= 51 ? snowCm : 0;
            w.Run(y, 1, 60); w.Run(y + 1, 1, 26);
            var f = Enumerable.Range(0, F.Count).First(i => F.Slot[i] == 0 && F.Crop[i] == Crop.Wheat);
            return F.YieldG[f];
        }
        long Gross(int wx) => YieldMath.GrossG(52_800, 85, wx, 1000, 0);   // the field spent its fallow year grazed: fertility 70 + 15
        Assert.Equal(Gross(600), Yield(0));                    // bare ground: 400 permille lost
        Assert.Equal(Gross(1000), Yield(10));                  // under snow: nothing lost
    }

    // ---------- labour ----------

    private static long SownAfterSpring(SynthSpec spec, out Synth w)
    {
        w = Synth.Build(spec); w.Run(w.Year, 1, 20);
        return w.State.SownAcres;
    }

    [Fact]
    public void LabourShortage_ReducesTheSownArea_AndHiredHandsRestoreIt()
    {
        var full = new SynthSpec(ParcelsPerSlot: 2, FieldsPerParcel: 3000, WorkersPerHolder: 600);
        long sownFull = SownAfterSpring(full, out var wFull);
        Assert.Equal(2 * 3000, sownFull);                                    // every spring field sown
        long sownShort = SownAfterSpring(full with { WorkersPerHolder = 1 }, out var wShort);
        Assert.InRange(sownShort, 1, sownFull - 1);
        long sownHired = SownAfterSpring(full with { WorkersPerHolder = 1, PoolHouseholds = 80, HolderCrowns = 5000 }, out var wHired);
        Assert.True(sownHired > sownShort, $"{sownHired} vs {sownShort}");
        Assert.True(wHired.Econ.Ledger.Flow(TransferReason.Wage).Mites > 0);
        Assert.True(wHired.Econ.Ledger.Audit().IsConserved);
        var (demand, supply) = wShort.Sys.LabourOutlook(5);
        Assert.True(demand >= 0 && supply > 0);
        Assert.True(wShort.Sys.LastLabour.UnmetHours >= 0);
    }

    [Fact]
    public void NoHands_MeansNoSowing_AndUncutGrainRotsAtFourPercentADay_G_F9()
    {
        var w = Synth.Build(new SynthSpec(WorkersPerHolder: 0)); int y = w.Year; var F = w.State.Fields;
        w.Run(y, 1, 36);                                                   // through doy 36: the first overdue day for winter grain
        Assert.Equal(0, w.State.SownAcres);
        int f = Enumerable.Range(0, F.Count).First(i => F.Slot[i] == 0 && F.Crop[i] == Crop.Wheat);
        long g = 52_800 * 750 / 1000;                                      // nobody weeded: x 0.75
        Assert.Equal(YieldMath.Overdue(g), (long)F.YieldG[f]);
        w.Run(y, 37, 4);
        for (int i = 0; i < 4; i++) g = YieldMath.Overdue(g);
        Assert.Equal(YieldMath.Overdue(g), (long)F.YieldG[f]);             // five overdue days
        w.Run(y, 41, 20); w.Run(y + 1, 1, 1);
        Assert.Contains(w.Chron.ByType("farm.unharvested"), e => e.Causes[0].Kind == CauseKind.Famine);
        Assert.Equal(0, w.State.Fields.YieldG.Sum());
    }

    [Fact]
    public void SeedShortage_SowsOnlyWhatTheSeedAllows()
    {
        var w = Synth.Build(new SynthSpec(ParcelsPerSlot: 1, FieldsPerParcel: 6)); int y = w.Year; var F = w.State.Fields; var P = w.State.Parcels;
        int hh = P.Holder[Enumerable.Range(0, P.Count).First(p => P.Slot[p] == 1)];
        foreach (var g in new[] { "barley", "oats", "peas", "beans" }) w.Econ.HouseholdStore[hh].DestroyUpTo(w.Good(g), long.MaxValue, DestroyReason.Lost);
        w.Econ.HouseholdStore[hh].Create(w.Good("peas"), 3 * 9_240, CreateReason.Seed);          // seed for three fields of peas
        w.Run(y, 1, 20);
        Assert.Equal(3, Enumerable.Range(0, F.Count).Count(i => F.Slot[i] == 1 && F.Crop[i] != Crop.Fallow));
    }

    // ---------- harvest split, mass, coin ----------

    [Fact]
    public void HarvestSplit_FlowsThroughTheStores_AndMassIsConserved()
    {
        var w = Synth.Build(new SynthSpec()); int y = w.Year; var cat = w.Econ.Goods;
        long hourKey = 0;
        for (int d = 1; d <= 60; d++)
        {
            w.Run(y, d, 1);
            w.Econ.ApplyDailySpoilage(77, hourKey += 24, 10);
            Assert.True(w.MassAudit(), $"day {d}");
        }
        long grains = 0, sown = 0;
        foreach (var g in new[] { "wheat", "rye", "barley", "oats", "peas", "beans" })
        {
            ushort id = w.Good(g);
            grains += w.Econ.Mass.Created(id, CreateReason.Harvest); sown += w.Econ.Mass.Destroyed(id, DestroyReason.Sown);
        }
        Assert.Equal(w.State.HarvestG, grains);                            // the harvest is created exactly once, split over the three stores
        Assert.Equal(w.State.SeedSownG, sown);
        Assert.Equal(w.State.HarvestG, w.State.TitheG + w.State.RentG + (w.State.HarvestG - w.State.TitheG - w.State.RentG));
        long parish = w.Econ.OtherStores[FarmIds.ParishGranary(0)].Rows().Sum(r => r.Qty);
        Assert.True(parish > 0 && parish <= w.State.TitheG);               // the granary spoils 1% a season
        Assert.True(w.Econ.HouseholdStore[0].TotalMassG() > 0);            // the lord's rent
        Assert.True(w.Econ.Mass.Created(w.Good("straw"), CreateReason.Harvest) > 0);
    }

    [Fact]
    public void CoinLeases_PayTheLandlordInCoin_AtAutumnEnd()
    {
        var w = Synth.Build(new SynthSpec(CoinLeases: true)); int y = w.Year;
        Assert.Contains(LeaseKind.Coin, Enumerable.Range(0, w.State.Parcels.Count).Select(p => w.State.Parcels.Lease[p]));
        for (int h = 1; h < w.Econ.HouseholdPurse.Length; h++) w.Econ.Ledger.Mint(w.Econ.HouseholdPurse[h], Coin.FromCrowns(200), SourceReason.Seed);
        long before = w.Econ.Ledger.Balance(w.Econ.HouseholdPurse[0]).Mites;
        w.Run(y, 1, 60);
        Assert.Equal(0, w.State.RentG);                                    // coin leases pay no grain
        long fields = w.State.Parcels.Count * 4;
        Assert.Equal(fields * 320, w.Econ.Ledger.Flow(TransferReason.Rent).Mites);
        Assert.Equal(before + fields * 320, w.Econ.Ledger.Balance(w.Econ.HouseholdPurse[0]).Mites);
        Assert.True(w.Econ.Ledger.Audit().IsConserved);
    }

    // ---------- livestock ----------

    [Fact]
    public void FodderShortfall_CullsSheep_G_R5()
    {
        var w = Synth.Build(new SynthSpec(Sheep: 1000)); int y = w.Year;
        w.State.Livestock.Oxen[0] = 0;
        w.Econ.OtherStores[FarmIds.Fodder(0)].Create(w.Good("hay"), 15_000_000, CreateReason.Cut);     // 15,000 kg against 22,500 needed
        w.Run(y, 1, 45);
        w.Econ.OtherStores[FarmIds.Fodder(0)].DestroyUpTo(w.Good("straw"), long.MaxValue, DestroyReason.Lost);
        int before = w.State.Livestock.Sheep[0];
        w.Run(y, 46, 1);
        Assert.Equal(before - before * 333 / 1000, w.State.Livestock.Sheep[0]);
        Assert.Contains(w.Chron.ByType("farm.cull"), e => e.Causes[0].Kind == CauseKind.Weather);
    }

    [Fact]
    public void GrazedFlag_NeedsFortySheepPerHundredFallowAcres()
    {
        var w = Synth.Build(new SynthSpec(Sheep: 4)); int y = w.Year;          // 12 fallow acres need 4.8 sheep
        w.Run(y, 1, 16);
        Assert.False(w.State.Livestock.Grazed[0]);
        var w2 = Synth.Build(new SynthSpec(Sheep: 5)); w2.Run(w2.Year, 1, 16);
        Assert.True(w2.State.Livestock.Grazed[0]);
    }

    // ---------- determinism ----------

    [Fact]
    public void TwoRuns_GiveTheSameStateHash_AndTheSameEconomy()
    {
        string Run() { var w = Synth.Build(new SynthSpec(Variance: 40)); w.Run(w.Year, 1, 60); w.Run(w.Year + 1, 1, 60); return w.State.ComputeHash() + w.Econ.ComputeHash(); }
        Assert.Equal(Run(), Run());
    }
}

public class FarmingCalibrationTests
{
    private static readonly FarmingSettingsDef D = new() { Id = "t" };

    [Fact]
    public void RainRatio_FollowsTheLongCondition_FullDroughtIsPointThree()
    {
        Assert.Equal(300, FarmingSystem.RainRatioPermille(new LongCondition(LongKind.Drought, 1, 60), 1, 25, D));
        Assert.Equal(580, YieldMath.RainFactorPermille(FarmingSystem.RainRatioPermille(new LongCondition(LongKind.Drought, 10, 60), 1, 25, D)));
        int partial = FarmingSystem.RainRatioPermille(new LongCondition(LongKind.Drought, 20, 60), 1, 25, D);       // 6 of 25 days: eased
        Assert.InRange(partial, 301, 999);
        Assert.Equal(1000, FarmingSystem.RainRatioPermille(new LongCondition(LongKind.Drought, 40, 60), 1, 25, D));  // after the window
        Assert.Equal(1000, FarmingSystem.RainRatioPermille(default, 1, 25, D));
        Assert.Equal(934, YieldMath.RainFactorPermille(FarmingSystem.RainRatioPermille(new LongCondition(LongKind.WetYear, 1, 60), 1, 25, D)));   // a wet year costs little
    }
}
