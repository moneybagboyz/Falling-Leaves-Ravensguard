## WorkSystem — wages and skill XP for all working characters each production tick.
##
## Registered with TickScheduler.PRODUCTION_PULSE (strategic cadence, once per day).
##
## For every character (player + persisted NPCs + active npc_pool entries) that
## has an active_role and work_cell_id set:
##   1. Locate the matching labor slot in the character's home settlement.
##   2. Deduct the slot's wage_per_day from the settlement's coin inventory.
##   3. Credit the worker's PersonState.coin field (PersonState.coin, P3-10).
##   4. Award skill XP for the slot's required skill (hunger penalises XP).
##
## If the settlement cannot meet the full wage, payment is skipped that tick.
## Debt system deferred to Phase 5.
##
## Public UI API:
##   assign_player_to_slot(slot_index: int) -> bool
##   remove_player_from_slot() -> void
class_name WorkSystem
extends RefCounted

## Base XP per work tick. With xp_per_level = 100:
## progress += 5.0 / 100 = 0.05 per day → 20 days to reach level 1.
const XP_PER_TICK: float = 5.0

var _world_state: WorldState = null


func setup(ws: WorldState) -> void:
	_world_state = ws


# ---------------------------------------------------------------------------
# Tick hook — PRODUCTION_PULSE
# ---------------------------------------------------------------------------

func tick_work(_tick: int) -> void:
	if _world_state == null:
		return
	# Process all characters (player + persistent NPCs).
	for pid: String in _world_state.characters:
		var person: PersonState = _world_state.characters[pid]
		if person.active_role == "" or person.work_cell_id == "":
			continue
		_process_worker(person)


func _process_worker(person: PersonState) -> void:
	# Prefer home settlement; fallback to cell owner.
	var sid: String = person.home_settlement_id
	if sid == "":
		var cell_data: Dictionary = _world_state.world_tiles.get(person.work_cell_id, {})
		sid = cell_data.get("owner_settlement_id", "")
	if sid == "":
		return
	var ss: SettlementState = _world_state.get_settlement(sid)
	if ss == null:
		return

	# Find the matching slot (exact match first, then role-only fallback).
	var slot: Dictionary = {}
	for s: Dictionary in ss.labor_slots:
		if s.get("slot_id",  "") == person.active_role and \
		   s.get("worker_id","") == person.person_id:
			slot = s
			break
	if slot.is_empty():
		for s: Dictionary in ss.labor_slots:
			if s.get("slot_id", "") == person.active_role:
				slot = s
				break
	if slot.is_empty():
		return

	# ── Wages ─────────────────────────────────────────────────────────────
	var wage: float         = float(slot.get("wage_per_day", 1))
	var settlement_coin: float = float(ss.inventory.get("coin", 0.0))
	if settlement_coin < wage:
		return  # Can't pay this tick.
	ss.inventory["coin"] = settlement_coin - wage
	person.coin += wage

	# ── Skill XP (with hunger penalty above 50%) ───────────────────────────
	var skill_id: String = slot.get("skill_required", "")
	if skill_id != "":
		var hunger: float   = person.needs.get("hunger", 0.0)
		var xp_mult: float  = 1.0 - clampf(hunger - 0.5, 0.0, 0.5)
		person.award_skill_xp(skill_id, XP_PER_TICK * xp_mult)


# ---------------------------------------------------------------------------
# Public API — SettlementView UI
# ---------------------------------------------------------------------------

## Assign the player to a specific labor slot (by index into ss.labor_slots).
## Returns true on success, false if the slot is already taken by someone else.
func assign_player_to_slot(slot_index: int) -> bool:
	if _world_state == null:
		return false
	var pid: String = _world_state.player_character_id
	if pid == "":
		return false
	var player: PersonState = _world_state.characters.get(pid)
	if player == null:
		return false

	var cid: String         = _world_state.player_location.get("cell_id", "")
	var cell_data: Dictionary = _world_state.world_tiles.get(cid, {})
	var sid: String         = cell_data.get("owner_settlement_id", "")
	if sid == "":
		return false
	var ss: SettlementState = _world_state.get_settlement(sid)
	if ss == null or slot_index < 0 or slot_index >= ss.labor_slots.size():
		return false

	var slot: Dictionary = ss.labor_slots[slot_index]
	if slot.get("is_filled", false) and slot.get("worker_id", "") != pid:
		return false  # Taken by someone else.

	remove_player_from_slot()  # Release any prior slot first.

	slot["is_filled"] = true
	slot["worker_id"] = pid
	ss.labor_slots[slot_index] = slot
	player.active_role  = slot.get("slot_id", "")
	player.work_cell_id = slot.get("cell_id", "")
	return true


## Release the player from their current labor slot.
func remove_player_from_slot() -> void:
	if _world_state == null:
		return
	var pid: String = _world_state.player_character_id
	if pid == "":
		return
	var player: PersonState = _world_state.characters.get(pid)
	if player == null or player.active_role == "":
		return

	for ss: SettlementState in _world_state.get_all_settlements():
		for i: int in ss.labor_slots.size():
			var slot: Dictionary = ss.labor_slots[i]
			if slot.get("worker_id", "") == pid:
				slot["is_filled"] = false
				slot["worker_id"] = ""
				ss.labor_slots[i] = slot

	player.active_role  = ""
	player.work_cell_id = ""
