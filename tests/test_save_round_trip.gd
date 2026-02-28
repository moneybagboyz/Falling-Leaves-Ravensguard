## Tests for WorldState/SettlementState serialisation and SaveManager
## round-trip (P0-07, P0-08).
extends BaseTest


# ---------------------------------------------------------------------------
# SettlementState serialisation
# ---------------------------------------------------------------------------

func test_settlement_state_round_trip() -> void:
	var s := SettlementState.new()
	s.settlement_id = "settlement_00000001"
	s.name          = "Ironford"
	s.cell_id       = "cell_004"
	s.faction_id    = "iron_lords"
	s.population    = {"peasant": 120, "artisan": 20}
	s.prosperity    = 0.65
	s.unrest        = 0.1
	s.inventory     = {"wheat_bushel": 300.0, "iron_ingot": 45.0}
	s.buildings     = ["building_00000001", "building_00000002"]

	var d := s.to_dict()
	var s2 := SettlementState.from_dict(d)

	assert_eq(s2.settlement_id, "settlement_00000001")
	assert_eq(s2.name,          "Ironford")
	assert_eq(s2.cell_id,       "cell_004")
	assert_eq(s2.faction_id,    "iron_lords")
	assert_eq(s2.prosperity,    0.65)
	assert_eq(s2.unrest,        0.1)
	assert_eq(s2.inventory["wheat_bushel"], 300.0)
	assert_eq(s2.buildings.size(), 2)


func test_settlement_total_population() -> void:
	var s := SettlementState.new()
	s.population = {"peasant": 100, "artisan": 30, "merchant": 10}
	assert_eq(s.total_population(), 140)


func test_empty_settlement_serialises() -> void:
	var s := SettlementState.new()
	var d := s.to_dict()
	var s2 := SettlementState.from_dict(d)
	assert_eq(s2.settlement_id, "")
	assert_eq(s2.total_population(), 0)


# ---------------------------------------------------------------------------
# WorldState serialisation
# ---------------------------------------------------------------------------

func test_world_state_round_trip() -> void:
	var ws := WorldState.new()
	ws.region_id    = "region_00000001"
	ws.seed         = 987654321
	ws.current_tick = 500

	var s := SettlementState.new()
	s.settlement_id = "settlement_00000010"
	s.name          = "Stonebridge"
	ws.add_settlement(s)

	var d := ws.to_dict()
	var ws2 := WorldState.from_dict(d)

	assert_eq(ws2.region_id,    "region_00000001")
	assert_eq(ws2.seed,         987654321)
	assert_eq(ws2.current_tick, 500)
	assert_true(ws2.has_settlement("settlement_00000010"))
	assert_eq(ws2.get_settlement("settlement_00000010").name, "Stonebridge")


func test_world_state_preserves_flags() -> void:
	var ws := WorldState.new()
	ws.world_flags["famine_active"] = true
	ws.world_flags["year"] = 1142

	var ws2 := WorldState.from_dict(ws.to_dict())
	assert_eq(ws2.world_flags["famine_active"], true)
	assert_eq(ws2.world_flags["year"], 1142)


func test_world_state_multiple_settlements() -> void:
	var ws := WorldState.new()
	for i in range(5):
		var s := SettlementState.new()
		s.settlement_id = "s_test_%02d" % i
		ws.add_settlement(s)

	var ws2 := WorldState.from_dict(ws.to_dict())
	assert_eq(ws2.get_all_settlements().size(), 5)


# ---------------------------------------------------------------------------
# SaveManager file round-trip (writes to user:// temp slot)
# ---------------------------------------------------------------------------

func test_save_and_load_world_state() -> void:
	var ws := WorldState.new()
	ws.region_id    = "region_save_test"
	ws.seed         = 42
	ws.current_tick = 77

	var s := SettlementState.new()
	s.settlement_id = "s_save_test"
	s.name          = "Testville"
	s.prosperity    = 0.8
	ws.add_settlement(s)

	# Save to a dedicated test slot.
	var err := SaveManager.save(ws, "unit_test_slot")
	assert_eq(err, OK, "Save must succeed")

	# Reload.
	var ws2 := SaveManager.load_save("unit_test_slot")
	assert_not_null(ws2, "Load must return a WorldState")
	if ws2 == null:
		return

	assert_eq(ws2.region_id,    "region_save_test")
	assert_eq(ws2.seed,         42)
	assert_eq(ws2.current_tick, 77)
	assert_true(ws2.has_settlement("s_save_test"))
	assert_eq(ws2.get_settlement("s_save_test").prosperity, 0.8)

	# Clean up.
	SaveManager.delete_save("unit_test_slot")


func test_save_includes_schema_version() -> void:
	var ws := WorldState.new()
	SaveManager.save(ws, "unit_test_version_check")

	var path := "user://saves/unit_test_version_check.json"
	var file := FileAccess.open(path, FileAccess.READ)
	assert_not_null(file, "Save file must exist")
	if file == null:
		return

	var json := JSON.new()
	json.parse(file.get_as_text())
	var d: Dictionary = json.data
	assert_has(d, "schema_version", "Save must contain schema_version")

	SaveManager.delete_save("unit_test_version_check")
