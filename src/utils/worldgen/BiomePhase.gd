class_name BiomePhase
extends WorldGenPhase

## Handles final biome assignment, geology, and resource placement

const GameData = preload("res://src/core/GameData.gd")
const TerrainColors = preload("res://src/ui/core/TerrainColors.gd")
const UIFormatting = preload("res://src/ui/core/UIFormatting.gd")

func get_phase_name() -> String:
	return "Biomes"

func execute(context: WorldGenContext) -> bool:
	var w = context.width
	var h = context.height
	var rng = context.rng
	var world_grid = context.world_grid
	
	# Extract river/lake data from hydrology phase
	var river_map = context.world_resources.get("_river_map", {})
	var lake_map = context.world_resources.get("_lake_map", {})
	
	step_completed.emit("PAINTING BIOMES...")
	for y in range(h):
		if y % 20 == 0:
			step_completed.emit("PAINTING BIOMES [%d%%]" % [int((float(y) / h) * 100)])
			await (Engine.get_main_loop() as SceneTree).process_frame
		
		for x in range(w):
			var e = context.elevation_map[y][x]
			var t = context.temp_map[y][x]
			var m = context.moisture_map[y][x]
			var d = context.drainage_map[y][x]
			var pos = Vector2i(x, y)
			
			# River influence on local moisture
			var flow = river_map.get(pos, 0.0)
			if flow > 200.0: m += 0.2
			
			var tile = '.'
			if e < 0.32:
				tile = '~'  # Ocean
			elif lake_map.has(pos) and flow > 100.0:
				tile = '≈'  # Inland lake
			elif flow > 1500.0:
				tile = '≈'  # Major river
			elif flow > 400.0:
				tile = '/' if (x + y) % 2 == 0 else '\\'  # River
			elif e > 0.60:
				tile = '^'  # Mountains
			else:
				# Biome matrix
				if t < -0.6:
					tile = 'X'  # Glaciers
				elif t < -0.35:
					if d < 0.2: tile = '*'  # Tundra
					else: tile = '#'  # Taiga
				elif t < 0.15:
					if m < 0.1: tile = '"'  # Badlands
					elif d < 0.3: tile = '.'  # Grassland
					else: tile = '#'  # Forest
				elif t < 0.4:
					if m < 0.12: tile = '"'  # Desert
					elif d < 0.4: tile = '.'  # Savanna
					else: tile = '&'  # Tropical forest
				else:  # Hot
					if m < 0.3: tile = '"'  # Scorching desert
					else: tile = '&'  # Jungle
				
				# Hills override
				if e > 0.48 and tile not in ['&', '#', '"', '~']:
					tile = 'o'
			
			world_grid[y][x] = tile
			
			# Geology
			var layers = context.strata_map[y][x]
			context.geology[pos] = {
				"temp": t, 
				"rain": m, 
				"layers": layers, 
				"biome": tile, 
				"elevation": e, 
				"drainage": d
			}
			
			# Resource placement
			_place_resources(pos, tile, t, m, flow, layers, rng, context)
	
	# Clear rendering cache
	TerrainColors.clear_cache()
	UIFormatting.clear_cache()
	
	# Clean up temporary river/lake data
	context.world_resources.erase("_river_map")
	context.world_resources.erase("_lake_map")
	
	return true

func _place_resources(pos: Vector2i, tile: String, t: float, m: float, flow: float, layers: Array, rng: RandomNumberGenerator, context: WorldGenContext) -> void:
	var roll = rng.randf()
	
	# Surface resources (biome dependent)
	if tile == '#' and roll < 0.06: 
		context.world_resources[pos] = "wood"
	elif tile == '#' and roll < 0.08: 
		context.world_resources[pos] = "game"
	elif tile == '.' and roll < 0.03: 
		context.world_resources[pos] = "horses"
	elif tile == '&' and roll < 0.10: 
		context.world_resources[pos] = "peat"
	elif tile == '"' and roll < 0.05: 
		context.world_resources[pos] = "salt"
	elif tile == '*' and roll < 0.07: 
		context.world_resources[pos] = "furs"
	
	# Luxury resources
	if t > 0.6 and m > 0.5 and roll < 0.04: 
		context.world_resources[pos] = "spices"
	elif t > 0.4 and m < 0.4 and tile == '.' and roll < 0.03: 
		context.world_resources[pos] = "ivory"
	elif tile == '&' and roll < 0.12: 
		context.world_resources[pos] = "clay"
	
	# Subsurface minerals (geology dependent)
	var roll_sub = rng.randf()
	var density_mod = context.mineral_density / 5.0
	
	var sorted_layers = layers.duplicate()
	sorted_layers.sort_custom(func(a, b):
		var score = {"igneous": 2, "metamorphic": 1, "sedimentary": 0}
		return score.get(a, -1) > score.get(b, -1)
	)
	
	for layer in sorted_layers:
		if GameData.GEOLOGY_RESOURCES.has(layer):
			var layer_res = GameData.GEOLOGY_RESOURCES[layer]
			var found_sub = false
			var res_keys = layer_res.keys()
			res_keys.sort()
			for res in res_keys:
				if roll_sub < (layer_res[res] * density_mod):
					context.world_resources[pos] = res
					found_sub = true
					break
			if found_sub: break
	
	# River/silt bonus
	if tile == '~' and flow > 1.0 and roll < 0.15:
		context.world_resources[pos] = "clay"

func cleanup(context: WorldGenContext) -> void:
	# Can now free elevation/temp/moisture/drainage maps
	context.elevation_map.clear()
	context.temp_map.clear()
	context.moisture_map.clear()
	context.drainage_map.clear()
	# strata_map kept for geology reference
