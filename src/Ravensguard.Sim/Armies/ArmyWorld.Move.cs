using Ravensguard.Sim.Caravans;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Politics;

namespace Ravensguard.Sim.Armies;

public sealed partial class ArmyWorld
{
    /// <summary>The hour a warband's lord thinks on a day (one lord an hour by fief id, inside the hours the men ride).</summary>
    public int SliceHour(Warband wb) => Rules.DayStartHour + wb.Fief % Math.Max(1, Rules.DayEndHour - Rules.DayStartHour);

    private bool Daylight(int hod) => hod >= Rules.DayStartHour && hod < Rules.DayEndHour;

    private static bool Stationary(WarbandTask t) => t is WarbandTask.Idle or WarbandTask.Garrison;

    // ------------------------------------------------------------------ the hour

    /// <summary>
    /// One hour of the standing warbands (H1, after the caravans): every warband in id order rides, dwells, searches, raids or thinks; then the lords' errands move. Warbands travel in the hours of the day only (05:00 to
    /// 21:00) and sleep where they stand. The cost is the walk of the warbands and one road path from the cache for each leg.
    /// </summary>
    public void Hour(ArmyEnv e)
    {
        if (Rule == null) return;
        bool day = Daylight(e.HourOfDay);
        foreach (var wb in Book.Warbands.Values.ToList())
        {
            if (e.HourOfDay == Rules.DayStartHour) PruneDead(wb);
            if (e.DayOfSeason >= Rules.WagonSeasonDay && e.HourOfDay == SliceHour(wb) && wb.Here >= 0) MakeWagon(e, wb);
            if (day && wb.Members.Count > 0) Step(e, wb);
        }
        ErrandsHour(e, day);
    }

    private void Step(ArmyEnv e, Warband wb)
    {
        if (wb.Dwell > 0) { wb.Dwell--; return; }
        if (wb.Here < 0) { Advance(e, wb); return; }
        if (wb.Task == WarbandTask.ChaseBandits && wb.Phase == 0 && wb.Here == wb.Target) { Search(e, wb); return; }
        if (wb.Task == WarbandTask.Raid && wb.Phase == 0 && wb.Here == wb.Target) { DoRaid(e, wb); wb.Phase = 1; wb.Dwell = Rules.RaidDwellHours; return; }
        if (wb.Stops.Count > 0) { Depart(e, wb); return; }
        bool finished = !Stationary(wb.Task);
        if (finished) { wb.Task = WarbandTask.Idle; wb.Target = -1; wb.TargetId = 0; wb.Phase = 0; wb.Cause = "ch_note_am_done"; wb.Escorting = 0; }
        if (wb.Here == wb.Seat) Unload(e, wb, force: false);
        int since = (e.HourOfDay - SliceHour(wb) + 24) % 24;
        bool think = finished || (wb.Task == WarbandTask.Idle ? since % 6 == 0 : since == 0 && wb.ThoughtDay != (int)e.Day);     // a resting warband hears the news every six hours, a garrison thinks daily
        if (think) Think(e, wb);
        if (wb.Stops.Count > 0 && wb.Dwell == 0) Depart(e, wb);
    }

    private void Depart(ArmyEnv e, Warband wb)
    {
        int next = wb.Stops[0];
        if (next == wb.Here) { wb.Stops.RemoveAt(0); OnArrive(e, wb); return; }
        if (PathOf(wb.Here, next) == null) { wb.Stops.RemoveAt(0); return; }
        wb.From = wb.Here; wb.To = next; wb.Here = -1; wb.DistMm = 0; wb.Stops.RemoveAt(0);
        if (wb.Escorting != 0 && Book.Errands.TryGetValue(wb.Escorting, out var er) && er.State == ErrandState.Waiting && er.Escort == wb.Id) er.State = ErrandState.Moving;
        Advance(e, wb);
    }

    private void Advance(ArmyEnv e, Warband wb)
    {
        var p = PathOf(wb.From, wb.To);
        if (p == null) { wb.Here = wb.To >= 0 ? wb.To : wb.Seat; wb.From = wb.To = -1; wb.DistMm = 0; return; }
        int speed = wb.Mounted ? Rules.RideMmPerGs : Rules.FootMmPerGs;
        Errand? er = null;
        if (wb.Escorting != 0 && Book.Errands.TryGetValue(wb.Escorting, out er) && er.State == ErrandState.Moving && er.Escort == wb.Id) speed = Math.Min(speed, Rules.WagonMmPerGs); else er = null;
        wb.DistMm += (long)speed * p.SpeedPermille(Math.Min(wb.DistMm, p.LengthMm)) / 1000 * 3600;
        if (er != null) er.DistMm = Math.Min(wb.DistMm, p.LengthMm);
        if (wb.DistMm < p.LengthMm) return;
        wb.Here = wb.To; wb.From = wb.To = -1; wb.DistMm = 0;
        OnArrive(e, wb);
    }

    private void OnArrive(ArmyEnv e, Warband wb)
    {
        int s = wb.Here, day = (int)e.Day;
        if ((uint)s < (uint)Region.Settlements.Count && Region.Settlements[s].FiefIndex == wb.Fief) wb.Visited[s] = day;
        switch (wb.Task)
        {
            case WarbandTask.Patrol: wb.Dwell = Rules.PatrolDwellHours; break;
            case WarbandTask.Visit when s == wb.Target:
            {
                wb.Dwell = Rules.VisitDwellHours; wb.Visits[wb.TargetId] = day;
                if (Politics is { } pb && Rule is { } rb) Opinions.Add(pb, rb, wb.TargetId, wb.Lord, "visit", day, 0, Rules.VisitOpinion);
                break;
            }
            case WarbandTask.Escort when wb.Escorting != 0 && Book.Errands.TryGetValue(wb.Escorting, out var er) && s == er.To:
                er.State = ErrandState.Done; er.DistMm = 0; wb.Escorting = 0; wb.Dwell = 2; break;
            case WarbandTask.ChaseBandits when s == wb.Target: wb.Dwell = Rules.SearchHours; break;
            case WarbandTask.Raid when s == wb.Target: wb.Dwell = Rules.RaidDwellHours; break;
        }
        if (s == wb.Seat) Unload(e, wb, force: false);
    }

    // ------------------------------------------------------------------ the errands move

    private void ErrandsHour(ArmyEnv e, bool daylight)
    {
        int day = (int)e.Day;
        foreach (var er in Book.Errands.Values.ToList())
        {
            if (er.State == ErrandState.Done) { if (day - er.DayMade > 20) Book.Errands.Remove(er.Id); continue; }
            if (er.State == ErrandState.Waiting && er.Escort == 0 && day - er.DayMade >= Rules.WagonWaitDays && daylight) er.State = ErrandState.Moving;      // no escort came: it goes alone
            if (er.State != ErrandState.Moving || er.Escort != 0 || !daylight) continue;
            var p = PathOf(er.From, er.To); if (p == null) { er.State = ErrandState.Done; continue; }
            er.DistMm += (long)Rules.WagonMmPerGs * p.SpeedPermille(Math.Min(er.DistMm, p.LengthMm)) / 1000 * 3600;
            if (er.DistMm >= p.LengthMm) { er.State = ErrandState.Done; er.DistMm = 0; }
        }
    }

    // ------------------------------------------------------------------ what a raid brought home

    /// <summary>At the seat the lord's men put down what they carried: the baggage goes into the lord's household store (a real move; what the store cannot take stays carried).</summary>
    public void Unload(ArmyEnv e, Warband wb, bool force)
    {
        if (wb.LootG <= 0 && !force) return;
        var bag = BaggageOf(wb.Id); if (bag.RowCount == 0) { wb.LootG = 0; return; }
        int hh = HouseholdOf(wb.Lord); if (hh < 0 || hh >= W.Economy.HouseholdCount) return;
        var to = W.Economy.HouseholdStore[hh];
        foreach (var (g, q) in bag.Rows()) Store.MoveUpTo(bag, to, g, q);
        wb.LootG = bag.TotalMassG();
    }
}
