namespace Ravensguard.Sim.Economy.Markets;

/// <summary>
/// The market price curve and its exact integral (money spec 3.1, 3.2). Prices are micro-mites per unit (a unit is a gram, or a piece for counted goods),
/// so 45 mites per kg is 45,000 micro-mites per gram. <c>mid(s) = Ref x clamp(sqrt(T d / s), min, max)</c> for stock <c>s</c>, with <c>T d</c> the target
/// cover in units. All integer: <see cref="Isqrt"/> is floor on a 128-bit value, results round half up to whole mites at the end.
/// </summary>
public readonly struct PriceCurve
{
    /// <summary>Micro-mites per mite.</summary>
    public const long Micro = 1_000_000;
    private const int Shift = 32;

    /// <summary>Reference price in micro-mites per unit.</summary>
    public long RefMicro { get; }
    /// <summary>Target cover times demand, in units (<c>T x d</c>).</summary>
    public long TargetUnits { get; }
    public int MinPermille { get; }
    public int MaxPermille { get; }

    public PriceCurve(long refMicroPerUnit, long targetUnits, int minPermille = 300, int maxPermille = 4000)
    {
        RefMicro = refMicroPerUnit; TargetUnits = Math.Max(1, targetUnits); MinPermille = minPermille; MaxPermille = maxPermille;
    }

    /// <summary>Floor integer square root of a non-negative 128-bit value (Newton on integers).</summary>
    public static UInt128 Isqrt(UInt128 n)
    {
        if (n < 2) return n;
        int bits = 128 - (int)UInt128.LeadingZeroCount(n);
        UInt128 x = UInt128.One << ((bits + 1) / 2);
        while (true)
        {
            UInt128 y = (x + n / x) >> 1;
            if (y >= x) return x;
            x = y;
        }
    }

    /// <summary><c>sqrt(a x b) x 2^16</c>, floor: the fixed-point square root the integral is built from.</summary>
    private static Int128 SqrtProductFixed(long a, long b) => (Int128)Isqrt(((UInt128)(ulong)a * (UInt128)(ulong)b) << Shift);

    /// <summary>Stock below which the price is flat at the maximum: <c>T d / (max/1000)^2</c>.</summary>
    public long LowStock => (long)((Int128)TargetUnits * 1_000_000 / ((long)MaxPermille * MaxPermille));
    /// <summary>Stock above which the price is flat at the minimum: <c>T d / (min/1000)^2</c>.</summary>
    public long HighStock => (long)((Int128)TargetUnits * 1_000_000 / ((long)MinPermille * MinPermille));

    /// <summary>Price multiplier (permille of Ref) at stock <paramref name="stock"/>.</summary>
    public int MultPermille(long stock)
    {
        if (stock <= 0) return MaxPermille;
        if (stock <= LowStock) return MaxPermille;
        if (stock >= HighStock) return MinPermille;
        // sqrt(T d / s) x 1000 = sqrt(T d x 1e6 / s)
        long v = (long)Isqrt((UInt128)((Int128)TargetUnits * 1_000_000 / stock));
        return (int)Math.Clamp(v, MinPermille, MaxPermille);
    }

    /// <summary>Mid price in micro-mites per unit at this stock (rounded to a micro-mite).</summary>
    public long MidMicro(long stock)
    {
        if (stock <= 0) return RefMicro * MaxPermille / 1000;
        if (stock <= LowStock) return RefMicro * MaxPermille / 1000;
        if (stock >= HighStock) return RefMicro * MinPermille / 1000;
        // Ref x sqrt(T d / s), exact to a micro-mite: sqrt(Td x 2^32 / s) in 16.16
        Int128 q = (Int128)((Int128)TargetUnits << Shift) / stock;
        Int128 r = (Int128)Isqrt((UInt128)q);                                  // sqrt(T d / s) x 2^16
        return (long)(((Int128)RefMicro * r + (1 << 15)) >> 16);
    }

    /// <summary>
    /// Integral of the mid price over stock levels <c>[from, to]</c> in micro-mites (the exact cost of moving the stock between them), via the closed form of the
    /// three zones (flat maximum, square-root curve, flat minimum). Both ends are clamped at zero.
    /// </summary>
    public Int128 IntegralMicro(long from, long to)
    {
        if (to < from) (from, to) = (to, from);
        if (from < 0) from = 0;
        if (to <= from) return 0;
        long lo = LowStock, hi = HighStock;
        Int128 total = 0;
        // zone A: [0, lo] at max multiplier
        long aEnd = Math.Min(to, lo);
        if (aEnd > from) total += (Int128)RefMicro * MaxPermille * (aEnd - from) / 1000;
        // zone B: [lo, hi]; Ref x 2 x (sqrt(Td b) - sqrt(Td a)) in 16.16
        long bFrom = Math.Max(from, lo), bTo = Math.Min(to, hi);
        if (bTo > bFrom)
        {
            Int128 sb = SqrtProductFixed(TargetUnits, bTo), sa = SqrtProductFixed(TargetUnits, bFrom);
            total += ((Int128)RefMicro * 2 * (sb - sa)) >> 16;
        }
        // zone C: [hi, inf) at min multiplier
        long cFrom = Math.Max(from, hi);
        if (to > cFrom) total += (Int128)RefMicro * MinPermille * (to - cFrom) / 1000;
        return total;
    }

    /// <summary>Whole mites of a micro-mite amount, rounded half up.</summary>
    public static long ToMites(Int128 micro) => (long)((micro + Micro / 2) / Micro);

    /// <summary>
    /// Cost in mites of buying <paramref name="qty"/> units from a book holding <paramref name="stock"/>: the integral from <c>stock - qty</c> to <c>stock</c>,
    /// before the spread. Selling onto the book is <see cref="SellMid"/>.
    /// </summary>
    public long BuyMid(long stock, long qty) => ToMites(IntegralMicro(stock - qty, stock));
    /// <summary>Value in mites of adding <paramref name="qty"/> units to a book holding <paramref name="stock"/> (integral from stock to stock + qty), before the spread.</summary>
    public long SellMid(long stock, long qty) => ToMites(IntegralMicro(stock, stock + qty));

    /// <summary>
    /// Largest quantity (units, at most <paramref name="cap"/>) whose customer total <c>mid cost x (1000 + half) / 1000</c> is within <paramref name="budgetMites"/>,
    /// found by 40 steps of integer bisection (money spec 3.2).
    /// </summary>
    public long MaxAffordable(long stock, long cap, long budgetMites, int halfSpreadPermille)
    {
        cap = Math.Min(cap, stock);
        if (cap <= 0 || budgetMites <= 0) return 0;
        long lo = 0, hi = cap;
        if (Total(stock, hi, halfSpreadPermille) <= budgetMites) return hi;
        for (int i = 0; i < 40 && lo < hi; i++)
        {
            long mid = lo + (hi - lo + 1) / 2;
            if (Total(stock, mid, halfSpreadPermille) <= budgetMites) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    private long Total(long stock, long qty, int half) => (long)(((Int128)BuyMid(stock, qty) * (1000 + half) + 500) / 1000);

    /// <summary>
    /// Largest quantity a merchant paying out of <paramref name="budgetMites"/> can take onto the book at the bid price (<c>mid x (1000 - half)</c>), at most <paramref name="cap"/>.
    /// </summary>
    public long MaxSellable(long stock, long cap, long budgetMites, int halfSpreadPermille)
    {
        if (cap <= 0 || budgetMites <= 0) return 0;
        var self = this;
        long Bid(long q) => (long)(((Int128)self.SellMid(stock, q) * (1000 - halfSpreadPermille) + 500) / 1000);
        if (Bid(cap) <= budgetMites) return cap;
        long lo = 0, hi = cap;
        for (int i = 0; i < 40 && lo < hi; i++)
        {
            long mid = lo + (hi - lo + 1) / 2;
            if (Bid(mid) <= budgetMites) lo = mid; else hi = mid - 1;
        }
        return lo;
    }
}
