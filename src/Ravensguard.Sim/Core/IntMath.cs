namespace Ravensguard.Sim.Core;

/// <summary>
/// Integer trigonometry and roots for sim decisions (plan 2.4: no floating point decides an outcome; review finding 7 replaced <c>Math.Cos</c>, <c>Math.Sin</c> and <c>Math.Cbrt</c>).
/// Directions are fractions of a turn; cosine and sine come back in Q30 (2^30 is one), computed by a Taylor series in 128-bit Q60 fixed point after reduction to the first
/// octant, so every platform gets the same bits. <see cref="Directions"/> caches the table of an n-way compass.
/// </summary>
public static class IntMath
{
    public const int Q30Bits = 30;
    public const long One30 = 1L << Q30Bits;
    private const int Q = 60;
    /// <summary>pi x 2^60, rounded.</summary>
    private static readonly Int128 PiQ60 = Int128.Parse("3622009729038561421");

    /// <summary>Cosine and sine of the angle <c>2 pi num / den</c> (a fraction of a turn, any sign) in Q30, rounded to nearest.</summary>
    public static (long Cos, long Sin) CosSinQ30(long num, long den)
    {
        if (den <= 0) throw new ArgumentOutOfRangeException(nameof(den));
        num %= den; if (num < 0) num += den;
        Int128 n8 = (Int128)num * 8;
        int oct = (int)(n8 / den); Int128 rem = n8 % den;
        Int128 part = (oct & 1) == 0 ? rem : den - rem;                      // x = part / den of an eighth of a turn, in [0, pi/4]
        Int128 x = part * PiQ60 / ((Int128)den * 4);
        var (c, s) = CosSinQ60(x);
        long C = Round30(c), S = Round30(s);
        return oct switch
        {
            0 => (C, S), 1 => (S, C), 2 => (-S, C), 3 => (-C, S),
            4 => (-C, -S), 5 => (-S, -C), 6 => (S, -C), _ => (C, -S),
        };
    }

    private static long Round30(Int128 v) => (long)((v + ((Int128)1 << (Q - Q30Bits - 1))) >> (Q - Q30Bits));

    /// <summary>cos and sin of x (Q60, 0 &lt;= x &lt;= pi/4) in Q60.</summary>
    private static (Int128 Cos, Int128 Sin) CosSinQ60(Int128 x)
    {
        Int128 x2 = (x * x) >> Q;
        Int128 sin = x, term = x;
        for (int k = 1; term != 0; k++) { term = -((term * x2) >> Q) / ((2 * k) * (2 * k + 1)); sin += term; }
        Int128 one = (Int128)1 << Q, cos = one; term = one;
        for (int k = 1; term != 0; k++) { term = -((term * x2) >> Q) / ((2 * k - 1) * (2 * k)); cos += term; }
        return (cos, sin);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (long Cos, long Sin)[]> Tables = new();

    /// <summary>The <paramref name="n"/>-way compass: entry k is the direction <c>2 pi k / n</c> (east first, counter-clockwise in maths axes), cos and sin in Q30.</summary>
    public static IReadOnlyList<(long Cos, long Sin)> Directions(int n)
        => Tables.GetOrAdd(n, m => { var t = new (long, long)[m]; for (int k = 0; k < m; k++) t[k] = CosSinQ30(k, m); return t; });

    /// <summary>Floor of the cube root of a non-negative integer.</summary>
    public static long CbrtFloor(long n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        long lo = 0, hi = 2_097_152;                                             // 2^21: (2^21)^3 = 2^63 is past every long
        while (lo < hi)
        {
            long mid = (lo + hi + 1) / 2;
            if ((Int128)mid * mid * mid <= n) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>The cube root rounded to the nearest integer (no exact half exists: 8n is even, (2c + 1)^3 odd), what <c>Math.Round(Math.Cbrt(n))</c> meant.</summary>
    public static long CbrtRound(long n)
    {
        long c = CbrtFloor(n);
        Int128 h = 2 * (Int128)c + 1;
        return 8 * (Int128)n >= h * h * h ? c + 1 : c;
    }

    /// <summary>P5-G (vehicle-designer 5): the floor of the square root of a non-negative integer (boat speeds).</summary>
    public static long Isqrt(long n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (n < 2) return n;
        long x = (long)Math.Sqrt(n);                                              // a guess; corrected below so the result is exact on every machine
        while (x * x > n) x--;
        while ((x + 1) * (x + 1) <= n) x++;
        return x;
    }
}
