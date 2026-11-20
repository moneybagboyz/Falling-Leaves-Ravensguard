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
    // P5-A3: appended (roofing, RoomDetector v2)
    /// <summary>A roof covering the cell below it: makes the room under it Indoor.</summary>
    Roof,
    // P5-B: appended (structural support, build-mode-and-construction 4.5)
    /// <summary>A timber beam at an upper level: carried by supports at both ends, it holds up the floor around it (the 6 m span rule).</summary>
    Beam,
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
    /// <summary>P5-A3: mass in kg for the room-value formula; 0 = the default for the piece kind (RoomDetector v2).</summary>
    public int Kg { get; init; }
    /// <summary>P5-B: what it takes to build one cell of this piece by hand (build-mode-and-construction 3.1); every base piece has one (BC-1).</summary>
    public PieceBuildDef? Build { get; init; }
}

/// <summary>
/// P5-B (build-mode-and-construction 3.1): the cost of one piece, one 1 m cell one storey high. <c>worker_hours</c> are at skill 50 (production's <c>time_mult = 1300 - 6 x skill</c>
/// permille applies; <c>skill: "none"</c> reads 50 for everyone); <c>kg</c> per piece of each material good; <c>hp</c> the piece's hit points; <c>carries_storeys</c> how many storeys
/// above this one it can bear; <c>salvage_pm</c> the share of the materials a deconstruction or a collapse returns as goods (the rest is rubble).
/// </summary>
public sealed record PieceBuildDef
{
    /// <summary>"none", "carpentry" or "masonry" (snake-case skill id).</summary>
    public string Skill { get; init; } = "none";
    public double WorkerHours { get; init; }
    public List<PieceToolDef> Tools { get; init; } = [];
    public List<PieceMaterialDef> Materials { get; init; } = [];
    public int Hp { get; init; } = 100;
    public int CarriesStoreys { get; init; }
    public int SalvagePm { get; init; }
    /// <summary>Built in the open (weather and light stop it); false for work done under a roof by its nature.</summary>
    public bool OutdoorWork { get; init; } = true;
    /// <summary>Daub, thatch and lime mortar do not set in rain over 2 mm an hour or below 0 C.</summary>
    public bool RainStops { get; init; }
    /// <summary>The least skill a worker needs (default 0); the skill gain's difficulty is this plus 20.</summary>
    public int MinSkill { get; init; }
    /// <summary>"normal" or "heavy" (stone and roofing: more fatigue and food).</summary>
    public string Effort { get; init; } = "normal";
}

/// <summary>A tool quality the work needs (a craft_tool quality: cutting, chopping, sawing, hammering, chiselling, digging) and its level.</summary>
public sealed record PieceToolDef
{
    public required string Quality { get; init; }
    public int Level { get; init; } = 1;
}

/// <summary>A material good and its mass per piece in kg (grams are exact to the thousandth).</summary>
public sealed record PieceMaterialDef
{
    public required string Good { get; init; }
    public double Kg { get; init; }
}
