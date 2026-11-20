using System.Globalization;
using Ravensguard.Sim.Construction;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Holdings;

// P6-B6, holdings 3.8: the works verbs of the site panel and the Q menu: buy and deliver to a site's pile, post haul jobs for what the household owns, install a workstation from an item, found a
// business in a generated building, cut coppice.
public static partial class HoldingsDesk
{
    /// <summary>Q on a site: buys <paramref name="qty"/> grams of a good at the settlement's market and has it carted to the site's pile (the book's price and 2 mites a kilogram-kilometre).</summary>
    public static HeroCommand BuyDeliver(EntityId site, string good, long qty, int settlement, long sec) => Cmd(sec, Tag("buydeliver", ConstructionIds.SerialOf(site), good, qty, settlement), settlement);
    /// <summary>The site panel's "Post haul jobs": every missing material the hero's household holds is hauled to the pile by the board's carriers.</summary>
    public static HeroCommand PostHaul(EntityId site, long sec) => Cmd(sec, Tag("posthaul", ConstructionIds.SerialOf(site)));
    /// <summary>Sets a workstation made from an item (an anvil block) as a piece at a cell: the site is placed and its item put on the pile from the pack.</summary>
    public static HeroCommand Install(string piece, int x, int y, int z, long sec) => Cmd(sec, Tag("install", piece, x, y, z));
    /// <summary>Opens a business in a generated building: the kind, the place, the lot and the capital.</summary>
    public static HeroCommand FoundInLot(string kind, int settlement, int lot, long capital, long sec) => Cmd(sec, Tag("foundlot", kind, settlement, lot, capital), settlement);
    /// <summary>Cuts the coppice of a woodland tile into the hero's pack (it grows again in seven game years).</summary>
    public static HeroCommand Coppice(int tile, long sec) => Cmd(sec, Tag("coppice", tile));

    private static HoldingsResult? Works(HeroContext ctx, HeroCommand c, string[] p, HoldingsWorld hw, int day)
    {
        long N(int i) => i < p.Length && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : long.MinValue;
        int I(int i) { long v = N(i); return v is >= int.MinValue and <= int.MaxValue ? (int)v : int.MinValue; }
        var cw = ctx.State.HomeRecords?.Sim.Get<ConstructionWorld>();
        var hero = ctx.Party.Hero; long sec = c.IssuedSec;
        switch (p[0])
        {
            case "buydeliver" when p.Length >= 5 && cw != null:
            {
                var site = cw.Site(ConstructionIds.Make(cw.RegionId, ConstructionIds.SiteSlot, (ulong)Math.Max(0, N(1))));
                if (site == null || !site.Open) return HoldingsResult.No("cn_no_site");
                if (site.Owner != hero) return HoldingsResult.No("cn_not_your_site");
                if (!ctx.Econ.Goods.TryIdOf(p[2], out ushort good)) return HoldingsResult.No("cn_unknown_good", p[2]);
                long grams = N(3);                                                                                 // the site panel's numbers are grams; a piece good is bought by the piece
                long units = ctx.Econ.Goods.IsPiece(good) ? (grams + ctx.Econ.Goods.MassG(good, 1) - 1) / Math.Max(1, ctx.Econ.Goods.MassG(good, 1)) : grams;
                return BuyAndDeliver(ctx, cw, site, good, units, I(4), day, sec);
            }
            case "posthaul" when p.Length >= 2 && cw != null:
            {
                var site = cw.Site(ConstructionIds.Make(cw.RegionId, ConstructionIds.SiteSlot, (ulong)Math.Max(0, N(1))));
                if (site == null || !site.Open) return HoldingsResult.No("cn_no_site");
                if (site.Owner != hero) return HoldingsResult.No("cn_not_your_site");
                return PostHaulAll(ctx, cw, site, sec);
            }
            case "install" when p.Length >= 5 && cw != null:
            {
                var (hh, _) = HeroPlace(ctx);
                var pr = cw.Place(hero, PurseKind.Person, hh, [new PlaceCell(I(2), I(3), I(4), p[1])], sec);
                if (!pr.Ok) return HoldingsResult.No(pr.Key, pr.Args);
                var site = cw.Site(pr.Site)!;
                long moved = 0;
                foreach (var (g, q) in cw.Needed(site)) { var r = cw.Deliver(ctx.Pack, site.Id, g, q, site.PileX, site.PileY, sec); if (r.Ok) moved += q; }
                var lack = cw.Needed(site).Where(kv => cw.PileOf(site).Qty(kv.Key) < kv.Value).Select(kv => ctx.Econ.Goods.Def(kv.Key).Name).ToList();
                return lack.Count == 0 ? HoldingsResult.Yes("hw_installed", p[1]) : HoldingsResult.Yes("hw_install_waits", p[1], string.Join(", ", lack));
            }
            case "foundlot" when p.Length >= 5:
            {
                var ow = ctx.State.HomeRecords?.Sim.Get<OwnershipWorld>();
                if (ow == null) return HoldingsResult.No("bz_no_hero");
                var r = ow.FoundInLot(p[1], I(2), I(3), Math.Max(0, N(4)), day);
                return new HoldingsResult(r.Ok, r.Key, r.Args, r.Mites, r.CannotAfford);
            }
            case "coppice" when p.Length >= 2:
            {
                var (logs, fire) = hw.CoppiceCut(I(1), day, ctx.Pack);
                return logs + fire > 0 ? HoldingsResult.Yes("hw_coppiced", (logs + fire) / 1000) : HoldingsResult.No("hw_nothing_to_coppice");
            }
        }
        return null;
    }

    /// <summary>The carter's bill: 2 mites a kilogram-kilometre (grams x metres x 2 / 10^6), rounded up.</summary>
    public static long CartingMites(long grams, long metres) => (grams * metres * 2 + 999_999) / 1_000_000;

    /// <summary>A market order delivered to a pile (holdings 3.8, HW-8): the book's price into the pile, the carting to a carter of the place, a mass and coin move through the ledger and the book.</summary>
    public static HoldingsResult BuyAndDeliver(HeroContext ctx, ConstructionWorld cw, ConstructionSite site, ushort good, long qty, int settlement, int day, long sec)
    {
        var b = ctx.Book(settlement); if (b == null) return HoldingsResult.No("HERO_THERE_NO_MARKET_HERE");
        if (!b.IsOpen((int)(sec % 86400 / 3600))) return HoldingsResult.No("HERO_STALLS_ARE_SHUT");
        if (qty <= 0 || good >= b.Goods.Length) return HoldingsResult.No("HERO_NOTHING_BUY");
        b.Sync(good);
        if (b.Goods[good].StockUnits < qty) return HoldingsResult.No("HERO_THEY_HAVE_ONLY", Quotes.Weight(b.Goods[good].StockUnits), ctx.Econ.Goods.Def(good).Name.ToLowerInvariant());
        var q = Quotes.Buy(b, good, qty);
        var reg = ctx.State.Population.Region; var st = reg.Settlements[settlement];
        long dx = st.CenterX - site.PileX, dy = st.CenterY - site.PileY;
        long metres = IntMath.Isqrt(dx * dx + dy * dy);
        long grams = ctx.Econ.Goods.MassG(good, qty);
        long carting = CartingMites(grams, metres);
        if (ctx.Coin < q.TotalMites + carting) return new HoldingsResult(false, "hw_cannot_afford_delivery", [Quotes.Money(q.TotalMites + carting), Quotes.Money(ctx.Coin)], q.TotalMites + carting, true);
        int carter = CarterOf(ctx.State, settlement, day);
        if (carting > 0 && carter < 0) return HoldingsResult.No("hw_no_carter");
        var party = new MarketParty(ctx.Party.Hero, ctx.Purse, cw.PileOf(site), PartyKind.Household, int.MaxValue);
        var r = b.Buy(party, good, qty, b.Money.PriceClampMaxPermille, day, q.TotalMites);
        if (r.Qty <= 0) return HoldingsResult.No("HERO_NO_ONE_WILL_SELL");
        if (carting > 0) ctx.Econ.Ledger.Transfer(ctx.Purse, ctx.Econ.HouseholdPurse[carter], new Coin(carting), TransferReason.Wage);
        cw.Refresh(site); cw.Publish(sec);
        return new HoldingsResult(true, "hw_delivered", [Quotes.Weight(r.Qty), ctx.Econ.Goods.Def(good).Name.ToLowerInvariant(), Quotes.Money(r.PaidMites), Quotes.Money(carting)], r.PaidMites + carting);
    }

    /// <summary>The household of a carter of the place: the first adult labourer of the settlement by person row (the cart is his own), or -1.</summary>
    public static int CarterOf(Ravensguard.Sim.Save.WorldState st, int settlement, int d)
    {
        var pt = st.Population.People;
        for (int i = 0; i < pt.Count; i++)
            if (pt.IsAlive(i) && pt.HomeSettlement[i] == settlement && pt.Occupation[i] == Occupation.Labourer && pt.Household[i] >= 0 && pt.AgeYears(i, d) is >= 16 and < 60) return pt.Household[i];
        return -1;
    }

    /// <summary>The site panel's "Post haul jobs": for each material the pile lacks that the hero's household store holds, the haul jobs the board's carriers take.</summary>
    public static HoldingsResult PostHaulAll(HeroContext ctx, ConstructionWorld cw, ConstructionSite site, long sec)
    {
        var (hh, home) = HeroPlace(ctx);
        if (hh < 0) return HoldingsResult.No("cn_nothing_to_haul");
        var hht = ctx.State.Population.Households; var src = hht.IdOf(hh);
        var store = ctx.Econ.HouseholdStore[hh]; var pile = cw.PileOf(site);
        int posted = 0; long wages = 0;
        int sx = home >= 0 ? ctx.State.Population.Region.Settlements[home].CenterX : site.PileX, sy = home >= 0 ? ctx.State.Population.Region.Settlements[home].CenterY : site.PileY;
        foreach (var (g, need) in cw.Needed(site))
        {
            long lack = need - pile.Qty(g); if (lack <= 0 || store.Qty(g) <= 0) continue;
            var r = cw.PostHaul(site.Id, src, sx, sy, g, Math.Min(lack, store.Qty(g)), sec);
            if (r.Ok) { posted++; wages += r.Mites; }
        }
        return posted == 0 ? HoldingsResult.No("cn_nothing_to_haul") : new HoldingsResult(true, "hw_hauls_posted", [posted, Quotes.Money(wages)], wages);
    }
}
