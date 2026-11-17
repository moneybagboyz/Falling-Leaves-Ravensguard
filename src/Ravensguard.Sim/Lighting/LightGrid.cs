using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Lighting;

/// <summary>One immutable result of <see cref="LightGrid.Compute"/>: per cell ambient and point light, 0-255, for every level.</summary>
public sealed class LightFrame
{
    public int Width { get; }
    public int Height { get; }
    public int MinZ { get; }
    public int MaxZ { get; }
    public int Sequence { get; private set; }
    private readonly byte[] _ambient, _point;

    internal LightFrame(int w, int h, int minZ, int maxZ, byte[] ambient, byte[] point, int sequence)
    { Width = w; Height = h; MinZ = minZ; MaxZ = maxZ; _ambient = ambient; _point = point; Sequence = sequence; }

    // the pair behind a recycled frame: see LightFramePair (a frame is only rewritten by the pair that owns it)
    internal byte[] AmbientArray => _ambient;
    internal byte[] PointArray => _point;
    internal void Restamp(int sequence) => Sequence = sequence;

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

    /// <summary>Bumps whenever walls, indoor flags or static lights change; frames computed before that are out of date.</summary>
    public int Revision { get; private set; }

    public void SetOpaque(int x, int y, int z, bool opaque) { int i = Idx(x, y, z); if (_opaque[i] != opaque) { _opaque[i] = opaque; Revision++; } }
    public void SetIndoor(int x, int y, int z, bool indoor) { int i = Idx(x, y, z); if (_indoor[i] != indoor) { _indoor[i] = indoor; Revision++; } }
    public bool IsOpaque(int x, int y, int z) => In(x, y, z) && _opaque[Idx(x, y, z)];
    public bool IsIndoor(int x, int y, int z) => In(x, y, z) && _indoor[Idx(x, y, z)];

    /// <summary>Replaces the static lights (call once after the map is built, and when a fixed light changes).</summary>
    public void SetStaticLights(IEnumerable<LightSource> lights)
    {
        Array.Clear(_static);
        foreach (var l in lights) Cast(l, _static);
        _sequence++; Revision++;
    }

    /// <summary>Ambient plus static plus dynamic point lights for every cell (a new, independent frame).</summary>
    public LightFrame Compute(Ambient ambient, IReadOnlyList<LightSource>? dynamicLights = null) => Compute(ambient, dynamicLights, null);

    /// <summary>
    /// Same as <see cref="Compute(Ambient, IReadOnlyList{LightSource}?)"/>, but when <paramref name="recycle"/> (a frame of this grid) is given its buffers are
    /// rewritten and it is returned instead of allocating a new frame. Only for a caller that owns the frame and knows nobody else still reads it.
    /// </summary>
    public LightFrame Compute(Ambient ambient, IReadOnlyList<LightSource>? dynamicLights, LightFrame? recycle)
    {
        int n = _opaque.Length;
        bool reuse = recycle != null && recycle.AmbientArray.Length == n && recycle.Width == Width && recycle.Height == Height && recycle.MinZ == MinZ;
        var amb = reuse ? recycle!.AmbientArray : new byte[n];
        var pt = reuse ? recycle!.PointArray : new byte[n];
        Array.Copy(_static, pt, n);
        if (dynamicLights != null) foreach (var l in dynamicLights) Cast(l, pt);
        int plane = Width * Height;
        for (int z = MinZ; z <= MaxZ; z++)
        {
            int outdoor = LightModel.AmbientLevel(ambient, z, false), indoor = LightModel.AmbientLevel(ambient, z, true);
            int o = (z - MinZ) * plane;
            for (int i = 0; i < plane; i++) amb[o + i] = (byte)(_indoor[o + i] ? indoor : outdoor);
        }
        if (reuse) { recycle!.Restamp(++_sequence); return recycle; }
        return new LightFrame(Width, Height, MinZ, MaxZ, amb, pt, ++_sequence);
    }

    /// <summary>
    /// Rewrites only the cells the dynamic lights touched: the squares around <paramref name="previous"/> go back to the static light, then
    /// <paramref name="current"/> is cast. Ambient is untouched, so the frame must have been computed with the current ambient and static revision.
    /// </summary>
    internal void PatchDynamic(LightFrame frame, IReadOnlyList<LightSource>? previous, IReadOnlyList<LightSource>? current)
    {
        var pt = frame.PointArray;
        if (previous != null) foreach (var l in previous) Restore(l, pt);
        if (current != null)
        {
            foreach (var l in current) Restore(l, pt);   // cells shared with a light that stays are rewritten below with every current light
            foreach (var l in current) Cast(l, pt);
        }
        frame.Restamp(++_sequence);
    }

    private void Restore(LightSource l, byte[] pt)
    {
        if (l.Radius <= 0 || l.Z < MinZ || l.Z > MaxZ) return;
        int x0 = Math.Max(0, l.X - l.Radius), x1 = Math.Min(Width - 1, l.X + l.Radius), y0 = Math.Max(0, l.Y - l.Radius), y1 = Math.Min(Height - 1, l.Y + l.Radius);
        for (int y = y0; y <= y1; y++)
        {
            int o = Idx(x0, y, l.Z);
            Array.Copy(_static, o, pt, o, x1 - x0 + 1);
        }
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

/// <summary>
/// Two recycled frames for a live view: each <see cref="Next"/> hands out the frame the previous-but-one call used, so the frame just published stays
/// intact for readers while this one is rewritten. When only the dynamic lights changed (a carried torch moving or flickering) just the squares around
/// the old and new light are rewritten; ambient or static changes recompute the whole frame in place. A frame stays valid until the second call after it.
/// </summary>
public sealed class LightFramePair(LightGrid grid)
{
    private readonly LightFrame?[] _frame = new LightFrame?[2];
    private readonly List<LightSource>[] _dyn = [[], []];
    private readonly Ambient[] _amb = new Ambient[2];
    private readonly int[] _rev = [-1, -1];
    private int _next;
    private readonly List<LightSource> _scratch = [];
    /// <summary>Calls that rewrote the whole frame (the rest patched only the light squares).</summary>
    public int FullComputes { get; private set; }
    public int Patches { get; private set; }

    public LightFrame Next(Ambient ambient, IReadOnlyList<LightSource>? dynamicLights)
    {
        int b = _next; _next ^= 1;
        _scratch.Clear();
        if (dynamicLights != null) for (int i = 0; i < dynamicLights.Count; i++) _scratch.Add(dynamicLights[i]);
        var f = _frame[b];
        if (f == null || _rev[b] != grid.Revision || !_amb[b].Equals(ambient))
        {
            f = _frame[b] = grid.Compute(ambient, _scratch, f);
            _amb[b] = ambient; _rev[b] = grid.Revision; FullComputes++;
        }
        else { grid.PatchDynamic(f, _dyn[b], _scratch); Patches++; }
        _dyn[b].Clear(); _dyn[b].AddRange(_scratch);
        return f;
    }
}
