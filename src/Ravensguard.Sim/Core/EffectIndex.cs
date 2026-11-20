namespace Ravensguard.Sim.Core;

/// <summary>
/// The chronicle's effect index (chronicle-full 3, 4.4): for an entry id, the entries whose causes name it, in append order. Derived on append (so rebuilt at load from the entries),
/// never saved. "What followed" in the Chronicle screen reads it.
/// </summary>
public sealed class EffectIndex
{
    private readonly Dictionary<EntityId, List<EntityId>> _after = new();
    private static readonly IReadOnlyList<EntityId> None = [];

    public void Add(ChronicleEntry e)
    {
        foreach (var c in e.Causes)
        {
            if (c.EventId is not { } cause) continue;
            if (!_after.TryGetValue(cause, out var l)) _after[cause] = l = [];
            if (l.Count == 0 || !l[^1].Equals(e.Id)) l.Add(e.Id);       // one entry naming a cause twice is listed once
        }
    }

    /// <summary>The entries that followed <paramref name="cause"/>, in append order (empty when none).</summary>
    public IReadOnlyList<EntityId> After(EntityId cause) => _after.TryGetValue(cause, out var l) ? l : None;

    public int Roots => _after.Count;

    /// <summary>A stable digest of the index (sorted by cause id), for the load-equals-live test (CH-11).</summary>
    public ulong Hash()
    {
        ulong h = 1469598103934665603UL;
        foreach (var kv in _after.OrderBy(k => k.Key.Value))
        {
            h = (h ^ kv.Key.Value) * 1099511628211UL;
            foreach (var id in kv.Value) h = (h ^ id.Value) * 1099511628211UL;
        }
        return h;
    }
}
