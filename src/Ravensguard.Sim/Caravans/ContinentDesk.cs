using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Economy.Markets;

public sealed partial class ContinentTrade
{
    public const int ImportMarkupPermille = 1100, ExportDiscountPermille = 900, StapleImportMarkupPermille = 1050, StapleExportDiscountPermille = 950;
    public const int ShortBelowPermilleOfCover = 300, GlutAbovePermilleOfCover = 2800;
    private bool[]? _staple, _deskSkip;
    public long DeskImportedMites, DeskExportedMites;

    /// <summary>
    /// The border desk: the merchants' agents at the gates, who top a book up from the neighbours when it is badly short and carry off a glut (the old stand-in's thresholds and prices, kept
    /// because the region's balance was tuned on them), now against the real continent: what the neighbours pay is limited by their summaries' coin for the season (<see cref="EdgeTrade.PayBudgetMites"/>),
    /// coin that crosses is booked as emigrated or immigrated on the region's link and settled into the summaries at the next tick, goods leave as exports and arrive as imports.
    /// Caravans (planner and hourly step) carry the larger, planned flows on top of it and are the only ones that ever show on the road.
    /// </summary>
    private void DeskDaily(GameTime now)
    {
        var rl = Link.Mine; if (rl.Edges.Count == 0) return;
        var st = Link.State;
        if (_staple == null)
        {
            var c = _econ.Goods; int n = c.Count; _staple = new bool[n]; _deskSkip = new bool[n];
            foreach (var g in new[] { "wheat", "rye", "barley", "oats", "peas", "beans", "flour", "bread", "ale", "salt", "malt" }) if (c.TryIdOf(g, out ushort id)) _staple[id] = true;
            for (int i = 0; i < n; i++) _deskSkip[i] = _local[i];
            if (c.TryIdOf("bread", out ushort b)) _deskSkip[b] = false;
        }
        int day = (int)now.TotalDays; var led = _econ.Ledger; int nGoods = _econ.Goods.Count;
        EdgeTrade? Edge() { foreach (var e in rl.Edges) { var nb = st.Summaries[e.Neighbour]; if (!nb.Detailed && nb.Pop > 0 && !st.Edges[e.Edge].Closed && e.PayBudgetMites - e.CoinNetIn > 0) return e; } return null; }
        EdgeTrade? Any() { foreach (var e in rl.Edges) if (!st.Summaries[e.Neighbour].Detailed) return e; return null; }
        foreach (var b in _books())
        {
            if (b.Merchants.Count == 0) continue;
            MarketParty? m = null; long best = -1;
            foreach (var x in b.Merchants) { long c = led.Balance(x.Purse).Mites; if (c > best) { best = c; m = x; } }
            if (m == null) continue;
            for (ushort g = 0; g < nGoods; g++)
            {
                if (_deskSkip![g]) continue;
                var gb = b.Goods[g]; long cover = gb.Demand * gb.TargetCoverDays;
                if (cover <= 1) continue;
                bool staple = _staple![g];
                // the thresholds lean with the region's coin (ContinentTrade.BoundDeskExports, decision I42): a region short of coin sells its gluts sooner, one over its path buys sooner
                if (!_made[g] && gb.StockUnits * 1000 < cover * _shortBelowPermille && (gb.StructuralDemand > 0 || gb.LastTradedDay >= 0 || gb.DemandEma > 0)) DeskImport(b, m, g, gb, cover, staple ? StapleImportMarkupPermille : ImportMarkupPermille, day, Any());
                else if (gb.StockUnits * 1000 > cover * _glutAbovePermille) DeskExport(b, g, gb, cover, staple ? StapleExportDiscountPermille : ExportDiscountPermille, Edge());
            }
        }
    }

    private void DeskImport(MarketBook b, MarketParty m, ushort g, GoodBook gb, long cover, int markup, int day, EdgeTrade? et)
    {
        if (et == null) return;
        var led = _econ.Ledger;
        long want = Math.Min(cover * 6 / 10 - gb.StockUnits, Math.Max(1, cover / 4));
        if (want <= 0) return;
        long cash = led.Balance(m.Purse).Mites, unit = gb.RefMicro * markup / 1000; if (unit <= 0) return;
        long q = Math.Min(want, (long)((Int128)Math.Min(cash * 3 / 10, _deskImportRoom) * 1_000_000 / unit)); if (q <= 0) return;      // and the region's coin floor (decision I42)
        long cost = Price(gb.RefMicro, q, markup);
        if (cost > 0 && !led.Sink(m.Purse, new Coin(cost), SinkReason.Emigrate)) return;
        et.CoinNetIn -= cost;
        m.Store.Create(g, q, CreateReason.Import);
        b.List(m, g, q, 1000, day, merchantLot: true);
        ImportedMites += cost; DeskImportedMites += cost; ImportedUnits[g] += q; _deskImportRoom -= cost;
    }

    private void DeskExport(MarketBook b, ushort g, GoodBook gb, long cover, int discount, EdgeTrade? et)
    {
        if (et == null) return;
        var led = _econ.Ledger;
        long take = Math.Max(1, (gb.StockUnits - cover * Math.Min(2000, _glutAbovePermille - 500) / 1000) / 2);
        long budget = Math.Max(0, Math.Min(et.PayBudgetMites - et.CoinNetIn, _deskRoom));      // the region's coin allowance (ContinentTrade.BoundDeskExports, decision I42)
        long unitMicro = gb.RefMicro * discount / 1000; if (unitMicro <= 0) return;
        take = Math.Min(take, (long)((Int128)budget * 1_000_000 / unitMicro)); if (take <= 0) return;
        b.Sync(g);
        long left = take, got = 0;
        for (int i = gb.Lots.Count - 1; i >= 0 && left > 0; i--)
        {
            var lot = gb.Lots[i]; long q = Math.Min(lot.Qty, left); if (q <= 0) continue;
            long pay = Price(gb.RefMicro, q, discount);
            if (pay > 0)
            {
                led.Mint(lot.Owner.Purse, new Coin(pay), SourceReason.Immigrate);
                et.CoinNetIn += pay;
                long toll = (long)(((Int128)pay * _econ.Money.MarketTollPermille + 500) / 1000);
                if (toll > 0) led.Transfer(lot.Owner.Purse, b.TollPurse, new Coin(toll), TransferReason.MarketToll);
            }
            b.BookStore.Destroy(g, q, DestroyReason.Export);
            lot.Qty -= q; gb.StockUnits -= q; left -= q; got += q;
            if (lot.Qty == 0) gb.Lots.RemoveAt(i);
            ExportedMites += pay; DeskExportedMites += pay; _deskRoom -= pay;
        }
        if (got > 0) ExportedUnits[g] += got;
    }
}
