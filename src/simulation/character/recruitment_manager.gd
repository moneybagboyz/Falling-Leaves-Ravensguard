## RecruitmentManager — handles player requests to hire NPC followers.
##
## Static API called from dialogue UI when "Recruit as follower" is chosen.
## Acceptance requires:
##   (a) offered_wage >= NPC's minimum wage (from population_class data)
##   (b) player reputation >= 0.0 with NPC's home settlement (or NPC has none)
##
## On success: NPC person_id added to player.follower_ids;
##             NPC's active_role set; labor slot freed.
class_name RecruitmentManager
extends RefCounted

## Absolute minimum wage offer accepted regardless of class (floor).
const WAGE_FLOOR: float = 0.5


## Attempt to recruit `npc_id` as a follower of `player`.
## offered_wage is coin-per-tick.
## Returns "" on success or a localised error string.
static func recruit(
		player:       PersonState,
		npc_id:       String,
		offered_wage: float,
		ws:           WorldState) -> String:

	var npc: PersonState = ws.characters.get(npc_id)
	if npc == null:
		return "That person is not here."

	# Already a follower of someone?
	if npc.active_role in ["guard", "laborer", "assistant"]:
		return "%s is already employed." % npc.name

	# Already a follower of this player?
	if npc_id in player.follower_ids:
		return "%s already follows you." % npc.name

	# ── Wage check ────────────────────────────────────────────────────────────
	var min_wage: float = _min_wage(npc, ws)
	if offered_wage < min_wage:
		var fmt: String = "%.1f" % min_wage
		return "%s wants at least %s coin/day." % [npc.name, fmt]

	# ── Reputation check ──────────────────────────────────────────────────────
	var home_sid: String = npc.home_settlement_id
	if home_sid != "":
		# Check player's reputation with the NPC's home settlement.
		if not ReputationEvents.meets_threshold(player, home_sid, 0.0):
			return "Your reputation in %s is too poor." % home_sid

		# Also check faction reputation if the settlement has a faction.
		var ss: SettlementState = ws.get_settlement(home_sid)
		if ss != null and ss.faction_id != "":
			if not ReputationEvents.meets_threshold(player, ss.faction_id, 0.0):
				return "The %s faction distrusts you." % ss.faction_id

	# ── Accept ────────────────────────────────────────────────────────────────
	player.follower_ids.append(npc_id)

	# Assign a role based on the NPC's population class.
	npc.active_role = _role_for_class(npc.population_class)

	# Free the labor slot if the NPC held one.
	_free_labor_slot(npc_id, npc.home_settlement_id, ws)

	# Update GroupState pay.
	if not ws.player_group.is_empty():
		var group: GroupState = GroupState.from_dict(ws.player_group)
		group.member_ids.assign(player.follower_ids)
		group.recalculate_pay(ws)
		ws.player_group = group.to_dict()

	# Small reputation gain in the NPC's home settlement for being a fair employer.
	if home_sid != "":
		ReputationEvents.gain(player, "contract_fulfilled", [home_sid])

	return ""


## Returns a list of recruitable NPCs from a settlement's character pool.
## Format: Array[{person_id, name, population_class, min_wage}]
static func get_recruitable(player: PersonState, settlement_id: String, ws: WorldState) -> Array:
	var out: Array = []
	for pid: String in ws.characters.keys():
		if pid == player.person_id:
			continue
		var npc: PersonState = ws.characters.get(pid)
		if npc == null:
			continue
		if npc.home_settlement_id != settlement_id:
			continue
		# Must be idle or wandering.
		if npc.active_role in ["guard", "laborer", "assistant"]:
			continue
		out.append({
			"person_id":       pid,
			"name":            npc.name,
			"population_class": npc.population_class,
			"min_wage":        _min_wage(npc, ws),
		})
	return out


# ── Helpers ───────────────────────────────────────────────────────────────────

static func _min_wage(npc: PersonState, _ws: WorldState) -> float:
	var cls_def: Dictionary = ContentRegistry.get_content(
		"population_class", npc.population_class)
	if cls_def.is_empty():
		return WAGE_FLOOR
	# Derive minimum wage from class coin_income_per_head_per_tick.
	var income: float = float(cls_def.get("coin_income_per_head_per_tick", 0.05))
	return maxf(income * 1.2, WAGE_FLOOR)


static func _role_for_class(pop_class: String) -> String:
	match pop_class:
		"peasant", "artisan": return "laborer"
		"merchant":           return "assistant"
		"noble":              return "guard"
		_:                    return "laborer"


static func _free_labor_slot(npc_id: String, settlement_id: String, ws: WorldState) -> void:
	if settlement_id == "":
		return
	var ss: SettlementState = ws.get_settlement(settlement_id)
	if ss == null:
		return
	for slot: Dictionary in ss.labor_slots:
		if slot.get("worker_id", "") == npc_id:
			slot["worker_id"] = ""
			slot["is_filled"] = false
			break
