## Tests for DataLoader and ContentRegistry (P0-02, P0-03, P0-12).
extends BaseTest

var _registry: Node


func setup() -> void:
	# Use an isolated ContentRegistry instance so we don't pollute the global one.
	_registry = load("res://src/core/registry/content_registry.gd").new()


func teardown() -> void:
	_registry.free()


# ---------------------------------------------------------------------------
# ContentRegistry tests
# ---------------------------------------------------------------------------

func test_register_and_retrieve() -> void:
	_registry.register("good", "test_iron", {"id": "test_iron", "name": "Iron"})
	var result: Dictionary = _registry.get_content("good", "test_iron")
	assert_eq(result["id"], "test_iron")
	assert_eq(result["name"], "Iron")


func test_has_content_true() -> void:
	_registry.register("good", "gold_coin", {"id": "gold_coin"})
	assert_true(_registry.has_content("good", "gold_coin"))


func test_has_content_false() -> void:
	assert_false(_registry.has_content("good", "nonexistent_id"))


func test_get_all_returns_registered_ids() -> void:
	_registry.register("building", "blacksmith", {"id": "blacksmith"})
	_registry.register("building", "tavern",     {"id": "tavern"})
	var all: Dictionary = _registry.get_all("building")
	assert_has(all, "blacksmith")
	assert_has(all, "tavern")
	assert_eq(all.size(), 2)


func test_get_types_lists_registered_types() -> void:
	_registry.register("faction", "iron_lords", {"id": "iron_lords"})
	var types: Array = _registry.get_types()
	assert_true("faction" in types)


func test_get_unknown_returns_empty() -> void:
	var result: Dictionary = _registry.get_content("good", "no_such_good")
	assert_true(result.is_empty())


func test_summary_includes_counts() -> void:
	_registry.register("good", "grain", {"id": "grain"})
	_registry.register("good", "ore",   {"id": "ore"})
	var summary: Dictionary = _registry.get_summary()
	assert_has(summary, "good")
	assert_eq(summary["good"], 2)


func test_clear_empties_registry() -> void:
	_registry.register("good", "silk", {"id": "silk"})
	_registry.clear()
	assert_false(_registry.has_content("good", "silk"))


# ---------------------------------------------------------------------------
# Data files have valid IDs (smoke test — loads a known good file)
# ---------------------------------------------------------------------------

func test_wheat_bushel_has_required_fields() -> void:
	var file := FileAccess.open("res://data/goods/wheat_bushel.json", FileAccess.READ)
	assert_not_null(file, "wheat_bushel.json must exist")
	if file == null:
		return
	var json := JSON.new()
	assert_eq(json.parse(file.get_as_text()), OK, "wheat_bushel.json must be valid JSON")
	var data: Dictionary = json.data
	assert_has(data, "id")
	assert_has(data, "name")
	assert_has(data, "category")
	assert_has(data, "base_weight_kg")
	assert_has(data, "base_value")


func test_grain_mill_has_required_fields() -> void:
	var file := FileAccess.open("res://data/buildings/grain_mill.json", FileAccess.READ)
	assert_not_null(file, "grain_mill.json must exist")
	if file == null:
		return
	var json := JSON.new()
	assert_eq(json.parse(file.get_as_text()), OK)
	var data: Dictionary = json.data
	assert_has(data, "id")
	assert_has(data, "construction_cost")
