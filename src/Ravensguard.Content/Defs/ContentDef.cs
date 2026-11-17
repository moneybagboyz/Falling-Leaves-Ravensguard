namespace Ravensguard.Content;

/// <summary>Base for all content definitions. The JSON "type" field is consumed by the loader.</summary>
public abstract record ContentDef
{
    public required string Id { get; init; }
}
