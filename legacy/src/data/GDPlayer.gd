class_name GDPlayer
extends RefCounted

var type: String = "player"
var pos: Vector2i = Vector2i.ZERO # MUST BE Vector2i
var camera_offset: Vector2i = Vector2i.ZERO
var gold: int = 1000
var crowns: int = 100
var renown: int = 0
var fame: int = 0
var faction: String = "player"
var commander: GDUnit
var roster: Array = [] # Array of GDUnit

# Cached values - invalidated when roster changes
var _cached_troop_count: int = 0
var _cached_strength: int = 0
var _cache_valid: bool = false

var troop_count: int:
	get:
		if not _cache_valid:
			_recalculate_stats()
		return _cached_troop_count

var strength: int:
	get:
		if not _cache_valid:
			_recalculate_stats()
		return _cached_strength

func _recalculate_stats():
	# Troop count
	_cached_troop_count = roster.size()
	if commander: _cached_troop_count += 1
	
	# Strength
	_cached_strength = 0
	var full_list = roster.duplicate()
	if commander: full_list.append(commander)
	
	for u in full_list:
		var base = 10
		var t = u.tier
		match t:
			2: base = 30
			3: base = 80
			4: base = 200
			5: base = 500
		var hp_ratio = float(u.hp) / max(1.0, float(u.hp_max))
		_cached_strength += int(base * hp_ratio)
	
	_cache_valid = true

# Call this whenever roster changes
func invalidate_cache():
	_cache_valid = false

# Helper functions that automatically invalidate cache
func add_to_roster(unit: GDUnit):
	roster.append(unit)
	invalidate_cache()

func remove_from_roster(unit: GDUnit):
	var idx = roster.find(unit)
	if idx >= 0:
		roster.remove_at(idx)
		invalidate_cache()

func clear_roster():
	roster.clear()
	invalidate_cache()

var stash: Array = [] # Array of Dictionary (Items)
var unit_classes: Dictionary = {}
var commissions: Array = []
var inventory: Dictionary = {}
var prisoners: Array = []
var provisions: int = 500
var morale: int = 100

# Mercenary & Feudal System
var id: String = "player_hero"
var active_contract: Dictionary = {} # {employer_id, faction_id, daily_wage, expires_day}
var service_history: Dictionary = {} # faction_id -> total_days_served
var fief_ids: Array[Vector2i] = [] # Positions of settlements owned by the player

# Tournament State
var tournament_round: int = 0
var tournament_bet: int = 0

# Founding State
var founding_timer: int = 0
var founding_pos: Vector2i = Vector2i(-1, -1)
var founding_type: String = ""
var charters: int = 0 # Number of royal permits to found settlements

# Camera & View
var camera_zoom: float = 1.0
var free_cam_mode: bool = false

func _init():
	commander = GDUnit.new("Commander")
	commander.type = "commander"
	commander.xp = 1000

