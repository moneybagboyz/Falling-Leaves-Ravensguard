## NpcScheduleSystem — daily schedule state transitions for pooled NPCs (P3-13).
##
## Registered on MOVEMENT phase (every tick).
##
## Uses tick % TICKS_PER_DAY to determine the current time-of-day,
## then assigns schedule_state to all NPCs in world_state.npc_pool:
##   - "working"   — daytime, work hours
##   - "wandering" — midday break (short wander window)
##   - "resting"   — night hours
##   - "idle"      — pre-dawn window before work starts
##
## NPCs with active_role = "" are set to "wandering" during work hours
## (no job to go to) or "resting" at night.
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

	for pid: String in _world_state.npc_pool:
		var npc: PersonState = _world_state.npc_pool[pid]
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
