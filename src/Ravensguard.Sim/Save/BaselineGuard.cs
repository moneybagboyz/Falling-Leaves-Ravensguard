using System.Security.Cryptography;
using System.Text;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The guard on diff-against-baseline saves (note N24, review phase 2 #5). A region's people are stored as patches over the population the generator makes again, so the
/// save is only meaningful against the very same generated region. Each such section stores the hash of the regenerated baseline (people, households, lots, terrain) and the
/// <see cref="SaveFormat.RegionGeneratorVersion"/>; loading regenerates, compares and refuses with a clear message instead of applying patches to the wrong people.
/// </summary>
public static class BaselineGuard
{
    /// <summary>Hash of a regenerated baseline: its population (people, households, lot bindings) and the terrain it stands on.</summary>
    public static string HashOf(Population baseline)
    {
        var terrain = LayerHashes.Of(baseline.Region);
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(baseline.ComputeHash() + ":" + terrain)));
    }

    /// <summary>Throws <see cref="SaveException"/> when the regenerated baseline differs from the one the save was diffed against. An empty stored hash (format v1) is not checked.</summary>
    public static void Verify(string what, string storedHash, int storedGeneratorVersion, Population regenerated)
    {
        if (storedHash.Length == 0) return;
        if (storedGeneratorVersion != SaveFormat.RegionGeneratorVersion)
            throw new SaveException($"{what} of this save was made with region generator v{storedGeneratorVersion}; this game generates regions with v{SaveFormat.RegionGeneratorVersion}, so the saved changes cannot be applied. Load it with the game version that made it.");
        if (!string.Equals(storedHash, HashOf(regenerated), StringComparison.Ordinal))
            throw new SaveException($"{what} of this save cannot be loaded: the region regenerated from the seed differs from the one the save was made against "
                + "(the world generator or game content changed since). Load it with the game version that made it.");
    }
}
