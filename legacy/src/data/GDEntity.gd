class_name GDEntity
extends RefCounted

var type: String
var pos: Vector2i
var faction: String
var roster: Array = []
var is_in_battle: bool = false
var troop_count: int:
	get:
		return roster.size()
var strength: int:
	get:
		var total = 0
		for u in roster:
			var base = 10
			var t = u.tier
			match t:
				2: base = 30
				3: base = 80
				4: base = 200
				5: base = 500
			# Factor in health
			var hp_ratio = float(u.hp) / max(1.0, float(u.hp_max))
			total += int(base * hp_ratio)
		return total
var crowns: int = 0
var renown: int = 0
var inventory: Dictionary = {}
var path: Array = []
var target_pos: Vector2i = Vector2i(-1, -1)
var state: String = "idle"

func _init(_pos: Vector2i = Vector2i.ZERO, _faction: String = "neutral"):
	pos = _pos
	faction = _faction

func move_to(_target: Vector2i):
	# Pathfinding logic will be handled by AIManager
	pass
