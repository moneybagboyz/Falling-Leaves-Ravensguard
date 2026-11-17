namespace Ravensguard.Sim.People;

/// <summary>
/// Read-only kin queries over a <see cref="PersonTable"/> (parents, spouse, children, siblings, heirs). Built once from the
/// father/mother/spouse links; children are stored compressed, oldest first (ties by index).
/// </summary>
public sealed class KinGraph
{
    private readonly PersonTable _p;
    private readonly int[] _childStart;
    private readonly int[] _children;

    public KinGraph(PersonTable p)
    {
        _p = p;
        int n = p.Count;
        var counts = new int[n + 1];
        for (int i = 0; i < n; i++)
        {
            if (p.Father[i] >= 0) counts[p.Father[i]]++;
            if (p.Mother[i] >= 0 && p.Mother[i] != p.Father[i]) counts[p.Mother[i]]++;
        }
        _childStart = new int[n + 1];
        for (int i = 0; i < n; i++) _childStart[i + 1] = _childStart[i] + counts[i];
        _children = new int[_childStart[n]];
        var fill = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (p.Father[i] >= 0) _children[_childStart[p.Father[i]] + fill[p.Father[i]]++] = i;
            if (p.Mother[i] >= 0 && p.Mother[i] != p.Father[i]) _children[_childStart[p.Mother[i]] + fill[p.Mother[i]]++] = i;
        }
        for (int i = 0; i < n; i++)
            Array.Sort(_children, _childStart[i], _childStart[i + 1] - _childStart[i], Comparer<int>.Create(Older));
    }

    private int Older(int a, int b) => _p.BirthDay[a] != _p.BirthDay[b] ? _p.BirthDay[a].CompareTo(_p.BirthDay[b]) : a.CompareTo(b);

    public int Father(int i) => _p.Father[i];
    public int Mother(int i) => _p.Mother[i];
    public int Spouse(int i) => _p.Spouse[i];

    public IEnumerable<int> Parents(int i)
    {
        if (_p.Father[i] >= 0) yield return _p.Father[i];
        if (_p.Mother[i] >= 0) yield return _p.Mother[i];
    }

    /// <summary>Children, eldest first.</summary>
    public ReadOnlySpan<int> Children(int i) => new(_children, _childStart[i], _childStart[i + 1] - _childStart[i]);

    /// <summary>Full and half siblings, eldest first, excluding the person.</summary>
    public List<int> Siblings(int i)
    {
        var set = new List<int>();
        foreach (int par in Parents(i))
            foreach (int c in Children(par))
                if (c != i && !set.Contains(c)) set.Add(c);
        set.Sort(Older);
        return set;
    }

    /// <summary>Succession order for a holder: sons eldest first, then daughters eldest first, then the spouse, then the lord (employer).</summary>
    public List<int> HeirOrder(int i)
    {
        var res = new List<int>();
        var kids = Children(i);
        foreach (int c in kids) if (_p.Sex[c] == Sex.Male) res.Add(c);
        foreach (int c in kids) if (_p.Sex[c] == Sex.Female) res.Add(c);
        if (_p.Spouse[i] >= 0) res.Add(_p.Spouse[i]);
        if (_p.Employer[i] >= 0) res.Add(_p.Employer[i]);
        return res;
    }

    public int Heir(int i) { var o = HeirOrder(i); return o.Count > 0 ? o[0] : -1; }

    /// <summary>True when following parent links from some person returns to that person (never valid).</summary>
    public bool HasParentCycle()
    {
        int n = _p.Count;
        var state = new byte[n]; // 0 new, 1 on path, 2 done
        var stack = new Stack<(int Node, int Next)>();
        for (int s = 0; s < n; s++)
        {
            if (state[s] != 0) continue;
            stack.Push((s, 0)); state[s] = 1;
            while (stack.Count > 0)
            {
                var (node, next) = stack.Pop();
                int par = next == 0 ? _p.Father[node] : next == 1 ? _p.Mother[node] : -2;
                if (par == -2) { state[node] = 2; continue; }
                stack.Push((node, next + 1));
                if (par < 0) continue;
                if (state[par] == 1) return true;
                if (state[par] == 0) { state[par] = 1; stack.Push((par, 0)); }
            }
        }
        return false;
    }
}
