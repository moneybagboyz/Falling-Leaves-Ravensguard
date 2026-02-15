extends RefCounted
class_name ProductionSystem

# ProductionSystem: Settlement resource production and labor allocation
# Handles terrain scanning, mining, farming, fishing, forestry, and organic industries

# GameData and GameState are autoloads - no need to preload
const Globals = preload("res://src/core/Globals.gd")
const PricingSystem = preload("res://src/economy/PricingSystem.gd")
const ConsumptionSystem = preload("res://src/economy/ConsumptionSystem.gd")

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
		var p_grain = PricingSystem.get_price("grain", s_data)
		var p_fish = PricingSystem.get_price("fish", s_data)
		var p_stone = PricingSystem.get_price("stone", s_data)
		var p_wood = PricingSystem.get_price("wood", s_data)
		var p_meat = PricingSystem.get_price("meat", s_data)
		var p_hides = PricingSystem.get_price("hides", s_data)
		var p_wool = PricingSystem.get_price("wool", s_data)
		var p_furs = PricingSystem.get_price("furs", s_data)
		var p_horses = PricingSystem.get_price("horses", s_data)
		
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
			ConsumptionSystem._calculate_dividend(s_data, "farm", g_prod, "grain")
		"fishing":
			b_mult = 1.0 + (s_data.buildings.get("fishery", 0) * 0.5)
			var f_prod = int(labor * Globals.FISHING_YIELD_BASE * b_mult / Globals.DAYS_PER_YEAR)
			s_data.add_inventory("fish", f_prod)
			GameState.track_production("fish", f_prod)
			ConsumptionSystem._calculate_dividend(s_data, "fishery", f_prod, "fish")
		"mining":
			b_mult = 1.0 + (s_data.buildings.get("mine", 0) * 0.5)
			var stone_prod = int(labor * (4.0 * b_mult) / 30.0)
			s_data.add_inventory("stone", stone_prod)
			GameState.track_production("stone", stone_prod)
			_process_mine_resources(s_data, labor)
			ConsumptionSystem._calculate_dividend(s_data, "mine", stone_prod, "stone")
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
			ConsumptionSystem._calculate_dividend(s_data, "lumber_mill", wood, "wood")
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
	var p_wool = PricingSystem.get_price("wool", s_data)
	var p_cloth = PricingSystem.get_price("cloth", s_data)
	options.append({"id": "weaver", "profit": (p_cloth * 0.5 - p_wool), "req": "wool"})
	
	# 2. Blacksmith
	var p_iron = PricingSystem.get_price("iron", s_data)
	var p_coal = PricingSystem.get_price("coal", s_data)
	var p_steel = PricingSystem.get_price("steel", s_data)
	options.append({"id": "blacksmith", "profit": (p_steel * 0.5 - (p_iron + p_coal * 0.5)), "req": "iron"})
	
	# 3. Tannery
	var p_hides = PricingSystem.get_price("hides", s_data)
	var p_leather = PricingSystem.get_price("leather", s_data)
	options.append({"id": "tannery", "profit": (p_leather * 1.0 - p_hides), "req": "hides"})
	
	# 4. Brewery
	var p_ale = PricingSystem.get_price("ale", s_data)
	var p_grain = PricingSystem.get_price("grain", s_data)
	options.append({"id": "brewery", "profit": (p_ale * 0.5 - p_grain), "req": "grain"})

	# 5. Goldsmith (Luxury)
	var p_gold = PricingSystem.get_price("gold", s_data)
	var p_jewelry = PricingSystem.get_price("jewelry", s_data)
	options.append({"id": "goldsmith", "profit": (p_jewelry * 0.2 - p_gold), "req": "gold"})
	
	# 6. Tailor
	var p_fine = PricingSystem.get_price("fine_garments", s_data)
	options.append({"id": "tailor", "profit": (p_fine * 0.5 - p_cloth), "req": "cloth"})

	# 7. Bronzesmith
	var p_copper = PricingSystem.get_price("copper", s_data)
	var p_tin = PricingSystem.get_price("tin", s_data)
	var p_bronze = PricingSystem.get_price("bronze", s_data)
	options.append({"id": "bronzesmith", "profit": (p_bronze * 1.0 - (p_copper + p_tin * 0.5)), "req": "copper"})
	
	# 8. Brickmaker
	var p_clay = PricingSystem.get_price("clay", s_data)
	var p_bricks = PricingSystem.get_price("bricks", s_data)
	options.append({"id": "brickmaker", "profit": (p_bricks * 1.0 - (p_clay + p_coal * 0.5)), "req": "clay"})
	
	# 9. Toolmaker
	var p_wood = PricingSystem.get_price("wood", s_data)
	var p_tools = PricingSystem.get_price("tools", s_data)
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
				ConsumptionSystem._calculate_dividend(s_data, "weaver", amt, "cloth")
		"blacksmith":
			# 4 labor + 2 iron + 1 coal -> 1 steel
			var count = int(min(labor / 4.0, inv.get("iron", 0) / 2.0, inv.get("coal", 0) / 1.0))
			if count > 0:
				var amt = int(count * b_mult)
				inv["iron"] -= count * 2
				inv["coal"] -= count * 1
				s_data.add_inventory("steel", amt)
				GameState.track_production("steel", amt)
				ConsumptionSystem._calculate_dividend(s_data, "blacksmith", amt, "steel")
		"tannery":
			# 2 labor + 2 hides -> 1 leather
			var count = int(min(labor / 2.0, inv.get("hides", 0) / 2.0))
			if count > 0:
				var amt = int(count * b_mult)
				inv["hides"] -= count * 2
				s_data.add_inventory("leather", amt)
				GameState.track_production("leather", amt)
				ConsumptionSystem._calculate_dividend(s_data, "tannery", amt, "leather")
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

