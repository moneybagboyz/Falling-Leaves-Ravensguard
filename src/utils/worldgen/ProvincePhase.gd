class_name ProvincePhase
extends WorldGenPhase

## Handles watershed-based province generation and settlement finalization

const GDSettlement = preload("res://src/data/GDSettlement.gd")
const EconomyManager = preload("res://src/managers/EconomyManager.gd")
const TerrainColors = preload("res://src/ui/core/TerrainColors.gd")

func get_phase_name() -> String:
	return "Provinces"

func execute(context: WorldGenContext) -> bool:
	# Retrieve hydrology data
	var river_map = context.world_resources.get("_river_map", {})
	var all_settlement_sites = context.world_resources.get("_settlement_sites", [])
	var mainland_set = context.world_resources.get("_mainland_set", {})
	
	# Detect watersheds
	step_completed.emit("DETECTING WATERSHEDS...")
	all_settlement_sites.sort_custom(func(a, b): return a.revenue > b.revenue)
	
	var target_provinces = clamp(int(all_settlement_sites.size() * 0.40), 8, 25)
	var province_result = await _detect_watersheds(river_map, context, target_provinces)
	context.provinces = province_result.provinces
	context.province_grid = province_result.sector_grid
	
	# Map resources to provinces
	step_completed.emit("MAPPING RESOURCES TO PROVINCES...")
	for p_id in context.provinces:
		for tile in context.provinces[p_id].tiles:
			if context.world_resources.has(tile):
				context.provinces[p_id].resources.append(tile)
	
	# Finalize settlements
	step_completed.emit("FINALIZING SETTLEMENTS...")
	all_settlement_sites.sort_custom(func(a, b): return a.revenue > b.revenue)
	
	_create_settlement_objects(all_settlement_sites, mainland_set, context)
	
	# Clear cache
	TerrainColors.clear_cache()
	
	return true

func _detect_watersheds(river_map: Dictionary, context: WorldGenContext, target_count: int) -> Dictionary:
	var w = context.width
	var h = context.height
	var rng = context.rng
	var world_grid = context.world_grid
	
	# Find basin candidates
	var basin_candidates = []
	for pos in river_map:
		var flow = river_map[pos]
		var e = context.geology[pos].elevation if context.geology.has(pos) else 0.5
		if flow > 15 and e < 0.45:
			basin_candidates.append({"pos": pos, "flow": flow, "elevation": e})
	
	basin_candidates.sort_custom(func(a, b): return a.flow > b.flow)
	
	# Select province seeds
	var province_seeds = []
	var min_seed_dist = max(w, h) / (target_count * 0.8)
	
	for candidate in basin_candidates:
		if province_seeds.size() >= target_count * 0.6: break
		
		var too_close = false
		for existing in province_seeds:
			if candidate.pos.distance_to(existing.pos) < min_seed_dist:
				too_close = true
				break
		
		if not too_close:
			province_seeds.append(candidate)
	
	# Add highland seeds
	for attempt in range(target_count * 2):
		if province_seeds.size() >= target_count: break
		
		var pos = Vector2i(rng.randi_range(0, w-1), rng.randi_range(0, h-1))
		var e = context.geology[pos].elevation if context.geology.has(pos) else 0.0
		
		if e > 0.58 and world_grid[pos.y][pos.x] != '~':
			var too_close = false
			for existing in province_seeds:
				if pos.distance_to(existing.pos) < min_seed_dist * 0.7:
					too_close = true
					break
			
			if not too_close:
				province_seeds.append({"pos": pos, "flow": 0, "elevation": e})
	
	# Create provinces
	var provinces = {}
	var prefixes = ["Holy", "Great", "Old", "New", "Grand", "Lower", "Upper", "Eastern", "Western", "Northern", "Southern"]
	var roots = ["Valia", "Dorn", "Thar", "Morn", "Oros", "Kesh", "Zun", "Lith", "Aeth", "Ryver", "Sunder", "Iron", "Marsh", "Dale"]
	
	for i in range(province_seeds.size()):
		var seed = province_seeds[i]
		var p_name = "%s %s" % [prefixes[rng.randi() % prefixes.size()], roots[rng.randi() % roots.size()]]
		
		provinces[i] = {
			"id": i,
			"name": p_name,
			"center": seed.pos,
			"tiles": [],
			"resources": [],
			"dominant_tile": ".",
			"traits": [],
			"capital": null,
			"type": "watershed" if seed.flow > 0 else "highland"
		}
	
	# Expand provinces using Dijkstra
	var sector_grid = WorldGenContext.ProvinceSectorGrid.new(w, h)
	var queue = []
	var dist_map = {}
	
	for i in range(province_seeds.size()):
		var seed_pos = province_seeds[i].pos
		dist_map[seed_pos] = 0.0
		queue.append([0.0, i, seed_pos])
	
	queue.sort_custom(func(a, b): return a[0] > b[0])
	
	var total_tiles = w * h
	var processed_count = 0
	
	while not queue.is_empty():
		var curr = queue.pop_back()
		var d = curr[0]
		var p_id = curr[1]
		var pos = curr[2]
		
		if sector_grid.get_province(pos) != -1: continue
		if world_grid[pos.y][pos.x] == '~': continue
		
		sector_grid.set_province(pos, p_id)
		provinces[p_id].tiles.append(pos)
		
		processed_count += 1
		if processed_count % 500 == 0:
			var progress = int((float(processed_count) / total_tiles) * 100)
			step_completed.emit("ANALYZING GEOGRAPHY... %d%%" % progress)
			await (Engine.get_main_loop() as SceneTree).process_frame
		
		# Expand to neighbors
		for dy in [-1, 0, 1]:
			for dx in [-1, 0, 1]:
				if dx == 0 and dy == 0: continue
				var next = pos + Vector2i(dx, dy)
				
				if next.x < 0 or next.x >= w or next.y < 0 or next.y >= h: continue
				if sector_grid.get_province(next) != -1: continue
				if world_grid[next.y][next.x] == '~': continue
				
				var e_curr = context.geology[pos].elevation if context.geology.has(pos) else 0.5
				var e_next = context.geology[next].elevation if context.geology.has(next) else 0.5
				var elevation_diff = abs(e_next - e_curr)
				
				var cost = 1.0
				if e_next > 0.65: cost += 8.0
				elif e_next > 0.55: cost += 3.0
				cost += elevation_diff * 10.0
				
				var flow_next = river_map.get(next, 0)
				if flow_next > 20: cost += 2.0
				elif flow_next > 10: cost += 1.0
				
				var new_d = d + cost
				if not dist_map.has(next) or new_d < dist_map[next]:
					dist_map[next] = new_d
					queue.append([new_d, p_id, next])
	
	return {"provinces": provinces, "sector_grid": sector_grid}

func _create_settlement_objects(all_settlement_sites: Array, mainland_set: Dictionary, context: WorldGenContext) -> void:
	var w = context.width
	var h = context.height
	var rng = context.rng
	var world_grid = context.world_grid
	
	all_settlement_sites.sort_custom(func(a, b): return a.revenue > b.revenue)
	
	for i in range(all_settlement_sites.size()):
		var site = all_settlement_sites[i]
		var s = GDSettlement.new(site.pos)
		
		var quality_rank = float(i) / all_settlement_sites.size()
		
		if quality_rank < 0.05:
			s.type = "metropolis"; s.tier = 5; s.radius = 70
			s.footprint = Rect2i(s.pos - Vector2i(2, 2), Vector2i(5, 5))
			s.population = clamp(int(site.capacity * 0.8), 8000, 25000)
		elif quality_rank < 0.15:
			s.type = "city"; s.tier = 4; s.radius = 40
			s.footprint = Rect2i(s.pos - Vector2i(1, 1), Vector2i(3, 3))
			s.population = clamp(int(site.capacity * 0.6), 3000, 8000)
		elif quality_rank < 0.35:
			s.type = "town"; s.tier = 3; s.radius = 25
			s.footprint = Rect2i(s.pos, Vector2i(2, 2))
			s.population = clamp(int(site.capacity * 0.4), 800, 3000)
		elif quality_rank < 0.65:
			s.type = "village"; s.tier = 2; s.radius = 12
			s.footprint = Rect2i(s.pos, Vector2i(1, 1))
			s.population = clamp(int(site.capacity * 0.2), 200, 800)
		else:
			s.type = "hamlet"; s.tier = 1; s.radius = 6
			s.footprint = Rect2i(s.pos, Vector2i(1, 1))
			s.population = clamp(int(site.capacity * 0.1), 50, 200)
		
		s.max_slots = s.tier * 6
		
		# Reserve footprint
		for fy in range(s.footprint.position.y, s.footprint.end.y):
			for fx in range(s.footprint.position.x, s.footprint.end.x):
				var fpos = Vector2i(fx, fy)
				if fx >= 0 and fx < w and fy >= 0 and fy < h:
					context.world_settlements[fpos] = s
					
					if fpos == s.pos:
						if s.type == "metropolis": world_grid[fy][fx] = 'M'
						elif s.type == "city": world_grid[fy][fx] = 'C'
						elif s.type == "town": world_grid[fy][fx] = 'T'
						elif s.type == "village": world_grid[fy][fx] = 'V'
						elif s.type == "hamlet": world_grid[fy][fx] = 'h'
					else:
						if world_grid[fy][fx] == '.':
							world_grid[fy][fx] = 'c'
	
	# Satellite hamlets
	var settlements_list = context.world_settlements.values().filter(func(s): return s is GDSettlement)
	for s in settlements_list:
		if s.tier >= 3:
			for j in range(rng.randi_range(2, 4)):
				var spawn_pos = s.pos + Vector2i(rng.randi_range(-8, 8), rng.randi_range(-8, 8))
				if spawn_pos.x >= 0 and spawn_pos.x < w and spawn_pos.y >= 0 and spawn_pos.y < h and mainland_set.has(spawn_pos) and world_grid[spawn_pos.y][spawn_pos.x] != '~' and not context.world_settlements.has(spawn_pos):
					var daughter = GDSettlement.new(spawn_pos)
					daughter.population = rng.randi_range(50, 120)
					daughter.tier = 1
					daughter.radius = 6
					daughter.max_slots = 6
					daughter.type = "hamlet"
					daughter.parent_city = s.pos
					context.world_settlements[spawn_pos] = daughter
					world_grid[spawn_pos.y][spawn_pos.x] = 'h'

func cleanup(context: WorldGenContext) -> void:
	context.world_resources.erase("_settlement_sites")
	context.world_resources.erase("_mainland_set")
