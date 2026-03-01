## FollowerSystem — tick hook managing follower wages, food, and group morale.
##
## Registered on TickScheduler.Phase.PRODUCTION_PULSE by Bootstrap.
## Each pulse: deducts wages, checks food supply, updates GroupState.morale,
## triggers desertion if morale < DESERTION_THRESHOLD.
class_name FollowerSystem
extends RefCounted

var _world_state: WorldState = null


func setup(ws: WorldState) -> void:
	_world_state = ws


func tick_followers(tick: int) -> void:
	var ws: WorldState = _world_state
	if ws == null or ws.player_character_id == "":
		return

	var player: PersonState = ws.characters.get(ws.player_character_id)
	if player == null or player.follower_ids.is_empty():
		return

	# Load or create GroupState.
	var group: GroupState
	if ws.player_group.is_empty():
		group = GroupState.new()
		group.group_id = "player_group"
	else:
		group = GroupState.from_dict(ws.player_group)

	# Keep member_ids in sync with player's follower_ids.
	group.member_ids.assign(player.follower_ids)
	group.recalculate_pay(ws)

	# ── Wages ─────────────────────────────────────────────────────────────────
	var wages_due: float = group.pay_per_tick * float(TickScheduler.STRATEGIC_CADENCE)
	var wages_paid: bool = player.coin >= wages_due
	if wages_paid:
		player.coin -= wages_due
		group.last_paid_tick = tick
	else:
		# Pay what's available; shortfall causes morale hit.
		player.coin = 0.0
		wages_paid = false

	# ── Food check (follower food comes from player camp or player inventory) ─
	var food_available: bool = _check_follower_food(player, ws, group.member_ids.size())
	if food_available:
		group.last_fed_tick = tick

	# ── Morale update ─────────────────────────────────────────────────────────
	if wages_paid and food_available:
		group.morale = minf(group.morale + GroupState.MORALE_RECOVER_RATE, 1.0)
	else:
		if not wages_paid:
			group.morale = maxf(group.morale - GroupState.MORALE_DECAY_WAGES, 0.0)
			ReputationEvents.lose(player, "wages_defaulted",
				_faction_ids_of_followers(ws, group.member_ids))
		if not food_available:
			group.morale = maxf(group.morale - GroupState.MORALE_DECAY_FOOD, 0.0)

	# ── Desertion ─────────────────────────────────────────────────────────────
	if group.morale < GroupState.DESERTION_THRESHOLD and not group.member_ids.is_empty():
		var deserter_id: String = group.member_ids[0]
		_remove_follower(player, ws, group, deserter_id)
		push_warning("[FollowerSystem] '%s' deserted (morale %.2f)." \
			% [deserter_id, group.morale])

	ws.player_group = group.to_dict()


# ── Helpers ───────────────────────────────────────────────────────────────────

## Attempt to consume food (wheat_bushel) for all followers.
## Checks player's camp stock first, then player's carried items.
## Returns true if food demand was fully met.
static func _check_follower_food(
		player: PersonState, ws: WorldState, count: int) -> bool:
	if count == 0:
		return true
	# ~0.015 wheat_bushel per person per tick (same as peasant consumption).
	var needed: float = float(count) * 0.015 * float(TickScheduler.STRATEGIC_CADENCE)

	# Try camp stock.
	for sid: String in ws.settlements:
		var ss := ws.get_settlement(sid)
		if ss == null or not ss.is_player_camp:
			continue
		var owner: String = ws.property_ledger.get("camp:" + sid, "")
		if owner != player.person_id:
			continue
		var have: float = float(ss.inventory.get("wheat_bushel", 0.0))
		if have >= needed:
			ss.inventory["wheat_bushel"] = have - needed
			return true
		else:
			needed -= have
			ss.inventory["wheat_bushel"] = 0.0

	# Try player's carried items as whole-unit food.
	var held: int = player.carried_items.count("wheat_bushel")
	var consume: int = mini(held, int(ceil(needed)))
	for _i: int in range(consume):
		var idx: int = player.carried_items.find("wheat_bushel")
		if idx >= 0:
			player.carried_items.remove_at(idx)
	return consume >= int(ceil(needed))


static func _remove_follower(
		player: PersonState,
		ws: WorldState,
		group: GroupState,
		follower_id: String) -> void:
	player.follower_ids.erase(follower_id)
	group.member_ids.erase(follower_id)
	# Reset the NPC's role so it can be recruited by others.
	var npc: PersonState = ws.characters.get(follower_id)
	if npc != null:
		npc.active_role = ""


static func _faction_ids_of_followers(ws: WorldState, member_ids: Array) -> Array:
	var fids: Array = []
	for pid: String in member_ids:
		var p: PersonState = ws.characters.get(pid)
		if p == null:
			continue
		var sid: String = p.home_settlement_id
		if sid != "" and sid not in fids:
			fids.append(sid)
	return fids
