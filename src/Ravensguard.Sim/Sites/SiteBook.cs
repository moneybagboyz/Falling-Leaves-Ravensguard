using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Sites;

/// <summary>
/// One site's state (exploration-and-sites 3.2 <c>SiteState</c>): the generated snapshot (kept as first visited) and the live map with play's changes, the day it was cleared,
/// how often it was reoccupied, the share of loot the abstract scavengers left before anyone came, a flood running, the curse of a robbed barrow, the auto-map.
/// </summary>
public sealed class SiteRecord
{
    public ulong Site;
    public byte[] Snapshot = [];
    public SiteMap? Base;
    public SiteMap? Map;
    public AutoMap? Known;
    public int Cleared = -1;
    public int Reoccupied;
    /// <summary>Permille of the history's loot still there (abstract scavengers take 5% a year from a few unvisited ruins).</summary>
    public int LootPm = 1000;
    public long FloodSince = -1;
    /// <summary>A barrow: the grave taboo was broken. A battlefield: the legend's named weapon has been found (<see cref="SiteSurface.Search"/>).</summary>
    public bool Curse;
    public int Visits;
    public bool Generated => Map != null;
}

/// <summary>
/// The sites of the world (exploration-and-sites 3.2, 4.3, 5): a record per site that history or play has touched, the crawl in progress, the weather of the last day (for floods),
/// the renown stub of slain monsters. The abstract tier (<see cref="SiteSystem"/>) reoccupies cleared sites, floods mines after storms and lets scavengers pick ruins; it never
/// touches the site the hero is in. A lock (<see cref="Sync"/>) keeps the tick worker and the main thread apart. Saved as the optional "sites" section.
/// </summary>
public sealed class SiteBook
{
    public object Sync { get; } = new();
    public SortedDictionary<ulong, SiteRecord> Records { get; } = [];
    /// <summary>The site the hero is in (0: none) and the crawl's saved state between a load and the crawl being rebuilt.</summary>
    public ulong ActiveSite;
    public Save.CrawlSave? ActiveCrawl;
    /// <summary>The last 24 game hours: bit h set when heavy rain or a storm was falling (the flood rule).</summary>
    public uint HeavyHours;
    public long LastHour = -1;
    public bool LegendsSeeded;
    public int Renown;
    public List<string> Slain { get; } = [];
    /// <summary>Chronicle entries the abstract tier made (type, game second, data), drained into the chronicle by the system's outbox.</summary>
    public bool IsEmpty { get { lock (Sync) return Records.Count == 0 && ActiveSite == 0 && Renown == 0 && Slain.Count == 0 && !LegendsSeeded && HeavyHours == 0; } }

    public SiteRecord Record(ulong site)
    {
        lock (Sync)
        {
            if (!Records.TryGetValue(site, out var r)) Records[site] = r = new SiteRecord { Site = site };
            return r;
        }
    }

    /// <summary>The site's map, generated on the first visit (4.1) and kept: later visits get the same map with its changes. The abstract history (scavengers) is applied once.</summary>
    public SiteRecord Open(SiteContent c, SiteHistoryInfo h, ulong worldSeed)
    {
        var r = Record(h.SiteId);
        lock (Sync)
        {
            if (r.Map != null) return r;
            var map = SiteGen.Generate(c, h, worldSeed);
            Adopt(r, map);
            return r;
        }
    }

    /// <summary>Makes a freshly generated map the site's: the snapshot is taken first, then the abstract history before the visit (scavenged loot, a flood) goes on as play would.</summary>
    public void Adopt(SiteRecord r, SiteMap map)
    {
        lock (Sync)
        {
            r.Snapshot = SiteCodec.Encode(map);
            r.Base = SiteCodec.Decode(r.Snapshot);
            r.Map = map;
            r.Known ??= new AutoMap(map.W, map.H, map.Levels);
            if (r.LootPm < 1000) Scavenge(map, 1000 - r.LootPm);
        }
    }

    /// <summary>Takes <paramref name="lossPm"/> of the loot value out of the map, from the last containers first (the scavengers took it).</summary>
    public static int Scavenge(SiteMap m, int lossPm)
    {
        int total = m.LootLeftMites, take = (int)((long)total * lossPm / 1000), taken = 0;
        for (int i = m.Containers.Count - 1; i >= 0 && taken < take; i--)
        {
            var items = m.Containers[i].Items;
            for (int k = items.Count - 1; k >= 0 && taken < take; k--) { taken += items[k].ValueMites; items.RemoveAt(k); }
        }
        return taken;
    }

    /// <summary>The <c>SiteHash</c> of the daily trail: every generated site's map and the book's own counters.</summary>
    public ulong Hash()
    {
        lock (Sync)
        {
            ulong h = 14695981039346656037UL;
            static ulong M(ulong h, long v) { unchecked { h ^= (ulong)v; h *= 1099511628211UL; h ^= h >> 29; return h; } }
            foreach (var (id, r) in Records)
            {
                h = M(h, (long)id); h = M(h, r.Cleared); h = M(h, r.Reoccupied); h = M(h, r.LootPm); h = M(h, r.FloodSince); h = M(h, r.Curse ? 1 : 0);
                if (r.Map != null) h = r.Map.Hash(h);
            }
            h = M(h, (long)ActiveSite); h = M(h, HeavyHours); h = M(h, Renown);
            return h;
        }
    }

    // ------------------------------------------------------------------ the abstract tier (5: daily and yearly, only sites nobody is in)

    /// <summary>Notes the hour's weather (heavy rain or a storm) for the flood rule.</summary>
    public void NoteHour(long hour, bool heavy)
    {
        lock (Sync)
        {
            if (LastHour >= 0 && hour > LastHour) HeavyHours = hour - LastHour >= 32 ? 0 : HeavyHours << (int)(hour - LastHour);
            if (heavy) HeavyHours |= 1;
            LastHour = hour;
        }
    }

    public int HeavyOfLastDay { get { lock (Sync) return System.Numerics.BitOperations.PopCount(HeavyHours & 0xFF_FFFF); } }

    /// <summary>
    /// Flood events (4.5.2, EX-8): when heavy rain or a storm lasted 4 or more of the last 24 hours, each active or abandoned mine rolls 4% a day (stream 0x4B3, key the day's first
    /// hour and the site) for water rising 30 cm an hour for 6 hours on its lowest level. Returns the mines that flooded.
    /// </summary>
    public static bool FloodRolls(ulong seed, int day, ulong siteId, int heavyHours, Ravensguard.Content.SiteConstantsDef k)
        => heavyHours >= k.FloodRainHours && DetRng.Range(seed, (long)day * 24, siteId, RngStreams.HazardEvent, 0x4F00, 0, 1000) < k.FloodPm;

    /// <summary>
    /// Reoccupation (4.3, EX-15): a cleared site with nobody living in it rolls once a game day after 20 days: <c>2% x pressure / 500</c> (stream 0x4B8 key day and site).
    /// </summary>
    public static bool ReoccupyRolls(ulong seed, int day, ulong siteId, int pressure, Ravensguard.Content.SiteConstantsDef k)
        => DetRng.Range(seed, day, siteId, RngStreams.Reoccupation, 0, 0, 1000 * k.ReoccupyPressure) < (long)k.ReoccupyPmPerDay * pressure;

    /// <summary>Moves a new band (3-8 persons, new ids and names) into a cleared site's hall; the chronicle notes it.</summary>
    public static List<Inhabitant> NewBand(SiteContent c, SiteMap m, ulong seed, int day, int serialBase, int culture)
    {
        var r = new RngStream(seed, day, m.SiteId, RngStreams.Reoccupation, 1);
        int n = 3 + r.Range(0, 6);
        var hall = m.Rooms.FirstOrDefault(p => p.Purposes.Contains("guard") || p.Purposes.Contains("rest")) ?? m.Rooms[Math.Min(1, m.Rooms.Count - 1)];
        var band = new List<Inhabitant>();
        for (int i = 0; i < n; i++)
        {
            int cell = -1;
            for (int k = 0; k < 30 && cell < 0; k++)
            {
                int x = hall.X + 1 + r.Range(0, Math.Max(1, hall.W - 2)), y = hall.Y + 1 + r.Range(0, Math.Max(1, hall.H - 2));
                int cc = m.Index(hall.Level, x, y);
                if (m.Tiles[cc] == SiteTile.Floor) cell = cc;
            }
            if (cell < 0) cell = m.Arrival[hall.Level];
            var (l, cx, cy) = m.At(cell);
            int skill = 22 + r.Range(0, 24);
            band.Add(new Inhabitant
            {
                Id = SiteGen.FolkId(m.SiteId, serialBase + i), Role = "bandits", Kit = "bandit_light", Name = SiteNames.Person(c, culture, r.Range(0, 6) == 0, ref r), Home = cell,
                X = cx * 1000 + 500, Y = cy * 1000 + 500, Level = l, State = (i & 1) == 0 ? InhabitantState.Home : InhabitantState.Asleep, Skill = skill, Hostile = true,
            });
        }
        return band;
    }
}
