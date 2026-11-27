using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;

namespace Ravensguard.Sim.WarSupply;

public sealed partial class HostSupply
{
    /// <summary>
    /// The siege train's purchase (P7-SG wiring): the host's quartermaster buys <paramref name="qty"/> units of a good (timber, iron, rope) from the books within the given reach (the host's own buy radius, or the wider reach of a siege train that carts its goods in), nearest first, at the books' asks and paid from the host's chest
    /// (the books move the coin and the goods; nothing is made). The goods land in the host's baggage. Returns the units bought.
    /// </summary>
    public (long Got, int Farthest) BuyForSiege(HostId h, ushort good, long qty, int day, int reachM)
    {
        if (qty <= 0 || !Exists(h)) return (0, -1);
        var a = Acc(h); var rt = Resolve(a); long got = 0; int far = -1; long farD2 = -1;
        foreach (var bk in BooksWithin(a, reachM))
        {
            for (int pass = 0; pass < 8 && got < qty; pass++)          // a book sells a share of its stock at a time (the price-ratio cap): the quartermaster asks again while it yields
            {
                long want = qty - got, quote = bk.QuoteBuy(good, Math.Min(want, bk.Goods[good].StockUnits)), lack = quote - _econ.Ledger.Balance(rt.Chest).Mites;
                var source = PurseId.None; long before = _econ.Ledger.Balance(rt.Chest).Mites, moved = 0;
                if (lack > 0)          // the realm pays for its siege train: what the chest lacks comes from the realm's treasury as a subsidy (a transfer; nothing is minted)
                {
                    source = RealmPurse(a.Realm); if (source.IsNone) source = rt.Treasury;
                    if (!source.IsNone && source != rt.Chest) { long move = Math.Min(lack, _econ.Ledger.Balance(source).Mites); if (move > 0 && _econ.Ledger.Transfer(source, rt.Chest, new Coin(move), TransferReason.Subsidy)) moved = move; }
                }
                var t = bk.Buy(rt.Party, good, want, SupplyConfig.BuyMaxRatioPermille, day);
                if (moved > 0)          // P7R-9: the buy is capped by the books' price ratio and may cost less than the quote: what of the subsidy was not spent goes back to where it came from
                {
                    long spent = before + moved - _econ.Ledger.Balance(rt.Chest).Mites, back = moved - Math.Max(0, spent - before);
                    if (back > 0) _econ.Ledger.Transfer(rt.Chest, source, new Coin(back), TransferReason.Subsidy);
                }
                if (t.Qty <= 0) break;
                got += Math.Min(t.Qty, want);
                var c = _pop.Region.Settlements[bk.SettlementIndex]; long dx = c.CenterX - a.PlaceXM, dy = c.CenterY - a.PlaceYM;
                if (dx * dx + dy * dy > farD2) { farD2 = dx * dx + dy * dy; far = bk.SettlementIndex; }
            }
            if (got >= qty) break;
        }
        return (got, far);
    }
}
