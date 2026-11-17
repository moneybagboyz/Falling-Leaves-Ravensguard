class_name SettlementPhase
extends WorldGenPhase

## Handles settlement placement using hierarchical organic growth model
## Creates primate cities → towns → villages → hamlets

const GDSettlement = preload("res://src/data/GDSettlement.gd")

func get_phase_name() -> String:
	return "Settlements"

func execute(context: WorldGenContext) -> bool:
	var w = context.width
	var h = context.height
	var rng = context.rng
	var world_grid = context.world_grid
	
	# Identify mainland
	var mainland_set = _identify_mainland(context)
	
	# Survey habitable land
	step_completed.emit("SURVEYING FOR HABITABLE LAND...")
	var land_candidates = []
	var mainland_list = mainland_set.keys()
	var survey_total = mainland_list.size()
	
	for i in range(survey_total):
		if i % 500 == 0:
			step_completed.emit("ANALYZING GEOGRAPHY... %d%%" % int((float(i) / survey_total) * 100))
			await (Engine.get_main_loop() as SceneTree).process_frame
		
		var p = mainland_list[i]
		if world_grid[p.y][p.x] == '~':
			continue
		
		var potential = _get_site_potential(p, context)
		if potential.capacity > 40:
			potential["pos"] = p
			land_candidates.append(potential)
	
	# Place settlements hierarchically
	step_completed.emit("FOUNDING CIVILIZATIONS...")
	var all_settlement_sites = []
	land_candidates.sort_custom(func(a, b): return a.revenue > b.revenue)
	
	# Phase 1: Primate cities
	var primate_count = clamp(int(sqrt(land_candidates.size()) * 0.8), 3, 7)
	var primate_cities = []
	var min_primate_distance = max(w, h) / (primate_count * 1.2)
	
	for cand in land_candidates:
		if primate_cities.size() >= primate_count: break
		
		var too_close = false
		for existing in primate_cities:
			if cand.pos.distance_to(existing.pos) < min_primate_distance:
				too_close = true
				break
		
		if not too_close:
			primate_cities.append(cand)
			all_settlement_sites.append(cand)
	
	# Phase 2: Satellite towns
	var towns_per_capital = 3
	var min_town_distance = 15
	var max_town_distance = 35
	
	for capital in primate_cities:
		var towns_placed = 0
		for cand in land_candidates:
			if towns_placed >= towns_per_capital: break
			if all_settlement_sites.has(cand): continue
			
			var dist_to_capital = cand.pos.distance_to(capital.pos)
			if dist_to_capital < min_town_distance or dist_to_capital > max_town_distance:
				continue
			
			var too_close = false
			for existing in all_settlement_sites:
				if cand.pos.distance_to(existing.pos) < min_town_distance:
					too_close = true
					break
			
			if not too_close:
				all_settlement_sites.append(cand)
				towns_placed += 1
	
	# Phase 3: Villages
	var min_village_distance = 12
	var target_villages = int(primate_count * 4.5)
	var villages_placed = 0
	
	for cand in land_candidates:
		if villages_placed >= target_villages: break
		if all_settlement_sites.has(cand): continue
		if cand.capacity < 80: continue
		
		var too_close = false
		for existing in all_settlement_sites:
			if cand.pos.distance_to(existing.pos) < min_village_distance:
				too_close = true
				break
		
		if not too_close:
			all_settlement_sites.append(cand)
			villages_placed += 1
	
	# Phase 4: Hamlets
	var min_hamlet_distance = 8
	var target_hamlets = int(all_settlement_sites.size() * 0.6)
	var hamlets_placed = 0
	
	for cand in land_candidates:
		if hamlets_placed >= target_hamlets: break
		if all_settlement_sites.has(cand): continue
		if cand.capacity < 50: continue
		
		var near_resource = false
		for res_pos in context.world_resources.keys():
			if cand.pos.distance_to(res_pos) < 5:
				near_resource = true
				break
		
		if not near_resource and rng.randf() > 0.3: continue
		
		var too_close = false
		for existing in all_settlement_sites:
			if cand.pos.distance_to(existing.pos) < min_hamlet_distance:
				too_close = true
				break
		
		if not too_close:
			all_settlement_sites.append(cand)
			hamlets_placed += 1
	
	# Store settlement sites for next phase
	context.world_resources["_settlement_sites"] = all_settlement_sites
	context.world_resources["_mainland_set"] = mainland_set
	
	return true

func _identify_mainland(context: WorldGenContext) -> Dictionary:
	var w = context.width
	var h = context.height
	var world_grid = context.world_grid
	
	var mainland_set = {}
	var visited = {}
	var lands = []
	
	for y in range(h):
		for x in range(w):
			var pos = Vector2i(x, y)
			if world_grid[y][x] != '~' and not visited.has(pos):
				var component = []
				var q = [pos]
				visited[pos] = true
				while not q.is_empty():
					var curr = q.pop_back()
					component.append(curr)
					for dy in [-1, 0, 1]:
						for dx in [-1, 0, 1]:
							if dx == 0 and dy == 0: continue
							var next = curr + Vector2i(dx, dy)
							if next.x >= 0 and next.x < w and next.y >= 0 and next.y < h and world_grid[next.y][next.x] != '~' and not visited.has(next):
								visited[next] = true
								q.append(next)
				lands.append(component)
	
	var best_l = []
	for l in lands:
		if l.size() > best_l.size(): best_l = l
	for lp in best_l: mainland_set[lp] = true
	
	return mainland_set

func _get_site_potential(pos: Vector2i, context: WorldGenContext) -> Dictionary:
	var w = context.width
	var h = context.height
	var world_grid = context.world_grid
	var world_resources = context.world_resources
	var geology = context.geology
	
	var radius = 12
	var wood = 0
	var iron = 0
	var fertile_tiles = 0
	var water_access = 0
	
	for dy in range(-radius, radius + 1):
		for dx in range(-radius, radius + 1):
			var np = pos + Vector2i(dx, dy)
			if np.x >= 0 and np.x < w and np.y >= 0 and np.y < h:
				var t = world_grid[np.y][np.x]
				if t in ['#', '&']: wood += 1
				if world_resources.get(np, "") == "iron": iron += 1
				if t in ['.', '#']: fertile_tiles += 1
				if t in ['~', '≈', '/', '\\']: water_access += 1
	
	var capacity = int(fertile_tiles * 8.0 + wood * 2.0 + water_access * 3.0)
	var revenue = capacity + iron * 50 + wood * 5
	
	return {"capacity": capacity, "revenue": revenue}

func cleanup(context: WorldGenContext) -> void:
	pass
