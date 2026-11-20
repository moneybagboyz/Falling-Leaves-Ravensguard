namespace Ravensguard.Content;

/// <summary>
/// Names for named artefacts (items-quality-and-wear 4.2, P5-A1): a masterwork weapon, armour piece or jewel that rolls a name gets <c>{epithet} of {maker}</c> from the pool
/// (the epithet picked by the item's id, the maker's given name). One row per pool; the default pool serves every culture until cultures carry their own.
/// </summary>
[ContentType("artefact_names")]
public sealed record ArtefactNamesDef : ContentDef
{
    public List<string> Epithets { get; init; } = [];
    /// <summary>The pattern: <c>{epithet}</c> and <c>{maker}</c> are filled in.</summary>
    public string Pattern { get; init; } = "{epithet} of {maker}";
}
