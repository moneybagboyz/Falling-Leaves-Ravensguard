class_name WorldGenPhase
extends RefCounted

## Base class for world generation phases
## Each phase is responsible for one aspect of world generation
## and can yield control for async execution with progress updates

signal step_completed(stage_name: String)

## Execute this phase of world generation
## @param context: WorldGenContext containing shared state
## @return bool: true if phase completed successfully
func execute(context: WorldGenContext) -> bool:
	push_error("WorldGenPhase.execute() must be overridden")
	return false

## Get the name of this phase for progress tracking
func get_phase_name() -> String:
	return "Unknown Phase"

## Clean up any temporary data this phase created
## Called by pipeline after phase completes to free memory
func cleanup(context: WorldGenContext) -> void:
	pass
