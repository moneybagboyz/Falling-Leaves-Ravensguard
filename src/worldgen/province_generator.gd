## ProvinceGenerator — Lloyd relaxation hub placement + Dijkstra flood-fill.
##
## Ported from moneybagboyz/Falling-Leaves-Ravensguard province_generator.gd.
##
## Pass 1 — Lloyd relaxation gives equal-area province hub positions,
##           snapped to the best-scored tile within SNAP_RADIUS.
## Pass 2 — Multi-source Dijkstra flood-fill assigns every land tile to
##           the nearest province (by terrain-weighted cost).
## Pass 3 — Build province adjacency map (used by RouteGenerator).
## Pass 4 — Generate province names (seeded per province).
class_name ProvinceGenerator
extends RefCounted

const SNAP_RADIUS:    int   = 6
const HUB_MIN_SEP:    int   = 6
const COST_MOUNTAIN:  float = 8.0
const COST_RIVER:     float = 2.0
const COST_SLOPE:     float = 12.0

# ── Min-heap ──────────────────────────────────────────────────────────────────
class _MinHeap:
	var _h: Array = []

	func push(cost: float, x: int, y: int, pid: int) -> void:
		_h.append([cost, x, y, pid])
		_bubble_up(_h.size() - 1)

	func pop() -> Array:
		if _h.size() == 1:
			return _h.pop_back()
		var top: Array = _h[0]
		_h[0] = _h.pop_back()
		_bubble_down(0)
		return top

	func is_empty() -> bool:
		return _h.is_empty()

	func _bubble_up(i: int) -> void:
		while i > 0:
			var p: int = (i - 1) >> 1
			if _h[p][0] <= _h[i][0]:
				break
			var t: Variant = _h[p]; _h[p] = _h[i]; _h[i] = t
			i = p

	func _bubble_down(i: int) -> void:
		var n: int = _h.size()
		while true:
			var l: int = (i << 1) + 1
			var r: int = l + 1
			var m: int = i
			if l < n and _h[l][0] < _h[m][0]: m = l
			if r < n and _h[r][0] < _h[m][0]: m = r
			if m == i: break
			var t: Variant = _h[m]; _h[m] = _h[i]; _h[i] = t
			i = m

# ── Procedural name tables ─────────────────────────────────────────────────────
const _PREFIXES: PackedStringArray = [
	"North", "South", "East", "West", "New", "Old", "High", "Low",
	"Grey", "Black", "White", "Red", "Iron", "Stone", "Dark", "Silver",
	"Green", "Amber", "Golden", "Storm",
]
const _ROOTS: PackedStringArray = [
	"march", "vale", "moor", "haven", "fen", "ridge", "dale", "wick",
	"field", "ford", "wood", "hold", "fell", "glen", "mere", "heath",
	"croft", "bourne", "hollow", "peak",
]
const _SUFFIXES: PackedStringArray = [
	"shire", "land", "mark", "reach", "hold", " Province", " Marches", " Reach", "", "",
]


static func generate(data: WorldGenData, seed_val: int, params: WorldGenParams) -> void:
	var num: int = params.resolved_num_provinces()
	# Note: SettlementScorer.score_all() is called by RegionGenerator (Step 8)
	# before this function runs. Do NOT call it again here.

	var rng := RandomNumberGenerator.new()
	rng.seed = seed_val

	var land := data.land_tiles()
	if land.is_empty():
		return

	var hubs: Array = _place_hubs_equal(data, land, num, rng)
	if hubs.is_empty():
		return
	num = hubs.size()

	data.province_capitals.resize(num)
	for i in range(num):
		data.province_capitals[i] = hubs[i]

	_flood_fill_provinces(data, hubs)
	_build_adjacency(data, num)
	_generate_names(data, num, seed_val, rng)


# ── Lloyd relaxation hub placement ────────────────────────────────────────────

static func _place_hubs_equal(
		data: WorldGenData, land: Array, num: int,
		rng: RandomNumberGenerator
) -> Array:
	if land.size() < num:
		return []

	# Seed centroids randomly across land tiles.
	var hubs: Array = []
	var used: Dictionary = {}
	while hubs.size() < num:
		var t: Vector2i = land[rng.randi() % land.size()]
		if not used.has(t):
			used[t] = true
			hubs.append(Vector2(t.x, t.y))

	# 15 iterations of Lloyd relaxation.
	for _iter in range(15):
		var sums_x: Array = []; sums_x.resize(num)
		var sums_y: Array = []; sums_y.resize(num)
		var counts: Array = []; counts.resize(num)
		for i in range(num):
			sums_x[i] = 0.0; sums_y[i] = 0.0; counts[i] = 0

		for tile: Vector2i in land:
			var best_dist: float = INF
			var best_i:    int   = 0
			for i in range(num):
				var dx: float = tile.x - hubs[i].x
				var dy: float = tile.y - hubs[i].y
				var d: float  = dx * dx + dy * dy
				if d < best_dist:
					best_dist = d
					best_i    = i
			sums_x[best_i] += tile.x
			sums_y[best_i] += tile.y
			counts[best_i] += 1

		var max_move_sq: float = 0.0
		for i in range(num):
			if counts[i] > 0:
				var new_x: float = sums_x[i] / counts[i]
				var new_y: float = sums_y[i] / counts[i]
				var mdx: float = new_x - hubs[i].x
				var mdy: float = new_y - hubs[i].y
				max_move_sq = maxf(max_move_sq, mdx * mdx + mdy * mdy)
				hubs[i] = Vector2(new_x, new_y)
			else:
				# Orphaned centroid — re-seed.
				var t: Vector2i = land[rng.randi() % land.size()]
				hubs[i] = Vector2(t.x, t.y)
		if max_move_sq < 0.25:
			break  # early exit — converged

	# Snap each centroid to best-scored land tile within SNAP_RADIUS.
	var snap_r2: float = float(SNAP_RADIUS * SNAP_RADIUS)
	var snap_cell: int = SNAP_RADIUS
	var snap_grid: Dictionary = {}
	for tile: Vector2i in land:
		@warning_ignore("integer_division")
		var key := Vector2i(tile.x / snap_cell, tile.y / snap_cell)
		if not snap_grid.has(key):
			snap_grid[key] = []
		snap_grid[key].append(tile)

	var result:  Array      = []
	var claimed: Dictionary = {}

	for hub in hubs:
		var best_score: float    = -INF
		var best_tile:  Vector2i = Vector2i(int(hub.x), int(hub.y))
		@warning_ignore("integer_division")
		var hgx: int = int(hub.x) / snap_cell
		@warning_ignore("integer_division")
		var hgy: int = int(hub.y) / snap_cell

		for cgy in range(hgy - 1, hgy + 2):
			for cgx in range(hgx - 1, hgx + 2):
				var cell_tiles: Array = snap_grid.get(Vector2i(cgx, cgy), [])
				for tile: Vector2i in cell_tiles:
					if claimed.has(tile):
						continue
					var dx: float = tile.x - hub.x
					var dy: float = tile.y - hub.y
					if dx * dx + dy * dy > snap_r2:
						continue
					var sc: float = data.settlement_score[tile.y][tile.x] \
						* rng.randf_range(0.3, 2.5)
					if sc > best_score:
						best_score = sc
						best_tile  = tile

		# Fallback: expand ring-by-ring until an unclaimed tile is found.
		if best_score == -INF:
			var best_dist: float = INF
			@warning_ignore("integer_division")
			var max_ring: int    = (maxi(data.width, data.height) / snap_cell) + 1
			for ring in range(1, max_ring + 1):
				for cgy in range(hgy - ring, hgy + ring + 1):
					for cgx in range(hgx - ring, hgx + ring + 1):
						if abs(cgx - hgx) < ring and abs(cgy - hgy) < ring:
							continue
						var cell_tiles: Array = snap_grid.get(Vector2i(cgx, cgy), [])
						for tile: Vector2i in cell_tiles:
							if claimed.has(tile):
								continue
							var dx2: float = tile.x - hub.x
							var dy2: float = tile.y - hub.y
							var d: float   = dx2 * dx2 + dy2 * dy2
							if d < best_dist:
								best_dist = d
								best_tile = tile
				if best_dist < INF:
					break

		claimed[best_tile] = true
		result.append(best_tile)

	return result


# ── Dijkstra flood-fill province assignment ───────────────────────────────────

static func _flood_fill_provinces(data: WorldGenData, hubs: Array) -> void:
	var num: int = hubs.size()
	var costs: Array = data._make_grid(INF)
	var pq := _MinHeap.new()

	for i in range(num):
		var tx: int = hubs[i].x
		var ty: int = hubs[i].y
		costs[ty][tx]          = 0.0
		data.province_id[ty][tx] = i
		pq.push(0.0, tx, ty, i)

	while not pq.is_empty():
		var cur: Array = pq.pop()
		var c: float   = cur[0]
		var cx: int    = cur[1]
		var cy: int    = cur[2]
		var pid: int   = cur[3]

		if c > costs[cy][cx]:
			continue  # stale entry

		for dy in [-1, 0, 1]:
			for dx in [-1, 0, 1]:
				if dx == 0 and dy == 0:
					continue
				var nx: int = cx + dx
				var ny: int = cy + dy
				if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
					continue
				if data.altitude[ny][nx] <= data.sea_level:
					continue

				var edge: float = 1.0
				if data.terrain[ny][nx] == "mountain":
					edge += COST_MOUNTAIN
				if data.is_river[ny][nx]:
					edge += COST_RIVER
				edge += absf(data.altitude[ny][nx] - data.altitude[cy][cx]) * COST_SLOPE

				var new_c: float = c + edge
				if new_c < costs[ny][nx]:
					costs[ny][nx]            = new_c
					data.province_id[ny][nx] = pid
					pq.push(new_c, nx, ny, pid)


# ── Build adjacency ───────────────────────────────────────────────────────────

static func _build_adjacency(data: WorldGenData, _num: int) -> void:
	for y in range(data.height):
		for x in range(data.width):
			var pid: int = data.province_id[y][x]
			if pid < 0:
				continue
			if not data.province_adjacency.has(pid):
				data.province_adjacency[pid] = {}
			for dy in [-1, 0, 1]:
				for dx in [-1, 0, 1]:
					var nx: int = x + dx
					var ny: int = y + dy
					if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
						continue
					var npid: int = data.province_id[ny][nx]
					if npid != pid and npid >= 0:
						data.province_adjacency[pid][npid] = true


# ── Province name generation ──────────────────────────────────────────────────

static func _generate_names(
		data: WorldGenData, num: int, base_seed: int,
		rng: RandomNumberGenerator
) -> void:
	data.province_names.resize(num)
	for i in range(num):
		rng.seed = base_seed + i * 31337
		var prefix: String = _PREFIXES[rng.randi() % _PREFIXES.size()]
		var root:   String = _ROOTS[rng.randi()   % _ROOTS.size()]
		var suffix: String = _SUFFIXES[rng.randi() % _SUFFIXES.size()]
		data.province_names[i] = prefix + root.capitalize() + suffix
