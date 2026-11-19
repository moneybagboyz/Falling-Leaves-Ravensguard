# Build mode and construction: ghost pieces, hands-on work, hauling, support and collapse, plans, contracts, visible growth

Plan refs: §5 Phase 5 (**building is hands-on, Kenshi style, decided with the user**: placing a plan only makes ghost pieces; every piece is built by a person doing the work: the hero by hand, which trains the building skills, or his people and hired workers through the job system; materials must physically be on site; work stops when they run out or wages go unpaid, and anyone can resume it; half-built work stays in the world; commissioning a builder is the same work done by NPCs for pay, never instant. **Build mode offers both** free placement on the 1 m grid **and** ready-made house plans from a library by culture, wealth and purpose, the world-generation blueprints plus player-saved ones, fitted to the lot and built the same way; multi-storey floors, stairs, ladders and trapdoors; structural support and collapse; save as blueprint; construction and hauling jobs), §2.2a (z = +1..+3; an upper floor tile needs a wall, beam or post under it within 3 tiles; destroy the supports and the floor collapses, dropping what is on it; portals in the HPA* graph, invalidated and re-planned), §2.9 (one blueprint format for world generation, the build tool, NPC contracts and mods; walls by culture style and wealth), §2.8 (Build context: left-click places, Z / X rotate, Esc cancels, right-click removes a ghost), §4A.2 (an 8 x 5 m cottage takes about 250 worker-hours, about 40 poles, 1.5 t of daub and 3 t of thatch; the lord's planner posts paid construction contracts; background growth: "Phase 5 turns this into visible construction jobs").

## 1. Purpose
Turn the Phase 1 blueprint machinery (`BuildingGrid`, `BlueprintParser`, `BlueprintPlacer`, `BlueprintWriter`, `StructuralSupport`, `RoomDetector`) into a game: the hero marks what he wants as ghosts, materials are carried to the site, people (himself, his followers, hired hands, a contractor's crew, the village's own households) build it piece by piece over real game hours, and the result is real tiles, rooms and portals in the one world. Unfinished work persists, can be resumed by anyone allowed, and can fall down.

## 2. Dependencies
| Needs | From |
|---|---|
| `BuildPieceDef` (`Kind`, `Material`, `Tags`, `Station`, `Beds`), `PieceCatalog`, `PieceKind` | exists (Phase 1); extended 3.1 |
| `BuildingGrid` (levels, `Structure[]`, `Fixture[]`, `Letter[]`, `Transform(quarterTurns, mirror)`), `BlueprintPlacer.Place` (open cells, stair and ladder portals), `BlueprintWriter`, `BlueprintValidator`, `BuildingLibrary`, culture styles | exists (`Sim/Building/`) |
| `StructuralSupport` (3-tile rule, walls and posts support) | exists; extended 4.5 |
| `LevelGrid`, `LevelPathfinder` (portals, invalidation), `ChunkStreamer.SetTile` and ID-keyed `ChunkDelta` | exists |
| Jobs (`JobTable`, `OddJobKind.Haul`, escrow, claims at the barrier, `WatchHero` rule), `ReservationTable` | exists (Phase 3) |
| `Store` (site piles are economy stores in `OtherStores`, as crafting job stores), `MassLedger`, `MarketBook.Buy` | exists |
| `GrowthSystem` `BuildProject` (materials in grams, hours, payer), `GrowthChunks` | exists (Phase 3) |
| Phase 4 falls through the resolver (`0x416` placement, fall damage), `FightTick` not needed | exists |
| LandRegistry `May`, structure deeds | `land-and-deeds.md` |
| RoomDetector v2 (incremental, `Indoor`, dwellings, workshops) | `roomdetector-v2.md` |
| Tool qualities and wear | `items-quality-and-wear.md` |
New: `Sim/Construction/` (`ConstructionSite`, `Ghost`, `SiteBook`, `BuildWork`, `SupportGraph`, `Collapse`, `PlanFitter`, `Contracts`, `GrowthSites`), Godot `World/Build/` (`BuildMode`, `GhostLayer`, `PlanLibraryPanel`, `SitePanel`). `OddJobKind` appends `Construct, Deconstruct`. `PieceKind` appends `Roof, Beam`.

## 3. Data

### 3.1 A piece's build cost (`BuildPieceDef.build`, content `build/pieces.jsonc`)
```jsonc
{ "type": "build_piece", "id": "wall_wattle", "name": "Wattle-and-daub wall", "kind": "wall", "glyph": "%", "material": "daub",
  "build": { "skill": "none", "worker_hours": 6.5, "tools": [ { "quality": "cutting", "level": 1 } ],
             "materials": [ { "good": "logs", "kg": 16 }, { "good": "clay", "kg": 79 } ],
             "hp": 300, "carries_storeys": 1, "salvage_pm": 200, "outdoor_work": true, "rain_stops": true } }
```
Units: `worker_hours` at skill 50 (production's `time_mult = 1300 - 6 x skill` permille applies; `skill: "none"` reads 50 for everyone, so an unskilled labourer is not penalised on daub); `kg` per piece (one 1 m cell, one storey high, about 2.4 m); `hp` piece hit points (resolver structures, fire and siege later); `carries_storeys` how many storeys above this one it can bear; `salvage_pm` the share of materials a deconstruction returns.
Representative rows (about 80 build pieces in Phase 5; the 74 existing rows gain `build`):
| Piece | Skill | h | Materials per cell | hp | Carries | Salvage |
|---|---|---|---|---|---|---|
| `floor_packed_earth` | none | 0.5 | - | 50 | - | 0 |
| `floor_plank` (on joists, upper floors) | Carpentry | 2.0 | planks 25 kg, nails 0.2 | 150 | - | 600 |
| `floor_flagstone` | Masonry | 3.0 | stone_dressed 120 kg, quicklime 5 | 400 | - | 800 |
| `wall_wattle` | none | 6.5 | logs 16, clay 79 | 300 | 1 | 200 |
| `wall_timber` (frame, plank or daub infill) | Carpentry | 10 | logs 120, planks 30, nails 0.5 | 600 | 2 | 600 |
| `wall_stone` (rubble, 0.5 m) | Masonry | 16 | stone_raw 2,300, quicklime 60, sand 120 | 2,000 | 3 | 800 |
| `wall_brick` | Masonry | 12 | bricks 900, quicklime 40, sand 80 | 1,500 | 3 | 700 |
| `post_timber` | Carpentry | 3 | logs 60 | 400 | 2 | 700 |
| `beam_timber` (new `Beam`) | Carpentry | 4 | logs 80 | 400 | - | 700 |
| `door_wood` | Carpentry | 3 | logs 4, planks 20, nails 0.3 | 120 | 0 | 600 |
| `window_shuttered` | Carpentry | 1.5 | logs 3, planks 5 | 80 | 0 | 600 |
| `stairs_up` + `stairs_down` (a pair, 2 cells) | Carpentry | 8 | planks 60, logs 40, nails 0.5 | 200 | - | 600 |
| `ladder` + `trapdoor` | Carpentry | 2 | logs 12, planks 8 | 80 | - | 700 |
| `roof_thatch` (new `Roof`) | none | 2.8 | logs 2, thatch 75 | 60 | - | 0 |
| `roof_shingle` | Carpentry | 3.5 | logs 6, planks 18, nails 0.4 | 120 | - | 500 |
| `roof_slate` | Masonry | 5 | stone_dressed 60, logs 8, nails 0.5 | 300 | - | 600 |
| furniture and workstations | by piece | 1-24 | by piece (forge: stone 600, bricks 200, iron_bar 20; anvil: bought as an item) | | - | 500-900 |
Workstations and furniture whose real form is an object (anvil, loom, millstone) can be **installed** from an item (an anvil item, 120 kg) instead of built: 1 worker-hour, the item becomes the piece and returns on deconstruction.
**Golden cottage (8 x 5 m, `test_cottage`):** 19 wattle walls, 1 door, 2 windows, 18 packed-earth floor, 40 thatch roof = **250.5 worker-hours, logs 394 kg, clay 1,501 kg, thatch 3,000 kg, planks 30 kg, nails 0.3 kg** (the plan's "about 250 worker-hours, about 40 poles, 1.5 t of daub and 3 t of thatch").

### 3.2 `ConstructionSite` and `Ghost`
```
ConstructionSite { Id (EntityId), Region, Deed, Owner (EntityId), Payer (purse), Origin (cell3), Rect, Blueprint (optional id), Contract (optional),
                   Store (the materials pile: an economy store at the site's pile cell), WagePerDayMites (per class), Crew[] (person ids),
                   State (Active, Paused, Finished, Abandoned), PauseReason (None, NoMaterials, NoWages, Rain, Night, Owner), Priority (0-3) }
Ghost { Id (EntityId, IdKind.Other serial base 0x740_0000), Site, Cell (x, y, z), Piece (dense id), Rot, Slot (Structure | Fixture),
        State (Planned, Blocked, Ready, InProgress, Built, Cancelled), Block (Support, Materials, Under), WorkMilli (milli-worker-hours done),
        Store (materials moved into the piece once work starts), Builder (last person), BuiltSkill }
```
`Blocked: Support` = what it rests on is not built yet; `Materials` = the pile lacks them; `Under` = another ghost or a built piece must be removed first. A built piece leaves the ghost table: it becomes a tile code in the chunk (`ChunkOp.SetTile`) and a cell of a **structure** record (3.3).

### 3.3 Structures (player and NPC built)
`Structure { Id, Deed, Rect, Levels (z set), Cells[] (piece, hp, builtSkill), Rooms (RoomDetector v2 cache) }`. Generated buildings get a structure record only when first changed (lazily, from the chunk and the blueprint), so the save holds only structures that were built or altered. Structure deeds (land spec) key on `Id`.

## 4. Rules

### 4.1 Placing (build mode)
Two ways, one result (ghosts):
- **Free placement** on the 1 m grid at the current level (PgUp / PgDn): pick a piece; LMB places; LMB-drag draws a **line** for walls, fences and posts, a **filled rectangle** for floors and roofs, Shift-drag an outline; Z / X rotate (doors, stairs, furniture); RMB removes a ghost (drag removes many). Tools in the same panel: **Roof over** (fills roof ghosts over every enclosed top-floor area of the structure at the cursor), **Stairs** (places the up and down pair on z and z+1), **Deconstruct** (marks built pieces).
- **A plan from the library** (P): a list filtered by **culture** (the plan's `cultures` field; `any` by default), **wealth** (resolves `$wall` / `$floor` through the culture's style, as world generation does), **purpose** (the declared room purposes: dwelling kind, `work:*`, storage, livestock) and **fits this lot**. Picking one shows it fitted (4.2); Z / X rotate, M mirrors, LMB or Enter commits: every cell becomes a ghost in one command. The library holds every `BlueprintKind.Building` blueprint of the loaded content (world generation's own, about 60) plus the player's saved ones (4.8).
**Validity** (red ghost with the reason, nothing placed): the land rule (`LandRules.May`); ground slope over 100 pm for floors at z = 0; the cell holds a built piece or another ghost (use Deconstruct first); support (4.5) would fail when finished; a stairs pair needs its upper cell free; `z <= 3`; no piece over a road or another deed. Upper-level ghosts can be placed before what holds them: they wait `Blocked: Support`.

### 4.2 Fitting a plan to a lot (`PlanFitter`)
Try the 8 transforms (4 rotations x mirror). A transform fits when its footprint lies inside the lot rectangle with a 0 m setback on the street side and 1 m elsewhere (burgage plots are built to the street), no cell is over water or a slope above 100 pm, and at least one exterior door faces the street or the lot's open side. Among fits choose: (1) front door toward the street, (2) footprint against the street edge, (3) centred along the street, (4) lowest transform index. If none fits, the panel says why ("12 m wide; the lot is 11 m") and offers free placement. Cells of the lot already built are kept if the plan has the same piece there (reuse a standing wall); otherwise they are `Blocked: Under` and a Deconstruct ghost is added.

### 4.3 Materials must be on site
- The site's pile is an economy `Store` at a pile cell the owner picks (default: the lot's first open cell nearest the street). Goods count for a ghost only when they are **in the pile** or carried by the builder standing at the ghost.
- When work on a ghost starts, its materials move from the pile into the ghost's own store (as a `CraftJob` does: mass conserved, progress is physical); cancelling a ghost in progress returns them to the pile.
- **Getting materials there:** the owner carries them (inventory, cart: `\` unloads into the pile when it stands within 3 m), buys them at a market and has them delivered (a `Haul` odd job posted from the seller's store to the pile, paid by the payer; Phase 3 haul jobs unchanged), gathers them (cutting poles on his claim, digging clay, Phase 3 gathering), or a contractor buys them (4.7). The site panel lists `needed / in pile / on the way` per good and offers **Post haul jobs** for what the payer owns elsewhere and **Buy and deliver** for what the market has (a market buy order to the pile).

### 4.4 Building a piece: by hand or by others
- **Who:** the hero (E on a `Ready` ghost: Build), any follower ordered to build (squad order or the Q menu), and **hired workers** through the job board: each active site posts `Construct` odd jobs (one slot per `Ready` ghost cluster, up to `crew_max` 8), paid by the hour at the site's wage; NPC labourers, idle adults and the hero himself can claim them. A worker needs the piece's skill above `min_skill` (default 0) and the tools (carried or in the pile).
- **Rate:** progress `+1 milli-worker-hour` per `3.6 / (time_mult/1000)` game seconds of work, i.e. a piece of `h` worker-hours takes `h x time_mult / 1000` hours for one worker; up to 2 workers per cell (4 for a stair pair or a forge). No tool of the quality: x1.5; improvised level 0: x1.5 and the tool wears x3. **Example:** the hero at Carpentry 20 on a timber wall: `time_mult` 1,180, 10 h -> 11.8 h.
- **Skill gain** per attended hour, as crafting (`d_x100 = max(25, difficulty) x 2` with the piece's difficulty `= min_skill + 20`): building trains Carpentry and Masonry; `none` pieces train Athletics a little (`d_x100 = 20`). Effort `heavy` for stone and roofing (fatigue +50 pm an hour, nutrition x1.5).
- **Built quality:** a finished piece stores `BuiltSkill`; its hp is `hp x (700 + 6 x min(skill, 50)) / 1000` (skill 0: 70%, 50+: 100%), so a botched wall is weaker but still a wall. No random draw.
- **Weather and light:** `rain_stops` pieces (daub, thatch, lime mortar) pause in rain over 2 mm an hour and in frost below 0 C (lime and daub do not set); outdoor work pauses at night unless the cell's light is >= 80. The workers take the Phase 3 `Shelter` action.
- **Pauses:** `NoMaterials` when no `Ready` ghost has its materials; `NoWages` when escrow cannot hold the next day (4.6); `Owner` when the owner pauses. A paused site keeps every ghost, store and progress; **anyone the land rule allows can resume it**, including an heir.
- **Order:** workers pick the lowest `(z, support-depth, distance)` ready ghost, so walls rise before floors above them and the roof goes on last; the owner can raise a ghost's priority.

### 4.5 Structural support and collapse (extends `StructuralSupport`)
- **Rest rule** for every piece at `z >= 1`: it rests on the cell below if that cell holds a wall, post or (for walls) a beam; or, for floor-bearing pieces (floor, roof, stairs, beam), a **support** (wall or post at `z - 1`) lies within 3 tiles (Chebyshev, as today); or a `Beam` row at `z` whose both ends rest on supports within 6 cells carries it (a beam spans 6 m). A wall at `z >= 1` with nothing directly below must lie within **1** tile of a support (a jetty).
- **Load rule:** a support at `z` can bear pieces up to `z + carries_storeys` (wattle carries a loft, timber two storeys, stone and brick three); the plan check and the ghost validity both use it.
- **Collapse:** when a support is removed or destroyed (deconstruction, fire and siege later, a collapse below), `SupportGraph` rechecks only the affected structure's cells above. Unsupported cells **groan** for 10 game seconds (a creak sound, flavour "the floor above groans", the cells flash), then fall together: each piece becomes rubble (its `salvage_pm` of materials as goods plus the rest as `waste` on the cell below; mass exact), what stood on it drops one level per missing floor (people take a fall of 3,000 mm per level through the Phase 4 fall rules, items land on the cell below), and the check cascades upward and outward until stable, in `(z, y, x)` order. Portals through fallen cells are removed and `LevelPathfinder` re-plans (plan §2.2a). Chronicle `collapse` with the cause (who removed the support, or the fire).
- A ghost never counts as a support, so a plan cannot fall down before it is built; deconstruction warns before removing the last support ("The floor above will fall").

### 4.6 Wages and escrow
The payer posts a day's wages for the crew into escrow at 06:00 (Phase 3 job escrow); workers are paid at the end of each worker-day `wage x hours / 10` (labourer 150, skilled 350 mites a day: production's classes, Carpentry or Masonry over 30 is skilled). If escrow cannot be filled the site pauses `NoWages` and its `Construct` jobs close (claimed workers leave at the end of their current piece hour); owed wages stay owed (the worker quits after 3 short days, as staff do). Followers on the hero's payroll build without a site wage.

### 4.7 Commissioning a builder (NPC contracts)
Q on the lot or the site > **Hire a builder** lists the region's carpentry and masonry businesses and master builders with their **bid**: `materials at the local ask + labour hours x wage class x 1,450 pm (the price solver's margin) + haul cost`, the crew size and the expected days. The contract: one third into escrow at signing, then a third at 50% of piece-hours built and the rest at handover; the contractor buys and hauls materials himself (real market orders; his demand registers in the book), his crew does the same ghost work with the same rules, and the site panel shows it piece by piece. Never instant: a contracted 8 x 5 m cottage with a crew of 4 takes about 7 game days (250.5 h / 4 / 10 h a day, with weather and haul time). If the payer misses a stage payment the contractor stops (`NoWages`) and keeps the stage paid; the half-built house stays and is the payer's.
**The lord's planner** posts the same contracts for civic work (a wall segment, a mill, a bridge in Phase 6) with the blueprint, the materials list and wages; the hero can bid as a contractor (Q on the notice board) if he has a crew.

### 4.8 Save as blueprint
Select a rectangle (Ctrl+S in build mode) over built pieces and ghosts: `BlueprintWriter` writes a `blueprint` JSONC (rows, legend, levels, fixtures, rooms from RoomDetector v2's primary purposes, wealth from the wall material's style rank, `cultures: ["any"]`) to `user://blueprints/player/<id>.jsonc`, loaded through the `ModLoader` as the mod `player` (ids prefixed `player.`). The save embeds the text of every player blueprint a site uses (so a save opened on another machine still builds it); `BlueprintValidator` runs on save (reachability, purposes) and its warnings are shown, never blocking.

### 4.9 Background growth made visible (`GrowthSites`)
- In a region that is **not** detailed, or a chunk **never visited**, growth stays as Phase 3 (abstract hours, generated complete).
- When a growth `BuildProject` (cottage, premises) is on a plot in a **visited** chunk, it becomes a `ConstructionSite` with the project's blueprint placed by `PlanFitter` (same rules as the hero's), ghosts in build order, the payer household as owner, and the project's bought materials (`MaterialG`) as the pile. **Mapping:** pieces are built in build order until their worker-hours sum to `HoursDone`; materials of built pieces leave the pile (mass exact). From then on the site is the truth: abstract hours from the seasonal step are credited piece by piece when the chunk is outside the bubble (H2, the same rate as 4.4 with the household's own and hired hands), and live workers do the pieces when it is inside. The `Construct` jobs it posts are real paid work for idle adults and the hero (the Phase 3 "idle adults" item).
- A project that finishes becomes a `Lot` through `RegionGrowth` exactly as Phase 3 (the log is unchanged), and its structure record is dropped (it equals the blueprint).

### 4.10 Tick placement
| Where | What |
|---|---|
| Live 20 Hz | workers on a ghost add progress per game second (one integer add each); groan timers |
| Barrier | place, remove, commit plan, deconstruct marks, piece start (materials into the ghost), piece finish (`ChunkDelta` SetTile, portals, RoomDetector v2 for the structure, support recheck), collapse resolution |
| H2 (hours 06-16) | abstract crews on sites outside every bubble (contractors, growth, the hero's crew while he is away) credit hours, ascending site id |
| H4 slice | wage escrow, pay, `NoWages` |
| H6 seasonal | growth projects advance as Phase 3; visible ones through 4.9 |
LODBridge: a worker demoted mid-piece keeps the job; the piece keeps `WorkMilli`; promotion places him at the ghost.

## 5. Determinism
Integer milli-worker-hours; piece choice by `(z, support depth, distance, ghost id)`; contractor choice is the player's; growth sites use the Phase 3 `GrowthWork` wobble (0x271) on the project's hours only. Streams: **0x510** collapse rubble scatter (key collapse event id, cell), **0x511** contractor pace jitter +-10% per day (key contract id, game day), falls reuse **0x416** (key event serial, person). `SiteHash` = sites, ghosts, structures; in the daily trail.

## 6. UI and controls (Build context; overview 6A)
- **B** opens build mode (refused in combat or while mounted). The hero keeps working; the world runs (pause with Space). WASD and arrows pan, PgUp / PgDn change level (levels above cut away, the one below one shade darker), `,` / `.` piece, `[` / `]` category (Walls, Floors, Roofs, Doors and windows, Stairs, Furniture, Workstations, Fences), P plan library, M mirror, R roof over, Delete deconstruct brush, Enter commit plan, Ctrl+Z undo the last placement, Ctrl+S save as blueprint, J site panel, Esc steps back (tool, then mode), B closes. While build mode is open the World keys other than time, camera, level, overlay, save and bug report are suspended (overview 6).
- **Ghosts** draw as the piece sprite at 40% alpha in blue (ready), amber (blocked: icon for support, materials or under), red (invalid), with a scaffold overlay and a progress bar when in progress. A built-today piece flashes once.
- **Site panel** (J, or Q on any ghost): progress (piece-hours done of total, % by storey), materials table (needed / pile / on the way, with Buy and deliver, Post haul), crew (who, skill, wage, hours today), escrow days left, pause reason in plain words ("Stopped: no thatch on site"), Pause / Resume, Hire a builder. Fits 960 x 540 at 14 px rows.
- **Hero by hand:** E on a ready ghost builds it (the default action; hold to keep working the next ready ghost within 3 m); Q on a ghost: Build, Haul materials here, Raise priority, Cancel.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| BC-1 | Content: every build piece has `build`; every material good resolves; every plan in the library resolves its pieces | pass; 80 build pieces |
| BC-2 | Cost sum of `test_cottage` | 250.5 worker-hours; logs 394 kg, clay 1,501, thatch 3,000, planks 30, nails 0.3 |
| BC-3 | Hands-on: hero Carpentry 20 builds one `wall_timber` with an axe and a saw | 11.8 game hours (42,480 game s); skill gain recorded; materials moved pile -> ghost -> built; `MassLedger` exact |
| BC-4 | No tool: the same wall without a saw | 17.7 h (x1.5) |
| BC-5 | Materials stop: 10 wall ghosts, clay in the pile for 6 | 6 built, 4 `Blocked: Materials`, site `NoMaterials` once they are done; 3 more bags arrive -> 3 resume |
| BC-6 | Wages stop: payer 450 mites, crew of 2 labourers (150 a day) | day 1 escrow 300, works; day 2 needs 300 with 150 left -> `NoWages`, jobs closed, progress kept; payer adds 600 -> resumes next 06:00 |
| BC-7 | Resume by anyone: owner dies mid-site | the site passes with the deed (inheritance); the heir resumes; a stranger is refused by `May` |
| BC-8 | Free placement validity: wall over a road, floor on a 150 pm slope, z = 4 | each refused with its reason; nothing placed |
| BC-9 | Plan fit: `burgage_middling` on an 11 x 28 m town plot | fits with the front door to the street, against the street edge; same transform on every run |
| BC-10 | Plan too big: `merchant_house` on the same plot | refused with "N m wide; the lot is 11 m" |
| BC-11 | Support: a z = 1 floor cell 4 tiles from any post | invalid at placement; with a beam whose ends rest on posts 6 cells apart: valid |
| BC-12 | Load rule: a z = 2 floor over wattle walls | refused "wattle carries one storey"; over timber valid |
| BC-13 | Collapse: remove the only post within 3 of a 3 x 3 upper floor with a person and a chest on it | groan 10 game s, then 9 cells fall; person takes a 3,000 mm fall; chest lands below; rubble = salvage + waste, mass exact; the stair portal removed and the path re-planned |
| BC-14 | Cascade: a z = 2 floor resting on z = 1 walls that rest on the fallen floor | falls in the same event, z order; deterministic on Linux and Windows |
| BC-15 | Multi-storey: build the two-storey `smithy_house` plan | stairs portal exists only after both stair pieces are built; RoomDetector v2 finds the smithy and the lodging; the pathfinder reaches z = 1 |
| BC-16 | Contract: hire a carpentry business for `test_cottage`, crew 4 | bid = materials at ask + 250.5 h x 350 x 1.45 / 10 + haul; 1/3 escrow; done in 7 +- 1 game days in dry weather; never sooner than 6.3 days |
| BC-17 | Growth visible: a growth cottage project 40% done in a visited chunk | site with ghosts; built pieces' hours = 100.2 of 250.5; pile = bought grams minus built pieces' materials; finishing gives the same `LotRecord` as Phase 3 |
| BC-18 | Save mid-site (ghosts in progress, a groan pending) | save, load, save byte-identical; the collapse happens at the same game second |
| BC-19 | Save as blueprint: build a 6 x 4 shed, Ctrl+S | JSONC parses; `BlueprintValidator` passes; placing it again reproduces the same cells |
| BC-20 | Rain: thatching in 5 mm/h rain | paused `Rain`; timber walls continue |
| BC-21 | Conservation soak: 30 sites built and half demolished over a game year | coin and mass exact; no ghost lost or duplicated |

## 8. Open questions (recommended defaults)
1. **Hero hand-building speed** (overview Q1): real worker-hours; a cottage alone is about 25 game days, with a crew of 6 about 4. Speeds up to 8x allowed while building if nothing hostile is in sight.
2. **Collapse warning:** 10 game seconds of groaning before a fall (overview Q6). Alternative: instant.
3. **Theft from site piles:** Phase 6 (crime); Phase 5 piles are safe.
4. **Piece upkeep** (thatch rots in 20 years, daub needs re-daubing): Phase 6 with fire; Phase 5 pieces do not decay.

## 9. Budgets
Live: 40 workers on ghosts <= 0.05 ms a tick. Barrier: a piece finish (delta, portals, detector for one structure) <= 0.5 ms; a collapse of 200 cells <= 3 ms. Build mode preview (fitting 8 transforms, validity of 600 cells) <= 4 ms per cursor move, render <= 1 ms a frame. H2: 200 abstract sites <= 2 ms an hour. Save: ghost 28 B, structure cell 6 B, site 200 B: a town with 30 sites and 40 altered buildings <= 600 KB.
