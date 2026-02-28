## SubRegionGenerator — lazily generates a 250×250 region grid for one world tile.
##
## Called by SettlementView the first time the player steps onto a world tile.
## The result is cached in WorldState.region_grids[wt_key] so subsequent visits
## are instant.
##
## Grid layout:
##   • 250 × 250 region cells, keyed by "rx,ry" (0–249 each axis).
##   • Terrain inherits from the parent world tile with gentle noise variation.
##   • Roads are stamped only toward neighbours that also carry roads.
##   • Settlement buildings are clustered around centre (125, 125) at
##     BUILDING_SPREAD spacing, derived from their world-tile offsets.
class_name SubRegionGenerator
extends RefCounted

const REGION_W: int = 250
const REGION_H: int = 250

## Centre of every region in both axes.
const CX: int = 125
const CY: int = 125

## Minor terrain variants per parent terrain type (adds visual variety).
const TERRAIN_VARIANTS: Dictionary = {
	"plains":        ["plains", "plains", "plains", "coast"],
	"coast":         ["coast",  "coast",  "shallow_water"],
	"hills":         ["hills",  "hills",  "plains"],
	"forest":        ["forest", "forest", "plains"],
	"desert":        ["desert", "desert", "plains"],
	"tundra":        ["tundra", "tundra", "plains"],
	"mountain":      ["mountain", "hills"],
	"river":         ["river",   "coast"],
	"shallow_water": ["shallow_water"],
	"ocean":         ["ocean"],
	"lake":          ["lake",   "shallow_water"],
}


## Generate a 250×250 region grid for one world tile.
##
## Parameters
##   wt_data     — world tile dict from WorldState.world_tiles
##   ss          — SettlementState for the settlement on this tile (null if none)
##   world_tiles — full WorldState.world_tiles dict (used for neighbour road checks
##                 and for reading building_id on territory tiles)
##   world_seed  — master seed for determinism
##   wtx / wty   — world tile grid coordinates (used to seed region RNG)
##
## Returns a Dictionary keyed by "rx,ry" (String) → region cell dict.
static func generate(
		wt_data:     Dictionary,
		ss,                        # SettlementState or null
		world_tiles: Dictionary,
		world_seed:  int,
		wtx:         int,
		wty:         int
) -> Dictionary:

	var result: Dictionary = {}

	# Seeded RNG: each world tile gets a unique, deterministic sub-seed.
	var rng := RandomNumberGenerator.new()
	rng.seed = world_seed ^ (wtx * 73856093) ^ (wty * 19349663)

	var base_terrain: String = wt_data.get("terrain_type", "plains")
	var base_prosp:   float  = wt_data.get("prosperity",   0.5)
	var is_water:     bool   = wt_data.get("is_water",     false)
	var has_road:     bool   = wt_data.get("has_road",     false)
	var owner_sid:    String = wt_data.get("owner_settlement_id", "")

	# Gentle noise for terrain micro-variation.
	var noise := FastNoiseLite.new()
	noise.seed      = rng.randi()
	noise.frequency = 0.04

	var variants: Array = TERRAIN_VARIANTS.get(base_terrain, [base_terrain]) as Array

	# ── Fill base cells ────────────────────────────────────────────────────────
	for ry: int in range(REGION_H):
		for rx: int in range(REGION_W):
			var nv:  float = (noise.get_noise_2d(float(rx), float(ry)) + 1.0) * 0.5
			var vi:  int   = int(nv * variants.size()) % variants.size()

			result["%d,%d" % [rx, ry]] = {
				"terrain_type":        variants[vi],
				"is_water":            is_water,
				"prosperity":          base_prosp,
				"owner_settlement_id": owner_sid,
				"building_id":         "",
				"has_road":            false,
				"z_levels":            [0],
			}

	# ── Neighbour terrain lookup (shared by all feature passes) ──────────────
	var n_terrain: String = world_tiles.get("%d,%d" % [wtx,     wty - 1], {}).get("terrain_type", "")
	var s_terrain: String = world_tiles.get("%d,%d" % [wtx,     wty + 1], {}).get("terrain_type", "")
	var w_terrain: String = world_tiles.get("%d,%d" % [wtx - 1, wty    ], {}).get("terrain_type", "")
	var e_terrain: String = world_tiles.get("%d,%d" % [wtx + 1, wty    ], {}).get("terrain_type", "")

	# Second noise layer used by all sub-tile feature passes.
	var feat_noise := FastNoiseLite.new()
	feat_noise.seed      = rng.randi()
	feat_noise.frequency = 0.08

	# ── Shoreline transitions ──────────────────────────────────────────────────
	# When a neighbour world tile is water, feather coast/shallow cells inward.
	# Very close cells become impassable shallow_water; further ones become coast.
	const WATER_TYPES:  PackedStringArray = ["ocean", "lake", "shallow_water", "river"]
	const SHORE_DEPTH:  int = 20
	if not is_water:
		for ry: int in range(REGION_H):
			for rx: int in range(REGION_W):
				var proximity := 0.0
				if n_terrain in WATER_TYPES: proximity = maxf(proximity, 1.0 - float(ry)                  / SHORE_DEPTH)
				if s_terrain in WATER_TYPES: proximity = maxf(proximity, 1.0 - float(REGION_H - 1 - ry)   / SHORE_DEPTH)
				if w_terrain in WATER_TYPES: proximity = maxf(proximity, 1.0 - float(rx)                  / SHORE_DEPTH)
				if e_terrain in WATER_TYPES: proximity = maxf(proximity, 1.0 - float(REGION_W - 1 - rx)   / SHORE_DEPTH)
				if proximity <= 0.0:
					continue
				var ck  := "%d,%d" % [rx, ry]
				var nv: float = (feat_noise.get_noise_2d(float(rx) * 1.3, float(ry) * 1.3) + 1.0) * 0.5
				if nv < proximity * 0.4:
					result[ck]["terrain_type"] = "shallow_water"
					result[ck]["is_water"]     = true
				elif nv < proximity * 0.85:
					result[ck]["terrain_type"] = "coast"

	# ── River corridors ────────────────────────────────────────────────────────
	# Stamp a 7-cell-wide winding channel from any river-bearing neighbour edge
	# toward centre. Same directional logic as roads.  A bridge strip is applied
	# later where the road crosses at (CX, CY).
	const RIVER_HALF: int = 3
	if not is_water:
		var river_n: bool = n_terrain == "river"
		var river_s: bool = s_terrain == "river"
		var river_w: bool = w_terrain == "river"
		var river_e: bool = e_terrain == "river"
		if base_terrain == "river":
			river_n = true; river_s = true; river_w = true; river_e = true

		if river_n or river_s:
			var start_ry: int = 0      if river_n else CY
			var end_ry:   int = CY + 1 if river_n else REGION_H
			if river_n and river_s: start_ry = 0; end_ry = REGION_H
			for ry: int in range(start_ry, end_ry):
				var jitter: int = int((feat_noise.get_noise_2d(99.0, float(ry) * 0.5) + 1.0) * 0.5 * 4.0) - 2
				for w: int in range(-RIVER_HALF, RIVER_HALF + 1):
					var rx: int = CX + w + jitter
					if rx < 0 or rx >= REGION_W: continue
					var ck := "%d,%d" % [rx, ry]
					if result.has(ck):
						result[ck]["terrain_type"] = "river"
						result[ck]["is_water"]     = true

		if river_w or river_e:
			var start_rx: int = 0      if river_w else CX
			var end_rx:   int = CX + 1 if river_w else REGION_W
			if river_w and river_e: start_rx = 0; end_rx = REGION_W
			for rx: int in range(start_rx, end_rx):
				var jitter: int = int((feat_noise.get_noise_2d(float(rx) * 0.5, 199.0) + 1.0) * 0.5 * 4.0) - 2
				for w: int in range(-RIVER_HALF, RIVER_HALF + 1):
					var ry: int = CY + w + jitter
					if ry < 0 or ry >= REGION_H: continue
					var ck := "%d,%d" % [rx, ry]
					if result.has(ck):
						result[ck]["terrain_type"] = "river"
						result[ck]["is_water"]     = true

	# ── Forest fringe ──────────────────────────────────────────────────────────
	# Scatter forest cells near forest-bearing neighbour edges.
	const FOREST_DEPTH: int = 25
	if not is_water and base_terrain != "forest":
		for ry: int in range(REGION_H):
			for rx: int in range(REGION_W):
				var proximity := 0.0
				if n_terrain == "forest": proximity = maxf(proximity, 1.0 - float(ry)                / FOREST_DEPTH)
				if s_terrain == "forest": proximity = maxf(proximity, 1.0 - float(REGION_H - 1 - ry) / FOREST_DEPTH)
				if w_terrain == "forest": proximity = maxf(proximity, 1.0 - float(rx)                / FOREST_DEPTH)
				if e_terrain == "forest": proximity = maxf(proximity, 1.0 - float(REGION_W - 1 - rx) / FOREST_DEPTH)
				if proximity <= 0.0:
					continue
				var ck := "%d,%d" % [rx, ry]
				if result[ck]["is_water"]: continue
				var nv: float = (feat_noise.get_noise_2d(float(rx) * 0.7, float(ry) * 0.7) + 1.0) * 0.5
				if nv < proximity * 0.6:
					result[ck]["terrain_type"] = "forest"

	# ── Rocky outcrops (hills / mountain only) ─────────────────────────────────
	if base_terrain == "hills" or base_terrain == "mountain":
		var rock_noise := FastNoiseLite.new()
		rock_noise.seed      = rng.randi()
		rock_noise.frequency = 0.12
		var rock_thresh: float = 0.72 if base_terrain == "hills" else 0.55
		for ry: int in range(REGION_H):
			for rx: int in range(REGION_W):
				var ck := "%d,%d" % [rx, ry]
				if result[ck]["is_water"]: continue
				var nv: float = (rock_noise.get_noise_2d(float(rx), float(ry)) + 1.0) * 0.5
				if nv > rock_thresh:
					result[ck]["terrain_type"] = "mountain"

	# ── Stamp roads only in directions connected to road-bearing neighbours ────
	if has_road and not is_water:
		var road_n: bool = world_tiles.get("%d,%d" % [wtx,     wty - 1], {}).get("has_road", false)
		var road_s: bool = world_tiles.get("%d,%d" % [wtx,     wty + 1], {}).get("has_road", false)
		var road_w: bool = world_tiles.get("%d,%d" % [wtx - 1, wty    ], {}).get("has_road", false)
		var road_e: bool = world_tiles.get("%d,%d" % [wtx + 1, wty    ], {}).get("has_road", false)

		# If no directional neighbours have roads, this tile is a terminal — draw
		# a short stub outward in all 4 directions so the road visually ends here.
		if not road_n and not road_s and not road_w and not road_e:
			road_n = true; road_s = true; road_w = true; road_e = true

		if road_n:
			for ry: int in range(0, CY + 1):
				result["%d,%d" % [CX, ry]]["has_road"] = true
		if road_s:
			for ry: int in range(CY, REGION_H):
				result["%d,%d" % [CX, ry]]["has_road"] = true
		if road_w:
			for rx: int in range(0, CX + 1):
				result["%d,%d" % [rx, CY]]["has_road"] = true
		if road_e:
			for rx: int in range(CX, REGION_W):
				result["%d,%d" % [rx, CY]]["has_road"] = true

		# ── Inner-city parallel streets (settlement tiles only) ──────────────
		# Two N-S avenues and two E-W avenues on each side of the main cross,
		# limited to a district-sized zone around the centre.
		if ss != null:
			var street_offsets: Array = [12, 24]
			const STREET_EXTENT: int = 40   # half-length perpendicular to each street
			# N-S parallels: vertical lines at CX ± offset
			for off: int in street_offsets:
				for dx: int in [-off, off]:
					var sx: int = CX + dx
					if sx < 0 or sx >= REGION_W:
						continue
					for sry: int in range(maxi(0, CY - STREET_EXTENT), mini(REGION_H, CY + STREET_EXTENT + 1)):
						result["%d,%d" % [sx, sry]]["has_road"] = true
			# E-W parallels: horizontal lines at CY ± offset
			for off: int in street_offsets:
				for dy: int in [-off, off]:
					var sy: int = CY + dy
					if sy < 0 or sy >= REGION_H:
						continue
					for srx: int in range(maxi(0, CX - STREET_EXTENT), mini(REGION_W, CX + STREET_EXTENT + 1)):
						result["%d,%d" % [srx, sy]]["has_road"] = true

	# ── Bridge: clear river water on road cells so roads are always passable ──
	for ry: int in range(REGION_H):
		for rx: int in range(REGION_W):
			var ck := "%d,%d" % [rx, ry]
			var cell: Dictionary = result[ck]
			if cell["has_road"] and cell["is_water"]:
				cell["terrain_type"] = "coast"
				cell["is_water"]     = false

	# ── Place settlement buildings scaled to population and industry ───────────
	# 1. Collect all real buildings from territory, tagged by category/zone.
	# 2. Synthesise extra house plots so residential density reflects population
	#    (1 rendered house ≈ 20 residents; capped at 80 so the grid stays sane).
	# 3. Sort plots by zone priority: trade/civic → storage → production → housing.
	#    This creates organic districts — market near the centre, industry behind
	#    it, residential blocks on the outside. Same approach CDDA uses.
	# 4. Lay out in a compact grid; production plots get PROD_STEP spacing so
	#    smelters/mines feel industrial rather than tightly packed.
	if ss != null and not is_water:
		var open_terrain := "plains" if base_terrain != "desert" else "desert"

		# Building category → zone priority (lower = closer to grid centre).
		const ZONE_ORDER: Dictionary = {
			"trade":          0,
			"civic":          0,
			"storage":        1,
			"production":     2,
			"housing":        3,
			"infrastructure": 3,
		}
		const PLOT_STEP: int  = 1   # buildings packed directly adjacent, no alley gap

		# ── Gather real buildings from territory ──────────────────────────────
		var plots: Array = []
		var house_count: int = 0
		for cid: String in ss.territory_cell_ids:
			var bid: String = world_tiles.get(cid, {}).get("building_id", "")
			if bid == "" or bid == "open_land":
				continue
			var bdef: Dictionary = ContentRegistry.get_content("building", bid)
			var cat: String  = bdef.get("category", "infrastructure")
			var zone: int    = ZONE_ORDER.get(cat, 3)
			plots.append({"cid": cid, "bid": bid, "zone": zone, "prod": cat == "production"})
			if cat == "housing":
				house_count += 1

		# ── Synthesise houses from population data ────────────────────────────
		var total_pop: int = ss.total_population()
		var target_houses: int = ceili(float(total_pop) / 5.0)
		for _h: int in range(maxi(0, target_houses - house_count)):
			plots.append({"cid": "", "bid": "house", "zone": 3, "prod": false})

		# ── Sort so districts cluster: trade → storage → production → housing ─
		plots.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
			return a["zone"] < b["zone"])

		var n: int = plots.size()
		if n > 0:
			var cols: int     = ceili(sqrt(float(n)))
			var rows: int     = ceili(float(n) / float(cols))
			var origin_x: int = CX - (cols * PLOT_STEP) / 2
			var origin_y: int = CY - (rows * PLOT_STEP) / 2

			# Build candidate list, skipping any cell the road already occupies.
			var candidates: Array = []
			for row: int in range(rows):
				for col: int in range(cols):
					var prx: int = clampi(origin_x + col * PLOT_STEP, 0, REGION_W - 1)
					var pry: int = clampi(origin_y + row * PLOT_STEP, 0, REGION_H - 1)
					var ck := "%d,%d" % [prx, pry]
					if result.has(ck) and not result[ck]["has_road"]:
						candidates.append(ck)

			for i: int in range(mini(n, candidates.size())):
				var bk: String = candidates[i]
				var cid: String = plots[i]["cid"]
				if cid != "":
					result[bk]["building_id"]   = world_tiles.get(cid, {}).get("building_id", "")
					result[bk]["z_levels"]      = world_tiles.get(cid, {}).get("z_levels", [0])
					result[bk]["source_wt_key"] = cid
				else:
					result[bk]["building_id"] = "house"
				result[bk]["terrain_type"] = open_terrain

	return result
