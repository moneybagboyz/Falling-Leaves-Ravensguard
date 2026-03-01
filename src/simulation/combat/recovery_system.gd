## RecoverySystem — advances wound healing and stamina recovery on the strategic clock.
##
## Called once per strategic tick (each day-phase pass of the StrategicClock).
## It is stateless: all data lives on PersonState.
##
## Wound healing rules
## ───────────────────
## Each wound dict on body_state[zone_id] has a "severity" key and a "healing"
## float (0.0 → 1.0).  When healing reaches 1.0 the wound is removed.
##
## Healing rate per tick is modulated by:
##   shelter_quality  : "none" | "camp" | "inn" | "house" — base multiplier
##   hunger_penalty   : needs.hunger > 0.7  → ×0.5
##   thirst_penalty   : needs.thirst > 0.7  → ×0.5
##   pain_reduction   : healing progresses even with pain, but slower (÷1.5 per pain unit)
##
## Stamina recovery between battles (out-of-combat)
## ─────────────────────────────────────────────────
## stamina += 0.25 per tick (full recovery in 4 ticks / one day roughly).
## Capped at 1.0.
##
## Strategic fatigue (needs.fatigue) is NOT affected here; it is handled by NeedsSimulator.
class_name RecoverySystem
extends RefCounted


## Healing per tick by shelter quality before need-modifiers.
const SHELTER_RATE: Dictionary = {
	"none":  0.04,   # exposed — very slow
	"camp":  0.08,   # improvised camp
	"inn":   0.14,   # commercial lodging
	"house": 0.12,   # private dwelling (slightly slower than inn — no dedicated care)
}
const STAMINA_RECOVERY_PER_TICK: float = 0.25
const BLEEDING_KEY: String = "bleeding"


## Tick every PersonState in world_state forward by one recovery step.
## shelter_map: person_id → shelter_quality string (caller provides from settlement data).
## Falls back to "none" for any unmapped person.
static func tick_all(world_state: WorldState, shelter_map: Dictionary) -> void:
	for pid: String in world_state.characters:
		var person: PersonState = world_state.characters[pid]
		var shelter: String = shelter_map.get(pid, "none")
		tick_person(person, shelter)
	# npc_pool is transient; don't tick (they are re-generated on demand).


## Tick a single PersonState — suitable for unit-testing individual characters.
static func tick_person(person: PersonState, shelter_quality: String = "none") -> void:
	_heal_wounds(person, shelter_quality)
	_recover_stamina(person)


# ── Private helpers ──────────────────────────────────────────────────────────

static func _heal_wounds(person: PersonState, shelter_quality: String) -> void:
	var base_rate: float = SHELTER_RATE.get(shelter_quality, SHELTER_RATE["none"])

	# Need multipliers.
	var rate: float = base_rate
	if person.needs.get("hunger", 0.0) > 0.7:
		rate *= 0.5
	if person.needs.get("fatigue", 0.0) > 0.7:
		rate *= 0.75   # exhaustion slows recovery but not as severely as starvation

	# Reduce bleeding each tick (bandaging / clotting).
	# PersonState doesn't track active bleeding between battles — skip.

	# Advance healing on each wound.
	var zones_to_clear: Array[String] = []
	for zone_id: String in person.body_state:
		var wounds: Array = person.body_state[zone_id]
		var updated_wounds: Array = []
		for w: Dictionary in wounds:
			var wd: Dictionary = w.duplicate(true)
			var pain_div: float = 1.0 + wd.get("pain_contrib", 0.0) * 0.5
			var effective_rate: float = rate / pain_div
			# Graze heals faster; lethal wound requires surgery.
			match wd.get("severity", "wound"):
				"graze":
					effective_rate *= 2.0
				"wound":
					effective_rate *= 1.0
				"severe":
					effective_rate *= 0.5
				"lethal":
					effective_rate *= 0.1   # barely heals without medical attention
			wd["healing"] = minf(wd.get("healing", 0.0) + effective_rate, 1.0)
			if wd["healing"] < 1.0:
				updated_wounds.append(wd)
			# else: wound fully healed — drop it from array.
		if updated_wounds.is_empty():
			zones_to_clear.append(zone_id)
		else:
			person.body_state[zone_id] = updated_wounds
	for zone_id: String in zones_to_clear:
		person.body_state.erase(zone_id)


static func _recover_stamina(person: PersonState) -> void:
	person.stamina = minf(person.stamina + STAMINA_RECOVERY_PER_TICK, 1.0)
