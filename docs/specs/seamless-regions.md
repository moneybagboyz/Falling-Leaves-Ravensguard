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
