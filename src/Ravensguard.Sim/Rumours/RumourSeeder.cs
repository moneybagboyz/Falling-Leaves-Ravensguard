using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Rumours;

/// <summary>
/// Turning chronicle entries into rumours (dialogue spec 4.5): every new entry whose type has a rumour row and a populated place becomes a <see cref="Rumour"/> with its witnesses, each of whom
/// holds a hop-0 hearing (or, for word from afar, a hop-2 hearing told by a traveller). Witness picks are stream 0x312 keyed on the entry.
/// </summary>
public static class RumourSeeder
{
    public const int AdultAge = 12;

    /// <summary>Per settlement, the living people of working age, ascending index.</summary>
    public static int[][] Residents(TalkWorld w, TalkState ts, int day)
    {
        if (ts.Residents != null && ts.ResidentsDay == day && ts.ResidentsCount == w.People.Count) return ts.Residents;
        int n = Math.Max(1, w.SettlementNames.Count); var lists = new List<int>[n];
        for (int i = 0; i < n; i++) lists[i] = [];
        var p = w.People;
        for (int i = 0; i < p.Count; i++)
        {
            int s = p.HomeSettlement[i];
            if (s >= 0 && s < n && p.IsAlive(i) && p.AgeYears(i, day) >= AdultAge) lists[s].Add(i);
        }
        ts.Residents = [.. lists.Select(l => l.ToArray())]; ts.ResidentsDay = day; ts.ResidentsCount = p.Count;
        return ts.Residents;
    }

    public static RumourTypeDef? RowFor(LineBook book, ChronicleEntry e)
    {
        foreach (var row in book.RumourRowsOf(e.TypeId))
        {
            bool ok = true;
            foreach (var (k, v) in row.Match) if (!e.Data.TryGetValue(k, out var dv) || !string.Equals(dv, v, StringComparison.OrdinalIgnoreCase)) { ok = false; break; }
            if (ok) return row;
        }
        return null;
    }

    private static int Int(ChronicleEntry e, string key) => e.Data.TryGetValue(key, out var v) && int.TryParse(v, out int i) ? i : 0;

    /// <summary>The settlement an entry happened in: its data's settlement name, a settlement subject, or the home of a person or household subject; -1 when it cannot be placed.</summary>
    public static int PlaceOf(TalkWorld w, ChronicleEntry e)
    {
        if (e.Data.TryGetValue("settlement", out var name)) { int s = w.SettlementOf(name); if (s >= 0) return s; }
        foreach (var id in e.Subjects)
        {
            if (id.Kind == IdKind.Settlement) { int s = w.SettlementOf(id); if (s >= 0) return s; }
            else if (id.Kind == IdKind.Person) { int pi = w.People.IndexOf(id); if (pi >= 0 && w.People.HomeSettlement[pi] >= 0) return w.People.HomeSettlement[pi]; }
            else if (id.Kind == IdKind.Household && w.Households != null) { int hi = w.Households.IndexOf(id); if (hi >= 0 && w.Households.Settlement[hi] >= 0) return w.Households.Settlement[hi]; }
        }
        return -1;
    }

    /// <summary>Reads the chronicle entries added since the last call. Returns how many rumours were made.</summary>
    public static int Seed(TalkWorld w, TalkState ts, LineBook book, long hour)
    {
        int made = 0, day = (int)(hour / 24); var chron = w.Chronicle.All; var set = book.Settings;
        for (; ts.Cursor < chron.Count; ts.Cursor++)
        {
            var e = chron[ts.Cursor]; var row = RowFor(book, e);
            if (row == null || ts.Rumours.Get(e.Id) != null) continue;
            int entryDay = (int)e.When.TotalDays;
            if (entryDay + row.LifeDays < day) continue;                        // an old tale: no one is talking of it now
            bool afar = e.TypeId == "word_reached";
            int s = afar ? LargestSettlement(w, ts, day) : PlaceOf(w, e);
            if (s < 0 && row.Witnesses == "traveller") s = LargestSettlement(w, ts, day);
            if (s < 0) continue;
            var residents = Residents(w, ts, day)[s];
            if (residents.Length == 0) continue;
            var wit = Witnesses(w, e, row.Witnesses, s, residents);
            if (wit.Count == 0) continue;
            string Data(string k) => k.Length > 0 && e.Data.TryGetValue(k, out var v) ? v : "";
            var r = new Rumour
            {
                Id = e.Id, Source = e.Id, Kind = Enum.Parse<RumourKind>(row.Kind), TypeId = row.Id, Key = row.Key, Settlement = afar ? -1 : s,
                Place = afar ? Data("origin") : w.SettlementNames[s], Subjects = [.. e.Subjects.Take(2)], Severity = (byte)row.Severity, BornHour = (int)Math.Min(e.When.TotalHours, hour),
                ExpiresDay = entryDay + row.LifeDays, LifeDays = row.LifeDays, InterestPm = row.InterestPm, Number = Int(e, row.NumberKey), Who = Data(row.WhoKey), About = Data(row.AboutKey),
                Witnesses = [.. wit], FromAfar = afar,
            };
            if (row.NumberKey.Length == 0) r.FactsMask &= 0b0111;
            ts.Rumours.Add(r); made++;
            foreach (int p in wit)
                ts.Rumours.AddHearing(r.Id, new Hearing { Listener = p, Teller = -1, Hour = r.BornHour, Hop = (byte)(afar ? 2 : 0), How = afar ? HearHow.Traveller : HearHow.Witnessed, PlaceAlt = -1 }, set.HearingsPerPersonMax);
        }
        return made;
    }

    private static int LargestSettlement(TalkWorld w, TalkState ts, int day)
    {
        var res = Residents(w, ts, day); int best = -1, bn = 0;
        for (int i = 0; i < res.Length; i++) if (res[i].Length > bn) { bn = res[i].Length; best = i; }
        return best;
    }

    /// <summary>The people who saw it (spec 4.5): the household and four neighbours, the reeve or priest and three elders, five townsfolk, or the innkeepers and merchants who heard it from a traveller.</summary>
    public static List<int> Witnesses(TalkWorld w, ChronicleEntry e, string policy, int s, int[] residents)
    {
        var res = new List<int>(); var p = w.People; uint draw = 0;
        void Add(int i) { if (i >= 0 && !res.Contains(i)) res.Add(i); }
        void Random(int k)
        {
            for (int tries = 0; tries < k * 6 && res.Count < 64 && k > 0; tries++)
            {
                int i = residents[DetRng.Range(w.Seed, 0, e.Id.Value, RngStreams.RumourWitness, draw++, 0, residents.Length)];
                if (!res.Contains(i)) { res.Add(i); k--; }
            }
        }
        switch (policy)
        {
            case "seen":
                // P6-B2 (crime 4.1): the people who saw the act and knew who did it (the entry's own "seen" list of person ids): they and no one else know the deed at first
                if (e.Data.TryGetValue("seen", out var seen))
                    foreach (var part in seen.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        if (ulong.TryParse(part, out ulong v) && p.IndexOf(new EntityId(v)) is int si && si >= 0 && Array.IndexOf(residents, si) >= 0) Add(si);
                break;
            case "household":
                foreach (var id in e.Subjects)
                {
                    int hh = -1;
                    if (id.Kind == IdKind.Person) { int pi = p.IndexOf(id); if (pi >= 0) { hh = p.Household[pi]; Add(pi); } }
                    else if (id.Kind == IdKind.Household && w.Households != null) hh = w.Households.IndexOf(id);
                    if (hh >= 0) foreach (int i in residents) if (p.Household[i] == hh) Add(i);
                }
                Random(4); break;
            case "elders":
                foreach (int i in residents) if (p.Occupation[i] is Occupation.Reeve or Occupation.Priest or Occupation.Mayor) { Add(i); break; }
                foreach (int i in residents.OrderByDescending(i => p.AgeYears(i, (int)e.When.TotalDays)).ThenBy(i => i).Take(3)) Add(i);
                break;
            case "traveller":
                foreach (int i in residents) if (p.Occupation[i] is Occupation.Innkeeper or Occupation.Merchant) { Add(i); if (res.Count >= 3) break; }
                if (res.Count == 0) Random(2);
                break;
            default: Random(5); break;
        }
        return res;
    }
}
