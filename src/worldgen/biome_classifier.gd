## BiomeClassifier — Whittaker-style biome classification.
##
## Given altitude, temperature, precipitation, and sea_level, returns a
## biome ID string. The string matches IDs in /data/world/biomes.json.
##
## Algorithm ported from the reference project (moneybagboyz/Falling-Leaves-
## Ravensguard), adapted to return content IDs rather than enum ints.
class_name BiomeClassifier
extends RefCounted


## Classify one cell. Returns a biome content ID string.
static func classify(
		alt: float, temp: float, precip: float, sea_level: float
) -> String:
	# ── Water bands ───────────────────────────────────────────────────────────
	var deep_cut := sea_level * 0.15
	if alt < deep_cut:
		return "deep_ocean"
	if alt < sea_level - sea_level * 0.25:
		return "ocean"
	if alt < sea_level:
		return "shallow_water"

	# ── Coastal strip ─────────────────────────────────────────────────────────
	if alt < sea_level + 0.015:
		return "beach"

	# ── High peaks ────────────────────────────────────────────────────────────
	if alt > 0.85:
		return "mountain_snow" if temp < 0.35 else "mountain_rock"
	if alt > 0.72:
		return "mountain_rock"

	# ── Cold zones ────────────────────────────────────────────────────────────
	if temp < 0.14:
		return "snow"
	if temp < 0.26:
		return "tundra" if precip < 0.45 else "boreal_forest"

	# ── Temperate zones ───────────────────────────────────────────────────────
	if temp < 0.52:
		if precip < 0.22:
			return "grassland"
		if precip < 0.50:
			return "woodland"
		return "temperate_forest"

	# ── Warm / tropical zones ─────────────────────────────────────────────────
	if precip < 0.18:
		return "desert"
	if precip < 0.40:
		return "savanna"
	if precip < 0.65:
		return "grassland"
	return "tropical_rainforest"


## Populate data.biome[y][x] for every cell in the grid.
static func classify_all(data: WorldGenData) -> void:
	for y in range(data.height):
		for x in range(data.width):
			var b: String = classify(
				data.altitude[y][x],
				data.temperature[y][x],
				data.precipitation[y][x],
				data.sea_level
			)
			data.biome[y][x]      = b
			data.prosperity[y][x] = base_fertility(b)


## Fertility (0.0–1.0) for a biome. Used to derive prosperity.
## Values from /data/world/biomes.json; hardcoded as fallback so generation
## works before data is fully loaded.
static func base_fertility(biome_id: String) -> float:
	# Try ContentRegistry first (loaded from biomes.json).
	if ContentRegistry.has_content("biome", biome_id):
		return ContentRegistry.get_content("biome", biome_id).get("base_fertility", 0.0)
	# Hardcoded fallback table (mirrors biomes.json values).
	const TABLE: Dictionary = {
		"deep_ocean":         0.00,
		"ocean":              0.00,
		"shallow_water":      0.05,
		"beach":              0.08,
		"desert":             0.04,
		"savanna":            0.40,
		"tropical_rainforest": 0.55,
		"grassland":          0.78,
		"woodland":           0.65,
		"temperate_forest":   0.70,
		"boreal_forest":      0.42,
		"tundra":             0.10,
		"snow":               0.00,
		"mountain_rock":      0.05,
		"mountain_snow":      0.00,
	}
	return TABLE.get(biome_id, 0.0)
