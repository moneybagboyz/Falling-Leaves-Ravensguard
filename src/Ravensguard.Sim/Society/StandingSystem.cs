using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rumours;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Society;

/// <summary>
/// The region as <see cref="IGroupMap"/>: settlements, the fiefs they belong to and the realms of those fiefs, with the living people of each (counted from the person table once a day).
/// </summary>
public sealed class RegionGroups : IGroupMap
{
    private readonly PersonTable _people;
    private readonly int[] _fief, _realm;
    private readonly int[] _members;
    private readonly int[] _fiefMembers, _realmMembers;
    private readonly List<int>[] _fiefSettlements, _realmSettlements;
    // P6-B4: religions are sets of persons, counted by each person's own culture; a religion spans the settlements where one of its people lives
    private readonly Ravensguard.Sim.Faith.ReligionBook? _religions;
    private readonly int[] _religionMembers = [], _settlementReligion = [];
    private readonly List<int>[] _religionSettlements = [];
    public int RegionAdults { get; }

    public RegionGroups(RegionData region, PersonTable people, int day, Ravensguard.Sim.Faith.ReligionBook? religions = null)
    {
        _people = people; _religions = religions is { Count: > 0 } ? religions : null;
        int ns = region.Settlements.Count, nf = region.Fiefs.Count;
        _fief = new int[ns]; _realm = new int[ns]; _members = new int[ns];
        int maxRealm = -1; foreach (var f in region.Fiefs) maxRealm = Math.Max(maxRealm, f.Realm);
        _fiefMembers = new int[nf]; _realmMembers = new int[maxRealm + 1];
        _fiefSettlements = new List<int>[nf]; _realmSettlements = new List<int>[maxRealm + 1];
        for (int i = 0; i < nf; i++) _fiefSettlements[i] = [];
        for (int i = 0; i <= maxRealm; i++) _realmSettlements[i] = [];
        for (int s = 0; s < ns; s++)
        {
            int f = region.Settlements[s].FiefIndex; _fief[s] = f >= 0 && f < nf ? f : -1;
            _realm[s] = _fief[s] >= 0 ? region.Fiefs[_fief[s]].Realm : -1;
            if (_fief[s] >= 0) _fiefSettlements[_fief[s]].Add(s);
            if (_realm[s] >= 0) _realmSettlements[_realm[s]].Add(s);
        }
        if (_religions != null)
        {
            int nr = _religions.Count;
            _religionMembers = new int[nr]; _religionSettlements = new List<int>[nr]; _settlementReligion = new int[ns];
            for (int i = 0; i < nr; i++) _religionSettlements[i] = [];
            for (int s = 0; s < ns; s++) _settlementReligion[s] = _religions.OfSettlement(region, s);
        }
        int adults = 0;
        for (int i = 0; i < people.Count; i++)
        {
            if (!people.IsAlive(i)) continue;
            if (people.AgeYears(i, day) >= RumourSeeder.AdultAge) adults++;
            int s = people.HomeSettlement[i]; if (s < 0 || s >= ns) continue;
            if (_religions != null && _religions.OfCulture(people.Culture[i]) is >= 0 and int rl)
            {
                _religionMembers[rl]++;
                var rs = _religionSettlements[rl]; if (rs.Count == 0 || rs[^1] != s) { int at = rs.BinarySearch(s); if (at < 0) rs.Insert(~at, s); }
            }
            _members[s]++;
            if (_fief[s] >= 0) _fiefMembers[_fief[s]]++;
            if (_realm[s] >= 0) _realmMembers[_realm[s]]++;
        }
        RegionAdults = adults;
    }

    public GroupRef FiefOf(int s) => new(GroupKind.Fief, s >= 0 && s < _fief.Length ? _fief[s] : -1);
    public GroupRef RealmOf(int s) => new(GroupKind.Realm, s >= 0 && s < _realm.Length ? _realm[s] : -1);
    public IReadOnlyList<int> SettlementsOf(GroupRef g) => g.Kind switch
    {
        GroupKind.Settlement => [g.Index],
        GroupKind.Fief => g.Index >= 0 && g.Index < _fiefSettlements.Length ? _fiefSettlements[g.Index] : [],
        GroupKind.Realm => g.Index >= 0 && g.Index < _realmSettlements.Length ? _realmSettlements[g.Index] : [],
        GroupKind.Religion => g.Index >= 0 && g.Index < _religionSettlements.Length ? _religionSettlements[g.Index] : [],
        _ => [],
    };
    public int Members(GroupRef g) => g.Kind switch
    {
        GroupKind.Settlement => g.Index >= 0 && g.Index < _members.Length ? _members[g.Index] : 0,
        GroupKind.Fief => g.Index >= 0 && g.Index < _fiefMembers.Length ? _fiefMembers[g.Index] : 0,
        GroupKind.Realm => g.Index >= 0 && g.Index < _realmMembers.Length ? _realmMembers[g.Index] : 0,
        GroupKind.Religion => g.Index >= 0 && g.Index < _religionMembers.Length ? _religionMembers[g.Index] : 0,
        _ => 0,
    };
    public int HomeOf(EntityId person) { int i = _people.IndexOf(person); return i >= 0 ? _people.HomeSettlement[i] : -1; }
    public GroupRef ReligionOf(int s) => GroupRef.Religion(_religions != null && s >= 0 && s < _settlementReligion.Length ? _settlementReligion[s] : -1);
    public GroupRef ReligionOfPerson(EntityId person) { int i = _people.IndexOf(person); return GroupRef.Religion(_religions != null && i >= 0 ? _religions.OfCulture(_people.Culture[i]) : -1); }
}

/// <summary>
/// The standing book on the world's hour (reputation 4.5). After the rumours of the hour have spread, every deed rumour that gained hearings has its counts copied into the book (so a deed is
/// remembered after its rumour expires) and its doer's rows are recomputed; on a day's first hour every person with a deed is recomputed (deeds age) and renown wears on a season's first day.
/// A household that moves takes its hearings with it (<see cref="Moved"/>).
/// </summary>
public static class StandingSystem
{
    /// <summary>
    /// Reads what the hour's hearings changed. <paramref name="book"/> is created by the caller when null and a deed appears (<paramref name="make"/>): a world with no deed keeps no standing section.
    /// </summary>
    public static void OnHour(Func<StandingBook> make, StandingBook? book, DeedTable deeds, IGroupMap map, TalkWorld w, TalkState ts, long hour)
    {
        int day = (int)(hour / 24);
        var dirty = new SortedSet<EntityId>();
        foreach (var id in ts.Rumours.DrainTouched())
        {
            var r = ts.Rumours.Get(id); if (r == null || r.Source == EntityId.None) continue;
            var e = w.Chronicle.Get(r.Source); if (e == null) continue;
            var row = deeds.RowFor(e); if (row == null) continue;
            if (DeedTable.DoerOf(e) is not { } doer) continue;
            book ??= make();
            var rec = book.Deed(id);
            if (rec == null)
            {
                int place = r.Settlement >= 0 ? r.Settlement : map.HomeOf(doer);
                rec = book.AddDeed(new DeedRecord { Entry = id, Doer = doer, DeedId = row.Id, Day = (int)e.When.TotalDays, Place = place, Amount = DeedTable.AmountOf(row, e) });
            }
            rec.Heard = new SortedDictionary<int, int>(ts.Rumours.HeardBySettlement(id).ToDictionary(kv => kv.Key, kv => kv.Value));
            dirty.Add(doer);
        }
        if (book == null) return;
        if (day != book.LastDay) { using var _d = Ravensguard.Sim.Diagnostics.PfClock.Span("stand.daily"); book.Daily(deeds, map, day); }
        else foreach (var d in dirty) book.Recompute(d, deeds, map, day);
    }

    /// <summary>A person's household moved from one settlement to another (RP-9): every deed they had heard counts for the new settlement instead, and the book follows at the next hour.</summary>
    public static void Moved(TalkState ts, int person, int fromSettlement, int toSettlement) => ts.Rumours.Relocate(person, fromSettlement, toSettlement);
}
