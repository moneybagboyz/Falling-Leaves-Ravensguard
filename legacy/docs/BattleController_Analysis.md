# BattleController.gd Analysis
**Total: 2698 lines, 55 functions**

## System Boundaries Identified

### 1. **BattlePhysics** (~400 lines)
**Responsibility:** Movement, collision, spatial grid, pathfinding

**Functions:**
- `initialize_grid()` - Grid initialization
- `ensure_chunk_at(pos)` - Lazy chunk loading
- `get_tile(x, y)` - Tile access
- `try_move(u, new_pos)` - Movement validation & collision
- `move_towards(u, target_pos)` - Pathfinding toward
- `move_away_from(u, target_pos)` - Pathfinding away
- `get_step_towards(u, t_pos)` - Direction calculation
- `get_step_away(u, t_pos)` - Reverse direction
- `is_in_bounds(pos)` - Bounds checking
- `register_unit(u)` - Add to spatial grid
- `unregister_unit(u)` - Remove from spatial grid
- `update_unit_spatial(u)` - Update bucket position
- `remove_unit_spatial(u)` - Clear bucket position
- `refresh_all_spatial()` - Rebuild spatial grid
- `_find_nearest_enemy_spatial(...)` - Fast enemy search
- `_find_nearest_tile_char(...)` - Fast tile search

**State:**
- `grid` - 2D tile map
- `generated_chunks` - Lazy loading tracker
- `spatial_grid` - Spatial hash buckets
- `spatial_team_mask` - Team presence bitmasks
- `unit_lookup` - Position → Unit map
- Constants: `MAP_W`, `MAP_H`, `SPATIAL_BUCKET_SIZE`

---

### 2. **BattleCombat** (~800 lines)
**Responsibility:** Attack resolution, damage calculation, projectiles, death

**Functions:**
- `perform_attack(u)` - Execute attack
- `perform_attack_on(u, target)` - Attack specific target
- `execute_player_attack(target)` - Player attack handler
- `perform_targeted_attack(...)` - Precise body part attack
- `spawn_projectile(...)` - Create ranged attack
- `resolve_complex_damage(...)` - Full damage calculation (400+ lines!)
- `resolve_aoe_damage(...)` - Area of effect damage
- `_find_unit_along_line(...)` - Penetration raycast
- `get_unit_range(u)` - Attack range calculation
- `is_unit_ranged(u)` - Ranged detection
- `damage_structure(pos, amount)` - Siege structure damage
- `_get_structure_name(tile)` - Structure name lookup

**State:**
- `projectiles` - Active projectile array
- `is_tournament` - Non-lethal mode flag
- `siege_data` - Siege battle data
- `structure_hp` - Fortification HP tracking

---

### 3. **BattleAI** (~600 lines)
**Responsibility:** Unit AI, decision making, formation logic

**Functions:**
- `plan_ai_decision(u)` - Main AI logic (250+ lines!)
- `update_ai_step(delta)` - AI update loop
- `execute_round()` - Turn execution (200+ lines!)
- `_update_global_battle_state()` - Center of mass, formation updates
- `set_order(new_order)` - Change tactical order
- `is_fleeing(u)` - Check flee state
- `escape_unit(u)` - Remove fleeing unit
- `_get_shield_wall_bonus(u)` - Formation bonus calculation

**State:**
- `current_order` - Player formation order (CHARGE, HOLD, etc.)
- `battalions` - Formation data
- `battalion_uid` - Formation ID counter
- `enemy_center_mass` - Enemy tactical center
- `player_center_mass` - Player tactical center
- `cached_pos`, `cached_team`, `cached_alive` - DOD cache arrays

---

### 4. **BattleTerrain** (~500 lines)
**Responsibility:** Map generation, procedural terrain, biomes

**Functions:**
- `generate_map()` - Initialize map generation
- `_generate_chunk(chunk_pos)` - Generate single chunk (200+ lines!)
- `_interp_neighborhood(...)` - Bilinear interpolation
- `_dist_to_segment(...)` - Distance calculation

**State:**
- Uses GameState geology data
- Uses FastNoiseLite for procedural noise
- River/road continuity logic

---

### 5. **BattleSiege** (~300 lines)
**Responsibility:** Siege engines, fortifications, crew management

**Functions:**
- `spawn_siege_units()` - Spawn siege battle setup (100+ lines!)
- `_create_battalion(...)` - Create unit formation with crew

**State:**
- `siege_data` - Walls, towers, gates, keep positions
- Uses GameData.SIEGE_ENGINES

---

### 6. **BattleState** (~400 lines)
**Responsibility:** Battle lifecycle, turn management, win/loss, rewards

**Functions:**
- `start(...)` - Initialize battle (40 lines)
- `end_battle(win)` - Battle resolution (200+ lines!)
- `_check_battle_end_conditions()` - Win/loss detection
- `spawn_units()` - Unit spawning (200+ lines!)
- `create_unit(...)` - Unit factory (200+ lines!)
- `_find_spawn_pos(target)` - Safe spawn location
- `add_log(msg)` - Battle log management
- `get_unit_at(p)` - Unit lookup

**State:**
- `active` - Battle active flag
- `turn` - Turn counter
- `units` - All units array
- `player_unit` - Player commander reference
- `enemy_ref`, `allies_ref` - Faction references
- `tournament_prize` - Reward tracking
- `battle_log` - Message log
- `log_offset` - Scroll position

---

### 7. **BattleInput** (~200 lines)
**Responsibility:** Input handling, targeting mode, camera

**Functions:**
- `handle_input(event)` - Main input router (90 lines)
- `enter_targeting_mode()` - Start targeting UI
- `handle_targeting_input(event)` - Targeting controls (30 lines)
- `move_player(dir)` - Player movement

**State:**
- `targeting_mode` - Targeting active flag
- `targeting_target`, `targeting_parts`, `targeting_index`, `targeting_attack_index`
- `camera_pos`, `camera_locked`, `camera_zoom`
- `auto_battle`, `battle_debug_enabled`

---

### 8. **BattleUpdate** (~100 lines)
**Responsibility:** Delta-based updates, projectile simulation, timing

**Functions:**
- `_process(delta)` - Main update loop (20 lines)

**State:**
- `simulation_time` - Turn simulation timer
- `ui_timer`, `logic_timer` - Frame rate throttling
- `is_batch_processing` - Batch update flag
- Constants: `TICK_RATE`, `UI_REFRESH_RATE`, `LOGIC_TICK`

---

## Extraction Strategy

**Phase 2 Part 1:** Extract systems in dependency order
1. **BattlePhysics** (fewest dependencies)
2. **BattleTerrain** (uses Physics)
3. **BattleCombat** (uses Physics)
4. **BattleSiege** (uses Combat + Physics)
5. **BattleAI** (uses Combat + Physics)
6. **BattleState** (orchestrates all systems)

**Phase 2 Part 2:** Slim down BattleController to coordinator
- Keep: `_process()`, `handle_input()`, system references
- Delegate: All heavy logic to systems

**Estimated Reduction:**
- BattleController: 2698 → ~400 lines (85% reduction!)
- New modules: 6 systems (~2300 lines total)

## Critical Dependencies
- GameState.gd - World data, signals
- GameData.gd - Combat formulas, unit generation
- GDUnit.gd - Unit data structure
- GameState signals: `map_updated`

## Next Steps
1. Create `src/battle/` directory
2. Extract BattlePhysics.gd first (cleanest dependencies)
3. Test for compilation errors
4. Continue with remaining systems
