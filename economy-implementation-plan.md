# Economy Overhaul — Implementation Plan

> **Purpose:** Step-by-step instructions for implementing `economy-simulation-design.md` into the live codebase.  
> **Reference doc:** `economy-simulation-design.md` (all section references are to that file).  
> **Executor:** GitHub Copilot agent — read this file top-to-bottom and execute each phase in order.  
> **Do NOT** skip phases or reorder them. Each phase produces outputs that later phases depend on.

---

## Current State Snapshot

| Subsystem | File | Status |
|-----------|------|--------|
| Settlement tick loop | `src/simulation/economy/settlement_pulse.gd` | ✅ Steps 1–7 wired, Steps 0/3d/10/11 missing |
| Production (farm + timber only) | `src/simulation/economy/production_ledger.gd` | ⚠️ Two recipes; no building-gated production; no labour split |
| Price signals | `src/simulation/economy/price_ledger.gd` | ⚠️ Basic supply/demand; no seasonal, no route bleed |
| Trade spawning | `src/simulation/economy/trade_party_spawner.gd` | ⚠️ Surplus→shortage wired; no profit margin, no transport cost |
| Settlement state | `src/simulation/settlement/settlement_state.gd` | ⚠️ Missing `build_demand`, `tool_stocks`, `seasonal_reserve` |
| Building schema | `data/schemas/building.schema.json` | ⚠️ No `milestones`, no `base_cost` / `base_labor`, no `hard_tier_min` |
| Buildings data | `data/buildings/*.json` | ⚠️ 16 files; none have `milestones` dict yet |
| Recipes data | `data/recipes/*.json` | ⚠️ 6 files (farm_grain, log_timber, mill_grain, mine_iron, smelt_iron, forge_tools); full chain missing |
| Governor AI | (none in new sim) | ❌ Entirely absent — lives only in old `SettlementManager.gd` |

---

## Phase 1 — Schema & Data Foundation

**Goal:** All data files match the design doc structures before any code touches them.  
**Test after:** Run `tests/test_data_loading.gd` — all assertions must pass.

### 1-A  Update `data/schemas/building.schema.json`

Replace the current flat schema with the milestone-based structure defined in §8.4.

**Changes:**
- Remove `tier_min` field.
- Add `base_cost` (integer, required).
- Add `base_labor` (integer, required).
- Add `max_level` (integer, 1–10, required).
- Add `hard_tier_min` (integer, optional, 0–4) — only for the 6 hard-gated buildings.
- Add `milestones` (object, required for levelled buildings): keys are level strings `"1"`, `"3"`, etc.; each value has:
  - `name` (string)
  - `flavor` (string)
  - `max_workers` (integer)
  - `output_multiplier` (float)
  - `recipes` (array of recipe ID strings, may be empty)
  - `adjacency_bonus_tags` (array of strings, optional)
- Mark `footprint_cells` optional (default 1) — some small buildings don't need it.
- Keep `construction_cost`, `upkeep_per_season`, `housing_capacity`, `tags` as-is.

### 1-B  Update all 16 `data/buildings/*.json` files

For each file apply the following pattern. Use §8.4 and §13 of the design doc as the authoritative source for `base_cost`, `base_labor`, `max_level`, and milestone entries.

**Levelled buildings** (all except the 6 hard-gated ones):

```json
{
  "$schema": "../schemas/building.schema.json",
  "id": "smithy",
  "name": "Smithy",
  "category": "production",
  "base_cost": 400,
  "base_labor": 400,
  "max_level": 10,
  "description": "Metal forging and weapons manufacture. Output scales with level.",
  "milestones": {
    "1": {
      "name": "Village Forge",
      "flavor": "A simple hearth and anvil, enough for basic ironwork.",
      "max_workers": 1,
      "output_multiplier": 0.4,
      "recipes": ["forge_tools", "forge_spear"]
    },
    "3": {
      "name": "Iron Workshop",
      "flavor": "A proper chimney, water-cooled trough, and room for an apprentice.",
      "max_workers": 2,
      "output_multiplier": 0.7,
      "recipes": ["forge_sword", "forge_shield"]
    },
    "5": {
      "name": "Master Forge",
      "flavor": "The smith has learned the secrets of steel.",
      "max_workers": 3,
      "output_multiplier": 1.0,
      "recipes": ["smelt_steel", "forge_chain_mail"]
    },
    "7": {
      "name": "Steel Foundry",
      "flavor": "Bellows and trip-hammers. The sound carries for miles.",
      "max_workers": 4,
      "output_multiplier": 1.3,
      "recipes": ["forge_plate_components"]
    },
    "9": {
      "name": "Legendary Smithy",
      "flavor": "A master whose works are spoken of in distant courts.",
      "max_workers": 5,
      "output_multiplier": 1.75,
      "recipes": ["master_steel", "masterwork_weapon"]
    }
  }
}
```

**Milestone levels for each file** (use §13 and §8.4 as source):

| File | base_cost | base_labor | Milestone levels |
|------|-----------|------------|-----------------|
| `smithy.json` | 400 | 400 | 1, 3, 5, 7, 9 |
| `iron_smelter.json` | 500 | 500 | 1, 3, 5, 7 |
| `grain_mill.json` | 350 | 350 | 1, 3, 5 |
| `farmstead.json` | 300 | 300 | 1, 3, 5 |
| `farm_plot.json` | 150 | 150 | 1, 3 |
| `granary.json` | 1200 | 800 | 1, 3, 5, 7 |
| `market.json` | 1000 | 1200 | 1, 3, 5, 7, 9 |
| `market_stall.json` | 100 | 100 | 1, 3 |
| `lumber_camp.json` | 350 | 350 | 1, 3, 5 |
| `iron_mine.json` | 600 | 600 | 1, 3, 5, 7 |
| `house.json` | 600 | 600 | 1, 3, 5, 7, 9 |
| `inn.json` | 800 | 800 | 1, 3, 5 |
| `well.json` | 100 | 100 | 1, 3 |

**Hard-gated buildings** (no `milestones`, add `hard_tier_min` instead):

| File | hard_tier_min |
|------|--------------|
| `bandit_camp.json` | (special — not player-buildable, leave as-is) |
| `derelict.json` | (state placeholder — leave as-is) |
| `open_land.json` | (terrain — leave as-is) |

The design calls for `city_wall`, `cathedral`, `bank`, `scriptorium`, `palace`, `armoury` as hard-gated. Of these, only `derelict.json` already exists. Leave the rest as ❌ to-do until Phase 5.

### 1-C  Add missing recipe files

The full production chain requires these recipes (§2 of design doc). Create one JSON file per recipe in `data/recipes/`. Use `data/schemas/recipe.schema.json` for the schema.

**Standard recipes to create:**

```
forge_spear.json       iron_ingot(2) → spear_unit(1)
forge_sword.json       iron_ingot(3) → iron_sword(1)
forge_shield.json      iron_ingot(2) + timber_log(1) → shield_unit(1)
smelt_steel.json       iron_ingot(3) + coal(2) → steel_billet(2)
forge_chain_mail.json  steel_billet(4) → chain_mail(1)
forge_plate_components.json  steel_billet(6) → plate_components(1)
brew_ale.json          wheat_bushel(2) → ale(1)
tan_leather.json       livestock_head(1) → leather(2) + meat(1)
weave_cloth.json       wool(3) → cloth_bolt(1)
bake_bread.json        flour_sack(1) → bread(2)
tailor_garment.json    cloth_bolt(1) + leather(1) → cloth_garment(1)
```

For each, set `recipe_type: "standard"`. Include `min_building_level` (integer) matching the milestone at which the recipe unlocks (from §8.4 smithy table and §2 production chains).

### 1-D  Add missing goods files

Check `data/goods/` — create any goods referenced in recipes above that don't have a JSON file yet. Fields: `id`, `name`, `base_value`, `weight`, `spoilage_days` (optional), `tags`.

Priority goods to add if missing:
- `ale.json` (base_value: 8, spoilage_days: 30)
- `bread.json` (base_value: 4, spoilage_days: 7)
- `spear_unit.json` (base_value: 15, tags: ["weapon"])
- `iron_sword.json` (base_value: 35, tags: ["weapon"])
- `shield_unit.json` (base_value: 20, tags: ["weapon"])
- `chain_mail.json` (base_value: 90, tags: ["armor"])
- `steel_billet.json` (base_value: 25, tags: ["refined_metal"])
- `plate_components.json` (base_value: 150, tags: ["armor_component"])
- `cloth_garment.json` (base_value: 12, tags: ["textile"])
- `meat.json` (base_value: 5, spoilage_days: 5)
- `coal.json` (base_value: 3, tags: ["fuel"])

---

## Phase 2 — SettlementState Extensions

**Goal:** Add the new fields §4, §7, and §12 require to `SettlementState`.  
**File:** `src/simulation/settlement/settlement_state.gd`  
**Test after:** Run `tests/test_data_loading.gd` and `tests/test_economy.gd`.

### 2-A  Add fields to `SettlementState`

Add the following at the end of the variable declarations, before any existing `from_dict` / `to_dict` methods:

```gdscript
## Acreage breakdown for terrain simulation.
## Keys: "arable_acres", "worked_acres", "woodlot_acres", "pasture_acres", "mining_slots"
var acreage: Dictionary = {}

## Tool stock tracking: tool_good_id → float quantity.
## Consumed by _run_standard_recipes each pulse; triggers shortage if zero.
var tool_stocks: Dictionary = {}

## Seasonal grain reserve target in bushels. Recomputed each pulse (Step 10).
## Governor will prioritse farm upgrades when actual stock < this target.
var seasonal_reserve: float = 0.0

## Per-building build-demand scores updated each pulse (Step 11).
## building_id → float score. Governor AI reads this directly.
var build_demand: Dictionary = {}

## Maximum building slots for this tier (from §7).
## Set by worldgen or settlement promotion; not recomputed each tick.
var max_slots: int = 8

## How many building slots are currently occupied.
## Computed from buildings array length; cached here to avoid re-counting.
var used_slots: int = 0
```

### 2-B  Update `from_dict` and `to_dict`

In both methods, add serialise/deserialise entries for every new field above. Follow the existing pattern in the file — look at how `inventory` and `shortages` are handled and use the same `dict.get(key, default)` pattern for `from_dict`.

### 2-C  Update `total_population()` helper

Verify that `total_population()` sums all values in `ss.population`. If it only sums specific keys, change it to `ss.population.values().reduce(func(acc, v): return acc + v, 0)`.

### 2-D  Populate derived acreage from `resource_tags` in `region_generator.gd`

**File:** `src/worldgen/region_generator.gd`

`arable_acres` already flows from `RegionCell` → `SettlementState.acreage["arable_acres"]` during worldgen. The three derived keys (`woodlot_acres`, `pasture_acres`, `mining_slots`) are **not** set — add them in the same place where `arable_acres` is written (look for the block that calls `cell.to_dict()` and assigns into the settlement state).

After the existing `acreage["arable_acres"] = cell.arable_acres` assignment, add:

```gdscript
# Derive acreage sub-types from resource_tags on the cell.
var tags: Array = cell.resource_tags

# Woodlot acres: proportional share of non-arable land when timber tag present.
if "timber" in tags or "wood" in tags:
    var non_arable: int = cell.total_acres - cell.arable_acres
    ss.acreage["woodlot_acres"] = int(non_arable * 0.6)
else:
    ss.acreage["woodlot_acres"] = 0

# Pasture acres: a portion of arable land when livestock/wool/pasture tags present.
if "pasture" in tags or "horses" in tags or "wool" in tags:
    ss.acreage["pasture_acres"] = int(cell.arable_acres * 0.3)
else:
    ss.acreage["pasture_acres"] = 0

# Mining slots: discrete slots per extractable mineral tag present.
var mineral_tags: Array[String] = ["iron_ore", "coal", "stone", "gold", "silver", "salt"]
var slot_count: int = 0
for tag in mineral_tags:
    if tag in tags:
        slot_count += 1
ss.acreage["mining_slots"] = slot_count
```

**Why proportional shares work here:**
- `woodlot_acres` = 60 % of remaining non-arable land (forests rarely occupy farmland).
- `pasture_acres` = 30 % of arable land (pasture competes with crops on the same flat ground).
- `mining_slots` is a count, not an acreage; one slot per distinct extractable mineral tag.

These numbers are intentionally conservative starting points — adjust via `economy_config.json` (Phase 9) once playtesting reveals balance issues.

Also add `"woodlot_acres"`, `"pasture_acres"`, and `"mining_slots"` to the `acreage` dictionary initialisation in `SettlementState.from_dict` with integer defaults of `0`.

---

## Phase 3 — Production System Overhaul

**Goal:** Replace the two-recipe stub in `ProductionLedger` with the full multi-pathway system from §4.  
**File:** `src/simulation/economy/production_ledger.gd`  
**Test after:** `tests/test_economy.gd` — all production assertions must pass.

### 3-A  Add labour split logic

Before `_run_agriculture`, add a helper `_compute_labour_split(ss)` that returns a dictionary:

```gdscript
{
  "farm_workers":       int,   # labourers assigned to arable acres
  "extraction_workers": int,   # assigned to woodlot / mining
  "industry_workers":   int    # assigned to building recipes (artisan class)
}
```

Rules (from §4 production decision model):
1. Total available = `ss.population.get("peasant", 0)` × `labour_efficiency(ss)`.
2. Assign farm workers first until `seasonal_reserve` is met (or all labourers exhausted).
3. Remainder split between extraction and industry proportional to `build_demand` scores for those categories.
4. Artisan/merchant class (`ss.population.get("artisan", 0)`) is always counted toward `industry_workers` — they don't do farm work.

```gdscript
static func _labour_efficiency(ss: SettlementState) -> float:
    # Unrest above 0.5 cuts output linearly to 0 at unrest=1.0
    return clampf(1.0 - maxf(0.0, ss.unrest - 0.5) * 2.0, 0.1, 1.0)
```

### 3-B  Overhaul `_run_agriculture`

Extend the existing acreage-based farm recipe to:
- Use `farm_workers` from the split above instead of the flat `_labour_factor`.
- Apply building level multiplier: look up the active smithy/farm level in `ss.buildings`, find the highest milestone at-or-below that level, use its `output_multiplier`.
- Support seasonal yield variation: multiply output by `_seasonal_factor(tick)` (spring/summer +20%, autumn +10%, winter −30%). Store the seasonal factor in `WorldState` so all settlements use the same value per tick.

### 3-C  Overhaul `_run_standard_recipes`

This is currently a no-op stub. Replace with:

```gdscript
static func _run_standard_recipes(ss: SettlementState, ws: WorldState, delta_ticks: int) -> void:
    for building_entry in ss.buildings:
        var b_id:    String = building_entry   # buildings is Array[String] of building instance IDs
        var b_def    = ContentRegistry.get_content("building", b_id)
        if b_def == null or b_def.is_empty():
            continue

        var level: int = _get_building_level(b_id, ss, ws)
        var milestone  = _active_milestone(b_def, level)
        if milestone == null:
            continue

        var recipes: Array = milestone.get("recipes", [])
        var workers: int   = mini(
            milestone.get("max_workers", 1),
            _available_workers_for(ss, b_id)
        )
        if workers == 0:
            continue

        for recipe_id: String in recipes:
            _run_one_standard_recipe(ss, recipe_id, workers, milestone.get("output_multiplier", 1.0), delta_ticks)
```

Add `_active_milestone(b_def, level)`:
- Collect all milestone keys (string → int).
- Sort descending.
- Return the first whose key ≤ current level.

Add `_run_one_standard_recipe(ss, recipe_id, workers, output_mult, delta_ticks)`:
- Load recipe from ContentRegistry.
- Check `recipe_type == "standard"`.
- Compute batches: `floor(workers × delta_ticks / recipe.workers_required)`.
- Check inputs: for each input good, ensure `ss.inventory.get(good, 0) >= batches × qty`. If short, reduce batches to what inputs allow, record shortage.
- Deduct inputs, credit outputs × `output_mult`.
- Append to `ss.production_log`.

### 3-D  Add spoilage (Step 0 in tick order)

Add a new public method `static func apply_spoilage(ss: SettlementState, delta_ticks: int)` called **before** Step 1 in `SettlementPulse._tick_one`:

```gdscript
static func apply_spoilage(ss: SettlementState, delta_ticks: int) -> void:
    var cr := ContentRegistry
    for good_id in ss.inventory.keys():
        var good_def = cr.get_content("good", good_id)
        if good_def == null:
            continue
        var spoilage_days: float = float(good_def.get("spoilage_days", 0.0))
        if spoilage_days <= 0.0:
            continue
        # Fraction that spoils per tick = delta_ticks / spoilage_days
        var spoil_rate: float = float(delta_ticks) / spoilage_days
        var loss: float = ss.inventory[good_id] * spoil_rate
        ss.inventory[good_id] = maxf(0.0, ss.inventory[good_id] - loss)
```

### 3-E  Add tool degradation (Step 3d in tick order)

After production, deduct tools from `ss.tool_stocks`. If `iron_tools` drops to zero, apply a `labour_efficiency` penalty of 0.5 in the next pulse. Record the shortage in `ss.shortages`.

Add to `SettlementPulse._tick_one` after the production block:
```gdscript
_apply_tool_degradation(ss, delta_ticks)
```

Add `_apply_tool_degradation` as a private method on `SettlementPulse`:
```gdscript
func _apply_tool_degradation(ss: SettlementState, delta_ticks: int) -> void:
    const TOOL_WEAR_PER_WORKER_PER_TICK: float = 0.002
    var workers: int = ss.total_population()
    var wear: float = float(workers) * TOOL_WEAR_PER_WORKER_PER_TICK * float(delta_ticks)
    var current: float = ss.tool_stocks.get("iron_tools", 0.0)
    ss.tool_stocks["iron_tools"] = maxf(0.0, current - wear)
    if ss.tool_stocks.get("iron_tools", 0.0) <= 0.0:
        ss.shortages["iron_tools"] = wear - current
```

---

## Phase 4 — Governor AI (New File)

**Goal:** Port governor AI from the old `SettlementManager.gd` and extend it to the new scoring model from §8.2.  
**Create:** `src/simulation/economy/governor_ai.gd`  
**Test after:** `tests/test_economy.gd` governor test cases.

### 4-A  Create `governor_ai.gd`

```gdscript
## GovernorAI — scores and queues building upgrades for a settlement.
##
## Called as Step 11 in SettlementPulse._tick_one.
## Reads ss.build_demand (populated by production decision model) and
## ss.buildings to compute action scores per building type.
## If the top-scored action exceeds BUILD_THRESHOLD and the settlement
## has sufficient resources, it queues the construction in WorldState.
class_name GovernorAI
extends RefCounted

const BUILD_THRESHOLD:    float = 6.0
const LEVEL_PENALTY_MULT: float = 0.5   # §8.2 — score / (1 + level × 0.5)
const TRANSPORT_COST_MULT: float = 1.5  # extra premium when importing materials
```

### 4-B  Implement `score_all(ss, ws)` → Dictionary of `building_id → float`

Follow the §8.2 formula exactly:

```
score = build_demand_score
      + district_fit_bonus
      + adjacency_bonus
      - cost_penalty
      - level_penalty
      - hard_gate_block
```

1. **`build_demand_score`**: read from `ss.build_demand[building_id]`, default 0.
2. **`district_fit_bonus`**: +2.0 if the building's category matches the settlement's predominant district (use `ss.tier` to infer: hamlet → agricultural, village → processing, town+ → mixed).
3. **`adjacency_bonus`**: implement the table from §8.3. For each pair, check `ss.buildings` for the adjacent building at level ≥ 1.
4. **`cost_penalty`**: compute `actual_cost / ss.inventory.get("coin", 1.0) * 3.0`. Actual cost = `base_cost × (target_level + 1)^2.2`.
5. **`level_penalty`**: `current_level × LEVEL_PENALTY_MULT`.
6. **`hard_gate_block`**: −99 if `b_def.hard_tier_min` exists and `ss.tier < b_def.hard_tier_min`.

### 4-C  Implement `maybe_queue_construction(ss, ws, scores)`

1. Sort scores descending.
2. For the top candidate, check:
   - Score ≥ `BUILD_THRESHOLD`.
   - Settlement has enough coin: `ss.inventory.get("coin", 0) >= actual_cost`.
   - `ss.used_slots < ss.max_slots` (slot available) OR building already exists (upgrade).
3. If all pass, add a construction entry to `ws.construction_queue` (create this dict in `WorldState` if it doesn't exist): `{ "settlement_id", "building_id", "target_level", "labor_remaining", "cost_paid" }`.
4. Deduct coin immediately.

### 4-D  Wire into `SettlementPulse._tick_one`

Add Step 10 (seasonal reserve) and Step 11 (governor) at the end of `_tick_one`:

```gdscript
# ── Step 10: Seasonal reserve check ─────────────────────────────────────────
_update_seasonal_reserve(ss)

# ── Step 11: Production decision scoring + governor AI ───────────────────────
if not ss.is_player_camp:
    _update_build_demand(ss, ws)
    var scores := GovernorAI.score_all(ss, ws)
    GovernorAI.maybe_queue_construction(ss, ws, scores)
```

Add `_update_seasonal_reserve(ss)` to `SettlementPulse`:
```gdscript
func _update_seasonal_reserve(ss: SettlementState) -> void:
    const SAFETY_DAYS: Array[int] = [60, 45, 30, 20, 15]  # by tier
    var days: int = SAFETY_DAYS[clampi(ss.tier, 0, 4)]
    var pop:  int = ss.total_population()
    var daily_wheat_need: float = float(pop) * 0.015
    ss.seasonal_reserve = daily_wheat_need * float(days)
```

Add `_update_build_demand(ss, ws)` — this is a minimal stub for now that sets demand scores based on shortages and prosperity:
```gdscript
func _update_build_demand(ss: SettlementState, ws: WorldState) -> void:
    ss.build_demand = {}
    # Food security drives farm demand
    var grain_stock: float = ss.inventory.get("wheat_bushel", 0.0)
    if grain_stock < ss.seasonal_reserve:
        var deficit_ratio := 1.0 - (grain_stock / maxf(ss.seasonal_reserve, 1.0))
        ss.build_demand["farmstead"] = 10.0 * deficit_ratio
        ss.build_demand["farm_plot"] = 8.0 * deficit_ratio
    # Tool shortage drives smithy demand
    if ss.shortages.has("iron_tools"):
        ss.build_demand["smithy"] = 8.0
    # Prosperity surplus drives market demand
    if ss.prosperity > 0.6:
        ss.build_demand["market_stall"] = ss.prosperity * 5.0
        ss.build_demand["market"] = ss.prosperity * 4.0
```

---

## Phase 5 — Price System Extensions

**Goal:** Add seasonal multiplier and route-price bleed to `PriceLedger` (§5 of design doc).  
**File:** `src/simulation/economy/price_ledger.gd`

### 5-A  Add seasonal multiplier

Add to `_update_good`:

```gdscript
var season_mult: float = _seasonal_price_mult(ws, good_id)
var target: float = clampf(base_val * ratio * season_mult, ...)
```

Add `_seasonal_price_mult(ws, good_id)`:
- Read `ws.current_tick` and compute current season (each season = 90 ticks, assuming 360 ticks/year).
- Apply multipliers from §5 of design doc:
  - Food goods (`tags` contains `"food"`): winter × 1.4, spring × 0.9, harvest × 0.8.
  - Fuel goods (`tags` contains `"fuel"`): winter × 1.6, summer × 0.7.
  - All others: × 1.0.

### 5-B  Add route-price bleed

After the smoothed price update in `_update_good`, blend in the average price of the same good at connected settlements:

```gdscript
var route_avg: float = _route_average_price(ss.settlement_id, good_id, ws)
if route_avg > 0.0:
    ss.prices[good_id] = lerpf(ss.prices[good_id], route_avg, ROUTE_BLEND)
```

Add constant `const ROUTE_BLEND: float = 0.15` at the top of `PriceLedger`.

Add `_route_average_price(sid, good_id, ws)`:
- Iterate `ws.routes.get(sid, [])`.
- For each connected settlement, get its price for `good_id`.
- Return the average; return 0 if no connected settlements or none have the good.

### 5-C  Add transport cost to `TradePartySpawner`

In `_try_spawn_from`, before committing a trade party, compute:

```gdscript
var dist: float   = edge.get("distance", 1.0)
var transport_cost: float = dist * 0.02 * cargo_qty * ss.prices.get(good_id, 1.0)
var buy_price: float  = ss.prices.get(good_id, 1.0)
var sell_price: float = dest_ss.prices.get(good_id, 1.0)
var profit: float = (sell_price - buy_price) * cargo_qty - transport_cost
const MIN_PROFIT_MARGIN: float = 0.05
if profit / maxf(buy_price * cargo_qty, 1.0) < MIN_PROFIT_MARGIN:
    continue   # not worth sending
```

---

## Phase 6 — WorldState Extensions

**Goal:** Add `route_states`, `construction_queue`, and seasonal tracking to `WorldState`.  
**File:** `src/simulation/world/world_state.gd` (locate and read this file first; it may be at a different path).

### 6-A  Add fields

```gdscript
## Active construction projects across all settlements.
## Array of {settlement_id, building_id, target_level, labor_remaining, cost_paid}
var construction_queue: Array = []

## Per-route trade state. Key: "origin_id:dest_id:good_id", Value: {active, tick_spawned}
var route_states: Dictionary = {}

## Current season index: 0=spring, 1=summer, 2=autumn, 3=winter
## Derived from current_tick; set once per tick for all settlements.
var current_season: int = 0
```

### 6-B  Advance construction queue each tick

In `SettlementPulse.tick_all`, after all settlements are ticked and before `TradePartySpawner.try_spawn_all`, iterate `ws.construction_queue` and advance each project's `labor_remaining` by the settlement's labour power. When `labor_remaining <= 0`, call a new `BuildingSystem.complete_construction(ws, project)` method (Phase 7).

### 6-C  Update `from_dict` / `to_dict` in `WorldState`

Ensure both new fields are serialised/deserialised. Follow the existing pattern for `settlements` and `trade_parties`.

---

## Phase 7 — Building System

**Goal:** Create a `BuildingSystem` that manages building levels, milestone lookups, and construction completion.  
**Create:** `src/simulation/economy/building_system.gd`

### 7-A  Create `building_system.gd`

```gdscript
## BuildingSystem — manages building level progression and milestone lookups.
class_name BuildingSystem
extends RefCounted
```

### 7-B  Implement `get_active_milestone(b_def, level) → Dictionary`

- Collect all keys from `b_def.milestones` as integers.
- Sort descending.
- Return the value of the first key ≤ `level`. Return `{}` if none found.

### 7-C  Implement `compute_cost(b_def, target_level) → float`

```gdscript
static func compute_cost(b_def: Dictionary, target_level: int) -> float:
    var base: float = float(b_def.get("base_cost", 100))
    return base * pow(float(target_level + 1), 2.2)
```

### 7-D  Implement `complete_construction(ws, project) → void`

- Find `ss = ws.settlements[project.settlement_id]`.
- Find or create the building instance in `ss.buildings` at the target level. (Since `SettlementState.buildings` is currently `Array[String]` of instance IDs, this will need to look up via `EntityRegistry` or a simple `building_levels` dict — choose the simpler path: add a `building_levels: Dictionary` to `SettlementState` mapping `building_id → int level`.)
- Increment `building_levels[building_id]` to `target_level`.
- Recompute `ss.used_slots`.
- Log completion to `ss.production_log`.

### 7-E  Add `building_levels` to `SettlementState`

In Phase 2 you added several fields; add one more:

```gdscript
## Simple level map: building_id (string) → current level (int).
## Used by GovernorAI and BuildingSystem. Separate from `buildings` (instance IDs).
var building_levels: Dictionary = {}
```

Update `from_dict` / `to_dict` accordingly.

---

## Phase 8 — Stability & Anti-Stupidity Rules

**Goal:** Implement §9 guards so the simulation doesn't produce nonsensical states.  
**File:** Extend `SettlementPulse._tick_one` with guard methods.

### 8-A  Implement minimum inventory floor

After consumption (Step 4), clamp all inventory to 0. Add shortages for any good that went negative:

```gdscript
func _apply_inventory_floor(ss: SettlementState) -> void:
    for good_id in ss.inventory.keys():
        if ss.inventory[good_id] < 0.0:
            ss.shortages[good_id] = ss.shortages.get(good_id, 0.0) + abs(ss.inventory[good_id])
            ss.inventory[good_id] = 0.0
```

Call this at the end of `_consume`.

### 8-B  Cap unrest

After `_update_prosperity_unrest`, clamp `ss.unrest` to `[0.0, 1.0]` and `ss.prosperity` to `[0.0, 1.0]`.

### 8-C  Prevent over-building

In `GovernorAI.maybe_queue_construction`, also check:
- The same `building_id` is not already in `ws.construction_queue` for this settlement.
- `building_levels.get(building_id, 0) < max_level_for_tier(ss.tier)` where `max_level_for_tier` returns `[3, 5, 7, 9, 10][tier]`.

### 8-D  Famine emergency override

In `_update_build_demand`, if `ss.inventory.get("wheat_bushel", 0.0) < ss.total_population() * 0.015 * 7.0` (less than 7 days of food), set `build_demand["farmstead"] = 50.0` and zero out all other build demands.

---

## Phase 9 — Data Config File

**Goal:** Move all magic numbers out of GDScript and into a config file (§12 of design doc).  
**Create:** `data/config/economy_config.json`

```json
{
  "safety_days_by_tier": [60, 45, 30, 20, 15],
  "route_blend": 0.15,
  "price_smooth": 0.20,
  "price_floor": 0.25,
  "price_cap": 4.0,
  "build_threshold": 6.0,
  "level_penalty_mult": 0.5,
  "labour_realloc_max_per_tick": 0.10,
  "min_profit_margin": 0.05,
  "surplus_threshold": 20.0,
  "shortage_threshold": 5.0,
  "cargo_fraction": 0.5,
  "days_buffer": 14.0,
  "abandon_threshold": 0.5,
  "abandonment_patience_ticks": 180,
  "tool_wear_per_worker_per_tick": 0.002,
  "seasonal_price_multipliers": {
    "food": {"spring": 0.9, "summer": 1.0, "autumn": 0.8, "winter": 1.4},
    "fuel": {"spring": 1.0, "summer": 0.7, "autumn": 1.1, "winter": 1.6}
  }
}
```

After creating this file, replace the hardcoded constants in `SettlementPulse`, `PriceLedger`, `TradePartySpawner`, and `GovernorAI` with reads from this config. Load via `ContentRegistry` or a dedicated `EconomyConfig` autoload singleton.

---

## Phase 10 — Test Coverage

**Goal:** Verify every phase with unit tests.  
**Existing test files:** `tests/test_economy.gd`, `tests/test_data_loading.gd`

Read both files first. Then extend them with the following cases:

### `test_data_loading.gd`
- Assert every building JSON in `data/buildings/` deserialises without schema errors.
- Assert every recipe JSON in `data/recipes/` validates.
- Assert `mill_grain`, `smelt_iron`, `forge_tools`, and all new recipes have valid `inputs`/`outputs`.

### `test_economy.gd`
- **Spoilage:** Seed a settlement with 100 bread (spoilage_days = 7). Run one pulse (delta = 30 ticks). Assert inventory < 100.
- **Recipe production:** Seed a settlement with a smithy at level 5, 10 `iron_ingot`, 5 `coal`. Run one pulse. Assert `steel_billet > 0`.
- **Labour split:** Seed a settlement with low grain stock (< seasonal reserve). Assert farm workers > industry workers.
- **Governor queues build:** Seed a settlement with tool shortage and enough coin. Run one pulse. Assert `ws.construction_queue` contains a smithy entry.
- **Hard gate blocks:** Set settlement tier = 0. Assert GovernorAI score for `cathedral` = -99 (or below `BUILD_THRESHOLD`).
- **Cost formula:** Assert `BuildingSystem.compute_cost(smithy_def, 5)` ≈ 27,000 (within 5%).
- **Price seasonal:** Set `ws.current_tick` to a winter tick. Assert wheat price > base_value × 1.3.
- **Route bleed:** Connect two settlements with different wheat prices. Run one pulse. Assert prices moved toward each other.

---

## Phase Order Summary

```
Phase 1  →  Schema + data files      (no code changes; run test_data_loading)
Phase 2  →  SettlementState fields   (pure data; run test_data_loading)
Phase 3  →  ProductionLedger overhaul (core simulation; run test_economy)
Phase 4  →  GovernorAI new file      (new file + small pulse wiring)
Phase 5  →  PriceLedger extensions   (extend existing file)
Phase 6  →  WorldState extensions    (extend existing file)
Phase 7  →  BuildingSystem new file  (new file; wired from Phase 6)
Phase 8  →  Anti-stupidity guards    (small additions to pulse)
Phase 9  →  Config file              (data file + const replacement)
Phase 10 →  Test coverage            (extend test files)
```

**Do not begin Phase N+1 until:**
1. All files for Phase N have been written.
2. The relevant test suite passes with 0 errors in the Godot test runner.
3. No new errors appear in `get_errors()` on the modified files.

---

## Files Created / Modified Reference

| Phase | Action | Path |
|-------|--------|------|
| 1-A | Modify | `data/schemas/building.schema.json` |
| 1-B | Modify ×16 | `data/buildings/*.json` |
| 1-C | Create ×11 | `data/recipes/forge_spear.json` … |
| 1-D | Create ×11 | `data/goods/ale.json` … |
| 2 | Modify | `src/simulation/settlement/settlement_state.gd` |
| 2-D | Modify | `src/worldgen/region_generator.gd` (derive woodlot/pasture/mining from resource_tags) |
| 3 | Modify | `src/simulation/economy/production_ledger.gd` |
| 3-D | Modify | `src/simulation/economy/settlement_pulse.gd` (spoilage call) |
| 3-E | Modify | `src/simulation/economy/settlement_pulse.gd` (tool degradation) |
| 4 | Create | `src/simulation/economy/governor_ai.gd` |
| 4-D | Modify | `src/simulation/economy/settlement_pulse.gd` (steps 10+11) |
| 5 | Modify | `src/simulation/economy/price_ledger.gd` |
| 5-C | Modify | `src/simulation/economy/trade_party_spawner.gd` |
| 6 | Modify | `src/simulation/world/world_state.gd` |
| 7 | Create | `src/simulation/economy/building_system.gd` |
| 7-E | Modify | `src/simulation/settlement/settlement_state.gd` |
| 8 | Modify | `src/simulation/economy/settlement_pulse.gd` |
| 9 | Create | `data/config/economy_config.json` |
| 10 | Modify | `tests/test_economy.gd`, `tests/test_data_loading.gd` |
