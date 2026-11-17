class_name UnitData
extends RefCounted

# Unit archetypes, templates, and combat data
# Extracted from GameData.gd for better modularity

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

static func get_archetypes() -> Dictionary:
	return ARCHETYPES

static func get_archetype(archetype_id: String) -> Dictionary:
	return ARCHETYPES.get(archetype_id, {})
