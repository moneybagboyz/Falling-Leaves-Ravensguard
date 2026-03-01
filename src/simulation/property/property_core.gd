## PropertyCore — tracks building/land ownership and routes income to owners.
##
## Called as a post-pulse hook inside SettlementPulse.tick_one() after
## production has run for a settlement. Redirects the owner's income share
## from the settlement's coin pool to the owner's PersonState.coin balance.
##
## WorldState.property_ledger: building_instance_id -> owner_person_id
##
## Per-building income is derived from the building's production output
## over the pulse, calculated as (settlement coin generated * owner_share).
## Upkeep (from building JSON "upkeep_per_season") is deducted each pulse.
##
## Static API — no instance state; all methods take WorldState directly.
class_name PropertyCore
extends RefCounted

## Fraction of a building's net coin output credited to the owner each pulse.
const OWNER_INCOME_SHARE: float = 0.40

## Fraction of upkeep_per_season charged per strategic tick.
## upkeep_per_season / UPKEEP_TICKS_PER_SEASON ≈ daily upkeep.
const UPKEEP_TICKS_PER_SEASON: float = 90.0


# ── Ownership registration ────────────────────────────────────────────────────

## Record ownership of a building slot by an entity.
## instance_key can be any unique string: building JSON id + cell_id is
## recommended ("smithy:14,22").
static func register_ownership(
		ws: WorldState, instance_key: String, owner_id: String) -> void:
	ws.property_ledger[instance_key] = owner_id


## Remove ownership record (e.g. sold, destroyed).
static func clear_ownership(ws: WorldState, instance_key: String) -> void:
	ws.property_ledger.erase(instance_key)


## Return the owner person_id of an instance, or "" if unowned.
static func owner_of(ws: WorldState, instance_key: String) -> String:
	return ws.property_ledger.get(instance_key, "")


## Return all instance_keys owned by a person.
static func owned_by(ws: WorldState, person_id: String) -> Array[String]:
	var out: Array[String] = []
	for key: String in ws.property_ledger:
		if ws.property_ledger[key] == person_id:
			out.append(key)
	return out


# ── Post-pulse income distribution ───────────────────────────────────────────

## Called by SettlementPulse._tick_one() after _generate_coin().
## Distributes owner income shares and deducts building upkeep.
static func apply_income_and_upkeep(
		ss: SettlementState, ws: WorldState, delta_ticks: int) -> void:

	for entry: Dictionary in ss.labor_slots:
		var bid: String      = entry.get("building_id", "")
		var cell: String     = entry.get("cell_id",     "")
		if bid == "" or cell == "":
			continue
		var instance_key: String = bid + ":" + cell
		var owner_id: String = ws.property_ledger.get(instance_key, "")
		if owner_id == "":
			continue
		var owner: PersonState = ws.characters.get(owner_id)
		if owner == null:
			continue

		# ── Income share ──────────────────────────────────────────────────────
		# Use the coin generated this pulse as a proxy for building productivity.
		# We allocate a flat per-slot share so multiple slots in the same
		# building don't over-pay.
		var bdef: Dictionary = ContentRegistry.get_content("building", bid)
		if bdef.is_empty():
			continue

		# Derive per-slot income from upkeep_per_season as a baseline proxy.
		var upkeep_season: float = float(
			(bdef.get("upkeep_per_season", {}) as Dictionary).get("coin", 0.0))
		var slot_count: int = max(1, _count_slots(ss, bid, cell))

		# Income is owner_share * ~2× upkeep (buildings should profit slightly).
		var income_per_pulse: float = (upkeep_season / UPKEEP_TICKS_PER_SEASON) \
			* 2.0 * OWNER_INCOME_SHARE * float(delta_ticks) / float(slot_count)
		owner.coin += income_per_pulse

		# ── Upkeep deduction ─────────────────────────────────────────────────
		var upkeep_per_tick: float = (upkeep_season / UPKEEP_TICKS_PER_SEASON) \
			* float(delta_ticks) / float(slot_count)
		owner.coin = maxf(owner.coin - upkeep_per_tick, 0.0)


## Applies income from a player-owned camp: all surplus coin in the camp's
## settlement goes directly to the owner instead of pooling.
static func apply_camp_income(ss: SettlementState, ws: WorldState, owner_id: String) -> void:
	var owner: PersonState = ws.characters.get(owner_id)
	if owner == null:
		return
	# Redirect the settlement's generated coin directly to the owner's wallet.
	# ss.inventory["coin"] is used as the camp's coin store; we transfer it.
	var camp_coin: float = float(ss.inventory.get("coin", 0.0))
	if camp_coin > 0.1:
		owner.coin += camp_coin * OWNER_INCOME_SHARE
		ss.inventory["coin"] = camp_coin * (1.0 - OWNER_INCOME_SHARE)


# ── Helpers ───────────────────────────────────────────────────────────────────

static func _count_slots(ss: SettlementState, bid: String, cell: String) -> int:
	var count: int = 0
	for s: Dictionary in ss.labor_slots:
		if s.get("building_id", "") == bid and s.get("cell_id", "") == cell:
			count += 1
	return count
