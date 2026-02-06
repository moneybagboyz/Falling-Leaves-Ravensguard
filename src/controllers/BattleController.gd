extends Node

const FaunaData = preload("res://src/data/FaunaData.gd")
const FloraData = preload("res://src/data/FloraData.gd")

const MAP_W = 500
const MAP_H = 500
const CHUNK_SIZE = 50
const TICK_RATE = 0.1 # How fast the game updates (lower = faster)

# -----------------------------

var active = false
var is_tournament = false
var is_siege = false
var siege_data = {}
var structure_hp = {} # Key: Vector2i, Value: float
var last_map_pos = Vector2i(-999, -999)

var grid = [] # 2D array of chars, pre-sized but filled lazily
var generated_chunks: Dictionary = {} # chunk_pos (Vector2i) -> bool

# Structural Cache (Optimization 5)
var structural_cache = {} # char -> Array of Vector2i

# Spatial Hashing (Optimization 3)
const SPATIAL_BUCKET_SIZE = 10
var spatial_grid = {} # Integer Key -> Array of GDUnit
var spatial_team_mask = {} # Integer Key -> int (Bitmask of teams present: 1=player, 2=enemy, 4=ally)

# DOD-Lite Cache (Optimization 4)
var cached_pos = PackedVector2Array() # Index matches 'units' array
var cached_team = PackedInt32Array()   # 0: player, 1: enemy, 2: ally
var cached_alive = PackedByteArray()    # 1: alive, 0: dead

func initialize_grid():
	grid = []
	for y in range(MAP_H):
		var row = []
		row.resize(MAP_W)
		row.fill(" ") # Ungenerated tile
		grid.append(row)
	generated_chunks.clear()
	spatial_grid.clear()
	structural_cache.clear()

func ensure_chunk_at(pos: Vector2i):
	var chunk_pos = Vector2i(pos.x / CHUNK_SIZE, pos.y / CHUNK_SIZE)
	if not generated_chunks.has(chunk_pos):
		_generate_chunk(chunk_pos)

func get_tile(x: int, y: int) -> String:
	if x < 0 or x >= MAP_W or y < 0 or y >= MAP_H:
		return " "
		
	var pos = Vector2i(x, y)
	ensure_chunk_at(pos)
	return grid[y][x]

var tournament_prize = 0
var units = [] 
var unit_lookup = {} # Vector2i -> GDUnit
var battalions = {} # id -> {team, type, pivot_pos, target_pos, order}
var battalion_uid = 0
var projectiles = [] # Array of {pos, target_pos, symbol, attacker, defender, damage_data}
var battle_log = []

func add_log(msg: String):
	battle_log.append(msg)
	if battle_log.size() > 300:
		battle_log.pop_front()
	if not is_batch_processing:
		GameState.emit_signal("map_updated")

var enemy_ref = null 
var allies_ref = null # Used for joint battles

# Turn-based state
var turn = 1
var player_unit = null
var current_order = "CHARGE" # CHARGE, FOLLOW, HOLD
var enemy_center_mass = Vector2.ZERO
var player_center_mass = Vector2.ZERO
var simulation_time = 0.0
var ui_timer = 0.0
var logic_timer = 0.0
const UI_REFRESH_RATE = 0.05 # 20 FPS for UI is plenty for ASCII
const LOGIC_TICK = 0.05 # 20 Hz for center mass and other global updates

# Targeting State
var targeting_mode = false
var targeting_target = null
var targeting_parts = []
var targeting_index = 0
var targeting_attack_index = 0
var log_offset = 0
var auto_battle = false
var battle_debug_enabled = false
var is_batch_processing = false
var camera_pos = Vector2.ZERO
var camera_locked = true
var camera_zoom = 1.0 # 0.5 to 2.0 range

func start(enemy, _is_tournament = false, _prize = 0, allies = null, _is_siege = false, _siege_data = null):
	active = true
	enemy_ref = enemy
	allies_ref = allies
	is_tournament = _is_tournament
	is_siege = _is_siege
	siege_data = _siege_data
	tournament_prize = _prize
	current_order = "ADVANCE"
	turn = 1
	targeting_mode = false
	log_offset = 0
	auto_battle = false
	camera_locked = true
	camera_zoom = 1.0
	
	# REPAIR: Initialize grid BEFORE logging, as logging triggers a UI refresh (_on_map_updated)
	generate_map()
	
	battle_log.clear()
	if is_tournament:
		add_log("[color=yellow]TOURNAMENT MATCH STARTED[/color]")
		add_log("[color=gray]Defeat all opponents to advance. Damage is non-lethal.[/color]")
	else:
		add_log("[color=yellow]BATTLE STARTED (Turn-Based)[/color]")
		add_log("[color=gray]WASD: Move, SPACE: Attack, 1-5: Orders, PGUP/PGDN: Scroll Log[/color]")
		add_log("[color=gray]1: ADVANCE | 2: CHARGE | 3: FOLLOW | 4: HOLD | 5: RETREAT[/color]")
		add_log("[color=gray]V: Toggle Free Cam (WASD to scroll), Z: Auto-Battle[/color]")
		add_log("[color=magenta]PRESS K TO TOGGLE BATTLE DEBUG LOGS[/color]")
	
	spawn_units()
	# Ensure spatial grid and DOD caches are ready for the first turn
	refresh_all_spatial()
	
	if player_unit:
		camera_pos = Vector2(player_unit.pos)
	GameState.emit_signal("map_updated")

func _process(delta):
	if active:
		if simulation_time > 0:
			simulation_time -= delta
			update_ai_step(delta)
			
			ui_timer -= delta
			if ui_timer <= 0:
				ui_timer = UI_REFRESH_RATE
				GameState.emit_signal("map_updated")
		elif auto_battle:
			execute_round()

func handle_input(event):
	if not active or not player_unit: return
	if not event is InputEventKey or not event.pressed: return

	# Metadata/Toggle Keys (Always allowed even during simulation)
	if event.keycode == KEY_Z:
		auto_battle = !auto_battle
		add_log("[color=cyan]Auto-Battle: %s[/color]" % ("ON" if auto_battle else "OFF"))
		GameState.emit_signal("map_updated")
		return
	elif event.keycode == KEY_K:
		battle_debug_enabled = !battle_debug_enabled
		var msg = "*** DEBUG MODE: %s ***" % ("ON" if battle_debug_enabled else "OFF")
		add_log("[color=magenta]%s[/color]" % msg)
		print(msg)
		GameState.emit_signal("map_updated")
		return
	elif event.keycode == KEY_V:
		camera_locked = !camera_locked
		if(!camera_locked and player_unit):
			camera_pos = Vector2(player_unit.pos)
		add_log("[color=cyan]Free Camera: %s[/color]" % ("OFF" if camera_locked else "ON"))
		GameState.emit_signal("map_updated")
		return
	elif event.keycode == KEY_PAGEUP:
		log_offset += 5
		GameState.emit_signal("map_updated")
		return
	elif event.keycode == KEY_PAGEDOWN:
		log_offset = max(0, log_offset - 5)
		GameState.emit_signal("map_updated")
		return

	# Orders (Don't cost a turn) - Always allowed
	if event.keycode == KEY_1: set_order("ADVANCE"); return
	elif event.keycode == KEY_2: set_order("CHARGE"); return
	elif event.keycode == KEY_3: set_order("FOLLOW"); return
	elif event.keycode == KEY_4: set_order("HOLD"); return
	elif event.keycode == KEY_5: set_order("RETREAT"); return

	# Simulation Lock (Blocks movement and combat actions)
	if simulation_time > 0: return 

	var is_incapacitated = player_unit.status["is_dead"] or player_unit.status["is_downed"]

	if targeting_mode:
		if is_incapacitated:
			targeting_mode = false
			GameState.emit_signal("map_updated")
			return
		handle_targeting_input(event)
		return

	# Free cam movement
	if not camera_locked:
		var cam_move = Vector2.ZERO
		if event.keycode == KEY_W: cam_move.y = -1
		elif event.keycode == KEY_S: cam_move.y = 1
		elif event.keycode == KEY_A: cam_move.x = -1
		elif event.keycode == KEY_D: cam_move.x = 1
		
		if cam_move != Vector2.ZERO:
			camera_pos += cam_move * (5.0 / camera_zoom) # Move faster if zoomed out
			camera_pos.x = clamp(camera_pos.x, 0, MAP_W)
			camera_pos.y = clamp(camera_pos.y, 0, MAP_H)
			GameState.emit_signal("map_updated")
			return 

	var acted = false
	var move_dir = Vector2i.ZERO
	
	if event.keycode == KEY_W or event.keycode == KEY_UP or event.keycode == KEY_KP_8: 
		if not is_incapacitated: move_dir.y = -1; acted = true
	elif event.keycode == KEY_S or event.keycode == KEY_DOWN or event.keycode == KEY_KP_2: 
		if not is_incapacitated: move_dir.y = 1; acted = true
	elif event.keycode == KEY_A or event.keycode == KEY_LEFT or event.keycode == KEY_KP_4: 
		if not is_incapacitated: move_dir.x = -1; acted = true
	elif event.keycode == KEY_D or event.keycode == KEY_RIGHT or event.keycode == KEY_KP_6: 
		if not is_incapacitated: move_dir.x = 1; acted = true
	elif event.keycode == KEY_SPACE:
		if not is_incapacitated:
			enter_targeting_mode()
			return # Don't execute round yet
	elif event.keycode == KEY_PERIOD: # Wait turn
		acted = true
	
	if acted:
		if move_dir != Vector2i.ZERO:
			try_move(player_unit, player_unit.pos + move_dir)
		execute_round()

func move_player(dir: Vector2i):
	if simulation_time > 0: return
	if not player_unit: return
	if player_unit.status["is_dead"] or player_unit.status["is_downed"]: return
	
	try_move(player_unit, player_unit.pos + dir)
	execute_round()

func execute_player_attack(target):
	if simulation_time > 0: return
	if not player_unit: return
	if player_unit.status["is_dead"] or player_unit.status["is_downed"]: return
	
	var range_val = get_unit_range(player_unit)
	if player_unit.pos.distance_to(target.pos) <= range_val:
		if is_unit_ranged(player_unit):
			spawn_projectile(player_unit, target, "torso", 0)
		else:
			resolve_complex_damage(player_unit, target, "torso", 0)
		execute_round()
	else:
		add_log("[color=gray]Target out of range![/color]")
		GameState.emit_signal("map_updated")

func get_unit_range(u):
	var range_val = 1.5
	if u.is_siege_engine:
		return float(u.engine_stats.get("range", 1.5))
		
	var wpn = u.equipment["main_hand"]
	if wpn:
		range_val = float(wpn.get("range", 1.5))
	elif u.type == "archer":
		range_val = 18.0
	return range_val

func is_unit_ranged(u):
	if u.is_siege_engine:
		return u.engine_stats.get("range", 1.5) > 2.0
	var wpn = u.equipment["main_hand"]
	if wpn and wpn.get("is_ranged", false):
		return true
	if u.type == "archer":
		return true
	return false

func enter_targeting_mode():
	# Find nearest enemy within range
	var target = null
	var range_val = get_unit_range(player_unit)
	var min_dist = range_val
	
	for u in units:
		if u.team != "player" and u.hp > 0 and not u.status["is_downed"] and not u.status["is_dead"]:
			if not is_fleeing(u):
				var d = player_unit.pos.distance_to(u.pos)
				if d <= range_val:
					if d < min_dist or target == null:
						min_dist = d
						target = u
	
	if target:
		targeting_mode = true
		targeting_target = target
		targeting_parts = []
		for k in target.body.keys():
			var part = target.body[k]
			# Allow major exterior parts: Top-level (Head, Torso) or direct attachments (Limbs, Hands, Feet)
			if not part.get("internal", false):
				var p = part.get("parent", "")
				if not p or p == "torso" or p.ends_with("_arm") or p.ends_with("_leg"):
					targeting_parts.append(k)
		targeting_index = 0
		targeting_attack_index = 0
		add_log("[color=cyan]Targeting %s. W/S: Part, A/D: Attack Type, SPACE: Strike.[/color]" % target.type)
		GameState.emit_signal("map_updated")
	else:
		add_log("[color=gray]No enemy in range (%d tiles)![/color]" % int(range_val))
		GameState.emit_signal("map_updated")

func handle_targeting_input(event):
	if event.keycode == KEY_ESCAPE:
		targeting_mode = false
		add_log("[color=gray]Cancelled attack.[/color]")
		GameState.emit_signal("map_updated")
	elif event.keycode == KEY_W:
		targeting_index = posmod(targeting_index - 1, targeting_parts.size())
		GameState.emit_signal("map_updated")
	elif event.keycode == KEY_S:
		targeting_index = posmod(targeting_index + 1, targeting_parts.size())
		GameState.emit_signal("map_updated")
	elif event.keycode == KEY_A:
		var wpn = player_unit.equipment["main_hand"]
		var attacks = wpn.get("attacks", []) if wpn else []
		if attacks.size() > 0:
			targeting_attack_index = posmod(targeting_attack_index - 1, attacks.size())
			GameState.emit_signal("map_updated")
	elif event.keycode == KEY_D:
		var wpn = player_unit.equipment["main_hand"]
		var attacks = wpn.get("attacks", []) if wpn else []
		if attacks.size() > 0:
			targeting_attack_index = posmod(targeting_attack_index + 1, attacks.size())
			GameState.emit_signal("map_updated")
	elif event.keycode == KEY_SPACE:
		var part_key = targeting_parts[targeting_index]
		if is_unit_ranged(player_unit):
			spawn_projectile(player_unit, targeting_target, part_key, targeting_attack_index)
		else:
			perform_targeted_attack(player_unit, targeting_target, part_key, targeting_attack_index)
		targeting_mode = false
		execute_round()

func perform_targeted_attack(attacker, defender, part_key, attack_idx = 0):
	resolve_complex_damage(attacker, defender, part_key, attack_idx)

func is_fleeing(u):
	if u == player_unit: return false
	var total_hp = 0
	var total_max = 0
	for p_key in u.body:
		for tissue in u.body[p_key]["tissues"]:
			total_hp += tissue["hp"]
			total_max += tissue["hp_max"]
	return total_hp < total_max * 0.2 or u.type == "merchant"

func update_ai_step(delta):
	is_batch_processing = true
	# 1. Update Projectiles (Smooth motion during simulation burst)
	var to_remove = []
	for p in projectiles:
		var dir = (p["target_pos"] - p["pos"]).normalized()
		var move = dir * p["speed"] * delta
		var old_pos = p["pos"]
		p["pos"] += move
		
		# Check if reached or passed target
		var dist_to_target = p["pos"].distance_to(p["target_pos"])
		if dist_to_target < 0.5 or (p["target_pos"] - old_pos).dot(p["target_pos"] - p["pos"]) < 0:
			# Hit!
			if is_instance_valid(p["defender"]) and p["defender"].hp > 0:
				var res = resolve_complex_damage(p["attacker"], p["defender"], p.get("forced_part", ""), p.get("attack_idx", 0))
				
				# Siege Engine Extra Logic
				if p.has("engine"):
					var e_key = p["engine"]
					var e_info = GameData.SIEGE_ENGINES.get(e_key, {})
					
					# AOE Handling
					if e_info.get("aoe", 0) > 0:
						resolve_aoe_damage(p["attacker"], Vector2i(p["target_pos"]), e_info["aoe"], e_info)
						to_remove.append(p)
						continue
					
					# Over-penetration Handling (Ballista)
					if e_info.get("overpenetrate", false) and res.get("remaining_energy", 0.0) > 20.0:
						var next = _find_unit_along_line(p["target_pos"], dir, 15.0, [p["defender"]])
						if next:
							p["defender"] = next
							p["target_pos"] = Vector2(next.pos)
							continue
			
			to_remove.append(p)
	
	for p in to_remove:
		projectiles.erase(p)

	# 2. Check Win/Loss logic occasionally (Optimization: logic_timer)
	logic_timer -= delta
	if logic_timer <= 0:
		logic_timer = LOGIC_TICK
		_check_battle_end_conditions()
	
	is_batch_processing = false

func _check_battle_end_conditions():
	var e_count = 0
	var p_count = 0
	for u in units:
		if u.hp <= 0 or u.status["is_dead"]: continue
		if u.team == "enemy": e_count += 1
		else: p_count += 1
		
	if enemy_ref:
		if e_count == 0:
			add_log("[color=yellow]All enemies have been defeated or routed![/color]")
			end_battle(true)
		elif p_count == 0:
			end_battle(false)
func perform_attack_on(u, target):
	if is_instance_valid(target) and target.hp > 0:
		if is_unit_ranged(u):
			spawn_projectile(u, target, "torso", 0, "engine:" + u.engine_type if u.is_siege_engine else "")
			if u.is_siege_engine:
				u.reload_timer = int(u.engine_stats.get("reload_turns", 4))
			else:
				# Archer Nerf: Add a reload timer for standard ranged units
				# Standard bowmen now fire roughly every 4-5 rounds
				u.reload_timer = 4
		else:
			resolve_complex_damage(u, target, "torso", 0)

func execute_round():
	turn += 1
	log_offset = 0
	simulation_time = 0.2
	is_batch_processing = true
	
	# REPAIR: Refresh spatial masks and DOD caches at start of turn
	refresh_all_spatial()
	
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
					add_log("[color=gray]  %s stands back up.[/color]" % u.name)
				else:
					u.status["is_prone"] = true

	# 2. Update global tactical state (Centers of mass, battalion targets)
	_update_global_battle_state()
	
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
			var engaged = false
			# Fast check for engagement using local grid
			for dx in range(-1, 2):
				for dy in range(-1, 2):
					if dx == 0 and dy == 0: continue
					var check_pos = u.pos + Vector2i(dx, dy)
					var other = unit_lookup.get(check_pos)
					if other and other.team != u.team and other.hp > 0:
						engaged = true; break
				if engaged: break
			
			if not engaged:
				var b = battalions[u.formation_id]
				var slot_pos = Vector2i(Vector2(b.pivot) + Vector2(u.formation_offset))
				
				# RANGED/SIEGE OPTIMIZATION: Check if we should ignore the slot to keep firing
				var in_firing_range = false
				var weapon_range = get_unit_range(u)
				if u.is_siege_engine: weapon_range = u.engine_stats.get("range", 1.5)
				
				if weapon_range > 1.5:
					# Quick Radar check for enemies in potential firing range
					var enemy_bit = 2 if u.team == "player" else (1 if u.team == "enemy" else 7)
					var bx = int(u.pos.x / SPATIAL_BUCKET_SIZE)
					var by = int(u.pos.y / SPATIAL_BUCKET_SIZE)
					var range_buckets = int(weapon_range / SPATIAL_BUCKET_SIZE) + 1
					
					for ny in range(by - range_buckets, by + range_buckets + 1):
						for nx in range(bx - range_buckets, bx + range_buckets + 1):
							var k = (nx << 16) | (ny & 0xFFFF)
							if spatial_team_mask.get(k, 0) & enemy_bit != 0:
								in_firing_range = true; break
						if in_firing_range: break
				
				# If not at formation slot and no enemies to shoot, move to slot
				if not in_firing_range and u.pos != slot_pos:
					u.planned_action = "move"
					u.planned_target_pos = slot_pos
					skip_full_ai = true
				
				# If at slot and infantry/melee...
				elif not is_unit_ranged(u):
					# Check if an enemy is in the "Aggression Zone"
					var enemy_bit = 2 if u.team == "player" else (1 if u.team == "enemy" else 7)
					var bx = int(u.pos.x / SPATIAL_BUCKET_SIZE)
					var by = int(u.pos.y / SPATIAL_BUCKET_SIZE)
					
					var enemy_nearby = false
					# Check current and 8 neighbor buckets
					for ny in range(by - 1, by + 2):
						for nx in range(bx - 1, bx + 2):
							var key = (nx << 16) | (ny & 0xFFFF)
							if spatial_team_mask.get(key, 0) & enemy_bit != 0:
								enemy_nearby = true; break
						if enemy_nearby: break

					# If radar shows enemies nearby, run full AI to close the gap
					if enemy_nearby:
						skip_full_ai = false
					else:
						u.planned_action = "none"
						skip_full_ai = true
				
				# If in firing range, don't skip AI (let plan_ai_decision find the best target)
				elif in_firing_range:
					skip_full_ai = false
		
		if not skip_full_ai:
			plan_ai_decision(u)
		
		if u.planned_action != "none":
			match u.planned_action:
				"move":
					if is_fleeing(u):
						move_away_from(u, u.planned_target_pos)
					else:
						move_towards(u, u.planned_target_pos)
				"attack":
					perform_attack_on(u, u.planned_target)
				"special":
					damage_structure(u.planned_target_pos, u.engine_stats.get("damage", 40.0))
			u.planned_action = "none"

	# 4. Final log cleanup
	if not battle_debug_enabled and battle_log.size() > 200:
		battle_log = battle_log.slice(-150)
	elif battle_log.size() > 500:
		battle_log = battle_log.slice(-400)
	
	is_batch_processing = false
	GameState.emit_signal("map_updated")

func _update_global_battle_state():
	var b_data = {} # id -> {sum, count, slowest_speed}
	var e_sum = Vector2.ZERO
	var p_sum = Vector2.ZERO
	var e_count = 0
	var p_count = 0
	
	for u in units:
		if u.hp <= 0 or u.status["is_downed"] or u.status["is_dead"]: continue
		if is_fleeing(u): continue
		
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
			var engaged = false
			for dx in range(-1, 2):
				for dy in range(-1, 2):
					if dx == 0 and dy == 0: continue
					var check_pos = u.pos + Vector2i(dx, dy)
					if unit_lookup.has(check_pos):
						var other = unit_lookup[check_pos]
						if other.team != u.team and other.hp > 0:
							engaged = true; break
				if engaged: break
			
			if engaged:
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
		var target_valid = false
		if b.target_id != -1 and battalions.has(b.target_id):
			if b_data.has(b.target_id) and b_data[b.target_id].count > 0:
				target_valid = true
		
		if not target_valid:
			var best_target = -1
			var min_dist = 99999.0
			var min_attackers = 999
			
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
		
		# CALCULATE PIVOT TARGET
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
					if player_unit: b_target = Vector2(player_unit.pos)
				"RETREAT":
					var dir_away = (Vector2(b.pivot) - enemy_center_mass).normalized()
					b_target = Vector2(b.pivot) + (dir_away * 30.0)
				"HOLD":
					b_target = Vector2(b.pivot)
		
		b["target_pos"] = b_target # Cache for units
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
						add_log("[color=yellow]DEBUG: B-ID %d ANCHORED[/color]" % b_id)
				elif engaged_percent > 0.15:
					step_size = 0.5 # Slowing down
					if battle_debug_enabled:
						add_log("[color=yellow]DEBUG: B-ID %d SLOWING[/color]" % b_id)
					
			b.pivot = b_pivot_v2 + (move_dir * step_size) 
			if battle_debug_enabled and b_pivot_v2.distance_to(b_target) <= 0.5:
				add_log("[color=yellow]DEBUG: B-ID %d AT TARGET[/color]" % b_id)
		
		# Shield Wall / Bracing Logic:
		# Units keep their shield wall as long as they are in an active formation.
		# This allows for a "Roman-style" slow push or shielded march.
		if b.type in ["infantry", "commander", "archer", "recruit", "heavy_infantry"]:
			b["is_braced"] = true
		else:
			b["is_braced"] = false

func set_order(new_order):
	if current_order != new_order:
		current_order = new_order
		add_log("[color=cyan]Order: %s![/color]" % new_order)
		GameState.emit_signal("map_updated")

func plan_ai_decision(u):
	if u.is_siege_engine:
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
	else:
		# Archer/Infantry reload logic
		if u.reload_timer > 0:
			u.reload_timer -= 1
			# Allowing movement while reloading for regular units, just not attacking
			# (Engineers have to stay with the engine to reload it)

	if u.is_siege_engine:
		var range_val = u.engine_stats.get("range", 1.5)
		
		if u.engine_type == "siege_tower":
			# Search for walls
			var target_wall = _find_nearest_tile_char(u.pos, "#", 100)
			if target_wall != Vector2i(-1, -1):
				if u.pos.distance_to(target_wall) <= 1.5: return
				u.planned_action = "move"
				u.planned_target_pos = target_wall
				return

		if u.engine_type == "battering_ram":
			var target_gate = _find_nearest_tile_char(u.pos, "G", 100)
			if target_gate != Vector2i(-1, -1):
				if u.pos.distance_to(target_gate) <= 2.5:
					u.planned_action = "special"
					u.planned_target_pos = target_gate
					return
				u.planned_action = "move"
				u.planned_target_pos = target_gate
				return

		# Targeted firing
		var target = null
		var best_dist = range_val + 1.0
		# Find nearest enemy via spatial grid for efficiency
		var target_b_id = -1
		if u.formation_id != -1 and battalions.has(u.formation_id):
			target_b_id = battalions[u.formation_id].target_id
			
		target = _find_nearest_enemy_spatial(u, range_val + 30, target_b_id, true) # Engines prioritize clusters
		
		if target:
			var d = u.pos.distance_to(target.pos)
			if d <= range_val:
				u.planned_action = "attack"
				u.planned_target = target
			else:
				u.planned_action = "move"
				u.planned_target_pos = target.pos
		return

	if u.assigned_engine_id != -1:
		var engine = null
		# Quick linear scan (Assigned crew is usually a small subset)
		for potential in units:
			if potential.id == u.assigned_engine_id:
				engine = potential; break
		
		if engine and engine.hp > 0:
			# Stay on engine footprint
			var on_footprint = false
			for offset in engine.footprint:
				if u.pos == engine.pos + offset:
					on_footprint = true; break
			
			if not on_footprint:
				u.planned_action = "move"
				# Move to the most vacant footprint tile
				var target_p = engine.pos
				for offset in engine.footprint:
					var p = engine.pos + offset
					if not unit_lookup.has(p):
						target_p = p; break
				u.planned_target_pos = target_p
				return
			
			# Check for melee threats within 1.5 tiles
			var threat = _find_nearest_enemy_spatial(u, 1.5, -1)
			if threat:
				u.planned_action = "attack"
				u.planned_target = threat
			return 
		else:
			u.assigned_engine_id = -1

	# 3. Standard Unit Behavior
	if is_fleeing(u):
		var flee_target = Vector2i(player_center_mass if u.team == "enemy" else enemy_center_mass)
		u.planned_action = "move"
		u.planned_target_pos = flee_target
		return

	var is_ranged = is_unit_ranged(u)
	var u_range = get_unit_range(u)
	
	if u.reload_timer > 0:
		# If we are in formation, we still want to move to our slot even if reloading
		# But we can't do anything else (like attack)
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
					adj_enemy = other; break
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
		var target = _find_nearest_enemy_spatial(u, 30, target_b_id)
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
			
		return

	# --- Individual/Skirmish logic (Broken Formation or Regular Army) ---
	var target = _find_nearest_enemy_spatial(u, 30, target_b_id)
	var target_pos = Vector2i.ZERO
	var dist = 999.0
	
	if target:
		target_pos = target.pos
		dist = u.pos.distance_to(target_pos)
	else:
		target_pos = Vector2i(player_center_mass if u.team == "enemy" else enemy_center_mass)
		dist = u.pos.distance_to(target_pos)

	if is_siege and u.team == "enemy":
		if grid[u.pos.y][u.pos.x] in ["#", "T"]:
			if target and dist <= u_range:
				u.planned_action = "attack"
				u.planned_target = target
			return

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

# --- AI Helper Methods (Optimization 2: Pure Calculation) ---

func get_step_towards(u, t_pos):
	var dir = (Vector2(t_pos) - Vector2(u.pos)).normalized()
	return u.pos + Vector2i(round(dir.x), round(dir.y))

func get_step_away(u, t_pos):
	var dir = (Vector2(u.pos) - Vector2(t_pos)).normalized()
	if dir == Vector2.ZERO: dir = Vector2(1, 0)
	return u.pos + Vector2i(round(dir.x), round(dir.y))

func _find_nearest_enemy_spatial(u, max_dist, target_b_id = -1, prioritize_clusters = false):
	var best_target_match = null
	var min_d_target = max_dist
	
	var best_any_match = null
	var min_d_any = max_dist
	
	# For Cluster Prioritization (Siege)
	var best_cluster_score = -1.0
	var best_cluster_target = null
	
	var bx = int(u.pos.x / SPATIAL_BUCKET_SIZE)
	var by = int(u.pos.y / SPATIAL_BUCKET_SIZE)
	var r = int(ceil(max_dist / float(SPATIAL_BUCKET_SIZE)))
	
	# BITMASK OPTIMIZATION (Optimization 3)
	var enemy_bit = 2 if u.team == "player" else (1 if u.team == "enemy" else 7) 
	
	for ny in range(by - r, by + r + 1):
		for nx in range(bx - r, bx + r + 1):
			var key = (nx << 16) | (ny & 0xFFFF)
			
			# Skip entire buckets if the mask says no enemies are here
			if spatial_team_mask.get(key, 0) & enemy_bit == 0:
				continue
				
			var bucket = spatial_grid.get(key, null)
			if bucket:
				for e in bucket:
					if e.team != u.team and e.hp > 0 and not e.status["is_dead"]:
						var d = u.pos.distance_to(e.pos)
						
						if prioritize_clusters:
							# Siege engines love Shield Walls and packed formations
							# We give a score based on how many enemies are in the same bucket
							# This is a extremely fast proxy for density
							var density = bucket.size()
							var score = float(density) / (d * 0.5) # Prefer closer AND denser
							if score > best_cluster_score:
								best_cluster_score = score
								best_cluster_target = e
						
						# Track closest in target battalion
						if target_b_id != -1 and e.formation_id == target_b_id:
							if d < min_d_target:
								min_d_target = d
								best_target_match = e
						
						# Track closest overall for self-defense fallback
						if d < min_d_any:
							min_d_any = d
							best_any_match = e
	
	if prioritize_clusters and best_cluster_target:
		return best_cluster_target
		
	if best_target_match:
		return best_target_match
	return best_any_match

func _find_nearest_tile_char(pos, char_to_find, max_dist):
	# Optimized Structural Cache check (Optimization 5)
	if structural_cache.has(char_to_find):
		var best = Vector2i(-1, -1)
		var min_d = max_dist
		for target in structural_cache[char_to_find]:
			var d = pos.distance_to(target)
			if d < min_d:
				min_d = d
				best = target
		return best

	# Fallback for structural targets (static grid) - Should rarely trigger now
	var best = Vector2i(-1, -1)
	var min_d = max_dist
	for dy in range(-max_dist, max_dist + 1):
		for dx in range(-max_dist, max_dist + 1):
			var wx = pos.x + dx
			var wy = pos.y + dy
			if wx >= 0 and wx < MAP_W and wy >= 0 and wy < MAP_H:
				if grid[wy][wx] == char_to_find:
					var d = pos.distance_to(Vector2i(wx, wy))
					if d < min_d:
						min_d = d
						best = Vector2i(wx, wy)
	return best

func move_towards(u, target_pos):
	if u.is_siege_engine and not u.engine_stats.get("is_mobile", true): return
	
	var diff = target_pos - u.pos
	var dir = diff.sign()
	
	# 1. Primary Path (Diagonal if possible)
	if dir.x != 0 and dir.y != 0:
		if try_move(u, u.pos + Vector2i(dir.x, dir.y)): return

	# 2. Orthogonal Path (Straight lines)
	if dir.x != 0 and try_move(u, u.pos + Vector2i(dir.x, 0)): return
	if dir.y != 0 and try_move(u, u.pos + Vector2i(0, dir.y)): return
	
	# 3. Smart Sliding (Pathfinding around allies/terrain)
	if u.pos.distance_to(target_pos) > get_unit_range(u):
		# If moving along an axis is blocked, try "niggling" sideways
		if dir.x != 0: 
			if try_move(u, u.pos + Vector2i(dir.x, 1)): return
			if try_move(u, u.pos + Vector2i(dir.x, -1)): return
		if dir.y != 0:
			if try_move(u, u.pos + Vector2i(1, dir.y)): return
			if try_move(u, u.pos + Vector2i(-1, dir.y)): return

func move_away_from(u, target_pos):
	var diff = u.pos - target_pos
	var dir = diff.sign()
	if dir == Vector2i.ZERO: dir = Vector2i(GameState.rng.randi_range(-1, 1), GameState.rng.randi_range(-1, 1))
	if dir == Vector2i.ZERO: dir = Vector2i(1, 0)
	
	# Try escape path
	if try_move(u, u.pos + dir): return
	# Try sliding escape
	if dir.x != 0 and try_move(u, u.pos + Vector2i(dir.x, 0)): return
	if dir.y != 0 and try_move(u, u.pos + Vector2i(0, dir.y)): return
	# Try Y axis
	if dir.y != 0 and try_move(u, u.pos + Vector2i(0, dir.y)): return

func register_unit(u):
	for offset in u.footprint:
		unit_lookup[u.pos + offset] = u
	update_unit_spatial(u)

func unregister_unit(u):
	for offset in u.footprint:
		unit_lookup.erase(u.pos + offset)
	remove_unit_spatial(u)

func update_unit_spatial(u):
	var bx = int(u.pos.x / SPATIAL_BUCKET_SIZE)
	var by = int(u.pos.y / SPATIAL_BUCKET_SIZE)
	var key = (bx << 16) | (by & 0xFFFF)
	
	if not spatial_grid.has(key):
		spatial_grid[key] = []
		spatial_team_mask[key] = 0
	
	if not u in spatial_grid[key]:
		spatial_grid[key].append(u)
		
		# Update Team Mask (Optimization 3)
		var team_bit = 1 if u.team == "player" else (2 if u.team == "enemy" else 4)
		spatial_team_mask[key] |= team_bit

func remove_unit_spatial(u):
	var bx = int(u.pos.x / SPATIAL_BUCKET_SIZE)
	var by = int(u.pos.y / SPATIAL_BUCKET_SIZE)
	var key = (bx << 16) | (by & 0xFFFF)
	if spatial_grid.has(key):
		spatial_grid[key].erase(u)
		# NOTE: We don't clear the team mask bit lazily for performance, 
		# it gets fully cleared in refresh_all_spatial() anyway.

func refresh_all_spatial():
	spatial_grid.clear()
	spatial_team_mask.clear()
	
	# Also update DOD Cache here (Optimization 4)
	cached_pos.resize(units.size())
	cached_team.resize(units.size())
	cached_alive.resize(units.size())
	
	for i in range(units.size()):
		var u = units[i]
		var is_alive = u.hp > 0 and not u.status["is_dead"]
		
		cached_pos[i] = Vector2(u.pos)
		cached_team[i] = 0 if u.team == "player" else (1 if u.team == "enemy" else 2)
		cached_alive[i] = 1 if is_alive else 0
		
		if is_alive:
			update_unit_spatial(u)

func try_move(u, new_pos):
	if u.status.get("is_prone", false) or u.status.get("is_paralyzed", false):
		return false
		
	# Check entire footprint for bounds and collisions
	for offset in u.footprint:
		var p = new_pos + offset
		if not is_in_bounds(p):
			# Allow escape if fleeing or if it's the player trying to leave
			if is_fleeing(u) or (u == player_unit):
				escape_unit(u)
				return true
			return false
		
		# Check unit collision (ignore tiles occupied by self)
		if unit_lookup.has(p) and unit_lookup[p] != u:
			return false
			
		# Terrain Collision
		var tile = grid[p.y][p.x]
		if tile in ["^", "~"]: # Rock or Deep Water
			return false
		
		# Wall/Structural Collision
		if tile == "#":
			var has_access = false
			if u.is_hero: has_access = true
			
			# 0. If already on a wall, can move to adjacent wall
			if grid[u.pos.y][u.pos.x] == "#":
				has_access = true
				
			if not has_access:
				# 1. Check if unit is currently "on" or adjacent to a docked Siege Tower
				for dy in range(-1, 2):
					for dx in range(-1, 2):
						var check_p = u.pos + Vector2i(dx, dy)
						if unit_lookup.has(check_p):
							var engine = unit_lookup[check_p]
							if engine.is_siege_engine and engine.engine_type == "siege_tower":
								# Siege Tower must be adjacent to the target wall tile p
								for ty in range(-1, 2):
									for tx in range(-1, 2):
										var tower_tile = engine.pos + Vector2i(tx, ty)
										if tower_tile.distance_to(p) <= 1.1:
											has_access = true; break
									if has_access: break
						if has_access: break
					if has_access: break
			
			if not has_access:
				return false

	# Update Lookup and Spatial
	var diff = new_pos - u.pos
	if diff != Vector2i.ZERO:
		u.facing = Vector2i(int(sign(float(diff.x))), int(sign(float(diff.y))))

	unregister_unit(u)
	u.pos = new_pos
	register_unit(u)
	return true

func escape_unit(u):
	unregister_unit(u)
	u.hp = -1 # Mark as removed
	
	if u == player_unit:
		add_log("[color=gray]You have retreated from battle![/color]")
		end_battle(false) # Retreat counts as loss/end
	else:
		add_log("[color=gray]%s has fled the battlefield![/color]" % u.type)

func damage_structure(pos: Vector2i, amount: float):
	if not structure_hp.has(pos):
		var tile = grid[pos.y][pos.x]
		var base_hp = 100.0
		match tile:
			"G": base_hp = 300.0 # Gate
			"#": base_hp = 500.0 # Wall
			"H": base_hp = 1000.0 # Heavy Wall
			"K": base_hp = 2000.0 # Keep
		structure_hp[pos] = base_hp
	
	structure_hp[pos] -= amount
	if structure_hp[pos] <= 0:
		var old_tile = grid[pos.y][pos.x]
		grid[pos.y][pos.x] = "%" # Rubble
		# Update structural cache
		if structural_cache.has(old_tile):
			structural_cache[old_tile].erase(pos)
		add_log("[color=red]The %s has been destroyed![/color]" % _get_structure_name(old_tile))
		
func _get_structure_name(tile: String) -> String:
	match tile:
		"G": return "Gate"
		"#": return "Wall"
		"H": return "Heavy Wall"
		"K": return "Keep"
	return "Structure"

func perform_attack(u):
	# Find target in front or range
	var hits = []
	var range_val = get_unit_range(u)
	var is_ranged = is_unit_ranged(u)
	
	# Optimized Attack Scan (Spiral/Box Search instead of global)
	var r = int(ceil(range_val))
	for dy in range(-r, r+1):
		for dx in range(-r, r+1):
			var check_pos = u.pos + Vector2i(dx, dy)
			if unit_lookup.has(check_pos):
				var other = unit_lookup[check_pos]
				if other.team != u.team and other.hp > 0 and not other.status["is_downed"] and not other.status["is_dead"]:
					if not is_fleeing(other):
						if u.pos.distance_to(other.pos) <= range_val:
							hits.append(other)
							if not is_ranged: break # Melee hits one
			if hits.size() > 0 and not is_ranged: break
		if hits.size() > 0 and not is_ranged: break
	
	if hits.size() > 0:
		var target = hits[0] # Just hit first found
		
		# Update Facing
		var attack_diff = target.pos - u.pos
		if attack_diff != Vector2i.ZERO:
			u.facing = Vector2i(int(sign(float(attack_diff.x))), int(sign(float(attack_diff.y))))

		# Check if attacking arm is functional
		if not u.status.get("r_arm_functional", true):
			if u == player_unit:
				add_log("[color=orange]Your right arm is useless! You can't strike![/color]")
			return

		# Choose attack index (random for AI)
		var attack_idx = 0
		var wpn = u.equipment["main_hand"]
		var attacks = wpn.get("attacks", []) if wpn else []
		if attacks.size() > 0:
			attack_idx = GameState.rng.randi() % attacks.size()
			
		if is_ranged:
			spawn_projectile(u, target, "", attack_idx)
		else:
			resolve_complex_damage(u, target, "", attack_idx)
	elif u == player_unit:
		add_log("[color=gray]You missed! Get closer![/color]")

func spawn_projectile(attacker, defender, forced_part = "", attack_idx = 0, mode = "standard"):
	var sym = "*"
	var p_data = {}
	
	if mode.begins_with("engine:"):
		var engine_key = mode.split(":")[1]
		var e_info = GameData.SIEGE_ENGINES.get(engine_key, {})
		sym = e_info.get("symbol", "X")
		p_data["engine"] = engine_key
		p_data["remaining_energy"] = e_info.get("dmg_base", 50) + (e_info.get("weight", 5) * e_info.get("velocity", 5))
	else:
		# Dynamic Symbol Selection based on orientation
		var diff = Vector2(defender.pos - attacker.pos)
		if abs(diff.x) > abs(diff.y) * 2: sym = "-"
		elif abs(diff.y) > abs(diff.x) * 2: sym = "|"
		elif diff.x * diff.y > 0: sym = "\\"
		else: sym = "/"
	
	var projectile = {
		"pos": Vector2(attacker.pos),
		"target_pos": Vector2(defender.pos),
		"symbol": sym,
		"attacker": attacker,
		"defender": defender,
		"forced_part": forced_part,
		"attack_idx": attack_idx,
		"speed": 35.0, # Increased speed for smoother frame-by-frame visibility
		"mode": mode,
		"traveled": 0.0
	}
	
	for k in p_data:
		projectile[k] = p_data[k]
		
	projectiles.append(projectile)

func resolve_complex_damage(attacker, defender, forced_part = "", attack_idx = 0):
	# Siege Engine Physics Integration
	var res = {}
	if attacker.is_siege_engine:
		res = GameData.resolve_engine_damage(attacker.engine_type, defender, GameState.rng)
		# Siege engines break the formation's bracing upon impact
		if defender.formation_id != -1:
			var b = battalions.get(defender.formation_id)
			if b and b.get("is_braced", false):
				b["is_braced"] = false
				if battle_debug_enabled:
					add_log("[color=red]DEBUG: FORMATION %d BRACING BROKEN BY %s[/color]" % [defender.formation_id, attacker.engine_type.to_upper()])
	else:
		var sw_bonus = _get_shield_wall_bonus(defender)
		res = GameData.resolve_attack(attacker, defender, GameState.rng, forced_part, attack_idx, sw_bonus)
	
	# Weapon / Attacker Name Construction
	var wpn = attacker.equipment["main_hand"]
	var wpn_part = "fists"
	var is_plural = true
	if wpn:
		var ammo = attacker.equipment.get("ammo")
		if wpn.get("is_ranged", false) and ammo:
			wpn_part = ammo.get("name", "arrow")
		else:
			var mat = wpn.get("material", "").capitalize()
			var w_name = wpn.get("name", "weapon")
			if w_name.begins_with(mat):
				wpn_part = w_name
			else:
				wpn_part = (mat + " " + w_name).strip_edges()
		is_plural = false
	
	var wpn_owner = ""
	if attacker.name == "You":
		wpn_owner = "Your %s" % wpn_part
	else:
		wpn_owner = "The %s's %s" % [attacker.name, wpn_part]

	if not res["hit"]:
		var miss_color = "green" if attacker.team == "player" else "red"
		var is_ranged = wpn.get("is_ranged", false) if wpn else false
		var miss_verb = "misses" if not is_plural else "miss"
		var action_verb = "flies wide" if is_ranged else "swings wide"
		add_log("[color=%s]%s %s and %s %s![/color]" % [miss_color, wpn_owner, action_verb, miss_verb, defender.name])
		return

	if res["blocked"]:
		var block_color = "green" if defender.team == "player" else "red"
		add_log("[color=%s]%s raises their %s and deflects the blow![/color]" % [block_color, defender.name, res["shield_name"]])
		return

	# Main Hit Log
	var log_color = "green" if attacker.team == "player" else "red"
	var part_display = "[color=yellow]%s[/color]" % res["part_hit"]
	
	# Determine descriptive verb and action
	var desc_verb = "hits"
	var dt = res.get("dmg_type", "blunt")
	var tissue = "skin"
	if res["tissues_hit"].size() > 0:
		tissue = res["tissues_hit"][-1]
	
	var armor_action = "deflected"
	if dt == "blunt":
		armor_action = "absorbed"
		if tissue == "bone": desc_verb = "smashes"
		elif tissue == "organ": desc_verb = "crushes"
		else: desc_verb = "bashes"
	elif dt == "pierce":
		armor_action = "pierced"
		if tissue == "bone": desc_verb = "pierces"
		elif tissue == "organ": desc_verb = "punctures"
		else: desc_verb = "stabs"
	elif dt == "cut":
		armor_action = "deflected"
		if tissue == "bone": desc_verb = "hacks"
		elif tissue == "organ": desc_verb = "cleaves"
		else: desc_verb = "cuts"

	var armor_desc = ""
	if res["armor_layers"].size() > 0:
		var top_armor = res["armor_layers"][-1]
		armor_desc = " (partially %s by the [i]%s[/i])" % [armor_action, top_armor]
	
	var final_verb = desc_verb
	if is_plural:
		match final_verb:
			"hits": final_verb = "hit"
			"smashes": final_verb = "smash"
			"punctures": final_verb = "puncture"
			"cuts": final_verb = "cut"
			"bashes": final_verb = "bash"
			"crushes": final_verb = "crush"
			"pierces": final_verb = "pierce"
			"stabs": final_verb = "stab"
			"hacks": final_verb = "hack"
			"cleaves": final_verb = "cleave"
	
	# Descriptive tissue impact
	var impact_desc = ""
	match tissue:
		"skin":
			if dt == "cut": impact_desc = "nicking the skin"
			elif dt == "pierce": impact_desc = "puncturing the skin"
			else: impact_desc = "bruising the skin"
		"fat":
			if dt == "cut": impact_desc = "slicing into the fat"
			elif dt == "pierce": impact_desc = "stabbing the fat"
			else: impact_desc = "bruising the fat"
		"muscle":
			if dt == "cut": impact_desc = "tearing through the muscle"
			elif dt == "pierce": impact_desc = "puncturing the muscle"
			else: impact_desc = "bruising the muscle"
		"bone":
			if dt == "cut": impact_desc = "hacking the bone"
			elif dt == "pierce": impact_desc = "piercing the bone"
			else: impact_desc = "shattering the bone"
		"organ":
			if dt == "cut": impact_desc = "cleaving the organ"
			elif dt == "pierce": impact_desc = "puncturing the organ"
			else: impact_desc = "rupturing the organ"
		"tendon": 
			impact_desc = "tearing the tendon" if dt != "blunt" else "crushing the tendon"
		"nerve": 
			impact_desc = "shredding the nerve" if dt != "blunt" else "compressing the nerve"

	var log_msg = "[color=%s]%s %s %s's %s, %s%s![/color]" % [log_color, wpn_owner, final_verb, defender.name, part_display, impact_desc, armor_desc]
	if res["final_dmg"] > 0:
		log_msg += " [color=yellow](-%d HP)[/color]" % res["final_dmg"]
	add_log(log_msg)

	# Log Critical Events
	for event in res["critical_events"]:
		match event:
			"artery_severed":
				add_log("[color=red]  [CRITICAL] An artery in the %s has been severed! Blood sprays![/color]" % res["part_hit"])
			"vein_opened":
				add_log("[color=red]  A major vein in the %s has been opened![/color]" % res["part_hit"])
			"tendon_snapped":
				add_log("[color=orange]  [CRITICAL] The tendon in the %s snaps with a sickening pop![/color]" % [res["part_hit"]])
			"nerve_destroyed":
				add_log("[color=red]  [CRITICAL] The nerve in the %s is shredded, leaving it limp![/color]" % [res["part_hit"]])
			"bone_fractured":
				add_log("[color=orange]  [CRITICAL] The bone in the %s shatters under the impact![/color]" % [res["part_hit"]])
			"brain_destroyed":
				add_log("[color=red]  [FATAL] The brain is pulverized! %s dies instantly![/color]" % defender.name)
			"heart_burst":
				add_log("[color=red]  [FATAL] The heart is burst! %s's life-blood sprays![/color]" % defender.name)
			"eye_gouged":
				add_log("[color=red]  [CRITICAL] %s's eye is gouged out, leaving a bloody socket![/color]" % defender.name)
			"decapitated":
				add_log("[color=red]  [FATAL] %s's head is completely severed from their body![/color]" % defender.name)
			"part_destroyed":
				add_log("[color=red]  [FATAL] The %s is completely obliterated![/color]" % [res["part_hit"]])
		
		if event.begins_with("organ_failure:"):
			var organ_name = event.split(":")[1]
			add_log("[color=red]  [FATAL] The %s has failed! %s's life fades...[/color]" % [organ_name, defender.name])

	if res["downed_occurred"]:
		add_log("[color=orange]  %s collapses from the agonizing pain![/color]" % defender.name)
	
	if res["prone_occurred"]:
		add_log("[color=orange]  %s is knocked violently to the ground![/color]" % defender.name)

	if (defender.status["is_dead"] or defender.status["is_downed"]) and unit_lookup.has(defender.pos):
		# Non-lethal intercept for tournaments
		if is_tournament and defender.status["is_dead"]:
			defender.status["is_dead"] = false
			defender.status["is_downed"] = true
			add_log("[color=yellow]The fight is stopped! %s has been knocked out.[/color]" % defender.name)

		unregister_unit(defender)
		if defender == player_unit:
			if defender.status["is_dead"]: add_log("[color=red][b]YOU HAVE DIED.[/b][/color]")
			else: add_log("[color=orange][b]YOU HAVE BEEN KNOCKED UNCONSCIOUS.[/b][/color]")
			add_log("[color=cyan]Tactical Mode: You can no longer move, but you can still issue orders.[/color]")
	
	for msg in GameData.check_functional_integrity(defender):
		add_log("  " + msg)
	
	return res


func resolve_aoe_damage(attacker, pos: Vector2i, radius: int, engine_data: Dictionary):
	add_log("[color=orange]  The %s impact creates a massive shockwave![/color]" % engine_data["name"])
	
	var victims = []
	# Check a box around the impact
	for dy in range(-radius, radius + 1):
		for dx in range(-radius, radius + 1):
			var target_pos = pos + Vector2i(dx, dy)
			if unit_lookup.has(target_pos):
				var victim = unit_lookup[target_pos]
				if victim.hp > 0 and not victim in victims:
					victims.append(victim)
	
	for victim in victims:
		# AOE damage is slightly lower than direct hit but hits everyone
		# Find distance to closest point of victim
		var dist = 9999.0
		for offset in victim.footprint:
			# Vector2i has distance_to - no need to convert to Vector2
			dist = min(dist, pos.distance_to(victim.pos + offset))
			
		var fallout = 1.0 - (dist / (radius + 1.0))
		if fallout > 0:
			# We simulate a "blunt" impact for AOE
			resolve_complex_damage(attacker, victim, "torso", 0)

func _find_unit_along_line(start_pos: Vector2, dir: Vector2, max_dist: float, exclude: Array) -> GDUnit:
	for i in range(1, int(max_dist)):
		var check_pos = Vector2i(start_pos + (dir * i))
		if unit_lookup.has(check_pos):
			var u = unit_lookup[check_pos]
			if u.hp > 0 and not u in exclude:
				return u
	return null


func is_in_bounds(pos):
	return pos.x >= 0 and pos.x < MAP_W and pos.y >= 0 and pos.y < MAP_H

func _find_spawn_pos(target: Vector2i) -> Vector2i:
	if not is_in_bounds(target): return target
	
	# Spiral search for nearest valid tile
	var tile = grid[target.y][target.x]
	if tile not in ["^", "~"] and not unit_lookup.has(target):
		return target
		
	for radius in range(1, 10):
		for dx in range(-radius, radius + 1):
			for dy in range(-radius, radius + 1):
				if abs(dx) != radius and abs(dy) != radius: continue
				var p = target + Vector2i(dx, dy)
				if is_in_bounds(p):
					tile = grid[p.y][p.x]
					if tile not in ["^", "~"] and not unit_lookup.has(p):
						return p
	return target # Should not happen usually

func _create_battalion(troop_list, team, b_type, pivot_pos, uid_start):
	var b_id = battalion_uid
	battalion_uid += 1
	var b = {
		"team": team, 
		"type": b_type, 
		"pivot": Vector2(pivot_pos), 
		"is_braced": false,
		"target_pos": Vector2(pivot_pos),
		"target_id": -1,
		"order": "ADVANCE"
	}
	battalions[b_id] = b
	
	var current_uid = uid_start
	var width = 10 # 10 Rows (Vertical)
	
	for i in range(troop_list.size()):
		var rank = i / width # Columns (Horizontal depth)
		var file = i % width # Rows (Vertical index)
		
		# Close formation
		var spacing = 1
		if b_type == "siege_engine":
			spacing = 6 # Reduced from 6 for better cohesion
		
		var dx = (-rank if team == "player" else rank) * spacing
		var dy = (file - (width / 2)) * spacing 
		
		var offset = Vector2i(dx, dy)
		var pos = _find_spawn_pos(Vector2i(b.pivot) + offset)
		
		var u = create_unit(current_uid, troop_list[i], team, pos, b_id, offset)
		units.append(u)
		register_unit(u)
		current_uid += 1
		
		# Auto-spawn crew for siege engines if not in siege mode
		if u.is_siege_engine:
			var req = u.engine_stats.get("crew_required", 2)
			for j in range(req):
				var c_pos = _find_spawn_pos(u.pos + Vector2i(randi_range(-1, 1), randi_range(-1, 1)))
				var c_data = GameData.generate_unit("laborer", u.tier)
				var crew = create_unit(current_uid, c_data, team, c_pos, b_id)
				crew.assigned_engine_id = u.id
				crew.symbol = "e" # Engine Crew
				u.crew_ids.append(crew.id)
				units.append(crew)
				register_unit(crew)
				current_uid += 1
	return current_uid

func spawn_siege_units():
	var uid = 0
	var center_y = MAP_H / 2
	
	# 1. Distribute Attackers (Player)
	var roster = GameState.player.roster
	var att_inf = []
	var att_arc = []
	var att_siege = []
	var att_cav = []
	
	for troop in roster:
		if troop.type == "siege_engine": att_siege.append(troop)
		elif troop.type == "archer": att_arc.append(troop)
		elif troop.type == "cavalry": att_cav.append(troop)
		else: att_inf.append(troop)
		
	# Attackers spawn on the far left (assuming city is central)
	uid = _create_battalion(att_siege, "player", "siege_engine", Vector2i(15, center_y), uid)
	uid = _create_battalion(att_inf, "player", "infantry", Vector2i(10, center_y), uid)
	uid = _create_battalion(att_arc, "player", "archer", Vector2i(5, center_y), uid)
	uid = _create_battalion(att_cav, "player", "cavalry", Vector2i(5, center_y - 20), uid)
	
	# Player Commander
	var p_cmd_pos = _find_spawn_pos(Vector2i(12, center_y))
	player_unit = create_unit(uid, GameState.player.commander, "player", p_cmd_pos)
	units.append(player_unit)
	register_unit(player_unit)
	uid += 1

	# 2. Distribute Defenders (Enemy)
	var e_roster = enemy_ref.roster
	var def_inf = []
	var def_arc = []
	
	for troop in e_roster:
		if troop.type == "archer": def_arc.append(troop)
		else: def_inf.append(troop)
	
	# Find fortification positions
	var towers = siege_data.get("towers", [])
	var walls = siege_data.get("wall_segments", [])
	var gates = siege_data.get("gates", [])
	var keep_pos = siege_data.get("keep_pos", Vector2i(MAP_W - 10, center_y))
	
	if towers.is_empty():
		for wy in range(MAP_H):
			for wx in range(MAP_W):
				var tile = grid[wy][wx]
				if tile == "T": towers.append(Vector2i(wx, wy))
				elif tile == "#": walls.append(Vector2i(wx, wy))
				elif tile == "G": gates.append(Vector2i(wx, wy))
				elif tile == "K": keep_pos = Vector2i(wx, wy)
	
	# Place Archers on Towers
	for t_pos in towers:
		if def_arc.is_empty(): break
		var u = create_unit(uid, def_arc.pop_back(), "enemy", t_pos)
		units.append(u)
		register_unit(u)
		uid += 1
	
	# Place remaining Archers on Walls (spread out)
	var wall_step = 3
	for i in range(0, walls.size(), wall_step):
		if def_arc.is_empty(): break
		var u = create_unit(uid, def_arc.pop_back(), "enemy", walls[i])
		units.append(u)
		register_unit(u)
		uid += 1
		
	# Place Infantry at Gates
	for g_pos in gates:
		if def_inf.is_empty(): break
		var spawn_p = _find_spawn_pos(g_pos + Vector2i(1, 0)) # Inside gate
		var u = create_unit(uid, def_inf.pop_back(), "enemy", spawn_p)
		units.append(u)
		register_unit(u)
		uid += 1
		
	# Place Enemy Leader and rest of Infantry at Keep
	var leader = enemy_ref.get("commander", e_roster[0])
	var u_leader = create_unit(uid, leader, "enemy", keep_pos)
	units.append(u_leader)
	register_unit(u_leader)
	uid += 1
	
	while not def_inf.is_empty():
		var p = _find_spawn_pos(keep_pos + Vector2i(randi_range(-2, 2), randi_range(-2, 2)))
		var u = create_unit(uid, def_inf.pop_back(), "enemy", p)
		units.append(u)
		register_unit(u)
		uid += 1

func spawn_units():
	units = []
	unit_lookup.clear()
	spatial_grid.clear()
	battalions.clear()
	battalion_uid = 0
	
	if is_siege and siege_data:
		spawn_siege_units()
		return

	var uid = 0
	var center_y = int(MAP_H / 2.0)
	var center_x = int(MAP_W / 2.0)
	
	# Determine distance between armies based on battle type
	var army_dist = 40 # Default (Closer for better ASCII visibility)
	if is_tournament: army_dist = 20
	elif enemy_ref and "roster" in enemy_ref and enemy_ref.roster.size() > 40: army_dist = 60
	
	var p_start_x = center_x - (army_dist / 2)
	var e_start_x = center_x + (army_dist / 2)

	if is_tournament:
		# Tournament: One-on-one or small team (Non-lethal)
		var p_pos = Vector2i(p_start_x, center_y)
		player_unit = create_unit(0, GameState.player.commander, "player", p_pos, -1, Vector2i.ZERO)
		player_unit.name = "You"
		units.append(player_unit)
		register_unit(player_unit)
		uid = 1
		
		var e_pos_base = Vector2i(e_start_x, center_y)
		if enemy_ref is GDNPC:
			var u = create_unit(uid, enemy_ref.commander_data, "enemy", e_pos_base, -1, Vector2i.ZERO)
			units.append(u)
			register_unit(u)
		elif enemy_ref is Array:
			for i in range(enemy_ref.size()):
				var npc_id = enemy_ref[i]
				var npc = GameState.find_npc(npc_id)
				if npc:
					var p = e_pos_base + Vector2i(0, (i - enemy_ref.size()/2)*4)
					var u = create_unit(uid + i, npc.commander_data, "enemy", p, -1, Vector2i.ZERO)
					units.append(u)
					register_unit(u)
		return

	# Ally/Reinforcement support (Join Battle)
	if allies_ref and "roster" in allies_ref:
		var a_inf = []
		var a_arc = []
		var a_cav = []
		for troop in allies_ref.roster:
			var effective_type = troop.get("type", "infantry")
			if effective_type in ["recruit", "laborer"]:
				effective_type = "infantry"
				var wpn = troop.get("equipment", {}).get("main_hand")
				if wpn:
					var w_id = wpn.get("type_key", wpn.get("id", ""))
					if w_id in ["shortbow", "longbow", "crossbow"]: effective_type = "archer"
					elif w_id in ["lance"]: effective_type = "cavalry"
			if effective_type == "archer": a_arc.append(troop)
			elif effective_type == "cavalry": a_cav.append(troop)
			else: a_inf.append(troop)
		
		uid = _create_battalion(a_inf, "player", "infantry", Vector2i(40, center_y), uid)
		uid = _create_battalion(a_arc, "player", "archer", Vector2i(50, center_y), uid)
		if a_cav.size() > 0:
			var a_half = a_cav.size() / 2
			uid = _create_battalion(a_cav.slice(0, a_half), "player", "cavalry", Vector2i(45, center_y - 20), uid)
			uid = _create_battalion(a_cav.slice(a_half), "player", "cavalry", Vector2i(45, center_y + 20), uid)

	# 1. Sort Roster by Type
	var roster = GameState.player.roster
	var infantry = []
	var archers = []
	var cavalry = []
	var siege = []
	
	for troop_data in roster:
		var effective_type = troop_data["type"]
		if effective_type == "siege_engine":
			siege.append(troop_data)
			continue
			
		if effective_type in ["recruit", "laborer"]:
			effective_type = "infantry"
			var wpn = troop_data["equipment"]["main_hand"]
			if wpn:
				var id = wpn.get("type_key", wpn.get("id", ""))
				if id in ["shortbow", "longbow", "crossbow"]:
					effective_type = "archer"
				elif id in ["lance"]:
					effective_type = "cavalry"
		
		if effective_type == "archer": archers.append(troop_data)
		elif effective_type == "cavalry": cavalry.append(troop_data)
		else: infantry.append(troop_data)

	# 2. Spawn Player Commander (Independent Hero for now)
	var cmd_data = GameState.player.commander
	var p_cmd_pos = _find_spawn_pos(Vector2i(p_start_x + 5, center_y))
	player_unit = create_unit(uid, cmd_data, "player", p_cmd_pos)
	units.append(player_unit)
	register_unit(player_unit)
	uid += 1
	
	# 3. Spawn Player Formations (Split into tactical 3x10 blocks)
	var BATTALION_SIZE = 30
	var player_sets = [
		{"list": infantry, "type": "infantry", "x_off": 0, "y_spacing": 12},
		{"list": archers, "type": "archer", "x_off": -6, "y_spacing": 12},
		{"list": siege, "type": "siege_engine", "x_off": -12, "y_spacing": 15}
	]
	
	for set in player_sets:
		var list = set["list"]
		var count = list.size()
		var num_batches = int(ceil(count / float(BATTALION_SIZE)))
		for b_idx in range(num_batches):
			var start_idx = b_idx * BATTALION_SIZE
			var end_idx = min(start_idx + BATTALION_SIZE, count)
			var sub_list = list.slice(start_idx, end_idx)
			
			# Stagger blocks vertically
			var v_off = (b_idx - (num_batches-1.0)/2.0) * set["y_spacing"]
			var pivot = Vector2i(p_start_x + set["x_off"], center_y + int(v_off))
			uid = _create_battalion(sub_list, "player", set["type"], pivot, uid)
	
	if cavalry.size() > 0:
		var half = int(cavalry.size() / 2)
		uid = _create_battalion(cavalry.slice(0, half), "player", "cavalry", Vector2i(p_start_x, center_y - 30), uid)
		uid = _create_battalion(cavalry.slice(half), "player", "cavalry", Vector2i(p_start_x, center_y + 30), uid)

	# 4. Spawn Enemy Formations
	if not enemy_ref:
		return

	var e_roster = []
	if "roster" in enemy_ref:
		e_roster = enemy_ref.roster
	
	if not e_roster.is_empty():
		var e_inf = []
		var e_arc = []
		var e_cav = []
		var e_siege = []
		
		for troop in e_roster:
			var effective_type = troop["type"]
			if effective_type == "siege_engine":
				e_siege.append(troop)
				continue
				
			if effective_type in ["recruit", "laborer"]:
				effective_type = "infantry"
				var wpn = troop["equipment"]["main_hand"]
				if wpn:
					var w_id = wpn.get("type_key", wpn.get("id", ""))
					if w_id in ["shortbow", "longbow", "crossbow"]:
						effective_type = "archer"
					elif w_id in ["lance"]:
						effective_type = "cavalry"
			
			if effective_type == "archer": e_arc.append(troop)
			elif effective_type == "cavalry": e_cav.append(troop)
			else: e_inf.append(troop)
		
		# Enemy Leader
		var leader_pos = _find_spawn_pos(Vector2i(e_start_x - 3, center_y))
		var e_leader = create_unit(uid, enemy_ref, "enemy", leader_pos)
		units.append(e_leader)
		register_unit(e_leader)
		uid += 1
		
		# 4. Spawn Enemy Formations (Split into tactical 3x10 blocks)
		var enemy_sets = [
			{"list": e_inf, "type": "infantry", "x_off": 0, "y_spacing": 12},
			{"list": e_arc, "type": "archer", "x_off": 6, "y_spacing": 12},
			{"list": e_siege, "type": "siege_engine", "x_off": 12, "y_spacing": 15}
		]
		
		for set in enemy_sets:
			var list = set["list"]
			var count = list.size()
			var num_batches = int(ceil(count / float(BATTALION_SIZE)))
			for b_idx in range(num_batches):
				var start_idx = b_idx * BATTALION_SIZE
				var end_idx = min(start_idx + BATTALION_SIZE, count)
				var sub_list = list.slice(start_idx, end_idx)
				
				var v_off = (b_idx - (num_batches-1.0)/2.0) * set["y_spacing"]
				var pivot = Vector2i(e_start_x + set["x_off"], center_y + int(v_off))
				uid = _create_battalion(sub_list, "enemy", set["type"], pivot, uid)
		
		if e_cav.size() > 0:
			var e_half = int(e_cav.size() / 2)
			uid = _create_battalion(e_cav.slice(0, e_half), "enemy", "cavalry", Vector2i(e_start_x, center_y - 30), uid)
			uid = _create_battalion(e_cav.slice(e_half), "enemy", "cavalry", Vector2i(e_start_x, center_y + 30), uid)
	else:
		# FALLBACK: Generic Enemies
		var e_type = enemy_ref.get("type", "") if enemy_ref is Dictionary else enemy_ref.type
		var is_caravan = e_type == "caravan"
		var e_strength = enemy_ref.get("strength", 10.0) if enemy_ref is Dictionary else enemy_ref.strength
		var e_count = int(max(3, e_strength / 2.0))
		var e_troops = []
		for i in range(e_count):
			var u_data = GameData.generate_recruit(GameState.rng, 1)
			if is_caravan and i == 0:
				u_data = GameData.generate_laborer(GameState.rng)
				u_data.name = "Merchant"
				u_data.type = "merchant"
			e_troops.append(u_data)
		
		uid = _create_battalion(e_troops, "enemy", "infantry", Vector2i(e_start_x, center_y), uid)

	GameState.emit_signal("map_updated")

func create_unit(id, data, team, pos, f_id = -1, f_offset = Vector2i.ZERO):
	var u_name = "Unit"
	var u_type = "infantry"
	var u_xp = 0
	var u_body = {}
	var u_equipment = {}
	var u_attributes = {}
	var u_skills = {}
	var u_is_hero = false
	var u_engine_type = ""
	
	if data is GDUnit:
		u_name = data.name
		u_type = data.type
		u_xp = data.xp
		u_body = data.body
		u_equipment = data.equipment
		u_attributes = data.attributes
		u_skills = data.skills
		u_is_hero = data.is_hero
		if "engine_type" in data: u_engine_type = data.engine_type
	elif data is Dictionary:
		u_name = data.get("name", "Unit")
		u_type = data.get("type", "infantry")
		u_xp = data.get("xp", 0)
		u_body = data.get("body", {})
		u_equipment = data.get("equipment", {})
		u_attributes = data.get("attributes", {})
		u_skills = data.get("skills", {})
		u_is_hero = data.get("is_hero", false)
		u_engine_type = data.get("engine_type", data.get("archetype", ""))
	elif data is RefCounted: # Handle GDArmy/GDEntity
		u_name = data.name if "name" in data else "Leader"
		u_type = data.type if "type" in data else "lord"
		u_xp = data.xp if "xp" in data else 500
		if "body" in data: u_body = data.body
		if "equipment" in data: u_equipment = data.equipment
		if "attributes" in data: u_attributes = data.attributes
		if "skills" in data: u_skills = data.skills
		if "engine_type" in data: u_engine_type = data.engine_type
	
	var type = u_type
	var xp = u_xp
	var spd = 0.6
	var sym = 'i'
	
	# Initialize Body Parts
	var body = {}
	if u_body and not u_body.is_empty():
		# Use existing body if available (prevents exponential scaling)
		body = u_body.duplicate(true)
	else:
		# Generate new body based on type/xp
		var hp_scale = 1.0
		var level = int(xp / 100)
		hp_scale += level * 0.1
		
		if type == "commander": hp_scale = 1.5
		elif type == "merchant": hp_scale = 0.4
		elif type == "laborer": hp_scale = 0.7
		elif type.ends_with("_engine"): hp_scale = 5.0 # Siege engines are very durable
		
		body = GameData.get_default_body(hp_scale)
	
	match type:
		"hero", "commander", "lord": spd=0.1; sym='@'
		"merchant": spd=0.7; sym='M'
		"infantry", "recruit", "laborer": 
			spd=0.6; 
			# For recruits/laborers, check weapon to refine symbol
			var is_archer = false
			if type in ["recruit", "laborer"] and not u_equipment.is_empty() and u_equipment.get("main_hand"):
				var main_hand = u_equipment["main_hand"]
				var w_id = main_hand.get("type_key", main_hand.get("id", ""))
				if w_id in ["shortbow", "longbow", "crossbow"]:
					is_archer = true
			
			if is_archer:
				spd=0.8; 
				var level = int(xp / 100)
				if level >= 4: sym='A' # Sniper / Master Archer
				elif level >= 2: sym='B' # Bowyer / Archer
				else: sym='a' # Young Archer
			else:
				var level = int(xp / 100)
				if level >= 5: sym='W' # Warleader
				elif level >= 3: sym='V' # Veteran
				elif level >= 1: sym='I' # Infantry
				else: sym='r' # Recruit
		"archer": 
			spd=0.8; 
			var level = int(xp / 100)
			if level >= 4: sym='A'
			elif level >= 2: sym='B'
			else: sym='a'
		"cavalry": 
			spd=0.3; 
			var level = int(xp / 100)
			if level >= 3: sym='C'
			else: sym='c'
		"siege_engine":
			spd=1.5; 
			sym='X'
	
	var total_hp = 0
	var body_objs = {}
	for p_key in body:
		var p_data = body[p_key]
		var part = {
			"name": p_data.get("name", p_key),
			"parent": p_data.get("parent", ""),
			"internal": p_data.get("internal", false),
			"bleed_rate": p_data.get("bleed_rate", 0.0),
			"tissues": []
		}
		for t_data in p_data["tissues"]:
			var tissue = {
				"type": t_data.get("type", "flesh"),
				"hp": t_data.get("hp", 10),
				"hp_max": t_data.get("hp_max", 10),
				"thick": t_data.get("thick", 5),
				"structural": t_data.get("structural", false),
				"name": t_data.get("name", "")
			}
			part["tissues"].append(tissue)
			total_hp += tissue["hp"]
		body_objs[p_key] = part
	
	# Assign Equipment (Layered System)
	var equipment = {
		"main_hand": null,
		"off_hand": null,
		"head": {"under": null, "over": null, "armor": null, "cover": null},
		"torso": {"under": null, "over": null, "armor": null, "cover": null},
		"l_arm": {"under": null, "over": null, "armor": null, "cover": null},
		"r_arm": {"under": null, "over": null, "armor": null, "cover": null},
		"l_hand": {"under": null, "over": null, "armor": null, "cover": null},
		"r_hand": {"under": null, "over": null, "armor": null, "cover": null},
		"l_leg": {"under": null, "over": null, "armor": null, "cover": null},
		"r_leg": {"under": null, "over": null, "armor": null, "cover": null},
		"l_foot": {"under": null, "over": null, "armor": null, "cover": null},
		"r_foot": {"under": null, "over": null, "armor": null, "cover": null},
		"ammo": null
	}
	
	if not u_equipment.is_empty():
		var old_eq = u_equipment
		if old_eq.get("main_hand"): equipment["main_hand"] = old_eq["main_hand"]
		if old_eq.get("off_hand"): equipment["off_hand"] = old_eq["off_hand"]
		if old_eq.get("ammo"): equipment["ammo"] = old_eq["ammo"]
		
		for part in ["head", "torso", "l_arm", "r_arm", "l_hand", "r_hand", "l_leg", "r_leg", "l_foot", "r_foot"]:
			if old_eq.has(part):
				var item = old_eq[part]
				if item:
					if typeof(item) == TYPE_DICTIONARY:
						if item.has("layer"):
							equipment[part][item["layer"]] = item
						elif item.has("under") or item.has("over") or item.has("armor") or item.has("cover"):
							equipment[part]["under"] = item.get("under")
							equipment[part]["over"] = item.get("over")
							equipment[part]["armor"] = item.get("armor")
							equipment[part]["cover"] = item.get("cover")
					else:
						equipment[part]["armor"] = item
	
	# Default Enemy Gear (Layered) - Fallback for generic enemies or naked recruits
	if team == "enemy" and equipment["main_hand"] == null:
		match type:
			"infantry", "recruit", "laborer", "lord":
				if type == "laborer":
					equipment["main_hand"] = GameState.create_item("club", "wood")
					var shirt = GameState.create_item("shirt", "linen")
					for p in shirt["coverage"]: equipment[p]["under"] = shirt
				else:
					equipment["main_hand"] = GameState.create_item("shortsword", "iron", "rusty")
					equipment["off_hand"] = GameState.create_item("heater_shield", "wood")
					var gambeson = GameState.create_item("gambeson", "wool")
					for p in gambeson["coverage"]: equipment[p]["over"] = gambeson
					if type != "recruit":
						var cuirass = GameState.create_item("cuirass", "iron")
						for p in cuirass["coverage"]: equipment[p]["armor"] = cuirass
			"archer":
				equipment["main_hand"] = GameState.create_item("shortbow", "wood")
				equipment["ammo"] = GameState.create_item("arrows", "iron")
				var tunic = GameState.create_item("tunic", "linen")
				for p in tunic["coverage"]: equipment[p]["under"] = tunic
			"crossbowman":
				equipment["main_hand"] = GameState.create_item("crossbow", "wood")
				equipment["ammo"] = GameState.create_item("bolts", "iron")
				var gambeson = GameState.create_item("gambeson", "wool")
				for p in gambeson["coverage"]: equipment[p]["under"] = gambeson
			"cavalry":
				equipment["main_hand"] = GameState.create_item("spear", "iron")
				var hauberk = GameState.create_item("hauberk", "iron")
				for p in hauberk["coverage"]: equipment[p]["over"] = hauberk
	
	var u = GDUnit.new(u_name)
	u.id = id
	u.pos = pos
	u.team = team
	u.faction = team
	u.type = type
	u.symbol = sym
	
	if type == "siege_engine":
		u.is_siege_engine = true
		u.engine_type = u_engine_type if u_engine_type != "" else "catapult"
		u.engine_stats = GameData.SIEGE_ENGINES.get(u.engine_type, {}).duplicate()
		u.symbol = u.engine_stats.get("symbol", "R")
		u.footprint = u.engine_stats.get("footprint", [Vector2i(0,0)]).duplicate()
	else:
		u.footprint = [Vector2i(0,0)]
	
	u.formation_id = f_id
	u.formation_offset = f_offset
	u.equipment = equipment # Use the properly formatted equipment dictionary we just built
	u.is_hero = u_is_hero
	
	if not u_attributes.is_empty():
		u.attributes = u_attributes.duplicate(true)
	if not u_skills.is_empty():
		u.skills = u_skills.duplicate(true)
		
	u.base_speed = GameData.calculate_unit_speed(u)
	u.speed = u.base_speed
	u.action_timer = u.speed
	u.hp = total_hp
	u.hp_max = total_hp
	u.blood_max = 500.0
	u.blood_current = 500.0
	u.bleed_rate = 0.0
	for p_key in body_objs:
		u.bleed_rate += body_objs[p_key].get("bleed_rate", 0.0)
	u.body = body_objs
	u.status = {
		"crippled_legs": false, 
		"crippled_arms": false,
		"is_downed": false,
		"is_dead": false,
		"is_prone": false,
		"knockdown_timer": 0
	}
	u.data_ref = data
	return u


func generate_map():
	# Lazy generation via get_tile() will handle it, but we pre-spawn the starting area
	if is_siege and siege_data:
		grid = siege_data.grid.duplicate(true)
		return
	
	if grid.size() != MAP_H:
		initialize_grid()
	
	var center = Vector2i(MAP_W/2, MAP_H/2)
	var start_cx = (center.x - 100) / CHUNK_SIZE
	var end_cx = (center.x + 100) / CHUNK_SIZE
	var start_cy = (center.y - 100) / CHUNK_SIZE
	var end_cy = (center.y + 100) / CHUNK_SIZE
	
	for cy in range(start_cy, end_cy + 1):
		for cx in range(start_cx, end_cx + 1):
			_generate_chunk(Vector2i(cx, cy))

func _generate_chunk(chunk_pos: Vector2i):
	if chunk_pos in generated_chunks: return
	if chunk_pos.x < 0 or chunk_pos.x >= (MAP_W/CHUNK_SIZE) or chunk_pos.y < 0 or chunk_pos.y >= (MAP_H/CHUNK_SIZE): return
	
	generated_chunks[chunk_pos] = true
	var gs = GameState
	var p_pos = gs.player.pos
	var l_off = gs.local_offset
	
	var world_tile = gs.grid[p_pos.y][p_pos.x] # Default world tile
	var local_rng = RandomNumberGenerator.new()
	var noise = FastNoiseLite.new()
	noise.seed = (p_pos.x * 73856093) ^ (p_pos.y * 19349663)
	noise.frequency = 0.08

	var neighborhood = {} 
	var default_geo = {"elevation": 0.5, "temp": 0.5, "rain": 0.5}
	for dy in range(-1, 2):
		for dx in range(-1, 2):
			var w_pos_n = p_pos + Vector2i(dx, dy)
			if w_pos_n.x < 0 or w_pos_n.x >= gs.width or w_pos_n.y < 0 or w_pos_n.y >= gs.height:
				w_pos_n = p_pos
			var geo = gs.geology.get(w_pos_n, default_geo)
			neighborhood[Vector2i(dx, dy)] = geo

	var center_wx = l_off.x / gs.WORLD_TILE_SIZE - 0.5
	var center_wy = l_off.y / gs.WORLD_TILE_SIZE - 0.5
	
	var is_river = (world_tile == "~" or world_tile == "≈")
	var has_road = (world_tile == "=" or world_tile == "/" or world_tile == "\\")
	
	# IMPROVEMENT: Road/River Continuity Neighbors
	var neighbors_road = [false, false, false, false] # N, E, S, W
	var neighbors_river = [false, false, false, false]
	for idx in range(4):
		var dir = [Vector2i.UP, Vector2i.RIGHT, Vector2i.DOWN, Vector2i.LEFT][idx]
		var n_wpos = p_pos + dir
		if n_wpos.x >= 0 and n_wpos.x < gs.width and n_wpos.y >= 0 and n_wpos.y < gs.height:
			var n_char = gs.grid[n_wpos.y][n_wpos.x]
			if n_char in ["=", "/", "\\"]: neighbors_road[idx] = true
			if n_char in ["~", "≈"]: neighbors_river[idx] = true

	for ly in range(chunk_pos.y * CHUNK_SIZE, (chunk_pos.y + 1) * CHUNK_SIZE):
		for lx in range(chunk_pos.x * CHUNK_SIZE, (chunk_pos.x + 1) * CHUNK_SIZE):
			if lx < 0 or lx >= MAP_W or ly < 0 or ly >= MAP_H: continue
			
			var m_off_x = (lx - (MAP_W/2.0)) * gs.METERS_PER_LOCAL_TILE
			var m_off_y = (ly - (MAP_H/2.0)) * gs.METERS_PER_LOCAL_TILE
			
			var wv_x = center_wx + (m_off_x / gs.WORLD_TILE_SIZE)
			var wv_y = center_wy + (m_off_y / gs.WORLD_TILE_SIZE)
			
			var interp_e = _interp_neighborhood(neighborhood, "elevation", wv_x, wv_y)
			var interp_t = _interp_neighborhood(neighborhood, "temp", wv_x, wv_y)
			var interp_r = _interp_neighborhood(neighborhood, "rain", wv_x, wv_y)
			
			# Simulator Flat Map Override
			var is_sim = false
			if enemy_ref is Dictionary and enemy_ref.get("name") == "Simulator Rivals":
				is_sim = true
			elif enemy_ref is GDArmy and enemy_ref.name == "Simulator Rivals":
				is_sim = true
				
			if is_sim:
				interp_e = 0.5
				interp_r = 0.4
				interp_t = 0.5
			
			var abs_x = p_pos.x * gs.WORLD_TILE_SIZE + l_off.x + m_off_x
			var abs_y = p_pos.y * gs.WORLD_TILE_SIZE + l_off.y + m_off_y
			var detail = noise.get_noise_2d(abs_x, abs_y) * 0.05
			
			if is_sim: detail = 0.0 # Perfectly flat for sim
			
			var final_e = interp_e + detail
			
			var tile = "."
			var veg_roll = local_rng.randf()
			
			if final_e < 0.35: tile = "~"
			elif final_e > 0.65: tile = "^"
			elif final_e > 0.50: tile = "o"
			else:
				if interp_r > 0.7 and interp_t > 0.6 and veg_roll > 0.7: tile = "&" 
				elif interp_r > 0.5 and veg_roll > 0.8: tile = "T" 
				elif interp_r < 0.2 and interp_t > 0.7: tile = "\"" 
				elif interp_t < 0.3: tile = "*" 
			
			var biome = "plains"
			if interp_r > 0.7 and interp_t > 0.6: biome = "jungle"
			elif interp_r > 0.5: biome = "forest"
			elif interp_r < 0.2 and interp_t > 0.7: biome = "desert"
			elif interp_t < 0.3: biome = "plains" # Mountain/Tundra can use plains table for now
			
			var l_norm = Vector2(wv_x * 2.0, wv_y * 2.0)
			
			# CONTINUITY RENDERING
			if is_river:
				var river_width = 0.3
				var in_river = false
				if l_norm.length() < river_width: in_river = true
				if neighbors_river[0] and wv_x > -river_width and wv_x < river_width and wv_y < 0: in_river = true
				if neighbors_river[1] and wv_y > -river_width and wv_y < river_width and wv_x > 0: in_river = true
				if neighbors_river[2] and wv_x > -river_width and wv_x < river_width and wv_y > 0: in_river = true
				if neighbors_river[3] and wv_y > -river_width and wv_y < river_width and wv_x < 0: in_river = true
				if in_river: tile = "~"
				
			if has_road and tile != "~":
				var road_width = 0.08
				var in_road = false
				if l_norm.length() < road_width: in_road = true
				if neighbors_road[0] and wv_x > -road_width and wv_x < road_width and wv_y < 0: in_road = true
				if neighbors_road[1] and wv_y > -road_width and wv_y < road_width and wv_x > 0: in_road = true
				if neighbors_road[2] and wv_x > -road_width and wv_x < road_width and wv_y > 0: in_road = true
				if neighbors_road[3] and wv_y > -road_width and wv_y < road_width and wv_x < 0: in_road = true
				if in_road: tile = "+"
			
			var abs_cell_x = int(abs_x / 2.0)
			var abs_cell_y = int(abs_y / 2.0)
			var cell_hash = (abs_cell_x * 73856093) ^ (abs_cell_y * 19349663)
			var roll = (abs(cell_hash) % 10000) / 10000.0
			
			if tile in [".", "o", "t", "\""]:
				var flora_list = FloraData.get_flora_for_biome(biome)
				for f in flora_list:
					if roll < f["chance"]:
						tile = f["symbol"]
						break
			
			var resource_type = gs.resources.get(p_pos, "")
			if resource_type != "" and tile in [".", "o", "^", "\""]:
				var res_roll = (abs(cell_hash ^ 999) % 1000) / 1000.0
				if res_roll < 0.01:
					tile = resource_type.substr(0, 1).to_upper()
			
			# Settlement logic 
			for ny in range(-1, 2):
				for nx in range(-1, 2):
					var s_w_pos = p_pos + Vector2i(nx, ny)
					if gs.settlements.has(s_w_pos):
						var s = gs.settlements[s_w_pos]
						var s_cx = s_w_pos.x * 1000.0 + 500.0
						var s_cy = s_w_pos.y * 1000.0 + 500.0
						var sdx = abs_x - s_cx
						var sdy = abs_y - s_cy
						var s_dist_c = Vector2(sdx, sdy).length()
						var settlement_radius = clamp(50.0 + (s.population / 1000.0) * 40.0, 50.0, 480.0)
						if s_dist_c < settlement_radius + 20.0:
							var is_major = s.type in ["town", "city", "metropolis", "castle"]
							var wall_lvl = s.buildings.get("wall", 0) 
							if is_major and wall_lvl == 0 and s.tier >= 2: wall_lvl = 1
							if wall_lvl > 0:
								var wall_thick = 2.0 + (wall_lvl * 1.5)
								if (abs(abs(sdx) - settlement_radius) < wall_thick or abs(abs(sdy) - settlement_radius) < wall_thick) and s_dist_c < settlement_radius + 5:
									if not (abs(sdx) < 8.0 or abs(sdy) < 8.0):
										tile = "#" if wall_lvl < 3 else "H"
							var street_w = 2.5 + (s.tier * 0.5) 
							if abs(sdx) < street_w or abs(sdy) < street_w:
								if s_dist_c < settlement_radius + 10.0: tile = "+"
							if s_dist_c < 12.0 + (s.tier * 4.0):
								if s.buildings.get("keep", 0) > 0 or is_major: tile = "K"
								else: tile = "o"
							if tile == "." and s_dist_c < settlement_radius:
								var b_h = (int(abs_x/20.0) * 73856093) ^ (int(abs_y/20.0) * 19349663)
								var b_lx = fmod(abs_x, 20.0)
								var b_ly = fmod(abs_y, 20.0)
								var d_f = 1.0 - (s_dist_c / settlement_radius)
								var occ = 10 + (d_f * 40.0)
								if (abs(b_h) % 100) < occ:
									if b_lx > 6.0 and b_lx < 14.0 and b_ly > 6.0 and b_ly < 14.0:
										tile = "B" if is_major else "#" 
			
			grid[ly][lx] = tile
			
			# Cache structural targets (Optimization 5)
			if tile in ["#", "H", "G", "K", "B"]:
				if not structural_cache.has(tile):
					structural_cache[tile] = []
				structural_cache[tile].append(Vector2i(lx, ly))

	# 6. Populate Fauna (Simplified Pass)
	var biome_at_center = "plains"
	var fauna_list = []
	var fauna_table = FaunaData.get_fauna_table()
	if fauna_table.has(biome_at_center): fauna_list = fauna_table.get(biome_at_center, [])
	
	if not fauna_list.is_empty():
		var s_rng = RandomNumberGenerator.new()
		s_rng.seed = (chunk_pos.x * 73856093) ^ (chunk_pos.y * 19349663)
		for f in fauna_list:
			if s_rng.randf() < f["chance"] * 0.1:
				var num = s_rng.randi_range(f["herd_range"][0], f["herd_range"][1])
				for i in range(num):
					var gx = chunk_pos.x * CHUNK_SIZE + s_rng.randi_range(0, CHUNK_SIZE-1)
					var gy = chunk_pos.y * CHUNK_SIZE + s_rng.randi_range(0, CHUNK_SIZE-1)
					if gx >= 0 and gx < MAP_W and gy >= 0 and gy < MAP_H:
						if grid[gy][gx] in [".", "o", "t", "\"", "*"]:
							grid[gy][gx] = f["symbol"]

func _interp_neighborhood(nb, key, wx, wy):
	# Determine which 4 tiles to interp between
	var x0 = -1 if wx < 0 else 0
	var x1 = 0 if wx < 0 else 1
	var y0 = -1 if wy < 0 else 0
	var y1 = 0 if wy < 0 else 1
	
	# Local weights 0 to 1 between the two tiles
	var tx = wx + 1.0 if wx < 0 else wx
	var ty = wy + 1.0 if wy < 0 else wy
	
	var v00 = nb[Vector2i(x0, y0)].get(key, 0.5)
	var v10 = nb[Vector2i(x1, y0)].get(key, 0.5)
	var v01 = nb[Vector2i(x0, y1)].get(key, 0.5)
	var v11 = nb[Vector2i(x1, y1)].get(key, 0.5)
	
	var top = lerp(v00, v10, tx)
	var bot = lerp(v01, v11, tx)
	return lerp(top, bot, ty)

func _dist_to_segment(p: Vector2, a: Vector2, b: Vector2) -> float:
	var pa = p - a
	var ba = b - a
	var h = clamp(pa.dot(ba) / ba.dot(ba), 0.0, 1.0)
	return (pa - ba * h).length()

func get_unit_at(p):
	if unit_lookup.has(p):
		var u = unit_lookup[p]
		if u.hp > 0 and not u.status["is_downed"] and not u.status["is_dead"]:
			return u
	return null

func end_battle(win):
	active = false
	
	# Clear ongoing battle if we joined one
	for i in range(GameState.ongoing_battles.size() - 1, -1, -1):
		var b = GameState.ongoing_battles[i]
		if b.attacker == enemy_ref or b.defender == enemy_ref or b.attacker == allies_ref or b.defender == allies_ref:
			GameState.ongoing_battles.remove_at(i)
			if b.attacker: b.attacker.is_in_battle = false
			if b.defender: b.defender.is_in_battle = false
	
	if win:
		if player_unit.status["is_dead"] or player_unit.status["is_downed"]:
			GameState.add_log("Battle Won! Your troops secured the field and recovered your body.")
		else:
			GameState.add_log("Battle Won! Glory to the Commander!")
		
		# City Generator / Peace Mode Support: enemy_ref might be null
		var e_type = ""
		if enemy_ref:
			e_type = enemy_ref.get("type", "") if enemy_ref is Dictionary else enemy_ref.type
			
		if e_type == "caravan":
			var loot_crowns = enemy_ref.get("crowns", 0) if enemy_ref is Dictionary else enemy_ref.crowns
			GameState.player.crowns += loot_crowns
			GameState.add_log("You plundered %d Crowns from the caravan!" % loot_crowns)
			
			# Plunder inventory
			var inv = enemy_ref.get("inventory", {}) if enemy_ref is Dictionary else enemy_ref.inventory
			for res in inv:
				var amt = inv[res]
				if amt > 0:
					GameState.player.inventory[res] = GameState.player.inventory.get(res, 0) + amt
					GameState.add_log("Plundered %d %s." % [amt, res.capitalize()])
			
			if not enemy_ref is Dictionary:
				enemy_ref.respawn_timer = 120 # Player plundered them hard, extra long recovery
				enemy_ref.pos = enemy_ref.origin
				enemy_ref.roster = []
				enemy_ref.inventory = {}
				enemy_ref.target_pos = Vector2i(-1, -1)
				enemy_ref.state = "idle"
			GameState.add_log("The surviving merchants flee back to their home settlement.")
		else:
			GameState.player.crowns += 50
			if not enemy_ref is Dictionary:
				GameState.erase_army(enemy_ref)
		
		# Looting items from dead units (100% Harvest Rule)
		var loot_count = 0
		for u in units:
			if u.team == "enemy" and u.status["is_dead"]:
				# Harvest Main Hand
				if u.equipment["main_hand"]:
					GameState.player.stash.append(u.equipment["main_hand"])
					loot_count += 1
				# Harvest Off Hand
				if u.equipment["off_hand"]:
					GameState.player.stash.append(u.equipment["off_hand"])
					loot_count += 1
				
				# Harvest Armor Layers
				for slot in ["head", "torso", "l_arm", "r_arm", "l_hand", "r_hand", "l_leg", "r_leg", "l_foot", "r_foot"]:
					var s = u.equipment.get(slot)
					if s:
						if s["under"]:
							GameState.player.stash.append(s["under"])
							loot_count += 1
						if s.get("over"):
							GameState.player.stash.append(s["over"])
							loot_count += 1
						if s["armor"]:
							GameState.player.stash.append(s["armor"])
							loot_count += 1
						if s["cover"]:
							GameState.player.stash.append(s["cover"])
							loot_count += 1
		
		if loot_count > 0:
			GameState.add_log("Scavenged %d items from the field (100%% Harvest)." % loot_count)
		
		var total_w = GameState.get_total_weight()
		var max_w = GameState.get_max_weight()
		if total_w > max_w:
			GameState.add_log("[color=orange]WARNING: Overburdened! (%d/%d kg). You must discard items in the Stash menu.[/color]" % [int(total_w), int(max_w)])
		
		# Capture Prisoners (Enemies who fled or were downed)
		var prisoners_taken = 0
		for u in units:
			if u.team == "enemy" and u.data_ref:
				# If they fled (hp == -1) or were downed
				if u.hp == -1 or u.status["is_downed"]:
					# 80% chance to capture a downed survivor, 30% for a fleer
					var cap_chance = 0.8 if u.status["is_downed"] else 0.3
					if GameState.rng.randf() < cap_chance:
						var prisoner
						if u.data_ref is Dictionary:
							prisoner = u.data_ref.duplicate(true)
						else:
							# For GDUnit/GDArmy, create a data dictionary representing that leader
							prisoner = {
								"name": u.name,
								"type": u.type,
								"xp": 100, # Default for captured leaders
								"cost": 500, # Default ransom/recruit value for leaders
								"body": u.body.duplicate(true),
								"equipment": u.equipment.duplicate(true),
								"status": u.status.duplicate(true),
								"attributes": u.data_ref.attributes.duplicate(true) if (u.data_ref and "attributes" in u.data_ref) else {},
								"skills": u.data_ref.skills.duplicate(true) if (u.data_ref and "skills" in u.data_ref) else {}
							}
						
						# Ensure prisoner has all required fields for a roster unit
						if not prisoner is GDUnit:
							if not prisoner.has("hp_max"): prisoner["hp_max"] = u.hp_max
							if not prisoner.has("hp"): prisoner["hp"] = u.hp_max
						if not prisoner.has("body"): prisoner["body"] = u.body.duplicate(true)
						if not prisoner.has("equipment"): prisoner["equipment"] = u.equipment.duplicate(true)
						if not prisoner.has("status"): prisoner["status"] = u.status.duplicate(true)
						if not prisoner.has("xp"): prisoner["xp"] = 0
						
						# Reset HP for recruitment later
						for p_key in prisoner["body"]:
							var part = prisoner["body"][p_key]
							for tissue in part["tissues"]:
								tissue["hp"] = tissue["hp_max"]
						prisoner["hp"] = GameData.get_total_hp(prisoner["body"])
						
						# Reset downed status
						prisoner["status"]["is_downed"] = false
						
						GameState.player.prisoners.append(prisoner)
						prisoners_taken += 1
		
		if prisoners_taken > 0:
			GameState.add_log("Captured %d prisoners." % prisoners_taken)
		
		# XP Gain and Health Sync for Survivors
		var p_survivors = []
		var a_survivors = []
		for u in units:
			if u.team == "player" and u.data_ref:
				var data = u.data_ref
				
				if u.status["is_dead"]:
					GameState.add_log("[color=red]%s has been killed in action.[/color]" % data.name)
					continue
				
				# Field Dressing Logic: Check for permanent injuries
				var is_fatal = false
				for p_key in u.body:
					var part = u.body[p_key]
					var is_mangled = false
					for tissue in part["tissues"]:
						if tissue["hp"] <= 0 and tissue.get("structural", false):
							is_mangled = true
							break
					
					if is_mangled:
						if p_key == "head" or p_key == "torso" or p_key == "neck":
							is_fatal = true
							break
						else:
							# Non-fatal part mangled
							if not data.status.get("mangled_" + p_key, false) and not data.status.get("severed_" + p_key, false):
								if GameState.rng.randf() < 0.3:
									data.status["severed_" + p_key] = true
									GameState.add_log("[color=red]%s's %s was severed![/color]" % [data.name, part["name"]])
								else:
									data.status["mangled_" + p_key] = true
									GameState.add_log("[color=orange]%s's %s was mangled![/color]" % [data.name, part["name"]])
				
				if is_fatal:
					GameState.add_log("[color=red]%s has died of their wounds.[/color]" % data.name)
					continue
				
				# Sync Health and Body Parts
				data.hp = u.hp
				data.status["is_downed"] = false # Recover from downed state after battle
				if data.body and not data.body.is_empty():
					for p_key in u.body:
						if data.body.has(p_key):
							# Sync tissues
							var u_tissues = u.body[p_key]["tissues"]
							var d_tissues = data.body[p_key]["tissues"]
							for i in range(min(u_tissues.size(), d_tissues.size())):
								d_tissues[i]["hp"] = u_tissues[i]["hp"]
				
				data.xp += 50
				
				# Sort back to owners
				if data != GameState.player.commander:
					if allies_ref and "roster" in allies_ref and data in allies_ref.roster:
						a_survivors.append(data)
					else:
						p_survivors.append(data)
				
				# Check Level Up
				var old_lvl = int((data.xp - 50) / 100)
				var new_lvl = int(data.xp / 100)
				
				# Update Rank Name
				var rank_name = data.name
				if u.type == "infantry":
					if new_lvl >= 5: rank_name = "Champion"
					elif new_lvl >= 3: rank_name = "Veteran"
					elif new_lvl >= 1: rank_name = "Footman"
					else: rank_name = "Recruit"
				elif u.type == "archer":
					if new_lvl >= 4: rank_name = "Sniper"
					elif new_lvl >= 2: rank_name = "Marksman"
					else: rank_name = "Bowman"
				elif u.type == "cavalry":
					if new_lvl >= 3: rank_name = "Knight"
					else: rank_name = "Squire"
				
				data.name = rank_name
				
				if new_lvl > old_lvl:
					GameState.add_log("%s leveled up to Lvl %d!" % [data.name, new_lvl])
		
		# Update Roster (Remove dead)
		GameState.player.roster = p_survivors
		if allies_ref and "roster" in allies_ref:
			allies_ref.roster = a_survivors
		
	else:
		GameState.add_log("Battle Lost! You were knocked unconscious.")
		# Sync Commander Health
		if player_unit:
			var cmd = GameState.player.commander
			# Field Dressing for Commander (He can't die, but can be mangled)
			for p_key in player_unit.body:
				var part = player_unit.body[p_key]
				var is_mangled = false
				for tissue in part["tissues"]:
					if tissue["hp"] <= 0 and tissue["hp_max"] > 0: # Simplified mangled check
						is_mangled = true
						break
				
				if is_mangled:
					if p_key != "head" and p_key != "torso" and p_key != "neck":
						if not cmd.status.get("mangled_" + p_key, false) and not cmd.status.get("severed_" + p_key, false):
							if GameState.rng.randf() < 0.3:
								cmd.status["severed_" + p_key] = true
								GameState.add_log("[color=red]Your %s was severed![/color]" % part["name"])
							else:
								cmd.status["mangled_" + p_key] = true
								GameState.add_log("[color=orange]Your %s was mangled![/color]" % part["name"])

			if player_unit.hp <= 0:
				cmd.hp = 1
				for p_k in cmd.body:
					for tissue in cmd.body[p_k]["tissues"]:
						tissue["hp"] = 1
			else:
				cmd.hp = player_unit.hp
				for p_k in player_unit.body:
					if cmd.body.has(p_k):
						for i in range(player_unit.body[p_k]["tissues"].size()):
							cmd.body[p_k]["tissues"][i]["hp"] = player_unit.body[p_k]["tissues"][i]["hp"]

		# In defeat, you lose most troops but maybe keep some who fled?
		var survivors_list: Array[GDUnit] = []
		for u in units:
			if u.team == "player" and u.hp == -1: # Fled
				if u.type != "commander":
					survivors_list.append(u)
		
		GameState.player.roster = survivors_list
	
	GameState.emit_signal("battle_ended", win)

func _get_shield_wall_bonus(u) -> float:
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
