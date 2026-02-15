class_name ItemData
extends RefCounted

# Weapons, armor, shields, ammunition, and transport
# Extracted from GameData.gd for better modularity

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
	},
	"hand_axe": {
		"type": "weapon", "dmg": 10, "dmg_type": "cut", "contact": 8, "penetration": 20, "hands": 1, "volume": 1.5, "weight": 1.2, "name": "Hand Axe", "material": "iron",
		"attacks": [
			{"name": "Chop", "dmg_mult": 1.0, "dmg_type": "cut", "contact": 8, "penetration": 20}
		]
	},
	"warhammer": {
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
	
	# --- SHIELDS ---
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

static func get_items() -> Dictionary:
	return ITEMS

static func get_item(item_id: String) -> Dictionary:
	return ITEMS.get(item_id, {})

static func get_base_prices() -> Dictionary:
	return BASE_PRICES

static func get_price(item_id: String) -> int:
	return BASE_PRICES.get(item_id, 0)
