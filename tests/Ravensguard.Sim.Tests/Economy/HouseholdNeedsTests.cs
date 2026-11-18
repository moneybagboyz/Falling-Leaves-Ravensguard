using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Tests.Economy;

public class HouseholdNeedsTests
{
    private static ushort G(EconomyState e, string id) => e.Goods.IdOf(id);

    private static (EconomyState E, PersonTable P, HouseholdTable H) Peasants5()
    {
        var (p, h) = EconFixture.Household(30, 28, 11, 7, 2);
        return (EconFixture.SmallEconomy(h, p), p, h);
    }

    [Fact] // G-H1
    public void G_H1_BasketOfAPeasantHouseholdOfFive()
    {
        var (e, p, h) = Peasants5();
        var b = HouseholdNeeds.Basket(e, p, h, 0, 0, 12);
        Assert.Equal((2, 3), (b.Adults, b.Children));
        Assert.Equal(400, b.StapleX100); Assert.Equal(100, b.RelishX100);                       // 4.00 and 1.00 measures
        Assert.Equal(4_400, HouseholdNeeds.GrainEquivalentG(b.StapleX100));                      // 4,400 g of grain
        Assert.Equal(1_050, b.AleMl);                                                            // 300 x (2 + 3 x 0.5) (integration decision I23)
        Assert.Equal(40, b.SaltG); Assert.Equal(7_500, b.FuelG);
        Assert.Equal(1_060, 1_050 * e.Goods.Def(G(e, "ale")).LitreMassG / 1000);
    }

    [Fact] // G-H2
    public void G_H2_FuelMultiplier_DoublesTowardZero()
    {
        var (e, p, h) = Peasants5();
        Assert.Equal([15_000L, 11_250L, 7_500L, 7_500L], new[] { 0, 6, 12, 18 }.Select(t => HouseholdNeeds.Basket(e, p, h, 0, 0, t).FuelG));
        Assert.Equal(200, HouseholdNeeds.FuelMultX100(-10)); Assert.Equal(100, HouseholdNeeds.FuelMultX100(30));
    }

    [Fact]
    public void ClassBaskets_FollowTheSpecTable()
    {
        foreach (var (cls, staple, relish, ale, wood) in new[] { (SocialClass.Peasant, 80, 20, 300, 1500), (SocialClass.Burgher, 72, 28, 400, 1500), (SocialClass.Gentry, 50, 50, 600, 2000) })
        {
            var (p, h) = EconFixture.Household(cls, [40]);
            var e = EconFixture.SmallEconomy(h, p); var b = HouseholdNeeds.Basket(e, p, h, 0, 0, 12);
            Assert.Equal((staple, relish, (long)ale, (long)wood, 100), (b.StapleX100, b.RelishX100, b.AleMl, b.FuelG, b.StapleX100 + b.RelishX100));   // everyone eats 1.0 measure
        }
    }

    [Fact] // G-H3
    public void G_H3_EatingGrain_WithAMill_PaysTheSixteenth()
    {
        var (e, p, h) = Peasants5(); long toll = 0;
        var miller = new Store(e.Goods, e.Mass, StoreKind.House, EntityId.Make(IdKind.Household, 99));
        var s = e.HouseholdStore[0]; ushort wheat = G(e, "wheat");
        s.Create(wheat, 10_000, CreateReason.Seed);
        int eaten = HouseholdNeeds.EatStaple(e, s, 400, 0, miller, ref toll);
        Assert.Equal(400, eaten); Assert.Equal(293, toll); Assert.Equal(293, miller.Qty(wheat));
        Assert.Equal(10_000 - 4_693, s.Qty(wheat));                                              // 4,400 x 16/15 = 4,693 drawn
        Assert.Equal(e.Mass.Net(wheat), s.Qty(wheat) + miller.Qty(wheat));                         // the toll moved, only 4,400 g were eaten
    }

    [Fact] // G-H3 (flour)
    public void G_H3_EatingFlour_NeedsNoToll_AndFlourComesFirst()
    {
        var (e, p, h) = Peasants5(); long toll = 0; var s = e.HouseholdStore[0];
        s.Create(G(e, "flour"), 10_000, CreateReason.Seed); s.Create(G(e, "wheat"), 10_000, CreateReason.Seed);
        var miller = new Store(e.Goods, e.Mass, StoreKind.House, EntityId.Make(IdKind.Household, 99));
        Assert.Equal(400, HouseholdNeeds.EatStaple(e, s, 400, 0, miller, ref toll));
        Assert.Equal(10_000 - 4_211, s.Qty(G(e, "flour"))); Assert.Equal(10_000, s.Qty(G(e, "wheat"))); Assert.Equal(0, toll);
    }

    [Fact]
    public void Staple_FallsBackThroughTheOrder_AndKeepsTheSeedReserve()
    {
        var (e, p, h) = Peasants5(); long toll = 0; var s = e.HouseholdStore[0];
        s.Create(G(e, "flour"), 1_000, CreateReason.Seed); s.Create(G(e, "wheat"), 6_000, CreateReason.Seed); s.Create(G(e, "rye"), 2_000, CreateReason.Seed);
        // 1,000 g flour is 95 measure-hundredths; the reserve of 5,000 g is held back from the grain, leaving 3,000 g
        int eaten = HouseholdNeeds.EatStaple(e, s, 400, 5_000, null, ref toll);
        Assert.Equal(0, s.Qty(G(e, "flour")));
        long grainLeft = s.Qty(G(e, "wheat")) + s.Qty(G(e, "rye"));
        Assert.True(grainLeft >= 5_000, $"reserve breached: {grainLeft}");
        Assert.InRange(eaten, 95 + 270, 95 + 275);
    }

    [Fact]
    public void RunDay_FeedsAWellStockedHousehold_AndConservesMass()
    {
        var (e, p, h) = Peasants5(); var s = e.HouseholdStore[0];
        s.Create(G(e, "wheat"), 300_000, CreateReason.Seed); s.Create(G(e, "peas"), 5_000, CreateReason.Seed); s.Create(G(e, "firewood"), 100_000, CreateReason.Seed);
        s.Create(G(e, "ale"), 5_000, CreateReason.Seed); s.Create(G(e, "salt"), 1_000, CreateReason.Seed); s.Create(G(e, "fish_fresh"), 3_000, CreateReason.Seed);
        var r = HouseholdNeeds.RunDay(e, p, h, 0, 0, 12);
        Assert.Equal(1000, r.FedPermille); Assert.False(r.DietPoor); Assert.False(r.Cold); Assert.False(r.Hungry);
        Assert.Equal((400, 100, 0), (r.EatenStapleX100, r.EatenRelishX100, r.SubstitutedX100));
        Assert.Equal(7_500, r.FuelBurnedG); Assert.Equal(1_060, r.AleDrunkG); Assert.Equal(40, r.SaltEatenG);
        Assert.Equal(100_000 - 7_500, s.Qty(G(e, "firewood")));
        Assert.Equal(HouseholdTier.Profit, r.Tier);
        Assert.True(e.MassAuditHolds());
        // fresh fish went first (most perishable): 100 hundredths at 0.45 measures/kg = 2,223 g
        Assert.InRange(3_000 - s.Qty(G(e, "fish_fresh")), 2_222, 2_223); Assert.Equal(5_000, s.Qty(G(e, "peas")));
        for (int i = 0; i < 5; i++) Assert.Equal(3000, e.NutritionCd[i]);
    }

    [Fact]
    public void RunDay_RelishShortfall_IsReplacedByGrain_AndMarksThePoorDiet()
    {
        var (e, p, h) = Peasants5(); var s = e.HouseholdStore[0];
        s.Create(G(e, "wheat"), 50_000, CreateReason.Seed);
        var r = HouseholdNeeds.RunDay(e, p, h, 0, 0, 12);
        Assert.Equal((400, 0, 100), (r.EatenStapleX100, r.EatenRelishX100, r.SubstitutedX100));
        Assert.Equal(1000, r.FedPermille); Assert.True(r.DietPoor); Assert.True(e.DietPoor[0]);
        Assert.InRange(50_000 - s.Qty(G(e, "wheat")), 5_499, 5_501);                              // 5 measures = 5,500 g of grain
    }

    [Fact] // G-H4
    public void G_H4_NutritionDrainsAndRefills()
    {
        var (p, h) = EconFixture.Household(30);
        var e = EconFixture.SmallEconomy(h, p); var s = e.HouseholdStore[0]; ushort wheat = G(e, "wheat");
        int[] expect = [2960, 2920, 2880, 2930, 2980, 3000];
        for (int day = 0; day < 6; day++)
        {
            s.DestroyUpTo(wheat, long.MaxValue, DestroyReason.Lost);
            s.Create(wheat, day < 3 ? 660 : 5_000, CreateReason.Seed);                          // 660 g = 0.60 measure of the 1.00 needed
            var r = HouseholdNeeds.RunDay(e, p, h, 0, 0, 12);
            Assert.Equal(day < 3 ? 600 : 1000, r.FedPermille);
            Assert.Equal(expect[day], e.NutritionCd[0]);
        }
    }

    [Fact] // G-H4 (starving)
    public void G_H4_Starvation_ReachesZeroOnDay30_AndNobodyDies()
    {
        var (p, h) = EconFixture.Household(30, 4);
        var e = EconFixture.SmallEconomy(h, p);
        for (int day = 1; day <= 30; day++)
        {
            var r = HouseholdNeeds.RunDay(e, p, h, 0, 0, 12);
            Assert.Equal(0, r.FedPermille); Assert.True(r.Hungry && r.DietPoor);
            Assert.Equal(Math.Max(0, 3000 - 100 * day), e.NutritionCd[0]);
        }
        Assert.Equal(0, e.NutritionCd[0]); Assert.Equal(0, e.NutritionCd[1]);
        Assert.Equal(2, h.MemberCount[0]); Assert.Equal(2, p.Count);                              // no deaths in this task
        Assert.True((e.Grievances[0] & Grievance.Hunger) != 0); Assert.Equal(HouseholdTier.Survive, e.Tiers[0]);
        Assert.Equal(0, e.FoodCoverDaysX10[0]);
    }

    [Fact]
    public void Cold_NoFuelBelowFreezing_RaisesTheColdGrievance_AndClearsWhenWarm()
    {
        var (e, p, h) = Peasants5(); var s = e.HouseholdStore[0];
        var r = HouseholdNeeds.RunDay(e, p, h, 0, 0, -5);
        Assert.True(r.Cold); Assert.True((e.Grievances[0] & Grievance.Cold) != 0); Assert.Equal(15_000, r.FuelNeedG);
        s.Create(G(e, "firewood"), 100_000, CreateReason.Seed);
        r = HouseholdNeeds.RunDay(e, p, h, 0, 0, -5);
        Assert.False(r.Cold); Assert.Equal(15_000, r.FuelBurnedG); Assert.True((e.Grievances[0] & Grievance.Cold) == 0);
        Assert.Equal(100_000 - 15_000, s.Qty(G(e, "firewood")));
    }

    [Fact] // G-H5
    public void G_H5_OrderSizing()
    {
        Assert.Equal(74_800, HouseholdNeeds.OrderSizeG(13_200, 88_000));
        Assert.Equal(0, HouseholdNeeds.OrderSizeG(44_000, 88_000)); Assert.Equal(0, HouseholdNeeds.OrderSizeG(60_000, 88_000));
        Assert.Equal(44_001, HouseholdNeeds.OrderSizeG(43_999, 88_000));
        Assert.Equal(88_000, HouseholdNeeds.GrainEquivalentG(400 * 20));                                 // 20 days of 4.00 measures
    }

    [Fact] // G-H8
    public void G_H8_TierRules()
    {
        var (e, _, _) = Peasants5(); var b = e.Basket(SocialClass.Peasant);
        Assert.Equal(HouseholdTier.Survive, HouseholdNeeds.TierOf(80, 200, 300, 12, b));
        Assert.Equal(HouseholdTier.Buffer, HouseholdNeeds.TierOf(150, 200, 300, 12, b));
        Assert.Equal(HouseholdTier.Profit, HouseholdNeeds.TierOf(250, 120, 200, 12, b));
        Assert.Equal(HouseholdTier.Survive, HouseholdNeeds.TierOf(250, 20, 200, -3, b));         // plenty of food but 2 days of fuel in the frost
        Assert.Equal(HouseholdTier.Buffer, HouseholdNeeds.TierOf(250, 20, 200, 5, b));
        Assert.Equal(HouseholdTier.Buffer, HouseholdNeeds.TierOf(250, 120, 140, 12, b));
    }

    [Fact]
    public void RunDay_OnEmptyHousehold_DoesNothing()
    {
        var p = new PersonTable(4, 1); var h = new HouseholdTable(1, 1); h.Add(0, 0, default, 4);
        var e = EconFixture.SmallEconomy(h, p);
        var r = HouseholdNeeds.RunDay(e, p, h, 0, 0, 12);
        Assert.Equal(0, r.Members); Assert.Equal(1000, r.FedPermille);
    }

    [Fact] // G-H13
    public void G_H13_ProcessingOrderDoesNotChangeTheOutcome()
    {
        var pop = EconFixture.Pop; var a = EconFixture.NewEconomy(); var b = EconFixture.NewEconomy();
        int n = pop.Households.Count;
        int day = pop.NowDay;
        for (int d = 0; d < 3; d++)
        {
            for (int h = 0; h < n; h++) HouseholdNeeds.RunDay(a, pop, h, day + d, 4);
            var order = Enumerable.Range(0, n).OrderBy(h => DetRng.Next(3, d, (ulong)h, 5, 0)).ToList();
            foreach (int h in order) HouseholdNeeds.RunDay(b, pop, h, day + d, 4);
        }
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void SeededWorld_EatsForAMonth_WithoutHungerOrLeaks()
    {
        var pop = EconFixture.Pop; var e = EconFixture.NewEconomy(); int n = pop.Households.Count;
        int hungry = 0;
        for (int d = 0; d < 12; d++)
            for (int h = 0; h < n; h++)
            {
                var r = HouseholdNeeds.RunDay(e, pop, h, pop.NowDay + d, 8);
                if (r.Members > 0 && r.Hungry) hungry++;
                Assert.Equal(1000, r.FedPermille);
            }
        Assert.Equal(0, hungry);
        Assert.Equal(0, e.NutritionCd.Count(c => c != 3000));
        Assert.True(e.MassAuditHolds());
        Assert.True(e.Ledger.Audit().IsConserved);
        Assert.Equal(pop.People.Count, e.PersonCount);
    }

    [Fact]
    public void Spoilage_RunsOverEveryStore_AndKeepsTheMassAudit()
    {
        var pop = EconFixture.Pop; var e = EconFixture.NewEconomy();
        e.OpenStore(StoreKind.Granary, EntityId.Make(IdKind.Settlement, 1), 0, 80_000_000).Create(e.Goods.IdOf("wheat"), 5_000_000, CreateReason.Harvest);
        long lost = 0;
        for (int d = 0; d < 15; d++) lost += e.ApplyDailySpoilage(1234, (pop.NowDay + d) * 24L, 12);
        Assert.True(lost > 0);
        Assert.True(e.MassAuditHolds());
        long granary = e.OtherStores[EntityId.Make(IdKind.Settlement, 1)].Qty(e.Goods.IdOf("wheat"));
        Assert.InRange(granary, 4_940_000, 4_960_000);                                           // about 1% of 5 t gone in a season
    }
}
