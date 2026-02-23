class_name Market extends RefCounted

## Per-settlement inventory + price simulation.
## Driven by Settlement.daily_tick() → market.consume() → market.update_prices().

# inventory: resource_id -> float quantity (never goes below 0)
var inventory: Dictionary = {}

# 14-day price history ring buffer: resource_id -> Array[float]
var price_history: Dictionary = {}

# Current smoothed price: resource_id -> float
var current_price: Dictionary = {}


# ── Stock access ──────────────────────────────────────────────────────────────

func get_stock(resource_id: String) -> float:
	return inventory.get(resource_id, 0.0)


func add_stock(resource_id: String, amount: float) -> void:
	if amount <= 0.0:
		return
	inventory[resource_id] = get_stock(resource_id) + amount


## Deducts up to `amount` units. Returns how much was actually deducted.
func deduct_stock(resource_id: String, amount: float) -> float:
	var available: float = get_stock(resource_id)
	var deducted: float  = minf(available, amount)
	inventory[resource_id] = available - deducted
	return deducted


# ── Pricing ───────────────────────────────────────────────────────────────────

func get_price(resource_id: String) -> float:
	return current_price.get(resource_id, ResourceRegistry.base_price(resource_id))


## Recalculates prices based on supply/demand ratio, smoothed over 14 days.
func update_prices(settlement: Object) -> void:
	for rid in ResourceRegistry.ALL_RESOURCES:
		var base:   float = ResourceRegistry.base_price(rid)
		var supply: float = get_stock(rid)
		var demand: float = ResourceRegistry.daily_demand(rid, settlement)
		# Raw price: inflates when supply < demand, deflates when supply > demand
		var raw: float = base * clampf(demand / maxf(supply, 1.0), 0.2, 5.0)

		if not price_history.has(rid):
			price_history[rid] = []
		price_history[rid].append(raw)
		if price_history[rid].size() > 14:
			price_history[rid].pop_front()

		var avg: float = 0.0
		for v: float in price_history[rid]:
			avg += v
		current_price[rid] = avg / float(price_history[rid].size())


# ── Daily consumption ─────────────────────────────────────────────────────────

## Deducts food and luxuries from inventory. Applies starvation / unrest if short.
func consume(settlement: Object) -> void:
	# Reset per-tick class flags.
	settlement.burgher_unhappy  = false
	settlement.nobility_unhappy = false

	# --- Food (all classes) ---
	var food_needed: float = settlement.population * 1.2
	var food_given:  float = deduct_stock("grain", food_needed)

	if food_given < food_needed * 0.90:
		var deficit: float = 1.0 - (food_given / maxf(food_needed, 1.0))
		settlement.unrest    = minf(100.0, settlement.unrest    + 20.0 * deficit)
		settlement.happiness = maxf(0.0,   settlement.happiness - 20.0 * deficit)
		var deaths: int = int(settlement.population * 0.02 * deficit) + 2
		settlement.population = maxi(0, settlement.population - deaths)
		settlement._init_population()
	else:
		# Slow recovery when food is plentiful.
		settlement.unrest    = maxf(0.0, settlement.unrest    - 1.0)
		settlement.happiness = minf(100.0, settlement.happiness + 0.5)

	# --- Fuel (wood) ---
	var wood_needed: float = settlement.population * 0.02
	var wood_given:  float = deduct_stock("wood", wood_needed)
	if wood_needed > 0.0 and wood_given < wood_needed:
		var shortage: float = 1.0 - (wood_given / maxf(wood_needed, 1.0))
		settlement.happiness = maxf(0.0,   settlement.happiness - 10.0 * shortage)
		settlement.unrest    = minf(100.0, settlement.unrest    +  5.0 * shortage)

	# --- Tavern bonus ---
	# An active tavern with ale stocked boosts happiness slightly.
	if settlement._building_level("tavern") > 0 and get_stock("ale") > 0.0:
		settlement.happiness = minf(100.0, settlement.happiness + 2.0)

	# --- Burgher luxuries (cloth/leather maintenance when produced) ---
	# Currently deferred — cloth and leather have no production chain yet.
	# When added, flag settlement.burgher_unhappy if unmet.

	# --- Noble luxuries ---
	var noble_met: bool = true
	noble_met = _consume_luxury(settlement, "meat", settlement.nobility * 0.5,  5.0) and noble_met
	noble_met = _consume_luxury(settlement, "furs", settlement.nobility * 0.05, 3.0) and noble_met
	noble_met = _consume_luxury(settlement, "salt", settlement.nobility * 0.05, 2.0) and noble_met
	if not noble_met:
		settlement.nobility_unhappy = true

	# --- Burgher luxuries ---
	var burgher_met: bool = true
	burgher_met = _consume_luxury(settlement, "ale",  settlement.burghers * 0.1,  3.0) and burgher_met
	burgher_met = _consume_luxury(settlement, "salt", settlement.population * 0.03, 2.0) and burgher_met
	if not burgher_met:
		settlement.burgher_unhappy = true


## Returns true if demand was reasonably met (>= 50%), false otherwise.
func _consume_luxury(settlement: Object, rid: String, needed: float, happiness_impact: float) -> bool:
	var given: float = deduct_stock(rid, needed)
	if needed > 0.0 and given < needed * 0.5:
		settlement.happiness = maxf(0.0, settlement.happiness - happiness_impact)
		return false
	return true


# ── Diagnostics ───────────────────────────────────────────────────────────────

## Returns a short human-readable summary for the debug console.
func summary() -> String:
	var lines: PackedStringArray = []
	for rid in ResourceRegistry.ALL_RESOURCES:
		var qty: float = get_stock(rid)
		if qty > 0.0:
			lines.append("  %s: %.1f @ %.2fg" % [rid, qty, get_price(rid)])
	return "\n".join(lines)
