class_name GDUnit
extends RefCounted

var name: String
var faction: String = "neutral"
var type: String = "recruit"
var archetype: String = ""
var tier: int = 1
var level: int = 1
var xp: int = 0
var stat_points: int = 0
var skill_points: int = 0
var is_hero: bool = false
var traits: Array = []
var attributes: Dictionary = {
	"strength": 10,
	"endurance": 10,
	"agility": 10,
	"intelligence": 10,
	"balance": 10,
	"pain_tolerance": 10
}
var skills: Dictionary = {
	"swordsmanship": 0,
	"axe_fighting": 0,
	"spear_use": 0,
	"mace_hammer": 0,
	"dagger_knife": 0,
	"shield_use": 0,
	"archery": 0,
	"crossbows": 0,
	"dodging": 0,
	"armor_handling": 0,
	"improvised": 0
}
var cost: int = 0
var body: Dictionary = {}
var hp: int = 10
var hp_max: int = 10
var fatigue: float = 0.0
var max_fatigue: float = 100.0
var morale: float = 1.0 
var blood_max: float = 500.0
var blood_current: float = 500.0
var bleed_rate: float = 0.0
var status: Dictionary = {"is_prone": false, "is_downed": false, "is_dead": false}
var facing: Vector2i = Vector2i.RIGHT
var formation_id: int = -1
var formation_offset: Vector2i = Vector2i.ZERO
var equipment: Dictionary = {
	"head": {"under": null, "over": null, "armor": null, "cover": null},
	"torso": {"under": null, "over": null, "armor": null, "cover": null},
	"l_arm": {"under": null, "over": null, "armor": null, "cover": null},
	"r_arm": {"under": null, "over": null, "armor": null, "cover": null},
	"l_hand": {"under": null, "over": null, "armor": null, "cover": null},
	"r_hand": {"under": null, "over": null, "armor": null, "cover": null},
	"l_leg": {"under": null, "over": null, "armor": null, "cover": null},
	"r_leg": {"under": null, "over": null, "armor": null, "cover": null},
	"l_foot": {"under": null, "over": null, "armor": null, "cover": null},
	"r_foot": {"under": null, "over": null, "armor": null, "cover": null},
	"main_hand": null,
	"off_hand": null,
	"ammo": null
}
var base_speed: float = 0.6
var speed: float = 0.6
var id: int = 0
var pos: Vector2i = Vector2i.ZERO
var team: String = "enemy"
var symbol: String = "i"

# Siege Engine specific data
var is_siege_engine: bool = false
var engine_type: String = "" # "ballista", "catapult", "battering_ram", "siege_tower", "trebuchet"
var crew_ids: Array = [] # IDs of units manning the engine
var assigned_engine_id: int = -1 # ID of the engine this unit is assigned to
var engine_stats: Dictionary = {} # Copy from GameData.SIEGE_ENGINES
var reload_timer: int = 0
var action_timer: float = 0.0
var footprint: Array = [] # Array of Vector2i offsets from center (pos)
var data_ref = null
var assigned_class: String = ""

# Performance Planning (Optimization 4)
var planned_action: String = "" # "move", "attack", "none"
var planned_target = null
var planned_pos: Vector2i = Vector2i.ZERO
var planned_target_pos: Vector2i = Vector2i.ZERO
var round_initiative: float = 0.0

func _init(_name: String = "Recruit"):
	name = _name
