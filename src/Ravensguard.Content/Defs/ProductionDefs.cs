namespace Ravensguard.Content;

/// <summary>
/// One input or output line of a recipe (production spec 2.2). Exactly one of <c>kg</c> (kilogram goods), <c>litres</c> (litre goods) or <c>pieces</c>
/// (counted goods) is given, matching the unit of the good; the simulation turns it into the store's integer quantity.
/// </summary>
public sealed record RecipeIoDef
{
    public required string Good { get; init; }
    public double Kg { get; init; }
    public double Litres { get; init; }
    public int Pieces { get; init; }
}

/// <summary>Tool requirement of a recipe: a tool of this quality class at least <c>min</c> speeds work, and loses <c>wear_ppm_per_batch</c> of a piece per batch.</summary>
public sealed record RecipeToolDef
{
    public required string Quality { get; init; }
    public int Min { get; init; } = 1;
    public int WearPpmPerBatch { get; init; }
}

/// <summary>
/// A production recipe (production spec 2.2, 2.3): inputs plus worker-hours at skill 50 give outputs. Mass is checked at load:
/// <c>sum(outputs kg) &lt;= sum(inputs kg) + adds_kg</c>. Station kinds and wage classes are plain strings validated by the recipe book.
/// </summary>
[ContentType("recipe")]
public sealed record RecipeDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>Station kind that runs it (oven, loom, bloomery ...).</summary>
    public required string Station { get; init; }
    /// <summary>Skill name from the people skill list, "None" for unskilled work.</summary>
    public string Skill { get; init; } = "None";
    public required int WorkerHours { get; init; }
    public int MaxWorkers { get; init; } = 2;
    public required List<RecipeIoDef> Inputs { get; init; }
    public required List<RecipeIoDef> Outputs { get; init; }
    /// <summary>Water, yeast, air that the mass check allows on top of the inputs.</summary>
    public double AddsKg { get; init; }
    public RecipeToolDef? Tool { get; init; }
    /// <summary>Calendar days the station is also busy (tanning pits 15, charcoal clamps 3); 0 for none.</summary>
    public int DurationDays { get; init; }
    public bool Quality { get; init; } = true;
    /// <summary>unskilled (15 mites/hour), skilled (35) or master (50).</summary>
    public string WageClass { get; init; } = "skilled";
    /// <summary>Smallest share of a full batch (permille) a station may run when inputs are short; 1,000 means whole batches only.</summary>
    public int MinBatchPermille { get; init; } = 1000;
}

/// <summary>A workstation line of a business: <c>count</c> stations of the given kind.</summary>
public sealed record StationDef
{
    public required string Kind { get; init; }
    public int Count { get; init; } = 1;
}

/// <summary>A staff slot of a business: <c>count</c> jobs of this occupation at this wage class.</summary>
public sealed record StaffSlotDef
{
    public required string Occupation { get; init; }
    public int Count { get; init; } = 1;
    public string WageClass { get; init; } = "skilled";
}

/// <summary>
/// A kind of business (production spec 2.2): the lot use it works in, its stations (the old "building level" is the station count), staff slots and the
/// recipes its stations may run. <c>owner_occupation</c> picks it for the owners found in the population.
/// </summary>
[ContentType("business")]
public sealed record BusinessDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>Occupation of the owner-operator that founds this kind at world start.</summary>
    public required string OwnerOccupation { get; init; }
    /// <summary>Lot use it works in (Smithy, Tavern, Mill) or empty for any home workshop or a site.</summary>
    public string LotUse { get; init; } = "";
    public required List<StationDef> Stations { get; init; }
    public List<StaffSlotDef> StaffSlots { get; init; } = [];
    public required List<string> Recipes { get; init; }
    /// <summary>shop, stall or export (how outputs are sold).</summary>
    public string OutputsSoldAs { get; init; } = "shop";
    /// <summary>Extraction ids this business may run (woodlot, fishery, mine, charcoal clamp crews); empty for pure workshops.</summary>
    public List<string> Extractions { get; init; } = [];
    /// <summary>True for merchants: they hold market stock and cash rather than run recipes.</summary>
    public bool Trader { get; init; }
    /// <summary>True when staff earn a day's wage for every day they are on the books (a manor's household and demesne staff, a stall's hands) rather than by the worker-hour of a recipe.</summary>
    public bool Salaried { get; init; }
    /// <summary>P5-F: a kind only the player founds (extraction businesses where the resources are, a bloomery, a sawmill): world generation and background growth never open one.</summary>
    public bool FoundedOnly { get; init; }
    /// <summary>P5-F: the worker-hours of its recipes in permille (a water sawmill's wheel does seven eighths of the sawing: 125).</summary>
    public int LabourPm { get; init; } = 1000;
}

/// <summary>
/// An extraction job kind (resources spec 2.4, 3.1, 3.2): rate per worker-day, season and weather multipliers, the resource it depletes and the good it makes.
/// Seasons are spring, summer, autumn, winter in permille.
/// </summary>
[ContentType("extraction")]
public sealed record ExtractionDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>Fish, Wood, Game, Peat, Forage, Salt, Mine, Stone, Clay or Sand.</summary>
    public required string Resource { get; init; }
    public string Skill { get; init; } = "None";
    /// <summary>Grams of output per worker-day at full stock, skill 50, clear weather (not used by hunting, which draws per hunter-day).</summary>
    public required long RateGPerWorkerDay { get; init; }
    public required List<int> SeasonMultPermille { get; init; }
    public List<string> WeatherBlock { get; init; } = [];
    public List<string> WeatherHalf { get; init; } = [];
    public required string OutputGood { get; init; }
    public int StockFloorPct { get; init; } = 30;
    /// <summary>Days of the season the job runs (peat: 1-15 of spring only is the whole season; 0 = all).</summary>
    public int SeasonDays { get; init; }
    /// <summary>By-products made per kilogram of output, in permille of the output mass (oak bark from felling).</summary>
    public List<ExtractionByproductDef> Byproducts { get; init; } = [];
    /// <summary>Tile search radius in metres (1,500 for wood and forage, 4,000 for game and fish).</summary>
    public int SearchRadiusM { get; init; } = 1500;
}

/// <summary>A by-product of an extraction: <c>permille</c> of the output grams, of the given good.</summary>
public sealed record ExtractionByproductDef
{
    public required string Good { get; init; }
    public required int Permille { get; init; }
}

/// <summary>Pure carcass numbers of hunting (resources spec 3.2) and the mine constants (3.5), kept as content so they can be tuned.</summary>
[ContentType("resource_settings")]
public sealed record ResourceSettingsDef : ContentDef
{
    public int WorkdayHours { get; init; } = 10;
    public int HuntCarcassMeatG { get; init; } = 40_000;
    public int HuntCarcassHideG { get; init; } = 4_000;
    public int HuntKillPermillePerDensity { get; init; } = 3000;
    public int HuntKillMaxPermille { get; init; } = 500;
    public int WoodKgPerM3 { get; init; } = 700;
    public int WoodRegrowGPerAcreSeason { get; init; } = 143_836;
    public int FishRegrowPct { get; init; } = 25;
    public int FishWinterRegrowPermille { get; init; } = 400;
    public int GameGrowthPctYear { get; init; } = 30;
    public int PeatRegrowGPerAcreYear { get; init; } = 50_000;
    public int MineFloodPctPerLevel { get; init; } = 3;
    public int MineCaveInPermillePerLevel { get; init; } = 5;
    public int SilverGradeGPerT { get; init; } = 3000;
}
