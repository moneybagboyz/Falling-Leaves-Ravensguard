## SettlementPlacer — places hub and spoke settlements using province data.
##
## Hub tier is driven by province quality (normalised average score).
## Spokes are placed via adaptive Poisson disk per province.
## Population is seeded from tier-appropriate min/max ranges (exponential curve).
## Final tiers are reassigned by percentile after road connectivity is applied.
##
## Returns a plain Array[Dictionary] — each dict has the fields needed to
## populate a SettlementState. The caller (RegionGenerator) creates the actual
## SettlementState objects and registers them in WorldState.
class_name SettlementPlacer
extends RefCounted

const SPOKE_MIN_SEP:    int = 5
const HUB_SUFFIXES:   PackedStringArray = ["", "bury", "ham", "ton", "wick", "ford", "stead"]
const SPOKE_SUFFIXES: PackedStringArray = ["ham", "ton", "wick", "ford", "stead", "croft", "hollow"]
## Population floor and ceiling per tier (hamlet → metropolis).
## Each step is ≈ 3–4× the previous so higher tiers genuinely need
## to import food from surrounding settlements.
const TIER_POP_MIN: Array[int] = [  50,  150,  700, 2000,  5000]
const TIER_POP_MAX: Array[int] = [ 120,  500, 1200, 4000, 12000]


## Returns Array[Dictionary]. Each dict:
##   { tile_x, tile_y, province_id, tier, name, population, is_hub }
static func place(
		data: WorldGenData,
		params: WorldGenParams
) -> Array:
	var result: Array = []
	var num_provinces: int = data.province_names.size()
	if num_provinces == 0:
		return result

	var rng := RandomNumberGenerator.new()

	# ── Collect tiles and aggregate scores per province ──────────────────────
	var prov_tiles:     Array = []
	var prov_score_sum: Array = []
	prov_tiles.resize(num_provinces)
	prov_score_sum.resize(num_provinces)
	for i in range(num_provinces):
		prov_tiles[i]     = []
		prov_score_sum[i] = 0.0

	for y in range(data.height):
		for x in range(data.width):
			var pid: int = data.province_id[y][x]
			if pid >= 0 and pid < num_provinces:
				var sc: float = data.settlement_score[y][x]
				prov_tiles[pid].append([sc, x, y])
				prov_score_sum[pid] += sc

	for pid in range(num_provinces):
		prov_tiles[pid].sort_custom(func(a: Array, b: Array) -> bool:
			return a[0] > b[0])

	# ── Compute per-province average scores and normalise ────────────────────
	var prov_avg: Array = []
	prov_avg.resize(num_provinces)
	var global_best_pid:  int   = 0
	var global_best_avg:  float = -INF

	for pid in range(num_provinces):
		var cnt: int   = prov_tiles[pid].size()
		var avg: float = prov_score_sum[pid] / maxf(cnt, 1.0)
		prov_avg[pid]  = avg
		if avg > global_best_avg:
			global_best_avg = avg
			global_best_pid = pid

	var sorted_avg: Array = prov_avg.duplicate()
	sorted_avg.sort()
	var min_s: float  = sorted_avg[0]
	var max_s: float  = sorted_avg[sorted_avg.size() - 1]
	var score_range: float = maxf(max_s - min_s, 0.001)

	# ── Place hubs ───────────────────────────────────────────────────────────
	for pid in range(num_provinces):
		if pid >= data.province_capitals.size():
			continue
		var hub: Vector2i = data.province_capitals[pid]

		var norm: float = (prov_avg[pid] - min_s) / score_range
		var tier: int
		if pid == global_best_pid:
			tier = 4   # Metropolis
		elif norm > 0.75:
			tier = 3   # City
		elif norm > 0.50:
			tier = 2   # Town
		else:
			tier = 1   # Village hub

		rng.seed = data.world_seed ^ (pid * 49297 + 1)
		var pop: int = rng.randi_range(TIER_POP_MIN[tier], TIER_POP_MAX[tier])

		rng.seed = data.world_seed ^ (pid * 49297)
		var pname: String = data.province_names[pid].split(" ")[0]
		var hub_name: String = pname + HUB_SUFFIXES[rng.randi() % HUB_SUFFIXES.size()]

		result.append({
			"tile_x":      hub.x,
			"tile_y":      hub.y,
			"province_id": pid,
			"tier":        tier,
			"name":        hub_name,
			"population":  pop,
			"is_hub":      true,
		})

	# ── Place spokes (adaptive Poisson disk per province) ────────────────────
	for pid in range(num_provinces):
		if pid >= data.province_capitals.size():
			continue
		var hub: Vector2i  = data.province_capitals[pid]
		var hub_s: Dictionary = result[pid]  # hub for this province
		var tile_count: int   = prov_tiles[pid].size()
		@warning_ignore("integer_division")
		var spoke_limit: int  = maxi(tile_count / params.tiles_per_settlement, 1)
		var max_spokes: int   = mini(spoke_limit, hub_s["tier"] * 3 + 2)
		var hub_pop: float = maxf(float(hub_s["population"]), 1.0)

		# Randomize-then-sort (FMG lesson) — prevents geographic monopolies.
		rng.seed = data.world_seed ^ (pid * 131071 + 3)
		var working: Array = []
		working.resize(prov_tiles[pid].size())
		for i in range(prov_tiles[pid].size()):
			var t: Array = prov_tiles[pid][i]
			working[i] = [t[0] * rng.randf_range(0.1, 3.0), t[1], t[2]]
		working.sort_custom(func(a: Array, b: Array) -> bool: return a[0] > b[0])

		# Adaptive Poisson disk.
		var placed: Array          = [hub]
		var placed_set: Dictionary = { hub: true }
		var current_sep: int       = SPOKE_MIN_SEP

		while placed.size() - 1 < max_spokes and current_sep >= 2:
			var sep2_cur: int = current_sep * current_sep

			for tile in working:
				if placed.size() - 1 >= max_spokes:
					break
				var cx: int = tile[1]
				var cy: int = tile[2]
				if cx == hub.x and cy == hub.y:
					continue
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

					rng.seed = data.world_seed ^ (cx * 31337 + cy * 73856093)
					var spoke_pop: int = maxi(
						int(float(hub_pop) * rng.randf_range(0.05, 0.30)), 10)

					# Tier from pop-ratio to hub.
					var pop_ratio: float = float(spoke_pop) / hub_pop
					var spoke_tier: int
					if pop_ratio >= 0.5:
						spoke_tier = maxi(hub_s["tier"] - 1, 0)
					elif pop_ratio >= 0.2:
						spoke_tier = maxi(hub_s["tier"] - 2, 0)
					else:
						spoke_tier = 0

					rng.seed = data.world_seed ^ (cx * 31337 + cy * 73856093 + 1)
					var pname: String = data.province_names[pid].split(" ")[0]
					var spoke_name: String = pname.left(4).capitalize() \
						+ SPOKE_SUFFIXES[rng.randi() % SPOKE_SUFFIXES.size()]

					result.append({
						"tile_x":      cx,
						"tile_y":      cy,
						"province_id": pid,
						"tier":        spoke_tier,
						"name":        spoke_name,
						"population":  spoke_pop,
						"is_hub":      false,
					})

			if placed.size() - 1 < max_spokes:
				@warning_ignore("integer_division")
				current_sep = current_sep / 2

	# ── Assign final tiers by population percentile (self-calibrating) ───────
	_assign_tiers_by_percentile(result)
	return result


static func _assign_tiers_by_percentile(settlements: Array) -> void:
	if settlements.is_empty():
		return
	var ranked: Array = settlements.duplicate()
	ranked.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		return a["population"] > b["population"])
	var n: int = ranked.size()
	for rank in range(n):
		var s: Dictionary = ranked[rank]
		var pct: float    = float(rank) / float(n)
		var tier: int
		if pct < 0.02 or rank == 0:
			tier = 4   # Metropolis
		elif pct < 0.12:
			tier = 3   # City
		elif pct < 0.35:
			tier = 2   # Town
		elif pct < 0.65:
			tier = 1   # Village
		else:
			tier = 0   # Hamlet
		s["tier"] = tier
