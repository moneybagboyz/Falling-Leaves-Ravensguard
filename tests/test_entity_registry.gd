## Tests for EntityRegistry (P0-04).
extends BaseTest

var _reg: Node


func setup() -> void:
	_reg = load("res://src/core/registry/entity_registry.gd").new()


func teardown() -> void:
	_reg.free()


func test_generate_id_returns_non_empty_string() -> void:
	var id: String = _reg.generate_id("settlement")
	assert_ne(id, "", "Generated ID must not be empty")


func test_generated_ids_are_unique() -> void:
	var a: String = _reg.generate_id("person")
	var b: String = _reg.generate_id("person")
	assert_ne(a, b, "Two successive IDs must differ")


func test_generated_id_encodes_type_prefix() -> void:
	var id: String = _reg.generate_id("faction")
	assert_true(id.begins_with("faction_"), "ID must start with the type prefix")


func test_resolve_type_returns_correct_type() -> void:
	var id: String = _reg.generate_id("party")
	assert_eq(_reg.resolve_type(id), "party")


func test_resolve_unknown_id_returns_empty() -> void:
	assert_eq(_reg.resolve_type("nonexistent_id_xyz"), "")


func test_has_id_true_after_generate() -> void:
	var id: String = _reg.generate_id("building")
	assert_true(_reg.has_id(id))


func test_has_id_false_for_unknown() -> void:
	assert_false(_reg.has_id("ghost_id"))


func test_register_id_stores_type() -> void:
	_reg.register_id("custom_id_001", "region")
	assert_eq(_reg.resolve_type("custom_id_001"), "region")


func test_get_all_of_type() -> void:
	_reg.generate_id("caravan")
	_reg.generate_id("caravan")
	_reg.generate_id("person")
	var caravans: Array = _reg.get_all_of_type("caravan")
	assert_eq(caravans.size(), 2)


func test_serial_round_trip() -> void:
	var id: String = _reg.generate_id("region")
	var dict: Dictionary = _reg.to_dict()
	assert_has(dict, "next_uid")
	assert_has(dict, "entity_map")

	var reg2: Node = load("res://src/core/registry/entity_registry.gd").new()
	reg2.from_dict(dict)
	assert_eq(reg2.resolve_type(id), "region")
	assert_eq(reg2.to_dict()["next_uid"], dict["next_uid"])
	reg2.free()


func test_ids_not_reused_across_clear_and_restore() -> void:
	var id1: String = _reg.generate_id("item")
	var snap: Dictionary = _reg.to_dict()
	_reg.clear()
	_reg.from_dict(snap)
	var id2: String = _reg.generate_id("item")
	assert_ne(id1, id2, "IDs must not be reused after restore")
