using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Live;
using Ravensguard.Sim.World;

namespace Ravensguard.Game.World;

/// <summary>Presentation rules: which baked sprite draws a cell of the <see cref="HamletMap"/>. Pure functions of the map, no engine types.</summary>
public static class TileSprites
{
    public static string? Terrain(HamletMap m, int x, int y, int z)
    {
        var id = m.StructureAt(x, y, z);
        if (id == null) return z == 0 ? Ground(m, x, y) : z < 0 ? "terrain.void" : null;
        var kind = m.Pieces.KindOf(id);
        switch (kind)
        {
            case PieceKind.Floor:
                return id switch
                {
                    "floor_plank" => "terrain.wood_floor",
                    "floor_packed_earth" => "terrain.earth_floor",
                    _ => "terrain.flagstone",
                };
            case PieceKind.StairsUp: return "terrain.stairs_up";
            case PieceKind.StairsDown: return "terrain.stairs_down";
            case PieceKind.Ladder: case PieceKind.Trapdoor: return "terrain.wood_floor";
            case PieceKind.Ground: return z == 0 ? Ground(m, x, y) : "terrain.wood_floor";
            case PieceKind.Post: return "terrain.wall_timber_cap";
            case PieceKind.Door:
                bool horizontal = m.IsWallish(x - 1, y, z) && m.IsWallish(x + 1, y, z);
                bool vertical = m.IsWallish(x, y - 1, z) && m.IsWallish(x, y + 1, z);
                return vertical && !horizontal ? "terrain.door_v" : "terrain.door_h";
            case PieceKind.Wall: return Wall(m, x, y, z, Material(id), false);
            case PieceKind.Window: return Wall(m, x, y, z, NeighbourMaterial(m, x, y, z), true);
        }
        return "terrain.void";
    }

    public static string? Object(HamletMap m, int x, int y, int z)
    {
        var kind = m.KindAt(x, y, z);
        if (kind == PieceKind.Ladder) return "object.ladder";
        if (kind == PieceKind.Trapdoor) return "object.trapdoor";
        return m.FixtureAt(x, y, z) switch
        {
            null => null,
            "hearth" => "object.hearth_lit",
            "forge" => "object.forge",
            "anvil" => "object.anvil",
            "workbench" => "object.workbench",
            "bed" or "bunk" => "object.bed",
            "table" or "altar" => "object.table",
            "chest" => "object.chest",
            "bench" or "loom" => "object.bench",
            "barrel" => "object.barrel",
            "shelf" or "tanning_rack" => "object.rack",
            "grain_bin" => "object.sacks",
            "stall" => "object.hay",
            _ => "object.crate",
        };
    }

    static string Material(string id) => id.EndsWith("stone") ? "stone" : id.EndsWith("wattle") ? "wattle" : "timber";

    static string NeighbourMaterial(HamletMap m, int x, int y, int z)
    {
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            if (m.KindAt(x + dx, y + dy, z) == PieceKind.Wall && m.StructureAt(x + dx, y + dy, z) is { } id) return Material(id);
        return "timber";
    }

    /// <summary>A wall whose southern neighbour is also wall shows only its top (so a vertical run reads as one raised wall); otherwise its front face.</summary>
    static string Wall(HamletMap m, int x, int y, int z, string mat, bool window)
    {
        bool cap = m.IsWallish(x, y + 1, z);
        return cap ? $"terrain.wall_{mat}_cap{(window ? "_win" : "")}" : $"terrain.wall_{mat}{(window ? "_win" : "")}";
    }

    static string Ground(HamletMap m, int x, int y)
    {
        var g = m.GroundAt(x, y, 0);
        int v = m.GroundVariant[m.Idx(x, y, 0)];
        switch (g)
        {
            case GroundKind.Dirt: case GroundKind.Cobble:
                int mask = (Linked(m, x, y - 1) ? 1 : 0) | (Linked(m, x + 1, y) ? 2 : 0) | (Linked(m, x, y + 1) ? 4 : 0) | (Linked(m, x - 1, y) ? 8 : 0);
                string set = g == GroundKind.Dirt ? "dirt" : "cobble";
                if (mask == 15) return $"terrain.{set}.{v % 4}";
                return $"autotile.{(g == GroundKind.Dirt ? "path_dirt" : "road_cobble")}.{MaskName(mask)}";
            default:
                return $"terrain.grass.{v}";
        }
    }

    /// <summary>A neighbour counts as connected to a road when it is road, paving or a building (no grass rim against a wall).</summary>
    static bool Linked(HamletMap m, int x, int y)
    {
        if (!m.Grid.InBounds(x, y, 0)) return true;
        return m.StructureAt(x, y, 0) != null || m.GroundAt(x, y, 0) != GroundKind.Grass;
    }

    public static string MaskName(int mk) =>
        (mk == 0 ? "o" : "") + ((mk & 1) != 0 ? "n" : "") + ((mk & 2) != 0 ? "e" : "") + ((mk & 4) != 0 ? "s" : "") + ((mk & 8) != 0 ? "w" : "");
}
