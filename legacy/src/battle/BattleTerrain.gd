extends RefCounted
class_name BattleTerrain

# Procedural terrain generation for tactical battles
# Handles biomes, geology interpolation, rivers, roads, settlements, and fauna

const FaunaData = preload("res://src/data/FaunaData.gd")
const FloraData = preload("res://src/data/FloraData.gd")

const MAP_W = 500
const MAP_H = 500
const CHUNK_SIZE = 50

# -----------------------------
# Generation
# -----------------------------

func generate_initial_area(grid: Array, generated_chunks: Dictionary, structural_cache: Dictionary, generate_chunk_callback: Callable):
	"""Pre-generate starting area around map center"""
	var center = Vector2i(MAP_W/2, MAP_H/2)
	var start_cx = (center.x - 100) / CHUNK_SIZE
	var end_cx = (center.x + 100) / CHUNK_SIZE
	var start_cy = (center.y - 100) / CHUNK_SIZE
	var end_cy = (center.y + 100) / CHUNK_SIZE
	
	for cy in range(start_cy, end_cy + 1):
		for cx in range(start_cx, end_cx + 1):
			generate_chunk_callback.call(Vector2i(cx, cy))

func generate_chunk(chunk_pos: Vector2i, grid: Array, generated_chunks: Dictionary, structural_cache: Dictionary, enemy_ref = null):
	"""Generate a single chunk with procedural terrain"""
	if chunk_pos in generated_chunks:
		return
	if chunk_pos.x < 0 or chunk_pos.x >= (MAP_W/CHUNK_SIZE) or chunk_pos.y < 0 or chunk_pos.y >= (MAP_H/CHUNK_SIZE):
		return
	
	generated_chunks[chunk_pos] = true
	var gs = GameState
	var p_pos = gs.player.pos
	var l_off = gs.local_offset
	
	var world_tile = gs.grid[p_pos.y][p_pos.x] # Default world tile
	var local_rng = RandomNumberGenerator.new()
	var noise = FastNoiseLite.new()
	noise.seed = (p_pos.x * 73856093) ^ (p_pos.y * 19349663)
	noise.frequency = 0.08

	# Sample geology from neighborhood
	var neighborhood = {} 
	var default_geo = {"elevation": 0.5, "temp": 0.5, "rain": 0.5}
	for dy in range(-1, 2):
		for dx in range(-1, 2):
			var w_pos_n = p_pos + Vector2i(dx, dy)
			if w_pos_n.x < 0 or w_pos_n.x >= gs.width or w_pos_n.y < 0 or w_pos_n.y >= gs.height:
				w_pos_n = p_pos
			var geo = gs.geology.get(w_pos_n, default_geo)
			neighborhood[Vector2i(dx, dy)] = geo

	var center_wx = l_off.x / gs.WORLD_TILE_SIZE - 0.5
	var center_wy = l_off.y / gs.WORLD_TILE_SIZE - 0.5
	
	var is_river = (world_tile == "~" or world_tile == "≈")
	var has_road = (world_tile == "=" or world_tile == "/" or world_tile == "\\")
	
	# Road/River Continuity Neighbors
	var neighbors_road = [false, false, false, false] # N, E, S, W
	var neighbors_river = [false, false, false, false]
	for idx in range(4):
		var dir = [Vector2i.UP, Vector2i.RIGHT, Vector2i.DOWN, Vector2i.LEFT][idx]
		var n_wpos = p_pos + dir
		if n_wpos.x >= 0 and n_wpos.x < gs.width and n_wpos.y >= 0 and n_wpos.y < gs.height:
			var n_char = gs.grid[n_wpos.y][n_wpos.x]
			if n_char in ["=", "/", "\\"]: neighbors_road[idx] = true
			if n_char in ["~", "≈"]: neighbors_river[idx] = true

	# Generate each tile in chunk
	for ly in range(chunk_pos.y * CHUNK_SIZE, (chunk_pos.y + 1) * CHUNK_SIZE):
		for lx in range(chunk_pos.x * CHUNK_SIZE, (chunk_pos.x + 1) * CHUNK_SIZE):
			if lx < 0 or lx >= MAP_W or ly < 0 or ly >= MAP_H:
				continue
			
			var m_off_x = (lx - (MAP_W/2.0)) * gs.METERS_PER_LOCAL_TILE
			var m_off_y = (ly - (MAP_H/2.0)) * gs.METERS_PER_LOCAL_TILE
			
			var wv_x = center_wx + (m_off_x / gs.WORLD_TILE_SIZE)
			var wv_y = center_wy + (m_off_y / gs.WORLD_TILE_SIZE)
			
			var interp_e = _interp_neighborhood(neighborhood, "elevation", wv_x, wv_y)
			var interp_t = _interp_neighborhood(neighborhood, "temp", wv_x, wv_y)
			var interp_r = _interp_neighborhood(neighborhood, "rain", wv_x, wv_y)
			
			# Simulator Flat Map Override
			var is_sim = false
			if enemy_ref is Dictionary and enemy_ref.get("name") == "Simulator Rivals":
				is_sim = true
			elif enemy_ref != null and "name" in enemy_ref and enemy_ref.name == "Simulator Rivals":
				is_sim = true
				
			if is_sim:
				interp_e = 0.5
				interp_r = 0.4
				interp_t = 0.5
			
			var abs_x = p_pos.x * gs.WORLD_TILE_SIZE + l_off.x + m_off_x
			var abs_y = p_pos.y * gs.WORLD_TILE_SIZE + l_off.y + m_off_y
			var detail = noise.get_noise_2d(abs_x, abs_y) * 0.05
			
			if is_sim:
				detail = 0.0 # Perfectly flat for sim
			
			var final_e = interp_e + detail
			
			# Base terrain from elevation
			var tile = "."
			var veg_roll = local_rng.randf()
			
			if final_e < 0.35: tile = "~"
			elif final_e > 0.65: tile = "^"
			elif final_e > 0.50: tile = "o"
			else:
				if interp_r > 0.7 and interp_t > 0.6 and veg_roll > 0.7: tile = "&" 
				elif interp_r > 0.5 and veg_roll > 0.8: tile = "T" 
				elif interp_r < 0.2 and interp_t > 0.7: tile = "\"" 
				elif interp_t < 0.3: tile = "*" 
			
			# Determine biome
			var biome = "plains"
			if interp_r > 0.7 and interp_t > 0.6: biome = "jungle"
			elif interp_r > 0.5: biome = "forest"
			elif interp_r < 0.2 and interp_t > 0.7: biome = "desert"
			elif interp_t < 0.3: biome = "plains"
			
			var l_norm = Vector2(wv_x * 2.0, wv_y * 2.0)
			
			# River continuity rendering
			if is_river:
				var river_width = 0.3
				var in_river = false
				if l_norm.length() < river_width: in_river = true
				if neighbors_river[0] and wv_x > -river_width and wv_x < river_width and wv_y < 0: in_river = true
				if neighbors_river[1] and wv_y > -river_width and wv_y < river_width and wv_x > 0: in_river = true
				if neighbors_river[2] and wv_x > -river_width and wv_x < river_width and wv_y > 0: in_river = true
				if neighbors_river[3] and wv_y > -river_width and wv_y < river_width and wv_x < 0: in_river = true
				if in_river: tile = "~"
			
			# Road continuity rendering
			if has_road and tile != "~":
				var road_width = 0.08
				var in_road = false
				if l_norm.length() < road_width: in_road = true
				if neighbors_road[0] and wv_x > -road_width and wv_x < road_width and wv_y < 0: in_road = true
				if neighbors_road[1] and wv_y > -road_width and wv_y < road_width and wv_x > 0: in_road = true
				if neighbors_road[2] and wv_x > -road_width and wv_x < road_width and wv_y > 0: in_road = true
				if neighbors_road[3] and wv_y > -road_width and wv_y < road_width and wv_x < 0: in_road = true
				if in_road: tile = "+"
			
			# Flora placement (deterministic based on position)
			var abs_cell_x = int(abs_x / 2.0)
			var abs_cell_y = int(abs_y / 2.0)
			var cell_hash = (abs_cell_x * 73856093) ^ (abs_cell_y * 19349663)
			var roll = (abs(cell_hash) % 10000) / 10000.0
			
			if tile in [".", "o", "t", "\""]:
				var flora_list = FloraData.get_flora_for_biome(biome)
				for f in flora_list:
					if roll < f["chance"]:
						tile = f["symbol"]
						break
			
			# Resource deposits
			var resource_type = gs.resources.get(p_pos, "")
			if resource_type != "" and tile in [".", "o", "^", "\""]:
				var res_roll = (abs(cell_hash ^ 999) % 1000) / 1000.0
				if res_roll < 0.01:
					tile = resource_type.substr(0, 1).to_upper()
			
			# Settlement rendering
			for ny in range(-1, 2):
				for nx in range(-1, 2):
					var s_w_pos = p_pos + Vector2i(nx, ny)
					if gs.settlements.has(s_w_pos):
						var s = gs.settlements[s_w_pos]
						var s_cx = s_w_pos.x * 1000.0 + 500.0
						var s_cy = s_w_pos.y * 1000.0 + 500.0
						var sdx = abs_x - s_cx
						var sdy = abs_y - s_cy
						var s_dist_c = Vector2(sdx, sdy).length()
						var settlement_radius = clamp(50.0 + (s.population / 1000.0) * 40.0, 50.0, 480.0)
						
						if s_dist_c < settlement_radius + 20.0:
							var is_major = s.type in ["town", "city", "metropolis", "castle"]
							var wall_lvl = s.buildings.get("wall", 0) 
							if is_major and wall_lvl == 0 and s.tier >= 2: wall_lvl = 1
							
							# Walls
							if wall_lvl > 0:
								var wall_thick = 2.0 + (wall_lvl * 1.5)
								if (abs(abs(sdx) - settlement_radius) < wall_thick or abs(abs(sdy) - settlement_radius) < wall_thick) and s_dist_c < settlement_radius + 5:
									if not (abs(sdx) < 8.0 or abs(sdy) < 8.0):
										tile = "#" if wall_lvl < 3 else "H"
							
							# Streets
							var street_w = 2.5 + (s.tier * 0.5) 
							if abs(sdx) < street_w or abs(sdy) < street_w:
								if s_dist_c < settlement_radius + 10.0: tile = "+"
							
							# Keep/Center
							if s_dist_c < 12.0 + (s.tier * 4.0):
								if s.buildings.get("keep", 0) > 0 or is_major: tile = "K"
								else: tile = "o"
							
							# Buildings
							if tile == "." and s_dist_c < settlement_radius:
								var b_h = (int(abs_x/20.0) * 73856093) ^ (int(abs_y/20.0) * 19349663)
								var b_lx = fmod(abs_x, 20.0)
								var b_ly = fmod(abs_y, 20.0)
								var d_f = 1.0 - (s_dist_c / settlement_radius)
								var occ = 10 + (d_f * 40.0)
								if (abs(b_h) % 100) < occ:
									if b_lx > 6.0 and b_lx < 14.0 and b_ly > 6.0 and b_ly < 14.0:
										tile = "B" if is_major else "#" 
			
			grid[ly][lx] = tile
			
			# Cache structural targets for spatial queries
			if tile in ["#", "H", "G", "K", "B"]:
				if not structural_cache.has(tile):
					structural_cache[tile] = []
				structural_cache[tile].append(Vector2i(lx, ly))

	# Populate fauna
	var biome_at_center = "plains"
	var fauna_list = []
	var fauna_table = FaunaData.get_fauna_table()
	if fauna_table.has(biome_at_center):
		fauna_list = fauna_table.get(biome_at_center, [])
	
	if not fauna_list.is_empty():
		var s_rng = RandomNumberGenerator.new()
		s_rng.seed = (chunk_pos.x * 73856093) ^ (chunk_pos.y * 19349663)
		for f in fauna_list:
			if s_rng.randf() < f["chance"] * 0.1:
				var num = s_rng.randi_range(f["herd_range"][0], f["herd_range"][1])
				for i in range(num):
					var gx = chunk_pos.x * CHUNK_SIZE + s_rng.randi_range(0, CHUNK_SIZE-1)
					var gy = chunk_pos.y * CHUNK_SIZE + s_rng.randi_range(0, CHUNK_SIZE-1)
					if gx >= 0 and gx < MAP_W and gy >= 0 and gy < MAP_H:
						if grid[gy][gx] in [".", "o", "t", "\"", "*"]:
							grid[gy][gx] = f["symbol"]

# -----------------------------
# Utilities
# -----------------------------

func _interp_neighborhood(nb: Dictionary, key: String, wx: float, wy: float) -> float:
	"""Bilinear interpolation for geology values"""
	# Determine which 4 tiles to interpolate between
	var x0 = -1 if wx < 0 else 0
	var x1 = 0 if wx < 0 else 1
	var y0 = -1 if wy < 0 else 0
	var y1 = 0 if wy < 0 else 1
	
	# Local weights 0 to 1 between the two tiles
	var tx = wx + 1.0 if wx < 0 else wx
	var ty = wy + 1.0 if wy < 0 else wy
	
	var v00 = nb[Vector2i(x0, y0)].get(key, 0.5)
	var v10 = nb[Vector2i(x1, y0)].get(key, 0.5)
	var v01 = nb[Vector2i(x0, y1)].get(key, 0.5)
	var v11 = nb[Vector2i(x1, y1)].get(key, 0.5)
	
	var top = lerp(v00, v10, tx)
	var bot = lerp(v01, v11, tx)
	return lerp(top, bot, ty)
