using Ravensguard.Sim.Core;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.Politics;

/// <summary>
/// P7-0B: history's realm rulers grow old in play. Each season a ruler of a realm that history made (not a founded realm, not one the hero rules) dies of old age with the history's own rate
/// (<see cref="HistoryRules.OldAgePctYear"/>, a quarter of the year's chance a season), and the crown passes by the history's own heir order (children, then siblings, then kin of the dynasty;
/// <c>HistoryGenerator.FindHeir</c>). The new ruler is written to the realm's row, the dead one to <see cref="PoliticsBook.RulerDeaths"/> (history itself stays as it was made). A realm with no heir
/// of that order keeps its ruler this season: the line does not fail in play (a later part may). Draws: <c>RngStreams.DiplomacyTies</c>, index 5, keyed on the ruler.
/// </summary>
public static class RulerSuccession
{
    private const int DrawIndex = 5;

    /// <summary>The ruler's age in years on a game day: history's own count at its end, plus the game years since.</summary>
    public static int AgeOn(PoliticsCtx c, Notable n) => (c.H!.Years - n.BirthYear) * c.H.AgingPace + c.Day / TimeConst.DaysPerYear;

    public static void Season(PoliticsCtx c)
    {
        var h = c.H; if (h == null) return;
        foreach (int realm in c.AliveRealms().ToList())
        {
            var row = c.B.RealmIfAny(realm);
            if (row is { Founded: true } || c.RB.Rulers.ContainsKey(realm) || (uint)realm >= (uint)h.Realms.Count) continue;
            var ruler = h.GetNotable(new EntityId(c.RulerOf(realm)));
            if (ruler == null || c.B.RulerDeaths.ContainsKey(ruler.Id.Value)) continue;
            int age = AgeOn(c, ruler);
            int pct = HistoryRules.OldAgePctYear(age);
            if (pct <= 0 || DetRng.Range(c.Seed, c.Day, ruler.Id.Value, RngStreams.DiplomacyTies, DrawIndex, 0, 10_000) >= pct * 25) continue;
            var heir = FindHeir(c, h.Realms[realm], ruler);
            if (heir == null) continue;
            Die(c, realm, ruler, heir, age);
        }
    }

    private static void Die(PoliticsCtx c, int realm, Notable dead, Notable heir, int age)
    {
        var death = c.Chron.Chron(RuleCtx.At(c.Day), "realm_ruler_died", "ch_dp_ruler_died", [ChronParam.Person("ruler", dead.Id), ChronParam.Count("age", age)],
            [dead.Id, c.R.RealmKey(realm)], [new Cause(CauseKind.Death, null, NoteKey: "ch_note_dp_old_age")]);
        c.B.RulerDeaths[dead.Id.Value] = c.Day;
        c.B.RealmOf(realm).Ruler = heir.Id.Value;
        c.Chron.Chron(RuleCtx.At(c.Day), "realm_succession", "ch_dp_succession", [ChronParam.Person("heir", heir.Id), ChronParam.Person("dead", dead.Id)], [heir.Id, c.R.RealmKey(realm)], [new Cause(CauseKind.Death, death)]);
    }

    /// <summary>The history's order: the dead man's children (sons before daughters, eldest first), his father's other children, then dynasty kin of 16 and over, eldest first; the living of the realm only.</summary>
    public static Notable? FindHeir(PoliticsCtx c, RealmState r, Notable dead)
    {
        var h = c.H!; var kids = new List<Notable>(); var sibs = new List<Notable>(); var others = new List<Notable>();
        foreach (var n in h.Notables)
        {
            if (!n.Alive || c.B.RulerDeaths.ContainsKey(n.Id.Value) || n.Id == dead.Id || n.Realm != r.Index || n.Role is NotableRole.Ruler or NotableRole.General or NotableRole.Rebel or NotableRole.Outlaw or NotableRole.Saint or NotableRole.MasterCrafter) continue;
            if (n.Father == dead.Id || n.Mother == dead.Id) kids.Add(n);
            else if (dead.Father.Value != 0 && n.Father == dead.Father) sibs.Add(n);
            else if (n.Dynasty == dead.Dynasty && AgeOn(c, n) >= 16) others.Add(n);
        }
        int Order(Notable n) => (n.Female ? 1000 : 0) + (n.BirthYear + 500);
        int ByOrder(Notable a, Notable b) => Order(a) != Order(b) ? Order(a).CompareTo(Order(b)) : a.Id.CompareTo(b.Id);
        kids.Sort(ByOrder); sibs.Sort(ByOrder);
        others.Sort((a, b) => a.BirthYear != b.BirthYear ? a.BirthYear.CompareTo(b.BirthYear) : a.Id.CompareTo(b.Id));
        return kids.Count > 0 ? kids[0] : sibs.Count > 0 ? sibs[0] : others.Count > 0 ? others[0] : null;
    }
}
