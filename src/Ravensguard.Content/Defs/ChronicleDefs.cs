namespace Ravensguard.Content;

/// <summary>One parameter of a chronicle entry type (chronicle-full 3): its name in the text key's sentence and its type: person, place, item, good, mites, count, days, entry or text_key.</summary>
public sealed record ChronicleParamDef
{
    public required string Name { get; init; }
    public required string Type { get; init; }
}

/// <summary>
/// One row per chronicle entry type id (<c>content/base/chronicle/types.jsonc</c>, chronicle-full 3): the en.csv key of its sentence, its typed parameters, the cause kinds it may
/// carry, the rumour row it feeds (Phase 3, by rumour_type id) and what makes its causes specific: an <c>event</c> naming an earlier entry, a <c>note</c> key from <see cref="NoteKeys"/>,
/// or <c>either</c>. A <see cref="Chance"/> type may be a root (an accident's dice), a <see cref="Root"/> type starts things on its own (weather, a person's decision, the history's seeding).
/// <see cref="Legacy"/> marks a writer that still passes English notes and data (Phase 2-5 and history v0): its entries are shown as written and its sentence key is the type's heading.
/// </summary>
[ContentType("chronicle_type")]
public sealed record ChronicleTypeDef : ContentDef
{
    public required string TextKey { get; init; }
    public List<ChronicleParamDef> Params { get; init; } = [];
    /// <summary>CauseKind names this type may carry (empty: any).</summary>
    public List<string> CauseKinds { get; init; } = [];
    /// <summary>The rumour_type row id this type feeds ("" none).</summary>
    public string Rumour { get; init; } = "";
    /// <summary>event, note or either.</summary>
    public string SpecificRequires { get; init; } = "event";
    public List<string> NoteKeys { get; init; } = [];
    public bool Chance { get; init; }
    public bool Root { get; init; }
    public bool Legacy { get; init; }
}
