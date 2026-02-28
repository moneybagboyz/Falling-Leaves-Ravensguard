## SettlementPulse — runs the full economic cycle for every settlement.
##
## Called on the PRODUCTION_PULSE tick phase (strategic cadence).
## Bootstrap creates one instance and registers tick_all as a hook.
## Call setup(world_state) before the first tick fires.
##
## Per-settlement order each pulse:
##   1. Reset shortages.
##   2. Seed starter stock if inventory is empty (first-tick safety).
##   3. ProductionLedger.run  — agriculture + extraction + buildings.
##   4. Consume goods by population class.
##   5. PriceLedger.update    — adjust prices toward supply/demand ratio.
##   6. Update prosperity and unrest.
##   7. Adjust worked_acres toward target.
##   8. TradePartySpawner.try_spawn_all — spawn surplus export parties.
class_name SettlementPulse
extends RefCounted

const PROSPERITY_GROW_RATE:  float = 0.005
const PROSPERITY_SHRINK_RATE: float = 0.008
const UNREST_DECAY_RATE:     float = 0.003
## +0.4 %% of each class per pulse when well-fed and prosperous.
const POPULATION_GROW_RATE:    float = 0.004
## −0.8 %% of each class per pulse when starving or in high unrest.
const POPULATION_DECLINE_RATE: float = 0.008

var _world_state: WorldState = null


func setup(ws: WorldState) -> void:
	_world_state = ws


## Hook registered with TickScheduler.PRODUCTION_PULSE.
## tick is the current simulation tick count.
func tick_all(tick: int) -> void:
	var ws: WorldState = _world_state
	if ws == null:
		return

	var delta: int = TickScheduler.STRATEGIC_CADENCE

	for sid: String in ws.settlements.keys():
		var sv = ws.settlements[sid]
		var ss: SettlementState
		if sv is SettlementState:
			ss = sv
		elif sv is Dictionary:
			# Migrate dict → object in-place (happens on first pulse after save-load).
			ss = SettlementState.from_dict(sv)
			ws.settlements[sid] = ss
		else:
			continue

		_tick_one(ss, ws, delta)

	# After all settlements have been pulsed, spawn any warranted trade parties.
	TradePartySpawner.try_spawn_all(ws, tick)

	ws.current_tick = tick


func _tick_one(ss: SettlementState, ws: WorldState, delta_ticks: int) -> void:
	# ── 1. Reset per-pulse shortage tracking ────────────────────────────────
	ss.shortages = {}

	# ── 2. Seed starter stock on first pulse ─────────────────────────────────
	if ss.inventory.is_empty():
		_seed_starter_stock(ss)

	# ── 3. Production ────────────────────────────────────────────────────────
	ProductionLedger.run(ss, ws, delta_ticks)
	# ── 3b. Coin income (wages, rents, market fees from artisans/merchants/nobles) ─
	_generate_coin(ss, delta_ticks)
	# ── 4. Consumption ───────────────────────────────────────────────────────
	_consume(ss, delta_ticks)

	# ── 5. Price signals ─────────────────────────────────────────────────────
	PriceLedger.update(ss, delta_ticks)

	# ── 6. Prosperity + unrest ───────────────────────────────────────────────
	_update_prosperity_unrest(ss)

	# ── 6b. Population growth / decline ─────────────────────────────────────
	_update_population(ss)

	# ── 7. Worked-acre adjustment ────────────────────────────────────────────
	_adjust_worked_acres(ss)


# ── Starter stock ─────────────────────────────────────────────────────────────
func _seed_starter_stock(ss: SettlementState) -> void:
	var pop: int = ss.total_population()
	if pop <= 0:
		pop = 10
	# 30 days of wheat, a modest timber stockpile, some coin proportional to tier.
	ss.inventory["wheat_bushel"] = float(pop) * 0.015 * 30.0
	ss.inventory["timber_log"]   = maxf(float(ss.acreage.get("woodlot_acres", 10)) * 0.1, 5.0)
	ss.inventory["coin"]         = float(ss.tier + 1) * 20.0

# ── Coin income ───────────────────────────────────────────────────────────────
## Each population class generates coin via wages, rents, and market activity.
## This replaces the old model where coin was simply consumed as if it were a
## commodity good — coin must circulate, not disappear.
func _generate_coin(ss: SettlementState, delta_ticks: int) -> void:
	var cr := ContentRegistry
	var total_coin: float = 0.0
	for cls_id: String in ss.population.keys():
		var cls_def = cr.get_content("population_class", cls_id)
		if cls_def == null or cls_def.is_empty():
			continue
		var income: float = float(cls_def.get("coin_income_per_head_per_tick", 0.0))
		total_coin += float(ss.population.get(cls_id, 0)) * income * float(delta_ticks)
	if total_coin > 0.0:
		ss.inventory["coin"] = ss.inventory.get("coin", 0.0) + total_coin

# ── Consumption ───────────────────────────────────────────────────────────────
func _consume(ss: SettlementState, delta_ticks: int) -> void:
	var cr := ContentRegistry

	for cls_id: String in ss.population.keys():
		var cls_def = cr.get_content("population_class", cls_id)
		if cls_def == null or cls_def.is_empty():
			continue
		var count: int       = ss.population[cls_id]
		var cons: Dictionary = cls_def.get("consumption_per_head_per_tick", {})
		var upr: float       = float(cls_def.get("unrest_per_food_shortage", 0.002))

		for good_id: String in cons.keys():
			var needed:   float = float(count) * float(cons[good_id]) * float(delta_ticks)
			var have:     float = ss.inventory.get(good_id, 0.0)
			var consumed: float = minf(needed, have)
			var shortfall: float = maxf(needed - have, 0.0)

			ss.inventory[good_id] = maxf(have - consumed, 0.0)

			if shortfall > 0.0:
				ss.shortages[good_id] = ss.shortages.get(good_id, 0.0) + shortfall
				# Any consumption shortfall drives unrest; food shortfalls hit hardest
				# because upr is highest for wheat-dependent classes.
				ss.unrest = minf(ss.unrest + shortfall * upr, 1.0)


# ── Prosperity / unrest ───────────────────────────────────────────────────────
func _update_prosperity_unrest(ss: SettlementState) -> void:
	var food_shortfall: float = ss.shortages.get("wheat_bushel", 0.0)

	if food_shortfall > 0.1:
		ss.prosperity = maxf(ss.prosperity - PROSPERITY_SHRINK_RATE, 0.0)
	else:
		ss.prosperity = minf(ss.prosperity + PROSPERITY_GROW_RATE, 1.0)

	if food_shortfall <= 0.0:
		ss.unrest = maxf(ss.unrest - UNREST_DECAY_RATE, 0.0)


# ── Population growth / decline ─────────────────────────────────────────────
## Each class grows or contracts proportionally each pulse.
## Growth requires: no food shortage + prosperity > 0.55 + unrest < 0.3.
## Decline fires on any food shortage or unrest > 0.5 (high flight risk).
## A minimum delta of ±1 prevents small settlements from freezing.
func _update_population(ss: SettlementState) -> void:
	var food_shortfall: float = ss.shortages.get("wheat_bushel", 0.0)
	var growing:  bool = food_shortfall <= 0.0 and ss.prosperity > 0.55 and ss.unrest < 0.3
	var declining: bool = food_shortfall > 0.0 or ss.unrest > 0.5

	if not growing and not declining:
		return

	var rate: float = POPULATION_GROW_RATE if growing else -POPULATION_DECLINE_RATE

	for cls_id: String in ss.population.keys():
		var count: int = ss.population[cls_id]
		if count <= 0:
			continue
		var delta: int = int(float(count) * rate)
		# Guarantee at least ±1 so hamlets are not frozen by rounding.
		if delta == 0:
			delta = 1 if rate > 0.0 else -1
		ss.population[cls_id] = maxi(0, count + delta)


# ── Worked-acre adjustment ────────────────────────────────────────────────────
func _adjust_worked_acres(ss: SettlementState) -> void:
	var arable: int = ss.acreage.get("arable_acres", 0)
	if arable <= 0:
		return
	# Target: 40–90 % of arable depending on prosperity.
	var target: int = int(float(arable) * (0.4 + ss.prosperity * 0.5))
	ss.acreage["worked_acres"] = clampi(target, 0, arable)
