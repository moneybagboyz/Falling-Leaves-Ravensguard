namespace Ravensguard.Sim.World;

public readonly record struct Cell3(int X, int Y, int Z);

public enum PortalKind : byte { Stairs, Ladder, Trapdoor, Shaft, CaveMouth }

/// <summary>A two-way link between two cells on (usually) different levels. Enabled=false: ladder pulled up, trapdoor barred.</summary>
public sealed class Portal
{
    public int Id { get; init; }
    public PortalKind Kind { get; init; }
    public Cell3 A { get; init; }
    public Cell3 B { get; init; }
    public int Cost { get; init; }
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Spike 5: grid with z-levels, cell passability and portals. Costs are ints: 10 per orthogonal step, 14 diagonal.
/// Per-chunk revision counters give incremental invalidation (a change touches only its chunk-level and its portals).
/// </summary>
public sealed class LevelGrid
{
    public const int Straight = 10, Diagonal = 14;
    public int Width { get; }
    public int Height { get; }
    public int MinZ { get; }
    public int MaxZ { get; }
    public int Levels => MaxZ - MinZ + 1;
    public int ChunksX { get; }
    public int ChunksY { get; }

    private readonly byte[] _open; // 1 = passable floor cell
    private readonly List<Portal> _portals = new();
    private readonly Dictionary<int, List<int>> _portalsAt = new(); // cell index -> portal ids
    private readonly int[] _chunkRev;

    public IReadOnlyList<Portal> Portals => _portals;
    public int Revision { get; private set; }

    public LevelGrid(int width, int height, int minZ, int maxZ)
    {
        Width = width; Height = height; MinZ = minZ; MaxZ = maxZ;
        ChunksX = (width + ScaleConstants.ChunkCells - 1) / ScaleConstants.ChunkCells;
        ChunksY = (height + ScaleConstants.ChunkCells - 1) / ScaleConstants.ChunkCells;
        _open = new byte[width * height * Levels];
        _chunkRev = new int[ChunksX * ChunksY * Levels];
    }

    public int CellCount => _open.Length;
    public bool InBounds(int x, int y, int z) => (uint)x < (uint)Width && (uint)y < (uint)Height && z >= MinZ && z <= MaxZ;
    public int Index(int x, int y, int z) => ((z - MinZ) * Height + y) * Width + x;
    public Cell3 FromIndex(int i) { int x = i % Width; int r = i / Width; return new Cell3(x, r % Height, r / Height + MinZ); }
    public bool IsOpen(int x, int y, int z) => InBounds(x, y, z) && _open[Index(x, y, z)] != 0;
    public bool IsOpen(int index) => _open[index] != 0;

    public int ChunkLevelIndex(int x, int y, int z)
        => ((z - MinZ) * ChunksY + y / ScaleConstants.ChunkCells) * ChunksX + x / ScaleConstants.ChunkCells;
    public int ChunkLevelOf(Cell3 c) => ChunkLevelIndex(c.X, c.Y, c.Z);
    public int ChunkLevelCount => _chunkRev.Length;
    public int ChunkRevision(int chunkLevel) => _chunkRev[chunkLevel];

    private void Require(int x, int y, int z)
    {
        if (!InBounds(x, y, z))
            throw new ArgumentOutOfRangeException(null, $"Cell ({x},{y},{z}) is outside the grid ({Width}x{Height}, z {MinZ}..{MaxZ}).");
    }

    public void SetOpen(int x, int y, int z, bool open)
    {
        Require(x, y, z);
        int i = Index(x, y, z);
        byte v = open ? (byte)1 : (byte)0;
        if (_open[i] == v) return;
        _open[i] = v;
        Touch(x, y, z);
    }

    public void FillRect(int x0, int y0, int x1, int y1, int z, bool open)
    {
        if (x0 > x1 || y0 > y1) return;
        Require(x0, y0, z); Require(x1, y1, z); // validate the whole rect up front: nothing changes on failure
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) SetOpen(x, y, z, open);
    }

    private void Touch(int x, int y, int z)
    {
        int cl = ChunkLevelIndex(x, y, z);
        _chunkRev[cl]++; Revision++;
    }

    /// <summary>Adds a two-way portal; both endpoints become open. Cost is clamped up to the octile distance to keep A* admissible.</summary>
    public Portal AddPortal(PortalKind kind, Cell3 a, Cell3 b, int cost)
    {
        Require(a.X, a.Y, a.Z); Require(b.X, b.Y, b.Z);
        SetOpen(a.X, a.Y, a.Z, true); SetOpen(b.X, b.Y, b.Z, true);
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        int octile = Straight * Math.Max(dx, dy) + (Diagonal - Straight) * Math.Min(dx, dy);
        var p = new Portal { Id = _portals.Count, Kind = kind, A = a, B = b, Cost = Math.Max(cost, octile) };
        _portals.Add(p);
        Touch(a.X, a.Y, a.Z); Touch(b.X, b.Y, b.Z); // new edge: re-zone only its two chunk-levels
        foreach (var c in new[] { a, b })
        {
            int idx = Index(c.X, c.Y, c.Z);
            if (!_portalsAt.TryGetValue(idx, out var l)) _portalsAt[idx] = l = new List<int>(2);
            l.Add(p.Id);
        }
        return p;
    }

    public IReadOnlyList<int> PortalsAt(int cellIndex)
        => _portalsAt.TryGetValue(cellIndex, out var l) ? l : (IReadOnlyList<int>)Array.Empty<int>();

    /// <summary>Pulls up a ladder / bars a trapdoor. Invalidates only that portal's two chunk-levels.</summary>
    public void SetPortalEnabled(int portalId, bool enabled)
    {
        var p = _portals[portalId];
        if (p.Enabled == enabled) return;
        p.Enabled = enabled;
        Touch(p.A.X, p.A.Y, p.A.Z); Touch(p.B.X, p.B.Y, p.B.Z);
    }

    /// <summary>Floor collapse: closes the rect on level z and disables every portal with an endpoint inside it. Returns affected portal ids.</summary>
    public List<int> CollapseFloor(int x0, int y0, int x1, int y1, int z)
    {
        Require(x0, y0, z); Require(x1, y1, z);
        FillRect(x0, y0, x1, y1, z, false);
        var hit = new List<int>();
        foreach (var p in _portals)
        {
            if (!p.Enabled) continue;
            if (InRect(p.A, x0, y0, x1, y1, z) || InRect(p.B, x0, y0, x1, y1, z)) { hit.Add(p.Id); SetPortalEnabled(p.Id, false); }
        }
        return hit;
    }

    private static bool InRect(Cell3 c, int x0, int y0, int x1, int y1, int z) => c.Z == z && c.X >= x0 && c.X <= x1 && c.Y >= y0 && c.Y <= y1;
}
