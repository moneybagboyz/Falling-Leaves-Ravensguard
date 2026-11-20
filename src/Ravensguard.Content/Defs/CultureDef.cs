using System.Text.Json.Serialization;

namespace Ravensguard.Content;

/// <summary>How a dead person's property and titles pass on (plan §4A.2 inheritance, per culture).</summary>
[JsonConverter(typeof(SnakeEnumConverter<InheritanceRule>))]
public enum InheritanceRule { Primogeniture, Partible, Elective }

/// <summary>Customary minimum ages to marry, in years.</summary>
public sealed record MarriageAgeDef
{
    public required int Male { get; init; }
    public required int Female { get; init; }
}

/// <summary>A yearly feast: month 1..12 and day 1..5 (the calendar has twelve five-day months, plan §3c).</summary>
public sealed record FeastDayDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required int Month { get; init; }
    public required int Day { get; init; }
}

/// <summary>
/// A culture pack (plan §2.9, §5 Phase 1): which name pool its people use, how it builds at each wealth level,
/// its law and customs, and its calendar words. <c>building_styles</c> maps "poor" / "middling" / "rich" to a
/// <see cref="CultureStyleDef"/> id; <c>crimes</c> maps a crime id to a punishment table key.
/// </summary>
[ContentType("culture")]
public sealed record CultureDef : ContentDef
{
    public required string Name { get; init; }
    [ContentRef(typeof(NamePoolDef))] public required string NamePool { get; init; }
    /// <summary>wealth ("poor", "middling", "rich") -> culture_style id.</summary>
    public required Dictionary<string, string> BuildingStyles { get; init; }
    /// <summary>Colour words for roofs and walls, used by the renderer to tint buildings.</summary>
    public List<string> RoofPalette { get; init; } = [];
    public List<string> WallPalette { get; init; } = [];

    public required InheritanceRule Inheritance { get; init; }
    public required MarriageAgeDef MarriageAge { get; init; }
    /// <summary>Share of the harvest owed to the church, in percent.</summary>
    public required double TithePercent { get; init; }
    /// <summary>Pence per hearth per year.</summary>
    public required int HearthTax { get; init; }
    /// <summary>crime id -> punishment table key.</summary>
    public required Dictionary<string, string> Crimes { get; init; }
    public required List<FeastDayDef> FeastDays { get; init; }
    /// <summary>Twelve month names, in calendar order; each month is five days.</summary>
    public required List<string> Months { get; init; }
    /// <summary>P6-D1 (slavery 3): the culture's law of bondage: <c>none</c> (no slaves; owning or trading is the crime slaving), <c>debt</c> (bond for debt or a sentence, for a term), <c>war_captives</c> (thralls taken in war or raids; children free) or <c>chattel</c> (bought and sold; children follow the mother).</summary>
    public string Slavery { get; init; } = "none";
}
