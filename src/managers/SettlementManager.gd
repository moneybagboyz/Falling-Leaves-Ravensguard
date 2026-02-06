class_name SettlementManager
extends Node

static func refresh_shop(s_data):
	var tier = s_data.tier
	if tier <= 1: 
		s_data.shop_inventory = []
		return
		
	s_data.shop_inventory = []
	var count = 10 + (tier * 10)
	if s_data.type == "castle": count += 10
	
	var types = GameData.ITEMS.keys()
	var materials = []
	for m in GameData.MATERIALS.keys():
		if m in ["flesh", "bone", "chitin", "tin", "lead"]: continue
		materials.append(m)
		
	var qualities = ["rusty", "standard"]
	if tier >= 3: qualities.append("fine")
	if tier >= 4: qualities.append("masterwork")
	
	if not (s_data.buildings.has("mine") or tier >= 4):
		if "steel" in materials: materials.erase("steel")
		if "silk" in materials: materials.erase("silk")
		
	for i in range(count):
		var t = types[GameState.rng.randi() % types.size()]
		var m = materials[GameState.rng.randi() % materials.size()]
		var q = qualities[GameState.rng.randi() % qualities.size()]
		
		# Validation logic
		var item_base = GameData.ITEMS[t]
		if t.contains("bow") and m != "wood": m = "wood"
		if (t == "gambeson" or t == "tunic") and not m in ["cloth", "wool", "linen", "silk"]: m = "wool"
		
		var item = GameData.create_item_data(t, m, q)
		if item:
			item["price"] = int((item.get("dmg", 0) * 5) + (item.get("prot", 0) * 10) + 10)
			if item.get("quality") == "masterwork": item["price"] *= 2
			s_data.shop_inventory.append(item)

static func refresh_recruits(s_data):
	s_data.recruit_pool = []
	var tier = s_data.tier
	if tier == 1:
		for i in range(5): s_data.recruit_pool.append(GameData.generate_laborer(GameState.rng))
		return

	var recruit_count = 5 + (tier * 5)
	var laborer_count = 15 - (tier * 2)
	var recruit_tier = clamp(tier - 1, 1, 5)
	
	# BARRACKS UNLOCKS (Odds = Amount, Evens = Tier)
	var b_lvl = s_data.buildings.get("barracks", 0)
	var t_lvl = s_data.buildings.get("training_ground", 0)
	
	# ODDS: Pool Size
	var pool_bonus = 0
	if b_lvl >= 1: pool_bonus += 5
	if b_lvl >= 3: pool_bonus += 5
	if b_lvl >= 5: pool_bonus += 10
	if b_lvl >= 7: pool_bonus += 10
	if b_lvl >= 9: pool_bonus += 15
	recruit_count += pool_bonus
	
	# EVENS: Quality / Tier Unlocks
	var max_tier = clamp(s_data.tier - 1, 1, 5)
	if b_lvl >= 2: max_tier = max(max_tier, 2)
	if b_lvl >= 4: max_tier = max(max_tier, 3)
	if b_lvl >= 6: max_tier = max(max_tier, 4)
	if b_lvl >= 8: max_tier = max(max_tier, 5)
	
	# Quality floor from training ground
	var q_floor = int(t_lvl / 2.0)
	
	for i in range(recruit_count):
		var r_tier = max(q_floor, GameState.rng.randi_range(1, max_tier))
		if b_lvl >= 10 and GameState.rng.randf() < 0.2: r_tier = 5 # Milestone bonus
		s_data.recruit_pool.append(GameData.generate_recruit(GameState.rng, clamp(r_tier, 1, 5)))
			
	# TAVERN UNLOCKS
	var tavern_lvl = s_data.buildings.get("tavern", 0)
	# Lvl 4: Traveler's Inn (Mercenaries)
	if tavern_lvl >= 4 and GameState.rng.randf() < 0.4:
		s_data.recruit_pool.append(GameData.generate_recruit(GameState.rng, 4)) # Veteran Mercenary

	for i in range(max(0, laborer_count)):
		s_data.recruit_pool.append(GameData.generate_laborer(GameState.rng))

static func buy_industry_share(s_data: GDSettlement, player: GDPlayer, b_id: String, percent_inc: float = 0.1):
	if not s_data.buildings.has(b_id):
		return "Building not present."
	
	var b_data = GameData.BUILDINGS.get(b_id, {})
	if b_data.get("category") != "industry":
		return "Only industrial buildings can be privately owned."
		
	var current_share = s_data.player_shares.get(b_id, 0.0)
	if current_share + percent_inc > 1.0:
		return "You already own the maximum possible share."
		
	# PRICE: (Base Cost * (Lvl + 1)^2.2) * multiplier
	var lvl = s_data.buildings[b_id]
	var base_cost = b_data.get("cost", 1000)
	var asset_value = int(base_cost * pow(lvl + 1, 2.2))
	
	# Investors usually pay a premium over build cost for a working asset
	var deed_cost = int(asset_value * 2.0 * percent_inc)
	
	if player.crowns >= deed_cost:
		player.crowns -= deed_cost
		s_data.crown_stock += deed_cost
		s_data.player_shares[b_id] = current_share + percent_inc
		return "Successfully purchased " + str(int(percent_inc * 100)) + "% of the " + b_id + " for " + str(deed_cost) + " crowns."
	else:
		return "Not enough crowns. Need " + str(deed_cost) + "."

static func process_governor_ai(s_data):
	if s_data.type == "hamlet": return
	if s_data.construction_queue.size() >= 1: return
	
	var gov = s_data.governor if s_data.governor else {"personality": "balanced"}
	var personality = gov.get("personality", "balanced")
	
	# 1. EVALUATE PRESSING NEEDS (0 to 100 scale)
	var housing_need = 0
	var pop_cap = s_data.get_housing_capacity()
	if s_data.population > pop_cap * 0.85:
		housing_need = 100 if s_data.population >= pop_cap else 75
		
	var food_stock = s_data.get_food_stock()
	var starvation_need = 0
	if food_stock < s_data.population * 14: # Less than 2 weeks
		starvation_need = 80
		
	var unrest_need = s_data.unrest * 1.5
	var war_need = 0
	for f in GameState.factions:
		if GameState.get_relation(s_data.faction, f.id) == "war":
			war_need = 60
			break
	
	# 2. SCORE ALL POSSIBLE BUILDINGS
	var build_scores = []
	for b_name in GameData.BUILDINGS.keys():
		var current_lvl = s_data.buildings.get(b_name, 0)
		var b_data = GameData.BUILDINGS[b_name]
		
		# Utility check (Geographic potential)
		if not is_building_useful(s_data, b_name): continue
		
		var score = 10.0 # Base score
		
		# PILLAR LOGIC
		match b_name:
			"housing_district": score += housing_need
			"granary": score += starvation_need
			"stone_walls", "barracks", "watchtower": score += war_need
			"tavern", "cathedral": score += unrest_need
			"market", "road_network", "merchant_guild": 
				score += 20 if s_data.type == "city" else 5
			"farm", "lumber_mill", "fishery", "mine", "pasture":
				score += 15 # Extraction is always decent
			"blacksmith", "weaver", "tannery", "brewery", "tailor":
				var input_res = _get_input_resource(b_name)
				if s_data.inventory.get(input_res, 0) > 100:
					score += 25 # Processing is good if we have inputs
		
		# PERSONALITY BIAS
		match personality:
			"builder": if b_name in ["housing_district", "warehouse_district"]: score *= 1.5
			"greedy": if b_name in ["market", "mine", "road_network", "merchant_guild", "goldsmith"]: score *= 2.0
			"balanced": score *= 1.0
			"cautious": if b_name in ["stone_walls", "granary", "watchtower"]: score *= 1.8
		
		# LEVEL PENALTY (Avoid over-specializing a single building too early)
		score /= (1.0 + current_lvl * 0.5) 
		
		build_scores.append({"id": b_name, "score": score})
	
	# Sort by score
	build_scores.sort_custom(func(a, b): return a.score > b.score)
	
	# 3. ATTEMPT TO BUILD TOP PRIORITIES
	for b_entry in build_scores:
		var b_name = b_entry.id
		var current_lvl = s_data.buildings.get(b_name, 0)
		var b_data = GameData.BUILDINGS[b_name]
		
		# POLYNOMIAL COST: Scale = (Level + 1)^2.2
		# Lvl 1: 1x, Lvl 2: 4.6x, Lvl 3: 11x, Lvl 10: ~150x
		var cost_mult = pow(current_lvl + 1, 2.2)
		var actual_cost = int(b_data.get("cost", 500) * cost_mult)
		
		var treasury_buffer = 500 if personality == "cautious" else 100
		if s_data.crown_stock >= actual_cost + treasury_buffer:
			# Execution
			s_data.crown_stock -= actual_cost
			s_data.construction_queue.append({
				"id": b_name,
				"progress": 0,
				"total_labor": int(b_data.get("labor", 500) * cost_mult),
				"resources_met": true
			})
			# GameState.add_log("[GOV] %s building %s (Lvl %d) Score: %.1f" % [s_data.name, b_name, current_lvl + 1, b_entry.score])
			break

static func _get_input_resource(b_name):
	match b_name:
		"blacksmith": return "iron"
		"weaver": return "wool"
		"tannery": return "livestock"
		"brewery": return "grain"
		"tailor": return "cloth"
		"goldsmith": return "gold"
	return "none"

static func is_building_useful(s_data, b_name):
	var r = s_data.radius
	match b_name:
		"farm": return _check_terrain_near(s_data, r, ["."])
		"lumber_mill": return _check_terrain_near(s_data, r, ["#", "&"])
		"fishery": return _check_terrain_near(s_data, r, ["~"])
		"mine": 
			if _check_terrain_near(s_data, r, ["^"]): return true
			for dy in range(-r, r+1):
				for dx in range(-r, r+1):
					var p = s_data.pos + Vector2i(dx, dy)
					if GameState.resources.has(p):
						var res = GameState.resources[p]
						if res in ["iron", "gold", "gems", "stone"]: return true
			return false
		"merchant_guild": return s_data.tier >= 3
		"road_network": return s_data.type != "hamlet"
	return true

static func _check_terrain_near(s_data, r, chars):
	var grid = GameState.grid
	if grid.is_empty(): return false
	var h = grid.size()
	var w = grid[0].size()
	for dy in range(-r, r+1):
		for dx in range(-r, r+1):
			var p = s_data.pos + Vector2i(dx, dy)
			if p.x >= 0 and p.x < w and p.y >= 0 and p.y < h:
				if grid[p.y][p.x] in chars: return true
	return false

static func process_construction(s_data):
	if s_data.construction_queue.is_empty(): return
	if GameState.hour < 6 or GameState.hour > 18: return # No night work
	
	var project = s_data.construction_queue[0]
	# CROWNS-ONLY: We assume materials are handled at the time of the crown purchase
	
	var labor = max(1, int(s_data.population / 100.0 * (s_data.happiness / 100.0)))
	project["progress"] += labor
	if project["progress"] >= project["total_labor"]:
		s_data.buildings[project["id"]] = s_data.buildings.get(project["id"], 0) + 1
		s_data.construction_queue.pop_front()
		EconomyManager.recalculate_production(s_data, GameState.grid, GameState.resources, GameState.geology)

static func check_promotions(s_data):
	if s_data.type == "hamlet" and s_data.stability >= Globals.HAMLET_PROMOTION_STABILITY:
		_promote_hamlet_to_village(s_data)
	elif s_data.type == "village" and s_data.population >= Globals.VILLAGE_PROMOTION_POP:
		_promote_village_to_city(s_data)

static func _promote_hamlet_to_village(s_data):
	s_data.type = "village"
	s_data.tier = 2
	s_data.radius = 2
	s_data.max_slots = 2
	s_data.stability = 50
	s_data.population = max(s_data.population, 100)
	s_data.crown_stock += 500
	s_data.houses = 40
	s_data.name = s_data.name.replace(" Hamlet", "")
	GameState.grid[s_data.pos.y][s_data.pos.x] = "v"
	EconomyManager.recalculate_production(s_data, GameState.grid, GameState.resources, GameState.geology)
	GameState.add_log("%s has been promoted to a Village!" % s_data.name)

static func _promote_village_to_city(s_data):
	s_data.type = "city"
	s_data.tier = 4
	s_data.radius = 4
	s_data.max_slots = 6
	GameState.grid[s_data.pos.y][s_data.pos.x] = "C"
	GameState.add_log("%s has grown into a City!" % s_data.name)

static func check_city_expansion(s_pos, gs):
	var s_data = gs.settlements[s_pos]
	if not s_data.type in ["city", "castle"]: return
	
	if s_data.crown_stock < 1000 or s_data.inventory.get("grain", 0) < 100: return
	
	var hamlet_count = 0
	for other_pos in gs.settlements:
		var other = gs.settlements[other_pos]
		if other.parent_city == s_pos:
			hamlet_count += 1
	
	if hamlet_count >= 5: 
		if s_data.crown_stock >= 5000:
			spawn_settler_party(s_pos, gs)
		return
	
	var best_score = -999
	var best_pos = Vector2i.ZERO
	var best_type = ""
	
	for dy in range(-8, 9):
		for dx in range(-8, 9):
			var check_pos = s_pos + Vector2i(dx, dy)
			if not gs.is_in_bounds(check_pos) or gs.settlements.has(check_pos): continue
			
			var score_data = score_tile_for_hamlet(check_pos, s_pos, gs)
			if score_data["score"] > best_score:
				best_score = score_data["score"]
				best_pos = check_pos
				best_type = score_data["type"]
	
	if best_pos != Vector2i.ZERO and best_score > 20:
		s_data.crown_stock -= 1000
		var h_data = GDSettlement.new(best_pos)
		h_data.name = "%s %s" % [s_data.name, best_type.replace("_", " ").capitalize()]
		h_data.type = best_type
		h_data.tier = 1
		h_data.max_slots = 1
		h_data.faction = s_data.faction
		h_data.parent_city = s_pos
		h_data.population = 50
		h_data.inventory = {"grain": 10}
		h_data.crown_stock = 100
		h_data.loyalty = 50
		h_data.happiness = 70
		h_data.unrest = 0
		h_data.stability = 50
		h_data.garrison = 5
		h_data.radius = 1
		h_data.houses = 20
		h_data.house_progress = 0.0
		h_data.buildings = {}
		
		gs.settlements[best_pos] = h_data
		
		if best_type == "mining_hamlet": h_data.buildings["mine"] = 1
		elif best_type == "lumber_hamlet": h_data.buildings["lumber_mill"] = 1
		elif best_type == "fishing_hamlet": h_data.buildings["fishery"] = 1
		elif best_type == "pasture_hamlet": h_data.buildings["pasture"] = 1
		else: h_data.buildings["farm"] = 1
		
		EconomyManager.recalculate_production(h_data, gs.grid, gs.resources, gs.geology)
		gs.add_log("%s has established a new %s at %v." % [s_data.name, best_type.replace("_", " "), best_pos])

static func finalize_player_settlement(gs, pos, type):
	var h_data = GDSettlement.new(pos)
	h_data.name = "%s's Stand" % gs.player.commander.name
	h_data.type = type
	h_data.tier = 1
	h_data.max_slots = 1
	h_data.faction = gs.player.faction
	h_data.lord_id = gs.player.id
	h_data.population = 50
	h_data.inventory = {"grain": 25}
	h_data.crown_stock = 500
	h_data.loyalty = 100 # High since founded by player
	h_data.stability = 50
	h_data.radius = 1
	h_data.houses = 20
	h_data.buildings = {}
	
	if type == "mining_hamlet": h_data.buildings["mine"] = 1
	elif type == "lumber_hamlet": h_data.buildings["lumber_mill"] = 1
	elif type == "fishing_hamlet": h_data.buildings["fishery"] = 1
	elif type == "pasture_hamlet": h_data.buildings["pasture"] = 1
	else: h_data.buildings["farm"] = 1

	gs.settlements[pos] = h_data
	gs.player.fief_ids.append(pos)
	gs.add_history_event("The pioneering expedition led by %s founded the settlement of %s at (%d, %d)." % [gs.player.commander.name, h_data.name, pos.x, pos.y])
	
	# Update tile on map
	gs.grid[pos.y][pos.x] = "o" # Hamlet symbol
	
	EconomyManager.recalculate_production(h_data, gs.grid, gs.resources, gs.geology)
	gs.add_log("[color=green]CONSTRUCTION COMPLETE: Your people have finished building %s![/color]" % h_data.name)
	gs.emit_signal("map_updated")

static func score_tile_for_hamlet(pos, parent_pos, gs):
	var t = gs.grid[pos.y][pos.x]
	var score = 0
	var type = "farming_hamlet"
	
	if t == '^': 
		score = 50
		type = "mining_hamlet"
	elif t == '#': 
		score = 30
		type = "lumber_hamlet"
	elif t == '.': 
		if gs.rng.randf() < 0.5:
			score = 40
			type = "farming_hamlet"
		else:
			score = 40
			type = "pasture_hamlet"
	else:
		return {"score": -100, "type": ""}
		
	var dist = pos.distance_to(parent_pos)
	score -= int(dist * 2)
	
	return {"score": score, "type": type}

static func spawn_settler_party(s_pos, gs):
	var s_data = gs.settlements[s_pos]
	s_data.crown_stock -= 5000
	
	var target_pos = find_prime_location(s_pos, gs)
	if target_pos == Vector2i.ZERO: return
	
	var a = GDArmy.new(s_pos, s_data.faction)
	a.type = "settler_party"
	a.target_pos = target_pos
	a.name = "Settlers from %s" % s_data.name
	for i in range(30):
		a.roster.append({"hp": 10, "hp_max": 10}) 
	

	gs.armies.append(a)
	gs.add_log("A Settler Party has departed from %s to found a new colony!" % s_data.name)

static func find_prime_location(origin, gs):
	for attempt in range(50):
		var rx = gs.rng.randi_range(20, gs.width - 20)
		var ry = gs.rng.randi_range(20, gs.height - 20)
		var pos = Vector2i(rx, ry)
		
		if pos.distance_to(origin) < 40: continue
		
		var too_close = false
		for s_pos in gs.settlements:
			if pos.distance_to(s_pos) < 25:
				too_close = true
				break
		if too_close: continue
		
		if gs.grid[pos.y][pos.x] == '.':
			return pos
	return Vector2i.ZERO

static func process_migration(gs):
	for pos in gs.settlements:
		var s = gs.settlements[pos]
		var pop = s.population
		var cap = s.get_housing_capacity()
		
		var migrants_count = 0
		
		if pop > cap:
			migrants_count += int((pop - cap) * 0.2) + 2 # Faster migration out of overcrowded cities
		if s.happiness < 40:
			migrants_count += int(pop * 0.05) + 1 
			
		if migrants_count <= 0: continue
		
		var targets = []
		if s.parent_city != Vector2i(-1, -1): targets.append(s.parent_city)
		for p in gs.settlements:
			if gs.settlements[p].parent_city == pos: targets.append(p)
			
		var best_target = null
		for t_pos in targets:
			var t = gs.settlements.get(t_pos)
			if not t: continue
			
			var t_pop = t.population
			var t_cap = t.get_housing_capacity()
			var t_hap = t.happiness
			
			if t_pop < t_cap: 
				if best_target == null or t_hap > gs.settlements[best_target].happiness:
					best_target = t_pos
		
		if best_target:
			var actual = min(migrants_count, pop - 10) 
			s.population -= actual
			s.migration_buffer += actual
			gs.track_migration(actual)
			
			if s.migration_buffer >= 15:
				var path = gs.astar.get_id_path(pos, best_target)
				if not path.is_empty():
					path.remove_at(0) # Remove current position
				
				gs.migrants.append({
					"pos": pos,
					"target": best_target,
					"amount": s.migration_buffer,
					"faction": s.faction,
					"origin": pos,
					"path": path
				})
				
				if pos.distance_to(gs.player.pos) < 30:
					gs.add_log("A large group of %d migrants has left %s for %s." % [s.migration_buffer, s.name, gs.settlements[best_target].name])
				
				s.migration_buffer = 0

static func hire_recruit(gs, s_pos, pool_idx):
	var s = gs.settlements[s_pos]
	var pool = s.recruit_pool
	if pool_idx < 0 or pool_idx >= pool.size(): return
	
	var recruit = pool[pool_idx]
	if gs.player.crowns >= recruit.cost:
		gs.player.crowns -= recruit.cost
		pool.remove_at(pool_idx)
		gs.player.add_to_roster(recruit)
		gs.add_log("Hired %s for %d Crowns." % [recruit.name, recruit.cost])
		gs.emit_signal("map_updated")
	else:
		gs.add_log("Not enough crowns to hire %s (Need %dc)." % [recruit.name, recruit.cost])

static func recruit_prisoner(gs, idx):
	if idx < 0 or idx >= gs.player.prisoners.size(): return
	
	var p = gs.player.prisoners[idx]
	var fee = int(p.cost * 0.5) # 50% fee to recruit
	
	if gs.player.crowns >= fee:
		gs.player.crowns -= fee
		gs.player.prisoners.remove_at(idx)
		gs.player.add_to_roster(p)
		gs.add_log("Recruited prisoner %s for %d Crowns." % [p.name, fee])
		gs.emit_signal("map_updated")
	else:
		gs.add_log("Not enough crowns to recruit %s (Need %dc)." % [p.name, fee])

static func ransom_prisoner(gs, idx):
	if idx < 0 or idx >= gs.player.prisoners.size(): return
	
	var p = gs.player.prisoners[idx]
	var ransom_value = int(p.cost * 0.3) # 30% of value as ransom
	
	gs.player.crowns += ransom_value
	gs.player.prisoners.remove_at(idx)
	gs.add_log("Ransomed prisoner %s for %d Crowns." % [p.name, ransom_value])
	gs.emit_signal("map_updated")

static func sponsor_building(gs, s_pos, b_name):
	var s = gs.settlements[s_pos]
	if not GameData.BUILDINGS.has(b_name): return
	var b_data = GameData.BUILDINGS[b_name]
	
	if not is_building_useful(s, b_name):
		gs.add_log("The terrain around %s is not suitable for a %s." % [s.name, b_name.capitalize()])
		return
	
	# Check building tier
	var s_tier = s.tier
	var b_tier = b_data.get("tier", 1)
	if b_tier > s_tier:
		gs.add_log("%s is too small for a %s (Requires Tier %d)." % [s.name, b_name.capitalize(), b_tier])
		return
	
	# Check building slots
	var buildings = s.buildings
	var queue = s.construction_queue
	var current_slots = buildings.size() + queue.size()
	var max_slots = s.max_slots
	if current_slots >= max_slots and not buildings.has(b_name):
		gs.add_log("%s has no more building slots available (Tier %d: %d slots)." % [s.name, s_tier, max_slots])
		return
	
	var current_lvl = buildings.get(b_name, 0)
	if current_lvl >= 5:
		gs.add_log("%s is already at maximum level." % b_name.capitalize())
		return
		
	var cost_mult = 1.0 + (current_lvl * 0.5)
	var actual_cost = int(b_data.get("cost", 1000) * cost_mult)
	
	if gs.player.crowns < actual_cost:
		gs.add_log("Not enough Crowns to sponsor %s." % b_name)
		return
		
	# Check if already in queue
	for q in queue:
		if q["id"] == b_name: return
		
	gs.player.crowns -= actual_cost
	s.construction_queue.append({
		"id": b_name,
		"progress": 0,
		"total_labor": int(b_data.get("labor", 100) * cost_mult),
		"wood_needed": int(b_data.get("wood", 0) * cost_mult),
		"stone_needed": int(b_data.get("stone", 0) * cost_mult),
		"iron_needed": int(b_data.get("iron", 0) * cost_mult),
		"resources_met": false,
		"sponsored": true
	})
	
	# Gain influence
	if not s.influence.has("player"): s.influence["player"] = 0
	s.influence["player"] += 10
	
	gs.add_log("You sponsored a %s in %s for %d Crowns." % [b_name.capitalize(), s.name, actual_cost])

static func donate_resource(gs, s_pos, res, amount):
	var s = gs.settlements[s_pos]
	if gs.player.inventory.get(res, 0) < amount: 
		gs.add_log("You don't have enough %s." % res)
		return
	
	if amount > 0:
		gs.player.inventory[res] -= amount
		s.inventory[res] = s.inventory.get(res, 0) + amount
	
	# Gain influence
	if not s.influence.has("player"): s.influence["player"] = 0
	s.influence["player"] += amount / 10.0
	gs.add_log("You donated %d %s to %s." % [amount, res, s.name])

static func process_logistics_ai(s_data):
	# STRATEGY 4: SHIPPING HUBS (Only Tier 3+ Towns/Cities/Castles maintain physical caravans)
	if s_data.type not in ["city", "castle", "town"] and s_data.tier < 3: return
	if not s_data.buildings.has("merchant_guild"): return
	
	var gs = GameState
	var guild_lvl = s_data.buildings.get("merchant_guild", 0)
	
	# STRATEGY 1: CAPACITY OVER QUANTITY (Limit to 1 Super-Caravan per hub)
	var max_caravans = 1 
	
	var current_caravans = 0
	for c in gs.caravans:
		if c.faction == s_data.faction and c.origin == s_data.pos:
			current_caravans += 1
			
	if current_caravans < max_caravans:
		var has_wood = s_data.inventory.get("wood", 0) >= Globals.CARAVAN_BUILD_COST_WOOD * 2
		var has_horses = s_data.inventory.get("horses", 0) >= Globals.CARAVAN_BUILD_COST_HORSES * 2
		var has_cash = s_data.crown_stock >= Globals.CARAVAN_BUILD_COST_CROWNS * 2
		
		# EMERGENCY RECONSTRUCTION
		var is_emergency = (current_caravans == 0)
		
		if is_emergency or (has_wood and has_horses and has_cash):
			if not is_emergency:
				s_data.inventory["wood"] -= Globals.CARAVAN_BUILD_COST_WOOD * 2
				s_data.inventory["horses"] -= Globals.CARAVAN_BUILD_COST_HORSES * 2
				s_data.crown_stock -= Globals.CARAVAN_BUILD_COST_CROWNS * 2
			
			var new_cav = GDCaravan.new(s_data.pos, s_data.faction)
			new_cav.origin = s_data.pos
			
			# Give Caravan starting capital from the city treasury
			var seed_money = min(5000, s_data.crown_stock / 2)
			s_data.crown_stock -= seed_money
			new_cav.crowns = seed_money
			
			# Super-Caravan Guarding
			var guard_count = 50 + (guild_lvl * 20)
			var avg_tier = clamp(s_data.tier, 3, 5)
			for j in range(guard_count):
				var tier = avg_tier if gs.rng.randf() < 0.3 else avg_tier - 1
				new_cav.roster.append(GameData.generate_recruit(gs.rng, clamp(tier, 2, 5)))
				
			gs.caravans.append(new_cav)
			gs.add_log("The Merchant Guild of %s has deployed a Heavy Caravan (%d guards)." % [s_data.name, guard_count])

static func refresh_npcs(s_data):
	if s_data.npcs.size() > 0: return # Already populated for now
	
	var gs = GameState
	var type = s_data.type
	
	# Common names generator
	var first_names = ["Alden", "Beatrice", "Cedric", "Daphne", "Edmund", "Freyja", "Gunnar", "Hilda", "Ivor", "Jocelyn", "Kendric", "Leif", "Morgaine", "Niall", "Osric", "Pia", "Quentin", "Rowena", "Sigurd", "Thora"]
	var last_names = ["Blackwood", "Ironfoot", "Stormborn", "Rivers", "Oakheart", "Highlander", "Frost", "Shadowstep", "Goldweaver", "Stonefist"]
	
	var generate_name = func():
		return first_names[gs.rng.randi() % first_names.size()] + " " + last_names[gs.rng.randi() % last_names.size()]
	
	var create_npc = func(npc_name, title):
		var id = "npc_%d_%d" % [gs.rng.randi(), gs.turn]
		var npc = GDNPC.new(id, npc_name, title, s_data.pos, s_data.faction)
		return npc

	# Settlement-specific NPCs
	if type == "hamlet" or type == "village":
		var elder = create_npc.call(generate_name.call(), "Village Elder")
		s_data.npcs.append(elder)
		s_data.npcs.append(create_npc.call(generate_name.call(), "Local Priest"))
		s_data.lord_id = elder.id
	elif type == "town" or type == "city" or type == "metropolis":
		var gov = create_npc.call(generate_name.call(), "Governor")
		s_data.npcs.append(gov)
		s_data.npcs.append(create_npc.call(generate_name.call(), "Guildmaster"))
		s_data.npcs.append(create_npc.call(generate_name.call(), "Market Overseer"))
		s_data.lord_id = gov.id
		
		if s_data.is_capital:
			gov.title = "King"
			var f_data = gs.get_faction(s_data.faction)
			if f_data: f_data.king_id = gov.id
	elif type == "castle":
		var lord = create_npc.call(generate_name.call(), "Lord")
		s_data.npcs.append(lord)
		s_data.npcs.append(create_npc.call(generate_name.call(), "Castellan"))
		s_data.npcs.append(create_npc.call(generate_name.call(), "Captain of the Guard"))
		s_data.lord_id = lord.id
		
		# If this is a capital, this lord is the King
		if s_data.is_capital:
			lord.title = "King"
			var f_data = gs.get_faction(s_data.faction)
			if f_data: f_data.king_id = lord.id
	
	# Register true Nobility as Vassals
	if s_data.lord_id != "" and type == "castle":
		var f_data = gs.get_faction(s_data.faction)
		if f_data:
			if not s_data.lord_id in f_data.vassal_ids:
				f_data.vassal_ids.append(s_data.lord_id)
		
		# Link NPC to any existing Lord Army at this position
		for a in gs.armies:
			if a.type == "lord" and a.pos == s_data.pos and a.lord_id == "":
				a.lord_id = s_data.lord_id
				var npc = gs.find_npc(s_data.lord_id)
				if npc:
					a.name = "%s %s's Party" % [npc.title, npc.name]
				break
	elif s_data.lord_id != "" and s_data.is_capital:
		# Kings are always recorded as vassals/leaders even if in a City
		var f_data = gs.get_faction(s_data.faction)
		if f_data and not s_data.lord_id in f_data.vassal_ids:
			f_data.vassal_ids.append(s_data.lord_id)

static func refresh_quests(s_data):
	var gs = GameState
	for npc in s_data.npcs:
		# Randomly generate a quest if they don't have one
		if npc.quests.size() < 1 and gs.rng.randf() < 0.3:
			generate_random_quest(s_data, npc)

static func generate_random_quest(s_data, npc):
	var gs = GameState
	var q_title = ""
	var q_type = GDQuest.Type.FETCH
	
	# Determine quest type based on NPC title
	if npc.title == "Village Elder":
		if s_data.get_food_stock() < s_data.population * 7:
			q_title = "Procure Emergency Grain"
			q_type = GDQuest.Type.FETCH
		else:
			# 50/50 between Ruin or Roaming Bandits
			if gs.rng.randf() < 0.5:
				q_title = "Clear Nearby Bandits"
				q_type = GDQuest.Type.EXTERMINATE
			else:
				q_title = "Hunt Bandit Party"
				q_type = GDQuest.Type.EXTERMINATE
	elif npc.title == "Governor":
		q_title = "Resource Delivery"
		q_type = GDQuest.Type.DELIVERY
	elif npc.title == "Captain of the Guard":
		q_title = "Scout Dark Ruin"
		q_type = GDQuest.Type.EXTERMINATE
	else:
		q_title = "Minor Task"
		q_type = GDQuest.Type.FETCH
		
	var q = GDQuest.new(q_title, q_type, s_data.pos)
	
	# Configure specific data
	if q_type == GDQuest.Type.FETCH:
		q.objective_data = {"resource": "grain", "amount": 20 + (gs.rng.randi() % 30)}
		q.description = "We are running low on supplies. Please bring us %d %s." % [q.objective_data.amount, q.objective_data.resource]
		q.rewards.crowns = q.objective_data.amount * 10
	elif q_type == GDQuest.Type.EXTERMINATE:
		if q_title == "Hunt Bandit Party":
			# Find a nearby bandit army
			var target_army = null
			for a in gs.armies:
				if a.faction == "bandits" and s_data.pos.distance_to(a.pos) < 20:
					target_army = a
					break
			
			if target_army:
				q.target_pos = target_army.pos
				q.objective_data = {"target_id": target_army.get_instance_id(), "type": "army"}
				q.description = "A group of bandits is harassing our borders. Track them down and eliminate them."
				q.rewards.crowns = 300
				q.rewards.influence = 3
			else:
				# Fallback to ruin if no roaming bandits found
				q_title = "Clear Nearby Bandits"
		
		if q_title == "Clear Nearby Bandits" or q_title == "Scout Dark Ruin":
			# Find a nearby ruin
			var nearest_ruin = Vector2i(-1, -1)
			var min_dist = 999
			for r_pos in gs.ruins:
				var d = s_data.pos.distance_to(r_pos)
				if d < min_dist and d > 0:
					min_dist = d
					nearest_ruin = r_pos
		
			if nearest_ruin != Vector2i(-1, -1):
				q.target_pos = nearest_ruin
				q.description = "There is a dark presence at the nearby ruins. Cleanse it for us."
				q.rewards.crowns = 500
				q.rewards.influence = 5
			else:
				return # No ruin found
			
	npc.quests.append(q)
