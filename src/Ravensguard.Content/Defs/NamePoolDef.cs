namespace Ravensguard.Content;

/// <summary>
/// Per-culture personal names (plan §4A.4). Given names are built from syllables so a culture has hundreds of distinct names per sex
/// (onset + mid + end); family stems and endings make hereditary surnames; patronymic suffixes make "Aldricson" style names for peasants.
/// </summary>
[ContentType("name_pool")]
public sealed record NamePoolDef : ContentDef
{
    public required string Name { get; init; }
    public required List<string> Onsets { get; init; }
    public required List<string> Mids { get; init; }
    public required List<string> MaleEnds { get; init; }
    public required List<string> FemaleEnds { get; init; }
    public required List<string> FamilyStems { get; init; }
    public required List<string> FamilyEnds { get; init; }
    public required string PatronymicSon { get; init; }
    public required string PatronymicDaughter { get; init; }
    public required List<string> Bynames { get; init; }
}
