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
    public List<string> Recognise { get; init; } = [];
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
}
