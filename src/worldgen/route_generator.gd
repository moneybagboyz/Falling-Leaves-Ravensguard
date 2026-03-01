## RouteGenerator — two-phase Dijkstra road network.
##
## Phase 1: Hub → all spokes within the same province.
## Phase 2: Hub → two nearest hub tiles in each adjacent province.
##
## Outputs into WorldState.routes as:
##   routes[settlement_idx] := Array[Dictionary{to_idx, cost}]
##
## Also records a connectivity_rate float per settlement (fraction of
## expected linkage) so economy simulation can use it.
class_name RouteGenerator
extends RefCounted

const ROAD_DISCOUNT: float = 0.7   # movement cost ×0.7 along existing roads
const INF_COST: float      = 9000.0   # any terrain cost >= this is impassable


## settlement_records: Array[Dictionary] as produced by SettlementPlacer.place()
## Returns Dictionary:
##   "routes": Array[Array[Dictionary{to_idx, cost}]]  — one per settlement
##   "connectivity_rate": Array[float]                  — one per settlement
static func build(
		data:               WorldGenData,
		settlement_records: Array,
		_params:            WorldGenParams
) -> Dictionary:

	var n: int = settlement_records.size()
	# Adjacency list: routes[i] = Array[{to_idx, cost}]
	var routes: Array = []
	routes.resize(n)
	for i in range(n):
		routes[i] = []

	if n == 0:
		return { "routes": routes, "connectivity_rate": [] }

	# Build lookup: Vector2i → settlement_idx
	var tile_to_idx: Dictionary = {}
	for i in range(n):
		var s: Dictionary  = settlement_records[i]
		tile_to_idx[Vector2i(s["tile_x"], s["tile_y"])] = i

	# Province → hub idx and spoke idxs
	var num_provinces: int       = data.province_names.size()
	var province_hub: Array      = []   # Array[int]  hub idx per province (or -1)
	var province_spokes: Array   = []   # Array[Array[int]] spoke idxs per province
	province_hub.resize(num_provinces)
	province_spokes.resize(num_provinces)
	for i in range(num_provinces):
		province_hub[i]    = -1
		province_spokes[i] = []

	for i in range(n):
		var s: Dictionary  = settlement_records[i]
		var pid: int       = s["province_id"]
		if pid < 0 or pid >= num_provinces:
			continue
		if s["is_hub"]:
			province_hub[pid] = i
		else:
			province_spokes[pid].append(i)

	# Phase 1: Hub → spokes within province
	for pid in range(num_provinces):
		var hub_idx: int = province_hub[pid]
		if hub_idx < 0:
			continue
		var hs: Dictionary = settlement_records[hub_idx]
		var origin := Vector2i(hs["tile_x"], hs["tile_y"])

		for spoke_idx in province_spokes[pid]:
			var ss: Dictionary = settlement_records[spoke_idx]
			var target := Vector2i(ss["tile_x"], ss["tile_y"])
			var result: Dictionary = _dijkstra(data, origin, target, null, ROAD_DISCOUNT)
			if result["cost"] < INF_COST:
				var path: Array = result["path"]
				routes[hub_idx].append({ "to_idx": spoke_idx, "cost": result["cost"], "path": path })
				routes[spoke_idx].append({ "to_idx": hub_idx,   "cost": result["cost"], "path": path })

	# Phase 2: Hub → two nearest hubs in each adjacent province
	for pid in range(num_provinces):
		var hub_idx: int = province_hub[pid]
		if hub_idx < 0:
			continue
		var hs: Dictionary = settlement_records[hub_idx]
		var origin := Vector2i(hs["tile_x"], hs["tile_y"])

		if not data.province_adjacency.has(pid):
			continue
		var adj_pids: Array = data.province_adjacency[pid].keys()

		# Sort adjacent provinces by euclidean hub distance, connect 2 nearest.
		var adj_sorted: Array = []
		for adj_pid: int in adj_pids:
			var ahub: int = province_hub[adj_pid]
			if ahub < 0:
				continue
			var as_dict: Dictionary = settlement_records[ahub]
			var dx: float = float(as_dict["tile_x"] - hs["tile_x"])
			var dy: float = float(as_dict["tile_y"] - hs["tile_y"])
			adj_sorted.append([dx * dx + dy * dy, adj_pid, ahub])
		adj_sorted.sort_custom(func(a: Array, b: Array) -> bool: return a[0] < b[0])

		var connected: int = 0
		for entry in adj_sorted:
			if connected >= 2:
				break
			var adj_hub_idx: int  = entry[2]
			# Skip if already linked.
			var already: bool = false
			for edge: Dictionary in routes[hub_idx]:
				if edge["to_idx"] == adj_hub_idx:
					already = true
					break
			if already:
				connected += 1
				continue
			var ats: Dictionary = settlement_records[adj_hub_idx]
			var target := Vector2i(ats["tile_x"], ats["tile_y"])
			var result: Dictionary = _dijkstra(data, origin, target, null, ROAD_DISCOUNT)
			if result["cost"] < INF_COST:
				var path: Array = result["path"]
				routes[hub_idx].append({ "to_idx": adj_hub_idx, "cost": result["cost"], "path": path })
				routes[adj_hub_idx].append({ "to_idx": hub_idx,  "cost": result["cost"], "path": path })
				connected += 1

	# Compute connectivity_rate per settlement: edges_count / expected_degree.
	# Expected deg: hubs=3, spokes=1 (one hub link).
	var connectivity_rate: Array = []
	connectivity_rate.resize(n)
	for i in range(n):
		var s: Dictionary  = settlement_records[i]
		var expected: float = 3.0 if s["is_hub"] else 1.0
		connectivity_rate[i] = clampf(float(routes[i].size()) / expected, 0.0, 1.0)

	return {
		"routes":            routes,
		"connectivity_rate": connectivity_rate,
	}


## Terrain-weighted Dijkstra. Returns { "cost": float, "path": Array[Vector2i] }.
## existing_roads is optional (Dictionary Vector2i → true) for road discounting.
static func _dijkstra(
		data:           WorldGenData,
		origin:         Vector2i,
		target:         Vector2i,
		existing_roads, # Dictionary or null
		road_discount:  float
) -> Dictionary:
	if origin == target:
		return { "cost": 0.0, "path": [origin] }

	var W: int = data.width
	var H: int = data.height

	# Flat distance array.
	var dist_flat: PackedFloat32Array
	dist_flat.resize(W * H)
	dist_flat.fill(INF_COST)
	var oi: int = origin.y * W + origin.x
	dist_flat[oi] = 0.0

	# Parent tracker for path reconstruction (-1 = none).
	var parent: PackedInt32Array
	parent.resize(W * H)
	parent.fill(-1)

	# Min-heap via sorted array of [cost, x, y].
	var heap: Array = [[0.0, origin.x, origin.y]]

	var DIRS: Array = [Vector2i(1,0), Vector2i(-1,0), Vector2i(0,1), Vector2i(0,-1)]
	var reached: bool = false

	while not heap.is_empty():
		var cur: Array  = heap[0]
		heap.remove_at(0)
		var cx: int     = cur[1]
		var cy: int     = cur[2]
		var cd: float   = cur[0]

		if cx == target.x and cy == target.y:
			reached = true
			break

		var ci: int = cy * W + cx
		if cd > dist_flat[ci]:
			continue

		for dir: Vector2i in DIRS:
			var nx: int = cx + dir.x
			var ny: int = cy + dir.y
			if nx < 0 or ny < 0 or nx >= W or ny >= H:
				continue

			var terrain: String   = data.terrain[ny][nx]
			var base_cost: float  = TerrainClassifier.move_cost(terrain)
			if base_cost >= INF_COST:
				continue   # water blocks routes

			var road_mod: float   = 1.0
			if existing_roads != null:
				var nv := Vector2i(nx, ny)
				if existing_roads.has(nv):
					road_mod = road_discount

			var nd: float = cd + base_cost * road_mod
			var ni: int   = ny * W + nx
			if nd < dist_flat[ni]:
				dist_flat[ni] = nd
				parent[ni]    = ci
				# Insert sorted (cheap linear insert).
				var inserted: bool = false
				for hi: int in range(heap.size()):
					if heap[hi][0] > nd:
						heap.insert(hi, [nd, nx, ny])
						inserted = true
						break
				if not inserted:
					heap.append([nd, nx, ny])

	if not reached:
		return { "cost": INF_COST, "path": [] }

	# Reconstruct path by walking parent pointers from target → origin.
	var path: Array[Vector2i] = []
	@warning_ignore("integer_division")
	var path_ci: int = target.y * W + target.x
	while path_ci >= 0:
		@warning_ignore("integer_division")
		path.append(Vector2i(path_ci % W, path_ci / W))
		path_ci = parent[path_ci]
	path.reverse()

	return { "cost": dist_flat[target.y * W + target.x], "path": path }
