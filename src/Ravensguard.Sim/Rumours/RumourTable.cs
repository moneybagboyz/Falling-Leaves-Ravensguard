using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Rumours;

/// <summary>
/// The rumours of the world and the hearings people hold of them (spec 3.5, caps in 9). Hearings are kept per rumour in the order they were made (so a trace can always be walked back),
/// with an index of who holds what; a person's six best hearings stay tellable and the rest are marked dropped but kept for their traces until the rumour is purged.
/// </summary>
public sealed class RumourTable
{
    private readonly SortedDictionary<EntityId, Rumour> _rumours = [];
    private readonly Dictionary<EntityId, List<Hearing>> _hearings = [];
    private readonly Dictionary<(EntityId, int), int> _index = [];
    private readonly Dictionary<int, List<EntityId>> _held = [];
    public int HearingCount { get; private set; }

    // ---- P6-A3 (reputation 4.1): hearings counted per home settlement of the listener, kept incrementally (a view: never saved, never hashed; rebuilt after a load)
    private readonly Dictionary<EntityId, SortedDictionary<int, int>> _heardBy = [];
    private readonly Dictionary<int, List<EntityId>> _heardIds = [];
    private readonly SortedSet<EntityId> _touched = [];
    private bool _countsStale;
    /// <summary>Where a person lives (their home settlement index, -1 unknown); set by the talk hour so hearings can be counted per settlement.</summary>
    public Func<int, int>? HomeOf { get; set; }
    /// <summary>Hearings of a rumour by the listener's home settlement (ascending index); empty when none counted.</summary>
    public IReadOnlyDictionary<int, int> HeardBySettlement(EntityId rumour) => _heardBy.TryGetValue(rumour, out var d) ? d : EmptyCounts;
    private static readonly SortedDictionary<int, int> EmptyCounts = [];
    /// <summary>Rumours that gained hearings (or lost them to a move) since the last drain, ascending id.</summary>
    public List<EntityId> DrainTouched() { var l = new List<EntityId>(_touched); _touched.Clear(); return l; }
    /// <summary>The rumours this person has heard, tellable or dropped, in the order heard.</summary>
    public IReadOnlyList<EntityId> HeardIds(int person) => _heardIds.TryGetValue(person, out var l) ? l : [];

    private void CountHeard(EntityId rumour, int listener, int s)
    {
        _heardIds.TryAdd(listener, []); _heardIds[listener].Add(rumour);
        if (s < 0) return;
        if (!_heardBy.TryGetValue(rumour, out var d)) _heardBy[rumour] = d = [];
        d[s] = d.GetValueOrDefault(s) + 1; _touched.Add(rumour);
    }

    /// <summary>Counts the hearings again from the stored rows (after a load, or when <see cref="HomeOf"/> was first set): the same numbers the increments would have made.</summary>
    public void EnsureCounts(Func<int, int> homeOf)
    {
        bool first = HomeOf == null; HomeOf = homeOf;
        if (!_countsStale && !first) return;
        _heardBy.Clear(); _heardIds.Clear(); _countsStale = false;
        foreach (var (id, list) in _hearings) foreach (var h in list) CountHeard(id, h.Listener, h.HomeAt > 0 ? h.HomeAt - 1 : homeOf(h.Listener));
        _touched.Clear();
    }

    /// <summary>A person moved home (RP-9): every deed they had heard counts for the new settlement instead of the old one.</summary>
    public void Relocate(int person, int from, int to)
    {
        if (from == to || !_heardIds.TryGetValue(person, out var ids)) return;
        foreach (var id in ids)
        {
            if (!_heardBy.TryGetValue(id, out var d)) _heardBy[id] = d = [];
            if (from >= 0 && d.TryGetValue(from, out int n)) { if (n <= 1) d.Remove(from); else d[from] = n - 1; }
            if (to >= 0) d[to] = d.GetValueOrDefault(to) + 1;
            _touched.Add(id);
        }
    }

    public IReadOnlyDictionary<EntityId, Rumour> Rumours => _rumours;
    public int Count => _rumours.Count;
    public Rumour? Get(EntityId id) => _rumours.GetValueOrDefault(id);
    public IReadOnlyList<Hearing> HearingsOf(EntityId id) => _hearings.TryGetValue(id, out var l) ? l : [];
    public IReadOnlyDictionary<int, List<EntityId>> Held => _held;
    /// <summary>Rumours a person holds as tellable, in the order they were heard.</summary>
    public IReadOnlyList<EntityId> HeldBy(int person) => _held.TryGetValue(person, out var l) ? l : [];

    public bool Add(Rumour r) { if (_rumours.ContainsKey(r.Id)) return false; _rumours.Add(r.Id, r); _hearings[r.Id] = []; return true; }

    public Hearing? Find(EntityId rumour, int person) => _index.TryGetValue((rumour, person), out int i) ? _hearings[rumour][i] : null;
    public bool Holds(EntityId rumour, int person) => _index.ContainsKey((rumour, person));

    /// <summary>Records a hearing; false when the person already holds the rumour or the rumour is not there.</summary>
    public bool AddHearing(EntityId rumour, Hearing h, int perPersonMax)
    {
        if (!_hearings.TryGetValue(rumour, out var list) || _index.ContainsKey((rumour, h.Listener))) return false;
        int home = HomeOf?.Invoke(h.Listener) ?? -1; h.HomeAt = (short)(home + 1);
        _index[(rumour, h.Listener)] = list.Count; list.Add(h); HearingCount++;
        if (!_held.TryGetValue(h.Listener, out var held)) _held[h.Listener] = held = [];
        held.Add(rumour); CountHeard(rumour, h.Listener, home);
        if (held.Count > perPersonMax) DropWeakest(h.Listener, held);
        return true;
    }

    // the lowest-interest tellable hearing (ties: the oldest rumour id) stops being tellable
    private void DropWeakest(int person, List<EntityId> held)
    {
        int worst = 0;
        for (int i = 1; i < held.Count; i++)
        {
            var a = _rumours[held[i]]; var b = _rumours[held[worst]];
            if (a.InterestPm < b.InterestPm || (a.InterestPm == b.InterestPm && held[i].Value < held[worst].Value)) worst = i;
        }
        var id = held[worst]; held.RemoveAt(worst);
        int idx = _index[(id, person)]; var l = _hearings[id]; var h = l[idx]; h.Dropped = true; l[idx] = h;
    }

    /// <summary>Removes the rumour and every hearing of it, except those <paramref name="keep"/> says to keep (the hero's).</summary>
    public int Purge(Func<Rumour, bool> expired, Func<EntityId, bool> keep)
    {
        List<EntityId>? gone = null;
        foreach (var (id, r) in _rumours) if (expired(r) && !keep(id)) (gone ??= []).Add(id);
        if (gone == null) return 0;
        foreach (var id in gone)
        {
            foreach (var h in _hearings[id]) { _index.Remove((id, h.Listener)); if (_held.TryGetValue(h.Listener, out var l)) { l.Remove(id); if (l.Count == 0) _held.Remove(h.Listener); } }
            foreach (var h in _hearings[id]) if (_heardIds.TryGetValue(h.Listener, out var hi)) hi.Remove(id);
            _heardBy.Remove(id); _touched.Remove(id);
            HearingCount -= _hearings[id].Count; _hearings.Remove(id); _rumours.Remove(id);
        }
        return gone.Count;
    }

    /// <summary>Drops the hearings of people who are no longer there (dead, gone), keeping every rumour's roots for traces.</summary>
    public void Forget(Func<int, bool> gone)
    {
        foreach (var p in _held.Keys.Where(gone).ToList()) _held.Remove(p);
    }

    public void Clear() { _rumours.Clear(); _hearings.Clear(); _index.Clear(); _held.Clear(); HearingCount = 0; _heardBy.Clear(); _heardIds.Clear(); _touched.Clear(); _countsStale = true; }

    /// <summary>Rebuilds the indexes from stored rows (hearings in creation order).</summary>
    public void Restore(IEnumerable<Rumour> rumours, IEnumerable<(EntityId Rumour, Hearing H)> hearings)
    {
        Clear();
        foreach (var r in rumours) { _rumours[r.Id] = r; _hearings[r.Id] = []; }
        foreach (var (id, h) in hearings)
        {
            var list = _hearings[id]; _index[(id, h.Listener)] = list.Count; list.Add(h); HearingCount++;
            if (!h.Dropped) { if (!_held.TryGetValue(h.Listener, out var held)) _held[h.Listener] = held = []; held.Add(id); }
        }
    }

    /// <summary>A 64-bit digest of every rumour and hearing in ascending order: equal tables give equal digests.</summary>
    public ulong Hash()
    {
        ulong h = 0x9E3779B97F4A7C15UL;
        static ulong M(ulong a, ulong b) => DetRng.Mix(a ^ (b + 0x9E3779B97F4A7C15UL));
        foreach (var (id, r) in _rumours)
        {
            h = M(h, id.Value); h = M(h, r.Source.Value); h = M(h, (ulong)r.Kind); h = M(h, (ulong)(uint)r.Settlement); h = M(h, (ulong)(uint)r.BornHour); h = M(h, (ulong)(uint)r.ExpiresDay);
            h = M(h, (ulong)(uint)r.Number); h = M(h, r.FactsMask); foreach (var c in r.Place) h = M(h, c);
            if (r.False) h = M(h, 0xFA15EUL);                      // P6-C2: the false flag, only for a lie (so a world with none keeps its hash)
            foreach (var hr in _hearings[id]) { h = M(h, (ulong)(uint)hr.Listener); h = M(h, (ulong)(uint)hr.Teller); h = M(h, (ulong)(uint)hr.Hour); h = M(h, (ulong)(hr.Hop | (byte)hr.Flags << 8 | (byte)hr.How << 16 | (hr.Dropped ? 1 << 24 : 0))); }
        }
        return h;
    }
}
