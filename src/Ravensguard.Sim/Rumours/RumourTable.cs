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
        _index[(rumour, h.Listener)] = list.Count; list.Add(h); HearingCount++;
        if (!_held.TryGetValue(h.Listener, out var held)) _held[h.Listener] = held = [];
        held.Add(rumour);
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
            HearingCount -= _hearings[id].Count; _hearings.Remove(id); _rumours.Remove(id);
        }
        return gone.Count;
    }

    /// <summary>Drops the hearings of people who are no longer there (dead, gone), keeping every rumour's roots for traces.</summary>
    public void Forget(Func<int, bool> gone)
    {
        foreach (var p in _held.Keys.Where(gone).ToList()) _held.Remove(p);
    }

    public void Clear() { _rumours.Clear(); _hearings.Clear(); _index.Clear(); _held.Clear(); HearingCount = 0; }

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
            foreach (var hr in _hearings[id]) { h = M(h, (ulong)(uint)hr.Listener); h = M(h, (ulong)(uint)hr.Teller); h = M(h, (ulong)(uint)hr.Hour); h = M(h, (ulong)(hr.Hop | (byte)hr.Flags << 8 | (byte)hr.How << 16 | (hr.Dropped ? 1 << 24 : 0))); }
        }
        return h;
    }
}
