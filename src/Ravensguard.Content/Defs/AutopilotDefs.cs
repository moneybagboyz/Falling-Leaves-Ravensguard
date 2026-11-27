using System.Text.Json.Serialization;

namespace Ravensguard.Content;

/// <summary>One way to train a skill (docs/specs/phase3/autopilot-v1.md 3.3): a practice recipe, the gain per use (x100), the level the practice stops at and what it needs at the place.</summary>
public sealed record ActivityUseDef
{
    public required string Recipe { get; init; }
    [JsonPropertyName("d_x100")] public int DX100 { get; init; } = 40;
    public int CapLevel { get; init; } = 30;
    public List<string> Needs { get; init; } = [];
}

/// <summary>How the autopilot trains a skill: the practice uses up to their caps, then the best real work (<c>then</c>).</summary>
[ContentType("activity")]
public sealed record ActivityDef : ContentDef
{
    public required string Skill { get; init; }
    public List<ActivityUseDef> Uses { get; init; } = [];
    public string Then { get; init; } = "";
}

/// <summary>The autopilot's numbers (spec 3.3, 8 and 9): default limits, the risk presets, how long before a person counts as stuck, the summary hour and the log size.</summary>
[ContentType("autopilot_settings")]
public sealed record AutopilotSettingsDef : ContentDef
{
    public int DefaultMaxRiskPm { get; init; } = 500;
    public int StuckGameHours { get; init; } = 20;
    public int SummaryHour { get; init; } = 6;
    public int LogLinesMax { get; init; } = 2000;
    public int NotifySpeedDropS { get; init; } = 5;
    public int HomeByNightfallOffsetMin { get; init; }
    /// <summary>Default spend cap when autopilot is first turned on: 3 crowns a day (open question 1).</summary>
    public int DefaultSpendCapMitesPerDay { get; init; } = 300;
    public int DefaultPurseReserveMites { get; init; }
    public int RiskLowPm { get; init; } = 200;
    public int RiskMediumPm { get; init; } = 500;
    public int RiskHighPm { get; init; } = 800;
    /// <summary>How far from where they stand a job may be to take it (m).</summary>
    public int MaxCommuteM { get; init; } = 1350;
    /// <summary>An escort cuts the risk of a plan by this much (permille).</summary>
    public int EscortRiskCutPm { get; init; } = 400;
    /// <summary>Practice uses an hour of training (the crafting gain cap is 12).</summary>
    public int TrainUsesPerHour { get; init; } = 12;
    /// <summary>Days of food the hero keeps in the pack when shopping.</summary>
    public int FoodDaysInPack { get; init; } = 3;
    /// <summary>Hours the hero rests when a Rest directive has no end.</summary>
    public int RestMaxHours { get; init; } = 72;
    /// <summary>Utility bias (permille) a directive adds to the action it wants.</summary>
    public int DirectiveBiasPm { get; init; } = 400;
    /// <summary>P8-AT-2 (ambitions spec 3.3): how much a means' risk takes off its score (permille).</summary>
    public int AmbitionRiskWeightPm { get; init; } = 500;
    /// <summary>P8-AT-2: game days with no means scoring before <c>AmbitionStuck</c>, and days with the step's progress not moving.</summary>
    public int AmbitionZeroDays { get; init; } = 3;
    public int AmbitionStillDays { get; init; } = 10;
}

/// <summary>
/// P8-AT-2 (ambitions spec 3.3): one way the autopilot can move an ambition condition. <c>Directive</c> is the child it runs (<c>work</c>, <c>trade_route</c>, <c>tend</c>, <c>train</c>, <c>contract</c>, <c>buy</c>, <c>recruit</c>, <c>steal</c>, or
/// <c>none</c> for a condition with no way the AI can take yet). The score is <c>rate x weight_pm / 1000 - risk_pm x risk weight / 1000</c>; <c>prior_pm_day</c> is the rate until the means has been tried.
/// </summary>
[ContentType("means")]
public sealed record MeansDef : ContentDef
{
    public required string Condition { get; init; }
    public required string Directive { get; init; }
    public int PriorPmDay { get; init; }
    public int WeightPm { get; init; } = 1000;
    public int RiskPm { get; init; }
    /// <summary>What the means spends in mites in one go (a purchase); 0 for none. Checked against the spending cap.</summary>
    public long SpendMites { get; init; }
    /// <summary>Tags: <c>crime</c> (removed by "no crimes"), <c>far</c> (a long trip: removed by "home by nightfall").</summary>
    public List<string> Tags { get; init; } = [];
    /// <summary>A skill (snake_case) and the level the person needs to use the means; empty for none.</summary>
    public string NeedsSkill { get; init; } = "";
    public int NeedsLevel { get; init; }
}
