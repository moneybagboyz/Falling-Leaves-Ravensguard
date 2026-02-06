extends Node

signal settlement_entered(settlement)

var active = false
var grid = [] # 2D array of region data
var width = 500
var height = 500
var player_pos = Vector2i.ZERO # Position in Region grid
var world_origin = Vector2i.ZERO # The top-left world tile this region covers
var minor_pois = {} # Vector2i -> Dict: {type: "shrine", name: "Ancient Altar", color: "gold", symbol: "π"}

# Scale: 1 World Tile = 50x50 Region Tiles (10m per tile)
const REGION_SCALE = 50 

# Cache for persistence
var cached_origin = Vector2i(-999, -999)
var cached_grid = []
var cached_pois = {}

func activate(world_pos: Vector2i):
	active = true
	var new_origin = world_pos - Vector2i(5, 5) # Center on player area
	
	if new_origin != cached_origin:
		world_origin = new_origin
		_generate_region()
		cached_origin = world_origin
		cached_grid = grid
		cached_pois = minor_pois
	else:
		world_origin = cached_origin
		grid = cached_grid
		minor_pois = cached_pois

	# Start at the center of the world tile we entered from
	# Each world tile is 50x50 region tiles. The entered tile starts at index 250, 250
	# within the 500x500 region grid.
	player_pos = Vector2i(275, 275) 
	
	# Update GameState smooth coordinates
	_sync_gamestate_pos()
	
	GameState.travel_mode = GameState.TravelMode.REGION
	GameState.region_ctrl = self # Ensure reference is active

func _sync_gamestate_pos():
	# Update the integer index GameState expects
	var wx = world_origin.x + (player_pos.x / 50)
	var wy = world_origin.y + (player_pos.y / 50)
	GameState.player.pos = Vector2i(wx, wy)
	
	# Update smooth local offset (meters within the current world tile)
	# One region tile = 10 meters. 
	GameState.local_offset = Vector2(
		(player_pos.x % 50) * 10.0 + 5.0, # +5 to center in tile
		(player_pos.y % 50) * 10.0 + 5.0
	)

func _generate_region():
	var gs = GameState
	grid = []
	minor_pois = {}
	
	# Pass 1: Initialize Grid with Base Terrain (Noise-Driven) (FAST)
	var noise = FastNoiseLite.new()
	noise.seed = (world_origin.x * 1000) + world_origin.y + GameState.world_seed
	noise.frequency = 0.1
	
	for y in range(height):
		var row = []
		for x in range(width):
			var wx = int(world_origin.x + (x / REGION_SCALE))
			var wy = int(world_origin.y + (y / REGION_SCALE))
			
			var char_type = gs.grid[wy][wx] if wy >= 0 and wy < gs.height and wx >= 0 and wx < gs.width else "~"
			var base_tile = gs.get_tile_type(Vector2i(wx, wy))
			var final_tile = base_tile
			
			# Noise Variation
			var n_val = noise.get_noise_2d(x, y)
			if base_tile == "plains" and n_val > 0.4:
				final_tile = "forest"
			elif base_tile == "forest" and n_val < -0.4:
				final_tile = "plains"
			elif base_tile == "water" and n_val > 0.6:
				final_tile = "plains"
				
			row.append(final_tile)
		grid.append(row)
		
	# Pass 2: Paint City Features (Optimized Blueprint Rendering)
	var check_range = 3 
	for sy in range(world_origin.y - check_range, world_origin.y + 10 + check_range):
		for sx in range(world_origin.x - check_range, world_origin.x + 10 + check_range):
			var wp = Vector2i(sx, sy)
			if gs.settlements.has(wp):
				var bp = CityController.build_blueprint(gs.settlements[wp], gs.world_seed)
				_paint_blueprint_on_grid(bp, wp) # Helper function to paint efficiently

	# Pass 3: Post-Processing (POIs, Rivers, Bridges)
	var poi_rng = RandomNumberGenerator.new()
	poi_rng.seed = (world_origin.x * 777) + (world_origin.y * 333) + GameState.world_seed
	
	for y in range(height):
		for x in range(width):
			var wx = int(world_origin.x + (x / REGION_SCALE))
			var wy = int(world_origin.y + (y / REGION_SCALE))
			var l_x = x % REGION_SCALE
			var l_y = y % REGION_SCALE
			
			# Reuse world grid lookup
			var char_type = gs.grid[wy][wx] if wy >= 0 and wy < gs.height and wx >= 0 and wx < gs.width else "~"
			var base_tile = grid[y][x] # Current tile from Pass 1 & 2
			
			# RIVERS & BRIDGES (Continuity)
			# Only override natural terrain, don't pave over cities with rivers unless it's a bridge
			var is_urban = base_tile in ["keep", "market", "industrial", "slum", "residential", "urban", "walls_outer", "road", "farms"]
			
			if char_type in ["~", "≈", "/", "\\", "="]:
				var neighbors = [] # [N, E, S, W] checks... (Simulating connectivity)
				# Simplified neighbor check for speed - check only if center of tile
				var in_path = false
				
				# NARROW PATHS logic...
				var path_range = [24, 25]
				var check_path = l_x in path_range or l_y in path_range
				
				if check_path:
					for dir in [Vector2i.UP, Vector2i.RIGHT, Vector2i.DOWN, Vector2i.LEFT]:
						var n_pos = Vector2i(wx, wy) + dir
						var n_char = "?"
						if n_pos.y >= 0 and n_pos.y < gs.height and n_pos.x >= 0 and n_pos.x < gs.width:
							n_char = gs.grid[n_pos.y][n_pos.x]
						neighbors.append(n_char == char_type or (char_type in ["~", "≈"] and n_char in ["~", "≈"]) or (char_type in ["=", "/", "\\"] and n_char in ["=", "/", "\\"]))
					
					if neighbors[0] and l_x in path_range and l_y < 25: in_path = true
					if neighbors[1] and l_y in path_range and l_x > 25: in_path = true
					if neighbors[2] and l_x in path_range and l_y > 25: in_path = true
					if neighbors[3] and l_y in path_range and l_x < 25: in_path = true
					if l_x in path_range and l_y in path_range: in_path = true

				if in_path:
					if char_type in ["~", "≈"]: 
						if not is_urban: grid[y][x] = "water" # Only flood if not urban (unless we add bridge logic here)
					else: 
						grid[y][x] = "road"
			
			# Simple Bridge Logic
			if grid[y][x] == "road" and (base_tile == "water" or char_type == "~"):
				grid[y][x] = "bridge"

			# MINOR POIs
			if not is_urban and l_x == 5 and l_y == 5 and poi_rng.randf() < 0.15:
				var p_pos = Vector2i(x, y)
				var poi_type = poi_rng.randi_range(0, 3)
				match poi_type:
					0: minor_pois[p_pos] = {"type": "shrine", "name": "Ancient Shrine", "symbol": "Y", "color": "gold"}
					1: minor_pois[p_pos] = {"type": "campsite", "name": "Abandoned Camp", "symbol": "c", "color": "peru"}
					2: minor_pois[p_pos] = {"type": "grave", "name": "Lonesome Grave", "symbol": "t", "color": "white"}
					3: minor_pois[p_pos] = {"type": "ruin", "name": "Crumbling Wall", "symbol": "z", "color": "dim_gray"}

func _paint_blueprint_on_grid(bp: Dictionary, city_pos: Vector2i):
	# Center of the city tile in Region Coords (50 region units per world unit)
	# city_pos (10, 10) -> Region Start (500, 500) -> Center (525, 525)?
	# World origin of this RegionController is 'world_origin'
	# Relative world coords:
	var rel_world_x = city_pos.x - world_origin.x
	var rel_world_y = city_pos.y - world_origin.y
	
	# Base pixel position of the city center relative to current grid (0,0)
	# City center is at local (250, 250) meters inside its tile.
	# 1 world tile = 50 region tiles.
	# Region tile (0,0) = World (x, y) top-left.
	# City Center in Region Grid:
	# city_center_x = (rel_world_x * 50) + 25 
	# city_center_y = (rel_world_y * 50) + 25
	
	var cx = (rel_world_x * 50) + 25
	var cy = (rel_world_y * 50) + 25
	var center = Vector2(cx, cy)
	
	# 1. Paint Roads (Arteries)
	if bp.has("roads"):
		for road in bp.roads:
			# Road points are in City Meters (0-500).
			# We need to scale them to Region Grid (0-50 for a full tile).
			# 1 Region Grid Unit = 10 meters.
			# So divide city coords by 10.
			
			for i in range(road.points.size() - 1):
				var p1 = (road.points[i] / 10.0) + Vector2(rel_world_x * 50, rel_world_y * 50)
				var p2 = (road.points[i+1] / 10.0) + Vector2(rel_world_x * 50, rel_world_y * 50)
				
				# Determine relative position to grid origin
				p1 -= Vector2(0,0) # Since grid starts at 0,0 relative to world_origin
				p2 -= Vector2(0,0)
				
				# Draw line on grid
				# Verify points are not null or NaN
				if p1.is_finite() and p2.is_finite():
					_draw_line_on_grid(p1, p2, "road")

	# 2. Paint Parcels (Buildings/Farms)
	if bp.has("parcels"):
		for p in bp.parcels:
			var type = "urban_block"
			if p.type == "farm": type = "farms"
			elif p.type == "garden": type = "garden"
			elif p.type in ["market", "industrial", "slum", "residential", "urban"]: type = "urban_block" # Simplified
			
			# Rect is in City Meters. Convert to Region Grid.
			var r_start = (Vector2(p.rect.position) / 10.0) + Vector2(rel_world_x * 50, rel_world_y * 50)
			var r_size = Vector2(p.rect.size) / 10.0
			var r_end = r_start + r_size
			
			# Clip to grid
			var x0 = max(0, int(r_start.x))
			var y0 = max(0, int(r_start.y))
			var x1 = min(width, int(r_end.x))
			var y1 = min(height, int(r_end.y))
			
			for y in range(y0, y1):
				for x in range(x0, x1):
					grid[y][x] = type
	
	# 3. Paint Walls
	if bp.has("hull") and bp.hull.size() > 0:
		for i in range(bp.hull.size()):
			var p1 = (bp.hull[i] / 10.0) + Vector2(rel_world_x * 50, rel_world_y * 50)
			var p2 = (bp.hull[(i+1)%bp.hull.size()] / 10.0) + Vector2(rel_world_x * 50, rel_world_y * 50)
			_draw_line_on_grid(p1, p2, "walls_outer")
			
	# 4. Paint Districts (Centers)
	if bp.has("districts"):
		for d in bp.districts:
			var d_pos = (d.pos / 10.0) + Vector2(rel_world_x * 50, rel_world_y * 50)
			var d_rad = d.radius / 10.0
			# Draw circle
			var cx_i = int(d_pos.x)
			var cy_i = int(d_pos.y)
			var rad_i = int(d_rad)
			
			for y in range(max(0, cy_i - rad_i), min(height, cy_i + rad_i + 1)):
				for x in range(max(0, cx_i - rad_i), min(width, cx_i + rad_i + 1)):
					if Vector2i(x, y).distance_to(Vector2i(int(d_pos.x), int(d_pos.y))) <= d_rad:
						if d.type == "keep": grid[y][x] = "keep"
						elif d.type == "market": grid[y][x] = "market"
						elif d.type == "docks": grid[y][x] = "docks"

func _draw_line_on_grid(p1: Vector2, p2: Vector2, type: String):
	var points = _get_line_points(p1, p2)
	for p in points:
		if p.x >= 0 and p.x < width and p.y >= 0 and p.y < height:
			grid[p.y][p.x] = type

func _get_line_points(from: Vector2, to: Vector2) -> Array:
	var arr = []
	var diff = to - from
	var steps = max(abs(diff.x), abs(diff.y))
	if steps == 0: return [Vector2i(from)]
	for i in range(steps + 1):
		arr.append(Vector2i(from.lerp(to, i / float(steps))))
	return arr

func handle_input(event: InputEvent):
	if not active: return
	if not event is InputEventKey or not event.pressed: return
	
	var move_dir = Vector2i.ZERO
	
	# Handle key input safely
	if event is InputEventKey:
		match event.keycode:
			KEY_W, KEY_UP: move_dir.y = -1
			KEY_S, KEY_DOWN: move_dir.y = 1
			KEY_A, KEY_LEFT: move_dir.x = -1
			KEY_D, KEY_RIGHT: move_dir.x = 1
			KEY_T: _toggle_travel_mode()
			KEY_E, KEY_ENTER, KEY_KP_ENTER: _try_enter_settlement()
			KEY_ESCAPE:
				_exit_to_overworld()
	
	# Fallback to actions if keypad/input map used
	if move_dir == Vector2i.ZERO:
		if event.is_action_pressed("ui_up"): move_dir.y = -1
		elif event.is_action_pressed("ui_down"): move_dir.y = 1
		elif event.is_action_pressed("ui_left"): move_dir.x = -1
		elif event.is_action_pressed("ui_right"): move_dir.x = 1
	
	if move_dir != Vector2i.ZERO:
		_move_player(move_dir)

func _move_player(dir: Vector2i):
	var new_pos = player_pos + dir
	if new_pos.x < 0 or new_pos.x >= width or new_pos.y < 0 or new_pos.y >= height:
		# Transition back to Overworld? Or page the region?
		_exit_to_overworld()
		return
		
	player_pos = new_pos
	_sync_gamestate_pos()
	
	# Discovery Logic
	if minor_pois.has(player_pos):
		var poi = minor_pois[player_pos]
		GameState.add_log("You have discovered: %s (%s)" % [poi.name, poi.type.capitalize()])

	GameState.advance_time() # Region movement takes time but less than world
	GameState.emit_signal("map_updated")

func _exit_to_overworld():
	active = false
	GameState.travel_mode = GameState.TravelMode.FAST

func _try_enter_settlement():
	var wx = world_origin.x + (player_pos.x / 50)
	var wy = world_origin.y + (player_pos.y / 50)
	var w_pos = Vector2i(wx, wy)
	
	if GameState.settlements.has(w_pos):
		var s = GameState.settlements[w_pos]
		active = false # Stop region updates
		emit_signal("settlement_entered", s)
	else:
		GameState.add_log("There is no settlement here to enter.")
	GameState.emit_signal("map_updated")

func _toggle_travel_mode():
	# Cycle back to Fast or forward to Local?
	# For simplicity, exit to Overworld Fast Mode
	_exit_to_overworld()
