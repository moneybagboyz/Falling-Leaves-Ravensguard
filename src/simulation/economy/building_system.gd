## BuildingSystem — manages building level progression and milestone lookups.
##
## All methods are static; no instance state required.
## Used by GovernorAI (scoring), ProductionLedger (output multipliers),
## and SettlementPulse (construction advancement via complete_construction).
class_name BuildingSystem
extends RefCounted


## Returns the active milestone dict for a building at the given level.
## The active milestone is the highest milestone key whose integer value ≤ level.
## Returns {} if no milestone qualifies (building inactive or not yet levelled).
static func get_active_milestone(b_def: Dictionary, level: int) -> Dictionary:
	var ms_obj: Dictionary = b_def.get("milestones", {})
	if ms_obj.is_empty() or level <= 0:
		return {}
	var keys: Array = ms_obj.keys()
	keys.sort_custom(func(a, b) -> bool: return int(a) > int(b))
	for k in keys:
		if int(k) <= level:
			return ms_obj[k]
	return {}


## Returns the construction cost (coin) to reach target_level.
## Formula from §8.4: base_cost × (target_level + 1)^2.2
static func compute_cost(b_def: Dictionary, target_level: int) -> float:
	var base: float = float(b_def.get("base_cost", 100))
	return base * pow(float(target_level + 1), 2.2)


## Returns the labour required (worker-days) to reach target_level.
## Same power-law shape as cost, using base_labor instead.
static func compute_labor(b_def: Dictionary, target_level: int) -> float:
	var base: float = float(b_def.get("base_labor", 100))
	return base * pow(float(target_level + 1), 2.2)


## Finalises a construction job: promotes the building level in ss.building_levels,
## updates ss.used_slots, and appends a completion entry to ss.production_log.
## `job` is the ConstructionJob dict from ws.construction_jobs.
static func complete_construction(ws: WorldState, job: Dictionary) -> void:
	var sid:          String = job.get("settlement_id", "")
	var bld_id:       String = job.get("building_id", "")
	var target_level: int    = int(job.get("target_level", 1))

	if sid.is_empty() or bld_id.is_empty():
		return

	var ss: SettlementState = ws.get_settlement(sid)
	if ss == null:
		return

	var was_new: bool = not ss.building_levels.has(bld_id)

	# Promote level — never lower it if something already upgraded it.
	var current: int = ss.building_levels.get(bld_id, 0)
	ss.building_levels[bld_id] = maxi(current, target_level)

	# Recalculate used_slots from the count of buildings at level ≥ 1.
	var count: int = 0
	for k: String in ss.building_levels.keys():
		if ss.building_levels[k] >= 1:
			count += 1
	ss.used_slots = count

	# Production log entry.
	var note: String = "%s reached level %d" % [bld_id, target_level]
	if was_new:
		note = "Built new %s (level %d)" % [bld_id, target_level]
	ss.production_log.append({
		"tick":          ws.current_tick,
		"recipe_id":     "construction",
		"output_amount": float(target_level),
		"output_good":   bld_id,
		"note":          note,
	})
	while ss.production_log.size() > SettlementState.PRODUCTION_LOG_MAX:
		ss.production_log.pop_front()
