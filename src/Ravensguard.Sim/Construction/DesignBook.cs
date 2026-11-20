using System.Security.Cryptography;
using System.Text;

namespace Ravensguard.Sim.Construction;

/// <summary>What a design is a plan of (overview 7: the "designs" section holds building plans now and vehicle designs from P5-G).</summary>
public enum DesignKind : byte { Building = 0, Vehicle = 1 }

/// <summary>
/// P5-C: the player's own designs this save knows (build-mode-and-construction 4.8, overview 7): the jsonc text of every player blueprint saved in this game or placed from the player's
/// mod, by id ("player." prefix), so a save opened on another machine still builds what its sites were placed from. Saved in the optional "designs" section (absent while empty).
/// The text is the truth; the hash is the text's, kept for a quick compare with the copy on disk.
/// </summary>
public sealed class DesignBook
{
    public SortedDictionary<string, (DesignKind Kind, string Text)> Designs { get; } = new(StringComparer.Ordinal);

    public bool IsEmpty => Designs.Count == 0;

    public static string HashOf(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    public void Put(string id, string text, DesignKind kind = DesignKind.Building) => Designs[id] = (kind, text);

    public string? Text(string id) => Designs.TryGetValue(id, out var d) ? d.Text : null;

    /// <summary>The designs' hash for the resume test: ids, kinds and texts in id order.</summary>
    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (id, (kind, text)) in Designs)
        {
            h.AppendData(Encoding.UTF8.GetBytes(id)); h.AppendData([0, (byte)kind]);
            h.AppendData(Encoding.UTF8.GetBytes(text)); h.AppendData([0]);
        }
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
