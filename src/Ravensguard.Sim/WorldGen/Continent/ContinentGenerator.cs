using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen;

/// <summary>
/// Stage 1 world generation: the continent (plan §1A). Pure function of <see cref="ContinentSettings"/>.
/// Integer and Q16.16 maths only (no float/double, no trig/pow), so a seed gives the same continent on every OS.
/// Order: plates, layout + heights, temperature, rain, hydrology, coast, biomes/soil, strata/minerals, cultures/realms.
/// </summary>
public sealed partial class ContinentGenerator
{
    private const int N = Continent.Size;
    private const int A = Continent.Cells;
    private static readonly int[] Dx = [1, 1, 0, -1, -1, -1, 0, 1];
    private static readonly int[] Dy = [0, 1, 1, 1, 0, -1, -1, -1];

    private readonly ContinentSettings _s;
    private readonly ulong _seed;
    private readonly Continent _c;

    private readonly int[] _warpX = new int[A], _warpY = new int[A];
    private readonly int[] _up = new int[A];     // blurred convergent uplift, Q16 (0..65536)
    private readonly int[] _down = new int[A];   // blurred trench / rift depth
    private readonly int[] _slope = new int[A];  // max |dElevation| to the 8 neighbours, raw
    private readonly List<(int X, int Y, int R2)> _blobs = [];

    private ContinentGenerator(ContinentSettings s)
    {
        _s = s; _seed = s.Seed; _c = new Continent(s);
    }

    public static Continent Generate(ContinentSettings settings)
    {
        var g = new ContinentGenerator(settings);
        g.Run();
        return g._c;
    }

    private void Run()
    {
        Layout();
        Plates();
        Heights();
        Slopes();
        Temperature();
        Rain();
        Hydrology();
        CoastDistance();
        Drainage();
        Biomes();
        Soil();
        StrataAndMinerals();
        CulturesAndRealms();
        SavageryLayer();
    }

    // ---------- helpers ----------
    private ulong Salt(uint k) => DetRng.Mix(_seed ^ (0x5EED0000UL + k));
    private int R(uint stream, ulong ent, uint draw, int lo, int hi) => DetRng.Range(_seed, 0, ent, stream, draw, lo, hi);
    private Fixed Nz(uint salt, int x, int y, int div, int oct) => DetNoise.Fbm(Salt(salt), Fixed.FromRatio(x, div), Fixed.FromRatio(y, div), oct);
    private static Fixed F(int pct) => Fixed.FromRatio(pct, 100);
    private static int ISqrt(long v)
    {
        if (v <= 0) return 0;
        long x = v, y = (x + 1) / 2;
        while (y < x) { x = y; y = (x + v / x) / 2; }
        return (int)x;
    }
    private static bool In(int x, int y) => (uint)x < N && (uint)y < N;

    // ---------- layout: blobs of "land potential" ----------
    private void Layout()
    {
        int warpAmp = _s.Layout switch { ContinentLayout.Pangea => 26, ContinentLayout.Continents => 22, _ => 10 };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                _warpX[i] = x + ((Nz(1, x, y, 38, 4).Raw - 32768) * 2 * warpAmp >> 16);
                _warpY[i] = y + ((Nz(2, x, y, 38, 4).Raw - 32768) * 2 * warpAmp >> 16);
            }

        switch (_s.Layout)
        {
            case ContinentLayout.Pangea:
                AddBlob(128 + R(3, 0, 0, -14, 15), 128 + R(3, 0, 1, -14, 15), 96 + R(3, 0, 2, -8, 9));
                break;
            case ContinentLayout.Continents:
            {
                int count = 3 + R(3, 0, 0, 0, 2);
                var pts = BestCandidates(count, 3, 40, 216, 80);
                for (int k = 0; k < count; k++) AddBlob(pts[k].X, pts[k].Y, 50 + R(3, (ulong)k + 1, 9, -6, 11));
                break;
            }
            default:
            {
                const int count = 18;
                var pts = BestCandidates(count, 3, 22, 234, 24);
                for (int k = 0; k < count; k++) AddBlob(pts[k].X, pts[k].Y, 13 + R(3, (ulong)k + 1, 9, 0, 17));
                break;
            }
        }
    }

    private void AddBlob(int x, int y, int r) => _blobs.Add((x, y, r * r));

    /// <summary>Mitchell best-candidate points in [lo,hi]^2 (even spacing).</summary>
    private List<(int X, int Y)> BestCandidates(int count, uint stream, int lo, int hi, int tries)
    {
        var pts = new List<(int X, int Y)>();
        for (int k = 0; k < count; k++)
        {
            long bestScore = -1; (int, int) best = (lo, lo);
            for (int t = 0; t < 12; t++)
            {
                int x = R(stream, 100 + (ulong)k, (uint)(t * 2), lo, hi + 1), y = R(stream, 100 + (ulong)k, (uint)(t * 2 + 1), lo, hi + 1);
                long md = long.MaxValue;
                foreach (var p in pts) md = Math.Min(md, (long)(p.X - x) * (p.X - x) + (long)(p.Y - y) * (p.Y - y));
                if (pts.Count == 0) md = 0;
                if (md > bestScore) { bestScore = md; best = (x, y); }
            }
            pts.Add(best);
        }
        return pts;
    }

    private int Mask(int wx, int wy)
    {
        int best = 0;
        foreach (var (bx, by, r2) in _blobs)
        {
            long d2 = (long)(wx - bx) * (wx - bx) + (long)(wy - by) * (wy - by);
            if (d2 >= r2) continue;
            long v = (r2 - d2) * 65536 / r2;
            v = Math.Min(65536, v * 3 / 2);
            if (v > best) best = (int)v;
        }
        return best;
    }

    // ---------- plates ----------
    private static readonly int[] DirCos = [256, 237, 181, 98, 0, -98, -181, -237, -256, -237, -181, -98, 0, 98, 181, 237];
    private static readonly int[] DirSin = [0, 98, 181, 237, 256, 237, 181, 98, 0, -98, -181, -237, -256, -237, -181, -98];

    private void Plates()
    {
        int np = Math.Clamp(_s.PlateCount, 4, 40);
        var seeds = BestCandidates(np, 10, 8, N - 9, 16);
        for (int p = 0; p < np; p++)
        {
            int m = Mask(seeds[p].X, seeds[p].Y);
            bool cont = m >= 20000 || (_s.Layout != ContinentLayout.Pangea && R(11, (ulong)p, 2, 0, 100) < (_s.Layout == ContinentLayout.Archipelago ? 25 : 12));
            int dir = R(11, (ulong)p, 0, 0, 16), speed = R(11, (ulong)p, 1, 1, 4);
            _c.Plates.Add(new Plate(p, seeds[p].X, seeds[p].Y, cont, DirCos[dir] * speed, DirSin[dir] * speed));
        }

        for (int i = 0; i < A; i++)
        {
            long best = long.MaxValue; int bp = 0;
            for (int p = 0; p < np; p++)
            {
                var pl = _c.Plates[p];
                long d = (long)(_warpX[i] - pl.SeedX) * (_warpX[i] - pl.SeedX) + (long)(_warpY[i] - pl.SeedY) * (_warpY[i] - pl.SeedY);
                if (d < best) { best = d; bp = p; }
            }
            _c.PlateId[i] = (byte)bp;
        }

        // Boundary stamps: convergent uplift / trench / rift, then blurred into ranges.
        var up = new int[A]; var dn = new int[A];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x; var a = _c.Plates[_c.PlateId[i]];
                for (int d = 0; d < 8; d += 2)
                {
                    int nx = x + Dx[d], ny = y + Dy[d];
                    if (!In(nx, ny)) continue;
                    var b = _c.Plates[_c.PlateId[ny * N + nx]];
                    if (b.Id == a.Id) continue;
                    long ddx = b.SeedX - a.SeedX, ddy = b.SeedY - a.SeedY;
                    int len = Math.Max(1, ISqrt(ddx * ddx + ddy * ddy));
                    long conv = ((a.VelX - b.VelX) * ddx + (a.VelY - b.VelY) * ddy) / len;
                    int sf = (int)Math.Clamp(conv * 128, -65536, 65536);
                    int upV = 0, dnV = 0;
                    if (sf > 0)
                    {
                        if (a.Continental && b.Continental) upV = sf * 9 / 10;
                        else if (a.Continental) upV = sf * 6 / 10;
                        else if (b.Continental) dnV = sf / 10;
                        else upV = sf * 35 / 100;
                    }
                    else dnV = -sf * 12 / 100;
                    if (upV > up[i]) up[i] = upV;
                    if (dnV > dn[i]) dn[i] = dnV;
                }
            }
        for (int pass = 0; pass < 3; pass++) { BoxBlur(up, 4); BoxBlur(dn, 3); }
        for (int i = 0; i < A; i++) { _up[i] = Math.Min(65536, up[i] * 5); _down[i] = Math.Min(65536, dn[i] * 4); }
    }

    private static void BoxBlur(int[] a, int r)
    {
        var tmp = new int[A];
        int w = 2 * r + 1;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                long sum = 0;
                for (int k = -r; k <= r; k++) sum += a[y * N + Math.Clamp(x + k, 0, N - 1)];
                tmp[y * N + x] = (int)(sum / w);
            }
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                long sum = 0;
                for (int k = -r; k <= r; k++) sum += tmp[Math.Clamp(y + k, 0, N - 1) * N + x];
                a[y * N + x] = (int)(sum / w);
            }
    }

    // ---------- heights: layout + plates + fBm, then a quantile remap onto the one fixed sea level ----------
    private void Heights()
    {
        int sea = _s.SeaLevelRaw;
        int bigAmp = _s.Layout == ContinentLayout.Archipelago ? 45 : 30;
        var pre = new long[A];
        var rel = new int[A];     // relief only (no layout mask): noise + plate uplift, so interiors are not artificial domes
        long min = long.MaxValue, max = long.MinValue;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                bool cont = _c.Plates[_c.PlateId[i]].Continental;
                Fixed h = Fixed.FromRaw(Mask(_warpX[i], _warpY[i])) * F(55) + (cont ? F(12) : -F(10));
                h += (Nz(20, x, y, 40, 4) - Fixed.Half) * F(bigAmp);
                h += (Nz(21, x, y, 9, 3) - Fixed.Half) * F(10);
                Fixed ridged = Fixed.One - (Nz(22, x, y, 14, 3) * 2 - Fixed.One).Abs();
                Fixed upT = Fixed.FromRaw(_up[i]) * (F(45) + ridged * F(80)) * F(85);
                h += upT;
                h -= Fixed.FromRaw(_down[i]) * F(35);
                rel[i] = (upT + (Nz(20, x, y, 40, 4) - Fixed.Half) * F(30) + (Nz(21, x, y, 9, 3) - Fixed.Half) * F(20)).Raw;
                int d = Math.Min(Math.Min(x, y), Math.Min(N - 1 - x, N - 1 - y));
                if (d < 16) h -= Fixed.FromRatio(16 - d, 16) * F(70);
                pre[i] = h.Raw;
                if (h.Raw < min) min = h.Raw;
                if (h.Raw > max) max = h.Raw;
            }

        var hist = new int[65536];
        var norm = new int[A];
        long span = Math.Max(1, max - min);
        for (int i = 0; i < A; i++) { norm[i] = (int)((pre[i] - min) * 65535 / span); hist[norm[i]]++; }

        int needSea = (int)((long)A * (100 - _s.TargetLandPct) / 100);
        int t = 0, acc = 0;
        for (; t < 65536; t++) { acc += hist[t]; if (acc >= needSea) break; }
        t = Math.Clamp(t, 1, 65534);
        int hi = 65535; acc = 0;                       // 99.5th percentile saturates the peaks
        for (int v = 65535; v >= 0; v--) { acc += hist[v]; if (acc > A / 1000) { hi = v; break; } }
        hi = Math.Max(hi, t + 1);

        for (int i = 0; i < A; i++)
        {
            int v = norm[i];
            int e;
            if (v <= t) e = (int)((long)v * (sea - 1) / t);
            else
            {
                long x = Math.Min(65536L, (long)(v - t) * 65536 / (hi - t));
                long xr = Math.Clamp(rel[i] + 20000, 0, 65536);           // relief share: ranges and noise
                x = (x * 20 + xr * 80) / 100;
                long xc = (x * x / 65536 + x * x / 65536 * x / 65536) / 2;   // between square and cubic: mostly lowland, ranges stand out
                e = (int)(sea + xc * (65535 - sea) / 65536);
            }
            _c.Elevation[i] = Math.Clamp(e, 0, 65535);
        }

        // Ocean = below-sea cells connected to the border; inland below-sea basins become lakes in hydrology.
        var q = new Queue<int>();
        for (int i = 0; i < A; i++)
        {
            int x = i % N, y = i / N;
            if ((x == 0 || y == 0 || x == N - 1 || y == N - 1) && _c.Elevation[i] < sea) { _c.Ocean[i] = true; q.Enqueue(i); }
        }
        while (q.Count > 0)
        {
            int i = q.Dequeue(); int x = i % N, y = i / N;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + Dx[d], ny = y + Dy[d];
                if (!In(nx, ny)) continue;
                int n = ny * N + nx;
                if (!_c.Ocean[n] && _c.Elevation[n] < sea) { _c.Ocean[n] = true; q.Enqueue(n); }
            }
        }
    }

    private void Slopes()
    {
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x, m = 0;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + Dx[d], ny = y + Dy[d];
                    if (!In(nx, ny)) continue;
                    m = Math.Max(m, Math.Abs(_c.Elevation[i] - _c.Elevation[ny * N + nx]));
                }
                _slope[i] = m;
            }
    }

    private int AbovePct(int i) => (int)((long)Math.Max(0, _c.Elevation[i] - _s.SeaLevelRaw) * 100 / (65536 - _s.SeaLevelRaw));

    // ---------- climate ----------
    private void Temperature()
    {
        const int tNorth = -60, tSouth = 260; // tenths of C: the north edge is polar-ish, the south edge warm
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int i = y * N + x;
                int t = tNorth + (tSouth - tNorth) * y / (N - 1) + _s.TemperatureOffsetTenths;
                t += (Nz(30, x, y, 24, 3).Raw - 32768) * 20 >> 16;
                if (_c.Elevation[i] > _s.SeaLevelRaw)
                {
                    int meters = (int)((long)(_c.Elevation[i] - _s.SeaLevelRaw) * 4500 / (65536 - _s.SeaLevelRaw));
                    t -= meters * 65 / 1000;   // 6.5 C per km lapse
                }
                _c.Temperature[i] = (short)Math.Clamp(t, -400, 450);
            }
    }

    /// <summary>West-to-east moisture sweep: oceans refill the air, land rains it out (more on upslopes), leaving a rain shadow.</summary>
    private void Rain()
    {
        var m = new Fixed[N];
        Fixed baseP = F(4), maxP = F(65), oro = Fixed.FromInt(10), hiP = F(4);
        int sea = _s.SeaLevelRaw;
        for (int x = 0; x < N; x++)
        {
            for (int y = 0; y < N; y++)
            {
                int i = y * N + x;
                if (_c.Ocean[i])
                {
                    int tc = Math.Clamp((int)_c.Temperature[i], 0, 300);
                    Fixed cap = (F(55) + Fixed.FromRatio(45 * tc, 100 * 300)) * Fixed.FromRatio(_s.MoisturePct, 100);
                    m[y] += (cap - m[y]) / 2;
                    _c.Rainfall[i] = 0;
                    continue;
                }
                int dh = x > 0 ? Math.Max(0, _c.Elevation[i] - _c.Elevation[i - 1]) : 0;
                Fixed hF = Fixed.FromRaw((int)((long)Math.Max(0, _c.Elevation[i] - sea) * 65536 / (65536 - sea)));
                Fixed p = Fixed.Min(maxP, baseP + Fixed.FromRaw(dh) * oro + hF * hiP);
                Fixed rain = m[y] * p;
                m[y] -= rain * F(50);                       // half of the rain is re-evaporated back into the air
                m[y] += (F(55) * Fixed.FromRatio(_s.MoisturePct, 100) - m[y]) * Fixed.FromRatio(12, 1000);   // slow land evapotranspiration refill
                long mm = (rain.Raw * 30000L >> 16);
                mm = mm * (85 + (Nz(31, x, y, 12, 2).Raw * 30 >> 16)) / 100;
                _c.Rainfall[i] = (short)Math.Clamp(mm, 0, 4000);
            }
            var nm = new Fixed[N];
            for (int y = 0; y < N; y++) nm[y] = (m[Math.Max(0, y - 1)] + m[y] * 2 + m[Math.Min(N - 1, y + 1)]) / 4;
            m = nm;
        }
    }
}
