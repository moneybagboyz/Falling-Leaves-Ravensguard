class_name SiegeData
extends RefCounted

# Siege engines, fortifications, and siege warfare data
# Extracted from GameData.gd for better modularity

const SIEGE_ENGINES = {
	"ballista": {
		"name": "Ballista",
		"symbol": "X",
		"dmg_base": 80,
		"dmg_type": "pierce",
		"weight": 5.0, # Bolt weight
		"velocity": 8.0,
		"contact": 1,
		"penetration": 100, # Bypasses almost all armor
		"accuracy": 0.85,
		"range": 50,
		"reload_turns": 15,
		"aoe": 0,
		"overpenetrate": true,
		"is_mobile": true,
		"crew_required": 2,
		"footprint": [Vector2i(0,0)] # 1x1
	},
	"catapult": {
		"name": "Catapult",
		"symbol": "C",
		"dmg_base": 200,
		"dmg_type": "blunt",
		"weight": 25.0, # Stone weight
		"velocity": 4.0,
		"contact": 50,
		"penetration": 5,
		"accuracy": 0.45,
		"range": 70,
		"reload_turns": 35,
		"aoe": 1, # 1 tile radius (3x3 square)
		"is_mobile": true,
		"crew_required": 4,
		"footprint": [Vector2i(0,0), Vector2i(1,0), Vector2i(0,1), Vector2i(1,1)] # 2x2
	},
	"battering_ram": {
		"name": "Battering Ram",
		"symbol": "R",
		"dmg_base": 150, # Deals structural damage to gates
		"dmg_type": "blunt",
		"weight": 100.0,
		"velocity": 1.0, # Momentum-based ramming
		"contact": 100,
		"penetration": 2,
		"accuracy": 1.0,
		"range": 1,
		"reload_turns": 5,
		"aoe": 0,
		"is_mobile": true,
		"crew_required": 6,
		"protection_bonus": 0.8, # 80% damage reduction for crew
		"footprint": [Vector2i(0,0), Vector2i(1,0), Vector2i(2,0), Vector2i(0,1), Vector2i(1,1), Vector2i(2,1)] # 3x2
	},
	"siege_tower": {
		"name": "Siege Tower",
		"symbol": "S",
		"dmg_base": 0,
		"dmg_type": "none",
		"is_mobile": true,
		"crew_required": 8,
		"provides_wall_access": true,
		"protection_bonus": 0.9,
		"hp": 800,
		"footprint": [Vector2i(0,0), Vector2i(1,0), Vector2i(0,1), Vector2i(1,1), Vector2i(0,2), Vector2i(1,2)] # 2x3 high
	},
	"trebuchet": {
		"name": "Trebuchet",
		"symbol": "V",
		"dmg_base": 400,
		"dmg_type": "blunt",
		"weight": 150.0,
		"velocity": 5.0,
		"contact": 200,
		"penetration": 10,
		"accuracy": 0.35,
		"range": 120,
		"reload_turns": 60,
		"aoe": 3,
		"is_mobile": false, # Must be built on-site
		"crew_required": 10,
		"footprint": [Vector2i(0,0), Vector2i(1,0), Vector2i(2,0), Vector2i(0,1), Vector2i(1,1), Vector2i(2,1), Vector2i(0,2), Vector2i(1,2), Vector2i(2,2)] # 3x3 base
	}
}

static func get_siege_engines() -> Dictionary:
	return SIEGE_ENGINES

static func get_engine(engine_name: String) -> Dictionary:
	return SIEGE_ENGINES.get(engine_name, {})
