class_name RoadPhase
extends WorldGenPhase

## Handles road network generation using AStar pathfinding

func get_phase_name() -> String:
	return "Roads"

func execute(context: WorldGenContext) -> bool:
	var w = context.width
	var h = context.height
	var world_grid = context.world_grid
	
	step_completed.emit("SURVEYING HIGHWAYS...")
	
	var astar = AStarGrid2D.new()
	astar.region = Rect2i(0, 0, w, h)
	astar.cell_size = Vector2(1, 1)
	astar.diagonal_mode = AStarGrid2D.DIAGONAL_MODE_ONLY_IF_NO_OBSTACLES
	astar.update()
	
	# Set terrain costs
	for y in range(h):
		for x in range(w):
			var t = world_grid[y][x]
			var e = context.geology[Vector2i(x, y)].elevation if context.geology.has(Vector2i(x, y)) else 0.5
			var weight = 1.0
			
			match t:
				'#': weight = 4.0
				'&': weight = 7.0
				'"': weight = 3.0
				'*': weight = 2.0
				'o': weight = 2.5
				'≈': weight = 25.0
				'/', '\\': weight = 12.0
				'^', 'O': weight = 15.0 + (e * 60.0)
				'~': weight = 200.0
			
			# River attraction
			var is_river_adjacent = false
			for dy in [-1, 0, 1]:
				for dx in [-1, 0, 1]:
					var np = Vector2i(x + dx, y + dy)
					if np.x >= 0 and np.x < w and np.y >= 0 and np.y < h:
						if world_grid[np.y][np.x] in ['≈', '/', '\\']:
							is_river_adjacent = true
							break
			
			if is_river_adjacent and t not in ['≈', '/', '\\', '^']:
				weight *= 0.6
			
			astar.set_point_weight_scale(Vector2i(x, y), weight)
			if t == '~': astar.set_point_solid(Vector2i(x, y), true)
	
	# Connect settlements with minimal spanning tree
	var s_keys = context.world_settlements.keys()
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
		
		# Draw roads
		for edge in edges:
			var path = astar.get_id_path(edge[0], edge[1])
			for p_pos in path:
				if world_grid[p_pos.y][p_pos.x] in ['.', '#', '"', '*', '&', '/', '\\', '^', 'o', '≈']:
					world_grid[p_pos.y][p_pos.x] = '='
					astar.set_point_weight_scale(p_pos, 0.5)
			
			context.roads.append({"start": edge[0], "end": edge[1], "path": path})
	
	return true

func cleanup(context: WorldGenContext) -> void:
	pass
