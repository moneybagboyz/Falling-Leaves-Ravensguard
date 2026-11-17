using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Weather;

/// <summary>Integer/Fixed helpers for weather: sine, triangle profiles and a 3D value noise that is safe for any hour count.</summary>
public static class WeatherMath
{
    public static Fixed F(int num, int den = 1) => Fixed.FromRatio(num, den);

    /// <summary>sin of an angle in degrees (Bhaskara I approximation, max error ~0.002). Any angle, wraps at 360.</summary>
    public static Fixed SinDeg(Fixed degrees)
    {
        long raw = degrees.Raw;
        long full = 360L << Fixed.FracBits;
        raw %= full; if (raw < 0) raw += full;
        bool neg = raw >= (180L << Fixed.FracBits);
        if (neg) raw -= 180L << Fixed.FracBits;
        // x in [0,180]: sin = 4x(180-x) / (40500 - x(180-x))
        Fixed x = Fixed.FromRaw((int)raw);
        Fixed p = x * (Fixed.FromInt(180) - x);
        Fixed s = Fixed.FromRaw((int)(4L * p.Raw * Fixed.OneRaw / ((40500L << Fixed.FracBits) - p.Raw)));
        return neg ? -s : s;
    }

    public static Fixed CosDeg(Fixed degrees) => SinDeg(degrees + Fixed.FromInt(90));

    /// <summary>Triangle: 1 at centre, 0 at centre ± half.</summary>
    public static Fixed Tri(Fixed d, Fixed centre, Fixed half)
    {
        Fixed a = (d - centre).Abs();
        if (a >= half) return Fixed.Zero;
        return Fixed.One - a / half;
    }

    public static Fixed Clamp01(Fixed v) => Fixed.Clamp(v, Fixed.Zero, Fixed.One);

    public static long FloorDiv(long a, long b) { long q = a / b; return (a % b != 0 && ((a < 0) != (b < 0))) ? q - 1 : q; }

    private static Fixed Lattice(ulong seed, long ix, long iy, long it)
    {
        ulong h = DetRng.Mix(seed ^ ((ulong)ix * 0x9E3779B97F4A7C15UL) ^ ((ulong)iy * 0xC2B2AE3D27D4EB4FUL) ^ ((ulong)it * 0x165667B19E3779F9UL));
        h = DetRng.Mix(h);
        return Fixed.FromRaw((int)(h >> 48));
    }

    private static Fixed Smooth(Fixed t) => t * t * (Fixed.FromInt(3) - t * 2);

    /// <summary>
    /// Value noise in [0,1) over integer coordinates with lattice spacings sx, sy, st (same units as the coordinates).
    /// Safe for arbitrarily large t (hours), unlike Fixed-coordinate noise.
    /// </summary>
    public static Fixed Noise3(ulong seed, long x, long y, long t, int sx, int sy, int st)
    {
        long ix = FloorDiv(x, sx), iy = FloorDiv(y, sy), it = FloorDiv(t, st);
        Fixed fx = Smooth(Fixed.FromRaw((int)((x - ix * sx) * Fixed.OneRaw / sx)));
        Fixed fy = Smooth(Fixed.FromRaw((int)((y - iy * sy) * Fixed.OneRaw / sy)));
        Fixed ft = Smooth(Fixed.FromRaw((int)((t - it * st) * Fixed.OneRaw / st)));
        Fixed a = Fixed.Lerp(Lattice(seed, ix, iy, it), Lattice(seed, ix + 1, iy, it), fx);
        Fixed b = Fixed.Lerp(Lattice(seed, ix, iy + 1, it), Lattice(seed, ix + 1, iy + 1, it), fx);
        Fixed c = Fixed.Lerp(Lattice(seed, ix, iy, it + 1), Lattice(seed, ix + 1, iy, it + 1), fx);
        Fixed d = Fixed.Lerp(Lattice(seed, ix, iy + 1, it + 1), Lattice(seed, ix + 1, iy + 1, it + 1), fx);
        return Fixed.Clamp(Fixed.Lerp(Fixed.Lerp(a, b, fy), Fixed.Lerp(c, d, fy), ft), Fixed.Zero, Fixed.FromRaw(Fixed.OneRaw - 1));
    }

    /// <summary>Uniform integer in [0,100) from a hash of the key parts.</summary>
    public static int Pct(ulong seed, long a, long b, long c)
        => (int)(DetRng.Mix(seed ^ DetRng.Mix((ulong)a ^ DetRng.Mix((ulong)b ^ DetRng.Mix((ulong)c)))) >> 33) % 100;
}
