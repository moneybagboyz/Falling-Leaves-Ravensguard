## TickScheduler — executes simulation phases in the mandated order each tick.
##
## Phase order is FIXED. Do not reorder without updating development-plan.md.
##
## Strategic phases (WORLD_PULSE, PRODUCTION_PULSE) run every
## STRATEGIC_CADENCE ticks; all other phases run every tick.
##
## Systems register Callables as hooks for one or more phases.
## Hook signature: func my_hook(tick: int) -> void
extends Node

## The eight simulation phases in mandatory execution order.
enum Phase {
	COLLECT_INPUT_AND_ORDERS          = 0,
	WORLD_PULSE                       = 1,  # strategic cadence
	PRODUCTION_PULSE                  = 2,  # strategic cadence
	MOVEMENT                          = 3,
	HAZARD_RESOLUTION                 = 4,
	COMBAT_RESOLUTION                 = 5,
	PERSISTENCE_COLLAPSE_REHYDRATION  = 6,
	PRESENTATION_SYNC                 = 7,
}

const PHASE_NAMES: Array[String] = [
	"collect_input_and_orders",
	"world_pulse",
	"production_pulse",
	"movement",
	"hazard_resolution",
	"combat_resolution",
	"persistence_collapse_rehydration",
	"presentation_sync",
]

## Strategic phases only fire every N ticks.
## 1 = fire every tick (i.e. every game day).
const STRATEGIC_CADENCE: int = 1

## Phases that run on the strategic (slow) cadence.
const STRATEGIC_PHASES: Array[int] = [Phase.WORLD_PULSE, Phase.PRODUCTION_PULSE]

signal phase_started(phase: int, tick: int)
signal phase_completed(phase: int, tick: int)
signal tick_phases_complete(tick: int)

# _hooks[Phase] = Array[Callable]
var _hooks: Dictionary = {}

# Performance tracking: phase -> total accumulated ms
var _phase_timings: Dictionary = {}


func _ready() -> void:
	for phase in Phase.values():
		_hooks[phase] = []
		_phase_timings[phase] = 0.0
	SimulationClock.tick_completed.connect(_on_tick_completed)


# ---------------------------------------------------------------------------
# Hook registration
# ---------------------------------------------------------------------------

func register_hook(phase: Phase, callable: Callable) -> void:
	_hooks[phase].append(callable)


func unregister_hook(phase: Phase, callable: Callable) -> void:
	var arr: Array = _hooks[phase]
	var idx := arr.find(callable)
	if idx >= 0:
		arr.remove_at(idx)


# ---------------------------------------------------------------------------
# Tick execution
# ---------------------------------------------------------------------------

func _on_tick_completed(tick: int) -> void:
	_run_tick(tick)


func _run_tick(tick: int) -> void:
	var is_strategic := (tick % STRATEGIC_CADENCE == 0)

	for phase in Phase.values():
		if phase in STRATEGIC_PHASES and not is_strategic:
			continue
		_run_phase(phase as Phase, tick)

	EventQueue.flush()
	tick_phases_complete.emit(tick)


func _run_phase(phase: Phase, tick: int) -> void:
	phase_started.emit(phase, tick)
	var t_start := Time.get_ticks_usec()

	var hooks: Array = _hooks[phase]
	for hook: Callable in hooks:
		hook.call(tick)

	_phase_timings[phase] += float(Time.get_ticks_usec() - t_start) / 1000.0
	phase_completed.emit(phase, tick)


## Returns accumulated CPU ms per phase name since startup.
func get_timings() -> Dictionary:
	var result := {}
	for phase in Phase.values():
		result[PHASE_NAMES[phase]] = _phase_timings[phase]
	return result


func reset_timings() -> void:
	for phase in Phase.values():
		_phase_timings[phase] = 0.0


func phase_name(phase: Phase) -> String:
	return PHASE_NAMES[phase]
