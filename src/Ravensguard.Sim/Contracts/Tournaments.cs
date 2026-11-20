using System.Globalization;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;

namespace Ravensguard.Sim.Contracts;

/// <summary>
/// Tournaments as fair events (contracts-and-rumours-v2 4.4). A town with a market charter holds one at its great fair when its treasury holds the purse: the purse moves from the treasury into escrow at the
/// announcement, fifteen days before; entry is a fee into the same escrow; the bracket is drawn on the fair day (stream 0x692, key the tournament and the round); bouts between NPCs are fought by the sim's own
/// rating draw, a bout with a person who is not rated by the sim waits for the sim's fight record, and the purse is paid to the winner of the final as <b>the bout records</b> name him. A result nobody fought changes nothing.
/// </summary>
public static class Tournaments
{
    public static EntityId EscrowId(int tid) => EntityId.Make(IdKind.Other, 0x49_0000_0000_0000UL | (uint)tid);
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
    public static int MaxEntrants => 8;
    /// <summary>The id that stands for nobody (a bye, a bout not yet fought): the hero is person 0, so zero cannot be it.</summary>
    public const ulong NoOne = ulong.MaxValue;

    /// <summary>The announcement (hour 0 of the day the fair is <c>TournamentAnnounceDays</c> off): each charter town, in order, whose treasury holds the purse puts it into escrow. Returns the tournaments announced.</summary>
    public static List<Tournament> Announce(ContractWorld w, ContractBook book, int day)
    {
        var made = new List<Tournament>();
        if (w.CharterTowns == null || w.FairInDays == null || w.FairInDays(day) != w.S.TournamentAnnounceDays) return made;
        int year = day / TimeConst.DaysPerYear;
        foreach (int s in w.CharterTowns().OrderBy(x => x))
        {
            if (book.Tournaments.Values.Any(t => t.Settlement == s && t.Year == year)) continue;
            if (w.TreasuryOf(s) is not { } treasury || w.Ledger.Balance(treasury).Mites < w.S.TournamentPurseMites) continue;
            int tid = book.NextTournament++;
            var escrow = w.Ledger.Open(PurseKind.Chest, EscrowId(tid));
            if (!w.Ledger.Transfer(treasury, escrow, new Coin(w.S.TournamentPurseMites), TransferReason.ContractEscrow)) continue;
            var t = new Tournament { Id = tid, Settlement = s, Year = year, FairDay = day + w.S.TournamentAnnounceDays, Escrow = escrow.Index, TreasuryPurse = treasury.Index, PurseMites = w.S.TournamentPurseMites, Fee = w.S.TournamentFeeMites };
            book.Tournaments[tid] = t;
            int f = w.FiefOf(s); var issuer = f >= 0 && f < w.FiefIds.Length ? w.FiefIds[f] : w.Ledger.OwnerOf(treasury);
            var ps = new List<ChronParam> { ChronParam.Mites("purse", t.PurseMites), ChronParam.Count("fair", t.FairDay) };
            if (w.PlaceId(s) != EntityId.None) ps.Add(ChronParam.Place("place", w.PlaceId(s)));
            var e = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86_400L), "tournament_announced", [issuer], [new Cause(CauseKind.Other, null, NoteKey: "ch_note_tournament_fair")],
                new Dictionary<string, string> { ["tournament"] = N(tid), ["settlement"] = w.PlaceName(s), ["purse"] = N(t.PurseMites), ["fair_day"] = N(t.FairDay) }, TextKey: "ch_tournament_announced", Params: ps);
            w.Chronicle.Add(e);
            // the entry contract: the purse is its escrow; the board lists it until the fair day
            var c = new WorkContract { Id = book.NextId++, Kind = "tournament_entry", Issuer = issuer, IssuerPurse = treasury.Index, Escrow = escrow.Index, PayMites = t.PurseMites, PostedDay = day, DeadlineDay = t.FairDay, Settlement = s, Param = tid, State = WorkState.Open, CauseEntry = e.Id.Value, PostedEntry = e.Id.Value };
            book.Contracts[c.Id] = c;
            made.Add(t);
        }
        return made;
    }

    /// <summary>A fighter enters: the fee moves from his purse into the purse's escrow (a ledger transfer). False when the fair has begun, he is in already, the bracket is full or he cannot pay.</summary>
    public static bool Enter(ContractWorld w, ContractBook book, int tid, EntityId person, int day)
    {
        if (!book.Tournaments.TryGetValue(tid, out var t) || t.Drawn || t.Cancelled || day >= t.FairDay) return false;
        if (t.Entrants.Contains(person.Value) || t.Entrants.Count >= MaxEntrants) return false;
        if (w.PurseOf(person) is not { } purse || w.Ledger.Balance(purse).Mites < t.Fee) return false;
        if (t.Fee > 0 && !w.Ledger.Transfer(purse, new PurseId(t.Escrow), new Coin(t.Fee), TransferReason.ContractEscrow)) return false;
        t.Entrants.Add(person.Value); t.Entrants.Sort();
        return true;
    }

    /// <summary>The fair day's morning: the town's own fighters fill the empty places (each pays his fee), and the bracket is drawn by stream 0x692 (key the tournament and round 0). A bracket of fewer than two is called off, the purse back to the treasury.</summary>
    public static void Draw(ContractWorld w, ContractBook book, int tid, int day)
    {
        if (!book.Tournaments.TryGetValue(tid, out var t) || t.Drawn || t.Cancelled || day < t.FairDay) return;
        if (w.Fighters != null)
            foreach (var (p, _) in w.Fighters(t.Settlement).OrderByDescending(x => x.Rating).ThenBy(x => x.Person))
            {
                if (t.Entrants.Count >= MaxEntrants) break;
                Enter(w, book, tid, p, t.FairDay - 1);
            }
        if (t.Entrants.Count < 2) { Cancel(w, book, t, day); return; }
        var order = t.Entrants.ToList();
        for (int i = order.Count - 1; i > 0; i--)                                  // Fisher-Yates by the bracket stream
        {
            int j = DetRng.Range(w.Seed, 0, (ulong)tid, RngStreams.TournamentBrackets, (uint)i, 0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        int size = 1; while (size < order.Count) size <<= 1;
        while (order.Count < size) order.Add(NoOne);                                    // byes
        for (int i = 0; i < size; i += 2) t.Bouts.Add(MakeBout(w, t, 0, i / 2, order[i], order[i + 1]));
        t.Drawn = true;
    }

    private static Bout MakeBout(ContractWorld w, Tournament t, int round, int slot, ulong a, ulong b)
    {
        var bout = new Bout { Round = round, Slot = slot, A = a, B = b, Winner = NoOne, AngleDeg = DetRng.Range(w.Seed, round, (ulong)t.Id, RngStreams.TournamentBrackets, (uint)(1000 + slot), 0, w.S.JoustMaxAngleDeg + 1) };
        if (b == NoOne && a != NoOne) bout.Winner = a; else if (a == NoOne && b != NoOne) bout.Winner = b;
        return bout;
    }

    private static void Cancel(ContractWorld w, ContractBook book, Tournament t, int day)
    {
        // everything in the escrow goes back where it came from: the fees to the entrants, the rest to the treasury
        var esc = new PurseId(t.Escrow);
        foreach (ulong p in t.Entrants)
            if (t.Fee > 0 && w.PurseOf(new EntityId(p)) is { } to && w.Ledger.Balance(esc).Mites >= t.Fee) w.Ledger.Transfer(esc, to, new Coin(t.Fee), TransferReason.ContractRefund);
        long left = w.Ledger.Balance(esc).Mites;
        if (left > 0) w.Ledger.Transfer(esc, new PurseId(t.TreasuryPurse), new Coin(left), TransferReason.ContractRefund);
        t.Cancelled = true;
        foreach (var c in book.Contracts.Values.Where(x => x.Kind == "tournament_entry" && x.Param == t.Id && x.Live)) { c.State = WorkState.Cancelled; c.ClosedDay = day; }
    }

    /// <summary>
    /// A bout's result as a fight reports it (<paramref name="winner"/>): taken only for a bout that is scheduled, between the two people named, not yet decided, and only when the sim's own fight record
    /// names the same winner (<see cref="ContractWorld.BoutRecord"/>). A forged result, a result for a bout that is not in the bracket, or one the record does not bear out, changes nothing (CR-10).
    /// </summary>
    public static bool ReportBout(ContractWorld w, ContractBook book, int tid, int round, int slot, EntityId winner, int day)
    {
        if (!book.Tournaments.TryGetValue(tid, out var t) || !t.Drawn || t.Cancelled) return false;
        int i = t.Bouts.FindIndex(b => b.Round == round && b.Slot == slot); if (i < 0) return false;
        var bout = t.Bouts[i];
        if (bout.Winner != NoOne || bout.A == NoOne || bout.B == NoOne || winner.Value != bout.A && winner.Value != bout.B) return false;
        if (w.BoutRecord?.Invoke(new EntityId(bout.A), new EntityId(bout.B), t.FairDay) is not { } rec || rec.Value != winner.Value) return false;
        bout.Winner = winner.Value; t.Bouts[i] = bout;
        return true;
    }

    /// <summary>
    /// Plays the fair day: every bout of the round between two people the sim rates is fought by the rating draw (<c>P(a) = ra / (ra + rb)</c>, stream 0x692 keyed on the tournament and the round); a bout whose
    /// fighters have a fight record is read from the record; a bout still open when <paramref name="forfeit"/> is true (the day's end) goes to the fighter who is not the hero. When the final is decided the purse is paid.
    /// </summary>
    public static void Play(ContractWorld w, ContractBook book, int tid, int day, bool forfeit)
    {
        if (!book.Tournaments.TryGetValue(tid, out var t) || !t.Drawn || t.Cancelled || t.Paid) return;
        for (int guard = 0; guard < 8; guard++)
        {
            int round = t.Bouts.Max(b => b.Round);
            var cur = t.Bouts.Where(b => b.Round == round).OrderBy(b => b.Slot).ToList();
            for (int k = 0; k < t.Bouts.Count; k++)
            {
                var b = t.Bouts[k]; if (b.Round != round || b.Winner != NoOne) continue;
                if (w.BoutRecord?.Invoke(new EntityId(b.A), new EntityId(b.B), t.FairDay) is { } rec && (rec.Value == b.A || rec.Value == b.B)) { b.Winner = rec.Value; t.Bouts[k] = b; continue; }
                bool heroIn = new EntityId(b.A) == w.HeroId || new EntityId(b.B) == w.HeroId;
                if (heroIn && !forfeit) continue;                                     // the hero fights his own bouts; the sim waits for the record
                if (heroIn) { b.Winner = new EntityId(b.A) == w.HeroId ? b.B : b.A; t.Bouts[k] = b; continue; }
                int ra = Math.Max(1, w.RatingOf(new EntityId(b.A))), rb = Math.Max(1, w.RatingOf(new EntityId(b.B)));
                int pa = (int)((long)ra * 1000 / (ra + rb));
                b.Winner = DetRng.Range(w.Seed, round, (ulong)tid, RngStreams.TournamentBrackets, (uint)b.Slot, 0, 1000) < pa ? b.A : b.B;
                t.Bouts[k] = b;
            }
            cur = [.. t.Bouts.Where(b => b.Round == round).OrderBy(b => b.Slot)];
            if (cur.Any(b => b.Winner == NoOne)) return;
            if (cur.Count == 1) { Pay(w, book, t, cur[0].Winner, day); return; }
            for (int i = 0; i < cur.Count; i += 2) t.Bouts.Add(MakeBout(w, t, round + 1, i / 2, cur[i].Winner, cur[i + 1].Winner));
        }
    }

    /// <summary>The purse (what the escrow holds: the purse and the fees) to the winner of the final, as the bout record says; the entry contract is done.</summary>
    private static void Pay(ContractWorld w, ContractBook book, Tournament t, ulong winner, int day)
    {
        var final = t.Bouts.Where(b => b.Round == t.Bouts.Max(x => x.Round)).FirstOrDefault();
        if (final.Winner != winner || winner == NoOne) return;                          // paid only from the final's own record
        var esc = new PurseId(t.Escrow); long pot = w.Ledger.Balance(esc).Mites;
        t.Winner = winner;
        long paid = 0;
        if (w.PurseOf(new EntityId(winner)) is { } to && pot > 0 && w.Ledger.Transfer(esc, to, new Coin(pot), TransferReason.ContractPay)) paid = pot;
        else if (pot > 0) w.Ledger.Transfer(esc, new PurseId(t.TreasuryPurse), new Coin(pot), TransferReason.ContractRefund);
        t.Paid = true;
        var ps = new List<ChronParam> { ChronParam.Person("winner", new EntityId(winner)), ChronParam.Mites("purse", paid) };
        if (w.PlaceId(t.Settlement) != EntityId.None) ps.Add(ChronParam.Place("place", w.PlaceId(t.Settlement)));
        var e = new ChronicleEntry(w.Ids.Allocate(IdKind.Event), new GameTime(day * 86_400L + 18 * 3600), "tournament_won", [new EntityId(winner)], [new Cause(CauseKind.Other, null, NoteKey: "ch_note_tournament_final")],
            new Dictionary<string, string> { ["tournament"] = N(t.Id), ["settlement"] = w.PlaceName(t.Settlement), ["paid"] = N(paid), ["who"] = w.Name(new EntityId(winner)), ["hero"] = new EntityId(winner) == w.HeroId ? "1" : "0" }, TextKey: "ch_tournament_won", Params: ps);
        w.Chronicle.Add(e);
        foreach (var c in book.Contracts.Values.Where(x => x.Kind == "tournament_entry" && x.Param == t.Id && x.Live)) { c.State = WorkState.Done; c.ClosedDay = day; c.Taker = new EntityId(winner); c.Evidence.Add(e.Id.Value); }
    }

    /// <summary>The fair day's hours (H5 for the announcement, then the day itself): the draw at 8, the NPC bouts from 10, the day's end at 20.</summary>
    public static void OnHour(ContractWorld w, ContractBook book, long hour)
    {
        int day = (int)(hour / 24), hod = (int)(hour % 24);
        if (hod == 0) Announce(w, book, day);
        foreach (var t in book.Tournaments.Values.Where(x => !x.Paid && !x.Cancelled && x.FairDay == day).OrderBy(x => x.Id).ToList())
        {
            if (hod == 8) Draw(w, book, t.Id, day);
            else if (hod is >= 10 and < 20) Play(w, book, t.Id, day, forfeit: false);
            else if (hod == 20) Play(w, book, t.Id, day, forfeit: true);
        }
    }
}
