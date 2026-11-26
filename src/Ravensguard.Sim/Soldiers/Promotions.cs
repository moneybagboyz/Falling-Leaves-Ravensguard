using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Rule;

namespace Ravensguard.Sim.Soldiers;

/// <summary>An offered promotion with the new template's gaps (4.5): what the man would be missing if he were kitted from the armoury now.</summary>
public sealed record PromotionView(PromotionOffer Offer, IReadOnlyList<FillGap> Gaps);

/// <summary>
/// P7-SC-B (soldiers-and-companies 4.5): promotions offered at a season's first day to every soldier of a lord who meets the next role's skills (never a dubbed role), accepted or refused by the lord (the hero in the Army tab,
/// an AI lord while the wage roll stays within the share of his receipts), and the dubbing of a man-at-arms into a knight. Pure over the book, in person order; no draw.
/// </summary>
public static class Promotions
{
    public static bool IsSeasonStart(int day) => day % TimeConst.DaysPerSeason == 0;

    /// <summary>The best role (lowest priority number) among a role's <c>next</c> that the person meets, or null.</summary>
    public static string? NextFor(SoldierWorld sw, SoldierRow row)
    {
        var cur = sw.Roles.Role(row.Role); if (cur == null) return null;
        string? best = null; int bp = int.MaxValue;
        foreach (var n in cur.Next)
        {
            var nr = sw.Roles.Role(n); if (nr == null || nr.Dubbed || !sw.Roles.Meets(n, s => sw.SkillOf(row.Person, s))) continue;
            if (nr.Priority < bp) { bp = nr.Priority; best = n; }
        }
        return best;
    }

    /// <summary>
    /// The season's offers for a lord's soldiers: on a season's first day, one offer a man who qualifies (kept in the book until answered). With an armoury, each comes with the gaps the new template would show.
    /// Not a season's first day: nothing new (offers stand).
    /// </summary>
    public static List<PromotionView> Offer(SoldierWorld sw, long lord, int day, long armoury = -1)
    {
        if (IsSeasonStart(day))
            foreach (var row in sw.Book.Rows.Values.Where(r => r.Lord == lord))
                if (NextFor(sw, row) is { } n) sw.Book.Offers[row.Person] = new PromotionOffer(row.Person, n, day);
        return Pending(sw, lord, armoury);
    }

    /// <summary>The offers waiting for a lord, with the gaps of each new template.</summary>
    public static List<PromotionView> Pending(SoldierWorld sw, long lord, long armoury = -1)
    {
        var res = new List<PromotionView>();
        foreach (var o in sw.Book.Offers.Values)
        {
            if (sw.Book.Of(o.Person) is not { } row || row.Lord != lord) continue;
            IReadOnlyList<FillGap> gaps = [];
            if (armoury >= 0)
            {
                var plan = sw.Plan(armoury, [new FillSoldier(o.Person, o.Role, "", sw.SkillFor(o.Person, o.Role))]);
                gaps = plan.Soldiers.Count > 0 ? plan.Soldiers[0].Gaps : [];
            }
            res.Add(new PromotionView(o, gaps));
        }
        return res;
    }

    /// <summary>The lord accepts one offer: the man holds the new role (his template reset to the role's own), the wage follows, a chronicle entry. False when there is no such offer.</summary>
    public static bool Accept(SoldierWorld sw, ulong person, int day, RuleChron chron, EntityId lordId)
    {
        if (!sw.Book.Offers.TryGetValue(person, out var o) || sw.Book.Of(person) is not { } row) return false;
        sw.Book.Offers.Remove(person);
        row.Role = o.Role; row.Template = ""; row.PromotedDay = day;
        chron.Chron(GameTime.FromGameHours((long)day * 24), "soldier_promoted", "ch_so_promoted",
            [ChronParam.Person("person", new EntityId(person)), ChronParam.TextKey("role", "so_role_" + o.Role), ChronParam.Person("lord", lordId)],
            [new EntityId(person), lordId], [new Cause(CauseKind.Campaign, null, "", "ch_note_so_skill")]);
        return true;
    }

    public static bool Refuse(SoldierWorld sw, ulong person) => sw.Book.Offers.Remove(person);

    /// <summary>
    /// An AI lord answers his offers: he accepts them in person order while the wage roll after each stays within <paramref name="budgetMitesPerDay"/> (the share of his receipts a day).
    /// The rest are refused (dropped). Returns the number accepted.
    /// </summary>
    public static int AcceptWithin(SoldierWorld sw, long lord, int day, RuleChron chron, EntityId lordId, long budgetMitesPerDay)
    {
        long roll = WageRoll.DailyMites(sw, lord); int n = 0;
        foreach (var o in sw.Book.Offers.Values.Where(o => sw.Book.Of(o.Person)?.Lord == lord).ToList())
        {
            var row = sw.Book.Of(o.Person)!;
            long rise = WageRoll.WageMites(sw, o.Role) - WageRoll.WageMites(sw, row.Role);
            if (roll + rise <= budgetMitesPerDay && Accept(sw, o.Person, day, chron, lordId)) { roll += rise; n++; }
            else Refuse(sw, o.Person);
        }
        return n;
    }

    /// <summary>Whether a man can be dubbed: a man-at-arms who meets the knight's skills and has a war horse.</summary>
    public static bool CanDub(SoldierWorld sw, ulong person)
        => sw.Book.Of(person) is { Role: "man_at_arms" } && sw.Roles.Meets("knight", s => sw.SkillOf(person, s), dubbed: true) && sw.HasWarHorse(person);

    /// <summary>
    /// The lord dubs him: the role is knight (wage 150 cp a day with it), a chronicle entry carrying the lord's fief's standing of +100 through the deed table (<c>knight_dubbed</c>: <c>fief_pm</c> 100). False when he cannot be dubbed.
    /// <paramref name="because"/> is the promotion entry that led to it, when there is one: the cause; else the entry is a root with its note.
    /// </summary>
    public static bool Dub(SoldierWorld sw, ulong person, int day, RuleChron chron, EntityId lordId, EntityId? because = null)
    {
        if (!CanDub(sw, person)) return false;
        var row = sw.Book.Of(person)!;
        row.Role = "knight"; row.Template = ""; row.PromotedDay = day; sw.Book.Offers.Remove(person);
        chron.Chron(GameTime.FromGameHours((long)day * 24), "knight_dubbed", "ch_so_dubbed",
            [ChronParam.Person("knight", new EntityId(person)), ChronParam.Person("lord", lordId)],
            [new EntityId(person), lordId], [new Cause(CauseKind.Campaign, because, "", "ch_note_so_dubbed")]);
        return true;
    }
}
