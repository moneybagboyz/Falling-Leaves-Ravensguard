namespace Ravensguard.Sim.Core;

/// <summary>Deterministic 2D value noise and fBm using only integer/Fixed math. Output in [0,1).</summary>
public static class DetNoise
{
    private static Fixed Lattice(ulong seed, int ix, int iy)
    {
        ulong h = DetRng.Mix(seed ^ DetRng.Mix((ulong)(uint)ix | ((ulong)(uint)iy << 32)));
        return Fixed.FromRaw((int)(h >> 48));
    }

    private static Fixed Smooth(Fixed t) => t * t * (Fixed.FromInt(3) - t * 2);

    /// <summary>Single-octave value noise, smoothstep-interpolated.</summary>
    public static Fixed Value(ulong seed, Fixed x, Fixed y)
    {
        int x0 = x.Floor(), y0 = y.Floor();
        Fixed fx = Smooth(x.Frac()), fy = Smooth(y.Frac());
        Fixed a = Lattice(seed, x0, y0), b = Lattice(seed, x0 + 1, y0);
        Fixed c = Lattice(seed, x0, y0 + 1), d = Lattice(seed, x0 + 1, y0 + 1);
        Fixed r = Fixed.Lerp(Fixed.Lerp(a, b, fx), Fixed.Lerp(c, d, fx), fy);
        return Fixed.Clamp(r, Fixed.Zero, Fixed.FromRaw(Fixed.OneRaw - 1));
    }

    /// <summary>Fractal Brownian motion: octaves double frequency and halve amplitude; normalised to [0,1).</summary>
    public static Fixed Fbm(ulong seed, Fixed x, Fixed y, int octaves)
    {
        if (octaves < 1) octaves = 1;
        Fixed sum = Fixed.Zero, amp = Fixed.One, total = Fixed.Zero;
        for (int i = 0; i < octaves; i++)
        {
            sum += Value(DetRng.Mix(seed + (ulong)i), x, y) * amp;
            total += amp;
            amp = amp / 2;
            x = x * 2; y = y * 2;
        }
        return Fixed.Clamp(sum / total, Fixed.Zero, Fixed.FromRaw(Fixed.OneRaw - 1));
    }
}
