using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Production;

public sealed partial class OwnershipWorld
{
    /// <summary>
    /// One game hour of ownership (spec 4.7), on the tick worker after the hour's production: each holding's daily slice at its business's hour (<c>index mod 24</c>: prices remembered, the
    /// steward paid, profit sent, alerts), stalls minded by their staff in the market hours, prospecting jobs finished, and at a season's first day the new mines' hazards (stream 0x5C2).
    /// </summary>
    public void Hour(long hour)
    {
        int hod = (int)(hour % 24), day = (int)(hour / 24);
        Today = Math.Max(Today, day);
        if (Book.ToastDay != day) { Book.ToastDay = day; Book.Toasted = OwnerAlert.None; }
        foreach (var h in Book.Holdings.Values.ToList())
            if (Prod.ById(h.Business) is { } b && b.Index % 24 == hod) Slice(b, h, day);
        if (hod >= Econ.Money.MarketOpenHour && hod < Econ.Money.MarketCloseHour)
            foreach (var s in Book.Stalls.Values) if (s.Minder >= 0) StallHour(s, hour);
        FinishProspecting(hour * 3600 + 3599);
        if (hod == 9 && day % TimeConst.DaysPerSeason == 0) NewMineHazards(day);
    }

    private void Slice(Business b, BusinessHolding h, int day)
    {
        if (h.MadeDay != day) { h.MadeDay = day; h.MadeToday = 0; }
        RememberPrices(b, h);
        if (!IsHeroOwner(b)) { h.Alerts = OwnerAlert.None; return; }
        if (b.State != BusinessState.Open) { h.Alerts = OwnerAlert.None; return; }
        if (h.Steward >= 0) PaySteward(b, h, final: false);
        if (h.Steward >= 0) SendProfit(b, h, day);
        Alerts(b, h, day);
        if (h.Steward >= 0 && Rules.ReportEveryDays > 0 && h.FoundedDay >= 0 && (day - h.FoundedDay) % Rules.ReportEveryDays == 0 && day > h.FoundedDay)
        {
            var last = h.History.Count > 0 ? h.History[^1].Profit : 0;
            Notices.Add(HeroText.Of("bz_report", Pop.People.FullName(h.Steward), Name(b), M(Econ.Ledger.Balance(b.Purse).Mites), b.Staff.Count, M(last), M(h.SentToPurse + h.SentToChest)));
            h.Alerts |= OwnerAlert.StewardReport;
        }
    }

    /// <summary>The goods a business's planner looks at: its recipes' inputs and outputs and its extraction outputs, ascending.</summary>
    private ushort[] GoodsOf(Business b)
    {
        var set = new SortedSet<ushort>(); var k = KindOf(b);
        foreach (var r in k.Recipes) { foreach (var (g, _) in r.Inputs) set.Add(g); foreach (var (g, _) in r.Outputs) set.Add(g); }
        foreach (var e in k.Extractions) set.Add(e.OutGood);
        return [.. set];
    }

    /// <summary>Today's mid prices for the steward's memory (spec 4.4: a novice plans with last week's prices): at most <c>lag_max + 1</c> days kept.</summary>
    private void RememberPrices(Business b, BusinessHolding h)
    {
        var book = Markets?.BookForSettlement(b.Settlement); if (book == null) return;
        if (h.PriceGoods.Length == 0) h.PriceGoods = GoodsOf(b);
        var row = new long[h.PriceGoods.Length];
        for (int i = 0; i < row.Length; i++) row[i] = book.MidMicro(h.PriceGoods[i]);
        h.PriceDays.Add(row);
        while (h.PriceDays.Count > Rules.StewardLagMaxDays + 1) h.PriceDays.RemoveAt(0);
    }

    /// <summary>The steward's day wage: from the cash box, else the hero's purse; three days short and he leaves (Phase 2's rule).</summary>
    private void PaySteward(Business b, BusinessHolding h, bool final)
    {
        if (!final) h.StewardOwed += h.StewardWage;
        if (h.StewardOwed <= 0) return;
        int p = h.Steward >= 0 ? h.Steward : -1;
        var to = p >= 0 && Pop.People.Household[p] is int hh and >= 0 && hh < Econ.HouseholdCount ? Econ.HouseholdPurse[hh] : (PurseId?)null;
        if (to == null) { h.StewardOwed = 0; return; }
        long fromBox = Math.Min(h.StewardOwed, Econ.Ledger.Balance(b.Purse).Mites);
        if (fromBox > 0 && Econ.Ledger.Transfer(b.Purse, to.Value, new Coin(fromBox), TransferReason.Wage)) { h.StewardOwed -= fromBox; h.StewardPaidSeason += fromBox; }
        long fromHero = Math.Min(h.StewardOwed, HeroCoin);
        if (fromHero > 0 && Econ.Ledger.Transfer(HeroPurse, to.Value, new Coin(fromHero), TransferReason.Wage)) h.StewardOwed -= fromHero;
        if (h.StewardOwed > 0) { h.StewardShortDays++; if (h.StewardShortDays >= 3 && !final) { Notices.Add(HeroText.Of("bz_steward_quit", Pop.People.FullName(h.Steward), Name(b))); DismissSteward(b.Id); } }
        else h.StewardShortDays = 0;
    }

    /// <summary>What the business costs a day: its staff's wages, the steward's, and its inputs this season (mites).</summary>
    public long DailyCost(Business b, BusinessHolding h)
    {
        long w = 0;
        foreach (int p in b.Staff) w += h.Wages.TryGetValue(p, out int x) ? x : CurrentWage(p, out _);
        if (h.Steward >= 0) w += h.StewardWage;
        int dayInSeason = Math.Max(1, Today % TimeConst.DaysPerSeason + 1);
        return w + Math.Max(b.InputSpendPerDay, b.InputsSeason / dayInSeason);
    }

    /// <summary>
    /// The steward sends profit (spec 4.4, BS-10): every <c>send_every</c> days, everything above both the reserve (15 days of costs, by his Leadership) and the owner's <c>keep_mites</c> goes to the
    /// owner's purse when he is at the place, else to his home chest (a <c>Dividend</c>; coin never travels by magic).
    /// </summary>
    public long SendProfit(Business b, BusinessHolding h, int day)
    {
        if (h.Steward < 0) return 0;
        int every = Math.Max(1, h.Orders.SendEveryDays);
        if (h.LastSentDay >= 0 && day - h.LastSentDay < every) return 0;
        if (h.LastSentDay < 0 && h.FoundedDay >= 0 && day - h.FoundedDay < every) return 0;
        h.LastSentDay = day;
        long cash = Econ.Ledger.Balance(b.Purse).Mites;
        long reserve = StewardBrain.Reserve(DailyCost(b, h), Pop.People.SkillLevelOf(h.Steward, Skill.Leadership), Rules);
        long send = StewardBrain.ToSend(cash, Math.Max(reserve, b.WagesSeason > 0 ? 0 : 0), h.Orders.KeepMites);
        if (send <= 0) { h.Alerts |= OwnerAlert.StewardNoProfit; return 0; }
        var to = PayeeFor(b.Settlement, out bool chest);
        if (!Econ.Ledger.Transfer(b.Purse, to, new Coin(send), TransferReason.Dividend)) return 0;
        if (chest) h.SentToChest += send; else h.SentToPurse += send;
        h.SentSeason += send; b.DividendsSeason += send; b.Dividends += send;
        return send;
    }

    private void Alerts(Business b, BusinessHolding h, int day)
    {
        var a = h.Alerts & OwnerAlert.StewardReport;
        var kind = KindOf(b);
        if (b.ArrearsMites > 0) a |= OwnerAlert.WagesOwed;
        if (kind.Slots.Length > 0 && b.Staff.Count == 0 && kind.Recipes.Length + kind.Extractions.Length > 0) a |= OwnerAlert.NoStaff;
        if (b.Stations.Count > 0 && b.Stations.All(s => !s.Busy)) a |= OwnerAlert.NoInput;
        if (h.History.Count > 0 && h.History[^1].Profit < 0) a |= OwnerAlert.LossSeason;
        if (h.Unlicensed) a |= OwnerAlert.Unlicensed;
        if (b.Mine >= 0 && b.Mine < Prod.Mines.Count)
        {
            var m = Prod.Mines[b.Mine];
            if (m.State == MineStateKind.Flooded) a |= OwnerAlert.Flooded;
            if (m.InitialG > 0 && m.RemainingG * 10 < m.InitialG) a |= OwnerAlert.Exhausted;
        }
        if (HasStall(b) && StallOf(b) is { Minder: < 0 }) a |= OwnerAlert.StallUnminded;
        // one toast per alert kind a day (spec 6)
        var fresh = a & ~h.Alerts & ~Book.Toasted & ~OwnerAlert.StewardReport;
        foreach (var f in Enum.GetValues<OwnerAlert>())
            if (f != OwnerAlert.None && (fresh & f) != 0) { Notices.Add(HeroText.Of("bz_alert_" + f.ToString().ToLowerInvariant(), Name(b))); Book.Toasted |= f; }
        h.Alerts = a;
    }

    // ------------------------------------------------------------------ stalls (spec 4.5)

    public bool HasStall(Business b) { foreach (var s in Book.Stalls.Values) if (s.Business == b.Index) return true; return false; }
    public StallHolding? StallOf(Business b) { foreach (var s in Book.Stalls.Values) if (s.Business == b.Index) return s; return null; }

    /// <summary>The stall's store: the business's (a stall it sells from), else the stall's own store keyed by its id (the hero's goods; opened when first stocked, so reading never adds one).</summary>
    public Store? StallStore(StallHolding s, bool open = false)
    {
        if (s.Business >= 0 && s.Business < Prod.Businesses.Count) return Prod.Businesses[s.Business].Store;
        if (Econ.OtherStores.TryGetValue(s.StoreId, out var st)) return st;
        return open ? Econ.OpenStore(StoreKind.Open, s.StoreId) : null;
    }

    public PurseId StallPurse(StallHolding s) => s.Business >= 0 && s.Business < Prod.Businesses.Count ? Prod.Businesses[s.Business].Purse : HeroPurse;

    /// <summary>Takes up a stall the hero leases (land 4.3): the stall's record, its store, the business that sells there (-1: his own goods).</summary>
    public OwnResult OpenStall(int settlement, int stall, int business)
    {
        long subject = Ravensguard.Sim.Land.LandIds.StallSubject(settlement, stall);
        if (Land != null)
        {
            var id = Ravensguard.Sim.Land.LandIds.Of(Ravensguard.Sim.Land.HoldingKind.Stall, Land.RegionId, subject);
            if (!Land.Book.Deeds.TryGetValue(id, out var d) || d.Holder != Hero || d.LeaseEndDay <= Today) return OwnResult.No("bz_stall_not_leased");
        }
        if (business >= 0 && (business >= Prod.Businesses.Count || !IsHeroOwner(Prod.Businesses[business]))) return OwnResult.No("bz_not_yours");
        if (!Book.Stalls.TryGetValue(subject, out var s))
            Book.Stalls[subject] = s = new StallHolding { Subject = subject, Settlement = settlement, StoreId = EntityId.Make(IdKind.Other, (0x5FUL << 40) | (ulong)subject) };
        s.Business = business;
        return OwnResult.Yes("bz_stall_open", Pop.Region.Settlements[settlement].Name);
    }

    public OwnResult StallMinder(long subject, int person)
    {
        if (!Book.Stalls.TryGetValue(subject, out var s)) return OwnResult.No("bz_no_stall");
        if (person >= 0 && (s.Business < 0 || !Prod.Businesses[s.Business].Staff.Contains(person))) return OwnResult.No("bz_not_staff");
        s.Minder = person;
        return OwnResult.Yes(person >= 0 ? "bz_stall_minder" : "bz_stall_no_minder", person >= 0 ? Pop.People.FullName(person) : "");
    }

    public OwnResult StallAsk(long subject, int askPm)
    {
        if (!Book.Stalls.TryGetValue(subject, out var s)) return OwnResult.No("bz_no_stall");
        s.AskPm = Math.Clamp(askPm, 500, 2000);
        return OwnResult.Yes("bz_stall_ask", s.AskPm / 10);
    }

    /// <summary>The hero minds his stall from <paramref name="fromSec"/> to <paramref name="untilSec"/> (BS-13, BS-16): each whole market hour in that span sells (a second counts once).</summary>
    public OwnResult Mind(long subject, long fromSec, long untilSec)
    {
        if (!Book.Stalls.TryGetValue(subject, out var s)) return OwnResult.No("bz_no_stall");
        long from = Math.Max(fromSec, s.MindedToSec);
        long sold0 = s.SoldSeason, take0 = s.TakingsSeason;
        for (long h = (from + 3599) / 3600; (h + 1) * 3600 <= untilSec; h++)
        {
            int hod = (int)(h % 24);
            if (hod < Econ.Money.MarketOpenHour || hod >= Econ.Money.MarketCloseHour) continue;
            StallHour(s, h);
        }
        s.MindedToSec = Math.Max(s.MindedToSec, untilSec);
        return new OwnResult(true, "bz_minded", [Pop.Region.Settlements[s.Settlement].Name, s.SoldSeason - sold0, M(s.TakingsSeason - take0)], s.TakingsSeason - take0);
    }

    /// <summary>Whether a fair runs at a settlement on a day (Phase 3 fairs: towns hold them; FairBook's holders).</summary>
    public bool FairDay(int settlement, int day)
    {
        if (Fairs == null || (uint)settlement >= (uint)Pop.Region.Settlements.Count) return false;
        var cls = Pop.Region.Settlements[settlement].Class;
        var holder = cls == Ravensguard.Sim.WorldGen.Region.SettlementClass.Town ? Ravensguard.Sim.Travel.FairHolder.Town : cls == Ravensguard.Sim.WorldGen.Region.SettlementClass.Village ? Ravensguard.Sim.Travel.FairHolder.Village : Ravensguard.Sim.Travel.FairHolder.Other;
        return Fairs.ActiveOn(GameTime.FromGameDays(day).DayOfYear) is { } a && Ravensguard.Sim.Travel.FairBook.Holds(a.Fair, holder);
    }

    /// <summary>One minded market hour of a stall: <see cref="StallTrade.SellHour"/> against the settlement's book and households, the day's books kept.</summary>
    public void StallHour(StallHolding s, long hour)
    {
        if (Markets == null) return;
        int day = (int)(hour / 24);
        if (s.Day != day) { s.Day = day; s.SoldToday = 0; s.TakingsToday = 0; s.TollToday = 0; }
        var book = Markets.BookForSettlement(s.Settlement);
        int stalls = Math.Max(1, Land?.StallCount(s.Settlement) ?? 1);
        int pm = FairDay(s.Settlement, day) ? Rules.StallFairDemandPm : Rules.StallDemandPm;
        if (StallStore(s) is not { } store) return;
        var r = StallTrade.SellHour(Econ, book, Pop, store, StallPurse(s), s.Settlement, stalls, pm, Rules.StallMarketHours, s.AskPm, hour, Rules.StallFootfallMitesPerHousehold);
        s.SoldToday += r.Units; s.TakingsToday += r.Mites; s.TollToday += r.Toll; s.SoldSeason += r.Units; s.TakingsSeason += r.Mites;
        if (s.Business >= 0 && s.Business < Prod.Businesses.Count && H(Prod.Businesses[s.Business]) is { } bh) bh.RevenueSeason += r.Mites;
    }

    public OwnResult StallStock(long subject, Store from, ushort good, long qty)
    {
        if (!Book.Stalls.TryGetValue(subject, out var s)) return OwnResult.No("bz_no_stall");
        long q = Math.Min(qty, from.Qty(good));
        if (q <= 0 || !Store.Move(from, StallStore(s, open: true)!, good, q)) return OwnResult.No("bz_none_to_supply");
        return new OwnResult(true, "bz_stall_stocked", [Econ.Goods.IsPiece(good) ? q.ToString() : Quotes.Weight(q), Econ.Goods.Def(good).Name.ToLowerInvariant()]);
    }

    public OwnResult StallTake(long subject, Store to, ushort good, long qty)
    {
        if (!Book.Stalls.TryGetValue(subject, out var s)) return OwnResult.No("bz_no_stall");
        var st = StallStore(s); long q = st == null ? 0 : Math.Min(qty, st.Qty(good));
        if (q <= 0 || !Store.Move(st!, to, good, q)) return OwnResult.No("bz_none_to_take");
        return new OwnResult(true, "bz_taken", [Econ.Goods.IsPiece(good) ? q.ToString() : Quotes.Weight(q), Econ.Goods.Def(good).Name.ToLowerInvariant()]);
    }
}

/// <summary>
/// Selling at a stall (spec 4.5): goods in the stall's store sell to the settlement's shoppers by the market's rules, at the book's per-unit ask (scaled by the owner's ask), while somebody minds
/// it. A minded stall meets up to <c>demand x stall_demand_pm / 1000 / stalls</c> of a good a market day, or what its passers-by spend (<c>households x footfall mites</c>) if more; a fair day's
/// footfall is 1,500 permille; spread over the market hours; a higher ask sells
/// fewer. Buyers are the settlement's households in a turn kept by the hour (ascending index from a cursor), each paying what it can; the seller pays the market toll (5%, <c>MarketToll</c>).
/// Coin by ledger transfers only, goods by store moves only. Pure in its inputs.
/// </summary>
public static class StallTrade
{
    public readonly record struct Sale(long Units, long Mites, long Toll);

    public static Sale SellHour(EconomyState econ, MarketBook book, Population pop, Store stock, PurseId seller, int settlement, int stalls, int demandPm, int marketHours, int askPm, long hour, int footfallMitesPerHousehold = 0)
    {
        long units = 0, mites = 0, toll = 0;
        var rows = stock.Rows();
        if (rows.Length == 0) return new Sale(0, 0, 0);
        var hht = pop.Households;
        // the settlement's households, the cursor turned by the hour so the turn is shared out
        var homes = new List<int>();
        for (int h = 0; h < hht.Count; h++) if (hht.Settlement[h] == settlement && hht.MemberCount[h] > 0 && h < econ.HouseholdCount) homes.Add(h);
        if (homes.Count == 0) return new Sale(0, 0, 0);
        int cursor = (int)((hour * 7919) % homes.Count);
        foreach (var (g, have) in rows)
        {
            if (have <= 0) continue;
            var gb = book.Goods[g];
            long unitMicro = book.AskMicro(g) * askPm / 1000;
            if (unitMicro <= 0) continue;
            long footfall = (long)((Int128)homes.Count * footfallMitesPerHousehold * demandPm / 1000 * PriceCurve.Micro / unitMicro);
            long perDay = Math.Max(1, Math.Max(gb.Demand * demandPm / 1000 / Math.Max(1, stalls), footfall));
            long cap = Math.Max(1, (perDay + marketHours - 1) / Math.Max(1, marketHours));
            if (askPm > 1000) cap = cap * Math.Max(0, 2000 - askPm) / 1000;                 // dearer than the book: fewer buyers
            long q = Math.Min(have, cap);
            if (q <= 0) continue;
            long left = q;
            for (int k = 0; k < homes.Count && left > 0; k++)
            {
                int hh = homes[(cursor + k) % homes.Count];
                var purse = econ.HouseholdPurse[hh];
                long coin = econ.Ledger.Balance(purse).Mites;
                long afford = (long)((Int128)coin * PriceCurve.Micro / unitMicro);
                long take = Math.Min(left, afford);
                if (take <= 0) continue;
                long pay = (long)((Int128)take * unitMicro / PriceCurve.Micro);
                if (pay <= 0) continue;
                if (!econ.Ledger.Transfer(purse, seller, new Coin(pay), TransferReason.Trade)) continue;
                if (!Store.Move(stock, econ.HouseholdStore[hh], g, take)) throw new InvalidOperationException("a stall lost goods it was selling");
                long t = (pay * econ.Money.MarketTollPermille + 500) / 1000;
                if (t > 0 && econ.Ledger.Transfer(seller, book.TollPurse, new Coin(t), TransferReason.MarketToll)) toll += t;
                left -= take; units += take; mites += pay;
            }
            gb.TradedToday += q - left; gb.LastTradedDay = (int)(hour / 24);
        }
        return new Sale(units, mites, toll);
    }
}
