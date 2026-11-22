using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Combat.Mounted;

/// <summary>
/// The ground of a battle box for horses, one byte per 4 m cell (cavalry-and-mounted-war 4.8, P7 CV-C): the footing class of each cell and, when given, the ground height at each cell's centre (mm,
/// the Phase 1 heights), from which a slope along any heading comes by integer bilinear interpolation. A cell holds 0 for "no data" (the terrain's zones and base answer) or footing + 1. Built when the
/// box opens from the chunks' surfaces by a sampler the game supplies (<see cref="Build"/>), changed when a piece is built or a breach made (<see cref="Set"/>); a fire layer marks burning cells.
/// A pure integer structure: its <see cref="Hash"/> is the build hash (the cells are rebuilt from the chunks on load, as zones are re-supplied by the setup).
/// </summary>
public sealed class FootingCells
{
    /// <summary>The cell size, mm (the battle grid's).</summary>
    public const int CellMm = 4000;

    public int Ox { get; }
    public int Oy { get; }
    public int W { get; }
    public int H { get; }
    private readonly byte[] _f;
    private int[]? _h;
    private byte[]? _fire;

    /// <summary>A box of <paramref name="w"/> x <paramref name="h"/> cells whose north-west corner is at (<paramref name="ox"/>, <paramref name="oy"/>) mm.</summary>
    public FootingCells(int ox, int oy, int w, int h)
    {
        Ox = ox; Oy = oy; W = Math.Max(1, w); H = Math.Max(1, h);
        _f = new byte[W * H];
    }

    public bool HasHeights => _h != null;

    private int Index(int x, int y)
    {
        if (x < Ox || y < Oy) return -1;
        int cx = (x - Ox) / CellMm, cy = (y - Oy) / CellMm;
        return cx >= W || cy >= H ? -1 : cy * W + cx;
    }

    /// <summary>The footing of the cell under (x, y) mm; false when the point is off the box or the cell holds no data.</summary>
    public bool TryAt(int x, int y, out Footing f)
    {
        int i = Index(x, y);
        if (i < 0 || _f[i] == 0) { f = Footing.Field; return false; }
        f = (Footing)(_f[i] - 1);
        return true;
    }

    public void Set(int x, int y, Footing f) { int i = Index(x, y); if (i >= 0) _f[i] = (byte)((int)f + 1); }

    /// <summary>Sets every cell whose centre lies in the rectangle (mm, half open).</summary>
    public void Fill(int x0, int y0, int x1, int y1, Footing f)
    {
        for (int cy = 0; cy < H; cy++)
            for (int cx = 0; cx < W; cx++)
            {
                int mx = Ox + cx * CellMm + CellMm / 2, my = Oy + cy * CellMm + CellMm / 2;
                if (mx >= x0 && mx < x1 && my >= y0 && my < y1) _f[cy * W + cx] = (byte)((int)f + 1);
            }
    }

    /// <summary>Plants a stake row (a build piece) in the cell under (x, y).</summary>
    public void PlantStakes(int x, int y) => Set(x, y, Footing.Stakes);

    /// <summary>Digs a ditch (a build piece) in the cell under (x, y).</summary>
    public void DigDitch(int x, int y) => Set(x, y, Footing.Ditch);

    /// <summary>Fills the cells from a sampler of (cell x, cell y) in the box: the footing and the height of the cell's centre (mm). The game's chunk adapter is such a sampler.</summary>
    public void Build(Func<int, int, (Footing Footing, int HeightMm)> sample)
    {
        _h ??= new int[W * H];
        for (int cy = 0; cy < H; cy++)
            for (int cx = 0; cx < W; cx++)
            {
                var (f, h) = sample(Ox / CellMm + cx, Oy / CellMm + cy);
                _f[cy * W + cx] = (byte)((int)f + 1); _h[cy * W + cx] = h;
            }
    }

    /// <summary>Gives a cell its centre height (mm).</summary>
    public void SetHeight(int x, int y, int mm) { _h ??= new int[W * H]; int i = Index(x, y); if (i >= 0) _h[i] = mm; }

    /// <summary>Marks the cell under (x, y) burning or not.</summary>
    public void SetFire(int x, int y, bool on)
    {
        int i = Index(x, y); if (i < 0) return;
        _fire ??= new byte[W * H];
        _fire[i] = (byte)(on ? 1 : 0);
    }

    public bool HasFire => _fire != null;

    /// <summary>True when a burning cell's centre lies within <paramref name="radiusMm"/> of (x, y).</summary>
    public bool FireNear(int x, int y, int radiusMm)
    {
        if (_fire == null) return false;
        int c0 = Math.Max(0, (x - radiusMm - Ox) / CellMm - 1), c1 = Math.Min(W - 1, (x + radiusMm - Ox) / CellMm + 1);
        int r0 = Math.Max(0, (y - radiusMm - Oy) / CellMm - 1), r1 = Math.Min(H - 1, (y + radiusMm - Oy) / CellMm + 1);
        long rr = (long)radiusMm * radiusMm;
        for (int cy = r0; cy <= r1; cy++)
            for (int cx = c0; cx <= c1; cx++)
            {
                if (_fire[cy * W + cx] == 0) continue;
                long dx = Ox + cx * CellMm + CellMm / 2 - x, dy = Oy + cy * CellMm + CellMm / 2 - y;
                if (dx * dx + dy * dy <= rr) return true;
            }
        return false;
    }

    /// <summary>How many cells of footing <paramref name="f"/> have their centre within <paramref name="radiusMm"/> of (x, y) (the stake cells of a refusal's arc).</summary>
    public int CountNear(Footing f, int x, int y, int radiusMm)
    {
        int c0 = Math.Max(0, (x - radiusMm - Ox) / CellMm - 1), c1 = Math.Min(W - 1, (x + radiusMm - Ox) / CellMm + 1);
        int r0 = Math.Max(0, (y - radiusMm - Oy) / CellMm - 1), r1 = Math.Min(H - 1, (y + radiusMm - Oy) / CellMm + 1);
        long rr = (long)radiusMm * radiusMm; int n = 0; byte want = (byte)((int)f + 1);
        for (int cy = r0; cy <= r1; cy++)
            for (int cx = c0; cx <= c1; cx++)
            {
                if (_f[cy * W + cx] != want) continue;
                long dx = Ox + cx * CellMm + CellMm / 2 - x, dy = Oy + cy * CellMm + CellMm / 2 - y;
                if (dx * dx + dy * dy <= rr) n++;
            }
        return n;
    }

    /// <summary>The first cell of footing <paramref name="f"/> along the heading from (x, y) within <paramref name="rangeMm"/> (sampled every metre): its centre. False when none.</summary>
    public bool FirstAlong(Footing f, int x, int y, int hx, int hy, int rangeMm, out int cx, out int cy)
    {
        byte want = (byte)((int)f + 1);
        for (int d = 1000; d <= rangeMm; d += 1000)
        {
            int px = x + (int)((long)hx * d >> 10), py = y + (int)((long)hy * d >> 10);
            int i = Index(px, py);
            if (i >= 0 && _f[i] == want)
            {
                int ix = (px - Ox) / CellMm, iy = (py - Oy) / CellMm;
                cx = Ox + ix * CellMm + CellMm / 2; cy = Oy + iy * CellMm + CellMm / 2;
                return true;
            }
        }
        cx = cy = 0; return false;
    }

    private static int FloorDiv(int a, int b) { int q = a / b; return a % b != 0 && (a < 0) != (b < 0) ? q - 1 : q; }

    /// <summary>Ground height (mm) at a point: bilinear between the centres of the four cells around it (clamped at the box's edge); 0 when no heights were given.</summary>
    public int HeightMm(int x, int y)
    {
        if (_h == null) return 0;
        int fx = x - Ox - CellMm / 2, fy = y - Oy - CellMm / 2;
        int cx = FloorDiv(fx, CellMm), cy = FloorDiv(fy, CellMm);
        int tx = fx - cx * CellMm, ty = fy - cy * CellMm;
        long a = Hc(cx, cy), b = Hc(cx + 1, cy), c = Hc(cx, cy + 1), d = Hc(cx + 1, cy + 1);
        long top = a * (CellMm - tx) + b * tx, bot = c * (CellMm - tx) + d * tx;
        return (int)((top * (CellMm - ty) + bot * ty) / ((long)CellMm * CellMm));
    }

    private int Hc(int ix, int iy) => _h![Math.Clamp(iy, 0, H - 1) * W + Math.Clamp(ix, 0, W - 1)];

    /// <summary>The rise per metre along the heading (permille, positive uphill): the ground one cell ahead less the ground one cell behind, over eight metres. 0 when no heights.</summary>
    public int SlopeAlongPm(int x, int y, int hx, int hy)
    {
        if (_h == null) return 0;
        int dx = (int)((long)hx * CellMm >> 10), dy = (int)((long)hy * CellMm >> 10);
        return (int)((long)(HeightMm(x + dx, y + dy) - HeightMm(x - dx, y - dy)) * 1000 / (2L * CellMm));
    }

    /// <summary>The build hash: the box, the footing bytes, the heights and the fire layer (FNV-1a over integers).</summary>
    public ulong Hash()
    {
        ulong h = 14695981039346656037UL;
        void M(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
        M(Ox); M(Oy); M(W); M(H);
        foreach (byte b in _f) M(b);
        if (_h != null) foreach (int v in _h) M(v);
        if (_fire != null) foreach (byte b in _fire) M(b | 0x100);
        return h;
    }

    // ------------------------------------------------------------------ weather on the ground

    /// <summary>
    /// What the weather did to the ground, as integers (4.8): the hours of rain over the mud threshold behind us, the air temperature (tenths of a degree), the snow lying (cm) and whether the ground is wet.
    /// </summary>
    public readonly record struct Ground(int WetHours, int TempTenthsC, int SnowCm, bool Wet)
    {
        /// <summary>The ground of a region cell at a game hour: the run of hours just past with rain at or over <paramref name="mudRainTenths"/> tenths of a millimetre an hour, and the sample now.</summary>
        public static Ground From(RegionWeather w, int cell, long gameHour, int mudRainTenths = 20, int lookBackHours = 24)
        {
            var now = w.AtLite(cell, gameHour);
            int run = 0;
            for (int k = 0; k < lookBackHours; k++)
            {
                var s = k == 0 ? now : w.AtLite(cell, gameHour - k);
                if (!s.PrecipIsSnow && (long)s.PrecipMmH.Raw * 10 >= (long)mudRainTenths * Fixed.OneRaw) run++; else break;
            }
            int temp = (int)((long)now.TempC.Raw * 10 / Fixed.OneRaw);
            int snow = (int)((long)now.SnowDepthCm.Raw / Fixed.OneRaw);
            bool wet = run > 0 || now.Raining || now.State is WeatherState.LightRain or WeatherState.HeavyRain or WeatherState.Thunderstorm;
            return new Ground(run, temp, snow, wet);
        }
    }

    /// <summary>A footing as the weather leaves it: snow lying over the depth covers open ground; frost on wet ground reads ice; field and rough turn to mud after the wet hours (stakes, ditches, rubble, woods, fords and water stay).</summary>
    public static Footing Weathered(Footing f, in Ground g, int mudAfterHours = 3, int iceBelowTenthsC = -20, int snowOverCm = 5)
    {
        if (f is not (Footing.Field or Footing.Rough or Footing.Road or Footing.Mud)) return f;
        if (g.SnowCm > snowOverCm) return Footing.Snow;
        if (g.TempTenthsC <= iceBelowTenthsC && (g.Wet || f == Footing.Mud)) return Footing.Ice;
        if (g.WetHours >= mudAfterHours && f is Footing.Field or Footing.Rough) return Footing.Mud;
        return f;
    }

    /// <summary>Applies <see cref="Weathered"/> to every cell with data and returns how many changed.</summary>
    public int ApplyWeather(in Ground g, int mudAfterHours = 3, int iceBelowTenthsC = -20, int snowOverCm = 5)
    {
        int n = 0;
        for (int i = 0; i < _f.Length; i++)
        {
            if (_f[i] == 0) continue;
            var f = (Footing)(_f[i] - 1);
            var w = Weathered(f, g, mudAfterHours, iceBelowTenthsC, snowOverCm);
            if (w != f) { _f[i] = (byte)((int)w + 1); n++; }
        }
        return n;
    }
}
