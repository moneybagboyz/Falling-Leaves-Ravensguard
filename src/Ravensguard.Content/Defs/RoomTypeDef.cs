namespace Ravensguard.Content;

/// <summary>A room needs at least <see cref="Min"/> of the pieces carrying <see cref="Tag"/> inside it.</summary>
public sealed record RoomRequirement
{
    public required string Tag { get; init; }
    public int Min { get; init; } = 1;
}

/// <summary>
/// A recognised kind of room (plan §2.9). <c>purpose</c> is what blueprints declare ("sleeping", "work:smithing", "hearth" ...);
/// a room matches this type when it is enclosed (unless <c>enclosed</c> is false), big enough and holds every required tag.
/// When several types match, the highest <c>priority</c> wins.
/// </summary>
[ContentType("room_type")]
public sealed record RoomTypeDef : ContentDef
{
    public required string Name { get; init; }
    public required string Purpose { get; init; }
    public int Priority { get; init; }
    public bool Enclosed { get; init; } = true;
    public int MinArea { get; init; }
    /// <summary>0 = no upper limit.</summary>
    public int MaxArea { get; init; }
    public List<RoomRequirement> Requires { get; init; } = [];
    // P5-A3 (RoomDetector v2): append-only; the defaults keep v1 behaviour.
    /// <summary>null = must be Indoor when the type is enclosed; true = must be Indoor; false = must be enclosed but unroofed (a yard).</summary>
    public bool? Roofed { get; init; }
    /// <summary>Room value floor in mites per m2 (0 = none).</summary>
    public int MinValue { get; init; }
    /// <summary>"general", "grain", "hay", "cellar" or "cold": the room holds goods (1,000 L per m2). Null = none.</summary>
    public string? StorageKind { get; init; }
    /// <summary>The room counts toward a dwelling (sleeping, hearth, solar, hall).</summary>
    public bool DwellingPart { get; init; }
    /// <summary>The lowest level the room may stand on (null = any). A loft is above the ground floor (min_z 1), so a ground-floor sleeping room with a ladder up stays a bedroom.</summary>
    public int? MinZ { get; init; }

    // P7-LB2 (large-buildings 4.4): append-only geometry rules for the large buildings; every default leaves the rule off, so no earlier type changes.
    /// <summary>The longer side of the room's bounding box must be at least this many metres (0 = none).</summary>
    public int MinLength { get; init; }
    /// <summary>The shorter side of the bounding box at least / at most this (0 = none).</summary>
    public int MinWidth { get; init; }
    public int MaxWidth { get; init; }
    /// <summary>One window or lamp (a piece tagged <c>lamp</c>) on the room's edge or inside it for every this many m2, rounded down, at least 1 (0 = none).</summary>
    public int LightPerM2 { get; init; }
    /// <summary>A door of the room must carry this tag (a lockable door); null = none.</summary>
    public string? DoorTag { get; init; }
    /// <summary>At most this many doors on the room's edge (0 = no limit).</summary>
    public int MaxDoors { get; init; }
    /// <summary>Some craft station (<c>station:x</c> tag) must appear at least this many times in the room (0 = none).</summary>
    public int MinStationCount { get; init; }
    /// <summary>Litres of storage a m2 of the room holds (0 = the default 1,000; a warehouse stacks to 1,500).</summary>
    public int StorageLitresPerM2 { get; init; }
    /// <summary>One of the large buildings' types: when a declared purpose can also be met by a small room type, the purpose-gap report names the small type's gaps (a bare storeroom lacks a "storage" piece, not warehouse bays).</summary>
    public bool Large { get; init; }
}
