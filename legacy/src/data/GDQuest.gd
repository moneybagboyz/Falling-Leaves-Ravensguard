class_name GDQuest
extends RefCounted

enum Type { FETCH, EXTERMINATE, DELIVERY, ESCORT, CONSTRUCTION }
enum Status { NOT_STARTED, ACTIVE, COMPLETED, FAILED }

var id: String
var title: String
var description: String
var type: Type
var status: Status = Status.NOT_STARTED

var origin_settlement: Vector2i
var target_pos: Vector2i # Destination or location of objective
var objective_data: Dictionary = {} # e.g. {"resource": "grain", "amount": 50} or {"enemy_type": "bandit"}

var rewards: Dictionary = {
	"crowns": 0,
	"influence": 0,
	"items": []
}

var expiration_turn: int = -1 # Turn number when quest expires

func _init(_title: String, _type: Type, _origin: Vector2i):
	title = _title
	type = _type
	origin_settlement = _origin
	# Avoid GameState reference in _init to prevent circular dependency
	id = str(Time.get_ticks_msec()) + "_" + str(randi())
