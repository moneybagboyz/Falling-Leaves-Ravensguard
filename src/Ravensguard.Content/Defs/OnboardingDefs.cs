using System.Text.Json.Serialization;

namespace Ravensguard.Content;

/// <summary>When a hint may fire (onboarding spec 3.1): one event of the closed list, an optional kind it must be about and the smallest value it must reach (a need in permille, a ratio, a distance).</summary>
public sealed record HintTriggerDef
{
    public required string Event { get; init; }
    public Dictionary<string, string> Args { get; init; } = [];
}

/// <summary>
/// A contextual one-line hint (onboarding spec 3.1, plan 3f): shown once per profile when its trigger happens, with the keys it names resolved from the live bindings (<c>{@action.id}</c>) and a link into the Codex.
/// <c>level</c> is <c>minimal</c> (the 12 essentials, shown at minimal and full) or <c>full</c> (shown at full only).
/// </summary>
[ContentType("hint")]
public sealed record HintDef : ContentDef
{
    public string Level { get; init; } = "full";
    public required HintTriggerDef Trigger { get; init; }
    /// <summary>Localisation key of the one-line text.</summary>
    public required string Text { get; init; }
    /// <summary>The Codex rule page the "learn more" link opens.</summary>
    public string LearnMore { get; init; } = "";
    public int MaxPerGame { get; init; } = 1;
    /// <summary>Higher shows first when several wait.</summary>
    public int Priority { get; init; } = 20;
}

/// <summary>
/// The first hour of one origin (onboarding spec 3.5): the mentor's small contract (an odd job through the job system), the lent animal and the errand it carries the hero on. <c>id</c> is the origin id.
/// Distances are in metres; the job pays the wage the job system works out, never more (the mentor gives no gifts beyond the loan).
/// </summary>
[ContentType("first_hour")]
public sealed record FirstHourDef : ContentDef
{
    [ContentRef(typeof(OriginDef))] public string Origin { get; init; } = "";
    /// <summary>The unique dialogue's speaker id (matches the origin's <c>mentor</c>).</summary>
    public required string Mentor { get; init; }
    /// <summary>Haul, Guard, Deliver, Fetch: an <c>OddJobKind</c> name.</summary>
    public required string JobKind { get; init; }
    public int JobDistanceM { get; init; } = 300;
    public int JobHours { get; init; } = 2;
    public int JobKg { get; init; } = 12;
    public required string JobTitle { get; init; }
    /// <summary>The lent animal's breed (species id, e.g. horse_palfrey, mule, donkey).</summary>
    public required string LoanBreed { get; init; }
    public int RideMinM { get; init; } = 2000;
    public int RideMaxM { get; init; } = 6000;
    public required string ErrandTitle { get; init; }
    /// <summary>Where the animal is to be returned: <c>start</c> (the mentor's place) or <c>errand</c> (the stable at the errand's end).</summary>
    public string ReturnAt { get; init; } = "start";
}

/// <summary>The numbers of the first hour and the practice yard (onboarding spec 3.4, 3.6, 4.1), one row so a mod can tune them.</summary>
[ContentType("onboarding_settings")]
public sealed record OnboardingSettingsDef : ContentDef
{
    public int WarSafeM { get; init; } = 10_000;
    public int LairSafeM { get; init; } = 3_000;
    public int LoanDays { get; init; } = 3;
    public int OverdueGraceDays { get; init; } = 3;
    public int OverdueTrustPm { get; init; } = -300;
    public int ReturnTrustPm { get; init; } = 50;
    public int MinWageMites { get; init; } = 40;
    public int JobDeadlineHours { get; init; } = 12;
    public int MountedSharePm { get; init; } = 600;
    public int MinRideM { get; init; } = 2000;
    public int FirstNightHours { get; init; } = 6;
    [JsonPropertyName("dummy_d_x100")] public int DummyDX100 { get; init; } = 40;
    [JsonPropertyName("spar_d_x100")] public int SparDX100 { get; init; } = 60;
    public int YardCapLevel { get; init; } = 30;
    public int SparCooldownMin { get; init; } = 5;
    public int YardMinPopulation { get; init; } = 1000;
    /// <summary>The yard lies this many metres beyond the settlement's farthest house.</summary>
    public int YardBeyondM { get; init; } = 30;
    public int SafeDays { get; init; } = 2;
    public int FullHintGameMinutes { get; init; } = 60;
}
