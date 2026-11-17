# Ravensguard.Art

Art toolchain v1 (REDESIGN_PLAN §2.7). Sprites are written as code plus data, drawn with a small pixel-buffer API,
using one fixed palette. A C# port of the approved concept page `docs/redesign/concept/ravensguard-tileset.html`.
No NuGet dependencies (PNG is written with `System.IO.Compression`).

```
dotnet run --project tools/Ravensguard.Art -- bake     --out art/out            # PNG atlases + index.json
dotnet run --project tools/Ravensguard.Art -- validate                          # exit 1 on any problem
dotnet run --project tools/Ravensguard.Art -- preview  --out art/out/preview.png # 2x contact sheet
```
`--palette <path>` overrides `content/base/art/palette.jsonc`. `bake` validates first and refuses to write on failure.
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
  Front view only for now; back/side views come with the rig baker.
- Recolouring is a palette swap: `PixelBuffer.RampSwap(R["red"], R["gold"])` (see `person.soldier_swap_*`).
- Autotiles: `Autotile.Build` makes 16 edge-mask variants (N=1 E=2 S=4 W=8 = connected neighbour) per set; masks cover
  ends, straights, corners, T-junctions, crossings. Inner (diagonal) corners and damaged-edge states are not generated yet.
  Damaged states exist for cobble and stone wall.

## Validator
Per sprite: every pixel is fully transparent or an exact palette colour; size equals declared size; terrain has no holes;
objects/items/people have outline pixels and no opaque pixel borders transparency without an outline.
The concept's uncovered mortar gaps in `tWallStone` are filled with `s1`, so wall tiles are opaque.

## Not yet
Rig frame baker, back/side views, horses and large animals, per-faction palette-row export for the shader.
