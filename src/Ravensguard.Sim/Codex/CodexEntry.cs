using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Codex;

/// <summary>What a Codex entry is about (plan 3f: the Codex is the in-game manual; history legends appear in it from Phase 1).</summary>
public enum CodexCategory { Legend, Place, Person, Landmark, Rule }

/// <summary>
/// One Codex page. <see cref="Id"/> is stable ("legend.123", "realm.4", "dynasty.7", "person.31", "landmark.2", "rule.controls.world").
/// <see cref="Sources"/> are the chronicle event ids the page rests on (the first one is what the legends viewer opens). An entry stays locked until the
/// player has heard of it or met it; locked entries show only as "?" and are never searched.
/// </summary>
public sealed class CodexEntry
{
    public required string Id { get; init; }
    public required CodexCategory Category { get; init; }
    public required string Title { get; init; }
    public required string Text { get; init; }
    public IReadOnlyList<EntityId> Sources { get; init; } = [];
    /// <summary>Game year the entry is about, -1 when it has no date (sort key).</summary>
    public int Year { get; init; } = -1;
    public bool Unlocked { get; internal set; }
}

/// <summary>A manual page supplied by the game (the sim does not know the key bindings): a title and the text of the rule.</summary>
public sealed record CodexRule(string Id, string Title, string Text);

/// <summary>What the save keeps of the Codex: the ids of the unlocked entries (rules are always open and are not stored).</summary>
public sealed class CodexProgress
{
    public SortedSet<string> Unlocked { get; } = new(StringComparer.Ordinal);
}
