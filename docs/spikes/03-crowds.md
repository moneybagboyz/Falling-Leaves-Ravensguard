# Spike 3: crowds (2,000 soldiers)

Scene: `res://Spikes/CrowdSpike.tscn` (`src/Ravensguard.Game/Spikes/CrowdSpike.cs`, `crowd.gdshader`).
Run: `xvfb-run -a -s "-screen 0 1920x1080x24" $G --rendering-driver opengl3 --resolution 1920x1080 --path src/Ravensguard.Game res://Spikes/CrowdSpike.tscn -- --bench --seconds=12 [--layers=N] [--phase=0..4]`
Without `--bench` the scene runs interactively; keys 1-5 pick the zoom/ground phase.

## What was built
- 2 armies x 1,000 soldiers, 512x320 m ground (163,840 tiles) from `terrain_32.png`.
- One `MultiMesh` (Transform2D, custom data) per layer group. Per-instance custom data: atlas frame, palette row, wound bits + moving flag, walk phase.
  The canvas_item shader does the palette swap (strongly-red pixels remapped through a 4-level ramp row: red/blue/gold/green), a bob + head-sway walk animation in the vertex shader, and wound tints (torso blood, hurt darkening, leg wound).
- Fake 20 Hz sim in C# on struct-of-arrays with a 2 m spatial hash and separation, jitter and lane keeping; render positions interpolated per frame.
- Every frame: interpolate, `Array.Sort(keys, idx)` by y, rewrite the instance buffer in sorted order, upload (`MultiMesh.Buffer`).
- Layers: the benchmark uploads 3 buffers (body, armour, weapon groups); only layer 0 is visible, so the other two measure upload cost but not extra GPU draw cost.
- Ground, two ways: a `TileMapLayer`, and "chunk images" (each 32x32-tile chunk baked to 1024 px, then resized to 256 px for Field and 64 px for Command; 160 `Sprite2D`s).
- Benchmark: 5 phases of 2.4 s (12 s total), first 5 frames of each phase dropped.

## Results (container: llvmpipe software GL under Xvfb, 1920x1080, 3 layers)
| Phase | frame avg | frame p99 | sort | fill | upload (3 layers) |
|---|---|---|---|---|---|
| Street 32px/m, TileMapLayer | 25.1 ms | 31.2 ms | 0.200 ms | 0.054 ms | 0.205 ms |
| Field 8px/m, TileMapLayer | 54.2 ms | 72.9 ms | 0.104 ms | 0.061 ms | 0.229 ms |
| Field 8px/m, chunk images | 24.3 ms | 36.8 ms | 0.097 ms | 0.055 ms | 0.201 ms |
| Command 2px/m, TileMapLayer | 144.6 ms | 170.8 ms | 0.093 ms | 0.055 ms | 0.202 ms |
| Command 2px/m, chunk images | 20.8 ms | 30.8 ms | 0.100 ms | 0.060 ms | 0.233 ms |
| All | 31.8 ms | 160.3 ms | 0.124 ms | 0.057 ms | 0.215 ms |

- **Sim tick** (2,000 soldiers, hash + separation + jitter): avg 0.46 ms, p99 0.90 ms, max 1.7 ms. Budget is 25 ms.
- **Y-sort + buffer fill + upload** (the CPU side of rendering): about 0.4 ms avg, p99 about 1 ms total. With 1 layer upload drops to 0.09 ms.
- Setup: TileMapLayer fill 137 ms. Chunk bake 9.8 s for 160 chunks x 3 LODs (per-tile `BlitRect` interop); this must be done by the art/world pipeline offline or incrementally, not at load.
- Screenshots (not committed): Street shows team-coloured, wounded, bobbing soldiers y-sorted correctly; Field and Command read as coloured columns.

## Reading the numbers
- Frame time here is dominated by llvmpipe rasterising the ground, so absolute FPS is pessimistic and not meaningful. The **GPU side is unverified**.
- What it does show: ground draw cost is by far the largest term at low zoom (TileMapLayer at Command: 145 ms vs 21 ms with chunk images, on a CPU rasteriser; on a real GPU the gap will be smaller but the draw-call/quad count difference of ~100x remains). Soldiers add very little CPU work.
- Not tested: real art for Field/Command (8x16 mini-sprites, banner blocks), overdraw of three visible layers, outline rings, lighting, a real combat sim, the per-layer equipment art.

## Decision: GO
The MultiMesh crowd path fits the plan. The sim-adjacent and render-feeding CPU work for 2,000 soldiers (about 0.5 ms tick + about 0.4 ms sort/upload) is under 5% of the 25 ms and 16.6 ms budgets, leaving room for real combat logic. Reshape only the ground:
1. Use TileMapLayer at Close/Street, **pre-baked chunk images at Field/Command** (not a TileMapLayer scaled down). Bake offline or time-sliced in a worker; per-tile Blit from C# is too slow at load.
2. Draw Field/Command soldiers from their own small atlas (the plan already says so) rather than the 32x64 sprite scaled down, which shimmers with nearest filtering.
3. Keep all instance data in one flat `float[]` per layer group, sorted with `Array.Sort(keys, idx)`; consider sorting only on y changes at Command zoom, or by y-bucket (counting sort) if soldier counts grow.

## Needed on the reference PC (RTX 3060-class) to close this spike
- Run the same benchmark with a real GPU (Windows/Linux, Compatibility renderer and Forward+ for comparison) and confirm frame p99 <= 16.6 ms for all 5 phases, with all 3 layers visible.
- Test 3 to 6 visible layer groups with overdraw, plus outline rings for selected units and wound overlays as extra layers.
- Measure chunk-image VRAM: 256 px LOD is 256 KB per chunk (about 40 MB for 160 chunks); a 1 km^2 bubble needs about 1,000 chunks, so budget about 250 MB at the Field LOD or stream LODs.
