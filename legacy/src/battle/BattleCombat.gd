extends RefCounted
class_name BattleCombat

# Combat system for tactical battles
# Handles attack resolution, damage calculation, projectiles, and death

# -----------------------------
# Combat Queries
# -----------------------------

func get_unit_range(u) -> float:
	"""Get attack range of unit"""
	var range_val = 1.5
	if u.is_siege_engine:
		return float(u.engine_stats.get("range", 1.5))
	
	var wpn = u.equipment["main_hand"]
	if wpn:
		range_val = float(wpn.get("range", 1.5))
	elif u.type == "archer":
		range_val = 18.0
	return range_val

func is_unit_ranged(u) -> bool:
	"""Check if unit has ranged attack"""
	if u.is_siege_engine:
		return u.engine_stats.get("range", 1.5) > 2.0
	var wpn = u.equipment["main_hand"]
	if wpn and wpn.get("is_ranged", false):
		return true
	if u.type == "archer":
		return true
	return false

func is_fleeing(u, player_unit) -> bool:
	"""Check if unit should flee"""
	if u == player_unit:
		return false
	var total_hp = 0
	var total_max = 0
	for p_key in u.body:
		for tissue in u.body[p_key]["tissues"]:
			total_hp += tissue["hp"]
			total_max += tissue["hp_max"]
	return total_hp < total_max * 0.2 or u.type == "merchant"

# -----------------------------
# Attack Execution
# -----------------------------

func perform_attack(u, unit_lookup: Dictionary, range_val: float, is_ranged: bool, player_unit, add_log_callback: Callable, spawn_proj_callback: Callable, resolve_dmg_callback: Callable):
	"""Find target and execute attack"""
	var hits = []
	
	# Optimized Attack Scan (Box Search)
	var r = int(ceil(range_val))
	for dy in range(-r, r+1):
		for dx in range(-r, r+1):
			var check_pos = u.pos + Vector2i(dx, dy)
			if unit_lookup.has(check_pos):
				var other = unit_lookup[check_pos]
				if other.team != u.team and other.hp > 0 and not other.status["is_downed"] and not other.status["is_dead"]:
					if not is_fleeing(other, player_unit):
						if u.pos.distance_to(other.pos) <= range_val:
							hits.append(other)
							if not is_ranged: break
			if hits.size() > 0 and not is_ranged: break
		if hits.size() > 0 and not is_ranged: break
	
	if hits.size() > 0:
		var target = hits[0]
		
		# Update Facing
		var attack_diff = target.pos - u.pos
		if attack_diff != Vector2i.ZERO:
			u.facing = Vector2i(int(sign(float(attack_diff.x))), int(sign(float(attack_diff.y))))
		
		# Check if attacking arm is functional
		if not u.status.get("r_arm_functional", true):
			if u == player_unit:
				add_log_callback.call("[color=orange]Your right arm is useless! You can't strike![/color]")
			return
		
		# Choose attack index
		var attack_idx = 0
		var wpn = u.equipment["main_hand"]
		var attacks = wpn.get("attacks", []) if wpn else []
		if attacks.size() > 0:
			attack_idx = GameState.rng.randi() % attacks.size()
		
		if is_ranged:
			spawn_proj_callback.call(u, target, "", attack_idx)
		else:
			resolve_dmg_callback.call(u, target, "", attack_idx)
	elif u == player_unit:
		add_log_callback.call("[color=gray]You missed! Get closer![/color]")

func perform_attack_on(u, target, add_log_callback: Callable, spawn_proj_callback: Callable, resolve_dmg_callback: Callable):
	"""Attack specific target"""
	if is_instance_valid(target) and target.hp > 0:
		if is_unit_ranged(u):
			var mode = "engine:" + u.engine_type if u.is_siege_engine else ""
			spawn_proj_callback.call(u, target, "torso", 0, mode)
			if u.is_siege_engine:
				u.reload_timer = int(u.engine_stats.get("reload_turns", 4))
			else:
				u.reload_timer = 4 # Archer reload nerf
		else:
			resolve_dmg_callback.call(u, target, "torso", 0)

# -----------------------------
# Projectiles
# -----------------------------

func spawn_projectile(attacker, defender, forced_part = "", attack_idx = 0, mode = "standard") -> Dictionary:
	"""Create projectile for ranged attack"""
	var sym = "*"
	var p_data = {}
	
	if mode.begins_with("engine:"):
		var engine_key = mode.split(":")[1]
		var e_info = GameData.SIEGE_ENGINES.get(engine_key, {})
		sym = e_info.get("symbol", "X")
		p_data["engine"] = engine_key
		p_data["remaining_energy"] = e_info.get("dmg_base", 50) + (e_info.get("weight", 5) * e_info.get("velocity", 5))
	else:
		# Dynamic symbol based on trajectory
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
		"speed": 35.0,
		"mode": mode,
		"traveled": 0.0
	}
	
	for k in p_data:
		projectile[k] = p_data[k]
	
	return projectile

func update_projectiles(projectiles: Array, delta: float, resolve_dmg_callback: Callable, resolve_aoe_callback: Callable, find_penetration_callback: Callable, add_log_callback: Callable, battle_debug: bool) -> Array:
	"""Update all projectiles, return array of projectiles to remove"""
	var to_remove = []
	
	for p in projectiles:
		var dir = (p["target_pos"] - p["pos"]).normalized()
		var move = dir * p["speed"] * delta
		var old_pos = p["pos"]
		p["pos"] += move
		
		# Check if hit target
		var dist_to_target = p["pos"].distance_to(p["target_pos"])
		if dist_to_target < 0.5 or (p["target_pos"] - old_pos).dot(p["target_pos"] - p["pos"]) < 0:
			# Hit!
			if is_instance_valid(p["defender"]) and p["defender"].hp > 0:
				var res = resolve_dmg_callback.call(p["attacker"], p["defender"], p.get("forced_part", ""), p.get("attack_idx", 0))
				
				# Siege engine special logic
				if p.has("engine"):
					var e_key = p["engine"]
					var e_info = GameData.SIEGE_ENGINES.get(e_key, {})
					
					# AOE
					if e_info.get("aoe", 0) > 0:
						resolve_aoe_callback.call(p["attacker"], Vector2i(p["target_pos"]), e_info["aoe"], e_info)
						to_remove.append(p)
						continue
					
					# Overpenetration (Ballista)
					if e_info.get("overpenetrate", false) and res.get("remaining_energy", 0.0) > 20.0:
						var next = find_penetration_callback.call(p["target_pos"], dir, 15.0, [p["defender"]])
						if next:
							p["defender"] = next
							p["target_pos"] = Vector2(next.pos)
							continue
			
			to_remove.append(p)
	
	return to_remove

# -----------------------------
# Damage Resolution
# -----------------------------

func resolve_complex_damage(attacker, defender, forced_part, attack_idx, battalions: Dictionary, is_tournament: bool, battle_debug: bool, add_log_callback: Callable, get_shield_bonus_callback: Callable, unregister_callback: Callable, player_unit) -> Dictionary:
	"""Full damage calculation with detailed combat log"""
	# Siege Engine Physics
	var res = {}
	if attacker.is_siege_engine:
		res = GameData.resolve_engine_damage(attacker.engine_type, defender, GameState.rng)
		# Break formation bracing
		if defender.formation_id != -1:
			var b = battalions.get(defender.formation_id)
			if b and b.get("is_braced", false):
				b["is_braced"] = false
				if battle_debug:
					add_log_callback.call("[color=red]DEBUG: FORMATION %d BRACING BROKEN BY %s[/color]" % [defender.formation_id, attacker.engine_type.to_upper()])
	else:
		var sw_bonus = get_shield_bonus_callback.call(defender)
		res = GameData.resolve_attack(attacker, defender, GameState.rng, forced_part, attack_idx, sw_bonus)
	
	# Weapon name construction
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
	
	# Miss
	if not res["hit"]:
		var miss_color = "green" if attacker.team == "player" else "red"
		var is_ranged = wpn.get("is_ranged", false) if wpn else false
		var miss_verb = "misses" if not is_plural else "miss"
		var action_verb = "flies wide" if is_ranged else "swings wide"
		add_log_callback.call("[color=%s]%s %s and %s %s![/color]" % [miss_color, wpn_owner, action_verb, miss_verb, defender.name])
		return res
	
	# Block
	if res["blocked"]:
		var block_color = "green" if defender.team == "player" else "red"
		add_log_callback.call("[color=%s]%s raises their %s and deflects the blow![/color]" % [block_color, defender.name, res["shield_name"]])
		return res
	
	# Hit - Build descriptive combat log
	var log_color = "green" if attacker.team == "player" else "red"
	var part_display = "[color=yellow]%s[/color]" % res["part_hit"]
	
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
	
	# Tissue impact description
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
	add_log_callback.call(log_msg)
	
	# Critical events
	for event in res["critical_events"]:
		match event:
			"artery_severed":
				add_log_callback.call("[color=red]  [CRITICAL] An artery in the %s has been severed! Blood sprays![/color]" % res["part_hit"])
			"vein_opened":
				add_log_callback.call("[color=red]  A major vein in the %s has been opened![/color]" % res["part_hit"])
			"tendon_snapped":
				add_log_callback.call("[color=orange]  [CRITICAL] The tendon in the %s snaps with a sickening pop![/color]" % [res["part_hit"]])
			"nerve_destroyed":
				add_log_callback.call("[color=red]  [CRITICAL] The nerve in the %s is shredded, leaving it limp![/color]" % [res["part_hit"]])
			"bone_fractured":
				add_log_callback.call("[color=orange]  [CRITICAL] The bone in the %s shatters under the impact![/color]" % [res["part_hit"]])
			"brain_destroyed":
				add_log_callback.call("[color=red]  [FATAL] The brain is pulverized! %s dies instantly![/color]" % defender.name)
			"heart_burst":
				add_log_callback.call("[color=red]  [FATAL] The heart is burst! %s's life-blood sprays![/color]" % defender.name)
			"eye_gouged":
				add_log_callback.call("[color=red]  [CRITICAL] %s's eye is gouged out, leaving a bloody socket![/color]" % defender.name)
			"decapitated":
				add_log_callback.call("[color=red]  [FATAL] %s's head is completely severed from their body![/color]" % defender.name)
			"part_destroyed":
				add_log_callback.call("[color=red]  [FATAL] The %s is completely obliterated![/color]" % [res["part_hit"]])
		
		if event.begins_with("organ_failure:"):
			var organ_name = event.split(":")[1]
			add_log_callback.call("[color=red]  [FATAL] The %s has failed! %s's life fades...[/color]" % [organ_name, defender.name])
	
	if res["downed_occurred"]:
		add_log_callback.call("[color=orange]  %s collapses from the agonizing pain![/color]" % defender.name)
	
	if res["prone_occurred"]:
		add_log_callback.call("[color=orange]  %s is knocked violently to the ground![/color]" % defender.name)
	
	# Death/downed handling
	if (defender.status["is_dead"] or defender.status["is_downed"]):
		# Tournament non-lethal intercept
		if is_tournament and defender.status["is_dead"]:
			defender.status["is_dead"] = false
			defender.status["is_downed"] = true
			add_log_callback.call("[color=yellow]The fight is stopped! %s has been knocked out.[/color]" % defender.name)
		
		unregister_callback.call(defender)
		
		if defender == player_unit:
			if defender.status["is_dead"]:
				add_log_callback.call("[color=red][b]YOU HAVE DIED.[/b][/color]")
			else:
				add_log_callback.call("[color=orange][b]YOU HAVE BEEN KNOCKED UNCONSCIOUS.[/b][/color]")
			add_log_callback.call("[color=cyan]Tactical Mode: You can no longer move, but you can still issue orders.[/color]")
	
	# Functional integrity check
	for msg in GameData.check_functional_integrity(defender):
		add_log_callback.call("  " + msg)
	
	return res

func resolve_aoe_damage(attacker, pos: Vector2i, radius: int, engine_data: Dictionary, unit_lookup: Dictionary, add_log_callback: Callable, resolve_dmg_callback: Callable):
	"""Area of effect damage from siege engines"""
	add_log_callback.call("[color=orange]  The %s impact creates a massive shockwave![/color]" % engine_data["name"])
	
	var victims = []
	# Check box around impact
	for dy in range(-radius, radius + 1):
		for dx in range(-radius, radius + 1):
			var target_pos = pos + Vector2i(dx, dy)
			if unit_lookup.has(target_pos):
				var victim = unit_lookup[target_pos]
				if victim.hp > 0 and not victim in victims:
					victims.append(victim)
	
	for victim in victims:
		# Calculate falloff
		var dist = 9999.0
		for offset in victim.footprint:
			dist = min(dist, pos.distance_to(victim.pos + offset))
		
		var fallout = 1.0 - (dist / (radius + 1.0))
		if fallout > 0:
			# Simulate blunt impact for AOE
			resolve_dmg_callback.call(attacker, victim, "torso", 0)
