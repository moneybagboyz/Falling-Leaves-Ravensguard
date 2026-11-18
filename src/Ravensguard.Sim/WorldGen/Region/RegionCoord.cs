namespace Ravensguard.Sim.WorldGen.Region;

/// <summary>
/// A square of the 16 x 16 region grid (plan 1B, seamless-regions spec 2.1). A region is 16 x 16 continental cells, 32 km a side; the continent has 256 of them.
/// Ids run row by row (<c>Y * 16 + X</c>). Continent metres: region origin is (X * 32,000, Y * 32,000).
/// </summary>
public readonly record struct RegionCoord(int X, int Y) : IComparable<RegionCoord>
{
    public const int Grid = 16;
    public const int CellsPerSide = 16;
    public const int Count = Grid * Grid;
    public const int RegionM = RegionSettings.RegionM;

    public int Id => Y * Grid + X;
    public bool IsValid => (uint)X < Grid && (uint)Y < Grid;
    /// <summary>Continental cell of the north-west corner.</summary>
    public int CellX => X * CellsPerSide;
    public int CellY => Y * CellsPerSide;
    /// <summary>Continent metres of the north-west corner.</summary>
    public int OriginXM => X * RegionM;
    public int OriginYM => Y * RegionM;

    public static RegionCoord FromId(int id) => new(id % Grid, id / Grid);
    /// <summary>The region holding a continental cell.</summary>
    public static RegionCoord OfCell(int cellX, int cellY) => new(FloorDiv(cellX, CellsPerSide), FloorDiv(cellY, CellsPerSide));
    /// <summary>The region holding a continental cell index (<c>y * 256 + x</c>).</summary>
    public static RegionCoord OfCell(int cell) => OfCell(cell % Continent.Size, cell / Continent.Size);
    /// <summary>The region holding a continent metre position.</summary>
    public static RegionCoord OfMetres(long xm, long ym) => new((int)FloorDiv(xm, RegionM), (int)FloorDiv(ym, RegionM));
    /// <summary>The grid square a window origin cell snaps down to (decision O1).</summary>
    public static RegionCoord Snap(int cellX, int cellY) => OfCell(Math.Clamp(cellX, 0, Continent.Size - 1), Math.Clamp(cellY, 0, Continent.Size - 1));

    public RegionCoord Offset(int dx, int dy) => new(X + dx, Y + dy);

    /// <summary>The up to 8 neighbours inside the grid, in (dy, dx) order.</summary>
    public IEnumerable<RegionCoord> Neighbours()
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = Offset(dx, dy);
                if (n.IsValid) yield return n;
            }
    }

    public int Chebyshev(RegionCoord o) => Math.Max(Math.Abs(X - o.X), Math.Abs(Y - o.Y));
    public int CompareTo(RegionCoord o) => Id.CompareTo(o.Id);
    public override string ToString() => $"R({X},{Y})";

    private static int FloorDiv(int a, int b) { int q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }
    private static long FloorDiv(long a, long b) { long q = a / b; return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q; }
}
