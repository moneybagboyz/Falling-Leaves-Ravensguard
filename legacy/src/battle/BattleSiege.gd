extends RefCounted
class_name BattleSiege

## Battle Siege System
## Handles siege engines, fortifications, crew management, and battalion formations

# GameData and GameState are autoloads - no need to preload
const GDUnit = preload("res://src/data/GDUnit.gd")

# Structure damage tracking
var structure_hp = {} # Key: Vector2i, Value: float

# Battalion counter
var battalion_uid = 0

## Damage a fortification structure
func damage_structure(pos: Vector2i, amount: float, grid: Array, structural_cache: Dictionary, add_log_callback: Callable):
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
		add_log_callback.call("[color=red]The %s has been destroyed![/color]" % _get_structure_name(old_tile))

## Get descriptive name for a structure tile
func _get_structure_name(tile: String) -> String:
	match tile:
		"G": return "Gate"
		"#": return "Wall"
		"H": return "Heavy Wall"
		"K": return "Keep"
	return "Structure"

## Create a battalion formation with units
func create_battalion(troop_list: Array, team: String, b_type: String, pivot_pos: Vector2i, uid_start: int, 
					   battalions: Dictionary, units: Array, create_unit_callback: Callable, 
					   register_unit_callback: Callable, find_spawn_pos_callback: Callable) -> int:
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
		var pos = find_spawn_pos_callback.call(Vector2i(b.pivot) + offset)
		
		var u = create_unit_callback.call(current_uid, troop_list[i], team, pos, b_id, offset)
		units.append(u)
		register_unit_callback.call(u)
		current_uid += 1
		
		# Auto-spawn crew for siege engines if not in siege mode
		if u.is_siege_engine:
			var req = u.engine_stats.get("crew_required", 2)
			for j in range(req):
				var c_pos = find_spawn_pos_callback.call(u.pos + Vector2i(randi_range(-1, 1), randi_range(-1, 1)))
				var c_data = GameData.generate_unit("laborer", u.tier)
				var crew = create_unit_callback.call(current_uid, c_data, team, c_pos, b_id)
				crew.assigned_engine_id = u.id
				crew.symbol = "e" # Engine Crew
				u.crew_ids.append(crew.id)
				units.append(crew)
				register_unit_callback.call(crew)
				current_uid += 1
	
	return current_uid

## Spawn units for a siege battle (fortification assault)
func spawn_siege_units(enemy_ref, siege_data: Dictionary, grid: Array, units: Array, battalions: Dictionary,
					   MAP_W: int, MAP_H: int, create_unit_callback: Callable, register_unit_callback: Callable,
					   find_spawn_pos_callback: Callable) -> GDUnit:
	var uid = 0
	var center_y = MAP_H / 2
	var player_unit = null
	
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
	uid = create_battalion(att_siege, "player", "siege_engine", Vector2i(15, center_y), uid, battalions, units, create_unit_callback, register_unit_callback, find_spawn_pos_callback)
	uid = create_battalion(att_inf, "player", "infantry", Vector2i(10, center_y), uid, battalions, units, create_unit_callback, register_unit_callback, find_spawn_pos_callback)
	uid = create_battalion(att_arc, "player", "archer", Vector2i(5, center_y), uid, battalions, units, create_unit_callback, register_unit_callback, find_spawn_pos_callback)
	uid = create_battalion(att_cav, "player", "cavalry", Vector2i(5, center_y - 20), uid, battalions, units, create_unit_callback, register_unit_callback, find_spawn_pos_callback)
	
	# Player Commander
	var p_cmd_pos = find_spawn_pos_callback.call(Vector2i(12, center_y))
	player_unit = create_unit_callback.call(uid, GameState.player.commander, "player", p_cmd_pos)
	units.append(player_unit)
	register_unit_callback.call(player_unit)
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
		var u = create_unit_callback.call(uid, def_arc.pop_back(), "enemy", t_pos)
		units.append(u)
		register_unit_callback.call(u)
		uid += 1
	
	# Place remaining Archers on Walls (spread out)
	var wall_step = 3
	for i in range(0, walls.size(), wall_step):
		if def_arc.is_empty(): break
		var u = create_unit_callback.call(uid, def_arc.pop_back(), "enemy", walls[i])
		units.append(u)
		register_unit_callback.call(u)
		uid += 1
		
	# Place Infantry at Gates
	for g_pos in gates:
		if def_inf.is_empty(): break
		var spawn_p = find_spawn_pos_callback.call(g_pos + Vector2i(1, 0)) # Inside gate
		var u = create_unit_callback.call(uid, def_inf.pop_back(), "enemy", spawn_p)
		units.append(u)
		register_unit_callback.call(u)
		uid += 1
		
	# Place Enemy Leader and rest of Infantry at Keep
	var leader = enemy_ref.get("commander", e_roster[0])
	var u_leader = create_unit_callback.call(uid, leader, "enemy", keep_pos)
	units.append(u_leader)
	register_unit_callback.call(u_leader)
	uid += 1
	
	while not def_inf.is_empty():
		var p = find_spawn_pos_callback.call(keep_pos + Vector2i(randi_range(-2, 2), randi_range(-2, 2)))
		var u = create_unit_callback.call(uid, def_inf.pop_back(), "enemy", p)
		units.append(u)
		register_unit_callback.call(u)
		uid += 1
	
	return player_unit
