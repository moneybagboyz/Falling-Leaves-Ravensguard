using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Family;

/// <summary>
/// The hero's family acts at the barrier (family-and-lifespan 4.1, 4.4; recorded for replay like every hero command): "fm:court|person|gift" a meeting and perhaps a gift, "fm:ask|person" the plea,
/// "fm:break|person" ends a courtship, "fm:teach|child|course|skill|tutor" sets a child's course and tutor (tutor 0: none; the hero's own id: himself).
/// </summary>
public static class FamilyDesk
{
    public const string Prefix = "fm:";

    private static string U(ulong v) => v.ToString(CultureInfo.InvariantCulture);

    private static HeroCommand Make(string tag, long sec, int settlement = -1) => new() { Kind = HeroCommandKind.Family, IssuedSec = sec, Tag = Prefix + tag, Origin = CommandOrigin.Human, Settlement = settlement };

    public static HeroCommand Court(EntityId person, long giftMites, long sec) => Make("court|" + U(person.Value) + "|" + giftMites.ToString(CultureInfo.InvariantCulture), sec);
    public static HeroCommand Ask(EntityId person, long sec) => Make("ask|" + U(person.Value), sec);
    public static HeroCommand Break(EntityId person, long sec) => Make("break|" + U(person.Value), sec);
    /// <summary>Arranges a match for an unmarried member of the household with a candidate (P6-E3, 4.2): the dowry within half the class share either way (-1: the share), <paramref name="defy"/> to marry a fief's heir without the overlord's consent.</summary>
    public static HeroCommand Arrange(EntityId member, EntityId candidate, long dowry, bool defy, long sec) =>
        Make("arrange|" + U(member.Value) + "|" + U(candidate.Value) + "|" + dowry.ToString(CultureInfo.InvariantCulture) + "|" + (defy ? 1 : 0), sec);
    /// <summary>The best candidate for a member at the class share of the dowry, by the answering head's utility (what the panel's Arrange button sends).</summary>
    public static HeroCommand ArrangeBest(EntityId member, long sec) => Make("arrange|" + U(member.Value) + "|-|-1|0", sec);
    /// <summary>A course for a child with a tutor (the hero's own person id is 0, so "no tutor" is the dash).</summary>
    public static HeroCommand Teach(EntityId child, Course course, Skill skill, EntityId? tutor, long sec) =>
        Make("teach|" + U(child.Value) + "|" + (int)course + "|" + (int)skill + "|" + (tutor is { } t ? U(t.Value) : "-"), sec);

    private static HeroResult R(HeroCommand c, bool ok, string key, params object[] args) =>
        new(c.Seq, c.Kind, ok ? HeroResultCode.Ok : HeroResultCode.Refused, HeroText.Of(key, args), 0, 0, c.IssuedSec);

    public static HeroResult Apply(HeroContext ctx, HeroCommand c)
    {
        if (!c.Tag.StartsWith(Prefix, StringComparison.Ordinal)) return new HeroResult(c.Seq, c.Kind, HeroResultCode.Invalid, HeroText.Of("HERO_UNKNOWN_COMMAND"), 0, 0, c.IssuedSec);
        var f = c.Tag[Prefix.Length..].Split('|');
        ulong N(int i) => i < f.Length && ulong.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        int day = (int)(c.IssuedSec / 86400);
        var w = FamilyWorld.From(ctx.State, ctx.Content);
        if (w.HeroRow < 0) return R(c, false, "fm_no_hero");
        CourtResult? r = null;
        switch (f[0])
        {
            case "court": r = Courting.Court(w, N(1), (long)N(2), day); break;
            case "ask": r = Courting.Ask(w, N(1), day); break;
            case "break": r = Courting.Break(w, N(1)); break;
            case "arrange": r = ArrangeFor(w, N(1), f.Length > 2 && f[2] != "-" ? N(2) : 0, f.Length > 2 && f[2] == "-", f.Length > 3 && long.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dow) ? dow : -1, f.Length > 4 && f[4] == "1", day); break;
            case "teach": r = Teach(w, N(1), (Course)N(2), (Skill)N(3), f.Length > 4 && f[4] != "-" ? new EntityId(N(4)) : null, day); break;
            default: return new HeroResult(c.Seq, c.Kind, HeroResultCode.Invalid, HeroText.Of("HERO_MAKES_NO_SENSE"), 0, 0, c.IssuedSec);
        }
        if (w.St.Family is { IsEmpty: true }) w.St.Family = null;
        return R(c, r.Ok, r.Key, r.Args);
    }

    private static CourtResult ArrangeFor(FamilyWorld w, ulong member, ulong candidate, bool best, long dowry, bool defy, int day)
    {
        if (best)
        {
            int m = w.People.IndexOf(new EntityId(member));
            var list = m >= 0 ? Matches.Candidates(w, m, day, 1) : [];
            if (list.Count == 0) return new(false, "fm_no_candidates");
            candidate = list[0].Person;
        }
        return Matches.Arrange(w, member, candidate, dowry, defy, day);
    }

    /// <summary>Sets a child's course and tutor (4.4): the child is a member of the hero's household and old enough to be taught; the tutor is the hero, a household member, the priest of the place or anyone alive (a hired tutor is paid by the day).</summary>
    public static CourtResult Teach(FamilyWorld w, ulong child, Course course, Skill skill, EntityId? tutor, int day)
    {
        var pt = w.People; int c = pt.IndexOf(new EntityId(child)), hero = w.HeroRow;
        if (c < 0 || pt.Status[c] != PersonStatus.Alive || pt.Household[c] != pt.Household[hero] || c == hero) return new(false, "fm_not_your_child");
        if (pt.AgeYears(c, day) >= w.S.AdultAge) return new(false, "fm_grown");
        if (course == Course.Trade && skill == Skill.None) return new(false, "fm_no_skill");
        if (tutor is { } tid && tid != w.HeroId)
        {
            int t = pt.IndexOf(tid);
            if (t < 0 || pt.Status[t] != PersonStatus.Alive) return new(false, "fm_no_tutor");
        }
        var book = w.Book;
        if (!book.Upbringings.TryGetValue(child, out var u)) book.Upbringings[child] = u = new Upbringing { Child = child };
        u.Course = course; u.HasTutor = course != Course.None && tutor != null; u.Tutor = u.HasTutor ? tutor!.Value.Value : 0; u.TradeSkill = course == Course.Trade ? skill : Skill.None;
        return new(true, course == Course.None ? "fm_lessons_stopped" : "fm_lessons_set", pt.FullName(c));
    }
}
