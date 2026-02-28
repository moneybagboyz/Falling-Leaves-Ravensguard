## PersonState — data class for a single named person (player or NPC).
##
## Stores the full persistent state for a character: identity, attributes,
## traits, skills, body state, needs, social links, reputation, and location.
##
## Must never be owned by a scene. All scene access is read-only.
## Serialised/deserialised as part of WorldState.characters (and npc_pool).
class_name PersonState
extends RefCounted

## Skill levels that trigger a perk unlock notification (perks themselves are Phase 6).
const PERK_THRESHOLDS: Array[int] = [5, 10, 20]

# ── Identity ─────────────────────────────────────────────────────────────────

## Stable EntityRegistry ID.
var person_id: String = ""

var name: String = ""

## Background ID (from data/backgrounds/*.json).
var background_id: String = ""

## Which settlement this person considers home. Empty = wanderer.
var home_settlement_id: String = ""

## Population class this person belongs to (peasant/artisan/merchant/noble).
## For player characters, set to their primary background's class.
var population_class: String = ""

# ── Attributes ───────────────────────────────────────────────────────────────
## Base attributes before trait modifiers.
## Keys: strength, endurance, agility, perception, intelligence, charisma.
## Values: integer (base 5; creation budget adds ±points; traits add further).
var attributes: Dictionary = {
	"strength":     5,
	"endurance":    5,
	"agility":      5,
	"perception":   5,
	"intelligence": 5,
	"charisma":     5,
}

# ── Traits ───────────────────────────────────────────────────────────────────
## Array of trait IDs (from data/traits/*.json).
var traits: Array[String] = []

# ── Skills ───────────────────────────────────────────────────────────────────
## skill_id -> { "level": int, "progress": float (0.0–1.0) }
## Only skills with non-zero level are stored. Missing key = level 0.
var skills: Dictionary = {}

# ── Body state ───────────────────────────────────────────────────────────────
## Wound and physical condition tracking.
## Populated by the combat system (Phase 4).
## phase3: kept as empty dict; NeedsComponent uses 'needs' below.
var body_state: Dictionary = {}

# ── Needs ─────────────────────────────────────────────────────────────────────
## Persistent need values managed by NeedsComponent (P3-11).
## hunger: 0.0 (full) → 1.0 (starving).
## fatigue: 0.0 (rested) → 1.0 (exhausted).
## temperature_stress: 0.0 (comfortable) → 1.0 (critical).
var needs: Dictionary = {
	"hunger":            0.0,
	"fatigue":           0.0,
	"temperature_stress": 0.0,
}

# ── Social ───────────────────────────────────────────────────────────────────
## Array of {person_id, relationship_type, strength} dicts.
## Kept small — only people the character has personally interacted with.
var social_links: Array = []

## settlement_id or faction_id -> float reputation (-1.0 to 1.0).
var reputation: Dictionary = {}

# ── Ownership ────────────────────────────────────────────────────────────────
## EntityRegistry IDs of owned buildings/assets.
var ownership_refs: Array[String] = []

## Personal coin balance (earned from wages, trade, etc.).
var coin: float = 0.0

# ── Role & location ───────────────────────────────────────────────────────────
## Current assigned role: "" | "farm_hand" | "innkeeper" | "wandering" | etc.
var active_role: String = ""

## Shelter status: "" | "rented" | "owned" | "derelict_claimed"
var shelter_status: String = ""

## cell_id of the labor slot building (if active_role is set).
var work_cell_id: String = ""

## Full positional location.
## cell_id: world tile key (matches WorldState.world_tiles key).
## rx, ry:  region cell coords within the world tile (0–249).
## lx, ly:  local tile coords within the region cell (0–24). Unused until P4-01.
## z_level: 0 = ground, 1 = upper floor, -1 = cellar.
var location: Dictionary = {
	"cell_id": "",
	"lx":      0,
	"ly":      0,
	"z_level": 0,
}

# ── NPC schedule (used by P3-13) ─────────────────────────────────────────────
## "working" | "resting" | "wandering" | "idle"
var schedule_state: String = "idle"

## Accumulated perk-unlock notifications: Array of {skill_id, level} dicts.
## Set by award_skill_xp() when a skill crosses a PERK_THRESHOLD.
## Consumed by the UI to show perk prompts (Phase 6 will act on them).
var pending_perk_unlocks: Array = []

# ── Skill XP helper ──────────────────────────────────────────────────────────

## Award XP to a skill. Applies trait multipliers from ContentRegistry.
## Increments level when progress reaches 1.0.
## xp_amount should be a small positive float (e.g. 0.01 per action).
func award_skill_xp(skill_id: String, xp_amount: float) -> void:
	# Apply trait multipliers.
	var multiplier: float = 1.0
	for tid: String in traits:
		var tdef: Dictionary = ContentRegistry.get_content("trait", tid)
		if tdef.is_empty():
			continue
		var mults: Dictionary = tdef.get("skill_xp_multipliers", {})
		if mults.has(skill_id):
			multiplier *= float(mults[skill_id])

	var adjusted: float = xp_amount * multiplier

	if not skills.has(skill_id):
		skills[skill_id] = { "level": 0, "progress": 0.0 }

	var entry: Dictionary = skills[skill_id]
	var sdef: Dictionary  = ContentRegistry.get_content("skill", skill_id)
	var max_level: int    = int(sdef.get("max_level", 20)) if not sdef.is_empty() else 20
	var xp_cap: float     = float(sdef.get("xp_per_level", 100.0)) if not sdef.is_empty() else 100.0

	if entry["level"] >= max_level:
		return

	entry["progress"] += adjusted / xp_cap
	if entry["progress"] >= 1.0:
		entry["level"]   += 1
		entry["progress"] = entry["progress"] - 1.0
		# Check whether the new level crosses a perk threshold.
		if entry["level"] in PERK_THRESHOLDS:
			pending_perk_unlocks.append({
				"skill_id": skill_id,
				"level":    entry["level"],
			})
	skills[skill_id] = entry


## Returns the effective level of skill_id (0 if never trained).
func skill_level(skill_id: String) -> int:
	return skills.get(skill_id, { "level": 0 })["level"]


## Returns the effective value of an attribute after applying all trait modifiers.
func effective_attribute(attr_id: String) -> int:
	var base: int = int(attributes.get(attr_id, 5))
	for tid: String in traits:
		var tdef: Dictionary = ContentRegistry.get_content("trait", tid)
		if tdef.is_empty():
			continue
		var mods: Dictionary = tdef.get("attribute_modifiers", {})
		base += int(mods.get(attr_id, 0))
	return base


# ── Serialisation ─────────────────────────────────────────────────────────────

func to_dict() -> Dictionary:
	return {
		"person_id":           person_id,
		"name":                name,
		"background_id":       background_id,
		"population_class":    population_class,
		"home_settlement_id":  home_settlement_id,
		"attributes":          attributes.duplicate(),
		"traits":              traits.duplicate(),
		"skills":              skills.duplicate(true),
		"body_state":          body_state.duplicate(true),
		"needs":               needs.duplicate(),
		"social_links":        social_links.duplicate(true),
		"reputation":          reputation.duplicate(),
		"ownership_refs":      ownership_refs.duplicate(),
		"coin":                coin,
		"active_role":         active_role,
		"shelter_status":      shelter_status,
		"work_cell_id":        work_cell_id,
		"location":            location.duplicate(),
		"schedule_state":      schedule_state,
		"pending_perk_unlocks": pending_perk_unlocks.duplicate(true),
	}


static func from_dict(d: Dictionary) -> PersonState:
	var p := PersonState.new()
	p.person_id           = d.get("person_id",           "")
	p.name                = d.get("name",                "")
	p.background_id       = d.get("background_id",       "")
	p.population_class    = d.get("population_class",    "")
	p.home_settlement_id  = d.get("home_settlement_id",  "")
	p.attributes          = d.get("attributes",          {
		"strength": 5, "endurance": 5, "agility": 5,
		"perception": 5, "intelligence": 5, "charisma": 5,
	}).duplicate()
	# traits stored as plain Array in JSON; restore as Array[String].
	p.traits.assign(d.get("traits", []))
	p.skills              = d.get("skills",         {}).duplicate(true)
	p.body_state          = d.get("body_state",     {}).duplicate(true)
	p.needs               = d.get("needs", {
		"hunger": 0.0, "fatigue": 0.0, "temperature_stress": 0.0,
	}).duplicate()
	p.social_links        = d.get("social_links",   []).duplicate(true)
	p.reputation          = d.get("reputation",     {}).duplicate()
	p.ownership_refs.assign(d.get("ownership_refs", []))
	p.coin                = float(d.get("coin", 0.0))
	p.active_role         = d.get("active_role",         "")
	p.shelter_status      = d.get("shelter_status",      "")
	p.work_cell_id        = d.get("work_cell_id",        "")
	p.location            = d.get("location", {
		"cell_id": "", "lx": 0, "ly": 0, "z_level": 0,
	}).duplicate()
	p.schedule_state      = d.get("schedule_state",   "idle")
	p.pending_perk_unlocks = d.get("pending_perk_unlocks", []).duplicate(true)
	return p
