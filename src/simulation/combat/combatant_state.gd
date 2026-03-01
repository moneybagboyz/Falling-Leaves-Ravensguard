## CombatantState — per-soldier data record for active combat.
##
## One instance per individual soldier/character on a battle map.
## Lives inside a FormationState.member_ids list.
## Created from a PersonState via CombatantState.from_person() at combat entry.
## Wounds written back to PersonState.body_state at combat exit (P4-19).
##
## Body zones are loaded from data/body_zones/*.json via ContentRegistry.
## Wounds are stored as an array of wound dicts per zone_id.
class_name CombatantState
extends RefCounted

# ── Quality multiplier tables ─────────────────────────────────────────────────
## DR multiplier per armor quality tier.
const QUALITY_DR_MULTIPLIER: Dictionary = {
	"poor":       0.75,
	"standard":   1.00,
	"fine":        1.25,
	"masterwork": 1.50,
}

## Momentum multiplier per weapon quality tier.
const QUALITY_MOMENTUM_MULTIPLIER: Dictionary = {
	"poor":       0.80,
	"standard":   1.00,
	"fine":        1.20,
	"masterwork": 1.50,
}

## Layer resolution order (outermost = lowest value = resolved first).
const LAYER_SORT_ORDER: Dictionary = {
	"shield":     -1,
	"armor":       0,
	"clothing":    1,
	"base_layer":  2,
}

## Velocity factors: metres-per-second proxy, multiplied by striking_mass_kg to get momentum.
## Values calibrated so that a sword (0.4 kg, fast) gives ~14 momentum vs bare skin → wound.
const VELOCITY_FACTORS: Dictionary = {
	"slow":      15.0,
	"medium":    25.0,
	"fast":      35.0,
	"very_fast": 50.0,
}

# ── Identity ──────────────────────────────────────────────────────────────────
## Unique ID for this combatant within the battle. Matches the source PersonState.person_id.
var combatant_id: String = ""

## Which formation this combatant belongs to.
var formation_id: String = ""

## Which side: "player" or "enemy" (or faction_id for multi-faction battles).
var team_id: String = ""

## Display name (copied from PersonState.name).
var display_name: String = ""

## Body plan ID from PersonState. Determines zone set and organ roster.
var body_plan_id: String = "human"

# ── Position ──────────────────────────────────────────────────────────────────
## Current tile position on the battle map.
var tile_pos: Vector2i = Vector2i.ZERO

## Z-level (0 = ground, 1 = elevated, -1 = below).
var z_level: int = 0

# ── Combat stats ─────────────────────────────────────────────────────────────
## Stamina: 0.0 (exhausted) – 1.0 (fresh). Drained per attack; recovers per turn.
var stamina: float = 1.0

## Pain accumulated from all wounds. High pain reduces accuracy and speed.
## At pain >= 1.0 the combatant is incapacitated.
var pain: float = 0.0

## Current bleed rate per WEGO turn (sum of all active wounds).
var bleeding: float = 0.0

## Shock = pain + accumulated blood loss. Incapacitation threshold: 1.0.
var shock: float = 0.0

## Whether this combatant is currently incapacitated (shock >= 1.0 or knocked out).
var is_incapacitated: bool = false

## Whether this combatant is dead.
var is_dead: bool = false

# ── Wounds ────────────────────────────────────────────────────────────────────
## zone_id (String) → Array of wound dicts.
## Each wound dict: { "severity": String, "bleed": float, "pain": float, "effects": Array,
##                    "tissues_reached": Array[String], "bone_fractured": bool, "organ_damaged": String }
## Severity levels: "graze" | "wound" | "severe" | "lethal"
var body_zones: Dictionary = {}

## zone_id → true when the bone in that zone has been fractured.
## Fractures persist after battle and affect mobility permanently until healed.
var bone_fractures: Dictionary = {}

## organ_id → "damaged" | "destroyed" for each damaged organ.
## Damaged organs add bleed and pain each tick; destroyed vital organs mean death.
var organ_damage: Dictionary = {}

# ── Equipment ─────────────────────────────────────────────────────────────────
## Slot → item_id. Copied from PersonState.equipment_refs at combat entry.
## e.g. { "main_hand": "short_sword", "torso": "mail_hauberk", "head": "helm" }
var equipment_refs: Dictionary = {}

# ── Orders ────────────────────────────────────────────────────────────────────
## The order this combatant is currently executing (derived from their formation's order).
## e.g. "advance" | "attack:combatant_id" | "hold" | "retreat"
var current_order: String = ""

## Actions resolved this WEGO turn. Array of { "type", "target_id", "hit_zone", "severity" } dicts.
var resolved_actions: Array = []

# ── Skill cache ───────────────────────────────────────────────────────────────
## Melee skill level cached from PersonState at combat entry.
var melee_skill: int = 0

## Cached agility attribute value (affects dodge/speed).
var agility: int = 5


# ── Factory ───────────────────────────────────────────────────────────────────

## Create a CombatantState from a PersonState. Call at combat entry.
static func from_person(p: PersonState, team: String, formation: String) -> CombatantState:
	var c := CombatantState.new()
	c.combatant_id   = p.person_id
	c.formation_id   = formation
	c.team_id        = team
	c.display_name   = p.name
	c.body_plan_id   = p.body_plan_id
	c.stamina        = p.stamina
	c.equipment_refs = p.equipment_refs.duplicate()
	# Derive melee_skill from the highest combat-relevant skill the person has.
	c.melee_skill    = maxi(p.skill_level("sword_fighting"),
				maxi(p.skill_level("spear_fighting"),
				maxi(p.skill_level("axe_fighting"),
				     p.skill_level("club_fighting"))))
	c.agility        = p.effective_attribute("agility")
	# Initialise body zones from body plan data. Fall back to hardcoded human list.
	c.body_zones = {}
	var plan_def: Dictionary = ContentRegistry.get_content("body_plan", c.body_plan_id)
	var zone_list: Array = []
	if not plan_def.is_empty():
		for entry: Dictionary in plan_def.get("zones", []):
			zone_list.append(entry.get("zone_id", ""))
	if zone_list.is_empty():
		zone_list = ["head", "neck", "chest", "abdomen",
					 "left_arm", "right_arm", "left_leg", "right_leg",
					 "left_hand", "right_hand", "left_foot", "right_foot"]
	for zone_id: String in zone_list:
		if zone_id != "":
			c.body_zones[zone_id] = []
	return c


# ── Wound helpers ─────────────────────────────────────────────────────────────

## Apply a wound to a body zone. Updates pain, bleeding, shock, and death/incap flags.
## bone_frac: whether the bone in this zone was fractured by this attack.
## organ_hit:  organ_id that was physically penetrated, or "" if none.
## tissues:    list of tissue types reached (e.g. ["skin", "fat", "muscle"]).
func apply_wound(zone_id: String, severity: String, bleed_amount: float, pain_amount: float,
		effects: Array, bone_frac: bool = false, organ_hit: String = "",
		tissues: Array = []) -> void:
	if not body_zones.has(zone_id):
		body_zones[zone_id] = []
	body_zones[zone_id].append({
		"severity":       severity,
		"bleed":          bleed_amount,
		"pain":           pain_amount,
		"effects":        effects.duplicate(),
		"tissues_reached": tissues.duplicate(),
		"bone_fractured": bone_frac,
		"organ_damaged":  organ_hit,
	})
	# Record structural damage flags.
	if bone_frac:
		bone_fractures[zone_id] = true
	if organ_hit != "":
		var plan_def: Dictionary = ContentRegistry.get_content("body_plan", body_plan_id)
		var vital: bool = false
		for organ: Dictionary in plan_def.get("organs", []):
			if organ.get("id", "") == organ_hit:
				vital = organ.get("vital", false)
				break
		var dmg_level: String = "destroyed" if severity == "lethal" else "damaged"
		organ_damage[organ_hit] = dmg_level
		if vital and dmg_level == "destroyed":
			effects = effects.duplicate()
			effects.append("death")
	# Immediately apply pain spike.
	pain     = clampf(pain     + pain_amount,   0.0, 2.0)
	bleeding = clampf(bleeding + bleed_amount,  0.0, 2.0)
	# Bone fracture adds pain and mobility impairment.
	if bone_frac:
		pain = clampf(pain + 0.15, 0.0, 2.0)
	_recalc_shock()
	_check_death(zone_id, severity)
	_apply_effects(effects)


## Recalculate shock from current pain and a bleeding contribution.
func _recalc_shock() -> void:
	shock = clampf(pain * 0.6 + bleeding * 0.4, 0.0, 2.0)
	if shock >= 1.0 and not is_dead:
		is_incapacitated = true


func _check_death(zone_id: String, severity: String) -> void:
	if severity == "lethal":
		var zone_def: Dictionary = ContentRegistry.get_content("body_zone", zone_id)
		for eff: Dictionary in zone_def.get("critical_effects", []):
			if eff.get("at_severity", "") == "lethal" and eff.get("effect", "") == "death":
				is_dead = true
				is_incapacitated = true
				return
	# Organ damage can also kill — checked via apply_wound's vital flag path.
	for organ_id: String in organ_damage:
		if organ_damage[organ_id] == "destroyed":
			var plan_def: Dictionary = ContentRegistry.get_content("body_plan", body_plan_id)
			for organ: Dictionary in plan_def.get("organs", []):
				if organ.get("id", "") == organ_id and organ.get("vital", false):
					is_dead         = true
					is_incapacitated = true
					return


func _apply_effects(effects: Array) -> void:
	for eff: String in effects:
		match eff:
			"knockout":
				is_incapacitated = true
			"death":
				is_dead          = true
				is_incapacitated = true


## Bleed tick: called each WEGO turn. Increases shock from ongoing blood loss.
## Organ damage adds extra bleed per turn.
func tick_bleed() -> void:
	if is_dead:
		return
	var extra_organ_bleed: float = 0.0
	for organ_id: String in organ_damage:
		extra_organ_bleed += 0.04 if organ_damage[organ_id] == "damaged" else 0.08
	shock = clampf(shock + (bleeding + extra_organ_bleed) * 0.1, 0.0, 2.0)
	if shock >= 1.0:
		is_incapacitated = true


## Stamina recovery: called at end of each WEGO turn.
## Encumbrance from equipped armor reduces recovery.
func tick_stamina_recovery() -> void:
	if is_dead or is_incapacitated:
		return
	var encumbrance: float = _calc_encumbrance()
	var regen: float = clampf(0.15 - encumbrance * 0.01, 0.02, 0.15)
	stamina = clampf(stamina + regen, 0.0, 1.0)


func _calc_encumbrance() -> float:
	var total: float = 0.0
	for slot: String in equipment_refs:
		var item_id: String = equipment_refs[slot]
		var adef: Dictionary = ContentRegistry.get_content("armor", item_id)
		if not adef.is_empty():
			total += float(adef.get("encumbrance", 0.0))
	return total


## Returns the weapon data dict for the currently equipped main-hand weapon,
## or an unarmed placeholder if no weapon is equipped.
func get_weapon_data() -> Dictionary:
	var wid: String = equipment_refs.get("main_hand", "")
	if wid != "":
		var wdef: Dictionary = ContentRegistry.get_content("weapon", wid)
		if not wdef.is_empty():
			return wdef
	# Unarmed fallback.
	return {
		"id": "unarmed", "reach_class": "unarmed",
		"damage_type": "blunt",
		"striking_mass_kg": 0.08, "contact_area_cm2": 20.0, "velocity_class": "slow",
		"swing_momentum": 3, "thrust_momentum": 2,
		"attack_speed": 1.2, "stamina_cost": 0.06,
		"min_skill": 0, "material_id": "", "quality": "standard",
	}


## Returns effective striking momentum for the equipped weapon.
## Physics model: momentum = striking_mass_kg × VELOCITY_FACTORS[velocity_class] × quality_mult × edge_retention
## Falls back to legacy swing_momentum if striking_mass_kg is absent (backwards compat).
func get_effective_weapon_momentum() -> float:
	var wdef: Dictionary = get_weapon_data()

	# Legacy fallback: if no striking_mass_kg, use old flat swing_momentum.
	var mass_kg: float = float(wdef.get("striking_mass_kg", 0.0))
	if mass_kg == 0.0:
		var base_m: float = float(wdef.get("swing_momentum", 3))
		var q_mod: float = QUALITY_MOMENTUM_MULTIPLIER.get(wdef.get("quality", "standard"), 1.0)
		return base_m * q_mod

	# Physics path.
	var vel_factor: float = VELOCITY_FACTORS.get(wdef.get("velocity_class", "medium"), 25.0)
	var q_mult: float     = QUALITY_MOMENTUM_MULTIPLIER.get(wdef.get("quality", "standard"), 1.0)

	# Material edge_retention scales effective sharpness for edged weapons.
	var mat_id: String    = wdef.get("material_id", "")
	var edge_ret: float   = 1.0
	if mat_id != "":
		var mdef: Dictionary = ContentRegistry.get_content("material", mat_id)
		if not mdef.is_empty():
			edge_ret = float(mdef.get("edge_retention", 1.0))

	return mass_kg * vel_factor * q_mult * edge_ret


## Returns physics layer data for a given zone, sorted outermost-first.
## Each element: { "coverage": float, "yield_strength": float, "thickness_mm": float,
##                 "blunt_transfer": float, "layer": String }
## The resolver computes pressure = momentum / contact_area, then checks
## pressure >= yield_strength × thickness_mm per layer.
func get_layered_armor_for_zone(zone_id: String, _damage_type: String) -> Array:
	var layers: Array = []
	for slot: String in equipment_refs:
		var item_id: String = equipment_refs[slot]
		var adef: Dictionary = ContentRegistry.get_content("armor", item_id)
		if adef.is_empty():
			continue
		var cz: Dictionary = adef.get("coverage_zones", {})
		var cov: float = float(cz.get(zone_id, 0.0))
		if cov <= 0.0:
			continue

		# Physics properties come from the material definition.
		var mat_id: String      = adef.get("material_id", "")
		var yield_str: float    = 0.0
		var blunt_xfer: float   = 1.0
		if mat_id != "":
			var mdef: Dictionary = ContentRegistry.get_content("material", mat_id)
			if not mdef.is_empty():
				yield_str  = float(mdef.get("yield_strength",  0.0))
				blunt_xfer = float(mdef.get("blunt_transfer",  1.0))

		var thickness: float = float(adef.get("thickness_mm", 0.0))

		# Quality multiplier scales effective thickness (better craft → tighter weave/forging).
		var quality: String  = adef.get("quality", "standard")
		var q_mod: float     = QUALITY_DR_MULTIPLIER.get(quality, 1.0)
		var eff_thickness: float = thickness * q_mod

		var layer: String = adef.get("layer", "armor")
		layers.append({
			"coverage":      cov,
			"yield_strength": yield_str,
			"thickness_mm":  eff_thickness,
			"blunt_transfer": blunt_xfer,
			"layer":         layer,
		})
	# Sort outermost first.
	layers.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		return LAYER_SORT_ORDER.get(a["layer"], 1) < LAYER_SORT_ORDER.get(b["layer"], 1)
	)
	return layers


# ── Serialisation ─────────────────────────────────────────────────────────────

func to_dict() -> Dictionary:
	return {
		"combatant_id":    combatant_id,
		"formation_id":    formation_id,
		"team_id":         team_id,
		"display_name":    display_name,
		"body_plan_id":    body_plan_id,
		"tile_pos":        { "x": tile_pos.x, "y": tile_pos.y },
		"z_level":         z_level,
		"stamina":         stamina,
		"pain":            pain,
		"bleeding":        bleeding,
		"shock":           shock,
		"is_incapacitated": is_incapacitated,
		"is_dead":         is_dead,
		"body_zones":      body_zones.duplicate(true),
		"bone_fractures":  bone_fractures.duplicate(),
		"organ_damage":    organ_damage.duplicate(),
		"equipment_refs":  equipment_refs.duplicate(),
		"current_order":   current_order,
		"resolved_actions": resolved_actions.duplicate(true),
		"melee_skill":     melee_skill,
		"agility":         agility,
	}


static func from_dict(d: Dictionary) -> CombatantState:
	var c := CombatantState.new()
	c.combatant_id    = d.get("combatant_id",    "")
	c.formation_id    = d.get("formation_id",    "")
	c.team_id         = d.get("team_id",         "")
	c.display_name    = d.get("display_name",    "")
	c.body_plan_id    = d.get("body_plan_id",    "human")
	var tp: Dictionary = d.get("tile_pos", {"x": 0, "y": 0})
	c.tile_pos        = Vector2i(int(tp.get("x", 0)), int(tp.get("y", 0)))
	c.z_level         = int(d.get("z_level",         0))
	c.stamina         = float(d.get("stamina",        1.0))
	c.pain            = float(d.get("pain",           0.0))
	c.bleeding        = float(d.get("bleeding",       0.0))
	c.shock           = float(d.get("shock",          0.0))
	c.is_incapacitated = bool(d.get("is_incapacitated", false))
	c.is_dead         = bool(d.get("is_dead",         false))
	c.body_zones      = d.get("body_zones",      {}).duplicate(true)
	c.bone_fractures  = d.get("bone_fractures",  {}).duplicate()
	c.organ_damage    = d.get("organ_damage",    {}).duplicate()
	c.equipment_refs  = d.get("equipment_refs",  {}).duplicate()
	c.current_order   = d.get("current_order",   "")
	c.resolved_actions = d.get("resolved_actions", []).duplicate(true)
	c.melee_skill     = int(d.get("melee_skill",     0))
	c.agility         = int(d.get("agility",         5))
	return c
