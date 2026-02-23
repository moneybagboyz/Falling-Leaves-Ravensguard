class_name WorldData

## Gameplay terrain categories — derived by TerrainClassifier from biome + hydrology.
enum TerrainType {
	OCEAN,
	SHALLOW_WATER,
	COAST,
	PLAINS,
	HILLS,
	FOREST,
	MOUNTAIN,
	DESERT,
	RIVER,
	LAKE,
}

## Container for all generated world data arrays.
## Each layer is a 2D array [y][x] of floats (0.0–1.0)
## or BiomeType values.

var width: int
var height: int
var world_seed: int

## Raw noise layers — all normalized to 0.0 .. 1.0
var altitude: Array      ## float
var temperature: Array   ## float
var precipitation: Array ## float
var drainage: Array      ## float

## Derived layers
var biome: Array         ## TileRegistry.BiomeType
var prosperity: Array    ## float

## Computed sea level — set by WorldGenerator after altitude + falloff so that
## exactly SEA_RATIO fraction of tiles are ocean.  Used by biome classification.
var sea_level: float = 0.34

## Hydrology layers — populated by Hydrology.process() after biome derivation
var flow:     Array      ## float 0..1, normalised accumulated flow (for display)
var is_river: Array      ## bool
var is_lake:  Array      ## bool

## Phase 1.5 layers — populated by TerrainClassifier and ProvinceGenerator
var terrain:            Array      ## TerrainType int, one per tile
var province_id:        Array      ## int; -1 = unassigned (ocean / unclassified)
var settlement_score:   Array      ## float; desirability score for settlements
var province_adjacency: Dictionary = {}  ## province_id -> {neighbour_id: true}
var province_names:     Array      ## String; element i = name of province i
var province_capitals:  Array      ## Vector2i; element i = hub tile of province i

## Phase 2 — Settlement objects; carries across thread boundary into WorldState
var settlements:        Array      ## Settlement; one per province
var road_network:       Dictionary = {}  ## Vector2i -> Array[Vector2i] tile adjacency list


func _init(w: int, h: int, seed_val: int = 0) -> void:
	width = w
	height = h
	world_seed = seed_val
	altitude      = _make_grid(w, h, 0.0)
	temperature   = _make_grid(w, h, 0.0)
	precipitation = _make_grid(w, h, 0.0)
	drainage      = _make_grid(w, h, 0.0)
	biome         = _make_grid(w, h, TileRegistry.BiomeType.OCEAN)
	prosperity    = _make_grid(w, h, 0.0)
	flow          = _make_grid(w, h, 0.0)
	is_river      = _make_grid(w, h, false)
	is_lake       = _make_grid(w, h, false)
	terrain          = _make_grid(w, h, 0)    # TerrainType.OCEAN = 0
	province_id      = _make_grid(w, h, -1)
	settlement_score = _make_grid(w, h, 0.0)
	province_names    = []
	province_capitals = []
	settlements       = []
	road_network      = {}


static func _make_grid(w: int, h: int, default_val: Variant) -> Array:
	var grid: Array = []
	grid.resize(h)
	for y in range(h):
		var row: Array = []
		row.resize(w)
		row.fill(default_val)
		grid[y] = row
	return grid
