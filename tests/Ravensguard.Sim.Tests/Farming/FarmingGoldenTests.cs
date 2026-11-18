using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Farming;

/// <summary>The golden tests of farming spec section 8, on the pure maths (grams; 1 measure = 1,100 g).</summary>
public class FarmingGoldenTests
{
    private static readonly FarmingSettings S = new();
    private static long Base(Crop c) => S.Info(c).BaseG(1100);
    private static long Seed(Crop c) => S.Info(c).SeedG(1100);

    [Fact] public void Content_CropsAndSettings_MatchTheBuiltInDefaults()
    {
        var fs = FarmingSettings.FromContent(EconFixture.Content);
        foreach (var c in new[] { Crop.Wheat, Crop.Rye, Crop.Barley, Crop.Oats, Crop.Peas, Crop.Beans })
        {
            Assert.Equal(S.Info(c).BaseYieldMeasures, fs.Info(c).BaseYieldMeasures);
            Assert.Equal(S.Info(c).FertilityDelta, fs.Info(c).FertilityDelta);
            Assert.Equal(S.Info(c).RipeDoy, fs.Info(c).RipeDoy);
            Assert.Equal(S.Info(c).SowFirstDoy, fs.Info(c).SowFirstDoy);
        }
        Assert.Equal(1100, fs.GramsPerMeasure); Assert.Equal(100, fs.Def.TithePermille); Assert.Equal(640, fs.Def.RentCoinMitesPerFieldYear);       // integration decision I3: every lease pays coin, twice the spec's 320 a field-year (the in-kind shares stayed the lords' idle grain)
        Assert.Equal(-10, fs.Info(Crop.Wheat).FertilityDelta); Assert.Equal(5, fs.Info(Crop.Beans).FertilityDelta);
        fs.BindGoods(new GoodCatalog(EconFixture.Content)); Assert.True(fs.Info(Crop.Rye).Good > 0 || fs.Info(Crop.Rye).GoodId == "rye");
    }

    [Fact] public void G_F1_WheatFertility70()
    {
        long gross = YieldMath.GrossG(Base(Crop.Wheat), 70, 1000, 1000, 0);
        Assert.Equal(57_200, gross); Assert.Equal(11_440, Seed(Crop.Wheat)); Assert.Equal(45_760, YieldMath.NetG(gross, Seed(Crop.Wheat)));
    }

    [Fact] public void G_F2_WheatFertility55()
    {
        long gross = YieldMath.GrossG(Base(Crop.Wheat), 55, 1000, 1000, 0);
        Assert.Equal(44_942, gross); Assert.Equal(33_502, YieldMath.NetG(gross, Seed(Crop.Wheat)));
    }

    [Fact] public void G_F3_FertilityIsCappedAt91()
        => Assert.Equal(74_360, YieldMath.GrossG(Base(Crop.Wheat), 95, 1000, 1000, 0));

    [Fact] public void G_F4_RyeFertility40()
    {
        Assert.Equal(55_000, Base(Crop.Rye));
        long gross = YieldMath.GrossG(Base(Crop.Rye), 40, 1000, 1000, 0);
        Assert.Equal(31_428, gross); Assert.Equal(20_428, YieldMath.NetG(gross, Seed(Crop.Rye)));
    }

    [Fact] public void G_F5_TenantShareSplit()
    {
        var sp = YieldMath.Split(52_800, 10_560, OwnerKind.Tenant, LeaseKind.Share, 2500, 100);
        Assert.Equal(4_224, sp.TitheG); Assert.Equal(10_560, sp.RentG); Assert.Equal(52_800 - 4_224 - 10_560, sp.HolderG);
        Assert.Equal(27_456, -10_560 + sp.HolderG);
        Assert.Equal(24.96, (-10_560 + sp.HolderG) / 1100.0, 2);
    }

    [Fact] public void G_F6_FreeholderKeeps34_56Measures()
    {
        var sp = YieldMath.Split(52_800, 10_560, OwnerKind.Freeholder, LeaseKind.None, 2500, 100);
        Assert.Equal(4_224, sp.TitheG); Assert.Equal(0, sp.RentG); Assert.Equal(52_800 - 4_224, sp.HolderG);
        Assert.Equal(34.56, (sp.HolderG - 10_560) / 1100.0, 2);
        Assert.Equal(0, YieldMath.Split(52_800, 10_560, OwnerKind.Glebe, LeaseKind.None, 0, 100).TitheG);   // glebe is exempt
        Assert.Equal(0, YieldMath.Split(52_800, 10_560, OwnerKind.Tenant, LeaseKind.Coin, 2500, 100).RentG); // coin leases pay coin
    }

    [Fact] public void G_F7_Drought()
    {
        Assert.Equal(580, YieldMath.RainFactorPermille(300));
        long gross = YieldMath.GrossG(Base(Crop.Wheat), 70, 580, 1000, 0);
        Assert.Equal(33_176, gross); Assert.Equal(21_736, YieldMath.NetG(gross, Seed(Crop.Wheat)));
        Assert.Equal(475, 20_064 * 1000 / 42_240);     // 47.5% of normal net
        Assert.Equal(1000, YieldMath.RainFactorPermille(1400)); Assert.Equal(600, YieldMath.RainFactorPermille(2400)); Assert.Equal(1000, YieldMath.RainFactorPermille(1000));
    }

    [Fact] public void G_F8_FertilityLedger()
    {
        int f = 70;
        f = YieldMath.AddFertility(f, S.Info(Crop.Wheat).FertilityDelta); Assert.Equal(60, f);
        f = YieldMath.AddFertility(f, S.Info(Crop.Barley).FertilityDelta); Assert.Equal(54, f);
        f = YieldMath.AddFertility(f, YieldMath.FallowBonus(true)); Assert.Equal(69, f);
        f = 70; f = YieldMath.AddFertility(f, -10); f = YieldMath.AddFertility(f, S.Info(Crop.Peas).FertilityDelta); Assert.Equal(65, f); f = YieldMath.AddFertility(f, 15); Assert.Equal(80, f);
        f = 70; f = YieldMath.AddFertility(f, -10); f = YieldMath.AddFertility(f, -6); f = YieldMath.AddFertility(f, YieldMath.FallowBonus(false)); Assert.Equal(59, f);
        Assert.Equal(100, YieldMath.AddFertility(98, 15)); Assert.Equal(0, YieldMath.AddFertility(4, -10));
    }

    [Fact] public void G_F9_OverdueLoss()
    {
        long g = 52_800; var expect = new long[] { 50_688, 48_660, 46_713, 44_844, 43_050 };
        for (int i = 0; i < 5; i++) { g = YieldMath.Overdue(g); Assert.Equal(expect[i], g); }
    }

    [Fact] public void G_F10_Spoilage_HouseAndGranary()
    {
        var cat = new GoodCatalog(EconFixture.Content); var mass = new MassLedger(cat.Count); ushort wheat = cat.IdOf("wheat");
        var house = new Store(cat, mass, StoreKind.Barn, EntityId.Make(IdKind.Other, 1)); var gran = new Store(cat, mass, StoreKind.Granary, EntityId.Make(IdKind.Other, 2));
        house.Create(wheat, 1_000_000, CreateReason.Seed); gran.Create(wheat, 1_000_000, CreateReason.Seed);
        for (int d = 0; d < 15; d++) { house.ApplySpoilage(7, d * 24, 10); gran.ApplySpoilage(7, d * 24, 10); }
        Assert.InRange(house.Qty(wheat), 982_151 - 100, 982_151 + 100);   // 1,200 ppm a day (decision I28)
        Assert.InRange(gran.Qty(wheat), 990_004 - 100, 990_004 + 100);
    }

    [Fact] public void G_F11_LateFrostResow_YieldAndSeedMaths()
    {
        Assert.Equal(42_900, YieldMath.GrossG(Base(Crop.Wheat), 70, 750, 1000, 0));      // resown: x 0.75 of 52,800
        Assert.Equal(12_320, Seed(Crop.Barley));                                          // each resow costs another seed
    }

    [Fact] public void G_F12_LabourCredit_ParcelOfTenAcres()
    {
        int carry = 0;
        Assert.Equal(20_000, YieldMath.CreditPpm(2, 1, 10, 10, ref carry));              // ppm per worker-hour
        int p = 0; long hours = 0; carry = 0; var day = new List<int>();
        while (p < 1_000_000) { p += YieldMath.CreditPpm(2, 4, 10, 10, ref carry); hours++; }   // 4 workers: 80,000 an hour
        Assert.Equal(13, hours);                                                          // 12.5 hours: day 1 (10 h) + 3 h on day 2
        Assert.Equal(10, YieldMath.FieldsDone(1_000_000, 10)); Assert.Equal(4, YieldMath.FieldsDone(400_000, 10)); Assert.Equal(3, YieldMath.FieldsDone(399_999, 10));
        carry = 0; int total = 0; for (int i = 0; i < 7; i++) total += YieldMath.CreditPpm(8, 1, 13, 10, ref carry);   // the remainder is carried, never lost
        Assert.Equal((int)(7L * 8 * 1_000_000 / (10 * 13)), total);
    }

    [Fact] public void G_F13_WetHarvest()
    {
        Assert.Equal(1000, YieldMath.WetHarvestPermille(59)); Assert.Equal(920, YieldMath.WetHarvestPermille(80)); Assert.Equal(850, YieldMath.WetHarvestPermille(120));
        Assert.Equal(48_576, 52_800 * 920 / 1000); Assert.Equal(44_880, 52_800 * 850 / 1000);
    }

    [Fact] public void G_F14_Hail()
    {
        long gross = YieldMath.GrossG(Base(Crop.Wheat), 70, 1000, 1000, 500);
        Assert.Equal(28_600, gross); Assert.Equal(17_160, YieldMath.NetG(gross, Seed(Crop.Wheat)));
    }

    [Fact] public void G_F15_RegionBalance_ReferenceRegion()
    {
        long Net(Crop c) => YieldMath.NetG(YieldMath.GrossG(Base(c), 70, 1000, 1000, 0), Seed(c));
        double winter = 0.60 * Net(Crop.Wheat) + 0.40 * Net(Crop.Rye), spring = 0.40 * Net(Crop.Barley) + 0.27 * Net(Crop.Oats) + 0.33 * (Net(Crop.Peas) + Net(Crop.Beans)) / 2;
        Assert.Equal(45_056, winter, 0); Assert.Equal(43_789, spring, 0);
        double regionMeasures = 20_800 * (winter + spring) / 1100.0;
        Assert.InRange(regionMeasures, 1_679_000, 1_681_000);                // integration decision I30: cereal yields 52/50/56/50 measures (were 48/46/48/46)
        double afterSpoil = regionMeasures * 0.96, demand = 25_000 * 60 * 0.92 + 45_000;
        double surplus = afterSpoil / demand - 1;
        Assert.InRange(surplus, 0.02, 0.15); Assert.InRange(surplus, 0.130, 0.135);   // was +4 percent; the 100-year soak needs the margin (spoilage, famine years)
    }

    [Fact] public void G_F16_CoinRentEquivalent()
    {
        double perCutAcre = 0.25 * 38.4 * 1.1 * 45;        // mites
        Assert.Equal(475, perCutAcre, 0);
        double perFieldYear = Math.Round(perCutAcre * 2 / 3);   // 317
        Assert.Equal(317, perFieldYear);
        Assert.InRange(320 / perFieldYear, 0.99, 1.01);
    }

    [Fact] public void G_F18_HailFrequency()
    {
        int hits = 0;
        for (int i = 0; i < 1_000_000; i++) if (YieldMath.HailHit(42, 17 * 24, EntityId.Make(IdKind.Parcel, (ulong)i / 30 + 1), 16 + i % 15)) hits++;
        Assert.InRange(hits, 4_850, 5_150);
    }

    [Fact] public void Blight_RollsAreStableAndWetYearsTripleTheOdds()
    {
        int dry = 0, wet = 0;
        for (int y = 0; y < 20_000; y++) { if (YieldMath.Blight(9, y * 1440L, 3, y, false)) dry++; if (YieldMath.Blight(9, y * 1440L, 3, y, true)) wet++; }
        Assert.InRange(dry, 450, 750); Assert.InRange(wet, 1_500, 2_100);
        Assert.Equal(YieldMath.Blight(9, 5, 3, 7, false), YieldMath.Blight(9, 5, 3, 7, false));
    }

    [Fact] public void CullFraction_G_R5()
        => Assert.Equal(333, YieldMath.CullPermille(15_000, 22_500));

    [Fact] public void FertilityMultiplier_IsLinearUpToTheCap()
    {
        for (int fert = 0; fert <= 100; fert += 7) Assert.Equal(57_200L * Math.Min(fert, 91) / 70, YieldMath.Gross0G(Base(Crop.Wheat), fert));
        Assert.Equal(74_360, YieldMath.Gross0G(Base(Crop.Wheat), 91)); Assert.Equal(74_360, YieldMath.Gross0G(Base(Crop.Wheat), 100));
    }

    [Fact] public void Variance_IsWithinFourPercent_MeanOne()
    {
        long sum = 0; int lo = 9999, hi = 0;
        for (int i = 1; i <= 20_000; i++) { int v = YieldMath.VariancePermille(5, 100, EntityId.Make(IdKind.Field, (ulong)i)); sum += v; lo = Math.Min(lo, v); hi = Math.Max(hi, v); }
        Assert.InRange(lo, 960, 962); Assert.InRange(hi, 1038, 1040); Assert.InRange(sum / 20_000.0, 999, 1001);
        Assert.Equal(1000, YieldMath.VariancePermille(5, 100, EntityId.Make(IdKind.Field, 1), 0));
    }
}
