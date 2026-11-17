# Ravensguard.Art

Art toolchain v1 (REDESIGN_PLAN §2.7). Sprites are written as code plus data, drawn with a small pixel-buffer API,
using one fixed palette. A C# port of the approved concept page `docs/redesign/concept/ravensguard-tileset.html`.
No NuGet dependencies (PNG is written with `System.IO.Compression`).

```
dotnet run --project tools/Ravensguard.Art -- bake     --out art/out            # PNG atlases + index.json
dotnet run --project tools/Ravensguard.Art -- validate                          # exit 1 on any problem
dotnet run --project tools/Ravensguard.Art -- preview  --out art/out/preview.png # 2x contact sheet
dotnet run --project tools/Ravensguard.Art -- rig      --out art/out [--out-game src/Ravensguard.Game/assets/art]  # rig atlas + rig_index.json
dotnet run --project tools/Ravensguard.Art -- rig-preview --out art/out/rig.png --scale 3 [--outfits soldier,peasant] [--cols walk:0,strike:1]
```
`validate` also bakes the rig in memory and checks it. `rig --out-game <dir>` copies `rig_32x64.png` and `rig_index.json` into the game's
`assets/art/` (`bake --out-game <dir>` copies the sprite atlases and `index.json`; CI bakes to a temp dir and fails if `assets/art` differs).

`--palette <path>` overrides `art/palette.jsonc`. `bake` validates first and refuses to write on failure.
`art/out/` is gitignored.

## Layout
- `PixelBuffer.cs` - RGBA buffer (0xAARRGGBB, 0 = transparent): `Set/Rect/Shade/Line/Ellipse/Outline`, `Scaled(k)` (nearest
  neighbour), `Recolor` (palette swap), plus `Noise` (hash/value-noise/mulberry32, bit-compatible with the JS).
- `Palette.cs` - loads `palette.jsonc`: named `colors` and named `ramps` (lists of colour names, dark to light).
- `Sprites/Terrain.cs, Objects.cs, Items.cs, People.cs, Autotile.cs` - sprite code. `Catalog.cs` lists every sprite id,
  atlas and declared size.
- `Atlas.cs` (bake + index + `palette.png` + `palette_ramps.json`), `Validator.cs`, `Preview.cs`, `Png.cs`.

## Output
`terrain_32.png`, `objects_32.png`, `objects_64.png` (stalls, cart, trees), `items_16.png`, `people_32x64.png`,
`terrain2_32.png`, `roofs_32.png`, `overlays_32.png`, `props_32.png`, `props_64.png`, `season_ramps.{json,png}`, `index.json` (`sprites[id] = {atlas, x, y, w, h}`), `palette.png`, `palette_ramps.json`.

Sprite ids: `terrain.grass.0`, `object.anvil`, `item.sword`, `person.smith`, `autotile.path_dirt.ne` ...

## Authoring
- Terrain is opaque 32x32; objects, items and people are transparent with an `out` outline pass.
- Characters are `PersonOpts` records (see `People.cs`): body base plus layers in draw order (linen, tunic/dress/apron/gambeson,
  mail, surcoat/robe/fur, coif/hood/helm/hat, weapons, carry, kite shield, wounds). Add a preset in `People.Presets()`.
  The posed front/back/side views come from the rig (below); `People.cs` stays the source of the static presets.
- Recolouring is a palette swap: `PixelBuffer.RampSwap(R["red"], R["gold"])` (see `person.soldier_swap_*`).
- Autotiles: `Autotile.Build` makes 16 edge-mask variants (N=1 E=2 S=4 W=8 = connected neighbour) per set; masks cover
  ends, straights, corners, T-junctions, crossings. Inner (diagonal) corners and damaged-edge states are not generated yet.
  Damaged states exist for cobble and stone wall.

## Validator
Per sprite: every pixel is fully transparent or an exact palette colour; size equals declared size; terrain has no holes;
objects/items/people have outline pixels and no opaque pixel borders transparency without an outline.
The concept's uncovered mortar gaps in `tWallStone` are filled with `s1`, so wall tiles are opaque.

## Rig frame baker (`Rig/`)
Characters are layered paper-dolls posed by a 2D rig (REDESIGN_PLAN §2.7). Nothing is hand-animated per frame.
- `RigModel.cs`: 15 bones (pelvis, torso, head, upper/lower arm, hand, upper/lower leg, foot, L/R). A `Pose` holds angles (swing, outward,
  elbow/knee, wrist/ankle, torso lean/twist, weapon angle). Forward kinematics runs in a tiny 3D body frame and is projected into one of three
  views (front, back, side facing east; west is the side view mirrored). Each bone gets a screen frame whose origin is snapped to the pixel grid;
  the lowest body point is planted on the ground row, so walk and jog bob by themselves.
- `RigLayers.cs`: part shapes per layer, drawn as bone-local boxes with shader functions (shading, mail checker, quilting, emblem, face). Pieces are
  sampled per pixel by inverse mapping (nearest, never blurred), so rotation never smears pixels. Layers in §2.6 order: `body` (skin, face, hair), `hose`,
  `linen`, `tunic`, `gambeson`, `mail`, `plate`, `surcoat`, `cloak`, `hood`, `helm`, `shield`, `sword`, `spear`. Weapons and the shield attach to the hand.
- `RigPoses.cs`: keyframes. idle 2, walk 4, jog 4, windup 2, strike 2, recover 1, block 1, hitreact 1, fall 3, in two stances (`blade`: sword hand,
  `pole`: right arm holds the spear). Listed but not posed yet (`todo` in the index): parry, stagger, crawl, carry, work.
- `RigBaker.cs`: bakes every layer per view x stance x action x frame, removes orphan pixels, and splits each layer into three depth planes
  (0 far, 1 body, 2 near) so that arm-behind-torso order is right in every view. Stack order is plane, then layer order; the cloak sits behind the
  body in front and side views, the shield behind it in back and side views. The outline is a separate ring sprite per outfit (`outline.<outfit>`,
  drawn first), because the ring depends on which layers are worn. Outfits: hero, soldier, spearman, knight, peasant, traveller.
- Recolour hook: `RigBake.Stack(outfit, view, action, frame, [(fromRamp, toRamp)])` swaps palette ramps (e.g. `green` to `red` for a tunic); the
  game can do the same per palette row from `palette_ramps.json`.

Output `rig_32x64.png` (trimmed frames, shelf packed) and `rig_index.json`:
`sprites[layer][view][stance][action][frame] = [[plane, x, y, w, h, ox, oy], ...]` (place at `(ox - anchorX[view], oy - 64)` from the feet; `anchorX`
is 16 front/back, 15 side). Also `order`, `outfits`, `actions` (frame counts) and `todo`.
Validation: palette-only pixels for every layer sprite; every outfit composite (6 outfits x 3 views x 20 frames) has an outline ring and no open
edges. idle, walk and jog must also stay inside the 32x64 frame (error). Combat and fall frames may touch the frame edge (counted as a warning):
a sword reach does not fit 32 px, and the last fall frame is a collapse onto the knees because a body lying flat is wider than the frame.

## Tileset v2 (Phase 1)
Sources: `Sprites/Ground.cs` (ground), `Walls.cs` (walls, features, floors), `Autotile2.cs` (shores, hedge, fences, walls, palisade), `Roofs.cs`, `Weather.cs`,
`Props.cs` (props, trees), `Seasons.cs`; the list is `CatalogV2.cs`. Atlases: `terrain2_32` (opaque 32x32), `roofs_32`, `overlays_32` (transparent, palette-only,
no outline rule: `SpriteKind.Overlay`), `props_32`, `props_64` (trees 64x80, stalls/carts/barrow 64x48 in 64x80 cells). Ids follow the existing scheme.
- Ground: `terrain.grass_long.*`, `meadow.*`, `farm.{ploughed,sown,young,wheat_ripe,barley_ripe,stubble,fallow}`, `mud`, `puddle`, `track.{h,v}*`, `sand`, `gravel`, `rock`,
  `scree`, `forest_floor`, `leaf_litter`, `peat`, `marsh`, `reeds`, `water_shallow`, `water_deep`, `ice`. Floors: `floor_{rush,brick,tile,plank_v,flag_irregular}`, `stairs_stone_*`.
- Walls: `terrain.wall.<mat>.<mask>` for mat = wattle timber stone brick palisade town, 16 masks each (N=1 E=2 S=4 W=8, set = neighbour is the same wall, name via
  `Autotile.MaskName`). Cells are full 1 m: south neighbour is wall -> all top surface; otherwise 8 px top strip over a 24 px face. Unconnected sides get a dark edge, so
  ends/corners/T/crossings read. `terrain.feat.<mat>.<door_closed|door_open|window_shuttered|window_open|window_glazed|arrow_slit|gate_closed|gate_open>` replace a straight
  east-west face cell (mask `ew`); the town wall has crenellated merlons on every open edge.
- Autotile sets (16 masks, `autotile.<set>.<mask>`): `shore_water` (grass/sand lip/shallows/deep), `bank_river` (mud bank), `track_packed`, `mud_patch`, `farm_field`, plus the line sets
  `hedge`, `fence_wattle`, `wall_field`, `palisade` (opaque, drawn over grass, 1 px outline, top + front face). v1 sets `path_dirt`, `road_cobble`, `pond_water` are unchanged.
- Roofs `roof.<thatch|shingle|slate|tile>.<n|e|s|w|ridge_h|ridge_v>`: n/e/s/w descend toward that side (ridge opposite); ridge_* is a one-cell-wide roof. Field/Command zoom and the world map only.
- Overlays: `overlay.snow.0-5` (0,1 full cover, 2,3 patchy, 4,5 dusting; tileable), `overlay.puddle.0-2`, `overlay.splash.0-3`, `overlay.rain.0-3`, `overlay.snowfall.0-3` (4-frame loops, 8 px per frame),
  `overlay.fog.0-1` (dithered, draw at ~40-60 % alpha). Lightning is a full-screen flash, no sprite.
- Props: see `CatalogV2.cs`; trees `object.tree.<oak|ash|birch|pine|apple>.<summer|autumn|winter>` (autumn is the summer sprite through the season swap, winter is bare / snow-laden pine),
  `*_snow` variants for haystack, woodpile, boulder, bush, sheaves, headstone, barrels.

### Seasons (palette ramp rows)
Sprites are authored in the summer colours. `season_ramps.json` / `season_ramps.png` (also copied to the game's `assets/art/`) list, per family and season, four colours; entry `i` of the
summer row maps to entry `i` of the target row. Families (row = familyIndex*4 + seasonIndex, seasons spring=0 summer=1 autumn=2 winter=3):

| family | rows | used by |
|---|---|---|
| grass 0 | 0-3 | all grass-based terrain and autotile grass bases, hedge-line grass, meadow, long grass, bog tussocks |
| foliage 1 | 4-7 | oak, hedges, bushes (`object.bush_*`) |
| leaf 2 | 8-11 | ash, birch, apple |
| conifer 3 | 12-15 | pine (autumn = winter = a duller green; snow comes from the `_winter` sprite / snow overlay) |

How the game picks a row: take the season from the calendar (spring Mar-May, summer Jun-Aug, autumn Sep-Nov, winter Dec-Feb). Use ONE merged lookup (all families at once, keys are the summer
colours) in the shader or when baking an atlas copy. For smooth transitions blend by tile: give every tile/tree a stable hash `h` in [0,1) and use the next season's row once the
fraction of the season elapsed exceeds `h` (so autumn creeps in tile by tile). Winter also switches tree sprites to `.winter`, swaps `farm.*` crops to `stubble`/`ploughed`, shows
`overlay.snow.*` on top (full on grass, dusting on roads and fields), and recolours water with the `water -> ice` ramp swap (or uses `terrain.ice.*`). `Seasons.Apply(pal, sprite, season)` does the same in C#;
`scene` renders the demo.

`dotnet run --project tools/Ravensguard.Art -- scene --out art/out` writes `scene-summer.png`, `scene-autumn.png`, `scene-winter.png` (a village corner built from the baked sprites).
`preview --set v2 [--filter prefix,prefix] [--scale 4]` shows only the new atlases.

## Not yet
Remaining actions (parry, stagger, crawl, carry, work), wound overlays on the rig, hair/skin variants beyond the recolour hook, horses and large
animals, per-faction palette-row export for the shader, hat layer.
