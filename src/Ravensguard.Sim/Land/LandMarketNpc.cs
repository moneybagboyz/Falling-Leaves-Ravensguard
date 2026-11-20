using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Land;

/// <summary>One sale of the NPC land market: where, what the seller was (the lord or a household in arrears), the buyer, the price.</summary>
public readonly record struct NpcSale(int Settlement, int Lot, EntityId Seller, EntityId Buyer, long Price, bool Distress);

/// <summary>
/// The NPC land market (holdings-and-works-v2 3.3, stream 0x500). Each season, place by place in index order, households with a member who wants a dwelling (a woman waiting to marry, or a crowded house)
/// and coin of at least <c>NpcBuyerCoinMultiple</c> (2) times the price buy: first the empty houses of households in arrears (the seller takes <c>DistressSalePm</c>, 90%, of the price; what he owes his lord is paid
/// out of it), then the lord's listings at the listed price (which carries the scarcity term). A plot goes to the richest buyer; buyers with exactly the same coin are told apart by a draw of stream 0x500 keyed on the plot and
/// the season (the draw is the household's index), and only then. A household buys one plot a season, a place sells <c>NpcBuysPerSeason</c> at most. The buyer then builds on it (growth's cottage project, paid by him).
/// Every payment is a ledger transfer between real purses (Trade, and Rent for the arrears). No other part of land draws.
/// </summary>
public static class LandMarketNpc
{
    /// <summary>The season's market (a season's first day, hour 9, after the dues and the resale). Returns the sales in the order made.</summary>
    public static List<NpcSale> Seasonal(LandWorld lw, int day, LandChronicle chron, LandChronicleKeyed? keyed)
    {
        var sales = new List<NpcSale>(); var r = lw.Region; var hht = lw.Pop.Households;
        var bought = new HashSet<int>();
        int season = day / TimeConst.DaysPerSeason;
        var cands = Candidates(lw);                                                    // the households that want a dwelling and could pay for the cheapest plot, by place: most places have none, and then nothing else is looked at
        for (int s = 0; s < r.Settlements.Count; s++)
        {
            var st = r.Settlements[s]; if (st.Class == SettlementClass.Farmstead || cands[s] is not { Count: > 0 } here) continue;
            int lord = LandRegistry.LordHousehold(lw.Pop, s); int done = 0;
            // 1. empty houses whose holder is in arrears
            foreach (var d in lw.Book.Deeds.Values.Where(x => x.Kind == HoldingKind.Lot && x.Settlement == s && !x.IsLease && x.OwedMites > 0 && x.HolderPurse == PurseKind.Household).OrderBy(x => x.Id).ToList())
            {
                if (done >= lw.Rules.NpcBuysPerSeason) break;
                var (_, lot) = LandIds.SplitLot(d.Subject);
                if (!lw.LotEmpty(s, lot)) continue;
                long price = lw.ValueOf(d) * lw.Rules.DistressSalePm / 1000;
                if (price <= 0 || Pick(lw, here, lord, price, PlotKey(d.Subject, 0), season, bought) is not { } b) continue;
                if (!Distress(lw, d, b, price, day, keyed)) continue;
                bought.Add(b); done++;
                sales.Add(new NpcSale(s, lot, d.Holder, hht.IdOf(b), price, true));
                lw.StartBuild?.Invoke(s, lot, b, day);
            }
            // 2. the lord's listings
            foreach (var l in lw.Listings(s))
            {
                if (done >= lw.Rules.NpcBuysPerSeason) break;
                if (Pick(lw, here, lord, l.PriceMites, PlotKey(LandIds.LotSubject(s, l.Lot < 0 ? 0xFFFF : l.Lot), l.Rect.X * 4096L + l.Rect.Y), season, bought) is not { } b) continue;
                var (lordId, _) = LandRegistry.LordOf(lw.Pop, s);
                var res = lw.Buy(hht.IdOf(b), PurseKind.Household, s, l.Lot, l.Rect, l.PriceMites, day, chron);
                if (!res.Ok || !lw.Book.Deeds.TryGetValue(res.Deed, out var deed)) continue;
                bought.Add(b); done++;
                int lot = LandIds.SplitLot(deed.Subject).Lot;
                sales.Add(new NpcSale(s, lot, lordId, hht.IdOf(b), res.Mites, false));
                lw.StartBuild?.Invoke(s, lot, b, day);
            }
        }
        return sales;
    }

    /// <summary>The households that want a dwelling and hold <c>NpcBuyerCoinMultiple</c> times the cheapest plot of their place (its area at the class's price, the lowest demand, the scarcity term), by place, in index order.</summary>
    private static List<int>?[] Candidates(LandWorld lw)
    {
        var r = lw.Region; var hht = lw.Pop.Households; var res = new List<int>?[r.Settlements.Count];
        var need = new long[r.Settlements.Count];
        for (int s = 0; s < need.Length; s++)
        {
            var st = r.Settlements[s]; bool town = st.Class == SettlementClass.Town;
            int area = town ? lw.GrowthTuning.PlotWidthTownM * lw.GrowthTuning.PlotDepthTownM : lw.GrowthTuning.PlotWidthVillageM * lw.GrowthTuning.PlotDepthVillageM;
            need[s] = lw.Scarce(LandRules.PlotPrice(lw.Rules, area, st.Class, lw.Rules.DemandMinPm)) * Math.Max(1, lw.Rules.NpcBuyerCoinMultiple);
        }
        for (int h = 0; h < hht.Count; h++)
        {
            int s = hht.Settlement[h]; if (hht.MemberCount[h] == 0 || (uint)s >= (uint)res.Length || r.Settlements[s].Class == SettlementClass.Farmstead) continue;
            if (lw.Econ.Ledger.Balance(lw.Econ.HouseholdPurse[h]).Mites < need[s] || !Wants(lw, h)) continue;
            (res[s] ??= []).Add(h);
        }
        return res;
    }

    private static ulong PlotKey(long subject, long extra) => DetRng.Mix((ulong)subject * 0x9E3779B97F4A7C15UL ^ (ulong)extra);

    /// <summary>A household wants a dwelling: a woman of it waits to marry, or its members crowd its beds.</summary>
    public static bool Wants(LandWorld lw, int h)
    {
        var hht = lw.Pop.Households; var pt = lw.Pop.People;
        if (hht.MemberCount[h] * 100 > hht.Beds[h] * lw.Rules.CrowdedOverPct) return true;
        foreach (int m in hht.Members(h)) if (pt.Status[m] == PersonStatus.Alive && pt.Sex[m] == Sex.Female && pt.Spouse[m] < 0 && pt.MarriageWait[m] > 0) return true;
        return false;
    }

    /// <summary>The household that buys a plot at a price in a place: the richest of those that want a dwelling and hold <c>NpcBuyerCoinMultiple</c> times the price, exact ties by the stream-0x500 draw, else null.</summary>
    public static int? Buyer(LandWorld lw, int s, int lord, long price, ulong plotKey, int season, HashSet<int> taken)
    {
        var hht = lw.Pop.Households; var all = new List<int>();
        for (int h = 0; h < hht.Count; h++) if (hht.Settlement[h] == s && hht.MemberCount[h] > 0) all.Add(h);
        return Pick(lw, all, lord, price, plotKey, season, taken);
    }

    private static int? Pick(LandWorld lw, List<int> hs, int lord, long price, ulong plotKey, int season, HashSet<int> taken)
    {
        long need = price * Math.Max(1, lw.Rules.NpcBuyerCoinMultiple);
        int best = -1; long bestCoin = -1; uint bestDraw = uint.MaxValue;
        foreach (int h in hs)
        {
            if (h == lord || taken.Contains(h) || !Wants(lw, h)) continue;
            long coin = lw.Econ.Ledger.Balance(lw.Econ.HouseholdPurse[h]).Mites;
            if (coin < need) continue;
            if (coin > bestCoin) { best = h; bestCoin = coin; bestDraw = uint.MaxValue; continue; }
            if (coin < bestCoin) continue;
            // exactly the same coin: the draw decides (drawn for the standing one too, once)
            if (bestDraw == uint.MaxValue) bestDraw = DetRng.NextUInt(lw.Seed, season, plotKey, RngStreams.LandMarketTies, (uint)best);
            uint draw = DetRng.NextUInt(lw.Seed, season, plotKey, RngStreams.LandMarketTies, (uint)h);
            if (draw < bestDraw || draw == bestDraw && h < best) { best = h; bestDraw = draw; }
        }
        return best < 0 ? null : best;
    }

    /// <summary>A household in arrears sells its empty house: what it owes its lord is paid from the price, the rest is the seller's; the deed passes to the buyer.</summary>
    private static bool Distress(LandWorld lw, Deed d, int buyer, long price, int day, LandChronicleKeyed? keyed)
    {
        var hht = lw.Pop.Households; var buyerId = hht.IdOf(buyer); var seller = d.Holder;
        var (payee, payeeKind) = lw.Payee(d);
        long owed = Math.Min(d.OwedMites, price), rest = price - owed;
        if (lw.Econ.Ledger.Balance(lw.Econ.HouseholdPurse[buyer]).Mites < price) return false;
        var bp = lw.Econ.HouseholdPurse[buyer];
        if (owed > 0 && payee != EntityId.None && !lw.Econ.Ledger.Transfer(bp, lw.PurseOf(payee, payeeKind), new Coin(owed), TransferReason.Rent)) return false;
        if (rest > 0 && !lw.Econ.Ledger.Transfer(bp, lw.PurseOf(seller, d.HolderPurse), new Coin(rest), TransferReason.Trade)) return false;
        var st = lw.Region.Settlements[d.Settlement];
        if (!lw.Transfer(d.Id, buyerId, PurseKind.Household, day, DeedCause.Sold)) return false;
        keyed?.Invoke(new GameTime(day * 86_400L + 9 * 3600), "land_distress_sale", "ch_land_distress_sale", [ChronParam.Person("seller", seller), ChronParam.Person("buyer", buyerId), ChronParam.Place("place", st.Id), ChronParam.Mites("price", price)],
            [seller, buyerId], [new Cause(CauseKind.Trade, null, NoteKey: "ch_note_distress_arrears")], ("price", price.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return true;
    }
}
