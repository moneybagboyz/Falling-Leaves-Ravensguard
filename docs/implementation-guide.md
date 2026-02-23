# Ravensguard — Implementation Guide (Godot 4.5 / GDScript)

> **Audience**: Developers working from the Game Design Essence document.  
> **Purpose**: Maps every design concept to concrete Godot scene trees, GDScript data structures, and implementation priorities.  
> **Rule**: Follow the phase order. Each phase produces a runnable, testable build.

---

## Build Status  *(updated 2026-02-22)*

| # | Component | Status | Notes |
|---|---|---|---|
| 1a | `world_data.gd` — tile grid with all arrays | ✅ Done | `altitude`, `temperature`, `precipitation`, `drainage`, `biome`, `prosperity`, `flow`, `is_river`, `is_lake`, `sea_level` |
| 1b | `tile_registry.gd` — BiomeType enum | ✅ Done | Includes `RIVER` and `LAKE` biomes |
| 1c | `world_gen_params.gd` — parameter container | ✅ Done | 12 tunable params, `duplicate()` for thread safety, 15 named presets |
| 1d | `world_generator.gd` — tectonic 3-noise pipeline | ✅ Done | 3-noise blend (base FBM + crust plate + tectonic spike), radial falloff, sea_ratio quantile |
| 1e | `hydrology.gd` — rivers + lakes | ✅ Done | D8 single-flow accumulation; BFS lake flood-fill with drain-abort guard |
| 1f | `map_renderer.gd` — 8 view modes | ✅ Done | BIOME (altitude-shaded), ALTITUDE, TEMPERATURE, PRECIPITATION, DRAINAGE, PROSPERITY, FLOW, PROVINCES |
| 1g | `world_map.gd` — interactive UI | ✅ Done | Sidebar with 13 HSliders (5 sections incl. Province Count 8–60), 15 preset buttons, 8 view-mode buttons [Q]=Provinces, zoom/pan, hover/pin |
| 1h | Three-tier map system (Region + Local) | ✅ Done | See §4.9; adjacency-based coast fix applied |
| 2a | `province_generator.gd` — two-tier Poisson placement | ✅ Done | Hub Poisson (min-sep 6) → Dijkstra provinces → spoke Poisson (min-sep 3); 2–5 spokes/province |
| 2b | `road_generator.gd` — two-phase Dijkstra road network | ✅ Done | Phase 1: hub→spoke intra-province; Phase 2: hub→2 nearest hub in adjacent provinces; ROAD_DISCOUNT corridor merging; `connectivity_rate` + population bonuses; `assign_tiers()` (Hamlet→Metropolis) |
| 2c | Road overlays — world, region, and local maps | ✅ Done | World map: Bresenham 1-px tan lines; Region map: 1-px centre→edge per connected neighbour; Local map: 3-px wide corridors (±1 perpendicular offset) |
| 2d | Settlements + Economy core | ⬜ | Scripts scaffolded; see §5 — **this is the next milestone** |
| 3 | Factions + Overworld Agents | ⬜ | See §6 |
| 4 | Player Character | ⬜ | See §7 |
| 5 | Tactical Battle | ⬜ | See §8 |
| 6 | Siege + Auto-resolution | ⬜ | See §9 |
| 7 | Full UI Pass | ⬜ | See §10 |

---

## What's Next  *(as of 2026-02-22)*

World generation (Phase 1 + 1.5) and the road/province infrastructure (Phase 2a–2c) are complete. The next work block is **Phase 2d — Settlements and Economy** (§5).

### Game flow overview

```
Main Menu  →  World Generator (sliders + presets + generate)
                └─ "Start Game" button
                      └─ Overworld (play mode: same map, no sliders, GameClock ticking)
                              ├─ double-click world tile  →  Region view
                              │       └─ double-click region tile  →  Local / City view
                              └─ Pause / Save / Quit
```

`main.tscn` currently goes **directly** to the world generator with no menu. When Phase 2 starts, `world_map.gd` needs a **"Start Game"** button that:
1. Hides the generator sidebar (sliders, presets, mode buttons).
2. Shows the overworld HUD (GameClock display, settlement list, etc.).
3. Calls `WorldState.start_game()` which wires `GameClock.daily_pulse` to `WorldState._on_daily_pulse()` and begins ticking.

A proper **Main Menu** scene (New Game / Load / Quit) is scheduled for Phase 7 UI pass. Until then, the world generator *is* the "new game" screen, and "Start Game" is the only button that matters.

### Immediate next steps (in order)

1. **Add "Start Game" button to `world_map.gd`** — visible only after a world has been generated; on press, hide the generator sidebar and call `WorldState.start_game()`.
2. **Wire `Settlement` into `WorldState`** — `WorldState.settlements` is already populated by `ProvinceGenerator.place_settlements()`; confirm the array is reachable from the play-mode tick.
3. **Implement `Production.calculate(settlement)`** — farming, mining, fishing, and forestry formulas using `arable_acres`, `mining_slots`, etc. (see §5.3).
4. **Implement `Market.consume(settlement)` and `Market.update_prices(settlement)`** — 14-day rolling price history, supply/demand curve (see §5.4).
5. **Implement `GovernorAI.decide(settlement)`** — build-queue logic, labor allocation (see §5.5).
6. **Hook everything into `GameClock.daily_pulse`** — the signal chain in §2.3 is the target wiring.
7. **Console smoke-test** — print daily totals to Output so the tick loop is confirmed working before any UI work.

### Backlog (do after Phase 2 tick is running)

- `SiteGenerator` — scatter resource sites (farmsteads, mines, camps) that feed `arable_acres` / `mining_slots` into settlements (§4.10).
- Name generation — replace placeholder settlement/province names with Markov-chain generated names (TODO §1).
- Culture zones — needed before factions can inherit expansion type and language (TODO §2).
- Sea routes — coastal settlement connectivity via `sea_route_network` (TODO §5).
- Street-level local map layout — stamp real town grids into `LocalMapData` based on settlement tier (TODO §6).

---

## Table of Contents

1. [Project Folder Architecture](#1-project-folder-architecture)  
2. [Build Status](#build-status--updated-2026-02-22)  
3. [Core Architecture Patterns](#2-core-architecture-patterns)  
4. [Phase Roadmap](#3-phase-roadmap)  
5. [Phase 1 — World Generation ✅](#4-phase-1--world-generation--complete)  
6. [Phase 2 — Settlements and Economy](#5-phase-2--settlements-and-economy)  
7. [Phase 3 — Factions and Overworld Agents](#6-phase-3--factions-and-overworld-agents)  
8. [Phase 4 — The Player](#7-phase-4--the-player)  
9. [Phase 5 — Tactical Battle](#8-phase-5--tactical-battle)  
10. [Phase 6 — Siege and Auto-Resolution](#9-phase-6--siege-and-auto-resolution)  
11. [Phase 7 — UI Layer](#10-phase-7--ui-layer)  
12. [Data Tables](#11-data-tables)  
13. [The Time Model](#12-the-time-model)  
14. [Critical Invariant Checks](#13-critical-invariant-checks)  
15. [System Wiring Diagram](#14-system-wiring-diagram)

---

## 1. Project Folder Architecture

```
res://
├── autoloads/
│   ├── GameClock.gd          # Singleton — turn counter, pulse dispatcher
│   ├── WorldState.gd         # Singleton — holds WorldData, all settlements, factions
│   └── MaterialDB.gd         # Singleton — loads material table on startup
│
├── scenes/
│   ├── main.tscn             # Root: mode switcher (MENU/OVERWORLD/CITY/BATTLE)
│   ├── overworld/
│   │   ├── WorldMapView.tscn  # Tier 1 — full world (existing world map scene)
│   │   ├── RegionMapView.tscn # Tier 2 — 8×8 region tiles for one world tile
│   │   ├── LocalMapView.tscn  # Tier 3 — 48×48 local tiles for one world tile
│   │   └── OverworldCamera.tscn
│   ├── city/
│   │   └── CityScreen.tscn   # Settlement management UI
│   ├── battle/
│   │   ├── BattleScene.tscn  # Tactical grid
│   │   └── BattleUnit.tscn   # Single unit instance
│   └── ui/                   # Reusable control panels
│
├── scripts/
│   ├── world/
│   │   ├── world_data.gd         # (existing) — ADD new arrays; keep noise layers intact
│   │   ├── world_generator.gd    # (existing) — ADD terrain + province steps at end
│   ├── world_gen_params.gd    # (existing) 12 params, 15 presets
│   │   ├── tile_registry.gd      # (existing) biome types
│   │   ├── terrain_classifier.gd # Biome → TerrainType conversion
│   │   ├── province_generator.gd # Dijkstra province growth (uses existing arrays)
│   │   ├── hydrology.gd          # Flow accumulation, river detection (uses altitude[])
│   │   ├── region_data.gd        # 8×8 region tile grid for one world tile
│   │   ├── region_generator.gd   # Derives region from WorldData tile + detail noise
│   │   ├── local_map_data.gd     # 48×48 local tile grid for one world tile
│   │   └── local_map_generator.gd # Derives local from RegionData + micro-noise
│   │
│   ├── settlement/
│   │   ├── settlement.gd         # The autonomous economic agent
│   │   ├── governor_ai.gd        # Build/trade/labor decisions
│   │   ├── market.gd             # Inventory, pricing, 14-day history
│   │   └── building.gd           # Building type + level data
│   │
│   ├── economy/
│   │   ├── resource_registry.gd  # All resource base prices + metadata
│   │   ├── production.gd         # Farming/mining/fishing formulas
│   │   └── caravan.gd            # Physical trade agent on the overworld
│   │
│   ├── faction/
│   │   ├── faction.gd            # Treasury, relations, personality
│   │   └── faction_ai.gd         # Weekly decision loop
│   │
│   ├── unit/
│   │   ├── unit.gd               # Body, blood, equipment, morale
│   │   ├── body_part.gd          # Tissues, HP, vital flags
│   │   ├── tissue.gd             # Layer data, damage absorption
│   │   └── equipment.gd          # Weapon / armor + material reference
│   │
│   ├── battle/
│   │   ├── battle_manager.gd     # Runs the tactical battle loop
│   │   ├── initiative_queue.gd   # Sorts units by speed + jitter
│   │   ├── damage_resolver.gd    # Penetration, energy, bleed
│   │   └── unit_ai.gd            # Battle FSM per unit
│   │
│   ├── player/
│   │   └── player_controller.gd  # Input, movement, action dispatch
│   │
│   └── ui/
│       ├── overworld_hud.gd
│       ├── city_ui.gd
│       └── battle_hud.gd
│
└── data/
    ├── materials.json            # 18 materials: hardness, density, impact_yield, shear_yield, elasticity
    ├── resources.json            # 40+ resources: category, weight, producers, consumers, base_price
    ├── buildings.json            # 23 building types in 3 categories (industry/military/civil); 10-level definitions
    ├── items.json                # Weapons, armour, shields, ammo, transport; damage physics per item
    ├── siege_engines.json        # 5 engine types: ballista, catapult, battering_ram, siege_tower, trebuchet
    └── unit_archetypes.json      # 8 archetypes: attributes, skills, equipment slot map, min_tier
```

---

## 2. Core Architecture Patterns

### 2.1 Autoload Singletons

Three autoloads carry global state. Register them in **Project → Project Settings → AutoLoad**:

```gdscript
# autoloads/GameClock.gd
extends Node

signal hourly_pulse(turn: int)
signal daily_pulse(turn: int)
signal weekly_pulse(turn: int)

var turn: int = 0          # Only ever increments. Never set backwards.

func advance(hours: int = 1) -> void:
    for _i in range(hours):
        turn += 1
        emit_signal("hourly_pulse", turn)
        if turn % 24 == 0:
            emit_signal("daily_pulse", turn)
        if turn % 168 == 0:
            emit_signal("weekly_pulse", turn)
```

```gdscript
# autoloads/WorldState.gd
extends Node

var world_data: WorldData = null          # Tile grid (from existing world_generator)
var settlements: Array[Settlement] = []   # All settlement instances
var factions:    Array[Faction]    = []   # All faction instances
var armies:      Array[Army]       = []   # All army instances on overworld
var caravans:    Array[Caravan]    = []   # All caravan instances on overworld

func get_settlement_at(tx: int, ty: int) -> Settlement:
    # Linear scan is fine for ≤ 400 settlements on an 80×80 grid
    for s in settlements:
        if s.tile_x == tx and s.tile_y == ty:
            return s
    return null

func get_province(province_id: int) -> Dictionary:
    return world_data.provinces[province_id]
```

```gdscript
# autoloads/MaterialDB.gd
extends Node

var materials: Dictionary = {}  # material_id -> MaterialData

func _ready() -> void:
    var f := FileAccess.open("res://data/materials.json", FileAccess.READ)
    var raw: Array = JSON.parse_string(f.get_as_text())
    for entry in raw:
        materials[entry["id"]] = entry
    f.close()
```

### 2.2 Resource vs. RefCounted vs. Node

| Type | Use for |
|---|---|
| `Resource` | Data objects saved to disk: Settlement, Faction, Unit snapshot |
| `RefCounted` | Lightweight data structs never placed in the scene tree: Market, BodyPart, Tissue |
| `Node` / `Node2D` | Visible overworld agents: CaravanSprite, ArmySprite, PlayerSprite |

> **Rule**: Game logic never lives in a Node subclass. Nodes are display-only wrappers.  
> All computation lives in Resource or RefCounted classes, driven by GameClock signals.

### 2.3 Signal Flow

```
GameClock.daily_pulse
    → WorldState._on_daily_pulse()
        → for each settlement: settlement.daily_tick()
            → production.calculate(settlement)
            → market.consume(settlement)
            → governor_ai.decide(settlement)
            → market.update_prices(settlement)

GameClock.weekly_pulse
    → WorldState._on_weekly_pulse()
        → for each faction: faction_ai.decide(faction)
```

---

## 3. Phase Roadmap

| Phase | Delivers | Testable milestone | Status |
|---|---|---|---|
| **1** | World generation — noise pipeline, hydrology, parameters, interactive map | Press R → colored map; hover/click tiles; 15 presets; 7 view modes | ✅ **Complete** |
| **1.5** | Three-tier map system (Region + Local drill-down) | Double-click world tile → region view; double-click region tile → local view | ✅ **Complete** |
| **2** | Settlements + economy tick | Console shows daily production/consumption numbers | ⬜ |
| **3** | Factions + AI agents on overworld | Armies and caravans move; wars start | ⬜ |
| **4** | Player character | WASD movement, enter city, buy goods | ⬜ |
| **5** | Tactical battle | Two squads fight with anatomy + bleeding | ⬜ |
| **6** | Siege + auto-resolution | AI armies siege and capture towns | ⬜ |
| **7** | Full UI pass | All screens polished | ⬜ |

Start from Phase 1 and complete each fully before moving on.

---

## 4. Phase 1 — World Generation  ✅ Complete

> **All subsystems below are implemented and running.**  
> The next step is Phase 2 (Settlements). Do not re-implement anything in this section.

### 4.0 What Was Built

Phase 1 is a fully interactive world generator. Pressing **R** generates a new map on a background thread. The sidebar exposes every tunable parameter via sliders and 15 named presets. Hovering over a tile shows its stats; clicking pins the info panel.

#### Scripts

| File | Role |
|---|---|
| `scripts/world_gen_params.gd` | All 12 tunable parameters + `duplicate()` for thread safety + 15 presets |
| `scripts/world_data.gd` | 2D array container for all tile data |
| `scripts/tile_registry.gd` | `BiomeType` enum (includes `RIVER`, `LAKE`) + colours + names |
| `scripts/world_generator.gd` | Full generation pipeline (threaded) |
| `scripts/hydrology.gd` | D8 flow accumulation, river marking, BFS lake detection |
| `scripts/map_renderer.gd` | Converts `WorldData` → `ImageTexture` for 7 view modes |
| `scripts/world_map.gd` | Main scene controller: sidebar, sliders, presets, zoom/pan, tile info |

---

### 4.1 WorldData Arrays

`world_data.gd` stores every layer as a plain `Array` of Arrays (row-major). All arrays are created in `_init(width, height)` via a `_make_grid` helper.

| Array | Type | Populated by |
|---|---|---|
| `altitude` | `float` 0–1 | `WorldGenerator` (tectonic 3-noise) |
| `temperature` | `float` 0–1 | `WorldGenerator` |
| `precipitation` | `float` 0–1 | `WorldGenerator` |
| `drainage` | `float` 0–1 | `WorldGenerator` |
| `biome` | `BiomeType` int | `WorldGenerator` (Whittaker) |
| `prosperity` | `float` 0–1 | `WorldGenerator` |
| `flow` | `float` ≥0 | `Hydrology` |
| `is_river` | `bool` | `Hydrology` |
| `is_lake` | `bool` | `Hydrology` |
| `sea_level` | `float` (scalar) | `WorldGenerator` — `sea_ratio`-th percentile of altitude |

---

### 4.2 WorldGenParams — Parameter Container

`world_gen_params.gd` is a plain `RefCounted` class. It is instantiated once, passed to generation via `duplicate()` so the background thread owns its own copy.

```gdscript
# scripts/world_gen_params.gd
class_name WorldGenParams extends RefCounted

# Shape
var sea_ratio:       float = 0.40   # fraction of tiles below sea_level
var island_falloff:  float = 1.8    # strength of radial edge-falloff

# Terrain noise
var noise_frequency: float = 1.8
var noise_octaves:   int   = 6
var noise_factor:    float = 0.5    # weight of base FBM noise
var crust_factor:    float = 0.3    # weight of crust-plate noise
var tectonic_factor: float = 0.2    # weight of tectonic-spike noise
var crust_frequency: float = 0.8    # scale of crust-plate noise

# Climate
var temp_bias:   float = 0.0   # added to raw temperature after normalisation
var precip_bias: float = 0.0   # added to raw precipitation after normalisation

# Hydrology
var river_threshold: float = 60.0  # flow accumulation needed to mark a river
var lake_fill_depth: float = 0.03  # BFS flood height above local min
```

**15 built-in presets** (accessed via `WorldGenParams.PRESETS: Array[Dictionary]`):  
Default, Pangaea, Archipelago, Ice Age, Desert, Lush, Ring of Fire, Ancient Craton, Rift Valley, Hothouse, Snowball, Monsoon, Inland Sea, Fractal Coast, Highlands.

---

### 4.3 Tectonic 3-Noise Altitude Pipeline

Altitude is computed in `WorldGenerator._generate_altitude(data, rng, params)`:

1. **Three FastNoiseLite layers** are blended per tile:
   - `n_base` — FBM with `params.noise_frequency` / `params.noise_octaves`  
   - `n_crust` — coarser FBM simulating plate interiors (`params.crust_frequency`)  
   - `n_tect` — domain-warped noise spiked at plate boundaries (`abs` of raw value)

2. **Blend**: `raw = n_base * noise_factor + n_crust * crust_factor + n_tect * tectonic_factor`

3. **Normalise** the entire grid to 0–1.

4. **Radial falloff**: multiply by `(1 − dist_from_centre ^ island_falloff)` to create the island outline.

5. **Sea level quantile**: sort all altitude values; `sea_level = altitudes[int(sea_ratio * N)]`. This guarantees exactly `sea_ratio` fraction of tiles are ocean regardless of seed or parameter settings. `data.sea_level` is stored for use by all downstream systems.

---

### 4.4 Hydrology — Rivers and Lakes

`hydrology.gd` runs after altitude is finalised. Entry point: `Hydrology.process(data, params)`.

#### D8 Flow Accumulation

Tiles are sorted high → low by altitude. For each tile the steepest downhill neighbour (8-directional) receives its accumulated flow. Result stored in `data.flow[y][x]`.

#### River Marking

Any land tile with `flow > params.river_threshold` is marked `is_river = true` and its biome is set to `BiomeType.RIVER`.

#### Lake Detection (BFS with drain-abort)

For each local-minimum land tile:
1. BFS outward up to height `min_alt + params.lake_fill_depth`.
2. **Abort** (`drains = true`) if any flood tile touches `alt ≤ data.sea_level` or a map edge — this prevents the entire map being classified as one lake.
3. If `drains == false`, all tiles in the BFS set are marked `is_lake = true` and biome → `BiomeType.LAKE`.

---

### 4.5 Map Renderer — 7 View Modes

`map_renderer.gd` converts `WorldData` into an `ImageTexture` at native map resolution. `TextureFilter = NEAREST` is set on the display node so zooming stays pixel-crisp.

| Mode | What it shows |
|---|---|
| `BIOME` | Whittaker biome colours with altitude darkening (up to −40% brightness at peak) |
| `ALTITUDE` | Blue (deep sea) → cyan → green → tan → white (mountains) |
| `TEMPERATURE` | Blue (cold) → red (hot) |
| `PRECIPITATION` | Brown (dry) → blue (wet) |
| `DRAINAGE` | Grey scale |
| `PROSPERITY` | Black → gold |
| `FLOW` | Black → cyan (sqrt-remapped so low values remain visible) |

---

### 4.6 World Map UI — sidebar, sliders, presets, tile inspection

`world_map.gd` builds the entire UI programmatically (no `.tscn` sidebar). Key features:

**Generation controls**
- **12 HSliders** in 4 sections: Shape (sea ratio, island falloff), Terrain (frequency, octaves, noise/crust/tectonic weight, crust scale), Climate (temperature bias, precipitation bias), Hydrology (river threshold, lake fill depth).
- **15 preset buttons** in a 3×5 grid — each press loads all 12 values and triggers regeneration.
- **R key / Generate button** — regenerates on a background thread; UI stays responsive.

**Map navigation**
- **Scroll wheel** — zoom (0.25× – 8×, nearest-neighbour filtered).
- **Middle-drag** — pan.
- **Right-click** — reset zoom and pan.

**Tile inspection**
- **Mouse hover** — sidebar label shows tile coordinates, biome name, altitude, temperature, precipitation, drainage, prosperity, flow accumulation, and river/lake flags.
- **Left-click** — **pins** the info panel (label shows `● PINNED` + `[click to unpin]`). Hover updates are suppressed and `mouse_exited` does not clear the panel while pinned.
- **Left-click again** (anywhere on the map) — unpins and resumes live hover.
- **New generation** — automatically clears any existing pin.

---

### 4.7 Future Phase 1 Extensions (not yet implemented)

Phase 2 depends on these additions to Phase 1. Implement them before starting `settlement.gd`:

| New array | Type | Purpose |
|---|---|---|
| `terrain` | `int` (TerrainType) | Gameplay terrain class derived from biome — for movement cost |
| `province_id` | `int` | Political region; −1 = unassigned (ocean) |
| `settlement_score` | `float` | Settlement suitability — used by ProvinceGenerator |

### 4.8 Province Generation Code (implement before Phase 2)

Add `terrain`, `province_id`, and `settlement_score` to `WorldData._init()`:

```gdscript
# In WorldData._init(), after existing array assignments:
terrain          = _make_grid(w, h, 0)     # TerrainType int
province_id      = _make_grid(w, h, -1)
settlement_score = _make_grid(w, h, 0.0)
var province_adjacency: Dictionary = {}
```

Then call at the end of `WorldGenerator.generate()`, after `Hydrology.process()`:

```gdscript
TerrainClassifier.classify(data)
ProvinceGenerator.generate(data, seed_val + 4)
ProvinceGenerator.score_settlement_sites(data)
```

`TerrainClassifier.classify()` maps each `BiomeType` to a `TerrainType` (OCEAN, SHALLOW_WATER, COAST, PLAINS, HILLS, FOREST, MOUNTAIN, DESERT, SWAMP, RIVER). River tiles use the existing `is_river` flag rather than biome.

Province borders follow geographic cost — mountains and rivers are hard to cross:

```gdscript
# scripts/world/province_generator.gd
class_name ProvinceGenerator

const COST_MOUNTAIN: float = 8.0
const COST_RIVER:    float = 2.0
const COST_SLOPE:    float = 10.0
const NUM_PROVINCES: int  = 20

static func generate(data: WorldData, s: int) -> void:
    # Place seeds on land tiles, then Dijkstra-expand from each.
    # cost = 1.0 + mountain_bonus + river_bonus + slope * COST_SLOPE
    # Province adjacency cache written to data.province_adjacency.
    ...

static func score_settlement_sites(data: WorldData) -> void:
    # For each land tile scan a 2-tile radius:
    # PLAINS +4, RIVER +5, HILLS +2, FOREST +1.5, MOUNTAIN +3
    ...

static func place_settlements(data: WorldData) -> Array[Settlement]:
    # For each province pick the tile with the highest settlement_score.
    ...
```

> Full source code for these functions is straightforward to generate from the scoring rules above; implement them when ending Phase 2 bootstrap.

---

### 4.9 Three-Tier Map System

Dwarf Fortress has **World → Region → Local** zoom levels. Ravensguard has the same three tiers. Tiers 2 and 3 are generated on-demand from the world tile’s data — they are **never stored in the save file**.

#### Scale Relationship

```
World tile (1 tile)
  └─ 8×8 Region tiles       (each ≈1.2 km × 1.2 km)
       └─ 6×6 Local tiles per region tile
            = 48×48 local tiles per world tile   (each ≈200 m × 200 m)
```

#### Data Structures

```gdscript
# scripts/world/region_data.gd
class_name RegionData extends RefCounted

const REGION_SCALE: int = 8   ## region tiles per world tile edge

var world_tile: Vector2i
var width:  int = REGION_SCALE
var height: int = REGION_SCALE

var altitude:  Array   ## float  — world altitude + detail noise
var biome:     Array   ## BiomeType int  — mostly matches world tile, blends at borders
var is_river:  Array   ## bool   — river carved through if world tile had river
var terrain:   Array   ## TerrainType int
var feature:   Array   ## RegionFeature: NONE, RUINS, CAMP, MINE_ENTRANCE, FORD
```

```gdscript
# scripts/world/local_map_data.gd
class_name LocalMapData extends RefCounted

const LOCAL_SCALE: int = 6    ## local tiles per region tile edge
## Full local map for one world tile: 48×48 tiles.

var world_tile:  Vector2i
var width:  int = 48
var height: int = 48

var terrain:     Array   ## TerrainType int
var elevation:   Array   ## int 0–10  — discrete height steps
var feature:     Array   ## LocalFeature: NONE, TREE, BOULDER, WATER, WALL, DOOR, CHEST
var passable:    Array   ## bool  — pre-computed for pathfinding
var building_id: Array   ## int   — -1 or Settlement building index if city tile
```

#### Generation Pipeline

`RegionGenerator` runs a **second noise pass** at 8× the world frequency, using the world tile’s altitude as the DC offset. The fine noise perturbs altitude by ±0.15 around the world value. Biome is inherited from the world tile (border blending looks at adjacent world tiles with `mix()`).

```gdscript
# scripts/world/region_generator.gd
class_name RegionGenerator

static func generate(world_data: WorldData, wx: int, wy: int) -> RegionData:
    var r := RegionData.new()
    r.world_tile = Vector2i(wx, wy)

    var world_alt: float  = world_data.altitude[wy][wx]
    var world_biome: int  = world_data.biome[wy][wx]
    var world_river: bool = world_data.is_river[wy][wx]

    var detail_noise := FastNoiseLite.new()
    detail_noise.seed            = world_data.world_seed ^ (wy * 10000 + wx)
    detail_noise.frequency       = world_data.sea_level * 8.0   # tuned per map
    detail_noise.fractal_octaves = 4

    for ry in range(r.height):
        for rx in range(r.width):
            # Normalised position within the world tile (0–1)
            var fx: float = (rx + 0.5) / float(r.width)
            var fy: float = (ry + 0.5) / float(r.height)
            var d: float = (detail_noise.get_noise_2d(fx * 16.0, fy * 16.0) + 1.0) * 0.5
            r.altitude[ry][rx] = clampf(world_alt + (d - 0.5) * 0.15, 0.0, 1.0)
            r.biome[ry][rx]    = world_biome   # full border blending added later

    if world_river:
        _carve_river(r, world_data, wx, wy)

    return r

static func _carve_river(r: RegionData, data: WorldData, wx: int, wy: int) -> void:
    # Trace lowest-altitude path from entry edge to exit edge, mark is_river.
    # Entry/exit determined by looking at adjacent world tiles’ river flags.
    ...
```

`LocalMapGenerator` subdivides each region tile 6×6 and adds **micro-noise** for individual rocks, trees, and water cells. If a Settlement exists at `(wx, wy)`, it overlays building footprints from `settlement.buildings`.

```gdscript
# scripts/world/local_map_generator.gd
class_name LocalMapGenerator

static func generate(world_data: WorldData, region: RegionData, wx: int, wy: int) -> LocalMapData:
    var lm := LocalMapData.new()
    lm.world_tile = Vector2i(wx, wy)

    var micro := FastNoiseLite.new()
    micro.seed = world_data.world_seed ^ 0xDEAD ^ (wy * 10000 + wx)
    micro.frequency = 0.18

    for ry in range(region.height):
        for rx in range(region.width):
            for ly in range(LocalMapData.LOCAL_SCALE):
                for lx in range(LocalMapData.LOCAL_SCALE):
                    var gx: int = rx * LocalMapData.LOCAL_SCALE + lx
                    var gy: int = ry * LocalMapData.LOCAL_SCALE + ly
                    lm.terrain[gy][gx]   = region.terrain[ry][rx]
                    lm.elevation[gy][gx] = int(region.altitude[ry][rx] * 10.0)
                    lm.feature[gy][gx]   = _roll_feature(
                        region.biome[ry][rx],
                        micro.get_noise_2d(gx, gy)
                    )
                    lm.passable[gy][gx]  = _is_passable(lm.terrain[gy][gx], lm.feature[gy][gx])

    # Overlay settlement buildings if one exists at this world tile
    var settlement = WorldState.get_settlement_at(wx, wy)
    if settlement != null:
        _place_buildings(lm, settlement)

    return lm

static func _roll_feature(biome: int, noise_val: float) -> int:
    # Trees in forest biomes when noise > 0.4, boulders in mountain when > 0.5, etc.
    ...
```

#### Cache in WorldState

Add to `WorldState.gd`:

```gdscript
# In WorldState.gd:
var _region_cache: Dictionary = {}   ## Vector2i → RegionData

func get_region(wx: int, wy: int) -> RegionData:
    var key := Vector2i(wx, wy)
    if not _region_cache.has(key):
        _region_cache[key] = RegionGenerator.generate(world_data, wx, wy)
    return _region_cache[key]

func get_local_map(wx: int, wy: int) -> LocalMapData:
    ## Not cached — always regenerated fresh (fast, <1 ms for 48×48).
    return LocalMapGenerator.generate(world_data, get_region(wx, wy), wx, wy)

func clear_map_cache() -> void:
    _region_cache.clear()
```

Call `WorldState.clear_map_cache()` whenever a new world is generated.

#### Scene Architecture

Update `main.tscn`’s mode enum:

```gdscript
# In the root scene controller:
enum MapMode { MENU, WORLD, REGION, LOCAL, CITY, BATTLE }
var current_mode: MapMode = MapMode.WORLD
var drill_tile:   Vector2i = Vector2i.ZERO   ## world tile currently drilled into

func drill_to_region(wx: int, wy: int) -> void:
    drill_tile = Vector2i(wx, wy)
    _switch_mode(MapMode.REGION)

func drill_to_local(wx: int, wy: int) -> void:
    drill_tile = Vector2i(wx, wy)
    var settlement = WorldState.get_settlement_at(wx, wy)
    if settlement != null:
        _switch_mode(MapMode.CITY)
    else:
        _switch_mode(MapMode.LOCAL)

func drill_up() -> void:
    match current_mode:
        MapMode.LOCAL, MapMode.CITY: _switch_mode(MapMode.REGION)
        MapMode.REGION:              _switch_mode(MapMode.WORLD)
```

#### World Map — Add Double-Click to Drill

In `world_map.gd`, add to the `MOUSE_BUTTON_LEFT` handler in `_on_map_input`:

```gdscript
elif event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
    if event.double_click:
        # Drill into region map for this tile
        var tile := _mouse_to_tile(event.position)
        if tile != Vector2i(-1, -1):
            get_tree().get_root().get_node("Main").drill_to_region(tile.x, tile.y)
    elif _tile_pinned:
        _tile_pinned = false
        _update_hover(event.position)
    else:
        _tile_pinned = true
        _pin_tile(event.position)
```

#### RegionMapView — Render and Navigate

`RegionMapView.tscn` is the simplest view: it renders `RegionData` as an `ImageTexture` (same technique as the world map renderer), at native 8×8 resolution with `TEXTURE_FILTER_NEAREST` scaled up. A breadcrumb `HBoxContainer` at the top links back to the world.

```gdscript
# scenes/overworld/RegionMapView.gd
extends Control

var _data: RegionData = null

func load_tile(wx: int, wy: int) -> void:
    _data = WorldState.get_region(wx, wy)
    _render()

func _render() -> void:
    var img := Image.create(_data.width, _data.height, false, Image.FORMAT_RGB8)
    for ry in range(_data.height):
        for rx in range(_data.width):
            img.set_pixel(rx, ry, TileRegistry.get_biome_color(_data.biome[ry][rx]))
    _map_display.texture = ImageTexture.create_from_image(img)
    # Double-click on this view → drill_to_local()
```

#### LocalMapView — Render and Navigate

`LocalMapView.tscn` renders `LocalMapData` as a 48×48 `ImageTexture`, or optionally as a `TileMap` once art assets exist. This scene is **reused as the base layer for `BattleScene`** — `BattleScene` adds unit sprites on top of it.

```gdscript
# scenes/overworld/LocalMapView.gd
extends Control

var _data: LocalMapData = null

func load_tile(wx: int, wy: int) -> void:
    _data = WorldState.get_local_map(wx, wy)
    _render()

func _render() -> void:
    var img := Image.create(_data.width, _data.height, false, Image.FORMAT_RGB8)
    for y in range(_data.height):
        for x in range(_data.width):
            var t: int = _data.terrain[y][x]
            var f: int = _data.feature[y][x]
            img.set_pixel(x, y, _feature_color(t, f))
    _map_display.texture = ImageTexture.create_from_image(img)
```

#### Breadcrumb Navigation

A reusable `BreadcrumbBar.tscn` sits at the top of Region and Local views:

```
[World Map]  ›  [Thornvale Province]  ›  [42, 31]
```

Each segment is a `Button` that calls the appropriate `drill_up()` depth. The province name is looked up from `world_data.province_id[wy][wx]` and a province-name dictionary generated alongside `province_id` during Phase 1.5.

#### Limitations and Deferred Work

- **Border biome blending** (smooth transitions at region tile edges) — implement after initial drill-down works.
- **Road rendering in Region and Local views** — ✅ Done (2026-02-22). Region view draws 1-px corridors from centre to edge midpoints/corners; Local view draws 3-px-wide corridors matching the same direction. Both read directly from `WorldData.road_network`.
- **Art assets on Local map** — Phase 7 UI pass replaces `ImageTexture` raster with a `TileMap`.
- **Local map size** — 48×48 is hardcoded for now. When battles need larger arenas, generate multiple adjacent world tiles and stitch.

---

Add `terrain`, `province_id`, and `settlement_score` to `WorldData._init()`:

```gdscript
# In WorldData._init(), after existing array assignments:
terrain          = _make_grid(w, h, 0)     # TerrainType int
province_id      = _make_grid(w, h, -1)
settlement_score = _make_grid(w, h, 0.0)
var province_adjacency: Dictionary = {}
```

Then call at the end of `WorldGenerator.generate()`, after `Hydrology.process()`:

```gdscript
TerrainClassifier.classify(data)
ProvinceGenerator.generate(data, seed_val + 4)
ProvinceGenerator.score_settlement_sites(data)
```

`TerrainClassifier.classify()` maps each `BiomeType` to a `TerrainType` (OCEAN, SHALLOW_WATER, COAST, PLAINS, HILLS, FOREST, MOUNTAIN, DESERT, SWAMP, RIVER). River tiles use the existing `is_river` flag rather than biome.

Province borders follow geographic cost — mountains and rivers are hard to cross:

```gdscript
# scripts/world/province_generator.gd
class_name ProvinceGenerator

const COST_MOUNTAIN: float = 8.0
const COST_RIVER:    float = 2.0
const COST_SLOPE:    float = 10.0
const NUM_PROVINCES: int  = 20

static func generate(data: WorldData, s: int) -> void:
    # Place seeds on land tiles, then Dijkstra-expand from each.
    # cost = 1.0 + mountain_bonus + river_bonus + slope * COST_SLOPE
    # Province adjacency cache written to data.province_adjacency.
    ...

static func score_settlement_sites(data: WorldData) -> void:
    # For each land tile scan a 2-tile radius:
    # PLAINS +4, RIVER +5, HILLS +2, FOREST +1.5, MOUNTAIN +3
    ...

static func place_settlements(data: WorldData) -> Array[Settlement]:
    # For each province pick the tile with the highest settlement_score.
    ...
```

> Full source code for these functions is straightforward to generate from the scoring rules above; implement them when ending Phase 2 bootstrap.

---

### 4.10 Site Generation

**Purpose:** scatter named points-of-interest across the world — farmsteads, lumber camps, mines, fishing huts, hamlets, bandit camps, wayside shrines, ruined keeps, temples, dungeons, and ancient ruins — after province generation but before economy simulation begins.

Sites are stored in `WorldData.sites: Dictionary` (Vector2i → `{ "feature": int, "name": String }`) and consumed by the Region view renderer and eventually by economy and event systems.

#### Pipeline position

```
WorldGenerator.generate()
  └─ ProvinceGenerator.generate()          # province_id, province_capitals
  └─ ProvinceGenerator.score_settlement_sites()
  └─ ProvinceGenerator.place_settlements() → WorldState.settlements
  └─ RoadGenerator.generate()
  └─ SiteGenerator.generate()              # ← new, writes data.sites
```

#### `WorldData` addition

```gdscript
# scripts/world/world_data.gd
var sites: Dictionary = {}   # Vector2i(tx,ty) -> { "feature": RegionFeature, "name": String }
# (clear in reset() alongside road_network)
```

#### Site types and terrain weights

| Feature | Best terrain | Rarity | Notes |
|---|---|---|---|
| `FARMSTEAD` | PLAINS, RIVER | 1.0 | contributes `arable_acres` to nearest settlement |
| `LUMBER_CAMP` | FOREST | 1.0 | contributes `forest_acres` |
| `MINE_ENTRANCE` | MOUNTAIN, HILLS | 1.0 | contributes `mining_slots` |
| `FISHING_HUT` | RIVER, COAST | 1.0 | contributes `fishing_slots` |
| `HAMLET` | PLAINS, RIVER | 0.8 | minor population node, no province hub |
| `BANDIT_CAMP` | FOREST, HILLS | 0.5 | spawns raider events (Phase 3) |
| `WAYSHRINE` | any | 0.4 | road-speed bonus in Phase 3 |
| `RUINED_KEEP` | HILLS | 0.3 | recruitable garrison (Phase 4) |
| `TEMPLE` | any | 0.25 | faction religion buff (Phase 3) |
| `DUNGEON` | MOUNTAIN, HILLS | 0.2 | player-enterable (Phase 4) |
| `ANCIENT_RUINS` | PLAINS, DESERT | 0.1 | rare, high-value exploration target |

#### `SiteGenerator` — key rules

- Max **6 sites per province**; placement order is randomized-then-weighted (same gauss-jitter pattern as spoke placement).
- Sites are never placed on a tile already occupied by a `Settlement`.
- Same-type sites within a province require **≥ 4-tile separation** (Poisson-disk, same pattern as spokes).
- Each tile rolls a site type via terrain-weighted sampling; a **0.35 spawn-gate** roll first prevents every eligible tile getting a site.

```gdscript
# scripts/world/site_generator.gd
class_name SiteGenerator

const MAX_SITES_PER_PROVINCE: int = 6
const MIN_SITE_SEP:           int = 4
const SPAWN_GATE:           float = 0.35   # probability a candidate tile gets any site at all

## SITE_TERRAIN_WEIGHTS[feature_id][TerrainType] = int weight (0 = never)
const SITE_TERRAIN_WEIGHTS: Dictionary = {
    RegionData.RegionFeature.FARMSTEAD:     { PLAINS: 10, RIVER: 8, HILLS: 3, FOREST: 1 },
    RegionData.RegionFeature.LUMBER_CAMP:   { FOREST: 10, HILLS: 3 },
    RegionData.RegionFeature.MINE_ENTRANCE: { MOUNTAIN: 10, HILLS: 7 },
    RegionData.RegionFeature.FISHING_HUT:   { RIVER: 10, COAST: 8 },
    RegionData.RegionFeature.HAMLET:        { PLAINS: 6, RIVER: 4, HILLS: 2 },
    RegionData.RegionFeature.BANDIT_CAMP:   { FOREST: 8, HILLS: 5, MOUNTAIN: 3 },
    RegionData.RegionFeature.WAYSHRINE:     { PLAINS: 4, HILLS: 3, FOREST: 3, MOUNTAIN: 2 },
    RegionData.RegionFeature.RUINED_KEEP:   { HILLS: 7, PLAINS: 3, MOUNTAIN: 2 },
    RegionData.RegionFeature.TEMPLE:        { PLAINS: 4, HILLS: 5, MOUNTAIN: 3, FOREST: 2 },
    RegionData.RegionFeature.DUNGEON:       { MOUNTAIN: 8, HILLS: 5, FOREST: 2 },
    RegionData.RegionFeature.ANCIENT_RUINS: { PLAINS: 3, HILLS: 3, DESERT: 5, FOREST: 2 },
}

static func generate(data: WorldData, rng: RandomNumberGenerator) -> void:
    data.sites = {}
    # 1. Build province_tiles: Dictionary (pid -> Array[Vector2i]) from data.province_id.
    # 2. For each province call _place_sites_in_province().

static func _place_sites_in_province(data, rng, pid, tiles) -> void:
    tiles.shuffle()
    var placed: Array = []          # Vector2i positions used so far
    for tile in tiles:
        if placed.size() >= MAX_SITES_PER_PROVINCE: break
        # Skip occupied settlement tiles
        # Roll spawn gate
        # Roll site type via _roll_site_type(terrain, rng)
        # Check MIN_SITE_SEP against same-type placed entries
        # Write to data.sites[tile] = { "feature": type, "name": _generate_site_name(type, rng) }

static func _roll_site_type(terrain: int, rng: RandomNumberGenerator) -> int:
    # Build weighted pool from SITE_TERRAIN_WEIGHTS × SITE_RARITY.
    # Return NONE if total == 0 or spawn-gate fails.
    # Otherwise weighted random pick.

static func _generate_site_name(feature: int, rng: RandomNumberGenerator) -> String:
    # Word-list lookup per feature; expand with _PREFIXES/_ROOTS/_SUFFIXES in Phase 3.
```

#### `RegionFeature` enum additions

Add to `region_data.gd` alongside existing feature values:

```gdscript
enum RegionFeature {
    NONE, RUINS, CAMP, MINE_ENTRANCE, FORD,   # existing
    FARMSTEAD, LUMBER_CAMP, FISHING_HUT, HAMLET,
    BANDIT_CAMP, WAYSHRINE, RUINED_KEEP,
    TEMPLE, DUNGEON, ANCIENT_RUINS,            # new
}
```

#### World-map dot colours (world_map.gd)

Sites are painted after settlement dots, 1×1 px, only when the map zoom is ≥ 2:

| Feature | Colour |
|---|---|
| FARMSTEAD / FISHING_HUT | `#a8d8a8` (pale green) |
| LUMBER_CAMP | `#3a7a3a` (dark green) |
| MINE_ENTRANCE | `#888888` (grey) |
| HAMLET | `#e8e0a0` (pale yellow) |
| BANDIT_CAMP | `#c03030` (dark red) |
| WAYSHRINE | `#e8e8ff` (pale blue) |
| RUINED_KEEP / ANCIENT_RUINS | `#8060a0` (purple) |
| TEMPLE | `#f0d060` (gold) |
| DUNGEON | `#404040` (near-black) |

#### Future hooks

- **Phase 2** — `SiteGenerator` writes `arable_acres`, `forest_acres`, `mining_slots`, `fishing_slots` contributions to the nearest settlement within 4 tiles.
- **Phase 3** — `BANDIT_CAMP` seeds the raider-event pool; `TEMPLE` adds a religion modifier to the province.
- **Phase 4** — `DUNGEON` and `ANCIENT_RUINS` become player-enterable from the Local map view.
- **World map** — small coloured 1×1 dots per site visible at zoom ≥ 2 (see colour table above).

---

## 5. Phase 2 — Settlements and Economy

### 5.1 Settlement Class

```gdscript
# scripts/settlement/settlement.gd
class_name Settlement extends Resource

# --- Identity ---
var id:           int
var name:         String
var tile_x:       int
var tile_y:       int
var province_id:  int
var faction_id:   int = -1
var tier:         int = 1       # 0 = Hamlet ... 4 = Metropolis
var settlement_type: String = "village"  # village, town, city, castle, port, hamlet
var governor_personality: String = "balanced"  # balanced, greedy, militant, builder

# --- Population ---
var population:  int = 100
var laborers:    int = 84
var burghers:    int = 15
var nobility:    int = 1

# --- Land (calculated once on init, based on surrounding tiles) ---
var arable_acres:  float = 0.0
var forest_acres:  float = 0.0
var mining_slots:  int   = 0
var fishing_slots: int   = 0

# --- Economy ---
var market: Market = null
var buildings: Array[Building] = []
var happiness: float = 75.0
var unrest:    float = 0.0
var housing_capacity: int = 200

# --- Flags ---
var has_three_field: bool = false   # unlocked at Farm level 4+

func initialize(tx: int, ty: int, pid: int, data: WorldData) -> void:
    tile_x = tx
    tile_y = ty
    province_id = pid
    market = Market.new()
    _calculate_land(data)
    _init_population()

func _calculate_land(data: WorldData) -> void:
    var radius: int = [1, 2, 3, 4, 5][tier]
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            var nx: int = tile_x + dx
            var ny: int = tile_y + dy
            if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
                continue
            match data.terrain[ny][nx]:
                WorldData.TerrainType.PLAINS:   arable_acres  += 250.0
                WorldData.TerrainType.HILLS:    arable_acres  += 125.0;  mining_slots += 150
                WorldData.TerrainType.FOREST:   arable_acres  += 50.0;   forest_acres += 200.0
                WorldData.TerrainType.MOUNTAIN: mining_slots  += 400
                WorldData.TerrainType.RIVER:    arable_acres  += 250.0;  fishing_slots += 150; mining_slots += 40
                WorldData.TerrainType.SWAMP:    forest_acres  += 112.0

func _init_population() -> void:
    laborers = int(population * 0.84)
    burghers = int(population * 0.15)
    nobility = population - laborers - burghers  # ensures sum == population

func daily_tick() -> void:
    Production.run(self)
    market.consume(self)
    GovernorAI.decide(self)
    market.update_prices(self)
    _update_population()

func _update_population() -> void:
    if market.get_stock("grain") > population * 30 and population < housing_capacity:
        var births: int = maxi(1, int(population * 0.0001))
        population += births
        _init_population()
```

### 5.2 Market Class

```gdscript
# scripts/settlement/market.gd
class_name Market extends RefCounted

# inventory: resource_id -> quantity (never goes negative)
var inventory:     Dictionary = {}
# price history ring buffer: resource_id -> Array[float] (14 entries)
var price_history: Dictionary = {}
var current_price: Dictionary = {}

func get_stock(resource_id: String) -> float:
    return inventory.get(resource_id, 0.0)

func add_stock(resource_id: String, amount: float) -> void:
    inventory[resource_id] = maxf(0.0, get_stock(resource_id) + amount)

func deduct_stock(resource_id: String, amount: float) -> float:
    # Returns how much was actually deducted (may be less than requested)
    var available: float = get_stock(resource_id)
    var deducted: float = minf(available, amount)
    inventory[resource_id] = available - deducted
    return deducted

func get_price(resource_id: String) -> float:
    return current_price.get(resource_id, ResourceRegistry.base_price(resource_id))

func update_prices(settlement: Settlement) -> void:
    for rid in ResourceRegistry.ALL_RESOURCES:
        var base:   float = ResourceRegistry.base_price(rid)
        var supply: float = get_stock(rid)
        var demand: float = ResourceRegistry.daily_demand(rid, settlement)
        var raw:    float = base * clampf(demand / maxf(supply, 1.0), 0.2, 5.0)

        if not price_history.has(rid):
            price_history[rid] = []
        price_history[rid].append(raw)
        if price_history[rid].size() > 14:
            price_history[rid].pop_front()

        var avg: float = 0.0
        for v in price_history[rid]:
            avg += v
        current_price[rid] = avg / price_history[rid].size()

func consume(settlement: Settlement) -> void:
    # Food: all classes eat 1.2 bushels/day each
    var food_needed: float = settlement.population * 1.2
    var food_given:  float = deduct_stock("grain", food_needed)
    if food_given < food_needed * 0.90:
        var deficit: float = 1.0 - (food_given / food_needed)
        settlement.unrest    += 20.0 * deficit
        settlement.happiness -= 20.0 * deficit
        var deaths: int = int(settlement.population * 0.02 * deficit) + 2
        settlement.population = maxi(0, settlement.population - deaths)
        settlement._init_population()

    # Luxury: Burghers need ale; Nobility need meat/furs/salt
    _consume_luxury(settlement, "ale",  settlement.burghers * 0.1,  "burgher_happiness")
    _consume_luxury(settlement, "meat", settlement.nobility * 0.5,  "noble_happiness")
    _consume_luxury(settlement, "furs", settlement.nobility * 0.05, "noble_happiness")
    _consume_luxury(settlement, "salt", settlement.nobility * 0.05, "noble_happiness")

func _consume_luxury(settlement: Settlement, rid: String, needed: float, mood: String) -> void:
    var given: float = deduct_stock(rid, needed)
    if given < needed * 0.5:
        settlement.happiness -= 5.0
```

### 5.3 Production

```gdscript
# scripts/economy/production.gd
class_name Production

static func run(settlement: Settlement) -> void:
    var available_laborers: int = settlement.laborers

    # ── Priority 1: Food survival buffer (24h) ───────────────────────
    var food_needed_24h: float = settlement.population * 1.2
    available_laborers = _assign_food(settlement, available_laborers, food_needed_24h)

    # ── Fuel survival buffer ─────────────────────────────────────────
    var fuel_needed: float = settlement.population / 50.0
    var wood_per_laborer: float = _wood_rate(settlement)
    var fuel_workers: int = mini(available_laborers, ceili(fuel_needed / wood_per_laborer))
    settlement.market.add_stock("wood", fuel_workers * wood_per_laborer)
    available_laborers -= fuel_workers

    # ── Priority 2: 60-day security buffer ───────────────────────────
    var food_60d: float = settlement.population * 1.2 * 60.0
    var current_food: float = settlement.market.get_stock("grain")
    if current_food < food_60d:
        available_laborers = _assign_food(settlement, available_laborers, food_60d - current_food)

    # ── Priority 3: Profit — best 2 resources by margin ──────────────
    if available_laborers > 0:
        _assign_profit(settlement, available_laborers)

static func _grain_rate(settlement: Settlement) -> float:
    var fallow_factor: float = 0.67 if settlement.has_three_field else 0.50
    var farm_level: int = settlement._building_level("farm")
    var building_mult: float = 1.0 + farm_level * 0.5
    return settlement.arable_acres * fallow_factor * 12.0 / 360.0 * building_mult

static func _wood_rate(settlement: Settlement) -> float:
    var mill_level: int = settlement._building_level("lumber_mill")
    var building_mult: float = 1.0 + mill_level * 1.0
    return settlement.forest_acres / 40.0 * 8.0 / 360.0 * building_mult

static func _assign_food(settlement: Settlement, workers_available: int, target: float) -> int:
    var rate_per_worker: float = _grain_rate(settlement) / maxf(settlement.laborers, 1.0)
    var workers_needed: int = ceili(target / maxf(rate_per_worker, 0.0001))
    var assigned: int = mini(workers_available, workers_needed)
    settlement.market.add_stock("grain", assigned * rate_per_worker)
    return workers_available - assigned

static func _assign_profit(settlement: Settlement, workers: int) -> void:
    var margins: Array = []
    for rid in ResourceRegistry.ALL_RESOURCES:
        var base:  float = ResourceRegistry.base_price(rid)
        var price: float = settlement.market.get_price(rid)
        margins.append([price / base, rid])
    margins.sort_custom(func(a, b): return a[0] > b[0])

    var split: int = workers / 2
    for i in range(mini(2, margins.size())):
        var rid: String = margins[i][1]
        var output: float = _produce_resource(settlement, rid, split)
        settlement.market.add_stock(rid, output)

static func _produce_resource(settlement: Settlement, rid: String, workers: int) -> float:
    match rid:
        "grain": return _grain_rate(settlement) / maxf(settlement.laborers, 1.0) * workers
        "wood":  return _wood_rate(settlement) / maxf(settlement.laborers, 1.0) * workers
        "ore":
            var mine_level: int = settlement._building_level("mine")
            return settlement.mining_slots * 0.005 / 360.0 * (1.0 + mine_level * 0.5)
        "fish":
            return settlement.fishing_slots * 25.0 / 360.0
    return 0.0
```

### 5.4 Building

```gdscript
# scripts/settlement/building.gd
class_name Building extends RefCounted

## 23 building types, 3 categories, max 10 levels each.
## Source of truth: data/buildings.json
## Categories:
##   industry — farm, lumber_mill, fishery, mine, pasture, blacksmith, tannery, weaver, brewery, tailor, goldsmith
##   military — stone_walls, barracks, training_ground, granary, watchtower
##   civil    — housing_district, market, road_network, merchant_guild, warehouse_district, cathedral, tavern
##
## Each entry in buildings.json has:
##   cost      — gold cost for level 1 construction
##   labor     — labour-hours to build (level 1)
##   tier      — minimum settlement tier required (1 = hamlet+, 2 = town+, 3 = city+)
##   desc      — one-line effect description
##   levels    — object keyed by level number (not every level listed; use nearest-lower for unlisted)
##     levels[n].name   — building name at that level
##     levels[n].flavor — flavour text
##
## Multiplier formula (industry buildings): 1.0 + level × per_level_bonus  (varies by type)
## Cost to upgrade to level N: 50.0 × N²

var building_type: String   # matches key in buildings.json
var level: int = 1

const DEFINITIONS: Dictionary = {
    "farm":             {"per_level_bonus": 0.50, "boosts": "grain"},
    "lumber_mill":      {"per_level_bonus": 1.00, "boosts": "wood"},
    "fishery":          {"per_level_bonus": 0.50, "boosts": "fish"},
    "mine":             {"per_level_bonus": 0.50, "boosts": "ore"},
    "pasture":          {"per_level_bonus": 0.50, "boosts": "meat"},
    "blacksmith":       {"per_level_bonus": 1.00, "boosts": "steel"},
    "tannery":          {"per_level_bonus": 1.00, "boosts": "leather"},
    "weaver":           {"per_level_bonus": 1.00, "boosts": "cloth"},
    "brewery":          {"per_level_bonus": 1.00, "boosts": "ale"},
    "tailor":           {"per_level_bonus": 1.00, "boosts": "fine_garments"},
    "goldsmith":        {"per_level_bonus": 1.00, "boosts": "jewelry"},
    "stone_walls":      {"per_level_bonus": 0.00, "boosts": "defense"},
    "barracks":         {"per_level_bonus": 0.00, "boosts": "garrison"},
    "training_ground":  {"per_level_bonus": 0.00, "boosts": "recruit_quality"},
    "granary":          {"per_level_bonus": 0.50, "boosts": "food_storage"},
    "watchtower":       {"per_level_bonus": 0.00, "boosts": "stability"},
    "housing_district": {"per_level_bonus": 0.00, "boosts": "housing"},
    "market":           {"per_level_bonus": 0.00, "boosts": "trade_slots"},
    "road_network":     {"per_level_bonus": 0.00, "boosts": "trade_throughput"},
    "merchant_guild":   {"per_level_bonus": 0.00, "boosts": "caravan_capacity"},
    "warehouse_district":{"per_level_bonus": 0.00, "boosts": "inventory_cap"},
    "cathedral":        {"per_level_bonus": 0.00, "boosts": "stability"},
    "tavern":           {"per_level_bonus": 0.00, "boosts": "happiness"},
}

func get_multiplier() -> float:
    var bonus: float = DEFINITIONS.get(building_type, {}).get("per_level_bonus", 0.0)
    return 1.0 + level * bonus

func upgrade_cost() -> float:
    return 50.0 * (level + 1) * (level + 1)
```

---

### 5.5 Road Network Generation

> **Inspiration**: Azgaar FMG connects burgs with Dijkstra using per-terrain movement costs, then stores road segments as cell-to-cell connections. `connectivity_rate` (how many road segments pass through a cell) multiplies settlement population, making crossroads cities grow organically.

Runs once at the end of world generation, after `place_settlements()`. Stored in `WorldData`.

**Add to `world_data.gd`:**
```gdscript
var road_network: Dictionary = {}  # Vector2i -> Array[Vector2i] adjacency list
```

**Add to `settlement.gd`:**
```gdscript
var connectivity_rate: float = 1.0  # 1.0 = isolated; 2.0+ = road junction
```

**`scripts/world/road_generator.gd`** (new file):
```gdscript
class_name RoadGenerator

## Terrain movement costs for road pathfinding (lower = preferred).
## Plains and rivers are cheap; forests moderate; mountains expensive.
## Roads follow the path of least resistance, hugging valleys.
const MOVE_COST: Dictionary = {
    WorldData.TerrainType.PLAINS:   1.0,
    WorldData.TerrainType.RIVER:    1.5,
    WorldData.TerrainType.COAST:    1.5,
    WorldData.TerrainType.FOREST:   3.0,
    WorldData.TerrainType.HILLS:    4.0,
    WorldData.TerrainType.MOUNTAIN: 9.0,
}

## For each settlement, connect it to its N nearest neighbours by
## running Dijkstra and storing the cheapest-path tile chain.
## Produces data.road_network (tile adjacency) and sets settlement.connectivity_rate.
static func generate(data: WorldData, settlements: Array) -> void:
    data.road_network.clear()
    var connectivity_hits: Dictionary = {}  # Vector2i -> int

    for i in range(settlements.size()):
        var src: Settlement = settlements[i]
        # Find nearest 3 settlements (by Euclidean distance) as targets.
        var others: Array = settlements.filter(func(s): return s.id != src.id)
        others.sort_custom(func(a, b):
            var da: float = Vector2(src.tile_x, src.tile_y).distance_squared_to(Vector2(a.tile_x, a.tile_y))
            var db: float = Vector2(src.tile_x, src.tile_y).distance_squared_to(Vector2(b.tile_x, b.tile_y))
            return da < db
        )
        var targets: Array = others.slice(0, 3)

        for dst in targets:
            var path: Array = _dijkstra_path(data, Vector2i(src.tile_x, src.tile_y), Vector2i(dst.tile_x, dst.tile_y))
            _stamp_road(data, path, connectivity_hits)

    # Derive connectivity_rate per settlement: 1 + (road tiles passing through / 4).
    for s in settlements:
        var key: Vector2i = Vector2i(s.tile_x, s.tile_y)
        s.connectivity_rate = 1.0 + float(connectivity_hits.get(key, 0)) / 4.0


static func _dijkstra_path(data: WorldData, src: Vector2i, dst: Vector2i) -> Array:
    var costs: Dictionary = {src: 0.0}
    var prev:  Dictionary = {}
    var pq: Array = [[0.0, src]]

    while not pq.is_empty():
        pq.sort_custom(func(a, b): return a[0] < b[0])
        var cur: Array  = pq.pop_front()
        var cost: float = cur[0]
        var pos: Vector2i = cur[1]

        if pos == dst:
            break
        if cost > costs.get(pos, INF):
            continue

        for dy in [-1, 0, 1]:
            for dx in [-1, 0, 1]:
                if dx == 0 and dy == 0:
                    continue
                var nx: int = pos.x + dx
                var ny: int = pos.y + dy
                if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
                    continue
                if data.altitude[ny][nx] <= data.sea_level:
                    continue
                var t: int = data.terrain[ny][nx]
                var step: float = MOVE_COST.get(t, 5.0)
                if dx != 0 and dy != 0:
                    step *= 1.414  # diagonal penalty
                var new_cost: float = cost + step
                var nkey: Vector2i = Vector2i(nx, ny)
                if new_cost < costs.get(nkey, INF):
                    costs[nkey] = new_cost
                    prev[nkey]  = pos
                    pq.append([new_cost, nkey])

    # Reconstruct path
    var path: Array = []
    var cur: Vector2i = dst
    while prev.has(cur):
        path.push_front(cur)
        cur = prev[cur]
    if not path.is_empty():
        path.push_front(src)
    return path


static func _stamp_road(data: WorldData, path: Array, hits: Dictionary) -> void:
    for i in range(path.size() - 1):
        var a: Vector2i = path[i]
        var b: Vector2i = path[i + 1]
        if not data.road_network.has(a):
            data.road_network[a] = []
        if not data.road_network[a].has(b):
            data.road_network[a].append(b)
        hits[a] = hits.get(a, 0) + 1
        hits[b] = hits.get(b, 0) + 1
```

**Wire into `world_generator.gd`** after `place_settlements()`:
```gdscript
RoadGenerator.generate(data, data.settlements)
```

---

### 5.6 Connectivity Rate → Population Multiplier

> **From FMG**: `population = (score / 5) × connectivity_rate × gauss(1, 1, 0.25, 4)`. Settlements at road junctions grow larger than identical isolated ones — no scripting required, it emerges from the road graph.

Apply in `settlement.gd` after `connectivity_rate` is set by `RoadGenerator`:

```gdscript
func apply_connectivity_bonus() -> void:
    ## Called once by world_generator after RoadGenerator.generate().
    ## Scales starting population by road connectivity.
    ## A hamlet with connectivity 1.0 is untouched; a crossroads town at 3.0
    ## triples its population, which flows into all downstream economy calculations.
    population = maxi(int(population * connectivity_rate), 10)
    _init_population()   # re-distribute laborers/burghers/nobility
```

**Tier can be re-evaluated here too** — a village that ends up at 400+ pop due to high connectivity deserves Town tier:
```gdscript
    # Re-tier if connectivity drove population into higher bracket
    if population >= 1000 and tier < 3:
        tier = 3
    elif population >= 300 and tier < 2:
        tier = 2
```

**Wire into `world_generator.gd`** after `RoadGenerator.generate()`:
```gdscript
for s in data.settlements:
    s.apply_connectivity_bonus()
```

---

## 6. Phase 3 — Factions and Overworld Agents

### 6.1 Faction

> **Relations model (FMG lesson)**: Store relations as a flat `Array[float]` indexed by faction ID rather than a `Dictionary`. O(1) lookup, serialises trivially, and the entire diplomatic state for N factions is one packed float array of length N.

```gdscript
# scripts/faction/faction.gd
class_name Faction extends Resource

var id:             int
var name:           String
var personality:    String        # aggressive, mercantile, defensive
var expansion_type: String = "generic"  # generic, naval, highland, nomadic — see §6.1a
var treasury:       float = 500.0
var province_ids:   Array[int] = []
var capital_settlement_id: int = -1

## Flat relations array indexed by faction ID.
## Value range: -1.0 (war) .. 0.0 (neutral) .. +1.0 (ally).
## Size is set to WorldState.factions.size() at world gen and never shrinks.
## Use get_relation() / set_relation() to access safely.
var _relations: Array[float] = []
var at_war_with: Array[int]  = []   # derived cache; rebuilt from _relations < -0.8

func init_relations(faction_count: int) -> void:
    _relations.resize(faction_count)
    _relations.fill(0.0)
    _relations[id] = 1.0   # self-relation

func get_relation(other_id: int) -> float:
    if other_id < 0 or other_id >= _relations.size():
        return 0.0
    return _relations[other_id]

func set_relation(other_id: int, value: float) -> void:
    if other_id < 0 or other_id >= _relations.size():
        return
    _relations[other_id] = clampf(value, -1.0, 1.0)
    # Rebuild war cache
    if value <= -0.8 and not at_war_with.has(other_id):
        at_war_with.append(other_id)
    elif value > -0.8:
        at_war_with.erase(other_id)

func adjust_relation(other_id: int, delta: float) -> void:
    set_relation(other_id, get_relation(other_id) + delta)
```

---

### 6.2a Faction Expansion Types

> **From FMG**: Culture types (`Nomadic`, `Naval`, `Highland`, `Generic`) modify the per-biome movement cost used when Dijkstra-expanding a faction's territory. This makes maritime factions naturally control coastlines and mountain clans naturally dominate highlands — no manual scripting required.

Each `expansion_type` carries a terrain cost modifier table. During faction province-claiming (Dijkstra from capital), multiply the base `COST` by the faction's modifier for that terrain.

```gdscript
# In faction.gd (or a static helper)
const EXPANSION_COSTS: Dictionary = {
    # expansion_type -> Dictionary[TerrainType -> cost_multiplier]
    # Multiplier < 1.0 = preferred; > 1.0 = avoided.
    "generic": {
        WorldData.TerrainType.PLAINS:   1.0,
        WorldData.TerrainType.RIVER:    1.2,
        WorldData.TerrainType.FOREST:   2.0,
        WorldData.TerrainType.HILLS:    2.5,
        WorldData.TerrainType.MOUNTAIN: 4.0,
        WorldData.TerrainType.COAST:    1.5,
    },
    "naval": {
        WorldData.TerrainType.PLAINS:   1.5,
        WorldData.TerrainType.RIVER:    1.0,
        WorldData.TerrainType.FOREST:   3.0,
        WorldData.TerrainType.HILLS:    3.0,
        WorldData.TerrainType.MOUNTAIN: 5.0,
        WorldData.TerrainType.COAST:    0.4,  # strongly prefers coast
    },
    "highland": {
        WorldData.TerrainType.PLAINS:   1.5,
        WorldData.TerrainType.RIVER:    1.5,
        WorldData.TerrainType.FOREST:   1.5,
        WorldData.TerrainType.HILLS:    0.6,
        WorldData.TerrainType.MOUNTAIN: 0.5,  # strongly prefers high ground
        WorldData.TerrainType.COAST:    2.5,
    },
    "nomadic": {
        # Ignores all terrain; spreads uniformly across open land.
        WorldData.TerrainType.PLAINS:   0.5,
        WorldData.TerrainType.RIVER:    0.8,
        WorldData.TerrainType.FOREST:   1.0,
        WorldData.TerrainType.HILLS:    0.9,
        WorldData.TerrainType.MOUNTAIN: 1.0,
        WorldData.TerrainType.COAST:    1.0,
    },
}

## Returns the movement cost from one tile to a neighbour for THIS faction.
func expansion_cost(terrain_type: int) -> float:
    return EXPANSION_COSTS.get(expansion_type, EXPANSION_COSTS["generic"]).get(terrain_type, 2.0)
```

**Usage in faction Dijkstra expansion** (called during world gen and after conquests):
```gdscript
# The base step cost is modified per faction expansion type:
var step: float = faction.expansion_cost(data.terrain[ny][nx])
# Otherwise identical to province_generator.gd Dijkstra flood-fill.
```

**Assign expansion types at world gen** based on faction capital's geography:
```gdscript
static func assign_expansion_type(faction: Faction, data: WorldData) -> void:
    var s: Settlement = WorldState.settlements[faction.capital_settlement_id]
    var t: int = data.terrain[s.tile_y][s.tile_x]
    if t == WorldData.TerrainType.COAST:
        faction.expansion_type = "naval"
    elif t == WorldData.TerrainType.MOUNTAIN or t == WorldData.TerrainType.HILLS:
        faction.expansion_type = "highland"
    elif data.province_id[s.tile_y][s.tile_x] >= 0:
        # Check if capital province is mostly plains → nomadic
        faction.expansion_type = "nomadic" if _is_steppe_province(faction, data) else "generic"
    else:
        faction.expansion_type = "generic"
```

---

### 6.2 Faction AI

```gdscript
# scripts/faction/faction_ai.gd
class_name FactionAI

static func decide(faction: Faction) -> void:
    var war_threshold: float = {"aggressive": 0.3, "mercantile": 0.7, "defensive": 0.8}[faction.personality]

    # 1. Needs assessment
    var has_shortage: bool = faction.treasury < 100.0

    # 2. Scan neighbors
    var target: Faction = null
    for other in WorldState.factions:
        if other.id == faction.id:
            continue
        if not _are_neighbors(faction, other):
            continue
        var ratio: float = _strength_ratio(other, faction)  # other/me
        if ratio < war_threshold:
            target = other
            break

    # 3. Decision
    if faction.personality == "mercantile":
        _try_trade(faction)
        return

    if has_shortage and target != null:
        _declare_war(faction, target)
        return

    if faction.personality == "aggressive" and target != null:
        _declare_war(faction, target)
        return

    # Hold / fortify: order garrisons to reinforce
    _fortify(faction)

static func _declare_war(attacker: Faction, defender: Faction) -> void:
    attacker.set_relation(defender.id, -1.0)
    defender.set_relation(attacker.id, -1.0)
    _order_advance(attacker, defender)

static func _order_advance(attacker: Faction, defender: Faction) -> void:
    # Find attacker's armies and set target to nearest enemy settlement
    for army in WorldState.armies:
        if army.faction_id != attacker.id:
            continue
        var target_settlement: Settlement = _nearest_enemy_settlement(army, defender)
        if target_settlement:
            army.target_tile = Vector2i(target_settlement.tile_x, target_settlement.tile_y)
            army.order = "advance"

static func _strength_ratio(a: Faction, b: Faction) -> float:
    var sa: float = 0.0
    var sb: float = 0.0
    for army in WorldState.armies:
        var s: float = _army_strength(army)
        if army.faction_id == a.id: sa += s
        if army.faction_id == b.id: sb += s
    return sa / maxf(sb, 1.0)

static func _army_strength(army: Army) -> float:
    return army.units.size() * 10.0   # placeholder; replace with unit tier sum in Phase 5

static func _are_neighbors(a: Faction, b: Faction) -> bool:
    for pid_a in a.province_ids:
        for pid_b in b.province_ids:
            if _provinces_adjacent(pid_a, pid_b):
                return true
    return false

static func _provinces_adjacent(a: int, b: int) -> bool:
    # Check if any tile with province a is adjacent to any tile with province b
    # Cache this adjacency map at world gen time for performance
    return WorldState.world_data.province_adjacency.get(a, {}).has(b)

static func _nearest_enemy_settlement(army: Army, enemy: Faction) -> Settlement:
    var best: Settlement = null
    var best_dist: float = INF
    for s in WorldState.settlements:
        if s.faction_id != enemy.id:
            continue
        var d: float = Vector2(army.tile_x, army.tile_y).distance_to(Vector2(s.tile_x, s.tile_y))
        if d < best_dist:
            best_dist = d
            best = s
    return best

static func _try_trade(_faction: Faction) -> void:
    pass   # Caravan dispatch logic goes here in Phase 3

static func _fortify(_faction: Faction) -> void:
    pass   # Reinforce garrison logic goes here
```

### 6.3 Army Node (overworld)

```gdscript
# scripts/faction/army.gd
class_name Army extends Resource

var id:          int
var faction_id:  int
var tile_x:      int
var tile_y:      int
var units:       Array[Unit] = []
var order:       String = "hold"   # advance, hold, retreat, siege
var target_tile: Vector2i = Vector2i(-1, -1)
var siege_target_id: int = -1
var siege_day:   int = 0

# Called every daily_pulse
func march() -> void:
    if order != "advance" or target_tile == Vector2i(-1, -1):
        return
    var dir: Vector2 = (Vector2(target_tile) - Vector2(tile_x, tile_y)).normalized()
    tile_x += int(round(dir.x))
    tile_y += int(round(dir.y))
    tile_x = clamp(tile_x, 0, WorldState.world_data.width - 1)
    tile_y = clamp(tile_y, 0, WorldState.world_data.height - 1)
    _check_encounter()

func _check_encounter() -> void:
    var settlement := WorldState.get_settlement_at(tile_x, tile_y)
    if settlement and settlement.faction_id != faction_id:
        order = "siege"
        siege_target_id = settlement.id
        siege_day = 0
    for other_army in WorldState.armies:
        if other_army.id == id or other_army.faction_id == faction_id:
            continue
        if other_army.tile_x == tile_x and other_army.tile_y == tile_y:
            AutoResolve.resolve(self, other_army)
```

### 6.4 Caravan

```gdscript
# scripts/economy/caravan.gd
class_name Caravan extends Resource

var origin_id:      int
var destination_id: int
var tile_x:         int
var tile_y:         int
var cargo:          Dictionary = {}   # resource_id -> quantity
var max_cargo:      float = 200.0
var faction_id:     int

# Called every hourly_pulse
func move_step() -> void:
    var dest := WorldState.get_settlement(destination_id)
    if not dest:
        return
    var dir: Vector2 = (Vector2(dest.tile_x, dest.tile_y) - Vector2(tile_x, tile_y)).normalized()
    tile_x += int(round(dir.x))
    tile_y += int(round(dir.y))

    if tile_x == dest.tile_x and tile_y == dest.tile_y:
        _deliver()

func _deliver() -> void:
    for rid in cargo:
        WorldState.get_settlement(destination_id).market.add_stock(rid, cargo[rid])
    cargo.clear()
    # Governor AI will reassign next tick
    WorldState.caravans.erase(self)
```

---

## 7. Phase 4 — The Player

```gdscript
# scripts/player/player_controller.gd
class_name PlayerController extends Node2D

var tile_x:  int = 10
var tile_y:  int = 10
var army:    Army = null
var gold:    float = 50.0
var renown:  float = 0.0

func _unhandled_key_input(event: InputEvent) -> void:
    if not event is InputEventKey or not event.pressed:
        return
    var moved := false
    match event.keycode:
        KEY_W: tile_y -= 1; moved = true
        KEY_S: tile_y += 1; moved = true
        KEY_A: tile_x -= 1; moved = true
        KEY_D: tile_x += 1; moved = true
        KEY_T: GameClock.advance(1)   # wait 1 hour
        KEY_E: _try_enter_settlement()

    if moved:
        tile_x = clamp(tile_x, 0, WorldState.world_data.width - 1)
        tile_y = clamp(tile_y, 0, WorldState.world_data.height - 1)
        GameClock.advance(1)
        _check_encounters()
        queue_redraw()

func _check_encounters() -> void:
    # Check for enemy armies
    for army_other in WorldState.armies:
        if army_other.tile_x == tile_x and army_other.tile_y == tile_y:
            if army_other.faction_id != army.faction_id:
                SceneManager.enter_battle(army, army_other)
                return

func _try_enter_settlement() -> void:
    var s := WorldState.get_settlement_at(tile_x, tile_y)
    if s:
        SceneManager.enter_city(s)
```

---

## 8. Phase 5 — Tactical Battle

### 8.0 Battalion Architecture

#### Scale rationale

At 1000v1000 individual units, a fixed 48×48 battle map (2,304 tiles) is impossible — 2,000 units fill 87% of it with no room to manoeuvre. The solution is a **two-level structure**:

- **Battalion** — the tactical AI unit, moves as one brain on the battle map, occupies one tile, formation 10 wide × 3 deep (30 unit slots)
- **Unit** — individual soldier inside the battalion, has full anatomy + blood + bleed simulation

| Scenario | Battalions per side | Battle map | Tiles used |
|---|---|---|---|
| 30v30 units | 1v1 | 64×64 | trivial |
| 300v300 | 10v10 | 64×64 | comfortable |
| 1000v1000 | 34v34 | 128×128 | 68 / 16,384 (0.4%) |

**Battle map size formula:**
```
battle_size = max(64, ceil(sqrt(total_units * 32)))
# 1000v1000 → sqrt(64000) ≈ 253 → 256×256
```

#### Battalion class

```gdscript
# scripts/battle/battalion.gd
class_name Battalion extends RefCounted

const FORMATION_WIDTH: int  = 10
const FORMATION_DEPTH: int  = 3
const MAX_SIZE:        int  = FORMATION_WIDTH * FORMATION_DEPTH  # 30

var display_name:   String
var faction_id:     int
var formation_tile: Vector2i      # single tile on the battle map
var facing:         Vector2i = Vector2i(1, 0)
var order:          String = "ADVANCE"  # ADVANCE, HOLD, CHARGE, RETREAT, FOLLOW
var morale:         float = 1.0
var units:          Array[Unit] = []  # up to MAX_SIZE; index 0..9 = front rank

## Effective combat strength for auto-resolve and morale checks.
func strength() -> float:
    var s: float = 0.0
    for u in units:
        if u.is_alive():
            s += u.strength
    return s

## Called each melee tick: front-rank units (indices 0..FORMATION_WIDTH-1)
## each pick the nearest enemy unit in an adjacent enemy battalion.
func front_rank() -> Array[Unit]:
    var out: Array[Unit] = []
    for i in range(mini(FORMATION_WIDTH, units.size())):
        if units[i].is_alive():
            out.append(units[i])
    return out

## Remove dead from front, pull rear ranks forward.
func compact() -> void:
    units = units.filter(func(u): return u.is_alive())
    # Morale penalty at 1/3 strength
    if units.size() < MAX_SIZE / 3 and morale > 0.4:
        morale -= 0.3
```

#### Army updated

```gdscript
# Army.battalions replaces Army.units
var battalions: Array[Battalion] = []

func total_units() -> int:
    var n: int = 0
    for b in battalions:
        n += b.units.size()
    return n

func is_defeated() -> bool:
    for b in battalions:
        if b.strength() > 0.0:
            return false
    return true
```

#### Rendering — MultiMeshInstance2D

With 1,000+ individual units visible, `Sprite2D` per unit will drop to single-digit FPS. Use one `MultiMeshInstance2D` per unit archetype (infantry, archer, cavalry, etc.) so all units of the same type render in a single GPU draw call:

```gdscript
# scripts/battle/multi_mesh_renderer.gd
class_name MultiMeshRenderer extends Node2D

# One entry per archetype
var _meshes: Dictionary = {}  # archetype_id -> MultiMeshInstance2D

func setup(archetypes: Array[String]) -> void:
    for a in archetypes:
        var mmi := MultiMeshInstance2D.new()
        mmi.multimesh = MultiMesh.new()
        mmi.multimesh.transform_format = MultiMesh.TRANSFORM_2D
        add_child(mmi)
        _meshes[a] = mmi

func sync_units(all_battalions: Array[Battalion]) -> void:
    # Group alive units by archetype, update instance transforms
    var groups: Dictionary = {}
    for b in all_battalions:
        for u in b.units:
            if not u.is_alive(): continue
            var a: String = u.archetype
            if not groups.has(a):
                groups[a] = []
            groups[a].append(Vector2(u.tile_x, u.tile_y) * TILE_PX)
    for a in groups:
        var mmi: MultiMeshInstance2D = _meshes[a]
        mmi.multimesh.instance_count = groups[a].size()
        for i in range(groups[a].size()):
            mmi.multimesh.set_instance_transform_2d(i, Transform2D(0.0, groups[a][i]))
```

#### Flow fields (battalion pathfinding)

Individual A\* per battalion every tick is O(n × map_size). Instead, compute one Dijkstra **flow field** per faction target and all battalions read from it in O(1):

```gdscript
# scripts/battle/flow_field_cache.gd
class_name FlowFieldCache

var _fields: Dictionary = {}  # Vector2i(target) -> Array[Array] of Vector2i directions

func get_direction(target: Vector2i, from: Vector2i, map: BattleMap) -> Vector2i:
    if not _fields.has(target):
        _fields[target] = _build(target, map)
    return _fields[target][from.y][from.x]

func _build(target: Vector2i, map: BattleMap) -> Array:
    # BFS/Dijkstra from target outward, store cheapest direction back at every tile
    ...
```

---

### 8.1 Unit Archetypes and Equipment

#### Archetypes — `data/unit_archetypes.json`

Eight archetypes define every unit class. Each specifies:
- **`min_tier`** — minimum settlement tier needed to recruit (0 = any hamlet)
- **`role`** — `levy`, `frontline`, `shock`, `ranged`
- **`attributes`** — strength, endurance, agility, balance, pain_tolerance (8–16)
- **`skills`** — weapon skills and dodging (15–50 points)
- **`equipment`** — slot → `[item_id, material_id]`; `"tier_mat"` means material scales with settlement tier

| Archetype | Role | Min Tier | Primary weapon | Notes |
|---|---|---|---|---|
| `laborer` | levy | 0 | pitchfork | unarmoured levy |
| `spearman` | frontline | 1 | spear + buckler | standard line |
| `footman` | frontline | 2 | longsword + heater | heavy line |
| `vanguard` | shock | 2 | battle_axe | unshielded two-hander |
| `pikeman` | frontline | 2 | pike | anti-cavalry |
| `archer` | ranged | 1 | shortbow + dagger | light armour |
| `crossbowman` | ranged | 3 | crossbow + pavise | heavy missile |
| `knight` | shock | 4 | greatsword | full plate |

**`tier_mat` resolution** — at recruitment, `"tier_mat"` is substituted with the material appropriate to the recruiting settlement's province tier:

| Settlement tier | Resolved material |
|---|---|
| 0–1 (hamlet/village) | `iron` |
| 2 (town) | `iron` |
| 3 (city) | `steel` |
| 4+ (metropolis) | `steel` |

#### Equipment slots

Slots follow the `items.json` layer system. Valid slot keys used in archetypes:

```
main_hand, off_hand, ammo
head_under, head_armor,  head_cover
torso_under, torso_armor, torso_over, torso_cover
arms_armor
legs_under, legs_armor
feet_over, feet_armor
hands_under, hands_armor
```

Armour items carry `layer` ∈ {`under`, `over`, `armor`, `cover`} and `coverage` — the list of body regions they protect. The damage resolver stacks all layers covering the struck region.

#### `equipment.gd` — slots dict

```gdscript
# scripts/unit/equipment.gd
class_name Equipment extends RefCounted

## Populated from unit_archetypes.json at spawn time.
## Key = slot string, Value = {item: Dictionary, material: Dictionary}
var slots: Dictionary = {}

func get_weapon() -> Dictionary:
    return slots.get("main_hand", {}).get("item", {})

func get_shield() -> Dictionary:
    return slots.get("off_hand", {}).get("item", {})

## Returns all armour layers covering a given body region, outermost → innermost.
func get_layers_for(region: String) -> Array:
    var out: Array = []
    for slot_key in slots:
        var entry: Dictionary = slots[slot_key]
        var item: Dictionary = entry.get("item", {})
        if item.get("type") != "armor" and item.get("type") != "shield":
            continue
        if region in item.get("coverage", []):
            out.append({"item": item, "material": entry.get("material", {})})
    # Sort: cover layer last (outermost absorbed first in practice — reverse order)
    # 'under' < 'over' < 'armor' < 'cover'  but combat penetrates armor → under
    var layer_order: Dictionary = {"cover": 0, "armor": 1, "over": 2, "under": 3}
    out.sort_custom(func(a, b):
        return layer_order.get(a.item.get("layer", "under"), 3) <
               layer_order.get(b.item.get("layer", "under"), 3)
    )
    return out
```

### 8.1b Unit Data

```gdscript
# scripts/unit/unit.gd
class_name Unit extends Resource

var display_name:   String
var archetype:      String   # key in unit_archetypes.json
var faction_id:     int
var body_parts:     Dictionary = {}   # part_name -> BodyPart
var blood:          float = 5000.0
var bleed_rate:     float = 0.0      # ml per second
var morale:         float = 1.0
var status:         Array[String] = []  # stunned, prone, panicked, routing
var equipment:      Equipment = null
var tile_x:         int
var tile_y:         int

# Derived from archetype attributes
var base_speed:     float = 2.0    # derived from agility
var agility_bonus:  float = 0.0
var strength:       float = 1.0    # derived from strength attribute

func is_alive() -> bool:
    return blood > 0.0 and not _vital_destroyed()

func _vital_destroyed() -> bool:
    for part_name in body_parts:
        if body_parts[part_name].vital_destroyed():
            return true
    return false

func apply_bleed_tick(tick_duration: float) -> void:
    blood -= bleed_rate * tick_duration
    if blood < 5000.0 * 0.40:
        morale -= 0.1
        if not "panicked" in status:
            status.append("panicked")
    if blood <= 0.0:
        blood = 0.0
```

```gdscript
# scripts/unit/body_part.gd
class_name BodyPart extends RefCounted

var name:     String
var tissues:  Array[Tissue] = []   # ordered front-to-back (skin first)
var size:     float = 1.0          # used for hit weighting

func vital_destroyed() -> bool:
    for t in tissues:
        if t.is_vital and t.hp <= 0.0:
            return true
    return false
```

```gdscript
# scripts/unit/tissue.gd
class_name Tissue extends RefCounted

var name:            String
var hp:              float
var max_hp:          float
var hardness:        float   # from material or biological table
var is_vital:        bool = false
var is_spine:        bool = false
var is_arterial:     bool = false
var bleed_on_damage: float = 1.0   # ml/sec per damage point; arterial = 5.0
```

### 8.2 Damage Resolver

```gdscript
# scripts/battle/damage_resolver.gd
class_name DamageResolver

## Weapon physics fields (from `items.json`)
##   dmg          — base damage value
##   dmg_type     — "cut" | "pierce" | "blunt"
##   contact      — contact area in cm²; smaller = higher effective pressure
##   penetration  — weapon hardness proxy (higher pierces harder armour)
##   weight       — kg
##   velocity     — not stored; derived as speed_factor from attacker agility
##   range        — reach in tiles (1 = adjacent; polearms 2.2; bows 8–15)
##   is_ranged    — requires ammo slot
##
## Material physics fields (from `materials.json`)
##   hardness       — resistance to being deformed by another material
##   density        — g/cm³
##   impact_yield   — energy (J) to deform under blunt strike
##   shear_yield    — energy (J) to cut/pierce through
##   elasticity     — fraction of energy returned (spring-back)

static func resolve_attack(attacker: Unit, defender: Unit) -> void:
    # 1. Pick hit location (weighted by body part size)
    var part: BodyPart = _pick_hit_location(defender)

    # 2. Weapon stats from items.json
    var weapon: Dictionary   = attacker.equipment.get_weapon()
    var w_mat:  Dictionary   = attacker.equipment.slots.get("main_hand", {}).get("material", {})
    var w_hard: float        = w_mat.get("hardness", 1.0)
    var w_pen:  float        = weapon.get("penetration", 1.0)   # weapon penetration factor
    var dmg_type: String     = weapon.get("dmg_type", "blunt")

    # 3. Calculate raw energy — kinetic approximation
    #    E = base_dmg × (1 + strength_bonus) / contact_area
    var base_dmg:    float = weapon.get("dmg", 1.0)
    var contact:     float = maxf(weapon.get("contact", 10.0), 0.5)
    var energy:      float = base_dmg * (1.0 + attacker.strength * 0.1) / contact

    # 4. Pass energy through equipment layers covering the hit region
    energy = _absorb_equipment(energy, defender, part.name, dmg_type, w_hard, w_pen)

    # 5. Pass energy through tissues in depth order
    for tissue in part.tissues:
        if energy <= 0.0:
            break
        var t_hard: float = tissue.hardness
        if w_hard < t_hard:
            break  # weapon cannot penetrate this tissue
        var absorbed: float = minf(energy, tissue.hp)
        tissue.hp -= absorbed
        energy -= absorbed
        # Bleed
        if absorbed > 0.0:
            var bleed_rate: float
            if tissue.is_arterial:
                bleed_rate = absorbed * 5.0
            else:
                bleed_rate = absorbed * 1.0
            defender.bleed_rate += bleed_rate

static func _pick_hit_location(unit: Unit) -> BodyPart:
    var total_size: float = 0.0
    for p in unit.body_parts.values():
        total_size += p.size
    var roll: float = randf() * total_size
    var acc: float = 0.0
    for p in unit.body_parts.values():
        acc += p.size
        if roll <= acc:
            return p
    return unit.body_parts.values()[0]

static func _absorb_equipment(energy: float, unit: Unit, part_name: String,
        dmg_type: String, w_hard: float, w_pen: float) -> float:
    # Iterate armour layers outermost → innermost (cover → armor → over → under)
    if not unit.equipment:
        return energy
    var layers: Array = unit.equipment.get_layers_for(part_name)
    for entry in layers:
        var mat:  Dictionary = entry["material"]
        var item: Dictionary = entry["item"]
        var a_hard: float    = mat.get("hardness", 1.0)
        # Penetration check: weapon penetration vs armour hardness
        if w_pen < a_hard * 0.5:
            energy *= 0.1   # mostly blocked; small blunt pass-through
            continue
        # Choose yield based on attack type
        var yield_val: float
        match dmg_type:
            "pierce", "cut": yield_val = mat.get("shear_yield", 50.0)
            _:               yield_val = mat.get("impact_yield", 50.0)
        var prot: float = item.get("prot", 1.0)   # prot rating from items.json
        var absorbed: float = minf(energy, prot * yield_val * 0.001)
        energy -= absorbed
    return maxf(energy, 0.0)
```

### 8.3 Battle Manager

```gdscript
# scripts/battle/battle_manager.gd
class_name BattleManager extends Node

const TICK_DURATION: float = 0.3  # seconds per bleed tick

var all_units: Array[Unit] = []
var initiative_order: Array[Unit] = []
var current_unit_index: int = 0
var tick_timer: float = 0.0

func setup(army_a: Army, army_b: Army) -> void:
    all_units.clear()
    # Flatten battalions → units for the bleed tick loop
    for b in army_a.battalions:
        for u in b.units:
            u.tile_x = 2; u.tile_y = randi_range(2, 10)
            all_units.append(u)
    for b in army_b.battalions:
        for u in b.units:
            u.tile_x = 18; u.tile_y = randi_range(2, 10)
            all_units.append(u)
    _build_initiative()

func _build_initiative() -> void:
    initiative_order = all_units.duplicate()
    initiative_order.sort_custom(func(a, b):
        var sa: float = a.base_speed - a.agility_bonus + randf_range(-0.3, 0.3)
        var sb: float = b.base_speed - b.agility_bonus + randf_range(-0.3, 0.3)
        return sa < sb
    )

func _process(delta: float) -> void:
    # Bleed tick
    tick_timer += delta
    if tick_timer >= TICK_DURATION:
        tick_timer -= TICK_DURATION
        for u in all_units:
            if u.is_alive():
                u.apply_bleed_tick(TICK_DURATION)
                _check_morale(u)

    _check_battle_end()

func advance_unit() -> void:
    # Advance one unit's action (called when player confirms, or auto for AI)
    var unit: Unit = initiative_order[current_unit_index]
    if unit.is_alive():
        UnitAI.act(unit, self)
    current_unit_index = (current_unit_index + 1) % initiative_order.size()

func _check_morale(unit: Unit) -> void:
    if unit.morale <= 0.2 and not "routing" in unit.status:
        unit.status.append("routing")

func _check_battle_end() -> bool:
    var factions_alive: Array = []
    for u in all_units:
        if u.is_alive() and not "routing" in u.status:
            if not factions_alive.has(u.faction_id):
                factions_alive.append(u.faction_id)
    if factions_alive.size() <= 1:
        _end_battle(factions_alive[0] if factions_alive.size() > 0 else -1)
        return true
    return false

func _end_battle(winner_faction_id: int) -> void:
    # Feed results back to WorldState, then return to overworld
    SceneManager.return_to_overworld(winner_faction_id)
```

---

## 9. Phase 6 — Siege and Auto-Resolution

### 9.0 Siege Engines — `data/siege_engines.json`

Five engine types, loaded at battle setup. Each carries physics values that plug directly into `DamageResolver`.

| Engine | Symbol | dmg | dmg_type | range | reload | aoe | crew | mobile | notes |
|---|---|---|---|---|---|---|---|---|---|
| `ballista` | X | 80 | pierce | 50 | 15 | 0 | 2 | ✓ | overpenetrates |
| `catapult` | C | 200 | blunt | 70 | 35 | 1 | 4 | ✓ | 2×2 footprint |
| `battering_ram` | R | 150 | blunt | 1 | 5 | 0 | 6 | ✓ | 3×2 footprint; 80% crew protection |
| `siege_tower` | S | 0 | — | — | — | — | 8 | ✓ | provides wall access; 800 HP; 3×2 footprint |
| `trebuchet` | V | 400 | blunt | 120 | 60 | 3 | 10 | ✗ | must be placed; 3×3 footprint |

Key fields:
- **`velocity`** — projectile speed (m/s), multiplied into energy in DamageResolver
- **`accuracy`** — hit-chance multiplier (0.35 trebuchet → 0.85 ballista)
- **`aoe`** — tile radius of splash damage (0 = point, 3 = trebuchet)
- **`overpenetrate`** — ballista bolt continues through target to hit next unit in line
- **`footprint`** — list of `[x, y]` offsets; convert to `Vector2i` on load
- **`protection_bonus`** — fraction of normal damage crew takes while operating (ram, tower)

```gdscript
# Loading siege engines at battle setup:
var _engines: Dictionary = {}  # id → data

func _load_siege_engines() -> void:
    var f := FileAccess.open("res://data/siege_engines.json", FileAccess.READ)
    var raw: Dictionary = JSON.parse_string(f.get_as_text())
    f.close()
    for engine_id in raw:
        if engine_id == "_note":
            continue
        var e: Dictionary = raw[engine_id].duplicate()
        # Convert footprint arrays to Vector2i
        var fp: Array = []
        for pt in e.get("footprint", []):
            fp.append(Vector2i(pt[0], pt[1]))
        e["footprint"] = fp
        _engines[engine_id] = e
```

### 9.1 Auto-Resolution

```gdscript
# scripts/battle/auto_resolve.gd
class_name AutoResolve

static func resolve(attacker_army: Army, defender_army: Army) -> void:
    var atk_str: float = _strength(attacker_army)
    var def_str: float = _strength(defender_army)

    var atk_roll: float = atk_str * randf_range(0.8, 1.2)
    var def_roll: float = def_str * randf_range(0.8, 1.2)

    var ratio: float = atk_roll / maxf(def_roll, 1.0)
    var winner:Army
    var loser: Army

    if atk_roll >= def_roll:
        winner = attacker_army
        loser  = defender_army
    else:
        winner = defender_army
        loser  = attacker_army

    var loser_casualties:  int = int(loser.units.size()  * clampf(ratio,       0.1, 0.8))
    var winner_casualties: int = int(winner.units.size() * clampf(1.0/ratio * 0.4, 0.05, 0.6))

    _remove_units(loser, loser_casualties)
    _remove_units(winner, winner_casualties)

    if loser.units.size() == 0:
        WorldState.armies.erase(loser)

static func _strength(army: Army) -> float:
    var total: float = 0.0
    for u in army.units:
        total += u.blood / 5000.0 * 10.0   # health fraction × tier weight
    return total

static func _remove_units(army: Army, count: int) -> void:
    for _i in range(count):
        if army.units.is_empty():
            break
        army.units.pop_back()
```

### 9.2 Siege

Handled in `Army.march()` when `order == "siege"`. Add `Siege.daily_tick(army, settlement)`:

```gdscript
# scripts/battle/siege.gd
class_name Siege

static func daily_tick(attacker: Army, settlement: Settlement) -> void:
    attacker.siege_day += 1

    var wall_level:  int   = settlement._building_level("walls")
    var wall_mult:   float = _wall_multiplier(wall_level)

    var atk_str: float = AutoResolve._strength(attacker) * (1.0 + attacker.siege_day * 0.05)
    var def_str: float = settlement._garrison_strength() * wall_mult

    # Defender attrition
    var attrition: float = 0.02 + attacker.siege_day * 0.005
    settlement._reduce_garrison(attrition)

    # Breach check
    var breach_chance: float = (atk_str / maxf(def_str, 1.0)) * 0.15 * (1.0 + attacker.siege_day / 4.0)
    if randf() < breach_chance:
        _capture(attacker, settlement)

static func _wall_multiplier(level: int) -> float:
    var mult: float = 1.0
    if level >= 3:  mult *= 1.0 / 0.75   # towers
    if level >= 7:  pass                  # defensive engines handled separately
    if level >= 9:  mult *= 1.0 / 0.5    # moat
    if level >= 10: mult *= 1.3
    return mult

static func _capture(attacker: Army, settlement: Settlement) -> void:
    # Transfer ownership
    var old_faction: int = settlement.faction_id
    settlement.faction_id = attacker.faction_id

    # Looting
    var loot_crowns: float = settlement.market.get_stock("crowns") * 0.5
    settlement.market.deduct_stock("crowns", loot_crowns)
    loot_crowns += settlement.population * 2.0
    WorldState.get_faction(attacker.faction_id).treasury += loot_crowns

    # Leave half army as garrison
    var garrison_count: int = attacker.units.size() / 2
    settlement._set_garrison(attacker.units.slice(0, garrison_count))
    attacker.units = attacker.units.slice(garrison_count)

    attacker.order = "hold"
    attacker.siege_target_id = -1
    attacker.siege_day = 0
```

---

## 10. Phase 7 — UI Layer

### Mode Switching (SceneManager)

One `Node` child of the root scene acts as a mode switcher:

```gdscript
# autoloads/SceneManager.gd — register as autoload
extends Node

@onready var _root: Node = get_tree().root

var _current_mode: Node = null

func goto_overworld() -> void:
    _replace(preload("res://scenes/overworld/Overworld.tscn").instantiate())

func enter_city(s: Settlement) -> void:
    var city: Node = preload("res://scenes/city/CityScreen.tscn").instantiate()
    city.settlement = s
    _replace(city)

func enter_battle(my_army: Army, enemy_army: Army) -> void:
    var battle: Node = preload("res://scenes/battle/BattleScene.tscn").instantiate()
    battle.setup(my_army, enemy_army)
    _replace(battle)

func return_to_overworld(_winner: int) -> void:
    goto_overworld()

func _replace(new_mode: Node) -> void:
    if _current_mode:
        _current_mode.queue_free()
    _root.add_child(new_mode)
    _current_mode = new_mode
```

### City Screen Panels

The `CityScreen` has four tabs:

| Tab | Content |
|---|---|
| **Market** | Table of every resource: name, current stock, current price. Buy/sell buttons. |
| **Buildings** | Grid of building slots; each shows type, level, upgrade button + cost. |
| **Recruit** | List of available unit types; hire with gold. |
| **Overview** | Population breakdown, happiness, unrest, 7-day price chart. |

---

## 11. Data Tables

### materials.json — 18 materials

Object keyed by material id. All fields are dimensionless unless noted.

| id | hardness | density | impact_yield | shear_yield | elasticity |
|---|---|---|---|---|---|
| flesh | 0 | 1 | 10 | 5 | 0.1 |
| bone | 30 | 2 | 100 | 50 | 0.05 |
| cloth | 2 | 1 | 5 | 20 | 0.5 |
| leather | 10 | 2 | 40 | 30 | 0.3 |
| wood | 15 | 3 | 80 | 40 | 0.2 |
| iron | 40 | 10 | 300 | 200 | 0.01 |
| steel | 60 | 12 | 500 | 400 | 0.02 |
| bronze | 35 | 11 | 250 | 180 | 0.01 |
| copper | 25 | 9 | 200 | 120 | 0.01 |

> Damage resolver uses **`shear_yield`** for cut/pierce, **`impact_yield`** for blunt.

### resources.json — 40+ resources across 8 categories

| Category | Resources (id) |
|---|---|
| food | grain, fish, meat, game |
| fuel | wood, coal, peat |
| textile | wool, cloth, linen, silk, leather, hides |
| luxury | furs, fine_garments, ale, salt, spices, ivory, jewelry |
| raw_metal | iron, copper, tin, lead, silver, gold |
| refined_metal | bronze, steel |
| mineral | stone, marble, clay, sand, glass_sand, gems |
| crafted | tools, bricks |

Key fields per entry: `category`, `weight` (kg/unit), `produced_by` (array of industries), `consumed_by` (`"all"`, `"burghers"`, `"nobility"`, `"industry"`, `"craft"`), `base_price` (silver coins).

Selected base prices:

| id | base_price | consumed_by |
|---|---|---|
| grain | 10 | all |
| fish | 8 | all |
| wood | 5 | all |
| iron | 40 | craft, industry |
| steel | 120 | craft |
| ale | 30 | burghers |
| salt | 45 | nobility |
| furs | 120 | nobility |
| spices | 500 | nobility |
| jewelry | 1000 | nobility |

### buildings.json — 23 buildings, 3 categories, 10 max levels

**Industry** (provide raw materials and refined goods):

| id | cost | labor | tier req | per-level bonus |
|---|---|---|---|---|
| farm | 500 | 500 | 1 | +50% grain/level |
| lumber_mill | 800 | 800 | 1 | +100% wood/level |
| fishery | 600 | 600 | 1 | +50% fish/level |
| mine | 1500 | 1500 | 1 | +50% ore/level |
| pasture | 700 | 700 | 1 | +50% meat-wool/level |
| blacksmith | 2000 | 2000 | 2 | +100% steel/level |
| tannery | 2500 | 1500 | 2 | +100% leather/level |
| weaver | 2500 | 1500 | 2 | +100% cloth/level |
| brewery | 3000 | 1500 | 2 | +100% ale/level |
| tailor | 3500 | 1800 | 2 | +100% fine_garments/level |
| goldsmith | 8000 | 3000 | 3 | +100% jewelry/level |

**Military**:

| id | cost | labor | tier req | effect |
|---|---|---|---|---|
| stone_walls | 5000 | 5000 | 2 | siege defense per level |
| barracks | 5000 | 2500 | 2 | garrison cap (odd) + troop quality (even) per level |
| training_ground | 4000 | 2000 | 2 | recruit quality tier per level |
| granary | 1200 | 1000 | 1 | +50% food storage + starvation resist/level |
| watchtower | 2000 | 1200 | 1 | stability + bandit reduction |

**Civil**:

| id | cost | labor | tier req | effect |
|---|---|---|---|---|
| housing_district | 1000 | 800 | 1 | +100 pop cap/level |
| market | 1000 | 1200 | 1 | trade slots + income |
| road_network | 1500 | 1500 | 1 | +15% trade throughput + tax efficiency |
| merchant_guild | 5000 | 2000 | 3 | caravan capacity + global reach |
| warehouse_district | 3000 | 1500 | 2 | +100% inventory cap/level |
| cathedral | 8000 | 5000 | 3 | +stability + noble loyalty |
| tavern | 800 | 1000 | 1 | +happiness + migration |

### items.json — weapons (excerpt)

| id | dmg | dmg_type | contact | penetration | weight | range | hands |
|---|---|---|---|---|---|---|---|
| dagger | 5 | pierce | 1 | 30 | 0.4 | 1 | 1 |
| shortsword | 8 | cut | 20 | 10 | 1.2 | 1 | 1 |
| longsword | 12 | cut | 30 | 15 | 1.8 | 1 | 2 |
| greatsword | 18 | cut | 40 | 20 | 3.5 | 1 | 2 |
| estoc | 10 | pierce | 2 | 50 | 1.4 | 1 | 1 |
| halberd | 16 | cut | 15 | 30 | 3.5 | 2.2 | 2 |
| pike | 10 | pierce | 1 | 60 | 4.0 | 3.2 | 2 |
| longbow | 10 | pierce | 1 | 35 | 1.5 | 12 | 2 |
| crossbow | 15 | pierce | 1 | 55 | 4.0 | 15 | 2 |
| maul | 20 | blunt | 20 | 1 | 6.0 | 1 | 2 |

Shields carry `prot` and `block_chance`. Armour carries `prot`, `coverage` (body regions), `layer` (`under`/`over`/`armor`/`cover`), and `shear_mult` where applicable.

---

## 12. The Time Model

Every player action calls `GameClock.advance(hours)`. The clock emits signals synchronously in the main thread, which call settlement and faction logic. This means no async complexity — the game only moves forward when the player acts.

```
Player presses W          → advance(1)
Player waits (T)          → advance(1)
Player enters battle      → BattleManager handles its own tick loop
  (each unit action = no GameClock advance during battle)
Player exits city         → advance(4)   // "time passed while in city"
```

> The turn counter is the single source of truth for time.  
> The daily pulse fires at turn multiples of 24, the weekly at 168.  
> Never bypass `GameClock.advance()` to trigger a pulse manually.

---

## 13. Critical Invariant Checks

Implement these as `assert()` calls or a `debug_validate()` method called in `_process()` during development. Remove from release builds.

```gdscript
# Add to Settlement.daily_tick() during development:
func _validate() -> void:
    assert(laborers + burghers + nobility == population,
        "Population class sum mismatch in %s" % name)
    for rid in market.inventory:
        assert(market.inventory[rid] >= 0.0,
            "Negative inventory for %s in %s" % [rid, name])
    assert(buildings.size() <= [0, 3, 6, 10, 15][tier],
        "Building slot overflow in %s" % name)
    assert(faction_id >= 0,
        "Settlement %s has no faction" % name)

# Add to GameClock.advance():
func _validate_turn() -> void:
    assert(turn >= 0, "Turn counter went negative")
    # Province IDs never change after world gen — no runtime check needed
    # Material DB must exist before any unit is created — enforce in MaterialDB._ready()
```

---

## 14. System Wiring Diagram

```
┌─────────────────────────────────────────────────────────┐
│                      AUTOLOADS                           │
│  GameClock ──signal──►  WorldState  ◄──► SceneManager   │
│                            │                             │
│                   MaterialDB (read-only)                 │
└─────────────────────────────────────────────────────────┘
                             │
          ┌──────────────────┼──────────────────┐
          ▼                  ▼                  ▼
    settlements[]        factions[]          armies[]
    (Settlement)         (Faction)           (Army)
          │                  │                  │
    market (Market)    faction_ai          caravan (Caravan)
    production         (weekly pulse)      (hourly pulse)
    governor_ai
    buildings[]
    (daily pulse)

          │ enter/exit                │ collide
          ▼                           ▼
    CityScreen                  BattleScene
    (Mode: CITY)                (Mode: BATTLE)
                                     │
                               BattleManager
                               ├─ InitiativeQueue
                               ├─ DamageResolver
                               ├─ UnitAI (FSM)
                               └─ AutoResolve (AI vs AI)
```

---

*Follow the phase order. Prototype one system at a time with debug output before building the next. The world generator (Phase 1) and economy tick (Phase 2) are the foundation — everything else depends on them being correct.*
