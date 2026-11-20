using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;

namespace Ravensguard.Sim.Faith;

/// <summary>
/// Excommunication as a political tool (faith-and-church 4.6, P6-E2): the bishop bans a person whose standing with the faith has fallen below -800 while his sins go on, or a ruler whose opinion the bishop holds at
/// 50 or more asks for a rival's ban on a cause from the chronicle and pays 100 crowns. The ban is the mark (no wedding, burial or will witnessed: <see cref="ChurchServices.Allows"/>), standing -1000 with the
/// religion, a released oath (the vassals' fealty utility falls 40, <see cref="FealtyPenalty"/>), two grievances for each household of the banned lord's fiefs (renewed each season while it stands) and a casus belli
/// for each ruler of the faith. Penance lifts it: a tenth of his treasury to the see and a pilgrimage made since, or a new bishop's grace.
/// </summary>
public static class Excommunication
{
    /// <summary>The religion of a person, or of a history notable (a realm's ruler) by his culture; -1 for none.</summary>
    public static int ReligionOfAny(FaithOps.Pass p, ulong who)
    {
        int r = FaithOps.ReligionOf(p, new EntityId(who)); if (r >= 0) return r;
        var h = p.St.History?.GetNotable(new EntityId(who)); return h == null ? -1 : p.Religions.OfCulture(h.Culture);
    }

    /// <summary>The see that holds a person to account: the one of his faith in his realm (or the realm he rules), else the first of the faith.</summary>
    public static SeeRow? SeeFor(FaithOps.Pass p, ulong who, int religion, PoliticsCtx? pc)
    {
        var rel = p.Religions.Get(religion); if (rel == null) return null;
        int realm = FaithOps.RealmOfPerson(p, new EntityId(who));
        if (realm < 0 && pc != null) foreach (int r in pc.AliveRealms()) if (pc.RulerOf(r) == who) { realm = r; break; }
        return p.Book.SeeOf(rel.Id, realm) ?? p.Book.Sees.Values.FirstOrDefault(s => s.Religion == rel.Id);
    }

    private static PoliticsCtx? Politics(WorldState st, ContentDatabase db, int day)
    {
        var sim = st.HomeRecords?.Sim; var rule = RuleWiring.Bind(st, db);
        if (sim == null || rule == null) return null;
        var pb = st.Politics ??= new PoliticsBook(); PoliticsWiring.Bind(pb, db, st.Seed);
        return PoliticsEngine.Context(sim, pb, rule, RuleChron.Of(st.Chronicle, st.Ids), day);
    }

    /// <summary>The fealty utility a vassal loses toward a ruler the church has banned (4.6: 40): read by the lord brain's renege input, so an excommunicated overlord is likelier to be renounced.</summary>
    public static int FealtyPenalty(PoliticsCtx c, int realm)
    {
        var pol = c.R.W.Get<ChurchPolitics>(); if (pol == null || pol.Excomms.Count == 0) return 0;
        ulong ruler = c.RulerOf(realm);
        return ruler != 0 && pol.IsExcommunicated(ruler) ? c.R.W.Get<ChurchBook>()?.Settings.ExcommunicationFealty ?? 40 : 0;
    }

    // ============================================================ asking

    /// <summary>
    /// A ruler asks the bishop for a rival's excommunication (4.6): the bishop's opinion of the asker at 50 or more, a cause from the chronicle that names the rival (an oath broken, a sacrilege, a heresy, arson, grave
    /// robbing), 100 crowns as a gift to the bishop. Returns "" and the row on success, else the text key of the refusal.
    /// </summary>
    public static string Request(WorldState st, ContentDatabase db, EntityId asker, EntityId target, EntityId cause, int day, out ExcommRow? row)
    {
        row = null;
        var p = FaithOps.Of(st, db); if (p == null) return "fa_no_church";
        int ri = ReligionOfAny(p, target.Value); if (ri < 0) return "fa_no_faith";
        if (asker == target) return "fa_excomm_self";
        if (p.Pol.IsExcommunicated(target.Value)) return "fa_excomm_already";
        var e = st.Chronicle.Get(cause);
        if (e == null || !p.S.ExcommunicationCauses.Contains(e.TypeId) || !e.Subjects.Contains(target)) return "fa_excomm_no_cause";
        var pc = Politics(st, db, day);
        var see = SeeFor(p, target.Value, ri, pc);
        if (see == null || see.Bishop < 0 || !FaithEngine.IsLivingPriest(p.People, see.Bishop, out int brow)) return "fa_excomm_no_bishop";
        var bishop = p.People.IdOf(brow);
        if (pc == null || Opinions.Value(pc.RB, bishop.Value, asker.Value) < p.S.ExcommunicationOpinion) return "fa_excomm_cold";
        var from = FaithOps.PurseOf(st, asker); var to = FaithOps.PurseOf(st, bishop); var led = st.Economy!.Ledger;
        if (from.IsNone || to.IsNone) return "fa_excomm_no_bishop";
        if (led.Balance(from).Mites < p.S.ExcommunicationGiftMites) return "fa_cannot_afford";
        if (!led.Transfer(from, to, new Coin(p.S.ExcommunicationGiftMites), TransferReason.Gift)) return "fa_cannot_afford";
        row = Impose(st, db, p, pc, target, ri, bishop, asker, cause, day);
        return "";
    }

    // ============================================================ the ban

    /// <summary>Lays the ban (4.6): the mark, the entry, the standing, the opinions, the grievances and the casus belli. Returns the row.</summary>
    public static ExcommRow Impose(WorldState st, ContentDatabase db, FaithOps.Pass p, PoliticsCtx? pc, EntityId target, int religion, EntityId bishop, EntityId requester, EntityId cause, int day)
    {
        var marks = (st.Standing ??= new StandingBook()).Marks; var rel = p.Religions.Get(religion)!;
        marks.Excommunicate(target, religion);
        int row = FaithOps.RowOf(st, target); int settlement = row >= 0 ? p.People.HomeSettlement[row] : -1;
        var ps = new List<ChronParam> { ChronParam.Person("doer", target), ChronParam.Person("bishop", bishop) };
        if (FaithOps.PlaceOf(st, settlement) is var pl && pl != EntityId.None) ps.Add(ChronParam.Place("place", pl));
        bool asked = requester != EntityId.None;
        if (asked) ps.Add(ChronParam.Person("asker", requester));
        var subj = new List<EntityId> { target, bishop }; if (asked) subj.Add(requester);
        var entry = FaithOps.Chron(st, FaithOps.SecOf(day), asked ? "excommunication_asked" : "excommunicated", asked ? "ch_faith_excommunicated_asked" : "ch_faith_excommunicated", ps, subj, cause == EntityId.None ? null : cause,
            cause == EntityId.None ? "ch_note_excomm_sins" : "ch_note_excomm_cause");
        var x = new ExcommRow { Person = target.Value, Religion = rel.Id, Day = day, Entry = entry.Value, Cause = cause.Value, By = bishop.Value, Requester = requester.Value, PilgrimsBefore = p.Book.Pilgrimages };
        p.Pol.Excomms.Add(x);
        FaithStanding.Record(st, db, target, "excommunicated", entry, settlement, 1, 0, true, day);
        if (pc != null) Consequences(st, p, pc, target, religion, entry, day, true);
        return x;
    }

    /// <summary>The ban's weight on the politics: vassals' opinions (the oath is released: fealty utility), grievances in his fiefs, casus belli for the faith's rulers (the first day: all; later seasons: the grievances renew).</summary>
    private static void Consequences(WorldState st, FaithOps.Pass p, PoliticsCtx pc, EntityId target, int religion, EntityId entry, int day, bool first)
    {
        var rb = pc.RB; var pb = pc.B; var s = p.S; ulong t = target.Value;
        if (first)
        {
            var seen = new SortedSet<ulong>();
            foreach (var f in rb.Fiefs.Values)
            {
                if (f.Holder < 0 || f.Forfeited || (ulong)f.Holder == t || f.Realm < 0 || pc.RulerOf(f.Realm) != t) continue;
                if (seen.Add((ulong)f.Holder)) Opinions.Add(pb, rb, (ulong)f.Holder, t, "excommunicated", day, entry.Value);
            }
            foreach (var o in pb.Oaths) if (o.Liege == t && seen.Add(o.Vassal)) Opinions.Add(pb, rb, o.Vassal, t, "excommunicated", day, entry.Value);
            // a casus belli for each ruler of the faith against the banned man's realm
            int against = -1;
            foreach (int r in pc.AliveRealms()) if (pc.RulerOf(r) == t) { against = r; break; }
            if (against < 0) foreach (var f in rb.Fiefs.Values) if (f.Holder == (long)t && !f.Forfeited && f.Realm >= 0) { against = f.Realm; break; }
            if (against >= 0)
                foreach (int r in pc.AliveRealms())
                {
                    ulong ruler = pc.RulerOf(r); if (r == against || ruler == 0 || ruler == t || ReligionOfAny(p, ruler) != religion) continue;
                    Diplomacy.AddCb(pc, r, against, CbKind.Excommunication, entry);
                }
        }
        foreach (var f in rb.Fiefs.Values)
        {
            if (f.Holder != (long)t || f.Forfeited) continue;
            foreach (int h in pc.R.HouseholdsOfFief(f.Fief)) RuleDues.AddGrievance(pc.R, h, s.ExcommunicationGrievances, s.ExcommunicationGrievanceSeasons, day, entry);
        }
    }

    // ============================================================ penance

    /// <summary>The coin a penance takes: a tenth of the treasuries of the fiefs he holds, or of his household's coin when he holds none.</summary>
    public static List<(PurseId Purse, long Mites)> PenanceDue(WorldState st, FaithOps.Pass p, EntityId person)
    {
        var list = new List<(PurseId, long)>(); var led = st.Economy!.Ledger; var region = p.Region;
        var rb = st.Rule;
        if (rb != null)
            foreach (var f in rb.Fiefs.Values)
                if (f.Holder == (long)person.Value && !f.Forfeited && f.Fief < region.Fiefs.Count && led.TryFind(PurseKind.Treasury, region.Fiefs[f.Fief].Id, out var tp) && led.Balance(tp).Mites * p.S.PenancePct / 100 is var due and > 0)
                    list.Add((tp, due));
        if (list.Count == 0) { var hp = FaithOps.PurseOf(st, person); if (!hp.IsNone && led.Balance(hp).Mites * p.S.PenancePct / 100 is var d and > 0) list.Add((hp, d)); }
        return list;
    }

    /// <summary>
    /// Penance (4.6, FC-9): the sinner pays a tenth of his treasury (or household coin) to his see and has made a pilgrimage since the ban; the mark is cleared, standing is restored by the church's word
    /// (a proclaimed deed), services return. <paramref name="pilgrimageMade"/> is the caller's knowledge (the hero's count of pilgrimages since, or an NPC lord's walk). Returns "" or the refusal key.
    /// </summary>
    public static string Penance(WorldState st, ContentDatabase db, EntityId person, bool pilgrimageMade, int day)
    {
        var p = FaithOps.Of(st, db); if (p == null) return "fa_no_church";
        var x = p.Pol.ActiveFor(person.Value); if (x == null) return "fa_not_excommunicated";
        if (!pilgrimageMade) return "fa_penance_no_pilgrimage";
        int ri = ReligionOfAny(p, person.Value);
        var see = SeeFor(p, person.Value, ri, null); if (see == null) return "fa_excomm_no_bishop";
        var led = st.Economy!.Ledger; var seePurse = led.Open(PurseKind.Treasury, FaithEngine.SeePurseOwner(see.Index));
        long paid = 0;
        foreach (var (purse, mites) in PenanceDue(st, p, person)) if (led.Transfer(purse, seePurse, new Coin(mites), TransferReason.Penance)) paid += mites;
        Lift(st, db, p, x, day, "ch_faith_penance_done", "ch_note_penance", paid);
        return "";
    }

    private static void Lift(WorldState st, ContentDatabase db, FaithOps.Pass p, ExcommRow x, int day, string textKey, string noteKey, long paid)
    {
        var target = new EntityId(x.Person);
        x.LiftedDay = day;
        (st.Standing ??= new StandingBook()).Marks.Absolve(target, p.Religions.All.FirstOrDefault(r => r.Id == x.Religion)?.Index ?? -1);
        int row = FaithOps.RowOf(st, target); int settlement = row >= 0 ? p.People.HomeSettlement[row] : -1;
        var ps = new List<ChronParam> { ChronParam.Person("doer", target) };
        if (paid > 0) ps.Add(ChronParam.Mites("amount", paid));
        if (FaithOps.PlaceOf(st, settlement) is var pl && pl != EntityId.None) ps.Add(ChronParam.Place("place", pl));
        string type = textKey == "ch_faith_grace" ? "grace_granted" : "church_penance_done";
        var e = FaithOps.Chron(st, FaithOps.SecOf(day), type, textKey, ps, [target], new EntityId(x.Entry), noteKey);
        FaithStanding.Record(st, db, target, type == "grace_granted" ? "grace_granted" : "church_penance_done", e, settlement, 1, 0, true, day);
    }

    // ============================================================ the day and the season

    /// <summary>
    /// The bishops' own bans (4.6): a person whose standing with the faith is -800 or less and who has a deed on the books within the last season ("his sins continue") is banned by his bishop. Run each season.
    /// </summary>
    public static int BishopsBan(WorldState st, ContentDatabase db, int day)
    {
        var p = FaithOps.Of(st, db); if (p == null || st.Standing == null) return 0;
        var pc = Politics(st, db, day); int n = 0;
        var due = new List<(ulong Person, int Religion)>();
        foreach (var ((person, packed), row) in st.Standing.Rows)
        {
            var g = GroupRef.Unpack(packed);
            if (g.Kind != GroupKind.Religion || !Standing.ExcommunicationDue(row.ValuePm) || p.Pol.IsExcommunicated(person) || ReligionOfAny(p, person) != g.Index) continue;
            bool sinning = false; foreach (ulong d in st.Standing.DeedsOf(new EntityId(person))) if (st.Standing.Deeds[d].Day >= day - TimeConst.DaysPerSeason) { sinning = true; break; }
            if (sinning) due.Add((person, g.Index));
        }
        foreach (var (person, ri) in due)
        {
            var see = SeeFor(p, person, ri, pc); if (see == null || see.Bishop < 0) continue;
            Impose(st, db, p, pc, new EntityId(person), ri, new EntityId((ulong)see.Bishop), EntityId.None, EntityId.None, day); n++;
        }
        return n;
    }

    /// <summary>The season's upkeep of the bans: the grievances renew while the ban stands; a new bishop (the one who laid it dead and another chosen) may grant grace (GracePct, stream 0x652).</summary>
    public static void Season(WorldState st, ContentDatabase db, int day)
    {
        var p = FaithOps.Of(st, db); if (p == null || p.Pol.Excomms.Count == 0) return;
        var pc = Politics(st, db, day);
        foreach (var x in p.Pol.Excomms.ToList())
        {
            if (!x.Active) continue;
            int ri = p.Religions.All.FirstOrDefault(r => r.Id == x.Religion)?.Index ?? -1; if (ri < 0) continue;
            var see = SeeFor(p, x.Person, ri, pc);
            if (see != null && see.Bishop >= 0 && (ulong)see.Bishop != x.By && !FaithEngine.IsLivingPriest(p.People, (long)x.By, out _)
                && DetRng.Range(st.Seed, day, x.Person ^ x.Entry, RngStreams.HeresyRoll, 800, 0, 100) < p.S.GracePct)
            { Lift(st, db, p, x, day, "ch_faith_grace", "ch_note_new_bishop_grace", 0); continue; }
            if (pc != null) Consequences(st, p, pc, new EntityId(x.Person), ri, new EntityId(x.Entry), day, false);
        }
    }
}
