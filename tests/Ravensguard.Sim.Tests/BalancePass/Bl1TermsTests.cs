using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Tests.Markets;
using Xunit;

namespace Ravensguard.Sim.Tests.BalancePass;

/// <summary>P8-BL-1 (EA_F18, balance pass 3.1): a works' charcoal, ore and barrels are bought on 30-day terms: the goods come at once and the coin goes at the due day, so the stock is paid as it is used.</summary>
public class Bl1TermsTests
{
    [Fact]
    public void BuyingOnTerms_BringsTheGoodsAtOnce_AndKeepsTheCoinInThePurseUntilTheDueDay_ThenPaysTheSellerAndTheToll()
    {
        var k = new MarketKit(); var seller = k.Stock(400_000);
        var buyer = k.Party(PartyKind.Business, 20_000);
        long total = k.TotalCoin;
        var r = k.Book.Buy(buyer, k.Grain, 76_000, 4000, 1, 0, BuyOrder.WorksTermsDays);
        Assert.Equal(76_000, r.Qty); Assert.Equal(7_776, r.PaidMites);                                    // the price is the cash price (GM-3's 7,776)
        Assert.Equal(76_000, k.Qty(buyer, k.Grain));                                                       // the goods are in the works' store now
        Assert.Equal(20_000, k.Coin(buyer)); Assert.Equal(0, k.Coin(seller));                              // and the coin has not moved
        var owed = Assert.Single(k.Book.Payables); Assert.Equal(7_776, owed.Gross); Assert.Equal(1 + BuyOrder.WorksTermsDays, owed.DueDay);
        Assert.Equal(7_776, k.Book.OwedBy(buyer.Purse));
        k.Book.Daily(30); Assert.Equal(20_000, k.Coin(buyer));                                             // not due on day 30
        k.Book.Daily(31);
        Assert.Equal(20_000 - 7_776, k.Coin(buyer)); Assert.Equal(7_776 - 389, k.Coin(seller));            // due on day 31: the seller is paid, less the toll as GM-3's seller is
        Assert.Equal(389, k.Econ.Ledger.Balance(k.Toll).Mites); Assert.Empty(k.Book.Payables);
        Assert.Equal(total, k.TotalCoin); Assert.True(k.Econ.Ledger.Audit().IsConserved); Assert.True(k.Econ.MassAuditHolds());
    }

    [Fact]
    public void ABuyerShortOfCoinAtTheDueDay_PaysWhatItHas_TheRestStaysOwed_AndNothingIsMade()
    {
        var k = new MarketKit(); var seller = k.Stock(400_000);
        var buyer = k.Party(PartyKind.Business, 8_000);
        k.Book.Buy(buyer, k.Grain, 76_000, 4000, 1, 0, BuyOrder.WorksTermsDays);
        k.Econ.Ledger.Transfer(buyer.Purse, k.Party(PartyKind.Household).Purse, new Coin(5_000), TransferReason.Trade);       // the works spent its coin meanwhile
        long total = k.TotalCoin;
        k.Book.Daily(31);
        Assert.Equal(0, k.Coin(buyer)); Assert.Equal(3_000 - 150, k.Coin(seller));                         // 3,000 paid, 150 of it the seller's toll (5%)
        Assert.Equal(7_776 - 3_000, Assert.Single(k.Book.Payables).Gross);
        Assert.Equal(total, k.TotalCoin);
        k.Econ.Ledger.Mint(buyer.Purse, new Coin(10_000), SourceReason.Seed);
        k.Book.Daily(32); Assert.Empty(k.Book.Payables); Assert.True(k.Econ.Ledger.Audit().IsConserved);
        // a debt 60 days past its due is written off: the seller bears it, no coin is made or lost
        var k2 = new MarketKit(); k2.Stock(400_000); var b2 = k2.Party(PartyKind.Business, 0);
        k2.Book.Payables.Add(new Payable { Buyer = b2.Purse.Index, Owner = k2.Party(PartyKind.Business).Purse.Index, Gross = 500, DueDay = 10 });
        k2.Book.Daily(71); Assert.Empty(k2.Book.Payables);
    }

    [Fact]
    public void WhatIsAlreadyOwedIsNotThereToSpendTwice()
    {
        var k = new MarketKit(); k.Stock(400_000);
        var buyer = k.Party(PartyKind.Business, 10_000);
        k.Book.Buy(buyer, k.Grain, 76_000, 4000, 1, 0, BuyOrder.WorksTermsDays);                           // 7,776 owed of 10,000
        var again = k.Book.Buy(buyer, k.Grain, 76_000, 4000, 1, 0, BuyOrder.WorksTermsDays);
        Assert.True(again.PaidMites <= 10_000 - 7_776, $"the second buy was held to what is left ({again.PaidMites})");
    }
}
