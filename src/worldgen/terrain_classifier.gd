## TerrainClassifier — derives terrain type ID from biome + altitude + water flags.
##
## Terrain types are coarser than biomes and drive movement costs,
## production modifiers, and settlement scoring. IDs match /data/world/terrain_types.json.
class_name TerrainClassifier
extends RefCounted


## Derive terrain type for one cell.
static func classify(
		biome_id: String,
		alt: float,
		sea_level: float,
		is_river_cell: bool,
		is_lake_cell:  bool
) -> String:
	# Water overrides come first.
	if is_lake_cell:
		return "lake"
	if is_river_cell:
		return "river"

	match biome_id:
		"deep_ocean":
			return "ocean"
		"ocean":
			return "ocean"
		"shallow_water":
			return "shallow_water"
		"beach":
			return "coast"
		"mountain_snow", "mountain_rock":
			return "mountain"
		"snow", "tundra":
			return "tundra"
		"boreal_forest", "temperate_forest", "tropical_rainforest":
			return "forest"
		"woodland":
			# Hills if the land is elevated mid-range.
			return "hills" if alt > sea_level + 0.25 else "forest"
		"desert":
			return "desert"
		"savanna", "grassland":
			return "hills" if alt > sea_level + 0.30 else "plains"
		_:
			return "plains"


## Populate data.terrain[y][x] for every cell.
## Call AFTER BiomeClassifier.classify_all() and Hydrology.process().
static func classify_all(data: WorldGenData) -> void:
	for y in range(data.height):
		for x in range(data.width):
			data.terrain[y][x] = classify(
				data.biome[y][x],
				data.altitude[y][x],
				data.sea_level,
				data.is_river[y][x],
				data.is_lake[y][x]
			)
	# Second pass: mark coast tiles — land adjacent to any water tile.
	_mark_coasts(data)


static func _mark_coasts(data: WorldGenData) -> void:
	for y in range(data.height):
		for x in range(data.width):
			if data.terrain[y][x] == "plains" or data.terrain[y][x] == "hills":
				if _adjacent_to_water(data, x, y):
					data.terrain[y][x] = "coast"


static func _adjacent_to_water(data: WorldGenData, x: int, y: int) -> bool:
	# Cardinal directions only (no diagonals) — prevents coast from bleeding
	# deep into concave bays and river deltas.
	for delta in [Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1)]:
		var nx: int = x + delta.x
		var ny: int = y + delta.y
		if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
			continue
		var t: String = data.terrain[ny][nx]
		# Only true ocean/shallow borders qualify as coast — not lakes or rivers.
		if t == "ocean" or t == "shallow_water":
			return true
	return false


## Movement cost for a terrain type ID.
## Used by Dijkstra in ProvinceGenerator and RouteGenerator.
## Values from /data/world/terrain_types.json; hardcoded fallback.
static func move_cost(terrain_id: String) -> float:
	if ContentRegistry.has_content("terrain_type", terrain_id):
		return ContentRegistry.get_content("terrain_type", terrain_id).get("movement_cost", 1.0)
	const TABLE: Dictionary = {
		"plains":        1.0,
		"coast":         1.5,
		"river":         1.5,
		"hills":         3.0,
		"forest":        3.0,
		"desert":        2.5,
		"tundra":        4.0,
		"mountain":      9.0,
		"shallow_water": INF,
		"ocean":         INF,
		"lake":          INF,
	}
	return TABLE.get(terrain_id, 1.0)
