using Ravensguard.Content;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Building;

/// <summary>
/// The CI blueprint check (plan §2.9): ids resolve, rows are rectangular and equal across levels, every room is reachable from an
/// exterior door, every declared purpose can be satisfied and is recognised by <see cref="RoomDetector"/>, upper floors are supported,
/// stairs and ladders pair up between levels, and dwellings hold the bed capacity of plan §4A.2.
/// </summary>
public static class BlueprintValidator
{
    /// <summary>Plan §4A.2 housing: bed capacity per dwelling kind (inclusive).</summary>
    public static readonly IReadOnlyDictionary<string, (int Min, int Max)> HousingBeds =
        new Dictionary<string, (int, int)> { ["cottage"] = (4, 7), ["longhouse"] = (6, 10), ["burgage"] = (6, 12) };

    /// <summary>A bed needs about this many free floor cells (bed plus space to move).</summary>
    public const int CellsPerBed = 3;

    public static List<BlueprintIssue> ValidateAll(BuildingLibrary lib)
    {
        var issues = new List<BlueprintIssue>();
        foreach (var e in lib.Blueprints) issues.AddRange(Validate(e, lib));
        return issues;
    }

    public static List<BlueprintIssue> Validate(BlueprintEntry entry, BuildingLibrary lib)
    {
        var issues = new List<BlueprintIssue>();
        var def = entry.Def;
        void Err(string prop, string code, string msg) => issues.Add(new(entry.Where(prop), def.Id, code, msg));

        var grid = lib.Parse(entry, issues);
        if (grid == null) return issues;
        var pieces = lib.Pieces;
        var detector = lib.Detector;
        var rooms = detector.Detect(grid);

        // declared rooms: purposes exist, letters used
        foreach (var (letter, purpose) in grid.Rooms)
        {
            if (!lib.TypesFor(purpose).Any()) Err("rooms", "rooms.unknown_purpose", $"room '{letter}': no room_type delivers purpose '{purpose}'");
            if (!rooms.Any(r => r.Letters.Contains(letter))) Err("rooms", "rooms.letter_unused", $"room letter '{letter}' ({purpose}) is not on any floor cell");
        }
        foreach (var r in rooms.Where(r => r.DeclaredPurposes.Count > 1))
            Err("rooms", "room.mixed", $"room#{r.Index} z={r.Z} mixes purposes {string.Join(", ", r.DeclaredPurposes)}");

        // exterior door and reachability
        if (!def.OpenAir && !rooms.Any(r => r.HasExteriorDoor) && !HasExteriorDoor(grid, pieces, detector))
            Err("rows", "exterior.no_door", "no door leads outside");
        var reached = Reachable(grid, pieces, detector);
        foreach (var r in rooms.Where(_ => !def.OpenAir))
            if (!r.Cells.Any(c => reached.Contains(new Cell3(c.X, c.Y, r.Z))))
                Err("rows", "room.unreachable", $"room#{r.Index} z={r.Z} at ({r.Cells[0].X},{r.Cells[0].Y}) cannot be reached from an exterior door");

        CheckPortals(grid, pieces, Err);

        foreach (var c in StructuralSupport.Unsupported(grid, pieces))
            Err("levels", "support.missing", $"floor at ({c.X},{c.Y}) z={c.Z} has no wall or post within {StructuralSupport.Reach} tiles below it");

        CheckDeclaredRooms(grid, rooms, lib, Err);
        CheckHousing(def, grid, rooms, Err);
        return issues;
    }

    // ---- reachability (simple BFS: doors, stairs and ladder/trapdoor portals across levels) ----

    private static bool HasExteriorDoor(BuildingGrid g, PieceCatalog pieces, RoomDetector d) => ExteriorDoors(g, pieces, d).Any();

    private static IEnumerable<Cell3> ExteriorDoors(BuildingGrid g, PieceCatalog pieces, RoomDetector d)
    {
        foreach (var (z, lv) in g.Levels)
            for (int y = 0; y < lv.Height; y++)
                for (int x = 0; x < lv.Width; x++)
                    if (pieces.KindOf(lv.Structure[lv.Idx(x, y)]) == PieceKind.Door && d.IsExteriorDoor(lv, x, y)) yield return new Cell3(x, y, z);
    }

    public static HashSet<Cell3> Reachable(BuildingGrid g, PieceCatalog pieces, RoomDetector detector)
    {
        var seen = new HashSet<Cell3>();
        var q = new Queue<Cell3>();
        foreach (var s in ExteriorDoors(g, pieces, detector)) if (seen.Add(s)) q.Enqueue(s);
        (int dx, int dy)[] n4 = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        bool Walkable(int x, int y, int z)
        {
            var lv = g.Level(z);
            if (lv == null || !lv.InBounds(x, y)) return false;
            var k = pieces.KindOf(lv.Structure[lv.Idx(x, y)]);
            if (k == null || !PieceCatalog.IsWalkable(k.Value)) return false;
            return !(pieces.KindOf(lv.Fixture[lv.Idx(x, y)]) is { } f && PieceCatalog.IsSolidFixture(f));
        }
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var (dx, dy) in n4)
            {
                var nx = new Cell3(c.X + dx, c.Y + dy, c.Z);
                if (Walkable(nx.X, nx.Y, nx.Z) && seen.Add(nx)) q.Enqueue(nx);
            }
            var kind = pieces.KindOf(g.StructureAt(c.X, c.Y, c.Z));
            (PieceKind from, PieceKind to, int dz)[] links =
                [(PieceKind.StairsUp, PieceKind.StairsDown, 1), (PieceKind.StairsDown, PieceKind.StairsUp, -1), (PieceKind.Ladder, PieceKind.Trapdoor, 1), (PieceKind.Trapdoor, PieceKind.Ladder, -1)];
            foreach (var (from, to, dz) in links)
                if (kind == from && pieces.KindOf(g.StructureAt(c.X, c.Y, c.Z + dz)) == to)
                {
                    var o = new Cell3(c.X, c.Y, c.Z + dz);
                    if (seen.Add(o)) q.Enqueue(o);
                }
        }
        return seen;
    }

    // ---- portals ----

    private static void CheckPortals(BuildingGrid g, PieceCatalog pieces, Action<string, string, string> err)
    {
        foreach (var (z, lv) in g.Levels)
            for (int y = 0; y < lv.Height; y++)
                for (int x = 0; x < lv.Width; x++)
                {
                    var k = pieces.KindOf(lv.Structure[lv.Idx(x, y)]);
                    (PieceKind want, int dz, string what)? rule = k switch
                    {
                        PieceKind.StairsUp => (PieceKind.StairsDown, 1, "stairs up need stairs down on the level above"),
                        PieceKind.StairsDown => (PieceKind.StairsUp, -1, "stairs down need stairs up on the level below"),
                        PieceKind.Ladder => (PieceKind.Trapdoor, 1, "a ladder needs a trapdoor on the level above"),
                        PieceKind.Trapdoor => (PieceKind.Ladder, -1, "a trapdoor needs a ladder on the level below"),
                        _ => null,
                    };
                    if (rule is not { } r) continue;
                    if (pieces.KindOf(g.StructureAt(x, y, z + r.dz)) != r.want)
                        err("levels", "stairs.unmatched", $"{k} at ({x},{y}) z={z}: {r.what} at the same cell");
                }
    }

    // ---- declared rooms: furnish minimally, expect the detector to agree ----

    private static void CheckDeclaredRooms(BuildingGrid grid, List<DetectedRoom> rooms, BuildingLibrary lib, Action<string, string, string> err)
    {
        var pieces = lib.Pieces;
        var declared = rooms.Where(r => r.DeclaredPurposes.Count == 1).ToList();
        if (declared.Count == 0) return;
        var furnished = grid.Clone();
        var ok = new List<DetectedRoom>();
        foreach (var r in declared)
        {
            var purpose = r.DeclaredPurposes[0];
            var candidates = lib.TypesFor(purpose).OrderByDescending(t => t.Priority).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
            if (candidates.Count == 0) continue; // already reported
            if (!r.Enclosed && candidates.All(t => t.Enclosed))
            { err("rows", "room.not_enclosed", $"room#{r.Index} z={r.Z} ({purpose}) is not enclosed: it opens onto empty space or the outdoors"); continue; }
            string? failure = null;
            foreach (var t in candidates)
            {
                failure = Furnish(furnished, r, t, pieces);
                if (failure == null) break;
            }
            if (failure != null) err("rows", "room.unsatisfiable", $"room#{r.Index} z={r.Z} ({purpose}): {failure}");
            else ok.Add(r);
        }
        // re-detect on the furnished copy and compare, by position
        var after = lib.Detector.Detect(furnished);
        foreach (var r in ok)
        {
            var (x, y) = r.Cells[0];
            var a = after.First(o => o.Z == r.Z && o.Contains(x, y));
            if (!a.PurposeMatches)
                err("rows", "room.misclassified", $"room#{r.Index} z={r.Z} declared {r.DeclaredPurposes[0]} but detected as {a.Primary?.Id ?? "nothing"} (matches: {string.Join(", ", a.Matches.Select(m => m.Id))})");
        }
    }

    /// <summary>Adds the cheapest fixtures that satisfy <paramref name="type"/> to the room; returns why it cannot, or null.</summary>
    private static string? Furnish(BuildingGrid g, DetectedRoom room, RoomTypeDef type, PieceCatalog pieces)
    {
        if (room.Area < type.MinArea) return $"area {room.Area} is below {type.Id} minimum {type.MinArea}";
        if (type.MaxArea > 0 && room.Area > type.MaxArea) return $"area {room.Area} is above {type.Id} maximum {type.MaxArea}";
        var lv = g.Level(room.Z)!;
        var free = room.Cells.Where(c => pieces.KindOf(lv.Structure[lv.Idx(c.X, c.Y)]) == PieceKind.Floor && lv.Fixture[lv.Idx(c.X, c.Y)] == null).ToList();
        // keep walkways: prefer cells not next to a door, stairs, ladder or trapdoor
        bool NextToAccess(int x, int y)
        {
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var k = pieces.KindOf(g.StructureAt(x + dx, y + dy, room.Z));
                if (k is PieceKind.Door or PieceKind.StairsUp or PieceKind.StairsDown or PieceKind.Ladder or PieceKind.Trapdoor) return true;
            }
            return false;
        }
        free = free.OrderBy(c => NextToAccess(c.X, c.Y) ? 1 : 0).ThenBy(c => c.Y).ThenBy(c => c.X).ToList();
        foreach (var req in type.Requires)
        {
            int need = req.Min - room.Tags.GetValueOrDefault(req.Tag);
            if (need <= 0) continue;
            var piece = pieces.All.Where(p => p.Kind is PieceKind.Furniture or PieceKind.Workstation && PieceCatalog.TagsOf(p).Contains(req.Tag))
                .OrderBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
            if (piece == null) return $"no furniture or workstation provides tag '{req.Tag}' required by {type.Id}";
            for (int i = 0; i < need; i++)
            {
                if (free.Count == 0) return $"no free floor cell left for '{piece.Id}' (tag '{req.Tag}')";
                var (x, y) = free[0]; free.RemoveAt(0);
                lv.Fixture[lv.Idx(x, y)] = piece.Id;
            }
        }
        return null;
    }

    // ---- housing ----

    private static void CheckHousing(BlueprintDef def, BuildingGrid grid, List<DetectedRoom> rooms, Action<string, string, string> err)
    {
        if (def.Dwelling == null) return;
        if (!HousingBeds.TryGetValue(def.Dwelling, out var range)) { err("dwelling", "housing.unknown_kind", $"unknown dwelling kind '{def.Dwelling}'"); return; }
        int beds = SleepingCapacity(grid, rooms);
        if (beds < range.Min || beds > range.Max)
            err("dwelling", "housing.capacity", $"{def.Dwelling} holds {beds} beds, plan §4A.2 wants {range.Min}-{range.Max}");
    }

    /// <summary>Beds already placed plus the beds that fit in the free floor of every "sleeping" room.</summary>
    public static int SleepingCapacity(BuildingGrid grid, IEnumerable<DetectedRoom> rooms)
    {
        int total = 0;
        foreach (var r in rooms.Where(r => r.DeclaredPurposes.Count == 1 && r.DeclaredPurposes[0] == "sleeping"))
        {
            var lv = grid.Level(r.Z)!;
            int free = r.Cells.Count(c => lv.Fixture[lv.Idx(c.X, c.Y)] == null && lv.Letter[lv.Idx(c.X, c.Y)] != 0);
            total += r.Beds + free / CellsPerBed;
        }
        return total;
    }
}
