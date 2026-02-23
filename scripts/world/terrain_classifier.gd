class_name TerrainClassifier

## Converts each WorldData tile's biome + hydrology flags into a TerrainType int.
## Call after Hydrology.process() in WorldGenerator.generate().
##
## COAST is assigned by adjacency (second pass), not by altitude band.
## This ensures coast is at most one tile deep regardless of terrain slope.

static func classify(data: WorldData) -> void:
	# Pass 1 — per-tile biome → terrain (BEACH is treated as ordinary land here)
	for y in range(data.height):
		for x in range(data.width):
			data.terrain[y][x] = _to_terrain(
				data.biome[y][x],
				data.altitude[y][x],
				data.is_river[y][x],
				data.is_lake[y][x]
			)

	# Pass 2 — adjacency coast: any non-water land tile that borders OCEAN or
	# SHALLOW_WATER becomes COAST.  Exactly one tile wide regardless of slope.
	const WATER := [
		WorldData.TerrainType.OCEAN,
		WorldData.TerrainType.SHALLOW_WATER,
	]
	const DIRS := [Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1)]
	for y in range(data.height):
		for x in range(data.width):
			var t: int = data.terrain[y][x]
			if t == WorldData.TerrainType.OCEAN   \
			or t == WorldData.TerrainType.SHALLOW_WATER \
			or t == WorldData.TerrainType.RIVER   \
			or t == WorldData.TerrainType.LAKE:
				continue
			for d in DIRS:
				var nx: int = x + d.x
				var ny: int = y + d.y
				if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
					continue
				if data.terrain[ny][nx] in WATER:
					data.terrain[y][x] = WorldData.TerrainType.COAST
					break


static func _to_terrain(biome: int, alt: float, is_riv: bool, is_lk: bool) -> int:
	# Hydrology overrides biome classification
	if is_lk:  return WorldData.TerrainType.LAKE
	if is_riv: return WorldData.TerrainType.RIVER

	match biome:
		TileRegistry.BiomeType.DEEP_OCEAN, \
		TileRegistry.BiomeType.OCEAN:
			return WorldData.TerrainType.OCEAN

		TileRegistry.BiomeType.SHALLOW_WATER:
			return WorldData.TerrainType.SHALLOW_WATER

		# BEACH biome is intentionally omitted here — it falls through to PLAINS.
		# The adjacency pass in classify() will promote it to COAST if it borders water.

		TileRegistry.BiomeType.DESERT:
			return WorldData.TerrainType.DESERT

		TileRegistry.BiomeType.MOUNTAIN_ROCK, \
		TileRegistry.BiomeType.MOUNTAIN_SNOW, \
		TileRegistry.BiomeType.VOLCANIC:
			return WorldData.TerrainType.MOUNTAIN

		TileRegistry.BiomeType.TROPICAL_RAINFOREST, \
		TileRegistry.BiomeType.TEMPERATE_FOREST, \
		TileRegistry.BiomeType.BOREAL_FOREST, \
		TileRegistry.BiomeType.WOODLAND:
			return WorldData.TerrainType.FOREST

		TileRegistry.BiomeType.TUNDRA, \
		TileRegistry.BiomeType.SNOW:
			return WorldData.TerrainType.HILLS if alt > 0.55 else WorldData.TerrainType.PLAINS

		TileRegistry.BiomeType.RIVER:
			return WorldData.TerrainType.RIVER

		TileRegistry.BiomeType.LAKE:
			return WorldData.TerrainType.LAKE

		_:  # GRASSLAND, SAVANNA
			return WorldData.TerrainType.PLAINS
