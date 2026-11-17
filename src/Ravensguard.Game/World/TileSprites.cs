using System;
using System.Collections.Generic;
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
            "stall" or "hurdle" => "object.hay",
            "hearth_central" or "hearth_chimney" => "object.hearth_lit",
            "straw_pallet" => "object.bed",
            "pew" or "high_seat" => "object.bench",
            "weapon_rack" => "object.rack",
            "shop_counter" or "toll_counter" => "object.table",
            "hay_stack" => "object.hay",
            "well" => "object.well",
            _ => "object.crate",
        };
    }

    static string Material(string id) => id.EndsWith("stone") || id.EndsWith("brick") || id.EndsWith("crenellated") ? "stone" : id.EndsWith("wattle") ? "wattle" : "timber";

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
            case GroundKind.Water: case GroundKind.DeepWater:
            {
                int wm = (WaterLike(m, x, y - 1) ? 1 : 0) | (WaterLike(m, x + 1, y) ? 2 : 0) | (WaterLike(m, x, y + 1) ? 4 : 0) | (WaterLike(m, x - 1, y) ? 8 : 0);
                if (wm != 15) return $"autotile.pond_water.{MaskName(wm)}";
                return g == GroundKind.DeepWater ? $"terrain.water_deep.{v % 3}" : $"terrain.water_shallow.{v % 3}";
            }
            case GroundKind.Ford: return "terrain.ford";
            case GroundKind.Bridge: return "terrain.wood_floor";
            case GroundKind.Meadow: return $"terrain.meadow.{v % 4}";
            case GroundKind.ForestFloor: return $"terrain.forest_floor.{v % 4}";
            case GroundKind.LeafLitter: return $"terrain.leaf_litter.{v % 4}";
            case GroundKind.Sand: return $"terrain.sand.{v % 4}";
            case GroundKind.Mud: return $"terrain.mud.{v % 4}";
            case GroundKind.Marsh: return $"terrain.marsh.{v % 4}";
            case GroundKind.Gravel: return $"terrain.gravel.{v % 4}";
            case GroundKind.Rock: return $"terrain.rock.{v % 4}";
            case GroundKind.Scree: return $"terrain.scree.{v % 4}";
            case GroundKind.Peat: return $"terrain.peat.{v % 4}";
            case GroundKind.Furrow: return "terrain.farm.ploughed";
            case GroundKind.Sown: return "terrain.farm.sown";
            case GroundKind.Stubble: return "terrain.farm.stubble";
            case GroundKind.Fallow: return "terrain.farm.fallow";
            case GroundKind.Crop: return v is 3 or 4 ? "terrain.farm.barley_ripe" : "terrain.farm.wheat_ripe"; // variant carries the crop: barley and oats vs wheat and rye
            default:
                return $"terrain.grass.{v % 6}";
        }
    }

    /// <summary>A neighbour counts as connected to a road when it is road, paving, a ford or bridge, or a building (no grass rim against a wall).</summary>
    static bool Linked(HamletMap m, int x, int y)
    {
        if (!m.Grid.InBounds(x, y, 0)) return true;
        if (m.StructureAt(x, y, 0) != null) return true;
        return m.GroundAt(x, y, 0) is GroundKind.Dirt or GroundKind.Cobble or GroundKind.Ford or GroundKind.Bridge or GroundKind.None;
    }

    static bool WaterLike(HamletMap m, int x, int y)
    {
        if (!m.Grid.InBounds(x, y, 0)) return true;
        if (m.StructureAt(x, y, 0) != null) return false;
        return m.GroundAt(x, y, 0) is GroundKind.Water or GroundKind.DeepWater or GroundKind.Ford or GroundKind.Bridge or GroundKind.None;
    }

    public static string MaskName(int mk) =>
        (mk == 0 ? "o" : "") + ((mk & 1) != 0 ? "n" : "") + ((mk & 2) != 0 ? "e" : "") + ((mk & 4) != 0 ? "s" : "") + ((mk & 8) != 0 ? "w" : "");

    // ---- graceful fallback: the generator names the sprite it wants; the atlas may not have it yet ----

    static readonly Dictionary<string, string[]> Families = new()
    {
        ["terrain.meadow"] = ["terrain.grass"],
        ["terrain.forest_floor"] = ["terrain.leaf_litter", "terrain.grass"],
        ["terrain.leaf_litter"] = ["terrain.forest_floor", "terrain.dirt"],
        ["terrain.sand"] = ["terrain.gravel", "terrain.dirt"],
        ["terrain.mud"] = ["terrain.dirt"],
        ["terrain.marsh"] = ["terrain.mud", "terrain.grass"],
        ["terrain.gravel"] = ["terrain.dirt"],
        ["terrain.rock"] = ["terrain.scree", "terrain.cobble", "terrain.dirt"],
        ["terrain.scree"] = ["terrain.gravel", "terrain.dirt"],
        ["terrain.peat"] = ["terrain.mud", "terrain.dirt"],
        ["terrain.water_shallow"] = ["terrain.water"],
        ["terrain.water_deep"] = ["terrain.water"],
        ["terrain.farm.ploughed"] = ["terrain.dirt"],
        ["terrain.farm.sown"] = ["terrain.farm.young", "terrain.wheat", "terrain.dirt"],
        ["terrain.farm.young"] = ["terrain.farm.sown", "terrain.wheat"],
        ["terrain.farm.stubble"] = ["terrain.farm.fallow", "terrain.dirt"],
        ["terrain.farm.fallow"] = ["terrain.grass"],
        ["terrain.farm.wheat_ripe"] = ["terrain.wheat"],
        ["terrain.farm.barley_ripe"] = ["terrain.farm.wheat_ripe", "terrain.wheat"],
        ["autotile.pond_water"] = ["terrain.water"],
        ["object.hurdle"] = ["object.bush", "object.stake"],
        ["object.haystack"] = ["object.hay"],
        ["object.sheaves"] = ["object.hay"],
        ["object.scarecrow"] = ["object.stake"],
        ["object.woodpile"] = ["object.crate"],
        ["object.signpost"] = ["object.sign"],
        ["object.barrow"] = ["object.rocks"],
        ["object.boulder"] = ["object.rocks"],
        ["object.rubble"] = ["object.rocks"],
        ["object.grave_mound"] = ["object.rocks"],
        ["object.stump"] = ["object.rocks"],
    };

    /// <summary>The sprite id and then what to try instead, most similar first. The caller takes the first the atlas holds.</summary>
    public static IEnumerable<string> Alternatives(string id)
    {
        yield return id;
        string family = id; string? variant = null;
        int dot = id.LastIndexOf('.');
        if (dot > 0 && int.TryParse(id.AsSpan(dot + 1), out _)) { family = id[..dot]; variant = id[(dot + 1)..]; }
        else if (id.StartsWith("autotile.") && dot > "autotile.".Length) family = id[..dot];
        if (variant != null) yield return family + ".0";
        if (Families.TryGetValue(family, out var alts))
            foreach (var a in alts)
            {
                if (variant != null) yield return a + "." + variant;
                yield return a + ".0";
                yield return a;
            }
        if (family.StartsWith("terrain.") || family.StartsWith("autotile.")) { yield return "terrain.grass.0"; }
    }
}
