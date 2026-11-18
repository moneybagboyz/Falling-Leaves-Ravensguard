namespace Ravensguard.Content;

/// <summary>
/// A systemic line (dialogue spec 3.1, <c>content/base/dialogue/lines_*.jsonc</c>): one whole localisable sentence under <see cref="Key"/>, chosen when every condition holds. The spec's <c>when</c> is
/// named <c>conditions</c> here so the vocabulary validator checks every id like it does for encounters and ambitions.
/// </summary>
[ContentType("line")]
public sealed record LineDef : ContentDef
{
    /// <summary>greeting, farewell, smalltalk, work_offer, trade, hire, recruit, directions, rumour, refusal, need, hostile.</summary>
    public required string Topic { get; init; }
    public int WeightPm { get; init; } = 1000;
    public List<ConditionRefDef> Conditions { get; init; } = [];
    public required string Key { get; init; }
    public List<string> Params { get; init; } = [];
    public List<ConditionRefDef> Effects { get; init; } = [];
}

/// <summary>An ambient line (dialogue spec 3.3): the same shape as a <see cref="LineDef"/> with the street's numbers (<c>radius_m</c>, <c>cooldown_s</c> in real seconds, <c>duration_s</c>, <c>channel</c>).</summary>
[ContentType("bark")]
public sealed record BarkDef : ContentDef
{
    public int WeightPm { get; init; } = 1000;
    public List<ConditionRefDef> Conditions { get; init; } = [];
    public required string Key { get; init; }
    public List<string> Params { get; init; } = [];
    public int RadiusM { get; init; } = 8;
    public int CooldownS { get; init; } = 60;
    public int DurationS { get; init; } = 3;
    /// <summary>ambient, market, guard, alarm, combat, animal.</summary>
    public string Channel { get; init; } = "ambient";
}

public sealed record DialogueChoiceDef
{
    public required string Text { get; init; }
    public required string Goto { get; init; }
    public List<ConditionRefDef> Conditions { get; init; } = [];
    public List<ConditionRefDef> Effects { get; init; } = [];
}

/// <summary>One node of an authored dialogue: what the speaker says, what it does, and where it goes (dialogue spec 3.4). A node with no choices and a <c>goto</c> advances on E; <c>end</c> closes.</summary>
public sealed record DialogueNodeDef
{
    public required string Text { get; init; }
    public List<ConditionRefDef> Conditions { get; init; } = [];
    public List<ConditionRefDef> Effects { get; init; } = [];
    public string Goto { get; init; } = "";
    public List<DialogueChoiceDef> Choices { get; init; } = [];
}

/// <summary>An authored dialogue of a unique NPC (<c>content/base/dialogue/unique/*.jsonc</c>).</summary>
[ContentType("dialogue")]
public sealed record DialogueDef : ContentDef
{
    public required string Speaker { get; init; }
    public required string Start { get; init; }
    /// <summary>The node a returning player starts at once any of <see cref="OnceFlags"/> is set (the spec's <c>repeat</c> node); empty: <see cref="Start"/>.</summary>
    public string Repeat { get; init; } = "";
    public required Dictionary<string, DialogueNodeDef> Nodes { get; init; }
    public List<string> OnceFlags { get; init; } = [];
}

/// <summary>
/// What a chronicle entry means to gossip (dialogue spec 3.5, <c>talk/rumours.jsonc</c>): its kind, how interesting it is (permille), how long it lives (game days) and who saw it.
/// <see cref="Match"/> narrows a row to entries whose data holds those pairs; the first matching row of an entry type wins.
/// </summary>
[ContentType("rumour_type")]
public sealed record RumourTypeDef : ContentDef
{
    /// <summary>The chronicle entry type id this row reads.</summary>
    public required string Entry { get; init; }
    public Dictionary<string, string> Match { get; init; } = [];
    public required string Kind { get; init; }
    public int InterestPm { get; init; } = 500;
    public int LifeDays { get; init; } = 8;
    /// <summary>household (the subject's household and four neighbours), elders (the reeve or priest and three elders), town (five townsfolk), traveller (innkeepers and merchants of the place, told by a traveller).</summary>
    public string Witnesses { get; init; } = "town";
    public int Severity { get; init; } = 1;
    /// <summary>The entry data keys that fill the sentence's {number}, {who} and {about} (empty: none).</summary>
    public string NumberKey { get; init; } = "";
    public string WhoKey { get; init; } = "";
    public string AboutKey { get; init; } = "";
    /// <summary>The en.csv key of the rumour's sentence (R_*): params place, number, who, about.</summary>
    public required string Key { get; init; }
}

/// <summary>The numbers of talk, mood and rumours that are not a line (dialogue spec 4.3, 4.6, 4.7, 9).</summary>
[ContentType("talk_settings")]
public sealed record TalkSettingsDef : ContentDef
{
    public int RecentSpeakerTurns { get; init; } = 8;
    public int RecentPairTurns { get; init; } = 24;
    public int FamiliarityPerTalk { get; init; } = 2;
    public int FamiliarityPerDayMax { get; init; } = 6;
    public int FamiliarityDecayDays { get; init; } = 15;
    public int RelationCap { get; init; } = 20000;
    public int BarkPersonCooldownS { get; init; } = 60;
    public int BarkGlobalPerSecond { get; init; } = 2;
    public int BarkVisibleMax { get; init; } = 3;
    // ---- mood (4.3)
    public int MoodFed { get; init; } = 20;
    public int MoodHoused { get; init; } = 10;
    public int MoodHungry { get; init; } = -30;
    public int MoodCold { get; init; } = -15;
    public int MoodCrowded { get; init; } = -10;
    public int MoodBereaved { get; init; } = -20;
    public int MoodLowBelow { get; init; } = -20;
    public int MoodHighAbove { get; init; } = 20;
    // ---- rumours (4.5, 4.6)
    public int HearPm { get; init; } = 600;
    public int FriendFactorPm { get; init; } = 1200;
    public int DistortPm { get; init; } = 50;
    public int MisattributePm { get; init; } = 10;
    public int GroupMin { get; init; } = 2;
    public int GroupMax { get; init; } = 6;
    /// <summary>Per game hour of the day (24 values): the chance in permille that an adult is out at the market, the well, the tavern or the church that hour.</summary>
    public List<int> SocialPmByHour { get; init; } = [];
    /// <summary>The chance in permille, per household and hour, that the household sits and talks together (early morning and evening hours).</summary>
    public int HomePm { get; init; } = 200;
    /// <summary>The chance in permille, per workplace and hour, that those working together stop to talk (working hours).</summary>
    public int WorkPm { get; init; } = 60;
    public int HearingsPerPersonMax { get; init; } = 6;
    public int HearingsMax { get; init; } = 120000;
    public int RumoursMax { get; init; } = 6000;
    public int NewsLagKmPerDay { get; init; } = 40;
    public int OverhearM { get; init; } = 6;
    public int PassLogMax { get; init; } = 256;
}
