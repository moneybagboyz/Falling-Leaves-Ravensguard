# Ravensguard — Systems Reference

A comprehensive reference document for all game systems. Intended for design refinement and future development.

---

## Table of Contents

1. [Simulation Clock & Tick Scheduler](#1-simulation-clock--tick-scheduler)
2. [Content Registry & Entity Registry](#2-content-registry--entity-registry)
3. [Save / Load / Migration](#3-save--load--migration)
4. [World State & Data Models](#4-world-state--data-models)
5. [Settlement Economy — SettlementPulse](#5-settlement-economy--settlementpulse)
6. [Production — ProductionLedger](#6-production--productionledger)
7. [Prices — PriceLedger](#7-prices--priceledger)
8. [Trade — TradePartySpawner & PartyCore](#8-trade--tradepartyspawner--partycore)
9. [Property & Ownership — PropertyCore](#9-property--ownership--propertycore)
10. [Construction System](#10-construction-system)
11. [Workshop Manager](#11-workshop-manager)
12. [Camp System — CampManager](#12-camp-system--campmanager)
13. [Player Trade — PlayerTrade](#13-player-trade--playertrade)
14. [Follower System & Group Management](#14-follower-system--group-management)
15. [Recruitment — RecruitmentManager](#15-recruitment--recruitmentmanager)
16. [Reputation — ReputationEvents](#16-reputation--reputationevents)
17. [Character Needs — NeedsSystem](#17-character-needs--needssystem)
18. [Work & Wages — WorkSystem](#18-work--wages--worksystem)
19. [NPC Pool & Schedules](#19-npc-pool--schedules)
20. [Combat System](#20-combat-system)
21. [World Generation](#21-world-generation)
22. [UI Layer](#22-ui-layer)
23. [Starting Profiles — Backgrounds](#23-starting-profiles--backgrounds)

---

## 1. Simulation Clock & Tick Scheduler

### Purpose
Central orchestrator for all simulation updates. Ensures systems execute in a fixed, deterministic order every frame/step.

### `SimulationClock`
- Tracks the current absolute tick counter.
- Serialised as part of every save file.
- Used by `TickScheduler` and `NeedsSystem` to time recurring events.

### `TickScheduler`

**Constants**
| Constant | Value | Notes |
|---|---|---|
| `STRATEGIC_CADENCE` | `1` | Strategic phases fire every tick (effectively real-time, no day skipping) |

**Phases (in execution order every tick)**
| # | Phase | Description |
|---|---|---|
| 1 | `NEEDS` | Player hunger, fatigue, inn-rent deduction |
| 2 | `MOVEMENT` | Trade party movement, NPC schedule steps |
| 3 | `NPC_BEHAVIOR` | NPC routine / schedule decisions |
| 4 | `COMBAT` | Combat resolver step (if active battle) |
| 5 | `WORLD_PULSE` | Settlement economy tick (strategic) |
| 6 | `PRODUCTION_PULSE` | Production, construction, work wages (strategic) |
| 7 | `UI_SYNC` | Signal flush to update on-screen data |
| 8 | `EVENT_FLUSH` | EventQueue drains all queued events to listeners |

**Strategic phases** = `WORLD_PULSE` + `PRODUCTION_PULSE`. Both fire every tick (cadence = 1).

**Registration API**
```
TickScheduler.register_hook(phase, callable)
TickScheduler.unregister_hook(phase, callable)
```
Bootstrap calls `register_hook` once for each system. The scheduler calls them in phase order each tick.

### Design Notes / Refinement Candidates
- `STRATEGIC_CADENCE = 1` means economy ticks every real tick — economy moves fast. Consider raising to 24 (one "day") with `delta_ticks` passed through to smooth production rates and give the player more time to react.
- Consider a `PAUSED` state that halts ticks but allows the player to issue orders (useful for combat and troop management).
- Day/night cycle could be tied to `tick % TICKS_PER_DAY`.

---

## 2. Content Registry & Entity Registry

### `ContentRegistry`
- Singleton.
- Loads and indexes all JSON data files under `data/`.
- Access pattern: `ContentRegistry.get_content(category, id) → Dictionary`
- Categories: `armor`, `background`, `biome`, `body_zone`, `building`, `faction`, `good`, `material`, `population_class`, `recipe`, `skill`, `terrain_type`, `trait`, `weapon`, `world`.

### `EntityRegistry`
- Generates unique IDs for runtime objects.
- `EntityRegistry.generate_id(prefix) → String` (e.g. `"settlement_00042"`).
- Serialised in saves to maintain ID continuity across load/save cycles.

### Design Notes
- No hot-reload of data at runtime; changes to JSON require a restart.
- Schema validation is JSON-Schema based (`data/schemas/`), enforced at load time.

---

## 3. Save / Load / Migration

### `SaveManager`

**File location:** `user://saves/<slot>.json` (default slot = `"default"`)

**Save format (top-level keys)**
```json
{
  "schema_version": "0.1.0",
  "timestamp": <unix epoch>,
  "clock": { ... },
  "entity_registry": { ... },
  "world_state": { ... }
}
```

**Constants**
| Constant | Value |
|---|---|
| `CURRENT_SCHEMA_VERSION` | `"0.1.0"` |
| `SAVE_DIR` | `"user://saves/"` |

**Signals**
- `save_completed(slot)`
- `load_completed(slot)`
- `save_failed(slot, reason)`
- `load_failed(slot, reason)`
- `migration_applied(from_version, to_version)`

**Migration**
- On load, if the saved `schema_version` differs from `CURRENT_SCHEMA_VERSION`, `MigrationRunner` is called to transform the raw dict before deserialisation.
- Every schema change must be accompanied by a migration function.

### Design Notes
- Only one save slot currently accessible from the UI; additional slots are supported at the `SaveManager` API level.
- Autosave not yet implemented.

---

## 4. World State & Data Models

### `WorldState`
Top-level container. Fields:

| Field | Type | Description |
|---|---|---|
| `world_seed` | `int` | Master RNG seed for deterministic worldgen |
| `world_tiles` | `Dictionary` | `"wx,wy" → tile dict` |
| `settlements` | `Dictionary` | `settlement_id → SettlementState` |
| `characters` | `Dictionary` | `person_id → PersonState` |
| `trade_parties` | `Dictionary` | `party_id → dict` |
| `construction_jobs` | `Dictionary` | `job_id → dict` |
| `property_ledger` | `Dictionary` | `instance_key → owner_person_id` |
| `region_grids` | `Dictionary` | `"wx,wy" → grid dict` (lazy cache) |
| `routes` | `Dictionary` | `settlement_id → Array[edge_dict]` |
| `player_character_id` | `String` | ID of the player's PersonState |
| `player_group` | `Dictionary` | Serialised `GroupState` |
| `active_battle` | `BattleState` or `null` | Non-null during combat |

### `SettlementState`
| Field | Type | Notes |
|---|---|---|
| `settlement_id` | `String` | |
| `name` | `String` | |
| `tier` | `int` | 0=camp, 1=hamlet, 2=village, 3=town, 4=city |
| `cell_id` | `String` | World tile key `"wx,wy"` |
| `faction_id` | `String` | |
| `is_hub` | `bool` | Regional trade hub |
| `is_player_camp` | `bool` | Player-founded camp |
| `population` | `Dictionary` | `class_id → count` |
| `prosperity` | `float` | 0.0–1.0 |
| `unrest` | `float` | 0.0–1.0 |
| `inventory` | `Dictionary` | `good_id → amount (float)` |
| `prices` | `Dictionary` | `good_id → current price (float)` |
| `shortages` | `Dictionary` | `good_id → shortage amount` |
| `labor_slots` | `Array[Dictionary]` | All job slots in settlement |
| `territory_cell_ids` | `Array[String]` | World tiles in this settlement's territory |
| `acreage` | `Dictionary` | `{worked_acres, arable_acres, woodlot_acres}` |

### `PersonState`
| Field | Type | Notes |
|---|---|---|
| `person_id` | `String` | |
| `name` | `String` | |
| `population_class` | `String` | peasant / artisan / merchant / noble |
| `home_settlement_id` | `String` | |
| `active_role` | `String` | Current job slot or `""` |
| `work_cell_id` | `String` | Tile where work takes place |
| `coin` | `float` | Personal wealth |
| `carried_items` | `Array[String]` | Item IDs in inventory |
| `skills` | `Dictionary` | `skill_id → {level, xp, xp_per_level}` |
| `needs` | `Dictionary` | `{hunger, fatigue}` (0.0–1.0) |
| `shelter_status` | `String` | `""` / `"rented"` |
| `follower_ids` | `Array[String]` | NPCs following this person |
| `ownership_refs` | `Array[String]` | Instance keys of owned properties |
| `reputation` | `Dictionary` | `entity_id → float (-1 to 1)` |

---

## 5. Settlement Economy — SettlementPulse

### Purpose
Simulates the macro-economy of each NPC settlement every tick: generate coin from trade, consume goods, update population, spread arable acreage.

### Key Constants
| Constant | Value | Meaning |
|---|---|---|
| `PROSPERITY_GROW` | `0.005` | Prosperity increase when conditions are good |
| `PROSPERITY_SHRINK` | `0.008` | Prosperity decrease under shortage/unrest |
| `UNREST_DECAY` | `0.003` | Unrest passive decay per tick |
| `UNREST_SHORTAGE` | `0.010` | Unrest increase per shortage event |
| `POP_GROW_RATE` | `0.004` | Population grow per tick (well-fed, low unrest) |
| `POP_DECLINE_RATE` | `0.008` | Population loss per tick (shortage or high unrest) |
| `COIN_PER_TIER_PER_TICK` | varies | Base coin generation by settlement tier |

### Tick Order (inside `SettlementPulse._tick_one(ss)`)
1. **`_generate_coin(ss)`** — base coin from settlement tier
2. **`ProductionLedger.run(ss, ws, delta)`** — produce goods from acreage and buildings
3. **`PriceLedger.update(ss, delta)`** — adjust prices based on supply/demand
4. **`_consume(ss)`** — population consumes goods; shortages incremented if stock < 0
5. **`_update_unrest(ss)`** — unrest rises per shortage, decays passively
6. **`_update_prosperity(ss)`** — prosperity shifts based on unrest and shortage
7. **`_update_population(ss)`** — population grows/declines based on prosperity
8. **`_adjust_worked_acres(ss)`** — farm acreage targeted at `arable × (0.4 + prosperity × 0.5)`
9. **`TradePartySpawner.try_spawn_all(ws, tick)`** — initiate trade parties for surplus goods

### Starter Stock (first settlement visit / game start)
`_seed_starter_stock(ss)`:
- `wheat_bushel = population × 0.015 × 30` (30-tick supply)
- `coin = tier × 20`

### Design Notes
- Player-owned camps (`is_player_camp = true`) are skipped for trade party spawning.
- Shortages immediately raise unrest — no buffer period. Consider a "days in shortage" grace window.
- All settlements tick every game tick, which is computationally cheap now but may need batching at large map counts.

---

## 6. Production — ProductionLedger

### Purpose
Computes goods produced by each settlement every production pulse. Three pathways:

### Pathway 1: Agriculture (`farm_grain` recipe)
```
output = worked_acres × base_yield_per_acre_per_tick × fertility × labour_factor × delta_ticks
```

**`base_yield_per_acre_per_tick`** — from `data/recipes/farm_grain.json`, default `0.11`

**`fertility`** — from world tile's `"prosperity"` field (set by BiomeClassifier at worldgen), clamped `0.05–1.0`

**`labour_factor`** — `total_population / tier_ref_pop`, clamped `0.1–2.0`

| Tier | Reference pop |
|---|---|
| 0 | 75 |
| 1 | 300 |
| 2 | 900 |
| 3 | 2800 |
| 4 | 8000 |

Output good: `wheat_bushel`

### Pathway 2: Extraction (`log_timber` recipe)
```
output = woodlot_acres × WOODLOT_LABOUR_FACTOR × base_yield × fertility × delta_ticks
```
`WOODLOT_LABOUR_FACTOR = 0.002` — keeps timber output modest without a real labour model.

Output good: `timber_log`

### Pathway 3: Standard Recipes (building-resident)
- Reads each building's recipe; deducts input goods and credits output goods.
- Skipped if the required inputs are not in inventory.

### Design Notes
- `WOODLOT_LABOUR_FACTOR` is a placeholder for a proper forestry labour model.
- No current seasonal variation — yield is uniform year-round.
- Buildings that produce outputs (smithy, smelter, mill) require inputs in inventory; there is no "work order queue" — the recipe either fires or doesn't each tick.

---

## 7. Prices — PriceLedger

### Purpose
Adjusts per-settlement prices toward supply/demand equilibrium each production pulse.

### Formula
```
demand = sum over population classes of (class_count × consumption_per_head_per_tick × delta_ticks)
target = clamp(base_value × demand / max(supply, ε), base_value × PRICE_FLOOR, base_value × PRICE_CAP)
price  = lerp(current_price, target, SMOOTH)
```

### Constants
| Constant | Value |
|---|---|
| `PRICE_FLOOR` | `0.25` — min 25% of base value |
| `PRICE_CAP` | `4.0` — max 400% of base value |
| `EPSILON` | `0.001` — prevents divide-by-zero |
| `SMOOTH` | `0.20` — 20% of gap closed per pulse |

### Design Notes
- Only recalculates goods currently present in inventory. Goods at zero aren't repriced.
- Consider adding a "famine premium" (PRICE_CAP > 4.0) for critical goods like wheat during shortage.
- Inter-settlement price arbitrage is the intended driver for trade parties.

---

## 8. Trade — TradePartySpawner & PartyCore

### Purpose
Spawns NPC trade caravans moving surplus goods from settlements that have too much to those that have too little. Caravans travel along world-tile routes and deliver goods on arrival.

### `TradePartySpawner` Constants
| Constant | Value | Meaning |
|---|---|---|
| `SURPLUS_THRESHOLD` | `20.0` | Min surplus before a settlement exports |
| `SHORTAGE_THRESHOLD` | `5.0` | Min shortage at destination before spawning |
| `CARGO_FRACTION` | `0.5` | Fraction of surplus loaded on the party |
| `DAYS_BUFFER` | `14.0` | Days of supply below which proactive resupply triggers |

### Spawn logic (per settlement per tick)
1. Skip if settlement is a player camp.
2. For each good with `inventory > SURPLUS_THRESHOLD`:
   - Score each connected neighbour by `shortage × 2` if above threshold, or `(DAYS_BUFFER - days_left) + 0.1` if running low.
   - Pick best scoring destination.
   - Skip if a party with that (origin, destination, good) already exists.
   - Spawn one party per surplus good per pulse.
3. Deduct `cargo_fraction × surplus` from origin inventory.

### `PartyCore` — Travel
- Hook registered on `MOVEMENT` phase.
- Each tick: `path_idx += speed_tiles_per_tick`.
- On arrival (`path_idx >= path.size()`): calls `_deliver()`.

### Delivery
- Payment = `sum(qty × dest_price) × 0.8` (20% kept as transport cost).
- Destination loses coin; origin gains coin (capped at what destination can afford).
- Goods deposited into destination inventory.

### `TradePartyState` Fields
| Field | Notes |
|---|---|
| `party_id` | Generated ID |
| `origin_id` / `dest_id` | Settlement IDs |
| `cargo` | `good_id → amount` (single good per party) |
| `path` | Array of world-tile keys |
| `path_idx` | Current position along path |
| `speed_tiles_per_tick` | Movement rate |
| `ticks_en_route` | Elapsed travel time |

### Design Notes
- Each party carries only one type of good. Multi-good caravans are a future feature.
- No party attrition / bandit interception yet.
- 20% transport cost is fixed; could scale with path length.
- Player cannot currently interact with or redirect NPC trade parties.

---

## 9. Property & Ownership — PropertyCore

### Purpose
Tracks which players own which buildings/camps and distributes income to owners every production pulse.

### Key Constants
| Constant | Value | Meaning |
|---|---|---|
| `OWNER_INCOME_SHARE` | `0.40` | Owner receives 40% of building's theoretical income |
| `UPKEEP_TICKS_PER_SEASON` | `90.0` | Season = 90 ticks (used as income denominator) |

### Instance Key Format
`"building_id:cell_id"` for buildings (e.g. `"smithy:32,18"`)
`"camp:settlement_id"` for player-founded camps

### Ownership API
```
PropertyCore.register_ownership(ws, instance_key, person_id)
PropertyCore.owner_of(ws, instance_key) → String
PropertyCore.buildings_owned_by(ws, person_id) → Array[String]
```

### Income Calculation (called each production pulse)
For each owned building:
```
income_per_tick = (upkeep_per_season / UPKEEP_TICKS_PER_SEASON) × 2 × OWNER_INCOME_SHARE × delta / slot_count
```

For player camps:
```
income = settlement_inventory["coin"] × OWNER_INCOME_SHARE
```
40% of the camp's coin pool is transferred to the owner's `PersonState.coin` each pulse.

### Design Notes
- Ownership is stored in `WorldState.property_ledger` (flat dict).
- No ownership transfer / sale system yet.
- Building maintenance costs (wear & tear, employee wages) not currently subtracted from owner income — net income = gross.
- `slot_count` denominator spreads income across all slots the building provides.

---

## 10. Construction System

### Purpose
Advances active construction jobs each production pulse, completing them when their `ticks_remaining` reaches zero.

### `ConstructionJob` Fields
| Field | Notes |
|---|---|
| `job_id` | Generated |
| `building_id` | What is being built |
| `cell_id` | World tile location |
| `owner_id` | Person who owns it on completion |
| `settlement_id` | Settlement the building belongs to |
| `ticks_remaining` | Counts down from `labor_days` in the recipe |
| `resources_committed` | Goods already deducted |
| `started` | bool |

### Completion Steps
1. Stamp `building_id` onto world tile.
2. Erase cached `region_grids[cell_id]` (forces SubRegionGenerator to rebuild).
3. Add `labor_slots` from building definition to `SettlementState`.
4. Add `cell_id` to `ss.territory_cell_ids` if not present.
5. Register `instance_key → owner_id` in `PropertyCore`.
6. Add `instance_key` to `owner.ownership_refs`.

### Under-Construction Tile State
During construction the tile shows `building_id = "open_land"` with `construction_job_id` set, so SubRegionGenerator can render scaffolding.

### Design Notes
- Construction speed is purely time-based (labor_days ticks). No construction worker assignment required.
- A building cannot be started on a tile already occupied.
- If `owner` left the game world mid-construction, job still completes (owner is stored by ID).

---

## 11. Workshop Manager

### Purpose
Two-path API for the player to acquire production buildings:
1. **Purchase** — buy an unowned building that already exists in a settlement.
2. **Build** — commission a new building on an open tile, deducting resources and creating a `ConstructionJob`.

### Production Categories
Only buildings with category `"production"`, `"extraction"`, or `"crafting"` are purchasable/buildable.

### Purchase Path
1. Check not already owned.
2. Look up `bdef.construction_cost.coin` for price (default 50 coin).
3. Deduct coin from player.
4. `PropertyCore.register_ownership`.
5. `ReputationEvents.gain("trade_completed")` for the settlement.

### Current Building Purchase Prices
| Building | Coin Price | Notes |
|---|---|---|
| Farmstead | 60 | ~60-day payback at 40% income share |
| Lumber Camp | 80 | |
| Grain Mill | 80 | |
| Smithy | 120 | |
| Iron Smelter | 150 | |
| Iron Mine | 200 | |

### Build Path
1. Validate tile is `open_land`.
2. Check and deduct construction resources from `player.carried_items` (or linked camp stock).
3. Create `ConstructionJob` with `ticks_remaining = cost.labor_days`.
4. Mark tile as under construction.

### Design Notes
- There is no "for sale" listing — the player can try to purchase any unowned production building they can see.
- Resources for building are taken from `carried_items` first, then camp inventory.
- No build permit / settlement permission required; anyone can build anywhere.

---

## 12. Camp System — CampManager

### Purpose
Lets the player found a personal base camp on any unoccupied, non-settlement world tile.

### Founding Cost
```
FOUNDING_COST = { "timber_log": 5 }
```

### Validation
- Tile must exist and have `building_id == "open_land"` or no `building_id`.
- Tile must NOT be inside any settlement's `territory_cell_ids`.

### On Success
- Deducts 5× `timber_log` from player's `carried_items`.
- Creates a new `SettlementState` with `tier = 0`, `is_player_camp = true`.
- Starting inventory: `{wheat_bushel: 10, coin: 0}`.
- Stamps tile `building_id = "bandit_camp"` (visual: rough shelters).
- Erases `region_grids` cache for the tile.
- Registers `PropertyCore` ownership as `"camp:<settlement_id>"`.
- Adds to `player.ownership_refs`.

### Camp Name Generation
Random combination of 7 prefixes × 6 suffixes (42 possibilities):
- Prefixes: Raven's, Grey, Iron, Warden's, Old, Black, River
- Suffixes: Camp, Post, Encampment, Outpost, Refuge, Hold

### Income
Each production pulse: `PropertyCore` transfers 40% of the camp's `coin` inventory to the player.

### Design Notes
- Only one camp limitation: not enforced — player can found multiple camps.
- Camp has no workers/production — it is purely a storage/income base.
- Camp food is consumed by followers (FollowerSystem reads camp inventory for wheat).
- No dismantling or upgrading mechanic yet.

---

## 13. Player Trade — PlayerTrade

### Purpose
Direct buy/sell transactions between the player and a settlement's inventory.

### Buy
1. Check settlement has the good in inventory.
2. Price = `ss.prices[good_id]` adjusted by player's `trading_skill`.
3. Player coin deducted, good added to `player.carried_items`.
4. Settlement inventory reduced.

### Sell
1. Player must have the good in `carried_items`.
2. Sell price = `ss.prices[good_id]` with trading skill margin.
3. Good removed from `player.carried_items`, coin credited.

### Trading Skill Price Modifier
- Each level of player's `trading` skill adjusts buy/sell price by `±0.5% per level`.
- Maximum discount/premium: `±30%` (cap).

### Design Notes
- No buy/sell order or quantity negotiation — all trades are immediate at market price.
- Trading skill levels up via `WorkSystem` when player is assigned to a trade labor slot.
- No faction-based price modifiers yet.

---

## 14. Follower System & Group Management

### Purpose
Tracks the player's hired followers, manages their wages and food, applies morale effects, and handles desertion.

### `FollowerSystem`

**Tick hook:** `PRODUCTION_PULSE` (strategic cadence)

**Each tick:**
1. **Wages** — deduct `group.pay_per_tick × STRATEGIC_CADENCE` from `player.coin` for each member.
2. **Food** — consume `0.015 wheat_bushel` per follower per tick. Sources in order: camp inventory → player's `carried_items`.
3. **Morale effects:**
   - If wages paid: no penalty.
   - If wages not paid: `morale -= MORALE_DECAY_WAGES = 0.06` per tick.
   - If food not available: `morale -= MORALE_DECAY_FOOD = 0.04` per tick.
   - Passive recovery: `morale += MORALE_RECOVER_RATE = 0.03` per tick (always).
4. **Desertion** — if `group.morale < DESERTION_THRESHOLD = 0.20`: remove `member_ids[0]` (earliest follower deserts).

### `GroupState` Fields
| Field | Notes |
|---|---|
| `member_ids` | Array of follower person_ids |
| `morale` | 0.0–1.0 |
| `pay_per_tick` | Total wages per tick across all members |
| `formation_order` | String label |

### `GroupState` Constants
| Constant | Value |
|---|---|
| `MORALE_RECOVER_RATE` | `0.03` |
| `MORALE_DECAY_WAGES` | `0.06` |
| `MORALE_DECAY_FOOD` | `0.04` |
| `DESERTION_THRESHOLD` | `0.20` |

### `recalculate_pay(ws)`
- Each member's wage = max of their combat skills (sword, spear, axe, club fighting) or smithing skill level.
- Base pay = `max_combat_skill × 0.5 + 0.5`.

### Design Notes
- Food consumption (`0.015 wheat/tick`) equals ~540 wheat for 10 followers over 1 season (90 ticks) — food security is important for larger groups.
- No partial food allocation — no food = morale penalty immediately.
- Desertion removes the first follower (FIFO), not the lowest morale one.
- No follower equipment management yet; followers fight with their PersonState weapon.

---

## 15. Recruitment — RecruitmentManager

### Purpose
Handles player attempts to hire NPCs as followers via the dialogue system.

### Acceptance Requirements
| Condition | Requirement |
|---|---|
| Wage | `offered_wage >= class_min_wage × 1.2` (NPC minimum from population_class data); absolute floor `0.5 coin/tick` |
| Reputation (settlement) | `player.reputation[npc.home_settlement_id] >= 0.0` |
| Reputation (faction) | `player.reputation[settlement.faction_id] >= 0.0` (if settlement has a faction) |
| Already employed | NPC must not be in role `guard`, `laborer`, or `assistant` |

### On Success
1. `player.follower_ids.append(npc_id)`.
2. `npc.active_role = _role_for_class(npc.population_class)` (guard / laborer / assistant).
3. Free any held labor slot in NPC's home settlement.
4. `GroupState.recalculate_pay`.
5. `ReputationEvents.gain("contract_fulfilled", [home_settlement_id])`.

### `get_recruitable(player, settlement_id, ws)` API
Returns all NPCs in a settlement who are idle (not already in guard/laborer/assistant role) and are not already following the player. Format: `[{person_id, name, population_class, min_wage}]`.

### Design Notes
- Minimum wage is multiplied by 1.2 — NPCs want you to slightly overpay as a "risk premium".
- Reputation requirement is 0.0 (neutral), not positive — even neutral standing is enough.
- Once recruited, NPCs leave their home settlement's labor pool permanently (no temporary hire).

---

## 16. Reputation — ReputationEvents

### Purpose
Tracks player standing with settlements and factions. Affects recruitment, trade, and dialogue options.

### Bounds
`REP_MIN = -1.0`, `REP_MAX = 1.0`

### Event Deltas
| Event | Delta | Trigger |
|---|---|---|
| `enemy_defeated` | `+0.05` | Winning a combat |
| `trade_completed` | `+0.02` | Completing a buy/sell or building purchase |
| `contract_fulfilled` | `+0.03` | Recruiting an NPC (paid correctly) |
| `crime_detected` | `−0.08` | Caught committing a crime |
| `npc_killed` | `−0.10` | Killing an NPC |
| `wages_defaulted` | `−0.06` | Failing to pay follower wages |

### API
```
ReputationEvents.gain(player, event_id, entity_ids: Array)
ReputationEvents.lose(player, event_id, entity_ids: Array)
ReputationEvents.get_rep(player, entity_id) → float
ReputationEvents.meets_threshold(player, entity_id, threshold) → bool
```

### Design Notes
- Reputation is stored per entity (settlement or faction), not globally.
- No decay over time — reputation changes are permanent unless reversed by actions.
- Current system has no "rumour spreading" — killing someone in City A doesn't affect City B's rep.

---

## 17. Character Needs — NeedsSystem

### Purpose
Simulates hunger and fatigue for the player character every tick.

**Tick hook:** `MOVEMENT` (every tick, not just strategic)

### Constants
| Constant | Value | Notes |
|---|---|---|
| `HUNGER_RATE` | `0.0008/tick` | 1.0 in ~1,250 ticks ("one game day") |
| `FATIGUE_DECAY` | `0.0006/tick` | Rate while working |
| `FATIGUE_REST` | `−0.0006/tick` | Recovery while idle |
| `SHELTER_BONUS` | `−0.0020/tick` | Extra fatigue recovery in shelter |
| `INN_RENT_PER_DAY` | `2.0 coin` | Deducted once per `TICKS_PER_DAY` |
| `TICKS_PER_DAY` | `24` | Must match NpcScheduleSystem |

### Hunger
- Rises at a flat `HUNGER_RATE` per tick regardless of activity.
- Above 50%: work XP gain penalised (in WorkSystem).
- No eating mechanic implemented yet — hunger only resets via food items (deferred).

### Fatigue
- Increases while `player.active_role != ""` (assigned to work).
- Decreases while idle.
- If `player.shelter_status != ""` (rented inn room), recovery is accelerated by `SHELTER_BONUS`.

### Inn Rent
- Deducted once per `TICKS_PER_DAY = 24` ticks when `shelter_status == "rented"`.
- If player cannot afford rent: `shelter_status = ""` (evicted).

### Design Notes
- Temperature stress deferred to Phase 4–5.
- No illness system yet.
- Eating food items to reduce hunger not yet implemented (hunger only accumulates).
- NPC followers do not have individual needs tracked — only group-level food consumption.

---

## 18. Work & Wages — WorkSystem

### Purpose
Pays wages to all working characters and awards skill XP each production pulse.

**Tick hook:** `PRODUCTION_PULSE` (strategic cadence)

### Constants
| Constant | Value | Notes |
|---|---|---|
| `XP_PER_TICK` | `5.0` | Base XP per work tick; with `xp_per_level = 100` this is 0.05 progress per day → 20 days to level 1 |

### Per-Worker Tick
1. Find worker's home settlement.
2. Find matching labor slot (by `slot_id` + `worker_id`, then by `slot_id` alone as fallback).
3. If `settlement.coin < wage`: skip. No debt system yet.
4. Deduct `wage_per_day` from settlement coin; add to `person.coin`.
5. Award `XP_PER_TICK × xp_mult` to `skill_required`.
   - `xp_mult = 1.0 - clamp(hunger - 0.5, 0.0, 0.5)` — hunger above 50% reduces XP.

### Player Slot Assignment API
```
WorkSystem.assign_player_to_slot(slot_index) → bool
WorkSystem.remove_player_from_slot()
```

### Design Notes
- If multiple workers share the same slot_id, lookup finds the first match — slots should have unique IDs.
- NPC characters in `world_state.characters` are processed in addition to the player.
- Skills currently on the PersonState: `sword_fighting`, `spear_fighting`, `axe_fighting`, `club_fighting`, `smithing`, `farming`, `trading`, `woodcutting`.

---

## 19. NPC Pool & Schedules

### `NpcPoolManager`
- Maintains a pool of NPC `PersonState` objects for settlements.
- NPCs are generated on demand when the player visits a settlement.
- Pool size scales with settlement population.

### `NpcScheduleSystem`
- `TICKS_PER_DAY = 24` ticks per in-game day.
- NPCs follow a schedule: work during day, rest at night.
- Schedule determines `active_role` and `work_cell_id` changes.

### Design Notes
- Only NPCs in `world_state.characters` are fully simulated; pool NPCs are lightweight placeholders.
- NPC persistence vs. pool lifecycle: NPCs recruited as followers are moved into `world_state.characters` and become fully persistent.

---

## 20. Combat System

### Overview
WEGO (all orders simultaneous, then resolve) tactical combat on a tile grid. Works in phases: planning → resolving → results.

### `BattleState`
Top-level container. Fields:
| Field | Notes |
|---|---|
| `battle_id` | Unique ID |
| `map_type` | `"subregion"` (outdoor) or `"local"` (building interior) |
| `map_tile` | World tile key (for subregion maps) |
| `map_building_id` | Building ID (for local maps) |
| `phase` | `"planning"` / `"resolving"` / `"results"` |
| `turn` | WEGO turn counter |
| `combatants` | `combatant_id → CombatantState` |
| `formations` | `formation_id → FormationState` |
| `result` | `"player_victory"` / `"player_defeat"` / `"draw"` / `""` |
| `loot_pool` | Items from defeated combatants |

### `FormationState`
Squad-level unit (1 to N combatants). Players issue orders at formation level.

**Orders:** `advance`, `hold`, `charge`, `flank`, `retreat`

**Morale**
| Event | Morale change |
|---|---|
| Member killed | `−0.10` |
| Member shocked | `−0.05` |
| Rout threshold | `0.20` → auto-retreat |

### `CombatantState` (individual soldier)
Key fields: `tile_pos`, `z_level`, `melee_skill`, `stamina`, `health`, `bleed`, `pain`, `is_dead`, `is_incapacitated`, `team_id`, `equipped_weapon_id`, `equipped_armor`.

### `CombatResolver` — WEGO Turn Order
Each `resolve_turn(battle, map_data, rng)` call:
1. **Propagate orders** — AI decides orders for enemy formations.
2. **Move** — all formations move simultaneously based on orders:
   - `advance` / `charge` / `flank`: step toward nearest enemy anchor.
   - `retreat`: step away from nearest enemy anchor.
3. **Attack snapshot** — record all current positions for simultaneous resolution.
4. **Attack resolution** — each combatant finds a target in range, rolls to hit, deals damage.
5. **End-of-turn ticks** — bleed damage, stamina recovery.
6. **Formation morale** — update; check for rout.
7. **Evaluate result** — check win/loss conditions; advance turn counter.

### Hit Chance Formula
```
hit_chance = BASE_HIT_CHANCE (0.65)
           + melee_skill × SKILL_HIT_BONUS (0.02)
           − LOW_STAMINA_PENALTY (0.20) if stamina < 0.30
           + MASTERWORK_HIT_BONUS (0.05) if weapon is masterwork
           + COHESION_BONUS (0.10) if adjacent ally present
           + ELEVATION_BONUS (0.15) if attacker z_level > target
           − ELEVATION_BONUS (0.15) if attacker z_level < target
```
Max effective hit chance capped at `0.95`.

### Reach / Weapon Classes
| Reach class | Max tiles (Chebyshev distance) |
|---|---|
| `short` | 1 |
| `medium` | 1 |
| `long` | 2 |
| `ranged` | 6 |

### Damage & Status Effects
- Hit: roll damage from weapon data; apply to `health`, `stamina`.
- **Severity levels** determine bleed rate and pain applied per hit.
- `tick_bleed()`: applies bleed damage every end-of-turn tick.
- `tick_stamina_recovery()`: restores stamina passively.
- Death: `is_dead = true` when `health <= 0`.
- Incapacitation: `is_incapacitated = true` below a threshold (bleeding out).

### Battle End Conditions
- `team_destroyed("player")` → `player_defeat`
- `team_destroyed("enemy")` → `player_victory`
- Both → `draw`
- A formation is "destroyed" when all members are `is_dead` or `is_incapacitated`.

### Design Notes
- Formation size is 1 combatant in Phase 4. Multi-member formations (10–50) planned for large battles.
- AI order selection for enemy formations is currently random / predetermined — no tactical AI.
- Loot system tracks `loot_pool` but UI for collecting loot post-battle not yet implemented.
- No ranged weapon special mechanics (cover, ammo count) beyond reach distance.
- `attack_speed > 1` allows multiple attacks per turn (fast weapons).

---

## 21. World Generation

### `SubRegionGenerator`

**Purpose:** Lazily generates a 250×250 tile region grid for one world tile when the player first visits. Cached in `WorldState.region_grids[wt_key]`.

**Grid:** 250×250 cells, keyed `"rx,ry"` (0–249 each axis). Centre = (125, 125).

**Terrain variation:** Gentle FastNoiseLite noise maps world tile terrain to local variants.

| Parent terrain | Local variants |
|---|---|
| plains | plains (×3), coast |
| forest | forest (×2), plains |
| hills | hills (×2), plains |
| mountain | mountain, hills |
| river | river, coast |
| coast | coast (×2), shallow_water |
| desert | desert (×2), plains |
| tundra | tundra (×2), plains |
| ocean / lake | uniform |

**RNG seed:** `world_seed XOR (wtx × 73856093) XOR (wty × 19349663)` — deterministic per tile.

**Roads:** Stamped only toward neighbouring tiles that also have `has_road = true`.

**Buildings:** Clustered around centre (125, 125) with `BUILDING_SPREAD` spacing, using the building's world-tile offset as guide. Buildings under construction show scaffolding (`construction_job_id` present).

### `BuildingPlacer` (worldgen)
- Places initial buildings on settlement tiles during world generation.
- Uses building definitions from `data/buildings/` to determine which buildings appear per tier.

### `WorldAudit`
- Validates world state integrity after generation (tile ownership, route connections, faction coverage).

### Design Notes
- Region grids are discarded and regenerated when a building is completed or a camp is founded.
- No interior layouts generated by `SubRegionGenerator` — interiors use `local_layout` from building data.
- Terrain variants are purely visual; fertility comes from the parent tile's `prosperity` value.

---

## 22. UI Layer

### `SettlementView`
Primary gameplay screen. Player interacts with the current world tile, works, shops, recruits, and owns buildings.

**Key Panel Areas:**
- **World info** — settlement name, tier, faction, prosperity/unrest bars.
- **Inventory** — player coin, carried items list.
- **Labor slots** — list of available jobs; player can assign/unassign.
- **Characters** — NPCs present; interact/recruit buttons.
- **Interact button** — context-sensitive; changes label based on tile type:
  - Settlement tiles → `"Enter <name>"`.
  - Open land (outside territory) → `"⛺ Found camp here (5× timber_log)"`.
  - Camp tile → camp management.

**Phase 5 Dialogue Actions (in settlement dialogue):**
- Buy goods from settlement inventory.
- Sell goods from player's carried items.
- Recruit an NPC as a follower (with wage offer).
- Purchase a production building.

**Ledger button** → pushes `OwnershipView` scene.

### `OwnershipView`
Displays all property owned by the player:
- Buildings owned with location and income.
- Camps with coin pool and income.
- Total income summary.

### `CombatView`
Tactical battle screen. Player selects formation, issues `advance/hold/charge/flank/retreat` orders, then confirms to trigger WEGO resolution. Displays health bars, morale bars, turn counter, and battle result.

### `LocalView`
Interior view for buildings. Shows local layout grid. Used for indoor combat maps and building interiors.

### `WorldView`
Top-level world map. Displays world tiles, settlement icons, trade party movement, and player position. Entry point for navigating to `SettlementView`.

### Main Menu / Character Creation
- Version label: `"pre-alpha · Phase 5"`.
- Character creation: players choose a background, which sets starting coin, starting items, and skill bonuses.
- Backgrounds: Farmer, Soldier, Merchant, Wanderer, Hedge Scholar.

---

## 23. Starting Profiles — Backgrounds

All values set in `data/backgrounds/<id>.json`. Loaded and applied in `character_creation_screen.gd → _on_confirm()`.

| Background | Starting Coin | Starting Items | Intended Role |
|---|---|---|---|
| Farmer | 8 | wheat_bushel ×2, iron_tools ×1 | Agricultural work, land purchase |
| Soldier | 20 | short_sword ×1, gambeson ×1 | Combat, mercenary work |
| Merchant | 50 | cloth_bolt ×2, iron_tools ×1 | Trading, building purchase |
| Wanderer | 10 | wheat_bushel ×2 | Exploration, flexibility |
| Hedge Scholar | 15 | wheat_bushel ×1, cloth_bolt ×1 | Skills, crafting |

### Background Schema Fields
| Field | Type | Description |
|---|---|---|
| `id` | string | Internal identifier |
| `name` | string | Display name |
| `description` | string | Flavour text |
| `skill_bonuses` | object | `skill_id → level bonus` |
| `starting_coin` | number (≥0) | Coin at game start |
| `starting_items` | array of strings | Item IDs in initial `carried_items` |

### Design Notes
- `starting_items` are raw item IDs. If an item doesn't exist in ContentRegistry, it is silently ignored.
- Skill bonuses are additive on top of the base character level 0.
- No starting reputation modifiers per background yet (e.g. Soldier could start with army faction rep).

---

*Last updated: Phase 5 implementation complete. All systems described reflect the current codebase state.*
