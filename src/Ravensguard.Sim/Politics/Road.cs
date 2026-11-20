using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Rule;

namespace Ravensguard.Sim.Politics;

/// <summary>One term of a verdict, in words for the Faction screen: a text key, the value it stands at, what it needs and whether it is met ("he thinks little of you (-20)").</summary>
public sealed record Term(string Key, int Value, int Need, bool Met);

/// <summary>Whether a lord or a ruler says yes, with the terms behind it (diplomacy 6: "He will say no: he thinks little of you (-20) and ...").</summary>
public sealed record Verdict(bool Ok, IReadOnlyList<Term> Terms)
{
    public static readonly Verdict No = new(false, []);
    public string FirstUnmet => Terms.FirstOrDefault(t => !t.Met)?.Key ?? "";
}

/// <summary>
/// The hero's road (diplomacy-and-ai-lords 4.3) by the same rules as everyone's: service earns fealty, fealty and the lord's good opinion earn a manor, war service and a ruler's good opinion earn a vacant fief, and a lord may
/// renounce his oath and found a faction. A grant moves only a vacant or the granter's own holding (the Phase 1 bug of "swearing fealty hands you a settlement taken from its lord" is fixed by construction: nothing here
/// takes a holding from a living holder). Chronicle entries carry their causes.
/// </summary>
public static class Road
{
    private static Cause Note(string key) => new(CauseKind.Diplomacy, null, NoteKey: key);

    // ------------------------------------------------------------------ fealty

    /// <summary>Fealty is offered when standing with the fief is at least 300, renown 100, three contracts done for it and the lord's opinion 30 (DA-6); the hero's asking is the same test.</summary>
    public static Verdict FealtyVerdict(PoliticsCtx c, EntityId hero, int fief)
    {
        var f = c.RB.FiefOf(fief); if (f == null || f.Holder < 0 || f.Forfeited || !c.R.Alive(f.Holder)) return Verdict.No;
        var s = c.S; int standing = c.Links.StandingWithFief(hero, fief), renown = c.Links.RenownPm(hero), contracts = c.Links.ContractsDone(hero, fief), op = Opinions.Value(c.RB, (ulong)f.Holder, hero.Value);
        var t = new List<Term>
        {
            new("dp_term_standing", standing, s.FealtyStanding, standing >= s.FealtyStanding),
            new("dp_term_renown", renown, s.FealtyRenown, renown >= s.FealtyRenown),
            new("dp_term_contracts", contracts, s.FealtyContracts, contracts >= s.FealtyContracts),
            new("dp_term_opinion", op, s.FealtyOpinion, op >= s.FealtyOpinion),
        };
        return new Verdict(t.All(x => x.Met), t);
    }

    /// <summary>The oath at the manor: the hero becomes the lord's man (a chronicle entry <c>fealty_sworn</c>); the lord thinks the better of him.</summary>
    public static (bool Ok, string Key) Swear(PoliticsCtx c, EntityId hero, int fief)
    {
        var f = c.RB.FiefOf(fief); if (f == null || f.Holder < 0) return (false, "dp_no_lord");
        if (c.B.OathOf(hero.Value) != null) return (false, "dp_already_sworn");
        var v = FealtyVerdict(c, hero, fief); if (!v.Ok) return (false, v.FirstUnmet);
        var place = c.R.SeatPlace(fief);
        var e = c.Chron.Chron(RuleCtx.At(c.Day), "fealty_sworn", "ch_dp_fealty_sworn", [ChronParam.Person("vassal", hero), ChronParam.Person("lord", new EntityId((ulong)f.Holder)), ChronParam.Place("place", place)],
            [hero, new EntityId((ulong)f.Holder), place], [Note("ch_note_dp_service_done")]);
        c.B.Oaths.Add(new Fealty { Vassal = hero.Value, Liege = (ulong)f.Holder, Day = c.Day, Fief = fief });
        Opinions.Add(c.B, c.RB, (ulong)f.Holder, hero.Value, "fealty", c.Day, e.Value);
        return (true, "dp_sworn");
    }

    // ------------------------------------------------------------------ the manor

    /// <summary>The settlements of a fief that could be a manor: not the seat, and not held by a living man as a manor already (the lord's own demesne villages), lowest first.</summary>
    public static List<int> VacantManors(PoliticsCtx c, int fief)
    {
        var l = new List<int>(); var fi = c.R.Region.Fiefs; if ((uint)fief >= (uint)fi.Count) return l;
        foreach (int s in fi[fief].Settlements)
        {
            if (s == fi[fief].SeatSettlement) continue;
            var m = c.B.ManorAt(s);
            if (m != null && m.Holder >= 0 && c.R.Alive(m.Holder)) continue;
            l.Add(s);
        }
        l.Sort(); return l;
    }

    public static Verdict ManorVerdict(PoliticsCtx c, EntityId hero, int fief)
    {
        var f = c.RB.FiefOf(fief); if (f == null || f.Holder < 0 || !c.R.Alive(f.Holder)) return Verdict.No;
        var oath = c.B.OathOf(hero.Value); int op = Opinions.Value(c.RB, (ulong)f.Holder, hero.Value); int free = VacantManors(c, fief).Count;
        var t = new List<Term>
        {
            new("dp_term_vassal", oath != null && oath.Liege == (ulong)f.Holder ? 1 : 0, 1, oath != null && oath.Liege == (ulong)f.Holder),
            new("dp_term_opinion", op, c.S.ManorOpinion, op >= c.S.ManorOpinion),
            new("dp_term_vacant_manor", free, 1, free > 0),
        };
        return new Verdict(t.All(x => x.Met), t);
    }

    /// <summary>The lord grants a vassal a manor out of his own demesne (DA-7): a sub-fief deed of that fief; the village's holder was the lord himself, so no one living is robbed.</summary>
    public static (bool Ok, string Key, int Settlement) GrantManor(PoliticsCtx c, EntityId hero, int fief)
    {
        var v = ManorVerdict(c, hero, fief); if (!v.Ok) return (false, v.FirstUnmet, -1);
        var f = c.RB.FiefOf(fief)!; int s = VacantManors(c, fief)[0];
        var m = c.B.ManorAt(s);
        if (m == null) { m = new Manor { Serial = c.B.NextManor++, Settlement = s, OverlordFief = fief, Realm = f.Realm }; c.B.Manors.Add(m); }
        m.Holder = (long)hero.Value; m.GrantedDay = c.Day; m.OverlordFief = fief; m.Realm = f.Realm;
        var place = c.R.Region.Settlements[s].Id;
        var e = c.Chron.Chron(RuleCtx.At(c.Day), "manor_granted", "ch_dp_manor_granted", [ChronParam.Person("vassal", hero), ChronParam.Person("lord", new EntityId((ulong)f.Holder)), ChronParam.Place("place", place)],
            [hero, new EntityId((ulong)f.Holder), place], [Note("ch_note_dp_good_service")]);
        Opinions.Add(c.B, c.RB, hero.Value, (ulong)f.Holder, "granted", c.Day, e.Value);
        return (true, "dp_manor_granted", s);
    }

    /// <summary>The season's rent of the manors: the holder takes a share of his village's hearth tax out of the fief's treasury (a real transfer; nothing is made).</summary>
    public static long ManorRents(PoliticsCtx c)
    {
        long total = 0; var tax = c.R.Tax; if (tax == null) return 0;
        foreach (var m in c.B.Manors)
        {
            if (m.Holder < 0 || !(c.R.Alive(m.Holder) || c.Links.IsHero((ulong)m.Holder))) continue;
            long due = (long)c.R.HouseholdsInSettlement(m.Settlement) * tax.HearthMites(tax.LevelOfFief(m.OverlordFief)) * c.S.ManorRentPct / 100;
            total += c.R.PayUpTo(Party.OfFief(m.OverlordFief), c.R.HouseholdOf(m.Holder), due, TransferReason.Rent);
        }
        return total;
    }

    // ------------------------------------------------------------------ a fief from the ruler

    /// <summary>What a ruler's service count for a person is: 25 for each war service on his record, 5 for each contract done in the realm's fiefs.</summary>
    public static int ServiceDone(PoliticsCtx c, EntityId person, int realm)
    {
        int n = 0; ulong rk = c.KeyOf(realm);
        foreach (var m in c.B.Mods) if (m.A == rk && m.B == person.Value && m.Kind == "helped_in_war") n += 25;
        foreach (var f in c.RB.Fiefs.Values) if (f.Realm == realm) n += 5 * c.Links.ContractsDone(person, f.Fief);
        return n;
    }

    /// <summary>A candidate's score for a fief (4.2): the ruler's opinion + standing / 10 + the service done.</summary>
    public static int CandidateScore(PoliticsCtx c, EntityId person, int realm) =>
        Opinions.Value(c.RB, c.KeyOf(realm), person.Value) + c.R.StandingWithRealm(person, realm) / c.S.GrantStandingDiv + ServiceDone(c, person, realm);

    public static bool Vacant(FiefRow f) => f.Holder < 0 || f.Forfeited;

    /// <summary>The hero asks his ruler for a vacant fief: the fief must be vacant, the ruler's opinion 40 and some service done.</summary>
    public static Verdict FiefVerdict(PoliticsCtx c, EntityId hero, int fief)
    {
        var f = c.RB.FiefOf(fief); if (f == null || f.Realm < 0) return Verdict.No;
        int op = Opinions.Value(c.RB, c.KeyOf(f.Realm), hero.Value), service = ServiceDone(c, hero, f.Realm);
        var t = new List<Term>
        {
            new("dp_term_vacant", Vacant(f) ? 1 : 0, 1, Vacant(f)),
            new("dp_term_opinion", op, c.S.FiefOpinion, op >= c.S.FiefOpinion),
            new("dp_term_service", service, 1, service >= 1),
        };
        return new Verdict(t.All(x => x.Met), t);
    }

    /// <summary>
    /// The ruler grants <paramref name="fief"/> to <paramref name="grantee"/> (DA-8): only a vacant fief of the ruler's realm moves, the deed is the grantee's from today, the relief is asked of him (paid at once when he can,
    /// else a due that falls under the rule book) and the grant's chronicle entry names the service. Returns false when the fief was not vacant.
    /// </summary>
    public static bool GrantFief(PoliticsCtx c, int fief, EntityId grantee, string noteKey)
    {
        var f = c.RB.FiefOf(fief); if (f == null || !Vacant(f)) return false;
        f.Holder = (long)grantee.Value; f.Forfeited = false; f.TookDay = c.Day; f.WardUntilDay = -1; f.Guardian = -1;
        var place = c.R.SeatPlace(fief);
        var e = c.Chron.Chron(RuleCtx.At(c.Day), "fief_granted", "ch_dp_fief_granted", [ChronParam.Person("grantee", grantee), ChronParam.Place("place", place), Diplomacy.RulerParam(c, "ruler", f.Realm)],
            [grantee, place], [Note(noteKey)]);
        var overlord = c.R.OverlordOf(f); long relief = RuleMath.Relief(f.LastYearReceipts, c.RB.Settings.ReliefPct);
        if (!overlord.IsNone && relief > 0)
        {
            var due = RuleDues.AddDue(c.R, DueKind.Relief, c.R.HouseholdOf((long)grantee.Value), overlord, relief, fief, c.Day + c.RB.Settings.ReliefDueSeasons * TimeConst.DaysPerSeason, e);
            long paid = c.R.PayUpTo(due.Payer, due.Payee, relief, TransferReason.Relief);
            due.PaidMites += paid; if (due.Owing == 0) due.State = DueState.Paid;
        }
        Opinions.Add(c.B, c.RB, grantee.Value, c.KeyOf(f.Realm), "granted", c.Day, e.Value);
        c.B.Petitions.RemoveAll(p => p.Person == grantee.Value);
        return true;
    }

    /// <summary>The hero (or anyone) asks the ruler of the fief's realm for it; the verdict decides.</summary>
    public static (bool Ok, string Key) AskFief(PoliticsCtx c, EntityId hero, int fief)
    {
        var v = FiefVerdict(c, hero, fief); if (!v.Ok) return (false, v.FirstUnmet);
        return GrantFief(c, fief, hero, "ch_note_dp_war_service") ? (true, "dp_fief_granted") : (false, "dp_term_vacant");
    }

    /// <summary>A war service done for a realm (a garrison, a raid escort, a war aim's contract): the ruler's opinion rises by the kind's amount (4.4: helped in war +25).</summary>
    public static int RecordService(PoliticsCtx c, EntityId person, int realm, ulong cause = 0) => Opinions.Add(c.B, c.RB, c.KeyOf(realm), person.Value, "helped_in_war", c.Day, cause);

    // ------------------------------------------------------------------ a gift

    /// <summary>A gift of coin from a person to a lord or a realm: a ledger transfer between their purses, the opinion following by its kind (+1 for ten crowns, at most +20, fading 5 a season: DA-10).</summary>
    public static (bool Ok, string Key, int Points) Gift(PoliticsCtx c, EntityId giver, Party to, ulong toKey, long mites)
    {
        if (mites <= 0) return (false, "dp_gift_nothing", 0);
        var from = c.R.HouseholdOf((long)giver.Value);
        if (!c.R.Pay(from, to, mites, TransferReason.Gift)) return (false, "dp_gift_cannot_afford", 0);
        var e = c.Chron.Chron(RuleCtx.At(c.Day), "lord_gift", "ch_dp_lord_gift", [ChronParam.Person("giver", giver), ChronParam.Person("receiver", new EntityId(toKey)), ChronParam.Mites("amount", mites)], [giver, new EntityId(toKey)],
            [Note("ch_note_dp_goodwill")]);
        int pts = Opinions.Add(c.B, c.RB, toKey, giver.Value, "gift", c.Day, e.Value, Opinions.GiftPoints(c.S, mites));
        return (true, "dp_gift_given", pts);
    }

    // ------------------------------------------------------------------ renouncing the oath and founding a faction

    /// <summary>
    /// A lord renounces his oath (4.3, DA-9): <c>oath_renounced</c>, his liege's opinion -60 and a casus belli for the liege; he becomes the ruler of a new realm (a faction: his name, a banner, his fiefs); the people sworn to him choose
    /// by their opinion of him against the old liege, and the manors under his fiefs go with him. Returns the new realm's index, -1 when he holds no fief of a realm.
    /// </summary>
    public static int Renounce(PoliticsCtx c, ulong person, string noteKey = "ch_note_dp_oath_broken")
    {
        var held = c.RB.Fiefs.Values.Where(x => x.Holder == (long)person && !x.Forfeited).OrderBy(x => x.Fief).ToList();
        if (held.Count == 0 || held[0].Realm < 0) return -1;
        int liege = held[0].Realm;
        int idx = c.HistoryRealms; foreach (var k in c.B.Realms.Keys) if (k >= idx) idx = k + 1;
        var pid = new EntityId(person);
        var row = c.B.RealmOf(idx); row.Founded = true; row.Ruler = person; row.Name = c.R.NameOf((long)person); row.Banner = (int)(person % 12); row.Parent = liege; row.FoundedDay = c.Day;
        if (c.R.PurseOf(Party.OfRealm(idx)).IsNone) c.R.Led.Open(PurseKind.Treasury, c.R.RealmKey(idx));
        var place = c.R.SeatPlace(held[0].Fief);
        var oath = c.Chron.Chron(RuleCtx.At(c.Day), "oath_renounced", "ch_dp_oath_renounced", [ChronParam.Person("lord", pid), Diplomacy.RulerParam(c, "liege", liege), ChronParam.Place("place", place)],
            [pid, place], [Note(noteKey)]);
        foreach (var f in held) { f.Realm = idx; f.OverlordFief = -1; }
        foreach (var m in c.B.Manors) if (held.Any(h => h.Fief == m.OverlordFief)) m.Realm = idx;
        // the men sworn to him choose: their opinion of him against that of the old liege
        int followed = 0;
        foreach (var o in c.B.Oaths.ToList())
        {
            if (o.Liege != person) continue;
            int opMe = Opinions.Value(c.RB, o.Vassal, person), opOld = Opinions.Value(c.RB, o.Vassal, c.KeyOf(liege));
            if (opMe >= opOld) { followed++; foreach (var f in c.RB.Fiefs.Values) if (f.Holder == (long)o.Vassal && !f.Forfeited) { f.Realm = idx; f.OverlordFief = -1; } }
            else c.B.Oaths.Remove(o);
        }
        c.RB.Rulers[idx] = (long)person;
        Opinions.Add(c.B, c.RB, c.KeyOf(liege), person, "broken_oath", c.Day, oath.Value, c.S.RenounceLiegeOpinion);
        Opinions.Add(c.B, c.RB, c.KeyOf(liege), c.KeyOf(idx), "broken_oath", c.Day, oath.Value, c.S.RenounceLiegeOpinion);
        Diplomacy.AddCb(c, liege, idx, CbKind.BrokenTreaty, oath);
        c.Chron.Chron(RuleCtx.At(c.Day), "realm_founded", "ch_dp_realm_founded", [ChronParam.Person("founder", pid), ChronParam.Count("fiefs", c.RB.Fiefs.Values.Count(x => x.Realm == idx)), ChronParam.Count("followers", followed)],
            [pid, c.R.RealmKey(idx)], [new Cause(CauseKind.Diplomacy, oath)]);
        return idx;
    }
}
