class_name Building extends RefCounted

## Represents a single building slot in a settlement.
## Buildings are keyed by type string and have a level (1..MAX_LEVEL).
## The multiplier they apply to production is inline here — no JSON dependency.

const MAX_LEVEL: int = 5

var building_type: String   # "farm", "lumber_mill", "mine", "fishery",
                             # "tavern", "warehouse", "barracks", "church", "market"
var level: int = 1

# ── Inline building definitions ───────────────────────────────────────────────
# Format: building_type -> { "name", "per_level_bonus" (additive per level) }
const DEFINITIONS: Dictionary = {
	"farm":        { "name": "Farm",         "per_level_bonus": 0.50 },
	"lumber_mill": { "name": "Lumber Mill",  "per_level_bonus": 1.00 },
	"mine":        { "name": "Mine",         "per_level_bonus": 0.50 },
	"fishery":     { "name": "Fishery",      "per_level_bonus": 0.40 },
	"tavern":      { "name": "Tavern",       "per_level_bonus": 0.30 },
	"warehouse":   { "name": "Warehouse",    "per_level_bonus": 0.20 },
	"barracks":    { "name": "Barracks",     "per_level_bonus": 0.25 },
	"church":      { "name": "Church",       "per_level_bonus": 0.15 },
	"market":      { "name": "Market Stall", "per_level_bonus": 0.20 },
	"forge":       { "name": "Forge",        "per_level_bonus": 0.40 },
	"workshop":    { "name": "Workshop",     "per_level_bonus": 0.40 },
	"jeweler":     { "name": "Jeweler",      "per_level_bonus": 0.50 },
}


func _init(type: String, starting_level: int = 1) -> void:
	building_type = type
	level = clampi(starting_level, 1, MAX_LEVEL)


## Production multiplier: base 1.0 + level × per_level_bonus.
func get_multiplier() -> float:
	if not DEFINITIONS.has(building_type):
		return 1.0
	return 1.0 + level * float(DEFINITIONS[building_type]["per_level_bonus"])


## Display name from the definition table.
func display_name() -> String:
	if not DEFINITIONS.has(building_type):
		return building_type.capitalize()
	return DEFINITIONS[building_type]["name"] + " (Lvl %d)" % level


## Cost in silver to upgrade to next level (simple formula; tune later).
func upgrade_cost() -> float:
	return 50.0 * (level + 1) * (level + 1)


## Returns true if the building can still be upgraded.
func can_upgrade() -> bool:
	return level < MAX_LEVEL
