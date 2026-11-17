using Ravensguard.Content;

namespace Ravensguard.Sim.Building;

/// <summary>Piece lookup plus the kind rules shared by the parser, room detector, support check and placer.</summary>
public sealed class PieceCatalog
{
    private readonly Dictionary<string, BuildPieceDef> _pieces;
    public PieceCatalog(IEnumerable<BuildPieceDef> pieces) => _pieces = pieces.ToDictionary(p => p.Id, StringComparer.Ordinal);
    public IEnumerable<BuildPieceDef> All => _pieces.Values;
    public bool TryGet(string id, out BuildPieceDef piece) => _pieces.TryGetValue(id, out piece!);
    public BuildPieceDef Get(string id) => _pieces[id];
    public PieceKind? KindOf(string? id) => id != null && _pieces.TryGetValue(id, out var p) ? p.Kind : null;

    /// <summary>Tags of a piece including the derived "station:x".</summary>
    public static IEnumerable<string> TagsOf(BuildPieceDef p)
    {
        foreach (var t in p.Tags) yield return t;
        if (p.Station != null) yield return "station:" + p.Station;
    }

    /// <summary>Cells a room is made of (floor and the portal cells standing on floor).</summary>
    public static bool IsRoomKind(PieceKind k) => k is PieceKind.Floor or PieceKind.StairsUp or PieceKind.StairsDown or PieceKind.Ladder or PieceKind.Trapdoor;
    /// <summary>Cells that hold up the floor above them (plan §2.2a: wall, beam or post).</summary>
    public static bool IsSupport(PieceKind k) => k is PieceKind.Wall or PieceKind.Post;
    /// <summary>Cells that need support when they are on an upper level.</summary>
    public static bool IsFloorBearing(PieceKind k) => IsRoomKind(k);
    public static bool IsWalkable(PieceKind k) => k is PieceKind.Ground or PieceKind.Door || IsRoomKind(k);
    public static bool IsSolidFixture(PieceKind k) => k is PieceKind.Furniture or PieceKind.Workstation;
}

/// <summary>One storey of a blueprint: piece ids, optional fixture ids and the declared room letters.</summary>
public sealed class BuildingLevel
{
    public int Z { get; }
    public int Width { get; }
    public int Height { get; }
    public string?[] Structure { get; }
    public string?[] Fixture { get; }
    public char[] Letter { get; }

    public BuildingLevel(int z, int width, int height)
    {
        Z = z; Width = width; Height = height;
        Structure = new string?[width * height];
        Fixture = new string?[width * height];
        Letter = new char[width * height];
    }

    public int Idx(int x, int y) => y * Width + x;
    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public BuildingLevel Clone()
    {
        var c = new BuildingLevel(Z, Width, Height);
        Array.Copy(Structure, c.Structure, Structure.Length);
        Array.Copy(Fixture, c.Fixture, Fixture.Length);
        Array.Copy(Letter, c.Letter, Letter.Length);
        return c;
    }

    /// <summary>Rotated 90 degrees clockwise: (x, y) goes to (Height-1-y, x).</summary>
    public BuildingLevel Rotate90()
    {
        var r = new BuildingLevel(Z, Height, Width);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int d = r.Idx(Height - 1 - y, x), s = Idx(x, y);
                r.Structure[d] = Structure[s]; r.Fixture[d] = Fixture[s]; r.Letter[d] = Letter[s];
            }
        return r;
    }

    public BuildingLevel MirrorX()
    {
        var r = new BuildingLevel(Z, Width, Height);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int d = r.Idx(Width - 1 - x, y), s = Idx(x, y);
                r.Structure[d] = Structure[s]; r.Fixture[d] = Fixture[s]; r.Letter[d] = Letter[s];
            }
        return r;
    }
}

/// <summary>A blueprint parsed to a grid of piece ids per level (z from -5 to +3, plan §2.2a).</summary>
public sealed class BuildingGrid
{
    public string Id { get; }
    public int Width { get; }
    public int Height { get; }
    /// <summary>Declared room letters to purposes.</summary>
    public IReadOnlyDictionary<char, string> Rooms { get; }
    public SortedDictionary<int, BuildingLevel> Levels { get; }

    public BuildingGrid(string id, int width, int height, IReadOnlyDictionary<char, string> rooms, IEnumerable<BuildingLevel> levels)
    {
        Id = id; Width = width; Height = height; Rooms = rooms;
        Levels = new SortedDictionary<int, BuildingLevel>(levels.ToDictionary(l => l.Z));
    }

    public BuildingLevel? Level(int z) => Levels.TryGetValue(z, out var l) ? l : null;

    /// <summary>Structure piece id at a cell, or null when empty, outside the footprint or on a level the building lacks.</summary>
    public string? StructureAt(int x, int y, int z)
    {
        var l = Level(z);
        return l != null && l.InBounds(x, y) ? l.Structure[l.Idx(x, y)] : null;
    }

    public string? FixtureAt(int x, int y, int z)
    {
        var l = Level(z);
        return l != null && l.InBounds(x, y) ? l.Fixture[l.Idx(x, y)] : null;
    }

    public BuildingGrid Clone() => new(Id, Width, Height, Rooms, Levels.Values.Select(l => l.Clone()));
    public BuildingGrid Rotate90() => new(Id, Height, Width, Rooms, Levels.Values.Select(l => l.Rotate90()));
    public BuildingGrid MirrorX() => new(Id, Width, Height, Rooms, Levels.Values.Select(l => l.MirrorX()));

    /// <summary>Applies the mirror (if any) and then <paramref name="quarterTurns"/> clockwise rotations.</summary>
    public BuildingGrid Transform(int quarterTurns, bool mirror)
    {
        var g = mirror ? MirrorX() : this;
        for (int i = ((quarterTurns % 4) + 4) % 4; i > 0; i--) g = g.Rotate90();
        return g;
    }

    public bool ContentEquals(BuildingGrid o)
    {
        if (Width != o.Width || Height != o.Height || Levels.Count != o.Levels.Count) return false;
        foreach (var (z, l) in Levels)
        {
            if (!o.Levels.TryGetValue(z, out var m)) return false;
            if (!l.Structure.AsSpan().SequenceEqual(m.Structure) || !l.Fixture.AsSpan().SequenceEqual(m.Fixture) || !l.Letter.AsSpan().SequenceEqual(m.Letter)) return false;
        }
        return true;
    }
}
