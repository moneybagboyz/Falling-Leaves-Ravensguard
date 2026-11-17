namespace Ravensguard.Content;

/// <summary>Minimal material definition (placeholder; fleshed out in a later task).</summary>
[ContentType("material")]
public sealed record MaterialDef : ContentDef
{
    public required string Name { get; init; }
    public required double DensityKgM3 { get; init; }
    public required double ValuePerKg { get; init; }
}
