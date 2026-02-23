extends Node

## WorldState — global singleton (autoload).
## Holds the current WorldData, all settlements, factions and armies.
## Register in Project → Project Settings → AutoLoad as "WorldState".

var world_data:  WorldData = null
var settlements: Array     = []  ## Array[Settlement]
var factions:    Array     = []  ## Array[Faction]   (Phase 3)
var armies:      Array     = []  ## Array[Army]      (Phase 3)

# Region cache: Vector2i(wx, wy) → RegionData
# Cleared every time a new world is generated.
var _region_cache: Dictionary = {}


func _ready() -> void:
	# Connect to GameClock once it is available (it is an autoload, so always ready).
	GameClock.daily_pulse.connect(_on_daily_pulse)


## Called by world_map.gd whenever a new map finishes generating.
func update_world(data: WorldData) -> void:
	world_data  = data
	settlements = data.settlements.duplicate()  ## take ownership
	factions.clear()
	armies.clear()
	_region_cache.clear()


## Daily simulation tick — drives all settlements.
func _on_daily_pulse(_turn: int) -> void:
	for s in settlements:
		s.daily_tick()


## Returns the settlement at world tile (tx, ty), or null.
func get_settlement_at(tx: int, ty: int) -> Settlement:
	for s: Settlement in settlements:
		if s.tile_x == tx and s.tile_y == ty:
			return s
	return null


## Returns (and caches) the RegionData for world tile (wx, wy).
func get_region(wx: int, wy: int) -> RegionData:
	if world_data == null:
		return null
	var key := Vector2i(wx, wy)
	if not _region_cache.has(key):
		_region_cache[key] = RegionGenerator.generate(world_data, wx, wy)
	return _region_cache[key]


## Generates (not cached) the LocalMapData for world tile (wx, wy).
## Fast enough (<1 ms for 48×48) to skip caching.
func get_local_map(wx: int, wy: int) -> LocalMapData:
	if world_data == null:
		return null
	var region := get_region(wx, wy)
	return LocalMapGenerator.generate(world_data, region)


## Returns the province name for the given ID, or a fallback string.
func get_province_name(pid: int) -> String:
	if world_data == null or pid < 0:
		return "Unknown"
	if pid >= world_data.province_names.size():
		return "Province %d" % pid
	return world_data.province_names[pid]
