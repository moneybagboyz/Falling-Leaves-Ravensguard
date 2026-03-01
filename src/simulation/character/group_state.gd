## GroupState — tracks the player's armed group of followers.
##
## Wraps follower_ids from PersonState with tactical fields:
##   pay_ledger:  coin owed per strategic tick across all followers.
##   morale:      0.0–1.0; decays when wages/food are unpaid; recovers when met.
##
## At morale < 0.2, followers desert one-per-tick until paid.
##
## Stored in WorldState.player_group as a plain Dictionary for serialisation.
## Use GroupState.from_dict() / to_dict() at the boundary.
class_name GroupState
extends RefCounted

## morale below this threshold causes desertion.
const DESERTION_THRESHOLD: float = 0.20
## morale recovers this much per tick when wages and food are paid.
const MORALE_RECOVER_RATE:  float = 0.03
## morale decays this much per tick when wages are missed.
const MORALE_DECAY_WAGES:   float = 0.06
## morale decays this much per tick when food is short.
const MORALE_DECAY_FOOD:    float = 0.04

var group_id: String = ""

## Mirror of PersonState.follower_ids. Must be kept in sync.
var member_ids: Array[String] = []

## Coin owed to ALL members combined per strategic tick.
var pay_per_tick: float = 0.0

## 0.0–1.0 group morale.
var morale: float = 1.0

## Tick on which wages were last successfully paid.
var last_paid_tick: int = 0

## Tick on which food was last adequate (set by FollowerSystem).
var last_fed_tick: int = 0


func to_dict() -> Dictionary:
	return {
		"group_id":      group_id,
		"member_ids":    member_ids.duplicate(),
		"pay_per_tick":  pay_per_tick,
		"morale":        morale,
		"last_paid_tick": last_paid_tick,
		"last_fed_tick": last_fed_tick,
	}


static func from_dict(d: Dictionary) -> GroupState:
	var g := GroupState.new()
	g.group_id       = d.get("group_id",       "")
	g.member_ids.assign(d.get("member_ids",    []))
	g.pay_per_tick   = float(d.get("pay_per_tick",  0.0))
	g.morale         = float(d.get("morale",        1.0))
	g.last_paid_tick = int(d.get("last_paid_tick",  0))
	g.last_fed_tick  = int(d.get("last_fed_tick",   0))
	return g


## Recalculate pay_per_tick from WS characters (1 coin/day per follower level).
func recalculate_pay(ws: WorldState) -> void:
	pay_per_tick = 0.0
	for pid: String in member_ids:
		var p: PersonState = ws.characters.get(pid)
		if p == null:
			continue
		# Base wage: 1 coin + (best combat + smithing skill avg) * 0.1.
		var melee: int    = maxi(p.skill_level("sword_fighting"),
				maxi(p.skill_level("spear_fighting"), p.skill_level("axe_fighting")))
		var craft: int    = p.skill_level("smithing")
		pay_per_tick += 1.0 + (melee + craft) * 0.1


## Returns a summary string for OwnershipView UI.
func summary_string(ws: WorldState) -> String:
	var roles: Dictionary = {}
	for pid: String in member_ids:
		var p: PersonState = ws.characters.get(pid)
		if p == null:
			continue
		var r: String = p.active_role if p.active_role != "" else "idle"
		roles[r] = roles.get(r, 0) + 1
	var parts: Array = []
	for r: String in roles:
		parts.append("%s×%d" % [r, roles[r]])
	return ", ".join(parts) if not parts.is_empty() else "empty"
