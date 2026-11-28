namespace Ravensguard.Content;

/// <summary>
/// Names for named artefacts (items-quality-and-wear 4.2, P5-A1): a masterwork weapon, armour piece or jewel that rolls a name gets <c>{epithet} of {maker}</c> from the pool
/// (the epithet picked by the item's id, the maker's given name). The row with no <see cref="Culture"/> is the default pool that serves every culture without a table of its own; a row naming a
/// culture (P8-LM-2: one of the continent's eight culture names) holds that culture's own tables of weapons, relics and heirlooms.
/// </summary>
[ContentType("artefact_names")]
public sealed record ArtefactNamesDef : ContentDef
{
    public List<string> Epithets { get; init; } = [];
    /// <summary>The pattern: <c>{epithet}</c> and <c>{maker}</c> are filled in.</summary>
    public string Pattern { get; init; } = "{epithet} of {maker}";
    /// <summary>The continent culture this pool belongs to (e.g. "Aldmark"); empty for the default pool.</summary>
    public string Culture { get; init; } = "";
    /// <summary>A culture's named weapons (the epithet of "{epithet} of {maker}"), relics and heirlooms (whole names).</summary>
    public List<string> Weapons { get; init; } = [];
    public List<string> Relics { get; init; } = [];
    public List<string> Heirlooms { get; init; } = [];
}
