using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Lighting;

/// <summary>One immutable result of <see cref="LightGrid.Compute"/>: per cell ambient and point light, 0-255, for every level.</summary>
public sealed class LightFrame
{
    public int Width { get; }
    public int Height { get; }
    public int MinZ { get; }
    public int MaxZ { get; }
    public int Sequence { get; }
    private readonly byte[] _ambient, _point;

    internal LightFrame(int w, int h, int minZ, int maxZ, byte[] ambient, byte[] point, int sequence)
    { Width = w; Height = h; MinZ = minZ; MaxZ = maxZ; _ambient = ambient; _point = point; Sequence = sequence; }

    private int Idx(int x, int y, int z) => ((z - MinZ) * Height + y) * Width + x;
    public int Ambient(int x, int y, int z) => _ambient[Idx(x, y, z)];
    public int Point(int x, int y, int z) => _point[Idx(x, y, z)];
    public int Total(int x, int y, int z) => Math.Min(255, _ambient[Idx(x, y, z)] + _point[Idx(x, y, z)]);
    /// <summary>Row-major offset of cell (0,0) on level z, for bulk copies.</summary>
    public int LevelOffset(int z) => (z - MinZ) * Height * Width;
    public ReadOnlySpan<byte> AmbientSpan => _ambient;
    public ReadOnlySpan<byte> PointSpan => _point;
}

/// <summary>
/// Per-cell light 0-255 per level (plan 2.7): ambient (sun, moon, none underground) plus radial point lights that
/// walls block. Static lights (hearths, braziers) are lit once; dynamic ones (a carried torch) are added per compute.
/// Point lights fall off with the square of distance; visibility from the light is a Bresenham walk that stops at opaque cells,
/// so the wall face itself is lit but nothing behind it. Integer maths only.
/// </summary>
public sealed class LightGrid
{
    public int Width { get; }
    public int Height { get; }
    public int MinZ { get; }
    public int MaxZ { get; }
    private readonly bool[] _opaque, _indoor;
    private readonly byte[] _static;
    private int _sequence;

    public LightGrid(int width, int height, int minZ, int maxZ)
    {
        Width = width; Height = height; MinZ = minZ; MaxZ = maxZ;
        int n = width * height * (maxZ - minZ + 1);
        _opaque = new bool[n]; _indoor = new bool[n]; _static = new byte[n];
    }

    public LightGrid(LevelGrid g) : this(g.Width, g.Height, g.MinZ, g.MaxZ) { }

    private int Idx(int x, int y, int z) => ((z - MinZ) * Height + y) * Width + x;
    private bool In(int x, int y, int z) => (uint)x < (uint)Width && (uint)y < (uint)Height && z >= MinZ && z <= MaxZ;

    public void SetOpaque(int x, int y, int z, bool opaque) => _opaque[Idx(x, y, z)] = opaque;
    public void SetIndoor(int x, int y, int z, bool indoor) => _indoor[Idx(x, y, z)] = indoor;
    public bool IsOpaque(int x, int y, int z) => In(x, y, z) && _opaque[Idx(x, y, z)];
    public bool IsIndoor(int x, int y, int z) => In(x, y, z) && _indoor[Idx(x, y, z)];

    /// <summary>Replaces the static lights (call once after the map is built, and when a fixed light changes).</summary>
    public void SetStaticLights(IEnumerable<LightSource> lights)
    {
        Array.Clear(_static);
        foreach (var l in lights) Cast(l, _static);
        _sequence++;
    }

    /// <summary>Ambient plus static plus dynamic point lights for every cell.</summary>
    public LightFrame Compute(Ambient ambient, IReadOnlyList<LightSource>? dynamicLights = null)
    {
        int n = _opaque.Length;
        var amb = new byte[n];
        var pt = new byte[n];
        Array.Copy(_static, pt, n);
        if (dynamicLights != null) foreach (var l in dynamicLights) Cast(l, pt);
        int plane = Width * Height;
        for (int z = MinZ; z <= MaxZ; z++)
        {
            int outdoor = LightModel.AmbientLevel(ambient, z, false), indoor = LightModel.AmbientLevel(ambient, z, true);
            int o = (z - MinZ) * plane;
            for (int i = 0; i < plane; i++) amb[o + i] = (byte)(_indoor[o + i] ? indoor : outdoor);
        }
        return new LightFrame(Width, Height, MinZ, MaxZ, amb, pt, ++_sequence);
    }

    private void Cast(LightSource l, byte[] target)
    {
        if (l.Radius <= 0 || l.Intensity <= 0 || l.Z < MinZ || l.Z > MaxZ) return;
        int r = l.Radius, r2 = r * r;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int d2 = dx * dx + dy * dy;
                if (d2 > r2) continue;
                int x = l.X + dx, y = l.Y + dy;
                if (!In(x, y, l.Z)) continue;
                if (!Clear(l.X, l.Y, x, y, l.Z)) continue;
                int v = l.Intensity * (r2 - d2) / r2;
                int i = Idx(x, y, l.Z);
                target[i] = (byte)Math.Min(255, target[i] + v);
            }
    }

    /// <summary>True when no opaque cell lies strictly between the two cells on a Bresenham line.</summary>
    private bool Clear(int x0, int y0, int x1, int y1, int z)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, e = dx - dy;
        int x = x0, y = y0;
        while (x != x1 || y != y1)
        {
            int e2 = 2 * e;
            if (e2 > -dy) { e -= dy; x += sx; }
            if (e2 < dx) { e += dx; y += sy; }
            if ((x != x1 || y != y1) && _opaque[Idx(x, y, z)]) return false;
        }
        return true;
    }

    public static int VisionRadius(int light) => LightModel.VisionRadius(light);
}
