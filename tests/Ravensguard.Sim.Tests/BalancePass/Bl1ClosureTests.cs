using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Tests.Faith;
using Ravensguard.Sim.Tests.Phase7Overview;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Worlds;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.BalancePass;

/// <summary>
/// P8-BL-1 (balance pass 3.1, goldens BL-4 and BL-5): OV-7's copper cap reached, and WL-9 / EA_F11 (the 960 levy kits and their 2.1 t of bar) measured on the whole runtime of seed 1234 with makers that buy their own materials.
/// </summary>
public class Bl1ClosureTests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ BL-4

    [Fact]
    public void BL4_CopperIsStruckUpToItsFourPercentCap_TheCoinBookExactEveryDay_AndTheCapIsNamedInTheChronicle()
    {
        using var r = new FaithRig();
        var mints = r.Sim.Get<MintWorld>()!; var e = r.State.Economy!; var led = e.Ledger; var book = r.State.Coinage!; var m0 = mints.Mints[0];
        void Advance(int days) { long h0 = r.Runtime.HourDone + (24 - r.Runtime.HourDone % 24) % 24; r.Runtime.AdvanceTo(GameTime.FromGameHours(h0 + 24L * days)); }
        HeroResult Hero(HeroCommand c) => r.Do(c, ((r.Runtime.HourDone / 24) + 1) * 86400 + 10 * 3600L);
        Advance(16);                                                                                                   // the mint master is in post
        ushort copper = e.Goods.IdOf("copper");
        long seeded = led.Audit().Seeded.Mites; int struckSales = 0; bool capped = false;
        for (int i = 0; i < 400 && !capped; i++)
        {
            r.Ctx.Pack.Create(copper, 400_000, CreateReason.Other);                                                    // 400 kg of the token metal at the counter
            long supplyBefore = led.Audit().Circulating.Mites;
            var res = Hero(CoinDesk.Sell(MintMetal.Copper, 400_000, m0.Settlement, 0));
            if (res.Code == HeroResultCode.Ok) struckSales++; else capped = true;
            Assert.True(book.Stock.CopperFace * 1000 <= supplyBefore * r.Content.All<MoneySettingsDef>().First().CopperCapPermille * 101 / 100, "the copper in coin never passes the cap (one percent for the day's drift of the supply)");
            Advance(1);
            Assert.Equal("", book.Stock.Check(led));                                                                   // opted + loose = struck - melted, every day
            Assert.True(led.Audit().IsConserved, "day " + i); Assert.True(e.MassAuditHolds(), "mass, day " + i);
            Assert.Equal(seeded, led.Audit().Seeded.Mites);                                                            // the strikes are the only faucet
        }
        long supply = led.Audit().Circulating.Mites, face = book.Stock.CopperFace;
        output.WriteLine($"copper: {struckSales} sales struck {book.Stock.CopperStruck} pieces, face {face} of a cap of {supply / 1000 * r.Content.All<MoneySettingsDef>().First().CopperCapPermille} mites ({face * 1000 / Math.Max(1, supply)} permille of the supply {supply})");
        Assert.True(capped, "the counter refused copper in the end: the cap was reached");
        Assert.True(struckSales > 0);
        int cap = r.Content.All<MoneySettingsDef>().First().CopperCapPermille;
        Assert.True(face * 1000 >= supply * cap * 99 / 100, $"copper face {face} is at {cap} permille of the supply {supply} (within one percent of the cap)");
        Assert.Contains(r.State.Chronicle.All, en => en.TypeId == "mint_copper_capped");
        Assert.True(book.Stock.CopperStruck > 0);
    }

    // ------------------------------------------------------------------ BL-5

    private static ushort Id(WorldState st, string good) => st.Economy!.Goods.IdOf(good);

    /// <summary>The highest price of a good, permille of its reference, over the books that have a merchant.</summary>
    private static int PriceMax(WorldState st, string good)
    {
        int g = Id(st, good); var all = new List<int>();
        foreach (var b in st.HomeRecords!.Economy.Markets.AllBooks()) if (b.Merchants.Count > 0) all.Add(b.MidRatioPermille((ushort)g));
        all.Sort(); return all.Count == 0 ? 0 : all[all.Count / 2];                                                    // the median over the books that have a merchant
    }

    [Fact, Trait("Category", "Soak")]
    public void BL5_AFullLevysKits_960OfThem_AreOrderedInLots_AndMeasuredAfterTwoSeasons_ByWorksOrImports_WithThePriceRiseLogged()
    {
        var (w, st) = P7Game.Game(); st.Content = w.Content;
        using var rt = new WorldRuntime(st, w.Content, new MemoryRegionPager());
        rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 24L * 20));
        var led = st.Economy!.Ledger; var lord = st.Party!.Hero;
        led.TryFind(PurseKind.Person, EconomyState.HeroOwner, out var purse);
        led.Mint(purse, new Coin(12_000_000), SourceReason.Seed);
        var supply = ArmsPlay.Build(st, w.Content, create: true)!;
        long list = supply.ListPrice(960); int day = (int)(rt.HourDone / 24);
        long BarKg() { long kg = 0; foreach (var b in st.HomeRecords!.Economy.Markets.AllBooks()) kg += b.Goods[Id(st, "iron_bar")].StockUnits / 1000; return kg; }
        long barKg0 = BarKg();
        int bar0 = PriceMax(st, "iron_bar"), cloth0 = PriceMax(st, "cloth_linen");
        var depot = ArmsPlay.DepotFor(st, lord.Value, 0)!;
        var order = supply.PlaceOrderLots(depot, 960, list, day);
        int lots = st.WarSupply!.Arms.Jobs.Count; int pieces = order.Ordered.Values.Sum();
        output.WriteLine($"order: list price {list}, {lots} lots, {pieces} pieces ordered ({string.Join(", ", order.Ordered.Select(kv => kv.Key + " " + kv.Value))}), bar in the books {barKg0} kg");
        Assert.True(lots > 1, "the order is cut into lots so that many makers share it");
        long coin0 = led.Audit().InPurses.Mites;
        for (int d = 0; d < 2 * TimeConst.DaysPerSeason; d++)
        {
            rt.AdvanceTo(GameTime.FromGameHours(rt.HourDone + 24));
            Assert.True(led.Audit().IsConserved, "coin, day " + d); Assert.True(st.Economy!.MassAuditHolds(), "mass, day " + d);
        }
        var arms = st.WarSupply!.Arms; long delivered = arms.SupplyStats[ArmsSupply.StatDelivered];
        var live = arms.Jobs.Values.Sum(j => j.Lines.Sum(l => l.Delivered)); var onRoad = arms.Transit.Values.Sum(t => t.Items.Count);
        int barMax = PriceMax(st, "iron_bar"), clothMax = PriceMax(st, "cloth_linen");
        output.WriteLine($"after two seasons: delivered {delivered} of {pieces} pieces ({delivered * 1000 / Math.Max(1, pieces)} permille), {onRoad} on the road, {arms.Jobs.Count} lots still open; makers bought {st.HomeRecords!.Economy.Markets.AllBooks().Count()} books' goods");
        output.WriteLine($"price rise (permille of the reference at the median book): iron bar {bar0} -> {barMax}, linen cloth {cloth0} -> {clothMax}; iron bar in the books {barKg0} kg -> {BarKg()} kg (the 960 kits draw 2,112 kg of bar when all are made)");
        Assert.True(delivered * 1000 / Math.Max(1, pieces) >= BalanceRatchets.Bl5KitsDeliveredPerMille, $"delivered {delivered} of {pieces}: under the ratchet of {BalanceRatchets.Bl5KitsDeliveredPerMille} permille");
    }
}
