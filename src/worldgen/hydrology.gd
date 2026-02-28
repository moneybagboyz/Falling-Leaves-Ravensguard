## Hydrology — places rivers and lakes on the generation grid.
##
## Algorithm:
##   1. Collect all land tiles sorted by altitude descending (highest first).
##   2. For each tile, compute flow direction to the lowest adjacent neighbour.
##   3. Accumulate flow counts downstream.
##   4. Tiles whose accumulated flow exceeds RIVER_THRESHOLD become rivers.
##   5. Enclosed low-altitude basins surrounded by higher land become lakes.
##
## Rivers always flow toward lower altitude and terminate at sea/lake/coast.
class_name Hydrology
extends RefCounted

## Drainage count required for a tile to be a river.
const RIVER_THRESHOLD: int = 80
## Minimum basin size (tile count) to become a lake.
const LAKE_MIN_SIZE: int = 6
## Base lake count for a 128×128 map; scales with map area at runtime.
const LAKE_BASE_COUNT: int = 8
## One extra lake per this many tiles above the reference 128×128 area.
const LAKE_AREA_DIVISOR: int = 2048


static func process(data: WorldGenData, _params: WorldGenParams) -> void:
	_flow_rivers(data)
	_place_lakes(data)


# ---------------------------------------------------------------------------
# Rivers — gradient-descent flow accumulation
# ---------------------------------------------------------------------------

static func _flow_rivers(data: WorldGenData) -> void:
	# flow_dir[y * w + x] = flat index of the downhill neighbour, or -1.
	var w := data.width
	var h := data.height
	var n := w * h
	var flow_dir: Array = []
	flow_dir.resize(n)
	flow_dir.fill(-1)

	for y in range(h):
		for x in range(w):
			if not data.is_land(x, y):
				continue
			var best_alt: float = data.altitude[y][x]
			var best_nb: int    = -1
			for dy in [-1, 0, 1]:
				for dx in [-1, 0, 1]:
					if dx == 0 and dy == 0:
						continue
					var nx: int = x + dx
					var ny: int = y + dy
					if nx < 0 or nx >= w or ny < 0 or ny >= h:
						continue
					if data.altitude[ny][nx] < best_alt:
						best_alt = data.altitude[ny][nx]
						best_nb  = ny * w + nx
			flow_dir[y * w + x] = best_nb

	# Accumulate flow count downstream.
	var flow_count: Array = []
	flow_count.resize(n)
	flow_count.fill(0)

	for start in range(n):
		var cur := start
		var visit_limit := n  # guard against cycles (shouldn't happen)
		while cur >= 0 and visit_limit > 0:
			flow_count[cur] += 1
			var nxt: int = flow_dir[cur]
			if nxt < 0:
				break
			# Stop propagating once we leave land (ocean/lake sink).
			@warning_ignore("integer_division")
			var ny: int = int(nxt) / w
			var nx: int = int(nxt) % w
			if not data.is_land(nx, ny):
				break
			cur = nxt
			visit_limit -= 1

	# Mark river tiles — use drainage noise as bonus so high-drainage
	# valleys are more likely to become rivers even at modest flow counts.
	for y in range(h):
		for x in range(w):
			if not data.is_land(x, y):
				continue
			var idx := y * w + x
			var effective: int = flow_count[idx] + int(data.drainage[y][x] * 20.0)
			if effective >= RIVER_THRESHOLD:
				data.is_river[y][x] = true


# ---------------------------------------------------------------------------
# Lakes — flood-fill enclosed basins
# ---------------------------------------------------------------------------

static func _place_lakes(data: WorldGenData) -> void:
	var w := data.width
	var h := data.height
	var visited: Array = []
	visited.resize(w * h)
	visited.fill(false)

	# Scale lake limit with map area so larger maps aren't starved of lakes.
	@warning_ignore("integer_division")
	var lakes_max: int = LAKE_BASE_COUNT + (w * h) / LAKE_AREA_DIVISOR
	var lakes_placed := 0

	# Candidate seeds: low-altitude land tiles not already rivers.
	var candidates: Array = []
	for y in range(h):
		for x in range(w):
			var alt: float = data.altitude[y][x]
			if data.is_land(x, y) and not data.is_river[y][x]:
				if alt < data.sea_level + 0.10:
					candidates.append([alt, x, y])
	candidates.sort_custom(func(a, b): return a[0] < b[0])

	for cand in candidates:
		if lakes_placed >= lakes_max:
			break
		var sx: int = cand[1]
		var sy: int = cand[2]
		var idx: int = sy * w + sx
		if visited[idx]:
			continue

		# BFS flood-fill the basin around this seed.
		var basin: Array = []
		var queue: Array = [Vector2i(sx, sy)]
		var local_visited: Dictionary = {}
		local_visited[Vector2i(sx, sy)] = true
		var touches_sea := false
		var fill_alt: float = data.altitude[sy][sx] + 0.04

		while not queue.is_empty():
			var pos: Vector2i = queue.pop_front()
			basin.append(pos)
			for dy in [-1, 0, 1]:
				for dx in [-1, 0, 1]:
					if dx == 0 and dy == 0:
						continue
					var nx: int = pos.x + dx
					var ny: int = pos.y + dy
					if nx < 0 or nx >= w or ny < 0 or ny >= h:
						continue
					var nb := Vector2i(nx, ny)
					if local_visited.has(nb):
						continue
					var nb_alt: float = data.altitude[ny][nx]
					if nb_alt <= data.sea_level:
						touches_sea = true
						continue
					if nb_alt <= fill_alt:
						local_visited[nb] = true
						queue.append(nb)

		if touches_sea or basin.size() < LAKE_MIN_SIZE:
			# Mark visited so we skip this area.
			for pos in basin:
				visited[pos.y * w + pos.x] = true
			continue

		# Stamp the lake.
		for pos in basin:
			data.is_lake[pos.y][pos.x] = true
			visited[pos.y * w + pos.x] = true
		lakes_placed += 1
