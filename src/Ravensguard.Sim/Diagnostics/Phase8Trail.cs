namespace Ravensguard.Sim.Diagnostics;

/// <summary>
/// The Phase 8 trail (overview 3, P8-0A; the <c>phase8_trail</c> line of <c>WorldTool hashes</c>, compared across Linux and Windows by CI). Skeleton only: the line is empty, so the tool prints no line and every
/// existing golden hash is unchanged. Each Phase 8 section's hash joins <see cref="Parts"/> only once non-empty, and the line appears with the first one.
/// </summary>
public static class Phase8Trail
{
    /// <summary>The hash parts, in order, of the sections a trail run left non-empty (name, hash); none at P8-0A.</summary>
    public static IReadOnlyList<(string Name, string Hash)> Parts(ulong seed) => [];

    /// <summary>The trail's one hash: empty while no part is filled (the tool then prints no line), else a SHA-256 of the parts in order.</summary>
    public static string Hash(ulong seed)
    {
        var parts = Parts(seed);
        if (parts.Count == 0) return "";
        var text = string.Join("|", parts.Select(p => p.Name + "=" + p.Hash));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];
    }
}
