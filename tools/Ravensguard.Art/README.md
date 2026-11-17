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
`assets/art/` (the other atlases and `index.json` are still copied by hand after `bake`).

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
`index.json` (`sprites[id] = {atlas, x, y, w, h}`), `palette.png`, `palette_ramps.json`.

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

## Not yet
Remaining actions (parry, stagger, crawl, carry, work), wound overlays on the rig, hair/skin variants beyond the recolour hook, horses and large
animals, per-faction palette-row export for the shader, hat layer.
