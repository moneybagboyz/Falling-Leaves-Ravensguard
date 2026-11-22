using Ravensguard.Sim.Core;
using Ravensguard.Sim.Politics;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Armies;

/// <summary>One thing a warband could do now, with what it scores, where it goes, the stops of its way and the text key of why (armies-on-the-map 4.1).</summary>
public sealed record WarbandOption(WarbandTask Task, int Score, int Target, ulong TargetId, string Cause, int[] Stops);

public sealed partial class ArmyWorld
{
    // ------------------------------------------------------------------ what a warband could do

    /// <summary>The options of a warband standing at <paramref name="at"/>, from what the lord knows: the news he has heard (never the bands' own places), the errands at his seat, the war and his friends. Pure: no draw.</summary>
    public List<WarbandOption> Options(ArmyEnv e, Warband wb, int at)
    {
        var res = new List<WarbandOption>(); var rb = Rule; if (rb == null) return res;
        int day = (int)e.Day, seat = wb.Seat; bool winter = e.Winter;
        var pb = Politics; var war = wb.Realm >= 0 ? pb?.WarOf(wb.Realm) : null;
        // a raid reported near the seat: hold it
        if (war != null)
            foreach (var n in Book.Reports)
                if (n.Realm >= 0 && n.Realm != wb.Realm && war.Involves(n.Realm) && day - n.Day <= Rules.GarrisonDays && (uint)n.Place < (uint)Region.Settlements.Count && DistM(seat, n.Place) <= Rules.GarrisonReachM)
                { res.Add(new WarbandOption(WarbandTask.Garrison, Rules.ScoreGarrison, seat, 0, "ch_note_am_raid_near", at == seat ? [] : [seat])); break; }
        // robbery news within a day's ride, not too old, of a band not too strong for the men
        WarbandOption? chase = null;
        foreach (var n in Book.Reports)
        {
            if (n.Band <= 0 || !Book.Bands.ContainsKey(n.Band) || day - n.Day > Rules.ChaseMaxAgeDays || (uint)n.Place >= (uint)Region.Settlements.Count) continue;
            if (DistM(at, n.Place) > Rules.ChaseReachM || (long)n.Strength * 1000 >= (long)Rules.ChaseMaxRatioPm * Math.Max(1, wb.Members.Count)) continue;
            if (PathOf(at, n.Place) == null && at != n.Place) continue;
            int score = Rules.ScoreChase - (day - n.Day) * 10;
            if (chase == null || score > chase.Score || (score == chase.Score && (ulong)n.Band < chase.TargetId))
                chase = new WarbandOption(WarbandTask.ChaseBandits, score, n.Place, (ulong)n.Band, "ch_note_am_robbery_news", at == n.Place ? [] : [n.Place, seat]);
        }
        if (chase != null) res.Add(chase);
        // a wagon waiting at the seat
        foreach (var er in Book.Errands.Values)
            if (er.Fief == wb.Fief && er.State == ErrandState.Waiting && er.Escort == 0 && at == er.From && DistM(er.From, er.To) <= Rules.EscortMaxM && PathOf(er.From, er.To) != null)
            { res.Add(new WarbandOption(WarbandTask.Escort, Rules.ScoreEscort, er.To, (ulong)er.Id, er.Kind == ErrandKind.TaxWagon ? "ch_note_am_wagon" : "ch_note_am_errand", [er.To, seat])); break; }
        if (!winter)
        {
            var raid = RaidOption(e, wb, at, war); if (raid != null) res.Add(raid);
            var visit = VisitOption(e, wb, at, rb); if (visit != null) res.Add(visit);
            var patrol = PatrolOption(e, wb, at); if (patrol != null) res.Add(patrol);
        }
        else res.Add(new WarbandOption(WarbandTask.Garrison, 100, seat, 0, "ch_note_am_winter", at == seat ? [] : [seat]));
        return res;
    }

    private WarbandOption? PatrolOption(ArmyEnv e, Warband wb, int at)
    {
        int day = (int)e.Day; var due = new List<(int S, int Over)>();
        foreach (int s in Region.Fiefs[wb.Fief].Settlements)
        {
            int since = day - wb.Visited.GetValueOrDefault(s, day - 1000);
            int need = Region.Settlements[s].Class == SettlementClass.Farmstead ? Rules.PatrolFarmDueDays : Rules.PatrolDueDays;
            if (since >= need && (s == at || PathOf(at, s) != null)) due.Add((s, since - need));
        }
        if (due.Count == 0) return null;
        var stops = new List<int>(); int cur = at;
        while (due.Count > 0 && stops.Count < Rules.PatrolMaxStops)
        {
            int best = 0;
            for (int i = 1; i < due.Count; i++) if (DistM(cur, due[i].S) < DistM(cur, due[best].S)) best = i;       // the nearest next (ties: the lowest index, as the list is ascending)
            cur = due[best].S; stops.Add(cur); due.RemoveAt(best);
        }
        if (stops.Count == 0 || (stops[^1] != wb.Seat)) stops.Add(wb.Seat);
        int over = 0; foreach (int s in stops) over = Math.Max(over, day - wb.Visited.GetValueOrDefault(s, day - 1000) - Rules.PatrolDueDays);
        return new WarbandOption(WarbandTask.Patrol, Rules.ScorePatrol + 10 * Math.Min(30, Math.Max(0, over)), stops[0], 0, "ch_note_am_patrol_due", [.. stops]);
    }

    private WarbandOption? VisitOption(ArmyEnv e, Warband wb, int at, RuleBook rb)
    {
        int day = (int)e.Day; WarbandOption? best = null; int bestOp = int.MinValue;
        foreach (var f in rb.Fiefs.Values)
        {
            if (f.Fief == wb.Fief || f.Holder < 0 || f.Forfeited || (uint)f.Fief >= (uint)Region.Fiefs.Count) continue;
            ulong his = (ulong)f.Holder; if (IsHero(his) || RowOf(his) < 0 || !People.IsAlive(RowOf(his))) continue;
            int op = Opinions.Value(rb, wb.Lord, his);
            if (op < Rules.VisitMinOpinion || day - wb.Visits.GetValueOrDefault(his, day - 1000) < Rules.VisitGapDays) continue;
            int hisSeat = Region.Fiefs[f.Fief].SeatSettlement; if ((uint)hisSeat >= (uint)Region.Settlements.Count || DistM(wb.Seat, hisSeat) > Rules.RaidReachM) continue;
            if (PathOf(at, hisSeat) == null) continue;
            if (op > bestOp || (op == bestOp && best != null && his < best.TargetId)) { bestOp = op; best = new WarbandOption(WarbandTask.Visit, Rules.ScoreVisit + op, hisSeat, his, "ch_note_am_friend", [hisSeat, wb.Seat]); }
        }
        return best;
    }

    private WarbandOption? RaidOption(ArmyEnv e, Warband wb, int at, War? war)
    {
        if (war == null) return null;
        var rb = Rule!; int enemy = war.Attacker == wb.Realm ? war.Defender : war.Attacker; int day = (int)e.Day;
        var cands = new List<(int S, int Hh)>(); var ht = Households;
        foreach (var f in rb.Fiefs.Values)
        {
            if (f.Realm != enemy || (uint)f.Fief >= (uint)Region.Fiefs.Count) continue;
            foreach (int s in Region.Fiefs[f.Fief].Settlements)
            {
                if (Region.Settlements[s].Class == SettlementClass.Town || DistM(wb.Seat, s) > Rules.RaidReachM || PathOf(at, s) == null) continue;
                int hh = 0; for (int h = 0; h < ht.Count; h++) if (ht.Settlement[h] == s && ht.MemberCount[h] > 0) hh++;
                if (hh > 0) cands.Add((s, hh));
            }
        }
        if (cands.Count == 0) return null;
        int top = cands.Max(c => c.Hh); var tied = cands.Where(c => c.Hh == top).OrderBy(c => c.S).ToList();
        int pick = tied.Count == 1 ? 0 : (int)(DetRng.Next(W.Seed, day, (ulong)wb.Id, RngStreams.WarbandTies, 1) % (ulong)tied.Count);
        int target = tied[pick].S;
        return new WarbandOption(WarbandTask.Raid, Rules.ScoreRaid + Math.Min(100, top), target, 0, "ch_note_am_war_raid", [target, wb.Seat]);
    }

    // ------------------------------------------------------------------ choosing

    /// <summary>
    /// The lord's thinking (4.1): the best option by score (ties by stream 0x7A2 keyed warband and day, then by task); the task in hand is kept unless another scores <c>switch_margin</c> more. Writes the log line with its cause.
    /// Returns the task the warband now holds.
    /// </summary>
    public WarbandTask Think(ArmyEnv e, Warband wb)
    {
        int day = (int)e.Day; wb.ThoughtDay = day;
        int at = wb.Here; if (at < 0) return wb.Task;
        var opts = Options(e, wb, at);
        WarbandOption? best = null; ulong bestTie = 0; int i = 0;
        foreach (var o in opts)
        {
            ulong tie = DetRng.Next(W.Seed, day, (ulong)wb.Id, RngStreams.WarbandTies, (uint)(0x100 + i++));
            if (best == null || o.Score > best.Score || (o.Score == best.Score && (tie < bestTie || (tie == bestTie && o.Task < best.Task)))) { best = o; bestTie = tie; }
        }
        int cur = opts.FirstOrDefault(o => o.Task == wb.Task && (o.Task != WarbandTask.ChaseBandits || o.TargetId == wb.TargetId))?.Score ?? 0;
        bool stationary = wb.Task is WarbandTask.Idle or WarbandTask.Garrison;
        if (best == null || best.Score <= 0) { if (wb.Task != WarbandTask.Idle) SetTask(e, wb, new WarbandOption(WarbandTask.Idle, 0, -1, 0, "ch_note_am_rest", at == wb.Seat ? [] : [wb.Seat])); return wb.Task; }
        if (stationary && best.Task == wb.Task) return wb.Task;
        if (stationary && wb.Task != WarbandTask.Idle && best.Score < cur + Rules.SwitchMargin) return wb.Task;
        SetTask(e, wb, best);
        return wb.Task;
    }

    private void SetTask(ArmyEnv e, Warband wb, WarbandOption o)
    {
        wb.Task = o.Task; wb.Target = o.Target; wb.TargetId = o.TargetId; wb.TaskDay = (int)e.Day; wb.Cause = o.Cause; wb.Phase = 0; wb.Dwell = 0;
        wb.Stops.Clear(); wb.Stops.AddRange(o.Stops);
        if (o.Task == WarbandTask.Escort && Book.Errands.TryGetValue((int)o.TargetId, out var er)) { er.Escort = wb.Id; wb.Escorting = er.Id; }
        Book.Note((int)e.Day, wb.Id, o.Task, o.Target, o.Cause);
    }

    // ------------------------------------------------------------------ the lords' errands

    /// <summary>
    /// The season's tax wagon (4.1): on its day of the season the lord of a fief that is not its realm's seat sends the dues of the season to the realm's seat in the region, a wagon that waits for its escort at the lord's seat.
    /// It carries no coin or goods of its own: the dues it stands for move in the tax and the rule's books. Returns whether a wagon was made.
    /// </summary>
    public bool MakeWagon(ArmyEnv e, Warband wb)
    {
        if (wb.Realm < 0 || Book.Errands.Values.Any(x => x.Fief == wb.Fief && (x.State != ErrandState.Done || x.DayMade / TimeConst.DaysPerSeason == (int)e.Day / TimeConst.DaysPerSeason))) return false;      // one wagon a lord a season
        var seats = Ravensguard.Sim.Production.SeatBuildings.Seats(Region);
        if (!seats.TryGetValue(wb.Realm, out int to) || to == wb.Seat || PathOf(wb.Seat, to) == null) return false;
        var er = new Errand { Id = Book.NextErrand++, Kind = ErrandKind.TaxWagon, Fief = wb.Fief, From = wb.Seat, To = to, DayMade = (int)e.Day, State = ErrandState.Waiting };
        Book.Errands[er.Id] = er;
        return true;
    }
}
