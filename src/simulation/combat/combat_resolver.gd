## CombatResolver — core WEGO resolution engine.
##
## Executes one full WEGO turn for a BattleState:
##   1. All formations execute their orders (move step, then attack step).
##   2. Attack resolution: hit location, armor mitigation, wound severity.
##   3. Wound effects applied; bleed ticked; stamina recovered.
##   4. Morale updated; destroyed formations flagged.
##   5. Battle end condition evaluated.
##
## Terrain effects (P4-17) read from the sub-region grid passed as map_data.
## Formation effects: adjacent allies in the same team grant +10% hit chance.
##
## All randomness seeded from battle_id and turn number for determinism.
class_name CombatResolver
extends RefCounted

# ── Accuracy constants ────────────────────────────────────────────────────────
## Base hit chance before stamina, skill, and terrain modifiers.
const BASE_HIT_CHANCE: float = 0.65
## Per melee skill level bonus.
const SKILL_HIT_BONUS:  float = 0.02
## Formation cohesion bonus (adjacent ally present).
const COHESION_BONUS:   float = 0.10
## Stamina < 0.3 penalty.
const LOW_STAMINA_PENALTY: float = 0.20
## Elevation advantage bonus.
const ELEVATION_BONUS:  float = 0.15

# ── Reach tile distances by class ─────────────────────────────────────────────
const REACH_DISTANCE: Dictionary = {
	"unarmed": 1,
	"short":   1,
	"medium":  1,
	"long":    2,
	"polearm": 2,
}

# ── Bleed/pain per severity (base before zone multipliers) ────────────────────
const SEVERITY_STATS: Dictionary = {
	"none":   { "bleed": 0.00, "pain": 0.00 },
	"graze":  { "bleed": 0.02, "pain": 0.05 },
	"wound":  { "bleed": 0.08, "pain": 0.20 },
	"severe": { "bleed": 0.18, "pain": 0.40 },
	"lethal": { "bleed": 0.30, "pain": 0.80 },
}


## Execute one complete WEGO turn. Modifies battle in-place.
## Returns true if the battle has ended after this turn.
static func resolve_turn(battle: BattleState, map_data: Dictionary, _world_seed: int) -> bool:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash(battle.battle_id) ^ battle.turn

	battle.phase = BattleState.PHASE_RESOLVING

	# Clear previous turn's resolved actions.
	for cid: String in battle.combatants:
		battle.combatants[cid].resolved_actions = []

	# ── Step 1: Propagate formation orders to individual combatants ──────
	for fid: String in battle.formations:
		var f: FormationState = battle.formations[fid]
		for cid: String in f.member_ids:
			var c: CombatantState = battle.get_combatant(cid)
			if c == null or c.is_dead or c.is_incapacitated:
				continue
			c.current_order = f.order

	# ── Step 2: Move all combatants one step toward their order target ────
	for fid: String in battle.formations:
		var f: FormationState = battle.formations[fid]
		if f.order == FormationState.ORDER_RETREAT:
			_move_retreat(f, battle, map_data)
		elif f.order in [FormationState.ORDER_ADVANCE, FormationState.ORDER_CHARGE, FormationState.ORDER_FLANK]:
			_move_advance(f, battle, map_data)
		# ORDER_HOLD: no movement

	# ── Step 3: Attack step — each active combatant attacks if in reach ──
	# Build a snapshot of positions before attack loop to avoid order-dependence.
	var pos_snap: Dictionary = {}
	for cid: String in battle.combatants:
		var c: CombatantState = battle.combatants[cid]
		pos_snap[cid] = c.tile_pos

	for fid: String in battle.formations:
		var f: FormationState = battle.formations[fid]
		if f.order == FormationState.ORDER_RETREAT or f.order == FormationState.ORDER_HOLD:
			# Hold still attacks adjacent enemies.
			pass  # fall through to attack check
		for cid: String in f.member_ids:
			var attacker: CombatantState = battle.get_combatant(cid)
			if attacker == null or attacker.is_dead or attacker.is_incapacitated:
				continue
			var target: CombatantState = _find_target(attacker, f, battle, pos_snap)
			if target == null:
				continue
			_do_attack(attacker, target, f, battle, pos_snap, map_data, rng)

	# ── Step 4: End-of-turn ticks ─────────────────────────────────────────
	for cid: String in battle.combatants:
		var c: CombatantState = battle.combatants[cid]
		if c.is_dead:
			continue
		c.tick_bleed()
		c.tick_stamina_recovery()

	# ── Step 5: Update formation morale ───────────────────────────────────
	for fid: String in battle.formations:
		var f: FormationState = battle.formations[fid]
		# Recount active members; mop up destroyed formations.
		if f.is_destroyed(battle.combatants):
			f.order = FormationState.ORDER_RETREAT

	# ── Step 6: Evaluate battle result ────────────────────────────────────
	battle.turn += 1
	var result: String = battle.evaluate_result()
	if result != "":
		battle.result = result
		battle.phase  = BattleState.PHASE_RESULTS
		return true

	battle.phase = BattleState.PHASE_PLANNING
	return false


# ── Movement helpers ──────────────────────────────────────────────────────────

static func _move_advance(f: FormationState, battle: BattleState, _map_data: Dictionary) -> void:
	# Find nearest enemy formation and step each active member 1 tile toward it.
	var nearest_enemy_pos: Vector2i = _nearest_enemy_anchor(f, battle)
	for cid: String in f.member_ids:
		var c: CombatantState = battle.get_combatant(cid)
		if c == null or c.is_dead or c.is_incapacitated:
			continue
		c.tile_pos = _step_toward(c.tile_pos, nearest_enemy_pos)


static func _move_retreat(f: FormationState, battle: BattleState, _map_data: Dictionary) -> void:
	var nearest_enemy_pos: Vector2i = _nearest_enemy_anchor(f, battle)
	for cid: String in f.member_ids:
		var c: CombatantState = battle.get_combatant(cid)
		if c == null or c.is_dead or c.is_incapacitated:
			continue
		# Step away from nearest enemy.
		var away: Vector2i = c.tile_pos + (c.tile_pos - nearest_enemy_pos).sign()
		c.tile_pos = away


static func _nearest_enemy_anchor(f: FormationState, battle: BattleState) -> Vector2i:
	var best_dist: int = 999999
	var best_pos: Vector2i = Vector2i(125, 125)
	for fid: String in battle.formations:
		var ef: FormationState = battle.formations[fid]
		if ef.team_id == f.team_id:
			continue
		if ef.is_destroyed(battle.combatants):
			continue
		var d: int = (ef.anchor_pos - f.anchor_pos).length_squared()
		if d < best_dist:
			best_dist = d
			best_pos  = ef.anchor_pos
	return best_pos


static func _step_toward(from: Vector2i, to: Vector2i) -> Vector2i:
	var diff: Vector2i = to - from
	if diff == Vector2i.ZERO:
		return from
	return from + diff.sign()


# ── Target selection ──────────────────────────────────────────────────────────

static func _find_target(
		attacker: CombatantState,
		_f: FormationState,
		battle: BattleState,
		pos_snap: Dictionary) -> CombatantState:
	var weapon_data: Dictionary = attacker.get_weapon_data()
	var reach: int = REACH_DISTANCE.get(weapon_data.get("reach_class", "medium"), 1)

	var best_dist: int = 999999
	var best: CombatantState = null
	for cid: String in battle.combatants:
		var target: CombatantState = battle.combatants[cid]
		if target.team_id == attacker.team_id:
			continue
		if target.is_dead or target.is_incapacitated:
			continue
		var t_pos: Vector2i = pos_snap.get(cid, target.tile_pos)
		var a_pos: Vector2i = pos_snap.get(attacker.combatant_id, attacker.tile_pos)
		var chebyshev: int  = maxi(absi(t_pos.x - a_pos.x), absi(t_pos.y - a_pos.y))
		if chebyshev > reach:
			continue
		if chebyshev < best_dist:
			best_dist = chebyshev
			best      = target
	return best


# ── Attack resolution ─────────────────────────────────────────────────────────

static func _do_attack(
		attacker:  CombatantState,
		target:    CombatantState,
		f:         FormationState,
		battle:    BattleState,
		pos_snap:  Dictionary,
		map_data:  Dictionary,
		rng:       RandomNumberGenerator) -> void:

	var weapon: Dictionary = attacker.get_weapon_data()

	# Drain stamina.
	attacker.stamina = clampf(attacker.stamina - float(weapon.get("stamina_cost", 0.12)), 0.0, 1.0)

	# ── Hit chance ────────────────────────────────────────────────────────
	var hit_chance: float = BASE_HIT_CHANCE
	hit_chance += attacker.melee_skill * SKILL_HIT_BONUS

	# Stamina penalty.
	if attacker.stamina < 0.3:
		hit_chance -= LOW_STAMINA_PENALTY
	if attacker.stamina <= 0.0:
		hit_chance = 0.0

	# Formation cohesion: any adjacent ally confers bonus.
	if _has_adjacent_ally(attacker, f, battle, pos_snap):
		hit_chance += COHESION_BONUS

	# Elevation advantage.
	if attacker.z_level > target.z_level:
		hit_chance += ELEVATION_BONUS
	elif attacker.z_level < target.z_level:
		hit_chance -= ELEVATION_BONUS

	# Attack speed: multiple attacks per turn at speed > 1.
	var attacks: int = maxi(1, roundi(float(weapon.get("attack_speed", 1.0))))

	for _a: int in attacks:
		if rng.randf() > clampf(hit_chance, 0.0, 0.95):
			# Miss.
			attacker.resolved_actions.append({
				"type": "miss", "target_id": target.combatant_id,
				"hit_zone": "", "severity": "none",
			})
			continue

		# ── Hit location ─────────────────────────────────────────────
		var zone_id: String = _roll_hit_zone(rng)

		# ── Momentum and damage type ──────────────────────────────────
		var damage_type: String  = weapon.get("damage_type", "slash")
		var momentum: float      = float(weapon.get("swing_momentum", 6))

		# ── Armor mitigation ──────────────────────────────────────────
		var armor: Dictionary = target.get_armor_for_zone(zone_id, damage_type)
		var coverage: float   = float(armor.get("coverage",  0.0))
		var reduction: float  = float(armor.get("reduction", 0.0))

		# If the hit lands on covered zone, roll whether armor blocks.
		if coverage > 0.0 and rng.randf() < coverage:
			momentum = maxf(0.0, momentum - reduction)

		# ── Wound severity ────────────────────────────────────────────
		var zone_def: Dictionary = ContentRegistry.get_content("body_zone", zone_id)
		var thresholds: Dictionary = zone_def.get("wound_thresholds",
			{"graze": 2, "wound": 6, "severe": 12, "lethal": 99})

		var severity: String = "none"
		if   momentum >= float(thresholds.get("lethal", 99)):  severity = "lethal"
		elif momentum >= float(thresholds.get("severe", 12)):  severity = "severe"
		elif momentum >= float(thresholds.get("wound",   6)):  severity = "wound"
		elif momentum >= float(thresholds.get("graze",   2)):  severity = "graze"

		if severity == "none":
			attacker.resolved_actions.append({
				"type": "hit_absorbed", "target_id": target.combatant_id,
				"hit_zone": zone_id, "severity": "none",
			})
			continue

		# ── Apply wound ───────────────────────────────────────────────
		var sev_stats: Dictionary  = SEVERITY_STATS.get(severity, {"bleed": 0.0, "pain": 0.0})
		var pain_mult: float       = float(zone_def.get("pain_multiplier", 1.0))
		var effects: Array         = _effects_for(zone_def, severity)

		target.apply_wound(zone_id, severity,
			float(sev_stats["bleed"]),
			float(sev_stats["pain"]) * pain_mult,
			effects)

		attacker.resolved_actions.append({
			"type":      "hit",
			"target_id": target.combatant_id,
			"hit_zone":  zone_id,
			"severity":  severity,
		})

		# Update formation morale.
		var target_formation: FormationState = _formation_of(target.combatant_id, battle)
		if target_formation != null:
			if target.is_dead:
				target_formation.on_member_killed()
			elif target.is_incapacitated:
				target_formation.on_member_shocked()

		# Update target formation anchor pos.
		if target_formation != null:
			target_formation.anchor_pos = target.tile_pos


# ── Hit zone roll ─────────────────────────────────────────────────────────────

## Weighted random zone selection. Weights must match body_zone data.
const ZONE_WEIGHTS: Dictionary = {
	"head":      0.10,
	"neck":      0.05,
	"chest":     0.25,
	"abdomen":   0.15,
	"left_arm":  0.10,
	"right_arm": 0.10,
	"left_leg":  0.125,
	"right_leg": 0.125,
}

static func _roll_hit_zone(rng: RandomNumberGenerator) -> String:
	var r: float = rng.randf()
	var cumulative: float = 0.0
	for zone: String in ZONE_WEIGHTS:
		cumulative += float(ZONE_WEIGHTS[zone])
		if r <= cumulative:
			return zone
	return "chest"  # fallback


# ── Effect extraction ─────────────────────────────────────────────────────────

static func _effects_for(zone_def: Dictionary, severity: String) -> Array:
	var out: Array = []
	for entry: Dictionary in zone_def.get("critical_effects", []):
		if entry.get("at_severity", "") == severity:
			out.append(entry.get("effect", ""))
	return out


# ── Formation adjacency check ─────────────────────────────────────────────────

static func _has_adjacent_ally(
		attacker: CombatantState,
		f: FormationState,
		battle: BattleState,
		pos_snap: Dictionary) -> bool:
	var a_pos: Vector2i = pos_snap.get(attacker.combatant_id, attacker.tile_pos)
	for cid: String in f.member_ids:
		if cid == attacker.combatant_id:
			continue
		var ally: CombatantState = battle.get_combatant(cid)
		if ally == null or ally.is_dead or ally.is_incapacitated:
			continue
		var ally_pos: Vector2i = pos_snap.get(cid, ally.tile_pos)
		if maxi(absi(ally_pos.x - a_pos.x), absi(ally_pos.y - a_pos.y)) <= 1:
			return true
	return false


static func _formation_of(combatant_id: String, battle: BattleState) -> FormationState:
	for fid: String in battle.formations:
		var f: FormationState = battle.formations[fid]
		if combatant_id in f.member_ids:
			return f
	return null
