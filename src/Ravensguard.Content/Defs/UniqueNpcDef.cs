namespace Ravensguard.Content;

/// <summary>
/// Where and for whom a unique NPC is placed (landmarks-npcs spec 3.2). <see cref="Kind"/> is <c>landmark</c> (the NPC keeps one of <see cref="Landmarks"/>, the first of the region's placed landmarks that
/// matches, and lives at the nearest settlement), <c>settlement</c> (a town or village of at least <see cref="MinPop"/> people; a <see cref="Seat"/> is a realm's capital) or <c>role</c> (placed by another
/// part: the first hour's mentor). <see cref="Origin"/> narrows a rival to the new game's origin.
/// </summary>
public sealed record UniqueNpcPlaceDef
{
    public string Kind { get; init; } = "settlement";
    [ContentRef(typeof(LandmarkDef))] public List<string> Landmarks { get; init; } = [];
    /// <summary>town, village or any (a town or a village).</summary>
    public string Settlement { get; init; } = "any";
    public int MinPop { get; init; }
    public bool Seat { get; init; }
    [ContentRef(typeof(OriginDef))] public string Origin { get; init; } = "";
    public string Role { get; init; } = "";
    /// <summary>The chance in permille that a fitting world has this NPC at all (0x828, keyed on the NPC and the region).</summary>
    public int ChancePm { get; init; } = 1000;
}

/// <summary>Where the NPC is found by day and by night: <c>landmark</c> (the keeper's post), <c>work</c>, <c>home</c>, <c>church</c>, <c>square</c> or <c>road</c> (a wanderer, on the roads between places).</summary>
public sealed record UniqueNpcScheduleDef
{
    public string Day { get; init; } = "work";
    public string Night { get; init; } = "home";
}

/// <summary>
/// An authored unique NPC (landmarks-npcs spec 3.2, <c>content/base/npcs/unique.jsonc</c>): a name, a life (age, trade, class, attributes, skills, traits), a placement rule, a schedule, an authored
/// dialogue (<c>content/base/dialogue/unique/&lt;id&gt;.jsonc</c>, 20 to 50 lines) and at most one contract. Placed as an ordinary person row of the world: they age, fight and die.
/// </summary>
[ContentType("unique_npc")]
public sealed record UniqueNpcDef : ContentDef
{
    public string Given { get; init; } = "";
    public string Family { get; init; } = "";
    public string Byname { get; init; } = "";
    public bool Female { get; init; }
    public int AgeMin { get; init; } = 30;
    public int AgeMax { get; init; } = 50;
    /// <summary>A name of the sim's Occupation (snake or camel case).</summary>
    public string Occupation { get; init; } = "none";
    /// <summary>peasant, burgher or gentry.</summary>
    public string Class { get; init; } = "peasant";
    /// <summary>Strength, toughness, dexterity, perception, wits (five values), or empty for the occupation's average.</summary>
    public List<int> Attributes { get; init; } = [];
    /// <summary>At most four: a skill name and its level.</summary>
    public Dictionary<string, int> Skills { get; init; } = [];
    /// <summary>From the sim's NPC trait list (<c>UniqueNpcs.TraitNames</c>).</summary>
    public List<string> Traits { get; init; } = [];
    public UniqueNpcPlaceDef Place { get; init; } = new();
    public UniqueNpcScheduleDef Schedule { get; init; } = new();
    /// <summary>A person who has no roof of their own (a pilgrim, a surgeon on the road) is a vagrant, not homeless for want of a bed.</summary>
    public bool Wanderer { get; init; }
    /// <summary>The odd-job kind the dialogue's one contract posts (guard, haul, deliver, tendshop, farmwork), or empty for none.</summary>
    public string Contract { get; init; } = "";
    /// <summary>weapon or relic: the kind of named heirloom the NPC carries, named from the table of the place's culture; empty for none.</summary>
    public string Heirloom { get; init; } = "";
    /// <summary>The id of the authored dialogue's speaker (default: the NPC's own id).</summary>
    public string Dialogue { get; init; } = "";
}
