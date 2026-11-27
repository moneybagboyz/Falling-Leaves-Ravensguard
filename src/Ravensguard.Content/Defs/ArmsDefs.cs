namespace Ravensguard.Content;

/// <summary>One piece of the levy kit (army-economy 4.10): the item shape the armoury issues, the pattern recipe its metal part is forged by (empty: no forging), the iron in it, the minutes of other work, and its price in mites for an order.</summary>
public sealed record ArmsKitPieceDef
{
    public required string Shape { get; init; }
    public string Recipe { get; init; } = "";
    public int IronG { get; init; }
    public int OtherMinutes { get; init; }
    public int PriceMites { get; init; }
}

/// <summary>P7-AE3 (army-economy 4.10; content <c>content/base/war/arms.jsonc</c>): the arms industry's numbers. One row, <c>arms_rules</c>.</summary>
[ContentType("arms_rules")]
public sealed record ArmsRulesDef : ContentDef
{
    /// <summary>The share of a lord's last year's receipts a season's arms orders may cost, permille (AEQ-6).</summary>
    public int BudgetPm { get; init; } = 100;
    /// <summary>Loot at or above KeepPm goes to the armoury, RefitPm to KeepPm goes to refit, below RefitPm to salvage (permille of condition).</summary>
    public int KeepPm { get; init; } = 600;
    public int RefitPm { get; init; } = 200;
    /// <summary>The stripping of the dead costs 0 to this many permille of an item's condition (stream 0x752).</summary>
    public int StripMaxPm { get; init; } = 100;
    /// <summary>The share of a salvaged item's iron that comes back as scrap, and of its leather and cloth as rags (permille).</summary>
    public int ScrapPm { get; init; } = 700;
    public int RagsPm { get; init; } = 700;
    /// <summary>Materials that count as iron for salvage, and as leather or cloth.</summary>
    public List<string> IronMaterials { get; init; } = [];
    public List<string> SoftMaterials { get; init; } = [];
    /// <summary>The condition a refit restores an item to, by quality tier name (permille).</summary>
    public Dictionary<string, int> RefitCapPm { get; init; } = [];
    /// <summary><c>forge_scrap</c>: grams of scrap and of charcoal in a batch, the grams of bar it gives, the hours at a forge.</summary>
    public int ScrapBatchG { get; init; } = 10000;
    public int CharcoalBatchG { get; init; } = 6000;
    public int BarBatchG { get; init; } = 8000;
    public int ForgeHours { get; init; } = 8;
    /// <summary>The arms trade: a realm's arms target in grams, the demand multiple of a realm at war (permille), the permille chance a drawn pair sends a caravan, the grams a caravan carries, the price index clamp, the price of a kilogram.</summary>
    public long TargetG { get; init; } = 20_000_000;
    public int WarDemandPm { get; init; } = 3000;
    public int CaravanPm { get; init; } = 600;
    public long CaravanG { get; init; } = 4_000_000;
    public int IndexMinPm { get; init; } = 300;
    public int IndexMaxPm { get; init; } = 4000;
    public long PriceMitesPerKg { get; init; } = 600;
    /// <summary>The levy kit, in issue order.</summary>
    public List<ArmsKitPieceDef> LevyKit { get; init; } = [];
}
