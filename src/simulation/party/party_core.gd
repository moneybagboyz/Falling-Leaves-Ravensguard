## PartyCore — moves trade parties along their paths every MOVEMENT tick.
##
## Registered as a hook on TickScheduler.Phase.MOVEMENT by Bootstrap.
## Each call advances every active trade party; on arrival the cargo is
## deposited into the destination settlement's inventory and the party is
## removed.
class_name PartyCore
extends RefCounted

var _world_state: WorldState = null


func setup(ws: WorldState) -> void:
	_world_state = ws


## Called every MOVEMENT tick by the TickScheduler hook system.
func tick_movement(_tick: int) -> void:
	if _world_state == null:
		return
	var ws: WorldState = _world_state

	var arrived: Array[String] = []

	for pid: String in ws.trade_parties.keys():
		var pdata: Dictionary = ws.trade_parties[pid]
		var tp := TradePartyState.from_dict(pdata)

		tp.path_idx       = tp.path_idx + int(tp.speed_tiles_per_tick)
		tp.ticks_en_route += 1

		if tp.path.is_empty() or tp.path_idx >= tp.path.size():
			_deliver(tp, ws)
			arrived.append(pid)
		else:
			# Write the updated state back as a plain dict.
			ws.trade_parties[pid] = tp.to_dict()

	for pid: String in arrived:
		ws.trade_parties.erase(pid)


# ── Delivery ──────────────────────────────────────────────────────────────────
static func _deliver(tp: TradePartyState, ws: WorldState) -> void:
	var dest_ss:   SettlementState = _as_ss(ws, tp.dest_id)
	var origin_ss: SettlementState = _as_ss(ws, tp.origin_id)
	if dest_ss == null:
		push_warning("PartyCore: destination '%s' not found for party '%s'; cargo lost."
				% [tp.dest_id, tp.party_id])
		return

	# Calculate cargo value at destination prices (less 20% transport cost).
	var payment: float = 0.0
	for good_id: String in tp.cargo.keys():
		var qty:   float = tp.cargo.get(good_id, 0.0)
		var price: float = dest_ss.prices.get(good_id, _good_base_value(good_id))
		payment += qty * price
	payment *= 0.8   # 20% kept by the party as transport cost

	# Deposit goods into the destination inventory.
	for good_id: String in tp.cargo.keys():
		var incoming: float = tp.cargo.get(good_id, 0.0)
		dest_ss.inventory[good_id] = dest_ss.inventory.get(good_id, 0.0) + incoming

	# Destination pays origin in coin; capped at what the destination can afford.
	var dest_coin:  float = dest_ss.inventory.get("coin", 0.0)
	var actual_pay: float = minf(payment, dest_coin)
	dest_ss.inventory["coin"] = maxf(dest_coin - actual_pay, 0.0)
	if origin_ss != null:
		origin_ss.inventory["coin"] = origin_ss.inventory.get("coin", 0.0) + actual_pay


## Look up a good's base value from ContentRegistry, fallback 1.0.
static func _good_base_value(good_id: String) -> float:
	var good := ContentRegistry.get_content("good", good_id)
	if not good.is_empty():
		return float(good.get("base_value", 1.0))
	return 1.0


# ── Helper ────────────────────────────────────────────────────────────────────
static func _as_ss(ws: WorldState, sid: String) -> SettlementState:
	var sv = ws.settlements.get(sid)
	if sv is SettlementState:
		return sv
	return null
