class_name ResourceRegistry

## Central registry for all tradeable resources.
## Provides base prices, daily demand, and per-resource metadata.
## All methods are static — no instance needed.

# ── Master resource list ──────────────────────────────────────────────────────
const ALL_RESOURCES: PackedStringArray = [
	# Subsistence
	"grain", "wood", "fish",
	# Organic raw — food & materials
	"meat", "game", "wool", "hides", "furs",
	# Surface mining
	"stone",
	# Geological minerals — sedimentary belt
	"coal", "lead", "clay",
	# Geological minerals — metamorphic belt
	"copper", "silver", "marble", "tin",
	# Geological minerals — igneous belt
	"gold", "gems",
	# Preserved
	"salt",
	# Metals (mined raw + processed)
	"iron", "bronze", "steel",
	# Processed — food & drink
	"ale",
	# Processed — textiles & materials
	"cloth", "leather", "timber", "bricks",
	# Processed — luxury
	"jewelry",
	# Deferred (no production chain yet): tools, livestock, silk, spices, horses
]

# ── Base prices (silver coins per unit) ──────────────────────────────────────
const _BASE_PRICE: Dictionary = {
	# Subsistence
	"grain":     2.0,
	"wood":      3.0,
	"fish":      3.0,
	# Organic raw
	"meat":      6.0,
	"game":      3.0,
	"wool":      4.0,
	"hides":     3.0,
	"furs":      24.0,
	# Surface mining
	"stone":     4.0,
	# Sedimentary minerals
	"coal":      5.0,
	"lead":      6.0,
	"clay":      3.0,
	# Metamorphic minerals
	"copper":    12.0,
	"silver":    30.0,
	"marble":    20.0,
	"tin":       9.0,
	# Igneous minerals
	"gold":      80.0,
	"gems":      120.0,
	# Preserved
	"salt":      9.0,
	# Metals
	"iron":      8.0,
	"bronze":    12.0,
	"steel":     24.0,
	# Processed
	"ale":       6.0,
	"cloth":     10.0,
	"leather":   12.0,
	"timber":    6.0,
	"bricks":    5.0,
	"jewelry":   200.0,
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
			return settlement.nobility * 0.5 + settlement.population * 0.05
		"game":
			return settlement.population * 0.05 if settlement.forest_acres > 0.0 else 0.0
		"furs":
			return settlement.nobility * 0.05
		"wool":
			# Floor ensures price doesn't deflate before a workshop exists.
			return maxf(settlement.population * 0.002, float(settlement._building_level("workshop")) * 6.0)
		"hides":
			return maxf(settlement.population * 0.001, float(settlement._building_level("workshop")) * 4.0)
		"stone":
			# Construction demand: every settlement slowly uses stone
			return settlement.population * 0.01
		"salt":
			return settlement.population * 0.03
		"cloth":
			return settlement.population * 0.005
		"leather":
			return settlement.population * 0.003
		"jewelry":
			return settlement.nobility * 0.01
		"fish":
			return settlement.population * 0.3 if settlement.fishing_slots > 0 else 0.0
		"iron":
			return float(settlement._building_level("forge")) * 2.0
		"coal":
			return settlement.population * 0.01
	return 0.0


## Display name for a resource.
static func display_name(resource_id: String) -> String:
	return resource_id.capitalize()


## Whether a resource is a raw material (vs. processed good).
static func is_raw(resource_id: String) -> bool:
	return resource_id in [
		"grain", "wood", "stone", "fish",
		"iron", "coal", "lead", "clay",
		"copper", "silver", "marble", "tin",
		"gold", "gems",
		"meat", "game", "wool", "hides", "furs",
	]
