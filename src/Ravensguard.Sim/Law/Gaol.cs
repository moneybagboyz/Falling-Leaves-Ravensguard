using System.Globalization;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Law;

/// <summary>What a bribe came to: taken (the man walks out), refused (a new crime) or not offered at all (no gaoler, not held, no coin).</summary>
public enum BribeOutcome : byte { NotHeld, Cannot, TooLittle, Honest, Failed, Taken }

/// <summary>
/// Arrest and the cell (crime-and-justice 4.4, 4.5): a person taken by the guards (or delivered to them) is held in the fief's gaol as a captive of <see cref="CaptorKind.Gaol"/>, bound (chained for a murderer
/// or a traitor), fed a measure of bread a day from the fief treasury, and kept until the court tries him and the sentence is served. The ways out are the court's (acquittal, a fine, the term served), the
/// captive's own (the existing lock and bonds attempts, which free him and make a jailbreak) and the gaoler's price (<see cref="Bribe"/>). Every step is a chronicle entry whose cause is the one before it.
/// </summary>
public static class Gaol
{
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Adds a justice entry with its typed parameters and a cause on an earlier entry (or none).</summary>
    internal static ChronicleEntry Log(LawWorld w, long sec, string type, IReadOnlyList<EntityId> subjects, ulong cause, string textKey, IReadOnlyList<ChronParam> ps, params (string K, string V)[] data)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in data) d[k] = v;
        var e = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(sec), type, subjects, [cause != 0 ? new Cause(CauseKind.Justice, new EntityId(cause)) : new Cause(CauseKind.Justice)], d, TextKey: textKey, Params: ps);
        w.Chronicle.Add(e);
        return e;
    }

    /// <summary>The entry a crime's later steps hang on: the report if there was one, else the witnessing, else the act.</summary>
    internal static ulong CauseOf(CrimeRecord rec) => rec.Reported != 0 ? rec.Reported : rec.Witnessed != 0 ? rec.Witnessed : rec.Entry;

    private static ChronParam Place(LawWorld w, int settlement) => ChronParam.Place("place", w.PlaceId(settlement));
    private static IEnumerable<ChronParam> PlaceOpt(LawWorld w, int settlement) => w.PlaceId(settlement) != EntityId.None ? [Place(w, settlement)] : [];

    /// <summary>The settlement whose gaol holds a fief's prisoners: its seat, else the place of the crime.</summary>
    public static int SeatOf(LawWorld w, int fief, int fallback) => fief >= 0 && fief < w.FiefSeat.Length && w.FiefSeat[fief] >= 0 ? w.FiefSeat[fief] : fallback;

    // ------------------------------------------------------------------ arrest

    /// <summary>The crimes a person may be held for now: known to be his, not yet in a cell or past it, in id order.</summary>
    public static List<CrimeRecord> Charges(CrimeBook cb, EntityId person) => [.. cb.Open(person).Where(c => c.State is CrimeState.Seen or CrimeState.Reported or CrimeState.Accused or CrimeState.Fled)];

    /// <summary>
    /// The guards take a person (or he gives himself up): the gaol row, the captive (bound, chained for a murderer or a traitor), every open charge Held, and <c>crime_arrested</c> hanging on the first charge's
    /// report. <paramref name="inAct"/>: taken running from the crime under the cry (the court then needs no witness). <paramref name="collector"/>: a bounty hunter or guard who brought him in is paid the bounty
    /// alive (a purse and a person; <see cref="Bounties.Collect"/>). Returns the charges (empty: nothing to hold him for).
    /// </summary>
    public static List<CrimeRecord> Arrest(LawWorld w, CrimeBook cb, JusticeBook jb, EntityId person, int settlement, long sec, bool inAct, EntityId arrester = default, (EntityId Who, PurseId Purse)? collector = null)
    {
        var charges = Charges(cb, person);
        if (charges.Count == 0 && !jb.Gaol.ContainsKey(person.Value)) return charges;
        int fief = w.FiefOf(charges.Count > 0 ? charges[0].Settlement : settlement); if (fief < 0) fief = w.FiefOf(settlement);
        int seat = SeatOf(w, fief, charges.Count > 0 ? charges[0].Settlement : settlement);
        var row = jb.Gaol.GetValueOrDefault(person.Value);
        if (row == null) row = Hold(w, jb, person, fief, seat, sec, inAct, charges.Any(c => w.Law.Get(c.Kind).Permanent), charges.Count > 0 ? CauseOf(charges[0]) : 0, charges.Count > 0 ? charges[0].Id : 0, arrester);
        else row.InAct |= inAct;
        foreach (var c in charges)
        {
            c.State = CrimeState.Held;
            if (!row.Crimes.Contains(c.Id)) row.Crimes.Add(c.Id);
            if (collector is { } col && c.BountyJob != 0) Bounties.Collect(w, cb, new EntityId(c.BountyJob), col.Who, col.Purse, alive: true, sec);
        }
        row.Crimes.Sort();
        return charges;
    }

    /// <summary>The cell door shuts: the row, the <c>crime_arrested</c> entry on <paramref name="cause"/>, and the captive of the fief (chained when <paramref name="heavy"/>). Used by an arrest and by a debtor held again.</summary>
    internal static GaolRow Hold(LawWorld w, JusticeBook jb, EntityId person, int fief, int seat, long sec, bool inAct, bool heavy, ulong cause, int crime, EntityId arrester = default)
    {
        var ps = new List<ChronParam> { ChronParam.Person("doer", person) }; ps.AddRange(PlaceOpt(w, seat));
        var subj = new List<EntityId> { person }; if (arrester != EntityId.None) subj.Add(arrester);
        var e = Log(w, sec, "crime_arrested", subj, cause, "ch_justice_arrested", ps, ("crime", N(crime)));
        var row = new GaolRow { Person = person.Value, Fief = fief, Settlement = seat, SinceDay = (int)(sec / 86_400), Gaoler = GaolerId(w, seat).Value, InAct = inAct, ArrestEntry = e.Id.Value };
        jb.Gaol[person.Value] = row;
        if (w.Captives != null && w.Ledger != null)
        {
            var pref = new PurseRef(PurseKind.Treasury, fief >= 0 && fief < w.FiefIds.Length ? w.FiefIds[fief] : EntityId.None);
            w.Captives.CaptureGaol(w.Rules, person, w.IsHero(person), pref.Owner, pref, w.PlaceId(seat), sec, heavy ? Restraint.Chained : Restraint.Bound, w.SkillsOf(person), Math.Max(1, Guards.Of(w.People, seat).Count));
        }
        return row;
    }

    private static EntityId GaolerId(LawWorld w, int seat) { int g = Guards.Gaoler(w.People, seat); return g < 0 ? EntityId.None : w.People.IdOf(g); }

    /// <summary>The settlement nearest to a spot of the region (millimetres): the place a fight on the road or in a street was fought, for the court that tries it.</summary>
    public static int NearestSettlement(Ravensguard.Sim.WorldGen.Region.Region region, long xMm, long yMm)
    {
        int best = -1; long bd = long.MaxValue;
        for (int i = 0; i < region.Settlements.Count; i++)
        {
            long dx = region.Settlements[i].CenterX - xMm / 1000, dy = region.Settlements[i].CenterY - yMm / 1000, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// The hero beaten by the guards (<see cref="DefeatTable"/>: guards win, he is Captured, no fine and release any more): taken to the gaol of the nearest settlement's fief. With nothing yet charged against him
    /// he is charged with assault on the watch (a guard of the place the witness), so there is always a case for the court; he is taken in the act.
    /// </summary>
    public static void ArrestHero(Ravensguard.Sim.Save.WorldState st, Ravensguard.Content.ContentDatabase content, Ravensguard.Sim.Trade.HeroParty party, long placeX, long placeY, long sec)
    {
        var w = LawWorld.From(st, content); var hero = party.Hero;
        var cb = st.Law ??= new CrimeBook(); var jb = st.Justice ??= new JusticeBook();
        int town = NearestSettlement(st.Population.Region, placeX, placeY); if (town < 0) town = 0;
        var guards = Guards.Of(w.People, town);
        if (Charges(cb, hero).Count == 0)
        {
            var witnesses = guards.Count > 0 ? new List<WitnessRow> { new(w.People.IdOf(guards[0]).Value, WitnessRow.KnewIt) } : [];
            var rec = CrimeSystem.Commit(w, cb, new ActSpec("assault", hero, EntityId.None, town, sec, 0, 0, "ch_note_crime_hero"), witnesses, ActorLook.None);
            rec.Perpetrator = (long)hero.Value; rec.Reported = rec.Witnessed; rec.Reporter = witnesses.Count > 0 ? witnesses[0].Person : 0; rec.State = CrimeState.Reported;
        }
        Arrest(w, cb, jb, hero, town, sec, inAct: true, guards.Count > 0 ? w.People.IdOf(guards[0]) : EntityId.None);
    }

    /// <summary>A bounty hunter delivers a culprit he took (alive): the same arrest, the hunter paid the bounty at once. The hero's capture of a wanted man is the caller's (D3, the live tier).</summary>
    public static List<CrimeRecord> Deliver(LawWorld w, CrimeBook cb, JusticeBook jb, EntityId culprit, EntityId hunter, PurseId hunterPurse, int settlement, long sec) =>
        Arrest(w, cb, jb, culprit, settlement, sec, false, hunter, (hunter, hunterPurse));

    // ------------------------------------------------------------------ the guards come for the known (abstract tier)

    /// <summary>
    /// The day's arrests (stream 0x615, key crime and day): a known culprit of a Reported crime is taken with <c>ArrestPct</c> when a guard of the place is on duty at the hour and he is free; the hero is never
    /// taken here (the live guards pursue him: <see cref="Arrest"/> is called by the street or by a surrender). Returns who was taken, in id order.
    /// </summary>
    public static List<EntityId> Daily(LawWorld w, CrimeBook cb, JusticeBook jb, int day, long sec)
    {
        var taken = new List<EntityId>();
        int hod = (int)(sec / 3600 % 24);
        foreach (var rec in cb.Crimes.Values.ToList())
        {
            if (rec.State is not (CrimeState.Reported or CrimeState.Fled) || !rec.Known || rec.Settlement < 0 || rec.Day >= day) continue;
            var who = new EntityId((ulong)rec.Perpetrator);
            if (w.IsHero(who) || jb.Gaol.ContainsKey(who.Value)) continue;
            int row = w.People.IndexOf(who); if (row < 0 || !w.People.IsAlive(row)) continue;
            if (taken.Contains(who)) continue;
            // a guard of the place awake at the hour (the watch of the culprit's town; the day shift by day, the night men by night)
            var on = Guards.OnDuty(w.People, rec.Settlement, hod); if (on.Count == 0) continue;
            if (DetRng.Range(w.Seed, day, (ulong)rec.Id, RngStreams.ArrestRoll, 0, 0, 100) >= w.S.ArrestPct) continue;
            if (Arrest(w, cb, jb, who, rec.Settlement, sec, false, w.People.IdOf(on[0])).Count > 0) taken.Add(who);
        }
        return taken;
    }

    // ------------------------------------------------------------------ the cell: fed, kept, released

    /// <summary>
    /// The day's feeding (H5 hour 0, crime 4.5): every prisoner of a gaol is bought a measure of bread from the fief treasury; a band's captives eat from the band's stash. Coin goes to the baker the town has (else it is
    /// spent: a sink). A prisoner unfed for <c>UnfedReleaseDays</c> days in a row is let go: nobody starves in a cell (CJ-16). Returns how many were fed.
    /// </summary>
    public static int Feed(LawWorld w, CrimeBook cb, JusticeBook jb, int day, long sec)
    {
        if (w.Captives == null || w.Ledger == null) return 0;
        int fed = 0;
        foreach (var c in w.Captives.Captives.Values.ToList())
        {
            if (!c.Held || c.Captor is not (CaptorKind.Gaol or CaptorKind.Bandits)) continue;
            if (c.Captor == CaptorKind.Gaol && !jb.Gaol.ContainsKey(c.Person.Value)) continue;
            int ration = w.S.RationMites;
            PurseId? from = c.CaptorPurse.Find(w.Ledger) is { } p ? p : null;
            bool ok = from is { } f && w.Ledger.Balance(f).Mites >= ration && Pay(w, f, jb.Gaol.GetValueOrDefault(c.Person.Value)?.Settlement ?? w.HomeOf(c.Person), ration);
            if (c.Captor == CaptorKind.Gaol)
            {
                var row = jb.Gaol[c.Person.Value];
                if (ok) { row.Unfed = 0; row.FedDays++; fed++; }
                else if (++row.Unfed >= w.S.UnfedReleaseDays) Free(w, jb, c.Person, CaptiveState.Released, "unfed", sec, "jl_why_unfed", cb);
            }
            else
            {
                if (ok) { jb.BandUnfed.Remove(c.Person.Value); fed++; }
                else
                {
                    int n = jb.BandUnfed.GetValueOrDefault(c.Person.Value) + 1;
                    if (n >= w.S.UnfedReleaseDays) { jb.BandUnfed.Remove(c.Person.Value); w.Captives.Release(c.Person, CaptiveState.Released, "unfed", sec); }
                    else jb.BandUnfed[c.Person.Value] = n;
                }
            }
        }
        return fed;
    }

    private static bool Pay(LawWorld w, PurseId from, int settlement, long ration)
    {
        if (w.RationPayee(settlement) is { } baker && baker != from) return w.Ledger!.Transfer(from, baker, Coin.FromMites(ration), TransferReason.Trade);
        return w.Ledger!.Sink(from, Coin.FromMites(ration), SinkReason.Burn);
    }

    /// <summary>
    /// A held person goes free: the captive's restraint is off, the row goes, his unfinished charges go back to Reported (the law still wants him) unless the court had ruled, and a <c>gaol_released</c> entry says
    /// why. <paramref name="whyKey"/> is the chronicle text key of the reason.
    /// </summary>
    public static void Free(LawWorld w, JusticeBook jb, EntityId person, CaptiveState how, string note, long sec, string whyKey, CrimeBook? cb = null)
    {
        w.Captives?.Release(person, how, note, sec);
        if (!jb.Gaol.Remove(person.Value, out var row)) return;
        if (cb != null) foreach (int id in row.Crimes) if (cb.Get(id) is { State: CrimeState.Held } c) c.State = CrimeState.Reported;
        var ps = new List<ChronParam> { ChronParam.Person("doer", person), ChronParam.TextKey("why", whyKey) }; ps.AddRange(PlaceOpt(w, row.Settlement));
        Log(w, sec, "gaol_released", [person], row.ArrestEntry, "ch_justice_released", ps);
    }

    // ------------------------------------------------------------------ the gaoler's price

    /// <summary>The least offer a gaoler of this honesty will weigh: <c>3 x daily wage x (60 - honesty) / 20</c>; null for an honest man (60 or more), who never takes one.</summary>
    public static long? BribeFloor(LawWorld w, int honesty) => honesty >= w.S.BribeHonestAt ? null : (long)w.S.BribeMult * w.S.GuardWageMites * (w.S.BribeHonestAt - honesty) / Math.Max(1, w.S.BribeDivisor);

    /// <summary>The chance (percent) an offer at or over the floor is taken: <c>60 - honesty</c>.</summary>
    public static int BribePct(LawWorld w, int honesty) => Math.Max(0, w.S.BribeHonestAt - honesty);

    /// <summary>
    /// The prisoner offers the gaoler coin (crime 4.5, <c>heists-and-jailbreaks</c> 4.3: one rule for every official). Under the floor, or an honest man: refused; at or over it taken with P = 60 - honesty percent
    /// (stream 0x613, key official and day). Taken: the coin moves (reason Bribe), the man walks out (the law still wants him) and a <c>bribe_taken</c> entry says so. Refused: a new crime, bribery, the gaoler
    /// its witness, tried with the rest.
    /// </summary>
    public static BribeOutcome Bribe(LawWorld w, CrimeBook cb, JusticeBook jb, EntityId person, long offer, long sec)
    {
        if (!jb.Gaol.TryGetValue(person.Value, out var row) || w.Captives?.Find(person) is not { Held: true }) return BribeOutcome.NotHeld;
        var gaoler = new EntityId(row.Gaoler);
        if (gaoler == EntityId.None || w.PurseOf(person) is not { } from || w.PurseOf(gaoler) is not { } to || w.Ledger == null) return BribeOutcome.Cannot;
        if (offer <= 0 || w.Ledger.Balance(from).Mites < offer) return BribeOutcome.Cannot;
        int day = (int)(sec / 86_400), honesty = w.HonestyOf(gaoler);
        var floor = BribeFloor(w, honesty);
        BribeOutcome result;
        if (floor == null) result = BribeOutcome.Honest;
        else if (offer < floor) result = BribeOutcome.TooLittle;
        else result = DetRng.Range(w.Seed, day, gaoler.Value, RngStreams.BribeRoll, 0, 0, 100) < BribePct(w, honesty) ? BribeOutcome.Taken : BribeOutcome.Failed;
        if (result == BribeOutcome.Taken)
        {
            w.Ledger.Transfer(from, to, Coin.FromMites(offer), TransferReason.Bribe);
            var ps = new List<ChronParam> { ChronParam.Person("official", gaoler), ChronParam.Person("doer", person), ChronParam.Mites("amount", offer) }; ps.AddRange(PlaceOpt(w, row.Settlement));
            var e = Log(w, sec, "bribe_taken", [gaoler, person], row.ArrestEntry, "ch_justice_bribe_taken", ps);
            w.Captives!.Release(person, CaptiveState.Released, "bribed", sec);
            jb.Gaol.Remove(person.Value);
            foreach (int id in row.Crimes) if (cb.Get(id) is { State: CrimeState.Held } c) c.State = CrimeState.Fled;
            _ = e;
            return result;
        }
        // refused: a new crime with the gaoler as its witness, tried with the others
        var rec = CrimeSystem.Commit(w, cb, new ActSpec("bribery", person, gaoler, row.Settlement, sec, offer, 0, w.IsHero(person) ? "ch_note_crime_hero" : "ch_note_crime_greed"), [new WitnessRow(gaoler.Value, WitnessRow.KnewIt)], ActorLook.None);
        rec.State = CrimeState.Held; if (!row.Crimes.Contains(rec.Id)) row.Crimes.Add(rec.Id); row.Crimes.Sort();
        var rps = new List<ChronParam> { ChronParam.Person("official", gaoler), ChronParam.Person("doer", person) }; rps.AddRange(PlaceOpt(w, row.Settlement));
        Log(w, sec, "bribe_refused", [gaoler, person], rec.Witnessed, "ch_justice_bribe_refused", rps);
        return result;
    }

    // ------------------------------------------------------------------ out by his own hand

    /// <summary>
    /// The daily look at who is no longer held (escaped by lock or bonds, rescued): the row goes, the crimes he was held for go back to wanted (Fled) and he has broken out of gaol, a crime of its own (jailbreak,
    /// witnessed by the gaoler) with its bounty. Returns how many broke out.
    /// </summary>
    public static int Escapes(LawWorld w, CrimeBook cb, JusticeBook jb, long sec)
    {
        if (w.Captives == null) return 0;
        int n = 0;
        foreach (var (pv, row) in jb.Gaol.ToList())
        {
            var who = new EntityId(pv);
            var cap = w.Captives.Find(who);
            if (cap == null || cap.Held || cap.Captor != CaptorKind.Gaol) continue;
            if (cap.State is not (CaptiveState.Escaped or CaptiveState.Rescued)) { jb.Gaol.Remove(pv); continue; }       // let go by us (released) leaves no trace here
            jb.Gaol.Remove(pv);
            foreach (int id in row.Crimes) if (cb.Get(id) is { State: CrimeState.Held } c) c.State = CrimeState.Fled;
            foreach (var s in jb.Sentences.Values) if (s.Person == pv && s.State == SentenceState.Pending) s.State = SentenceState.Avoided;
            var gaoler = new EntityId(row.Gaoler);
            var witnesses = gaoler == EntityId.None ? [] : new List<WitnessRow> { new(gaoler.Value, WitnessRow.KnewIt) };
            var rec = CrimeSystem.Commit(w, cb, new ActSpec("jailbreak", who, gaoler, row.Settlement, sec, 0, 0, "ch_note_crime_escape"), witnesses, ActorLook.None);
            n++;
            _ = rec;
        }
        return n;
    }
}
