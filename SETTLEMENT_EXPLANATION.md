# Settlement Systems: Technical Deep Dive

> **Purpose**: This document explains the settlement simulation architecture—from acreage allocation and population dynamics to governor AI, construction systems, and hamlet promotion. Covers the complete lifecycle of a settlement from founding to metropolis.

---

## Table of Contents
1. [System Overview](#1-system-overview)
2. [Settlement Architecture](#2-settlement-architecture)
3. [Acreage & Land Management](#3-acreage--land-management)
4. [Population & Social Classes](#4-population--social-classes)
5. [Governor AI System](#5-governor-ai-system)
6. [Growth & Migration](#6-growth--migration)
7. [Construction System](#7-construction-system)
8. [Hamlet Promotion](#8-hamlet-promotion)
9. [Settlement Data Flow](#9-settlement-data-flow)
10. [Configuration & Tuning](#10-configuration--tuning)
11. [Testing & Debugging](#11-testing--debugging)
12. [Extension Guide](#12-extension-guide)

---

## 1. System Overview

### 1.1 Settlement Philosophy

Settlements are **autonomous economic agents** that:
1. **Scan their geographic radius** to determine production capacity
2. **Allocate labor** based on survival priorities (food → security → profit)
3. **Consume resources** based on population and social class
4. **Make construction decisions** via governor AI
5. **Trade** with other settlements through caravans
6. **Grow or decline** based on food supply, housing, and happiness

**No Settlement is Identical**: A plains settlement focuses on grain export. A mountain settlement mines ore. A coastal settlement fishes. Geography is destiny.

### 1.2 File Structure

```
src/data/
└── GDSettlement.gd         # Settlement data structure (240 lines)

src/managers/
├── SettlementManager.gd    # Governor AI, construction, shops (826 lines)
└── EconomyManager.gd       # Orchestrates settlement ticks

src/economy/
├── ProductionSystem.gd     # Acreage scanning, labor allocation (559 lines)
└── ConsumptionSystem.gd    # Food/fuel consumption, growth (394 lines)

src/controllers/
└── OverworldController.gd  # Visual rendering, input handling
```

### 1.3 Settlement Tiers

**Tier Progression**:

| Tier | Type | Population Range | Radius | Building Slots | Garrison Max |
|------|------|------------------|--------|----------------|--------------|
| 0 | **Hamlet** | 10-50 | 1 | 0 | 5 |
| 1 | **Village** | 50-300 | 2 | 3 | 20 |
| 2 | **Town** | 300-1000 | 3 | 6 | 50 |
| 3 | **City** | 1000-5000 | 4 | 10 | 200 |
| 4 | **Metropolis** | 5000+ | 5 | 15 | 500 |

**Tier Unlocks**:
- **Tier 2 (Town)**: Merchant Guild → physical caravans spawn
- **Tier 3 (City)**: Markets produce tariff income, advanced recruits
- **Tier 4 (Metropolis)**: Passive renown generation, faction capital status

---

## 2. Settlement Architecture

### 2.1 Three-Layer Design

**Data Layer** ([GDSettlement.gd](src/data/GDSettlement.gd)):
```gdscript
class_name GDSettlement
extends RefCounted

var pos: Vector2i = Vector2i.ZERO
var name: String = "Settlement"
var faction: String = "neutral"
var type: String = "village"
var tier: int = 1
var population: int = 100
var laborers: int = 84
var burghers: int = 15
var nobility: int = 1
var happiness: int = 50
var unrest: int = 0
var stability: int = 50
var inventory: Dictionary = {}  # {"grain": 5000, "wood": 1200, ...}
var buildings: Dictionary = {}  # {"farm": 3, "barracks": 2, ...}
var construction_queue: Array = []
var governor: Dictionary = {}  # {"personality": "greedy", ...}
```

**Logic Layer** ([SettlementManager.gd](src/managers/SettlementManager.gd)):
- `process_governor_AI()`: Evaluates needs, scores buildings, queues construction
- `refresh_shop()`: Generates items for sale based on tier/resources
- `refresh_recruits()`: Spawns recruit pool based on barracks/training levels
- `process_construction()`: Applies daily labor to construction queue

**Visual Layer** ([OverworldController.gd](src/controllers/OverworldController.gd)):
- Renders settlement symbol (based on tier & type)
- Draws influence radius as colored circle
- Handles click events for settlement UI

### 2.2 Settlement Types

**Type Variations**:

| Type | Symbol | Special Features |
|------|--------|------------------|
| **Village** | `○` | Basic production, general purpose |
| **Town** | `●` | Higher capacity, unlocks merchant guild |
| **City** | `⊕` | Large scale, advanced buildings |
| **Castle** | `⊙` | Military focus, +100 garrison bonus |
| **Port** | `⊗` | Coastal, bonus fishing slots |
| **Hamlet** | `·` | No governor, virtual logistics only |

**Type Assignment** ([WorldGen.gd](src/utils/WorldGen.gd)):

```gdscript
if has_castle_terrain_nearby(pos):
    s_type = "castle"
elif is_coastal(pos):
    s_type = "port"
elif potential_revenue > 10000:
    s_type = "city"
elif potential_revenue > 5000:
    s_type = "town"
else:
    s_type = "village"
```

---

## 3. Acreage & Land Management

### 3.1 Acreage Calculation

**Entry Point**: [GDSettlement.gd:initialize_acres()](src/data/GDSettlement.gd#L120-L200)

**Process**:

```gdscript
func initialize_acres(grid, resources, geology):
    total_acres = 0
    arable_acres = 0
    forest_acres = 0
    pasture_acres = 0
    
    # Scan radius for terrain types
    for dy in range(-radius, radius+1):
        for dx in range(-radius, radius+1):
            var p = pos + Vector2i(dx, dy)
            var t = GameState.get_true_terrain(p)
            var tile_acres = Globals.ACRES_PER_TILE  # 250
            
            match t:
                ".":  # Plains
                    arable_acres += tile_acres
                "o":  # Hills
                    arable_acres += int(tile_acres * 0.5)
                    pasture_acres += int(tile_acres * 0.5)
                "#":  # Forest
                    var cleared = int(tile_acres * 0.2)
                    arable_acres += cleared
                    forest_acres += (tile_acres - cleared)
                "^":  # Mountain
                    mining_slots += 400
                "~":  # Water
                    fishing_slots += 150
```

**Land Breakdown Example** (settlement on 3 plains, 2 forests, 1 mountain):

- Arable: (3 × 250) + (2 × 50) = **850 acres**
- Forest: (2 × 200) = **400 acres**
- Mining: 400 slots
- Total: 1,250 acres

### 3.2 Three-Field Crop Rotation

**Historical Realism** ([ProductionSystem.gd#L100](src/economy/ProductionSystem.gd)):

```gdscript
var fallow_ratio = 1.0/3.0 if s_data.has_three_field_system else 0.5
var active_acres = int(s_data.arable_acres * (1.0 - fallow_ratio))
s_data.fallow_acres = s_data.arable_acres - active_acres
s_data.pasture_acres = s_data.fallow_acres  # Fallow doubles as pasture
```

**Effect**:
- **Without Three-Field** (early game): 50% of land is fallow = 50% active
- **With Three-Field** (Farm Level 4+): 33% fallow = **67% active**

**Grain Production Impact**:
- Before: 850 acres × 0.5 = 425 active acres
- After: 850 acres × 0.67 = **570 active acres** (+34% grain output)

### 3.3 Geology & Climate Modifiers

**Climate Effects** ([ProductionSystem.gd:increment_prod()](src/economy/ProductionSystem.gd#L90-L110)):

```gdscript
var mult = 1.0
if geology.has(s_data.pos):
    var geo = geology[s_data.pos]
    if res_name == "grain":
        if geo.get("rain", 0) > 0.1: mult += 0.5  # Wet: bonus yield
        if abs(geo.get("temp", 0)) > 0.3: mult -= 0.5  # Extreme temp: penalty
    elif res_name == "wood":
        if geo.get("rain", 0) > 0.2: mult += 0.5  # Rainforest growth
```

**Example**:
- Tropical rainforest (rain=0.4, temp=0.5): Wood × 1.5, Grain × 0.5
- Temperate plains (rain=0.2, temp=0): Grain × 1.5, balanced
- Desert (rain=-0.5, temp=0.6): All production × 0.5

---

## 4. Population & Social Classes

### 4.1 Class Structure

**Fixed Ratios** ([GDSettlement.gd:sync_social_classes()](src/data/GDSettlement.gd#L210-L220)):

```gdscript
nobility = max(1, int(population * Globals.NOBILITY_TARGET_PERCENT))    # 1%
burghers = int(population * Globals.BURGHER_TARGET_PERCENT)             # 15%
laborers = population - nobility - burghers                             # 84%
```

**Class Roles**:

| Class | % of Pop | Economic Role | Consumption | Upkeep Cost |
|-------|----------|---------------|-------------|-------------|
| **Laborers** | 84% | Extract resources (farming, mining, fishing) | Food (1.2/day) | 0.1 crowns/day tax income |
| **Burghers** | 15% | Industrial conversion (blacksmiths, weavers) | Food + Ale (0.1/day) + Cloth/Leather | 0.5 crowns/day tax income |
| **Nobility** | 1% | Governance, military leadership | Food + Meat (0.5/day) + Furs (0.05/day) + Salt (0.05/day) | Cost: happiness penalty if unsatisfied |

**Population Example** (pop=1000):
- Nobility: 10
- Burghers: 150
- Laborers: 840

### 4.2 Happiness & Unrest

**Happiness Drivers** ([ConsumptionSystem.gd](src/economy/ConsumptionSystem.gd)):

```gdscript
# Positive
happiness += (food_variety × 2)  # 2-4 different food types consumed
happiness += (tavern_lvl × 1)
happiness += (cathedral_lvl × 2)

# Negative
happiness -= (starvation_deficit × 20)
happiness -= (overcrowding × 1)  # Pop > housing capacity
happiness -= (unmet_luxury_needs × 5)  # Burghers/nobles unhappy
```

**Unrest Drivers**:

```gdscript
unrest += (starvation_deficit × 20)
unrest += (overcrowding × 1)
unrest -= (tavern_lvl × 1)
unrest -= (cathedral_lvl × 2)
unrest -= (stone_walls_lvl × 1)
```

**Critical Thresholds**:
- **Happiness < 40**: Migration begins (future feature)
- **Unrest > 70**: Work stoppage risk, reduced efficiency
- **Unrest > 90**: Rebellion (settlement switches faction)

---

## 5. Governor AI System

### 5.1 Personality Types

**Definition** ([SettlementManager.gd:process_governor_AI()](src/managers/SettlementManager.gd#L110-L200)):

```gdscript
var personality = gov.get("personality", "balanced")
```

**Personality Behaviors**:

| Personality | Priorities | Multipliers | Typical Settlements |
|-------------|-----------|-------------|---------------------|
| **Greedy** | Market, Merchant Guild, Goldsmith, Mine | Commerce buildings × 2.0 | Trade hubs, ports |
| **Builder** | Housing District, Warehouse | Capacity buildings × 1.5 | Population centers |
| **Cautious** | Stone Walls, Granary, Watchtower | Defense buildings × 1.8 | Frontier towns |
| **Balanced** | Even distribution | All × 1.0 | General purpose |

**Assignment** ([WorldGen.gd](src/utils/WorldGen.gd)):

```gdscript
if settlement.tier >= 3 and has_trade_route_nearby:
    governor.personality = "greedy"
elif settlement.tier == 2 and near_border:
    governor.personality = "cautious"
else:
    governor.personality = ["balanced", "builder"][rng.randi() % 2]
```

### 5.2 Decision Algorithm

**Entry Point**: [SettlementManager.gd:process_governor_AI()](src/managers/SettlementManager.gd#L110-L180)

**Process**:

```
1. Evaluate Pressing Needs (0-100 scale)
   ├─ housing_need = 100 if pop >= capacity, else 0
   ├─ starvation_need = 80 if food < pop × 14 days
   ├─ unrest_need = unrest × 1.5
   └─ war_need = 60 if at war with any faction

2. Score All Possible Buildings
   ┌──────────────────────────────────────────┐
   │ For each building in GameData.BUILDINGS: │
   │   base_score = 10.0                      │
   │                                          │
   │   # Geographic utility check            │
   │   if not is_building_useful(): skip     │
   │                                          │
   │   # Match building to needs             │
   │   match b_name:                          │
   │       "housing_district": += housing_need│
   │       "granary": += starvation_need     │
   │       "stone_walls": += war_need        │
   │       "farm": += 15  # Always decent    │
   │                                          │
   │   # Personality bias                    │
   │   if personality == "greedy" and        │
   │      b_name in commerce: score *= 2.0   │
   │                                          │
   │   # Level penalty (avoid over-spec)     │
   │   score /= (1.0 + current_lvl * 0.5)    │
   └──────────────────────────────────────────┘

3. Sort by Score, Attempt to Build Top Priority
   ├─ Sort descending by score
   ├─ Calculate cost: base_cost × (lvl + 1)^2.2
   ├─ If crown_stock >= cost + treasury_buffer:
   │    └─ Add to construction_queue
   └─ Else: Wait (economy rebuilds over time)
```

**Example Decision** (Cautious governor, war declared, pop=500, food=8000, capacity=600):

| Building | Base | Need Bonus | Personality | Level Penalty | Final Score |
|----------|------|------------|-------------|---------------|-------------|
| Stone Walls (Lvl 2) | 10 | +60 (war) | × 1.8 | ÷ 2.0 | **63** |
| Granary (Lvl 1) | 10 | +0 (food OK) | × 1.0 | ÷ 1.5 | 6.7 |
| Housing (Lvl 0) | 10 | +0 (pop < cap) | × 1.0 | ÷ 1.0 | 10 |

**Decision**: Build Stone Walls Level 3 (highest score).

### 5.3 Geographic Utility Check

**Purpose**: Prevent irrational builds (e.g., fishery in desert).

**Implementation** ([SettlementManager.gd:is_building_useful()](src/managers/SettlementManager.gd#L200-L230)):

```gdscript
func is_building_useful(s_data, b_name):
    var r = s_data.radius
    match b_name:
        "farm":
            return _check_terrain_near(s_data, r, ["."])  # Needs plains
        "lumber_mill":
            return _check_terrain_near(s_data, r, ["#", "&"])  # Needs forest/swamp
        "fishery":
            return _check_terrain_near(s_data, r, ["~"])  # Needs water
        "mine":
            # Needs mountains OR special resources
            if _check_terrain_near(s_data, r, ["^"]): return true
            for p in scan_radius:
                if GameState.resources.has(p) and GameState.resources[p] in ["iron", "gold", "gems"]:
                    return true
            return false
    return true  # All other buildings always useful
```

---

## 6. Growth & Migration

### 6.1 Population Growth

**Trigger Conditions** ([ConsumptionSystem.gd#L120-L140](src/economy/ConsumptionSystem.gd)):

```gdscript
if hunger_satisfied >= total_hunger and s_data.get_food_stock() > total_hunger * 30:
    var cap = s_data.get_housing_capacity()
    if s_data.population < cap:
        var births = int(s_data.population * Globals.GROWTH_RATE) + Globals.GROWTH_BASE
        s_data.population += births
        if s_data.population > cap: s_data.population = cap
```

**Formula**:

$$\text{Births} = \lfloor P \times 0.0001 \rfloor + 1$$

Where $P$ = current population.

**Growth Rate**:
- **Daily**: 0.01% + 1 person
- **Annual**: ~3.65% compound
- **Examples**:
  - Pop=100: +1-2 per day = +365-730/year = **48-97% annual growth**
  - Pop=1000: +2-3 per day = +730-1095/year = **73-110% annual growth**
  - Pop=5000: +6-7 per day = +2190-2555/year = **44-51% annual growth**

**Limits**:
1. **Housing Capacity**: Growth stops at capacity
2. **Food Buffer**: Requires 30-day food stock (pop × 1.2 × 30)
3. **Happiness**: (Future) Low happiness slows growth

### 6.2 Starvation

**Death Formula** ([ConsumptionSystem.gd#L45-L55](src/economy/ConsumptionSystem.gd)):

```gdscript
var deficit_ratio = 1.0 - (hunger_satisfied / total_hunger)
var granary_lvl = s_data.buildings.get("granary", 0)
var mitigation = clamp(granary_lvl * 0.15, 0.0, 0.8)

var deaths = int(pop * Globals.STARVATION_DEATH_RATE * deficit_ratio * (1.0 - mitigation)) + Globals.STARVATION_BASE_DEATH
s_data.population = max(0, pop - deaths)
```

**Constants**:
- `STARVATION_DEATH_RATE = 0.02` (2% of pop per day)
- `STARVATION_BASE_DEATH = 2` (minimum deaths)
- `Granary mitigation: 15% per level` (max 80% at level 5+)

**Example** (pop=1000, 50% food deficit, granary=0):
- Deaths = (1000 × 0.02 × 0.5 × 1.0) + 2 = **12 deaths/day**
- After 10 days: **120 deaths** = 88% survival
- After 30 days: **360 deaths** = 64% survival

**With Granary Level 5** (80% mitigation):
- Deaths = (1000 × 0.02 × 0.5 × 0.2) + 2 = **4 deaths/day**
- After 30 days: **120 deaths** = 88% survival

### 6.3 Migration (Future Feature)

**Planned System**:

```gdscript
if happiness < 40:
    var nearby_settlements = get_settlements_in_range(5)
    for target in nearby_settlements:
        if target.happiness > 60 and target.population < target.get_housing_capacity():
            var emigrants = int(population * 0.01)  # 1% leave
            population -= emigrants
            target.population += emigrants
            GameState.add_log("%s: %d emigrants fled to %s" % [name, emigrants, target.name])
            break
```

**Effect**: Creates dynamic population flows from unhappy → happy settlements.

---

## 7. Construction System

### 7.1 Construction Queue

**Data Structure** ([GDSettlement.gd](src/data/GDSettlement.gd)):

```gdscript
var construction_queue: Array = [
    {
        "id": "farm",
        "progress": 250,  # Current labor applied
        "total_labor": 500,  # Labor required
        "resources_met": true  # (Future) Resource requirements satisfied
    }
]
```

**Daily Progress** ([SettlementManager.gd:process_construction()](src/managers/SettlementManager.gd)):

```gdscript
func process_construction(s_data):
    if s_data.construction_queue.is_empty(): return
    
    var project = s_data.construction_queue[0]
    var efficiency = s_data.get_workforce_efficiency()
    var labor_power = efficiency * (s_data.population / 100.0) * (s_data.happiness / 100.0)
    
    project.progress += int(labor_power)
    
    if project.progress >= project.total_labor:
        # Construction complete!
        var b_id = project.id
        s_data.buildings[b_id] = s_data.buildings.get(b_id, 0) + 1
        s_data.construction_queue.pop_front()
        s_data.invalidate_cache("all")  # Recalc housing, efficiency, etc.
        GameState.add_log("%s completed %s (Level %d)!" % [s_data.name, b_id, s_data.buildings[b_id]])
```

**Labor Power Formula**:

$$L = \frac{P}{100} \times E \times \frac{H}{100}$$

Where:
- $L$: Labor power per day
- $P$: Population
- $E$: Workforce efficiency (0.1-1.0, based on unrest/burgher happiness)
- $H$: Happiness (0-100)

**Construction Time Examples**:

| Pop | Efficiency | Happiness | Labor Power | Farm Lvl 1 (500 labor) | Stone Walls Lvl 1 (2000 labor) |
|-----|------------|-----------|-------------|------------------------|--------------------------------|
| 500 | 1.0 | 100 | 5/day | **100 days** | **400 days** |
| 1000 | 1.0 | 80 | 8/day | **63 days** | **250 days** |
| 2000 | 0.8 | 60 | 9.6/day | **52 days** | **208 days** |

### 7.2 Cost Scaling

**Polynomial Formula** ([SettlementManager.gd#L140](src/managers/SettlementManager.gd)):

$$C_{\text{actual}} = C_{\text{base}} \times (L + 1)^{2.2}$$

**Example: Farm** (base cost = 500):

| Level | Multiplier | Cost (Crowns) | Labor Required |
|-------|------------|---------------|----------------|
| 1 | 1.0 | 500 | 500 |
| 2 | 4.6 | 2,300 | 2,300 |
| 3 | 11.2 | 5,600 | 5,600 |
| 5 | 33.6 | 16,800 | 16,800 |
| 10 | 158.5 | 79,250 | 79,250 |

**Design Rationale**:
- Early levels (1-3) are accessible for rapid growth
- Mid levels (4-6) require planning and economic stability
- High levels (7-10) are prestige projects for wealthy capitals

### 7.3 Player Sponsorship

**Mechanism** ([SettlementManager.gd](src/managers/SettlementManager.gd)):

```gdscript
func sponsor_building(s_data, player, b_id):
    var current_lvl = s_data.buildings.get(b_id, 0)
    var cost = calculate_building_cost(b_id, current_lvl)
    
    if player.crowns >= cost:
        player.crowns -= cost
        s_data.crown_stock += cost  # Settlement receives funds
        s_data.construction_queue.append({
            "id": b_id,
            "progress": 0,
            "total_labor": int(cost),
            "resources_met": true
        })
        
        # Grant influence
        s_data.player_influence += 10
        GameState.add_log("You sponsored %s in %s. +10 Influence." % [b_id, s_data.name])
```

**Influence Effects** (future):
- **50+ Influence**: Trade discounts (10% cheaper buy prices)
- **75+ Influence**: Access to elite recruits
- **100+ Influence**: Settlement pledges allegiance to player faction

---

## 8. Hamlet Promotion

### 8.1 Hamlet System

**Purpose**: Low-population nodes (10-50 pop) that feed resources to parent settlements without spawning AI entities.

**Characteristics**:
- No governor AI
- No construction queue
- No shop/recruit pool
- **Virtual logistics only**: Uses logistical pulses instead of physical caravans

**Spawning** ([WorldGen.gd](src/utils/WorldGen.gd)):

```gdscript
# Create hamlet at resource-rich but low-capacity site
if potential_revenue > 500 and potential_revenue < 2000:
    var hamlet = GDSettlement.new(pos)
    hamlet.type = "hamlet"
    hamlet.tier = 0
    hamlet.population = rng.randi_range(10, 50)
    hamlet.radius = 1
    hamlet.parent_city = find_nearest_city(pos)
```

### 8.2 Logistical Pulse System

**Hamlet Production** ([ProductionSystem.gd](src/economy/ProductionSystem.gd)):

```gdscript
func hamlet_daily_tick(hamlet, parent_city):
    # Hamlets always allocate 100% of labor to extraction
    var daily_output = {}
    
    if hamlet.arable_acres > 0:
        daily_output["grain"] = int(hamlet.arable_acres * 12 / 365)
    if hamlet.forest_acres > 0:
        daily_output["wood"] = int(hamlet.forest_acres * 8 / 365)
    if hamlet.mining_slots > 0:
        daily_output["stone"] = int(hamlet.mining_slots * 0.5)
    
    # Create pulse for each resource
    for res in daily_output.keys():
        if daily_output[res] > 0:
            send_virtual_pulse(hamlet, parent_city, res, daily_output[res])
```

**Pulse Arrival** ([GameState.gd](src/core/GameState.gd)):

```gdscript
func process_logistical_pulses():
    for p in logistical_pulses:
        if turn >= p.arrival_turn:
            var target = get_settlement_at(p.target)
            if target:
                target.add_inventory(p.resource, p.amount)
                hamlet_delivery_success_count[p.origin] += 1
            logistical_pulses.erase(p)
```

### 8.3 Promotion Trigger

**Stability System**:

```gdscript
# In GameState.gd
var hamlet_stability = {}  # hamlet_pos -> int

# Each successful delivery increases stability
hamlet_stability[hamlet.pos] += 1

# At stability >= 50, promote to village
if hamlet_stability[hamlet.pos] >= 50:
    promote_hamlet_to_village(hamlet)
```

**Promotion Effects** ([SettlementManager.gd](src/managers/SettlementManager.gd)):

```gdscript
func promote_hamlet_to_village(s_data):
    s_data.type = "village"
    s_data.tier = 1
    s_data.radius = 2
    s_data.population = rng.randi_range(80, 120)
    s_data.garrison = 10
    s_data.houses = 20
    s_data.crown_stock = 500
    
    # Assign governor
    s_data.governor = {
        "personality": ["balanced", "builder", "cautious"][rng.randi() % 3]
    }
    
    # Unlock building slots
    s_data.buildings = {"farm": 1}  # Start with basic farm
    
    GameState.add_log("%s has been promoted to Village!" % s_data.name)
```

**Promotion is One-Way**: Once a hamlet becomes a village, it never reverts (even if pop drops).

---

## 9. Settlement Data Flow

### 9.1 Daily Tick Pipeline (Settlement Focus)

```
EconomyManager.daily_pulse()
   â†“
For each settlement:
   â†“
   â”œâ”€â†’ ProductionSystem.run_production_tick(settlement)
   â”‚      â†“
   â”‚      â”œâ”€ IF type == "hamlet":
   â”‚      â”‚    â””â”€ hamlet_daily_tick() â†’ Create logistical pulses
   â”‚      â”œâ”€ ELSE:
   â”‚      â”‚    â”œâ”€ recalculate_production() [scan terrain if radius changed]
   â”‚      â”‚    â”œâ”€ _process_labor_pool() [allocate laborers to tasks]
   â”‚      â”‚    â””â”€ _process_industry() [convert inputs to outputs]
   â”‚      â””â”€ Update inventory (add resources)
   â†“
   â”œâ”€â†’ ConsumptionSystem.run_consumption_tick(settlement)
   â”‚      â†“
   â”‚      â”œâ”€ _process_consumption_and_growth()
   â”‚      â”‚    â”œâ”€ Consume food (check starvation)
   â”‚      â”‚    â”œâ”€ Consume fuel (wood)
   â”‚      â”‚    â”œâ”€ Consume luxuries (ale, meat, furs)
   â”‚      â”‚    â”œâ”€ Check growth (if food buffer > 30 days)
   â”‚      â”‚    â””â”€ Update happiness/unrest
   â”‚      â””â”€ _process_taxes()
   â”‚           â”œâ”€ Collect poll tax
   â”‚           â””â”€ Collect tariff revenue (if market present)
   â†“
   â”œâ”€â†’ SettlementManager.process_governor_AI(settlement)
   â”‚      â†“
   â”‚      â”œâ”€ IF has_governor:
   â”‚      â”‚    â”œâ”€ Evaluate pressing needs
   â”‚      â”‚    â”œâ”€ Score all buildings
   â”‚      â”‚    â”œâ”€ Attempt to construct top priority
   â”‚      â”‚    â””â”€ Update construction queue progress
   â†“
   â””â”€â†’ PricingSystem.invalidate_cache(settlement)
          â””â”€ Clear price cache (lazy recalculation on next query)
```

### 9.2 Construction Flow

```
Governor AI Decision â†’ Queue Building
   â†“
EconomyManager.daily_pulse()
   â†“
SettlementManager.process_construction(settlement)
   â†“
   â”œâ”€ Calculate labor_power (pop, efficiency, happiness)
   â”œâ”€ Add labor_power to project.progress
   â”œâ”€ IF progress >= total_labor:
   â”‚    â”œâ”€ Increment building level
   â”‚    â”œâ”€ Remove from queue
   â”‚    â”œâ”€ Invalidate caches (housing, efficiency, prices)
   â”‚    â””â”€ Log completion
   â””â”€ ELSE: Continue next day
```

### 9.3 Player Interaction Flow

```
Player clicks settlement in overworld
   â†“
OverworldController.handle_settlement_click(pos)
   â†“
UIRenderer.show_settlement_panel(settlement)
   â†“
   â”œâ”€ Display: Population, Resources, Buildings, Happiness
   â”œâ”€ Shop Tab: Display settlement.shop_inventory (refreshes weekly)
   â”œâ”€ Recruits Tab: Display settlement.recruit_pool (refreshes weekly)
   â”œâ”€ Construction Tab: Display construction_queue + available buildings
   â””â”€ Management Tab: World market orders, governor status
   â†“
Player action (buy item, recruit unit, sponsor building, etc.)
   â†“
   â”œâ”€ BUY ITEM:
   â”‚    â”œâ”€ Deduct crowns from player
   â”‚    â”œâ”€ Add crowns to settlement.crown_stock
   â”‚    â”œâ”€ Add item to player.inventory
   â”‚    â””â”€ Remove from shop_inventory
   â”‚
   â”œâ”€ RECRUIT UNIT:
   â”‚    â”œâ”€ Deduct crowns from player (cost = tier Ã— 100)
   â”‚    â”œâ”€ Add crowns to settlement.crown_stock
   â”‚    â”œâ”€ Remove unit from recruit_pool
   â”‚    â””â”€ Add to player.roster
   â”‚
   â””â”€ SPONSOR BUILDING:
        â”œâ”€ Deduct cost from player.crowns
        â”œâ”€ Add cost to settlement.crown_stock
        â”œâ”€ Add project to settlement.construction_queue
        â””â”€ Grant player_influence +10
```

---

## 10. Configuration & Tuning

### 10.1 Key Settlement Constants

**[Globals.gd](src/core/Globals.gd)**:

```gdscript
# Acreage
const ACRES_PER_TILE = 250
const ACRES_WORKED_PER_LABORER = 40

# Population
const GROWTH_RATE = 0.0001  # 0.01% daily
const GROWTH_BASE = 1
const STARVATION_DEATH_RATE = 0.02
const STARVATION_BASE_DEATH = 2

# Social Classes
const NOBILITY_TARGET_PERCENT = 0.01
const BURGHER_TARGET_PERCENT = 0.15

# Happiness/Unrest
const STARVATION_HAPPINESS_DEC = 20.0
const STARVATION_UNREST_INC = 20.0
const OVERCROWDING_PENALTY = 1  # Per day when pop > capacity

# Construction
const BASE_LABOR_POWER_DIVISOR = 100  # Pop / 100 = labor power
```

**[GameData.gd:BUILDINGS](src/core/GameData.gd)**:

```gdscript
BUILDINGS = {
    "farm": {"cost": 500, "labor": 500, "category": "industry"},
    "stone_walls": {"cost": 15000, "labor": 2000, "category": "defense"},
    "housing_district": {"cost": 1000, "labor": 800, "category": "civil"},
    ...
}
```

### 10.2 Tuning Levers

**Make Settlements Grow Faster**:
- Increase `GROWTH_RATE` â†’ Higher birth rate
- Decrease housing costs â†’ Easier to expand capacity
- Increase `BUSHELS_PER_ACRE_BASE` â†’ More food = more growth

**Make Construction Faster**:
- Decrease `BASE_LABOR_POWER_DIVISOR` (e.g., 50 instead of 100) â†’ Double labor power
- Increase happiness baseline (add tavern bonuses) â†’ Higher efficiency

**Make Starvation Less Punishing**:
- Decrease `STARVATION_DEATH_RATE` (e.g., 0.01 = 1% per day)
- Increase granary mitigation (0.2 per level instead of 0.15)

**Make Governor AI More Aggressive**:
- Lower `treasury_buffer` in governor AI (allow spending down to 100 crowns instead of 500)
- Increase personality multipliers (greedy Ã— 3.0 instead of Ã— 2.0)

---

## 11. Testing & Debugging

### 11.1 Settlement Inspector

**Purpose**: View detailed settlement state for balancing.

**Usage** ([Main.gd](Main.gd)):

```gdscript
func inspect_settlement(s_data):
    print("=== SETTLEMENT: %s ===" % s_data.name)
    print("Type: %s | Tier: %d | Faction: %s" % [s_data.type, s_data.tier, s_data.faction])
    print("Population: %d (Nobles: %d, Burghers: %d, Laborers: %d)" % [s_data.population, s_data.nobility, s_data.burghers, s_data.laborers])
    print("Happiness: %d | Unrest: %d | Stability: %d" % [s_data.happiness, s_data.unrest, s_data.stability])
    print("Crown Stock: %d" % s_data.crown_stock)
    print("\n--- LAND ---")
    print("Arable: %d acres | Forest: %d acres | Pasture: %d acres" % [s_data.arable_acres, s_data.forest_acres, s_data.pasture_acres])
    print("Mining Slots: %d | Fishing Slots: %d" % [s_data.mining_slots, s_data.fishing_slots])
    print("\n--- RESOURCES ---")
    for res in s_data.inventory.keys():
        if s_data.inventory[res] > 0:
            var price = PricingSystem.get_price(res, s_data)
            print("%s: %d (Price: %d crowns)" % [res, s_data.inventory[res], price])
    print("\n--- BUILDINGS ---")
    for b in s_data.buildings.keys():
        print("%s: Level %d" % [b, s_data.buildings[b]])
    print("\n--- CONSTRUCTION QUEUE ---")
    if s_data.construction_queue.is_empty():
        print("(None)")
    else:
        for proj in s_data.construction_queue:
            print("%s: %d/%d labor" % [proj.id, proj.progress, proj.total_labor])
```

**Example Output**:

```
=== SETTLEMENT: Riverrun ===
Type: city | Tier: 3 | Faction: red
Population: 1520 (Nobles: 15, Burghers: 228, Laborers: 1277)
Happiness: 65 | Unrest: 20 | Stability: 70
Crown Stock: 8420

--- LAND ---
Arable: 1250 acres | Forest: 800 acres | Pasture: 416 acres
Mining Slots: 0 | Fishing Slots: 450

--- RESOURCES ---
grain: 45200 (Price: 8 crowns)
wood: 12400 (Price: 10 crowns)
fish: 3200 (Price: 12 crowns)
iron: 450 (Price: 40 crowns)

--- BUILDINGS ---
farm: Level 3
lumber_mill: Level 2
fishery: Level 4
market: Level 2
stone_walls: Level 5

--- CONSTRUCTION QUEUE ---
barracks: 580/2300 labor
```

### 11.2 Common Issues

**Problem**: Settlement has negative growth despite abundant food.  
**Diagnosis**: Check `get_housing_capacity()`. Likely at capacity.  
**Fix**: Sponsor Housing District or increase base houses.

**Problem**: Governor never builds anything.  
**Diagnosis**: Check `crown_stock`. Likely too low (below treasury_buffer).  
**Fix**: Increase tax revenue (add market) or reduce building costs.

**Problem**: Hamlet never promotes to village.  
**Diagnosis**: Check `hamlet_stability[pos]`. Likely pulses not arriving.  
**Fix**: Verify parent_city exists and is not too far (> 20 tiles).

**Problem**: Population oscillates wildly (boom/bust cycles).  
**Diagnosis**: Food production barely meets consumption, causing feast/famine.  
**Fix**: Increase farm levels or fishing capacity to create buffer.

### 11.3 Test Scenarios

```gdscript
# Scenario 1: Rapid Growth
func test_growth():
    var settlement = create_settlement(pop=500, food=100000, housing=2000)
    for day in range(100):
        ConsumptionSystem._process_consumption_and_growth(settlement)
    # Expected: Pop grows to ~900+ (80% increase over 100 days)

# Scenario 2: Starvation Recovery
func test_starvation():
    var settlement = create_settlement(pop=1000, food=0)
    for day in range(10):
        ConsumptionSystem._process_consumption_and_growth(settlement)
    # Expected: ~120 deaths (12/day), pop = 880
    
    settlement.inventory["grain"] = 50000
    for day in range(20):
        ConsumptionSystem._process_consumption_and_growth(settlement)
    # Expected: Population stabilizes, begins regrowing

# Scenario 3: Governor AI Building Spree
func test_governor():
    var settlement = create_settlement(tier=2, crown_stock=50000)
    settlement.governor = {"personality": "greedy"}
    for day in range(365):
        SettlementManager.process_governor_AI(settlement)
    # Expected: Market Level 5+, Merchant Guild built, possibly goldsmith
```

---

## 12. Extension Guide

### 12.1 Adding a New Settlement Type

**Step 1**: Define type in [WorldGen.gd](src/utils/WorldGen.gd):

```gdscript
if has_mine_nearby and potential_revenue > 8000:
    s_type = "mining_town"
```

**Step 2**: Add symbol in [OverworldController.gd](src/controllers/OverworldController.gd):

```gdscript
match settlement.type:
    "mining_town": return "âŠ›"  # New symbol
```

**Step 3**: Add special bonuses:

```gdscript
# In GDSettlement.gd:initialize_acres()
if type == "mining_town":
    mining_slots *= 2.0  # Double mining capacity
```

### 12.2 Adding a New Governor Personality

**Step 1**: Define in [SettlementManager.gd:process_governor_AI()](src/managers/SettlementManager.gd):

```gdscript
match personality:
    "warmonger":
        if b_name in ["stone_walls", "barracks", "training_ground", "watchtower"]:
            score *= 2.5
```

**Step 2**: Assign during worldgen:

```gdscript
if settlement.near_enemy_border:
    governor.personality = "warmonger"
```

**Step 3**: Test (warmongers should prioritize military buildings over commerce).

### 12.3 Modifying Growth Formula

**Current** ([ConsumptionSystem.gd#L130](src/economy/ConsumptionSystem.gd)):

```gdscript
var births = int(s_data.population * Globals.GROWTH_RATE) + Globals.GROWTH_BASE
```

**Modified (Happiness-Dependent Growth)**:

```gdscript
var base_births = int(s_data.population * Globals.GROWTH_RATE) + Globals.GROWTH_BASE
var happiness_mult = clamp(s_data.happiness / 100.0, 0.5, 1.5)
var births = int(base_births * happiness_mult)
```

**Effect**: High happiness (80+) â†’ 1.2-1.5Ã— birth rate. Low happiness (< 50) â†’ 0.5-0.8Ã— birth rate.

### 12.4 Adding Building Milestones

**Example**: Unlock "Mage Tower" at Cathedral Level 10.

**Step 1**: Define building:

```gdscript
BUILDINGS["mage_tower"] = {
    "cost": 20000,
    "labor": 5000,
    "category": "civil",
    "requires_building": {"cathedral": 10}  # New requirement field
}
```

**Step 2**: Add check in governor AI:

```gdscript
# In process_governor_AI(), before scoring
if b_data.has("requires_building"):
    for req_building in b_data["requires_building"].keys():
        var req_lvl = b_data["requires_building"][req_building]
        if s_data.buildings.get(req_building, 0) < req_lvl:
            continue  # Skip this building (requirement not met)
```

**Step 3**: Add effect:

```gdscript
# When mage tower is built
if b_id == "mage_tower":
    s_data.mana_generation = s_data.buildings[b_id] * 10  # Future magic system
```

---

## Conclusion

Settlements are the **economic engines** of the simulation. They:

1. **Transform geography into wealth** (terrain â†’ acreage â†’ resources)
2. **Make autonomous decisions** (governor AI prioritizes based on needs + personality)
3. **Grow organically** (food + housing â†’ population â†’ labor â†’ more production)
4. **Create emergent trade networks** (surplus â†’ world market orders â†’ caravans)
5. **Suffer realistic crises** (starvation, overcrowding, rebellion)

The key innovations are:

1. **Acreage-Based Production**: No arbitrary "resource nodes"â€”land dictates output
2. **Three-Tier Labor Priority**: Survival â†’ Security â†’ Profit
3. **Governor Personalities**: Different settlements develop differently
4. **Polynomial Scaling**: Early growth is fast, late game requires prestige investment
5. **Hamlet â†’ Village Promotion**: Low-tier settlements can graduate to cities

The system is modularâ€”acreage, population, construction, and governor AI are independent subsystems. Use the **Settlement Inspector** tool to debug economic stalls and validate balance changes.
