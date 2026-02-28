## Tests for SimulationClock and TickScheduler phase ordering (P0-05, P0-06).
extends BaseTest

# ---------------------------------------------------------------------------
# SimulationClock tests
# ---------------------------------------------------------------------------

func test_clock_starts_at_zero() -> void:
	var clock: Node = load("res://src/core/time/simulation_clock.gd").new()
	assert_eq(clock.get_tick(), 0)
	clock.free()


func test_clock_starts_paused() -> void:
	var clock: Node = load("res://src/core/time/simulation_clock.gd").new()
	assert_true(clock.is_paused(), "Clock must start paused")
	clock.free()


func test_set_and_get_tick() -> void:
	var clock: Node = load("res://src/core/time/simulation_clock.gd").new()
	clock.set_tick(42)
	assert_eq(clock.get_tick(), 42)
	clock.free()


func test_pause_resume_toggle() -> void:
	var clock: Node = load("res://src/core/time/simulation_clock.gd").new()
	clock.resume()
	assert_false(clock.is_paused())
	clock.pause()
	assert_true(clock.is_paused())
	clock.free()


func test_set_speed_clamps() -> void:
	var clock: Node = load("res://src/core/time/simulation_clock.gd").new()
	clock.set_speed(-1.0)
	assert_eq(clock.get_speed(), 0.0)
	clock.set_speed(100.0)
	assert_eq(clock.get_speed(), 16.0)
	clock.free()


func test_clock_serialisation_round_trip() -> void:
	var clock: Node = load("res://src/core/time/simulation_clock.gd").new()
	clock.set_tick(99)
	clock.set_speed(4.0)
	var d: Dictionary = clock.to_dict()
	assert_eq(d["tick_count"], 99)
	assert_eq(d["speed_multiplier"], 4.0)

	var clock2: Node = load("res://src/core/time/simulation_clock.gd").new()
	clock2.from_dict(d)
	assert_eq(clock2.get_tick(), 99)
	assert_eq(clock2.get_speed(), 4.0)
	clock.free()
	clock2.free()


# ---------------------------------------------------------------------------
# Tick phase ordering tests
# ---------------------------------------------------------------------------

func test_phase_enum_has_eight_values() -> void:
	# Load TickScheduler script and verify the Phase enum.
	var script: GDScript = load("res://src/core/time/tick_scheduler.gd")
	# Access enum constants via a temp instance.
	var instance: Node = script.new()
	# The enum should expose 8 values (0-7).
	var phase_names: Array = instance.PHASE_NAMES
	assert_eq(phase_names.size(), 8, "Must be exactly 8 tick phases")
	instance.free()


func test_phase_names_match_spec() -> void:
	var instance: Node = load("res://src/core/time/tick_scheduler.gd").new()
	var names: Array = instance.PHASE_NAMES
	assert_eq(names[0], "collect_input_and_orders")
	assert_eq(names[1], "world_pulse")
	assert_eq(names[2], "production_pulse")
	assert_eq(names[3], "movement")
	assert_eq(names[4], "hazard_resolution")
	assert_eq(names[5], "combat_resolution")
	assert_eq(names[6], "persistence_collapse_rehydration")
	assert_eq(names[7], "presentation_sync")
	instance.free()


func test_hooks_run_in_registration_order() -> void:
	## Verify that two hooks on the same phase fire in registration order.
	## We wire a minimal clock + scheduler without using the autoloads.
	var order: Array[int] = []

	# We can't easily detach the scheduler from the autoload SimulationClock
	# in a unit test context, so we drive it manually via _run_tick.
	var scheduler_script: GDScript = load("res://src/core/time/tick_scheduler.gd")
	var scheduler: Node = scheduler_script.new()
	# Zero-out hooks (bypass _ready which would connect to the autoload clock).
	for phase in range(8):
		scheduler._hooks[phase] = []

	scheduler.register_hook(
		scheduler_script.Phase.MOVEMENT,
		func(_t): order.append(1)
	)
	scheduler.register_hook(
		scheduler_script.Phase.MOVEMENT,
		func(_t): order.append(2)
	)
	# Strategic phases need a strategic tick (tick % 10 == 0).
	scheduler._run_tick(10)
	assert_eq(order, [1, 2], "Hooks must fire in registration order")
	scheduler.free()


func test_strategic_phases_skip_on_non_strategic_tick() -> void:
	var called_world_pulse: Array[bool] = [false]
	var called_movement: Array[bool]    = [false]

	var scheduler_script: GDScript = load("res://src/core/time/tick_scheduler.gd")
	var scheduler: Node = scheduler_script.new()
	for phase in range(8):
		scheduler._hooks[phase] = []

	scheduler.register_hook(
		scheduler_script.Phase.WORLD_PULSE,
		func(_t): called_world_pulse[0] = true
	)
	scheduler.register_hook(
		scheduler_script.Phase.MOVEMENT,
		func(_t): called_movement[0] = true
	)

	# Tick 1 is NOT a strategic tick (1 % 10 != 0).
	scheduler._run_tick(1)
	assert_false(called_world_pulse[0], "world_pulse must not fire on non-strategic tick")
	assert_true(called_movement[0], "movement must fire every tick")
	scheduler.free()


func test_strategic_phases_fire_on_strategic_tick() -> void:
	var called_world_pulse: Array[bool] = [false]

	var scheduler_script: GDScript = load("res://src/core/time/tick_scheduler.gd")
	var scheduler: Node = scheduler_script.new()
	for phase in range(8):
		scheduler._hooks[phase] = []

	scheduler.register_hook(
		scheduler_script.Phase.WORLD_PULSE,
		func(_t): called_world_pulse[0] = true
	)

	# Tick 10 IS a strategic tick (10 % 10 == 0).
	scheduler._run_tick(10)
	assert_true(called_world_pulse[0], "world_pulse must fire on strategic tick")
	scheduler.free()
