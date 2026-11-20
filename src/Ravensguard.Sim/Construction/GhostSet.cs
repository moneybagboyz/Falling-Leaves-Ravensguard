using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Construction;

/// <summary>
/// P5-D: one site's live ghosts in ascending id order, held directly (no lookup in the book's sorted table per ghost), walked by index so the work engine's picks and refreshes
/// allocate nothing. Ghost ids only grow, so an add is almost always an append.
/// </summary>
public sealed class GhostSet
{
    private readonly List<Ghost> _g = [];
    public int Count => _g.Count;
    public Ghost this[int i] => _g[i];

    public void Add(Ghost g)
    {
        if (_g.Count == 0 || _g[^1].Id.CompareTo(g.Id) < 0) { _g.Add(g); return; }
        int i = Find(g.Id);
        if (i >= 0) { _g[i] = g; return; }
        _g.Insert(~i, g);
    }

    public bool Remove(EntityId id)
    {
        int i = Find(id);
        if (i < 0) return false;
        _g.RemoveAt(i);
        return true;
    }

    private int Find(EntityId id)
    {
        int lo = 0, hi = _g.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int c = _g[mid].Id.CompareTo(id);
            if (c == 0) return mid;
            if (c < 0) lo = mid + 1; else hi = mid - 1;
        }
        return ~lo;
    }

    public List<Ghost>.Enumerator GetEnumerator() => _g.GetEnumerator();
    public IEnumerable<Ghost> AsEnumerable() => _g;
}
