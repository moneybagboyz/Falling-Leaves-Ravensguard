using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Trade;

/// <summary>
/// Trade in the markets of another resident region (Phase 3 cleanup B, item 5). The hero's coin and pack live in the home region's records, the neighbour's market in its own economy, so a deal crosses a
/// border the way a migrant's coin does (<see cref="RegionTransfer"/>): the coin for the deal leaves the home ledger as <c>Emigrate</c> and enters the neighbour's as <c>Immigrate</c> on a visitor purse,
/// the deal runs on the neighbour's book against the visitor's open store, and what is left comes back the same way; goods leave one mass ledger as <c>Export</c> and enter the other as <c>Import</c>.
/// Both regions' audits hold and the two together lose and gain nothing. Runs at the hero's barrier only (no region is stepping), in command order, so 1x and 8x agree.
/// </summary>
public static class AbroadDealing
{
    public static RegionWorld? RegionOf(HeroContext ctx, int region)
    {
        if (ctx.State.Regions is not { } rs) return null;
        foreach (var rw in rs.Resident) if (rw.Coord.Id == region) return rw;
        return null;
    }

    public static MarketBook? BookOf(HeroContext ctx, int region, int settlement)
    {
        if (ctx.BookAbroad != null) return ctx.BookAbroad(region, settlement);
        var rw = RegionOf(ctx, region); var m = rw?.Economy.Markets;
        return m == null || settlement < 0 || settlement >= m.BookOf.Length ? null : m.BookForSettlement(settlement);
    }

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText text, long mites = 0, long qty = 0) => new(c.Seq, c.Kind, code, text, mites, qty, c.IssuedSec);
    private static HeroText T(string key, params object[] args) => HeroText.Of(key, args);
    private static string Amount(Economy.EconomyState e, ushort g, long q) => e.Goods.IsPiece(g) ? $"{q}" : Quotes.Weight(q);

    public static HeroResult Visit(HeroContext ctx, HeroCommand c, out int market, out int region)
    {
        market = -1; region = -1;
        var b = BookOf(ctx, c.Region, c.Settlement);
        if (b == null) return R(c, HeroResultCode.NoMarket, T("ABROAD_THERE_NO_MARKET_THERE"));
        market = c.Settlement; region = c.Region;
        return R(c, HeroResultCode.Ok, T("HERO_MARKET", b.Name));
    }

    private static (PurseId Purse, Store Store) Visitor(Economy.EconomyState e)
        => (e.Ledger.Open(PurseKind.Person, EconomyState.HeroOwner), e.OpenStore(StoreKind.Open, EconomyState.HeroOwner));

    /// <summary>Takes <paramref name="mites"/> from the home purse to the visitor purse over the border (false: cannot afford).</summary>
    private static bool CoinOver(HeroContext ctx, Economy.EconomyState there, PurseId visitor, long mites)
    {
        if (mites <= 0) return true;
        var coin = Coin.FromMites(mites);
        if (!ctx.Econ.Ledger.Sink(ctx.Purse, coin, RegionTransfer.CoinOut)) return false;
        there.Ledger.Mint(visitor, coin, RegionTransfer.CoinIn);
        return true;
    }

    private static void CoinHome(HeroContext ctx, Economy.EconomyState there, PurseId visitor, long keep = 0)
    {
        var left = new Coin(Math.Max(0, there.Ledger.Balance(visitor).Mites - keep));       // only what this deal brought and did not spend
        if (left.Mites <= 0) return;
        there.Ledger.Sink(visitor, left, RegionTransfer.CoinOut);
        ctx.Econ.Ledger.Mint(ctx.Purse, left, RegionTransfer.CoinIn);
    }

    public static HeroResult Buy(HeroContext ctx, HeroCommand c)
    {
        var b = BookOf(ctx, c.Region, c.Settlement); if (b == null) return R(c, HeroResultCode.NoMarket, T("ABROAD_THERE_NO_MARKET_THERE"));
        var there = b.Econ;
        if (!b.IsOpen((int)(c.IssuedSec / 3600 % 24))) return R(c, HeroResultCode.Closed, T("HERO_STALLS_ARE_SHUT"));
        ushort g = c.Good; if (g >= b.Goods.Length || c.Qty <= 0) return R(c, HeroResultCode.Invalid, T("HERO_NOTHING_BUY"));
        b.Sync(g);
        if (b.Goods[g].StockUnits < c.Qty) return R(c, HeroResultCode.NoStock, T("HERO_THEY_HAVE_ONLY", Amount(there, g, b.Goods[g].StockUnits), there.Goods.Def(g).Name.ToLowerInvariant()));
        var q = Quotes.Buy(b, g, c.Qty);
        if (c.Limit > 0 && q.TotalMites > c.Limit) return R(c, HeroResultCode.PriceMoved, T("ABROAD_PRICES_MOVED_NOW_COSTS", Quotes.Money(q.TotalMites)), q.TotalMites);
        long bring = c.Limit > 0 ? c.Limit : q.TotalMites;
        if (ctx.Coin < bring) return R(c, HeroResultCode.CannotAfford, T("HERO_CANNOT_AFFORD_HAVE", Quotes.Money(q.TotalMites), Quotes.Money(ctx.Coin)), q.TotalMites);
        var (vp, vs) = Visitor(there); long pre = there.Ledger.Balance(vp).Mites;
        if (!CoinOver(ctx, there, vp, bring)) return R(c, HeroResultCode.CannotAfford, T("ABROAD_CANNOT_AFFORD"), q.TotalMites);
        var party = new MarketParty(EconomyState.HeroOwner, vp, vs, PartyKind.Household, int.MaxValue);
        var r = b.Buy(party, g, c.Qty, b.Money.PriceClampMaxPermille, (int)(c.IssuedSec / 86400), bring);
        long got = vs.Qty(g);
        if (got > 0) { vs.Destroy(g, got, DestroyReason.Export); ctx.Pack.Create(g, got, CreateReason.Import); ctx.Party.Carried.Stow(ctx.Econ, ctx.Rules, g); ctx.Party.Traded.Add(g); }
        CoinHome(ctx, there, vp, pre);
        if (r.Qty <= 0) return R(c, HeroResultCode.NoStock, T("HERO_NO_ONE_WILL_SELL"));
        return R(c, HeroResultCode.Ok, T("ABROAD_BOUGHT", Amount(there, g, r.Qty), there.Goods.Def(g).Name.ToLowerInvariant(), b.Name, Quotes.Money(r.PaidMites)), r.PaidMites, r.Qty);
    }

    /// <summary>
    /// P5-F (businesses spec 4.5, BS-15; the Phase 3 carried limit "items and barter abroad"): an item row sold to a merchant of another region at its bid of <c>ItemTable.ValueMites</c>, as at home.
    /// The row crosses the border whole: it leaves the home item table and a row of the same shape, quality, condition and parts is the merchant's in the other region's; the coin comes back over
    /// the border the way a deal's coin does. Both regions' audits hold and the item is neither lost nor doubled.
    /// </summary>
    public static HeroResult SellItem(HeroContext ctx, HeroCommand c, long valueMites)
    {
        var b = BookOf(ctx, c.Region, c.Settlement); if (b == null) return R(c, HeroResultCode.NoMarket, T("ABROAD_THERE_NO_MARKET_THERE"));
        var there = b.Econ; var e = ctx.Econ; int row = e.Items.IndexOf(c.Item);
        if (row < 0 || e.Items.At(row).Owner != ctx.Party.Hero) return R(c, HeroResultCode.Invalid, T("HERO_DO_NOT_HAVE"));
        if ((e.Items.At(row).Flags & ItemFlags.Stolen) != 0) return R(c, HeroResultCode.Refused, T("HERO_I_DON_T_BUY"));
        if (!b.IsOpen((int)(c.IssuedSec / 3600 % 24))) return R(c, HeroResultCode.Closed, T("HERO_STALLS_ARE_SHUT"));
        long bid = ItemPricing.Bid(valueMites, b.Money.SpreadHalfPermille, ctx.Party.Inspected.Contains(c.Item), ctx.Trade.SellUnknownConditionPm);
        if (bid <= 0) return R(c, HeroResultCode.Refused, T("HERO_WORTH_NOTHING_THEM"));
        if (c.Limit > 0 && bid < c.Limit) return R(c, HeroResultCode.PriceMoved, T("HERO_THEY_NOW_OFFER", Quotes.Money(bid)), bid);
        var (vp, _) = Visitor(there); long pre = there.Ledger.Balance(vp).Mites;
        foreach (var m in b.Merchants)
        {
            if (there.Ledger.Balance(m.Purse).Mites < bid) continue;
            if (!there.Ledger.Transfer(m.Purse, vp, Coin.FromMites(bid), TransferReason.Trade)) continue;
            ref var it = ref e.Items.At(row);
            var parts = e.Items.ComponentsOf(row).ToList();
            foreach (int k in ctx.Party.Carried.Rows(e).ToArray()) if (e.Items.At(k).Parent == row) e.Items.At(k).Parent = -1;   // its contents stay with the hero
            var made = there.Items.Add(ctx.State.Ids, it.Shape, it.Quality, m.Id, parts, it.Good, it.LabourHours, it.Skill, it.ConditionPermille);
            ref var nr = ref there.Items.Get(made); nr.Wear = it.Wear; nr.Flags = it.Flags; nr.Stains = it.Stains;
            string shape = it.Shape;
            e.Items.Free(c.Item);
            CoinHome(ctx, there, vp, pre);
            return R(c, HeroResultCode.Ok, T("HERO_SOLD", ctx.Rules.Name(shape).ToLowerInvariant(), Quotes.Money(bid)), bid, 1);
        }
        return R(c, HeroResultCode.TheyCannotAfford, T("HERO_NO_MERCHANT_HAS", Quotes.Money(bid)));
    }

    public static HeroResult Sell(HeroContext ctx, HeroCommand c)
    {
        var b = BookOf(ctx, c.Region, c.Settlement); if (b == null) return R(c, HeroResultCode.NoMarket, T("ABROAD_THERE_NO_MARKET_THERE"));
        var there = b.Econ;
        if (!b.IsOpen((int)(c.IssuedSec / 3600 % 24))) return R(c, HeroResultCode.Closed, T("HERO_STALLS_ARE_SHUT"));
        ushort g = c.Good;
        if (c.Qty <= 0 || ctx.Pack.Qty(g) < c.Qty) return R(c, HeroResultCode.Invalid, T("HERO_DO_NOT_HAVE_MUCH"));
        if (b.Merchants.Count == 0) return R(c, HeroResultCode.TheyCannotAfford, T("HERO_NO_MERCHANT_BUYS_HERE"));
        if (there.Goods.SpoilPpm(g, StoreKind.Granary) >= MarketBook.PerishablePpmPerDay) return R(c, HeroResultCode.Refused, T("HERO_NO_MERCHANT_BUYS_FRESH"));
        b.Sync(g);
        var q = Quotes.Sell(b, g, c.Qty);
        if (c.Limit > 0 && q.NetMites < c.Limit) return R(c, HeroResultCode.PriceMoved, T("HERO_PRICES_MOVED_THEY_NOW", Quotes.Money(q.NetMites)), q.NetMites);
        var (vp, vs) = Visitor(there); long pre = there.Ledger.Balance(vp).Mites;
        ctx.Pack.Destroy(g, c.Qty, DestroyReason.Export); vs.Create(g, c.Qty, CreateReason.Import);
        var party = new MarketParty(EconomyState.HeroOwner, vp, vs, PartyKind.Household, int.MaxValue);
        var r = b.SellToMerchants(party, g, c.Qty, (int)(c.IssuedSec / 86400));
        long unsold = vs.Qty(g);                                              // what no merchant could pay for goes back in the pack
        if (unsold > 0) { vs.Destroy(g, unsold, DestroyReason.Export); ctx.Pack.Create(g, unsold, CreateReason.Import); }
        long before = ctx.Coin;
        CoinHome(ctx, there, vp, pre);
        ctx.Party.Carried.Stow(ctx.Econ, ctx.Rules, g);
        if (r.Qty <= 0) return R(c, HeroResultCode.TheyCannotAfford, T("HERO_MERCHANTS_HAVE_NO_COIN", Quotes.Money(HeroDesk.MerchantCash(b))));
        ctx.Party.Traded.Add(g);
        return R(c, HeroResultCode.Ok, T("ABROAD_SOLD_TOLL", Amount(there, g, r.Qty), there.Goods.Def(g).Name.ToLowerInvariant(), b.Name, Quotes.Money(r.PaidMites), Quotes.Money(r.TollMites)), ctx.Coin - before, r.Qty);
    }
}
