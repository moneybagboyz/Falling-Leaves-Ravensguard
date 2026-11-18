using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Economy.Markets;

/// <summary>
/// The continent as a trade partner of the region until caravans exist (integration decision I13, plan Phase 2 "continent tick v1: trade into the region"). Once a day each
/// book's richest merchant buys what the book is short of from the continent at a markup, and the continent buys what the book holds beyond its cover at a discount: raw
/// materials and manufactures between <see cref="ImportMarkupPermille"/> and <see cref="ExportDiscountPermille"/> of the reference price, the staples inside a tighter and
/// wider band (so a thin market is topped up and a glut is carried off, which is what bounds a staple's price). Food that is eaten fresh, livestock, fodder and firewood stay
/// local. The continent pays and is paid through one purse (<see cref="Purse"/>) that holds a finite float, so it can export only as much as it has imported plus what it
/// started with and never creates coin; goods enter as <c>Import</c> and leave as <c>Export</c> in the mass ledger.
/// </summary>
public sealed class ContinentTrade
{
    public const int ImportMarkupPermille = 1100, ExportDiscountPermille = 900, StapleImportMarkupPermille = 1050, StapleExportDiscountPermille = 950;
    /// <summary>The continent steps in only at the extremes: below 0.3 of the target cover the price curve gives 1.83 x Ref, above 2.8 of it 0.6 x Ref, which is the band the spec asks staples to keep.</summary>
    public const int ShortBelowPermilleOfCover = 300, GlutAbovePermilleOfCover = 2800;

    private readonly MarketSystem _markets;
    private readonly EconomyState _econ;
    private readonly bool[] _local, _staple;
    public PurseId Purse { get; }

    public long ImportedMites, ExportedMites;
    public long[] ImportedUnits, ExportedUnits;

    public ContinentTrade(MarketSystem markets, EconomyState econ, PurseId purse)
    {
        _markets = markets; _econ = econ; Purse = purse;
        var c = econ.Goods; int n = c.Count;
        _local = new bool[n]; _staple = new bool[n];
        foreach (var g in new[] { "milk", "meat_fresh", "fish_fresh", "eggs", "vegetables", "cheese", "sheep", "cow", "pig", "hay", "straw", "firewood", "silver", "arming_sword", "whey", "sawdust", "offal" })
            if (c.TryIdOf(g, out ushort id)) _local[id] = true;
        foreach (var g in new[] { "wheat", "rye", "barley", "oats", "peas", "beans", "flour", "bread", "ale", "salt", "malt" })
            if (c.TryIdOf(g, out ushort id)) _staple[id] = true;
        ImportedUnits = new long[n]; ExportedUnits = new long[n];
    }

    /// <summary>One day of trade in every book that has a merchant (ascending settlement, then good).</summary>
    public void Daily(GameTime now)
    {
        int day = (int)now.TotalDays; var led = _econ.Ledger; int n = _econ.Goods.Count;
        foreach (var b in _markets.AllBooks())
        {
            if (b.Merchants.Count == 0) continue;
            MarketParty? m = null; long best = -1;
            foreach (var x in b.Merchants) { long c = led.Balance(x.Purse).Mites; if (c > best) { best = c; m = x; } }
            if (m == null) continue;
            for (ushort g = 0; g < n; g++)
            {
                if (_local[g]) continue;
                var gb = b.Goods[g]; long cover = gb.Demand * gb.TargetCoverDays;
                if (cover <= 1) continue;
                bool staple = _staple[g];
                if (gb.StockUnits * 1000 < cover * ShortBelowPermilleOfCover && (gb.StructuralDemand > 0 || gb.LastTradedDay >= 0)) Import(b, m, g, gb, cover, staple ? StapleImportMarkupPermille : ImportMarkupPermille, day);
                else if (gb.StockUnits * 1000 > cover * GlutAbovePermilleOfCover) Export(b, g, gb, cover, staple ? StapleExportDiscountPermille : ExportDiscountPermille, day);
            }
        }
    }

    private static long Price(long unitsRefMicro, long qty, int permille) => (long)(((Int128)unitsRefMicro * qty * permille / 1000 + 500_000) / 1_000_000);

    private void Import(MarketBook b, MarketParty m, ushort g, GoodBook gb, long cover, int markup, int day)
    {
        var led = _econ.Ledger;
        long want = Math.Min(cover * 6 / 10 - gb.StockUnits, Math.Max(1, cover / 4));
        if (want <= 0) return;
        long cash = led.Balance(m.Purse).Mites;
        long unit = gb.RefMicro * markup / 1000;
        if (unit <= 0) return;
        long afford = (long)((Int128)(cash * 3 / 10) * 1_000_000 / unit);
        long q = Math.Min(want, afford);
        if (q <= 0) return;
        long cost = Price(gb.RefMicro, q, markup);
        if (cost > 0 && !led.Transfer(m.Purse, Purse, new Coin(cost), TransferReason.Trade)) return;
        m.Store.Create(g, q, CreateReason.Import);
        b.List(m, g, q, 1000, day, merchantLot: true);
        ImportedMites += cost; ImportedUnits[g] += q;
    }

    private void Export(MarketBook b, ushort g, GoodBook gb, long cover, int discount, int day)
    {
        var led = _econ.Ledger;
        long take = Math.Max(1, (gb.StockUnits - cover * 2000 / 1000) / 2);
        long budget = led.Balance(Purse).Mites;
        long unitMicro = gb.RefMicro * discount / 1000; if (unitMicro <= 0) return;
        long maxQ = (long)((Int128)budget * 1_000_000 / unitMicro);
        take = Math.Min(take, maxQ);
        if (take <= 0) return;
        b.Sync(g);
        long left = take, got = 0;
        for (int i = gb.Lots.Count - 1; i >= 0 && left > 0; i--)               // the dearest floors first: the lots the market would sell last
        {
            var lot = gb.Lots[i]; long q = Math.Min(lot.Qty, left);
            if (q <= 0) continue;
            long pay = Price(gb.RefMicro, q, discount);
            if (pay > 0)
            {
                if (!led.Transfer(Purse, lot.Owner.Purse, new Coin(pay), TransferReason.Trade)) break;
                long toll = (long)(((Int128)pay * _econ.Money.MarketTollPermille + 500) / 1000);
                if (toll > 0) led.Transfer(lot.Owner.Purse, b.TollPurse, new Coin(toll), TransferReason.MarketToll);
            }
            b.BookStore.Destroy(g, q, DestroyReason.Export);
            lot.Qty -= q; gb.StockUnits -= q; left -= q; got += q;
            if (lot.Qty == 0) gb.Lots.RemoveAt(i);
            ExportedMites += pay;
        }
        if (got > 0) ExportedUnits[g] += got;
    }
}
