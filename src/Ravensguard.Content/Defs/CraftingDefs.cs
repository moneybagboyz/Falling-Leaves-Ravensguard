namespace Ravensguard.Content;

/// <summary>One line of a recipe's component list (basic-crafting spec 3.1). Exactly one of kg, litres or pieces, matching the unit of the good. Alternatives are the entries of one inner list.</summary>
public sealed record CraftComponentDef
{
    public required string Good { get; init; }
    public double Kg { get; init; }
    public double Litres { get; init; }
    public int Pieces { get; init; }
    /// <summary>A mould or form: used, not consumed.</summary>
    public bool Keep { get; init; }
    /// <summary>A step id that must be done first for this alternative (grinding wheat).</summary>
    public string? NeedsStep { get; init; }
}

/// <summary>A tool quality at a level (cutting 1, hammering 2, smithing 3); level 0 means an improvised thing is enough.</summary>
public sealed record CraftToolReqDef
{
    public required string Quality { get; init; }
    public int Level { get; init; } = 1;
}

/// <summary>One step of a recipe: attended (the crafter works) or unattended (the work sits in the world with a deadline and a grace window, spec 4.5).</summary>
public sealed record CraftStepDef
{
    public required string Id { get; init; }
    public int Minutes { get; init; }
    public double Hours { get; init; }
    public bool Attended { get; init; } = true;
    public double DeadlineH { get; init; }
    public double GraceH { get; init; }
    public string? Container { get; init; }
    public string? RuinedInto { get; init; }
    /// <summary>The step where failure can occur.</summary>
    public bool Critical { get; init; }
    public CraftComponentDef? Fuel { get; init; }
    /// <summary>An outdoor unattended step: rain halves the deadline.</summary>
    public bool Outdoor { get; init; }
}

/// <summary>A product. A good (stock, yield scales with quality) or an item shape (carries the quality tier, mass is <c>kg</c>, made of <c>material</c>).</summary>
public sealed record CraftResultDef
{
    public string? Good { get; init; }
    public string? Item { get; init; }
    public string? Material { get; init; }
    public double Kg { get; init; }
    public double Litres { get; init; }
    public int Pieces { get; init; }
}

/// <summary>A by-product that appears with a chance in permille (ash from a fire).</summary>
public sealed record CraftByproductDef
{
    public required string Good { get; init; }
    public double Kg { get; init; }
    public int Pieces { get; init; }
    public int ChancePm { get; init; } = 1000;
}

public sealed record CraftProfRefDef
{
    public required string Id { get; init; }
    public int TimeMultPm { get; init; } = 1000;
    public int SkillPenalty { get; init; }
}

public sealed record CraftBatchDef
{
    public int Max { get; init; } = 1;
    public int TimeScalePm { get; init; } = 800;
}

public sealed record CraftAutoDef
{
    public required string Skill { get; init; }
    public int Level { get; init; }
}

/// <summary>How a recipe is learnt (spec 4.6).</summary>
public sealed record CraftLearnDef
{
    public bool KnownByAll { get; init; }
    public CraftAutoDef? AutoAt { get; init; }
    public bool Master { get; init; } = true;
    public double WatchHours { get; init; }
    public string? Manuscript { get; init; }
    public bool Decomp { get; init; }
}

/// <summary>A practice recipe: trains skill and proficiency but gives no item; <c>d_x100</c> is the gain weight and the gain stops at <c>cap_level</c>.</summary>
public sealed record CraftPracticeDef
{
    public int GainDx100 { get; init; } = 40;
    public int CapLevel { get; init; } = 30;
}

/// <summary>A mending recipe acts on an existing item: it adds condition (permille) up to <c>max</c> and may set a flag.</summary>
public sealed record CraftRepairDef
{
    public int Add { get; init; } = 300;
    public int Max { get; init; } = 700;
    public string Flag { get; init; } = "repaired";
    /// <summary>What kind of item may be mended: garment, tool or blade.</summary>
    public string Target { get; init; } = "garment";
}

/// <summary>A recipe in the Cataclysm-like shape (basic-crafting spec 3.1). Quantities are kg, litres or pieces; times are game minutes or hours.</summary>
[ContentType("craft_recipe")]
public sealed record CraftRecipeDef : ContentDef
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    /// <summary>A name of the people skill list, "None" for none.</summary>
    public required string Skill { get; init; }
    public required int Difficulty { get; init; }
    /// <summary>light, moderate or heavy.</summary>
    public string Effort { get; init; } = "light";
    public List<CraftProfRefDef> Proficiencies { get; init; } = [];
    /// <summary>Each inner list is a set of alternatives; the first satisfiable one is used.</summary>
    public List<List<CraftComponentDef>> Components { get; init; } = [];
    public List<string> Using { get; init; } = [];
    public List<CraftToolReqDef> Tools { get; init; } = [];
    public string? Workstation { get; init; }
    public int MinLight { get; init; }
    public Dictionary<string, int> MinAttr { get; init; } = [];
    public required List<CraftStepDef> Steps { get; init; }
    public List<CraftResultDef> Results { get; init; } = [];
    public List<CraftByproductDef> Byproducts { get; init; } = [];
    /// <summary>Water and air that the mass check allows on top of the components.</summary>
    public double AddsKg { get; init; }
    public CraftBatchDef Batch { get; init; } = new();
    public CraftLearnDef Learn { get; init; } = new();
    public CraftPracticeDef? Practice { get; init; }
    public CraftRepairDef? Repair { get; init; }
    public string Culture { get; init; } = "any";
    public string Secret { get; init; } = "none";
    public int Morale { get; init; }
    public string Note { get; init; } = "";
    /// <summary>What the finished work does in the world instead of (or besides) making goods: lit_fire, repair_cart, refit.</summary>
    public string? Effect { get; init; }
}

/// <summary>A reusable requirement set (<c>using</c>): qualities and a station or a fire, as in "oven_or_hearth" (spec 3.1).</summary>
[ContentType("requirement_set")]
public sealed record RequirementSetDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>Qualities at levels; a lit fire, an oven or a forge provide <c>heat</c>, a pot provides <c>cooking</c>, and so on.</summary>
    public List<CraftToolReqDef> Qualities { get; init; } = [];
    /// <summary>Water within 8 m or carried (ambient water goods are drawn from the site).</summary>
    public bool Water { get; init; }
}

/// <summary>A workstation (a furniture piece with qualities it provides, spec 3.1).</summary>
[ContentType("craft_station")]
public sealed record CraftStationDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>The build piece ids that count as this station.</summary>
    public List<string> Pieces { get; init; } = [];
    public Dictionary<string, int> Provides { get; init; } = [];
}

/// <summary>A tool item and the qualities it carries (knife cutting 1, hammer hammering 2).</summary>
[ContentType("craft_tool")]
public sealed record CraftToolDef : ContentDef
{
    public required string Name { get; init; }
    public Dictionary<string, int> Qualities { get; init; } = [];
    /// <summary>The good (a counted piece) that is this tool, when the tool is held as stock.</summary>
    public string? Good { get; init; }
    /// <summary>P5-A1 (items-quality-and-wear 4.3): permille of condition an attended game hour takes per quality used, at Common (0.5 is exact: kept to the milli-permille).</summary>
    public Dictionary<string, double> WearPmPerHour { get; init; } = [];
}

/// <summary>A proficiency: a learnt know-how that speeds work and sharpens skill (spec 3.2). They form a graph without cycles.</summary>
[ContentType("proficiency")]
public sealed record ProficiencyDef : ContentDef
{
    public required string Name { get; init; }
    public List<ProfRequireDef> Requires { get; init; } = [];
    public int LearnPmPerHour { get; init; } = 8;
    public int TeachMultPm { get; init; } = 2000;
    public int ManuscriptMultPm { get; init; } = 1500;
}

public sealed record ProfRequireDef
{
    public required string Id { get; init; }
    public int MinPm { get; init; }
}

/// <summary>A written work that teaches recipes (spec 3.4).</summary>
[ContentType("manuscript")]
public sealed record ManuscriptDef : ContentDef
{
    public required string Name { get; init; }
    public List<string> Recipes { get; init; } = [];
}
