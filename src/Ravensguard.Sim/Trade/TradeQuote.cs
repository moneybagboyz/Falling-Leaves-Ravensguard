using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Trade;

public enum TradeSide : byte { Buy, Sell }

/// <summary>
/// The three numbers the trade screen shows for (good, quantity) where you stand (trade-inventory-character spec 4.1): the price of the first unit, the average over the quantity and the
/// total, from the Phase 2 integral (<see cref="PriceCurve.BuyMid"/> / <see cref="PriceCurve.SellMid"/>) times <c>1 + h</c> buying or <c>1 - h</c> selling. Selling also shows the 5%
/// market toll the seller pays and what is left. Prices per unit are micro-mites per unit (per gram, or per piece).
/// </summary>
public readonly record struct TradeQuote(TradeSide Side, ushort Good, long Qty, long FirstUnitMicro, long AverageMicro, long TotalMites, long TollMites, bool Piece)
{
    /// <summary>What changes hands for the hero: paid when buying, received after the toll when selling.</summary>
    public long NetMites => Side == TradeSide.Buy ? TotalMites : TotalMites - TollMites;
    /// <summary>Per kg (per piece) in mites, for display.</summary>
    public double FirstPerUnitMites => FirstUnitMicro * (Piece ? 1.0 : 1000.0) / PriceCurve.Micro;
    public double AveragePerUnitMites => AverageMicro * (Piece ? 1.0 : 1000.0) / PriceCurve.Micro;
    public static readonly TradeQuote None = default;
}

/// <summary>The price state of one good in one book, frozen at a barrier so the screen can quote any quantity on the main thread without touching the book.</summary>
public readonly record struct BookLine(ushort Good, long StockUnits, long RefMicro, long TargetUnits, int MinPm, int MaxPm, int HalfSpreadPm, int TollPm, bool Piece, long LotUnits)
{
    public PriceCurve Curve => new(RefMicro, TargetUnits, MinPm, MaxPm);
    public long MidMicro => Curve.MidMicro(StockUnits);
    public long AskMicro => MidMicro * (1000 + HalfSpreadPm) / 1000;
    public long BidMicro => MidMicro * (1000 - HalfSpreadPm) / 1000;

    public static BookLine Of(MarketBook b, ushort g)
    {
        var gb = b.Goods[g]; var c = b.Curve(g);
        return new BookLine(g, gb.StockUnits, c.RefMicro, c.TargetUnits, c.MinPermille, c.MaxPermille, b.Money.SpreadHalfPermille, b.Money.MarketTollPermille, b.Econ.Goods.IsPiece(g), b.TotalLotUnits(g));
    }

    public TradeQuote Quote(TradeSide side, long qty) => Quotes.Of(Curve, StockUnits, Good, side, qty, HalfSpreadPm, TollPm, Piece);
}

/// <summary>Exact quotes (integer, the same rounding as <see cref="MarketBook.QuoteBuy"/> and <see cref="MarketBook.SellToMerchants"/>).</summary>
public static class Quotes
{
    /// <summary>
    /// A mass for people to read (Phase 3 cleanup B polish): 100 kg and over in whole kilograms with a thousands separator (8,020 kg), under that one decimal without a trailing zero (45.5 kg, 0.8 kg), under 50 g in grams.
    /// Half rounds away from zero; never prints float noise such as 8019.999.
    /// </summary>
    public static string Weight(long grams)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        long g = Math.Abs(grams); string sign = grams < 0 ? "-" : "";
        if (g < 50) return $"{sign}{g} g";
        if (g >= 100_000) return sign + ((g + 500) / 1000).ToString("N0", inv) + " kg";
        long tenths = (g + 50) / 100;                                     // hundreds of grams, rounded half up
        return sign + (tenths / 10).ToString(inv) + (tenths % 10 == 0 ? "" : "." + (tenths % 10).ToString(inv)) + " kg";
    }

    public static TradeQuote Buy(MarketBook b, ushort g, long qty) => BookLine.Of(b, g).Quote(TradeSide.Buy, qty);
    public static TradeQuote Sell(MarketBook b, ushort g, long qty) => BookLine.Of(b, g).Quote(TradeSide.Sell, qty);

    public static TradeQuote Of(PriceCurve curve, long stock, ushort good, TradeSide side, long qty, int half, int tollPm, bool piece)
    {
        if (qty <= 0 || (side == TradeSide.Buy && qty > stock)) return new TradeQuote(side, good, 0, 0, 0, 0, 0, piece);
        int pm = side == TradeSide.Buy ? 1000 + half : 1000 - half;
        long midFirst = side == TradeSide.Buy ? curve.MidMicro(Math.Max(0, stock - 1)) : curve.MidMicro(stock);
        long first = midFirst * pm / 1000;
        long midTotal = side == TradeSide.Buy ? curve.BuyMid(stock, qty) : curve.SellMid(stock, qty);
        long total = (long)(((Int128)midTotal * pm + 500) / 1000);
        long avg = (long)((Int128)total * PriceCurve.Micro / qty);
        long toll = side == TradeSide.Sell ? (long)(((Int128)total * tollPm + 500) / 1000) : 0;
        return new TradeQuote(side, good, qty, first, avg, total, toll, piece);
    }

    /// <summary>The slippage guard (spec 4.1): a buy may cost at most the quote plus <paramref name="slipPm"/> permille, a sale must bring at least the quote less it.</summary>
    public static long MaxTotal(long quoteMites, int slipPm) => quoteMites + (quoteMites * slipPm + 999) / 1000;
    public static long MinTotal(long quoteMites, int slipPm) => quoteMites - quoteMites * slipPm / 1000;

    /// <summary>A sum for people to read: gp, sp and cp (<see cref="Core.MoneyText"/>; the Game passes its own words through <c>UiKit.Money</c>).</summary>
    public static string Money(long mites) => Core.MoneyText.Format(mites);

    /// <summary>A bulk price with its pack ("4.5 cp a kg", "2 sp 6 cp per 100 kg"), from micro-mites per unit (<see cref="Core.MoneyText.PerUnit"/>).</summary>
    public static string PerUnit(long micro, bool piece) => Core.MoneyText.PerUnit(micro, piece);
}

/// <summary>Individual items (a sword, a saddle) are priced from their value (spec 4.1): ask = value x (1 + h); bid = value x (1 - h), and 70% of that until the buyer has inspected it.</summary>
public static class ItemPricing
{
    public static long Ask(long valueMites, int halfSpreadPm) => (valueMites * (1000 + halfSpreadPm) + 500) / 1000;
    public static long Bid(long valueMites, int halfSpreadPm, bool inspected, int unknownPm = 700)
    {
        long b = (valueMites * (1000 - halfSpreadPm) + 500) / 1000;
        return inspected ? b : (b * unknownPm + 500) / 1000;
    }
}

/// <summary>
/// "Last heard" prices (overview O4, spec 4.2): the hero's notebook ages a note by days since it was heard. Fresh up to 3 days, aged to 15 (amber), old beyond (grey, "?").
/// The local book is exact and bold.
/// </summary>
public enum PriceAge : byte { Exact, Fresh, Aged, Old }

public static class LastHeard
{
    public static PriceAge AgeClass(int ageDays, int freshDays = 3, int oldDays = 15, bool local = false)
        => local ? PriceAge.Exact : ageDays <= freshDays ? PriceAge.Fresh : ageDays <= oldDays ? PriceAge.Aged : PriceAge.Old;

    /// <summary>"1.84 (6 d)", with "?" for an old price; the local market shows the number alone.</summary>
    public static string Cell(long askMicro, bool piece, int ageDays, PriceAge cls)
        => cls == PriceAge.Exact ? Quotes.PerUnit(askMicro, piece) : $"{Quotes.PerUnit(askMicro, piece)} ({ageDays} d){(cls == PriceAge.Old ? " ?" : "")}";
}
