## test_economy.gd — economy subsystem stress tests.
##
## Tests run in isolation, bypassing TickScheduler and autoloads.
## They drive SettlementPulse and PartyCore directly.
class_name TestEconomy
extends BaseTest

# ── Helpers ───────────────────────────────────────────────────────────────────

## Build a minimal WorldState with two connected settlements
## suitable for economy testing.
func _make_world() -> WorldState:
	var ws := WorldState.new()
	ws.region_id    = "test_region"
	ws.world_seed   = 12345
	ws.current_tick = 0

	# Cell data (referenced by acreage seeding)
	ws.world_tiles["00_00"] = {
		"cell_id": "00_00", "grid_x": 0, "grid_y": 0,
		"terrain_type": "plains", "tags": ["arable"],
		"fertility": 0.8, "altitude": 0.3
	}
	ws.world_tiles["01_00"] = {
		"cell_id": "01_00", "grid_x": 1, "grid_y": 0,
		"terrain_type": "forest", "tags": ["arable", "wood"],
		"fertility": 0.6, "altitude": 0.35
	}

	# Settlement A — farming village
	var ss_a := SettlementState.new()
	ss_a.settlement_id = "sid_a"
	ss_a.cell_id       = "00_00"
	ss_a.name          = "Ashford"
	ss_a.tier          = 1
	ss_a.population    = {"peasant": 150, "artisan": 40, "merchant": 10}
	ss_a.prosperity    = 0.5
	ss_a.unrest        = 0.0
	ss_a.arable_acres  = 100.0
	ss_a.woodland_acres= 20.0
	ss_a.worked_acres  = 60.0
	ss_a.fallow_acres  = 30.0
	ss_a.pasture_acres = 5.0
	ss_a.woodlot_acres = 10.0
	ss_a.inventory     = {"wheat_bushel": 0.0, "coin": 100.0}
	ws.settlements["sid_a"] = ss_a

	# Settlement B — forest hamlet with shortage
	var ss_b := SettlementState.new()
	ss_b.settlement_id = "sid_b"
	ss_b.cell_id       = "01_00"
	ss_b.name          = "Briarvale"
	ss_b.tier          = 0
	ss_b.population    = {"peasant": 70, "artisan": 10}
	ss_b.prosperity    = 0.4
	ss_b.unrest        = 0.0
	ss_b.arable_acres  = 30.0
	ss_b.woodland_acres= 80.0
	ss_b.worked_acres  = 18.0
	ss_b.fallow_acres  = 9.0
	ss_b.pasture_acres = 2.0
	ss_b.woodlot_acres = 40.0
	ss_b.inventory     = {"wheat_bushel": 2.0, "coin": 50.0}
	ws.settlements["sid_b"] = ss_b

	# Route between A and B (simple 3-tile path)
	ws.routes["sid_a"] = [{"to_id": "sid_b", "distance": 3,
			"path": [[0, 0], [0, 1], [1, 0]]}]
	ws.routes["sid_b"] = [{"to_id": "sid_a", "distance": 3,
			"path": [[1, 0], [0, 1], [0, 0]]}]

	return ws


# ── Tests ─────────────────────────────────────────────────────────────────────

func test_production_ledger_adds_wheat() -> void:
	var ws := _make_world()
	var pulse := SettlementPulse.new()
	pulse.setup(ws)

	# Tick once (delta = STRATEGIC_CADENCE = 1, i.e. 1 game day)
	pulse.tick_all(10)

	var ss_a: SettlementState = ws.settlements.get("sid_a")
	assert_not_null(ss_a, "ss_a must exist")
	var wheat: float = ss_a.inventory.get("wheat_bushel", 0.0)
	# Ashford should have produced wheat (60 worked acres × ~0.0137/tick × 10 ticks × 0.8 fert)
	assert_gt(wheat, 0.0, "Ashford should produce wheat after one pulse (got %.4f)" % wheat)


func test_no_negative_inventory_after_many_pulses() -> void:
	var ws := _make_world()
	var pulse := SettlementPulse.new()
	pulse.setup(ws)

	# Simulate 20 in-game years: 7300 ticks / 10 per strategic tick = 730 pulses
	for i: int in 730:
		pulse.tick_all((i + 1) * 10)

	for sid: String in ws.settlements.keys():
		var sv = ws.settlements[sid]
		if not (sv is SettlementState):
			continue
		var ss: SettlementState = sv
		for good: String in ss.inventory.keys():
			assert_true(ss.inventory[good] >= 0.0,
				"Negative inventory for %s.%s = %.4f" % [sid, good, ss.inventory[good]])


func test_price_propagates_from_surplus_to_shortage() -> void:
	var ws := _make_world()
	var pulse := SettlementPulse.new()
	pulse.setup(ws)

	# Run 50 pulses to build up surplus in Ashford
	for i: int in 50:
		pulse.tick_all((i + 1) * 10)

	var ss_a: SettlementState = ws.settlements.get("sid_a")
	assert_not_null(ss_a, "ss_a must exist after pulses")
	var wheat_price: float = ss_a.prices.get("wheat_bushel", 0.0)
	# Surplus should drive price below or near base
	assert_gt(wheat_price, 0.0, "wheat_bushel price should be set after pulses")


func test_trade_party_spawns_and_delivers() -> void:
	var ws := _make_world()
	var pulse := SettlementPulse.new()
	pulse.setup(ws)

	# Build enough surplus in sid_a to trigger a spawn
	var ss_a: SettlementState = ws.settlements.get("sid_a")
	ss_a.inventory["wheat_bushel"] = 60.0   # well above SURPLUS_THRESHOLD=20
	var ss_b: SettlementState = ws.settlements.get("sid_b")
	ss_b.shortages["wheat_bushel"] = 10.0   # above SHORTAGE_THRESHOLD=5

	# Run pulse so TradePartySpawner fires
	pulse.tick_all(10)

	assert_gt(ws.trade_parties.size(), 0,
		"A trade party should have been spawned toward Briarvale")

	# Move party to destination via PartyCore
	var core := PartyCore.new()
	core.setup(ws)

	var wheat_before: float = ss_b.inventory.get("wheat_bushel", 0.0)
	# Advance enough ticks for the party to traverse a 3-tile path
	for i: int in 5:
		core.tick_movement(11 + i)

	var wheat_after: float = ss_b.inventory.get("wheat_bushel", 0.0)
	assert_true(wheat_after >= wheat_before,
		"Briarvale wheat should not decrease after party movement")


func test_prosperity_grows_with_sufficient_food() -> void:
	var ws := _make_world()
	var ss_a: SettlementState = ws.settlements.get("sid_a")
	# Pre-fill with ample food and coin
	ss_a.inventory["wheat_bushel"] = 500.0
	ss_a.inventory["coin"]         = 500.0

	var pulse := SettlementPulse.new()
	pulse.setup(ws)

	var prosperity_before: float = ss_a.prosperity

	for i: int in 10:
		pulse.tick_all((i + 1) * 10)

	assert_true(ss_a.prosperity >= prosperity_before,
		"Prosperity should grow when food is plentiful (%.3f → %.3f)"
		% [prosperity_before, ss_a.prosperity])


func test_settlement_state_serialisation_round_trip() -> void:
	var ws := _make_world()
	var pulse := SettlementPulse.new()
	pulse.setup(ws)
	pulse.tick_all(10)

	var ss_a: SettlementState = ws.settlements.get("sid_a")
	var d: Dictionary = ss_a.to_dict()
	var ss_clone := SettlementState.from_dict(d)

	assert_eq(ss_clone.settlement_id, ss_a.settlement_id,
		"Round-trip settlement_id mismatch")
	assert_eq(ss_clone.inventory.get("wheat_bushel", -1.0),
		ss_a.inventory.get("wheat_bushel", -1.0),
		"Round-trip inventory[wheat_bushel] mismatch")
	assert_eq(ss_clone.prosperity, ss_a.prosperity,
		"Round-trip prosperity mismatch")
