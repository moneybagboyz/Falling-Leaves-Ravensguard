class_name RoadGenerator

## Builds the world road network in two phases, sets settlement.connectivity_rate,
## applies population bonuses, then runs assign_tiers().
##
## Phase 1 — Intra-province: connect each province hub to all its spokes.
##   Guarantees every settlement gets road exposure before inter-province
##   arteries are laid, so spokes accumulate connectivity rather than being
##   bypassed by long-distance hub-to-hub links.
##
## Phase 2 — Inter-province: connect each hub to its 2 nearest neighbours
##   in ADJACENT provinces only (using province_adjacency).
##   Realism: roads follow political corridors, not arbitrary global distance.
##
## Both phases use Dijkstra with terrain movement costs identical to FMG:
##   Plains 1.0 · River/Coast 1.5 · Forest 3.0 · Hills 4.0 · Mountain 9.0

const MOVE_COST: Dictionary = {
	WorldData.TerrainType.PLAINS:        1.0,
	WorldData.TerrainType.RIVER:         1.5,
	WorldData.TerrainType.COAST:         1.5,
	WorldData.TerrainType.FOREST:        3.0,
	WorldData.TerrainType.HILLS:         4.0,
	WorldData.TerrainType.MOUNTAIN:      9.0,
	WorldData.TerrainType.DESERT:        2.5,
	WorldData.TerrainType.OCEAN:         INF,
	WorldData.TerrainType.SHALLOW_WATER: INF,
	WorldData.TerrainType.LAKE:          INF,
}

## Road discount: already-roaded tiles are cheaper to route through,
## so roads naturally merge into shared corridors (like FMG).
const ROAD_DISCOUNT: float = 0.7


# ── Min-heap for Dijkstra ─────────────────────────────────────────────────
class _MinHeap:
	var _h: Array = []

	func push(cost: float, pos: Vector2i) -> void:
		_h.append([cost, pos])
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


static func generate(data: WorldData, settlements: Array) -> void:
	if settlements.is_empty():
		return

	data.road_network.clear()
	var connectivity_hits: Dictionary = {}  # Vector2i -> int

	# ── Identify hub settlements (tile matches province_capitals) ────────────
	# hub_by_province[pid] = Settlement
	var hub_by_province: Dictionary = {}
	for s: Settlement in settlements:
		var pid: int = s.province_id
		if pid < 0 or pid >= data.province_capitals.size():
			continue
		var cap: Vector2i = data.province_capitals[pid]
		if s.tile_x == cap.x and s.tile_y == cap.y:
			hub_by_province[pid] = s

	# ── Phase 1: hub → every spoke in the same province ──────────────────────
	# Group spokes by province
	var spokes_by_province: Dictionary = {}  # pid -> Array[Settlement]
	for s: Settlement in settlements:
		var pid: int = s.province_id
		if not hub_by_province.has(pid):
			continue
		var cap: Vector2i = data.province_capitals[pid]
		if s.tile_x == cap.x and s.tile_y == cap.y:
			continue  # skip the hub itself
		if not spokes_by_province.has(pid):
			spokes_by_province[pid] = []
		spokes_by_province[pid].append(s)

	for pid in hub_by_province:
		var hub: Settlement = hub_by_province[pid]
		var src := Vector2i(hub.tile_x, hub.tile_y)
		if not spokes_by_province.has(pid):
			continue
		for spoke: Settlement in spokes_by_province[pid]:
			var dst := Vector2i(spoke.tile_x, spoke.tile_y)
			var path: Array = _dijkstra_path(data, src, dst)
			_stamp_road(data, path, connectivity_hits)

	# ── Phase 2: hub → 2 nearest hubs in adjacent provinces only ─────────────
	for pid in hub_by_province:
		var hub: Settlement = hub_by_province[pid]
		var src := Vector2i(hub.tile_x, hub.tile_y)

		# Collect hubs of neighbouring provinces from the adjacency map
		var neighbour_hubs: Array = []
		var adj: Dictionary = data.province_adjacency.get(pid, {})
		for npid in adj:
			if hub_by_province.has(npid):
				neighbour_hubs.append(hub_by_province[npid])

		if neighbour_hubs.is_empty():
			continue

		# Sort by Euclidean distance and take the 2 nearest
		neighbour_hubs.sort_custom(func(a: Settlement, b: Settlement) -> bool:
			var da: float = Vector2(hub.tile_x, hub.tile_y).distance_squared_to(Vector2(a.tile_x, a.tile_y))
			var db: float = Vector2(hub.tile_x, hub.tile_y).distance_squared_to(Vector2(b.tile_x, b.tile_y))
			return da < db
		)
		var targets: Array = neighbour_hubs.slice(0, 2)

		for t: Settlement in targets:
			var dst := Vector2i(t.tile_x, t.tile_y)
			var path: Array = _dijkstra_path(data, src, dst)
			_stamp_road(data, path, connectivity_hits)

	# ── Derive connectivity_rate per settlement ───────────────────────────────
	# 1.0 = isolated, +0.25 per road path passing through the tile.
	# A tile with 4 paths gets 2.0 → doubles population.
	for s: Settlement in settlements:
		var key := Vector2i(s.tile_x, s.tile_y)
		s.connectivity_rate = 1.0 + float(connectivity_hits.get(key, 0)) * 0.25
		s.apply_connectivity_bonus()


## Dijkstra shortest path on the world grid between src and dst.
## Uses ROAD_DISCOUNT so subsequent roads prefer existing corridors.
static func _dijkstra_path(data: WorldData, src: Vector2i, dst: Vector2i) -> Array:
	var costs: Dictionary = {src: 0.0}
	var prev:  Dictionary = {}
	var pq := _MinHeap.new()
	pq.push(0.0, src)

	while not pq.is_empty():
		var cur: Array    = pq.pop()
		var cost: float   = cur[0]
		var pos: Vector2i = cur[1]

		if pos == dst:
			break
		if cost > costs.get(pos, INF):
			continue

		for dy in [-1, 0, 1]:
			for dx in [-1, 0, 1]:
				if dx == 0 and dy == 0:
					continue
				var nx: int = pos.x + dx
				var ny: int = pos.y + dy
				if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
					continue
				var t: int = data.terrain[ny][nx]
				var base_cost: float = MOVE_COST.get(t, INF)
				if base_cost >= INF:
					continue  # impassable (water)

				# Existing roads are cheaper to follow — promotes corridor merging
				var nkey := Vector2i(nx, ny)
				if data.road_network.has(nkey):
					base_cost *= ROAD_DISCOUNT

				var step: float = base_cost
				if dx != 0 and dy != 0:
					step *= 1.414  # diagonal penalty

				var new_cost: float = cost + step
				if new_cost < costs.get(nkey, INF):
					costs[nkey] = new_cost
					prev[nkey]  = pos
					pq.push(new_cost, nkey)

	# Reconstruct path src → dst
	var path: Array = []
	var step: Vector2i = dst
	while prev.has(step):
		path.push_front(step)
		step = prev[step]
	if not path.is_empty():
		path.push_front(src)
	return path


## Stamp a path into data.road_network and increment connectivity_hits per tile.
static func _stamp_road(data: WorldData, path: Array, hits: Dictionary) -> void:
	for i in range(path.size() - 1):
		var a: Vector2i = path[i]
		var b: Vector2i = path[i + 1]
		# Forward direction
		if not data.road_network.has(a):
			data.road_network[a] = []
		if not (b in data.road_network[a]):
			data.road_network[a].append(b)
		# Reverse direction (undirected graph)
		if not data.road_network.has(b):
			data.road_network[b] = []
		if not (a in data.road_network[b]):
			data.road_network[b].append(a)
		hits[a] = hits.get(a, 0) + 1
		hits[b] = hits.get(b, 0) + 1


## Assign tiers to all settlements using percentile rank on final population.
## Self-calibrating: every world always produces the same tier distribution
## regardless of how population magnitudes are tuned.
##
## Tier 4 Metropolis — top  2%  (≥1 guaranteed)
## Tier 3 City       — top 12%
## Tier 2 Town       — top 35%
## Tier 1 Village    — top 65%
## Tier 0 Hamlet     — remainder
static func assign_tiers(settlements: Array) -> void:
	if settlements.is_empty():
		return

	# Sort by population descending to get rank order
	var ranked: Array = settlements.duplicate()
	ranked.sort_custom(func(a: Settlement, b: Settlement) -> bool:
		return a.population > b.population
	)

	var n: int = ranked.size()
	for rank in range(n):
		var s: Settlement = ranked[rank]
		var pct: float = float(rank) / float(n)  # 0.0 = highest pop
		if pct < 0.02 or rank == 0:  # top 2%, always at least 1
			s.tier = 4
		elif pct < 0.12:
			s.tier = 3
		elif pct < 0.35:
			s.tier = 2
		elif pct < 0.65:
			s.tier = 1
		else:
			s.tier = 0
