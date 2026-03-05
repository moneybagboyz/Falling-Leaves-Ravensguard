# Implementation Guide — Economy, Buildings & Combat

This document is the single source of truth for implementing all designed systems.
Follow the sections in order — each section depends on the one before it.

---

## Table of Contents

1. [File Map — What Goes Where](#1-file-map)
2. [Constants to Verify in Globals.gd](#2-globalsgd-constants)
3. [Production System Rewrite](#3-production-system)
4. [Building Placer Overhaul](#4-building-placer)
5. [Civic Center System](#5-civic-center)
6. [Combat & Garrison System](#6-combat--garrison)
7. [Governor AI Hooks](#7-governor-ai)
8. [Known ID Mismatches to Fix](#8-id-mismatches)
9. [Pasture & Horse System](#9-pasture--horse-system)
10. [Implementation Checklist](#10-checklist)

---

## 1. File Map

| File (in repo) | Status | Action |
|---|---|---|
| `src/simulation/economy/production_ledger.gd` | Extend | Merge logic from `production_system_rewrite.gd` into `ProductionLedger` |
| `src/simulation/economy/settlement_pulse.gd` | Update | Add garrison tick, alehouse tick, civic tick calls |
| `src/worldgen/building_placer.gd` | Extend | Merge tile-driven extraction and workshop split logic from `building_placer_overhaul.gd` |
| `src/worldgen/region_generator.gd` | Update | Fix tier-0 artisan lockout; update pop tier ranges (Section 4e) |
| `src/worldgen/sub_region_generator.gd` | Update | Swap building scan to read `ss.buildings` (Section 4b) |
| `src/simulation/settlement/settlement_state.gd` | Update | Add `civic_center_tier`, `horse_accumulator`, `building_data`, `garrison_priority` fields (Sections 5, 9) |
| `src/simulation/economy/` *(new file)* | Create | `governor_ai.gd` — signal table + retool hooks (Section 7) |
| `data/buildings/*.json` | Update | Add building definitions from Section 3 & 5 |
| `data/recipes/*.json` | Update | Add all processing recipes from Section 3d |

**Important — not drop-in replacements:**
The two files in the Review folder (`production_system_rewrite.gd`, `building_placer_overhaul.gd`)
are **design references**, not literal file replacements. The actual target files already exist
with a different class architecture. Merge the logic rather than overwriting.

---

## 2. Constants to Verify

There is no single `Globals.gd` in the repo — constants are embedded in individual files.
The values below are what the production math was designed around.
Before touching production output, verify these against the live files:

| Constant | Designed Value | Where to check / set |
|---|---|---|
| Acres worked per laborer | 10 | `production_ledger.gd` labour_factor logic |
| Bushels per acre base | 12.0 | `data/recipes/farm_grain.json` → `base_yield_per_acre_per_tick` |
| Seed ratio (keep as seed) | 20% | same recipe |
| Days per year | 360 | `src/core/time/` tick scheduler |
| Forestry yield (wood per worker-day) | 100.0 | `data/recipes/log_timber.json` → `yield_per_worker_day` |
| Hunting yield (meat per worker-day) | 2.5 | relevant recipe |
| Burgher target % of population | 10% | `region_generator.gd` population split |

**Critical:** `production_ledger.gd` currently uses `wheat_bushel` (not `grain`)
and `timber_log` (not `wood`) as good IDs. All recipe references in Sections 3–9
use the designed IDs — **resolve good IDs before implementing** (see Section 8b).

---

## 3. Production System

### 3a. Architecture Overview

Two separate production modes. Do not mix them.

**EXTRACTION** — acre-based, building is a labor cap, not the producer.
- Formula: `output = workers_assigned × ACRES_WORKED × yield_per_acre / DAYS_PER_YEAR`
- Buildings set the ceiling of how many workers can work the land.
- If no buildings are placed, a survival fallback runs at 15% efficiency.
- Subsistence priority: food workers (farm/fish) fill before any other extraction type.

**PROCESSING** — recipe-based, strict gate on inputs AND workers.
- Formula: `cycles = min(workers / workers_per_cycle, input_stock / input_qty)`
- If any input is missing, output is zero. No phantom production.
- Supply chain starvation propagates cleanly upstream.

**Labor pools** — hard-separated:
- Peasants → extraction buildings only
- Burghers (artisans) → processing buildings only

### 3b. How to Integrate

**Do not overwrite** `production_ledger.gd`. The existing class (`ProductionLedger`) already
has the correct 3-pathway architecture (agriculture / extraction / standard recipes).
Integrate the design rewrite by extending those three pathways:

- `_run_agriculture()` — already handles `farm_grain` / `wheat_bushel`. Add ore and livestock yields.
- `_run_extraction()` — already handles `log_timber`. Expand to handle all extraction building types.
- `_run_standard_recipes()` — already reads recipe from ContentRegistry. Ensure all processing recipes (Section 3d) are in `data/recipes/` as JSON.

The main entry point is `ProductionLedger.run(ss, ws, delta_ticks)` — already called
by `SettlementPulse._tick_one()`. Do not change that call signature.

**Good ID alignment required first —** the live system uses `wheat_bushel` / `timber_log`.
Decide whether to rename the live IDs to match the design (`grain` / `wood`), or update
the design recipes to use the live IDs. Do this in ONE place before writing any recipe JSON.

### 3c. Extraction Building Worker Slots

These constants are defined in `production_system_rewrite.gd` as `EXTRACTION_WORKERS_PER_BUILDING`:

| Building | Workers per placed building |
|---|---|
| `farm_plot` | 5 |
| `fishery` | 4 |
| `ore_mine` | 4 |
| `lumber_camp` | 3 |
| `charcoal_camp` | 3 |
| `clay_pit` | 3 |
| `pasture` | 4 |

### 3d. Processing Recipes

All recipes are in `PROCESSING_RECIPES` in `production_system_rewrite.gd`.
Canonical list — do not duplicate these in JSON. The GDScript dict is the single source.

| Building | Workers/cycle | Inputs | Outputs |
|---|---|---|---|
| `grain_mill` | 2 | grain×2 | flour×1 |
| `brewery` | 2 | grain×2 | ale×1 |
| `weaver` | 2 | wool×2 | cloth×1 |
| `tailor` | 4 | cloth×2 | fine_garments×1 |
| `tannery` | 3 | hides×2 | leather×1 |
| `smelter` | 4 | iron×3 + coal×2 | steel×2 |
| `smithy` | 3 | *priority — see below* | *priority — see below* |
| `bronzesmith` | 2 | copper×2 + tin×1 | bronze×2 |
| `toolmaker` | 4 | iron×1 + wood×2 | tools×2 |
| `goldsmith` | 8 | gold×1 + coal×1 | jewelry×1 |
| `brickmaker` | 3 | clay×3 + coal×1 | bricks×2 |

**Smithy priority recipe** (tries variants in order, first satisfiable wins):
1. `steel×2 + coal×1` → `steel_sword×1` *(smelter required upstream)*
2. `iron×2 + coal×1` → `iron_sword×1` *(no smelter needed — T0/T1 viable)*
3. `iron×2` → `tools×1` *(coal unavailable fallback)*

The smithy does NOT require a smelter for iron weapons. Smelter is the steel upgrade path only.

**Garrison-mode gear recipes** — when garrison priority mode is active, smithy/bronzesmith/tannery/weaver override their normal output and produce gear instead. These are the same recipes listed in Section 6d. The table above covers peacetime production. In garrison mode:

| Building | Garrison output (examples) |
|---|---|
| `smithy` | `iron_mail`, `steel_plate`, `iron_helm`, `steel_helm`, `iron_axe`, `iron_spear`, `iron_greaves`, `iron_gauntlets` |
| `bronzesmith` | `bronze_mail`, `bronze_helm`, `bronze_sword`, `bronze_axe`, `bronze_spear` |
| `tannery` | `leather_armor`, `leather_cap`, `leather_boots` |
| `weaver` | `gambeson` (cloth×2 → gambeson×1) |

Garrison mode is triggered when `stockpile < target × garrison_size` (Section 6e). The governor sets the priority flag; processing logic checks it before selecting the recipe.

### 3e. Key Functions in production_system_rewrite.gd

| Function | Purpose |
|---|---|
| `run_production_tick(s_data)` | Main entry — call once per game day |
| `_process_extraction(s_data, laborer_pool)` | Runs acre formula for all extraction buildings |
| `_process_processing(s_data, burgher_pool)` | Distributes burghers, runs all processing recipes |
| `_process_standard_recipe(...)` | Handles single-input-set recipes |
| `_process_priority_recipe(...)` | Handles smithy-style priority variants |
| `recalculate_production(s_data, grid, resources, geology)` | Call on terrain change, not every tick |
| `is_processing_idle(s_data, bid)` | Returns true if a processing building has no valid inputs |

### 3f. SettlementState Fields

`SettlementState` (`src/simulation/settlement/settlement_state.gd`) currently exposes:

```gdscript
# Existing fields (confirmed in repo)
ss.buildings         : Array      # Array of building instance IDs (strings)
                                  # NOT a count dict — one entry per placed building
ss.inventory         : Dictionary  # { good_id: float }
ss.population        : Dictionary  # { "peasant": int, "artisan": int, "merchant": int, "noble": int }
ss.prosperity        : float       # 0.0–1.0
ss.unrest            : float       # 0.0–1.0
ss.acreage           : Dictionary  # { "total_acres", "arable_acres", "worked_acres",
                                  #   "fallow_acres", "pasture_acres", "woodlot_acres" }
ss.shortages         : Dictionary  # { good_id: float } — reset each pulse
ss.labor_slots       : Array       # per-slot dicts built by BuildingPlacer
ss.territory_cell_ids: Array[String]
ss.tier              : int         # 0–4

# Fields to ADD (for systems in this guide)
ss.civic_center_tier            : int = 0
ss.civic_center_id              : String = "hut"
ss.civic_under_construction     : bool = false
ss.civic_construction_days_remaining : int = 0
ss.horse_accumulator            : float = 0.0
ss.building_data                : Dictionary = {}  # per-type metadata e.g. {"farm_plot": {"draft_horses": 2}}
ss.garrison_priority            : bool = false
```

**Labor pool access —** there are no `laborers` / `burghers` top-level fields.
Get peasant and artisan counts from `ss.population`:
```gdscript
var laborer_pool: int = ss.population.get("peasant", 0)
var burgher_pool: int = ss.population.get("artisan", 0)
```

**`add_inventory()` does not exist** on SettlementState. Use direct dict mutation:
```gdscript
ss.inventory[good] = ss.inventory.get(good, 0.0) + qty
```

---

## 4. Building Placer

### 4a. Architecture Overview

The live `building_placer.gd` (`src/worldgen/building_placer.gd`) uses a **fixed
`TIER_DISTRIBUTION` table** — a hardcoded count of each building type per tier — not
the tile-driven formula from the design reference file. The current distribution is:

| Tier | Key buildings placed |
|---|---|
| 0 hamlet | inn×1, well×1, farm_plot×2, lumber_camp×1 |
| 1 village | + granary×1, smithy×1, grain_mill×1 |
| 2 town | + market×1, market_stall×2, smithy×2 |
| 3 city | + market×2, iron_mine×1, smithy×3 |
| 4 metropolis | + iron_smelter×1, smithy×5 |

Houses are added separately based on `ceil(total_pop / housing_capacity)`.
The remainder cells are filled with `open_land`.

**To implement the tile-driven extraction model** from the design, you have two paths:
- **Path A (recommended):** Keep the TIER_DISTRIBUTION table but add ore/forest/water tile
  checks per cell before stamping extraction buildings. If the cell lacks the right resource tag,
  replace `iron_mine` with `open_land`.
- **Path B:** Replace the distribution table with the tile-count formula from
  `building_placer_overhaul.gd`. More accurate but requires more refactoring.

`ss.buildings` is populated as an **Array of building ID strings** (one per territory cell).
`production_ledger._run_standard_recipes()` already iterates this correctly.

### 4b. Required Change in sub_region_generator.gd

If `src/worldgen/sub_region_generator.gd` scans `world_tiles` for `building_id` to count
buildings, it may undercount. Verify whether it reads `ss.buildings` or scans tiles —
if the latter, swap to reading `ss.buildings` directly:

```gdscript
# If sub_region_generator.gd does this (tile scan — replace it):
for cid in ss.territory_cell_ids:
    var bid = world_state.world_tiles.get(cid, {}).get("building_id", "")

# Replace with (authoritative array):
for bid in ss.buildings:
    var bdef = ContentRegistry.get_content("building", bid)
    # ... layout logic using bdef
```

### 4c. Key Constants in building_placer.gd

The live file uses `TIER_RADIUS` (confirmed matching our design) and `TIER_DISTRIBUTION`
(fixed counts). The tile-density constants from the design reference are targets for
when you refactor to tile-driven placement.

**Target tiles per extraction building** (for tile-driven refactor):

| Building | Tiles per building |
|---|---|
| `farm_plot` | 2 fertile tiles |
| `iron_mine` / `ore_mine` | 3 ore-tagged tiles |
| `lumber_camp` | 4 forest tiles |
| `charcoal_camp` | 6 forest tiles |
| `clay_pit` | 2 wetland tiles |
| `fishery` | 2 water-adjacent tiles |
| `pasture` | 3 grassland tiles |

### 4d. Governor Workshop Retooling

The live codebase has no `retool_workshops()` function yet — GovernorAI doesn't exist.
When you create `governor_ai.gd`, the retool call should modify the settlement's
`TIER_DISTRIBUTION`-derived building list at runtime by updating building types in
`ss.buildings` and `ss.labor_slots`. The design interface (from reference file) is:

```gdscript
# Proposed call (to implement in governor_ai.gd):
GovernorAI.retool_workshops(ss, {
    "grain_mill": 0.4,
    "smelter":    0.3,
    # remainder → smithy automatically
})
```

### 4e. Population Tier Ranges & Artisan Fix

The live `region_generator.gd` already has the correct tier ranges used by `SettlementPlacer`.
The **artisan lockout** is confirmed in the live code — tier-0 hamlets get:
`peasant 88% / merchant 10% / noble 2% / artisan 0%`, which blocks any processing buildings.

Fix in `region_generator.gd` by changing the tier-0 population block to include artisans:

```gdscript
# CURRENT (tier 0 only) — remove this block:
# ss.population["peasant"]  = int(total_pop * 0.88)
# ss.population["merchant"] = int(total_pop * 0.10)
# ss.population["noble"]    = maxi(int(total_pop * 0.02), 1)

# REPLACE with unified split for ALL tiers:
ss.population["peasant"]  = int(total_pop * 0.70)
ss.population["artisan"]  = int(total_pop * 0.15)
ss.population["merchant"] = int(total_pop * 0.10)
ss.population["noble"]    = maxi(int(total_pop * 0.05), 1)
```

Target population ranges by tier (for `SettlementPlacer` / `WorldGenParams`):

| Tier | Name | Pop Range |
|---|---|---|
| 0 | Hamlet | 80 – 200 |
| 1 | Village | 250 – 800 |
| 2 | Town | 900 – 1 800 |
| 3 | City | 2 000 – 5 000 |
| 4 | Metropolis | 6 000 – 15 000 |

---

## 5. Civic Center System

### 5a. Overview

- One civic center per settlement, never demolished, upgrades in place.
- Provides two passive bonuses: **happiness** and **tax rate**.
- No building unlocks are gated behind it. It is purely a passive bonus system.

### 5b. Tier Table

| Tier | Building ID | Happiness Bonus | Tax Rate Bonus | Upgrade Cost |
|:---:|---|:---:|:---:|---|
| 0 | `hut` | — | — | *(starting state)* |
| 1 | `longhouse` | +1 | +1% | wood×30 |
| 2 | `village_hall` | +2 | +2% | wood×80 + stone×20 |
| 3 | `town_hall` | +3 | +4% | wood×100 + stone×80 |
| 4 | `guildhall` | +4 | +6% | stone×150 + bricks×80 |
| 5 | `manor_house` | +5 | +8% | stone×200 + bricks×120 + iron×30 |
| 6 | `keep` | +6 | +10% | stone×300 + bricks×200 + iron×60 |
| 7 | `fortress` | +7 | +13% | stone×500 + bricks×300 + iron×100 + steel×20 |
| 8 | `castle` | +8 | +16% | stone×600 + bricks×400 + iron×150 + steel×50 |
| 9 | `palace` | +10 | +20% | stone×800 + marble×50 + steel×100 |

### 5c. settlement_state.gd Changes

Add to `src/simulation/settlement/settlement_state.gd` (and `to_dict()` / `from_dict()`):

```gdscript
var civic_center_tier: int = 0         # 0–9, matches tier table above
var civic_center_id: String = "hut"    # current building ID at this tier
var civic_under_construction: bool = false
var civic_construction_days_remaining: int = 0
```

### 5d. Applying the Bonus

Call this once per tick. Apply before other happiness/tax calculations so other
modifiers stack on top:

```gdscript
static func apply_civic_bonuses(ss: SettlementState) -> void:
    var HAPPINESS_BONUS := [0, 1, 2, 3, 4, 5, 6, 7, 8, 10]
    var TAX_BONUS_PCT   := [0, 1, 2, 4, 6, 8, 10, 13, 16, 20]
    var t := clampi(ss.civic_center_tier, 0, 9)
    if ss.civic_under_construction:
        return  # no bonus while upgrading
    ss.happiness    += HAPPINESS_BONUS[t]
    ss.tax_rate_pct += TAX_BONUS_PCT[t]
```

### 5e. Upgrade Logic

```gdscript
static func try_upgrade_civic_center(ss: SettlementState) -> bool:
    if ss.civic_under_construction:
        return false
    if ss.civic_center_tier >= 9:
        return false
    # No upgrade during siege or famine
    if ss.is_under_siege or ss.famine_active:
        return false

    var next_tier := ss.civic_center_tier + 1
    var cost := CIVIC_UPGRADE_COSTS[next_tier]   # dict of { resource: qty }

    # Check inventory
    for res in cost:
        if ss.inventory.get(res, 0) < cost[res]:
            return false

    # Consume materials and begin construction
    for res in cost:
        ss.inventory[res] -= cost[res]
    ss.civic_under_construction = true
    ss.civic_construction_days_remaining = CIVIC_CONSTRUCTION_DAYS  # e.g. 30
    return true
```

```gdscript
# In the daily tick:
static func tick_civic_construction(ss: SettlementState) -> void:
    if not ss.civic_under_construction:
        return
    ss.civic_construction_days_remaining -= 1
    if ss.civic_construction_days_remaining <= 0:
        ss.civic_center_tier += 1
        ss.civic_center_id    = CIVIC_TIER_IDS[ss.civic_center_tier]
        ss.civic_under_construction = false
```

```gdscript
# In siege resolution — downgrade civic center by 1 tier on settlement capture:
static func apply_siege_damage_to_civic(ss: SettlementState) -> void:
    if ss.civic_under_construction:
        # Abort the in-progress upgrade, refund nothing (materials consumed in siege)
        ss.civic_under_construction = false
        ss.civic_construction_days_remaining = 0
    if ss.civic_center_tier > 0:
        ss.civic_center_tier -= 1
        ss.civic_center_id = CIVIC_TIER_IDS[ss.civic_center_tier]
```

```gdscript
const CIVIC_TIER_IDS := [
    "hut", "longhouse", "village_hall", "town_hall",
    "guildhall", "manor_house", "keep", "fortress", "castle", "palace"
]

const CIVIC_UPGRADE_COSTS := {
    1: { "wood": 30 },
    2: { "wood": 80,  "stone": 20 },
    3: { "wood": 100, "stone": 80 },
    4: { "stone": 150, "bricks": 80 },
    5: { "stone": 200, "bricks": 120, "iron": 30 },
    6: { "stone": 300, "bricks": 200, "iron": 60 },
    7: { "stone": 500, "bricks": 300, "iron": 100, "steel": 20 },
    8: { "stone": 600, "bricks": 400, "iron": 150, "steel": 50 },
    9: { "stone": 800, "marble": 50,  "steel": 100 },
}

const CIVIC_CONSTRUCTION_DAYS := 30

```

---

## 6. Combat & Garrison System

### 6a. Body Coverage Slots

Every soldier tracks armor independently across these slots:

```
skull       — head (critical hit zone; stun/knockdown if unarmored)
face        — eyes/jaw (bleed, morale break if exposed)
neck        — artery zone (lethal on pierce if unarmored)
upper_body  — chest/shoulders (largest zone)
lower_body  — abdomen/hips
upper_arm   — L and R independently
lower_arm   — L and R independently (loss = weapon drop)
hand        — L and R independently
upper_leg   — L and R independently
lower_leg   — L and R independently (loss = falls/crawls)
foot        — L and R independently (loss = speed ×0.1)
```

### 6b. Armor Layer Stack

Slots support up to 3 layers. Strike resolution goes outer → inner:
a blow that penetrates layer 3 continues to layer 2, then layer 1, then flesh.

| Layer | Type | Material | Blocks |
|---|---|---|---|
| 1 | Base padding | `gambeson` (cloth) | Blunt only. **Required under mail.** |
| 2 | Medium armor | `leather_armor` | Slash reduction, low pierce resist |
| 2 | Medium armor | `bronze_mail` | Slash + pierce, heavy vs. blunt |
| 2 | Medium armor | `iron_mail` | Slash + pierce, better edge than bronze |
| 3 | Heavy armor | `steel_plate` | Best slash/pierce, weaker vs. blunt |

**Critical rule:** A soldier wearing iron_mail with NO gambeson underneath takes full
blunt damage through the mail. Gambeson is not optional — it must be produced and equipped.

### 6c. Damage Type vs. Armor

| Damage Type | Blocked By | Passes Through |
|---|---|---|
| Slash | leather > bronze_mail > iron_mail > steel_plate | gambeson |
| Pierce | iron_mail > bronze_mail > steel_plate | leather (partial) |
| Blunt | gambeson (partial) + steel_plate (partial) | mail (fully) |
| Cut/Bite | leather ≥ bronze_mail | iron_mail |

Material hardness order: `cloth < leather < bronze < iron < steel`

### 6d. Typed Gear — Full Item List

**Weapons** (all from smithy or bronzesmith):

| Item ID | Material | Damage Type | Recipe | Building |
|---|---|---|---|---|
| `iron_sword` | iron | slash | iron×2 + coal | smithy |
| `iron_axe` | iron | slash/chop | iron×2 + coal | smithy |
| `iron_spear` | iron | pierce | iron×2 + coal | smithy |
| `bronze_sword` | bronze | slash | bronze×1 | bronzesmith |
| `bronze_axe` | bronze | slash/chop | bronze×1 | bronzesmith |
| `bronze_spear` | bronze | pierce | bronze×1 | bronzesmith |
| `steel_sword` | steel | slash | steel×1 + coal | smithy |
| `steel_axe` | steel | slash/chop | steel×1 + coal | smithy |

**Armor — Torso:**

| Item ID | Layer | Slots Covered | Recipe | Building |
|---|---|---|---|---|
| `gambeson` | 1 | upper_body + lower_body + upper_arm | cloth×2 | weaver |
| `leather_armor` | 2 | upper_body | leather×2 | tannery |
| `bronze_mail` | 2 | upper_body + upper_arm + lower_arm | bronze×2 | bronzesmith |
| `iron_mail` | 2 | upper_body + upper_arm + lower_arm | iron×2 + coal | smithy |
| `steel_plate` | 3 | upper_body + lower_body | steel×2 + coal | smithy |

**Armor — Head:**

| Item ID | Layer | Recipe | Building |
|---|---|---|---|
| `leather_cap` | 1 | leather×1 | tannery |
| `bronze_helm` | 1 | bronze×1 | bronzesmith |
| `iron_helm` | 1 | iron×2 + coal | smithy |
| `steel_helm` | 2 | steel×1 + coal | smithy |

**Armor — Limbs:**

| Item ID | Layer | Slots | Recipe | Building |
|---|---|---|---|---|
| `leather_boots` | 1 | foot L+R | leather×1 | tannery |
| `iron_greaves` | 1 | lower_leg L+R | iron×2 + coal | smithy |
| `iron_gauntlets` | 1 | hand L+R | iron×2 + coal | smithy |

### 6e. Garrison Stockpile Targets

The garrison posts a target load per soldier tier. When `stockpile < target × garrison_size`,
smithy/bronzesmith/weaver/tannery switch to garrison priority mode.

```
Light:     gambeson + leather_armor + leather_cap + iron_sword
Standard:  gambeson + iron_mail + iron_helm + iron_greaves + iron_sword
Heavy:     gambeson + iron_mail + steel_plate + steel_helm + steel_sword
Elite:     gambeson + iron_mail + steel_plate + steel_helm
           + iron_gauntlets + iron_greaves + steel_sword
```

### 6f. Gear Degradation & Repair

- Items degrade on wound. A pierced `iron_mail` gains a `damaged` flag.
- `damaged` iron_mail counts as leather_armor until repaired.
- Repair: 2 iron + 1 coal at smithy.
- Masterwork items never degrade and are only lost in combat (not consumed).

### 6g. Artisan Quality Tiers

Quality is set at craft time by artisan skill level (0–100). Not upgradeable after.

| Quality | Combat Effect | Trade Value |
|---|---|---|
| average | Base stats | Base price |
| fine | +10% block threshold | ×1.5 |
| exceptional | +20% block, +5% durability | ×2.5 |
| masterwork | +35% block, +15% durability — never degrades | ×5 — luxury noble trade |

---

## 7. Governor AI

**GovernorAI does not exist yet in the repo.** Create `src/simulation/economy/governor_ai.gd`
(or `src/simulation/settlement/governor_ai.gd`) as a new `RefCounted` class.
Wire it into `SettlementPulse._tick_one()` with a weekly cadence check.

### 7a. Workshop Retooling

Governor calls `BuildingPlacer.retool_workshops(ss, split)` to respond to economy signals:

```gdscript
# Food shortage — shift toward grain_mill
BuildingPlacer.retool_workshops(ss, { "grain_mill": 0.6 })

# Iron/weapons demand — more smithy
BuildingPlacer.retool_workshops(ss, { "grain_mill": 0.3, "smithy": 0.7 })

# Ore in territory, steel demand — add smelter
BuildingPlacer.retool_workshops(ss, { "grain_mill": 0.3, "smelter": 0.3 })
# remainder automatically becomes smithy
```

### 7b. Priority Signal Table

| Signal | Meaning | Governor Response |
|---|---|---|
| Grain price rising | Food shortage or mill underweight | Increase `grain_mill` share first; each mill doubles food output |
| Only reduce `brewery` | if ale is also surplus | — |
| Ale price rising | Merchant income opportunity | Increase `brewery` from grain surplus |
| Steel / tools price rising | Military or construction demand | Increase `smithy` share |
| Steel / iron shortage | Mine underweight or no coal | Expand `ore_mine`; check `charcoal_camp` coal supply |
| Fine_garments price rising | Noble demand unmet | Increase `weaver` + `tailor` share |
| Jewelry price rising | Noble unrest risk | Increase `goldsmith` (needs gold ore + coal) |
| Migration stalling | No hospitality pull | Build `alehouse` (ale req) or `inn` (ale + food req) |
| Construction stalled | Stone/brick shortage | Expand `ore_mine` or `brickmaker` |
| Happiness stagnant | Civic tier too low | Queue civic center upgrade |
| Tax income low | Civic tier below potential | Upgrade civic center (highest tax ROI) |
| Siege ended, civic downgraded | Conquest damage | Civic rebuild before garrison restock |

### 7c. Civic Center Auto-Upgrade

Governor should check `try_upgrade_civic_center(ss)` once per in-game week.
Do not call it every tick — it's a strategic decision, not daily maintenance.

---

## 8. ID Mismatches & Missing Definitions

### 8a. Building ID Mismatches

The design docs use certain building IDs. The live repo uses different ones in some cases.
Resolve in ONE place — either rename `TIER_DISTRIBUTION` entries in `building_placer.gd`,
or update recipe/section references to match the live IDs.

| Design ID | Live repo ID (confirmed) | Where it appears |
|---|---|---|
| `ore_mine` | `iron_mine` | `building_placer.gd` TIER_DISTRIBUTION tier 3+ |
| `smelter` | `iron_smelter` | `building_placer.gd` TIER_DISTRIBUTION tier 4 |
| `farm_plot` | `farm_plot` | ✅ already matches |
| `smithy` | `smithy` | ✅ already matches |
| `grain_mill` | `grain_mill` | ✅ already matches |
| `lumber_camp` | `lumber_camp` | ✅ already matches |

**Recommendation:** rename `iron_mine` → `ore_mine` and `iron_smelter` → `smelter`
in `building_placer.gd` and any corresponding `data/buildings/` JSON files.
The design IDs are cleaner and more extensible (copper/tin mines use the same building).

### 8b. Good (Resource) ID Mismatches

The live production system uses different good IDs than the design docs:

| Design ID | Live repo ID (confirmed) | Where it appears |
|---|---|---|
| `grain` | `wheat_bushel` | `production_ledger.gd`, `settlement_pulse.gd`, starter stock |
| `wood` | `timber_log` | `production_ledger.gd`, starter stock |
| All other goods | *unconfirmed — check `data/goods/`* | — |

**Recommendation:** Standardise on the design IDs (`grain`, `wood`) by renaming
`wheat_bushel` → `grain` and `timber_log` → `wood` across the codebase before
writing any new recipe JSON. Otherwise every new recipe will be inconsistent with
the existing agriculture and extraction outputs that feed consumption.

### 8b. Marble — Production Source

`marble` is required for the tier-9 palace upgrade (stone×800 + marble×50 + steel×100).
It is produced by `ore_mine` — the tile tag `"marble"` is already in `EXTRACTION_TILE_TAGS`
for `ore_mine` (alongside iron, copper, tin, etc.). No separate quarry building needed.
Settlements without marble-tagged tiles will never produce it, so tier 9 is geography-gated
by design — only settlements near marble outcroppings can reach palace.

If marble tiles do not exist in your world generator yet, add `"marble"` as a tile tag
that can appear on mountainous terrain during geology pass.

### 8c. Alehouse — Missing Building Definition

The governor signal table references `alehouse` as a migration pull building.
It is NOT a processing building (no input recipe). It is a service building that requires
`ale` in inventory to stay active. Add to `content/buildings/`:

```gdscript
# Alehouse — service building, requires ale stock to operate
# Workers: 2 burghers (service staff, not artisans — may need a separate SERVICE_BUILDINGS list)
# Consumption per day: 1 ale
# Effect when stocked: +1 happiness, counts as hospitality for migration pull
# Effect when unstocked (ale=0): closed, no happiness, no migration bonus

# In daily tick:
static func tick_alehouse(ss: SettlementState) -> void:
    var count := ss.buildings.get("alehouse", 0)
    if count == 0:
        return
    var ale_needed := count  # 1 ale per alehouse per day
    if ss.inventory.get("ale", 0) >= ale_needed:
        ss.inventory["ale"] -= ale_needed
        ss.happiness += count            # +1 happiness per active alehouse
        ss.migration_pull += count * 2   # adjust multiplier to taste
    # else: no ale → alehouse is closed, no bonus applied
```

`inn` (also referenced in the governor table) is the higher-tier version: requires both
`ale` AND `food` per day, provides stronger migration pull. Define identically but with
`food` as a second consumption and a larger `migration_pull` bonus.

---

## 9. Pasture & Horse System

### 9a. Pasture as Placed Building

Pasture changes from the acreage model (`ss.acreage["pasture_acres"]`) to a tile-driven
placed building, consistent with `farm_plot`. Changes required in the actual repo files:

```gdscript
# In building_placer.gd — add pasture to TIER_DISTRIBUTION for tiers with grassland:
# (conditionally, when settlement has grassland-tagged territory cells)
# tier 0: "pasture": 1 if grassland cells present
# tier 1+: "pasture": 2+

# In production_ledger.gd — add pasture handling to _run_extraction():
# Check ss.buildings for "pasture", then call _run_pasture(ss, count, delta_ticks)
```

No `EXTRACTION_WORKERS_PER_BUILDING` or `EXTRACTION_TILE_TAGS` constants exist in the
live `production_ledger.gd` — the design file has these as reference values.
In the live codebase, building counts come from `ss.buildings.count("pasture")`
and tile tags come from the territory cell `resource_tags` in `world_state.world_tiles`.

### 9b. Pasture Output Formula

Wool, hides, and meat use the standard extraction formula with their own yield constants.
Horses use a slow-accumulation formula:

```gdscript
# In production_ledger.gd — add inside _run_extraction() or as its own helper:
const PASTURE_WOOL_YIELD:   float = 8.0
const PASTURE_HIDE_YIELD:   float = 4.0
const PASTURE_MEAT_YIELD:   float = 3.0
const PASTURE_HORSE_YIELD:  float = 0.033  # ~1 horse per 30 days per building at full staff

static func _run_pasture(ss: SettlementState, building_count: int, delta_ticks: int) -> void:
    var labour_mult: float = _labour_factor(ss)
    var base := float(building_count) * labour_mult * float(delta_ticks) / 360.0
    ss.inventory["wool"]  = ss.inventory.get("wool",  0.0) + base * PASTURE_WOOL_YIELD
    ss.inventory["hides"] = ss.inventory.get("hides", 0.0) + base * PASTURE_HIDE_YIELD
    ss.inventory["meat"]  = ss.inventory.get("meat",  0.0) + base * PASTURE_MEAT_YIELD
    # Horses use fractional accumulation to avoid never producing at low building counts
    ss.horse_accumulator += base * PASTURE_HORSE_YIELD
    while ss.horse_accumulator >= 1.0:
        ss.inventory["horse"] = ss.inventory.get("horse", 0.0) + 1.0
        ss.horse_accumulator -= 1.0
```

Add `horse_accumulator: float = 0.0` to `settlement_state.gd`.

### 9c. Horse Use — Cavalry

Add `horse` to the garrison stockpile model. Cavalry units require one horse in stockpile:

```gdscript
# In garrison target definition:
"cavalry_light":  { "horse": 1, "iron_sword": 1, "leather_armor": 1 }
"cavalry_heavy":  { "horse": 1, "iron_sword": 1, "iron_mail": 1, "gambeson": 1 }
```

On cavalryman death: consume horse from garrison stockpile (same as consuming iron_mail on a soldier death).

Mounted unit combat bonuses:
- Movement speed: `×1.5`
- First melee contact charge bonus: `+30% damage`
- Penalty: horses can be targeted separately (aimed shot at mount = dismount)

### 9d. Horse Use — Draft Animals

`ss.buildings` is a count dict (`{building_id: int}`) — it cannot store per-instance data.
Use `ss.building_data` (the per-type metadata dict defined in Section 3f) to store draft horses:

```gdscript
static func assign_draft_horses(ss: SettlementState, count: int) -> void:
    var available := ss.inventory.get("horse", 0)
    var actual    := mini(count, available)
    if not ss.building_data.has("farm_plot"):
        ss.building_data["farm_plot"] = {}
    ss.building_data["farm_plot"]["draft_horses"] = actual
    ss.inventory["horse"] = available - actual
```

In the extraction tick, read draft horses from `building_data` and apply a flat bonus
to ALL farm_plots (bonus averages across the type, not per individual building):
```gdscript
var draft_horses := ss.building_data.get("farm_plot", {}).get("draft_horses", 0)
var draft_bonus: float = 1.0 + 0.20 * float(draft_horses)
var output := int(workers * acres * yield_per_acre / DAYS_PER_YEAR * draft_bonus)
```

Draft horse daily feeding cost (subtracted each tick from inventory):
```gdscript
var total_draft := ss.building_data.get("farm_plot", {}).get("draft_horses", 0)
var grain_consumed := total_draft  # 1 grain per draft horse per day
ss.inventory["grain"] = maxi(0, ss.inventory.get("grain", 0) - grain_consumed)
```

### 9e. Horse Use — Caravan Transport

At caravan departure, check horse inventory:
```gdscript
func get_caravan_speed_mult(ss: SettlementState) -> float:
    if ss.inventory.get("horse", 0) > 0:
        return 1.5
    return 1.0
```

Horses are not consumed. The check is purely a presence test at departure time.

### 9f. Fields to Add to settlement_state.gd

```gdscript
# In src/simulation/settlement/settlement_state.gd — add these vars:
var horse_accumulator: float = 0.0   # fractional horse production accumulator
var building_data: Dictionary = {}   # per-type metadata: { "farm_plot": { "draft_horses": 2 } }
var garrison_priority: bool = false  # when true, workshops produce gear instead of normal output

# Also add all four civic center fields from Section 5c.
# Also update to_dict() and from_dict() to include all new fields.
```

---

## 10. Checklist

Work through these in order. Each group can be done independently after the previous group is done.

### Group 1 — Foundation (do first, everything else depends on this)
- [ ] Confirm building ID situation (Section 8a): rename `iron_mine`→`ore_mine` and `iron_smelter`→`smelter` in `building_placer.gd` and `data/buildings/`
- [ ] Confirm good ID situation (Section 8b): decide on `grain` vs `wheat_bushel` and `wood` vs `timber_log`; rename across codebase in one pass
- [ ] Confirm `settlement_state.gd` exposes fields in Section 3f; add missing fields (`building_data`, `garrison_priority`, `horse_accumulator`, civic fields)
- [ ] Check whether `marble` tile tag exists in geology generator; add if missing (Section 8c)

### Group 2 — Production System
- [ ] Add all processing recipe JSON files to `data/recipes/` (Section 3d) using `recipe_type: "standard"` and correct input/output good IDs
- [ ] Verify `production_ledger._run_standard_recipes()` handles all recipe types correctly (it's already wired in `settlement_pulse`)
- [ ] Extend `production_ledger._run_extraction()` to cover fishery, ore_mine, clay_pit, charcoal_camp (currently only handles `log_timber`)
- [ ] Add garrison-mode recipe dispatch: check `ss.garrison_priority` flag in standard recipe loop; swap to gear recipe when true (Section 3d)
- [ ] Add all gear building definitions to `data/buildings/` with `production.recipe` pointing to gear recipes
- [ ] Smoke test: run one pulse on a hamlet, verify `wheat_bushel` / `timber_log` are produced
- [ ] Smoke test: place a smithy (ss.buildings includes `"smithy"`), add iron to inventory, run pulse, verify iron_sword added
- [ ] Smoke test: remove iron from inventory, verify smithy produces nothing (no phantom output)
- [ ] Smoke test: set `ss.garrison_priority = true`, verify smithy produces `iron_mail` instead

### Group 3 — Building Placer
- [ ] Fix tier-0 artisan lockout in `region_generator.gd` (Section 4e) — add `artisan: 15%` to tier-0 pop split
- [ ] Update `building_placer.gd` TIER_DISTRIBUTION tiers 1–4 to use resolved building IDs (after Group 1)
- [ ] Add `pasture` to TIER_DISTRIBUTION for grassland settlements (when grassland tiles are present)
- [ ] Verify `sub_region_generator.gd` reads from `ss.buildings` not tile scan (Section 4b)
- [ ] Smoke test: generate a hamlet, verify it has artisan population and smithy/grain_mill in buildings
- [ ] Smoke test: generate a city on ore terrain, verify `ore_mine` appears in buildings
- [ ] Smoke test: generate a grassland hamlet, verify `pasture` appears in buildings

### Group 4 — Civic Center
- [ ] Add `civic_center_tier`, `civic_center_id`, `civic_under_construction`, `civic_construction_days_remaining` to `settlement_state.gd` (and `to_dict` / `from_dict`)
- [ ] Initialize all new settlements to `civic_center_tier = 0`, `civic_center_id = "hut"`
- [ ] Implement `apply_civic_bonuses()` and call it from `settlement_pulse._tick_one()` before consumption
- [ ] Implement `try_upgrade_civic_center()` and call it from GovernorAI weekly check
- [ ] Implement `tick_civic_construction()` and call it from `settlement_pulse._tick_one()` daily
- [ ] Implement `apply_siege_damage_to_civic()` and call it in siege resolution
- [ ] Smoke test: upgrade a settlement from hut to longhouse, verify +1 happiness and +1% tax

### Group 5 — Combat & Garrison
- [ ] Add slot tracking to soldier data: `skull`, `face`, `neck`, `upper_body`, `lower_body`, `upper_arm`, `lower_arm`, `hand`, `upper_leg`, `lower_leg`, `foot`
- [ ] Implement 3-layer armor resolution per slot
- [ ] Add damage type → armor penetration logic (Section 6c)
- [ ] Add gambeson requirement check (no blunt protection without gambeson even if mail is worn)
- [ ] Implement garrison stockpile targets (Section 6e)
- [ ] Implement garrison priority mode: set `ss.garrison_priority = true` when stockpile deficit detected; smithy/bronzesmith/tannery/weaver check this flag each tick (Section 3d)
- [ ] Define alehouse and inn `tick_*` functions, add to daily tick (Section 8c)
- [ ] Implement gear degradation on wound (`damaged` flag, repair recipe)
- [ ] Add artisan quality tiers to craft output (Section 6g)

### Group 6 — Governor AI
- [ ] Create `governor_ai.gd` (Section 7 \u2014 file does not exist yet in repo)
- [ ] Wire `GovernorAI.tick_weekly(ss)` into `settlement_pulse._tick_one()` on a weekly cadence
- [ ] Connect signal table (Section 7b) to workshop retool calls modifying `ss.buildings`
- [ ] Add weekly civic center upgrade check (Section 7c)

### Group 7 — Pasture & Horses
- [ ] Add `"pasture"` to `EXTRACTION_BUILDINGS`, `EXTRACTION_WORKERS_PER_BUILDING`, `EXTRACTION_TILE_TAGS`, and `EXTRACTION_TILES_PER_BUILDING` (Section 9a)
- [ ] Implement `_process_pasture()` with fractional horse accumulator (Section 9b)
- [ ] Add `horse_accumulator: float` to `settlement_state.gd` (Section 9f)
- [ ] Add `horse` to garrison stockpile model with cavalry unit definitions (Section 9c)
- [ ] Implement mounted unit combat bonuses (speed ×1.5, charge +30%) (Section 9c)
- [ ] Implement `assign_draft_horses()` and draft yield bonus in extraction tick (Section 9d)
- [ ] Implement draft horse grain feeding cost per daily tick (Section 9d)
- [ ] Implement `get_caravan_speed_mult()` horse check at caravan departure (Section 9e)
- [ ] Smoke test: generate a grassland settlement, verify pasture appears and produces wool + hides + meat
- [ ] Smoke test: run 30+ days, verify horses slowly accumulate
- [ ] Smoke test: assign a draft horse to a farm_plot, verify +20% yield and grain drain

---

*Source files: `production_system_rewrite.gd` (design ref), `building_placer_overhaul.gd` (design ref), `supply-chains-full.md`*
*Repo paths confirmed against: `src/simulation/economy/`, `src/simulation/settlement/`, `src/worldgen/`, `data/`*
*Last updated: March 2026*
