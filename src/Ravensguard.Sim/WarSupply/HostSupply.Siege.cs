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
                if (lack > 0)          // the realm pays for its siege train: what the chest lacks comes from the realm's treasury (a transfer; nothing is minted)
                {
                    var purse = RealmPurse(a.Realm); if (purse.IsNone) purse = rt.Treasury;
                    if (!purse.IsNone && purse != rt.Chest) { long move = Math.Min(lack, _econ.Ledger.Balance(purse).Mites); if (move > 0) _econ.Ledger.Transfer(purse, rt.Chest, new Coin(move), TransferReason.Hoard); }
                }
                var t = bk.Buy(rt.Party, good, want, SupplyConfig.BuyMaxRatioPermille, day);
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
