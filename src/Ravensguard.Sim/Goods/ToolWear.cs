using Ravensguard.Content;

namespace Ravensguard.Sim.Goods;

/// <summary>
/// Tool wear and breakage (items-quality-and-wear 4.3, P5-A1). A tool row loses <c>wear_pm_per_hour x wear_pm[tier] / 1000</c> of condition per attended game hour for each quality it is used
/// for (the <c>craft_tool</c> row's rate, at Common). The fraction is kept in milli-permille in the low twelve bits of the row's <c>Wear</c> and carried into the condition at 1,000, so 0.5 an
/// hour is exact; the top four bits are the repair count. At condition 0 the tool breaks: the row is freed and its components' mass goes to the owner's store as scrap (metal to
/// <c>scrap_iron</c>, wood to <c>shavings</c>, cloth and leather to <c>rags</c>), counted by the mass ledger. A tool below 300 gives <c>tool_bonus -3</c>. Integer only; applied at the barrier.
/// </summary>
public sealed class ToolWear
{
    public const int BluntBelowPm = 300, BluntToolBonus = -3;
    private readonly Dictionary<string, Dictionary<string, int>> _milliPerHour = new(StringComparer.Ordinal);
    private readonly QualityTiers _tiers;

    public ToolWear(ContentDatabase db, QualityTiers? tiers = null)
    {
        _tiers = tiers ?? QualityTiers.From(db);
        foreach (var t in db.All<CraftToolDef>().OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (q, pm) in t.WearPmPerHour) d[q] = (int)Math.Round(pm * 1000, MidpointRounding.AwayFromZero);       // the content's decimals become integers here, once
            _milliPerHour[t.Id] = d;
        }
    }

    /// <summary>Milli-permille of condition one attended hour takes from a tool of this shape used for <paramref name="quality"/> at Common (0 when the content gives no rate).</summary>
    public int MilliPerHour(string shape, string quality) => _milliPerHour.TryGetValue(shape, out var d) && d.TryGetValue(quality, out var v) ? v : 0;

    /// <summary>The tool bonus a worn tool still gives: <see cref="BluntToolBonus"/> below <see cref="BluntBelowPm"/>, else 0 (a blunt chisel).</summary>
    public static int Bonus(int conditionPm) => conditionPm < BluntBelowPm ? BluntToolBonus : 0;

    /// <summary>
    /// Wears the tool row for <paramref name="seconds"/> of attended use for <paramref name="quality"/> (an improvised tool wears at x3). Returns true when it broke: the row is then freed and
    /// its scrap is in <paramref name="ownerStore"/> (when given).
    /// </summary>
    public bool Use(ItemTable items, int row, string quality, long seconds, Store? ownerStore, Func<string, string?> materialCategory, bool improvised = false)
    {
        ref var r = ref items.At(row);
        long milli = (long)MilliPerHour(r.Shape, quality) * _tiers.Wear(r.Quality) / 1000 * seconds / 3600;
        if (improvised) milli *= 3;
        if (milli <= 0) return false;
        long acc = QualityTiers.WearFraction(r.Wear) + milli;
        int lose = (int)Math.Min(int.MaxValue, acc / 1000);
        r.Wear = QualityTiers.WithWearFraction(r.Wear, (int)(acc % 1000));
        if (lose == 0) return false;
        r.ConditionPermille = (short)Math.Max(0, r.ConditionPermille - lose);
        if (r.ConditionPermille > 0) return false;
        Break(items, row, ownerStore, materialCategory);
        return true;
    }

    /// <summary>Wears the tool by whole attended hours (the rule as the tests state it).</summary>
    public bool UseHours(ItemTable items, int row, string quality, int hours, Store? ownerStore, Func<string, string?> materialCategory)
    {
        bool broke = false;
        for (int h = 0; h < hours && !broke; h++) broke = Use(items, row, quality, 3600, ownerStore, materialCategory);
        return broke;
    }

    /// <summary>The scrap good a component of this material category becomes.</summary>
    public static string ScrapOf(string? category) => category switch { "metal" => "scrap_iron", "wood" => "shavings", "cloth" or "leather" or "fibre" => "rags", _ => "scrap_iron" };

    /// <summary>
    /// A row falls apart: its components' mass, gram for gram, is created in <paramref name="ownerStore"/> as scrap (reason Dematerialised, the documented faucet for an item that becomes
    /// stock), and the row is freed. Returns the grams of scrap made.
    /// </summary>
    public static long Break(ItemTable items, int row, Store? ownerStore, Func<string, string?> materialCategory)
    {
        var id = items.At(row).Id;
        long grams = 0;
        if (ownerStore != null)
        {
            var bySrap = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var c in items.ComponentsOf(row))
            {
                string scrap = ScrapOf(materialCategory(c.Material));
                bySrap[scrap] = bySrap.GetValueOrDefault(scrap) + c.MassG;
            }
            foreach (var (good, g) in bySrap)
                if (ownerStore.Catalog.TryIdOf(good, out var gid) && g > 0 && ownerStore.CreateUpTo(gid, g, CreateReason.Dematerialised) > 0) grams += g;
        }
        items.Free(id);
        return grams;
    }
}
