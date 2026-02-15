class_name WorldState
extends RefCounted

# World Grid and Terrain
var grid: Array = [] # 2D array [y][x] - Array of Arrays
var width: int = 0
var height: int = 0
var resources: Dictionary = {} # Vector2i -> String (e.g. "iron", "gold")
var geology: Dictionary = {} # Vector2i -> Dict (temp, rain, layers)
var ruins: Dictionary = {} # Vector2i -> Dict

# World Identity
var world_seed: int = 0
var world_name: String = "Unknown Land"

# Province/Political Data
# Note: province_grid is a legacy 2D array [y][x] for compatibility
# Generated from watershed-based sector system (10x10 tile sectors)
# See WorldGen.ProvinceSectorGrid for efficient storage
var province_grid = []
var provinces = {}

# Travel Mode Data
enum TravelMode { FAST, REGION, LOCAL }
var travel_mode = TravelMode.FAST
var map_mode = "terrain" # "terrain", "political", "province", "resource"
var render_mode = "grid" # "ascii", "grid"

# --- SECTOR PAGING CONSTANTS ---
const WORLD_TILE_SIZE = 1000.0 # Meters per side
const METERS_PER_LOCAL_TILE = 2.0 # Tactical scale (DF standard)
const SECTOR_SIZE_METERS = 1000.0 # 500 tiles * 2m (Matches World Tile)
const PAGING_THRESHOLD = 50.0 # Regenerate if player moves too far from center

var local_offset = Vector2(500.0, 500.0) # Meters within current world tile
var last_gen_offset = Vector2(-999, -999) # Tracking for re-generation

const LOCAL_GRID_W = 500 # Matches BattleController.MAP_W
const LOCAL_GRID_H = 500 # Matches BattleController.MAP_H
var local_step_count = 0

# Pathfinding
var astar = AStarGrid2D.new()

# --- FAUNA SYSTEM ---
var killed_fauna: Dictionary = {} # Vector2i (world) -> Array of Vector2i (local)

# --- SPATIAL HASHING ---
const SPATIAL_CELL_SIZE = 10
var spatial_grid: Dictionary = {} # Vector2i (cell) -> Array of Entities

# --- PERFORMANCE CACHE ---
var distance_cache: Dictionary = {} # Vector2i_pair_key -> float

func _init():
	pass

func clear():
	"""Reset world state"""
	grid.clear()
	width = 0
	height = 0
	resources.clear()
	geology.clear()
	ruins.clear()
	province_grid.clear()
	provinces.clear()
	killed_fauna.clear()
	spatial_grid.clear()
	distance_cache.clear()
	local_offset = Vector2(500.0, 500.0)
	last_gen_offset = Vector2(-999, -999)
	local_step_count = 0

func get_tile(pos: Vector2i) -> String:
	"""Get terrain tile at position"""
	if pos.y >= 0 and pos.y < grid.size() and pos.x >= 0 and pos.x < grid[pos.y].size():
		return grid[pos.y][pos.x]
	return ""

func set_tile(pos: Vector2i, tile: String):
	"""Set terrain tile at position"""
	if pos.y >= 0 and pos.y < grid.size() and pos.x >= 0 and pos.x < grid[pos.y].size():
		grid[pos.y][pos.x] = tile

func is_valid_position(pos: Vector2i) -> bool:
	"""Check if position is within world bounds"""
	return pos.x >= 0 and pos.x < width and pos.y >= 0 and pos.y < height

func get_biome(pos: Vector2i) -> String:
	"""Get biome type at position based on terrain"""
	var tile = get_tile(pos)
	match tile:
		"~", "≈": return "ocean"
		"^": return "mountain"
		"▲": return "hills"
		"♠", "♣": return "forest"
		"\"": return "grassland"
		",": return "plains"
		"*": return "tundra"
		"_": return "desert"
		_: return "grassland"

func get_true_terrain(pos: Vector2i) -> String:
	"""Get actual terrain type, using geology to override settlement/road tiles"""
	if not is_valid_position(pos): return "~"
	var t = get_tile(pos)
	# Use geology to find underlying terrain if grid is an overlay (Roads, Towns, etc.)
	if geology.has(pos) and t in ["=", "T", "C", "v", "h", "k", "?"]:
		return geology[pos].get("biome", t)
	return t

func get_geology_data(pos: Vector2i) -> Dictionary:
	"""Get geological data at position"""
	return geology.get(pos, {"elevation": 0.5, "temp": 0.5, "rain": 0.5})

func get_cached_distance(from: Vector2i, to: Vector2i) -> float:
	"""Get cached Manhattan distance between positions"""
	var key = str(from) + "_" + str(to)
	if not distance_cache.has(key):
		distance_cache[key] = abs(from.x - to.x) + abs(from.y - to.y)
	return distance_cache[key]

func add_to_spatial_grid(entity, pos: Vector2i):
	"""Add entity to spatial hashing grid"""
	var cell = Vector2i(pos.x / SPATIAL_CELL_SIZE, pos.y / SPATIAL_CELL_SIZE)
	if not spatial_grid.has(cell):
		spatial_grid[cell] = []
	spatial_grid[cell].append(entity)

func remove_from_spatial_grid(entity, pos: Vector2i):
	"""Remove entity from spatial hashing grid"""
	var cell = Vector2i(pos.x / SPATIAL_CELL_SIZE, pos.y / SPATIAL_CELL_SIZE)
	if spatial_grid.has(cell):
		spatial_grid[cell].erase(entity)

func get_entities_near(pos: Vector2i, radius: int = 1) -> Array:
	"""Get entities in nearby spatial cells"""
	var entities = []
	var center_cell = Vector2i(pos.x / SPATIAL_CELL_SIZE, pos.y / SPATIAL_CELL_SIZE)
	
	for dy in range(-radius, radius + 1):
		for dx in range(-radius, radius + 1):
			var cell = center_cell + Vector2i(dx, dy)
			if spatial_grid.has(cell):
				entities.append_array(spatial_grid[cell])
	
	return entities
