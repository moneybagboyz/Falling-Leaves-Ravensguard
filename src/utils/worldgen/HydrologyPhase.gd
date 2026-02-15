class_name HydrologyPhase
extends WorldGenPhase

## Handles flow-based hydrology (DF style)
## Creates river_map and lake_map for water flow accumulation

var river_map = {}  # Vector2i -> flow accumulation
var lake_map = {}  # Vector2i -> is_lake

func get_phase_name() -> String:
	return "Hydrology"

func execute(context: WorldGenContext) -> bool:
	var w = context.width
	var h = context.height
	
	step_completed.emit("CALCULATING HYDROLOGY (DF STYLE)...")
	
	# Build sortable tile list
	var sort_tiles = []
	for y in range(h):
		for x in range(w):
			var e = context.elevation_map[y][x]
			if e > 0.32:
				sort_tiles.append({"pos": Vector2i(x, y), "e": e})
	
	# Sort by elevation (high to low)
	sort_tiles.sort_custom(func(a, b): return a.e > b.e)
	
	var processed = 0
	var total_land = sort_tiles.size()
	
	for tile_data in sort_tiles:
		var pos = tile_data.pos
		processed += 1
		if processed % 5000 == 0:
			step_completed.emit("TRACING WATERWAYS [%d%%]" % [int((float(processed) / total_land) * 100)])
			await (Engine.get_main_loop() as SceneTree).process_frame
		
		# Base inflow from moisture/rain
		var inflow = 1.0 + context.moisture_map[pos.y][pos.x]
		river_map[pos] = river_map.get(pos, 0.0) + inflow
		
		# Find lowest neighbor
		var lowest_val = context.elevation_map[pos.y][pos.x]
		var target = null
		
		for dy in [-1, 0, 1]:
			for dx in [-1, 0, 1]:
				if dx == 0 and dy == 0: continue
				var n = pos + Vector2i(dx, dy)
				if n.x >= 0 and n.x < w and n.y >= 0 and n.y < h:
					var e_val = context.elevation_map[n.y][n.x]
					if e_val < lowest_val:
						lowest_val = e_val
						target = n
		
		if target != null:
			river_map[target] = river_map.get(target, 0.0) + river_map[pos]
		elif context.elevation_map[pos.y][pos.x] > 0.32:
			# No lower neighbor = lake/sink
			if river_map[pos] > 10.0:
				lake_map[pos] = true
				
				# Lake overflow
				var spill_target = _find_spillover_target(pos, context.elevation_map, w, h)
				if spill_target != null:
					river_map[spill_target] = river_map.get(spill_target, 0.0) + river_map[pos]
	
	return true

func _find_spillover_target(pos: Vector2i, elevation_map: Array, w: int, h: int) -> Variant:
	var e_center = elevation_map[pos.y][pos.x]
	var min_barrier = 9999.0
	var best_target = null
	
	# Check 3-tile radius for spillover pass
	for dy in range(-3, 4):
		for dx in range(-3, 4):
			if dx == 0 and dy == 0: continue
			var n = pos + Vector2i(dx, dy)
			if n.x >= 0 and n.x < w and n.y >= 0 and n.y < h:
				var e_neighbor = elevation_map[n.y][n.x]
				if e_neighbor < e_center:
					var barrier_height = e_center - e_neighbor
					if barrier_height < min_barrier:
						min_barrier = barrier_height
						best_target = n
	
	return best_target

func cleanup(context: WorldGenContext) -> void:
	# Store for next phase
	context.world_resources["_river_map"] = river_map
	context.world_resources["_lake_map"] = lake_map
