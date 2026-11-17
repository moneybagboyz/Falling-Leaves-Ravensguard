class_name TectonicsPhase
extends WorldGenPhase

## Handles tectonic plate simulation and elevation generation
## Creates elevation_map, temp_map (initial), moisture_map (initial), drainage_map, strata_map

func get_phase_name() -> String:
	return "Tectonics"

func execute(context: WorldGenContext) -> bool:
	var w = context.width
	var h = context.height
	var rng = context.rng
	var world_grid = context.world_grid
	
	# Tectonic simulation parameters
	var _plate_activity = 1.8
	var noise_influence = 0.22
	
	var world_center = Vector2(w / 2.0, h / 2.0)
	var cont_centers = [
		Vector2(w * 0.25, h * 0.5),
		Vector2(w * 0.75, h * 0.5)
	]
	
	# Generate tectonic plates
	step_completed.emit("SIMULATING TECTONICS...")
	for i in range(context.num_plates):
		var seed_pos = Vector2(rng.randf_range(0, w), rng.randf_range(0, h))
		var is_oceanic = true
		
		match context.layout:
			"Pangea":
				var dist_to_center = seed_pos.distance_to(world_center)
				var normalized_dist = dist_to_center / (min(w, h) * 0.5)
				is_oceanic = normalized_dist > 0.65
				if normalized_dist < 0.25: is_oceanic = false
			"Continents":
				is_oceanic = true
				for c in cont_centers:
					if seed_pos.distance_to(c) < min(w, h) * 0.35:
						is_oceanic = false
						break
			"Archipelago":
				is_oceanic = rng.randf() > 0.2
		
		var velocity = Vector2.ZERO
		if is_oceanic:
			velocity = Vector2(rng.randf_range(-1.5, 1.5), rng.randf_range(-1.5, 1.5)).normalized() * 2.0
		else:
			var target = world_center
			if context.layout == "Continents":
				target = cont_centers[0] if seed_pos.x < w * 0.5 else cont_centers[1]
			velocity = (target - seed_pos).normalized() * rng.randf_range(0.3, 1.2)
		
		context.plates.append({
			"id": i,
			"seed": seed_pos,
			"velocity": velocity,
			"is_oceanic": is_oceanic
		})
	
	# Visualize plate seeds
	for p in context.plates:
		var px = int(p.seed.x)
		var py = int(p.seed.y)
		if px >= 0 and px < w and py >= 0 and py < h:
			world_grid[py][px] = 'P'
	await (Engine.get_main_loop() as SceneTree).process_frame
	await (Engine.get_main_loop() as SceneTree).process_frame
	
	# Generate elevation and geology
	step_completed.emit("RAISING MOUNTAINS...")
	var tiles_processed = 0
	var total_tiles = w * h
	var update_interval = 3000
	
	# Create strata noise generator
	var noise_strata = FastNoiseLite.new()
	noise_strata.seed = rng.randi()
	noise_strata.frequency = 0.008
	
	for y in range(h):
		context.elevation_map.append([])
		context.temp_map.append([])
		context.moisture_map.append([])
		context.drainage_map.append([])
		context.strata_map.append([])
		
		for x in range(w):
			tiles_processed += 1
			
			if tiles_processed % update_interval == 0:
				step_completed.emit("RAISING MOUNTAINS [%d%%]" % [int((float(tiles_processed) / total_tiles) * 100)])
				await (Engine.get_main_loop() as SceneTree).process_frame
			
			var pos_v = Vector2(x, y)
			
			# Find two nearest plates
			var d1 = 999999.0
			var d2 = 999999.0
			var p1 = null
			var p2 = null
			
			for p in context.plates:
				var d = pos_v.distance_to(p.seed)
				if d < d1:
					d2 = d1
					p2 = p1
					d1 = d
					p1 = p
				elif d < d2:
					d2 = d
					p2 = p
			
			# Base elevation
			var base_e = 0.12 if p1.is_oceanic else 0.40
			var e = base_e
			
			# Boundary interaction
			var boundary_dist = d2 - d1
			var jitter = context.noise_detail.get_noise_2d(x, y) * 8.0
			var influence_range = 14.0 + jitter
			
			if boundary_dist < influence_range:
				var weight = pow(1.0 - (boundary_dist / influence_range), 1.5)
				var normal = (p2.seed - p1.seed).normalized()
				var dot = p1.velocity.dot(normal) - p2.velocity.dot(normal)
				
				if dot > 0:  # Convergent
					var variation = 0.8 + (context.noise_detail.get_noise_2d(y, x) * 0.4)
					if not p1.is_oceanic and not p2.is_oceanic:
						e += dot * 0.38 * weight * variation
					elif p1.is_oceanic and not p2.is_oceanic:
						e += dot * 0.32 * weight * variation
					elif not p1.is_oceanic and p2.is_oceanic:
						e += dot * 0.32 * weight * variation
					else:
						e += dot * 0.20 * weight * variation
				else:  # Divergent
					e += dot * 0.3 * weight
			
			# Detail noise
			e += context.noise_detail.get_noise_2d(x, y) * noise_influence
			
			# Layout masking
			var mask = 1.0
			var dist_to_center = pos_v.distance_to(world_center)
			
			match context.layout:
				"Pangea":
					var normalized_dist = dist_to_center / (min(w, h) * 0.58)
					mask = clamp(1.4 - normalized_dist, 0.0, 1.0)
				"Continents":
					var m1 = clamp(1.2 - (pos_v.distance_to(cont_centers[0]) / (min(w, h) * 0.4)), 0.0, 1.0)
					var m2 = clamp(1.2 - (pos_v.distance_to(cont_centers[1]) / (min(w, h) * 0.4)), 0.0, 1.0)
					mask = max(m1, m2)
				"Archipelago":
					var island_noise = FastNoiseLite.new()
					island_noise.seed = rng.seed + 99
					island_noise.frequency = 0.08
					var n = island_noise.get_noise_2d(x, y)
					mask = clamp(n + 0.4, 0.0, 1.0)
			
			e *= mask
			var final_e = clamp(e, 0.0, 1.0)
			context.elevation_map[y].append(final_e)
			
			# Visualization
			if final_e < 0.32: world_grid[y][x] = '~'
			elif final_e < 0.48: world_grid[y][x] = '.'
			elif final_e < 0.60: world_grid[y][x] = 'o'
			elif final_e < 0.75: world_grid[y][x] = 'O'
			else: world_grid[y][x] = '^'
			
			# Initial temperature
			var lat_factor = (float(y) / h) * 2.0 - 1.0
			var t = context.noise_temp.get_noise_2d(x, y)
			t = (t + 0.15) * context.temp_bias - (final_e * 0.2) - (abs(lat_factor) * 0.45)
			context.temp_map[y].append(t)
			
			# Initial moisture (will be calculated in hydrology phase)
			context.moisture_map[y].append(0.0)
			
			# Drainage (soil permeability)
			var drain = (context.noise_drainage.get_noise_2d(x, y) + 1.0) / 2.0
			context.drainage_map[y].append(drain)
			
			# Geological strata
			var s_val = noise_strata.get_noise_2d(x, y)
			var layers = []
			if s_val < -0.3: layers = ["igneous", "metamorphic", "sedimentary"]
			elif s_val < 0.3: layers = ["metamorphic", "sedimentary", "soil"]
			else: layers = ["sedimentary", "sedimentary", "soil"]
			context.strata_map[y].append(layers)
	
	return true

func cleanup(context: WorldGenContext) -> void:
	# Plate map can be freed after elevation generation
	context.plate_map.clear()
