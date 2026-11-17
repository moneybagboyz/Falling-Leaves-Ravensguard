using Ravensguard.Art.Sprites;
using static Ravensguard.Art.Noise;

namespace Ravensguard.Art;

/// <summary>Composes a sample village corner from the baked sprites, to check that the set reads as one tileset and to demo the season swap.
/// The same map is rendered per season: ground and foliage are recoloured through <see cref="Seasons"/>, winter swaps in bare trees,
/// snow-capped props and the snow overlay, and the pond water is swapped to the ice ramp.</summary>
public static class Scene
{
    private const int W = 22, H = 14;

    public static PixelBuffer Render(Palette pal, Dictionary<string, PixelBuffer> cache, string season)
    {
        bool winter = season == "winter";
        var cv = new PixelBuffer(W * 32, H * 32);
        PixelBuffer Get(string id) => cache.TryGetValue(id, out var b) ? b : throw new KeyNotFoundException(id);
        var iceSwap = PixelBuffer.RampSwap(pal.R["water"], pal.R["ice"]);
        PixelBuffer Tile(string id, bool water = false) { var b = Seasons.Apply(pal, Get(id), season); return winter && water ? b.Recolor(iceSwap) : b; }

        var cell = new string?[W, H]; var interior = new bool[W, H];
        var set = new Dictionary<string, HashSet<(int, int)>>();
        void Mark(string kind, int x, int y) { if (!set.TryGetValue(kind, out var s)) set[kind] = s = new(); s.Add((x, y)); }
        bool Has(string kind, int x, int y) => set.TryGetValue(kind, out var s) && s.Contains((x, y));
        int Mask(string kind, int x, int y) => (Has(kind, x, y - 1) ? 1 : 0) | (Has(kind, x + 1, y) ? 2 : 0) | (Has(kind, x, y + 1) ? 4 : 0) | (Has(kind, x - 1, y) ? 8 : 0);
        string Auto(string name, string kind, int x, int y) => $"autotile.{name}.{Autotile.MaskName(Mask(kind, x, y))}";

        // ---- regions
        for (int x = 0; x < 22; x++) Mark("road", x, 8);
        for (int y = 6; y <= 8; y++) Mark("road", 5, y);
        for (int x = 9; x <= 11; x++) for (int y = 9; y <= 10; y++) Mark("cobble", x, y);
        for (int x = 15; x <= 20; x++) for (int y = 1; y <= 6; y++) Mark("field", x, y);
        for (int y = 0; y <= 7; y++) Mark("hedge", 14, y);
        for (int x = 14; x <= 21; x++) { Mark("hedge", x, 0); Mark("hedge", x, 7); }
        for (int x = 0; x <= 3; x++) Mark("fence", x, 7);
        for (int y = 3; y <= 7; y++) Mark("fence", 0, y);
        for (int x = 9; x <= 13; x++) Mark("wall", x, 2);
        for (int y = 2; y <= 5; y++) Mark("wall", 13, y);
        for (int x = 17; x <= 20; x++) for (int y = 11; y <= 13; y++) Mark("pond", x, y);
        Mark("pond", 16, 12); Mark("pond", 21, 12);
        Mark("mud", 14, 9); Mark("mud", 15, 9); Mark("mud", 15, 10);
        for (int x = 2; x <= 8; x++) for (int y = 2; y <= 6; y++) Mark("house", x, y);
        for (int x = 3; x <= 7; x++) for (int y = 3; y <= 5; y++) interior[x, y] = true;

        // ---- ground layer
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double h = Hash(x, y, 4242);
                string id = h < .55 ? $"terrain.grass.{(int)(Hash(x, y, 1) * 4)}" : h < .7 ? "terrain.grass_long.0" : h < .8 ? $"terrain.meadow.{(int)(Hash(x, y, 2) * 3)}" : $"terrain.grass.{(int)(Hash(x, y, 3) * 6)}";
                if (Has("road", x, y)) id = Auto("track_packed", "road", x, y);
                if (Has("cobble", x, y)) id = Auto("road_cobble", "cobble", x, y);
                if (Has("mud", x, y)) id = Auto("mud_patch", "mud", x, y);
                if (Has("pond", x, y)) id = Auto("shore_water", "pond", x, y);
                if (Has("field", x, y))
                {
                    bool inner = Has("field", x - 1, y) && Has("field", x + 1, y) && Has("field", x, y - 1) && Has("field", x, y + 1);
                    string crop = y <= 2 ? "wheat_ripe" : y == 3 ? "barley_ripe" : y == 4 ? "young" : "ploughed";
                    if (winter) crop = y <= 4 ? "stubble" : "ploughed";
                    id = inner ? $"terrain.farm.{crop}" : Auto("farm_field", "field", x, y);
                }
                if (Has("hedge", x, y)) id = Auto("hedge", "hedge", x, y);
                if (Has("fence", x, y)) id = Auto("fence_wattle", "fence", x, y);
                if (Has("wall", x, y)) id = Auto("wall_field", "wall", x, y);
                if (interior[x, y]) id = (x + y) % 5 == 0 ? "terrain.floor_rush.1" : "terrain.floor_rush.0";
                cell[x, y] = id;
            }
        // ---- cottage walls (wattle): door south, glazed window north, shuttered window south
        for (int x = 2; x <= 8; x++)
            for (int y = 2; y <= 6; y++)
            {
                if (interior[x, y]) continue;
                int m = Mask("house", x, y); bool edgeN = y == 2, edgeS = y == 6, edgeW = x == 2, edgeE = x == 8;
                // wall cell masks: connected only to other wall cells (not the interior floor)
                m = (y > 2 && (edgeW || edgeE) ? 1 : 0) | (edgeN || edgeS ? 2 | 8 : 0) | (y < 6 && (edgeW || edgeE) ? 4 : 0);
                if (edgeN) m = (x > 2 ? 8 : 0) | (x < 8 ? 2 : 0) | (edgeW || edgeE ? 4 : 0);
                if (edgeS) m = (x > 2 ? 8 : 0) | (x < 8 ? 2 : 0) | (edgeW || edgeE ? 1 : 0);
                if ((edgeW || edgeE) && !edgeN && !edgeS) m = 5;
                cell[x, y] = $"terrain.wall.wattle.{Autotile.MaskName(m)}";
            }
        cell[5, 6] = "terrain.feat.wattle.door_closed"; cell[4, 2] = "terrain.feat.wattle.window_shuttered"; cell[7, 6] = "terrain.feat.wattle.window_open";
        cell[6, 2] = "terrain.feat.wattle.window_shuttered";

        // ---- draw ground
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var id = cell[x, y]!; bool water = id.StartsWith("autotile.shore_water");
                cv.Blit(Tile(id, water), x * 32, y * 32);
            }
        if (winter)
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var id = cell[x, y]!;
                    if (interior[x, y] || id.StartsWith("terrain.wall") || id.StartsWith("terrain.feat") || id.StartsWith("autotile.shore_water") || id.StartsWith("autotile.hedge") || id.StartsWith("autotile.wall_field") || id.StartsWith("autotile.fence")) continue;
                    int v = id.StartsWith("autotile.track") || id.StartsWith("autotile.road_cobble") || id.StartsWith("autotile.mud") ? 4 + (x + y) % 2 : id.Contains("farm") ? 4 + (x * 3 + y) % 2 : (x * 7 + y * 3) % 2;
                    cv.Blit(Get($"overlay.snow.{v}"), x * 32, y * 32);
                }

        // ---- y-sorted objects: (id, feet x, feet y)
        var objs = new List<(string Id, int X, int Y, bool Tall)>();
        void Obj(string id, int tx, int ty) => objs.Add((id, tx * 32, ty * 32 + 32, false));
        void Tree(string sp, double tx, int ty)
        {
            string id = $"object.tree.{sp}.{(winter ? "winter" : season == "autumn" ? "autumn" : "summer")}";
            objs.Add((id, (int)(tx * 32 + 16) - 32, ty * 32 + 30, true));
        }
        string Snow(string id) => winter ? id + "_snow" : id;
        Obj("object.bed", 3, 3); Obj("object.table", 6, 4); Obj("object.bench", 6, 5); Obj("object.hearth_lit", 7, 3); Obj("object.barrel", 3, 5); Obj("object.chest", 4, 3);
        Obj("object.well", 10, 8 + 1);
        Obj("object.signpost", 6, 7);
        Obj(Snow("object.barrel_stack"), 12, 9);
        Obj(Snow("object.haystack"), 18, 9); Obj(Snow("object.woodpile"), 1, 2); Obj(Snow("object.sheaves"), 20, 9);
        Obj(winter ? "object.bush_bare" : "object.bush_berry", 13, 6); Obj(winter ? "object.bush_bare" : "object.bush_round", 1, 9);
        Obj("object.stump.0", 16, 9); Obj(Snow("object.boulder"), 8, 12); Obj("object.rock_small", 10, 12); Obj("object.headstone_cross", 3, 12);
        Obj("object.trough_water", 12, 5); Obj("object.scarecrow", 17, 4);
        Tree("oak", 11.5, 5); Tree("apple", 0.0, 11); Tree("pine", 4.5, 13); Tree("birch", 13.5, 12); Tree("ash", 7.2, 11);
        foreach (var (id, x, y, tall) in objs.OrderBy(o => o.Y))
        {
            var spr = Seasons.Apply(pal, Get(id), season);
            cv.Blit(spr, x, y - spr.H);
        }
        return cv;
    }

    public static void Write(Palette pal, Dictionary<string, PixelBuffer> cache, string dir, int scale)
    {
        Directory.CreateDirectory(dir);
        foreach (var s in new[] { "summer", "autumn", "winter" })
            Png.Write(Path.Combine(dir, $"scene-{s}.png"), Render(pal, cache, s).Scaled(scale));
    }
}
