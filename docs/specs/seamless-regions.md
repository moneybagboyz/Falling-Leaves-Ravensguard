# Seamless regions: ids, gates, neighbour generation, levels, paging, determinism

Plan refs: §1B (the whole of it), §1 (region = 32 x 32 km = 16 x 16 cells; clock), §2.2 (chunks snapshotted on first visit;
ChunkDeltas), §2.3 (LODBridge), §2.4 and §1B Determinism (a region's content depends only on seed, content and history, never
on visit order), §2.11 (budgets: <= 1 MB/region save, <= 150 MB after 10 years near home, <= 400 MB crossing the continent,
<= 9 detailed regions ~300 MB, neighbour generation <= 3 s on a worker, no frame hitch), §4A.4 (roads: highways link towns;
fords/bridges), §5 Phase 2 (seamless continent contents and done-when: walk across three regions in a straight line, any
generation order gives the same hashes, on Windows and Linux).
Phase 1 code this builds on: `RegionGenerator.Generate(cx, cy)`, `RegionBuilder.Roads` (gates from
`WorldHistory.TradeRoutes`), `Region.CellX/CellY`, `Region.ComputeHash`, `Population.ComputeHash`, `ChunkStreamer`.

## 1. Purpose
Make the 16 x 16 grid of regions behave as one continuous world: any region can be built on demand, identically; borders have
matching terrain, rivers, roads; records know which region they belong to and can cross; tick cost and memory follow distance
to the player; saves page regions in and out.

## 2. Data

### 2.1 Addressing (`Sim/Worlds/RegionGrid.cs`)
- `RegionId` = `byte`, `ry x 16 + rx`, `rx, ry in 0..15`. Region origin cell `(rx x 16, ry x 16)`, origin metres `(rx x 32,000, ry x 32,000)` (world coordinates, continent metres). Region-local metres 0..32,000 (Phase 1). Global tile = `region tile + region origin tile (rx x 500, ry x 500)`. Chunk id extends to `(RegionId, cx, cy)` (32 m chunks, 1,000 x 1,000 per
  region).
- Neighbours: 8-connected `(dx, dy)` ids; edges of the continent have fewer.
- **Embark windows snap to the grid (decision O1, conflict C6):** `RegionGenerator.Generate(cx, cy)` requires `cx % 16 == 0 && cy % 16 == 0`; the embark map scores 256 candidate regions (`EmbarkSite.ScoreCore` unchanged, window origin multiples of
  16).
- `EntityId`: origin region in counter bits 48-55 (overview 2.2); every table has a `Region` column (current).

### 2.2 `RegionState` and `RegionManager`
`RegionState`: `Id`, `Level`, `Region` (Phase 1 terrain/settlements), `PersonTable`, `HouseholdTable`, `FieldTable`,
`ParcelWork`, `BusinessTable`, `StockTable` (owners of this region), `AccountTable`, `MarketBook[]`, `HerdTable`,
`ResourceLayer`, `CaravanTable` (homed), `TreasuryTable`, `Allocators` (per-kind serials), `Mailbox` in/out, `Dirty` flags,
`ChunkStore`. `RegionManager`: `RegionState?[256]`, `RegionSummary[256]` (continent spec), generation queue, residency
policy, hero/bubble tracking, saves.
`RegionLevel`: `L0 Live` (contains a bubble centre), `L1 Detailed` (generated, Chebyshev ring 1 of a bubble's region), `L2 Daily` (resident records-only, ring 2), `L3 Sleeping` (generated, paged to disk, summary only), `L4 Abstract` (never
generated).
Ring of region `r` = `min over active bubbles b of max(|rx - bx|, |ry - by|)` (Chebyshev distance to the nearest bubble's
region); the hero's region used for the ring changes with hysteresis (3.5).

### 2.3 Border gates (canonical; fixes conflict C5)
`BorderGate` (`Sim/Worlds/BorderGates.cs`): computed once per world from `WorldHistory.TradeRoutes` and cached with the
continent: for every consecutive cell pair `(cA, cB)` of a non-abandoned route whose cells lie in different regions:
```
key        = (min(cA, cB), max(cA, cB), routeId)
point      = segment centre(cA)-centre(cB) intersected with the shared region border line (continent metres, integer, floor)
tile_row/col along the border = floor(point / 64 m)       (global tile; the same row/col on both sides)
class      = Highway if both route endpoints are towns, Road if one is, else Track     (history settlement classes)
bearing    = unit vector of the cell segment (integer 1/1024 units)
```
De-duplication: gates on the same border within 1,500 m along it merge into the one with the lowest `routeId` (ordering is
global, never per region). **No dry-land nudge** (Phase 1 `GateTile` nudge depended on one side's terrain and is removed): if
the gate tile is water the road builder places a `RoadCrossing` (ford/bridge) there. Each side builds a road node of kind
`Gate` at its own last/first tile of the border row/col, a polyline segment of 200 m along `bearing` from the gate, and then
routes to its settlements. The gate list per border side is `Gates[regionId][side]` (sorted by tile coordinate), shared by
both neighbours, used by caravans for pairing (caravans spec).
Rivers: `Continent.Rivers` cells crossing a border produce `(border tile, width, depth)` from the continental `RiverFlow` by
one function of the cell pair; Phase 1 river polylines starting at an edge (`WaterEnd.Edge`, `Continues`) must start at those
points.

### 2.4 Content
`content/base/world.json`: `border_trigger_m` 4000, `border_trigger_hysteresis_m` 8000, `gate_merge_m` 1500, `gate_segment_m`
200, `ring1_hourly`, `ring2_daily`, `max_detailed_regions` 9, `max_resident_regions` 25, `neighbour_gen_budget_ms` 3000,
`catchup_budget_ms` 500, `level_hysteresis_m` 1000, `page_out_after_days` 15.

## 3. Rules

### 3.1 Determinism of generation
`GenerateRegion(seed, contentHash, continent, history, regionId) -> RegionState(day 0)` is a pure function: RNG keyed `(seed, regionId, stream)` only; it reads continent/history (immutable after world creation); never reads another region's generated
data except through the **seam interface** (gates, river entry points, border heights, settlement exclusion margins) which
are pure functions of the continent. The day-0 content is the end-of-history state (`Population.NowDay`). Test: generate any
set of regions in any order, in parallel or sequence, on both OSes: identical `Region.ComputeHash`, `Population.ComputeHash`,
`EconomyInitHash` per region (E2 id scheme: serials per `(regionId, kind)` allocated in generation order inside the region
only).
**Seam rules**: no town/village lot or field within 64 m of a border (settlement placement margin 1,200 m for towns, 400 m
for villages from region edges; fields and parcels clipped at the border; a settlement whose history cell is near the edge is
nudged inward by <= 1,200 m deterministically). Heights match by construction (Phase 1 test extended to 4 corners). Biome,
fertility, forest at the seam come from the same continental cells (no per-region blending).

### 3.2 Activation = Generate + CatchUp
First activation at game day `D`: `RegionState = GenerateRegion(...)` (day 0) then `CatchUp(state, summary, D)`;
re-activation after paging: `Load(section)` then `CatchUp(state, summary, D - LastDetailDay)`. One deterministic function:
1. **Ageing and survival**: `BirthDay` is fixed, ages grow automatically. For each living person (ascending id) draw survival
   over the gap with the vital table (stream 0x2B0, key person id): `S = product of (1 - eff_q(age))` year by year
   (precomputed table); dead -> `Status Dead`. Households whose heads die inherit (population 3.8).
2. **Births**: for each full year of gap, `births = floor(Pop x 2.58% x food_mult)` assigned to married women (ascending id,
   hash order), newborn ids from the region allocator in (year, mother) order; newborns survive to now by the same draws.
3. **Scale to the summary**: target `Pop = summary.Pop`; if records are above/below by > 1%, remove/add whole households
   (ascending hash of household id for removal; template households from the largest-vacancy settlement for additions) until
   within 1%.
4. **Food, coin, prices**: household grain stocks scaled so region food = `summary.FoodMeasures` by class weights; accounts
   scaled to `summary.CoinTotal` (largest-remainder, ascending id); `Ref` per settlement set from `summary.CoinPerPerson`
   (money 3.5); market book stocks set to the target cover implied by `summary.PriceIdx`.
5. **Calendar state**: fields set to the stage the calendar implies (ploughed fraction by day, crops sown, ripe...),
   `Fertility` unchanged except closed-form drift of the gap (farming ledger); herds scaled by 3.3 growth; businesses open;
   treasuries get the summary treasury coin.
6. **News**: summary `News` ring becomes `Rumour` seeds; no chronicle entries are invented for the gap except one "chronicle
   gap" marker (`CauseKind.Other`, text "years passed unseen") and famine/war items from the ring.
Budget: CatchUp <= 500 ms for up to 100 game years (O(people) with <= 4 passes). The summary is then **retired**: C1 exports
from records each season.

### 3.3 Neighbour generation ahead of the hero
Trigger evaluation 2 Hz in the `RegionManager` for each active "pioneer" (hero, active squads, armies, ships):
- For each of the 4 sides: if `distance to that border < border_trigger_m` (4,000 m) and the neighbour is `L4` or `L3`:
  enqueue it with priority `distance`.
- Diagonal neighbour: enqueue when `distance to the shared corner point < 4,000 m` (Euclid).
- At most 2 generations run concurrently on worker threads (ring-1 neighbours first, diagonals later); each takes <= 3 s in
  the background (stages: seam data -> terrain -> water -> settlements -> people -> fields -> sites -> roads/gates ->
  catch-up); results are applied to the main thread in <= 1 ms slices (building `RegionState` objects is on the worker; the
  attach is a pointer swap at the barrier); chunk generation remains time-sliced at 4 ms/frame.
- Cancel if the pioneer moves > 8,000 m away (hysteresis) and the job is < 50% done; keep finished regions (they become
  L1/L2/L3 by ring).
- A pioneer faster than the queue (fast travel, gallop 12 m/s covers 4 km in 5.5 min, generation 3 s) never outruns it; a
  teleport lands in a freshly generated region (blocking only the teleport target, with a loading cover permitted *only* for
  teleport/load, not for walking). If a region is still not ready when a chunk at its border would stream, the hero's speed
  is capped to 30% over the last 200 m (never a screen; logged as a perf event).
- The seam is usable on arrival: chunk generation on both sides reads the same gate list and river entry points, and
  `ChunkStreamer` resolves chunks by global coords across regions.

### 3.4 Region ticks by level (see abstract-tick spec)
| Level | Ticks | Records | Memory |
|---|---|---|---|
| L0, L1 | hourly abstract tick (H0-H7) + seasonal S | full + chunk caches (L1 keeps only the chunks near gates and nearest the hero when memory pressure) | <= 33 MB each, <= 9 total (300 MB) |
| L2 | daily kernels at hour 0 (24 slices at once, markets once) + seasonal S; no live | records only (no chunks) | <= 25 MB each, <= 16 (400 MB) |
| L3 | continent tick only (summary) | none (on disk) | ~280 B summary |
| L4 | continent tick only | none | ~280 B summary |
Resident cap `max_resident_regions = 25`; if more would be needed the farthest L2 is paged out first (ties highest id).
Page-out: final C1 export, write the section on a worker (<= 500 ms), drop. Page-in: load section (<= 1 s) + CatchUp (<= 0.5
s), flagged `L2` then promoted by ring.

### 3.5 Level hysteresis
The hero's *ring region* changes only when the hero is more than `level_hysteresis_m = 1,000` m inside the new region (so
oscillating on the border changes nothing). Level changes are applied at the next barrier. L1 -> L2 demotion needs the region
to have been out of ring 1 for 15 game days.

### 3.6 Crossing records
People, households, caravans, migrants, armies, ships are **records that move**: the origin region writes a `Departed`
tombstone and the destination `Mailbox` receives the record at the barrier; mailboxes apply in ascending `(source region id, record kind, record id)`. The record's id keeps its origin region bits; the `Region` column becomes the destination. Entities
in a live bubble that straddle the border continue unchanged (position function and chunk coordinates are global); ownership
flips at the next barrier. Market commands go to the region owning the market (`MarketBook.Region`); a hero standing at a
border market uses the nearer region.

### 3.7 Persistence
`WorldFile` = `WorldSection` (continent, history, content manifest, summaries[256], id allocators, global chronicle, clock,
hero) + `RegionSection[id]` for each generated region (`Level`, `Version`, `ContentHash`, `LastDetailDay`, MessagePack blob
of records, economy tables, `ChunkStore` (first-visit snapshots), `ChunkDelta`s). Only dirty sections are rewritten on save
(dirty flags per region); L3 sections are copied unchanged. `SaveMigrator` runs per section. Budgets: records ~1.0 MB +
economy ~0.4 MB per detailed region, chunks 2-6 KB each; 10 years near home < 150 MB (9 regions x 1.4 MB + chunks < 30 MB +
world 5 MB), crossing 20+ regions < 400 MB; save <= 100 ms hitch (clone dirty tables at a barrier, write on a worker); load
<= 10 s (world + hero region + ring 1 sections only, the rest lazily).

### 3.8 World map
Reads `RegionSummary` for the whole continent (population, prosperity, realm, wars, fog for unknown) and the generated
`Region` details (settlement names, roads) where `Level <= L3`; unvisited regions show continental data only. Zoom to a
generated region reads the persisted region labels.

## 4. Tick placement
- **Pioneer evaluation**: 2 Hz on the main thread (cheap: 4 distances + 4 corners per pioneer).
- **Generation/catch-up/page I/O**: worker threads, results applied at barriers.
- **Mailboxes**: applied at barrier B(h) before workers start (arrivals) and after (departures).
- **Region levels**: recomputed at barrier (rings with hysteresis); L-transition work scheduled on workers.

## 5. Determinism
All region-ordered loops ascend by region id; allocators per `(region, kind)`; generation independent of residency; `CatchUp`
consumes only `(state, summary, D)`; mailbox application order fixed; page-out/page-in is byte-identical for records
(round-trip test) so paging never changes results. Windows/Linux hashes compared in CI for 6 sample regions (corners, centre,
coast).

## 6. Interactions
Continent tick (summaries, activation requests), abstract tick (levels, mailboxes), caravans/migrants (gates, mailboxes),
population/households (catch-up, tombstones), markets (per-region books, border market), farming/resources (region-local
layers; seam margins), persistence/chunks, world map UI, weather (`RegionWeather` is continental and region-independent),
history/chronicle (gap markers).

## 7. Edge cases
- Continent edge/sea: regions with < 1% land generate as `Empty` (no people) and are never generated for play unless an
  expedition reaches them; coastal regions keep partial maps.
- A settlement exactly on a region seam in history: assigned to the region containing its cell centre; its houses may
  overflow the border only into the sea or parcels clipped; test `NoLotsOverBorder`.
- Gate on water: ford/bridge inserted; bridge toll owner = the region's fief containing the gate tile (the fief with the
  lowest id if the fiefs of both sides differ).
- Save made mid-generation: the job restarts after load (generation is deterministic).
- Mod changes content between sessions: region content hash mismatch -> region sections keep their saved state (ChunkDelta
  principle, plan §2.2); only newly generated regions use the new content (report lists it).
- Memory pressure: demote L1 to L2 early; never page L0.


## Decisions made in this spec (collected in 00-overview.md section 10.4)
- **S1** Decision: Embark windows snap to the 16-cell grid. Reason: neighbours tile exactly (O1).
- **S2** Decision: Border gates are pure functions of the continental route and cell pair; no terrain nudge; de-dup by route id. Reason: both neighbours must agree (C5).
- **S3** Decision: Ring 0-1 resident detailed, ring 2 resident records-only, beyond paged with a summary. Reason: resolves the plan's memory vs daily-tick wording (E8).
- **S4** Decision: Activation is `Generate` (day 0) + `CatchUp` to the summary; page-in uses the same CatchUp. Reason: order-independent content with elapsed time.
- **S5** Decision: Neighbours are queued at 4 km from a border or corner, at most 2 generations at a time, cancelled beyond 8 km. Reason: plan: about 4 km; budget and hysteresis.
- **S6** Decision: Ring changes need 1,000 m inside the new region. Reason: no level thrash at a border.

## 8. Golden tests
| id | Case | Expected |
|---|---|---|
| G-S1 | Grid math: continental cell (37, 130) | region `(130/16 x 16 + 37/16)` = **130**; origin cell (32, 128); id 130 -> rx 2, ry 8; neighbours N 114, NE 115, E 131, SE 147, S 146, SW 145, W 129, NW 113; id 0 has 3 neighbours |
| G-S2 | Embark snap: window (37, 130) | snapped to (32, 128); embark scoring evaluates 256 windows |
| G-S3 | Gate: route cells A (31, 140) region 129, B (32, 140) region 130 | border x = 64,000 m; crossing y = 281,000 m -> global tile row 4,390 -> **local row 390 in both regions**; tile columns 499 (region 129) and 0 (region 130); both sides compute identical key, class and bearing; no terrain dependence |
| G-S4 | Trigger: hero 3,900 m from the east border vs 4,100 m | 3,900: east neighbour queued; 4,100: not; hero (3.2 km E, 3.8 km S): E and S queued, SE corner distance 4.97 km: not; hero (2.0 km, 3.0 km): corner distance 3.6 km: SE queued |
| G-S5 | Rings: hero in region 130 | ring 0: 1 region; ring 1: 8; ring 2: 16 (interior); resident cap 25; memory `9 x 33 + 16 x 25` = 697 MB <= 700 target |
| G-S6 | Hysteresis: hero oscillates across the east border of region 130 into 131 at depths 0-900 m | ring region stays 130 (no level changes); at 1,001 m inside 131 the ring region becomes 131 and levels update once |
| G-S7 | Order independence: generate regions {129, 130, 131} in 20 random permutations, parallel and serial | identical `Region.ComputeHash`, `Population.ComputeHash`, `EconomyInitHash` per region; person ids of 130 identical; Windows == Linux |
| G-S8 | Gate de-dup: three routes crossing one border within 1,200 m with ids 12, 7, 30 | one gate kept, route 7; identical on both sides regardless of which region builds first |
| G-S9 | Seam geometry | no lot, field or parcel crosses a border; roads cross at the gate row/col with a 200 m aligned segment; river at the seam has equal width/depth and position on both sides (0 cm tolerance) |
| G-S10 | Catch-up survival: 1,000 persons aged 40 at day 0, activated at day 660 (11 game years) | expected survivors `1,000 x 0.98807^10 x 0.97044` = **861** (+-12); births per year 645.8 x food_mult scaled to pop; final `Pop` within 1% of `summary.Pop` |
| G-S11 | Page round trip | `RegionSection` write -> read -> write is byte-identical; after page-in and before catch-up the records hash equals the hash at page-out |
| G-S12 | Mailbox order: migrants from 129 and 131 and a caravan from 146 arrive at 130 in one barrier | applied 129, 131, 146; ids/serials identical in every run |
| G-S13 | Budgets: neighbour generation (reference runner) | <= 3 s wall on a worker, main-thread attach <= 1 ms, no frame > 16.6 ms during it; catch-up <= 500 ms; page-out <= 500 ms, page-in <= 1.5 s |
| G-S14 | Walk test: hero crosses 3 regions in a straight line at jog and at 8x | no stop, no frame over budget, neighbour ready >= 60 s before the border at jog and >= 4 min at 12 m/s gallop; road and river continue at each border; hashes unchanged by the walk |
| G-S15 | Save size | detailed region section <= 1.4 MB; 9 regions + chunks < 30 MB; crossing 20 regions < 100 MB for these sections |

## 9. Soak invariants
1. 100-year soak with the hero walking a scripted loop across 9 regions: no region drift in coin audit (global `sum`
   conserved), people count conserved across crossings (departed + arrived = moved), zero duplicate ids.
2. Regions generated, paged out, paged in 50 times each keep records hash identical across a no-time-advance round trip;
   after a 100-year gap the catch-up result is deterministic for the same summary.
3. Peak resident memory <= 1 GB for 25 resident regions plus chunks; total game memory <= 6 GB after a 10-year crossing game.
4. L2 daily tick <= 250 ms per region p99; L1 hourly <= 150 ms p99; the sum of worker time per game hour at 9 L1 regions <=
   1.35 s CPU (<= 340 ms wall on 4 workers).
5. Determinism hashes of the 6 reference regions equal on Windows and Linux in CI.

## 10. Notes: part A implementation (grid, gates, rivers, neighbour generation, cross-border streaming)
Decisions taken while building part A (code in `Sim/WorldGen/Region/{RegionCoord,BorderGates,RegionBuilder.Rivers}.cs`, `Sim/Worlds/RegionManager.cs`, `Sim/WorldGen/Chunks/ChunkStreamer.cs`; tests in `tests/Ravensguard.Sim.Tests/Seamless/`). Part B (records keyed by region, per-region ticks, paging, `RegionSection` saves) is not started.
- **N1 Embark grid (O1).** `RegionCoord` (`X, Y` in 0..15, id `Y * 16 + X`) lives beside the region code, not in `Sim/Worlds/RegionGrid.cs`. `RegionGenerator.Generate` throws unless both origin cells are multiples of 16; `EmbarkSite.Score(c, h, cx, cy)` snaps any cell to its square and `AutoPick` scores the 256 squares (ties to the lowest id). Because a square cannot be nudged to a better window, the score gained two terms: `thin_population` (below 22,000 in history, -1 per 30 people) and `crowded_population` (above 28,000). Seeds 1 and 1234 now embark at squares (8,6) and (13,8) (were cells 114,212 and 199,96). Old saves with a free window are refused with a `SaveException` (hook in `WorldSave.Restore`). Town-spacing and ruin-count tests were relaxed where they depended on a free window (nearest-town links in 6-10 km: 71% instead of 75%).
- **N2 Gates.** The continental road network is the history's trade routes (4-connected cell paths between settlements with 60+ people, abandoned ones dropped). `BorderGates.For(history)` (cached per history, integer maths) turns every route step that leaves a region into a gate on the shared edge: position = the cell row (or column) centre, `tile = (pos - region origin) / 64` (the same number on both sides), class Highway/Road/Track by whether both/one/none of the route ends are towns (500+), bearing straight across the edge. Gates on one edge closer than 1,500 m merge into the lowest route id (global order). A route step never changes region in both axes (steps are 4-connected), so there is no corner case. Each region puts a gate node **on the border line itself** (x = 0 or 32,000, y = tile centre) plus an inner node 200 m inside, a straight segment between them, then a road of the gate's class to the nearest town: the neighbour's node is the same point, so the two polylines are one road (the first version used the edge tile centre and left a 64 m gap). No dry-land nudge; if the edge tile is sea or lake the A* from that node may find no way and the gate has no road (counted in tests: all gates of the test blocks carry road ground on both sides). `Region.Gates` holds the region's own gates (in the hash); `BorderGates.ComputeHash` is in the `hashes` output.
- **N3 Heights.** Heights were already a pure function of position (bicubic from the continental cells plus noise from absolute metres). `TerrainBlock` now computes any rectangle of global tiles, `RegionGenerator.HeightBlock` exposes it, and the test compares both neighbours' edge columns to it to the centimetre (mean step across an edge 24-28 cm against 57-74 cm between inner tiles).
- **N4 Rivers (the old ones did not line up).** Probe before the fix: of the channel tiles on an edge only about half had a partner on the other side and accumulations differed up to 100x, because each region's priority flood and its ring-cell inflow saw its own window. Now continental rivers are **drawn the same by every region**: every continental flow step whose cell has 3+ cells draining through it (`UpstreamCells` over `Continent.FlowTo`, computed once per continent) becomes a tile path between the two cell centres, found by a terrain-following A* over pure heights (`HeightAtGlobal`: the window array, else cached 32 x 32 blocks), laid in ascending cell order (first laid wins; a path that reaches a laid one merges into it). The local receivers are bent to these paths, accumulation runs topologically, and the chain tiles carry the accumulation of their step (average of the cell and next-cell accumulations in tiles), raised so it never falls downstream; width and depth come from that, so both sides compute identical vertices. A waterway that reaches an edge gets a vertex **on the border line** (midpoint of the two adjacent chain tiles, the same point from both sides). Local streams (smaller than 3 cells) stay local and are cut at the edge tiles: water running into a pruned edge tile drains off the region there (`FlowTo = -1`). Lakes: the basin a lake fills depends on the window, so edge tiles are never lake (otherwise a river meeting a lake at the border was a river on one side only). Known limits: a lake straddling a border is shaved one tile short on both sides; a basin river with no continental cell behind it ends one tile short; a river through a region corner meets the diagonal neighbour (the corner vertex is not asserted against the side neighbour). Tests: identical set of border vertices (position, width, depth, kind) on every shared edge of the 3 x 3 blocks of seeds 1 and 1234 (68 and 50 crossings).
- **N5 Seam margins.** Settlement lots already stayed clear of the border (test `No_lot_field_or_parcel_stands_within_a_tile_of_a_region_border` passes with the existing 8-tile placement margin; the spec's 1,200 m / 400 m margins are not implemented); field tiles on the edge row or column are no longer eligible.
- **N6 `RegionManager`** (`Sim/Worlds/RegionManager.cs`): owns `GeneratedRegion` (region, landmarks on it, population, chunk generator). `Track(id, x, y)` takes continent metres: side neighbours within `BorderTriggerM` (4,000 m) and the diagonal when the corner is within 4,000 m (Euclid) join a queue ordered by distance; the traveller's own region joins first (a hero set down in an unbuilt region); at most 2 builds run on workers; a queued job is dropped when no traveller is within 8,000 m of its region. A finished region is attached by `Poll()` as one dictionary insert under the lock (measured 0.01-0.7 ms); `ChunkStreamer.Update` polls every call and reports the hero when its chunk changes. `GenerateNow` is the blocking path for a start, load or teleport (`LiveWorld` calls `ChunkStreamer.EnsureReady` for its start chunk). A region is a pure function, so a region built on a worker equals one built alone (tests: hash equality in three orders, on four threads, both seeds). The embark region is handed in with `Adopt`. Numbers (this machine): region + landmarks + people + chunk generator 280-1,500 ms per region (region alone 0.2-1.2 s), budget 3 s.
- **N7 Streaming frame.** Chunk and world coordinates stay in the **frame of the home (embark) region** (origin = its north-west corner), so the home region keeps 0..999 and saved chunk keys, hero positions and everything in the home region are unchanged; a neighbour west or north has negative chunk coordinates, east or south 1,000 and up. `ChunkStreamer(RegionManager, home)` resolves a chunk to (region, local chunk); `RegionOfChunk`, `GeneratorOfChunk`, `HomeOriginXM/YM` give the mapping. Chunk store keys are the frame coordinates (ints, negatives fine). A chunk whose region is not built yet is a neutral grass chunk (not stored, listed in `NeutralCount`); when the region is attached the chunk is rebuilt in place with a new `Version`, the built chunks next to it get a version bump (road rims were drawn against grass), within the 4 ms slice. Off the continent a chunk is closed ground as before. `ChunkGenerator.Version` is now 2. Explored tiles and Codex visits are home-region only for now; `FarLayer` bakes chunks of any generated region; the minimap is the home region.
- **N8 World map.** Zooming out past 1x on the region map (or `--shot-map-scale 0`/`-n`) shows the continent: the pixel map, the 16 x 16 grid, every generated region washed and drawn in detail (rivers, roads by class, towns, villages and farmsteads by zoom, town names from 3x), the hero. `--street-poi border` starts 40 m inside the edge on the best gate road; `--shot-frames n` (> 12) delays the map shot so neighbours are built. The embark map draws the grid and picks whole squares.
- **N9 Walk test** (`Walking_along_the_roads_at_8x_...`, seed 1234, R(10,6) to R(12,6), a bridge-free road of 71.9 km from 1 km before the first border to 1.5 km into the third region; bot paths to the road 60 m ahead with `CommandMoveTo`; the window swaps synchronously, regions are built on workers, the test ticks at 50 ms while a build runs): 59,330 ticks at 8x, avg 0.27 ms, p99 6.1 ms, max 65 ms (a window swap), 0 ticks without progress within 300 m of either border, 0 ticks on neutral ground, the second neighbour ready 3,996 m before its border (the first, 1,002 m: the route began about 1 km from the border). A bridge at a bend of a wide river can be a dead end for a walker (the deck is a straight rectangle along the nearest road segment, rails on both sides): the bot route avoids bridges. That is a chunk-generator limit, not a seam one, and worth its own fix.
- **N10 Not done / part B.** Ids still carry no region bits (two regions both start settlement, person and household counters after the history ids), so records of two generated regions can collide until E2 lands; no `RegionLevel`, hourly or daily region ticks, mailboxes, `RegionSection` saves or paging (generated regions are not saved: they are rebuilt from the seed when needed); the walk does not yet slow the hero when a region is late (it never was in tests: neutral ground is shown instead).
- **N11 Golden hashes.** The hashes of seeds 1 and 1234 changed (embark square, rivers, gate roads, field margin, chunk version) and were re-pinned in `tests/Ravensguard.Sim.Tests/Determinism/GoldenHashes.cs`; `hashes` also prints `gates` and a 2 x 2 block of regions (region, landmark and people hashes) per seed, so CI's Windows/Linux diff covers neighbours.

## 11. Notes: part B implementation (records by region, levels, paging, per-region saves)
Code: `Core/EntityId.cs`, `Sim/Worlds/{RegionWorld,RegionRecordSet,RegionPager,RegionTransfer}.cs`, `Sim/Abstract/RegionScheduler.cs`, `Sim/Save/{RegionSave,WorldSave,ProductionSave}.cs`; tests in `tests/Ravensguard.Sim.Tests/RegionsB/`.
- **N12 Ids (E2).** `EntityId.Region` is bits 48-55 of the counter, `Serial` the low 48. `IdAllocator.ForRegion(r)`; the region builder (settlements, parcels, fields, fiefs, sites) and the populator (person and household base counters) start at `region << 48`. Settlements that come from history keep their global history ids (no region bits). This changed every generated id, so the golden hashes were re-pinned. A person who moves region keeps the id of the region they came from (`PersonTable.SetForeignId`, saved as `PeopleData.ForeignRow/ForeignId`).
- **N13 Records and levels.** `RegionWorld` is one region's records: a `SimWorld` (people, households, economy, tick state, own chronicle and id counters) plus its `EconomyWorld` (farming, markets, production). Region sim seed = world seed for the home region, `Mix(seed, region)` otherwise. `RegionScheduler.LevelFor`: ring 0 Live, ring 1 Detailed (hourly steps, at most 9), ring 2 Daily (stepped in whole days at the day boundary; at most 16 more resident), further = Paged, never generated = Abstract. Levels are a pure function of distance and generated-ness, so visit and generation order change nothing (test). The abstract systems have no daily-mode kernel yet, so Daily runs the same 24 hourly steps in one batch: same records as Detailed at the same hour, but no CPU saved. **Lead: if a real daily mode is wanted, systems need a `ctx.Coarse` flag (24 slices at once, one market session).** Regions with no households (sea) get terrain only, no records.
- **N14 Catch-up.** `RegionWorld.AdvanceTo`: gaps up to 72 hours replay every phase; for a longer gap the older hours run coarse (`WorldTicker.PhaseMask = H5|H6|H7`: daily globals, seasons, prices; no work, markets or vital slices) and the last 72 hours run in full. This is a stand-in for the spec's closed-form CatchUp (3.2): a region left for years does not age, breed or trade for that time, and its fields are not set to the calendar stage.
- **N15 Paging.** `RegionRecordSet.PageOut` writes the region's `RegionSectionData` (people, lots, economy, abstract, farming, production, markets, chronicle, id counters, and its share of the chunk store) to an `IRegionPager` (memory, or `r<id>.region` files), removes the chunk entries and evicts the terrain from the manager. Page-in is a normal `RegionManager` build (worker) whose records factory restores from the section instead of making fresh ones; `Sync()` at a barrier takes the region in. Page out -> in -> capture is byte-identical (test); the home region is never paged. Neighbour build including fresh economy: about 1.8 s on a worker; page-in (terrain plus restore) about 0.4 s.
- **N16 Save.** `WorldSave` now writes `production` and `markets` (they were missing; `WorldState.Production/Markets`) and one `region.NNN` section per generated region other than home (resident ones captured at the barrier, paged ones as stored). `chunks` then holds the home region's chunk entries only. `WorldState.RegionSections` carries the sections of a loaded save (re-saved unchanged, or handed to `RegionRecordSet.AttachSections`). Save -> load -> save is byte-identical, with paged regions (test). `ProductionSave` stores the resource layer as a diff against a freshly built one (the layer is 450,000 rows; the section fell from 2.1 MB to about 1 KB).
- **N17 Hero.** `HeroRegionTracker`: `Region` follows the hero at once; `RingRegion` (levels) changes only 1,000 m inside the new region (3.5). The `live` section stores the hero's region id (`LiveSave.RegionPlus1`, checked against the position on load; `LiveResume.RegionId`); a save 40 m either side of a border resumes in the right region (test).
- **N18 Transfer.** `RegionTransfer.MovePerson` leaves a `Departed` tombstone and adds a row (same id) to the lowest household with a free bed in the destination settlement. Spouse, kin, employer links do not cross (rows differ); households and whole families do not move yet; `Vital` counters and the economy's person columns are updated.
- **N19 Budgets measured** (seed 1234, 4 workers idle box): region section 0.1 MB (small region) to 1.1 MB (25,000 people, home) after the resource diff; page-in 0.4 s; walk of 12 regions in a line (row 8, x 1-12, 6 game hours per stop, 5 x 5 block asked for): resident peak 13 regions, detailed peak 9, 664 MB peak managed memory, 26 regions (50 MB) paged on the way; neighbour build at most 2.1 s.
- **N20 Not done.** Game integration: nothing in the game yet runs the abstract tick or the scheduler (`RegionScheduler.Update` is meant for a worker at the hourly barrier; its cost for 9 detailed regions is about 0.15-0.4 s per game hour on one thread, so it must not run on the main thread); the economy of a new game is not created by the region build screen, so new games save no production or markets. Not saved: the economy counters, the parish and labour-market working state, the continent trade float (rebuilt on restore). Mailbox tombstones, caravans and migrants that cross by themselves are not implemented, only the explicit person transfer. The spec's 1,200 m seam margins (N5) are still open. One part A streaming test (`Neighbours_are_generated_on_workers...`) could not be made to fail again; its attach-time threshold (1 ms) was the only load-sensitive assertion and now uses `TestBudget`.

## 12. Notes: part C implementation (the game runs the world, memory, save size, seam margins)
Code: `Sim/Worlds/{WorldRuntime,NewGameWorld}.cs`, `Sim/Save/{PeopleDiff,ProductionSave,RegionSave,WorldSave,SaveWriter,ChunkPersistence}.cs`, the street view, region build and run load screens in `Game/`; tests in `tests/Ravensguard.Sim.Tests/RegionsC/` (`WorldRuntimeTests` is the three-day headless game).
- **N21 The runtime.** `WorldRuntime` wires a `WorldState` to a manager, record set and scheduler (`DailyRing = 1`: only the 3 x 3 block is resident, ring 2 is paged; at most 2 regions step in parallel (one on a 2-3 core machine) so the render thread and the chunk workers keep their cores: an hour is 22 real seconds at 8x and a nine-region step takes 0.3-0.8 s, so the tick has room to be slow). The street view calls `Pump(clock, hero metres)` once per frame: it updates the hero tracker and, if the clock is past the last barrier and the worker is idle, queues the next single game hour. The tick worker (`region-tick`, one thread, below normal priority) runs `RegionScheduler.Update` for that hour (joins, page-in requests, page-outs, steps in ascending region id) and then swaps in an immutable `RuntimeSnapshot` (hour, resident/detailed/paged counts, home alive and households, one row per resident region, hour ms): that swap is the barrier. Records are written by the worker only; the main thread reads snapshots, and anything that needs records (a save) is a job queued on the same worker (`AtBarrier`), so it runs between two hours and sees whole hours. One hour is in flight at a time: a slow machine falls behind in game hours, never in frames. Time controls (pause, 1x-8x) only move the live clock, so they drive the tick with no extra code; at 8x an hour is 22.5 real seconds. `Pump` measured 0.6 us average, 3 us p99 over 32,400 calls in the three-day test (a one-off 5-6 ms call is a GC pause of the process, not the pump); in the engine (xvfb, software GL, 4 cores, hero sprinting at 8x with all eight neighbours building on workers and nine regions ticking, 150 s) the pump took 10 us average and 1.1 ms at worst, a world step 0.19 ms average and 14 ms at worst, a frame 1.0 ms average and 9.3 ms at worst after warm-up. Two main-thread stalls were found and fixed on the way: `ChunkGenerator.For` built a generator (about 100 ms) inside its global lock, so a frame asking for another region's generator waited for a worker (120-176 ms frames; now built outside the lock, and a neighbour a traveller is near gets its generator on the build worker, one nobody is near makes it on first use and gives it back when the hero is 8 km away), and a blocking full collection of a 400 MB heap, so the street view runs with `GCLatencyMode.SustainedLowLatency` while a world runtime exists. Worker cost per game hour: home region alone 6-9 ms (most hours), day-boundary and market hours up to 0.2-0.8 s, nine detailed regions 0.15-0.4 s. Determinism: hour for hour the worker-driven and the synchronous runs give identical records (test); what can depend on timing is only which ring region the hero had when an hour started (levels change when a region steps, not what it holds, N13).
- **N22 A new game has its economy.** `NewGameWorld.Create` builds the home region the way `HeadlessWorld` does (`RegionWorld.CreateFresh`: economy, markets, businesses, resource layer, farming, abstract state, region id in the ids) and returns a `WorldState` with `HomeRecords`; the region build screen, the dev seed start and the tests use it. A new game's live clock now opens at the end of history (08:00 on the first spring day of year `pop.NowYear`), which is where the tick's own clock starts; before, the street view opened year 1 and the tick could never have run. Neighbours join with their economies when the manager attaches them (`RegionManager.Attached` takes the records in at once, so a visited chunk of a returning region is in the store before it streams); the scheduler's own `Sync` stays as a barrier step. `ChunkStore` takes a lock on every member (the streamer on the main thread and a page-out on the worker share it); chunk generation reads household classes with a bounds check because the tick may be growing the table (the lot's wealth style follows the live class: a visual detail that depends on when the chunk is first visited).
- **N23 Memory.** Where it goes, per generated region (seed 1234, 25,000 people): terrain layers 10.8 MB, people 4.2 MB, records 15.3 MB (of which the resource layer is about 5-11 MB: 223,000 to 495,000 rows of 23 bytes), chunk generator 5.8 MB, growth from ticking about 2 MB per 30 game hours. Done: (a) `DailyRing = 1`, so ring 2 is paged and the resident set is the 3 x 3 block plus the home region (9 detailed + home = 10 at most; the home region cannot page because the world state holds it); (b) `Region.TrimDerived` releases height, drainage, lake, fief, realm, catchment and flow layers (4.3 MB) of every neighbour built by the manager (`RegionManager.TrimTerrain`; the hash is kept, the home region keeps its layers for the map); (c) the chunk generator is made on first use (`GeneratedRegion.Generator`), so a neighbour nobody walks in never pays for it. The 12-region walk (row 8, 6 game hours per stop, paged sections in a folder as in the game; the test now measures what the walk adds to the process, and runs alone because other tests share the heap) added 398 MB at its peak with 10 resident (9 detailed), home region included (32 MB of it), from 664 MB with 13 resident (measured the old way, about 614 MB added); a populated region with its records is 25-37 MB (28.7 MB for the 25,700-person home region after 48 game hours: terrain and people 14.6 after trimming, records 15.3 of which the resource layer is the largest part, ticking +3.2). About 300 MB is not reachable without economy data changes: the resource layer alone is 5-11 MB a region (long stock and cap per row, 223,000 to 495,000 rows; ints and a coarser unit would halve it), and the household and farming stores another 8-10 MB. Recommended to the economy owner. Chunk caches in the game (loaded chunks, far-layer bakes) are on top and were not part of the walk.
- **N24 Save size.** Region sections averaged 1.9 MB (home-sized 1.95 MB); profile: people 980 KB, economy 296, production 363 (the resource diff was a row list), markets 130, farming 123, lots 41. Now (a) people are saved as a difference from the baseline population the populator makes again (`PeopleDiffer`; per column the changed rows gap-coded plus the rows added after the baseline; names, birth days, kin, attributes and traits did not change in three days, jobs, beds and skills change in a tenth of the rows): 980 KB becomes 53 KB; the loader has the baseline anyway (the region was just built), the saver makes it on the thread pool in `LiveCapture.Encode` (`WorldState.Content`, `RegionServices.History`), and a region paged out writes the same compact bytes, so paging still never changes a save; (b) the resource layer is stored as one class byte per row (as built, at cap, fresh plus the kind's usual regrowth amount, fresh plus the kind's usual share of the gap to the cap, or explicit), which compresses to a few KB: production 363 KB becomes 78 KB (a sparse region with 450,000 rows: 1,447 KB to 44 KB). Result after 48 game hours: 0.70 MB (25,700 people), 0.60 MB (20,400), 0.08 MB (1,700; 0.45 MB before the regrowth law). A whole save of the three-day test is dominated by visited chunks (about 3.8 KB per chunk entry, 33 MB for the 40 km walk: first-visit snapshots, plan 2.2); recommended: regenerate unmodified chunks instead of keeping their snapshot when the generator version and content hash match.
- **N25 Seam margins (N5).** The spec's 1,200 m (towns) and 400 m (villages) margins were a means to the end that no lot or field stands within 64 m of an edge and no settlement spills over it. Measured on the 3 x 3 blocks of seeds 1 and 1234 (`SeamMarginTests`): nearest settlement centre 480 m from an edge, nearest lot corner 260-330 m, nearest field tile 64 m (the eight-tile placement margin plus the unused edge row); every village is 400 m clear (454/454, 458/458); 7 of 24 and 9 of 25 towns are inside 1,200 m by centre but their lots are still at least 260 m from the edge. Resolution: the margins are not implemented as written (moving towns changes every region of every seed and the economy's baselines); the guarantee they were for is, and the test pins it (centre >= 480 m, lot >= 250 m, field >= 64 m).
- **N26 Saving and loading.** Save: the main thread takes the hero, clock, torch, explored map and Codex progress (`LiveWorld.CaptureResume`), the tick worker copies everything else between two hours (`RunSession.SnapshotAsync` -> `WorldRuntime.AtBarrier` -> `WorldSave.SnapshotLive(state, resume)`), the thread pool diffs, encodes and writes. Quicksave (F5), the pause menu, the 15-minute autosave and the save on quit all use it. Load: `RunLoadScreen` restores the state, makes the runtime on its worker (the sections of other regions go to the run's pager folder, restored when the hero comes near) and the street view starts it; a hero saved across a border resumes in the neighbour (`EnsureReady` restores that region from its section before the first chunk loads). Save -> load -> save is byte-identical after three game days at 8x across a border (`A_new_game_ticks_three_days_at_8x_...`). F9 quickload loads the run's manual slot (never in Ironman; a toast says why); autosave and Ironman slots are per run (`<run>.autosave_1.sav`, `<run>.ironman.sav` with `.bak1/.bak2`), the manual slot of a loaded autosave or Ironman run is its run name; the older shared names still load. The ring region now changes when the hero is 1,000 m beyond the ring region's square on either axis (before: 1,000 m inside the new region on every axis, which a hero walking along a border would never reach, leaving regions ahead of it paged).
- **N27 Not done.** The home region is never paged (10 resident at most, not 9); about 300 MB needs economy data changes (N23); chunk snapshots dominate a long walk's save (N24); the abstract tick still has no daily mode (N13), no mailbox tombstones, caravans or migrants crossing by themselves (N20); the world map shows generated regions but not their tick level yet (the snapshot rows carry it); economy counters, parish and labour working state and the trade float are still not saved.
