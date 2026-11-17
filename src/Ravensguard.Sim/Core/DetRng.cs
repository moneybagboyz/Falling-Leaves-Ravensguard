namespace Ravensguard.Sim.Core;

/// <summary>
/// Counter-based deterministic RNG: pure functions of (seed, tick, entity, stream, draw).
/// No shared state, so results never depend on call order or thread order (plan §2.4).
/// </summary>
public static class DetRng
{
    /// <summary>SplitMix64 finalizer.</summary>
    public static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>64 random bits for the given key.</summary>
    public static ulong Next(ulong seed, long tick, ulong entityId, uint stream, uint draw)
    {
        ulong h = Mix(seed ^ 0xA0761D6478BD642FUL);
        h = Mix(h ^ (ulong)tick);
        h = Mix(h ^ entityId);
        h = Mix(h ^ (((ulong)stream << 32) | draw));
        return h;
    }

    public static uint NextUInt(ulong seed, long tick, ulong entityId, uint stream, uint draw)
        => (uint)(Next(seed, tick, entityId, stream, draw) >> 32);

    /// <summary>Fixed in [0,1).</summary>
    public static Fixed NextFixed01(ulong seed, long tick, ulong entityId, uint stream, uint draw)
        => Fixed.FromRaw((int)(Next(seed, tick, entityId, stream, draw) >> 48));

    /// <summary>Integer in [minInclusive, maxExclusive).</summary>
    public static int Range(ulong seed, long tick, ulong entityId, uint stream, uint draw, int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        ulong span = (ulong)((long)maxExclusive - minInclusive);
        // Multiply-shift on the high 32 bits: unbiased enough for spans far below 2^32.
        ulong r = (ulong)NextUInt(seed, tick, entityId, stream, draw) * span >> 32;
        return (int)(minInclusive + (long)r);
    }

    /// <summary>True with probability p (p ≤ 0 never, p ≥ 1 always).</summary>
    public static bool Chance(ulong seed, long tick, ulong entityId, uint stream, uint draw, Fixed p)
        => NextFixed01(seed, tick, entityId, stream, draw) < p;
}

/// <summary>Convenience wrapper holding a key and an incrementing draw counter.</summary>
public struct RngStream
{
    private readonly ulong _seed;
    private readonly long _tick;
    private readonly ulong _entity;
    private readonly uint _stream;
    private uint _draw;

    public RngStream(ulong seed, long tick, ulong entityId, uint stream, uint startDraw = 0)
    {
        _seed = seed; _tick = tick; _entity = entityId; _stream = stream; _draw = startDraw;
    }

    public uint Draws => _draw;
    public uint NextUInt() => DetRng.NextUInt(_seed, _tick, _entity, _stream, _draw++);
    public Fixed NextFixed01() => DetRng.NextFixed01(_seed, _tick, _entity, _stream, _draw++);
    public int Range(int minInclusive, int maxExclusive) => DetRng.Range(_seed, _tick, _entity, _stream, _draw++, minInclusive, maxExclusive);
    public bool Chance(Fixed p) => DetRng.Chance(_seed, _tick, _entity, _stream, _draw++, p);
}
