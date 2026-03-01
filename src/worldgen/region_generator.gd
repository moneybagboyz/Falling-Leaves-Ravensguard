## RegionGenerator — top-level world generation orchestrator.
##
## Call:
##   var world_state: WorldState = RegionGenerator.generate(seed, params)
##
## All passes run in deterministic order. WorldGenData (the transient grid) is
## discarded after generation; only WorldState survives.
class_name RegionGenerator
extends RefCounted

## Primary entry point.
## on_step (optional): called via call_deferred after each pass with
##   (step_label: String, data: WorldGenData) — safe to render a live preview.
static func generate(
		world_seed: int,
		params:     WorldGenParams = null,
		on_step:    Callable       = Callable()
) -> WorldState:

	if params == null:
		params = WorldGenParams.default_params()

	# ── Step 1: Allocate transient grid ──────────────────────────────────────
	var data := WorldGenData.new(params.grid_width, params.grid_height, world_seed)
	if on_step.is_valid(): on_step.call_deferred("1 · Allocating grid", data)

	# ── Step 2: Altitude synthesis (3-layer octave noise + tectonics) ─────────
	_generate_altitude(data, params, world_seed)
	if on_step.is_valid(): on_step.call_deferred("2 · Altitude + Tectonics", data)

	# ── Step 3: Compute climate fields (temperature + precipitation) ─────────
	_generate_climate(data, params, world_seed)
	if on_step.is_valid(): on_step.call_deferred("3 · Climate", data)

	# ── Step 4: Biome classification (Whittaker) ─────────────────────────────
	BiomeClassifier.classify_all(data)
	if on_step.is_valid(): on_step.call_deferred("4 · Biomes", data)

	# ── Step 5: Hydrology (flow rivers + lake basins) ────────────────────────
	Hydrology.process(data, params)
	if on_step.is_valid(): on_step.call_deferred("5 · Hydrology", data)

	# ── Step 6: Terrain classification (biome → terrain + coasts) ───────────
	TerrainClassifier.classify_all(data)
	if on_step.is_valid(): on_step.call_deferred("6 · Terrain", data)

	# ── Step 7: Geology (resource tags + prosperity) ─────────────────────────
	GeologyGenerator.assign(data, world_seed)
	if on_step.is_valid(): on_step.call_deferred("7 · Geology", data)

	# ── Step 8: Settlement scoring (flux-normalized) ─────────────────────────
	SettlementScorer.score_all(data)
	if on_step.is_valid(): on_step.call_deferred("8 · Settlement scoring", data)

	# ── Step 9: Province generation (Lloyd + Dijkstra) ───────────────────────
	ProvinceGenerator.generate(data, world_seed, params)
	if on_step.is_valid(): on_step.call_deferred("9 · Provinces", data)

	# ── Step 10: Settlement placement (hub+spoke) ────────────────────────────
	var settlement_records: Array = SettlementPlacer.place(data, params)
	if on_step.is_valid(): on_step.call_deferred("10 · Settlements", data)

	# ── Step 11: Route network (two-phase Dijkstra) ──────────────────────────
	var route_result: Dictionary = RouteGenerator.build(data, settlement_records, params)
	if on_step.is_valid(): on_step.call_deferred("11 · Routes", data)

	# ── Step 12: History stub ────────────────────────────────────────────────
	var world_state := _build_world_state(
		data, params, world_seed, settlement_records, route_result
	)
	if params.run_history_sim:
		HistorySim.run(world_state, data, world_seed)
	if on_step.is_valid(): on_step.call_deferred("12 · History", data)

	# ── Step 13: Building placement (P3-06) ──────────────────────────────────
	BuildingPlacer.place(world_state, world_seed)
	if on_step.is_valid(): on_step.call_deferred("13 · Buildings", data)

	return world_state


# ── Altitude synthesis ────────────────────────────────────────────────────────
static func _generate_altitude(
		data:   WorldGenData,
		params: WorldGenParams,
		world_seed: int
) -> void:
	var rng := RandomNumberGenerator.new()
	var W: int = data.width
	var H: int = data.height

	# Three FastNoiseLite passes blended.
	var noise_base       := FastNoiseLite.new()
	var noise_detail     := FastNoiseLite.new()
	var noise_ridge      := FastNoiseLite.new()

	rng.seed = world_seed
	noise_base.seed      = rng.randi()
	noise_detail.seed    = rng.randi()
	noise_ridge.seed     = rng.randi()

	noise_base.noise_type        = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	noise_base.frequency         = params.base_freq
	noise_base.fractal_octaves   = params.base_octaves

	noise_detail.noise_type      = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	noise_detail.frequency       = params.detail_freq
	noise_detail.fractal_octaves = params.detail_octaves

	noise_ridge.noise_type       = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	noise_ridge.frequency        = params.ridge_freq
	noise_ridge.fractal_octaves  = params.ridge_octaves

	var wf: float = float(W)
	var hf: float = float(H)

	for y in range(H):
		for x in range(W):
			var xf: float = float(x)
			var yf: float = float(y)

			var base:   float = (noise_base.get_noise_2d(xf, yf)   + 1.0) * 0.5
			var detail: float = (noise_detail.get_noise_2d(xf, yf) + 1.0) * 0.5
			var ridge:  float = (noise_ridge.get_noise_2d(xf, yf)  + 1.0) * 0.5
			ridge = 1.0 - absf(ridge - 0.5) * 2.0

			var raw: float = (
				base   * params.base_weight +
				detail * params.detail_weight +
				ridge  * params.ridge_weight
			)

			# Elliptical island falloff.
			var nx: float = (xf / wf) * 2.0 - 1.0
			var ny: float = (yf / hf) * 2.0 - 1.0
			var dist: float = clampf(sqrt(nx*nx + ny*ny), 0.0, 1.0)
			var falloff: float = 1.0 - pow(dist, params.island_falloff)
			if params.island_mode:
				raw = lerpf(raw, raw * falloff, 0.75)

			data.altitude[y][x] = clampf(raw, 0.0, 1.0)

	# ── Tectonic plate modifier ──────────────────────────────────────────────
	TectonicsGenerator.blend(data, params, world_seed)

	# Derive sea_level from params.sea_ratio.
	var flat: PackedFloat32Array = data.altitude_flat()
	flat.sort()
	var idx: int = clamp(
		int(flat.size() * params.sea_ratio), 0, flat.size() - 1
	)
	data.sea_level = float(flat[idx])


# ── Climate ───────────────────────────────────────────────────────────────────
static func _generate_climate(
		data:   WorldGenData,
		params: WorldGenParams,
		world_seed: int
) -> void:
	var rng := RandomNumberGenerator.new()
	var W: int = data.width
	var H: int = data.height

	var n_temp  := FastNoiseLite.new()
	var n_prec  := FastNoiseLite.new()
	rng.seed = world_seed ^ 0x5A5A5A5A
	n_temp.seed = rng.randi()
	rng.seed = world_seed ^ 0xA5A5A5A5
	n_prec.seed = rng.randi()

	n_temp.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	n_temp.frequency  = params.climate_freq
	n_prec.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	n_prec.frequency  = params.climate_freq

	for y in range(H):
		for x in range(W):
			var xf: float = float(x)
			var yf: float = float(y)
			# Latitude band: cool poles, warm equator (map top=north).
			var lat: float = 1.0 - absf((float(y) / float(H)) * 2.0 - 1.0)   # 0=poles 1=equator
			var alt: float = data.altitude[y][x]

			var t_noise: float = (n_temp.get_noise_2d(xf, yf) + 1.0) * 0.5
			var p_noise: float = (n_prec.get_noise_2d(xf, yf) + 1.0) * 0.5

			var temp: float = clampf(
				lat * 0.6 + t_noise * 0.3 + params.temp_bias - alt * 0.35, 0.0, 1.0
			)
			var prec: float = clampf(
				(1.0 - alt) * 0.3 + p_noise * 0.6 + params.precip_bias, 0.0, 1.0
			)

			data.temperature[y][x]   = temp
			data.precipitation[y][x] = prec

			# Drainage noise (later used by hydrology and geology).
			data.drainage[y][x] = clampf(
				(1.0 - alt) * 0.5 + p_noise * 0.4, 0.0, 1.0
			)

	# ── Orographic rain shadow ────────────────────────────────────────────────
	# Prevailing wind direction: west → east.
	# For each land tile, scan cells directly upwind (to the west).
	# If a mountain barrier stands between the source of moisture and this tile,
	# reduce precipitation proportionally — creating dry leeward rain shadows
	# and leaving windward slopes wet.
	const SHADOW_REACH: int    = 8    # How many tiles upwind to check.
	const SHADOW_STRENGTH: float = 0.52  # How aggressively the barrier blocks rain.

	for y in range(H):
		for x in range(W):
			if data.altitude[y][x] <= data.sea_level:
				continue
			var cell_alt: float  = data.altitude[y][x]
			var max_barrier: float = 0.0
			for dx in range(1, SHADOW_REACH + 1):
				var ux: int = x - dx
				if ux < 0:
					break
				max_barrier = maxf(max_barrier, data.altitude[y][ux])
			# Only cast a shadow when the barrier is meaningfully taller.
			var barrier_excess: float = max_barrier - cell_alt
			if barrier_excess > 0.08:
				var shadow: float = clampf(barrier_excess * SHADOW_STRENGTH, 0.0, 0.55)
				data.precipitation[y][x] = maxf(data.precipitation[y][x] - shadow, 0.0)


# ── WorldState assembly ───────────────────────────────────────────────────────
static func _build_world_state(
		data:               WorldGenData,
		_params:            WorldGenParams,
		world_seed:         int,
		settlement_records: Array,
		route_result:       Dictionary
) -> WorldState:
	var ws := WorldState.new()
	ws.region_id         = "region_%d" % world_seed
	ws.world_seed        = world_seed
	ws.province_names    = data.province_names
	ws.province_adjacency = data.province_adjacency

	# Convert WorldGenData grid to RegionCell dicts.
	for y in range(data.height):
		for x in range(data.width):
			var cell := RegionCell.new()
			cell.cell_id       = RegionCell.key(x, y)
			cell.grid_x        = x
			cell.grid_y        = y
			cell.elevation     = data.altitude[y][x]
			cell.temperature   = data.temperature[y][x]
			cell.precipitation = data.precipitation[y][x]
			cell.drainage      = data.drainage[y][x]
			cell.biome         = data.biome[y][x]
			cell.terrain_type  = data.terrain[y][x]
			cell.prosperity    = data.prosperity[y][x]
			cell.settlement_score = data.settlement_score[y][x]
			cell.is_water      = not data.is_land(x, y)
			cell.is_river      = data.is_river[y][x]
			cell.is_lake       = data.is_lake[y][x]
			cell.province_id   = data.province_id[y][x]

			var geo_key := Vector2i(x, y)
			if data.geology.has(geo_key):
				cell.resource_tags = Array(data.geology[geo_key], TYPE_STRING, "", null)

			# Acreage: 1 km² per tile = 247 acres; simplified for now.
			cell.total_acres   = int(247.0)
			cell.arable_acres  = int(247.0 * cell.prosperity)

			ws.world_tiles[cell.cell_id] = cell.to_dict()

	# Settlement records → SettlementState + EntityRegistry IDs.
	var settlement_ids: Array = []
	var conn_rates: Array     = route_result.get("connectivity_rate", [])

	for i in range(settlement_records.size()):
		var rec: Dictionary = settlement_records[i]
		var ss := SettlementState.new()
		var sid: String = "settlement_%04d" % i
		ss.settlement_id = sid
		ss.cell_id       = RegionCell.key(rec["tile_x"], rec["tile_y"])
		ss.name          = rec["name"]
		ss.tier          = rec["tier"]
		ss.province_id   = str(rec["province_id"])
		ss.tile_x        = rec["tile_x"]
		ss.tile_y        = rec["tile_y"]
		ss.is_hub        = rec["is_hub"]
		ss.connectivity_rate = conn_rates[i] if i < conn_rates.size() else 0.0

		# Seed population by class.
		# Artisans (smiths, millers, bakers) appear from tier 1 upward.
		# Noble fraction stays near 5 % — historical gentry density.
		var total_pop: int = rec["population"]
		if rec["tier"] >= 1:
			ss.population["peasant"]  = int(total_pop * 0.70)
			ss.population["artisan"]  = int(total_pop * 0.15)
			ss.population["merchant"] = int(total_pop * 0.10)
			ss.population["noble"]    = maxi(int(total_pop * 0.05), 1)
		else:
			ss.population["peasant"]  = int(total_pop * 0.88)
			ss.population["merchant"] = int(total_pop * 0.10)
			ss.population["noble"]    = maxi(int(total_pop * 0.02), 1)

		ss.prosperity = data.prosperity[rec["tile_y"]][rec["tile_x"]]
		ss.unrest     = 0.0

		# Seed acreage from the tile's cell data.
		# Territory multiplier scales land area by tier: hamlets control ~2 tiles
		# worth of farmland, metropolises ~18. This is scaffolding until
		# BuildingPlacer (P3-06) replaces it with real cell counts.
		const TIER_TERRITORY_TILES: Array[int] = [2, 6, 10, 14, 18]
		var tx: int = rec["tile_x"]
		var ty: int = rec["tile_y"]
		var cell_key: String = RegionCell.key(tx, ty)
		if ws.world_tiles.has(cell_key):
			var cd: Dictionary  = ws.world_tiles[cell_key]
			var tile_acres: int = cd.get("total_acres", 247)
			var tier_idx: int   = clampi(rec["tier"], 0, TIER_TERRITORY_TILES.size() - 1)
			var t_acres: int    = tile_acres * TIER_TERRITORY_TILES[tier_idx]
			var a_acres: int    = int(float(t_acres) * ss.prosperity)
			ss.acreage = {
				"total_acres":   t_acres,
				"arable_acres":  a_acres,
				"worked_acres":  int(a_acres * 0.6),   # 60 %% worked at start
				"fallow_acres":  int(a_acres * 0.3),   # 30 %% fallow
				"pasture_acres": int(a_acres * 0.05),
				"woodlot_acres": int(float(t_acres - a_acres) * 0.5),
			}

		# Register into WorldState as a live SettlementState (not a dict).
		ws.settlements[sid] = ss
		settlement_ids.append(sid)

	# Route network keyed by settlement_id.
	var raw_routes: Array = route_result.get("routes", [])
	for i in range(raw_routes.size()):
		if i >= settlement_ids.size():
			break
		var sid: String        = settlement_ids[i]
		var edges: Array       = raw_routes[i]
		var route_edges: Array = []
		for edge: Dictionary in edges:
			var to_i: int = edge["to_idx"]
			if to_i >= settlement_ids.size():
				continue
			route_edges.append({
					"to_id": settlement_ids[to_i],
					"cost":  edge["cost"],
					# Serialize Vector2i path as plain [[x,y],...] for JSON safety.
					"path":  _serialize_path(edge.get("path", [])),
				})
		ws.routes[sid] = route_edges

	# Stamp has_road and road_dirs onto every tile that lies on a route path.
	# road_dirs records which edges the route actually crosses ("n","s","e","w"),
	# so SubRegionGenerator draws the road in exactly the right direction rather
	# than guessing from which neighbours also happen to have roads.
	for i in range(raw_routes.size()):
		for edge: Dictionary in raw_routes[i]:
			var path: Array = edge.get("path", [])
			for pi: int in range(path.size()):
				var v: Vector2i = path[pi]
				var cid := "%d,%d" % [v.x, v.y]
				if not ws.world_tiles.has(cid):
					continue
				ws.world_tiles[cid]["has_road"] = true
				var dirs: Array = ws.world_tiles[cid].get("road_dirs", [])
				if pi > 0:
					var d := _edge_dir(v, path[pi - 1])
					if not dirs.has(d):
						dirs.append(d)
				if pi < path.size() - 1:
					var d := _edge_dir(v, path[pi + 1])
					if not dirs.has(d):
						dirs.append(d)
				ws.world_tiles[cid]["road_dirs"] = dirs

	# Spawn persistent NPCs for every settlement (CDDA-style: born once, live forever).
	NpcPoolManager.spawn_all(ws, ws.world_seed)

	return ws


# Converts Array[Vector2i] to Array[[int,int]] so paths survive JSON round-trip.
static func _serialize_path(path: Array) -> Array:
	var out: Array = []
	for v: Vector2i in path:
		out.append([v.x, v.y])
	return out


## Returns the compass letter ("n","s","e","w") for the edge of tile `from`
## that faces toward tile `to`.
static func _edge_dir(from: Vector2i, to: Vector2i) -> String:
	if to.x > from.x: return "e"
	if to.x < from.x: return "w"
	if to.y < from.y: return "n"
	return "s"
