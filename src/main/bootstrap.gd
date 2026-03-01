## Bootstrap — central startup sequence for the simulation engine.
##
## This autoload runs last. It coordinates the startup order:
##   1. Load all data definitions (DataLoader → ContentRegistry)
##   2. Validate that critical content is present
##   3. Optionally restore the last save or create a new WorldState
##   4. Resume the SimulationClock
##
## Nothing else should manually call DataLoader.load_all() or resume the clock.
extends Node

## If true, a headless test world is created on startup (no UI, no save).
@export var headless_test_mode: bool = false

## Current active WorldState. Access this through Bootstrap.world_state.
var world_state: WorldState = null

## Economy subsystem instances (kept on Bootstrap so hooks stay registered).
var _settlement_pulse:    SettlementPulse     = null
var _party_core:          PartyCore           = null
var _world_audit:         WorldAudit          = null
var _work_system:         WorkSystem          = null
var _needs_system:        NeedsSystem         = null
var _npc_schedule_system: NpcScheduleSystem   = null
var _construction_system: ConstructionSystem  = null
var _follower_system:     FollowerSystem      = null

## True once DataLoader.load_all() has completed. Read by MainMenuScreen.
var data_loaded: bool = false

signal bootstrap_completed()
@warning_ignore("UNUSED_SIGNAL")
signal bootstrap_failed(reason: String)


func _ready() -> void:
	# Use call_deferred so all other autoloads have finished their _ready().
	call_deferred("_run_bootstrap")


func _run_bootstrap() -> void:
	print("[Bootstrap] Loading data definitions...")
	DataLoader.load_all()

	var summary := ContentRegistry.get_summary()
	print("[Bootstrap] ContentRegistry: ", summary)

	data_loaded = true

	if headless_test_mode:
		_create_test_world()
		_setup_economy(world_state)
		SimulationClock.resume()
		print("[Bootstrap] Headless bootstrap complete. Tick: %d" % SimulationClock.get_tick())

	bootstrap_completed.emit()
	print("[Bootstrap] Data ready.")


# ---------------------------------------------------------------------------
# Public API — called by MainMenuScreen
# ---------------------------------------------------------------------------

## Returns true if a default save slot exists.
func has_save() -> bool:
	return SaveManager.save_exists("default")


## Start a fresh game: empty WorldState → WorldGenScreen.
## The clock stays paused; WorldGenScreen resumes it after generation.
func start_new_game() -> void:
	world_state = _new_world()
	_setup_economy(world_state)
	SimulationClock.pause()
	SceneManager.replace_scene("res://src/ui/world_gen_screen.tscn")


## Load the default save and go straight to WorldView.
func continue_game() -> void:
	world_state = SaveManager.load_save("default")
	if world_state == null:
		push_error("[Bootstrap] continue_game: failed to load default save.")
		return
	# Migrate old saves: spawn NPCs for any settlement that has none yet.
	NpcPoolManager.ensure_spawned(world_state, world_state.world_seed)
	# Migrate old saves: if any road tile is missing road_dirs the region road
	# layout was generated with the old neighbour-guess algorithm. Wipe the
	# cached region grids so they regenerate correctly on next visit.
	var needs_road_regen := false
	for cid: String in world_state.world_tiles:
		var tile: Dictionary = world_state.world_tiles[cid]
		if tile.get("has_road", false) and not tile.has("road_dirs"):
			needs_road_regen = true
			break
	if needs_road_regen:
		world_state.region_grids.clear()
	_setup_economy(world_state)
	SimulationClock.resume()
	SceneManager.replace_scene("res://src/ui/world_view.tscn")


func _create_test_world() -> void:
	print("[Bootstrap] Headless test mode — creating minimal test WorldState.")
	world_state = _new_world()


func _new_world() -> WorldState:
	var ws := WorldState.new()
	ws.region_id    = EntityRegistry.generate_id("region")
	ws.world_seed   = randi()
	ws.current_tick = 0
	return ws


## Instantiates the economy subsystems and registers their tick hooks.
## Safe to call multiple times — previous hooks are unregistered first.
func _setup_economy(ws: WorldState) -> void:
	# Unregister any previously registered hooks (e.g. on world regeneration).
	if _settlement_pulse != null:
		TickScheduler.unregister_hook(TickScheduler.Phase.PRODUCTION_PULSE,
				_settlement_pulse.tick_all)
	if _party_core != null:
		TickScheduler.unregister_hook(TickScheduler.Phase.MOVEMENT,
				_party_core.tick_movement)
	if _world_audit != null:
		TickScheduler.unregister_hook(TickScheduler.Phase.PRODUCTION_PULSE,
				_world_audit.tick_audit)
	if _work_system != null:
		TickScheduler.unregister_hook(TickScheduler.Phase.PRODUCTION_PULSE,
				_work_system.tick_work)
	if _needs_system != null:
		TickScheduler.unregister_hook(TickScheduler.Phase.MOVEMENT,
				_needs_system.tick_needs)
	if _npc_schedule_system != null:
		TickScheduler.unregister_hook(TickScheduler.Phase.MOVEMENT,
				_npc_schedule_system.tick_schedules)
	if _construction_system != null:
		TickScheduler.unregister_hook(TickScheduler.Phase.PRODUCTION_PULSE,
				_construction_system.tick_construction)
	if _follower_system != null:
		TickScheduler.unregister_hook(TickScheduler.Phase.PRODUCTION_PULSE,
				_follower_system.tick_followers)

	_settlement_pulse = SettlementPulse.new()
	_settlement_pulse.setup(ws)
	TickScheduler.register_hook(TickScheduler.Phase.PRODUCTION_PULSE,
			_settlement_pulse.tick_all)

	_party_core = PartyCore.new()
	_party_core.setup(ws)
	TickScheduler.register_hook(TickScheduler.Phase.MOVEMENT,
			_party_core.tick_movement)

	_world_audit = WorldAudit.new()
	_world_audit.setup(ws)
	TickScheduler.register_hook(TickScheduler.Phase.PRODUCTION_PULSE,
			_world_audit.tick_audit)

	_work_system = WorkSystem.new()
	_work_system.setup(ws)
	TickScheduler.register_hook(TickScheduler.Phase.PRODUCTION_PULSE,
			_work_system.tick_work)

	_needs_system = NeedsSystem.new()
	_needs_system.setup(ws)
	TickScheduler.register_hook(TickScheduler.Phase.MOVEMENT,
			_needs_system.tick_needs)

	_npc_schedule_system = NpcScheduleSystem.new()
	_npc_schedule_system.setup(ws)
	TickScheduler.register_hook(TickScheduler.Phase.MOVEMENT,
			_npc_schedule_system.tick_schedules)

	_construction_system = ConstructionSystem.new()
	_construction_system.setup(ws)
	TickScheduler.register_hook(TickScheduler.Phase.PRODUCTION_PULSE,
			_construction_system.tick_construction)

	_follower_system = FollowerSystem.new()
	_follower_system.setup(ws)
	TickScheduler.register_hook(TickScheduler.Phase.PRODUCTION_PULSE,
			_follower_system.tick_followers)

	print("[Bootstrap] Economy hooks registered (SettlementPulse + PartyCore + WorldAudit + WorkSystem + NeedsSystem + NpcScheduleSystem + ConstructionSystem + FollowerSystem).")
