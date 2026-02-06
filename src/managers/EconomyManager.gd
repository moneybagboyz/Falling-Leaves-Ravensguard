@warning_ignore("shadowed_global_identifier")
class_name EconomyManager
extends Node

@warning_ignore("shadowed_global_identifier")
static func get_price(res_name, s_data):
	if s_data.cache_prices.has(res_name):
		return s_data.cache_prices[res_name]
		
	var base = GameData.BASE_PRICES.get(res_name, 10)
	var stock = s_data.inventory.get(res_name, 0)
	var pop = s_data.population
	
	# OPTIMIZATION: Use a demand mapping instead of elif chain
	var demand = 1.0
	
	if res_name in ["grain", "fish", "meat", "game"]:
		demand = pop * Globals.DAILY_BUSHELS_PER_PERSON * 14.0
	elif res_name == "wood":
		demand = (pop / Globals.WOOD_FUEL_POP_DIVISOR) + (s_data.buildings.size() * Globals.WOOD_FUEL_BUILDING_MULT)
		var temp = GameState.geology.get(s_data.pos, {}).get("temp", 0.0)
		if temp > 0.0:
			demand *= max(0.2, 1.0 - temp)
	else:
		# Use a coefficient-based demand for other resources
		var coeffs = {
			"ale": 0.1,
			"salt": 0.05,
			"peat": 0.025,
			"furs": 0.016,
			"fine_garments": 0.02,
			"jewelry": 0.0 # Handled manually
		}
		if coeffs.has(res_name):
			demand = (pop * coeffs[res_name]) + (10 if res_name in ["ale", "salt"] else 5)
		elif res_name == "jewelry":
			# Demand scales with nobility (approx 1 unit per 5 nobles daily turnover/desire)
			demand = max(2, int(s_data.nobility * 0.2))
	
	var ratio = float(demand) / max(1.0, float(stock))
	var price = base * ratio
	if stock <= 0:
		price = base * Globals.PRICE_ZERO_STOCK_MULT
	else:
		price = clamp(price, base * Globals.PRICE_MIN_MULT, base * Globals.PRICE_MAX_MULT)
	
	var final_p = int(price)
	s_data.cache_prices[res_name] = final_p
	return final_p

@warning_ignore("shadowed_global_identifier")
static func get_buy_price(res_name, s_data):
	return int(get_price(res_name, s_data) * 1.1)

@warning_ignore("shadowed_global_identifier")
static func get_sell_price(res_name, s_data):
	return int(get_price(res_name, s_data) * 0.9)

@warning_ignore("shadowed_global_identifier")
static func recalculate_production(s_data, grid, resources, geology):
	s_data.initialize_acres(grid, resources, geology)
	s_data.production_capacity = {}
	s_data.mining_slots = 0
	s_data.fishing_slots = 0
	s_data.extraction_slots = 0
	s_data.ore_deposits = {}
	
	var r = s_data.radius
	var h = grid.size()
	var w = grid[0].size()
	
	s_data.garrison_max = 20
	if s_data.type == "castle": s_data.garrison_max = 100
	elif s_data.type == "city": s_data.garrison_max = 200
	
	for dy in range(-r, r+1):
		for dx in range(-r, r+1):
			var p = s_data.pos + Vector2i(dx, dy)
			if p.x < 0 or p.x >= w or p.y < 0 or p.y >= h: continue
			if p.distance_to(s_data.pos) > r: continue
			
			# Early exit if all slots are saturated (avoid processing entire radius unnecessarily)
			if s_data.mining_slots > 10000 and s_data.fishing_slots > 5000 and s_data.extraction_slots > 3000:
				break
			
			var t = GameState.get_true_terrain(p)
			
			if t == "~": 
				s_data.fishing_slots += 150
			elif t == "≈": # Major Rivers / Lakes
				s_data.fishing_slots += 150
				s_data.extraction_slots += 40 # Silt/Clay
			elif t == "^" or t == "O": # Mountains
				s_data.mining_slots += 400 # Represents multiple shaft systems in 250 acres
			elif t == "o": # Hills
				s_data.mining_slots += 150 # Hills allow for surface mining/quarrying
			elif t in ["/", "\\"]:
				s_data.fishing_slots += 50
				s_data.extraction_slots += 20 # River sifting
			elif t == "&": # Jungle/Swamp
				s_data.extraction_slots += 80 # Swamp peat/clay
			elif t == '"':
				s_data.extraction_slots += 100 # Desert salt/sand
			
			if resources.has(p):
				var res = resources[p]
				if res in ["game", "horses", "spices", "ivory", "furs", "peat", "salt", "clay"]: 
					increment_prod(s_data, res, 1, geology)
				elif res in ["iron", "copper", "silver", "gold", "gems", "marble", "coal", "tin", "lead"]:
					s_data.ore_deposits[res] = s_data.ore_deposits.get(res, 0) + 1
					s_data.mining_slots += 200 # Grant slots for specific resource veins even if not on mountain
			
			# DWARF FORTRESS LOGIC: Subterranean Prospecting
			# Even if no resource is visible on the surface, the rock layers provide potential
			if geology.has(p):
				var layers = geology[p].get("layers", [])
				for layer in layers:
					if GameData.GEOLOGY_RESOURCES.has(layer):
						var layer_res = GameData.GEOLOGY_RESOURCES[layer]
						for ore in layer_res:
							# Use a deterministic hash of the position to see if an ore exists here
							# This ensures the same map always has the same hidden minerals
							var pos_seed = (p.x * 34223) ^ (p.y * 59573)
							var roll = float(pos_seed % 1000) / 1000.0
							
							# If we "find" the hidden ore in this layer
							if roll < layer_res[ore]:
								# Hidden deposits provide 0.25 of a full surface deposit's value 
								# but they add up across the many tiles in a settlement's radius
								s_data.ore_deposits[ore] = s_data.ore_deposits.get(ore, 0.0) + 0.25
								s_data.mining_slots += 50 
	
	# Perform initial labor allocation so the UI isn't empty on Day 1
	_process_labor_pool(s_data, s_data.get_workforce_efficiency())

@warning_ignore("shadowed_global_identifier")
static func increment_prod(s_data, res_name, amount, geology = null):
	var mult = 1.0
	if geology and geology.has(s_data.pos):
		var geo = geology[s_data.pos]
		if res_name == "grain":
			if geo.get("rain", 0) > 0.1: mult += 0.5
			if geo.get("temp", 0) > 0.3 or geo.get("temp", 0) < -0.3: mult -= 0.5
		elif res_name == "wood":
			if geo.get("rain", 0) > 0.2: mult += 0.5
			elif geo.get("rain", 0) < -0.2: mult -= 0.5
		elif res_name == "fish":
			if geo.get("temp", 0) < -0.3: mult -= 0.5
			
	amount = max(1, int(amount * mult))
	s_data.production_capacity[res_name] = s_data.production_capacity.get(res_name, 0.0) + amount

@warning_ignore("shadowed_global_identifier")
static func process_daily_pulse(gs, s_data):
	# Refresh Cache
	s_data.cache_prices.clear()
	s_data.cache_efficiency = s_data.get_workforce_efficiency()
	s_data.cache_housing_cap = s_data.get_housing_capacity()
	
	var efficiency = s_data.cache_efficiency
	
	# 1. Base Production from world resources (Spices, Ivory, etc.)
	for res in s_data.production_capacity:
		var amount = int(s_data.production_capacity[res] * efficiency)
		s_data.add_inventory(res, amount)
		GameState.track_production(res, amount)
	
	# 2. Labor Intensive Production (Food & Raw Materials)
	_process_labor_pool(s_data, efficiency)
	
	# 3. Energy Pulses (Charcoal Burning)
	_process_energy(s_data, efficiency)
	
	_process_housing(s_data)
	# CONSUMPTION BEFORE DUMPING: Let people eat before we throw away food
	_process_consumption_and_growth(s_data)
	_process_taxes(s_data) # Generate income to afford buildings
	_process_storage_limits(s_data)
	_process_settlement_logistics(gs, s_data)

static func _process_taxes(s_data):
	# Basic tax simulation to give settlements purchasing power
	# 1. Poll Tax (Laborers & Burghers)
	# Laborers pay roughly 0.05 crowns/day (tax on their produce)
	# Burghers pay roughly 0.25 crowns/day (guild fees/tariffs)
	var tax_income = int(s_data.laborers * 0.05) + int(s_data.burghers * 0.25)
	
	# 2. Nobles don't pay tax, they receive a stipend from the Lord (Player/Faction), 
	# but in this abstraction, they generate "Prestige" or influence, not direct crowns here.
	
	# 3. Market Tariffs (Trade Tax)
	# A small fraction of trade volume (approximated by market tier)
	if s_data.buildings.has("market"):
		tax_income += 20
	if s_data.buildings.has("merchant_guild"):
		tax_income += 50
		
	# LVL 10 MARKET: The Grand Exchange (1% Interest)
	if s_data.buildings.get("market", 0) >= 10:
		var interest = int(s_data.crown_stock * 0.01)
		tax_income += interest

	s_data.crown_stock += tax_income
	# GameState.track_gold(tax_income) # Optional: global tracking?

static func _process_storage_limits(s_data):
	var total_items = 0
	for res in s_data.inventory:
		total_items += s_data.inventory[res]
	
	# Hubs need more storage, but let's assume a universal cap for now
	# Cities/Castles have 10x the storage of Hamlets
	var base_storage = 2000
	if s_data.type in ["city", "castle"]: base_storage = 20000
	elif s_data.type == "village": base_storage = 5000
	
	# WAREHOUSE DISTRICT MULTIPLIER: Adds 100% capacity per level
	var mult = 1.0 + s_data.buildings.get("warehouse_district", 0)
	base_storage = int(base_storage * mult)
	
	# If storage is exceeding 90%, start "dumping" low value gluts to reach 85%
	var dump_threshold = int(base_storage * 0.9)
	var safety_threshold = int(base_storage * 0.85)
	
	if total_items > dump_threshold:
		var items_to_dump = total_items - safety_threshold
		
		# Identify items to dump (Leather/Furs/Wool are common gluts)
		# We PROTECT Wood, Stone, and Fuel because lack of them stalls the economy.
		# We add 'meat' to the dump list if it's excessively hoarded.
		var dump_order = ["leather", "wool", "furs", "meat"]
		
		# Protection for meat: Only dump if we have > 30 days of total food
		if s_data.get_food_stock() < (float(s_data.population) * Globals.DAILY_BUSHELS_PER_PERSON * 30.0):
			dump_order.erase("meat")
		
		for res in dump_order:
			if items_to_dump <= 0: break
			if s_data.inventory.has(res):
				var amt = s_data.inventory[res]
				var dump_amt = min(amt, items_to_dump)
				if res == "meat":
					# Keep a minimum reserve even if dumping
					var reserve = int(float(s_data.population) * Globals.DAILY_BUSHELS_PER_PERSON * 20.0)
					dump_amt = max(0, min(dump_amt, amt - reserve))
				
				if dump_amt > 0:
					s_data.inventory[res] -= dump_amt
					items_to_dump -= dump_amt
					# Selling for pennies (symbolic dump)
					s_data.crown_stock += int(dump_amt * 0.1) 
		
		# If still over, dump everything starting from lowest price item
		if items_to_dump > 0:
			var sorted_res = s_data.inventory.keys()
			sorted_res.sort_custom(func(a, b): return get_price(a, s_data) < get_price(b, s_data))
			for res in sorted_res:
				if items_to_dump <= 0: break
				# NEVER dump critical survival items in the second pass
				if res in ["grain", "fish", "wood", "peat", "stone"]: continue 
				
				var amt = s_data.inventory[res]
				var dump_amt = min(amt, items_to_dump)
				if res == "meat": # Meat can be dumped in second pass but only if it's still excessive
					var reserve = int(float(s_data.population) * Globals.DAILY_BUSHELS_PER_PERSON * 20.0)
					dump_amt = max(0, min(dump_amt, amt - reserve))
				
				if dump_amt > 0:
					s_data.inventory[res] -= dump_amt
					items_to_dump -= dump_amt
					s_data.crown_stock += int(dump_amt * 0.1)

static func _process_labor_pool(s_data, efficiency):
	var pop = s_data.population
	# Initialize classes if they don't exist
	if s_data.laborers <= 0 and s_data.burghers <= 0 and pop > 0:
		s_data.nobility = max(1, int(pop * Globals.NOBILITY_TARGET_PERCENT))
		s_data.burghers = int(pop * Globals.BURGHER_TARGET_PERCENT)
		s_data.laborers = pop - s_data.nobility - s_data.burghers
	
	var remaining_laborers = int(s_data.laborers * efficiency)
	var remaining_burghers = int(s_data.burghers * efficiency)
	
	# Reset allocation
	var alloc = {
		"farms": 0, "fishing": 0, "mining": 0, "pasture": 0,
		"foraging": 0, "hunting": 0, "wood": 0,
		"extraction": 0, "trapping": 0, "idle": 0
	}
	
	# Land & Limits
	var fallow_ratio = 1.0/3.0 if s_data.has_three_field_system else 0.5
	var active_acres = int(s_data.arable_acres * (1.0 - fallow_ratio))
	s_data.fallow_acres = s_data.arable_acres - active_acres
	s_data.pasture_acres = s_data.fallow_acres
	
	var farm_limit = int(float(active_acres) / Globals.ACRES_WORKED_PER_LABORER)
	var fish_limit = int(s_data.fishing_slots)
	var mine_limit = int(s_data.mining_slots)
	var pasture_limit = int((s_data.pasture_acres + s_data.arid_acres) / Globals.ACRES_WORKED_PER_LABORER)
	var forest_land_limit = int(s_data.forest_acres / Globals.ACRES_WORKED_PER_LABORER)
	var wilderness_limit = int((s_data.forest_acres + s_data.wilderness_acres + s_data.pasture_acres) / Globals.ACRES_WORKED_PER_LABORER)

	# --- TIER 1: SUBSISTENCE (24-Hour Survival) ---
	var daily_food_req = float(pop) * Globals.DAILY_BUSHELS_PER_PERSON
	var daily_wood_req = float(pop) / Globals.WOOD_FUEL_POP_DIVISOR
	var temp = GameState.geology.get(s_data.pos, {}).get("temp", 0.0)
	if temp > 0.0:
		daily_wood_req *= max(0.2, 1.0 - temp)
	
	if s_data.get_food_stock() < daily_food_req:
		var needed = daily_food_req - s_data.get_food_stock()
		# Priority 1: Fishing (fast yield) if available
		if fish_limit > 0:
			var f_take = clamp(int((needed * Globals.DAYS_PER_YEAR) / Globals.FISHING_YIELD_BASE), 1, min(remaining_laborers, fish_limit))
			alloc["fishing"] += f_take
			remaining_laborers -= f_take
			fish_limit -= f_take
			needed -= (f_take * Globals.FISHING_YIELD_BASE / Globals.DAYS_PER_YEAR)
		
		# Priority 2: Hunting (Wilderness food)
		if needed > 0 and remaining_laborers > 0 and wilderness_limit > 0:
			var h_take = clamp(int((needed * Globals.DAYS_PER_YEAR) / Globals.HUNTING_YIELD_MEAT), 1, min(remaining_laborers, wilderness_limit))
			alloc["hunting"] += h_take
			remaining_laborers -= h_take
			wilderness_limit -= h_take
			needed -= (h_take * Globals.HUNTING_YIELD_MEAT / Globals.DAYS_PER_YEAR)

		# Priority 3: Farming
		if needed > 0 and farm_limit > 0 and remaining_laborers > 0:
			var g_needed = needed / (1.0 - Globals.SEED_RATIO_INV)
			var g_take = clamp(int((g_needed * Globals.DAYS_PER_YEAR) / (Globals.ACRES_WORKED_PER_LABORER * Globals.BUSHELS_PER_ACRE_BASE)), 1, min(remaining_laborers, farm_limit))
			alloc["farms"] += g_take
			remaining_laborers -= g_take
			farm_limit -= g_take
			
	if remaining_laborers > 0 and s_data.inventory.get("wood", 0) < daily_wood_req:
		var needed = daily_wood_req - s_data.inventory.get("wood", 0)
		if forest_land_limit > 0:
			var w_take = clamp(int((needed * Globals.DAYS_PER_YEAR) / (Globals.ACRES_WORKED_PER_LABORER * Globals.FORESTRY_YIELD_WOOD)), 1, min(remaining_laborers, forest_land_limit))
			alloc["wood"] += w_take
			remaining_laborers -= w_take
			forest_land_limit -= w_take

	# --- TIER 2: SECURITY (The Buffer - 60 Days) ---
	var security_days = 60.0
	var food_buffer_target = daily_food_req * security_days
	var wood_buffer_target = daily_wood_req * security_days
	
	if remaining_laborers > 0:
		# Allocate to reach food security
		var current_food = s_data.get_food_stock()
		if current_food < food_buffer_target:
			var _needed = food_buffer_target - current_food
			# Distribute between farming and fishing and hunting
			if farm_limit > 0 and remaining_laborers > 0:
				var g_take = clamp(int(remaining_laborers * 0.5), 0, farm_limit) 
				alloc["farms"] += g_take
				remaining_laborers -= g_take
				farm_limit -= g_take
			if fish_limit > 0 and remaining_laborers > 0:
				var f_take = clamp(int(remaining_laborers * 0.3), 0, fish_limit)
				alloc["fishing"] += f_take
				remaining_laborers -= f_take
				fish_limit -= f_take
			if wilderness_limit > 0 and remaining_laborers > 0:
				var h_take = clamp(remaining_laborers, 0, wilderness_limit)
				alloc["hunting"] += h_take
				remaining_laborers -= h_take
				wilderness_limit -= h_take
				
		# Allocate to reach fuel security
		var current_wood = s_data.inventory.get("wood", 0)
		if remaining_laborers > 0 and current_wood < wood_buffer_target and forest_land_limit > 0:
			var w_take = clamp(int(remaining_laborers * 0.5), 0, forest_land_limit)
			alloc["wood"] += w_take
			remaining_laborers -= w_take
			forest_land_limit -= w_take

	# --- TIER 3: SPECIALIZATION (Profit) ---
	if remaining_laborers > 0:
		var p_grain = get_price("grain", s_data)
		var p_fish = get_price("fish", s_data)
		var p_stone = get_price("stone", s_data)
		var p_wood = get_price("wood", s_data)
		var p_meat = get_price("meat", s_data)
		var p_hides = get_price("hides", s_data)
		var p_wool = get_price("wool", s_data)
		var p_furs = get_price("furs", s_data)
		var p_horses = get_price("horses", s_data)
		
		var v_farm = (Globals.ACRES_WORKED_PER_LABORER * Globals.BUSHELS_PER_ACRE_BASE / Globals.DAYS_PER_YEAR) * (1.0 - Globals.SEED_RATIO_INV) * p_grain
		var v_fish = (Globals.FISHING_YIELD_BASE / Globals.DAYS_PER_YEAR) * p_fish
		var v_mine = (4.0 / 30.0) * p_stone
		var v_wood = (Globals.ACRES_WORKED_PER_LABORER * Globals.FORESTRY_YIELD_WOOD / Globals.DAYS_PER_YEAR) * p_wood
		var v_pasture = (Globals.ACRES_WORKED_PER_LABORER / Globals.DAYS_PER_YEAR) * (Globals.PASTURE_YIELD_WOOL * p_wool + Globals.PASTURE_YIELD_HIDES * p_hides + Globals.PASTURE_YIELD_MEAT * p_meat + Globals.PASTURE_YIELD_HORSES * p_horses)
		var v_hunt = (Globals.ACRES_WORKED_PER_LABORER / Globals.DAYS_PER_YEAR) * (Globals.HUNTING_YIELD_MEAT * p_meat + Globals.HUNTING_YIELD_HIDES * p_hides)
		var v_forage = (Globals.ACRES_WORKED_PER_LABORER * Globals.FORAGING_YIELD_GRAIN / Globals.DAYS_PER_YEAR) * p_grain
		var v_trap = (Globals.ACRES_WORKED_PER_LABORER * Globals.FUR_YIELD / Globals.DAYS_PER_YEAR) * p_furs
		
		var jobs = [
			{"id": "fishing", "val": v_fish, "limit": fish_limit},
			{"id": "farms", "val": v_farm, "limit": farm_limit},
			{"id": "mining", "val": v_mine, "limit": mine_limit},
			{"id": "wood", "val": v_wood, "limit": forest_land_limit},
			{"id": "pasture", "val": v_pasture, "limit": pasture_limit},
			{"id": "hunting", "val": v_hunt, "limit": wilderness_limit},
			{"id": "foraging", "val": v_forage, "limit": wilderness_limit},
			{"id": "trapping", "val": v_trap, "limit": forest_land_limit}
		]
		jobs.sort_custom(func(a, b): return a["val"] > b["val"])
		
		for j in jobs:
			if remaining_laborers <= 0: break
			var take = int(min(remaining_laborers, j["limit"]))
			if take > 0:
				alloc[j["id"]] += take
				remaining_laborers -= take

		if remaining_laborers > 0 and s_data.extraction_slots > 0:
			var take = int(min(remaining_laborers, s_data.extraction_slots))
			alloc["extraction"] += take
			remaining_laborers -= take

	# Burghers process industry automatically for now, but we track their size
	_process_organic_industry(s_data, remaining_burghers)

	alloc["idle"] = remaining_laborers
	s_data.last_labor_allocation = alloc
	
	# Execute production for all allocated labor
	for job_key in alloc:
		if job_key != "idle" and alloc[job_key] > 0:
			_produce_resource_by_job(s_data, job_key, alloc[job_key])

static func _produce_resource_by_job(s_data, job_id, labor):
	if labor <= 0: return
	var b_mult = 1.0
	match job_id:
		"farms":
			b_mult = 1.0 + (s_data.buildings.get("farm", 0) * 0.5)
			var h_acres = labor * Globals.ACRES_WORKED_PER_LABORER
			var g_prod = int((float(h_acres * Globals.BUSHELS_PER_ACRE_BASE * b_mult) / Globals.DAYS_PER_YEAR) * (1.0 - Globals.SEED_RATIO_INV))
			s_data.add_inventory("grain", g_prod)
			GameState.track_production("grain", g_prod)
			_calculate_dividend(s_data, "farm", g_prod, "grain")
		"fishing":
			b_mult = 1.0 + (s_data.buildings.get("fishery", 0) * 0.5)
			var f_prod = int(labor * Globals.FISHING_YIELD_BASE * b_mult / Globals.DAYS_PER_YEAR)
			s_data.add_inventory("fish", f_prod)
			GameState.track_production("fish", f_prod)
			_calculate_dividend(s_data, "fishery", f_prod, "fish")
		"mining":
			b_mult = 1.0 + (s_data.buildings.get("mine", 0) * 0.5)
			var stone_prod = int(labor * (4.0 * b_mult) / 30.0)
			s_data.add_inventory("stone", stone_prod)
			GameState.track_production("stone", stone_prod)
			_process_mine_resources(s_data, labor)
			_calculate_dividend(s_data, "mine", stone_prod, "stone")
		"pasture":
			b_mult = 1.0 + (s_data.buildings.get("pasture", 0) * 0.5)
			var p_worked = labor * Globals.ACRES_WORKED_PER_LABORER
			var wool = int(p_worked * Globals.PASTURE_YIELD_WOOL * b_mult / Globals.DAYS_PER_YEAR)
			var hides = int(p_worked * Globals.PASTURE_YIELD_HIDES * b_mult / Globals.DAYS_PER_YEAR)
			var meat = int(p_worked * Globals.PASTURE_YIELD_MEAT * b_mult / Globals.DAYS_PER_YEAR)
			var horses = int(p_worked * Globals.PASTURE_YIELD_HORSES * b_mult / Globals.DAYS_PER_YEAR)
			s_data.add_inventory("wool", wool)
			s_data.add_inventory("hides", hides)
			s_data.add_inventory("meat", meat)
			s_data.add_inventory("horses", horses)
			GameState.track_production("wool", wool)
			GameState.track_production("hides", hides)
			GameState.track_production("meat", meat)
			GameState.track_production("horses", horses)
		"foraging":
			var f_worked = labor * Globals.ACRES_WORKED_PER_LABORER
			var f_grain = int(f_worked * Globals.FORAGING_YIELD_GRAIN / Globals.DAYS_PER_YEAR)
			s_data.add_inventory("grain", f_grain)
			GameState.track_production("grain", f_grain)
		"hunting":
			var h_worked = labor * Globals.ACRES_WORKED_PER_LABORER
			var h_meat = int(h_worked * Globals.HUNTING_YIELD_MEAT / Globals.DAYS_PER_YEAR)
			var h_hides = int(h_worked * Globals.HUNTING_YIELD_HIDES / Globals.DAYS_PER_YEAR)
			s_data.add_inventory("meat", h_meat)
			s_data.add_inventory("hides", h_hides)
			GameState.track_production("meat", h_meat)
			GameState.track_production("hides", h_hides)
		"wood":
			b_mult = 1.0 + (s_data.buildings.get("lumber_mill", 0) * 1.0) # Mills are very effective
			var w_worked = labor * Globals.ACRES_WORKED_PER_LABORER
			var wood = int(w_worked * Globals.FORESTRY_YIELD_WOOD * b_mult / Globals.DAYS_PER_YEAR)
			s_data.add_inventory("wood", wood)
			GameState.track_production("wood", wood)
			_calculate_dividend(s_data, "lumber_mill", wood, "wood")
		"extraction":
			# Choice of product based on tile mix
			if s_data.arid_acres > 0:
				var salt = int(labor * Globals.SALT_YIELD / Globals.DAYS_PER_YEAR)
				var sand = int(labor * Globals.SAND_YIELD / Globals.DAYS_PER_YEAR)
				s_data.add_inventory("salt", salt)
				s_data.add_inventory("sand", sand)
				GameState.track_production("salt", salt)
				GameState.track_production("sand", sand)
			elif s_data.wetland_acres > 0:
				var peat = int(labor * Globals.PEAT_YIELD / Globals.DAYS_PER_YEAR)
				var clay = int(labor * Globals.CLAY_YIELD / Globals.DAYS_PER_YEAR)
				s_data.add_inventory("peat", peat)
				s_data.add_inventory("clay", clay)
				GameState.track_production("peat", peat)
				GameState.track_production("clay", clay)
			else: # River Sifting
				var gold = int(labor * Globals.SIFTING_YIELD_GOLD / Globals.DAYS_PER_YEAR)
				var tin = int(labor * Globals.SIFTING_YIELD_TIN / Globals.DAYS_PER_YEAR)
				s_data.add_inventory("gold", gold)
				s_data.add_inventory("tin", tin)
				GameState.track_production("gold", gold)
				GameState.track_production("tin", tin)
		"trapping":
			var t_worked = labor * Globals.ACRES_WORKED_PER_LABORER
			var furs = int(t_worked * Globals.FUR_YIELD / 360.0)
			s_data.add_inventory("furs", furs)
			GameState.track_production("furs", furs)

static func _process_mine_resources(s_data, mine_labor):
	if mine_labor <= 0: return
	
	# Each ore deposit present takes a slice of the labor
	var ores = s_data.ore_deposits.keys()
	if ores.is_empty(): return
	
	# Max 30% of mining labor can be specialized in ores if deposits exist
	# Increased from 10% to allow for major ore-focused "Boomtowns"
	var specialized_labor = int(mine_labor * 0.3)
	if specialized_labor <= 0: specialized_labor = 1
	var b_mult = 1.0 + (s_data.buildings.get("mine", 0) * 0.5)
	
	for ore in ores:
		# Production differentiation: 
		# Iron/Copper/Coal/Tin/Lead: High volume (3-5 units)
		# Marble: Medium volume, high weight (1.5 units)
		# Gold/Silver: Low volume, high value (0.8 units)
		# Gems: Rare volume (0.2 units)
		var yield_mult = 3.0 # Default High Volume
		if ore in ["gold", "silver"]: yield_mult = 0.8
		elif ore == "marble": yield_mult = 1.5
		elif ore == "gems": yield_mult = 0.2
		
		var amt = int(specialized_labor * s_data.ore_deposits[ore] * yield_mult * b_mult / 30.0) 
		if amt > 0:
			s_data.add_inventory(ore, amt)
			GameState.track_production(ore, amt)

static func _process_energy(s_data, efficiency):
	# Charcoal creation: 10 Wood -> 4 Coal (Charcoal)
	# Requires labor. We'll use a small fraction of population for this if wood is available.
	# We reserve a floor of wood for heating/cooking (approx population / divisor)
	var wood_reserve = int(s_data.population / Globals.WOOD_FUEL_POP_DIVISOR) + 50
	if s_data.inventory.get("wood", 0) > wood_reserve + 20:
		var burner_capacity = int(s_data.population * efficiency * 0.05) # 5% of pop can be burners
		# Each burner can process 10 wood per day
		var wood_to_burn = min(s_data.inventory["wood"] - wood_reserve, burner_capacity * 10)
		if wood_to_burn >= 10:
			var units = int(wood_to_burn / 10)
			s_data.inventory["wood"] -= units * 10
			s_data.add_inventory("coal", units * 4)
			GameState.track_consumption("wood", units * 10)
			GameState.track_production("coal", units * 4)

@warning_ignore("shadowed_global_identifier")
@warning_ignore("shadowed_global_identifier")
@warning_ignore("shadowed_global_identifier")
static func _process_housing(s_data):
	var pop = s_data.population
	# Total capacity is Organic Houses (5 ppl each) + Housing Districts (100 ppl per level)
	var cap = s_data.cache_housing_cap
	
	if pop > cap * 0.8:
		# If no current progress, try to start a new house
		if s_data.house_progress <= 0.0:
			if s_data.inventory.get("wood", 0) >= Globals.WOOD_PER_HOUSE:
				s_data.inventory["wood"] -= Globals.WOOD_PER_HOUSE
				s_data.house_progress = 0.001 # Start project
		
		# Advance progress if a project is active
		if s_data.house_progress > 0.0:
			var building_labor = max(1.0, float(pop) * 0.05) # 5% of residents build their own homes
			s_data.house_progress += (building_labor / Globals.LABOR_PER_HOUSE)
			
			if s_data.house_progress >= 1.0:
				s_data.houses += 1
				s_data.house_progress = 0.0

static func _process_organic_industry(s_data, burgher_labor):
	if burgher_labor <= 0: return # Hamlets or small villages don't have burghers
	
	# Industry Slots based on population density + MARKET MULTIPLIERS
	var slots_base = int(s_data.population / Globals.POP_PER_INDUSTRY_SLOT)
	var slots_mult = 1.0 + (s_data.buildings.get("market", 0) * 0.5) + (s_data.buildings.get("merchant_guild", 0) * 1.5)
	var total_slots = int(slots_base * slots_mult)
	if total_slots <= 0: return

	# Evaluate Industry Opportunities
	var inv = s_data.inventory
	var options = []
	
	# 1. Weaver
	var p_wool = get_price("wool", s_data)
	var p_cloth = get_price("cloth", s_data)
	options.append({"id": "weaver", "profit": (p_cloth * 0.5 - p_wool), "req": "wool"})
	
	# 2. Blacksmith
	var p_iron = get_price("iron", s_data)
	var p_coal = get_price("coal", s_data)
	var p_steel = get_price("steel", s_data)
	options.append({"id": "blacksmith", "profit": (p_steel * 0.5 - (p_iron + p_coal * 0.5)), "req": "iron"})
	
	# 3. Tannery
	var p_hides = get_price("hides", s_data)
	var p_leather = get_price("leather", s_data)
	options.append({"id": "tannery", "profit": (p_leather * 1.0 - p_hides), "req": "hides"})
	
	# 4. Brewery
	var p_ale = get_price("ale", s_data)
	var p_grain = get_price("grain", s_data)
	options.append({"id": "brewery", "profit": (p_ale * 0.5 - p_grain), "req": "grain"})

	# 5. Goldsmith (Luxury)
	var p_gold = get_price("gold", s_data)
	var p_jewelry = get_price("jewelry", s_data)
	options.append({"id": "goldsmith", "profit": (p_jewelry * 0.2 - p_gold), "req": "gold"})
	
	# 6. Tailor
	var p_fine = get_price("fine_garments", s_data)
	options.append({"id": "tailor", "profit": (p_fine * 0.5 - p_cloth), "req": "cloth"})

	# 7. Bronzesmith
	var p_copper = get_price("copper", s_data)
	var p_tin = get_price("tin", s_data)
	var p_bronze = get_price("bronze", s_data)
	options.append({"id": "bronzesmith", "profit": (p_bronze * 1.0 - (p_copper + p_tin * 0.5)), "req": "copper"})
	
	# 8. Brickmaker
	var p_clay = get_price("clay", s_data)
	var p_bricks = get_price("bricks", s_data)
	options.append({"id": "brickmaker", "profit": (p_bricks * 1.0 - (p_clay + p_coal * 0.5)), "req": "clay"})
	
	# 9. Toolmaker
	var p_wood = get_price("wood", s_data)
	var p_tools = get_price("tools", s_data)
	options.append({"id": "toolmaker", "profit": (p_tools * 0.5 - (p_iron * 0.5 + p_wood * 0.5)), "req": "iron"})
	
	# Sort by best profit
	options.sort_custom(func(a, b): return a["profit"] > b["profit"])
	
	# Allocate Slots
	var used_slots = 0
	s_data.organic_industries = {}
	for opt in options:
		if used_slots >= total_slots: break
		if opt["profit"] > 0 and inv.get(opt["req"], 0) > 10:
			s_data.organic_industries[opt["id"]] = s_data.organic_industries.get(opt["id"], 0) + 1
			used_slots += 1
			
	# Execute
	var labor_per_slot = burgher_labor / max(1, used_slots)
	for industry in s_data.organic_industries:
		var slots = s_data.organic_industries[industry]
		var labor = int(slots * labor_per_slot)
		_execute_industry_work(s_data, industry, labor)

static func _execute_industry_work(s_data, industry_id, labor):
	var inv = s_data.inventory
	var b_mult = 1.0 + (s_data.buildings.get(industry_id, 0) * 1.0) # Organic production is doubled with a guild building
	
	match industry_id:
		"weaver":
			# 2 labor + 2 wool -> 1 cloth
			var count = int(min(labor / 2.0, inv.get("wool", 0) / 2.0))
			if count > 0:
				var amt = int(count * b_mult)
				inv["wool"] -= count * 2
				s_data.add_inventory("cloth", amt)
				GameState.track_production("cloth", amt)
				_calculate_dividend(s_data, "weaver", amt, "cloth")
		"blacksmith":
			# 4 labor + 2 iron + 1 coal -> 1 steel
			var count = int(min(labor / 4.0, inv.get("iron", 0) / 2.0, inv.get("coal", 0) / 1.0))
			if count > 0:
				var amt = int(count * b_mult)
				inv["iron"] -= count * 2
				inv["coal"] -= count * 1
				s_data.add_inventory("steel", amt)
				GameState.track_production("steel", amt)
				_calculate_dividend(s_data, "blacksmith", amt, "steel")
		"tannery":
			# 2 labor + 2 hides -> 1 leather
			var count = int(min(labor / 2.0, inv.get("hides", 0) / 2.0))
			if count > 0:
				var amt = int(count * b_mult)
				inv["hides"] -= count * 2
				s_data.add_inventory("leather", amt)
				GameState.track_production("leather", amt)
				_calculate_dividend(s_data, "tannery", amt, "leather")
		"brewery":
			# 2 labor + 2 grain -> 1 ale
			var count = int(min(labor / 2.0, inv.get("grain", 0) / 2.0))
			if count > 0:
				inv["grain"] -= count * 2
				s_data.add_inventory("ale", int(count * b_mult))
				GameState.track_production("ale", int(count * b_mult))
		"brickmaker":
			# 3 labor + 3 clay + 1 coal -> 2 bricks
			var count = int(min(labor / 3.0, inv.get("clay", 0) / 3.0, inv.get("coal", 0) / 1.0))
			if count > 0:
				inv["clay"] -= count * 3
				inv["coal"] -= count * 1
				s_data.add_inventory("bricks", int(count * 2 * b_mult))
				GameState.track_production("bricks", int(count * 2 * b_mult))
		"toolmaker":
			# 4 labor + 1 iron + 2 wood -> 2 tools
			var count = int(min(labor / 4.0, inv.get("iron", 0) / 1.0, inv.get("wood", 0) / 2.0))
			if count > 0:
				inv["iron"] -= count * 1
				inv["wood"] -= count * 2
				s_data.add_inventory("tools", int(count * 2 * b_mult))
				GameState.track_production("tools", int(count * 2 * b_mult))
		"goldsmith":
			# 8 labor + 1 gold + 1 coal -> 1 jewelry
			var count = int(min(labor / 8.0, inv.get("gold", 0) / 1.0, inv.get("coal", 0) / 1.0))
			if count > 0:
				inv["gold"] -= count * 1
				inv["coal"] -= count * 1
				s_data.add_inventory("jewelry", int(count * b_mult))
				GameState.track_production("jewelry", int(count * b_mult))
		"tailor":
			# 4 labor + 2 cloth -> 1 fine garments
			var count = int(min(labor / 4.0, inv.get("cloth", 0) / 2.0))
			if count > 0:
				inv["cloth"] -= count * 2
				s_data.add_inventory("fine_garments", int(count * b_mult))
				GameState.track_production("fine_garments", int(count * b_mult))
		"bronzesmith":
			# 2 labor + 2 copper + 1 tin -> 2 bronze
			var count = int(min(labor / 2.0, inv.get("copper", 0) / 2.0, inv.get("tin", 0) / 1.0))
			if count > 0:
				inv["copper"] -= count * 2
				inv["tin"] -= count * 1
				s_data.add_inventory("bronze", int(count * 2 * b_mult))
				GameState.track_production("bronze", int(count * 2 * b_mult))

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

@warning_ignore("shadowed_global_identifier")
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

@warning_ignore("shadowed_global_identifier")
static func get_item_price(type_key, mat_key, qual, is_commission := false):
	var base = GameData.ITEMS.get(type_key, {}).get("price", 100)
	var mat_mult = GameData.MATERIALS.get(mat_key, {}).get("price_mult", 1.0)
	var qual_mult = 1.0
	match qual:
		"fine": qual_mult = 1.5
		"masterwork": qual_mult = 3.0
		"artifact": qual_mult = 10.0
	var price = int(base * mat_mult * qual_mult)
	if is_commission: price = int(price * 1.5)
	return price

@warning_ignore("shadowed_global_identifier")
static func create_item(type_key, material_key, quality := "standard"):
	var type_data = GameData.ITEMS.get(type_key)
	if not type_data: return {}
	var item = type_data.duplicate(true)
	item["material"] = material_key
	item["quality"] = quality
	item["price"] = get_item_price(type_key, material_key, quality)
	var mat_data = GameData.MATERIALS.get(material_key, {})
	if item.has("damage"): item["damage"] = int(item["damage"] * mat_data.get("dmg_mult", 1.0))
	if item.has("protection"): item["protection"] = int(item["protection"] * mat_data.get("prot_mult", 1.0))
	if item.has("weight"): item["weight"] *= mat_data.get("weight_mult", 1.0)
	
	# Ammo Specifics
	if type_key == "arrows" or type_key == "bolts":
		match material_key:
			"copper":
				item["dmg_mod"] = -2
				item["penetration_mod"] = 0.8
			"iron":
				item["dmg_mod"] = 0
				item["penetration_mod"] = 1.0
			"steel":
				item["dmg_mod"] = 2 if type_key == "arrows" else 3
				item["penetration_mod"] = 1.5 if type_key == "arrows" else 1.8
				
	return item

@warning_ignore("shadowed_global_identifier")
static func buy_resource(s_data, res_name, amount, player_obj):
	var price = get_price(res_name, s_data)
	var total_cost = price * amount
	if player_obj.crowns >= total_cost and s_data.inventory.get(res_name, 0) >= amount:
		player_obj.crowns -= total_cost
		s_data.crown_stock += total_cost
		if amount > 0:
			s_data.inventory[res_name] -= amount
		player_obj.add_to_stash(res_name, amount)
		return true
	return false

@warning_ignore("shadowed_global_identifier")
static func sell_resource(s_data, res_name, amount, player_obj):
	var price = int(get_price(res_name, s_data) * 0.7)
	var total_val = price * amount
	if s_data.crown_stock >= total_val and player_obj.get_stash_count(res_name) >= amount:
		s_data.crown_stock -= total_val
		player_obj.crowns += total_val
		s_data.inventory[res_name] = s_data.inventory.get(res_name, 0) + amount
		player_obj.remove_from_stash(res_name, amount)
		return true
	return false

@warning_ignore("shadowed_global_identifier")
static func get_quality_rank(q) -> int:
	match q:
		"rusty": return 0
		"standard": return 1
		"fine": return 2
		"masterwork": return 3
	return 0

@warning_ignore("shadowed_global_identifier")
static func get_kit_cost(player_obj, c_name, is_commission = false) -> int:
	if not player_obj.unit_classes.has(c_name): return 0
	var bp = player_obj.unit_classes[c_name]
	var total = 0
	for slot in bp:
		var req = bp[slot]
		if req.get("type") != "none":
			total += get_item_price(req.get("type"), req.get("material"), req.get("quality"), is_commission)
	return total

@warning_ignore("shadowed_global_identifier")
static func get_reequip_cost(player_obj, c_name) -> int:
	var total = 0
	if not player_obj.unit_classes.has(c_name): return 0
	var bp = player_obj.unit_classes[c_name]
	for u_obj in player_obj.roster:
		if u_obj.assigned_class == c_name:
			var readiness = check_readiness(player_obj, u_obj)
			for slot in readiness.get("missing", []):
				var req = bp[slot]
				total += get_item_price(req.get("type"), req.get("material"), req.get("quality"), true)
	return total

@warning_ignore("shadowed_global_identifier")
static func fund_class_commissions(gs, s_pos, c_name):
	var player_obj = gs.player
	if not player_obj.unit_classes.has(c_name): return
	var cost = get_reequip_cost(player_obj, c_name)
	if cost <= 0:
		gs.add_log("All units of class %s are already equipped." % c_name)
		return
		
	if player_obj.crowns >= cost:
		player_purchase_reequip(gs, s_pos, c_name, cost)
	else:
		gs.add_log("Cannot afford bulk order for %s (Need %dg)." % [c_name, cost])

@warning_ignore("shadowed_global_identifier")
static func player_purchase_reequip(gs, s_pos, c_name, cost):
	var player_obj = gs.player
	player_obj.crowns -= cost
	var bp = player_obj.unit_classes[c_name]
	var orders = {} # { "type_mat_qual": count }
	
	for u_obj in player_obj.roster:
		if u_obj.assigned_class == c_name:
			var readiness = check_readiness(player_obj, u_obj)
			for slot in readiness.get("missing", []):
				var req = bp[slot]
				var key = "%s|%s|%s" % [req.get("type"), req.get("material"), req.get("quality")]
				orders[key] = orders.get(key, 0) + 1
	
	for key in orders:
		var parts = key.split("|")
		var count = orders[key]
		var item_template = create_item(parts[0], parts[1], parts[2])
		player_obj.commissions.append({
			"item_data": item_template,
			"count": count,
			"remaining_turns": 24 + (count * 2),
			"s_pos": s_pos
		})
		
	gs.add_log("Funded bulk commissions for %s: %dg spent." % [c_name, cost])
	gs.emit_signal("map_updated")

@warning_ignore("shadowed_global_identifier")
static func create_class(gs, c_name, reqs):
	gs.player.unit_classes[c_name] = reqs
	gs.add_log("Created unit class: %s" % c_name)
	gs.emit_signal("map_updated")

@warning_ignore("shadowed_global_identifier")
static func assign_class(gs, unit_idx, c_name):
	var player_obj = gs.player
	if unit_idx < 0 or unit_idx >= player_obj.roster.size(): return
	player_obj.roster[unit_idx].assigned_class = c_name
	gs.add_log("Assigned %s to class %s." % [player_obj.roster[unit_idx].name, c_name])
	gs.emit_signal("map_updated")

@warning_ignore("shadowed_global_identifier")
static func check_readiness(player_obj, u_obj) -> Dictionary:
	if u_obj.assigned_class == "" or not player_obj.unit_classes.has(u_obj.assigned_class):
		return {"is_ready": true, "missing": []}
		
	var blueprint = player_obj.unit_classes[u_obj.assigned_class]
	var missing = []
	
	for bp_key in blueprint:
		var req = blueprint[bp_key]
		if not req or req.get("type") == "none": continue
		
		var item = null
		if bp_key in ["main_hand", "off_hand"]:
			item = u_obj.equipment.get(bp_key)
		elif bp_key == "cover":
			item = u_obj.equipment.get("torso", {}).get("cover")
		else:
			var parts = bp_key.split("_")
			if parts.size() >= 2:
				var layer = parts[parts.size() - 1]
				var slot = bp_key.trim_suffix("_" + layer)
				if slot == "arms": slot = "l_arm"
				elif slot == "hands": slot = "l_hand"
				elif slot == "legs": slot = "l_leg"
				elif slot == "feet": slot = "l_foot"
				if u_obj.equipment.has(slot) and u_obj.equipment[slot] is Dictionary:
					item = u_obj.equipment[slot].get(layer)
		
		if not item:
			missing.append(bp_key)
			continue
			
		var matches = true
		if item.get("type_key") != req.get("type"): matches = false
		if req.get("material") != "any" and item.get("material") != req.get("material"): matches = false
		if get_quality_rank(item.get("quality", "standard")) < get_quality_rank(req.get("quality", "standard")): matches = false
		
		if not matches:
			missing.append(bp_key)
			
	return {"is_ready": missing.size() == 0, "missing": missing}

@warning_ignore("shadowed_global_identifier")
static func auto_equip_all(gs):
	var player_obj = gs.player
	var count = 0
	for i in range(player_obj.roster.size()):
		var u_obj = player_obj.roster[i]
		if u_obj.assigned_class == "" or not player_obj.unit_classes.has(u_obj.assigned_class): continue
		
		var blueprint = player_obj.unit_classes[u_obj.assigned_class]
		for bp_key in blueprint:
			var req = blueprint[bp_key]
			if not req or req.get("type") == "none": continue
			
			var current_item = null
			if bp_key in ["main_hand", "off_hand"]:
				current_item = u_obj.equipment.get(bp_key)
			elif bp_key == "cover":
				current_item = u_obj.equipment.get("torso", {}).get("cover")
			else:
				var parts = bp_key.split("_")
				if parts.size() >= 2:
					var layer = parts[parts.size() - 1]
					var slot = bp_key.trim_suffix("_" + layer)
					if slot == "arms": slot = "l_arm"
					elif slot == "hands": slot = "l_hand"
					elif slot == "legs": slot = "l_leg"
					elif slot == "feet": slot = "l_foot"
					if u_obj.equipment.has(slot):
						current_item = u_obj.equipment[slot].get(layer)
			
			if current_item:
				var matches = true
				if current_item.get("type_key") != req.get("type"): matches = false
				if req.get("material") != "any" and current_item.get("material") != req.get("material"): matches = false
				if get_quality_rank(current_item.get("quality", "standard")) < get_quality_rank(req.get("quality", "standard")): matches = false
				if matches: continue
			
			var best_idx = -1
			var best_rank = -1
			for j in range(player_obj.stash.size()):
				var item = player_obj.stash[j]
				if item.get("id") == req.get("type"):
					if req.get("material") != "any" and item.get("material") != req.get("material"): continue
					var rank = get_quality_rank(item.get("quality", "standard"))
					if rank >= get_quality_rank(req.get("quality", "standard")) and rank > best_rank:
						best_rank = rank
						best_idx = j
			
			if best_idx != -1:
				var item = player_obj.stash[best_idx]
				player_obj.stash.remove_at(best_idx)
				if bp_key in ["main_hand", "off_hand"]:
					u_obj.equipment[bp_key] = item
				elif bp_key == "cover":
					u_obj.equipment["torso"]["cover"] = item
				else:
					var parts = bp_key.split("_")
					var layer = parts[parts.size() - 1]
					var slot = bp_key.trim_suffix("_" + layer)
					if slot == "arms":
						u_obj.equipment["l_arm"][layer] = item
						u_obj.equipment["r_arm"][layer] = item.duplicate()
					elif slot == "hands":
						u_obj.equipment["l_hand"][layer] = item
						u_obj.equipment["r_hand"][layer] = item.duplicate()
					elif slot == "legs":
						u_obj.equipment["l_leg"][layer] = item
						u_obj.equipment["r_leg"][layer] = item.duplicate()
					elif slot == "feet":
						u_obj.equipment["l_foot"][layer] = item
						u_obj.equipment["r_foot"][layer] = item.duplicate()
					else:
						u_obj.equipment[slot][layer] = item
				count += 1
	if count > 0:
		for u in player_obj.roster:
			u.base_speed = GameData.calculate_unit_speed(u)
			u.speed = u.base_speed
		player_obj.commander.base_speed = GameData.calculate_unit_speed(player_obj.commander)
		player_obj.commander.speed = player_obj.commander.base_speed
		gs.add_log("Auto-equipped %d items from stash." % count)
		gs.emit_signal("map_updated")

@warning_ignore("shadowed_global_identifier")
static func commission_items(gs, s_pos, type_key, mat_key, qual, count):
	var player_obj = gs.player
	var item_template = create_item(type_key, mat_key, qual)
	if not item_template: return
	
	var unit_price = get_item_price(type_key, mat_key, qual, true)
	
	var total_cost = unit_price * count
	if player_obj.crowns >= total_cost:
		player_obj.crowns -= total_cost
		player_obj.commissions.append({
			"item_data": item_template,
			"count": count,
			"remaining_turns": 24 + (count * 2),
			"s_pos": s_pos
		})
		gs.add_log("Commissioned %dx %s for %dg." % [count, item_template.get("name", "Item"), total_cost])
		gs.emit_signal("map_updated")
	else:
		gs.add_log("Cannot afford commission (Need %dg)." % total_cost)

@warning_ignore("shadowed_global_identifier")
static func perform_equip(gs, u_obj, stash_idx):
	var player_obj = gs.player
	if stash_idx < 0 or stash_idx >= player_obj.stash.size(): return
	var item = player_obj.stash[stash_idx]
	var slot = item.get("slot", "main_hand")
	
	if slot in ["main_hand", "off_hand"]:
		if slot == "main_hand" and item.get("hands", 1) == 2:
			if u_obj.equipment.get("off_hand") != null:
				player_obj.stash.append(u_obj.equipment["off_hand"])
				u_obj.equipment["off_hand"] = null
				gs.add_log("Unequipped off-hand to use two-handed %s." % item.get("name", "Item"))
		elif slot == "off_hand":
			var main = u_obj.equipment.get("main_hand")
			if main and main.get("hands", 1) == 2:
				player_obj.stash.append(main)
				u_obj.equipment["main_hand"] = null
				gs.add_log("Unequipped two-handed %s to use off-hand." % main.get("name", "Item"))

		if u_obj.equipment.get(slot) != null:
			player_obj.stash.append(u_obj.equipment[slot])
		u_obj.equipment[slot] = item
	else:
		var layer = item.get("layer", "armor")
		var coverage = item.get("coverage", [slot])
		
		var to_remove = []
		for part in coverage:
			var existing = u_obj.equipment.get(part, {}).get(layer)
			if existing and not existing in to_remove:
				to_remove.append(existing)
		
		for old_item in to_remove:
			var old_cov = old_item.get("coverage", [])
			for part in old_cov:
				if u_obj.equipment.get(part, {}).get(layer) == old_item:
					u_obj.equipment[part][layer] = null
			player_obj.stash.append(old_item)
			
		for part in coverage:
			u_obj.equipment[part][layer] = item
	
	player_obj.stash.remove_at(stash_idx)
	u_obj.base_speed = GameData.calculate_unit_speed(u_obj)
	u_obj.speed = u_obj.base_speed
	gs.add_log("Equipped %s to %s." % [item.get("name", "Item"), u_obj.name])
	gs.emit_signal("map_updated")

@warning_ignore("shadowed_global_identifier")
static func perform_unequip(gs, u_obj, slot, layer = ""):
	var player_obj = gs.player
	var item = null
	if slot in ["main_hand", "off_hand"]:
		item = u_obj.equipment[slot]
		u_obj.equipment[slot] = null
	elif u_obj.equipment.has(slot) and layer != "":
		item = u_obj.equipment[slot][layer]
		if item:
			var coverage = item.get("coverage", [slot])
			for part in coverage:
				if u_obj.equipment[part][layer] == item:
					u_obj.equipment[part][layer] = null
	
	if item != null:
		player_obj.stash.append(item)
		u_obj.base_speed = GameData.calculate_unit_speed(u_obj)
		u_obj.speed = u_obj.base_speed
		gs.add_log("Unequipped %s from %s." % [item.get("name", "Item"), u_obj.name])
		gs.emit_signal("map_updated")

@warning_ignore("shadowed_global_identifier")
static func get_market_info(s_data, res):
	var val = get_price(res, s_data)
	var buy = get_buy_price(res, s_data)
	var sell = get_sell_price(res, s_data)
	
	var state = "normal"
	var base = GameData.BASE_PRICES.get(res, 10)
	
	if val > base * 1.5: state = "high_demand"
	elif val < base * 0.7: state = "oversupplied"
	
	return {
		"buy": buy,
		"sell": sell,
		"state": state
	}

static func update_trade_networks(gs):
	# 1. Clean up old or expired contracts
	for i in range(gs.trade_contracts.size() - 1, -1, -1):
		var contract = gs.trade_contracts[i]
		
		# Validate that the assigned caravan is still pursuing this contract
		var caravan_active = false
		for c in gs.caravans:
			if c.get_instance_id() == contract.get("caravan_id", -1):
				if c.has_meta("contract_id") and c.get_meta("contract_id") == contract["id"]:
					if c.state != "idle":
						caravan_active = true
				break
		
		if not caravan_active:
			contract["status"] = "cancelled"

		if contract["status"] == "cancelled" or contract["status"] == "completed":
			gs.trade_contracts.remove_at(i)

	# 2. Check for new high-value "Matches"
	if gs.world_market_orders.is_empty(): return
	
	# Group caravans by origin to see who is available
	var available_caravans = []
	for c in gs.caravans:
		# If caravan lost its contract (e.g. killed/reloaded), reset it
		if c.state != "idle" and not gs.trade_contracts.any(func(con): return con.get("caravan_id") == c.get_instance_id()):
			c.state = "idle"
			c.target_pos = Vector2i(-1, -1)
			
		if c.state == "idle":
			available_caravans.append(c)
	
	if available_caravans.is_empty(): return
	
	# Process existing Buy Orders (Demands)
	var demands = gs.world_market_orders.duplicate()
	demands.sort_custom(func(a, b): return a["price_offered"] > b["price_offered"])
	
	for order in demands:
		if available_caravans.is_empty(): break
		
		# Skip if order is already fully matched by active contracts
		var matched_amt = 0
		for con in gs.trade_contracts:
			if con["buyer_pos"] == order["buyer_pos"] and con["resource"] == order["resource"]:
				matched_amt += con["amount"]
		
		if matched_amt >= order["amount"]: continue
		
		var res = order["resource"]
		var buyer_pos = order["buyer_pos"]
		
		var best_supplier_pos = Vector2i(-1, -1)
		var best_profit = -1000.0
		
		for s_pos in gs.settlements:
			var s_data = gs.settlements[s_pos]
			if s_pos == buyer_pos: continue
			if gs.get_relation(order["faction"], s_data.faction) == "war": continue
			
			var stock = s_data.inventory.get(res, 0)
			
			# RESERVATION SYSTEM: Check how much stock is already booked for pickup
			var reserved = 0
			for con in gs.trade_contracts:
				if con["seller_pos"] == s_pos and con["resource"] == res and con["status"] == "active":
					reserved += con["amount"]
			
			if (stock - reserved) < 20: continue 
			
			var buy_price = get_price(res, s_data)
			var profit = order["price_offered"] - buy_price
			var dist = s_pos.distance_to(buyer_pos)
			
			var score = profit - (dist * 0.1)
			if s_data.faction == order["faction"]: score += 50 
			
			if score > best_profit:
				best_profit = score
				best_supplier_pos = s_pos
				
		if best_supplier_pos != Vector2i(-1, -1):
			var best_caravan = null
			var closest_dist = 9999.9
			
			for c in available_caravans:
				var d = c.pos.distance_to(best_supplier_pos)
				if d < closest_dist:
					closest_dist = d
					best_caravan = c
			
			if best_caravan:
				var contract = {
					"id": gs.rng.randi(),
					"seller_pos": best_supplier_pos,
					"buyer_pos": buyer_pos,
					"resource": res,
					"amount": int(min(order["amount"] - matched_amt, 200)),
					"price": order["price_offered"],
					"status": "active",
					"caravan_id": best_caravan.get_instance_id()
				}
				gs.trade_contracts.append(contract)
				
				best_caravan.target_pos = best_supplier_pos
				best_caravan.target_resource = res
				best_caravan.state = "buying"
				best_caravan.final_destination = buyer_pos
				# Use set_meta to avoid modifying class if not needed
				best_caravan.set_meta("contract_id", contract.id)
				
				available_caravans.erase(best_caravan)

@warning_ignore("shadowed_global_identifier")
static func resolve_caravan_trade(gs, caravan_obj):
	var s_pos = caravan_obj.pos
	if not gs.settlements.has(s_pos):
		for k in gs.settlements:
			if k.distance_to(s_pos) < 2:
				s_pos = k
				break
	
	if not gs.settlements.has(s_pos): return

	var s_data = gs.settlements[s_pos]
	var res = caravan_obj.target_resource
	
	if caravan_obj.state == "buying":
		var price = get_price(res, s_data)
		var base_cap = Globals.CARAVAN_CAPACITY_BULK if res in ["wood", "stone", "iron", "grain", "fish", "meat", "leather", "cloth"] else Globals.CARAVAN_CAPACITY_VALUE
		
		# Merchant Guild Multiplier: +50% capacity per level
		var origin_s = gs.settlements.get(caravan_obj.origin)
		var guild_lvl = origin_s.buildings.get("merchant_guild", 0) if origin_s else 0
		var capacity = int(base_cap * (1.0 + (guild_lvl * 0.5)))
		
		var amount = min(s_data.inventory.get(res, 0), capacity)
		var cost = amount * price
		
		if caravan_obj.crowns >= cost and amount > 0:
			caravan_obj.crowns -= cost
			s_data.crown_stock += cost
			s_data.inventory[res] = s_data.inventory.get(res, 0) - amount
			caravan_obj.inventory[res] = caravan_obj.inventory.get(res, 0) + amount
			
			caravan_obj.target_pos = caravan_obj.final_destination
			caravan_obj.state = "selling"
		else:
			caravan_obj.state = "idle"
			caravan_obj.target_pos = Vector2i(-1, -1)
			
	elif caravan_obj.state == "selling":
		var price = get_price(res, s_data)
		
		# WORLD MARKET PULLED TRADE: Identify if this city has a buy order for this resource
		var order_idx = -1
		for i in range(gs.world_market_orders.size()):
			var o = gs.world_market_orders[i]
			if o["buyer_pos"] == s_data.pos and o["resource"] == res:
				order_idx = i
				price = max(price, o["price_offered"]) # GUARANTEE: Higher of market or order price
				break

		var amount = caravan_obj.inventory.get(res, 0)
		var payout = amount * price
		
		if s_data.crown_stock >= payout:
			s_data.crown_stock -= payout
			caravan_obj.crowns += payout
			GameState.track_trade_volume(payout)
			s_data.inventory[res] = s_data.inventory.get(res, 0) + amount
			caravan_obj.inventory[res] = 0
			caravan_obj.state = "idle"
			caravan_obj.target_pos = Vector2i(-1, -1)
			
			# MARK CONTRACT COMPLETED
			if caravan_obj.has_meta("contract_id"):
				var c_id = caravan_obj.get_meta("contract_id")
				for con in gs.trade_contracts:
					if con["id"] == c_id:
						con["status"] = "completed"
						break
				caravan_obj.remove_meta("contract_id")
			
			if order_idx != -1:
				var order = gs.world_market_orders[order_idx]
				order["amount"] -= amount
				gs.track_buy_order("fulfilled")
				if order["amount"] <= 0:
					gs.world_market_orders.remove_at(order_idx)

			s_data.population += int(amount / 5.0) + 1
			s_data.happiness = min(100, s_data.happiness + 2)
			if not s_data.influence.has(caravan_obj.faction): s_data.influence[caravan_obj.faction] = 0
			s_data.influence[caravan_obj.faction] += 5
		else:
			var can_afford = int(s_data.crown_stock / price) if price > 0 else 0
			if can_afford > 0:
				var sell = min(amount, can_afford)
				var actual_payout = sell * price
				s_data.crown_stock -= actual_payout
				caravan_obj.crowns += actual_payout
				GameState.track_trade_volume(actual_payout)
				s_data.inventory[res] = s_data.inventory.get(res, 0) + sell
				caravan_obj.inventory[res] = caravan_obj.inventory.get(res, 0) - sell
				
				# Order fulfillment (partial)
				if order_idx != -1:
					gs.world_market_orders[order_idx]["amount"] -= sell
					GameState.track_buy_order("fulfilled")
					if gs.world_market_orders[order_idx]["amount"] <= 0:
						gs.world_market_orders.remove_at(order_idx)

				s_data.population += int(sell / 5.0) + 1
			
			if caravan_obj.inventory.get(res, 0) <= 0:
				caravan_obj.state = "idle"
	
	if s_data.faction == caravan_obj.faction and caravan_obj.crowns > 500:
		var tax = caravan_obj.crowns - 500
		gs.get_faction(caravan_obj.faction).treasury += tax
		caravan_obj.crowns = 500
		gs.track_tax(tax)
		caravan_obj.target_pos = Vector2i(-1, -1)

static func _calculate_dividend(s_data, building_id, amount, res_name):
	# player_shares: { "mine": 0.5, "farm": 1.0 }
	if not s_data.player_shares.has(building_id) or amount <= 0:
		return
		
	var share = s_data.player_shares[building_id]
	if share <= 0: return
	
	var price = get_price(res_name, s_data)
	var total_value = amount * price
	var dividend = int(total_value * share)
	
	if dividend > 0:
		# Reference GameState directly if needed OR assume it is managed in the session
		if GameState.player:
			GameState.player.crowns += dividend
			s_data.crown_stock -= dividend
