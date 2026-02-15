class_name EntityRegistry
extends RefCounted

const GDSettlement = preload("res://src/data/GDSettlement.gd")
const GDArmy = preload("res://src/data/GDArmy.gd")
const GDBattle = preload("res://src/data/GDBattle.gd")
const GDCaravan = preload("res://src/data/GDCaravan.gd")
const GDFaction = preload("res://src/data/GDFaction.gd")

# Entity Collections
var settlements: Dictionary = {} # Vector2i -> GDSettlement
var armies: Array[GDArmy] = []
var ongoing_battles: Array[GDBattle] = []
var caravans: Array[GDCaravan] = []
var factions: Array[GDFaction] = []

# Economic & Military Systems
var trade_contracts: Array[Dictionary] = [] # {id, seller_pos, buyer_pos, resource, amount, price, status}
var military_campaigns: Array[Dictionary] = [] # {id, faction, target_pos, type, participants: [], status}
var logistical_pulses: Array[Dictionary] = [] # {target_pos, resource, amount, arrival_turn, origin_pos}
var migrants: Array[Dictionary] = [] # Population Movement
var world_market_orders: Array = [] # Array of {buyer_pos, resource, amount, price_offered, faction}

# Statistics
var total_battles: int = 0
var total_sieges: int = 0
var total_captures: int = 0
var total_caravan_raids: int = 0
var total_trade_volume: int = 0 # Crowns traded this month

func _init():
	_initialize_factions()

func _initialize_factions():
	"""Initialize default factions"""
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

func clear():
	"""Reset all entities"""
	settlements.clear()
	armies.clear()
	ongoing_battles.clear()
	caravans.clear()
	trade_contracts.clear()
	military_campaigns.clear()
	logistical_pulses.clear()
	migrants.clear()
	world_market_orders.clear()
	total_battles = 0
	total_sieges = 0
	total_captures = 0
	total_caravan_raids = 0
	total_trade_volume = 0

func get_settlement(pos: Vector2i) -> GDSettlement:
	"""Get settlement at position"""
	return settlements.get(pos)

func add_settlement(pos: Vector2i, settlement: GDSettlement):
	"""Add settlement at position"""
	settlements[pos] = settlement

func remove_settlement(pos: Vector2i):
	"""Remove settlement at position"""
	settlements.erase(pos)

func get_faction_by_id(faction_id: String) -> GDFaction:
	"""Get faction by ID string"""
	for faction in factions:
		if faction.id == faction_id:
			return faction
	return null

func get_faction_by_name(name: String) -> GDFaction:
	"""Get faction by name"""
	for faction in factions:
		if faction.name == name:
			return faction
	return null

func get_armies_at(pos: Vector2i) -> Array[GDArmy]:
	"""Get all armies at position"""
	var result: Array[GDArmy] = []
	for army in armies:
		if army.pos == pos:
			result.append(army)
	return result

func get_army_by_id(army_id: int) -> GDArmy:
	"""Get army by ID"""
	for army in armies:
		if army.id == army_id:
			return army
	return null

func add_army(army: GDArmy):
	"""Add army to registry"""
	armies.append(army)

func remove_army(army: GDArmy):
	"""Remove army from registry"""
	armies.erase(army)

func get_caravan_at(pos: Vector2i) -> GDCaravan:
	"""Get caravan at position"""
	for caravan in caravans:
		if caravan.pos == pos:
			return caravan
	return null

func add_caravan(caravan: GDCaravan):
	"""Add caravan to registry"""
	caravans.append(caravan)

func remove_caravan(caravan: GDCaravan):
	"""Remove caravan from registry"""
	caravans.erase(caravan)

func get_settlements_by_faction(faction_id: String) -> Array:
	"""Get all settlements belonging to faction"""
	var result = []
	for pos in settlements:
		var s = settlements[pos]
		if s.faction_id == faction_id:
			result.append(s)
	return result

func get_battle_at(pos: Vector2i) -> GDBattle:
	"""Get ongoing battle at position"""
	for battle in ongoing_battles:
		if battle.pos == pos:
			return battle
	return null

func add_battle(battle: GDBattle):
	"""Add battle to registry"""
	ongoing_battles.append(battle)
	total_battles += 1

func remove_battle(battle: GDBattle):
	"""Remove battle from registry"""
	ongoing_battles.erase(battle)
