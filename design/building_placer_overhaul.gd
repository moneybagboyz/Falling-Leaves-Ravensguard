## BuildingPlacer — tile-driven extraction + population-driven processing placement.
##
## OVERHAUL: Buildings fall into two fundamentally different categories:
##
## EXTRACTION BUILDINGS (tile-driven — farm_plot, ore_mine, lumber_camp, charcoal_camp, clay_pit, fishery):
##   count = max(1, floor(matching_territory_tiles / EXTRACTION_TILES_PER_BUILDING[bid]))
##   A farm is a piece of land. It exists because the land is there, not because
##   someone decided to staff it. Population fills the labor slots — understaffed
##   plots yield less, but the plots don't disappear. A farming settlement with
##   50 fertile tiles gets ~25 farm_plots whether it has 80 or 800 people.
##   This is the correct behaviour: land constrains extraction, population constrains yield.
##
## PROCESSING / SERVICE BUILDINGS (population-driven):
##   Artisan workshops, merchant buildings, granary, well, house.
##   count = floor(class_pop × ratio / workers_per_building)
##   A smithy requires skilled labor. Without enough artisans, there is no smithy.
##   These scale naturally with settlement population and class composition.
##
## ARTISAN WORKSHOP POOL:
##   All artisan capacity is placed as ARTISAN_WORKSHOP_DEFAULT (smithy).
##   _initial_workshop_split() seeds the type mix at worldgen based on
##   territory tile counts (ore tiles → more smelters, always some grain_mills).
##   GovernorAI calls retool_workshops(ss, split) to reassign slots at any time.
##
## COMPANION CHANGE REQUIRED in SubRegionGenerator:
##   Replace the territory-cell building scan with a read from ss.buildings:
##
##     # OLD — scans territory tiles for building_id (caps at cell count):
##     for cid in ss.territory_cell_ids:
##         var bid = world_tiles.get(cid, {}).get("building_id", "")
##
##     # NEW — reads the authoritative building list directly:
##     for bid in ss.buildings:
##         var bdef = ContentRegistry.get_content("building", bid)
##         ...
##
## farmstead is intentionally absent — it is a player-constructable building,
## not a worldgen-placed one.
##
## COMPANION CHANGE REQUIRED in SubRegionGenerator:
##   Replace the territory-cell building scan with a read from ss.buildings:
##
##     # OLD — scans territory tiles for building_id (caps at cell count):
##     for cid in ss.territory_cell_ids:
##         var bid = world_tiles.get(cid, {}).get("building_id", "")
##
##     # NEW — reads the authoritative building list directly:
##     for bid in ss.buildings:
##         var bdef = ContentRegistry.get_content("building", bid)
##         ...
##
## farmstead is intentionally absent — it is a player-constructable building,
## not a worldgen-placed one.

class_name BuildingPlacer
extends RefCounted


# ── Population class workforce pools ─────────────────────────────────────────

## Labor-days per head per tick from population_classes/*.json.
## Documented here for reference — NOT used in building-count math.
## ProductionLedger uses these directly when computing output per tick.
## Placement uses class headcount (ss.population) as the pool, not labor_days.
const CLASS_LABOR_DAYS: Dictionary = {
	"peasant":  0.8,
	"artisan":  0.6,
	"merchant": 0.2,
	"noble":    0.0,   # non-laborer — no buildings
}

## Maps each building to the population class that staffs its labor slots.
## Extraction buildings are tile-driven at worldgen; peasant class still fills
## their labor slots at runtime. Processing/service buildings are population-driven.
const BUILDING_WORKER_CLASS: Dictionary = {
	# Extraction — tile-driven placement, peasant labor fills slots
	"farm_plot":     "peasant",
	"ore_mine":      "peasant",
	"lumber_camp":   "peasant",
	"charcoal_camp": "peasant",
	"clay_pit":      "peasant",
	"fishery":       "peasant",
	# Storage — population-ratio driven
	"granary":       "peasant",
	# Artisan — valid governor-assignable workshop types.
	# BuildingPlacer places all artisan capacity as ARTISAN_WORKSHOP_DEFAULT.
	# GovernorAI calls retool_workshops() to reassign slots at runtime.
	"smithy":        "artisan",
	"grain_mill":    "artisan",
	"smelter":       "artisan",
	# Merchant — trade and hospitality
	"inn":           "merchant",
	"market_stall":  "merchant",
	"market":        "merchant",   # also formula-gated by MARKET_BASE_TIER
}


# ── Artisan workshop pool ────────────────────────────────────────────────────────

## Fraction of artisan headcount that runs a workshop.
## Previously split as smithy(0.10) + grain_mill(0.06) + smelter(0.07) = 0.23.
## The smithy/mill/smelter mix is now set by _initial_workshop_split() at worldgen
## and can be updated any time via retool_workshops().
const ARTISAN_WORKSHOP_RATIO:   float  = 0.23
const ARTISAN_WORKSHOP_DEFAULT: String = "smithy" # fallback type; also used when retool receives an empty split

## Valid reassignment targets for GovernorAI.retool_workshops().
## Value = tier_min the settlement must meet before that type is allowed.
const ARTISAN_WORKSHOP_TYPES: Dictionary = {
	"smithy":     0,
	"grain_mill": 0,
	"smelter":    1,  # processes all ore types; requires tier 1+ — never assign in a hamlet
}


# ── Tile-driven extraction buildings ─────────────────────────────────────────

## Extraction buildings whose count is determined by territory tile composition,
## not by population. Order here is also the placement order.
const EXTRACTION_BUILDINGS: Array[String] = [
	"farm_plot", "ore_mine", "lumber_camp", "charcoal_camp", "clay_pit", "fishery",
]

## Resource tags on world tiles that contribute to each extraction building's count.
## A tile contributes if it has ANY of the listed tags.
const EXTRACTION_TILE_TAGS: Dictionary = {
	"farm_plot":     ["fertile", "farmland"],
	"ore_mine":      ["iron", "copper", "tin", "gold", "silver", "coal", "stone", "marble", "gems", "lead"],
	"lumber_camp":   ["wood", "forest"],
	"charcoal_camp": ["wood", "forest"],
	"clay_pit":      ["wetland", "clay", "sedimentary"],
	"fishery":       ["coastal", "river", "lake"],
}

## How many matching territory tiles are required per building of that type.
## Lower = denser land use. Tuning parameters — adjust to taste.
const EXTRACTION_TILES_PER_BUILDING: Dictionary = {
	"farm_plot":     2,   # 1 farm per 2 fertile tiles
	"ore_mine":      3,   # 1 mine per 3 ore-tagged tiles
	"lumber_camp":   4,   # 1 camp per 4 forest tiles
	"charcoal_camp": 6,   # 1 charcoal camp per 6 forest tiles (less common; uses slash/coppice)
	"clay_pit":      2,   # 1 pit per 2 wetland tiles
	"fishery":       2,   # 1 fishery per 2 water-adjacent tiles
}

## One well per this many residents (rounded up). Wells have 0 max_workers so
## they use a population formula instead of a ratio.
const WELL_PER_POP: int = 200

## market (price-discovery hub) is formula-scaled, not ratio-scaled.
## One market per tier once MARKET_BASE_TIER is reached, +1 per tier above.
const MARKET_BASE_TIER: int = 3


# ── Class building ratios ─────────────────────────────────────────────────────

## Per-class allocation ratios. Flat across all tiers — the class headcount
## already encodes tier progression (artisans first appear at tier 1,
## merchants at tier 1, nobles at tier 2, so lower-tier settlements get fewer
## of those building types without any explicit tier gating).
##
## count = floor(class_pop * ratio / workers_needed_per_building)
##
## Ratios represent the fraction of the class's headcount dedicated to that
## building type, and are tuning parameters — adjust to taste.
## They intentionally don't sum to 1.0; the remainder represents the class
## working in fields, homes, or the acreage system.
const CLASS_BUILDING_RATIOS: Dictionary = {
	"peasant": {
		# Extraction buildings (farm_plot, ore_mine, lumber_camp, charcoal_camp,
		# clay_pit, fishery) are tile-driven — no ratio entries here.
		# granary (1w): storage buffer scaled to population
		# T0(98p)→1, T1(368p)→5, T2(945p)→14, T3(2450p)→36
		"granary": 0.015,
	},
	"artisan": {
		# Artisan workshops are now a unified pool — see ARTISAN_WORKSHOP_RATIO.
		# GovernorAI calls retool_workshops(ss, split) to set the smithy/mill/smelter mix.
		# No individual ratios here.
	},
	"merchant": {
		# inn (3w): T0(9m)→0, large-hamlet(12m+ratio≥0.25)→1, T1(33m)→1, T2(95m)→3, T3(300m)→12
		# NOTE: raise to 0.25 if you want inns at every hamlet.
		"inn":          0.12,
		# market_stall (2w): T0→0, T1(33m)→1, T2(95m)→3, T3(300m)→12
		"market_stall": 0.08,
		# market is formula-based (MARKET_BASE_TIER), ratio entry is unused
		# but present to keep market in the merchant class for readability.
		"market":       0.00,
	},
}

## Placement order — ensures merchant/civic buildings fill before artisan craft,
## artisan craft before peasant extraction, extraction before storage.
## Both the placement loop and SubRegionGenerator use this ordering.
## Placement and spatial layout order.
## Extraction buildings appear here for SubRegionGenerator zone ordering;
## their counts are set by the tile-driven loop, not the ratio loop.
const ZONE_ORDER: Array[String] = [
	"inn", "market_stall", "market",
	"artisan_workshop",                # unified pool — all slots placed as smithy, governor retypes
	"ore_mine", "farm_plot", "lumber_camp", "charcoal_camp", "clay_pit", "fishery",
	"granary",
	"well",
]


## Zone order used by SubRegionGenerator when laying out districts.
## Passed through to ss so the sub-region renderer can cluster by type
## without re-querying ContentRegistry for every building.
const ZONE_PRIORITY: Dictionary = {
	"civic":          0,
	"trade":          0,
	"production":     1,
	"storage":        2,
	"infrastructure": 3,
	"housing":        4,
}

## Starter market goods seeded at placement (multiplied by tier + 1).
const MARKET_STARTER: Dictionary = {
	"wheat_bushel": 30.0,
	"timber_log":    8.0,
	"coin":         20.0,
}

## Territory (Chebyshev) radius by tier — unchanged, used for acreage math.
const TIER_RADIUS: Array[int] = [1, 2, 3, 4, 5]


# ── Entry point ───────────────────────────────────────────────────────────────

## Run building placement for every settlement in world_state.
## Modifies world_state in-place: ss.buildings, ss.labor_slots,
## ss.housing_slots, ss.market_inventory, ss.territory_cell_ids,
## and owner_settlement_id on claimed world tiles.
static func place(world_state: WorldState, world_seed: int) -> void:
	for sid: String in world_state.settlements:
		var ss: SettlementState = world_state.get_settlement(sid)
		if ss == null:
			push_warning("BuildingPlacer: settlement '%s' is null — skipping." % sid)
			continue
		_place_settlement(world_state, ss, world_seed)
	place_bandit_camps(world_state, world_seed)


# ── Per-settlement placement ──────────────────────────────────────────────────

static func _place_settlement(
		world_state: WorldState,
		ss:          SettlementState,
		world_seed:  int
) -> void:

	var tier:   int = clampi(ss.tier, 0, TIER_RADIUS.size() - 1)
	var radius: int = TIER_RADIUS[tier]
	var tx:     int = ss.tile_x
	var ty:     int = ss.tile_y

	# ── 1. Claim territory cells (land ownership + acreage math, unchanged) ──
	var territory: Array[String] = []
	for cx: int in range(tx - radius, tx + radius + 1):
		for cy: int in range(ty - radius, ty + radius + 1):
			var cid := "%d,%d" % [cx, cy]
			if not world_state.world_tiles.has(cid):
				continue
			var cell: Dictionary = world_state.world_tiles[cid]
			if cell.get("is_water", true):
				continue
			var owner: String = cell.get("owner_settlement_id", "")
			if owner != "" and owner != ss.settlement_id:
				continue
			territory.append(cid)
			world_state.world_tiles[cid]["owner_settlement_id"] = ss.settlement_id

	ss.territory_cell_ids.assign(territory)

	# ── 2. Count resource tags across ALL territory cells ────────────────────
	# Extraction buildings use these counts; processing buildings remain
	# population-driven. Anchor cell is included in territory so its tags count.
	var territory_tag_counts: Dictionary = {}
	for cid: String in territory:
		var cell: Dictionary = world_state.world_tiles.get(cid, {})
		for tag: String in cell.get("resource_tags", []):
			territory_tag_counts[tag] = territory_tag_counts.get(tag, 0) + 1

	var anchor_cid := "%d,%d" % [tx, ty]

	# ── 3. Compute per-class workforce pools (processing buildings) ───────────
	# Nobles have labor_days = 0.0 — they produce no workforce pool.
	var peasant_pop:  int = ss.population.get("peasant",  0)
	var artisan_pop:  int = ss.population.get("artisan",  0)
	var merchant_pop: int = ss.population.get("merchant", 0)

	var class_pools: Dictionary = {
		"peasant":  peasant_pop,
		"artisan":  artisan_pop,
		"merchant": merchant_pop,
	}

	ss.buildings.clear()
	var labor_slots:   Array = []
	var housing_slots: Array = []

	# ── 4. Place extraction buildings (tile-driven) ───────────────────────────
	# count = max(1, floor(matching_tiles / tiles_per_building)) when tiles > 0.
	# Population fills labor slots; understaffed plots yield less but still exist.
	for bid: String in EXTRACTION_BUILDINGS:
		var bdef: Dictionary = ContentRegistry.get_content("building", bid)
		if bdef.is_empty():
			push_warning("BuildingPlacer: unknown extraction building '%s' — skipping." % bid)
			continue
		var matching_tiles: int = 0
		for tag: String in EXTRACTION_TILE_TAGS.get(bid, []):
			matching_tiles += territory_tag_counts.get(tag, 0)
		if matching_tiles == 0:
			continue   # no matching terrain — naturally zero, no artificial gate
		var tiles_per: int = maxi(1, EXTRACTION_TILES_PER_BUILDING.get(bid, 1))
		var count: int = maxi(1, floori(float(matching_tiles) / float(tiles_per)))
		for _i: int in count:
			_append_building(bid, bdef, anchor_cid, ss.buildings, labor_slots, housing_slots)

	# ── 5. Place processing and service buildings (population-driven) ─────────
	# Extraction buildings in ZONE_ORDER are skipped here — already placed above.
	for bid: String in ZONE_ORDER:
		if bid in EXTRACTION_BUILDINGS:
			continue   # tile-driven — handled in step 4

		# ── artisan workshop pool ────────────────────────────────────────────
		if bid == "artisan_workshop":
			var wdef: Dictionary = ContentRegistry.get_content("building", ARTISAN_WORKSHOP_DEFAULT)
			if not wdef.is_empty():
				var workers_per_slot: int = maxi(1, int(wdef.get("max_workers", 1)))
				var workshop_count: int = floori(float(artisan_pop) * ARTISAN_WORKSHOP_RATIO / float(workers_per_slot))
				for _w: int in workshop_count:
					_append_building(ARTISAN_WORKSHOP_DEFAULT, wdef, anchor_cid, ss.buildings, labor_slots, housing_slots)
			continue

		if not BUILDING_WORKER_CLASS.has(bid):
			continue

		var bdef: Dictionary = ContentRegistry.get_content("building", bid)
		if bdef.is_empty():
			push_warning("BuildingPlacer: unknown building '%s' — skipping." % bid)
			continue

		var workers_needed: int = int(bdef.get("max_workers", 0))

		# ── market: formula-based ────────────────────────────────────────────
		if bid == "market":
			if tier < MARKET_BASE_TIER:
				continue
			var market_count: int = (tier - MARKET_BASE_TIER) + 1
			for _m: int in market_count:
				_append_building(bid, bdef, anchor_cid, ss.buildings, labor_slots, housing_slots)
			continue

		# ── class-pool ratio formula ─────────────────────────────────────────
		var worker_class: String = BUILDING_WORKER_CLASS[bid]
		var class_pop:    int    = class_pools.get(worker_class, 0)
		var ratio:        float  = float(CLASS_BUILDING_RATIOS.get(worker_class, {}).get(bid, 0.0))

		if workers_needed <= 0 or ratio <= 0.0:
			continue

		var count: int = floori(float(class_pop) * ratio / float(workers_needed))
		for _i: int in count:
			_append_building(bid, bdef, anchor_cid, ss.buildings, labor_slots, housing_slots)

	# ── 5c. Apply initial workshop type split ────────────────────────────────
	# Uses territory_tag_counts (full tile picture) instead of anchor cell only.
	retool_workshops(ss, _initial_workshop_split(territory_tag_counts, tier))

	# ── 4b. Wells — civic infrastructure, count driven by population ──────────
	var well_def:   Dictionary = ContentRegistry.get_content("building", "well")
	var well_count: int        = ceili(float(ss.total_population()) / float(WELL_PER_POP))
	for _w: int in well_count:
		_append_building("well", well_def, anchor_cid, ss.buildings, labor_slots, housing_slots)

	# ── 5. Houses — population-driven, independent of workforce ───────────────
	# Housing demand is a headcount problem, not a skilled-labour problem.
	# Houses are appended after production buildings so the ratio budget
	# never starves a smithy to make room for a house.
	var house_def:     Dictionary = ContentRegistry.get_content("building", "house")
	var housing_cap:   int        = maxi(1, int(house_def.get("housing_capacity", 4)))
	var houses_needed: int        = ceili(float(ss.total_population()) / float(housing_cap))

	for _h: int in houses_needed:
		_append_building("house", house_def, anchor_cid, ss.buildings, labor_slots, housing_slots)

	ss.labor_slots   = labor_slots
	ss.housing_slots = housing_slots

	# ── 6. Seed market inventory ──────────────────────────────────────────────
	var tier_mult: float  = float(tier + 1)
	var market:    Dictionary = {}
	for good: String in MARKET_STARTER:
		market[good] = MARKET_STARTER[good] * tier_mult
	ss.market_inventory = market


# ── Bandit camp placement ─────────────────────────────────────────────────────

## For each tier-1+ settlement, place 1–2 bandit camps on unclaimed non-water
## cells just outside the territory radius. Camps are never placed on
## settlement-owned land.
static func place_bandit_camps(world_state: WorldState, world_seed: int) -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = world_seed ^ 0xBAD517

	for sid: String in world_state.settlements:
		var ss: SettlementState = world_state.get_settlement(sid)
		if ss == null or ss.tier < 1:
			continue

		var radius: int = TIER_RADIUS[clampi(ss.tier, 0, TIER_RADIUS.size() - 1)]
		var tx:     int = ss.tile_x
		var ty:     int = ss.tile_y
		var ring_r: int = radius + 1

		var candidates: Array[String] = []
		for cx: int in range(tx - ring_r - 1, tx + ring_r + 2):
			for cy: int in range(ty - ring_r - 1, ty + ring_r + 2):
				var dist: int = maxi(absi(cx - tx), absi(cy - ty))
				if dist < ring_r or dist > ring_r + 1:
					continue
				var cid := "%d,%d" % [cx, cy]
				if not world_state.world_tiles.has(cid):
					continue
				var cell: Dictionary = world_state.world_tiles[cid]
				if cell.get("is_water",            true):  continue
				if cell.get("owner_settlement_id", "") != "": continue
				if cell.get("building_id",         "") != "": continue
				candidates.append(cid)

		if candidates.is_empty():
			continue

		# Shuffle candidates.
		for i: int in range(candidates.size() - 1, 0, -1):
			var j: int  = rng.randi_range(0, i)
			var tmp: String = candidates[i]
			candidates[i]   = candidates[j]
			candidates[j]   = tmp

		var camp_def:   Dictionary = ContentRegistry.get_content("building", "bandit_camp")
		var g_min:      int        = int(camp_def.get("group_size_min", 4))
		var g_max:      int        = int(camp_def.get("group_size_max", 12))
		var camp_count: int        = 1 if ss.tier < 3 else 2
		var placed:     int        = 0

		for cid: String in candidates:
			if placed >= camp_count:
				break
			world_state.world_tiles[cid]["building_id"]        = "bandit_camp"
			world_state.world_tiles[cid]["hostile"]            = true
			world_state.world_tiles[cid]["bandit_group_size"]  = rng.randi_range(g_min, g_max)
			world_state.world_tiles[cid]["bandit_gear_tier"]   = camp_def.get("gear_tier", "ragged")
			placed += 1


# ── Helpers ───────────────────────────────────────────────────────────────────

## Shared helper: appends one instance of a building to all output arrays.
## Extracted to avoid duplicating the labor/housing slot accumulation logic.
static func _append_building(
		bid:           String,
		bdef:          Dictionary,
		anchor_cid:    String,
		buildings:     Array,
		labor_slots:   Array,
		housing_slots: Array
) -> void:
	buildings.append(bid)

	for slot_tmpl: Dictionary in bdef.get("labor_slots", []):
		var count: int = int(slot_tmpl.get("count", 1))
		for _k: int in count:
			labor_slots.append({
				"slot_id":        slot_tmpl.get("slot_id",        "worker"),
				"building_id":    bid,
				"cell_id":        anchor_cid,
				"wage_per_day":   slot_tmpl.get("wage_per_day",   1),
				"skill_required": slot_tmpl.get("skill_required", ""),
				"is_filled":      false,
				"worker_id":      "",
			})

	var cap: int = int(bdef.get("housing_capacity", 0))
	if cap > 0:
		housing_slots.append({
			"building_id": bid,
			"cell_id":     anchor_cid,
			"capacity":    cap,
		})


## Returns an initial workshop type split based on territory tile composition.
## Called once per settlement during worldgen; GovernorAI may call retool_workshops
## again later using its own split to respond to economic conditions.
##
## territory_tag_counts — Dictionary of {tag: tile_count} for the full territory.
## tier                 — settlement tier (0–4).
static func _initial_workshop_split(territory_tag_counts: Dictionary, tier: int) -> Dictionary:
	# Base: always allocate some mills for food processing.
	var split: Dictionary = { "grain_mill": 0.30 }
	# Check whether any ore tags are present in the territory.
	# The smelter handles all ore types so any hit is sufficient.
	# retool_workshops will silently skip smelter at tier 0 via ARTISAN_WORKSHOP_TYPES.
	var has_ore: bool = false
	for ore_tag: String in ["iron", "copper", "tin", "gold", "silver", "coal", "stone", "marble", "gems", "lead"]:
		if territory_tag_counts.get(ore_tag, 0) > 0:
			has_ore = true
			break
	if has_ore:
		split["smelter"]    = 0.25
		split["grain_mill"] = 0.25   # tighten mills slightly to make room for smelters
	# Remainder (0.45 base / 0.50 ore-free) becomes smithy via retool's fallback.
	return split


## Reassigns workshop types in an existing settlement without changing slot count.
## Called during worldgen (_initial_workshop_split) and by GovernorAI at runtime
## (e.g. in response to iron prices, low food).
##
## ss     — the SettlementState to modify in place.
## split  — desired distribution of workshop types, e.g. {"grain_mill": 0.4, "smelter": 0.3}.
##          Fractions are normalised; remainder goes to ARTISAN_WORKSHOP_DEFAULT (smithy).
##          Any type with tier_min > ss.tier is silently skipped and its fraction
##          redistributed to smithy.
##
## NOTE: ss.labor_slots is NOT rebuilt here — if caller reads labor_slots
##       directly it must call _rebuild_labor_slots(ss) afterward.
static func retool_workshops(ss: SettlementState, split: Dictionary) -> void:
	# Collect indices of all workshop slots in ss.buildings.
	var ws_indices: Array = []
	for i: int in ss.buildings.size():
		var entry: Dictionary = ss.buildings[i]
		if ARTISAN_WORKSHOP_TYPES.has(entry.get("building_id", "")):
			ws_indices.append(i)

	if ws_indices.is_empty():
		return

	var tier: int = ss.tier

	# Normalise split; clamp out types that don't meet tier_min.
	var valid_split: Dictionary = {}
	var total_frac: float = 0.0
	for btype: String in split:
		if not ARTISAN_WORKSHOP_TYPES.has(btype):
			continue
		if int(ARTISAN_WORKSHOP_TYPES[btype]) > tier:
			continue  # tier gate — silently skip
		var f: float = maxf(0.0, float(split[btype]))
		if f > 0.0:
			valid_split[btype] = f
			total_frac += f

	# Reset all slots to default first so smithy is the fallback.
	for idx: int in ws_indices:
		ss.buildings[idx]["building_id"] = ARTISAN_WORKSHOP_DEFAULT

	if valid_split.is_empty() or total_frac <= 0.0:
		return  # all smithy

	# Assign slots in proportion order.
	var remaining: Array = ws_indices.duplicate()
	for btype: String in valid_split:
		var frac:  float = valid_split[btype] / total_frac
		var slots: int   = roundi(float(ws_indices.size()) * frac)
		for _s: int in mini(slots, remaining.size()):
			ss.buildings[remaining[0]]["building_id"] = btype
			remaining.remove_at(0)
	# Any remaining indices keep ARTISAN_WORKSHOP_DEFAULT.


## Returns estimated production building count for a tier at the tier's
## population midpoint, using class-pool ratios. Excludes houses and wells.
## Useful for tests and the debug inspector.
static func expected_production_count(tier: int) -> int:
	# Population class split: peasant 70% · artisan 15% · merchant 10% · noble 5%.
	# Flat across all tiers — matches region_generator.gd _build_world_state.
	# NOTE: region_generator.gd must also be updated:
	#   1. Remove the tier-0 special case (currently 88/0/10/2).
	#   2. Update TIER_POP_MIN/MAX to the new ranges below.
	#
	# New tier population ranges (Option B):
	#   T0 Hamlet:    80  –  200   mid 140
	#   T1 Village:   250 –  800   mid 525
	#   T2 Town:      900 – 1800   mid 1350
	#   T3 City:     2000 – 5000   mid 3500
	#   T4 Metropolis:6000 –15000  mid 10500
	#
	# Key thresholds with these ranges + 70/15/10/5 split:
	#   First smithy  → 133 total (T0 hamlet at ~200 max gets 1)
	#   First inn     → 250 total (first possible at T1 entry)
	#   First mill    → 227 total (T1 entry)
	const TIER_CLASS_MID: Array[Dictionary] = [
		{ "peasant": 98,   "artisan": 21,  "merchant": 14   },  # T0 hamlet    (140 pop)
		{ "peasant": 368,  "artisan": 79,  "merchant": 53   },  # T1 village   (525 pop)
		{ "peasant": 945,  "artisan": 203, "merchant": 135  },  # T2 town      (1350 pop)
		{ "peasant": 2450, "artisan": 525, "merchant": 350  },  # T3 city      (3500 pop)
		{ "peasant": 7350, "artisan": 1575,"merchant": 1050 },  # T4 metropolis (10500 pop)
	]
	var t: int   = clampi(tier, 0, TIER_CLASS_MID.size() - 1)
	var mid: Dictionary = TIER_CLASS_MID[t]
	var count: int = 0

	# Artisan workshops counted as a unified pool.
	# Use max_workers from the default workshop type so the estimate tracks JSON edits.
	var ws_bdef: Dictionary = ContentRegistry.get_content("building", ARTISAN_WORKSHOP_DEFAULT)
	var ws_workers: int = maxi(1, int(ws_bdef.get("max_workers", 1)))
	var artisan_count: int = int(mid.get("artisan", 0))
	count += floori(float(artisan_count) * ARTISAN_WORKSHOP_RATIO / float(ws_workers))

	# Extraction buildings are tile-driven — their count depends on world tile
	# composition which is unknown at estimate time. Skipped here intentionally.
	# Use the tile-counting path in _place_settlement for accurate counts.

	for bid: String in BUILDING_WORKER_CLASS:
		if bid in EXTRACTION_BUILDINGS:
			continue  # tile-driven — cannot estimate without world data
		if bid in ["well", "house", "market"]:
			continue
		var worker_class: String = BUILDING_WORKER_CLASS[bid]
		if worker_class == "artisan":
			continue  # handled above via ARTISAN_WORKSHOP_RATIO
		var bdef: Dictionary = ContentRegistry.get_content("building", bid)
		if bdef.is_empty():
			continue
		var workers_needed: int = int(bdef.get("max_workers", 1))
		if workers_needed <= 0:
			continue
		var class_pop: int   = int(mid.get(worker_class, 0))
		var ratio:     float = float(CLASS_BUILDING_RATIOS.get(worker_class, {}).get(bid, 0.0))
		var c: int = floori(float(class_pop) * ratio / float(workers_needed))
		count += c
	# market uses the formula path
	if t >= MARKET_BASE_TIER:
		count += (t - MARKET_BASE_TIER) + 1
	return count
