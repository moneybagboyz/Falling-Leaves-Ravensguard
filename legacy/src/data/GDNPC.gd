class_name GDNPC
extends RefCounted

var id: String
var name: String
var title: String # "Governor", "Village Elder", "Merchant", "Priest"
var faction_id: String
var personality: String = "balanced"
var relationship: int = 0 # -100 to 100
var quests: Array = [] # Array of GDQuest
var settlement_pos: Vector2i
var crowns: int = 0

# Feudal System
var suzerain_id: String = "" # ID of the NPC they serve (King or High Lord)
var fief_ids: Array[Vector2i] = [] # Positions of settlements they own

func _init(_id: String, _name: String, _title: String, _pos: Vector2i, _faction_id: String = ""):
	id = _id
	name = _name
	title = _title
	settlement_pos = _pos
	faction_id = _faction_id
