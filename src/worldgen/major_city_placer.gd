## MajorCityPlacer — places tier 3-4 settlements globally with enforced spacing.
##
## This is the first step in settlement-first worldgen architecture:
##   1. Major cities (tier 3-4) placed by quality + spacing constraint
##   2. Provinces generated around these city anchors
##   3. Minor settlements (tier 0-2) placed within provinces
##
## Key design: Spacing constraints prevent clustering of major settlements
## in high-quality regions, ensuring geographic diversity.
class_name MajorCityPlacer
extends RefCounted

## Minimum spacing between tier-4 metropolises (roughly 1/6 of map width).
const METROPOLIS_MIN_SPACING: int = 50

## Minimum spacing between tier-3 cities (roughly 1/12 of map width).
const CITY_MIN_SPACING: int = 35

## Score jitter range to prevent deterministic resource monopolies.
## Applied as multiplier: score × randf_range(MIN_JITTER, MAX_JITTER)
const MIN_JITTER: float = 0.8
const MAX_JITTER: float = 1.2


## Main entry point. Returns Array[Dictionary] of tier 3-4 settlements.
## Each entry: {tile_x: int, tile_y: int, tier: int, score: float}
##
## Target counts:
##   - Tier 4 (metropolis): 1
##   - Tier 3 (city): varies by map size (8-15 for 512x512)
static func place_major_cities(
		data: WorldGenData,
		_params: WorldGenParams
) -> Array:
	var metropolises := _place_tier(data, 4, 1, METROPOLIS_MIN_SPACING)
	
	# Scale city count with map area: ~1 city per 20,000 tiles
	var map_area: int = data.width * data.height
	@warning_ignore("integer_division")
	var city_target: int = clampi(map_area / 20000, 6, 20)
	
	var cities := _place_tier(data, 3, city_target, CITY_MIN_SPACING)
	
	return metropolises + cities


## Greedy best-first placement with spacing constraint and fallback relaxation.
static func _place_tier(
		data: WorldGenData,
		tier: int,
		target_count: int,
		min_spacing: int
) -> Array:
	var candidates := _collect_scored_tiles(data, data.world_seed, tier)
	
	# Greedy placement with spacing enforcement.
	var placed: Array = []
	var min_spacing_sq: int = min_spacing * min_spacing
	var current_spacing_sq: int = min_spacing_sq
	
	# Adaptive spacing: relax constraint if we can't meet target count.
	# Try: min_spacing → 0.9× → 0.8× → 0.7× → give up.
	const RELAXATION_STEPS: Array[float] = [1.0, 0.9, 0.8, 0.7]
	var relaxation_idx: int = 0
	
	while placed.size() < target_count and relaxation_idx < RELAXATION_STEPS.size():
		var multiplier: float = RELAXATION_STEPS[relaxation_idx]
		current_spacing_sq = int(float(min_spacing_sq) * multiplier * multiplier)
		
		for candidate in candidates:
			if placed.size() >= target_count:
				break
			
			var pos := Vector2i(candidate.x, candidate.y)
			
			# Check spacing constraint against all already-placed settlements.
			var too_close := false
			for existing in placed:
				var existing_pos: Vector2i = existing.get("pos", Vector2i(-1000, -1000))
				var dist_sq: int = pos.distance_squared_to(existing_pos)
				if dist_sq < current_spacing_sq:
					too_close = true
					break
			
			if not too_close:
				placed.append({
					"tile_x": pos.x,
					"tile_y": pos.y,
					"tier":   tier,
					"is_hub": tier == 3,  # Only tier-3 cities are provincial hubs
					"pos":    pos,        # Cache for distance checks
				})
		
		# If we didn't meet target, relax spacing and try again.
		if placed.size() < target_count:
			relaxation_idx += 1
	
	# Diagnostic warning if we still couldn't meet target.
	if placed.size() < target_count:
		push_warning("MajorCityPlacer: Could only place %d/%d tier-%d settlements (spacing constraint too tight)" \
				% [placed.size(), target_count, tier])
	
	return placed


## Collects all habitable land tiles with their settlement scores.
## Returns Array[Dictionary]: [{x: int, y: int, score: float}, ...]
## Sorted descending by jittered score (best first).
static func _collect_scored_tiles(
		data: WorldGenData,
		world_seed: int,
		tier: int
) -> Array:
	var candidates: Array = []
	
	for y in range(data.height):
		for x in range(data.width):
			var score: float = data.settlement_score[y][x]
			if score <= 0.0:
				continue  # Water or unsuitable land
			
			candidates.append({
				"x":     x,
				"y":     y,
				"score": score,
			})
	
	# Apply score jitter to prevent deterministic resource monopolies.
	# If 3 plains tiles have identical scores, the first one shouldn't
	# always win just because it's scanned first.
	var rng := RandomNumberGenerator.new()
	for i in range(candidates.size()):
		var c: Dictionary = candidates[i]
		rng.seed = world_seed ^ (c.x * 7919 + c.y * 6547 + tier * 131071)
		var jitter: float = rng.randf_range(MIN_JITTER, MAX_JITTER)
		c["score"] = c.score * jitter
	
	# Sort descending by jittered score.
	candidates.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		return a.score > b.score
	)
	
	return candidates
