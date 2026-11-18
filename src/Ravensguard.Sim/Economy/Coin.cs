using System.Globalization;

namespace Ravensguard.Sim.Economy;

/// <summary>
/// An amount of money in mites, 1/100 of a crown (overview 2.1, E1). A 64-bit integer with checked arithmetic: overflow throws, it never wraps.
/// Negative values are legal only as differences; no purse may hold one.
/// </summary>
public readonly record struct Coin(long Mites) : IComparable<Coin>
{
    public const long MitesPerCrown = 100;
    public static readonly Coin Zero = new(0);

    public static Coin FromMites(long mites) => new(mites);
    public static Coin FromCrowns(long crowns) => new(checked(crowns * MitesPerCrown));

    /// <summary>Whole crowns, truncated toward zero.</summary>
    public long WholeCrowns => Mites / MitesPerCrown;
    /// <summary>The mite remainder beyond whole crowns (same sign as the amount).</summary>
    public int MiteRemainder => (int)(Mites % MitesPerCrown);
    public bool IsZero => Mites == 0;
    public bool IsNegative => Mites < 0;

    public static Coin operator +(Coin a, Coin b) => new(checked(a.Mites + b.Mites));
    public static Coin operator -(Coin a, Coin b) => new(checked(a.Mites - b.Mites));
    public static Coin operator -(Coin a) => new(checked(-a.Mites));
    public static Coin operator *(Coin a, long k) => new(checked(a.Mites * k));
    public static Coin operator *(long k, Coin a) => a * k;
    public static bool operator <(Coin a, Coin b) => a.Mites < b.Mites;
    public static bool operator >(Coin a, Coin b) => a.Mites > b.Mites;
    public static bool operator <=(Coin a, Coin b) => a.Mites <= b.Mites;
    public static bool operator >=(Coin a, Coin b) => a.Mites >= b.Mites;
    public int CompareTo(Coin other) => Mites.CompareTo(other.Mites);

    public static Coin Min(Coin a, Coin b) => a <= b ? a : b;
    public static Coin Max(Coin a, Coin b) => a >= b ? a : b;
    public static Coin Sum(IEnumerable<Coin> coins) { var s = Zero; foreach (var c in coins) s += c; return s; }

    /// <summary><c>mites x num / den</c> rounded half up, with a 128-bit intermediate (for shares, tolls, seigniorage).</summary>
    public Coin MulDiv(long num, long den)
    {
        if (den <= 0) throw new ArgumentOutOfRangeException(nameof(den));
        Int128 v = (Int128)Mites * num;
        Int128 r = v >= 0 ? (v * 2 + den) / (2 * (Int128)den) : -((-v * 2 + den) / (2 * (Int128)den));
        return new((long)r);
    }

    /// <summary>Crowns with two decimals, invariant culture: <c>1234.05</c>, <c>-0.07</c>.</summary>
    public override string ToString()
    {
        ulong abs = Mites < 0 ? (ulong)(-(Mites + 1)) + 1UL : (ulong)Mites;
        string s = (abs / 100).ToString(CultureInfo.InvariantCulture) + "." + (abs % 100).ToString("00", CultureInfo.InvariantCulture);
        return Mites < 0 ? "-" + s : s;
    }

    /// <summary>Parses the <see cref="ToString"/> form (whole crowns with up to two decimals).</summary>
    public static bool TryParse(string text, out Coin coin)
    {
        coin = Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool neg = text[0] == '-'; var t = neg ? text[1..] : text;
        var parts = t.Split('.');
        if (parts.Length > 2 || parts[0].Length == 0 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long crowns)) return false;
        long mites = 0;
        if (parts.Length == 2)
        {
            if (parts[1].Length is 0 or > 2 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out mites)) return false;
            if (parts[1].Length == 1) mites *= 10;
        }
        try { coin = new Coin(checked((neg ? -1 : 1) * (crowns * 100 + mites))); return true; } catch (OverflowException) { return false; }
    }
}
