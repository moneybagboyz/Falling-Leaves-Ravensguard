class_name CityController
extends Node
# Updated by Copilot

var width = 500
var height = 500
var active = false
var city_name = "Settlement"

# --- LAYERED DATA STRUCTURE ---
var layers = {
	"terrain": [],   # . (grass), ~ (water), " (dirt)
	"zoning": [],    # residential, market, industry
	"structure": [], # B (building), # (wall), K (keep), M (market), S (smith)
	"pathing": [],   # + (dirt road), = (stone road)
	"entities": {}   # pos -> Entity object
}

var grid = [] # Legacy grid for backward compat rendering
var fog_of_war = []

var player_pos = Vector2i(250, 250)
var world_pos = Vector2i.ZERO # The world tile currently being rendered
var bp_offset = Vector2.ZERO # Blueprint offset for slicing
var tile_under_player = "."

# Studio/Preview Meta
var buildings = []
var wall_segments = []
var gates = []
var towers = []
var engines = [] # Defensive engines on walls
var capture_points = []
var structure_health = {} # pos -> float

# --- Layout Blueprint (Shared with Region Map) ---
var blueprint = {
	"districts": [], # {pos: Vector2, type: "keep", "market", "residential", "industrial", radius: float}
	"roads": [],     # {points: Array[Vector2], type: "artery", "alley"}
	"parcels": [],   # {rect: Rect2i, type: "fallow", "farm", "urban"}
	"hull": [],      # Array[Vector2] (Wall polygon)
	"seed": 0
}

static func build_blueprint(s: GDSettlement, world_seed: int) -> Dictionary:
	var rng = RandomNumberGenerator.new()
	rng.seed = s.pos.x + s.pos.y * 1000 + world_seed
	
	var bp = {
		"districts": [],
		"roads": [],
		"parcels": [],
		"hull": [],
		"water": [], # Array of {points: PackedVector2Array, width: float}
		"seed": rng.seed
	}
	
	var center = Vector2(250, 250)
	var is_major = s.type in ["town", "city", "metropolis", "castle"]
	var noise = FastNoiseLite.new()
	noise.seed = world_seed
	noise.frequency = 0.015

	# --- PHASE 0: WATERWAYS (The major constraint) ---
	# Fix: Use seed-based direction to solve "Water Problem" (mismatched flow).
	# Rivers now cut across the map, acting as obstacles for the city growth.
	if s.river_acres > 50:
		var flow_angle = rng.randf_range(0, PI) # Generally West->East or North->South
		# Start/End far off-map
		var start = center + Vector2.from_angle(flow_angle + PI) * 400
		var end = center + Vector2.from_angle(flow_angle) * 400
		
		var curve = Curve2D.new()
		curve.add_point(start)
		# Add meander
		var mid = (start + end) / 2
		mid += Vector2(rng.randf_range(-60, 60), rng.randf_range(-60, 60))
		curve.add_point(mid, Vector2.ZERO, Vector2.ZERO) 
		curve.add_point(end)
		
		var r_points = curve.get_baked_points()
		var r_width = 15.0 + (s.river_acres / 15.0) # 15m to 50m+ rivers
		bp.water.append({"points": r_points, "width": r_width})

	# --- PHASE 1: A* GRID ---
	# Setup A* Grid (Coarse 25m resolution)
	var astar = AStar2D.new()
	var grid_step = 25
	var cols = 500 / grid_step
	var rows = 500 / grid_step
	
	# Add points and weights
	for y in range(rows):
		for x in range(cols):
			var id = y * cols + x
			var pos = Vector2(x * grid_step + grid_step/2.0, y * grid_step + grid_step/2.0)
			astar.add_point(id, pos)
			
			var weight = 1.0
			# Noise terrain cost
			var n_val = noise.get_noise_2d(pos.x, pos.y)
			weight += max(0, n_val * 4.0)
			
			# River Avoidance (Soft Constraint: Bridges are expensive)
			for w in bp.water:
				for i in range(0, w.points.size() - 1, 4):
					if pos.distance_to(w.points[i]) < w.width * 0.7:
						weight += 50.0 # Bridge cost
						break
			
			astar.set_point_weight_scale(id, weight)
	
	# Connect grid neighbors
	for y in range(rows):
		for x in range(cols):
			var id = y * cols + x
			if x < cols - 1: astar.connect_points(id, id + 1)
			if y < rows - 1: astar.connect_points(id, id + cols)

	# 2. Trace Paths from Exits to Center
	var exits = [Vector2(250, 0), Vector2(250, 500), Vector2(0, 250), Vector2(500, 250)]
	var used_exits = [exits[0], exits[1]] # N-S default
	if is_major: used_exits.append(exits[2])
	
	var road_segments = [] # For lot generation: {start, end, type}
	
	for exit in used_exits:
		var start_id = astar.get_closest_point(exit)
		var end_id = astar.get_closest_point(center)
		var path_ids = astar.get_point_path(start_id, end_id)
		
		# If path crosses water, it will naturally try to find shortest crossing due to weights
		if path_ids.size() > 1:
			var smooth_path = _smooth_path(path_ids)
			bp.roads.append({"points": smooth_path, "type": "artery"})
			for i in range(smooth_path.size() - 1):
				road_segments.append({"start": smooth_path[i], "end": smooth_path[i+1], "type": "artery"})

	# 3. Branching Streets (Iterative Growth)
	var branch_count = 12 if is_major else 5
	var attempts = 0
	while bp.roads.size() < branch_count + used_exits.size() and attempts < 60:
		attempts += 1
		var parent_road = bp.roads.pick_random()
		if parent_road.points.size() < 2: continue
		
		var idx = rng.randi_range(0, parent_road.points.size() - 2)
		var p1 = parent_road.points[idx]
		var p2 = parent_road.points[idx+1]
		var start_pos = p1.lerp(p2, rng.randf())
		
		var angle_offset = PI/2 * (1 if rng.randf() > 0.5 else -1)
		var next_p = start_pos + (p2 - p1).normalized().rotated(angle_offset + rng.randf_range(-0.3, 0.3)) * 20
		
		# Simple raycast check for L-system branch validity (no water)
		var in_water = false
		for w in bp.water:
			for i in range(0, w.points.size()-1, 5):
				if next_p.distance_to(w.points[i]) < w.width * 0.6: in_water = true; break
		if in_water: continue
		
		# Trace Branch
		var branch_len = rng.randf_range(60, 160)
		var branch_path = [start_pos]
		var curr = start_pos
		var dir = (next_p - start_pos).normalized()
		
		for s_i in range(10): # 10 steps
			curr += dir * (branch_len / 10.0)
			# Bend
			dir = dir.rotated(rng.randf_range(-0.15, 0.15))
			
			# Constraints
			if curr.x < 20 or curr.x > 480 or curr.y < 20 or curr.y > 480: break
			
			# Water check for street tip
			var tip_wet = false
			for w in bp.water:
				for i in range(0, w.points.size()-1, 5): 
					if curr.distance_to(w.points[i]) < w.width * 0.6: tip_wet = true
			if tip_wet: break
			
			branch_path.append(curr)
		
		if branch_path.size() > 3:
			bp.roads.append({"points": branch_path, "type": "street"})
			for k in range(branch_path.size() - 1):
				road_segments.append({"start": branch_path[k], "end": branch_path[k+1], "type": "street"})

	# --- PHASE 3: LOTS (Frontage + Backyard Filling) ---
	# Fix: Added "Backyard" logic for density
	for seg in road_segments:
		var p1 = seg.start
		var p2 = seg.end
		var dir = (p2 - p1).normalized()
		var length = p1.distance_to(p2)
		var perp = Vector2(-dir.y, dir.x)
		
		var lot_w = rng.randf_range(10, 16)
		var t = 0.0
		while t < length - lot_w:
			var lot_pos = p1 + dir * (t + lot_w/2.0)
			
			# Try both sides
			for side in [1, -1]:
				if rng.randf() < 0.15: continue
				
				var lot_d = rng.randf_range(15, 25)
				var center_offset = perp * side * (lot_d/2.0 + 3.0)
				var l_center = lot_pos + center_offset
				var rect = Rect2i(l_center.x - lot_w/2, l_center.y - lot_d/2, int(lot_w), int(lot_d))
				
				if _is_lot_valid(rect, bp.roads, bp.parcels, bp.water):
					var type = "residential"
					var dist = l_center.distance_to(center)
					if dist < 60: type = "market"
					elif dist > 180: type = "farm"
					
					bp.parcels.append({"rect": rect, "type": type})
					
					# BACKYARD FILLER (Density Fix)
					if type == "residential" and rng.randf() < 0.7:
						var yard_d = rng.randf_range(10, 20)
						var y_offset = perp * side * (lot_d + yard_d/2.0 + 3.0) 
						var y_center = lot_pos + y_offset
						var y_rect = Rect2i(y_center.x - lot_w/2, y_center.y - yard_d/2, int(lot_w), int(yard_d))
						
						if _is_lot_valid(y_rect, bp.roads, bp.parcels, bp.water):
							bp.parcels.append({"rect": y_rect, "type": "garden"})

			t += lot_w + 2.0

	# --- PHASE 4: HULL ---
	if s.buildings.get("wall", 0) > 0:
		var points = PackedVector2Array()
		for p in bp.parcels:
			if p.type != "farm" and p.type != "garden":
				points.append(Vector2(p.rect.position))
				points.append(Vector2(p.rect.end))
		if points.size() > 3:
			bp.hull = Geometry2D.convex_hull(points)

	# Keep legacy districts for UI/metadata
	bp.districts.append({"pos": center, "type": "keep", "radius": 40.0})
	return bp

static func _smooth_path(points: PackedVector2Array) -> PackedVector2Array:
	return points

static func _is_lot_valid(rect: Rect2i, roads: Array, parcels: Array, water: Array = []) -> bool:
	var shrunk = rect.grow(-2) 
	var r_poly = [Vector2(rect.position), Vector2(rect.end.x, rect.position.y), 
				  Vector2(rect.end), Vector2(rect.position.x, rect.end.y)]
	
	# Check Parcels
	for p in parcels:
		if p.rect.intersects(shrunk): return false
	
	# Check Water
	for w in water:
		for i in range(0, w.points.size()-1, 2):
			if _dist_to_line(Vector2(rect.get_center()), w.points[i], w.points[min(i+1, w.points.size()-1)]) < w.width/2.0 + 2.0:
				return false

	# Check Roads (Intersecting road lines)
	for r in roads:
		for i in range(r.points.size() - 1):
			if Geometry2D.intersect_polyline_with_polygon([r.points[i], r.points[i+1]], r_poly).size() > 0:
				return false
				
	return true

static func get_blueprint_tile(bp: Dictionary, pos: Vector2, area_size: float = 0.0) -> String:
	# Priority 0: Water
	if bp.has("water"):
		for w in bp.water:
			for i in range(0, w.points.size()-1, 2):
				if _dist_to_line(pos, w.points[i], w.points[min(i+1, w.points.size()-1)]) < w.width/2.0:
					return "water"

	# Priority 1: Roads
	for road in bp.roads:
		for i in range(road.points.size() - 1):
			if _dist_to_line(pos, road.points[i], road.points[i+1]) < 3.0:
				return "road"
	
	# Priority 2: Parcels
	for p in bp.parcels:
		if p.rect.has_point(Vector2i(pos)):
			if p.type == "farm": return "farms"
			return "urban_block"
			
	# Priority 3: Walls
	if bp.hull.size() > 0:
		for i in range(bp.hull.size()):
			if _dist_to_line(pos, bp.hull[i], bp.hull[(i+1)%bp.hull.size()]) < 4.0:
				return "walls_outer"
				
	return ""

static func _is_pos_inside_hull(p: Vector2, poly: Array) -> bool:
	return Geometry2D.is_point_in_polygon(p, poly)

static func _dist_to_line(p: Vector2, a: Vector2, b: Vector2) -> float:
	var ab = b - a
	var ap = p - a
	var t = ap.dot(ab) / ab.length_squared()
	t = clamp(t, 0.0, 1.0)
	var closest = a + t * ab
	return p.distance_to(closest)

static func _rect_intersects_segment(rect: Rect2i, p1: Vector2, p2: Vector2, width: float) -> bool:
	var seg_rect = Rect2(min(p1.x, p2.x), min(p1.y, p2.y), abs(p1.x - p2.x), abs(p1.y - p2.y)).grow(width)
	if not seg_rect.intersects(Rect2(rect)): return false
	var r_poly = [Vector2(rect.position), Vector2(rect.end.x, rect.position.y), Vector2(rect.end), Vector2(rect.position.x, rect.end.y)]
	return Geometry2D.intersect_polyline_with_polygon([p1, p2], r_poly).size() > 0

func activate(s: GDSettlement, w_pos: Vector2i, w_seed: int):
	active = true
	city_name = s.name
	world_pos = w_pos
	blueprint = build_blueprint(s, w_seed)
	_generate_tactical_from_blueprint(s)

func generate_test_city():
	active = true
	city_name = "Test City"
	var s = GDSettlement.new()
	s.name = "Test City"
	s.type = "city"
	s.pos = Vector2i(100, 100)
	s.buildings["wall"] = 2
	
	blueprint = build_blueprint(s, GameState.world_seed)
	_generate_tactical_from_blueprint(s)

func _generate_winding_path(start: Vector2, end: Vector2, rng: RandomNumberGenerator) -> Array:
	var path = []
	var segments = 10
	for i in range(segments + 1):
		var t = i / float(segments)
		var jitter = Vector2(rng.randf_range(-20, 20), rng.randf_range(-20, 20)) if i > 0 and i < segments else Vector2.ZERO
		path.append(start.lerp(end, t) + jitter)
	return path

func _generate_tactical_from_blueprint(s: GDSettlement):
	width = 500
	height = 500
	
	# Calculate offset: (world_pos - s.pos) * 500
	bp_offset = Vector2(world_pos - s.pos) * 500.0
	
	# Reset Layers
	layers.terrain = []
	layers.zoning = []
	layers.structure = []
	layers.pathing = []
	layers.entities = {}
	
	grid = []
	fog_of_war = []
	buildings = []
	wall_segments = []
	gates = []
	structure_health = {}
	
	# Initial ground noise (World-relative seed for continuity)
	var ground_noise = FastNoiseLite.new()
	ground_noise.seed = blueprint.seed + (world_pos.x * 3) + (world_pos.y * 7)
	ground_noise.frequency = 0.05
	
	for y in range(height):
		var row_t = []
		var row_z = []
		var row_s = []
		var row_p = []
		var row_f = []
		var row_g = []
		for x in range(width):
			var n = ground_noise.get_noise_2d(x,y)
			var t = "\"" if n > 0.4 else "f" if n > 0.2 else "."
			row_t.append(t)
			row_z.append("")
			row_s.append("")
			row_p.append("")
			row_f.append(false)
			row_g.append(t)
		layers.terrain.append(row_t)
		layers.zoning.append(row_z)
		layers.structure.append(row_s)
		layers.pathing.append(row_p)
		grid.append(row_g)
		fog_of_war.append(row_f)

	# Water Logic (River continuity)
	for w in blueprint.water:
		var p_start = w.points[0] - bp_offset
		# Draw segment by segment
		for i in range(w.points.size() - 1):
			var p1 = w.points[i] - bp_offset
			var p2 = w.points[i+1] - bp_offset
			_draw_smooth_water(p1, p2, int(w.width / 2.0))

	# 1. Parcels
	if blueprint.has("parcels"):
		for p in blueprint.parcels:
			var b_rect = p.rect
			var l_rect = Rect2i(Vector2(b_rect.position) - bp_offset, b_rect.size)
			var view_rect = Rect2i(0, 0, 500, 500)
			
			if l_rect.intersects(view_rect):
				var terrain_type = "\""
				if p.type == "farm": terrain_type = "f"
				elif p.type == "garden": terrain_type = "\"" # Garden is just dirt/grass
				elif p.type in ["urban", "residential", "market"]: terrain_type = "urban_block"
				
				var draw_box = l_rect.intersection(view_rect)
				for py in range(draw_box.position.y, draw_box.end.y):
					for px in range(draw_box.position.x, draw_box.end.x):
						var gx = int(px + bp_offset.x)
						var gy = int(py + bp_offset.y)
						var is_border = (gx == b_rect.position.x or gx == b_rect.end.x - 1 or 
										gy == b_rect.position.y or gy == b_rect.end.y - 1)
						if not is_border:
							_set_l_cell("terrain", px, py, terrain_type)

	# 2. Roads
	for road in blueprint.roads:
		for i in range(road.points.size() - 1):
			var p1 = road.points[i] - bp_offset
			var p2 = road.points[i+1] - bp_offset
			_draw_smooth_road(p1, p2, 4 if road.type == "artery" else 2)

	# 3. Districts (Zoning & Terrain bases)
	for d in blueprint.districts:
		var l_pos = d.pos - bp_offset
		var dist_to_view = (l_pos - Vector2(250, 250)).length()
		if dist_to_view < d.radius + 100:
			_draw_circle_l("zoning", Vector2i(l_pos), int(d.radius), d.type)
			if d.type == "docks":
				_draw_circle_l("terrain", Vector2i(l_pos), int(d.radius), "docks")
			elif d.type in ["keep", "market", "industrial"]:
				_draw_circle_l("terrain", Vector2i(l_pos), int(d.radius), "urban_block")

	# 4. Walls
	if blueprint.hull.size() > 0:
		for i in range(blueprint.hull.size()):
			var p1 = blueprint.hull[i] - bp_offset
			var p2 = blueprint.hull[(i+1)%blueprint.hull.size()] - bp_offset
			_draw_wall_line(Vector2i(p1), Vector2i(p2))

	# 5. Buildings
	for p in blueprint.parcels:
		var l_rect = Rect2i(Vector2(p.rect.position) - bp_offset, p.rect.size)
		# Viewport check
		if l_rect.intersects(Rect2i(0, 0, width, height)):
			if p.type in ["residential", "market", "industrial"]:
				var b_rect = l_rect.grow(-2)
				if b_rect.size.x >= 3 and b_rect.size.y >= 3:
					var b_type = "B"
					if p.type == "market": b_type = "M"
					elif p.type == "industrial": b_type = "S"
					_draw_building(b_rect, b_type)
	
	_sync_layers_to_grid()
	
	# Player Placement logic
	if world_pos == s.pos and player_pos == Vector2i(250, 250):
		if blueprint.districts.size() > 0:
			player_pos = Vector2i(blueprint.districts[0].pos - bp_offset) + Vector2i(0, 5)
	
	tile_under_player = _get_cell(player_pos.x, player_pos.y)
	_set_cell(player_pos.x, player_pos.y, "@")
	active = true



func _draw_circle_l(layer: String, center: Vector2i, radius: int, val: String):
	for y in range(center.y - radius, center.y + radius + 1):
		for x in range(center.x - radius, center.x + radius + 1):
			if x < 0 or x >= width or y < 0 or y >= height: continue
			if Vector2(x - center.x, y - center.y).length() <= radius:
				layers[layer][y][x] = val

func _set_l_cell(layer: String, x: int, y: int, val: String):
	if x >= 0 and x < width and y >= 0 and y < height:
		layers[layer][y][x] = val

func _get_l_cell(layer: String, x: int, y: int) -> String:
	if x >= 0 and x < width and y >= 0 and y < height:
		return layers[layer][y][x]
	return ""

func _sync_layers_to_grid():
	for y in range(height):
		for x in range(width):
			var final = layers.terrain[y][x]
			if layers.pathing[y][x] != "": final = layers.pathing[y][x]
			if layers.structure[y][x] != "": final = layers.structure[y][x]
			grid[y][x] = final

func _draw_smooth_water(p1: Vector2, p2: Vector2, radius: int):
	var dist = p1.distance_to(p2)
	var steps = int(dist)
	if steps == 0: return
	for i in range(steps + 1):
		var gp = Vector2i(p1.lerp(p2, i / float(steps)))
		for dy in range(-radius, radius + 1):
			for dx in range(-radius, radius + 1):
				if Vector2(dx, dy).length() <= radius:
					_set_l_cell("terrain", gp.x + dx, gp.y + dy, "~")

func _draw_smooth_road(p1: Vector2, p2: Vector2, thick: int):
	var dist = p1.distance_to(p2)
	var steps = int(dist * 2)
	for i in range(steps):
		var gp = Vector2i(p1.lerp(p2, i / float(steps)))
		for dy in range(-thick, thick+1):
			for dx in range(-thick, thick+1):
				if Vector2(dx, dy).length() <= thick:
					var tx = gp.x + dx
					var ty = gp.y + dy
					if _get_l_cell("terrain", tx, ty) == "~":
						_set_l_cell("pathing", tx, ty, "=") # Bridge over water
					else:
						_set_l_cell("pathing", tx, ty, "+")

func _draw_wall_line(p1: Vector2i, p2: Vector2i):
	var d = p1.distance_to(p2)  # Vector2i has distance_to
	var steps = int(d * 2)
	for i in range(steps):
		var t = i / float(steps)
		# Use Vector2 for lerp as Vector2i doesn't have it, but avoid unnecessary conversions
		var p = Vector2(p1).lerp(Vector2(p2), t)
		_set_wall(int(p.x), int(p.y))

func _set_wall(x, y):
	if x < 0 or x >= width or y < 0 or y >= height: return
	if _get_cell(x, y) == "~": return
	_set_l_cell("structure", x, y, "#")
	var pos = Vector2i(x, y)
	wall_segments.append(pos)
	structure_health[pos] = 200.0

func _draw_building(rect: Rect2i, type: String):
	buildings.append(rect)
	for y in range(rect.position.y, rect.end.y):
		for x in range(rect.position.x, rect.end.x):
			if x < 0 or x >= width or y < 0 or y >= height: continue
			var is_edge = (x == rect.position.x or x == rect.end.x-1 or y == rect.position.y or y == rect.end.y-1)
			
			if is_edge:
				# Door Logic: If this wall faces a road/path, make it a door
				var is_door = false
				# Check neighbors for pathing
				var neighbors = [Vector2i(0, -1), Vector2i(0, 1), Vector2i(-1, 0), Vector2i(1, 0)]
				for n in neighbors:
					var nx = x + n.x
					var ny = y + n.y
					if _get_l_cell("pathing", nx, ny) in ["+", "="]:
						is_door = true
						break
				
				if is_door and (x + y) % 2 == 0: # Prevent every single tile being a door
					_set_l_cell("structure", x, y, "+") # + = Door/Floor
				else:
					_set_l_cell("structure", x, y, type)
			else: 
				_set_l_cell("pathing", x, y, "+") # Floor

func _set_cell(x, y, char):
	if x >= 0 and x < width and y >= 0 and y < height:
		grid[y][x] = char

func _get_cell(x, y) -> String:
	if x >= 0 and x < width and y >= 0 and y < height:
		return grid[y][x]
	return ""

func move_player(dir: Vector2i):
	var new_pos = player_pos + dir
	
	# Tile Boundary Transition (Multi-Tile City Support)
	if new_pos.x < 0 or new_pos.x >= 500 or new_pos.y < 0 or new_pos.y >= 500:
		var target_world = world_pos + (Vector2i(1, 0) if new_pos.x >= 500 else Vector2i(-1, 0) if new_pos.x < 0 else Vector2i.ZERO)
		target_world += (Vector2i(0, 1) if new_pos.y >= 500 else Vector2i(0, -1) if new_pos.y < 0 else Vector2i.ZERO)
		
		# Check if target world tile is part of this city's footprint
		var is_footprint = false
		var target_s = null
		for setl in GameState.settlements.values():
			if setl.footprint.has_point(target_world):
				is_footprint = true
				target_s = setl
				break
				
		if is_footprint:
			world_pos = target_world
			GameState.player.pos = target_world
			player_pos.x = posmod(new_pos.x, 500)
			player_pos.y = posmod(new_pos.y, 500)
			activate(target_s, target_world, GameState.world_seed)
			return

	var target_cell = _get_cell(new_pos.x, new_pos.y)
	if target_cell in ["#", "B", "K", "S", "M", "H", "~", "T", "X", "C"]:
		return 
	
	_set_cell(player_pos.x, player_pos.y, tile_under_player)
	player_pos = new_pos
	tile_under_player = _get_cell(player_pos.x, player_pos.y)
	_set_cell(player_pos.x, player_pos.y, "@")

func handle_input(event: InputEvent):
	if not active: return
	if not event is InputEventKey or not event.pressed: return
	var move = Vector2i.ZERO
	match event.keycode:
		KEY_W, KEY_UP: move.y = -1
		KEY_S, KEY_DOWN: move.y = 1
		KEY_A, KEY_LEFT: move.x = -1
		KEY_D, KEY_RIGHT: move.x = 1
		KEY_T:
			# Allow toggling out of City Mode directly to World/Fast Travel
			active = false
			GameState.travel_mode = GameState.TravelMode.FAST
			var main = GameState.get_node("/root/Main") # Assuming Main is root or singleton access
			if main: main.state = "overworld"
			return
		KEY_ESCAPE:
			active = false
			return 
	if move != Vector2i.ZERO:
		move_player(move)
		GameState.emit_signal("map_updated")
