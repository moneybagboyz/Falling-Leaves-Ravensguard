extends Node

const BattlePhysics = preload("res://src/battle/BattlePhysics.gd")
const BattleTerrain = preload("res://src/battle/BattleTerrain.gd")
const BattleCombat = preload("res://src/battle/BattleCombat.gd")
const BattleSiege = preload("res://src/battle/BattleSiege.gd")
const BattleAI = preload("res://src/battle/BattleAI.gd")
const BattleState = preload("res://src/battle/BattleState.gd")
const FaunaData = preload("res://src/data/FaunaData.gd")
const FloraData = preload("res://src/data/FloraData.gd")

const MAP_W = BattlePhysics.MAP_W
const MAP_H = BattlePhysics.MAP_H
const CHUNK_SIZE = BattlePhysics.CHUNK_SIZE
const SPATIAL_BUCKET_SIZE = BattlePhysics.SPATIAL_BUCKET_SIZE
const TICK_RATE = 0.1 # How fast the game updates (lower = faster)

# -----------------------------

# Battle Systems
var physics: BattlePhysics
var terrain: BattleTerrain
var combat: BattleCombat
var siege: BattleSiege
var ai: BattleAI
var state: BattleState

var active = false
var is_tournament = false
var is_siege = false
var siege_data = {}
var last_map_pos = Vector2i(-999, -999)

# Physics system manages these via delegation:
var grid: Array: get = _get_grid, set = _set_grid
var generated_chunks: Dictionary: get = _get_generated_chunks
var structural_cache: Dictionary: get = _get_structural_cache
var spatial_grid: Dictionary: get = _get_spatial_grid
var spatial_team_mask: Dictionary: get = _get_spatial_team_mask
var unit_lookup: Dictionary: get = _get_unit_lookup

func _get_grid() -> Array: return physics.grid if physics else []
func _set_grid(value: Array): if physics: physics.grid = value
func _get_generated_chunks() -> Dictionary: return physics.generated_chunks if physics else {}
func _get_structural_cache() -> Dictionary: return physics.structural_cache if physics else {}
func _get_spatial_grid() -> Dictionary: return physics.spatial_grid if physics else {}
func _get_spatial_team_mask() -> Dictionary: return physics.spatial_team_mask if physics else {}
func _get_unit_lookup() -> Dictionary: return physics.unit_lookup if physics else {}

func _ready():
	physics = BattlePhysics.new()
	terrain = BattleTerrain.new()
	combat = BattleCombat.new()
	siege = BattleSiege.new()
	ai = BattleAI.new()
	state = BattleState.new()

func initialize_grid():
	physics.initialize_grid()

func ensure_chunk_at(pos: Vector2i):
	physics.ensure_chunk_at(pos, _generate_chunk)

func get_tile(x: int, y: int) -> String:
	ensure_chunk_at(Vector2i(x, y))
	return physics.get_tile(x, y)

var tournament_prize = 0
var units = [] 
var battalions = {} # id -> {team, type, pivot_pos, target_pos, order}
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
	ai.current_order = "ADVANCE"
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
	elif event.keycode == KEY_G:
		# Toggle grid lines in battle shader renderer
		var main = get_tree().root.get_node_or_null("Main")
		if main and main.battle_shader_renderer:
			main.battle_shader_renderer.toggle_grid_lines()
			add_log("[color=cyan]Grid Lines: %s[/color]" % ("ON" if main.battle_shader_renderer.show_grid else "OFF"))
		GameState.emit_signal("map_updated")
		return
	elif event.keycode == KEY_P:
		# Toggle graphics mode in battle shader renderer
		var main = get_tree().root.get_node_or_null("Main")
		if main and main.battle_shader_renderer:
			main.battle_shader_renderer.toggle_graphics_mode()
			var mode_name = "PROCEDURAL" if main.battle_shader_renderer.graphics_mode == 1 else "SOLID"
			add_log("[color=cyan]Graphics Mode: %s[/color]" % mode_name)
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
	return combat.get_unit_range(u)

func is_unit_ranged(u):
	return combat.is_unit_ranged(u)

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
	return combat.is_fleeing(u, player_unit)

func update_ai_step(delta):
	is_batch_processing = true
	# 1. Update Projectiles (Smooth motion during simulation burst)
	combat.update_projectiles(projectiles, delta, resolve_complex_damage, resolve_aoe_damage, _find_unit_along_line, add_log, battle_debug_enabled)

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
	combat.perform_attack_on(u, target, add_log, spawn_projectile, resolve_complex_damage)

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
	ai.update_global_battle_state(units, battalions, unit_lookup, battle_debug_enabled, add_log, is_fleeing)
	
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

func set_order(new_order):
	ai.set_order(new_order, add_log, func(): GameState.emit_signal("map_updated"))

func plan_ai_decision(u):
	ai.plan_ai_decision(u, units, player_unit, battalions, unit_lookup, is_siege, grid, 
						get_unit_range, is_unit_ranged, is_fleeing, _find_nearest_enemy_spatial, _find_nearest_tile_char)

# --- AI Helper Methods (Optimization 2: Pure Calculation) ---

func get_step_towards(u, t_pos):
	return physics.get_step_towards(u, t_pos)

func get_step_away(u, t_pos):
	return physics.get_step_away(u, t_pos)

func _find_nearest_enemy_spatial(u, max_dist, target_b_id = -1, prioritize_clusters = false):
	return physics.find_nearest_enemy_spatial(u, max_dist, target_b_id, prioritize_clusters)

func _find_nearest_tile_char(pos, char_to_find, max_dist):
	return physics.find_nearest_tile_char(pos, char_to_find, max_dist)

func move_towards(u, target_pos):
	physics.move_towards(u, target_pos)

func move_away_from(u, target_pos):
	physics.move_away_from(u, target_pos)

func register_unit(u):
	physics.register_unit(u)

func unregister_unit(u):
	physics.unregister_unit(u)

func update_unit_spatial(u):
	physics.update_unit_spatial(u)

func remove_unit_spatial(u):
	physics.remove_unit_spatial(u)

func refresh_all_spatial():
	physics.refresh_all_spatial(units)
	ai.refresh_caches(units)

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
	siege.damage_structure(pos, amount, grid, structural_cache, add_log)

func _get_structure_name(tile: String) -> String:
	return siege._get_structure_name(tile)

func perform_attack(u):
	var range_val = get_unit_range(u)
	var is_ranged = is_unit_ranged(u)
	combat.perform_attack(u, unit_lookup, range_val, is_ranged, player_unit, add_log, spawn_projectile, resolve_complex_damage)

func spawn_projectile(attacker, defender, forced_part = "", attack_idx = 0, mode = "standard"):
	var projectile = combat.spawn_projectile(attacker, defender, forced_part, attack_idx, mode)
	projectiles.append(projectile)

func resolve_complex_damage(attacker, defender, forced_part = "", attack_idx = 0):
	return combat.resolve_complex_damage(attacker, defender, forced_part, attack_idx, battalions, is_tournament, battle_debug_enabled, add_log, _get_shield_wall_bonus, unregister_unit, player_unit)


func resolve_aoe_damage(attacker, pos: Vector2i, radius: int, engine_data: Dictionary):
	combat.resolve_aoe_damage(attacker, pos, radius, engine_data, unit_lookup, resolve_complex_damage, add_log)

func _find_unit_along_line(start_pos: Vector2, dir: Vector2, max_dist: float, exclude: Array) -> GDUnit:
	return physics.find_unit_along_line(start_pos, dir, max_dist, exclude)


func is_in_bounds(pos):
	return physics.is_in_bounds(pos)

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
	return siege.create_battalion(troop_list, team, b_type, pivot_pos, uid_start, battalions, units, create_unit, register_unit, _find_spawn_pos)

func spawn_siege_units():
	player_unit = siege.spawn_siege_units(enemy_ref, siege_data, grid, units, battalions, MAP_W, MAP_H, create_unit, register_unit, _find_spawn_pos)

func spawn_units():
	units = []
	unit_lookup.clear()
	spatial_grid.clear()
	battalions.clear()
	siege.battalion_uid = 0
	
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
	# Siege mode uses pre-built map
	if is_siege and siege_data:
		physics.grid = siege_data.grid.duplicate(true)
		return
	
	if physics.grid.size() != MAP_H:
		initialize_grid()
	
	# Delegate to terrain system
	terrain.generate_initial_area(physics.grid, physics.generated_chunks, physics.structural_cache, _generate_chunk)

func _generate_chunk(chunk_pos: Vector2i):
	terrain.generate_chunk(chunk_pos, physics.grid, physics.generated_chunks, physics.structural_cache, enemy_ref)

func _dist_to_segment(p: Vector2, a: Vector2, b: Vector2) -> float:
	var pa = p - a
	var ba = b - a
	var h = clamp(pa.dot(ba) / ba.dot(ba), 0.0, 1.0)
	return (pa - ba * h).length()

func get_unit_at(p):
	return physics.get_unit_at(p)

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
	return ai.get_shield_wall_bonus(u, battalions, unit_lookup)
