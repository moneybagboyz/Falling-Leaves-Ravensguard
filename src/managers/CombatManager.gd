@warning_ignore("shadowed_global_identifier")
class_name CombatManager
extends Node

@warning_ignore("shadowed_global_identifier")
static func resolve_ai_battle(att, def, gs):
	gs.total_battles += 1
	
	# AGGREGATION: Collect all participants near the epicenter (Chebyshev radius 2)
	var nearby = gs.get_entities_near(def.pos, 2)
	var attackers = []
	var defenders = []
	
	for e in nearby:
		if "type" in e and e.type in ["army", "lord", "caravan"]:
			# Filter: Is it the same faction as att/def?
			if e.faction == att.faction:
				attackers.append(e)
			elif e.faction == def.faction:
				defenders.append(e)
	
	if not att in attackers: attackers.append(att)
	if not def in defenders: defenders.append(def)
	
	var total_att_str = 0.0
	var total_def_str = 0.0
	for a in attackers: total_att_str += a.strength
	for d in defenders: total_def_str += d.strength
	
	gs.add_log("BATTLE CLUSTER at %v: %d %s vs %d %s!" % [def.pos, attackers.size(), att.faction, defenders.size(), def.faction])
	
	total_att_str *= gs.rng.randf_range(0.8, 1.2)
	total_def_str *= gs.rng.randf_range(0.8, 1.2)
	
	if total_att_str > total_def_str:
		# Attackers Win
		var loot_total = 0
		var loss_pct = clamp(total_def_str / total_att_str * 0.4, 0.05, 0.6)
		for a in attackers:
			for u in a.roster:
				u.hp -= int(u.hp_max * loss_pct * gs.rng.randf_range(0.5, 1.5))
				# XP for survivors
				if u.hp > 0: gs.grant_xp(u, 20)
			a.roster = a.roster.filter(func(u): return u.hp > 0)
			
		# Defenders suffer heavy losses/defeat & provide loot
		for d in defenders:
			var d_val = int(d.strength * 0.05)
			if d.type == "caravan": d_val += d.crowns
			loot_total += d_val
			
			if d.type == "lord":
				d.respawn_timer = 48
				d.pos = d.home_fief if d.home_fief != Vector2i(-1, -1) else d.pos
				d.roster = []
			elif d.type == "caravan":
				d.respawn_timer = 96
				d.pos = d.origin
				d.roster = []
				d.state = "idle"
			else:
				gs.erase_army(d)
		
		# Distribute Loot among attackers
		var share = int(loot_total / max(1, attackers.size()))
		for a in attackers:
			if a.type == "lord": 
				a.crowns += share
				a.renown += 10
			elif a.type == "player": 
				gs.player.crowns += share
				gs.player.renown += 10
		
		gs.add_log("Attackers of %s dominated! Total Loot: %d Crowns." % [att.faction, loot_total])
		gs.track_war_event("victory", att.faction, def.faction)
	else:
		# Defenders Win
		var loss_pct = clamp(total_att_str / total_def_str * 0.4, 0.05, 0.6)
		for d in defenders:
			for u in d.roster:
				u.hp -= int(u.hp_max * loss_pct * gs.rng.randf_range(0.5, 1.5))
				if u.hp > 0: gs.grant_xp(u, 20)
			d.roster = d.roster.filter(func(u): return u.hp > 0)
			
		for a in attackers:
			if a.type == "lord":
				a.respawn_timer = 48
				a.pos = a.home_fief if a.home_fief != Vector2i(-1, -1) else a.pos
				a.roster = []
			elif a.type == "caravan":
				a.respawn_timer = 96
				a.pos = a.origin
				a.roster = []
				a.state = "idle"
			else:
				gs.erase_army(a)
		gs.add_log("Defenders of %s held their ground!" % def.faction)
		gs.track_war_event("victory", def.faction, att.faction)

@warning_ignore("shadowed_global_identifier")
static func resolve_siege(army_obj, town_obj, gs) -> String:
	# Siege initialization
	if not town_obj.is_under_siege:
		town_obj.is_under_siege = true
		town_obj.siege_timer = 0
		town_obj.siege_attacker_faction = army_obj.faction
		gs.add_log("The Siege of %s has begun! Led by %s." % [town_obj.name, army_obj.faction])

	# DYNAMIC DAILY TICK: Track last processed day via metadata
	# This ensures throttled/skipped AI updates still only resolve combat once every 24 turns
	var current_siege_day = int(town_obj.siege_timer / 24.0)
	var last_tick = town_obj.get_meta("last_siege_day_tick", -1)
	
	if current_siege_day <= last_tick:
		return "ongoing"
	
	town_obj.set_meta("last_siege_day_tick", current_siege_day)

	gs.total_sieges += 1
	
	# AGGREGATION: Collect all nearby friendly armies to join the siege
	var siege_pos = town_obj.pos
	var nearby = gs.get_entities_near(siege_pos, 2)
	var attackers = []
	for e in nearby:
		if "type" in e and e.type in ["army", "lord"] and e.faction == army_obj.faction:
			attackers.append(e)
	
	if not army_obj in attackers: attackers.append(army_obj)
	
	var garrison = float(town_obj.garrison)
	var total_att_str = 0.0
	for a in attackers: total_att_str += a.strength
	
	# Siege Progress Logic: Days under siege weakens the defenders
	var starvation_mult = 1.0 + (current_siege_day * 0.05) 
	
	gs.add_log("SIEGE UPDATE: %s has been besieged for %d days. Attacking Force: %.0f." % [town_obj.name, current_siege_day, total_att_str])
		
	total_att_str *= gs.rng.randf_range(0.8, 1.2) * starvation_mult
	
	# Defence Calculation
	var wall_lvl = town_obj.buildings.get("stone_walls", 0)
	
	# WALL MILESTONES (Odd levels)
	if wall_lvl >= 3: # Towers
		total_att_str *= 0.75 
	if wall_lvl >= 7: # Engines
		for a in attackers:
			for u in a.roster:
				if gs.rng.randf() < 0.1: 
					u.hp -= int(u.hp_max * 0.2) 
	if wall_lvl >= 9: # Moat
		total_att_str *= 0.5 
		
	# Adjusted multipliers to match AI assessment (5-10x instead of 150x)
	var wall_mult = 3.0 + (wall_lvl * 3.0) 
	if wall_lvl >= 10: wall_mult *= 1.3
	
	var barracks_lvl = town_obj.buildings.get("barracks", 0)
	var training_lvl = town_obj.buildings.get("training_ground", 0)
	var garrison_quality = 6.0 + (barracks_lvl * 1.5) + (training_lvl * 0.8)
	
	var def_str = garrison * garrison_quality * wall_mult 
	
	# CHANCE TO BREACH: Base chance 10% + scaling
	var breach_chance = (total_att_str / max(1.0, def_str)) * 0.15 * (1.0 + current_siege_day / 4.0)
	
	if gs.rng.randf() < breach_chance or total_att_str > def_str * 2.5:
		# Capture!
		gs.total_captures += 1
		var old_owner = town_obj.faction
		town_obj.faction = army_obj.faction
		town_obj.is_under_siege = false
		town_obj.siege_timer = 0
		
		# Siege Spoils
		var loot = int(town_obj.population * 2.0) + int(town_obj.crown_stock * 0.5)
		town_obj.crown_stock = int(town_obj.crown_stock * 0.5)
		if army_obj.type == "lord": army_obj.crowns += loot
		elif army_obj.type == "player": gs.player.crowns += loot
		
		# Transfer half the primary army to the new garrison
		var half = army_obj.roster.size() / 2
		town_obj.garrison = half
		army_obj.roster = army_obj.roster.slice(0, half)
			
		gs.add_log("%s has been CAPTURED! %s takes the city from %s." % [town_obj.name, gs.get_faction(army_obj.faction).name, gs.get_faction(old_owner).name])
		return "captured"
	else:
		# Repelled / Continued
		# Minor daily casualties for attackers
		var attrition = 0.05 + (wall_lvl * 0.01)
		for a in attackers:
			for u in a.roster:
				if gs.rng.randf() < attrition:
					u.hp -= int(u.hp_max * 0.1)
			a.roster = a.roster.filter(func(u): return u.hp > 0)
			
			if a.roster.size() == 0:
				if a.type == "lord":
					a.respawn_timer = 48
					a.pos = a.home_fief if a.home_fief != Vector2i(-1, -1) else a.pos
				else:
					gs.armies.erase(a)
		
		# DEFENDER ATTRITION: Garrison loses some units daily to starvation/desertion
		var def_attrition = 0.02 + (current_siege_day * 0.005) # Increases over time
		var losses = int(town_obj.garrison * def_attrition)
		town_obj.garrison = max(10, town_obj.garrison - losses)
		
		return "ongoing"
