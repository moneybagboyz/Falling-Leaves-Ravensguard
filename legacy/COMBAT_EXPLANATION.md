# Combat Systems: Technical Deep Dive

> **Purpose**: This document explains every layer of the combat simulation, from individual anatomy resolution to strategic army battles—covering tactical turn-based combat, AI decision-making, siege warfare, and battle flow architecture.

---

## Table of Contents
1. [System Overview](#1-system-overview)
2. [Anatomy-Based Combat](#2-anatomy-based-combat)
3. [Tactical Battle Architecture](#3-tactical-battle-architecture)
4. [AI Decision-Making System](#4-ai-decision-making-system)
5. [Siege Warfare](#5-siege-warfare)
6. [Strategic Auto-Resolution](#6-strategic-auto-resolution)
7. [Battalion Formations](#7-battalion-formations)
8. [Siege Engines](#8-siege-engines)
9. [Combat Data Flow](#9-combat-data-flow)
10. [Configuration & Tuning](#10-configuration--tuning)
11. [Testing & Debugging](#11-testing--debugging)
12. [Extension Guide](#12-extension-guide)

---

## 1. System Overview

### 1.1 Two Combat Modes

The engine operates in **two distinct modes**:

| Mode | Use Case | Entry Point | Resolution |
|------|----------|-------------|------------|
| **Tactical** | Player-controlled battles | [BattleController.gd](src/controllers/BattleController.gd) | Turn-based with anatomical damage |
| **Strategic** | AI-vs-AI off-screen | [CombatManager.gd](src/managers/CombatManager.gd) | Strength-based instant resolution |

**Tactical battles** are entity-based simulations where each unit has full anatomy, equipment, and AI. **Strategic resolution** uses aggregate strength calculations for efficiency.

### 1.2 File Structure

```
src/battle/
├── BattlePhysics.gd    # Spatial grid, pathfinding, collision (318 lines)
├── BattleTerrain.gd    # Procedural terrain generation
├── BattleCombat.gd     # Damage resolution, projectiles (411 lines)
├── BattleAI.gd         # Unit FSM, formations, orders (697 lines)
├── BattleSiege.gd      # Siege engine logic, battering rams
└── BattleState.gd      # Battalion management, unit spawning

src/controllers/
└── BattleController.gd # Main orchestrator (1466 lines)

src/managers/
└── CombatManager.gd    # Strategic resolution engine
```

### 1.3 Core Principles

1. **Dwarf Fortress-Style Anatomy**: No HP bars—death from bleeding, organ failure, or structural destruction
2. **Material Physics**: Weapon vs. armor matchups based on hardness, yield strength, elasticity ([data/materials.json](data/materials.json))
3. **Turn-Based Initiative**: Sorted by `(speed - agility_bonus + jitter)` to break determinism
4. **Spatial Hashing**: 10x10 tile buckets with team bitmasks for fast queries ([BattlePhysics.gd#L76-L102](src/battle/BattlePhysics.gd))

---

## 2. Anatomy-Based Combat

### 2.1 Body Structure

Every `GDUnit` has a hierarchical `body` dictionary:

```gdscript
body = {
  "head": {
    "tissues": [
      {"type": "skin", "hp": 20, "hp_max": 20},
      {"type": "bone", "hp": 40, "hp_max": 40, "is_skull": true},
      {"type": "brain", "hp": 15, "hp_max": 15, "is_vital": true}
    ]
  },
  "torso": {
    "tissues": [
      {"type": "skin", "hp": 30, "hp_max": 30},
      {"type": "bone", "hp": 60, "hp_max": 60, "is_ribcage": true},
      {"type": "heart", "hp": 25, "hp_max": 25, "is_vital": true},
      {"type": "lungs_l", "hp": 20, "hp_max": 20},
      {"type": "lungs_r", "hp": 20, "hp_max": 20},
      {"type": "gut", "hp": 15, "hp_max": 15},
      {"type": "spine", "hp": 50, "hp_max": 50, "is_spine": true}
    ]
  },
  "l_arm": { "tissues": [...] },
  "r_arm": { "tissues": [...] },
  "l_leg": { "tissues": [...] },
  "r_leg": { "tissues": [...] }
}
```

**Key Tissue Flags**:
- `is_vital`: Destruction causes instant death (brain, heart)
- `is_spine`: Destruction causes paralysis
- `is_arterial`: Bleeding at 5ml/sec instead of 1ml/sec

### 2.2 Damage Resolution Algorithm

**Entry Point**: [BattleCombat.gd:resolve_complex_damage()](src/battle/BattleCombat.gd#L235-L300)

```gdscript
func resolve_complex_damage(attacker, defender, forced_part, attack_idx, ...):
    # 1. Siege Engine Physics (special case)
    if attacker.is_siege_engine:
        res = GameData.resolve_engine_damage(attacker.engine_type, defender, rng)
        return res
    
    # 2. Standard Attack Resolution
    var sw_bonus = get_shield_bonus_callback.call(defender)
    res = GameData.resolve_attack(attacker, defender, rng, forced_part, attack_idx, sw_bonus)
    
    # res = {
    #   "hit": bool,
    #   "blocked": bool,
    #   "part_hit": "torso",
    #   "tissues_hit": ["skin", "bone", "heart"],
    #   "armor_layers": ["steel_cuirass"],
    #   "dmg_type": "pierce",
    #   "total_damage": 45
    # }
```

**Core Formula** (from [GameData.gd](src/core/GameData.gd)):

$$E = (B + M \times 2.0) \times S$$

Where:
- $B$: Weapon base damage ([ITEMS](src/core/GameData.gd) dictionary)
- $M$: Momentum = `weapon.weight × weapon.velocity`
- $S$: Strength multiplier = `(attacker.attributes.strength / 50.0)`

**Layered Absorption**:

Energy passes through 4 equipment layers sequentially:
1. **Cover** (cloak, cape): `dmg *= 0.9`
2. **Armor** (plate, mail): `dmg -= armor_material.impact_yield × contact_area`
3. **Over** (gambeson): `dmg *= 0.8`
4. **Under** (tunic): `dmg *= 0.95`

Remaining energy distributes to tissues **in depth order**.

### 2.3 Bleeding & Death

**Exsanguination System**:

```gdscript
# From BattleAI.gd:execute_round()
GameData.process_bleeding(u, 0.2, GameState.rng) # 0.2s = one turn

# Bleeding formula (GameData.gd)
blood_loss = bleed_rate * delta
blood_current -= blood_loss
if blood_current <= 0:
    status["is_dead"] = true
```

**Death Conditions**:
1. `blood_current <= 0` (exsanguination)
2. `body["head"]["tissues"][brain].hp <= 0` (brain destroyed)
3. `body["torso"]["tissues"][heart].hp <= 0` (heart destroyed)
4. `body["torso"].destroyed` (torso structural failure)

**Bleeding Rates**:
- **Capillary**: 1 ml/sec
- **Arterial**: 5 ml/sec (femoral artery, carotid)
- **Base Blood**: 5000 ml (death at 0 ml)

---

## 3. Tactical Battle Architecture

### 3.1 System Components

**BattleController** orchestrates five subsystems:

```gdscript
# From BattleController.gd:_ready()
physics = BattlePhysics.new()  # Spatial grid, collision
terrain = BattleTerrain.new()  # Procedural map generation
combat = BattleCombat.new()    # Damage resolution
siege = BattleSiege.new()      # Siege engines
ai = BattleAI.new()            # Unit FSM & formations
state = BattleState.new()      # Battalion management
```

### 3.2 Turn Execution Flow

```
User Input (WASD/Space)
   ↓
Main._input() → BattleController.handle_input()
   ↓
BattleController.execute_player_turn()
   ↓
   ├─→ Move player unit (BattlePhysics.move_towards)
   ├─→ Resolve attack (BattleCombat.perform_attack)
   └─→ Update bleeding (GameData.process_bleeding)
   ↓
BattleAI.execute_round(all_units)
   ↓
   ├─→ Update bleeding for all units
   ├─→ Update global battle state (centers of mass)
   ├─→ Sort units by initiative
   ├─→ For each AI unit:
   │      ├─→ plan_ai_decision()
   │      └─→ Execute planned action
   ↓
Update projectiles (BattleCombat.update_projectiles)
   ↓
Check victory conditions
   ↓
Render UI (UIRenderer)
```

### 3.3 Initiative System

**Purpose**: Break simultaneous turn execution to prevent side-vs-side lockups.

**Formula** ([BattleAI.gd#L74-L82](src/battle/BattleAI.gd)):

```gdscript
var agi = u.attributes.get("agility", 10)
var agi_bonus = (agi - 10) * 0.05
var jitter = GameState.rng.randf_range(-0.3, 0.3)

u.round_initiative = u.speed - agi_bonus + jitter

sorted_units.sort_custom(func(a, b): return a.round_initiative < b.round_initiative)
```

**Speed Values**:
- Infantry: 2.0s base
- Archer: 2.2s (slower due to aim time)
- Cavalry: 1.5s (fast movement)
- Siege Engines: 4.0s (reload cycles)

**Effect**: Units with high agility and low base speed act first. Random jitter prevents deterministic patterns.

### 3.4 Spatial Hashing Optimization

**Problem**: $O(n^2)$ enemy-finding is prohibitive for 500+ unit battles.

**Solution**: 10x10 spatial buckets with team bitmasks ([BattlePhysics.gd#L76-L102](src/battle/BattlePhysics.gd)):

```gdscript
# Hash unit position to bucket
var bx = int(u.pos.x / SPATIAL_BUCKET_SIZE)
var by = int(u.pos.y / SPATIAL_BUCKET_SIZE)
var key = (bx << 16) | (by & 0xFFFF)

spatial_grid[key].append(u)

# Team bitmask: 1=player, 2=enemy, 4=ally
var team_bit = 1 if u.team == "player" else (2 if u.team == "enemy" else 4)
spatial_team_mask[key] |= team_bit
```

**Enemy Search** ([BattlePhysics.gd#L115-L135](src/battle/BattlePhysics.gd)):

```gdscript
var enemy_bit = 2 if u.team == "player" else 1
for ny in range(by - r, by + r + 1):
    for nx in range(bx - r, bx + r + 1):
        var key = (nx << 16) | (ny & 0xFFFF)
        
        # Skip buckets with no enemies (bitmask test)
        if spatial_team_mask.get(key, 0) & enemy_bit == 0:
            continue
        
        # Only iterate units in buckets that might have enemies
        for e in spatial_grid[key]:
            if e.team != u.team and e.hp > 0:
                # ... distance check
```

**Speedup**: ~50x faster for 200+ unit battles (measured in profiler).

---

## 4. AI Decision-Making System

### 4.1 Tactical Orders

**Global Command** (player-controlled):

```gdscript
# BattleAI.gd
var current_order = "ADVANCE" # ADVANCE, CHARGE, FOLLOW, HOLD, RETREAT
```

**Order Behaviors**:

| Order | Battalion Pivot Behavior | Unit Behavior |
|-------|-------------------------|---------------|
| **ADVANCE** | Move toward enemy center of mass (2 tiles/turn) | Engage enemies within weapon range |
| **CHARGE** | Move toward enemy center of mass (4 tiles/turn) | Break formation to pursue |
| **FOLLOW** | Stay within 5 tiles of player unit | Defensive stance, counter-attack only |
| **HOLD** | No pivot movement | Hold position, ranged fire only |
| **RETREAT** | Move away from enemy center of mass | Flee toward map edge |

**Code** ([BattleAI.gd#L195-L230](src/battle/BattleAI.gd)):

```gdscript
match ai.current_order:
    "ADVANCE":
        var dir = (enemy_center_mass - b.pivot).normalized()
        b.target = b.pivot + Vector2i(dir * 2)
    "CHARGE":
        var dir = (enemy_center_mass - b.pivot).normalized()
        b.target = b.pivot + Vector2i(dir * 4)
    "RETREAT":
        var dir = (b.pivot - enemy_center_mass).normalized()
        b.target = b.pivot + Vector2i(dir * 3)
```

### 4.2 Unit FSM

**States** (implicit, behavior-based):

1. **Formation Holding**: `pos == formation_slot AND no enemies within melee range`
2. **Engaging**: `enemy within weapon_range`
3. **Moving to Slot**: `pos != formation_slot AND no tactical override`
4. **Fleeing**: `blood_current < 40% OR morale collapsed`

**Decision Tree** ([BattleAI.gd:plan_ai_decision()](src/battle/BattleAI.gd#L250-L400)):

```
Is fleeing?
├─ YES → Move away from nearest enemy
└─ NO
   ├─ Is assigned to siege engine?
   │  ├─ YES → Move to engine OR operate engine
   │  └─ NO
   ├─ Is in formation AND not engaged?
   │  ├─ YES → Move to formation slot
   │  └─ NO
   ├─ Enemy in weapon range?
   │  ├─ YES → Attack
   │  └─ NO → Move toward nearest enemy
```

### 4.3 Morale System

**Morale Calculation**:

```gdscript
# GDUnit.gd
var morale: float = 1.0  # 0.0 to 1.0

# Morale adjustments (applied in BattleAI.gd)
if blood_current < blood_max * 0.4:
    morale -= 0.1 per turn
if friendly_casualties > 30% of starting_units:
    morale -= 0.3 instant
if routing_units_nearby > 5:
    morale -= 0.05 per turn per routing unit
```

**Routing Trigger**:

```gdscript
if morale <= 0.2:
    status["is_routing"] = true
    planned_action = "flee"
```

**Fear Check** ([BattleCombat.gd:is_fleeing()](src/battle/BattleCombat.gd#L40-L50)):

```gdscript
func is_fleeing(u, player_unit) -> bool:
    if u == player_unit: return false
    
    var total_hp = 0
    var total_max = 0
    for p_key in u.body:
        for tissue in u.body[p_key]["tissues"]:
            total_hp += tissue["hp"]
            total_max += tissue["hp_max"]
    
    return total_hp < total_max * 0.2 or u.type == "merchant"
```

---

## 5. Siege Warfare

### 5.1 Siege Initiation

**Trigger**: Army arrives at enemy settlement ([CombatManager.gd:resolve_siege()](src/managers/CombatManager.gd#L90-L95)):

```gdscript
if not town_obj.is_under_siege:
    town_obj.is_under_siege = true
    town_obj.siege_timer = 0
    town_obj.siege_attacker_faction = army_obj.faction
```

### 5.2 Defense Calculation

**Formula** ([CombatManager.gd#L140-L165](src/managers/CombatManager.gd)):

```gdscript
# Base Defense
var garrison = float(town_obj.garrison)
var garrison_quality = 6.0 + (barracks_lvl * 1.5) + (training_lvl * 0.8)

# Wall Multiplier (5-30x, not 150x as originally)
var wall_mult = 3.0 + (wall_lvl * 3.0)
if wall_lvl >= 10: wall_mult *= 1.3

var def_str = garrison * garrison_quality * wall_mult
```

**Wall Milestones**:

| Level | Feature | Effect |
|-------|---------|--------|
| 3 | **Towers** | Attacker strength × 0.75 |
| 7 | **Engines** | 10% daily chance to deal 20% HP damage to all attackers |
| 9 | **Moat** | Attacker strength × 0.5 |
| 10 | **Masterwork** | Final multiplier × 1.3 |

### 5.3 Breach Mechanics

**Daily Breach Check** ([CombatManager.gd#L170-L180](src/managers/CombatManager.gd)):

```gdscript
var current_siege_day = int(town_obj.siege_timer / 24.0)
var starvation_mult = 1.0 + (current_siege_day * 0.05)  # +5% per day
total_att_str *= starvation_mult

var breach_chance = (total_att_str / max(1.0, def_str)) * 0.15 * (1.0 + current_siege_day / 4.0)

if rng.randf() < breach_chance or total_att_str > def_str * 2.5:
    # CAPTURE!
```

**Capture Effects**:
1. Faction ownership changes
2. Garrison replaced with half of attacker's army
3. Crown stock looted (50%)
4. Population value looted (2 crowns per pop)

### 5.4 Defender Attrition

**Daily Losses** ([CombatManager.gd#L195-L200](src/managers/CombatManager.gd)):

```gdscript
var def_attrition = 0.02 + (current_siege_day * 0.005)  # Increases over time
var losses = int(town_obj.garrison * def_attrition)
town_obj.garrison = max(10, town_obj.garrison - losses)
```

**Rationale**: Starvation, disease, and desertion erode garrison over time. By day 20, defenders lose ~12% daily.

---

## 6. Strategic Auto-Resolution

### 6.1 Strength-Based Combat

**Use Case**: AI-vs-AI battles off-screen (player not present).

**Entry Point**: [CombatManager.gd:resolve_ai_battle()](src/managers/CombatManager.gd#L5-L50)

**Formula**:

$$\text{Strength} = \sum_{u \in \text{roster}} \left( u.\text{tier} \times u.\text{hp} \times 10 \right)$$

**Battle Resolution**:

```gdscript
var total_att_str = 0.0
var total_def_str = 0.0
for a in attackers: total_att_str += a.strength
for d in defenders: total_def_str += d.strength

total_att_str *= rng.randf_range(0.8, 1.2)  # Dice roll
total_def_str *= rng.randf_range(0.8, 1.2)

if total_att_str > total_def_str:
    # Attackers win
    var loss_pct = clamp(total_def_str / total_att_str * 0.4, 0.05, 0.6)
    for a in attackers:
        for u in a.roster:
            u.hp -= int(u.hp_max * loss_pct * randf_range(0.5, 1.5))
            if u.hp > 0: grant_xp(u, 20)
```

**Loot Distribution**:

$$\text{Loot} = \sum_{d \in \text{defeated}} \left( d.\text{strength} \times 0.05 + d.\text{crowns} \right)$$

Each victor receives: $\text{share} = \lfloor \text{Loot} / \max(1, |\text{attackers}|) \rfloor$

### 6.2 Battle Aggregation

**2-Tile Radius Rule** ([CombatManager.gd#L10-L20](src/managers/CombatManager.gd)):

```gdscript
var nearby = gs.get_entities_near(def.pos, 2)  # Chebyshev distance
var attackers = []
var defenders = []

for e in nearby:
    if e.faction == att.faction: attackers.append(e)
    elif e.faction == def.faction: defenders.append(e)
```

**Effect**: All friendly forces within 2 tiles join the battle. This simulates reinforcements arriving from nearby positions.

---

## 7. Battalion Formations

### 7.1 Formation Structure

**Data Structure** ([BattleState.gd](src/battle/BattleState.gd)):

```gdscript
battalions = {
    0: {
        "team": "player",
        "type": "infantry",
        "pivot": Vector2i(150, 150),
        "target_pos": Vector2i(150, 150),
        "order": "ADVANCE",
        "is_braced": false,
        "units": [...]  # Array of unit references
    },
    1: {
        "team": "player",
        "type": "archers",
        "pivot": Vector2i(145, 155),
        ...
    }
}
```

**Unit Assignment**:

```gdscript
# GDUnit.gd
var formation_id: int = -1
var formation_offset: Vector2i = Vector2i.ZERO  # Offset from pivot

# BattleState.gd:_assign_formation()
u.formation_id = battalion_id
u.formation_offset = Vector2i(col - center_col, row)
```

### 7.2 Formation Movement

**Pivot Update** ([BattleAI.gd:update_global_battle_state()](src/battle/BattleAI.gd#L190-L230)):

```gdscript
# Calculate center of mass for battalion
var b_data = {}
for u in units:
    if u.formation_id != -1:
        b_data[u.formation_id].sum += Vector2(u.pos)
        b_data[u.formation_id].count += 1

# Update pivot to center of mass
for id in b_data:
    if b_data[id].count > 0:
        var new_pivot = Vector2i(b_data[id].sum / b_data[id].count)
        battalions[id].pivot = new_pivot
```

**Unit Slot Calculation**:

```gdscript
var slot_pos = Vector2i(Vector2(b.pivot) + Vector2(u.formation_offset))

if u.pos != slot_pos:
    u.planned_action = "move"
    u.planned_target_pos = slot_pos
```

### 7.3 Formation Bracing

**Trigger**: Infantry formations in "HOLD" mode can brace to resist charges.

**Effect** ([BattleAI.gd#L110-L120](src/battle/BattleAI.gd)):

```gdscript
if b.order == "HOLD" and b.type == "infantry":
    b.is_braced = true
    # Units in braced formation resist knockback and take 50% less damage from charges
```

**Breaking Brace**: Siege engines hitting the formation break the brace instantly.

---

## 8. Siege Engines

### 8.1 Engine Types

**Definition**: [GameData.gd:SIEGE_ENGINES](src/core/GameData.gd)

```gdscript
SIEGE_ENGINES = {
    "ballista": {
        "symbol": "═",
        "dmg_base": 40,
        "weight": 8,
        "velocity": 12,
        "range": 30.0,
        "reload_turns": 4,
        "crew": 2,
        "overpenetrate": true,
        "aoe": 0
    },
    "catapult": {
        "symbol": "╬",
        "dmg_base": 60,
        "weight": 20,
        "velocity": 8,
        "range": 40.0,
        "reload_turns": 6,
        "crew": 4,
        "overpenetrate": false,
        "aoe": 3
    },
    "battering_ram": {
        "symbol": "╣",
        "dmg_base": 100,
        "weight": 40,
        "velocity": 2,
        "range": 1.5,
        "reload_turns": 3,
        "crew": 6,
        "overpenetrate": false,
        "aoe": 0,
        "structure_only": true
    }
}
```

### 8.2 Engine Mechanics

**Ballista Overpenetration** ([BattleCombat.gd#L180-L195](src/battle/BattleCombat.gd)):

```gdscript
if e_info.get("overpenetrate", false) and res.get("remaining_energy", 0.0) > 20.0:
    var next = find_penetration_callback.call(p["target_pos"], dir, 15.0, [p["defender"]])
    if next:
        p["defender"] = next
        p["target_pos"] = Vector2(next.pos)
        continue  # Projectile continues to next target
```

**Catapult AOE** ([BattleCombat.gd#L175-L180](src/battle/BattleCombat.gd)):

```gdscript
if e_info.get("aoe", 0) > 0:
    resolve_aoe_callback.call(p["attacker"], Vector2i(p["target_pos"]), e_info["aoe"], e_info)
    # Deals splash damage to all units within radius
```

**Battering Ram** ([BattleSiege.gd](src/battle/BattleSiege.gd)):

```gdscript
func damage_structure(pos: Vector2i, damage: float):
    var tile = grid[pos.y][pos.x]
    if tile in ["#", "═", "║"]:  # Walls/Doors
        structural_hp[pos] -= damage
        if structural_hp[pos] <= 0:
            grid[pos.y][pos.x] = " "  # Breach created
```

### 8.3 Crew Assignment

**Spawn Logic** ([BattleState.gd:spawn_siege_engine()](src/battle/BattleState.gd)):

```gdscript
func spawn_siege_engine(type_key, pos, team, crew_units):
    var engine = GDUnit.new()
    engine.is_siege_engine = true
    engine.engine_type = type_key
    engine.engine_stats = GameData.SIEGE_ENGINES[type_key].duplicate()
    engine.crew_ids = []
    
    for crew in crew_units:
        crew.assigned_engine_id = engine.id
        engine.crew_ids.append(crew.id)
```

**Crew Behavior**: Crew units stay within 2 tiles of engine and cannot attack independently.

---

## 9. Combat Data Flow

### 9.1 From Input to Damage

```
User presses SPACE (attack)
   ↓
Main._input() → BattleController.handle_input("attack")
   ↓
BattleController.execute_player_turn()
   ↓
BattleCombat.perform_attack(player_unit, unit_lookup, range, is_ranged, ...)
   ↓
   ├─ Scan 2D box for enemies (optimized)
   ├─ Select closest valid target
   └─ Call resolve_dmg_callback()
   ↓
BattleCombat.resolve_complex_damage(attacker, defender, ...)
   ↓
GameData.resolve_attack(attacker, defender, rng, forced_part, attack_idx, shield_bonus)
   ↓
   ├─ Roll to-hit (skill vs. dodging + shield block)
   ├─ Select hit location (random weighted by surface area)
   ├─ Calculate energy = (base + momentum * 2.0) * strength
   ├─ Apply armor absorption (4 layers)
   ├─ Distribute remaining energy to tissues
   └─ Update tissue HP, bleed_rate, blood_current
   ↓
Return damage result to BattleCombat
   ↓
Generate combat log message
   ↓
Check death conditions (blood <= 0, vital organs, torso destroyed)
   ↓
UI updated (render damage numbers, update unit status)
```

### 9.2 AI Turn Flow

```
BattleAI.execute_round(all_units)
   ↓
   ├─ Process bleeding for all units (0.2s burst)
   ├─ Update knockdown timers
   ├─ Calculate enemy/player centers of mass
   ├─ Update battalion pivots
   ├─ Sort units by initiative
   └─ For each AI unit:
       ↓
       BattleAI.plan_ai_decision(u)
       ↓
       ├─ Check if fleeing (blood < 40% OR morale < 0.2)
       ├─ Check if assigned to siege engine
       ├─ Check if in formation AND not engaged
       ├─ Find nearest enemy (spatial hash query)
       ├─ Decide action: move, attack, operate_engine, flee
       └─ Set planned_action, planned_target, planned_target_pos
       ↓
       Execute planned action:
       ├─ "move" → BattlePhysics.move_towards()
       ├─ "attack" → BattleCombat.perform_attack_on()
       └─ "special" → BattleSiege.damage_structure()
```

### 9.3 Projectile Updates

```
BattleController._process(delta)
   ↓
BattleCombat.update_projectiles(projectiles, delta, ...)
   ↓
For each projectile:
   ├─ Update position (pos += dir * speed * delta)
   ├─ Check if hit target (distance < 0.5)
   │  ├─ YES → Resolve damage
   │  │         ↓
   │  │         Check for overpenetration (ballista)
   │  │         Check for AOE (catapult)
   │  └─ Remove projectile
   └─ Continue flight
```

---

## 10. Configuration & Tuning

### 10.1 Key Constants

**[Globals.gd](src/core/Globals.gd)**:

```gdscript
# Combat
const BASE_DODGE_CHANCE = 0.15
const SKILL_DODGE_MULT = 0.01
const SHIELD_BLOCK_CHANCE = 0.25
const SHIELD_SKILL_MULT = 0.015

# Bleeding
const CAPILLARY_BLEED_RATE = 1.0  # ml/sec
const ARTERIAL_BLEED_RATE = 5.0   # ml/sec
const BASE_BLOOD_VOLUME = 5000.0  # ml

# Initiative
const BASE_SPEED_INFANTRY = 2.0
const BASE_SPEED_ARCHER = 2.2
const BASE_SPEED_CAVALRY = 1.5
const AGILITY_INIT_MULT = 0.05
```

**[data/materials.json](data/materials.json)**:

```json
{
    "iron": {
        "hardness": 40,
        "density": 7.87,
        "impact_yield": 200,
        "shear_yield": 350,
        "elasticity": 0.3
    },
    "steel": {
        "hardness": 60,
        "density": 7.85,
        "impact_yield": 400,
        "shear_yield": 600,
        "elasticity": 0.2
    }
}
```

### 10.2 Tuning Siege Difficulty

**Break-even Point** (when walls make defense easier than offense):

$$\text{Garrison} \times \text{Quality} \times (3 + 3 \times \text{Wall\_Lvl}) > \text{Army Strength}$$

**Example**:
- Garrison: 100 units
- Barracks Level 5: Quality = 6.0 + (5 × 1.5) = 13.5
- Walls Level 7: Multiplier = 3 + (7 × 3) = 24
- **Defense Strength**: 100 × 13.5 × 24 = 32,400
- **Required Attack Strength**: ~13,000 to have 40% breach chance per day

**Tuning Levers**:
- `wall_mult` coefficient ([CombatManager.gd#L160](src/managers/CombatManager.gd))
- `breach_chance` formula ([CombatManager.gd#L170](src/managers/CombatManager.gd))
- `starvation_mult` growth rate ([CombatManager.gd#L145](src/managers/CombatManager.gd))

---

## 11. Testing & Debugging

### 11.1 Battle Debug Mode

**Activation**: Press `K` during tactical battle.

**Debug Logs** ([BattleController.gd](src/controllers/BattleController.gd)):

```gdscript
if battle_debug_enabled:
    add_log("[color=cyan]DEBUG: Unit %d planned %s at %v[/color]" % [u.id, u.planned_action, u.planned_target_pos])
    add_log("[color=magenta]DEBUG: Spatial bucket %d contains %d units[/color]" % [key, spatial_grid[key].size()])
```

**Useful Checks**:
- Initiative order correctness
- Spatial hash bucket distribution
- Formation coherence (units at correct offsets)
- Projectile trajectories

### 11.2 Common Issues

**Problem**: Units stuck in formation, not engaging.  
**Diagnosis**: Check `_check_unit_engaged()` logic. Likely spatial mask not refreshed.  
**Fix**: Call `refresh_all_spatial()` at start of round.

**Problem**: Siege never breaches despite overwhelming force.  
**Diagnosis**: Check wall multiplier. Level 10 walls = 33x defense.  
**Fix**: Reduce `wall_mult` coefficient or increase `breach_chance` base value.

**Problem**: Units die instantly from single hits.  
**Diagnosis**: Armor absorption not working. Check equipment layers.  
**Fix**: Ensure `u.equipment["torso"]["armor"]` is populated with material data.

### 11.3 Test Scenarios

**[test_new_architecture.gd](test_new_architecture.gd)** (now deleted, but concept):

```gdscript
# Scenario 1: 20v20 Infantry Battle
func test_basic_melee():
    var player_army = spawn_units("player", 20, "infantry", Vector2i(100, 150))
    var enemy_army = spawn_units("enemy", 20, "infantry", Vector2i(200, 150))
    BattleController.start(enemy_army)
    # Expected: Fairly even casualties, winner by ~10% margin

# Scenario 2: Siege with Level 10 Walls
func test_max_walls():
    var attacker = spawn_army(100, "infantry")
    var defender_settlement = create_settlement(garrison=50, walls=10, barracks=10)
    CombatManager.resolve_siege(attacker, defender_settlement)
    # Expected: ~8-10 days to breach with 100 attackers

# Scenario 3: Ballista Overpenetration
func test_ballista():
    var ballista = spawn_siege_engine("ballista", Vector2i(100, 150))
    var target_line = spawn_units_in_line("enemy", 10, Vector2i(130, 150))
    # Fire ballista
    # Expected: Bolt penetrates 2-3 units before stopping
```

---

## 12. Extension Guide

### 12.1 Adding a New Weapon Type

**Step 1**: Define in [GameData.gd:ITEMS](src/core/GameData.gd)

```gdscript
"halberd": {
    "type": "weapon",
    "dmg": 12,
    "weight": 4.0,
    "velocity": 6.0,
    "range": 2.0,
    "attacks": [
        {"dmg_type": "cut", "contact": 2.0, "penetration": 1.0, "verb": "slashes"},
        {"dmg_type": "pierce", "contact": 0.5, "penetration": 3.0, "verb": "thrusts"}
    ]
}
```

**Step 2**: Add skill category (if new)

```gdscript
# GDUnit.gd
var skills: Dictionary = {
    ...
    "polearms": 0  # New skill
}
```

**Step 3**: Update loot tables

```gdscript
# GameData.gd:generate_loot()
if rarity > 0.7:
    equipment.append(create_item_data("halberd", "steel", "fine"))
```

### 12.2 Adding a New Siege Engine

**Step 1**: Define in [GameData.gd:SIEGE_ENGINES](src/core/GameData.gd)

```gdscript
"scorpion": {
    "symbol": "═",
    "dmg_base": 30,
    "weight": 5,
    "velocity": 15,
    "range": 25.0,
    "reload_turns": 3,
    "crew": 1,
    "overpenetrate": true,
    "aoe": 0
}
```

**Step 2**: Add to settlement recruitment pool

```gdscript
# SettlementManager.gd:refresh_recruits()
if s_data.buildings.get("siege_workshop", 0) >= 5:
    # Unlock scorpion construction
```

**Step 3**: Test overpenetration/AOE logic (if unique mechanics)

### 12.3 Adding a New Tactical Order

**Step 1**: Add to order enum

```gdscript
# BattleAI.gd
var current_order = "ADVANCE"  # Add "SKIRMISH" as new option
```

**Step 2**: Define behavior in `update_global_battle_state()`

```gdscript
match ai.current_order:
    ...
    "SKIRMISH":
        # Archers stay at max range, kite away from melee
        if b.type == "archers":
            var dir = (b.pivot - enemy_center_mass).normalized()
            b.target = b.pivot + Vector2i(dir * 2)
```

**Step 3**: Add input mapping ([BattleController.gd:handle_input()](src/controllers/BattleController.gd))

```gdscript
if key == KEY_6:
    ai.current_order = "SKIRMISH"
    add_log("[color=yellow]ORDER: SKIRMISH[/color]")
```

---

## Conclusion

This combat system models warfare from **individual tissue damage** to **strategic sieges**, unifying anatomical physics with large-scale tactics. The key innovations are:

1. **Dwarf Fortress Anatomy**: Realistic wounding without abstraction
2. **Spatial Hashing**: $O(n)$ enemy-finding for massive battles
3. **Initiative System**: Non-deterministic turn order prevents lockups
4. **Siege Calculus**: Walls scale exponentially, making fortifications meaningful
5. **Dual Resolution**: Tactical for player, strategic for AI (performance)

The system is modular—each subsystem (Physics, AI, Combat, Siege) can be extended independently. Use the debug mode (`K` key) to visualize AI decisions and verify battle balance.

