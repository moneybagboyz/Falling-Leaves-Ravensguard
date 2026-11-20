using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People.Vital;

namespace Ravensguard.Sim.Disease;

/// <summary>
/// The great plague on the continent (disease-and-plague 4.2, 4.3), one function for the two clocks: <see cref="Starts"/>, <see cref="OriginOf"/> and <see cref="SpreadPpm"/> are pure (history v1 calls
/// them with its own year and its own settlements), and <see cref="Run"/> is the seasonal step of <see cref="ContinentTick"/> (S9): a start roll once a game year, spread along the road graph, the closed-form
/// deaths of the regions nobody watches, and the end. A detailed region is never infected by dice here: its own caravans carry it in (<see cref="DiseaseEngine"/>) and it reports itself.
/// </summary>
public static class ContinentPlagueStep
{
    /// <summary>Whether a plague starts in a continent-year (stream 0x6D2, key the continent: 0, tick the year), by the slider's chance (a permille x10 as <see cref="Difficulty.PlagueStartPermilleX10"/>: 100 is 1 percent).</summary>
    public static bool Starts(ulong seed, int year, PlagueSeverity severity)
    {
        int pm10 = new Difficulty { Plague = severity }.PlagueStartPermilleX10;
        return VitalMath.Roll(seed, year, 0, RngStreams.PlagueStart, 0, pm10 * 100);
    }

    /// <summary>The origin: a region weighted by its people (the draw is stream 0x6D2 draw 1), among the regions that have people and at least one road (so it can travel); -1 when there is none.</summary>
    public static int OriginOf(ulong seed, int year, IReadOnlyList<int> pop, Func<int, bool> hasRoad)
    {
        long total = 0;
        for (int r = 0; r < pop.Count; r++) if (pop[r] > 0 && hasRoad(r)) total += pop[r];
        if (total <= 0) return -1;
        long pick = (long)(DetRng.NextUInt(seed, year, 0, RngStreams.PlagueStart, 1) * (ulong)total >> 32);
        for (int r = 0; r < pop.Count; r++)
        {
            if (pop[r] <= 0 || !hasRoad(r)) continue;
            if (pick < pop[r]) return r;
            pick -= pop[r];
        }
        return -1;
    }

    /// <summary>The chance (ppm) one infected region infects the neighbour over a road in a season: 40 percent x (the edge's trade volume over the median edge's, clamped to 0.5 to 2).</summary>
    public static int SpreadPpm(long volume, long median)
    {
        long f = median <= 0 ? 1000 : Math.Clamp(volume * 1000 / median, 500, 2000);
        return (int)Math.Min(1_000_000, DiseaseRules.Current.S.ContinentSpreadPct * 10_000L * f / 1000);
    }

    /// <summary>The share of a closed-form region's people the plague takes, percent x100 (attack of a town-and-village mix times the case fatality, by the slider).</summary>
    public static int ClosedFormLossPctX100(PlagueSeverity severity)
    {
        var s = DiseaseRules.Current.S; long attack = (s.AttackTownPct + s.AttackVillagePct) / 2;
        long fatality = Math.Min(90, (long)s.FatalityPct * DiseaseRules.SeverityMultPct(severity) / 100);
        return (int)(attack * fatality);
    }

    /// <summary>The seasonal step. <paramref name="isResident"/> says which regions are detailed (their own records are the truth).</summary>
    public static void Run(ContinentState st, Func<int, bool> isResident, GameTime now, long season)
    {
        var cp = st.Plague; var sd = DiseaseRules.Current.S;
        cp.NowDay = (int)now.TotalDays;
        // the end: nobody is in it any more
        foreach (var r in cp.Infected.Where(kv => kv.Value.Until < season).Select(kv => kv.Key).ToList()) cp.Infected.Remove(r);
        foreach (var r in cp.Reports.Where(kv => cp.NowDay - kv.Value > 15).Select(kv => kv.Key).ToList()) cp.Reports.Remove(r);
        if (cp.Active && cp.Infected.Count == 0 && cp.Reports.Count == 0 && season > cp.StartSeason + 1) cp.Active = false;

        if (!cp.Active)
        {
            // a start, once a game year: the tick of its first season (spring), which runs once for a season
            if (now.Season != Season.Spring) return;
            int year = now.Year;
            if (!Starts(st.Seed, year, cp.Severity)) return;
            var pops = new int[ContinentState.Regions]; for (int r = 0; r < pops.Length; r++) pops[r] = st.Summaries[r].Pop;
            int origin = OriginOf(st.Seed, year, pops, r => st.EdgesOf[r].Length > 0);
            if (origin < 0) return;
            cp.Active = true; cp.Id++; cp.Origin = origin; cp.StartDay = cp.NowDay; cp.StartSeason = season;
            cp.NameIndex = (int)(DetRng.NextUInt(st.Seed, year, (ulong)cp.Id, RngStreams.PlagueStart, 2) % (uint)ContinentPlague.PlagueNames.Length);
            cp.DeadEstimate = 0;
            if (!isResident(origin)) Infect(st, cp, origin, season, sd, 0);          // a resident origin starts through its own gate check
            cp.PendingNews.Add((origin, 0));
            return;
        }

        // the spread: every infected region (closed form or reporting) over each of its roads, in region and edge order
        var volumes = st.Edges.Select(e => e.CapacityKgPerSeason).OrderBy(v => v).ToArray();
        long median = volumes.Length == 0 ? 0 : volumes[volumes.Length / 2];
        var sources = cp.Infected.Keys.Union(cp.Reports.Keys).OrderBy(r => r).ToList();
        foreach (int r in sources)
        {
            foreach (int ei in st.EdgesOf[r])
            {
                var e = st.Edges[ei]; if (e.Closed) continue;
                int o = e.Other(r);
                if (cp.IsInfected(o) || isResident(o) || st.Summaries[o].Pop <= 0 || cp.Infected.ContainsKey(o)) continue;
                if (!VitalMath.Roll(st.Seed, season, (ulong)ei, RngStreams.PlagueSpread, 0, SpreadPpm(e.CapacityKgPerSeason, median))) continue;
                Infect(st, cp, o, season, sd, ei);
            }
        }
    }

    private static void Infect(ContinentState st, ContinentPlague cp, int region, long season, Ravensguard.Content.DiseaseSettingsDef sd, int edge)
    {
        int len = DetRng.Range(st.Seed, season, (ulong)region, RngStreams.PlagueSpread, 2, sd.ContinentSeasonsMin, sd.ContinentSeasonsMax + 1);
        cp.Infected[region] = (season, season + len - 1);
        // the dead of a region nobody watches: its people fall by the plague's share, its dwellings empty (coin stays, so the audit holds)
        var s = st.Summaries[region];
        long lost = s.Pop * ClosedFormLossPctX100(cp.Severity) / 10_000;
        s.Pop = (int)Math.Max(0, s.Pop - lost); s.Households = Math.Max(s.Pop > 0 ? 1 : 0, (int)(s.Households * (long)s.Pop / Math.Max(1, s.Pop + lost)));
        s.Vacancies = Math.Max(0, s.Dwellings - s.Households);
        cp.DeadEstimate += (int)Math.Min(int.MaxValue / 2, lost);
        cp.PendingNews.Add((region, (int)Math.Min(int.MaxValue, lost)));
    }
}
