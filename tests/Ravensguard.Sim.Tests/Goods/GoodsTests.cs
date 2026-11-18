using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Tests.Economy;

namespace Ravensguard.Sim.Tests.Goods;

public class GoodCatalogTests
{
    private static readonly GoodCatalog Cat = EconFixture.Catalog();

    [Fact]
    public void Goods_LoadWithDenseOrdinalIds_AndKnownSpoilage()
    {
        Assert.True(Cat.Count >= 30);
        for (int i = 1; i < Cat.Count; i++) Assert.True(string.CompareOrdinal(Cat.StringId((ushort)(i - 1)), Cat.StringId((ushort)i)) < 0);
        foreach (var id in new[] { "wheat", "rye", "barley", "oats", "flour", "ale", "firewood", "hay", "wool_raw", "cloth_wool", "hide_raw", "leather", "iron_ore", "charcoal", "iron_bloom", "iron_bar", "salt", "fish_fresh", "meat_fresh" })
            Assert.True(Cat.TryIdOf(id, out _), id);
        ushort wheat = Cat.IdOf("wheat");
        Assert.Equal(1200, Cat.SpoilPpm(wheat, StoreKind.House)); Assert.Equal(1200, Cat.SpoilPpm(wheat, StoreKind.Barn)); Assert.Equal(670, Cat.SpoilPpm(wheat, StoreKind.Granary));
        Assert.Equal(300_000, Cat.SpoilPpm(Cat.IdOf("fish_fresh"), StoreKind.House)); Assert.Equal(150_000, Cat.SpoilPpm(Cat.IdOf("fish_fresh"), StoreKind.Cellar));
        Assert.Equal(0, Cat.SpoilPpm(Cat.IdOf("salt"), StoreKind.Open));
        Assert.Equal(1200, Cat.SpoilPpm(wheat, StoreKind.Chest));
        Assert.Throws<KeyNotFoundException>(() => Cat.IdOf("unobtainium"));
    }

    [Fact]
    public void EveryGood_HasSanePriceAndUnits()
    {
        for (ushort g = 0; g < Cat.Count; g++)
        {
            var d = Cat.Def(g);
            Assert.InRange(d.BasePriceMitesPerKg, 0.05, 5_000_000); // items spec soak 9.3
            Assert.True(d.BulkLitresPerKg > 0, d.Id);
            Assert.True(Cat.MassG(g, 1000) > 0 && Cat.VolumeMl(g, 1000) > 0);
        }
        Assert.Equal(45, Cat.Def(Cat.IdOf("wheat")).BasePriceMitesPerKg);
        Assert.Equal(1100, (int)Math.Round(1000.0 / (Cat.FoodMeasuresPpm(Cat.IdOf("wheat")) / 1e6), 0));   // one measure is 1,100 g of grain
    }

    [Fact] // G-I11
    public void G_I11_CoinParity_SilverValueMatchesTheCrown()
    {
        var silver = MaterialResolver.Resolve(EconFixture.Content, "silver");
        double crownsFor1300mg = silver.ValuePerKg * 1.3 / 1000.0;
        Assert.InRange(crownsFor1300mg, 0.999, 1.001);
        var gold = MaterialResolver.Resolve(EconFixture.Content, "gold");
        Assert.InRange(gold.ValuePerKg / silver.ValuePerKg, 12.49, 12.51);
        Assert.Equal(76920, Cat.BasePriceMilliMites(Cat.IdOf("silver")) / 1000);   // 1 crown = 100 mites per 1.3 g
    }

    [Fact]
    public void VolumeMath_IsConsistent()
    {
        ushort g = Cat.IdOf("wheat");
        foreach (long q in new long[] { 0, 1, 7, 1000, 123_456 })
        {
            long ml = Cat.VolumeMl(g, q);
            Assert.True(Cat.QtyForVolumeMl(g, ml) >= q);
            Assert.True(Cat.VolumeMl(g, Cat.QtyForVolumeMl(g, ml)) <= ml);
            Assert.True(Cat.VolumeMl(g, Cat.QtyForVolumeMl(g, ml) + 1) > ml);
        }
        Assert.Equal(1330, Cat.VolumeMl(g, 1000));
    }

    [Fact]
    public void TempMultiplier_Table() { Assert.Equal([500, 800, 800, 1000, 1000, 1500], new[] { -5, 0, 9, 10, 20, 21 }.Select(GoodCatalog.TempMultPermille)); }

    [Fact]
    public void Hash_IsStable() => Assert.Equal(Cat.Hash(), EconFixture.Catalog().Hash());
}

public class StoreTests
{
    private static readonly GoodCatalog Cat = EconFixture.Catalog();
    private static Store NewStore(StoreKind k = StoreKind.House, long mass = 0, long ml = 0, bool? enforce = null, ulong owner = 1, MassLedger? ml2 = null)
        => new(Cat, ml2 ?? new MassLedger(Cat.Count), k, EntityId.Make(IdKind.Household, owner), mass, ml, enforce);

    [Fact]
    public void CreateDestroyMove_ConserveAndCount()
    {
        var led = new MassLedger(Cat.Count); ushort w = Cat.IdOf("wheat");
        var a = NewStore(ml2: led); var b = NewStore(owner: 2, ml2: led);
        Assert.True(a.Create(w, 10_000, CreateReason.Harvest));
        Assert.True(Store.Move(a, b, w, 4_000));
        Assert.Equal(6_000, a.Qty(w)); Assert.Equal(4_000, b.Qty(w));
        Assert.True(b.Destroy(w, 1_500, DestroyReason.Eaten));
        Assert.Equal(10_000, led.Created(w, CreateReason.Harvest)); Assert.Equal(1_500, led.Destroyed(w, DestroyReason.Eaten));
        Assert.Equal(8_500, led.Net(w)); Assert.Equal(a.Qty(w) + b.Qty(w), led.Net(w));
    }

    [Fact]
    public void Operations_AreAllOrNothing_AndNeverNegative()
    {
        ushort w = Cat.IdOf("wheat"); var a = NewStore(); var b = NewStore(owner: 2);
        a.Create(w, 100, CreateReason.Seed);
        Assert.False(a.Destroy(w, 101, DestroyReason.Eaten)); Assert.Equal(100, a.Qty(w));
        Assert.False(Store.Move(a, b, w, 101)); Assert.Equal(100, a.Qty(w)); Assert.Equal(0, b.Qty(w));
        Assert.Equal(100, a.DestroyUpTo(w, 500, DestroyReason.Lost)); Assert.Equal(0, a.RowCount);
        Assert.Equal(0, a.DestroyUpTo(w, 5, DestroyReason.Lost));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.Create(w, -1, CreateReason.Seed));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.Destroy(w, -1, DestroyReason.Lost));
        Assert.True(a.Create(w, 0, CreateReason.Seed)); Assert.Equal(0, a.RowCount);
        Assert.True(Store.Move(a, a, w, 0));
    }

    [Fact]
    public void Rows_StaySortedByGood()
    {
        var s = NewStore();
        foreach (var id in new[] { "wheat", "ale", "salt", "firewood", "iron_bar", "cheese" }) s.Create(Cat.IdOf(id), 10, CreateReason.Seed);
        var rows = s.Rows();
        Assert.Equal(rows.Select(r => r.Good).Order().ToArray(), rows.Select(r => r.Good).ToArray());
        s.Destroy(Cat.IdOf("salt"), 10, DestroyReason.Lost);
        Assert.Equal(5, s.RowCount);
    }

    [Fact] // G-I6
    public void G_I6_CartCapacity_MassAndVolumeLimits()
    {
        // a cart: 500 kg and 1,000 L
        Store Cart() => NewStore(StoreKind.Open, 500_000, 1_000_000, enforce: true);
        long Fit(string good) { var c = Cart(); return c.CreateUpTo(Cat.IdOf(good), 10_000_000, CreateReason.Seed); }
        Assert.Equal(500_000, Fit("wheat"));                                     // 667 L, mass-limited
        Assert.InRange(Fit("firewood"), 399_000, 400_000);                       // 2.5 L/kg: volume-limited at 400 kg
        Assert.InRange(Fit("wool_raw"), 332_000, 334_000);                       // baled wool 3 L/kg: 333 kg
        Assert.Equal(500_000, Fit("iron_bar"));                                  // mass-limited
        Assert.Equal(500_000, Fit("bread"));                                     // 1.5 L/kg is 750 L: mass-limited
        var c1 = Cart();
        Assert.True(c1.Create(Cat.IdOf("firewood"), 400_000, CreateReason.Cut)); Assert.False(c1.Create(Cat.IdOf("firewood"), 1_000, CreateReason.Cut));
    }

    [Fact]
    public void Capacity_IsEnforced_ByKind_AndWhenAsked()
    {
        ushort w = Cat.IdOf("wheat");
        var granary = NewStore(StoreKind.Granary, 0, 80_000_000);            // 80,000 L
        long max = granary.Room(w);
        Assert.InRange(max, 60_000_000, 60_200_000);                          // 1.33 L/kg -> ~60 t
        Assert.True(granary.Create(w, max, CreateReason.Harvest));
        Assert.False(granary.Create(w, 1, CreateReason.Harvest));              // full: refuses
        Assert.Equal(0, granary.CreateUpTo(w, 100, CreateReason.Harvest));
        Assert.Equal(0, granary.Room(w));
        // a house store has a nominal capacity but does not refuse (spec: only granary and barn are enforced in Phase 2)
        var house = NewStore(StoreKind.House, 0, 1_000);
        Assert.True(house.Create(w, 5_000_000, CreateReason.Harvest));
        Assert.True(Store.DefaultEnforced(StoreKind.Barn) && Store.DefaultEnforced(StoreKind.Chest) && !Store.DefaultEnforced(StoreKind.House));
        // a move into a full store fails and changes nothing
        var src = NewStore(owner: 9); src.Create(w, 1_000, CreateReason.Seed);
        Assert.False(Store.Move(src, granary, w, 1_000)); Assert.Equal(1_000, src.Qty(w));
        // freeing room lets it in, exactly up to the limit
        granary.Destroy(w, 750, DestroyReason.Eaten);
        Assert.Equal(750, Store.MoveUpTo(src, granary, w, 1_000)); Assert.Equal(250, src.Qty(w));
    }

    [Fact]
    public void Capacity_MixesMassAndVolume_AcrossGoods()
    {
        var chest = NewStore(StoreKind.Chest, 100_000, 150_000);                // 100 kg, 150 L
        Assert.True(chest.Create(Cat.IdOf("iron_bar"), 60_000, CreateReason.Crafted));           // 60 kg, 7.8 L
        long cloth = chest.Room(Cat.IdOf("cloth_wool"));
        Assert.Equal(40_000, cloth);                                                              // mass-limited
        Assert.False(chest.Create(Cat.IdOf("hay"), 40_000, CreateReason.Cut));                // 40 kg of hay is 200 L: volume-limited
        Assert.True(chest.Create(Cat.IdOf("hay"), 17_000, CreateReason.Cut));                  // 85 L fits
        Assert.True(chest.UsedVolumeMl() <= 150_000 && chest.TotalMassG() <= 100_000);
    }

    [Fact] // G-I12 (volume side)
    public void G_I12_BarrelOf200LitresHolds200LitresOfAle()
    {
        ushort ale = Cat.IdOf("ale");
        var barrel = NewStore(StoreKind.Sealed, 0, 200_000);
        Assert.True(barrel.Create(ale, 202_000, CreateReason.Brewed));               // 200 L at 1,010 g/L
        Assert.Equal(202_000, barrel.TotalMassG());
        Assert.False(barrel.Create(ale, 5_000, CreateReason.Brewed));
        Assert.Equal(30_000, Cat.SpoilPpm(ale, StoreKind.Sealed)); Assert.Equal(150_000, Cat.SpoilPpm(ale, StoreKind.House));
    }

    [Fact] // G-I4
    public void G_I4_FreshFishRotsAbout30PercentADay()
    {
        ushort fish = Cat.IdOf("fish_fresh"); var s = NewStore();
        s.Create(fish, 100_000, CreateReason.Caught);
        long[] want = [70_000, 49_000, 34_300];
        for (int d = 0; d < 3; d++) { s.ApplySpoilage(1, d * 24, tempC: 12); Assert.Equal(want[d], s.Qty(fish)); }
        // below freezing: x0.5 (15% a day), stochastic rounding keeps within a gram or two
        var cold = NewStore(owner: 2); cold.Create(fish, 100_000, CreateReason.Caught);
        long[] coldWant = [85_000, 72_250, 61_412];
        for (int d = 0; d < 3; d++) { cold.ApplySpoilage(1, d * 24, tempC: -3); Assert.InRange(cold.Qty(fish), coldWant[d] - 2, coldWant[d] + 2); }
    }

    [Fact] // G-I5
    public void G_I5_GrainLosesFivePercentASeasonInAHouse_OnePercentInAGranary()
    {
        ushort w = Cat.IdOf("wheat");
        long Run(StoreKind k)
        {
            var s = NewStore(k, owner: 5); s.Create(w, 1_000_000, CreateReason.Harvest);
            for (int d = 0; d < 15; d++) s.ApplySpoilage(7, d * 24, 10);
            return s.Qty(w);
        }
        Assert.InRange(Run(StoreKind.House), 982_151 - 100, 982_151 + 100);   // 1.8% a season at 1,200 ppm (decision I28; the spec's 5% was 3,414 ppm)
        Assert.InRange(Run(StoreKind.Barn), 982_151 - 100, 982_151 + 100);
        Assert.InRange(Run(StoreKind.Granary), 990_004 - 100, 990_004 + 100);
    }

    [Fact]
    public void Spoilage_OverASeason_PerStoreKind()
    {
        string[] goods = ["wheat", "flour", "ale", "fish_fresh", "fish_salted", "cheese", "vegetables", "wool_raw", "leather", "iron_bar", "firewood", "salt"];
        foreach (var g in goods)
        {
            ushort id = Cat.IdOf(g); var res = new Dictionary<StoreKind, long>();
            foreach (var k in Enum.GetValues<StoreKind>())
            {
                var s = NewStore(k, owner: 3); s.Create(id, 10_000_000, CreateReason.Seed);
                for (int d = 0; d < 15; d++) s.ApplySpoilage(11, d * 24, 12);
                res[k] = s.Qty(id);
            }
            // a store kind never spoils a good faster than its own column says: cellar/granary/sealed are no worse than the house
            Assert.True(res[StoreKind.Cellar] >= res[StoreKind.House] && res[StoreKind.Granary] >= res[StoreKind.House] && res[StoreKind.Sealed] >= res[StoreKind.House], g);
            if (g == "salt") Assert.All(res.Values, v => Assert.Equal(10_000_000, v));
            if (g == "fish_fresh") Assert.True(res[StoreKind.House] < 1_000_000 && res[StoreKind.Cellar] < 3_000_000, "fresh fish is gone in a season");
        }
        // bread-making tolerance from the spec: over a normal 60-day year a house loses 3-8% of grain if held through the whole year
        var y = NewStore(StoreKind.House, owner: 4); y.Create(Cat.IdOf("wheat"), 10_000_000, CreateReason.Harvest);
        for (int d = 0; d < 60; d++) y.ApplySpoilage(11, d * 24, 12);
        double lost = 1.0 - y.Qty(Cat.IdOf("wheat")) / 10_000_000.0;
        Assert.InRange(lost, 0.06, 0.08);   // 1,200 ppm a day (integration decision I28, was 3,414): a season is 1.8%, a full 60-day year held in a house compounds to ~6.9%
    }

    [Fact]
    public void Spoilage_IsLoggedAsASink_AndCountedByTheMassLedger()
    {
        var led = new MassLedger(Cat.Count); ushort w = Cat.IdOf("wheat"); var s = NewStore(ml2: led);
        s.Create(w, 1_000_000, CreateReason.Harvest);
        long lost = s.ApplySpoilage(3, 100, 12);
        Assert.InRange(lost, 1_199, 1_201);
        Assert.Equal(lost, led.Destroyed(w, DestroyReason.Spoiled)); Assert.Equal(s.Qty(w), led.Net(w));
    }

    [Fact]
    public void Spoilage_IsAPureFunctionOfTheKey()
    {
        ushort w = Cat.IdOf("wheat");
        long Run(ulong seed, long hour, ulong owner) { var s = NewStore(owner: owner); s.Create(w, 777, CreateReason.Seed); s.ApplySpoilage(seed, hour, 12); return s.Qty(w); }
        for (ulong owner = 1; owner < 20; owner++) Assert.Equal(Run(5, 1000, owner), Run(5, 1000, owner));
        // a small stock loses a gram or not depending on the key: both outcomes occur across keys
        var outcomes = Enumerable.Range(0, 2000).Select(h => Run(5, h, 1)).Distinct().ToList();
        Assert.Equal([776, 777], outcomes.Order().ToList());      // 777 x 1,200 ppm = 0.93 g: loses 0 or 1
    }

    [Fact] // G-I7
    public void G_I7_StochasticRounding_KeepsTheExpectation()
    {
        ushort w = Cat.IdOf("wheat"); long lost = 0;
        var s = NewStore(owner: 8);
        for (int k = 0; k < 1_000_000; k++)
        {
            s.Create(w, 1, CreateReason.Seed);
            lost += s.ApplySpoilage(21, k, 12);
            s.DestroyUpTo(w, 1, DestroyReason.Consumed);
        }
        Assert.InRange(lost, 1_200 - 120, 1_200 + 120);   // sigma ~ 35: three sigma
    }

    [Fact]
    public void RoofLeaks_MultiplyTheLoss()
    {
        ushort w = Cat.IdOf("wheat"); var a = NewStore(owner: 1); var b = NewStore(owner: 1);
        a.Create(w, 10_000_000, CreateReason.Seed); b.Create(w, 10_000_000, CreateReason.Seed);
        long normal = a.ApplySpoilage(1, 5, 12), leaky = b.ApplySpoilage(1, 5, 12, leakMult: 10);
        Assert.InRange(leaky, normal * 10 - 20, normal * 10 + 20);
    }

    [Fact] // G-I8
    public void G_I8_RandomOperations_ConserveMassPerGood()
    {
        var led = new MassLedger(Cat.Count);
        var stores = Enumerable.Range(0, 200).Select(i => NewStore(i % 7 == 0 ? StoreKind.Granary : StoreKind.House, i % 7 == 0 ? 50_000 : 0, 0, i % 7 == 0, (ulong)i + 1, led)).ToList();
        ushort[] goods = new[] { "wheat", "flour", "ale", "iron_bar", "salt", "arming_sword" }.Select(Cat.IdOf).ToArray();
        const ulong seed = 5;
        for (long n = 0; n < 100_000; n++)
        {
            int a = DetRng.Range(seed, n, 1, 3, 0, 0, 200), b = DetRng.Range(seed, n, 2, 3, 0, 0, 200);
            ushort g = goods[DetRng.Range(seed, n, 3, 3, 0, 0, goods.Length)];
            long q = DetRng.Range(seed, n, 4, 3, 0, 0, 3000);
            switch (DetRng.Range(seed, n, 5, 3, 0, 0, 4))
            {
                case 0: stores[a].Create(g, q, CreateReason.Harvest); break;
                case 1: stores[a].Destroy(g, q, DestroyReason.Eaten); break;
                case 2: Store.Move(stores[a], stores[b], g, q); break;
                default: Store.MoveUpTo(stores[a], stores[b], g, q); break;
            }
        }
        foreach (var g in goods)
        {
            long sum = 0; foreach (var s in stores) { Assert.True(s.Qty(g) >= 0); sum += s.Qty(g); }
            Assert.Equal(led.Net(g), sum);
        }
        foreach (var s in stores) { if (s.EnforceCapacity) Assert.True(s.TotalMassG() <= 50_000); foreach (var (_, q) in s.Rows()) Assert.True(q > 0); }
        Assert.True(goods.Sum(g => led.TotalCreated(g)) > 0);
    }

    [Fact]
    public void Store_MoveBetweenGranaryAndHouse_RespectsMassCapOfPieces()
    {
        ushort sword = Cat.IdOf("arming_sword");
        var chest = NewStore(StoreKind.Chest, 5_000);                       // 5 kg holds 3 swords of 1,453 g
        Assert.Equal(3, chest.CreateUpTo(sword, 10, CreateReason.Crafted));
        Assert.Equal(4_359, chest.TotalMassG());
    }
}
