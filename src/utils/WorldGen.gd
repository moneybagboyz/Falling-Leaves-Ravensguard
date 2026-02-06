class_name WorldGen
extends RefCounted

signal step_completed(stage_name)

func generate(w: int, h: int, rng: RandomNumberGenerator, live_grid: Array = [], config: Dictionary = {}) -> Dictionary:
	# Clear rendering caches to prevent ghosting or stripe artifacts from previous runs
	UIPanels.terrain_color_cache.clear()
	UIPanels.color_hex_cache.clear()
	
	var world_grid = live_grid
	world_grid.clear()
	for y in range(h):
		var row = []
		row.resize(w)
		row.fill('~')
		world_grid.append(row)
	
	# Extract parameters from config if available
	var num_plates = config.get("num_plates", 12)
	var num_factions = config.get("num_factions", 5)
	var savagery = config.get("savagery", 5)
	var moisture_bias = config.get("moisture", 1.0)
	var temp_bias = config.get("temperature", 1.0)
	var layout = config.get("layout", "Pangea")
	var mineral_density = config.get("mineral_density", 5)
	
	var world_resources = {}
	var world_settlements = {}
	var geology = {} # Vector2i -> {temp, rain, layers}

	# ---------------------------------------------------------
	# 1. Physical Generation (Tectonic Plate Simulation - PANGEA BIAS)
	# ---------------------------------------------------------
	var _plate_activity = 1.8 # Higher energy for more distinct ranges
	var noise_influence = 0.22 
	
	var plates = []
	var world_center = Vector2(w / 2.0, h / 2.0)
	
	# Continent Centers for "Continents" layout
	var cont_centers = [
		Vector2(w * 0.25, h * 0.5),
		Vector2(w * 0.75, h * 0.5)
	]
	
	for i in range(num_plates):
		var seed_pos = Vector2(rng.randf_range(0, w), rng.randf_range(0, h))
		var is_oceanic = true
		
		match layout:
			"Pangea":
				var dist_to_center = seed_pos.distance_to(world_center)
				var normalized_dist = dist_to_center / (min(w, h) * 0.5)
				is_oceanic = normalized_dist > 0.65
				if normalized_dist < 0.25: is_oceanic = false
			"Continents":
				# Land if near one of the continent centers
				is_oceanic = true
				for c in cont_centers:
					if seed_pos.distance_to(c) < min(w, h) * 0.35:
						is_oceanic = false
						break
			"Archipelago":
				# Randomly assign 20% of plates as land, rest ocean
				is_oceanic = rng.randf() > 0.2
		
		var velocity = Vector2.ZERO
		if is_oceanic:
			velocity = Vector2(rng.randf_range(-1.5, 1.5), rng.randf_range(-1.5, 1.5)).normalized() * 2.0
		else:
			# Continental plates move towards centers slightly to encourage collision
			var target = world_center
			if layout == "Continents":
				target = cont_centers[0] if seed_pos.x < w*0.5 else cont_centers[1]
			velocity = (target - seed_pos).normalized() * rng.randf_range(0.3, 1.2)
		
		plates.append({
			"id": i,
			"seed": seed_pos,
			"velocity": velocity,
			"is_oceanic": is_oceanic
		})
	
	var noise_detail = FastNoiseLite.new()
	noise_detail.seed = rng.randi()
	noise_detail.frequency = 0.05
	
	var noise_temp = FastNoiseLite.new()
	noise_temp.seed = rng.randi()
	noise_temp.frequency = 0.01
	
	var noise_drainage = FastNoiseLite.new()
	noise_drainage.seed = rng.randi()
	noise_drainage.frequency = 0.04
	
	var noise_strata = FastNoiseLite.new()
	noise_strata.seed = rng.randi()
	noise_strata.frequency = 0.008
	
	var elevation_map = []
	var temp_map = []
	var moisture_map = []
	var drainage_map = []
	var strata_map = [] # Store deep rock layers independently of surface
	
	var max_dist = world_center.length()
	
	# INITIAL VIZ: Plate Seeds
	step_completed.emit("SIMULATING TECTONICS...")
	for p in plates:
		var px = int(p.seed.x)
		var py = int(p.seed.y)
		if is_in_bounds(px, py, w, h):
			world_grid[py][px] = 'P'
	await (Engine.get_main_loop() as SceneTree).process_frame
	await (Engine.get_main_loop() as SceneTree).process_frame # Extra pause to see it
	
	step_completed.emit("RAISING MOUNTAINS...") # Changed from Atmosphere to align with elevation logic
	var tiles_processed = 0
	var total_tiles = w * h
	var update_interval = 3000  # Update UI every 3000 tiles for smoother progress
	
	for y in range(h):
		elevation_map.append([])
		temp_map.append([])
		moisture_map.append([])
		drainage_map.append([])
		strata_map.append([])
		for x in range(w):
			tiles_processed += 1
			
			# Batched await: Update UI periodically based on tiles processed
			if tiles_processed % update_interval == 0:
				step_completed.emit("RAISING MOUNTAINS [%d%%]" % [int((float(tiles_processed)/total_tiles)*100)])
				await (Engine.get_main_loop() as SceneTree).process_frame
			
			var pos_v = Vector2(x, y)
			
			# 1. Find two nearest plates
			var d1 = 999999.0
			var d2 = 999999.0
			var p1 = null
			var p2 = null
			
			for p in plates:
				var d = pos_v.distance_to(p.seed)
				if d < d1:
					d2 = d1
					p2 = p1
					d1 = d
					p1 = p
				elif d < d2:
					d2 = d
					p2 = p
			
			# 2. Base Elevation
			var base_e = 0.12 if p1.is_oceanic else 0.40 # Much lower continental base for plains
			var e = base_e
			
			# 3. Boundary Interaction
			var boundary_dist = d2 - d1
			# Jitter the influence range to allow for natural passes and valleys
			var jitter = noise_detail.get_noise_2d(x, y) * 8.0
			var influence_range = 14.0 + jitter
			
			if boundary_dist < influence_range:
				var weight = pow(1.0 - (boundary_dist / influence_range), 1.5)
				var normal = (p2.seed - p1.seed).normalized()
				var dot = p1.velocity.dot(normal) - p2.velocity.dot(normal) 
				
				if dot > 0: # Convergent
					# Break up the perfectly straight plate boundaries with noise
					var variation = 0.8 + (noise_detail.get_noise_2d(y, x) * 0.4)
					if not p1.is_oceanic and not p2.is_oceanic:
						e += dot * 0.38 * weight * variation # Moderate Himalayan growth
					elif p1.is_oceanic and not p2.is_oceanic:
						e += dot * 0.32 * weight * variation # Andean subduction
					elif not p1.is_oceanic and p2.is_oceanic:
						e += dot * 0.32 * weight * variation
					else:
						e += dot * 0.20 * weight * variation # Volcanic Arcs
				else: # Divergent
					e += dot * 0.3 * weight # Deeper rifts
			
			# 4. Detail & Mask
			e += noise_detail.get_noise_2d(x, y) * noise_influence
			
			# Layout Masking
			var dist_to_center = pos_v.distance_to(world_center)
			var mask = 1.0
			
			match layout:
				"Pangea":
					var normalized_dist = dist_to_center / (min(w, h) * 0.58)
					mask = clamp(1.4 - normalized_dist, 0.0, 1.0)
				"Continents":
					var m1 = clamp(1.2 - (pos_v.distance_to(cont_centers[0]) / (min(w, h) * 0.4)), 0.0, 1.0)
					var m2 = clamp(1.2 - (pos_v.distance_to(cont_centers[1]) / (min(w, h) * 0.4)), 0.0, 1.0)
					mask = max(m1, m2)
				"Archipelago":
					# Use noise to create scattered islands
					var island_noise = FastNoiseLite.new()
					island_noise.seed = rng.seed + 99
					island_noise.frequency = 0.08
					var n = island_noise.get_noise_2d(x, y)
					mask = clamp(n + 0.4, 0.0, 1.0)
			
			e *= mask
			
			var final_e = clamp(e, 0.0, 1.0)
			elevation_map[y].append(final_e)
			
			# Visualization Thresholds
			if final_e < 0.32: world_grid[y][x] = '~' # Ocean
			elif final_e < 0.48: world_grid[y][x] = '.' # Lowland/Plains
			elif final_e < 0.60: world_grid[y][x] = 'o' # Foothills/Hills
			elif final_e < 0.75: world_grid[y][x] = 'O' # High Mountains
			else: world_grid[y][x] = '^' # Ancient Peaks / Volcanos
			
			var lat_factor = (float(y) / h) * 2.0 - 1.0
			var t = noise_temp.get_noise_2d(x, y)
			# Apply temp_bias to the base temperature logic
			t = (t + 0.15) * temp_bias - (e * 0.2) - (abs(lat_factor) * 0.45) 
			temp_map[y].append(t)
			
			moisture_map[y].append(0.0)
			
			# DWARF FORTRESS: Noise-based drainage (soil permeability)
			var drain = (noise_drainage.get_noise_2d(x, y) + 1.0) / 2.0
			drainage_map[y].append(drain) 

			# DWARF FORTRESS: Global Strata (Geology determined by noise, not biome)
			var s_val = noise_strata.get_noise_2d(x, y)
			var layers = []
			if s_val < -0.3: layers = ["igneous", "metamorphic", "sedimentary"]
			elif s_val < 0.3: layers = ["metamorphic", "sedimentary", "soil"]
			else: layers = ["sedimentary", "sedimentary", "soil"]
			strata_map[y].append(layers)


	# ---------------------------------------------------------
	# 1.1. Moisture Simulation (Wind: West to East)
	# ---------------------------------------------------------
	step_completed.emit("SIMULATING ATMOSPHERE...")
	for y in range(h):
		if y % 20 == 0:
			step_completed.emit("SIMULATING ATMOSPHERE [%d%%]" % [int((float(y)/h)*100)])
			await (Engine.get_main_loop() as SceneTree).process_frame
		var wind_moisture = 0.0
		for x in range(w):
			var e = elevation_map[y][x]
			if e < 0.35: # Water
				wind_moisture += 0.25 * moisture_bias # Pickup biased by config
				wind_moisture = clamp(wind_moisture, 0.0, 5.0)
			else: # Land
				# Mountain Rain Shadow Logic
				if e > 0.58: 
					var dump = wind_moisture * 0.5
					moisture_map[y][x] += dump
					wind_moisture -= dump
				
				moisture_map[y][x] += wind_moisture * 0.12 # Faster uptake
				wind_moisture *= 0.982 # Much slower decay for realism in interior
			
			wind_moisture = clamp(wind_moisture, 0.0, 5.0)

	# ---------------------------------------------------------
	# 1.2. Flow-Based Hydrology (DF Style: Flow Accumulation)
	# ---------------------------------------------------------
	step_completed.emit("CALCULATING HYDROLGY (DF STYLE)...")
	var flow_map = {} # Vector2i -> cumulative_flow
	var lake_map = {}
	
	# Pass 1: Simple Flow Accumulation 
	# (Sort tiles by elevation and flow from top to bottom)
	var sort_tiles = []
	for y in range(h):
		for x in range(w):
			if elevation_map[y][x] > 0.32:
				sort_tiles.append(Vector2i(x, y))
	
	sort_tiles.sort_custom(func(a, b): return elevation_map[a.y][a.x] > elevation_map[b.y][b.x])
	
	var processed = 0
	var total_land = sort_tiles.size()
	
	for pos in sort_tiles:
		processed += 1
		if processed % 5000 == 0:
			step_completed.emit("TRACING WATERWAYS [%d%%]" % [int((float(processed)/total_land)*100)])
			await (Engine.get_main_loop() as SceneTree).process_frame
		
		# Base inflow from moisture/rain
		var inflow = 1.0 + moisture_map[pos.y][pos.x]
		flow_map[pos] = flow_map.get(pos, 0.0) + inflow
		
		# Find lowest neighbor to flow into
		var lowest_val = elevation_map[pos.y][pos.x]
		var target = null
		
		for dy in [-1, 0, 1]:
			for dx in [-1, 0, 1]:
				if dx == 0 and dy == 0: continue
				var n = pos + Vector2i(dx, dy)
				if is_in_bounds(n.x, n.y, w, h):
					var e_val = elevation_map[n.y][n.x]
					if e_val < lowest_val:
						lowest_val = e_val
						target = n
		
		if target != null:
			flow_map[target] = flow_map.get(target, 0.0) + flow_map[pos]
		elif elevation_map[pos.y][pos.x] > 0.32:
			# If no lower neighbor, it's a sink/lake
			if flow_map[pos] > 10.0:
				lake_map[pos] = true
				
				# Lake Overflow: Allow water to spill to the lowest pass
				var spill_target = _find_spillover_target(pos, elevation_map, w, h)
				if spill_target != null:
					flow_map[spill_target] = flow_map.get(spill_target, 0.0) + flow_map[pos]

	var river_map = flow_map # Renaming to maintain compatibility with later steps


	# ---------------------------------------------------------
	# 1.3. Final Tile Assignment (Biome Matrix)
	# ---------------------------------------------------------
	step_completed.emit("PAINTING BIOMES...")
	for y in range(h):
		if y % 20 == 0:
			step_completed.emit("PAINTING BIOMES [%d%%]" % [int((float(y)/h)*100)])
			await (Engine.get_main_loop() as SceneTree).process_frame
		for x in range(w):
			var e = elevation_map[y][x]
			var t = temp_map[y][x]
			var m = moisture_map[y][x]
			var d = drainage_map[y][x]
			var pos = Vector2i(x,y)
			
			# River influence on local moisture
			var flow = river_map.get(pos, 0.0)
			if flow > 200.0: m += 0.2 # DF Style: Larger rivers have more impact
			
			var tile = '.'
			if e < 0.32: 
				tile = '~' # Ocean
			elif lake_map.has(pos) and flow > 100.0:
				tile = '≈' # Inland Lake
			elif flow > 1500.0:
				tile = '≈' # Major River (DF: huge flow accumulation)
			elif flow > 400.0:
				tile = '/' if (x + y) % 2 == 0 else '\\' # River
			elif e > 0.60: 
				tile = '^' # Mountains
			else:
				# Biome matrix using Whitaker + Drainage (DF logic)
				if t < -0.6: # Extreme Cold
					tile = 'X' # Glaciers / Ice Caps
				elif t < -0.35: # Cold
					if d < 0.2: tile = '*' # Tundra
					else: tile = '#' # Taiga
				elif t < 0.15: # Temperate
					if m < 0.1: tile = '"' # Badlands
					elif d < 0.3: tile = '.' # Grassland
					else: tile = '#' # Forest
				elif t < 0.4: # Warm
					if m < 0.12: tile = '"' # Desert
					elif d < 0.4: tile = '.' # Savanna
					else: tile = '&' # Tropical Forest
				else: # Hot
					if m < 0.3: tile = '"' # Scorching Desert
					else: tile = '&' # Jungle
				
				# Hills override
				if e > 0.48 and tile not in ['&', '#', '"', '~']:
					tile = 'o'
			
			world_grid[y][x] = tile
			
			# DWARF FORTRESS: Use the pre-computed global strata for geology
			var layers = strata_map[y][x]
			geology[pos] = {"temp": t, "rain": m, "layers": layers, "biome": tile, "elevation": e, "drainage": d}
			
			# Resource Roll (Surface & Subsurface)
			var roll = rng.randf()
			
			# 1. Surface Resources (Biome Dependent)
			if tile == '#' and roll < 0.06: world_resources[pos] = "wood"
			elif tile == '#' and roll < 0.08: world_resources[pos] = "game"
			elif tile == '.' and roll < 0.03: world_resources[pos] = "horses"
			elif tile == '&' and roll < 0.10: world_resources[pos] = "peat"
			elif tile == '"' and roll < 0.05: world_resources[pos] = "salt"
			elif tile == '*' and roll < 0.07: world_resources[pos] = "furs"
			
			# Luxury & Specialty (Climate/Biome Combos)
			if t > 0.6 and m > 0.5 and roll < 0.04: world_resources[pos] = "spices"
			elif t > 0.4 and m < 0.4 and tile == '.' and roll < 0.03: world_resources[pos] = "ivory"
			elif tile == '&' and roll < 0.12: world_resources[pos] = "clay" # High clay in swamps
			
			# 2. Subsurface Minerals (Geology Dependent)
			var roll_sub = rng.randf()
			var density_mod = mineral_density / 5.0 # 1.0 at density 5, 2.0 at density 10
			
			# Sort layers so that igneous/metamorphic take precedence for rare minerals
			var sorted_layers = layers.duplicate()
			sorted_layers.sort_custom(func(a, b): 
				var score = {"igneous": 2, "metamorphic": 1, "sedimentary": 0}
				return score.get(a, -1) > score.get(b, -1)
			)
			
			for layer in sorted_layers:
				if GameData.GEOLOGY_RESOURCES.has(layer):
					var layer_res = GameData.GEOLOGY_RESOURCES[layer]
					var found_sub = false
					# Keys are sorted to ensure consistent roll thresholds
					var res_keys = layer_res.keys()
					res_keys.sort() 
					for res in res_keys:
						if roll_sub < (layer_res[res] * density_mod):
							world_resources[pos] = res
							found_sub = true
							break
					if found_sub: break
			
			# Rivers/Silt bonus
			if tile == '~' and flow > 1.0 and roll < 0.15:
				world_resources[pos] = "clay"

	# Clear rendering cache after main biome painting is done
	UIPanels.terrain_color_cache.clear()
	UIPanels.color_hex_cache.clear()

	# ---------------------------------------------------------
	# 1.4. Mainland Identification
	# ---------------------------------------------------------
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
					var curr = q.pop_back() # Use pop_back for O(1) efficiency (DFS)
					component.append(curr)
					for dy in [-1, 0, 1]:
						for dx in [-1, 0, 1]:
							if dx == 0 and dy == 0: continue
							var next = curr + Vector2i(dx, dy)
							if is_in_bounds(next.x, next.y, w, h) and world_grid[next.y][next.x] != '~' and not visited.has(next):
								visited[next] = true
								q.append(next)
				lands.append(component)
	var best_l = []
	for l in lands:
		if l.size() > best_l.size(): best_l = l
	for lp in best_l: mainland_set[lp] = true

	# ---------------------------------------------------------
	# 2. People First: Global Settlement Survey
	# ---------------------------------------------------------
	step_completed.emit("SURVEYING FOR HABITABLE LAND...")
	var land_candidates = []
	var mainland_list = mainland_set.keys()
	var survey_total = mainland_list.size()
	
	for i in range(survey_total):
		if i % 2000 == 0:
			step_completed.emit("ANALYIZING GEOGRAPHY... %d%%" % int((float(i)/survey_total)*100))
			await (Engine.get_main_loop() as SceneTree).process_frame
		
		var p = mainland_list[i]
		var potential = _get_site_potential(p, w, h, world_grid, world_resources, geology)
		if potential.capacity > 40:
			potential["pos"] = p
			land_candidates.append(potential)

	# Sector-based selection to ensure even distribution
	var all_settlement_sites = []
	var sector_size = 40 
	var sectors = {} # Vector2i -> Array of candidates
	
	for cand in land_candidates:
		var s_pos = Vector2i(cand.pos.x / sector_size, cand.pos.y / sector_size)
		if not sectors.has(s_pos): sectors[s_pos] = []
		sectors[s_pos].append(cand)
		
	for s_pos in sectors:
		var sector_cands = sectors[s_pos]
		# Sort by quality and take top 2 per sector
		sector_cands.sort_custom(func(a, b): return a.revenue > b.revenue)
		for j in range(min(2, sector_cands.size())):
			all_settlement_sites.append(sector_cands[j])

	# ---------------------------------------------------------
	# 2.5. Political Emergence: Hub Promotion & Provinces
	# ---------------------------------------------------------
	var province_prefixes = ["Holy", "Great", "Old", "New", "Grand", "Lower", "Upper", "Eastern", "Western", "Northern", "Southern"]
	var province_roots = ["Valia", "Dorn", "Thar", "Morn", "Oros", "Kesh", "Zun", "Lith", "Aeth", "Ryver", "Sunder", "Iron"]
	
	step_completed.emit("PROMOTING REGIONAL HUBS...")
	
	# Rank every settlement in the world by its economic importance
	all_settlement_sites.sort_custom(func(a, b): return a.revenue > b.revenue)
	
	var hubs = []
	var province_grid = []
	var provinces = {}
	for y in range(h):
		province_grid.append([])
		for x in range(w): province_grid[y].append(-1)
	
	# Top 40% of settlements become provincial Hubs, others become Spokes
	var hub_count = int(all_settlement_sites.size() * 0.45)
	for i in range(hub_count):
		var hub_data = all_settlement_sites[i]
		hubs.append(hub_data)
		var p_name = "%s %s" % [province_prefixes[rng.randi() % province_prefixes.size()], province_roots[rng.randi() % province_roots.size()]]
		provinces[i] = {"id": i, "name": p_name, "center": hub_data.pos, "tiles": [], "resources": [], "dominant_tile": ".", "traits": [], "capital": null}

	# Province Expansion (Dijkstra)
	var queue = []
	var dist_map = {}
	for i in range(hubs.size()):
		var h_pos = hubs[i].pos
		dist_map[h_pos] = 0.0
		queue.append([0.0, i, h_pos])
		
	queue.sort_custom(func(a, b): return a[0] > b[0])
	var tiles_finished = 0
	var loop_time_limit = Time.get_ticks_msec() + 15
	var MAX_REACH = 50.0 # Economic transport limit
	
	while not queue.is_empty():
		if Time.get_ticks_msec() > loop_time_limit:
			queue.sort_custom(func(a, b): return a[0] > b[0])
			step_completed.emit("EXPANDING CATCHMENTS... [%d%%]" % int((float(tiles_finished)/total_land)*100))
			await (Engine.get_main_loop() as SceneTree).process_frame
			loop_time_limit = Time.get_ticks_msec() + 15
			
		var curr = queue.pop_back()
		var d = curr[0]
		var p_id = curr[1]
		var pos = curr[2]
		
		if province_grid[pos.y][pos.x] != -1: continue
		if d > MAX_REACH: continue
		
		province_grid[pos.y][pos.x] = p_id
		tiles_finished += 1
		provinces[p_id].tiles.append(pos)
		if world_resources.has(pos): provinces[p_id].resources.append(pos)
		
		for dy in [-1, 0, 1]:
			for dx in [-1, 0, 1]:
				if dx == 0 and dy == 0: continue
				var next = pos + Vector2i(dx, dy)
				if is_in_bounds(next.x, next.y, w, h) and province_grid[next.y][next.x] == -1:
					var terrain = world_grid[next.y][next.x]
					if terrain == '~': continue
					var cost = 1.0 + (elevation_map[next.y][next.x] * 5.0)
					var new_d = d + cost
					if not dist_map.has(next) or new_d < dist_map[next]:
						dist_map[next] = new_d
						queue.append([new_d, p_id, next])

	# ---------------------------------------------------------
	# 3. Finalization: Anchoring unique Settlement Objects
	# ---------------------------------------------------------
	step_completed.emit("FINALIZING SETTLEMENTS...")
	var unique_settlements = []
	for i in range(all_settlement_sites.size()):
		var site = all_settlement_sites[i]
		var s = GDSettlement.new(site.pos)
		unique_settlements.append(s)
		
		# Determine Tier based on Hub status
		var is_hub = i < hub_count
		if is_hub:
			var hub_rank = float(i) / hub_count
			if hub_rank < 0.1: 
				s.type = "metropolis"; s.tier = 5; s.radius = 800
				s.footprint = Rect2i(s.pos - Vector2i(2, 2), Vector2i(5, 5))
				s.population = clamp(int(site.capacity * 0.95), 10000, 100000)
			elif hub_rank < 0.4: 
				s.type = "city"; s.tier = 4; s.radius = 450
				s.footprint = Rect2i(s.pos - Vector2i(1, 1), Vector2i(3, 3))
				s.population = clamp(int(site.capacity * 0.70), 2000, 10000)
			else: 
				s.type = "town"; s.tier = 3; s.radius = 230
				s.footprint = Rect2i(s.pos, Vector2i(2, 2))
				s.population = clamp(int(site.capacity * 0.35), 500, 2000)
		else:
			var spoke_rank = float(i - hub_count) / (all_settlement_sites.size() - hub_count)
			if spoke_rank < 0.4: 
				s.type = "village"; s.tier = 2; s.radius = 90
				s.footprint = Rect2i(s.pos, Vector2i(1, 1))
				s.population = clamp(int(site.capacity * 0.15), 200, 500)
			else: 
				s.type = "hamlet"; s.tier = 1; s.radius = 60
				s.footprint = Rect2i(s.pos, Vector2i(1, 1))
				s.population = clamp(int(site.capacity * 0.05), 30, 200)

		s.max_slots = s.tier * 6
		
		# Reserve all tiles in footprint so no other settlement spawns there
		for fy in range(s.footprint.position.y, s.footprint.end.y):
			for fx in range(s.footprint.position.x, s.footprint.end.x):
				var fpos = Vector2i(fx, fy)
				if is_in_bounds(fx, fy, w, h):
					world_settlements[fpos] = s # All tiles point to the same owner
					
					# Draw symbols on the map for the footprint
					if fpos == s.pos:
						# Main site
						if s.type == "metropolis": world_grid[fy][fx] = 'M'
						elif s.type == "city": world_grid[fy][fx] = 'C'
						elif s.type == "town": world_grid[fy][fx] = 'T'
						elif s.type == "village": world_grid[fy][fx] = 'V'
						elif s.type == "hamlet": world_grid[fy][fx] = 'h'
					else:
						# Satellite tiles of the city
						if world_grid[fy][fx] == '.': # Only if plain
							world_grid[fy][fx] = 'c' # Urban sprawl symbol


	# Phase 4: Satellite Hamlets
	var hamlets = []
	for s in unique_settlements:
		if s.tier >= 3:
			# Large cities spawn 2-4 satellites
			for j in range(rng.randi_range(2, 4)):
				var spawn_pos = s.pos + Vector2i(rng.randi_range(-8, 8), rng.randi_range(-8, 8))
				if is_in_bounds(spawn_pos.x, spawn_pos.y, w, h) and mainland_set.has(spawn_pos) and world_grid[spawn_pos.y][spawn_pos.x] != '~' and not world_settlements.has(spawn_pos):
					var daughter = GDSettlement.new(spawn_pos)
					daughter.population = rng.randi_range(30, 60)
					daughter.tier = 1
					daughter.radius = 1
					daughter.max_slots = 6
					daughter.type = "hamlet"
					daughter.parent_city = s.pos
					hamlets.append(daughter)
	
	for hamlet_obj in hamlets:
		world_settlements[hamlet_obj.pos] = hamlet_obj
		world_grid[hamlet_obj.pos.y][hamlet_obj.pos.x] = 'h'

	# Clear cache again now that cities 'M/C/T' and hamlets 'h' are stamped
	UIPanels.terrain_color_cache.clear()

	# ---------------------------------------------------------
	# 2.5 Territorial Assignment (Politicized Expansion)
	# ---------------------------------------------------------
	step_completed.emit("ESTABLISHING FACTIONS...")
	
	# 1. Pre-calculate Province Adjacency for clean borders
	var p_neighbors = {} # p_id -> Set of p_ids
	for y in range(h - 1):
		for x in range(w - 1):
			var p1 = province_grid[y][x]
			if p1 == -1: continue
			
			var p_right = province_grid[y][x+1]
			if p_right != -1 and p_right != p1:
				if not p_neighbors.has(p1): p_neighbors[p1] = {}
				if not p_neighbors.has(p_right): p_neighbors[p_right] = {}
				p_neighbors[p1][p_right] = true
				p_neighbors[p_right][p1] = true
				
			var p_down = province_grid[y+1][x]
			if p_down != -1 and p_down != p1:
				if not p_neighbors.has(p1): p_neighbors[p1] = {}
				if not p_neighbors.has(p_down): p_neighbors[p_down] = {}
				p_neighbors[p1][p_down] = true
				p_neighbors[p_down][p1] = true

	var f_prefixes = ["Kingdom of", "The", "Empire of", "Principality of", "Holy", "Grand", "United"]
	var f_roots = ["Valia", "Dorn", "Oros", "Kesh", "Zun", "Lith", "Aeth", "Ryver", "Esk", "Beln", "Gorth", "Mord"]
	var f_suffixes = ["Kingdom", "Empire", "Lands", "Dominion", "States", "Alliance", "Hegemony"]
	var f_colors = ["coral", "chartreuse", "blueviolet", "dark_orange", "deep_pink", "deep_sky_blue", "gold", "lawn_green", "light_pink", "medium_spring_green", "orchid", "spring_green", "tomato", "turquoise", "yellow"]
	
	var faction_list: Array[GDFaction] = []
	for i in range(num_factions):
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

	var province_owners = {} # province_id -> faction_id
	
	# 2. Select Distant Capitals (Avoid clustering in the same fertile delta)
	var cap_pool = []
	for s_pos in world_settlements:
		var s = world_settlements[s_pos]
		cap_pool.append({"pos": s_pos, "pop": s.population})
	cap_pool.sort_custom(func(a, b): return a.pop > b.pop)
	
	var capitals = []
	var f_idx = 0
	
	# Minimum distance between capital cities (e.g., 20% of world size)
	var min_cap_dist = min(w, h) * 0.2
	
	for cand in cap_pool:
		if f_idx >= faction_list.size(): break
		
		# Minimum distance check
		var too_close = false
		for c in capitals:
			if cand.pos.distance_to(c) < min_cap_dist:
				too_close = true
				break
		if too_close: continue
		
		var f_obj = faction_list[f_idx]
		var f_id = f_obj.id
		var s_pos = cand.pos
		var s = world_settlements[s_pos]
		s.faction = f_id
		s.tier = 5 # Master Capital Rank
		s.is_capital = true
		s.type = "capital" # Match UIPanels expectation
		s.radius = 5
		capitals.append(s_pos)
		
		var p_id = province_grid[s_pos.y][s_pos.x]
		if p_id != -1:
			province_owners[p_id] = f_id
		f_idx += 1

	# 3. Flood-Fill Expansion (Logical contiguity)
	var f_queue = []
	for p_id in province_owners:
		f_queue.append(p_id)
		
	# Expand until all connected provinces on the mainland are claimed
	while not f_queue.is_empty():
		var curr_p = f_queue.pop_front()
		var owner = province_owners[curr_p]
		
		if p_neighbors.has(curr_p):
			for neighbor_p in p_neighbors[curr_p]:
				if not province_owners.has(neighbor_p):
					province_owners[neighbor_p] = owner
					f_queue.append(neighbor_p)

	# Finalize: Update all provinces and settlements to match their owner
	for p_id in provinces:
		if province_owners.has(p_id):
			provinces[p_id].owner = province_owners[p_id]
		else:
			provinces[p_id].owner = "neutral"

	for ss_pos in world_settlements:
		var pp_id = province_grid[ss_pos.y][ss_pos.x]
		if pp_id != -1 and province_owners.has(pp_id):
			world_settlements[ss_pos].faction = province_owners[pp_id]

	# Phase 5: Initial naming and naming
	for s_pos in world_settlements:
		var s = world_settlements[s_pos]
		# Assign Name based on Province
		var p_id = province_grid[s.pos.y][s.pos.x]
		var suffix = "Hamlet"
		if s.tier >= 5: suffix = "Metropolis"
		elif s.tier == 4: suffix = "City"
		elif s.tier == 3: suffix = "Town"
		elif s.tier == 2: suffix = "Village"
		s.name = "%s %s" % [provinces[p_id].name if p_id != -1 else "Wild", suffix]
		
		# Final Industry & Legacy Infrastructure
		s.buildings = {"farm": 1}
		if world_grid[s.pos.y][s.pos.x] == '^' or _check_terrain_near(s.pos, world_grid, 2, ['^']): 
			s.buildings["mine"] = 1
		if _check_terrain_near(s.pos, world_grid, 2, ['#', '&']):
			s.buildings["lumber_mill"] = 1
		
		# Tier-based Legacy Infrastructure
		if s.tier >= 3: # Town and above
			s.buildings["market"] = 1
			s.buildings["granary"] = 1
			s.buildings["tavern"] = 1
			s.buildings["watchtower"] = 1
			if s.tier >= 4: # City
				s.buildings["stone_walls"] = 1
				s.buildings["warehouse_district"] = 1
				s.buildings["housing_district"] = 1
				s.buildings["barracks"] = 1
			if s.tier >= 5: # Metropolis
				s.buildings["cathedral"] = 1
				s.buildings["merchant_guild"] = 1
				s.buildings["road_network"] = 1
				s.buildings["stone_walls"] = 2
		
		# FORCE FRESH POPULATION SYNC: This fixes the bug where we saw 800 people 
		# but only 96 laborers/24 houses (which suggests it used a pop of ~100).
		var cap_provided = s.buildings.get("housing_district", 0) * 100
		s.houses = max(20, int((s.population - cap_provided) / 5.0) + 5)
		s.inventory = {"wood": 1000, "stone": 250, "grain": s.population * 15, "crowns": s.tier * 2000}
		s.crown_stock = s.tier * 1000
		
		# SCALE SOCIAL CLASSES: Ensure workforce matches population
		s.sync_social_classes()
		
		# MAP THE LAND: Crucial to fill land-based slots (farms, mines, forests)
		EconomyManager.recalculate_production(s, world_grid, world_resources, geology)
		
		s.governor = {
			"personality": GameData.GOVERNOR_PERSONALITIES[rng.randi() % GameData.GOVERNOR_PERSONALITIES.size()],
			"name": "%s %s" % [GameData.FIRST_NAMES[rng.randi() % GameData.FIRST_NAMES.size()], GameData.LAST_NAMES[rng.randi() % GameData.LAST_NAMES.size()]]
		}

	# ---------------------------------------------------------
	# 3. Organic Roads (Historical Trade Routes)
	# ---------------------------------------------------------
	step_completed.emit("SURVEYING HIGHWAYS...")
	var astar = AStarGrid2D.new()
	astar.region = Rect2i(0, 0, w, h)
	astar.cell_size = Vector2(1, 1)
	astar.diagonal_mode = AStarGrid2D.DIAGONAL_MODE_ONLY_IF_NO_OBSTACLES
	astar.update()
	for y in range(h):
		for x in range(w):
			var t = world_grid[y][x]
			var e = elevation_map[y][x]
			var weight = 1.0
			
			match t:
				'#': weight = 4.0 # Forest
				'&': weight = 7.0 # Jungle (Harder)
				'"': weight = 3.0 # Desert
				'*': weight = 2.0 # Tundra
				'o': weight = 2.5 # Hills
				'≈': weight = 25.0 # Major River (Needs big bridges)
				'/': weight = 12.0 # Standard River
				'\\': weight = 12.0
				'^', 'O': # Mountains
					# Mountain Pass logic: Weight scales with elevation
					# A "low" mountain (0.65) is much easier than a peak (0.9)
					weight = 15.0 + (e * 60.0) 
				'~': weight = 200.0 # Ocean (Almost solid)
			
			# DWARF FORTRESS LOGIC: River Attraction
			# If a tile is adjacent to a river, reduce its weight.
			# Historically, roads follow rivers for navigation and flat terrain.
			var is_river_adjacent = false
			for dy in [-1, 0, 1]:
				for dx in [-1, 0, 1]:
					var np = Vector2i(x + dx, y + dy)
					if is_in_bounds(np.x, np.y, w, h):
						if world_grid[np.y][np.x] in ['≈', '/', '\\']:
							is_river_adjacent = true
							break
			
			if is_river_adjacent and t not in ['≈', '/', '\\', '^']:
				weight *= 0.6 # 40% discount for following a river bank
				
			astar.set_point_weight_scale(Vector2i(x, y), weight)
			if t == '~': astar.set_point_solid(Vector2i(x, y), true)

	var s_keys = world_settlements.keys()
	var connected = []
	var edges = [] 
	if s_keys.size() > 0:
		connected = [s_keys[0]]
		var pool = s_keys.slice(1)
		while not pool.is_empty():
			var best_d = 999999
			var best_p = [null, null]
			var p_idx = -1
			for c in connected:
				for i in range(pool.size()):
					var d = c.distance_squared_to(pool[i])
					if d < best_d:
						best_d = d
						best_p = [c, pool[i]]
						p_idx = i
			if best_p[0] != null:
				edges.append(best_p)
				connected.append(best_p[1])
				pool.remove_at(p_idx)
			else: break
		for edge in edges:
			var path = astar.get_id_path(edge[0], edge[1])
			for p_pos in path:
				# Allow roads to be visible on most land/river tiles
				if world_grid[p_pos.y][p_pos.x] in ['.', '#', '"', '*', '&', '/', '\\', '^', 'o', '≈']:
					world_grid[p_pos.y][p_pos.x] = '='
					astar.set_point_weight_scale(p_pos, 0.5) 
	
	# ---------------------------------------------------------
	# 4. Final Polish & Starting Stocks
	# ---------------------------------------------------------
	step_completed.emit("CALCULATING ECONOMY...")
	for s_pos in world_settlements:
		var s = world_settlements[s_pos]
		# Everyone gets a baseline 7 days of food for stability
		s.inventory["grain"] = s.population * Globals.DAILY_BUSHELS_PER_PERSON * 7.0
		s.crown_stock = 1000
		
		# Provincial Capitals (Seats) get extra
		var is_seat = false
		for p_id in provinces:
			if provinces[p_id].capital == s_pos:
				is_seat = true
				break
				
		if is_seat:
			s.crown_stock = 3000
			s.inventory["wood"] = 150
			s.inventory["iron"] = 40
			s.inventory["coal"] = 20
			
		# Royal Centers (Tier 4) get even more
		if s.tier == 4:
			s.crown_stock = 8000
			s.inventory["wood"] = 500
			s.inventory["iron"] = 100
			s.inventory["coal"] = 50
			s.inventory["steel"] = 25
			s.population = max(s.population, 800)
			
			# ENSURE NAME & CLASSES MATCH PROMOTION
			var p_id = province_grid[s.pos.y][s.pos.x]
			var p_name = provinces[p_id].name if p_id != -1 else "Royal"
			s.name = "%s Royal Center" % p_name
			s.sync_social_classes()
			
			# SYNC HOUSES FOR PROMOTION
			var cap_provided = s.buildings.get("housing_district", 0) * 100
			s.houses = max(20, int((s.population - cap_provided) / 5.0) + 5)
			
			world_grid[s_pos.y][s_pos.x] = 'C'
		elif s.type == "city":
			world_grid[s_pos.y][s_pos.x] = 'C'
		elif s.type == "town" or s.tier == 2:
			world_grid[s_pos.y][s_pos.x] = 'v'
		elif s.type == "satellite" or s.type == "hamlet":
			world_grid[s_pos.y][s_pos.x] = 'h'
			
		# Final Economy Pass: Ensure population changes are reflected in land usage and housing
		s.sync_social_classes()
		var final_cap_provided = s.buildings.get("housing_district", 0) * 100
		s.houses = max(20, int((s.population - final_cap_provided) / 5.0) + 5)
		EconomyManager.recalculate_production(s, world_grid, world_resources, geology)

	# ---------------------------------------------------------
	# 4.5. Administrative Feudalism (Capitals & Lords)
	# ---------------------------------------------------------
	step_completed.emit("DELINEATING BORDERS...")
	
	# Ensure every province has at least one settlement to act as the Seat
	for p_id in provinces:
		var p = provinces[p_id]
		# Find already existing settlements in this province
		var local_settlements = []
		for s_pos in world_settlements:
			if province_grid[s_pos.y][s_pos.x] == p_id:
				local_settlements.append(s_pos)
		
		if local_settlements.is_empty():
			# This land was empty after history. Found a small frontier outpost.
			var p_seed = p.center
			var outpost = GDSettlement.new(p_seed)
			outpost.name = p.name + " Outpost"
			outpost.population = 150
			outpost.tier = 1
			outpost.type = "hamlet"
			outpost.faction = p.get("owner", "neutral")
			world_settlements[p_seed] = outpost
			p.capital = p_seed
			world_grid[p_seed.y][p_seed.x] = 'h'
		else:
			# Pick the largest existing city as the Provincial Capital
			var best_s = local_settlements[0]
			var max_pop = world_settlements[best_s].population
			for s_pos in local_settlements:
				if world_settlements[s_pos].population > max_pop:
					max_pop = world_settlements[s_pos].population
					best_s = s_pos
			p.capital = best_s
			
		# All non-capital settlements in the province become fiefs/satellites
		for s_pos in local_settlements:
			if s_pos != p.capital:
				var s = world_settlements[s_pos]
				if s.tier >= 3: s.type = "city"
				else: s.type = "satellite"

	# ---------------------------------------------------------
	# 5. Entity Spawning (One Lord per Province Capital)
	# ---------------------------------------------------------
	step_completed.emit("RAISING ARMIES...")
	var armies: Array[GDArmy] = []
	var caravans: Array[GDCaravan] = []
	
	# Spawn Bandits (Global)
	var b_count = 0
	for attempt in range(1000):
		if b_count >= 35: break
		var bpos = Vector2i(rng.randi_range(0, w-1), rng.randi_range(0, h-1))
		if mainland_set.has(bpos) and world_grid[bpos.y][bpos.x] in ['.', '#', '"', '*', '&']:
			var bandit = GDArmy.new(bpos, "bandits")
			bandit.name = "Bandit Ravagers"
			bandit.type = "bandit"
			for j in range(25): bandit.roster.append(GameData.generate_recruit(rng, 1))
			armies.append(bandit)
			b_count += 1
	
	# Spawn Lords only at Provincial Capitals
	for p_id in provinces:
		var p = provinces[p_id]
		if p.capital == null: continue
		
		var s_pos = p.capital
		var s = world_settlements[s_pos]
		
		# Skip if Neutral or Bandit-controlled (though they shouldn't be yet)
		if s.faction == "neutral" or s.faction == "bandits": continue
		
		var lord = GDArmy.new(s_pos, s.faction)
		lord.name = "Lord " + GameData.LAST_NAMES[rng.randi() % GameData.LAST_NAMES.size()]
		lord.type = "lord"
		lord.home_fief = s_pos
		
		# Set Administrative Title
		lord.name += " of " + p.name
		
		# AI Personalities
		var doctrines = ["defender", "conqueror", "raider"]
		lord.doctrine = doctrines[rng.randi() % doctrines.size()]
		var personalities = ["balanced", "aggressive", "cautious"]
		lord.personality = personalities[rng.randi() % personalities.size()]
		
		# Create the Political NPC record and link it
		var lord_npc_id = "npc_lord_" + str(rng.randi())
		var npc_obj = GDNPC.new(lord_npc_id, lord.name, "Lord", s_pos, s.faction)
		npc_obj.crowns = 1000 # Starting personal wealth
		s.npcs.append(npc_obj)
		s.lord_id = lord_npc_id
		lord.lord_id = lord_npc_id
		
		# Scale roster to the development of the province seat
		var r_count = 30 + clamp(int(s.population / 10), 0, 150)
		for j in range(r_count): 
			lord.roster.append(GameData.generate_recruit(rng, clamp(s.tier, 1, 4)))
			
		armies.append(lord)
		
		# Provincial Caravans (Supply Lines)
		var cav = GDCaravan.new(s_pos, s.faction)
		cav.origin = s_pos
		cav.crowns = 5000 # Starting capital for world-gen caravans
		for j in range(10): cav.roster.append(GameData.generate_recruit(rng, 2))
		caravans.append(cav)

	var ruins = {}
	var ruin_types = ["Vault", "Crypt", "Temple", "Keep"]
	var target_ruins = 10 + (savagery * 3) # Scaled by savagery config
	var attempts = 0
	
	# Whitelist of land tiles (Plains, Forest, Desert, Tundra, Jungle, Hills, Peaks, High Mountains)
	var land_tiles = ['.', '#', '"', '*', '&', 'o', '^', 'O']
	
	while ruins.size() < target_ruins and attempts < 1000:
		attempts += 1
		var rpos = Vector2i(rng.randi_range(0, w-1), rng.randi_range(0, h-1))
		
		# Ensure it's on land, not on a settlement, and not in water
		var t = world_grid[rpos.y][rpos.x]
		if t in land_tiles:
			if not world_settlements.has(rpos):
				# Additional check: avoid being directly adjacent to water if possible for "land" ruins
				var neighbor_water = _check_terrain_near(rpos, world_grid, 1, ['~', '≈', '/', '\\'])
				if not neighbor_water or rng.randf() < 0.2: # 20% chance to allow coastal ruins
					ruins[rpos] = {
						"name": "Old " + ruin_types[rng.randi() % ruin_types.size()],
						"type": "ruin",
						"explored": false,
						"danger": rng.randi_range(1, 5),
						"loot_quality": rng.randi_range(1, 5)
					}

	var s_list = world_settlements.keys()
	# Update start_pos for player (find a random settlement)
	var start_pos = Vector2i(w/2, h/2)
	if capitals.size() > 0:
		start_pos = capitals[rng.randi() % capitals.size()]
	elif s_list.size() > 0: 
		start_pos = s_list[0]
	else:
		for p in mainland_set:
			start_pos = p
			break

	# ---------------------------------------------------------
	# 6. NPC Generation
	# ---------------------------------------------------------
	step_completed.emit("POPULATING SETTLEMENTS...")
	for s_pos in world_settlements:
		var s = world_settlements[s_pos]
		SettlementManager.refresh_npcs(s)
		
		# Link NPCs to armies generated in worldgen
		if s.lord_id != "":
			for a in armies:
				if a.type == "lord" and a.pos == s_pos and a.lord_id == "":
					a.lord_id = s.lord_id
					# We can't use GameState.find_npc yet, so we look inside the settlement
					for npc in s.npcs:
						if npc.id == s.lord_id:
							a.name = "%s %s's Party" % [npc.title, npc.name]
							break
					break

	return {
		"grid": world_grid, "resources": world_resources, "geology": geology,
		"settlements": world_settlements, "ruins": ruins, "start_pos": start_pos,
		"armies": armies, "caravans": caravans,
		"province_grid": province_grid, "provinces": provinces,
		"factions": faction_list
	}

func _get_livable_score(pos: Vector2i, w: int, h: int, world_grid: Array, geology: Dictionary, world_resources: Dictionary) -> float:
	var score = 0.0
	var t = world_grid[pos.y][pos.x]
	
	# Biome Bonus
	var g = geology.get(pos, {"temp": 0.5, "rain": 0.5})
	var temp = g.temp
	var rain = g.rain
	
	# Ideal range for humans (not too hot, not too cold, moderate rain)
	var t_score = 1.0 - abs(temp - 0.5) * 2.0 
	var r_score = 1.0 - abs(rain - 0.6) * 2.0
	score += (t_score + r_score) * 10.0
	
	# Feature Bonuses
	if t == '.': score += 15.0 # Plains are ideal
	elif t == '#': score += 5.0 # Forest
	elif t == 'o': score += -5.0 # Hills are harder
	elif t == '^': score += -20.0 # Mountains are very hard
	elif t == '"': score += -15.0 # Desert
	elif t == '*': score += -10.0 # Tundra
	
	# Resource Proximity
	if world_resources.has(pos): score += 10.0
	
	# Adjacency Bonuses
	for dy in range(-2, 3):
		for dx in range(-2, 3):
			var np = pos + Vector2i(dx, dy)
			if np.x < 0 or np.x >= w or np.y < 0 or np.y >= h: continue
			var nt = world_grid[np.y][np.x]
			if nt in ['~', '/', '\\', '≈']: score += 15.0 # Water proximity is huge
			if nt == '#': score += 2.0 # Proximity to wood
			
	return score

func _get_site_potential(pos: Vector2i, w: int, h: int, world_grid: Array, world_resources: Dictionary, geology: Dictionary) -> Dictionary:
	var capacity = 0.0
	var magnetism = 1.0 
	var revenue = 0.0
	
	# Evaluate resources in a 3x3 area for core potential
	for dy in range(-1, 2):
		for dx in range(-1, 2):
			var np = pos + Vector2i(dx, dy)
			if not is_in_bounds(np.x, np.y, w, h): continue
			
			var t = world_grid[np.y][np.x]
			
			# 1. Carrying Capacity (Acreage Proxy)
			# Globals: ACRES_PER_TILE=250, BUSHELS_PER_ACRE=12, BUSHELS_PER_PERSON=15
			# 250 * 12 / 15 = 200 people capacity per Plains tile
			if t == '.': capacity += 200 
			elif t == '#': capacity += 25 # REDUCED: Forest clearing is only 20% efficient
			elif t in ['~', '/', '\\']: capacity += 40 # Fishing/Floodplains are slightly better
			elif t == '*': capacity += 15 # REDUCED: Tundra is for survival, not growth
			elif t == '&': capacity += 10 # Swamps are hard to settle
			
			# 2. Magnetism (Industry Pull)
			if world_resources.has(np):
				var res = world_resources[np]
				match res:
					"gold", "gems": 
						magnetism += 1.0
						revenue += 1000
					"silver", "iron", "copper": 
						magnetism += 0.5
						revenue += 400
					"spices", "ivory":
						magnetism += 0.8
						revenue += 600
					"horses", "game":
						magnetism += 0.2
						revenue += 100

	# Factor in climate (Cold/Hot reduce efficiency/magnetism slightly)
	var g = geology.get(pos, {"temp": 0.0, "rain": 0.5})
	var climate_penalty = abs(g.temp) * 0.3
	magnetism = max(0.5, magnetism - climate_penalty)
	
	# Total Potential Revenue uses capacity as a tax base proxy
	revenue += (capacity * 2.0)
	
	return {
		"capacity": capacity,
		"magnetism": magnetism,
		"revenue": revenue
	}

func _find_spillover_target(start: Vector2i, elevation_map: Array, w: int, h: int) -> Variant:
	var best_n = null
	var min_e = 999.0
	
	for dy in [-1, 0, 1]:
		for dx in [-1, 0, 1]:
			if dx == 0 and dy == 0: continue
			var n = start + Vector2i(dx, dy)
			if is_in_bounds(n.x, n.y, w, h):
				var e = elevation_map[n.y][n.x]
				if e < min_e:
					min_e = e
					best_n = n
					
	return best_n

func _check_terrain_near(pos: Vector2i, grid: Array, r: int, chars: Array) -> bool:
	var h = grid.size()
	var w = grid[0].size()
	for dy in range(-r, r+1):
		for dx in range(-r, r+1):
			var p = pos + Vector2i(dx, dy)
			if p.x >= 0 and p.x < w and p.y >= 0 and p.y < h:
				if grid[p.y][p.x] in chars: return true
	return false

func is_in_bounds(x: int, y: int, w: int, h: int) -> bool:
	return x >= 0 and x < w and y >= 0 and y < h
