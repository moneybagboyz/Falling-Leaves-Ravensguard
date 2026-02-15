extends RefCounted
class_name BattleAI

## Battle AI System
## Handles unit AI decisions, formations, tactical orders, and battle flow

# GameData and GameState are autoloads - no need to preload
const GDUnit = preload("res://src/data/GDUnit.gd")

# Tactical state
var enemy_center_mass = Vector2.ZERO
var player_center_mass = Vector2.ZERO
var current_order = "ADVANCE" # ADVANCE, CHARGE, FOLLOW, HOLD, RETREAT

# Performance optimization caches
var cached_pos = []
var cached_team = []
var cached_alive = []

## Execute a full battle round with all unit actions
func execute_round(units: Array, player_unit: GDUnit, battalions: Dictionary, unit_lookup: Dictionary, 
				   is_siege: bool, grid: Array, battle_debug_enabled: bool, turn_ref: Array,
				   add_log_callback: Callable, refresh_spatial_callback: Callable, 
				   get_unit_range_callback: Callable, is_unit_ranged_callback: Callable,
				   is_fleeing_callback: Callable, perform_attack_on_callback: Callable,
				   damage_structure_callback: Callable, move_towards_callback: Callable,
				   move_away_from_callback: Callable, find_nearest_enemy_callback: Callable,
				   find_nearest_tile_callback: Callable):
	
	turn_ref[0] += 1
	
	# REPAIR: Refresh spatial masks and DOD caches at start of turn
	refresh_spatial_callback.call()
	
	# 1. Update Status and Bleeding (Optimization 6: Tick-based)
	for u in units:
		if u.hp <= 0 or u.status["is_dead"]: continue
		
		# Bleeding
		if u.bleed_rate > 0 or u.blood_current < u.blood_max:
			GameData.process_bleeding(u, 0.2, GameState.rng) # 0.2s is one turn burst
			
		if u.status.get("knockdown_timer", 0) > 0:
			u.status["knockdown_timer"] -= 1
			if u.status["knockdown_timer"] <= 0:
				var l_leg_hp = 0
				for t in u.body["l_leg"]["tissues"]: l_leg_hp += t["hp"]
				var r_leg_hp = 0
				for t in u.body["r_leg"]["tissues"]: r_leg_hp += t["hp"]
				if l_leg_hp > 0 or r_leg_hp > 0:
					u.status["is_prone"] = false
					add_log_callback.call("[color=gray]  %s stands back up.[/color]" % u.name)
				else:
					u.status["is_prone"] = true

	# 2. Update global tactical state (Centers of mass, battalion targets)
	update_global_battle_state(units, battalions, unit_lookup, battle_debug_enabled, add_log_callback, 
								is_fleeing_callback)
	
	# 3. Sequential AI Phase (Optimization 7: Sorted Initiative breaks deadlocks)
	var sorted_units = []
	for u in units:
		if u.hp > 0 and not u.status["is_dead"]:
			# INITIATIVE ROLL:
			# 1. Base speed is the primary delay (2.0s, 2.2s, etc.)
			# 2. Agility subtracts from that delay (better reaction)
			# 3. Random Jitter breaks deterministic "side vs side" turns.
			var agi = u.attributes.get("agility", 10)
			var agi_bonus = (agi - 10) * 0.05
			var jitter = GameState.rng.randf_range(-0.3, 0.3)
			
			u.round_initiative = u.speed - agi_bonus + jitter
			sorted_units.append(u)
			
	sorted_units.sort_custom(func(a, b): return a.round_initiative < b.round_initiative)
	
	for u in sorted_units:
		if u == player_unit: continue
		if u.status["is_downed"]: continue
		
		# VECTORIZED MOVEMENT (Optimization 1)
		# If the unit is deep in formation and not engaged, skip expensive logic
		var skip_full_ai = false
		if u.formation_id != -1 and battalions.has(u.formation_id) and u.assigned_engine_id == -1:
			var engaged = _check_unit_engaged(u, unit_lookup)
			
			if not engaged:
				skip_full_ai = _handle_formation_movement(u, battalions, unit_lookup, 
														  get_unit_range_callback, is_unit_ranged_callback)
		
		if not skip_full_ai:
			plan_ai_decision(u, units, player_unit, battalions, unit_lookup, is_siege, grid, 
							 get_unit_range_callback, is_unit_ranged_callback, is_fleeing_callback,
							 find_nearest_enemy_callback, find_nearest_tile_callback)
		
		if u.planned_action != "none":
			match u.planned_action:
				"move":
					if is_fleeing_callback.call(u):
						move_away_from_callback.call(u, u.planned_target_pos)
					else:
						move_towards_callback.call(u, u.planned_target_pos)
				"attack":
					perform_attack_on_callback.call(u, u.planned_target)
				"special":
					damage_structure_callback.call(u.planned_target_pos, u.engine_stats.get("damage", 40.0))
			u.planned_action = "none"

## Check if unit is engaged in melee
func _check_unit_engaged(u: GDUnit, unit_lookup: Dictionary) -> bool:
	for dx in range(-1, 2):
		for dy in range(-1, 2):
			if dx == 0 and dy == 0: continue
			var check_pos = u.pos + Vector2i(dx, dy)
			var other = unit_lookup.get(check_pos)
			if other and other.team != u.team and other.hp > 0:
				return true
	return false

## Handle formation movement optimization
func _handle_formation_movement(u: GDUnit, battalions: Dictionary, unit_lookup: Dictionary,
								 get_unit_range_callback: Callable, is_unit_ranged_callback: Callable) -> bool:
	var b = battalions[u.formation_id]
	var slot_pos = Vector2i(Vector2(b.pivot) + Vector2(u.formation_offset))
	
	# RANGED/SIEGE OPTIMIZATION: Check if we should ignore the slot to keep firing
	var in_firing_range = false
	var weapon_range = get_unit_range_callback.call(u)
	if u.is_siege_engine: weapon_range = u.engine_stats.get("range", 1.5)
	
	if weapon_range > 1.5:
		# Quick Radar check for enemies in potential firing range
		var enemy_bit = 2 if u.team == "player" else (1 if u.team == "enemy" else 7)
		var bx = int(u.pos.x / 10)
		var by = int(u.pos.y / 10)
		var range_buckets = int(weapon_range / 10) + 1
		
		# Note: spatial_team_mask access delegated to physics system
		# For now, simplified check
		in_firing_range = false
	
	# If not at formation slot and no enemies to shoot, move to slot
	if not in_firing_range and u.pos != slot_pos:
		u.planned_action = "move"
		u.planned_target_pos = slot_pos
		return true
	
	# If at slot and infantry/melee...
	if not is_unit_ranged_callback.call(u):
		var enemy_nearby = false
		# Check adjacent tiles for enemies
		for dy in range(-1, 2):
			for dx in range(-1, 2):
				if dx == 0 and dy == 0: continue
				var p = u.pos + Vector2i(dx, dy)
				var other = unit_lookup.get(p)
				if other and other.team != u.team and other.hp > 0:
					enemy_nearby = true
					break
			if enemy_nearby: break

		# If radar shows enemies nearby, run full AI to close the gap
		if enemy_nearby:
			return false
		else:
			u.planned_action = "none"
			return true
	
	# If in firing range, don't skip AI (let plan_ai_decision find the best target)
	if in_firing_range:
		return false
	
	return false

## Update global tactical state - centers of mass, battalion targets, pivot movement
func update_global_battle_state(units: Array, battalions: Dictionary, unit_lookup: Dictionary,
								battle_debug_enabled: bool, add_log_callback: Callable, 
								is_fleeing_callback: Callable):
	var b_data = {} # id -> {sum, count, slowest_speed, engaged}
	var e_sum = Vector2.ZERO
	var p_sum = Vector2.ZERO
	var e_count = 0
	var p_count = 0
	
	for u in units:
		if u.hp <= 0 or u.status["is_downed"] or u.status["is_dead"]: continue
		if is_fleeing_callback.call(u): continue
		
		if u.team == "enemy":
			e_sum += Vector2(u.pos)
			e_count += 1
		else:
			p_sum += Vector2(u.pos)
			p_count += 1
		
		if u.formation_id != -1:
			if not b_data.has(u.formation_id):
				b_data[u.formation_id] = {"sum": Vector2.ZERO, "count": 0, "slowest": 2.0, "engaged": 0}
			b_data[u.formation_id].sum += Vector2(u.pos)
			b_data[u.formation_id].count += 1
			# Keep track of slowest unit for cohesion (high value = slow in this engine's action_timer logic)
			if u.speed > b_data[u.formation_id].slowest:
				b_data[u.formation_id].slowest = u.speed
			
			# Count engaged units for Pivot Braking (Optimization: Use direct lookup for adjacent melee)
			if _check_unit_engaged(u, unit_lookup):
				b_data[u.formation_id].engaged += 1
	
	if e_count > 0: enemy_center_mass = e_sum / e_count
	if p_count > 0: player_center_mass = p_sum / p_count

	var b_centers = {}
	for b_id in b_data:
		if b_data[b_id].count > 0:
			b_centers[b_id] = b_data[b_id].sum / b_data[b_id].count

	# Pre-calculate attacker counts for each battalion
	var attacker_counts = {} # target_id -> count
	for b_id in battalions:
		var target_id = battalions[b_id].target_id
		if target_id != -1:
			attacker_counts[target_id] = attacker_counts.get(target_id, 0) + 1

	# Update Battalion Pivots
	for b_id in battalions:
		var b = battalions[b_id]
		var b_center = b_centers.get(b_id, b.pivot)
		
		# TARGET ASSIGNMENT
		_assign_battalion_target(b, b_id, battalions, b_data, b_centers, attacker_counts)
		
		# CALCULATE PIVOT TARGET
		var b_target = _calculate_battalion_pivot(b, b_id, b_center, b_centers, battalions, b_data, units)
		
		b["target_pos"] = b_target # Cache for units
		_move_battalion_pivot(b, b_id, b_target, b_data, battle_debug_enabled, add_log_callback)
		
		# Shield Wall / Bracing Logic:
		# Units keep their shield wall as long as they are in an active formation.
		# This allows for a "Roman-style" slow push or shielded march.
		if b.type in ["infantry", "commander", "archer", "recruit", "heavy_infantry"]:
			b["is_braced"] = true
		else:
			b["is_braced"] = false

## Assign target battalion for a battalion
func _assign_battalion_target(b: Dictionary, b_id: int, battalions: Dictionary, b_data: Dictionary, 
							   b_centers: Dictionary, attacker_counts: Dictionary):
	var target_valid = false
	if b.target_id != -1 and battalions.has(b.target_id):
		if b_data.has(b.target_id) and b_data[b.target_id].count > 0:
			target_valid = true
	
	if not target_valid:
		var best_target = -1
		var min_dist = 99999.0
		var min_attackers = 999
		var b_center = b_centers.get(b_id, b.pivot)
		
		for other_id in battalions:
			var other = battalions[other_id]
			if other.team != b.team and b_data.has(other_id) and b_data[other_id].count > 0:
				var attackers = attacker_counts.get(other_id, 0)
				var dist = b_center.distance_to(b_centers.get(other_id, other.pivot))
				
				if attackers < min_attackers:
					min_attackers = attackers
					min_dist = dist
					best_target = other_id
				elif attackers == min_attackers and dist < min_dist:
					min_dist = dist
					best_target = other_id
		
		b.target_id = best_target
		if b.target_id != -1:
			attacker_counts[b.target_id] = attacker_counts.get(b.target_id, 0) + 1

## Calculate battalion pivot target position
func _calculate_battalion_pivot(b: Dictionary, b_id: int, b_center: Vector2, b_centers: Dictionary,
								 battalions: Dictionary, b_data: Dictionary, units: Array) -> Vector2:
	var b_target = Vector2.ZERO
	
	if b.target_id != -1:
		var target_b = battalions[b.target_id]
		var target_center = b_centers.get(b.target_id, target_b.pivot)
		
		# "TOTAL WAR" SQUARING UP: 
		# We adjust the "Gap" based on the unit type and current order
		var dir_to_target = (target_center - b_center).normalized()
		var gap = 1.0
		
		if b.type == "siege_engine":
			# Standoff only if battalion is PURELY or HEAVILY artillery
			var artillery_count = 0
			var melee_count = 0
			for u in units:
				if u.formation_id == b_id and u.hp > 0:
					if u.is_siege_engine and u.engine_stats.get("range", 0) > 3.0:
						artillery_count += 1
					elif not u.is_siege_engine:
						melee_count += 1
			
			if artillery_count > 0 and melee_count < 2: # Mostly engines
				gap = 25.0 
				if b.team == "player" and current_order == "CHARGE": gap = 8.0
			else:
				gap = 1.0 # Support infantry move closer
		elif b.type == "archer":
			gap = 8.0
			if b.team == "player" and (current_order == "CHARGE" or current_order == "ADVANCE"): gap = 2.0
		elif b.team == "player":
			match current_order:
				"CHARGE": gap = -0.5
				"ADVANCE": gap = 1.0
				"HOLD": gap = 15.0
				_: gap = 1.5
		elif b.team == "enemy":
			gap = 0.0 # Full aggression
		
		b_target = target_center - (dir_to_target * gap)
	else:
		# Fallback to general center of mass
		b_target = player_center_mass if b.team == "enemy" else enemy_center_mass
	
	# Override based on orders
	if b.team == "player":
		match current_order:
			"FOLLOW":
				# Note: player_unit access would need to be passed as parameter
				# For now, keeping generic behavior
				b_target = Vector2(b.pivot)
			"RETREAT":
				var dir_away = (Vector2(b.pivot) - enemy_center_mass).normalized()
				b_target = Vector2(b.pivot) + (dir_away * 30.0)
			"HOLD":
				b_target = Vector2(b.pivot)
	
	return b_target

## Move battalion pivot towards target with braking logic
func _move_battalion_pivot(b: Dictionary, b_id: int, b_target: Vector2, b_data: Dictionary,
						   battle_debug_enabled: bool, add_log_callback: Callable):
	var b_pivot_v2 = Vector2(b.pivot)
	if b_pivot_v2.distance_to(b_target) > 0.1:
		var move_dir = (b_target - b_pivot_v2).normalized()
		
		var step_size = 1.0 
		
		# PIVOT BRAKING: If more than 40% of units are engaged, the formation slows/stops to wait
		# This creates the "Bannerlord" front-line anchor effect.
		if b_data.has(b_id):
			var engaged_percent = float(b_data[b_id].engaged) / float(b_data[b_id].count)
			if engaged_percent > 0.40:
				step_size = 0.0 # Anchor holds
				if battle_debug_enabled:
					add_log_callback.call("[color=yellow]DEBUG: B-ID %d ANCHORED[/color]" % b_id)
			elif engaged_percent > 0.15:
				step_size = 0.5 # Slowing down
				if battle_debug_enabled:
					add_log_callback.call("[color=yellow]DEBUG: B-ID %d SLOWING[/color]" % b_id)
				
		b.pivot = b_pivot_v2 + (move_dir * step_size) 
		if battle_debug_enabled and b_pivot_v2.distance_to(b_target) <= 0.5:
			add_log_callback.call("[color=yellow]DEBUG: B-ID %d AT TARGET[/color]" % b_id)

## Set tactical order for player battalions
func set_order(new_order: String, add_log_callback: Callable, emit_signal_callback: Callable):
	if current_order != new_order:
		current_order = new_order
		add_log_callback.call("[color=cyan]Order: %s![/color]" % new_order)
		emit_signal_callback.call()

## Plan AI decision for a unit
func plan_ai_decision(u: GDUnit, units: Array, player_unit: GDUnit, battalions: Dictionary, 
					  unit_lookup: Dictionary, is_siege: bool, grid: Array,
					  get_unit_range_callback: Callable, is_unit_ranged_callback: Callable,
					  is_fleeing_callback: Callable, find_nearest_enemy_callback: Callable,
					  find_nearest_tile_callback: Callable):
	# Siege Engine Logic
	if u.is_siege_engine:
		_plan_siege_engine_ai(u, units, unit_lookup, battalions, find_nearest_enemy_callback, find_nearest_tile_callback)
		return
	
	# Crew Assignment Logic
	if u.assigned_engine_id != -1:
		if _plan_crew_ai(u, units, unit_lookup, find_nearest_enemy_callback):
			return
	
	# Fleeing Logic
	if is_fleeing_callback.call(u):
		var flee_target = Vector2i(player_center_mass if u.team == "enemy" else enemy_center_mass)
		u.planned_action = "move"
		u.planned_target_pos = flee_target
		return

	var is_ranged = is_unit_ranged_callback.call(u)
	var u_range = get_unit_range_callback.call(u)
	
	# Reload Timer Check
	if u.reload_timer > 0:
		if u.formation_id != -1 and battalions.has(u.formation_id):
			var b = battalions[u.formation_id]
			var slot_pos = Vector2i(Vector2(b.pivot) + Vector2(u.formation_offset))
			if u.pos != slot_pos:
				u.planned_action = "move"
				u.planned_target_pos = slot_pos
			else:
				u.planned_action = "none"
		else:
			u.planned_action = "none"
		return

	var target_b_id = -1
	if u.formation_id != -1 and battalions.has(u.formation_id):
		target_b_id = battalions[u.formation_id].target_id
		
	# --- COHESION RULE: Formation Steering ---
	if u.formation_id != -1 and battalions.has(u.formation_id):
		_plan_formation_ai(u, units, battalions, unit_lookup, u_range, target_b_id, find_nearest_enemy_callback)
		return

	# --- Individual/Skirmish logic (Broken Formation or Regular Army) ---
	_plan_individual_ai(u, player_unit, unit_lookup, is_siege, grid, is_ranged, u_range, target_b_id, find_nearest_enemy_callback)

## Plan AI for siege engines
func _plan_siege_engine_ai(u: GDUnit, units: Array, unit_lookup: Dictionary, battalions: Dictionary,
						   find_nearest_enemy_callback: Callable, find_nearest_tile_callback: Callable):
	# Siege Engine Manning Check (Fast lookup optimization)
	var manned = false
	for offset in u.footprint:
		var base_p = u.pos + offset
		for dy in range(-1, 2):
			for dx in range(-1, 2):
				var crew = unit_lookup.get(base_p + Vector2i(dx, dy))
				if crew and crew != u and crew.team == u.team and crew.hp > 0:
					if crew.type in ["infantry", "recruit", "laborer", "commander"]:
						manned = true; break
			if manned: break
		if manned: break
	
	if not manned: return

	# Reloading logic: Tick down if manned
	if u.reload_timer > 0:
		u.reload_timer -= 1
		u.planned_action = "none"
		return
	
	var range_val = u.engine_stats.get("range", 1.5)
	
	# Special engine behaviors
	if u.engine_type == "siege_tower":
		var target_wall = find_nearest_tile_callback.call(u.pos, "#", 100)
		if target_wall != Vector2i(-1, -1):
			if u.pos.distance_to(target_wall) <= 1.5: return
			u.planned_action = "move"
			u.planned_target_pos = target_wall
			return

	if u.engine_type == "battering_ram":
		var target_gate = find_nearest_tile_callback.call(u.pos, "G", 100)
		if target_gate != Vector2i(-1, -1):
			if u.pos.distance_to(target_gate) <= 2.5:
				u.planned_action = "special"
				u.planned_target_pos = target_gate
				return
			u.planned_action = "move"
			u.planned_target_pos = target_gate
			return

	# Targeted firing
	var target_b_id = -1
	if u.formation_id != -1 and battalions.has(u.formation_id):
		target_b_id = battalions[u.formation_id].target_id
		
	var target = find_nearest_enemy_callback.call(u, range_val + 30, target_b_id, true)
	
	if target:
		var d = u.pos.distance_to(target.pos)
		if d <= range_val:
			u.planned_action = "attack"
			u.planned_target = target
		else:
			u.planned_action = "move"
			u.planned_target_pos = target.pos

## Plan AI for crew units
func _plan_crew_ai(u: GDUnit, units: Array, unit_lookup: Dictionary, find_nearest_enemy_callback: Callable) -> bool:
	var engine = null
	# Quick linear scan (Assigned crew is usually a small subset)
	for potential in units:
		if potential.id == u.assigned_engine_id:
			engine = potential
			break
	
	if engine and engine.hp > 0:
		# Stay on engine footprint
		var on_footprint = false
		for offset in engine.footprint:
			if u.pos == engine.pos + offset:
				on_footprint = true
				break
		
		if not on_footprint:
			u.planned_action = "move"
			# Move to the most vacant footprint tile
			var target_p = engine.pos
			for offset in engine.footprint:
				var p = engine.pos + offset
				if not unit_lookup.has(p):
					target_p = p
					break
			u.planned_target_pos = target_p
			return true
		
		# Check for melee threats within 1.5 tiles
		var threat = find_nearest_enemy_callback.call(u, 1.5, -1, false)
		if threat:
			u.planned_action = "attack"
			u.planned_target = threat
		return true
	else:
		u.assigned_engine_id = -1
		return false

## Plan AI for units in formation
func _plan_formation_ai(u: GDUnit, units: Array, battalions: Dictionary, unit_lookup: Dictionary,
						u_range: float, target_b_id: int, find_nearest_enemy_callback: Callable):
	var b = battalions[u.formation_id]
	var slot_pos = Vector2i(Vector2(b.pivot) + Vector2(u.formation_offset))
	
	# 1. STICKY ENGAGEMENT (Adjacency check is much faster than spatial search)
	var adj_enemy = null
	for dy in range(-1, 2):
		for dx in range(-1, 2):
			if dx == 0 and dy == 0: continue
			var p = u.pos + Vector2i(dx, dy)
			var other = unit_lookup.get(p)
			if other and other.team != u.team and other.hp > 0 and not other.status["is_dead"]:
				adj_enemy = other
				break
		if adj_enemy: break
		
	if adj_enemy:
		u.planned_action = "attack"
		u.planned_target = adj_enemy
		return
	
	# 2. THE LEASH (Bannerlord order-based behavior)
	# If the order is ADVANCE, the leash is tight (High discipline).
	# If the order is CHARGE, the leash is loose (Aggressive pursuit).
	var leash_dist = 4.0
	if u.team == "player":
		if current_order == "ADVANCE": leash_dist = 2.0
		elif current_order == "CHARGE": leash_dist = 10.0
		elif current_order == "RETREAT": leash_dist = 0.5 # Run directly to the backing pivot
	
	var dist_to_slot = u.pos.distance_to(slot_pos)
	if dist_to_slot > leash_dist:
		u.planned_action = "move"
		u.planned_target_pos = slot_pos
		return
	
	# 3. TACTICAL ENGAGEMENT
	# If we are within the leash, search for a target to engage
	var target = find_nearest_enemy_callback.call(u, 30, target_b_id, false)
	if target:
		var d_to_target = u.pos.distance_to(target.pos)
		if d_to_target <= u_range:
			u.planned_action = "attack"
			u.planned_target = target
			return
	
	# 4. REFORM
	# If idle and not at slot, return to formation.
	if u.pos != slot_pos:
		u.planned_action = "move"
		u.planned_target_pos = slot_pos
		return

## Plan AI for individual units (not in formation)
func _plan_individual_ai(u: GDUnit, player_unit: GDUnit, unit_lookup: Dictionary, is_siege: bool, 
						 grid: Array, is_ranged: bool, u_range: float, target_b_id: int,
						 find_nearest_enemy_callback: Callable):
	var target = find_nearest_enemy_callback.call(u, 30, target_b_id, false)
	var target_pos = Vector2i.ZERO
	var dist = 999.0
	
	if target:
		target_pos = target.pos
		dist = u.pos.distance_to(target_pos)
	else:
		target_pos = Vector2i(player_center_mass if u.team == "enemy" else enemy_center_mass)
		dist = u.pos.distance_to(target_pos)

	# Siege defense: Stay on fortifications
	if is_siege and u.team == "enemy":
		if grid[u.pos.y][u.pos.x] in ["#", "T"]:
			if target and dist <= u_range:
				u.planned_action = "attack"
				u.planned_target = target
			return

	# Enemy AI
	if u.team == "enemy":
		if target:
			if (is_ranged or u.type == "merchant") and dist < 3.0:
				u.planned_action = "move"
				u.planned_target_pos = target.pos
			elif dist <= u_range:
				u.planned_action = "attack"
				u.planned_target = target
			else:
				u.planned_action = "move"
				u.planned_target_pos = target.pos
		else:
			u.planned_action = "move"
			u.planned_target_pos = target_pos
				
	else: # Player team
		match current_order:
			"CHARGE":
				if target:
					if is_ranged and dist < 2.0:
						u.planned_action = "move"
						u.planned_target_pos = target.pos
					elif dist <= u_range:
						u.planned_action = "attack"
						u.planned_target = target
					else:
						u.planned_action = "move"
						u.planned_target_pos = target.pos
				else:
					u.planned_action = "move"
					u.planned_target_pos = target_pos
			"FOLLOW":
				if player_unit:
					var d_to_p = u.pos.distance_to(player_unit.pos)
					if d_to_p > 3:
						u.planned_action = "move"
						u.planned_target_pos = player_unit.pos
					elif target and dist <= u_range:
						u.planned_action = "attack"
						u.planned_target = target
			"HOLD":
				if target and dist <= u_range:
					u.planned_action = "attack"
					u.planned_target = target

## Get shield wall bonus for a unit in formation
func get_shield_wall_bonus(u: GDUnit, battalions: Dictionary, unit_lookup: Dictionary) -> float:
	if u.formation_id == -1 or not battalions.has(u.formation_id): return 0.0
	var b = battalions[u.formation_id]
	if not b.get("is_braced", false): return 0.0
	
	# Only infantry/archers can form a wall
	if not u.type in ["infantry", "commander", "archer", "recruit"]: return 0.0
	
	# Check if unit has a shield
	var off_hand = u.equipment.get("off_hand")
	if not off_hand or off_hand.get("type") != "shield": return 0.0
	
	var bonus = 0.0
	var my_offset = u.formation_offset
	
	# Check adjacency in the formation grid (sideways neighbors)
	# 3x10 grid: 10 is the width (y-axis in formation_offset calculation)
	var neighbors = [Vector2i(0, 1), Vector2i(0, -1)]
	
	for offset_dir in neighbors:
		var target_offset = my_offset + offset_dir
		
		# Optimization: Instead of searching all units, we check absolute neighbor pos in unit_lookup
		var n_abs_pos = Vector2i(Vector2(b.pivot) + Vector2(target_offset))
		var other = unit_lookup.get(n_abs_pos)
		
		if other and other.hp > 0 and other.formation_id == u.formation_id:
			var other_shield = other.equipment.get("off_hand")
			if other_shield and other_shield.get("type") == "shield":
				bonus += 0.15 # 15% block bonus per shielded neighbor
					
	return bonus

## Refresh cached DOD arrays
func refresh_caches(units: Array):
	cached_pos.resize(units.size())
	cached_team.resize(units.size())
	cached_alive.resize(units.size())
	
	for i in range(units.size()):
		var u = units[i]
		var is_alive = u.hp > 0 and not u.status["is_dead"]
		
		cached_pos[i] = Vector2(u.pos)
		cached_team[i] = 0 if u.team == "player" else (1 if u.team == "enemy" else 2)
		cached_alive[i] = 1 if is_alive else 0
