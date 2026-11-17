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
}
