extends Node
# class_name GameState

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

# Use direct references to Globals to avoid constant expression issues during parsing

var grid: Array = [] # 2D array [y][x] - Array of Arrays
var width: int = 0
var height: int = 0
var resources: Dictionary = {} # Vector2i -> String (e.g. "iron", "gold")
var geology: Dictionary = {} # Vector2i -> Dict (temp, rain, layers)
var settlements: Dictionary = {} # Vector2i -> GDSettlement
var ruins: Dictionary = {} # Vector2i -> Dict
var armies: Array[GDArmy] = []
var ongoing_battles: Array[GDBattle] = []
var caravans: Array[GDCaravan] = []
var trade_contracts: Array[Dictionary] = [] # {id, seller_pos, buyer_pos, resource, amount, price, status}
var military_campaigns: Array[Dictionary] = [] # {id, faction, target_pos, type, participants: [], status}
var logistical_pulses: Array[Dictionary] = [] # {target_pos, resource, amount, arrival_turn, origin_pos}
var migrants: Array[Dictionary] = [] # Population Movement
var astar = AStarGrid2D.new()

# World Identity
var world_seed: int = 0
var world_name: String = "Unknown Land"

# Region Data
var region_ctrl = null # Set when Region view active

# City Studio Data
var city_studio_config = {
	"type": "city",
	"size": 200,
	"walls": "stone",
	"rivers": false,
	"pop": 5000,
	"seed": 12345
}
var city_studio_idx = 0

# Province/Political Data
var province_grid = []
var provinces = {}
var map_mode = "terrain" # "terrain", "political", "province", "resource"
var render_mode = "grid" # "ascii", "grid"
var graphical_mode_active = false

# Travel Mode Data
enum TravelMode { FAST, REGION, LOCAL }
var travel_mode = TravelMode.FAST

# --- SECTOR PAGING CONSTANTS ---
const WORLD_TILE_SIZE = 1000.0 # Meters per side
const METERS_PER_LOCAL_TILE = 2.0 # Tactical scale (DF standard)
const SECTOR_SIZE_METERS = 1000.0 # 500 tiles * 2m (Matches World Tile)
const PAGING_THRESHOLD = 50.0 # Regenerate if player moves too far from center

var local_offset = Vector2(500.0, 500.0) # Meters within current world tile
var last_gen_offset = Vector2(-999, -999) # Tracking for re-generation

const LOCAL_GRID_W = 500 # Matches BattleController.MAP_W
const LOCAL_GRID_H = 500 # Matches BattleController.MAP_H
var local_step_count = 0 

# Faction Definitions
var factions: Array[GDFaction] = []

var player: GDPlayer
var active_quests: Array[GDQuest] = []
var event_log: Array[String] = []
var history: Array[Dictionary] = [] # {turn, day, month, year, text}
var active_ruin_pos: Vector2i = Vector2i(-1, -1)
var is_resting = false
# var rng = RandomNumberGenerator.new() -- Moved to existing declaration down below
# var total_battles = 0 -- Moved to existing declaration down below

# --- FAUNA SYSTEM (DATA MOVED TO res://data/fauna_table.json) ---
# Use FaunaData.get_fauna_table() or FaunaData.get_fauna_for_biome(biome)
var killed_fauna: Dictionary = {} # Vector2i (world) -> Array of Vector2i (local)

var is_turbo = false
var monthly_ledger = {}
var rng = RandomNumberGenerator.new()
var turn = 0
var hour = 0
var day = 1

# --- SPATIAL HASHING ---
const SPATIAL_CELL_SIZE = 10
var spatial_grid: Dictionary = {} # Vector2i (cell) -> Array of Entities

# Statistics and Tracking
var world_market_orders = [] # Array of {buyer_pos, resource, amount, price_offered, faction}
var total_battles = 0
var total_sieges = 0
var total_captures = 0
var total_caravan_raids = 0
var total_trade_volume = 0 # Crowns traded this month

# --- PERFORMANCE CACHE ---
var distance_cache: Dictionary = {} # Vector2i_pair_key -> float

func get_party_size() -> int:
	var count = 1 # Commander
	for u_obj in player.roster:
		if not u_obj.status.get("is_dead", false):
			count += 1
	return count

func get_max_weight() -> float:
	# Base 100kg + 20kg per unit in roster
	var base = 100.0
	base += player.roster.size() * 20.0
	
	# Add bonus from transport items in stash
	for item in player.stash:
		if typeof(item) == TYPE_DICTIONARY:
			base += item.get("capacity_bonus", 0.0)
			
	return base

func get_total_weight() -> float:
	var total = 0.0
	
	# Inventory weight (Resource stocks like grain, iron)
	for k in player.inventory:
		total += player.inventory[k] * 0.5 # 0.5kg per unit of resource
	
	# Stash (Items in the party inventory)
	for item in player.stash:
		total += item.get("weight", 1.0)
	
	# Equipped gear (Commander)
	total += get_unit_equipment_weight(player.commander)
	
	# Equipped gear (Roster)
	for u_obj in player.roster:
		total += get_unit_equipment_weight(u_obj)
		
	return total

func get_unit_equipment_weight(u_obj: GDUnit) -> float:
	var w = 0.0
	var eq = u_obj.equipment
	if eq.get("main_hand"): w += eq["main_hand"].get("weight", 0.0)
	if eq.get("off_hand"): w += eq["off_hand"].get("weight", 0.0)
	
	for slot in ["head", "torso", "l_arm", "r_arm", "l_hand", "r_hand", "l_leg", "r_leg", "l_foot", "r_foot"]:
		var s = eq.get(slot)
		if s:
			if s.get("under"): w += s["under"].get("weight", 0.0)
			if s.get("over"): w += s["over"].get("weight", 0.0)
			if s.get("armor"): w += s["armor"].get("weight", 0.0)
			if s.get("cover"): w += s["cover"].get("weight", 0.0)
	return w

func _ready():
	player = GDPlayer.new()
	# Initialize Factions
	var faction_data = {
		"player": ["Player's Band", 1000, "yellow"],
		"royalists": ["The Royalist League", 5000, "blue"],
		"mercenaries": ["Iron Mercenary Company", 5000, "red"],
		"merchants": ["Coster's Trade Guild", 5000, "green"],
		"church": ["Order of the Sacred Flame", 5000, "white"],
		"commonwealth": ["The Free Commonwealth", 5000, "purple"],
		"bandits": ["Outlaws", 0, "gray"],
		"neutral": ["Independent", 0, "white"]
	}
	for f_id in faction_data:
		var d = faction_data[f_id]
		factions.append(GDFaction.new(f_id, d[0], d[1], d[2]))

	rng.randomize()
	print("World Seed: ", rng.seed)
	# init_world called manually from menu
	
	player.commander.body = GameData.get_default_body(1.5)
	player.commander.hp_max = GameData.get_total_hp(player.commander.body)
	player.commander.hp = player.commander.hp_max
	player.commander.blood_max = 500.0
	player.commander.blood_current = 500.0
	player.commander.bleed_rate = 0.0
	player.commander.status["is_prone"] = false
	player.commander.is_hero = true
	
	# Hero Attributes
	player.commander.attributes = {
		"strength": 16,
		"endurance": 16,
		"agility": 16,
		"balance": 16,
		"pain_tolerance": 18
	}
	# Hero Skills
	player.commander.skills = {
		"swordsmanship": 40,
		"axe_fighting": 20,
		"spear_use": 20,
		"mace_hammer": 20,
		"dagger_knife": 20,
		"improvised": 10,
		"shield_use": 35,
		"dodging": 30,
		"armor_handling": 25,
		"archery": 15,
		"crossbows": 10
	}
	
	connect("battle_ended", _on_battle_ended)
	
	# Initial equipment and roster are now handled by the Character Creator in _confirm_embark
	# (See Main.gd for logic)
	
	# player.commander.speed = GameData.calculate_unit_speed(player.commander)
	# player.commander.base_speed = player.commander.speed

func create_item(type_key, material_key, quality = "standard"):
	return EconomyManager.create_item(type_key, material_key, quality)



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

# --- Blueprint & Commission Logic ---

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

func set_relation(f1, f2, status):
	get_faction(f1).relations[f2] = status
	get_faction(f2).relations[f1] = status
	if status == "war":
		# Only log major wars
		if f1 != "bandits" and f2 != "bandits":
			add_log("WAR! %s declared war on %s!" % [get_faction(f1).name, get_faction(f2).name])

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

func init_world(config: Dictionary = {}):
	# Clear previous game state
	player = GDPlayer.new()
	history.clear()
	event_log.clear()
	day = 1
	turn = 0
	hour = 0
	
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
	armies = data["armies"]
	caravans = data["caravans"]
	province_grid = data.get("province_grid", [])
	provinces = data.get("provinces", {})
	
	# Capture dynamic factions
	if data.has("factions"):
		factions = data["factions"]
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

func add_log(msg: String):
	# Core Game Log (UI)
	event_log.append("[Turn %d] %s" % [turn, msg])
	if event_log.size() > 500: # Slightly larger buffer for history
		event_log.pop_front()
	
	if not is_turbo:
		emit_signal("log_updated")
	
	# Internal Event Tracking for Report (Optional critical events only)
	if is_turbo:
		if msg.contains("Starvation!") or msg.contains("at WAR"):
			if monthly_ledger.has("events"):
				monthly_ledger["events"].append(msg)

func add_history_event(msg: String):
	var d_idx = day - 1
	var year = int(float(d_idx) / (Globals.DAYS_PER_MONTH * Globals.MONTHS_PER_YEAR)) + 1
	var month_idx = int(float(d_idx) / Globals.DAYS_PER_MONTH) % Globals.MONTHS_PER_YEAR
	var m_day = (d_idx % Globals.DAYS_PER_MONTH) + 1
	
	history.append({
		"turn": turn,
		"day": m_day,
		"month": GameData.MONTH_NAMES[month_idx],
		"year": year,
		"text": msg
	})
	# If also want it in the current log
	add_log("[color=orange][HISTORY][/color] " + msg)

func get_date_string() -> String:
	var d_idx = day - 1
	var year = int(float(d_idx) / (Globals.DAYS_PER_MONTH * Globals.MONTHS_PER_YEAR)) + 1
	var month_idx = int(float(d_idx) / Globals.DAYS_PER_MONTH) % Globals.MONTHS_PER_YEAR
	var m_day = (d_idx % Globals.DAYS_PER_MONTH) + 1
	
	return "Day %d of %s, Year %d (%02d:00)" % [m_day, GameData.MONTH_NAMES[month_idx], year, hour]

func get_time_of_day() -> String:
	if hour >= 6 and hour < 20: return "Day"
	return "Night"

func is_night() -> bool:
	return hour < 6 or hour >= 20

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
			land_lord.crowns += l_cut # Unified: NPC and Army now use Lord wealth Record
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

func get_tile_type(pos: Vector2i) -> String:
	if pos.y < 0 or pos.y >= height or pos.x < 0 or pos.x >= width:
		return "water"
	var t = grid[pos.y][pos.x]
	match t:
		'#': return "forest"
		'&': return "jungle"
		'"': return "desert"
		'*': return "tundra"
		'o': return "hills"
		'O': return "peaks"
		'^': return "mountain"
		'~', '≈': return "water"
		'=', '/', '\\': return "road"
	return "plains"

func advance_time():
	turn += 1
	hour = (hour + 1) % Globals.TURNS_PER_DAY
	
	# Update spatial hash at the start of every hour for AI lookups
	update_spatial_grid()
	
	# Update World Market and Trade Contracts
	EconomyManager.update_trade_networks(self)
	
	# SIEGE LOGIC: Advance timers and handle abandonment
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
				# This allows for a 12-turn "Grace Period" if a lord moves or respawns.
				s.siege_timer = max(0, s.siege_timer - 2) # Ticks down twice as fast as it ticks up
				if s.siege_timer <= 0:
					s.is_under_siege = false
					s.siege_attacker_faction = ""
					add_log("The siege of %s has been abandoned." % s.name)
	
	if hour == 0:
		day += 1
		add_log("--- Day %d ---" % day)
		
		SettlementManager.process_migration(self)
		AIManager.spawn_bandit_party(self)
		WarManager.process_diplomacy(self)
		
		# --- FACTION STIPENDS ---
		# Distribute 20% of faction treasury back to lords and settlements daily
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
					if s.faction == f.id: f_settlements.append(s)
				
				if not f_lords.is_empty() or not f_settlements.is_empty():
					var share = payout / (f_lords.size() + f_settlements.size())
					for l in f_lords: l.crowns += share
					for s in f_settlements: s.crown_stock += share

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
		
		_process_player_contract()
		
		if player.founding_timer > 0:
			player.founding_timer -= 1
			if player.founding_timer == 0:
				SettlementManager.finalize_player_settlement(self, player.founding_pos, player.founding_type)
				player.founding_pos = Vector2i(-1, -1)
				player.founding_type = ""

		if not is_turbo and day % Globals.DAYS_PER_MONTH == 0:
			run_world_audit()

	# STAGGERED SETTLEMENT UPDATES (Optimization 2)
	# Use hash for better distribution than (x+y) % TURNS_PER_DAY
	for pos in settlements:
		var pos_hash = (pos.x * 73856093) ^ (pos.y * 19349663)  # Spatial hash function
		if abs(pos_hash) % Globals.TURNS_PER_DAY == hour:
			process_settlement_pulse(settlements[pos])

	# 1. Party Food Consumption (Twice a day)
	if hour == 8 or hour == 20:
		var consumption = max(1, player.roster.size() + 1)
		player.provisions -= consumption
		if player.provisions < 0:
			player.provisions = 0
			player.morale = max(0, player.morale - 10)
			add_log("Starvation! Morale drops.")
		else:
			process_daily_healing()
	
	# 2. Hourly Logic (Movement, Construction, Healing)
	for pos in settlements:
		var s = settlements[pos]
		if not s.construction_queue.is_empty():
			SettlementManager.process_construction(s)

	# Healing
	var heal_rate = 0.5 if player.provisions > 0 else 0.1
	_heal_unit(player.commander, heal_rate * 0.5)
	for u in player.roster: _heal_unit(u, heal_rate * 0.5)

	# BATTLES
	for i in range(ongoing_battles.size() - 1, -1, -1):
		var b = ongoing_battles[i]
		b.process_turn(self)
		if b.is_finished:
			ongoing_battles.remove_at(i)

	# Commissions
	for i in range(player.commissions.size() - 1, -1, -1):
		var c = player.commissions[i]
		c["remaining_turns"] -= 1
		if c["remaining_turns"] <= 0:
			for j in range(c["count"]): 
				player.stash.append(c["item_data"].duplicate())
			player.commissions.remove_at(i)
			add_log("Commission delivered.")

	AIManager.process_movement(self)
	if not is_turbo:
		emit_signal("map_updated")


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

func get_faction(id: String) -> GDFaction:
	return FactionManager.get_faction(id, factions)

func find_npc(npc_id: String) -> GDNPC:
	for pos in settlements:
		var s = settlements[pos]
		for npc in s.npcs:
			if npc.id == npc_id:
				return npc
	return null

func get_province_at(pos: Vector2i) -> Dictionary:
	if province_grid.size() > pos.y and province_grid[pos.y].size() > pos.x:
		var p_id = province_grid[pos.y][pos.x]
		return provinces.get(p_id, {})
	return {}

func get_province_owner_faction(p_id: int) -> String:
	var p = provinces.get(p_id)
	if p and p.has("owner"):
		return p.owner
	return "neutral"

func get_entity_name(entity):
	return FactionManager.get_entity_name(entity, factions)

func is_in_bounds(pos: Vector2i) -> bool:
	return pos.x >= 0 and pos.x < width and pos.y >= 0 and pos.y < height

func get_true_terrain(pos: Vector2i) -> String:
	"""Get actual terrain type, using geology to override settlement/road tiles"""
	if not is_in_bounds(pos): return "~"
	var t = grid[pos.y][pos.x]
	# Use geology to find underlying terrain if grid is an overlay (Roads, Towns, etc.)
	if geology.has(pos) and t in ["=", "T", "C", "v", "h", "k", "?"]:
		return geology[pos].get("biome", t)
	return t

func is_walkable(pos: Vector2i) -> bool:
	if not is_in_bounds(pos): return false
	if settlements.has(pos): return true # Can always visit settlements
	var t = grid[pos.y][pos.x]
	return t != '~' and t != '^'

# --- SPATIAL GRID METHODS ---

func update_spatial_grid():
	spatial_grid.clear()
	# Add Armies
	for a in armies:
		_add_to_spatial(a)
	# Add Caravans
	for c in caravans:
		_add_to_spatial(c)
	# Add Migrants
	for m in migrants:
		if m is Dictionary: # Migrants are sometimes dicts
			_add_to_spatial(m)
	# Add player
	_add_to_spatial(player)

func _add_to_spatial(entity):
	var pos = entity.pos if "pos" in entity else Vector2i(-1, -1)
	if pos == Vector2i(-1, -1): return
	var cell = pos / SPATIAL_CELL_SIZE
	if not spatial_grid.has(cell):
		spatial_grid[cell] = []
	spatial_grid[cell].append(entity)

func get_entities_near(pos: Vector2i, radius: int) -> Array:
	var result = []
	var cell_pos = pos / SPATIAL_CELL_SIZE
	var cell_radius = int(ceil(float(radius) / SPATIAL_CELL_SIZE))
	
	for dy in range(-cell_radius, cell_radius + 1):
		for dx in range(-cell_radius, cell_radius + 1):
			var cell = cell_pos + Vector2i(dx, dy)
			if spatial_grid.has(cell):
				for entity in spatial_grid[cell]:
					# Use Chebyshev distance as it's faster for grid games
					var dist = max(abs(entity.pos.x - pos.x), abs(entity.pos.y - pos.y))
					if dist <= radius:
						result.append(entity)
	return result

func get_entity_at(pos: Vector2i):
	# Prioritize dynamic entities over static ones for inspection
	var nearby = get_entities_near(pos, 0)
	for e in nearby:
		if not (e is Dictionary) and e == player: return {"type": "player", "data": e}
		if e is GDArmy and e in armies: return {"type": "army", "data": e}
		if e is GDCaravan and e in caravans: return {"type": "caravan", "data": e}
		# Handle migrant dicts
		if e is Dictionary and migrants.has(e): return {"type": "migrants", "data": e}
	
	for s_pos in settlements:
		if s_pos == pos: return {"type": "settlement", "data": settlements[s_pos]}
	
	if ruins.has(pos):
		return {"type": "ruin", "data": ruins[pos]}
		
	return null

func erase_army(army):
	# Check for quest targets
	for q in active_quests:
		if q.type == GDQuest.Type.EXTERMINATE and q.objective_data.get("target_id") == army.get_instance_id():
			q.status = GDQuest.Status.COMPLETED
			add_log("[color=green]QUEST COMPLETE: %s[/color]" % q.title)
	
	armies.erase(army)

func _on_battle_ended(win):
	if win and active_ruin_pos != Vector2i(-1, -1):
		# Check for quest targets (Ruins)
		for q in active_quests:
			if q.type == GDQuest.Type.EXTERMINATE and q.target_pos == active_ruin_pos:
				q.status = GDQuest.Status.COMPLETED
				add_log("[color=green]QUEST COMPLETE: %s[/color]" % q.title)
		
		AIManager._reward_ruin(self, active_ruin_pos)
	active_ruin_pos = Vector2i(-1, -1)

# --- AUDIT & SIMULATION TOOLS ---

func run_world_audit():
	WorldAudit.run_audit(self)

func run_turbo_simulation():
	WorldAudit.run_turbo_simulation(self)

func run_annual_simulation():
	WorldAudit.run_annual_simulation(self)

func get_total_population() -> int:
	var total = 0
	for pos in settlements:
		total += settlements[pos].population
	return total

func track_production(res, amount):
	if not is_turbo: return
	var p = monthly_ledger["production"]
	p[res] = p.get(res, 0) + amount

func track_consumption(res, amount):
	if not is_turbo: return
	var c = monthly_ledger["consumption"]
	c[res] = c.get(res, 0) + amount

func track_idle(building):
	if not is_turbo: return
	monthly_ledger["idle_buildings"][building] = monthly_ledger["idle_buildings"].get(building, 0) + 1

func track_starvation(deaths):
	if not is_turbo: return
	monthly_ledger["starvation_deaths"] = monthly_ledger.get("starvation_deaths", 0) + deaths

func track_migration(amount):
	if not is_turbo: return
	monthly_ledger["migration_net"] = monthly_ledger.get("migration_net", 0) + amount

func track_tax(amount):
	if not is_turbo: return
	monthly_ledger["tax_collected"] = monthly_ledger.get("tax_collected", 0) + amount

func track_upkeep(amount):
	if not is_turbo: return
	monthly_ledger["upkeep_paid"] = monthly_ledger.get("upkeep_paid", 0) + amount

func track_births(amount):
	if not is_turbo: return
	monthly_ledger["births"] = monthly_ledger.get("births", 0) + amount

func track_war_deaths(amount):
	if not is_turbo: return
	monthly_ledger["deaths_war"] = monthly_ledger.get("deaths_war", 0) + amount

func track_war_event(type: String, actor: String, victim: String):
	# Track major war events for historical ledger
	var event_msg = ""
	match type:
		"capture":
			event_msg = "%s has captured a territory from %s!" % [actor, victim]
			add_log(event_msg)
		"defeat":
			event_msg = "%s was defeated in battle by %s!" % [victim, actor]
	
	if event_msg != "":
		add_history_event(event_msg)

func track_trade_volume(amount):
	total_trade_volume += amount
	if not is_turbo: return
	monthly_ledger["trade_volume"] = monthly_ledger.get("trade_volume", 0) + amount

func track_logistical_pulse(type):
	if not is_turbo: return
	# type: "generated", "delivered", "dropped"
	var key = "pulses_" + type
	monthly_ledger[key] = monthly_ledger.get(key, 0) + 1

func track_buy_order(type):
	if not is_turbo: return
	# type: "placed", "fulfilled"
	var key = "buy_orders_" + type
	monthly_ledger[key] = monthly_ledger.get(key, 0) + 1
