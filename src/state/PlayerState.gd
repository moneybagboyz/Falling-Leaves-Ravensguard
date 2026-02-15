class_name PlayerState
extends RefCounted

const GDPlayer = preload("res://src/data/GDPlayer.gd")
const GDQuest = preload("res://src/data/GDQuest.gd")
const GDUnit = preload("res://src/data/GDUnit.gd")
# GameData and EconomyManager are autoloads - no need to preload

# Player Data
var player: GDPlayer
var active_quests: Array[GDQuest] = []
var active_ruin_pos: Vector2i = Vector2i(-1, -1)
var is_resting: bool = false

func _init():
	player = GDPlayer.new()
	_initialize_player()

func _initialize_player():
	"""Initialize player with default stats"""
	# Create commander if it doesn't exist
	if not player.commander:
		player.commander = GDUnit.new("Hero")
	
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

func clear():
	"""Reset player state"""
	player = GDPlayer.new()
	_initialize_player()
	active_quests.clear()
	active_ruin_pos = Vector2i(-1, -1)
	is_resting = false

func get_party_size() -> int:
	"""Get total party size including commander"""
	var count = 1 # Commander
	for u_obj in player.roster:
		if not u_obj.status.get("is_dead", false):
			count += 1
	return count

func get_max_weight() -> float:
	"""Calculate maximum carrying capacity"""
	var base = 100.0
	base += player.roster.size() * 20.0
	
	# Add bonus from transport items in stash
	for item in player.stash:
		if typeof(item) == TYPE_DICTIONARY:
			base += item.get("capacity_bonus", 0.0)
			
	return base

func get_total_weight() -> float:
	"""Calculate total weight carried by party"""
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
	"""Calculate total equipment weight for a unit"""
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

func create_item(type_key: String, material_key: String, quality: String = "standard"):
	"""Create item using economy system"""
	return EconomyManager.create_item(type_key, material_key, quality)

func add_to_inventory(resource: String, amount: int):
	"""Add resources to player inventory"""
	player.inventory[resource] = player.inventory.get(resource, 0) + amount

func remove_from_inventory(resource: String, amount: int) -> bool:
	"""Remove resources from player inventory, returns false if insufficient"""
	if player.inventory.get(resource, 0) < amount:
		return false
	player.inventory[resource] -= amount
	return true

func has_resource(resource: String, amount: int) -> bool:
	"""Check if player has enough of a resource"""
	return player.inventory.get(resource, 0) >= amount

func add_quest(quest: GDQuest):
	"""Add quest to active quests"""
	active_quests.append(quest)

func complete_quest(quest_id: String):
	"""Complete quest by ID"""
	for i in range(active_quests.size()):
		if active_quests[i].id == quest_id:
			active_quests.remove_at(i)
			return

func get_quest(quest_id: String) -> GDQuest:
	"""Get quest by ID"""
	for quest in active_quests:
		if quest.id == quest_id:
			return quest
	return null
