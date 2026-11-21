using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Battle;

/// <summary>
/// The battle bubble's spatial grid (battle-bubble-and-formations 2.1, 4.2; P7 BB-A): struct arrays indexed by fighter id plus a uniform grid of 4 m cells, a counting sort of
/// fighter ids by cell rebuilt without allocating (the arrays only grow). It answers one question: which fighters could lie within a radius of a point, <b>in ascending id</b>, the
/// order every old full-list scan visited them in, so a loop that filters the candidates by its own exact test gives the same result, the same draws and the same hash as the scan (BB-1).
/// <para>
/// A query returns a <b>superset</b> of the fighters whose live position is within the radius (a caller's own distance test makes it exact). The grid holds positions as of the last
/// <see cref="Rebuild"/>; <see cref="Fighter.X"/> and <see cref="Fighter.Y"/> report every write to <see cref="NoteX"/> and <see cref="NoteY"/>, which keep <see cref="Drift"/>, the largest
/// distance any fighter has moved on either axis since the rebuild, and a query widens its box by that drift. So a query is exact at any moment (mid-<c>Separate</c>, after a
/// shove, after a knock-back), not only straight after a rebuild; the fight rebuilds after movement and separation and whenever the drift passes half a cell, which is a speed matter only.
/// Outliers (a fighter fled a kilometre off) are clamped into the border cells of a grid of at most 512 x 512 cells, which can only add candidates, never lose one.
/// </para>
/// Not thread-safe: the query scratch is shared, so a grid is used only by the thread that steps its fight. <see cref="FormationId"/> is reserved for BB-B and stays 0.
/// </summary>
public sealed class BattleGrid
{
    /// <summary>The cell size, mm (4 m).</summary>
    public const int CellMm = 4000;
    /// <summary>The widest grid, in cells a side (2 km); beyond it positions clamp into the border cells.</summary>
    public const int MaxDim = 512;
    /// <summary>How many queries may hold a result at once (a scan that calls another scan, three deep at most).</summary>
    public const int Depths = 4;

    public int N { get; private set; }
    /// <summary>Per fighter id, as of the last rebuild: position (mm), team, 1 when still in the fight, formation (BB-B).</summary>
    public int[] X = [], Y = [], Team = [], ActBits = [], FormationId = [];
    /// <summary>True until the first rebuild and after a fighter is added: the next query rebuilds first.</summary>
    public bool Dirty = true;
    /// <summary>The most any fighter has moved on either axis since the last rebuild (mm).</summary>
    public int Drift { get; private set; }
    /// <summary>The longest weapon reach any fighter has had in this fight (mm); only ever grows.</summary>
    public int MaxReachMm { get; private set; }
    /// <summary>Rebuilds so far (the test counter).</summary>
    public int Rebuilds { get; private set; }

    private int _ox, _oy, _dimX = 1, _dimY = 1;
    private int[] _cellStart = new int[2], _items = [];
    private readonly int[][] _buf = new int[Depths][];
    private int[] _ident = [];

    public void NoteReach(int reachMm) { if (reachMm > MaxReachMm) MaxReachMm = reachMm; }

    public void NoteX(int id, int x)
    {
        if (Dirty || (uint)id >= (uint)N) return;
        int d = x - X[id]; if (d < 0) d = -d;
        if (d > Drift) Drift = d;
    }

    public void NoteY(int id, int y)
    {
        if (Dirty || (uint)id >= (uint)N) return;
        int d = y - Y[id]; if (d < 0) d = -d;
        if (d > Drift) Drift = d;
    }

    /// <summary>True when a query now would have to rebuild first (never built, a fighter added, or drifted past half a cell).</summary>
    public bool Stale(int fighters) => Dirty || N != fighters || Drift > CellMm / 2;

    /// <summary>Reads every fighter's position and sorts the ids by cell (a counting sort: ascending id inside a cell). Allocates only when the fight has grown.</summary>
    public void Rebuild(List<Fighter> fighters)
    {
        int n = fighters.Count;
        if (X.Length < n)
        {
            int cap = Math.Max(n, X.Length * 2);
            X = new int[cap]; Y = new int[cap]; Team = new int[cap]; ActBits = new int[cap]; FormationId = new int[cap]; _items = new int[cap];
            for (int d = 0; d < Depths; d++) _buf[d] = new int[cap];
        }
        N = n;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < n; i++)
        {
            var f = fighters[i];
            int x = f.X, y = f.Y;
            X[i] = x; Y[i] = y; Team[i] = f.Team; ActBits[i] = f.Active ? 1 : 0;
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }
        if (n == 0) { minX = minY = maxX = maxY = 0; }
        (_ox, _dimX) = Axis(minX, maxX);
        (_oy, _dimY) = Axis(minY, maxY);
        int cells = _dimX * _dimY;
        if (_cellStart.Length < cells + 1) _cellStart = new int[Math.Max(cells + 1, _cellStart.Length * 2)];
        Array.Clear(_cellStart, 0, cells + 1);
        for (int i = 0; i < n; i++) _cellStart[CellOf(X[i], Y[i]) + 1]++;
        for (int c = 0; c < cells; c++) _cellStart[c + 1] += _cellStart[c];
        // place in ascending id: _cellStart[c] doubles as the write cursor, then is restored by the shift below
        for (int i = 0; i < n; i++) _items[_cellStart[CellOf(X[i], Y[i])]++] = i;
        for (int c = cells; c > 0; c--) _cellStart[c] = _cellStart[c - 1];
        _cellStart[0] = 0;
        Dirty = false; Drift = 0; Rebuilds++;
    }

    private static (int Origin, int Dim) Axis(int min, int max)
    {
        long span = (long)max - min;
        int dim = (int)(span / CellMm) + 1;
        if (dim <= MaxDim) return (min, dim);
        return ((int)(((long)min + max) / 2 - (long)MaxDim / 2 * CellMm), MaxDim);
    }

    private static int Cell(long v, int origin, int dim)
    {
        long d = v - origin;
        long c = d >= 0 ? d / CellMm : -((-d + CellMm - 1) / CellMm);
        return c < 0 ? 0 : c >= dim ? dim - 1 : (int)c;
    }

    private int CellOf(int x, int y) => Cell(y, _oy, _dimY) * _dimX + Cell(x, _ox, _dimX);

    /// <summary>
    /// The ids that could be within <paramref name="r"/> mm (Euclidean, plus the rounding of an integer length) of (x, y), ascending, in the scratch of <paramref name="depth"/>; the
    /// result stays valid until the next query at that depth. The caller must have a fresh grid (<see cref="Stale"/> false), or no id can be missed only by luck.
    /// </summary>
    public ReadOnlySpan<int> Near(int depth, int x, int y, long r)
    {
        long pad = r + Drift + 2;
        int cx0 = Cell(x - pad, _ox, _dimX), cx1 = Cell(x + pad, _ox, _dimX);
        int cy0 = Cell(y - pad, _oy, _dimY), cy1 = Cell(y + pad, _oy, _dimY);
        // a box over a third of the grid gains nothing from the cells (and would sort most of the fighters): every id, ascending, is a superset too
        if ((long)(cx1 - cx0 + 1) * (cy1 - cy0 + 1) * 3 >= (long)_dimX * _dimY) return Identity(N);
        var buf = _buf[depth];
        int k = 0;
        for (int cy = cy0; cy <= cy1; cy++)
        {
            int row = cy * _dimX;
            int a = _cellStart[row + cx0], b = _cellStart[row + cx1 + 1];
            if (b > a) { Array.Copy(_items, a, buf, k, b - a); k += b - a; }
        }
        var span = new Span<int>(buf, 0, k);
        if (cx1 > cx0 || cy1 > cy0) span.Sort();
        return span;
    }

    /// <summary>Every id 0 .. n-1 (what a query returns with the grid off: the old full scan, the oracle of BB-1).</summary>
    public ReadOnlySpan<int> Identity(int n)
    {
        if (_ident.Length < n) { int old = _ident.Length; Array.Resize(ref _ident, Math.Max(n, old * 2)); for (int i = old; i < _ident.Length; i++) _ident[i] = i; }
        return new ReadOnlySpan<int>(_ident, 0, n);
    }
}
