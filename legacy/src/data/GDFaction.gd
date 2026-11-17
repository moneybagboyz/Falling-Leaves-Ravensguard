class_name GDFaction
extends RefCounted

var id: String
var name: String
var treasury: int = 0
var relations: Dictionary = {} # faction_id -> int
var color: String = "white"

# Feudal System
var king_id: String = "" # ID of the NPC who is the ruler
var vassal_ids: Array[String] = [] # IDs of all lords in this faction

func _init(_id: String, _name: String, _treasury: int = 5000, _color: String = "white"):
	id = _id
	name = _name
	treasury = _treasury
	color = _color
