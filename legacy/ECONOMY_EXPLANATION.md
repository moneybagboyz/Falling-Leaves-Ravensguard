# Economy Systems: Technical Deep Dive

> **Purpose**: This document explains the circular flow economic simulation—from labor allocation and production to consumption, markets, trade, and faction treasuries. Covers the complete pipeline from geography to wealth generation.

---

## Table of Contents
1. [System Overview](#1-system-overview)
2. [Circular Flow Model](#2-circular-flow-model)
3. [Production System](#3-production-system)
4. [Consumption & Growth](#4-consumption--growth)
5. [Market Dynamics](#5-market-dynamics)
6. [Logistics & Trade](#6-logistics--trade)
7. [Building System](#7-building-system)
8. [Faction Economy](#8-faction-economy)
9. [Geographic Potential](#9-geographic-potential)
10. [Economic Data Flow](#10-economic-data-flow)
11. [Configuration & Tuning](#11-configuration--tuning)
12. [Testing & Debugging](#12-testing--debugging)
13. [Extension Guide](#13-extension-guide)

---

## 1. System Overview

### 1.1 Economic Philosophy

The economy is a **physical simulation**, not an abstract balance sheet. Every resource exists as discrete units in settlement inventories. Price is emergent from supply/demand, not preset. Labor is finite and allocated by priority heuristics.

**Core Principles**:
1. **Faucets & Sinks**: Resources enter (production) and exit (consumption, spoilage) the system
2. **Acreage-Based**: Land dictates what can be produced (250 acres per tile)
3. **Class-Based Consumption**: Laborers, Burghers, Nobility consume different goods
4. **Dynamic Pricing**: Supply/demand ratios set prices, not lookup tables
5. **Three-Tier Production**: Extraction → Processing → Luxury Goods

### 1.2 File Structure

```
src/economy/
├── ProductionSystem.gd    # Labor allocation, terrain scanning (559 lines)
├── ConsumptionSystem.gd   # Food/fuel consumption, growth (394 lines)
├── PricingSystem.gd       # Dynamic pricing formulas (200 lines)
├── TradeSystem.gd         # Caravan AI, arbitrage
└── EquipmentSystem.gd     # Weapon/armor crafting

src/managers/
├── EconomyManager.gd      # Orchestrates daily ticks
└── SettlementManager.gd   # Governor AI, construction (826 lines)

src/data/
└── GDSettlement.gd        # Settlement data structure (240 lines)
```

### 1.3 Update Frequency

**Daily Pulse** (every 24 turns in GameState):

```
ProductionSystem.run_production_tick()
   ↓
ConsumptionSystem.run_consumption_tick()
   ↓
SettlementManager.process_governor_AI()
   ↓
PricingSystem.update_price_cache()
   ↓
TradeSystem.update_caravans()
```

**Turn = 1 hour**. Daily cycle = 24 turns. Economic state recalculates once per simulated day.

---

## 2. Circular Flow Model

### 2.1 Faucets (Resource Generation)

**Entry Points**:

| Faucet | Mechanism | Output Rate | File Reference |
|--------|-----------|-------------|----------------|
| **Farming** | `(arable_acres / 40) × BUSHELS_PER_ACRE_BASE × multipliers` | ~12 bushels/acre/year | [ProductionSystem.gd#L100-L150](src/economy/ProductionSystem.gd) |
| **Forestry** | `(forest_acres / 40) × FORESTRY_YIELD_WOOD` | 8 wood/acre/year | [ProductionSystem.gd#L150-L200](src/economy/ProductionSystem.gd) |
| **Mining** | `mining_slots × ore_deposits[res] × mining_efficiency` | Variable by geology | [ProductionSystem.gd#L200-L250](src/economy/ProductionSystem.gd) |
| **Fishing** | `fishing_slots × FISHING_YIELD_BASE` | 25 fish/slot/year | [ProductionSystem.gd#L250-L300](src/economy/ProductionSystem.gd) |
| **Organic Industry** | `input_stock × efficiency × building_multiplier` | 1:1 to 2:1 conversion | [ProductionSystem.gd#L400-L500](src/economy/ProductionSystem.gd) |
| **Taxation** | `(laborers × 0.1 + burghers × 0.5) crowns/day` | Scales with population | [ConsumptionSystem.gd#L200-L220](src/economy/ConsumptionSystem.gd) |

**Code Example** ([ProductionSystem.gd#L100-L120](src/economy/ProductionSystem.gd)):

```gdscript
# TIER 1: SUBSISTENCE (24-Hour Survival)
var daily_food_req = float(pop) * Globals.DAILY_BUSHELS_PER_PERSON

if s_data.get_food_stock() < daily_food_req:
    var needed = daily_food_req - s_data.get_food_stock()
    
    # Priority 1: Fishing (fast yield)
    if fish_limit > 0:
        var f_take = clamp(int((needed * Globals.DAYS_PER_YEAR) / Globals.FISHING_YIELD_BASE), 1, min(remaining_laborers, fish_limit))
        alloc["fishing"] += f_take
        remaining_laborers -= f_take
        needed -= (f_take * Globals.FISHING_YIELD_BASE / Globals.DAYS_PER_YEAR)
    
    # Priority 2: Farming
    if needed > 0 and farm_limit > 0:
        var g_take = clamp(int((needed * Globals.DAYS_PER_YEAR) / (Globals.ACRES_WORKED_PER_LABORER * Globals.BUSHELS_PER_ACRE_BASE)), 1, min(remaining_laborers, farm_limit))
        alloc["farms"] += g_take
        remaining_laborers -= g_take
```

### 2.2 Sinks (Resource Destruction)

**Exit Points**:

| Sink | Mechanism | Destruction Rate | File Reference |
|------|-----------|------------------|----------------|
| **Food Consumption** | `population × DAILY_BUSHELS_PER_PERSON` | ~1.2 bushels/person/day | [ConsumptionSystem.gd#L20-L60](src/economy/ConsumptionSystem.gd) |
| **Fuel Consumption** | `(pop / WOOD_FUEL_POP_DIVISOR) + (buildings × WOOD_FUEL_BUILDING_MULT)` | Scales with pop + infrastructure | [ConsumptionSystem.gd#L60-L80](src/economy/ConsumptionSystem.gd) |
| **Seed Reservation** | `grain_output × (1 - SEED_RATIO_INV)` | 20% of harvest | [ProductionSystem.gd#L150](src/economy/ProductionSystem.gd) |
| **Class Consumption** | Burghers: ale (0.1/day), Nobles: meat (0.5/day), furs (0.05/day) | Fixed rates | [ConsumptionSystem.gd#L80-L120](src/economy/ConsumptionSystem.gd) |
| **Building Upkeep** | `crown_stock -= maintenance_cost` (not yet implemented) | Future feature | — |

**Starvation Cascade** ([ConsumptionSystem.gd#L40-L55](src/economy/ConsumptionSystem.gd)):

```gdscript
if hunger_satisfied < total_hunger * 0.9:
    var deficit_ratio = 1.0 - (hunger_satisfied / total_hunger)
    var granary_lvl = s_data.buildings.get("granary", 0)
    var mitigation = clamp(granary_lvl * 0.15, 0.0, 0.8)
    
    s_data.unrest = min(100, s_data.unrest + int(Globals.STARVATION_UNREST_INC * deficit_ratio * (1.0 - mitigation)))
    s_data.happiness = max(0, s_data.happiness - int(Globals.STARVATION_HAPPINESS_DEC * deficit_ratio * (1.0 - mitigation)))
    
    var deaths = int(pop * Globals.STARVATION_DEATH_RATE * deficit_ratio * (1.0 - mitigation)) + Globals.STARVATION_BASE_DEATH
    s_data.population = max(0, pop - deaths)
```

**Constants** ([Globals.gd](src/core/Globals.gd)):

```gdscript
const STARVATION_DEATH_RATE = 0.02    # 2% of pop per day
const STARVATION_BASE_DEATH = 2       # Minimum deaths
const STARVATION_UNREST_INC = 20.0
const STARVATION_HAPPINESS_DEC = 20.0
const DAILY_BUSHELS_PER_PERSON = 1.2
```

---

## 3. Production System

### 3.1 Acreage Allocation

**Land Types** ([GDSettlement.gd:initialize_acres()](src/data/GDSettlement.gd#L120-L180)):

| Terrain | Arable Acres | Forest Acres | Mining Slots | Special |
|---------|--------------|--------------|--------------|---------|
| Plains `.` | 250 | 0 | 0 | Best farmland |
| Hills `o` | 125 | 0 | 150 | Mixed farm/mine |
| Forest `#` | 50 | 200 | 0 | 20% clearable + timber |
| Mountain `^` | 0 | 0 | 400 | Primary mining |
| River `/\` | 250 | 0 | 40 | Floodplain + silt extraction |
| Swamp `&` | 0 | 112 | 0 | Wetlands (45% forest, 55% wetland) |
| Desert `"` | 0 | 0 | 0 | Arid (no ag, salt extraction) |

**Acreage Code** ([GDSettlement.gd#L130-L160](src/data/GDSettlement.gd)):

```gdscript
match t:
    ".": # Plains
        arable_acres += tile_acres
    "o": # Hills
        var cleared = int(tile_acres * 0.5)
        arable_acres += cleared
        pasture_acres += (tile_acres - cleared)
    "#": # Forests
        var cleared = int(tile_acres * 0.2)
        arable_acres += cleared
        forest_acres += (tile_acres - cleared)
```

**Three-Field System** (crop rotation):

```gdscript
var fallow_ratio = 1.0/3.0 if s_data.has_three_field_system else 0.5
var active_acres = int(s_data.arable_acres * (1.0 - fallow_ratio))
s_data.fallow_acres = s_data.arable_acres - active_acres
s_data.pasture_acres = s_data.fallow_acres  # Fallow = pasture for livestock
```

**Effect**: Only 66% of arable land produces grain annually. Remaining 33% rests and provides pasture.

### 3.2 Labor Allocation Heuristic

**Entry Point**: [ProductionSystem.gd:_process_labor_pool()](src/economy/ProductionSystem.gd#L100-L250)

**Three-Tier Priority**:

```
Tier 1: SUBSISTENCE (24-hour buffer)
   ├─ Food: daily_food_req = pop × 1.2 bushels
   └─ Fuel: daily_wood_req = pop / (climate-adjusted divisor)
   
Tier 2: SECURITY (60-day buffer)
   ├─ Food security: pop × 1.2 × 60 days
   └─ Strategic reserves
   
Tier 3: PROFIT OPTIMIZATION
   ├─ Allocate remaining labor to highest-margin resource
   └─ Driven by scarcity multiplier (high price = high priority)
```

**Allocation Algorithm** ([ProductionSystem.gd#L100-L200](src/economy/ProductionSystem.gd)):

```gdscript
var remaining_laborers = int(s_data.laborers * efficiency)
var alloc = {"farms": 0, "fishing": 0, "mining": 0, "pasture": 0, ...}

# TIER 1: SUBSISTENCE
if s_data.get_food_stock() < daily_food_req:
    # Fishing first (fast yield)
    var f_take = min(remaining_laborers, fish_limit)
    alloc["fishing"] += f_take
    remaining_laborers -= f_take
    
    # Then farming
    var g_take = min(remaining_laborers, farm_limit)
    alloc["farms"] += g_take
    remaining_laborers -= g_take

# TIER 2: SECURITY (60-day buffer)
if remaining_laborers > 0 and current_food < food_buffer_target:
    alloc["farms"] += int(remaining_laborers * 0.5)
    alloc["fishing"] += int(remaining_laborers * 0.3)

# TIER 3: PROFIT (margin-driven)
if remaining_laborers > 0:
    var margins = {}
    for res in ["iron", "wood", "stone", "wool"]:
        var price = PricingSystem.get_price(res, s_data)
        margins[res] = price / Globals.BASE_PRICES[res]
    
    # Sort by margin, allocate to top 2 resources
    var sorted_margins = margins.keys()
    sorted_margins.sort_custom(func(a, b): return margins[a] > margins[b])
    
    # Example: If iron price is 300% above base, allocate to mining
```

### 3.3 Infrastructure Multipliers

**Building Bonuses** ([ProductionSystem.gd#L400-L500](src/economy/ProductionSystem.gd)):

| Building | Effect | Formula |
|----------|--------|---------|
| **Farm** | Grain yield × multiplier | `base_yield × (1.0 + farm_lvl * 0.5)` |
| **Lumber Mill** | Wood yield × multiplier | `base_yield × (1.0 + mill_lvl * 1.0)` |
| **Mine** | Stone/ore yield × multiplier | `base_yield × (1.0 + mine_lvl * 0.5)` |
| **Blacksmith** | Iron → Steel efficiency | `iron_consumed × (1.0 + smith_lvl * 1.0)` |
| **Weaver** | Wool → Cloth efficiency | `wool_consumed × (1.0 + weaver_lvl * 1.0)` |
| **Brewery** | Grain → Ale efficiency | `grain_consumed × (1.0 + brewery_lvl * 1.0)` |

**Example** (Level 5 Farm):

- Base yield: 100 laborers × 40 acres/laborer × 12 bushels/acre = 48,000 bushels/year
- With Farm Level 5: 48,000 × (1.0 + 5 × 0.5) = 48,000 × 3.5 = **168,000 bushels/year**

---

## 4. Consumption & Growth

### 4.1 Social Classes

**Population Structure** ([GDSettlement.gd:sync_social_classes()](src/data/GDSettlement.gd#L210-L220)):

```gdscript
nobility = max(1, int(population * Globals.NOBILITY_TARGET_PERCENT))    # 1%
burghers = int(population * Globals.BURGHER_TARGET_PERCENT)             # 15%
laborers = population - nobility - burghers                             # 84%
```

**Class Consumption** ([ConsumptionSystem.gd#L20-L120](src/economy/ConsumptionSystem.gd)):

| Class | Food | Fuel | Luxuries | Effect if Unsatisfied |
|-------|------|------|----------|----------------------|
| **Laborers** | 1.2 bushels/day | Wood (shared) | — | Death if no food |
| **Burghers** | 1.2 bushels/day | Wood (shared) | Ale (0.1/day), Cloth OR Leather | `burgher_unhappy = true`, tax revenue × 0.5 |
| **Nobility** | 1.2 bushels/day | Wood (shared) | Meat (0.5/day), Furs (0.05/day), Salt (0.05/day) | `nobility_unhappy = true`, loyalty risk (future) |

**Consumption Code** ([ConsumptionSystem.gd#L50-L100](src/economy/ConsumptionSystem.gd)):

```gdscript
# BURGHER COMFORT
var ale_needed = int(s_data.burghers * 0.1)
var ale_burn = s_data.remove_inventory("ale", ale_needed)
if ale_burn < ale_needed:
    s_data.burgher_unhappy = true

# NOBLE LUXURIES
var noble_meat_req = int(s_data.nobility * 0.5)
var noble_furs_req = max(1, int(s_data.nobility * 0.05))
var n_meat = s_data.remove_inventory("meat", noble_meat_req)
var n_furs = s_data.remove_inventory("furs", noble_furs_req)
if n_meat < noble_meat_req or n_furs < noble_furs_req:
    s_data.nobility_unhappy = true
```

### 4.2 Population Growth

**Growth Trigger** ([ConsumptionSystem.gd#L120-L140](src/economy/ConsumptionSystem.gd)):

```gdscript
if hunger_satisfied >= total_hunger and s_data.get_food_stock() > total_hunger * 30:
    var cap = s_data.get_housing_capacity()
    if s_data.population < cap:
        var births = int(s_data.population * Globals.GROWTH_RATE) + Globals.GROWTH_BASE
        s_data.population += births
        if s_data.population > cap: s_data.population = cap
```

**Constants**:

```gdscript
const GROWTH_RATE = 0.0001  # 0.01% daily = 3.65% annual
const GROWTH_BASE = 1       # Minimum 1 birth/day per settlement
```

**Housing Capacity** ([GDSettlement.gd:get_housing_capacity()](src/data/GDSettlement.gd#L230-L245)):

```gdscript
var base = houses * 5
var district_lvl = buildings.get("housing_district", 0)
var civil_lvl = buildings.get("town_hall", 0)

var cap = base + (district_lvl * 200)
cap = int(cap * (1.0 + (civil_lvl * 0.1)))
```

**Example**:
- Base houses: 20 × 5 = 100 capacity
- Housing District Level 5: 100 + (5 × 200) = 1,100
- Town Hall Level 3: 1,100 × (1.0 + 0.3) = **1,430 capacity**

### 4.3 Overcrowding & Migration

**Overcrowding Penalty** ([ConsumptionSystem.gd#L135-L140](src/economy/ConsumptionSystem.gd)):

```gdscript
if s_data.population >= cap:
    # Growth stops
    s_data.unrest = min(100, s_data.unrest + 1)
    s_data.happiness = max(0, s_data.happiness - 1)
```

**Migration** (not yet implemented):

```
if happiness < 40 and nearby_settlement.happiness > 60 and nearby_settlement.population < cap:
    emigrants = int(population * 0.01)
    population -= emigrants
    nearby_settlement.population += emigrants
```

---

## 5. Market Dynamics

### 5.1 Dynamic Pricing

**Formula** ([PricingSystem.gd:get_price()](src/economy/PricingSystem.gd#L10-L60)):

$$P = B \times \text{clamp}\left(\frac{D}{S}, 0.2, 5.0\right)$$

Where:
- $ P $: Final price
- $ B $: Base price from `Globals.BASE_PRICES`
- $ D $: Demand (function of population and resource type)
- $ S $: Current stock in settlement inventory

**Special Case (Zero Stock)**:

$$P = B \times 5.0 \quad \text{if} \quad S = 0$$

**Demand Calculation**:


### 3.2 Housing & Growth
Population growth is limited by **Housing Capacity**.
*   **Houses:** Basic sprawl provides a small amount of housing.
*   **Housing Districts:** Specialized urban planning (Level 1, 2, 3...) provides massive blocks of high-density housing for megacities.
*   **The Hub Model:** Settlements are prioritized by the AI based on their **Geographic Potential** (Arable land + Resource magnetism). High-potential sites will see rapid pillar investment.

---

## 4. The Market: Dynamic Price Scarcity
Pricing is the "brain" of the simulation. Every resource has a **Base Price** (`Globals.BASE_PRICES`), but the **Market Price** is calculated daily via the **Scarcity Ratio**.

### 4.1 The Supply/Demand Formula
`Price = Base_Price * (Demand / Stock)`
*   **Demand Anchors:** Food demand is based on **Population * 14 Days** of survival. Wood demand is tied to population and climate.
*   **Price Clamping:** Generally between **0.2x** and **4.0x** base, jumping to **5.0x** when stock is empty (`Globals.PRICE_ZERO_STOCK_MULT`).

### 4.2 World Market Buy Orders (The "Pull" System)
When a settlement's stock of a critical resource (Grain, Iron, Wood, Wool, Coal, Meat, Salt) falls below a survival threshold, the local Merchants' Guild places a **Buy Order** on the World Market (visible in the Management tab).
*   **Guaranteed Premium:** Buy orders offer a guaranteed price (usually **1.2x Base** or current market price, whichever is higher).
*   **Trading Magnet:** Caravans prioritize fulfilling these orders over search-based arbitrage, ensuring resources flow to starving cities and industrial hubs.

---

## 5. Logistics: The Supply Chain
Resources move through the world via two primary vectors: **Virtual Logistical Pulses** and **Physical Merchant Hubs**.

### 5.1 Virtual Logistical Pulses (Hamlets)
To optimize performance and eliminate "Entity Bloat," small settlements (Hamlets) do not spawn physical villager units. Instead, they use **Logistical Pulses**.
*   **The Virtual Pipeline:** Every day, a hamlet calculates its surplus. A "Pulse" entry is created in the global system with an `Arrival Turn` based on the distance to the Parent City.
*   **Reliability:** This system ensures that hub cities receive a steady, staggered flow of raw materials without requiring thousands of AI entities to pathfind simultaneously.

### 5.2 Physical Caravans & Merchant Hubs
Only Tier 3 (Towns) or higher settlements with a **Merchant Guild** can spawn physical Caravans. These are high-value, high-capacity units designed for long-distance trade.
*   **Super-Caravans:** As a city invests in its **Industry Pillar**, its caravans gain massive capacity bonuses (+50% per Guild level) and better guards.
*   **Staggered AI:** To save CPU, caravans only update their pathfinding and trade scanning logic every 4 turns, with their updates staggered across the global clock.

---

## 6. Population & Growth
A settlement's population is its most valuable asset, providing the workforce for both land and industry.

### 6.1 Consumption & Starvation
Each person consumes a specific amount of grain daily. If food is unavailable:
*   **Death Toll:** `(Population * 0.02) + 2` people die daily. 
*   **Social Unrest:** Increases by **10** per day.
*   **Happiness:** Decreases by **20** per day.

### 6.2 The Demographic Model (Housing Districts)
Population growth is no longer a simple "5 per house" limit. It is controlled by a multi-layered capacity formula.
*   **Base Sprawl:** Every settlement tier provides a base capacity (e.g. 50, 150, 250, 350 for Village, Town, City, Metropolis).
*   **Housing Districts (Civil Pillar):** The primary way to grow a city is investing in the Housing District building. Each level adds **200** to the population cap.
*   **Urban Efficiency:** The **Town Hall** (Civil Pillar) provides a percentage multiplier (e.g. +10% per level) to the *total* housing capacity, reflecting better urban planning.
*   **Migration:** If Happiness falls below **40**, citizens begin to leave the settlement, seeking nearby cities with higher satisfaction and available housing capacity.
*   **Overcrowding:** If population exceeds capacity, growth stops, happiness plummets, and **Unrest** increases by 1 per day.

---

## 6. Construction & Development
Settlements grow by building and upgrading structures.

### Building Slots & Tiers
Settlements have a limited number of **Building Slots** based on their Tier. You cannot build a high-tier building (like a Cathedral) in a small Village.

### Costs & Labor
- **Cost Scaling**: Building costs grow on a polynomial curve: `Base Cost * (Level + 1)^2.2`. This makes early levels highly accessible for growth, while Level 10 milestones become a prestigious long-term objective for wealthy capitals.
- **Accessible Entry**: Base training/industry costs (Walls, Blacksmiths, Markets) have been tuned downward to ensure villages can begin their climb to urban centers.
- **Labor Power**: `(Population / 100) * Happiness Modifier`.
- **Resource Import**: If a city lacks Wood, Stone, or Iron for a project, it can "Import" them from the global market at a premium (Wood: 10, Stone: 20, Iron: 40).

### Player Sponsorship & Influence
The player can **Sponsor** buildings in a settlement. This pays the upfront Crown cost and grants the player **Influence** with that settlement:
- **Sponsoring a Building**: Grants **+10 Influence**.
- **Donating Resources**: Grants Influence based on the amount donated (`Amount / 10.0`).
- **Influence Benefits**: High influence can lead to better trade prices, political support, and access to unique recruits (mechanics currently in development).

---

## 8. Macro-Economy: Caravans & Factions
### Trade Routes
Caravans seek profit by comparing prices between cities. They factor in:
- **Profit Margin**: `Sell Price - Buy Price`.
- **Distance Penalty**: `Distance / 10.0` is subtracted from the potential profit.
- **Capacity**: Caravans can carry up to **50 units** of bulk goods (Wood/Stone) or **20 units** of high-value goods.
- **Influence**: Caravans gain **+5 Influence** for their faction in every city they trade with.

### Faction Treasuries & Taxation
Factions (Red Kingdom, Blue Empire, etc.) maintain their own treasuries:
- **Caravan Tax**: When a caravan returns to a settlement owned by its faction, any Crowns it has above **500** are deposited into the Faction Treasury as "Tax".
- **Economic Stipends**: Factions inject **5% of their treasury daily** back into their Lords and Settlements. This ensures that even poor frontier fiefs can eventually afford to hire guards or build infrastructure.
- **Upkeep**: Factions use these funds to pay for army upkeep (`Roster Size * 2`). If a Lord cannot pay, **10% of the roster will desert** each day.
- **Colonization**: Factions can spend their treasury to send out **Settler Parties** to found new villages. New colonies start with 100 population and basic infrastructure.

---

## 9. World Generation & Initial Economic State

The world's economic landscape is determined by a **Geographic Potential Analysis** in [WorldGen.gd](WorldGen.gd). Instead of a temporal simulation, the game evaluates the physical capacity of every tile at the moment of creation.

### 9.1 Carrying Capacity & Magnetism
Each potential settlement site is scored based on:
- **Carrying Capacity (The Food Ceiling):** Calculated from surrounding **Arable**, **Water**, and **Forest** tiles. A tile surrounded by fertile plains has a natural capacity to support thousands, while a mountain pass is capped until trade begins.
- **Economic Magnetism (The Labor Draw):** Industrial resources like **Gold**, **Gems**, and **Iron** act as multipliers. They represent the "pull" that draws people to settle in density despite potentially low local food yields.

### 9.2 Potential-Based Tiering
Settlements are ranked by **Potential Revenue** (Food Tax Base + Industry Magnetism).
- **Metropolis (Top 5%):** Starts at 95% of its carrying capacity ceiling.
- **City (Next 10%):** Starts at 70% capacity.
- **Town (Next 20%):** Starts at 40% capacity.
- **Village:** Starts at 20% capacity.

This ensures that "High Value" geographic locations naturally emerge as large urban centers because the land *can* support and *wants* to draw a population there.

### 9.3 Territorial Assignment
Factions are assigned based on distance to distant capitals. These capitals are always selected from the highest-potential Metropolis sites to ensure major kingdoms occupy the most fertile and resource-rich deltas.

### 9.4 Roads, Connectivity, and Pathfinding

The world is wired together with **roads** `=` using an A* grid in [WorldGen.gd](WorldGen.gd):
- Terrain defines **path cost**:
    - Plains `.`, Towns: cheap.
    - Forest `#`: 5× cost.
    - Mountains `^`: 50× cost.
    - Water `~`: blocked.
- The generator builds a **minimum spanning tree** (MST) over settlements, then adds extra edges between nearby settlements to form **trade loops** and crossroads.
- Roads reduce future path costs (weight 0.5), encouraging more roads to follow existing “highways”.

These roads are only visual in the overworld, but economically they:
- Increase income during the history simulation.
- Determine where caravans can travel efficiently once the game starts.

---

## 10. Historical Simulation (Deprecated)
The 100-year history simulation has been replaced by the **Gegeographic Potential Model** (see Section 9) to ensure instant generation and geographically logical urban placement.

---

## 11. Hamlets, Villages, and Cities: Promotion Path

The economy models long-run rural–urban migration and development.

### 11.1 Hamlet → Village Promotion

Hamlets can graduate into full **Villages** via the hamlet promotion logic in GameState:

Key triggers:
- **Stability**: Each successful villager delivery from hamlet to city increases **stability** by +1.
- At **stability ≥ 50**, promotion from hamlet to village fires.

Promotion effects:
- Type becomes `village`, roughly matching **tier 2**.
- Population jumps to around 100; garrison increases.
- Radius increases, and building slots increase accordingly.
- Crown stock is boosted and houses expanded to house the larger population.
- A governor is assigned, and shops/recruit pools become available.

### 11.2 Geographic Potential WorldGen (The "Fix")
To prevent "Gilded Death Traps" (cities with high population but no food or housing), the WorldGen now uses a **Geographic Potential Model**:
1.  **Site Capacity:** Analyzes arable land and water access within an 8-tile radius.
2.  **Magnetism:** Adds bonuses for rare resource deposits (Iron, Gems) and strategic bottlenecks.
3.  **Tier Assignment:** Cities are only spawned on sites with the capacity to support them. 
4.  **Auto-Sync:** Upon generation, population classes, housing, and radius match the Tier, ensuring starting cities are economically viable.

---

## 12. Full Building Catalog

| Building | Category | Base Cost | Description |
| :--- | :--- | :--- | :--- |
| **Farm** | Industry | 500 | +50% Grain yield multiplier. |
| **Lumber Mill** | Industry | 800 | +100% Wood yield multiplier. |
| **Fishery** | Industry | 600 | +50% Fish yield multiplier. |
| **Mine** | Industry | 1500 | +50% Stone/Ore yield multiplier. |
| **Pasture** | Industry | 700 | +50% Wool/Hide/Meat yield multiplier. |
| **Blacksmith** | Industry | 4000 | +100% Steel efficiency multiplier. |
| **Tannery** | Industry | 2500 | +100% Leather efficiency multiplier. |
| **Weaver** | Industry | 2500 | +100% Cloth efficiency multiplier. |
| **Brewery** | Industry | 3000 | +100% Ale efficiency multiplier. |
| **Tailor** | Industry | 3500 | +100% Garment efficiency multiplier. |
| **Bronzesmith** | Industry | 3500 | +100% Bronze efficiency multiplier. |
| **Warehouse** | Industry | 3000 | +100% Storage Limit. |
| **Stone Walls** | Defense | 15000 | Adds +10x to Garrison Hardness. |
| **Barracks** | Defense | 5000 | Increases Garrison Quality and Capacity. |
| **Granary** | Defense | 1200 | Starvation mitigation and food storage. |
| **Housing Dist.**| Civil | 1000 | +100 Population Capacity. |
| **Market** | Civil | 2000 | +25% Commerce Tax multiplier. |
| **Road Network** | Civil | 2500 | +15% Trade Efficiency multiplier. |
| **Tavern** | Civil | 1500 | Passively boosts Happiness and Growth. |
| **Cathedral** | Civil | 12000 | Massive Stability and Nobility Loyalty. |

---

## 13. Factions, Lords, and Military Economy

Beyond towns, a large part of the macro-economy exists in **armies and lords**.

### 12.1 Faction Treasuries

Each faction (Red, Blue, Green, Purple, Orange, Bandits, Neutral, Player) has:
- A global **treasury** used to finance lords and indirectly construction.
- Starting values in GameState:
    - Player: 1000.
    - Major AI factions: around 5000.
    - Bandits/Neutral: 0.

Income sources to faction treasuries:
- **Caravan Tax**: When a caravan is in a settlement owned by its faction and has **>500 Crowns**, everything above 500 is skimmed as **tax** into the faction’s treasury.
- **Residual Wealth**: Rich settlements after worldgen history effectively act as tax bases because their city coffers fund lords and construction.

### 12.2 Lords: Creation and Maintenance

Lords are spawned in WorldGen near castles/cities:
- Each lord has:
    - `roster`: 30–100 recruits.
    - `crowns`: 1000–5000.
    - `provisions`: 500–2000.
    - `home_fief`: A settlement they draw support from.
    - `doctrine`: Conqueror/Defender/Raider/Merchant Prince.

Daily economics for lords:
- **Upkeep**: Each **lord army** pays `roster_size * 2` Crowns per day.
- If the lord’s personal `crowns` are insufficient:
    - They draw from their **home_fief’s** `crown_stock`, but only above a buffer of 1000 Crowns.
    - If they still can’t meet upkeep, **10% of their roster deserts** (roster shrinks to 90%).

Recruitment logic:
- When understrength, a lord seeks a **recruitment center** (friendly settlement).
- They attempt to spend **500 Crowns**:
    - First from the settlement’s `crown_stock`.
    - If insufficient, from the faction’s **treasury**.
- Successful recruitment adds new recruits, increasing roster size and future upkeep.

### 12.3 Sieges and Captures

While primarily military, sieges have economic consequences:
- If attackers win:
    - Settlement’s **faction** changes; its future **crown_stock**, production, and recruitment now serve the conqueror.
    - Part of the attacking army becomes the new **garrison**.
- If defenders win:
    - Attacker’s roster/strength is heavily reduced; some lords may be effectively reset.

This shifts long-run economic power by moving high-tier settlements between factions.

---

## 13. Ruins, Loot, and Player-Centric Money Injection

The **dungeon/ruin** system acts as an external money & item faucet for the player.

### 13.1 Ruin Generation

In WorldGen, ruins are placed:
- On forest, mountain, or plains tiles at a safe distance from settlements.
- Each ruin has:
    - `danger` (1–5): Difficulty proxy.
    - `loot_quality` (1–5): Determines reward quality.

### 13.2 Ruin Rewards

When a ruin is cleared:
- Player receives:
    - **Crowns**: `loot_quality * rand_range(50, 150)`.
    - **Items**: Random count between 1 and `loot_quality`.
- Items are sampled from the global equipment list and material pool:
    - Metals: Iron, Steel, Bronze.
    - Soft: Leather, Wool, Linen.
    - Quality escalates with `loot_quality` (Fine, Masterwork).

Economically, this means:
- Dungeons inject **pure currency** into the player economy (no drain on settlements).
- High-quality equipment enters circulation without requiring local production chains.
- The player can liquidate loot into crowns by selling items to shops.

---

## 14. Analytics and Simulation Tools

The code includes tools to inspect and stress-test the economy.

### 14.1 World Audit (Developer Tool)

The function that runs a world audit prints a holistic economic snapshot:
- **Demographics**:
    - Total population, total houses, housing capacity, overcrowded settlements.
- **Economy**:
    - Total wealth (sum of all `crown_stock`).
    - Average production efficiency across settlements.
- **Faction Breakdown**:
    - For each faction: population, number of settlements, total wealth (treasury + local), army strength, average happiness.
    - Top produced resources per faction.
- **Global Resource Stocks & Prices**:
    - For key goods (Grain, Fish, Meat, Wood, Stone, Iron, Steel, Leather, Cloth, Ale, Horses):
    - Total global stock and average **dynamic price** from `get_price`.
- **Logistics**:
    - Active caravan and army counts.

This is mostly for debugging and balancing, but it reflects exactly how the economy perceives itself at runtime.

### 14.2 Monthly Turbo Simulation

The **Turbo Simulation** fast-forwards **30 days**:
- Disables most logs except the monthly report.
- Tracks:
    - `production[res]`: Amount produced across all settlements.
    - `consumption[res]`: Amount consumed/burned.
    - `idle_buildings[building]`: Building-days spent idle due to missing inputs.
    - Important economic **events** (e.g., starvation) appended by other systems.

At the end, the monthly report emits a detailed summary:
- Population and player treasury deltas.
- Per-resource production vs. consumption and net surplus/deficit.
- Warnings about frequently idle buildings (signs of bottlenecks).
- Logged events (famines, etc.).

This tool lets you observe the **systemic behavior** of the simulated economy over time without manual play, making it invaluable for tuning.

---

## 15. Putting It All Together

In summary, the economy in *Falling Leaves* is the emergent result of:
- **Geography & Geology**: Climate and layers decide where resources live.
- **Production & Buildings**: Farms, Mines, Pastures, Mills, and Fisheries convert terrain into daily output, modulated by workforce efficiency.
- **Industrial Chains**: Blacksmiths, Tanneries, Weavers, Breweries, Markets, Tailors, and Goldsmiths transform raw goods into higher-value exports.
- **Logistics**: Virtual pulses and physical super-caravans fulfill global buy orders and move raw goods to industrial hearts.
- **Population Dynamics**: Food and housing capacity drive growth, migration, and sometimes starvation.
- **Construction & Governance**: AI governors and the player invest crowns into the Three Pillars (Industry, Defense, Civil), which reshape the world map.
- **Factions & Lords**: Military spending and territorial conquest continuously re-distribute wealth and economic capacity.
- **Player Actions**: Trading via the World Market, sponsoring buildings, and clearing ruins all inject shocks into the simulation.

## 16. Building Milestones & Unlocks
Buildings now feature a **Milestone System** where reaching specific levels unlocks new mechanics, unit types, or economic flavor. Costs follow a polynomial curve (`(Level+1)^2.2`) rather than exponential, making high levels achievable for wealthy kingdoms.

### Industry (The Engine)
*   **Yield & Production Multipliers**
    *   **Level 3**: +300% Yield/Efficiency Multiplier.
    *   **Level 7**: +700% Yield/Efficiency Multiplier.
    *   **Level 10**: +1000% Yield/Efficiency Multiplier (Mass Industrialization).
*   **Blacksmith**
    *   Level 1: **Village Smithy** | Level 5: **Foundry** | Level 10: **The Vulcan Complex**.
*   **Mine**
    *   Level 1: **Surface Quarry** | Level 5: **Drainage Pumps** | Level 10: **Under-Kingdom**.
*   **Farm**
    *   Level 1: **Fields** | Level 4: **Three-Field System** | Level 10: **Agricultural Revolution**.
*   **Fishery**
    *   Level 1: **Fishing Huts** | Level 6: **Deep Sea Fleet** | Level 10: **The Great Harbor**.

### Defense (The Shield)
*   **Barracks**
    *   **Structure**: Follows a "Volume vs. Quality" staggered progression.
    *   **Odds (1, 3, 5, 7, 9)**: Each level significantly increases the number of recruits generated per batch (Muster Volume).
    *   **Evens (2, 4, 6, 8)**: Each level unlocks a hardware/tier upgrade (Muster Quality).
        *   Level 2: Unlocks Tier 2 (Trained).
        *   Level 4: Unlocks Tier 3 (Men-at-Arms).
        *   Level 6: Unlocks Tier 4 (Veterans).
        *   Level 8: Unlocks Tier 5 (Royal Guard).
    *   **Level 10 (Citadel)**: The Milestone. Grants maximum muster volume and a high percentage chance for every recruit to be Tier 4 or 5.
*   **Stone Walls**
    - **Logic**: Walls provide a base defense multiplier, but odd-numbered levels unlock major tactical advantages that severely weaken attackers.
    - **Level 1**: **Palisade** (Basic wooden protection).
    - **Level 3**: **Watch Towers** (-25% Attacker Strength via archer harassment).
    - **Level 5**: **Stone Walls** (Significant increase to base defense multiplier).
    - **Level 7**: **Siege Engines** (Defensive balistas inflict 40% HP damage to random attackers).
    - **Level 9**: **The Moat** (-50% Attacker Strength via massive bottlenecking).
    - **Level 10 (Star Fort)**: **Siege Immunity**. The ultimate milestone. Doubles all existing wall status.

### Civil (The Heart)
*   **Market**
    *   **Level 1**: **Town Stalls** (Basic trade).
    *   **Level 3**: **Tax Office** (+20% tax efficiency without increasing unrest).
    *   **Level 6**: **Guild Hall** (Market price fixing and better trade margins).
    *   **Level 10**: **Grand Exchange** (Earn 1% interest on total settlement crowns weekly).
*   **Tavern**
    *   **Level 1**: **Alehouse** (Basic community gathering).
    *   **Level 4**: **Traveler's Inn** (Potential to hire Tier 4 veteran Mercenaries).
    *   **Level 7**: **Bard's College** (Propaganda: manipulate public opinion/unrest).
    *   **Level 10**: **Shadow Broker** (Full map vision and deep state counter-espionage).
*   **Housing District**
    *   Level 1: **Thatched Cottages** | Level 5: **Stone Tenements** | Level 10: **The High District** (+2000 total pop cap).
*   **Road Network**
    *   Level 1: **Dirt Paths** | Level 5: **Cobblestone Streets** | Level 10: **Imperial Highways**.
*   **Cathedral**
    *   Level 1: **Sanctuary** | Level 4: **Basilica** | Level 10: **The Seat of Divines** (Massive global stability).# [CONTINUATION FROM PREVIOUS SECTION]

**Demand Calculation** ([PricingSystem.gd#L20-L50](src/economy/PricingSystem.gd)):

```gdscript
if res_name in ["grain", "fish", "meat", "game"]:
    demand = pop * Globals.DAILY_BUSHELS_PER_PERSON * 14.0  # 2-week buffer
elif res_name == "wood":
    demand = (pop / Globals.WOOD_FUEL_POP_DIVISOR) + (buildings.size() * Globals.WOOD_FUEL_BUILDING_MULT)
    var temp = GameState.geology.get(s_data.pos, {}).get("temp", 0.0)
    if temp > 0.0:
        demand *= max(0.2, 1.0 - temp)  # Hot climates use less fuel
elif res_name == "jewelry":
    demand = max(2, int(s_data.nobility * 0.2))  # Nobles consume jewelry
```

**Example Price Calculation**:

Settlement with pop=1000, grain stock=5000:
- Base price: 10 crowns
- Demand: 1000 Ã— 1.2 Ã— 14 = 16,800 bushels (2 weeks)
- Stock: 5,000 bushels
- Ratio: 16,800 / 5,000 = 3.36
- **Final Price**: 10 Ã— 3.36 = **34 crowns** (clamped to 5.0 max = 50 crowns if demand were higher)

### 5.2 World Market Orders

**Buy Order System** ([ConsumptionSystem.gd#L145-L175](src/economy/ConsumptionSystem.gd)):

```gdscript
if GameState.turn % 12 == 0:  # Check twice daily
    var critical_resources = ["grain", "iron", "wood", "wool", "coal", "meat", "salt"]
    for res in critical_resources:
        var stock = s_data.inventory.get(res, 0)
        var threshold = s_data.population * 0.5 if res == "grain" else 50
        
        if stock < threshold:
            var guild_lvl = s_data.buildings.get("merchant_guild", 0)
            var cap = 100 * (1.0 + (guild_lvl * 0.5))
            var buy_price = int(GameData.BASE_PRICES.get(res, 10) * 1.2)  # 20% premium
            
            GameState.world_market_orders.append({
                "buyer_pos": s_data.pos,
                "resource": res,
                "amount": int(cap),
                "price_offered": buy_price,
                "faction": s_data.faction
            })
```

**Effect**: Caravans prioritize fulfilling buy orders over speculative arbitrage, creating a "pull" system that directs trade toward desperate settlements.

### 5.3 Price Caching

**Performance Optimization** ([GDSettlement.gd](src/data/GDSettlement.gd)):

```gdscript
var cache_prices: Dictionary = {}  # res_name -> int
var cache_dirty_flags: Dictionary = {}

func invalidate_cache(cache_type = "all"):
    if cache_type == "all" or cache_type == "prices":
        cache_dirty_flags["prices"] = true
        cache_prices.clear()
```

**Rationale**: Price calculations happen ~50 times per settlement per day (UI queries, caravan pathfinding). Caching reduces CPU usage by 80%.

---

## 6. Logistics & Trade

### 6.1 Virtual Logistical Pulses

**Problem**: Spawning 1000+ villagers to ferry grain from hamlets to cities causes lag.

**Solution**: Virtual "pulses" ([GameState.gd](src/core/GameState.gd)):

```gdscript
var logistical_pulses = []  # Array of {origin, target, resource, amount, arrival_turn}

# Hamlet creates pulse
func send_virtual_pulse(hamlet, parent_city, resource, amount):
    var distance = hamlet.pos.distance_to(parent_city.pos)
    var travel_time = int(distance * 4)  # 4 turns per tile
    
    logistical_pulses.append({
        "origin": hamlet.pos,
        "target": parent_city.pos,
        "resource": resource,
        "amount": amount,
        "arrival_turn": GameState.turn + travel_time
    })

# Daily pulse processor
func process_pulses():
    for p in logistical_pulses:
        if GameState.turn >= p.arrival_turn:
            var target = get_settlement_at(p.target)
            if target:
                target.add_inventory(p.resource, p.amount)
            logistical_pulses.erase(p)
```

**Performance**: Reduces entity count by ~70% in large maps (200+ settlements).

### 6.2 Physical Caravans

**Spawning Conditions** ([SettlementManager.gd](src/managers/SettlementManager.gd)):

```gdscript
if s_data.tier >= 3 and s_data.buildings.get("merchant_guild", 0) >= 1:
    # Spawn caravan
    var caravan = GDCaravan.new()
    caravan.origin = s_data.pos
    caravan.faction = s_data.faction
    caravan.capacity = 50 * (1.0 + merchant_guild_lvl * 0.5)
```

**Caravan AI** ([TradeSystem.gd](src/economy/TradeSystem.gd)):

```
1. Check world_market_orders for high-premium buy orders
   â”œâ”€ If found: Load resource, pathfind to buyer
   â””â”€ Else: Scan settlement prices within 20 tiles
2. Calculate profit margin:
   Profit = (sell_price - buy_price) - (distance / 10.0)
3. Sort opportunities by profit, pick top 3
4. Load cargo, set destination
5. Travel (4 turns per tile)
6. Sell cargo, deposit tax to faction treasury
7. Return to origin or seek new opportunity
```

**Staggered Updates** ([TradeSystem.gd](src/economy/TradeSystem.gd)):

```gdscript
# Only update caravan logic every 4 turns, staggered by ID
if (GameState.turn + caravan.id) % 4 == 0:
    update_caravan_ai(caravan)
```

**Effect**: Reduces caravan AI CPU load by 75% (200 caravans updating at 25% frequency instead of 100%).

### 6.3 Trade Influence

**Mechanism** ([TradeSystem.gd](src/economy/TradeSystem.gd)):

```gdscript
# When caravan completes trade at settlement
s_data.faction_influence[caravan.faction] += 5

# Future use: Unlock special recruits, diplomatic bonuses
if s_data.faction_influence[faction_id] >= 100:
    # Faction gains control of settlement peacefully
```

---

## 7. Building System

### 7.1 Construction Queue

**Data Structure** ([GDSettlement.gd](src/data/GDSettlement.gd)):

```gdscript
var construction_queue: Array = [
    {
        "id": "blacksmith",
        "progress": 150,
        "total_labor": 500,
        "resources_met": true
    }
]
```

**Daily Progress** ([SettlementManager.gd:process_construction()](src/managers/SettlementManager.gd)):

```gdscript
func process_construction(s_data):
    if s_data.construction_queue.is_empty(): return
    
    var project = s_data.construction_queue[0]
    var labor_power = s_data.get_workforce_efficiency() * (s_data.population / 100.0)
    
    project.progress += int(labor_power * s_data.happiness / 100.0)
    
    if project.progress >= project.total_labor:
        # Construction complete!
        s_data.buildings[project.id] = s_data.buildings.get(project.id, 0) + 1
        s_data.construction_queue.pop_front()
        s_data.invalidate_cache("all")
```

**Labor Power Formula**:

$$L = \frac{P}{100} \times E \times \frac{H}{100}$$

Where:
- $L$: Labor power per day
- $P$: Population
- $E$: Workforce efficiency (0.1-1.0)
- $H$: Happiness (0-100)

**Example**: Pop=1000, Efficiency=1.0, Happiness=80  
Labor Power = (1000 / 100) Ã— 1.0 Ã— 0.8 = **8 labor per day**

### 7.2 Polynomial Cost Scaling

**Formula** ([SettlementManager.gd:process_governor_AI()](src/managers/SettlementManager.gd#L140-L150)):

$$C_{\text{actual}} = C_{\text{base}} \times (L + 1)^{2.2}$$

Where:
- $C_{\text{actual}}$: Cost to build next level
- $C_{\text{base}}$: Base cost from `GameData.BUILDINGS`
- $L$: Current building level

**Cost Progression** (example: Farm with base cost 500):

| Level | Multiplier | Cost |
|-------|------------|------|
| 1 | $(1)^{2.2} = 1.0$ | 500 |
| 2 | $(2)^{2.2} = 4.6$ | 2,300 |
| 3 | $(3)^{2.2} = 11.2$ | 5,600 |
| 5 | $(5)^{2.2} = 33.6$ | 16,800 |
| 10 | $(10)^{2.2} = 158.5$ | 79,250 |

**Rationale**: Early levels are accessible for growth. Level 10 becomes a prestigious milestone requiring massive capital.

### 7.3 Building Catalog

**Definition** ([GameData.gd:BUILDINGS](src/core/GameData.gd)):

```gdscript
BUILDINGS = {
    "farm": {
        "category": "industry",
        "cost": 500,
        "labor": 500,
        "effect": {"type": "production_mult", "resource": "grain", "mult": 0.5},
        "tier_req": 1
    },
    "stone_walls": {
        "category": "defense",
        "cost": 15000,
        "labor": 2000,
        "effect": {"type": "garrison_mult", "mult": 10.0},
        "tier_req": 2
    },
    "housing_district": {
        "category": "civil",
        "cost": 1000,
        "labor": 800,
        "effect": {"type": "housing", "capacity": 200},
        "tier_req": 2
    }
}
```

**Categories**:
- **Industry**: Production multipliers (Farm, Mine, Blacksmith, Weaver)
- **Defense**: Garrison bonuses (Walls, Barracks, Watchtower)
- **Civil**: Population/happiness (Housing District, Market, Cathedral)

### 7.4 Governor AI

**Personality Types** ([SettlementManager.gd:process_governor_AI()](src/managers/SettlementManager.gd#L110-L200)):

| Personality | Priorities | Bias Multipliers |
|-------------|-----------|------------------|
| **Greedy** | Market, Mine, Merchant Guild, Goldsmith | 2.0Ã— on commerce buildings |
| **Builder** | Housing District, Warehouse | 1.5Ã— on capacity buildings |
| **Cautious** | Stone Walls, Granary, Watchtower | 1.8Ã— on defense buildings |
| **Balanced** | Even distribution | 1.0Ã— on all |

**Decision Algorithm** ([SettlementManager.gd#L110-L180](src/managers/SettlementManager.gd)):

```gdscript
# 1. Evaluate pressing needs (0-100 scale)
var housing_need = 100 if s_data.population >= cap else 0
var starvation_need = 80 if food_stock < pop * 14 else 0
var war_need = 60 if at_war else 0

# 2. Score all possible buildings
for b_name in GameData.BUILDINGS.keys():
    var score = 10.0  # Base score
    
    # PILLAR LOGIC
    match b_name:
        "housing_district": score += housing_need
        "granary": score += starvation_need
        "stone_walls": score += war_need
        "farm": score += 15  # Extraction is always decent
    
    # PERSONALITY BIAS
    if personality == "greedy" and b_name in ["market", "mine"]:
        score *= 2.0
    
    # LEVEL PENALTY (avoid over-specializing)
    score /= (1.0 + current_lvl * 0.5)
    
    build_scores.append({"id": b_name, "score": score})

# 3. Sort by score, attempt to build top priority
build_scores.sort_custom(func(a, b): return a.score > b.score)
if s_data.crown_stock >= build_scores[0].cost + treasury_buffer:
    construct(build_scores[0].id)
```

---

## 8. Faction Economy

### 8.1 Faction Treasuries

**Structure** ([GameState.gd](src/core/GameState.gd)):

```gdscript
var faction_treasuries = {
    "red": 5000,
    "blue": 5000,
    "green": 5000,
    "purple": 5000,
    "orange": 5000,
    "player": 1000,
    "bandits": 0,
    "neutral": 0
}
```

**Income Sources**:
1. **Caravan Tax** ([TradeSystem.gd](src/economy/TradeSystem.gd)):
   ```gdscript
   if caravan.crowns > 500:
       var tax = caravan.crowns - 500
       faction_treasuries[caravan.faction] += tax
       caravan.crowns = 500
   ```
2. **Conquest Loot**: 50% of captured settlement crown_stock
3. **Tribute** (future feature): Vassals pay percentage of income

**Expenditures**:
1. **Lord Upkeep**: `roster_size Ã— 2` crowns/day per lord
2. **Colonization**: 5000 crowns to found new settlement
3. **Emergency Subsidies**: Faction injects 5% of treasury into poor settlements daily

### 8.2 Lord Economics

**Upkeep System** ([CombatManager.gd](src/managers/CombatManager.gd)):

```gdscript
func process_lord_upkeep(lord):
    var upkeep = lord.roster.size() * 2
    
    if lord.crowns >= upkeep:
        lord.crowns -= upkeep
    else:
        # Try to withdraw from home fief
        var fief = get_settlement_at(lord.home_fief)
        if fief and fief.crown_stock > 1000 + upkeep:
            fief.crown_stock -= upkeep
        else:
            # DESERTION!
            var deserters = int(lord.roster.size() * 0.1)
            lord.roster = lord.roster.slice(0, lord.roster.size() - deserters)
```

**Recruitment Costs** ([AIManager.gd](src/managers/AIManager.gd)):

```gdscript
func recruit_units(lord):
    var cost = 500  # Base recruitment package
    var settlement = find_nearest_friendly_settlement(lord.pos)
    
    if settlement.crown_stock >= cost:
        settlement.crown_stock -= cost
    elif faction_treasuries[lord.faction] >= cost:
        faction_treasuries[lord.faction] -= cost
    else:
        return  # Cannot recruit
    
    # Add 10-20 recruits to roster
    for i in range(rng.randi_range(10, 20)):
        lord.roster.append(GameData.generate_recruit(rng, 2))
```

---

## 9. Geographic Potential

### 9.1 WorldGen Simulation

**Old System (Deprecated)**: 100-year historical simulation, performance issues.

**New System**: Geographic Potential Model ([WorldGen.gd](src/utils/WorldGen.gd)):

```gdscript
func calculate_site_potential(pos, grid, resources, geology):
    var capacity = 0.0
    var magnetism = 0.0
    
    # Scan 8-tile radius
    for dy in range(-8, 9):
        for dx in range(-8, 9):
            var p = pos + Vector2i(dx, dy)
            var t = grid[p.y][p.x]
            
            # CARRYING CAPACITY (food ceiling)
            if t == ".": capacity += 250  # Arable acres
            elif t == "~": capacity += 150  # Fishing
            elif t in ["/", "\\"]: capacity += 250  # Floodplain
            
            # MAGNETISM (labor draw)
            if resources.has(p):
                match resources[p]:
                    "gold": magnetism += 500
                    "gems": magnetism += 400
                    "iron": magnetism += 300
                    "copper": magnetism += 200
    
    var potential_revenue = (capacity * 0.5) + magnetism
    return potential_revenue
```

**Tier Assignment** ([WorldGen.gd](src/utils/WorldGen.gd)):

```gdscript
# Sort all potential sites by revenue
sites.sort_custom(func(a, b): return a.potential > b.potential)

# Top 5%: Metropolis (start at 95% capacity)
# Next 10%: City (start at 70% capacity)
# Next 20%: Town (start at 40% capacity)
# Remaining: Village (start at 20% capacity)

for i in range(sites.size()):
    var percentile = float(i) / sites.size()
    var tier = 0
    var pop_ratio = 0.2
    
    if percentile < 0.05:
        tier = 4  # Metropolis
        pop_ratio = 0.95
    elif percentile < 0.15:
        tier = 3  # City
        pop_ratio = 0.7
    elif percentile < 0.35:
        tier = 2  # Town
        pop_ratio = 0.4
    
    sites[i].starting_population = int(sites[i].capacity * pop_ratio)
```

**Effect**: High-value geographic locations (river deltas, mineral-rich valleys) naturally spawn as large cities. Eliminates "gilded death traps" (cities with no food).

### 9.2 Road Networks

**Minimum Spanning Tree** ([WorldGen.gd](src/utils/WorldGen.gd)):

```gdscript
# Connect all settlements with roads using MST + extra edges
var edges = []
for i in range(settlements.size()):
    for j in range(i+1, settlements.size()):
        var dist = settlements[i].pos.distance_to(settlements[j].pos)
        edges.append({"a": i, "b": j, "dist": dist})

edges.sort_custom(func(a, b): return a.dist < b.dist)

# Kruskal's algorithm for MST
var parent = []
parent.resize(settlements.size())
for i in range(settlements.size()):
    parent[i] = i

for edge in edges:
    if find(parent, edge.a) != find(parent, edge.b):
        draw_road(settlements[edge.a].pos, settlements[edge.b].pos)
        union(parent, edge.a, edge.b)
```

**Path Cost Reduction**: Roads reduce pathfinding cost from 1.0 to 0.5, encouraging caravans to follow "highways".

---

## 10. Economic Data Flow

### 10.1 Daily Tick Pipeline

```
GameState.advance_time() [Every 24 turns]
   â†“
EconomyManager.daily_pulse()
   â†“
   â”œâ”€â†’ ProductionSystem.run_production_tick()
   â”‚      â†“
   â”‚      â”œâ”€ For each settlement:
   â”‚      â”‚    â”œâ”€ recalculate_production() [scan terrain if needed]
   â”‚      â”‚    â”œâ”€ _process_labor_pool() [allocate laborers]
   â”‚      â”‚    â”œâ”€ _process_industry() [convert inputs to outputs]
   â”‚      â”‚    â””â”€ Update inventory (add grain, wood, ore, etc.)
   â”‚      â””â”€ Update global production_tracking
   â†“
   â”œâ”€â†’ ConsumptionSystem.run_consumption_tick()
   â”‚      â†“
   â”‚      â”œâ”€ For each settlement:
   â”‚      â”‚    â”œâ”€ _process_consumption_and_growth()
   â”‚      â”‚    â”‚    â”œâ”€ Consume food (pop Ã— 1.2 bushels)
   â”‚      â”‚    â”‚    â”œâ”€ Consume fuel (wood)
   â”‚      â”‚    â”‚    â”œâ”€ Consume luxuries (ale, meat, furs)
   â”‚      â”‚    â”‚    â”œâ”€ Check starvation (if food < 0)
   â”‚      â”‚    â”‚    â””â”€ Check growth (if food > 30-day buffer)
   â”‚      â”‚    â””â”€ _process_taxes()
   â”‚      â”‚         â”œâ”€ Collect poll tax (laborers Ã— 0.1 + burghers Ã— 0.5)
   â”‚      â”‚         â””â”€ Collect tariffs (market level Ã— 10 per 24 turns)
   â”‚      â””â”€ Sync social classes (recalc nobility/burghers/laborers)
   â†“
   â”œâ”€â†’ SettlementManager.process_all_governors()
   â”‚      â†“
   â”‚      â”œâ”€ For each settlement with governor:
   â”‚      â”‚    â”œâ”€ Evaluate pressing needs (housing, food, war)
   â”‚      â”‚    â”œâ”€ Score all possible buildings
   â”‚      â”‚    â”œâ”€ Attempt to construct top priority
   â”‚      â”‚    â””â”€ Update construction queue progress
   â†“
   â”œâ”€â†’ PricingSystem.update_all_prices()
   â”‚      â†“
   â”‚      â”œâ”€ For each settlement:
   â”‚      â”‚    â”œâ”€ Invalidate price cache
   â”‚      â”‚    â””â”€ Recalculate prices on-demand (lazy evaluation)
   â†“
   â””â”€â†’ TradeSystem.update_caravans()
          â†“
          â”œâ”€ Process logistical pulses (check arrival_turn)
          â””â”€ Update physical caravans (staggered, every 4 turns)
               â”œâ”€ Check world_market_orders
               â”œâ”€ Scan settlement prices (arbitrage)
               â”œâ”€ Load cargo + pathfind
               â””â”€ Travel / sell goods
```

### 10.2 Item Crafting Flow

```
Player Commission Request
   â†“
SettlementManager.commission_item(settlement, item_type, material, quality)
   â†“
EquipmentSystem.check_resources(settlement, material)
   â”œâ”€ Iron for steel weapon? Check inventory
   â”œâ”€ Wool for cloth tunic? Check inventory
   â””â”€ If missing: Return error
   â†“
EquipmentSystem.craft_item()
   â”œâ”€ Consume resources (iron, wood, leather, etc.)
   â”œâ”€ Calculate craft time (quality Ã— base_time)
   â””â”€ Add to settlement.crafting_queue
   â†“
Daily tick: Process crafting_queue
   â”œâ”€ Decrement time remaining
   â””â”€ If time == 0: Generate item, add to player inventory
```

---

## 11. Configuration & Tuning

### 11.1 Key Economic Constants

**[Globals.gd](src/core/Globals.gd)**:

```gdscript
# Production
const ACRES_PER_TILE = 250
const ACRES_WORKED_PER_LABORER = 40
const BUSHELS_PER_ACRE_BASE = 12.0
const FORESTRY_YIELD_WOOD = 8.0
const FISHING_YIELD_BASE = 25.0
const HUNTING_YIELD_MEAT = 15.0

# Consumption
const DAILY_BUSHELS_PER_PERSON = 1.2
const WOOD_FUEL_POP_DIVISOR = 100.0
const WOOD_FUEL_BUILDING_MULT = 2.0
const CLOTH_CONSUMPTION_RATE = 0.02
const LEATHER_CONSUMPTION_RATE = 0.01

# Growth
const GROWTH_RATE = 0.0001  # 0.01% daily = 3.65% annual
const GROWTH_BASE = 1

# Starvation
const STARVATION_DEATH_RATE = 0.02
const STARVATION_BASE_DEATH = 2
const STARVATION_UNREST_INC = 20.0
const STARVATION_HAPPINESS_DEC = 20.0

# Pricing
const PRICE_MIN_MULT = 0.2
const PRICE_MAX_MULT = 4.0
const PRICE_ZERO_STOCK_MULT = 5.0

# Social Classes
const NOBILITY_TARGET_PERCENT = 0.01
const BURGHER_TARGET_PERCENT = 0.15
```

### 11.2 Tuning Levers

**Balance Food Production**:
- Increase `BUSHELS_PER_ACRE_BASE` â†’ More grain output
- Decrease `DAILY_BUSHELS_PER_PERSON` â†’ Lower consumption
- Increase `FISHING_YIELD_BASE` â†’ Fishing becomes more viable

**Make Cities Grow Faster**:
- Increase `GROWTH_RATE` â†’ Higher birth rate
- Decrease housing costs in `BUILDINGS` â†’ Easier to expand capacity

**Adjust Market Volatility**:
- Increase `PRICE_MAX_MULT` â†’ Prices can spike higher during shortages
- Decrease `PRICE_ZERO_STOCK_MULT` â†’ Less panic buying

---

## 12. Testing & Debugging

### 12.1 World Audit Tool

**Purpose**: Inspect global economic state for balancing.

**Usage** ([GameState.gd](src/core/GameState.gd)):

```gdscript
func world_audit():
    print("=== WORLD ECONOMIC AUDIT ===")
    
    # Demographics
    var total_pop = 0
    var total_houses = 0
    var total_housing_cap = 0
    for s in settlements:
        total_pop += s.population
        total_houses += s.houses
        total_housing_cap += s.get_housing_capacity()
    
    print("Total Population: %d" % total_pop)
    print("Total Houses: %d" % total_houses)
    print("Total Housing Capacity: %d" % total_housing_cap)
    
    # Economy
    var total_wealth = 0
    for s in settlements:
        total_wealth += s.crown_stock
    for f in factions:
        total_wealth += faction_treasuries[f.id]
    
    print("Total Wealth: %d crowns" % total_wealth)
    
    # Resource Stocks
    for res in ["grain", "wood", "iron", "stone"]:
        var global_stock = 0
        var prices = []
        for s in settlements:
            global_stock += s.inventory.get(res, 0)
            prices.append(PricingSystem.get_price(res, s))
        
        var avg_price = prices.reduce(func(a, b): return a + b, 0) / max(1, prices.size())
        print("%s: Global Stock=%d, Avg Price=%d" % [res, global_stock, avg_price])
```

**Output Example**:
```
=== WORLD ECONOMIC AUDIT ===
Total Population: 45,320
Total Houses: 2,100
Total Housing Capacity: 48,500
Total Wealth: 125,430 crowns
grain: Global Stock=345,200, Avg Price=12
wood: Global Stock=89,400, Avg Price=8
iron: Global Stock=12,300, Avg Price=45
```

### 12.2 Turbo Simulation

**Purpose**: Fast-forward 30 days to observe systemic behavior.

**Usage** ([Main.gd](Main.gd)):

```gdscript
func monthly_turbo_simulation():
    print("=== TURBO SIMULATION: 30 DAYS ===")
    
    # Disable most logs
    GameState.batch_mode = true
    
    # Track metrics
    var start_pop = 0
    var end_pop = 0
    var production = {}
    var consumption = {}
    
    for s in GameState.settlements:
        start_pop += s.population
    
    # Run 30 days
    for day in range(30):
        for tick in range(24):
            GameState.advance_time()
        
        # Track daily production/consumption
        for res in ["grain", "wood", "iron"]:
            production[res] = production.get(res, 0) + GameState.production_tracking.get(res, 0)
            consumption[res] = consumption.get(res, 0) + GameState.consumption_tracking.get(res, 0)
    
    for s in GameState.settlements:
        end_pop += s.population
    
    GameState.batch_mode = false
    
    # Report
    print("Population Change: %d â†’ %d (%+d)" % [start_pop, end_pop, end_pop - start_pop])
    for res in production.keys():
        var net = production[res] - consumption[res]
        print("%s: Produced=%d, Consumed=%d, Net=%+d" % [res, production[res], consumption[res], net])
```

**Expected Output**:
```
=== TURBO SIMULATION: 30 DAYS ===
Population Change: 45,320 â†’ 45,780 (+460)
grain: Produced=125,400, Consumed=118,200, Net=+7,200
wood: Produced=45,200, Consumed=42,800, Net=+2,400
iron: Produced=3,200, Consumed=2,900, Net=+300
```

**Diagnosis**: If net is consistently negative, increase production multipliers or reduce consumption rates.

---

## 13. Extension Guide

### 13.1 Adding a New Resource

**Step 1**: Define base price in [Globals.gd](src/core/Globals.gd):

```gdscript
const BASE_PRICES = {
    ...
    "silk": 50,  # New luxury resource
}
```

**Step 2**: Add production logic in [ProductionSystem.gd](src/economy/ProductionSystem.gd):

```gdscript
# In _process_industry()
if s_data.buildings.get("silk_farm", 0) > 0:
    var silk_yield = s_data.buildings["silk_farm"] * 10
    s_data.add_inventory("silk", silk_yield)
```

**Step 3**: Add consumption logic in [ConsumptionSystem.gd](src/economy/ConsumptionSystem.gd):

```gdscript
# Nobility consumes silk
var noble_silk_req = max(1, int(s_data.nobility * 0.1))
var n_silk = s_data.remove_inventory("silk", noble_silk_req)
if n_silk < noble_silk_req:
    s_data.nobility_unhappy = true
```

**Step 4**: Add to world generation resource placement ([WorldGen.gd](src/utils/WorldGen.gd)):

```gdscript
if biome == "jungle" and rng.randf() < 0.05:
    resources[pos] = "silk"  # 5% chance in jungle tiles
```

### 13.2 Adding a New Building

**Step 1**: Define in [GameData.gd:BUILDINGS](src/core/GameData.gd):

```gdscript
"silk_farm": {
    "category": "industry",
    "cost": 3000,
    "labor": 800,
    "effect": {"type": "production", "resource": "silk", "amount": 10},
    "tier_req": 3,
    "requires_resource": "silk"  # Only buildable if silk nearby
}
```

**Step 2**: Add utility check in [SettlementManager.gd:is_building_useful()](src/managers/SettlementManager.gd):

```gdscript
match b_name:
    "silk_farm":
        for dy in range(-r, r+1):
            for dx in range(-r, r+1):
                var p = s_data.pos + Vector2i(dx, dy)
                if GameState.resources.has(p) and GameState.resources[p] == "silk":
                    return true
        return false
```

**Step 3**: Test with governor AI (greedy personalities will build it if silk is valuable).

### 13.3 Modifying Production Formulas

**Example**: Make farms scale with population instead of acres.

**Current** ([ProductionSystem.gd#L150](src/economy/ProductionSystem.gd)):

```gdscript
var farm_yield = (alloc["farms"] * Globals.ACRES_WORKED_PER_LABORER * Globals.BUSHELS_PER_ACRE_BASE) / Globals.DAYS_PER_YEAR
```

**Modified**:

```gdscript
# New formula: yield scales with farm buildings + population
var base_yield = (alloc["farms"] * Globals.ACRES_WORKED_PER_LABORER * Globals.BUSHELS_PER_ACRE_BASE) / Globals.DAYS_PER_YEAR
var pop_bonus = s_data.population * 0.01  # 0.01 bushels per pop
var farm_lvl = s_data.buildings.get("farm", 0)
var farm_yield = int((base_yield + pop_bonus) * (1.0 + farm_lvl * 0.5))
```

**Effect**: Larger cities produce more food even with same acreage, representing intensive farming techniques.

---

## Conclusion

The economy is the **heartbeat** of the simulation. Every mechanicâ€”warfare, politics, construction, tradeâ€”depends on the circular flow of resources. The key innovations are:

1. **Geographic Determinism**: Terrain dictates what settlements CAN produce (no wheat in deserts)
2. **Priority-Driven Labor**: Survival first, security second, profit third
3. **Dynamic Markets**: Prices emerge from scarcity, driving trade routes organically
4. **Polynomial Building Costs**: Early growth is fast, but Level 10 structures are prestige projects
5. **Faction Treasuries**: Wealth flows from settlements â†’ lords â†’ factions â†’ infrastructure

The system is modularâ€”production, consumption, pricing, and trade are independent subsystems that can be tuned separately. Use the **World Audit** and **Turbo Simulation** tools to validate balance changes before committing.
