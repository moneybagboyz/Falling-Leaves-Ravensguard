using Ravensguard.Content;
using Ravensguard.Sim.Bondage;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Soldiers;

/// <summary>
/// P8-LW-4 (live-war 3.4): the pen's hooks, set where the soldiers' runtime is built. Every hook moves real things by the books and nothing is made: a prisoner's ration is a kilogram of the captor's household staple destroyed as eaten (the gaol rule);
/// a sale is a ledger transfer from a buyer's purse to the captor's (<c>SlaveSale</c>) and a bond for the buyer, only where the culture's law has a market; a lord who cannot pay the quartered ransom cedes his fief to the captor's realm; a broken
/// parole costs the man 200 standing in the soldiers' book (the chronicle entry is the pen's own).
/// </summary>
public static class PenWiring
{
    public const int FeedG = 1000, ParoleStandingLoss = 200;

    public static void Install(SoldierWorld sw, WorldState s, ContentDatabase content)
    {
        var h = sw.PenHooks;
        h.Feed = row => Feed(s, sw, row);
        EntityId buyer = default; long price = 0;
        h.SaleOffer = row =>
        {
            if (s.Population.People.Count == 0 || row.Pen < 0 || row.CaptorPurse < 0) return null;
            var bw = BondWiring.World(s, content);
            if (!Slavery.HasMarket(bw.LawAt(row.Pen))) return null;
            int day = h.Today; long ask = bw.PriceOf(new EntityId(row.Person), day) * bw.D.SellPm / 1000;
            if (ask <= 0 || Slavery.Buyer(bw, row.Pen, ask, day, (uint)(row.Person & 0xFFFF)) is not { } b || bw.PurseOf(b) is not { } purse) return null;
            buyer = b; price = ask;
            return (purse, ask);
        };
        h.Sold = (row, _) =>
        {
            if (buyer.Value == 0) return;
            var bw = BondWiring.World(s, content); var book = s.Bonds ??= new BondBook();
            Slavery.Make(bw, book, new EntityId(row.Person), buyer, BondKind.Chattel, BondOrigin.Sold, h.Today, row.Pen, price, 0, row.Entry);
            buyer = default;
        };
        h.Cede = row => Cede(s, row);
        h.ParoleBroken = person => sw.Book.Standing[(long)person] = Math.Max(-1000, sw.Book.Standing.GetValueOrDefault((long)person) - ParoleStandingLoss);
    }

    /// <summary>A kilogram of the captor's household staple, destroyed as eaten; true when it was there. A captor with no household (or no table) feeds nobody.</summary>
    private static bool Feed(WorldState s, SoldierWorld sw, PrisonerRow row)
    {
        var econ = s.Economy; var pt = sw.People(); if (econ == null || pt == null || row.Captor < 0) return false;
        int r = pt.IndexOf(new EntityId((ulong)row.Captor)); if (r < 0) return false;
        int hh = pt.Household[r]; if (hh < 0 || hh >= econ.HouseholdStore.Length) return false;
        var store = econ.HouseholdStore[hh]; long left = FeedG;
        foreach (var g in econ.Food.Staples)
        {
            long unit = Math.Max(1, econ.Goods.MassG(g, 1)), have = store.Qty(g); if (have <= 0) continue;
            long take = Math.Min(have, (left + unit - 1) / unit);
            store.Destroy(g, take, DestroyReason.Eaten); left -= take * unit;
            if (left <= 0) return true;
        }
        return false;
    }

    /// <summary>A lord who cannot pay: every fief he holds passes to the captor's realm (the realm of the fief the captor holds); false when the captor holds none or the prisoner holds none.</summary>
    private static bool Cede(WorldState s, PrisonerRow row)
    {
        var rb = s.Rule; if (rb == null) return false;
        int realm = -1;
        foreach (var f in rb.Fiefs.Values) if (f.Holder == row.Captor && !f.Forfeited) { realm = f.Realm; break; }
        if (realm < 0) return false;
        bool any = false;
        foreach (var f in rb.Fiefs.Values.OrderBy(f => f.Fief)) if (f.Holder == (long)row.Person && !f.Forfeited && f.Realm != realm) { f.Realm = realm; f.OverlordFief = -1; any = true; }
        return any;
    }
}
