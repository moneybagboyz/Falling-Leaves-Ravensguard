using System.Globalization;
using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Law;

namespace Ravensguard.Sim.Heists;

/// <summary>What the barrier made of a run's events: a text key and its arguments for the hero's result line, and whether the run is over.</summary>
public sealed record HeistReport(string Key, object[] Args, bool Ended, long Mites = 0);

/// <summary>
/// The heist meeting the rest of the world at the barrier (heists-and-jailbreaks 4.5, 4.6): the run moves no coin and writes no crime; its events do it here, through the ledger, the crime book, the gaol and the chronicle, each entry's cause
/// the one before (the bribe refused, the crime, the manhunt, the bounty). A bribe taken is a transfer (reason Bribe); a chest emptied moves real coin from the fief treasury to the hero's purse (reason Plunder) and its value is the burglary's;
/// a cell opened and left unseen frees its captives quietly and the crime is found at the next round (no witness, no bounty, a rumour); seen, each person recognised has a jailbreak on his head and the fief's guards begin a manhunt.
/// </summary>
public static class HeistOutcome
{
    private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static ChronParam Place(LawWorld w, int s) => ChronParam.Place("place", w.PlaceId(s));
    private static IEnumerable<ChronParam> PlaceOpt(LawWorld w, int s) => w.PlaceId(s) != EntityId.None ? [Place(w, s)] : [];

    /// <summary>
    /// Handles the run's pending events in order. Returns the last report worth showing (a bribe taken or refused, a chest emptied, the end of the run). <paramref name="sec"/> is the world second the barrier applies it at.
    /// </summary>
    public static HeistReport Apply(LawWorld w, CrimeBook cb, JusticeBook jb, HeistBook hb, HeistRun run, long sec)
    {
        var report = new HeistReport("", [], false);
        var hero = w.HeroId;
        foreach (var e in run.TakeEvents())
        {
            switch (e.Kind)
            {
                case HeistEventKind.Bribed: report = Bribed(w, hb, run, e, hero, sec); break;
                case HeistEventKind.BribeRefused: report = BribeRefused(w, cb, run, e, hero, sec); break;
                case HeistEventKind.Looted: report = Looted(w, run, e, hero, sec); break;
                case HeistEventKind.Left: report = Left(w, cb, jb, hb, run, hero, sec); break;
                case HeistEventKind.Caught: report = Caught(w, cb, jb, hb, run, e, hero, sec); break;
            }
        }
        return report;
    }

    private static HSite? SiteOf(HeistRun run) => run.Sites.TryGetValue(run.Cur, out var s) ? s : null;

    private static (int Settlement, int Fief, int Seat) Where(LawWorld w, HeistRun run)
    {
        var s = SiteOf(run); int st = s?.Settlement ?? 0; int fief = w.FiefOf(st);
        return (st, fief, Gaol.SeatOf(w, fief, st));
    }

    // ------------------------------------------------------------------ bribes

    private static HeistReport Bribed(LawWorld w, HeistBook hb, HeistRun run, HeistEvent e, EntityId hero, long sec)
    {
        var guard = new EntityId(e.A); var (st, fief, seat) = Where(w, run);
        var from = w.PurseOf(hero);
        PurseId? to = w.PurseOf(guard) ?? w.TreasuryOf?.Invoke(st);                                    // a jailer made up for the roster is paid into the fief's own purse
        if (w.Ledger != null && from != null && to != null && from != to) w.Ledger.Transfer(from.Value, to.Value, Coin.FromMites(e.Value), TransferReason.Bribe);
        var ps = new List<ChronParam> { ChronParam.Person("official", guard), ChronParam.Person("doer", hero), ChronParam.Mites("amount", e.Value) }; ps.AddRange(PlaceOpt(w, st));
        Gaol.Log(w, sec, "bribe_taken", [guard, hero], 0, "ch_justice_bribe_taken", ps);
        _ = fief; _ = seat;
        return new HeistReport("hj_bribe_taken", [Ravensguard.Sim.Trade.Quotes.Money(e.Value)], false, e.Value);
    }

    private static HeistReport BribeRefused(LawWorld w, CrimeBook cb, HeistRun run, HeistEvent e, EntityId hero, long sec)
    {
        var guard = new EntityId(e.A); var (st, _, _) = Where(w, run);
        var rec = CrimeSystem.Commit(w, cb, new ActSpec("bribery", hero, guard, st, sec, e.Value, 0, "ch_note_crime_hero"), [new WitnessRow(guard.Value, WitnessRow.KnewIt)], ActorLook.None);
        var ps = new List<ChronParam> { ChronParam.Person("official", guard), ChronParam.Person("doer", hero) }; ps.AddRange(PlaceOpt(w, st));
        Gaol.Log(w, sec, "bribe_refused", [guard, hero], rec.Witnessed, "ch_justice_bribe_refused", ps);
        return new HeistReport("hj_bribe_refused", [], false);
    }

    // ------------------------------------------------------------------ the chest

    /// <summary>What a man can carry out of a chest: <see cref="Content.HeistSettingsDef.ChestSharePct"/> of what the fief's treasury holds, never more than the carry cap.</summary>
    public static long ChestTake(HeistRules r, long treasuryMites) => Math.Min(r.S.ChestCarryMites, Math.Max(0, treasuryMites) * r.S.ChestSharePct / 100);

    private static HeistReport Looted(LawWorld w, HeistRun run, HeistEvent e, EntityId hero, long sec)
    {
        int st = (int)e.Value; long taken = 0;
        var from = w.TreasuryOf?.Invoke(st); var to = w.PurseOf(hero);
        if (w.Ledger != null && from != null && to != null)
        {
            taken = ChestTake(run.R, w.Ledger.Balance(from.Value).Mites);
            if (taken > 0 && !w.Ledger.Transfer(from.Value, to.Value, Coin.FromMites(taken), AftermathCoin.Plunder)) taken = 0;
        }
        run.Stolen += taken;
        return new HeistReport("hj_looted", [Ravensguard.Sim.Trade.Quotes.Money(taken), e.B], false, taken);
    }

    // ------------------------------------------------------------------ the way out

    private static List<WitnessRow> Witnesses(HeistRun run, bool allKnow) => [.. run.Seen.Select(x => new WitnessRow(x.Guard, allKnow || x.Knew ? WitnessRow.KnewIt : (byte)0))];

    private static HeistReport Left(LawWorld w, CrimeBook cb, JusticeBook jb, HeistBook hb, HeistRun run, EntityId hero, long sec)
    {
        var (st, fief, seat) = Where(w, run);
        bool seen = run.SeenByAny;
        var freed = new List<EntityId>(); ulong gaoler = 0; var crimes = new List<CrimeRecord>();
        foreach (ulong p in run.Followers)
        {
            var who = new EntityId(p);
            if (jb.Gaol.TryGetValue(p, out var row))
            {
                gaoler = row.Gaoler;
                foreach (int id in row.Crimes) if (cb.Get(id) is { State: CrimeState.Held } c) c.State = CrimeState.Fled;
                foreach (var sn in jb.Sentences.Values) if (sn.Person == p && sn.State == SentenceState.Pending) { sn.State = SentenceState.Avoided; if (cb.Get(sn.Crime) is { } sc) sc.State = CrimeState.Fled; }
                jb.Gaol.Remove(p);
            }
            if (w.Captives?.Rescued(who, sec) == true) freed.Add(who);
        }
        var victim = gaoler != 0 ? new EntityId(gaoler) : w.LordOf(fief);
        if (freed.Count > 0)
        {
            if (!seen)
            {
                // nobody saw it: the empty cell is found at the next round (a rumour, no bounty)
                var rec = CrimeSystem.Commit(w, cb, new ActSpec("jailbreak", hero, victim, seat, sec, 0, 0, "ch_note_crime_hero"), [], ActorLook.None);
                crimes.Add(rec);
            }
            else
            {
                foreach (var f in freed)
                    crimes.Add(CrimeSystem.Commit(w, cb, new ActSpec("jailbreak", f, victim, seat, sec, 0, 0, "ch_note_crime_escape"), Witnesses(run, true), ActorLook.None));
                crimes.Add(CrimeSystem.Commit(w, cb, new ActSpec("jailbreak", hero, victim, seat, sec, 0, 0, "ch_note_crime_hero"), Witnesses(run, false), ActorLook.None));
            }
        }
        if (run.Stolen > 0)
            crimes.Add(CrimeSystem.Commit(w, cb, new ActSpec("burglary", hero, w.LordOf(fief), st, sec, run.Stolen, 0, "ch_note_crime_hero"), seen ? Witnesses(run, false) : [], ActorLook.None));
        HeistLink.Closed(hb, run, sec);
        if (seen && crimes.Count > 0 && crimes.Any(c => c.Witnesses.Count > 0))
        {
            var first = crimes.First(c => c.Witnesses.Count > 0);
            var mh = HeistLink.BeginManhunt(w, hb, seat, sec, [hero.Value, .. freed.Select(f => f.Value)], first.Witnessed);
            return new HeistReport("hj_left_seen", [freed.Count, mh.Id], true);
        }
        return new HeistReport(freed.Count > 0 ? "hj_left_unseen_freed" : crimes.Count > 0 ? "hj_left_unseen_stolen" : "hj_left_nothing", [freed.Count], true);
    }

    private static HeistReport Caught(LawWorld w, CrimeBook cb, JusticeBook jb, HeistBook hb, HeistRun run, HeistEvent e, EntityId hero, long sec)
    {
        var (st, fief, seat) = Where(w, run);
        var guardId = new EntityId(e.A); var crimes = new List<CrimeRecord>();
        var rows = Witnesses(run, true); if (rows.All(r => r.Person != e.A)) rows.Add(new WitnessRow(e.A, WitnessRow.KnewIt));
        var victim = run.Sites.TryGetValue(run.Cur, out var s) && s.Guards.FirstOrDefault(g => g.Keeper) is { Synthetic: false } k ? new EntityId(k.Person) : w.LordOf(fief);
        if (run.Followers.Count > 0) crimes.Add(CrimeSystem.Commit(w, cb, new ActSpec("jailbreak", hero, victim, seat, sec, 0, 0, "ch_note_crime_hero"), rows, ActorLook.None));
        if (run.Stolen > 0) crimes.Add(CrimeSystem.Commit(w, cb, new ActSpec("burglary", hero, w.LordOf(fief), st, sec, run.Stolen, 0, "ch_note_crime_hero"), rows, ActorLook.None));
        foreach (var c in crimes) { c.Perpetrator = (long)hero.Value; c.State = CrimeState.Seen; }
        HeistLink.Closed(hb, run, sec);
        if (crimes.Count == 0) return new HeistReport("hj_caught_thrown_out", [], true);
        Gaol.Arrest(w, cb, jb, hero, seat, sec, inAct: true, guardId);
        return new HeistReport("hj_caught", [crimes.Count], true);
    }
}
