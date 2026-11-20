namespace Ravensguard.Content;

/// <summary>
/// What a chronicle entry does to the standing of the person who did it (reputation spec 3, <c>society/deeds.jsonc</c>): a chronicle type, optionally narrowed by <see cref="Match"/> like a rumour
/// row, and the standing it moves in permille once every member of a group has heard of it (the group's share who have heard scales it). The doer is the entry's first person subject; the
/// settlement is where the entry happened (or, with none, where the doer lives), and its fief and realm follow.
/// </summary>
[ContentType("deed")]
public sealed record DeedDef : ContentDef
{
    /// <summary>The chronicle entry type id this row reads.</summary>
    public required string Entry { get; init; }
    public Dictionary<string, string> Match { get; init; } = [];
    /// <summary>The standing moved, in permille, once everyone in the group has heard (negative: disgrace).</summary>
    public int SettlementPm { get; init; }
    public int FiefPm { get; init; }
    public int RealmPm { get; init; }
    public int ReligionPm { get; init; }
    /// <summary>The entry data key that scales the row (the mites of alms), empty: a flat row.</summary>
    public string PerKey { get; init; } = "";
    /// <summary>The amount of <see cref="PerKey"/> the deltas are for (100 mites of alms), at least 1.</summary>
    public int PerUnit { get; init; } = 1;
    /// <summary>The most units of <see cref="PerKey"/> counted (a cap on alms), 0: no cap.</summary>
    public int PerMax { get; init; }
}
