using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Succession;

namespace Ravensguard.Sim.Family;

/// <summary>A person of the hero's family as the panel shows them: who, how related, age, where, the course and tutor of a child, the skills the lessons gave and the loyalty (permille) with what weighs on it.</summary>
public sealed record FamilyMemberView(EntityId Id, string Name, string Relation, int Age, bool Alive, bool Held, string Place, Course Course, EntityId Tutor, bool HasTutor, string TutorName, string Skills, int LoyaltyPm, int Grievances, bool Child);

/// <summary>One thing age has taken from the hero: the attribute, its value now and what was lost to age.</summary>
public sealed record AgeLossView(string Attr, int Value, int Lost);

/// <summary>A tutor the head could name for a child: the hero himself, a member of the household, or the priest of the place.</summary>
public sealed record TutorChoice(EntityId Id, string Name);

/// <summary>An arranged match as the panel says it (P6-E3): the pair, where it stands, the wedding day, the dowry, whether it joins two lords' houses and whether the overlord's fee is still owing.</summary>
public sealed record MatchLine(string Groom, string Bride, MatchState State, int WeddingDay, long DowryMites, bool Alliance, bool FeeOwing);

/// <summary>A member of the house who could be matched (P6-E3): the best candidate the houses would consider, the answering head's utility, the dowry at the class share, whether it is an alliance, whether the overlord's consent is needed, and whether the member refuses any match (loyalty under the turn line).</summary>
public sealed record ArrangeLine(EntityId Member, string Name, int Age, string Candidate, int Utility, long DowryMites, bool Alliance, bool NeedsConsent, bool Refuses);

/// <summary>The family panel's data (family-and-lifespan 6), built at a barrier: the hero's age and what it has taken, the spouse and children with their lessons, the courtship under way, the roof and the hazard of old age.</summary>
public sealed record FamilyView(string HeroName, int HeroAge, int OldAgePct, int NextBirthdayInDays, IReadOnlyList<AgeLossView> Ageing, IReadOnlyList<FamilyMemberView> Members,
    string CourtedName, EntityId Courted, CourtState CourtState, int Meetings, long Gifts, int WeddingDay, bool HasRoof, IReadOnlyList<TutorChoice> Tutors, int Day,
    IReadOnlyList<MatchLine>? MatchLines = null, IReadOnlyList<ArrangeLine>? ArrangeLines = null)
{
    public IReadOnlyList<MatchLine> OpenMatches => MatchLines ?? [];
    public IReadOnlyList<ArrangeLine> Arrangeable => ArrangeLines ?? [];
    public FamilyMemberView? Spouse => Members.FirstOrDefault(m => m.Relation == "spouse");
    public IEnumerable<FamilyMemberView> Children => Members.Where(m => m.Child);

    /// <summary>The panel's data for the hero at <paramref name="day"/>; null when the world has no hero row.</summary>
    public static FamilyView? Of(WorldState st, ContentDatabase? db, int day)
    {
        if (st.Economy == null || st.Hero == null) return null;
        var w = FamilyWorld.From(st, db); int hero = w.HeroRow; if (hero < 0) return null;
        var pt = w.People; var book = st.Family; var f = w.F;
        int age = pt.AgeYears(hero, day);
        var losses = new List<AgeLossView>();
        for (int a = 0; a < PersonTable.AttrCount; a++)
        {
            int lost = book?.Ageing is { } ag && ag.Row == hero ? ag.Lost[a] : 0;
            losses.Add(new AgeLossView(FamilyContent.AttrIds[a], st.Hero.Attributes[a], lost));
        }
        var members = new List<FamilyMemberView>();
        var seen = new HashSet<int>();
        void Add(int i, string rel)
        {
            if (i < 0 || i >= pt.Count || i == hero || !seen.Add(i)) return;
            var id = pt.IdOf(i); bool alive = pt.Status[i] == PersonStatus.Alive;
            var up = book != null && book.Upbringings.TryGetValue(id.Value, out var uu) ? uu : null;
            string tutorName = up is { HasTutor: true } ? (up.Tutor == w.HeroId.Value ? st.Hero.Name : pt.IndexOf(new EntityId(up.Tutor)) is var t and >= 0 ? pt.FullName(t) : "") : "";
            string skills = up == null ? "" : string.Join(", ", up.Milli.Where(kv => kv.Key < Upbringing.AttrBase).Select(kv => ((Skill)kv.Key).ToString().ToLowerInvariant() + " " + kv.Value / 1000));
            int trust = w.TrustPm(id); var g = book != null && book.Grievances.TryGetValue(id.Value, out var gg) ? gg : KinGrievance.None;
            int s = pt.HomeSettlement[i];
            members.Add(new FamilyMemberView(id, pt.FullName(i), rel, pt.AgeYears(i, day), alive, st.Captivity?.Find(id) is { Held: true }, w.PlaceName(s), up?.Course ?? Course.None, up?.HasTutor == true ? new EntityId(up.Tutor) : EntityId.None, up?.HasTutor == true,
                tutorName, skills, FamilyBook.Loyalty(trust == 0 ? 400 : trust, g, f.S.GrievanceWeightPm), System.Numerics.BitOperations.PopCount((uint)g), rel == "child"));
        }
        Add(pt.Spouse[hero], "spouse");
        if (st.Succession != null) foreach (var k in st.Succession.Kin.Where(k => k.Relation == KinRelation.Child).OrderBy(k => pt.BirthDay[Math.Max(0, pt.IndexOf(k.Person))])) Add(pt.IndexOf(k.Person), "child");
        // the courtship under way
        var c = book?.Courtships.Values.FirstOrDefault(x => x.State is CourtState.Courting or CourtState.Betrothed);
        int cp = c != null ? pt.IndexOf(new EntityId(c.Courted)) : -1;
        // who can teach: the hero, the grown members of the house, and the priest of the place
        var tutors = new List<TutorChoice> { new(w.HeroId, st.Hero.Name) };
        int hh = pt.Household[hero];
        if (hh >= 0) foreach (int m in w.Households.Members(hh).OrderBy(m => m)) if (pt.Status[m] == PersonStatus.Alive && pt.AgeYears(m, day) >= f.S.AdultAge) tutors.Add(new(pt.IdOf(m), pt.FullName(m)));
        int home = hh >= 0 ? w.Households.Settlement[hh] : -1;
        for (int i = 0; i < pt.Count; i++) if (pt.Status[i] == PersonStatus.Alive && pt.Occupation[i] == Occupation.Priest && pt.HomeSettlement[i] == home) { tutors.Add(new(pt.IdOf(i), pt.FullName(i))); break; }
        int next = FamilySystem.BirthdayOf(pt, hero, age + 1) - day;
        // P6-E3 (4.2): the matches under way and the members of the house the head could match
        var lines = new List<MatchLine>(); var arrange = new List<ArrangeLine>();
        if (book != null)
            foreach (var m in book.Matches.Values.Where(m => m.State is MatchState.AwaitingFee or MatchState.Betrothed or MatchState.Wed))
            {
                int g = pt.IndexOf(new EntityId(m.Groom)), b = pt.IndexOf(new EntityId(m.Bride));
                lines.Add(new MatchLine(g >= 0 ? pt.FullName(g) : "", b >= 0 ? pt.FullName(b) : "", m.State, m.WeddingDay, m.DowryMites, m.Alliance, m.State == MatchState.AwaitingFee));
            }
        if (hh >= 0)
            foreach (int m in w.Households.Members(hh).OrderBy(m => m))
            {
                if (m == hero || Matches.WhyNot(w, m, day).Length > 0) continue;
                var cand = Matches.Candidates(w, m, day, 1).FirstOrDefault();
                arrange.Add(new ArrangeLine(pt.IdOf(m), pt.FullName(m), pt.AgeYears(m, day), cand?.Name ?? "", cand?.Terms.Total ?? 0, cand?.DowryMites ?? 0, cand?.Alliance ?? false,
                    cand != null && Matches.ConsentFief(w, m, cand.Row) >= 0, Matches.KinLoyalty(w, pt.IdOf(m)) < f.S.TurnBelowPm));
            }
        return new FamilyView(st.Hero.Name, age, f.OldAgePct(age), Math.Max(0, next), losses, members, cp >= 0 ? pt.FullName(cp) : "", cp >= 0 ? pt.IdOf(cp) : EntityId.None, c?.State ?? CourtState.Broken,
            c?.Meetings ?? 0, c?.Gifts ?? 0, c?.WeddingDay ?? -1, Courting.RoofFor(w, hero).Ok, tutors, day, lines, arrange);
    }
}

/// <summary>What the talk window needs to know about courting the person being talked to: can she be, and where the courtship stands.</summary>
public sealed record CourtOffer(bool Can, string Why, CourtState State, bool Started, int Meetings, long Gifts, int WeddingDay, string Name)
{
    public static readonly CourtOffer None = new(false, "", CourtState.Broken, false, 0, 0, -1, "");

    /// <summary>The offer for a person at <paramref name="day"/> (read at a barrier).</summary>
    public static CourtOffer For(WorldState st, ContentDatabase? db, EntityId person, int day)
    {
        if (st.Economy == null || st.Hero == null) return None;
        var w = FamilyWorld.From(st, db); int hero = w.HeroRow, p = w.People.IndexOf(person); if (hero < 0 || p < 0) return None;
        var c = st.Family?.CourtshipOf(person.Value);
        string why = Courting.WhyNot(w, hero, p, day);
        if (c != null && why.Length == 0) return new CourtOffer(true, "", c.State, true, c.Meetings, c.Gifts, c.WeddingDay, w.People.FullName(p));
        return new CourtOffer(why.Length == 0, why, CourtState.Broken, false, 0, 0, -1, w.People.FullName(p));
    }
}
