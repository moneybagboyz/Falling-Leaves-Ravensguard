## SettlementScorer — scores every land tile for settlement suitability.
##
## Ported directly from ProvinceGenerator.score_settlement_sites() in the
## reference project, adapted to work with our WorldGenData structure.
##
## Key learnings from reference (FMG-style):
##  • Flux-normalised river bonus: a major river in a dry world scores higher
##    than a trickle in a well-watered one.
##  • Terrain scan uses a 1-tile radius to prevent plains clusters from
##    dominating over an entire region.
##  • Elevation penalty: lower land near sea level is most desirable.
##  • Estuary bonus: river tile adjacent to coast.
class_name SettlementScorer
extends RefCounted

## Per-terrain score bonuses in the local scan radius.
const TERRAIN_BONUS: Dictionary = {
	"plains":   4.0,
	"forest":   1.5,
	"hills":    2.0,
	"mountain": 2.0,  # mining value
	"coast":    3.0,
	"river":    2.0,
}


static func score_all(data: WorldGenData) -> void:
	# ── Pre-compute drainage statistics for flux normalisation ────────────
	var max_drainage: float = 0.001
	var total_drain:  float = 0.0
	var drain_count:  int   = 0

	for y in range(data.height):
		for x in range(data.width):
			if data.is_land(x, y):
				var d: float = data.drainage[y][x]
				if d > max_drainage:
					max_drainage = d
				total_drain += d
				drain_count  += 1

	var mean_drainage: float = total_drain / maxf(drain_count, 1.0)

	# Elevation range for penalty normalisation.
	var max_alt: float = 1.0
	for y in range(data.height):
		for x in range(data.width):
			if data.altitude[y][x] > max_alt:
				max_alt = data.altitude[y][x]
	var alt_range: float = maxf(max_alt - data.sea_level, 0.001)

	for y in range(data.height):
		for x in range(data.width):
			if not data.is_land(x, y):
				data.settlement_score[y][x] = 0.0
				continue

			var score: float = 0.0

			# ── 1-tile terrain scan ──────────────────────────────────────────
			for dy in range(-1, 2):
				for dx in range(-1, 2):
					var nx: int = x + dx
					var ny: int = y + dy
					if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
						continue
					score += TERRAIN_BONUS.get(data.terrain[ny][nx], 0.0)

			# ── Flux-normalised river bonus ──────────────────────────────────
			var cell_drain: float = data.drainage[y][x]
			if cell_drain > 0.0:
				var norm_drain: float = minf(cell_drain / max_drainage, 1.0)
				score += norm_drain * 8.0
				if cell_drain > mean_drainage:
					score += ((cell_drain - mean_drainage) / max_drainage) * 4.0

			# ── Estuary bonus (river tile adjacent to a coast tile) ────────────────			
			if data.is_river[y][x] and _adjacent_to_coast(data, x, y):
				score += 6.0

			# ── Elevation penalty ────────────────────────────────────────────
			var rel_alt: float = (data.altitude[y][x] - data.sea_level) / alt_range
			score -= rel_alt * 10.0

			# ── Prosperity bonus (fertile soil nearby) ────────────────────────
			score += data.prosperity[y][x] * 5.0

			data.settlement_score[y][x] = maxf(score, 0.0)


static func _adjacent_to_coast(data: WorldGenData, x: int, y: int) -> bool:
	for dy in [-1, 0, 1]:
		for dx in [-1, 0, 1]:
			if dx == 0 and dy == 0:
				continue
			var nx: int = x + dx
			var ny: int = y + dy
			if nx < 0 or nx >= data.width or ny < 0 or ny >= data.height:
				continue
			if data.terrain[ny][nx] == "coast":
				return true
	return false
