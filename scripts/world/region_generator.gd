class_name RegionGenerator

## Generates a RegionData (8×8 tiles) from one world tile.
## Uses the world tile's noise arrays as a DC offset, plus a fine detail
## noise pass at 8× the world frequency.

static func generate(world_data: WorldData, wx: int, wy: int) -> RegionData:
	var r := RegionData.new(wx, wy)

	var world_alt:   float = world_data.altitude[wy][wx]
	var world_biome: int   = world_data.biome[wy][wx]
	var world_terr:  int   = world_data.terrain[wy][wx]
	var world_riv:   bool  = world_data.is_river[wy][wx]
	var world_lk:    bool  = world_data.is_lake[wy][wx]

	# Fine detail noise — unique per world tile
	var detail := FastNoiseLite.new()
	detail.seed            = world_data.world_seed ^ (wy * 73856093 + wx * 19349663)
	detail.noise_type      = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	detail.frequency       = 0.25
	detail.fractal_octaves = 3

	for ry in range(r.height):
		for rx in range(r.width):
			var fx: float = (rx + 0.5) / float(r.width)
			var fy: float = (ry + 0.5) / float(r.height)
			var d: float  = (detail.get_noise_2d(fx * 12.0, fy * 12.0) + 1.0) * 0.5
			var alt: float = clampf(world_alt + (d - 0.5) * 0.20, 0.0, 1.0)
			r.altitude[ry][rx] = alt
			r.biome[ry][rx]    = world_biome
			r.terrain[ry][rx]  = world_terr

	# Overlay hydrology
	if world_lk:
		_fill_lake(r)
	elif world_riv:
		_carve_river(r, world_data, wx, wy)

	# Feature rolls
	var feat_noise := FastNoiseLite.new()
	feat_noise.seed      = world_data.world_seed ^ 0xBEEF ^ (wy * 10007 + wx)
	feat_noise.frequency = 0.60
	for ry in range(r.height):
		for rx in range(r.width):
			r.feature[ry][rx] = _roll_feature(
				r.terrain[ry][rx], feat_noise.get_noise_2d(rx, ry)
			)

	return r


static func _fill_lake(r: RegionData) -> void:
	for ry in range(r.height):
		for rx in range(r.width):
			r.terrain[ry][rx] = WorldData.TerrainType.LAKE
			r.biome[ry][rx]   = TileRegistry.BiomeType.LAKE


static func _carve_river(r: RegionData, data: WorldData, wx: int, wy: int) -> void:
	# Determine which edges the river enters/exits based on adjacent world tiles.
	var entry_edge: int = -1
	var exit_edge:  int = -1
	var adj: Array = [
		[wx,     wy - 1, 0, 2],  # above  → entry top / exit bottom
		[wx + 1, wy,     1, 3],  # right
		[wx,     wy + 1, 2, 0],  # below
		[wx - 1, wy,     3, 1],  # left
	]
	for a in adj:
		var nx: int = a[0]; var ny: int = a[1]
		if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
			continue
		if data.is_river[ny][nx]:
			if entry_edge == -1:
				entry_edge = a[2]
			elif exit_edge == -1:
				exit_edge = a[3]

	if entry_edge == -1:
		# Isolated river tile — diagonal stripe
		for i in range(r.width):
			var ci: int = clampi(i, 0, r.height - 1)
			r.is_river[ci][i] = true
			r.terrain[ci][i]  = WorldData.TerrainType.RIVER
		return

	if exit_edge == -1:
		exit_edge = (entry_edge + 2) % 4  # opposite edge

	var s: int = RegionData.SCALE
	var edge_pts: Array = [
		Vector2i(s / 2, 0),      # top
		Vector2i(s - 1, s / 2),  # right
		Vector2i(s / 2, s - 1),  # bottom
		Vector2i(0,     s / 2),  # left
	]
	var p0: Vector2i = edge_pts[entry_edge]
	var p1: Vector2i = edge_pts[exit_edge]
	var steps: int = maxi(absi(p1.x - p0.x), absi(p1.y - p0.y)) + 1
	for i in range(steps):
		var t: float = float(i) / float(maxi(steps - 1, 1))
		var rx: int = clampi(roundi(lerpf(p0.x, p1.x, t)), 0, s - 1)
		var ry: int = clampi(roundi(lerpf(p0.y, p1.y, t)), 0, s - 1)
		r.is_river[ry][rx] = true
		r.terrain[ry][rx]  = WorldData.TerrainType.RIVER


static func _roll_feature(terrain: int, noise_v: float) -> int:
	match terrain:
		WorldData.TerrainType.FOREST:
			if noise_v > 0.6:  return RegionData.RegionFeature.DENSE_FOREST
		WorldData.TerrainType.MOUNTAIN:
			if noise_v > 0.7:  return RegionData.RegionFeature.MINE_ENTRANCE
		WorldData.TerrainType.PLAINS, WorldData.TerrainType.HILLS:
			if noise_v > 0.82: return RegionData.RegionFeature.RUINS
			if noise_v > 0.72: return RegionData.RegionFeature.CAMP
		WorldData.TerrainType.RIVER, WorldData.TerrainType.COAST:
			if noise_v > 0.5:  return RegionData.RegionFeature.FORD
	return RegionData.RegionFeature.NONE
