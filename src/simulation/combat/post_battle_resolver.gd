## PostBattleResolver — applies battle results back to WorldState after combat ends.
##
## Call resolve() immediately after CombatResolver.resolve_turn() returns true.
## Handles:
##   1. Write wounds from CombatantState back to PersonState.body_state.
##   2. Write stamina back to PersonState.stamina.
##   3. Remove dead entities from WorldState.characters / npc_pool.
##   4. Remove dead bandit formations — erase bandit_camp from world tile if cleared.
##   5. Build loot pool from defeated combatants' equipment_refs.
##   6. Update player location to remain at the battle tile.
##   7. Clear WorldState.active_battle.
class_name PostBattleResolver
extends RefCounted


## Resolve all aftermath. Returns a summary dict for the UI to display.
## Summary keys:
##   result:     "player_victory" | "player_defeat" | "draw"
##   casualties: Array of { name, team, cause } dicts
##   loot:       Array of item_id Strings
static func resolve(battle: BattleState, world_state: WorldState) -> Dictionary:
	var summary: Dictionary = {
		"result":     battle.result,
		"casualties": [],
		"loot":       [],
	}

	# ── 1. Write combatant state back to PersonState ───────────────────────
	for cid: String in battle.combatants:
		var c: CombatantState = battle.combatants[cid]
		var person: PersonState = _find_person(cid, world_state)
		if person == null:
			continue

		# Write wounds into body_state (keyed by zone_id).
		person.body_state = {}
		for zone_id: String in c.body_zones:
			var wounds: Array = c.body_zones[zone_id]
			if not wounds.is_empty():
				person.body_state[zone_id] = wounds.duplicate(true)

		# Write stamina.
		person.stamina = c.stamina

		# If dead → remove from world.
		if c.is_dead:
			summary["casualties"].append({
				"name":  c.display_name,
				"team":  c.team_id,
				"cause": _death_cause(c),
			})
			# Collect their equipment as loot if they were enemies.
			if c.team_id == "enemy":
				for slot: String in c.equipment_refs:
					var item_id: String = c.equipment_refs[slot]
					if item_id != "":
						battle.loot_pool.append(item_id)
						summary["loot"].append(item_id)
			# Remove from world state.
			world_state.characters.erase(cid)
			world_state.npc_pool.erase(cid)

	# ── 2. Remove cleared bandit camps from the world tile ─────────────────
	if battle.result == "player_victory" and battle.map_tile != "":
		var tile: Dictionary = world_state.world_tiles.get(battle.map_tile, {})
		if tile.get("building_id", "") == "bandit_camp":
			tile["building_id"]        = ""
			tile["hostile"]            = false
			tile["bandit_group_size"]  = 0
			tile["bandit_gear_tier"]   = ""

	# ── 3. Store loot pool on the battle for the UI to present ─────────────
	battle.loot_pool.assign(summary["loot"])

	# ── 4. Clear active battle ──────────────────────────────────────────────
	world_state.active_battle = null

	return summary


# ── Helpers ───────────────────────────────────────────────────────────────────

static func _find_person(person_id: String, world_state: WorldState) -> PersonState:
	if world_state.characters.has(person_id):
		return world_state.characters[person_id]
	if world_state.npc_pool.has(person_id):
		return world_state.npc_pool[person_id]
	return null


static func _death_cause(c: CombatantState) -> String:
	# Find the most severe wound.
	for zone_id: String in c.body_zones:
		for wound: Dictionary in c.body_zones[zone_id]:
			if wound.get("severity", "") == "lethal":
				return "lethal wound to %s" % zone_id.replace("_", " ")
	if c.shock >= 1.0:
		return "shock"
	return "unknown"
