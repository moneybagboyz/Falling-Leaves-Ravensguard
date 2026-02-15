extends Node

# Static data facade - delegates to modular data files
# Refactored from 2393-line monolith into focused modules

const MaterialsData = preload("res://src/data/MaterialsData.gd")
const NamesData = preload("res://src/data/NamesData.gd")
const AIConfigData = preload("res://src/data/AIConfigData.gd")

# NEW: Modular static data files
const SiegeData = preload("res://src/data/static/SiegeData.gd")
const ItemData = preload("res://src/data/static/ItemData.gd")
const BuildingData = preload("res://src/data/static/BuildingData.gd")
const UnitData = preload("res://src/data/static/UnitData.gd")
const CharacterCreationData = preload("res://src/data/static/CharacterCreationData.gd")

# --- MATERIALS (Dwarf Fortress Depth) ---
# Loaded from data/materials.json
static var MATERIALS: Dictionary:
	get:
		return MaterialsData.get_materials()

# --- SIEGE ENGINES & EQUIPMENT ---
# Delegated to SiegeData module
static var SIEGE_ENGINES: Dictionary:
	get:
		return SiegeData.get_siege_engines()

# --- ITEMS ---
# Delegated to ItemData module
static var ITEMS: Dictionary:
	get:
		return ItemData.get_items()

static var BASE_PRICES: Dictionary:
	get:
		return ItemData.get_base_prices()

# --- SETTLEMENTS & BUILDINGS ---
# Delegated to BuildingData module
static var BUILDINGS: Dictionary:
	get:
		return BuildingData.get_buildings()

static var GEOLOGY_RESOURCES: Dictionary:
	get:
		return BuildingData.get_geology_resources()

# --- CHARACTER CREATION DATA (CDDA + KENSHI STYLE) ---
# Delegated to CharacterCreationData module
static var SCENARIOS: Dictionary:
	get:
		return CharacterCreationData.get_scenarios()

static var PROFESSIONS: Dictionary:
	get:
		return CharacterCreationData.get_professions()

static var TRAITS: Dictionary:
	get:
		return CharacterCreationData.get_traits()

# --- UNIT ARCHETYPES ---
# Delegated to UnitData module
static var ARCHETYPES: Dictionary:
	get:
		return UnitData.get_archetypes()

# Legacy data removed - now in dedicated modules:
# - _LEGACY_ITEMS (1000+ lines) -> ItemData.gd
# - _LEGACY_BUILDINGS (500+ lines) -> BuildingData.gd
# - _LEGACY_SCENARIOS/PROFESSIONS/TRAITS (300+ lines) -> CharacterCreationData.gd
# - _LEGACY_ARCHETYPES (200+ lines) -> UnitData.gd

# --- COMBAT & UNIT FUNCTIONS (Keep here for now - will extract to CombatSystem later) ---

static func get_weapon_skill_tag(weapon: Dictionary) -> String:
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
	if attacks.is_empty():
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

# --- AI CONFIGURATION ---
# Loaded from data/ai_config.json
static var GOVERNOR_PERSONALITIES: Array:
	get:
		return AIConfigData.get_governor_personalities()

static var LORD_DOCTRINES: Array:
	get:
		return AIConfigData.get_lord_doctrines()

static var MATERIAL_TIERS: Dictionary:
	get:
		return AIConfigData.get_material_tiers()

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

# --- BODY & UNIT GENERATION ---

static func get_default_body(hp_scale: float = 1.0) -> Dictionary:
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

static func get_total_hp(body: Dictionary) -> int:
	var total = 0
	for p in body:
		for tissue in body[p]["tissues"]:
			total += tissue["hp"]
	return total

static func generate_laborer(rng: RandomNumberGenerator) -> GDUnit:
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

static func generate_recruit(rng: RandomNumberGenerator, tier: int) -> GDUnit:
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
	
	if valid_archetypes.is_empty():
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

static func is_valid_material(item_id: String, mat_key: String) -> bool:
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

static func get_valid_material(type_key: String, mat_key: String) -> String:
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

static func create_item_data(id: String, mat: String, qual: String = "common") -> Dictionary:
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

static func get_item_value(item: Dictionary) -> int:
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

static func calculate_unit_speed(u: GDUnit) -> float:
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

static func generate_unit(a_key: String, tier: int = 1) -> GDUnit:
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
