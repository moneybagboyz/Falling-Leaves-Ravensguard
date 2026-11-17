class_name GDArmy
extends GDEntity

var name: String
var provisions: int = 0
var home_fief: Vector2i = Vector2i(-1, -1)
var personalities: Array = ["balanced", "aggressive", "cautious"]
var personality: String = "balanced"
var doctrine: String = "defender"
var respawn_timer: int = 0
var cached_target = null

# Feudal/NPC Linkage
var lord_id: String = "" # ID of the GDNPC leading this army

func _init(_pos: Vector2i = Vector2i.ZERO, _faction: String = "neutral"):
	super(_pos, _faction)
	type = "army"
