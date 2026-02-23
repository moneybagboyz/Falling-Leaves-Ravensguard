class_name LocalMapGenerator

## Generates a LocalMapData (48×48) from a RegionData.
## Subdivides each 8×8 region tile into 6×6 local tiles and adds
## micro-noise for individual features (trees, boulders, water cells).
## Settlement buildings are overlaid by the caller if needed.

static func generate(world_data: WorldData, region: RegionData) -> LocalMapData:
	var lm := LocalMapData.new(region.world_tile.x, region.world_tile.y)

	var micro := FastNoiseLite.new()
	micro.seed            = world_data.world_seed ^ 0xCAFE \
		^ (region.world_tile.y * 73856 + region.world_tile.x)
	micro.noise_type      = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	micro.frequency       = 0.18
	micro.fractal_octaves = 2

	for ry in range(region.height):
		for rx in range(region.width):
			var base_terr:  int   = region.terrain[ry][rx]
			var base_biome: int   = region.biome[ry][rx]
			var reg_alt:    float = region.altitude[ry][rx]
			var is_riv:     bool  = region.is_river[ry][rx]

			for ly in range(LocalMapData.SCALE):
				for lx in range(LocalMapData.SCALE):
					var gx: int   = rx * LocalMapData.SCALE + lx
					var gy: int   = ry * LocalMapData.SCALE + ly
					var n:  float = micro.get_noise_2d(gx, gy)  # roughly −1..1

					lm.terrain[gy][gx]   = base_terr
					lm.elevation[gy][gx] = int(reg_alt * 10.0)
					lm.feature[gy][gx]   = _roll_feature(base_biome, base_terr, is_riv, n)
					lm.passable[gy][gx]  = _is_passable(base_terr, lm.feature[gy][gx])

	return lm


static func _roll_feature(biome: int, terrain: int, is_riv: bool, n: float) -> int:
	if is_riv or terrain == WorldData.TerrainType.RIVER:
		return LocalMapData.LocalFeature.WATER_SHALLOW

	match terrain:
		WorldData.TerrainType.OCEAN, \
		WorldData.TerrainType.SHALLOW_WATER, \
		WorldData.TerrainType.LAKE:
			return LocalMapData.LocalFeature.WATER_DEEP

		WorldData.TerrainType.FOREST:
			if n > 0.30:  return LocalMapData.LocalFeature.DENSE_TREE
			if n > 0.05:  return LocalMapData.LocalFeature.TREE

		WorldData.TerrainType.MOUNTAIN:
			if n > 0.40:  return LocalMapData.LocalFeature.BOULDER

		WorldData.TerrainType.HILLS:
			if n > 0.55:  return LocalMapData.LocalFeature.BOULDER

	return LocalMapData.LocalFeature.NONE


static func _is_passable(terrain: int, feature: int) -> bool:
	match terrain:
		WorldData.TerrainType.OCEAN, \
		WorldData.TerrainType.SHALLOW_WATER, \
		WorldData.TerrainType.LAKE:
			return false

	match feature:
		LocalMapData.LocalFeature.BOULDER, \
		LocalMapData.LocalFeature.DENSE_TREE, \
		LocalMapData.LocalFeature.WATER_DEEP, \
		LocalMapData.LocalFeature.WALL:
			return false

	return true
