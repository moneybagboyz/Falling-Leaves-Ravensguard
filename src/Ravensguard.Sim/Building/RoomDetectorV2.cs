using Ravensguard.Content;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Building;

// P5-A3: RoomDetector v2 (docs/specs/phase5/roomdetector-v2.md). Pure and deterministic: no RNG (stream 0x520 is reserved and never drawn),
// integer arithmetic, flood order by (z, y, x), ties by room type id (ordinal).

public enum ValueTier { Mean, Plain, Decent, Fine, Grand }

public enum DwellingKind { None, Cottage, Longhouse, Burgage, Manor, Tower }

/// <summary>A piece as the detector sees it: kind, tags, beds, station and its value in mites (reference price, spec 4.5).</summary>
public sealed class PieceInfo
{
    public required string Id { get; init; }
    public required PieceKind Kind { get; init; }
    public required string[] Tags { get; init; }
    public int Beds { get; init; }
    /// <summary>The craft_station id this piece counts as (null for none).</summary>
    public string? Station { get; init; }
    public long ValueMites { get; init; }
}

/// <summary>Room value rules (spec 4.5): reference prices, never market prices.</summary>
public static class RoomValue
{
    public const int PlainFrom = 30, DecentFrom = 100, FineFrom = 300, GrandFrom = 1000;

    public static ValueTier TierOf(int mitesPerM2) =>
        mitesPerM2 >= GrandFrom ? ValueTier.Grand : mitesPerM2 >= FineFrom ? ValueTier.Fine : mitesPerM2 >= DecentFrom ? ValueTier.Decent : mitesPerM2 >= PlainFrom ? ValueTier.Plain : ValueTier.Mean;

    /// <summary>Default piece mass in kg when the piece row gives none (one 1 m cell).</summary>
    public static int DefaultKg(PieceKind k) => k switch
    {
        PieceKind.Floor => 6,
        PieceKind.Wall => 8,
        PieceKind.Post => 5,
        PieceKind.Door => 6,
        PieceKind.Window => 3,
        PieceKind.StairsUp or PieceKind.StairsDown => 40,
        PieceKind.Ladder or PieceKind.Trapdoor => 10,
        PieceKind.Furniture => 40,
        PieceKind.Workstation => 80,
        PieceKind.Roof => 10,
        _ => 0,
    };
}

public enum CellLayer { Structure, Fixture, Purpose }

/// <summary>One change to one cell: the piece in a layer (null clears it) or the declared purpose of the floor cell.</summary>
public readonly record struct CellEdit(int X, int Y, int Z, CellLayer Layer, string? Value);

public enum StructureEventKind { Created, Joined, Split, Removed }

/// <summary>A structure id change the land rule follows (spec 4.2): on Joined the lower id survives; on Split the old id stays with the part holding the lowest cell and the other part is new.</summary>
public readonly record struct StructureEvent(StructureEventKind Kind, int Structure, int Other);

public sealed class RoomChange
{
    public List<StructureEvent> Structures { get; } = [];
    public int RoomsRemoved { get; internal set; }
    public int RoomsAdded { get; internal set; }
    /// <summary>Structures whose facts changed (housing, businesses and storage listen to these; spec 5).</summary>
    public SortedSet<int> Touched { get; } = [];
}

/// <summary>An enclosed (or leaking) floor region on one level, classified (v2: roofs, value, stations, storage, purpose gaps).</summary>
public sealed class RoomV2
{
    internal long[] CellKeys = [];
    private List<(int X, int Y)>? _cells;
    public int Z { get; internal set; }
    public int Level => Z;
    public int Area => CellKeys.Length;
    /// <summary>The lowest cell by (y, x): a stable identity for the room.</summary>
    public (int X, int Y) Origin => RoomWorld.Unpack(CellKeys[0]) is var (x, y, _) ? (x, y) : default;
    public IReadOnlyList<(int X, int Y)> Cells => _cells ??= CellKeys.Select(k => { var (x, y, _) = RoomWorld.Unpack(k); return (x, y); }).ToList();
    public bool Enclosed { get; internal set; }
    /// <summary>Enclosed and every cell roofed (spec 4.1).</summary>
    public bool Indoor { get; internal set; }
    public Dictionary<string, int> Tags { get; internal set; } = [];
    public int Beds { get; internal set; }
    public List<RoomDoor> Doors { get; internal set; } = [];
    public bool HasExteriorDoor => Doors.Any(d => d.Exterior);
    public List<RoomTypeDef> Matches { get; internal set; } = [];
    public RoomTypeDef? Primary => Matches.Count > 0 ? Matches[0] : null;
    public List<string> DeclaredPurposes { get; internal set; } = [];
    public bool PurposeMatches => DeclaredPurposes.Count == 1 && Primary != null && BuildingLibrary.PurposeMatches(DeclaredPurposes[0], Primary.Purpose);
    public int ValuePerM2 { get; internal set; }
    public ValueTier ValueTier => RoomValue.TierOf(ValuePerM2);
    /// <summary>Craft station id to the number of its pieces inside the room.</summary>
    public SortedDictionary<string, int> Stations { get; internal set; } = new(StringComparer.Ordinal);
    public long StorageL { get; internal set; }
    public string? StorageKind { get; internal set; }
    /// <summary>What a declared purpose still lacks (tags such as "bed", "roof"); empty when satisfied or undeclared (spec 4.6).</summary>
    public string[] PurposeGaps { get; internal set; } = [];
    /// <summary>Free floor cells that carry a declared purpose and no fixture (bed capacity of an unfurnished plan).</summary>
    public int FreeDeclaredCells { get; internal set; }
    public bool Contains(int x, int y) => Array.BinarySearch(CellKeys, RoomWorld.Key(x, y, Z)) >= 0;
}

/// <summary>What other systems read about one structure (spec 3.3).</summary>
public sealed class BuildingFacts
{
    public int Structure { get; init; }
    public bool IsDwelling { get; init; }
    public DwellingKind DwellingKind { get; init; }
    /// <summary>Beds in Indoor sleeping, solar, loft and guest rooms.</summary>
    public int Beds { get; init; }
    /// <summary>Placed beds plus the beds that fit the free declared floor of sleeping rooms (what the blueprint check counts).</summary>
    public int BedCapacity { get; init; }
    public int HearthRooms { get; init; }
    public required IReadOnlyList<(RoomV2 Room, IReadOnlyDictionary<string, int> Stations)> Workshops { get; init; }
    public required IReadOnlyDictionary<string, long> StorageL { get; init; }
    public required IReadOnlyList<RoomV2> Rooms { get; init; }
    public ValueTier BestValueTier { get; init; }
    public required IReadOnlyList<Cell3> Entrances { get; init; }
    public required IReadOnlyList<int> Levels { get; init; }
    public int FootprintW { get; init; }
    public int FootprintH { get; init; }
    public int FootprintArea { get; init; }
    public long Hash { get; init; }

    /// <summary>Station counts summed over the recognised workshop rooms (a business counts these, never a loose forge; spec 4.4).</summary>
    public SortedDictionary<string, int> WorkshopStations()
    {
        var d = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, s) in Workshops) foreach (var (k, v) in s) d[k] = d.GetValueOrDefault(k) + v;
        return d;
    }
}
