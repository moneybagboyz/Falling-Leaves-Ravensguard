# Phase 3 — Character and Local Map Layer

**Status:** ✅ Complete  
**Goal:** The player exists as a living entity inside a settlement — they have an identity, needs, a job, shelter, and can talk to NPCs.

---

## Overview

Phase 3 added the full character layer on top of the world simulation built in Phase 2. It covers data modelling, scene routing, building placement, the settlement map view, z-levels, needs, work, housing, NPC pools, NPC schedules, and basic dialogue. No further infrastructure changes are needed before Phase 4 begins.

---

## Tasks Completed

### P3-01 — Character data schemas and starter content

Created `data/schemas/background.schema.json`, `trait.schema.json`, and `skill.schema.json`. Populated `data/characters/` with minimum starter sets:

- **5 backgrounds:** farmer, soldier, merchant, wanderer, hedge-scholar. Each defines attribute bonuses, starting traits, starting skills, and population class.
- **~10 traits:** hardworking, cowardly, strong, quick, perceptive, silver-tongued, cautious, reckless, scholarly, iron-gut.
- **~12 skills:** farming, melee, archery, trading, crafting, persuasion, survival, herbalism, smithing, stealth, athletics, scholarship.

All load and validate through `DataLoader` / `ContentRegistry` at startup.

---

### P3-02 — PersonState and WorldState fields

Implemented `PersonState` (`src/simulation/character/person_state.gd`):

| Field | Type | Notes |
|---|---|---|
| `person_id` | `String` | EntityRegistry ID |
| `name` | `String` | Display name |
| `background_id` | `String` | References `data/characters/backgrounds/` |
| `population_class` | `String` | peasant / artisan / merchant / noble |
| `attributes` | `Dictionary` | strength, endurance, agility, perception, intelligence, charisma |
| `traits` | `Array[String]` | Trait IDs |
| `skills` | `Dictionary` | skill_id → `{level, progress}` |
| `needs` | `Dictionary` | hunger, fatigue, temperature_stress |
| `coin` | `float` | Personal coin balance |
| `active_role` | `String` | Current labor slot ID |
| `work_cell_id` | `String` | Cell of assigned role |
| `shelter_status` | `String` | `""` / `"rented"` / `"derelict_claimed"` |
| `location` | `Dictionary` | `{cell_id, lx, ly, z_level}` |
| `schedule_state` | `String` | working / resting / wandering / idle |
| `social_links` | `Array` | `{person_id, relationship_type, strength}` |
| `pending_perk_unlocks` | `Array` | `{skill_id, level}` queued for Phase 6 |

Full `to_dict()` / `from_dict()` round-trip. `WorldState` gained `characters: Dictionary`, `player_character_id: String`, `player_location: Dictionary`, and `npc_pool: Dictionary`.

---

### P3-03 — CharacterCreationScreen

Pure-code scene (`src/ui/character_creation/character_creation_screen.gd`):

- Left column: scrollable background picker; clicking a background highlights it and populates the detail panel.
- Centre column: detail panel shows background description, attribute bonuses (colour-coded), starting traits, starting skills.
- Right column: attribute allocation — base 5 per attribute; player distributes a fixed point budget using +/− buttons; budget counter updates live.
- Bottom bar: name input, validation error label, **▶ BEGIN** button.

On confirm: writes `PersonState` into `WorldState.characters`, sets `player_character_id`, sets `player_location.cell_id` to the anchor cell of the highest-tier available starting settlement, then calls `SceneManager.replace_scene("world_view.tscn")`.

---

### P3-04 — Skill XP and perk thresholds

`PersonState.award_skill_xp(skill_id, amount)`:

1. Applies trait multipliers from `ContentRegistry` (e.g. *hardworking* boosts farming XP).
2. Increments `progress` by `adjusted / xp_per_level`.
3. When `progress >= 1.0`, levels up and resets progress.
4. If the new level crosses a threshold in `PERK_THRESHOLDS = [5, 10, 20]`, appends `{skill_id, level}` to `pending_perk_unlocks`. Perk resolution is Phase 6.

---

### P3-05 — SceneManager autoload

`src/core/scene_manager.gd` registered as an autoload. Stack-based router:

| Method | Behaviour |
|---|---|
| `replace_scene(path, params)` | Changes scene, no stack entry |
| `push_scene(path, params)` | Saves current scene path on stack, loads new scene |
| `pop_scene(params)` | Restores previous scene from stack |
| `take_params()` | Called once in `_ready()`; returns and clears pending params |
| `stack_depth()` | Returns stack depth |
| `clear_stack()` | Empties stack |

All existing transitions (WorldGenScreen → WorldView, CharacterCreationScreen → WorldView) migrated to use `SceneManager`. `WorldView` gained an **▶ ENTER SETTLEMENT** button (enabled when a settlement is selected) that calls `SceneManager.push_scene("settlement_view.tscn", {settlement_id: ...})`.

---

### P3-06 — BuildingPlacer

`src/worldgen/building_placer.gd` runs as step 13 in `RegionGenerator` after history simulation.

For each settlement:
1. Collects territory cells within a **Chebyshev radius** scaled by tier (radius 1–5).
2. Claims unclaimed, non-water cells and writes `owner_settlement_id`.
3. Builds a tier-scaled placement list (farms, inn, well, granary, market stall, derelict, open land) and Fisher-Yates shuffles it.
4. Stamps `building_id` and `z_levels` onto each `RegionCell` dict.
5. Harvests `labor_slots` from building templates into `SettlementState.labor_slots`.
6. Seeds `market_inventory` with starter goods scaled by tier.

`RegionCell` gained `building_id: String`, `owner_settlement_id: String`, and `z_levels: Array`. `SettlementState` gained `labor_slots: Array`, `market_inventory: Dictionary`, and `territory_cell_ids: Array[String]`.

**Building starter set:** `farm_plot`, `inn`, `well`, `granary`, `market_stall`, `derelict`, `open_land`.

---

### P3-07 — SettlementView

`src/ui/settlement_view.gd` + `settlement_view.tscn` — pure-code scene, no art assets.

- **Right panel:** scrollable tile map. Each territory `RegionCell` renders as a 64×64 coloured rect keyed to `BUILDING_COLORS` (building type) or `TERRAIN_COLORS` (fallback). Short text abbreviation labels on each tile.
- **Player pawn:** white 24×24 rect with `@` label. Moves 8-directional via WASD + Q/E/Z/C diagonals. Yellow cursor underlay highlights current cell.
- **Left panel:** settlement name/tier/population/prosperity/unrest; current cell info; player status; context interaction button; key hints.

On entry: reads `SceneManager.take_params()` for `settlement_id`. On exit (Esc): calls `NpcPoolManager.cull()` then `SceneManager.pop_scene()`.

---

### P3-08 — Z-level support

`RegionCell.z_levels: Array` holds which floor levels exist at a cell (e.g. `[0, 1]` for inn; `[0, -1]` for granary).

In `SettlementView`:
- `_current_z: int` tracks the active floor.
- `KEY_PAGEUP` / `KEY_PAGEDOWN` calls `_try_change_zlevel(delta)` — validates the target z exists in the cell's `z_levels` before switching.
- Floor label shows: Ground floor / Upper floor / Cellar.
- Cell info panel shows PgUp/PgDn hints based on available floors.
- `WorldState.player_location.z_level` is updated on every change.

Data files updated:
- `inn.json` → `"z_levels": [0, 1]`
- `granary.json` → `"z_levels": [0, -1]`

---

### P3-09 — NPC pool initialisation

`src/simulation/character/npc_pool_manager.gd`:

- **`populate(world_state, ss, world_seed)`** — idempotent. RNG seeded from `hash(settlement_id) ^ world_seed`. Generates up to 40 NPCs proportional to settlement population class headcounts. Assigns open labor slots to NPCs whose class matches the slot type. Each NPC is a full `PersonState` with `population_class`, `work_cell_id`, `schedule_state = "working"`.
- **`cull(world_state, settlement_id, player_state)`** — removes transient NPCs from `npc_pool`. NPCs appearing in `player_state.social_links` are promoted to `world_state.characters` so they persist between visits.
- Name tables: 24 masc + 24 fem first names, 24 surnames. Randomly combined.

---

### P3-10 — Work loops

`src/simulation/character/work_system.gd` — registered on `PRODUCTION_PULSE`:

- Each strategic tick: finds every character with `active_role != ""`, locates the matching `SettlementState.labor_slots` entry, deducts `wage_per_day` from settlement coin, credits `PersonState.coin`, awards `award_skill_xp(slot.skill_required, 5.0)` modified by hunger penalty (hunger > 50% reduces XP linearly to zero at 100%).
- **`assign_player_to_slot(slot_index)`** — writes `player.active_role`, `player.work_cell_id`, marks slot `is_filled = true`.
- **`remove_player_from_slot()`** — clears all of the above.

`PersonState` gained `coin: float = 0.0` (serialised).

---

### P3-11 — NeedsSystem

`src/simulation/character/needs_system.gd` — registered on `MOVEMENT` (every tick):

| Need | Rate | Notes |
|---|---|---|
| Hunger | +0.0008 / tick | Always rises |
| Fatigue (working) | +0.0006 / tick | While `active_role != ""` |
| Fatigue (resting) | −0.0006 / tick | While not working |
| Fatigue (sheltered) | −0.002 extra / tick | While `shelter_status != ""` |

Inn rent: 2 coin deducted once per 24-tick day while `shelter_status == "rented"`. If the player can't afford rent, `shelter_status` is cleared (eviction).

Hunger > 50% applies an XP penalty in `WorkSystem`. Hunger and fatigue are colour-coded in the player status panel (green → yellow → red).

---

### P3-12 — Housing

Interactions available from the `SettlementView` context button and dialogue panel:

| Context | Action | Effect |
|---|---|---|
| Inn cell | Rent room | `shelter_status = "rented"`, 2 coin/day charged by NeedsSystem |
| Derelict cell | Claim shelter | `shelter_status = "derelict_claimed"`, free |

`shelter_status` drives fatigue recovery rate in NeedsSystem. Having shelter roughly triples rest recovery speed.

---

### P3-13 — NPC schedule system

`src/simulation/character/npc_schedule_system.gd` — registered on `MOVEMENT` (every tick):

Uses `tick % 24` as time-of-day (TOD):

| TOD range | schedule_state |
|---|---|
| 0–1 | `"idle"` (pre-dawn) |
| 2–9 | `"working"` |
| 10 | `"wandering"` (midday break) |
| 11–15 | `"working"` (afternoon) |
| 16–23 | `"resting"` (night) |

NPCs with no job (`active_role == ""`) wander during day hours and rest at night.

---

### P3-14 — Dialogue interface

Pressing `F` or `T` in `SettlementView` opens a centred dialogue overlay:

- **With NPC present:** Shows NPC name and class. Options:
  - *Inquire about work* — lists all open labor slots in the settlement with slot ID, wage, and required skill.
  - *Ask about the settlement* — shows tier, population, prosperity, and unrest.
- **At market stall:** *Browse goods* — shows full `market_inventory` with quantities.
- **At inn:** *Rent a room* — sets `shelter_status = "rented"`.
- `Esc` / `F` closes the dialogue.

---

## New Files

| File | Purpose |
|---|---|
| `src/core/scene_manager.gd` | Stack-based scene router autoload |
| `src/worldgen/building_placer.gd` | Stamps buildings onto region cells at worldgen |
| `src/ui/settlement_view.gd` | Settlement local map view |
| `src/ui/settlement_view.tscn` | Scene root for SettlementView |
| `src/simulation/character/work_system.gd` | Wage + skill XP on PRODUCTION_PULSE |
| `src/simulation/character/needs_system.gd` | Hunger / fatigue decay on MOVEMENT |
| `src/simulation/character/npc_pool_manager.gd` | NPC pool populate / cull |
| `src/simulation/character/npc_schedule_system.gd` | NPC schedule transitions on MOVEMENT |

## Modified Files

| File | Changes |
|---|---|
| `src/simulation/character/person_state.gd` | Added `coin`, `population_class`, `pending_perk_unlocks`; full serialisation |
| `src/simulation/world/world_state.gd` | Added `characters`, `player_character_id`, `player_location`, `npc_pool` |
| `src/simulation/settlement/settlement_state.gd` | Added `labor_slots`, `market_inventory`, `territory_cell_ids` |
| `src/worldgen/region_cell.gd` | Added `building_id`, `owner_settlement_id`, `z_levels` |
| `src/worldgen/region_generator.gd` | Added step 13: `BuildingPlacer.place()` |
| `src/main/bootstrap.gd` | Registered `WorkSystem`, `NeedsSystem`, `NpcScheduleSystem` hooks |
| `src/ui/world_view.gd` | Added ▶ ENTER SETTLEMENT button; SceneManager migration |
| `src/ui/world_gen_screen.gd` | SceneManager migration |
| `src/ui/character_creation/character_creation_screen.gd` | SceneManager migration |
| `data/schemas/building.schema.json` | Added `z_levels` property |
| `data/buildings/inn.json` | Added `"z_levels": [0, 1]` |
| `data/buildings/granary.json` | Added `"z_levels": [0, -1]` |
| `project.godot` | Registered `SceneManager` autoload |

---

## Exit Gate

Phase 3 is complete when all of the following hold:

- ✅ Player creates a character with background, traits, skills, and attributes
- ✅ Player enters a settlement tile map from the world view
- ✅ Player navigates the settlement cell-by-cell including z-levels
- ✅ Player applies for a labor slot and earns wages each strategic tick
- ✅ Hunger and fatigue decay over time; shelter accelerates fatigue recovery
- ✅ Player can rent a room at an inn or claim a derelict building as shelter
- ✅ NPCs are present in the settlement with schedules that change through the day
- ✅ Player can open a dialogue panel to ask about work, market goods, or settlement info
- ✅ All state saves and loads cleanly through `SaveManager`

---

## What Phase 4 Builds On

Phase 4 (Combat Vertical Slice) requires:

- `PersonState` — body state, attributes, equipment refs
- `WorldState.characters` — combatant registry
- `SettlementView` / future `LocalView` — tactical map surface
- `TickScheduler` — `HAZARD_RESOLUTION` and `COMBAT_RESOLUTION` phases (already defined, no hooks registered yet)
