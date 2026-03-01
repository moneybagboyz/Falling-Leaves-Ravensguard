## NpcScheduleSystem — daily schedule state transitions for all NPC characters.
##
## Registered on MOVEMENT phase (every tick).
##
## CDDA-style reality bubble: only NPCs on the same world tile as the player
## receive AI ticks. All others are frozen (no cost, no drift).
##
## Uses tick % TICKS_PER_DAY to determine time-of-day and sets schedule_state:
##   - "working"   — daytime, work hours
##   - "wandering" — midday break (short wander window)
##   - "resting"   — night hours
##   - "idle"      — pre-dawn window before work starts
class_name NpcScheduleSystem
extends RefCounted

## Must stay in sync with NeedsSystem.TICKS_PER_DAY.
const TICKS_PER_DAY: int = 24

## Tick-of-day boundaries (inclusive lower bound):
##   0–1    → idle (pre-dawn)
##   2–9    → working
##   10     → wandering (midday break)
##   11–15  → working (afternoon)
##   16–23  → resting (night)
const WORK_START:    int = 2
const WANDER_TICK:   int = 10
const WORK_RESUME:   int = 11
const REST_START:    int = 16

var _world_state: WorldState = null


func setup(ws: WorldState) -> void:
	_world_state = ws


# ---------------------------------------------------------------------------
# Tick hook — MOVEMENT (every tick)
# ---------------------------------------------------------------------------

func tick_schedules(tick: int) -> void:
	if _world_state == null:
		return
	var tod: int = tick % TICKS_PER_DAY
	var default_state: String = _tod_to_schedule(tod)

	# Reality bubble: only tick NPCs on the player's current world tile.
	var player_wt_x: int = _world_state.player_location.get("wt_x", -999)
	var player_wt_y: int = _world_state.player_location.get("wt_y", -999)

	for pid: String in _world_state.characters:
		if pid == _world_state.player_character_id:
			continue  # skip the player
		var npc: PersonState = _world_state.characters[pid]
		if npc.home_settlement_id == "":
			continue  # skip non-NPC characters (e.g. bandits mid-battle)
		# Freeze NPCs outside the reality bubble.
		var loc_wt_x: int = npc.location.get("wt_x", -999)
		var loc_wt_y: int = npc.location.get("wt_y", -999)
		if loc_wt_x != player_wt_x or loc_wt_y != player_wt_y:
			continue
		if npc.active_role == "":
			# Unemployed — wander during day, rest at night.
			npc.schedule_state = "wandering" if tod < REST_START else "resting"
		else:
			npc.schedule_state = default_state


func _tod_to_schedule(tod: int) -> String:
	if tod >= REST_START:
		return "resting"
	if tod == WANDER_TICK:
		return "wandering"
	if tod >= WORK_START:
		return "working"
	return "idle"
