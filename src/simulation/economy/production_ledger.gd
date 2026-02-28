## ProductionLedger — computes and credits production for one settlement per pulse.
##
## Three production pathways:
##   1. acreage  — farm_grain and similar year-round tile-based yields.
##   2. extraction — log_timber and similar resource-tag-gated yields
##                   using woodlot acres as a proxy capacity.
##   3. standard — building-resident recipes (deducts inputs, credits outputs).
##                 Inactive in Phase 2 since no buildings are placed yet.
##
## All mutations are applied directly to the SettlementState passed in.
## No global side effects; safe to call from tests.
class_name ProductionLedger
extends RefCounted

# ── Recipe IDs handled automatically by each pathway ─────────────────────────
const AGRI_RECIPE_ID:   String = "farm_grain"
const TIMBER_RECIPE_ID: String = "log_timber"

# Fraction of woodlot_acres that functions as "effective labour" per tick.
# Keeps timber output modest in Phase 2 without a real labour model.
const WOODLOT_LABOUR_FACTOR: float = 0.002


## Main entry point. delta_ticks is the number of ticks since the last pulse
## (normally TickScheduler.STRATEGIC_CADENCE).
static func run(ss: SettlementState, ws: WorldState, delta_ticks: int) -> void:
	_run_agriculture(ss, ws, delta_ticks)
	_run_extraction(ss, ws, delta_ticks)
	_run_standard_recipes(ss, ws, delta_ticks)


# ── Agriculture ───────────────────────────────────────────────────────────────
static func _run_agriculture(ss: SettlementState, ws: WorldState, delta_ticks: int) -> void:
	var cr := ContentRegistry
	var recipe = cr.get_content("recipe", AGRI_RECIPE_ID)
	if recipe == null or recipe.is_empty():
		return

	var worked_acres: float = float(ss.acreage.get("worked_acres", 0))
	if worked_acres <= 0.0:
		return

	var base_yield:    float = float(recipe.get("base_yield_per_acre_per_tick", 0.11))
	var fertility:     float = _cell_fertility(ss.cell_id, ws)
	var labour_factor: float = _labour_factor(ss)
	var output:        float = worked_acres * base_yield * fertility * labour_factor * float(delta_ticks)
	var good:          String = recipe.get("output_good", "wheat_bushel")

	ss.inventory[good] = ss.inventory.get(good, 0.0) + output
	_log(ss, AGRI_RECIPE_ID, output)


# ── Labour factor ────────────────────────────────────────────────────────────
## Scales production by population relative to the tier reference size.
## Reference values are calibrated to match the populations actually generated
## by the worldgen pipeline so that all tiers start near factor 1.0.
## A half-populated settlement produces ~0.5× output; over-populated gives up to 2×.
static func _labour_factor(ss: SettlementState) -> float:
	# Reference populations calibrated to TIER_POP_MIN/MAX midpoints in SettlementPlacer.
	const TIER_REF_POP: Array[int] = [75, 300, 900, 2800, 8000]
	var total_pop: int = ss.total_population()
	if total_pop <= 0:
		return 0.1
	var tier: int = clampi(ss.tier, 0, TIER_REF_POP.size() - 1)
	var ref_pop: int = TIER_REF_POP[tier]
	return clampf(float(total_pop) / float(ref_pop), 0.1, 2.0)

## Returns the terrain fertility stored on the cell this settlement occupies.
## Falls back to 0.5 if the cell is not found in world_tiles.
## The cell dict uses "prosperity" as its fertility proxy (set by BiomeClassifier).
static func _cell_fertility(cell_id: String, ws: WorldState) -> float:
	var cell: Dictionary = ws.world_tiles.get(cell_id, {})
	return clampf(float(cell.get("prosperity", 0.5)), 0.05, 1.0)


# ── Extraction ────────────────────────────────────────────────────────────────
static func _run_extraction(ss: SettlementState, ws: WorldState, delta_ticks: int) -> void:
	var cr := ContentRegistry
	var recipe = cr.get_content("recipe", TIMBER_RECIPE_ID)
	if recipe == null or recipe.is_empty():
		return

	var woodlot_acres: float = float(ss.acreage.get("woodlot_acres", 0))
	if woodlot_acres <= 0.0:
		return

	var required_tag: String = recipe.get("required_resource_tag", "wood")
	if not _cell_has_tag(ss.cell_id, ws, required_tag):
		return

	var yield_per_wd: float = float(recipe.get("yield_per_worker_day", 0.5))
	# woodlot_acres × factor ≈ effective worker-days available for logging.
	var labour: float = woodlot_acres * WOODLOT_LABOUR_FACTOR
	var output: float = labour * yield_per_wd * float(delta_ticks)

	var good: String = recipe.get("output_good", "timber_log")
	ss.inventory[good] = ss.inventory.get(good, 0.0) + output
	_log(ss, TIMBER_RECIPE_ID, output)


# ── Standard recipes (buildings) ─────────────────────────────────────────────
static func _run_standard_recipes(ss: SettlementState, _ws: WorldState, delta_ticks: int) -> void:
	if ss.buildings.is_empty():
		return   # No buildings in Phase 2 — skip without error.
	var cr := ContentRegistry

	for bld_id: String in ss.buildings:
		var bld = cr.get_content("building", bld_id)
		if bld == null or bld.is_empty() or not bld.has("production"):
			continue
		var prod:      Dictionary = bld["production"]
		var recipe_id: String     = prod.get("recipe", "")
		if recipe_id.is_empty():
			continue
		var recipe = cr.get_content("recipe", recipe_id)
		if recipe == null or recipe.is_empty() or recipe.get("recipe_type", "") != "standard":
			continue
		_run_standard_batch(ss, recipe, recipe_id, delta_ticks)


static func _run_standard_batch(
		ss:        SettlementState,
		recipe:    Dictionary,
		recipe_id: String,
		delta_ticks: int
) -> void:
	var wpb: float     = maxf(float(recipe.get("worker_days_per_batch", 1.0)), 0.001)
	var batches: float = float(delta_ticks) / wpb
	var inputs:  Dictionary = recipe.get("inputs",  {})
	var outputs: Dictionary = recipe.get("outputs", {})

	# Verify all inputs present.
	for good in inputs:
		if ss.inventory.get(good, 0.0) < float(inputs[good]) * batches:
			_log(ss, recipe_id, 0.0, "shortage: %s" % good)
			return

	# Deduct inputs, credit outputs.
	for good in inputs:
		ss.inventory[good] = maxf(ss.inventory.get(good, 0.0) - float(inputs[good]) * batches, 0.0)
	for good in outputs:
		ss.inventory[good] = ss.inventory.get(good, 0.0) + float(outputs[good]) * batches
	_log(ss, recipe_id, batches)


# ── Helpers ───────────────────────────────────────────────────────────────────
static func _cell_has_tag(cell_id: String, ws: WorldState, tag: String) -> bool:
	var cell: Dictionary = ws.world_tiles.get(cell_id, {})
	var tags = cell.get("resource_tags", [])
	return (tags as Array).has(tag)


static func _log(ss: SettlementState, recipe_id: String, amount: float, note: String = "") -> void:
	ss.production_log.append({
		"recipe": recipe_id,
		"amount": amount,
		"note":   note,
	})
	while ss.production_log.size() > SettlementState.PRODUCTION_LOG_MAX:
		ss.production_log.pop_front()
