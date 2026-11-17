class_name WorldGenContext
extends RefCounted

## Shared context for world generation phases
## Contains all temporary and final data needed across generation pipeline

# World dimensions
var width: int = 0
var height: int = 0
var rng: RandomNumberGenerator
var config: Dictionary = {}

# Live grid for rendering
var world_grid: Array = []

# Configuration parameters (extracted from config)
var num_plates: int = 12
var num_factions: int = 5
var savagery: int = 5
var moisture_bias: float = 1.0
var temp_bias: float = 1.0
var layout: String = "Pangea"
var mineral_density: int = 5

# Temporary arrays (freed after use to save memory)
var elevation_map: Array = []  # Freed after atmosphere
var temp_map: Array = []  # Freed after biomes
var moisture_map: Array = []  # Freed after biomes
var drainage_map: Array = []  # Freed after biomes
var strata_map: Array = []  # Kept for geology
var plate_map: Array = []  # Freed after elevation

# Persistent data (kept until generation complete)
var plates: Array = []
var geology: Dictionary = {}  # Vector2i -> {temp, rain, layers}
var world_resources: Dictionary = {}
var world_settlements: Dictionary = {}
var provinces: Dictionary = {}  # province_id -> { cells: [], resources: {} }
var province_grid: ProvinceSectorGrid  # Efficient province lookup
var factions: Dictionary = {}  # faction_id -> GDFaction
var roads: Array = []  # Array of road segment dictionaries
var armies: Array = []  # Array of GDArmy instances
var caravans: Array = []  # Array of GDCaravan instances
var npcs: Array = []  # Array of GDNPC instances

# Noise generators (reused across phases)
var noise_detail: FastNoiseLite
var noise_temp: FastNoiseLite
var noise_drainage: FastNoiseLite

# Helper class for sector-based province storage (from WorldGen)
class ProvinceSectorGrid:
	const SECTOR_SIZE = 10
	var sectors = {}
	var width = 0
	var height = 0
	
	func _init(w: int, h: int):
		width = w
		height = h
	
	func get_province(pos: Vector2i) -> int:
		var sector_pos = Vector2i(pos.x / SECTOR_SIZE, pos.y / SECTOR_SIZE)
		return sectors.get(sector_pos, -1)
	
	func set_province(pos: Vector2i, province_id: int):
		var sector_pos = Vector2i(pos.x / SECTOR_SIZE, pos.y / SECTOR_SIZE)
		sectors[sector_pos] = province_id
	
	func to_legacy_grid() -> Array:
		var grid = []
		for y in range(height):
			grid.append([])
			for x in range(width):
				grid[y].append(get_province(Vector2i(x, y)))
		return grid

func _init(w: int, h: int, rng_instance: RandomNumberGenerator, live_grid: Array, gen_config: Dictionary):
	width = w
	height = h
	rng = rng_instance
	world_grid = live_grid
	config = gen_config
	
	# Extract config parameters
	num_plates = config.get("num_plates", 12)
	num_factions = config.get("num_factions", 5)
	savagery = config.get("savagery", 5)
	moisture_bias = config.get("moisture", 1.0)
	temp_bias = config.get("temperature", 1.0)
	layout = config.get("layout", "Pangea")
	mineral_density = config.get("mineral_density", 5)
	
	# Initialize province grid
	province_grid = ProvinceSectorGrid.new(w, h)
	
	# Initialize noise generators
	noise_detail = FastNoiseLite.new()
	noise_detail.seed = rng.randi()
	noise_detail.frequency = 0.05
	
	noise_temp = FastNoiseLite.new()
	noise_temp.seed = rng.randi()
	noise_temp.frequency = 0.01
	
	noise_drainage = FastNoiseLite.new()
	noise_drainage.seed = rng.randi()
	noise_drainage.frequency = 0.04
	
	# Initialize world grid if empty
	if world_grid.is_empty():
		for y in range(h):
			var row = []
			row.resize(w)
			row.fill('~')
			world_grid.append(row)

## Build the final output dictionary expected by GameState
func to_output_dict() -> Dictionary:
	return {
		"grid": world_grid,
		"geology": geology,
		"resources": world_resources,
		"settlements": world_settlements,
		"provinces": provinces,
		"province_grid": province_grid.to_legacy_grid(),
		"factions": factions,
		"roads": roads,
		"armies": armies,
		"caravans": caravans,
		"npcs": npcs
	}
