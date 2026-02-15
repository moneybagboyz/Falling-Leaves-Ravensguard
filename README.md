# Ravensguard: Grand Strategy & Tactical Combat Engine

> **A high-fidelity simulation where units have organs, settlements have economies, and every action creates cascading consequences across a living world.**

Ravensguard is a deep-simulation engine built in Godot 4.5 that combines:
- **Anatomy-based combat**: Units possess full physiological models (organs, blood, bones) instead of abstract HP pools
- **Circular economy**: Every resource must be physically produced, transported, and consumed
- **Historical simulation**: Medieval land-use models (three-field rotation, acreage-based yields) drive settlement growth
- **Emergent AI**: Factions pursue wars, trade routes, and diplomatic alliances based on real resource needs

**Who this is for:**
- **Players**: Emergent sandbox gameplay where you can be a trader, mercenary, lord, or conqueror
- **Developers**: Contributors extending systems (combat, economy, AI) or modding game content
- **Researchers**: Anyone studying complex simulation architectures or historical economic models

---

## Table of Contents
1. [Quickstart](#quickstart)
2. [Concepts & Mental Model](#concepts--mental-model)
3. [Architecture at a Glance](#architecture-at-a-glance)
4. [End-to-End Data Flow](#end-to-end-data-flow)
5. [Repository Map](#repository-map)
6. [Detailed Pipeline / Execution Model](#detailed-pipeline--execution-model)
7. [Combat System Architecture](#combat-system-architecture)
8. [Economy System Architecture](#economy-system-architecture)
9. [Settlement System Architecture](#settlement-system-architecture)
10. [Configuration](#configuration)
11. [Testing & Quality](#testing--quality)
12. [Debugging & Observability](#debugging--observability)
13. [Extending the System](#extending-the-system)
14. [Roadmap & Known Limitations](#roadmap--known-limitations)
15. [FAQ](#faq)

---

## Quickstart

### Prerequisites
- **Godot Engine**: Version 4.5 or higher ([download here](https://godotengine.org/))
- **Operating System**: Windows 10+ or Linux (macOS untested but likely works)
- **Graphics**: GPU supporting OpenGL 3.3+ (GL Compatibility mode)
- **RAM**: 4GB minimum (8GB recommended for large world generation)

### Installation
```bash
# Clone the repository
git clone <repository-url>
cd Falling-Leaves-Ravensguard

# Open in Godot
# 1. Launch Godot Engine 4.5+
# 2. Click "Import"
# 3. Navigate to the project folder and select project.godot
# 4. Click "Import & Edit"
```

### Run Hello World
```bash
# In Godot Editor:
# Press F5 (or click Play button)
# This runs Main.tscn
```

**Expected Output** (console):
```
=== Falling Leaves - Ravensguard ===
[GameData] Loading materials from data/materials.json...
[GameData] Loaded 42 materials
[GameData] Loaded 18 unit types
[GameState] Initializing world state...
[EntityRegistry] Created 8 factions
[Main] Ready - Main Menu
```

**Minimal Test Scenario:**
```bash
# From Main Menu:
# 1. Select "NEW WORLD GEN"
# 2. Press Enter to accept defaults
# 3. Wait ~30 seconds for world generation
# 4. Select "CHARACTER CREATOR"
# 5. Press Enter to accept default character
# 6. Select "START ADVENTURE"
# 7. Use WASD to move on overworld
# 8. Press T to advance time
```

---

## Concepts & Mental Model

Understanding these core concepts is essential for working with the codebase:

### Core Entities
- **GDUnit**: Individual combatant with full anatomy (organs/blood/bones) defined in `src/data/GDUnit.gd`
- **GDSettlement**: Autonomous settlement node managing population, labor, and production (`src/data/GDSettlement.gd`)
- **GDArmy**: Mobile group of units (`src/data/GDArmy.gd`)
- **GDCaravan**: Trade convoy transporting resources (`src/data/GDCaravan.gd`)
- **GDFaction**: Political entity with territory and treasury (`src/data/GDFaction.gd`)

### Key Concepts

**Acreage System**  
Every world tile = 250 acres (`Globals.ACRES_PER_TILE`). Production is calculated per-acre, not per-tile. A settlement's `arable_acres` determines grain output, `forest_acres` determines wood output, etc.

**Anatomy Map**  
Instead of HP, units have a physiological tree:
```gdscript
body["head"]["brain"] -> {hp: 10, bleeding: 0.0, status: "intact"}
body["torso"]["heart"] -> {hp: 15, bleeding: 2.5, status: "damaged"}
```
Damage propagates through layers: Skin → Muscle → Bone → Organ.

**Circular Economy**  
Every item must be:
1. **Produced**: Labor + Land → Resources (Faucet)
2. **Transported**: Caravans move physical inventory between settlements
3. **Consumed**: Population eats Grain, Burghers consume Ale (Sink)
4. **Priced**: Supply/demand curves update daily based on 14-day rolling averages

**Simulation Pulse**  
The world updates in discrete time steps (turns = hours):
- **Hourly**: Unit movement, combat resolution
- **Daily**: Settlement production, population growth/death, price updates
- **Weekly**: Faction diplomacy checks, war declarations

**Invariants** (must always be true):
1. All entities in `EntityRegistry` must have a valid `faction` field
2. Material hardness values must exist in `data/materials.json` before use
3. Settlement `population` = `laborers + burghers + nobility`
4. No negative inventory (enforced in `GDSettlement.add_inventory()`)

---

## Architecture at a Glance

```
┌─────────────────────────────────────────────────────────────────┐
│                         GAME LOOP (Main.gd)                      │
│  • Handles input via InputRouter                                 │
│  • Switches GameMode states (MENU → OVERWORLD → BATTLE)         │
│  • Delegates rendering to UIRenderer / ShaderGridRenderer        │
└────────────┬────────────────────────────────────────────────────┘
             │
             ├─────→ [ Controllers ] ←─── User Input Events
             │        • OverworldController (movement, AI, time)
             │        • BattleController (tactical combat)
             │        • CityController (settlement UI)
             │        • RegionController (province view)
             │
             ├─────→ [ GameState Facade ] ───┐
             │        (src/core/GameState.gd) │
             │                                 │
             ↓                                 ↓
    ┌────────────────┐              ┌──────────────────┐
    │  WorldState    │              │ EntityRegistry   │
    │  • grid[][],   │              │ • settlements{}  │
    │  • resources,  │              │ • armies[]       │
    │  • geology     │              │ • caravans[]     │
    └────────────────┘              │ • factions[]     │
             │                       └──────────────────┘
             │                                 │
             ├─────────────────────────────────┤
             │                                 │
             ↓                                 ↓
    ┌────────────────────────────────────────────────────┐
    │              MANAGERS (Simulation Layer)            │
    │  • EconomyManager → Production/Consumption/Pricing  │
    │  • SettlementManager → Population/Labor/Growth      │
    │  • FactionManager → Diplomacy/Territory             │
    │  • CombatManager → Strategic battle resolution      │
    │  • WarManager → Campaign orchestration              │
    │  • AIManager → NPC decision-making                  │
    └────────────────────────────────────────────────────┘
             │
             ↓
    ┌────────────────────────────────────────────────────┐
    │         DATA LAYER (Static + Dynamic)              │
    │  Static: data/*.json (materials, AI configs)       │
    │  Dynamic: GDSettlement, GDUnit, GDArmy instances   │
    └────────────────────────────────────────────────────┘
```

**Data Flow Narrative:**
1. **Input** enters via `Main._input()` → routed by `InputRouter` to current controller
2. **Controller** interprets action (e.g., "move army") → calls `GameState` methods
3. **GameState** delegates to specialized modules (`WorldState`, `EntityRegistry`)
4. **Managers** run simulation logic (e.g., `EconomyManager.process_daily_pulse()`)
5. **Entities** update their internal state (`GDSettlement.inventory`, `GDUnit.blood_current`)
6. **Rendering** queries `GameState` and draws via `UIRenderer` or `ShaderGridRenderer`

---

## End-to-End Data Flow (The "Tour")

This section traces a complete gameplay action from input to rendering.

### Example: Player Moves Army on Overworld

**1. Input Detection** (`Main.gd:1100`)
```gdscript
func _input(event):
    input_router.route_input(event, state)
```

**2. Input Routing** (`src/input/InputRouter.gd:70`)
```gdscript
func handle_overworld_mode(event):
    if event.is_action_pressed("ui_right"):
        main_node.player.pos += Vector2i.RIGHT
        GameState.move_player(Vector2i.RIGHT)
```

**3. State Update** (`src/core/GameState.gd:450`)
```gdscript
func move_player(direction: Vector2i):
    player_state.player.pos += direction
    _check_collisions(player_state.player.pos)
    advance_time(1) # Moving costs 1 hour
```

**4. Time Advancement** (`src/state/GameClock.gd:55`)
```gdscript
func advance_time(hours: int):
    turn += hours
    if turn % 24 == 0: # Daily pulse
        emit_signal("daily_pulse")
```

**5. Daily Pulse Handler** (`src/core/GameState.gd:520`)
```gdscript
func _on_daily_pulse():
    for pos in entities.settlements:
        var settlement = entities.settlements[pos]
        EconomyManager.process_daily_pulse(self, settlement)
        SettlementManager.process_growth(settlement)
```

**6. Settlement Production** (`src/managers/EconomyManager.gd:60`)
```gdscript
static func process_daily_pulse(gs, s_data):
    ProductionSystem._process_labor_pool(s_data, efficiency)
    ConsumptionSystem._process_consumption_and_growth(s_data)
```

**7. Labor Pool Processing** (`src/economy/ProductionSystem.gd:120`)
```gdscript
static func _process_labor_pool(s_data, efficiency):
    var grain_per_laborer = (Globals.BUSHELS_PER_ACRE_BASE * 
                             Globals.ACRES_WORKED_PER_LABORER) / 360.0
    var daily_grain = int(s_data.laborers * grain_per_laborer * efficiency)
    s_data.add_inventory("grain", daily_grain)
```

**8. Rendering Update** (`Main.gd:300`)
```gdscript
func _on_map_updated():
    UIRenderer.render_overworld(self, GameState)
```

### Example: Combat Initiation

**1. Collision Detection** (`src/controllers/OverworldController.gd:250`)
```gdscript
func check_entity_collisions(player_pos):
    for army in GameState.entities.armies:
        if army.pos == player_pos and army.faction != "player":
            start_battle(army)
```

**2. Battle Transition** (`Main.gd:800`)
```gdscript
func start_battle(enemy_army):
    state = GameEnums.GameMode.BATTLE
    battle_ctrl.initialize_battle(GameState.player_state.player, enemy_army)
```

**3. Battle Initialization** (`src/controllers/BattleController.gd:200`)
```gdscript
func initialize_battle(player, enemy):
    physics.initialize_grid()
    terrain.generate_terrain(last_map_pos, physics)
    _spawn_units(player.army, "player")
    _spawn_units(enemy, "enemy")
```

**4. Combat Loop** (`BattleController.gd:400` - runs every TICK_RATE seconds)
```gdscript
func _process(delta):
    simulation_time += delta
    if simulation_time >= TICK_RATE:
        ai.update_all_units(units, physics)
        physics.update_projectiles(projectiles, combat)
        combat.process_damage(units)
        simulation_time = 0.0
```

**5. Damage Resolution** (`src/battle/BattleCombat.gd:600`)
```gdscript
static func resolve_hit(attacker, defender, attack_data):
    var penetration = calculate_penetration(attack_data)
    var body_part = select_hit_location(defender)
    apply_damage_to_tissue(defender.body[body_part], penetration)
    calculate_bleeding(defender)
```

**6. Physiology Update** (`BattleCombat.gd:850`)
```gdscript
static func update_vitality(unit):
    unit.blood_current -= unit.bleed_rate * 0.1 # Per tick
    if unit.blood_current <= 0:
        unit.status.is_dead = true
    elif unit.blood_current < unit.blood_max * 0.3:
        unit.status.is_downed = true
```

---

## Repository Map

### Top-Level Structure
```
Falling-Leaves-Ravensguard/
├── src/                    # All game logic
├── data/                   # Static JSON configurations
├── docs/                   # Technical documentation
├── shaders/                # Custom GLSL shaders
├── .godot/                 # Godot engine cache (ignored)
├── Main.gd                 # Game loop orchestrator (2451 lines)
├── Main.tscn               # Root scene
├── project.godot           # Godot project config
└── README.md               # This file
```

### src/ Breakdown

| Folder | Purpose | Key Files | What NOT to put here |
|--------|---------|-----------|---------------------|
| **src/core/** | Engine foundation<br>Facades, singletons, enums | `GameState.gd` (main facade)<br>`GameData.gd` (static data loader)<br>`Globals.gd` (constants)<br>`GameEnums.gd` (all enums)<br>`StateManager.gd` (mode transitions) | Entity-specific logic<br>UI code<br>Combat math |
| **src/state/** | State modules<br>Delegated from GameState | `WorldState.gd` (terrain grid)<br>`EntityRegistry.gd` (all entities)<br>`PlayerState.gd` (player data)<br>`GameClock.gd` (time/turns) | Static data loading<br>Input handling |
| **src/managers/** | Global simulators<br>Stateless orchestration | `EconomyManager.gd`<br>`SettlementManager.gd`<br>`FactionManager.gd`<br>`CombatManager.gd`<br>`AIManager.gd`<br>`WarManager.gd` | UI rendering<br>Entity definitions<br>Controller logic |
| **src/data/** | Entity class definitions<br>Data structures | `GDUnit.gd` (combatant)<br>`GDSettlement.gd` (city/town)<br>`GDArmy.gd` (unit group)<br>`GDCaravan.gd` (trader)<br>`GDFaction.gd` (nation) | Update loops<br>Simulation logic<br>Static data |
| **src/controllers/** | Scene orchestration<br>Input delegation | `OverworldController.gd`<br>`BattleController.gd`<br>`CityController.gd`<br>`DungeonController.gd`<br>`RegionController.gd` | Game logic formulas<br>Data definitions |
| **src/battle/** | Tactical combat subsystem | `BattlePhysics.gd` (collisions, movement)<br>`BattleCombat.gd` (damage resolution)<br>`BattleAI.gd` (unit behaviors)<br>`BattleTerrain.gd` (tactical map gen)<br>`BattleSiege.gd` (siege engines)<br>`BattleState.gd` (combat state) | Overworld logic<br>Economy code<br>UI rendering |
| **src/economy/** | Economic subsystems<br>Delegated from EconomyManager | `ProductionSystem.gd`<br>`ConsumptionSystem.gd`<br>`PricingSystem.gd`<br>`TradeSystem.gd`<br>`EquipmentSystem.gd` | Settlement UI<br>Combat logic<br>World generation |
| **src/ui/** | Rendering functions<br>Pure display logic | `UIRenderer.gd` (main renderer)<br>`UIBattle.gd`<br>`UIOverworld.gd`<br>`UIManagement.gd`<br>`UIMainMenu.gd` | Game logic<br>State management<br>Input handling |
| **src/input/** | Input routing | `InputRouter.gd` | UI rendering<br>Game logic |
| **src/rendering/** | GPU-based rendering | `ShaderGridRenderer.gd` | Game logic<br>UI code |
| **src/utils/** | Helper functions | `UIPanels.gd`<br>`WorldAudit.gd` | Core game logic |

### data/ Folder

| File | Purpose | Schema Example |
|------|---------|----------------|
| **materials.json** | Physical properties of materials | `{"steel": {"hardness": 90, "impact_yield": 500}}` |
| **ai_config.json** | AI behavior parameters | `{"aggressive": {"war_threshold": 0.3}}` |
| **names.json** | Name generation tables | `{"male_names": ["Aldric", "Bertram"]}` |
| **fauna_table.json** | Wildlife definitions | `{"wolf": {"hp": 50, "damage": 15}}` |
| **flora_table.json** | Plant definitions | `{"oak": {"wood_yield": 500}}` |

**What NOT to put in data/:**
- Saved game files (those go in user:// directory via Godot)
- Generated world maps (stored in GameState at runtime)
- Player inventories (stored in GDPlayer instances)

### docs/ Folder

| File | Purpose |
|------|---------|
| **TESTING_GUIDE.md** | How to run automated tests |
| **REFACTORING_SUMMARY.md** | Architecture migration notes |
| **Architecture_Refactoring_Phase3.md** | Detailed refactoring plan |
| **BattleController_Analysis.md** | Combat system deep-dive |

### Root-Level Documentation

| File | Purpose |
|------|---------|
| **COMBAT_EXPLANATION.md** | Technical spec for combat |
| **ECONOMY_EXPLANATION.md** | Technical spec for economy |
| **SETTLEMENT_EXPLANATION.md** | Technical spec for settlements |
| **PROVINCE_SYSTEM.md** | Province/region system |
| **QUICKSTART.md** | Fast onboarding guide |
| **SHADER_RENDERER.md** | GPU rendering tech notes |

---

## Detailed Pipeline / Execution Model

### Initialization Sequence

When you press F5 (Run Main Scene), this happens:

**1. Godot Autoloads** (`project.godot:18`)
```ini
[autoload]
GameData="*res://src/core/GameData.gd"
```
- `GameData._ready()` loads all JSON files from `data/`

**2. Main Scene Init** (`Main.gd:_ready():100`)
```gdscript
func _ready():
    GameState.connect("log_updated", _on_log_updated)
    GameState.connect("map_updated", _on_map_updated)
    input_router = InputRouter.new(self)
    overworld_ctrl = $OverworldController
    battle_ctrl = $BattleController
    state = GameEnums.GameMode.MENU
```

**3. GameState Initialization** (`src/core/GameState.gd:_init():75`)
```gdscript
func _init():
    world = WorldState.new()
    entities = EntityRegistry.new()
    player_state = PlayerState.new()
    clock = GameClock.new()
    state_manager = StateManager.new()
```

**4. Entity Registry Setup** (`src/state/EntityRegistry.gd:_init():40`)
```gdscript
func _initialize_factions():
    var faction_data = {
        "player": ["Player's Band", 1000, "yellow"],
        "royalists": ["The Royalist League", 5000, "blue"],
        # ... 8 factions total
    }
    for f_id in faction_data:
        factions.append(GDFaction.new(f_id, ...))
```

### Game Mode Lifecycle

The game operates in discrete **modes** managed by `StateManager`:

```
MENU
  ↓ (Select "NEW WORLD GEN")
WORLD_CREATION
  ↓ (World generates)
WORLD_PREVIEW
  ↓ (Select start location)
CHARACTER_CREATION
  ↓ (Create character)
PLAY_SELECT
  ↓ (Choose starting settlement)
LOADING
  ↓
OVERWORLD ⇄ BATTLE/DUNGEON/CITY/REGION
       ↓
     MENU (ESC)
```

**Valid Transitions** (`src/core/StateManager.gd:25`):
```gdscript
const VALID_TRANSITIONS = {
    GameEnums.GameMode.MENU: [WORLD_CREATION, CHARACTER_CREATION, ...],
    GameEnums.GameMode.OVERWORLD: [BATTLE, CITY, DUNGEON, ...],
    # ...
}
```

### Simulation Pipeline Stages

| Stage | Frequency | Trigger | Entry Point | Outputs |
|-------|-----------|---------|-------------|---------|
| **Input Polling** | Every frame | `Main._input()` | `InputRouter.route_input()` | Player commands queued |
| **Physics Update** | Every `TICK_RATE` (0.1s) | Timer in BattleController | `BattlePhysics.update()` | Unit positions, projectile trajectories |
| **Hourly Pulse** | Every turn (1 hour) | `GameClock.advance_time()` | Controllers check collisions | Entity movements processed |
| **Daily Pulse** | Every 24 turns | `GameClock` signal | `GameState._on_daily_pulse()` | Production, consumption, growth |
| **Weekly Pulse** | Every 168 turns | `GameClock` signal | `FactionManager.weekly_diplomacy()` | War declarations, peace treaties |
| **Rendering** | Every frame | Godot _process() | `UIRenderer.render_*()` | Screen updates |

### Daily Pulse Deep Dive

This is the core economic/population simulation loop.

**Trigger:** `GameClock.advance_time()` detects `turn % 24 == 0`

**Entry:** `src/core/GameState.gd:_on_daily_pulse():520`

**Execution Flow:**
```
1. For each settlement:
   ├─→ EconomyManager.process_daily_pulse()
   │    ├─→ ProductionSystem._process_labor_pool()
   │    │    ├─→ Calculate grain from laborers × acres × efficiency
   │    │    ├─→ Calculate wood from forest_acres × laborers
   │    │    └─→ Add to settlement.inventory{}
   │    ├─→ ProductionSystem._process_energy()
   │    │    └─→ Charcoal burners convert wood → charcoal
   │    ├─→ ConsumptionSystem._process_consumption_and_growth()
   │    │    ├─→ Population consumes grain (starvation if < 0)
   │    │    ├─→ Burghers consume ale
   │    │    ├─→ Calculate population growth
   │    │    └─→ Check migration triggers
   │    ├─→ ConsumptionSystem._process_taxes()
   │    │    └─→ settlement.crown_stock += daily_tax
   │    └─→ ConsumptionSystem._process_storage_limits()
   │         └─→ Spoilage for inventory > capacity
   │
   ├─→ SettlementManager.process_growth()
   │    ├─→ Update social class ratios (laborers/burghers/nobility)
   │    ├─→ Process construction queue
   │    └─→ Update organic industries
   │
   └─→ PricingSystem invalidates price caches (recalc next query)

2. For each caravan:
   └─→ TradeSystem.resolve_caravan_trade()

3. For each faction:
   └─→ AIManager.process_faction_ai()
        ├─→ Decide on building investments
        ├─→ Decide on army movements
        └─→ Decide on trade policies
```

**Failure Modes:**
- **Starvation**: `grain_stock < population × daily_consumption` → population decreases
- **Bankruptcy**: `crown_stock < 0` → buildings halt construction
- **Rebellion**: `stability < 30` → labor pool stops contributing

---

## Combat System Architecture

*(See COMBAT_EXPLANATION.md for full technical spec)*

### Core Principle
Instead of `hp -= damage`, combat uses **layered tissue damage** and **physiological failure**.

### System Components

| Module | File | Responsibility |
|--------|------|----------------|
| **Physics** | `BattlePhysics.gd` | Spatial grid, collisions, movement, line-of-sight |
| **Combat** | `BattleCombat.gd` | Damage calculation, armor penetration, injury resolution |
| **AI** | `BattleAI.gd` | Unit decision-making (FSM: IDLE → SEEK → ENGAGE → FLEE) |
| **Terrain** | `BattleTerrain.gd` | Procedural battlefield generation from overworld tile |
| **Siege** | `BattleSiege.gd` | Siege engine physics and structure damage |
| **State** | `BattleState.gd` | Mock battle data for testing |

### Damage Resolution Flow

```
1. Attack declared (e.g., "Swing Longsword")
   ↓
2. BattlePhysics.check_hit(attacker_pos, defender_pos)
   → Returns true if in range & line-of-sight clear
   ↓
3. BattleCombat.select_hit_location(defender)
   → Weighted random: head (10%), torso (40%), limbs (50%)
   ↓
4. BattleCombat.resolve_hit(attacker, defender, attack_data)
   ├─→ Calculate energy: E = (base_damage + momentum × 2.0) × strength_mult
   ├─→ Apply material matchup: steel vs iron = 1.2× multiplier
   ├─→ Penetrate armor layers:
   │    ├─→ Cover layer (cloak) → absorbs impact_yield × contact_area
   │    ├─→ Armor layer (plate) → absorbs impact_yield × contact_area
   │    ├─→ Over layer (gambeson) → absorbs shear_yield / penetration
   │    └─→ Under layer (shirt) → minimal absorption
   └─→ Remaining energy hits tissue:
        ├─→ Damage skin (hp -= energy)
        ├─→ If penetration > skin_thickness:
        │    └─→ Damage muscle
        ├─→ If penetration > muscle_thickness:
        │    └─→ Damage bone (fracture if hp <= 0)
        └─→ If penetration > bone_thickness:
             └─→ Damage organs (heart/brain death = immediate)
   ↓
5. BattleCombat.calculate_bleeding(unit, damaged_part)
   → bleed_rate += damage × vessel_type_mult (arteries bleed 10× faster)
   ↓
6. BattleCombat.update_vitality(unit)
   → unit.blood_current -= bleed_rate × tick_delta
   → if blood_current < blood_max × 0.3: unit.status.is_downed = true
   → if blood_current <= 0: unit.status.is_dead = true
```

### AI State Machine

Units follow a Finite State Machine (`BattleAI.gd:150`):

```
IDLE
 ↓ (enemy within aggro_radius)
SEEK
 ↓ (enemy within weapon_range)
ENGAGE
 ├─→ (stamina < 20%) → MANEUVER
 ├─→ (health < 40%) → RETREAT
 └─→ (enemy dies) → SEEK (find new target)

RETREAT
 ↓ (at safe distance OR reinforcements arrive)
REGROUP
 ↓ (stamina restored)
SEEK
```

**Transitions** (`BattleAI.gd:update_unit_ai():200`):
```gdscript
if unit.status.is_downed or unit.status.is_dead:
    return # No AI for incapacitated units

if current_state == "IDLE":
    var nearest_enemy = find_nearest_enemy(unit)
    if distance_to(nearest_enemy) < AGGRO_RADIUS:
        unit.ai_state = "SEEK"
        unit.ai_target = nearest_enemy

elif current_state == "SEEK":
    if distance_to(unit.ai_target) < weapon_range:
        unit.ai_state = "ENGAGE"
    else:
        move_toward(unit.ai_target)

elif current_state == "ENGAGE":
    if unit.fatigue > 80 or unit.blood_current < unit.blood_max * 0.4:
        unit.ai_state = "RETREAT"
    else:
        execute_attack(unit, unit.ai_target)
```

### Testing Combat in Isolation

You can test combat without running the full game:

```gdscript
# In Godot script editor
# Create a new scene: BattleTest.tscn

var battle_ctrl = BattleController.new()
battle_ctrl.initialize_grid()

# Spawn test units
var player_unit = GDUnit.new()
player_unit.pos = Vector2i(50, 50)
player_unit.team = "player"

var enemy_unit = GDUnit.new()
enemy_unit.pos = Vector2i(55, 50)
enemy_unit.team = "enemy"

battle_ctrl.units = [player_unit, enemy_unit]

# Run simulation
for i in range(100): # 100 ticks
    battle_ctrl.ai.update_all_units(battle_ctrl.units, battle_ctrl.physics)
    battle_ctrl.combat.process_damage(battle_ctrl.units)
```

---

## Economy System Architecture

*(See ECONOMY_EXPLANATION.md for full technical spec)*

### Core Principle
A **circular flow** where every item must be produced, transported, and consumed. No resources spawn from nothing.

### Faucets (Resource Generation)

| Source | Module | Formula |
|--------|--------|---------|
| **Agricultural Labor** | `ProductionSystem._process_labor_pool()` | `laborers × acres_per_laborer × bushels_per_acre × efficiency / 360` |
| **Forest Labor** | `ProductionSystem._process_labor_pool()` | `laborers × forestry_yield × efficiency / 360` |
| **Mining Labor** | `ProductionSystem._process_labor_pool()` | `laborers × ore_deposits × mine_yield × efficiency / 360` |
| **Organic Industry** | `ProductionSystem._process_organic_industries()` | `burghers × workshop_output × building_multiplier` |
| **Faction Minting** | `FactionManager.process_economy()` | Factions generate crowns to pay armies |

### Sinks (Resource Deletion)

| Sink | Module | Formula |
|------|--------|---------|
| **Population Consumption** | `ConsumptionSystem._process_consumption()` | `population × daily_bushels_per_person` |
| **Burgher Consumption** | `ConsumptionSystem._process_consumption()` | `burghers × 0.1 ale/day` |
| **Seed Reservation** | `ProductionSystem` | `grain_harvest × 0.20` (deleted, not added to inventory) |
| **Maintenance Costs** | `ConsumptionSystem._process_taxes()` | `-1 crown per building level per week` |
| **Spoilage** | `ConsumptionSystem._process_storage_limits()` | `inventory[res] -= (inventory[res] - capacity) × 0.1` |

### Price Discovery Algorithm

Prices update daily (`PricingSystem.get_price():50`):

```gdscript
func get_price(res_name, settlement):
    var demand = settlement.population × daily_consumption × 14 # 14-day buffer
    var supply = settlement.inventory.get(res_name, 0)
    var base_price = GameData.BASE_PRICES[res_name]
    
    var scarcity = demand / max(supply, 1.0)
    var multiplier = clamp(scarcity, 0.2, 5.0) # Floor 20%, ceiling 500%
    
    return int(base_price × multiplier)
```

**Damping Factors:**
- Minimum multiplier: `0.2` (prevents free resources)
- Maximum multiplier: `5.0` (prevents hyperinflation)
- Rolling average: Prices use 14-day demand anchors to smooth volatility

### Trade System

**Caravan Routing** (`TradeSystem.resolve_caravan_trade():200`):
```
1. Caravan spawns at surplus settlement
2. Pathfinding to highest-price destination
   → A* grid search via WorldState.astar
3. Travel time = distance × terrain_penalty
4. On arrival:
   ├─→ Sell goods at destination price
   ├─→ Buy local surplus
   └─→ Return to origin or continue to new destination
5. Risks:
   ├─→ Bandit ambush (chance based on route wilderness %)
   └─→ Faction war intercepts (auto-captured if enemy territory)
```

### Testing Economy in Isolation

```gdscript
# Create a test settlement
var settlement = GDSettlement.new()
settlement.population = 1000
settlement.laborers = 900
settlement.burghers = 80
settlement.nobility = 20
settlement.arable_acres = 5000
settlement.inventory = {"grain": 10000, "wood": 2000}

# Run daily pulse
for day in range(30): # Simulate 1 month
    EconomyManager.process_daily_pulse(GameState, settlement)
    print("Day %d: Grain=%d, Pop=%d" % [day, settlement.inventory.grain, settlement.population])

# Expected: Grain decreases by ~42 per day (1000 pop × 0.042 bushels/day)
#           New grain added: ~150 per day (900 laborers × 10 acres × 12 bushels / 360 days)
#           Net: +108 grain/day
```

---

## Settlement System Architecture

*(See SETTLEMENT_EXPLANATION.md for full technical spec)*

### Core Principle
Settlements are **autonomous agent-nodes** that manage land, labor, and buildings.

### Hierarchical Structure

```
GDSettlement (Data Layer)
  ├─→ population: int
  ├─→ inventory: Dictionary
  ├─→ buildings: Dictionary
  ├─→ arable_acres: int
  ├─→ laborers: int
  └─→ ... (240 lines of properties)
        ↓
SettlementManager (Logic Layer)
  ├─→ process_growth(settlement)
  ├─→ process_construction(settlement)
  └─→ update_organic_industries(settlement)
        ↓
CityController (Visual Layer)
  └─→ render_settlement_ui(settlement)
```

### Procedural Land Allocation

When a settlement is created (`SettlementManager.create_settlement():50`):

```
1. Scan radius around settlement pos (default: 3 tiles)
2. For each tile in radius:
   ├─→ Check biome (WorldState.get_biome(pos))
   ├─→ If "grassland" or "plains":
   │    └─→ arable_acres += 250
   ├─→ If "forest":
   │    └─→ forest_acres += 250
   ├─→ If "mountain":
   │    ├─→ mining_slots += count_ore_deposits(pos)
   │    └─→ Check geology for ore types
   └─→ If "ocean" or "river":
        └─→ fishing_slots += 1
3. Apply three-field rotation:
   └─→ fallow_acres = arable_acres / 3
```

### Event-Driven Growth

**Migration Trigger** (`SettlementManager.process_growth():150`):
```gdscript
if settlement.happiness > 80 and settlement.population < settlement.get_housing_capacity():
    var migration_chance = Globals.MIGRATION_CHANCE # 0.05 (5%)
    if randf() < migration_chance:
        settlement.migration_buffer += 1
        # Migrants arrive after 7-day journey
```

**Starvation Trigger** (`ConsumptionSystem._process_consumption():100`):
```gdscript
if settlement.inventory.grain <= 0:
    var death_count = int(settlement.population × Globals.STARVATION_DEATH_RATE) # 2%
    settlement.population -= death_count
    settlement.stability -= 10
    settlement.unrest += 10
    settlement.happiness -= 20
```

**Rebellion Trigger** (`SettlementManager.check_rebellion():300`):
```gdscript
if settlement.stability < 30:
    settlement.status = "REBELLION"
    # Halts all production/construction
    # Triggers faction intervention event
```

### Construction System

Buildings scale exponentially (`Globals.gd` + `BuildingData`):

```
Cost = Base × (2.5 ^ Current_Level)

Example: Granary
  Level 1: 100 crowns, 50 labor-days
  Level 2: 250 crowns, 125 labor-days
  Level 3: 625 crowns, 312 labor-days
```

**Construction Queue** (`SettlementManager.process_construction():200`):
```
1. For each building in queue:
   ├─→ Check if funds available (settlement.crown_stock >= cost)
   ├─→ Deduct crowns immediately
   ├─→ Assign available laborers to project
   │    └─→ labor_per_day = min(available_laborers × 0.1, remaining_labor)
   └─→ When labor_remaining <= 0:
        ├─→ Increment building level
        ├─→ Remove from queue
        └─→ Apply building effects (e.g., +storage capacity)
```

---

## Configuration

### Static Data Files (data/)

**materials.json** - Physical properties for combat/crafting
```json
{
  "steel": {
    "hardness": 90,
    "density": 7.85,
    "impact_yield": 500,
    "shear_yield": 400,
    "elasticity": 0.2
  }
}
```
- **hardness**: Resistance to deformation (0-100)
- **impact_yield**: Force required to break under blunt impact
- **shear_yield**: Force required to cut/pierce
- **elasticity**: Bounce-back factor (0.0-1.0)

**ai_config.json** - Faction AI personalities
```json
{
  "aggressive": {
    "war_threshold": 0.3,
    "expansion_priority": 0.8,
    "trade_priority": 0.2
  }
}
```

**names.json** - Procedural name generation
```json
{
  "male_names": ["Aldric", "Bertram", "Cedric"],
  "female_names": ["Aelwen", "Brynn", "Cerys"],
  "settlement_prefixes": ["North", "South", "Fort"],
  "settlement_suffixes": ["holm", "wick", "ton"]
}
```

### Constants (src/core/Globals.gd)

**Economic Tuning:**
```gdscript
const BUSHELS_PER_ACRE_BASE = 12.0    # Medieval yield
const SEED_RATIO_INV = 0.20           # 20% seed reservation
const GROWTH_RATE = 0.0001            # 3.6% annual growth
const STARVATION_DEATH_RATE = 0.02    # 2% daily when starving
```

**Combat Tuning:**
```gdscript
# See BattlePhysics.gd
const AGGRO_RADIUS = 15.0
const WEAPON_RANGE_MELEE = 2.0
const ARROW_SPEED = 5.0
```

**Changing Config at Runtime:**
```gdscript
# In Godot console (F12 to access):
Globals.GROWTH_RATE = 0.0002  # Double population growth
GameData.MATERIALS["steel"]["hardness"] = 100  # Buff steel
```

### Project Settings (project.godot)

```ini
[application]
config/name="Falling Leaves"
run/main_scene="res://Main.tscn"
config/features=PackedStringArray("4.5", "GL Compatibility")

[autoload]
GameData="*res://src/core/GameData.gd"

[display]
window/size/viewport_width=1920
window/size/viewport_height=1080
window/stretch/mode="viewport"
```

---

## Testing & Quality

### Automated Tests

**Location:** Tests are embedded in `test_new_architecture.tscn` scene.

**Running Tests:**
```bash
# In Godot Editor:
# 1. Press F6 (Run Current Scene)
# 2. Select: res://test_new_architecture.tscn
# 3. Check console output
```

**Test Coverage:**
- ✅ Module instantiation (WorldState, EntityRegistry, etc.)
- ✅ Property forwarding (GameState delegates to modules)
- ✅ State transitions (valid/invalid mode changes)
- ✅ Core methods (time advancement, faction lookup)

**Example Test** (`test_new_architecture.gd`):
```gdscript
func test_combat_damage():
    var attacker = GDUnit.new()
    attacker.attributes.strength = 15
    
    var defender = GDUnit.new()
    defender.body = BattleCombat.create_human_anatomy()
    
    var attack_data = {
        "damage": 50,
        "penetration": 20,
        "contact_area": 5,
        "type": "CUT"
    }
    
    BattleCombat.resolve_hit(attacker, defender, attack_data)
    
    assert(defender.blood_current < defender.blood_max, "Defender should bleed")
    assert(defender.bleed_rate > 0.0, "Bleed rate should increase")
```

### Manual Testing Checklist

**Critical Paths** (test before release):

✅ **World Generation**
  - Create world with various seeds
  - Verify settlements spawn
  - Check terrain variety (not all plains)

✅ **Character Creation**
  - Create character with max/min stats
  - Test equipment loadout selection
  - Verify starting resources

✅ **Overworld Movement**
  - Move in all 8 directions
  - Collision detection (can't walk through mountains)
  - Army following player

✅ **Combat**
  - Initiate battle by colliding with enemy
  - Test damage (units should bleed/die)
  - Test AI (enemies should attack)
  - Test siege equipment

✅ **Settlement**
  - Enter city (E key)
  - Trade resources
  - Recruit units
  - Commission buildings

✅ **Economy**
  - Advance time 30 days
  - Verify grain production > 0
  - Check price changes
  - Test caravan spawning

### What's NOT Tested (Known Gaps)

- [ ] Save/load system (manual testing only)
- [ ] Dungeon generation (WIP)
- [ ] Multiplayer (not implemented)
- [ ] Mod loading (not implemented)
- [ ] Performance under 10k+ units

---

## Debugging & Observability

### Logging System

**Log Prefixes** (grep for these in console):
```
[GameData]       - Static data loading
[GameState]      - World state changes
[Economy]        - Production/prices
[Battle]         - Combat events
[Settlement]     - Population/growth
[Faction]        - Diplomacy
[AI]             - NPC decisions
[ERROR]          - Critical failures
```

**Enable Verbose Logging:**
```gdscript
# Add to Main.gd:_ready()
GameState.debug_mode = true  # Enables detailed pulse logging
```

### Common Issues Playbook

**Problem: Population not growing**
1. Check grain stock: `print(settlement.inventory.grain)`
2. Check happiness: `print(settlement.happiness)` (must be > 40)
3. Check housing: `print(settlement.get_housing_capacity())` (must exceed population)
4. **Root Cause**: Usually starvation (grain = 0) or unrest (happiness < 40)

**Problem: Combat crashes**
1. Check anatomy initialization: `print(unit.body.keys())` (should have "head", "torso", etc.)
2. Check equipment data: `print(unit.equipment)`
3. **Root Cause**: Usually null equipment or missing body parts
4. **Fix**: Run `BattleCombat.create_human_anatomy()` for new units

**Problem: Prices are zero**
1. Check base prices loaded: `print(GameData.BASE_PRICES)`
2. Check settlement inventory: `print(settlement.inventory)`
3. **Root Cause**: Usually missing base price in `data/materials.json`

**Problem: Army won't move**
1. Check pathfinding: `print(GameState.world.astar.is_in_bounds(target_pos))`
2. Check terrain: `print(GameState.world.get_tile(army.pos))`
3. **Root Cause**: Usually trying to path through impassable terrain (mountains, ocean)

**Problem: Buildings won't construct**
1. Check funds: `print(settlement.crown_stock)` vs `building_cost`
2. Check queue: `print(settlement.construction_queue)`
3. **Root Cause**: Usually insufficient crowns or laborers
4. **Debug Command**: `settlement.crown_stock = 10000` to bypass

### Reproduction Steps Template

When reporting bugs,include:
```
1. Game Mode: [OVERWORLD / BATTLE / CITY]
2. Save File: [Attach if possible]
3. Steps:
   - Press T to advance 1 day
   - Enter settlement at (50, 50)
   - Click "Trade"
4. Expected: Can buy grain
5. Actual: Error: "No grain available" despite inventory showing 500
6. Console Log: [Paste last 20 lines]
```

---

## Extending the System

### Add a New Resource

**1. Define in `data/materials.json`:**
```json
{
  "jade": {
    "hardness": 70,
    "density": 3.3,
    "impact_yield": 300,
    "shear_yield": 250,
    "elasticity": 0.15
  }
}
```

**2. Add to pricing (`GameData.gd` static DATA):**
```gdscript
# In src/data/static/ItemData.gd
static var BASE_PRICES = {
    "jade": 500,  # Base price in crowns
    # ...
}
```

**3. Add production source (`ProductionSystem.gd`):**
```gdscript
# In _process_labor_pool()
if settlement.has_jade_quarry():
    var jade_daily = laborers_assigned × JADE_YIELD / 360.0
    settlement.add_inventory("jade", int(jade_daily))
```

**4. Add consumption sink (optional):**
```gdscript
# In ConsumptionSystem.gd
if settlement.nobility > 0:
    var jade_demand = settlement.nobility × 0.001 # Luxury consumption
    settlement.remove_inventory("jade", jade_demand)
```

### Add a New Building Type

**1. Define in `src/data/static/BuildingData.gd`:**
```gdscript
static func get_buildings() -> Dictionary:
    return {
        "jade_workshop": {
            "name": "Jade Workshop",
            "base_cost": 500,
            "base_labor": 100,
            "effects": {"jade_output_mult": 1.5},
            "max_level": 5
        }
    }
```

**2. Add construction logic (`SettlementManager.gd`):**
```gdscript
# Construction happens automatically via queue system
# Player/AI adds to queue:
settlement.construction_queue.append("jade_workshop")
```

**3. Apply effects (`ProductionSystem.gd`):**
```gdscript
var jade_mult = 1.0
if settlement.buildings.has("jade_workshop"):
    jade_mult += settlement.buildings["jade_workshop"] × 0.5
jade_output = int(base_jade × jade_mult)
```

### Add a New Unit Type

**1. Define in `src/data/static/UnitData.gd`:**
```gdscript
static var UNIT_TYPES = {
    "heavy_cavalry": {
        "tier": 4,
        "cost": 1000,
        "hp": 150,
        "speed": 1.2,
        "equipment": ["lance", "full_plate", "warhorse"]
    }
}
```

**2. Add recruitment logic (`SettlementManager.gd`):**
```gdscript
func refresh_recruit_pool(settlement):
    if settlement.has_stable() and settlement.buildings.stable >= 2:
        settlement.recruit_pool.append({
            "type": "heavy_cavalry",
            "cost": 1000
        })
```

**3. Add combat stats (`BattleCombat.gd`):**
```gdscript
# Stats are pulled from equipment automatically
# Just ensure unit has proper equipment defined
```

### Add a New AI Personality

**1. Define in `data/ai_config.json`:**
```json
{
  "merchant_prince": {
    "war_threshold": 0.8,
    "expansion_priority": 0.3,
    "trade_priority": 0.9,
    "building_preference": ["market", "road", "warehouse"]
  }
}
```

**2. Implement decision tree (`src/managers/AIManager.gd`):**
```gdscript
func decide_building(faction, settlement, ai_profile):
    if ai_profile == "merchant_prince":
        # Prioritize trade infrastructure
        if not settlement.buildings.has("market"):
            return "market"
        elif settlement.buildings.market < 3:
            return "market" # Upgrade to level 3
        else:
            return "warehouse"
```

### Safe Extension Patterns

**DO:**
- ✅ Add new entries to existing JSON files
- ✅ Create new Manager classes that follow delegation pattern
- ✅ Extend enums in `GameEnums.gd`
- ✅ Add new subsystems to `src/economy/` or `src/battle/`

**DON'T:**
- ❌ Modify core `GameState.gd` facade (add to modules instead)
- ❌ Add circular dependencies between Managers
- ❌ Hardcode values in Controllers (use Globals.gd or JSON)
- ❌ Store state in static functions (Managers should be stateless)

---

## Roadmap & Known Limitations

### Current Limitations

**Performance:**
- **Unit Count**: Combat slows noticeably at >500 units per battle (anatomy calculations are expensive)
- **World Size**: 300×300 recommended max (pathfinding A* becomes slow beyond this)
- **Settlement Count**: 100+ settlements cause daily pulse lag (50ms+ per pulse)

**Gameplay:**
- **No Save/Load**: Persistence not implemented (runs are session-only)
- **No Multiplayer**: Single-player only
- **Limited Diplomacy**: Alliances don't have nuanced mechanics (just binary friend/enemy)
- **No Mod Support**: No official modding API

**Technical Debt:**
- **Main.gd Monolith**: 2451 lines (refactoring in progress, see `docs/REFACTORING_SUMMARY.md`)
- **Mixed Rendering**: Uses both TileMap and Shader renderers (inconsistent)
- **No Unit Tests**: Only integration tests exist

### Planned Improvements

**Short-Term (Next 3 Months):**
- [ ] Implement save/load system (`GameState.serialize()`)
- [ ] Refactor `Main.gd` into smaller controllers
- [ ] Add unit tests for combat formulas
- [ ] Optimize battle physics (spatial hashing improvements)

**Medium-Term (6-12 Months):**
- [ ] Multi-threading for daily pulse (settlements update in parallel)
- [ ] GPU-based combat calculations (compute shaders for damage)
- [ ] Modding API (Lua scripting for events)
- [ ] Province-level strategic AI (regions instead of individual settlements)

**Long-Term (1-2 Years):**
- [ ] Network multiplayer (deterministic lockstep)
- [ ] Advanced diplomacy (treaties, vassalage, marriage alliances)
- [ ] Dynamic quest generation (procedural story arcs)
- [ ] 3D tactical view (optional, keeping 2D as default)

**Unlikely/Out-of-Scope:**
- Voice acting (too expensive)
- Real-time strategy mode (turn-based is core to design)
- Mobile port (UI not designed for touch)

---

## FAQ

**Q: How do I modify blood volume levels?**  
A: Edit `src/data/GDUnit.gd:45`:
```gdscript
var blood_max: float = 5000.0  # Change this value
var blood_current: float = 5000.0
```

**Q: Why use GDScript instead of C++?**  
A: Rapid iteration on economic formulas. Combat could benefit from C++ GDExtension in the future.

**Q: Where is the save game logic?**  
A: Not implemented yet. Planned for next milestone (see Roadmap).

**Q: Can I add new armor layers?**  
A: Yes. Edit `src/core/GameEnums.gd` to add enum, then update `src/battle/BattleCombat.gd:resolve_armor_layers()`.

**Q: How is terrain generated?**  
A: Overworld uses Perlin noise (`WorldGen.gd`). Battle maps use custom GLSL shader (`shaders/terrain_grid.gdshader`).

**Q: How do I run a headless simulation?**  
A: Create a test script that initializes `GameState` without running `Main.tscn`:
```gdscript
extends Node
func _ready():
    var gs = GameState.new()
    gs.initialize()
    for i in range(365): # Simulate 1 year
        gs.advance_time(24)
    print(gs.entities.settlements[Vector2i(50,50)].population)
```

**Q: What is the 'Three-Field System'?**  
A: Historical crop rotation. Only 66% of arable land is planted per year; 33% is fallow (pasture). See `Globals.gd` and `SettlementManager.gd`.

**Q: How do I debug price spikes?**  
A: Check `src/economy/PricingSystem.gd:get_price()`. Enable debug logging:
```gdscript
# In Main.gd:_ready()
PricingSystem.debug = true  # Prints price calculations to console
```

**Q: What renderer should I use?**  
A: `ShaderGridRenderer` (GPU-based) is faster for large maps. TileMap renderer is legacy.

**Q: How do I increase starting gold?**  
A: Edit `src/data/GDPlayer.gd` or press F12 console and type:
```gdscript
GameState.player_state.player.crown_stock = 10000
```

**Q: Why do my units have no equipment?**  
A: Equipment must be assigned via `EquipmentSystem.create_class()`. Units don't auto-equip.

**Q: How do settlements get resources they don't produce?**  
A: Via **caravans** (`GDCaravan`). They auto-spawn when a settlement has surplus and another has deficit.

---

*Developed by the Ravensguard Engineering Team. For technical questions, see COMBAT_EXPLANATION.md, ECONOMY_EXPLANATION.md, or SETTLEMENT_EXPLANATION.md.*
