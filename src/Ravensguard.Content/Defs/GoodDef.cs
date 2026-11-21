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
    /// <summary>Grams of clothes in one set and of shoes in one pair (integration decision I36): a worn-out set is replaced by this mass of the kg goods <c>clothes</c> and <c>shoes</c>.</summary>
    public int ClothesSetG { get; init; } = 1500;
    public int ShoesPairG { get; init; } = 700;
    /// <summary>Target stock in days of use per basket line: staple, relish, ale, firewood, salt.</summary>
    public required Dictionary<string, int> TargetCoverDays { get; init; }
    public required int SurviveBelowDays { get; init; }
    public required int BufferBelowDays { get; init; }
}

/// <summary>
/// How a household of a role spends the coin it holds above its reserve (integration decision I37): each season it spends <see cref="SpendPctPerSeason"/> percent of what
/// it holds above <see cref="ReserveDays"/> days of its members' food (at the base price of grain), split into wages for idle hands, works (builders' wages and materials),
/// craft goods and alms. Roles: <c>peasant</c>, <c>burgher</c>, <c>gentry</c>, <c>lord</c> (a fief's lord), <c>parish</c> (a priest's household, with the parish chest).
/// Every spend is a transfer or a market purchase: coin and goods are conserved.
/// </summary>
[ContentType("spending_rule")]
public sealed record SpendingRuleDef : ContentDef
{
    public required string Role { get; init; }
    public required int ReserveDays { get; init; }
    public required int SpendPctPerSeason { get; init; }
    public int WagesPct { get; init; }
    public int WorksPct { get; init; }
    public int GoodsPct { get; init; }
    public int AlmsPct { get; init; }
    /// <summary>Share of a works budget paid as the builders' wages; the rest buys the materials.</summary>
    public int WorksLabourPct { get; init; } = 50;
    /// <summary>Craft goods bought, by weight (good id to weight).</summary>
    public Dictionary<string, int> GoodsWeights { get; init; } = [];
    /// <summary>Building materials bought for works, by weight.</summary>
    public Dictionary<string, int> MaterialsWeights { get; init; } = [];
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
    /// <summary>
    /// P6-D4 (holdings-and-works-v2 3.8, HW-14, conditional on the Phase 5 fun check): at world start the first merchant of every book holds at least this many days of the structural demand of each good that has one
    /// (5 when the first weeks of a new game are found starved). 0 (the default) leaves the world-start stock as it was.
    /// </summary>
    public int WorldStartStockDays { get; init; } = 0;

    // P7-CN1 (coinage 5.8, additive): the gold piece and the copper token, the mints' staff and the changers' fee
    /// <summary>Gold in a gold piece (mg), the mint's cut on gold (basis points), copper in a copper piece (mg) and the cap of copper coin's face in permille of the money supply.</summary>
    public int GoldPieceMg { get; init; } = 1100;
    public int GoldSeigniorageBp { get; init; } = 500;
    public int CopperPieceMg { get; init; } = 5000;
    public int CopperCapPermille { get; init; } = 40;
    /// <summary>A money changer's fee in permille of the amount changed.</summary>
    public int ChangerFeePermille { get; init; } = 20;
    /// <summary>A payment of at least this many mites carries gold pieces with it (opted-in payers).</summary>
    public int GoldTransferMinMites { get; init; } = 10_000;
    /// <summary>Pieces one moneyer strikes in a working day, and the mint master's daily wage.</summary>
    public int MoneyerPiecesPerDay { get; init; } = 3000;
    public int MintMasterWageMites { get; init; } = 500;
    /// <summary>Gold in a tonne of dressed ore, mg, and what a worker pans from a stream in a day, mg.</summary>
    public int GoldGradeMgPerT { get; init; } = 6000;
    public int PanGoldMgPerDay { get; init; } = 300;
}
