using Ravensguard.Sim.Combat.Aftermath;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Heists;

/// <summary>One man of a site's roster: his person, whether he is made up to fill the roster, his order (even: the day shift, odd: the night), and whether he lodges at the site and holds its keys (the first man).</summary>
public sealed record RosterMan(ulong Person, bool Synthetic, int Slot, bool Keeper)
{
    public bool DayShift => Slot % 2 == 0;
}

/// <summary>
/// The guards of a site (heists-and-jailbreaks 3, 4.2): the real guards of the place in id order, then jailers made up to fill the template's number; shifts by roster order as the watch keeps them (<see cref="Guards"/>: the day 06 to 18,
/// the night 18 to 06 with half the men, every fourth night man asleep after 02:00 in the guardroom); the keeper (the gaoler or the treasurer) lodges at the site, on watch by day and asleep through the night with the keys on his belt.
/// Patrol loops are 6 to 12 game minutes, the period drawn for each night (stream 0x6B0, key site and night).
/// </summary>
public static class GuardRoster
{
    public const int MaxMen = 12;

    /// <summary>The roster of a site: the place's real guards first (<paramref name="real"/>, person ids in order), made up to <paramref name="men"/> by synthetic jailers.</summary>
    public static List<RosterMan> Build(ulong siteId, IReadOnlyList<ulong> real, int men)
    {
        int n = Math.Clamp(Math.Max(men, 0), 0, MaxMen);
        var l = new List<RosterMan>();
        for (int k = 0; k < n; k++)
        {
            bool synth = k >= real.Count;
            ulong id = synth ? EntityId.Make(IdKind.Person, 0x7000_0000UL | ((siteId & 0xFFFFUL) << 8) | (ulong)(uint)k).Value : real[k];
            l.Add(new RosterMan(id, synth, k, k == 0));
        }
        return l;
    }

    /// <summary>The real guards of a settlement as person ids, in id order.</summary>
    public static List<ulong> RealOf(PersonTable pt, int settlement) => [.. Guards.Of(pt, settlement).Select(r => pt.IdOf(r).Value)];

    /// <summary>The men on watch at an hour of day (no sleepers, no keeper at night): the shift's men, minus the night men who sleep.</summary>
    public static List<RosterMan> OnWatch(IReadOnlyList<RosterMan> men, int hourOfDay, int sleepFromHour = 2)
    {
        var on = new List<RosterMan>(); bool day = Guards.IsDayHour(hourOfDay); int nk = 0;
        foreach (var m in men)
        {
            if (m.DayShift) { if (day) on.Add(m); continue; }
            int k = nk++;
            if (!day && !(hourOfDay >= sleepFromHour && hourOfDay < 6 && k % 4 == 3)) on.Add(m);
        }
        return on;
    }

    /// <summary>The night men who sleep after 02:00 (every fourth), in order.</summary>
    public static List<RosterMan> SleepingNightMen(IReadOnlyList<RosterMan> men, int hourOfDay, int sleepFromHour = 2)
    {
        var l = new List<RosterMan>(); int nk = 0;
        foreach (var m in men)
        {
            if (m.DayShift) continue;
            int k = nk++;
            if (hourOfDay >= sleepFromHour && hourOfDay < 6 && k % 4 == 3) l.Add(m);
        }
        return l;
    }

    /// <summary>Whether the keeper (a day man who lodges at the site) is asleep: from 22:00 to 06:00.</summary>
    public static bool KeeperAsleep(int hourOfDay) => hourOfDay >= 22 || hourOfDay < 6;

    /// <summary>The period of the loop on a night: 6 to 12 game minutes, drawn once per site and night (stream 0x6B0).</summary>
    public static int PeriodS(HeistRules r, ulong seed, ulong siteId, long night)
    {
        int span = Math.Max(1, r.S.LoopMaxS - r.S.LoopMinS + 1);
        return r.S.LoopMinS + DetRng.Range(seed, night, siteId, RngStreams.PatrolJitter, 0, 0, span);
    }

    /// <summary>The walk of one loop in steps: the distances between consecutive waypoints, closing the loop.</summary>
    public static int LoopSteps(HeistMap m)
    {
        var w = m.Waypoints; if (w.Count < 2) return 0;
        int total = 0;
        for (int i = 0; i < w.Count; i++)
        {
            var f = m.Field(w[(i + 1) % w.Count].X, w[(i + 1) % w.Count].Y);
            int d = f[m.Index(w[i].X, w[i].Y)]; total += Math.Max(0, d);
        }
        return total;
    }

    /// <summary>The dwell at each waypoint that makes the whole loop last about <paramref name="periodS"/>: <c>(period - walk) / waypoints</c>, never negative.</summary>
    public static int DwellS(HeistMap m, int periodS)
    {
        int n = m.Waypoints.Count; if (n < 2) return 0;
        return Math.Max(0, (periodS - LoopSteps(m)) / n);
    }

    /// <summary>The actual length of the loop in seconds with that dwell (walking one cell a second).</summary>
    public static int LoopSeconds(HeistMap m, int periodS) => LoopSteps(m) + DwellS(m, periodS) * m.Waypoints.Count;

    /// <summary>
    /// Lays the guards out for a run beginning at <paramref name="hourOfDay"/>: the men on watch fill the posts in order and walk the loop from waypoints offset by their order; the sleeping night men lie in the beds; the keeper stands his post by day
    /// and lies in a bed from 22:00. Camps have two guards of the band, one awake at night. Returns the guards, in roster order.
    /// </summary>
    public static List<HGuard> Layout(HeistRules r, ulong seed, HeistMap m, IReadOnlyList<RosterMan> men, int hourOfDay, long night, ulong siteId)
    {
        var res = new List<HGuard>();
        var posts = m.Posts; var beds = m.Beds; var wps = m.Waypoints;
        int period = PeriodS(r, seed, siteId, night), dwell = DwellS(m, period);
        var on = m.Kind == HeistKind.Camp ? [.. men.Take(2).Where((x, i) => !EscapeTable.Dark(hourOfDay * 3600L) || i == 0)] : OnWatch(men, hourOfDay, r.S.SleepFromHour);
        var sleepers = m.Kind == HeistKind.Camp ? [.. men.Take(2).Where((x, i) => EscapeTable.Dark(hourOfDay * 3600L) && i == 1)] : SleepingNightMen(men, hourOfDay, r.S.SleepFromHour);
        int bed = 0, post = 0, walker = 0;
        HGuard Make(RosterMan x) => new() { Person = x.Person, Synthetic = x.Synthetic, Slot = x.Slot, Keeper = x.Keeper };
        void Lay(HGuard g, (int X, int Y) at) { g.X = at.X; g.Y = at.Y; g.HomeX = at.X; g.HomeY = at.Y; }
        foreach (var x in men)
        {
            var g = Make(x);
            bool sleeper = sleepers.Any(s => s.Slot == x.Slot) || (x.Keeper && KeeperAsleep(hourOfDay) && m.Kind != HeistKind.Camp);
            bool watch = on.Any(s => s.Slot == x.Slot);
            if (sleeper && beds.Count > 0)
            {
                Lay(g, beds[bed++ % beds.Count]); g.State = GState.Asleep;
            }
            else if (watch || (x.Keeper && m.Kind != HeistKind.Camp))
            {
                if (post < posts.Count) { Lay(g, posts[post++]); g.State = GState.Post; }
                else if (wps.Count > 0)
                {
                    int k = walker++ % wps.Count; g.Route = k;
                    Lay(g, wps[k]); g.State = GState.Dwell; g.Timer = dwell * (walker % 3 + 1) / 3;
                }
                else if (posts.Count > 0) { Lay(g, posts[post++ % posts.Count]); g.State = GState.Post; }
                else continue;
            }
            else continue;                                                                 // off duty and away
            res.Add(g);
        }
        return res;
    }
}
