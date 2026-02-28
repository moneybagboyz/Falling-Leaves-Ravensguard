## PriceLedger — updates per-settlement good prices based on supply/demand.
##
## Price formula:
##   price_target = base_value × (demand / max(supply, ε))
##   clamped to [base_value × PRICE_FLOOR, base_value × PRICE_CAP]
##   then smoothed 20 % per pulse toward target.
##
## demand is estimated from population-class consumption definitions.
class_name PriceLedger
extends RefCounted

const PRICE_FLOOR: float = 0.25   # minimum fraction of base_value
const PRICE_CAP:   float = 4.0    # maximum fraction of base_value
const EPSILON:     float = 0.001  # prevents divide-by-zero
const SMOOTH:      float = 0.20   # fraction of gap closed each pulse


static func update(ss: SettlementState, delta_ticks: int) -> void:
	var cr := ContentRegistry

	# Update prices for every good currently in inventory.
	for good_id: String in ss.inventory.keys():
		_update_good(ss, good_id, delta_ticks, cr)


static func _update_good(ss: SettlementState, good_id: String, delta_ticks: int, cr) -> void:
	var base_val: float  = _base_value(cr, good_id)
	var supply:   float  = maxf(ss.inventory.get(good_id, 0.0), EPSILON)
	var demand:   float  = _estimate_demand(ss, good_id, delta_ticks, cr)
	var ratio:    float  = demand / supply
	var target:   float  = clampf(base_val * ratio, base_val * PRICE_FLOOR, base_val * PRICE_CAP)
	var current:  float  = ss.prices.get(good_id, base_val)
	ss.prices[good_id]   = lerpf(current, target, SMOOTH)


static func _base_value(cr, good_id: String) -> float:
	var good = cr.get_content("good", good_id)
	if good != null and not good.is_empty():
		return float(good.get("base_value", 1.0))
	return 1.0


static func _estimate_demand(ss: SettlementState, good_id: String, delta_ticks: int, cr) -> float:
	var demand: float = 0.0
	for cls_id: String in ss.population.keys():
		var cls_def = cr.get_content("population_class", cls_id)
		if cls_def == null or cls_def.is_empty():
			continue
		var cons: Dictionary = cls_def.get("consumption_per_head_per_tick", {})
		if cons.has(good_id):
			demand += float(ss.population.get(cls_id, 0)) * float(cons[good_id]) * float(delta_ticks)
	return maxf(demand, EPSILON)
