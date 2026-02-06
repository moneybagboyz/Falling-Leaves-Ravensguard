@warning_ignore("shadowed_global_identifier")
class_name AIManager
extends Node

@warning_ignore("shadowed_global_identifier")
static func process_movement(gs):
	# 1. Process Armies (Bandits & Lords)
	# We iterate backwards so we can remove destroyed armies safely
	for i in range(gs.armies.size() - 1, -1, -1):
		var army_obj = gs.armies[i]
		if army_obj.is_in_battle: continue
		
		# Throttle: Only update 1/2 of armies per turn to save CPU
		if (gs.turn + i) % 2 != 0: continue

		if army_obj.type == "bandit":
			# Bandits seek nearby caravans but avoid strong foes
			var found_prey = null
			var best_dist = 15
			
			# OPTIMIZATION: Use spatial hash
			var nearby = gs.get_entities_near(army_obj.pos, best_dist)
			for e in nearby:
				if "type" in e and e.type == "caravan":
					var d = max(abs(army_obj.pos.x - e.pos.x), abs(army_obj.pos.y - e.pos.y))
					if d < best_dist:
						best_dist = d
						found_prey = e
			
			if found_prey:
				# Distance already computed during best_dist calculation, use it
				if best_dist < 1.2:
					gs.resolve_ai_battle(army_obj, found_prey)
				else:
					var dir = (found_prey.pos - army_obj.pos).sign()
					var next = army_obj.pos + dir
					if gs.is_walkable(next):
						army_obj.pos = next
			else:
				var move = Vector2i(gs.rng.randi_range(-1, 1), gs.rng.randi_range(-1, 1))
				var new_pos = army_obj.pos + move
				if gs.is_walkable(new_pos):
					army_obj.pos = new_pos
				
		elif army_obj.type == "settler_party":
			# Move towards target
			var dir = (army_obj.target_pos - army_obj.pos).sign()
			var next = army_obj.pos + dir
			if gs.is_walkable(next):
				army_obj.pos = next
			
			# Arrived?
			if army_obj.pos.distance_to(army_obj.target_pos) < 1.5:
				var s_data = GDSettlement.new(army_obj.pos)
				s_data.name = "New %s" % gs.get_faction(army_obj.faction).name
				s_data.type = "village"
				s_data.tier = 2
				s_data.max_slots = 2
				s_data.faction = army_obj.faction
				s_data.population = 100
				s_data.garrison = 10
				s_data.radius = 2
				s_data.inventory = {"grain": 100, "wood": 50, "stone": 20}
				s_data.crown_stock = 500
				s_data.loyalty = 50
				s_data.happiness = 70
				s_data.unrest = 0
				s_data.stability = 50
				s_data.houses = 20
				s_data.house_progress = 0.0
				s_data.buildings = {"farm": 1}
				s_data.governor = {
					"personality": GameData.GOVERNOR_PERSONALITIES[gs.rng.randi() % GameData.GOVERNOR_PERSONALITIES.size()],
					"name": "%s %s" % [
						GameData.FIRST_NAMES[gs.rng.randi() % GameData.FIRST_NAMES.size()],
						GameData.LAST_NAMES[gs.rng.randi() % GameData.LAST_NAMES.size()]
					]
				}
				gs.settlements[army_obj.pos] = s_data
				gs.grid[army_obj.pos.y][army_obj.pos.x] = "v"
				EconomyManager.recalculate_production(s_data, gs.grid, gs.resources, gs.geology)
				gs.add_log("A new colony has been founded at %v by %s!" % [army_obj.pos, gs.get_faction(army_obj.faction).name])
				gs.armies.erase(army_obj)
				continue

		elif army_obj.type == "lord":
			if army_obj.respawn_timer > 0:
				army_obj.respawn_timer -= 1
				if army_obj.respawn_timer <= 0:
					army_obj.roster = []
					var home_pos = army_obj.home_fief if army_obj.home_fief != Vector2i(-1, -1) else army_obj.pos
					var home = gs.settlements.get(home_pos)
					var h_tier = home.tier if home else 1
					for j in range(20):
						army_obj.roster.append(GameData.generate_recruit(gs.rng, clamp(h_tier, 1, 4)))
					army_obj.pos = home_pos
					if gs.settlements.has(army_obj.pos):
						gs.add_log("Lord %s has returned to the field at %s." % [army_obj.name, gs.settlements[army_obj.pos].name])
				continue

			if not army_obj.cached_target or gs.turn % 5 == 0:
				army_obj.cached_target = AIManager.decide_army_target(army_obj, gs)
			
			var target = army_obj.cached_target
			if target:
				var target_pos = target.get("pos", army_obj.pos)
				var dist = army_obj.pos.distance_to(target_pos)
				
				if dist <= 1.5:
					var type = target.get("type")
					if type == "recruitment":
						var s_data = target.get("data")
						if s_data and s_data.crown_stock >= 500:
							s_data.crown_stock -= 500
							
							var barracks_lvl = s_data.buildings.get("barracks", 0)
							var training_lvl = s_data.buildings.get("training_ground", 0)
							var count = 10 + (barracks_lvl * 5)
							var tier_bonus = int(training_lvl / 2.0)
							
							for j in range(count):
								army_obj.roster.append(GameData.generate_recruit(gs.rng, clamp(s_data.tier + tier_bonus, 1, 5)))
					elif type in ["intercept", "attack"]:
						gs.resolve_ai_battle(army_obj, target.get("data"))
						army_obj.cached_target = null
					elif type == "raid":
						gs.resolve_ai_battle(army_obj, target.get("data"))
						army_obj.cached_target = null
					elif type == "siege":
						gs.resolve_siege(army_obj, target.get("data"), target_pos)
						army_obj.cached_target = null
					elif type == "player":
						# Trigger battle with player (Main logic handles this elsewhere usually, but for AI vs Player:)
						gs.start_battle(army_obj)
						army_obj.cached_target = null
					else:
						# Generic destination reached (wander, patrol, etc)
						army_obj.cached_target = null
				else:
					var next = army_obj.pos + (target_pos - army_obj.pos).sign()
					if gs.is_walkable(next):
						army_obj.pos = next
			else:
				# Patrol Home Fief or Wander
				var center = army_obj.home_fief if army_obj.home_fief != Vector2i(-1, -1) else army_obj.pos
				if army_obj.pos.distance_to(center) > 10:
					# Return to patrol zone
					var dir = (center - army_obj.pos).sign()
					var next = army_obj.pos + dir
					if gs.is_walkable(next): army_obj.pos = next
				else:
					# Random patrol move
					var move = Vector2i(gs.rng.randi_range(-1, 1), gs.rng.randi_range(-1, 1))
					var new_pos = army_obj.pos + move
					if gs.is_walkable(new_pos) and new_pos.distance_to(center) <= 10:
						army_obj.pos = new_pos

	# 2. Caravans
	for i in range(gs.caravans.size()):
		var caravan_obj = gs.caravans[i]
		if caravan_obj.is_in_battle: continue

		if caravan_obj.respawn_timer > 0:
			caravan_obj.respawn_timer -= 1
			if caravan_obj.respawn_timer <= 0:
				var s_data = gs.settlements.get(caravan_obj.origin)
				if s_data:
					var guild_lvl = s_data.buildings.get("merchant_guild", 0)
					var guard_count = 30 + (guild_lvl * 10)
					var avg_tier = clamp(s_data.tier, 2, 4)
					for j in range(guard_count):
						var tier = avg_tier if gs.rng.randf() < 0.3 else avg_tier - 1
						caravan_obj.roster.append(GameData.generate_recruit(gs.rng, clamp(tier, 1, 4)))
					gs.add_log("A caravan from %s has finished repairs and is back in service." % s_data.name)
			continue

		# Throttle: Only update caravans based on Road Network
		var origin_s = gs.settlements.get(caravan_obj.origin)
		var road_lvl = origin_s.buildings.get("road_network", 0) if origin_s else 0
		
		# Base speed: Every 2nd turn. Level 1: Every turn (Double speed). Level 2+: Chance for 2 tiles/turn.
		var tick_chance = 0.5 + (road_lvl * 0.5)
		if gs.rng.randf() > tick_chance:
			if (gs.turn + i) % 2 != 0: continue

		# STRATEGY 3: STAGGERED UPDATING
		# High-cost AI logic (Route finding, Enemy Avoidance) only runs every 4 turns
		var update_this_turn = (gs.turn + i) % 4 == 0

		if caravan_obj.state == "idle" and update_this_turn:
			var route = AIManager.find_trade_route(gs, caravan_obj)
			if not route.is_empty():
				caravan_obj.target_pos = route["buy_pos"]
				caravan_obj.target_resource = route["resource"]
				caravan_obj.state = "buying"
				caravan_obj.final_destination = route["sell_pos"]
		
		if caravan_obj.target_pos != Vector2i(-1, -1):
			# AVOIDANCE: Only check for enemies every 4 turns to save performance
			if update_this_turn:
				# Use spatial hash instead of iterating all armies
				var nearby_threats = gs.get_entities_near(caravan_obj.pos, 8)
				var enemy_nearby = false
				for e in nearby_threats:
					if e is GDArmy and e.type == "bandit":
						enemy_nearby = true
						break
				
				if enemy_nearby:
				var enemies = []
				for army in gs.armies:
					if gs.get_relation(caravan_obj.faction, army.faction) == "war":
						if caravan_obj.pos.distance_to(army.pos) < 8:
							enemies.append(army)
				
				if not enemies.is_empty():
				# Pre-compute distances once to avoid redundant calculations during sort
				var enemy_distances = enemies.map(func(e): return {"army": e, "dist": caravan_obj.pos.distance_to(e.pos)})
				enemy_distances.sort_custom(func(a, b): return a.dist < b.dist)
				var e = enemy_distances[0].army
					var run_dir = (caravan_obj.pos - e.pos).sign()
					if run_dir == Vector2i.ZERO: run_dir = Vector2i(1, 0)
					var run_pos = caravan_obj.pos + run_dir
					if gs.is_walkable(run_pos):
						caravan_obj.pos = run_pos
						caravan_obj.path = [] # Force path recalc
						continue

			# If no path or target changed, calculate path
			if caravan_obj.path.is_empty() or caravan_obj.path[caravan_obj.path.size()-1] != caravan_obj.target_pos:
				caravan_obj.path = gs.astar.get_id_path(caravan_obj.pos, caravan_obj.target_pos)
				if caravan_obj.path.size() > 0:
					caravan_obj.path.remove_at(0) # Remove current position
			
			if not caravan_obj.path.is_empty():
				var next = caravan_obj.path[0]
				if gs.is_walkable(next):
					caravan_obj.pos = next
					caravan_obj.path.remove_at(0)
				else:
					# Path blocked? Recalculate next turn
					caravan_obj.path = []
			
			# Arrived?
			if caravan_obj.pos.distance_to(caravan_obj.target_pos) < 1.5:
				caravan_obj.path = []
				EconomyManager.resolve_caravan_trade(gs, caravan_obj)

	# Process arrival of pulses
	for i in range(gs.logistical_pulses.size() - 1, -1, -1):
		var p = gs.logistical_pulses[i]
		if gs.turn >= p.arrival_turn:
			var city = gs.settlements.get(p.target_pos)
			if city:
				city.inventory[p.resource] = city.inventory.get(p.resource, 0) + p.amount
				gs.track_logistical_pulse("delivered")
				var hamlet = gs.settlements.get(p.origin)
				if hamlet:
					hamlet.stability += 1
					SettlementManager.check_promotions(hamlet)
			else:
				gs.track_logistical_pulse("dropped")
			gs.logistical_pulses.remove_at(i)

	# Process Migrant Movement (Keep physical for now as they are rarer)
	for i in range(gs.migrants.size() - 1, -1, -1):
		var m = gs.migrants[i]
		
		# Intentional Movement: Follow pre-calculated path if available
		var next = Vector2i.ZERO
		if m.has("path") and not m["path"].is_empty():
			next = m["path"][0]
			m["path"].remove_at(0)
		else:
			# Fallback for old migrants or direct line if path blocked
			var dir = (m["target"] - m["pos"]).sign()
			next = m["pos"] + dir
			
		if gs.is_walkable(next):
			m["pos"] = next
		
		# Arrived at Destination?
		if m["pos"].distance_to(m["target"]) < 1.5:
			var target_s = gs.settlements.get(m["target"])
			if target_s:
				# Target reached: check if they are still accepting people
				var target_pop = target_s.population
				var ideal = target_s.get_housing_capacity()
				var cap = target_s.get_housing_capacity()
				
				if target_pop < ideal or target_pop < cap:
					target_s.population += m["amount"]
					if m["target"].distance_to(gs.player.pos) < 30:
						gs.add_log("A group of %d migrants has arrived at %s." % [m["amount"], target_s.name])
					gs.migrants.remove_at(i)
				else:
					# Turned away! Settlement is at sustainable capacity.
					# They will wander randomly until they find a vacancy or starve (not yet implemented)
					var move = Vector2i(gs.rng.randi_range(-3, 3), gs.rng.randi_range(-3, 3))
					m["pos"] += move
					# Search for a new nearby target
					var best_dist = 9999
					var next_target = Vector2i(-1, -1)
					for s_pos in gs.settlements:
						var s = gs.settlements[s_pos]
						if s.faction == m["faction"] and s.population < s.get_housing_capacity():
							var d = m["pos"].distance_to(s_pos)
							if d < best_dist:
								best_dist = d
								next_target = s_pos
					
					if next_target != Vector2i(-1, -1):
						m["target"] = next_target
						m["path"] = gs.astar.get_id_path(m["pos"], next_target)
						if not m["path"].is_empty(): m["path"].remove_at(0)
					else:
						# No factory jobs? They force their way in but settlement is unhappy.
						target_s.population += m["amount"]
						target_s.happiness = max(0, target_s.happiness - 10)
						gs.migrants.remove_at(i)
			else:
				gs.migrants.remove_at(i)
		
		# Fallback: Migrants who wander too long or get lost
		if i < gs.migrants.size(): # Check index again as it might have been removed
			var fallback_m = gs.migrants[i]
			if GameState.turn % 100 == 0:
				fallback_m["amount"] -= 1 # Starvation/attrition on the road
				if fallback_m["amount"] <= 0:
					gs.migrants.remove_at(i)
					continue


@warning_ignore("shadowed_global_identifier")
static func explore_ruin(gs, pos: Vector2i):
	if not gs.ruins.has(pos): return
	var r = gs.ruins[pos]
	if r["explored"] and not r.has("floor_data"):
		gs.add_log("This place has already been picked clean.")
		return
	
	gs.dungeon_started.emit(r)

@warning_ignore("shadowed_global_identifier")
static func _reward_ruin(gs, pos: Vector2i):
	if not gs.ruins.has(pos): return
	var r = gs.ruins[pos]
	var crown_loot = r["loot_quality"] * gs.rng.randi_range(50, 150)
	gs.player.crowns += crown_loot
	
	var item_count = gs.rng.randi_range(1, r["loot_quality"])
	var types = []
	for k in GameData.ITEMS:
		if GameData.ITEMS[k].has("name") and k not in ["fist", "bite"]:
			types.append(k)
			
	var mats = ["iron", "steel", "bronze", "leather", "wool", "linen"]
	
	for i in range(item_count):
		var type = types[gs.rng.randi() % types.size()]
		var mat = mats[gs.rng.randi() % mats.size()]
			
		var qual = "standard"
		if r["loot_quality"] >= 5: qual = "masterwork"
		elif r["loot_quality"] >= 3: qual = "fine"
		
		var item = gs.create_item(type, mat, qual)
		if item:
			gs.player.stash.append(item)
	
	r["explored"] = true
	gs.add_log("Found %d Crowns and %d items in the ruins." % [crown_loot, item_count])
	gs.emit_signal("map_updated")

@warning_ignore("shadowed_global_identifier")
static func find_trade_route(gs, caravan_obj):
	# PULL MODEL: Fulfill World Market Orders
	if gs.world_market_orders.is_empty(): 
		# Fallback to legacy random sniffing for basic profit if no orders
		return _find_random_trade(gs, caravan_obj)
	
	var best_order_idx = -1
	var best_seller_pos = Vector2i.ZERO
	var best_score = -1000.0
	
	# Sample a few orders
	var orders = gs.world_market_orders.duplicate()
	orders.shuffle()
	var sample_size = min(orders.size(), 15)
	
	for i in range(sample_size):
		var order = orders[i]
		if gs.get_relation(caravan_obj.faction, order.faction) == "war": continue
		
		# Find a supplier
		for s_pos in gs.settlements:
			var seller_s = gs.settlements[s_pos]
			if seller_s.pos == order.buyer_pos: continue
			if gs.get_relation(caravan_obj.faction, seller_s.faction) == "war": continue
			
			var stock = seller_s.inventory.get(order.resource, 0)
			if stock < 20: continue
			
			var buy_price = EconomyManager.get_price(order.resource, seller_s)
			var profit = order.price_offered - buy_price
			
			var pair_key = str(seller_s.pos) + str(order.buyer_pos)
			var dist = 0.0
			if gs.distance_cache.has(pair_key):
				dist = gs.distance_cache[pair_key]
			else:
				dist = seller_s.pos.distance_to(order.buyer_pos)
				gs.distance_cache[pair_key] = dist
				
			var score = profit - (dist * 0.1)
			
			# Internal faction trade priority
			if seller_s.faction == caravan_obj.faction: score += 50
			if order.faction == caravan_obj.faction: score += 50
			
			if score > best_score:
				best_score = score
				best_seller_pos = s_pos
				# Find actual index in real array
				for real_idx in range(gs.world_market_orders.size()):
					if gs.world_market_orders[real_idx] == order:
						best_order_idx = real_idx
						break
				
	if best_order_idx != -1:
		var order = gs.world_market_orders[best_order_idx]
		return {
			"buy_pos": best_seller_pos,
			"sell_pos": order.buyer_pos,
			"resource": order.resource,
			"profit": best_score
		}
	
	return _find_random_trade(gs, caravan_obj)

static func _find_random_trade(gs, caravan_obj):
	# OPTIMIZED: Pre-compute price differentials instead of nested random sampling
	var trade_opportunities = []
	
	# Cache settlements.keys() to avoid creating new arrays in nested loops
	var s_keys = gs.settlements.keys()
	if s_keys.size() < 2: return {}
	
	# Build opportunity list (much faster than nested random sampling)
	for i in range(min(s_keys.size(), 30)):  # Limit to 30 settlements max
		var s1_pos = s_keys[i]
		var s1_data = gs.settlements[s1_pos]
		if s1_data.type == "hamlet": continue
		if gs.get_relation(caravan_obj.faction, s1_data.faction) == "war": continue
		
		for res in ["grain", "iron", "wood", "fish", "wool"]:  # Focus on high-volume goods
			var stock = s1_data.inventory.get(res, 0)
			if stock < 10: continue
			var buy_price = EconomyManager.get_price(res, s1_data)
			
			# Find best seller for this resource
			for j in range(min(s_keys.size(), 20)):  # Limit comparisons
				if i == j: continue
				var s2_pos = s_keys[j]
				var s2_data = gs.settlements[s2_pos]
				if s2_data.type == "hamlet": continue
				if gs.get_relation(caravan_obj.faction, s2_data.faction) == "war": continue
				
				var sell_price = EconomyManager.get_price(res, s2_data)
				var profit = sell_price - buy_price
				var dist = s1_pos.distance_to(s2_pos)
				var score = profit - int(dist / 10.0)
				
				if score > 0:
					trade_opportunities.append({
						"buy_pos": s1_pos,
						"sell_pos": s2_pos,
						"resource": res,
						"profit": score
					})
	
	# Return best opportunity
	if not trade_opportunities.is_empty():
		trade_opportunities.sort_custom(func(a, b): return a["profit"] > b["profit"])
		return trade_opportunities[0]
					
	return {}

@warning_ignore("shadowed_global_identifier")
static func is_strength_sufficient(army, target_obj) -> bool:
	var my_power = army.strength
	var their_power = 0
	if target_obj is GDSettlement:
		# Siege is risky. 10 base * 15 walls = 150. AI wants to be sure.
		their_power = target_obj.garrison * 10 * 15
	else:
		their_power = target_obj.strength
		
	var threshold = 1.2
	if army.doctrine == "defender": threshold = 0.8
	elif army.doctrine == "conqueror": threshold = 1.0 # Aggressive
	
	return my_power >= (their_power * threshold)

static func decide_army_target(army_obj, gs) -> Dictionary:
	var doctrine = army_obj.doctrine
	var army_size = army_obj.roster.size()
	var needs_recruits = army_size < 30 # Critical threshold
	var wants_recruits = army_size < 80 # Desired threshold
	var min_dist = 9999
	var target = null
	
	var vision_range = 25 if not gs.is_night() else 10
	if doctrine == "conqueror": vision_range *= 1.5
	
	# 1. Survival: Recruitment if weak
	if needs_recruits or (wants_recruits and gs.turn % 20 == 0):
		var best_s = null
		if army_obj.home_fief != Vector2i(-1, -1):
			var s = gs.settlements.get(army_obj.home_fief)
			if s and s.crown_stock >= 500:
				best_s = {"pos": army_obj.home_fief, "type": "recruitment", "data": s}
		
		if not best_s:
			for s_pos in gs.settlements:
				var s_data = gs.settlements[s_pos]
				if s_data.faction == army_obj.faction and s_data.crown_stock >= 500:
					var d = army_obj.pos.distance_to(s_pos)
					if d < min_dist:
						min_dist = d
						target = {"pos": s_pos, "type": "recruitment", "data": s_data}
			best_s = target
			
		if best_s: return best_s

	# 2. Doctrine Logic
	match doctrine:
		"defender":
			if army_obj.home_fief != Vector2i(-1, -1):
				var s_home = gs.settlements.get(army_obj.home_fief)
				var home_pos = army_obj.home_fief
				
				# Defenders of Hubs are much more vigilant
				var patrol_radius = 15
				if s_home and s_home.type in ["city", "castle"]: patrol_radius = 25
				
				if army_obj.pos.distance_to(home_pos) > patrol_radius:
					return {"pos": home_pos, "type": "patrol"}
				
				# OPTIMIZATION: Use spatial hash for nearby threats
				var nearby_threats = gs.get_entities_near(home_pos, patrol_radius + 10)
				for other in nearby_threats:
					if not (other is GDArmy) or other == army_obj or other.respawn_timer > 0: continue
					if gs.get_relation(army_obj.faction, other.faction) == "war":
						if is_strength_sufficient(army_obj, other):
							return {"pos": other.pos, "type": "intercept", "data": other}
				
				# Also protect caravans near home
				var nearby_caravans = gs.get_entities_near(home_pos, patrol_radius)
				for caravan in nearby_caravans:
					if not ("type" in caravan and caravan.type == "caravan"): continue
					if caravan.faction == army_obj.faction:
						if caravan.state != "idle":
							# Just linger near the caravan if it's moving
							return {"pos": caravan.pos, "type": "escort"}
		
		"raider":
			# OPTIMIZATION: Use spatial hash
			var nearby = gs.get_entities_near(army_obj.pos, vision_range)
			for caravan_obj in nearby:
				if not ("type" in caravan_obj and caravan_obj.type == "caravan"): continue
				if gs.get_relation(army_obj.faction, caravan_obj.faction) == "war":
					return {"pos": caravan_obj.pos, "type": "raid", "data": caravan_obj}

	# 3. Generic Aggression
	var rel_player = gs.get_relation(army_obj.faction, "player")
	if rel_player == "war":
		var d = max(abs(army_obj.pos.x - gs.player.pos.x), abs(army_obj.pos.y - gs.player.pos.y))
		if d < vision_range:
			if is_strength_sufficient(army_obj, gs.player):
				return {"pos": gs.player.pos, "type": "player"}

	var nearby_armies = gs.get_entities_near(army_obj.pos, vision_range)
	for other in nearby_armies:
		# COMPARISON CRASH FIX: Added 'is_instance_of(other, GDArmy)' check before comparing
		# and replaced 'other == army_obj' which can fail if 'other' is a Dictionary
		if not (other is GDArmy) or other == army_obj or other.respawn_timer > 0: continue
		if gs.get_relation(army_obj.faction, other.faction) == "war":
			if is_strength_sufficient(army_obj, other):
				return {"pos": other.pos, "type": "attack", "data": other}
	
	# 4. Strategic Objectives
	var s_target = null
	var s_min_dist = 9999
	for s_pos in gs.settlements:
		var s_data = gs.settlements[s_pos]
		if gs.get_relation(army_obj.faction, s_data.faction) == "war":
			var d = army_obj.pos.distance_to(s_pos)
			var max_range = 100 if doctrine == "conqueror" else 40
			
			# HUB AND SPOKE STRATEGY: Prioritize Hubs (Cities/Castles)
			var importance = 1.0
			if s_data.type in ["city", "castle"]: importance = 2.5
			
			var score = d / importance
			
			if score < s_min_dist and d < max_range:
				if is_strength_sufficient(army_obj, s_data):
					s_min_dist = score
					s_target = {"pos": s_pos, "type": "siege", "data": s_data}
	
	if s_target: return s_target

	# 5. Idle
	var wander_pos = army_obj.pos + Vector2i(gs.rng.randi_range(-3, 3), gs.rng.randi_range(-3, 3))
	return {"pos": wander_pos, "type": "wander"}

@warning_ignore("shadowed_global_identifier")
static func spawn_bandit_party(gs):
	var current_bandits = 0
	for a in gs.armies:
		if a.type == "bandit": current_bandits += 1
	
	if current_bandits >= Globals.MAX_BANDITS: return
	
	var attempts = 0
	while attempts < 50:
		attempts += 1
		var x = gs.rng.randi_range(0, gs.width - 1)
		var y = gs.rng.randi_range(0, gs.height - 1)
		var pos = Vector2i(x, y)
		
		if gs.is_walkable(pos):
			# Check distance to towns and player
			var safe = true
			for s_pos in gs.settlements:
				if pos.distance_to(s_pos) < 15:
					safe = false
					break
			if pos.distance_to(gs.player.pos) < 20:
				safe = false
				
			if safe:
				var new_bandit = GDArmy.new()
				new_bandit.type = "bandit"
				new_bandit.faction = "bandits"
				new_bandit.name = "Bandit Raiders"
				new_bandit.pos = pos
				var b_size = gs.rng.randi_range(15, 40)
				for j in range(b_size):
					new_bandit.roster.append(GameData.generate_recruit(gs.rng, 1))
				gs.armies.append(new_bandit)
				gs.add_log("Rumors of new bandit activity in the wilderness...")
				return
