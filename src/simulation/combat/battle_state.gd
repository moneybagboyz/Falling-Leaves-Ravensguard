## BattleState — top-level container for an active tactical battle.
##
## Holds all FormationState and CombatantState records for one fight.
## Created at combat entry (P4-11), destroyed at combat exit (P4-19).
## Stored on WorldState.active_battle (null when no battle is in progress).
##
## Map context:
##   If battle started outdoors, map_type = "subregion" and map_tile = the world
##   tile key where combat was triggered.
##   If battle started inside a building, map_type = "local" and
##   map_building_id = the building data id for the local_layout.
class_name BattleState
extends RefCounted

# ── Phase constants ────────────────────────────────────────────────────────────
const PHASE_PLANNING:   String = "planning"
const PHASE_RESOLVING:  String = "resolving"
const PHASE_RESULTS:    String = "results"

# ── Identity ──────────────────────────────────────────────────────────────────
var battle_id: String = ""

## "subregion" or "local"
var map_type: String = "subregion"

## World tile key (e.g. "32,18") — used when map_type == "subregion".
var map_tile: String = ""

## Building ID — used when map_type == "local".
var map_building_id: String = ""

## Current battle phase.
var phase: String = PHASE_PLANNING

## WEGO turn counter.
var turn: int = 0

# ── Combatants ────────────────────────────────────────────────────────────────
## combatant_id → CombatantState
var combatants: Dictionary = {}

# ── Formations ────────────────────────────────────────────────────────────────
## formation_id → FormationState
var formations: Dictionary = {}

# ── Result ────────────────────────────────────────────────────────────────────
## Set after the battle ends.
## "player_victory" | "player_defeat" | "draw" | ""
var result: String = ""

## Loot pool collected from defeated combatants. Array of item_id Strings.
var loot_pool: Array[String] = []


# ── Helpers ───────────────────────────────────────────────────────────────────

func add_combatant(c: CombatantState) -> void:
	combatants[c.combatant_id] = c


func add_formation(f: FormationState) -> void:
	formations[f.formation_id] = f


func get_combatant(cid: String) -> CombatantState:
	return combatants.get(cid)


func get_formation(fid: String) -> FormationState:
	return formations.get(fid)


## Returns all formations for a given team.
func formations_for_team(team: String) -> Array:
	var out: Array = []
	for fid: String in formations:
		var f: FormationState = formations[fid]
		if f.team_id == team:
			out.append(f)
	return out


## Returns true if all formations on a given team are destroyed.
func team_destroyed(team: String) -> bool:
	for fid: String in formations:
		var f: FormationState = formations[fid]
		if f.team_id == team and not f.is_destroyed(combatants):
			return false
	return true


## Evaluate battle end condition. Returns "" if still ongoing.
func evaluate_result() -> String:
	var player_done: bool = team_destroyed("player")
	var enemy_done:  bool = team_destroyed("enemy")
	if player_done and enemy_done:
		return "draw"
	if player_done:
		return "player_defeat"
	if enemy_done:
		return "player_victory"
	return ""


# ── Serialisation ─────────────────────────────────────────────────────────────

func to_dict() -> Dictionary:
	var c_dict: Dictionary = {}
	for cid: String in combatants:
		c_dict[cid] = combatants[cid].to_dict()
	var f_dict: Dictionary = {}
	for fid: String in formations:
		f_dict[fid] = formations[fid].to_dict()
	return {
		"battle_id":       battle_id,
		"map_type":        map_type,
		"map_tile":        map_tile,
		"map_building_id": map_building_id,
		"phase":           phase,
		"turn":            turn,
		"combatants":      c_dict,
		"formations":      f_dict,
		"result":          result,
		"loot_pool":       loot_pool.duplicate(),
	}


static func from_dict(d: Dictionary) -> BattleState:
	var b := BattleState.new()
	b.battle_id       = d.get("battle_id",       "")
	b.map_type        = d.get("map_type",         "subregion")
	b.map_tile        = d.get("map_tile",         "")
	b.map_building_id = d.get("map_building_id",  "")
	b.phase           = d.get("phase",            PHASE_PLANNING)
	b.turn            = int(d.get("turn",         0))
	b.result          = d.get("result",           "")
	b.loot_pool.assign(d.get("loot_pool", []))
	for cid: String in d.get("combatants", {}):
		b.combatants[cid] = CombatantState.from_dict(d["combatants"][cid])
	for fid: String in d.get("formations", {}):
		b.formations[fid] = FormationState.from_dict(d["formations"][fid])
	return b
