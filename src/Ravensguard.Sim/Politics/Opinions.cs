using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Rule;

namespace Ravensguard.Sim.Politics;

/// <summary>
/// Opinions (diplomacy-and-ai-lords 3): the integer value (-100..100) of what one party thinks of another is <c>RuleBook.Opinions</c>, the only opinion store; the modifiers behind it (kind, size, day, the entry that explains it)
/// are the <see cref="PoliticsBook.Mods"/>. A modifier moves the value when it is added and again when it decays at a season's start, so the value always equals what was added less what faded (plus whatever rule and income wrote
/// directly). A realm's opinion of another is its ruler's, kept under the realms' treasury keys.
/// </summary>
public static class Opinions
{
    public const int Max = 100;

    public static int Value(RuleBook rb, ulong who, ulong of) => Math.Clamp(rb.Opinion(who, of), -Max, Max);

    /// <summary>The sum of one kind's sizes now standing for a pair (a cap is measured against it).</summary>
    public static int Standing(PoliticsBook pb, ulong a, ulong b, string kind) { int n = 0; foreach (var m in pb.Mods) if (m.A == a && m.B == b && m.Kind == kind) n += m.Amount; return n; }

    /// <summary>
    /// Adds a modifier to what <paramref name="a"/> thinks of <paramref name="b"/>: the kind's own size unless <paramref name="amount"/> says another, held to the kind's cap and to plus or minus 100. Returns the points actually added
    /// (0 when the cap was already reached). The row is kept so the screen can say why.
    /// </summary>
    public static int Add(PoliticsBook pb, RuleBook rb, ulong a, ulong b, string kind, int day, ulong cause = 0, int? amount = null)
    {
        if (a == 0 || b == 0 || a == b) return 0;
        pb.OpinionKinds.TryGetValue(kind, out var def);
        int want = amount ?? def?.Amount ?? 0; if (want == 0) return 0;
        if (def != null && def.Cap > 0)
        {
            int have = Standing(pb, a, b, kind);
            want = want > 0 ? Math.Min(want, Math.Max(0, def.Cap - have)) : Math.Max(want, Math.Min(0, -def.Cap - have));
            if (want == 0) return 0;
        }
        int old = rb.Opinion(a, b);
        int now = Math.Clamp(old + want, -Max, Max);
        int applied = now - old; if (applied == 0) return 0;
        rb.AddOpinion(a, b, applied);
        pb.Mods.Add(new OpinionMod { A = a, B = b, Kind = kind, Amount = applied, Day = day, Cause = cause });
        return applied;
    }

    /// <summary>A gift's size: <c>+1 per 10 crowns</c> (1000 mites) at least +1 for any real gift, held to the cap of +20 (DA-10).</summary>
    public static int GiftPoints(PoliticsSettingsDef s, long mites) => mites <= 0 ? 0 : (int)Math.Max(1, mites / 1000 * s.GiftPerTenCrowns);

    /// <summary>
    /// The season's fading (3, DA-10): each modifier moves toward zero by its kind's decay; one that reaches zero is dropped. <paramref name="holds"/> says a modifier stays whole (a war's -50 holds while the war lasts, a
    /// marriage's +30 while the marriage lasts). The value follows by the same step, so value and modifiers agree. Returns how many modifiers changed.
    /// </summary>
    public static int Decay(PoliticsBook pb, RuleBook rb, Func<OpinionMod, bool>? holds = null)
    {
        int n = 0;
        for (int i = pb.Mods.Count - 1; i >= 0; i--)
        {
            var m = pb.Mods[i];
            if (!pb.OpinionKinds.TryGetValue(m.Kind, out var def) || def.DecayPerSeason <= 0) continue;
            if (holds != null && holds(m)) continue;
            int step = Math.Min(def.DecayPerSeason, Math.Abs(m.Amount)) * Math.Sign(m.Amount);
            m.Amount -= step; rb.AddOpinion(m.A, m.B, -step); n++;
            if (m.Amount == 0) pb.Mods.RemoveAt(i);
        }
        return n;
    }

    /// <summary>The modifiers behind a value, the largest three by size (ties by day, then kind): what the Opinions tab shows with links to their causes.</summary>
    public static List<OpinionMod> Top(PoliticsBook pb, ulong a, ulong b, int n = 3)
    {
        var l = new List<OpinionMod>(); foreach (var m in pb.Mods) if (m.A == a && m.B == b) l.Add(m);
        l.Sort((x, y) => { int c = Math.Abs(y.Amount).CompareTo(Math.Abs(x.Amount)); if (c != 0) return c; c = y.Day.CompareTo(x.Day); return c != 0 ? c : string.CompareOrdinal(x.Kind, y.Kind); });
        return l.Count > n ? l.GetRange(0, n) : l;
    }

    /// <summary>Every party that <paramref name="who"/> holds an opinion of, from the rule book's store, in ascending id: what the Faction screen walks.</summary>
    public static IEnumerable<(ulong Of, int Value)> Of(RuleBook rb, ulong who) { foreach (var ((w, o), v) in rb.Opinions) if (w == who) yield return (o, Math.Clamp(v, -Max, Max)); }
}
