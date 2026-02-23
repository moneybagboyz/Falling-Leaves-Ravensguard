class_name Production

## Calculates and adds daily resource output for a settlement.
## Called by Settlement.daily_tick() before market.consume().
##
## Labour allocation priority:
##   1. 24-hour food survival
##   2. Fuel survival
##   3. 60-day grain security buffer
##   4. Profit (top-2 resources by current price / base price margin)

static func run(settlement: Settlement) -> void:
	var available_laborers: int = settlement.laborers

	# ── 1. Immediate food need (1 day) ───────────────────────────────────────
	var food_24h: float = settlement.population * 1.2
	available_laborers = _assign_food(settlement, available_laborers, food_24h)

	# ── 2. Fuel need ─────────────────────────────────────────────────────────
	if available_laborers > 0 and settlement.forest_acres > 0.0:
		var fuel_needed: float  = settlement.population * 0.02
		var wood_rate:   float  = _wood_rate(settlement)
		var fuel_workers: int   = mini(available_laborers, ceili(fuel_needed / maxf(wood_rate / maxf(settlement.laborers, 1.0), 0.0001)))
		settlement.market.add_stock("wood", fuel_workers * (wood_rate / maxf(settlement.laborers, 1.0)))
		available_laborers -= fuel_workers

	# ── 3. 60-day grain buffer ────────────────────────────────────────────────
	if available_laborers > 0:
		var food_60d:    float = settlement.population * 1.2 * 60.0
		var current:     float = settlement.market.get_stock("grain")
		var shortfall:   float = food_60d - current
		if shortfall > 0.0:
			available_laborers = _assign_food(settlement, available_laborers, shortfall)

	# ── 4. Profit run ─────────────────────────────────────────────────────────
	if available_laborers > 0:
		_assign_profit(settlement, available_laborers)


# ── Production rate calculations ──────────────────────────────────────────────

## Daily grain output if ALL laborers work the fields (units/day).
static func _grain_rate(settlement: Settlement) -> float:
	var fallow:     float = 0.67 if settlement.has_three_field else 0.50
	var farm_level: int   = settlement._building_level("farm")
	var mult:       float = 1.0 + farm_level * 0.5
	## Yield: arable_acres × fallow × base_bushels/acre/year ÷ 360 days
	return settlement.arable_acres * fallow * 12.0 / 360.0 * mult


static func _wood_rate(settlement: Settlement) -> float:
	var mill_level: int   = settlement._building_level("lumber_mill")
	var mult:       float = 1.0 + mill_level * 1.0
	return settlement.forest_acres / 40.0 * 8.0 / 360.0 * mult


static func _ore_rate(settlement: Settlement) -> float:
	var mine_level: int   = settlement._building_level("mine")
	var mult:       float = 1.0 + mine_level * 0.5
	return settlement.mining_slots * 0.005 / 360.0 * mult


static func _fish_rate(settlement: Settlement) -> float:
	var fishery_level: int   = settlement._building_level("fishery")
	var mult:          float = 1.0 + fishery_level * 0.5
	return settlement.fishing_slots * 25.0 / 360.0 * mult


## Per-labourer daily output for a given resource.
static func _rate_per_worker(settlement: Settlement, rid: String) -> float:
	var total_workers: int = maxi(settlement.laborers, 1)
	match rid:
		"grain": return _grain_rate(settlement) / total_workers
		"wood":  return _wood_rate(settlement)  / total_workers
		"ore":   return _ore_rate(settlement)   / total_workers
		"fish":  return _fish_rate(settlement)
	return 0.0


# ── Labour assignment helpers ─────────────────────────────────────────────────

## Assigns enough workers to produce `target` units of grain.
## Returns remaining available_laborers.
static func _assign_food(settlement: Settlement, workers_available: int, target: float) -> int:
	var rate: float = _rate_per_worker(settlement, "grain")
	if rate <= 0.0:
		return workers_available
	var workers_needed: int = ceili(target / rate)
	var assigned:       int = mini(workers_available, workers_needed)
	settlement.market.add_stock("grain", assigned * rate)
	return workers_available - assigned


## Splits remaining workers across the top-2 highest-margin resources.
static func _assign_profit(settlement: Settlement, workers: int) -> void:
	var margins: Array = []
	for rid in ResourceRegistry.ALL_RESOURCES:
		var rate: float = _rate_per_worker(settlement, rid)
		if rate <= 0.0:
			continue
		var base:  float = ResourceRegistry.base_price(rid)
		var price: float = settlement.market.get_price(rid)
		margins.append([price / base, rid])
	margins.sort_custom(func(a: Array, b: Array) -> bool: return a[0] > b[0])

	# Split workers evenly across top 2
	var slots: int = mini(2, margins.size())
	if slots == 0:
		return
	var per_slot: int = workers / slots
	for i in range(slots):
		var rid:    String = margins[i][1]
		var rate:   float  = _rate_per_worker(settlement, rid)
		var output: float  = per_slot * rate
		if output > 0.0:
			settlement.market.add_stock(rid, output)
