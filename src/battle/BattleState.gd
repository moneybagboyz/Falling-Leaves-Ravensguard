extends RefCounted
class_name BattleState

## Battle State System
## Handles battle lifecycle, unit spawning, rewards, and win/loss conditions

# GameData and GameState are autoloads - no need to preload
const GDUnit = preload("res://src/data/GDUnit.gd")
const GDNPC = preload("res://src/data/GDNPC.gd")

const BATTALION_SIZE = 30
const MAP_W = 200
const MAP_H = 100

## Initialize battle with parameters
func start_battle(battle_params: Dictionary, add_log_callback: Callable, 
				  generate_map_callback: Callable, spawn_units_callback: Callable,
				  refresh_spatial_callback: Callable, set_camera_callback: Callable) -> Dictionary:
	var state = {
		"active": true,
		"enemy_ref": battle_params.get("enemy"),
		"allies_ref": battle_params.get("allies"),
		"is_tournament": battle_params.get("is_tournament", false),
		"is_siege": battle_params.get("is_siege", false),
		"siege_data": battle_params.get("siege_data"),
		"tournament_prize": battle_params.get("prize", 0),
		"turn": 1,
		"targeting_mode": false,
		"log_offset": 0,
		"auto_battle": false,
		"camera_locked": true,
		"camera_zoom": 1.0,
		"battle_log": []
	}
	
	# REPAIR: Initialize grid BEFORE logging, as logging triggers a UI refresh
	generate_map_callback.call()
	
	if state["is_tournament"]:
		add_log_callback.call("[color=yellow]TOURNAMENT MATCH STARTED[/color]")
		add_log_callback.call("[color=gray]Defeat all opponents to advance. Damage is non-lethal.[/color]")
	else:
		add_log_callback.call("[color=yellow]BATTLE STARTED (Turn-Based)[/color]")
		add_log_callback.call("[color=gray]WASD: Move, SPACE: Attack, 1-5: Orders, PGUP/PGDN: Scroll Log[/color]")
		add_log_callback.call("[color=gray]1: ADVANCE | 2: CHARGE | 3: FOLLOW | 4: HOLD | 5: RETREAT[/color]")
		add_log_callback.call("[color=gray]V: Toggle Free Cam (WASD to scroll), Z: Auto-Battle[/color]")
		add_log_callback.call("[color=magenta]PRESS K TO TOGGLE BATTLE DEBUG LOGS[/color]")
	
	spawn_units_callback.call()
	refresh_spatial_callback.call()
	
	var player_unit = state.get("player_unit")
	if player_unit:
		set_camera_callback.call(Vector2(player_unit.pos))
	
	return state

## Spawn all units for the battle
func spawn_units(battle_params: Dictionary, create_unit_callback: Callable, 
				 create_battalion_callback: Callable, spawn_siege_callback: Callable,
				 find_spawn_pos_callback: Callable, register_unit_callback: Callable) -> Dictionary:
	var result = {
		"units": [],
		"player_unit": null,
		"battalions": {}
	}
	
	var is_siege = battle_params.get("is_siege", false)
	var siege_data = battle_params.get("siege_data")
	var is_tournament = battle_params.get("is_tournament", false)
	var enemy_ref = battle_params.get("enemy")
	var allies_ref = battle_params.get("allies")
	
	if is_siege and siege_data:
		return spawn_siege_callback.call()
	
	var uid = 0
	var center_y = int(MAP_H / 2.0)
	var center_x = int(MAP_W / 2.0)
	
	# Determine distance between armies
	var army_dist = 40
	if is_tournament: 
		army_dist = 20
	elif enemy_ref and "roster" in enemy_ref and enemy_ref.roster.size() > 40: 
		army_dist = 60
	
	var p_start_x = center_x - (army_dist / 2)
	var e_start_x = center_x + (army_dist / 2)
	
	if is_tournament:
		# Tournament: One-on-one or small team (Non-lethal)
		var p_pos = Vector2i(p_start_x, center_y)
		var player_unit = create_unit_callback.call(0, GameState.player.commander, "player", p_pos, -1, Vector2i.ZERO)
		player_unit.name = "You"
		result.units.append(player_unit)
		register_unit_callback.call(player_unit)
		result.player_unit = player_unit
		uid = 1
		
		var e_pos_base = Vector2i(e_start_x, center_y)
		if enemy_ref is GDNPC:
			var u = create_unit_callback.call(uid, enemy_ref.commander_data, "enemy", e_pos_base, -1, Vector2i.ZERO)
			result.units.append(u)
			register_unit_callback.call(u)
		elif enemy_ref is Array:
			for i in range(enemy_ref.size()):
				var npc_id = enemy_ref[i]
				var npc = GameState.find_npc(npc_id)
				if npc:
					var p = e_pos_base + Vector2i(0, (i - enemy_ref.size()/2)*4)
					var u = create_unit_callback.call(uid + i, npc.commander_data, "enemy", p, -1, Vector2i.ZERO)
					result.units.append(u)
					register_unit_callback.call(u)
		return result
	
	# Ally/Reinforcement support
	if allies_ref and "roster" in allies_ref:
		var ally_result = _spawn_ally_battalions(allies_ref, center_y, uid, create_battalion_callback)
		result.battalions.merge(ally_result.battalions)
		uid = ally_result.uid
	
	# Sort player roster by type
	var sorted_roster = _sort_roster_by_type(GameState.player.roster)
	
	# Spawn Player Commander
	var cmd_data = GameState.player.commander
	var p_cmd_pos = find_spawn_pos_callback.call(Vector2i(p_start_x + 5, center_y))
	var player_unit = create_unit_callback.call(uid, cmd_data, "player", p_cmd_pos)
	result.units.append(player_unit)
	register_unit_callback.call(player_unit)
	result.player_unit = player_unit
	uid += 1
	
	# Spawn Player Formations
	var player_result = _spawn_player_battalions(sorted_roster, p_start_x, center_y, uid, create_battalion_callback)
	result.battalions.merge(player_result.battalions)
	uid = player_result.uid
	
	# Spawn Enemy Formations
	if enemy_ref:
		var enemy_result = _spawn_enemy_battalions(enemy_ref, e_start_x, center_y, uid, 
													create_unit_callback, create_battalion_callback,
													find_spawn_pos_callback, register_unit_callback)
		result.units.append_array(enemy_result.units)
		result.battalions.merge(enemy_result.battalions)
	
	return result

## Sort roster by unit type
func _sort_roster_by_type(roster: Array) -> Dictionary:
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
	
	return {
		"infantry": infantry,
		"archers": archers,
		"cavalry": cavalry,
		"siege": siege
	}

## Spawn ally battalions
func _spawn_ally_battalions(allies_ref, center_y: int, start_uid: int, create_battalion_callback: Callable) -> Dictionary:
	var result = {"battalions": {}, "uid": start_uid}
	
	var sorted = _sort_roster_by_type(allies_ref.roster)
	var a_inf = sorted.infantry
	var a_arc = sorted.archers
	var a_cav = sorted.cavalry
	
	result.uid = create_battalion_callback.call(a_inf, "player", "infantry", Vector2i(40, center_y), result.uid)
	result.uid = create_battalion_callback.call(a_arc, "player", "archer", Vector2i(50, center_y), result.uid)
	
	if a_cav.size() > 0:
		var a_half = a_cav.size() / 2
		result.uid = create_battalion_callback.call(a_cav.slice(0, a_half), "player", "cavalry", Vector2i(45, center_y - 20), result.uid)
		result.uid = create_battalion_callback.call(a_cav.slice(a_half), "player", "cavalry", Vector2i(45, center_y + 20), result.uid)
	
	return result

## Spawn player battalions
func _spawn_player_battalions(sorted_roster: Dictionary, p_start_x: int, center_y: int, 
							   start_uid: int, create_battalion_callback: Callable) -> Dictionary:
	var result = {"battalions": {}, "uid": start_uid}
	
	var infantry = sorted_roster.infantry
	var archers = sorted_roster.archers
	var cavalry = sorted_roster.cavalry
	var siege = sorted_roster.siege
	
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
			
			var v_off = (b_idx - (num_batches-1.0)/2.0) * set["y_spacing"]
			var pivot = Vector2i(p_start_x + set["x_off"], center_y + int(v_off))
			result.uid = create_battalion_callback.call(sub_list, "player", set["type"], pivot, result.uid)
	
	if cavalry.size() > 0:
		var half = int(cavalry.size() / 2)
		result.uid = create_battalion_callback.call(cavalry.slice(0, half), "player", "cavalry", Vector2i(p_start_x, center_y - 30), result.uid)
		result.uid = create_battalion_callback.call(cavalry.slice(half), "player", "cavalry", Vector2i(p_start_x, center_y + 30), result.uid)
	
	return result

## Spawn enemy battalions
func _spawn_enemy_battalions(enemy_ref, e_start_x: int, center_y: int, start_uid: int,
							  create_unit_callback: Callable, create_battalion_callback: Callable,
							  find_spawn_pos_callback: Callable, register_unit_callback: Callable) -> Dictionary:
	var result = {"units": [], "battalions": {}, "uid": start_uid}
	
	var e_roster = []
	if "roster" in enemy_ref:
		e_roster = enemy_ref.roster
	
	if not e_roster.is_empty():
		var sorted = _sort_roster_by_type(e_roster)
		var e_inf = sorted.infantry
		var e_arc = sorted.archers
		var e_cav = sorted.cavalry
		var e_siege = sorted.siege
		
		# Enemy Leader
		var leader_pos = find_spawn_pos_callback.call(Vector2i(e_start_x - 3, center_y))
		var e_leader = create_unit_callback.call(result.uid, enemy_ref, "enemy", leader_pos)
		result.units.append(e_leader)
		register_unit_callback.call(e_leader)
		result.uid += 1
		
		# Enemy Formations
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
				result.uid = create_battalion_callback.call(sub_list, "enemy", set["type"], pivot, result.uid)
		
		if e_cav.size() > 0:
			var e_half = int(e_cav.size() / 2)
			result.uid = create_battalion_callback.call(e_cav.slice(0, e_half), "enemy", "cavalry", Vector2i(e_start_x, center_y - 30), result.uid)
			result.uid = create_battalion_callback.call(e_cav.slice(e_half), "enemy", "cavalry", Vector2i(e_start_x, center_y + 30), result.uid)
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
		
		result.uid = create_battalion_callback.call(e_troops, "enemy", "infantry", Vector2i(e_start_x, center_y), result.uid)
	
	return result

## End battle and process rewards/consequences
func end_battle(win: bool, battle_params: Dictionary, units: Array, player_unit) -> void:
	var enemy_ref = battle_params.get("enemy")
	var allies_ref = battle_params.get("allies")
	var is_tournament = battle_params.get("is_tournament", false)
	
	# Clear ongoing battle
	for i in range(GameState.ongoing_battles.size() - 1, -1, -1):
		var b = GameState.ongoing_battles[i]
		if b.attacker == enemy_ref or b.defender == enemy_ref or b.attacker == allies_ref or b.defender == allies_ref:
			GameState.ongoing_battles.remove_at(i)
			if b.attacker: b.attacker.is_in_battle = false
			if b.defender: b.defender.is_in_battle = false
	
	if win:
		_process_victory(enemy_ref, units, player_unit, is_tournament)
	else:
		_process_defeat(units)

## Process victory rewards
func _process_victory(enemy_ref, units: Array, player_unit, is_tournament: bool) -> void:
	if player_unit.status["is_dead"] or player_unit.status["is_downed"]:
		GameState.add_log("Battle Won! Your troops secured the field and recovered your body.")
	else:
		GameState.add_log("Battle Won! Glory to the Commander!")
	
	var e_type = ""
	if enemy_ref:
		e_type = enemy_ref.get("type", "") if enemy_ref is Dictionary else enemy_ref.type
	
	if e_type == "caravan":
		_loot_caravan(enemy_ref)
	else:
		GameState.player.crowns += 50
		if not enemy_ref is Dictionary:
			GameState.erase_army(enemy_ref)
	
	_loot_equipment(units)
	_capture_prisoners(units)
	_process_survivors(units)

## Loot a defeated caravan
func _loot_caravan(enemy_ref) -> void:
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
		enemy_ref.respawn_timer = 120
		enemy_ref.pos = enemy_ref.origin
		enemy_ref.roster = []
		enemy_ref.inventory = {}
		enemy_ref.target_pos = Vector2i(-1, -1)
		enemy_ref.state = "idle"
	GameState.add_log("The surviving merchants flee back to their home settlement.")

## Loot equipment from dead enemies
func _loot_equipment(units: Array) -> void:
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

## Capture prisoners from defeated enemies
func _capture_prisoners(units: Array) -> void:
	var prisoners_taken = 0
	for u in units:
		if u.team == "enemy" and u.data_ref:
			if u.hp == -1 or u.status["is_downed"]:
				var cap_chance = 0.8 if u.status["is_downed"] else 0.3
				if GameState.rng.randf() < cap_chance:
					var prisoner = _create_prisoner_data(u)
					GameState.player.prisoners.append(prisoner)
					prisoners_taken += 1
	
	if prisoners_taken > 0:
		GameState.add_log("Captured %d prisoners." % prisoners_taken)

## Create prisoner data from a unit
func _create_prisoner_data(u) -> Dictionary:
	var prisoner
	if u.data_ref is Dictionary:
		prisoner = u.data_ref.duplicate(true)
	else:
		prisoner = {
			"name": u.name,
			"type": u.type,
			"xp": 100,
			"cost": 500,
			"body": u.body.duplicate(true),
			"equipment": u.equipment.duplicate(true),
			"status": u.status.duplicate(true),
			"attributes": u.data_ref.attributes.duplicate(true) if (u.data_ref and "attributes" in u.data_ref) else {},
			"skills": u.data_ref.skills.duplicate(true) if (u.data_ref and "skills" in u.data_ref) else {}
		}
	
	# Ensure required fields
	if not prisoner is GDUnit:
		if not prisoner.has("hp_max"): prisoner["hp_max"] = u.hp_max
		if not prisoner.has("hp"): prisoner["hp"] = u.hp_max
	if not prisoner.has("body"): prisoner["body"] = u.body.duplicate(true)
	if not prisoner.has("equipment"): prisoner["equipment"] = u.equipment.duplicate(true)
	if not prisoner.has("status"): prisoner["status"] = u.status.duplicate(true)
	if not prisoner.has("xp"): prisoner["xp"] = 0
	
	# Reset HP
	for p_key in prisoner["body"]:
		var part = prisoner["body"][p_key]
		for tissue in part["tissues"]:
			tissue["hp"] = tissue["hp_max"]
	prisoner["hp"] = GameData.get_total_hp(prisoner["body"])
	prisoner["status"]["is_downed"] = false
	
	return prisoner

## Process survivors and sync rosters
func _process_survivors(units: Array) -> void:
	var p_survivors = []
	var a_survivors = []
	
	for u in units:
		if u.team == "player" and u.data_ref:
			var data = u.data_ref
			
			if u.status["is_dead"]:
				GameState.add_log("[color=red]%s has been killed in action.[/color]" % data.name)
				continue
			
			# Check for permanent injuries
			var is_fatal = _check_fatal_injuries(u)
			if is_fatal:
				GameState.add_log("[color=red]%s died from wounds.[/color]" % data.name)
				continue
			
			# Sync wounds
			data.body = u.body.duplicate(true)
			data.status = u.status.duplicate(true)
			data.hp = u.hp
			
			# XP Gain
			var xp_gain = 10
			data.xp += xp_gain
			
			p_survivors.append(data)
	
	# Sync rosters
	GameState.player.roster = p_survivors
	if GameState.player.roster.is_empty():
		GameState.add_log("[color=red]All troops lost![/color]")

## Check if unit has fatal injuries
func _check_fatal_injuries(u) -> bool:
	for p_key in u.body:
		var part = u.body[p_key]
		var is_mangled = false
		for tissue in part["tissues"]:
			if tissue["hp"] <= 0 and tissue.get("structural", false):
				is_mangled = true
				break
		
		if is_mangled:
			if p_key == "head" or p_key == "torso" or p_key == "neck":
				return true
	return false

## Process defeat
func _process_defeat(units: Array) -> void:
	GameState.add_log("[color=red]Battle Lost! You have been defeated.[/color]")
	GameState.add_log("[color=orange]You wake up days later, stripped of all equipment...[/color]")
	
	# Player loses all equipment and gold
	GameState.player.crowns = int(GameState.player.crowns * 0.1)
	GameState.player.stash.clear()
	
	# Clear roster
	for u in units:
		if u.team == "player" and u.data_ref:
			u.data_ref.equipment = {
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
				"r_foot": {"under": null, "over": null, "armor": null, "cover": null}
			}
