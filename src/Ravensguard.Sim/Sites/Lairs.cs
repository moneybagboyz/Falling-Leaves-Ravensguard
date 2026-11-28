using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Sites;

/// <summary>What a monster lair is (P8-LM-3, bestiary 3.3): the kind that lives there, the legend event of the history it is tied to, and how deep it goes.</summary>
public sealed record LairInfo(ulong SiteId, string Kind, EntityId Legend, string LegendKind, int Levels)
{
    /// <summary>The tier of the site: its number of levels (a tier-3 deep site has three).</summary>
    public int Tier => Levels;
}

/// <summary>
/// The lairs of a region as a later stage, after region generation (D8: the lair <i>sites</i> are Phase 1's, 1 to 3 a region, always two or three levels deep, so no region hash moves).
/// The kind that lives in each is chosen by the content's lair rules (<see cref="MonsterLairDef"/>: the first rule by rank whose site, ground, depth and legend fit and whose roll on
/// stream 0x830, keyed on the region and the site, passes), and each lair is tied to a legend event of the history: the event of the site it was placed by, else the nearest ruin or
/// battlefield, else any history site of the region, else a battle or a ruin of the world. Pure: the same region, content and seed give the same lairs, in site id order.
/// </summary>
public static class Lairs
{
    public static List<LairInfo> Resolve(Region region, SiteContent c, ulong seed, WorldHistory? history = null)
    {
        var rules = c.Monsters.Values.Where(m => m.Lair != null).OrderBy(m => m.Lair!.Rank).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
        var made = new Dictionary<string, int>(StringComparer.Ordinal);
        var list = new List<LairInfo>();
        ulong regionKey = ((ulong)(uint)region.CellX << 20) | (uint)region.CellY;
        foreach (var s in region.Sites.Where(x => x.MonsterLair).OrderBy(x => x.Id.Value))
        {
            var (legend, legendKind) = LegendOf(region, s, seed, history);
            string biome = region.Biomes[Math.Clamp(s.TileY, 0, Region.Size - 1) * Region.Size + Math.Clamp(s.TileX, 0, Region.Size - 1)].ToString();
            string site = s.Kind == SiteKind.Barrow ? "barrow" : "cave";
            string kind = "";
            for (int ri = 0; ri < rules.Count && kind.Length == 0; ri++)
            {
                var m = rules[ri]; var r = m.Lair!;
                if (r.Site != site || s.Levels < r.MinLevels) continue;
                if (r.Biomes.Count > 0 && !r.Biomes.Contains(biome)) continue;
                if (r.Legend != "any" && r.Legend != legendKind) continue;
                if (r.MaxPerRegion > 0 && made.GetValueOrDefault(m.Id) >= r.MaxPerRegion) continue;
                if (r.Pm < 1000 && DetRng.Range(seed, (long)regionKey, s.Id.Value, RngStreams.LairPlacement, (uint)ri, 0, 1000) >= r.Pm) continue;
                kind = m.Id;
            }
            if (kind.Length == 0) kind = s.LairKind.Length > 0 && c.Monsters.ContainsKey(s.LairKind) ? s.LairKind : site == "barrow" ? "draugr" : "cave_beast";
            made[kind] = made.GetValueOrDefault(kind) + 1;
            list.Add(new LairInfo(s.Id.Value, kind, legend, legendKind, s.Levels));
        }
        return list;
    }

    /// <summary>The legend a lair is tied to and what kind of place it is (ruin, battlefield, or "" for another): see the class summary.</summary>
    public static (EntityId Event, string Kind) LegendOf(Region region, Site s, ulong seed, WorldHistory? history)
    {
        Site? by = null;
        if (s.HistoryEvent != EntityId.None) by = region.Sites.FirstOrDefault(x => x.Id != s.Id && x.HistoryEvent == s.HistoryEvent && x.Kind is SiteKind.Ruin or SiteKind.Battlefield);
        if (by == null)
        {
            long bd = long.MaxValue;
            foreach (var l in region.Sites.Where(x => x.Kind is SiteKind.Ruin or SiteKind.Battlefield && x.HistoryEvent != EntityId.None).OrderBy(x => x.Id.Value))
            {
                long dx = l.X - s.X, dy = l.Y - s.Y, d = dx * dx + dy * dy;
                if (d < bd) { bd = d; by = l; }
            }
        }
        string kind(Site? x) => x == null ? "" : x.Kind == SiteKind.Ruin ? "ruin" : x.Kind == SiteKind.Battlefield ? "battlefield" : "";
        if (s.HistoryEvent != EntityId.None) return (s.HistoryEvent, kind(by));
        if (by != null) return (by.HistoryEvent, kind(by));
        // no ruin or battlefield: any place of the region that history made, the nearest
        long best = long.MaxValue; Site? other = null;
        foreach (var o in region.Sites.Where(x => x.Id != s.Id && !x.MonsterLair && x.HistoryEvent != EntityId.None).OrderBy(x => x.Id.Value))
        {
            long dx = o.X - s.X, dy = o.Y - s.Y, d = dx * dx + dy * dy;
            if (d < best) { best = d; other = o; }
        }
        if (other != null) return (other.HistoryEvent, "");
        // the world's own: a battle, else a fallen place
        if (history != null)
        {
            ulong k = DetRng.Next(seed, 0, s.Id.Value, RngStreams.LairPlacement, 77);
            if (history.Battlefields.Count > 0) return (history.Battlefields[(int)(k % (ulong)history.Battlefields.Count)].Event, "battlefield");
            var ruins = history.Settlements.Where(h => h.IsRuin && h.RuinEvent != EntityId.None).ToList();
            if (ruins.Count > 0) return (ruins[(int)(k % (ulong)ruins.Count)].RuinEvent, "ruin");
        }
        return (EntityId.None, "");
    }
}
