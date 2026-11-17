extends RefCounted
class_name ConsumptionSystem

# ConsumptionSystem: Settlement consumption, growth, taxes, and logistics
# Handles food/fuel consumption, population growth, taxation, and inter-settlement logistics

# GameData and GameState are autoloads - no need to preload
const Globals = preload("res://src/core/Globals.gd")
const PricingSystem = preload("res://src/economy/PricingSystem.gd")

static func _process_consumption_and_growth(s_data):
	var pop = s_data.population
	if pop <= 0: return
	
	s_data.burgher_unhappy = false
	s_data.nobility_unhappy = false
	
	# Synchronize Classes (Initialize if needed)
	if s_data.laborers <= 0 and s_data.burghers <= 0:
		s_data.nobility = max(1, int(pop * Globals.NOBILITY_TARGET_PERCENT))
		s_data.burghers = int(pop * Globals.BURGHER_TARGET_PERCENT)
		s_data.laborers = pop - s_data.nobility - s_data.burghers
	
	# Caloric demand based on acres/bushels logic
	var total_hunger = float(pop) * Globals.DAILY_BUSHELS_PER_PERSON
	var hunger_satisfied = 0.0
	
	# Priority 1: Primary Foods (Grain, Fish, Meat, Game)
	var food_types = ["grain", "fish", "meat", "game"]
	var variety = 0
	for f in food_types:
		if hunger_satisfied >= total_hunger: break
		var available = s_data.inventory.get(f, 0)
		if available > 0:
			var eat = min(available, int(total_hunger - hunger_satisfied) + 1)
			s_data.inventory[f] -= eat
			hunger_satisfied += eat
			variety += 1
			GameState.track_consumption(f, eat)

	if hunger_satisfied < total_hunger * 0.9: # 10% margin before severe starvation
		var deficit_ratio = 1.0 - (hunger_satisfied / total_hunger)
		var granary_lvl = s_data.buildings.get("granary", 0)
		var mitigation = clamp(granary_lvl * 0.15, 0.0, 0.8)
		
		s_data.unrest = min(100, s_data.unrest + int(Globals.STARVATION_UNREST_INC * deficit_ratio * (1.0 - mitigation)))
		s_data.happiness = max(0, s_data.happiness - int(Globals.STARVATION_HAPPINESS_DEC * deficit_ratio * (1.0 - mitigation)))
		var deaths = int(pop * Globals.STARVATION_DEATH_RATE * deficit_ratio * (1.0 - mitigation)) + Globals.STARVATION_BASE_DEATH
		s_data.population = max(0, pop - deaths)
		GameState.track_starvation(deaths)
	else:
		# Fuel consumption (Wood for heating/cooking)
		var total_lvls = 0
		for build in s_data.buildings: total_lvls += s_data.buildings[build]
		var wood_needed = float(pop / Globals.WOOD_FUEL_POP_DIVISOR) + (total_lvls * Globals.WOOD_FUEL_BUILDING_MULT)
		var temp = GameState.geology.get(s_data.pos, {}).get("temp", 0.0)
		if temp > 0.0:
			wood_needed *= max(0.2, 1.0 - temp)
			
		var wood_burn = s_data.remove_inventory("wood", int(wood_needed))
		GameState.track_consumption("wood", wood_burn)
		
		# Penalty for fuel shortage
		if wood_burn < wood_needed:
			var shortage = 1.0 - (float(wood_burn) / max(1.0, wood_needed))
			s_data.happiness = max(0, s_data.happiness - int(10 * shortage))
			s_data.unrest = min(100, s_data.unrest + int(5 * shortage))
		
		if variety >= 2: s_data.happiness = min(100, s_data.happiness + 2)
		
		# --- INDUSTRIAL SINKS (MAINTENANCE) ---
		var cloth_needed = int(pop * Globals.CLOTH_CONSUMPTION_RATE) + 1
		var leather_needed = int(pop * Globals.LEATHER_CONSUMPTION_RATE) + 1
		var cloth_burn = s_data.remove_inventory("cloth", cloth_needed)
		var leather_burn = s_data.remove_inventory("leather", leather_needed)
		GameState.track_consumption("cloth", cloth_burn)
		GameState.track_consumption("leather", leather_burn)
		
		# Penalty for clothing shortage
		if cloth_burn < cloth_needed or leather_burn < leather_needed:
			s_data.happiness = max(0, s_data.happiness - 1)
			# Long term shortage could increase unrest, but start small
		
		# --- BURGHER COMFORT ---
		var ale_needed = int(s_data.burghers * 0.1)
		var ale_burn = s_data.remove_inventory("ale", ale_needed)
		GameState.track_consumption("ale", ale_burn)
		if ale_burn < ale_needed:
			s_data.burgher_unhappy = true
		# Also check maintenance items for burghers specifically?
		# The request says "ale (0.1 units per burgher) and cloth or leather (maintenance)".
		# Maintenance is already checked above for the whole population.
		# Let's check if the population-wide maintenance was met.
		if cloth_burn < cloth_needed and leather_burn < leather_needed:
			s_data.burgher_unhappy = true
			
		# --- NOBLE LUXURIES ---
		var noble_meat_req = int(s_data.nobility * 0.5)
		var noble_furs_req = max(1, int(s_data.nobility * 0.05))
		var noble_salt_req = max(1, int(s_data.nobility * 0.05))
		
		var n_meat = s_data.remove_inventory("meat", noble_meat_req)
		var n_furs = s_data.remove_inventory("furs", noble_furs_req)
		var n_salt = s_data.remove_inventory("salt", noble_salt_req)
		
		GameState.track_consumption("meat", n_meat)
		GameState.track_consumption("furs", n_furs)
		GameState.track_consumption("salt", n_salt)
		
		if n_meat < noble_meat_req or n_furs < noble_furs_req or n_salt < noble_salt_req:
			s_data.nobility_unhappy = true

		if hunger_satisfied >= total_hunger and s_data.get_food_stock() > total_hunger * 30:
			var cap = s_data.get_housing_capacity()
			if s_data.population < cap:
				var births = int(s_data.population * Globals.GROWTH_RATE) + Globals.GROWTH_BASE
				s_data.population += births
				GameState.track_births(births)
				if s_data.population > cap: s_data.population = cap
			else:
				# OVERCROWDING: Increase unrest if above capacity
				s_data.unrest = min(100, s_data.unrest + 1)
				s_data.happiness = max(0, s_data.happiness - 1)

	# --- WORLD MARKET ORDERS ---
	# If critical resources are low, place a "Buy Order" on the World Market
	if GameState.turn % 12 == 0: # Check twice a day
		var critical_resources = ["grain", "iron", "wood", "wool", "coal", "meat", "salt"]
		for res in critical_resources:
			var stock = s_data.inventory.get(res, 0)
			var threshold = s_data.population * 0.5 if res == "grain" else 50
			
			if stock < threshold:
				var guild_lvl = s_data.buildings.get("merchant_guild", 0)
				var cap = 100 * (1.0 + (guild_lvl * 0.5))
				var buy_price = int(GameData.BASE_PRICES.get(res, 10) * 1.2) # Default 20% premium
				
				# Don't duplicate orders
				var existing = false
				for order in GameState.world_market_orders:
					if order["buyer_pos"] == s_data.pos and order["resource"] == res:
						existing = true
						break
				
				if not existing and s_data.crown_stock >= buy_price * 10:
					GameState.world_market_orders.append({
						"buyer_pos": s_data.pos,
						"resource": res,
						"amount": int(cap),
						"price_offered": buy_price,
						"faction": s_data.faction
					})
					GameState.track_buy_order("placed")

	# --- PILLAR EFFECTS (CIVIL & DEFENSE) ---
	var tavern_lvl = s_data.buildings.get("tavern", 0)
	var cathedral_lvl = s_data.buildings.get("cathedral", 0)
	var stone_walls_lvl = s_data.buildings.get("stone_walls", 0)
	
	if tavern_lvl > 0:
		s_data.happiness = min(100, s_data.happiness + (tavern_lvl * 1))
		s_data.unrest = max(0, s_data.unrest - (tavern_lvl * 1))
	
	if cathedral_lvl > 0:
		s_data.stability = min(100, s_data.stability + (cathedral_lvl * 2))
		s_data.unrest = max(0, s_data.unrest - (cathedral_lvl * 2))
		
	if stone_walls_lvl > 0:
		s_data.stability = min(100, s_data.stability + stone_walls_lvl)

	# Final Class Sync (Strict fixed ratios)
	s_data.nobility = max(1, int(pop * Globals.NOBILITY_TARGET_PERCENT))
	s_data.burghers = int(pop * Globals.BURGHER_TARGET_PERCENT)
	s_data.laborers = pop - s_data.nobility - s_data.burghers
	
	# Final check to ensure no negative values (extreme edge cases)
	if s_data.laborers < 0:
		s_data.burghers = max(0, s_data.burghers + s_data.laborers)
		s_data.laborers = 0

static func _process_taxes(s_data):
	# Basic tax simulation to give settlements purchasing power
	# 1. Poll Tax (Laborers & Burghers)
	var laborer_income = int(s_data.laborers * 0.1)
	var burgher_income = int(s_data.burghers * 0.5)
	
	if s_data.burgher_unhappy:
		burgher_income = int(burgher_income * 0.5)
	
	s_data.crown_stock += laborer_income + burgher_income
	
	# 2. Market Tariffs (Fixed 10 crowns per market level every 24 turns)
	if GameState.turn % 24 == 0:
		var market_lvl = s_data.buildings.get("market", 0)
		if market_lvl > 0:
			var tariff = market_lvl * 10
			s_data.crown_stock += tariff
			
			# Market Lvl 10: INTEREST on Crown Stock (Banks!)
			if market_lvl >= 10:
				var interest = int(s_data.crown_stock * 0.02)
				s_data.crown_stock += interest

static func _process_storage_limits(s_data):
	if s_data.inventory.is_empty(): return
	
	var cap = s_data.get_storage_capacity()
	var total_volume = 0
	for res in s_data.inventory:
		total_volume += s_data.inventory[res]
	
	if total_volume > cap * 0.9:
		var overage = total_volume - int(cap * 0.85)
		
		var protect = {
			"grain": int(s_data.population * Globals.DAILY_BUSHELS_PER_PERSON * 30),
			"fish": int(s_data.population * 0.01 * 10),
			"wood": int(s_data.population / Globals.WOOD_FUEL_POP_DIVISOR) + 20,
			"iron": 10,
			"coal": 10,
			"stone": 20
		}
		
		var dump_order = []
		for res in s_data.inventory:
			var stock = s_data.inventory[res]
			var keep_min = protect.get(res, 0)
			var surplus = stock - keep_min
			if surplus > 0:
				dump_order.append({"res": res, "surplus": surplus})
		
		dump_order.sort_custom(func(a, b): return a["surplus"] > b["surplus"])
		
		var items_to_dump = overage
		for entry in dump_order:
			if items_to_dump <= 0: break
			var res = entry["res"]
			var stock = s_data.inventory[res]
			var dump_amt = min(entry["surplus"], items_to_dump)
			if dump_amt > 0:
				s_data.inventory[res] -= dump_amt
				items_to_dump -= dump_amt
				s_data.crown_stock += int(dump_amt * 0.1)

@warning_ignore("shadowed_global_identifier")
static func _process_settlement_logistics(gs, s_data):
	var food_types = ["grain", "fish", "game", "meat"]
	
	# UPSTREAM: Hamlets, specialized hamlets, and Villages send surplus to parent City
	if "hamlet" in s_data.type or s_data.type == "village":
		# AUTO-PARENT DETECTION: If satellite has no parent, find the nearest Hub (City/Castle)
		if s_data.parent_city == Vector2i(-1, -1):
			var best_dist = 999.0
			var best_p = Vector2i(-1, -1)
			for pos in gs.settlements:
				var target = gs.settlements[pos]
				if target.type in ["city", "castle"] and target.faction == s_data.faction:
					var d = s_data.pos.distance_to(pos)
					if d < best_dist:
						best_dist = d
						best_p = pos
			s_data.parent_city = best_p

		if s_data.parent_city != Vector2i(-1, -1):
			var parent = gs.settlements.get(s_data.parent_city)
			if parent:
				for res in s_data.inventory.keys():
					var amt = s_data.inventory[res]
					var keep = 0
					if res == "grain":
						# Seed Reserve Logic: Keep enough grain to plant NEXT year
						var fallow_ratio = 1.0/3.0 if s_data.has_three_field_system else 0.5
						var potential_acres = int(s_data.arable_acres * (1.0 - fallow_ratio))
						
						# FIX: Only hoard seed for the land we can actually farm (Labor Limit)
						var labor_cap_acres = int(s_data.laborers * Globals.ACRES_WORKED_PER_LABORER)
						var active_acres = min(potential_acres, labor_cap_acres)
						
						var yearly_seed_needed = active_acres * (Globals.BUSHELS_PER_ACRE_BASE * Globals.SEED_RATIO_INV)
						
						# Keep (Seed Reserve + 2 days of food)
						keep = int(yearly_seed_needed + (s_data.population * Globals.DAILY_BUSHELS_PER_PERSON * Globals.VILLAGER_TRANSFER_FOOD_DAYS))
					elif res in food_types: 
						keep = int(s_data.population * Globals.DAILY_BUSHELS_PER_PERSON * Globals.VILLAGER_TRANSFER_FOOD_DAYS)
					elif res == "wood": 
						keep = int(s_data.population / Globals.WOOD_FUEL_POP_DIVISOR) + 20
					
					if amt > keep:
						var trans = amt - keep
						s_data.inventory[res] -= trans
						
						# LOGISTICS 2.0: Virtualized delivery instead of instant teleport
						var dist = 0.0
						var pair_key = str(s_data.pos) + str(s_data.parent_city)
						if gs.distance_cache.has(pair_key):
							dist = gs.distance_cache[pair_key]
						else:
							dist = s_data.pos.distance_to(s_data.parent_city)
							gs.distance_cache[pair_key] = dist
							
						var arrival = gs.turn + int(dist * 2.5) + 24 # Takes time to travel
						
						gs.logistical_pulses.append({
							"target_pos": s_data.parent_city,
							"origin": s_data.pos,
							"resource": res,
							"amount": trans,
							"arrival_turn": arrival
						})
						GameState.track_logistical_pulse("generated")
	
	# DOWNSTREAM: Hubs send emergency aid
	if not "hamlet" in s_data.type:
		# Only send food if city has its own buffer
		var city_food = s_data.get_food_stock()
		var city_safe_threshold = s_data.population * Globals.VILLAGER_SUPPORT_CITY_MIN_DAYS
		
		if city_food > city_safe_threshold:
			for h_pos in gs.settlements:
				var h = gs.settlements[h_pos]
				if h.parent_city == s_data.pos:
					var h_food = h.get_food_stock()
					if h_food < h.population * Globals.VILLAGER_SUPPORT_THRESHOLD_DAYS:
						var need = int(h.population * Globals.VILLAGER_SUPPORT_SEND_DAYS) - h_food
						for f in food_types:
							if need <= 0: break
							var send = s_data.remove_inventory(f, need)
							h.add_inventory(f, send) # Emergency support remains instant for gameplay stability
							need -= send

static func get_daily_tax(s_data) -> int:
	var pop = s_data.population
	if pop <= 0: return 0
	
	# Base Tax Logic (Inflation Adjusted: Reduced from 0.5/2.0/10.0)
	var laborer_tax = s_data.laborers * 0.1 # 1 crown per 10 laborers
	var burgher_tax = s_data.burghers * 0.5 # 1 crown per 2 burghers
	
	if s_data.burgher_unhappy:
		burgher_tax *= 0.5
		
	var noble_tax = s_data.nobility * 2.0 # Feudal dues
	
	var total = laborer_tax + burgher_tax + noble_tax
	
	# Industry Bonus
	if s_data.type != "hamlet":
		var industry_count = s_data.buildings.size() # Simplified to total building count for prosperity
		total += (industry_count * 0.5) 
	
	# Apply Side Effects (Stability)
	if s_data.nobility_unhappy:
		s_data.stability = max(0, s_data.stability - 2)
	
	# Infrastructure Multipliers
	var market_lvl = s_data.buildings.get("market", 0)
	var road_lvl = s_data.buildings.get("road_network", 0)
	var cathedral_lvl = s_data.buildings.get("cathedral", 0)
	
	total *= (1.0 + (market_lvl * 0.25))   # +25% commerce tax per level
	total *= (1.0 + (road_lvl * 0.15))     # +15% trade throughput per level
	total *= (1.0 + (cathedral_lvl * 0.1)) # +10% tithes/donation per level
	
	# LVL 3 MARKET: Tax Office (+20% Efficiency)
	if market_lvl >= 3:
		total *= 1.2

	# Tax Level Multiplier
	match s_data.tax_level:
		"low": total *= 0.5
		"normal": total *= 1.0
		"high": total *= 1.5
		"extortionate": total *= 2.5
	
	return int(total)

static func _calculate_dividend(s_data, building_id, amount, res_name):
	# player_shares: { "mine": 0.5, "farm": 1.0 }
	if not s_data.player_shares.has(building_id) or amount <= 0:
		return
		
	var share = s_data.player_shares[building_id]
	if share <= 0: return
	
	var price = PricingSystem.get_price(res_name, s_data)
	var total_value = amount * price
	var dividend = int(total_value * share)
	
	if dividend > 0:
		# Reference GameState directly if needed OR assume it is managed in the session
		if GameState.player:
			GameState.player.crowns += dividend
			s_data.crown_stock -= dividend
