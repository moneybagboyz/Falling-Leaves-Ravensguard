using Ravensguard.Art.Sprites;

namespace Ravensguard.Art;

public enum SpriteKind { Terrain, Object, Item, Person, Overlay }

/// <param name="Id">Stable sprite id, e.g. "terrain.grass.0".</param>
/// <param name="Atlas">Atlas name; file is {Atlas}.png.</param>
/// <param name="Group">Preview grouping (rows break between groups).</param>
public sealed record SpriteDef(string Id, SpriteKind Kind, string Atlas, string Group, int W, int H, Func<PixelBuffer> Build)
{
    public bool NeedsOutline => Kind is SpriteKind.Object or SpriteKind.Item or SpriteKind.Person;
}

public static class Catalog
{
    public static List<SpriteDef> Build(Palette pal)
    {
        var l = new List<SpriteDef>();
        var t = new Terrain(pal); var hm = new Hamlet(pal); var o = new Objects(pal); var it = new Items(pal); var pe = new People(pal);

        void T(string id, string group, Func<PixelBuffer> f) => l.Add(new SpriteDef(id, SpriteKind.Terrain, "terrain_32", group, 32, 32, f));
        for (int v = 0; v < 6; v++) { int c = v; T($"terrain.grass.{c}", "terrain.grass", () => t.Grass(c)); }
        for (int v = 0; v < 4; v++) { int c = v; T($"terrain.dirt.{c}", "terrain.dirt", () => t.Dirt(c)); }
        for (int v = 0; v < 4; v++) { int c = v; T($"terrain.cobble.{c}", "terrain.cobble", () => t.Cobble(c)); }
        T("terrain.cobble_damaged.1", "terrain.cobble", () => t.CobbleDamaged(0, 1));
        T("terrain.cobble_damaged.2", "terrain.cobble", () => t.CobbleDamaged(1, 2));
        T("terrain.flagstone", "terrain.floor", t.Flag);
        T("terrain.wood_floor", "terrain.floor", t.Wood);
        T("terrain.earth_floor", "terrain.floor", t.Earth);
        T("terrain.wheat", "terrain.floor", t.Wheat);
        for (int v = 0; v < 3; v++) { int c = v; T($"terrain.water.{c}", "terrain.water", () => t.Water(c)); }
        T("terrain.ford", "terrain.water", t.Ford);
        foreach (var k in new[] { "", "win", "door", "open" })
        {
            var kk = k; T("terrain.wall_stone" + (k == "" ? "" : "_" + k), "terrain.wall", () => t.WallStone(kk));
        }
        T("terrain.wall_stone_damaged", "terrain.wall", t.WallStoneDamaged);
        foreach (var k in new[] { "", "win", "door" })
        {
            var kk = k; T("terrain.wall_timber" + (k == "" ? "" : "_" + k), "terrain.wall", () => t.WallTimber(kk));
        }

        // street-view additions
        foreach (var m in new[] { "timber", "stone", "wattle" })
            foreach (var w in new[] { false, true })
            {
                var mm = m; var ww = w;
                T($"terrain.wall_{m}_cap{(w ? "_win" : "")}", "terrain.wall", () => hm.WallCap(mm, ww));
            }
        foreach (var k in new[] { "", "win" }) { var kk = k; T("terrain.wall_wattle" + (k == "" ? "" : "_" + k), "terrain.wall", () => hm.WallWattle(kk)); }
        T("terrain.door_h", "terrain.floor", hm.DoorH);
        T("terrain.door_v", "terrain.floor", hm.DoorV);
        T("terrain.stairs_up", "terrain.floor", () => hm.Stairs(true));
        T("terrain.stairs_down", "terrain.floor", () => hm.Stairs(false));
        T("terrain.void", "terrain.floor", hm.Void);

        // autotile sets: 16 edge masks each (N=1 E=2 S=4 W=8, set = connected neighbour)
        var sets = new (string Name, Func<PixelBuffer> Overlay, string Rim, int Seed)[]
        {
            ("path_dirt", () => t.Dirt(0), "d1", 1),
            ("road_cobble", () => t.Cobble(0), "s1", 2),
            ("pond_water", () => t.Water(0), "d3", 3),
        };
        foreach (var s in sets)
            for (int m = 0; m < 16; m++)
            {
                var ss = s; int mm = m;
                T($"autotile.{s.Name}.{Autotile.MaskName(m)}", $"autotile.{s.Name}",
                    () => Autotile.Build(t.Grass(0), ss.Overlay(), mm, pal.P[ss.Rim], ss.Seed));
            }

        void O(string id, string atlas, int w, int h, Func<PixelBuffer> f) => l.Add(new SpriteDef(id, SpriteKind.Object, atlas, "object." + w, w, h, f));
        O("object.anvil", "objects_32", 32, 32, o.Anvil);
        O("object.forge", "objects_32", 32, 32, o.Forge);
        O("object.barrel", "objects_32", 32, 32, () => o.Barrel(false));
        O("object.barrel_water", "objects_32", 32, 32, () => o.Barrel(true));
        O("object.crate", "objects_32", 32, 32, o.Crate);
        O("object.chest", "objects_32", 32, 32, o.Chest);
        O("object.bed", "objects_32", 32, 32, o.Bed);
        O("object.table", "objects_32", 32, 32, o.Table);
        O("object.bench", "objects_32", 32, 32, o.Bench);
        O("object.rack", "objects_32", 32, 32, o.Rack);
        O("object.sacks", "objects_32", 32, 32, o.Sacks);
        O("object.hay", "objects_32", 32, 32, o.Hay);
        O("object.well", "objects_32", 32, 32, o.Well);
        O("object.hearth", "objects_32", 32, 32, () => o.Hearth(false));
        O("object.hearth_lit", "objects_32", 32, 32, () => o.Hearth(true));
        O("object.campfire", "objects_32", 32, 32, () => o.CampFire(false));
        O("object.campfire_lit", "objects_32", 32, 32, () => o.CampFire(true));
        O("object.snare", "objects_32", 32, 32, o.Snare);
        O("object.sign", "objects_32", 32, 32, o.Sign);
        O("object.stake", "objects_32", 32, 32, o.Stake);
        O("object.sconce", "objects_32", 32, 32, () => o.Sconce(false));
        O("object.sconce_lit", "objects_32", 32, 32, () => o.Sconce(true));
        O("object.bush", "objects_32", 32, 32, o.Bush);
        O("object.ladder", "objects_32", 32, 32, hm.Ladder);
        O("object.trapdoor", "objects_32", 32, 32, hm.Trapdoor);
        O("object.workbench", "objects_32", 32, 32, hm.Workbench);
        O("object.brazier", "objects_32", 32, 32, () => hm.Brazier(false));
        O("object.brazier_lit", "objects_32", 32, 32, () => hm.Brazier(true));
        O("object.rocks", "objects_32", 32, 32, o.Rocks);
        foreach (var c in new[] { "red", "blue", "green" }) { var cc = c; O($"object.stall_{c}", "objects_64", 64, 48, () => o.Stall(cc)); }
        O("object.cart", "objects_64", 64, 48, o.Cart);
        for (int v = 0; v < 3; v++) { int c = v; O($"object.tree.{c}", "objects_64", 64, 80, () => o.Tree(c)); }

        foreach (var (id, f) in it.All())
            l.Add(new SpriteDef("item." + id, SpriteKind.Item, "items_16", "item", 16, 16, f));

        foreach (var (id, opts) in People.Presets())
        {
            var oo = opts; l.Add(new SpriteDef("person." + id, SpriteKind.Person, "people_32x64", "person", 32, 64, () => pe.Person(oo)));
        }
        // palette-swap recolouring: one sprite, new faction colours (no redraw)
        var red = People.Presets().First(p => p.Id == "soldier_red").O;
        foreach (var (name, to) in new[] { ("gold", "gold"), ("brown", "brown") })
        {
            var tt = to;
            l.Add(new SpriteDef($"person.soldier_swap_{name}", SpriteKind.Person, "people_32x64", "person", 32, 64, () =>
                pe.Person(red).Recolor(PixelBuffer.RampSwap(pal.R["red"], pal.R[tt]))));
        }
        CatalogV2.Add(l, pal);
        return l;
    }
}
