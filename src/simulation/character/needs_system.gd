## NeedsSystem — hunger and fatigue decay for the player character (P3-11).
##
## Registered on MOVEMENT phase (every tick, not just strategic).
##
## Hunger rises continuously. Fatigue rises while working, falls while resting.
## Having adequate shelter accelerates fatigue recovery.
## Hunger above 70% applies a penalty to work productivity (handled by WorkSystem).
##
## Full simulation (temperature, illness, food items) deferred to Phase 4–5.
class_name NeedsSystem
extends RefCounted

## Per-tick hunger increase. 1.0 in ~1250 ticks ≈ a full "game day" of hunger.
const HUNGER_RATE:       float = 0.0008
## Per-tick fatigue increase while working. Same scale as hunger.
const FATIGUE_DECAY:     float = 0.0006
## Per-tick fatigue while resting (negative = recovery).
const FATIGUE_REST:      float = -0.0006
## Extra recovery per tick when resting in shelter.
const SHELTER_BONUS:     float = -0.0020
## Coin cost deducted from player once per game day (TICKS_PER_DAY ticks)
## for rented accommodation. Eviction occurs when coin < daily rent.
const INN_RENT_PER_DAY:  float = 2.0
## How many ticks per in-game day (must stay in sync with NpcScheduleSystem).
const TICKS_PER_DAY:     int   = 24

var _world_state: WorldState = null


func setup(ws: WorldState) -> void:
	_world_state = ws


# ---------------------------------------------------------------------------
# Tick hook — MOVEMENT (every tick)
# ---------------------------------------------------------------------------

func tick_needs(tick: int) -> void:
	if _world_state == null:
		return
	var pid: String = _world_state.player_character_id
	if pid == "":
		return
	var player: PersonState = _world_state.characters.get(pid)
	if player == null:
		return

	# ── Hunger ────────────────────────────────────────────────────────────
	player.needs["hunger"] = clampf(
		player.needs.get("hunger", 0.0) + HUNGER_RATE, 0.0, 1.0
	)

	# ── Fatigue ───────────────────────────────────────────────────────────
	var is_working: bool  = player.active_role != ""
	var has_shelter: bool = player.shelter_status != ""
	var fatigue_delta: float
	if is_working:
		fatigue_delta = FATIGUE_DECAY
	else:
		fatigue_delta = FATIGUE_REST
		if has_shelter:
			fatigue_delta += SHELTER_BONUS  # Both are negative, so accelerates recovery.
	player.needs["fatigue"] = clampf(
		player.needs.get("fatigue", 0.0) + fatigue_delta, 0.0, 1.0
	)

	# ── Inn rent deduction (once per day) ─────────────────────────────────
	if player.shelter_status == "rented" and (tick % TICKS_PER_DAY == 0):
		if player.coin >= INN_RENT_PER_DAY:
			player.coin -= INN_RENT_PER_DAY
		else:
			# Can't afford rent — evicted.
			player.shelter_status = ""
			player.coin = maxf(player.coin, 0.0)
