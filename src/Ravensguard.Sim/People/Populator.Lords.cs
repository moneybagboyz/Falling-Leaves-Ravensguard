using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.People;

public sealed partial class Populator
{
    /// <summary>Living adult male nobles per history realm, oldest id first: the pool the fiefs draw their lords from.</summary>
    private Dictionary<int, Queue<Notable>> LordCandidates()
    {
        var res = new Dictionary<int, Queue<Notable>>();
        foreach (var n in _h.Notables)
        {
            if (!n.Alive || n.Female || n.Realm < 0) continue;
            if (n.Role is not (NotableRole.Noble or NotableRole.General or NotableRole.Heir)) continue;
            int age = (_nowYear - n.BirthYear) * _h.AgingPace;
            if (age < 22 || age > 70) continue;
            if (!res.TryGetValue(n.Realm, out var q)) res[n.Realm] = q = new Queue<Notable>();
            q.Enqueue(n);
        }
        return res;
    }

    private void BuildLords()
    {
        var cands = LordCandidates();
        for (int fi = 0; fi < _r.Fiefs.Count; fi++)
        {
            var f = _r.Fiefs[fi];
            int si = f.SeatSettlement;
            var s = _r.Settlements[si];
            var plan = s.Plan;
            int lot = plan.ManorLot;
            if (lot < 0)
            {
                // No room for a manor hall: the lord takes the grandest house of the seat.
                int best = -1, bestCap = -1;
                foreach (var l in plan.Lots)
                    if (l.Dwelling is DwellingType.Burgage or DwellingType.Longhouse or DwellingType.Cottage && _lotBeds[si][l.Index] > bestCap) { best = l.Index; bestCap = _lotBeds[si][l.Index]; }
                lot = best;
            }
            if (lot < 0) continue;
            _lordLot[si] = lot;
            Notable? nb = null;
            if (f.Realm >= 0 && cands.TryGetValue(f.Realm, out var q) && q.Count > 0) nb = q.Dequeue();
            BuildLordHousehold(fi, si, lot, nb);
        }
    }

    private void BuildLordHousehold(int fi, int si, int lot, Notable? nb)
    {
        var f = _r.Fiefs[fi];
        var s = _r.Settlements[si];
        bool town = s.Class == SettlementClass.Town;
        int culture = nb?.Culture ?? CultureOf(s);
        var np = _names.For(culture);
        var r = new Rnd(_seed, f.Id.Value, StLord);
        var lotInfo = s.Plan.Lots[lot];
        int beds = _lotBeds[si][lot];
        int hh = _hh.Add(si, lot, lotInfo.Dwelling, beds);
        _hh.Class[hh] = SocialClass.Gentry; _hh.Deed[hh] = DeedKind.Owned; _hh.Purse[hh] = r.Range(800, 4000);

        RealmState? realm = f.Realm >= 0 && f.Realm < _h.Realms.Count ? _h.Realms[f.Realm] : null;
        Dynasty? dyn = nb != null ? _h.GetDynasty(nb.Dynasty) : realm != null ? _h.GetDynasty(realm.Dynasty) : null;
        string house = dyn?.Name ?? np.FamilyName(r);
        int lordAge = nb != null ? (_nowYear - nb.BirthYear) * _h.AgingPace : r.Range(26, 57);
        string given = nb?.Name ?? "";

        int lord = NewLord(r, np, hh, si, lot, Sex.Male, lordAge, house, culture, given, nb?.Epithet ?? "", nb?.Id ?? EntityId.None, dyn?.Id ?? EntityId.None);
        SetJob(lord, Occupation.Lord, -1, si, lot);
        Finish(lord, r, Occupation.Lord, lordAge);

        // Wife: the notable's own spouse when alive and of age, otherwise a lady of the same generation.
        int wife = -1;
        Notable? ns = nb != null && nb.Spouse.Value != 0 ? _h.GetNotable(nb.Spouse) : null;
        bool historic = ns is { Alive: true, Female: true } && (_nowYear - ns.BirthYear) * _h.AgingPace is >= 18 and <= 60;
        if (historic || r.Pct(85))
        {
            int wAge = historic ? (_nowYear - ns!.BirthYear) * _h.AgingPace : Math.Clamp(lordAge + r.Range(-9, 3), 18, 49);
            wife = NewLord(r, np, hh, si, lot, Sex.Female, wAge, house, culture, historic ? ns!.Name : "", historic ? ns!.Epithet : "", historic ? ns!.Id : EntityId.None, dyn?.Id ?? EntityId.None);
            SetJob(wife, Occupation.None, -1, -1, -1);
            Finish(wife, r, Occupation.None, wAge);
            Link(_p, lord, wife);
        }
        // Staff first so the household never exceeds the hall's beds; children take what is left.
        int maxL = Math.Max(4, Math.Min(beds, Math.Max(4, s.TargetPop * 3 / 10)));
        int steward = 1, servants = town ? r.Range(4, 9) : r.Range(2, 6), guards = town ? r.Range(3, 7) : r.Range(1, 4);
        int kids = wife >= 0 ? r.Range(0, 5) : 0;
        int fixedPeople = 1 + (wife >= 0 ? 1 : 0);
        while (fixedPeople + steward + servants + guards + kids > maxL)
        {
            if (guards > 1) guards--; else if (servants > 1) servants--; else if (kids > 0) kids--; else if (servants > 0) servants--; else if (guards > 0) guards--; else steward = 0;
        }
        int parentAge = wife >= 0 ? Math.Min(lordAge, _p.AgeYears(wife, _nowDay)) : lordAge;
        int maxKid = Math.Min(24, parentAge - 16);
        if (maxKid < 0) kids = 0;
        var kidAges = new List<int>();
        for (int t = 0; t < kids; t++) kidAges.Add(r.Range(0, maxKid + 1));
        kidAges.Sort((a, b) => b.CompareTo(a));
        foreach (int cAge in kidAges)
        {
            var cs = r.Pct(50) ? Sex.Male : Sex.Female;
            int c = NewLord(r, np, hh, si, lot, cs, cAge, house, culture, "", "", EntityId.None, dyn?.Id ?? EntityId.None);
            _p.Father[c] = lord; _p.Mother[c] = wife;
            SetJob(c, Occupation.None, -1, -1, -1);
            Finish(c, r, Occupation.None, cAge);
        }
        for (int t = 0; t < steward + servants + guards; t++)
        {
            var occ = t < steward ? Occupation.Steward : t < steward + servants ? Occupation.Servant : Occupation.Guard;
            var sx = occ == Occupation.Servant ? (r.Pct(60) ? Sex.Female : Sex.Male) : Sex.Male;
            int age = occ == Occupation.Servant ? r.Range(15, 50) : occ == Occupation.Steward ? r.Range(30, 58) : r.Range(18, 45);
            int c = NewPerson(r, np, hh, si, lot, sx, age, np.FamilyName(r), culture, SocialClass.Peasant);
            SetJob(c, occ, lord, si, lot);
            Finish(c, r, occ, age);
        }
        _hh.Head[hh] = lord; _hh.Name[hh] = house;
        int m = 0;
        foreach (int c in _hh.Members(hh))
        {
            if (m < beds) _p.Bed[c] = (short)m; else { _p.Bed[c] = -1; _p.Homeless[c] = HomelessReason.NoRoom; }
            m++;
        }
        _fiefLord[fi] = lord; _fiefLordHh[fi] = hh;
        _lotHousehold[si][lot] = hh; _lotKeeper[si][lot] = lord;
        _lordPeople[si] = _hh.MemberCount[hh];
    }

    /// <summary>A member of a noble house: the house name is the family name, historic names are kept as they are.</summary>
    private int NewLord(Rnd r, NamePool np, int hh, int si, int lot, Sex sex, int age, string house, int culture, string given, string byname, EntityId notable, EntityId dynasty)
    {
        int i = NewPerson(r, np, hh, si, lot, sex, age, house, culture, SocialClass.Gentry, byname.Length == 0);
        if (given.Length > 0)
        {
            _used.Remove(Key(_p.Given[i], house, _p.Byname[i]));
            _p.Given[i] = given; _p.Byname[i] = byname;
            if (!_used.Add(Key(given, house, byname))) { _p.Byname[i] = byname.Length > 0 ? byname + " the Younger" : "the Younger"; _used.Add(Key(given, house, _p.Byname[i])); }
        }
        _p.Notable[i] = notable; _p.Dynasty[i] = dynasty;
        return i;
    }
}
