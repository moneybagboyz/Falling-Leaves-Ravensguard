## ConstructionJob — data class for one player-initiated building job.
##
## Created by WorkshopManager when the build path is confirmed.
## Stored in WorldState.construction_jobs (job_id -> to_dict()).
## Decremented by ConstructionSystem each PRODUCTION_PULSE tick.
class_name ConstructionJob
extends RefCounted

var job_id:     String = ""

## The building type to create on completion (must exist in data/buildings/).
var building_id: String = ""

## World tile cell key where the building will be placed ("wt_x,wt_y").
var cell_id:    String = ""

## The PersonState ID of the character who commissioned the build.
var owner_id:   String = ""

## Settlement ID where this building will be registered.
var settlement_id: String = ""

## Remaining ticks until completion. Set to building.construction_cost.labor_days
## on creation; decremented each production_pulse tick.
var ticks_remaining: float = 0.0

## Resources committed at job start (good_id -> float).
## Already deducted from owner's inventory or camp stock.
var resources_committed: Dictionary = {}

## Whether labor resources have been deducted (only deducted once).
var started: bool = false


func to_dict() -> Dictionary:
	return {
		"job_id":              job_id,
		"building_id":         building_id,
		"cell_id":             cell_id,
		"owner_id":            owner_id,
		"settlement_id":       settlement_id,
		"ticks_remaining":     ticks_remaining,
		"resources_committed": resources_committed.duplicate(),
		"started":             started,
	}


static func from_dict(d: Dictionary) -> ConstructionJob:
	var j := ConstructionJob.new()
	j.job_id              = d.get("job_id",              "")
	j.building_id         = d.get("building_id",         "")
	j.cell_id             = d.get("cell_id",             "")
	j.owner_id            = d.get("owner_id",            "")
	j.settlement_id       = d.get("settlement_id",       "")
	j.ticks_remaining     = float(d.get("ticks_remaining", 0.0))
	j.resources_committed = d.get("resources_committed", {}).duplicate()
	j.started             = bool(d.get("started",         false))
	return j
