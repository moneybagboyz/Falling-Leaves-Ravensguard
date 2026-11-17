# Spike 5: levels and pathfinding

Code: `src/Ravensguard.Sim/World/LevelGrid.cs`, `LevelPathfinder.cs`. Tests: `tests/Ravensguard.Sim.Tests/Spikes/LevelsSpike.cs` (5 tests).

## Model
- `LevelGrid`: W x H x z (-2..+2 in the spike), byte passability per cell, `Portal` list (stairs, ladder, trapdoor, shaft, cave mouth) linking two cells, per-chunk-level (32 x 32) revision and dirty flags.
- `LevelPathfinder.FindPath`: exact A* over cells + enabled portals, 8-way with no corner cutting, integer costs (10/14), deterministic tie-break on cell index. Portal cost is clamped to at least the octile distance so the xy heuristic stays admissible.
- `FindPathAbstract`: HPA*-style. Each chunk-level is flood-filled into zones; zone edges are chunk borders and enabled portals. A zone-level A* gives a corridor (plus one neighbour ring) that restricts the cell search, and is also an instant reachability check.
- Invalidation: `SetPortalEnabled` (ladder pulled up, trapdoor barred), `CollapseFloor` (closes cells and disables only portals with an endpoint inside), `AddPortal` and `SetOpen` mark only their chunk-levels dirty; the next query re-zones only those.

## Results
| Check | Result |
|---|---|
| Cellar (z-1) to tower top (z+2) | 3 stair portals, 55 cells, cost 698; abstract path has the same cost |
| Ladder pulled up | reroutes via stairs (higher cost); with stairs also barred: no path (exact and abstract agree); re-lowering restores it |
| Floor collapse on z+1 | only the portal inside the hole is disabled; 2 of 20 chunk-levels re-zoned; upper floors become unreachable until a new portal is added, then re-plans through it |
| Determinism | identical path hashes across independent rebuilds (exact and abstract) |
| 200 random paths, 256 x 256 x 5 (327,680 cells, 256 portals, 20% rubble on z0) | exact A* 913 ms (4.6 ms/path); abstract 480 ms (2.4 ms/path); 199/200 reachable, 153 cross-level; worst abstract/exact cost ratio 1.04 |
| Abstraction build (full) | 30-55 ms; after invalidating one portal: 2 chunk-levels re-zoned, ~5 ms |

(Debug build, single thread, one dev machine. Numbers will vary; budgets in the test are deliberately loose.)

## Limits and honest caveats
- The abstract search expands only ~20% fewer nodes than exact A* here (1.25 M vs 1.59 M over 200 queries) because corridors are wide and zones are large relative to the rubble map. The time win is real but not dramatic. A true HPA* with entrance nodes and cached intra-chunk portal-to-portal costs would cut expansion much further; not needed to prove feasibility.
- Edge rebuild after any change rescans all chunk borders (cheap at 256 x 256, would need to be incremental for a 1,000 x 1,000 chunk region; the data is already per-chunk).
- Dense levels are stored as flat arrays; the real game stores only non-empty levels (sparse), which is a storage change, not an algorithm change.
- Not covered: per-level flow fields for crowds, doors/keys, agent size, falling/jumping edges, cost of searching through unloaded chunks.

## Decision: GO for Phase 1
Portals as ordinary graph edges with enable flags, chunk-level dirty tracking and zone abstraction work, are deterministic, re-plan correctly after ladders/collapses, and are well inside budget (2-5 ms for a worst-case cross-map search in Debug). Phase 1 should: (1) replace the whole-border edge rebuild with per-chunk incremental edges, (2) add cached entrance-to-entrance costs for true HPA*, (3) run searches off the sim thread with a per-tick expansion budget, and (4) build flow fields per level on top of the same grid.
