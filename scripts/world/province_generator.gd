class_name ProvinceGenerator

## Two-tier hierarchical settlement placement.
## Pass 1 — Lloyd relaxation picks equal-area hub positions, snapped to best-scored tile.
## Pass 2 — Dijkstra flood-fill claims province tiles from each hub.
## Pass 3 — per-province Poisson picks spoke (hamlet/village) positions.

const HUB_MIN_SEP:   int   = 6     ## minimum tile gap between province capitals
const SPOKE_MIN_SEP: int   = 6     ## minimum tile gap between spoke sites
const COST_MOUNTAIN: float = 8.0
const COST_RIVER:    float = 2.0
const COST_SLOPE:    float = 12.0

# ── Procedural name parts ──────────────────────────────────────────────────
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


# ── Min-heap for Dijkstra ──────────────────────────────────────────────────
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


# ── Main entry points ──────────────────────────────────────────────────────

## Scores tiles, places hub seeds via Poisson, then flood-fills provinces.
## Also builds province_adjacency, province_names, and province_capitals.
static func generate(data: WorldData, s: int, params: WorldGenParams = null) -> void:
	var num: int = 20
	if params != null:
		num = params.num_provinces

	# Score every land tile first so hub placement favours high-value positions.
	score_settlement_sites(data)

	# Place hub seats via Lloyd relaxation (equal-area) with score-biased snapping.
	var rng := RandomNumberGenerator.new()
	rng.seed = s
	var hubs: Array = _place_hubs_equal(data, num, rng)
	if hubs.is_empty():
		return
	num = hubs.size()

	# Store hub positions as province capitals.
	data.province_capitals.resize(num)
	for i in range(num):
		data.province_capitals[i] = hubs[i]

	# Dijkstra multi-source flood-fill from hub seats.
	var costs: Array = WorldData._make_grid(data.width, data.height, INF)
	var pq := _MinHeap.new()

	for i in range(num):
		var tx: int = hubs[i].x
		var ty: int = hubs[i].y
		costs[ty][tx] = 0.0
		data.province_id[ty][tx] = i
		pq.push(0.0, tx, ty, i)

	while not pq.is_empty():
		var cur: Array = pq.pop()
		var c:   float = cur[0]
		var cx:  int   = cur[1]
		var cy:  int   = cur[2]
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
				var t: int = data.terrain[ny][nx]
				if t == WorldData.TerrainType.MOUNTAIN:
					edge += COST_MOUNTAIN
				if data.is_river[ny][nx]:
					edge += COST_RIVER
				edge += absf(data.altitude[ny][nx] - data.altitude[cy][cx]) * COST_SLOPE

				var new_c: float = c + edge
				if new_c < costs[ny][nx]:
					costs[ny][nx] = new_c
					data.province_id[ny][nx] = pid
					pq.push(new_c, nx, ny, pid)

	# Build adjacency cache.
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

	# Generate province names (seeded per province for reproducibility).
	data.province_names.resize(num)
	for i in range(num):
		rng.seed = s + i * 31337
		var prefix: String = _PREFIXES[rng.randi() % _PREFIXES.size()]
		var root:   String = _ROOTS[rng.randi() % _ROOTS.size()]
		var suffix: String = _SUFFIXES[rng.randi() % _SUFFIXES.size()]
		data.province_names[i] = prefix + root.capitalize() + suffix


## Greedily place hub positions on highest-scored land tiles (Poisson-disk).
## Kept for reference; generate() now calls _place_hubs_equal() instead.
static func _place_hubs(data: WorldData, num: int, min_sep: int) -> Array:
	# Collect scored land tiles sorted by score descending.
	var candidates: Array = []
	for y in range(data.height):
		for x in range(data.width):
			if data.altitude[y][x] > data.sea_level and data.settlement_score[y][x] > 0.0:
				candidates.append([data.settlement_score[y][x], x, y])
	candidates.sort_custom(func(a: Array, b: Array) -> bool: return a[0] > b[0])

	var hubs: Array = []
	var sep2: int = min_sep * min_sep

	for cand in candidates:
		if hubs.size() >= num:
			break
		var cx: int = cand[1]
		var cy: int = cand[2]
		var ok: bool = true
		for h: Vector2i in hubs:
			var ddx: int = cx - h.x
			var ddy: int = cy - h.y
			if ddx * ddx + ddy * ddy < sep2:
				ok = false
				break
		if ok:
			hubs.append(Vector2i(cx, cy))

	return hubs


## Equal-area province seeding via Lloyd relaxation.
## Centroids are snapped to the best-scored land tile within SNAP_RADIUS,
## so provinces are roughly equal in size but hubs still prefer good terrain.
static func _place_hubs_equal(data: WorldData, num: int, rng: RandomNumberGenerator) -> Array:
	const SNAP_RADIUS: int = 6
	var snap_r2: float     = SNAP_RADIUS * SNAP_RADIUS

	# ── Step 1: Collect all land tiles ───────────────────────────────────
	var land_tiles: Array = []
	for y in range(data.height):
		for x in range(data.width):
			if data.altitude[y][x] > data.sea_level:
				land_tiles.append(Vector2i(x, y))

	if land_tiles.size() < num:
		return []

	# ── Step 2: Random initial seeds spread across land tiles ────────────
	var hubs: Array     = []
	var used: Dictionary = {}
	while hubs.size() < num:
		var t: Vector2i = land_tiles[rng.randi() % land_tiles.size()]
		if not used.has(t):
			used[t] = true
			hubs.append(Vector2(t.x, t.y))

	# ── Step 3: Lloyd relaxation (15 iterations) ─────────────────────────
	# Each iteration moves every centroid to the mean position of all land
	# tiles closer to it than to any other centroid.
	for _iter in range(15):
		var sums_x: Array = []; sums_x.resize(num)
		var sums_y: Array = []; sums_y.resize(num)
		var counts: Array = []; counts.resize(num)
		for i in range(num):
			sums_x[i] = 0.0; sums_y[i] = 0.0; counts[i] = 0

		for tile in land_tiles:
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
				# Orphaned centroid — re-seed randomly on land
				var t: Vector2i = land_tiles[rng.randi() % land_tiles.size()]
				hubs[i] = Vector2(t.x, t.y)
		# Early-exit: all centroids moved less than 0.5 tiles — converged.
		if max_move_sq < 0.25:
			break

	# ── Step 4: Snap each centroid to best-scored tile within SNAP_RADIUS ─
	# Centroids may fall on water after relaxation; snapping also lets hubs
	# prefer fertile land without drifting far from their equal-area position.
	# Build a spatial grid (cell = SNAP_RADIUS) so each hub only checks nearby
	# cells instead of all land_tiles — reduces O(hubs × tiles) to O(hubs × k).
	var snap_cell: int = SNAP_RADIUS
	var snap_grid: Dictionary = {}
	for tile in land_tiles:
		var key := Vector2i(tile.x / snap_cell, tile.y / snap_cell)
		if not snap_grid.has(key):
			snap_grid[key] = []
		snap_grid[key].append(tile)

	var result:  Array      = []
	var claimed: Dictionary = {}

	for hub in hubs:
		var best_score: float   = -INF
		var best_tile:  Vector2i = Vector2i(int(hub.x), int(hub.y))
		var hgx: int = int(hub.x) / snap_cell
		var hgy: int = int(hub.y) / snap_cell

		for cgy in range(hgy - 1, hgy + 2):
			for cgx in range(hgx - 1, hgx + 2):
				var cell_tiles: Array = snap_grid.get(Vector2i(cgx, cgy), [])
				for tile in cell_tiles:
					if claimed.has(tile):
						continue
					var dx: float = tile.x - hub.x
					var dy: float = tile.y - hub.y
					if dx * dx + dy * dy > snap_r2:
						continue
					# Randomize score so mediocre-terrain provinces don't always snap
					# to the same high-score edge tiles near a neighbouring hotspot.
					# σ≈0.8 spread (range 0.3–2.5) matches FMG's gauss(1,3,...) intent.
					var sc: float = data.settlement_score[tile.y][tile.x] * rng.randf_range(0.3, 2.5)
					if sc > best_score:
						best_score = sc
						best_tile  = tile

		# Fallback: radius had no unclaimed candidates — nearest unclaimed land tile.
		# Expand outward cell-ring by cell-ring until a candidate is found.
		if best_score == -INF:
			var best_dist: float = INF
			var max_ring: int = (maxi(data.width, data.height) / snap_cell) + 1
			for ring in range(1, max_ring + 1):
				for cgy in range(hgy - ring, hgy + ring + 1):
					for cgx in range(hgx - ring, hgx + ring + 1):
						if abs(cgx - hgx) < ring and abs(cgy - hgy) < ring:
							continue  # inner cells already searched
						var cell_tiles: Array = snap_grid.get(Vector2i(cgx, cgy), [])
						for tile in cell_tiles:
							if claimed.has(tile):
								continue
							var dx: float = tile.x - hub.x
							var dy: float = tile.y - hub.y
							var d: float  = dx * dx + dy * dy
							if d < best_dist:
								best_dist = d
								best_tile = tile
				if best_dist < INF:
					break  # found a candidate in this ring — stop expanding

		claimed[best_tile] = true
		result.append(best_tile)

	return result


## Places hub settlements (tier driven by province quality) and fills each
## province with spokes proportional to its area.
## Hub tier: Metropolis for the globally best province; City/Town/Village for
## others based on normalised average settlement score.
## Spoke tier: Town (≤2 tiles from hub), Village (≤4), Hamlet (5+).
## Spoke count: province_area / tiles_per_settlement, capped at hub_tier*3+2.
static func place_settlements(data: WorldData, params: WorldGenParams = null) -> Array:
	var tiles_per_settlement: int = 12
	if params != null:
		tiles_per_settlement = params.tiles_per_settlement

	var result: Array = []
	if data.province_names.is_empty():
		return result

	var num_provinces: int = data.province_names.size()
	var rng := RandomNumberGenerator.new()
	const HUB_SUFFIXES:   PackedStringArray = ["", "bury", "ham", "ton", "wick", "ford", "stead"]
	const SPOKE_SUFFIXES: PackedStringArray = ["ham", "ton", "wick", "ford", "stead", "croft", "hollow"]

	# ── Collect province tile lists + aggregate scores ───────────────────
	var prov_tiles: Array = []
	prov_tiles.resize(num_provinces)
	var prov_score_sum: Array = []
	prov_score_sum.resize(num_provinces)
	for i in range(num_provinces):
		prov_tiles[i] = []
		prov_score_sum[i] = 0.0

	for y in range(data.height):
		for x in range(data.width):
			var pid: int = data.province_id[y][x]
			if pid >= 0 and pid < num_provinces:
				var sc: float = data.settlement_score[y][x]
				prov_tiles[pid].append([sc, x, y])
				prov_score_sum[pid] += sc

	for pid in range(num_provinces):
		prov_tiles[pid].sort_custom(func(a: Array, b: Array) -> bool: return a[0] > b[0])

	# ── Hub tier per province ─────────────────────────────────────────
	var prov_avg: Array = []
	prov_avg.resize(num_provinces)
	var global_best_pid: int = 0
	var global_best_avg: float = -INF
	for pid in range(num_provinces):
		var cnt: int = prov_tiles[pid].size()
		var avg: float = prov_score_sum[pid] / maxf(cnt, 1.0)
		prov_avg[pid] = avg
		if avg > global_best_avg:
			global_best_avg = avg
			global_best_pid = pid

	# Normalise avg scores to 0-1 range across all provinces.
	var sorted_avg: Array = prov_avg.duplicate()
	sorted_avg.sort()
	var min_s: float = sorted_avg[0]
	var max_s: float = sorted_avg[sorted_avg.size() - 1]
	var score_range: float = maxf(max_s - min_s, 0.001)

	# ── Place hub settlements ────────────────────────────────────────
	for pid in range(num_provinces):
		if pid >= data.province_capitals.size():
			continue
		var hub: Vector2i = data.province_capitals[pid]
		var s := Settlement.new()
		s.id          = result.size()
		s.province_id = pid

		var norm: float = (prov_avg[pid] - min_s) / score_range
		if pid == global_best_pid:
			s.tier = 4   # Metropolis — dominant province capital
		elif norm > 0.75:
			s.tier = 3   # City
		elif norm > 0.50:
			s.tier = 2   # Town
		else:
			s.tier = 1   # Village hub

		# Population from score: capitals get 1.5× the local suitability (FMG lesson).
		# Must be set BEFORE initialize() so _init_population() distributes correctly.
		rng.seed = data.world_seed ^ (pid * 49297 + 1)
		s.population = maxi(int(data.settlement_score[hub.y][hub.x] * 1.5 * rng.randf_range(0.5, 3.0) * 4), 50)
		s.initialize(hub.x, hub.y, pid, data)
		rng.seed = data.world_seed ^ (pid * 49297)
		var pname: String = data.province_names[pid].split(" ")[0]
		s.name = pname + HUB_SUFFIXES[rng.randi() % HUB_SUFFIXES.size()]
		result.append(s)

	# ── Fill spokes: randomize-then-sort + adaptive spacing + pop-ratio tiers ───
	# FMG lessons applied:
	#   • Randomize-then-sort: jitter each tile’s score with a random multiplier
	#     before sorting so geographic score clusters don’t monopolise placement.
	#   • Adaptive spacing: if the quota isn’t met, halve sep and retry rather
	#     than leaving provinces under-populated.
	#   • Population continuous: spoke.population = score × rand; tier follows
	#     from pop-ratio to hub (not hard-coded Chebyshev distance).
	for pid in range(num_provinces):
		if pid >= data.province_capitals.size():
			continue
		var hub: Vector2i     = data.province_capitals[pid]
		var hub_s: Settlement = result[pid]
		var tile_count: int   = prov_tiles[pid].size()
		var max_spokes: int   = mini(
			maxi(tile_count / tiles_per_settlement, 1),
			hub_s.tier * 3 + 2
		)
		var hub_pop: float = maxf(float(hub_s.population), 1.0)

		# ── Randomize-then-sort (FMG: score × gauss before rank) ────────────
		# Multiply each tile’s score by rand(0.1, 3.0) then sort descending.
		# High-quality tiles still tend toward the front, but any habitable
		# tile anywhere in the province can be promoted.
		rng.seed = data.world_seed ^ (pid * 131071 + 3)
		var working: Array = []
		working.resize(prov_tiles[pid].size())
		for i in range(prov_tiles[pid].size()):
			var t: Array = prov_tiles[pid][i]
			working[i] = [t[0] * rng.randf_range(0.1, 3.0), t[1], t[2]]
		working.sort_custom(func(a: Array, b: Array) -> bool: return a[0] > b[0])

		# ── Adaptive Poisson (FMG: reduce spacing on failure) ─────────────
		var placed: Array          = [hub]
		var placed_set: Dictionary = { hub: true }  # O(1) duplicate check
		var current_sep: int = SPOKE_MIN_SEP

		while placed.size() - 1 < max_spokes and current_sep >= 2:
			var sep2_cur: int = current_sep * current_sep

			for tile in working:
				if placed.size() - 1 >= max_spokes:
					break
				var cx: int = tile[1]
				var cy: int = tile[2]
				if cx == hub.x and cy == hub.y:
					continue
				# O(1) duplicate check via Dictionary set
				var pos := Vector2i(cx, cy)
				if placed_set.has(pos):
					continue
				var ok: bool = true
				for p: Vector2i in placed:
					var ddx: int = cx - p.x
					var ddy: int = cy - p.y
					if ddx * ddx + ddy * ddy < sep2_cur:
						ok = false
						break
				if ok:
					placed.append(pos)
					placed_set[pos] = true

					# ── Population from score (FMG: pop = s[cell]/5 × gauss) ───
					var raw_score: float = data.settlement_score[cy][cx]
					rng.seed = data.world_seed ^ (cx * 31337 + cy * 73856093)
					var spoke_pop: int = maxi(int(raw_score * rng.randf_range(0.4, 2.0) * 3.0), 10)

					# ── Tier from pop-ratio to hub (FMG: tier-by-percentile) ────
					# Replaces hard-coded Chebyshev distance tiers.
					var pop_ratio: float = float(spoke_pop) / hub_pop
					var spoke_tier: int
					if pop_ratio >= 0.5:
						spoke_tier = maxi(hub_s.tier - 1, 0)
					elif pop_ratio >= 0.2:
						spoke_tier = maxi(hub_s.tier - 2, 0)
					else:
						spoke_tier = 0

					var s := Settlement.new()
					s.id          = result.size()
					s.province_id = pid
					s.tier        = spoke_tier
					s.population  = spoke_pop
					s.initialize(cx, cy, pid, data)
					rng.seed = data.world_seed ^ (cx * 31337 + cy * 73856093 + 1)
					var pname: String = data.province_names[pid].split(" ")[0]
					s.name = pname.left(4).capitalize() + SPOKE_SUFFIXES[rng.randi() % SPOKE_SUFFIXES.size()]
					result.append(s)

			# Quota not met — halve sep and pass over the list again.
			if placed.size() - 1 < max_spokes:
				current_sep = current_sep / 2

	return result


## Scores each land tile by how suitable it is for a settlement.
## Inspired by Azgaar FMG's rankCells():
##   - Biome bonuses from terrain scan (modified from flat model)
##   - River/water bonus is flux-normalised so a big river in a dry world
##     scores much higher than a trickle in a wet world (FMG lesson 5)
##   - Elevation penalty: high peaks are progressively less desirable
##   - Estuary bonus when a river reaches the coast
static func score_settlement_sites(data: WorldData) -> void:
	# ── Pre-compute drainage statistics for normalisation ────────────────
	var max_drainage: float = 0.001
	var total_drain:  float = 0.0
	var drain_count:  int   = 0
	for y in range(data.height):
		for x in range(data.width):
			if data.altitude[y][x] > data.sea_level:
				var d: float = data.drainage[y][x]
				if d > max_drainage:
					max_drainage = d
				total_drain += d
				drain_count  += 1
	var mean_drainage: float = total_drain / maxf(drain_count, 1.0)
	# Elevation range for penalty normalisation.
	var max_alt: float = 1.0
	for y in range(data.height):
		for x in range(data.width):
			if data.altitude[y][x] > max_alt:
				max_alt = data.altitude[y][x]
	var alt_range: float = maxf(max_alt - data.sea_level, 0.001)

	for y in range(data.height):
		for x in range(data.width):
			if data.altitude[y][x] <= data.sea_level:
				data.settlement_score[y][x] = 0.0
				continue

			var score: float = 0.0

			# ── Terrain scan (1-tile radius) ──────────────────────────────
			# Reduced from 2-tile (25 tiles) to 1-tile (9 tiles) to prevent
			# plains clusters from dominating the score map by up to 100 pts.
			for dy in range(-1, 2):
				for dx in range(-1, 2):
					var nx: int = x + dx
					var ny: int = y + dy
					if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
						continue
					match data.terrain[ny][nx]:
						WorldData.TerrainType.PLAINS:   score += 4.0
						WorldData.TerrainType.FOREST:   score += 1.5
						WorldData.TerrainType.HILLS:    score += 2.0
						WorldData.TerrainType.MOUNTAIN: score += 2.0  # mining
						WorldData.TerrainType.COAST:    score += 3.0
						WorldData.TerrainType.RIVER:    score += 2.0  # local agriculture

			# ── Flux-normalised river bonus (FMG lesson) ─────────────────────
			# Relative to the world’s rivers; a great river in a dry world
			# scores much higher than a trickle in a well-watered one.
			var cell_drain: float = data.drainage[y][x]
			if cell_drain > 0.0:
				var norm_drain: float = minf(cell_drain / max_drainage, 1.0)
				score += norm_drain * 8.0    # reduced from 15.0 — prevents river valleys monopolising hubs
				# Extra bonus if drainage is above the world mean (confluence effect)
				if cell_drain > mean_drainage:
					score += ((cell_drain - mean_drainage) / max_drainage) * 4.0  # reduced from 8.0

			# ── Estuary bonus (river tile adjacent to coast) ──────────────────
			if data.is_river[y][x] and data.terrain[y][x] == WorldData.TerrainType.COAST:
				score += 6.0   # reduced from 12.0 — estuaries remain attractive but not dominant

			# ── Elevation penalty (FMG: -(height-50)/5 analogue) ──────────────
			# Low-lying land near sea level is most valuable; high peaks penalised.
			var rel_alt: float = (data.altitude[y][x] - data.sea_level) / alt_range
			score -= rel_alt * 10.0

			data.settlement_score[y][x] = maxf(score, 0.0)
