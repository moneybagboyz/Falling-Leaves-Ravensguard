namespace Ravensguard.Sim.Core;

public enum CauseKind { Fire, Raid, Battle, Crime, Wound, Death, Law, Famine, MineFlood, War, Weather, MarketDrift, Unknown, Other,
    // Phase 2 (overview 2.4): append only, never renumber
    Tax, Trade, Disease, Migration }

/// <summary>Why something happened; may reference another chronicle entry.</summary>
public sealed record Cause(CauseKind Kind, EntityId? EventId = null, string Note = "");

public sealed record ChronicleEntry(
    EntityId Id,
    GameTime When,
    string TypeId,
    IReadOnlyList<EntityId> Subjects,
    IReadOnlyList<Cause> Causes,
    IReadOnlyDictionary<string, string> Data);

/// <summary>Append-only store of chronicle entries.</summary>
public sealed class Chronicle
{
    private readonly Dictionary<EntityId, ChronicleEntry> _byId = new();
    private readonly List<ChronicleEntry> _all = new();

    public int Count => _all.Count;
    public IReadOnlyList<ChronicleEntry> All => _all;

    public void Add(ChronicleEntry entry)
    {
        if (!_byId.TryAdd(entry.Id, entry)) throw new ArgumentException($"Duplicate chronicle id {entry.Id}.");
        _all.Add(entry);
    }

    public ChronicleEntry? Get(EntityId id) => _byId.GetValueOrDefault(id);

    public IEnumerable<ChronicleEntry> ByType(string typeId) => _all.Where(e => e.TypeId == typeId);

    /// <summary>Fraction of entries with at least one cause other than MarketDrift/Unknown (1 when empty).</summary>
    public double CauseCoverage()
    {
        if (_all.Count == 0) return 1.0;
        int good = _all.Count(e => e.Causes.Any(c => c.Kind != CauseKind.MarketDrift && c.Kind != CauseKind.Unknown));
        return (double)good / _all.Count;
    }
}
