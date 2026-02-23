class_name Production

## Calculates and adds daily resource output for a settlement.
## Called by Settlement.daily_tick() before market.consume().
##
## Labour allocation priority:
##   0. Flat-rate auto-produce (fish, furs — no workers needed)
##   1. 24-hour food survival
##   2. Fuel survival (only when wood stock < 7-day buffer)
##   3. 30-day grain security buffer
##   4. Profit (top-2 resources by current price / base price margin)

static func run(settlement: Settlement) -> void:
	var available_laborers: int = settlement.laborers

	# ── 0. Flat-rate resources (no labour required) ───────────────────────────
	# Fish and furs produce automatically regardless of worker allocation.
	settlement.market.add_stock("fish", _fish_rate(settlement))
	settlement.market.add_stock("furs", _furs_rate(settlement))

	# ── 1. Immediate food need (1 day) ───────────────────────────────────────
	var food_24h: float = settlement.population * 1.2
	available_laborers = _assign_food(settlement, available_laborers, food_24h)

	# ── 2. Fuel need (only if stock < 7-day buffer) ───────────────────────────
	if available_laborers > 0 and settlement.forest_acres > 0.0:
		var fuel_needed:    float = settlement.population * 0.02
		var wood_stock:     float = settlement.market.get_stock("wood")
		var fuel_days:      float = wood_stock / maxf(fuel_needed, 0.001)
		if fuel_days < 7.0:
			var wood_rate:      float = _wood_rate(settlement)
			var rate_per_worker: float = wood_rate / maxf(settlement.laborers, 1.0)
			var fuel_workers:   int   = mini(available_laborers, ceili(fuel_needed / maxf(rate_per_worker, 0.0001)))
			settlement.market.add_stock("wood", fuel_workers * rate_per_worker)
			available_laborers -= fuel_workers

	# ── 3. 30-day grain buffer ────────────────────────────────────────────────
	if available_laborers > 0:
		var food_30d:    float = settlement.population * 1.2 * 30.0
		var current:     float = settlement.market.get_stock("grain")
		var shortfall:   float = food_30d - current
		if shortfall > 0.0:
			available_laborers = _assign_food(settlement, available_laborers, shortfall)

	# ── 4. Profit run ─────────────────────────────────────────────────────────
	if available_laborers > 0:
		_assign_profit(settlement, available_laborers)

	# ── 5. Processing chains ───────────────────────────────────────────────────
	_run_processing(settlement)


# ── Production rate calculations ──────────────────────────────────────────────

## Daily grain output if ALL laborers work the fields (units/day).
## Calibrated so a hamlet on plains (arable_acres ≈ 2250) produces ~470/day
## total, meaning ~22 workers feed 100 people and 60 workers are free for profit.
static func _grain_rate(settlement: Settlement) -> float:
	var fallow:     float = 0.67 if settlement.has_three_field else 0.50
	var farm_level: int   = settlement._building_level("farm")
	var mult:       float = 1.0 + farm_level * 0.5
	return settlement.arable_acres * fallow * 0.28 * mult


## Daily wood output if ALL laborers work the forests.
## 200 forest_acres → 5 wood/day total; pop 100 needs 2/day fuel.
static func _wood_rate(settlement: Settlement) -> float:
	var mill_level: int   = settlement._building_level("lumber_mill")
	var mult:       float = 1.0 + mill_level * 1.0
	return settlement.forest_acres * 0.025 * mult


## Daily ore output if ALL laborers work the mines.
## 400 mining_slots (1 mountain tile) → ~21/day at mine lv1.
static func _ore_rate(settlement: Settlement) -> float:
	var mine_level: int   = settlement._building_level("mine")
	var mult:       float = 1.0 + mine_level * 0.5
	return settlement.mining_slots * 0.035 * mult


## Daily fish output (flat rate — not divided by laborers).
## 80 fishing_slots (1 coast tile) → ~11/day at fishery lv1.
static func _fish_rate(settlement: Settlement) -> float:
	var fishery_level: int   = settlement._building_level("fishery")
	var mult:          float = 1.0 + fishery_level * 0.5
	return settlement.fishing_slots * 0.14 * mult


## Surface quarrying alongside ore extraction.
static func _stone_rate(settlement: Settlement) -> float:
	var mine_level: int   = settlement._building_level("mine")
	var mult:       float = 1.0 + mine_level * 0.30
	return settlement.mining_slots * 0.045 * mult


## Deep-seam coal; only accessible from mine level 2+.
static func _coal_rate(settlement: Settlement) -> float:
	var mine_level: int = settlement._building_level("mine")
	if mine_level < 2:
		return 0.0
	return settlement.mining_slots * 0.020 * float(mine_level - 1)


## Livestock output from a fraction of arable land set aside for pasture.
static func _meat_rate(settlement: Settlement) -> float:
	var farm_level: int   = settlement._building_level("farm")
	var mult:       float = 1.0 + farm_level * 0.30
	return settlement.arable_acres * 0.003 * mult


## Trapping yield from forest acreage (flat rate — no labour required).
static func _furs_rate(settlement: Settlement) -> float:
	return settlement.forest_acres * 0.005


## Per-labourer daily output for a given resource.
## Fish and furs are flat-rate (auto-produced in step 0) so return 0.0 here
## to prevent workers being erroneously assigned to them in _assign_profit.
static func _rate_per_worker(settlement: Settlement, rid: String) -> float:
	var total_workers: int = maxi(settlement.laborers, 1)
	match rid:
		"grain": return _grain_rate(settlement) / total_workers
		"wood":  return _wood_rate(settlement)  / total_workers
		"ore":   return _ore_rate(settlement)   / total_workers
		"fish":  return 0.0  # flat-rate: auto-produced in step 0
		"stone": return _stone_rate(settlement) / total_workers
		"coal":  return _coal_rate(settlement)  / total_workers
		"meat":  return _meat_rate(settlement)  / total_workers
		"furs":  return 0.0  # flat-rate: auto-produced in step 0
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


## Converts raw resources into processed goods using active buildings.
## Runs after labour assignment — raw stocks are already updated for the day.
static func _run_processing(settlement: Settlement) -> void:
	# Ale: tavern converts grain → ale (4 grain per ale, per tavern level per day).
	# Only converts surplus grain beyond a 7-day safety buffer.
	var tavern_level: int = settlement._building_level("tavern")
	if tavern_level > 0:
		var ale_target:    float = float(tavern_level) * 2.0
		var grain_safety:  float = settlement.population * 1.2 * 7.0
		var grain_spare:   float = maxf(0.0, settlement.market.get_stock("grain") - grain_safety)
		var grain_used:    float = minf(grain_spare, ale_target * 4.0)
		if grain_used > 0.0:
			settlement.market.deduct_stock("grain", grain_used)
			settlement.market.add_stock("ale",    grain_used / 4.0)

	# Timber: lumber_mill lv2+ converts surplus wood → timber (3 wood per timber).
	var mill_level: int = settlement._building_level("lumber_mill")
	if mill_level >= 2:
		var timber_target: float = float(mill_level - 1)
		var wood_safety:   float = settlement.population * 0.02 * 7.0
		var wood_spare:    float = maxf(0.0, settlement.market.get_stock("wood") - wood_safety)
		var wood_used:     float = minf(wood_spare, timber_target * 3.0)
		if wood_used > 0.0:
			settlement.market.deduct_stock("wood",  wood_used)
			settlement.market.add_stock("timber",   wood_used / 3.0)

	# Iron: forge converts ore → iron (3 ore per iron, per forge level per day).
	var forge_level: int = settlement._building_level("forge")
	if forge_level > 0:
		var iron_target: float = float(forge_level)
		var ore_used:    float = minf(settlement.market.get_stock("ore"), iron_target * 3.0)
		if ore_used > 0.0:
			settlement.market.deduct_stock("ore",  ore_used)
			settlement.market.add_stock("iron",    ore_used / 3.0)


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

	# Split workers evenly across top 2 (use ceili so small worker counts aren't floored to 0)
	var slots: int = mini(2, margins.size())
	if slots == 0:
		return
	var remaining: int = workers
	for i in range(slots):
		var per_slot: int  = ceili(float(remaining) / float(slots - i))
		var rid:    String = margins[i][1]
		var rate:   float  = _rate_per_worker(settlement, rid)
		var output: float  = per_slot * rate
		if output > 0.0:
			settlement.market.add_stock(rid, output)
		remaining -= per_slot
