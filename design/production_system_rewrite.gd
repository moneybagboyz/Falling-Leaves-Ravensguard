## ProductionSystem (Rewrite) — Hybrid acre/recipe model.
##
## Two fundamentally different production modes:
##
## ── EXTRACTION (acre-based, labor-capped by placed buildings) ────────────────
##   Every extraction building type has a worker-slot count.
##   Workers assigned to it run the standard acre/yield formula from Globals.gd.
##   The building itself is NOT the output source — the land is.
##   Placed buildings set the CEILING on how many workers can work the land.
##
##   If no extraction buildings are placed (new hamlet, siege aftermath),
##   a survival fallback runs at greatly reduced efficiency so settlements
##   don't instantly starve on day 1 before worldgen buildings are assigned.
##
##   Subsistence priority applies: food workers are allocated before any other
##   extraction type, ensuring farms fill before lumber or clay.
##
## ── PROCESSING (recipe-based, strict input + worker gate) ────────────────────
##   Each processing building type has:
##     max_workers     — artisan capacity of a single placed building
##     workers_per_cycle — artisans consumed per recipe run
##     inputs          — {resource: quantity} consumed per cycle
##     outputs         — {resource: quantity} produced per cycle
##
##   Output = floor(min(
##     available_workers / workers_per_cycle,
##     floor(input_stock[r] / input_qty[r])  for each input r
##   )) × output_qty
##
##   A smithy with no iron idles completely. A smelter with coal but no ore
##   idles completely. This makes supply chains real: upstream scarcity
##   propagates downstream instead of being masked by phantom production.
##
## ── LABOR POOLS ────────────────────────────────────────────────────────────
##   Laborers (peasants)  → extraction buildings
##   Burghers (artisans)  → processing buildings
##
##   Both pools are soft-separated: the system won't borrow artisans for
##   farm work or vice versa. Governors must actually staff the right buildings.
##
## ── BUILDING DATA ─────────────────────────────────────────────────────────
##   s_data.buildings is a Dictionary: { building_id: count }
##   Example: { "farm_plot": 5, "ore_mine": 2, "smithy": 1, "grain_mill": 1 }
##
##   count is the number of placed building instances, set by BuildingPlacer
##   and updated when the player constructs or destroys buildings.
##
## ── COMPANION FILES ────────────────────────────────────────────────────────
##   building_placer_overhaul.gd  — sets initial building counts at worldgen
##   GDSettlement.gd              — holds s_data.buildings, s_data.inventory,
##                                  s_data.laborers, s_data.burghers, etc.
##
## Drop-in target: replaces src/economy/ProductionSystem.gd in the rewrite branch.

class_name ProductionSystemRewrite
extends RefCounted


# ── Globals preload (same path as original) ───────────────────────────────────
const Globals = preload("res://src/core/Globals.gd")


# ═══════════════════════════════════════════════════════════════════════════════
# EXTRACTION BUILDING CONSTANTS
# ═══════════════════════════════════════════════════════════════════════════════

## Max laborer slots provided by a single placed extraction building.
## These are tuning targets — set them so that a "full" settlement
## (tile count filling every slot) just saturates available laborer supply.
const EXTRACTION_WORKERS_PER_BUILDING: Dictionary = {
	"farm_plot":     5,   # 5 peasants per farm
	"ore_mine":      4,   # 4 miners per mine shaft
	"lumber_camp":   3,   # 3 woodcutters per woodland camp
	"charcoal_camp": 3,   # 3 burners per charcoal camp (less labor-intensive than felling)
	"clay_pit":      3,   # 3 diggers per clay/peat pit
	"fishery":       4,   # 4 fishers per fishery (boats + nets)
}

## Survival fallback output rate when NO extraction buildings are placed.
## Expressed as fraction of FULL_EFFICIENCY output.
## (e.g. 0.15 = 15% yield — enough to prevent instant starvation, not enough
## to sustain growth. Governor must place buildings to unlock full yield.)
const SURVIVAL_FALLBACK_EFFICIENCY: float = 0.15


# ═══════════════════════════════════════════════════════════════════════════════
# PROCESSING BUILDING RECIPES
# ═══════════════════════════════════════════════════════════════════════════════
##
## Recipe format per building type:
##   max_workers      int   — total artisan slots across one placed building
##   workers_per_cycle int  — artisans consumed per output cycle
##   inputs           Dict  — { resource_id: qty } consumed per cycle
##   outputs          Dict  — { resource_id: qty } produced per cycle
##
## Special keys:
##   "priority_inputs"  — Array of input sets tried in order; first set whose
##                        stock is satisfied wins. Used for smithy gear selection.
##
const PROCESSING_RECIPES: Dictionary = {

	# ── Food processing ────────────────────────────────────────────────────────
	"grain_mill": {
		"max_workers":      4,
		"workers_per_cycle": 2,
		"inputs":  { "grain": 2 },
		"outputs": { "flour": 1 },
		# flour has food_value 2.0 — 1 flour is as filling as 2 grain
	},

	# ── Drink ─────────────────────────────────────────────────────────────────
	"brewery": {
		"max_workers":      4,
		"workers_per_cycle": 2,
		"inputs":  { "grain": 2 },
		"outputs": { "ale": 1 },
	},

	# ── Textiles ──────────────────────────────────────────────────────────────
	"weaver": {
		"max_workers":      4,
		"workers_per_cycle": 2,
		"inputs":  { "wool": 2 },
		"outputs": { "cloth": 1 },
	},
	"tailor": {
		"max_workers":      4,
		"workers_per_cycle": 4,
		"inputs":  { "cloth": 2 },
		"outputs": { "fine_garments": 1 },
	},

	# ── Leather ───────────────────────────────────────────────────────────────
	"tannery": {
		"max_workers":      4,
		"workers_per_cycle": 3,
		"inputs":  { "hides": 2 },
		"outputs": { "leather": 1 },
	},

	# ── Metalwork ─────────────────────────────────────────────────────────────
	# Smelter: converts raw iron → steel. Not a mandatory gate.
	# T0/T1 smithy runs on raw iron. Smelter unlocks the steel tier.
	"smelter": {
		"max_workers":       6,
		"workers_per_cycle":  4,
		"inputs":  { "iron": 3, "coal": 2 },
		"outputs": { "steel": 2 },
	},

	# Smithy: raw iron → iron weapons at T0/T1; steel → fine weapons when smelter running.
	# Priority order: steel (smelter output) → iron (direct, no smelter needed) → tools fallback
	"smithy": {
		"max_workers":       6,
		"workers_per_cycle":  3,
		"priority_inputs": [
			# Best: steel (smelter output) → exceptional weapons
			{ "inputs": { "steel": 2, "coal": 1 },
			  "outputs": { "steel_sword": 1 } },
			# Standard: raw iron → iron weapons (no smelter required)
			{ "inputs": { "iron": 2, "coal": 1 },
			  "outputs": { "iron_sword": 1 } },
			# Fallback: raw iron → crude tools (coal unavailable)
			{ "inputs": { "iron": 2 },
			  "outputs": { "tools": 1 } },
		],
	},

	# Bronzesmith: copper + tin → bronze (primary T1 military metal)
	"bronzesmith": {
		"max_workers":       6,
		"workers_per_cycle":  2,
		"inputs":  { "copper": 2, "tin": 1 },
		"outputs": { "bronze": 2 },
	},

	# Toolmaker: iron + wood → tools (frees smithy for weapons if present)
	"toolmaker": {
		"max_workers":      4,
		"workers_per_cycle": 4,
		"inputs":  { "iron": 1, "wood": 2 },
		"outputs": { "tools": 2 },
	},

	# Goldsmith: gold + coal → luxury jewelry
	"goldsmith": {
		"max_workers":      4,
		"workers_per_cycle": 8,
		"inputs":  { "gold": 1, "coal": 1 },
		"outputs": { "jewelry": 1 },
	},

	# ── Construction materials ─────────────────────────────────────────────────
	"brickmaker": {
		"max_workers":      4,
		"workers_per_cycle": 3,
		"inputs":  { "clay": 3, "coal": 1 },
		"outputs": { "bricks": 2 },
	},
}


# ═══════════════════════════════════════════════════════════════════════════════
# PUBLIC ENTRY POINT
# ═══════════════════════════════════════════════════════════════════════════════

## Main daily production tick. Call once per settlement per day.
## efficiency: float in [0, 1] — from s_data.get_workforce_efficiency()
static func run_production_tick(s_data, efficiency: float) -> void:
	if efficiency <= 0.0:
		return

	# 1. Re-scan territory acres if radius or terrain changed (same as original).
	#    recalculate_production() is called externally by EconomyManager when
	#    terrain changes; we don't call it here every tick.

	# 2. Compute available labor pools.
	var laborer_pool: int = int(s_data.laborers * efficiency)
	var burgher_pool:  int = int(s_data.burghers * efficiency)

	# 3. Extraction (acre-based, worker-capped by placed buildings).
	laborer_pool = _process_extraction(s_data, laborer_pool)

	# 4. Processing (recipe-based, artisan-driven).
	_process_processing(s_data, burgher_pool)

	# 5. Charcoal energy pass (same as original — wood surplus → coal).
	_process_charcoal(s_data, efficiency)

	# 6. Store last allocation for UI / audit access.
	s_data.last_labor_allocation = s_data.get("_last_alloc", {})


# ═══════════════════════════════════════════════════════════════════════════════
# EXTRACTION
# ═══════════════════════════════════════════════════════════════════════════════

## Returns the remaining unallocated laborer count after extraction buildings fill.
static func _process_extraction(s_data, laborer_pool: int) -> int:
	var remaining: int = laborer_pool
	var alloc: Dictionary = {}

	# ── Subsistence priority: food first ──────────────────────────────────────
	var daily_food_req: float = float(s_data.population) * Globals.DAILY_BUSHELS_PER_PERSON
	if s_data.get_food_stock() < daily_food_req:
		remaining = _fill_extraction_building(s_data, "farm_plot", remaining, alloc)
		remaining = _fill_extraction_building(s_data, "fishery",   remaining, alloc)

	# ── Fuel priority: wood second ──────────────────────────────────────────
	var daily_wood_req: float = float(s_data.population) / Globals.WOOD_FUEL_POP_DIVISOR
	if s_data.inventory.get("wood", 0) < daily_wood_req:
		remaining = _fill_extraction_building(s_data, "lumber_camp",   remaining, alloc)
		remaining = _fill_extraction_building(s_data, "charcoal_camp", remaining, alloc)

	# ── Economic extraction: remaining labor fills whatever is under-staffed ──
	for bid in ["farm_plot", "fishery", "ore_mine", "lumber_camp",
				"charcoal_camp", "clay_pit"]:
		if remaining <= 0:
			break
		remaining = _fill_extraction_building(s_data, bid, remaining, alloc)

	# ── Execute production for each allocated building/worker pair ─────────────
	for bid in alloc:
		if alloc[bid] > 0:
			_produce_extraction(s_data, bid, alloc[bid])

	s_data["_last_alloc"] = alloc
	return remaining


## Fills as many worker slots as possible for one extraction building type.
## Slots already used in alloc[] are respected (won't double-count subsistence).
static func _fill_extraction_building(
	s_data,
	building_id: String,
	remaining: int,
	alloc: Dictionary
) -> int:
	var placed: int = s_data.buildings.get(building_id, 0)
	if placed <= 0:
		# No buildings placed — survival fallback handled in produce step.
		return remaining

	var workers_per: int = EXTRACTION_WORKERS_PER_BUILDING.get(building_id, 1)
	var max_workers: int = placed * workers_per
	var already: int    = alloc.get(building_id, 0)
	var slots_left: int = max_workers - already

	if slots_left <= 0:
		return remaining

	var take: int = min(remaining, slots_left)
	alloc[building_id] = already + take
	return remaining - take


## Runs the acre/yield formula for a filled extraction building.
static func _produce_extraction(s_data, building_id: String, workers: int) -> void:
	var placed: int = s_data.buildings.get(building_id, 0)

	# ── Survival fallback ────────────────────────────────────────────────────
	# No buildings placed → allow minimal output so fresh hamlets can survive
	# day 1 before BuildingPlacer has run or assignment is queued.
	var mult: float = 1.0
	if placed == 0:
		mult = SURVIVAL_FALLBACK_EFFICIENCY
		# For fallback, use a nominal 1 worker's worth of output.
		workers = 1

	match building_id:

		"farm_plot":
			var h_acres: float   = workers * Globals.ACRES_WORKED_PER_LABORER
			var g_prod: int      = int((h_acres * Globals.BUSHELS_PER_ACRE_BASE * mult
										/ Globals.DAYS_PER_YEAR)
									* (1.0 - Globals.SEED_RATIO_INV))
			s_data.add_inventory("grain", g_prod)
			GameState.track_production("grain", g_prod)

		"fishery":
			var f_prod: int = int(workers * Globals.FISHING_YIELD_BASE * mult
								/ Globals.DAYS_PER_YEAR)
			s_data.add_inventory("fish", f_prod)
			GameState.track_production("fish", f_prod)

		"ore_mine":
			_process_ore_mine(s_data, workers, mult)

		"lumber_camp":
			var w_worked: float = workers * Globals.ACRES_WORKED_PER_LABORER
			var wood: int       = int(w_worked * Globals.FORESTRY_YIELD_WOOD * mult
									/ Globals.DAYS_PER_YEAR)
			s_data.add_inventory("wood", wood)
			GameState.track_production("wood", wood)

		"charcoal_camp":
			# Direct conversion: woodcutters specifically produce charcoal
			# 10 wood → 4 coal (same ratio as _process_charcoal but labor-driven)
			var w_worked: float = workers * Globals.ACRES_WORKED_PER_LABORER
			var wood_cut: int   = int(w_worked * Globals.FORESTRY_YIELD_WOOD * mult
									/ Globals.DAYS_PER_YEAR)
			var coal_out: int   = int(wood_cut * 0.4)
			s_data.add_inventory("wood",  wood_cut)   # cut, then burn a fraction
			s_data.add_inventory("coal",  coal_out)
			GameState.track_production("wood",  wood_cut)
			GameState.track_production("coal",  coal_out)

		"clay_pit":
			if s_data.wetland_acres > 0:
				var peat: int = int(workers * Globals.PEAT_YIELD * mult / Globals.DAYS_PER_YEAR)
				var clay: int = int(workers * Globals.CLAY_YIELD * mult / Globals.DAYS_PER_YEAR)
				s_data.add_inventory("peat", peat)
				s_data.add_inventory("clay", clay)
				GameState.track_production("peat", peat)
				GameState.track_production("clay", clay)
			elif s_data.arid_acres > 0:
				var salt: int = int(workers * Globals.SALT_YIELD * mult / Globals.DAYS_PER_YEAR)
				var sand: int = int(workers * Globals.SAND_YIELD * mult / Globals.DAYS_PER_YEAR)
				s_data.add_inventory("salt", salt)
				s_data.add_inventory("sand", sand)
				GameState.track_production("salt", salt)
				GameState.track_production("sand", sand)


## Distributes mine labor across all present ore deposits.
## Identical logic to the original _process_mine_resources() but reads
## placed building count instead of a level multiplier.
static func _process_ore_mine(s_data, labor: int, mult: float) -> void:
	# Stone — baseline from any mine labor
	var stone_prod: int = int(labor * 4.0 * mult / 30.0)
	s_data.add_inventory("stone", stone_prod)
	GameState.track_production("stone", stone_prod)

	# Ore deposits — split labor evenly across present deposit types
	var ores: Array = s_data.ore_deposits.keys()
	if ores.is_empty():
		return

	var specialized_labor: float = float(labor) / float(ores.size())

	for ore in ores:
		var yield_mult: float = 1.0
		match ore:
			"iron":   yield_mult = 1.0
			"copper": yield_mult = 0.8
			"tin":    yield_mult = 0.8
			"coal":   yield_mult = 1.2   # coal seams are productive
			"lead":   yield_mult = 0.7
			"silver": yield_mult = 0.4
			"gold":   yield_mult = 0.3
			"gems":   yield_mult = 0.2
			"marble": yield_mult = 0.4

		var amt: int = int(specialized_labor * s_data.ore_deposits[ore] * yield_mult
						* mult / 30.0)
		if amt > 0:
			s_data.add_inventory(ore, amt)
			GameState.track_production(ore, amt)


# ═══════════════════════════════════════════════════════════════════════════════
# PROCESSING
# ═══════════════════════════════════════════════════════════════════════════════

## Distributes burgher labor across all placed processing buildings and
## runs each building's recipe as many times as workers + stock allow.
static func _process_processing(s_data, burgher_pool: int) -> void:
	if burgher_pool <= 0:
		return

	# Build the active roster: { building_id: placed_count } for processing types only.
	var active: Dictionary = {}
	for bid in PROCESSING_RECIPES:
		var placed: int = s_data.buildings.get(bid, 0)
		if placed > 0:
			active[bid] = placed

	if active.is_empty():
		return

	# Total max-worker demand across all processing buildings.
	var total_demand: int = 0
	for bid in active:
		var recipe = PROCESSING_RECIPES[bid]
		total_demand += active[bid] * recipe["max_workers"]

	if total_demand == 0:
		return

	# Distribute burgher pool proportionally if demand > supply.
	# Each building gets: floor(burgher_pool × (its_demand / total_demand))
	var remaining: int = burgher_pool
	var bids: Array   = active.keys()

	for i in range(bids.size()):
		var bid            = bids[i]
		var recipe         = PROCESSING_RECIPES[bid]
		var building_demand: int = active[bid] * recipe["max_workers"]

		# Last building gets all remaining to avoid rounding losses.
		var workers: int
		if i == bids.size() - 1:
			workers = remaining
		else:
			workers = int(float(burgher_pool) * float(building_demand) / float(total_demand))

		workers = min(workers, building_demand)
		remaining = max(0, remaining - workers)

		if workers <= 0:
			continue

		# Run the recipe.
		if recipe.has("priority_inputs"):
			_process_priority_recipe(s_data, bid, workers, recipe)
		else:
			_process_standard_recipe(s_data, bid, workers, recipe)


## Standard recipe: one input set, one output set.
## cycles = min(workers // workers_per_cycle, floor(stock / input_qty) for each input)
static func _process_standard_recipe(
	s_data,
	bid: String,
	workers: int,
	recipe: Dictionary
) -> void:
	var wpb: int = recipe["workers_per_cycle"]
	if wpb <= 0:
		return

	var cycles: int = workers / wpb

	# Cap by each input resource's stock.
	var inv = s_data.inventory
	for res in recipe["inputs"]:
		var qty_per: int  = recipe["inputs"][res]
		var in_stock: int = inv.get(res, 0)
		cycles = min(cycles, in_stock / max(1, qty_per))

	if cycles <= 0:
		return

	# Consume inputs.
	for res in recipe["inputs"]:
		inv[res] = inv.get(res, 0) - (cycles * recipe["inputs"][res])

	# Produce outputs.
	for res in recipe["outputs"]:
		var out_qty: int = cycles * recipe["outputs"][res]
		s_data.add_inventory(res, out_qty)
		GameState.track_production(res, out_qty)


## Priority recipe (smithy): try each input variant in order, run with whatever wins.
## Only one variant can run per tick (workers aren't split across variants).
static func _process_priority_recipe(
	s_data,
	bid: String,
	workers: int,
	recipe: Dictionary
) -> void:
	var wpb: int = recipe["workers_per_cycle"]
	if wpb <= 0:
		return

	var max_cycles: int = workers / wpb
	var inv = s_data.inventory

	for variant in recipe["priority_inputs"]:
		var v_inputs  = variant["inputs"]
		var v_outputs = variant["outputs"]

		# Check if any of this variant's inputs are in stock at all.
		var feasible: bool = true
		for res in v_inputs:
			if inv.get(res, 0) <= 0:
				feasible = false
				break
		if not feasible:
			continue

		# Cap cycles by stock of each input.
		var cycles: int = max_cycles
		for res in v_inputs:
			var qty_per: int  = v_inputs[res]
			var in_stock: int = inv.get(res, 0)
			cycles = min(cycles, in_stock / max(1, qty_per))

		if cycles <= 0:
			continue

		# Consume and produce.
		for res in v_inputs:
			inv[res] = inv.get(res, 0) - (cycles * v_inputs[res])
		for res in v_outputs:
			var out_qty: int = cycles * v_outputs[res]
			s_data.add_inventory(res, out_qty)
			GameState.track_production(res, out_qty)

		# First viable variant wins — stop here.
		return



# ═══════════════════════════════════════════════════════════════════════════════
# ENERGY (charcoal)
# ═══════════════════════════════════════════════════════════════════════════════

## Converts surplus wood to coal using a passive fraction of the population.
## Identical to the original _process_energy() — unchanged.
static func _process_charcoal(s_data, efficiency: float) -> void:
	var wood_reserve: int = int(s_data.population / Globals.WOOD_FUEL_POP_DIVISOR) + 50
	if s_data.inventory.get("wood", 0) <= wood_reserve + 20:
		return

	var burner_cap: int   = int(s_data.population * efficiency * 0.05)
	var surplus_wood: int = s_data.inventory.get("wood", 0) - wood_reserve
	var wood_to_burn: int = min(surplus_wood, burner_cap * 10)

	if wood_to_burn < 10:
		return

	var units: int = wood_to_burn / 10
	s_data.inventory["wood"] -= units * 10
	s_data.add_inventory("coal", units * 4)
	GameState.track_consumption("wood", units * 10)
	GameState.track_production("coal", units * 4)


# ═══════════════════════════════════════════════════════════════════════════════
# TERRAIN SCAN — identical interface to original recalculate_production()
# ═══════════════════════════════════════════════════════════════════════════════
## Scans the settlement radius and populates:
##   s_data.ore_deposits      — ore type → deposit strength (for mine recipes)
##   s_data.arable_acres      — for farm yield math
##   s_data.forest_acres, s_data.wetland_acres, s_data.arid_acres — for extraction routing
##   s_data.fishing_slots, s_data.mining_slots, s_data.extraction_slots
##   s_data.production_capacity  — passive luxury/exotic yields (spices, ivory, etc.)
##
## Called by EconomyManager.recalculate_production() whenever terrain changes
## (building constructed, radius expanded, etc.). Not called every tick.
static func recalculate_production(s_data, grid, resources, geology) -> void:
	# Delegate to the full terrain scan — this is identical to the original.
	# (Kept separate so hot-path run_production_tick() doesn't re-scan every day.)
	s_data.initialize_acres(grid, resources, geology)
	s_data.production_capacity = {}
	s_data.mining_slots        = 0
	s_data.fishing_slots       = 0
	s_data.extraction_slots    = 0
	s_data.ore_deposits        = {}

	var r: int = s_data.radius
	var h: int = grid.size()
	var w: int = grid[0].size() if h > 0 else 0

	for dy in range(-r, r + 1):
		for dx in range(-r, r + 1):
			var p := s_data.pos + Vector2i(dx, dy)
			if p.x < 0 or p.x >= w or p.y < 0 or p.y >= h:
				continue
			if p.distance_to(s_data.pos) > r:
				continue

			var t: String = GameState.get_true_terrain(p)

			match t:
				"^", "O":
					s_data.mining_slots += 400
				"o":
					s_data.mining_slots += 150
				"~", "≈":
					s_data.fishing_slots += 150
					s_data.extraction_slots += 40
				"&":
					s_data.extraction_slots += 80
				'"':
					s_data.extraction_slots += 100
				"/", "\\":
					s_data.fishing_slots    += 50
					s_data.extraction_slots += 20

			if resources.has(p):
				var res: String = resources[p]
				if res in ["iron", "copper", "silver", "gold", "gems",
							"marble", "coal", "tin", "lead"]:
					# Surface ore deposit.
					s_data.ore_deposits[res] = s_data.ore_deposits.get(res, 0.0) + 1.0
					s_data.mining_slots      += 100

				elif res in ["game", "horses", "spices", "ivory", "furs",
							"peat", "salt", "clay"]:
					_increment_prod(s_data, res, 1, geology)

			# Hidden geology deposits (same roll as original).
			if geology.has(p):
				var geo: Dictionary = geology[p]
				var ore_layer = geo.get("ore_layer", {})
				for ore in ore_layer:
					var roll: float = randf()
					if roll < ore_layer[ore]:
						s_data.ore_deposits[ore] = s_data.ore_deposits.get(ore, 0.0) + 0.25
						s_data.mining_slots      += 50

	# Initial labor allocation so the UI shows something on day 1.
	var eff: float = s_data.get_workforce_efficiency()
	run_production_tick(s_data, eff)


## Passive production capacity bonus (luxury goods).
## Identical to original increment_prod().
@warning_ignore("shadowed_global_identifier")
static func _increment_prod(s_data, res_name: String, amount: int, geology = null) -> void:
	var mult: float = 1.0
	if geology and geology.has(s_data.pos):
		var geo: Dictionary = geology[s_data.pos]
		if res_name == "grain":
			if geo.get("rain", 0) > 0.1: mult += 0.5
			elif geo.get("rain", 0) < -0.1: mult -= 0.3
			if geo.get("temp", 0) > 0.3 or geo.get("temp", 0) < -0.3: mult -= 0.5
		elif res_name == "wood":
			if geo.get("rain", 0) > 0.2: mult += 0.5
			elif geo.get("rain", 0) < -0.2: mult -= 0.5
		elif res_name == "fish":
			if geo.get("temp", 0) < -0.3: mult -= 0.5

	amount = max(1, int(amount * mult))
	s_data.production_capacity[res_name] = \
		s_data.production_capacity.get(res_name, 0.0) + amount


# ═══════════════════════════════════════════════════════════════════════════════
# MIGRATION / AUDIT HELPERS
# ═══════════════════════════════════════════════════════════════════════════════

## Returns the maximum daily grain output this settlement can achieve given its
## current farm_plot count, regardless of current population / food state.
## Used by WorldAudit and settlement UI to show "food ceiling".
static func max_daily_grain(s_data) -> float:
	var placed: int     = s_data.buildings.get("farm_plot", 0)
	var workers_per: int = EXTRACTION_WORKERS_PER_BUILDING.get("farm_plot", 5)
	var max_w: int      = placed * workers_per
	var acres: float    = max_w * Globals.ACRES_WORKED_PER_LABORER
	return (acres * Globals.BUSHELS_PER_ACRE_BASE / Globals.DAYS_PER_YEAR) \
		   * (1.0 - Globals.SEED_RATIO_INV)


## Returns true if a processing building is currently idle due to missing inputs.
## Used by Governor AI to decide whether to build upstream extraction.
static func is_processing_idle(s_data, bid: String) -> bool:
	if not PROCESSING_RECIPES.has(bid):
		return false
	if s_data.buildings.get(bid, 0) == 0:
		return false   # not placed — not "idle", just absent

	var recipe = PROCESSING_RECIPES[bid]

	if recipe.has("priority_inputs"):
		for variant in recipe["priority_inputs"]:
			var ok: bool = true
			for res in variant["inputs"]:
				if s_data.inventory.get(res, 0) <= 0:
					ok = false
					break
			if ok:
				return false
		return true  # all variants blocked

	for res in recipe["inputs"]:
		if s_data.inventory.get(res, 0) <= 0:
			return true   # at least one input missing

	return false
