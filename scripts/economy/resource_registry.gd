class_name ResourceRegistry

## Central registry for all tradeable resources.
## Provides base prices, daily demand, and per-resource metadata.
## All methods are static — no instance needed.

# ── Master resource list ──────────────────────────────────────────────────────
const ALL_RESOURCES: PackedStringArray = [
	# Subsistence
	"grain", "wood", "fish",
	# Surface mining (all rocky terrain)
	"stone",
	# Geological minerals — sedimentary belt
	"ore", "coal", "lead", "clay",
	# Geological minerals — metamorphic belt
	"copper", "silver", "marble", "tin",
	# Geological minerals — igneous belt
	"gold", "gems",
	# Organic raw
	"meat", "furs", "salt",
	# Processed goods
	"ale", "cloth", "leather", "iron", "timber",
]

# ── Base prices (silver coins per unit) ──────────────────────────────────────
const _BASE_PRICE: Dictionary = {
	# Subsistence
	"grain":   2.0,
	"wood":    3.0,
	"fish":    3.0,
	# Surface mining
	"stone":   4.0,
	# Sedimentary minerals
	"ore":     8.0,
	"coal":    5.0,
	"lead":    6.0,
	"clay":    3.0,
	# Metamorphic minerals
	"copper":  12.0,
	"silver":  30.0,
	"marble":  10.0,
	"tin":     9.0,
	# Igneous minerals
	"gold":    80.0,
	"gems":    60.0,
	# Organic raw
	"meat":    6.0,
	"furs":    12.0,
	"salt":    7.0,
	# Processed goods
	"ale":     5.0,
	"cloth":   10.0,
	"leather": 9.0,
	"iron":    15.0,
	"timber":  6.0,
}

# ── Daily consumption per capita (units/person/day) ──────────────────────────
# Only subsistence goods have per-capita demand; luxuries use class-based demand.
const _DAILY_PER_CAPITA: Dictionary = {
	"grain": 1.2,
	"wood":  0.02,  # fuel; scales with climate (not yet implemented)
}


## Base market price for a resource, or 1.0 if unknown.
static func base_price(resource_id: String) -> float:
	return _BASE_PRICE.get(resource_id, 1.0)


## Expected daily demand at this settlement, used for price calculation.
## Returns units/day the market expects to move.
static func daily_demand(resource_id: String, settlement: Object) -> float:
	match resource_id:
		"grain":
			return settlement.population * 1.2
		"wood":
			return settlement.population * 0.02
		"ale":
			return settlement.burghers * 0.1 + settlement.nobility * 0.2
		"meat":
			return settlement.nobility * 0.5 + settlement.burghers * 0.05
		"furs":
			return settlement.nobility * 0.05
		"salt":
			return settlement.population * 0.03
		"cloth":
			return settlement.population * 0.01
		"fish":
			return settlement.population * 0.3 if settlement.fishing_slots > 0 else 0.0
	return 0.0


## Display name for a resource.
static func display_name(resource_id: String) -> String:
	return resource_id.capitalize()


## Whether a resource is a raw material (vs. processed good).
static func is_raw(resource_id: String) -> bool:
	return resource_id in [
		"grain", "wood", "stone", "fish",
		"ore", "coal", "lead", "clay",
		"copper", "silver", "marble", "tin",
		"gold", "gems",
	]
