using Ravensguard.Sim.Building;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Crafting;

/// <summary>A container in the world (a chest, a barrel, a shelf's store): where it stands, its store, the tools in it, and whether the crafter may use it (land 4.1: "store goods in a building").</summary>
public sealed record ReachContainer(EntityId Id, string Name, int X, int Y, int Z, Store Store, IReadOnlyList<string> Tools, bool MayUse = true);

/// <summary>A station the crafter can use, as the screen lists it: which, how many, the best tier, how far the nearest is (mm).</summary>
public sealed record ReachStation(string Station, string Name, int Count, ItemQuality Tier, long DistMm, bool InRoom);

/// <summary>What <see cref="CraftReach.Scan"/> found: the site (qualities, stations with tiers, light, water), the stores in their use order, the stations and containers for the screen.</summary>
public sealed class ReachScan
{
    public CraftSite Site = new();
    public CraftSources Sources = [];
    public List<ReachStation> Stations = [];
    public List<(ReachContainer C, long DistMm, bool Fetch)> Containers = [];
    public string RoomName = "";
    public int Structure;
}

/// <summary>
/// Crafting from the world (crafting-full 4.1, P5-E; decided with the user): the stations, tools and containers in the crafter's room or within 6 m count where they stand; a container elsewhere in the
/// same building (the same structure, RoomDetector v2) that the crafter may use is a fetched source: its materials are brought as part of the job, at walking pace and the carry limit, but its tools
/// do not count until they are carried. Stations come from the room's workstation pieces (RoomDetector v2 4.4) and any workstation piece within 6 m on the same level; each station's tier is the best of
/// its pieces (the piece's built skill or installed item, passed in), Common when unknown. A lit fire within 2 m is a <c>campfire</c> station and lights 4 m. Pure: a function of its inputs.
/// </summary>
public static class CraftReach
{
    /// <summary>Integer square root (Newton's method on integers: no floating point in the sim).</summary>
    private static long Isqrt(long v)
    {
        if (v <= 0) return 0;
        long r = v, next = (r + 1) / 2;
        while (next < r) { r = next; next = (r + v / r) / 2; }
        return r;
    }

    /// <summary>Distance in whole millimetres between two cells (1 m grid), integer square root.</summary>
    public static long Mm(int x0, int y0, int x1, int y1) { long dx = (x1 - x0) * 1000L, dy = (y1 - y0) * 1000L; return Isqrt(dx * dx + dy * dy); }

    public static ReachScan Scan(RoomWorld world, RecipeDb db, int x, int y, int z, Store? pack, IEnumerable<ReachContainer> containers, int light = 255, bool water = false,
        Func<int, int, int, ItemQuality>? tierAt = null, FireBook? fires = null, long reachMm = 6000)
    {
        var scan = new ReachScan(); var site = scan.Site; site.Light = light; site.Water = water;
        var room = world.RoomAt(x, y, z);
        scan.RoomName = room?.Primary?.Name ?? ""; site.RoomName = scan.RoomName;
        scan.Structure = world.StructureAt(x, y, z);
        int cells = (int)(reachMm / 1000);
        // stations: the room's workstation pieces, and any within reach on this level
        var found = new SortedDictionary<string, (int Count, ItemQuality Tier, long Dist, bool InRoom)>(StringComparer.Ordinal);
        void See(int cx, int cy, bool inRoom)
        {
            var f = world.FixtureAt(cx, cy, z); if (f?.Station is not { } st || !db.Stations.ContainsKey(st)) return;
            var tier = tierAt?.Invoke(cx, cy, z) ?? ItemQuality.Common; long d = Mm(x, y, cx, cy);
            (int Count, ItemQuality Tier, long Dist, bool InRoom) had = found.TryGetValue(st, out var h) ? h : (0, ItemQuality.Crude, long.MaxValue, false);
            found[st] = (had.Count + 1, had.Count == 0 || tier > had.Tier ? tier : had.Tier, Math.Min(had.Dist, d), had.InRoom || inRoom);
        }
        var seen = new HashSet<(int, int)>();
        if (room != null) foreach (var (cx, cy) in room.Cells) if (seen.Add((cx, cy))) See(cx, cy, true);
        for (int cy = y - cells; cy <= y + cells; cy++)
            for (int cx = x - cells; cx <= x + cells; cx++)
                if (Mm(x, y, cx, cy) <= reachMm && seen.Add((cx, cy))) See(cx, cy, false);
        foreach (var (st, v) in found)
        {
            site.AddStation(db, st, v.Tier);
            scan.Stations.Add(new ReachStation(st, db.Stations[st].Name, v.Count, v.Tier, v.Dist, v.InRoom));
        }
        long hx = x * 1000L, hy = y * 1000L;
        if (fires != null)
        {
            if (fires.StationNear(hx, hy) && db.Stations.ContainsKey("campfire")) { site.AddStation(db, "campfire"); scan.Stations.Add(new ReachStation("campfire", db.Stations["campfire"].Name, 1, ItemQuality.Common, 0, false)); }
            site.Light = Math.Max(site.Light, fires.LightAt(hx, hy));
        }
        // stores: the pack, then the containers in reach by distance (then id), then the ones to fetch
        if (pack != null) scan.Sources.Add(new CraftSource { Store = pack, Name = "pack" });
        var near = new List<(ReachContainer, long)>(); var far = new List<(ReachContainer, long)>();
        foreach (var c in containers)
        {
            if (!c.MayUse) continue;
            long d = c.Z == z ? Mm(x, y, c.X, c.Y) : long.MaxValue;
            bool inRoom = room != null && c.Z == z && room.Contains(c.X, c.Y);
            if (inRoom || d <= reachMm) near.Add((c, inRoom ? Math.Min(d, reachMm) : d));
            else if (scan.Structure != 0 && world.StructureAt(c.X, c.Y, c.Z) == scan.Structure) far.Add((c, c.Z == z ? d : Mm(x, y, c.X, c.Y) + Math.Abs(c.Z - z) * 3000L));
        }
        foreach (var (c, d) in near.OrderBy(t => t.Item2).ThenBy(t => t.Item1.Id))
        {
            scan.Sources.Add(new CraftSource { Store = c.Store, Id = c.Id, Name = c.Name, DistMm = d });
            scan.Containers.Add((c, d, false));
            foreach (var t in c.Tools) if (db.Tools.ContainsKey(t)) site.AddTool(db, t);
        }
        foreach (var (c, d) in far.OrderBy(t => t.Item2).ThenBy(t => t.Item1.Id))
        {
            scan.Sources.Add(new CraftSource { Store = c.Store, Id = c.Id, Name = c.Name, DistMm = d, Fetch = true });
            scan.Containers.Add((c, d, true));
        }
        return scan;
    }
}
