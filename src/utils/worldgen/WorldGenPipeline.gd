class_name WorldGenPipeline
extends RefCounted

## Orchestrates the world generation pipeline
## Runs phases in sequence and manages context/memory

signal step_completed(stage_name: String)

var phases: Array[WorldGenPhase] = []

func _init():
	# Define the generation pipeline order
	phases = [
		TectonicsPhase.new(),
		ClimatePhase.new(),
		HydrologyPhase.new(),
		BiomePhase.new(),
		SettlementPhase.new(),
		ProvincePhase.new(),
		FactionPhase.new(),
		RoadPhase.new(),
		EconomyPhase.new(),
		ArmyPhase.new(),
		PopulationPhase.new()
	]
	
	# Connect phase signals to pipeline signal
	for phase in phases:
		phase.step_completed.connect(_on_phase_step)

func _on_phase_step(stage_name: String) -> void:
	step_completed.emit(stage_name)

## Run the complete generation pipeline
## @param w: World width
## @param h: World height
## @param rng: Random number generator
## @param live_grid: Grid array to update live
## @param config: Configuration dictionary
## @return Dictionary with generated world data
func generate(w: int, h: int, rng: RandomNumberGenerator, live_grid: Array = [], config: Dictionary = {}) -> Dictionary:
	# Validate inputs
	if w <= 0 or h <= 0:
		push_error("WorldGenPipeline: Invalid world dimensions (%d x %d)" % [w, h])
		return {}
	
	if not rng:
		push_error("WorldGenPipeline: No RNG provided")
		return {}
	
	if w < 50 or h < 50:
		push_warning("WorldGenPipeline: Small world size (%d x %d) may cause generation issues" % [w, h])
	
	# Create shared context
	var context = WorldGenContext.new(w, h, rng, live_grid, config)
	
	# Execute each phase in sequence
	for phase in phases:
		step_completed.emit("Starting %s phase..." % phase.get_phase_name())
		
		var success = await phase.execute(context)
		
		if not success:
			push_error("WorldGenPipeline: Phase '%s' failed" % phase.get_phase_name())
			return {}
		
		# Clean up memory after each phase
		phase.cleanup(context)
		
		step_completed.emit("%s phase complete" % phase.get_phase_name())
	
	# Return final output
	return context.to_output_dict()

## Get a list of phase names for progress tracking
func get_phase_names() -> Array[String]:
	var names: Array[String] = []
	for phase in phases:
		names.append(phase.get_phase_name())
	return names

## Get total number of phases
func get_phase_count() -> int:
	return phases.size()
