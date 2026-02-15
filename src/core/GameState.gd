extends Node

# ============================================================================
# REFACTORED GAMESTATE - FACADE PATTERN
# Delegates to specialized modules: WorldState, EntityRegistry, PlayerState, GameClock
# ============================================================================

const WorldState = preload("res://src/state/WorldState.gd")
const EntityRegistry = preload("res://src/state/EntityRegistry.gd")
const PlayerState = preload("res://src/state/PlayerState.gd")
const GameClock = preload("res://src/state/GameClock.gd")
const GameEnums = preload("res://src/core/GameEnums.gd")
const StateManager = preload("res://src/core/StateManager.gd")

# Managers (existing)
const EconomyManager = preload("res://src/managers/EconomyManager.gd")
const SettlementManager = preload("res://src/managers/SettlementManager.gd")
const FactionManager = preload("res://src/managers/FactionManager.gd")
const CombatManager = preload("res://src/managers/CombatManager.gd")
const AIManager = preload("res://src/managers/AIManager.gd")
const WarManager = preload("res://src/managers/WarManager.gd")

const GameData = preload("res://src/core/GameData.gd")
const WorldAudit = preload("res://src/utils/WorldAudit.gd")
const Globals = preload("res://src/core/Globals.gd")

# Data Classes
const GDPlayer = preload("res://src/data/GDPlayer.gd")
const GDUnit = preload("res://src/data/GDUnit.gd")
const GDQuest = preload("res://src/data/GDQuest.gd")
const GDFaction = preload("res://src/data/GDFaction.gd")
const GDArmy = preload("res://src/data/GDArmy.gd")
const GDBattle = preload("res://src/data/GDBattle.gd")
const GDNPC = preload("res://src/data/GDNPC.gd")
const GDSettlement = preload("res://src/data/GDSettlement.gd")

# Signals
signal log_updated
signal map_updated
signal world_gen_updated(stage_name)
@warning_ignore("unused_signal")
signal battle_started(enemy)
signal battle_ended(result)
signal dungeon_started(ruin)
@warning_ignore("unused_signal")
signal dungeon_ended
@warning_ignore("unused_signal")
signal dialogue_started(target, options)

# Core State Modules
var world: WorldState
var entities: EntityRegistry
var player_state: PlayerState
var clock: GameClock
var state_manager: StateManager

# Temporary controller reference (for backwards compat)
var region_ctrl = null

# City Studio Config (TODO: Move to appropriate module)
var city_studio_config = {
	"type": "city",
	"size": 200,
	"walls": "stone",
	"rivers": false,
	"pop": 5000,
	"seed": 12345
}
var city_studio_idx = 0

# === PROPERTY FORWARDING FOR BACKWARD COMPATIBILITY ===
# These properties forward to the appropriate module

# World properties
var grid: Array:
	get: return world.grid
	set(value): world.grid = value

var width: int:
	get: return world.width
	set(value): world.width = value

var height: int:
	get: return world.height
	set(value): world.height = value

var resources: Dictionary:
	get: return world.resources
	set(value): world.resources = value

var geology: Dictionary:
	get: return world.geology
	set(value): world.geology = value

var ruins: Dictionary:
	get: return world.ruins
	set(value): world.ruins = value

var world_seed: int:
	get: return world.world_seed
	set(value): world.world_seed = value

var world_name: String:
	get: return world.world_name
	set(value): world.world_name = value

var province_grid:
	get: return world.province_grid
	set(value): world.province_grid = value

var provinces:
	get: return world.provinces
	set(value): world.provinces = value

var map_mode: String:
	get: return world.map_mode
	set(value): world.map_mode = value

var render_mode: String:
	get: return world.render_mode
	set(value): world.render_mode = value

var travel_mode:
	get: return world.travel_mode
	set(value): world.travel_mode = value

var local_offset: Vector2:
	get: return world.local_offset
	set(value): world.local_offset = value

var last_gen_offset: Vector2:
	get: return world.last_gen_offset
	set(value): world.last_gen_offset = value

var local_step_count: int:
	get: return world.local_step_count
	set(value): world.local_step_count = value

var astar:
	get: return world.astar
	set(value): world.astar = value

var killed_fauna: Dictionary:
	get: return world.killed_fauna
	set(value): world.killed_fauna = value

var spatial_grid: Dictionary:
	get: return world.spatial_grid
	set(value): world.spatial_grid = value

var distance_cache: Dictionary:
	get: return world.distance_cache
	set(value): world.distance_cache = value

# Entity properties
var settlements: Dictionary:
	get: return entities.settlements
	set(value): entities.settlements = value

var armies: Array:
	get: return entities.armies

var ongoing_battles: Array:
	get: return entities.ongoing_battles

var caravans: Array:
	get: return entities.caravans

var factions: Array:
	get: return entities.factions

var trade_contracts: Array:
	get: return entities.trade_contracts

var military_campaigns: Array:
	get: return entities.military_campaigns

var logistical_pulses: Array:
	get: return entities.logistical_pulses

var migrants: Array:
	get: return entities.migrants

var world_market_orders:
	get: return entities.world_market_orders

var total_battles: int:
	get: return entities.total_battles
	set(value): entities.total_battles = value

var total_sieges: int:
	get: return entities.total_sieges
	set(value): entities.total_sieges = value

var total_captures: int:
	get: return entities.total_captures
	set(value): entities.total_captures = value

var total_caravan_raids: int:
	get: return entities.total_caravan_raids
	set(value): entities.total_caravan_raids = value

var total_trade_volume: int:
	get: return entities.total_trade_volume
	set(value): entities.total_trade_volume = value

# Player properties
var player: GDPlayer:
	get: return player_state.player
	set(value): player_state.player = value

var active_quests: Array:
	get: return player_state.active_quests
	set(value): player_state.active_quests = value

var active_ruin_pos: Vector2i:
	get: return player_state.active_ruin_pos
	set(value): player_state.active_ruin_pos = value

var is_resting: bool:
	get: return player_state.is_resting
	set(value): player_state.is_resting = value

# Clock properties
var turn: int:
	get: return clock.turn
	set(value): clock.turn = value

var hour: int:
	get: return clock.hour
	set(value): clock.hour = value

var day: int:
	get: return clock.day
	set(value): clock.day = value

var month: int:
	get: return clock.month
	set(value): clock.month = value

var year: int:
	get: return clock.year
	set(value): clock.year = value

var is_turbo: bool:
	get: return clock.is_turbo
	set(value): clock.is_turbo = value

var event_log: Array:
	get: return clock.event_log
	set(value): clock.event_log = value

var history: Array:
	get: return clock.history
	set(value): clock.history = value

var monthly_ledger: Dictionary:
	get: return clock.monthly_ledger
	set(value): clock.monthly_ledger = value

# RNG
var rng = RandomNumberGenerator.new()

# Constants forwarding
const WORLD_TILE_SIZE = 1000.0
const METERS_PER_LOCAL_TILE = 2.0
const SECTOR_SIZE_METERS = 1000.0
const PAGING_THRESHOLD = 50.0
const LOCAL_GRID_W = 500
const LOCAL_GRID_H = 500
const SPATIAL_CELL_SIZE = 10
enum TravelMode { FAST, REGION, LOCAL }

# === INITIALIZATION ===

func _ready():
	# Initialize modules
	world = WorldState.new()
	entities = EntityRegistry.new()
	player_state = PlayerState.new()
	clock = GameClock.new()
	state_manager = StateManager.new()
	
	# Forward clock signals
	clock.log_updated.connect(func(): log_updated.emit())
	clock.time_advanced.connect(_on_time_advanced)
	clock.day_changed.connect(_on_day_changed)
	clock.month_changed.connect(_on_month_changed)
	
	# Initialize RNG
	rng.randomize()
	print("World Seed: ", rng.seed)
	
	# Connect battle ended (for compatibility)
	battle_ended.connect(_on_battle_ended)
	
	# Validate initialization
	_validate_initialization()

func _validate_initialization() -> bool:
	"""Validate that all critical components are properly initialized"""
	var errors = []
	
	# Check modules
	if not world:
		errors.append("WorldState module not initialized")
	if not entities:
		errors.append("EntityRegistry module not initialized")
	if not player_state:
		errors.append("PlayerState module not initialized")
	if not clock:
		errors.append("GameClock module not initialized")
	if not state_manager:
		errors.append("StateManager module not initialized")
	
	# Check player initialization
	if player_state:
		if not player_state.player:
			errors.append("Player object not initialized")
		elif not player_state.player.commander:
			errors.append("Player commander not initialized")
	
	if errors.size() > 0:
		push_error("GameState initialization failed:")
		for err in errors:
			push_error("  - " + err)
		return false
	
	print("GameState: All modules initialized successfully")
	return true

func _on_time_advanced(h: int, d: int, m: int, y: int):
	pass # Custom logic if needed

func _on_day_changed(d: int):
	pass # Custom logic if needed

func _on_month_changed(m: int):
	if not is_turbo:
		run_world_audit()

# === DELEGATED PLAYER METHODS ===

func get_party_size() -> int:
	return player_state.get_party_size()

func get_max_weight() -> float:
	return player_state.get_max_weight()

func get_total_weight() -> float:
	return player_state.get_total_weight()

func get_unit_equipment_weight(u_obj: GDUnit) -> float:
	return player_state.get_unit_equipment_weight(u_obj)

func create_item(type_key: String, material_key: String, quality: String = "standard"):
	return player_state.create_item(type_key, material_key, quality)

# === DELEGATED WORLD METHODS ===

func get_tile_type(pos: Vector2i) -> String:
	var biome = world.get_biome(pos)
	match biome:
		"forest": return "forest"
		"ocean": return "water"
		"mountain": return "mountain"
		"desert": return "desert"
		_: return "plains"

func get_true_terrain(pos: Vector2i) -> String:
	"""Get actual terrain type, using geology to override settlement/road tiles"""
	return world.get_true_terrain(pos)

func get_total_population() -> int:
	"""Get total population across all settlements"""
	var total = 0
	for pos in settlements:
		total += settlements[pos].population
	return total

func get_entities_near(pos: Vector2i, radius: int = 1) -> Array:
	return world.get_entities_near(pos, radius)

func update_spatial_grid():
	"""Rebuild spatial grid from current entity positions"""
	world.spatial_grid.clear()
	for army in entities.armies:
		world.add_to_spatial_grid(army, army.pos)
	for caravan in entities.caravans:
		world.add_to_spatial_grid(caravan, caravan.pos)

# === DELEGATED ENTITY METHODS ===

func get_faction(id: String) -> GDFaction:
	return entities.get_faction_by_id(id)

func find_npc(npc_id: String) -> GDNPC:
	for pos in settlements:
		var s = settlements[pos]
		for npc in s.npcs:
			if npc.id == npc_id:
				return npc
	return null

# === DELEGATED CLOCK METHODS ===

func add_log(message: String):
	clock.add_log("[Turn %d] %s" % [turn, message])

func add_history_event(msg: String):
	clock.add_history(msg)
	add_log("[color=orange][HISTORY][/color] " + msg)

func get_date_string() -> String:
	return clock.get_full_datetime()

func get_time_of_day() -> String:
	return "Day" if clock.is_day() else "Night"

func is_night() -> bool:
	return clock.is_night()

func advance_time():
	clock.advance_time(1)
	_process_turn_logic()

# === ECONOMY/SETTLEMENT DELEGATION ===

func hire_recruit(s_pos, pool_idx):
	SettlementManager.hire_recruit(self, s_pos, pool_idx)

func recruit_prisoner(idx):
	SettlementManager.recruit_prisoner(self, idx)

func ransom_prisoner(idx):
	SettlementManager.ransom_prisoner(self, idx)

func buy_item(s_pos, shop_idx):
	var s = settlements[s_pos]
	var shop = s.shop_inventory
	if shop_idx < 0 or shop_idx >= shop.size(): return
	
	var item = shop[shop_idx]
	if player.crowns >= item.get("price", 0):
		player.crowns -= item.get("price", 0)
		shop.remove_at(shop_idx)
		player.stash.append(item)
		add_log("Bought %s for %d Crowns." % [item.get("name", "Item"), item.get("price", 0)])
		emit_signal("map_updated")
	else:
		add_log("Not enough crowns for %s (Need %dc)." % [item.get("name", "Item"), item.get("price", 0)])

func buy_resource(s_pos, res_name, amount = 10):
	var s = settlements.get(s_pos)
	if not s: return
	
	var price = get_buy_price(s, res_name)
	var cost = amount * price
	
	if player.crowns >= cost and s.inventory.get(res_name, 0) >= amount:
		player.crowns -= cost
		s.crown_stock += cost
		if amount > 0:
			s.inventory[res_name] -= amount
		player.inventory[res_name] = player.inventory.get(res_name, 0) + amount
		
		# Food conversion
		if res_name in ["grain", "fish", "game", "meat"]:
			player.provisions += amount * 10
			player.inventory[res_name] -= amount
			add_log("Bought %d %s (Provisions +%d) for %d Crowns." % [amount, res_name, amount * 10, cost])
		else:
			add_log("Bought %d %s for %d Crowns." % [amount, res_name, cost])
		
		emit_signal("map_updated")
	else:
		add_log("Cannot afford or insufficient stock.")

func sell_resource(s_pos, res_name, amount = 10):
	var s = settlements.get(s_pos)
	if not s: return
	
	var price = get_sell_price(s, res_name)
	var payout = amount * price
	
	if player.inventory.get(res_name, 0) >= amount and s.crown_stock >= payout:
		if amount > 0:
			player.inventory[res_name] -= amount
		s.inventory[res_name] = s.inventory.get(res_name, 0) + amount
		s.crown_stock -= payout
		player.crowns += payout
		add_log("Sold %d %s for %d Crowns." % [amount, res_name, payout])
		emit_signal("map_updated")
	else:
		add_log("Insufficient stock or town cannot afford.")

func get_item_price(type_key, mat_key, qual, is_commission = false) -> int:
	return EconomyManager.get_item_price(type_key, mat_key, qual, is_commission)

func get_kit_cost(c_name: String, is_commission = false) -> int:
	return EconomyManager.get_kit_cost(player, c_name, is_commission)

func get_reequip_cost(c_name: String) -> int:
	return EconomyManager.get_reequip_cost(player, c_name)

func fund_class_commissions(s_pos, c_name: String):
	EconomyManager.fund_class_commissions(self, s_pos, c_name)

func create_class(c_name: String, reqs: Dictionary):
	EconomyManager.create_class(self, c_name, reqs)

func assign_class(unit_idx: int, c_name: String):
	EconomyManager.assign_class(self, unit_idx, c_name)

func get_quality_rank(q: String) -> int:
	return EconomyManager.get_quality_rank(q)

func check_readiness(unit: GDUnit) -> Dictionary:
	return EconomyManager.check_readiness(player, unit)

func auto_equip_all():
	EconomyManager.auto_equip_all(self)

func commission_items(s_pos, type_key, mat_key, qual, count):
	EconomyManager.commission_items(self, s_pos, type_key, mat_key, qual, count)

func equip_commander_item(_slot_key: String, stash_idx: int):
	EconomyManager.perform_equip(self, player.commander, stash_idx)

func unequip_commander_item(slot_key: String):
	var slot = ""
	var layer = ""
	if slot_key in ["main_hand", "off_hand"]:
		slot = slot_key
	elif slot_key == "cover":
		slot = "torso"
		layer = "cover"
	else:
		var parts = slot_key.split("_")
		layer = parts[parts.size()-1]
		slot = slot_key.trim_suffix("_" + layer)
		if slot == "arms": slot = "l_arm"
		elif slot == "legs": slot = "l_leg"
	
	EconomyManager.perform_unequip(self, player.commander, slot, layer)

func equip_item(unit_idx: int, stash_idx: int):
	if unit_idx < 0 or unit_idx >= player.roster.size(): return
	EconomyManager.perform_equip(self, player.roster[unit_idx], stash_idx)

func unequip_item(unit_idx: int, slot: String, layer: String = ""):
	if unit_idx < 0 or unit_idx >= player.roster.size(): return
	EconomyManager.perform_unequip(self, player.roster[unit_idx], slot, layer)

func get_price(s, res):
	if not s: return 0
	return EconomyManager.get_price(res, s)

func get_buy_price(s, res):
	var val = get_price(s, res)
	return int(val * 1.1) # 10% markup for player buying from town

func get_sell_price(s, res):
	var val = get_price(s, res)
	return int(val * 0.9) # 10% margin for player selling to town

func get_market_info(s, res):
	return EconomyManager.get_market_info(s, res)

func sponsor_building(s_pos, b_name):
	SettlementManager.sponsor_building(self, s_pos, b_name)

func donate_resource(s_pos, res, amount):
	SettlementManager.donate_resource(self, s_pos, res, amount)

func accept_quest(s_pos, npc_idx, quest_idx):
	var s = settlements.get(s_pos)
	if not s: return
	
	if npc_idx >= s.npcs.size(): return
	var npc = s.npcs[npc_idx]
	
	if quest_idx >= npc.quests.size(): return
	var q = npc.quests[quest_idx]
	
	q.status = GDQuest.Status.ACTIVE
	active_quests.append(q)
	npc.quests.remove_at(quest_idx)
	
	add_log("Accepted Quest: [color=cyan]%s[/color] from %s." % [q.title, npc.name])

# === FACTION/COMBAT DELEGATION ===

func set_relation(f1, f2, status):
	get_faction(f1).relations[f2] = status
	get_faction(f2).relations[f1] = status
	if status == "war":
		# Only log major wars
		if f1 != "bandits" and f2 != "bandits":
			add_log("WAR! %s declared war on %s!" % [get_faction(f1).name, get_faction(f2).name])

func get_relation(f1, f2):
	return FactionManager.get_relation(f1, f2, factions)

func resolve_ai_battle(att, def):
	# Check if a battle already exists at this location or involving these parties
	for b in ongoing_battles:
		if (b.attacker == att and b.defender == def) or (b.attacker == def and b.defender == att):
			return # Already in battle
	
	var new_battle = GDBattle.new(def.pos, att, def)
	ongoing_battles.append(new_battle)
	att.is_in_battle = true
	def.is_in_battle = true
	add_log("[color=red]BATTLE:[/color] %s and %s are engaged at %v!" % [att.name, def.name, def.pos])

func resolve_siege(army_obj, town, _t_pos):
	return CombatManager.resolve_siege(army_obj, town, self)

# === TRACKING ===

func track_production(res: String, amount: int):
	monthly_ledger["production_" + res] = monthly_ledger.get("production_" + res, 0) + amount

func track_tax(amount: int):
	monthly_ledger["taxes"] = monthly_ledger.get("taxes", 0) + amount

func track_upkeep(amount: int):
	monthly_ledger["upkeep"] = monthly_ledger.get("upkeep", 0) + amount

func run_world_audit():
	WorldAudit.print_monthly_report(self)
	monthly_ledger.clear()

# === WORLD GENERATION ===

func init_world(config: Dictionary = {}):
	# Clear previous game state
	world.clear()
	entities.clear()
	player_state.clear()
	clock.clear()
	
	var gen = WorldGen.new()
	# Connect to world gen signal to relay it to UI
	gen.step_completed.connect(func(stage): world_gen_updated.emit(stage))
	
	width = config.get("width", int(Globals.WORLD_W))
	height = config.get("height", int(Globals.WORLD_H))
	
	if "seed" in config:
		world_seed = config["seed"]
		rng.seed = world_seed
	else:
		world_seed = randi()
		rng.seed = world_seed
	
	if "name" in config:
		world_name = config["name"]
	
	# Pre-initialize grid to ocean so the loading screen shows a blue void being filled
	grid.clear()
	for y in range(height):
		var row = []
		for x in range(width):
			row.append('~')
		grid.append(row)
	
	# Pass our grid so WorldGen can update it live for the loading screen
	var data = await gen.generate(width, height, rng, grid, config)
	# Merge back Other data but keep our grid reference
	resources = data["resources"]
	geology = data.get("geology", {})
	settlements = data["settlements"]
	ruins = data.get("ruins", {})
	player.pos = data["start_pos"]
	
	# Populate armies array (cannot reassign typed arrays directly)
	armies.clear()
	for army in data.get("armies", []):
		armies.append(army)
	
	# Populate caravans array
	caravans.clear()
	for caravan in data.get("caravans", []):
		caravans.append(caravan)
	
	province_grid = data.get("province_grid", [])
	provinces = data.get("provinces", {})
	
	# Capture dynamic factions
	if data.has("factions"):
		# Clear and repopulate factions array
		factions.clear()
		for faction in data["factions"]:
			factions.append(faction)
		# Keep player and special factions
		var player_f = GDFaction.new("player", "Player's Band", 1000, "yellow")
		var bandit_f = GDFaction.new("bandits", "Outlaws", 0, "gray")
		var neutral_f = GDFaction.new("neutral", "Independent", 0, "white")
		factions.append(player_f)
		factions.append(bandit_f)
		factions.append(neutral_f)
	
	# Initialize AStar
	astar.region = Rect2i(0, 0, width, height)
	astar.cell_size = Vector2(1, 1)
	astar.diagonal_mode = AStarGrid2D.DIAGONAL_MODE_NEVER
	astar.update()
	
	for y in range(height):
		for x in range(width):
			var t = grid[y][x]
			var weight = 1.0
			if t == '#': weight = 3.0 # Forest
			elif t == '&': weight = 5.0 # Jungle/Swamp
			elif t == '"': weight = 2.0 # Desert
			elif t == '*': weight = 1.2 # Tundra
			elif t == 'o': weight = 2.5 # Hills
			elif t == 'O': weight = 7.0 # Peaks
			elif t == '^': weight = 15.0 # Mountain
			elif t == '~': 
				astar.set_point_solid(Vector2i(x, y), true)
				continue
			elif t == '≈': weight = 8.0 # Large River
			elif t == '/': weight = 4.0 # Small River
			elif t == '\\': weight = 4.0
			elif t == '=': weight = 0.5 # Roads
			
			astar.set_point_weight_scale(Vector2i(x, y), weight)
	
	# Initial Survey for all settlements
	for s_pos in settlements:
		var s = settlements[s_pos]
		EconomyManager.recalculate_production(s, grid, resources, geology)
		SettlementManager.refresh_recruits(s)
		if not s.type.ends_with("_hamlet"):
			SettlementManager.refresh_shop(s)
	
	WorldAudit.init_ledger(self)
	WorldAudit.print_monthly_report(self)
	
	# Initial spatial grid build
	update_spatial_grid()
	
	add_log("Welcome to the ASCII Realms. WASD to move.")
	world_gen_updated.emit("COMPLETE")
	map_updated.emit()
	emit_signal("map_updated")

# === XP & LEVELING ===

func get_xp_for_next_level(lvl: int) -> int:
	return lvl * lvl * 100

func grant_xp(unit: GDUnit, amount: int):
	# Intelligence Bonus (approx 5% per point over 10)
	var int_val = unit.attributes.get("intelligence", 10)
	var multiplier = 1.0 + (max(0, int_val - 10) * 0.05)
	
	unit.xp += int(amount * multiplier)
	var needed = get_xp_for_next_level(unit.level)
	while unit.xp >= needed:
		unit.xp -= needed
		unit.level += 1
		unit.stat_points += 2
		unit.skill_points += 5
		if unit == player.commander:
			add_log("[color=cyan]LEVEL UP! You are now Level %d![/color]" % unit.level)
		else:
			add_log("%s reached Level %d!" % [unit.name, unit.level])
		needed = get_xp_for_next_level(unit.level)
	map_updated.emit()

# === HEALING ===

func process_daily_healing():
	var heal_amt = 1 if not is_resting else 5
	
	# Heal Commander
	_heal_unit(player.commander, heal_amt)
	
	# Heal Roster
	for u_obj in player.roster:
		_heal_unit(u_obj, heal_amt)
	
	emit_signal("map_updated")

func _heal_unit(u_obj, amt):
	var healed_any = false
	var total_hp = 0
	for p_key in u_obj.body:
		var part = u_obj.body[p_key]
		for tissue in part["tissues"]:
			if tissue["hp"] < tissue["hp_max"]:
				# Severed parts don't heal
				if u_obj.status.get("severed_" + p_key, false): continue
				
				var old_hp = tissue["hp"]
				tissue["hp"] = min(tissue["hp_max"], tissue["hp"] + amt)
				if tissue["hp"] > old_hp: healed_any = true
				
				# If part was mangled, it heals to a lower max
				if u_obj.status.get("mangled_" + p_key, false):
					tissue["hp"] = min(tissue["hp_max"] / 2, tissue["hp"])
		
		# Calculate part total for unit.hp sync
		for tissue in part["tissues"]:
			total_hp += tissue["hp"]
			
	u_obj.hp = total_hp
	
	if healed_any:
		# Optional: add log or effect
		pass

# === PROVINCE DATA ===

func get_province_at(pos: Vector2i) -> Dictionary:
	if province_grid.size() > pos.y and province_grid[pos.y].size() > pos.x:
		var p_id = province_grid[pos.y][pos.x]
		return provinces.get(p_id, {})
	return {}

func get_province_owner_faction(p_id: int) -> String:
	var p_data = provinces.get(p_id, {})
	return p_data.get("faction", "neutral")

# === TURN PROCESSING ===

func _process_turn_logic():
	"""Process all turn-based game logic"""
	# Update spatial hash at the start of every hour for AI lookups
	update_spatial_grid()
	
	# Update World Market and Trade Contracts
	EconomyManager.update_trade_networks(self)
	
	# SIEGE LOGIC: Advance timers and handle abandonment
	_process_sieges()
	
	# Day rollover logic
	if hour == 0:
		_process_new_day()
	
	# STAGGERED SETTLEMENT UPDATES (Optimization 2)
	_process_settlement_updates()
	
	# Party Food Consumption (Twice a day)
	if hour == 8 or hour == 20:
		_process_food_consumption()
	
	# Hourly Logic (Movement, Construction, Healing)
	_process_hourly_logic()
	
	# BATTLES
	_process_battles()
	
	# Commissions
	_process_commissions()
	
	AIManager.process_movement(self)
	
	if not is_turbo:
		emit_signal("map_updated")

func _process_sieges():
	for s_pos in settlements:
		var s = settlements[s_pos]
		if s.is_under_siege:
			var nearby = get_entities_near(s_pos, 2)
			var still_active = false
			for e in nearby:
				if "faction" in e and e.faction == s.siege_attacker_faction:
					if "type" in e and e.type in ["army", "lord", "player"]:
						still_active = true
						break
			
			if still_active:
				s.siege_timer += 1
			else:
				# Abandonment: If no one is near, timer ticks down. At 0, siege breaks.
				s.siege_timer = max(0, s.siege_timer - 2)
				if s.siege_timer <= 0:
					s.is_under_siege = false
					s.siege_attacker_faction = ""
					add_log("The siege of %s has been abandoned." % s.name)

func _process_new_day():
	add_log("--- Day %d ---" % day)
	
	SettlementManager.process_migration(self)
	AIManager.spawn_bandit_party(self)
	WarManager.process_diplomacy(self)
	
	# --- FACTION STIPENDS ---
	_process_faction_stipends()
	
	# --- LORD UPKEEP ---
	_process_lord_upkeep()
	
	_process_player_contract()
	
	if player.founding_timer > 0:
		player.founding_timer -= 1
		if player.founding_timer == 0:
			SettlementManager.finalize_player_settlement(self, player.founding_pos, player.founding_type)
			player.founding_pos = Vector2i(-1, -1)
			player.founding_type = ""

func _process_faction_stipends():
	"""Distribute 20% of faction treasury back to lords and settlements daily"""
	for f in factions:
		if f.id in ["neutral", "bandits", "player"]: continue
		if f.treasury > 5000:
			var payout = int(f.treasury * 0.20)
			f.treasury -= payout
			
			# find lords and settlements
			var f_lords = []
			for a in armies:
				if a.type == "lord" and a.faction == f.id: f_lords.append(a)
			
			var f_settlements = []
			for s in settlements.values():
				if s.faction_id == f.id: f_settlements.append(s)
			
			if not f_lords.is_empty() or not f_settlements.is_empty():
				var share = payout / (f_lords.size() + f_settlements.size())
				for l in f_lords: l.crowns += share
				for s in f_settlements: s.crown_stock += share

func _process_lord_upkeep():
	"""Process upkeep costs for lord armies"""
	for a in armies:
		if a.type == "lord":
			# SYNC: Pull taxes/stipends from the political NPC record into the field army
			if a.lord_id != "":
				var lord_npc = find_npc(a.lord_id)
				if lord_npc and lord_npc.crowns > 0:
					a.crowns += lord_npc.crowns
					lord_npc.crowns = 0
					
			var upkeep = a.roster.size() * Globals.LORD_UPKEEP_PER_UNIT
			if a.crowns >= upkeep:
				a.crowns -= upkeep
				track_upkeep(upkeep)
			elif a.home_fief != Vector2i(-1, -1):
				var s = settlements[a.home_fief]
				var take = min(upkeep, max(0, s.crown_stock - 1000))
				s.crown_stock -= take
				a.crowns += take
				track_upkeep(take)
				if a.crowns < upkeep:
					a.roster.resize(int(a.roster.size() * (1.0 - Globals.LORD_DESERTION_RATE)))

func _process_player_contract():
	if player.active_contract.has("daily_wage"):
		var wage = player.active_contract["daily_wage"]
		var f_id = player.active_contract.get("faction_id", "")
		var f_data = get_faction(f_id)
		
		if f_data and f_data.treasury >= wage:
			f_data.treasury -= wage
			player.crowns += wage
			add_log("Mercenary Pay: Received %d Crowns from %s." % [wage, f_data.name])
			
			var h = player.service_history.get(f_id, 0)
			player.service_history[f_id] = h + 1
		else:
			add_log("Mercenary Pay: %s failed to pay your daily wage!" % (f_data.name if f_data else "Faction"))
		
		if day >= player.active_contract.get("expires_day", 999999):
			add_log("Contract Expired: Your service with %s has ended." % (f_data.name if f_data else "Faction"))
			player.active_contract = {}

func _process_settlement_updates():
	"""STAGGERED SETTLEMENT UPDATES - Use hash for better distribution"""
	for pos in settlements:
		var pos_hash = (pos.x * 73856093) ^ (pos.y * 19349663)  # Spatial hash function
		if abs(pos_hash) % Globals.TURNS_PER_DAY == hour:
			process_settlement_pulse(settlements[pos])

func _process_food_consumption():
	var consumption = max(1, player.roster.size() + 1)
	player.provisions -= consumption
	if player.provisions < 0:
		player.provisions = 0
		player.morale = max(0, player.morale - 10)
		add_log("Starvation! Morale drops.")
	else:
		process_daily_healing()

func _process_hourly_logic():
	for pos in settlements:
		var s = settlements[pos]
		if not s.construction_queue.is_empty():
			SettlementManager.process_construction(s)

	# Healing
	var heal_rate = 0.5 if player.provisions > 0 else 0.1
	_heal_unit(player.commander, heal_rate * 0.5)
	for u in player.roster: _heal_unit(u, heal_rate * 0.5)

func _process_battles():
	for i in range(ongoing_battles.size() - 1, -1, -1):
		var b = ongoing_battles[i]
		b.process_turn(self)
		if b.is_finished:
			ongoing_battles.remove_at(i)

func _process_commissions():
	for i in range(player.commissions.size() - 1, -1, -1):
		var c = player.commissions[i]
		c["remaining_turns"] -= 1
		if c["remaining_turns"] <= 0:
			for j in range(c["count"]): 
				player.stash.append(c["item_data"].duplicate())
			player.commissions.remove_at(i)
			add_log("Commission delivered.")

func process_settlement_pulse(s: GDSettlement):
	if grid.is_empty(): return
	
	EconomyManager.process_daily_pulse(self, s)
	SettlementManager.refresh_npcs(s)
	SettlementManager.refresh_quests(s)
	
	# --- PASSIVE TAXATION SYSTEM ---
	var daily_tax = EconomyManager.get_daily_tax(s)
	if daily_tax > 0:
		var f_data = get_faction(s.faction)
		var land_lord = null
		var is_player_fief = (s.lord_id == player.id)
		
		if not is_player_fief:
			land_lord = find_npc(s.lord_id) if s.lord_id != "" else null
		
		if is_player_fief:
			var s_cut = int(daily_tax * 0.5)
			var l_cut = int(daily_tax * 0.3)
			var f_cut = daily_tax - s_cut - l_cut
			s.crown_stock += s_cut
			player.crowns += l_cut
			if f_data: f_data.treasury += f_cut
			track_tax(daily_tax)
		elif land_lord:
			var s_cut = int(daily_tax * 0.5)
			var l_cut = int(daily_tax * 0.3)
			var f_cut = daily_tax - s_cut - l_cut
			
			s.crown_stock += s_cut
			land_lord.crowns += l_cut
			if f_data: f_data.treasury += f_cut
			track_tax(daily_tax)
		else:
			var s_cut = int(daily_tax * 0.8)
			var f_cut = daily_tax - s_cut
			s.crown_stock += s_cut
			if f_data: f_data.treasury += f_cut
			track_tax(daily_tax)

	if s.type != "hamlet":
		SettlementManager.refresh_shop(s)
		SettlementManager.refresh_recruits(s)
		SettlementManager.process_governor_ai(s)
		SettlementManager.process_logistics_ai(s)
		SettlementManager.check_promotions(s)
		
	# --- TOURNAMENT SYSTEM ---
	_process_tournaments(s)

func _process_tournaments(s: GDSettlement):
	if s.tournament_active:
		s.tournament_days_left -= 1
		if s.tournament_days_left <= 0:
			s.tournament_active = false
			add_log("The Tournament at %s has concluded." % s.name)
	elif s.type in ["town", "city", "metropolis"] and randf() < 0.05:
		s.tournament_active = true
		s.tournament_days_left = 3
		s.tournament_prize_pool = 1000 + (randi() % 2000)
		s.tournament_participants = []
		for npc in s.npcs:
			if s.tournament_participants.size() < 12:
				s.tournament_participants.append(npc.id)
		add_log("[color=yellow]TOURNAMENT:[/color] A grand tournament has begun in %s!" % s.name)

# === BATTLE ENDED HANDLER ===

func _on_battle_ended(result):
	# Handle post-battle cleanup
	pass

# === A* PATHFINDING ===

func rebuild_astar():
	"""Rebuild the A* pathfinding grid after loading a world"""
	if not astar:
		push_error("rebuild_astar: astar object is null")
		return
		
	# Configure A* grid
	astar.region = Rect2i(0, 0, width, height)
	astar.cell_size = Vector2(1, 1)
	astar.diagonal_mode = AStarGrid2D.DIAGONAL_MODE_NEVER
	astar.update()
	
	# Set terrain-based weights
	for y in range(height):
		for x in range(width):
			var t = grid[y][x]
			var weight = 1.0
			if t == '#': weight = 3.0 # Forest
			elif t == '&': weight = 5.0 # Jungle/Swamp
			elif t == '"': weight = 2.0 # Desert
			elif t == '*': weight = 1.2 # Tundra
			elif t == 'o': weight = 2.5 # Hills
			elif t == 'O': weight = 7.0 # Peaks
			elif t == '^': weight = 15.0 # Mountain
			elif t == '~': 
				astar.set_point_solid(Vector2i(x, y), true)
				continue
			elif t == '≈': weight = 8.0 # Large River
			elif t == '/': weight = 4.0 # Small River
			elif t == '\\': weight = 4.0
			elif t == '=': weight = 0.5 # Roads
			
			astar.set_point_weight_scale(Vector2i(x, y), weight)
