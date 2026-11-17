namespace Ravensguard.Art;

/// <summary>Deterministic hash/noise helpers, bit-compatible with the concept page's JS.</summary>
public static class Noise
{
    public static double Hash(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 1442695041u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h / 4294967296.0;
        }
    }

    public static double Vn(double x, double y, int s)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        double a = Hash(xi, yi, s), b = Hash(xi + 1, yi, s), c = Hash(xi, yi + 1, s), d = Hash(xi + 1, yi + 1, s);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    /// <summary>mulberry32.</summary>
    public static Func<double> Rng(int seed)
    {
        uint a = unchecked((uint)seed);
        return () =>
        {
            unchecked
            {
                a += 0x6D2B79F5u;
                uint t = (a ^ (a >> 15)) * (1u | a);
                t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        };
    }

    /// <summary>JS Math.round (half rounds up).</summary>
    public static int Round(double v) => (int)Math.Floor(v + 0.5);
}

public sealed class ShadeOptions
{
    public Func<int, int, bool>? Mask { get; init; }
    public bool TopHi { get; init; }
}

/// <summary>RGBA pixel buffer; colours are packed 0xAARRGGBB, 0 is transparent.</summary>
public sealed class PixelBuffer
{
    public int W { get; }
    public int H { get; }
    public uint[] Data { get; }

    public PixelBuffer(int w, int h) { W = w; H = h; Data = new uint[w * h]; }

    public uint Get(int x, int y) => (x < 0 || y < 0 || x >= W || y >= H) ? 0u : Data[y * W + x];
    public bool Opaque(int x, int y) => (Get(x, y) >> 24) != 0;

    public void Set(double x, double y, uint c)
    {
        int xi = Noise.Round(x), yi = Noise.Round(y);
        if (c == 0 || xi < 0 || yi < 0 || xi >= W || yi >= H) return;
        Data[yi * W + xi] = c;
    }

    public void Clear(int x, int y)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        Data[y * W + x] = 0;
    }

    public void Rect(int x, int y, int w, int h, uint c)
    {
        for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Set(x + i, y + j, c);
    }

    /// <summary>Top-left lit block: highlights left edge, shadows right/bottom edges.</summary>
    public void Shade(int x, int y, int w, int h, uint[] ramp, ShadeOptions? o = null)
    {
        int n = ramp.Length;
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                if (o?.Mask != null && !o.Mask(i, j)) continue;
                int k = Math.Min(n - 2, Math.Max(1, n >> 1));
                if (i == 0) k = Math.Min(n - 1, k + 1);
                if (i >= w - 2) k = Math.Max(0, k - 1);
                if (j >= h - 1) k = Math.Max(0, k - 1);
                if (j == 0 && o?.TopHi == true) k = Math.Min(n - 1, k + 1);
                Set(x + i, y + j, ramp[k]);
            }
    }

    public void Line(int x0, int y0, int x1, int y1, uint c)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, e = dx - dy;
        for (int g = 0; g < 400; g++)
        {
            Set(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * e;
            if (e2 > -dy) { e -= dy; x0 += sx; }
            if (e2 < dx) { e += dx; y0 += sy; }
        }
    }

    public void Ellipse(int cx, int cy, int rx, int ry, uint c, bool fill)
    {
        for (int y = -ry; y <= ry; y++)
            for (int x = -rx; x <= rx; x++)
            {
                double v = (double)(x * x) / (rx * rx) + (double)(y * y) / (ry * ry);
                if (fill ? v <= 1 : (v <= 1 && v >= .62)) Set(cx + x, cy + y, c);
            }
    }

    /// <summary>Paints every transparent pixel 4-adjacent to an opaque one.</summary>
    public PixelBuffer Outline(uint c)
    {
        var t = new List<(int, int)>();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (Opaque(x, y)) continue;
                if (Opaque(x - 1, y) || Opaque(x + 1, y) || Opaque(x, y - 1) || Opaque(x, y + 1)) t.Add((x, y));
            }
        foreach (var (x, y) in t) Set(x, y, c);
        return this;
    }

    /// <summary>Copy pixels from another buffer at (dx,dy); transparent source pixels are skipped.</summary>
    public void Blit(PixelBuffer src, int dx, int dy)
    {
        for (int y = 0; y < src.H; y++)
            for (int x = 0; x < src.W; x++)
            {
                uint c = src.Data[y * src.W + x];
                if ((c >> 24) != 0) Set(dx + x, dy + y, c);
            }
    }

    public PixelBuffer Scaled(int k)
    {
        var r = new PixelBuffer(W * k, H * k);
        for (int y = 0; y < r.H; y++)
            for (int x = 0; x < r.W; x++)
                r.Data[y * r.W + x] = Data[(y / k) * W + x / k];
        return r;
    }

    /// <summary>Palette swap: replaces colours by map (e.g. red ramp to green ramp).</summary>
    public PixelBuffer Recolor(IReadOnlyDictionary<uint, uint> map)
    {
        var r = new PixelBuffer(W, H);
        for (int i = 0; i < Data.Length; i++) r.Data[i] = map.TryGetValue(Data[i], out var m) ? m : Data[i];
        return r;
    }

    public static Dictionary<uint, uint> RampSwap(uint[] from, uint[] to)
    {
        var m = new Dictionary<uint, uint>();
        for (int i = 0; i < from.Length && i < to.Length; i++) m[from[i]] = to[i];
        return m;
    }

    public PixelBuffer Clone() { var r = new PixelBuffer(W, H); Array.Copy(Data, r.Data, Data.Length); return r; }
}
