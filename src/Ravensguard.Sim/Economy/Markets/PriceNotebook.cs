namespace Ravensguard.Sim.Economy.Markets;

/// <summary>A price someone heard about (money spec: "last heard" knowledge with an age, for caravans and merchants who are not in the market).</summary>
public readonly record struct PriceNote(int Book, ushort Good, long AskMicro, long BidMicro, long StockUnits, int HeardDay)
{
    public int AgeDays(int today) => Math.Max(0, today - HeardDay);
}

/// <summary>
/// What one traveller, caravan or merchant knows of prices elsewhere: the last note per (market, good), with the day it was heard. Deterministic order
/// (book, good) so saves are stable. A note is only ever replaced by a newer one.
/// </summary>
public sealed class PriceNotebook
{
    private readonly SortedDictionary<(int Book, ushort Good), PriceNote> _notes = [];
    public int Count => _notes.Count;

    /// <summary>Records one price; ignored when an equal or newer note already exists.</summary>
    public void Hear(in PriceNote n)
    {
        if (_notes.TryGetValue((n.Book, n.Good), out var old) && old.HeardDay > n.HeardDay) return;
        _notes[(n.Book, n.Good)] = n;
    }

    /// <summary>Reads every price the book shows today (a visit to the market).</summary>
    public void Visit(MarketBook book, int today)
    {
        for (ushort g = 0; g < book.Goods.Length; g++)
        {
            var gb = book.Goods[g];
            if (gb.StockUnits == 0 && gb.LastTradedDay < 0) continue;
            Hear(new PriceNote(book.SettlementIndex, g, book.AskMicro(g), book.BidMicro(g), gb.StockUnits, today));
        }
    }

    public bool TryGet(int book, ushort good, out PriceNote note) => _notes.TryGetValue((book, good), out note);

    /// <summary>The heard ask in mites per kg (per piece for pieces) with its age, or null when the market's price was never heard.</summary>
    public (double Ask, int AgeDays)? AskWithAge(MarketBook b, ushort good, int today)
        => _notes.TryGetValue((b.SettlementIndex, good), out var n) ? (n.AskMicro * (b.Econ.Goods.IsPiece(good) ? 1.0 / PriceCurve.Micro : 1000.0 / PriceCurve.Micro), n.AgeDays(today)) : null;

    public IEnumerable<PriceNote> Notes => _notes.Values;
    public void Load(IEnumerable<PriceNote> notes) { _notes.Clear(); foreach (var n in notes) _notes[(n.Book, n.Good)] = n; }
}
