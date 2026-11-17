namespace Ravensguard.Content;

public sealed record ModManifest
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public string Version { get; init; } = "0.0.0";
    public List<string> LoadAfter { get; init; } = [];
    public List<string> Dependencies { get; init; } = [];
}

/// <summary>A mod folder with its parsed manifest and its position in the caller's list.</summary>
public sealed record ModInfo(ModManifest Manifest, string Folder, int InputIndex);
