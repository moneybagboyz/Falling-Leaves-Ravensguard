namespace Ravensguard.Content;

/// <summary>
/// The one quality scale (items-quality-and-wear 3, P5-A1): every number a tier drives, permille, indexed by the sim's <c>Goods.ItemQuality</c> byte (crude, poor, common, fine,
/// masterwork). Combat Qw and Qa share one column (DF's standard 1.0, fine 1.4, masterwork 2.0, and crude and poor below DF's floor); there is no artifact tier (decided with the
/// user, Nov 2026: a legendary piece is a named masterwork, not a stronger one). One row, <c>content/base/goods/quality.jsonc</c>.
/// </summary>
[ContentType("quality_tiers")]
public sealed record QualityTiersDef : ContentDef
{
    public List<int> CombatPm { get; init; } = [];
    public List<int> ValuePm { get; init; } = [];
    public List<int> WearPm { get; init; } = [];
    public List<int> RepairCapPm { get; init; } = [];
    /// <summary>The crafting score each tier starts at (<c>FromScore</c>): 0, 15, 40, 70, 90.</summary>
    public List<int> ScoreFrom { get; init; } = [];
}

/// <summary>Marks a string property that holds an item quality name; the validator reads it through <see cref="QualityNames"/> and warns on an old name.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ItemQualityFieldAttribute : Attribute;

/// <summary>
/// Item quality names as content writes them (items-quality-and-wear 6): the five tiers by name, and for one release the seven names of the old DF scale read through a fixed map with a
/// load warning (standard and well_crafted to common; finely_crafted, superior and exceptional to fine; masterwork to masterwork; artifact to masterwork, named).
/// </summary>
public static class QualityNames
{
    public static readonly string[] Tiers = ["crude", "poor", "common", "fine", "masterwork"];

    private static readonly Dictionary<string, (int Tier, bool Named)> Old = new(StringComparer.Ordinal)
    {
        ["standard"] = (2, false), ["well_crafted"] = (2, false), ["finely_crafted"] = (3, false), ["superior"] = (3, false), ["exceptional"] = (3, false),
        ["artifact"] = (4, true),
    };

    /// <summary>The tier index of a name (0 crude .. 4 masterwork); <paramref name="alias"/> is set when it was an old name, <paramref name="named"/> for the old artifact. False when unknown.</summary>
    public static bool TryParse(string? name, out int tier, out bool alias, out bool named)
    {
        tier = 2; alias = false; named = false;
        if (string.IsNullOrEmpty(name)) return true;
        int i = Array.IndexOf(Tiers, name);
        if (i >= 0) { tier = i; return true; }
        if (Old.TryGetValue(name, out var o)) { tier = o.Tier; named = o.Named; alias = true; return true; }
        return false;
    }

    /// <summary>The tier index of a name, common when empty or unknown.</summary>
    public static int Tier(string? name) => TryParse(name, out int t, out _, out _) ? t : 2;
}
