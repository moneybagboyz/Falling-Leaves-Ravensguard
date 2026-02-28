## test_world_generation.gd — Phase 1 world generation tests.
##
## Run with: godot --headless --script tests/test_runner.gd
## (TestRunner auto-discovers all test_*.gd files.)
extends "res://tests/base_test.gd"

func test_world_tile_count() -> void:
	var params := WorldGenParams.default_params()
	params.grid_width  = 32
	params.grid_height = 32
	var ws: WorldState = RegionGenerator.generate(12345, params)
	assert_eq(
		ws.world_tiles.size(),
		params.grid_width * params.grid_height,
		"world_tiles count must equal grid_width × grid_height"
	)


func test_settlement_count_in_range() -> void:
	var params := WorldGenParams.default_params()
	params.grid_width  = 64
	params.grid_height = 64
	params.num_provinces = 8
	var ws: WorldState = RegionGenerator.generate(99999, params)
	var n: int = ws.settlements.size()
	assert_true(n >= 5,  "must generate at least 5 settlements (got %d)" % n)
	assert_true(n <= 80, "must generate at most 80 settlements (got %d)" % n)


func test_all_settlements_reachable() -> void:
	var params := WorldGenParams.default_params()
	params.grid_width  = 48
	params.grid_height = 48
	params.num_provinces = 6
	var ws: WorldState = RegionGenerator.generate(777, params)
	if ws.settlements.is_empty():
		return

	# BFS from first settlement through route graph.
	var all_ids: Array = ws.settlements.keys()
	var start: String  = all_ids[0]
	var visited: Dictionary = { start: true }
	var queue: Array        = [start]

	while not queue.is_empty():
		var cur: String = queue.pop_front()
		var edges: Array = ws.routes.get(cur, [])
		for edge: Dictionary in edges:
			var nxt: String = edge.get("to_id", "")
			if not nxt.is_empty() and not visited.has(nxt):
				visited[nxt] = true
				queue.append(nxt)

	# Each settlement must be reachable (connected graph).
	for sid: String in all_ids:
		assert_true(
			visited.has(sid),
			"settlement %s not reachable from %s via routes" % [sid, start]
		)


func test_determinism() -> void:
	var params := WorldGenParams.default_params()
	params.grid_width  = 32
	params.grid_height = 32
	params.num_provinces = 4

	var ws_a: WorldState = RegionGenerator.generate(314159, params)
	var ws_b: WorldState = RegionGenerator.generate(314159, params)

	assert_eq(
		ws_a.world_tiles.size(),
		ws_b.world_tiles.size(),
		"determinism: cell count must match"
	)
	assert_eq(
		ws_a.settlements.size(),
		ws_b.settlements.size(),
		"determinism: settlement count must match"
	)

	# Spot-check: first cell's terrain must be identical.
	if not ws_a.world_tiles.is_empty():
		var key: String = ws_a.world_tiles.keys()[0]
		var ta: String = ws_a.world_tiles[key].get("terrain_type", "")
		var tb: String = ws_b.world_tiles[key].get("terrain_type", "")
		assert_eq(ta, tb, "determinism: first cell terrain must match")


func test_world_gen_params_serialisation() -> void:
	var p := WorldGenParams.default_params()
	p.num_provinces = 10
	p.sea_ratio     = 0.42
	var d: Dictionary = p.to_dict()
	var p2: WorldGenParams = WorldGenParams.from_dict(d)
	assert_eq(p2.num_provinces, 10,   "params round-trip: num_provinces")
	assert_eq(p2.sea_ratio,     0.42, "params round-trip: sea_ratio")


func test_province_names_present() -> void:
	var params := WorldGenParams.default_params()
	params.grid_width  = 32
	params.grid_height = 32
	params.num_provinces = 4
	var ws: WorldState = RegionGenerator.generate(5555, params)
	assert_true(ws.province_names.size() > 0, "province_names must not be empty")
	for nm: String in ws.province_names:
		assert_true(nm.length() > 0, "province name must not be blank")


func test_region_cell_serialisation() -> void:
	var cell := RegionCell.new()
	cell.cell_id      = "3,7"
	cell.grid_x       = 3
	cell.grid_y       = 7
	cell.terrain_type = "forest"
	cell.biome        = "temperate_forest"
	cell.elevation    = 0.55
	cell.prosperity   = 0.40
	cell.resource_tags = ["timber"]
	var d: Dictionary = cell.to_dict()
	var c2: RegionCell = RegionCell.from_dict(d)
	assert_eq(c2.cell_id,      "3,7",             "cell round-trip: cell_id")
	assert_eq(c2.terrain_type, "forest",           "cell round-trip: terrain_type")
	assert_eq(c2.biome,        "temperate_forest", "cell round-trip: biome")
	assert_true(
		c2.resource_tags.has("timber"),
		"cell round-trip: resource_tags should contain timber"
	)
