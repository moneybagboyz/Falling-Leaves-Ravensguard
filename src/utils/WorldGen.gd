class_name WorldGen
extends RefCounted

## World Generation Facade
## Uses the phase-based pipeline architecture for modular, memory-efficient generation

const WorldGenPipeline = preload("res://src/utils/worldgen/WorldGenPipeline.gd")
const TerrainColors = preload("res://src/ui/core/TerrainColors.gd")
const UIFormatting = preload("res://src/ui/core/UIFormatting.gd")

signal step_completed(stage_name)

func generate(w: int, h: int, rng: RandomNumberGenerator, live_grid: Array = [], config: Dictionary = {}) -> Dictionary:
	## Main generation entry point - uses pipeline architecture
	## Returns dictionary with world data compatible with GameState
	
	# Clear rendering caches to prevent ghosting or stripe artifacts from previous runs
	TerrainColors.clear_cache()
	UIFormatting.clear_cache()
	
	# Create and run pipeline
	var pipeline = WorldGenPipeline.new()
	pipeline.step_completed.connect(_on_pipeline_step)
	
	var result = await pipeline.generate(w, h, rng, live_grid, config)
	
	# Add ruins to result (stored in resources dict by ArmyPhase)
	if result.has("resources") and result.resources.has("_ruins"):
		result["ruins"] = result.resources["_ruins"]
		result.resources.erase("_ruins")
	
	# Set start position
	result["start_pos"] = _find_start_position(result, w, h)
	
	return result

func _on_pipeline_step(stage_name: String) -> void:
	step_completed.emit(stage_name)

func _find_start_position(result: Dictionary, w: int, h: int) -> Vector2i:
	# Try faction capitals first
	if result.has("settlements"):
		for s_pos in result.settlements:
			var s = result.settlements[s_pos]
			if s.is_capital:
				return s_pos
		
		# Fallback to any settlement
		if not result.settlements.is_empty():
			return result.settlements.keys()[0]
	
	# Ultimate fallback: world center
	return Vector2i(w / 2, h / 2)
