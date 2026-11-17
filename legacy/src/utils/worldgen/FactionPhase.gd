class_name FactionPhase
extends WorldGenPhase

## Handles faction generation and territorial assignment

const GDFaction = preload("res://src/data/GDFaction.gd")
const GameData = preload("res://src/core/GameData.gd")
const EconomyManager = preload("res://src/managers/EconomyManager.gd")

func get_phase_name() -> String:
	return "Factions"

func execute(context: WorldGenContext) -> bool:
	var w = context.width
	var h = context.height
	var rng = context.rng
	
	step_completed.emit("ESTABLISHING FACTIONS...")
	
	# Pre-calculate province adjacency
	var p_neighbors = _calc_province_neighbors(context)
	
	# Generate factions
	var faction_list: Array[GDFaction] = []
	var f_prefixes = ["Kingdom of", "The", "Empire of", "Principality of", "Holy", "Grand", "United"]
	var f_roots = ["Valia", "Dorn", "Oros", "Kesh", "Zun", "Lith", "Aeth", "Ryver", "Esk", "Beln", "Gorth", "Mord"]
	var f_suffixes = ["Kingdom", "Empire", "Lands", "Dominion", "States", "Alliance", "Hegemony"]
	var f_colors = ["coral", "chartreuse", "blueviolet", "dark_orange", "deep_pink", "deep_sky_blue", "gold", "lawn_green", "light_pink", "medium_spring_green", "orchid", "spring_green", "tomato", "turquoise", "yellow"]
	
	for i in range(context.num_factions):
		var f_id = "faction_%d" % i
		var f_name = ""
		var roll = rng.randf()
		if roll < 0.33:
			f_name = "%s %s" % [f_prefixes[rng.randi() % f_prefixes.size()], f_roots[rng.randi() % f_roots.size()]]
		elif roll < 0.66:
			f_name = "%s %s" % [f_roots[rng.randi() % f_roots.size()], f_suffixes[rng.randi() % f_suffixes.size()]]
		else:
			f_name = f_roots[rng.randi() % f_roots.size()]
		
		var f_data = GDFaction.new(f_id, f_name)
		f_data.color = f_colors[i % f_colors.size()]
		faction_list.append(f_data)
		context.factions[f_id] = f_data
	
	# Select capital cities
	var province_owners = {}
	var province_grid_arr = context.province_grid.to_legacy_grid()
	
	var cap_pool = []
	for s_pos in context.world_settlements:
		var s = context.world_settlements[s_pos]
		cap_pool.append({"pos": s_pos, "pop": s.population})
	cap_pool.sort_custom(func(a, b): return a.pop > b.pop)
	
	var capitals = []
	var f_idx = 0
	var min_cap_dist = min(w, h) * 0.2
	
	for cand in cap_pool:
		if f_idx >= faction_list.size(): break
		
		var too_close = false
		for c in capitals:
			if cand.pos.distance_to(c) < min_cap_dist:
				too_close = true
				break
		if too_close: continue
		
		var f_obj = faction_list[f_idx]
		var f_id = f_obj.id
		var s = context.world_settlements[cand.pos]
		s.faction = f_id
		s.tier = 5
		s.is_capital = true
		s.type = "capital"
		s.radius = 5
		capitals.append(cand.pos)
		
		var p_id = province_grid_arr[cand.pos.y][cand.pos.x]
		if p_id != -1:
			province_owners[p_id] = f_id
		f_idx += 1
	
	# Flood-fill expansion
	var f_queue = []
	for p_id in province_owners:
		f_queue.append(p_id)
	
	while not f_queue.is_empty():
		var curr_p = f_queue.pop_front()
		var owner = province_owners[curr_p]
		
		if p_neighbors.has(curr_p):
			for neighbor_p in p_neighbors[curr_p]:
				if not province_owners.has(neighbor_p):
					province_owners[neighbor_p] = owner
					f_queue.append(neighbor_p)
	
	# Finalize province ownership
	for p_id in context.provinces:
		if province_owners.has(p_id):
			context.provinces[p_id].owner = province_owners[p_id]
		else:
			context.provinces[p_id].owner = "neutral"
	
	# Assign settlements to factions
	for ss_pos in context.world_settlements:
		var pp_id = province_grid_arr[ss_pos.y][ss_pos.x]
		if pp_id != -1 and province_owners.has(pp_id):
			context.world_settlements[ss_pos].faction = province_owners[pp_id]
	
	# Name settlements and add infrastructure
	_finalize_settlements(context, province_grid_arr, rng)
	
	return true

func _calc_province_neighbors(context: WorldGenContext) -> Dictionary:
	var w = context.width
	var h = context.height
	var province_grid_arr = context.province_grid.to_legacy_grid()
	
	var p_neighbors = {}
	for y in range(h - 1):
		for x in range(w - 1):
			var p1 = province_grid_arr[y][x]
			if p1 == -1: continue
			
			var p_right = province_grid_arr[y][x + 1]
			if p_right != -1 and p_right != p1:
				if not p_neighbors.has(p1): p_neighbors[p1] = {}
				if not p_neighbors.has(p_right): p_neighbors[p_right] = {}
				p_neighbors[p1][p_right] = true
				p_neighbors[p_right][p1] = true
			
			var p_down = province_grid_arr[y + 1][x]
			if p_down != -1 and p_down != p1:
				if not p_neighbors.has(p1): p_neighbors[p1] = {}
				if not p_neighbors.has(p_down): p_neighbors[p_down] = {}
				p_neighbors[p1][p_down] = true
				p_neighbors[p_down][p1] = true
	
	return p_neighbors

func _finalize_settlements(context: WorldGenContext, province_grid_arr: Array, rng: RandomNumberGenerator) -> void:
	for s_pos in context.world_settlements:
		var s = context.world_settlements[s_pos]
		var p_id = province_grid_arr[s.pos.y][s.pos.x]
		var suffix = "Hamlet"
		if s.tier >= 5: suffix = "Metropolis"
		elif s.tier == 4: suffix = "City"
		elif s.tier == 3: suffix = "Town"
		elif s.tier == 2: suffix = "Village"
		s.name = "%s %s" % [context.provinces[p_id].name if p_id != -1 else "Wild", suffix]
		
		# Buildings
		s.buildings = {"farm": 1}
		if context.world_grid[s.pos.y][s.pos.x] == '^' or _check_terrain_near(s.pos, context.world_grid, 2, ['^']):
			s.buildings["mine"] = 1
		if _check_terrain_near(s.pos, context.world_grid, 2, ['#', '&']):
			s.buildings["lumber_mill"] = 1
		
		if s.tier >= 3:
			s.buildings["market"] = 1
			s.buildings["granary"] = 1
			s.buildings["tavern"] = 1
			s.buildings["watchtower"] = 1
			if s.tier >= 4:
				s.buildings["stone_walls"] = 1
				s.buildings["warehouse_district"] = 1
				s.buildings["housing_district"] = 1
				s.buildings["barracks"] = 1
			if s.tier >= 5:
				s.buildings["cathedral"] = 1
				s.buildings["merchant_guild"] = 1
				s.buildings["road_network"] = 1
				s.buildings["stone_walls"] = 2
		
		var cap_provided = s.buildings.get("housing_district", 0) * 100
		s.houses = max(20, int((s.population - cap_provided) / 5.0) + 5)
		s.inventory = {"wood": 1000, "stone": 250, "grain": s.population * 15, "crowns": s.tier * 2000}
		s.crown_stock = s.tier * 1000
		
		s.sync_social_classes()
		EconomyManager.recalculate_production(s, context.world_grid, context.world_resources, context.geology)
		
		s.governor = {
			"personality": GameData.GOVERNOR_PERSONALITIES[rng.randi() % GameData.GOVERNOR_PERSONALITIES.size()],
			"name": "%s %s" % [GameData.FIRST_NAMES[rng.randi() % GameData.FIRST_NAMES.size()], GameData.LAST_NAMES[rng.randi() % GameData.LAST_NAMES.size()]]
		}

func _check_terrain_near(pos: Vector2i, grid: Array, r: int, chars: Array) -> bool:
	var h = grid.size()
	var w = grid[0].size()
	for dy in range(-r, r + 1):
		for dx in range(-r, r + 1):
			var p = pos + Vector2i(dx, dy)
			if p.x >= 0 and p.x < w and p.y >= 0 and p.y < h:
				if grid[p.y][p.x] in chars: return true
	return false

func cleanup(context: WorldGenContext) -> void:
	pass
