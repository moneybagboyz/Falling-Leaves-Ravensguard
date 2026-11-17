using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.World;

namespace Ravensguard.Sim.Live;

/// <summary>Builds the test hamlet (cottage, longhouse, smithy, tavern, roads) on a 96 x 96 m area by placing real blueprints into a LevelGrid.</summary>
public static class HamletBuilder
{
    public const int Size = 96;
    public static readonly Cell3 HeroStart = new(47, 51, 0);

    private static int H(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 1442695041u;
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (int)(h & 0x7fffffff);
        }
    }

    public static HamletMap Build(BuildingLibrary lib, int seed = 1)
    {
        var grid = new LevelGrid(Size, Size, -1, 1);
        grid.FillRect(0, 0, Size - 1, Size - 1, 0, true);
        var map = new HamletMap(grid, lib.Pieces);

        // ground: grass everywhere, then roads, lanes and the cobbled square
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int i = grid.Index(x, y, 0);
                map.Ground[i] = GroundKind.Grass; map.GroundVariant[i] = (byte)(H(x, y, seed) % 6);
            }

        // buildings first (their cells are excluded from the ground paint below)
        Place(map, lib, "smithy", 34, 40, 0, false, Smithy);
        Place(map, lib, "tavern", 50, 38, 0, false, Tavern);
        Place(map, lib, "peasant_cottage", 30, 52, 1, false, Cottage);       // door to the north
        Place(map, lib, "longhouse", 60, 53, 2, false, Longhouse);           // door to the north
        Place(map, lib, "peasant_cottage", 74, 38, 3, false, Cottage);       // door to the south

        void Paint(int x0, int y0, int x1, int y1, GroundKind k)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                    int i = grid.Index(x, y, 0);
                    if (map.Structure[i] != null) continue;
                    map.Ground[i] = k; map.GroundVariant[i] = (byte)(H(x, y, seed + 3) % 4);
                }
        }
        Paint(0, 47, Size - 1, 49, GroundKind.Dirt);       // main road, east-west
        Paint(46, 12, 47, 86, GroundKind.Dirt);            // lane, north-south
        Paint(44, 40, 49, 51, GroundKind.Cobble);          // square between smithy and tavern
        Paint(38, 46, 38, 46, GroundKind.Dirt);            // door paths
        Paint(54, 46, 54, 46, GroundKind.Dirt);
        Paint(76, 46, 76, 46, GroundKind.Dirt);
        Paint(32, 50, 32, 51, GroundKind.Dirt);
        Paint(68, 50, 68, 52, GroundKind.Dirt);
        Paint(36, 46, 56, 46, GroundKind.Cobble);          // pavement along the shopfronts
        Paint(22, 51, 29, 51, GroundKind.Dirt);

        // scenery
        void Add(int x, int y, string sprite, bool wide = false, bool blocks = true, int lr = 0, int li = 0, int z = 0)
        {
            if (!grid.InBounds(x, y, z)) return;
            map.Props.Add(new Prop(x, y, z, sprite, wide, blocks, lr, li));
            if (blocks) { grid.SetOpen(x, y, z, false); if (wide) grid.SetOpen(x + 1, y, z, false); }
        }
        bool Free(int x, int y) => grid.IsOpen(x, y, 0) && map.Ground[grid.Index(x, y, 0)] == GroundKind.Grass && map.Structure[grid.Index(x, y, 0)] == null;

        Add(47, 44, "object.well");
        Add(44, 42, "object.stall_red", wide: true);
        Add(48, 42, "object.stall_blue", wide: true);
        Add(53, 46, "object.brazier_lit", lr: 7, li: 235);
        Add(56, 46, "object.brazier_lit", lr: 7, li: 235);
        Add(55, 46, "object.sign");
        Add(37, 46, "object.barrel_water");
        Add(40, 46, "object.crate");
        Add(33, 44, "object.barrel");
        Add(33, 45, "object.barrel");
        Add(64, 51, "object.cart", wide: true);
        Add(58, 59, "object.hay"); Add(59, 59, "object.hay"); Add(57, 59, "object.sacks");
        Add(35, 52, "object.bench");
        Add(26, 49, "object.stake");
        Add(66, 47, "object.sacks", blocks: false);

        // trees and bushes: denser toward the edges, kept off roads and buildings
        for (int y = 2; y < Size - 1; y += 1)
            for (int x = 2; x < Size - 1; x += 1)
            {
                if (!Free(x, y)) continue;
                int edge = Math.Min(Math.Min(x, Size - 1 - x), Math.Min(y, Size - 1 - y));
                int h = H(x, y, seed + 11) % 1000;
                int treeChance = edge < 12 ? 70 : edge < 22 ? 28 : 7;
                if (h < treeChance) { if (NoNeighbourProp(map, x, y)) Add(x, y, "object.tree." + H(x, y, 5) % 3); }
                else if (h < treeChance + 14) Add(x, y, "object.bush");
                else if (h < treeChance + 18) Add(x, y, "object.rocks");
            }
        return map;
    }

    private static bool NoNeighbourProp(HamletMap map, int x, int y)
    {
        foreach (var p in map.Props) if (Math.Abs(p.X - x) <= 1 && Math.Abs(p.Y - y) <= 1) return false;
        return true;
    }

    // ---- furniture (blueprint-local coordinates, before rotation); fixtures here are what the household brings later ----
    private static readonly (int Z, int X, int Y, string Piece)[] Cottage =
    {
        (0, 4, 2, "table"), (0, 4, 3, "bench"), (0, 6, 3, "chest"), (0, 6, 2, "barrel"),
        (1, 1, 1, "bed"), (1, 3, 1, "bed"), (1, 1, 3, "bed"), (1, 3, 3, "bed"), (1, 5, 3, "chest"),
    };
    private static readonly (int Z, int X, int Y, string Piece)[] Longhouse =
    {
        (0, 1, 1, "stall"), (0, 1, 4, "stall"), (0, 3, 1, "grain_bin"),
        (0, 8, 4, "table"), (0, 7, 4, "bench"), (0, 10, 3, "chest"),
        (0, 14, 3, "bed"), (0, 14, 4, "bed"), (0, 13, 4, "bed"), (0, 12, 4, "chest"),
    };
    private static readonly (int Z, int X, int Y, string Piece)[] Smithy =
    {
        (0, 3, 2, "anvil"), (0, 5, 1, "workbench"), (0, 1, 4, "barrel"),
        (0, 7, 1, "shelf"), (0, 8, 1, "grain_bin"), (0, 8, 4, "chest"), (0, 7, 4, "barrel"),
    };
    private static readonly (int Z, int X, int Y, string Piece)[] Tavern =
    {
        (0, 2, 2, "table"), (0, 6, 2, "table"), (0, 2, 5, "table"), (0, 6, 5, "table"),
        (0, 3, 2, "bench"), (0, 5, 2, "bench"), (0, 3, 5, "bench"), (0, 5, 5, "bench"),
        (0, 10, 2, "table"), (0, 9, 5, "barrel"), (0, 10, 5, "barrel"), (0, 10, 6, "chest"),
        (1, 1, 1, "bed"), (1, 3, 1, "bed"), (1, 1, 3, "bed"), (1, 5, 1, "bed"), (1, 7, 1, "bed"), (1, 5, 3, "bed"),
        (1, 10, 1, "bed"), (1, 10, 3, "bed"),
    };

    private static void Place(HamletMap map, BuildingLibrary lib, string id, int ox, int oy, int turns, bool mirror, (int Z, int X, int Y, string Piece)[] furniture)
    {
        var g = lib.Grid(id).Clone();
        foreach (var (z, x, y, piece) in furniture)
        {
            var lv = g.Level(z) ?? throw new InvalidOperationException($"{id}: no level {z}");
            int i = lv.Idx(x, y);
            var kind = lib.Pieces.KindOf(lv.Structure[i]);
            if (kind != PieceKind.Floor || lv.Fixture[i] != null)
                throw new InvalidOperationException($"{id}: cannot put {piece} at ({x},{y},z{z}): not a free floor cell");
            lv.Fixture[i] = piece;
        }
        var placed = BlueprintPlacer.Place(g, lib.Pieces, map.Grid, ox, oy, turns, mirror);
        map.Buildings.Add(placed);
        var t = g.Transform(turns, mirror);
        foreach (var (z, lv) in t.Levels)
            for (int y = 0; y < lv.Height; y++)
                for (int x = 0; x < lv.Width; x++)
                {
                    int i = lv.Idx(x, y);
                    if (lv.Structure[i] == null) continue;
                    int wi = map.Grid.Index(ox + x, oy + y, z);
                    map.Structure[wi] = lv.Structure[i];
                    map.Fixture[wi] = lv.Fixture[i];
                }
    }
}
