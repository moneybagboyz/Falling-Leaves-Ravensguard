## ConstructionSystem — advances active construction jobs each PRODUCTION_PULSE.
##
## Registered as a tick hook by Bootstrap on Phase.PRODUCTION_PULSE.
## On completion: stamps building_id onto world tile, creates labor slots
## on the owning settlement, and registers ownership in PropertyCore.
class_name ConstructionSystem
extends RefCounted

var _world_state: WorldState = null


func setup(ws: WorldState) -> void:
	_world_state = ws


func tick_construction(tick: int) -> void:
	var ws: WorldState = _world_state
	if ws == null:
		return

	var completed: Array[String] = []

	for job_id: String in ws.construction_jobs.keys():
		var jdata: Dictionary = ws.construction_jobs[job_id]
		var job: ConstructionJob = ConstructionJob.from_dict(jdata)

		job.ticks_remaining -= float(TickScheduler.STRATEGIC_CADENCE)

		if job.ticks_remaining <= 0.0:
			_complete_job(job, ws, tick)
			completed.append(job_id)
		else:
			ws.construction_jobs[job_id] = job.to_dict()

	for jid: String in completed:
		ws.construction_jobs.erase(jid)


func _complete_job(job: ConstructionJob, ws: WorldState, _tick: int) -> void:
	# ── Stamp building onto world tile ───────────────────────────────────────
	var tile: Dictionary = ws.world_tiles.get(job.cell_id, {})
	if tile.is_empty():
		push_warning("ConstructionSystem: cell '%s' not found in world_tiles." % job.cell_id)
		return
	tile["building_id"] = job.building_id
	ws.world_tiles[job.cell_id] = tile

	# Invalidate the cached sub-region grid for this world tile so
	# SubRegionGenerator re-generates it with the new building.
	var parts: PackedStringArray = job.cell_id.split(",")
	if parts.size() == 2:
		ws.region_grids.erase(job.cell_id)

	# ── Add labor slots to settlement ─────────────────────────────────────────
	var ss: SettlementState = ws.get_settlement(job.settlement_id)
	if ss != null:
		var bdef: Dictionary = ContentRegistry.get_content("building", job.building_id)
		var slot_defs: Array = bdef.get("labor_slots", [])
		for sdef: Dictionary in slot_defs:
			for _i: int in range(int(sdef.get("count", 1))):
				ss.labor_slots.append({
					"slot_id":     sdef.get("slot_id", job.building_id),
					"building_id": job.building_id,
					"cell_id":     job.cell_id,
					"role_id":     sdef.get("slot_id", job.building_id),
					"wage_per_day": float(sdef.get("wage_per_day", 1.0)),
					"is_filled":   false,
					"worker_id":   "",
				})
		if job.cell_id not in ss.territory_cell_ids:
			ss.territory_cell_ids.append(job.cell_id)

	# ── Register ownership ────────────────────────────────────────────────────
	var instance_key: String = job.building_id + ":" + job.cell_id
	PropertyCore.register_ownership(ws, instance_key, job.owner_id)

	# Also update owner's ownership_refs.
	var owner: PersonState = ws.characters.get(job.owner_id)
	if owner != null and instance_key not in owner.ownership_refs:
		owner.ownership_refs.append(instance_key)

	print("[ConstructionSystem] '%s' completed at %s for owner '%s'."
		% [job.building_id, job.cell_id, job.owner_id])
