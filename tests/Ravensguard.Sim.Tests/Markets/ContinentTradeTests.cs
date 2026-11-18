using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Tests.Markets;

/// <summary>The continent as a trade partner (integration decision I13): it tops up a book that has run short, carries off a glut, can pay only what it holds, never makes coin and never sells what the region makes.</summary>
public class ContinentTradeTests
{
    private static readonly GameTime Day = GameTime.FromGameDays(10);

    private static (MarketKit K, ContinentTrade T, MarketParty Merchant, PurseId Purse) Setup(long continentMites, bool madeLocally = false)
    {
        var k = new MarketKit(); var m = k.Party(PartyKind.Merchant, 5_000_000);
        k.Book.Goods[k.Grain].StructuralDemand = 100_000;                          // households want it: the book is supplied, not forgotten
        var purse = k.Econ.Ledger.Open(PurseKind.Treasury, EntityId.Make(IdKind.Other, 0));
        if (continentMites > 0) k.Econ.Ledger.Mint(purse, new Coin(continentMites), SourceReason.Seed);
        var t = new ContinentTrade(() => [k.Book], k.Econ, purse, madeLocally ? [k.Grain] : null);
        return (k, t, m, purse);
    }

    [Fact]
    public void AShortBook_IsToppedUp_AtTheMarkup_PaidToTheContinent_AndCoinAndGoodsBalance()
    {
        var (k, t, m, purse) = Setup(0);
        long coin0 = k.TotalCoin, mass0 = k.Econ.Mass.Net(k.Grain);
        t.Daily(Day);
        var gb = k.Book.Goods[k.Grain];
        Assert.True(gb.StockUnits > 0 && t.ImportedUnits[k.Grain] == gb.StockUnits);
        Assert.True(k.Econ.Ledger.Balance(purse).Mites > 0 && k.Coin(m) < 5_000_000);
        Assert.Equal(coin0, k.TotalCoin);                                       // coin moved, none made
        Assert.Equal(gb.StockUnits, k.Econ.Mass.Created(k.Grain, CreateReason.Import));
        Assert.Equal(gb.StockUnits, k.Book.BookStore.Qty(k.Grain));
    }

    [Fact]
    public void WhatTheRegionMakes_IsNeverImported()
    {
        var (k, t, _, purse) = Setup(0, madeLocally: true);
        t.Daily(Day);
        Assert.Equal(0, k.Book.Goods[k.Grain].StockUnits); Assert.Equal(0, k.Econ.Ledger.Balance(purse).Mites);
    }

    [Fact]
    public void AGlut_IsCarriedOff_OnlyAsFarAsTheContinentCanPay_AndTheSellerPaysTheToll()
    {
        var (k, t, _, purse) = Setup(1_000_000);
        var seller = k.Stock(60_000_000);                                       // far above 2.8 x cover (16 days x 100 kg = 1.6 t)
        long stock0 = k.Book.Goods[k.Grain].StockUnits, coin0 = k.TotalCoin;
        t.Daily(Day);
        var gb = k.Book.Goods[k.Grain];
        Assert.True(gb.StockUnits < stock0 && t.ExportedUnits[k.Grain] == stock0 - gb.StockUnits);
        Assert.True(k.Coin(seller) > 0);
        Assert.True(k.Econ.Ledger.Balance(purse).Mites < 1_000_000 && k.Econ.Ledger.Balance(purse).Mites >= 0);
        Assert.True(k.Econ.Ledger.Balance(k.Toll).Mites > 0);                   // the 5 percent toll of the sale
        Assert.Equal(coin0, k.TotalCoin);
        Assert.Equal(gb.StockUnits, gb.Lots.Sum(l => l.Qty)); Assert.Equal(gb.StockUnits, k.Book.BookStore.Qty(k.Grain));
        Assert.Equal(stock0 - gb.StockUnits, k.Econ.Mass.Destroyed(k.Grain, DestroyReason.Export));
    }

    [Fact]
    public void AnEmptyContinentPurse_ExportsNothing()
    {
        var (k, t, _, _) = Setup(0);
        k.Stock(60_000_000); long stock0 = k.Book.Goods[k.Grain].StockUnits;
        t.Daily(Day);
        Assert.Equal(stock0, k.Book.Goods[k.Grain].StockUnits);
    }
}
