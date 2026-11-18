using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Tests.Markets;
using Ravensguard.Sim.Tests.Production;
using Xunit;

namespace Ravensguard.Sim.Tests.Review;

/// <summary>Regression tests for the Phase 2 economy review (docs/reviews/phase-2.md, economy section). Each failed before its fix.</summary>
public class Phase2EconomyReviewTests
{
    /// <summary>Finding 3: a buy order's spending cap binds, whatever its price ratio lets it buy (the steward's 2 percent relief budget was computed and never applied).</summary>
    [Fact]
    public void F3_ABuyOrdersSpendingCap_LimitsWhatItPays()
    {
        var k = new MarketKit(); k.Stock(1_600_000);
        var steward = k.Party(PartyKind.Household, 1_000_000);                    // a treasury of 10,000 crowns
        long cap = 1_000_000 * 20 / 1000;                                           // 2 percent of it: 200 crowns
        k.Book.Post(new BuyOrder(steward, k.Grain, 1_600_000, 3500) { MaxSpendMites = cap });
        k.Book.RunSession(10, 0);
        long spent = 1_000_000 - k.Coin(steward);
        Assert.True(spent > 0 && spent <= cap, $"spent {spent} mites against a cap of {cap}");
        // without the cap the same order spends far more (the defect)
        var k2 = new MarketKit(); k2.Stock(1_600_000); var s2 = k2.Party(PartyKind.Household, 1_000_000);
        k2.Book.Post(new BuyOrder(s2, k2.Grain, 1_600_000, 3500)); k2.Book.RunSession(10, 0);
        Assert.True(1_000_000 - k2.Coin(s2) > cap * 3);
    }

    /// <summary>Finding 3: the fief's daily relief budget is shared by its settlements in proportion to their need, so all of them together spend at most the budget.</summary>
    [Fact]
    public void F3_ReliefShares_AddUpToAtMostTheFiefsBudget()
    {
        long budget = 20_000; long[] need = [3_300, 1_100, 7_700];
        long total = need.Sum(), spent = need.Sum(n => ParishRelief.ReliefShare(budget, n, total));
        Assert.InRange(spent, budget - need.Length, budget);
        Assert.Equal(0, ParishRelief.ReliefShare(budget, 5, 0));
    }

    /// <summary>Finding 9: new goods no longer join an older lot at its decayed floor and listing day; they keep the seller's floor and their own day.</summary>
    [Fact]
    public void F9_NewGoods_KeepTheirFloorAndDay_InsteadOfJoiningAnOldDecayedLot()
    {
        var k = new MarketKit(); var seller = k.Party(PartyKind.Business, 0, ("wheat", 200_000));
        k.Book.List(seller, k.Grain, 100_000, 900, 0);
        k.Book.ExpireLots(1);                                                       // a day unsold: the floor comes down to 600
        var old = k.Book.Goods[k.Grain].Lots.Single(l => l.Owner == seller);
        Assert.Equal(600, old.FloorPermille);
        k.Book.List(seller, k.Grain, 100_000, 900, 1);
        var lots = k.Book.Goods[k.Grain].Lots.Where(l => l.Owner == seller).ToList();
        Assert.Equal(2, lots.Count);
        var fresh = lots.Single(l => l.ListedDay == 1);
        Assert.Equal(900, fresh.FloorPermille); Assert.Equal(100_000, fresh.Qty);
        Assert.Equal(k.Book.BookStore.Qty(k.Grain), lots.Sum(l => l.Qty));
        // the same day and floor still merge (decision I40)
        k.Book.List(seller, k.Grain, 0, 900, 1); var seller2 = seller;
        seller2.Store.Create(k.Grain, 5_000, CreateReason.Seed); k.Book.List(seller2, k.Grain, 5_000, 900, 1);
        Assert.Equal(2, k.Book.Goods[k.Grain].Lots.Count(l => l.Owner == seller));
    }

    /// <summary>Finding 4: fuel is gathered from the settlement's woodland with the household's hours, not created from nothing.</summary>
    [Fact]
    public void F4_Gathering_DrawsTheWoodland_AndNeedsHands()
    {
        using var w = SoakWorld.Create(false); var e = w.Econ; var wl = w.Economy.Woodland; var hht = w.Pop.Households; var wood = e.Food.Firewood;
        Assert.Same(wl, e.FuelGatherer);
        int hh = Enumerable.Range(0, hht.Count).First(h => hht.MemberCount[h] > 1 && hht.Class[h] == SocialClass.Peasant && wl.RowsOf(hht.Settlement[h]).Count > 0 && wl.GatherHoursOf(h) > 0);
        int s = hht.Settlement[hh];
        long avail0 = wl.AvailableG(s), store0 = e.HouseholdStore[hh].Qty(wood);
        long got = wl.Gather(hh, 20_000);
        Assert.Equal(Math.Min(20_000, wl.GatherHoursOf(hh) * (long)e.Tuning.GatherFuelGPerWorkerHour), got);
        Assert.Equal(avail0 - got, wl.AvailableG(s));                                   // every gram came off the woodland
        Assert.Equal(store0 + got, e.HouseholdStore[hh].Qty(wood));
        // the hands bound it: no more than the members' hours at the gathering rate
        long big = wl.Gather(hh, 10_000_000);
        Assert.Equal(wl.GatherHoursOf(hh) * (long)e.Tuning.GatherFuelGPerWorkerHour, big);
        // a woodland cut down to its floor gives nothing
        foreach (int row in wl.RowsOf(s)) w.Layer.SetStock(row, w.Layer.CapG(row) * e.Tuning.GatherStockFloorPct / 100);
        Assert.Equal(0, wl.Gather(hh, 20_000));
        Assert.True(e.MassAuditHolds());
    }

    /// <summary>Finding 4: a household gathers its burn and a small buffer up to its keep, not 105 percent of its burn whatever it holds.</summary>
    [Fact]
    public void F4_GatherWant_StopsAtTheKeep()
    {
        using var w = SoakWorld.Create(false); var e = w.Econ; var hht = w.Pop.Households; var wood = e.Food.Firewood;
        int hh = Enumerable.Range(0, hht.Count).First(h => hht.MemberCount[h] > 1 && hht.Class[h] == SocialClass.Peasant);
        long burn = HouseholdNeeds.BurnG(e, hht, hh, 0);
        e.HouseholdStore[hh].DestroyUpTo(wood, long.MaxValue, DestroyReason.Burned);
        Assert.Equal(burn * (100 + e.Tuning.GatherFuelBufferPct) / 100, HouseholdNeeds.GatherWantG(e, hht, hh, 0));
        e.HouseholdStore[hh].Create(wood, burn * e.Tuning.FuelKeepDays, CreateReason.Seed);
        Assert.Equal(0, HouseholdNeeds.GatherWantG(e, hht, hh, 0));
    }

    /// <summary>Finding 10: food and ale bought by the spending go to the store (eaten through the needs); only durables wear on delivery.</summary>
    [Fact]
    public void F10_SpendingDeliveries_KeepFoodAndAle_AndWearOnlyDurables()
    {
        using var w = SoakWorld.Create(false); var e = w.Econ; var ms = w.Markets;
        ushort ale = e.Food.Ale, cloth = e.Goods.IdOf("cloth_wool"), planks = e.Goods.IdOf("timber_planks");
        var store = e.HouseholdStore[0];
        long a0 = store.Qty(ale), c0 = store.Qty(cloth), p0 = store.Qty(planks);
        store.Create(ale, 5_000, CreateReason.Seed); store.Create(cloth, 2_000, CreateReason.Seed); store.Create(planks, 30_000, CreateReason.Seed);
        ms.DeliveryHook(0, ale, 3)(new TradeResult(5_000, 100, 0));
        ms.DeliveryHook(0, cloth, 3)(new TradeResult(2_000, 100, 0));
        ms.DeliveryHook(0, planks, 4)(new TradeResult(30_000, 100, 0));
        Assert.Equal(a0 + 5_000, store.Qty(ale));                                      // drunk later through the needs (Eaten), not destroyed as Worn
        Assert.Equal(c0, store.Qty(cloth));                                            // a durable wears out
        Assert.Equal(p0 + 30_000, store.Qty(planks));                                  // materials wait for the builders
    }

    /// <summary>Finding 8: the integer square root is exact where the double one rounds up, and production planning keeps no floating point.</summary>
    [Fact]
    public void F8_ProductionPlanning_IsInteger()
    {
        long n = 67_108_865L * 67_108_865L - 1;                                         // (2^26 + 1)^2 - 1: Math.Sqrt rounds it up to 2^26 + 1
        Assert.Equal(67_108_864L, ProductionSystem.ISqrt(n));
        Assert.Equal(67_108_865L, ProductionSystem.ISqrt(n + 1));
        for (long v = 0; v < 5000; v++) { long r = ProductionSystem.ISqrt(v); Assert.True(r * r <= v && (r + 1) * (r + 1) > v); }
        var methods = typeof(ProductionSystem).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
        foreach (var m in methods)
        {
            Assert.False(m.ReturnType == typeof(double) || m.ReturnType == typeof(float), $"{m.Name} returns a float");
            Assert.DoesNotContain(m.GetParameters(), p => p.ParameterType == typeof(double) || p.ParameterType == typeof(float));
        }
    }

    /// <summary>Findings 5 and 6: an empty staple book is out of band (not left out), and a closed stall counts against the merchants (not left out of the denominator).</summary>
    [Fact]
    public void F5_F6_EmptyBooksAndClosedStalls_CountAgainstTheMeasures()
    {
        using var w = SoakWorld.Create(); var soak = new EconomySoak(w.Headless); var e = w.Econ;
        ushort wheat = e.Goods.IdOf("wheat");
        var books = w.Markets.AllBooks().Where(b => b.Merchants.Count > 0).ToList();
        var empty = books[0];
        foreach (var l in empty.Goods[wheat].Lots.ToList()) { empty.BookStore.Destroy(wheat, l.Qty, DestroyReason.Spoiled); empty.Goods[wheat].StockUnits -= l.Qty; }
        empty.Goods[wheat].Lots.Clear();
        int stalls = w.Sys.Businesses.Count(b => w.Sys.KindOf(b).Trader);
        var stall = w.Sys.Businesses.First(b => w.Sys.KindOf(b).Trader && b.State == BusinessState.Open && !empty.Merchants.Contains(b.Party));
        w.Sys.Close(stall);
        soak.Sample();
        var r = soak.CloseYear();
        int iw = Array.IndexOf(EconomySoak.Staples, "wheat");
        Assert.True(r.InBandPct[iw] <= 100.0 * (books.Count - 1) / books.Count + 1e-9, $"wheat in band {r.InBandPct[iw]:0.0}% with one of {books.Count} books empty");
        Assert.True(r.MerchantsSolventPct <= 100.0 * (stalls - 1) / stalls + 1e-9, $"solvent {r.MerchantsSolventPct:0.0}% with one of {stalls} stalls closed");
        Assert.True(r.ClosedPct > 0);
    }
}
