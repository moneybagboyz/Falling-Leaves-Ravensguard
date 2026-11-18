using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Economy.Markets;

/// <summary>
/// Carters between the books of one fief (integration decision I26, a stand-in for the caravans of the logistics spec): once a day the richest merchant of a book that is
/// short of a good (under half its target cover) buys it from the book of the same fief that holds the most beyond its own cover, at that book's price, and lists it at home
/// as a merchant lot. Goods move by <see cref="Store.Move"/> and coin by the ledger (trade), so both books stay exact; the producer's coin goes to the surplus village or town
/// and the town gets its grain (without it a town, which grows no grain, starved while the villages round it sold theirs to the continent).
/// </summary>
public sealed class RegionalTrade
{
    private readonly MarketSystem _markets; private readonly EconomyState _econ; private readonly Region _region;
    private readonly List<MarketBook>[] _byFief;
    private readonly bool[] _skip;
    public long MovedUnits, SpentMites;
    public long[] MovedByGood;

    public RegionalTrade(MarketSystem markets, EconomyState econ, Region region)
    {
        _markets = markets; _econ = econ; _region = region;
        int nf = Math.Max(1, region.Fiefs.Count);
        _byFief = new List<MarketBook>[nf + 1]; for (int i = 0; i < _byFief.Length; i++) _byFief[i] = [];
        foreach (var b in markets.AllBooks()) _byFief[Slot(b.SettlementIndex)].Add(b);
        var c = econ.Goods; _skip = new bool[c.Count]; MovedByGood = new long[c.Count];
        foreach (var g in new[] { "milk", "meat_fresh", "fish_fresh", "eggs", "vegetables", "sheep", "cow", "pig", "hay", "straw", "whey", "offal", "silver" })
            if (c.TryIdOf(g, out ushort id)) _skip[id] = true;
    }

    private int Slot(int settlement) { int f = _region.Settlements[settlement].FiefIndex; return f >= 0 && f < _byFief?.Length - 1 ? f : _byFief!.Length - 1; }

    public void Daily(GameTime now)
    {
        int day = (int)now.TotalDays; var led = _econ.Ledger; int n = _econ.Goods.Count;
        foreach (var b in _markets.AllBooks())
        {
            if (b.Merchants.Count == 0) continue;
            var sibs = _byFief[Slot(b.SettlementIndex)]; if (sibs.Count < 2) continue;
            MarketParty? m = null; long best = -1;
            foreach (var x in b.Merchants) { long c = led.Balance(x.Purse).Mites; if (c > best) { best = c; m = x; } }
            if (m == null || best < 2000) continue;
            long budget = best * 4 / 10;
            for (ushort g = 0; g < n && budget > 0; g++)
            {
                if (_skip[g]) continue;
                var gb = b.Goods[g]; long cover = gb.Demand * gb.TargetCoverDays;
                if (cover <= 1 || gb.StockUnits * 2 >= cover) continue;
                long want = Math.Min(cover * 8 / 10 - gb.StockUnits, Math.Max(1, cover / 3));
                if (want <= 0) continue;
                MarketBook? src = null; long bestSurplus = 0;
                foreach (var s in sibs)
                {
                    if (ReferenceEquals(s, b)) continue;
                    var sg = s.Goods[g]; long sc = sg.Demand * sg.TargetCoverDays;
                    long surplus = sg.StockUnits - Math.Max(sc, 1);
                    if (surplus > bestSurplus) { bestSurplus = surplus; src = s; }
                }
                if (src == null) continue;
                long q = Math.Min(want, bestSurplus / 2);
                if (q <= 0) continue;
                long cash = led.Balance(m.Purse).Mites;
                var r = src.Buy(m, g, q, 1300, day);
                if (r.Qty <= 0) continue;
                b.List(m, g, r.Qty, 1000, day, merchantLot: true);
                MovedUnits += r.Qty; MovedByGood[g] += r.Qty; SpentMites += r.PaidMites; budget -= r.PaidMites;
                _ = cash;
            }
        }
    }
}
