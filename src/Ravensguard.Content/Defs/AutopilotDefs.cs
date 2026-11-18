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
}
