using System.Runtime.CompilerServices;

namespace Ravensguard.Sim.Core;

/// <summary>Q16.16 fixed-point number. All arithmetic is integer-only (long intermediates) and deterministic.</summary>
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
    public static Fixed FromInt(int v) => new(checked(v << FracBits));

    /// <summary>num/den, rounded toward zero.</summary>
    public static Fixed FromRatio(int num, int den) => new((int)(((long)num << FracBits) / den));

    /// <summary>Display/debug only; never use in simulation arithmetic.</summary>
    public float ToFloat() => Raw / (float)OneRaw;
    public double ToDouble() => Raw / (double)OneRaw;

    /// <summary>Largest integer not greater than this value.</summary>
    public int Floor() => Raw >> FracBits;

    /// <summary>Fractional part in [0,1).</summary>
    public Fixed Frac() => new(Raw & (OneRaw - 1));

    public Fixed Abs() => Raw < 0 ? new(-Raw) : this;

    public static Fixed Min(Fixed a, Fixed b) => a.Raw <= b.Raw ? a : b;
    public static Fixed Max(Fixed a, Fixed b) => a.Raw >= b.Raw ? a : b;
    public static Fixed Clamp(Fixed v, Fixed lo, Fixed hi) => Min(Max(v, lo), hi);

    /// <summary>a + (b-a)*t.</summary>
    public static Fixed Lerp(Fixed a, Fixed b, Fixed t) => a + (b - a) * t;

    public static Fixed operator +(Fixed a, Fixed b) => new(a.Raw + b.Raw);
    public static Fixed operator -(Fixed a, Fixed b) => new(a.Raw - b.Raw);
    public static Fixed operator -(Fixed a) => new(-a.Raw);
    public static Fixed operator *(Fixed a, Fixed b) => new((int)(((long)a.Raw * b.Raw) >> FracBits));
    public static Fixed operator /(Fixed a, Fixed b) => new((int)(((long)a.Raw << FracBits) / b.Raw));
    public static Fixed operator *(Fixed a, int b) => new(checked(a.Raw * b));
    public static Fixed operator *(int a, Fixed b) => b * a;
    public static Fixed operator /(Fixed a, int b) => new(a.Raw / b);

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
