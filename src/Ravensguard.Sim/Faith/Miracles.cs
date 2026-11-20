using Ravensguard.Content;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Faith;

/// <summary>
/// Believed miracles (overview 10a, P6-E2): very rare events the folk read as miracles, each with its natural explanation recorded beside it and never a supernatural mechanic. Two kinds: a pilgrim's recovery after a
/// shrine (a natural recovery watched for 20 days: the wound closes as wounds do, the faith event came first) and a fire that stopped at a church (B5's <c>FireBook.Stops</c> carry the natural reason: a stone wall, the rain,
/// a change of wind). The chance that the folk call it a miracle is <see cref="FaithSettingsDef.MiraclePct"/> (stream 0x655); it may be disputed by sceptics (draw 3). A miracle raises the priest's piety and is told in the chronicle
/// with the natural explanation as its cause note.
/// </summary>
public static class Miracles
{
    /// <summary>The days a recovering pilgrim is watched for.</summary>
    public const int WatchDays = 20;

    /// <summary>A pilgrim who arrives hurt is watched for a natural recovery (the hero's pain or an open wound at the shrine).</summary>
    public static void WatchPilgrim(WorldState st, ContentDatabase db, EntityId pilgrim, int settlement, EntityId arrival, int day)
    {
        var p = FaithOps.Of(st, db); if (p == null) return;
        if (st.Wounds is not { } wl || !wl.Books.TryGetValue(pilgrim.Value, out var wb) || (wb.PainX100 <= 0 && wb.OpenCount == 0)) return;
        p.Pol.Cures.RemoveAll(c => c.Person == pilgrim.Value);
        p.Pol.Cures.Add(new CureWatch { Person = pilgrim.Value, Day = day, Settlement = settlement, Entry = arrival.Value });
    }

    /// <summary>The day's passes: watched pilgrims who have recovered, and fires that stopped at a church since the last look. Returns the miracles told.</summary>
    public static int Daily(WorldState st, ContentDatabase db, int day)
    {
        var p = FaithOps.Of(st, db); if (p == null) return 0;
        int told = 0;
        foreach (var c in p.Pol.Cures.ToList())
        {
            if (day < c.Day + p.S.ShrinePainDays) continue;
            bool healed = st.Wounds is { } wl && wl.Books.TryGetValue(c.Person, out var wb) && wb.PainX100 <= 0 && wb.OpenCount == 0;
            if (healed || day >= c.Day + WatchDays)
            {
                p.Pol.Cures.Remove(c);
                if (healed && Roll(st, p, c.Person, day, 1)) { Tell(st, p, "cure", "miracle_cure", "ch_faith_miracle_cure", "ch_note_miracle_natural_recovery", new EntityId(c.Person), true, c.Settlement, new EntityId(c.Entry), day); told++; }
            }
        }
        var stops = st.Fire?.Stops; if (stops != null)
        {
            for (int i = p.Pol.FireStopsSeen; i < stops.Count; i++)
            {
                var stop = stops[i]; if (!stop.Sacred) continue;
                if (!Roll(st, p, stop.Fire ^ (ulong)(uint)stop.Structure, day, 0)) continue;
                string note = stop.Reason switch { "stone_wall" => "ch_note_miracle_stone_wall", "rain" => "ch_note_miracle_rain", _ => "ch_note_miracle_wind" };
                int s = HeroSettlement(st, p);
                Tell(st, p, "fire_stopped", "miracle_fire", "ch_faith_miracle_fire", note, EntityId.None, false, s, new EntityId(stop.Fire), day); told++;
            }
            p.Pol.FireStopsSeen = stops.Count;
        }
        return told;
    }

    private static int HeroSettlement(WorldState st, FaithOps.Pass p)
    {
        int row = st.Party == null ? -1 : FaithOps.RowOf(st, st.Party.Hero); return row >= 0 ? p.People.HomeSettlement[row] : -1;
    }

    private static bool Roll(WorldState st, FaithOps.Pass p, ulong key, int day, int draw) => DetRng.Range(st.Seed, day, key, RngStreams.BelievedMiracles, (uint)draw, 0, 100) < p.S.MiraclePct;

    private static void Tell(WorldState st, FaithOps.Pass p, string kind, string type, string key, string natural, EntityId person, bool hasPerson, int settlement, EntityId cause, int day)
    {
        var ps = new List<ChronParam> { ChronParam.Place("place", FaithOps.PlaceOf(st, settlement)) };
        var subjects = new List<EntityId> { FaithOps.PlaceOf(st, settlement) };
        if (hasPerson) { ps.Add(ChronParam.Person("doer", person)); subjects.Add(person); }
        var e = FaithOps.Chron(st, FaithOps.SecOf(day), type, key, ps, subjects, cause, natural);
        bool disputed = DetRng.Range(st.Seed, day, e.Value, RngStreams.BelievedMiracles, 3, 0, 100) < p.S.MiracleDisputePct;
        p.Pol.Miracles.Add(new MiracleRow { Day = day, Settlement = settlement, Kind = kind, Natural = natural, Entry = e.Value, Person = person.Value, Disputed = disputed });
        var priest = p.Book.ParishAt(settlement)?.Priest ?? -1;
        if (priest >= 0) Piety.Adjust(p.Book, p.People, new EntityId((ulong)priest), p.S.MiraclePiety);
        if (hasPerson) Piety.Adjust(p.Book, p.People, person, p.S.MiraclePiety);
        foreach (var rr in p.Pol.Relics.Values) if (rr.Holder == 0 && rr.At == settlement) rr.Fame++;
        if (disputed) FaithOps.Chron(st, FaithOps.SecOf(day), "miracle_disputed", "ch_faith_miracle_disputed", [ChronParam.Place("place", FaithOps.PlaceOf(st, settlement))], [FaithOps.PlaceOf(st, settlement)], e, "ch_note_miracle_dispute");
    }
}
