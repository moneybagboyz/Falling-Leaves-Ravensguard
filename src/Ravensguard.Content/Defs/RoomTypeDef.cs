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
}
