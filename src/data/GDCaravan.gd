class_name GDCaravan
extends GDEntity

var name: String = "Merchant Caravan"
var origin: Vector2i
var final_destination: Vector2i = Vector2i(-1, -1)
var target_resource: String = ""
var respawn_timer: int = 0

func _init(_pos: Vector2i = Vector2i.ZERO, _faction: String = "neutral"):
	super(_pos, _faction)
	type = "caravan"
