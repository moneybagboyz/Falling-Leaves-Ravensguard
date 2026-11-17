namespace Ravensguard.Content;

/// <summary>
/// How a culture builds (plan §2.9): which wall and floor piece a blueprint's <c>$wall</c> / <c>$floor</c> legend tokens
/// become at each wealth level ("poor", "middling", "rich"). Values are <see cref="BuildPieceDef"/> ids (checked by the blueprint validator).
/// </summary>
[ContentType("culture_style")]
public sealed record CultureStyleDef : ContentDef
{
    public required string Name { get; init; }
    public required Dictionary<string, string> Walls { get; init; }
    public required Dictionary<string, string> Floors { get; init; }
}
