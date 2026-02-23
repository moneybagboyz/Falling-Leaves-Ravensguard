class_name RegionData

## 8×8 tile grid representing a single world tile at region resolution.
## Generated on-demand by RegionGenerator from WorldData.
## Cached in WorldState._region_cache keyed by Vector2i(wx, wy).

const SCALE: int = 8  ## region tiles per world-tile edge

var world_tile: Vector2i = Vector2i.ZERO
var width:  int = SCALE
var height: int = SCALE

var altitude: Array   ## float  — world altitude ± detail noise  (0..1)
var biome:    Array   ## TileRegistry.BiomeType int
var terrain:  Array   ## WorldData.TerrainType int
var feature:  Array   ## RegionFeature int
var is_river: Array   ## bool

## Features visible in region view.
enum RegionFeature {
	NONE,
	RUINS,
	CAMP,
	MINE_ENTRANCE,
	FORD,
	DENSE_FOREST,
}


func _init(wx: int = 0, wy: int = 0) -> void:
	world_tile = Vector2i(wx, wy)
	altitude = _make(SCALE, SCALE, 0.0)
	biome    = _make(SCALE, SCALE, int(TileRegistry.BiomeType.OCEAN))
	terrain  = _make(SCALE, SCALE, int(WorldData.TerrainType.OCEAN))
	feature  = _make(SCALE, SCALE, int(RegionFeature.NONE))
	is_river = _make(SCALE, SCALE, false)


static func _make(w: int, h: int, v: Variant) -> Array:
	var g: Array = []
	g.resize(h)
	for y in range(h):
		var row: Array = []
		row.resize(w)
		row.fill(v)
		g[y] = row
	return g
