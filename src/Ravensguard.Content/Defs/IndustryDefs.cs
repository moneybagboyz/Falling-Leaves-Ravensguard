namespace Ravensguard.Content;

/// <summary>
/// P7-IN1 (industries 3.1): a chain of staged production, its stage kinds in order (iron: mine, crusher and washer, ore yard, smeltery, packing). A stage is a business of the stage kind of the same id; the goods that flow between stages are the stage kinds' own.
/// </summary>
[ContentType("chain")]
public sealed record ChainDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>The stage kind ids, upstream first.</summary>
    public required List<string> Stages { get; init; }
}

/// <summary>
/// P7-IN1 (industries 3.1, 4.1, 5.1): one stage kind and its three tiers. <c>role</c> says what the day does: <c>extract</c> (makes the output good from the ground or the wood), <c>process</c> (turns the main good into the outputs, using the inputs),
/// <c>handle</c> (passes the main goods on, bounded by its crews and its own store) or <c>haul</c> (handle, with the rate over a reference distance). Rates are kilograms of the main good a worker-day at skill 50; money in mites.
/// </summary>
[ContentType("stage_kind")]
public sealed record StageKindDef : ContentDef
{
    public required string Name { get; init; }
    public required string Chain { get; init; }
    public required string Role { get; init; }
    /// <summary>The occupation of the hands that do the stage's main work (a hewer, a smith, a carpenter) and of the carriers that serve them when the tier has a <c>support_pm</c>.</summary>
    public required string Occupation { get; init; }
    public string SupportOccupation { get; init; } = "Labourer";
    /// <summary>The skill the speed curve reads; empty: the occupation's primary skill.</summary>
    public string Skill { get; init; } = "";
    /// <summary><c>extraction</c> (800 + 4 x skill permille) or <c>craft</c> (1,000,000 / (1,300 - 6 x skill) permille): 1,000 at skill 50 either way (5.1a).</summary>
    public string Curve { get; init; } = "extraction";
    /// <summary>The wage class the stage's skilled steps are paid at at most: unskilled, skilled or master (a smeltery pays its master 500, its journeymen 350, its hands 150).</summary>
    public string Wage { get; init; } = "unskilled";
    /// <summary>The main good: what a process stage consumes, what an extraction makes, what a handling stage passes on.</summary>
    public required string Main { get; init; }
    /// <summary>What a process stage uses besides the main good, in permille of the main kilograms (charcoal 1,500 for a smeltery); <c>fuel</c> goods stop the stage as <c>NoFuel</c>.</summary>
    public List<StageGoodDef> Inputs { get; init; } = [];
    /// <summary>What a process stage makes, in permille of the main kilograms; the rest of the main good is destroyed as <c>Waste</c> (tailings) or <c>Consumed</c> (burned).</summary>
    public List<StageGoodDef> Outputs { get; init; } = [];
    /// <summary>The permille of the main good a process stage destroys as <c>Waste</c> (a crusher's tailings); the remainder is <c>Consumed</c>.</summary>
    public int WastePm { get; init; }
    /// <summary>A process stage whose output share varies by batch (the crusher: 550 to 650 of the rough ore, stream 0x741): the output good's permille is drawn between these.</summary>
    public int YieldLoPm { get; init; }
    public int YieldHiPm { get; init; }
    /// <summary>A haul stage's reference distance in metres (the rates are over this).</summary>
    public int RefDistanceM { get; init; }
    /// <summary>A packing stage's container (5.9): the good, and the kilograms a container holds.</summary>
    public string Container { get; init; } = "";
    public int ContainerKg { get; init; } = 100;
    public required List<StageTierDef> Tiers { get; init; }
}

public sealed record StageGoodDef
{
    public required string Good { get; init; }
    public int PerMainPm { get; init; } = 1000;
    public bool Fuel { get; init; }
}

public sealed record StageTierDef
{
    public required int Tier { get; init; }
    public required int CrewMin { get; init; }
    public required int CrewMax { get; init; }
    /// <summary>Kilograms of the main good a worker-day.</summary>
    public required int Rate { get; init; }
    /// <summary>Carriers needed per 1,000 hewers (a mine: 500 = one hand per two hewers): the working hewers are at most <c>hands x 1,000,000 / support_pm</c> thousandths. 0: none.</summary>
    public int SupportPm { get; init; }
    /// <summary>The stage's own limit in kilograms a day (its stations and machines); 0: the crews alone.</summary>
    public int Capacity { get; init; }
    public int PowerW { get; init; }
    /// <summary>Kilograms the stage's own store holds.</summary>
    public required int StoreKg { get; init; }
    /// <summary>A multiplier in permille on the stage's inputs at this tier (a kiln's firewood falls with the tier); 1,000 when absent.</summary>
    public int InputPm { get; init; } = 1000;
    /// <summary>A haul tier that is not slowed by distance (floating down a river).</summary>
    public bool AnyDistance { get; init; }
    /// <summary>The blueprint and the pieces that make the tier (IN2); named here so the tables are one place.</summary>
    public string Plan { get; init; } = "";
    /// <summary>What must stand in the stage's premises for the tier (P7-IN2, 5.2): entries <c>piece</c>, <c>piece*N</c> (at least N) and <c>a|b</c> (either), all of which count (a tier also needs every lower tier's).</summary>
    public List<string> Recognise { get; init; } = [];
    /// <summary>Kilograms a day the stage still does when its machines are stopped (no water, a frozen wheel); -1: what the tier below can do.</summary>
    public int CapacityUnpowered { get; init; } = -1;
    /// <summary>A worker-day's risk of an accident in millionths (stream 0x740, 5.7).</summary>
    public int AccidentPpm { get; init; } = 200;
    /// <summary>Flood and wet days stop the stage (a mine's or a pit's lower tiers, 5.6).</summary>
    public bool Floods { get; init; }
    /// <summary>The share of the day's work done in winter, in permille (felling 800; frozen ground helps a sledge, 1,500; 1,000 when absent).</summary>
    public int WinterPm { get; init; } = 1000;
    /// <summary>A drying stage: days a batch must lie (6), and the thousandths of a day it ages in winter (0 for open hacks, 500 for sheds, 1,000 for hot floors).</summary>
    public int DryDays { get; init; }
    public int DryWinterPm { get; init; } = 1000;
    /// <summary>A kiln's losses a firing in permille of what it makes (stream 0x742, 5.1).</summary>
    public int LossLoPm { get; init; }
    public int LossHiPm { get; init; }
    /// <summary>A smeltery's finery (5.1): the share of the bloom that becomes bar in permille, and the grams of bloom a day it works.</summary>
    public int BarPm { get; init; }
    public int FineryKg { get; init; }
}

/// <summary>P7-IN1 (industries 4.1, 4.3, 5.12): the runtime's numbers.</summary>
[ContentType("industry_settings")]
public sealed record IndustrySettingsDef : ContentDef
{
    public int WorkdayHours { get; init; } = 10;
    /// <summary>Within this many metres the crews' own hands pass goods; beyond it a link needs carts (4.3).</summary>
    public int PassWithinM { get; init; } = 60;
    public int CartKg { get; init; } = 500;
    public int WagonKg { get; init; } = 1000;
    /// <summary>A cart covers this many metres a game hour; loading takes this many thousandths of an hour a trip (half for packed goods or with a crane).</summary>
    public int CartMetresPerHour { get; init; } = 450;
    public int LoadMilliHours { get; init; } = 1000;
    public int PackedLoadMilliHours { get; init; } = 500;
    /// <summary>A worker under 16 contributes this many permille.</summary>
    public int ApprenticePm { get; init; } = 500;
    /// <summary>Days of the balance ring (the payback's mean).</summary>
    public int RingDays { get; init; } = 60;
    /// <summary>Days a stop lasts before <c>works_stopped</c> is written (4.2).</summary>
    public int StopChronicleDays { get; init; } = 3;
    /// <summary>The generated mine's composite rate (5.12): the ore good it makes, the stage kinds whose first-tier rates make it, and the dressed share of the rough ore.</summary>
    public string CompositeGood { get; init; } = "iron_ore";
    public List<string> CompositeStages { get; init; } = [];
    public int CompositeYieldPm { get; init; } = 600;
    /// <summary>Packed goods load this much denser on a cart (5.9), in permille; a container is worn out a trip with these chances in permille (crates, dry casks 100; barrels 50).</summary>
    public int PackedDensityPm { get; init; } = 1100;
    public int ContainerWornPm { get; init; } = 100;
    /// <summary>The metres a stage's site may reach from its first corner (a stage's rectangle is at most this wide and tall).</summary>
    public int SiteMaxM { get; init; } = 48;
    /// <summary>The height of a fall an accident is worked out as (millimetres), by kind: rockfall, scalding, saw (5.7).</summary>
    public int AccidentFallMm { get; init; } = 3000;
    /// <summary>Mites a worker-hour of hand building is priced at when a tier plan's bill is added to the works' buildings cost (5.3a).</summary>
    public int PlanLabourMites { get; init; } = 30;
}
