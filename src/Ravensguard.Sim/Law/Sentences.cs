using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Law;

/// <summary>What the court would do to the accused of one crime (crime-and-justice 4.6, 4.7): the key and the step, what it comes to, and why it is that step (the law panel's, mercy, harshness, clergy).</summary>
public sealed record Expected(string Key, int Step, string Kind, long Mites, int Days, int Lashes, int Display, int Feud, string Unpaid, int Severity, int Priors, int Mercy, bool Clergy, bool LordsLaw, string CultureKey)
{
    /// <summary>The text key of the punishment in en.csv (<c>jl_pun_&lt;key&gt;</c>), for the cell and the court panel.</summary>
    public string TextKey => "jl_pun_" + Key;
}

/// <summary>
/// The sentence table and its rules (crime-and-justice 3, 4.6): a crime's key comes from the lord's own law for the fief, else the culture of the place, else a plain fine; the step is the count of the
/// accused's earlier convictions of the same kind; mercy lowers it one step (standing with the fief of 300 or more, full restitution before sentence) and a lord's ill opinion (-50 or less) raises it; when
/// both press, a draw (0x612, key crime) decides at 20% each way. Benefit of clergy turns it into penance. Pure: the same crime gives the same answer however often it is asked.
/// </summary>
public static class Sentences
{
    /// <summary>The culture's own crime id for ours: burglary and pickpocketing are theft, grave robbing and sacrilege are heresy's company, and so on.</summary>
    public static string Alias(string kind) => kind switch
    {
        "burglary" or "pickpocketing" or "embezzlement" => "theft",
        "unlicensed_mining" or "smuggling" => "poaching",
        "grave_robbing" or "sacrilege" => "heresy",
        "debt_default" => "debt",
        "jailbreak" or "bribery" or "harbouring" or "slaving" or "trespass" or "squatting" => "assault",
        _ => kind,
    };

    /// <summary>The punishment key for a crime at a place: the fief's law, the culture's table, else <c>fine</c>.</summary>
    public static (string Key, bool LordsLaw, string Culture) KeyFor(LawWorld w, JusticeBook jb, int fief, int settlement, string kind)
    {
        string culture = w.CultureKey(settlement, kind) ?? w.CultureKey(settlement, Alias(kind)) ?? "fine";
        if (fief >= 0 && jb.Laws.TryGetValue((fief, kind), out var own) && w.Law.Punishment(own) != null) return (own, own != culture, culture);
        return (w.Law.Punishment(culture) != null ? culture : "fine", false, culture);
    }

    /// <summary>The keys a lord may put in a crime's place: any table as mild as the culture's own or milder (the law panel's list), in key order.</summary>
    public static List<string> Allowed(LawWorld w, string cultureKey)
    {
        int sev = w.Law.Punishment(cultureKey)?.Severity ?? 1;
        return [.. w.Law.Punishments.Where(p => p.Severity <= sev).Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal)];
    }

    /// <summary>The step a table gives for a count of earlier convictions.</summary>
    public static int StepFor(PunishmentDef d, int priors) => Math.Clamp(priors, 0, d.Steps.Count - 1);

    /// <summary>One step of a table as an <see cref="Expected"/> (the numbers worked out for this crime's value).</summary>
    public static Expected Make(LawWorld w, PunishmentDef d, int step, CrimeRecord rec, int priors, int mercy, bool clergy, bool lordsLaw, string culture)
    {
        step = Math.Clamp(step, 0, d.Steps.Count - 1);
        var s = d.Steps[step];
        long mites = s.Mites + (long)s.ValueX * rec.ValueMites;
        int days = Math.Max(s.Days, s.DaysDiv > 0 ? (int)(rec.ValueMites / s.DaysDiv) : 0);
        return new Expected(d.Id, step, s.Kind, mites, days, s.Lashes, s.Display, s.Feud, s.Unpaid, d.Severity, priors, mercy, clergy, lordsLaw, culture);
    }

    /// <summary>
    /// What the court of <paramref name="fief"/> would do to <paramref name="accused"/> for <paramref name="rec"/> now. <paramref name="npcLord"/>: the mercy tie is decided by a draw (an NPC lord); the hero as
    /// judge sees both pressures and chooses himself.
    /// </summary>
    public static Expected Expect(LawWorld w, CrimeBook cb, JusticeBook jb, CrimeRecord rec, EntityId accused, int fief, bool npcLord = true)
    {
        var (key, lords, culture) = KeyFor(w, jb, fief, rec.Settlement, rec.Kind);
        var def = w.Law.Punishment(key) ?? w.Law.Punishment("fine") ?? LawContent.DefaultPunishments()[0];
        int priors = cb.ConvictionsOf(accused, rec.Kind);
        if (jb.Clergy.Contains(rec.Id) && w.LiteracyOf(accused) >= w.S.ClergyLiteracy && w.ChurchCourts(rec.Settlement) && w.Law.Punishment("penance") is { } pen)
            return Make(w, pen, 0, rec, priors, 0, true, lords, culture);
        int shift = Mercy(w, jb, rec, accused, fief, npcLord);
        return Make(w, def, StepFor(def, priors) + shift, rec, priors, shift, false, lords, culture);
    }

    /// <summary>The mercy shift of a case: -1, 0 or +1 (4.6).</summary>
    public static int Mercy(LawWorld w, JusticeBook jb, CrimeRecord rec, EntityId accused, int fief, bool npcLord = true)
    {
        bool down = (fief >= 0 && w.StandingWithFief(accused, fief) >= w.S.MercyStandingPm) || jb.Restituted.Contains(rec.Id);
        var lord = fief >= 0 ? w.LordOf(fief) : EntityId.None;
        bool up = lord != EntityId.None && w.OpinionPm(lord, accused) <= w.S.HarshOpinionPm;
        if (down && up)
        {
            if (!npcLord) return 0;
            int r = DetRng.Range(w.Seed, 0, (ulong)rec.Id, RngStreams.LordMercyTie, 0, 0, 100);
            return r < w.S.MercyTiePct ? -1 : r >= 100 - w.S.MercyTiePct ? 1 : 0;
        }
        return down ? -1 : up ? 1 : 0;
    }

    /// <summary>The choices a judge has for a case: every table as mild or milder than the culture's, and the culture's own, at the step of the accused's priors; harsher ones are allowed but flagged.</summary>
    public static List<Expected> Alternatives(LawWorld w, CrimeBook cb, JusticeBook jb, CrimeRecord rec, EntityId accused)
    {
        var (_, _, culture) = KeyFor(w, jb, -1, rec.Settlement, rec.Kind);
        int priors = cb.ConvictionsOf(accused, rec.Kind);
        var l = new List<Expected>();
        foreach (var d in w.Law.Punishments.OrderBy(p => p.Severity).ThenBy(p => p.Id, StringComparer.Ordinal))
            l.Add(Make(w, d, StepFor(d, priors), rec, priors, 0, false, d.Id != culture, culture));
        return l;
    }
}
