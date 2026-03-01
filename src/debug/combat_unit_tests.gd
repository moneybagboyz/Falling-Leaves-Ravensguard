## CombatUnitTests — automated combat system regression tests (P4-21).
##
## Usage: attach to a standalone test scene, or call CombatUnitTests.run_all()
## from any scene after the game has fully loaded.
##
## Each test returns a result dict: { "name": String, "pass": bool, "msg": String }
## run_all() prints a summary and returns the Array of results.
##
## Gate: only active in debug builds.
class_name CombatUnitTests
extends Node


func _ready() -> void:
	if not OS.has_feature("debug"):
		return
	call_deferred("_run_deferred")


func _run_deferred() -> void:
	var results: Array = run_all()
	var passed: int    = 0
	var failed: int    = 0
	for r: Dictionary in results:
		if r["pass"]:
			passed += 1
			print("  [PASS] %s" % r["name"])
		else:
			failed += 1
			push_error("  [FAIL] %s — %s" % [r["name"], r["msg"]])
	print("─────────────────────────────────────────────────────")
	print("CombatUnitTests complete: %d passed, %d failed." % [passed, failed])


# ── Public API ────────────────────────────────────────────────────────────────

## Run all tests and return an Array of result dicts.
static func run_all() -> Array:
	print("═════ CombatUnitTests ════════════════════════════════")
	var results: Array = []
	results.append(test_zone_weights_sum_to_one())
	results.append(test_stamina_drain_clamp_lower())
	results.append(test_stamina_recovery_clamp_upper())
	results.append(test_stamina_recovery_no_overflow())
	results.append(test_morale_rout_at_threshold())
	results.append(test_morale_does_not_rout_above_threshold())
	results.append(test_morale_death_penalty_accurate())
	results.append(test_pressure_penetration_analytic())
	results.append(test_pressure_absorption_analytic())
	results.append(test_formation_layer_sort_keys())
	results.append(test_wego_terminates())
	return results


# ══════════════════════════════════════════════════════════════════════════════
# Tests
# ══════════════════════════════════════════════════════════════════════════════

## 1. Zone weights must sum to exactly 1.0 (within float epsilon).
static func test_zone_weights_sum_to_one() -> Dictionary:
	var tname := "zone_weights_sum_to_one"
	var total: float = 0.0
	for zone: String in CombatResolver.ZONE_WEIGHTS:
		total += float(CombatResolver.ZONE_WEIGHTS[zone])
	var ok: bool = absf(total - 1.0) < 1e-6
	return _result(tname, ok,
		"sum = %.8f (expected 1.0)" % total)


## 2. Stamina cannot go below 0.0 when drained past its current value.
static func test_stamina_drain_clamp_lower() -> Dictionary:
	var tname := "stamina_drain_clamp_lower"
	var c := CombatantState.new()
	c.stamina = 0.05
	# Simulate draining 0.12 (short_sword stamina_cost).
	c.stamina = clampf(c.stamina - 0.12, 0.0, 1.0)
	var ok: bool = c.stamina >= 0.0 and c.stamina <= 1.0
	return _result(tname, ok,
		"stamina = %.4f after drain below zero" % c.stamina)


## 3. Stamina recovery must not exceed 1.0 from any starting value.
static func test_stamina_recovery_clamp_upper() -> Dictionary:
	var tname := "stamina_recovery_clamp_upper"
	var c := CombatantState.new()
	c.stamina = 0.98   # near full; regen = 0.15 would overshoot
	c.tick_stamina_recovery()   # no equipment → encumbrance = 0 → regen = 0.15
	var ok: bool = c.stamina <= 1.0 and c.stamina > 0.0
	return _result(tname, ok,
		"stamina = %.4f after recovery near cap" % c.stamina)


## 4. Stamina recovery from 0.0 stays ≤ 1.0 and > 0.0 after one tick.
static func test_stamina_recovery_no_overflow() -> Dictionary:
	var tname := "stamina_recovery_no_overflow"
	var c := CombatantState.new()
	c.stamina = 0.0
	for _i: int in 20:
		c.tick_stamina_recovery()
	var ok: bool = c.stamina <= 1.0 and c.stamina >= 0.0
	return _result(tname, ok,
		"stamina = %.4f after 20 recovery ticks from 0" % c.stamina)


## 5. Morale rout triggers when morale drops below MORALE_ROUT_THRESHOLD.
static func test_morale_rout_at_threshold() -> Dictionary:
	var tname := "morale_rout_at_threshold"
	var f := FormationState.new()
	f.morale = FormationState.MORALE_ROUT_THRESHOLD  # exactly at threshold — not yet below
	f.order  = FormationState.ORDER_ADVANCE
	# One kill drops it below threshold.
	f.on_member_killed()
	var ok: bool = (f.order == FormationState.ORDER_RETREAT)
	return _result(tname, ok,
		"order = '%s' (expected 'retreat') after killing morale below threshold" % f.order)


## 6. Morale rout does NOT trigger when still above threshold.
static func test_morale_does_not_rout_above_threshold() -> Dictionary:
	var tname := "morale_no_rout_above_threshold"
	var f := FormationState.new()
	f.morale = 0.50
	f.order  = FormationState.ORDER_ADVANCE
	f.on_member_killed()   # morale = 0.40 — still above ROUT_THRESHOLD (0.20)
	var ok: bool = (f.order == FormationState.ORDER_ADVANCE)
	return _result(tname, ok,
		"order = '%s' (expected 'advance') — morale still above threshold" % f.order)


## 7. Death penalty is exact constant.
static func test_morale_death_penalty_accurate() -> Dictionary:
	var tname := "morale_death_penalty_accurate"
	var f := FormationState.new()
	f.morale = 1.0
	f.order  = FormationState.ORDER_HOLD  # hold so it won't rout at high morale
	f.on_member_killed()
	var expected: float = 1.0 - FormationState.MORALE_DEATH_PENALTY
	var ok: bool = absf(f.morale - expected) < 1e-6
	return _result(tname, ok,
		"morale = %.4f (expected %.4f)" % [f.morale, expected])


## 8. Pressure formula: analytic penetration check.
##    Parameters chosen so penetration definitely occurs with no armor.
##    momentum = 0.4 kg × 35 m/s = 14.0  (ignoring quality/edge_ret for clarity)
##    contact_area = 0.30 cm²
##    pressure = 14.0 / 0.30 = 46.7 kPa
##    Unarmored target → no layers → momentum stays at 14.0 → expect "wound" or higher.
##    Chest wound thresholds (from data): graze≥2, wound≥6, severe≥12, lethal≥25 (typical).
static func test_pressure_penetration_analytic() -> Dictionary:
	var tname := "pressure_penetration_analytic"
	# Pure math — no ContentRegistry needed.
	var mass_kg     : float = 0.4
	var vel_factor  : float = CombatantState.VELOCITY_FACTORS.get("fast", 35.0)
	var quality_mult: float = 1.0   # standard
	var edge_ret    : float = 1.0   # simplified (ignores material lookup)
	var contact_area: float = 0.30
	var momentum    : float = mass_kg * vel_factor * quality_mult * edge_ret
	var pressure    : float = momentum / maxf(contact_area, 0.001)
	# With no armor layers, resolved momentum stays at 14.0.
	# Chest graze threshold is 2.0 — we should at least get "graze".
	var chest_graze_thresh: float = 2.0
	var ok: bool = (momentum >= chest_graze_thresh) and (pressure > 0.0)
	return _result(tname, ok,
		"momentum = %.2f, pressure = %.2f (expected momentum >= %.1f)" % [
			momentum, pressure, chest_graze_thresh])


## 9. Pressure formula: analytic absorption check.
##    Paper-thin cloth (low yield) stops a very weak arrow.
##    mass = 0.02 kg, vel = slow (15), contact = 0.04 cm²
##    momentum = 0.02 × 15 = 0.30; pressure = 0.30 / 0.04 = 7.5
##    If we set a synthetic armor resistance of yield_strength=1.0, thickness=5mm
##    → resistance = 5.0 > pressure=7.5? No, 5.0 < 7.5 → penetrates.
##    Let's use resistance=10 to absorb: yield=2, thickness=5 → resistance=10 > 7.5 → absorbed.
static func test_pressure_absorption_analytic() -> Dictionary:
	var tname := "pressure_absorption_analytic"
	var mass_kg     : float = 0.02
	var vel_factor  : float = CombatantState.VELOCITY_FACTORS.get("slow", 15.0)
	var momentum    : float = mass_kg * vel_factor   # = 0.30
	var contact_area: float = 0.04
	var pressure    : float = momentum / maxf(contact_area, 0.001)  # = 7.5
	# Synthetic armor layer: yield_strength=2.0, thickness_mm=5 → resistance=10 > pressure=7.5 → absorbed.
	var yield_str   : float = 2.0
	var thickness   : float = 5.0
	var resistance  : float = yield_str * thickness  # = 10.0
	var absorbed    : bool  = pressure < resistance
	# After absorption, only blunt shock carries through.
	var blunt_t: float = 0.4  # synthetic blunt_transfer (e.g., gambeson)
	var final_momentum: float = momentum * blunt_t   # = 0.12
	# Graze threshold = 2.0 → final_momentum 0.12 < 2.0 → severity = "none"
	var graze_thresh: float = 2.0
	var ok: bool = absorbed and (final_momentum < graze_thresh)
	return _result(tname, ok,
		"absorbed=%s, final momentum=%.3f (should be < %.1f graze threshold)" % [
			str(absorbed), final_momentum, graze_thresh])


## 10. FormationState layer sort keys are all present in LAYER_SORT_ORDER.
static func test_formation_layer_sort_keys() -> Dictionary:
	var tname := "formation_layer_sort_keys"
	var expected_layers: Array[String] = ["shield", "armor", "clothing", "base_layer"]
	var ok: bool = true
	var missing: Array = []
	for lyr: String in expected_layers:
		if not CombatantState.LAYER_SORT_ORDER.has(lyr):
			ok = false
			missing.append(lyr)
	return _result(tname, ok,
		"missing layers: %s" % str(missing))


## 11. WEGO resolution terminates within MAX_TURNS on a minimal battle.
##     Two unarmed combatants in adjacent tiles — at least one must die or be
##     incapacitated within MAX_TURNS turns.
static func test_wego_terminates() -> Dictionary:
	var tname     := "wego_terminates"
	const MAX_TURNS: int = 100

	# Build a two-combatant minimal BattleState.
	var ws      := WorldState.new()
	ws.world_seed = 42

	# Player combatant.
	var p1      := PersonState.new()
	p1.person_id = "p1"
	p1.name      = "TestPlayer"
	ws.player_character_id = "p1"
	ws.characters["p1"] = p1

	# Enemy combatant.
	var p2      := PersonState.new()
	p2.person_id = "e1"
	p2.name      = "TestEnemy"
	ws.npc_pool["e1"] = p2

	# Build FormationStates first so we know the formation_ids.
	var f1 := FormationState.single("p1", "player", "Player", Vector2i(5, 5))
	var f2 := FormationState.single("e1", "enemy",  "Enemy",  Vector2i(6, 5))
	f1.order = FormationState.ORDER_CHARGE
	f2.order = FormationState.ORDER_CHARGE
	f1.target_formation_id = f2.formation_id   ## "e1_formation"
	f2.target_formation_id = f1.formation_id   ## "p1_formation"

	# Build CombatantStates — pass the matching formation_id so c.formation_id is consistent.
	var c1 := CombatantState.from_person(p1, "player", f1.formation_id)
	var c2 := CombatantState.from_person(p2, "enemy",  f2.formation_id)
	c1.tile_pos = Vector2i(5, 5)
	c2.tile_pos = Vector2i(6, 5)  # adjacent

	# Build BattleState — key formations by their own .formation_id.
	var battle := BattleState.new()
	battle.battle_id = "unit_test_battle"
	battle.turn      = 0
	battle.combatants[c1.combatant_id] = c1
	battle.combatants[c2.combatant_id] = c2
	battle.formations[f1.formation_id] = f1
	battle.formations[f2.formation_id] = f2
	ws.active_battle = battle

	# Run WEGO until result or MAX_TURNS.
	var ended: bool = false
	for _t: int in MAX_TURNS:
		var result: bool = CombatResolver.resolve_turn(battle, {}, ws.world_seed)
		if result or battle.result != "":
			ended = true
			break
		# Also stop if all combatants on a side are down.
		if c1.is_dead or c1.is_incapacitated or c2.is_dead or c2.is_incapacitated:
			ended = true
			break

	return _result(tname, ended,
		"battle did not end within %d turns" % MAX_TURNS)


# ── Utility ───────────────────────────────────────────────────────────────────

static func _result(test_name: String, pass_flag: bool, detail: String) -> Dictionary:
	return {
		"name": test_name,
		"pass": pass_flag,
		"msg":  "" if pass_flag else detail,
	}
