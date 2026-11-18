using System.Text.Json.Serialization;

namespace Ravensguard.Content;

/// <summary>How quantity of a good is counted: kilograms and litres are held as grams of mass, pieces as a count (items spec 2.1).</summary>
[JsonConverter(typeof(SnakeEnumConverter<GoodUnit>))]
public enum GoodUnit { Kg, Litre, Piece }

/// <summary>Physical state of a good (items spec 2.1); informational for stowage and recipes.</summary>
[JsonConverter(typeof(SnakeEnumConverter<GoodState>))]
public enum GoodState { Solid, Powder, Grain, Liquid, Fibre, Fresh }

/// <summary>
/// A bulk or counted commodity (items spec 2.1, content/base/goods/*.jsonc). Quantity in stock is grams for kg and litre goods and a piece count
/// for piece goods. Prices are mites (1/100 crown) per kilogram, or per piece for piece goods. Rate keys carry unit suffixes.
/// </summary>
[ContentType("good")]
public sealed record GoodDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>Free-form family (grain, flour, bread, fish, fuel...) used by the needs rules to order foods.</summary>
    public required string Class { get; init; }
    public GoodState State { get; init; } = GoodState.Solid;
    public GoodUnit Unit { get; init; } = GoodUnit.Kg;
    /// <summary>Mass of one piece in grams (unit piece only).</summary>
    public int PieceMassG { get; init; }
    /// <summary>Mass of one litre in grams (unit litre only: ale 1010, milk 1030).</summary>
    public int LitreMassG { get; init; }
    /// <summary>Stowage volume per kg in litres (carts, containers): grain 1.33, flour 1.6, firewood 2.5, baled wool 3.0.</summary>
    public double BulkLitresPerKg { get; init; } = 1.0;
    /// <summary>Base price in mites per kg (per piece for piece goods); the market reference price starts here.</summary>
    public required double BasePriceMitesPerKg { get; init; }
    /// <summary>Measures of food per kg (1 measure = 1,100 g of grain); 0 for inedible goods.</summary>
    public double FoodMeasuresPerKg { get; init; }
    [ContentRef(typeof(SpoilClassDef))] public required string SpoilClass { get; init; }
    public int TargetCoverDays { get; init; }
    public bool Tradable { get; init; } = true;
    /// <summary>Material the good is made of (counted pieces and metals).</summary>
    [ContentRef(typeof(MaterialDef))] public string? Material { get; init; }
    /// <summary>Weapon or armour shape id for piece goods that can become item instances.</summary>
    public string? Shape { get; init; }
    public List<string> Tags { get; init; } = [];
}

/// <summary>
/// One row of the spoilage table (items spec 3.5): ppm of the quantity lost per game day, by the kind of store the stock sits in.
/// A store kind with no value of its own uses <c>house_ppm</c>.
/// </summary>
[ContentType("spoil_class")]
public sealed record SpoilClassDef : ContentDef
{
    public required int HousePpm { get; init; }
    public int? OpenPpm { get; init; }
    public int? BarnPpm { get; init; }
    public int? SealedPpm { get; init; }
    public int? GranaryPpm { get; init; }
    public int? CellarPpm { get; init; }
    /// <summary>Perishables rot faster in the warm and slower in the cold (the temperature multiplier applies).</summary>
    public bool TempSensitive { get; init; }
}

/// <summary>Household needs basket per social class (households spec 2.2). The class is the name of a SocialClass: Peasant, Burgher or Gentry.</summary>
[ContentType("needs_basket")]
public sealed record NeedsBasketDef : ContentDef
{
    public required string Class { get; init; }
    public required int StapleMeasuresPerPersonDayX100 { get; init; }
    public required int RelishMeasuresPerPersonDayX100 { get; init; }
    public required int AleMlPerPersonDay { get; init; }
    public required int SaltGPerPersonDay { get; init; }
    public required int FirewoodGPerPersonDay { get; init; }
    public required int ClothesLifetimeDays { get; init; }
    public required int ShoesLifetimeDays { get; init; }
    public required int ToolsLifetimeDays { get; init; }
    /// <summary>Target stock in days of use per basket line: staple, relish, ale, firewood, salt.</summary>
    public required Dictionary<string, int> TargetCoverDays { get; init; }
    public required int SurviveBelowDays { get; init; }
    public required int BufferBelowDays { get; init; }
}

/// <summary>Money constants (money spec 6): coin parity, mint, and the one-off seed money shares of world creation.</summary>
[ContentType("money_settings")]
public sealed record MoneySettingsDef : ContentDef
{
    public int CrownSilverMg { get; init; } = 1300;
    public int MintSeigniorageBp { get; init; } = 750;
    public int SeedMoneyCrownsPerPerson { get; init; } = 50;
    public int SeedGentryPct { get; init; } = 20;
    public int SeedBurgherPct { get; init; } = 35;
    public int SeedPeasantPct { get; init; } = 30;
    public int SeedTreasuryPct { get; init; } = 12;
    public int SeedParishPct { get; init; } = 3;
    // market keys (money spec 6): defaults are the spec values, money.jsonc may override them
    public int SpreadHalfPermille { get; init; } = 80;
    public int MarketTollPermille { get; init; } = 50;
    public int MarketOpenHour { get; init; } = 6;
    public int MarketCloseHour { get; init; } = 18;
    public int PriceClampMinPermille { get; init; } = 300;
    public int PriceClampMaxPermille { get; init; } = 4000;
    public int RefDriftDivisor { get; init; } = 4;
    public int DemandEmaDays { get; init; } = 7;
    public int SeedCppMites { get; init; } = 5000;
    public int LotExpiryDays { get; init; } = 3;
    public int MerchantCashShareBp { get; init; } = 4000;
    public int MerchantStockCapPermilleOfCover { get; init; } = 800;
}
