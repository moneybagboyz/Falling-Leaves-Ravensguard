namespace Ravensguard.Content;

/// <summary>A reference to a condition of the small typed vocabulary (see <see cref="BaseVocabulary"/>) with string arguments.</summary>
public sealed record ConditionRefDef
{
    public required string Id { get; init; }
    public Dictionary<string, string> Args { get; init; } = [];
}

/// <summary>One milestone of an ambition: done when every condition holds.</summary>
public sealed record AmbitionStepDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public required List<ConditionRefDef> Conditions { get; init; }
}

/// <summary>A goal with milestones (plan §3.11). Steps are data; the rules that test the conditions live in tested C#.</summary>
[ContentType("ambition")]
public sealed record AmbitionDef : ContentDef
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required List<AmbitionStepDef> Steps { get; init; }
}
