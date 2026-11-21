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
    /// <summary>P7-IN5 (industries 5.12): the chain's first-tier buildings (a founder pays them; the tier plans of IN2 start at the second tier), the founders' reference sheet and where a works of the chain may be founded.</summary>
    public ChainBuildDef Build { get; init; } = new();
    public ChainSheetDef Sheet { get; init; } = new();
    /// <summary>The ore good a mine of the settlement must make (iron: <c>iron_ore</c>), and the business kinds that mark a place of the chain's trade; a settlement qualifies by any one of them. Both empty: anywhere.</summary>
    public string SiteMineral { get; init; } = "";
    public List<string> SiteKinds { get; init; } = [];
    /// <summary>The crew a founder sets at each stage (upstream first), the sheet's: a stage's target is clamped to its first tier's band.</summary>
    public List<int> Crew { get; init; } = [];
}

/// <summary>P7-IN5: the first-tier buildings of a chain: worker-hours at the settings' labour rate, materials bought from the place's market and built in (the sheets' base-metal licence has no rule behind it yet).</summary>
public sealed record ChainBuildDef
{
    public int LabourHours { get; init; }
    public List<StageAmountDef> Materials { get; init; } = [];
}

/// <summary>P7-IN5: a good and a mass in kilograms (a day's flow on a reference sheet, a material of a bill).</summary>
public sealed record StageAmountDef
{
    public required string Good { get; init; }
    public required int Kg { get; init; }
}

/// <summary>
/// P7-IN5 (industries 5.11): a founder's reference sheet of a well-run first-tier works, per working day: what it sells (at the gate's share of the book's mid) and buys (at the book's mid), the wages and other costs in mites, and the share of the year it works
/// (<c>work_days</c> of 60; the others cost <c>off_day_mites</c>). The expected payback is the buildings' bill over the net a day of this sheet at the place's prices; it is the same sheet as the worked examples of 5.11.
/// </summary>
public sealed record ChainSheetDef
{
    public List<StageAmountDef> Sells { get; init; } = [];
    public List<StageAmountDef> Buys { get; init; } = [];
    public int WagesMites { get; init; }
    public int OtherMites { get; init; }
    public int WorkDays { get; init; } = 60;
    public int OffDayMites { get; init; }
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

    // ---- P7-IN4 (industries 5.10): selling at the gate and your own caravan
    /// <summary>The caravan master's pay: a day's wage and a share of the sales (permille), and the Trading skill a master needs.</summary>
    public int MasterWageMites { get; init; } = 500;
    public int MasterSharePm { get; init; } = 20;
    public int MasterMinTrading { get; init; } = 40;
    /// <summary>A hired driver's and a guard's day on the road, mites (the caravan-guard danger premium, 1,500-2,000 a day).</summary>
    public int DriverWageMites { get; init; } = 150;
    public int GuardWageMites { get; init; } = 1750;
    /// <summary>Wear on the wagons: permille of their price a day on the road, paid to the region's wainwrights when the caravan is home.</summary>
    public int WagonWearPmPerDay { get; init; } = 3;
    /// <summary>The share of the cargo a successful ambush takes, permille (Phase 2's robbery: 50 to 100 percent of the goods, 60 is the usual).</summary>
    public int AmbushTakePm { get; init; } = 600;
    /// <summary>A standing order sends a caravan only when the stage holds at least this share of the fleet's load, permille.</summary>
    public int MinLoadPm { get; init; } = 600;
    /// <summary>Wagons a works' caravan may have, and the most a gate buyer holds of a good beyond its target cover (a border neighbour stops taking at this share of its cover, permille).</summary>
    public int MaxWagons { get; init; } = 8;
    public int NeighbourHoldPm { get; init; } = 2000;
    /// <summary>A price heard of a place loses this much of its worth a day of age, permille (the planner's rule), and the float a trip carries over its quoted costs, permille.</summary>
    public int PriceAgePmPerDay { get; init; } = 10;
    public int FloatPm { get; init; } = 1150;
    /// <summary>Days a stage's output is held back for a supply contract: the goods of this many deliveries stay in its store (the rest goes to the gate).</summary>
    public int ContractHoldDeliveries { get; init; } = 3;

    // ---- P7-IN5 (industries 5.12): NPC founders
    /// <summary>Whether households found works at all (the world rule behind the overview's Q6), the coin a household must hold to think of it, and the payback in game days it may expect at today's prices (3 years).</summary>
    public bool NpcFounders { get; init; } = true;
    public int NpcMinCapitalMites { get; init; } = 500_000;
    public int NpcMaxPaybackDays { get; init; } = 1080;
    /// <summary>Days between a household's looks at the trade (a working week), the most works the founders hold in the region, the days of the works' costs a founder puts into the purse as working capital, and the share of the owner's coin he keeps back, permille.</summary>
    public int NpcCheckDays { get; init; } = 6;
    public int NpcMaxWorks { get; init; } = 6;
    public int NpcCapitalDays { get; init; } = 60;
    public int NpcReservePm { get; init; } = 100;
    /// <summary>The share of a good's mid a gate sale gets on the founders' sheet, permille (the gate's bid after the spread, 5.11), and the days a works may run at a loss over its ring before its owner closes it.</summary>
    public int NpcGatePm { get; init; } = 920;
    public int NpcLossDays { get; init; } = 180;
    /// <summary>Game days between an NPC works' upgrades (one tier at most every 2 years) and the share of the owner's coin an upgrade may take, permille.</summary>
    public int NpcUpgradeGapDays { get; init; } = 720;
    public int NpcUpgradeMaxPm { get; init; } = 600;
    /// <summary>The days of a works' costs its purse keeps before the owner draws the rest as a dividend (a multiple of <c>npc_capital_days</c>, permille).</summary>
    public int NpcDividendPm { get; init; } = 1500;
}
