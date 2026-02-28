## TradePartySpawner — decides when and where to spawn trade parties.
##
## Called at the end of each SettlementPulse after all settlements have been
## ticked. For each settlement with a significant surplus of a good, finds the
## nearest connected settlement that has a shortage of the same good and spawns
## a TradePartyState carrying half the surplus.
##
## Constraints:
##   - Only one party per (origin, good, destination) pair at a time.
##   - Both origin and destination must be SettlementState objects in memory.
##   - A route path must exist for the chosen edge.
class_name TradePartySpawner
extends RefCounted

## Minimum inventory above local demand before a settlement will export.
const SURPLUS_THRESHOLD: float = 20.0
## Minimum shortage at the destination before a party is warranted.
const SHORTAGE_THRESHOLD: float = 5.0
## Fraction of surplus to load onto the party.
const CARGO_FRACTION: float = 0.5
## Days of supply below which a destination is considered "running low".
## Trade parties are dispatched proactively before a crisis hits.
const DAYS_BUFFER: float = 14.0


static func try_spawn_all(ws: WorldState, tick: int) -> void:
	for sid: String in ws.settlements.keys():
		var ss := _as_ss(ws, sid)
		if ss == null:
			continue
		_try_spawn_from(ss, sid, ws, tick)


# ── Per-settlement spawn attempt ──────────────────────────────────────────────
static func _try_spawn_from(
		ss:  SettlementState,
		sid: String,
		ws:  WorldState,
		_tick: int
) -> void:
	var edges: Array = ws.routes.get(sid, [])
	if edges.is_empty():
		return

	# Find the surplus good with the largest excess.
	for good_id: String in ss.inventory.keys():
		var surplus: float = ss.inventory.get(good_id, 0.0) - SURPLUS_THRESHOLD
		if surplus <= 0.0:
			continue

		# Find the best destination: largest shortage of this good
		# among directly connected settlements.
		var best_dest:     String = ""
		var best_shortage: float  = 0.0
		var best_path:     Array  = []

		for edge: Dictionary in edges:
			var to_id: String = edge.get("to_id", "")
			if to_id.is_empty():
				continue
			var dest_ss := _as_ss(ws, to_id)
			if dest_ss == null:
				continue

			# Score destination need: urgent shortage scores high;
			# low stock (< DAYS_BUFFER days) also triggers proactive resupply.
			var shortage:  float = dest_ss.shortages.get(good_id, 0.0)
			var days_left: float = _days_of_supply(dest_ss, good_id)
			var need_score: float = 0.0
			if shortage > SHORTAGE_THRESHOLD:
				need_score = shortage * 2.0
			elif days_left < DAYS_BUFFER:
				need_score = (DAYS_BUFFER - days_left) + 0.1

			if need_score > 0.0 and need_score > best_shortage:
				best_shortage = need_score
				best_dest     = to_id
				best_path     = edge.get("path", [])

		if best_dest.is_empty():
			continue

		# Skip if a party is already en route with the same cargo.
		if _party_already_exists(ws, sid, best_dest, good_id):
			continue

		_spawn(ws, ss, sid, best_dest, good_id, surplus * CARGO_FRACTION, best_path)
		# At most one party per surplus good per pulse; continue to next good.
		continue


# ── Spawn a new trade party ───────────────────────────────────────────────────
static func _spawn(
		ws:        WorldState,
		origin_ss: SettlementState,
		origin_id: String,
		dest_id:   String,
		good_id:   String,
		cargo_amt: float,
		path:      Array
) -> void:
	var tp           := TradePartyState.new()
	tp.party_id       = "party_%06d" % ws.trade_parties.size()
	tp.origin_id      = origin_id
	tp.dest_id        = dest_id
	tp.cargo[good_id] = cargo_amt
	tp.path           = path
	tp.path_idx       = 0

	# Deduct cargo from origin inventory.
	origin_ss.inventory[good_id] = maxf(
		origin_ss.inventory.get(good_id, 0.0) - cargo_amt, 0.0
	)

	ws.trade_parties[tp.party_id] = tp.to_dict()


# ── Helpers ───────────────────────────────────────────────────────────────────
static func _party_already_exists(
		ws:        WorldState,
		origin_id: String,
		dest_id:   String,
		good_id:   String
) -> bool:
	for pid: String in ws.trade_parties.keys():
		var p: Dictionary = ws.trade_parties[pid]
		if (p.get("origin_id", "") == origin_id
				and p.get("dest_id", "") == dest_id
				and p.get("cargo", {}).has(good_id)):
			return true
	return false


static func _as_ss(ws: WorldState, sid: String) -> SettlementState:
	var sv = ws.settlements.get(sid)
	if sv is SettlementState:
		return sv
	return null


## Estimated days of supply remaining for a good at a given settlement.
static func _days_of_supply(ss: SettlementState, good_id: String) -> float:
	var inv:   float = ss.inventory.get(good_id, 0.0)
	var daily: float = _est_daily_demand(ss, good_id)
	return inv / maxf(daily, 0.0001)


## Estimates daily consumption of a good based on population class definitions.
static func _est_daily_demand(ss: SettlementState, good_id: String) -> float:
	var cr := ContentRegistry
	var demand: float = 0.0
	for cls_id: String in ss.population.keys():
		var cls_def = cr.get_content("population_class", cls_id)
		if cls_def == null or cls_def.is_empty():
			continue
		var cons: Dictionary = cls_def.get("consumption_per_head_per_tick", {})
		if cons.has(good_id):
			demand += float(ss.population.get(cls_id, 0)) * float(cons[good_id])
	return demand
