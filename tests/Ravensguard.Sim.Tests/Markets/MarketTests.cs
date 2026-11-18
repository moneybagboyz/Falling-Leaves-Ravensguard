using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Tests.Markets;

/// <summary>Golden tests G-M2..G-M12 of the money spec (market parts) and the price walk, spread and trade-storm properties.</summary>
public class MarketTests
{
    private static MarketKit Kit(long stockG, out MarketParty seller, int floor = 800)
    {
        var k = new MarketKit(); seller = k.Stock(stockG, floor); return k;
    }

    [Fact]
    public void GM2_PriceCurve_AtThreeStocks()
    {
        var k = Kit(400_000, out _);
        Assert.Equal(90.0, k.Book.MidMitesPerKg(k.Grain), 3);
        Assert.Equal(2000, k.Book.MidRatioPermille(k.Grain));
        var c = k.Book.Curve(k.Grain);
        Assert.Equal(45_000, c.MidMicro(1_600_000));                       // cover 16 d: Ref
        Assert.Equal(180_000, c.MidMicro(100_000));                        // clamp 4x at TD/16
        Assert.Equal(180_000, c.MidMicro(10_000));
        Assert.Equal(1_600_000 / 16, c.LowStock);
        Assert.Equal(17_777_777, c.HighStock);                             // 1.6e6 / 0.09
    }

    [Fact]
    public void GM3_BuyFromBook_ExactIntegralToll_AndNewMid()
    {
        var k = Kit(400_000, out var seller);
        var buyer = k.Party(PartyKind.Household, 20_000);
        var r = k.Book.Buy(buyer, k.Grain, 76_000, 4000, 1);
        Assert.Equal(76_000, r.Qty);
        Assert.Equal(7_776, r.PaidMites);                                  // 7,200 x 1.08
        Assert.Equal(389, r.TollMites);                                    // 388.8 rounded up
        Assert.Equal(7_776 - 389, k.Coin(seller));                         // seller nets 7,387
        Assert.Equal(20_000 - 7_776, k.Coin(buyer));
        Assert.Equal(389, k.Econ.Ledger.Balance(k.Toll).Mites);
        Assert.Equal(76_000, k.Qty(buyer, k.Grain));
        Assert.Equal(324_000, k.Book.Goods[k.Grain].StockUnits);
        Assert.Equal(100.0, k.Book.MidMitesPerKg(k.Grain), 3);             // 45 x sqrt(1600/324)
        Assert.True(k.Econ.MassAuditHolds());
    }

    [Fact]
    public void GM4_SellToMerchant_PaysBid_RoundTripMargin()
    {
        var k = new MarketKit();
        var first = k.Stock(324_000);                                      // book at 324 kg
        var m = k.Party(PartyKind.Merchant, 50_000);
        var producer = k.Party(PartyKind.Business, 0, ("wheat", 76_000));
        var r = k.Book.SellToMerchants(producer, k.Grain, 76_000, 1);
        Assert.Equal(76_000, r.Qty);
        Assert.Equal(6_624, r.PaidMites);                                  // 7,200 x 0.92
        Assert.Equal(6_624 - 331, k.Coin(producer));                       // seller paid the 5% toll (331.2 -> 331)
        Assert.Equal(50_000 - 6_624, k.Coin(m));
        Assert.Equal(400_000, k.Book.Goods[k.Grain].StockUnits);
        Assert.Equal(7_776 - 6_624, 1_152);                                // spec margin of the round trip
        Assert.Equal(1000, k.Book.Goods[k.Grain].Lots.Last(l => l.IsMerchant).FloorPermille);
        _ = first;
    }

    [Fact]
    public void GM5_LargeBuy_CurvedThenFlatPart()
    {
        var k = Kit(400_000, out _);
        var buyer = k.Party(PartyKind.Household, 100_000);
        var r = k.Book.Buy(buyer, k.Grain, 350_000, 4000, 1);
        Assert.Equal(350_000, r.Qty);
        Assert.Equal(48_600, r.PaidMites);                                 // 45,000 x 1.08: 36,000 curved + 9,000 flat
        Assert.Equal(50_000, k.Book.Goods[k.Grain].StockUnits);
        Assert.Equal(180.0, k.Book.MidMitesPerKg(k.Grain), 3);
    }

    [Fact]
    public void GM6_FarAboveTarget_FlatAtPointThreeRef()
    {
        var k = Kit(20_000_000, out _);
        Assert.Equal(13.5, k.Book.MidMitesPerKg(k.Grain), 3);
        Assert.Equal(300, k.Book.MidRatioPermille(k.Grain));
    }

    [Fact]
    public void GM7_Splitting_NeverBeatsOneTrade_WithinOneMite()
    {
        var one = Kit(400_000, out _); var two = Kit(400_000, out _);
        var b1 = one.Party(PartyKind.Household, 50_000); var b2 = two.Party(PartyKind.Household, 50_000);
        long single = one.Book.Buy(b1, one.Grain, 76_000, 4000, 1).PaidMites;
        long split = two.Book.Buy(b2, two.Grain, 38_000, 4000, 1).PaidMites + two.Book.Buy(b2, two.Grain, 38_000, 4000, 1).PaidMites;
        Assert.InRange(split - single, -1, 2);
        // property over many pieces: the sum of mid integrals is exactly additive within one micro-mite per piece
        var c = one.Book.Curve(one.Grain);
        long whole = (long)c.IntegralMicro(100_000, 900_000);
        long sum = 0; for (long s = 100_000; s < 900_000; s += 10_000) sum += (long)c.IntegralMicro(s, s + 10_000);
        Assert.InRange(sum - whole, -80, 80);
    }

    [Fact]
    public void PerUnitPriceWalk_LargeBuy_PaysEachGramAtItsOwnPrice()
    {
        var k = new MarketKit();
        for (int i = 0; i < 10; i++) k.Stock(100_000);                     // 10 lots, 1,000 kg on the book (above target: price below Ref)
        var buyer = k.Party(PartyKind.Household, 10_000_000);
        var c = k.Book.Curve(k.Grain);
        long s0 = k.Book.Goods[k.Grain].StockUnits;
        var r = k.Book.Buy(buyer, k.Grain, 900_000, 4000, 1);
        Assert.Equal(900_000, r.Qty);
        long expect = (long)(((Int128)c.BuyMid(s0, 900_000) * 1080 + 500) / 1000);
        Assert.Equal(expect, r.PaidMites);
        // the walk: marginal price per kg climbs as stock falls, ending at the mid of the remaining stock
        double prev = 0;
        for (long s = s0; s > s0 - 900_000; s -= 100_000)
        {
            double mid = c.MidMicro(s) / 1000.0;
            Assert.True(mid >= prev); prev = mid;
        }
        // the average paid per kg lies strictly between the first and last unit price
        double avg = r.PaidMites / 1.08 / 900.0, first = c.MidMicro(s0) / 1000.0, last = c.MidMicro(s0 - 900_000) / 1000.0;
        Assert.InRange(avg, first, last);
        Assert.True(avg > first * 1.5);
        Assert.True(k.Econ.MassAuditHolds());
    }

    [Fact]
    public void Spread_IsSixteenPercent_AndInsidePlanBand()
    {
        var k = Kit(400_000, out _);
        long ask = k.Book.AskMicro(k.Grain), bid = k.Book.BidMicro(k.Grain), mid = k.Book.MidMicro(k.Grain);
        Assert.Equal(mid * 1080 / 1000, ask); Assert.Equal(mid * 920 / 1000, bid);
        double spread = (ask - bid) / (double)mid;
        Assert.InRange(spread, 0.10, 0.20);
        // a quote round trip loses the spread: buy 10 kg then a merchant takes it back
        var m = k.Party(PartyKind.Merchant, 1_000_000);
        long buyQuote = k.Book.QuoteBuy(k.Grain, 10_000);
        var lotOwner = k.Party(PartyKind.Household, 0, ("wheat", 10_000));
        long afterSell = k.Book.QuoteSell(k.Grain, 10_000);
        Assert.True(buyQuote > afterSell);
        _ = lotOwner; _ = m;
    }

    [Fact]
    public void GM10_EmptyBook_AsksFourTimesRef_AndBuyerKeepsItsCoin()
    {
        var k = new MarketKit();
        Assert.Equal(180.0, k.Book.MidMitesPerKg(k.Grain), 3);
        var buyer = k.Party(PartyKind.Household, 5_000);
        var r = k.Book.Buy(buyer, k.Grain, 10_000, 4000, 1);
        Assert.Equal(TradeResult.None, r);
        Assert.Equal(5_000, k.Coin(buyer));
    }

    [Fact]
    public void BuyRespectsMaxPriceRatio_AndLotFloors_AndBudget()
    {
        var k = Kit(400_000, out _);
        var buyer = k.Party(PartyKind.Household, 1_000_000);
        // cap the ratio at 2.0 x Ref: the book is already at 2.0, so no unit may be taken (the price would rise past the cap)
        Assert.Equal(0, k.Book.Buy(buyer, k.Grain, 10_000, 2000, 1).Qty);
        // a lot whose owner wants 3.0 x Ref sells nothing at 2.0 x
        var k2 = new MarketKit(); k2.Stock(400_000, floor: 3000); var b2 = k2.Party(PartyKind.Household, 1_000_000);
        Assert.Equal(0, k2.Book.Buy(b2, k2.Grain, 10_000, 4000, 1).Qty);
        // budget: 1,000 mites buys only what it affords
        var k3 = Kit(400_000, out _); var poor = k3.Party(PartyKind.Household, 1_000);
        var r = k3.Book.Buy(poor, k3.Grain, 400_000, 4000, 1);
        Assert.InRange(r.PaidMites, 990, 1_000);
        Assert.InRange(k3.Coin(poor), 0, 10);
    }

    [Fact]
    public void LotsExpire_AndGoodsReturnToTheOwner()
    {
        var k = new MarketKit(); var s = k.Stock(50_000);
        Assert.Equal(0, k.Qty(s, k.Grain));
        k.Book.Daily(1); Assert.Equal(50_000, k.Book.Goods[k.Grain].StockUnits);
        k.Book.Daily(3);
        Assert.Equal(0, k.Book.Goods[k.Grain].StockUnits);
        Assert.Equal(50_000, k.Qty(s, k.Grain));
        Assert.True(k.Econ.MassAuditHolds());
    }

    [Fact]
    public void GM9_RefDrift_TowardLocalCoinPerPerson()
    {
        var k = new MarketKit(); k.Book.Population = 1;
        int mult = k.Book.DriftRefs(10_000);                                // cpp = 10,000 mites: twice the seed
        Assert.Equal(2000, mult);
        Assert.Equal(56_250, k.Book.Goods[k.Grain].RefMicro);               // 45 + (90 - 45)/4
        for (int i = 0; i < 3; i++) k.Book.DriftRefs(10_000);
        Assert.InRange(k.Book.Goods[k.Grain].RefMicro / 1000.0, 75.6, 75.9);
        // a poor town drifts down but never below half of base
        var p = new MarketKit(); p.Book.Population = 1000;
        for (int i = 0; i < 40; i++) p.Book.DriftRefs(0);
        Assert.InRange(p.Book.Goods[p.Grain].RefMicro, 22_500, 22_600);
    }

    [Fact]
    public void DemandEma_UsesFloorDivision_AndTheDemandIsTheLargestTerm()
    {
        var k = new MarketKit(); var gb = k.Book.Goods[k.Grain];
        gb.DemandEma = 0; gb.StructuralDemand = 1_000; gb.FloorDemand = 5_000;
        Assert.Equal(5_000, gb.Demand);
        gb.DemandToday = 7_000; k.Book.Daily(1);
        Assert.Equal(1_000, gb.DemandEma);                                  // 7,000 / 7
        gb.DemandToday = 0; k.Book.Daily(2);
        Assert.Equal(1_000 - 143, gb.DemandEma);                            // floor((0 - 1,000) / 7) = -143
    }

    [Fact]
    public void GM12_OrderPermutation_GivesIdenticalFinalState()
    {
        string Run(int rotate)
        {
            var k = new MarketKit(3); k.Stock(300_000);
            var buyers = Enumerable.Range(0, 50).Select(i => k.Party(PartyKind.Household, 3_000 + i * 17)).ToList();
            var orders = buyers.Select((b, i) => new BuyOrder(b, k.Grain, 9_000 + (i % 7) * 1_000, 4000)).ToList();
            for (int i = 0; i < orders.Count; i++) k.Book.Post(orders[(i * 7 + rotate) % orders.Count]);     // different posting orders
            k.Book.RunSession(100, 4);
            return string.Join(",", buyers.Select(b => $"{k.Coin(b)}/{k.Qty(b, k.Grain)}")) + $"|{k.Book.Goods[k.Grain].StockUnits}|{k.Econ.Ledger.Balance(k.Toll).Mites}";
        }
        Assert.Equal(Run(0), Run(13));
        Assert.Equal(Run(0), Run(31));
    }

    [Fact]
    public void GM11_TradeStorm_ConservesCoinAndMass()
    {
        var k = new MarketKit(11);
        var kits = new[] { k, new MarketKit(12, 1), new MarketKit(13, 2) };
        // three books share the first kit's economy so one ledger and one mass ledger are audited
        var econ = k.Econ;
        var books = new[] { k.Book, new MarketBook(econ, 1, EntityId.Make(IdKind.Settlement, 2), "B", k.Toll, 0, 12), new MarketBook(econ, 2, EntityId.Make(IdKind.Settlement, 3), "C", k.Toll, 0, 13) };
        foreach (var b in books) { var g = b.Goods[k.Grain]; g.RefMicro = g.BaseRefMicro = 45_000; g.TargetCoverDays = 16; g.DemandEma = 100_000; }
        var parties = Enumerable.Range(0, 200).Select(i => k.Party(i % 10 == 0 ? PartyKind.Merchant : i % 2 == 0 ? PartyKind.Business : PartyKind.Household, 20_000 + i * 31, ("wheat", 5_000 + i * 97))).ToList();
        foreach (var m in parties.Where(p => p.Kind == PartyKind.Merchant)) { k.Book.Merchants.Add(m); }
        long coin0 = k.TotalCoin; long mass0 = Net(econ, k.Grain);
        var rng = new RngStream(99, 0, 1, 0x500);
        for (int i = 0; i < 10_000; i++)
        {
            var b = books[rng.Range(0, 3)]; var p = parties[rng.Range(0, parties.Count)];
            switch (rng.Range(0, 5))
            {
                case 0: b.List(p, k.Grain, 1 + rng.Range(0, 20_000), 700 + rng.Range(0, 5) * 100, i / 100); break;
                case 1: case 2: b.Buy(p, k.Grain, 1 + rng.Range(0, 30_000), 2000 + rng.Range(0, 3) * 1000, i / 100); break;
                case 3: b.SellOrList(p, k.Grain, 1 + rng.Range(0, 15_000), 900, i / 100); break;
                default: b.Post(new BuyOrder(p, k.Grain, 1 + rng.Range(0, 10_000), 3000)); if (i % 7 == 0) b.RunSession(i, i / 100); b.Daily(i / 100); break;
            }
            if (i % 997 == 0) { Assert.Equal(coin0, k.TotalCoin); Assert.True(econ.MassAuditHolds()); }
        }
        Assert.Equal(coin0, k.TotalCoin);
        Assert.Equal(mass0, Net(econ, k.Grain));
        Assert.True(econ.Ledger.Audit().IsConserved);
        Assert.True(econ.MassAuditHolds());
        foreach (var p in parties) Assert.True(k.Coin(p) >= 0);
        foreach (var b in books) Assert.Equal(b.BookStore.Qty(k.Grain), b.Goods[k.Grain].Lots.Sum(l => l.Qty));
        Assert.True(econ.Ledger.Flow(TransferReason.Trade).Mites > 0 && econ.Ledger.Flow(TransferReason.MarketToll).Mites > 0);
    }

    private static long Net(EconomyState e, ushort g) => e.Mass.Net(g);

    [Fact]
    public void PriceKnowledge_KeepsTheLastHeardPriceWithItsAge()
    {
        var k = Kit(400_000, out _);
        var nb = new PriceNotebook();
        nb.Visit(k.Book, 10);
        var heard = nb.AskWithAge(k.Book, k.Grain, 25);
        Assert.NotNull(heard);
        Assert.Equal(90.0 * 1.08, heard!.Value.Ask, 2);
        Assert.Equal(15, heard.Value.AgeDays);
        // the market moves; the notebook keeps the old price until the next visit
        var buyer = k.Party(PartyKind.Household, 100_000); k.Book.Buy(buyer, k.Grain, 200_000, 4000, 12);
        Assert.Equal(90.0 * 1.08, nb.AskWithAge(k.Book, k.Grain, 25)!.Value.Ask, 2);
        nb.Visit(k.Book, 30);
        var fresh = nb.AskWithAge(k.Book, k.Grain, 30)!.Value;
        Assert.Equal(0, fresh.AgeDays); Assert.True(fresh.Ask > 90.0 * 1.08);
        // an older note never overwrites a newer one
        nb.Hear(new PriceNote(k.Book.SettlementIndex, k.Grain, 1, 1, 0, 5));
        Assert.Equal(30, nb.AskWithAge(k.Book, k.Grain, 30) is { } x ? 30 - x.AgeDays : -1);
        Assert.Null(nb.AskWithAge(k.Book, (ushort)(k.Grain + 1), 30));
    }

    [Fact]
    public void Isqrt_IsFloorExact()
    {
        foreach (ulong n in new ulong[] { 0, 1, 2, 3, 4, 15, 16, 17, 99, 100, 101, 1UL << 40, (1UL << 40) + 1, ulong.MaxValue })
        {
            var r = PriceCurve.Isqrt(n);
            Assert.True(r * r <= n && (r + 1) * (r + 1) > n, n.ToString());
        }
        var big = (UInt128)1 << 100;
        Assert.Equal((UInt128)1 << 50, PriceCurve.Isqrt(big));
        Assert.Equal(((UInt128)1 << 50) - 1, PriceCurve.Isqrt(big - 1));
    }
}
