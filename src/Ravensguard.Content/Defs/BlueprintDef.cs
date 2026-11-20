using System.Text.Json.Serialization;

namespace Ravensguard.Content;

[JsonConverter(typeof(SnakeEnumConverter<BlueprintKind>))]
public enum BlueprintKind { Building, RoomTemplate }

[JsonConverter(typeof(SnakeEnumConverter<Wealth>))]
public enum Wealth { Poor, Middling, Rich }

/// <summary>An extra storey: z = +1..+3 above ground, -1..-5 below (plan §2.2a).</summary>
public sealed record BlueprintLevelDef
{
    public required int Z { get; init; }
    public required List<string> Rows { get; init; }
    /// <summary>Optional overlay of furniture/workstations, same size as the rows; "_" = nothing.</summary>
    public List<string>? Fixtures { get; init; }
}

/// <summary>
/// A building or room template as text (plan §2.9). <c>rows</c> is the ground level (z = 0); every character is a key of
/// <c>legend</c> (piece id, or <c>$wall</c> / <c>$floor</c> resolved by the culture style and wealth) or "_" for empty.
/// <c>rooms</c> maps floor letters to room purposes; contents beyond fixed fixtures come from households later.
/// </summary>
[ContentType("blueprint")]
public sealed record BlueprintDef : ContentDef
{
    public required string Name { get; init; }
    public BlueprintKind Kind { get; init; } = BlueprintKind.Building;
    public Wealth Wealth { get; init; } = Wealth.Poor;
    [ContentRef(typeof(CultureStyleDef))] public string? Style { get; init; }
    /// <summary>"cottage", "longhouse" or "burgage": the validator checks bed capacity against plan §4A.2.</summary>
    public string? Dwelling { get; init; }
    /// <summary>P5-C: the culture ids whose plan library lists this plan (build-mode-and-construction 4.1); empty or "any" = every culture. Player blueprints write ["any"].</summary>
    public List<string> Cultures { get; init; } = [];
    public required Dictionary<string, string> Legend { get; init; }
    /// <summary>Floor letter -> purpose ("sleeping", "hearth", "work:smithing", "storage", "gathering", "worship", "livestock", "watch"). "work:*" accepts any workshop.</summary>
    public Dictionary<string, string> Rooms { get; init; } = [];
    public required List<string> Rows { get; init; }
    public List<string>? Fixtures { get; init; }
    public List<BlueprintLevelDef> Levels { get; init; } = [];
}
