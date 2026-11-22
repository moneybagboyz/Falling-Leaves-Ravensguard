using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using PartyKind = Ravensguard.Sim.Economy.Markets.PartyKind;

namespace Ravensguard.Sim.WarSupply;

/// <summary>
/// Merchants following the army (army-economy 4.7, P7-AE2), ladder step 5: a host of 200 men or more, camped two days or more (or marching in its own realm), draws the trader businesses of the books within 16 km that hold at least 50 sp
/// and 50 kg of staple in their own store, each joining with P = 10% a day (stream 0x751, key the host and the day, draw by the merchant) when the camp's price (their book's ask times 1,300 per mille) is a margin of at least 20% over
/// their book's bid, at most one per hundred men. They sell to the quartermaster at that price from their own stores to the baggage, paid from the pay chest to the merchant's purse by a trade transfer (the goods and the coin move,
/// nothing is made); they leave when pay is three days in arrears or their stock is out. Not built here: the camp's market book and the soldiers' own spending (left out, army-economy.md Notes).
/// </summary>
public sealed partial class HostSupply
{
    /// <summary>The merchant party of an id among the books' merchants, with its book.</summary>
    private (MarketParty Party, MarketBook Book)? FindMerchant(ulong id)
    {
        foreach (var bk in Economy.Markets.AllBooks()) foreach (var m in bk.Merchants) if (m.Id.Value == id) return (m, bk);
        return null;
    }

    /// <summary>The sutlers of a host: those that joined (the book's rows), in join order.</summary>
    public IEnumerable<SutlerRec> SutlersOf(HostId h) { foreach (var s in Book.Sutlers) if (s.Host == h.Value) yield return s; }

    private long StapleStock(Store s) { long t = 0; foreach (var g in _stapleGoods) t += EqG(g, s.Qty(g)); return t; }

    /// <summary>The day's joining and leaving of a host's sutlers (once, before the ladder).</summary>
    private void UpdateSutlers(HostAccount a, int day)
    {
        if (a.Abstract) return;
        bool unpaid = false; for (int r = 0; r < Roster.Roles; r++) if (a.Men[r] > 0 && a.UnpaidDays[r] >= 3) unpaid = true;
        for (int i = Book.Sutlers.Count - 1; i >= 0; i--)
        {
            var sr = Book.Sutlers[i]; if (sr.Host != a.Id) continue;
            var m = FindMerchant(sr.Merchant);
            if (unpaid || m == null || StapleStock(m.Value.Party.Store) < 1000) Book.Sutlers.RemoveAt(i);
        }
        int men = a.Total; if (men < SupplyConfig.SutlerMinMen) return;
        bool camped = a.Mode == HostMode.Camp && a.ServiceDays >= 2, marching = a.Mode == HostMode.March && InOwnRealm(a) && a.ServiceDays >= 2;
        if (!camped && !marching) return;
        int have = Book.Sutlers.Count(x => x.Host == a.Id), cap = men / SupplyConfig.SutlerPerMen;
        if (have >= cap || unpaid) return;
        ulong seed = Economy.World.Seed;
        foreach (int s in SettlementsWithin(a, SupplyConfig.PurveyanceRadiusM))
        {
            var bk = BookOf(s); if (bk == null || bk.Blockaded) continue;
            for (int mi = 0; mi < bk.Merchants.Count && have < cap; mi++)
            {
                var m = bk.Merchants[mi];
                if (Book.Sutlers.Any(x => x.Merchant == m.Id.Value)) continue;
                if (_econ.Ledger.Balance(m.Purse).Mites < SupplyConfig.SutlerMinMites || StapleStock(m.Store) < SupplyConfig.SutlerMinStockG) continue;
                ushort g = StapleOf(m.Store); long ask = bk.AskMicro(g), bid = bk.BidMicro(g);
                if (bid <= 0 || ask * SupplyConfig.SutlerMarkupPm / 1000 * 100 < bid * (100 + SupplyConfig.SutlerMinMargin)) continue;
                if (DetRng.Range(seed, day, (ulong)a.Id, RngStreams.SutlersJoin, (uint)(s * 64 + mi), 0, 100) >= SupplyConfig.SutlerJoinPct) continue;
                bool first = !Book.Sutlers.Any(x => x.Host == a.Id);
                Book.Sutlers.Add(new SutlerRec(a.Id, m.Id.Value, day)); have++;
                if (first)
                {
                    var sid = _pop.Region.Settlements[s].Id;
                    Chron.Chron(WriteTime(day), "sutlers_joined", "ch_war_sutlers", [ChronParam.Place("place", sid), ChronParam.Count("merchants", 1)], a.Lord != 0 ? [sid, new EntityId(a.Lord)] : [sid], [HostCause(a, CauseKind.Trade, "ch_note_war_sutlers")], ("host", a.Id.ToString()));
                }
            }
        }
    }

    private ushort StapleOf(Store s) { ushort best = _stapleGoods.Length > 0 ? _stapleGoods[0] : (ushort)0; long q = -1; foreach (var g in _stapleGoods) { long x = s.Qty(g); if (x > q) { q = x; best = g; } } return best; }

    /// <summary>Ladder step 5: the host's sutlers sell staple to the quartermaster at their book's ask times 1,300 per mille, from their own stores, paid from the chest. Returns what is still wanted.</summary>
    private long Sutle(HostAccount a, Rt rt, DayResult res, ushort[] goods, long left, int day)
    {
        if (a.Abstract || left <= 0) return left;
        var led = _econ.Ledger;
        foreach (var sr in Book.Sutlers.ToArray())
        {
            if (sr.Host != a.Id || left <= 0) continue;
            var f = FindMerchant(sr.Merchant); if (f == null) continue;
            var (m, bk) = f.Value;
            foreach (var g in goods)
            {
                if (left <= 0) break;
                long have = m.Store.Qty(g); if (have <= 0) continue;
                long ppu = bk.AskMicro(g) * SupplyConfig.SutlerMarkupPm / 1000; if (ppu <= 0) continue;
                long units = Math.Min(have, QtyFor(SupplyNeed.Staple, g, left)), bal = led.Balance(rt.Chest).Mites;
                long cost = (long)(((Int128)units * ppu + PriceCurve.Micro - 1) / PriceCurve.Micro);
                if (cost > bal) { units = (long)((Int128)bal * PriceCurve.Micro / ppu); cost = (long)(((Int128)units * ppu + PriceCurve.Micro - 1) / PriceCurve.Micro); }
                if (units <= 0 || cost <= 0 || cost > bal) continue;
                if (!led.Transfer(rt.Chest, m.Purse, new Coin(cost), TransferReason.Trade)) continue;
                if (!Store.Move(m.Store, rt.Baggage, g, units)) continue;
                long eq = Math.Min(left, EqG(g, units));
                rt.Baggage.Destroy(g, units, DestroyReason.Eaten);
                res.Delivered[(int)SupplyNeed.Staple, (int)SupplySource.Sutlers] += eq; left -= eq;
                Book.Stats[10] += units; Book.Stats[11] += cost;
            }
        }
        return left;
    }
}
