## GeologyGenerator — places mineral and resource deposits on the grid.
##
## Resource tags are written into WorldGenData.geology and later transferred
## to RegionCell.resource_tags. Tag strings match content IDs in
## /data/goods/ so ProductionLedger can look them up directly.
##
## Differentiated by terrain/biome per the reference project's geology system.
class_name GeologyGenerator
extends RefCounted

## Ore deposit probability per terrain type.
const ORE_CHANCE: Dictionary = {
	"mountain": 0.22,
	"hills":    0.12,
	"plains":   0.03,
	"forest":   0.04,
	"tundra":   0.08,
	"desert":   0.06,
}

## Timber probability per terrain.
const TIMBER_CHANCE: Dictionary = {
	"forest":        0.90,
	"temperate_forest": 0.90,
	"boreal_forest": 0.85,
	"woodland":      0.60,
	"hills":         0.20,
	"plains":        0.05,
}

## Ore type weights by biome.
## Dict: biome_id -> {ore_id: relative_weight, ...}
const ORE_WEIGHTS_BY_BIOME: Dictionary = {
	"mountain_rock": {"iron_ore": 5, "stone": 8, "copper_ore": 3, "coal_ore": 2},
	"mountain_snow": {"iron_ore": 4, "stone": 8, "silver_ore": 2, "coal_ore": 1},
	"tundra":        {"iron_ore": 3, "stone": 5, "coal_ore": 4},
	"hills":         {"iron_ore": 6, "stone": 6, "copper_ore": 2, "clay": 4},
	"desert":        {"stone": 5, "salt": 6, "copper_ore": 3},
	_DEFAULT:        {"iron_ore": 4, "stone": 6, "clay": 3},
}
const _DEFAULT := "__default__"

## Arable bonus biomes: add fertile_soil tag.
const FERTILE_BIOMES: Array = ["grassland", "woodland", "savanna", "temperate_forest"]

## Water access tag: cells adjacent to rivers or coasts get this.
const WATER_ACCESS_TAG := "water_access"


static func assign(data: WorldGenData, seed_val: int) -> void:
	var rng := RandomNumberGenerator.new()

	for y in range(data.height):
		for x in range(data.width):
			if not data.is_land(x, y):
				continue

			var terrain: String = data.terrain[y][x]
			var biome:   String = data.biome[y][x]
			var tags: Array     = []

			# ── Timber ───────────────────────────────────────────────────────
			var timber_prob: float = TIMBER_CHANCE.get(terrain,
				TIMBER_CHANCE.get(biome, 0.0))
			rng.seed = seed_val ^ (y * 131071 + x * 49297 + 1)
			if rng.randf() < timber_prob:
				tags.append("timber")

			# ── Ore / mineral deposits ────────────────────────────────────────
			var ore_prob: float = ORE_CHANCE.get(terrain, 0.02)
			rng.seed = seed_val ^ (y * 73856093 + x * 19349663 + 2)
			if rng.randf() < ore_prob:
				var ore_id := _pick_ore(biome, rng)
				if ore_id != "":
					tags.append(ore_id)

			# ── Fertile soil ──────────────────────────────────────────────────
			if biome in FERTILE_BIOMES and terrain == "plains":
				tags.append("fertile_soil")

			# ── Water access ──────────────────────────────────────────────────
			if _has_water_adjacent(data, x, y):
				tags.append(WATER_ACCESS_TAG)

			if not tags.is_empty():
				data.geology[Vector2i(x, y)] = tags


static func _pick_ore(biome: String, rng: RandomNumberGenerator) -> String:
	var weights: Dictionary = ORE_WEIGHTS_BY_BIOME.get(biome,
		ORE_WEIGHTS_BY_BIOME[_DEFAULT])
	var total: int = 0
	for w in weights.values():
		total += w
	if total == 0:
		return ""
	var roll := rng.randi() % total
	var cumulative := 0
	for ore_id in weights:
		cumulative += weights[ore_id]
		if roll < cumulative:
			return ore_id
	return ""


static func _has_water_adjacent(data: WorldGenData, x: int, y: int) -> bool:
	for dy in [-1, 0, 1]:
		for dx in [-1, 0, 1]:
			if dx == 0 and dy == 0:
				continue
			var nx: int = x + dx
			var ny: int = y + dy
			if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
				continue
			if data.is_river[ny][nx] or data.is_lake[ny][nx]:
				return true
			var t: String = data.terrain[ny][nx]
			if t == "coast" or t == "ocean" or t == "shallow_water":
				return true
	return false


## Derive prosperity score for every land cell.
## prosperity = biome_base_fertility × (1 + drainage_bonus) - altitude_penalty
## Runs after biome + hydrology passes.
static func derive_prosperity(data: WorldGenData) -> void:
	for y in range(data.height):
		for x in range(data.width):
			if not data.is_land(x, y):
				data.prosperity[y][x] = 0.0
				continue
			var base: float = BiomeClassifier.base_fertility(data.biome[y][x])
			var drain_bonus: float = data.drainage[y][x] * 0.28
			data.prosperity[y][x] = clampf(base + drain_bonus - 0.12, 0.0, 1.0)
