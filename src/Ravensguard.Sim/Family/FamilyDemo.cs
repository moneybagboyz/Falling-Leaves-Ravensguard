using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Succession;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Family;

/// <summary>
/// Dev and test scaffolding for the family screens (screenshots, the selftest): a married hero with children of several ages and lessons under way, made through the same rules play uses (a courtship,
/// the plea, the wedding on a holy day) with a roof granted to the household and the trust the answers need. Never called by the game itself.
/// </summary>
public static class FamilyDemo
{
    /// <summary>Marries the hero to the first fitting person of the other sex, gives the couple two children (7 and 3 years old) and a course for the elder; returns the spouse's row or -1.</summary>
    public static int MarryWithChildren(WorldState st, ContentDatabase db, int day)
    {
        var w = FamilyWorld.From(st, db); int hero = w.HeroRow; if (hero < 0) return -1;
        var pt = w.People; var hht = w.Households; int hh = pt.Household[hero];
        if (hht.Lot[hh] < 0) { hht.Lot[hh] = 0; hht.Dwelling[hh] = DwellingType.Cottage; hht.Beds[hh] = 6; }
        var want = pt.Sex[hero] == Sex.Female ? Sex.Male : Sex.Female; int bride = -1;
        for (int i = 0; i < pt.Count && bride < 0; i++)
        {
            if (i == hero || pt.Status[i] != PersonStatus.Alive || pt.Sex[i] != want || pt.Spouse[i] >= 0) continue;
            int a = pt.AgeYears(i, day), bh = pt.Household[i];
            if (a < 22 || a > 34 || bh < 0 || bh == hh || hht.Head[bh] < 0 || hht.Head[bh] == i || Courting.WhyNot(w, hero, i, day).Length > 0) continue;
            bride = i;
        }
        if (bride < 0) return -1;
        int head = hht.Head[pt.Household[bride]]; hht.Class[pt.Household[bride]] = pt.Class[hero];
        st.Party!.Know(pt.IdOf(bride)).TrustPm = 900; st.Party.Know(pt.IdOf(head)).TrustPm = 900;
        Courting.Court(w, pt.IdOf(bride).Value, 0, day); Courting.Ask(w, pt.IdOf(bride).Value, day);
        var c = st.Family!.CourtshipOf(pt.IdOf(bride).Value);
        if (c == null) return -1;
        if (c.WeddingDay < 0) c.WeddingDay = day;
        Courting.Wed(w, c, hero, bride, Math.Max(day, c.WeddingDay));
        if (pt.Spouse[hero] != bride) return -1;
        var sb = st.Succession ??= new SuccessionBook();
        foreach (var (years, name) in new[] { (7, "Wren"), (3, "Edda"), (23, "Ysolde") })
        {
            st.Ids.EnsureNext(IdKind.Person, pt.BaseCounter + (ulong)pt.Count); st.Ids.Allocate(IdKind.Person);
            int ch = pt.Add(); st.Economy!.OpenPerson(ch);
            pt.Given[ch] = name; pt.Family[ch] = pt.Family[hero]; pt.Sex[ch] = years == 7 ? Sex.Male : Sex.Female; pt.BirthDay[ch] = PersonTable.BirthDayFor(day, years, 10, pt.AgingPace);
            pt.Father[ch] = pt.Sex[hero] == Sex.Male ? hero : bride; pt.Mother[ch] = pt.Sex[hero] == Sex.Male ? bride : hero; pt.Household[ch] = hh; pt.HomeSettlement[ch] = hht.Settlement[hh]; pt.Class[ch] = hht.Class[hh];
            pt.Culture[ch] = pt.Culture[hero]; pt.Status[ch] = PersonStatus.Alive; hht.AddMemberLater(hh, ch);
            sb.Kin.Add(new HeroKinLink(pt.IdOf(ch), KinRelation.Child)); st.Family!.Born.Add(pt.IdOf(ch).Value);
            if (years == 7) FamilyDesk.Teach(w, pt.IdOf(ch).Value, Course.Arms, Skill.None, w.HeroId, day);
        }
        w.Pop.ResetKin();
        return bride;
    }
}
