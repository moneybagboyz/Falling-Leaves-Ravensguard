extends RefCounted
class_name BattlePhysics

# Spatial grid and movement system for tactical battles
# Handles collision detection, pathfinding, and spatial hashing

const MAP_W = 500
const MAP_H = 500
const CHUNK_SIZE = 50
const SPATIAL_BUCKET_SIZE = 10

var grid = [] # 2D array of chars
var generated_chunks: Dictionary = {} # chunk_pos -> bool
var structural_cache = {} # char -> Array of Vector2i (walls, doors, etc.)

# Spatial Hashing for fast unit queries
var spatial_grid = {} # Integer Key -> Array of GDUnit
var spatial_team_mask = {} # Integer Key -> int (Bitmask: 1=player, 2=enemy, 4=ally)

# Unit position lookup
var unit_lookup = {} # Vector2i -> GDUnit

# -----------------------------
# Grid Management
# -----------------------------

func initialize_grid():
	grid = []
	for y in range(MAP_H):
		var row = []
		row.resize(MAP_W)
		row.fill(" ") # Ungenerated tile
		grid.append(row)
	generated_chunks.clear()
	spatial_grid.clear()
	spatial_team_mask.clear()
	structural_cache.clear()
	unit_lookup.clear()

func ensure_chunk_at(pos: Vector2i, generator_callback: Callable):
	"""Ensure chunk at position is generated using provided callback"""
	var chunk_pos = Vector2i(pos.x / CHUNK_SIZE, pos.y / CHUNK_SIZE)
	if not generated_chunks.has(chunk_pos):
		generator_callback.call(chunk_pos)
		generated_chunks[chunk_pos] = true

func get_tile(x: int, y: int) -> String:
	if x < 0 or x >= MAP_W or y < 0 or y >= MAP_H:
		return " "
	return grid[y][x]

func is_in_bounds(pos: Vector2i) -> bool:
	return pos.x >= 0 and pos.x < MAP_W and pos.y >= 0 and pos.y < MAP_H

# -----------------------------
# Unit Registry
# -----------------------------

func register_unit(u):
	"""Add unit to spatial grid and lookup"""
	for offset in u.footprint:
		unit_lookup[u.pos + offset] = u
	update_unit_spatial(u)

func unregister_unit(u):
	"""Remove unit from spatial grid and lookup"""
	for offset in u.footprint:
		unit_lookup.erase(u.pos + offset)
	remove_unit_spatial(u)

func get_unit_at(pos: Vector2i):
	"""Get unit at position (if alive)"""
	if unit_lookup.has(pos):
		var u = unit_lookup[pos]
		if u.hp > 0 and not u.status.get("is_downed", false) and not u.status.get("is_dead", false):
			return u
	return null

# -----------------------------
# Spatial Grid (Optimization)
# -----------------------------

func update_unit_spatial(u):
	"""Add unit to spatial bucket for fast queries"""
	var bx = int(u.pos.x / SPATIAL_BUCKET_SIZE)
	var by = int(u.pos.y / SPATIAL_BUCKET_SIZE)
	var key = (bx << 16) | (by & 0xFFFF)
	
	if not spatial_grid.has(key):
		spatial_grid[key] = []
		spatial_team_mask[key] = 0
	
	if not u in spatial_grid[key]:
		spatial_grid[key].append(u)
		
		# Update Team Mask
		var team_bit = 1 if u.team == "player" else (2 if u.team == "enemy" else 4)
		spatial_team_mask[key] |= team_bit

func remove_unit_spatial(u):
	"""Remove unit from spatial bucket"""
	var bx = int(u.pos.x / SPATIAL_BUCKET_SIZE)
	var by = int(u.pos.y / SPATIAL_BUCKET_SIZE)
	var key = (bx << 16) | (by & 0xFFFF)
	
	if spatial_grid.has(key):
		spatial_grid[key].erase(u)

func refresh_all_spatial(units: Array):
	"""Rebuild entire spatial grid from unit list"""
	spatial_grid.clear()
	spatial_team_mask.clear()
	
	for u in units:
		if u.hp > 0 and not u.status.get("is_dead", false):
			update_unit_spatial(u)

# -----------------------------
# Spatial Queries
# -----------------------------

func find_nearest_enemy_spatial(u, max_dist: float, target_battalion_id = -1, prioritize_clusters = false):
	"""Fast spatial enemy search using bucket optimization"""
	var best_target_match = null
	var min_d_target = max_dist
	
	var best_any_match = null
	var min_d_any = max_dist
	
	var best_cluster_score = -1.0
	var best_cluster_target = null
	
	var bx = int(u.pos.x / SPATIAL_BUCKET_SIZE)
	var by = int(u.pos.y / SPATIAL_BUCKET_SIZE)
	var r = int(ceil(max_dist / float(SPATIAL_BUCKET_SIZE)))
	
	# BITMASK OPTIMIZATION
	var enemy_bit = 2 if u.team == "player" else (1 if u.team == "enemy" else 7)
	
	for ny in range(by - r, by + r + 1):
		for nx in range(bx - r, bx + r + 1):
			var key = (nx << 16) | (ny & 0xFFFF)
			
			# Skip buckets with no enemies
			if spatial_team_mask.get(key, 0) & enemy_bit == 0:
				continue
			
			var bucket = spatial_grid.get(key, null)
			if bucket:
				for e in bucket:
					if e.team != u.team and e.hp > 0 and not e.status.get("is_dead", false):
						var d = u.pos.distance_to(e.pos)
						
						if prioritize_clusters:
							# Siege engines prioritize dense formations
							var density = bucket.size()
							var score = float(density) / (d * 0.5)
							if score > best_cluster_score:
								best_cluster_score = score
								best_cluster_target = e
						
						# Track closest in target battalion
						if target_battalion_id != -1 and e.formation_id == target_battalion_id:
							if d < min_d_target:
								min_d_target = d
								best_target_match = e
						
						# Track closest overall
						if d < min_d_any:
							min_d_any = d
							best_any_match = e
	
	if prioritize_clusters and best_cluster_target:
		return best_cluster_target
	
	if best_target_match:
		return best_target_match
	return best_any_match

func find_nearest_tile_char(pos: Vector2i, char_to_find: String, max_dist: int) -> Vector2i:
	"""Find nearest tile matching character (uses structural cache)"""
	# Optimized: Check structural cache first
	if structural_cache.has(char_to_find):
		var best = Vector2i(-1, -1)
		var min_d = max_dist
		for target in structural_cache[char_to_find]:
			var d = pos.distance_to(target)
			if d < min_d:
				min_d = d
				best = target
		return best
	
	# Fallback: Brute force search
	var best = Vector2i(-1, -1)
	var min_d = max_dist
	for dy in range(-max_dist, max_dist + 1):
		for dx in range(-max_dist, max_dist + 1):
			var wx = pos.x + dx
			var wy = pos.y + dy
			if wx >= 0 and wx < MAP_W and wy >= 0 and wy < MAP_H:
				if grid[wy][wx] == char_to_find:
					var d = pos.distance_to(Vector2i(wx, wy))
					if d < min_d:
						min_d = d
						best = Vector2i(wx, wy)
	return best

# -----------------------------
# Movement & Pathfinding
# -----------------------------

func try_move(u, new_pos: Vector2i) -> bool:
	"""Attempt to move unit to new position (handles collision)"""
	if not is_in_bounds(new_pos):
		return false
	
	# Check footprint collision
	for offset in u.footprint:
		var check_pos = new_pos + offset
		if not is_in_bounds(check_pos):
			return false
		
		# Terrain blocking
		var tile = grid[check_pos.y][check_pos.x]
		if tile in ["^", "~"]: # Mountains, Water
			return false
		if tile == "#" and not u.is_siege_engine: # Walls block non-siege
			return false
		
		# Unit collision
		if unit_lookup.has(check_pos):
			var blocker = unit_lookup[check_pos]
			if blocker != u and blocker.hp > 0:
				# Allow swapping with same-team units
				if blocker.team != u.team:
					return false
	
	# Execute move
	unregister_unit(u)
	u.pos = new_pos
	register_unit(u)
	return true

func move_towards(u, target_pos: Vector2i):
	"""Pathfind toward target with sliding"""
	if u.is_siege_engine and not u.engine_stats.get("is_mobile", true):
		return
	
	var diff = target_pos - u.pos
	var dir = diff.sign()
	
	# 1. Diagonal movement
	if dir.x != 0 and dir.y != 0:
		if try_move(u, u.pos + Vector2i(dir.x, dir.y)):
			return
	
	# 2. Orthogonal movement
	if dir.x != 0 and try_move(u, u.pos + Vector2i(dir.x, 0)):
		return
	if dir.y != 0 and try_move(u, u.pos + Vector2i(0, dir.y)):
		return
	
	# 3. Smart sliding around obstacles
	if dir.x != 0:
		if try_move(u, u.pos + Vector2i(dir.x, 1)):
			return
		if try_move(u, u.pos + Vector2i(dir.x, -1)):
			return
	if dir.y != 0:
		if try_move(u, u.pos + Vector2i(1, dir.y)):
			return
		if try_move(u, u.pos + Vector2i(-1, dir.y)):
			return

func move_away_from(u, target_pos: Vector2i):
	"""Pathfind away from target (fleeing)"""
	var diff = u.pos - target_pos
	var dir = diff.sign()
	
	if dir == Vector2i.ZERO:
		dir = Vector2i(randi_range(-1, 1), randi_range(-1, 1))
	if dir == Vector2i.ZERO:
		dir = Vector2i(1, 0)
	
	# Try escape paths
	if try_move(u, u.pos + dir):
		return
	if dir.x != 0 and try_move(u, u.pos + Vector2i(dir.x, 0)):
		return
	if dir.y != 0 and try_move(u, u.pos + Vector2i(0, dir.y)):
		return

func get_step_towards(u, target_pos: Vector2i) -> Vector2i:
	"""Calculate one step toward target"""
	var dir = (Vector2(target_pos) - Vector2(u.pos)).normalized()
	return u.pos + Vector2i(round(dir.x), round(dir.y))

func get_step_away(u, target_pos: Vector2i) -> Vector2i:
	"""Calculate one step away from target"""
	var dir = (Vector2(u.pos) - Vector2(target_pos)).normalized()
	if dir == Vector2.ZERO:
		dir = Vector2(1, 0)
	return u.pos + Vector2i(round(dir.x), round(dir.y))

# -----------------------------
# Utility
# -----------------------------

func find_unit_along_line(start_pos: Vector2, dir: Vector2, max_dist: float, exclude: Array):
	"""Raycast to find unit along trajectory (for penetration)"""
	for i in range(1, int(max_dist)):
		var check_pos = Vector2i(start_pos + (dir * i))
		if unit_lookup.has(check_pos):
			var u = unit_lookup[check_pos]
			if u.hp > 0 and not u in exclude:
				return u
	return null
