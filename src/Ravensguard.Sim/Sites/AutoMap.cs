using Ravensguard.Sim.Lighting;

namespace Ravensguard.Sim.Sites;

/// <summary>
/// The auto-drawn map of a site (exploration-and-sites 4.7): one bit per cell per level, set when the cell is in line of sight of an observer on the same level, lit at least
/// 30 and within the vision radius for its light (adjacent cells are always felt). Saved run-length coded (about 1 KB a level, EX-11). Two maps merge when their parties are
/// within 20 m. Deterministic from the walk and the seeds.
/// </summary>
public sealed class AutoMap
{
    public int W { get; }
    public int H { get; }
    public int Levels { get; }
    private readonly ulong[] _bits;
    public List<MapPin> Pins { get; } = [];
    /// <summary>Bumps when a cell becomes known (the view redraws).</summary>
    public int Revision { get; private set; }

    public AutoMap(int w, int h, int levels) { W = w; H = h; Levels = levels; _bits = new ulong[(w * h * levels + 63) / 64]; }

    public bool Known(int cell) => (uint)cell < (uint)(W * H * Levels) && (_bits[cell >> 6] & (1UL << (cell & 63))) != 0;
    public void Mark(int cell) { if ((uint)cell >= (uint)(W * H * Levels)) return; ulong b = 1UL << (cell & 63); if ((_bits[cell >> 6] & b) == 0) { _bits[cell >> 6] |= b; Revision++; } }
    public int Count { get { int n = 0; foreach (var w in _bits) n += System.Numerics.BitOperations.PopCount(w); return n; } }
    public int CountLevel(int level) { int n = 0, p = W * H; for (int i = level * p; i < (level + 1) * p; i++) if (Known(i)) n++; return n; }

    /// <summary>Merges another map into this one (parties within 20 m share what they drew).</summary>
    public void Merge(AutoMap o) { for (int i = 0; i < _bits.Length && i < o._bits.Length; i++) { if ((o._bits[i] & ~_bits[i]) != 0) Revision++; _bits[i] |= o._bits[i]; } }

    /// <summary>
    /// Marks what an observer at cell (<paramref name="x"/>, <paramref name="y"/>) on <paramref name="level"/> sees: cells within the frame's vision rule (4.7) in line of sight.
    /// The walls the light falls on are marked too (they stop the sight line, but their faces are seen).
    /// </summary>
    public int Observe(SiteMap m, LightFrame f, int level, int x, int y, int seenLight = 30, int maxR = 40)
    {
        int before = Revision, z = SiteMap.Z(level);
        int x0 = Math.Max(0, x - maxR), x1 = Math.Min(W - 1, x + maxR), y0 = Math.Max(0, y - maxR), y1 = Math.Min(H - 1, y + maxR);
        for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
            {
                int cell = m.Index(level, cx, cy);
                if (Known(cell)) continue;
                int dx = cx - x, dy = cy - y;
                long d2 = (long)dx * dx + dy * dy;
                if (d2 <= 2) { Mark(cell); continue; }                    // felt in the dark
                int light = f.Total(cx, cy, z);
                if (light < seenLight) continue;
                long vr = SiteLight.VisionRadiusMm(light);
                if (d2 * 1_000_000 > vr * vr) continue;
                if (!Clear(m, level, x, y, cx, cy)) continue;
                Mark(cell);
            }
        return Revision - before;
    }

    /// <summary>True when no opaque cell lies strictly between the two cells (Bresenham).</summary>
    public static bool Clear(SiteMap m, int level, int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, e = dx - dy;
        int x = x0, y = y0;
        while (x != x1 || y != y1)
        {
            int e2 = 2 * e;
            if (e2 > -dy) { e -= dy; x += sx; }
            if (e2 < dx) { e += dx; y += sy; }
            if ((x != x1 || y != y1) && SiteTiles.Opaque(m.Tile(level, x, y))) return false;
        }
        return true;
    }

    /// <summary>Run-length coding of the bits, level after level: alternating run lengths (unknown first) as varints.</summary>
    public byte[] ToRle()
    {
        var o = new List<byte>();
        int n = W * H * Levels, i = 0; bool cur = false;
        while (i < n)
        {
            int run = 0;
            while (i < n && Known(i) == cur) { run++; i++; }
            uint v = (uint)run;
            while (v >= 0x80) { o.Add((byte)(v | 0x80)); v >>= 7; }
            o.Add((byte)v);
            cur = !cur;
        }
        return [.. o];
    }

    public static AutoMap FromRle(int w, int h, int levels, ReadOnlySpan<byte> rle)
    {
        var m = new AutoMap(w, h, levels);
        int i = 0, p = 0, n = w * h * levels; bool cur = false;
        while (p < rle.Length && i < n)
        {
            uint v = 0; int shift = 0;
            while (p < rle.Length) { byte b = rle[p++]; v |= (uint)(b & 0x7F) << shift; if ((b & 0x80) == 0) break; shift += 7; }
            for (uint k = 0; k < v && i < n; k++, i++) if (cur) m._bits[i >> 6] |= 1UL << (i & 63);
            cur = !cur;
        }
        m.Revision = 0;
        return m;
    }

    public ulong Hash(ulong h)
    {
        foreach (var w in _bits) { unchecked { h ^= w; h *= 1099511628211UL; } }
        return h;
    }
}

/// <summary>The last cells a hero walked (4.7, a ring of 400): Retrace steps walks them back to the portal the hero came by.</summary>
public sealed class Breadcrumbs
{
    private readonly int[] _ring;
    private int _head, _count;
    public Breadcrumbs(int capacity = 400) { _ring = new int[capacity]; }
    public int Count => _count;
    public int Capacity => _ring.Length;

    public void Add(int cell)
    {
        if (_count > 0 && Newest == cell) return;
        _ring[_head] = cell; _head = (_head + 1) % _ring.Length; if (_count < _ring.Length) _count++;
    }

    public int Newest => _count == 0 ? -1 : _ring[(_head - 1 + _ring.Length) % _ring.Length];
    /// <summary>The <paramref name="back"/>th cell back (0 = newest).</summary>
    public int Back(int back) => back >= _count ? -1 : _ring[(_head - 1 - back + _ring.Length * 2) % _ring.Length];
    public int[] ToArray() { var a = new int[_count]; for (int i = 0; i < _count; i++) a[i] = Back(_count - 1 - i); return a; }
    public static Breadcrumbs From(int capacity, IEnumerable<int> cells) { var b = new Breadcrumbs(capacity); foreach (int c in cells) b.Add(c); return b; }

    /// <summary>
    /// The walked way back from the newest cell to <paramref name="until"/> (inclusive, if met), loops cut out: each cell appears once, the path follows the crumbs in reverse.
    /// </summary>
    public List<int> WayBack(Func<int, bool> until)
    {
        var path = new List<int>(); var at = new Dictionary<int, int>();
        for (int i = 0; i < _count; i++)
        {
            int c = Back(i);
            if (at.TryGetValue(c, out int k)) { for (int j = path.Count - 1; j > k; j--) { at.Remove(path[j]); path.RemoveAt(j); } continue; }
            at[c] = path.Count; path.Add(c);
            if (until(c)) break;
        }
        return path;
    }
}
