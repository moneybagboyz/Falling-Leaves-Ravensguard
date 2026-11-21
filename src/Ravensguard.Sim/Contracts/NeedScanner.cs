using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Contracts;

/// <summary>
/// The needs that post contracts (contracts-and-rumours-v2 4.1). A settlement is scanned a slice at a time (one settlement an hour): its stores short of an input with no merchant stock near, the caravans
/// leaving with goods worth guarding, the bands seen near its roads, the captives held for ransom, the cottages households want built, the farms and sites short of hands. Each need writes its own chronicle
/// entry first and the posting hangs on it; a posting needs the issuer's purse to hold the pay (it moves into escrow at once), so an issuer with an empty box posts nothing (CR-2). Settlements and needs
/// are walked in ascending order, so the same world posts the same contracts in the same order.
/// </summary>
public static class NeedScanner
{
    /// <summary>Whether this need already has a contract: one still live, or one that closed less than <c>RepostDays</c> ago (a need nobody answered is not posted again at once, so a shortage does not flood the board and the chronicle).</summary>
    private static bool Posted(ContractWorld w, ContractBook book, int day, Func<WorkContract, bool> same) => book.Contracts.Values.Any(c => same(c) && (c.Live || c.ClosedDay >= 0 && day - c.ClosedDay < w.S.RepostDays));

    /// <summary>Scans one settlement at <paramref name="day"/> (hour 1 of it is when contracts show); returns the contracts posted.</summary>
    public static List<WorkContract> Scan(ContractWorld w, ContractBook book, int settlement, int day)
    {
        var posted = new List<WorkContract>();
        if (settlement < 0 || settlement >= w.SettlementNames.Length) return posted;
        long sec = day * 86_400L + 3600;
        Lapse(w, book, settlement, day);
        // a full board posts nothing, so a need written now would be a chronicle entry with no contract after it (P6-G2: 2.3 stock_short entries for each delivery posted over eight game years); wait for a place
        if (book.Contracts.Values.Count(x => x.Open && x.Settlement == settlement) >= w.S.BoardCap) return posted;
        StockShort(w, book, settlement, day, sec, posted);
        Caravans(w, book, settlement, day, sec, posted);
        Bands(w, book, settlement, day, sec, posted);
        Captives(w, book, settlement, day, sec, posted);
        Builds(w, book, settlement, day, sec, posted);
        Hands(w, book, settlement, day, sec, posted);
        return posted;
    }

    /// <summary>
    /// A shortage the world has made good by itself (a merchant brought the goods, the days of stock rose again) takes down the delivery nobody had yet taken: the escrow goes back to the issuer. A contract that is
    /// taken stays the taker's: his hand-over still counts.
    /// </summary>
    private static void Lapse(ContractWorld w, ContractBook book, int settlement, int day)
    {
        if (w.StockNeeds == null) return;
        var open = book.Contracts.Values.Where(c => c.Open && c.Settlement == settlement && c.Kind == "deliver_goods").ToList();
        if (open.Count == 0) return;
        var still = new HashSet<(EntityId, int)>(w.StockNeeds().Where(n => n.Settlement == settlement && n.DaysLeft < w.S.StockDaysMin).Select(n => (n.Owner, (int)n.Good)));
        foreach (var c in open) if (!still.Contains((c.TargetOwner, c.Good))) ContractSystem.Cancel(w, book, c.Id, day);
    }

    /// <summary>A household's or business's store below the days of an input it buys, with no merchant holding any within the reach: a delivery contract, the pay of kilometres x kilograms.</summary>
    private static void StockShort(ContractWorld w, ContractBook book, int s, int day, long sec, List<WorkContract> posted)
    {
        if (w.StockNeeds == null) return;
        var k = w.Content.Kind("deliver_goods"); if (k == null) return;
        foreach (var n in w.StockNeeds().Where(x => x.Settlement == s).OrderBy(x => x.Owner).ThenBy(x => x.Good))
        {
            if (n.DaysLeft >= w.S.StockDaysMin) continue;
            if (Posted(w, book, day, c => c.Kind == "deliver_goods" && c.TargetOwner == n.Owner && c.Good == n.Good)) continue;
            bool merchant = false;
            for (int t = 0; t < w.SettlementNames.Length && !merchant; t++)
                if (w.DistanceM(s, t) <= w.S.MerchantReachM && w.MerchantHolds(t, n.Good, n.Grams)) merchant = true;
            if (merchant) continue;
            if (w.PurseOfOwner(n.Owner) is not { } purse) continue;
            long pay = ContractSystem.PayOf(k, Math.Max(1, w.S.MerchantReachM / 1000), Math.Max(1, n.Grams / 1000));
            if (w.Ledger.Balance(purse).Mites < pay) continue;                       // nothing is posted that cannot be paid (CR-2)
            var need = ContractSystem.NeedEntry(w, "stock_short", s, sec, [n.Owner], n.Good, n.Grams, n.DaysLeft);
            if (ContractSystem.Post(w, book, "deliver_goods", n.Owner, purse, s, pay, day, need, c => { c.TargetOwner = n.Owner; c.Good = n.Good; c.Grams = n.Grams; c.Param = n.DaysLeft; }) is { } c1) posted.Add(c1);
        }
    }

    private static void Caravans(ContractWorld w, ContractBook book, int s, int day, long sec, List<WorkContract> posted)
    {
        if (w.Caravans == null) return;
        var k = w.Content.Kind("guard_caravan"); if (k == null) return;
        foreach (var cv in w.Caravans().Where(x => x.Settlement == s && x.ValueMites > w.S.CaravanGuardValueMites).OrderBy(x => x.Caravan))
        {
            if (Posted(w, book, day, c => c.Target == cv.Caravan && c.Kind is "guard_caravan" or "escort_caravan")) continue;
            if (w.PurseOfOwner(cv.Owner) is not { } purse) continue;
            long pay = ContractSystem.PayOf(k, Math.Max(1, cv.Days), Math.Max(1, cv.Guards));
            if (w.Ledger.Balance(purse).Mites < pay) continue;
            var need = ContractSystem.NeedEntry(w, "caravan_leaving", s, sec, [cv.Caravan], days: cv.Days);
            if (ContractSystem.Post(w, book, "guard_caravan", cv.Owner, purse, s, pay, day, need, c => { c.Target = cv.Caravan; c.Param = cv.Days; }) is { } c1) posted.Add(c1);
        }
    }

    /// <summary>A band seen within three kilometres of a road in the last five days: the fief (or a guild) pays for it to be cleared.</summary>
    private static void Bands(ContractWorld w, ContractBook book, int s, int day, long sec, List<WorkContract> posted)
    {
        if (w.Bands == null) return;
        var k = w.Content.Kind("clear_bandit_cave"); if (k == null) return;
        foreach (var b in w.Bands().Where(x => x.Settlement == s && day - x.LastSeenDay <= w.S.BandSeenDays).OrderBy(x => x.Band))
        {
            if (Posted(w, book, day, c => c.Target == b.Band && c.Kind == "clear_bandit_cave")) continue;
            if (w.TreasuryOf(s) is not { } purse) continue;
            int f = w.FiefOf(s); var issuer = f >= 0 && f < w.FiefIds.Length ? w.FiefIds[f] : w.Ledger.OwnerOf(purse);
            long pay = k.PayBase + 200L * Math.Max(0, b.Members - 4);                // a bigger band is worth more
            if (w.Ledger.Balance(purse).Mites < pay) continue;
            var need = ContractSystem.NeedEntry(w, "band_seen", s, sec, [b.Band], days: day - b.LastSeenDay);
            if (ContractSystem.Post(w, book, "clear_bandit_cave", issuer, purse, s, pay, day, need, c => { c.Target = b.Band; c.Param = b.Members; }) is { } c1) posted.Add(c1);
        }
    }

    /// <summary>A captive held by bandits or a lord: his household posts a rescue, the ransom is the hero's or the household's affair (family 4.7).</summary>
    private static void Captives(ContractWorld w, ContractBook book, int s, int day, long sec, List<WorkContract> posted)
    {
        if (w.Captives == null) return;
        var k = w.Content.Kind("rescue_captive"); if (k == null) return;
        foreach (var cp in w.Captives().Where(x => x.Settlement == s).OrderBy(x => x.Person))
        {
            if (Posted(w, book, day, c => c.Target == cp.Person && c.Kind == "rescue_captive")) continue;
            if (w.PurseOfOwner(cp.Household) is not { } purse) continue;
            long pay = Math.Max(k.PayBase, cp.RansomMites / 4);
            if (w.Ledger.Balance(purse).Mites < pay) continue;
            var need = ContractSystem.NeedEntry(w, "captive_held", s, sec, [cp.Person]);
            if (ContractSystem.Post(w, book, "rescue_captive", cp.Household, purse, s, pay, day, need, c => c.Target = cp.Person) is { } c1) posted.Add(c1);
        }
    }

    /// <summary>A household that wants a cottage in a place posts a building contract at its budget (the construction market takes bids on it, <see cref="ConstructionMarket"/>).</summary>
    private static void Builds(ContractWorld w, ContractBook book, int s, int day, long sec, List<WorkContract> posted)
    {
        if (w.Builds == null || w.Content.Kind("build_cottage") == null) return;
        foreach (var b in w.Builds().Where(x => x.Settlement == s).OrderBy(x => x.Client))
        {
            if (Posted(w, book, day, c => c.Kind == "build_cottage" && c.Issuer == b.Client)) continue;
            if (w.PurseOfOwner(b.Client) is not { } purse || w.Ledger.Balance(purse).Mites < b.BudgetMites) continue;
            var need = ContractSystem.NeedEntry(w, "planner", s, sec, [b.Client]);
            if (ContractSystem.Post(w, book, "build_cottage", b.Client, purse, s, b.BudgetMites, day, need) is { } c1) posted.Add(c1);
        }
    }

    /// <summary>The farms and sites short of hands (harvest, hauling, a day's labour): the pay of days x hands, as Phase 5's day labour posted it, now a contract anyone may take.</summary>
    private static void Hands(ContractWorld w, ContractBook book, int s, int day, long sec, List<WorkContract> posted)
    {
        if (w.Hands == null) return;
        foreach (var h in w.Hands().Where(x => x.Settlement == s).OrderBy(x => x.Owner).ThenBy(x => x.Kind, StringComparer.Ordinal))
        {
            string kind = h.Kind switch { "harvest" => "harvest_hands", "haul" => "haul_for_site", _ => "day_labour" };
            if (w.Content.Kind(kind) is not { } k) continue;
            if (Posted(w, book, day, c => c.Kind == kind && c.Issuer == h.Owner)) continue;
            if (w.PurseOfOwner(h.Owner) is not { } purse) continue;
            long pay = ContractSystem.PayOf(k, Math.Max(1, h.Hands));
            if (w.Ledger.Balance(purse).Mites < pay) continue;
            var need = ContractSystem.NeedEntry(w, "hands_short", s, sec, [h.Owner], days: h.Hands);
            if (ContractSystem.Post(w, book, kind, h.Owner, purse, s, pay, day, need, c => c.Param = h.Hands) is { } c1) posted.Add(c1);
        }
    }

    /// <summary>The hourly slice (H7): one settlement a game hour, round the region (the settlement is the hour modulo the count, so there is nothing to save). Returns the contracts posted.</summary>
    public static List<WorkContract> Hour(ContractWorld w, ContractBook book, long hour)
    {
        int n = w.SettlementNames.Length; var all = new List<WorkContract>();
        if (n == 0) return all;
        int day = (int)(hour / 24);
        for (int i = 0; i < Math.Max(1, w.S.ScanSettlementsPerHour); i++)
        {
            int s = (int)((hour + i) % n);
            all.AddRange(Scan(w, book, s, day));
        }
        return all;
    }
}
