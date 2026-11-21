using System.Globalization;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Trade;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Economy;

/// <summary>
/// Installs the mints on the home region (the P7-CN1 hook in <see cref="WorldRuntime"/>): the world state's coinage book (the "coinage" section, created empty and never written while empty), the mint world in the sim's services,
/// the ledger's gold hook, the rule's mint master as the mint's staff and the seigniorage he handles as the rule's skimming base, the market's bid for copper, and the open businesses as the daily sellers of gold and silver.
/// </summary>
public static class CoinageWiring
{
    public static MintWorld Attach(RegionWorld home, WorldState state, ContentDatabase content)
    {
        var mw = Make(state, content, home.Economy.Production);
        home.Sim.Set(mw);
        return mw;
    }

    /// <summary>The mint world a desk command works on: the home region's when the runtime installed one, else one over the world state's own records (tests, a world without regions).</summary>
    public static MintWorld For(WorldState state, ContentDatabase content)
    {
        if (state.HomeRecords?.Sim.Get<MintWorld>() is { } mw) return mw;
        return Loose.GetValue(state, s => Make(s, content, s.Production));
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldState, MintWorld> Loose = new();

    private static MintWorld Make(WorldState state, ContentDatabase content, Production.ProductionSystem? prod)
    {
        var econ = state.Economy ?? throw new InvalidOperationException("coinage needs the world's economy");
        var book = state.Coinage ??= new CoinageBook();
        var cfg = content.All<MoneySettingsDef>().OrderBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault() ?? econ.Money;
        var mw = new MintWorld(book, econ, state.Population.Region, cfg);
        mw.Staffed = realm => state.Rule == null || state.Rule.OfficeOfRealm(OfficeKind.MintMaster, realm) != null;
        mw.Handled = (realm, seigniorage) =>
        {
            if (state.Rule is not { } rule || rule.OfficeOfRealm(OfficeKind.MintMaster, realm) is not { } off || state.HomeRecords?.Sim is not { } sim) return;
            var c = RuleWiring.Context(sim, rule, mw.Chron);
            RuleOfficials.Handle(c, off, Party.OfRealm(realm), seigniorage, seigniorage);                                       // his hands, so his honesty may skim it (Skim, a real transfer)
        };
        mw.BidMitesPerKg = (settlement, good) =>
        {
            var m = state.Markets; if (m == null || settlement < 0 || settlement >= m.BookOf.Length) return (long)(econ.Goods.Def(good).BasePriceMitesPerKg * (1000 - cfg.SpreadHalfPermille) / 1000);
            return m.BookForSettlement(settlement).BidMicro(good) * 1000 / Economy.Markets.PriceCurve.Micro;
        };
        if (prod != null)
            mw.Sellers = () => prod.Businesses.Where(b => b.State == Production.BusinessState.Open && b.Store != null).Select(b => (b.Store, b.Purse, b.Settlement));
        return mw;
    }
}

/// <summary>
/// The hero's side of coinage at the barrier (the hero desk calls it for <see cref="HeroCommandKind.Coin"/>; the tag's prefix "cn:" is also construction's text prefix: the desk routes by kind). The tag names the verb:
/// <c>sell|metal|grams</c> (gold, silver or copper at the mint of the command's settlement, in market hours), <c>change|gold|mites</c> (silver for gold pieces at a money changer of the settlement), <c>change|silver|pieces</c> (gold pieces back to silver),
/// <c>melt|metal|pieces</c> (coin into metal, from the hero's purse). Every coin moves by the ledger, every piece by the gold book.
/// </summary>
public static class CoinDesk
{
    public const string Prefix = "cn:";
    private static string Tag(params object[] parts) => Prefix + string.Join('|', parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));
    private static HeroCommand Cmd(long sec, string tag, int settlement) => new() { Kind = HeroCommandKind.Coin, IssuedSec = sec, Tag = tag, Settlement = settlement, Origin = CommandOrigin.Human };

    public static HeroCommand Sell(MintMetal metal, long grams, int settlement, long sec) => Cmd(sec, Tag("sell", metal.ToString().ToLowerInvariant(), grams), settlement);
    public static HeroCommand ChangeToGold(long mites, int settlement, long sec) => Cmd(sec, Tag("change", "gold", mites), settlement);
    public static HeroCommand ChangeToSilver(long pieces, int settlement, long sec) => Cmd(sec, Tag("change", "silver", pieces), settlement);
    public static HeroCommand Melt(MintMetal metal, long pieces, long sec) => Cmd(sec, Tag("melt", metal.ToString().ToLowerInvariant(), pieces), -1);

    private static HeroResult R(HeroCommand c, HeroResultCode code, HeroText text, long mites = 0) => new(c.Seq, c.Kind, code, text, mites, 0, c.IssuedSec);

    /// <summary>The money changer a settlement has: its first open business of the kind (ascending id), else none.</summary>
    public static Production.Business? ChangerAt(Production.ProductionSystem prod, int settlement)
    {
        foreach (var b in prod.Businesses)
            if (b.State == Production.BusinessState.Open && b.Settlement == settlement && prod.KindOf(b).Id == "money_changer") return b;
        return null;
    }

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_UNKNOWN_COMMAND"));
        var p = c.Tag[Prefix.Length..].Split('|');
        long N(int i) => i < p.Length && long.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : long.MinValue;
        var st = ctx.State; var econ = ctx.Econ; var led = econ.Ledger;
        var mw = CoinageWiring.For(st, ctx.Content);
        var book = st.Coinage!; led.Coinage = book.Stock;
        int day = (int)(c.IssuedSec / 86400); int hour = (int)(c.IssuedSec / 3600 % 24);
        bool metal(int i, out MintMetal m) { m = default; return i < p.Length && Enum.TryParse(p[i], true, out m) && Enum.IsDefined(m); }
        var chron = RuleChron.Of(st.Chronicle, st.Ids);
        switch (p[0])
        {
            case "sell" when p.Length >= 3 && metal(1, out var mm) && N(2) > 0:
            {
                if (mw.MintAt(c.Settlement) is not { } row) return R(c, HeroResultCode.Refused, HeroText.Of("co_no_mint"));
                if (hour < econ.Money.MarketOpenHour || hour >= econ.Money.MarketCloseHour) return R(c, HeroResultCode.Refused, HeroText.Of("co_mint_shut"));
                if (mm != MintMetal.Copper) book.Stock.OptIn(ctx.Purse);                      // the hero's purse keeps the gold pieces he is paid
                mw.Chron = chron;
                var o = mm == MintMetal.Copper ? mw.SellCopper(row.Realm, N(2), ctx.Pack, ctx.Purse, day) : mw.Deliver(row.Realm, mm, N(2), ctx.Pack, ctx.Purse, day);
                if (!o.Ok) return R(c, HeroResultCode.Refused, HeroText.Of(o.Why));
                return R(c, HeroResultCode.Ok, HeroText.Of(o.Pieces > 0 ? "co_sold_metal" : "co_metal_queued", o.Grams, o.Pieces, o.Paid), o.Paid);
            }
            case "change" when p.Length >= 3 && N(2) > 0:
            {
                var prod = st.Production; if (prod == null || ChangerAt(prod, c.Settlement) is not { } changer) return R(c, HeroResultCode.Refused, HeroText.Of("co_no_changer"));
                var cfg = ctx.Content.All<MoneySettingsDef>().OrderBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault() ?? econ.Money;
                var r = p[1] == "gold" ? MoneyChanger.ToGold(led, book.Stock, ctx.Purse, changer.Purse, N(2), cfg.ChangerFeePermille)
                      : p[1] == "silver" ? MoneyChanger.ToSilver(led, book.Stock, ctx.Purse, changer.Purse, N(2), cfg.ChangerFeePermille) : ExchangeResult.No("co_bad_amount");
                return r.Ok ? R(c, HeroResultCode.Ok, HeroText.Of("co_changed", r.Pieces, r.Fee), r.Fee) : R(c, HeroResultCode.Refused, HeroText.Of(r.Why));
            }
            case "melt" when p.Length >= 3 && metal(1, out var mt) && N(2) > 0:
            {
                var good = mt switch { MintMetal.Gold => mw.GoldGood, MintMetal.Silver => mw.SilverGood, _ => mw.CopperGood };
                if (good == ushort.MaxValue) return R(c, HeroResultCode.Refused, HeroText.Of("co_no_metal"));
                bool ok = mt switch
                {
                    MintMetal.Gold => book.Stock.MeltGold(led, ctx.Purse, N(2), ctx.Pack, good, mw.Cfg.GoldPieceMg),
                    MintMetal.Silver => book.Stock.MeltSilver(led, ctx.Purse, N(2), ctx.Pack, good, mw.Cfg.CrownSilverMg),
                    _ => book.Stock.MeltCopper(led, ctx.Purse, N(2), ctx.Pack, good, mw.Cfg.CopperPieceMg),
                };
                return ok ? R(c, HeroResultCode.Ok, HeroText.Of("co_melted", N(2))) : R(c, HeroResultCode.Refused, HeroText.Of("co_cannot_melt"));
            }
        }
        return R(c, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"));
    }
}
