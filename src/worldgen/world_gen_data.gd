## WorldGenData — transient 2-D grid used during world generation.
##
## This object is NOT persisted. It is created by RegionGenerator,
## filled by every generation pass, then consumed to produce the
## WorldState's world_tiles dictionary and SettlementState list.
##
## All 2-D arrays are row-major: array[y][x].
class_name WorldGenData
extends RefCounted

var width:      int = 0
var height:     int = 0
var world_seed: int = 0

# ── Raw noise layers (float 0.0–1.0) ─────────────────────────────────────────
var altitude:     Array = []
var temperature:  Array = []
var precipitation: Array = []
var drainage:     Array = []

# ── Derived layers ────────────────────────────────────────────────────────────
var biome:            Array = []   # String biome ID per cell
var terrain:          Array = []   # String terrain type ID per cell
var prosperity:       Array = []   # float fertility score
var settlement_score: Array = []   # float settlement suitability

# ── Hydrology ─────────────────────────────────────────────────────────────────
var is_river: Array = []  # bool
var is_lake:  Array = []  # bool

# ── Province assignment ───────────────────────────────────────────────────────
var province_id:       Array = []    # int (-1 = unassigned / water)
var province_capitals: Array = []    # Array[Vector2i] — one per province
var province_names:    Array = []    # Array[String]
var province_adjacency: Dictionary = {}  # pid -> {npid: true}

# ── Road network ──────────────────────────────────────────────────────────────
## road_network[Vector2i(x,y)] = Array[Vector2i] neighbours in road graph.
var road_network: Dictionary = {}

# ── Geology ───────────────────────────────────────────────────────────────────
## geology[Vector2i(x,y)] = Array[String] resource tags placed here.
var geology: Dictionary = {}

# ── Computed sea level (quantile of altitude distribution) ───────────────────
var sea_level: float = 0.4


func _init(w: int, h: int, seed_val: int) -> void:
	width      = w
	height     = h
	world_seed = seed_val
	_allocate()


func _allocate() -> void:
	altitude       = _make_grid(0.0)
	temperature    = _make_grid(0.0)
	precipitation  = _make_grid(0.0)
	drainage       = _make_grid(0.0)
	biome          = _make_grid("")
	terrain        = _make_grid("")
	prosperity     = _make_grid(0.0)
	settlement_score = _make_grid(0.0)
	is_river       = _make_grid(false)
	is_lake        = _make_grid(false)
	province_id    = _make_grid(-1)


func _make_grid(default) -> Array:
	var grid: Array = []
	grid.resize(height)
	for y in range(height):
		var row: Array = []
		row.resize(width)
		row.fill(default)
		grid[y] = row
	return grid


## True if (x,y) is above sea level.
func is_land(x: int, y: int) -> bool:
	return altitude[y][x] > sea_level


## Iterate all land cells as Vector2i. Used frequently by placement passes.
func land_tiles() -> Array:
	var tiles: Array = []
	for y in range(height):
		for x in range(width):
			if altitude[y][x] > sea_level:
				tiles.append(Vector2i(x, y))
	return tiles


## Collect the flat altitude array for quantile computation.
func altitude_flat() -> Array:
	var flat: Array = []
	flat.resize(width * height)
	var i := 0
	for y in range(height):
		for x in range(width):
			flat[i] = altitude[y][x]
			i += 1
	return flat
