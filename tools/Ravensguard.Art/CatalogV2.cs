using Ravensguard.Art.Sprites;

namespace Ravensguard.Art;

/// <summary>Tileset v2 (Phase 1): ground, floors, walls, autotile sets, roofs, overlays, props and trees. Atlases: terrain2_32, roofs_32, overlays_32, props_32, props_64.</summary>
public static class CatalogV2
{
    public static readonly HashSet<string> Atlases = new() { "terrain2_32", "roofs_32", "overlays_32", "props_32", "props_64", "field_8", "mini_16", "vehicles_32" };
    public static readonly string[] Seasonal = { "summer", "autumn", "winter" };

    public static void Add(List<SpriteDef> l, Palette pal)
    {
        var g = new Ground(pal); var w = new Walls(pal); var rf = new Roofs(pal); var wx = new Weather(pal); var pr = new Props(pal); var t = new Terrain(pal);
        void T(string id, string group, Func<PixelBuffer> f) => l.Add(new SpriteDef(id, SpriteKind.Terrain, "terrain2_32", group, 32, 32, f));

        // ---- ground
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.grass_long.{c}", "v2.grass", () => g.GrassLong(c)); }
        for (int v = 0; v < 3; v++) { int c = v; T($"terrain.meadow.{c}", "v2.grass", () => g.Meadow(c)); }
        T("terrain.farm.ploughed", "v2.farm", g.Ploughed); T("terrain.farm.sown", "v2.farm", g.Sown); T("terrain.farm.young", "v2.farm", g.YoungCrop);
        T("terrain.farm.wheat_ripe", "v2.farm", () => g.Ripe(false)); T("terrain.farm.barley_ripe", "v2.farm", () => g.Ripe(true));
        T("terrain.farm.stubble", "v2.farm", g.Stubble); T("terrain.farm.fallow", "v2.farm", g.Fallow);
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.mud.{c}", "v2.dirt", () => g.Mud(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.puddle.{c}", "v2.dirt", () => g.PuddleMud(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.track.h{c}", "v2.dirt", () => g.Track(c, true)); T($"terrain.track.v{c}", "v2.dirt", () => g.Track(c, false)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.sand.{c}", "v2.ground", () => g.Sand(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.gravel.{c}", "v2.ground", () => g.Gravel(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.rock.{c}", "v2.ground", () => g.Rock(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.scree.{c}", "v2.ground", () => g.Scree(c)); }
        for (int v = 0; v < 3; v++) { int c = v; T($"terrain.forest_floor.{c}", "v2.ground", () => g.ForestFloor(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.leaf_litter.{c}", "v2.ground", () => g.LeafLitter(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.peat.{c}", "v2.wet", () => g.Peat(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.marsh.{c}", "v2.wet", () => g.Marsh(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.reeds.{c}", "v2.wet", () => g.Reeds(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.water_shallow.{c}", "v2.wet", () => g.WaterShallow(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.water_deep.{c}", "v2.wet", () => g.WaterDeep(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.ice.{c}", "v2.wet", () => g.Ice(c)); }

        // ---- floors
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.floor_rush.{c}", "v2.floor", () => w.Rush(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.floor_brick.{c}", "v2.floor", () => w.BrickFloor(c)); }
        for (int v = 0; v < 2; v++) { int c = v; T($"terrain.floor_tile.{c}", "v2.floor", () => w.TileFloor(c)); }
        T("terrain.floor_plank_v", "v2.floor", w.PlankV); T("terrain.floor_flag_irregular", "v2.floor", w.FlagIrregular);
        T("terrain.stairs_stone_up", "v2.floor", () => w.StoneStairs(true)); T("terrain.stairs_stone_down", "v2.floor", () => w.StoneStairs(false));

        // ---- walls: 16 masks per material (N=1 E=2 S=4 W=8 = neighbour is the same wall)
        foreach (var m in Walls.Mats)
            for (int k = 0; k < 16; k++) { var mm = m; int kk = k; T($"terrain.wall.{m}.{Autotile.MaskName(k)}", $"v2.wall.{m}", () => w.Wall(mm, kk)); }
        var feats = new (string Mat, string[] Kinds)[]
        {
            ("wattle", new[] { "door_closed", "door_open", "window_shuttered", "window_open" }),
            ("timber", new[] { "door_closed", "door_open", "window_shuttered", "window_open", "window_glazed" }),
            ("stone", new[] { "door_closed", "door_open", "window_shuttered", "window_open", "window_glazed", "arrow_slit" }),
            ("brick", new[] { "door_closed", "door_open", "window_shuttered", "window_open", "window_glazed" }),
            ("town", new[] { "arrow_slit", "gate_closed", "gate_open" }),
        };
        foreach (var (mat, kinds) in feats) foreach (var kd in kinds) { var a = mat; var b = kd; T($"terrain.feat.{mat}.{kd}", "v2.feat", () => w.Feature(a, b)); }

        // ---- autotile sets (16 edge masks)
        var grass0 = t.Grass(0);
        void Set(string name, Func<int, PixelBuffer> build) { for (int m = 0; m < 16; m++) { int mm = m; T($"autotile.{name}.{Autotile.MaskName(m)}", $"autotile.{name}", () => build(mm)); } }
        Set("shore_water", m => Autotile2.BuildBanded(grass0, new[] { (g.Sand(0), 2), (g.WaterShallow(0), 3) }, g.WaterDeep(0), m, 21));
        Set("bank_river", m => Autotile2.BuildBanded(g.Mud(0), new[] { (g.WaterShallow(1), 4) }, g.WaterDeep(1), m, 22));
        Set("track_packed", m => Autotile.Build(grass0, ((m & 5) != 0 && (m & 10) == 0) ? g.Track(0, false) : g.Track(0, true), m, pal.P["d1"], 23));
        Set("mud_patch", m => Autotile.Build(grass0, g.Mud(0), m, pal.P["mu1"], 24));
        Set("farm_field", m => Autotile.Build(grass0, g.Ploughed(), m, pal.P["d1"], 25));
        Set("hedge", m => Autotile2.BuildLine(grass0, Autotile2.Hedge(pal), m));
        Set("fence_wattle", m => Autotile2.BuildLine(grass0, Autotile2.Wattle(pal), m));
        Set("wall_field", m => Autotile2.BuildLine(grass0, Autotile2.FieldWall(pal), m));
        Set("palisade", m => Autotile2.BuildLine(grass0, Autotile2.Palisade(pal), m));
        Set("wall_churchyard", m => Autotile2.BuildLine(grass0, Autotile2.ChurchWall(pal), m));

        // ---- roofs (Field / Command zoom, world map)
        foreach (var m in Roofs.Mats) foreach (var k in Roofs.Kinds) { var mm = m; var kk = k; l.Add(new SpriteDef($"roof.{m}.{k}", SpriteKind.Terrain, "roofs_32", "roof." + m, 32, 32, () => rf.Roof(mm, kk))); }

        // ---- overlays
        void Ov(string id, string group, Func<PixelBuffer> f) => l.Add(new SpriteDef(id, SpriteKind.Overlay, "overlays_32", group, 32, 32, f));
        for (int v = 0; v < 6; v++) { int c = v; Ov($"overlay.snow.{c}", "ov.snow", () => wx.Snow(c)); }
        for (int v = 0; v < 3; v++) { int c = v; Ov($"overlay.puddle.{c}", "ov.rain", () => wx.Puddle(c)); }
        for (int f = 0; f < 4; f++) { int c = f; Ov($"overlay.splash.{c}", "ov.rain", () => wx.Splash(c)); }
        for (int f = 0; f < 4; f++) { int c = f; Ov($"overlay.rain.{c}", "ov.fall", () => wx.Rain(c)); }
        for (int f = 0; f < 4; f++) { int c = f; Ov($"overlay.snowfall.{c}", "ov.fall", () => wx.SnowFall(c)); }
        for (int v = 0; v < 2; v++) { int c = v; Ov($"overlay.fog.{c}", "ov.fall", () => wx.Fog(c)); }

        // ---- props 32x32
        void O(string id, Func<PixelBuffer> f) => l.Add(new SpriteDef(id, SpriteKind.Object, "props_32", "props.32", 32, 32, f));
        O("object.stump.0", () => pr.Stump(0)); O("object.stump.1", () => pr.Stump(1));
        foreach (var k in new[] { "round", "berry", "thorn", "bare" }) { var kk = k; O($"object.bush_{k}", () => pr.Bush(kk)); }
        O("object.rock_small", () => pr.Rock(0)); O("object.rock_mid", () => pr.Rock(1)); O("object.boulder", () => pr.Rock(2));
        O("object.haystack", pr.Haystack); O("object.sheaves", pr.Sheaves); O("object.woodpile", pr.Woodpile);
        O("object.trough_water", () => pr.Trough(true)); O("object.trough_feed", () => pr.Trough(false));
        O("object.barrel_stack", pr.BarrelStack); O("object.barrel_open", pr.BarrelOpen); O("object.crate_stack", pr.CrateStack);
        O("object.sack_pile", () => pr.SackPile(false)); O("object.sack_grain", () => pr.SackPile(true));
        O("object.signpost", pr.Signpost); O("object.sign_inn", pr.InnSign);
        for (int v = 0; v < 4; v++) { int c = v; O(new[] { "object.headstone_round", "object.headstone_cross", "object.headstone_slab", "object.grave_mound" }[v], () => pr.Headstone(c)); }
        foreach (var k in new[] { "h", "low", "corner" }) { var kk = k; O($"object.ruin_wall_{k}", () => pr.RuinWall(kk)); }
        O("object.rubble.0", () => pr.Rubble(0)); O("object.rubble.1", () => pr.Rubble(1));
        O("object.gate_closed", () => pr.Gate(false)); O("object.gate_open", () => pr.Gate(true));
        O("object.hurdle", pr.Hurdle); O("object.scarecrow", pr.Scarecrow);
        O("object.trapdoor_open", pr.TrapdoorOpen); O("object.ladder_down", pr.LadderDown);
        // winter: snow caps on the finished sprites
        O("object.haystack_snow", () => pr.Snowy(pr.Haystack())); O("object.woodpile_snow", () => pr.Snowy(pr.Woodpile())); O("object.boulder_snow", () => pr.Snowy(pr.Rock(2)));
        O("object.bush_round_snow", () => pr.Snowy(pr.Bush("round"))); O("object.sheaves_snow", () => pr.Snowy(pr.Sheaves())); O("object.headstone_round_snow", () => pr.Snowy(pr.Headstone(0)));
        O("object.barrel_stack_snow", () => pr.Snowy(pr.BarrelStack())); O("object.rock_mid_snow", () => pr.Snowy(pr.Rock(1)));

        // ---- props 64 wide: trees (64x80) in summer / autumn / winter-bare, stalls, loaded carts, barrow
        void O64(string id, int h, Func<PixelBuffer> f) => l.Add(new SpriteDef(id, SpriteKind.Object, "props_64", "props.64." + h, 64, h, f));
        foreach (var sp in new[] { "oak", "ash", "birch", "pine", "apple" })
        {
            var s = sp;
            O64($"object.tree.{sp}.summer", 80, () => pr.Tree(s, false));
            O64($"object.tree.{sp}.autumn", 80, () => Seasons.Apply(pal, pr.Tree(s, false, s == "apple" ? "ripe" : ""), "autumn"));
            O64($"object.tree.{sp}.winter", 80, () => pr.Tree(s, true));
        }
        O64("object.tree.apple.spring", 80, () => pr.Tree("apple", false, "blossom"));
        O64("object.lychgate", 48, pr.Lychgate);
        foreach (var k in new[] { "yellow", "brown", "plain" }) { var kk = k; O64($"object.stall_{k}", 48, () => pr.Stall(kk)); }
        foreach (var k in new[] { "hay", "barrels", "goods" }) { var kk = k; O64($"object.cart_{k}", 48, () => pr.CartLoaded(kk)); }
        O64("object.barrow", 48, pr.Barrow);
        O64("object.draught_ox", 48, () => pr.Draught("ox")); O64("object.draught_horse", 48, () => pr.Draught("horse"));
        O64("object.horse_riding", 64, pr.Mount);

        // ---- template vehicles at their footprint (one cell = one metre = 32 px; sprite = footprint plus a 4 px margin, and the rise above the ground rectangle)
        var veh = new Vehicles(pal);
        foreach (var vs in Vehicles.Specs)
        {
            var spec = vs; var (vw, vh) = Vehicles.Size(spec); var (nw, nh) = Vehicles.SizeNS(spec);
            l.Add(new SpriteDef("vehicle." + spec.Id, SpriteKind.Object, "vehicles_32", "vehicles", vw, vh, () => veh.Draw(spec)));
            // facing north and south (the game mirrors the east one for west), and with a load on the bed
            l.Add(new SpriteDef($"vehicle.{spec.Id}.n", SpriteKind.Object, "vehicles_32", "vehicles", nw, nh, () => veh.DrawNS(spec, true, false)));
            l.Add(new SpriteDef($"vehicle.{spec.Id}.s", SpriteKind.Object, "vehicles_32", "vehicles", nw, nh, () => veh.DrawNS(spec, false, false)));
            if (spec.Id is "handcart" or "cart" or "ox_wagon" or "sledge")
            {
                l.Add(new SpriteDef($"vehicle.{spec.Id}_loaded", SpriteKind.Object, "vehicles_32", "vehicles", vw, vh, () => veh.Draw(spec, true)));
                l.Add(new SpriteDef($"vehicle.{spec.Id}.n_loaded", SpriteKind.Object, "vehicles_32", "vehicles", nw, nh, () => veh.DrawNS(spec, true, true)));
                l.Add(new SpriteDef($"vehicle.{spec.Id}.s_loaded", SpriteKind.Object, "vehicles_32", "vehicles", nw, nh, () => veh.DrawNS(spec, false, true)));
            }
        }

        // ---- P5-G (vehicle-designer 6): the river boats' templates (east, north, south) and every vehicle part in four drawn views and four baked diagonals, for designed vehicles
        var vp = new VehicleParts(pal);
        foreach (var bs in VehicleParts.Boats)
        {
            var spec = bs; var (bw, bh) = VehicleParts.Size(spec); var (nw2, nh2) = VehicleParts.SizeNS(spec);
            l.Add(new SpriteDef("vehicle." + spec.Id, SpriteKind.Object, "vehicles_32", "vehicles", bw, bh, () => vp.Boat(spec)));
            l.Add(new SpriteDef($"vehicle.{spec.Id}.n", SpriteKind.Object, "vehicles_32", "vehicles", nw2, nh2, () => vp.BoatNS(spec, true)));
            l.Add(new SpriteDef($"vehicle.{spec.Id}.s", SpriteKind.Object, "vehicles_32", "vehicles", nw2, nh2, () => vp.BoatNS(spec, false)));
        }
        foreach (var part in VehicleParts.Parts)
        {
            string id = part;
            foreach (var v in VehicleParts.Views) { string vv = v; l.Add(new SpriteDef($"vpart.{id}.{vv}", SpriteKind.Object, "vparts_32", "vehicle_parts", VehicleParts.Cell, VehicleParts.Cell, () => vp.Draw(id, vv))); }
            foreach (var v in VehicleParts.Diagonals) { string vv = v; l.Add(new SpriteDef($"vpart.{id}.{vv}", SpriteKind.Object, "vparts_32", "vehicle_parts", VehicleParts.Diag, VehicleParts.Diag, () => vp.Draw(id, vv))); }
        }

        AddMinis(l, pal, g, t);
    }

    /// <summary>Field / Command zoom art: 8x8 ground, wall-top and roof cells (field_8), and transparent minis (mini_16): people 8x16 in six team colours, trees 16x20, shrubs.</summary>
    private static void AddMinis(List<SpriteDef> l, Palette pal, Ground g, Terrain t)
    {
        var m = new Minis(pal);
        void C(string id, Func<PixelBuffer> f) => l.Add(new SpriteDef(id, SpriteKind.Terrain, "field_8", "field", 8, 8, f));
        void CellOf(string id, Func<PixelBuffer> src, string style = "noise", int variants = 1)
        {
            for (int v = 0; v < variants; v++) { int k = v; string name = variants > 1 ? $"{id}.{k}" : id; int seed = 5000 + Math.Abs(StableHash(name)) % 997; C("field." + name, () => m.Cell(src(), seed, style)); }
        }
        for (int v = 0; v < 4; v++) { int c = v; CellOf($"grass.{c}", () => t.Grass(c)); }
        CellOf("grass_long", () => g.GrassLong(0)); CellOf("meadow", () => g.Meadow(0));
        CellOf("forest_floor", () => g.ForestFloor(0)); CellOf("leaf_litter", () => g.LeafLitter(0));
        for (int v = 0; v < 2; v++) { int c = v; CellOf($"dirt.{c}", () => t.Dirt(c)); CellOf($"cobble.{c}", () => t.Cobble(c), "cobble"); }
        CellOf("sand", () => g.Sand(0)); CellOf("mud", () => g.Mud(0)); CellOf("marsh", () => g.Marsh(0)); CellOf("gravel", () => g.Gravel(0));
        CellOf("rock", () => g.Rock(0)); CellOf("scree", () => g.Scree(0)); CellOf("peat", () => g.Peat(0));
        CellOf("water_shallow", () => g.WaterShallow(0), "water"); CellOf("water_deep", () => g.WaterDeep(0), "water"); CellOf("ford", () => t.Ford(), "water");
        CellOf("bridge", () => t.Wood(), "planks");
        CellOf("farm.ploughed", g.Ploughed, "furrows"); CellOf("farm.sown", g.Sown, "furrows"); CellOf("farm.young", g.YoungCrop, "furrows");
        CellOf("farm.wheat_ripe", () => g.Ripe(false)); CellOf("farm.barley_ripe", () => g.Ripe(true)); CellOf("farm.stubble", g.Stubble); CellOf("farm.fallow", g.Fallow);
        CellOf("floor", () => t.Wood(), "planks");
        // wall tops: stone, timber and wattle (no roof over them: field walls, palisades, town walls)
        C("field.wall.stone", () => m.Cell((pal.R["stone"][1], pal.R["stone"][2], pal.R["stone"][3]), 5301, "cobble"));
        C("field.wall.timber", () => m.Cell((pal.R["wood"][1], pal.R["wood"][2], pal.R["wood"][3]), 5302, "planks"));
        C("field.wall.wattle", () => m.Cell((pal.R["dirt"][1], pal.R["dirt"][2], pal.R["dirt"][3]), 5303));
        foreach (var mat in Roofs.Mats) foreach (var part in new[] { "lo", "mid", "hi", "ridge" }) { var mm = mat; var pp = part; C($"field.roof.{mat}.{part}", () => m.Roof(mm, pp, 5400)); }

        void Mi(string id, int w, int h, Func<PixelBuffer> f) => l.Add(new SpriteDef(id, SpriteKind.Overlay, "mini_16", "mini", w, h, f));
        foreach (var role in Minis.Roles) foreach (var team in Minis.Teams) { var r = role; var tm = team; Mi($"mini.person.{role}.{team}", 8, 16, () => m.Person(r, tm)); }
        foreach (var sp in Minis.Species) { var s = sp; Mi($"mini.tree.{sp}.summer", 16, 20, () => m.Tree(s)); }
        Mi("mini.bush", 8, 8, () => m.Bush()); Mi("mini.rock", 8, 8, m.Rock);
        Mi("mini.block", 8, 8, () => m.Block(pal.R["wood"][2], pal.R["wood"][3]));
    }

    private static int StableHash(string s) { unchecked { int h = 17; foreach (char c in s) h = h * 31 + c; return h; } }
}
