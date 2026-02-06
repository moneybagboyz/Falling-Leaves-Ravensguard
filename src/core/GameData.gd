extends Node

const MaterialsData = preload("res://src/data/MaterialsData.gd")
const NamesData = preload("res://src/data/NamesData.gd")

# --- MATERIALS (Dwarf Fortress Depth) ---
# Loaded from data/materials.json
static var MATERIALS: Dictionary:
	get:
		return MaterialsData.get_materials()

# --- SIEGE ENGINES & EQUIPMENT ---
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

# --- ITEMS ---
const ITEMS = {
	# --- WEAPONS ---
	"fist": {
		"type": "weapon", "dmg": 2, "dmg_type": "blunt", "contact": 5, "penetration": 1, "material": "flesh", "hands": 1, "volume": 0.5, "weight": 0.0, "name": "Fist",
		"attacks": [
			{"name": "Punch", "dmg_mult": 1.0, "dmg_type": "blunt", "contact": 5, "penetration": 1},
			{"name": "Gouge", "dmg_mult": 0.6, "dmg_type": "pierce", "contact": 1, "penetration": 10}
		]
	},
	"shortsword": {
		"type": "weapon", "dmg": 8, "dmg_type": "cut", "contact": 20, "penetration": 10, "hands": 1, "volume": 1.5, "weight": 1.2, "name": "Shortsword", "material": "steel",
		"attacks": [
			{"name": "Slash", "dmg_mult": 1.0, "dmg_type": "cut", "contact": 20, "penetration": 10},
			{"name": "Thrust", "dmg_mult": 0.9, "dmg_type": "pierce", "contact": 2, "penetration": 30},
			{"name": "Pommel", "dmg_mult": 0.4, "dmg_type": "blunt", "contact": 5, "penetration": 1}
		]
	},
	"longsword": {
		"type": "weapon", "dmg": 12, "dmg_type": "cut", "contact": 30, "penetration": 15, "hands": 2, "volume": 2.5, "weight": 1.8, "name": "Longsword", "material": "steel",
		"attacks": [
			{"name": "Slash", "dmg_mult": 1.0, "dmg_type": "cut", "contact": 30, "penetration": 15},
			{"name": "Thrust", "dmg_mult": 0.9, "dmg_type": "pierce", "contact": 2, "penetration": 40},
			{"name": "Mordhau", "dmg_mult": 0.7, "dmg_type": "blunt", "contact": 10, "penetration": 5}
		]
	},
	"estoc": {
		"type": "weapon", "dmg": 10, "dmg_type": "pierce", "contact": 2, "penetration": 50, "hands": 1, "volume": 1.8, "weight": 1.4, "name": "Estoc", "material": "steel",
		"attacks": [
			{"name": "Thrust", "dmg_mult": 1.0, "dmg_type": "pierce", "contact": 2, "penetration": 50},
			{"name": "Pommel", "dmg_mult": 0.4, "dmg_type": "blunt", "contact": 5, "penetration": 1}
		]
	},
	"battle_axe": {
		"type": "weapon", "dmg": 15, "dmg_type": "cut", "contact": 10, "penetration": 20, "hands": 2, "volume": 3.0, "weight": 2.5, "name": "Battle Axe", "material": "iron",
		"attacks": [
			{"name": "Chop", "dmg_mult": 1.0, "dmg_type": "cut", "contact": 10, "penetration": 20},
			{"name": "Hook", "dmg_mult": 0.6, "dmg_type": "blunt", "contact": 15, "penetration": 1}
		]
	},	"hand_axe": {
		"type": "weapon", "dmg": 10, "dmg_type": "cut", "contact": 8, "penetration": 20, "hands": 1, "volume": 1.5, "weight": 1.2, "name": "Hand Axe", "material": "iron",
		"attacks": [
			{"name": "Chop", "dmg_mult": 1.0, "dmg_type": "cut", "contact": 8, "penetration": 20}
		]
	},	"warhammer": {
		"type": "weapon", "dmg": 10, "dmg_type": "blunt", "contact": 1, "penetration": 5, "hands": 1, "volume": 2.0, "weight": 2.0, "name": "Warhammer", "material": "steel",
		"attacks": [
			{"name": "Strike", "dmg_mult": 1.0, "dmg_type": "blunt", "contact": 1, "penetration": 5},
			{"name": "Spike", "dmg_mult": 0.8, "dmg_type": "pierce", "contact": 1, "penetration": 40}
		]
	},
	"maul": {
		"type": "weapon", "dmg": 20, "dmg_type": "blunt", "contact": 20, "penetration": 1, "hands": 2, "volume": 8.0, "weight": 6.0, "name": "Maul", "material": "iron",
		"attacks": [
			{"name": "Smash", "dmg_mult": 1.0, "dmg_type": "blunt", "contact": 20, "penetration": 1}
		]
	},
	"mace": {
		"type": "weapon", "dmg": 12, "dmg_type": "blunt", "contact": 5, "penetration": 2, "hands": 1, "volume": 3.0, "weight": 2.5, "name": "Mace", "material": "iron",
		"attacks": [
			{"name": "Bash", "dmg_mult": 1.0, "dmg_type": "blunt", "contact": 5, "penetration": 2}
		]
	},
	"morningstar": {
		"type": "weapon", "dmg": 13, "dmg_type": "pierce", "contact": 2, "penetration": 15, "hands": 1, "volume": 3.2, "weight": 2.8, "name": "Morningstar", "material": "iron",
		"attacks": [
			{"name": "Strike", "dmg_mult": 1.0, "dmg_type": "pierce", "contact": 2, "penetration": 15}
		]
	},
	"flail": {
		"type": "weapon", "dmg": 13, "dmg_type": "blunt", "contact": 3, "penetration": 2, "ignore_shield": true, "hands": 1, "volume": 3.5, "weight": 3.0, "name": "Flail", "material": "iron",
		"attacks": [
			{"name": "Swing", "dmg_mult": 1.0, "dmg_type": "blunt", "contact": 3, "penetration": 2}
		]
	},
	"dagger": {
		"type": "weapon", "dmg": 5, "dmg_type": "pierce", "contact": 1, "penetration": 30, "hands": 1, "volume": 0.5, "weight": 0.4, "name": "Dagger", "material": "steel",
		"attacks": [
			{"name": "Stab", "dmg_mult": 1.0, "dmg_type": "pierce", "contact": 1, "penetration": 30},
			{"name": "Slash", "dmg_mult": 0.7, "dmg_type": "cut", "contact": 10, "penetration": 5}
		]
	},
	"club": {
		"type": "weapon", "dmg": 7, "dmg_type": "blunt", "contact": 10, "penetration": 1, "hands": 1, "volume": 2.0, "weight": 1.5, "name": "Club", "material": "wood",
		"attacks": [
			{"name": "Bash", "dmg_mult": 1.0, "dmg_type": "blunt", "contact": 10, "penetration": 1}
		]
	},
	"pitchfork": {
		"type": "weapon", "dmg": 6, "dmg_type": "pierce", "contact": 1, "penetration": 15, "range": 2.2, "hands": 2, "volume": 2.0, "weight": 1.8, "name": "Pitchfork", "material": "iron",
		"attacks": [
			{"name": "Thrust", "dmg_mult": 1.0, "dmg_type": "pierce", "contact": 1, "penetration": 15}
		]
	},
	"spear": {
		"type": "weapon", "dmg": 9, "dmg_type": "pierce", "contact": 1, "penetration": 40, "range": 2.2, "hands": 2, "volume": 2.2, "weight": 2.0, "name": "Spear", "material": "steel",
		"attacks": [
			{"name": "Thrust", "dmg_mult": 1.0, "dmg_type": "pierce", "contact": 1, "penetration": 40},
			{"name": "Bash", "dmg_mult": 0.5, "dmg_type": "blunt", "contact": 10, "penetration": 1}
		]
	},
	"halberd": {
		"type": "weapon", "dmg": 16, "dmg_type": "cut", "contact": 15, "penetration": 30, "range": 2.2, "hands": 2, "volume": 4.5, "weight": 3.5, "name": "Halberd", "material": "steel",
		"attacks": [
			{"name": "Chop", "dmg_mult": 1.0, "dmg_type": "cut", "contact": 15, "penetration": 30},
			{"name": "Thrust", "dmg_mult": 0.8, "dmg_type": "pierce", "contact": 1, "penetration": 40},
			{"name": "Hook", "dmg_mult": 0.5, "dmg_type": "blunt", "contact": 20, "penetration": 1}
		]
	},
	"glaive": {
		"type": "weapon", "dmg": 14, "dmg_type": "cut", "contact": 12, "penetration": 25, "range": 2.2, "hands": 2, "volume": 4.0, "weight": 3.0, "name": "Glaive", "material": "steel",
		"attacks": [
			{"name": "Slash", "dmg_mult": 1.0, "dmg_type": "cut", "contact": 12, "penetration": 25},
			{"name": "Thrust", "dmg_mult": 0.7, "dmg_type": "pierce", "contact": 1, "penetration": 30}
		]
	},
	"greatsword": {
		"type": "weapon", "dmg": 18, "dmg_type": "cut", "contact": 40, "penetration": 20, "hands": 2, "volume": 5.0, "weight": 3.5, "name": "Greatsword", "material": "steel",
		"attacks": [
			{"name": "Slash", "dmg_mult": 1.0, "dmg_type": "cut", "contact": 40, "penetration": 20},
			{"name": "Thrust", "dmg_mult": 0.8, "dmg_type": "pierce", "contact": 2, "penetration": 35},
			{"name": "Pommel", "dmg_mult": 0.3, "dmg_type": "blunt", "contact": 5, "penetration": 1}
		]
	},
	"pike": {
		"type": "weapon", "dmg": 10, "dmg_type": "pierce", "contact": 1, "penetration": 60, "range": 3.2, "hands": 2, "volume": 3.5, "weight": 4.0, "name": "Pike", "material": "steel",
		"attacks": [
			{"name": "Thrust", "dmg_mult": 1.0, "dmg_type": "pierce", "contact": 1, "penetration": 60}
		]
	},
	"shortbow": {
		"type": "weapon", "dmg": 6, "dmg_type": "pierce", "contact": 1, "penetration": 25, "range": 8, "is_ranged": true, "hands": 2, "volume": 1.0, "weight": 1.0, "name": "Shortbow", "material": "wood",
		"attacks": [{"name": "Shoot", "dmg_mult": 1.0, "dmg_type": "pierce", "contact": 1, "penetration": 25}]
	},
	"longbow": {
		"type": "weapon", "dmg": 10, "dmg_type": "pierce", "contact": 1, "penetration": 35, "range": 12, "is_ranged": true, "hands": 2, "volume": 1.5, "weight": 1.5, "name": "Longbow", "material": "wood",
		"attacks": [{"name": "Shoot", "dmg_mult": 1.0, "dmg_type": "pierce", "contact": 1, "penetration": 35}]
	},
	"crossbow": {
		"type": "weapon", "dmg": 15, "dmg_type": "pierce", "contact": 1, "penetration": 55, "range": 15, "is_ranged": true, "hands": 2, "volume": 3.0, "weight": 4.0, "name": "Crossbow", "material": "iron",
		"attacks": [{"name": "Shoot", "dmg_mult": 1.0, "dmg_type": "pierce", "contact": 1, "penetration": 55}]
	},
	
	# --- AMMUNITION ---
	"arrows": {
		"type": "ammo", "name": "Arrows", "material": "iron", "weight": 0.3, "dmg_mod": 0, "penetration_mod": 1.0
	},
	"bolts": {
		"type": "ammo", "name": "Bolts", "material": "iron", "weight": 0.4, "dmg_mod": 0, "penetration_mod": 1.0
	},
	"arrows_copper": {
		"type": "ammo", "name": "Copper Arrows", "material": "copper", "weight": 0.2, "dmg_mod": -2, "penetration_mod": 0.8
	},
	"arrows_iron": {
		"type": "ammo", "name": "Iron Arrows", "material": "iron", "weight": 0.3, "dmg_mod": 0, "penetration_mod": 1.0
	},
	"arrows_steel": {
		"type": "ammo", "name": "Steel Bodkin Arrows", "material": "steel", "weight": 0.5, "dmg_mod": 2, "penetration_mod": 1.5
	},
	"bolts_iron": {
		"type": "ammo", "name": "Iron Bolts", "material": "iron", "weight": 0.4, "dmg_mod": 0, "penetration_mod": 1.0
	},
	"bolts_steel": {
		"type": "ammo", "name": "Steel Bolts", "material": "steel", "weight": 0.6, "dmg_mod": 3, "penetration_mod": 1.8
	},
	
	# --- ARMOR (Layered: Under, Over, Armor, Cover) ---
	"tunic": {"type": "armor", "layer": "under", "prot": 2, "weight": 1, "coverage": ["torso", "l_arm", "r_arm"], "name": "Tunic", "material": "linen"},
	"shirt": {"type": "armor", "layer": "under", "prot": 1, "weight": 0.5, "coverage": ["torso", "l_arm", "r_arm"], "shear_mult": 2.0, "name": "Shirt", "material": "cloth"},
	"trousers": {"type": "armor", "layer": "under", "prot": 1, "weight": 0.5, "coverage": ["l_leg", "r_leg"], "name": "Trousers", "material": "cloth"},
	"gloves": {"type": "armor", "layer": "under", "prot": 1, "weight": 0.2, "coverage": ["l_hand", "r_hand"], "name": "Gloves", "material": "leather"},
	"boots": {"type": "armor", "layer": "over", "prot": 2, "weight": 1.0, "coverage": ["l_foot", "r_foot"], "name": "Boots", "material": "leather"},
	"gambeson": {"type": "armor", "layer": "over", "prot": 4, "weight": 3, "coverage": ["torso", "l_arm", "r_arm", "l_leg", "r_leg", "l_hand", "r_hand", "l_foot", "r_foot"], "name": "Gambeson", "material": "wool"},
	"hauberk": {"type": "armor", "layer": "over", "prot": 10, "weight": 8, "coverage": ["torso", "l_arm", "r_arm", "l_hand", "r_hand"], "name": "Hauberk", "material": "iron"},
	"coif": {"type": "armor", "layer": "over", "prot": 5, "weight": 2, "coverage": ["head"], "name": "Coif", "material": "iron"},
	"leather_armor": {"type": "armor", "layer": "armor", "prot": 6, "weight": 5, "coverage": ["torso", "l_arm", "r_arm", "l_leg", "r_leg"], "name": "Leather Armor", "material": "leather"},
	"cuirass": {"type": "armor", "layer": "armor", "prot": 15, "weight": 10, "coverage": ["torso"], "name": "Cuirass", "material": "iron"},
	"brigandine": {"type": "armor", "layer": "armor", "prot": 18, "weight": 12, "coverage": ["torso"], "name": "Brigandine", "material": "steel"},
	"lamellar": {"type": "armor", "layer": "armor", "prot": 14, "weight": 10, "coverage": ["torso", "l_arm", "r_arm"], "name": "Lamellar", "material": "iron"},
	"breastplate": {"type": "armor", "layer": "armor", "prot": 20, "weight": 12, "coverage": ["torso"], "name": "Breastplate", "material": "steel"},
	"greaves": {"type": "armor", "layer": "armor", "prot": 8, "weight": 4, "coverage": ["l_leg", "r_leg"], "name": "Greaves", "material": "iron"},
	"sabatons": {"type": "armor", "layer": "armor", "prot": 6, "weight": 3, "coverage": ["l_foot", "r_foot"], "name": "Sabatons", "material": "iron"},
	"gauntlets": {"type": "armor", "layer": "armor", "prot": 5, "weight": 2, "coverage": ["l_hand", "r_hand"], "name": "Gauntlets", "material": "iron"},
	"pauldrons": {"type": "armor", "layer": "armor", "prot": 7, "weight": 3, "coverage": ["l_arm", "r_arm"], "name": "Pauldrons", "material": "iron"},
	"vambraces": {"type": "armor", "layer": "armor", "prot": 5, "weight": 2, "coverage": ["l_arm", "r_arm"], "name": "Vambraces", "material": "iron"},
	"cloak": {"type": "armor", "layer": "cover", "prot": 2, "weight": 2, "coverage": ["torso", "l_arm", "r_arm"], "name": "Cloak", "material": "wool"},
	"surcoat": {"type": "armor", "layer": "cover", "prot": 1, "weight": 1, "coverage": ["torso"], "name": "Surcoat", "material": "linen"},
	"cap": {"type": "armor", "layer": "armor", "prot": 3, "weight": 1, "coverage": ["head"], "name": "Cap", "material": "wool"},
	"helmet": {"type": "armor", "layer": "armor", "prot": 15, "weight": 5, "coverage": ["head"], "name": "Helmet", "material": "iron"},
	"great_helm": {"type": "armor", "layer": "armor", "prot": 25, "weight": 8, "coverage": ["head"], "name": "Great Helm", "material": "steel"},
	"buckler": {"type": "shield", "prot": 5, "weight": 2, "block_chance": 0.2, "name": "Buckler", "material": "iron"},
	"heater_shield": {"type": "shield", "prot": 12, "weight": 6, "block_chance": 0.4, "name": "Heater Shield", "material": "wood"},
	"kite_shield": {"type": "shield", "prot": 15, "weight": 8, "block_chance": 0.5, "name": "Kite Shield", "material": "wood"},
	"tower_shield": {"type": "shield", "prot": 20, "weight": 15, "block_chance": 0.6, "name": "Tower Shield", "material": "wood"},
	"pavise": {"type": "shield", "prot": 18, "weight": 12, "block_chance": 0.55, "name": "Pavise", "material": "wood"},
	# --- TRANSPORT & LOGISTICS ---
	"mule": {"type": "transport", "name": "Mule", "capacity_bonus": 150.0, "weight": 0.0, "volume": 0.0},
	"horse": {"type": "transport", "name": "Horse", "capacity_bonus": 250.0, "weight": 0.0, "volume": 0.0},
	"cart": {"type": "transport", "name": "Cart", "capacity_bonus": 800.0, "weight": 0.0, "volume": 0.0}
}

# --- SETTLEMENTS & BUILDINGS ---
const BUILDINGS = {
	# --- INDUSTRY (The Engine) ---
	"farm": {
		"category": "industry",
		"cost": 500, "labor": 500, "tier": 1, 
		"desc": "Increases grain yield by 50% per level.",
		"levels": {
			1: {"name": "Fields", "flavor": "Basic grain production from tilled earth."},
			2: {"name": "Granary Extension", "flavor": "Raised floors protect the harvest from rats and rot."},
			4: {"name": "Three-Field System", "flavor": "Crop rotation ensures the soil never sleeps."},
			6: {"name": "Irrigation Network", "flavor": "Canals bring life even in the driest summer."},
			8: {"name": "Plantation", "flavor": "Vast monocultures dedicated to efficiency."},
			10: {"name": "Agricultural Revolution", "flavor": "The land yields bounties undreamt of by our ancestors."}
		}
	},
	"lumber_mill": {
		"category": "industry",
		"cost": 800, "labor": 800, "tier": 1, 
		"desc": "Increases wood yield by 100% per level.",
		"levels": {
			1: {"name": "Woodcutter's Camp", "flavor": "The sound of axes rings through the trees."},
			3: {"name": "Sawmill", "flavor": "Water-driven blades slice timber effortlessly."},
			7: {"name": "Logging Empire", "flavor": "Entire forests are processed into fleets and cities."},
			10: {"name": "The Great Arboretum", "flavor": "We do not just harvest nature; we master it."}
		}
	},
	"fishery": {
		"category": "industry",
		"cost": 600, "labor": 600, "tier": 1, 
		"desc": "Increases fish yield by 50% per level.",
		"levels": {
			1: {"name": "Fishing Huts", "flavor": "Simple piers and nets for the local catch."},
			3: {"name": "Fishmonger's Row", "flavor": "A bustling market for cleaning and salting the harvest."},
			6: {"name": "Deep Sea Fleet", "flavor": "Sturdy boats that can handle the open waves for weeks."},
			10: {"name": "The Great Harbor", "flavor": "The sea is our larder; we take what we please."}
		}
	},
	"mine": {
		"category": "industry",
		"cost": 1500, "labor": 1500, "tier": 1, 
		"desc": "Increases stone/ore yield by 50% per level.",
		"levels": {
			1: {"name": "Surface Quarry", "flavor": "Extracting the easiest stones from the hillside."},
			3: {"name": "Shaft Mine", "flavor": "Vertical shafts reach for deeper veins of iron and copper."},
			5: {"name": "Drainage Pumps", "flavor": "Clearing flooded tunnels to reach the deepest riches."},
			8: {"name": "Pillared Gallery", "flavor": "Intricate subterranean networks of hauling and extraction."},
			10: {"name": "Under-Kingdom", "flavor": "Total mastery of the earth. The mountains are hollowed and bled dry."}
		}
	},
	"pasture": {
		"category": "industry",
		"cost": 700, "labor": 700, "tier": 1, 
		"desc": "Increases wool/hide/meat yield by 50% per level.",
		"levels": {
			1: {"name": "Grazing Land", "flavor": "Basic fenced areas for herds to wander."},
			3: {"name": "Shearing Sheds", "flavor": "Dedicated spaces for processing wool and hides."},
			6: {"name": "Breeding Stables", "flavor": "Selective breeding results in larger, hardier livestock."},
			10: {"name": "The King's Ranch", "flavor": "Endless herds stretching to the horizon."}
		}
	},
	
	"blacksmith": {
		"category": "industry",
		"cost": 2000, "labor": 2000, "tier": 2, 
		"desc": "Increases steel production efficiency by 100% per level.",
		"levels": {
			1: {"name": "Village Smithy", "flavor": "A single anvil rings out, forging horseshoes and spearheads."},
			3: {"name": "Ironworks", "flavor": "Several fires burn constantly, churning out ingots."},
			5: {"name": "Foundry", "flavor": "Liquid metal flows into molds day and night."},
			7: {"name": "Blast Furnace", "flavor": "Massive bellows pump air into towers of flame."},
			10: {"name": "The Vulcan Complex", "flavor": "The sky is black with soot. This place births armies."}
		}
	},
	"tannery": {
		"category": "industry",
		"cost": 2500, "labor": 1500, "tier": 2, 
		"desc": "Increases leather production efficiency by 100% per level.",
		"levels": {
			1: {"name": "Tanner's Yard", "flavor": "The smell of urea and curing hide is unmistakable."},
			5: {"name": "Refining Vats", "flavor": "Advanced chemical washes produce softer, stronger leathers."},
			10: {"name": "Imperial Leatherworks", "flavor": "Supplying the saddles and armor of a thousand knights."}
		}
	},
	"weaver": {
		"category": "industry",
		"cost": 2500, "labor": 1500, "tier": 2, 
		"desc": "Increases cloth production efficiency by 100% per level.",
		"levels": {
			1: {"name": "Loom Room", "flavor": "Wooden looms click-clack as thread becomes cloth."},
			5: {"name": "Textile Mill", "flavor": "Coordinated looms and dyeing vats produce vast quantities of fabric."},
			10: {"name": "The Tapestry Master", "flavor": "Fabrics so fine they are traded for their weight in silver."}
		}
	},
	"brewery": {
		"category": "industry",
		"cost": 3000, "labor": 1500, "tier": 2, 
		"desc": "Increases ale production efficiency by 100% per level.",
		"levels": {
			1: {"name": "Small Batch Fermenter", "flavor": "Vats of bubbling mash produce thick, dark ale."},
			5: {"name": "Distillery", "flavor": "Pipes and barrels for mass fermentation and aging."},
			10: {"name": "The Celestial Keg", "flavor": "Known across the world; a sip can make a pauper feel like a king."}
		}
	},
	"tailor": {
		"category": "industry",
		"cost": 3500, "labor": 1800, "tier": 2, 
		"desc": "Increases garment production efficiency by 100% per level.",
		"levels": {
			1: {"name": "Seamstress Shop", "flavor": "Repairing tunics and sewing basic shirts."},
			5: {"name": "Clothier Guild", "flavor": "The town's elite come here for custom doublets and gowns."},
			10: {"name": "High Fashion House", "flavor": "Dictating the style of the entire kingdom's court."}
		}
	},
	"goldsmith": {
		"category": "industry",
		"cost": 8000, "labor": 3000, "tier": 3, 
		"desc": "Increases jewelry production efficiency by 100% per level.",
		"levels": {
			1: {"name": "Jeweler's Bench", "flavor": "Setting small stones into copper rings."},
			5: {"name": "Artisan's Workshop", "flavor": "Fine gold wire and precious gems are crafted into masterworks."},
			10: {"name": "The Royal Treasury Shop", "flavor": "Only Emperors and Gods can afford these creations."}
		}
	},

	# --- DEFENSE (The Shield) ---
	"stone_walls": {
		"category": "military",
		"cost": 5000, "labor": 5000, "tier": 2, 
		"desc": "Increases settlement defense, reducing damage to garrison and hindering attackers.",
		"levels": {
			1: {"name": "Palisade", "flavor": "Sharpened wooden stakes to deter the wolves and bandits."},
			2: {"name": "Reinforced Gate", "flavor": "Iron-banded oak that can withstand many a ramming."},
			3: {"name": "Watch Towers", "flavor": "High vantage points for archers to fire down upon the foe."},
			4: {"name": "Machicolations", "flavor": "Opening in the floor to drop stones and boiling oil."},
			5: {"name": "Stone Curtain Wall", "flavor": "Huge granite blocks that shrug off fire and axe alike."},
			6: {"name": "Merlons & Battlements", "flavor": "Crenelated walls to provide cover for the defenders."},
			7: {"name": "Wall-Mounted Balistas", "flavor": "Black-bolt engines capable of skewering knights and horses."},
			8: {"name": "Great Keep", "flavor": "A final redoubt where the garrison can retreat and regroup."},
			9: {"name": "Moat & Drawbridge", "flavor": "Water and depth. The ultimate bottleneck for any army."},
			10: {"name": "The Star Fort", "flavor": "A geometric masterpiece of killing zones. To attack is suicide."}
		}
	},
	"barracks": {
		"category": "military",
		"cost": 5000, "labor": 2500, "tier": 2, 
		"desc": "Increases garrison capacity, recruitment volume (Odds) and troop quality (Evens).",
		"levels": {
			1: {"name": "Muster Field", "flavor": "A muddy field where peasants learn to hold a spear. Increased muster volume."},
			2: {"name": "Drill Square", "flavor": "Strict sergeants bark orders. Unlocks Tier 2 Trained soldiers."},
			3: {"name": "Garrison Quarters", "flavor": "Soldiers live here full-time. Even greater muster capacity."},
			4: {"name": "Sergeant's Mess", "flavor": "Veteran discipline. Unlocks Tier 3 Men-at-Arms."},
			5: {"name": "Training Hall", "flavor": "A dedicated facility for the local levy. Massive muster capacity."},
			6: {"name": "Veteran Lodge", "flavor": "The air smells of old leather. Unlocks Tier 4 Veteran soldiers."},
			7: {"name": "Military District", "flavor": "Entire city blocks dedicated to the march. Imperial muster volume."},
			8: {"name": "Officer's Academy", "flavor": "War is studied as a science. Unlocks Tier 5 Royal Guards."},
			9: {"name": "War Room", "flavor": "Planning for global conquest. Maximum muster capacity."},
			10: {"name": "Citadel of Marshals", "flavor": "The pinnacle of military might. Elite quality and quantity."}
		}
	},
	"training_ground": {
		"category": "military",
		"cost": 4000, "labor": 2000, "tier": 2, 
		"desc": "Increases recruit quality/tier per level.",
		"levels": {
			1: {"name": "Training Field", "flavor": "Wooden swords and straw targets."},
			5: {"name": "Combat Pit", "flavor": "Live sparring and combat maneuvers."},
			10: {"name": "War College", "flavor": "Recruits leave as seasoned veterans before their first real battle."}
		}
	},
	"granary": {
		"category": "military",
		"cost": 1200, "labor": 1000, "tier": 1, 
		"desc": "Increases starvation resistance and food storage cap by 50% per level.",
		"levels": {
			1: {"name": "Food Cellar", "flavor": "Cold, dry storage for grain."},
			4: {"name": "Raised Granary", "flavor": "Elevated structures keep pests away from the stockpile."},
			10: {"name": "The Eternal Silo", "flavor": "Stored food can last through a decade-long siege."}
		}
	},
	"watchtower": {
		"category": "military",
		"cost": 2000, "labor": 1200, "tier": 1, 
		"desc": "Increases stability and reduces bandit loot success chance.",
		"levels": {
			1: {"name": "Lookout Post", "flavor": "A simple wooden platform with a bell."},
			5: {"name": "Stone Beacon", "flavor": "Fires lit at the top can signal trouble for leagues."},
			10: {"name": "The Vigilant Eye", "flavor": "Not a bird flies by without the tower's knowledge."}
		}
	},

	# --- CIVIL (The Heart) ---
	"housing_district": {
		"cost": 1000, "labor": 800, "tier": 1, 
		"desc": "Increases population capacity by 100 per level.",
		"levels": {
			1: {"name": "Thatched Cottages", "flavor": "Simple homes for simple folk."},
			5: {"name": "Stone Tenements", "flavor": "Rows of sturdy buildings housing dozens of families."},
			10: {"name": "The High District", "flavor": "Ornate villas and sprawling estates for a massive populace."}
		}
	},
	
	"market": {
		"cost": 1000, "labor": 1200, "tier": 1, 
		"desc": "Increases industrial slots and trade income.",
		"levels": {
			1: {"name": "Town Stalls", "flavor": "Farmers shouting prices over bushel baskets."},
			3: {"name": "Tax Office", "flavor": "A grim building where the Lord's due is weighed and counted."},
			6: {"name": "Guild Hall", "flavor": "Merchants meet behind closed doors to decide who gets rich."},
			10: {"name": "The Grand Exchange", "flavor": "The gold of the world flows through these ledgers."}
		}
	},
	"road_network": {
		"cost": 1500, "labor": 1500, "tier": 1, 
		"desc": "Increases trade throughput and tax efficiency by 15%.",
		"levels": {
			1: {"name": "Dirt Paths", "flavor": "Better than bush-whacking, but muddy in the rain."},
			5: {"name": "Cobblestone Streets", "flavor": "Paved paths for carts and horses."},
			10: {"name": "Imperial Highways", "flavor": "Straight, smooth roads built to last ages."}
		}
	},
	"merchant_guild": {
		"cost": 5000, "labor": 2000, "tier": 3, 
		"desc": "Increases caravan capacity and global trade reach.",
		"levels": {
			1: {"name": "Local Chapterhouse", "flavor": "Where local traders talk shop."},
			5: {"name": "National Registry", "flavor": "Coordinating trade across the entire kingdom."},
			10: {"name": "World Trade Council", "flavor": "Controlling the flow of gold between continents."}
		}
	},
	"warehouse_district": {
		"cost": 3000, "labor": 1500, "tier": 2, 
		"desc": "Increases total inventory storage limit by 100% per level.",
		"levels": {
			1: {"name": "Basement Storage", "flavor": "Extra space beneath the shops."},
			5: {"name": "Port Warehouses", "flavor": "Massive sheds for sea-borne cargo."},
			10: {"name": "The Great Vaults", "flavor": "Capable of holding the riches of a fallen empire."}
		}
	},
	"cathedral": {
		"cost": 8000, "labor": 5000, "tier": 3, 
		"desc": "Massively increases stability and loyalty of the Nobility.",
		"levels": {
			1: {"name": "Sanctuary", "flavor": "A quiet place for prayer."},
			4: {"name": "Basilica", "flavor": "Towering arches and stained glass."},
			10: {"name": "The Seat of Divines", "flavor": "Where Kings are crowned and gods are said to walk."}
		}
	},
	
	"tavern": {
		"cost": 800, "labor": 1000, "tier": 1, 
		"desc": "Increases happiness and migration.",
		"levels": {
			1: {"name": "Alehouse", "flavor": "Cheap swill and loud songs."},
			4: {"name": "Traveler's Inn", "flavor": "Warm beds attract sellswords from distant lands."},
			7: {"name": "Bard's College", "flavor": "Songs are powerful. A good tune can make a tyrant look like a savior."},
			10: {"name": "The Shadow Broker", "flavor": "The bartender knows everything. For the right price, so do you."}
		}
	}
}

const BASE_PRICES = {
	# --- RESOURCES ---
	"grain": 10, "fish": 8, "meat": 20, "wood": 5, "stone": 15, "iron": 40, "steel": 120, "leather": 60, "cloth": 50, "ale": 30,
	"copper": 25, "bronze": 60, "silver": 150, "gold": 400, "jewelry": 1000, "livestock": 40, "wool": 20, "fine_garments": 250,
	"hides": 15, "glass_sand": 15, "spices": 500, "ivory": 800, "coal": 20, "marble": 100, "gems": 600, "peat": 8, "clay": 12,
	"salt": 45, "furs": 120, "tin": 25, "lead": 20, "sand": 5, "tools": 80, "bricks": 30,
	
	# --- WEAPONS ---
	"shortsword": 300, "hand_axe": 150, "mace": 200, "dagger": 100, "club": 20, "spear": 180, "shortbow": 250, "arrows": 50,
	"estoc": 350, "longsword": 500, "greatsword": 800, "battle_axe": 450, "warhammer": 400, "maul": 600, "morningstar": 350,
	"flail": 400, "pitchfork": 50, "halberd": 550, "glaive": 500, "pike": 450, "longbow": 450, "crossbow": 600, "bolts": 60,
	
	# --- ARMOR ---
	"shirt": 10, "trousers": 15, "tunic": 20, "gambeson": 120, "leather_armor": 250, "helmet": 180, "cap": 30, "boots": 40, "gloves": 30,
	"hauberk": 800, "cuirass": 1200, "brigandine": 1500, "breastplate": 2000, "greaves": 400, "sabatons": 300, "gauntlets": 350,
	"pauldrons": 450, "vambraces": 300, "cloak": 60, "surcoat": 80, "great_helm": 600,
	"heater_shield": 400, "buckler": 150, "kite_shield": 600, "tower_shield": 900, "pavise": 750,

	# --- TRANSPORT ---
	"mule": 150,
	"horse": 450,
	"cart": 800
}

const GEOLOGY_RESOURCES = {
	"sedimentary": {
		"iron": 0.04,
		"coal": 0.08,
		"lead": 0.05,
		"clay": 0.12
	},
	"metamorphic": {
		"copper": 0.03,
		"silver": 0.06,
		"marble": 0.09,
		"tin": 0.07
	},
	"igneous": {
		"gold": 0.02,
		"gems": 0.05
	}
}

func get_weapon_skill_tag(weapon: Dictionary) -> String:
	var w_name = weapon.get("name", "").to_lower()
	if w_name.contains("sword"): return "swordsmanship"
	if w_name.contains("axe"): return "axe_fighting"
	if w_name.contains("spear") or w_name.contains("pike") or w_name.contains("halberd") or w_name.contains("glaive") or w_name.contains("pitchfork"): return "spear_use"
	if w_name.contains("mace") or w_name.contains("warhammer") or w_name.contains("maul") or w_name.contains("club") or w_name.contains("flail"): return "mace_hammer"
	if w_name.contains("dagger") or w_name.contains("knife"): return "dagger_knife"
	if w_name.contains("shortbow") or w_name.contains("longbow"): return "archery"
	if w_name.contains("crossbow"): return "crossbows"
	return "improvised"

func get_engine_damage_estimate(engine_key: String, distance: float) -> Dictionary:
	if not SIEGE_ENGINES.has(engine_key): return {}
	var e = SIEGE_ENGINES[engine_key]
	var momentum = e.weight * e.velocity
	
	# Massive engines use a higher momentum multiplier than handheld weapons
	var dmg = (e.dmg_base + (momentum * 5.0))
	
	# Accuracy dropoff
	var acc = e.accuracy
	if distance > 20: # Engines have a 'sweet spot' before dropoff starts
		acc -= (distance - 20) * 0.01
		
	return {
		"name": e.name,
		"dmg": dmg,
		"accuracy": clamp(acc, 0.05, 0.95),
		"dmg_type": e.dmg_type,
		"penetration": e.penetration,
		"contact": e.contact,
		"aoe": e.aoe
	}

func get_damage_estimate(attacker: GDUnit, defender: GDUnit, part_key: String, attack_idx: int = 0) -> Dictionary:
	var weapon = attacker.equipment["main_hand"]
	if not weapon: weapon = ITEMS["fist"]
	
	var attacks = weapon.get("attacks", [])
	if attacks.size() == 0:
		attacks = [{"name": "Strike", "dmg_mult": 1.0, "dmg_type": weapon.get("dmg_type", "blunt"), "contact": weapon.get("contact", 10), "penetration": weapon.get("penetration", 10)}]
	
	var attack = attacks[clamp(attack_idx, 0, attacks.size() - 1)]
	
	var w_mat = MATERIALS.get(weapon.get("material", "flesh"), MATERIALS.flesh)
	var weight = float(weapon.get("weight", 1.0))
	
	# Velocity proxy: 1.0 / speed
	var velocity = 1.0 / max(0.1, attacker.speed)
	var momentum = weight * velocity
	
	# Skill and Attribute Integration
	var skill_tag = get_weapon_skill_tag(weapon)
	var attacker_skill = attacker.skills.get(skill_tag, 0)
	var defender_dodge = defender.skills.get("dodging", 0)
	
	var base_dmg = float(weapon.get("dmg", 5)) * attack.get("dmg_mult", 1.0)
	# Strength bonus (10 is baseline)
	var str_mult = 1.0 + (float(attacker.attributes.strength - 10) * 0.05)
	var current_dmg = ((base_dmg * 0.5) + (momentum * 0.2)) * str_mult
	
	# Skill-based damage bonus (Mastery)
	current_dmg *= (1.0 + (float(attacker_skill) / 200.0))
	
	var hit_chance = 0.8
	if part_key == "head": hit_chance = 0.4
	elif part_key == "neck": hit_chance = 0.3
	elif part_key == "torso": hit_chance = 0.8
	elif part_key in ["l_arm", "r_arm"]: hit_chance = 0.6
	elif part_key in ["l_leg", "r_leg"]: hit_chance = 0.7
	elif part_key in ["l_eye", "r_eye"]: hit_chance = 0.05
	elif part_key in ["l_hand", "r_hand", "l_foot", "r_foot"]: hit_chance = 0.15
	elif part_key in ["brain", "heart", "spine", "ribs", "lungs", "gut"]: hit_chance = 0.02 # Hard to hit directly
	
	var def_speed = defender.speed
	hit_chance *= (def_speed / 0.6)
	
	# Skill adjustments
	hit_chance += (float(attacker_skill - defender_dodge) / 500.0)
	
	# Limb-based Penalties for Attacker
	var wpn = attacker.equipment.get("main_hand")
	var is_two_handed = wpn.get("hands", 1) == 2 if wpn else false
	
	for side in ["l", "r"]:
		if not attacker.status.get(side + "_arm_functional", true):
			# If using a two-handed weapon, losing ANY arm is a disaster
			if is_two_handed:
				hit_chance *= 0.2
			else:
				# If using a one-handed weapon, losing the primary arm is a disaster
				# (Assuming main_hand is r_hand for now, or just penalizing heavily)
				hit_chance *= 0.5
	
	if defender.status.get("is_prone", false):
		hit_chance *= 1.5 # Prone units are much easier to hit
		
	hit_chance = clamp(hit_chance, 0.01, 0.95)

	var layers = ["cover", "armor", "over", "under"]
	var armor_names = []
	
	# Armor lookup (Sub-parts use parent's armor if they don't have a slot)
	var armor_part_key = part_key
	if not defender.equipment.has(armor_part_key):
		var part_data = defender.body.get(part_key, {})
		if part_data.get("parent"):
			armor_part_key = part_data["parent"]
	
	if not defender.equipment.has(armor_part_key):
		# Fallback for parts that don't have equipment slots (like internal organs)
		# and aren't correctly parented to a slot-bearing part.
		return {
			"est_dmg": int(current_dmg),
			"hit_chance": int(hit_chance * 100),
			"armor": [],
			"attack_name": attack["name"]
		}

	for l_key in layers:
		var armor = defender.equipment[armor_part_key][l_key]
		if not armor or typeof(armor) != TYPE_DICTIONARY: continue
		
		var a_mat = MATERIALS.get(armor.get("material", "leather"), MATERIALS.leather)
		var absorbed = 0.0
		if attack["dmg_type"] == "blunt":
			var contact_area = max(0.1, float(attack.get("contact", 10)))
			var effective_yield = a_mat["impact_yield"] * (contact_area / 10.0)
			absorbed = min(current_dmg, armor.get("prot", 0) * (effective_yield / 100.0))
			var bruising = absorbed * 0.1
			current_dmg -= (absorbed - bruising)
		else:
			var penetration_depth = max(0.1, float(attack.get("penetration", 10)))
			var effective_yield = a_mat["shear_yield"] / (penetration_depth / 10.0)
			if armor.get("shear_mult"): effective_yield *= armor["shear_mult"]
			absorbed = min(current_dmg, armor.get("prot", 0) * (effective_yield / 100.0))
			current_dmg -= absorbed
		
		if w_mat["hardness"] < a_mat["hardness"]:
			current_dmg *= 0.8 
		elif w_mat["hardness"] > a_mat["hardness"] * 1.5:
			current_dmg *= 1.1
			
		armor_names.append(armor.get("name", "Armor"))
		if current_dmg <= 0: break

	return {
		"est_dmg": int(current_dmg),
		"hit_chance": int(hit_chance * 100),
		"armor": armor_names,
		"attack_name": attack["name"]
	}

func resolve_engine_damage(engine_key: String, defender: GDUnit, rng: RandomNumberGenerator) -> Dictionary:
	var e = SIEGE_ENGINES.get(engine_key, {})
	var res = {
		"hit": true,
		"blocked": false,
		"part_hit": "torso",
		"armor_layers": [],
		"tissues_hit": [],
		"final_dmg": 0,
		"dmg_type": e.get("dmg_type", "blunt"),
		"critical_events": [],
		"remaining_energy": 0.0,
		"downed_occurred": false,
		"prone_occurred": false
	}
	
	var momentum = e.get("weight", 5) * e.get("velocity", 5)
	var current_energy = float(e.get("dmg_base", 50)) + (momentum * 5.0)
	
	# Siege engines logic: They usually hit the torso or whole body
	var target_part = "torso"
	if not defender.body.has(target_part):
		# If it's a structural target or weird entity, pick first available part
		target_part = defender.body.keys()[0]
	
	res["part_hit"] = part_hit_name(target_part)
	
	# Process layers of the defender (Siege engines often ignore or crush armor)
	var part = defender.body[target_part]
	var tissues = part["tissues"]
	
	for i in range(tissues.size()):
		var tissue = tissues[i]
		var resistance = float(tissue.get("thick", 5))
		if tissue.get("type") == "bone": resistance *= 2.0
		
		# Energy loss calculation
		var energy_loss = min(current_energy, resistance)
		current_energy -= energy_loss
		
		var dmg = int(energy_loss * 2.0) # Siege damage is catastrophic to tissues
		tissue["hp"] -= dmg
		res["final_dmg"] += dmg
		res["tissues_hit"].append(tissue.get("type", "flesh"))
		
		if tissue["hp"] <= 0:
			if tissue.get("type") == "bone": res["critical_events"].append("bone_fractured")
			if tissue.get("type") == "organ": res["critical_events"].append("organ_failure:" + tissue.get("name", "organ"))

	res["remaining_energy"] = current_energy
	
	# Update defender status
	if defender.hp <= 0:
		defender.status["is_dead"] = true
	elif res["final_dmg"] > 25:
		defender.status["is_prone"] = true
		res["prone_occurred"] = true
		
	return res

func resolve_attack(attacker: GDUnit, defender: GDUnit, rng: RandomNumberGenerator, forced_part: String = "", attack_idx: int = 0, shield_wall_bonus: float = 0.0) -> Dictionary:
	var res = {
		"hit": false,
		"blocked": false,
		"part_hit": "",
		"armor_layers": [],
		"tissues_hit": [],
		"total_pain": 0,
		"added_bleed_rate": 0.0,
		"death_occurred": false,
		"downed_occurred": false,
		"prone_occurred": false,
		"final_dmg": 0,
		"dmg_type": "blunt",
		"verb": "hits",
		"critical_events": [],
		"remaining_energy": 0.0
	}
	
	# 1. Determine Hit Location
	var part_key = "torso"
	if forced_part != "":
		part_key = forced_part
	else:
		var roll = rng.randf()
		if roll < 0.08: part_key = "head"
		elif roll < 0.12: part_key = "neck"
		elif roll < 0.25: part_key = "l_arm"
		elif roll < 0.40: part_key = "r_arm"
		elif roll < 0.60: part_key = "l_leg"
		elif roll < 0.80: part_key = "r_leg"
		else: part_key = "torso"
	
	# Sub-part redirection
	var sub_roll = rng.randf()
	if part_key == "head":
		if sub_roll < 0.10: part_key = "l_eye"
		elif sub_roll < 0.20: part_key = "r_eye"
		elif sub_roll < 0.45: part_key = "brain" # 25% chance for a deep head hit to aim for the brain
	elif part_key == "neck" and sub_roll < 0.30:
		part_key = "spine"
	elif part_key in ["l_arm", "r_arm"] and sub_roll < 0.20:
		part_key = "l_hand" if part_key == "l_arm" else "r_hand"
	elif part_key in ["l_leg", "r_leg"] and sub_roll < 0.20:
		part_key = "l_foot" if part_key == "l_leg" else "r_foot"
	elif part_key == "torso" and sub_roll < 0.40:
		var int_roll = rng.randf()
		if int_roll < 0.15: part_key = "heart"
		elif int_roll < 0.35: part_key = "l_lung"
		elif int_roll < 0.55: part_key = "r_lung"
		elif int_roll < 0.75: part_key = "gut"
		elif int_roll < 0.85: part_key = "liver"
		else: part_key = "spine"
	
	res["part_hit"] = part_hit_name(part_key)
	res["part_key"] = part_key
	
	# 2. Hit Chance Check
	var est = get_damage_estimate(attacker, defender, part_key, attack_idx)
	if rng.randi_range(0, 100) > est["hit_chance"]:
		return res # hit = false
		
	res["hit"] = true
	var weapon = attacker.equipment["main_hand"]
	if not weapon: weapon = ITEMS["fist"]
	var attacks = weapon.get("attacks", [])
	var attack = attacks[attack_idx] if attack_idx < attacks.size() else {"name": "Strike", "dmg_mult": 1.0, "dmg_type": "blunt", "contact": 10, "penetration": 5}
	var dmg_type = attack.get("dmg_type", "blunt")
	res["dmg_type"] = dmg_type
	res["attack_name"] = attack["name"]
	
	# 3. Shield Block Check
	if not weapon.get("ignore_shield", false) and defender.equipment["off_hand"]:
		var shield = defender.equipment["off_hand"]
		var defender_shield_skill = defender.skills.get("shield_use", 0)
		var block_chance = shield.get("block_chance", 0.1) + (float(defender_shield_skill) / 200.0) + shield_wall_bonus
		
		# Ranged units have a harder time hitting a dense shield wall
		var ammo = attacker.equipment.get("ammo")
		if weapon.get("is_ranged", false) and ammo != null:
			block_chance += 0.2 # Extra protection from range when in formation
			
		if rng.randf() < block_chance:
			res["blocked"] = true
			res["shield_name"] = shield.get("name", "Shield")
			return res

	# 4. Layered Armor Physics
	var ammo = attacker.equipment.get("ammo")
	var is_ranged_shot = weapon.get("is_ranged", false) and ammo != null
	
	var projectile_mat_key = ammo.get("material", "iron") if is_ranged_shot else weapon.get("material", "flesh")
	var w_mat = MATERIALS.get(projectile_mat_key, MATERIALS.flesh)
	
	var weight = float(weapon.get("weight", 1.0))
	if is_ranged_shot:
		weight = float(ammo.get("weight", 0.3))
		
	var velocity = 1.0 / max(0.1, attacker.speed)
	if weapon.get("is_ranged", false):
		velocity += (float(weapon.get("dmg", 10)) / 10.0) # Bow tension adds to velocity
		
	var momentum = weight * velocity
	
	# Skill and Attribute Integration
	var skill_tag = get_weapon_skill_tag(weapon)
	var attacker_skill = attacker.skills.get(skill_tag, 0)
	
	var base_dmg = float(weapon.get("dmg", 5)) * attack.get("dmg_mult", 1.0)
	if is_ranged_shot:
		base_dmg += ammo.get("dmg_mod", 0)
		
	# Strength bonus (10 is baseline)
	var str_mult = 1.0 + (float(attacker.attributes.strength - 10) * 0.1) # Increased from 0.05
	
	# Realistic Damage: Base + Momentum. 
	# A heavy weapon with momentum should hit much harder.
	var current_dmg = (base_dmg + (momentum * 2.0)) * str_mult
	
	# Skill-based damage bonus (Mastery)
	current_dmg *= (1.0 + (float(attacker_skill) / 100.0)) # Skill is more impactful
	current_dmg += rng.randi_range(-1, 3)
	
	var layers = ["cover", "armor", "over", "under"]
	var contact_area = float(attack.get("contact", 10))
	var penetration_factor = float(attack.get("penetration", 5))
	if is_ranged_shot:
		penetration_factor *= ammo.get("penetration_mod", 1.0)
	
	var part = defender.body[part_key]
	var armor_part_key = part_key
	
	# Armor redirection: Sub-parts use parent's armor if they don't have a slot.
	# Internal organs ALWAYS use their parent's armor.
	if part.get("internal") and part.get("parent"):
		armor_part_key = part["parent"]
	elif not defender.equipment.has(armor_part_key):
		if part.get("parent"):
			armor_part_key = part["parent"]
	
	# Determine tissues to hit (Internal organs are behind parent tissues)
	var target_tissues = []
	if part.get("internal") and part.get("parent") and defender.body.has(part["parent"]):
		var parent_part = defender.body[part["parent"]]
		for t in parent_part["tissues"]:
			target_tissues.append(t)
	
	for t in part["tissues"]:
		target_tissues.append(t)
		
	if not defender.equipment.has(armor_part_key): 
		armor_part_key = "torso"

	for l_key in layers:
		var armor = defender.equipment[armor_part_key][l_key]
		if not armor or typeof(armor) != TYPE_DICTIONARY: continue
		var a_mat = MATERIALS.get(armor.get("material", "leather"), MATERIALS.leather)
		var absorbed = 0.0
		
		# Material Hardness Comparison (DF-style)
		# If weapon is softer than armor, it performs significantly worse
		var material_factor = 1.0
		if w_mat["hardness"] < a_mat["hardness"]:
			material_factor = 0.4 # Significant penalty
		elif w_mat["hardness"] > a_mat["hardness"] * 1.2:
			material_factor = 1.2 # Bonus for superior material

		if dmg_type == "blunt":
			var effective_yield = a_mat["impact_yield"] * (contact_area / 10.0)
			absorbed = min(current_dmg, armor.get("prot", 0) * (effective_yield / 100.0))
			current_dmg -= (absorbed * material_factor)
		else:
			var effective_yield = a_mat["shear_yield"] / (penetration_factor / 10.0)
			if armor.get("shear_mult"): effective_yield *= armor["shear_mult"]
			absorbed = min(current_dmg, armor.get("prot", 0) * (effective_yield / 100.0))
			current_dmg -= (absorbed * material_factor)
		
		res["armor_layers"].append(armor.get("name", "Armor"))
		if current_dmg <= 0: break

	# 5. Tissue Penetration
	var final_dmg = max(0, int(current_dmg))
	res["final_dmg"] = final_dmg
	
	# Verb determination based on damage and type
	if dmg_type == "cut":
		if final_dmg > 40: res["verb"] = "cleaves clean through"
		elif final_dmg > 25: res["verb"] = "hacks deeply into"
		elif final_dmg > 12: res["verb"] = "slashes"
		else: res["verb"] = "cuts"
	elif dmg_type == "blunt":
		if final_dmg > 40: res["verb"] = "pulverizes"
		elif final_dmg > 25: res["verb"] = "shatters"
		elif final_dmg > 12: res["verb"] = "smashes"
		else: res["verb"] = "clobbers"
	elif dmg_type == "pierce":
		if final_dmg > 30: res["verb"] = "impales"
		elif final_dmg > 15: res["verb"] = "pierces"
		else: res["verb"] = "stabs"

	# DF-style Tissue Model: Damage is Energy/Momentum
	var current_energy = float(final_dmg)
	var contact_mult = 10.0 / max(1.0, contact_area) 
	
	for tissue in target_tissues:
		if current_energy <= 0.1: break
		
		var t_dmg = 0
		var res_mult = 1.0
		
		# Determine tissue resistance based on weapon material vs tissue type
		var t_mat = MATERIALS.flesh
		if tissue["type"] == "bone": t_mat = MATERIALS.bone
		
		if dmg_type == "blunt":
			# Blunt force transfers through tissues but is absorbed by fat/muscle
			var yield_val = t_mat["impact_yield"]
			res_mult = 0.05 # Soft tissues only take 5% energy
			if tissue["type"] == "bone": res_mult = 0.6 # Bone takes 60% and resists
			
			t_dmg = min(tissue["hp"] * 2.0, current_energy * res_mult)
			current_energy -= (t_dmg * 0.5) # Force carries through
		else:
			# Sharp/Piercing: Material vs Material Yield
			var yield_val = t_mat["shear_yield"]
			
			# If weapon shear yield is much higher than tissue, it passes through easily
			var ease_of_cut = float(w_mat["shear_yield"]) / float(max(1, yield_val))
			# Pierces use contact mult to increase pressure
			if dmg_type == "pierce": ease_of_cut *= contact_mult
			
			if ease_of_cut > 2.0:
				# Razor sharp / High pressure: cuts through with minimal energy loss
				t_dmg = min(tissue["hp"], current_energy)
				current_energy -= (t_dmg / ease_of_cut)
			else:
				# Struggling to cut: loses energy fast
				t_dmg = min(tissue["hp"], current_energy)
				current_energy -= t_dmg
		
		tissue["hp"] = max(0, tissue["hp"] - int(t_dmg))
		res["tissues_hit"].append(tissue["type"])
		
		# Lethality Logic for Vitals
		if tissue["type"] == "organ" and t_dmg > 0:
			var organ_name = tissue.get("name", "")
			if organ_name == "brain":
				res["death_occurred"] = true
				res["critical_events"].append("brain_destroyed")
			elif organ_name == "heart" and tissue["hp"] <= 0:
				res["death_occurred"] = true
				res["critical_events"].append("heart_burst")
			elif organ_name == "eye" and t_dmg >= tissue["hp_max"]:
				res["critical_events"].append("eye_gouged")

		match tissue["type"]:
			"skin": res["added_bleed_rate"] += 2.0
			"fat": res["total_pain"] += 1
			"muscle": 
				res["added_bleed_rate"] += 5.0
				res["total_pain"] += 5
				if rng.randf() < 0.1:
					var is_artery = rng.randf() < 0.3
					if is_artery:
						res["added_bleed_rate"] += 50.0
						res["critical_events"].append("artery_severed")
					else:
						res["added_bleed_rate"] += 15.0
						res["critical_events"].append("vein_opened")
			"tendon":
				res["total_pain"] += 10
				if tissue["hp"] <= 0: res["critical_events"].append("tendon_snapped")
			"nerve":
				res["total_pain"] += 30
				if tissue["hp"] <= 0: res["critical_events"].append("nerve_destroyed")
			"bone":
				res["total_pain"] += 20
				if tissue["hp"] <= 0: res["critical_events"].append("bone_fractured")
			"organ":
				res["added_bleed_rate"] += 40.0
				res["total_pain"] += 30
				if tissue["hp"] <= 0:
					res["critical_events"].append("organ_failure:" + tissue.get("name", "organ"))
					if tissue.get("name") in ["heart", "brain", "spine"]:
						res["death_occurred"] = true

	# 6. Systemic Failure Checks
	var p_tol = defender.attributes.get("pain_tolerance", 10)
	var pain_threshold = 140 + (p_tol * 2) # Baseline 160 (Hardened combatants)

	if part_key == "neck" and res["tissues_hit"].has("bone") and res["tissues_hit"].size() >= 3:
		res["death_occurred"] = true
		res["critical_events"].append("decapitated")
	
	if not res["death_occurred"] and part_key in ["head", "neck", "torso"]:
		var part_hp = 0
		for t_in_part in defender.body[part_key]["tissues"]:
			part_hp += t_in_part["hp"]
		if part_hp <= 0:
			res["death_occurred"] = true
			res["critical_events"].append("part_destroyed")
			
	if res["death_occurred"]:
		defender.status["is_dead"] = true
	elif res["total_pain"] > pain_threshold:
		if not defender.status["is_downed"]:
			res["downed_occurred"] = true
			defender.status["is_downed"] = true
			res["critical_events"].append("is incapacitated by pain")
	elif res["total_pain"] > pain_threshold * 0.5:
		if not defender.status.get("is_prone", false):
			res["prone_occurred"] = true
			defender.status["is_prone"] = true
			res["critical_events"].append("is knocked down by pain")
			
	# Prone Logic
	if not defender.status["is_dead"] and not defender.status["is_downed"]:
		var l_leg_hp = 0
		for t in defender.body["l_leg"]["tissues"]: l_leg_hp += t["hp"]
		var r_leg_hp = 0
		for t in defender.body["r_leg"]["tissues"]: r_leg_hp += t["hp"]
		
		if l_leg_hp <= 0 and r_leg_hp <= 0:
			if not defender.status.get("is_prone", false):
				res["prone_occurred"] = true
				defender.status["is_prone"] = true
		elif dmg_type == "blunt" and res["final_dmg"] > 15:
			if rng.randf() < 0.3:
				res["prone_occurred"] = true
				defender.status["is_prone"] = true
				defender.status["knockdown_timer"] = 2

	# Sync HP
	defender.hp = get_total_hp(defender.body)
	if res["added_bleed_rate"] > 0:
		defender.bleed_rate = defender.bleed_rate + res["added_bleed_rate"]
		if defender.body.has(part_key):
			defender.body[part_key]["bleed_rate"] = defender.body[part_key].get("bleed_rate", 0.0) + res["added_bleed_rate"]
	
	res["remaining_energy"] = current_energy
	return res

func check_functional_integrity(u: GDUnit) -> Array:
	var s = u.status
	var messages = []
	
	# 1. Leg & Foot Integrity (Movement)
	var working_legs = 0
	for lp in ["l_leg", "r_leg"]:
		if not u.body.has(lp): continue
		var part = u.body[lp]
		var side = lp.split("_")[0]
		var foot_key = side + "_foot"
		
		# Part checks
		var lp_ok = true
		for t in part["tissues"]:
			if t["hp"] <= 0 and (t["type"] == "tendon" or t["type"] == "nerve" or t["type"] == "bone"):
				lp_ok = false; break
		
		var foot_ok = true
		if u.body.has(foot_key):
			for t in u.body[foot_key]["tissues"]:
				if t["hp"] <= 0 and (t["type"] == "tendon" or t["type"] == "nerve" or t["type"] == "bone"):
					foot_ok = false; break
		
		if lp_ok and foot_ok:
			working_legs += 1
		elif not foot_ok and not s.get(foot_key + "_notified", false):
			s[foot_key + "_notified"] = true
			messages.append("[color=orange]%s's %s foot is mangled![/color]" % [u.name, "left" if side == "l" else "right"])
			
	if working_legs == 0 and u.body.has("l_leg"):
		if not s.get("is_prone", false):
			s["is_prone"] = true
			messages.append("[color=orange]%s's legs are no longer functional! They collapse![/color]" % u.name)
	
	# 2. Arm & Hand Integrity (Attacking/Blocking)
	for side in ["l", "r"]:
		var ap = side + "_arm"
		var hp = side + "_hand"
		if not u.body.has(ap): continue
		
		var arm_part = u.body[ap]
		var arm_functional = true
		for t in arm_part["tissues"]:
			if t["hp"] <= 0 and (t["type"] == "tendon" or t["type"] == "nerve" or t["type"] == "bone"):
				arm_functional = false; break
		
		var hand_functional = true
		if u.body.has(hp):
			for t in u.body[hp]["tissues"]:
				if t["hp"] <= 0 and (t["type"] == "tendon" or t["type"] == "nerve" or t["type"] == "bone"):
					hand_functional = false; break
		
		var total_functional = arm_functional and hand_functional
		s[side + "_arm_functional"] = total_functional
		
		if not hand_functional and not s.get(hp + "_notified", false):
			s[hp + "_notified"] = true
			messages.append("[color=orange]%s's %s hand is mangled and can no longer grip![/color]" % [u.name, "left" if side == "l" else "right"])
		elif not arm_functional and not s.get(side + "_arm_notified", false):
			s[side + "_arm_notified"] = true
			messages.append("[color=orange]%s's %s arm hangs limp and useless![/color]" % [u.name, "left" if side == "l" else "right"])

	# 3. Spine Integrity (Total Paralysis)
	if u.body.has("spine"):
		var spine = u.body["spine"]
		var spine_ok = true
		for t in spine["tissues"]:
			if t["type"] == "nerve" and t["hp"] <= 0:
				spine_ok = false
				break
		
		if not spine_ok and not s.get("is_paralyzed", false):
			s["is_paralyzed"] = true
			s["is_prone"] = true
			s["is_downed"] = true
			messages.append("[color=red]%s's spine is severed! They are paralyzed![/color]" % u.name)
	
	# 4. Bleeding Status
	for p_key in u.body:
		var part = u.body[p_key]
		var br = part.get("bleed_rate", 0.0)
		if br > 0:
			var severity = "slightly"
			var color = "orange"
			if br > 40: 
				severity = "UNCONTROLLABLY (ARTERIAL)"
				color = "red"
			elif br > 15: 
				severity = "profusely"
				color = "red"
			elif br > 5: 
				severity = "heavily"
				color = "orange"
			
			messages.append("[color=%s]%s is bleeding %s from the %s![/color]" % [color, "You" if u.team == "player" else u.name, severity, part.get("name", p_key)])
			
	return messages

func part_hit_name(key: String) -> String:
	match key:
		"l_arm": return "left arm"
		"r_arm": return "right arm"
		"l_leg": return "left leg"
		"r_leg": return "right leg"
		"l_hand": return "left hand"
		"r_hand": return "right hand"
		"l_foot": return "left foot"
		"r_foot": return "right foot"
		"l_eye": return "left eye"
		"r_eye": return "right eye"
		"gut": return "abdomen"
	return key

const GOVERNOR_PERSONALITIES = ["builder", "greedy", "balanced", "cautious"]
const LORD_DOCTRINES = ["conqueror", "defender", "raider", "merchant_prince"]

const MATERIAL_TIERS = {
	1: "leather",
	2: "copper",
	3: "bronze",
	4: "iron",
	5: "steel"
}

# --- CHARACTER CREATION DATA (CDDA + KENSHI STYLE) ---

const SCENARIOS = {
	"trader_caravan": {
		"name": "Trader Caravan",
		"desc": "You are a minor merchant with a small wagon and some bodyguards. Life is stable, but slow.",
		"points": 0,
		"gold": 2500,
		"fame": 10,
		"location_type": "city",
		"items": ["mace", "heater_shield"], 
		"start_roster": [{"type": "recruit", "tier": 1}, {"type": "recruit", "tier": 1}, {"type": "recruit", "tier": 1}, {"type": "recruit", "tier": 1}],
		"relations": {}
	},
	"escaped_thrall": {
		"name": "Escaped Thrall",
		"desc": "You have escaped from a Salt Mine with a fellow prisoner. You have nothing but rags.",
		"points": 10,
		"gold": 0,
		"fame": -10,
		"location_type": "wilds",
		"items": ["shirt"],
		"start_roster": [{"type": "laborer", "tier": 1}],
		"status": ["starving", "wanted"],
		"relations": {"faction_0": -100} 
	},
	"failed_heir": {
		"name": "Failed Heir",
		"desc": "Your family was ousted in a coup. You have high-quality gear and a few loyal retainers.",
		"points": -5,
		"gold": 5000,
		"fame": 50,
		"location_type": "capital",
		"items": ["shortsword", "hauberk", "helmet"],
		"start_roster": [{"type": "recruit", "tier": 2}, {"type": "recruit", "tier": 2}],
		"status": ["hunted"],
		"relations": {"faction_1": -100}
	},
	"rock_bottom": {
		"name": "Rock Bottom",
		"desc": "The desert took everything. You start in the middle of nowhere with a missing arm and no clothes. Alone.",
		"points": 25,
		"gold": 0,
		"fame": 0,
		"location_type": "desert",
		"items": [],
		"start_roster": [],
		"status": ["missing_arm", "starving"]
	},
	"holy_crusade": {
		"name": "Holy Crusade",
		"desc": "You are a pilgrim in a massive holy war. You have brothers-in-arms but no personal freedom.",
		"points": 5,
		"gold": 500,
		"fame": 20,
		"location_type": "town",
		"items": ["spear", "tunic"],
		"start_roster": [{"type": "recruit", "tier": 1}, {"type": "recruit", "tier": 1}, {"type": "recruit", "tier": 1}, {"type": "recruit", "tier": 1}, {"type": "recruit", "tier": 1}],
		"relations": {"church": 100, "commonwealth": -50}
	},
	"shipwrecked": {
		"name": "Shipwrecked",
		"desc": "Washed up on a strange shore. You are the sole survivor.",
		"points": 5,
		"gold": 0,
		"fame": 0,
		"location_type": "coastal",
		"items": ["club"],
		"start_roster": [],
		"status": ["wet", "exhausted"]
	}
}

const PROFESSIONS = {
	"mercenary": {
		"name": "Mercenary",
		"desc": "A professional soldier of fortune.",
		"cost": 5,
		"stats": {"strength": 2, "endurance": 2, "agility": 1},
		"skills": {"spear_use": 15, "shield_use": 10, "armor_handling": 10},
		"equipment": ["gambeson", "spear", "helmet"]
	},
	"laborer": {
		"name": "Laborer",
		"desc": "Spent years in the fields or mines. Strong back, empty pockets.",
		"cost": 0,
		"stats": {"strength": 3, "endurance": 3},
		"skills": {"improvised": 20},
		"equipment": ["trousers", "club"]
	},
	"scholar": {
		"name": "Scholar",
		"desc": "A product of the Great Libraries. Wise but physically fragile.",
		"cost": 3,
		"stats": {"intelligence": 5, "strength": -2},
		"skills": {"dagger_knife": 10},
		"equipment": ["tunic", "dagger"]
	},
	"thief": {
		"name": "Thief",
		"desc": "A shadow in the city streets.",
		"cost": 5,
		"stats": {"agility": 4, "intelligence": 1},
		"skills": {"dagger_knife": 10, "dodging": 10},
		"equipment": ["shirt", "dagger"]
	},
	"hunter": {
		"name": "Hunter",
		"desc": "Lives off the land, far from the king's taxes.",
		"cost": 2,
		"stats": {"agility": 2, "endurance": 2},
		"skills": {"archery": 20, "dagger_knife": 15},
		"equipment": ["tunic", "shortbow", "dagger"]
	},
	"blacksmith": {
		"name": "Blacksmith",
		"desc": "Forged steel for others, now wields it for themselves.",
		"cost": 4,
		"stats": {"strength": 4, "endurance": 1},
		"skills": {"mace_hammer": 15},
		"equipment": ["tunic", "mace"]
	}
}

const TRAITS = {
	"tough": {
		"name": "Tough",
		"desc": "High pain tolerance and thick skin. +15% Health to all parts.",
		"cost": 5,
		"type": "positive"
	},
	"fast_runner": {
		"name": "Fast Runner",
		"desc": "Your legs are like springs. +20% Movement speed.",
		"cost": 4,
		"type": "positive"
	},
	"eagle_eyed": {
		"name": "Eagle Eyed",
		"desc": "You can spot a bandit a mile away. +3 Vision range.",
		"cost": 3,
		"type": "positive"
	},
	"strong_stomach": {
		"name": "Strong Stomach",
		"desc": "You can eat anything without getting sick.",
		"cost": 2,
		"type": "positive"
	},
	"night_vision": {
		"name": "Night Vision",
		"desc": "You see better in the dark. Ignore 50% of night penalties.",
		"cost": 4,
		"type": "positive"
	},
	"quick_learner": {
		"name": "Quick Learner",
		"desc": "You pick up skills fast. +20% XP gain.",
		"cost": 6,
		"type": "positive"
	},
	"weak": {
		"name": "Weak",
		"desc": "Lacks physical power. -3 Strength.",
		"cost": -4,
		"type": "negative"
	},
	"clumsy": {
		"name": "Clumsy",
		"desc": "Prone to tripping and missing. -5 Accuracy.",
		"cost": -3,
		"type": "negative"
	},
	"addict": {
		"name": "Addict",
		"desc": "Needs constant stimulation. Requires Ale or Spices to avoid withdrawal.",
		"cost": -5,
		"type": "negative"
	},
	"near_sighted": {
		"name": "Near Sighted",
		"desc": "Poor long-distance vision. -3 Vision range.",
		"cost": -4,
		"type": "negative"
	},
	"hemophiliac": {
		"name": "Hemophiliac",
		"desc": "Bleed much faster when wounded.",
		"cost": -6,
		"type": "negative"
	},
	"frail": {
		"name": "Frail",
		"desc": "Brittle bones and thin skin. -15% Health to all parts.",
		"cost": -5,
		"type": "negative"
	}
}

const ARCHETYPES = {
	"laborer": {
		"name": "Laborer", "min_tier": 0, "role": "levy",
		"attributes": {"strength": 8, "endurance": 8, "agility": 8, "balance": 8, "pain_tolerance": 8},
		"skills": {"improvised": 15, "dodging": 5},
		"equipment": {
			"main_hand": ["pitchfork", "wood"],
			"torso_under": ["shirt", "linen"],
			"legs_under": ["trousers", "wool"]
		}
	},
	"spearman": {
		"name": "Spearman", "min_tier": 1, "role": "frontline",
		"attributes": {"strength": 10, "endurance": 11, "agility": 9, "balance": 10, "pain_tolerance": 10},
		"skills": {"spear_use": 25, "shield_use": 20, "dodging": 10, "armor_handling": 10},
		"equipment": {
			"main_hand": ["spear", "tier_mat"],
			"off_hand": ["buckler", "tier_mat"],
			"head_armor": ["helmet", "tier_mat"],
			"head_under": ["coif", "wool"],
			"torso_armor": ["cuirass", "tier_mat"],
			"torso_under": ["gambeson", "wool"],
			"arms_armor": ["vambraces", "tier_mat"],
			"legs_armor": ["greaves", "tier_mat"],
			"feet_over": ["boots", "leather"],
			"hands_under": ["gloves", "leather"]
		}
	},
	"footman": {
		"name": "Footman", "min_tier": 2, "role": "frontline",
		"attributes": {"strength": 11, "endurance": 12, "agility": 10, "balance": 11, "pain_tolerance": 12},
		"skills": {"swordsmanship": 35, "shield_use": 30, "dodging": 15, "armor_handling": 25},
		"equipment": {
			"main_hand": ["longsword", "tier_mat"],
			"off_hand": ["heater_shield", "wood"],
			"head_armor": ["helmet", "tier_mat"],
			"head_under": ["coif", "wool"],
			"torso_armor": ["hauberk", "tier_mat"],
			"torso_under": ["gambeson", "wool"],
			"arms_armor": ["pauldrons", "tier_mat"],
			"legs_armor": ["greaves", "tier_mat"],
			"feet_armor": ["sabatons", "tier_mat"],
			"hands_armor": ["gauntlets", "tier_mat"]
		}
	},
	"vanguard": {
		"name": "Vanguard", "min_tier": 2, "role": "shock",
		"attributes": {"strength": 14, "endurance": 12, "agility": 11, "balance": 12, "pain_tolerance": 15},
		"skills": {"axe_fighting": 40, "swordsmanship": 15, "dodging": 10, "armor_handling": 20},
		"equipment": {
			"main_hand": ["battle_axe", "tier_mat"],
			"head_armor": ["helmet", "tier_mat"],
			"torso_armor": ["brigandine", "tier_mat"],
			"torso_under": ["gambeson", "wool"],
			"arms_armor": ["vambraces", "tier_mat"],
			"legs_armor": ["greaves", "tier_mat"],
			"feet_over": ["boots", "leather"],
			"hands_armor": ["gauntlets", "tier_mat"]
		}
	},
	"pikeman": {
		"name": "Pikeman", "min_tier": 2, "role": "frontline",
		"attributes": {"strength": 12, "endurance": 13, "agility": 10, "balance": 12, "pain_tolerance": 12},
		"skills": {"spear_use": 45, "dodging": 5, "armor_handling": 15},
		"equipment": {
			"main_hand": ["pike", "tier_mat"],
			"head_armor": ["helmet", "tier_mat"],
			"torso_armor": ["cuirass", "tier_mat"],
			"torso_under": ["gambeson", "wool"],
			"arms_armor": ["vambraces", "tier_mat"],
			"legs_armor": ["greaves", "tier_mat"],
			"feet_over": ["boots", "leather"]
		}
	},
	"archer": {
		"name": "Archer", "min_tier": 1, "role": "ranged",
		"attributes": {"strength": 10, "endurance": 12, "agility": 14, "balance": 11, "pain_tolerance": 9},
		"skills": {"archery": 40, "dagger_knife": 20, "dodging": 25, "armor_handling": 5},
		"equipment": {
			"main_hand": ["shortbow", "wood"],
			"ammo": ["arrows", "tier_mat"],
			"off_hand": ["dagger", "tier_mat"],
			"head_armor": ["cap", "leather"],
			"torso_under": ["tunic", "linen"],
			"legs_under": ["trousers", "wool"],
			"feet_over": ["boots", "leather"]
		}
	},
	"crossbowman": {
		"name": "Crossbowman", "min_tier": 3, "role": "ranged",
		"attributes": {"strength": 12, "endurance": 11, "agility": 9, "balance": 10, "pain_tolerance": 11},
		"skills": {"crossbows": 45, "shield_use": 25, "dodging": 5, "armor_handling": 25},
		"equipment": {
			"main_hand": ["crossbow", "wood"],
			"ammo": ["bolts", "tier_mat"],
			"off_hand": ["pavise", "wood"],
			"head_armor": ["helmet", "tier_mat"],
			"torso_armor": ["hauberk", "tier_mat"],
			"torso_under": ["gambeson", "wool"],
			"legs_armor": ["greaves", "tier_mat"],
			"feet_over": ["boots", "leather"]
		}
	},
	"knight": {
		"name": "Knight", "min_tier": 4, "role": "shock",
		"attributes": {"strength": 15, "endurance": 15, "agility": 12, "balance": 14, "pain_tolerance": 16},
		"skills": {"swordsmanship": 50, "mace_hammer": 35, "shield_use": 40, "armor_handling": 50, "dodging": 10},
		"equipment": {
			"main_hand": ["greatsword", "tier_mat"],
			"head_armor": ["great_helm", "tier_mat"],
			"head_under": ["coif", "wool"],
			"torso_armor": ["breastplate", "tier_mat"],
			"torso_over": ["hauberk", "tier_mat"],
			"torso_under": ["gambeson", "wool"],
			"arms_armor": ["pauldrons", "tier_mat"],
			"legs_armor": ["greaves", "tier_mat"],
			"feet_armor": ["sabatons", "tier_mat"],
			"hands_armor": ["gauntlets", "tier_mat"],
			"torso_cover": ["surcoat", "linen"]
		}
	}
}

# --- CALENDAR & NAMES ---
# Loaded from data/names.json
static var MONTH_NAMES: Array:
	get:
		return NamesData.get_month_names()

static var FIRST_NAMES: Array:
	get:
		return NamesData.get_first_names()

static var LAST_NAMES: Array:
	get:
		return NamesData.get_last_names()

func get_default_body(hp_scale: float = 1.0) -> Dictionary:
	var body = {}
	
	# Tissue Templates
	var t_skin = {"type": "skin", "hp": int(2 * hp_scale), "hp_max": int(2 * hp_scale), "thick": 1}
	var t_fat = {"type": "fat", "hp": int(4 * hp_scale), "hp_max": int(4 * hp_scale), "thick": 5}
	var t_muscle = {"type": "muscle", "hp": int(12 * hp_scale), "hp_max": int(12 * hp_scale), "thick": 10}
	var t_tendon = {"type": "tendon", "hp": int(5 * hp_scale), "hp_max": int(5 * hp_scale), "thick": 2, "structural": true}
	var t_nerve = {"type": "nerve", "hp": int(2 * hp_scale), "hp_max": int(2 * hp_scale), "thick": 1, "structural": true}
	var t_bone = {"type": "bone", "hp": int(25 * hp_scale), "hp_max": int(25 * hp_scale), "thick": 10, "structural": true}
	var _t_organ = {"type": "organ", "hp": int(10 * hp_scale), "hp_max": int(10 * hp_scale), "thick": 5}
	
	body["head"] = {
		"name": "head", "parent": null, "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [t_skin.duplicate(), t_fat.duplicate(), t_muscle.duplicate(), {"type": "bone", "name": "skull", "hp": int(30 * hp_scale), "hp_max": int(30 * hp_scale), "thick": 8, "structural": true}, t_nerve.duplicate()]
	}
	body["brain"] = {
		"name": "brain", "parent": "head", "internal": true, "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [{"type": "organ", "name": "brain", "hp": int(5 * hp_scale), "hp_max": int(5 * hp_scale), "thick": 10, "structural": true}]
	}
	body["l_eye"] = {
		"name": "left eye", "parent": "head", "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [{"type": "organ", "name": "eye", "hp": int(2 * hp_scale), "hp_max": int(2 * hp_scale), "thick": 2, "structural": true}]
	}
	body["r_eye"] = {
		"name": "right eye", "parent": "head", "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [{"type": "organ", "name": "eye", "hp": int(2 * hp_scale), "hp_max": int(2 * hp_scale), "thick": 2, "structural": true}]
	}
	
	body["neck"] = {
		"name": "neck", "parent": null, "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [t_skin.duplicate(), t_muscle.duplicate(), {"type": "bone", "name": "vertebrae", "hp": int(15 * hp_scale), "hp_max": int(15 * hp_scale), "thick": 5, "structural": true}, t_nerve.duplicate()]
	}
	
	body["torso"] = {
		"name": "torso", "parent": null, "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [t_skin.duplicate(), t_fat.duplicate(), t_muscle.duplicate(), {"type": "bone", "name": "ribs", "hp": int(20 * hp_scale), "hp_max": int(20 * hp_scale), "thick": 5, "structural": true}]
	}
	body["heart"] = {
		"name": "heart", "parent": "torso", "internal": true, "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [{"type": "organ", "name": "heart", "hp": int(8 * hp_scale), "hp_max": int(8 * hp_scale), "thick": 5, "structural": true}]
	}
	body["l_lung"] = {
		"name": "left lung", "parent": "torso", "internal": true, "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [{"type": "organ", "name": "lung", "hp": int(8 * hp_scale), "hp_max": int(8 * hp_scale), "thick": 10, "structural": true}]
	}
	body["r_lung"] = {
		"name": "right lung", "parent": "torso", "internal": true, "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [{"type": "organ", "name": "lung", "hp": int(8 * hp_scale), "hp_max": int(8 * hp_scale), "thick": 10, "structural": true}]
	}
	body["liver"] = {
		"name": "liver", "parent": "torso", "internal": true, "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [{"type": "organ", "name": "liver", "hp": int(10 * hp_scale), "hp_max": int(10 * hp_scale), "thick": 15}]
	}
	body["spleen"] = {
		"name": "spleen", "parent": "torso", "internal": true, "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [{"type": "organ", "name": "spleen", "hp": int(4 * hp_scale), "hp_max": int(4 * hp_scale), "thick": 5}]
	}
	body["l_kidney"] = {
		"name": "left kidney", "parent": "torso", "internal": true, "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [{"type": "organ", "name": "kidney", "hp": int(4 * hp_scale), "hp_max": int(4 * hp_scale), "thick": 5}]
	}
	body["r_kidney"] = {
		"name": "right kidney", "parent": "torso", "internal": true, "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [{"type": "organ", "name": "kidney", "hp": int(4 * hp_scale), "hp_max": int(4 * hp_scale), "thick": 5}]
	}
	body["gut"] = {
		"name": "gut", "parent": "torso", "internal": true, "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [{"type": "organ", "name": "intestines", "hp": int(15 * hp_scale), "hp_max": int(15 * hp_scale), "thick": 20}]
	}
	body["spine"] = {
		"name": "spine", "parent": "torso", "internal": true, "bleed_rate": 0.0, "internal_bleeding": 0.0,
		"tissues": [{"type": "bone", "name": "spine", "hp": int(20 * hp_scale), "hp_max": int(20 * hp_scale), "thick": 5, "structural": true}, t_nerve.duplicate()]
	}
	
	for side in ["l", "r"]:
		var s_name = "left" if side == "l" else "right"
		body[side + "_arm"] = {
			"name": s_name + " arm", "parent": "torso", "bleed_rate": 0.0, "internal_bleeding": 0.0,
			"tissues": [t_skin.duplicate(), t_fat.duplicate(), t_muscle.duplicate(), t_bone.duplicate(), t_tendon.duplicate(), t_nerve.duplicate()]
		}
		body[side + "_hand"] = {
			"name": s_name + " hand", "parent": side + "_arm", "bleed_rate": 0.0, "internal_bleeding": 0.0,
			"tissues": [t_skin.duplicate(), t_muscle.duplicate(), {"type": "bone", "hp": 10, "hp_max": 10, "thick": 3, "structural": true}, t_tendon.duplicate(), t_nerve.duplicate()]
		}
		body[side + "_leg"] = {
			"name": s_name + " leg", "parent": "torso", "bleed_rate": 0.0, "internal_bleeding": 0.0,
			"tissues": [t_skin.duplicate(), t_fat.duplicate(), t_muscle.duplicate(), t_bone.duplicate(), t_tendon.duplicate(), t_nerve.duplicate()]
		}
		body[side + "_foot"] = {
			"name": s_name + " foot", "parent": side + "_leg", "bleed_rate": 0.0, "internal_bleeding": 0.0,
			"tissues": [t_skin.duplicate(), t_muscle.duplicate(), {"type": "bone", "hp": 10, "hp_max": 10, "thick": 3, "structural": true}, t_tendon.duplicate(), t_nerve.duplicate()]
		}
		
	return body

func get_total_hp(body: Dictionary) -> int:
	var total = 0
	for p in body:
		for tissue in body[p]["tissues"]:
			total += tissue["hp"]
	return total

func generate_laborer(rng: RandomNumberGenerator) -> GDUnit:
	var r_name = "%s %s" % [
		FIRST_NAMES[rng.randi() % FIRST_NAMES.size()],
		LAST_NAMES[rng.randi() % LAST_NAMES.size()]
	]
	
	var body = get_default_body(0.8)
	var total_hp = get_total_hp(body)
	
	var recruit = GDUnit.new(r_name)
	recruit.type = "laborer"
	recruit.assigned_class = ""
	recruit.xp = 0
	recruit.hp_max = total_hp
	recruit.hp = total_hp
	recruit.blood_max = 500.0
	recruit.blood_current = 500.0
	recruit.bleed_rate = 0.0
	recruit.status = {
		"is_downed": false,
		"is_dead": false,
		"is_prone": false
	}
	recruit.body = body
	# Equipment dict is initialized in GDUnit.new()
	recruit.cost = 10
	
	var archetype = ARCHETYPES["laborer"]
	recruit.archetype = "laborer"
	
	# Apply Attributes and Skills from Archetype
	for attr in recruit.attributes:
		if archetype.has("attributes") and archetype["attributes"].has(attr):
			recruit.attributes[attr] = archetype["attributes"][attr]
	for sk in recruit.skills:
		if archetype.has("skills") and archetype["skills"].has(sk):
			recruit.skills[sk] = archetype["skills"][sk]
	
	# Equip from Archetype
	for slot_key in archetype["equipment"]:
		var item_info = archetype["equipment"][slot_key]
		var item = create_item_data(item_info[0], item_info[1])
		if not item: continue
		
		if slot_key == "main_hand":
			# Randomize weapon slightly for variety
			var weapons = [["pitchfork", "wood"], ["club", "wood"], ["dagger", "iron"]]
			var w_info = weapons[rng.randi() % weapons.size()]
			recruit.equipment["main_hand"] = create_item_data(w_info[0], w_info[1])
		else:
			var parts = slot_key.split("_")
			if parts.size() < 2:
				apply_armor_to_recruit(recruit, item)
			else:
				var part_group = parts[0]
				var target_layer = parts[1]
				var target_slots = []
				match part_group:
					"head": target_slots = ["head"]
					"torso": target_slots = ["torso"]
					"arms": target_slots = ["l_arm", "r_arm"]
					"legs": target_slots = ["l_leg", "r_leg"]
					"hands": target_slots = ["l_hand", "r_hand"]
					"feet": target_slots = ["l_foot", "r_foot"]
				
				for s in target_slots:
					if recruit.equipment.has(s):
						recruit.equipment[s][target_layer] = item
	
	recruit.speed = calculate_unit_speed(recruit)
	recruit.base_speed = recruit.speed

	return recruit

func generate_monster(rng: RandomNumberGenerator, m_type: String, hp_scale: float = 1.0) -> GDUnit:
	var m_name = m_type.capitalize()
	var body = get_default_body(hp_scale)
	var total_hp = get_total_hp(body)
	
	var monster = GDUnit.new(m_name)
	monster.type = m_type
	monster.hp_max = total_hp
	monster.hp = total_hp
	monster.blood_max = 500.0
	monster.blood_current = 500.0
	monster.status = {
		"is_downed": false,
		"is_dead": false,
		"is_prone": false
	}
	monster.body = body
	
	# Basic monster stats
	monster.attributes = {
		"strength": 12,
		"endurance": 12,
		"agility": 8,
		"balance": 10,
		"pain_tolerance": 20
	}
	monster.skills = {
		"swordsmanship": 20,
		"dodging": 10,
		"improvised": 20
	}
	
	if m_type == "skeleton":
		monster.symbol = 's'
		monster.attributes["pain_tolerance"] = 100
		monster.equipment["main_hand"] = create_item_data("shortsword", "iron", "rusty")
	elif m_type == "zombie":
		monster.symbol = 'z'
		monster.attributes["strength"] = 14
		monster.attributes["agility"] = 5
		monster.equipment["main_hand"] = create_item_data("club", "wood")
	elif m_type == "spider":
		monster.symbol = 's' # lowercase s for spider
		monster.attributes["agility"] = 14
		monster.attributes["strength"] = 8
		monster.skills["dodging"] = 30
		monster.equipment["main_hand"] = create_item_data("fist", "chitin") # Using fist as bite
	elif m_type == "goblin":
		monster.symbol = 'g'
		monster.attributes["agility"] = 12
		monster.attributes["strength"] = 9
		monster.skills["swordsmanship"] = 15
		monster.skills["dodging"] = 20
		monster.equipment["main_hand"] = create_item_data("dagger", "iron")
	elif m_type == "orc":
		monster.symbol = 'o'
		monster.attributes["strength"] = 15
		monster.attributes["endurance"] = 14
		monster.attributes["pain_tolerance"] = 30
		monster.skills["swordsmanship"] = 30
		monster.equipment["main_hand"] = create_item_data("battle_axe", "iron")
	elif m_type == "wraith":
		monster.symbol = 'w'
		monster.attributes["agility"] = 16
		monster.attributes["pain_tolerance"] = 150 # Hard to "hurt"
		monster.skills["dodging"] = 50
		monster.equipment["main_hand"] = create_item_data("fist", "flesh") # Spectral touch
	elif m_type == "corrupted_guard":
		monster.symbol = 'G'
		monster.attributes["strength"] = 11
		monster.attributes["endurance"] = 13
		monster.skills["swordsmanship"] = 40
		monster.skills["shield_use"] = 40
		monster.equipment["main_hand"] = create_item_data("mace", "iron")
		monster.equipment["off_hand"] = create_item_data("heater_shield", "iron")
		monster.equipment["torso_armor"] = create_item_data("hauberk", "iron")
	elif m_type == "rat":
		monster.symbol = 'r'
		monster.attributes["agility"] = 12
		monster.attributes["strength"] = 4
		monster.hp_max = int(monster.hp_max * 0.4)
		monster.hp = monster.hp_max
		monster.equipment["main_hand"] = create_item_data("fist", "flesh")
	elif m_type == "troll":
		monster.symbol = 'T'
		monster.attributes["strength"] = 18
		monster.attributes["endurance"] = 16
		monster.attributes["pain_tolerance"] = 40
		monster.skills["improvised"] = 30
		monster.equipment["main_hand"] = create_item_data("fist", "flesh") # Claw/Slam
	elif m_type == "draugr":
		monster.symbol = 'D'
		monster.attributes["strength"] = 13
		monster.attributes["endurance"] = 13
		monster.attributes["pain_tolerance"] = 80
		monster.skills["swordsmanship"] = 35
		monster.equipment["main_hand"] = create_item_data("longsword", "iron", "ancient")
	elif m_type == "falmer":
		monster.symbol = 'F'
		monster.attributes["agility"] = 15
		monster.attributes["strength"] = 10
		monster.skills["dodging"] = 40
		monster.skills["swordsmanship"] = 30
		monster.equipment["main_hand"] = create_item_data("shortsword", "chitin")
	elif m_type == "lich":
		monster.symbol = 'L'
		monster.attributes["pain_tolerance"] = 200
		monster.attributes["endurance"] = 15
		monster.skills["improvised"] = 50 
		monster.equipment["main_hand"] = create_item_data("mace", "silver")
	elif m_type == "imp":
		monster.symbol = 'i'
		monster.attributes["agility"] = 18
		monster.attributes["strength"] = 5
		monster.skills["dodging"] = 60
		monster.equipment["main_hand"] = create_item_data("fist", "flesh")
	elif m_type == "daedra":
		monster.symbol = 'D' # Capitalized for danger
		monster.attributes["strength"] = 16
		monster.attributes["agility"] = 12
		monster.attributes["pain_tolerance"] = 60
		monster.skills["swordsmanship"] = 50
		monster.equipment["main_hand"] = create_item_data("longsword", "steel", "daedric")
		monster.equipment["torso_armor"] = create_item_data("cuirass", "steel", "daedric")
	elif m_type == "hagraven":
		monster.symbol = 'H'
		monster.attributes["agility"] = 14
		monster.attributes["strength"] = 12
		monster.skills["improvised"] = 40
		monster.equipment["main_hand"] = create_item_data("fist", "bone") # Long talons
	elif m_type == "centurion":
		monster.symbol = 'C'
		monster.attributes["strength"] = 25
		monster.attributes["endurance"] = 30
		monster.attributes["agility"] = 4
		monster.attributes["pain_tolerance"] = 500 # Mechanical
		monster.skills["improvised"] = 40
		monster.hp_max = int(monster.hp_max * 2.5) # Massive bulk
		monster.hp = monster.hp_max
		monster.equipment["main_hand"] = create_item_data("maul", "bronze") # Steam hammer
		# Mechanical units don't bleed as much (represented here by high starting blood)
		monster.blood_max = 2000.0
		monster.blood_current = 2000.0

	monster.speed = calculate_unit_speed(monster)
	monster.base_speed = monster.speed
	
	return monster

func generate_recruit(rng: RandomNumberGenerator, tier: int) -> GDUnit:
	var r_name = "%s %s" % [
		FIRST_NAMES[rng.randi() % FIRST_NAMES.size()],
		LAST_NAMES[rng.randi() % LAST_NAMES.size()]
	]
	
	var body = get_default_body(1.0)
	var total_hp = get_total_hp(body)
	
	var recruit = GDUnit.new(r_name)
	recruit.type = "recruit"
	recruit.tier = tier
	recruit.hp_max = total_hp
	recruit.hp = total_hp
	recruit.blood_max = 500.0
	recruit.blood_current = 500.0
	recruit.bleed_rate = 0.0
	recruit.status = {
		"is_downed": false,
		"is_dead": false,
		"is_prone": false
	}
	recruit.body = body
	recruit.cost = 50 # Base fee
	
	# Pick Material based on Tier
	var material = MATERIAL_TIERS.get(tier, "iron")
	
	# Pick Archetype (Class) with weighted tiering
	var valid_archetypes = []
	var max_found_tier = -1
	
	# First, find the highest available archetype tier for this unit's tier
	for a_key in ARCHETYPES:
		var a_tier = ARCHETYPES[a_key].get("min_tier", 0)
		if a_tier <= tier:
			if a_tier > max_found_tier:
				max_found_tier = a_tier
				
	# Now only include archetypes that are close to the max found tier
	# This ensures Tier 3 units don't become Tier 0 laborers
	for a_key in ARCHETYPES:
		var a_tier = ARCHETYPES[a_key].get("min_tier", 0)
		if a_tier <= tier and a_tier >= max_found_tier - 1:
			valid_archetypes.append(a_key)
	
	if valid_archetypes.size() == 0:
		valid_archetypes.append("spearman")
		
	var a_key = valid_archetypes[rng.randi() % valid_archetypes.size()]
	var archetype = ARCHETYPES[a_key]
	recruit.archetype = a_key
	
	# Apply Attributes and Skills from Archetype
	for attr in recruit.attributes:
		if archetype.has("attributes") and archetype["attributes"].has(attr):
			recruit.attributes[attr] = archetype["attributes"][attr]
	for sk in recruit.skills:
		if archetype.has("skills") and archetype["skills"].has(sk):
			recruit.skills[sk] = archetype["skills"][sk]
			
	# Naming Logic
	var arch_name = archetype["name"]
	var display_name = "%s %s" % [material.capitalize(), arch_name]
	
	# Special names for flavor
	if material == "iron" and arch_name == "Footman": display_name = "Man-at-Arms"
	elif material == "steel" and arch_name == "Footman": display_name = "Sergeant"
	elif material == "steel" and arch_name == "Knight": display_name = "Paladin"
	elif material == "copper" and arch_name == "Spearman": display_name = "Levy Spearman"
	
	recruit.name = "%s (%s)" % [recruit.name, display_name]
	
	# Equip from Archetype
	var final_equipment = archetype["equipment"].duplicate()
	
	for slot_key in final_equipment:
		var item_info = final_equipment[slot_key]
		var item_name = item_info[0]
		var item_mat = item_info[1]
		
		if item_mat == "tier_mat":
			item_mat = material
			
		var item = create_item_data(item_name, item_mat)
		if not item: continue
		
		if slot_key == "main_hand":
			recruit.equipment["main_hand"] = item
		elif slot_key == "off_hand":
			recruit.equipment["off_hand"] = item
		elif slot_key == "ammo":
			recruit.equipment["ammo"] = item
		else:
			# Handle complex slot mapping (e.g. "arms_armor", "head_under")
			var parts = slot_key.split("_")
			if parts.size() < 2:
				# Fallback for simple slots if they still exist
				apply_armor_to_recruit(recruit, item)
			else:
				var part_group = parts[0] # head, torso, arms, legs, hands, feet
				var target_layer = parts[1] # under, over, armor, cover
				
				# Map part groups to actual slot keys in GDUnit
				var target_slots = []
				match part_group:
					"head": target_slots = ["head"]
					"torso": target_slots = ["torso"]
					"arms": target_slots = ["l_arm", "r_arm"]
					"legs": target_slots = ["l_leg", "r_leg"]
					"hands": target_slots = ["l_hand", "r_hand"]
					"feet": target_slots = ["l_foot", "r_foot"]
				
				for s in target_slots:
					if recruit.equipment.has(s):
						recruit.equipment[s][target_layer] = item
				
		recruit.cost += get_item_value(item)

	recruit.speed = calculate_unit_speed(recruit)
	recruit.base_speed = recruit.speed

	return recruit

func is_valid_material(item_id: String, mat_key: String) -> bool:
	var item_base = ITEMS.get(item_id, {})
	if item_base.is_empty(): return true
	
	var type = item_base.get("type", "")
	
	if mat_key in ["flesh", "bone", "chitin"]:
		return item_base.get("material") == mat_key
	
	if type == "weapon":
		if item_id in ["shortbow", "longbow", "crossbow", "club"]:
			return mat_key == "wood" or mat_key in ["copper", "iron", "steel"]
		return mat_key in ["copper", "iron", "steel", "bronze", "silver", "gold", "tin"]
	elif type == "ammo":
		return mat_key in ["copper", "iron", "steel", "bronze", "wood"]
	elif type == "shield":
		return mat_key in ["wood", "copper", "iron", "steel", "bronze"]
	elif type == "armor":
		var layer = item_base.get("layer", "")
		if layer == "under":
			return mat_key in ["cloth", "linen", "wool", "silk", "leather"]
		if layer == "armor":
			return mat_key in ["cloth", "linen", "wool", "silk", "leather", "copper", "iron", "steel", "bronze", "tin", "wood"] # wooden shields/armor exist
		if layer == "over":
			return mat_key in ["cloth", "linen", "wool", "silk", "leather", "iron", "steel"]
		if layer == "cover":
			return mat_key in ["cloth", "linen", "wool", "silk", "leather"]
	elif type == "transport":
		if item_id in ["mule", "horse"]:
			return mat_key == "leather" or mat_key == "cloth" # Not ideal but prevents "iron horse" in CC
		return mat_key == "wood" or mat_key in ["iron", "steel"] # Carts
			
	return true

func get_valid_material(type_key: String, mat_key: String) -> String:
	if is_valid_material(type_key, mat_key):
		return mat_key
	
	var item_base = ITEMS.get(type_key, {})
	var type = item_base.get("type", "")
	
	if type == "weapon":
		if type_key in ["shortbow", "longbow", "crossbow"]: return "wood"
		return "iron"
	elif type == "shield":
		return "wood"
	elif type == "armor":
		var layer = item_base.get("layer", "")
		if layer == "under": return "wool"
		if layer == "armor": return "leather"
		return "wool"
		
	return mat_key

func create_item_data(id: String, mat: String, qual: String = "common") -> Dictionary:
	var base = ITEMS[id].duplicate()
	base["id"] = id
	base["material"] = get_valid_material(id, mat)
	base["quality"] = qual
	
	# Update Name with Material
	if base.has("name") and base["material"] != "flesh":
		var q_prefix = ""
		if qual != "common" and qual != "standard":
			q_prefix = qual.capitalize() + " "
		base["name"] = "%s%s %s" % [q_prefix, base["material"].capitalize(), base["name"]]
	
	# Quality Multipliers
	var q_mult = 1.0
	match qual:
		"shoddy", "poor", "rusty": q_mult = 0.6
		"average", "standard", "common": q_mult = 1.0
		"fine", "well_made": q_mult = 1.3
		"masterwork": q_mult = 1.8
		"legendary": q_mult = 2.5
	
	if base.has("dmg"): base["dmg"] = int(base["dmg"] * q_mult)
	if base.has("prot"): base["prot"] = int(base["prot"] * q_mult)
	
	# Apply Ammunition Multipliers based on material
	if base["type"] == "ammo" and (id == "arrows" or id == "bolts"):
		match base["material"]:
			"copper":
				base["dmg_mod"] = -2
				base["penetration_mod"] = 0.8
			"iron":
				base["dmg_mod"] = 0
				base["penetration_mod"] = 1.0
			"steel":
				base["dmg_mod"] = 2 if id == "arrows" else 3
				base["penetration_mod"] = 1.5 if id == "arrows" else 1.8
	
	# Calculate Weight based on Volume and Density
	var m_data = MATERIALS.get(base["material"], MATERIALS.iron)
	if base.has("volume"):
		base["weight"] = base["volume"] * m_data["density"]
	elif base.has("weight"):
		# For armor that already has a weight, scale it slightly by density relative to iron (10)
		base["weight"] = base["weight"] * (m_data["density"] / 10.0)
		
	return base

func get_item_value(item: Dictionary) -> int:
	var val = 10 # Base
	if item.has("prot"): val += item["prot"] * 2
	if item.has("dmg"): val += item["dmg"] * 3
	if item.has("material"):
		var m = MATERIALS.get(item["material"], {"hardness": 10})
		val += m["hardness"] / 2
	return val

static func get_unit_equipment_weight(u: GDUnit) -> float:
	var w = 0.0
	var eq = u.equipment
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

func calculate_unit_speed(u: GDUnit) -> float:
	var spd = 0.6
	match u.type:
		"commander": spd = 0.1
		"merchant": spd = 0.7
		"infantry", "recruit", "laborer": spd = 0.6
		"archer": spd = 0.8
		"cavalry": spd = 0.3
	
	# Nervous System Check (Brain/Spine)
	var mobility_mult = 1.0
	if u.body.has("spine"):
		var s_hp = 0
		for t in u.body["spine"]["tissues"]: s_hp += t["hp"]
		if s_hp <= 0: mobility_mult = 0.05 # Paralyzed
	if u.body.has("brain"):
		var b_hp = 0
		for t in u.body["brain"]["tissues"]: b_hp += t["hp"]
		if b_hp <= 0: mobility_mult = 0.0 # Braindead
		
	# Limb-based Mobility Multipliers
	var leg_penalty = 1.0
	for side in ["l", "r"]:
		var lp = side + "_leg"
		var fp = side + "_foot"
		var limb_ok = true
		if u.body.has(lp):
			for t in u.body[lp]["tissues"]:
				if t["hp"] <= 0 and (t["type"] == "tendon" or t["type"] == "nerve" or t["type"] == "bone"):
					limb_ok = false; break
		if limb_ok and u.body.has(fp):
			for t in u.body[fp]["tissues"]:
				if t["hp"] <= 0 and (t["type"] == "tendon" or t["type"] == "nerve" or t["type"] == "bone"):
					limb_ok = false; break
		
		if not limb_ok:
			leg_penalty *= 0.5 # Losing a leg/foot halves speed
			
	if leg_penalty < 1.0:
		mobility_mult *= leg_penalty
	
	if u.status.get("is_prone", false):
		mobility_mult *= 0.2 # Prone units crawl very slowly
	
	# Agility impact: 10 is baseline.
	var agility = u.attributes.get("agility", 10)
	var agility_mod = 10.0 / float(max(1, agility))
	
	# Armor weight impact:
	var weight = get_unit_equipment_weight(u)
	var armor_handling = u.skills.get("armor_handling", 0)
	var effective_weight = max(0, weight * (1.0 - (float(armor_handling) / 200.0)))
	var weight_penalty = effective_weight * 0.02
	
	var final_speed = (spd * agility_mod) + weight_penalty
	
	if mobility_mult < 1.0:
		final_speed = final_speed / max(0.001, mobility_mult)

	# Clamp to reasonable values
	return clamp(final_speed, 0.05, 5.0)

static func apply_armor_to_recruit(recruit: GDUnit, item: Dictionary):
	var layer = item["layer"]
	for slot in item["coverage"]:
		if recruit.equipment.has(slot):
			recruit.equipment[slot][layer] = item

func process_bleeding(u: GDUnit, delta: float, rng: RandomNumberGenerator) -> Dictionary:
	var res = {"died": false, "downed": false, "msg": ""}
	
	# Internal Bleeding (Pressure)
	for p_key in u.body:
		var part = u.body[p_key]
		if part.has("internal_bleeding") and part["internal_bleeding"] > 0:
			u.blood_current -= part["internal_bleeding"] * 0.1 * delta
			# High pressure leads to shock
			if part["internal_bleeding"] > 50 and rng.randf() < 0.1 * delta:
				if not u.status["is_downed"]:
					res["downed"] = true
					u.status["is_downed"] = true
					res["msg"] = "collapses from internal pressure in " + part.name
			# Natural drainage/absorption
			part["internal_bleeding"] = max(0, part["internal_bleeding"] - 0.2 * delta)

	if u.bleed_rate > 0:
		var loss = u.bleed_rate * delta
		u.blood_current = max(0, u.blood_current - loss)
		
		# Natural Coagulation
		var coag_chance = 0.05 if u.bleed_rate < 40 else 0.005
		if rng.randf() < coag_chance * delta * 10:
			u.bleed_rate = max(0, u.bleed_rate - 1.0)
			for p_key in u.body:
				var part = u.body[p_key]
				if part.get("bleed_rate", 0.0) > 0:
					part["bleed_rate"] = max(0, part["bleed_rate"] - 0.5) 

		# Shock Thresholds
		var blood_pct = u.blood_current / u.blood_max
		if blood_pct < 0.3:
			u.status["is_dead"] = true
			res["died"] = true
			res["msg"] = "%s has bled to death!" % u.name
		elif blood_pct < 0.5 and not u.status["is_downed"]:
			u.status["is_downed"] = true
			u.status["is_prone"] = true
			res["downed"] = true
			res["msg"] = "%s collapses into hypovolemic shock!" % u.name
		elif blood_pct < 0.75:
			# Dizziness / Speed penalty
			u.speed = u.base_speed * 1.5
	
	if u.blood_current <= 0 and not res["died"]:
		res["died"] = true
		u.status["is_dead"] = true
		res["msg"] = "has bled out"
		
	return res

func generate_unit(a_key: String, tier: int = 1) -> GDUnit:
	var rng = GameState.rng
	var r_name = "%s %s" % [
		FIRST_NAMES[rng.randi() % FIRST_NAMES.size()],
		LAST_NAMES[rng.randi() % LAST_NAMES.size()]
	]
	
	var body = get_default_body(1.0)
	var total_hp = get_total_hp(body)
	
	var u = GDUnit.new(r_name)
	u.tier = tier
	u.hp_max = total_hp
	u.hp = total_hp
	u.blood_max = 500.0
	u.blood_current = 500.0
	u.bleed_rate = 0.0
	u.status = {
		"is_downed": false,
		"is_dead": false,
		"is_prone": false
	}
	u.body = body
	u.cost = 50 
	
	# Archetype
	if a_key == "recruit": a_key = "spearman" 
	
	if not ARCHETYPES.has(a_key):
		a_key = "spearman"
	
	var archetype = ARCHETYPES[a_key]
	u.archetype = a_key
	u.type = a_key 
	
	# Apply Attributes and Skills
	for attr in u.attributes:
		if archetype.has("attributes") and archetype["attributes"].has(attr):
			u.attributes[attr] = archetype["attributes"][attr]
	for sk in u.skills:
		if archetype.has("skills") and archetype["skills"].has(sk):
			u.skills[sk] = archetype["skills"][sk]
			
	# Equipment
	var material = MATERIAL_TIERS.get(tier, "iron")
	for slot_key in archetype["equipment"]:
		var item_info = archetype["equipment"][slot_key]
		var i_type = item_info[0]
		var i_mat = item_info[1]
		if i_mat == "tier_mat": i_mat = material
		
		var item = create_item_data(i_type, i_mat)
		if item:
			if slot_key in ["main_hand", "off_hand"]:
				u.equipment[slot_key] = item
			else:
				var parts = slot_key.split("_")
				if parts.size() >= 2:
					var part_group = parts[0]
					var target_layer = parts[1]
					var target_slots = []
					match part_group:
						"head": target_slots = ["head"]
						"torso": target_slots = ["torso"]
						"arms": target_slots = ["l_arm", "r_arm"]
						"legs": target_slots = ["l_leg", "r_leg"]
						"hands": target_slots = ["l_hand", "r_hand"]
						"feet": target_slots = ["l_foot", "r_foot"]
					
					for slot in target_slots:
						if not u.equipment.has(slot): u.equipment[slot] = {}
						u.equipment[slot][target_layer] = item
				
	return u
