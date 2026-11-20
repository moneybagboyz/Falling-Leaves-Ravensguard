using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Society;

/// <summary>One counted deed (reputation 4.1): what was done, by whom, where and when, and how many people of each settlement have heard of it. Kept after the rumour itself expires, so a murder is remembered.</summary>
public sealed class DeedRecord
{
    /// <summary>The chronicle entry (and the rumour's id).</summary>
    public EntityId Entry;
    public EntityId Doer;
    /// <summary>The deed row's content id (<c>society/deeds.jsonc</c>); an id the content no longer has counts for nothing.</summary>
    public string DeedId = "";
    public int Day;
    /// <summary>The settlement it happened in (-1: unknown).</summary>
    public int Place = -1;
    /// <summary>How much of the row it carries (see <see cref="DeedTable.AmountOf"/>).</summary>
    public int Amount = 1;
    /// <summary>What share of it the doer still carries, in permille: 1000, or 500 once a kin heir took the name up (4.4), 0 for a companion heir.</summary>
    public int WeightPm = 1000;
    /// <summary>Hearings by the listener's home settlement (ascending), the last count the rumour table gave.</summary>
    public SortedDictionary<int, int> Heard = [];
    public int HeardTotal => Heard.Values.Sum();
}

/// <summary>A person's standing with a group (reputation 3): the value in permille, how many of the group's members have heard a counted deed, and the day of the newest one.</summary>
public struct StandingRow
{
    public int ValuePm;
    public int Heard;
    public int LastDay;
}

/// <summary>A person's renown (reputation 3): the share of the region's adults who have heard any deed of theirs (permille), worn down 5% a season once a year has passed without a new deed.</summary>
public struct RenownRow
{
    /// <summary>The wider-known share before the decay, permille.</summary>
    public int SharePm;
    /// <summary>The decay factor, permille (1000 when fresh).</summary>
    public int DecayPm;
    public int LastDeedDay;
    public readonly int Pm => (int)((long)SharePm * DecayPm / 1000);
}

/// <summary>One deed's part in a standing (the "why" of the Standing tab).</summary>
public readonly record struct Contribution(EntityId Entry, string DeedId, int Day, int ValuePm);

/// <summary>
/// What groups think of people, and why (reputation spec 3, 4): the counted deeds, the standing rows computed from the people who have heard of them, renown, and the marks. A person's value with a
/// group is the sum over their deeds of <c>delta x heard / members</c> (integer permille, ascending entry id), aged by the game year; nobody keeps a private reputation, everything reads
/// <see cref="Standing.Of"/>. Saved in the optional "standing" section. No random draw.
/// </summary>
public sealed class StandingBook
{
    /// <summary>A contribution counts fully for a game year, then 10% less each further year, never below 30% (4.1).</summary>
    public const int FullYears = 1, AgeStepPct = 10, AgeFloorPct = 30;
    /// <summary>A season without a new deed past a year wears renown by 5% (3).</summary>
    public const int RenownDecayPct = 5;
    /// <summary>The share of a dead hero's name that passes to a kin heir, permille (4.4, T2).</summary>
    public const int KinInheritPm = 500;

    public readonly SortedDictionary<ulong, DeedRecord> Deeds = [];
    public readonly SortedDictionary<(ulong Person, uint Group), StandingRow> Rows = [];
    public readonly SortedDictionary<ulong, RenownRow> Renowns = [];
    public readonly MarkBook Marks = new();
    /// <summary>The day the daily pass last ran (-1: never).</summary>
    public int LastDay = -1;

    private readonly Dictionary<ulong, List<ulong>> _byDoer = [];

    public bool IsEmpty => Deeds.Count == 0 && Rows.Count == 0 && Renowns.Count == 0 && Marks.IsEmpty;
    public int RowCount => Rows.Count;

    // ------------------------------------------------------------------ deeds

    public DeedRecord? Deed(EntityId entry) => Deeds.GetValueOrDefault(entry.Value);

    public DeedRecord AddDeed(DeedRecord r)
    {
        Deeds[r.Entry.Value] = r;
        if (!_byDoer.TryGetValue(r.Doer.Value, out var l)) _byDoer[r.Doer.Value] = l = [];
        if (!l.Contains(r.Entry.Value)) { l.Add(r.Entry.Value); l.Sort(); }
        return r;
    }

    public IReadOnlyList<ulong> DeedsOf(EntityId doer) => _byDoer.TryGetValue(doer.Value, out var l) ? l : [];
    public IEnumerable<EntityId> Doers => _byDoer.Keys.Order().Select(v => new EntityId(v));

    /// <summary>Rebuilds the by-doer index (after a load).</summary>
    public void Reindex()
    {
        _byDoer.Clear();
        foreach (var r in Deeds.Values) { if (!_byDoer.TryGetValue(r.Doer.Value, out var l)) _byDoer[r.Doer.Value] = l = []; l.Add(r.Entry.Value); }
        foreach (var l in _byDoer.Values) l.Sort();
    }

    // ------------------------------------------------------------------ the numbers

    /// <summary>The age factor of a deed, in percent (4.1): 100 for a game year, then 10 fewer each year, never below 30.</summary>
    public static int AgePct(int ageDays)
    {
        int years = Math.Max(0, ageDays) / TimeConst.DaysPerYear;
        return Math.Max(AgeFloorPct, 100 - AgeStepPct * Math.Max(0, years - FullYears));
    }

    /// <summary>One deed's permille part in a group's value: <c>delta x amount/unit x heard/members x age x weight</c>, truncated toward zero.</summary>
    public static int Part(int deltaPm, int amount, int perUnit, int heard, int members, int agePct, int weightPm)
    {
        if (deltaPm == 0 || members <= 0 || heard <= 0 || weightPm == 0) return 0;
        heard = Math.Min(heard, members);
        long num = (long)deltaPm * amount * heard * agePct * weightPm, den = (long)Math.Max(1, perUnit) * members * 100 * 1000;
        return (int)(num / den);
    }

    /// <summary>Every group a person has a counted deed with, ascending: the deed's settlement, its fief and its realm, where its row moves them.</summary>
    private IEnumerable<GroupRef> GroupsOf(EntityId doer, DeedTable deeds, IGroupMap map)
    {
        var set = new SortedSet<GroupRef>();
        foreach (var key in DeedsOf(doer))
        {
            var rec = Deeds[key]; var row = deeds.Get(rec.DeedId); if (row == null) continue;
            foreach (var g in PlaceGroups(rec, map)) if (DeedTable.DeltaFor(row, g.Kind) != 0) set.Add(g);
        }
        return set;
    }

    private static IEnumerable<GroupRef> PlaceGroups(DeedRecord rec, IGroupMap map)
    {
        int place = rec.Place >= 0 ? rec.Place : map.HomeOf(rec.Doer);
        if (place < 0) yield break;
        yield return GroupRef.Settlement(place);
        var f = map.FiefOf(place); if (!f.IsNone) yield return f;
        var r = map.RealmOf(place); if (!r.IsNone) yield return r;
    }

    private static int HeardIn(DeedRecord rec, GroupRef g, IGroupMap map)
    {
        if (g.Kind == GroupKind.Settlement) return rec.Heard.GetValueOrDefault(g.Index);
        int n = 0; foreach (int s in map.SettlementsOf(g)) n += rec.Heard.GetValueOrDefault(s);
        return n;
    }

    /// <summary>The deeds that make up a person's standing with a group, largest effect first (the Standing tab's "why", reputation 6).</summary>
    public List<Contribution> Why(EntityId doer, GroupRef g, DeedTable deeds, IGroupMap map, int day, int top = 3)
    {
        var list = new List<Contribution>();
        int members = map.Members(g);
        foreach (var key in DeedsOf(doer))
        {
            var rec = Deeds[key]; var row = deeds.Get(rec.DeedId); if (row == null || !PlaceGroups(rec, map).Contains(g)) continue;
            int v = Part(DeedTable.DeltaFor(row, g.Kind), rec.Amount, row.PerUnit, HeardIn(rec, g, map), members, AgePct(day - rec.Day), rec.WeightPm);
            if (v != 0) list.Add(new Contribution(rec.Entry, rec.DeedId, rec.Day, v));
        }
        return [.. list.OrderByDescending(c => Math.Abs(c.ValuePm)).ThenBy(c => c.Entry.Value).Take(top)];
    }

    /// <summary>Recomputes one person's rows and renown from their deeds (H5 and whenever hearings changed): sum over deeds in ascending entry id, clamped to -1000..1000.</summary>
    public void Recompute(EntityId doer, DeedTable deeds, IGroupMap map, int day)
    {
        var groups = GroupsOf(doer, deeds, map).ToList();
        var seen = new HashSet<GroupRef>(groups);
        foreach (var g in groups)
        {
            long sum = 0; int heardMax = 0, last = int.MinValue; int members = map.Members(g);
            foreach (var key in DeedsOf(doer))
            {
                var rec = Deeds[key]; var row = deeds.Get(rec.DeedId); if (row == null) continue;
                int delta = DeedTable.DeltaFor(row, g.Kind); if (delta == 0 || !PlaceGroups(rec, map).Contains(g)) continue;
                int heard = HeardIn(rec, g, map);
                sum += Part(delta, rec.Amount, row.PerUnit, heard, members, AgePct(day - rec.Day), rec.WeightPm);
                if (heard > 0 && rec.WeightPm > 0) { heardMax = Math.Max(heardMax, Math.Min(heard, members)); last = Math.Max(last, rec.Day); }
            }
            Rows[(doer.Value, g.Packed)] = new StandingRow { ValuePm = (int)Math.Clamp(sum, -1000, 1000), Heard = heardMax, LastDay = last == int.MinValue ? 0 : last };
        }
        // a row the doer no longer has any deed behind (a changed content row) falls to nothing
        foreach (var k in Rows.Keys.Where(k => k.Person == doer.Value && !seen.Contains(GroupRef.Unpack(k.Group))).ToList()) Rows.Remove(k);
        RecomputeRenown(doer, map);
    }

    private void RecomputeRenown(EntityId doer, IGroupMap map)
    {
        int adults = Math.Max(1, map.RegionAdults), best = 0, last = int.MinValue;
        foreach (var key in DeedsOf(doer))
        {
            var rec = Deeds[key]; if (rec.WeightPm == 0) continue;
            int share = (int)Math.Min(1000L, 1000L * rec.HeardTotal / adults * rec.WeightPm / 1000);
            if (rec.HeardTotal > 0) { best = Math.Max(best, share); last = Math.Max(last, rec.Day); }
        }
        if (last == int.MinValue) { Renowns.Remove(doer.Value); return; }
        Renowns.TryGetValue(doer.Value, out var r);
        if (r.DecayPm == 0 || last > r.LastDeedDay) r.DecayPm = 1000;     // a new deed makes the name fresh
        r.SharePm = best; r.LastDeedDay = last;
        Renowns[doer.Value] = r;
    }

    /// <summary>The daily pass (H5 hour 0, 4.5): every person with a deed is recomputed (deeds age), and on a season's first day renown wears 5% for those a year past their last deed.</summary>
    public void Daily(DeedTable deeds, IGroupMap map, int day)
    {
        if (day == LastDay) return;
        foreach (var d in Doers.ToList()) Recompute(d, deeds, map, day);
        if (day % TimeConst.DaysPerSeason == 0)
            foreach (var k in Renowns.Keys.ToList())
            {
                var r = Renowns[k];
                if (day - r.LastDeedDay > TimeConst.DaysPerYear) { r.DecayPm = Math.Max(0, (int)((long)r.DecayPm * (100 - RenownDecayPct) / 100)); Renowns[k] = r; }
            }
        LastDay = day;
    }

    // ------------------------------------------------------------------ reads

    public StandingRow? Row(EntityId person, GroupRef g) => Rows.TryGetValue((person.Value, g.Packed), out var r) ? r : null;
    public int Value(EntityId person, GroupRef g) => Rows.TryGetValue((person.Value, g.Packed), out var r) ? r.ValuePm : 0;
    public int RenownOf(EntityId person) => Renowns.TryGetValue(person.Value, out var r) ? r.Pm : 0;
    public IEnumerable<(GroupRef Group, StandingRow Row)> RowsOf(EntityId person)
    {
        foreach (var (k, r) in Rows) if (k.Person == person.Value) yield return (GroupRef.Unpack(k.Group), r);
    }

    // ------------------------------------------------------------------ inheritance (4.4)

    /// <summary>
    /// Play passes to <paramref name="heirPerson"/> who takes up the hero's id <paramref name="hero"/>: a kin heir starts at 50% of every row and of renown (the family name), a companion at nothing,
    /// and the marks never pass. Whatever the heir had earned under their own id is theirs and joins the hero's. Returns the groups carried, as the succession record keeps them
    /// (<c>S3,F1,R0</c> by kind letter and index).
    /// </summary>
    public string Inherit(EntityId hero, EntityId heirPerson, bool kin)
    {
        int w = kin ? KinInheritPm : 0;
        foreach (var key in DeedsOf(hero)) Deeds[key].WeightPm = (int)((long)Deeds[key].WeightPm * w / 1000);
        var carried = new List<GroupRef>();
        foreach (var k in Rows.Keys.Where(k => k.Person == hero.Value).ToList())
        {
            var r = Rows[k]; r.ValuePm = r.ValuePm * w / 1000; r.Heard = r.Heard * w / 1000;
            if (r.ValuePm != 0) carried.Add(GroupRef.Unpack(k.Group));
            Rows[k] = r;
        }
        if (Renowns.TryGetValue(hero.Value, out var rn)) { rn.SharePm = rn.SharePm * w / 1000; Renowns[hero.Value] = rn; }
        Marks.Clear(hero);
        if (heirPerson != hero) Rekey(heirPerson, hero);
        return string.Join(",", carried.OrderBy(g => g).Select(g => $"{Letter(g.Kind)}{g.Index}"));
    }

    private static char Letter(GroupKind k) => k switch { GroupKind.Settlement => 'S', GroupKind.Fief => 'F', GroupKind.Realm => 'R', GroupKind.Religion => 'G', GroupKind.Guild => 'U', GroupKind.Band => 'B', _ => 'H' };

    /// <summary>Everything held under <paramref name="from"/> now belongs to <paramref name="to"/> (the heir's own deeds join the hero's).</summary>
    private void Rekey(EntityId from, EntityId to)
    {
        foreach (var key in DeedsOf(from).ToList()) Deeds[key].Doer = to;
        Reindex();
        foreach (var k in Rows.Keys.Where(k => k.Person == from.Value).ToList()) { var r = Rows[k]; Rows.Remove(k); Rows[(to.Value, k.Group)] = r; }
        if (Renowns.Remove(from.Value, out var rn)) Renowns[to.Value] = rn;
        if (Marks.Of(from) is { } m) { Marks.Clear(from); Marks.Restore(to.Value, m); }
    }

    // ------------------------------------------------------------------ the hash

    /// <summary>The <c>StandingHash</c> of the daily trail (reputation 5): deeds, rows, renown and marks in ascending order.</summary>
    public ulong Hash()
    {
        ulong h = 0xCBF29CE484222325UL;
        void M(ulong v) => h = DetRng.Mix(h ^ (v + 0x9E3779B97F4A7C15UL));
        foreach (var (k, d) in Deeds)
        {
            M(k); M(d.Doer.Value); M((ulong)(uint)d.Day); M((ulong)(uint)d.Place); M((ulong)(uint)d.Amount); M((ulong)(uint)d.WeightPm);
            foreach (char c in d.DeedId) M(c);
            foreach (var (s, n) in d.Heard) { M((ulong)(uint)s); M((ulong)(uint)n); }
        }
        foreach (var (k, r) in Rows) { M(k.Person); M(k.Group); M((ulong)(uint)r.ValuePm); M((ulong)(uint)r.Heard); M((ulong)(uint)r.LastDay); }
        foreach (var (k, r) in Renowns) { M(k); M((ulong)(uint)r.SharePm); M((ulong)(uint)r.DecayPm); M((ulong)(uint)r.LastDeedDay); }
        foreach (var (k, m) in Marks.All)
        {
            M(k); M((ulong)m.Brands); M(m.Enslaved ? 1UL : 0); M(m.Pardoned ? 1UL : 0);
            foreach (int r in m.OutlawRealms) M((ulong)(uint)r + 0x100); foreach (int r in m.Excommunicated) M((ulong)(uint)r + 0x200);
        }
        M((ulong)(uint)LastDay);
        return h;
    }
}
