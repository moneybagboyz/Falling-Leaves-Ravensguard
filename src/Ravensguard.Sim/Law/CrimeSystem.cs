using Ravensguard.Sim.Core;
using Ravensguard.Sim.Jobs;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Law;

/// <summary>The act as the caller describes it: what, who, to whom, where and when, the value stolen or the damage done, and the cause note key of the act's chronicle entry.</summary>
public sealed record ActSpec(string Kind, EntityId Doer, EntityId Victim, int Settlement, long Sec, long Value, long Damage, string NoteKey, IReadOnlyList<ChronParam>? NoteParams = null);

/// <summary>What an act left behind: the record (null when nobody saw it) and the cry that was raised.</summary>
public sealed record CommitResult(CrimeRecord? Record, CryResult? Cry);

/// <summary>
/// The crime core (crime-and-justice 4.1 to 4.3): an act is seen or not by geometry; a seen act becomes a record with its chain of chronicle entries (the act, the witnessed entry, the hue and cry), and the
/// daily pass lets a witness report it (stream 0x610), which makes it known to the law and has the lord post a bounty. Each step's cause is the one before, so "why is there a price on my head" always
/// has an answer. The seed keys are crime ids, witness ids and game days, never a tick.
/// </summary>
public static class CrimeSystem
{
    // ------------------------------------------------------------------ the act

    /// <summary>
    /// The hero's act in the live street (or any act with positions): who saw it is the geometry of <see cref="Witnessing"/>. Nobody saw: no record, no entry, no rumour (CJ-2). Otherwise the record, the
    /// chronicle entries and (for a crime that raises one, in a settlement) the hue and cry.
    /// </summary>
    public static CommitResult Commit(LawWorld w, CrimeBook book, ActSpec a, IWitnessScene scene, WitnessActor actor, IReadOnlyList<Observer> everyone)
    {
        var s = w.S; var sightings = Witnessing.Evaluate(s, scene, actor, everyone);
        if (sightings.Count == 0) return new CommitResult(null, null);
        var rows = sightings.Select(x => new WitnessRow(x.Who.Person.Value,
            (byte)((x.Recognises ? WitnessRow.KnewIt : 0) | (HueAndCry.IsFriend(s, x.Who) ? WitnessRow.FriendFlag : 0) | (HueAndCry.Fears(s, actor, x, sightings) ? WitnessRow.FearFlag : 0)))).ToList();
        CryResult? cry = w.Law.Get(a.Kind).Cry && a.Settlement >= 0 ? HueAndCry.Raise(s, scene, actor, sightings, everyone) : null;
        var rec = Write(w, book, a, rows, actor.Look, cry?.Info);
        return new CommitResult(rec, cry);
    }

    /// <summary>An act in the abstract tier (NPC crime): the witnesses are given, with their flags, and no cry is modelled.</summary>
    public static CrimeRecord Commit(LawWorld w, CrimeBook book, ActSpec a, IReadOnlyList<WitnessRow> witnesses, ActorLook look, CryInfo? cry = null) => Write(w, book, a, witnesses, look, cry);

    private static CrimeRecord Write(LawWorld w, CrimeBook book, ActSpec a, IReadOnlyList<WitnessRow> witnesses, ActorLook look, CryInfo? cry)
    {
        var def = w.Law.Get(a.Kind); var when = new GameTime(a.Sec);
        var recognisers = witnesses.Where(x => x.Recognised).Select(x => x.Person).Order().ToList();
        var rec = new CrimeRecord
        {
            Kind = a.Kind, Doer = a.Doer.Value, Perpetrator = recognisers.Count > 0 ? (long)a.Doer.Value : -1, Look = look, Victim = a.Victim.Value, Settlement = a.Settlement,
            Day = (int)when.TotalDays, Sec = a.Sec, Witnesses = [.. witnesses.OrderBy(x => x.Person)], ValueMites = a.Value, Damage = a.Damage, Cried = cry != null,
        };
        var place = w.PlaceId(a.Settlement);
        var ps = new List<ChronParam> { ChronParam.Person("doer", a.Doer) };
        if (a.Victim != EntityId.None && a.Victim.Kind == IdKind.Person) ps.Add(ChronParam.Person("victim", a.Victim));
        if (place != EntityId.None) ps.Add(ChronParam.Place("place", place));
        if (a.Value > 0) ps.Add(ChronParam.Mites("value", a.Value));
        var subjects = new List<EntityId> { a.Doer };
        if (a.Victim != EntityId.None) subjects.Add(a.Victim);
        var data = new Dictionary<string, string>
        {
            ["settlement"] = w.PlaceName(a.Settlement), ["kind"] = a.Kind, ["doer"] = w.Name(a.Doer), ["value"] = a.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["seen"] = string.Join(",", recognisers),
        };
        string type = w.Law.EntryOf(a.Kind);
        var act = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), when, type, subjects, [new Cause(CauseKind.Crime, null, NoteKey: a.NoteKey, NoteParams: a.NoteParams)], data, TextKey: "ch_crime_" + type, Params: ps);
        w.Chronicle.Add(act);
        rec.Entry = act.Id.Value;
        book.Add(rec);
        w.Committed?.Invoke(a.Doer, a.Kind);                                                // P6-B4: the sin costs its piety (faith 4.3)
        ChronicleEntry wit;
        if (witnesses.Count == 0)
        {
            // nobody saw it: the victim finds the loss (NPC crime), and his report is the next link of the chain
            wit = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), when, "crime_discovered", [a.Victim], [new Cause(CauseKind.Crime, act.Id)], new Dictionary<string, string> { ["crime"] = rec.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["kind"] = a.Kind },
                TextKey: "ch_crime_discovered", Params: [ChronParam.Person("victim", a.Victim), .. place != EntityId.None ? [ChronParam.Place("place", place)] : Array.Empty<ChronParam>()]);
        }
        else
        {
            // the witnessed entry names the first witness (one who knew him first) and carries the count
            var lead = witnesses.OrderByDescending(x => x.Recognised).ThenBy(x => x.Person).First();
            var leadId = new EntityId(lead.Person);
            wit = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), when, "crime_witnessed", [leadId, a.Doer], [new Cause(CauseKind.Crime, act.Id)], new Dictionary<string, string> { ["crime"] = rec.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["kind"] = a.Kind },
                TextKey: "ch_crime_witnessed", Params: [ChronParam.Person("witness", leadId), ChronParam.Person("doer", a.Doer), ChronParam.Count("witnesses", witnesses.Count), .. place != EntityId.None ? [ChronParam.Place("place", place)] : Array.Empty<ChronParam>()]);
        }
        w.Chronicle.Add(wit);
        rec.Witnessed = wit.Id.Value;
        if (cry is { } cr)
            w.Chronicle.Add(new ChronicleEntry(w.Ids.Allocate(IdKind.Event), when, "hue_and_cry", [cr.Raiser], [new Cause(CauseKind.Crime, wit.Id)], new Dictionary<string, string> { ["crime"] = rec.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                TextKey: "ch_crime_cry", Params: [ChronParam.Person("witness", cr.Raiser), ChronParam.Count("heard", cr.Hearers), ChronParam.Count("guards", cr.Guards)]));
        return rec;
    }

    // ------------------------------------------------------------------ reports and bounties (the day's first hour)

    /// <summary>
    /// The day's pass (H5 hour 0, crime 4.9): every crime still only seen is reported by a witness who passes the draw (0x610, key crime, witness: 90%, minus 40 if he fears the actor, minus 60 if he is
    /// kin or friend), in crime id and witness id order; a report makes it Reported, writes <c>crime_reported</c> and has the lord post the bounty. Expired bounties are refunded. Returns the crimes reported.
    /// </summary>
    public static List<CrimeRecord> Daily(LawWorld w, CrimeBook book, int day)
    {
        var done = new List<CrimeRecord>();
        long now = day * 86_400L;
        foreach (var rec in book.Crimes.Values)
        {
            if (rec.State != CrimeState.Seen || rec.Day >= day) continue;
            foreach (var wr in rec.Witnesses)
            {
                int row = w.People.IndexOf(new EntityId(wr.Person));
                if (row < 0 || !w.People.IsAlive(row)) continue;
                int p = w.S.ReportPct - (wr.Fears ? w.S.ReportFearPct : 0) - (wr.Friend ? w.S.ReportFriendPct : 0);
                if (DetRng.Range(w.Seed, day, (ulong)rec.Id, RngStreams.CrimeReporting, (uint)(wr.Person & 0xFFFFFFFFUL), 0, 100) >= p) continue;
                Report(w, rec, new EntityId(wr.Person), now);
                done.Add(rec); break;
            }
        }
        if (w.Jobs != null && w.JobEnv != null) w.Jobs.AdvanceWhere(w.JobEnv, now, j => j.Kind == OddJobKind.Bounty);
        return done;
    }

    /// <summary>A victim who finds the loss reports it himself (NPC crime with no witness): the same chain, the reporter the victim.</summary>
    public static void Report(LawWorld w, CrimeRecord rec, EntityId reporter, long nowSec)
    {
        var when = new GameTime(Math.Max(nowSec, rec.Sec));
        var place = w.PlaceId(rec.Settlement);
        var causeId = new EntityId(rec.Witnessed);
        var ps = new List<ChronParam> { ChronParam.Person("witness", reporter) };
        if (place != EntityId.None) ps.Add(ChronParam.Place("place", place));
        var rep = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), when, "crime_reported", [reporter], [new Cause(CauseKind.Crime, causeId)], new Dictionary<string, string> { ["crime"] = rec.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["kind"] = rec.Kind },
            TextKey: "ch_crime_reported", Params: ps);
        w.Chronicle.Add(rep);
        rec.Reported = rep.Id.Value; rec.Reporter = reporter.Value; rec.State = CrimeState.Reported;
        var job = Bounties.Post(w, rec, when.Seconds);
        if (job == null) return;
        rec.BountyJob = job.Id.Value;
        var bps = new List<ChronParam> { ChronParam.Mites("amount", job.WageMites), ChronParam.Person("witness", reporter) };
        if (place != EntityId.None) bps.Add(ChronParam.Place("place", place));
        var subjects = new List<EntityId> { reporter }; if (rec.Known) subjects.Add(new EntityId(rec.Doer));
        var posted = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), when, "bounty_posted", subjects, [new Cause(CauseKind.Crime, rep.Id)], new Dictionary<string, string> { ["crime"] = rec.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["amount"] = job.WageMites.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            TextKey: "ch_crime_bounty", Params: bps);
        w.Chronicle.Add(posted); rec.BountyEntry = posted.Id.Value;
    }

    /// <summary>A guard matches a person to a described crime by clothes (0x611): a crime still unknown-doer whose description fits now. Returns whether the guard names him.</summary>
    public static bool GuardMatches(LawWorld w, CrimeRecord rec, EntityId guard, EntityId person, int day, ActorLook now) =>
        !rec.Known && Witnessing.GuardRecognises(w.Seed, guard, person, day, rec.Look, now);
}
