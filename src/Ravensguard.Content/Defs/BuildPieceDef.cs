using System.Text.Json.Serialization;

namespace Ravensguard.Content;

/// <summary>What a building piece is for; the room detector, pathing and structural support key off this (plan §2.9, §2.2a).</summary>
[JsonConverter(typeof(SnakeEnumConverter<PieceKind>))]
public enum PieceKind
{
    /// <summary>Outdoor ground around a building (walkable, never part of a room).</summary>
    Ground,
    /// <summary>Indoor floor: the cells rooms are made of.</summary>
    Floor,
    Wall,
    Door,
    Window,
    StairsUp,
    StairsDown,
    /// <summary>Climbs to a <see cref="Trapdoor"/> on the level above.</summary>
    Ladder,
    /// <summary>A hatch in the floor; has a <see cref="Ladder"/> below it.</summary>
    Trapdoor,
    /// <summary>Solid, supports floors above, but does not seal a room by itself.</summary>
    Post,
    Furniture,
    Workstation,
}

/// <summary>
/// One placeable building piece: a wall of a material, a floor, a door, a stair, a piece of furniture or a workstation.
/// <c>tags</c> are what room types look for ("bed", "hearth", "table", "storage", "altar" ...); a workstation also carries
/// "station:&lt;station&gt;".
/// </summary>
[ContentType("build_piece")]
public sealed record BuildPieceDef : ContentDef
{
    public required string Name { get; init; }
    public required PieceKind Kind { get; init; }
    /// <summary>One ASCII character used by the text dump.</summary>
    public required string Glyph { get; init; }
    [ContentRef(typeof(MaterialDef))] public string? Material { get; init; }
    public List<string> Tags { get; init; } = [];
    /// <summary>Workstation id ("smithing", "weaving", "tanning", "carpentry"); adds the tag "station:&lt;id&gt;".</summary>
    public string? Station { get; init; }
    /// <summary>Beds this piece provides (bed 1, bunk 2).</summary>
    public int Beds { get; init; }
}
