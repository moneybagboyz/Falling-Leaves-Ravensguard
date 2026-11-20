using Ravensguard.Content;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Building;

public sealed record RoomDoor(Cell3 Cell, bool Exterior);

/// <summary>An enclosed (or leaking) region of floor on one level, classified against the room types.</summary>
public sealed class DetectedRoom
{
    public int Index { get; init; }
    public int Z { get; init; }
    public required List<(int X, int Y)> Cells { get; init; }
    public int Area => Cells.Count;
    /// <summary>False when any room cell touches empty space, outdoor ground or the edge of the footprint.</summary>
    public bool Enclosed { get; init; }
    public required Dictionary<string, int> Tags { get; init; }
    public int Beds { get; init; }
    public required List<RoomDoor> Doors { get; init; }
    public bool HasExteriorDoor => Doors.Any(d => d.Exterior);
    /// <summary>Every room type whose rules the room satisfies, best first.</summary>
    public required List<RoomTypeDef> Matches { get; init; }
    public RoomTypeDef? Primary => Matches.Count > 0 ? Matches[0] : null;
    /// <summary>Declared room letters found in the room.</summary>
    public required HashSet<char> Letters { get; init; }
    /// <summary>Distinct purposes declared by those letters.</summary>
    public required List<string> DeclaredPurposes { get; init; }
    /// <summary>True when the room declares a purpose and the primary type delivers it.</summary>
    public bool PurposeMatches => DeclaredPurposes.Count == 1 && Primary != null && BuildingLibrary.PurposeMatches(DeclaredPurposes[0], Primary.Purpose);
    public bool Contains(int x, int y) => Cells.Contains((x, y));
}

/// <summary>
/// Plan §2.9: flood-fills floor regions per level, bounded by walls, doors, windows (posts are solid but do not seal),
/// and classifies each by the furniture inside against the room-type rules.
/// </summary>
public sealed class RoomDetector(PieceCatalog pieces, IReadOnlyList<RoomTypeDef> roomTypes)
{
    private static readonly (int dx, int dy)[] N4 = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    public List<DetectedRoom> Detect(BuildingGrid g)
    {
        var rooms = new List<DetectedRoom>();
        foreach (var (z, lv) in g.Levels)
        {
            var seen = new bool[lv.Width * lv.Height];
            for (int y0 = 0; y0 < lv.Height; y0++)
                for (int x0 = 0; x0 < lv.Width; x0++)
                {
                    int i0 = lv.Idx(x0, y0);
                    if (seen[i0] || !IsRoomCell(lv.Structure[i0])) continue;
                    rooms.Add(Flood(g, lv, x0, y0, seen, rooms.Count));
                }
        }
        return rooms;
    }

    private bool IsRoomCell(string? id) => pieces.KindOf(id) is { } k && PieceCatalog.IsRoomKind(k);

    private DetectedRoom Flood(BuildingGrid g, BuildingLevel lv, int sx, int sy, bool[] seen, int index)
    {
        var cells = new List<(int, int)>();
        var q = new Queue<(int X, int Y)>();
        q.Enqueue((sx, sy)); seen[lv.Idx(sx, sy)] = true;
        bool enclosed = true;
        var tags = new Dictionary<string, int>();
        int beds = 0;
        var letters = new HashSet<char>();
        var doors = new Dictionary<(int, int), RoomDoor>();
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            cells.Add((x, y));
            int i = lv.Idx(x, y);
            if (lv.Letter[i] != 0) letters.Add(lv.Letter[i]);
            Count(lv.Structure[i], tags, ref beds);
            Count(lv.Fixture[i], tags, ref beds);
            foreach (var (dx, dy) in N4)
            {
                int nx = x + dx, ny = y + dy;
                if (!lv.InBounds(nx, ny)) { enclosed = false; continue; }
                int ni = lv.Idx(nx, ny);
                var id = lv.Structure[ni];
                var kind = pieces.KindOf(id);
                if (kind == null || kind == PieceKind.Ground) { enclosed = false; continue; }
                if (kind == PieceKind.Door && !doors.ContainsKey((nx, ny)))
                    doors[(nx, ny)] = new RoomDoor(new Cell3(nx, ny, lv.Z), IsExteriorDoor(lv, nx, ny));
                if (PieceCatalog.IsRoomKind(kind.Value) && !seen[ni]) { seen[ni] = true; q.Enqueue((nx, ny)); }
            }
        }
        var matches = roomTypes.Where(t => Satisfies(t, cells.Count, enclosed, tags, lv.Z))
            .OrderByDescending(t => t.Priority).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
        var declared = letters.Select(c => g.Rooms[c]).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        return new DetectedRoom
        {
            Index = index, Z = lv.Z, Cells = cells, Enclosed = enclosed, Tags = tags, Beds = beds,
            Doors = doors.Values.OrderBy(d => d.Cell.Y).ThenBy(d => d.Cell.X).ToList(),
            Matches = matches, Letters = letters, DeclaredPurposes = declared,
        };
    }

    private void Count(string? id, Dictionary<string, int> tags, ref int beds)
    {
        if (id == null || !pieces.TryGet(id, out var p)) return;
        beds += p.Beds;
        foreach (var t in PieceCatalog.TagsOf(p)) tags[t] = tags.GetValueOrDefault(t) + 1;
    }

    public static bool Satisfies(RoomTypeDef t, int area, bool enclosed, IReadOnlyDictionary<string, int> tags, int z = 0)
    {
        if (t.Enclosed && !enclosed) return false;
        if (t.MinZ is int mz && z < mz) return false;
        if (t.Roofed == false) return false; // P5-A3: unroofed (yard) types are v2's; v1 sees finished blueprints
        if (area < t.MinArea || (t.MaxArea > 0 && area > t.MaxArea)) return false;
        foreach (var r in t.Requires)
            if (tags.GetValueOrDefault(r.Tag) < r.Min) return false;
        return true;
    }

    /// <summary>A door that opens onto outdoor ground, empty space or (on the ground level) the edge of the footprint.</summary>
    public bool IsExteriorDoor(BuildingLevel lv, int x, int y)
    {
        foreach (var (dx, dy) in N4)
        {
            int nx = x + dx, ny = y + dy;
            if (!lv.InBounds(nx, ny)) { if (lv.Z == 0) return true; continue; }
            var k = pieces.KindOf(lv.Structure[lv.Idx(nx, ny)]);
            if (k == PieceKind.Ground || (k == null && lv.Z == 0)) return true;
        }
        return false;
    }
}
