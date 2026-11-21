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
    /// <summary>P5-E (crafting-full 4.3): the score bonus this alternative gives (iron where the recipe wants steel: -5).</summary>
    public int Bonus { get; init; }
    /// <summary>P5-E: the material an item result is made of when this alternative is used (iron bar: <c>wrought_iron</c>); null keeps the result's own.</summary>
    public string? Material { get; init; }
}

/// <summary>A tool quality at a level (cutting 1, hammering 2, smithing 3); level 0 means an improvised thing is enough.</summary>
public sealed record CraftToolReqDef
{
    public required string Quality { get; init; }
    public int Level { get; init; } = 1;
    /// <summary>P5-E (crafting-full 3.1): the Phase 2 abstract wear of a business batch (ppm of a <c>tools</c> piece), kept for abstract runs.</summary>
    public int WearPpmPerBatch { get; init; }
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
    /// <summary>P5-E (crafting-full 4.3): a step the crafter may leave out (temper); leaving it out has the process's cost.</summary>
    public bool Optional { get; init; }
}

/// <summary>A product. A good (stock, yield scales with quality) or an item shape (carries the quality tier, mass is <c>kg</c>, made of <c>material</c>).</summary>
public sealed record CraftResultDef
{
    public string? Good { get; init; }
    public string? Item { get; init; }
    /// <summary>P5-E (crafting-full 3.1): a vehicle part (an item row of that part, for the vehicle designer) or an installed build piece (an item row the builder places).</summary>
    public string? VehiclePart { get; init; }
    public string? BuildPiece { get; init; }
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
    /// <summary>P5-E: the smallest share of a full batch (permille) a business station may run (Phase 2 <c>min_batch_permille</c>).</summary>
    public int MinPm { get; init; } = 1000;
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
    /// <summary>What kind of item may be mended: garment, tool, blade (P5-E: mail, plate, leather).</summary>
    public string Target { get; init; } = "garment";
    /// <summary>P5-E: a flag the repair clears (<c>rings_burst</c>, <c>brittle</c>): optional.</summary>
    public string? Clears { get; init; }
}

/// <summary>P5-E (crafting-full 4.5): a refit changes a worn piece's size by one seventh of the wearer's body per run, toward the wearer.</summary>
public sealed record CraftRefitDef
{
    /// <summary>garment, mail or plate.</summary>
    public string Target { get; init; } = "garment";
    /// <summary>Whether the piece can be made larger (plate cannot).</summary>
    public bool Grow { get; init; } = true;
    public bool Shrink { get; init; } = true;
    /// <summary>Extra material to grow it, permille of the piece's mass (mail: 100 = 10 percent), taken from the recipe's first component slot.</summary>
    public int GrowMassPm { get; init; }
}

/// <summary>P5-E (crafting-full 4.5): taking an item apart into its stuff: each line a good and a permille of the item's mass; the rest of the mass goes to <c>rest</c>, so mass is exact.</summary>
public sealed record CraftSalvageDef
{
    /// <summary>mail or armour (plate, scale).</summary>
    public required string Target { get; init; }
    public List<CraftSalvageLineDef> Into { get; init; } = [];
    public string Rest { get; init; } = "scrap_iron";
}

public sealed record CraftSalvageLineDef
{
    public required string Good { get; init; }
    public int Pm { get; init; }
    /// <summary>Only the components of this material category (metal, leather...); null for the whole item.</summary>
    public string? Category { get; init; }
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
    /// <summary>What the finished work does in the world instead of (or besides) making goods: lit_fire, repair_cart, refit, oil.</summary>
    public string? Effect { get; init; }
    /// <summary>P6-B6 (holdings 3.8): the finished item is brittle whatever the process (knapped stone breaks on a blow a rigid layer stops, stream 0x532).</summary>
    public bool Brittle { get; init; }

    // ---- P5-E (crafting-full 3.1): one recipe format for the hero, NPCs and businesses ----
    /// <summary>A workshop may queue it; the Phase 2 recipe book is the view of these.</summary>
    public bool Business { get; init; }
    /// <summary>People at one station at once (Phase 2 <c>max_workers</c>).</summary>
    public int WorkersMax { get; init; } = 2;
    /// <summary>unskilled, skilled or master (the Phase 2 wage class of the work).</summary>
    public string WageClass { get; init; } = "skilled";
    /// <summary>The Phase 2 id this recipe was converted from, when it had to be renamed (the business book, saves and business kinds keep the old id).</summary>
    public string? Migrate { get; init; }
    /// <summary>"smithing" for the heat, forge, quench and temper steps (crafting-full 4.3).</summary>
    public string? Process { get; init; }
    /// <summary>Whether a craftsman takes it as a commission (default: item results only).</summary>
    public bool? Commission { get; init; }
    /// <summary>Phase 2: whether the batch rolls a quality tier.</summary>
    public bool BusinessQuality { get; init; } = true;
    public CraftRefitDef? Refit { get; init; }
    public CraftSalvageDef? Salvage { get; init; }
    /// <summary>Makes a new item of the target's shape and material from its components plus 10 percent more of its main material, at a new quality roll.</summary>
    public bool Reforge { get; init; }
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
    /// <summary>P5-E (crafting-full 3.2): the Phase 2 station kind this station is (the business book's <c>station</c>); null when the id is the kind.</summary>
    public string? Kind { get; init; }
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
    /// <summary>P5-E (decided with the user): a limited field kit: its qualities work anywhere, at a -5 score penalty when the kit is what supplies them.</summary>
    public bool FieldKit { get; init; }
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

/// <summary>
/// P5-E (crafting-full 3.4, 4.8): a guild of a trade. The row is a template: the world has one guild of it in each chartered town with at least three businesses of its trades (generated without a draw).
/// Its secrets are taught only by members, to members; joining needs residence, skill and the entry fee; dues each season.
/// </summary>
[ContentType("guild")]
public sealed record GuildDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>The settlement class it forms in ("town").</summary>
    public string SettlementClass { get; init; } = "town";
    /// <summary>Occupation names of the trade (Smith).</summary>
    public List<string> Trades { get; init; } = [];
    /// <summary>The skill of the trade (Smithing): the minimum skill is in it.</summary>
    public string Skill { get; init; } = "None";
    public List<string> Secrets { get; init; } = [];
    public long EntryFeeMites { get; init; } = 5000;
    public long DuesMitesPerSeason { get; init; } = 300;
    public int MinSkill { get; init; } = 40;
    public int ResidenceDays { get; init; } = 15;
    /// <summary>P7-ES1 (economies-of-scale 5.3): a member's shop of the trade inside the liberty may have at most this many journeymen, apprentices and stations of the trade, unless the lord licenses a manufactory.</summary>
    public int MaxJourneymen { get; init; } = 3;
    public int MaxApprentices { get; init; } = 2;
    public int MaxStations { get; init; } = 2;
}

/// <summary>P5-E (crafting-full 4.6, 4.7): the numbers of apprenticeships and commissions.</summary>
[ContentType("craft_settings")]
public sealed record CraftSettingsDef : ContentDef
{
    public int ApprenticeTermDays { get; init; } = 15;
    public int ApprenticeDaysPerRecipe { get; init; } = 2;
    public int ApprenticeHoursPerDay { get; init; } = 10;
    public int ApprenticeMaxPerMaster { get; init; } = 2;
    public int ApprenticeMasterMinSkill { get; init; } = 40;
    public int CommissionMarginPm { get; init; } = 1450;
    public int CommissionWindowDays { get; init; } = 15;
    public int MakerHoursPerDay { get; init; } = 10;
    public int MakerDayStartHour { get; init; } = 8;
    public int ReachMm { get; init; } = 6000;
    public int WalkMmPerSec { get; init; } = 1200;
    public int FetchHandleSec { get; init; } = 30;
}
