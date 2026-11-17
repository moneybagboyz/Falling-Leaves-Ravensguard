using System.Runtime.CompilerServices;

namespace Ravensguard.Sim.Core;

/// <summary>
/// Q16.16 fixed-point number. All arithmetic is integer-only (long intermediates) and deterministic.
/// Range: [-32768, 32767.99998]. Any result outside it throws <see cref="OverflowException"/> (never wraps silently).
/// Rounding rule everywhere (*, /, FromRatio): toward negative infinity (floor), the same as the arithmetic shift.
/// So rounding is not symmetric: FromRatio(1,3).Raw = 21845 but FromRatio(-1,3).Raw = -21846.
/// </summary>
public readonly struct Fixed : IComparable<Fixed>, IEquatable<Fixed>
{
    public const int FracBits = 16;
    public const int OneRaw = 1 << FracBits;

    /// <summary>Underlying Q16.16 value.</summary>
    public int Raw { get; }

    private Fixed(int raw) => Raw = raw;

    public static readonly Fixed Zero = new(0);
    public static readonly Fixed One = new(OneRaw);
    public static readonly Fixed Half = new(OneRaw / 2);
    public static readonly Fixed MaxValue = new(int.MaxValue);
    public static readonly Fixed MinValue = new(int.MinValue);

    public static Fixed FromRaw(int raw) => new(raw);
    public static Fixed FromInt(int v)
    {
        if (v < short.MinValue || v > short.MaxValue) throw new OverflowException($"{v} is outside the Fixed range.");
        return new(v << FracBits);
    }

    /// <summary>num/den, rounded toward negative infinity.</summary>
    public static Fixed FromRatio(int num, int den) => Narrow(FloorDiv((long)num << FracBits, den));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed Narrow(long v)
    {
        int r = (int)v;
        if (r != v) throw new OverflowException("Fixed arithmetic overflow.");
        return new(r);
    }

    private static long FloorDiv(long n, long d)
    {
        long q = n / d; // DivideByZeroException for d == 0
        if ((n % d != 0) && ((n ^ d) < 0)) q--;
        return q;
    }

    /// <summary>Display/debug only; never use in simulation arithmetic.</summary>
    public float ToFloat() => Raw / (float)OneRaw;
    public double ToDouble() => Raw / (double)OneRaw;

    /// <summary>Largest integer not greater than this value.</summary>
    public int Floor() => Raw >> FracBits;

    /// <summary>Fractional part in [0,1).</summary>
    public Fixed Frac() => new(Raw & (OneRaw - 1));

    public Fixed Abs() => Raw < 0 ? Narrow(-(long)Raw) : this;

    public static Fixed Min(Fixed a, Fixed b) => a.Raw <= b.Raw ? a : b;
    public static Fixed Max(Fixed a, Fixed b) => a.Raw >= b.Raw ? a : b;
    public static Fixed Clamp(Fixed v, Fixed lo, Fixed hi) => Min(Max(v, lo), hi);

    /// <summary>a + (b-a)*t.</summary>
    public static Fixed Lerp(Fixed a, Fixed b, Fixed t) => a + (b - a) * t;

    public static Fixed operator +(Fixed a, Fixed b) => Narrow((long)a.Raw + b.Raw);
    public static Fixed operator -(Fixed a, Fixed b) => Narrow((long)a.Raw - b.Raw);
    public static Fixed operator -(Fixed a) => Narrow(-(long)a.Raw);
    public static Fixed operator *(Fixed a, Fixed b) => Narrow(((long)a.Raw * b.Raw) >> FracBits);
    public static Fixed operator /(Fixed a, Fixed b) => Narrow(FloorDiv((long)a.Raw << FracBits, b.Raw));
    public static Fixed operator *(Fixed a, int b) => Narrow((long)a.Raw * b);
    public static Fixed operator *(int a, Fixed b) => b * a;
    public static Fixed operator /(Fixed a, int b) => Narrow(FloorDiv(a.Raw, b));

    public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;
    public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;
    public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;
    public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;
    public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
    public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;

    public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);
    public bool Equals(Fixed other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fixed f && f.Raw == Raw;
    public override int GetHashCode() => Raw;
    public override string ToString() => ToDouble().ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
