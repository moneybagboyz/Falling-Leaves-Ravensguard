# Combat System Migration Plan
## Converting from WEGO Formation System to Dwarf Fortress Anatomy System

**Target:** Migrate from current WEGO simultaneous-resolution combat to GitHub repo's initiative-based, tissue-damage combat system.

**Date:** March 3, 2026  
**Status:** ~40% Complete (Tissue penetration implemented, blood/initiative/armor layers pending)

## Implementation Status (as of March 9, 2026)

| Component | Status | Location |
|-----------|--------|----------|
| **Tissue penetration system** | ✅ IMPLEMENTED | [combat_resolver.gd:292-350](../src/simulation/combat/combat_resolver.gd) |
| **Body zones with tissue layers** | ✅ IMPLEMENTED | [data/body_zones/](../data/body_zones/) (12 zones) |
| **Wound tracking (tissues_reached, bone_fractured, organ_damaged)** | ✅ IMPLEMENTED | [combatant_state.gd:92-94](../src/simulation/combat/combatant_state.gd) |
| **Blood volume system (blood_current, blood_max)** | ❌ NOT IMPLEMENTED | Still uses simple `bleeding: float` |
| **Initiative-based turn queue** | ❌ NOT IMPLEMENTED | Still WEGO simultaneous resolution |
| **4-layer armor system (under/over/armor/cover)** | ❌ NOT IMPLEMENTED | Still single armor slot |
| **Material properties system** | ❌ NOT IMPLEMENTED | No hardness/yield_strength lookups |

**Summary:** Tissue damage mechanics are working. Blood loss, initiative, and layered armor remain to be implemented.

---

## Executive Summary

### Current System (Your Workspace)
- **Turn Model:** WEGO simultaneous (all orders → resolve together)
- **Scale:** Formation-centric squad commands
- **Health:** Single HP pool + wound severity array per body zone
- **Damage:** Momentum - DR = damage to health
- **Death:** `health <= 0` or `shock >= 1.0`

### Target System (GitHub Repo)
- **Turn Model:** Initiative-based queue (speed - agility + jitter)
- **Scale:** Individual unit AI with full autonomy
- **Health:** No HP—tissue-by-tissue damage tracking
- **Damage:** Energy penetrates 4 armor layers → damages tissues in depth order
- **Death:** Vital organ destruction, blood loss, or structural collapse

---

## Phase 1: Data Structure Migration

### 1.1 Body System Overhaul

**Current:**
```gdscript
# CombatantState
body_zones: Dictionary = {
  "head": [
    {severity: "wound", bleed: 0.08, pain: 0.20, tissues_reached: ["skin", "muscle"]}
  ]
}
```

**Target:**
```gdscript
# GDUnit (from repo)
body: Dictionary = {
  "head": {
    "tissues": [
      {type: "skin", hp: 20, hp_max: 20, thick: 3},
      {type: "bone", hp: 40, hp_max: 40, thick: 8, is_skull: true},
      {type: "brain", hp: 15, hp_max: 15, is_vital: true}
    ]
  },
  "torso": {
    "tissues": [
      {type: "skin", hp: 30, hp_max: 30, thick: 4},
      {type: "bone", hp: 60, hp_max: 60, thick: 12, is_ribcage: true},
      {type: "heart", hp: 25, hp_max: 25, is_vital: true, is_arterial: true},
      {type: "lungs_l", hp: 20, hp_max: 20},
      {type: "lungs_r", hp: 20, hp_max: 20},
      {type: "gut", hp: 15, hp_max: 15},
      {type: "liver", hp: 18, hp_max: 18},
      {type: "spine", hp: 50, hp_max: 50, is_spine: true}
    ]
  },
  # ... limbs with muscle, bone, artery, nerve
}
```

**Migration Steps:**
1. Create `data/body_plans/human_detailed.json` with full tissue definitions
2. Add tissue flags: `is_vital`, `is_spine`, `is_arterial`, `is_muscle`
3. Create conversion function: `migrate_person_body(person: PersonState) -> Dictionary`
4. Update `CombatantState.from_person()` to build detailed body structure

**Files to Modify:**
- `src/simulation/combat/combatant_state.gd`
- `src/data/person_state.gd`
- Create: `data/body_plans/human_detailed.json`

---

### 1.2 Equipment Layer System

**Current:**
```gdscript
equipped_armor: Array[String] = ["gambeson", "chainmail"]  # item IDs
```

**Target:**
```gdscript
equipment: Dictionary = {
  "head": {
    "under": null,  # coif
    "over": null,   # padding
    "armor": {      # helmet
      "name": "iron_helm",
      "material": "iron",
      "prot": 15,
      "coverage": 0.85
    },
    "cover": null   # hood/cloak
  },
  "torso": {...},
  "main_hand": {
    "name": "longsword",
    "material": "steel",
    "dmg": 18,
    "weight": 1.5,
    "velocity": 8,
    "attacks": [
      {name: "Slash", dmg_mult: 1.0, dmg_type: "cut", contact: 15, penetration: 8},
      {name: "Thrust", dmg_mult: 0.8, dmg_type: "pierce", contact: 5, penetration: 20}
    ]
  },
  "off_hand": {...},  # shield
  "ammo": null
}
```

**Migration Steps:**
1. Expand weapon JSON to include multiple attack modes
2. Create 4-layer armor slot system per body part
3. Add material properties lookup: `MATERIALS.steel.hardness`, `.impact_yield`, `.shear_yield`
4. Update equipment UI to show layered armor

**Files to Create:**
- `data/materials.json` (hardness, yield strength, elasticity)
- `data/weapons/detailed/*.json` (multi-attack definitions)
- `data/armor/layered/*.json` (layer assignments: under/over/armor/cover)

**Files to Modify:**
- `src/simulation/combat/combatant_state.gd` - equipment structure
- `src/ui/character_sheet.gd` - display layered armor

---

### 1.3 Blood & Bleeding System

**Current:**
```gdscript
bleeding: float = 0.0  # sum of wound bleed rates
# Ticked at end of turn
```

**Target:**
```gdscript
blood_max: float = 5000.0     # ml
blood_current: float = 5000.0
bleed_rate: float = 0.0       # ml/sec (recalculated when tissues damaged)

# Constants
CAPILLARY_BLEED_RATE = 1.0   # ml/sec
ARTERIAL_BLEED_RATE = 5.0    # ml/sec
BASE_BLOOD_VOLUME = 5000.0

# Death threshold
if blood_current <= 0:
  is_dead = true
elif blood_current < blood_max * 0.3:
  is_downed = true
```

**Migration Steps:**
1. Replace `bleeding` with `blood_current` and `bleed_rate`
2. Tag arterial tissues in body plan JSON
3. Recalculate `bleed_rate` whenever tissue damaged: `bleed_rate += dmg × (ARTERIAL if arterial else CAPILLARY)`
4. Process bleeding per tick (not per turn): `blood_current -= bleed_rate × delta_time`

**Files to Modify:**
- `src/simulation/combat/combatant_state.gd`
- `src/simulation/combat/combat_resolver.gd` - replace tick_bleed logic
- `data/body_plans/*.json` - add `is_arterial` flags

---

## Phase 2: Damage Resolution Engine

### 2.1 Replace Simple DR with Energy Penetration

**Current Algorithm:**
```gdscript
# combat_resolver.gd:_do_attack()
momentum = weapon.striking_mass × VELOCITY_FACTORS[weapon.velocity]
damage = momentum

for armor_item in target_armor:
  dr = armor_item.dr × QUALITY_DR_MULTIPLIER[armor_item.quality]
  damage -= dr
  if damage <= 0: break

hit_data.damage = max(0, damage)
target.health -= hit_data.damage
```

**Target Algorithm:**
```gdscript
# GameData.gd:resolve_attack()
# 1. Calculate base energy
base_dmg = weapon.dmg × attack.dmg_mult
momentum = weapon.weight × weapon.velocity
strength_mult = 1.0 + (attacker.attributes.strength - 10) × 0.1
current_dmg = (base_dmg + momentum × 2.0) × strength_mult

# 2. Penetrate 4 layers sequentially
for layer in ["cover", "armor", "over", "under"]:
  var armor = defender.equipment[target_part][layer]
  if not armor: continue
  
  var material = MATERIALS[armor.material]
  var absorbed = 0.0
  
  if attack.dmg_type == "blunt":
    absorbed = armor.prot × (material.impact_yield / 100.0) × (contact_area / 10.0)
    current_dmg -= (absorbed - absorbed × 0.1)  # 10% bruising through
  elif attack.dmg_type == "cut":
    absorbed = armor.prot × (material.shear_yield / 100.0) / attack.penetration
    current_dmg -= absorbed
  elif attack.dmg_type == "pierce":
    absorbed = armor.prot × (material.hardness / 100.0) / attack.penetration
    current_dmg -= absorbed
  
  if current_dmg <= 0: return  # Attack stopped

# 3. Distribute remaining energy to tissues in depth order
for tissue in target_part.tissues:
  var resistance = tissue.thick
  if tissue.type == "bone": resistance *= 2.0
  
  var energy_loss = min(current_dmg, resistance)
  var dmg = int(energy_loss × 2.0)
  
  tissue.hp -= dmg
  current_dmg -= energy_loss
  
  if tissue.hp <= 0:
    if tissue.is_vital:
      defender.status.is_dead = true
    if tissue.is_arterial:
      defender.bleed_rate += ARTERIAL_BLEED_RATE
    elif dmg > 5:
      defender.bleed_rate += CAPILLARY_BLEED_RATE
  
  if current_dmg <= 0: break
```

**Migration Steps:**
1. Create `src/core/GameData.gd` with `resolve_attack()` function
2. Import materials database with hardness/yield values
3. Update weapon JSON with `dmg_type`, `contact`, `penetration` per attack
4. Replace `CombatResolver._do_attack()` with call to `GameData.resolve_attack()`

**New Files:**
- `src/core/GameData.gd` - centralized combat math
- `data/materials.json`

**Modified Files:**
- `src/simulation/combat/combat_resolver.gd`

---

### 2.2 Hit Location Selection

**Current:**
```gdscript
# Implicit from formation positions
var target_zone = "torso"  # or random
```

**Target:**
```gdscript
# Weighted random by body part surface area
var roll = rng.randf()
var part_key = ""
if forced_part != "":
  part_key = forced_part
elif roll < 0.10:
  part_key = "head"
elif roll < 0.50:
  part_key = "torso"
elif roll < 0.65:
  part_key = "r_arm"
elif roll < 0.80:
  part_key = "l_arm"
elif roll < 0.90:
  part_key = "r_leg"
else:
  part_key = "l_leg"
```

**Migration Steps:**
1. Add weighted hit location selection to `GameData.resolve_attack()`
2. Support forced hit location for called shots
3. Update combat log to show specific hit locations

---

### 2.3 Material Matchup System

**Current:** None (flat DR)

**Target:**
```gdscript
# data/materials.json
{
  "steel": {
    "hardness": 50,
    "impact_yield": 60,
    "shear_yield": 45,
    "elasticity": 30
  },
  "iron": {
    "hardness": 40,
    "impact_yield": 50,
    "shear_yield": 35,
    "elasticity": 25
  },
  "leather": {
    "hardness": 10,
    "impact_yield": 15,
    "shear_yield": 20,
    "elasticity": 60
  }
}

# In damage calc:
var wpn_mat = MATERIALS[weapon.material]
var armor_mat = MATERIALS[armor.material]
var matchup_mult = 1.0 + (wpn_mat.hardness - armor_mat.hardness) / 100.0
current_dmg *= matchup_mult
```

**Migration Steps:**
1. Create materials database JSON
2. Assign materials to all weapons and armor
3. Apply matchup multiplier in damage calculation

---

## Phase 3: Turn Structure Migration

### 3.1 Replace WEGO with Initiative Queue

**Current Flow:**
```
1. All formations set orders (advance/hold/retreat)
2. All combatants move simultaneously (position snapshot)
3. All combatants attack simultaneously
4. Resolve damage in parallel
5. Tick bleeding/stamina recovery
```

**Target Flow:**
```
1. Calculate initiative for each unit: speed - (agility × 0.05) + rng.randf_range(-0.2, 0.2)
2. Sort units by initiative (highest first)
3. for each unit in initiative_queue:
     a. Unit decides action (AI or player)
     b. Execute action (move OR attack)
     c. Resolve damage immediately
     d. Check death conditions
4. Process bleeding for all units (every 0.1 sec burst)
5. Check victory conditions
```

**Migration Steps:**
1. Add `initiative: float` to `CombatantState`
2. Create `src/battle/BattleController.gd` to orchestrate initiative queue
3. Create `src/battle/BattleAI.gd` for individual unit AI decisions
4. Remove formation-level order propagation
5. Update combat UI to show initiative order

**New Files:**
- `src/battle/BattleController.gd` - main orchestrator
- `src/battle/BattleAI.gd` - unit AI state machine
- `src/battle/BattlePhysics.gd` - spatial queries
- `src/battle/BattleCombat.gd` - attack execution

**Modified Files:**
- `src/simulation/combat/combat_resolver.gd` - becomes thin wrapper
- `src/ui/combat_view/combat_view.gd` - show initiative queue

---

### 3.2 Individual Unit AI

**Current:**
```gdscript
# Formations have orders; individuals follow formation
for fid in battle.formations:
  var f = battle.formations[fid]
  if f.order == "advance":
    _move_advance(f, battle, map_data)
```

**Target:**
```gdscript
# Each unit has independent AI state machine
const STATE_IDLE = 0
const STATE_SEEK = 1
const STATE_ENGAGE = 2
const STATE_FLEE = 3

func plan_ai_decision(unit, all_units, player_unit):
  # Check fleeing condition
  if unit.blood_current < unit.blood_max × 0.4:
    return {action: "flee", state: STATE_FLEE}
  
  # Find nearest enemy
  var nearest = _find_nearest_enemy(unit, all_units)
  if not nearest:
    return {action: "idle", state: STATE_IDLE}
  
  var dist = unit.pos.distance_to(nearest.pos)
  var range_val = _get_weapon_range(unit)
  
  if dist <= range_val:
    return {action: "attack", target: nearest, state: STATE_ENGAGE}
  else:
    return {action: "move", target_pos: nearest.pos, state: STATE_SEEK}
```

**Migration Steps:**
1. Create `BattleAI.gd` with state machine logic
2. Add enemy-finding spatial queries (O(n) box search)
3. Implement flee logic based on blood loss
4. Update each unit independently in initiative order

---

### 3.3 Spatial Optimization

**Current:** Simple tile position dictionary

**Target:** Spatial hashing for large battles
```gdscript
# BattlePhysics.gd
var spatial_grid: Dictionary = {}  # key: "bucket_x,bucket_y", value: [units]
const BUCKET_SIZE = 10

func register_unit(u):
  var bx = int(u.pos.x / BUCKET_SIZE)
  var by = int(u.pos.y / BUCKET_SIZE)
  var key = "%d,%d" % [bx, by]
  if not spatial_grid.has(key):
    spatial_grid[key] = []
  spatial_grid[key].append(u)

func find_units_in_radius(center: Vector2i, radius: float) -> Array:
  var r_buckets = int(ceil(radius / BUCKET_SIZE))
  var hits = []
  for dy in range(-r_buckets, r_buckets + 1):
    for dx in range(-r_buckets, r_buckets + 1):
      var key = "%d,%d" % [center.x / BUCKET_SIZE + dx, center.y / BUCKET_SIZE + dy]
      if spatial_grid.has(key):
        hits.append_array(spatial_grid[key])
  return hits
```

**Migration Steps:**
1. Create `BattlePhysics.gd` with spatial hashing
2. Register/unregister units on move
3. Use spatial queries for enemy-finding instead of full iteration

---

## Phase 4: Combat Flow Integration

### 4.1 Battle Initialization

**Current:**
```gdscript
# In CombatManager or similar
var battle = BattleState.new()
battle.combatants = {}
for person in party.members:
  var c = CombatantState.from_person(person)
  battle.combatants[c.combatant_id] = c
```

**Target:**
```gdscript
# BattleController._ready()
var battle_controller = BattleController.new()
battle_controller.initialize(player_army, enemy_army, terrain_type)

# Inside initialize:
for soldier in player_army.roster:
  var unit = GDUnit.new()
  unit.name = soldier.name
  unit.body = GameData.build_body_from_plan(soldier.body_plan_id)
  unit.equipment = GameData.equip_from_person(soldier)
  unit.team = "player"
  unit.pos = _get_spawn_position(unit, "player")
  units.append(unit)
  physics.register_unit(unit)
```

**Migration Steps:**
1. Create `BattleController.gd:initialize()` function
2. Build detailed body structures for all combatants
3. Equip layered armor from inventory
4. Position units on battlefield (formation spawn zones)

---

### 4.2 Turn Execution Loop

**Current:**
```gdscript
# combat_resolver.gd
static func resolve_turn(battle: BattleState, map_data: Dictionary, seed: int) -> bool:
  # WEGO simultaneous resolution
  _move_all_formations()
  _attack_all_combatants()
  _tick_status_effects()
  return _check_end_conditions()
```

**Target:**
```gdscript
# BattleController.gd
func execute_round():
  turn += 1
  
  # 1. Update bleeding (burst tick)
  for u in units:
    if u.hp > 0:
      u.blood_current -= u.bleed_rate × 0.1
      if u.blood_current <= 0:
        u.status.is_dead = true
  
  # 2. Calculate initiative
  var initiative_queue = []
  for u in units:
    if u.hp > 0 and not u.status.is_downed:
      u.initiative = u.speed - (u.attributes.agility × 0.05) + rng.randf_range(-0.2, 0.2)
      initiative_queue.append(u)
  initiative_queue.sort_custom(func(a, b): return a.initiative > b.initiative)
  
  # 3. Execute each unit's turn
  for u in initiative_queue:
    if u == player_unit:
      # Wait for player input
      await player_turn_complete
    else:
      # AI decides action
      var decision = ai.plan_ai_decision(u, units, player_unit)
      _execute_action(u, decision)
  
  # 4. Check victory
  check_battle_end()
```

**Migration Steps:**
1. Implement `execute_round()` in `BattleController.gd`
2. Add initiative calculation and sorting
3. Separate player input flow from AI execution
4. Process bleeding as burst tick at round start

---

### 4.3 Player Input Handling

**Current:**
```gdscript
# Player issues orders to formations during planning phase
func _on_formation_order_selected(formation_id, order):
  battle.formations[formation_id].order = order

# Then resolve_turn() executes
```

**Target:**
```gdscript
# Player controls individual unit on their initiative turn
func _input(event):
  if not is_player_turn():
    return
  
  if event.is_action_pressed("move_up"):
    try_move(player_unit, player_unit.pos + Vector2i(0, -1))
    execute_round()
  elif event.is_action_pressed("attack"):
    var target = get_target_under_cursor()
    if target:
      combat.resolve_complex_damage(player_unit, target, "", 0)
      execute_round()
  elif event.is_action_pressed("wait"):
    execute_round()
```

**Migration Steps:**
1. Add direct player unit control (WASD movement)
2. Add attack targeting (Space + click or auto-target nearest)
3. Remove formation order UI
4. Add initiative queue UI showing turn order

---

## Phase 5: Combat Subsystems

### 5.1 Siege Engines

**Current:** Not implemented

**Target:**
```gdscript
# GameData.gd:SIEGE_ENGINES
{
  "catapult": {
    "dmg_base": 80,
    "dmg_type": "blunt",
    "range": 30.0,
    "aoe": 3,  # radius
    "weight": 50,
    "velocity": 10,
    "reload_turns": 4
  },
  "ballista": {
    "dmg_base": 60,
    "dmg_type": "pierce",
    "range": 25.0,
    "overpenetrate": true,  # hits multiple units in line
    "weight": 15,
    "velocity": 30,
    "reload_turns": 3
  }
}

# Special damage resolution
func resolve_engine_damage(engine_key, defender, rng):
  var e = SIEGE_ENGINES[engine_key]
  var momentum = e.weight × e.velocity
  var energy = e.dmg_base + momentum × 5.0  # Siege engines hit MUCH harder
  
  # Ignore most armor
  for tissue in defender.body[target_part].tissues:
    var dmg = int(energy × 2.0)  # Catastrophic
    tissue.hp -= dmg
    if tissue.hp <= 0 and tissue.is_vital:
      defender.status.is_dead = true
```

**Migration Steps:**
1. Add siege engine definitions to `GameData.gd`
2. Create `resolve_engine_damage()` function
3. Implement AOE damage for catapults
4. Implement overpenetration for ballistas
5. Add siege engine units to battle spawning

---

### 5.2 Detailed Combat Logging

**Current:**
```
"Soldier A hits Soldier B for 15 damage"
```

**Target:**
```
"Your Steel Longsword slashes Bandit's left arm, cutting the skin and tearing the muscle (partially deflected by the leather vest)! (-12 HP)"
"The Orc's Iron Mace smashes your head, fracturing the bone and rupturing internal organs! (-40 HP)"
```

**Migration Steps:**
1. Generate dynamic verb selection based on damage type and amount:
   - Cut: "scratches" / "cuts" / "slashes" / "cleaves"
   - Blunt: "taps" / "bashes" / "smashes" / "pulverizes"
   - Pierce: "pokes" / "stabs" / "pierces" / "impales"
2. List armor layers that absorbed damage
3. List tissues damaged (skin, muscle, bone, organs)
4. Add impact descriptions for critical hits

---

### 5.3 Death & Incapacitation

**Current:**
```gdscript
if target.health <= 0:
  target.is_dead = true
elif target.shock >= 1.0:
  target.is_incapacitated = true
```

**Target:**
```gdscript
# Multiple death conditions
if defender.blood_current <= 0:
  defender.status.is_dead = true
  log("bleeds out and dies!")
elif any_vital_organ_destroyed(defender):
  defender.status.is_dead = true
  log("dies instantly from vital organ failure!")
elif spine_destroyed(defender):
  defender.status.is_paralyzed = true
  log("collapses, paralyzed!")
elif defender.pain > 80:
  defender.status.is_downed = true
  log("falls unconscious from pain!")
elif defender.blood_current < defender.blood_max × 0.3:
  defender.status.is_downed = true
  log("collapses from blood loss!")
```

**Migration Steps:**
1. Add multiple death condition checks in damage resolution
2. Add `is_paralyzed` status for spine destruction
3. Update death messages to reflect cause
4. Track critical events array: `["bone_fractured", "organ_failure:heart"]`

---

## Phase 6: Data Migration

### 6.1 Convert Existing Content

**Armor:**
```json
// OLD: data/armor/gambeson.json
{
  "id": "gambeson",
  "name": "Gambeson",
  "dr": 8,
  "quality": "standard"
}

// NEW: data/armor/gambeson.json
{
  "id": "gambeson",
  "name": "Gambeson",
  "material": "textile",
  "layer": "over",
  "prot": 8,
  "coverage": 0.90,
  "slots": ["torso", "arms"],
  "weight": 3.0
}
```

**Weapons:**
```json
// OLD: data/weapons/longsword.json
{
  "id": "longsword",
  "name": "Longsword",
  "damage": 18,
  "reach": "medium"
}

// NEW: data/weapons/longsword.json
{
  "id": "longsword",
  "name": "Longsword",
  "material": "steel",
  "dmg": 15,
  "weight": 1.5,
  "velocity": 8,
  "range": 1.5,
  "attacks": [
    {
      "name": "Slash",
      "dmg_mult": 1.0,
      "dmg_type": "cut",
      "contact": 15,
      "penetration": 8
    },
    {
      "name": "Thrust",
      "dmg_mult": 0.8,
      "dmg_type": "pierce",
      "contact": 5,
      "penetration": 18
    },
    {
      "name": "Pommel Strike",
      "dmg_mult": 0.4,
      "dmg_type": "blunt",
      "contact": 8,
      "penetration": 3
    }
  ]
}
```

**Migration Script:**
```gdscript
# tools/migrate_item_data.gd
static func migrate_all_items():
  _migrate_armor_files()
  _migrate_weapon_files()
  _migrate_material_data()

static func _migrate_weapon_files():
  var weapon_dir = "res://data/weapons/"
  var files = DirAccess.get_files_at(weapon_dir)
  for file in files:
    if file.ends_with(".json"):
      var old_data = JSON.parse_file(weapon_dir + file)
      var new_data = _convert_weapon(old_data)
      var new_file = FileAccess.open(weapon_dir + file, FileAccess.WRITE)
      new_file.store_string(JSON.stringify(new_data, "\t"))
```

**Migration Steps:**
1. Create migration script in `tools/migrate_item_data.gd`
2. Backup existing data directory
3. Run migration to convert all armor JSON
4. Run migration to convert all weapon JSON
5. Manually review/tweak generated data
6. Create `data/materials.json` from scratch

---

### 6.2 Save Game Compatibility

**Challenge:** Existing save games have old combat data structure

**Options:**
1. **Breaking change:** Increment save version, reject old saves
2. **Migration layer:** Detect old format, convert on load
3. **Parallel systems:** Keep both systems, detect which to use

**Recommended:** Option 1 (clean break) for Phase 4
- Add version check in save/load
- Display "incompatible save version" message
- Document breaking change in changelog

---

## Phase 7: UI Updates

### 7.1 Combat View Changes

**Current UI Elements:**
- Formation order buttons (advance/hold/retreat/charge/flank)
- Formation morale bars
- WEGO phase indicator (planning/resolving/results)

**New UI Elements:**
- Initiative queue display (vertical list of unit portraits)
- Current unit highlight
- Body damage paperdoll (show tissue HP per zone)
- Detailed damage log with expandable tissue info
- Blood loss indicator (red bar, 0-5000ml)
- Status effects: bleeding, downed, paralyzed

**Migration Steps:**
1. Remove formation UI controls
2. Add initiative queue widget to top-right
3. Add unit health display showing:
   - Blood remaining vs max
   - Injured body parts (color-coded by severity)
   - Active status effects
4. Expand combat log to show detailed hit descriptions
5. Add body zone target selection (for called shots)

---

### 7.2 Character Sheet Updates

**Add:**
- Layered armor view (4 slots per body part)
- Tissue HP breakdown per body zone
- Blood volume / bleed rate
- Weapon attack mode selector

---

## Phase 8: Testing Strategy

### 8.1 Unit Tests

**Create Test Suite:**
```gdscript
# tests/test_anatomy_combat.gd
func test_basic_hit():
  var attacker = _create_test_unit("soldier")
  var defender = _create_test_unit("bandit")
  
  var res = GameData.resolve_attack(attacker, defender, rng, "torso", 0)
  
  assert(res.hit == true)
  assert(res.tissues_hit.size() > 0)
  assert(defender.body.torso.tissues[0].hp < 30)  # Skin damaged

func test_vital_organ_death():
  var attacker = _create_test_unit("knight")
  var defender = _create_test_unit("peasant")
  
  # Force hit to head with heavy weapon
  var res = GameData.resolve_attack(attacker, defender, rng, "head", 0)
  
  if res.tissues_hit.has("brain"):
    assert(defender.status.is_dead == true)

func test_bleeding_death():
  var unit = _create_test_unit("soldier")
  unit.bleed_rate = 10.0  # Severe arterial bleed
  
  for i in range(100):  # 10 seconds
    unit.blood_current -= unit.bleed_rate × 0.1
  
  assert(unit.blood_current <= 0)
  assert(unit.status.is_dead == true)

func test_armor_layering():
  var attacker = _create_test_unit("soldier")
  var defender = _create_test_unit("knight")
  
  # Knight has 4 armor layers
  defender.equipment.torso.under = {material: "linen", prot: 2}
  defender.equipment.torso.over = {material: "textile", prot: 8}
  defender.equipment.torso.armor = {material: "steel", prot: 25}
  defender.equipment.torso.cover = {material: "leather", prot: 5}
  
  var res = GameData.resolve_attack(attacker, defender, rng, "torso", 0)
  
  assert(res.armor_layers.size() > 0)
  assert(res.final_dmg < 10)  # Heavy armor blocked most damage
```

**Test Files to Create:**
- `tests/test_damage_resolution.gd`
- `tests/test_bleeding_system.gd`
- `tests/test_initiative_queue.gd`
- `tests/test_armor_penetration.gd`
- `tests/test_material_matchups.gd`

---

### 8.2 Integration Tests

**Test Scenarios:**
1. **1v1 duel:** Player vs single enemy, verify turn order
2. **Armor test:** Heavy armor vs light armor damage difference
3. **Bleed-out test:** Arterial wound leads to death in ~50 turns
4. **Vital organ test:** Head/heart hit causes instant death
5. **Siege engine test:** Catapult AOE hits multiple units
6. **Overpenetration test:** Ballista pierces through multiple enemies
7. **Large battle:** 50v50, verify spatial hashing performance

---

### 8.3 Balance Testing

**Metrics to Track:**
1. Average turns to kill (TTK) by equipment tier
2. Armor effectiveness curves (DR vs damage reduction)
3. Bleed-out time from various wound severities
4. Hit rate by skill level
5. Initiative spread (ensure no unit stuck always going last)

**Tuning Knobs:**
```gdscript
# Globals.gd or GameData.gd
const BASE_HIT_CHANCE = 0.70  # Currently 0.65, may be too low
const SKILL_HIT_BONUS = 0.02  # Per skill level
const ARTERIAL_BLEED_MULT = 5.0  # vs capillary
const STRENGTH_DMG_MULT = 0.1  # Per point above 10
const ARMOR_ABS_MULT = 1.0  # Global armor effectiveness tuner
```

---

## Phase 9: Deprecation & Cleanup

### 9.1 Remove Old System

**Files to Delete:**
- `src/simulation/combat/combat_resolver.gd` (old WEGO resolver)
- `src/simulation/combat/formation_state.gd` (if fully replaced)

**Files to Archive:**
- Move to `deprecated/` folder instead of delete (for reference)

---

### 9.2 Update Documentation

**Files to Update:**
- `systems-reference.md` - rewrite Combat System section
- `README.md` - update combat description
- Create: `COMBAT_EXPLANATION.md` (copy from repo, adapt to your game)

---

## Phase 10: Performance Optimization

### 10.1 Profiling Targets

**Bottlenecks to Watch:**
1. Damage resolution (10+ function calls per hit)
2. Spatial queries (O(n²) without hashing)
3. Blood ticking (every frame vs every 0.1s burst)
4. Combat log generation (string concatenation)

**Optimization Strategies:**
1. Object pooling for projectiles
2. Spatial hashing (10x10 buckets)
3. Burst ticking (process bleeding in 0.1s batches)
4. Combat log buffering (append to array, render once per frame)

---

### 10.2 Strategic Auto-Resolution

**For AI-vs-AI battles off-screen:**
```gdscript
# CombatManager.gd:resolve_ai_battle()
static func resolve_ai_battle(att: Army, def: Army):
  var total_att_str = _calculate_strength(att.roster)
  var total_def_str = _calculate_strength(def.roster)
  
  total_att_str *= rng.randf_range(0.8, 1.2)
  total_def_str *= rng.randf_range(0.8, 1.2)
  
  if total_att_str > total_def_str:
    # Attackers win
    var loss_pct = clamp(total_def_str / total_att_str × 0.4, 0.05, 0.6)
    _apply_casualties(att.roster, loss_pct)
    _destroy_army(def)
  else:
    # Defenders win
    var loss_pct = clamp(total_att_str / total_def_str × 0.4, 0.05, 0.6)
    _apply_casualties(def.roster, loss_pct)
    _destroy_army(att)

static func _calculate_strength(roster: Array) -> float:
  var total = 0.0
  for unit in roster:
    var tier_mult = 1.0 + unit.tier × 0.2
    var equip_mult = 1.0 + _calc_equipment_value(unit) / 100.0
    total += unit.hp × tier_mult × equip_mult
  return total
```

---

## Implementation Timeline

### Milestone 1: Core Data Structures (1 week)
- [ ] Create detailed body plan JSON
- [ ] Add equipment layer system
- [ ] Implement blood/bleeding tracking
- [ ] Create materials database

### Milestone 2: Damage Engine (1 week)
- [ ] Implement `GameData.resolve_attack()`
- [ ] Add armor penetration layers
- [ ] Add material matchup system
- [ ] Add hit location selection

### Milestone 3: Turn System (1 week)
- [ ] Replace WEGO with initiative queue
- [ ] Implement `BattleController`
- [ ] Add individual unit AI
- [ ] Add spatial hashing

### Milestone 4: Combat Flow (1 week)
- [ ] Update battle initialization
- [ ] Implement turn execution loop
- [ ] Add player unit control
- [ ] Add victory conditions

### Milestone 5: Polish & Testing (1 week)
- [ ] Detailed combat logging
- [ ] Death condition checks
- [ ] Unit tests for damage resolution
- [ ] Balance testing & tuning

### Milestone 6: UI Updates (3 days)
- [ ] Initiative queue display
- [ ] Body damage paperdoll
- [ ] Layered armor view
- [ ] Combat log formatting

### Milestone 7: Data Migration (2 days)
- [ ] Migrate all weapon JSON
- [ ] Migrate all armor JSON
- [ ] Test with existing saves (or break compatibility)
- [ ] Update documentation

**Total Estimated Time:** ~5-6 weeks

---

## Risk Assessment

### High Risk
1. **Performance:** Tissue-damage system is ~10x more expensive than simple HP
   - **Mitigation:** Spatial hashing, burst ticking, strategic auto-resolution
2. **Balance:** Complex system means many tuning knobs, hard to balance
   - **Mitigation:** Extensive testing, configurable constants, analytics tracking

### Medium Risk
3. **Save compatibility:** Breaking change will frustrate existing players
   - **Mitigation:** Clear communication, version warning, migration guide
4. **UI complexity:** Body damage display can overwhelm players
   - **Mitigation:** Progressive disclosure, tooltips, simplified view by default

### Low Risk
5. **Code complexity:** More LOC = more bugs
   - **Mitigation:** Comprehensive unit tests, modular architecture

---

## Decision Points

### Do we want formations at all?
**Option A:** Pure individual control (like repo)
- Pro: Simpler, more tactical
- Con: Unmanageable with >10 units

**Option B:** Hybrid (formations for movement, individuals for combat)
- Pro: Best of both worlds
- Con: More complex to implement

**Recommendation:** Start with Option A (pure individual), add formations later if needed.

---

### How much realism?
**Option A:** Full Dwarf Fortress (all tissues, nerves, arteries, etc.)
- Pro: Maximum emergent gameplay
- Con: Very complex, hard to tune

**Option B:** Simplified (skin/muscle/bone/organs only)
- Pro: Easier to balance, still interesting
- Con: Less depth

**Recommendation:** Option B. Add tendons/nerves later if desired.

---

### WEGO or Initiative?
**Decision:** Switch to initiative-based (as per repo)
- More intuitive for players (see unit act → react)
- Easier to implement player control
- Matches repo architecture

---

## Appendix: Code Structure Comparison

### Current (WEGO)
```
src/simulation/combat/
  combat_resolver.gd      # Main WEGO resolution
  combatant_state.gd      # Individual soldier data
  formation_state.gd      # Squad-level orders
  combat_ai.gd            # Formation AI (minimal)
```

### Target (Initiative)
```
src/battle/
  BattleController.gd     # Main orchestrator (~1500 lines)
  BattleAI.gd            # Individual unit AI FSM (~700 lines)
  BattleCombat.gd        # Attack/damage resolution (~400 lines)
  BattlePhysics.gd       # Spatial queries (~300 lines)
  BattleTerrain.gd       # Map generation
  BattleSiege.gd         # Siege engines
  BattleState.gd         # Battalion management (optional)

src/core/
  GameData.gd            # Central combat math & data (~2000 lines)

src/data/
  GDUnit.gd              # Individual unit class
  GDBattle.gd            # Battle metadata (strategic)

src/managers/
  CombatManager.gd       # Strategic auto-resolution
```

---

## Next Steps

1. **Review this plan** with team/stakeholders
2. **Prototype damage resolution** in isolation (Milestone 2)
3. **Test performance** with 50 units before full migration
4. **Create backward compatibility plan** for save games
5. **Begin Milestone 1** (data structures)

---

**Status:** Awaiting approval to proceed with implementation.
