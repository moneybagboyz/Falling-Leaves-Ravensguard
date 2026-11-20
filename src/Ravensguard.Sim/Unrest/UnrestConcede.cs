using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Taxes;

namespace Ravensguard.Sim.Unrest;

/// <summary>
/// What a lord can do about unrest (unrest-and-revolt 4.1 concessions, 4.4) and the events other systems report to it: lowering the tax, a grain dole, a pardon, an enclosure of a common, an eviction, a harsher sentence,
/// a trigger. Concessions take effect at once: the points they lift are not counted at the next daily pass. Every act is a chronicle entry that names its cause.
/// </summary>
public static class UnrestConcede
{
    private static Cause Why(string key) => new(CauseKind.Unrest, null, "", key);

    // ------------------------------------------------------------------ events others report

    /// <summary>A sentence harsher than the culture's key falls on someone (UR-4): one point of <c>kin_punished</c> for a year on his household; a negative <paramref name="sign"/> is an acquittal of the plainly guilty, which angers the victim's kin (<c>wronged</c>).</summary>
    public static void KinHarsh(UnrestBook b, SimWorld w, EntityId person, int sign, int day, ulong entry = 0)
    {
        int row = w.People.IndexOf(person); if (row < 0) return;
        int hh = w.People.Household[row]; if (hh < 0 || hh >= w.Households.Count) return;
        b.Add(w.Households.IdOf(hh).Value, sign >= 0 ? "kin_punished" : "wronged", day, entry);
    }

    /// <summary>A common is closed for a quarry or a business (UR-9, the Phase 5 hook): one point for two years to the households that used it (the settlement's landholders).</summary>
    public static int Enclose(UnrestBook b, SimWorld w, int settlement, int day, ulong entry = 0)
    {
        int n = 0; var hht = w.Households;
        for (int h = 0; h < hht.Count; h++)
            if (hht.MemberCount[h] > 0 && hht.Settlement[h] == settlement && hht.ParcelCount[h] > 0) { b.Add(hht.IdOf(h).Value, "enclosure", day, entry); n++; }
        return n;
    }

    /// <summary>A household is evicted (the tax's hook): its neighbours hold it against the lord for a season, and a riot may follow at once if the settlement is already restless (the trigger, 4.1).</summary>
    public static void Evicted(UnrestBook b, SimWorld w, int settlement, EntityId household, int day, ulong entry)
    {
        var hht = w.Households;
        for (int h = 0; h < hht.Count; h++)
            if (hht.MemberCount[h] > 0 && hht.Settlement[h] == settlement && hht.IdOf(h) != household) b.Add(hht.IdOf(h).Value, "eviction", day, entry);
        Trigger(b, settlement, "eviction", day);
    }

    /// <summary>A trigger of a riot (an eviction, the hanging of a local, a tallage): at 350 unrest or more the next daily pass raises one.</summary>
    public static void Trigger(UnrestBook b, int settlement, string kind, int day)
    {
        var su = b.Peek(settlement) ?? new SettlementUnrest(); su.TriggerDay = day; su.TriggerKind = kind; b.Settlements[settlement] = su;
    }

    // ------------------------------------------------------------------ the lord's concessions

    /// <summary>Lowers a fief's tax one level (the grievance of the level is a point less at once). False when it is at the lowest.</summary>
    public static bool LowerTax(UnrestCtx c, int fief, string noteKey = "ch_note_ur_unrest_high")
    {
        var tax = c.R.Tax; if (tax == null) return false;
        var lv = tax.LevelOfFief(fief); if (lv <= TaxLevel.Low) return false;
        tax.SetLevel(fief, lv - 1);
        Entry(c, fief, "unrest_concession", "ur_c_tax", noteKey);
        return true;
    }

    /// <summary>The lord's barn feeds the hungry: each hungry household of the fief gets the dole's coin from the payer (the fief's chest when none is named) and its hunger points are cleared while the dole lasts. Returns the households fed.</summary>
    public static int Dole(UnrestCtx c, int fief, Party payer, string noteKey = "ch_note_ur_unrest_high")
    {
        var e = c.W.Economy; int fed = 0; long coin = 0;
        if (payer.IsNone) payer = Party.OfFief(fief);
        foreach (int h in c.R.HouseholdsOfFief(fief))
        {
            if ((e.Grievances[h] & Grievance.Hunger) == 0) continue;
            long paid = c.R.PayUpTo(payer, Party.OfHousehold(c.W.Households.IdOf(h)), c.S.DoleMitesPerHousehold, TransferReason.Alms);
            if (paid > 0) { fed++; coin += paid; }
        }
        c.B.DoleUntil[fief] = c.Day + c.S.DoleDays;
        Entry(c, fief, "unrest_concession", "ur_c_dole", noteKey, coin);
        return fed;
    }

    /// <summary>A pardon: the kin points of the fief's households are cleared, the leaders of its ended riots are pardoned and their open crimes dropped.</summary>
    public static int Pardon(UnrestCtx c, int fief, string noteKey = "ch_note_ur_pardon_mercy")
    {
        int cleared = 0;
        foreach (int h in c.R.HouseholdsOfFief(fief))
        {
            ulong id = c.W.Households.IdOf(h).Value;
            if (!c.B.Rows.TryGetValue(id, out var l)) continue;
            cleared += l.RemoveAll(r => r.Kind is "kin_punished" or "wronged");
            if (l.Count == 0) c.B.Rows.Remove(id);
        }
        foreach (var r in c.B.Riots.Where(x => x.Fief == fief && x.State == RiotState.Ended))
            if (c.B.Pardoned.Add(r.Leader)) { cleared++; c.Links.Pardon?.Invoke(new EntityId(r.Leader)); }
        foreach (var v in c.B.Revolts.Where(x => x.Fief == fief && x.State == RevoltState.Lost && !x.Bonded))
            if (c.B.Pardoned.Add(v.Leader)) { cleared++; c.Links.Pardon?.Invoke(new EntityId(v.Leader)); }
        Entry(c, fief, "unrest_concession", "ur_c_pardon", noteKey);
        return cleared;
    }

    /// <summary>A harsh lord hangs the leaders: the riot's leader's household and his kin carry the grievance for a year (the ladder feeds on it).</summary>
    public static bool Punish(UnrestCtx c, int fief, string noteKey = "ch_note_dp_greedy")
    {
        var r = c.B.Riots.LastOrDefault(x => x.Fief == fief && x.State == RiotState.Ended && !c.B.Pardoned.Contains(x.Leader)); if (r == null) return false;
        KinHarsh(c.B, c.W, new EntityId(r.Leader), 1, c.Day, r.EndEntry);
        var s = r.Settlement; Trigger(c.B, s, "hanging", c.Day);
        Entry(c, fief, "unrest_concession", "ur_c_punish", noteKey);
        return true;
    }

    private static void Entry(UnrestCtx c, int fief, string type, string action, string noteKey, long coin = 0)
    {
        var lord = c.LordOf(fief); var place = c.R.SeatPlace(fief);
        var ps = new List<ChronParam> { ChronParam.Place("place", place), ChronParam.TextKey("action", action), ChronParam.Mites("coin", coin) };
        var subj = new List<EntityId> { place }; if (lord >= 0) { ps.Add(ChronParam.Person("lord", new EntityId((ulong)lord))); subj.Insert(0, new EntityId((ulong)lord)); }
        c.Chron.Chron(c.Now, type, "ch_ur_concession", ps, subj, [Why(noteKey)]);
    }

    // ------------------------------------------------------------------ the lord's utility and the hero's mediation

    /// <summary>The utility (0..1000) of the best concession (a lower tax or a dole) the fief's lord would make now, and the choice; null when he has none above zero (UR: the mediator's test).</summary>
    public static LordBrain.Choice? BestConcession(UnrestCtx c, int fief)
    {
        if (c.PB == null || c.RB.FiefOf(fief) is not { Holder: >= 0 } f) return null;
        var pc = PoliticsEngine.Context(c.W, c.PB, c.RB, c.Chron, c.Day);
        var inp = new LordBrain.Inputs(pc, f); LordBrain.Choice? best = null;
        foreach (var def in c.PB.Actions)
        {
            if (def.Id is not ("set_tax_down" or "dole")) continue;
            var (u, note) = Considerations.Evaluate(def, inp.Get);
            if (best == null || u > best.Utility) best = new LordBrain.Choice(def, u, note);
        }
        return best;
    }

    /// <summary>
    /// The hero mediates a riot (4.2): his Speech against the unrest over ten; if he wins and the lord's utility for a concession is at least the line, the concession is made on the spot and the crowd goes home at the next hour.
    /// Returns the result key.
    /// </summary>
    public static string Mediate(UnrestCtx c, Riot riot, int speech)
    {
        if (riot.State == RiotState.Ended) return "ur_refused_over";
        var su = c.B.Peek(riot.Settlement); int u = su?.Unrest ?? riot.UnrestPm;
        if (speech < u / 10) return "ur_mediate_failed";
        var best = BestConcession(c, riot.Fief);
        if (best == null || best.Utility < c.S.ConcessionUtility) return "ur_mediate_lord_refuses";
        var pc = PoliticsEngine.Context(c.W, c.PB!, c.RB, c.Chron, c.Day);
        if (c.RB.FiefOf(riot.Fief) is not { } f || !LordBrain.Act(pc, f, best)) return "ur_mediate_lord_refuses";
        riot.Conceded = true; riot.HeroSide = 4;
        return "ur_mediated";
    }
}
