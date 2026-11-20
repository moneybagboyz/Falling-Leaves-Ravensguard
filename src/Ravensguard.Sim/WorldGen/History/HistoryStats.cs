using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.WorldGen.History;

/// <summary>
/// The calibration measures of history-recalibration 4.4, read off a finished <see cref="WorldHistory"/> (no generator state): used by the calibration tests and the world tool's <c>history-stats</c>.
/// Integer maths only; the shares are in permille.
/// </summary>
public sealed record HistoryStats(
    int Years, int StartRealms, int RealmsFell, int Wars, int NeighbourPairs, int WarShareMille, int PairsEverWarredMille, int WarsPerPairCenturyMille,
    int Plagues, int Ruins, int LandRegions, int RuinsPer1000Regions, int Saints, int Cultures, int Heresies, int Schisms, int Excommunications, int SlaveRaids, int Rebellions, int ExcommunicationsUsed = 0, int ExcommunicationsGiven = 0)
{
    /// <summary>Measures <paramref name="h"/> over <paramref name="c"/> (the continent it was made on).</summary>
    public static HistoryStats Of(Continent c, WorldHistory h)
    {
        int n = Continent.Size;
        // neighbouring pairs: 4-adjacent cells of two different realms, per snapshot (every 50 years and the end)
        var pairSets = new List<HashSet<long>>();
        foreach (var s in h.Snapshots)
        {
            var set = new HashSet<long>();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    byte a = s.Owner[y * n + x]; if (a == Continent.None) continue;
                    if (x + 1 < n) { byte b = s.Owner[y * n + x + 1]; if (b != Continent.None && b != a) set.Add(Math.Min(a, b) * 1000L + Math.Max(a, b)); }
                    if (y + 1 < n) { byte b = s.Owner[(y + 1) * n + x]; if (b != Continent.None && b != a) set.Add(Math.Min(a, b) * 1000L + Math.Max(a, b)); }
                }
            pairSets.Add(set);
        }
        int avgPairs = pairSets.Count == 0 ? 0 : (int)(pairSets.Sum(p => (long)p.Count) / pairSets.Count);
        // the share of the time neighbouring pairs are at war: war-seasons over pair-seasons (the pair count of the nearest earlier snapshot)
        long warSeasons = 0, pairSeasons = 0;
        int ticks = h.Years * 4;
        for (int t = 0; t < ticks; t++)
        {
            int si = 0;
            for (int k = 0; k < h.Snapshots.Count; k++) if (h.Snapshots[k].Year * 4 <= t) si = k;
            pairSeasons += pairSets.Count == 0 ? 0 : pairSets[si].Count;
        }
        foreach (var w in h.Wars) warSeasons += Math.Max(1, (w.EndTick < 0 ? ticks : w.EndTick) - w.StartTick);
        var ever = new HashSet<long>();
        foreach (var w in h.Wars) ever.Add(Math.Min(w.Attacker, w.Defender) * 1000L + Math.Max(w.Attacker, w.Defender));
        var allPairs = new HashSet<long>();
        foreach (var p in pairSets) allPairs.UnionWith(p);
        int everNeighbour = ever.Count(e => allPairs.Contains(e));
        int startRealms = c.Realms.Count;
        int fell = h.Realms.Take(startRealms).Count(r => !r.Alive);
        int plagues = h.Chronicle.All.Count(e => e.TypeId == "plague_begins");
        int ruins = h.Settlements.Count(s => s.IsRuin);
        // regions of 16 x 16 cells (32 km) that are at least half land
        int landRegions = 0;
        for (int ry = 0; ry < n / 16; ry++)
            for (int rx = 0; rx < n / 16; rx++)
            {
                int land = 0;
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++) if (!c.Ocean[(ry * 16 + y) * n + rx * 16 + x]) land++;
                if (land >= 128) landRegions++;
            }
        int saints = h.Notables.Count(x => x.Role == NotableRole.Saint);
        int cultures = Math.Max(1, c.Cultures.Count);
        int heresies = h.Heresies.Count, schisms = h.Religions.Count(r => r.SchismFrom >= 0);
        int rebellions = h.Chronicle.All.Count(e => e.TypeId is "rebellion" or "h_rebellion");
        int perCentury = avgPairs == 0 || h.Years == 0 ? 0 : (int)((long)h.Wars.Count * 100_000 / avgPairs / h.Years);
        return new HistoryStats(h.Years, startRealms, fell, h.Wars.Count, avgPairs, pairSeasons == 0 ? 0 : (int)(warSeasons * 1000 / pairSeasons),
            allPairs.Count == 0 ? 0 : everNeighbour * 1000 / allPairs.Count, perCentury, plagues, ruins, landRegions, landRegions == 0 ? 0 : ruins * 1000 / landRegions, saints, cultures,
            heresies, schisms, h.Excommunications.Count, h.SlaveRaids.Count, rebellions, h.Excommunications.Count(x => x.WarTick >= 0 && x.WarTick - x.Tick <= 40), h.Excommunications.Count(x => x.Given > 0));
    }

    public override string ToString() =>
        $"{Years}y realms {StartRealms} fell {RealmsFell} wars {Wars} nbrPairs {NeighbourPairs} warShare {WarShareMille / 10.0:F1}% everWarred {PairsEverWarredMille / 10.0:F0}% wars/pair/cent {WarsPerPairCenturyMille / 1000.0:F2} " +
        $"plagues {Plagues} ruins {Ruins} ruins/region {RuinsPer1000Regions / 1000.0:F1} saints/rel {Saints * 10 / Cultures / 10.0:F1} heresies {Heresies} schisms {Schisms} excomm {Excommunications} raids {SlaveRaids} rebellions {Rebellions}";
}
