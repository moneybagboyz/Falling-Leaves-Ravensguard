using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Taxes;

namespace Ravensguard.Sim.Politics;

/// <summary>
/// The lord's planning brain (diplomacy-and-ai-lords 4.2): at a season's first day (one fief an hour) each lord scores what he could do by considerations of content, takes the best one or two above the floor and writes each as a
/// chronicle entry whose cause is the consideration that drove it ("raised the tallage: the treasury held less than one season"). What a lord knows of other realms is last-known, with its age. The hero's holdings are his own.
/// Integer only; ties between equal utilities are drawn with stream 0x671 (key lord and season).
/// </summary>
public static class LordBrain
{
    /// <summary>The hour of a season's first day a fief's lord thinks (one fief an hour, after the realms' pass).</summary>
    public static int HourOf(int fief) => (3 + fief) % 24;

    public sealed record Choice(LordActionDef Def, int Utility, string Note);

    // ------------------------------------------------------------------ inputs

    /// <summary>The named inputs of a lord's considerations, evaluated lazily for one lord (every one an integer).</summary>
    public sealed class Inputs
    {
        private readonly PoliticsCtx _c; private readonly FiefRow _f; private readonly LordTrait _t;
        private readonly Dictionary<string, int> _cache = [];
        public Inputs(PoliticsCtx c, FiefRow f) { _c = c; _f = f; _t = c.TraitsOfPerson((ulong)f.Holder); }

        public int Get(string name)
        {
            if (_cache.TryGetValue(name, out int v)) return v;
            return _cache[name] = Compute(name);
        }

        private int Compute(string name)
        {
            var c = _c; var f = _f; var r = c.R; var tax = r.Tax; ulong lord = (ulong)f.Holder;
            switch (name)
            {
                case "treasury_tenths":
                {
                    long pay = tax?.PayrollSeason(f.Fief) ?? 0; long bal = r.Balance(Party.OfFief(f.Fief));
                    return pay <= 0 ? 99 : (int)Math.Min(99, bal * 10 / pay);
                }
                case "unrest_pm": { int hh = r.HouseholdsOfFief(f.Fief).Count(); if (hh == 0) return 0; int n = 0; foreach (int h in r.HouseholdsOfFief(f.Fief)) if (c.RB.GrievancePoints(r.Households.IdOf(h).Value, c.Day) > 0) n++; return n * 1000 / hh; }
                case "arrears_pm": { int hh = 0, n = 0; if (tax == null) return 0; foreach (int h in r.HouseholdsOfFief(f.Fief)) { hh++; if (tax.State.Row(h) is { Total: > 0 }) n++; } return hh == 0 ? 0 : n * 1000 / hh; }
                case "room_up": return tax != null && tax.LevelOfFief(f.Fief) < TaxLevel.High ? 1 : 0;
                case "room_down": return tax != null && tax.LevelOfFief(f.Fief) > TaxLevel.Low ? 1 : 0;
                case "opinion_ruler": return f.Realm < 0 ? 0 : Opinions.Value(c.RB, lord, c.KeyOf(f.Realm));
                case "wealth_units": { long bal = r.Balance(r.HouseholdOf(f.Holder)); return (int)Math.Min(99, bal / 1000); }          // tens of crowns
                case "thief": { foreach (var o in c.RB.OfficesOf(f.Fief)) if (o.Expected > 0 && RuleOfficials.ShortfallPct(o) >= RuleLedger.ShortfallAlertPct) return 1; return 0; }
                case "vacancy_realm": { foreach (var x in c.RB.Fiefs.Values) if (x.Realm == f.Realm && x.Fief != f.Fief && (x.Holder < 0 || x.Forfeited)) return 1; return 0; }
                case "petitioned": { foreach (var p in c.B.Petitions) if (p.Person == lord) return 1; return 0; }
                case "owed": { foreach (var d in c.RB.Dues) if (d.State is DueState.Arrears or DueState.Grievance && d.Owing > 0 && (d.Payee == Party.OfFief(f.Fief) || d.Payee == r.HouseholdOf(f.Holder)) && r.Balance(d.Payer) > 0) return 1; return 0; }
                case "at_war": return f.Realm >= 0 && c.B.WarOf(f.Realm) != null ? 1 : 0;
                case "renege": return f.Realm < 0 ? 0 : -Opinions.Value(c.RB, lord, c.KeyOf(f.Realm));
                case "own_strength_pm": return f.Realm < 0 ? 1000 : (int)(c.Strength(f.Realm) * 1000 / Math.Max(1, c.Strength(RootOpponent(f.Realm))));
                case "unrest_rung": { var ub = c.R.W.Get<Ravensguard.Sim.Unrest.UnrestBook>(); if (ub == null) return 0; int m = 0; foreach (var st in c.R.Region.Settlements) if (st.FiefIndex == f.Fief) m = Math.Max(m, ub.RungOf(c.R.Region.Settlements.IndexOf(st))); return m; }
                case "riot_leader": { var ub = c.R.W.Get<Ravensguard.Sim.Unrest.UnrestBook>(); if (ub == null) return 0; foreach (var rt in ub.Riots) if (rt.Fief == f.Fief && rt.State == Ravensguard.Sim.Unrest.RiotState.Ended && c.Day - rt.StartDay <= 30 && !ub.Pardoned.Contains(rt.Leader)) return 1; return 0; }
                case "trait_kind": return (_t & (LordTrait.Just | LordTrait.Generous)) != 0 ? 1 : 0;
                case "trait_hard": return (_t & (LordTrait.Greedy | LordTrait.Cruel)) != 0 ? 1 : 0;
                case "trait_greedy": return Has(LordTrait.Greedy);
                case "trait_generous": return Has(LordTrait.Generous);
                case "trait_just": return Has(LordTrait.Just);
                case "trait_cruel": return Has(LordTrait.Cruel);
                case "trait_pious": return Has(LordTrait.Pious);
                case "trait_martial": return Has(LordTrait.Martial);
                case "trait_cautious": return Has(LordTrait.Cautious);
                default: return 0;
            }
        }

        private int Has(LordTrait x) => (_t & x) != 0 ? 1 : 0;
        private int RootOpponent(int realm) { var w = _c.B.WarOf(realm); return w == null ? realm : w.Attacker == realm ? w.Defender : w.Attacker; }
    }

    // ------------------------------------------------------------------ what a lord could do

    /// <summary>The scored options of one lord above their floor, best first (ties by stream 0x671, then by id).</summary>
    public static List<Choice> Options(PoliticsCtx c, FiefRow f)
    {
        var inp = new Inputs(c, f); var list = new List<(Choice, ulong)>(); int floor = c.S.ActMin;
        foreach (var def in c.B.Actions)
        {
            if (def.Actor != "lord") continue;
            var (u, note) = Considerations.Evaluate(def, inp.Get);
            if (u < (def.Min > 0 ? def.Min : floor)) continue;
            ulong tie = DetRng.Next(c.Seed, c.Day, (ulong)f.Holder ^ (ulong)StableHash(def.Id) , RngStreams.DiplomacyTies, 0);
            list.Add((new Choice(def, u, note), tie));
        }
        list.Sort((x, y) => { int cmp = y.Item1.Utility.CompareTo(x.Item1.Utility); if (cmp != 0) return cmp; cmp = x.Item2.CompareTo(y.Item2); return cmp != 0 ? cmp : string.CompareOrdinal(x.Item1.Def.Id, y.Item1.Def.Id); });
        return list.ConvertAll(x => x.Item1);
    }

    public static int StableHash(string s) { int h = 17; foreach (char ch in s) h = h * 31 + ch; return h & 0x7FFFFFFF; }

    // ------------------------------------------------------------------ a fief's turn

    /// <summary>One lord's season: he takes the best option or two (each that does something), and every action is written with its cause. Returns the actions taken.</summary>
    public static int Think(PoliticsCtx c, FiefRow f)
    {
        if (f.Holder < 0 || f.Forfeited || !c.R.Alive(f.Holder) || c.Links.IsHero((ulong)f.Holder)) return 0;
        int done = 0;
        foreach (var o in Options(c, f))
        {
            if (done >= c.S.ActionsPerSeason) break;
            if (Act(c, f, o)) done++;
        }
        return done;
    }

    /// <summary>The lords of every fief whose hour is <paramref name="hour"/> of a season's first day (one fief an hour, in fief id order).</summary>
    public static int Slice(PoliticsCtx c, int hour)
    {
        int n = 0;
        foreach (var f in c.RB.Fiefs.Values) if (HourOf(f.Fief) == hour) n += Think(c, f);
        return n;
    }

    private static Cause CauseOf(string note) => new(CauseKind.Diplomacy, null, NoteKey: note.Length > 0 ? note : "ch_note_dp_own_judgement");

    private static EntityId Entry(PoliticsCtx c, string type, string key, IReadOnlyList<ChronParam> ps, IReadOnlyList<EntityId> subj, string note) =>
        c.Chron.Chron(RuleCtx.At(c.Day), type, key, ps, subj, [CauseOf(note)]);

    /// <summary>Does what the option says; false when it turns out to change nothing (the next option is tried).</summary>
    public static bool Act(PoliticsCtx c, FiefRow f, Choice o)
    {
        var r = c.R; var holder = new EntityId((ulong)f.Holder); var place = r.SeatPlace(f.Fief); string id = o.Def.Id;
        var psAction = new List<ChronParam> { ChronParam.Person("lord", holder), ChronParam.Place("place", place), ChronParam.TextKey("action", o.Def.TextKey) };
        switch (id)
        {
            case "set_tax_up": case "set_tax_down":
            {
                var tax = r.Tax; if (tax == null) return false;
                var lv = tax.LevelOfFief(f.Fief);
                var next = id == "set_tax_up" ? (lv < TaxLevel.High ? lv + 1 : lv) : (lv > TaxLevel.Low ? lv - 1 : lv);
                if (next == lv) return false;
                tax.SetLevel(f.Fief, next);
                var e = Entry(c, "lord_action", "ch_dp_lord_action", psAction, [holder, place], o.Note);
                c.B.Note(c.Day, f.Fief, (ulong)f.Holder, o.Def.TextKey, o.Note, e.Value);
                return true;
            }
            case "gift":
            {
                if (f.Realm < 0) return false;
                var from = r.HouseholdOf(f.Holder); var to = Party.OfRealm(f.Realm);
                long bal = r.Balance(from); long give = Math.Min(c.S.GiftMaxMitesPerSeason, bal / 10); give = give / 100 * 100;
                if (give < 1000 || r.PurseOf(to).IsNone || !r.Pay(from, to, give, TransferReason.Gift)) return false;
                var ps = new List<ChronParam> { ChronParam.Person("giver", holder), RulerParam(c, "receiver", f.Realm), ChronParam.Mites("amount", give) };
                var e = Entry(c, "lord_gift", "ch_dp_lord_gift", ps, [holder, r.RealmKey(f.Realm)], o.Note);
                Opinions.Add(c.B, c.RB, c.KeyOf(f.Realm), (ulong)f.Holder, "gift", c.Day, e.Value, Opinions.GiftPoints(c.S, give));
                c.B.Note(c.Day, f.Fief, (ulong)f.Holder, o.Def.TextKey, o.Note, e.Value);
                return true;
            }
            case "demand_dues":
            {
                long got = 0; DueRow? first = null;
                foreach (var d in c.RB.Dues)
                {
                    if (d.State is not (DueState.Arrears or DueState.Grievance) || d.Owing <= 0) continue;
                    if (d.Payee != Party.OfFief(f.Fief) && d.Payee != r.HouseholdOf(f.Holder)) continue;
                    long pay = r.PayUpTo(d.Payer, d.Payee, d.Owing, TransferReason.Fee); if (pay <= 0) continue;
                    d.PaidMites += pay; if (d.Owing == 0) d.State = DueState.Paid; got += pay; first ??= d;
                }
                if (got <= 0) return false;
                var ps = new List<ChronParam> { ChronParam.Person("lord", holder), ChronParam.Place("place", place), ChronParam.Mites("amount", got) };
                var e = Entry(c, "dues_demanded", "ch_dp_dues_demanded", ps, [holder, place], o.Note);
                c.B.Note(c.Day, f.Fief, (ulong)f.Holder, o.Def.TextKey, o.Note, e.Value);
                return true;
            }
            case "dismiss_official":
            {
                Office? worst = null; foreach (var off in c.RB.OfficesOf(f.Fief)) if (off.Expected > 0 && RuleOfficials.ShortfallPct(off) >= RuleLedger.ShortfallAlertPct && (worst == null || RuleOfficials.ShortfallPct(off) > RuleOfficials.ShortfallPct(worst))) worst = off;
                if (worst == null) return false;
                RuleOfficials.Dismiss(c.R, worst, c.Day, "ch_note_lord_dismisses");
                var e = Entry(c, "lord_action", "ch_dp_lord_action", psAction, [holder, place], o.Note);
                c.B.Note(c.Day, f.Fief, (ulong)f.Holder, o.Def.TextKey, o.Note, e.Value);
                return true;
            }
            case "petition":
            {
                foreach (var p in c.B.Petitions) if (p.Person == (ulong)f.Holder) return false;
                int target = -1; foreach (var x in c.RB.Fiefs.Values) if (x.Realm == f.Realm && x.Fief != f.Fief && (x.Holder < 0 || x.Forfeited)) { target = x.Fief; break; }
                if (target < 0) return false;
                c.B.Petitions.Add(new Petition { Person = (ulong)f.Holder, Fief = target, Day = c.Day });
                var ps = new List<ChronParam> { ChronParam.Person("lord", holder), ChronParam.Place("place", r.SeatPlace(target)), RulerParam(c, "ruler", f.Realm) };
                var e = Entry(c, "petition_made", "ch_dp_petition", ps, [holder, r.SeatPlace(target)], o.Note);
                c.B.Note(c.Day, f.Fief, (ulong)f.Holder, o.Def.TextKey, o.Note, e.Value);
                return true;
            }
            case "dole": case "pardon": case "punish":
            {
                var uc = Ravensguard.Sim.Unrest.UnrestWiring.CtxFor(c); if (uc == null) return false;
                bool ok = id == "dole" ? Ravensguard.Sim.Unrest.UnrestConcede.Dole(uc, f.Fief, Party.OfFief(f.Fief), o.Note.Length > 0 ? o.Note : "ch_note_dp_own_judgement") > 0
                    : id == "pardon" ? Ravensguard.Sim.Unrest.UnrestConcede.Pardon(uc, f.Fief, o.Note.Length > 0 ? o.Note : "ch_note_dp_own_judgement") > 0
                    : Ravensguard.Sim.Unrest.UnrestConcede.Punish(uc, f.Fief, o.Note.Length > 0 ? o.Note : "ch_note_dp_own_judgement");
                if (ok) c.B.Note(c.Day, f.Fief, (ulong)f.Holder, o.Def.TextKey, o.Note, 0);
                return ok;
            }
            case "renounce":
            {
                var made = Road.Renounce(c, (ulong)f.Holder, o.Note);
                if (made < 0) return false;
                c.B.Note(c.Day, f.Fief, (ulong)f.Holder, o.Def.TextKey, o.Note, 0);
                return true;
            }
        }
        return false;
    }

    private static ChronParam RulerParam(PoliticsCtx c, string name, int realm) => Diplomacy.RulerParam(c, name, realm);
}
