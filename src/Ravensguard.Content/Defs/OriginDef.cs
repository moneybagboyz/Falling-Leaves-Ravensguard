namespace Ravensguard.Content;

/// <summary>An item the origin starts with: the id of a weapon_shape or armour_shape and how many.</summary>
public sealed record KitItemDef
{
    public required string Item { get; init; }
    public int Count { get; init; } = 1;
    /// <summary>The item quality (crude, poor, common, fine, masterwork); empty is common (P5-A1). An old seven-tier name loads with a warning.</summary>
    [ItemQualityField] public string? Quality { get; init; }
}

/// <summary>
/// Kin an origin starts with (inheritance 3, P5-A4): a relation (<c>child</c>, <c>spouse_dead</c>, <c>parent</c>, <c>sibling</c>), an age range in years and a count range, rolled with
/// stream 0x560 keyed on the hero seed. A dead spouse is a row that died before the game began; the others are living people of the start settlement.
/// </summary>
public sealed record OriginKinDef
{
    public required string Rel { get; init; }
    public List<int> Age { get; init; } = [];
    public List<int> Count { get; init; } = [];
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
    /// <summary>P5-A4: the hero's age at the start in years, [min, max] (default 22 to 30); rolled with stream 0x560.</summary>
    public List<int> Age { get; init; } = [];
    /// <summary>P5-A4: real kin by origin (inheritance 3). Empty: the Phase 4 rule, the grown child of a household of the start region (<c>HeroKin.Choose</c>).</summary>
    public List<OriginKinDef> Kin { get; init; } = [];
    /// <summary>P5-A4: the occupation the hero's person row carries (a sim <c>Occupation</c> name); empty is none.</summary>
    public string Occupation { get; init; } = "";
}
