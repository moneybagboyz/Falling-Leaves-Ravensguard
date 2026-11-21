namespace Ravensguard.Sim.Diagnostics;

/// <summary>
/// The Phase 7 trail (overview 7; the <c>phase7_trail</c> line of <c>WorldTool hashes</c>, compared across Linux and Windows by CI). P7-0A is the skeleton only: the line is empty, so the tool prints no line and
/// every existing golden hash is unchanged. Each Phase 7 section's hash (<c>CoinHash</c>, <c>ScaleHash</c>, <c>WorksHash</c>, <c>IndustryHash</c>, <c>SupplyHash</c>, <c>ArmyHash</c>, <c>SoldierHash</c>, <c>SiegeHash</c>,
/// <c>NavalHash</c>, the battle's fight hash) joins <see cref="Parts"/> only once non-empty, and the line appears with the first one (the close-out scripts the run that fills it).
/// </summary>
public static class Phase7Trail
{
    /// <summary>The hash parts, in order, of the sections a trail run left non-empty (name, hash); none at P7-0A.</summary>
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
