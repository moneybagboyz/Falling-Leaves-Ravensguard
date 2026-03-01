## ReputationEvents — applies reputation deltas to PersonState.reputation dict.
##
## Reputation is keyed by faction_id or settlement_id → float (−1.0 to +1.0).
##
## Static API — call gain() or lose() from any system that wants to emit
## a reputation event. Changes are applied immediately (no EventQueue
## indirection; these are small delta floats, not structural mutations).
##
## Triggers that grant positive reputation:
##   - Defeating an enemy of a faction ("enemy_defeated")
##   - Completing a trade deal through a faction settlement ("trade_completed")
##   - Fulfilling a work contract ("contract_fulfilled")
##
## Triggers that reduce reputation:
##   - Crime detected in faction territory ("crime_detected")
##   - Killing a faction-aligned NPC ("npc_killed")
##   - Defaulting on follower wages ("wages_defaulted")
class_name ReputationEvents
extends RefCounted

const REP_MIN: float = -1.0
const REP_MAX: float =  1.0

## Per-event magnitude table.
const EVENT_DELTAS: Dictionary = {
	"enemy_defeated":    0.05,
	"trade_completed":   0.02,
	"contract_fulfilled":0.03,
	"crime_detected":   -0.08,
	"npc_killed":       -0.10,
	"wages_defaulted":  -0.06,
}


## Add a positive reputation event for person with one or more factions.
## event_type must be a key in EVENT_DELTAS.
## faction_ids is an Array[String] of faction_id or settlement_id keys.
static func gain(
		person: PersonState,
		event_type: String,
		faction_ids: Array) -> void:
	var delta: float = absf(float(EVENT_DELTAS.get(event_type, 0.0)))
	if delta == 0.0:
		push_warning("ReputationEvents.gain: unknown event_type '%s'" % event_type)
		return
	for fid: String in faction_ids:
		var current: float = float(person.reputation.get(fid, 0.0))
		person.reputation[fid] = clampf(current + delta, REP_MIN, REP_MAX)


## Add a negative reputation event.
static func lose(
		person: PersonState,
		event_type: String,
		faction_ids: Array) -> void:
	var delta: float = absf(float(EVENT_DELTAS.get(event_type, 0.0)))
	if delta == 0.0:
		push_warning("ReputationEvents.lose: unknown event_type '%s'" % event_type)
		return
	for fid: String in faction_ids:
		var current: float = float(person.reputation.get(fid, 0.0))
		person.reputation[fid] = clampf(current - delta, REP_MIN, REP_MAX)


## Returns person's reputation with faction_id, defaulting to 0.0.
static func get_rep(person: PersonState, faction_id: String) -> float:
	return float(person.reputation.get(faction_id, 0.0))


## Returns true if person meets the minimum reputation threshold for an action.
## Threshold defaults to 0.0 (neutral or better).
static func meets_threshold(
		person: PersonState,
		faction_id: String,
		threshold: float = 0.0) -> bool:
	return get_rep(person, faction_id) >= threshold
