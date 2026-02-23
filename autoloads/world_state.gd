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
	# Print a world audit every 30 in-game days.
	var day: int = turn / 24
	if day > 0 and day % 30 == 0:
		_print_world_audit(day)


# ===========================================================================
# World Audit — printed every 30 in-game days to the Godot Output console.
# Replace with an in-game UI panel once the observability UI pass begins.
# ===========================================================================
func _print_world_audit(day: int) -> void:
	if settlements.is_empty():
		return

	# ── Aggregate data ──────────────────────────────────────────────────────
	var total_pop:      int   = 0
	var total_treasury: float = 0.0
	var total_happy:    float = 0.0
	var total_unrest:   float = 0.0
	var tier_counts:    Array = [0, 0, 0, 0, 0]   # index = tier 0..4
	var resource_totals: Dictionary = {}
	for rid in ResourceRegistry.ALL_RESOURCES:
		resource_totals[rid] = 0.0
	var building_counts:    Dictionary = {}  # building_type -> count
	var building_max_level: Dictionary = {}  # building_type -> highest level

	var starving:     Array = []   # settlements with < 7 days of grain
	var unrest_hot:   Array = []   # settlements with unrest > 50
	var richest:    Settlement = null
	var poorest:    Settlement = null
	var happiest:   Settlement = null
	var saddest:    Settlement = null

	for s: Settlement in settlements:
		total_pop      += s.population
		total_treasury += s.treasury
		total_happy    += s.happiness
		total_unrest   += s.unrest
		tier_counts[clampi(s.tier, 0, 4)] += 1

		# Per-resource stock totals
		for rid in ResourceRegistry.ALL_RESOURCES:
			resource_totals[rid] += s.market.get_stock(rid)

		# Building census
		for b: Building in s.buildings:
			building_counts[b.building_type] = building_counts.get(b.building_type, 0) + 1
			if not building_max_level.has(b.building_type) or b.level > building_max_level[b.building_type]:
				building_max_level[b.building_type] = b.level

		# Food security: 7 days = population * 1.2 * 7
		var grain_days: float = s.market.get_stock("grain") / maxf(s.population * 1.2, 1.0)
		if grain_days < 7.0:
			starving.append([s.name, grain_days])

		if s.unrest > 50.0:
			unrest_hot.append([s.name, s.unrest])

		if richest  == null or s.treasury  > richest.treasury:   richest  = s
		if poorest  == null or s.treasury  < poorest.treasury:   poorest  = s
		if happiest == null or s.happiness > happiest.happiness:  happiest = s
		if saddest  == null or s.happiness < saddest.happiness:   saddest  = s

	var n:          int   = settlements.size()
	var avg_happy:  float = total_happy  / n
	var avg_unrest: float = total_unrest / n
	const TIER_NAMES: PackedStringArray = ["Hamlet","Village","Town","City","Metropolis"]

	# ── Print ───────────────────────────────────────────────────────────────
	print("\n╔══════════════════════════════════════════════════════════════")
	print("║  WORLD AUDIT — Day %d" % day)
	print("╠══════════════════════════════════════════════════════════════")

	print("║  SUMMARY")
	print("║    Settlements  : %d        Population : %d" % [n, total_pop])
	print("║    Avg Happiness: %.1f%%      Avg Unrest : %.1f" % [avg_happy, avg_unrest])
	print("║    World Treasury: %.0fg" % total_treasury)

	print("║")
	print("║  TIER CENSUS")
	for t in range(5):
		if tier_counts[t] > 0:
			print("║    %-12s : %d" % [TIER_NAMES[t], tier_counts[t]])

	print("║")
	print("║  FOOD SECURITY")
	if starving.is_empty():
		print("║    All settlements have >= 7 days of grain  OK")
	else:
		print("║    %d settlement(s) at starvation risk:" % starving.size())
		for entry in starving:
			print("║      %-22s  %.1f days remaining" % [entry[0], entry[1]])

	if not unrest_hot.is_empty():
		print("║")
		print("║  UNREST HOTSPOTS  (unrest > 50)")
		for entry in unrest_hot:
			print("║    %-22s  unrest: %.0f" % [entry[0], entry[1]])

	print("║")
	print("║  NOTABLE SETTLEMENTS")
	if richest  != null: print("║    Richest   : %-20s  %.0fg" % [richest.name,  richest.treasury])
	if poorest  != null: print("║    Poorest   : %-20s  %.0fg" % [poorest.name,  poorest.treasury])
	if happiest != null: print("║    Happiest  : %-20s  %.0f%%" % [happiest.name, happiest.happiness])
	if saddest  != null: print("║    Saddest   : %-20s  %.0f%%" % [saddest.name,  saddest.happiness])

	print("║")
	print("║  GLOBAL RESOURCES")
	for rid in ResourceRegistry.ALL_RESOURCES:
		var qty: float = resource_totals[rid]
		if qty > 0.0:
			print("║    %-14s : %.0f" % [rid, qty])

	print("║")
	print("║  BUILDING CENSUS")
	var btypes: Array = building_counts.keys()
	btypes.sort()
	for bt in btypes:
		var max_lv: int = building_max_level.get(bt, 1)
		print("║    %-16s : %d built  (max lv %d)" % [bt, building_counts[bt], max_lv])

	print("╚══════════════════════════════════════════════════════════════")


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
