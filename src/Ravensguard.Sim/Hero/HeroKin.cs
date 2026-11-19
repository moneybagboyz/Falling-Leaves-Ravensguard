using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Succession;

/// <summary>How a person of the person table is kin to the hero (the Phase 4 hero is not a row of the table, so the links are kept here; morale-capture-succession 4.8).</summary>
public enum KinRelation : byte { Parent, Sibling, Spouse, Child }

/// <summary>One of the hero's kin: a person row (by id, so a save or a moved row never dangles) and how they are related.</summary>
public readonly record struct HeroKinLink(EntityId Person, KinRelation Relation);

/// <summary>
/// The hero's family (P4 close-out): at a new game the hero is made the grown child of a household of the home region (the first settlement, in index order, with a living head of 38 to 62
/// who has a living child of 12 to 30: the head and the head's spouse are the parents, their children the siblings), so the kinship order of 4.8 has someone to find. A pure choice of the
/// tables (no draw). When an heir who was a person row takes over, the heir's own kin come from the row's Father, Mother and Spouse columns (<see cref="FromRow"/>).
/// <see cref="OrderForHero"/> is <see cref="SuccessionOrder.Order"/> for a hero outside the table: the same ranks and tie-breaks, read from these links.
/// </summary>
public static class HeroKin
{
    public const int ParentMinAge = 38, ParentMaxAge = 62, SiblingMinAge = 12, SiblingMaxAge = 30;

    /// <summary>The family a new hero is born into, or empty when the region has no household that fits.</summary>
    public static List<HeroKinLink> Choose(PersonTable pt, HouseholdTable hht, int settlements, int nowDay)
    {
        for (int s = 0; s < settlements; s++)
            for (int hh = 0; hh < hht.Count; hh++)
            {
                if (hht.Settlement[hh] != s || hht.MemberCount[hh] == 0) continue;
                int head = hht.Head[hh];
                if (head < 0 || head >= pt.Count || pt.Status[head] != PersonStatus.Alive) continue;
                int age = pt.AgeYears(head, nowDay);
                if (age < ParentMinAge || age > ParentMaxAge) continue;
                int sp = pt.Spouse[head];
                bool hasChild = false;
                for (int i = 0; i < pt.Count && !hasChild; i++)
                    if (pt.Status[i] == PersonStatus.Alive && (pt.Father[i] == head || pt.Mother[i] == head) && pt.AgeYears(i, nowDay) is >= SiblingMinAge and <= SiblingMaxAge) hasChild = true;
                if (!hasChild) continue;
                var list = new List<HeroKinLink> { new(pt.IdOf(head), KinRelation.Parent) };
                if (sp >= 0 && sp < pt.Count && pt.Status[sp] == PersonStatus.Alive) list.Add(new(pt.IdOf(sp), KinRelation.Parent));
                for (int i = 0; i < pt.Count; i++)
                    if (i != head && i != sp && (pt.Father[i] == head || pt.Mother[i] == head || sp >= 0 && (pt.Father[i] == sp || pt.Mother[i] == sp))) list.Add(new(pt.IdOf(i), KinRelation.Sibling));
                return list;
            }
        return [];
    }

    /// <summary>The kin of a person row, as links (children, spouse, siblings, parents), for an heir who now is the hero.</summary>
    public static List<HeroKinLink> FromRow(PersonTable pt, int row)
    {
        var list = new List<HeroKinLink>();
        if (row < 0 || row >= pt.Count) return list;
        int f = pt.Father[row], m = pt.Mother[row], sp = pt.Spouse[row];
        if (f >= 0 && f < pt.Count) list.Add(new(pt.IdOf(f), KinRelation.Parent));
        if (m >= 0 && m < pt.Count) list.Add(new(pt.IdOf(m), KinRelation.Parent));
        if (sp >= 0 && sp < pt.Count) list.Add(new(pt.IdOf(sp), KinRelation.Spouse));
        for (int i = 0; i < pt.Count; i++)
        {
            if (i == row) continue;
            if (pt.Father[i] == row || pt.Mother[i] == row) list.Add(new(pt.IdOf(i), KinRelation.Child));
            else if (f >= 0 && pt.Father[i] == f || m >= 0 && pt.Mother[i] == m) list.Add(new(pt.IdOf(i), KinRelation.Sibling));
        }
        return list;
    }

    /// <summary>
    /// The kinship order for a hero who is not a person row (4.8): children 16 and over eldest first, the spouse, siblings 16 and over eldest first, children of 12 to 15 with the most loyal follower
    /// as guardian, parents, then the squad as <see cref="SuccessionOrder.Order"/> ranks it. Eligible kin are alive, at least 12 and not <paramref name="unavailable"/>; ties by birth day, then id.
    /// </summary>
    public static List<SuccessionCandidate> OrderForHero(PersonTable pt, IReadOnlyList<HeroKinLink> kin, int nowDay, IReadOnlyList<SquadMember> squad, Func<int, EntityId, bool>? unavailable = null)
    {
        var list = new List<SuccessionCandidate>();
        var seen = new HashSet<ulong>();
        bool Ok(int i) => i >= 0 && i < pt.Count && pt.Status[i] == PersonStatus.Alive && pt.AgeYears(i, nowDay) >= SuccessionOrder.MinAge && !(unavailable?.Invoke(i, pt.IdOf(i)) ?? false);
        void Add(int i, SuccessionRule rule, EntityId guardian = default)
        {
            var id = pt.IdOf(i);
            if (!seen.Add(id.Value)) return;
            list.Add(new SuccessionCandidate(id, i, rule, pt.AgeYears(i, nowDay), pt.FullName(i), guardian));
        }
        List<int> Rows(KinRelation r) => [.. kin.Where(k => k.Relation == r).Select(k => pt.IndexOf(k.Person)).Where(i => i >= 0).Distinct().OrderBy(i => pt.BirthDay[i]).ThenBy(i => pt.IdOf(i).Value)];
        var children = Rows(KinRelation.Child);
        foreach (int i in children) if (Ok(i) && pt.AgeYears(i, nowDay) >= SuccessionOrder.AdultAge) Add(i, SuccessionRule.Child);
        foreach (int i in Rows(KinRelation.Spouse)) if (Ok(i)) Add(i, SuccessionRule.Spouse);
        foreach (int i in Rows(KinRelation.Sibling)) if (Ok(i) && pt.AgeYears(i, nowDay) >= SuccessionOrder.AdultAge) Add(i, SuccessionRule.Sibling);
        var guardian = squad.Where(s => s.PersonIndex < 0 || s.PersonIndex < pt.Count && pt.Status[s.PersonIndex] == PersonStatus.Alive && !(unavailable?.Invoke(s.PersonIndex, s.Person) ?? false))
            .OrderByDescending(s => s.LoyaltyPm).ThenBy(s => s.JoinedDay).ThenBy(s => s.Person.Value).Select(s => s.Person).FirstOrDefault();
        foreach (int i in children) if (Ok(i) && pt.AgeYears(i, nowDay) < SuccessionOrder.AdultAge) Add(i, SuccessionRule.YoungChild, guardian);
        foreach (int i in Rows(KinRelation.Parent).OrderBy(i => pt.IdOf(i).Value)) if (Ok(i) && pt.AgeYears(i, nowDay) >= SuccessionOrder.AdultAge) Add(i, SuccessionRule.Parent);
        // the squad, as the table order ranks it (a hero outside the table has no row of its own: -1)
        foreach (var c in SuccessionOrder.Order(pt, -1, nowDay, squad, unavailable))
            if (seen.Add(c.Person.Value)) list.Add(c);
        return list;
    }

    /// <summary>The kin that stay with an heir (4.8: kin always): the persons of the links.</summary>
    public static IReadOnlySet<EntityId> People(IReadOnlyList<HeroKinLink> kin) => kin.Select(k => k.Person).ToHashSet();
}
