namespace Ravensguard.Sim.Core;

/// <summary>
/// The Chronicle screen's logic without the widgets (chronicle-full 4.4, 6): which entries a view lists, the filter by family and the search, the entry opened with its Why (causes, back along
/// the chain) and What followed (the effect index, forward), and the trail Backspace walks back. The Godot screen only draws it; the tests drive it. Generic over <c>types.jsonc</c> and the
/// entries, so a type a later part adds shows up here with no change.
/// </summary>
public sealed class ChronicleBrowser
{
    public const int MaxRows = 3000;

    private readonly ChronicleView _v;
    private readonly ChronicleTypes _types;
    private readonly Func<ChronicleEntry, string> _text;
    private readonly Dictionary<EntityId, ChronicleEntry> _region = new();
    private readonly EffectIndex _effects = new();
    private readonly Dictionary<EntityId, string> _blob = new();
    private readonly Stack<EntityId> _trail = new();
    private List<ChronicleEntry> _rows = [];
    private int _matched;

    public ChronicleBrowser(ChronicleView view, ChronicleTypes types, Func<ChronicleEntry, string> text)
    {
        _v = view; _types = types; _text = text;
        foreach (var e in view.Entries) { _region[e.Id] = e; _effects.Add(e); }
        Refilter();
    }

    public ChronicleView View => _v;
    public ChronicleScope Scope { get; private set; } = ChronicleScope.MyLife;
    /// <summary>The family filter ("" for all).</summary>
    public string Family { get; private set; } = "";
    public string Search { get; private set; } = "";
    public ChronicleEntry? Current { get; private set; }
    /// <summary>The entries the view lists now, newest first, at most <see cref="MaxRows"/>.</summary>
    public IReadOnlyList<ChronicleEntry> Rows => _rows;
    /// <summary>How many entries matched before the cap.</summary>
    public int Matched => _matched;
    public int TrailDepth => _trail.Count;

    public string Text(ChronicleEntry e) => _text(e);

    public void SetScope(ChronicleScope s) { if (Scope == s) return; Scope = s; Refilter(); }
    public void SetFamily(string family) { if (Family == family) return; Family = family; Refilter(); }
    public void SetSearch(string search) { search = search.Trim(); if (Search == search) return; Search = search; Refilter(); }

    /// <summary>The next family filter: all, then each family in turn, then all again.</summary>
    public void CycleFamily()
    {
        int i = Family.Length == 0 ? -1 : Array.IndexOf(ChronicleFamilies.All, Family);
        SetFamily(i + 1 >= ChronicleFamilies.All.Length ? "" : ChronicleFamilies.All[i + 1]);
    }

    public void CycleScope() => SetScope((ChronicleScope)(((int)Scope + 1) % 4));

    /// <summary>An entry by id, in the region's chronicle or the history it continues.</summary>
    public ChronicleEntry? Resolve(EntityId id) => _region.GetValueOrDefault(id) ?? _v.History.Get(id);

    /// <summary>The entry with the longest chain of causes behind it, the region's (newest of a tie) before the history's; null when nothing has a cause that names an entry.</summary>
    public ChronicleEntry? Deepest()
    {
        var memo = new Dictionary<EntityId, int>();
        int Depth(ChronicleEntry e)
        {
            if (memo.TryGetValue(e.Id, out int d)) return d;
            memo[e.Id] = 0;                                                            // a cycle (none should exist) ends here
            var up = e.Causes.Select(c => c.EventId).FirstOrDefault(x => x != null && Resolve(x.Value) != null);
            return memo[e.Id] = up == null ? 0 : 1 + Depth(Resolve(up.Value)!);
        }
        ChronicleEntry? best = null; int bd = 1;
        foreach (var e in _v.Entries) { int d = Depth(e); if (d >= bd) { best = e; bd = d; } }
        if (best != null) return best;
        foreach (var e in _v.History.All) { int d = Depth(e); if (d > bd) { best = e; bd = d; } }
        return best;
    }

    public bool IsLegend(ChronicleEntry e) => !_region.ContainsKey(e.Id);

    /// <summary>The causes of an entry with the earlier entry each names (null for a note alone).</summary>
    public IReadOnlyList<(Cause Cause, ChronicleEntry? Entry)> Why(ChronicleEntry e) => e.Causes.Select(c => (c, c.EventId is { } id ? Resolve(id) : null)).ToList();

    /// <summary>What followed an entry: the region's entries that name it, then the history's, in the order they were written.</summary>
    public IReadOnlyList<ChronicleEntry> Followed(ChronicleEntry e)
    {
        var l = new List<ChronicleEntry>();
        foreach (var id in _effects.After(e.Id)) if (_region.TryGetValue(id, out var x)) l.Add(x);
        foreach (var id in _v.History.Effects.After(e.Id)) if (_v.History.Get(id) is { } x) l.Add(x);
        return l;
    }

    /// <summary>Opens an entry (pushing the one open now onto the trail), moving to the view and clearing the filters that would hide it. False when no such entry exists.</summary>
    public bool Open(EntityId id) => Open(id, push: true);

    private bool Open(EntityId id, bool push)
    {
        var e = Resolve(id);
        if (e == null) return false;
        if (push && Current != null && !Current.Id.Equals(e.Id)) _trail.Push(Current.Id);
        Current = e;
        if (!_rows.Any(r => r.Id.Equals(e.Id)))
        {
            Family = ""; Search = "";
            if (IsLegend(e)) Scope = ChronicleScope.Legends;
            else if (!InScope(e, Scope) || Scope == ChronicleScope.Legends) Scope = new[] { ChronicleScope.MyLife, ChronicleScope.Here, ChronicleScope.Realm }.FirstOrDefault(s => InScope(e, s), ChronicleScope.Realm);
            Refilter();
            if (!_rows.Any(r => r.Id.Equals(e.Id))) _rows.Insert(0, e);               // a chain's far end can lie outside every list: it is still shown
        }
        return true;
    }

    /// <summary>F: one step back along the Why (the first cause that names an entry, or the one at <paramref name="index"/> of those).</summary>
    public bool FollowCause(int index = 0)
    {
        if (Current == null) return false;
        var linked = Why(Current).Where(w => w.Entry != null).ToList();
        return index < linked.Count && Open(linked[index].Entry!.Id);
    }

    /// <summary>D: one step forward along What followed.</summary>
    public bool FollowEffect(int index = 0)
    {
        if (Current == null) return false;
        var f = Followed(Current);
        return index < f.Count && Open(f[index].Id);
    }

    /// <summary>Backspace: back to the entry opened before this one.</summary>
    public bool Back()
    {
        while (_trail.Count > 0)
        {
            if (Open(_trail.Pop(), push: false)) return true;
        }
        return false;
    }

    /// <summary>Selects an entry of the list without recording the step (a click or an arrow in the list).</summary>
    public void Select(EntityId id) { if (Resolve(id) is { } e) Current = e; }

    public bool InScope(ChronicleEntry e, ChronicleScope s)
    {
        switch (s)
        {
            case ChronicleScope.MyLife: return Mentions(e, _v.Mine);
            case ChronicleScope.Here:
                return Mentions(e, _v.Here) || (_v.HereName.Length > 0 && e.Data.TryGetValue("settlement", out var n) && n == _v.HereName);
            case ChronicleScope.Realm:
                return Mentions(e, _v.Realm) || Mentions(e, _v.Here) || ChronicleFamilies.Of(e) == "war";
            default: return !_region.ContainsKey(e.Id);
        }
    }

    private static bool Mentions(ChronicleEntry e, IReadOnlySet<ulong> ids)
    {
        if (ids.Count == 0) return false;
        foreach (var s in e.Subjects) if (ids.Contains(s.Value)) return true;
        return false;
    }

    private string Blob(ChronicleEntry e)
    {
        if (_blob.TryGetValue(e.Id, out var b)) return b;
        string subj = string.Join(' ', e.Subjects.Select(s => _v.NameOf(s.Value)));
        b = (_text(e) + " " + e.TypeId.Replace('_', ' ').Replace('.', ' ') + " " + subj + " " + e.When.Year).ToLowerInvariant();
        _blob[e.Id] = b;
        return b;
    }

    /// <summary>Re-lists the entries for the current view, family and search.</summary>
    public void Refilter()
    {
        var terms = Search.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rows = new List<ChronicleEntry>(); int matched = 0;
        IEnumerable<ChronicleEntry> source = Scope == ChronicleScope.Legends ? _v.History.All.Reverse() : _v.Entries.Reverse();
        foreach (var e in source)
        {
            if (Scope != ChronicleScope.Legends && !InScope(e, Scope)) continue;
            if (Family.Length > 0 && ChronicleFamilies.Of(e) != Family) continue;
            if (terms.Length > 0) { string b = Blob(e); if (terms.Any(t => !b.Contains(t, StringComparison.Ordinal))) continue; }
            matched++;
            if (rows.Count < MaxRows) rows.Add(e);
        }
        _rows = rows; _matched = matched;
    }
}
