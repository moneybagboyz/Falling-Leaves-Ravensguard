# Ravensguard — AI Development Plan

## How to Use This Document

This document is the primary implementation handoff for an AI coding assistant building this game. Read it together with `master-game-vision.md`, which is the **normative authority** on design intent. When this plan and the vision doc conflict, the vision doc wins. When this plan is silent on a design question, resolve it using the principles in the vision doc rather than guessing or inventing.

**Core obligation before writing any code:**
1. Confirm which phase is currently active.
2. Check whether the task is in the current phase's scope.
3. If it is out of scope, note it and defer — do not implement it.

---

## Project Identity Quick Reference

| Key | Value |
|---|---|
| **Project codename** | `MEDIEVAL_WORLDSIM` |
| **Spec version** | 4.1.0 |
| **Engine** | Godot 4.5 (GDScript primary; C# optional for hot-path simulation logic) |
| **Architecture** | Hybrid ECS (Godot nodes + component resources) + domain ledger stores |
| **Content format** | JSON with schema validation; all major content in `/data/` |
| **Save format** | Versioned JSON with migration support from day one |
| **MVP target** | Playable regional sandbox — one region, not a continent |
| **WorldGen layer** | 512×512 tiles; **1 tile = 1 km²**; drives economy sim, province/settlement placement, climate |
| **Region layer** | 250×250 cells; **1 cell = 1 acre (63.6 m)**; simulation coordinate system; each cell is one economic unit |
| **Sub-region layer** | each world tile expands to a **250×250 sub-region grid** (`SubRegionGenerator`); walkable intermediate layer; building city-grid, roads, and terrain features generated here |
| **Local layer** | each building cell has a **25×25 local layout** defined in its JSON template; **1 local tile ≈ 2.5 m²**; realized by `LocalView` (P4-01) |
| **Design authority** | `master-game-vision.md` |

---

## Non-Negotiable Technical Rules

These rules must never be violated, regardless of phase or convenience:

1. **Simulation state is never owned by a scene.** Scenes read from state; they do not store it.
2. **Every entity has a stable string ID** generated at creation and never reassigned or reused.
3. **All content definitions live in `/data/` as JSON** and are loaded, parsed, and schema-validated at startup.
4. **Save files are versioned from the first save.** `schema_version` is always written. Migration functions are written at the same time as schema changes, not after.
5. **Every simulation system has a reduced-fidelity off-screen equivalent.** No system may stall because the player is not present.
6. **No hardcoded content tables.** If something can be a data definition, it must be.
7. **Debug and inspection tools are mandatory production deliverables,** not nice-to-haves.

---

## Project Structure

```
ravensguard/
├── project.godot
├── data/                          # All JSON content definitions
│   ├── schemas/                   # JSON Schema files for validation
│   ├── goods/                     # Goods and commodity definitions
│   ├── buildings/                 # Building templates
│   ├── characters/                # Trait, skill, background defs
│   ├── combat/                    # Weapon, armor, body zone defs
│   ├── factions/                  # Faction templates
│   ├── recipes/                   # Production chain recipes
│   └── world/                     # Terrain, biome, region rule defs
├── src/
│   ├── core/                      # Engine-agnostic simulation core
│   │   ├── registry/              # Entity ID and data def registries
│   │   ├── time/                  # Deterministic tick loop and time service
│   │   ├── events/                # Global event queue and dispatcher
│   │   └── save/                  # Save serialization, versioning, migration
│   ├── simulation/                # Domain simulation modules
│   │   ├── world/                 # WorldState and region graph
│   │   ├── economy/               # econ_core ledger and production pulses
│   │   ├── settlement/            # settlement_core manager
│   │   ├── party/                 # party_core movement and routing
│   │   ├── combat/                # combat_core WEGO engine
│   │   ├── character/             # PersonState, skills, traits, needs
│   │   ├── property/              # property_core ownership and income
│   │   └── law/                   # law_politics module
│   ├── worldgen/                  # Region and settlement generation
│   ├── components/                # ECS component resources for active entities
│   ├── systems/                   # ECS systems (scheduled per tick phase)
│   ├── ui/                        # All UI scenes — observers only, no sim state
│   ├── debug/                     # State inspector, sim visualizer, REPL
│   └── main/                      # Entry point, bootstrap, scene manager
├── saves/
└── tests/                         # Unit tests for simulation core
```

---

## Architecture Patterns

### ECS vs. Ledger: Decision Rule

Use **ECS (Godot nodes + attached component Resources)** for:
- Characters, combatants, companions, animals
- Moving trade parties and caravans
- Local map props, fires, projectiles, construction jobs (when loaded)

Use **domain ledger stores (plain GDScript classes / Dictionaries)** for:
- Settlement production, acreage, reserve stocks
- Regional trade balance summaries
- Historical event records
- Migration summaries, macro weather state
- Anything that runs at strategic/regional scale without a loaded scene

### Communication Between Modules

Modules must **not** call each other directly. Use:
- **`EventQueue`:** fire-and-forget events consumed by interested modules on the next tick phase.
- **`StateDeltas`:** explicit change objects passed between systems in the same tick phase.
- Scenes and UI subscribe to state change signals — they never push authoritative state back into the simulation.

### Tick Phase Order

Execute in this exact order every simulation tick. Do not reorder without updating this document:

```
1. collect_input_and_orders
2. world_pulse          (strategic: faction, weather, migration — slow cadence)
3. production_pulse     (settlement economy ledger updates)
4. movement             (party routing, local entity movement)
5. hazard_resolution    (fires, floods, structural collapses)
6. combat_resolution    (WEGO combat — fine cadence when active)
7. persistence_collapse_rehydration   (ECS entity lifecycle)
8. presentation_sync    (push state deltas to UI/scene layer)
```

Strategic pulses (phases 2–3) run on a slower cadence than tactical pulses (phases 5–6). Both must be **deterministic**: given the same seed and inputs, the simulation must produce identical output.

---

## Data Schema Conventions

- Every JSON content file must reference its schema in a `"$schema"` field.
- Schema files live in `/data/schemas/`.
- The data loader validates every file against its schema on startup.
- In **debug builds**: invalid data throws an error and halts.
- In **release builds**: invalid data logs an explicit error and skips the offending definition.
- IDs in data files are `snake_case` strings (e.g., `"iron_ingot"`, `"wheat_bushel"`).

### Minimal Example — Goods Definition

```json
{
  "$schema": "../schemas/good.schema.json",
  "id": "wheat_bushel",
  "name": "Wheat (bushel)",
  "category": "food_grain",
  "base_weight_kg": 27,
  "base_value": 4,
  "spoilage_days": 180,
  "bulk": true
}
```

### Minimal Example — Building Template

```json
{
  "$schema": "../schemas/building.schema.json",
  "id": "grain_mill",
  "name": "Grain Mill",
  "category": "production",
  "footprint_cells": 1,
  "construction_cost": { "timber": 20, "stone": 10, "labor_days": 30 },
  "upkeep_per_season": { "coin": 2 },
  "production": {
    "recipe": "mill_grain",
    "workers_required": 2,
    "output_per_worker_day": 8
  }
}
```

---

## Phase-by-Phase Implementation Plan

### Phase 0 — Technical Foundation
**Goal:** The simulation clock runs, content loads from external files, and saves serialize and reload.  
**Time estimate:** 1–2 months  
**Exit gate:** A headless test script can tick the simulation, load all data definitions, create a `WorldState`, and round-trip it through save/load.

#### Tasks

- [x] **P0-01** Set up Godot 4 project with the directory structure above.
- [x] **P0-02** Implement `DataLoader`: scans `/data/`, loads all JSON files, validates against schemas, populates a global `ContentRegistry`.
- [x] **P0-03** Implement `ContentRegistry`: keyed by content type and ID; throws on duplicate IDs; exposes `get(type, id)`.
- [x] **P0-04** Implement `EntityRegistry`: generates and resolves stable entity IDs; persists the ID→type map in saves.
- [x] **P0-05** Implement `SimulationClock`: deterministic tick counter, configurable real-time rate, pause/resume, speed multipliers, and a `tick_completed` signal.
- [x] **P0-06** Implement the tick phase scheduler in the correct 8-phase order. Each phase calls registered system hooks.
- [x] **P0-07** Implement `WorldState` and `SettlementState` as plain data classes with `to_dict()` / `from_dict()` serialization.
- [x] **P0-08** Implement `SaveManager`: writes versioned JSON saves, reads them back, dispatches migration if `schema_version` is older.
- [x] **P0-09** Implement `MigrationRunner`: applies ordered migration functions from the save's version to the current version.
- [x] **P0-10** Implement `StateInspector` debug tool: in-editor panel showing live `WorldState` and `SettlementState` fields, updated each tick.
- [x] **P0-11** Write unit tests for: data loading, ID generation, tick ordering, save round-trip, and migration.
- [x] **P0-12** Write placeholder JSON schema files and at least one example each for: goods, buildings, factions.

---

### Phase 1 — World and Region Generation
**Goal:** A region can be generated repeatedly with logical settlement placement and a visible route structure.  
**Time estimate:** 2–3 months  
**Exit gate:** Run generation 10 times with different seeds; each result has 5–12 settlements, connected routes, and no placement errors.

#### Tasks

- [x] **P1-01** Define `RegionCell` data model: `cell_id`, `terrain_type`, `elevation`, `resource_tags[]`, `total_acres`, `arable_acres`, `feature` (river, forest, etc.).
- [x] **P1-02** Implement terrain generation: noise-based heightmap → terrain type assignment → biome tags per cell.
- [x] **P1-03** Implement resource placement: distribute ore deposits, timber zones, fertile plains, and water sources according to terrain rules in data.
- [x] **P1-04** Implement settlement seed placement: score candidate cells by resource access, defensibility, and water proximity; place 5–12 settlements per region respecting minimum distance rules.
- [x] **P1-05** Implement route graph generation: connect settlements via Delaunay/minimum spanning tree + secondary routes; store as a weighted graph with terrain cost modifiers.
- [x] **P1-06** Implement `history_sim` stub: optional lightweight pre-gen pass that assigns faction ownership, ruin placement, and starting relations from templates.
- [x] **P1-07** Implement `RegionMapView` debug scene: renders generated region cells, settlement positions, and route edges with color-coded terrain and resource overlays.
- [x] **P1-08** Expose generation seed in the debug UI; allow re-running generation live.
- [x] **P1-09** Write generation unit tests: valid cell counts, settlement count within bounds, every settlement reachable via route graph.

---

### Phase 2 — Regional Economy and Settlement Core
**Goal:** Regional economy runs; settlements can prosper or decline without player input.  
**Time estimate:** 3–4 months  
**Exit gate:** Simulate 10 in-game years; at least one settlement grows and at least one declines based on resource access and trade conditions.

#### Pre-Phase 2 fixes (completed before P2-01)

These were latent bugs in Phase 1 output that would have broken Phase 2 silently:

- [x] `settlement_id` and `cell_id` were never written in `_build_world_state` — fixed; economy lookups now resolve correctly.
- [x] `SettlementState.acreage` was always zero — fixed; worldgen now seeds `worked_acres`, `fallow_acres`, `pasture_acres`, `woodlot_acres` from tile prosperity.
- [x] Route `path` stored as `Array[Vector2i]` — fixed; `_serialize_path()` converts to `[[x,y],…]` for JSON round-trip safety.
- [x] `DataLoader.DIR_TO_TYPE` missing `"population_classes"` entry — fixed; files in that folder will now load.

#### Data gaps to fill at the start of Phase 2

These do not require code changes but must exist before the economy can run:

- **Missing schemas:** `recipe.schema.json`, `population_class.schema.json` (required for DataLoader validation).
- **Missing goods:** cloth, tools, livestock, coin — see P2-01.
- **`data/recipes/`** is empty — needs at minimum: `farm_grain`, `log_timber`, `smelt_iron`. See P2-02.
- **`data/population_classes/`** is empty — needs: `peasant`, `artisan`, `merchant`, `noble`. See P2-06.
- **`SettlementState.inventory`** is empty at worldgen — P2-07 (`SettlementPulse`) must handle zero inventories gracefully, or worldgen must seed a small starter stock per tier.

#### Tasks

- [x] **P2-01** Create `good.schema.json` (already exists — verify it covers all fields) and add missing goods JSON files: cloth, tools, livestock, coin. Validate all goods load via DataLoader.
- [x] **P2-02** Create `recipe.schema.json`. Implement production recipe JSON files: fields for inputs, outputs, worker-days, required building. Add starter recipes: `farm_grain`, `log_timber`, `smelt_iron`. Agriculture recipes have no seasonal restriction.
- [x] **P2-03** Implement `AcreageLedger` per settlement: tracks `total_acres`, `arable_acres`, `worked_acres`, `fallow_acres`, `pasture_acres`, `woodlot_acres`.
- [x] **P2-04** Implement agriculture formula (year-round, no seasonal gate): `output = worked_acres × base_yield × fertility × labor × tool × disruption`. Weather modifier deferred to later phase.
- [x] **P2-05** Implement `ProductionLedger` per settlement: resolves recipes each production pulse; deducts inputs, credits outputs, logs failures.
- [x] **P2-06** Create `population_class.schema.json`. Add `data/population_classes/` JSON files: `peasant`, `artisan`, `merchant`, `noble` — each with consumption demands per good and labor contribution per worker-day.
- [x] **P2-07** Implement `SettlementPulse`: runs each regional tick — consumption, production, surplus/deficit calculation, prosperity and unrest adjustments. Pulse must handle zero-inventory gracefully on first tick and seed a minimal starter stock from tier if inventory is empty.
- [x] **P2-08** Implement price discovery: regional price for each good derived from supply/demand ratio within the settlement; export price biased by route distance.
- [x] **P2-09** Implement `TradeParty` entity: spawns from settlements with surplus goods; travels routes; delivers to destinations with deficit; resolves transaction at arrival.
- [x] **P2-10** Implement `party_core` movement: schedule parties on the route graph with travel-time derived from route cost and party speed.
- [x] **P2-11** Implement `EconomyView` debug panel: per-settlement inventory table, price list, active trade parties, and prosperity/unrest trend graph.
- [x] **P2-12** Write simulation stress tests: run 20 in-game years; assert no negative inventories without an explicit shortage event; assert price signals propagate across connected settlements.
- [x] **P2-13** Implement population growth/decline: well-fed prosperous settlements grow +0.4%/pulse; food-short or high-unrest settlements decline −0.8%/pulse. Closes Phase 2 exit gate.

---

### Phase 3 — Character and Local Map Layer
**Goal:** The player can spawn, travel locally, earn basic income, and secure shelter.  
**Time estimate:** 2–3 months  
**Exit gate:** A character can be created, spawned into a settlement, find work, get paid, and rent or claim shelter — without combat.

#### Map architecture decision

Four distinct layers with **CDDA-style lazy realization**:

```
WorldGen    512×512 tiles     1 km²/tile      continent scale; economy sim; never walked
    │
Region      250×250 cells     1 acre/cell     simulation coordinates; one economic unit per cell
    │
Sub-region  250×250 cells     ~0.25 m/cell    walkable intermediate layer per world tile (SubRegionGenerator)
    │
Local       25×25 tiles       ~2.5 m/tile     building interior; realized from JSON layout templates
```

The **Region layer** (`cell_id = "x,y"`) is the universal simulation key used by the economy, trade, and NPC systems. Every ledger, every price, every inventory is indexed by `cell_id`. This layer is always in memory.

The **Sub-region layer** is generated on demand by `SubRegionGenerator` when the player enters a world tile. Each world tile expands to a lazy 250×250 grid cached in `WorldState.region_grids`. Buildings are arranged in a **population-scaled city grid** centred on `(125, 125)` — trade/civic at the centre, production in the middle ring, housing on the outside. Roads are stamped only toward road-bearing neighbours. Terrain features (shorelines, river corridors, forest fringes, rocky outcrops, bridges) are generated from neighbouring world tile types. Walking off any edge of the 250×250 grid seamlessly loads the adjacent world tile's sub-region.

The **Local layer** is realized from each building's `local_layout` — a **25×25 ASCII grid** defined in `data/buildings/*.json`. `LocalView` (P4-01) renders this grid tile-by-tile when the player enters a building cell.

Three rendering modes:
- **WorldView**: region cells render as terrain pixels; settlements as coloured dots. No sub-region or local data loaded.
- **SettlementView**: the active world tile's 250×250 sub-region renders at cell-level. Player walks cell-by-cell. Building abbreviations label each cell. Crossing any edge loads the adjacent tile's sub-region.
- **LocalView** *(P4-01)*: a building's 25×25 local layout renders tile-by-tile. Player walks tile-by-tile. SettlementView demoted to a map overlay toggled with Tab.

Scale facts:
- Sub-region cell ≈ 0.25 m. A 25×25 building layout = ~6.25 m × 6.25 m footprint within the sub-region.
- `SettlementState.acreage` values remain world-tile counts (`worked_acres` = count of farm-tagged world tiles). Economy never operates at sub-region or local tile resolution.
- `BuildingPlacer` stamps `building_id` onto world tiles; `SubRegionGenerator` places buildings in the sub-region grid; `LocalView` fills a building cell with its 25×25 layout.
- Z-levels are per-world-tile vertical slices. Local tile z-level is inherited from the parent cell's z-level data.
- Population drives housing density: 1 house plot per 5 residents. Houses are synthesised by `SubRegionGenerator` beyond the `BuildingPlacer`-stamped count, so large cities visually fill with residential blocks.

#### Pre-Phase 3 data and infrastructure gaps

These must exist before Phase 3 code can be written. None require changes to Phase 2 systems.

- **`data/characters/` directory is missing entirely.** Needs sub-folders and populated JSON files for: backgrounds, traits, skills. Three schemas required: `background.schema.json`, `trait.schema.json`, `skill.schema.json`. Minimum starter content: 5 backgrounds (farmer, soldier, merchant, wanderer, hedge-scholar), ~10 starter traits (hardworking, cowardly, strong, etc.), ~12 skills covering farming, fighting, trading, crafting, persuasion, survival.
- **No `SceneManager` autoload.** Currently scene transitions are hardcoded per scene (WorldGenScreen → WorldView is hand-wired in `world_gen_screen.gd`). Phase 3 adds WorldView → SettlementView and SettlementView → back; a single general router is required before any settlement-view work begins.
- **`WorldState` has no player fields.** Need `player_character_id: String` and `player_location: Dictionary` (`{"cell_id": "...", "lx": 0, "ly": 0, "z_level": 0}`) added to `WorldState` and its serialisation round-trip. `cell_id` identifies the region cell; `lx`/`ly` (0–7) identify the sub-tile within the 8×8 local grid. Default to 0 — ignored until LocalView is implemented in P4-01.
- **`RegionCell` has no `building_id` field.** Building placement writes a `building_id: String` (or `""`) onto each cell. Add this field to `RegionCell` and its serialisation. Also add `z_levels: Array` to hold per-level data (ground, upper, cellar) as needed.
- **`SettlementState` has no `labor_slots`.** Work loops need to know what jobs a settlement offers. Derive from building types placed on its cells at worldgen time: a farm cell → farm labor slots; an inn cell → innkeeper/server slots. Store as `labor_slots: Array[Dictionary]` on `SettlementState`.
- **`SettlementState` has no `market_inventory`.** The ledger `inventory` is bulk simulation state. Player-facing trade needs a separate `market_inventory: Dictionary` (good_id → float quantity) refreshed each pulse as a fraction of settlement surplus.
- **NPC entities don't exist.** `SettlementState.population` is headcounts only. P3-09 will instantiate a capped subset (~20–40 per settlement) as actual entity records when the player enters. These live in `WorldState.npc_pool: Dictionary` and serialise normally.
- **`data/buildings/` directory is missing entirely.** `BuildingPlacer` (P3-06) stamps `building_id` onto world tiles. That ID must reference a building template defining: labor slots produced, whether z-levels exist, and the 25×25 local tile layout used by LocalView. Required before P3-06: `building.schema.json`; minimum starter set: `farm_plot`, `inn`, `well`, `market_stall`, `granary`, `smithy_stub`, `derelict`, `open_land`.

  **Completed building set (all at 25×25):** `smithy`, `inn`, `well`, `derelict`, `farmstead`, `farm_plot`, `grain_mill`, `granary`, `iron_mine`, `iron_smelter`, `lumber_camp`, `market`, `market_stall`, `open_land`, `house`. Schema updated with `housing_capacity` property.

#### Task ordering rationale

Tasks are ordered strictly by dependency: data → data classes → creation UI → scene routing → building placement → settlement rendering → player systems → NPC systems → dialogue. Do not reorder.

#### Tasks

- [x] **P3-01** Create character data schemas and starter content files: `background.schema.json`, `trait.schema.json`, `skill.schema.json`; populate `data/characters/` with the minimum starter sets listed above. Validate all load via DataLoader at startup.
- [x] **P3-02** Implement `PersonState`: full data model with `person_id`, `name`, `background_id`, `attributes{}`, `traits[]`, `skills{}` (id → `{level, progress}`), `body_state{}`, `needs{}`, `social_links[]`, `reputation{}`, `ownership_refs[]`, `active_role`, `location{}`. Add `to_dict()` / `from_dict()`. Add to `WorldState`: `characters: Dictionary`, `player_character_id: String`, `player_location: Dictionary` (`{"cell_id": "", "lx": 0, "ly": 0, "z_level": 0}`). Confirm save round-trip including `lx`/`ly`.
- [x] **P3-03** Implement character creation screen (`CharacterCreationScreen`): background picker shows trait grants and starting skill bonuses; player assigns a small attribute point budget; screen confirms and writes the new `PersonState` into `WorldState.characters`, sets `world_state.player_character_id` and `player_location` (`cell_id` = anchor cell of chosen starting settlement, `lx`/`ly`/`z_level` = 0), then transitions to `WorldView`.
- [x] **P3-04** Implement skill system: each skill entry has `level: int` and `progress: float`; successful relevant actions call `PersonState.award_skill_xp(skill_id, amount)`; when `progress >= 1.0` the level increments and progress resets; level thresholds (5, 10, 20, …) flag perk unlock eligibility (perks themselves deferred to Phase 6).
- [x] **P3-05** Implement `SceneManager` autoload: stack-based scene router with `push_scene(path, params)`, `pop_scene()`, `replace_scene(path, params)`. Migrate existing WorldGenScreen → WorldView transition to use it. Add "► ENTER SETTLEMENT" button to `WorldView` (enabled when a settlement is selected) that calls `SceneManager.push_scene("settlement_view", {settlement_id: ...})`. Esc in settlement view calls `pop_scene()` returning to WorldView.
- [x] **P3-06** Implement `BuildingPlacer`: runs at worldgen time after `SettlementPlacer`. For each settlement, iterates cells within its territory radius and stamps a `building_id` onto each using a deterministic RNG seeded from `settlement_id`. Building type distribution is tier-scaled: tier-0 gets 1 inn, 1 well, 2 farm plots, 2 houses; tier-4 gets market, multiple inns, smithy, granary, many farm plots, many houses (fill). Writes `labor_slots` and seeds `market_inventory` onto the `SettlementState`. Introduces no new scene or UI — pure data pass.
- [x] **P3-07** Implement `SettlementView` scene: renders the active world tile's **250×250 sub-region grid** (generated by `SubRegionGenerator`) at cell level. Building cells show an abbreviation label keyed to `building_id`; terrain colours keyed to `terrain_type`. Player pawn walks cell-by-cell (8-directional); crossing any edge of the 250×250 grid loads the adjacent world tile's sub-region seamlessly. Interaction cursor highlights current cell. Esc → `SceneManager.pop_scene()`.

  **SubRegionGenerator features implemented:**
  - Population-scaled city grid: `ceil(population / 5)` house plots synthesised; buildings sorted trade/civic → storage → production → housing; compact 2-cell-step grid centred on `(125, 125)`.
  - Roads stamped only toward road-bearing neighbour tiles; terminal tiles get a short stub.
  - **Terrain features:** shoreline transitions (20-cell feather of `coast`/`shallow_water` near water neighbours); river corridors (7-cell jittered channel from river-bearing neighbours, bridged where roads cross); forest fringes (scatter within 25 cells of forest neighbours); rocky outcrops (noise-driven `mountain` cells in hills/mountain tiles).
  - `source_wt_key` on each building cell for correct labor/NPC/dialogue resolution.
- [x] **P3-08** Implement z-level support baseline: each `RegionCell` stores `z_levels: Array` — index 0 = ground, 1 = upper floor, -1 = cellar (sparse; only inn and granary cells have them in Phase 3). `SettlementView` renders one z-level at a time; staircase building tiles trigger z-level change. Player z-level stored in `player_location.z_level`.
- [x] **P3-09** Implement NPC pool initialization: on `SettlementView` entry, read `SettlementState.population` headcounts and instantiate up to 40 NPC records in `WorldState.npc_pool` for that settlement if not already present. Each NPC: `npc_id`, `name`, `settlement_id`, `population_class`, `cell_id`, `z_level`, `schedule_state` (`working/resting/wandering`), `labor_slot_id`. Cull settlement NPCs on map exit to keep save size bounded (persist only the player's known social links).
- [x] **P3-10** Implement basic work loops: player interacts with a building cell to see its open labor slots (from `SettlementState.labor_slots`); "Apply for work" assigns the player to a slot; each strategic tick while assigned, the player earns wages deducted from settlement coin, and relevant skill XP is awarded via `award_skill_xp`.
- [x] **P3-11** Implement `NeedsComponent`: `hunger: float`, `fatigue: float`, `temperature_stress: float`; all decay each local tick; `hunger` is reduced by consuming food items from personal inventory; `fatigue` recovers during rest in shelter. Hunger above threshold applies a productivity penalty to work output. Stored on `PersonState.needs`.
- [x] **P3-12** Implement housing: inn cell interaction offers room rental at a daily/weekly coin rate; rented room sets `PersonState.shelter_status = "rented"`; derelict cells (tagged `building_id = "derelict"`) can be claimed as free shelter; `shelter_status` drives fatigue recovery rate in `NeedsComponent`.
- [x] **P3-13** Implement NPC daily schedules: each NPC with a `labor_slot_id` transitions `schedule_state` — `working` during day ticks, `resting` at night, `wandering` briefly at midday; NPCs without slots wander or idle at their home cell. Schedules advance on the local tick, not the strategic tick.
- [x] **P3-14** Implement basic dialogue interface: player interacts with any NPC to open a dialogue panel; functional options: "Inquire about work" (lists open labor slots at this NPC's building cell), "Buy goods" (shows `market_inventory` with prices from `SettlementState.prices`); all other options ("Rumors", "Ask about factions") are stubbed — return "I have nothing to say." Deepen in Phase 6.

---

### Phase 4 — Combat Vertical Slice
**Goal:** Small battles are readable, tactically influenced, and mechanically deep.  
**Time estimate:** 3–4 months  
**Exit gate:** A 4v4 fight on a tactical map resolves with body-part injuries, armor mitigation, weapon effects, WEGO order influence, and a clear winner. No crashes, no degenerate loops.

#### Tasks

- [ ] **P4-01** Implement `LocalView` scene: when the player interacts with a building cell in `SettlementView`, parse its `local_layout` (25×25 ASCII grid from `data/buildings/*.json`) and render it tile-by-tile. Tile key: `#` wall · `.` floor · `+` door · `~` water · `,` dirt · `_` open · `b` bed · `t` table · `c` counter · `s` shelf · `f` forge/fire · `^` stairs up · `v` stairs down · `%` crop · `*` rubble · `x` chest · `A` anvil · `M` millstone · `p` pillar. Player pawn moves tile-by-tile. `player_location.lx`/`ly` become live. `LocalView` is the default view when inside a building; `SettlementView` demoted to a map overlay toggled with Tab. Exiting any door tile returns the player to `SettlementView` at the parent cell. _(Large standalone task — can be completed independently of combat tasks below.)_
- [ ] **P4-02** Implement combat scenario bootstrap: a debug scene (`CombatTestScene`) that hard-spawns two teams of 4 combatants on a flat 20×20 test map with preset weapon/armor loadouts, bypassing full game flow. Used to verify P4-07 through P4-15 in isolation without requiring character creation, settlement entry, and hostile encounter. Remove or gate behind a debug flag before release.
- [ ] **P4-03** Define body zone data: head, neck, chest, abdomen, left/right arm, left/right leg — each with hit probability weight, wound severity thresholds, and critical effect conditions. Store in `data/body_zones/*.json` with a `body_zone.schema.json`.
- [ ] **P4-04** Define weapon data: reach class, damage type, swing/thrust momentum, attack speed, stamina cost, minimum skill. Store in `data/weapons/*.json` with a `weapon.schema.json`. Minimum starter set: dagger, short sword, spear, axe, club.
- [ ] **P4-05** Define armor coverage data: per-zone coverage values, material (`leather`, `mail`, `plate`), encumbrance, durability. Store in `data/armor/*.json` with an `armor.schema.json`. Minimum starter set: gambeson, leather vest, mail hauberk, iron helm.
- [ ] **P4-06** Add `stamina` float to `PersonState`; extend it into `CombatantState`. Stamina drains per attack action based on weapon `stamina_cost`; recovers at rest each WEGO turn. Low stamina applies accuracy and speed penalties. Stamina is distinct from fatigue (`NeedsComponent`) — fatigue is a slow strategic drain; stamina is a fast tactical resource.
- [ ] **P4-07** Implement `CombatantState`: `combatant_id`, `team_id`, `tile_pos`, `z_level`, `body_zones{}`, `equipment_refs[]`, `planned_orders[]`, `resolved_actions[]`, plus `pain: float`, `bleeding: float`, `shock: float`, `stamina: float`.
- [ ] **P4-08** Implement basic inventory and equipment UI: player can pick up item entities from the ground (`x` chest tiles in `LocalView`), view carried items, and equip/unequip weapons and armor to their `PersonState.equipment_refs`. Without this, `CombatantState.equipment_refs` will always be empty and damage calculations will always assume unarmed/unarmored.
- [ ] **P4-09** Implement combat trigger system: player can initiate attack on a hostile NPC or group from `SettlementView` (e.g. bandits, enemy soldiers); transition to `CombatView` with both sides converted to `CombatantState` records. Also handle the inverse: hostile groups within range initiative an attack on the player, forcing the same transition.
- [ ] **P4-10** Implement WEGO order system (player side): during the planning phase the player assigns each friendly combatant one order (move-to, attack-target, defend, retreat); orders are locked in and all combatants resolve simultaneously. UI shows planned paths and targets before commit.
- [ ] **P4-11** Implement enemy AI orders: each enemy combatant selects orders during the planning phase using simple behaviour rules — advance if out of reach, attack nearest reachable target, retreat if shock threshold exceeded. AI must produce legal orders and must not loop infinitely.
- [ ] **P4-12** Implement attack resolution: attacker reaches target → roll hit location weighted by `body_zone` hit probability → apply weapon momentum vs. zone armor coverage → derive wound severity (`none / graze / wound / severe / lethal`) → apply wound to body zone.
- [ ] **P4-13** Implement wound effects: wounds accumulate `bleeding` rate, `pain` level, and mobility penalty per zone; `shock` threshold triggers incapacitation; death threshold is explicit; multiple wounds stack.
- [ ] **P4-14** Implement terrain effects: elevation advantage (+hit chance), obstacle blocking (line-of-sight / melee reach), difficult ground speed penalty.
- [ ] **P4-15** Implement formation effects: adjacent allies in formation confer attack/defense bonuses; broken formation removes bonus.
- [ ] **P4-16** Implement `CombatView`: tactical map renderer showing combatant positions, order overlays, wound status indicators, and turn-phase indicator (planning / resolving / results).
- [ ] **P4-17** Implement post-battle state resolution: apply wounds back to `PersonState`; remove dead entities from world state; generate loot pool from defeated combatants; trigger equipment recovery prompt.
- [ ] **P4-18** Implement recovery: wound healing over time driven by rest, shelter quality, and care skill; permanent injury flag for severe wounds that do not fully heal.
- [ ] **P4-19** Write combat unit tests: valid hit distribution across body zones sums to 1.0, armor mitigation math matches expected values, no infinite loops in WEGO resolution, stamina drain/recovery stays in [0, 1] range.

---

### Phase 5 — Snowball Systems
**Goal:** The player can create a self-reinforcing income or power loop.  
**Time estimate:** 3–4 months  
**Exit gate:** A character can own a workshop, hire a worker, produce goods, sell them, and reinvest profit — demonstrably compounding over 30 in-game days.

#### Tasks

- [ ] **P5-01** Implement `property_core`: tracks ownership of buildings and land by entity ID; assigns income rights and upkeep obligations.
- [ ] **P5-02** Implement workshop ownership: player can purchase or build a production building; assign workers; collect surplus output for sale.
- [ ] **P5-03** Implement player trade: buy from settlement market inventory, sell surplus goods; price responds to trade volume.
- [ ] **P5-04** Implement camp founding: player can establish a camp outside a settlement; camp has a small inventory and can host followers.
- [ ] **P5-05** Implement follower system: NPCs can be recruited as followers; followers have roles (guard, laborer, assistant); they consume wages and food; they contribute labor or protection.
- [ ] **P5-06** Implement basic recruitment: players can approach unemployed or mercenary NPCs with a wage offer; reputation and faction standing affect acceptance.
- [ ] **P5-07** Implement small armed group management: group has a size, equipment summary, pay ledger, and morale-lite value; unpaid groups lose members.
- [ ] **P5-08** Implement `OwnershipView` UI: shows all player-owned assets, income/upkeep breakdown, and current follower roster.

---

### Phase 6 — Regional Sandbox Alpha
**Goal:** The game is fun as a regional sandbox; all core loops are connected; no critical missing links.  
**Time estimate:** 3–5 months  
**Exit gate:** Gate E — the regional sandbox stands on its own as a product core. A session starting from character creation through 60 in-game days is completable without crashes, softlocks, or obviously broken systems.

#### Tasks

- [ ] **P6-01** Implement faction pressure: faction aggression, territory claims, and inter-faction conflict drive NPC army movements and settlement sieges at a coarse level.
- [ ] **P6-02** Implement basic law system: crime detection, local guard response, bounty assignment, legal status effects on market access and housing.
- [ ] **P6-03** Add profession depth: at least 5 distinct viable profession paths (soldier, trader, farmer, artisan, outlaw) with meaningful differentiation in gameplay affordances.
- [ ] **P6-04** Build management UI pass: settlement overview, personal ledger, property management, follower management, local map quick-travel.
- [ ] **P6-05** Stabilization pass: fix all known simulation edge cases, negative inventory bugs, economy collapse failure modes, and combat degenerate states.
- [ ] **P6-06** Performance audit: profile a 10-year simulation run; identify and fix any O(n²) tick costs; establish frame budget targets.
- [ ] **P6-07** Implement `SimulationVisualizer`: time-series graphs of settlement prosperity, regional trade volume, faction territory, and player net worth — viewable in debug mode.
- [ ] **P6-08** Implement permadeath and heir continuation as optional settings: on player death, optionally spawn an heir with partial inheritance.
- [ ] **P6-09** First content expansion pass: add at least 20 additional goods, 15 building types, and 3 additional factions using data definitions only — no code changes.

---

### Phase 7 — Scale-Out Architecture *(deferred until Gate E is passed)*

Do not begin this phase until Phase 6 is complete and the regional sandbox is confirmed fun.

- [ ] Multi-region streaming: load/unload regions around the player; maintain strategic-fidelity simulation for unloaded regions.
- [ ] Larger army support: scale combat subsystem to 50+ combatants per side with acceptable performance.
- [ ] Broader faction logic: dynastic succession hooks, treaty system, claim escalation.
- [ ] Long-run simulation stress tests: 100 in-game years, multiple regions.

---

### Phase 8 — Long-Tail Expansions *(deferred)*

Implement only after Phase 7 is stable:

- Deep dynastic politics and succession
- Religion and culture simulation layers
- Full siege engineering
- Dungeon subsystem expansion
- Supernatural world events and undead incursions

---

## Coding Conventions

### GDScript Style

- Use `snake_case` for variables, functions, and file names.
- Use `PascalCase` for class names and node names.
- Prefer typed GDScript (`var x: int`, `func foo(a: String) -> void:`).
- No magic strings for IDs — use constants defined in the relevant registry module.
- Every simulation function that modifies state must be **pure with respect to side effects** where feasible: take state in, return deltas out, do not write to singletons inside the function.

### File Naming

- Simulation domain files: `econ_ledger.gd`, `settlement_pulse.gd`, `combat_resolver.gd`
- Data classes: `world_state.gd`, `settlement_state.gd`, `person_state.gd`
- ECS components: `body_component.gd`, `identity_component.gd`
- ECS systems: `movement_system.gd`, `combat_system.gd`
- UI scenes: `settlement_view.tscn`, `character_sheet.tscn`

### Commit Discipline

- One logical change per commit.
- Commit message format: `[Phase N] short description` (e.g., `[P0] implement deterministic tick loop`).
- Never commit a broken save round-trip or broken schema validation.

---

## Testing Requirements

Every simulation module must have tests covering:

1. **Happy path:** nominal input produces correct output.
2. **Edge cases:** zero inputs, empty inventories, dead entities, missing route connections.
3. **Save round-trip:** state before save equals state after load.
4. **Determinism:** running the same tick twice with the same seed produces identical output.

Use Godot's built-in test runner or `gut` (Godot Unit Testing) framework. Test files live in `/tests/` mirroring the `/src/` structure.

---

## Deferral Register

Items explicitly deferred and not to be implemented until their gate condition is met:

| Item | Deferred Until |
|---|---|
| Deep religion and culture simulation | Phase 8 |
| Full dynastic politics | Phase 7–8 |
| Rich quest structure | Never before Gate E |
| Continental-scale battles (1500+ units) | Phase 7+ |
| Full siege engineering | Phase 8 |
| Supernatural world events / undead invasions | Phase 8 |
| Morale, panic, routing, surrender (in combat) | Phase 6+ |
| Multiplayer / async world sharing | Post-release |
| Global history generation (full pre-gen pass) | Phase 1 stub only |

---

## Key Design Invariants (Never Violate)

These are drawn directly from `master-game-vision.md` and must be checked before any architectural decision:

1. The player is not narratively special. No plot armor, no quest markers, no forced tutorial events.
2. The world does not pause for the player. Simulation continues at reduced fidelity when the player is absent.
3. Combat is WEGO and order-driven. The player commands, not directly acts, during battles.
4. One economic unit = one world tile at region scale. The walkable sub-region grid (250×250 per world tile) and 25×25 local building layouts are presentation layers only — economy never operates below world-tile resolution.
5. Bulk commodity simulation uses ledgers, not discrete item objects, until the player enters a local context.
6. Production is causally realistic: outputs depend on land, labor, tools, inputs, and disruption. Agriculture runs year-round (no seasonal gate); weather/season modifiers are deferred to a later phase.
7. Save files are always versioned and always migratable.
8. Four layers, never collapsed: WorldGen (512×512, 1 km²) → Region (250×250 world tiles, 1 acre each) → Sub-region (250×250 walkable cells per world tile, `SubRegionGenerator`) → Local (25×25 building interior from JSON layout). Economy runs on region layer only. Sub-region and local are realized on demand, freed on exit.
