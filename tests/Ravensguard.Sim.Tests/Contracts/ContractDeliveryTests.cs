using Ravensguard.Sim.Contracts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Xunit;

namespace Ravensguard.Sim.Tests.Contracts;

/// <summary>P8-BL-1 (balance pass 3.2, BL-6): the delivery contracts nobody took (phase-6 G2: 30,754 posted in a century, none taken).</summary>
public partial class ContractTests
{
    private static readonly EntityId CarterFaction = EntityId.Make(IdKind.Faction, 0x4C00_0000UL);

    [Fact]
    public void BL6_ACarterHouseholdTakesADelivery_ByTheScorer_TheGoodsAreHauledForReal_AndItIsPaidFromTheEscrow()
    {
        var r = new Rig { Risk = 0 }; r.Needs.Add(r.CharcoalNeed());
        var coPurse = r.L.Open(PurseKind.Household, EntityId.Make(IdKind.Household, 700)); r.L.Mint(coPurse, new Coin(1_000), SourceReason.Seed);
        r.Companies.Add(new CompanyInfo(CarterFaction, "Ashby carters", 0, 20, 1, coPurse.Index, "carter"));
        var c = r.Scan(10).Single();
        Assert.Equal(10 + 10, c.DeadlineDay);                                                           // withdrawn after ten days, not six
        var hauled = new List<(int Id, long Fee)>(); r.HaulHook = (x, fee) => { hauled.Add((x.Id, fee)); return true; };
        Assert.Equal(c.Id, Assert.Single(Companies.TakeAll(r.W, r.B, 10)).Id);                         // a household with a cart scores a delivery like any job
        Assert.True(c.ByCompany); Assert.Equal(CarterFaction, c.Taker);
        long before = r.L.Balance(coPurse).Mites;
        Assert.Equal(0, Companies.Resolve(r.W, r.B, 10));
        Assert.Equal(1, Companies.Resolve(r.W, r.B, 11));
        Assert.Equal((c.Id, 0L), Assert.Single(hauled));                                                // the escrow pays the carter, so no fee from the buyer
        Assert.Equal(WorkState.Done, c.State); Assert.Equal(before + c.PayMites + c.BondMites, r.L.Balance(coPurse).Mites);
        Assert.True(r.L.Audit().IsConserved);
    }

    [Fact]
    public void BL6_ADeliveryThatCannotBeHauled_FailsAndTheEscrowGoesHome()
    {
        var r = new Rig(); r.Needs.Add(r.CharcoalNeed());
        var coPurse = r.L.Open(PurseKind.Household, EntityId.Make(IdKind.Household, 701)); r.L.Mint(coPurse, new Coin(1_000), SourceReason.Seed);
        r.Companies.Add(new CompanyInfo(CarterFaction, "Ashby carters", 0, 20, 1, coPurse.Index, "carter"));
        r.Risk = 0; var c = r.Scan(10).Single(); long issuer0 = r.L.Balance(r.SmithyPurse).Mites + c.PayMites;
        r.HaulHook = (_, _) => false;                                                                   // no book holds the goods
        Companies.TakeAll(r.W, r.B, 10); Companies.Resolve(r.W, r.B, 11);
        Assert.Equal(WorkState.Failed, c.State); Assert.Equal(issuer0, r.L.Balance(r.SmithyPurse).Mites);
        Assert.True(r.L.Audit().IsConserved);
    }

    [Fact]
    public void BL6_ADeliveryNobodyTookInTenDays_IsWithdrawn_AndItsGoodsGoByTheSellersOwnCarterAtThePostedPay()
    {
        var r = new Rig(); r.Needs.Add(r.CharcoalNeed());
        var c = r.Scan(10).Single(); long pay = c.PayMites, issuer0 = r.L.Balance(r.SmithyPurse).Mites;
        var hauled = new List<(int Id, long Fee)>(); r.HaulHook = (x, fee) => { hauled.Add((x.Id, fee)); return true; };
        ContractSystem.Expiries(r.W, r.B, 19); Assert.Empty(hauled); Assert.Equal(WorkState.Open, c.State);   // not yet
        ContractSystem.Expiries(r.W, r.B, 20);
        Assert.Equal(WorkState.Expired, c.State);
        Assert.Equal((c.Id, pay), Assert.Single(hauled));                                                // withdrawn, then hauled at the posted pay out of the buyer's own purse
        Assert.Equal(issuer0 + pay, r.L.Balance(r.SmithyPurse).Mites);                                  // the escrow came home first
        Assert.True(r.L.Audit().IsConserved);
    }

    [Fact]
    public void BL6_TheCarterIsOnlyForDeliveries()
    {
        Assert.True(Companies.Fits("carter", "deliver"));
        Assert.False(Companies.Fits("carter", "guard")); Assert.False(Companies.Fits("carter", "build"));
        Assert.False(Companies.Fits("mercenary", "deliver"));
    }
}
