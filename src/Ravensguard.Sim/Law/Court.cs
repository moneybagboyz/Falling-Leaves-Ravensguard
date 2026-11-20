using System.Globalization;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Law;

/// <summary>
/// The court and the sentence (crime-and-justice 4.6, 4.7). On a court day (the last day of each five-day month) the cases of the held are heard in crime id order at the fief's seat: proof is a recognising
/// witness who comes (alive and within reach), being caught in the act, or a confession; without it the accused is acquitted. Guilty, the sentence is the lord's law, else the culture's, at the step of his
/// convictions of that crime, moved a step by mercy; it is carried out the next morning through the wound system or the ledger. When the hero holds the fief the cases are laid on his docket and what he leaves
/// undecided is judged by the steward at the next sitting. Every step is a chronicle entry on the one before, so "why is my hand gone" has an answer back to the crime.
/// </summary>
public static class Court
{
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    public static bool IsCourtDay(LawWorld w, int day) => w.S.CourtEveryDays > 0 && day % w.S.CourtEveryDays == w.S.CourtOffset;

    private static IEnumerable<ChronParam> PlaceOpt(LawWorld w, int settlement) => w.PlaceId(settlement) != EntityId.None ? [ChronParam.Place("place", w.PlaceId(settlement))] : [];

    // ------------------------------------------------------------------ the proof

    /// <summary>The witnesses of a crime who recognised the accused and can come: alive, and living within the setting's reach of the court's seat.</summary>
    public static List<ulong> Attending(LawWorld w, CrimeRecord rec, int seat)
    {
        var l = new List<ulong>();
        foreach (var wr in rec.Witnesses)
        {
            if (!wr.Recognised) continue;
            int row = w.People.IndexOf(new EntityId(wr.Person)); if (row < 0 || !w.People.IsAlive(row)) continue;
            int home = w.People.HomeSettlement[row];
            if (home >= 0 && w.DistanceM(home, seat) > w.S.WitnessReachM) continue;
            l.Add(wr.Person);
        }
        return l;
    }

    /// <summary>Whether the court has its proof (4.6): a recognising witness attends, he was taken in the act (the crime was cried and he was taken within a day of it), or he confessed.</summary>
    public static bool Proved(LawWorld w, JusticeBook jb, CrimeRecord rec, GaolRow row) =>
        jb.Confessed.Contains(rec.Id) || (row.InAct && rec.Cried && rec.Day >= row.SinceDay - 1) || Attending(w, rec, row.Settlement).Count > 0;

    // ------------------------------------------------------------------ the sitting

    /// <summary>
    /// A court day's sitting (hour 8): what the hero left on his docket at the last sitting is judged by the steward; then every held case is heard in crime id order, except those of a fief the hero holds, which
    /// are laid on his docket for him. Returns the cases decided here.
    /// </summary>
    public static int Sit(LawWorld w, CrimeBook cb, JusticeBook jb, int day, long sec)
    {
        int decided = 0;
        foreach (int id in jb.Docket.ToList())
        {
            var rec = cb.Get(id);
            jb.Docket.Remove(id);
            if (rec != null && rec.State == CrimeState.Held && Decide(w, cb, jb, rec, byHero: false, null, sec)) decided++;
        }
        jb.DocketDay = -1;
        var cases = jb.Gaol.Values.SelectMany(g => g.Crimes.Select(c => cb.Get(c))).Where(c => c is { State: CrimeState.Held }).Select(c => c!).OrderBy(c => c.Id).ToList();
        foreach (var rec in cases)
        {
            if (!jb.Gaol.TryGetValue((ulong)rec.Perpetrator, out var grow)) continue;
            int fief = grow.Fief;
            if (w.HeroLordOf(fief)) { jb.Docket.Add(rec.Id); jb.DocketDay = day; }
            else if (Decide(w, cb, jb, rec, byHero: false, null, sec)) decided++;
        }
        return decided;
    }

    /// <summary>One case by the NPC rule (or the steward's): proof or acquittal, the expected sentence, the pronouncement.</summary>
    private static bool Decide(LawWorld w, CrimeBook cb, JusticeBook jb, CrimeRecord rec, bool byHero, Expected? chosen, long sec)
    {
        var who = new EntityId((ulong)rec.Perpetrator);
        if (!jb.Gaol.TryGetValue(who.Value, out var row)) return false;
        if (!Proved(w, jb, rec, row)) return Acquit(w, cb, jb, rec, byHero, sec);
        return Pronounce(w, cb, jb, rec, chosen ?? Sentences.Expect(w, cb, jb, rec, who, row.Fief, npcLord: true), byHero, sec);
    }

    /// <summary>The hero as judge pronounces a case on his docket (the expected sentence or one he chose from <see cref="Sentences.Alternatives"/>); he cannot convict without proof, he may acquit the plainly guilty.</summary>
    public static bool Judge(LawWorld w, CrimeBook cb, JusticeBook jb, int crime, string key, long sec)
    {
        if (!jb.Docket.Contains(crime) || cb.Get(crime) is not { State: CrimeState.Held } rec) return false;
        var who = new EntityId((ulong)rec.Perpetrator);
        if (!jb.Gaol.TryGetValue(who.Value, out var row)) return false;
        jb.Docket.Remove(crime);
        if (!Proved(w, jb, rec, row)) return Acquit(w, cb, jb, rec, true, sec);
        Expected e = Sentences.Expect(w, cb, jb, rec, who, row.Fief, npcLord: false);
        if (key.Length > 0 && key != e.Key && w.Law.Punishment(key) is { } d)
        {
            int priors = cb.ConvictionsOf(who, rec.Kind);
            e = Sentences.Make(w, d, Sentences.StepFor(d, priors), rec, priors, 0, false, true, e.CultureKey);
        }
        return Pronounce(w, cb, jb, rec, e, true, sec);
    }

    /// <summary>The hero adjourns: what is left on his docket goes to the steward now.</summary>
    public static int Adjourn(LawWorld w, CrimeBook cb, JusticeBook jb, long sec)
    {
        int n = 0;
        foreach (int id in jb.Docket.ToList())
        {
            jb.Docket.Remove(id);
            if (cb.Get(id) is { State: CrimeState.Held } rec && Decide(w, cb, jb, rec, false, null, sec)) n++;
        }
        jb.DocketDay = -1;
        return n;
    }

    /// <summary>No proof: the accused is acquitted (<c>crime_acquitted</c>), the charge and its bounty go, and he is let out if nothing else holds him.</summary>
    public static bool Acquit(LawWorld w, CrimeBook cb, JusticeBook jb, CrimeRecord rec, bool byHero, long sec)
    {
        var who = new EntityId((ulong)rec.Perpetrator); var row = jb.Gaol[who.Value];
        rec.State = CrimeState.Acquitted; row.Crimes.Remove(rec.Id);
        var ps = new List<ChronParam> { ChronParam.Person("doer", who) }; ps.AddRange(PlaceOpt(w, row.Settlement));
        Gaol.Log(w, sec, "crime_acquitted", [who], row.ArrestEntry, "ch_justice_acquitted", ps, ("crime", N(rec.Id)));
        CancelBounty(w, rec, sec);
        if (byHero && rec.Witnesses.Any(x => x.Recognised)) w.Grievance?.Invoke(new EntityId(rec.Victim), -1);            // acquitting the plainly guilty angers the victim's kin (UR-4)
        Release(w, cb, jb, who, sec);
        return true;
    }

    private static void CancelBounty(LawWorld w, CrimeRecord rec, long sec)
    {
        if (rec.BountyJob != 0 && w.Jobs != null && w.JobEnv != null) w.Jobs.Cancel(w.JobEnv, new EntityId(rec.BountyJob), sec);
    }

    /// <summary>The sentence is pronounced (4.6): the record, the conviction counted, the bounty withdrawn (the culprit is taken), and <c>sentence_pronounced</c> on the arrest, to be carried out tomorrow.</summary>
    public static bool Pronounce(LawWorld w, CrimeBook cb, JusticeBook jb, CrimeRecord rec, Expected e, bool byHero, long sec)
    {
        var who = new EntityId((ulong)rec.Perpetrator); var row = jb.Gaol[who.Value];
        int day = (int)(sec / 86_400);
        var lord = row.Fief >= 0 ? w.LordOf(row.Fief) : EntityId.None;
        var ps = new List<ChronParam> { ChronParam.Person("doer", who), ChronParam.TextKey("punishment", e.TextKey) }; ps.AddRange(PlaceOpt(w, row.Settlement));
        var subj = new List<EntityId> { who }; if (!byHero && lord != EntityId.None) subj.Add(lord);
        var entry = Gaol.Log(w, sec, "sentence_pronounced", subj, row.ArrestEntry, "ch_justice_pronounced", ps, ("crime", N(rec.Id)), ("key", e.Key), ("step", N(e.Step)), ("kind", rec.Kind), ("priors", N(e.Priors)));
        var s = new Sentence
        {
            Crime = rec.Id, Person = who.Value, Key = e.Key, Step = e.Step, Kind = e.Kind, Mites = e.Mites, Days = e.Days, Lashes = e.Lashes, Display = e.Display, Feud = e.Feud, Unpaid = e.Unpaid,
            Day = day, DueDay = day + 1, Fief = row.Fief, Settlement = row.Settlement, Presider = byHero ? 0 : lord.Value, ByHero = byHero, Clergy = e.Clergy, LordsLaw = e.LordsLaw, Mercy = (sbyte)e.Mercy, Entry = entry.Id.Value,
        };
        jb.Sentences[rec.Id] = s;
        cb.Convictions[(who.Value, rec.Kind)] = cb.ConvictionsOf(who, rec.Kind) + 1;
        rec.State = CrimeState.Sentenced; row.Crimes.Remove(rec.Id);
        CancelBounty(w, rec, sec);
        if (w.Law.Punishment(e.CultureKey) is { } cul && e.Severity > cul.Severity) w.Grievance?.Invoke(who, 1);          // harsher than the culture's key: the kin's grievance (UR-4)
        return true;
    }

    // ------------------------------------------------------------------ the next morning

    /// <summary>
    /// The morning's carrying out (hour 6): every sentence due, in crime id order. A convict who is no longer held (broke out, was rescued, bribed his way) is spared by his own flight: the sentence is Avoided and the
    /// crime stands Fled. Money is paid from his purse; what he cannot pay becomes gaol (or outlawry where the table says so); the body punishments go through the wound system. After the last, who has nothing
    /// more to serve goes free (<c>sentence_served</c>).
    /// </summary>
    public static void CarryOut(LawWorld w, CrimeBook cb, JusticeBook jb, int day, long sec)
    {
        var touched = new SortedSet<ulong>();
        foreach (var s in jb.Sentences.Values.Where(x => x.State == SentenceState.Pending && (x.DueDay <= day)).OrderBy(x => x.Crime).ToList())
        {
            var rec = cb.Get(s.Crime); if (rec == null) { s.State = SentenceState.Pardoned; continue; }
            var who = new EntityId(s.Person);
            bool held = jb.Gaol.ContainsKey(s.Person) && (w.Captives == null || w.Captives.Find(who) is { Held: true });
            if (!held && !(s.Started && s.Kind is "weregild" or "blood_price" or "exile")) { s.State = SentenceState.Avoided; rec.State = CrimeState.Fled; continue; }       // he was gone before dawn
            if (CarryOne(w, cb, jb, s, rec, who, day, sec)) touched.Add(s.Person);
        }
        foreach (ulong p in touched) Release(w, cb, jb, new EntityId(p), sec);
    }

    /// <summary>Carries one sentence out (or lets a running one run on). Returns whether anything happened that may free the convict.</summary>
    private static bool CarryOne(LawWorld w, CrimeBook cb, JusticeBook jb, Sentence s, CrimeRecord rec, EntityId who, int day, long sec)
    {
        var place = PlaceOpt(w, s.Settlement).ToList();
        switch (s.Kind)
        {
            case "fine" or "penance" or "confiscation" or "kin_pays":
            case "restitution" or "weregild" or "blood_price":
                return Money(w, cb, jb, s, rec, who, day, sec);
            case "stocks":
                if (!s.Started)
                {
                    s.Started = true; s.EndDay = day + Math.Max(1, s.Days);
                    var e = Gaol.Log(w, sec, "sentence_stocks", [who], s.Entry, "ch_justice_stocks", [ChronParam.Person("doer", who), .. place]);
                    Bodily.Stocks(w, who, sec); AddMark(jb, who, "stocks", "", rec, day, e.Id.Value);
                    return false;
                }
                if (day < s.EndDay) return false;
                Done(cb, s, rec); return true;
            case "gaol" or "bond_labour":
                if (!s.Started) { s.Started = true; s.EndDay = day + Math.Max(1, s.Days); return false; }
                if (day < s.EndDay) return false;
                Done(cb, s, rec); return true;
            case "flogging":
            {
                var e = Gaol.Log(w, sec, "sentence_flogged", [who], s.Entry, "ch_justice_flogged", [ChronParam.Person("doer", who), ChronParam.Count("lashes", s.Lashes), .. place]);
                int cuts = Bodily.Flog(w, who, s.Lashes, sec, e.Id.Value);
                AddMark(jb, who, "flogged", cuts > 0 ? "upper_body" : "", rec, day, e.Id.Value);
                Done(cb, s, rec); return true;
            }
            case "branding":
            {
                bool cheek = w.Law.Get(rec.Kind).Permanent;
                var e = Gaol.Log(w, sec, "sentence_branded", [who], s.Entry, "ch_justice_branded", [ChronParam.Person("doer", who), .. place]);
                Bodily.Brand(w, who, cheek, sec, e.Id.Value);
                AddMark(jb, who, "branded", cheek ? "head" : "hand_r", rec, day, e.Id.Value);
                Done(cb, s, rec); return true;
            }
            case "hand":
            {
                var e = Gaol.Log(w, sec, "sentence_hand", [who], s.Entry, "ch_justice_hand", [ChronParam.Person("doer", who), .. place], ("crime", N(rec.Id)));
                string part = Bodily.Hand(w, who, sec, e.Id.Value);
                AddMark(jb, who, "hand", part, rec, day, e.Id.Value);
                Done(cb, s, rec); return true;
            }
            case "hanging":
            {
                var e = Gaol.Log(w, sec, "sentence_hanged", [who], s.Entry, "ch_justice_hanged", [ChronParam.Person("doer", who), .. place], ("crime", N(rec.Id)), ("display", N(s.Display)));
                bool died = Bodily.Hang(w, who, sec, e.Id.Value);
                if (w.IsHero(who)) jb.HeroExecuted = e.Id.Value;
                if (s.Display > 0) Gaol.Log(w, sec, "body_displayed", [who], e.Id.Value, "ch_justice_displayed", [ChronParam.Person("doer", who), ChronParam.Days("days", s.Display), .. place]);
                Done(cb, s, rec);
                if (died) { w.Captives?.Release(who, CaptiveState.Dead, "executed", sec); jb.Gaol.Remove(who.Value); }
                return true;
            }
            case "outlawry":
                Outlaw(w, cb, jb, s, rec, who, day, sec); return true;
            case "exile":
                if (!s.Started)
                {
                    s.Started = true; s.EndDay = day + Math.Max(1, s.Days);
                    Gaol.Log(w, sec, "exile_ordered", [who], s.Entry, "ch_justice_exile", [ChronParam.Person("doer", who), ChronParam.Days("days", Math.Max(1, s.Days)), .. place]);
                    return true;                                                       // let out to go
                }
                if (day < s.EndDay) return false;
                if (w.HasLeftRealm?.Invoke(who) == true) { Done(cb, s, rec); return false; }
                Outlaw(w, cb, jb, s, rec, who, day, sec); return false;
        }
        Done(cb, s, rec);
        return true;
    }

    private static void Done(CrimeBook cb, Sentence s, CrimeRecord rec) { s.State = SentenceState.Done; rec.State = CrimeState.Served; }

    private static void AddMark(JusticeBook jb, EntityId who, string kind, string part, CrimeRecord rec, int day, ulong entry) =>
        jb.Marks.Add(new BodyMark { Person = who.Value, Kind = kind, Part = part, Day = day, Crime = rec.Id, Entry = entry });

    // ------------------------------------------------------------------ coin sentences

    private static bool Money(LawWorld w, CrimeBook cb, JusticeBook jb, Sentence s, CrimeRecord rec, EntityId who, int day, long sec)
    {
        long due = s.Mites; bool toKin = s.Kind is "restitution" or "weregild" or "blood_price";
        PurseId? from = w.PurseOf(who);
        var treasury = w.TreasuryOf?.Invoke(s.Settlement);
        PurseId? to = s.Kind switch
        {
            "restitution" or "weregild" or "blood_price" => w.VictimPurse(rec.Victim) ?? treasury,
            "penance" => w.ChurchPurse(s.Settlement) ?? treasury,
            _ => treasury,
        };
        bool paid = false;
        if (from is { } f && to is { } t && w.Ledger != null && f != t && w.Ledger.Balance(f).Mites >= due)
            paid = due <= 0 || w.Ledger.Transfer(f, t, Coin.FromMites(due), toKin ? TransferReason.Restitution : s.Kind == "penance" ? TransferReason.Alms : TransferReason.Fine);
        if (paid)
        {
            var ps = new List<ChronParam> { ChronParam.Person("doer", who), ChronParam.Mites("amount", due) }; ps.AddRange(PlaceOpt(w, s.Settlement));
            if (s.Kind == "penance") Gaol.Log(w, sec, "penance_done", [who], s.Entry, "ch_justice_penance", ps);
            else Gaol.Log(w, sec, toKin ? "restitution_paid" : "fine_paid", [who], s.Entry, toKin ? "ch_justice_restitution" : "ch_justice_fine", ps);
            Done(cb, s, rec);
            return true;
        }
        // not paid: a term to find it (weregild, blood price), then what the table says
        if (s.Days > 0 && !s.Started)
        {
            s.Started = true; s.EndDay = day + s.Days;
            if (s.Kind == "blood_price" && s.Feud > 0 && rec.Victim != 0) jb.Feuds.Add(new FeudRow { Culprit = s.Person, Kin = rec.Victim, Until = day + s.Feud, Crime = rec.Id });
            return true;                                                                    // he may go and raise it
        }
        if (s.Started && day < s.EndDay) return false;
        // the term is out (or there was none): unpaid
        s.State = SentenceState.Unpaid;
        if (s.Unpaid == "outlawry") { Outlaw(w, cb, jb, s, rec, who, day, sec); return true; }
        long owed = Math.Max(1, due);
        int days = (int)Math.Clamp(owed / Math.Max(1, w.S.GaolMitesPerDay), 1, w.S.GaolMaxDays);
        s.Kind = "gaol"; s.Days = days; s.State = SentenceState.Pending; s.Started = true; s.EndDay = day + days; s.Mites = 0;
        if (!jb.Gaol.ContainsKey(s.Person))
        {
            int fief = s.Fief; int seat = Gaol.SeatOf(w, fief, s.Settlement);
            Gaol.Hold(w, jb, who, fief, seat, sec, false, false, s.Entry, rec.Id);
        }
        return true;
    }

    // ------------------------------------------------------------------ outlawry and the going free

    /// <summary>Outlawry (4.6): the realm's mark on him (his standing there is -1000, anyone may kill him), his deeds there forfeit, a permanent bounty on his head; he is cast out of the cell.</summary>
    private static void Outlaw(LawWorld w, CrimeBook cb, JusticeBook jb, Sentence s, CrimeRecord rec, EntityId who, int day, long sec)
    {
        int realm = w.RealmOf(s.Settlement);
        w.Marks()?.Outlaw(who, realm);
        w.Forfeit?.Invoke(who, realm, sec);
        var ps = new List<ChronParam> { ChronParam.Person("doer", who) }; ps.AddRange(PlaceOpt(w, s.Settlement));
        Gaol.Log(w, sec, "outlawed", [who], s.Entry, "ch_justice_outlawed", ps, ("crime", N(rec.Id)));
        CancelBounty(w, rec, sec);
        if (Bounties.Post(w, rec, sec, outlaw: true) is { } job) { rec.BountyJob = job.Id.Value; }
        s.Kind = "outlawry"; s.State = SentenceState.Done; rec.State = CrimeState.Served;
    }

    /// <summary>
    /// After a morning's carrying out: a person with no charge left to try and no term left to serve goes free with <c>sentence_served</c> (a hanged man is gone). A gaol or stocks term still running keeps him.
    /// </summary>
    private static void Release(LawWorld w, CrimeBook cb, JusticeBook jb, EntityId who, long sec)
    {
        if (!jb.Gaol.TryGetValue(who.Value, out var row)) return;
        if (row.Crimes.Any(id => cb.Get(id) is { State: CrimeState.Held })) return;
        bool holds = jb.Sentences.Values.Any(s => s.Person == who.Value && s.State == SentenceState.Pending && (s.Kind is "gaol" or "bond_labour" or "stocks" || !s.Started));
        if (holds) return;
        bool pending = jb.Sentences.Values.Any(s => s.Person == who.Value && s.State == SentenceState.Pending);
        bool served = !pending && jb.Sentences.Values.Any(s => s.Person == who.Value && s.State is SentenceState.Done or SentenceState.Unpaid);
        var last = jb.Sentences.Values.Where(s => s.Person == who.Value).OrderByDescending(s => s.Day).ThenByDescending(s => s.Crime).FirstOrDefault();
        w.Captives?.Release(who, CaptiveState.Released, served ? "served" : pending ? "paroled" : "acquitted", sec);
        jb.Gaol.Remove(who.Value);
        var ps = new List<ChronParam> { ChronParam.Person("doer", who) }; ps.AddRange(PlaceOpt(w, row.Settlement));
        if (served) Gaol.Log(w, sec, "sentence_served", [who], last?.Entry ?? row.ArrestEntry, "ch_justice_served", ps);
    }
}
