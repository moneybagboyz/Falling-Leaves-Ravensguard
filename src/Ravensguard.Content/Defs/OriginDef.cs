namespace Ravensguard.Content;

/// <summary>An item the origin starts with: the id of a weapon_shape or armour_shape and how many.</summary>
public sealed record KitItemDef
{
    public required string Item { get; init; }
    public int Count { get; init; } = 1;
}

/// <summary>
/// A way to begin (plan §3 item 1, §5 Phase 1): a background card on the Quick Start screen. Skill and attribute entries are deltas
/// on top of the rolled commoner (skill ids are the snake_case names of the sim's Skill enum; attributes are strength, toughness,
/// dexterity, perception, wits). <c>start_location</c> is a kind the embark step maps to a place: village, town, road, manor, monastery or wilds.
/// </summary>
[ContentType("origin")]
public sealed record OriginDef : ContentDef
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    /// <summary>Card order on the Quick Start screen.</summary>
    public int Order { get; init; }
    public Dictionary<string, int> Skills { get; init; } = [];
    public Dictionary<string, int> Attributes { get; init; } = [];
    /// <summary>Weapon and armour shape ids.</summary>
    public List<KitItemDef> Kit { get; init; } = [];
    /// <summary>Starting coin in pence.</summary>
    public int Coin { get; init; }
    public required string StartLocation { get; init; }
    [ContentRef(typeof(AmbitionDef))] public required string Ambition { get; init; }
    /// <summary>The kind of NPC who gives the first small contract (plan §3: a caravan master, a reeve ...).</summary>
    public required string Mentor { get; init; }
}
