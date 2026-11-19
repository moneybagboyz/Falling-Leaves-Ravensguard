using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Sites;

/// <summary>A room placed on a level: the template, its rectangle, the purposes and its anchors.</summary>
public sealed record PlacedRoom(int Index, string Template, int Level, int X, int Y, int W, int H, IReadOnlyList<string> Purposes)
{
    public int Cx => X + W / 2;
    public int Cy => Y + H / 2;
    public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

/// <summary>
/// A generated site (exploration-and-sites 3.2): a stack of <see cref="Levels"/> levels of <see cref="W"/> x <see cref="H"/> one-metre cells, the rooms, portals, containers,
/// traps, unstable zones, the air and water of each cell and the inhabitants. Level 0 lies under the surface (z = -1), level L at z = -(L + 1). The map is generated once (a pure
/// function of the world seed, the site id, the level and the content hash) and then changed by play; the save keeps the generated snapshot and what play changed.
/// </summary>
public sealed class SiteMap
{
    public ulong SiteId { get; init; }
    public ulong Seed { get; init; }
    public string Template { get; init; } = "";
    public string ContentHash { get; init; } = "";
    public int W { get; init; }
    public int H { get; init; }
    public int Levels { get; init; }
    public int Plane => W * H;
    public int Cells => W * H * Levels;

    public SiteTile[] Tiles { get; init; } = [];
    /// <summary>Air index per cell, 0 (foul) .. 1000 (fresh) (4.5.1).</summary>
    public ushort[] Air { get; init; } = [];
    /// <summary>Standing water per cell, cm (a sump, a river, a flooded drift); the level's flood rise adds to it.</summary>
    public ushort[] WaterCm { get; init; } = [];
    /// <summary>Room index per cell, -1 for corridors and rock.</summary>
    public short[] Room { get; init; } = [];
    public List<PlacedRoom> Rooms { get; init; } = [];
    public List<SitePortal> Portals { get; init; } = [];
    public List<SiteContainer> Containers { get; init; } = [];
    public List<SiteTrap> Traps { get; init; } = [];
    public List<UnstableZone> Unstable { get; init; } = [];
    public List<Inhabitant> Inhabitants { get; init; } = [];
    /// <summary>Light sconces (unlit until someone lights them): cells.</summary>
    public List<int> Sconces { get; init; } = [];
    /// <summary>The cell of the stairs up to the surface on level 0 (the mouth), and of each level's arrival.</summary>
    public int Mouth { get; set; }
    public int[] Arrival { get; init; } = [];
    /// <summary>The flood on each level (cm over every floor cell), raised by flood events.</summary>
    public int[] FloodCm { get; init; } = [];
    /// <summary>The loot budget the history gave and what of it was placed (mites of value).</summary>
    public int LootBudgetMites { get; set; }
    public int LootPlacedMites { get; set; }
    public bool Taboo { get; set; }
    public bool Lair { get; set; }
    public string Walls { get; set; } = "masonry";

    public int Index(int level, int x, int y) => (level * H + y) * W + x;
    public (int Level, int X, int Y) At(int cell) { int x = cell % W, r = cell / W; return (r / H, x, r % H); }
    public bool In(int level, int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H && (uint)level < (uint)Levels;
    public SiteTile Tile(int level, int x, int y) => In(level, x, y) ? Tiles[Index(level, x, y)] : SiteTile.Rock;
    public int Water(int cell) => Tiles[cell] is SiteTile.Water or SiteTile.Floor or SiteTile.DoorOpen ? WaterCm[cell] + FloodCm[cell / Plane] : WaterCm[cell];
    public static int Z(int level) => -(level + 1);
    public static int LevelOf(int z) => -z - 1;

    /// <summary>The pathfinding grid of the site (z = -Levels .. -1): walkable cells open, portals between levels.</summary>
    public LevelGrid BuildGrid()
    {
        var g = new LevelGrid(W, H, -Levels, -1);
        for (int l = 0; l < Levels; l++)
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (SiteTiles.Walkable(Tiles[Index(l, x, y)]) && Tiles[Index(l, x, y)] != SiteTile.Hole) g.SetOpen(x, y, Z(l), true);
        foreach (var p in Portals)
        {
            if (p.LowerCell < 0) continue;
            var (la, xa, ya) = At(p.UpperCell); var (lb, xb, yb) = At(p.LowerCell);
            var portal = g.AddPortal(p.Kind == PortalKindSite.Stairs ? PortalKind.Stairs : PortalKind.Ladder, new Cell3(xa, ya, Z(la)), new Cell3(xb, yb, Z(lb)), p.Kind == PortalKindSite.Stairs ? 40 : 60);
            if (!p.Enabled) g.SetPortalEnabled(portal.Id, false);
        }
        return g;
    }

    /// <summary>The light grid of the site: walls, rock, closed doors and rubble are opaque; everything is underground (no ambient light).</summary>
    public LightGrid BuildLight()
    {
        var lg = new LightGrid(W, H, -Levels, -1);
        for (int l = 0; l < Levels; l++)
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    lg.SetOpaque(x, y, Z(l), SiteTiles.Opaque(Tiles[Index(l, x, y)]));
        return lg;
    }

    public SiteContainer? ContainerAt(int cell) { foreach (var c in Containers) if (c.Cell == cell) return c; return null; }
    public SiteTrap? TrapAt(int cell) { foreach (var t in Traps) if (t.Cell == cell) return t; return null; }
    public SitePortal? PortalAt(int cell) { foreach (var p in Portals) if (p.UpperCell == cell || p.LowerCell == cell) return p; return null; }

    /// <summary>Loot value still in the site (mites).</summary>
    public int LootLeftMites { get { int v = 0; foreach (var c in Containers) foreach (var i in c.Items) v += i.ValueMites; return v; } }

    /// <summary>The <c>SiteHash</c> (overview 11): tiles, air, water, flood, containers, traps, portals, zones and inhabitants.</summary>
    public ulong Hash(ulong h = 14695981039346656037UL)
    {
        static ulong M(ulong h, long v) { unchecked { h ^= (ulong)v; h *= 1099511628211UL; h ^= h >> 29; return h; } }
        static ulong N(ulong h, params long[] v) { foreach (var x in v) h = M(h, x); return h; }
        h = N(h, (long)SiteId, (long)Seed, W, H, Levels);
        for (int i = 0; i < Tiles.Length; i++) h = M(M(M(h, (long)Tiles[i]), Air[i]), WaterCm[i]);
        foreach (var f in FloodCm) h = M(h, f);
        foreach (var c in Containers)
        {
            h = N(h, c.Id, c.Cell, c.Opened ? 1 : 0);
            foreach (var it in c.Items) h = N(h, Str(it.Item), it.ValueMites, it.MassG, it.ConditionPm);
        }
        foreach (var t in Traps) h = N(h, t.Id, t.Cell, t.Armed ? 1 : 0, t.Detected ? 1 : 0, t.Triggered);
        foreach (var p in Portals) h = N(h, p.Id, p.UpperCell, p.LowerCell, p.Enabled ? 1 : 0);
        foreach (var z in Unstable) h = N(h, z.Id, z.InstabilityPm, z.Collapsed ? 1 : 0);
        foreach (var n in Inhabitants) h = N(h, (long)n.Id, n.X, n.Y, n.Level, (long)n.State, n.LossPm, n.FledDay);
        h = M(h, LootBudgetMites);
        return h;
    }

    internal static long Str(string s) { unchecked { long h = 17; foreach (char c in s) h = h * 31 + c; return h; } }
}
