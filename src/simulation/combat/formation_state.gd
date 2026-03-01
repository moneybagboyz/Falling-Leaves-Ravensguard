## FormationState — a squad-level unit in tactical combat.
##
## The player and AI issue orders at formation level.
## Each formation contains N CombatantState members (N=1 in Phase 4 tests;
## N=10–50 in later large battles).
##
## WEGO resolution:
##   1. Player picks a formation from the sidebar → selects order from dropdown.
##   2. AI picks orders for all enemy formations.
##   3. All formations execute simultaneously each step of the resolution tick.
##
## Morale:
##   Starts at 1.0. Drops when members are killed (−0.1) or shocked (−0.05).
##   At morale < 0.2 the formation automatically issues "retreat".
class_name FormationState
extends RefCounted

# ── Order constants ───────────────────────────────────────────────────────────
const ORDER_ADVANCE: String  = "advance"
const ORDER_HOLD:    String  = "hold"
const ORDER_CHARGE:  String  = "charge"
const ORDER_FLANK:   String  = "flank"
const ORDER_RETREAT: String  = "retreat"
const VALID_ORDERS: Array[String] = ["advance", "hold", "charge", "flank", "retreat"]

const MORALE_DEATH_PENALTY:  float = 0.10
const MORALE_SHOCK_PENALTY:  float = 0.05
const MORALE_ROUT_THRESHOLD: float = 0.20

# ── Identity ──────────────────────────────────────────────────────────────────
var formation_id: String = ""

## "player" or "enemy" (or faction_id for multi-faction battles later).
var team_id: String = ""

## Display label shown in the sidebar (e.g. "Spearmen I", "Bandits").
var label: String = ""

# ── Members ───────────────────────────────────────────────────────────────────
## Ordered list of combatant_id Strings belonging to this formation.
var member_ids: Array[String] = []

# ── Position ──────────────────────────────────────────────────────────────────
## The tile the formation is anchored to / marching toward.
## Individual members spread around this anchor.
var anchor_pos: Vector2i = Vector2i.ZERO

# ── Orders ────────────────────────────────────────────────────────────────────
## Current WEGO order (see ORDER_* constants above).
var order: String = ORDER_HOLD

## Target tile for advance / charge / flank orders.
var target_pos: Vector2i = Vector2i.ZERO

## Target enemy formation_id for attack-focused orders.
var target_formation_id: String = ""

# ── Morale ────────────────────────────────────────────────────────────────────
var morale: float = 1.0


# ── Factory ───────────────────────────────────────────────────────────────────

## Create a formation containing a single combatant (Phase 4 1-man squads).
static func single(combatant_id: String, team: String, lbl: String, pos: Vector2i) -> FormationState:
	var f := FormationState.new()
	f.formation_id = combatant_id + "_formation"
	f.team_id      = team
	f.label        = lbl
	f.anchor_pos   = pos
	f.member_ids.append(combatant_id)
	return f


## Create a multi-member formation.
static func make(fid: String, team: String, lbl: String, members: Array, pos: Vector2i) -> FormationState:
	var f := FormationState.new()
	f.formation_id = fid
	f.team_id      = team
	f.label        = lbl
	f.anchor_pos   = pos
	f.member_ids.assign(members)
	return f


# ── Morale helpers ────────────────────────────────────────────────────────────

func on_member_killed() -> void:
	morale = clampf(morale - MORALE_DEATH_PENALTY, 0.0, 1.0)
	_check_rout()


func on_member_shocked() -> void:
	morale = clampf(morale - MORALE_SHOCK_PENALTY, 0.0, 1.0)
	_check_rout()


func _check_rout() -> void:
	if morale < MORALE_ROUT_THRESHOLD and order != ORDER_RETREAT:
		order = ORDER_RETREAT


## Returns true if all members are dead or incapacitated.
func is_destroyed(combatants: Dictionary) -> bool:
	for cid: String in member_ids:
		var c: CombatantState = combatants.get(cid)
		if c != null and not c.is_dead and not c.is_incapacitated:
			return false
	return true


## Returns count of members still active (not dead, not incapacitated).
func active_count(combatants: Dictionary) -> int:
	var n: int = 0
	for cid: String in member_ids:
		var c: CombatantState = combatants.get(cid)
		if c != null and not c.is_dead and not c.is_incapacitated:
			n += 1
	return n


# ── Serialisation ─────────────────────────────────────────────────────────────

func to_dict() -> Dictionary:
	return {
		"formation_id":        formation_id,
		"team_id":             team_id,
		"label":               label,
		"member_ids":          member_ids.duplicate(),
		"anchor_pos":          { "x": anchor_pos.x, "y": anchor_pos.y },
		"order":               order,
		"target_pos":          { "x": target_pos.x, "y": target_pos.y },
		"target_formation_id": target_formation_id,
		"morale":              morale,
	}


static func from_dict(d: Dictionary) -> FormationState:
	var f := FormationState.new()
	f.formation_id        = d.get("formation_id",        "")
	f.team_id             = d.get("team_id",             "")
	f.label               = d.get("label",               "")
	f.member_ids.assign(d.get("member_ids", []))
	var ap: Dictionary    = d.get("anchor_pos", {"x": 0, "y": 0})
	f.anchor_pos          = Vector2i(int(ap.get("x", 0)), int(ap.get("y", 0)))
	f.order               = d.get("order",               ORDER_HOLD)
	var tp: Dictionary    = d.get("target_pos", {"x": 0, "y": 0})
	f.target_pos          = Vector2i(int(tp.get("x", 0)), int(tp.get("y", 0)))
	f.target_formation_id = d.get("target_formation_id", "")
	f.morale              = float(d.get("morale",         1.0))
	return f
