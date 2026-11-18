using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Rumours;

/// <summary>
/// Rumours passing from person to person (dialogue spec 4.6). Each game hour, in each settlement that has someone holding a rumour, a share of the adults (by the hour of the day) are out among
/// people: with their household in the early and late hours, at their workplace by day, at the market or the tavern table in between and in the evening. They sit in groups of 2 to 6 (a group
/// is a household, a workplace or a venue, split in ascending person order); each teller tells the best rumour they hold (interest times freshness) once an hour, and each other member who does not
/// hold it hears it with <c>600 permille x familiarity factor x freshness</c>. A retelling may degrade a detail (5%: vague, exaggerated or, in 1%, misattributed), as flags only. The same
/// function serves the abstract hour and a spoken exchange in the live tier.
/// </summary>
public static class RumourSpreader
{
    private static int Freshness(Rumour r, long hour) => (int)Math.Clamp(1000 - 1000L * (hour - r.BornHour) / 24 / Math.Max(1, r.LifeDays), 0, 1000);

    /// <summary>The rumour a person would tell: the held one with the highest interest times freshness among those heard before this hour.</summary>
    public static (Rumour? R, Hearing H) BestToTell(RumourTable t, int person, long hour, Func<EntityId, bool>? skip = null)
    {
        Rumour? best = null; Hearing bh = default; long bs = 0;
        foreach (var id in t.HeldBy(person))
        {
            var r = t.Get(id)!; var h = t.Find(id, person)!.Value;
            if (h.Hour >= hour || h.Dropped || (skip != null && skip(id))) continue;
            long score = (long)r.InterestPm * Freshness(r, hour);
            if (score > bs || (score == bs && best != null && id.Value < best.Id.Value)) { best = r; bh = h; bs = score; }
        }
        return (bs > 0 ? best : null, bh);
    }

    /// <summary>The distortion of one retelling (stream 0x314, key listener at the hour): one roll of 1000; below the misattribute share it is misattributed, below the distortion share vague or exaggerated.</summary>
    public static (Distortion Flag, short PlaceAlt) Distort(TalkWorld w, TalkSettingsDef s, Rumour r, int listener, long hour)
    {
        int roll = DetRng.Range(w.Seed, hour, (ulong)listener, RngStreams.RumourDistort, 0, 0, 1000);
        if (roll >= s.DistortPm) return (Distortion.None, -1);
        if (roll < s.MisattributePm && w.SettlementNames.Count > 1 && r.Settlement >= 0)
        {
            int n = w.SettlementNames.Count, alt = (r.Settlement + 1 + DetRng.Range(w.Seed, hour, (ulong)listener, RngStreams.RumourDistort, 2, 0, n - 1)) % n;
            return (Distortion.Misattributed, (short)alt);
        }
        bool exag = DetRng.Range(w.Seed, hour, (ulong)listener, RngStreams.RumourDistort, 1, 0, 2) == 0 && r.Number > 0;
        return (exag ? Distortion.Exaggerated : Distortion.Vague, -1);
    }

    /// <summary>
    /// Who talks together this hour, in groups of 2 to 6 (ascending person index inside, groups ascending by kind then unit): a household at home in the early and late hours, people at one
    /// workplace by day, and individuals out at the market, the well, the tavern or the church. Every draw is stream 0x315 keyed on the settlement, the hour and the unit.
    /// </summary>
    public static List<List<int>> Groups(TalkWorld w, TalkSettingsDef s, int settlement, int[] residents, long hour)
    {
        int hod = (int)(hour % 24); var p = w.People; ulong sid = (ulong)settlement; var groups = new List<List<int>>();
        void Chunk(List<int> members)
        {
            for (int k = 0; k < members.Count; k += s.GroupMax)
            {
                var part = members.GetRange(k, Math.Min(s.GroupMax, members.Count - k));
                if (part.Count >= s.GroupMin) groups.Add(part);
            }
        }
        bool Unit(int kind, int unit, int pm) => pm > 0 && DetRng.Range(w.Seed, hour, sid, RngStreams.GatherPartition, (uint)((kind << 28) | (unit & 0xFFFFFFF)), 0, 1000) < pm;
        bool away(int i) => w.Away?.Invoke(i) == true;
        if (hod is >= 5 and < 8 or >= 19 and < 23)
        {
            var byHome = new SortedDictionary<int, List<int>>();
            foreach (int i in residents) if (!away(i)) (byHome.TryGetValue(p.Household[i], out var l) ? l : byHome[p.Household[i]] = []).Add(i);
            foreach (var (hh, members) in byHome) if (Unit(1, hh, s.HomePm)) Chunk(members);
        }
        else if (hod is >= 8 and < 18)
        {
            var byWork = new SortedDictionary<int, List<int>>();
            foreach (int i in residents) if (!away(i)) { int key = p.WorkSettlement[i] == settlement && p.WorkLot[i] >= 0 ? p.WorkLot[i] : 1000 + p.Household[i] % 8; (byWork.TryGetValue(key, out var l) ? l : byWork[key] = []).Add(i); }
            foreach (var (unit, members) in byWork) if (Unit(2, unit, s.WorkPm)) Chunk(members);
        }
        int pm = hod < s.SocialPmByHour.Count ? s.SocialPmByHour[hod] : 0;
        if (pm > 0)
        {
            int venues = Math.Max(1, residents.Length * pm / 1000 / 4); var out_ = new List<(int Venue, int Person)>();
            foreach (int i in residents)
                if (!away(i) && DetRng.Range(w.Seed, hour, sid, RngStreams.GatherPartition, (uint)((3 << 28) | (i & 0xFFFFFFF)), 0, 1000) < pm)
                    out_.Add((DetRng.Range(w.Seed, hour, sid, RngStreams.GatherPartition, (uint)((4 << 28) | (i & 0xFFFFFFF)), 0, venues), i));
            out_.Sort((a, b) => a.Venue != b.Venue ? a.Venue.CompareTo(b.Venue) : a.Person.CompareTo(b.Person));
            int cur = int.MinValue; var members = new List<int>();
            foreach (var (v, i) in out_) { if (v != cur) { Chunk(members); members = []; cur = v; } members.Add(i); }
            Chunk(members);
        }
        return groups;
    }

    /// <summary>One game hour of spreading for the whole home region. Returns how many hearings were made.</summary>
    public static int Hour(TalkWorld w, TalkState ts, LineBook book, long hour)
    {
        var s = book.Settings; int made = 0, day = (int)(hour / 24);
        // the settlements where anyone holds something tellable
        var active = new SortedSet<int>(); var p = w.People;
        foreach (var (person, held) in ts.Rumours.Held) if (held.Count > 0 && person < p.Count && p.HomeSettlement[person] >= 0) active.Add(p.HomeSettlement[person]);
        var residents = RumourSeeder.Residents(w, ts, day);
        var t = ts.Rumours;
        foreach (int st in active)
        {
            if (st >= residents.Length) continue;
            foreach (var g in Groups(w, s, st, residents[st], hour))
            {
                int venue = g[0];
                foreach (int teller in g)
                {
                    var (r, th) = BestToTell(t, teller, hour);
                    if (r == null) continue;
                    int fresh = Freshness(r, hour);
                    foreach (int listener in g)
                    {
                        if (listener == teller || t.Holds(r.Id, listener)) continue;
                        bool close = p.Household[listener] == p.Household[teller] || ts.Relations.TierOf(teller, listener, false) >= RelationTier.Friend && ts.Relations.TierOf(teller, listener) < RelationTier.Disliked;
                        int pm = (int)((long)s.HearPm * (close ? s.FriendFactorPm : 1000) / 1000 * fresh / 1000);
                        if (DetRng.Range(w.Seed, hour, (ulong)teller, RngStreams.RumourHear, (uint)listener, 0, 1000) >= pm) continue;
                        var (flag, alt) = Distort(w, s, r, listener, hour);
                        var h = new Hearing { Listener = listener, Teller = teller, Hour = (int)hour, Hop = (byte)Math.Min(250, th.Hop + 1), Flags = th.Flags | flag, PlaceAlt = alt >= 0 ? alt : th.PlaceAlt, How = HearHow.Told };
                        if (t.AddHearing(r.Id, h, s.HearingsPerPersonMax))
                        {
                            made++; ts.Passes.Add(new Pass((int)hour, st, teller, listener, r.Id, venue));
                            ts.Relations.Met(teller, listener, day, s);
                        }
                    }
                }
            }
        }
        // keep the log of passes short: the last two hours are all an overhearing needs
        ts.Passes.RemoveAll(x => x.Hour < hour - 1);
        if (ts.Passes.Count > s.PassLogMax) ts.Passes.RemoveRange(0, ts.Passes.Count - s.PassLogMax);
        return made;
    }
}
