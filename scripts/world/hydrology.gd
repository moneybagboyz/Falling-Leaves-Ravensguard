class_name Hydrology

## Computes river flow accumulation and lake detection from the altitude array.
## Results are written into WorldData.flow, .is_river, .is_lake,
## then _apply_to_biomes() overrides .biome for those tiles.

## A land tile needs at least this much accumulated flow to become a river.
## Lower = more / wider rivers.  Higher = fewer, only major valleys.
const RIVER_FLOW_THRESHOLD: float = 60.0

## Tiles inside a depression are flooded up to this height above the basin minimum.
const LAKE_FILL_HEIGHT: float = 0.026

## A flood-filled basin must be at least this many tiles to count as a lake
## (prevents single-pixel noise depressions from appearing).
const MIN_LAKE_SIZE: int = 4

## Rivers are only placed between SEA_LEVEL and this altitude ceiling
## (avoids rivers appearing on mountain peaks).
const RIVER_ALTITUDE_MAX: float = 0.80


static func process(data: WorldData, params: WorldGenParams = null) -> void:
	var raw_flow := _calculate_flow(data)
	_mark_rivers(data, raw_flow, params)
	_detect_lakes(data, params)
	_apply_to_biomes(data)


# ---------------------------------------------------------------------------
# Step 1 — flow accumulation
# ---------------------------------------------------------------------------

## Returns raw (un-normalised) flow values as a separate 2D array so we can
## threshold them before normalising into data.flow for display.
static func _calculate_flow(data: WorldData) -> Array:
	# Every tile starts with 1 unit of "rainfall".
	var raw: Array = WorldData._make_grid(data.width, data.height, 1.0)

	# Sort tiles high → low by altitude so that when we process a tile we have
	# already accumulated all flow from tiles above it.
	var coords: Array = []
	for y in range(data.height):
		for x in range(data.width):
			coords.append([data.altitude[y][x], x, y])
	coords.sort_custom(func(a, b): return a[0] > b[0])

	for entry in coords:
		var x: int   = entry[1]
		var y: int   = entry[2]
		var e: float = data.altitude[y][x]

		# Find the steepest downhill neighbour (D8 single-flow-direction).
		var best_nx: int   = -1
		var best_ny: int   = -1
		var lowest: float  = e

		for dy in [-1, 0, 1]:
			for dx in [-1, 0, 1]:
				if dx == 0 and dy == 0:
					continue
				var nx: int = x + dx
				var ny: int = y + dy
				if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
					continue
				if data.altitude[ny][nx] < lowest:
					lowest        = data.altitude[ny][nx]
					best_nx = nx
					best_ny = ny

		if best_nx != -1:
			raw[best_ny][best_nx] += raw[y][x]

	# Normalise to 0..1 for display and store in data.flow.
	var max_flow: float = 1.0
	for y in range(data.height):
		for x in range(data.width):
			if raw[y][x] > max_flow:
				max_flow = raw[y][x]

	for y in range(data.height):
		for x in range(data.width):
			data.flow[y][x] = raw[y][x] / max_flow

	return raw


# ---------------------------------------------------------------------------
# Step 2 — river marking
# ---------------------------------------------------------------------------

static func _mark_rivers(data: WorldData, raw_flow: Array, params: WorldGenParams = null) -> void:
	var threshold: float = params.river_threshold if params != null else RIVER_FLOW_THRESHOLD
	for y in range(data.height):
		for x in range(data.width):
			var alt: float = data.altitude[y][x]
			if raw_flow[y][x] >= threshold \
					and alt > data.sea_level \
					and alt < RIVER_ALTITUDE_MAX:
				data.is_river[y][x] = true


# ---------------------------------------------------------------------------
# Step 3 — lake detection (priority flood from local minima)
# ---------------------------------------------------------------------------

static func _detect_lakes(data: WorldData, params: WorldGenParams = null) -> void:
	var fill_h: float = params.lake_fill_depth if params != null else LAKE_FILL_HEIGHT
	# Use 4-directional local minima (stricter than 8-directional — far fewer seeds).
	var CARDINAL: Array[Vector2i] = [Vector2i(-1,0), Vector2i(1,0), Vector2i(0,-1), Vector2i(0,1)]

	for y in range(1, data.height - 1):
		for x in range(1, data.width - 1):
			var alt: float = data.altitude[y][x]

			# Must be well above sea level and not already a river.
			if alt <= data.sea_level + 0.03:
				continue
			if data.is_river[y][x]:
				continue

			# 4-directional strict local minimum: all cardinal neighbours >= alt.
			var is_min: bool = true
			for nb: Vector2i in CARDINAL:
				if data.altitude[y + nb.y][x + nb.x] < alt:
					is_min = false
					break
			if not is_min:
				continue

			# BFS flood-fill up to fill_level.
			# KEY FIX: if the BFS ever reaches sea or the map edge the basin drains
			# naturally — abort immediately. Only enclosed basins become lakes.
			var fill_level: float = alt + fill_h
			var basin: Array[Vector2i] = []
			var visited: Dictionary = {}
			var queue: Array = [Vector2i(x, y)]
			visited[Vector2i(x, y)] = true
			var drains: bool = false

			while queue.size() > 0 and not drains:
				var cur: Vector2i = queue.pop_front()
				basin.append(cur)

				for nb: Vector2i in CARDINAL:
					var nx: int = cur.x + nb.x
					var ny: int = cur.y + nb.y
					# Reached map edge → basin drains off-map.
					if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
						drains = true
						break
					var nv := Vector2i(nx, ny)
					if visited.has(nv):
						continue
					var nalt: float = data.altitude[ny][nx]
					# Reached sea → basin drains to ocean.
					if nalt <= data.sea_level:
						drains = true
						break
					if nalt <= fill_level:
						visited[nv] = true
						queue.append(nv)

			if not drains and basin.size() >= MIN_LAKE_SIZE:
				for t in basin:
					data.is_lake[t.y][t.x] = true


# ---------------------------------------------------------------------------
# Step 4 — bake into biome layer
# ---------------------------------------------------------------------------

static func _apply_to_biomes(data: WorldData) -> void:
	for y in range(data.height):
		for x in range(data.width):
			if data.is_lake[y][x]:
				data.biome[y][x] = TileRegistry.BiomeType.LAKE
			elif data.is_river[y][x]:
				data.biome[y][x] = TileRegistry.BiomeType.RIVER
