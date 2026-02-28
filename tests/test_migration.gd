## Tests for MigrationRunner (P0-09).
extends BaseTest

var _runner: Node


func setup() -> void:
	_runner = load("res://src/core/save/migration_runner.gd").new()


func teardown() -> void:
	_runner.free()


func test_same_version_returns_data_unchanged() -> void:
	var data := {"schema_version": "1.0.0", "value": 42}
	var result: Dictionary = _runner.migrate(data, "1.0.0", "1.0.0")
	assert_eq(result["value"], 42)


func test_single_step_migration_applied() -> void:
	_runner.register_migration("0.1.0", "0.2.0", func(d: Dictionary) -> Dictionary:
		var out := d.duplicate(true)
		out["migrated"] = true
		out["schema_version"] = "0.2.0"
		return out
	)
	var data := {"schema_version": "0.1.0", "x": 10}
	var result: Dictionary = _runner.migrate(data, "0.1.0", "0.2.0")
	assert_true(result.get("migrated", false), "Migration function must have run")
	assert_eq(result["schema_version"], "0.2.0")


func test_two_step_migration_chain() -> void:
	_runner.register_migration("1.0.0", "1.1.0", func(d: Dictionary) -> Dictionary:
		var out := d.duplicate(true)
		out["step1"] = true
		return out
	)
	_runner.register_migration("1.1.0", "1.2.0", func(d: Dictionary) -> Dictionary:
		var out := d.duplicate(true)
		out["step2"] = true
		return out
	)
	var data := {"schema_version": "1.0.0"}
	var result: Dictionary = _runner.migrate(data, "1.0.0", "1.2.0")
	assert_true(result.get("step1", false), "Step 1 must have run")
	assert_true(result.get("step2", false), "Step 2 must have run")
	assert_eq(result["schema_version"], "1.2.0")


func test_result_has_target_schema_version() -> void:
	_runner.register_migration("2.0.0", "2.1.0", func(d: Dictionary) -> Dictionary:
		return d.duplicate(true)
	)
	var result: Dictionary = _runner.migrate({"schema_version": "2.0.0"}, "2.0.0", "2.1.0")
	assert_eq(result["schema_version"], "2.1.0")


func test_has_migration_returns_true_when_registered() -> void:
	_runner.register_migration("3.0.0", "3.1.0", func(d): return d)
	assert_true(_runner.has_migration("3.0.0"))


func test_has_migration_returns_false_when_not_registered() -> void:
	assert_false(_runner.has_migration("99.0.0"))


func test_duplicate_migration_not_registered_twice() -> void:
	var call_count: Array[int] = [0]
	_runner.register_migration("4.0.0", "4.1.0", func(d: Dictionary) -> Dictionary:
		call_count[0] += 1
		return d.duplicate(true)
	)
	# Attempt to register the same from→to again — should be ignored.
	_runner.register_migration("4.0.0", "4.1.0", func(d: Dictionary) -> Dictionary:
		call_count[0] += 100  # Should never run.
		return d.duplicate(true)
	)
	_runner.migrate({"schema_version": "4.0.0"}, "4.0.0", "4.1.0")
	assert_eq(call_count[0], 1, "Migration function must only be registered once")


func test_migration_preserves_unrelated_fields() -> void:
	_runner.register_migration("5.0.0", "5.1.0", func(d: Dictionary) -> Dictionary:
		var out := d.duplicate(true)
		out["new_field"] = "added"
		return out
	)
	var data := {"schema_version": "5.0.0", "world_state": {"region_id": "r1"}}
	var result: Dictionary = _runner.migrate(data, "5.0.0", "5.1.0")
	assert_has(result, "world_state")
	assert_eq(result["world_state"]["region_id"], "r1")


func test_get_registered_migrations_count() -> void:
	_runner.register_migration("6.0.0", "6.1.0", func(d): return d)
	_runner.register_migration("6.1.0", "6.2.0", func(d): return d)
	assert_eq(_runner.get_registered_migrations().size(), 2)
