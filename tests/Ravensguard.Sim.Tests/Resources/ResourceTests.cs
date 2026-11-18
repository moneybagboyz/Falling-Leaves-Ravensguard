using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.Tests.Production;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Tests.Resources;

/// <summary>Golden tests G-R1..G-R13 of the resources spec (herd tests excluded: herds are not built yet) and the stock invariants.</summary>
public class ResourceTests
{
    private static ExtractionKind K(string id) => ProdKit.Content.Extractions.First(e => e.Id == id);
    private static ResourceSettingsDef Cfg => ProdKit.Content.Settings;
    private static GameTime Day(int doy, int hour = 6) => GameTime.FromGameHours((doy - 1) * 24L + hour);

    [Fact]
    public void GR1_Woodcutter_FullStockAndAtTwentyPercentOfCap()
    {
        var k = K("woodcutting"); var summer = Day(20);
        long cap = 70_000_000;
        Assert.Equal(105_000, Extraction.GramsPerWorkerHour(k, summer, 1000, 50, cap, cap, 10));
        Assert.Equal(666, Extraction.StockFactor(cap / 5, cap, 30));
        Assert.Equal(69_930, Extraction.GramsPerWorkerHour(k, summer, 1000, 50, cap / 5, cap, 10));          // 699 kg a 10-hour day
        Assert.Equal(0, Extraction.GramsPerWorkerHour(k, summer, 1000, 50, 0, cap, 10));
        Assert.Equal(52_500, Extraction.GramsPerWorkerHour(k, summer, 500, 50, cap, cap, 10));               // heavy rain halves it
        Assert.Equal(0, Extraction.GramsPerWorkerHour(k, summer, 0, 50, cap, cap, 10));
        Assert.Equal(84_000, Extraction.GramsPerWorkerHour(k, Day(50), 1000, 50, cap, cap, 10));              // winter x0.8
        Assert.Equal(0, Extraction.WeatherMult(k, WeatherState.Blizzard));
        Assert.Equal(500, Extraction.WeatherMult(k, WeatherState.HeavyRain));
        Assert.Equal(1000, Extraction.WeatherMult(k, WeatherState.Clear));
        Assert.True(Extraction.GramsPerWorkerHour(k, summer, 1000, 100, cap, cap, 10) > Extraction.GramsPerWorkerHour(k, summer, 1000, 0, cap, cap, 10));
    }

    [Fact]
    public void GR2_WoodRegrowth_143836GramsPerAcrePerSeason_NeverAboveCap()
    {
        var l = new ResourceLayer(); int row = l.Add(10, ResourceKind.Wood, 0, 70_000_000);
        l.Regrow(Day(1), Cfg);
        Assert.Equal(143_836, l.StockG(row));
        Assert.InRange(3_500_000L * 60 / (365 * 4), 143_800, 143_900);                           // 5 m3 x 700 kg x 60/365 / 4 = 143.8 kg
        var near = new ResourceLayer(); int r2 = near.Add(1, ResourceKind.Wood, 69_950_000, 70_000_000);
        near.Regrow(Day(16), Cfg); Assert.Equal(70_000_000, near.StockG(r2));
        for (int i = 0; i < 1000; i++) near.Regrow(Day(16), Cfg);
        Assert.Equal(70_000_000, near.StockG(r2));
    }

    [Fact]
    public void GR3_RegionFirewoodBalance_ArithmeticOfTheSpec()
    {
        double kgDay = 25_000 * 1.5 * 1.25;
        Assert.Equal(46_875, kgDay, 6);
        double tYear = kgDay * 60 / 1000; Assert.Equal(2_812.5, tYear, 6);
        double m3 = tYear * 1000 / 700; Assert.Equal(4_018, m3, 0);
        double acres = m3 / (5.0 * 60 / 365); Assert.Equal(4_888, acres, 0);
        double cutters = kgDay / 1_050; Assert.InRange(cutters, 44, 46);                       // ~45 cutter FTE
    }

    [Fact]
    public void GR4_LakeRegrowth_AndShoreFishing()
    {
        var l = new ResourceLayer(); int row = l.Add(5, ResourceKind.Fish, 6_000_000, 15_000_000);
        l.Regrow(Day(16), Cfg);                                                                 // summer: +25% of 9,000 kg
        Assert.Equal(8_250_000, l.StockG(row));
        var fish = K("fishing_shore");
        long perHour = Extraction.GramsPerWorkerHour(fish, Day(20), 1000, 50, 8_250_000, 15_000_000, 10);
        Assert.Equal(400, perHour);
        Assert.Equal(600_000, perHour * 10 * 10 * 15);                                          // 10 fishers, 15 days, 10 hours
        long winter = Extraction.GramsPerWorkerHour(fish, Day(50), 1000, 50, 8_250_000, 15_000_000, 10);
        Assert.Equal(240_000, winter * 10 * 10 * 15);
        // winter regrowth is x0.4
        var w = new ResourceLayer(); int rw = w.Add(5, ResourceKind.Fish, 6_000_000, 15_000_000);
        w.Regrow(Day(46), Cfg); Assert.Equal(6_000_000L + 9_000_000L * 25 * 400 / 100_000, w.StockG(rw));
    }

    [Fact]
    public void GR7_Peat_SpringOnly_FifteenDays_4500Kg()
    {
        var peat = K("peat_cutting");
        long total = 0;
        for (int doy = 1; doy <= 60; doy++) total += Extraction.GramsPerWorkerHour(peat, Day(doy), 1000, 50, 1_000_000_000, 1_000_000_000, 10) * 10;
        Assert.Equal(4_500_000, total);                                                         // only spring days 1-15 count
        Assert.Equal(0, Extraction.SeasonMult(peat, Day(16)));
        Assert.Equal(2_700, 4_500 * 6 / 10);                                                    // heat value 0.6 of firewood
        Assert.Equal(2_700, 25 * 7.2 * 15, 6);                                                  // 25 households for the 15-day winter
    }

    [Fact]
    public void GR9_Hunting_KillRateFollowsDensity_AndSpringIsClosed()
    {
        var hunt = K("hunting");
        long kills = 0;
        for (int d = 0; d < 100_000; d++) if (Extraction.HuntKill(77, 1234, d, 0, 70_000, Cfg)) kills++;
        Assert.InRange(kills, 20_600, 21_400);                                                  // 21,000 +- 400
        Assert.Equal(0, Extraction.SeasonMult(hunt, Day(5)));                                   // closed in spring
        Assert.Equal(300, Extraction.SeasonMult(hunt, Day(20)));
        // min(0.5, 3 x density): a rich tile still caps at one kill in two
        long rich = 0; for (int d = 0; d < 20_000; d++) if (Extraction.HuntKill(77, 9, d, 0, 400_000, Cfg)) rich++;
        Assert.InRange(rich, 9_700, 10_300);
        Assert.Equal(8.4, 0.21 * Cfg.HuntCarcassMeatG / 1000.0, 6);                              // expected kg per hunter-day
    }

    [Fact]
    public void GR10_Mine_SixMinersThreeTonnesADay_ThirtyDaysLeave910t()
    {
        var site = new Site { Id = EntityId.Make(IdKind.Other, 0x300_0001), Kind = SiteKind.Mine, Mineral = Ravensguard.Sim.WorldGen.MineralType.Iron, Tonnage = 1000, Levels = 1, Mine = MineState.Active };
        var m = MineWorks.FromSite(0, site); var k = K("mining_iron");
        Assert.Equal(3_000_000, m.DayYieldG(k, 6, -1));
        long mined = 0; for (int d = 0; d < 30; d++) mined += m.Mine(m.DayYieldG(k, 6, -1));
        Assert.Equal(90_000_000, mined);
        Assert.Equal(910_000_000, m.RemainingG);                                                // 910 t left
        int days = 30; while (m.RemainingG > 0) { m.Mine(m.DayYieldG(k, 6, -1)); days++; }
        Assert.Equal(334, days);                                                                // lasts 333.3 days: the last day is a part day
        var fresh = new MineWorks { RemainingG = 1_000_000_000, InitialG = 1_000_000_000, Levels = 1 };
        Assert.Equal(1_000_000, fresh.DayYieldG(k, 3, 1));                                       // one hauler per two miners caps the crew
        Assert.Equal(2_000_000, fresh.DayYieldG(k, 4, 2));
        Assert.Equal(4_000_000, fresh.DayYieldG(k, 20, -1));                                     // max_miners = 4 + 4 x levels = 8
        Assert.Equal(8, new MineWorks { Levels = 1 }.MaxMiners);                                // 4 + 4 x levels
    }

    [Fact]
    public void GR11_MineFlood_SixPercentAtTwoLevels_TwelveInAWetYear()
    {
        int normal = 0, wet = 0, caves = 0; const int N = 20_000;
        for (int i = 0; i < N; i++)
        {
            var site = new Site { Id = EntityId.Make(IdKind.Other, 0x300_0000UL + (ulong)i + 1), Kind = SiteKind.Mine, Mineral = Ravensguard.Sim.WorldGen.MineralType.Iron, Tonnage = 100, Levels = 2, Mine = MineState.Active };
            var a = MineWorks.FromSite(i, site); var b = MineWorks.FromSite(i, site);
            var ha = a.RollHazards(5, Day(31, 0), false, false, Cfg); var hb = b.RollHazards(5, Day(31, 0), true, false, Cfg);
            if (ha.Flood) normal++; if (hb.Flood) wet++; if (ha.CaveIn) { caves++; Assert.InRange(ha.Dead, 1, 2); }
        }
        Assert.InRange(normal / (double)N, 0.05, 0.07);
        Assert.InRange(wet / (double)N, 0.11, 0.13);
        Assert.InRange(caves / (double)N, 0.007, 0.013);                                         // 0.5% x 2 levels = 1%
    }

    [Fact]
    public void GR12_SilverFromHundredTonnesOfGalena_IsEighteenPercentOfSeedMoney()
    {
        long galenaT = 100, silverG = galenaT * Cfg.SilverGradeGPerT;
        Assert.Equal(300_000, silverG);                                                         // 300 kg
        var strike = MintMath.Strike(silverG * 1000, 750);
        Assert.Equal(230_769, strike.Struck.WholeCrowns);
        double share = strike.Struck.WholeCrowns / (25_000.0 * 50);
        Assert.InRange(share, 0.18, 0.19);
    }

    [Fact]
    public void GR13_Derivation_IsPureInTheRegion_AndEveryStockIsWithinItsCap()
    {
        var region = EconFixture.Pop.Region;
        var a = ResourceLayer.Build(region, Cfg, 1234); var b = ResourceLayer.Build(region, Cfg, 1234);
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
        Assert.Equal(ResourceLayer.PastureTiles(region).OrderBy(x => x), ResourceLayer.PastureTiles(region).OrderBy(x => x));
        Assert.NotEqual(a.ComputeHash(), ResourceLayer.Build(region, Cfg, 99).ComputeHash());   // initial fish stock follows the seed
        Assert.True(a.Count > 10_000);
        int prev = -1; long prevKey = -1;
        for (int i = 0; i < a.Count; i++)
        {
            Assert.InRange(a.StockG(i), 0, a.CapG(i));
            long key = ((long)a.TileOf(i) << 4) | (long)a.KindOf(i); Assert.True(key > prevKey); prevKey = key; prev = i;
        }
        var (ws, wc) = a.Totals(ResourceKind.Wood); Assert.Equal(0.9, ws / (double)wc, 2);
        Assert.True(a.Totals(ResourceKind.Fish).Cap > 0 && a.Totals(ResourceKind.Game).Cap > 0 && a.Totals(ResourceKind.Pasture).Cap > 0);
        Assert.True(a.Find(a.TileOf(5), a.KindOf(5)) == 5);
        _ = prev;
    }

    [Fact]
    public void Soak_TakeAndRegrow_NeverLeaveTheBounds_AndRegrowthDoesNotCreateStockAboveCap()
    {
        var region = EconFixture.Pop.Region;
        var l = ResourceLayer.Build(region, Cfg, 1234);
        var rng = new RngStream(3, 0, 1, 0x501);
        for (int season = 0; season < 40; season++)
        {
            for (int i = 0; i < 2_000; i++) l.Take(rng.Range(0, l.Count), rng.Range(0, 2_000_000), season * 15);
            l.Regrow(Day(1 + 15 * (season % 4)), Cfg);
            for (int i = 0; i < l.Count; i++) Assert.InRange(l.StockG(i), 0, l.CapG(i));
        }
    }

    [Fact]
    public void ForagingDepletesTilesAndRefillsAtYearStart_GR8()
    {
        // a forager takes 3 kg a day from 1.6 kg acres: about 1.9 acres a day, 56 acres in 30 days; an emptied tile gives nothing until S1
        var layer = new ResourceLayer();
        for (int y = 240; y <= 260; y++) for (int x = 240; x <= 260; x++) layer.Add(RegionData.Index(x, y), ResourceKind.Forage, 1600, 1600);
        var content = WithKind(new BusinessKind { Id = "forager", OwnerOccupation = Occupation.Labourer, Slots = [(Occupation.Labourer, 4, WageClass.Unskilled)], Extractions = [K("foraging")] });
        var k = new ProdKit(households: 2, layer: layer, content: content);
        k.People.Occupation[1] = Occupation.Labourer;
        var b = k.Make("forager", 1_000, 1, Occupation.Labourer, Skill.None, 50, ownerWorks: false);
        for (int doy = 16; doy < 46; doy++)
        {
            var t = Day(doy, 0);
            b.Jobs.Clear(); b.Jobs.Add(new CrewJob { Kind = K("foraging").Index, Row = k.Sys.PickTile(K("foraging"), 16000, 16000, ResourceKind.Forage), Workers = 1 });
            for (int h = 6; h < 16; h++) k.Sys.WorkHour(Day(doy, h));
        }
        int empty = 0; for (int i = 0; i < layer.Count; i++) if (layer.StockG(i) < 1600) empty++;
        long taken = k.Have(b, "vegetables");
        Assert.InRange(taken, 30 * 2_500, 30 * 3_000);
        Assert.InRange(empty, 40, 62);
        long before = layer.Totals(ResourceKind.Forage).Stock;
        layer.Regrow(Day(1), Cfg);                                                              // S1: refill to cap
        Assert.Equal(layer.Totals(ResourceKind.Forage).Cap, layer.Totals(ResourceKind.Forage).Stock);
        Assert.True(layer.Totals(ResourceKind.Forage).Stock > before);
        // no work in winter or spring
        Assert.Equal(0, Extraction.GramsPerWorkerHour(K("foraging"), Day(5), 1000, 50, 1600, 1600, 10));
        Assert.Equal(0, Extraction.GramsPerWorkerHour(K("foraging"), Day(50), 1000, 50, 1600, 1600, 10));
    }

    private static ProductionContent WithKind(BusinessKind kind)
    {
        kind.Index = (ushort)ProdKit.Content.Kinds.Length;
        return new ProductionContent { Recipes = ProdKit.Content.Recipes, Kinds = [.. ProdKit.Content.Kinds, kind], Extractions = ProdKit.Content.Extractions, Settings = ProdKit.Content.Settings };
    }

    [Fact]
    public void OverHarvest_FiftyCuttersOnOneVillagesWoods_DepleteThem()
    {
        var layer = new ResourceLayer();
        for (int y = 240; y <= 259; y++) for (int x = 240; x <= 259; x++) layer.Add(RegionData.Index(x, y), ResourceKind.Wood, 63_000_000, 70_000_000);   // 400 acres, 28,000 t
        long cap0 = layer.Totals(ResourceKind.Wood).Cap;
        var content = WithKind(new BusinessKind { Id = "woodgang", OwnerOccupation = Occupation.Woodcutter, Slots = [(Occupation.Woodcutter, 50, WageClass.Unskilled)], Extractions = [K("woodcutting")] });
        var k = new ProdKit(households: 51, layer: layer, content: content);
        var b = k.Make("woodgang", 1_000_000, 50, Occupation.Woodcutter, Skill.Forestry, 50, ownerWorks: false);
        var wc = K("woodcutting"); int days = 0;
        for (int year = 0; year < 5; year++)
            for (int doy = 1; doy <= 60; doy++, days++)
            {
                if (doy % 15 == 1) layer.Regrow(Day(doy, 0), Cfg);
                b.Jobs.Clear(); b.Jobs.Add(new CrewJob { Kind = wc.Index, Row = k.Sys.PickTile(wc, 16000, 16000, ResourceKind.Wood), Workers = 50 });
                for (int h = 6; h < 16; h++) k.Sys.WorkHour(Day(doy, h));
                b.Store.DestroyUpTo(k.Cat.IdOf("logs"), long.MaxValue, DestroyReason.Burned);
                b.Store.DestroyUpTo(k.Cat.IdOf("oak_bark"), long.MaxValue, DestroyReason.Burned);
                for (int i = 0; i < b.Owed.Count; i++) b.Owed[i] = 0;
            }
        var (stock, cap) = layer.Totals(ResourceKind.Wood);
        Assert.Equal(cap0, cap);
        Assert.True(stock < cap * 0.5, $"{stock / (double)cap:P0} left");                      // 5 years of 52 t a day on 400 acres visibly depletes the woods
        int low = 0; for (int i = 0; i < layer.Count; i++) if (layer.StockG(i) * 10 > layer.CapG(i) * 6) low++;
        Assert.True(low < 400, $"{low} acres still above 60%");                                       // the cutters' favoured acres are stripped
        // the unit price of firewood would follow: cover falls by the same ratio at constant demand, 2x or more
        var curve = new PriceCurve(18_000, (long)(cap0 * 0.9));
        Assert.True(curve.MidMicro(cap0 / 10) / (double)curve.MidMicro((long)(cap0 * 0.9)) >= 2);
        // regrowth is monotone and bounded
        long before = layer.Totals(ResourceKind.Wood).Stock; layer.Regrow(Day(16), Cfg);
        Assert.True(layer.Totals(ResourceKind.Wood).Stock > before);
    }
}
