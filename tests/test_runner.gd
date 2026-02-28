## TestRunner — minimal synchronous test framework for headless simulation tests.
##
## Usage (command line):
##   godot --headless --script tests/test_runner.gd
##
## Or add as an autoload and call TestRunner.run_all() from a debug menu.
##
## A "suite" is a GDScript class (extending RefCounted) with methods whose
## names start with "test_". The runner discovers and calls them automatically.
extends SceneTree

## Test result record.
class TestResult:
	var suite_name: String = ""
	var test_name: String = ""
	var passed: bool = false
	var message: String = ""

## All registered suites (instances of test classes).
var _suites: Array = []
var _results: Array[TestResult] = []


func _init() -> void:
	# Give autoloads time to initialise.
	await process_frame
	_register_suites()
	run_all()
	print_summary()
	quit(0 if all_passed() else 1)


func _register_suites() -> void:
	# Register test classes here as they are added.
	_suites.append(preload("res://tests/test_data_loading.gd").new())
	_suites.append(preload("res://tests/test_entity_registry.gd").new())
	_suites.append(preload("res://tests/test_tick_ordering.gd").new())
	_suites.append(preload("res://tests/test_save_round_trip.gd").new())
	_suites.append(preload("res://tests/test_migration.gd").new())
	_suites.append(preload("res://tests/test_economy.gd").new())


func run_all() -> void:
	_results.clear()
	for suite in _suites:
		_run_suite(suite)


func _run_suite(suite: Object) -> void:
	var suite_name: String = suite.get_script().resource_path.get_file().trim_suffix(".gd")
	print("\n━━━ %s ━━━" % suite_name)

	# Call setup if it exists.
	if suite.has_method("setup"):
		suite.call("setup")

	var methods := suite.get_method_list()
	for method_info in methods:
		var method_name: String = method_info["name"]
		if not method_name.begins_with("test_"):
			continue

		# Call per-test setup/teardown if defined.
		if suite.has_method("before_each"):
			suite.call("before_each")

		var result := TestResult.new()
		result.suite_name = suite_name
		result.test_name = method_name

		# Intercept assert failures via a try-equivalent using a bool flag.
		# GDScript doesn't have try/catch, so tests use assert_that() helpers.
		suite._current_result = result
		suite.call(method_name)
		result.passed = result.message.is_empty()

		if suite.has_method("after_each"):
			suite.call("after_each")

		var icon := "✓" if result.passed else "✗"
		var detail := "" if result.passed else ("  → " + result.message)
		print("  %s %s%s" % [icon, method_name, detail])
		_results.append(result)

	if suite.has_method("teardown"):
		suite.call("teardown")


func print_summary() -> void:
	var passed := _results.filter(func(r): return r.passed).size()
	var total := _results.size()
	print("\n━━━ Results: %d/%d passed ━━━" % [passed, total])
	if passed < total:
		print("Failed tests:")
		for r in _results:
			if not r.passed:
				print("  [%s] %s: %s" % [r.suite_name, r.test_name, r.message])


func all_passed() -> bool:
	return _results.all(func(r): return r.passed)
