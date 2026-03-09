## GovernorAI — scores and queues building upgrades for a settlement.
##
## Called as Step 11 in SettlementPulse._tick_one.
## Reads ss.build_demand (populated by _update_build_demand) and
## ss.building_levels to compute a score per buildable type.
## If the top-scored candidate clears BUILD_THRESHOLD and the settlement
## has sufficient coin and a free slot, a construction job is written to
## ws.construction_jobs (keyed "settlement_id:building_id:target_level").
##
## All methods are static — no instance state; safe to call from tests.
class_name GovernorAI
extends RefCounted

## Tunable scoring parameters are loaded from data/config/economy_config.json.
static func _ec() -> Dictionary:
	return EconomyConfig.get_config()

## Adjacency bonus table (§8.3).
## Key = building being evaluated; value = Array of [neighbour_id, score_bonus].
const ADJACENCY_TABLE: Dictionary = {
	"iron_smelter": [["lumber_camp", 1.5]],
	"smithy":       [["iron_smelter", 1.0]],
	"inn":          [["market", 0.5]],
	"market":       [["inn", 0.5]],
}

## Predominant district per tier (0=hamlet … 4=metropolis).
const TIER_DISTRICT: Array[String] = [
	"agricultural",  # 0 hamlet
	"processing",    # 1 village
	"mixed",         # 2 town
	"mixed",         # 3 city
	"mixed",         # 4 metropolis
]

## Maximum building level allowed at each tier.
const TIER_MAX_LEVEL: Array[int] = [3, 5, 7, 9, 10]


## Terrain-utility gate: buildings in this table will be skipped by score_all
## unless the settlement's territory contains at least one of the listed tags.
## This prevents governors from queuing resource buildings in unsuitable terrain.
const TERRAIN_REQUIRED_TAGS: Dictionary = {
	"lumber_camp": ["timber", "wood"],
	"iron_mine":   ["iron_ore", "ore"],
}


## Returns true if any of `tags` appears in the resource_tags of any territory cell.
static func _territory_has_any_tag(ss: SettlementState, ws: WorldState, tags: Array) -> bool:
	for cid: String in ss.territory_cell_ids:
		var cell: Dictionary = ws.world_tiles.get(cid, {})
		var cell_tags: Array = cell.get("resource_tags", [])
		for t: String in tags:
			if (cell_tags as Array).has(t):
				return true
	# Fallback: mining_slots > 0 satisfies the ore gate even without explicit tags.
	if tags.has("iron_ore") or tags.has("ore"):
		if ss.acreage.get("mining_slots", 0) > 0:
			return true
	return false


## Returns building_id → float score for every candidate building.
## Candidates = all keys in ss.build_demand ∪ ss.building_levels.
## Buildings beyond the tier cap or failing a hard gate are skipped.
static func score_all(ss: SettlementState, ws: WorldState) -> Dictionary:
	var cr        := ContentRegistry
	var scores:   Dictionary = {}
	var tier_max: int = TIER_MAX_LEVEL[clampi(ss.tier, 0, 4)]

	# Collect all candidate building type IDs.
	var candidates: Array[String] = []
	for k: String in ss.build_demand.keys():
		if k not in candidates:
			candidates.append(k)
	for k: String in ss.building_levels.keys():
		if k not in candidates:
			candidates.append(k)

	for bld_id: String in candidates:
		var b_def = cr.get_content("building", bld_id)
		if b_def == null or b_def.is_empty():
			continue

		var current_level: int = ss.building_levels.get(bld_id, 0)
		var target_level:  int = current_level + 1

		# Skip if target level exceeds tier cap.
		if target_level > tier_max:
			continue

		# ── 0. Terrain-utility gate ───────────────────────────────────────────
		# Skip resource-extraction buildings when the territory has no matching
		# resource tags. Buildings already at level >= 1 are exempt (already built).
		if current_level == 0 and TERRAIN_REQUIRED_TAGS.has(bld_id):
			if not _territory_has_any_tag(ss, ws, TERRAIN_REQUIRED_TAGS[bld_id]):
				continue

		# ── 1. Build demand score ────────────────────────────────────────────
		var demand_score: float = float(ss.build_demand.get(bld_id, 0.0))

		# ── 2. District fit bonus ────────────────────────────────────────────
		var category: String = b_def.get("category", "")
		var district: String = TIER_DISTRICT[clampi(ss.tier, 0, 4)]
		var district_fit: float = 0.0
		match district:
			"agricultural":
				if category == "agricultural":
					district_fit = 2.0
			"processing":
				if category == "production":
					district_fit = 2.0
			"mixed":
				district_fit = 2.0

		# ── 3. Adjacency bonus ───────────────────────────────────────────────
		var adj_bonus: float = 0.0
		var adj_pairs: Array = ADJACENCY_TABLE.get(bld_id, [])
		for pair in adj_pairs:
			if ss.building_levels.get(pair[0], 0) >= 1:
				adj_bonus += float(pair[1])

		# ── 4. Cost penalty ──────────────────────────────────────────────────
		var base_cost:    float = float(b_def.get("base_cost", 100))
		var actual_cost:  float = base_cost * pow(float(target_level + 1), 2.2)
		var coin:         float = maxf(ss.inventory.get("coin", 0.0), 1.0)
		var cost_penalty: float = (actual_cost / coin) * 3.0

		# ── 5. Level penalty ─────────────────────────────────────────────────
		var level_penalty: float = float(current_level) * _ec().get("level_penalty_mult", 0.5)

		# ── 6. Hard gate block ───────────────────────────────────────────────
		var hard_gate_block: float = 0.0
		if b_def.has("hard_tier_min") and ss.tier < int(b_def["hard_tier_min"]):
			hard_gate_block = 99.0

		# ── 7. Personality bias ──────────────────────────────────────────────
		# Multiplies the positive (demand+fit+adjacency) component so punishing
		# penalties are unaffected — a greedy governor still can't afford things.
		var raw_positive := demand_score + district_fit + adj_bonus
		var personality_mult := 1.0
		match ss.governor_personality:
			"greedy":
				if bld_id in ["market", "market_stall", "iron_mine", "iron_smelter"]:
					personality_mult = 2.0
			"builder":
				if bld_id in ["inn", "well", "house", "granary"]:
					personality_mult = 1.5
			"cautious":
				if bld_id in ["granary", "farmstead", "farm_plot"]:
					personality_mult = 1.8
			# "balanced" → mult stays 1.0

		scores[bld_id] = raw_positive * personality_mult \
				- cost_penalty - level_penalty - hard_gate_block

	return scores


## Picks the top-scored candidate and — if it clears all checks — writes a
## construction job to ws.construction_jobs and deducts coin from ss.inventory.
static func maybe_queue_construction(ss: SettlementState, ws: WorldState, scores: Dictionary) -> void:
	if scores.is_empty():
		return

	# Sort descending by score.
	var sorted_ids: Array = scores.keys()
	sorted_ids.sort_custom(func(a: String, b: String) -> bool: return scores[a] > scores[b])

	var top_id:    String = sorted_ids[0]
	var top_score: float  = scores[top_id]

	if top_score < _ec().get("build_threshold", 6.0):
		return

	var cr    := ContentRegistry
	var b_def  = cr.get_content("building", top_id)
	if b_def == null or b_def.is_empty():
		return

	# Avoid double-queuing: skip if a job for this building already exists.
	for job_id: String in ws.construction_jobs.keys():
		var job: Dictionary = ws.construction_jobs[job_id]
		if job.get("settlement_id", "") == ss.settlement_id \
				and job.get("building_id", "") == top_id:
			return

	var current_level: int = ss.building_levels.get(top_id, 0)
	var target_level:  int = current_level + 1

	# 8-C: Hard tier cap — never queue a level that exceeds what this tier allows.
	var tier_max: int = TIER_MAX_LEVEL[clampi(ss.tier, 0, 4)]
	if current_level >= tier_max:
		return

	var base_cost:   float = float(b_def.get("base_cost", 100))
	var actual_cost: float = base_cost * pow(float(target_level + 1), 2.2)
	var coin:        float = ss.inventory.get("coin", 0.0)

	# Must afford it.
	if coin < actual_cost:
		return

	# Slot check: upgrades are free; new buildings need a free slot.
	var is_upgrade: bool = ss.building_levels.has(top_id)
	if not is_upgrade and ss.used_slots >= ss.max_slots:
		return

	# Commit: deduct coin, increment slot usage for new builds.
	ss.inventory["coin"] = maxf(0.0, coin - actual_cost)
	if not is_upgrade:
		ss.used_slots += 1

	# Write job.
	var base_labor:  float = float(b_def.get("base_labor", 100))
	var labor_total: float = base_labor * pow(float(target_level + 1), 2.2)
	var job_id: String = "%s:%s:%d" % [ss.settlement_id, top_id, target_level]

	ws.construction_jobs[job_id] = {
		"settlement_id":   ss.settlement_id,
		"building_id":     top_id,
		"target_level":    target_level,
		"labor_remaining": labor_total,
		"cost_paid":       actual_cost,
	}
