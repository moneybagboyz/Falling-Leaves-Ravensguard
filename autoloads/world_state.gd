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
func _on_daily_pulse(turn: int) -> void:
	for s in settlements:
		s.daily_tick()
	# Print a summary to the Output console every 30 in-game days.
	# Remove or increase the interval once the UI shows this data.
	var day: int = turn / 24
	if day % 30 == 0:
		_print_daily_summary(day)


func _print_daily_summary(day: int) -> void:
	var total_pop: int   = 0
	var total_grain: float = 0.0
	var total_treasury: float = 0.0
	var avg_happiness: float  = 0.0
	for s: Settlement in settlements:
		total_pop      += s.population
		total_grain    += s.market.get_stock("grain")
		total_treasury += s.treasury
		avg_happiness  += s.happiness
	if settlements.size() > 0:
		avg_happiness /= float(settlements.size())
	print("\n─── Day %d ──────────────────────────────" % day)
	print("  Settlements : %d" % settlements.size())
	print("  Total pop   : %d" % total_pop)
	print("  Total grain  : %.0f" % total_grain)
	print("  Avg happiness: %.1f" % avg_happiness)
	print("  Total treasury: %.0fg" % total_treasury)
	print("  ── Per settlement ──")
	for s: Settlement in settlements:
		print("  %s" % s.summary())


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
