## SimulationClock — deterministic tick counter with configurable real-time rate.
##
## The clock drives the entire simulation. Every tick emits tick_completed with
## the absolute tick number. All simulation logic is tied to tick number, NOT
## to wall-clock time, so that saves/loads replay identically given the same seed.
##
## Strategic cadence (world_pulse, production_pulse) runs every
## STRATEGIC_TICK_INTERVAL tactical ticks.
extends Node

signal tick_completed(tick: int)
signal clock_paused()
signal clock_resumed()
signal speed_changed(multiplier: float)

## How many simulation ticks per real second at 1× speed.
const BASE_TICKS_PER_SECOND: float = 1.0

## Available speed multiplier presets.
const SPEED_PRESETS: Array[float] = [0.25, 0.5, 1.0, 2.0, 4.0, 8.0, 16.0]

var _tick_count: int = 0
var _paused: bool = true          # Start paused; Bootstrap resumes when ready.
var _speed_multiplier: float = 1.0
var _accumulator: float = 0.0


func _process(delta: float) -> void:
	if _paused:
		return
	_accumulator += delta * _speed_multiplier * BASE_TICKS_PER_SECOND
	while _accumulator >= 1.0:
		_accumulator -= 1.0
		_advance_tick()


func _advance_tick() -> void:
	_tick_count += 1
	tick_completed.emit(_tick_count)


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------

func pause() -> void:
	if not _paused:
		_paused = true
		clock_paused.emit()


func resume() -> void:
	if _paused:
		_paused = false
		clock_resumed.emit()


func toggle_pause() -> void:
	if _paused:
		resume()
	else:
		pause()


func set_speed(multiplier: float) -> void:
	_speed_multiplier = clampf(multiplier, 0.0, 16.0)
	speed_changed.emit(_speed_multiplier)


func get_tick() -> int:
	return _tick_count


func is_paused() -> bool:
	return _paused


func get_speed() -> float:
	return _speed_multiplier


## Force the tick counter to a specific value (used when restoring from save).
func set_tick(tick: int) -> void:
	_tick_count = tick


## Reset the clock to tick 0 and clear the accumulator.
## Call this before starting a fresh simulation (e.g. after world generation).
func reset() -> void:
	_tick_count  = 0
	_accumulator = 0.0


# ---------------------------------------------------------------------------
# Serialisation
# ---------------------------------------------------------------------------

func to_dict() -> Dictionary:
	return {
		"tick_count": _tick_count,
		"speed_multiplier": _speed_multiplier,
	}


func from_dict(data: Dictionary) -> void:
	_tick_count = data.get("tick_count", 0)
	_speed_multiplier = data.get("speed_multiplier", 1.0)
