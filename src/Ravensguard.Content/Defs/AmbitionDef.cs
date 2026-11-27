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
    /// <summary>P8-AT-1: <c>start</c> (an origin's), <c>middle</c> or <c>great</c>. An old or modded ambition with none is a start ambition.</summary>
    public string Tier { get; init; } = "start";
    /// <summary>P8-AT-1: the ambitions offered when this one is done (those whose <see cref="Requires"/> hold).</summary>
    [ContentRef(typeof(AmbitionDef))] public List<string> Opens { get; init; } = [];
    /// <summary>P8-AT-1: conditions that must hold for this ambition to be offered at all.</summary>
    public List<ConditionRefDef> Requires { get; init; } = [];
    /// <summary>P8-AT-1: a done ambition in this list keeps this one from being offered as a middle ambition.</summary>
    [ContentRef(typeof(AmbitionDef))] public List<string> Excludes { get; init; } = [];
    /// <summary>P8-AT-1: a great ambition names the ending it offers (endings spec 3.4: dynasty, crown, realm, legend, merchant_prince, holy_life).</summary>
    public string Ending { get; init; } = "";
}
