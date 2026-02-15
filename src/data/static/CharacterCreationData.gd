class_name CharacterCreationData
extends RefCounted

# Character creation: scenarios, professions, and traits (CDDA + Kenshi style)
# Extracted from GameData.gd for better modularity

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

static func get_scenarios() -> Dictionary:
	return SCENARIOS

static func get_scenario(scenario_id: String) -> Dictionary:
	return SCENARIOS.get(scenario_id, {})

static func get_professions() -> Dictionary:
	return PROFESSIONS

static func get_profession(profession_id: String) -> Dictionary:
	return PROFESSIONS.get(profession_id, {})

static func get_traits() -> Dictionary:
	return TRAITS

static func get_trait(trait_id: String) -> Dictionary:
	return TRAITS.get(trait_id, {})
